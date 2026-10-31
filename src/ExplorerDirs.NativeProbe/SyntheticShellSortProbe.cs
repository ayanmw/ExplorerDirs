using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ExplorerDirs.App;

public static class SyntheticShellSortProbe
{
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid SizeFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly Guid RecursiveSizeFormatId = new("7E1F84B7-C052-4E51-8A3C-869254A6B910");
    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmGetItemRect = LvmFirst + 14;
    private const int LvmEnsureVisible = LvmFirst + 19;
    private const int LvmGetHeader = LvmFirst + 31;
    private const int LvmSetItemState = LvmFirst + 43;
    private const int LvmGetItemState = LvmFirst + 44;
    private const int LvmGetTopIndex = LvmFirst + 39;
    private const int LvmGetItemTextW = LvmFirst + 115;
    private const uint ShcneUpdatedir = 0x00001000;
    private const uint ShcneUpdateitem = 0x00002000;
    private const uint ShcneCreate = 0x00000002;
    private const uint ShcneDelete = 0x00000004;
    private const uint ShcneRenameItem = 0x00000001;
    private const uint ShcneMakeDirectory = 0x00000008;
    private const uint ShcneRemoveDirectory = 0x00000010;
    private const uint ShcneAttributes = 0x00000800;
    private const uint ShcneRenameFolder = 0x00020000;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const uint LvifText = 0x0001;
    private const uint DvaspectContent = 1;
    private const uint TymedHGlobal = 1;

