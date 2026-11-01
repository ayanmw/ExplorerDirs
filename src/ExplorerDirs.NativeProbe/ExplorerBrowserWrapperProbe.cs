using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerDirs.App;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

// Checks whether BrowseToObject keeps a wrapped filesystem folder as the active view folder.
// The host runs only on PrivateDesktopShellProbe's separate Windows Desktop.
internal static class ExplorerBrowserWrapperProbe
{
    private static readonly Guid BrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid BrowserInterfaceId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid FileOperationClassId = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid FileOperationInterfaceId = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid PersistFolder2Id = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
    private const uint SigdnFileSystemPath = 0x80058000;
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;
    private const uint SbspParent = 0x2000;
    private const uint SbspNavigateBack = 0x4000;
    private const uint SbspNavigateForward = 0x8000;

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-BrowserWrapper-" + Guid.NewGuid().ToString("N"));
        nint browser = 0, parent = 0, folderPidl = 0, fileSystemFolder = 0, adapter = 0;
        nint shellView = 0, folderView = 0, viewFolder = 0, adapterIdentity = 0, viewIdentity = 0, persist = 0, currentPidl = 0;
        nint nativeLibrary = 0;
        uint initialized = 0;
        try
        {
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: create-fixture");
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "sample.txt"), "wrapper-probe");
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            var shellFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(SHBindToObject(0, folderPidl, 0, ref shellFolderId, out fileSystemFolder));

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            var createAdapter = Marshal.GetDelegateForFunctionPointer<CreateAdapter>(
                NativeLibrary.GetExport(nativeLibrary, "CreateStrictShellFolderAdapter"));
            Marshal.ThrowExceptionForHR(createAdapter(fileSystemFolder, out adapter));

            var browserClass = BrowserClassId;
            var browserId = BrowserInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out browser));
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated wrapper browser probe",
                0x90000000, 20, 20, 820, 620, 0, 0, 0, 0); // Visible only on the isolated probe Desktop.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var bounds = new ProbeRect { Right = 820, Bottom = 620 };
            var settings = new ProbeFolderSettings { Mode = 4 };
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: browse-to-object");
            Marshal.ThrowExceptionForHR(At<Initialize>(browser, 3)(browser, parent, ref bounds, ref settings));
            initialized = 1;
            var browseHr = At<BrowseToObject>(browser, 14)(browser, adapter, 0);
            if (browseHr < 0) Marshal.ThrowExceptionForHR(browseHr);
            Application.DoEvents();

            PrivateDesktopShellProbe.ReportStage("wrapper-probe: inspect-view-identity");
            var shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
            var folderView2Id = FolderView2Id;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
            var viewFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref viewFolderId, out viewFolder));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(adapter, in UnknownId, out adapterIdentity));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(viewFolder, in UnknownId, out viewIdentity));
            var adapterRetained = adapterIdentity == viewIdentity;

            var persistId = PersistFolder2Id;
            string currentPath = "unavailable";
            if (Marshal.QueryInterface(viewFolder, in persistId, out persist) >= 0 && persist != 0 &&
                At<GetCurFolder>(persist, 5)(persist, out currentPidl) >= 0 && currentPidl != 0 &&
                SHGetNameFromIDList(currentPidl, SigdnFileSystemPath, out var pathPointer) >= 0)
            {
                currentPath = Marshal.PtrToStringUni(pathPointer) ?? "unavailable";
                Marshal.FreeCoTaskMem(pathPointer);
            }

            var currentPathMatchesFixture = string.Equals(currentPath, fixture, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"explorerbrowser-wrapper-object: browse-to-object=0x{browseHr:X8}; adapter-folder-retained={adapterRetained}; current-path-matches-fixture={currentPathMatchesFixture}; current-path={currentPath}; fixture-path={fixture}; host-window-created=True; visible-to-user=False");
            if (adapterRetained || !currentPathMatchesFixture)
                throw new InvalidOperationException("BrowseToObject behavior differed from the expected filesystem rebind diagnostic.");
            return 0;
        }
        finally
        {
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-current-pidl");
            if (currentPidl != 0) Marshal.FreeCoTaskMem(currentPidl);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-persist");
            if (persist != 0) Marshal.Release(persist);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-view-identity");
            if (viewIdentity != 0) Marshal.Release(viewIdentity);
            if (adapterIdentity != 0) Marshal.Release(adapterIdentity);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-view-folder");
            if (viewFolder != 0) Marshal.Release(viewFolder);
            if (folderView != 0) Marshal.Release(folderView);
            if (shellView != 0) Marshal.Release(shellView);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: browser-destroy");
            if (initialized != 0 && browser != 0) _ = At<Destroy>(browser, 4)(browser);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: browser-release");
            if (browser != 0) Marshal.Release(browser);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: parent-destroy");
            // ExplorerBrowser parent destruction can block after the browser is destroyed;
            // the isolated probe process owns this private-Desktop HWND until process exit.
            Console.WriteLine($"explorerbrowser-wrapper-object: parent-destroy-deferred={parent != 0 && IsWindow(parent)}");
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: release-adapter-and-folder");
            if (adapter != 0) Marshal.Release(adapter);
            if (fileSystemFolder != 0) Marshal.Release(fileSystemFolder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (nativeLibrary != 0) NativeLibrary.Free(nativeLibrary);
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-complete");
        }
    }

    internal static int RunNamespace()
    {
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-BrowserNamespace-" + classId.ToString("N"));
        nint browser = 0, parent = 0, rootPidl = 0, shellView = 0, folderView = 0, viewFolder = 0, persist = 0, currentPidl = 0;
        nint nativeLibrary = 0;
        uint initialized = 0;
        var classRegistrationCreated = false;
        var namespaceRegistrationCreated = false;
        try
        {
            PrivateDesktopShellProbe.ReportStage("namespace-probe: prepare-fixture-and-registration");
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            Directory.CreateDirectory(Path.Combine(fixture, "child", "nested"));
            Directory.CreateDirectory(Path.Combine(fixture, "child-small"));
            File.WriteAllBytes(Path.Combine(fixture, "root.txt"), [1, 2, 3, 4]);
            File.WriteAllText(Path.Combine(fixture, "rename-source.txt"), "namespace rename contract");
            File.WriteAllBytes(Path.Combine(fixture, "child", "nested", "deep.bin"), Enumerable.Repeat((byte)7, 10).ToArray());
            File.WriteAllBytes(Path.Combine(fixture, "child-small", "payload.bin"), [1, 2]);

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetFolderClassId>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticFolderClassIdForProbe"))(ref classId));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetBackingPath>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticSortBackingPath"))(fixture));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticCustomSizeColumnForProbe"))(1));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticChildNotificationTranslationForProbe"))(1));
            RegisterSyntheticFolder(classKeyPath);
            classRegistrationCreated = true;
            RegisterSyntheticNamespace(namespaceKeyPath);
            namespaceRegistrationCreated = true;
            Marshal.ThrowExceptionForHR(SHParseDisplayName($"::{{{classId:D}}}", 0, out rootPidl, 0, out _));
            if (rootPidl == 0) throw new InvalidOperationException("The temporary namespace did not resolve to an absolute root PIDL.");

            var browserClass = BrowserClassId;
            var browserId = BrowserInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out browser));
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated registered namespace browser probe",
                0x90000000, 20, 20, 820, 620, 0, 0, 0, 0); // Visible only on the isolated probe Desktop.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var bounds = new ProbeRect { Right = 820, Bottom = 620 };
            var settings = new ProbeFolderSettings { Mode = 4 };
            PrivateDesktopShellProbe.ReportStage("namespace-probe: browse-to-root-pidl");
            Marshal.ThrowExceptionForHR(At<Initialize>(browser, 3)(browser, parent, ref bounds, ref settings));
            initialized = 1;
            var browseHr = At<BrowseToIdList>(browser, 13)(browser, rootPidl, 0);
            Marshal.ThrowExceptionForHR(browseHr);
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();

            PrivateDesktopShellProbe.ReportStage("namespace-probe: inspect-active-view");
            var shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
            var folderView2Id = FolderView2Id;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
            var viewFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref viewFolderId, out viewFolder));

            var persistId = PersistFolder2Id;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(viewFolder, in persistId, out persist));
            Marshal.ThrowExceptionForHR(At<GetClassId>(persist, 3)(persist, out var activeClassId));
            Marshal.ThrowExceptionForHR(At<GetCurFolder>(persist, 5)(persist, out currentPidl));
            var classIdMatches = activeClassId == classId;
            var rootPidlMatches = PidlBytesEqual(rootPidl, currentPidl);
            var recursiveColumn = ReadHeader(viewFolder, 1);
            var recursiveColumnPresent = string.Equals(recursiveColumn, "Recursive Size", StringComparison.Ordinal);
            Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var itemCount)); // SVGIO_ALLVIEW
            var sizeKey = new PropertyKeyState(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
            var captureHr = ShellColumnManager.Capture(folderView, out var initialVisibleColumns);
            if (captureHr != 0 || initialVisibleColumns is null)
                throw new InvalidOperationException($"Could not capture ExplorerBrowser Shell columns: {captureHr}.");
            var requestedColumns = initialVisibleColumns.Any(column => column.Key == sizeKey)
                ? initialVisibleColumns
                : initialVisibleColumns.Append(new DisplayColumnState(sizeKey, 160)).ToArray();
            var applyHr = ShellColumnManager.Apply(folderView, requestedColumns);
            var verifyCaptureHr = ShellColumnManager.Capture(folderView, out var visibleColumnsAfterApply);
            if (applyHr != 0 || verifyCaptureHr != 0 || visibleColumnsAfterApply is null)
                throw new InvalidOperationException($"Could not apply/read ExplorerBrowser Shell columns: apply={applyHr}; capture={verifyCaptureHr}.");
            var recursiveSizeVisible = visibleColumnsAfterApply.Any(column => column.Key == sizeKey);
            var shellViewWindow = At<GetWindow>(shellView, 3)(shellView, out var viewWindow) >= 0 ? viewWindow : 0;
            var listView = FindChildByClass(shellViewWindow, "SysListView32");
            var header = listView == 0 ? 0 : SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
            var headerTitles = header == 0 ? [] : ReadNativeHeaderTitles(header);
            var viewChildClasses = ReadChildClasses(shellViewWindow);
            if (!classIdMatches || !rootPidlMatches || !recursiveColumnPresent || !recursiveSizeVisible || itemCount != 4)
                throw new InvalidOperationException("ExplorerBrowser did not preserve the registered custom namespace view contract.");
            Console.WriteLine($"explorerbrowser-registered-namespace: browse-to-idlist=0x{browseHr:X8}; class-id-matches={classIdMatches}; root-pidl-matches={rootPidlMatches}; recursive-column-metadata={recursiveColumn}; recursive-size-visible-in-shell={recursiveSizeVisible}; visible-shell-column-count={visibleColumnsAfterApply.Count}; native-header=[{string.Join('|', headerTitles)}]; view-child-classes=[{string.Join('|', viewChildClasses)}]; row-count={itemCount}; fixture={fixture}; visible-to-user=False");

            nint backgroundMenu = 0, backgroundPopup = 0;
            var backgroundMenuHr = CreateViewObject(viewFolder, ContextMenuId, out backgroundMenu);
            var backgroundMenuQueryHr = unchecked((int)0x80004005);
            var backgroundMenuItemCount = -1;
            try
            {
                if (backgroundMenuHr >= 0 && backgroundMenu != 0)
                {
                    backgroundPopup = CreatePopupMenu();
                    if (backgroundPopup == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    backgroundMenuQueryHr = At<QueryContextMenu>(backgroundMenu, 3)(backgroundMenu, backgroundPopup, 0, 1, 0x7FFF, 0);
                    if (backgroundMenuQueryHr >= 0) backgroundMenuItemCount = GetMenuItemCount(backgroundPopup);
                }
            }
            finally
            {
                if (backgroundPopup != 0) DestroyMenu(backgroundPopup);
                if (backgroundMenu != 0) Marshal.Release(backgroundMenu);
            }
            Console.WriteLine($"explorerbrowser-registered-namespace: background-context-menu-hr=0x{backgroundMenuHr:X8}; query-hr=0x{backgroundMenuQueryHr:X8}; menu-items={backgroundMenuItemCount}");
            if (backgroundMenuHr < 0 || backgroundMenu == 0 || backgroundMenuQueryHr < 0 || backgroundMenuItemCount <= 0)
                throw new InvalidOperationException("The registered namespace did not expose native background-folder commands.");

            // Verify sorting through the actual ExplorerBrowser view. New property keys may be
            // rejected by IFolderView2 even when the folder maps the column, so also probe the
            // legacy Shell-folder-view column-index path used by DefView.
            Marshal.ThrowExceptionForHR(At<MapColumnToScid>(viewFolder, 19)(viewFolder, 1, out var recursiveSizeKey));
            var sortColumn = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeSortColumn>());
            int recursiveKeySortHr;
            try
            {
                Marshal.StructureToPtr(new NativeSortColumn { Key = recursiveSizeKey, Direction = 1 }, sortColumn, false);
                recursiveKeySortHr = At<SetSortColumns>(folderView, 27)(folderView, sortColumn, 1);
                Marshal.StructureToPtr(new NativeSortColumn
                {
                    Key = new ShellColumnManager.NativeKey { FormatId = sizeKey.FormatId, PropertyId = sizeKey.PropertyId },
                    Direction = 1
                }, sortColumn, false);
                Marshal.ThrowExceptionForHR(At<SetSortColumns>(folderView, 27)(folderView, sortColumn, 1));
            }
            finally { Marshal.FreeCoTaskMem(sortColumn); }
            Marshal.ThrowExceptionForHR(At<RefreshView>(shellView, 8)(shellView));
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();
            var pkeySizeOrder = ReadViewItemNamesInOrder(folderView, viewFolder);
            var compareCount = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
                NativeLibrary.GetExport(nativeLibrary, "GetSyntheticSizeCompareCount"))();
            Console.WriteLine($"explorerbrowser-registered-namespace: recursive-property-sort-hr=0x{recursiveKeySortHr:X8}; PKEY_Size-order=[{string.Join('|', pkeySizeOrder)}]; recursive-size-compare-calls={compareCount}");
            if (recursiveKeySortHr >= 0 || compareCount != 0 ||
                !pkeySizeOrder.SequenceEqual(new[] { "child", "child-small", "root.txt", "rename-source.txt" }, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("The property-key sort control did not distinguish ExplorerBrowser's unsupported custom-key path.");

            var shellFolderViewId = new Guid("37A378C0-F82D-11CE-AE65-08002B2E1262");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in shellFolderViewId, out var shellFolderView));
            try
            {
                Marshal.ThrowExceptionForHR(At<Rearrange>(shellFolderView, 3)(shellFolderView, 1));
                Marshal.ThrowExceptionForHR(At<RefreshView>(shellView, 8)(shellView));
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();
            }
            finally { Marshal.Release(shellFolderView); }
            var legacyColumnOrder = ReadViewItemNamesInOrder(folderView, viewFolder);
            compareCount = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
                NativeLibrary.GetExport(nativeLibrary, "GetSyntheticSizeCompareCount"))();
            Console.WriteLine($"explorerbrowser-registered-namespace: legacy-size-column-order=[{string.Join('|', legacyColumnOrder)}]; compare-calls={compareCount}");
            if (recursiveKeySortHr >= 0 || compareCount == 0 ||
                !legacyColumnOrder.SequenceEqual(new[] { "child-small", "root.txt", "child", "rename-source.txt" }, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Shell view did not sort mixed file/folder rows by the recursive Size column through its legacy column-index path.");

            File.WriteAllText(Path.Combine(fixture, "context-delete.txt"), "temporary native-menu delete target");
            PrivateDesktopShellProbe.ReportStage("namespace-probe: invoke-native-delete-command");
            var deleteCommandOffset = InvokeNativeDeleteWithoutUi(viewFolder, "context-delete.txt");
            if (File.Exists(Path.Combine(fixture, "context-delete.txt")))
                throw new InvalidOperationException("The registered namespace's native Delete verb did not reach the backing file.");
            Console.WriteLine($"explorerbrowser-registered-namespace: native-delete-verb=delete; command-offset={deleteCommandOffset}; backing-file-removed=True; no-confirmation-ui=True");

            PrivateDesktopShellProbe.ReportStage("namespace-probe: native-item-menu-and-rename");
            Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "rename-source.txt", out var renameSource));
            nint contextMenu = 0, popup = 0;
            int shellMenuItemCount;
            try
            {
                Marshal.ThrowExceptionForHR(GetUiObjectOf(viewFolder, renameSource, ContextMenuId, out contextMenu));
                popup = CreatePopupMenu();
                if (popup == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Marshal.ThrowExceptionForHR(At<QueryContextMenu>(contextMenu, 3)(contextMenu, popup, 0, 1, 0x7FFF, 0));
                shellMenuItemCount = GetMenuItemCount(popup);
                if (shellMenuItemCount <= 0) throw new InvalidOperationException("The active namespace did not provide native file commands.");
            }
            finally
            {
                if (popup != 0) DestroyMenu(popup);
                if (contextMenu != 0) Marshal.Release(contextMenu);
                Marshal.FreeCoTaskMem(renameSource);
            }

            Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "rename-source.txt", out var renameSourceAgain));
            nint renamedItem = 0;
            try
            {
                Marshal.ThrowExceptionForHR(SetNameOf(viewFolder, renameSourceAgain, "rename-complete.txt", out renamedItem));
                if (File.Exists(Path.Combine(fixture, "rename-source.txt")) ||
                    !File.Exists(Path.Combine(fixture, "rename-complete.txt")))
                    throw new InvalidOperationException("The registered namespace rename did not reach the backing filesystem.");
            }
            finally
            {
                if (renamedItem != 0) Marshal.FreeCoTaskMem(renamedItem);
                Marshal.FreeCoTaskMem(renameSourceAgain);
            }
            Console.WriteLine($"explorerbrowser-registered-namespace: native-item-menu-items={shellMenuItemCount}; backing-filesystem-rename=True");

            PrivateDesktopShellProbe.ReportStage("namespace-probe: shell-updatedir-refresh");
            var rootNotificationPath = Path.Combine(fixture, "notification-created.txt");
            var rootNotifyExitCode = RunExternalShellNotificationChild(rootNotificationPath, [9, 8, 7]);
            if (rootNotifyExitCode != 0)
                throw new InvalidOperationException($"The external process failed to create and notify the root item; exit={rootNotifyExitCode}.");
            int countAfterNotification = -1;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                Application.DoEvents();
                Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out countAfterNotification));
                if (countAfterNotification == 5) break;
                Thread.Sleep(100);
            }
            if (countAfterNotification != 5)
                throw new InvalidOperationException($"The registered namespace view did not enumerate the externally created file after SHCNE_UPDATEDIR; count={countAfterNotification}.");
            Console.WriteLine($"explorerbrowser-registered-namespace: external-process-root-create-exit={rootNotifyExitCode}; updatedir-row-count={countAfterNotification}; external-create-visible=True");

            var childSizeBeforeNotification = ReadViewItemDetailsByName(folderView, viewFolder, "child", 1);
            if (childSizeBeforeNotification != "10")
                throw new InvalidOperationException($"The recursive Size row value was unexpected before its child update; value={childSizeBeforeNotification}.");
            var childNotificationPath = Path.Combine(fixture, "child", "notification-size.bin");
            var childNotifyExitCode = RunExternalShellNotificationChild(childNotificationPath, [1, 2, 3, 4, 5, 6, 7]);
            if (childNotifyExitCode != 0)
                throw new InvalidOperationException($"The external process failed to create and notify the nested item; exit={childNotifyExitCode}.");
            string childSizeAfterNotification = "";
            var getQueuedNotifications = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
                NativeLibrary.GetExport(nativeLibrary, "GetSyntheticChildNotificationTranslationQueuedCount"));
            var getSentNotifications = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
                NativeLibrary.GetExport(nativeLibrary, "GetSyntheticChildNotificationTranslationSentCount"));
            var getEnumerationCount = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
                NativeLibrary.GetExport(nativeLibrary, "GetSyntheticEnumObjectsCount"));
            var queuedBeforeChildChange = getQueuedNotifications();
            var sentBeforeChildChange = getSentNotifications();
            var enumerationsBeforeChildChange = getEnumerationCount();
            string providerSizeAfterNotification = "";
            for (var attempt = 0; attempt < 50; attempt++)
            {
                Application.DoEvents();
                childSizeAfterNotification = ReadViewItemDetailsByName(folderView, viewFolder, "child", 1);
                providerSizeAfterNotification = ReadParsedItemDetailsByName(viewFolder, "child", 1);
                if (childSizeAfterNotification == "17") break;
                Thread.Sleep(100);
            }
            var enumerationsAfterChildChange = getEnumerationCount();
            var viewItemSize = ReadViewItemPidlSizeByName(folderView, viewFolder, "child");
            Console.WriteLine($"explorerbrowser-registered-namespace: external-process-child-create-exit={childNotifyExitCode}; child-updateitem-queued={getQueuedNotifications() - queuedBeforeChildChange}; sent={getSentNotifications() - sentBeforeChildChange}; enumerations={enumerationsBeforeChildChange}->{enumerationsAfterChildChange}; view-size={childSizeBeforeNotification}->{childSizeAfterNotification}; provider-size={providerSizeAfterNotification}; view-pidl-size={viewItemSize}; row-refreshed={childSizeAfterNotification == "17"}");
            if (providerSizeAfterNotification != "17" || enumerationsAfterChildChange <= enumerationsBeforeChildChange)
                throw new InvalidOperationException("The backing namespace provider did not rescan the changed child directory after its Shell notification.");
            if (childSizeAfterNotification != "17")
                throw new InvalidOperationException($"The existing Shell row did not read the updated recursive Size snapshot; value={childSizeAfterNotification}.");

            PrivateDesktopShellProbe.ReportStage("namespace-probe: child-and-parent-navigation");
            Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "child", out var childItem));
            var expectedChildRoot = ILCombine(rootPidl, childItem);
            Marshal.FreeCoTaskMem(childItem);
            if (expectedChildRoot == 0) throw new InvalidOperationException("Could not compose a child namespace PIDL.");
            try
            {
                NavigateAndWaitForFolder(browser, expectedChildRoot, 0, classId,
                    expectedChildRoot, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                var childRowCount = WaitForViewItemCount(folderView, 2);
                if (childRowCount != 2)
                    throw new InvalidOperationException("Child navigation did not preserve the namespace PIDL or enumerate its backing folder.");
                var childColumnCaptureHr = ShellColumnManager.Capture(folderView, out var childColumns);
                if (childColumnCaptureHr != 0 || childColumns is null)
                    throw new InvalidOperationException($"Could not capture wrapped child-folder columns: {childColumnCaptureHr}.");
                var recursiveColumnVisibleInChild = childColumns.Any(column => column.Key == sizeKey);

                Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "nested", out var nestedItem));
                var expectedNestedRoot = ILCombine(expectedChildRoot, nestedItem);
                if (expectedNestedRoot == 0) throw new InvalidOperationException("Could not compose a nested namespace PIDL.");
                var absoluteNestedNameHr = SHParseDisplayName($"::{{{classId:D}}}\\child\\nested", 0,
                    out var absoluteNestedNamePidl, 0, out _);
                var nestedFolderIid = ShellFolder2Id;
                var nestedBindHr = At<BindToObjectNative>(viewFolder, 5)(viewFolder, nestedItem, 0,
                    ref nestedFolderIid, out var directlyBoundNestedFolder);
                Marshal.FreeCoTaskMem(nestedItem);
                var directlyBoundNestedPidlMatches = false;
                if (nestedBindHr >= 0 && directlyBoundNestedFolder != 0)
                {
                    var nestedPersistId = PersistFolder2Id;
                    if (Marshal.QueryInterface(directlyBoundNestedFolder, in nestedPersistId, out var directlyBoundPersist) >= 0)
                    {
                        try
                        {
                            Marshal.ThrowExceptionForHR(At<GetCurFolder>(directlyBoundPersist, 5)(directlyBoundPersist, out var directlyBoundPidl));
                            try { directlyBoundNestedPidlMatches = PidlBytesEqual(expectedNestedRoot, directlyBoundPidl); }
                            finally { Marshal.FreeCoTaskMem(directlyBoundPidl); }
                        }
                        finally { Marshal.Release(directlyBoundPersist); }
                    }
                }
                var absoluteNestedNameMatches = absoluteNestedNameHr >= 0 &&
                    PidlBytesEqual(expectedNestedRoot, absoluteNestedNamePidl);
                Console.WriteLine($"explorerbrowser-registered-namespace: direct-nested-bind-hr=0x{nestedBindHr:X8}; direct-nested-pidl-matches={directlyBoundNestedPidlMatches}; absolute-nested-parse-hr=0x{absoluteNestedNameHr:X8}; absolute-nested-pidl-matches={absoluteNestedNameMatches}");
                var nestedNavigationPidl = absoluteNestedNameHr >= 0 ? absoluteNestedNamePidl : expectedNestedRoot;
                try
                {
                    try
                    {
                        NavigateAndWaitForFolder(browser, nestedNavigationPidl, 0, classId,
                            nestedNavigationPidl, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                    }
                    catch (TimeoutException)
                    {
                        var objectNavigation = ProbeBrowseToObject(browser, directlyBoundNestedFolder, classId,
                            nestedNavigationPidl, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                        Console.WriteLine($"explorerbrowser-registered-namespace: browse-to-object-hr=0x{objectNavigation.HResult:X8}; object-class-matches={objectNavigation.ClassMatches}; object-pidl-matches={objectNavigation.PidlMatches}; object-location={objectNavigation.Location}; filesystem-path={objectNavigation.FileSystemPath}");
                        if (!objectNavigation.ClassMatches || !objectNavigation.PidlMatches)
                            throw new InvalidOperationException("BrowseToIDList did not reach nested and BrowseToObject did not preserve the wrapped nested folder.");
                    }
                    var nestedRowCount = WaitForViewItemCount(folderView, 1);
                    NavigateAndWaitForFolder(browser, 0, SbspParent, classId,
                        expectedChildRoot, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                    _ = WaitForViewItemCount(folderView, 2);
                    NavigateAndWaitForFolder(browser, 0, SbspNavigateBack, classId,
                        nestedNavigationPidl, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                    _ = WaitForViewItemCount(folderView, 1);
                    NavigateAndWaitForFolder(browser, 0, SbspNavigateForward, classId,
                        expectedChildRoot, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                    _ = WaitForViewItemCount(folderView, 2);
                    if (nestedRowCount != 1)
                        throw new InvalidOperationException("Nested navigation did not enumerate its backing folder.");
                    var forwardColumnCaptureHr = ShellColumnManager.Capture(folderView, out var forwardColumns);
                    if (forwardColumnCaptureHr != 0 || forwardColumns is null)
                        throw new InvalidOperationException($"Could not capture columns after Shell TravelLog forward: {forwardColumnCaptureHr}.");
                    var recursiveColumnVisibleAfterForward = forwardColumns.Any(column => column.Key == sizeKey);
                    var nestedRecursiveSizeAfterForward = recursiveColumnVisibleAfterForward
                        ? ReadViewItemDetailsByName(folderView, viewFolder, "nested", 1)
                        : "<column-hidden>";
                    var recursiveColumnAndValueRestored = recursiveColumnVisibleInChild &&
                        recursiveColumnVisibleAfterForward && nestedRecursiveSizeAfterForward == "10";
                    Console.WriteLine($"explorerbrowser-registered-namespace: nested-pidl-matches=True; nested-row-count={nestedRowCount}; parent-pidl-matches=True; travel-log-back=True; travel-log-forward=True; recursive-column-visible-in-child={recursiveColumnVisibleInChild}; recursive-column-visible-after-forward={recursiveColumnVisibleAfterForward}; nested-size-after-forward={nestedRecursiveSizeAfterForward}; recursive-column-and-value-restored={recursiveColumnAndValueRestored}");
                }
                finally
                {
                    if (directlyBoundNestedFolder != 0) Marshal.Release(directlyBoundNestedFolder);
                    if (absoluteNestedNamePidl != 0) Marshal.FreeCoTaskMem(absoluteNestedNamePidl);
                    Marshal.FreeCoTaskMem(expectedNestedRoot);
                }

                NavigateAndWaitForFolder(browser, rootPidl, 0, classId,
                    rootPidl, ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var rootRowCountAfterRename));
                if (rootRowCountAfterRename != 5)
                    throw new InvalidOperationException("Parent navigation did not return to the registered namespace root.");
                Console.WriteLine($"explorerbrowser-registered-namespace: child-pidl-matches=True; child-row-count={childRowCount}; return-to-root=True; root-row-count-after-notification={rootRowCountAfterRename}");
            }
            finally { Marshal.FreeCoTaskMem(expectedChildRoot); }

            var fileOperationResults = ExerciseWrappedDataObjectFileOperations(viewFolder, fixture);
            Console.WriteLine($"explorerbrowser-registered-namespace: wrapped-IDataObject-copy={fileOperationResults.Copy}; move={fileOperationResults.Move}; copy-source-preserved={fileOperationResults.CopySourcePreserved}; move-source-removed={fileOperationResults.MoveSourceRemoved}; destinations-verified={fileOperationResults.DestinationsVerified}; ui-suppressed=True");
            if (!fileOperationResults.Copy || !fileOperationResults.Move ||
                !fileOperationResults.CopySourcePreserved || !fileOperationResults.MoveSourceRemoved ||
                !fileOperationResults.DestinationsVerified)
                throw new InvalidOperationException("IFileOperation could not copy and move files supplied by the wrapped namespace IDataObject.");

            var wrappedDropTargetHr = CreateViewObject(viewFolder, DropTargetId, out var wrappedDropTarget);
            if (wrappedDropTarget != 0) Marshal.Release(wrappedDropTarget);
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out var dragBackingRootPidl, 0, out _));
            try
            {
                var shellFolderId = ShellFolder2Id;
                Marshal.ThrowExceptionForHR(SHBindToObject(0, dragBackingRootPidl, 0, ref shellFolderId, out var backingFolder));
                try
                {
                    var backingDropTargetHr = CreateViewObject(backingFolder, DropTargetId, out var backingDropTarget);
                    if (backingDropTarget != 0) Marshal.Release(backingDropTarget);
                    Console.WriteLine($"explorerbrowser-registered-namespace: wrapped-drop-target-hr=0x{wrappedDropTargetHr:X8}; backing-filesystem-drop-target-hr=0x{backingDropTargetHr:X8}");
                }
                finally { Marshal.Release(backingFolder); }
            }
            finally { Marshal.FreeCoTaskMem(dragBackingRootPidl); }

            var dropCopyResults = ExerciseWrappedFolderDropTarget(viewFolder, "child", "copy-source.txt", fixture, move: false);
            var dropMoveResults = ExerciseWrappedFolderDropTarget(viewFolder, "child", "drag-move-source.txt", fixture, move: true);
            Console.WriteLine($"explorerbrowser-registered-namespace: child-drop-target={dropCopyResults.TargetAvailable && dropMoveResults.TargetAvailable}; copy-enter={dropCopyResults.DragEnter}; copy-drop={dropCopyResults.Drop}; copy-verified={dropCopyResults.TransferVerified && dropCopyResults.SourcePreserved}; move-enter={dropMoveResults.DragEnter}; move-drop={dropMoveResults.Drop}; move-verified={dropMoveResults.TransferVerified && !dropMoveResults.SourcePreserved}");
            if (!dropCopyResults.TargetAvailable || !dropCopyResults.DragEnter || !dropCopyResults.Drop ||
                !dropCopyResults.TransferVerified || !dropCopyResults.SourcePreserved)
                throw new InvalidOperationException("The wrapped child folder could not accept a native Shell copy drop operation.");

            PrivateDesktopShellProbe.ReportStage("namespace-probe: invoke-native-background-new-folder");
            Marshal.ThrowExceptionForHR(At<GetWindow>(shellView, 3)(shellView, out var nativeShellViewWindow));
            var createdFolder = InvokeBackgroundNewFolderViaProductCommand(shellView, folderView, nativeShellViewWindow, fixture);
            Console.WriteLine($"explorerbrowser-registered-namespace: background-newfolder-view-route=True; created={createdFolder.Name}; filesystem-created=True; shell-row-count={createdFolder.Before}->{createdFolder.After}; row-updated={createdFolder.After == createdFolder.Before + 1}");
            if (createdFolder.After != createdFolder.Before + 1)
                throw new InvalidOperationException("The native New Folder command created the backing directory but the active Shell view did not add exactly one row.");
            VerifyExplorerBrowserDestroyDuringRecursiveScan(browser, shellView, viewFolder, nativeLibrary,
                rootPidl, fixture, ref initialized);
            return 0;
        }
        finally
        {
            PrivateDesktopShellProbe.ReportStage("namespace-probe: destroy-browser");
            if (currentPidl != 0) Marshal.FreeCoTaskMem(currentPidl);
            if (persist != 0) Marshal.Release(persist);
            if (viewFolder != 0) Marshal.Release(viewFolder);
            if (folderView != 0) Marshal.Release(folderView);
            if (shellView != 0) Marshal.Release(shellView);
            if (initialized != 0 && browser != 0) _ = At<Destroy>(browser, 4)(browser);
            if (browser != 0) Marshal.Release(browser);
            // ExplorerBrowser parent destruction can block; this HWND belongs to the private Desktop.
            if (parent != 0 && IsWindow(parent))
                Console.WriteLine("explorerbrowser-registered-namespace: parent-destroy-deferred-to-private-desktop-process-exit=True");
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            PrivateDesktopShellProbe.ReportStage("namespace-probe: unregister-temporary-namespace");
            if (namespaceRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            var registrationsRemoved = Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is null &&
                Registry.CurrentUser.OpenSubKey(classKeyPath) is null;
            Console.WriteLine($"explorerbrowser-registered-namespace: temporary-registrations-removed={registrationsRemoved}");
            if (nativeLibrary != 0)
            {
                _ = Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                    NativeLibrary.GetExport(nativeLibrary, "SetSyntheticChildNotificationTranslationForProbe"))(0);
                var canUnload = Marshal.GetDelegateForFunctionPointer<CanUnload>(NativeLibrary.GetExport(nativeLibrary, "DllCanUnloadNow"))();
                if (canUnload == 0) NativeLibrary.Free(nativeLibrary);
                else Console.WriteLine($"explorerbrowser-registered-namespace: native-library-retained-until-process-exit=0x{canUnload:X8}");
            }
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void VerifyExplorerBrowserDestroyDuringRecursiveScan(nint browser, nint shellView,
        nint viewFolder, nint nativeLibrary, nint rootPidl, string fixture, ref uint initialized)
    {
        var setOnDemand = Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
            NativeLibrary.GetExport(nativeLibrary, "SetSyntheticRecursiveSizeScanOnDemandForProbe"));
        var setAsyncScan = Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
            NativeLibrary.GetExport(nativeLibrary, "SetSyntheticAsyncRecursiveSizeScanForProbe"));
        var beginGate = Marshal.GetDelegateForFunctionPointer<ProbeActionResult>(
            NativeLibrary.GetExport(nativeLibrary, "BeginSyntheticAsyncRecursiveSizeQueueGateForProbe"));
        var releaseGate = Marshal.GetDelegateForFunctionPointer<ProbeAction>(
            NativeLibrary.GetExport(nativeLibrary, "ReleaseSyntheticAsyncRecursiveSizeQueueGateForProbe"));
        var clearGate = Marshal.GetDelegateForFunctionPointer<ProbeAction>(
            NativeLibrary.GetExport(nativeLibrary, "ClearSyntheticAsyncRecursiveSizeQueueGateForProbe"));
        var hasReservation = Marshal.GetDelegateForFunctionPointer<HasChildScanReservation>(
            NativeLibrary.GetExport(nativeLibrary, "HasSyntheticAsyncRecursiveSizeScanPendingForViewProbe"));
        var retireView = Marshal.GetDelegateForFunctionPointer<RetireShellView>(
            NativeLibrary.GetExport(nativeLibrary, "RetireSyntheticSortViewHoldForProbe"));
        var getFinished = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
            NativeLibrary.GetExport(nativeLibrary, "GetSyntheticAsyncRecursiveSizeScanFinishedCountForProbe"));
        var getCompleted = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
            NativeLibrary.GetExport(nativeLibrary, "GetSyntheticAsyncRecursiveSizeScanCompletedCountForProbe"));
        var getScanCount = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
            NativeLibrary.GetExport(nativeLibrary, "GetSyntheticOnDemandSizeScanCount"));
        var getSnapshotCount = Marshal.GetDelegateForFunctionPointer<GetProbeCount>(
            NativeLibrary.GetExport(nativeLibrary, "GetSyntheticRecursiveSizeCacheSnapshotCountForProbe"));
        var hasSnapshot = Marshal.GetDelegateForFunctionPointer<HasChildScanReservation>(
            NativeLibrary.GetExport(nativeLibrary, "HasSyntheticRecursiveSizeSnapshotForViewProbe"));
        var finishedBefore = getFinished();
        var completedBefore = getCompleted();
        var scansBefore = getScanCount();
        var snapshotsBefore = getSnapshotCount();
        nint peerBrowser = 0, peerParent = 0, peerShellView = 0, peerFolderView = 0, peerViewFolder = 0, peerPersist = 0;
        uint peerInitialized = 0;
        var destroyedBrowser = false;
        var scheduledScans = 0;

        try
        {
            // Open both views before creating the target so startup enumeration cannot warm its snapshot.
            var browserClass = BrowserClassId;
            var browserId = BrowserInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out peerBrowser));
            peerParent = CreateWindowExW(0, "STATIC", "ExplorerDirs second registered namespace browser probe",
                0x90000000, 40, 40, 820, 620, 0, 0, 0, 0); // Visible only on the isolated probe Desktop.
            if (peerParent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var peerBounds = new ProbeRect { Right = 820, Bottom = 620 };
            var peerSettings = new ProbeFolderSettings { Mode = 4 };
            Marshal.ThrowExceptionForHR(At<Initialize>(peerBrowser, 3)(peerBrowser, peerParent, ref peerBounds, ref peerSettings));
            peerInitialized = 1;
            Marshal.ThrowExceptionForHR(At<BrowseToIdList>(peerBrowser, 13)(peerBrowser, rootPidl, 0));
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();
            ReadActiveFolder(peerBrowser, ref peerShellView, ref peerFolderView, ref peerViewFolder, ref peerPersist);

            const string scanChildName = "lifecycle-scan";
            var scanChildPath = Path.Combine(fixture, scanChildName);
            Directory.CreateDirectory(Path.Combine(scanChildPath, "nested"));
            File.WriteAllBytes(Path.Combine(scanChildPath, "nested", "payload.bin"), [1, 2, 3]);
            Marshal.ThrowExceptionForHR(setOnDemand(1));
            Marshal.ThrowExceptionForHR(setAsyncScan(1));
            Marshal.ThrowExceptionForHR(beginGate());
            var pendingValue = ReadParsedItemDetailsByName(viewFolder, scanChildName, 1);
            if (pendingValue.Length != 0)
                throw new InvalidOperationException($"The recursive-size value should be pending behind the scan gate, got '{pendingValue}'.");
            var deadline = Environment.TickCount64 + 5000;
            while (hasReservation(shellView, scanChildName) == 0 && Environment.TickCount64 < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (hasReservation(shellView, scanChildName) == 0)
                throw new TimeoutException("The registered Shell provider did not queue the requested recursive-size scan.");
            scheduledScans++;

            var peerPendingValue = ReadParsedItemDetailsByName(peerViewFolder, scanChildName, 1);
            if (peerPendingValue.Length != 0)
                throw new InvalidOperationException($"The second view should also be waiting for the uncached recursive-size value, got '{peerPendingValue}'.");
            deadline = Environment.TickCount64 + 5000;
            while (hasReservation(peerShellView, scanChildName) == 0 && Environment.TickCount64 < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (hasReservation(peerShellView, scanChildName) == 0)
                throw new TimeoutException("The second Shell provider did not queue its same-directory recursive-size request.");
            scheduledScans++;

            PrivateDesktopShellProbe.ReportStage("namespace-probe: destroy-browser-with-recursive-scan-pending");
            Marshal.ThrowExceptionForHR(At<Destroy>(browser, 4)(browser));
            initialized = 0;
            destroyedBrowser = true;
            Marshal.ThrowExceptionForHR(retireView(shellView));
            releaseGate();
            var finishDeadline = Environment.TickCount64 + 5000;
            while (getFinished() < finishedBefore + scheduledScans && Environment.TickCount64 < finishDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            var finishedCount = getFinished() - finishedBefore;
            var completedCount = getCompleted() - completedBefore;
            var scansAfterRetire = getScanCount();
            var snapshotsAfterRetire = getSnapshotCount();
            var peerValueAfterRetirement = ReadParsedItemDetailsByName(peerViewFolder, scanChildName, 1);
            var survivingViewHasSnapshot = hasSnapshot(peerShellView, scanChildName) != 0;
            if (finishedCount != 2 || completedCount != 1 || scansAfterRetire != scansBefore + 1 ||
                snapshotsAfterRetire < snapshotsBefore + 1 || !survivingViewHasSnapshot || peerValueAfterRetirement != "3")
                throw new InvalidOperationException($"Retiring one of two same-root views did not isolate cancellation: finished={finishedCount}/2; completed={completedCount}/1; scans={scansBefore}->{scansAfterRetire}; snapshots={snapshotsBefore}->{snapshotsAfterRetire}; surviving-view-value='{peerValueAfterRetirement}'.");
            Console.WriteLine($"explorerbrowser-registered-namespace-scan-lifecycle: same-root-provider-requests=2; first-browser-destroyed=True; retired-view-request-canceled=True; surviving-view-request-completed=True; finished={finishedCount}; completed={completedCount}; scan-count={scansBefore}->{scansAfterRetire}; cache-count={snapshotsBefore}->{snapshotsAfterRetire}; surviving-view-has-target-snapshot={survivingViewHasSnapshot}; surviving-view-value={peerValueAfterRetirement}");
        }
        finally
        {
            if (initialized != 0 && browser != 0)
            {
                _ = At<Destroy>(browser, 4)(browser);
                initialized = 0;
                destroyedBrowser = true;
            }
            if (destroyedBrowser) _ = retireView(shellView);
            if (peerInitialized != 0 && peerBrowser != 0)
            {
                _ = At<Destroy>(peerBrowser, 4)(peerBrowser);
                peerInitialized = 0;
            }
            if (peerShellView != 0) _ = retireView(peerShellView);
            releaseGate();
            var cleanupDeadline = Environment.TickCount64 + 10000;
            while (getFinished() < finishedBefore + scheduledScans && Environment.TickCount64 < cleanupDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (getFinished() >= finishedBefore + scheduledScans) clearGate();
            else Console.WriteLine("explorerbrowser-registered-namespace-scan-lifecycle: scan-gate-retained-until-process-exit=True");
            _ = setOnDemand(0);
            _ = setAsyncScan(0);
            if (peerPersist != 0) Marshal.Release(peerPersist);
            if (peerViewFolder != 0) Marshal.Release(peerViewFolder);
            if (peerFolderView != 0) Marshal.Release(peerFolderView);
            if (peerShellView != 0) Marshal.Release(peerShellView);
            if (peerBrowser != 0) Marshal.Release(peerBrowser);
        }
    }

    private static void RegisterSyntheticFolder(string classKeyPath)
    {
        using var classes = Registry.CurrentUser.OpenSubKey("Software\\Classes", writable: true)
            ?? throw new InvalidOperationException("HKCU\\Software\\Classes is unavailable.");
        using var existing = classes.OpenSubKey(classKeyPath["Software\\Classes\\".Length..]);
        if (existing is not null) throw new InvalidOperationException("The random probe CLSID already exists; refusing to overwrite it.");
        try
        {
            using var classKey = Registry.CurrentUser.CreateSubKey(classKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Shell-folder CLSID key.");
            classKey.SetValue(null, "ExplorerDirs isolated ExplorerBrowser namespace probe", RegistryValueKind.String);
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
        if (Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null)
            throw new InvalidOperationException("The random probe namespace key already exists; refusing to overwrite it.");
        try
        {
            using var namespaceKey = Registry.CurrentUser.CreateSubKey(namespaceKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Desktop\\NameSpace key.");
            namespaceKey.SetValue(null, "ExplorerDirs isolated ExplorerBrowser namespace probe", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static bool PidlBytesEqual(nint left, nint right)
    {
        if (left == 0 || right == 0) return false;
        var leftSize = ILGetSize(left);
        var rightSize = ILGetSize(right);
        if (leftSize == 0 || leftSize != rightSize) return false;
        var leftBytes = new byte[leftSize];
        var rightBytes = new byte[rightSize];
        Marshal.Copy(left, leftBytes, 0, leftBytes.Length);
        Marshal.Copy(right, rightBytes, 0, rightBytes.Length);
        return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }

    private static int ParseDisplayName(nint folder, string name, out nint item)
    {
        uint eaten = 0, attributes = 0;
        return At<ParseDisplayNameNative>(folder, 3)(folder, 0, 0, name, out eaten, out item, ref attributes);
    }

    private static int CreateViewObject(nint folder, Guid iid, out nint value)
        => At<CreateViewObjectNative>(folder, 8)(folder, 0, ref iid, out value);

    private static string ReadViewItemDetailsByName(nint folderView, nint folder, string expectedName, uint column)
    {
        Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var count));
        for (var index = 0; index < count; index++)
        {
            Marshal.ThrowExceptionForHR(At<GetItemPidl>(folderView, 6)(folderView, index, out var item));
            try
            {
                var itemName = ReadShellDetailsText(folder, item, 0);
                if (string.Equals(itemName, expectedName, StringComparison.OrdinalIgnoreCase))
                    return ReadShellDetailsText(folder, item, column);
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        return "<missing>";
    }

    private static string ReadParsedItemDetailsByName(nint folder, string name, uint column)
    {
        Marshal.ThrowExceptionForHR(ParseDisplayName(folder, name, out var item));
        try { return ReadShellDetailsText(folder, item, column); }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    private static ulong ReadViewItemPidlSizeByName(nint folderView, nint folder, string name)
    {
        Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var count));
        for (var index = 0; index < count; index++)
        {
            Marshal.ThrowExceptionForHR(At<GetItemPidl>(folderView, 6)(folderView, index, out var item));
            try
            {
                if (string.Equals(ReadShellDetailsText(folder, item, 0), name, StringComparison.OrdinalIgnoreCase))
                    return unchecked((ulong)Marshal.ReadInt64(item, sizeof(ulong)));
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        return ulong.MaxValue;
    }

    private static string[] ReadViewItemNamesInOrder(nint folderView, nint folder)
    {
        Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var count));
        var names = new string[count];
        for (var index = 0; index < count; index++)
        {
            Marshal.ThrowExceptionForHR(At<GetItemPidl>(folderView, 6)(folderView, index, out var item));
            try { names[index] = ReadShellDetailsText(folder, item, 0); }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        return names;
    }

    private static string ReadShellDetailsText(nint folder, nint item, uint column)
    {
        var details = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeShellDetails>());
        try
        {
            Marshal.StructureToPtr(default(NativeShellDetails), details, false);
            Marshal.ThrowExceptionForHR(At<GetDetailsOf>(folder, 18)(folder, item, column, details));
            var value = Marshal.PtrToStructure<NativeShellDetails>(details);
            if (value.Text.Type != 0 || value.Text.Pointer == 0) return "<non-WSTR>";
            var text = Marshal.PtrToStringUni(value.Text.Pointer) ?? "";
            Marshal.FreeCoTaskMem(value.Text.Pointer);
            return text;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static int GetUiObjectOf(nint folder, nint item, Guid iid, out nint value)
    {
        var items = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(items, item);
            uint reserved = 0;
            return At<GetUiObjectOfNative>(folder, 10)(folder, 0, 1, items, ref iid, ref reserved, out value);
        }
        finally { Marshal.FreeHGlobal(items); }
    }

    private static uint InvokeNativeDeleteWithoutUi(nint folder, string name)
    {
        Marshal.ThrowExceptionForHR(ParseDisplayName(folder, name, out var item));
        nint contextMenu = 0, menu = 0;
        try
        {
            Marshal.ThrowExceptionForHR(GetUiObjectOf(folder, item, ContextMenuId, out contextMenu));
            menu = CreatePopupMenu();
            if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7FFF, 0x10 | 0x800);
            Marshal.ThrowExceptionForHR(queryResult);
            var commandCount = queryResult & 0xFFFF;
            var verbBuffer = Marshal.AllocHGlobal(512);
            try
            {
                for (uint commandOffset = 0; commandOffset < commandCount; commandOffset++)
                {
                    Marshal.WriteInt16(verbBuffer, 0);
                    if (At<GetCommandString>(contextMenu, 5)(contextMenu, commandOffset, 4, 0, verbBuffer, 256) != 0)
                        continue;
                    var verb = Marshal.PtrToStringUni(verbBuffer);
                    if (!string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) continue;

                    var invokeInfo = new NativeInvokeInfo
                    {
                        Size = Marshal.SizeOf<NativeInvokeInfo>(),
                        // This generated fixture item is disposable; request permanent deletion without a modal confirmation.
                        Mask = 0x00000400 | 0x00000100, // CMIC_MASK_FLAG_NO_UI | CMIC_MASK_SHIFT_DOWN
                        Verb = (nint)commandOffset,
                        Show = 0
                    };
                    Marshal.ThrowExceptionForHR(At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invokeInfo));
                    return commandOffset;
                }
            }
            finally { Marshal.FreeHGlobal(verbBuffer); }
            throw new InvalidOperationException("The native item menu did not expose a canonical Delete verb.");
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
            Marshal.FreeCoTaskMem(item);
        }
    }

    private static (string Name, int Before, int After) InvokeBackgroundNewFolderViaProductCommand(nint view,
        nint folderView, nint ownerWindow, string backingPath)
    {
        var expectedInitialRowCount = Directory.EnumerateFileSystemEntries(backingPath).Count();
        var initialDeadline = Environment.TickCount64 + 5000;
        var initialRowCount = -1;
        while (initialRowCount != expectedInitialRowCount && Environment.TickCount64 < initialDeadline)
        {
            Application.DoEvents();
            Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out initialRowCount)); // SVGIO_ALLVIEW.
            if (initialRowCount != expectedInitialRowCount) Thread.Sleep(50);
        }
        if (initialRowCount != expectedInitialRowCount)
            throw new InvalidOperationException($"The Shell view was not settled before New Folder: rows={initialRowCount}; backing-items={expectedInitialRowCount}.");
        var existingDirectories = Directory.EnumerateDirectories(backingPath)
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invokeResult = ShellFileCommands.Invoke(ShellFileCommand.NewFolder, background =>
        {
            if (!background) return null;
            var iid = ContextMenuId;
            Marshal.ThrowExceptionForHR(At<GetItemObject>(view, 15)(view, 0, ref iid, out var contextMenu)); // SVGIO_BACKGROUND.
            try
            {
                return new NativeShellCommandMenu(Marshal.GetObjectForIUnknown(contextMenu), ownerWindow);
            }
            finally { Marshal.Release(contextMenu); }
        });
        if (invokeResult is null)
            throw new InvalidOperationException("The production Shell command adapter did not enable New Folder on the isolated writable folder.");
        Marshal.ThrowExceptionForHR(invokeResult.Value);

        var deadline = Environment.TickCount64 + 5000;
        string[] createdDirectories;
        do
        {
            Application.DoEvents();
            createdDirectories = Directory.EnumerateDirectories(backingPath)
                .Select(Path.GetFullPath)
                .Where(path => !existingDirectories.Contains(path))
                .ToArray();
            if (createdDirectories.Length > 1)
                throw new InvalidOperationException($"The native New Folder command created {createdDirectories.Length} new backing directories; expected one.");
            if (createdDirectories.Length == 0) Thread.Sleep(50);
        } while (createdDirectories.Length == 0 && Environment.TickCount64 < deadline);
        if (createdDirectories.Length != 1)
            throw new InvalidOperationException("The native New Folder command returned without creating a backing directory.");

        var rowDeadline = Environment.TickCount64 + 5000;
        var updatedRowCount = initialRowCount;
        while (updatedRowCount != initialRowCount + 1 && Environment.TickCount64 < rowDeadline)
        {
            Application.DoEvents();
            Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out updatedRowCount));
            if (updatedRowCount != initialRowCount + 1) Thread.Sleep(50);
        }

        // Native New Folder may start inline rename; cancel it before leaving the isolated fixture.
        var edit = FindChildByClass(ownerWindow, "Edit");
        if (edit != 0)
        {
            _ = SendMessageW(edit, 0x0100, (nint)0x1B, (nint)0x00010001); // WM_KEYDOWN / VK_ESCAPE.
            _ = SendMessageW(edit, 0x0101, (nint)0x1B, unchecked((nint)0xC0010001u)); // WM_KEYUP / VK_ESCAPE.
            Application.DoEvents();
            if (IsWindow(edit)) throw new InvalidOperationException("The inline New Folder rename editor remained open after Escape.");
        }
        return (Path.GetFileName(createdDirectories[0]), initialRowCount, updatedRowCount);
    }

    private static (bool Copy, bool Move, bool CopySourcePreserved, bool MoveSourceRemoved, bool DestinationsVerified)
        ExerciseWrappedDataObjectFileOperations(nint folder, string fixture)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("IFileOperation requires the isolated Shell probe to run in an STA.");
        var destinationPath = Path.Combine(fixture, "file-operation-destination");
        Directory.CreateDirectory(destinationPath);
        var copySourcePath = Path.Combine(fixture, "copy-source.txt");
        var moveSourcePath = Path.Combine(fixture, "file-operation-move-source.txt");
        var dragMoveSourcePath = Path.Combine(fixture, "drag-move-source.txt");
        const string copyContents = "copied through namespace IDataObject";
        const string moveContents = "moved through namespace IDataObject";
        File.WriteAllText(copySourcePath, copyContents);
        File.WriteAllText(moveSourcePath, moveContents);
        File.WriteAllText(dragMoveSourcePath, "moved through namespace IDropTarget");
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(destinationPath, 0, in ShellItemId, out var destination));
        try
        {
            var copyData = GetDataObjectForItem(folder, "copy-source.txt");
            bool copy;
            try { copy = RunFileOperation(copyData, destination, move: false); }
            finally { Marshal.Release(copyData); }

            var moveData = GetDataObjectForItem(folder, "file-operation-move-source.txt");
            bool move;
            try { move = RunFileOperation(moveData, destination, move: true); }
            finally { Marshal.Release(moveData); }

            var copySourcePreserved = File.Exists(copySourcePath);
            var moveSourceRemoved = !File.Exists(moveSourcePath);
            var copyDestinationPath = Path.Combine(destinationPath, "copy-source.txt");
            var moveDestinationPath = Path.Combine(destinationPath, "file-operation-move-source.txt");
            var destinationsVerified = File.Exists(copyDestinationPath) && File.ReadAllText(copyDestinationPath) == copyContents &&
                File.Exists(moveDestinationPath) && File.ReadAllText(moveDestinationPath) == moveContents;
            return (copy, move, copySourcePreserved, moveSourceRemoved, destinationsVerified);
        }
        finally { Marshal.Release(destination); }
    }

    internal static int RunExternalShellNotificationChild(string path, string base64Contents)
    {
        var contents = Convert.FromBase64String(base64Contents);
        File.WriteAllBytes(path, contents);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var itemPidl, 0, out _));
        try { SHChangeNotify(0x00000002, 0, itemPidl, 0); } // SHCNE_CREATE / SHCNF_IDLIST
        finally { Marshal.FreeCoTaskMem(itemPidl); }

        var parentPath = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The created file did not have a parent directory.");
        Marshal.ThrowExceptionForHR(SHParseDisplayName(parentPath, 0, out var parentPidl, 0, out _));
        try { SHChangeNotify(0x00001000, 0, parentPidl, 0); } // SHCNE_UPDATEDIR / SHCNF_IDLIST
        finally { Marshal.FreeCoTaskMem(parentPidl); }
        Console.WriteLine($"external-shell-notification-child: created=True; item-and-parent-notified=True; pid={Environment.ProcessId}");
        return 0;
    }

    private static int RunExternalShellNotificationChild(string path, byte[] contents)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(ExplorerBrowserWrapperProbe).Assembly.Location);
        start.ArgumentList.Add("--shell-change-create-file-child");
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(Convert.ToBase64String(contents));
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the external Shell-notification process.");
        if (!child.WaitForExit(15_000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new TimeoutException("The external Shell-notification process did not exit within 15 seconds.");
        }
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output)) Console.Write(output);
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.Write(error);
        return child.ExitCode;
    }

    private static nint GetDataObjectForItem(nint folder, string name)
    {
        Marshal.ThrowExceptionForHR(ParseDisplayName(folder, name, out var item));
        try
        {
            Marshal.ThrowExceptionForHR(GetUiObjectOf(folder, item, DataObjectId, out var dataObject));
            if (dataObject == 0) throw new InvalidOperationException($"The wrapped namespace returned no IDataObject for '{name}'.");
            return dataObject;
        }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    private static (bool TargetAvailable, bool DragEnter, bool Drop, bool TransferVerified, bool SourcePreserved) ExerciseWrappedFolderDropTarget(
        nint parentFolder, string destinationName, string sourceName, string fixture, bool move)
    {
        Marshal.ThrowExceptionForHR(ParseDisplayName(parentFolder, destinationName, out var childPidl));
        try
        {
            var shellFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(At<BindToObjectNative>(parentFolder, 5)(parentFolder, childPidl, 0, ref shellFolderId, out var childFolder));
            try
            {
                var targetHr = CreateViewObject(childFolder, DropTargetId, out var dropTarget);
                if (targetHr < 0 || dropTarget == 0) return (false, false, false, false, false);
                try
                {
                    var dataObject = GetDataObjectForItem(parentFolder, sourceName);
                    try
                    {
                        var requestedEffect = move ? 2u : 1u;
                        var keyState = move ? 5u : 1u; // MK_LBUTTON | MK_SHIFT requests a move.
                        var sourcePath = Path.Combine(fixture, sourceName);
                        var destinationPath = Path.Combine(fixture, destinationName, sourceName);
                        var expectedContents = File.ReadAllBytes(sourcePath);
                        var effect = requestedEffect;
                        var point = new ProbePoint();
                        var dragEnterHr = At<DropDragEnter>(dropTarget, 3)(dropTarget, dataObject, keyState, point, ref effect);
                        var dragEnter = dragEnterHr >= 0 && (effect & requestedEffect) != 0;
                        var enterEffect = effect;
                        var drop = false;
                        var dropHr = unchecked((int)0x80004005);
                        if (dragEnter)
                        {
                            effect = requestedEffect;
                            dropHr = At<DropTargetDrop>(dropTarget, 6)(dropTarget, dataObject, keyState, point, ref effect);
                            drop = dropHr >= 0 &&
                                (effect & requestedEffect) != 0;
                        }
                        else _ = At<DropTargetDragLeave>(dropTarget, 5)(dropTarget);
                        // Direct Drop calls omit DoDragDrop's source-side completion; MOVE remains diagnostic-only.
                        Console.WriteLine($"explorerbrowser-registered-namespace: drop-kind={(move ? "move" : "copy")}; drag-enter-hr=0x{dragEnterHr:X8}; enter-effect={enterEffect}; drop-hr=0x{dropHr:X8}; drop-effect={effect}");
                        var sourcePreserved = File.Exists(sourcePath);
                        var transferVerified = drop && sourcePreserved == !move && File.Exists(destinationPath) &&
                            expectedContents.AsSpan().SequenceEqual(File.ReadAllBytes(destinationPath));
                        return (true, dragEnter, drop, transferVerified, sourcePreserved);
                    }
                    finally { Marshal.Release(dataObject); }
                }
                finally { Marshal.Release(dropTarget); }
            }
            finally { Marshal.Release(childFolder); }
        }
        finally { Marshal.FreeCoTaskMem(childPidl); }
    }

    private static bool RunFileOperation(nint dataObject, nint destination, bool move)
    {
        var operationClass = FileOperationClassId;
        var operationInterface = FileOperationInterfaceId;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref operationClass, 0, 1, ref operationInterface, out var operation));
        try
        {
            // FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI; the destination is unique and pre-created.
            Marshal.ThrowExceptionForHR(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0010 | 0x0400));
            Marshal.ThrowExceptionForHR(move
                ? At<TransferItems>(operation, 15)(operation, dataObject, destination)
                : At<TransferItems>(operation, 17)(operation, dataObject, destination));
            Marshal.ThrowExceptionForHR(At<PerformOperations>(operation, 21)(operation));
            Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            return aborted == 0;
        }
        finally { Marshal.Release(operation); }
    }

    private static void ReadActiveFolder(nint browser, ref nint shellView, ref nint folderView, ref nint viewFolder, ref nint persist)
    {
        var shellViewId = ShellViewId;
        Marshal.ThrowExceptionForHR(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
        var folderView2Id = FolderView2Id;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
        var viewFolderId = ShellFolder2Id;
        Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref viewFolderId, out viewFolder));
        var persistId = PersistFolder2Id;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(viewFolder, in persistId, out persist));
    }

    private static void NavigateAndWaitForFolder(nint browser, nint targetPidl, uint flags, Guid expectedClassId,
        nint expectedPidl, ref nint shellView, ref nint folderView, ref nint viewFolder, ref nint persist,
        ref nint currentPidl)
    {
        ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
        var browseHr = At<BrowseToIdList>(browser, 13)(browser, targetPidl, flags);
        Marshal.ThrowExceptionForHR(browseHr);
        Console.WriteLine($"explorerbrowser-registered-namespace: navigation-request flags=0x{flags:X8}; browse-hr=0x{browseHr:X8}; expected={ReadAbsoluteParsingName(expectedPidl)}");
        var stopwatch = Stopwatch.StartNew();
        var actualClassId = Guid.Empty;
        var actualLocation = "<unavailable>";
        while (true)
        {
            Application.DoEvents();
            ReadActiveFolder(browser, ref shellView, ref folderView, ref viewFolder, ref persist);
            Marshal.ThrowExceptionForHR(At<GetClassId>(persist, 3)(persist, out actualClassId));
            Marshal.ThrowExceptionForHR(At<GetCurFolder>(persist, 5)(persist, out currentPidl));
            actualLocation = ReadAbsoluteParsingName(currentPidl);
            if (actualClassId == expectedClassId && PidlBytesEqual(expectedPidl, currentPidl)) return;

            ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
            if (stopwatch.Elapsed >= TimeSpan.FromSeconds(5))
                throw new TimeoutException($"ExplorerBrowser navigation mismatch: flags=0x{flags:X8}; expected-class={expectedClassId}; actual-class={actualClassId}; expected={ReadAbsoluteParsingName(expectedPidl)}; actual={actualLocation}.");
            Thread.Sleep(20);
        }
    }

    private static string ReadAbsoluteParsingName(nint pidl)
        => ReadShellName(pidl, SigdnDesktopAbsoluteParsing);

    private static string ReadShellName(nint pidl, uint nameType)
    {
        if (pidl == 0 || SHGetNameFromIDList(pidl, nameType, out var pointer) < 0)
            return "<unavailable>";
        try { return Marshal.PtrToStringUni(pointer) ?? "<empty>"; }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static (int HResult, bool ClassMatches, bool PidlMatches, string Location, string FileSystemPath)
        ProbeBrowseToObject(nint browser, nint targetFolder, Guid expectedClassId, nint expectedPidl,
            ref nint shellView, ref nint folderView, ref nint viewFolder, ref nint persist, ref nint currentPidl)
    {
        ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
        var browseHr = At<BrowseToObject>(browser, 14)(browser, targetFolder, 0);
        if (browseHr < 0) return (browseHr, false, false, "<browse-failed>", "<browse-failed>");

        var classMatches = false;
        var pidlMatches = false;
        var location = "<unavailable>";
        var fileSystemPath = "<unavailable>";
        var stopwatch = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            ReadActiveFolder(browser, ref shellView, ref folderView, ref viewFolder, ref persist);
            Marshal.ThrowExceptionForHR(At<GetClassId>(persist, 3)(persist, out var actualClassId));
            Marshal.ThrowExceptionForHR(At<GetCurFolder>(persist, 5)(persist, out currentPidl));
            classMatches = actualClassId == expectedClassId;
            pidlMatches = PidlBytesEqual(expectedPidl, currentPidl);
            location = ReadAbsoluteParsingName(currentPidl);
            fileSystemPath = ReadShellName(currentPidl, SigdnFileSystemPath);
            if (classMatches && pidlMatches) break;
            ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
            Thread.Sleep(20);
        } while (stopwatch.Elapsed < TimeSpan.FromSeconds(5));

        ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
        return (browseHr, classMatches, pidlMatches, location, fileSystemPath);
    }

    private static int WaitForViewItemCount(nint folderView, int expectedCount)
    {
        // ExplorerBrowser publishes the active PIDL before the new view finishes enumerating its rows.
        var stopwatch = Stopwatch.StartNew();
        int actualCount;
        do
        {
            Application.DoEvents();
            Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out actualCount));
            if (actualCount == expectedCount) return actualCount;
            Thread.Sleep(20);
        } while (stopwatch.Elapsed < TimeSpan.FromSeconds(5));

        throw new TimeoutException($"ExplorerBrowser view item count did not reach {expectedCount}; actual={actualCount}.");
    }

    private static void ReleaseActiveFolder(ref nint shellView, ref nint folderView, ref nint viewFolder,
        ref nint persist, ref nint currentPidl)
    {
        if (currentPidl != 0) { Marshal.FreeCoTaskMem(currentPidl); currentPidl = 0; }
        if (persist != 0) { Marshal.Release(persist); persist = 0; }
        if (viewFolder != 0) { Marshal.Release(viewFolder); viewFolder = 0; }
        if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
        if (shellView != 0) { Marshal.Release(shellView); shellView = 0; }
    }

    private static int SetNameOf(nint folder, nint item, string name, out nint renamed)
        => At<SetNameOfNative>(folder, 12)(folder, 0, item, name, 0, out renamed);

    private static string ReadHeader(nint folder, uint column)
    {
        var details = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeShellDetails>());
        try
        {
            Marshal.StructureToPtr(default(NativeShellDetails), details, false);
            Marshal.ThrowExceptionForHR(At<GetDetailsOf>(folder, 18)(folder, 0, column, details));
            var value = Marshal.PtrToStructure<NativeShellDetails>(details);
            if (value.Text.Type != 0 || value.Text.Pointer == 0) return "<non-WSTR>";
            var text = Marshal.PtrToStringUni(value.Text.Pointer) ?? "";
            Marshal.FreeCoTaskMem(value.Text.Pointer);
            return text;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static nint FindChildByClass(nint parent, string className)
    {
        if (parent == 0) return 0;
        nint found = 0;
        _ = EnumChildWindows(parent, (child, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            if (GetClassNameW(child, name, name.Capacity) > 0 && name.ToString() == className)
            {
                found = child;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static string[] ReadNativeHeaderTitles(nint header)
    {
        var count = checked((int)SendMessageW(header, 0x1200, 0, 0)); // HDM_GETITEMCOUNT
        if (count < 0 || count > 64) throw new InvalidOperationException($"Invalid native header item count: {count}.");
        var text = Marshal.AllocHGlobal(512);
        try
        {
            var titles = new string[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt16(text, 0);
                var item = new NativeHeaderItem { Mask = 2, Text = text, TextCapacity = 256 };
                if (SendMessageW(header, 0x120B, index, ref item) == 0) // HDM_GETITEMW
                    throw new InvalidOperationException($"Could not read native header item {index}.");
                titles[index] = Marshal.PtrToStringUni(text) ?? string.Empty;
            }
            return titles;
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    private static string[] ReadChildClasses(nint parent)
    {
        if (parent == 0) return [];
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        _ = EnumChildWindows(parent, (child, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            if (GetClassNameW(child, name, name.Capacity) > 0) classes.Add(name.ToString());
            return true;
        }, 0);
        return [.. classes];
    }

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before this probe.");
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)] private struct ProbeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ProbeFolderSettings { public uint Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSortColumn
    {
        public ShellColumnManager.NativeKey Key;
        public int Direction;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeStrRet { public uint Type; public nint Pointer; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeShellDetails { public int Format, Width; public NativeStrRet Text; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeHeaderItem
    {
        public uint Mask;
        public int Width;
        public nint Text;
        public nint Bitmap;
        public int TextCapacity;
        public int Format;
        public nint Param;
        public int Image;
        public int Order;
        public uint Type;
        public nint Filter;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Window, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public nint Icon, Title, VerbW, ParametersW, DirectoryW, TitleW;
        public ProbePoint Point;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProbePoint { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateAdapter(nint folder, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref ProbeRect rect, ref ProbeFolderSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowseToObject(nint self, nint value, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentView(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemPidl(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurFolder(nint self, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetClassId(nint self, out Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MapColumnToScid(nint self, uint column, out ShellColumnManager.NativeKey key);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Rearrange(nint self, nint sortColumn);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowseToIdList(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint scope, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int ParseDisplayNameNative(nint self, nint owner, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out nint item, ref uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUiObjectOfNative(nint self, nint owner, uint count, nint items, ref Guid iid, ref uint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObjectNative(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BindToObjectNative(nint self, nint item, nint bindContext, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropDragEnter(nint self, nint dataObject, uint keyState, ProbePoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropTargetDrop(nint self, nint dataObject, uint keyState, ProbePoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropTargetDragLeave(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetNameOfNative(nint self, nint owner, nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint renamed);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint first, uint last, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint flags, nint reserved, nint buffer, uint bufferLength);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TransferItems(nint self, nint items, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetBackingPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetProbeFlag(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetProbeCount();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProbeActionResult();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ProbeAction();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int HasChildScanReservation(nint view, [MarshalAs(UnmanagedType.LPWStr)] string childName);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RetireShellView(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CanUnload();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string name, nint bindContext, in Guid iid, out nint item);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, ref Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool EnumChildProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder className, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, ref NativeHeaderItem item);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
}
