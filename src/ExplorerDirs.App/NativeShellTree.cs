using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// STA-owned native tree. The caller owns the parent HWND and destroys it after Dispose.
// This object never navigates an ExplorerBrowser or changes foreground/focus.
internal sealed class NativeShellTree : IDisposable
{
    private static readonly Guid ItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeTreeEvents _events;
    private nint _tree;
    private uint _cookie;
    private bool _advised;
    private nint _root;
    private int _synchronizing;
    internal nint WindowHandle { get; private set; }
    internal long SelectionRevision { get; private set; }
    internal event Action<string?>? SelectionChanged;
    internal event Action? UserInput;
    internal event Action<string>? FolderMiddleClicked;

    // The host keeps this scope across its bounded, cancellable dispatcher retries.
    // Native expansion/selection notifications can arrive after a COM call returns.
    internal IDisposable BeginSynchronization()
    {
        VerifyThread();
        _synchronizing++;
        return new Synchronization(this);
    }
    private sealed class Synchronization(NativeShellTree owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (owner._thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("目录树同步必须由创建它的线程结束。");
            _disposed = true;
            owner._synchronizing--;
        }
    }

    internal NativeShellTree(nint parent, int width, int height)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("目录树必须在 STA 线程创建。");
        _events = new NativeTreeEvents(OnSelectionChanged, () => UserInput?.Invoke(), OnItemClick);
        var clsid = new Guid("AE054212-3535-4430-83ED-D501AA6680E6");
        var iid = new Guid("028212A3-B627-47E9-8856-C14265554E4F");
        try
        {
            Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out _tree));
            var bounds = new Rect { Right = Math.Max(1, width), Bottom = Math.Max(1, height) };
            // No label edits or drag/drop until those file-operation paths are accepted.
            // NOORDERSTREAM avoids native ordering persistence outside portable settings.
            Check(Method<Initialize>(3)(_tree, parent, ref bounds, 0x1 | 0x40 | 0x80 | 0x1000 | 0x2000 | 0x10000));
            var windowId = new Guid("00000114-0000-0000-C000-000000000046");
            Check(Marshal.QueryInterface(_tree, in windowId, out var window));
            try
            {
                Check(At<GetObject>(window, 3)(window, out var hwnd));
                if (hwnd == 0 || !IsChild(parent, hwnd)) throw new InvalidOperationException("目录树窗口不属于当前宿主。");
                WindowHandle = hwnd;
            }
            finally { Marshal.Release(window); }
            Check(Method<Advise>(4)(_tree, _events, out _cookie));
            _advised = true;
        }
        catch { Dispose(); throw; }
    }

    internal void SetRoot(string parsingName, Func<bool>? isCurrent = null)
    {
        VerifyThread();
        var item = CreateItem(parsingName);
        _synchronizing++;
        try
        {
            bool Current() => _tree != 0 && (isCurrent?.Invoke() ?? true);
            if (!Current()) return;
            // An empty native tree returns E_INVALIDARG from RemoveAllRoots.
            if (_root != 0)
            {
                Check(Method<NoArgs>(9)(_tree));
                if (_root != 0) Marshal.Release(_root);
                _root = 0;
            }
            if (!Current()) return;
            Check(Method<AppendRoot>(6)(_tree, item, 0x20, 0, 0)); // folders only; initially collapsed
            if (_tree == 0) return;
            Marshal.AddRef(item);
            _root = item;
        }
        finally { _synchronizing--; Marshal.Release(item); }
    }

    // False can mean native descendants are not yet materialized. The UI host must
    // retry later with a bounded lifetime; never pump messages or wait inside here.
    internal bool SelectDirectory(string path, Func<bool>? isCurrent = null)
    {
        VerifyThread();
        if (!Path.IsPathFullyQualified(path)) return false;
        return SelectLocation(path, isCurrent);
    }

    // Address-bar trees can contain virtual Shell folders as well as filesystem
    // directories. Validate through Shell before expanding/selecting any target.
    internal bool SelectLocation(string identity, Func<bool>? isCurrent = null)
    {
        VerifyThread();
        if (!ShellLocationIdentity.TryGetFolderPidl(identity, out var pidl)) return false;
        Marshal.FreeCoTaskMem(pidl);
        var item = CreateItem(identity);
        _synchronizing++;
        try
        {
            bool Current() => _tree != 0 && (isCurrent?.Invoke() ?? true);
            if (!Current()) return false;
            // GetAttributes returns S_FALSE for a valid subset of the requested flags
            // (ordinary folders lack STREAM). Only a negative HRESULT is a failure.
            if (At<GetAttributes>(item, 6)(item, 0x20000000, out var attributes) < 0 ||
                (attributes & 0x20000000) == 0) return false;
            if (!Current() || !ExpandAncestors(item, Current) || !Current()) return false;
            if (Method<OneItem>(16)(_tree, item) != 0) return false; // EnsureItemVisible
            if (!Current()) return false;
            return Method<SetState>(11)(_tree, item, 1, 1) == 0;
        }
        finally { _synchronizing--; Marshal.Release(item); }
    }

    private bool ExpandAncestors(nint item, Func<bool> isCurrent)
    {
        // EnsureItemVisible does not materialize descendants of collapsed nodes.
        // Compare Shell identities before changing the tree, then expand root-first.
        if (_root == 0) return false;
        var parents = new List<nint>();
        var current = item;
        try
        {
            while (parents.Count < 256)
            {
                if (!isCurrent()) return false;
                if (At<Compare>(current, 7)(current, _root, 0, out var order) == 0 && order == 0)
                {
                    for (var index = parents.Count - 1; index >= 0; index--)
                        if (!isCurrent() || Method<SetState>(11)(_tree, parents[index], 2, 2) != 0) return false;
                    return true;
                }
                var hr = At<GetObject>(current, 4)(current, out var parent);
                if (hr != 0 || parent == 0)
                {
                    if (parent != 0) Marshal.Release(parent);
                    return false;
                }
                parents.Add(parent);
                current = parent;
            }
            return false;
        }
        finally { foreach (var parent in parents) Marshal.Release(parent); }
    }

    internal string? SelectedDirectory
    {
        get
        {
            VerifyThread();
            nint array = 0, item = 0, name = 0;
            try
            {
                if (Method<GetObject>(13)(_tree, out array) != 0 || array == 0 ||
                    At<Count>(array, 7)(array, out var count) != 0 || count != 1 ||
                    At<ItemAt>(array, 8)(array, 0, out item) != 0 || item == 0 ||
                    At<GetAttributes>(item, 6)(item, 0x60400000, out var attributes) < 0 ||
                    (attributes & 0x60400000) != 0x60000000 ||
                    At<DisplayName>(item, 5)(item, 0x80058000, out name) != 0) return null;
                var path = Marshal.PtrToStringUni(name);
                return path is not null && Path.IsPathFullyQualified(path) ? path : null;
            }
            finally
            {
                if (name != 0) Marshal.FreeCoTaskMem(name);
                if (item != 0) Marshal.Release(item);
                if (array != 0) Marshal.Release(array);
            }
        }
    }

    internal string? SelectedFolderIdentity
    {
        get
        {
            VerifyThread();
            nint array = 0, item = 0;
            try
            {
                if (Method<GetObject>(13)(_tree, out array) != 0 || array == 0 ||
                    At<Count>(array, 7)(array, out var count) != 0 || count != 1 ||
                    At<ItemAt>(array, 8)(array, 0, out item) != 0 || item == 0 ||
                    At<GetAttributes>(item, 6)(item, 0x20000000, out var attributes) < 0 ||
                    (attributes & 0x20000000) == 0 ||
                    At<DisplayName>(item, 5)(item, 0x80028000, out var name) != 0 || name == 0)
                    return null;
                try
                {
                    var parsingName = Marshal.PtrToStringUni(name);
                    return parsingName is not null &&
                        ShellTreeFolderLocation.TryNormalizeSelected(parsingName, true,
                            out var identity, out _) && ShellLocationIdentity.TryGetFolderPidl(identity, out var pidl)
                        ? FreePidlAndReturn(pidl, identity) : null;
                }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally
            {
                if (item != 0) Marshal.Release(item);
                if (array != 0) Marshal.Release(array);
            }
        }
    }

    private static string FreePidlAndReturn(nint pidl, string identity)
    {
        Marshal.FreeCoTaskMem(pidl);
        return identity;
    }

    private void OnSelectionChanged()
    {
        var revision = ++SelectionRevision;
        if (_tree == 0 || _synchronizing != 0) return;
        var path = SelectedDirectory;
        if (_tree != 0 && _synchronizing == 0 && revision == SelectionRevision)
            SelectionChanged?.Invoke(path);
    }
    private bool OnItemClick(nint item, uint hit, uint click)
    {
        if (_tree == 0 || item == 0 || (hit & 0x0046) == 0 || (click & 0x0003) != 0x0002)
            return false;
        nint name = 0;
        try
        {
            if (At<GetAttributes>(item, 6)(item, ShellItemKind.RequestedAttributes, out var attributes) < 0 ||
                !ShellTreeMiddleClick.ShouldOpenInNewTab(hit, click, attributes) ||
                At<DisplayName>(item, 5)(item, 0x80058000, out name) != 0) return false;
            var path = Marshal.PtrToStringUni(name);
            if (path is null || !Path.IsPathFullyQualified(path)) return false;
            FolderMiddleClicked?.Invoke(path);
            return true;
        }
        catch { return false; } // A stale Shell item must not unwind through the COM event callback.
        finally { if (name != 0) Marshal.FreeCoTaskMem(name); }
    }
    private void VerifyThread()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("目录树必须由创建它的线程访问。");
        ObjectDisposedException.ThrowIf(_tree == 0, this);
    }
    public void Dispose()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("目录树必须由创建它的线程释放。");
        var tree = _tree;
        if (tree == 0) return;
        _tree = 0; // Suppress callbacks during teardown.
        WindowHandle = 0;
        try { if (_advised) At<Unadvise>(tree, 5)(tree, _cookie); }
        finally
        {
            _advised = false;
            if (_root != 0) { Marshal.Release(_root); _root = 0; }
            Marshal.Release(tree);
            GC.KeepAlive(_events);
        }
    }
    private static nint CreateItem(string path)
    {
        var id = ItemId;
        Check(SHCreateItemFromParsingName(path, 0, ref id, out var item));
        return item;
    }
    private static void Check(int hr)
    { if (hr != 0) throw new COMException("原生目录树调用失败。", hr); }
    private T Method<T>(int slot) where T : Delegate => At<T>(_tree, slot);
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref Rect rect, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Advise(nint self, [MarshalAs(UnmanagedType.IUnknown)] object events, out uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Unadvise(nint self, uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int AppendRoot(nint self, nint root, uint enumFlags, uint rootFlags, nint filter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetState(nint self, nint item, uint mask, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneItem(nint self, nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetObject(nint self, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Count(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributes(nint self, uint mask, out uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Compare(nint self, nint other, uint hint, out int order);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DisplayName(nint self, uint kind, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NoArgs(nint self);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, out nint item);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class NativeTreeEvents(Action selectionChanged, Action userInput,
    Func<nint, uint, uint, bool> itemClick) : INativeTreeEvents
{
    public int OnItemClick(nint item, uint hit, uint click)
    {
        NotifyInput(userInput);
        if ((hit & 0x0046) != 0 && (click & 0x0003) == 0x0002)
        {
            try { return itemClick(item, hit, click) ? 0 : unchecked((int)0x80004001); }
            catch (Exception ex) { return Marshal.GetHRForException(ex); }
        }
        return unchecked((int)0x80004001); // Let both namespace and tree view process the click.
    }
    public int OnPropertyItemCommit(nint item) => 0;
    public int OnItemStateChanging(nint item, uint mask, uint state) => 0;
    public int OnItemStateChanged(nint item, uint mask, uint state) => 0;
    public int OnSelectionChanged(nint items)
    {
        // Never unwind a managed event subscriber through the native callback.
        try { selectionChanged(); return 0; }
        catch (Exception ex) { return Marshal.GetHRForException(ex); }
    }
    public int OnKeyboardInput(uint message, nint wParam, nint lParam) => NotifyInput(userInput);
    private static int NotifyInput(Action action)
    {
        try { action(); return 0; } // OnKeyboardInput requires S_OK to process the key.
        catch (Exception ex) { return Marshal.GetHRForException(ex); }
    }
    public int OnBeforeExpand(nint item) => 0;
    public int OnAfterExpand(nint item) => 0;
    public int OnBeginLabelEdit(nint item) => 1;
    public int OnEndLabelEdit(nint item) => 0;
    public int OnGetToolTip(nint item, nint text, int capacity) => unchecked((int)0x80004001);
    public int OnBeforeItemDelete(nint item) => 0;
    public int OnItemAdded(nint item, int root) => 0;
    public int OnItemDeleted(nint item, int root) => 0;
    public int OnBeforeContextMenu(nint item, ref Guid iid, out nint menu) { menu = 0; return unchecked((int)0x80004001); }
    public int OnAfterContextMenu(nint item, nint input, ref Guid iid, out nint menu) { menu = 0; return unchecked((int)0x80004001); }
    public int OnBeforeStateImageChange(nint item) => 0;
    public int OnGetDefaultIconIndex(nint item, out int normal, out int open)
    {
        normal = open = -1;
        if (item == 0 || GetDisplayName(item, out var parsingName) < 0 || parsingName == 0)
            return unchecked((int)0x80004001);
        try
        {
            var path = Marshal.PtrToStringUni(parsingName);
            if (string.IsNullOrWhiteSpace(path)) return unchecked((int)0x80004001);
            const uint systemImageIndex = 0x00004000;
            const uint smallIcon = 0x00000001;
            const uint openIcon = 0x00000002;
            var size = (uint)Marshal.SizeOf<ShellFileInfo>();
            if (SHGetFileInfo(path, 0, out var closedInfo, size, systemImageIndex | smallIcon) == 0 || closedInfo.Index < 0)
                return unchecked((int)0x80004001);
            normal = closedInfo.Index;
            if (SHGetFileInfo(path, 0, out var openInfo, size, systemImageIndex | smallIcon | openIcon) != 0 && openInfo.Index >= 0)
                open = openInfo.Index;
            else
                open = normal;
            return 0;
        }
        catch { return unchecked((int)0x80004001); }
        finally { Marshal.FreeCoTaskMem(parsingName); }
    }

    private static int GetDisplayName(nint item, out nint value) =>
        Marshal.GetDelegateForFunctionPointer<GetDisplayNameNative>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(item), 5 * nint.Size))(item, 0x80028000, out value);

    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplayNameNative(nint self, uint kind, out nint value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint infoSize, uint flags);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ShellFileInfo
{
    public nint Icon;
    public int Index;
    public uint Attributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
}

[ComVisible(true), Guid("93D77985-B3D8-4484-8318-672CDDA002CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INativeTreeEvents
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