    internal static int RunFolderTreeContractSmoke()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-SyntheticShellTree-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            Directory.CreateDirectory(Path.Combine(fixture, "child", "nested-folder"));
            File.WriteAllText(Path.Combine(fixture, "root.txt"), "root");
            File.WriteAllText(Path.Combine(fixture, "second.txt"), "second");
            var hiddenFile = Path.Combine(fixture, "hidden.txt");
            File.WriteAllText(hiddenFile, "hidden");
            File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(fixture, "child", "nested.txt"), "nested");
            File.WriteAllText(Path.Combine(fixture, "child", "nested-folder", "deep.txt"), "deep");
            var module = NativeLibrary.Load(FindAdapterLibrary());
            try
            {
                var checkTree = Marshal.GetDelegateForFunctionPointer<FolderTreeContractSmoke>(
                    NativeLibrary.GetExport(module, "RunSyntheticFolderTreeContractSmoke"));
                Check(checkTree(fixture));
            }
            finally { NativeLibrary.Free(module); }
            Console.WriteLine("synthetic-shell-folder-tree-smoke: nested-enumeration=True; size-column-index-key-map=True; standard/custom-detail-values=True; multi-component-parse-bind-storage=True; native-item-menu=True; multi-select-CF_HDROP=True; CopyItems/MoveItems/DeleteItems=True; collision-rename-preserved=True; shell-view-created=False");
            return 0;
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    internal static int RunRecursiveDirectorySizeSort() =>
        Run(recursiveDirectorySizeSortOnly: true, selfOwnedAttributes: true, customSizeColumn: true, exerciseUpdateNotifications: true);

    internal static int RunRecursiveDirectorySizeSortWithSlowColumn() =>
        Run(recursiveDirectorySizeSortOnly: true, selfOwnedAttributes: true, customSizeColumn: true,
            slowCustomSizeColumn: true, exerciseUpdateNotifications: true);

    internal static int RunRecursiveDirectorySizeSortWithWpfHost() =>
        Run(recursiveDirectorySizeSortOnly: true, selfOwnedAttributes: true, customSizeColumn: true,
            useWpfParent: true, exerciseUpdateNotifications: true);

    internal static int RunNativeLibraryUnloadContractSmoke()
    {
        var module = NativeLibrary.Load(FindAdapterLibrary());
        try
        {
            var smoke = Marshal.GetDelegateForFunctionPointer<NativeLibraryUnloadContractSmoke>(
                NativeLibrary.GetExport(module, "RunSyntheticDllUnloadContractSmoke"));
            var result = smoke();
            Console.WriteLine($"synthetic-dll-unload-contract: result=0x{result:X8}; passed={result >= 0}");
            return result >= 0 ? 0 : 1;
        }
        finally { NativeLibrary.Free(module); }
    }

    internal static int Run(bool recursiveDirectorySizeSortOnly = false, bool maskFilesystemAttributes = false,
        bool selfOwnedAttributes = false, bool customSizeColumn = false, bool useWpfParent = false,
        bool exerciseUpdateNotifications = false, bool slowCustomSizeColumn = false)
    {
        var sizeUpdateStrategy = Environment.GetEnvironmentVariable("EXPLORER_DIRS_NATIVEPROBE_SIZE_UPDATE_STRATEGY") ?? "single-item";
        var shellWindowScrollProbe = sizeUpdateStrategy == "single-item-recreate-shell-window-scroll";
        if (exerciseUpdateNotifications && sizeUpdateStrategy == "all")
            throw new ArgumentException("Run each recursive-size update strategy in its own fresh probe process; 'all' cannot share a view baseline.");
        if (exerciseUpdateNotifications && sizeUpdateStrategy is not ("single-item" or "single-item-reapply-column" or "single-item-reapply-column-recreate-view" or "single-item-recreate-shell-window" or "single-item-recreate-shell-window-multiselect" or "single-item-recreate-shell-window-scroll" or "item-plus-parent" or "delete-create" or "full-view-refresh"))
            throw new ArgumentException($"Unknown recursive-size update strategy '{sizeUpdateStrategy}'.");
        var foreground = GetForegroundWindow();
        var runIdText = Environment.GetEnvironmentVariable("EXPLORER_DIRS_NATIVEPROBE_RUN_ID");
        if (runIdText is not null && !Guid.TryParseExact(runIdText, "N", out _))
            throw new ArgumentException("EXPLORER_DIRS_NATIVEPROBE_RUN_ID must be a GUID in N format.");
        var fixtureId = runIdText ?? Guid.NewGuid().ToString("N");
        var syntheticFolderClassId = Guid.ParseExact(fixtureId, "N");
        // A prior interrupted probe may leave its fixture behind; each run owns a fresh path
        // so it can proceed without reusing or deleting another run's artifacts.
        var backingFolder = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-SyntheticShellSort-{fixtureId}");
        var copyDestination = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-SyntheticShellSort-Copy-{fixtureId}");
        if (Directory.Exists(backingFolder))
            throw new InvalidOperationException($"Refusing to reuse an existing synthetic Shell sort fixture directory: {backingFolder}");
        if (Directory.Exists(copyDestination))
            throw new InvalidOperationException($"Refusing to reuse an existing synthetic Shell sort copy destination: {copyDestination}");
        try
        {
            Directory.CreateDirectory(backingFolder);
            Directory.CreateDirectory(copyDestination);
            if (recursiveDirectorySizeSortOnly)
            {
                Directory.CreateDirectory(Path.Combine(backingFolder, "child", "nested"));
                File.WriteAllBytes(Path.Combine(backingFolder, "child", "direct.bin"), new byte[6]);
                File.WriteAllBytes(Path.Combine(backingFolder, "child", "nested", "deep.bin"), new byte[4]);
                File.WriteAllBytes(Path.Combine(backingFolder, "small.bin"), new byte[4]);
                if (shellWindowScrollProbe)
                    for (var index = 0; index < 300; index++)
                        File.WriteAllBytes(Path.Combine(backingFolder, $"scroll-{index:D3}.dat"), []);
            }
            else
            {
                File.WriteAllBytes(Path.Combine(backingFolder, "a-large.bin"), Enumerable.Repeat((byte)0xA1, 100).ToArray());
                File.WriteAllBytes(Path.Combine(backingFolder, "two.bin"), [0x02, 0x02]);
                File.WriteAllBytes(Path.Combine(backingFolder, "z-ten.bin"), Enumerable.Repeat((byte)0x10, 10).ToArray());
            }
        }
        catch
        {
            if (Directory.Exists(backingFolder)) Directory.Delete(backingFolder, recursive: true);
            if (Directory.Exists(copyDestination)) Directory.Delete(copyDestination, recursive: true);
            throw;
        }
        System.Windows.Interop.HwndSource? wpfParentSource = null;
        nint parent;
        try
        {
            if (useWpfParent)
            {
                var parameters = new System.Windows.Interop.HwndSourceParameters("ExplorerDirs synthetic size sort WPF root")
                {
                    Width = 900,
                    Height = 600,
                    WindowStyle = 0x10CF0000
                };
                wpfParentSource = new System.Windows.Interop.HwndSource(parameters);
                wpfParentSource.RootVisual = new System.Windows.Controls.Grid();
                parent = wpfParentSource.Handle;
            }
            else
            {
                parent = CreateWindowExW(0, "STATIC", "ExplorerDirs synthetic namespace sort probe",
                    0x80000000, 0, 0, 900, 600, 0, 0, 0, 0);
            }
        }
        catch
        {
            wpfParentSource?.Dispose();
            Directory.Delete(backingFolder, recursive: true);
            Directory.Delete(copyDestination, recursive: true);
            throw;
        }
        if (parent == 0)
        {
            wpfParentSource?.Dispose();
            Directory.Delete(backingFolder, recursive: true);
            Directory.Delete(copyDestination, recursive: true);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the Private Desktop probe parent.");
        }

        nint module = 0, rootPidl = 0, folder = 0, view = 0, folderView = 0, hostPointer = 0;
        nint parent2 = 0, folder2 = 0, view2 = 0, folderView2 = 0, hostPointer2 = 0, listView2 = 0;
        var classKeyPath = $@"Software\Classes\CLSID\{{{syntheticFolderClassId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{syntheticFolderClassId:D}}}";
        Console.WriteLine($"synthetic-shell-size-sort: run-id={fixtureId}; clsid={syntheticFolderClassId:B}; backing-fixture={Path.GetFileName(backingFolder)}; copy-fixture={Path.GetFileName(copyDestination)}");
        StreamViewHost? host = null;
        StreamViewHost? host2 = null;
        var viewWindowCreated = false;
        var viewWindowCreated2 = false;
        var parentDestroyed = false;
        var classRegistrationCreated = false;
        var namespaceRegistrationCreated = false;
        if (recursiveDirectorySizeSortOnly)
            Console.WriteLine($"synthetic-recursive-directory-size-view: parent-kind={(wpfParentSource is null ? "native-static" : "wpf-hwndsource")}; hwnd=0x{parent:X}");
        try
        {
            module = NativeLibrary.Load(FindAdapterLibrary());
            Check(Marshal.GetDelegateForFunctionPointer<SetSyntheticFolderClassId>(NativeLibrary.GetExport(module,
                "SetSyntheticFolderClassIdForProbe"))(ref syntheticFolderClassId));
            Marshal.GetDelegateForFunctionPointer<ResetCounters>(NativeLibrary.GetExport(module, "ResetSyntheticSortCounters"))();
            Check(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(NativeLibrary.GetExport(module,
                "SetSyntheticMaskFilesystemAttributesForProbe"))(maskFilesystemAttributes ? 1 : 0));
            Check(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(NativeLibrary.GetExport(module,
                "SetSyntheticOwnAttributesForProbe"))(selfOwnedAttributes ? 1 : 0));
            Check(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(NativeLibrary.GetExport(module,
                "SetSyntheticCustomSizeColumnForProbe"))(customSizeColumn ? 1 : 0));
            Check(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(NativeLibrary.GetExport(module,
                "SetSyntheticSlowCustomSizeColumnForProbe"))(slowCustomSizeColumn ? 1 : 0));
            Check(Marshal.GetDelegateForFunctionPointer<SetBackingPath>(NativeLibrary.GetExport(module, "SetSyntheticSortBackingPath"))(backingFolder));
            RegisterSyntheticFolder(classKeyPath, syntheticFolderClassId);
            classRegistrationCreated = true;
            RegisterSyntheticNamespace(namespaceKeyPath);
            namespaceRegistrationCreated = true;
            Check(SHParseDisplayName($"::{syntheticFolderClassId:B}", 0, out rootPidl, 0, out _));
            if (rootPidl == 0) throw new InvalidOperationException("Shell resolved the synthetic namespace without returning an absolute PIDL.");
            Console.WriteLine("synthetic-shell-size-sort: Shell parsing name resolved through temporary Desktop\\NameSpace registration");
            Console.WriteLine("synthetic-shell-size-sort: calling CreateSyntheticSortShellView");
            Check(Marshal.GetDelegateForFunctionPointer<CreateSyntheticView>(NativeLibrary.GetExport(module, "CreateSyntheticSortShellView"))(rootPidl, out folder, out view));
            var classFactoryCreateCount = ReadCounter(module, "GetSyntheticClassFactoryCreateCount");
            Console.WriteLine($"synthetic-shell-size-sort: Shell-bound folder and view returned; registered-class-factory-creates={classFactoryCreateCount}");

            Console.WriteLine("synthetic-shell-size-sort: creating StreamViewHost");
            var browserSite = new ShellBrowserSite();
            browserSite.SelectionChanged += shellView =>
                Console.WriteLine($"synthetic-shell-size-sort: CDBOSC_SELCHANGE view=0x{unchecked((nuint)shellView):X}");
            host = new StreamViewHost(parent, []) { View = view, BrowserSite = browserSite };
            Console.WriteLine("synthetic-shell-size-sort: StreamViewHost created");
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            Console.WriteLine("synthetic-shell-size-sort: browser interface acquired");
            var settings = new StreamProbeSettings { Mode = 4 };
            var bounds = new StreamProbeRect { Right = 900, Bottom = 600 };
            Console.WriteLine("synthetic-shell-size-sort: calling IShellView.CreateViewWindow");
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out _));
            Console.WriteLine("synthetic-shell-size-sort: IShellView.CreateViewWindow returned");
            viewWindowCreated = true;
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("Creating the synthetic Shell view changed the user's foreground window.");
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
            if (recursiveDirectorySizeSortOnly)
            {
                var sizeListView = FindListView(view);
                var expectedInitialCount = shellWindowScrollProbe ? 302 : 2;
                var initialNames = WaitForNames(sizeListView, module, "child", expectedCount: expectedInitialCount);
                var initialSizes = ReadColumnTexts(sizeListView, module, 1, expectedCount: expectedInitialCount);
                var detailsExRequests = ReadCounter(module, "GetSyntheticSizeDetailsExCount");
                var detailsExFolderRequests = ReadCounter(module, "GetSyntheticSizeDetailsExFolderCount");
                var detailsExFileRequests = ReadCounter(module, "GetSyntheticSizeDetailsExFileCount");
                var customDetailsExRequests = ReadCounter(module, "GetSyntheticCustomSizeDetailsExCount");
                var customDetailsExFolderRequests = ReadCounter(module, "GetSyntheticCustomSizeDetailsExFolderCount");
                var customDetailsExFileRequests = ReadCounter(module, "GetSyntheticCustomSizeDetailsExFileCount");
                var detailsOfItemRequests = ReadCounter(module, "GetSyntheticItemDetailsOfCount");
                var detailsOfSizeRequests = ReadCounter(module, "GetSyntheticSizeDetailsOfItemCount");
                var propertyStoreFactoryRequests = ReadCounter(module, "GetSyntheticPropertyStoreFactoryRequestCount");
                var propertyStoreRequests = ReadCounter(module, "GetSyntheticPropertyStoreRequestCount");
                var attributeRequests = ReadCounter(module, "GetSyntheticGetAttributesOfCount");
                var filesystemAttributeRequests = ReadCounter(module, "GetSyntheticFilesystemAttributeCount");
                Console.WriteLine($"synthetic-recursive-directory-size-view: phase=initial; filesystem-attributes-masked={maskFilesystemAttributes}; self-owned-attributes={selfOwnedAttributes}; custom-size-column={customSizeColumn}; custom-size-column-slow={slowCustomSizeColumn}; initial=[{FormatProbeRows(initialNames)}]; size-text=[{FormatProbeRows(initialSizes)}]; standard-details-ex={detailsExRequests}(folder={detailsExFolderRequests},file={detailsExFileRequests}); custom-details-ex={customDetailsExRequests}(folder={customDetailsExFolderRequests},file={customDetailsExFileRequests}); details-of-items={detailsOfItemRequests}; details-of-size={detailsOfSizeRequests}; property-store-factory-requests={propertyStoreFactoryRequests}; property-store-requests={propertyStoreRequests}; get-attributes-of={attributeRequests}(filesystem-flags={filesystemAttributeRequests})");
                string[] ascendingNames;
                if (shellWindowScrollProbe)
                {
                    ascendingNames = initialNames;
                }
                else try
                {
                    ascendingNames = customSizeColumn
                        ? SortByHeader(module, sizeListView, columnIndex: 1, expectedCount: expectedInitialCount, stage: "header-sort-ascending")
                        : SortAndRead(module, folderView, view, sizeListView, direction: 1, expectedCount: expectedInitialCount);
                }
                catch (COMException error)
                {
                    Console.WriteLine($"synthetic-recursive-directory-size-view: sort-error=0x{error.HResult:X8}; foreground-unchanged={GetForegroundWindow() == foreground}");
                    return 1;
                }
                var ascendingComparisons = ReadCounter(module, "GetSyntheticSizeCompareCount");
                var descendingNames = shellWindowScrollProbe ? initialNames : customSizeColumn
                    ? SortByHeader(module, sizeListView, columnIndex: 1, expectedCount: expectedInitialCount, stage: "header-sort-descending")
                    : SortAndRead(module, folderView, view, sizeListView, direction: -1, expectedCount: expectedInitialCount);
                var descendingComparisons = ReadCounter(module, "GetSyntheticSizeCompareCount");
                Console.WriteLine($"synthetic-recursive-directory-size-view: filesystem-attributes-masked={maskFilesystemAttributes}; self-owned-attributes={selfOwnedAttributes}; custom-size-column={customSizeColumn}; custom-size-column-slow={slowCustomSizeColumn}; initial=[{FormatProbeRows(initialNames)}]; size-text=[{FormatProbeRows(initialSizes)}]; standard-details-ex={detailsExRequests}(folder={detailsExFolderRequests},file={detailsExFileRequests}); custom-details-ex={customDetailsExRequests}(folder={customDetailsExFolderRequests},file={customDetailsExFileRequests}); details-of-items={detailsOfItemRequests}; details-of-size={detailsOfSizeRequests}; property-store-factory-requests={propertyStoreFactoryRequests}; property-store-requests={propertyStoreRequests}; get-attributes-of={attributeRequests}(filesystem-flags={filesystemAttributeRequests}); ascending=[{FormatProbeRows(ascendingNames)}]; descending=[{FormatProbeRows(descendingNames)}]; compare-counts={ascendingComparisons}/{descendingComparisons}; foreground-unchanged={GetForegroundWindow() == foreground}");
                var childIndex = Array.FindIndex(initialNames, name => string.Equals(name, "child", StringComparison.OrdinalIgnoreCase));
                // Require the full value; substring matching would accept unrelated values such as "1200".
                var displayedDirectorySize = childIndex >= 0 && string.Equals(initialSizes[childIndex], "10", StringComparison.Ordinal);
                if (!displayedDirectorySize ||
                    (!shellWindowScrollProbe && !ascendingNames.SequenceEqual(new[] { "small.bin", "child" }, StringComparer.OrdinalIgnoreCase)) ||
                    (!shellWindowScrollProbe && !descendingNames.SequenceEqual(new[] { "child", "small.bin" }, StringComparer.OrdinalIgnoreCase)) ||
                    (shellWindowScrollProbe && (!ascendingNames.Contains("child", StringComparer.OrdinalIgnoreCase) || !descendingNames.Contains("small.bin", StringComparer.OrdinalIgnoreCase))) ||
                    (!shellWindowScrollProbe && (ascendingComparisons == 0 || descendingComparisons == 0)) || GetForegroundWindow() != foreground)
                    return 1;
                if (exerciseUpdateNotifications)
                    ProbeSizeUpdateNotifications(module, folder, folderView, view, rootPidl, backingFolder, copyDestination,
                        sizeUpdateStrategy, customSizeColumn, host, hostPointer);
                if (recursiveDirectorySizeSortOnly && exerciseUpdateNotifications)
                {
                    var sentBeforeViewDestroy = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
                    var schedulePendingDirectoryUpdate = Marshal.GetDelegateForFunctionPointer<CancelViewRefresh>(NativeLibrary.GetExport(module,
                        "ScheduleSyntheticBackingDirectoryUpdateForProbe"));
                    Check(schedulePendingDirectoryUpdate(view));
                    var cancelPendingDirectoryUpdate = Marshal.GetDelegateForFunctionPointer<CancelViewRefresh>(NativeLibrary.GetExport(module,
                        "CancelSyntheticBackingDirectoryUpdateForProbe"));
                    var cancelDirectoryUpdateResult = cancelPendingDirectoryUpdate(view);
                    if (cancelDirectoryUpdateResult != 0)
                        throw new InvalidOperationException($"Expected the pending directory-update timer to cancel before view destruction, got 0x{cancelDirectoryUpdateResult:X8}.");
                    Check(At<DestroyViewWindow>(view, 10)(view));
                    viewWindowCreated = false;
                    Pump(400);
                    var sentAfterViewDestroy = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
                    Console.WriteLine($"synthetic-recursive-directory-size-view: directory-updates-after-destroy={sentAfterViewDestroy - sentBeforeViewDestroy}");
                    if (sentAfterViewDestroy != sentBeforeViewDestroy)
                        throw new InvalidOperationException("A delayed parent-directory update ran after its Shell view window was destroyed.");
                }
                var savedViewState = host?.ReadSavedBytes() ?? [];
                Console.WriteLine($"synthetic-view-state-stream: reads={host?.Reads ?? 0}; writes={host?.Writes ?? 0}; trace=[{string.Join(" || ", host?.ViewStateStreamTrace ?? [])}]; saved-bytes={savedViewState.Length}; saved-sha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(savedViewState))}");
                ReportProbeStage("assertions-complete");
                return 0;
            }
            parent2 = CreateWindowExW(0, "STATIC", "ExplorerDirs synthetic namespace second view",
                0x80000000, 20, 20, 900, 600, 0, 0, 0, 0);
            if (parent2 == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the second Private Desktop probe parent.");
            var createSyntheticView = Marshal.GetDelegateForFunctionPointer<CreateSyntheticView>(NativeLibrary.GetExport(module, "CreateSyntheticSortShellView"));
            Check(createSyntheticView(rootPidl, out folder2, out view2));
            var browserSite2 = new ShellBrowserSite();
            browserSite2.SelectionChanged += shellView =>
                Console.WriteLine($"synthetic-shell-size-sort: CDBOSC_SELCHANGE view=0x{unchecked((nuint)shellView):X}");
            host2 = new StreamViewHost(parent2, []) { View = view2, BrowserSite = browserSite2 };
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
            var listView = FindListView(view);
            listView2 = FindListView(view2);
            Check(At<GetWindow>(view, 3)(view, out var shellViewWindow1));
            Check(At<GetWindow>(view2, 3)(view2, out var shellViewWindow2));
            Console.WriteLine($"synthetic-shell-size-sort: view-windows=0x{unchecked((nuint)shellViewWindow1):X}/0x{unchecked((nuint)shellViewWindow2):X}; list-windows=0x{unchecked((nuint)listView):X}/0x{unchecked((nuint)listView2):X}");
            using var selectionObserver1 = new ListViewSelectionObserver(listView, "view1");
            using var selectionObserver2 = new ListViewSelectionObserver(listView2, "view2");
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
                    VerifyCancelledCopies(selectionDataObject, copyDestination);
                }
                finally { Marshal.Release(selectionDataObject); }
            }
            Console.WriteLine($"synthetic-shell-size-sort: selected-items={selectedCount}; selected-item-IContextMenu-hr=0x{selectionMenuHr:X8}; QueryContextMenu-hr=0x{selectionMenuQueryHr:X8}; menu-items={selectionMenuItemCount}; selected-IDataObject-hr=0x{dataObjectHr:X8}; CF_HDROP-hr=0x{hdropHr:X8} path={hdropPath}; CIDA-hr=0x{cidaHr:X8} path={cidaPath}; IFileOperation={copyResult}");
            VerifyMultiSelectionCancellation(folderView, view, module, backingFolder, Path.Combine(copyDestination, "MultiSelection"));
            // IFileOperation cancellation can leave DefView finishing an asynchronous refresh; wait for the fixture rows before sorting.
            var initial = WaitForNames(listView, module, "a-large.bin");
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

            var previousListView = listView;
            var previousListView2 = listView2;
            listView = FindListView(view);
            listView2 = FindListView(view2);
            Console.WriteLine($"synthetic-shell-size-sort: pre-move-listviews view1=0x{unchecked((nuint)listView):X} (previous=0x{unchecked((nuint)previousListView):X}, previous-valid={IsWindow(previousListView)}); view2=0x{unchecked((nuint)listView2):X} (previous=0x{unchecked((nuint)previousListView2):X}, previous-valid={IsWindow(previousListView2)})");
            SetListViewSelectionAndFocus(listView, "two.bin", ReadNames(listView, module, expectedCount: -1));
            SetListViewSelectionAndFocus(listView2, "two.bin", ReadNames(listView2, module, expectedCount: -1));
            var preMoveNames1 = ReadNames(listView, module, expectedCount: -1);
            var preMoveNames2 = ReadNames(listView2, module, expectedCount: -1);
            if (!HasSelectionAndFocus(listView, preMoveNames1, "two.bin") ||
                !HasSelectionAndFocus(listView2, preMoveNames2, "two.bin"))
                throw new IOException("Could not establish independent two.bin selections in both views before testing MoveItems.");
            Console.WriteLine("synthetic-shell-size-sort: move-precondition-selection-view1=True; view2=True");
            var moveDestination = Path.Combine(copyDestination, "Moved");
            MoveSelectedShellItem(view, listView, view2, listView2, module, backingFolder, "z-ten.bin", "two.bin", moveDestination);
            var moveAutoUpdated = false;
            var moveAutoUpdated2 = false;
            var moveSelectionRetained = false;
            var moveSelectionRetained2 = false;
            uint moveState = 0;
            uint moveState2 = 0;
            var moveDeadline = Environment.TickCount64 + 2500;
            string[] afterMove = [];
            string[] afterMove2 = [];
            while ((!moveAutoUpdated || !moveAutoUpdated2 || !moveSelectionRetained || !moveSelectionRetained2) && Environment.TickCount64 < moveDeadline)
            {
                Pump(100);
                listView = FindListView(view);
                listView2 = FindListView(view2);
                afterMove = ReadNames(listView, module, expectedCount: -1);
                afterMove2 = ReadNames(listView2, module, expectedCount: -1);
                moveAutoUpdated = !afterMove.Contains("z-ten.bin", StringComparer.OrdinalIgnoreCase) && afterMove.Contains("two.bin", StringComparer.OrdinalIgnoreCase);
                moveAutoUpdated2 = !afterMove2.Contains("z-ten.bin", StringComparer.OrdinalIgnoreCase) && afterMove2.Contains("two.bin", StringComparer.OrdinalIgnoreCase);
                moveState = StateForName(listView, afterMove, "two.bin");
                moveState2 = StateForName(listView2, afterMove2, "two.bin");
                moveSelectionRetained = (moveState & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
                moveSelectionRetained2 = (moveState2 & (LvisSelected | LvisFocused)) == (LvisSelected | LvisFocused);
            }
            Console.WriteLine($"synthetic-shell-size-sort: move-auto-updated-view1={moveAutoUpdated}; move-auto-updated-view2={moveAutoUpdated2}; selection-view1={moveSelectionRetained}; selection-view2={moveSelectionRetained2}; two-state-view1=0x{moveState:X}; two-state-view2=0x{moveState2:X}; destination-item={File.Exists(Path.Combine(moveDestination, "z-ten.bin"))}");
            if (!moveAutoUpdated || !moveAutoUpdated2 || !moveSelectionRetained || !moveSelectionRetained2 ||
                !File.Exists(Path.Combine(moveDestination, "z-ten.bin")) || File.Exists(Path.Combine(backingFolder, "z-ten.bin")))
                throw new IOException("The Shell move did not update both views and preserve the unaffected item focus.");

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
            Check(At<GetWindow>(view, 3)(view, out var closingViewWindow));
            Check(At<DestroyViewWindow>(view, 10)(view));
            viewWindowCreated = false;
            var callbacksDuringDestroy = deferredRefreshCounter() - refreshCountBeforeCancel;
            var cancelledRefresh = Marshal.GetDelegateForFunctionPointer<CancelViewRefresh>(NativeLibrary.GetExport(module, "CancelSyntheticSortRefreshForProbe"))(view);
            Check(cancelledRefresh);
            Check(At<GetWindow>(view, 3)(view, out var destroyedViewWindow));
            Check(At<GetWindow>(view2, 3)(view2, out var survivingViewWindow));
            if (IsWindow(closingViewWindow) || IsWindow(destroyedViewWindow) || !IsWindow(survivingViewWindow))
                throw new InvalidOperationException("The cancelled first view was not destroyed, or the uncancelled sibling view was lost.");
            Console.WriteLine($"synthetic-shell-size-sort: pending-refresh-at-view-destroy=1; callbacks-during-destroy={callbacksDuringDestroy}; remaining-refresh-cancelled-after-destroy=1; sibling-view-remains-live");
            Pump(400);
            var callbacksAfterViewDestroy = deferredRefreshCounter() - refreshCountBeforeCancel;
            Console.WriteLine($"synthetic-shell-size-sort: callbacks-after-destroy={callbacksAfterViewDestroy}; callbacks-during-destroy={callbacksDuringDestroy}");
            if (callbacksAfterViewDestroy != callbacksDuringDestroy + 1)
                throw new InvalidOperationException($"Expected only the sibling refresh plus any callback already entered during view destruction, got {callbacksAfterViewDestroy} callbacks.");
            Marshal.GetDelegateForFunctionPointer<ReleaseViewHold>(NativeLibrary.GetExport(module, "ReleaseSyntheticSortViewHold"))();
            Pump(400);
            if (deferredRefreshCounter() != refreshCountBeforeCancel + callbacksAfterViewDestroy)
                throw new InvalidOperationException("A deferred refresh callback ran after the probe released both view holds.");
            Console.WriteLine("synthetic-shell-size-sort: pending-refresh-cancelled-on-release=1; callbacks-after-release=0");
            return 0;
        }
        finally
        {
            // Destroy Shell's child windows while the view, browser site, and folder objects they call back into are still alive.
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-destroy-view-window-1");
            if (viewWindowCreated && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-destroy-view-window-2");
            if (viewWindowCreated2 && view2 != 0) _ = At<DestroyViewWindow>(view2, 10)(view2);
            if (module != 0 && exerciseUpdateNotifications)
                _ = Marshal.GetDelegateForFunctionPointer<SetSyntheticSizeOverrideDelegate>(
                    NativeLibrary.GetExport(module, "SetSyntheticChildSizeOverrideForProbe"))(-1);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-native-release-view-holds");
            if (module != 0) Marshal.GetDelegateForFunctionPointer<ReleaseViewHold>(NativeLibrary.GetExport(module, "ReleaseSyntheticSortViewHold"))();
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-release-folder-view-1");
            if (folderView != 0) Marshal.Release(folderView);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-release-folder-view-2");
            if (folderView2 != 0) Marshal.Release(folderView2);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-release-view-1");
            if (view != 0) Marshal.Release(view);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-release-view-2");
            if (view2 != 0) Marshal.Release(view2);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-release-folder-objects");
            if (folder != 0) Marshal.Release(folder);
            if (folder2 != 0) Marshal.Release(folder2);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-release-browser-sites");
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (hostPointer2 != 0) Marshal.Release(hostPointer2);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-dispose-browser-hosts");
            host?.Dispose();
            host2?.Dispose();
            if (recursiveDirectorySizeSortOnly &&
                Environment.GetEnvironmentVariable("EXPLORER_DIRS_NATIVEPROBE_CLEAR_FOCUS_BEFORE_PARENT_DESTROY") == "1")
            {
                var focusedWindow = GetFocus();
                var capturedWindow = GetCapture();
                var releasedCapture = capturedWindow != 0 && IsChild(parent, capturedWindow) && ReleaseCapture();
                var clearedFocus = focusedWindow != 0 && (focusedWindow == parent || IsChild(parent, focusedWindow)) && SetFocus(0) != 0;
                Console.WriteLine($"synthetic-recursive-directory-size-view: cleanup-input-state focus=0x{focusedWindow:X}; capture=0x{capturedWindow:X}; released-capture={releasedCapture}; cleared-focus={clearedFocus}");
                ReportProbeStage($"cleanup-input-state-cleared focus=0x{focusedWindow:X} capture=0x{capturedWindow:X}");
            }
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-destroy-parent-window-1");
            if (wpfParentSource is not null)
            {
                wpfParentSource.Dispose();
                wpfParentSource = null;
                parentDestroyed = !IsWindow(parent);
            }
            else
            {
                parentDestroyed = DestroyWindow(parent);
            }
            if (!parentDestroyed) throw new InvalidOperationException("The Private Desktop synthetic-view parent did not destroy cleanly.");
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-destroy-parent-window-2");
            if (parent2 != 0 && !DestroyWindow(parent2)) throw new InvalidOperationException("The second Private Desktop synthetic-view parent did not destroy cleanly.");
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-remove-namespace-registration");
            if (namespaceRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-check-native-library-unload");
            if (module != 0)
            {
                var canUnloadNow = Marshal.GetDelegateForFunctionPointer<CanUnloadNativeLibrary>(
                    NativeLibrary.GetExport(module, "DllCanUnloadNow"))();
                var unloaded = canUnloadNow == 0;
                Console.WriteLine($"synthetic-recursive-directory-size-view: dll-can-unload-now=0x{canUnloadNow:X8}; freed={unloaded}; retained-until-process-exit={!unloaded}");
                if (unloaded)
                {
                    if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-free-native-library");
                    NativeLibrary.Free(module);
                    module = 0;
                }
            }
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-delete-backing-fixture");
            Directory.Delete(backingFolder, recursive: true);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-delete-copy-fixture");
            Directory.Delete(copyDestination, recursive: true);
            if (recursiveDirectorySizeSortOnly) ReportProbeStage("cleanup-complete");
            if (GetForegroundWindow() != foreground) throw new InvalidOperationException("The synthetic Shell sort probe changed the user's foreground window.");
        }
    }

    private static void ProbeSizeUpdateNotifications(nint module, nint folder, nint folderView, nint view,
        nint rootPidl, string backingFolder, string copyDestination, string strategyFilter, bool customSizeColumn,
        StreamViewHost host, nint hostPointer)
    {
        if (!customSizeColumn)
            throw new InvalidOperationException("Recursive-size notifications require the synthetic custom size column.");
        Check(At<ParseDisplayName>(folder, 3)(folder, 0, 0, "child", 0, out var childPidl, 0));
        if (childPidl == 0) throw new InvalidOperationException("The synthetic namespace did not parse its child folder PIDL.");
        var absoluteChildPidl = ILCombine(rootPidl, childPidl);
        ILFree(childPidl);
        if (absoluteChildPidl == 0) throw new InvalidOperationException("Could not combine the absolute child PIDL for the update notification.");

        try
        {
            Console.WriteLine($"synthetic-size-update: selected-strategy={strategyFilter}");
            if (strategyFilter == "single-item")
                RunSizeUpdateStrategy("single-item", 1, () =>
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0),
                    module, folderView, view, backingFolder, host: host);
            if (strategyFilter == "single-item-reapply-column")
                RunSizeUpdateStrategy("single-item-reapply-column", 1, () =>
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0),
                    module, folderView, view, backingFolder, reapplyCustomSizeColumn: true, host: host);
            if (strategyFilter == "single-item-reapply-column-recreate-view")
                RunSizeUpdateStrategy("single-item-reapply-column-recreate-view", 1, () =>
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0),
                    module, folderView, view, backingFolder, reapplyCustomSizeColumn: true,
                    recreateDetailsView: true, host: host);
            if (strategyFilter == "single-item-recreate-shell-window")
                RunSizeUpdateStrategy("single-item-recreate-shell-window", 1, () =>
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0),
                    module, folderView, view, backingFolder, recreateShellWindow: true,
                    host: host, hostPointer: hostPointer);
            if (strategyFilter == "single-item-recreate-shell-window-multiselect")
                RunSizeUpdateStrategy("single-item-recreate-shell-window-multiselect", 1, () =>
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0),
                    module, folderView, view, backingFolder, recreateShellWindow: true,
                    multiSelect: true, host: host, hostPointer: hostPointer);
            if (strategyFilter == "single-item-recreate-shell-window-scroll")
                RunSizeUpdateStrategy("single-item-recreate-shell-window-scroll", 1, () =>
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0),
                    module, folderView, view, backingFolder, recreateShellWindow: true,
                    preserveScrollAnchor: true, host: host, hostPointer: hostPointer,
                    copyDestination: copyDestination);
            if (strategyFilter == "item-plus-parent")
                RunSizeUpdateStrategy("item-plus-parent", 1, () =>
                {
                    SHChangeNotify(ShcneUpdateitem, 0, absoluteChildPidl, 0);
                    SHChangeNotify(ShcneUpdatedir, 0, rootPidl, 0);
                }, module, folderView, view, backingFolder, host: host);
            if (strategyFilter == "delete-create")
                RunSizeUpdateStrategy("delete-create", 1, () =>
                {
                    SHChangeNotify(ShcneDelete, 0, absoluteChildPidl, 0);
                    SHChangeNotify(ShcneCreate, 0, absoluteChildPidl, 0);
                }, module, folderView, view, backingFolder, host: host);
            if (strategyFilter == "full-view-refresh")
                RunSizeUpdateStrategy("full-view-refresh", 1, () =>
                    Check(At<RefreshView>(view, 8)(view)), module, folderView, view, backingFolder, host: host);
        }
        finally
        {
            ILFree(absoluteChildPidl);
        }
    }

    private static void RunSizeUpdateStrategy(string name, long updatedSize, Action notify,
        nint module, nint folderView, nint view, string backingFolder, bool reapplyCustomSizeColumn = false,
        bool recreateDetailsView = false, bool recreateShellWindow = false, StreamViewHost? host = null,
        nint hostPointer = 0, bool multiSelect = false, bool preserveScrollAnchor = false,
        string? copyDestination = null)
    {
        // Start from the new view's actual custom-column state. IColumnManager does not
        // capture this provider-defined key, so refreshing and replaying its snapshot
        // would erase the baseline before the notification under investigation.
        var listView = FindListView(view);
        var before = WaitForSizeState(listView, module, "10", expectedFirstName: preserveScrollAnchor ? null : "child", expectedSelection: null);
        Console.WriteLine($"synthetic-size-update: strategy={name}; baseline=initial-custom-view; value-visible={before.ValueUpdated}; column-1=[{string.Join('|', before.Sizes)}]");
        if (!before.OrderUpdated)
            throw new TimeoutException($"The new view did not retain the recursive-size baseline order before '{name}': {FormatSizeState(before)}");
        if (!before.ValueUpdated)
            throw new InvalidOperationException($"The new view did not expose the recursive-size column before '{name}'; notification strategy was not run: {FormatSizeState(before)}");
        var baselineColumnCaptureResult = ShellColumnManager.Capture(folderView, out var baselineColumns);
        Console.WriteLine($"synthetic-size-update-columns: phase=before; hr=0x{baselineColumnCaptureResult:X8}; columns=[{FormatColumns(baselineColumns)}]");
        var baselineSortColumns = preserveScrollAnchor ? CaptureNativeSortColumns(folderView) : null;
        if (preserveScrollAnchor && (baselineSortColumns is null || baselineSortColumns.Length == 0))
            throw new InvalidOperationException("The scroll-recreation scenario did not expose a native sort column to verify.");
        if (preserveScrollAnchor)
            Console.WriteLine($"synthetic-size-update-sort: phase=before; columns=[{FormatSortColumns(baselineSortColumns)}]");

        SetListViewSelectionAndFocus(listView, "small.bin", before.Names);
        if (multiSelect)
        {
            var childIndex = Array.FindIndex(before.Names, item => string.Equals(item, "child", StringComparison.OrdinalIgnoreCase));
            if (childIndex < 0) throw new InvalidOperationException("Could not find the second item for the multiselection probe.");
            SetListViewState(listView, childIndex, state: LvisSelected, stateMask: LvisSelected);
        }
        using var shellWindowSelection = recreateShellWindow ? ShellViewSelectionSnapshot.Capture(folderView) : null;
        if (recreateShellWindow && shellWindowSelection is null)
            throw new InvalidOperationException("Could not capture the synthetic Shell view selection before recreating its window.");
        if (multiSelect && shellWindowSelection?.SelectedCount != 2)
            throw new InvalidOperationException($"Could not capture both selected Shell items; captured={shellWindowSelection?.SelectedCount ?? -1}.");
        SetBackingChildSize(backingFolder, updatedSize);
        SetSyntheticSizeOverride(module, updatedSize);
        var enumsBefore = ReadCounter(module, "GetSyntheticEnumObjectsCount");
        var rowsEnumeratedBefore = ReadCounter(module, "GetSyntheticEnumeratedItemCount");
        var detailsBefore = ReadCounter(module, "GetSyntheticSizeDetailsOfItemCount");
        var comparesBefore = ReadCounter(module, "GetSyntheticSizeCompareCount");
        var propertiesBefore = ReadCounter(module, "GetSyntheticCustomSizeDetailsExCount");
        var streamReadsBefore = host?.Reads ?? 0;
        var streamWritesBefore = host?.Writes ?? 0;
        var streamTraceBefore = host?.ViewStateStreamTrace.Count ?? 0;
        var notificationStarted = Environment.TickCount64;
        notify();

        var reapplyResult = 0;
        if (reapplyCustomSizeColumn)
        {
            // The notification path drops the provider-defined column. Reintroduce
            // only Name and its mapped recursive-size key to test whether DefView
            // will request that column's values again after an explicit column set.
            var columns = new[]
            {
                new DisplayColumnState(new PropertyKeyState(SizeFormatId, 10), 240),
                new DisplayColumnState(new PropertyKeyState(RecursiveSizeFormatId, 1), 140)
            };
            reapplyResult = ShellColumnManager.Apply(folderView, columns);
            Console.WriteLine($"synthetic-size-update: strategy={name}; explicit-custom-column-reapply-hr=0x{reapplyResult:X8}");
            if (reapplyResult != 0)
                throw new COMException($"Could not explicitly restore the recursive-size column after '{name}'.", reapplyResult);
            if (recreateDetailsView)
            {
                Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 1, 16)); // FVM_ICON.
                Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 4, 16)); // FVM_DETAILS.
                Pump(250);
            }
        }
        string? scrollAnchor = null;
        if (recreateShellWindow)
        {
            int topIndexBefore = -1;
            if (preserveScrollAnchor)
            {
                var currentNames = ReadNames(listView, module, expectedCount: -1);
                var requestedAnchorIndex = Math.Min(250, currentNames.Length - 1);
                topIndexBefore = MoveListViewItemToTop(listView, currentNames, requestedAnchorIndex, out _);
                scrollAnchor = currentNames[requestedAnchorIndex];
                Console.WriteLine($"synthetic-size-update-scroll: before-top-index={topIndexBefore}; anchor={scrollAnchor}");
            }
            Check(At<DestroyViewWindow>(view, 10)(view));
            var settings = new StreamProbeSettings { Mode = 4 };
            var bounds = new StreamProbeRect { Right = 900, Bottom = 600 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out _));
            Pump(250);
            listView = FindListView(view);
            if (preserveScrollAnchor)
                ProbeRecreatedViewBackgroundMenu(view);
            var recreatedTopIndex = preserveScrollAnchor ? GetListViewTopIndex(listView) : -1;
            var recreatedNames = preserveScrollAnchor ? ReadNames(listView, module, expectedCount: -1) : [];
            var recreatedTopName = preserveScrollAnchor && recreatedTopIndex >= 0 && recreatedTopIndex < recreatedNames.Length
                ? recreatedNames[recreatedTopIndex]
                : "<unavailable>";
            if (preserveScrollAnchor)
                Console.WriteLine($"synthetic-size-update-scroll: after-recreate-top-index={recreatedTopIndex}; top={recreatedTopName}");
            Check(At<ActivateView>(view, 7)(view, 2)); // SVUIA_ACTIVATE_NOFOCUS.
            var restored = shellWindowSelection!.Restore(view, trace: message => Console.WriteLine($"synthetic-size-update-selection: {message}"));
            Console.WriteLine($"synthetic-size-update-selection: selected={restored.SelectionRestored}; focused={restored.FocusRestored}; saved-selected={shellWindowSelection.SelectedCount}; saved-focus={shellWindowSelection.HasFocusedItem}");
            if (multiSelect)
            {
                using var readback = ShellViewSelectionSnapshot.Capture(folderView);
                Console.WriteLine($"synthetic-size-update-selection: multiselect-readback={readback?.SelectedCount ?? -1}; expected=2");
                if (!restored.SelectionRestored || readback?.SelectedCount != 2)
                    throw new InvalidOperationException("The Shell selection snapshot did not restore the multiselection after view-window recreation.");
            }
        }

        var after = WaitForSizeState(listView, module, updatedSize.ToString(), expectedFirstName: preserveScrollAnchor ? null : "small.bin", expectedSelection: "small.bin");
        if (preserveScrollAnchor)
        {
            var restoredNames = ReadNames(listView, module, expectedCount: -1);
            var restoredIndex = Array.FindIndex(restoredNames, item => string.Equals(item, scrollAnchor, StringComparison.OrdinalIgnoreCase));
            if (restoredIndex < 0) throw new InvalidOperationException($"The scroll anchor '{scrollAnchor}' was not present after view recreation.");
            var finalTopIndex = MoveListViewItemToTop(listView, restoredNames, restoredIndex, out _);
            var finalNames = ReadNames(listView, module, expectedCount: -1);
            var finalTopName = finalTopIndex >= 0 && finalTopIndex < finalNames.Length ? finalNames[finalTopIndex] : "<unavailable>";
            var anchorTop = GetListViewItemTop(listView, restoredIndex);
            var finalAnchorIndex = Array.FindIndex(finalNames, item => string.Equals(item, scrollAnchor, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"synthetic-size-update-scroll: restored-top-index={finalTopIndex}; top={finalTopName}; anchor-index={finalAnchorIndex}; anchor-y={anchorTop}; anchor={scrollAnchor}");
            if (anchorTop < 0 || anchorTop > 24)
                throw new InvalidOperationException($"The restored scroll anchor '{scrollAnchor}' is at pixel {anchorTop}, outside the top row.");

            var restoredAnchor = scrollAnchor ?? throw new InvalidOperationException("The scroll anchor was not captured before view recreation.");
            SetListViewSelectionAndFocus(listView, restoredAnchor, finalNames);
            var anchorIndex = Array.FindIndex(finalNames, item => string.Equals(item, restoredAnchor, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex < 0 || anchorIndex + 1 >= finalNames.Length)
                throw new InvalidOperationException("The restored scroll anchor has no following item for the keyboard-navigation probe.");
            var expectedNextName = finalNames[anchorIndex + 1];
            SetFocus(listView);
            var focusRestoredToList = GetFocus() == listView;
            SendMessageW(listView, 0x0100, 0x28, 0); // WM_KEYDOWN / VK_DOWN.
            SendMessageW(listView, 0x0101, 0x28, 0); // WM_KEYUP / VK_DOWN.
            Pump(100);
            var namesAfterArrow = ReadNames(listView, module, expectedCount: -1);
            var arrowMovedFocus = HasSelectionAndFocus(listView, namesAfterArrow, expectedNextName);
            Console.WriteLine($"synthetic-size-update-focus: list-focused={focusRestoredToList}; before={restoredAnchor}; arrow-down-target={expectedNextName}; moved={arrowMovedFocus}; hwnd=0x{listView:X}");
            if (!focusRestoredToList || !arrowMovedFocus)
                throw new InvalidOperationException("After restoring focus to the recreated native file list, Arrow Down did not move selection and focus to the next item.");
            VerifyRecreatedViewSelectionCopy(view, backingFolder,
                copyDestination ?? throw new InvalidOperationException("The recreated-view copy destination was not provided."),
                expectedNextName);
            VerifyRecreatedViewDirectoryNotification(view, backingFolder, listView, module);
        }
        var settleMs = Environment.TickCount64 - notificationStarted;
        var columnsResult = ShellColumnManager.Capture(folderView, out var columnsAfter);
        var sortColumnsAfter = preserveScrollAnchor ? CaptureNativeSortColumns(folderView) : null;
        var sortPreserved = !preserveScrollAnchor || baselineSortColumns!.SequenceEqual(sortColumnsAfter!);
        if (preserveScrollAnchor)
            Console.WriteLine($"synthetic-size-update-sort: phase=after; preserved={sortPreserved}; columns=[{FormatSortColumns(sortColumnsAfter)}]");
        if (!sortPreserved)
            throw new InvalidOperationException($"The native Shell sort columns changed during view-window recreation: before=[{FormatSortColumns(baselineSortColumns)}]; after=[{FormatSortColumns(sortColumnsAfter)}].");
        var columnLayoutPreserved = baselineColumnCaptureResult == 0 && baselineColumns is not null &&
            columnsAfter is not null && baselineColumns.SequenceEqual(columnsAfter);
        var systemSizeColumnPresent = columnsAfter?.Any(column =>
            column.Key.FormatId == SizeFormatId && column.Key.PropertyId == 12) == true;
        var recursiveSizeColumnPresent = columnsAfter?.Any(column =>
            column.Key.FormatId == RecursiveSizeFormatId && column.Key.PropertyId == 1) == true;
        var columnKeys = columnsAfter is null ? "<unavailable>" :
            string.Join('|', columnsAfter.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}"));
        var enumsAfter = ReadCounter(module, "GetSyntheticEnumObjectsCount");
        var rowsEnumeratedAfter = ReadCounter(module, "GetSyntheticEnumeratedItemCount");
        var detailsAfter = ReadCounter(module, "GetSyntheticSizeDetailsOfItemCount");
        var comparesAfter = ReadCounter(module, "GetSyntheticSizeCompareCount");
        var propertiesAfter = ReadCounter(module, "GetSyntheticCustomSizeDetailsExCount");
        var streamTraceAfter = host?.ViewStateStreamTrace.Skip(streamTraceBefore).ToArray() ?? [];
        Console.WriteLine($"synthetic-size-update: strategy={name}; explicit-custom-column-reapply-hr=0x{reapplyResult:X8}; settle-ms={settleMs}; value-column-index={after.SizeColumnIndex}; value-updated={after.ValueUpdated}; shell-column-capture-hr=0x{columnsResult:X8}; system-size-column-present={systemSizeColumnPresent}; recursive-size-column-present={recursiveSizeColumnPresent}; column-keys=[{columnKeys}]; order-updated={after.OrderUpdated}; selection-focus-preserved={after.SelectionPreserved}; enum-calls={enumsAfter - enumsBefore}; rows-enumerated={rowsEnumeratedAfter - rowsEnumeratedBefore}; size-details={detailsAfter - detailsBefore}; size-properties={propertiesAfter - propertiesBefore}; size-compares={comparesAfter - comparesBefore}; names=[{FormatProbeRows(after.Names)}]; sizes=[{FormatProbeRows(after.Sizes)}]");
        Console.WriteLine($"synthetic-size-update-columns: phase=after; layout-preserved={columnLayoutPreserved}; hr=0x{columnsResult:X8}; columns=[{FormatColumns(columnsAfter)}]");
        Console.WriteLine($"synthetic-size-update-stream: strategy={name}; reads-delta={(host?.Reads ?? 0) - streamReadsBefore}; writes-delta={(host?.Writes ?? 0) - streamWritesBefore}; calls=[{string.Join(" || ", streamTraceAfter)}]");
    }

    private static (bool ValueUpdated, bool OrderUpdated, bool SelectionPreserved, int SizeColumnIndex, string[] Names, string[] Sizes, string SizeColumnProbe)
        WaitForSizeState(nint listView, nint module, string expectedChildSize, string? expectedFirstName, string? expectedSelection)
    {
        var deadline = Environment.TickCount64 + 2500;
        string[] names = [];
        string[] sizes = [];
        var sizeColumnProbe = string.Empty;
        var sizeColumnIndex = -1;
        bool valueUpdated = false, orderUpdated = false, selectionPreserved = expectedSelection is null;
        while (Environment.TickCount64 < deadline)
        {
            SetSizeMetricsObservation(module, enabled: true);
            try
            {
                names = ReadNames(listView, module, expectedCount: -1);
                sizeColumnIndex = -1;
                sizes = [];
                var observedColumns = new List<string>();
                var childCandidateIndex = Array.FindIndex(names, name => string.Equals(name, "child", StringComparison.OrdinalIgnoreCase));
                var fileIndex = Array.FindIndex(names, name => string.Equals(name, "small.bin", StringComparison.OrdinalIgnoreCase));
                for (var columnIndex = 1; columnIndex <= 8 && childCandidateIndex >= 0 && fileIndex >= 0; columnIndex++)
                {
                    var columnTexts = ReadColumnTexts(listView, module, subItem: columnIndex, expectedCount: -1);
                    observedColumns.Add($"{columnIndex}=[{string.Join('|', columnTexts)}]");
                    if (columnTexts.Length == names.Length &&
                        string.Equals(columnTexts[childCandidateIndex], expectedChildSize, StringComparison.Ordinal) &&
                        string.Equals(columnTexts[fileIndex], "4", StringComparison.Ordinal))
                    {
                        sizeColumnIndex = columnIndex;
                        sizes = columnTexts;
                        break;
                    }
                }
                sizeColumnProbe = string.Join(';', observedColumns);
            }
            finally
            {
                SetSizeMetricsObservation(module, enabled: false);
            }
            var childIndex = Array.FindIndex(names, name => string.Equals(name, "child", StringComparison.OrdinalIgnoreCase));
            valueUpdated = sizeColumnIndex >= 1 && childIndex >= 0 &&
                string.Equals(sizes[childIndex], expectedChildSize, StringComparison.Ordinal);
            orderUpdated = expectedFirstName is null || names.Length > 0 && string.Equals(names[0], expectedFirstName, StringComparison.OrdinalIgnoreCase);
            selectionPreserved = expectedSelection is null || HasSelectionAndFocus(listView, names, expectedSelection);
            if ((valueUpdated || sizeColumnIndex < 1) && orderUpdated && selectionPreserved) break;
            Pump(50);
        }
        return (valueUpdated, orderUpdated, selectionPreserved, sizeColumnIndex, names, sizes, sizeColumnProbe);
    }

    private static string FormatSizeState((bool ValueUpdated, bool OrderUpdated, bool SelectionPreserved, int SizeColumnIndex, string[] Names, string[] Sizes, string SizeColumnProbe) state) =>
        $"value-updated={state.ValueUpdated}; order-updated={state.OrderUpdated}; size-column-index={state.SizeColumnIndex}; names=[{string.Join('|', state.Names)}]; sizes=[{string.Join('|', state.Sizes)}]; columns=[{state.SizeColumnProbe}]";

    private static string FormatColumns(IReadOnlyList<DisplayColumnState>? columns) => columns is null
        ? "<unavailable>"
        : string.Join('|', columns.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}@{column.Width}"));

    private static NativeSortColumn[] CaptureNativeSortColumns(nint folderView)
    {
        int count = 0;
        Check(At<GetSortColumnCount>(folderView, 26)(folderView, out count));
        if (count is < 0 or > 16) throw new InvalidOperationException($"The native Shell returned an invalid sort-column count: {count}.");
        if (count == 0) return [];
        var buffer = Marshal.AllocCoTaskMem(checked(count * Marshal.SizeOf<NativeSortColumn>()));
        try
        {
            Check(At<GetSortColumns>(folderView, 28)(folderView, buffer, count));
            var columns = new NativeSortColumn[count];
            for (var index = 0; index < count; index++)
                columns[index] = Marshal.PtrToStructure<NativeSortColumn>(buffer + index * Marshal.SizeOf<NativeSortColumn>());
            return columns;
        }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    private static void ProbeRecreatedViewBackgroundMenu(nint view)
    {
        var menuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref menuId, out var contextMenu)); // SVGIO_BACKGROUND.
        if (contextMenu == 0) throw new InvalidOperationException("The recreated Shell view returned no background context menu.");
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            Marshal.Release(contextMenu);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create an in-memory menu for the recreated Shell view.");
        }
        try
        {
            var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
            Check(queryResult);
            var itemCount = GetMenuItemCount(menu);
            Console.WriteLine($"synthetic-size-update-menu: background-context-menu=True; query-hr=0x{queryResult:X8}; items={itemCount}; popup-shown=False; command-invoked=False");
            if (itemCount <= 0)
                throw new InvalidOperationException("The recreated Shell view returned an empty background context menu.");
        }
        finally
        {
            DestroyMenu(menu);
            Marshal.Release(contextMenu);
        }
    }

    private static void VerifyRecreatedViewSelectionCopy(nint view, string backingFolder, string copyDestination, string selectedName)
    {
        var dataObjectId = DataObjectId;
        Check(At<GetItemObject>(view, 15)(view, 1, ref dataObjectId, out var selectionDataObject)); // SVGIO_SELECTION.
        if (selectionDataObject == 0) throw new InvalidOperationException("The recreated Shell view returned no selected-item IDataObject.");
        var destinationPath = Path.Combine(copyDestination, "after-recreate");
        Directory.CreateDirectory(destinationPath);
        try
        {
            var fileOperationClass = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var fileOperationInterface = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Check(CoCreateInstance(ref fileOperationClass, 0, 1, ref fileOperationInterface, out var operation));
            try
            {
                Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                var shellItemInterface = ShellItemId;
                Check(SHCreateItemFromParsingName(destinationPath, 0, ref shellItemInterface, out var destinationItem));
                try { Check(At<CopyItems>(operation, 17)(operation, selectionDataObject, destinationItem)); }
                finally { Marshal.Release(destinationItem); }
                Check(At<PerformOperations>(operation, 21)(operation));
                Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
                var sourcePath = Path.Combine(backingFolder, selectedName);
                var copiedPath = Path.Combine(destinationPath, selectedName);
                var copied = aborted == 0 && File.Exists(sourcePath) && File.Exists(copiedPath) &&
                    new FileInfo(sourcePath).Length == new FileInfo(copiedPath).Length;
                Console.WriteLine($"synthetic-size-update-file-operation: selected={selectedName}; copy-items=True; aborted={aborted}; source-preserved={File.Exists(sourcePath)}; destination-matches={copied}");
                if (!copied)
                    throw new IOException("IFileOperation did not copy the selected item from the recreated Shell view with matching content length.");
            }
            finally { Marshal.Release(operation); }
        }
        finally { Marshal.Release(selectionDataObject); }
    }

    private static void VerifyRecreatedViewDirectoryNotification(nint view, string backingFolder, nint listView, nint module)
    {
        var translateChildNotifications = Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(NativeLibrary.GetExport(module,
            "SetSyntheticChildNotificationTranslationForProbe"));
        var translationEnabled = false;
        const string itemName = "post-recreate-notify.dat";
        const string renamedItemName = "renamed-after-recreate-notify.dat";
        const string createdItemName = "created-child-notify.dat";
        var itemPath = Path.Combine(backingFolder, itemName);
        var renamedItemPath = Path.Combine(backingFolder, renamedItemName);
        File.WriteAllText(itemPath, "post-recreate-notification");
        Check(SHParseDisplayName(backingFolder, 0, out var absolutePidl, 0, out _));
        if (absolutePidl == 0) throw new InvalidOperationException("Could not parse the physical backing-folder PIDL.");
        try
        {
            var enumCountBefore = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var callbackCountBefore = ReadCounter(module, "GetSyntheticDeferredRefreshCount");
            SHChangeNotify(ShcneUpdatedir, 0, absolutePidl, 0);
            var deadline = Environment.TickCount64 + 3000;
            string[] names;
            do
            {
                Pump(50);
                names = ReadNames(listView, module, expectedCount: -1);
                if (names.Contains(itemName, StringComparer.OrdinalIgnoreCase)) break;
            } while (Environment.TickCount64 < deadline);
            var enumCountAfter = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var callbackCountAfter = ReadCounter(module, "GetSyntheticDeferredRefreshCount");
            var itemVisible = names.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            var deferredCallbacks = callbackCountAfter - callbackCountBefore;
            Console.WriteLine($"synthetic-size-update-notification: event=SHCNE_UPDATEDIR; notification-visible={itemVisible}; rows={names.Length}; enum-calls={ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumCountBefore}; deferred-callbacks={deferredCallbacks}");
            if (!itemVisible || deferredCallbacks != 0)
                throw new TimeoutException("The recreated Shell view did not use default SFVM_FSNOTIFY processing to expose the new file without a host-scheduled refresh.");

            Check(translateChildNotifications(1));
            translationEnabled = true;
            var createdItemPath = Path.Combine(backingFolder, createdItemName);
            File.WriteAllBytes(createdItemPath, [0x43]);
            Check(SHParseDisplayName(createdItemPath, 0, out var createdItemPidl, 0, out _));
            if (createdItemPidl == 0) throw new InvalidOperationException("Could not parse the physical created-item PIDL.");
            try
            {
                var enumBeforeCreate = ReadCounter(module, "GetSyntheticEnumObjectsCount");
                var queuedBeforeCreate = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
                var sentBeforeCreate = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
                SHChangeNotify(ShcneCreate, 0, createdItemPidl, 0);
                deadline = Environment.TickCount64 + 3000;
                do
                {
                    Pump(50);
                    names = ReadNames(listView, module, expectedCount: -1);
                    if (names.Contains(createdItemName, StringComparer.OrdinalIgnoreCase) &&
                        ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBeforeCreate) break;
                } while (Environment.TickCount64 < deadline);
                var createVisible = names.Contains(createdItemName, StringComparer.OrdinalIgnoreCase);
                var createEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBeforeCreate;
                var createQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBeforeCreate;
                var createSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBeforeCreate;
                Console.WriteLine($"synthetic-size-update-notification: event=SHCNE_CREATE; create-visible={createVisible}; rows={names.Length}; enum-calls={createEnumCalls}; translated-queued={createQueued}; translated-sent={createSent}");
                if (!createVisible || createEnumCalls != 1 || createQueued != 1 || createSent != 1)
                    throw new TimeoutException("The recreated synthetic view did not expose a new child through one translated SHCNE_CREATE notification.");
            }
            finally { ILFree(createdItemPidl); }

            var burstNames = new[] { "burst-create-1.dat", "burst-create-2.dat", "burst-create-3.dat" };
            var enumBeforeBurst = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var queuedBeforeBurst = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var sentBeforeBurst = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            foreach (var burstName in burstNames)
            {
                var burstPath = Path.Combine(backingFolder, burstName);
                File.WriteAllBytes(burstPath, [0x42]);
                Check(SHParseDisplayName(burstPath, 0, out var burstItemPidl, 0, out _));
                if (burstItemPidl == 0) throw new InvalidOperationException($"Could not parse the physical PIDL for '{burstName}'.");
                try { SHChangeNotify(ShcneCreate, 0, burstItemPidl, 0); }
                finally { ILFree(burstItemPidl); }
            }
            deadline = Environment.TickCount64 + 3000;
            do
            {
                Pump(50);
                names = ReadNames(listView, module, expectedCount: -1);
                if (burstNames.All(name => names.Contains(name, StringComparer.OrdinalIgnoreCase)) &&
                    ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBeforeBurst) break;
            } while (Environment.TickCount64 < deadline);
            var burstVisibleCount = burstNames.Count(name => names.Contains(name, StringComparer.OrdinalIgnoreCase));
            var burstEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBeforeBurst;
            var burstQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBeforeBurst;
            var burstSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBeforeBurst;
            Console.WriteLine($"synthetic-size-update-notification: event=SHCNE_CREATE-burst; visible={burstVisibleCount}/{burstNames.Length}; rows={names.Length}; enum-calls={burstEnumCalls}; translated-queued={burstQueued}; translated-sent={burstSent}");
            if (burstVisibleCount != burstNames.Length || burstEnumCalls != 1 || burstQueued != 1 || burstSent != 1)
                throw new TimeoutException("The recreated synthetic view did not coalesce a three-item create burst into one parent update and enumeration.");

            names = ReadNames(listView, module, expectedCount: -1);
            SetListViewSelectionAndFocus(listView, createdItemName, names);
            var childFolder = Path.Combine(backingFolder, "child");
            var createdDestinationPath = Path.Combine(childFolder, createdItemName);
            var enumBeforeMove = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var queuedBeforeMove = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var sentBeforeMove = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            MoveSelectedShellItemToFilesystemFolder(view, listView, module, createdItemName, itemName, childFolder);
            if (File.Exists(createdItemPath) || !File.Exists(createdDestinationPath) || File.ReadAllBytes(createdDestinationPath)[0] != 0x43)
                throw new IOException("IFileOperation did not move the created notification fixture into the backing child folder.");
            deadline = Environment.TickCount64 + 3000;
            do
            {
                Pump(50);
                names = ReadNames(listView, module, expectedCount: -1);
                if (!names.Contains(createdItemName, StringComparer.OrdinalIgnoreCase) &&
                    ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBeforeMove) break;
            } while (Environment.TickCount64 < deadline);
            var moveVisible = !names.Contains(createdItemName, StringComparer.OrdinalIgnoreCase);
            var moveEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBeforeMove;
            var moveQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBeforeMove;
            var moveSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBeforeMove;
            Console.WriteLine($"synthetic-size-update-notification: operation=IFileOperation-MoveItems; source-cleared={moveVisible}; rows={names.Length}; enum-calls={moveEnumCalls}; translated-queued={moveQueued}; translated-sent={moveSent}");
            if (!moveVisible || moveEnumCalls != 1 || moveQueued != 1 || moveSent != 1)
                throw new TimeoutException("The recreated synthetic view did not clear a moved child through one translated Shell notification.");

            SetListViewSelectionAndFocus(listView, itemName, names);
            var enumBeforeRename = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var queuedBeforeRename = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var sentBeforeRename = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            RenameSelectedShellItem(view, listView, module, itemName, "small.bin", renamedItemName);
            if (File.Exists(itemPath) || !File.Exists(renamedItemPath))
                throw new IOException("The post-recreation IFileOperation rename did not update the backing fixture.");
            deadline = Environment.TickCount64 + 3000;
            do
            {
                Pump(50);
                names = ReadNames(listView, module, expectedCount: -1);
                if (!names.Contains(itemName, StringComparer.OrdinalIgnoreCase) &&
                    names.Count(name => string.Equals(name, renamedItemName, StringComparison.OrdinalIgnoreCase)) == 1 &&
                    ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBeforeRename) break;
            } while (Environment.TickCount64 < deadline);
            var shellRenameVisible = !names.Contains(itemName, StringComparer.OrdinalIgnoreCase) &&
                names.Count(name => string.Equals(name, renamedItemName, StringComparison.OrdinalIgnoreCase)) == 1;
            var renameEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBeforeRename;
            var renameQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBeforeRename;
            var renameSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBeforeRename;
            Console.WriteLine($"synthetic-size-update-notification: operation=IFileOperation-RenameItems; rename-visible={shellRenameVisible}; rows={names.Length}; enum-calls={renameEnumCalls}; translated-queued={renameQueued}; translated-sent={renameSent}");
            if (!shellRenameVisible || renameEnumCalls != 1 || renameQueued != 1 || renameSent != 1)
                throw new TimeoutException("The recreated synthetic view did not normalize after IFileOperation rename without a manually supplied parent update.");

            SetListViewSelectionAndFocus(listView, renamedItemName, names);
            var enumBeforeDelete = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var queuedBeforeDelete = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var sentBeforeDelete = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            DeleteSelectedShellItem(view, listView, module, renamedItemName, "small.bin");
            if (File.Exists(renamedItemPath))
                throw new IOException("The post-recreation IFileOperation delete left the backing fixture item present.");
            deadline = Environment.TickCount64 + 3000;
            do
            {
                Pump(50);
                names = ReadNames(listView, module, expectedCount: -1);
                if (!names.Contains(renamedItemName, StringComparer.OrdinalIgnoreCase) &&
                    ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBeforeDelete) break;
            } while (Environment.TickCount64 < deadline);
            var shellDeleteVisible = !names.Contains(renamedItemName, StringComparer.OrdinalIgnoreCase);
            var deleteEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBeforeDelete;
            var deleteQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBeforeDelete;
            var deleteSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBeforeDelete;
            Console.WriteLine($"synthetic-size-update-notification: operation=IFileOperation-DeleteItems; delete-visible={shellDeleteVisible}; rows={names.Length}; enum-calls={deleteEnumCalls}; translated-queued={deleteQueued}; translated-sent={deleteSent}");
            if (!shellDeleteVisible || deleteEnumCalls != 1 || deleteQueued != 1 || deleteSent != 1)
                throw new TimeoutException("The recreated synthetic view did not normalize after IFileOperation delete without a manually supplied parent update.");

            VerifyFolderLifecycleNotifications(view, listView, module, backingFolder);

            // A real view owner must cancel the adapter's delayed parent update before destroying the view.
            var queuedBeforeCancel = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var sentBeforeCancel = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            var enumerationsBeforeCancel = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var schedulePendingUpdate = Marshal.GetDelegateForFunctionPointer<CancelViewRefresh>(NativeLibrary.GetExport(module,
                "ScheduleSyntheticBackingDirectoryUpdateForProbe"));
            var scheduleResult = schedulePendingUpdate(view);
            if (scheduleResult != 0)
                throw new InvalidOperationException($"Expected a delayed directory update to schedule with S_OK, got 0x{scheduleResult:X8}.");
            var queuedAfterSchedule = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            if (queuedAfterSchedule != queuedBeforeCancel + 1)
                throw new InvalidOperationException($"Expected one delayed parent update before cancellation, got {queuedAfterSchedule - queuedBeforeCancel}.");
            var cancelPendingUpdate = Marshal.GetDelegateForFunctionPointer<CancelViewRefresh>(NativeLibrary.GetExport(module,
                "CancelSyntheticBackingDirectoryUpdateForProbe"));
            var cancelResult = cancelPendingUpdate(view);
            if (cancelResult != 0)
                throw new InvalidOperationException($"Expected an active pending directory-update timer to cancel with S_OK, got 0x{cancelResult:X8}.");
            Pump(400);
            var sentAfterCancel = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            var enumerationsAfterCancel = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            Console.WriteLine($"synthetic-size-update-notification: pending-directory-update-cancelled=1; queued={queuedAfterSchedule - queuedBeforeCancel}; sent-after-cancel={sentAfterCancel - sentBeforeCancel}; enum-after-cancel={enumerationsAfterCancel - enumerationsBeforeCancel}");
            if (sentAfterCancel != sentBeforeCancel || enumerationsAfterCancel != enumerationsBeforeCancel)
            {
                throw new InvalidOperationException("A cancelled delayed parent update still notified Shell or caused provider enumeration.");
            }

            var attributeFixturePath = Path.Combine(backingFolder, "small.bin");
            Check(SHParseDisplayName(attributeFixturePath, 0, out var attributeFixturePidl, 0, out _));
            if (attributeFixturePidl == 0) throw new InvalidOperationException("Could not parse the physical attribute-fixture PIDL.");
            try { VerifyAttributeChangeNotification(attributeFixturePath, "small.bin", attributeFixturePidl, listView, module); }
            finally { ILFree(attributeFixturePidl); }
        }
        finally
        {
            if (translationEnabled) Check(translateChildNotifications(0));
            ILFree(absolutePidl);
        }
    }

    private static string FormatSortColumns(IReadOnlyList<NativeSortColumn>? columns) => columns is null
        ? "<unavailable>"
        : string.Join('|', columns.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}/{column.Direction}"));

    private static void VerifyFolderLifecycleNotifications(nint view, nint listView, nint module, string backingFolder)
    {
        var folderName = "notify-folder";
        var renamedFolderName = "notify-renamed-folder";
        var folderPath = Path.Combine(backingFolder, folderName);
        var renamedFolderPath = Path.Combine(backingFolder, renamedFolderName);
        var names = ReadNames(listView, module, expectedCount: -1);
        var deadline = Environment.TickCount64 + 3000;
        var enumBefore = ReadCounter(module, "GetSyntheticEnumObjectsCount");
        var queuedBefore = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
        var sentBefore = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
        CreateFolderInFilesystemFolder(backingFolder, folderName);
        do
        {
            Pump(50);
            names = ReadNames(listView, module, expectedCount: -1);
            if (names.Contains(folderName, StringComparer.OrdinalIgnoreCase) &&
                ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBefore) break;
        } while (Environment.TickCount64 < deadline);
        var mkdirEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBefore;
        var mkdirQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBefore;
        var mkdirSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBefore;
        var mkdirVisible = Directory.Exists(folderPath) && names.Contains(folderName, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"synthetic-size-update-notification: event=IFileOperation-NewItem-folder(SHCNE_MKDIR=0x{ShcneMakeDirectory:X}); visible={mkdirVisible}; enum-calls={mkdirEnumCalls}; translated-queued={mkdirQueued}; translated-sent={mkdirSent}");
        if (!mkdirVisible || mkdirQueued < 1 || mkdirSent < 1)
            throw new TimeoutException("A Shell-created folder did not reach the synthetic view through its translated lifecycle notification.");

        SetListViewSelectionAndFocus(listView, folderName, names);
        enumBefore = ReadCounter(module, "GetSyntheticEnumObjectsCount");
        queuedBefore = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
        sentBefore = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
        RenameSelectedShellItem(view, listView, module, folderName, "child", renamedFolderName);
        deadline = Environment.TickCount64 + 3000;
        do
        {
            Pump(50);
            names = ReadNames(listView, module, expectedCount: -1);
            if (!names.Contains(folderName, StringComparer.OrdinalIgnoreCase) &&
                names.Contains(renamedFolderName, StringComparer.OrdinalIgnoreCase) &&
                ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBefore) break;
        } while (Environment.TickCount64 < deadline);
        var renameEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBefore;
        var renameQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBefore;
        var renameSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBefore;
        var renameVisible = !Directory.Exists(folderPath) && Directory.Exists(renamedFolderPath) &&
            !names.Contains(folderName, StringComparer.OrdinalIgnoreCase) &&
            names.Contains(renamedFolderName, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"synthetic-size-update-notification: event=IFileOperation-RenameItems-folder(SHCNE_RENAMEFOLDER=0x{ShcneRenameFolder:X}); visible={renameVisible}; enum-calls={renameEnumCalls}; translated-queued={renameQueued}; translated-sent={renameSent}");
        if (!renameVisible || renameQueued < 1 || renameSent < 1)
            throw new TimeoutException("A Shell-renamed folder did not reach the synthetic view through its translated lifecycle notification.");

        SetListViewSelectionAndFocus(listView, renamedFolderName, names);
        enumBefore = ReadCounter(module, "GetSyntheticEnumObjectsCount");
        queuedBefore = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
        sentBefore = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
        DeleteSelectedShellItem(view, listView, module, renamedFolderName, "child");
        deadline = Environment.TickCount64 + 3000;
        do
        {
            Pump(50);
            names = ReadNames(listView, module, expectedCount: -1);
            if (!names.Contains(renamedFolderName, StringComparer.OrdinalIgnoreCase) &&
                ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") > sentBefore) break;
        } while (Environment.TickCount64 < deadline);
        var rmdirEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBefore;
        var rmdirQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBefore;
        var rmdirSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBefore;
        var rmdirVisible = !Directory.Exists(renamedFolderPath) && !names.Contains(renamedFolderName, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"synthetic-size-update-notification: event=IFileOperation-DeleteItems-folder(SHCNE_RMDIR=0x{ShcneRemoveDirectory:X}); visible={rmdirVisible}; enum-calls={rmdirEnumCalls}; translated-queued={rmdirQueued}; translated-sent={rmdirSent}");
        if (!rmdirVisible || rmdirQueued < 1 || rmdirSent < 1)
            throw new TimeoutException("A Shell-deleted folder did not disappear from the synthetic view through its translated lifecycle notification.");
    }

    private static void VerifyAttributeChangeNotification(string itemPath, string itemName, nint itemPidl, nint listView, nint module)
    {
        WaitForChildNotificationTranslationToSettle(module);
        var originalAttributes = File.GetAttributes(itemPath);
        try
        {
            File.SetAttributes(itemPath, originalAttributes | FileAttributes.Hidden);
            var enumBeforeHide = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            var queuedBeforeHide = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var sentBeforeHide = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            var attributesBeforeHide = ReadCounter(module, "GetSyntheticAttributeNotificationTranslationCount");
            SHChangeNotify(ShcneAttributes, 0, itemPidl, 0);
            var deadline = Environment.TickCount64 + 3000;
            string[] names;
            do
            {
                Pump(50);
                names = ReadNames(listView, module, expectedCount: -1);
                if (ReadCounter(module, "GetSyntheticAttributeNotificationTranslationCount") > attributesBeforeHide) break;
            } while (Environment.TickCount64 < deadline);
            var hideAttributeTranslations = ReadCounter(module, "GetSyntheticAttributeNotificationTranslationCount") - attributesBeforeHide;
            var hideEnumCalls = ReadCounter(module, "GetSyntheticEnumObjectsCount") - enumBeforeHide;
            var hideQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount") - queuedBeforeHide;
            var hideSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount") - sentBeforeHide;
            var hiddenRowRemainsVisible = names.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"synthetic-size-update-notification: event=SHCNE_ATTRIBUTES-hidden; row-remains-visible={hiddenRowRemainsVisible}; attribute-translations={hideAttributeTranslations}; enum-calls={hideEnumCalls}; translated-queued={hideQueued}; translated-sent={hideSent}");
            if (!hiddenRowRemainsVisible || hideAttributeTranslations != 1 || hideQueued < 1 || hideSent < 1)
                throw new TimeoutException("The recreated synthetic view did not reconcile a hidden attribute change through one translated SHCNE_ATTRIBUTES notification.");
        }
        finally
        {
            // Keep the Shell view and later cleanup checks independent of attribute-probe failures.
            File.SetAttributes(itemPath, originalAttributes);
            WaitForChildNotificationTranslationToSettle(module);
        }
    }

    private static void WaitForChildNotificationTranslationToSettle(nint module)
    {
        var queued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
        var sent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
        var enumerations = ReadCounter(module, "GetSyntheticEnumObjectsCount");
        var unchangedSince = Environment.TickCount64;
        var deadline = unchangedSince + 3000;
        while (Environment.TickCount64 < deadline)
        {
            Pump(50);
            var nextQueued = ReadCounter(module, "GetSyntheticChildNotificationTranslationQueuedCount");
            var nextSent = ReadCounter(module, "GetSyntheticChildNotificationTranslationSentCount");
            var nextEnumerations = ReadCounter(module, "GetSyntheticEnumObjectsCount");
            if (nextQueued != queued || nextSent != sent || nextEnumerations != enumerations)
            {
                queued = nextQueued;
                sent = nextSent;
                enumerations = nextEnumerations;
                unchangedSince = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - unchangedSince >= 300)
            {
                return;
            }
        }
        throw new TimeoutException("Synthetic child notifications did not settle before the attribute-change check.");
    }

    private static string FormatProbeRows(string[] rows) => rows.Length <= 12
        ? string.Join('|', rows)
        : $"{string.Join('|', rows.Take(4))}|...|{string.Join('|', rows.TakeLast(4))} (count={rows.Length})";

    private static void SetSyntheticSizeOverride(nint module, long size) =>
        Check(Marshal.GetDelegateForFunctionPointer<SetSyntheticSizeOverrideDelegate>(
            NativeLibrary.GetExport(module, "SetSyntheticChildSizeOverrideForProbe"))(size));

    private static void SetBackingChildSize(string backingFolder, long size)
    {
        var child = Path.Combine(backingFolder, "child");
        if (Directory.Exists(child)) Directory.Delete(child, recursive: true);
        Directory.CreateDirectory(child);
        if (size == 10)
        {
            Directory.CreateDirectory(Path.Combine(child, "nested"));
            File.WriteAllBytes(Path.Combine(child, "direct.bin"), new byte[6]);
            File.WriteAllBytes(Path.Combine(child, "nested", "deep.bin"), new byte[4]);
        }
        else
        {
            File.WriteAllBytes(Path.Combine(child, "direct.bin"), new byte[checked((int)size)]);
        }
    }

    private static string[] SortAndRead(nint module, nint folderView, nint view, nint listView, int direction,
        int expectedCount = 3)
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
        return ReadNames(listView, module, expectedCount);
    }

    private static string[] SortByHeader(nint module, nint listView, int columnIndex, int expectedCount, string stage)
    {
        ReportProbeStage(stage + "-begin");
        Marshal.GetDelegateForFunctionPointer<ResetCounters>(NativeLibrary.GetExport(module, "ResetSyntheticSortCounters"))();
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER.
        if (header == 0) throw new InvalidOperationException("The synthetic DefView has no header control.");
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            if (SendMessageW(header, 0x1207, columnIndex, rectanglePointer) == 0) // HDM_GETITEMRECT.
                throw new InvalidOperationException($"Could not read the synthetic header column {columnIndex} rectangle.");
            var rectangle = Marshal.PtrToStructure<NativeRect>(rectanglePointer);
            var x = (rectangle.Left + rectangle.Right) / 2;
            var y = (rectangle.Top + rectangle.Bottom) / 2;
            var coordinates = unchecked((nint)((y << 16) | (x & 0xffff)));
            SendMessageW(header, 0x0200, 0, coordinates); // WM_MOUSEMOVE.
            SendMessageW(header, 0x0201, 1, coordinates); // WM_LBUTTONDOWN.
            SendMessageW(header, 0x0202, 0, coordinates); // WM_LBUTTONUP.
        }
        finally { Marshal.FreeHGlobal(rectanglePointer); }
        Pump(350);
        ReportProbeStage(stage + "-complete");
        return ReadNames(listView, module, expectedCount);
    }

    private static void ReportProbeStage(string stage) =>
        PrivateDesktopShellProbe.ReportStage("synthetic-recursive-directory-size:" + stage);

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

    private static void CreateFolderInFilesystemFolder(string parentPath, string name)
    {
        var shellItemInterface = ShellItemId;
        Check(SHCreateItemFromParsingName(parentPath, 0, ref shellItemInterface, out var destination));
        var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        var createOperationHr = CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var operation);
        if (createOperationHr < 0) { Marshal.Release(destination); Check(createOperationHr); }
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            Check(At<NewItem>(operation, 20)(operation, destination, 0x10, name, 0, 0));
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("The isolated Shell folder-creation operation was aborted.");
        }
        finally
        {
            Marshal.Release(operation);
            Marshal.Release(destination);
        }
    }

    private static void MoveSelectedShellItemToFilesystemFolder(nint view, nint listView, nint module,
        string itemName, string restoreFocusName, string destinationFolder)
    {
        var dataObject = GetSelectionDataObject(view, listView, module, itemName, restoreFocusName);
        var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        var createOperationHr = CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var operation);
        if (createOperationHr < 0) { Marshal.Release(dataObject); Check(createOperationHr); }
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            var shellItemInterface = ShellItemId;
            Check(SHCreateItemFromParsingName(destinationFolder, 0, ref shellItemInterface, out var destinationItem));
            try { Check(At<MoveItems>(operation, 15)(operation, dataObject, destinationItem)); }
            finally { Marshal.Release(destinationItem); }
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("The isolated synthetic-view Shell move was aborted.");
        }
        finally
        {
            Marshal.Release(operation);
            Marshal.Release(dataObject);
        }
    }

    private static void MoveSelectedShellItem(nint view, nint listView, nint view2, nint listView2, nint module, string backingFolder, string itemName, string restoreFocusName, string destinationFolder)
    {
        if (Directory.Exists(destinationFolder))
            throw new InvalidOperationException($"Refusing to reuse the isolated Shell move destination: {destinationFolder}");
        Directory.CreateDirectory(destinationFolder);
        var sourcePath = Path.Combine(backingFolder, itemName);
        var expectedBytes = File.ReadAllBytes(sourcePath);
        var dataObject = GetSelectionDataObject(view, listView, module, itemName, restoreFocusName, listView2);
        var beforeMoveNames1 = ReadNames(listView, module, expectedCount: -1);
        var beforeMoveNames2 = ReadNames(listView2, module, expectedCount: -1);
        var beforeMoveState1 = StateForName(listView, beforeMoveNames1, restoreFocusName);
        var beforeMoveState2 = StateForName(listView2, beforeMoveNames2, restoreFocusName);
        Console.WriteLine($"synthetic-shell-size-sort: before-PerformOperations view1={restoreFocusName}:0x{beforeMoveState1:X}; view2={restoreFocusName}:0x{beforeMoveState2:X}; names1=[{string.Join('|', beforeMoveNames1)}]; names2=[{string.Join('|', beforeMoveNames2)}]");
        var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        var createOperationHr = CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var operation);
        if (createOperationHr < 0) { Marshal.Release(dataObject); Check(createOperationHr); }
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            var shellItemInterface = ShellItemId;
            Check(SHCreateItemFromParsingName(destinationFolder, 0, ref shellItemInterface, out var destinationItem));
            try { Check(At<MoveItems>(operation, 15)(operation, dataObject, destinationItem)); }
            finally { Marshal.Release(destinationItem); }
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            var movedPath = Path.Combine(destinationFolder, itemName);
            if (aborted != 0 || File.Exists(sourcePath) || !File.Exists(movedPath) ||
                !expectedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(movedPath)))
                throw new IOException($"IFileOperation did not move '{itemName}' with matching content: aborted={aborted}; source-exists={File.Exists(sourcePath)}; destination-exists={File.Exists(movedPath)}.");
        }
        finally
        {
            Marshal.Release(operation);
            Marshal.Release(dataObject);
        }
    }

    private static nint GetSelectionDataObject(nint view, nint listView, nint module, string itemName, string restoreFocusName,
        nint companionListView = 0)
    {
        // This helper also runs after earlier file operations have reduced the fixture row count.
        if (companionListView != 0)
        {
            TraceSelectionPair("before-source-select", listView, companionListView, module, restoreFocusName);
            EnsureCompanionSelection("before-source-select", companionListView, module, restoreFocusName);
        }
        SetListViewSelectionAndFocus(listView, itemName, ReadNames(listView, module, expectedCount: -1));
        if (companionListView != 0)
        {
            TraceSelectionPair("after-source-select", listView, companionListView, module, restoreFocusName);
            EnsureCompanionSelection("after-source-select", companionListView, module, restoreFocusName);
        }
        var dataObjectId = DataObjectId;
        Check(At<GetItemObject>(view, 15)(view, 1, ref dataObjectId, out var dataObject)); // SVGIO_SELECTION.
        if (dataObject == 0) throw new IOException($"The synthetic view returned no Shell IDataObject for selected item '{itemName}'.");
        try
        {
            if (companionListView != 0)
            {
                TraceSelectionPair("after-selection-data-object", listView, companionListView, module, restoreFocusName);
                EnsureCompanionSelection("after-selection-data-object", companionListView, module, restoreFocusName);
            }
            SetListViewSelectionAndFocus(listView, restoreFocusName, ReadNames(listView, module, expectedCount: -1));
            if (companionListView != 0)
            {
                TraceSelectionPair("after-selection-restore", listView, companionListView, module, restoreFocusName);
                EnsureCompanionSelection("after-selection-restore", companionListView, module, restoreFocusName);
            }
            return dataObject;
        }
        catch { Marshal.Release(dataObject); throw; }
    }

    private static void EnsureCompanionSelection(string stage, nint companionListView, nint module, string itemName)
    {
        var names = ReadNames(companionListView, module, expectedCount: -1);
        if (!HasSelectionAndFocus(companionListView, names, itemName))
            throw new IOException($"The source-view selection setup disturbed the companion view at {stage}: '{itemName}' lost selection or focus.");
    }

    private static void TraceSelectionPair(string stage, nint listView1, nint listView2, nint module, string name)
    {
        var names1 = ReadNames(listView1, module, expectedCount: -1);
        var names2 = ReadNames(listView2, module, expectedCount: -1);
        var state1 = StateForName(listView1, names1, name);
        var state2 = StateForName(listView2, names2, name);
        Console.WriteLine($"synthetic-shell-size-sort: {stage} item={name}; view1=0x{state1:X}; view2=0x{state2:X}; names1=[{string.Join('|', names1)}]; names2=[{string.Join('|', names2)}]");
    }

    private static string[] ReadNames(nint listView, nint module, int expectedCount = 3)
        => ReadColumnTexts(listView, module, subItem: 0, expectedCount: expectedCount);

    private static string[] ReadColumnTexts(nint listView, nint module, int subItem, int expectedCount)
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
                var item = new NativeListViewItem { Mask = LvifText, Item = index, SubItem = subItem, Text = text, TextCapacity = 1024 };
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

    private static string[] WaitForNames(nint listView, nint module, string expectedName, int expectedCount = 3)
    {
        var deadline = Environment.TickCount64 + 2500;
        string[] names;
        do
        {
            names = ReadNames(listView, module, expectedCount: -1);
            if (names.Length == expectedCount && names.Contains(expectedName, StringComparer.OrdinalIgnoreCase)) return names;
            Pump(50);
        } while (Environment.TickCount64 < deadline);
        throw new TimeoutException($"The synthetic Shell view did not settle to {expectedCount} rows including '{expectedName}' within 2.5 seconds; current=[{string.Join('|', names)}].");
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

    private static int MoveListViewItemToTop(nint listView, string[] names, int itemIndex, out int originalTopIndex)
    {
        if (itemIndex < 0 || itemIndex >= names.Length)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), $"Cannot scroll to item {itemIndex} in a {names.Length}-row view.");
        originalTopIndex = GetListViewTopIndex(listView);
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.WriteInt32(rectanglePointer, 0); // LVIR_BOUNDS
            if (SendMessageW(listView, LvmGetItemRect, itemIndex, rectanglePointer) == 0)
                throw new InvalidOperationException($"Could not read ListView item {itemIndex} bounds while restoring the scroll anchor.");
            var itemHeight = Marshal.ReadInt32(rectanglePointer, sizeof(int) * 3) - Marshal.ReadInt32(rectanglePointer, sizeof(int));
            if (itemHeight <= 0) throw new InvalidOperationException($"ListView item {itemIndex} has invalid height {itemHeight}.");
            if (!GetClientRect(listView, out var client))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not read the ListView client bounds for scroll restoration.");
            var header = SendMessageW(listView, LvmGetHeader, 0, 0);
            var headerHeight = header != 0 && GetClientRect(header, out var headerClient) ? headerClient.Bottom - headerClient.Top : 0;
            var visibleRows = Math.Max(1, (client.Bottom - client.Top - headerHeight) / itemHeight);
            var bottomAnchor = Math.Min(names.Length - 1, checked(itemIndex + visibleRows - 1));
            var ensureResult = SendMessageW(listView, LvmEnsureVisible, bottomAnchor, 0);
            Console.WriteLine($"synthetic-size-update-scroll: move-index={itemIndex}; initial-top={originalTopIndex}; visible-rows={visibleRows}; ensure-bottom={bottomAnchor}; ensure-hr={ensureResult}");
        }
        finally { Marshal.FreeHGlobal(rectanglePointer); }
        return GetListViewTopIndex(listView);
    }

    private static int GetListViewTopIndex(nint listView) =>
        checked((int)SendMessageW(listView, LvmGetTopIndex, 0, 0));

    private static int GetListViewItemTop(nint listView, int itemIndex)
    {
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.WriteInt32(rectanglePointer, 0); // LVIR_BOUNDS
            if (SendMessageW(listView, LvmGetItemRect, itemIndex, rectanglePointer) == 0)
                throw new InvalidOperationException($"Could not read ListView item {itemIndex} bounds.");
            return Marshal.ReadInt32(rectanglePointer, sizeof(int));
        }
        finally { Marshal.FreeHGlobal(rectanglePointer); }
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

    private static void RegisterSyntheticFolder(string classKeyPath, Guid classId)
    {
        using var classes = Registry.CurrentUser.OpenSubKey("Software\\Classes", writable: true)
            ?? throw new InvalidOperationException("HKCU\\Software\\Classes is unavailable.");
        using var existing = classes.OpenSubKey($"CLSID\\{{{classId:D}}}");
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

    private static void SetSizeMetricsObservation(nint module, bool enabled) =>
        Check(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
            NativeLibrary.GetExport(module, "SetSyntheticSizeMetricsObservationForProbe"))(enabled ? 1 : 0));

    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    private static void VerifyCancelledCopies(nint selectedItems, string destinationFolder)
    {
        var cancellationFolder = Path.Combine(destinationFolder, "Cancelled");
        if (Directory.Exists(cancellationFolder))
            throw new InvalidOperationException($"Refusing to reuse the isolated cancellation destination: {cancellationFolder}");
        Directory.CreateDirectory(cancellationFolder);
        var fileOperationClass = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var fileOperationInterface = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref fileOperationClass, 0, 1, ref fileOperationInterface, out var operation));
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            var shellItemInterface = ShellItemId;
            Check(SHCreateItemFromParsingName(cancellationFolder, 0, ref shellItemInterface, out var destinationItem));
            try
            {
                var sink = new CancelFirstCopySink();
                var sinkPointer = Marshal.GetComInterfaceForObject(sink, typeof(IFileOperationProgressSinkProbe));
                try
                {
                    Check(At<Advise>(operation, 3)(operation, sinkPointer, out var adviseCookie));
                    try
                    {
                        var copyItems = At<CopyItems>(operation, 17);
                        Check(copyItems(operation, selectedItems, destinationItem));
                        Check(copyItems(operation, selectedItems, destinationItem));
                        var performResult = At<PerformOperations>(operation, 21)(operation);
                        Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
                        if (sink.PreCopyCalls != 1 || performResult != unchecked((int)0x80004004) ||
                            Directory.EnumerateFileSystemEntries(cancellationFolder).Any())
                            throw new IOException($"IFileOperation cancellation did not stop the queued view-selection copy batch: pre-copy={sink.PreCopyCalls}; aborted={aborted}; perform=0x{performResult:X8}.");
                        Console.WriteLine($"synthetic-shell-size-sort: cancelled-view-copy-batch=True; pre-copy-callbacks={sink.PreCopyCalls}; queued-view-copies=2; copied-items=0; any-operations-aborted={aborted}; perform-hr=0x{performResult:X8}");
                    }
                    finally { Check(At<Unadvise>(operation, 4)(operation, adviseCookie)); }
                }
                finally { Marshal.Release(sinkPointer); }
            }
            finally { Marshal.Release(destinationItem); }
        }
        finally { Marshal.Release(operation); }
    }

    private static void VerifyMultiSelectionCancellation(nint folderView, nint view, nint module, string backingFolder, string destinationFolder)
    {
        var listView = FindListView(view);
        var names = ReadNames(listView, module);
        var firstIndex = Array.FindIndex(names, name => string.Equals(name, "two.bin", StringComparison.OrdinalIgnoreCase));
        var secondIndex = Array.FindIndex(names, name => string.Equals(name, "z-ten.bin", StringComparison.OrdinalIgnoreCase));
        if (firstIndex < 0 || secondIndex < 0 || firstIndex == secondIndex)
            throw new InvalidOperationException("The isolated multi-select cancellation fixture items were not present in the Shell view.");

        var selectItem = At<SelectItem>(folderView, 15);
        Check(selectItem(folderView, firstIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));
        Check(selectItem(folderView, secondIndex, 0x1u | 0x40000000u));
        Check(At<GetItemCount>(folderView, 7)(folderView, 1, out var selectedCount));
        if (selectedCount != 2) throw new InvalidOperationException($"Expected two selected Shell items for the cancellation probe, got {selectedCount}.");

        var dataObjectId = DataObjectId;
        Check(At<GetItemObject>(view, 15)(view, 1, ref dataObjectId, out var selectedItems));
        if (selectedItems == 0) throw new InvalidOperationException("The two-item Shell selection did not produce a data object.");
        try
        {
            VerifyCancelledCopies(selectedItems, destinationFolder);
            Check(At<GetItemCount>(folderView, 7)(folderView, 1, out var selectedAfterCancel));
            var afterNames = ReadNames(listView, module);
            var firstState = StateForName(listView, afterNames, "two.bin");
            var secondState = StateForName(listView, afterNames, "z-ten.bin");
            if (selectedAfterCancel != 2 ||
                (firstState & (LvisSelected | LvisFocused)) != (LvisSelected | LvisFocused) ||
                (secondState & LvisSelected) == 0)
                throw new IOException($"Cancelling a multi-selection copy changed the view's selection or focus: selected={selectedAfterCancel}; first=0x{firstState:X}; second=0x{secondState:X}.");
            Console.WriteLine("synthetic-shell-size-sort: cancelled-multi-selection-preserved=True; selected=2; focused=two.bin");
            Pump(800); // Aborted IFileOperation still emits Shell invalidations; let the asynchronous view refresh finish before the next operation.
            VerifySuccessfulMultiSelectionCopy(selectedItems, backingFolder, Path.Combine(destinationFolder, "Successful"));
        }
        finally { Marshal.Release(selectedItems); }
    }

    private static void VerifySuccessfulMultiSelectionCopy(nint selectedItems, string backingFolder, string destinationFolder)
    {
        if (Directory.Exists(destinationFolder))
            throw new InvalidOperationException($"Refusing to reuse the isolated multi-copy destination: {destinationFolder}");
        Directory.CreateDirectory(destinationFolder);
        var fileOperationClass = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var fileOperationInterface = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        Check(CoCreateInstance(ref fileOperationClass, 0, 1, ref fileOperationInterface, out var operation));
        try
        {
            Check(At<SetOperationFlags>(operation, 5)(operation, 0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
            var shellItemInterface = ShellItemId;
            Check(SHCreateItemFromParsingName(destinationFolder, 0, ref shellItemInterface, out var destinationItem));
            try
            {
                Check(At<CopyItems>(operation, 17)(operation, selectedItems, destinationItem));
                Check(At<PerformOperations>(operation, 21)(operation));
                Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
                var expectedNames = new[] { "two.bin", "z-ten.bin" };
                var copiedNames = Directory.EnumerateFiles(destinationFolder).Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                if (aborted != 0 || !copiedNames.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase))
                    throw new IOException($"The selected Shell data object did not copy exactly two files: aborted={aborted}; files=[{string.Join('|', copiedNames)}].");
                foreach (var name in expectedNames)
                {
                    var sourceBytes = File.ReadAllBytes(Path.Combine(backingFolder, name));
                    var copiedBytes = File.ReadAllBytes(Path.Combine(destinationFolder, name));
                    if (!sourceBytes.AsSpan().SequenceEqual(copiedBytes))
                        throw new IOException($"The copied multi-selection item '{name}' did not match its source bytes.");
                }
                Console.WriteLine("synthetic-shell-size-sort: successful-multi-selection-copy=True; copied=2; sources-preserved=True; content-matches=True");
            }
            finally { Marshal.Release(destinationItem); }
        }
        finally { Marshal.Release(operation); }
    }

    [ComVisible(true)]
    [Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSinkProbe
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, nint newItem);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, nint newItem);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, nint newItem);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint newItem);
        [PreserveSig] int PreNewItem(uint flags, nint destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostNewItem(uint flags, nint destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, nint newItem);
        [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
        [PreserveSig] int ResetTimer(out ulong elapsed);
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class CancelFirstCopySink : IFileOperationProgressSinkProbe
    {
        public int PreCopyCalls { get; private set; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, nint item, string? newName) => 0;
        public int PostRenameItem(uint flags, nint item, string? newName, int result, nint newItem) => 0;
        public int PreMoveItem(uint flags, nint item, nint destination, string? newName) => 0;
        public int PostMoveItem(uint flags, nint item, nint destination, string? newName, int result, nint newItem) => 0;
        public int PreCopyItem(uint flags, nint item, nint destination, string? newName)
        {
            PreCopyCalls++;
            return unchecked((int)0x80004004); // An error from PreCopyItem cancels this and every queued operation.
        }
        public int PostCopyItem(uint flags, nint item, nint destination, string? newName, int result, nint newItem) => 0;
        public int PreDeleteItem(uint flags, nint item) => 0;
        public int PostDeleteItem(uint flags, nint item, int result, nint newItem) => 0;
        public int PreNewItem(uint flags, nint destination, uint attributes, string? name) => 0;
        public int PostNewItem(uint flags, nint destination, uint attributes, string? name, int result, nint newItem) => 0;
        public int UpdateProgress(uint workTotal, uint workSoFar) => 0;
        public int ResetTimer(out ulong elapsed) { elapsed = 0; return 0; }
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeSortColumn { public ShellColumnManager.NativeKey Key; public int Direction; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct FormatEtc
    {
        public short Format; public nint TargetDevice; public uint Aspect; public int Index; public uint Tymed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StorageMedium
    {
        public uint Tymed; public nint Value; public nint ReleaseUnknown;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Advise(nint self, nint progressSink, out uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Unadvise(nint self, uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MoveItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ReleaseViewHold();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SchedulePendingRefreshes();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CancelViewRefresh(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ScheduleSingleRefresh(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NotifySingleRefresh(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RenameItems(nint self, nint dataObject, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItems(nint self, nint dataObject);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int NewItem(nint self, nint destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, nint template, nint progressSink);
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewItem
    {
        public uint Mask; public int Item; public int SubItem; public uint State; public uint StateMask; public nint Text;
        public int TextCapacity; public int Image; public nint Parameter; public int Indent; public int GroupId;
        public uint ColumnCount; public nint Columns; public nint ColumnFormats; public int Group;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ResetCounters();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetProbeFlag(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSyntheticFolderClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSyntheticSizeOverrideDelegate(long size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateSyntheticView(nint rootPidl, out nint folder, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int ParseDisplayName(nint self, nint hwnd, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string displayName, nint eaten, out nint childPidl, nint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int FolderTreeContractSmoke([MarshalAs(UnmanagedType.LPWStr)] string backingRoot);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetBackingPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, in Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewModeAndIconSize(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumns(nint self, nint columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstCommand, uint lastCommand, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryGetData(nint self, in FormatEtc format);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetData(nint self, ref FormatEtc format, out StorageMedium medium);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint maxCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    private delegate bool EnumChildProcedure(nint child, nint parameter);

    // Observe native LVN_ITEMCHANGED so move-refresh traces show when DefView itself clears selection.
    private sealed class ListViewSelectionObserver : IDisposable
    {
        private const uint WmNotify = 0x004E;
        private const int LvnItemChanged = -101;
        private const uint LvisFocused = 0x0001;
        private const uint LvisSelected = 0x0002;
        private const uint LvmFirst = 0x1000;
        private const uint LvmSetItem = LvmFirst + 6;
        private const uint LvmDeleteItem = LvmFirst + 8;
        private const uint LvmDeleteAllItems = LvmFirst + 9;
        private const uint LvmSetItemState = LvmFirst + 43;
        private const uint LvmGetNextItem = LvmFirst + 12;
        private static readonly SubclassProcedure Procedure = WindowProcedure;
        private static nuint _nextSubclassId = 0xE170;
        private readonly nint _parent;
        private readonly nint _listView;
        private readonly nuint _subclassId;
        private readonly nuint _listViewSubclassId;
        private readonly GCHandle _self;
        private readonly string _label;

        internal ListViewSelectionObserver(nint listView, string label)
        {
            _label = label;
            _listView = listView;
            _parent = GetParent(listView);
            if (_parent == 0) throw new InvalidOperationException($"Could not find the {label} ListView notification parent.");
            _subclassId = unchecked(++_nextSubclassId);
            _listViewSubclassId = unchecked(++_nextSubclassId);
            _self = GCHandle.Alloc(this);
            if (!SetWindowSubclass(_listView, Procedure, _listViewSubclassId, GCHandle.ToIntPtr(_self)) ||
                !SetWindowSubclass(_parent, Procedure, _subclassId, GCHandle.ToIntPtr(_self)))
            {
                if (IsWindow(_listView)) _ = RemoveWindowSubclass(_listView, Procedure, _listViewSubclassId);
                _self.Free();
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    $"Could not observe {label} ListView selection notifications.");
            }
        }

        private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam,
            nuint subclassId, nint referenceData)
        {
            var result = default(nint);
            var defaultCalled = false;
            try
            {
                if (GCHandle.FromIntPtr(referenceData).Target is ListViewSelectionObserver observer &&
                    window == observer._listView &&
                    message is LvmSetItem or LvmSetItemState or LvmDeleteItem or LvmDeleteAllItems)
                {
                    var selectedBefore = GetListViewIndex(window, LvisSelected);
                    var focusedBefore = GetListViewIndex(window, LvisFocused);
                    result = DefSubclassProc(window, message, wParam, lParam);
                    defaultCalled = true;
                    var selectedAfter = GetListViewIndex(window, LvisSelected);
                    var focusedAfter = GetListViewIndex(window, LvisFocused);
                    if (selectedBefore != selectedAfter || focusedBefore != focusedAfter)
                    {
                        Console.WriteLine($"synthetic-shell-size-sort: ListView-state {observer._label} msg=0x{message:X}; index={unchecked((long)wParam)}; selected={selectedBefore}->{selectedAfter}; focused={focusedBefore}->{focusedAfter}");
                    }
                    return result;
                }

                if (message == WmNotify && lParam != 0)
                {
                    var header = Marshal.PtrToStructure<NativeNotifyHeader>(lParam);
                    if (header.Code == LvnItemChanged)
                    {
                        var change = Marshal.PtrToStructure<NativeListViewChange>(lParam);
                        if (((change.NewState ^ change.OldState) & (LvisSelected | LvisFocused)) != 0 &&
                            GCHandle.FromIntPtr(referenceData).Target is ListViewSelectionObserver changeObserver)
                        {
                            Console.WriteLine($"synthetic-shell-size-sort: LVN_ITEMCHANGED {changeObserver._label} item={change.Item}; old=0x{change.OldState:X}; new=0x{change.NewState:X}; changed=0x{change.Changed:X}");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"synthetic-shell-size-sort: selection-notify-observer-error={exception.GetType().Name}");
            }
            return defaultCalled ? result : DefSubclassProc(window, message, wParam, lParam);
        }

        private static int GetListViewIndex(nint listView, uint state)
            => checked((int)SendMessageW(listView, unchecked((int)LvmGetNextItem), unchecked((nint)(-1)), unchecked((nint)state)));

        public void Dispose()
        {
            if (_self.IsAllocated)
            {
                if (IsWindow(_parent)) _ = RemoveWindowSubclass(_parent, Procedure, _subclassId);
                if (IsWindow(_listView)) _ = RemoveWindowSubclass(_listView, Procedure, _listViewSubclassId);
                _self.Free();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeNotifyHeader
    {
        public nint WindowFrom; public nuint IdFrom; public int Code;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewChange
    {
        public NativeNotifyHeader Header; public int Item; public int SubItem;
        public uint NewState; public uint OldState; public uint Changed; public nint ItemData;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(
        nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nint referenceData);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CanUnloadNativeLibrary();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NativeLibraryUnloadContractSmoke();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint GetCapture();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint subclassId, nint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProcedure callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out NativeRect rect);
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
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SHGetPathFromIDListW(nint pidl, StringBuilder path);
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string name, nint bindContext, ref Guid interfaceId, out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
}
