using System.Runtime.InteropServices;

// Capability experiment only. No ExplorerBrowser, visible parent, input, or user files.
internal static class StandaloneTreeProbe
{
    internal static int RunCore()
    {
        var foreground = GetForegroundWindow();
        var parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden tree core",
            0x80000000, 0, 0, 320, 480, 0, 0, 0, 0);
        if (parent == 0) throw new Exception("Hidden parent creation failed.");
        ExplorerDirs.App.NativeShellTree? tree = null;
        nint treeWindow = 0;
        var notices = 0;
        var callbacksInNotice = 0;
        var readableAfterNotice = 0;
        var unexpectedUserCause = false;
        SubclassProc observer = (hwnd, message, wParam, lParam, id, data) =>
        {
            var before = tree?.SelectionRevision ?? 0;
            var selectionNotice = false;
            if (message == 0x4e && lParam != 0)
            {
                var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
                if (header.Code is -451 or -402 && IsChild(hwnd, header.From))
                {
                    selectionNotice = true;
                    notices++;
                    var action = Marshal.ReadInt32(lParam, Marshal.SizeOf<NotifyHeader>());
                    unexpectedUserCause |= action is 1 or 2;
                }
            }
            var result = DefSubclassProc(hwnd, message, wParam, lParam);
            if (selectionNotice && tree?.SelectionRevision > before) callbacksInNotice++;
            if (selectionNotice && tree?.SelectedDirectory is not null) readableAfterNotice++;
            return result;
        };
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
            var child = Directory.CreateDirectory(Path.Combine(fixture, "child")).FullName;
            var deep = Directory.CreateDirectory(Path.Combine(child, "nested", "deep")).FullName;
            var other = Directory.CreateDirectory(Path.Combine(fixture, "other")).FullName;
            var file = Path.Combine(fixture, "not-a-folder.txt");
            File.WriteAllText(file, "isolated fixture");
            tree = new(parent, 320, 480);
            treeWindow = tree.WindowHandle;
            if (!SetWindowSubclass(treeWindow, observer, 42, 0))
                throw new Exception("Tree notification observation failed.");
            if (FindWindowExW(treeWindow, 0, "SysTreeView32", null) == 0)
                throw new Exception("Expected tree focus child is missing.");
            var requests = 0;
            tree.SelectionChanged += _ => requests++;
            using var synchronization = tree.BeginSynchronization();
            tree.SetRoot(fixture);
            Pump();
            var selected = tree.SelectDirectory(child);
            Pump(); // Native expansion may materialize descendants asynchronously.
            if (!selected) selected = tree.SelectDirectory(child);
            if (!selected) throw new Exception("Production tree did not select child.");
            Pump();
            if (!string.Equals(tree.SelectedDirectory, child, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Production tree selected path mismatch.");
            if (requests != 0 || tree.SelectionRevision == 0)
                throw new Exception("Programmatic selection was not observed and suppressed.");
            Console.WriteLine($"core: selected-child=True; programmatic-requests={requests}; selection-revision={tree.SelectionRevision}");
            var cancellationChecks = 0;
            if (tree.SelectDirectory(deep, () => ++cancellationChecks < 4) ||
                tree.SelectedDirectory != child)
                throw new Exception("Cancelled descendant sync changed the selection.");
            tree.SetRoot(other, () => false);
            if (tree.SelectedDirectory != child)
                throw new Exception("Cancelled root replacement changed the tree.");
            Console.WriteLine("core: cancelled-selection-and-root-preserved=True");
            selected = false;
            for (var attempt = 0; attempt < 6 && !selected; attempt++)
            {
                selected = tree.SelectDirectory(deep);
                Pump();
            }
            if (!selected || tree.SelectedDirectory != deep || requests != 0)
                throw new Exception("Deep descendant synchronization failed.");
            Console.WriteLine("core: collapsed-deep-descendant=True; delayed-navigation-suppressed=True");
            if (tree.SelectDirectory("relative") || tree.SelectDirectory(file))
                throw new Exception("Non-directory navigation target accepted.");
            tree.SetRoot(other);
            if (!tree.SelectDirectory(other)) throw new Exception("Replacement root could not be selected.");
            Pump();
            if (tree.SelectedDirectory != other || requests != 0)
                throw new Exception("Root replacement selected stale path or requested navigation.");
            if (tree.SelectDirectory(deep) || tree.SelectedDirectory != other)
                throw new Exception("Outside-root selection changed the tree.");

            tree.SetRoot(fixture);
            Pump();
            if (!tree.SelectDirectory(fixture)) throw new Exception("Could not select the isolated tree root for key routing.");
            Pump();
            var treeControl = FindWindowExW(treeWindow, 0, "SysTreeView32", null);
            var caret = SendMessageW(treeControl, 0x110a, 9, 0); // TVGN_CARET
            if (treeControl == 0 || caret == 0) throw new Exception("The isolated tree root was not selected before key routing.");
            var priorFocus = GetFocus();
            SetFocus(treeControl);
            var nativeTreeFocused = GetFocus() == treeControl;
            SendMessageW(treeControl, 0x1102, 1, caret); // TVE_COLLAPSE
            PostMessageW(treeControl, 0x100, 0x6b, 0); // WM_KEYDOWN / VK_ADD
            PostMessageW(treeControl, 0x102, 0x2b, 0); // WM_CHAR / '+' from the numpad key translation
            PostMessageW(treeControl, 0x101, 0x6b, 0); // WM_KEYUP / VK_ADD
            Pump();
            var expandedByPlus = (ReadTreeItemState(treeControl, caret) & 0x20) != 0; // TVIS_EXPANDED
            var firstChild = SendMessageW(treeControl, 0x110a, 4, caret); // TVGN_CHILD
            if (firstChild == 0) throw new Exception("Expanded fixture root did not materialize child directories.");
            PostMessageW(treeControl, 0x100, 0x6a, 0); // WM_KEYDOWN / VK_MULTIPLY
            PostMessageW(treeControl, 0x102, 0x2a, 0); // WM_CHAR / '*'
            PostMessageW(treeControl, 0x101, 0x6a, 0); // WM_KEYUP / VK_MULTIPLY
            Pump();
            var expandedByMultiply = (ReadTreeItemState(treeControl, firstChild) & 0x20) != 0;
            var grandchild = SendMessageW(treeControl, 0x110a, 4, firstChild);
            var deepExpandedByMultiply = grandchild != 0 && (ReadTreeItemState(treeControl, grandchild) & 0x20) != 0;
            PostMessageW(treeControl, 0x100, 0x6d, 0); // WM_KEYDOWN / VK_SUBTRACT
            PostMessageW(treeControl, 0x102, 0x2d, 0); // WM_CHAR / '-'
            PostMessageW(treeControl, 0x101, 0x6d, 0); // WM_KEYUP / VK_SUBTRACT
            Pump();
            var collapsedByMinus = (ReadTreeItemState(treeControl, caret) & 0x20) == 0;
            Console.WriteLine($"native-tree-numpad focus-acquired={nativeTreeFocused}; plus-expands={expandedByPlus}; multiply-expands-child={expandedByMultiply}; multiply-expands-deep={deepExpandedByMultiply}; minus-collapses={collapsedByMinus}; message-target=SysTreeView32; desktop-input=False");
            if (priorFocus != 0 && IsWindow(priorFocus)) SetFocus(priorFocus);
            if (!expandedByPlus || !expandedByMultiply || !deepExpandedByMultiply || !collapsedByMinus)
                throw new Exception("Native tree did not apply the numpad expand/collapse messages.");

            var wrongThreadRejected = Task.Run(() =>
            {
                try { _ = tree.SelectedDirectory; return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            if (!wrongThreadRejected) throw new Exception("Cross-thread COM access was accepted.");
            var mtaRejected = Task.Run(() =>
            {
                try { using var invalid = new ExplorerDirs.App.NativeShellTree(parent, 320, 480); return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            if (!mtaRejected) throw new Exception("MTA construction was accepted.");
            Console.WriteLine("core: replacement-root=True; non-directory-rejected=True");
            Console.WriteLine("core: outside-root-rejected=True; wrong-thread-rejected=True; mta-rejected=True");
            Console.WriteLine($"core: native-selection-notices={notices}; callbacks-in-notice={callbacksInNotice}; unexpected-user-cause={unexpectedUserCause}");
            if (notices == 0 || readableAfterNotice == 0 || unexpectedUserCause)
                throw new Exception("Native selection notification source/timing did not match the host contract.");
            Console.WriteLine($"core: readable-after-notice={readableAfterNotice}; programmatic-cause-only=True");
            RemoveWindowSubclass(treeWindow, observer, 42);
            tree.Dispose();
            tree.Dispose(); // Repeated cleanup must not release the COM reference twice.
            try { _ = tree.SelectedDirectory; throw new Exception("Disposed tree remained readable."); }
            catch (ObjectDisposedException) { }
            Console.WriteLine("core: repeated-dispose=True; disposed-access-rejected=True");
        }
        finally
        {
            if (treeWindow != 0 && IsWindow(treeWindow)) RemoveWindowSubclass(treeWindow, observer, 42);
            tree?.Dispose();
            DestroyWindow(parent);
            GC.KeepAlive(observer);
        }
        if (IsWindow(treeWindow) || IsWindow(parent) || GetForegroundWindow() != foreground)
            throw new Exception("Core probe did not restore its hidden window boundary.");
        Console.WriteLine("core: parent-and-tree-destroyed=True; foreground-unchanged=True");
        return 0;
    }

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var classId = new Guid("AE054212-3535-4430-83ED-D501AA6680E6");
        var treeId = new Guid("028212A3-B627-47E9-8856-C14265554E4F");
        var itemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        nint tree = 0, parent = 0, rootItem = 0, childItem = 0;
        var events = new TreeProbeEvents();
        uint cookie = 0;
        var advised = false;
        List<nint> childWindows = [];
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        var child = Directory.CreateDirectory(Path.Combine(fixture, "child")).FullName;
        File.WriteAllText(Path.Combine(fixture, "not-a-folder.txt"), "isolated fixture");
        try
        {
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden standalone tree",
                0x80000000, 0, 0, 320, 480, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(CoCreateInstance(ref classId, 0, 1, ref treeId, out tree));
            var bounds = new Rect { Right = 320, Bottom = 480 };
            // Expandos, root expando, persistent selection display, no label edits,
            // no drag/drop, no ordering stream. The parent remains hidden.
            Check(Method<Initialize>(tree, 3)(tree, parent, ref bounds, 0x1 | 0x40 | 0x80 | 0x10000 | 0x1000 | 0x2000));
            var oleWindowId = new Guid("00000114-0000-0000-C000-000000000046");
            Check(Marshal.QueryInterface(tree, in oleWindowId, out var oleWindow));
            try
            {
                Check(Method<GetObject>(oleWindow, 3)(oleWindow, out var treeWindow));
                if (treeWindow == 0 || !IsChild(parent, treeWindow))
                    throw new Exception("Native tree HWND is not owned by the isolated parent.");
                Console.WriteLine("ole-window-child=True");
            }
            finally { Marshal.Release(oleWindow); }
            Check(Method<Advise>(tree, 4)(tree, events, out cookie));
            advised = true;
            Check(SHCreateItemFromParsingName(fixture, 0, ref itemId, out rootItem));
            Check(SHCreateItemFromParsingName(child, 0, ref itemId, out childItem));
            Check(Method<AppendRoot>(tree, 6)(tree, rootItem, 0x20, 2, 0)); // folders only, expanded root
            Pump();
            VerifyArray(tree, 10, fixture, "roots");
            Check(Method<SetState>(tree, 11)(tree, childItem, 1, 1)); // select child, no keyboard input
            Pump();
            VerifyArray(tree, 13, child, "selected-child");
            var beforeUnadvise = events.SelectionChanges;
            if (beforeUnadvise == 0) throw new Exception("Tree selection notification was not observed.");
            Console.WriteLine($"selection-callbacks={beforeUnadvise}; added-callbacks={events.Added}");
            Check(Method<Unadvise>(tree, 5)(tree, cookie));
            advised = false;
            Check(Method<SetState>(tree, 11)(tree, rootItem, 1, 1));
            Pump();
            VerifyArray(tree, 13, fixture, "selected-root-after-unadvise");
            if (events.SelectionChanges != beforeUnadvise)
                throw new Exception("Tree callback continued after unadvise.");
            Check(Method<NoArgs>(tree, 9)(tree)); // RemoveAllRoots, not a filesystem deletion
            EnumChildWindows(parent, (window, _) => { childWindows.Add(window); return true; }, 0);
            if (childWindows.Count == 0) throw new Exception("Native tree did not create a child window.");
            Console.WriteLine("unadvise-stopped-callbacks=True; roots-removed=True");
            Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Hidden probe changed desktop state.");
        }
        finally
        {
            if (advised) Method<Unadvise>(tree, 5)(tree, cookie);
            if (childItem != 0) Marshal.Release(childItem);
            if (rootItem != 0) Marshal.Release(rootItem);
            if (tree != 0) Marshal.Release(tree);
            if (parent != 0) DestroyWindow(parent);
            GC.KeepAlive(events);
        }
        if (IsWindow(parent) || childWindows.Any(IsWindow))
            throw new Exception("Native tree window survived parent teardown.");
        if (GetForegroundWindow() != foreground)
            throw new Exception("Teardown changed foreground.");
        Console.WriteLine($"destroyed-child-windows={childWindows.Count}; parent-destroyed=True");
        return 0;
    }

    private static void VerifyArray(nint tree, int slot, string expected, string stage)
    {
        nint array = 0, item = 0, name = 0;
        try
        {
            Check(Method<GetObject>(tree, slot)(tree, out array));
            Check(Method<Count>(array, 7)(array, out var count));
            if (count != 1) throw new Exception(stage + ": expected exactly one Shell item.");
            Check(Method<ItemAt>(array, 8)(array, 0, out item));
            Check(Method<DisplayName>(item, 5)(item, 0x80058000, out name));
            var match = string.Equals(Marshal.PtrToStringUni(name), expected, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"{stage}: count={count}; path-match={match}");
            if (!match) throw new Exception(stage + ": unexpected Shell item.");
        }
        finally
        {
            if (name != 0) Marshal.FreeCoTaskMem(name);
            if (item != 0) Marshal.Release(item);
            if (array != 0) Marshal.Release(array);
        }
    }
    private static void Pump()
    {
        var until = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void Check(int hr)
    { if (hr != 0) throw new COMException("Unexpected native result.", hr); }
    private static T Method<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    private static uint ReadTreeItemState(nint control, nint item)
    {
        var value = new TreeItem { Mask = 8, Item = item, StateMask = 0x20 };
        if (SendMessageW(control, 0x110c, 0, ref value) == 0) throw new Exception("Could not read native tree item state.");
        return value.State;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct TreeItem
    {
        public uint Mask; public nint Item; public uint State, StateMask; public nint Text;
        public int TextLength, Image, SelectedImage, Children; public nint Parameter;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint From; public nuint Id; public int Code; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref TreeItem value);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref Rect rect, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Advise(nint self, [MarshalAs(UnmanagedType.IUnknown)] object events, out uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Unadvise(nint self, uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int AppendRoot(nint self, nint root, uint enumFlags, uint rootFlags, nint filter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetState(nint self, nint item, uint mask, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetObject(nint self, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Count(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DisplayName(nint self, uint kind, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NoArgs(nint self);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, out nint item);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    private delegate bool EnumChildCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildCallback callback, nint parameter);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TreeProbeEvents : ITreeProbeEvents
{
    internal int SelectionChanges, Added;
    public int OnItemClick(nint item, uint hit, uint click) => 1;
    public int OnPropertyItemCommit(nint item) => 0;
    public int OnItemStateChanging(nint item, uint mask, uint state) => 0;
    public int OnItemStateChanged(nint item, uint mask, uint state) => 0;
    public int OnSelectionChanged(nint items) { SelectionChanges++; return 0; }
    public int OnKeyboardInput(uint message, nint wParam, nint lParam) => 1;
    public int OnBeforeExpand(nint item) => 0;
    public int OnAfterExpand(nint item) => 0;
    public int OnBeginLabelEdit(nint item) => 1;
    public int OnEndLabelEdit(nint item) => 0;
    public int OnGetToolTip(nint item, nint text, int capacity) => unchecked((int)0x80004001);
    public int OnBeforeItemDelete(nint item) => 0;
    public int OnItemAdded(nint item, int root) { Added++; return 0; }
    public int OnItemDeleted(nint item, int root) => 0;
    public int OnBeforeContextMenu(nint item, ref Guid iid, out nint menu) { menu = 0; return unchecked((int)0x80004001); }
    public int OnAfterContextMenu(nint item, nint input, ref Guid iid, out nint menu) { menu = 0; return unchecked((int)0x80004001); }
    public int OnBeforeStateImageChange(nint item) => 0;
    public int OnGetDefaultIconIndex(nint item, out int normal, out int open)
    {
        normal = open = -1;
        return unchecked((int)0x80004001); // E_NOTIMPL lets NamespaceTreeControl resolve the Shell icon.
    }
}

[ComVisible(true), Guid("93D77985-B3D8-4484-8318-672CDDA002CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITreeProbeEvents
{
    [PreserveSig] int OnItemClick(nint item, uint hit, uint click);
    [PreserveSig] int OnPropertyItemCommit(nint item);
    [PreserveSig] int OnItemStateChanging(nint item, uint mask, uint state);
    [PreserveSig] int OnItemStateChanged(nint item, uint mask, uint state);
    [PreserveSig] int OnSelectionChanged(nint items);
    [PreserveSig] int OnKeyboardInput(uint message, nint wParam, nint lParam);
    [PreserveSig] int OnBeforeExpand(nint item);
    [PreserveSig] int OnAfterExpand(nint item);
    [PreserveSig] int OnBeginLabelEdit(nint item);
    [PreserveSig] int OnEndLabelEdit(nint item);
    [PreserveSig] int OnGetToolTip(nint item, nint text, int capacity);
    [PreserveSig] int OnBeforeItemDelete(nint item);
    [PreserveSig] int OnItemAdded(nint item, int root);
    [PreserveSig] int OnItemDeleted(nint item, int root);
    [PreserveSig] int OnBeforeContextMenu(nint item, ref Guid iid, out nint menu);
    [PreserveSig] int OnAfterContextMenu(nint item, nint input, ref Guid iid, out nint menu);
    [PreserveSig] int OnBeforeStateImageChange(nint item);
    [PreserveSig] int OnGetDefaultIconIndex(nint item, out int normal, out int open);
}
