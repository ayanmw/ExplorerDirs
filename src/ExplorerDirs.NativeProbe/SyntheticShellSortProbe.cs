using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ExplorerDirs.App;

internal static class SyntheticShellSortProbe
{
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid SyntheticFolderClassId = new("A17D2A61-4C0B-4B22-8E36-35D92CBC1170");
    private static readonly Guid SizeFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmGetItemTextW = LvmFirst + 115;
    private const uint LvifText = 0x0001;
    private const uint DvaspectContent = 1;
    private const uint TymedHGlobal = 1;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var backingFolder = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SyntheticShellSort");
        if (Directory.Exists(backingFolder))
            throw new InvalidOperationException($"Refusing to reuse an existing synthetic Shell sort fixture directory: {backingFolder}");
        try
        {
            Directory.CreateDirectory(backingFolder);
            File.WriteAllBytes(Path.Combine(backingFolder, "a-large.bin"), Enumerable.Repeat((byte)0xA1, 100).ToArray());
            File.WriteAllBytes(Path.Combine(backingFolder, "two.bin"), [0x02, 0x02]);
            File.WriteAllBytes(Path.Combine(backingFolder, "z-ten.bin"), Enumerable.Repeat((byte)0x10, 10).ToArray());
        }
        catch
        {
            if (Directory.Exists(backingFolder)) Directory.Delete(backingFolder, recursive: true);
            throw;
        }
        var parent = CreateWindowExW(0, "STATIC", "ExplorerDirs synthetic namespace sort probe",
            0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
        if (parent == 0)
        {
            Directory.Delete(backingFolder, recursive: true);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the Private Desktop probe parent.");
        }

        nint module = 0, rootPidl = 0, folder = 0, view = 0, folderView = 0, hostPointer = 0;
        var classKeyPath = $@"Software\Classes\CLSID\{{{SyntheticFolderClassId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{SyntheticFolderClassId:D}}}";
        StreamViewHost? host = null;
        var viewWindowCreated = false;
        var parentDestroyed = false;
        var classRegistrationCreated = false;
        var namespaceRegistrationCreated = false;
        try
        {
            module = NativeLibrary.Load(FindAdapterLibrary());
            Marshal.GetDelegateForFunctionPointer<ResetCounters>(NativeLibrary.GetExport(module, "ResetSyntheticSortCounters"))();
            Check(Marshal.GetDelegateForFunctionPointer<SetBackingPath>(NativeLibrary.GetExport(module, "SetSyntheticSortBackingPath"))(backingFolder));
            RegisterSyntheticFolder(classKeyPath);
            classRegistrationCreated = true;
            RegisterSyntheticNamespace(namespaceKeyPath);
            namespaceRegistrationCreated = true;
            Check(SHParseDisplayName($"::{SyntheticFolderClassId:B}", 0, out rootPidl, 0, out _));
            if (rootPidl == 0) throw new InvalidOperationException("Shell resolved the synthetic namespace without returning an absolute PIDL.");
            Console.WriteLine("synthetic-shell-size-sort: Shell parsing name resolved through temporary Desktop\\NameSpace registration");
            Console.WriteLine("synthetic-shell-size-sort: calling CreateSyntheticSortShellView");
            Check(Marshal.GetDelegateForFunctionPointer<CreateSyntheticView>(NativeLibrary.GetExport(module, "CreateSyntheticSortShellView"))(rootPidl, out folder, out view));
            var classFactoryCreateCount = ReadCounter(module, "GetSyntheticClassFactoryCreateCount");
            Console.WriteLine($"synthetic-shell-size-sort: Shell-bound folder and view returned; registered-class-factory-creates={classFactoryCreateCount}");

            Console.WriteLine("synthetic-shell-size-sort: creating StreamViewHost");
            host = new StreamViewHost(parent, []) { View = view };
            Console.WriteLine("synthetic-shell-size-sort: StreamViewHost created");
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            Console.WriteLine("synthetic-shell-size-sort: browser interface acquired");
            var settings = new StreamProbeSettings { Mode = 4 };
            var bounds = new StreamProbeRect { Right = 900, Bottom = 600 };
            Console.WriteLine("synthetic-shell-size-sort: calling IShellView.CreateViewWindow");
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out _));
            Console.WriteLine("synthetic-shell-size-sort: IShellView.CreateViewWindow returned");
            viewWindowCreated = true;
            _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE on the Private Desktop only.
            Pump(700);
            if (!IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The synthetic Shell view crossed the Private Desktop/foreground boundary.");

            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 4, 16)); // FVM_DETAILS.
            Check(At<GetFolder>(folderView, 5)(folderView, in ShellFolder2Id, out var viewFolder));
            Check(Marshal.QueryInterface(folder, in UnknownId, out var rootIdentity));
            Check(Marshal.QueryInterface(viewFolder, in UnknownId, out var viewIdentity));
            var sameFolder = rootIdentity == viewIdentity;
            Marshal.Release(viewIdentity);
            Marshal.Release(rootIdentity);
            Marshal.Release(viewFolder);
            if (!sameFolder) throw new InvalidOperationException("IFolderView2::GetFolder did not return the synthetic namespace root.");
            Check(At<ActivateView>(view, 7)(view, 2)); // SVUIA_ACTIVATE_NOFOCUS.
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var contextMenuId = ContextMenuId;
            var contextMenuHr = At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var backgroundMenu); // SVGIO_BACKGROUND
            var menuQueryHr = unchecked((int)0x80004005);
            var shellMenuItemCount = 0;
            var shellMenuVerbs = new List<string>();
            try
            {
                if (backgroundMenu != 0)
                {
                    var menu = CreatePopupMenu();
                    if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create an in-memory Shell menu.");
                    try
                    {
                        menuQueryHr = At<QueryContextMenu>(backgroundMenu, 3)(backgroundMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                        if (menuQueryHr >= 0)
                        {
                            shellMenuItemCount = GetMenuItemCount(menu);
                            var verbBuffer = Marshal.AllocHGlobal(512);
                            try
                            {
                                var commandCount = menuQueryHr & 0xffff;
                                for (var offset = 0; offset < commandCount; offset++)
                                {
                                    Marshal.WriteInt16(verbBuffer, 0);
                                    if (At<GetCommandString>(backgroundMenu, 5)(backgroundMenu, (uint)offset, 4, 0, verbBuffer, 256) == 0)
                                    {
                                        var verb = Marshal.PtrToStringUni(verbBuffer);
                                        if (!string.IsNullOrWhiteSpace(verb)) shellMenuVerbs.Add(verb);
                                    }
                                }
                            }
                            finally { Marshal.FreeHGlobal(verbBuffer); }
                        }
                    }
                    finally { DestroyMenu(menu); }
                }
            }
            finally { if (backgroundMenu != 0) Marshal.Release(backgroundMenu); }
            Console.WriteLine($"synthetic-shell-size-sort: background-IContextMenu-hr=0x{contextMenuHr:X8}; available={contextMenuHr >= 0}; QueryContextMenu-hr=0x{menuQueryHr:X8}; menu-items={shellMenuItemCount}; canonical-verbs=[{string.Join('|', shellMenuVerbs)}]");
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u)); // Select the first item without moving focus.
            Check(At<GetItemCount>(folderView, 7)(folderView, 1, out var selectedCount)); // SVGIO_SELECTION
            if (selectedCount != 1) throw new InvalidOperationException($"Expected exactly one selected synthetic item before querying its Shell objects; got {selectedCount}.");
            var selectionMenuId = ContextMenuId;
            var selectionMenuHr = At<GetItemObject>(view, 15)(view, 1, ref selectionMenuId, out var selectionMenu); // SVGIO_SELECTION
            var selectionMenuQueryHr = unchecked((int)0x80004005);
            var selectionMenuItemCount = 0;
            if (selectionMenu != 0)
            {
                var selectionMenuHandle = CreatePopupMenu();
                if (selectionMenuHandle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create an in-memory selected-item Shell menu.");
                try
                {
                    selectionMenuQueryHr = At<QueryContextMenu>(selectionMenu, 3)(selectionMenu, selectionMenuHandle, 0, 1, 0x7fff, 0x10 | 0x800);
                    if (selectionMenuQueryHr >= 0) selectionMenuItemCount = GetMenuItemCount(selectionMenuHandle);
                }
                finally
                {
                    DestroyMenu(selectionMenuHandle);
                    Marshal.Release(selectionMenu);
                }
            }
            var dataObjectId = DataObjectId;
            var dataObjectHr = At<GetItemObject>(view, 15)(view, 1, ref dataObjectId, out var selectionDataObject);
            var hdropHr = unchecked((int)0x80004005);
            var cidaHr = unchecked((int)0x80004005);
            if (selectionDataObject != 0)
            {
                try
                {
                    hdropHr = At<QueryGetData>(selectionDataObject, 5)(selectionDataObject, new FormatEtc { Format = 15, Aspect = DvaspectContent, Index = -1, Tymed = TymedHGlobal });
                    var cidaFormat = RegisterClipboardFormatW("Shell IDList Array");
                    cidaHr = At<QueryGetData>(selectionDataObject, 5)(selectionDataObject, new FormatEtc { Format = unchecked((short)cidaFormat), Aspect = DvaspectContent, Index = -1, Tymed = TymedHGlobal });
                }
                finally { Marshal.Release(selectionDataObject); }
            }
            Console.WriteLine($"synthetic-shell-size-sort: selected-items={selectedCount}; selected-item-IContextMenu-hr=0x{selectionMenuHr:X8}; QueryContextMenu-hr=0x{selectionMenuQueryHr:X8}; menu-items={selectionMenuItemCount}; selected-IDataObject-hr=0x{dataObjectHr:X8}; CF_HDROP-hr=0x{hdropHr:X8}; CIDA-hr=0x{cidaHr:X8}");
            var listView = FindListView(view);
            var initial = ReadNames(listView, module);
            var ascending = SortAndRead(module, folderView, view, listView, direction: 1);
            var ascendingCompareCount = ReadCounter(module, "GetSyntheticSizeCompareCount");
            var descending = SortAndRead(module, folderView, view, listView, direction: -1);
            var descendingCompareCount = ReadCounter(module, "GetSyntheticSizeCompareCount");
            var totalCompareCount = ReadCounter(module, "GetSyntheticSortCompareCount");

            Console.WriteLine($"synthetic-shell-size-sort: initial=[{string.Join('|', initial)}]; ascending=[{string.Join('|', ascending)}]; descending=[{string.Join('|', descending)}]; size-CompareIDs-ascending={ascendingCompareCount}; size-CompareIDs-descending={descendingCompareCount}; total-CompareIDs={totalCompareCount}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (!initial.SequenceEqual(new[] { "a-large.bin", "two.bin", "z-ten.bin" }) ||
                !ascending.SequenceEqual(new[] { "two.bin", "z-ten.bin", "a-large.bin" }) ||
                !descending.SequenceEqual(new[] { "a-large.bin", "z-ten.bin", "two.bin" }) ||
                selectionMenuHr < 0 || selectionMenuQueryHr < 0 || selectionMenuItemCount == 0 ||
                dataObjectHr < 0 || hdropHr < 0 || cidaHr < 0 ||
                classFactoryCreateCount == 0 ||
                ascendingCompareCount == 0 || descendingCompareCount == 0 ||
                ReadCounter(module, "GetSyntheticSizeCompareCount") == 0)
                return 1;
            return 0;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            if (viewWindowCreated && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            if (view != 0) Marshal.Release(view);
            if (folder != 0) Marshal.Release(folder);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            host?.Dispose();
            if (module != 0) NativeLibrary.Free(module);
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            if (namespaceRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            parentDestroyed = DestroyWindow(parent);
            if (!parentDestroyed) throw new InvalidOperationException("The Private Desktop synthetic-view parent did not destroy cleanly.");
            Directory.Delete(backingFolder, recursive: true);
            if (GetForegroundWindow() != foreground) throw new InvalidOperationException("The synthetic Shell sort probe changed the user's foreground window.");
        }
    }

    private static string[] SortAndRead(nint module, nint folderView, nint view, nint listView, int direction)
    {
        Marshal.GetDelegateForFunctionPointer<ResetCounters>(NativeLibrary.GetExport(module, "ResetSyntheticSortCounters"))();
        var sort = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
        try
        {
            Marshal.StructureToPtr(new NativeSortColumn
            {
                Key = new ShellColumnManager.NativeKey { FormatId = SizeFormatId, PropertyId = 12 },
                Direction = direction
            }, sort, false);
            Check(At<SetSortColumns>(folderView, 27)(folderView, sort, 1));
        }
        finally { Marshal.FreeCoTaskMem(sort); }
        Check(At<RefreshView>(view, 8)(view));
        Pump(400);
        return ReadNames(listView, module);
    }

    private static string[] ReadNames(nint listView, nint module)
    {
        var count = checked((int)SendMessageW(listView, LvmGetItemCount, 0, 0));
        if (count != 3)
            throw new InvalidOperationException($"Expected three synthetic rows, got {count}; EnumObjects={ReadCounter(module, "GetSyntheticEnumObjectsCount")}; enumerated={ReadCounter(module, "GetSyntheticEnumeratedItemCount")}.");
        var values = new string[count];
        var text = Marshal.AllocHGlobal(1024 * sizeof(char));
        try
        {
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt16(text, 0);
                var item = new NativeListViewItem { Mask = LvifText, Item = index, SubItem = 0, Text = text, TextCapacity = 1024 };
                var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
                try
                {
                    Marshal.StructureToPtr(item, itemPointer, false);
                    var length = checked((int)SendMessageW(listView, LvmGetItemTextW, index, itemPointer));
                    if (length < 0 || length >= 1024) throw new InvalidOperationException($"Invalid synthetic list text length {length}.");
                    values[index] = Marshal.PtrToStringUni(text, length) ?? string.Empty;
                }
                finally { Marshal.FreeHGlobal(itemPointer); }
            }
        }
        finally { Marshal.FreeHGlobal(text); }
        return values;
    }

    private static nint FindListView(nint view)
    {
        Check(At<GetWindow>(view, 3)(view, out var viewWindow));
        nint listView = 0;
        EnumChildWindows(viewWindow, (child, _) =>
        {
            var className = new StringBuilder(32);
            if (GetClassNameW(child, className, className.Capacity) > 0 && className.ToString() == "SysListView32")
            {
                listView = child;
                return false;
            }
            return true;
        }, 0);
        if (listView == 0) throw new InvalidOperationException("The synthetic DefView did not create its SysListView32.");
        return listView;
    }

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before the synthetic Shell sort probe.");
    }

    private static void RegisterSyntheticFolder(string classKeyPath)
    {
        using var classes = Registry.CurrentUser.OpenSubKey("Software\\Classes", writable: true)
            ?? throw new InvalidOperationException("HKCU\\Software\\Classes is unavailable.");
        using var existing = classes.OpenSubKey($"CLSID\\{{{SyntheticFolderClassId:D}}}");
        if (existing is not null)
            throw new InvalidOperationException("The reserved synthetic folder CLSID already has a per-user registration; refusing to overwrite it.");

        try
        {
            using var classKey = Registry.CurrentUser.CreateSubKey(classKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary synthetic folder CLSID key.");
            classKey.SetValue(null, "ExplorerDirs isolated synthetic sort probe", RegistryValueKind.String);
            using var inproc = classKey.CreateSubKey("InprocServer32", writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary InprocServer32 key.");
            inproc.SetValue(null, FindAdapterLibrary(), RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            using var shellFolder = classKey.CreateSubKey("ShellFolder", writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary ShellFolder key.");
            shellFolder.SetValue("Attributes", unchecked((int)0xA0000000), RegistryValueKind.DWord);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static void RegisterSyntheticNamespace(string namespaceKeyPath)
    {
        using var existing = Registry.CurrentUser.OpenSubKey(namespaceKeyPath);
        if (existing is not null)
            throw new InvalidOperationException("The reserved synthetic Desktop\\NameSpace key already exists; refusing to overwrite it.");

        try
        {
            using var namespaceKey = Registry.CurrentUser.CreateSubKey(namespaceKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Desktop\\NameSpace key.");
            namespaceKey.SetValue(null, "ExplorerDirs isolated synthetic sort probe", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static int ReadCounter(nint module, string name) =>
        Marshal.GetDelegateForFunctionPointer<GetCounter>(NativeLibrary.GetExport(module, name))();

    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)] private struct NativeSortColumn { public ShellColumnManager.NativeKey Key; public int Direction; }
    [StructLayout(LayoutKind.Sequential)] private struct FormatEtc
    {
        public short Format; public nint TargetDevice; public uint Aspect; public int Index; public uint Tymed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewItem
    {
        public uint Mask; public int Item; public int SubItem; public uint State; public uint StateMask; public nint Text;
        public int TextCapacity; public int Image; public nint Parameter; public int Indent; public int GroupId;
        public uint ColumnCount; public nint Columns; public nint ColumnFormats; public int Group;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ResetCounters();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateSyntheticView(nint rootPidl, out nint folder, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetBackingPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, in Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewModeAndIconSize(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstCommand, uint lastCommand, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryGetData(nint self, in FormatEtc format);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint maxCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    private delegate bool EnumChildProcedure(nint child, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProcedure callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterClipboardFormatW(string format);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
}
