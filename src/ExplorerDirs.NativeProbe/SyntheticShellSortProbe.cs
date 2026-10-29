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
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid SyntheticFolderClassId = new("A17D2A61-4C0B-4B22-8E36-35D92CBC1170");
    private static readonly Guid SizeFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmSetItemState = LvmFirst + 43;
    private const int LvmGetItemState = LvmFirst + 44;
    private const int LvmGetItemTextW = LvmFirst + 115;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const uint LvifText = 0x0001;
    private const uint DvaspectContent = 1;
    private const uint TymedHGlobal = 1;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var backingFolder = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SyntheticShellSort");
        var copyDestination = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SyntheticShellSort-Copy");
        if (Directory.Exists(backingFolder))
            throw new InvalidOperationException($"Refusing to reuse an existing synthetic Shell sort fixture directory: {backingFolder}");
        if (Directory.Exists(copyDestination))
            throw new InvalidOperationException($"Refusing to reuse an existing synthetic Shell sort copy destination: {copyDestination}");
        try
        {
            Directory.CreateDirectory(backingFolder);
            Directory.CreateDirectory(copyDestination);
            File.WriteAllBytes(Path.Combine(backingFolder, "a-large.bin"), Enumerable.Repeat((byte)0xA1, 100).ToArray());
            File.WriteAllBytes(Path.Combine(backingFolder, "two.bin"), [0x02, 0x02]);
            File.WriteAllBytes(Path.Combine(backingFolder, "z-ten.bin"), Enumerable.Repeat((byte)0x10, 10).ToArray());
        }
        catch
        {
            if (Directory.Exists(backingFolder)) Directory.Delete(backingFolder, recursive: true);
            if (Directory.Exists(copyDestination)) Directory.Delete(copyDestination, recursive: true);
            throw;
        }
        var parent = CreateWindowExW(0, "STATIC", "ExplorerDirs synthetic namespace sort probe",
            0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
        if (parent == 0)
        {
            Directory.Delete(backingFolder, recursive: true);
            Directory.Delete(copyDestination, recursive: true);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the Private Desktop probe parent.");
        }

        nint module = 0, rootPidl = 0, folder = 0, view = 0, folderView = 0, hostPointer = 0;
        nint parent2 = 0, folder2 = 0, view2 = 0, folderView2 = 0, hostPointer2 = 0, listView2 = 0;
        var classKeyPath = $@"Software\Classes\CLSID\{{{SyntheticFolderClassId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{SyntheticFolderClassId:D}}}";
        StreamViewHost? host = null;
        StreamViewHost? host2 = null;
        var viewWindowCreated = false;
        var viewWindowCreated2 = false;
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
            parent2 = CreateWindowExW(0, "STATIC", "ExplorerDirs synthetic namespace second view",
                0x80000000, 20, 20, 900, 600, 0, 0, 0, 0);
            if (parent2 == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the second Private Desktop probe parent.");
            var createSyntheticView = Marshal.GetDelegateForFunctionPointer<CreateSyntheticView>(NativeLibrary.GetExport(module, "CreateSyntheticSortShellView"));
            Check(createSyntheticView(rootPidl, out folder2, out view2));
            host2 = new StreamViewHost(parent2, []) { View = view2 };
            hostPointer2 = Marshal.GetComInterfaceForObject(host2, typeof(IStreamProbeBrowser));
            Check(At<CreateViewWindow>(view2, 9)(view2, 0, ref settings, hostPointer2, ref bounds, out _));
            viewWindowCreated2 = true;
            _ = ShowWindow(parent2, 4); // SW_SHOWNOACTIVATE on the Private Desktop only.
            Pump(400);
            if (!IsWindowVisible(parent2) || GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The second synthetic Shell view crossed the Private Desktop/foreground boundary.");
            Check(Marshal.QueryInterface(view2, in FolderView2Id, out folderView2));
            Check(At<SetViewModeAndIconSize>(folderView2, 35)(folderView2, 4, 16)); // FVM_DETAILS.
            Check(At<RefreshView>(view2, 8)(view2));
            Pump(300);
            listView2 = FindListView(view2);
            var secondInitial = ReadNames(listView2, module);
            Console.WriteLine($"synthetic-shell-size-sort: second-view-initial=[{string.Join('|', secondInitial)}]");
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
            string? hdropPath = null;
            string? cidaPath = null;
            var copyResult = "not-run";
            if (selectionDataObject != 0)
            {
                try
                {
                    var hdropFormat = new FormatEtc { Format = 15, Aspect = DvaspectContent, Index = -1, Tymed = TymedHGlobal };
                    hdropHr = At<QueryGetData>(selectionDataObject, 5)(selectionDataObject, hdropFormat);
                    if (hdropHr >= 0)
                    {
                        var getHdropHr = At<GetData>(selectionDataObject, 3)(selectionDataObject, ref hdropFormat, out var hdropMedium);
                        Check(getHdropHr);
                        try
                        {
                            if (hdropMedium.Tymed != TymedHGlobal || hdropMedium.Value == 0) throw new InvalidOperationException("CF_HDROP did not return a valid HGLOBAL medium.");
                            var dropFiles = GlobalLock(hdropMedium.Value);
                            if (dropFiles == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not lock CF_HDROP data.");
                            try
                            {
                                var pathOffset = Marshal.ReadInt32(dropFiles);
                                if (pathOffset < 20 || Marshal.ReadInt32(dropFiles, 16) != 1) throw new InvalidOperationException("CF_HDROP did not contain a valid Unicode DROPFILES block.");
                                hdropPath = Marshal.PtrToStringUni(dropFiles + pathOffset);
                            }
                            finally { GlobalUnlock(hdropMedium.Value); }
                        }
                        finally { ReleaseStgMedium(ref hdropMedium); }
                    }
                    var cidaFormat = RegisterClipboardFormatW("Shell IDList Array");
                    var cidaEtc = new FormatEtc { Format = unchecked((short)cidaFormat), Aspect = DvaspectContent, Index = -1, Tymed = TymedHGlobal };
                    cidaHr = At<QueryGetData>(selectionDataObject, 5)(selectionDataObject, cidaEtc);
                    if (cidaHr >= 0)
                    {
                        var getCidaHr = At<GetData>(selectionDataObject, 3)(selectionDataObject, ref cidaEtc, out var cidaMedium);
                        Check(getCidaHr);
                        try
                        {
                            if (cidaMedium.Tymed != TymedHGlobal || cidaMedium.Value == 0) throw new InvalidOperationException("Shell IDList Array did not return a valid HGLOBAL medium.");
                            var cida = GlobalLock(cidaMedium.Value);
                            if (cida == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not lock Shell IDList Array data.");
                            try
                            {
                                var cidaSize = GlobalSize(cidaMedium.Value).ToUInt64();
                                var childCount = unchecked((uint)Marshal.ReadInt32(cida));
                                var parentOffset = unchecked((uint)Marshal.ReadInt32(cida, 4));
                                var childOffset = unchecked((uint)Marshal.ReadInt32(cida, 8));
                                if (childCount != 1 || cidaSize < 12 || parentOffset >= cidaSize || childOffset >= cidaSize)
                                    throw new InvalidOperationException("Shell IDList Array had an invalid count or PIDL offset.");
                                var absolutePidl = ILCombine(cida + checked((int)parentOffset), cida + checked((int)childOffset));
                                if (absolutePidl == 0) throw new InvalidOperationException("Could not combine the CIDA parent and child PIDLs.");
                                try
                                {
                                    var pathBuffer = new StringBuilder(32768);
                                    if (!SHGetPathFromIDListW(absolutePidl, pathBuffer)) throw new InvalidOperationException("CIDA did not resolve to a filesystem path.");
                                    cidaPath = pathBuffer.ToString();
                                }
                                finally { ILFree(absolutePidl); }
                            }
                            finally { GlobalUnlock(cidaMedium.Value); }
                        }
                        finally { ReleaseStgMedium(ref cidaMedium); }
                    }
                    var fileOperationClass = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
                    var fileOperationInterface = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
                    var createOperationHr = CoCreateInstance(ref fileOperationClass, 0, 1, ref fileOperationInterface, out var fileOperation);
                    Check(createOperationHr);
                    try
                    {
                        Check(At<SetOperationFlags>(fileOperation, 5)(fileOperation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                        var shellItemInterface = ShellItemId;
                        Check(SHCreateItemFromParsingName(copyDestination, 0, ref shellItemInterface, out var destinationItem));
                        try { Check(At<CopyItems>(fileOperation, 17)(fileOperation, selectionDataObject, destinationItem)); }
                        finally { Marshal.Release(destinationItem); }
                        Check(At<PerformOperations>(fileOperation, 21)(fileOperation));
                        Check(At<GetAnyOperationsAborted>(fileOperation, 22)(fileOperation, out var aborted));
                        var copiedPath = Path.Combine(copyDestination, "a-large.bin");
                        if (aborted != 0 || !File.Exists(copiedPath) || new FileInfo(copiedPath).Length != 100 || !File.Exists(hdropPath))
                            throw new IOException("IFileOperation did not copy the selected synthetic item or modified its source.");
                        copyResult = "copied=100-byte a-large.bin; source-preserved=True";
                    }
                    finally { Marshal.Release(fileOperation); }
                }
                finally { Marshal.Release(selectionDataObject); }
            }
            Console.WriteLine($"synthetic-shell-size-sort: selected-items={selectedCount}; selected-item-IContextMenu-hr=0x{selectionMenuHr:X8}; QueryContextMenu-hr=0x{selectionMenuQueryHr:X8}; menu-items={selectionMenuItemCount}; selected-IDataObject-hr=0x{dataObjectHr:X8}; CF_HDROP-hr=0x{hdropHr:X8} path={hdropPath}; CIDA-hr=0x{cidaHr:X8} path={cidaPath}; IFileOperation={copyResult}");
            var listView = FindListView(view);
            var initial = ReadNames(listView, module);
            var ascending = SortAndRead(module, folderView, view, listView, direction: 1);
            var ascendingCompareCount = ReadCounter(module, "GetSyntheticSizeCompareCount");
            var descending = SortAndRead(module, folderView, view, listView, direction: -1);
            var descendingCompareCount = ReadCounter(module, "GetSyntheticSizeCompareCount");
            var totalCompareCount = ReadCounter(module, "GetSyntheticSortCompareCount");

            var originalPath = Path.Combine(backingFolder, "a-large.bin");
            var renamedPath = Path.Combine(backingFolder, "a-large-renamed.bin");
            SetListViewSelectionAndFocus(listView, "two.bin", WaitForNames(listView, module, "two.bin"));
            SetListViewSelectionAndFocus(listView2, "two.bin", WaitForNames(listView2, module, "two.bin"));
            RenameSelectedShellItem(view, listView, module, "a-large.bin", "two.bin", "a-large-renamed.bin");
            if (File.Exists(originalPath) || !File.Exists(renamedPath)) throw new IOException("The isolated IFileOperation rename did not update its backing filesystem fixture.");
            var renameAutoUpdated = false;
            var renameAutoUpdated2 = false;
            var renameNotificationDeadline = Environment.TickCount64 + 2500;
            while ((!renameAutoUpdated || !renameAutoUpdated2) && Environment.TickCount64 < renameNotificationDeadline)
            {
                Pump(100);
                var afterRenameNotification = ReadNames(listView, module, expectedCount: -1);
                var afterRenameNotification2 = ReadNames(listView2, module, expectedCount: -1);
                renameAutoUpdated = afterRenameNotification.Contains("a-large-renamed.bin", StringComparer.OrdinalIgnoreCase);
                renameAutoUpdated2 = afterRenameNotification2.Contains("a-large-renamed.bin", StringComparer.OrdinalIgnoreCase);
            }
            Console.WriteLine($"synthetic-shell-size-sort: rename-auto-updated-view1={renameAutoUpdated}; rename-auto-updated-view2={renameAutoUpdated2}");
            if (!renameAutoUpdated || !renameAutoUpdated2) throw new IOException("Both virtual namespace views did not react to the backing folder's Shell rename notification.");
            var renameAutoNames = ReadNames(listView, module);
            var renameAutoState = StateForName(listView, renameAutoNames, "two.bin");
            var renameSelectionRetained = (renameAutoState & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
            var renameNames2 = ReadNames(listView2, module, expectedCount: -1);
            var renameState2 = StateForName(listView2, renameNames2, "two.bin");
            var renameSelectionRetained2 = (renameState2 & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
            var renameSelectionDeadline = Environment.TickCount64 + 2500;
            while ((!renameSelectionRetained || !renameSelectionRetained2) && Environment.TickCount64 < renameSelectionDeadline)
            {
                Pump(50);
                renameAutoNames = ReadNames(listView, module);
                renameAutoState = StateForName(listView, renameAutoNames, "two.bin");
                renameSelectionRetained = (renameAutoState & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
                renameNames2 = ReadNames(listView2, module, expectedCount: -1);
                renameState2 = StateForName(listView2, renameNames2, "two.bin");
                renameSelectionRetained2 = (renameState2 & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
            }
            Check(At<RefreshView>(view, 8)(view));
            Pump(400);
            var afterRename = ReadNames(listView, module);
            Console.WriteLine($"synthetic-shell-size-sort: rename-auto-updated={renameAutoUpdated}; selection-view1={renameSelectionRetained}; selection-view2={renameSelectionRetained2}; two-state=0x{renameAutoState:X}; after-refresh-rename=[{string.Join('|', afterRename)}]");
            if (!afterRename.Contains("a-large-renamed.bin", StringComparer.OrdinalIgnoreCase))
                throw new IOException("Refreshing the namespace view did not enumerate the renamed backing filesystem item.");
            if (!renameSelectionRetained) throw new IOException("The automatic rename refresh did not preserve selection and keyboard focus on the unchanged item.");
            if (!renameSelectionRetained2) throw new IOException("The second view did not preserve selection and keyboard focus on the unchanged item.");

            DeleteSelectedShellItem(view, listView, module, "a-large-renamed.bin", "two.bin");
            if (File.Exists(renamedPath)) throw new IOException("The isolated IFileOperation delete left the backing filesystem item present.");
            var deleteAutoUpdated = false;
            var deleteAutoUpdated2 = false;
            var deleteAutoSelectionRetained = false;
            var deleteAutoSelectionRetained2 = false;
            var deleteAutoNames = Array.Empty<string>();
            var deleteAutoNames2 = Array.Empty<string>();
            var deleteAutoDeadline = Environment.TickCount64 + 2500;
            while ((!deleteAutoUpdated || !deleteAutoUpdated2 || !deleteAutoSelectionRetained || !deleteAutoSelectionRetained2) && Environment.TickCount64 < deleteAutoDeadline)
            {
                Pump(100);
                deleteAutoNames = ReadNames(listView, module, expectedCount: -1);
                deleteAutoNames2 = ReadNames(listView2, module, expectedCount: -1);
                deleteAutoUpdated = !deleteAutoNames.Contains("a-large-renamed.bin", StringComparer.OrdinalIgnoreCase);
                deleteAutoUpdated2 = !deleteAutoNames2.Contains("a-large-renamed.bin", StringComparer.OrdinalIgnoreCase);
                var deleteAutoState = StateForName(listView, deleteAutoNames, "two.bin");
                var deleteAutoState2 = StateForName(listView2, deleteAutoNames2, "two.bin");
                deleteAutoSelectionRetained = (deleteAutoState & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
                deleteAutoSelectionRetained2 = (deleteAutoState2 & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
            }
            Console.WriteLine($"synthetic-shell-size-sort: delete-auto-updated-view1={deleteAutoUpdated}; delete-auto-updated-view2={deleteAutoUpdated2}; selection-view1={deleteAutoSelectionRetained}; selection-view2={deleteAutoSelectionRetained2}");
            if (!deleteAutoUpdated || !deleteAutoUpdated2) throw new IOException("Both virtual namespace views did not react to the backing folder's Shell delete notification.");
            var finalDeleteState = StateForName(listView, deleteAutoNames, "two.bin");
            Check(At<RefreshView>(view, 8)(view));
            Check(At<RefreshView>(view2, 8)(view2));
            Pump(400);
            var afterDelete = ReadNames(listView, module, expectedCount: 2);
            var afterDelete2 = ReadNames(listView2, module, expectedCount: 2);
            var deleteSelectionRetained = HasSelectionAndFocus(listView, afterDelete, "two.bin");
            Console.WriteLine($"synthetic-shell-size-sort: after-refresh-delete-view1=[{string.Join('|', afterDelete)}]; view2=[{string.Join('|', afterDelete2)}]; two-state=0x{finalDeleteState:X}; rename-selection-retained={renameSelectionRetained}; delete-selection-retained={deleteSelectionRetained}");
            if (afterDelete.Contains("a-large-renamed.bin", StringComparer.OrdinalIgnoreCase))
                throw new IOException("Refreshing the namespace view retained a deleted backing filesystem item.");
            if (!deleteAutoSelectionRetained) throw new IOException("The automatic delete refresh did not preserve selection and keyboard focus on the unchanged item.");
            if (!deleteAutoSelectionRetained2) throw new IOException("The second view did not preserve selection and keyboard focus on the unchanged item.");

            Console.WriteLine($"synthetic-shell-size-sort: initial=[{string.Join('|', initial)}]; ascending=[{string.Join('|', ascending)}]; descending=[{string.Join('|', descending)}]; size-CompareIDs-ascending={ascendingCompareCount}; size-CompareIDs-descending={descendingCompareCount}; total-CompareIDs={totalCompareCount}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (!initial.SequenceEqual(new[] { "a-large.bin", "two.bin", "z-ten.bin" }) ||
                !ascending.SequenceEqual(new[] { "two.bin", "z-ten.bin", "a-large.bin" }) ||
                !descending.SequenceEqual(new[] { "a-large.bin", "z-ten.bin", "two.bin" }) ||
                selectionMenuHr < 0 || selectionMenuQueryHr < 0 || selectionMenuItemCount == 0 ||
                dataObjectHr < 0 || hdropHr < 0 || cidaHr < 0 ||
                !string.Equals(hdropPath, Path.Combine(backingFolder, "a-large.bin"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(cidaPath, hdropPath, StringComparison.OrdinalIgnoreCase) ||
                copyResult == "not-run" || Directory.EnumerateFiles(copyDestination).Count() != 1 ||
                classFactoryCreateCount == 0 ||
                ascendingCompareCount == 0 || descendingCompareCount == 0 ||
                ReadCounter(module, "GetSyntheticSizeCompareCount") == 0)
                return 1;

            var deferredRefreshCounter = Marshal.GetDelegateForFunctionPointer<GetCounter>(NativeLibrary.GetExport(module, "GetSyntheticDeferredRefreshCount"));
            var scheduleSingleRefresh = Marshal.GetDelegateForFunctionPointer<ScheduleSingleRefresh>(NativeLibrary.GetExport(module, "ScheduleSyntheticSortRefreshForProbe"));
            var notifySingleRefresh = Marshal.GetDelegateForFunctionPointer<NotifySingleRefresh>(NativeLibrary.GetExport(module, "NotifySyntheticSortRefreshForProbe"));
            var coalescedRefreshStart = deferredRefreshCounter();
            Check(scheduleSingleRefresh(view));
            var firstRefreshDeadline = Environment.TickCount64 + 2500;
            while (deferredRefreshCounter() == coalescedRefreshStart && Environment.TickCount64 < firstRefreshDeadline) Pump(10);
            if (deferredRefreshCounter() != coalescedRefreshStart + 1)
                throw new TimeoutException("The first per-view refresh callback did not run before the coalescing probe.");
            var lateFilePath = Path.Combine(backingFolder, "late-update.bin");
            File.WriteAllBytes(lateFilePath, [0x7A]);
            var followupNotificationResults = new int[3];
            for (var notificationIndex = 0; notificationIndex < followupNotificationResults.Length; notificationIndex++)
            {
                followupNotificationResults[notificationIndex] = notifySingleRefresh(view);
                if (followupNotificationResults[notificationIndex] < 0)
                    Marshal.ThrowExceptionForHR(followupNotificationResults[notificationIndex]);
            }
            var followupRefreshDeadline = Environment.TickCount64 + 2500;
            while (deferredRefreshCounter() < coalescedRefreshStart + 2 && Environment.TickCount64 < followupRefreshDeadline) Pump(20);
            var lateUpdateNames = ReadNames(listView, module, expectedCount: -1);
            var lateItemDeadline = Environment.TickCount64 + 2500;
            while (!lateUpdateNames.Contains("late-update.bin", StringComparer.OrdinalIgnoreCase) && Environment.TickCount64 < lateItemDeadline)
            {
                Pump(50);
                lateUpdateNames = ReadNames(listView, module, expectedCount: -1);
            }
            var followupRefreshCallbacks = deferredRefreshCounter() - coalescedRefreshStart - 1;
            Console.WriteLine($"synthetic-shell-size-sort: notifications-during-selection-restore={followupNotificationResults.Length}; followup-refresh-callbacks={followupRefreshCallbacks}; late-item-visible={lateUpdateNames.Contains("late-update.bin", StringComparer.OrdinalIgnoreCase)}");
            if (deferredRefreshCounter() < coalescedRefreshStart + 2 ||
                followupRefreshCallbacks != 1 ||
                !lateUpdateNames.Contains("late-update.bin", StringComparer.OrdinalIgnoreCase))
                throw new IOException("Shell change notifications received during selection restoration did not coalesce into one follow-up refresh that exposed the late item.");
            Pump(350);

            var refreshCountBeforeCancel = deferredRefreshCounter();
            var pendingRefreshes = Marshal.GetDelegateForFunctionPointer<SchedulePendingRefreshes>(NativeLibrary.GetExport(module, "ScheduleSyntheticSortRefreshesForProbe"))();
            if (pendingRefreshes != 2) throw new InvalidOperationException($"Expected to schedule one pending refresh per view, got {pendingRefreshes}.");
            var cancelledRefresh = Marshal.GetDelegateForFunctionPointer<CancelViewRefresh>(NativeLibrary.GetExport(module, "CancelSyntheticSortRefreshForProbe"))(view);
            Check(cancelledRefresh);
            Check(At<DestroyViewWindow>(view, 10)(view));
            viewWindowCreated = false;
            Check(At<GetWindow>(view, 3)(view, out var destroyedViewWindow));
            Check(At<GetWindow>(view2, 3)(view2, out var survivingViewWindow));
            if (IsWindow(destroyedViewWindow) || !IsWindow(survivingViewWindow))
                throw new InvalidOperationException("The cancelled first view was not destroyed, or the uncancelled sibling view was lost.");
            Console.WriteLine("synthetic-shell-size-sort: first-view-refresh-cancelled-before-destroy; sibling-view-remains-live");
            Pump(400);
            var callbacksAfterViewDestroy = deferredRefreshCounter() - refreshCountBeforeCancel;
            Console.WriteLine($"synthetic-shell-size-sort: sibling-refresh-callbacks={callbacksAfterViewDestroy}; cancelled-view-callbacks=0");
            if (callbacksAfterViewDestroy != 1)
                throw new InvalidOperationException($"Expected only the uncancelled sibling view refresh to run, got {callbacksAfterViewDestroy} callbacks.");
            Marshal.GetDelegateForFunctionPointer<ReleaseViewHold>(NativeLibrary.GetExport(module, "ReleaseSyntheticSortViewHold"))();
            Pump(400);
            if (deferredRefreshCounter() != refreshCountBeforeCancel + callbacksAfterViewDestroy)
                throw new InvalidOperationException("A deferred refresh callback ran after the probe released both view holds.");
            Console.WriteLine("synthetic-shell-size-sort: pending-refresh-cancelled-on-release=1; callbacks-after-release=0");
            return 0;
        }
        finally
        {
            if (module != 0) Marshal.GetDelegateForFunctionPointer<ReleaseViewHold>(NativeLibrary.GetExport(module, "ReleaseSyntheticSortViewHold"))();
            if (folderView != 0) Marshal.Release(folderView);
            if (folderView2 != 0) Marshal.Release(folderView2);
            if (viewWindowCreated && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            if (viewWindowCreated2 && view2 != 0) _ = At<DestroyViewWindow>(view2, 10)(view2);
            if (view != 0) Marshal.Release(view);
            if (view2 != 0) Marshal.Release(view2);
            if (folder != 0) Marshal.Release(folder);
            if (folder2 != 0) Marshal.Release(folder2);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (hostPointer2 != 0) Marshal.Release(hostPointer2);
            host?.Dispose();
            host2?.Dispose();
            if (module != 0) NativeLibrary.Free(module);
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            if (namespaceRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            parentDestroyed = DestroyWindow(parent);
            if (!parentDestroyed) throw new InvalidOperationException("The Private Desktop synthetic-view parent did not destroy cleanly.");
            if (parent2 != 0 && !DestroyWindow(parent2)) throw new InvalidOperationException("The second Private Desktop synthetic-view parent did not destroy cleanly.");
            Directory.Delete(backingFolder, recursive: true);
            Directory.Delete(copyDestination, recursive: true);
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

    private static void RenameSelectedShellItem(nint view, nint listView, nint module, string itemName, string restoreFocusName, string newName)
    {
        var dataObject = GetSelectionDataObject(view, listView, module, itemName, restoreFocusName);
        var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        var createOperationHr = CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var operation);
        if (createOperationHr < 0) { Marshal.Release(dataObject); Check(createOperationHr); }
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            Check(At<RenameItems>(operation, 13)(operation, dataObject, newName));
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("The isolated virtual-view Shell rename was aborted.");
        }
        finally
        {
            Marshal.Release(operation);
            Marshal.Release(dataObject);
        }
    }

    private static void DeleteSelectedShellItem(nint view, nint listView, nint module, string itemName, string restoreFocusName)
    {
        var dataObject = GetSelectionDataObject(view, listView, module, itemName, restoreFocusName);
        var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        var createOperationHr = CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var operation);
        if (createOperationHr < 0) { Marshal.Release(dataObject); Check(createOperationHr); }
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            Check(At<DeleteItems>(operation, 19)(operation, dataObject));
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("The isolated virtual-view Shell delete was aborted.");
        }
        finally
        {
            Marshal.Release(operation);
            Marshal.Release(dataObject);
        }
    }

    private static nint GetSelectionDataObject(nint view, nint listView, nint module, string itemName, string restoreFocusName)
    {
        SetListViewSelectionAndFocus(listView, itemName, ReadNames(listView, module));
        var dataObjectId = DataObjectId;
        Check(At<GetItemObject>(view, 15)(view, 1, ref dataObjectId, out var dataObject)); // SVGIO_SELECTION.
        if (dataObject == 0) throw new IOException($"The synthetic view returned no Shell IDataObject for selected item '{itemName}'.");
        try { SetListViewSelectionAndFocus(listView, restoreFocusName, ReadNames(listView, module)); }
        catch { Marshal.Release(dataObject); throw; }
        return dataObject;
    }

    private static string[] ReadNames(nint listView, nint module, int expectedCount = 3)
    {
        var count = checked((int)SendMessageW(listView, LvmGetItemCount, 0, 0));
        if (expectedCount >= 0 && count != expectedCount)
            throw new InvalidOperationException($"Expected {expectedCount} synthetic rows, got {count}; EnumObjects={ReadCounter(module, "GetSyntheticEnumObjectsCount")}; enumerated={ReadCounter(module, "GetSyntheticEnumeratedItemCount")}.");
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

    private static string[] WaitForNames(nint listView, nint module, string expectedName)
    {
        var deadline = Environment.TickCount64 + 2500;
        string[] names;
        do
        {
            names = ReadNames(listView, module, expectedCount: -1);
            if (names.Length == 3 && names.Contains(expectedName, StringComparer.OrdinalIgnoreCase)) return names;
            Pump(50);
        } while (Environment.TickCount64 < deadline);
        throw new TimeoutException($"The synthetic Shell view did not settle to three rows including '{expectedName}' within 2.5 seconds; current=[{string.Join('|', names)}].");
    }

    private static void SetListViewSelectionAndFocus(nint listView, string name, string[] names)
    {
        var index = Array.FindIndex(names, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"Could not find '{name}' to establish the selection/focus probe state.");
        SetListViewState(listView, -1, state: 0, stateMask: LvisSelected | LvisFocused);
        SetListViewState(listView, index, state: LvisSelected | LvisFocused, stateMask: LvisSelected | LvisFocused);
        if (!HasSelectionAndFocus(listView, names, name)) throw new InvalidOperationException("Could not establish the selection/focus probe state.");
    }

    private static bool HasSelectionAndFocus(nint listView, string[] names, string name)
    {
        var state = StateForName(listView, names, name);
        return (state & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
    }

    private static uint StateForName(nint listView, string[] names, string name)
    {
        var index = Array.FindIndex(names, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : GetListViewState(listView, index);
    }

    private static uint GetListViewState(nint listView, int index) =>
        unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)));

    private static void SetListViewState(nint listView, int index, uint state, uint stateMask)
    {
        var item = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            Marshal.StructureToPtr(new NativeListViewItem { State = state, StateMask = stateMask }, item, false);
            SendMessageW(listView, LvmSetItemState, index, item);
        }
        finally { Marshal.FreeHGlobal(item); }
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
    [StructLayout(LayoutKind.Sequential)] private struct StorageMedium
    {
        public uint Tymed; public nint Value; public nint ReleaseUnknown;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ReleaseViewHold();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SchedulePendingRefreshes();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CancelViewRefresh(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ScheduleSingleRefresh(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NotifySingleRefresh(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RenameItems(nint self, nint dataObject, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItems(nint self, nint dataObject);
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetData(nint self, ref FormatEtc format, out StorageMedium medium);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint maxCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    private delegate bool EnumChildProcedure(nint child, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProcedure callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterClipboardFormatW(string format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint GlobalSize(nint memory);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref StorageMedium medium);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SHGetPathFromIDListW(nint pidl, StringBuilder path);
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string name, nint bindContext, ref Guid interfaceId, out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
}
