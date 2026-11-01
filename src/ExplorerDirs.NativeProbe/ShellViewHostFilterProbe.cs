using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Collections.Concurrent;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

// Hidden direct-IShellView probe for the ICommDlgBrowser3 filtering route.
// It uses only a unique fixture and never creates ExplorerBrowser or user files.
internal static class ShellViewHostFilterProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid PropertyFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid ColumnManagerId = new("D8EC27BB-3F3B-4042-B10A-4ACFD924D453");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid FileOperationClassId = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid FileOperationId = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
    private const uint SbspParent = 0x2000;
    private const uint SbspNavigateBack = 0x4000;
    private const uint SbspNavigateForward = 0x8000;
    private const int GwlStyle = -16;
    private const long LvsTypeMask = 0x3;
    private const long LvsReport = 0x1;
    private const uint LvmDeleteAllItems = 0x1009;
    private const uint LvmInsertColumnW = 0x101B;
    private const uint LvmSetItemTextW = 0x1074;
    private const uint LvmInsertItemW = 0x104D;
    private const uint LvmGetItemCount = 0x1004;
    private const uint LvmGetTopIndex = 0x1027;
    private const uint LvmGetCountPerPage = 0x1028;
    private const uint LvmEnsureVisible = 0x1013;
    private const uint LvmGetItemTextW = 0x1073;
    private const uint LvmGetItemW = 0x104B;
    private const uint LvmGetItemState = 0x102C;
    private const uint LvmSetItemState = 0x102B;
    private const uint LvmSortItems = 0x1030;
    private const uint LvmSetImageList = 0x1003;
    private const uint LvmInsertGroup = 0x1091;
    private const uint LvmGetGroupCount = 0x1098;
    private const uint LvmEnableGroupView = 0x109D;
    private const uint LvmRemoveAllGroups = 0x10A0;
    private const uint LvmIsGroupViewEnabled = 0x10AF;
    private const uint ShellChangeCreated = 0x00000002;
    private const uint ShellChangeDeleted = 0x00000004;
    private const uint ShellChangeMakeDirectory = 0x00000008;
    private const uint ShellChangeRemoveDirectory = 0x00000010;
    private const uint ShellChangeRenamedItem = 0x00000001;
    private const uint ShellChangeRenamedFolder = 0x00020000;
    private const uint ShellChangeUpdatedItem = 0x00002000;
    private const uint ShellChangeUpdatedDirectory = 0x00001000;
    private const uint ShellChangePathUnicode = 0x00000005;
    private const uint SigDnFileSystemPath = 0x80058000;

    internal static int Run() => Run(stopAfterGroupedExport: false, leaveParentForThreadExit: false);

    internal static int RunGroupedExportOnly() => Run(stopAfterGroupedExport: true, leaveParentForThreadExit: false);

    internal static int RunForPrivateDesktop() => Run(stopAfterGroupedExport: false, leaveParentForThreadExit: true);

    internal static int RunMenuCommandsForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, menuCommandsOnly: true);

    internal static int RunNewFolderForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, newFolderOnly: true);

    internal static int RunRecursiveSizeProjectionForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, recursiveSizeProjectionOnly: true);

    internal static int RunMultiViewRecursiveSizeProjectionForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, recursiveSizeMultiViewProjectionOnly: true);

    internal static int RunVirtualScopeForPrivateDesktop(string scope) =>
        Run(stopAfterGroupedExport: false, leaveParentForThreadExit: true, virtualScopeOnly: scope);

    internal static string ProbeRegisteredNamespaceNavigation(nint rootPidl, nint childPidl, nint nestedPidl)
    {
        var foreground = GetForegroundWindow();
        nint parent = 0, folder = 0, view = 0, viewPointer = 0, viewWindow = 0, folderView = 0;
        var created = false;
        FilteredHost? host = null;

        int Navigate(nint targetPidl, uint flags)
        {
            nint childFolder = 0, childView = 0, childWindow = 0;
            var previousView = view;
            try
            {
                Check(SHBindToObject(0, targetPidl, 0, in ShellFolderId, out childFolder));
                var shellViewId = ShellViewId;
                Check(At<CreateViewObject>(childFolder, 8)(childFolder, parent, ref shellViewId, out childView));
                host!.View = childView;
                var settings = new StreamProbeSettings { Mode = 4 }; // FVM_DETAILS at CreateViewWindow time.
                var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
                Check(At<CreateViewWindow>(childView, 9)(childView, previousView, ref settings,
                    viewPointer, ref bounds, out childWindow));
                Check(At<ActivateView>(childView, 7)(childView, 2)); // SVUIA_ACTIVATE_NOFOCUS establishes DefView interaction routing.
                host.SetPendingView(childFolder, childView, childWindow);
                return 0;
            }
            catch (Exception ex)
            {
                host!.View = previousView;
                if (childWindow != 0) _ = At<DestroyViewWindow>(childView, 10)(childView);
                if (childView != 0) Marshal.Release(childView);
                if (childFolder != 0) Marshal.Release(childFolder);
                return Marshal.GetHRForException(ex);
            }
        }

        int AdoptPendingView(int expectedItemCount)
        {
            if (host!.PendingView == 0) throw new InvalidOperationException("Self-host navigation did not create a pending Shell view.");
            if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            created = false;
            if (view != 0) { Marshal.Release(view); view = 0; }
            if (folder != 0) { Marshal.Release(folder); folder = 0; }
            (folder, view, viewWindow) = host.TakePendingView();
            created = true;
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int itemCount;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out itemCount));
                if (itemCount == expectedItemCount) return itemCount;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            throw new TimeoutException($"Self-host Shell view did not enumerate {expectedItemCount} items; received {itemCount}.");
        }

        string ParsingName(nint pidl)
        {
            if (SHGetNameFromIDList(pidl, 0x80028000, out var name) < 0 || name == 0) return "<unavailable>";
            try { return Marshal.PtrToStringUni(name) ?? "<empty>"; }
            finally { Marshal.FreeCoTaskMem(name); }
        }

        try
        {
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated registered namespace IShellView host",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0); // WS_POPUP; hidden on the isolated Desktop.
            if (parent == 0) throw new InvalidOperationException("Could not create the hidden IShellView parent window.");
            Check(SHBindToObject(0, rootPidl, 0, in ShellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));
            host = new FilteredHost(parent, new ShellFolderFilter(new QuickFilterSettings("*", false, false)),
                rootPidl, childPidl, Navigate);
            host.View = view;
            viewPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = 4 }; // FVM_DETAILS at CreateViewWindow time.
            var initialBounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, viewPointer, ref initialBounds, out viewWindow));
            created = true;
            _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE on the isolated Private Desktop enables native header interaction.
            Check(At<ActivateView>(view, 7)(view, 2)); // SVUIA_ACTIVATE_NOFOCUS.
            _ = At<RefreshView>(view, 8)(view);
            Application.DoEvents();
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            int rootCount;
            var rootEnumerationDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out rootCount));
                if (rootCount >= 4) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < rootEnumerationDeadline);
            if (rootCount < 4) throw new TimeoutException($"Self-host root Shell view did not enumerate fixture items: {rootCount}.");
            var initialRootState = CaptureRecursiveSizeViewState(folder, viewWindow, folderView);
            var initialRootRestore = RestoreAndReadRecursiveSizeViewState(
                folder, view, viewWindow, folderView, rootCount, initialRootState, "initial-root");
            Marshal.Release(folderView);
            folderView = 0;
            Check(host.BrowseObject(childPidl, 0));
            var childCount = AdoptPendingView(expectedItemCount: 2);
            var childLocationMatches = ILIsEqual(host.CurrentPidl, childPidl);
            var childLocationBytesMatch = GetPidlIdentity(host.CurrentPidl) == GetPidlIdentity(childPidl);
            var childAndNestedShellEqual = ILIsEqual(childPidl, nestedPidl);
            var childAndNestedExactEqual = PidlExactlyEquals(childPidl, nestedPidl);
            Console.WriteLine($"ishellview-host-filter: registered-namespace-child=current-match:{childLocationMatches}; bytes-match:{childLocationBytesMatch}; child-nested-shell-equal:{childAndNestedShellEqual}; exact-equal:{childAndNestedExactEqual}; child-shape={DescribePidl(childPidl)}; nested-shape={DescribePidl(nestedPidl)}; child={ParsingName(childPidl)}; nested={ParsingName(nestedPidl)}; history-index={host.HistoryIndex}");
            var nestedIndex = FindFolderIndex(folderView, folder, childCount);
            if (nestedIndex < 0) throw new InvalidOperationException("The self-hosted child view did not expose its nested folder row.");
            Check(At<GetItem>(folderView, 6)(folderView, nestedIndex, out var nestedRowPidl));
            string nestedRecursiveSize;
            try
            {
                var sizeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                do
                {
                    Application.DoEvents();
                    Check(ReadShellDetail(folder, nestedRowPidl, 1, out nestedRecursiveSize));
                    if (nestedRecursiveSize == "10") break;
                    Thread.Sleep(20);
                } while (DateTime.UtcNow < sizeDeadline);
            }
            finally { Marshal.FreeCoTaskMem(nestedRowPidl); }
            Console.WriteLine($"ishellview-host-filter: registered-namespace-recursive-size={nestedRecursiveSize}; expected=10");
            var savedRecursiveViewState = CaptureRecursiveSizeViewState(folder, viewWindow, folderView);
            var initialRecursiveRestore = RestoreAndReadRecursiveSizeViewState(
                folder, view, viewWindow, folderView, childCount, savedRecursiveViewState, "initial-child");
            Check(host.BrowseObject(nestedPidl, 0));
            var nestedCount = AdoptPendingView(expectedItemCount: 1);
            var currentPidlMatches = PidlExactlyEquals(host.CurrentPidl, nestedPidl);
            Console.WriteLine($"ishellview-host-filter: registered-namespace-route=current:{currentPidlMatches}; history-index={host.HistoryIndex}; child-items={childCount}; nested-items={nestedCount}; current={ParsingName(host.CurrentPidl)}; expected={ParsingName(nestedPidl)}");
            if (!currentPidlMatches || host.HistoryIndex != 2 || childCount != 2 || nestedCount != 1)
                throw new InvalidOperationException($"Self-hosted namespace navigation mismatch: current={currentPidlMatches}; history={host.HistoryIndex}; child-items={childCount}; nested-items={nestedCount}.");
            var names = ReadVisibleItemNames(folderView, nestedCount);
            if (!names.Contains("deep.bin", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Self-hosted nested Shell view did not display its backing file.");
            if (nestedRecursiveSize != "10")
                throw new InvalidOperationException($"Self-hosted child view did not provide the recursive size column: {nestedRecursiveSize}.");
            Check(host.BrowseObject(0, SbspNavigateBack));
            var backCount = AdoptPendingView(expectedItemCount: 2);
            var backRestoredChild = host.HistoryIndex == 1 &&
                PidlExactlyEquals(host.CurrentPidl, childPidl) && backCount == 2;
            var backRecursiveRestore = RestoreAndReadRecursiveSizeViewState(
                folder, view, viewWindow, folderView, backCount, savedRecursiveViewState, "travel-log-back-child");
            Check(host.BrowseObject(0, SbspNavigateForward));
            var forwardCount = AdoptPendingView(expectedItemCount: 1);
            var forwardRestoredNested = host.HistoryIndex == 2 &&
                PidlExactlyEquals(host.CurrentPidl, nestedPidl) && forwardCount == 1;
            if (!backRestoredChild || !forwardRestoredNested)
                throw new InvalidOperationException($"Self-hosted Shell history mismatch: back={backRestoredChild}; forward={forwardRestoredNested}; index={host.HistoryIndex}.");
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("Self-hosted namespace navigation changed the foreground window.");
            return $"root->child->nested=True; root-items={rootCount}; root-recursive-state={initialRootRestore}; child-items={childCount}; nested-items={nestedCount}; nested-file={names[0]}; recursive-size={nestedRecursiveSize}; back-forward=True; history-index={host.HistoryIndex}; recursive-state-initial={initialRecursiveRestore}; recursive-state-after-back={backRecursiveRestore}";
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            host?.DisposePendingView();
            host?.Dispose();
            if (view != 0) Marshal.Release(view);
            if (folder != 0) Marshal.Release(folder);
            if (viewPointer != 0) Marshal.Release(viewPointer);
            // On this isolated non-input Desktop, the owning parent HWND is reclaimed at thread/process exit.
            GC.KeepAlive(host);
        }
    }

    private static ViewState CaptureRecursiveSizeViewState(nint folder, nint viewWindow, nint view)
    {
        var mapResult = At<MapColumnToScid>(folder, 19)(folder, 1, out var providerKey);
        Check(mapResult);
        var captureResult = ShellColumnManager.Capture(view, out var columns);
        Check(captureResult);
        if (columns is null) throw new InvalidOperationException("Shell returned no display-column snapshot for the recursive-size state probe.");
        // A real header interaction makes the Shell choose its own sort identity; capture that state instead of deriving it from provider metadata.
        ClickHeaderItem(viewWindow, 1);
        var sortCountResult = At<GetSortColumnCount>(view, 26)(view, out var sortCount);
        Check(sortCountResult);
        if (sortCount == 0) throw new InvalidOperationException("The native recursive-size header did not establish a Shell sort key.");
        var nativeSortColumns = new NativeSortColumn[sortCount];
        Check(At<GetSortColumns>(view, 28)(view, nativeSortColumns, sortCount));
        var sortColumns = nativeSortColumns.Select(column => new SortColumnState(
            new PropertyKeyState(column.Key.FormatId, column.Key.PropertyId), column.Direction)).ToArray();
        if (sortColumns.Any(column => !columns.Any(display => display.Key.FormatId == column.Key.FormatId && display.Key.PropertyId == column.Key.PropertyId)))
            throw new InvalidOperationException("The Shell-reported sort key is missing from its live display-column snapshot.");
        Console.WriteLine($"ishellview-native-column-state: provider-key={providerKey.FormatId:B}:{providerKey.PropertyId}; sort-columns=[{string.Join('|', nativeSortColumns.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}:{column.Direction}"))}]; columns=[{string.Join('|', columns.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}"))}]");
        return new ViewState(4, -1,
            sortColumns, null, true, columns);
    }

    private static string RestoreAndReadRecursiveSizeViewState(nint folder, nint shellView, nint viewWindow,
        nint view, int itemCount, ViewState saved, string stage)
    {
        var failures = new List<string>();
        var restored = ShellViewStateRestore.Restore(saved, new NativeShellViewStateTarget(view),
            (step, result) => failures.Add($"{step}=0x{result:X8}"), () => true);
        // Probe whether applying columns/sort after mode leaves the live DefView style stale.
        var modeReapplyResult = At<SetViewModeAndIconSize>(view, 35)(view, saved.Mode, saved.IconSize);
        var currentModeReapplyResult = At<SetCurrentViewMode>(view, 4)(view, saved.Mode);
        var refreshResult = At<RefreshView>(shellView, 8)(shellView);
        var modeReapplyDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(150);
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
        } while (DateTime.UtcNow < modeReapplyDeadline);
        var columnCaptureResult = ShellColumnManager.Capture(view, out var columns);
        var recursiveColumnVisible = columnCaptureResult >= 0 && columns is not null &&
            columns.Any(column => column.Key == saved.SortColumns[0].Key);
        var recursiveColumnIndex = columns is null
            ? -1
            : Enumerable.Range(0, columns.Count).FirstOrDefault(index => columns[index].Key == saved.SortColumns[0].Key, -1);
        var listViewForHeader = FindChildByClass(viewWindow, "SysListView32");
        var headerForDiagnostics = listViewForHeader == 0 ? 0 : SendMessageW(listViewForHeader, 0x101F, 0, 0);
        var headerCount = headerForDiagnostics == 0 ? -1 : SendMessageW(headerForDiagnostics, 0x1200, 0, 0).ToInt32();
        var nativeHeaderTitles = headerForDiagnostics == 0 ? [] : ReadNativeHeaderTitles(headerForDiagnostics, headerCount);
        var listViewMode = listViewForHeader == 0 ? -1 : SendMessageW(listViewForHeader, 0x108F, 0, 0).ToInt32(); // LVM_GETVIEW.
        var modeResult = At<GetViewModeAndIconSize>(view, 36)(view, out var currentMode, out var currentIconSize);
        var listViewStyle = listViewForHeader == 0 ? 0 : GetWindowLongPtrW(listViewForHeader, GwlStyle).ToInt64();
        var nativeColumns = ReadNativeColumnDiagnostics(folder);
        var headerDiagnostics = $"view-mode-hr=0x{modeResult:X8}; mode={currentMode}; icon-size={currentIconSize}; list-view-mode={listViewMode}; list-style=0x{listViewStyle:X}; list-visible={(listViewForHeader != 0 && IsWindowVisible(listViewForHeader))}; header-visible={(headerForDiagnostics != 0 && IsWindowVisible(headerForDiagnostics))}; header-count={headerCount}; header-titles=[{string.Join('|', nativeHeaderTitles)}]; native-columns=[{nativeColumns}]; recursive-column-index={recursiveColumnIndex}; column-keys=[{(columns is null ? "" : string.Join('|', columns.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}")))}]";
        var countResult = At<GetSortColumnCount>(view, 26)(view, out var sortCount);
        var sortReadbackResult = countResult;
        NativeSortColumn[] sorts = [];
        if (countResult >= 0 && sortCount > 0)
        {
            sorts = new NativeSortColumn[sortCount];
            sortReadbackResult = At<GetSortColumns>(view, 28)(view, sorts, sortCount);
        }
        var names = ReadVisibleItemNames(view, itemCount);
        var values = ReadRecursiveSizeValues(folder, view, itemCount);
        var sortMatches = sortReadbackResult >= 0 && sorts.Length == 1 &&
            sorts[0].Key.FormatId == saved.SortColumns[0].Key.FormatId &&
            sorts[0].Key.PropertyId == saved.SortColumns[0].Key.PropertyId &&
            sorts[0].Direction == saved.SortColumns[0].Direction;
        var orderBeforeRestoreRetry = string.Join('|', names);
        var valuesBeforeRestoreRetry = string.Join('|', values);
        var valueOrder = values.Select(entry =>
        {
            var separator = entry.LastIndexOf('=');
            return separator < 0 || !ulong.TryParse(entry.AsSpan(separator + 1), out var bytes)
                ? (Name: entry, Bytes: ulong.MaxValue)
                : (Name: entry[..separator], Bytes: bytes);
        }).ToArray();
        var expectedNames = saved.SortColumns[0].Direction > 0
            ? valueOrder.OrderBy(entry => entry.Bytes).Select(entry => entry.Name).ToArray()
            : valueOrder.OrderByDescending(entry => entry.Bytes).Select(entry => entry.Name).ToArray();
        var orderMatchesSavedSort = names.SequenceEqual(expectedNames,
            StringComparer.OrdinalIgnoreCase);
        string? shellMenuSortFailure = null;
        string[] namesAfterShellMenuSort = names;
        string[] valuesAfterShellMenuSort = values;
        var shellMenuSortReadback = "not-run";
        if (stage == "initial-root")
        {
            try
            {
                var opposite = saved.SortColumns.Select(column => column with { Direction = -column.Direction }).ToArray();
                var target = new NativeShellViewStateTarget(view);
                Check(target.SetSorting(opposite));
                InvokeBackgroundVerb(shellView, GetParent(viewWindow),
                    saved.SortColumns[0].Direction > 0 ? "sortascending" : "sortdescending");
                var settleDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(350);
                do
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                } while (DateTime.UtcNow < settleDeadline);
                namesAfterShellMenuSort = ReadVisibleItemNames(view, itemCount);
                valuesAfterShellMenuSort = ReadRecursiveSizeValues(folder, view, itemCount);
                var afterMenuCountHr = At<GetSortColumnCount>(view, 26)(view, out var afterMenuCount);
                if (afterMenuCountHr >= 0 && afterMenuCount > 0)
                {
                    var afterMenuSorts = new NativeSortColumn[afterMenuCount];
                    var afterMenuReadHr = At<GetSortColumns>(view, 28)(view, afterMenuSorts, afterMenuCount);
                    shellMenuSortReadback = afterMenuReadHr < 0
                        ? $"hr=0x{afterMenuReadHr:X8}"
                        : string.Join('|', afterMenuSorts.Select(sort => $"{sort.Key.FormatId:B}:{sort.Key.PropertyId}:{sort.Direction}"));
                }
                else shellMenuSortReadback = $"count-hr=0x{afterMenuCountHr:X8};count={afterMenuCount}";
            }
            catch (Exception ex) { shellMenuSortFailure = $"{ex.GetType().Name}: {ex.Message}"; }
        }
        string? headerSortFailure = null;
        string[] namesAfterNativeColumnOne = names;
        string[] namesAfterSnapshotColumn = names;
        string[] namesAfterNativeColumnOneFirstClick = names;
        string[] namesAfterSnapshotColumnFirstClick = names;
        string sortAfterNativeColumnOneFirstClick = "not-run";
        string sortAfterSnapshotColumnFirstClick = "not-run";
        var headerSlotProbes = new List<string>();
        try
        {
            if (recursiveColumnIndex < 0)
                throw new InvalidOperationException("The saved recursive-size column is not present in the native header.");
            // The shared setter restores metadata but may not establish the DefView's comparator for provider columns.
            if (stage == "initial-root")
            {
                var target = new NativeShellViewStateTarget(view);
                for (var slot = 0; slot < headerCount; slot++)
                {
                    Check(target.SetSorting(saved.SortColumns));
                    var compareCountBefore = ReadSyntheticCompareCount(slot);
                    ClickHeaderItem(viewWindow, slot);
                    var slotNames = ReadVisibleItemNames(view, itemCount);
                    var slotSort = ReadSortColumnsSummary(view);
                    var compareCountAfter = ReadSyntheticCompareCount(slot);
                    headerSlotProbes.Add($"slot={slot};sort=[{slotSort}];compare-calls={compareCountBefore}->{compareCountAfter};order=[{string.Join('|', slotNames)}]");
                    if (slot == 1)
                    {
                        namesAfterNativeColumnOneFirstClick = slotNames;
                        sortAfterNativeColumnOneFirstClick = slotSort;
                    }
                    if (slot == recursiveColumnIndex)
                    {
                        namesAfterSnapshotColumnFirstClick = slotNames;
                        sortAfterSnapshotColumnFirstClick = slotSort;
                    }
                    if (slot == 0) namesAfterNativeColumnOne = slotNames;
                }
            }
            else
            {
                ClickHeaderItem(viewWindow, recursiveColumnIndex);
                ClickHeaderItem(viewWindow, recursiveColumnIndex);
            }
            namesAfterSnapshotColumn = ReadVisibleItemNames(view, itemCount);
        }
        catch (Exception ex) { headerSortFailure = $"{ex.GetType().Name}: {ex.Message}"; }
        var namesAfterHeaderSort = ReadVisibleItemNames(view, itemCount);
        var valuesAfterHeaderSort = ReadRecursiveSizeValues(folder, view, itemCount);
        var headerSortMatchesSavedSort = namesAfterHeaderSort.SequenceEqual(expectedNames,
            StringComparer.OrdinalIgnoreCase);
        var sortAfterHeaderCountResult = At<GetSortColumnCount>(view, 26)(view, out var sortAfterHeaderCount);
        var sortAfterHeaderMatches = false;
        if (sortAfterHeaderCountResult >= 0 && sortAfterHeaderCount == 1)
        {
            var sortAfterHeader = new NativeSortColumn[1];
            sortAfterHeaderMatches = At<GetSortColumns>(view, 28)(view, sortAfterHeader, 1) >= 0 &&
                sortAfterHeader[0].Key.FormatId == saved.SortColumns[0].Key.FormatId &&
                sortAfterHeader[0].Key.PropertyId == saved.SortColumns[0].Key.PropertyId &&
                sortAfterHeader[0].Direction == saved.SortColumns[0].Direction;
        }
        var result = $"restored={restored}; failures=[{string.Join(',', failures)}]; mode-reapply-hr=0x{modeReapplyResult:X8}; current-mode-reapply-hr=0x{currentModeReapplyResult:X8}; refresh-hr=0x{refreshResult:X8}; columns-hr=0x{columnCaptureResult:X8}; recursive-column-visible={recursiveColumnVisible}; sort-count-hr=0x{countResult:X8}; sort-readback-hr=0x{sortReadbackResult:X8}; sort-matches={sortMatches}; header-diagnostics={headerDiagnostics}; order=[{orderBeforeRestoreRetry}]; recursive-values=[{valuesBeforeRestoreRetry}]; order-matches-saved-sort={orderMatchesSavedSort}; shell-menu-sort-failure={shellMenuSortFailure ?? "none"}; sort-after-shell-menu=[{shellMenuSortReadback}]; order-after-shell-menu-sort=[{string.Join('|', namesAfterShellMenuSort)}]; values-after-shell-menu-sort=[{string.Join('|', valuesAfterShellMenuSort)}]; header-sort-failure={headerSortFailure ?? "none"}; header-slot-probes=[{string.Join(" || ", headerSlotProbes)}]; order-after-native-column-one-first-click=[{string.Join('|', namesAfterNativeColumnOneFirstClick)}]; sort-after-native-column-one-first-click=[{sortAfterNativeColumnOneFirstClick}]; order-after-native-column-one=[{string.Join('|', namesAfterNativeColumnOne)}]; order-after-snapshot-column-first-click=[{string.Join('|', namesAfterSnapshotColumnFirstClick)}]; sort-after-snapshot-column-first-click=[{sortAfterSnapshotColumnFirstClick}]; order-after-snapshot-column=[{string.Join('|', namesAfterSnapshotColumn)}]; order-after-header-sort=[{string.Join('|', namesAfterHeaderSort)}]; values-after-header-sort=[{string.Join('|', valuesAfterHeaderSort)}]; header-sort-matches-saved-sort={headerSortMatchesSavedSort}; sort-after-header-matches={sortAfterHeaderMatches}";
        Console.WriteLine($"ishellview-state-restore: stage={stage}; {result}");
        return result;
    }

    private static string ReadSortColumnsSummary(nint view)
    {
        var countHr = At<GetSortColumnCount>(view, 26)(view, out var count);
        if (countHr < 0 || count == 0) return $"count-hr=0x{countHr:X8};count={count}";
        var columns = new NativeSortColumn[count];
        var readHr = At<GetSortColumns>(view, 28)(view, columns, count);
        return readHr < 0
            ? $"read-hr=0x{readHr:X8}"
            : string.Join('|', columns.Select(column => $"{column.Key.FormatId:B}:{column.Key.PropertyId}:{column.Direction}"));
    }

    private static string ReadNativeColumnDiagnostics(nint folder)
    {
        var columns = new List<string>();
        for (uint index = 0; index < 8; index++)
        {
            try
            {
                var hr = ReadShellDetail(folder, 0, index, out var title);
                if (hr < 0)
                {
                    columns.Add($"{index}:details-hr=0x{hr:X8}");
                    continue;
                }
                var mapHr = At<MapColumnToScid>(folder, 19)(folder, index, out var key);
                var mapping = mapHr < 0 ? $"map-hr=0x{mapHr:X8}" : $"key={key.FormatId:B}:{key.PropertyId}";
                columns.Add($"{index}:title={title};{mapping}");
            }
            catch (Exception ex)
            {
                columns.Add($"{index}:error={ex.GetType().Name}:0x{Marshal.GetHRForException(ex):X8}");
            }
        }
        return string.Join('|', columns);
    }

    private static string[] ReadNativeHeaderTitles(nint header, int count)
    {
        if (count < 0 || count > 64) return [$"invalid-count:{count}"];
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            var titles = new string[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt16(buffer, 0);
                var item = new NativeHeaderItem { Mask = 2, Text = buffer, TextCapacity = 256 };
                if (SendHeaderMessage(header, 0x120B, index, ref item) == 0)
                    titles[index] = $"{index}:<read-failed>";
                else titles[index] = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            return titles;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int ReadSyntheticCompareCount(int column)
    {
        var module = NativeLibrary.Load(FindAdapterLibrary());
        try
        {
            var read = Marshal.GetDelegateForFunctionPointer<GetSyntheticSortCompareCountForColumn>(
                NativeLibrary.GetExport(module, "GetSyntheticSortCompareCountForColumn"));
            return read((uint)column);
        }
        finally { NativeLibrary.Free(module); }
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

    private static void ClickHeaderItem(nint viewWindow, int itemIndex)
    {
        var listView = FindChildByClass(viewWindow, "SysListView32");
        var header = listView == 0 ? 0 : SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER.
        if (header == 0) throw new InvalidOperationException("The restored Shell view has no native header control.");
        _ = ShowWindow(header, 4); // SW_SHOWNOACTIVATE, isolated view only; test whether a hidden but present header owns sorting.
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            if (SendMessageW(header, 0x1207, itemIndex, rectanglePointer) == 0) // HDM_GETITEMRECT.
                throw new InvalidOperationException($"The native header has no item at column {itemIndex}.");
            var rectangle = Marshal.PtrToStructure<NativeRect>(rectanglePointer);
            var x = (rectangle.Left + rectangle.Right) / 2;
            var y = (rectangle.Top + rectangle.Bottom) / 2;
            var point = unchecked((nint)((y << 16) | (x & 0xffff)));
            _ = SendMessageW(header, 0x0200, 0, point); // WM_MOUSEMOVE.
            _ = SendMessageW(header, 0x0201, 1, point); // WM_LBUTTONDOWN.
            _ = SendMessageW(header, 0x0202, 0, point); // WM_LBUTTONUP.
        }
        finally { Marshal.FreeHGlobal(rectanglePointer); }
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(350);
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
    }

    private static string[] ReadRecursiveSizeValues(nint folder, nint view, int count)
    {
        var values = new string[count];
        for (var index = 0; index < count; index++)
        {
            Check(At<GetItem>(view, 6)(view, index, out var pidl));
            try
            {
                Check(ReadShellDetail(folder, pidl, 1, out var value));
                values[index] = $"{ReadShellItemDisplayName(folder, pidl)}={value}";
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        return values;
    }

    private static string ReadShellItemDisplayName(nint folder, nint pidl)
    {
        var itemId = ShellItemId;
        Check(SHCreateItemWithParent(0, folder, pidl, ref itemId, out var item));
        try
        {
            Check(At<GetItemDisplayName>(item, 5)(item, 0, out var name));
            try { return Marshal.PtrToStringUni(name) ?? string.Empty; }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.Release(item); }
    }

    private sealed class NativeShellViewStateTarget(nint view) : IShellViewStateTarget
    {
        public int SetMode(uint mode, int iconSize) =>
            At<SetViewModeAndIconSize>(view, 35)(view, mode, iconSize);

        public int SetColumns(IReadOnlyList<DisplayColumnState> columns) =>
            ShellColumnManager.Apply(view, columns);

        public int SetSorting(IReadOnlyList<SortColumnState> columns)
        {
            var native = columns.Select(column => new NativeSortColumn
            {
                Key = new ShellColumnManager.NativeKey
                {
                    FormatId = column.Key.FormatId,
                    PropertyId = column.Key.PropertyId
                },
                Direction = column.Direction
            }).ToArray();
            return At<SetSortColumns>(view, 27)(view, native, native.Length);
        }

        public int SetGrouping(PropertyKeyState? key, bool ascending)
        {
            var native = key is null ? default : new ShellColumnManager.NativeKey
            {
                FormatId = key.FormatId,
                PropertyId = key.PropertyId
            };
            return At<SetGroupBy>(view, 17)(view, ref native, ascending ? 1 : 0);
        }
    }

    private static int Run(bool stopAfterGroupedExport, bool leaveParentForThreadExit, string? virtualScopeOnly = null,
        bool menuCommandsOnly = false, bool newFolderOnly = false, bool recursiveSizeProjectionOnly = false,
        bool recursiveSizeMultiViewProjectionOnly = false)
    {
        var foreground = GetForegroundWindow();
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
        string? zipPath = null;
        nint parent = 0, folderPidl = 0, childFolderPidl = 0, grandchildFolderPidl = 0, alternateFolderPidl = 0, volumeRootPidl = 0, virtualFolderPidl = 0, filePidl = 0, folder = 0, view = 0, viewPointer = 0, viewWindow = 0, folderView = 0;
        bool created = false;
        var failAfterCreateViewObject = false;
        var failAfterCreateViewWindow = false;
        nint failedNavigationWindow = 0;
        var itemCountLastHResult = 0;
        FilteredHost? host = null;
        Exception? navigationError = null;

        int Navigate(nint targetPidl, uint flags)
        {
            nint childFolder = 0, childView = 0, childWindow = 0;
            var previousView = view;
            try
            {
                Check(SHBindToObject(0, targetPidl, 0, in ShellFolderId, out childFolder));
                var shellViewId = ShellViewId;
                Check(At<CreateViewObject>(childFolder, 8)(childFolder, parent, ref shellViewId, out childView));
                if (failAfterCreateViewObject)
                {
                    // Exercise rollback after COM view creation but before any replacement HWND is requested.
                    failAfterCreateViewObject = false;
                    Marshal.ThrowExceptionForHR(unchecked((int)0x80004005));
                }
                host!.View = childView;
                var settings = new StreamProbeSettings { Mode = uint.MaxValue };
                var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
                var hr = At<CreateViewWindow>(childView, 9)(childView, previousView, ref settings, viewPointer, ref bounds, out childWindow);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (failAfterCreateViewWindow)
                {
                    // Exercise rollback after the replacement HWND exists but before ownership transfers to the host.
                    failAfterCreateViewWindow = false;
                    failedNavigationWindow = childWindow;
                    Marshal.ThrowExceptionForHR(unchecked((int)0x80004005));
                }
                host.SetPendingView(childFolder, childView, childWindow);
                return 0;
            }
            catch (Exception ex)
            {
                navigationError = ex;
                host!.View = previousView;
                if (childWindow != 0) _ = At<DestroyViewWindow>(childView, 10)(childView);
                if (childView != 0) Marshal.Release(childView);
                if (childFolder != 0) Marshal.Release(childFolder);
                return Marshal.GetHRForException(ex);
            }
        }
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            File.WriteAllText(Path.Combine(fixture, "child", "inside.txt"), "inside");
            if (recursiveSizeProjectionOnly || recursiveSizeMultiViewProjectionOnly)
            {
                foreach (var (name, size) in new[] { ("size-2", 2), ("size-10", 10), ("size-100", 100) })
                {
                    var directory = Path.Combine(fixture, name);
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, "payload.bin"), new byte[size]);
                }
            }
            if (menuCommandsOnly)
                Directory.CreateDirectory(Path.Combine(fixture, "aardvark"));
            Check(SHParseDisplayName(Path.Combine(fixture, "child"), 0, out childFolderPidl, 0, out _));
            File.WriteAllText(Path.Combine(fixture, "keep.txt"), "keep");
            Check(SHParseDisplayName(Path.Combine(fixture, "keep.txt"), 0, out filePidl, 0, out _));
            Check(SHParseDisplayName(Path.GetPathRoot(Environment.SystemDirectory)!, 0, out volumeRootPidl, 0, out _));
            var volumeParentPidl = TryGetParentPidl(volumeRootPidl);
            if (volumeParentPidl == 0)
                throw new Exception("The Shell drive root did not resolve its namespace parent.");
            Marshal.FreeCoTaskMem(volumeParentPidl);
            if (TryGetParentPidl(0) != 0)
                throw new Exception("The Shell desktop namespace root unexpectedly produced a parent PIDL.");
            Console.WriteLine("ishellview-host-filter: shell-parent-boundary=True; drive-root-parent=available; desktop-root-parent=none");
            File.WriteAllText(Path.Combine(fixture, "filter.bin"), "filter");
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden ICommDlgBrowser3 filter probe",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0); // WS_POPUP; never shown.
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            var shellFolderId = ShellFolderId;
            Check(SHBindToObject(0, folderPidl, 0, in shellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));

            host = new FilteredHost(parent, new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), folderPidl, childFolderPidl, Navigate);
            List<uint> controlWindowRequests = [];
            List<string?> statusTextChanges = [];
            if (newFolderOnly)
            {
                host.ControlWindowProvider = id => { controlWindowRequests.Add(id); return 0; };
                host.StatusTextChanged += text => statusTextChanges.Add(text);
            }
            host.View = view;
            viewPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, viewPointer, ref bounds, out viewWindow));
            created = true;
            var folderViewId = FolderViewId;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            var expectedVisibleCount = recursiveSizeProjectionOnly || recursiveSizeMultiViewProjectionOnly ? 5 : menuCommandsOnly ? 3 : 2;
            int count;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count)); // SVGIO_ALLVIEW
                if (count == expectedVisibleCount && host.IncludeObjectCalls >= 3) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: includecalls={host.IncludeObjectCalls}; filtercalls={host.Filter.ShouldShowCalls}; visible-items={count}; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            var passed = host.IncludeObjectCalls >= 3 && host.Filter.ShouldShowCalls >= 3 && count == expectedVisibleCount &&
                !IsWindowVisible(parent) && GetForegroundWindow() == foreground;
            if (!passed)
                throw new Exception("Direct IShellView did not apply the ICommDlgBrowser3 filter to the isolated fixture.");
            if (recursiveSizeProjectionOnly)
            {
                VerifyRecursiveSizePeerProjection(view, folderView, folderPidl, folder, parent, count, () =>
                {
                    Check(At<DestroyViewWindow>(view, 10)(view));
                    created = false; // The projection probe destroys the real Shell view while a scan is pending.
                });
                return 0;
            }
            if (recursiveSizeMultiViewProjectionOnly)
            {
                VerifyMultiViewRecursiveSizeProjection(view, folderView, folderPidl, folder, parent, count, fixture, childFolderPidl);
                return 0;
            }
            if (virtualScopeOnly is not null)
            {
                var scope = virtualScopeOnly switch
                {
                    "Desktop" => "shell:Desktop",
                    "ThisPC" => "shell:MyComputerFolder",
                    "RecycleBin" => "shell:RecycleBinFolder",
                    "ControlPanel" => "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                    "Libraries" => "shell:Libraries",
                    "NetworkPlaces" => "shell:NetworkPlacesFolder",
                    "SearchFolder" => ShellSearchLocation.BuildUri(fixture, "nested"),
                    "ZipContainer" => CreateZipFixture(),
                    _ => throw new ArgumentOutOfRangeException(nameof(virtualScopeOnly), virtualScopeOnly, "Unknown virtual Shell probe target.")
                };
                Console.WriteLine($"ishellview-host-filter: virtual-probe-start={virtualScopeOnly}");
                BrowseVirtualScope(scope, virtualScopeOnly);
                return 0;
            }
            if (menuCommandsOnly)
            {
                VerifyNativeMenuCommands(view, folderView, parent, foreground, folderPidl);
                return 0;
            }
            if (newFolderOnly)
            {
                _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE, still confined to the Private Desktop.
                Check(At<ActivateView>(view, 7)(view, 1)); // SVUIA_ACTIVATE_FOCUS.
                var newFolderListView = FindChildByClass(viewWindow, "SysListView32");
                if (newFolderListView == 0) throw new Exception("The activated Shell view has no native list control.");
                _ = SetFocus(newFolderListView);
                Application.DoEvents();
                var listFocusBefore = GetFocus() == newFolderListView;
                var before = Directory.GetFileSystemEntries(fixture).Select(entry => Path.GetFileName(entry)!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                var attempts = new List<string>();
                var invocationModes = new[]
                {
                    (Name: "offset-no-ui", Mask: 0x400u, Canonical: false),
                    (Name: "offset", Mask: 0u, Canonical: false),
                    (Name: "ansi-verb", Mask: 0u, Canonical: true)
                };
                foreach (var mode in invocationModes)
                {
                    var invokeHr = InvokeBackgroundVerbRaw(view, parent, "newfolder",
                        invokeMask: mode.Mask, canonicalString: mode.Canonical);
                    var settleUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
                    while (DateTime.UtcNow < settleUntil)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    var afterAttempt = Directory.GetFileSystemEntries(fixture).Select(entry => Path.GetFileName(entry)!)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                    attempts.Add($"{mode.Name}=0x{invokeHr:X8},entries=[{string.Join('|', afterAttempt)}]");
                    if (invokeHr >= 0 || !before.SequenceEqual(afterAttempt, StringComparer.OrdinalIgnoreCase)) break;
                }
                var after = Directory.GetFileSystemEntries(fixture).Select(entry => Path.GetFileName(entry)!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out var afterViewCount));
                var added = after.Except(before, StringComparer.OrdinalIgnoreCase).ToArray();
                var canonicalVerbSucceeded = attempts.Any(attempt => attempt.StartsWith("ansi-verb=0x00000000", StringComparison.Ordinal));
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out var createdSelectionCount));
                Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex));
                var focusedName = focusedIndex >= 0 && focusedIndex < afterViewCount
                    ? ReadVisibleItemNames(folderView, afterViewCount)[focusedIndex]
                    : string.Empty;
                var renameEditor = FindChildByClass(viewWindow, "Edit");
                var renameEditorAfterCreate = renameEditor != 0;
                var focusedWindowClass = new System.Text.StringBuilder(128);
                if (GetFocus() != 0) _ = GetClassNameW(GetFocus(), focusedWindowClass, focusedWindowClass.Capacity);
                if (!IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("The isolated New Folder diagnostic changed the desktop boundary.");
                if (!listFocusBefore || !canonicalVerbSucceeded || added.Length != 1 || afterViewCount != 3 ||
                    !Directory.Exists(Path.Combine(fixture, added[0])))
                    throw new Exception("The focused Shell view's newfolder canonical verb did not create exactly one visible fixture directory.");

                var addedIndex = Array.FindIndex(ReadVisibleItemNames(folderView, afterViewCount),
                    name => name.Equals(added[0], StringComparison.OrdinalIgnoreCase));
                if (addedIndex < 0) throw new Exception("The created folder has no row in the Shell view.");
                Check(At<SelectItem>(folderView, 15)(folderView, addedIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));
                _ = SetFocus(newFolderListView);
                var f2 = new NativeMessage { Window = newFolderListView, Id = 0x0100, WParam = 0x71, LParam = 0x003C0001 };
                var f2Hr = At<TranslateAccelerator>(view, 5)(view, ref f2);
                var renameDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                while (DateTime.UtcNow < renameDeadline && renameEditor == 0)
                {
                    Application.DoEvents();
                    renameEditor = FindChildByClass(viewWindow, "Edit");
                    if (renameEditor == 0) Thread.Sleep(10);
                }
                if (renameEditor == 0) throw new Exception($"Shell TranslateAccelerator F2 did not open an inline rename editor: hr=0x{f2Hr:X8}.");
                var focusInRenameEditor = GetFocus() == renameEditor;
                var arrowResults = new List<string>();
                foreach (var arrow in new[]
                {
                    (Name: "left", Key: 0x25u, LParam: 0x004B0001u),
                    (Name: "up", Key: 0x26u, LParam: 0x00480001u),
                    (Name: "right", Key: 0x27u, LParam: 0x004D0001u),
                    (Name: "down", Key: 0x28u, LParam: 0x00500001u)
                })
                {
                    var message = new NativeMessage { Window = renameEditor, Id = 0x0100, WParam = arrow.Key, LParam = (nint)arrow.LParam };
                    var arrowHr = At<TranslateAccelerator>(view, 5)(view, ref message);
                    Application.DoEvents();
                    arrowResults.Add($"{arrow.Name}=0x{arrowHr:X8},edit-alive={IsWindow(renameEditor)},edit-focus={GetFocus() == renameEditor}");
                    if (!IsWindow(renameEditor) || GetFocus() != renameEditor)
                        throw new Exception($"Shell {arrow.Name} accelerator left the inline rename editor.");
                }
                Console.WriteLine($"ishellview-newfolder-diagnostic: list-focus-before={listFocusBefore}; attempts=[{string.Join(';', attempts)}]; filesystem-before=[{string.Join('|', before)}]; filesystem-after=[{string.Join('|', after)}]; view-items-before=2; view-items-after={afterViewCount}; canonical-verb-succeeded={canonicalVerbSucceeded}; selected-count-after-create={createdSelectionCount}; focused-index-after-create={focusedIndex}; focused-name-after-create={focusedName}; rename-editor-after-create={renameEditorAfterCreate}; focused-window-class-after-create={focusedWindowClass}; f2-hr=0x{f2Hr:X8}; f2-focus-in-edit={focusInRenameEditor}; arrow-routing=[{string.Join(',', arrowResults)}]; get-control-window-ids=[{string.Join(',', controlWindowRequests)}]; status-callbacks=[{string.Join('|', statusTextChanges)}]; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                return 0;
            }
            VerifyGroupedFilteredExport(folderView);
            if (stopAfterGroupedExport)
            {
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Grouped export probe changed the hidden desktop boundary.");
                Console.WriteLine("ishellview-grouped-filter-export: parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            var initialView = view;
            var initialBackHr = host.BrowseObject(0, SbspNavigateBack);
            if (initialBackHr >= 0 || host.View != initialView || host.PendingView != 0 || host.HistoryIndex != 0 || !ILIsEqual(host.CurrentPidl, folderPidl))
                throw new Exception("Back at the first history entry changed the active view or history position.");
            Console.WriteLine("ishellview-host-filter: back-boundary-rejected=True; history-index=0; active-view-preserved=True");
            var childIndex = FindFolderIndex(folderView, folder, count);
            if (childIndex < 0) throw new Exception("The filtered view did not retain its fixture subfolder.");
            Check(At<SelectItem>(folderView, 15)(folderView, childIndex, 1)); // SVSI_SELECT
            var selectedFolderVerbs = ReadContextMenuVerbs(view, 1); // SVGIO_SELECTION
            var selectedFolderHasOpen = selectedFolderVerbs.Contains("open", StringComparer.OrdinalIgnoreCase);
            if (!selectedFolderHasOpen)
                throw new Exception("The selected Shell folder did not expose its native Open context-menu verb.");
            Check(At<SelectItem>(folderView, 15)(folderView, childIndex, 0)); // SVSI_DESELECT
            var itemNames = ReadVisibleItemNames(folderView, count);
            var fileIndex = Array.FindIndex(itemNames, name => name.Equals("keep.txt", StringComparison.OrdinalIgnoreCase));
            if (fileIndex < 0) throw new Exception("The filtered view did not retain its fixture file.");
            Check(At<SelectItem>(folderView, 15)(folderView, fileIndex, 1)); // SVSI_SELECT
            var selectedFileVerbs = ReadContextMenuVerbs(view, 1); // SVGIO_SELECTION
            Check(At<SelectItem>(folderView, 15)(folderView, fileIndex, 0)); // SVSI_DESELECT
            var backgroundVerbs = ReadContextMenuVerbs(view, 0); // SVGIO_BACKGROUND
            var selectedFileHasCopy = selectedFileVerbs.Contains("copy", StringComparer.OrdinalIgnoreCase);
            var backgroundHasRefresh = backgroundVerbs.Contains("refresh", StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"ishellview-host-filter: selected-folder-menu-has-open={selectedFolderHasOpen}; selected-file-menu-has-copy={selectedFileHasCopy}; background-menu-has-refresh={backgroundHasRefresh}");
            if (!selectedFileHasCopy || !backgroundHasRefresh)
                throw new Exception("The native context-menu scopes did not resolve file Copy and background Refresh commands independently.");
            var listView = FindChildByClass(viewWindow, "SysListView32");
            if (listView == 0) throw new Exception("The isolated Shell view has no native file list window.");
            VerifySynchronizedViewModes(folderView, listView, host, viewWindow, parent);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedCount)); // SVGIO_SELECTION
            if (selectedCount != 0) throw new Exception("The fixture view unexpectedly started with a selection.");
            var callbackSelectionVersionBefore = host.CallbackSelectionVersion;
            var keyboardState = new byte[256];
            if (!GetKeyboardState(keyboardState)) throw new Exception("Could not snapshot the isolated probe thread keyboard state.");
            var ctrlDownState = (byte[])keyboardState.Clone();
            ctrlDownState[0x11] |= 0x80; // VK_CONTROL down for the direct Shell accelerator call.
            int selectAllHr;
            try
            {
                var activateHr = At<ActivateView>(view, 6)(view, 2); // SVUIA_ACTIVATE_FOCUS.
                if (activateHr < 0) Marshal.ThrowExceptionForHR(activateHr);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Activating the isolated Shell view crossed the hidden desktop boundary.");
                _ = SetFocus(listView);
                if (GetFocus() != listView) throw new Exception("The isolated Shell view did not accept thread-local list focus.");
                if (!SetKeyboardState(ctrlDownState)) throw new Exception("Could not set the isolated probe thread modifier state.");
                var ctrlA = new NativeMessage { Window = listView, Id = 0x0100, WParam = (nuint)'A', LParam = 0x001E0001 };
                selectAllHr = At<TranslateAccelerator>(view, 5)(view, ref ctrlA);
            }
            finally
            {
                if (!SetKeyboardState(keyboardState)) throw new Exception("Could not restore the isolated probe thread keyboard state.");
            }
            Check(selectAllHr);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            Console.WriteLine($"ishellview-host-filter: accelerator-ctrl-a-hr=0x{selectAllHr:X8}; selection={selectedCount}/{count}; callback-selection-version={callbackSelectionVersionBefore}->{host.CallbackSelectionVersion}; focus={GetFocus()}; list={listView}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (selectedCount != count || host.CallbackSelectionVersion <= callbackSelectionVersionBefore)
                throw new Exception($"IShellView.TranslateAccelerator did not select the complete isolated view: {selectedCount}/{count}.");
            for (var index = 0; index < count; index++)
                Check(At<SelectItem>(folderView, 15)(folderView, index, 0)); // SVSI_DESELECT
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 0) throw new Exception("The Ctrl+A accelerator test did not restore the empty fixture selection.");
            Console.WriteLine($"ishellview-host-filter: translate-accelerator-ctrl-a=True; selected={count}; selection-restored-empty=True; thread-keyboard-state-restored=True");

            // Production calls TranslateAccelerator only after confirming focus is inside
            // _viewWindow; a no-focus WM_COMMAND fallback probe is therefore unreachable.
            _ = SetFocus(listView);
            if (GetFocus() != listView) throw new Exception("The isolated list did not regain focus after the fallback test.");

            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x10u | 0x40000000u)); // SVSI_FOCUSED | SVSI_NOTAKEFOCUS.
            Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusBeforeArrow));
            if (focusBeforeArrow != 0) throw new Exception($"Arrow-key fixture expected first row focus, got {focusBeforeArrow}.");
            var down = new NativeMessage { Window = listView, Id = 0x0100, WParam = 0x28, LParam = 0x00500001 };
            var downHr = At<TranslateAccelerator>(view, 5)(view, ref down);
            Check(downHr);
            Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusAfterArrow));
            if (focusAfterArrow != 1)
                throw new Exception($"IShellView.TranslateAccelerator did not advance the focused item with Down: {focusBeforeArrow}->{focusAfterArrow}, hr=0x{downHr:X8}.");
            for (var index = 0; index < count; index++)
                Check(At<SelectItem>(folderView, 15)(folderView, index, 0)); // Leave the isolated fixture with no selected rows.
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 0) throw new Exception("The arrow accelerator test did not restore the empty fixture selection.");
            Console.WriteLine($"ishellview-host-filter: translate-accelerator-down=True; focus={focusBeforeArrow}->{focusAfterArrow}; selection-restored-empty=True");

            Check(At<SelectItem>(folderView, 15)(folderView, childIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));
            var enter = new NativeMessage { Window = listView, Id = 0x0100, WParam = 0x0D, LParam = 0x001C0001 };
            var keyResult = At<TranslateAccelerator>(view, 5)(view, ref enter); // Same IShellView accelerator path used by ExplorerBrowserHost.
            Application.DoEvents();
            Console.WriteLine($"ishellview-host-filter: translate-accelerator-enter-result={keyResult}; default-command-calls={host.DefaultCommandCalls}; browseobject-calls={host.BrowseObjectCalls}; browse-target-is-child={host.BrowseTargetIsChild}");
            if (keyResult != 0 || host.DefaultCommandCalls != 1 || host.BrowseObjectCalls != 2 || !host.BrowseTargetIsChild)
                throw new Exception("IShellView.TranslateAccelerator did not route Enter on the focused fixture folder through OnDefaultCommand to BrowseObject.");

            if (navigationError is not null) throw new Exception("The isolated host could not create the child Shell view.", navigationError);
            if (host.PendingView == 0) throw new Exception("BrowseObject did not create a child Shell view.");
            if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            created = false;
            if (view != 0) { Marshal.Release(view); view = 0; }
            if (folder != 0) { Marshal.Release(folder); folder = 0; }
            (folder, view, viewWindow) = host.TakePendingView();
            created = true;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
                if (count == 1 && host.IncludeObjectCalls >= 4) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: child-view-items={count}; child-filter-callbacks={host.IncludeObjectCalls - 3}; child-view-created=True; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (count != 1 || host.IncludeObjectCalls < 4 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("The child IShellView did not enumerate its filtered fixture item or changed the hidden desktop boundary.");

            var childView = view;
            var invalidTargetHr = host.BrowseObject(filePidl, 0);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            if (invalidTargetHr >= 0 || host.View != childView || host.PendingView != 0 || count != 1 || host.HistoryIndex != 1 || !ILIsEqual(host.CurrentPidl, childFolderPidl))
                throw new Exception("Rejecting a non-folder navigation target changed the active child view.");
            Console.WriteLine($"ishellview-host-filter: nonfolder-target-rejected=True; active-child-view-preserved=True; history-index={host.HistoryIndex}; items={count}");

            Check(host.BrowseObject(0, SbspParent));
            if (host.PendingView == 0 || !host.BrowseTargetIsRoot)
                throw new Exception("The isolated child view could not request navigation back to its parent.");
            Marshal.Release(folderView);
            folderView = 0;
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            created = false;
            Marshal.Release(view);
            view = 0;
            Marshal.Release(folder);
            folder = 0;
            (folder, view, viewWindow) = host.TakePendingView();
            created = true;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
                if (count == 2 && host.IncludeObjectCalls >= 7) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: parent-return-items={count}; parent-include-calls={host.IncludeObjectCalls - 4}; parent-view-restored=True; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (count != 2 || host.IncludeObjectCalls < 7 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Returning to the parent did not restore its filtered Shell view.");
            Console.WriteLine("ishellview-host-filter: sbsp-parent=True; history-index=2");

            var rootView = view;
            var endForwardHr = host.BrowseObject(0, SbspNavigateForward);
            if (endForwardHr >= 0 || host.View != rootView || host.PendingView != 0 || host.HistoryIndex != 2 || !ILIsEqual(host.CurrentPidl, folderPidl))
                throw new Exception("Forward at the end of the history changed the active view or history position.");
            Console.WriteLine("ishellview-host-filter: forward-boundary-rejected=True; history-index=2; active-view-preserved=True");

            Check(host.BrowseObject(0, SbspNavigateBack));
            AdoptPendingView(expectedItemCount: 1);
            if (!host.BrowseTargetIsChild || !ILIsEqual(host.CurrentPidl, childFolderPidl))
                throw new Exception("SBSP_NAVIGATEBACK did not return to the child folder.");
            Check(host.BrowseObject(0, SbspNavigateForward));
            AdoptPendingView(expectedItemCount: 2);
            if (!host.BrowseTargetIsRoot || !ILIsEqual(host.CurrentPidl, folderPidl))
                throw new Exception("SBSP_NAVIGATEFORWARD did not return to the root folder.");
            Console.WriteLine($"ishellview-host-filter: sbsp-back-forward=True; index={host.HistoryIndex}; items={GetItemCount()}; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (host.HistoryIndex != 2 || GetItemCount() != 2 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Back/forward navigation did not restore the expected isolated Shell views.");

            var grandchildPath = Path.Combine(fixture, "child", "grandchild");
            Directory.CreateDirectory(grandchildPath);
            File.WriteAllText(Path.Combine(grandchildPath, "nested.txt"), "nested");
            var alternatePath = Path.Combine(fixture, "child", "alternate");
            Directory.CreateDirectory(alternatePath);
            File.WriteAllText(Path.Combine(alternatePath, "alternate.txt"), "alternate");
            Check(SHParseDisplayName(grandchildPath, 0, out grandchildFolderPidl, 0, out _));
            Check(SHParseDisplayName(alternatePath, 0, out alternateFolderPidl, 0, out _));
            Check(host.BrowseObject(childFolderPidl, 0));
            AdoptPendingView(expectedItemCount: 3);
            var nestedNavigateHr = host.BrowseObject(grandchildFolderPidl, 0);
            if (nestedNavigateHr < 0 || host.PendingView == 0 || !ILIsEqual(host.CurrentPidl, grandchildFolderPidl))
                throw new Exception($"The isolated host rejected a valid nested folder target: hr=0x{nestedNavigateHr:X8}.");
            AdoptPendingView(expectedItemCount: 1);
            Check(host.BrowseObject(0, SbspParent));
            if (!ILIsEqual(host.CurrentPidl, childFolderPidl))
                throw new Exception("SBSP_PARENT did not resolve an arbitrary nested folder to its actual parent.");
            AdoptPendingView(expectedItemCount: 3);
            Check(host.BrowseObject(0, SbspNavigateBack));
            AdoptPendingView(expectedItemCount: 1);
            if (!ILIsEqual(host.CurrentPidl, grandchildFolderPidl))
                throw new Exception("Back after nested parent navigation did not restore the nested folder.");
            Check(host.BrowseObject(0, SbspNavigateForward));
            AdoptPendingView(expectedItemCount: 3);
            if (!ILIsEqual(host.CurrentPidl, childFolderPidl) || host.HistoryIndex != 5)
                throw new Exception("Forward after nested parent navigation did not restore the child folder.");
            Check(host.BrowseObject(0, SbspNavigateBack));
            AdoptPendingView(expectedItemCount: 1);
            if (!ILIsEqual(host.CurrentPidl, grandchildFolderPidl) || host.HistoryIndex != 4)
                throw new Exception("Back before branching did not restore the grandchild history entry.");
            Check(host.BrowseObject(alternateFolderPidl, 0));
            AdoptPendingView(expectedItemCount: 1);
            if (!ILIsEqual(host.CurrentPidl, alternateFolderPidl) || host.HistoryIndex != 5)
                throw new Exception("A new navigation did not replace the old forward history branch.");
            var abandonedForwardHr = host.BrowseObject(0, SbspNavigateForward);
            if (abandonedForwardHr >= 0 || host.PendingView != 0 || host.HistoryIndex != 5 || !ILIsEqual(host.CurrentPidl, alternateFolderPidl))
                throw new Exception("Forward navigation followed an abandoned history branch.");
            Console.WriteLine($"ishellview-host-filter: arbitrary-depth-navigation=True; parent=True; back-forward=True; history-branch-truncated=True; history-index={host.HistoryIndex}; foreground-unchanged={GetForegroundWindow() == foreground}");

            foreach (var (parsingName, label) in new[]
            {
                ("shell:Desktop", "Desktop"),
                ("shell:MyComputerFolder", "ThisPC"),
                ("shell:RecycleBinFolder", "RecycleBin"),
                ("shell:Libraries", "Libraries"),
                ("shell:NetworkPlacesFolder", "NetworkPlaces"),
                ("::{26EE0668-A00A-44D7-9371-BEB064C98683}", "ControlPanel")
            })
                BrowseVirtualScope(parsingName, label);

            BrowseVirtualScope(ShellSearchLocation.BuildUri(fixture, "nested"), "SearchFolder");

            BrowseVirtualScope(CreateZipFixture(), "ZipContainer");
            return 0;

            int GetItemCount()
            {
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out var result));
                return result;
            }

            void BrowseVirtualScope(string parsingName, string label)
            {
                TraceVirtualScope($"{label}-parse-before", foreground);
                var previousLocationPidl = ILClone(host.CurrentPidl);
                try
                {
                    if (label == "SearchFolder")
                        virtualFolderPidl = ShellSearchLocation.ParsePidl(parsingName);
                    else
                        Check(SHParseDisplayName(parsingName, 0, out virtualFolderPidl, 0, out _));
                    TraceVirtualScope($"{label}-browse-before", foreground);
                    Check(host.BrowseObject(virtualFolderPidl, 0));
                    TraceVirtualScope($"{label}-browse-after", foreground);
                    var itemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: label == "ControlPanel");
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"The isolated host did not retain the {label} PIDL.");
                    var listView = FindChildByClass(viewWindow, "SysListView32");
                    var listViewItems = listView == 0 ? -1 : SendMessageW(listView, 0x1004, 0, 0).ToInt32();
                    Console.WriteLine($"ishellview-host-filter: virtual-shell-view-created=True; target={label}; folder-view-items={itemCount}; folder-view-items-hr=0x{itemCountLastHResult:X8}; list-view-items={listViewItems}; {InspectNativeContracts(readVerbs: label is not "ZipContainer" and not "ControlPanel")}");
                    Check(host.BrowseObject(0, SbspNavigateBack));
                    var backItemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                    var backItemCountHResult = itemCountLastHResult;
                    if (!ILIsEqual(host.CurrentPidl, previousLocationPidl))
                        throw new Exception($"Back from {label} did not restore the previous absolute location.");
                    Check(host.BrowseObject(0, SbspNavigateForward));
                    var forwardItemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                    var forwardItemCountHResult = itemCountLastHResult;
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Forward to {label} did not restore its absolute PIDL identity.");
                    Console.WriteLine($"ishellview-host-filter: virtual-history-roundtrip=True; target={label}; back-items={backItemCount}; back-hr=0x{backItemCountHResult:X8}; forward-items={forwardItemCount}; forward-hr=0x{forwardItemCountHResult:X8}; view-counts-readable={backItemCount >= 0 && forwardItemCount >= 0}; pidl-identity-preserved=True");

                    // Match the filesystem failure gate: a rejected file target must not replace a virtual view or advance its travel history.
                    var activeVirtualView = host.View;
                    var historyIndexBeforeInvalidTarget = host.HistoryIndex;
                    var invalidTargetHr = host.BrowseObject(filePidl, 0);
                    if (invalidTargetHr >= 0 || host.PendingView != 0 || host.View != activeVirtualView ||
                        host.HistoryIndex != historyIndexBeforeInvalidTarget || !ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Rejecting a file target after {label} navigation changed the active virtual view or history.");
                    Console.WriteLine($"ishellview-host-filter: virtual-nonfolder-target-rejected=True; target={label}; hr=0x{invalidTargetHr:X8}; active-view-preserved=True; history-index={host.HistoryIndex}; pidl-identity-preserved=True");

                    var retainedViewBeforeCreateWindowFailure = host.View;
                    var retainedWindowBeforeCreateWindowFailure = viewWindow;
                    var historyCountBeforeCreateWindowFailure = host.NavigationHistory!.Count;
                    failAfterCreateViewObject = true;
                    var createWindowFailureHr = host.BrowseObject(virtualFolderPidl, 0);
                    if (createWindowFailureHr >= 0 || failAfterCreateViewObject ||
                        !IsWindow(retainedWindowBeforeCreateWindowFailure) || host.PendingView != 0 ||
                        host.View != retainedViewBeforeCreateWindowFailure ||
                        host.HistoryIndex != historyIndexBeforeInvalidTarget ||
                        host.NavigationHistory.Count != historyCountBeforeCreateWindowFailure ||
                        !ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Failure after CreateViewObject for {label} did not preserve the old view and history.");
                    Console.WriteLine($"ishellview-host-filter: post-create-view-object-failure-rollback=True; target={label}; hr=0x{createWindowFailureHr:X8}; previous-view-valid=True; no-pending-view=True; history-unchanged=True");

                    var retainedViewBeforeInjectedFailure = host.View;
                    var retainedWindowBeforeInjectedFailure = viewWindow;
                    var historyCountBeforeInjectedFailure = host.NavigationHistory!.Count;
                    failAfterCreateViewWindow = true;
                    var injectedFailureHr = host.BrowseObject(virtualFolderPidl, 0);
                    if (injectedFailureHr >= 0 || failAfterCreateViewWindow || failedNavigationWindow == 0 ||
                        IsWindow(failedNavigationWindow) || !IsWindow(retainedWindowBeforeInjectedFailure) ||
                        host.PendingView != 0 || host.View != retainedViewBeforeInjectedFailure ||
                        host.HistoryIndex != historyIndexBeforeInvalidTarget ||
                        host.NavigationHistory.Count != historyCountBeforeInjectedFailure ||
                        !ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Injected CreateViewWindow handoff failure for {label} did not destroy its new window and preserve the old view/history.");
                    Console.WriteLine($"ishellview-host-filter: view-window-handoff-failure-rollback=True; target={label}; hr=0x{injectedFailureHr:X8}; failed-window-destroyed=True; previous-view-valid=True; history-unchanged=True");
                    failedNavigationWindow = 0;

                    var historyCountBeforeSamePath = host.NavigationHistory!.Count;
                    Check(host.BrowseObject(virtualFolderPidl, 0));
                    var samePathItemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl) || host.HistoryIndex != historyIndexBeforeInvalidTarget ||
                        host.NavigationHistory.Count != historyCountBeforeSamePath)
                        throw new Exception($"Same-path navigation to {label} changed its PIDL or duplicated the travel-history entry.");
                    Console.WriteLine($"ishellview-host-filter: virtual-same-path-navigation=True; target={label}; view-created=True; view-items={samePathItemCount}; view-items-hr=0x{itemCountLastHResult:X8}; history-index={host.HistoryIndex}; history-count={host.NavigationHistory.Count}; history-unchanged=True");

                    if (label == "ThisPC")
                    {
                        var thisPcPidl = ILClone(host.CurrentPidl);
                        try
                        {
                            var historyCountBeforeRecreateLoop = host.NavigationHistory.Count;
                            var historyIndexBeforeRecreateLoop = host.HistoryIndex;
                            var retiredWindowsDestroyed = true;
                            for (var cycle = 0; cycle < 8; cycle++)
                            {
                                var retiredWindow = viewWindow;
                                Check(host.BrowseObject(thisPcPidl, 0));
                                AdoptPendingView(expectedItemCount: null,
                                    allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                                if (IsWindow(retiredWindow) || !IsWindow(viewWindow) ||
                                    host.HistoryIndex != historyIndexBeforeRecreateLoop ||
                                    host.NavigationHistory.Count != historyCountBeforeRecreateLoop ||
                                    !ILIsEqual(host.CurrentPidl, thisPcPidl))
                                    retiredWindowsDestroyed = false;
                                if (!retiredWindowsDestroyed)
                                    throw new Exception($"Repeated This PC view replacement failed at cycle {cycle + 1}: retired-window-destroyed={!IsWindow(retiredWindow)}; active-window-valid={IsWindow(viewWindow)}; history-index={host.HistoryIndex}; history-count={host.NavigationHistory.Count}.");
                            }
                            Console.WriteLine($"ishellview-host-filter: same-path-view-retirement=True; target=ThisPC; cycles=8; retired-windows-destroyed={retiredWindowsDestroyed}; active-window-valid={IsWindow(viewWindow)}; history-unchanged=True");

                            Check(host.BrowseObject(volumeRootPidl, 0));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, volumeRootPidl))
                                throw new Exception("The This PC view could not navigate into the system volume root.");

                            Check(host.BrowseObject(0, SbspParent));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, thisPcPidl))
                                throw new Exception("SBSP_PARENT from the system volume root did not return to This PC.");

                            Check(host.BrowseObject(0, SbspNavigateBack));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, volumeRootPidl))
                                throw new Exception("Back from This PC did not restore the system volume root.");

                            Check(host.BrowseObject(0, SbspNavigateForward));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, thisPcPidl))
                                throw new Exception("Forward from the system volume root did not restore This PC.");
                            Console.WriteLine($"ishellview-host-filter: virtual-filesystem-parent-history=True; target=ThisPC; drive-root-parent=True; back-forward=True; history-index={host.HistoryIndex}; pidl-identity-preserved=True");
                        }
                        finally { Marshal.FreeCoTaskMem(thisPcPidl); }
                    }
                    TraceVirtualScope($"{label}-complete", foreground);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(virtualFolderPidl);
                    Marshal.FreeCoTaskMem(previousLocationPidl);
                    virtualFolderPidl = 0;
                }
            }

            string CreateZipFixture()
            {
                zipPath = Path.Combine(fixtureRoot, $"probe-{Guid.NewGuid():N}.zip");
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                using var entry = archive.CreateEntry("archive.txt").Open();
                entry.WriteByte(0x41);
                TraceVirtualScope("zip-fixture-created", foreground);
                return zipPath;
            }

            int AdoptPendingView(int? expectedItemCount, bool allowItemCountFailure = false,
                bool returnOnFirstSuccessfulCount = false)
            {
                if (host.PendingView == 0) throw new Exception("Shell navigation did not create a pending view.");
                if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
                if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
                created = false;
                if (view != 0) { Marshal.Release(view); view = 0; }
                if (folder != 0) { Marshal.Release(folder); folder = 0; }
                (folder, view, viewWindow) = host.TakePendingView();
                created = true;
                Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
                var waitUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                var itemCount = 0;
                do
                {
                    Application.DoEvents();
                    var itemCountHr = At<ItemCount>(folderView, 7)(folderView, 2, out itemCount);
                    itemCountLastHResult = itemCountHr;
                    if (itemCountHr < 0)
                    {
                        if (allowItemCountFailure) return -1;
                        Check(itemCountHr);
                    }
                    // Search Folder counts are index-driven; navigation history needs a readable view, not a settled result count.
                    if (returnOnFirstSuccessfulCount) return itemCount;
                    if (itemCount == expectedItemCount) return itemCount;
                    Thread.Sleep(10);
                } while (DateTime.UtcNow < waitUntil);
                if (expectedItemCount is null) return itemCount;
                throw new Exception($"Navigated Shell view did not enumerate expected items: expected={expectedItemCount}, actual={itemCount}.");
            }

            string InspectNativeContracts(bool readVerbs)
            {
                var menuId = ContextMenuId;
                var menuHr = At<GetItemObject>(view, 15)(view, 0, ref menuId, out var contextMenu);
                var dropHr = Marshal.QueryInterface(view, in DropTargetId, out var dropTarget);
                var menuQueryHr = unchecked((int)0x80004005);
                var menuItemCount = -1;
                var verbs = new List<string>();
                var menuCaptions = new List<string>();
                var coreStates = new List<string>();
                nint menu = 0;
                nint commandBuffer = 0;
                try
                {
                    if (menuHr >= 0 && contextMenu != 0)
                    {
                        menu = CreatePopupMenu();
                        if (menu != 0)
                        {
                            menuQueryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                            if (menuQueryHr >= 0)
                            {
                                menuItemCount = GetMenuItemCount(menu);
                                for (var index = 0; index < menuItemCount; index++)
                                {
                                    var caption = new System.Text.StringBuilder(256);
                                    if (GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400) > 0)
                                        menuCaptions.Add(caption.ToString());
                                }
                            }
                            if (readVerbs && menuQueryHr >= 0)
                            {
                                commandBuffer = Marshal.AllocHGlobal(512);
                                var commandCount = Math.Min(menuQueryHr & 0xffff, 0x7fff);
                                var getCommandString = At<GetCommandString>(contextMenu, 5);
                                for (uint offset = 0; offset < commandCount; offset++)
                                {
                                    Marshal.WriteInt16(commandBuffer, 0);
                                    if (getCommandString(contextMenu, offset, 4, 0, commandBuffer, 256) == 0)
                                    {
                                        var verb = Marshal.PtrToStringUni(commandBuffer);
                                        if (!string.IsNullOrWhiteSpace(verb))
                                        {
                                            verbs.Add(verb);
                                            if (verb.Equals("view", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("groupby", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("refresh", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("properties", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("empty", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("addnetworkplace", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("savesearch", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("newfolder", StringComparison.OrdinalIgnoreCase))
                                            {
                                                var state = GetMenuState(menu, offset + 1, 0);
                                                var stateName = state == uint.MaxValue ? "unknown" : (state & 3) == 0 ? "enabled" : "disabled";
                                                coreStates.Add($"{verb}:{stateName}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (commandBuffer != 0) Marshal.FreeHGlobal(commandBuffer);
                    if (menu != 0) DestroyMenu(menu);
                    if (contextMenu != 0) Marshal.Release(contextMenu);
                    if (dropTarget != 0) Marshal.Release(dropTarget);
                }
                var verbReport = readVerbs ? $"verbs=[{string.Join(',', verbs)}]" : "verbs=skipped-provider-compatibility";
                return $"background-menu=0x{menuHr:X8}/query=0x{menuQueryHr:X8}/items={menuItemCount}/captions=[{string.Join('|', menuCaptions)}]/{verbReport}/core-state=[{string.Join(',', coreStates)}];drop-target=0x{dropHr:X8}";
            }

        }
        finally
        {
            TraceCleanup("pending-view-before", foreground);
            host?.DisposePendingView();
            TraceCleanup("pending-view-after", foreground);
            if (created && view != 0)
            {
                TraceCleanup("destroy-view-before", foreground);
                _ = At<DestroyViewWindow>(view, 10)(view);
                TraceCleanup("destroy-view-after", foreground);
            }
            TraceCleanup("release-interfaces-before", foreground);
            if (folderView != 0) Marshal.Release(folderView);
            if (view != 0) Marshal.Release(view);
            if (viewPointer != 0) Marshal.Release(viewPointer);
            host?.Dispose();
            if (folder != 0) Marshal.Release(folder);
            if (grandchildFolderPidl != 0) Marshal.FreeCoTaskMem(grandchildFolderPidl);
            if (alternateFolderPidl != 0) Marshal.FreeCoTaskMem(alternateFolderPidl);
            if (virtualFolderPidl != 0) Marshal.FreeCoTaskMem(virtualFolderPidl);
            if (volumeRootPidl != 0) Marshal.FreeCoTaskMem(volumeRootPidl);
            if (childFolderPidl != 0) Marshal.FreeCoTaskMem(childFolderPidl);
            if (filePidl != 0) Marshal.FreeCoTaskMem(filePidl);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            TraceCleanup("release-interfaces-after", foreground);
            if (!leaveParentForThreadExit && parent != 0 && IsWindow(parent))
            {
                TraceCleanup("destroy-parent-before", foreground);
                DestroyWindow(parent);
                TraceCleanup("destroy-parent-after", foreground);
            }
            else if (leaveParentForThreadExit && parent != 0 && IsWindow(parent))
            {
                // DestroyWindow on the owning window of a non-input Desktop stalls even after
                // the Shell view and child controls are gone; thread exit owns final HWND cleanup.
                TraceCleanup("parent-cleanup-deferred-to-thread-exit", foreground);
            }
            TraceCleanup("fixture-delete-before", foreground);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            if (zipPath is not null && File.Exists(zipPath)) File.Delete(zipPath);
            TraceCleanup("fixture-delete-after", foreground);
            TraceCleanup("keepalive-before", foreground);
            GC.KeepAlive(host);
            TraceCleanup("keepalive-after", foreground);
        }
    }

    private static void TraceCleanup(string phase, nint expectedForeground)
    {
        var foreground = GetForegroundWindow();
        Console.WriteLine($"ishellview-host-filter: cleanup={phase}; foreground=0x{foreground:X}; expected=0x{expectedForeground:X}; foreground-unchanged={foreground == expectedForeground}");
        Console.Out.Flush();
    }

    private static void TraceVirtualScope(string phase, nint expectedForeground)
    {
        var foreground = GetForegroundWindow();
        Console.WriteLine($"ishellview-host-filter: virtual-phase={phase}; foreground=0x{foreground:X}; expected=0x{expectedForeground:X}; foreground-unchanged={foreground == expectedForeground}");
        Console.Out.Flush();
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static nint ClonePidl(nint pidl)
    {
        var size = checked((int)ILGetSize(pidl));
        var clone = Marshal.AllocCoTaskMem(size);
        var bytes = new byte[size];
        Marshal.Copy(pidl, bytes, 0, size);
        Marshal.Copy(bytes, 0, clone, size);
        return clone;
    }

    private static int FindFolderIndex(nint folderView, nint shellFolder, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var hr = At<GetItem>(folderView, 6)(folderView, index, out var itemPidl);
            if (hr < 0 || itemPidl == 0) continue;
            try
            {
                var itemArray = Marshal.AllocHGlobal(nint.Size);
                var attributes = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    Marshal.WriteIntPtr(itemArray, itemPidl);
                    Marshal.WriteInt32(attributes, unchecked((int)0x20000000)); // SFGAO_FOLDER
                    if (At<GetAttributesOf>(shellFolder, 9)(shellFolder, 1, itemArray, attributes) >= 0 &&
                        (unchecked((uint)Marshal.ReadInt32(attributes)) & 0x20000000) != 0)
                        return index;
                }
                finally
                {
                    Marshal.FreeHGlobal(attributes);
                    Marshal.FreeHGlobal(itemArray);
                }
            }
            finally { Marshal.FreeCoTaskMem(itemPidl); }
        }
        return -1;
    }

    private static void VerifyGroupedFilteredExport(nint folderView)
    {
        var view2Id = FolderView2Id;
        Check(Marshal.QueryInterface(folderView, in view2Id, out var view2));
        var typeKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 4 };
        try
        {
            Check(At<SetGroupBy>(view2, 17)(view2, ref typeKey, 1));
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
            if (count != 2) throw new Exception($"Grouped filtered fixture expected 2 visible rows, got {count}.");

            var expectedOrder = ReadVisibleItemNames(folderView, count);
            var allViewSnapshot = ShellFileViewExportCapture.Capture(folderView, quickFilterActive: true);
            var allViewNames = allViewSnapshot.Rows.Select(row => row[0]).ToArray();
            if (!allViewNames.SequenceEqual(expectedOrder, StringComparer.OrdinalIgnoreCase) ||
                !allViewNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["child", "keep.txt"]))
                throw new Exception($"Grouped filtered SVGIO_ALLVIEW export mismatch: shell=[{string.Join('|', expectedOrder)}], export=[{string.Join('|', allViewNames)}].");

            var indexedSnapshot = ShellFileViewExportCapture.CaptureIndexedView(folderView);
            var indexedNames = indexedSnapshot.Rows.Select(row => row[0]).ToArray();
            if (!indexedNames.SequenceEqual(expectedOrder, StringComparer.OrdinalIgnoreCase) ||
                !indexedNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["child", "keep.txt"]))
                throw new Exception($"Grouped filtered indexed-view export mismatch: shell=[{string.Join('|', expectedOrder)}], export=[{string.Join('|', indexedNames)}].");

            Console.WriteLine($"ishellview-host-filter: grouped-filtered-export=True; svgio={string.Join('|', allViewNames)}; indexed={string.Join('|', indexedNames)}");
        }
        finally
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            _ = At<SetGroupBy>(view2, 17)(view2, ref noGroup, 0);
            Marshal.Release(view2);
        }
    }

    private static string[] ReadVisibleItemNames(nint folderView, int count)
    {
        var folderId = ShellFolderId;
        Check(At<GetFolder>(folderView, 5)(folderView, ref folderId, out var folder));
        try
        {
            var names = new string[count];
            for (var index = 0; index < count; index++)
            {
                Check(At<GetItem>(folderView, 6)(folderView, index, out var childPidl));
                try
                {
                    var itemId = ShellItemId;
                    Check(SHCreateItemWithParent(0, folder, childPidl, ref itemId, out var item));
                    try
                    {
                        Check(At<GetItemDisplayName>(item, 5)(item, 0, out var name));
                        try { names[index] = Marshal.PtrToStringUni(name) ?? string.Empty; }
                        finally { Marshal.FreeCoTaskMem(name); }
                    }
                    finally { Marshal.Release(item); }
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            return names;
        }
        finally { Marshal.Release(folder); }
    }

    private static void VerifySynchronizedViewModes(nint folderView, nint listView, FilteredHost host, nint viewWindow, nint parent)
    {
        const int LvmSetView = 0x108E;
        const int LvmGetView = 0x108F;
        var setMode = At<SetViewModeAndIconSize>(folderView, 35);
        var getMode = At<GetViewModeAndIconSize>(folderView, 36);
        var viewPairs = new[]
        {
            (Name: "Icon", ListView: 0, Shell: 1, IconSize: 48), (Name: "Details", ListView: 1, Shell: 4, IconSize: 16),
            (Name: "SmallIcon", ListView: 2, Shell: 2, IconSize: 16), (Name: "List", ListView: 3, Shell: 3, IconSize: 16),
            (Name: "Tile", ListView: 4, Shell: 6, IconSize: 48)
        };
        var results = new List<string>();
        _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE; the parent remains on the Private Desktop.
        Check(At<ActivateView>(host.View, 7)(host.View, 1)); // SVUIA_ACTIVATE_FOCUS.
        foreach (var mode in viewPairs)
        {
            var setHr = setMode(folderView, (uint)mode.Shell, mode.IconSize);
            Check(setHr);
            Check(getMode(folderView, out var shellModeAfterSet, out var sizeAfterSet));
            var listViewSetResult = SendMessageW(listView, LvmSetView, (nint)mode.ListView, 0).ToInt32();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int visibleItems;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out visibleItems));
                if (visibleItems == 2) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(getMode(folderView, out var actualShellMode, out var iconSize));
            var actualListViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            var visibleNames = ReadVisibleItemNames(folderView, visibleItems);
            if (actualShellMode != mode.Shell || iconSize != mode.IconSize || actualListViewMode != mode.ListView || visibleItems != 2 ||
                !visibleNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).SequenceEqual(["child", "keep.txt"], StringComparer.OrdinalIgnoreCase))
                throw new Exception($"Synchronized {mode.Name} mode changed the native list layout or filtered contents: set=0x{setHr:X8},shell-immediate={shellModeAfterSet}/{sizeAfterSet},lvm-set={listViewSetResult},shell-final={actualShellMode}/{iconSize},list={actualListViewMode},items={visibleItems},names={string.Join(',', visibleNames)}.");
            var renameArrowResults = VerifyRenameArrowRouting(folderView, listView, viewWindow, host.View, visibleNames, mode.Name);
            results.Add($"{mode.Name}=shell:{actualShellMode}/{iconSize},list:{actualListViewMode},items:{visibleItems},rename-arrows:[{string.Join(',', renameArrowResults)}]");
        }
        VerifyShellNativeViewModes(folderView, listView);
        // Keep the following linear Down-key probe in Details, where its expected next-row behavior is defined.
        Check(setMode(folderView, 4, 16));
        _ = SendMessageW(listView, LvmSetView, 1, 0);
        Application.DoEvents();
        _ = ShowWindow(parent, 0); // SW_HIDE; restore the probe's original hidden-parent boundary.
        Console.WriteLine("ishellview-host-filter: synchronized-view-modes=" + string.Join('|', results));
    }

    private static void VerifyShellNativeViewModes(nint folderView, nint listView)
    {
        const int LvmGetView = 0x108F;
        var setMode = At<SetViewModeAndIconSize>(folderView, 35);
        var getMode = At<GetViewModeAndIconSize>(folderView, 36);
        var results = new List<string>();
        var initialNames = ReadVisibleItemNames(folderView, 2);
        var keepIndex = Array.FindIndex(initialNames, name => name.Equals("keep.txt", StringComparison.OrdinalIgnoreCase));
        if (keepIndex < 0) throw new Exception("The Shell view mode probe has no persistent selection item.");
        Check(At<SelectItem>(folderView, 15)(folderView, keepIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));

        foreach (var style in ViewStyle.All)
        {
            var requestedSize = ViewStyle.NativeIconSize(style.Mode, style.IconSize);
            Check(setMode(folderView, style.Mode, requestedSize));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int visibleItems;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out visibleItems));
                if (visibleItems == 2) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Check(getMode(folderView, out var actualMode, out var actualSize));
            var listViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            var names = ReadVisibleItemNames(folderView, visibleItems);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedItems));
            Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex));
            var focusedName = focusedIndex >= 0 && focusedIndex < names.Length ? names[focusedIndex] : string.Empty;
            var expectedListViewMode = style.Mode switch
            {
                1 => 0, // FVM_ICON -> LV_VIEW_ICON.
                2 => 2, // FVM_SMALLICON -> LV_VIEW_SMALLICON.
                3 => 3, // FVM_LIST -> LV_VIEW_LIST.
                4 => 1, // FVM_DETAILS -> LV_VIEW_DETAILS.
                6 => 4, // FVM_TILE -> LV_VIEW_TILE.
                _ => -1 // Content is not one of the five documented SysListView32 modes.
            };
            var expectedSizeMatches = requestedSize <= 0 || actualSize == requestedSize;
            var listViewModeMatches = expectedListViewMode < 0 || listViewMode == expectedListViewMode;
            var selectionPreserved = selectedItems == 1 && focusedName.Equals("keep.txt", StringComparison.OrdinalIgnoreCase);
            var itemsMatch = visibleItems == 2 && names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(["child", "keep.txt"], StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"ishellview-native-view-mode: name={style.Name}; requested={style.Mode}/{requestedSize}; actual={actualMode}/{actualSize}; list-view={listViewMode}; items={visibleItems}; names={string.Join(',', names)}; selection={selectedItems}/{focusedName}; item-set-preserved={itemsMatch}; selection-preserved={selectionPreserved}");

            if (actualMode != style.Mode || !expectedSizeMatches || !listViewModeMatches || !itemsMatch || !selectionPreserved)
                throw new Exception($"IShellView did not apply {style.Name} through its native view API: requested={style.Mode}/{requestedSize}, actual={actualMode}/{actualSize}, list={listViewMode}, items={visibleItems}, names={string.Join(',', names)}, selection={selectedItems}/{focusedName}.");
            results.Add($"{style.Name}={actualMode}/{actualSize}/list:{listViewMode}/items:{visibleItems}");
        }

        for (var index = 0; index < initialNames.Length; index++)
            Check(At<SelectItem>(folderView, 15)(folderView, index, 0));
        Check(At<ItemCount>(folderView, 7)(folderView, 1, out var finalSelectionCount));
        if (finalSelectionCount != 0) throw new Exception("The native view mode probe did not restore its empty selection baseline.");

        Console.WriteLine("ishellview-host-filter: shell-api-view-modes=" + string.Join('|', results));
    }

    private static IReadOnlyList<string> VerifyRenameArrowRouting(nint folderView, nint listView, nint viewWindow, nint view,
        IReadOnlyList<string> visibleNames, string modeName)
    {
        const uint selectFocusedVisible = 0x1u | 0x4u | 0x10u | 0x40000000u;
        var itemIndex = Array.FindIndex(visibleNames.ToArray(), name => name.Equals("keep.txt", StringComparison.OrdinalIgnoreCase));
        if (itemIndex < 0) throw new Exception($"The {modeName} view has no rename fixture item.");

        Check(At<SelectItem>(folderView, 15)(folderView, itemIndex, selectFocusedVisible));
        _ = SetFocus(listView);
        var f2 = new NativeMessage { Window = listView, Id = 0x0100, WParam = 0x71, LParam = 0x003C0001 };
        var f2Hr = At<TranslateAccelerator>(view, 5)(view, ref f2);
        var renameEditor = FindChildByClass(viewWindow, "Edit");
        var renameDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < renameDeadline && (renameEditor == 0 || GetFocus() != renameEditor))
        {
            Application.DoEvents();
            renameEditor = FindChildByClass(viewWindow, "Edit");
            if (renameEditor == 0 || GetFocus() != renameEditor) Thread.Sleep(10);
        }
        if (renameEditor == 0 || GetFocus() != renameEditor)
        {
            var focusedWindow = GetFocus();
            var focusedClass = new System.Text.StringBuilder(128);
            if (focusedWindow != 0) _ = GetClassNameW(focusedWindow, focusedClass, focusedClass.Capacity);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedCount));
            throw new Exception($"The {modeName} view did not focus its inline rename editor after F2 (hr=0x{f2Hr:X8},editor=0x{renameEditor:X},focus=0x{focusedWindow:X}/{focusedClass},selected={selectedCount},list=0x{listView:X},view=0x{viewWindow:X}).");
        }

        var arrows = new[]
        {
            (Name: "left", Key: 0x25u, LParam: 0x004B0001u),
            (Name: "up", Key: 0x26u, LParam: 0x00480001u),
            (Name: "right", Key: 0x27u, LParam: 0x004D0001u),
            (Name: "down", Key: 0x28u, LParam: 0x00500001u)
        };
        var results = new List<string>(arrows.Length);
        try
        {
            foreach (var arrow in arrows)
            {
                var message = new NativeMessage { Window = renameEditor, Id = 0x0100, WParam = arrow.Key, LParam = (nint)arrow.LParam };
                var hr = At<TranslateAccelerator>(view, 5)(view, ref message);
                Application.DoEvents();
                var retained = IsWindow(renameEditor) && GetFocus() == renameEditor;
                results.Add($"{arrow.Name}:{hr:X8}/{retained}");
                if (hr < 0 || !retained)
                    throw new Exception($"The {modeName} view lost its inline rename editor or focus on {arrow.Name} (hr=0x{hr:X8}).");
            }
        }
        finally
        {
            if (IsWindow(renameEditor))
            {
                var escape = new NativeMessage { Window = renameEditor, Id = 0x0100, WParam = 0x1B, LParam = 0x00010001 };
                _ = At<TranslateAccelerator>(view, 5)(view, ref escape);
                Application.DoEvents();
            }
            Check(At<SelectItem>(folderView, 15)(folderView, itemIndex, 0));
        }
        if (IsWindow(renameEditor)) throw new Exception($"The {modeName} view did not dismiss its cancelled inline rename editor.");
        return results;
    }

    private static void VerifyNativeMenuCommands(nint view, nint folderView, nint owner, nint foreground, nint folderPidl)
    {
        var getMode = At<GetViewModeAndIconSize>(folderView, 36);
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 1, 48));
        Check(getMode(folderView, out var initialMode, out _));
        if (initialMode == 4) throw new Exception("The menu-command fixture unexpectedly started in Details mode.");

        // The preceding grouped-export probe intentionally leaves Type grouping active; reset it so
        // sorting focus checks measure sort behavior rather than a leftover grouped-view state.
        var groupViewId = FolderView2Id;
        Check(Marshal.QueryInterface(folderView, in groupViewId, out var groupView));
        try
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            Check(At<SetGroupBy>(groupView, 17)(groupView, ref noGroup, 0));
        }
        finally { Marshal.Release(groupView); }

        var listView = FindChildByClass(owner, "SysListView32");
        if (listView == 0) throw new Exception("The menu-command fixture has no native file list window.");
        if (At<ItemCount>(folderView, 7)(folderView, 2, out var initialCount) < 0 || initialCount == 0)
            throw new Exception("The menu-command fixture has no visible item to focus.");
        var folderId = ShellFolderId;
        Check(SHBindToObject(0, folderPidl, 0, in folderId, out var shellFolder));
        var nextPeerRowId = 0;
        List<PeerShellRow> peerRows = [];
        var initialProjection = CapturePeerShellRows(folderView, folderPidl, shellFolder, [], ref nextPeerRowId);
        peerRows = initialProjection.Rows;
        var peerList = CreatePeerListView(owner, initialProjection.SystemImageList);
        PopulatePeerList(peerList, peerRows);
        var peerRowsBeforeRefresh = ReadPeerListNames(peerList);
        if (peerRowsBeforeRefresh.Length != initialCount)
            throw new Exception("The independent ListView projection did not receive the initial Shell rows.");
        var peerIdsBeforeRefresh = ReadPeerListRowIds(peerList);
        if (!peerIdsBeforeRefresh.SequenceEqual(peerRows.Select(row => row.Id)))
            throw new Exception("The peer ListView did not preserve stable row IDs as its item data.");
        Check(At<GetItem>(folderView, 6)(folderView, 0, out var expectedFocusedPidl));
        var expectedFocusedName = ReadVisibleItemNames(folderView, initialCount)[0];
        List<string> menuCommandFocusRecovery = [];
        List<string> nativeSortOrders = [];
        uint modeAfterMenu;
        int iconSizeAfterMenu, visibleCount;
        // Sort/group can briefly expose the old item count and PIDL before a second Shell rebuild; require settled focus state before measuring or restoring.
        void RestoreFocusedSelectionAfterMenuCommand(string command)
        {
            var focusSamples = new List<string>();
            (bool Hwnd, bool Item, int Selection, int Items) SampleFocus(string phase)
            {
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectionCount));
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out var itemCount));
                var sample = (Hwnd: GetFocus() == listView, Item: FocusedItemNameMatches(folderView, expectedFocusedName), Selection: selectionCount, Items: itemCount);
                focusSamples.Add($"{phase}:hwnd={sample.Hwnd},item={sample.Item},selection={sample.Selection},items={sample.Items}");
                return sample;
            }

            bool IsExpectedState((bool Hwnd, bool Item, int Selection, int Items) sample) =>
                sample.Hwnd && sample.Item && sample.Selection == 1 && sample.Items == initialCount;
            var settled = SampleFocus("0ms");
            var consecutiveStableSamples = IsExpectedState(settled) ? 1 : 0;
            var sampleNumber = 0;
            var stabilityDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (consecutiveStableSamples < 3 && DateTime.UtcNow < stabilityDeadline)
            {
                var sampleDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
                while (DateTime.UtcNow < sampleDeadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                settled = SampleFocus($"+{++sampleNumber * 50}ms");
                consecutiveStableSamples = IsExpectedState(settled) ? consecutiveStableSamples + 1 : 0;
            }
            var itemPreserved = settled.Item;
            var rawSelectedCount = settled.Selection;
            var hwndFocusPreserved = settled.Hwnd;
            var rawPreserved = consecutiveStableSamples == 3 && IsExpectedState(settled);
            if (!rawPreserved)
            {
                var restoredIndex = FindItemIndexByName(folderView, expectedFocusedName);
                if (restoredIndex < 0)
                    throw new Exception($"The focused fixture item '{expectedFocusedName}' was lost by Shell {command}.");
                Check(At<SelectItem>(folderView, 15)(folderView, restoredIndex,
                    1u | 0x10u | 0x40000000u)); // SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS
                if (GetFocus() != listView) _ = SetFocus(listView);
            }
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedCount));
            if (GetFocus() != listView || !FocusedItemNameMatches(folderView, expectedFocusedName) || selectedCount != 1)
                throw new Exception($"The host could not restore list/item focus after Shell {command}: list-focus={GetFocus() == listView}, selection={selectedCount}.");
            menuCommandFocusRecovery.Add($"{command}:raw={rawPreserved}(hwnd={hwndFocusPreserved},item={itemPreserved},selection={rawSelectedCount}),samples=[{string.Join(';', focusSamples)}],restored=True");
        }
        void VerifyFolderSortOrder(bool ascending, string command)
        {
            var orderDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            var stableSamples = 0;
            string[] names = [];
            while (DateTime.UtcNow < orderDeadline && stableSamples < 3)
            {
                Application.DoEvents();
                names = ReadVisibleItemNames(folderView, initialCount);
                var aardvarkIndex = Array.FindIndex(names, name => string.Equals(name, "aardvark", StringComparison.OrdinalIgnoreCase));
                var childIndex = Array.FindIndex(names, name => string.Equals(name, "child", StringComparison.OrdinalIgnoreCase));
                var orderMatches = aardvarkIndex >= 0 && childIndex >= 0 &&
                    (aardvarkIndex < childIndex) == ascending;
                stableSamples = orderMatches ? stableSamples + 1 : 0;
                if (stableSamples < 3) Thread.Sleep(50);
            }
            if (stableSamples < 3)
                throw new Exception($"The Shell {command} menu did not produce the expected folder order: [{string.Join(",", names)}].");
            nativeSortOrders.Add($"{command}:[{string.Join(",", names)}]");
        }
        try
        {
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 1u | 0x10u | 0x40000000u)); // SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS
            _ = SetFocus(listView);
            if (GetFocus() != listView || !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception("The menu-command fixture could not establish its initial list and item focus.");
            using var refreshSelection = ShellViewSelectionSnapshot.Capture(folderView);
            if (refreshSelection is null || refreshSelection.SelectedCount != 1 || !refreshSelection.HasFocusedItem)
                throw new Exception("The production selection snapshot did not capture the focused refresh fixture item.");

            InvokeBackgroundVerb(view, owner, "viewlogicaldetails");
            Check(getMode(folderView, out modeAfterMenu, out iconSizeAfterMenu));
            if (modeAfterMenu != 4 || iconSizeAfterMenu != 16)
                throw new Exception($"The Shell background menu did not switch the isolated view to Details: mode={modeAfterMenu}, size={iconSizeAfterMenu}.");
            SetPeerListMode(peerList, LvsReport);
            if ((GetWindowLongPtrW(peerList, GwlStyle).ToInt64() & LvsTypeMask) != LvsReport ||
                !ReadPeerListNames(peerList).SequenceEqual(peerRowsBeforeRefresh, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Projecting Shell Details mode changed or dropped the independent ListView rows.");
            VerifyColumnHeaderControl(view, owner);
            if (GetFocus() != listView || !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception("Switching view mode through the Shell menu lost the fixture list or focused item.");

            InvokeBackgroundVerb(view, owner, "refresh");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            var stableRefreshSamples = 0;
            var refreshSampleNumber = 0;
            var refreshSamples = new List<string>();
            bool listFocusPreserved = false;
            bool rawRefreshPreservedItem = false;
            int selectedCount = 0;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out visibleCount));
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
                if (!ReadPeerListNames(peerList).SequenceEqual(peerRowsBeforeRefresh, StringComparer.OrdinalIgnoreCase))
                    throw new Exception("The independent ListView lost its old rows while the native Shell view was refreshing.");
                listFocusPreserved = GetFocus() == listView;
                rawRefreshPreservedItem = FocusedItemMatches(folderView, expectedFocusedPidl) && selectedCount == 1;
                // Wait for Shell's item model to settle; selection recovery is performed below
                // with the same production snapshot helper used by ExplorerBrowser refresh.
                var stable = visibleCount == initialCount;
                stableRefreshSamples = stable ? stableRefreshSamples + 1 : 0;
                refreshSamples.Add($"+{refreshSampleNumber++ * 50}ms:items={visibleCount},hwnd={listFocusPreserved},item={FocusedItemMatches(folderView, expectedFocusedPidl)},selection={selectedCount}");
                if (stableRefreshSamples < 3) Thread.Sleep(50);
            } while (stableRefreshSamples < 3 && DateTime.UtcNow < deadline);
            if (visibleCount != initialCount || IsWindowVisible(owner) || GetForegroundWindow() != foreground)
                throw new Exception($"The Shell background Refresh command changed the fixture or desktop boundary: items={visibleCount}, parent-visible={IsWindowVisible(owner)}, foreground-unchanged={GetForegroundWindow() == foreground}.");
            var refreshProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, visibleCount);
            peerRows = refreshProjection.Rows;
            if (!refreshProjection.ExistingIdentitiesPreserved)
                throw new Exception("A Shell item changed identity during a refresh without a fixture mutation.");
            rawRefreshPreservedItem &= stableRefreshSamples == 3;
            (bool SelectionRestored, bool FocusRestored) recovery = default;
            var recoveryDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                recovery = refreshSelection.Restore(view, allowSelectedFocusFallback: false);
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
                if (recovery.SelectionRestored && recovery.FocusRestored && selectedCount == 1 &&
                    FocusedItemMatches(folderView, expectedFocusedPidl)) break;
                Thread.Sleep(50);
            } while (DateTime.UtcNow < recoveryDeadline);
            if (GetFocus() != listView || !recovery.SelectionRestored || !recovery.FocusRestored ||
                !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception($"The production snapshot could not restore Shell selection/focus after Refresh: result={recovery}, item={FocusedItemMatches(folderView, expectedFocusedPidl)}, selection={selectedCount}, focus-hwnd=0x{GetFocus():X}, list-hwnd=0x{listView:X}.");
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 1)
                throw new Exception($"The host could not restore the selected focused item after Refresh: {selectedCount}.");
            Console.WriteLine($"ishellview-menu-command-focus: raw-refresh-preserved-item={rawRefreshPreservedItem}; production-snapshot-restore={recovery.SelectionRestored && recovery.FocusRestored}; hwnd-focus-preserved={GetFocus() == listView}; samples=[{string.Join(';', refreshSamples)}]");
        }
        finally { Marshal.FreeCoTaskMem(expectedFocusedPidl); }

        var view2Id = FolderView2Id;
        Check(Marshal.QueryInterface(folderView, in view2Id, out var view2));
        try
        {
            var nameKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 10 };
            var sort = new NativeSortColumn { Key = nameKey, Direction = 1 };
            Check(At<SetSortColumns>(view2, 27)(view2, [sort], 1));
            InvokeBackgroundVerb(view, owner, "sortdescending");
            RestoreFocusedSelectionAfterMenuCommand("sortdescending");
            var descendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount);
            peerRows = descendingProjection.Rows;
            if (!descendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("The Shell descending sort changed a row's PIDL identity.");
            Check(At<GetSortColumnCount>(view2, 26)(view2, out var sortCount));
            if (sortCount != 1) throw new Exception($"The Shell Sort menu changed the column count unexpectedly: {sortCount}.");
            var sorts = new NativeSortColumn[sortCount];
            Check(At<GetSortColumns>(view2, 28)(view2, sorts, sortCount));
            if (sorts[0].Key.FormatId != nameKey.FormatId || sorts[0].Key.PropertyId != nameKey.PropertyId || sorts[0].Direction != -1)
                throw new Exception($"The Shell Sort Descending command did not update the active sort state: key={sorts[0].Key.FormatId:B}/{sorts[0].Key.PropertyId}, direction={sorts[0].Direction}.");
            VerifyFolderSortOrder(ascending: false, "sortdescending");
            InvokeBackgroundVerb(view, owner, "sortascending");
            RestoreFocusedSelectionAfterMenuCommand("sortascending");
            var ascendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount);
            peerRows = ascendingProjection.Rows;
            if (!ascendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("The Shell ascending sort changed a row's PIDL identity.");
            Check(At<GetSortColumns>(view2, 28)(view2, sorts, sortCount));
            if (sorts[0].Direction != 1) throw new Exception($"The Shell Sort Ascending command did not restore ascending order: direction={sorts[0].Direction}.");
            VerifyFolderSortOrder(ascending: true, "sortascending");

            var typeKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 4 };
            Check(At<SetGroupBy>(view2, 17)(view2, ref typeKey, 1));
            InvokeBackgroundVerb(view, owner, "groupdescending");
            RestoreFocusedSelectionAfterMenuCommand("groupdescending");
            var groupDescendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount, groupByType: true);
            peerRows = groupDescendingProjection.Rows;
            if (!groupDescendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("Grouping changed a Shell row's PIDL identity.");
            VerifyPeerListGrouping(peerList, peerRows);
            var groupResult = At<GetGroupBy>(view2, 18)(view2, out var groupKey, out var groupAscending);
            if (groupResult != 0 || groupKey.FormatId != typeKey.FormatId || groupKey.PropertyId != typeKey.PropertyId || groupAscending != 0)
                throw new Exception($"The Shell Group Descending command did not update the active grouping state: hr=0x{groupResult:X8}, key={groupKey.FormatId:B}/{groupKey.PropertyId}, ascending={groupAscending}.");
            InvokeBackgroundVerb(view, owner, "groupascending");
            RestoreFocusedSelectionAfterMenuCommand("groupascending");
            var groupAscendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount, groupByType: true);
            peerRows = groupAscendingProjection.Rows;
            if (!groupAscendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("Changing group direction changed a Shell row's PIDL identity.");
            VerifyPeerListGrouping(peerList, peerRows);
            groupResult = At<GetGroupBy>(view2, 18)(view2, out groupKey, out groupAscending);
            if (groupResult != 0 || groupKey.FormatId != typeKey.FormatId || groupKey.PropertyId != typeKey.PropertyId || groupAscending != 1)
                throw new Exception($"The Shell Group Ascending command did not restore ascending order: hr=0x{groupResult:X8}, ascending={groupAscending}.");

            var originalPeerIds = peerRows.Select(row => row.Id).ToHashSet();
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(folderPidl, SigDnFileSystemPath, out var peerFixturePathPointer));
            var peerFixturePath = Marshal.PtrToStringUni(peerFixturePathPointer)
                ?? throw new InvalidOperationException("Could not resolve the native Shell fixture folder path.");
            Marshal.FreeCoTaskMem(peerFixturePathPointer);
            var createdPeerPath = Path.Combine(peerFixturePath, "ExplorerDirs-peer-change-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(createdPeerPath, "peer-shell-change");
                SHChangeNotify(ShellChangeCreated, ShellChangePathUnicode, createdPeerPath, null);
                WaitForPeerShellItem(folderView, createdPeerPath, shouldExist: true, initialCount + 1);
                var addedProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                    peerRows, ref nextPeerRowId, initialCount + 1, groupByType: true);
                peerRows = addedProjection.Rows;
                var addedPeerRow = peerRows.Single(row => string.Equals(row.Name, Path.GetFileName(createdPeerPath), StringComparison.OrdinalIgnoreCase));
                var originalRowsRetained = originalPeerIds.All(id => peerRows.Any(row => row.Id == id));
                var newRowIdentityAssigned = !originalPeerIds.Contains(addedPeerRow.Id) && peerRows.Select(row => row.Id).Distinct().Count() == peerRows.Count;
                var detailsAfterCreateAligned = originalRowsRetained && newRowIdentityAssigned;
                if (!detailsAfterCreateAligned)
                    throw new Exception("Shell create notification did not preserve existing peer PIDL rows and assign the new row a distinct identity.");

                File.Delete(createdPeerPath);
                SHChangeNotify(ShellChangeDeleted, ShellChangePathUnicode, createdPeerPath, null);
                WaitForPeerShellItem(folderView, createdPeerPath, shouldExist: false, initialCount);
                var deletedProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                    peerRows, ref nextPeerRowId, initialCount, groupByType: true);
                peerRows = deletedProjection.Rows;
                var deletedIdentityRetired = peerRows.All(row => row.Id != addedPeerRow.Id) &&
                    originalPeerIds.All(id => peerRows.Any(row => row.Id == id));
                if (!deletedIdentityRetired)
                    throw new Exception("Shell delete notification did not retire the removed peer row while preserving surviving PIDL identities.");
                Console.WriteLine($"ishellview-peer-shell-change: create=True;delete=True;existing-pidl-ids-retained={originalRowsRetained};new-row-id-assigned={newRowIdentityAssigned};deleted-row-id-retired={deletedIdentityRetired};details-reconciled=True");

                var renamedOldPath = Path.Combine(peerFixturePath, "keep.txt");
                var renamedNewPath = Path.Combine(peerFixturePath, "keep-renamed.txt");
                var renamedOldRow = peerRows.Single(row => string.Equals(row.Name, Path.GetFileName(renamedOldPath), StringComparison.OrdinalIgnoreCase));
                using var renameWatch = new ShellChangeReconciliationProbe.ChangeWindow();
                renameWatch.Register(folderPidl);
                var renameEventIndex = renameWatch.Events.Count;
                File.Move(renamedOldPath, renamedNewPath);
                SHChangeNotify(ShellChangeRenamedItem, ShellChangePathUnicode, renamedOldPath, renamedNewPath);
                if (!renameWatch.WaitForAnySince(renameEventIndex, [ShellChangeRenamedItem], TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The isolated Shell change window did not receive SHCNE_RENAMEITEM.");
                WaitForPeerShellRename(folderView, renamedOldPath, renamedNewPath, initialCount);
                var renamedProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                    peerRows, ref nextPeerRowId, initialCount, groupByType: true,
                    renamedParsingNames: renameWatch.RenamedParsingNames);
                peerRows = renamedProjection.Rows;
                var renamedPeerRow = peerRows.Single(row => string.Equals(row.Name, Path.GetFileName(renamedNewPath), StringComparison.OrdinalIgnoreCase));
                var renameIdentityPreserved = renamedPeerRow.Id == renamedOldRow.Id && renamedProjection.ExistingIdentitiesPreserved;
                var renamedDetailsUpdated = renamedPeerRow.DetailValues.Length > 0 &&
                    string.Equals(renamedPeerRow.DetailValues[0], Path.GetFileName(renamedNewPath), StringComparison.OrdinalIgnoreCase);
                var renameOtherIdsPreserved = originalPeerIds.Where(id => id != renamedOldRow.Id).All(id => peerRows.Any(row => row.Id == id));
                if (!renameIdentityPreserved || !renamedDetailsUpdated || !renameOtherIdsPreserved)
                    throw new Exception("Shell rename reconciliation did not preserve the old row ID and update the renamed row's details.");
                Console.WriteLine($"ishellview-peer-shell-change: rename=True;rename-notification=True;old-id={renamedOldRow.Id};new-id={renamedPeerRow.Id};identity-preserved={renameIdentityPreserved};details-updated={renamedDetailsUpdated};other-pidl-ids-retained={renameOtherIdsPreserved}");
            }
            finally
            {
                if (File.Exists(createdPeerPath)) File.Delete(createdPeerPath);
            }

            // This intentionally changes sort state only after the menu-command focus matrix;
            // the Shell view uses a disposable fixture and is torn down immediately afterward.
            VerifyColumnHeaderContextMenu(view, owner);
        }
        finally
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            _ = At<SetGroupBy>(view2, 17)(view2, ref noGroup, 0);
            Marshal.Release(view2);
        }

        FreePeerShellRows(peerRows);
        Marshal.Release(shellFolder);
        VerifyPeerListDetails(peerList, peerRows);
        Console.WriteLine($"ishellview-menu-command: viewlogicaldetails=True; peer-view-mode=details; peer-stable-pidl-identity=True; peer-shell-icon-projection=True; peer-shell-details-columns=[{string.Join('|', peerRows[0].DetailTitles)}]; peer-rows-stable-during-refresh=True; peer-refresh-projection=True; peer-sort-projection=True; peer-listview-groups=True; peer-group-headings=[{string.Join('|', peerRows.Select(row => row.TypeName).Distinct(StringComparer.OrdinalIgnoreCase))}]; mode={modeAfterMenu}/{iconSizeAfterMenu}; refresh=True; sortdescending=True; sortascending=True; native-sort-order=[{string.Join(';', nativeSortOrders)}]; groupdescending=True; groupascending=True; focus-recovery=[{string.Join(',', menuCommandFocusRecovery)}]; visible-items={visibleCount}; parent-visible=False; foreground-unchanged=True");
    }

    private static nint CreatePeerListView(nint parent, nint systemImageList)
    {
        var listView = CreateWindowExW(0, "SysListView32", "",
            0x40000000u | 0x00000001u | 0x00000008u, 0, 0, 320, 240, parent, 0, 0, 0);
        if (listView == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the independent peer ListView.");
        _ = SendMessageW(listView, LvmSetImageList, 1, systemImageList); // LVSIL_SMALL; Shell-owned system image list.
        return listView;
    }

    private static void VerifyMultiViewRecursiveSizeProjection(nint firstShellView, nint firstFolderView,
        nint folderPidl, nint shellFolder, nint parent, int expectedCount, string fixture, nint childFolderPidl)
    {
        var foreground = GetForegroundWindow();
        nint secondFolder = 0, secondShellView = 0, secondViewPointer = 0, secondViewWindow = 0, secondFolderView = 0;
        FilteredHost? secondHost = null;
        var secondCreated = false;
        var nextFirstId = 0;
        var nextSecondId = 0;
        var firstProjection = CapturePeerShellRows(firstFolderView, folderPidl, shellFolder, [], ref nextFirstId);
        (List<PeerShellRow> Rows, nint SystemImageList) secondProjection = ([], 0);
        try
        {
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out secondFolder));
            var viewId = ShellViewId;
            Check(At<CreateViewObject>(secondFolder, 8)(secondFolder, parent, ref viewId, out secondShellView));
            secondHost = new FilteredHost(parent,
                new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), folderPidl, childFolderPidl,
                (_, _) => Marshal.GetHRForException(new InvalidOperationException("Unexpected navigation in multi-view probe.")));
            secondHost.View = secondShellView;
            secondViewPointer = Marshal.GetComInterfaceForObject(secondHost, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(secondShellView, 9)(secondShellView, 0, ref settings, secondViewPointer, ref bounds, out secondViewWindow));
            secondCreated = true;
            var folderViewId = FolderViewId;
            Check(Marshal.QueryInterface(secondShellView, in folderViewId, out secondFolderView));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int secondCount;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(secondFolderView, 7)(secondFolderView, 2, out secondCount));
                if (secondCount == expectedCount && secondHost.IncludeObjectCalls >= 3) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(At<ItemCount>(secondFolderView, 7)(secondFolderView, 2, out secondCount));
            if (secondCount != expectedCount)
                throw new TimeoutException($"Second real Shell view did not enumerate the shared fixture: expected={expectedCount}, actual={secondCount}.");

            secondProjection = CapturePeerShellRows(secondFolderView, folderPidl, secondFolder, [], ref nextSecondId);
            var firstList = CreatePeerListView(parent, firstProjection.SystemImageList);
            var secondList = CreatePeerListView(parent, secondProjection.SystemImageList);
            PopulatePeerList(firstList, firstProjection.Rows);
            PopulatePeerList(secondList, secondProjection.Rows);
            SetPeerListMode(firstList, LvsReport);
            SetPeerListMode(secondList, LvsReport);
            InsertPeerColumn(firstList, firstProjection.Rows[0].DetailTitles.Length, "Recursive size (on demand)");
            InsertPeerColumn(secondList, secondProjection.Rows[0].DetailTitles.Length, "Recursive size (on demand)");
            for (var index = 0; index < firstProjection.Rows.Count; index++)
                SetPeerListCell(firstList, index, firstProjection.Rows[index].DetailTitles.Length, "");
            for (var index = 0; index < secondProjection.Rows.Count; index++)
                SetPeerListCell(secondList, index, secondProjection.Rows[index].DetailTitles.Length, "");

            var firstIds = ReadPeerListRowIds(firstList);
            var secondIds = ReadPeerListRowIds(secondList);
            var firstSelected = firstProjection.Rows.Single(row => row.Name == "size-10");
            var secondSelected = secondProjection.Rows.Single(row => row.Name == "size-10");
            SetPeerListItemState(firstList, firstProjection.Rows.IndexOf(firstSelected), 0x0003, 0x0003);
            SetPeerListItemState(secondList, secondProjection.Rows.IndexOf(secondSelected), 0x0003, 0x0003);

            var scanStarts = 0;
            var cache = new ConcurrentDictionary<string, Lazy<Task<RecursiveDirectoryScanResult>>>(StringComparer.OrdinalIgnoreCase);
            var cacheGenerations = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            using var watcher = new ShellChangeReconciliationProbe.ChangeWindow();
            watcher.Register(folderPidl);
            string[] InvalidateFromShellChanges(int eventIndex)
            {
                var updates = watcher.ChangeEvents.Skip(eventIndex)
                    .Where(change => change.EventId is ShellChangeCreated or ShellChangeDeleted or ShellChangeMakeDirectory or
                        ShellChangeRemoveDirectory or ShellChangeRenamedItem or
                        ShellChangeRenamedFolder or ShellChangeUpdatedItem or ShellChangeUpdatedDirectory)
                    .SelectMany(change => new[] { (change.EventId, Path: change.FirstPath), (change.EventId, Path: change.SecondPath) })
                    .Where(update => !string.IsNullOrWhiteSpace(update.Path) && Path.IsPathRooted(update.Path))
                    .Select(update => (update.EventId, Path: Path.GetFullPath(update.Path!))).ToArray();
                var updatedDirectories = updates.Where(update => update.EventId == ShellChangeUpdatedDirectory)
                    .Select(update => update.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var structuralPaths = updates.Where(update => update.EventId is ShellChangeCreated or ShellChangeDeleted or
                        ShellChangeMakeDirectory or ShellChangeRemoveDirectory or
                        ShellChangeRenamedItem or ShellChangeRenamedFolder)
                    .Select(update => update.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var changedPaths = structuralPaths.Length > 0
                    ? structuralPaths
                    : updates.Where(update => update.EventId == ShellChangeUpdatedDirectory || updatedDirectories.Length == 0 ||
                            updatedDirectories.Any(directory => IsSameOrDescendantPath(update.Path, directory)))
                    .Select(update => update.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var invalidated = cache.Keys.Where(cachedRoot => changedPaths.Any(changedPath =>
                        IsSameOrDescendantPath(changedPath, cachedRoot)))
                    .ToArray();
                foreach (var cachedRoot in invalidated)
                {
                    _ = cache.TryRemove(cachedRoot, out _);
                    _ = cacheGenerations.AddOrUpdate(cachedRoot, 1, static (_, generation) => generation + 1);
                }
                return invalidated;
            }

            Task<RecursiveDirectoryScanResult> Request(string path)
            {
                var fullPath = Path.GetFullPath(path);
                return cache.GetOrAdd(fullPath, key => new Lazy<Task<RecursiveDirectoryScanResult>>(() => Task.Run(() =>
                {
                    Interlocked.Increment(ref scanStarts);
                    return RecursiveDirectoryScanner.Scan(key, CancellationToken.None);
                }))).Value;
            }

            bool TryPublish(string path, long requestGeneration, RecursiveDirectoryScanResult result)
            {
                var fullPath = Path.GetFullPath(path);
                if (cacheGenerations.GetOrAdd(fullPath, 0) != requestGeneration || !Directory.Exists(fullPath))
                    return false;
                return cache.TryAdd(fullPath,
                    new Lazy<Task<RecursiveDirectoryScanResult>>(() => Task.FromResult(result)));
            }

            void PerformShellFileOperation(Action<nint> enqueueOperation)
            {
                nint operation = 0;
                try
                {
                    var operationClassId = FileOperationClassId;
                    var operationId = FileOperationId;
                    Marshal.ThrowExceptionForHR(CoCreateInstance(ref operationClassId, 0, 1, ref operationId, out operation));
                    Marshal.ThrowExceptionForHR(At<SetOperationFlags>(operation, 5)(operation,
                        0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                    enqueueOperation(operation);
                    Marshal.ThrowExceptionForHR(At<PerformOperations>(operation, 21)(operation));
                    Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
                    if (aborted != 0) throw new IOException("The isolated Shell file operation was aborted.");
                }
                finally
                {
                    if (operation != 0) Marshal.Release(operation);
                }
            }

            nint GetShellItem(string path)
            {
                var itemId = ShellItemId;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref itemId, out var item));
                return item;
            }

            var firstPaths = firstProjection.Rows.Where(row => row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
                .Select(row => Path.GetFullPath(row.FileSystemPath!)).ToArray();
            var secondPaths = secondProjection.Rows.Where(row => row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
                .Select(row => Path.GetFullPath(row.FileSystemPath!)).ToArray();
            if (firstPaths.Length != 4 || !firstPaths.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(secondPaths))
                throw new InvalidOperationException($"The two real Shell views did not expose the same recursive-size directory rows: first=[{string.Join('|', firstProjection.Rows.Select(row => $"{row.Name}:{row.FileSystemPath}"))}], second=[{string.Join('|', secondProjection.Rows.Select(row => $"{row.Name}:{row.FileSystemPath}"))}].");
            var firstRequests = firstPaths.Select(path => Request(path)).ToArray();
            var secondRequests = secondPaths.Select(path => Request(path)).ToArray();
            if (!Task.WaitAll(firstRequests.Concat(secondRequests).ToArray(), TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The shared recursive scans did not complete within ten seconds.");
            var firstResults = firstRequests.Select((task, index) => (Path: firstPaths[index], Result: task.GetAwaiter().GetResult()))
                .ToDictionary(item => item.Path, item => item.Result, StringComparer.OrdinalIgnoreCase);
            var secondResults = secondRequests.Select((task, index) => (Path: secondPaths[index], Result: task.GetAwaiter().GetResult()))
                .ToDictionary(item => item.Path, item => item.Result, StringComparer.OrdinalIgnoreCase);
            if (scanStarts != 4 || firstResults.Values.Aggregate(0UL, (total, result) => total + result.Bytes) != 118UL ||
                !firstResults.All(pair => secondResults.TryGetValue(pair.Key, out var result) && ReferenceEquals(result, pair.Value)))
                throw new InvalidOperationException($"Concurrent views did not share four in-flight recursive scans: starts={scanStarts}.");

            var firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
            var secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
            SortPeerListByRecursiveSize(firstList, firstBytesById);
            SortPeerListByRecursiveSize(secondList, secondBytesById);
            VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
            VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);

            var changedDirectory = Path.GetFullPath(Path.Combine(fixture, "size-10"));
            var changedPayload = Path.Combine(changedDirectory, "payload.bin");
            var eventIndex = watcher.Events.Count;
            File.WriteAllBytes(changedPayload, new byte[20]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            SHChangeNotify(ShellChangeUpdatedDirectory, ShellChangePathUnicode, changedDirectory, null);
            if (!watcher.WaitForAnySince(eventIndex, [ShellChangeUpdatedItem, ShellChangeUpdatedDirectory], TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The shared Shell change observer did not receive the modified-directory notification.");
            WaitForPeerShellItem(firstFolderView, changedDirectory, shouldExist: true, expectedCount);
            WaitForPeerShellItem(secondFolderView, changedDirectory, shouldExist: true, expectedCount);
            var refreshedFirst = CapturePeerShellRows(firstFolderView, folderPidl, shellFolder, firstProjection.Rows, ref nextFirstId);
            var refreshedSecond = CapturePeerShellRows(secondFolderView, folderPidl, secondFolder, secondProjection.Rows, ref nextSecondId);
            if (!refreshedFirst.Rows.Select(row => row.Id).ToHashSet().SetEquals(firstIds) ||
                !refreshedSecond.Rows.Select(row => row.Id).ToHashSet().SetEquals(secondIds))
                throw new InvalidOperationException("Shell notifications changed stable row identities in one of the peer views.");
            // CapturePeerShellRows transfers matching absolute PIDLs and releases old relative PIDLs itself.
            firstProjection = refreshedFirst;
            secondProjection = refreshedSecond;

            var invalidatedRoots = InvalidateFromShellChanges(eventIndex);
            if (invalidatedRoots.Length != 1 || !string.Equals(invalidatedRoots[0], changedDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Shell update notifications did not invalidate exactly the affected recursive directory: invalidated=[{string.Join('|', invalidatedRoots)}]; events=[{string.Join('|', watcher.ChangeEvents.Skip(eventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}->{change.SecondPath}"))}].");
            var invalidatedFirst = Request(changedDirectory);
            var invalidatedSecond = Request(changedDirectory);
            if (!Task.WaitAll([invalidatedFirst, invalidatedSecond], TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The post-notification recursive scan did not complete within ten seconds.");
            if (scanStarts != 5 || invalidatedFirst.Result.Bytes != 20 || !ReferenceEquals(invalidatedFirst.Result, invalidatedSecond.Result))
                throw new InvalidOperationException($"The two views did not share one fresh scan after Shell notification invalidation: scans={scanStarts}; first-bytes={invalidatedFirst.Result.Bytes}; second-bytes={invalidatedSecond.Result.Bytes}; shared-result={ReferenceEquals(invalidatedFirst.Result, invalidatedSecond.Result)}.");
            firstResults[changedDirectory] = invalidatedFirst.Result;
            secondResults[changedDirectory] = invalidatedSecond.Result;
            firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
            secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
            SortPeerListByRecursiveSize(firstList, firstBytesById);
            SortPeerListByRecursiveSize(secondList, secondBytesById);
            VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
            VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);

            void RescanAfterNotification(int eventIndex, uint[] expectedEvents, ulong expectedBytes, int expectedScanCount, string description)
            {
                if (!watcher.WaitForAnySince(eventIndex, expectedEvents, TimeSpan.FromSeconds(5)))
                    throw new TimeoutException($"The Shell change observer did not receive the {description} notification.");
                var invalidated = InvalidateFromShellChanges(eventIndex);
                if (invalidated.Length != 1 || !string.Equals(invalidated[0], changedDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The {description} notification did not invalidate only its recursive parent: [{string.Join('|', invalidated)}].");
                var first = Request(changedDirectory);
                var second = Request(changedDirectory);
                if (!Task.WaitAll([first, second], TimeSpan.FromSeconds(10)))
                    throw new TimeoutException($"The {description} recursive scan did not complete within ten seconds.");
                if (scanStarts != expectedScanCount || first.Result.Bytes != expectedBytes || !ReferenceEquals(first.Result, second.Result))
                    throw new InvalidOperationException($"The {description} update did not share the expected fresh scan: scans={scanStarts}; bytes={first.Result.Bytes}; shared={ReferenceEquals(first.Result, second.Result)}.");

                firstResults[changedDirectory] = first.Result;
                secondResults[changedDirectory] = second.Result;
                firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
                secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
                SortPeerListByRecursiveSize(firstList, firstBytesById);
                SortPeerListByRecursiveSize(secondList, secondBytesById);
                VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
                VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);
                Console.WriteLine($"ishellview-peer-recursive-size-change: kind={description}; operation=IFileOperation; natural-shell-notification=True; events=[{string.Join('|', watcher.ChangeEvents.Skip(eventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}->{change.SecondPath}"))}]; invalidated-roots={invalidated.Length}; shared-scans={scanStarts}; bytes={expectedBytes}; row-ids-stable=True; selection-focus-preserved=True");
            }

            bool WaitForShellPathsSince(int eventIndex, uint eventId, IReadOnlyCollection<string> expectedPaths, TimeSpan timeout)
            {
                var expected = expectedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
                while (Environment.TickCount64 < deadline)
                {
                    Application.DoEvents();
                    var observed = watcher.ChangeEvents.Skip(eventIndex)
                        .Where(change => change.EventId == eventId && change.FirstPath is not null)
                        .Select(change => Path.GetFullPath(change.FirstPath!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (expected.IsSubsetOf(observed)) return true;
                    Thread.Sleep(10);
                }
                return expected.IsSubsetOf(watcher.ChangeEvents.Skip(eventIndex)
                    .Where(change => change.EventId == eventId && change.FirstPath is not null)
                    .Select(change => Path.GetFullPath(change.FirstPath!)).ToHashSet(StringComparer.OrdinalIgnoreCase));
            }

            var addedPayloads = new[]
            {
                Path.Combine(changedDirectory, "added-1.bin"),
                Path.Combine(changedDirectory, "added-2.bin"),
                Path.Combine(changedDirectory, "added-3.bin")
            };
            var addedPayloadSizes = new[] { 1, 2, 3 };
            var createEventIndex = watcher.Events.Count;
            var createParentItem = GetShellItem(changedDirectory);
            try
            {
                PerformShellFileOperation(operation =>
                {
                    foreach (var path in addedPayloads)
                        Marshal.ThrowExceptionForHR(At<NewItem>(operation, 20)(operation, createParentItem,
                            0x00000080, Path.GetFileName(path), null, 0));
                });
            }
            finally { Marshal.Release(createParentItem); }
            for (var index = 0; index < addedPayloads.Length; index++)
                File.WriteAllBytes(addedPayloads[index], new byte[addedPayloadSizes[index]]);
            if (!WaitForShellPathsSince(createEventIndex, ShellChangeCreated, addedPayloads, TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"The batched Shell create did not deliver one CREATE event for each file: events=[{string.Join('|', watcher.ChangeEvents.Skip(createEventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}"))}].");
            RescanAfterNotification(createEventIndex, [ShellChangeCreated, ShellChangeUpdatedDirectory], 26, 6, "create-burst");

            var deleteEventIndex = watcher.Events.Count;
            var deleteShellItems = addedPayloads.Select(GetShellItem).ToArray();
            try
            {
                PerformShellFileOperation(operation =>
                {
                    foreach (var item in deleteShellItems)
                        Marshal.ThrowExceptionForHR(At<DeleteItem>(operation, 18)(operation, item, 0));
                });
            }
            finally
            {
                foreach (var item in deleteShellItems) Marshal.Release(item);
            }
            if (!WaitForShellPathsSince(deleteEventIndex, ShellChangeDeleted, addedPayloads, TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"The batched Shell delete did not deliver one DELETE event for each file: events=[{string.Join('|', watcher.ChangeEvents.Skip(deleteEventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}"))}].");
            RescanAfterNotification(deleteEventIndex, [ShellChangeDeleted, ShellChangeUpdatedDirectory], 20, 7, "delete-burst");

            var renamedPayload = Path.Combine(changedDirectory, "renamed.bin");
            var renameEventIndex = watcher.Events.Count;
            var renameItem = GetShellItem(changedPayload);
            try
            {
                PerformShellFileOperation(operation => Marshal.ThrowExceptionForHR(At<RenameItem>(operation, 12)(operation,
                    renameItem, "renamed.bin", 0)));
            }
            finally { Marshal.Release(renameItem); }
            RescanAfterNotification(renameEventIndex, [ShellChangeRenamedItem, ShellChangeUpdatedDirectory], 20, 8, "rename");

            var renamedDirectory = Path.GetFullPath(Path.Combine(fixture, "size-10-renamed"));
            var oldPathGeneration = cacheGenerations.GetOrAdd(changedDirectory, 0);
            using var oldPathScanCancellation = new CancellationTokenSource();
            using var oldPathScanOpened = new ManualResetEventSlim();
            using var releaseOldPathScan = new ManualResetEventSlim();
            var oldPathScan = Task.Run(() =>
            {
                Interlocked.Increment(ref scanStarts);
                return HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory], oldPathScanCancellation.Token,
                    (_, _) =>
                    {
                        oldPathScanOpened.Set();
                        if (!releaseOldPathScan.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("The in-flight old-path scan was not released after the folder rename.");
                    });
            });
            var oldPathScanDeadline = Environment.TickCount64 + 5000;
            while (!oldPathScanOpened.IsSet && Environment.TickCount64 < oldPathScanDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!oldPathScanOpened.IsSet)
            {
                releaseOldPathScan.Set();
                throw new TimeoutException("The old-path scan did not open its directory before the rename race.");
            }

            var folderRenameEventIndex = watcher.Events.Count;
            nint folderItem = 0, renameOperation = 0;
            try
            {
                var shellItemId = ShellItemId;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(changedDirectory, 0, ref shellItemId, out folderItem));
                var operationClassId = FileOperationClassId;
                var operationId = FileOperationId;
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref operationClassId, 0, 1, ref operationId, out renameOperation));
                Marshal.ThrowExceptionForHR(At<SetOperationFlags>(renameOperation, 5)(renameOperation,
                    0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                Marshal.ThrowExceptionForHR(At<RenameItem>(renameOperation, 12)(renameOperation, folderItem,
                    Path.GetFileName(renamedDirectory), 0));
                Marshal.ThrowExceptionForHR(At<PerformOperations>(renameOperation, 21)(renameOperation));
                Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(renameOperation, 22)(renameOperation, out var renameAborted));
                if (renameAborted != 0) throw new IOException("The isolated Shell folder rename was aborted.");
            }
            finally
            {
                if (renameOperation != 0) Marshal.Release(renameOperation);
                if (folderItem != 0) Marshal.Release(folderItem);
            }
            if (Directory.Exists(changedDirectory) || !Directory.Exists(renamedDirectory))
                throw new IOException("IFileOperation did not rename the isolated recursive-size folder.");
            if (!watcher.WaitForAnySince(folderRenameEventIndex, [ShellChangeRenamedFolder], TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"IFileOperation did not produce a natural Shell folder-rename notification: events=[{string.Join('|', watcher.ChangeEvents.Skip(folderRenameEventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}->{change.SecondPath}"))}].");
            WaitForPeerShellRename(firstFolderView, changedDirectory, renamedDirectory, expectedCount);
            WaitForPeerShellRename(secondFolderView, changedDirectory, renamedDirectory, expectedCount);
            var folderRenamedFirst = CapturePeerShellRows(firstFolderView, folderPidl, shellFolder, firstProjection.Rows, ref nextFirstId);
            var folderRenamedSecond = CapturePeerShellRows(secondFolderView, folderPidl, secondFolder, secondProjection.Rows, ref nextSecondId);
            var firstRenamedRow = folderRenamedFirst.Rows.Single(row => row.Name == "size-10-renamed");
            var secondRenamedRow = folderRenamedSecond.Rows.Single(row => row.Name == "size-10-renamed");
            if (firstRenamedRow.Id != firstSelected.Id || secondRenamedRow.Id != secondSelected.Id ||
                !folderRenamedFirst.Rows.Select(row => row.Id).ToHashSet().SetEquals(firstIds) ||
                !folderRenamedSecond.Rows.Select(row => row.Id).ToHashSet().SetEquals(secondIds))
                throw new InvalidOperationException("Renaming a recursive Shell directory changed its stable row identity in one of the peer views.");

            // The directory name changed while the peer Lists remain size-sorted, so update the Name cells by stable row ID.
            var firstListIndexById = ReadPeerListRowIds(firstList).Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
            var secondListIndexById = ReadPeerListRowIds(secondList).Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
            foreach (var row in folderRenamedFirst.Rows)
                SetPeerListCell(firstList, firstListIndexById[row.Id], 0, row.Name);
            foreach (var row in folderRenamedSecond.Rows)
                SetPeerListCell(secondList, secondListIndexById[row.Id], 0, row.Name);
            firstProjection = folderRenamedFirst;
            secondProjection = folderRenamedSecond;

            var folderInvalidatedRoots = InvalidateFromShellChanges(folderRenameEventIndex);
            if (folderInvalidatedRoots.Length != 1 || !string.Equals(folderInvalidatedRoots[0], changedDirectory, StringComparison.OrdinalIgnoreCase) ||
                cache.ContainsKey(changedDirectory) || cache.ContainsKey(renamedDirectory))
                throw new InvalidOperationException($"Folder rename did not retire exactly the old recursive cache key: invalidated=[{string.Join('|', folderInvalidatedRoots)}].");
            if (cacheGenerations[changedDirectory] == oldPathGeneration || Directory.Exists(changedDirectory))
            {
                releaseOldPathScan.Set();
                throw new InvalidOperationException("The folder rename did not retire the old-path scan ticket or path.");
            }
            var renamedDirectoryFirst = Request(renamedDirectory);
            var renamedDirectorySecond = Request(renamedDirectory);
            if (!Task.WaitAll([renamedDirectoryFirst, renamedDirectorySecond], TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The renamed recursive directory did not complete its fresh scan within ten seconds.");
            if (scanStarts != 10 || renamedDirectoryFirst.Result.Bytes != 20 ||
                !ReferenceEquals(renamedDirectoryFirst.Result, renamedDirectorySecond.Result))
                throw new InvalidOperationException($"The renamed recursive folder did not share one fresh snapshot: scans={scanStarts}; bytes={renamedDirectoryFirst.Result.Bytes}.");

            releaseOldPathScan.Set();
            if (!oldPathScan.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The pre-rename scan did not finish after releasing its opened directory handle.");
            var staleOldPathResult = oldPathScan.GetAwaiter().GetResult();
            var staleOldPathSnapshot = staleOldPathResult[changedDirectory];
            var staleOldPathWasPublished = TryPublish(changedDirectory, oldPathGeneration, staleOldPathSnapshot.Summary);
            var sharedNewPathAfterStaleCompletion = Request(renamedDirectory);
            if (staleOldPathSnapshot.Summary.Bytes != renamedDirectoryFirst.Result.Bytes || staleOldPathWasPublished ||
                cache.ContainsKey(changedDirectory) || !ReferenceEquals(sharedNewPathAfterStaleCompletion.Result, renamedDirectoryFirst.Result))
                throw new InvalidOperationException($"The stale pre-rename scan was not retired cleanly: old-bytes={staleOldPathSnapshot.Summary.Bytes}; new-bytes={renamedDirectoryFirst.Result.Bytes}; old-published={staleOldPathWasPublished}; new-shared={ReferenceEquals(sharedNewPathAfterStaleCompletion.Result, renamedDirectoryFirst.Result)}.");
            firstResults.Remove(changedDirectory);
            secondResults.Remove(changedDirectory);
            firstResults[renamedDirectory] = renamedDirectoryFirst.Result;
            secondResults[renamedDirectory] = renamedDirectorySecond.Result;
            firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
            secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
            SortPeerListByRecursiveSize(firstList, firstBytesById);
            SortPeerListByRecursiveSize(secondList, secondBytesById);
            VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
            VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);
            if (!ReadPeerListNames(firstList).Contains("size-10-renamed", StringComparer.OrdinalIgnoreCase) ||
                !ReadPeerListNames(secondList).Contains("size-10-renamed", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("The peer projections did not publish the renamed directory caption.");
            Console.WriteLine($"ishellview-peer-recursive-size-change: kind=folder-rename; operation=IFileOperation.RenameItem; natural-shell-notification=True; invalidated-roots={folderInvalidatedRoots.Length}; old-cache-retired=True; in-flight-old-path-scan-completed=True; stale-result-rejected=True; new-path-shared-scan=True; bytes={renamedDirectoryFirst.Result.Bytes}; row-id={firstSelected.Id}; selection-focus-preserved=True");

            var nestedDirectory = Path.Combine(renamedDirectory, "nested");
            var nestedCreateEventIndex = watcher.Events.Count;
            var nestedParentItem = GetShellItem(renamedDirectory);
            try
            {
                PerformShellFileOperation(operation => Marshal.ThrowExceptionForHR(At<NewItem>(operation, 20)(operation,
                    nestedParentItem, 0x00000010, "nested", null, 0)));
            }
            finally { Marshal.Release(nestedParentItem); }
            if (!Directory.Exists(nestedDirectory) ||
                !watcher.WaitForAnySince(nestedCreateEventIndex, [ShellChangeMakeDirectory], TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"The Shell folder-create operation did not produce SHCNE_MKDIR: events=[{string.Join('|', watcher.ChangeEvents.Skip(nestedCreateEventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}->{change.SecondPath}"))}].");
            File.WriteAllBytes(Path.Combine(nestedDirectory, "nested.bin"), new byte[4]);
            var nestedCreateInvalidated = InvalidateFromShellChanges(nestedCreateEventIndex);
            if (nestedCreateInvalidated.Length != 1 || !string.Equals(nestedCreateInvalidated[0], renamedDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SHCNE_MKDIR did not invalidate only its recursive parent: [{string.Join('|', nestedCreateInvalidated)}].");
            var nestedCreateFirst = Request(renamedDirectory);
            var nestedCreateSecond = Request(renamedDirectory);
            if (!Task.WaitAll([nestedCreateFirst, nestedCreateSecond], TimeSpan.FromSeconds(10)) || scanStarts != 11 ||
                nestedCreateFirst.Result.Bytes != 24 || !ReferenceEquals(nestedCreateFirst.Result, nestedCreateSecond.Result))
                throw new InvalidOperationException($"The nested-folder creation did not share one fresh 24 B scan: scans={scanStarts}; bytes={nestedCreateFirst.Result.Bytes}; shared={ReferenceEquals(nestedCreateFirst.Result, nestedCreateSecond.Result)}.");
            firstResults[renamedDirectory] = nestedCreateFirst.Result;
            secondResults[renamedDirectory] = nestedCreateSecond.Result;
            firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
            secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
            SortPeerListByRecursiveSize(firstList, firstBytesById);
            SortPeerListByRecursiveSize(secondList, secondBytesById);
            VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
            VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);

            var nestedCachedFirst = Request(nestedDirectory);
            var nestedCachedSecond = Request(nestedDirectory);
            if (!ReferenceEquals(nestedCachedFirst.Result, nestedCachedSecond.Result) || nestedCachedFirst.Result.Bytes != 4 || scanStarts != 12)
                throw new InvalidOperationException($"The original nested directory did not have one cached 4 B result: scans={scanStarts}; bytes={nestedCachedFirst.Result.Bytes}; shared={ReferenceEquals(nestedCachedFirst.Result, nestedCachedSecond.Result)}.");
            var nestedGenerationBeforeReplacement = cacheGenerations.GetOrAdd(nestedDirectory, 0);
            using var oldNestedScanComputed = new ManualResetEventSlim();
            using var releaseOldNestedScan = new ManualResetEventSlim();
            using var oldNestedScanCancellation = new CancellationTokenSource();
            ShellFileSystemIdentity oldNestedIdentity = default;
            RecursiveDirectoryScanResult? oldNestedSummary = null;
            var oldNestedScan = Task.Run(() =>
            {
                Interlocked.Increment(ref scanStarts);
                return HandleBoundRecursiveDirectoryScanner.Scan([nestedDirectory], oldNestedScanCancellation.Token,
                    afterRootScannedBeforeReturn: (path, identity, summary) =>
                    {
                        oldNestedIdentity = identity;
                        oldNestedSummary = summary;
                        oldNestedScanComputed.Set();
                        if (!releaseOldNestedScan.Wait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("The old same-path result was not released after the directory replacement.");
                    });
            });
            var oldNestedDeadline = Environment.TickCount64 + 5000;
            while (!oldNestedScanComputed.IsSet && Environment.TickCount64 < oldNestedDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!oldNestedScanComputed.IsSet || oldNestedSummary?.Bytes != 4 || scanStarts != 13)
            {
                releaseOldNestedScan.Set();
                throw new TimeoutException($"The original nested scan did not capture its 4 B result before replacement: opened={oldNestedScanComputed.IsSet}; bytes={oldNestedSummary?.Bytes}; scans={scanStarts}.");
            }

            var nestedDeleteEventIndex = watcher.Events.Count;
            var nestedShellItem = GetShellItem(nestedDirectory);
            try
            {
                PerformShellFileOperation(operation => Marshal.ThrowExceptionForHR(At<DeleteItem>(operation, 18)(operation, nestedShellItem, 0)));
            }
            finally { Marshal.Release(nestedShellItem); }
            var nestedDeleteNotified = watcher.WaitForAnySince(nestedDeleteEventIndex,
                [ShellChangeRemoveDirectory, ShellChangeDeleted], TimeSpan.FromSeconds(5));
            var nestedDirectoryDeleteEvent = watcher.ChangeEvents.Skip(nestedDeleteEventIndex).Any(change =>
                (change.EventId is ShellChangeRemoveDirectory or ShellChangeDeleted) &&
                string.Equals(change.FirstPath, nestedDirectory, StringComparison.OrdinalIgnoreCase));
            if (Directory.Exists(nestedDirectory) || !nestedDeleteNotified || !nestedDirectoryDeleteEvent)
                throw new TimeoutException($"The Shell folder-delete operation did not produce a directory removal event: directory-exists={Directory.Exists(nestedDirectory)}; matching-directory-event={nestedDirectoryDeleteEvent}; events=[{string.Join('|', watcher.ChangeEvents.Skip(nestedDeleteEventIndex).Select(change => $"0x{change.EventId:X8}:{change.FirstPath}->{change.SecondPath}"))}].");
            var nestedDeleteInvalidated = InvalidateFromShellChanges(nestedDeleteEventIndex);
            if (!nestedDeleteInvalidated.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([renamedDirectory, nestedDirectory]) ||
                cacheGenerations[nestedDirectory] == nestedGenerationBeforeReplacement)
                throw new InvalidOperationException($"The nested-folder removal event did not retire both cached recursive roots and advance the old path ticket: roots=[{string.Join('|', nestedDeleteInvalidated)}].");
            var nestedDeleteFirst = Request(renamedDirectory);
            var nestedDeleteSecond = Request(renamedDirectory);
            if (!Task.WaitAll([nestedDeleteFirst, nestedDeleteSecond], TimeSpan.FromSeconds(10)) || scanStarts != 14 ||
                nestedDeleteFirst.Result.Bytes != 20 || !ReferenceEquals(nestedDeleteFirst.Result, nestedDeleteSecond.Result))
                throw new InvalidOperationException($"The nested-folder deletion did not share one fresh 20 B scan: scans={scanStarts}; bytes={nestedDeleteFirst.Result.Bytes}; shared={ReferenceEquals(nestedDeleteFirst.Result, nestedDeleteSecond.Result)}.");
            firstResults[renamedDirectory] = nestedDeleteFirst.Result;
            secondResults[renamedDirectory] = nestedDeleteSecond.Result;
            firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
            secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
            SortPeerListByRecursiveSize(firstList, firstBytesById);
            SortPeerListByRecursiveSize(secondList, secondBytesById);
            VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
            VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);

            var nestedReplacementCreateEventIndex = watcher.Events.Count;
            var replacementParentItem = GetShellItem(renamedDirectory);
            try
            {
                PerformShellFileOperation(operation => Marshal.ThrowExceptionForHR(At<NewItem>(operation, 20)(operation,
                    replacementParentItem, 0x00000010, "nested", null, 0)));
            }
            finally { Marshal.Release(replacementParentItem); }
            if (!Directory.Exists(nestedDirectory) ||
                !watcher.WaitForAnySince(nestedReplacementCreateEventIndex, [ShellChangeMakeDirectory], TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The replacement directory was not created with a natural SHCNE_MKDIR event.");
            File.WriteAllBytes(Path.Combine(nestedDirectory, "replacement.bin"), new byte[6]);
            var nestedReplacementInvalidated = InvalidateFromShellChanges(nestedReplacementCreateEventIndex);
            if (nestedReplacementInvalidated.Length != 1 || !string.Equals(nestedReplacementInvalidated[0], renamedDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Creating the same-path replacement did not invalidate its recursive parent: [{string.Join('|', nestedReplacementInvalidated)}].");
            var replacementRootFirst = Request(renamedDirectory);
            var replacementRootSecond = Request(renamedDirectory);
            var replacementNestedFirst = Request(nestedDirectory);
            var replacementNestedSecond = Request(nestedDirectory);
            if (!Task.WaitAll([replacementRootFirst, replacementRootSecond, replacementNestedFirst, replacementNestedSecond], TimeSpan.FromSeconds(10)) ||
                scanStarts != 16 || replacementRootFirst.Result.Bytes != 26 || replacementNestedFirst.Result.Bytes != 6 ||
                !ReferenceEquals(replacementRootFirst.Result, replacementRootSecond.Result) ||
                !ReferenceEquals(replacementNestedFirst.Result, replacementNestedSecond.Result))
                throw new InvalidOperationException($"The replacement directory did not publish shared 26/6 B scans: scans={scanStarts}; root={replacementRootFirst.Result.Bytes}; nested={replacementNestedFirst.Result.Bytes}.");
            var replacementIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(nestedDirectory);
            if (replacementIdentity is null || replacementIdentity == oldNestedIdentity)
                throw new InvalidOperationException("The replacement directory did not expose a new filesystem identity at the reused path.");
            releaseOldNestedScan.Set();
            if (!oldNestedScan.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The old same-path scan did not finish after releasing its result.");
            var oldNestedResult = oldNestedScan.GetAwaiter().GetResult();
            if (!oldNestedResult.TryGetValue(nestedDirectory, out var oldNestedScanValue) ||
                oldNestedScanValue.Identity != oldNestedIdentity || oldNestedScanValue.Summary.Bytes != 4 ||
                TryPublish(nestedDirectory, nestedGenerationBeforeReplacement, oldNestedScanValue.Summary) ||
                !ReferenceEquals(Request(nestedDirectory).Result, replacementNestedFirst.Result) ||
                !ReferenceEquals(Request(renamedDirectory).Result, replacementRootFirst.Result))
                throw new InvalidOperationException("The stale result from the deleted directory was published over its same-path replacement.");
            firstResults[renamedDirectory] = replacementRootFirst.Result;
            secondResults[renamedDirectory] = replacementRootSecond.Result;
            firstBytesById = ApplyRecursiveValues(firstList, firstProjection.Rows, firstResults);
            secondBytesById = ApplyRecursiveValues(secondList, secondProjection.Rows, secondResults);
            SortPeerListByRecursiveSize(firstList, firstBytesById);
            SortPeerListByRecursiveSize(secondList, secondBytesById);
            VerifySortedSelection(firstList, firstProjection.Rows, firstBytesById, firstIds, firstSelected.Id);
            VerifySortedSelection(secondList, secondProjection.Rows, secondBytesById, secondIds, secondSelected.Id);
            var observedDirectoryDeleteEvent = watcher.ChangeEvents.Skip(nestedDeleteEventIndex)
                .First(change => (change.EventId is ShellChangeRemoveDirectory or ShellChangeDeleted) &&
                    string.Equals(change.FirstPath, nestedDirectory, StringComparison.OrdinalIgnoreCase)).EventId;
            Console.WriteLine($"ishellview-peer-recursive-size-change: kind=nested-folder-replace-same-path; create-event=SHCNE_MKDIR; delete-event=0x{observedDirectoryDeleteEvent:X8}; invalidated-roots={nestedCreateInvalidated.Length + nestedDeleteInvalidated.Length + nestedReplacementInvalidated.Length}; old-bytes={oldNestedScanValue.Summary.Bytes}; replacement-bytes={replacementNestedFirst.Result.Bytes}; old-identity-retired=True; stale-publish-rejected=True; parent-bytes=[deleted:{nestedDeleteFirst.Result.Bytes},replacement:{replacementRootFirst.Result.Bytes}]; both-views-shared=True; selection-focus-preserved=True");

            if (GetForegroundWindow() != foreground || IsWindowVisible(parent))
                throw new InvalidOperationException("The multi-view Shell probe changed foreground ownership or exposed its hidden parent.");
            Console.WriteLine($"ishellview-peer-recursive-size-multiview: real-shell-views=2; shared-root=True; unique-directories={firstPaths.Length}; shared-scan-starts-initial=4; shell-notification-invalidated-roots={invalidatedRoots.Length}; shared-scan-starts-after-all-changes={scanStarts}; update/create/delete/rename/folder-rename/nested-folder-replace=True; stale-old-path-result-rejected=True; stale-replaced-directory-result-rejected=True; peer-row-ids-stable=True; numeric-sort=True; selection-focus-preserved=True; production-scanner=True; product-integration=False; foreground-unchanged=True; parent-visible=False");
        }
        finally
        {
            FreePeerShellRows(firstProjection.Rows);
            FreePeerShellRows(secondProjection.Rows);
            if (secondFolderView != 0) Marshal.Release(secondFolderView);
            if (secondCreated && secondShellView != 0) _ = At<DestroyViewWindow>(secondShellView, 10)(secondShellView);
            if (secondShellView != 0) Marshal.Release(secondShellView);
            if (secondFolder != 0) Marshal.Release(secondFolder);
            if (secondViewPointer != 0) Marshal.Release(secondViewPointer);
            secondHost?.Dispose();
        }
    }

    private static Dictionary<int, ulong> ApplyRecursiveValues(nint listView, IReadOnlyList<PeerShellRow> rows,
        IReadOnlyDictionary<string, RecursiveDirectoryScanResult> results)
    {
        var bytesById = new Dictionary<int, ulong>();
        var indexById = ReadPeerListRowIds(listView).Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.FileSystemPath is null || !results.TryGetValue(Path.GetFullPath(row.FileSystemPath), out var result) ||
                !indexById.TryGetValue(row.Id, out var listIndex)) continue;
            bytesById[row.Id] = result.Bytes;
            // Sorting reorders ListView rows; publish by stable Shell row ID, never by the old projection index.
            SetPeerListCell(listView, listIndex, row.DetailTitles.Length, result.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
        }
        return bytesById;
    }

    private static void VerifySortedSelection(nint listView, IReadOnlyList<PeerShellRow> rows,
        IReadOnlyDictionary<int, ulong> bytesById, IReadOnlyList<int> oldIds, int selectedId)
    {
        var expected = rows.OrderBy(row => bytesById.ContainsKey(row.Id) ? 0 : 1)
            .ThenBy(row => bytesById.TryGetValue(row.Id, out var bytes) ? bytes : ulong.MaxValue)
            .ThenBy(row => row.Id).Select(row => row.Id).ToArray();
        var actual = ReadPeerListRowIds(listView);
        var rowById = rows.ToDictionary(row => row.Id);
        var selectedIndex = Array.IndexOf(actual, selectedId);
        if (!actual.SequenceEqual(expected) || selectedIndex < 0 || GetPeerListItemState(listView, selectedIndex, 0x0003) != 0x0003 ||
            !oldIds.ToHashSet().SetEquals(actual))
            throw new InvalidOperationException("Peer recursive-size sort did not retain the original row IDs and selected/focused Shell row.");
        for (var index = 0; index < actual.Length; index++)
        {
            if (!bytesById.TryGetValue(actual[index], out var bytes)) continue;
            var row = rowById[actual[index]];
            var expectedText = bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";
            if (!string.Equals(GetPeerListCell(listView, index, row.DetailTitles.Length), expectedText, StringComparison.Ordinal))
                throw new InvalidOperationException($"Recursive-size text no longer matches sorted Shell row {row.Name}: expected='{expectedText}'.");
        }
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyRecursiveSizePeerProjection(nint shellView, nint folderView, nint folderPidl, nint shellFolder,
        nint parent, int expectedCount, Action destroyShellView)
    {
        var foreground = GetForegroundWindow();
        var nextId = 0;
        var projection = CapturePeerShellRows(folderView, folderPidl, shellFolder, [], ref nextId);
        try
        {
            if (projection.Rows.Count != expectedCount)
                throw new InvalidOperationException($"Recursive-size peer row count mismatch: expected={expectedCount}, actual={projection.Rows.Count}.");
            var listView = CreatePeerListView(parent, projection.SystemImageList);
            PopulatePeerList(listView, projection.Rows);
            SetPeerListMode(listView, LvsReport);
            if (!MoveWindow(listView, 0, 0, 320, 58, true))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not size the hidden peer ListView for viewport-trigger validation.");
            InsertPeerColumn(listView, projection.Rows[0].DetailTitles.Length, "Recursive size (on demand)");
            var recursiveSizeColumn = projection.Rows[0].DetailTitles.Length;
            var relativePercentageColumn = recursiveSizeColumn + 1;
            InsertPeerColumn(listView, relativePercentageColumn, "Relative percentage");
            for (var index = 0; index < projection.Rows.Count; index++)
            {
                SetPeerListCell(listView, index, projection.Rows[index].DetailTitles.Length, "");
                SetPeerListCell(listView, index, relativePercentageColumn, "");
            }

            var scanPaths = projection.Rows.Where(row => row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
                .Select(row => Path.GetFullPath(row.FileSystemPath!)).ToArray();
            foreach (var expected in new[] { "size-2", "size-10", "size-100" })
                if (!scanPaths.Any(path => string.Equals(Path.GetFileName(path), expected, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"The Shell rows did not contain the recursive-size fixture '{expected}'.");

            var selectedRow = projection.Rows.Single(row => row.Name == "size-10");
            var secondarySelectedRow = projection.Rows.Single(row => row.Name == "size-2");
            var selectedIndex = projection.Rows.IndexOf(selectedRow);
            SetPeerListItemState(listView, selectedIndex, 0x0003, 0x0003);
            VerifyPeerShellCommandMenu(shellView, shellFolder, listView,
                [selectedRow.RelativePidl], ["cut", "copy", "delete", "rename", "properties"]);
            SetPeerListItemState(listView, projection.Rows.IndexOf(secondarySelectedRow), 0x0002, 0x0002);
            VerifyPeerShellCommandMenu(shellView, shellFolder, listView,
                [selectedRow.RelativePidl, secondarySelectedRow.RelativePidl], ["cut", "copy", "delete"]);
            VerifyPeerMultiSelectionCopy(shellFolder, parent, listView, initialIds: ReadPeerListRowIds(listView),
                selectedRowId: selectedRow.Id, secondarySelectedRowId: secondarySelectedRow.Id,
                relativePidls: [selectedRow.RelativePidl, secondarySelectedRow.RelativePidl],
                sourcePaths: [selectedRow.FileSystemPath!, secondarySelectedRow.FileSystemPath!]);
            SetPeerListItemState(listView, projection.Rows.IndexOf(secondarySelectedRow), 0, 0x0002);
            var initialNames = ReadPeerListNames(listView);
            var initialIds = ReadPeerListRowIds(listView);
            var ownerThread = Environment.CurrentManagedThreadId;
            using var cancellation = new CancellationTokenSource();
            CancellationTokenSource? pendingViewportRequest = null;
            Task<(IReadOnlyDictionary<string, ShellListViewRowScanResult> Rows, int WorkerThread, string[] Paths)>? latestViewportRequest = null;
            Task<(IReadOnlyDictionary<string, ShellListViewRowScanResult> Rows, int WorkerThread, string[] Paths)>? supersededViewportRequest = null;
            var startedViewportScans = 0;
            var completedViewportScans = 0;
            var viewportChangeEvents = 0;
            using var obsoleteScanEntered = new ManualResetEventSlim();
            using var releaseObsoleteScan = new ManualResetEventSlim();
            var obsoletePath = Path.GetFullPath(selectedRow.FileSystemPath!);
            Task<(IReadOnlyDictionary<string, ShellListViewRowScanResult> Rows, int WorkerThread, string[] Paths)> ScheduleVisibleScan()
            {
                if (pendingViewportRequest is not null)
                {
                    pendingViewportRequest.Cancel();
                    pendingViewportRequest.Dispose();
                    if (latestViewportRequest is not null)
                        supersededViewportRequest = latestViewportRequest;
                }
                pendingViewportRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                var requestToken = pendingViewportRequest.Token;
                var visibleRows = GetVisibleRecursiveFolderRows(listView, projection.Rows);
                var paths = visibleRows.Select(row => Path.GetFullPath(row.FileSystemPath!)).ToArray();
                if (paths.Length == 0)
                    throw new InvalidOperationException("The current peer ListView viewport contains no recursive-size folders to request.");
                latestViewportRequest = Task.Run(async () =>
                {
                    await Task.Delay(120, requestToken);
                    Interlocked.Increment(ref startedViewportScans);
                    var workerThread = Environment.CurrentManagedThreadId;
                    var rows = HandleBoundRecursiveDirectoryScanner.Scan(paths, requestToken, (path, _) =>
                    {
                        if (!string.Equals(path, obsoletePath, StringComparison.OrdinalIgnoreCase)) return;
                        obsoleteScanEntered.Set();
                        if (!releaseObsoleteScan.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("The in-flight viewport scan was not released by the probe.");
                    });
                    Interlocked.Increment(ref completedViewportScans);
                    return (Rows: rows, WorkerThread: workerThread, Paths: paths);
                }, requestToken);
                return latestViewportRequest;
            }

            using var viewportObserver = new PeerListViewViewportObserver(listView, () =>
            {
                viewportChangeEvents++;
                _ = ScheduleVisibleScan();
            });
            _ = SendMessageW(listView, LvmEnsureVisible, selectedIndex, 0);
            var initialVisibleRows = GetVisibleRecursiveFolderRows(listView, projection.Rows);
            if (!initialVisibleRows.Any(row => row.Id == selectedRow.Id))
                throw new InvalidOperationException("The selected recursive-size fixture was not within the peer ListView viewport.");
            var scanStartDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (!obsoleteScanEntered.IsSet && DateTime.UtcNow < scanStartDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!obsoleteScanEntered.IsSet)
                throw new TimeoutException("The first visible-directory scan did not enter its controlled in-flight gate.");
            var topBeforeLineScroll = GetPeerListTopIndex(listView);
            _ = SendMessageW(listView, 0x0115, 1, 0); // WM_VSCROLL / SB_LINEDOWN
            var lineScrollMovedViewport = GetPeerListTopIndex(listView) != topBeforeLineScroll;
            releaseObsoleteScan.Set();
            var topBeforeWheel = GetPeerListTopIndex(listView);
            _ = SendMessageW(listView, 0x020A, (nint)0x00780000, 0); // WM_MOUSEWHEEL / +WHEEL_DELTA
            var wheelMovedViewport = GetPeerListTopIndex(listView) != topBeforeWheel;
            Thread.Sleep(20);
            var finalVisibleIndex = projection.Rows.IndexOf(projection.Rows.Single(row => row.Name == "size-100"));
            _ = SendMessageW(listView, LvmEnsureVisible, finalVisibleIndex, 0);
            var finalVisibleRows = GetVisibleRecursiveFolderRows(listView, projection.Rows);
            if (!lineScrollMovedViewport || !finalVisibleRows.Any(row => row.Name == "size-100") ||
                finalVisibleRows.Any(row => row.Id == selectedRow.Id) || viewportChangeEvents < 2)
                throw new InvalidOperationException("The native line-scroll message did not move the viewport and replace the pending size-10 request with size-100.");
            var scan = latestViewportRequest ?? throw new InvalidOperationException("The peer ListView viewport observer did not schedule a recursive-size request.");
            while (!scan.IsCompleted)
            {
                Application.DoEvents();
                if (!ReadPeerListNames(listView).SequenceEqual(initialNames, StringComparer.OrdinalIgnoreCase) ||
                    !ReadPeerListRowIds(listView).SequenceEqual(initialIds))
                    throw new InvalidOperationException("The peer list changed while the requested recursive value was loading.");
                Thread.Sleep(10);
            }
            try
            {
                _ = (supersededViewportRequest ?? throw new InvalidOperationException("The viewport observer did not supersede its first request.")).GetAwaiter().GetResult();
                throw new InvalidOperationException("A superseded viewport request completed instead of being canceled before its scan began.");
            }
            catch (OperationCanceledException) { }
            var completed = scan.GetAwaiter().GetResult();
            if (completed.WorkerThread == ownerThread || Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Recursive sizes were not calculated off the Shell owner thread.");

            var sizes = completed.Rows.ToDictionary(pair => Path.GetFullPath(pair.Key), pair => pair.Value.Summary.Bytes,
                StringComparer.OrdinalIgnoreCase);
            if (!sizes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(completed.Paths) ||
                !completed.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(finalVisibleRows.Select(row => Path.GetFullPath(row.FileSystemPath!))) ||
                startedViewportScans != 2 || completedViewportScans != 1)
                throw new InvalidOperationException("Viewport cancellation did not discard the in-flight old scan and complete only the final visible-row scan.");
            var requestedPaths = completed.Paths;
            var recursiveSizeCache = new Dictionary<string, ulong>(sizes, StringComparer.OrdinalIgnoreCase);
            var bytesById = new Dictionary<int, ulong>();
            for (var index = 0; index < projection.Rows.Count; index++)
            {
                var row = projection.Rows[index];
                if (row.FileSystemPath is not null && sizes.TryGetValue(Path.GetFullPath(row.FileSystemPath), out var bytes))
                {
                    bytesById[row.Id] = bytes;
                    SetPeerListCell(listView, index, row.DetailTitles.Length,
                        bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
                }
            }
            var initialIndexById = initialIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
            var requestedIds = finalVisibleRows.Select(row => row.Id).ToHashSet();
            var otherFolderRowsRemainPending = projection.Rows.Where(row => !requestedIds.Contains(row.Id) &&
                    row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
                .All(row => string.IsNullOrEmpty(GetPeerListCell(listView, initialIndexById[row.Id], row.DetailTitles.Length)));
            var percentagesBlankWhileFolderSizesPending = projection.Rows.Select((row, index) =>
                    string.IsNullOrEmpty(GetPeerListCell(listView, index, relativePercentageColumn)))
                .All(isBlank => isBlank);
            if (bytesById.GetValueOrDefault(finalVisibleRows.Single(row => row.Name == "size-100").Id) != 100 ||
                !otherFolderRowsRemainPending || !percentagesBlankWhileFolderSizesPending ||
                !string.IsNullOrEmpty(GetPeerListCell(listView, initialIndexById[selectedRow.Id], selectedRow.DetailTitles.Length)))
                throw new InvalidOperationException("The viewport on-demand scan populated a non-visible recursive-size row or showed percentages before the sibling total was known.");

            var remainingPaths = scanPaths.Where(path => !requestedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray();
            var rowByPath = projection.Rows
                .Where(row => row.FileSystemPath is not null)
                .ToDictionary(row => Path.GetFullPath(row.FileSystemPath!), row => row,
                    StringComparer.OrdinalIgnoreCase);
            var ownerDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var ownerContext = new System.Windows.Threading.DispatcherSynchronizationContext(ownerDispatcher);
            using var remainingScanCoordinator = new DirectoryTreeScanCoordinator();
            using var cancelRemainingScanRegistration = cancellation.Token.Register(remainingScanCoordinator.CancelCurrent);
            var incrementalUpdatesApplied = 0;
            var firstIncrementalValueArrived = false;
            IReadOnlyDictionary<string, ShellListViewRowScanResult>? remainingResults = null;
            var remainingScan = remainingScanCoordinator.RunLatestIncrementalAsync<
                (string Path, ShellListViewRowScanResult Result),
                IReadOnlyDictionary<string, ShellListViewRowScanResult>>(
                (scanToken, publish) => HandleBoundRecursiveDirectoryScanner.Scan(remainingPaths, scanToken,
                    completedDirectory: (path, result) => publish((path, result))),
                ownerContext,
                update =>
                {
                    var fullPath = Path.GetFullPath(update.Path);
                    if (!rowByPath.TryGetValue(fullPath, out var row) || row.FileSystemIdentity != update.Result.Identity ||
                        ShellListViewRowIdentity.GetFileSystemIdentity(fullPath) != update.Result.Identity)
                        throw new InvalidOperationException("An incremental recursive-size result no longer matches its captured Shell row identity.");
                    // Shell sorting can move ListView rows; resolve the stable row ID for every owner-thread update.
                    var liveIndexById = ReadPeerListRowIds(listView).Select((id, index) => (id, index))
                        .ToDictionary(item => item.id, item => item.index);
                    if (!liveIndexById.TryGetValue(row.Id, out var liveIndex))
                        throw new InvalidOperationException("An incremental recursive-size result no longer maps to a live peer row ID.");
                    var liveIds = liveIndexById.OrderBy(item => item.Value).Select(item => item.Key);
                    if (!liveIds.SequenceEqual(initialIds) ||
                        GetPeerListItemState(listView, liveIndexById[selectedRow.Id], 0x0003) != 0x0003)
                        throw new InvalidOperationException("Incremental cell updates changed the peer row order or selected/focused Shell row.");

                    sizes[fullPath] = update.Result.Summary.Bytes;
                    recursiveSizeCache[fullPath] = update.Result.Summary.Bytes;
                    bytesById[row.Id] = update.Result.Summary.Bytes;
                    SetPeerListCell(listView, liveIndex, row.DetailTitles.Length,
                        update.Result.Summary.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
                    incrementalUpdatesApplied++;
                    firstIncrementalValueArrived = true;
                },
                results =>
                {
                    if (!results.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(remainingPaths))
                        throw new InvalidOperationException("The final recursive-size scan map did not include every independently requested directory.");
                    remainingResults = results;
                });
            var remainingFrame = new System.Windows.Threading.DispatcherFrame();
            var remainingPoll = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            remainingPoll.Tick += (_, _) =>
            {
                if (remainingScan.IsCompleted) remainingFrame.Continue = false;
            };
            remainingPoll.Start();
            try { System.Windows.Threading.Dispatcher.PushFrame(remainingFrame); }
            finally { remainingPoll.Stop(); }
            if (!remainingScan.GetAwaiter().GetResult() || remainingResults is null || !firstIncrementalValueArrived ||
                incrementalUpdatesApplied != remainingPaths.Length)
                throw new InvalidOperationException("The latest remaining-directory scan did not incrementally publish every row on its owner dispatcher.");

            foreach (var pair in remainingResults)
            {
                var fullPath = Path.GetFullPath(pair.Key);
                if (sizes.GetValueOrDefault(fullPath) != pair.Value.Summary.Bytes)
                    throw new InvalidOperationException("An incrementally rendered row differed from the final handle-bound scan result.");
            }
            if (bytesById[selectedRow.Id] != 10 || bytesById[projection.Rows.Single(row => row.Name == "size-2").Id] != 2 ||
                bytesById[projection.Rows.Single(row => row.Name == "size-100").Id] != 100)
                throw new InvalidOperationException("The recursive-size peer values did not match the fixture bytes.");

            var denominatorBeforeNotification = CalculatePeerDirectoryTotalBytes(projection.Rows, bytesById);
            PopulatePeerRelativePercentages(listView, projection.Rows, bytesById, relativePercentageColumn, denominatorBeforeNotification);
            var relativePercentagesBeforeNotification = projection.Rows.Select((row, index) =>
                (row.Name, Value: GetPeerListCell(listView, index, relativePercentageColumn)))
                .ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase);
            if (denominatorBeforeNotification == 0)
                throw new InvalidOperationException("The recursive-size fixture unexpectedly has a zero directory total.");
            var expectedSize10Percent = (100m * bytesById[selectedRow.Id] / denominatorBeforeNotification)
                .ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "%";
            if (!string.Equals(relativePercentagesBeforeNotification[selectedRow.Name], expectedSize10Percent, StringComparison.Ordinal))
                throw new InvalidOperationException("The completed sibling scan did not produce the expected relative-size percentage.");

            SortPeerListByRecursiveSize(listView, bytesById);

            var sortedRows = ReadPeerListNames(listView);
            var sortedIds = ReadPeerListRowIds(listView);
            var expectedOrder = projection.Rows.OrderBy(row => bytesById.ContainsKey(row.Id) ? 0 : 1)
                .ThenBy(row => bytesById.TryGetValue(row.Id, out var bytes) ? bytes : ulong.MaxValue)
                .ThenBy(row => row.Id).ToArray();
            var selectedAfterSort = Array.IndexOf(sortedIds, selectedRow.Id);
            var selectedState = selectedAfterSort < 0 ? 0 : GetPeerListItemState(listView, selectedAfterSort, 0x0003);
            if (!sortedIds.SequenceEqual(expectedOrder.Select(row => row.Id)) || selectedState != 0x0003)
                throw new InvalidOperationException($"Recursive-size sort order or selected row identity was lost: ids=[{string.Join(',', sortedIds)}], expected=[{string.Join(',', expectedOrder.Select(row => row.Id))}], selected-state=0x{selectedState:X}.");
            pendingViewportRequest?.Dispose();
            Console.WriteLine($"ishellview-peer-recursive-size: shell-rows={projection.Rows.Count}; wm-vscroll-moved-viewport={lineScrollMovedViewport}; wm-mousewheel-moved-viewport={wheelMovedViewport}; native-viewport-events={viewportChangeEvents}; obsolete-inflight-scan-canceled={supersededViewportRequest!.IsCanceled}; scans-started={startedViewportScans}; scans-completed={completedViewportScans}; viewport-updates-coalesced={viewportChangeEvents - completedViewportScans}; visible-folder-rows-requested={completed.Paths.Length}; remaining-row-values-published-incrementally={incrementalUpdatesApplied}; final-map-reconciled=True; viewport-top-index={GetPeerListTopIndex(listView)}; viewport-count-per-page={GetPeerListCountPerPage(listView)}; only-final-visible-rows-published=True; other-rows-pending={otherFolderRowsRemainPending}; worker-thread={completed.WorkerThread}; owner-thread={ownerThread}; rows-retained-during-scan=True; bytes=[size-2:2,size-10:10,size-100:100]; numeric-order=[{string.Join(',', sortedRows)}]; selected-focused-id={selectedRow.Id}; selection-focus-preserved=True; foreground-unchanged={GetForegroundWindow() == foreground}; product-view-changed=False");

            var changedDirectory = Path.GetFullPath(projection.Rows.Single(row => row.Name == "size-10").FileSystemPath!);
            var changedPayload = Path.Combine(changedDirectory, "payload.bin");
            using var changeWatch = new ShellChangeReconciliationProbe.ChangeWindow();
            changeWatch.Register(folderPidl);
            var eventIndex = changeWatch.Events.Count;
            File.WriteAllBytes(changedPayload, new byte[12]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            File.WriteAllBytes(changedPayload, new byte[16]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            File.WriteAllBytes(changedPayload, new byte[20]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            SHChangeNotify(ShellChangeUpdatedDirectory, ShellChangePathUnicode, changedDirectory, null);
            if (!changeWatch.WaitForAnySince(eventIndex, [ShellChangeUpdatedItem, ShellChangeUpdatedDirectory], TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The Shell change window did not receive the recursive-size fixture update notification.");
            var lastNotificationCount = -1;
            var lastNotificationAt = Environment.TickCount64;
            var notificationDeadline = lastNotificationAt + 5000;
            while (Environment.TickCount64 < notificationDeadline &&
                   Environment.TickCount64 - lastNotificationAt < 150)
            {
                Application.DoEvents();
                var observedCount = changeWatch.ChangeEvents.Count;
                if (observedCount != lastNotificationCount)
                {
                    lastNotificationCount = observedCount;
                    lastNotificationAt = Environment.TickCount64;
                }
                Thread.Sleep(10);
            }
            var updateEvents = changeWatch.ChangeEvents.Skip(eventIndex)
                .Where(change => change.EventId is ShellChangeUpdatedItem or ShellChangeUpdatedDirectory).ToArray();
            if (updateEvents.Length < 2)
                throw new InvalidOperationException($"Expected duplicate Shell update notifications for the burst; observed={updateEvents.Length}.");
            var dirtyDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in updateEvents)
            {
                if (change.FirstPath is not { } changedPath) continue;
                var fullChangedPath = Path.GetFullPath(changedPath);
                if (string.Equals(fullChangedPath, changedDirectory, StringComparison.OrdinalIgnoreCase) ||
                    fullChangedPath.StartsWith(changedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    dirtyDirectories.Add(changedDirectory);
            }
            if (dirtyDirectories.Count != 1)
                throw new InvalidOperationException($"The update burst did not coalesce to the changed directory: paths=[{string.Join('|', updateEvents.Select(change => change.FirstPath))}].");
            WaitForPeerShellItem(folderView, changedDirectory, shouldExist: true, expectedCount);

            var refreshedProjection = CapturePeerShellRows(folderView, folderPidl, shellFolder, projection.Rows, ref nextId);
            if (refreshedProjection.Rows.Count != expectedCount ||
                !refreshedProjection.Rows.Select(row => row.Id).ToHashSet().SetEquals(sortedIds))
                throw new InvalidOperationException("Shell re-enumeration changed peer identities during recursive-size invalidation.");
            projection = refreshedProjection;

            var cacheEntryInvalidated = recursiveSizeCache.Remove(changedDirectory);
            if (!cacheEntryInvalidated || sizes[changedDirectory] != 10)
                throw new InvalidOperationException("The Shell update did not invalidate the stale recursive-size snapshot.");
            using var refreshCancellation = new CancellationTokenSource();
            var refreshScan = Task.Run(() =>
            {
                var workerThread = Environment.CurrentManagedThreadId;
                Thread.Sleep(150); // Keep the coalesced invalidation pending while the peer list remains unchanged.
                return (Rows: HandleBoundRecursiveDirectoryScanner.Scan(dirtyDirectories, refreshCancellation.Token), WorkerThread: workerThread);
            }, refreshCancellation.Token);
            while (!refreshScan.IsCompleted)
            {
                Application.DoEvents();
                if (!ReadPeerListRowIds(listView).SequenceEqual(sortedIds) ||
                    GetPeerListItemState(listView, selectedAfterSort, 0x0003) != 0x0003)
                    throw new InvalidOperationException("The peer rows or selection changed while the coalesced recursive-size refresh was pending.");
                Thread.Sleep(10);
            }
            var refreshedScan = refreshScan.GetAwaiter().GetResult();
            if (refreshedScan.WorkerThread == ownerThread || !refreshedScan.Rows.TryGetValue(changedDirectory, out var changedSummary) ||
                changedSummary.Summary.Bytes != 20)
                throw new InvalidOperationException("The invalidated recursive-size row did not rescan to the updated value.");

            long scanGeneration = 0;
            var scanStartedGeneration = Interlocked.Read(ref scanGeneration);
            using var scanOpened = new ManualResetEventSlim();
            using var continueScan = new ManualResetEventSlim();
            var supersededScan = Task.Run(() =>
            {
                var workerThread = Environment.CurrentManagedThreadId;
                var rows = HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory], refreshCancellation.Token,
                    (_, _) =>
                    {
                        scanOpened.Set();
                        if (!continueScan.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("The in-flight recursive scan was not released by the notification race probe.");
                    });
                return (Rows: rows, WorkerThread: workerThread);
            }, refreshCancellation.Token);
            var scanOpenDeadline = Environment.TickCount64 + 5000;
            while (!scanOpened.IsSet && Environment.TickCount64 < scanOpenDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!scanOpened.IsSet)
            {
                continueScan.Set();
                throw new TimeoutException("The worker did not open the recursive-size directory before the notification race.");
            }
            if (!ReadPeerListRowIds(listView).SequenceEqual(sortedIds) ||
                GetPeerListItemState(listView, selectedAfterSort, 0x0003) != 0x0003)
                throw new InvalidOperationException("The peer selection changed before the in-flight notification race.");

            var raceEventIndex = changeWatch.ChangeEvents.Count;
            File.WriteAllBytes(changedPayload, new byte[30]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            SHChangeNotify(ShellChangeUpdatedDirectory, ShellChangePathUnicode, changedDirectory, null);
            if (!changeWatch.WaitForAnySince(raceEventIndex, [ShellChangeUpdatedItem, ShellChangeUpdatedDirectory], TimeSpan.FromSeconds(5)))
            {
                continueScan.Set();
                throw new TimeoutException("The Shell change window did not receive the update that supersedes the in-flight recursive scan.");
            }
            var currentScanGeneration = Interlocked.Increment(ref scanGeneration);
            continueScan.Set();
            var supersededResult = supersededScan.GetAwaiter().GetResult();
            var supersededValueDiscarded = currentScanGeneration != scanStartedGeneration &&
                supersededResult.WorkerThread != ownerThread;
            if (!supersededValueDiscarded || !supersededResult.Rows.TryGetValue(changedDirectory, out var supersededSummary) ||
                supersededSummary.Summary.Bytes != 30)
                throw new InvalidOperationException("The in-flight result was not retired after a newer Shell update arrived.");

            using var finalScanCancellation = new CancellationTokenSource();
            var finalScan = Task.Run(() => HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory], finalScanCancellation.Token));
            while (!finalScan.IsCompleted)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            var finalScanResult = finalScan.GetAwaiter().GetResult();
            if (!finalScanResult.TryGetValue(changedDirectory, out var finalSummary) || finalSummary.Summary.Bytes != 30)
                throw new InvalidOperationException("The follow-up recursive-size scan did not read the latest value after an in-flight update.");
            recursiveSizeCache[changedDirectory] = finalSummary.Summary.Bytes;

            var retiredScanGeneration = Interlocked.Read(ref scanGeneration);
            using var retiredScanCancellation = new CancellationTokenSource();
            using var retiredScanOpened = new ManualResetEventSlim();
            using var retireScan = new ManualResetEventSlim();
            var retiredScan = Task.Run(() => HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory],
                retiredScanCancellation.Token, (_, _) =>
                {
                    retiredScanOpened.Set();
                    if (!retireScan.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The retired recursive scan was not released by the cancellation probe.");
                }));
            var retiredScanDeadline = Environment.TickCount64 + 5000;
            while (!retiredScanOpened.IsSet && Environment.TickCount64 < retiredScanDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!retiredScanOpened.IsSet)
            {
                retireScan.Set();
                throw new TimeoutException("The worker did not open the directory before the retirement cancellation.");
            }
            _ = Interlocked.Increment(ref scanGeneration);
            destroyShellView();
            retiredScanCancellation.Cancel();
            retireScan.Set();
            var retiredFailurePreservedValue = false;
            try { _ = retiredScan.GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                retiredFailurePreservedValue = Interlocked.Read(ref scanGeneration) != retiredScanGeneration &&
                    recursiveSizeCache.TryGetValue(changedDirectory, out var cachedBytes) && cachedBytes == 30;
            }
            if (!retiredFailurePreservedValue || !ReadPeerListRowIds(listView).SequenceEqual(sortedIds) ||
                GetPeerListItemState(listView, selectedAfterSort, 0x0003) != 0x0003)
                throw new InvalidOperationException("A retired recursive scan failure cleared the newer cached value or disturbed the peer selection.");

            PopulatePeerList(listView, projection.Rows);
            InsertPeerColumn(listView, projection.Rows[0].DetailTitles.Length, "Recursive size (on demand)");
            InsertPeerColumn(listView, relativePercentageColumn, "Relative percentage");
            var updatedBytesById = new Dictionary<int, ulong>();
            for (var index = 0; index < projection.Rows.Count; index++)
            {
                var row = projection.Rows[index];
                if (row.FileSystemPath is not null && recursiveSizeCache.TryGetValue(Path.GetFullPath(row.FileSystemPath), out var bytes))
                {
                    updatedBytesById[row.Id] = bytes;
                    SetPeerListCell(listView, index, row.DetailTitles.Length,
                        bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
                }
            }
            var denominatorAfterNotification = CalculatePeerDirectoryTotalBytes(projection.Rows, updatedBytesById);
            PopulatePeerRelativePercentages(listView, projection.Rows, updatedBytesById, relativePercentageColumn, denominatorAfterNotification);
            var updatedSize10RowIndex = projection.Rows.FindIndex(row => row.Id == selectedRow.Id);
            var updatedSize10Percent = GetPeerListCell(listView, updatedSize10RowIndex, relativePercentageColumn);
            var size100Row = projection.Rows.Single(row => row.Name == "size-100");
            var updatedSize100RowIndex = projection.Rows.FindIndex(row => row.Id == size100Row.Id);
            var updatedSize100Percent = GetPeerListCell(listView, updatedSize100RowIndex, relativePercentageColumn);
            if (updatedBytesById[selectedRow.Id] != 30 || denominatorAfterNotification == denominatorBeforeNotification ||
                string.Equals(updatedSize10Percent, relativePercentagesBeforeNotification[selectedRow.Name], StringComparison.Ordinal) ||
                string.Equals(updatedSize100Percent, relativePercentagesBeforeNotification[size100Row.Name], StringComparison.Ordinal))
                throw new InvalidOperationException("The recursive-size notification did not update the sibling percentage denominator and row values.");
            Console.WriteLine($"ishellview-peer-relative-percentage: blank-while-sibling-sizes-pending={percentagesBlankWhileFolderSizesPending}; row={selectedRow.Name}; initial-bytes={bytesById[selectedRow.Id]}; initial-directory-total={denominatorBeforeNotification}; initial-percent={relativePercentagesBeforeNotification[selectedRow.Name]}; updated-bytes={updatedBytesById[selectedRow.Id]}; updated-directory-total={denominatorAfterNotification}; updated-percent={updatedSize10Percent}; sibling-values-recomputed=True");
            var refreshedSelectedRow = projection.Rows.Single(row => row.Id == selectedRow.Id);
            SetPeerListItemState(listView, projection.Rows.IndexOf(refreshedSelectedRow), 0x0003, 0x0003);
            SortPeerListByRecursiveSize(listView, updatedBytesById);
            var refreshedNames = ReadPeerListNames(listView);
            var refreshedIds = ReadPeerListRowIds(listView);
            var expectedRefreshedOrder = projection.Rows.OrderBy(row => updatedBytesById.ContainsKey(row.Id) ? 0 : 1)
                .ThenBy(row => updatedBytesById.TryGetValue(row.Id, out var bytes) ? bytes : ulong.MaxValue)
                .ThenBy(row => row.Id).ToArray();
            var refreshedSelectedIndex = Array.IndexOf(refreshedIds, selectedRow.Id);
            if (updatedBytesById[selectedRow.Id] != 30 || !refreshedIds.SequenceEqual(expectedRefreshedOrder.Select(row => row.Id)) ||
                refreshedSelectedIndex < 0 || GetPeerListItemState(listView, refreshedSelectedIndex, 0x0003) != 0x0003 ||
                !refreshedNames.SequenceEqual(expectedRefreshedOrder.Select(row => row.Name), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The updated recursive-size value did not flow through peer sort/reselection: names=[{string.Join(',', refreshedNames)}], value={updatedBytesById.GetValueOrDefault(selectedRow.Id)}.");
            Console.WriteLine($"ishellview-peer-recursive-size-notification: shell-update-events={updateEvents.Length}; changed-directories-coalesced={dirtyDirectories.Count}; peer-rows-retained-during-worker-rescan=True; superseded-inflight-result-discarded={supersededValueDiscarded}; shell-view-destroyed-during-scan=True; retired-scan-canceled=True; newer-cache-preserved={retiredFailurePreservedValue}; follow-up-bytes={updatedBytesById[selectedRow.Id]}; peer-rows-reenumerated=True; row-ids-preserved=True; stale-cache-invalidated={cacheEntryInvalidated}; rescan-worker={refreshedScan.WorkerThread}; numeric-order=[{string.Join(',', refreshedNames)}]; selected-focused-id={selectedRow.Id}; selection-focus-preserved=True; foreground-unchanged={GetForegroundWindow() == foreground}");
        }
        finally
        {
            FreePeerShellRows(projection.Rows);
        }
    }

    private static void SetPeerListItemState(nint listView, int index, uint state, uint mask)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            Marshal.StructureToPtr(new NativeListViewItem { Item = index, State = state, StateMask = mask }, pointer, false);
            _ = SendMessageW(listView, LvmSetItemState, index, pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static uint GetPeerListItemState(nint listView, int index, uint mask) =>
        unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)mask));

    private static ulong CalculatePeerDirectoryTotalBytes(IReadOnlyList<PeerShellRow> rows,
        IReadOnlyDictionary<int, ulong> directoryBytesById)
    {
        ulong total = 0;
        foreach (var row in rows)
        {
            if (row.FileSystemPath is null) continue;
            var bytes = Directory.Exists(row.FileSystemPath)
                ? directoryBytesById.TryGetValue(row.Id, out var directoryBytes)
                    ? directoryBytes
                    : throw new InvalidOperationException($"The directory '{row.Name}' has no completed recursive size for the percentage denominator.")
                : checked((ulong)new FileInfo(row.FileSystemPath).Length);
            total = checked(total + bytes);
        }
        return total;
    }

    private static void PopulatePeerRelativePercentages(nint listView, IReadOnlyList<PeerShellRow> rows,
        IReadOnlyDictionary<int, ulong> directoryBytesById, int column, ulong directoryBytes)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.FileSystemPath is null) continue;
            var itemBytes = Directory.Exists(row.FileSystemPath)
                ? directoryBytesById.GetValueOrDefault(row.Id)
                : checked((ulong)new FileInfo(row.FileSystemPath).Length);
            var percent = RecursiveSizePercentage.Calculate(itemBytes, directoryBytes);
            var text = percent is { } value
                ? value.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "%"
                : string.Empty;
            SetPeerListCell(listView, index, column, text);
        }
    }

    private static void SortPeerListByRecursiveSize(nint listView, Dictionary<int, ulong> bytesById)
    {
        var sortState = GCHandle.Alloc(bytesById);
        NativeListViewCompare compare = (first, second, data) =>
        {
            var values = (Dictionary<int, ulong>)GCHandle.FromIntPtr(data).Target!;
            var firstId = unchecked((int)first);
            var secondId = unchecked((int)second);
            var hasFirst = values.TryGetValue(firstId, out var firstBytes);
            var hasSecond = values.TryGetValue(secondId, out var secondBytes);
            if (hasFirst != hasSecond) return hasFirst ? -1 : 1;
            var sizeOrder = hasFirst ? firstBytes.CompareTo(secondBytes) : 0;
            return sizeOrder != 0 ? sizeOrder : firstId.CompareTo(secondId);
        };
        try
        {
            if (SendMessageW(listView, LvmSortItems, GCHandle.ToIntPtr(sortState),
                Marshal.GetFunctionPointerForDelegate(compare)).ToInt32() == 0)
                throw new InvalidOperationException("The peer ListView rejected recursive-size sorting.");
        }
        finally { sortState.Free(); GC.KeepAlive(compare); }
    }

    private static void SetPeerListMode(nint listView, long mode)
    {
        var style = GetWindowLongPtrW(listView, GwlStyle).ToInt64();
        _ = SetWindowLongPtrW(listView, GwlStyle, (nint)((style & ~LvsTypeMask) | mode));
    }

    private static int GetPeerListTopIndex(nint listView) => SendMessageW(listView, LvmGetTopIndex, 0, 0).ToInt32();

    private static int GetPeerListCountPerPage(nint listView) => SendMessageW(listView, LvmGetCountPerPage, 0, 0).ToInt32();

    private static PeerShellRow[] GetVisibleRecursiveFolderRows(nint listView, IReadOnlyList<PeerShellRow> rows)
    {
        var topIndex = GetPeerListTopIndex(listView);
        var countPerPage = GetPeerListCountPerPage(listView);
        if (topIndex < 0 || countPerPage <= 0 || topIndex >= rows.Count)
            throw new InvalidOperationException($"The peer ListView returned an invalid viewport: top={topIndex}; count-per-page={countPerPage}; rows={rows.Count}.");
        var end = Math.Min(rows.Count, topIndex + countPerPage);
        return rows.Skip(topIndex).Take(end - topIndex)
            .Where(row => row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
            .ToArray();
    }

    private sealed class PeerListViewViewportObserver : IDisposable
    {
        private const uint WmVScroll = 0x0115;
        private const uint WmMouseWheel = 0x020A;
        private const uint WmKeyDown = 0x0100;
        private readonly nint _listView;
        private readonly Action _viewportChanged;
        private readonly SubclassProc _callback;
        private int _topIndex;
        private int _countPerPage;
        private bool _disposed;

        internal PeerListViewViewportObserver(nint listView, Action viewportChanged)
        {
            _listView = listView;
            _viewportChanged = viewportChanged;
            _topIndex = GetPeerListTopIndex(listView);
            _countPerPage = GetPeerListCountPerPage(listView);
            _callback = OnMessage;
            if (!SetWindowSubclass(listView, _callback, 1, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not observe peer ListView viewport changes.");
        }

        private nint OnMessage(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData)
        {
            var result = DefSubclassProc(window, message, wParam, lParam);
            if (message is LvmEnsureVisible or WmVScroll or WmMouseWheel or WmKeyDown)
            {
                var topIndex = GetPeerListTopIndex(_listView);
                var countPerPage = GetPeerListCountPerPage(_listView);
                if (topIndex != _topIndex || countPerPage != _countPerPage)
                {
                    _topIndex = topIndex;
                    _countPerPage = countPerPage;
                    _viewportChanged();
                }
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = RemoveWindowSubclass(_listView, _callback, 1);
            GC.KeepAlive(_callback);
        }
    }

    private static void PopulatePeerList(nint listView, IReadOnlyList<PeerShellRow> rows, bool groupByType = false)
    {
        _ = SendMessageW(listView, LvmDeleteAllItems, 0, 0);
        _ = SendMessageW(listView, LvmEnableGroupView, 0, 0);
        _ = SendMessageW(listView, LvmRemoveAllGroups, 0, 0);
        RemovePeerColumns(listView);
        if (rows.Count > 0)
            for (var columnIndex = 0; columnIndex < rows[0].DetailTitles.Length; columnIndex++)
                InsertPeerColumn(listView, columnIndex, rows[0].DetailTitles[columnIndex]);
        var groupIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groupInsertResults = new List<int>();
        if (groupByType)
        {
            foreach (var title in rows.Select(row => row.TypeName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("The Shell did not provide a type label for grouping.");
                var header = Marshal.StringToHGlobalUni(title);
                var groupPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewGroup>());
                try
                {
                    var groupId = groupIds.Count + 1;
                    var group = new NativeListViewGroup
                    {
                        Size = (uint)Marshal.SizeOf<NativeListViewGroup>(), Mask = 0x0001 | 0x0010,
                        Header = header, HeaderCapacity = title.Length, GroupId = groupId
                    };
                    Marshal.StructureToPtr(group, groupPointer, false);
                    var insertResult = SendMessageW(listView, LvmInsertGroup, groupIds.Count, groupPointer).ToInt32();
                    groupInsertResults.Add(insertResult);
                    if (insertResult < 0)
                        throw new InvalidOperationException($"Could not insert the Shell type group '{title}'.");
                    groupIds.Add(title, groupId);
                }
                finally { Marshal.FreeHGlobal(groupPointer); Marshal.FreeHGlobal(header); }
            }
            var enableResult = SendMessageW(listView, LvmEnableGroupView, 1, 0).ToInt32();
            Console.WriteLine($"peer-list-groups: inserted=[{string.Join(',', groupInsertResults)}]; enable-result={enableResult}; count-after-enable={SendMessageW(listView, LvmGetGroupCount, 0, 0)}; enabled-after-enable={SendMessageW(listView, LvmIsGroupViewEnabled, 0, 0)}");
            if (enableResult < 0)
                throw new InvalidOperationException("The peer ListView rejected group view mode.");
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var firstValue = row.DetailValues.Length > 0 ? row.DetailValues[0] : row.Name;
            var text = Marshal.StringToHGlobalUni(firstValue);
            var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
            try
            {
                var item = new NativeListViewItem { Mask = 0x0001 | 0x0002 | 0x0004 | (groupByType ? 0x0100u : 0), Item = index, SubItem = 0, Text = text,
                    TextCapacity = firstValue.Length + 1, Image = row.SystemImageIndex, Parameter = row.Id,
                    GroupId = groupByType ? groupIds[row.TypeName] : 0 };
                Marshal.StructureToPtr(item, itemPointer, false);
                if (SendMessageW(listView, LvmInsertItemW, 0, itemPointer).ToInt32() != index)
                    throw new InvalidOperationException($"Could not populate peer ListView row {index}.");
            }
            finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(text); }
            for (var columnIndex = 1; columnIndex < row.DetailValues.Length; columnIndex++)
                SetPeerListCell(listView, index, columnIndex, row.DetailValues[columnIndex]);
        }
    }

    private static void RemovePeerColumns(nint listView)
    {
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        // Hidden empty report controls may materialize their header only after the first column is inserted.
        if (header == 0) return;
        while (SendMessageW(header, 0x1200, 0, 0).ToInt32() > 0) // HDM_GETITEMCOUNT
            if (SendMessageW(listView, 0x101C, 0, 0) == 0) // LVM_DELETECOLUMN
                throw new InvalidOperationException("Could not reset peer ListView columns.");
    }

    private static void InsertPeerColumn(nint listView, int index, string titleText)
    {
        var title = Marshal.StringToHGlobalUni(titleText);
        var columnPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewColumn>());
        try
        {
            var column = new NativeListViewColumn { Mask = 0x0001 | 0x0002 | 0x0004, Width = index == 0 ? 220 : 150, Text = title, TextCapacity = titleText.Length + 1, SubItem = index };
            Marshal.StructureToPtr(column, columnPointer, false);
            if (SendMessageW(listView, LvmInsertColumnW, index, columnPointer).ToInt32() < 0)
                throw new InvalidOperationException($"Could not add peer ListView column '{titleText}'.");
        }
        finally { Marshal.FreeHGlobal(columnPointer); Marshal.FreeHGlobal(title); }
    }

    private static void SetPeerListCell(nint listView, int row, int column, string value)
    {
        var text = Marshal.StringToHGlobalUni(value);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            Marshal.StructureToPtr(new NativeListViewItem { Item = row, SubItem = column, Text = text, TextCapacity = value.Length + 1 }, itemPointer, false);
            if (SendMessageW(listView, LvmSetItemTextW, row, itemPointer) == 0)
                throw new InvalidOperationException($"Could not populate peer ListView cell ({row}, {column}).");
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(text); }
    }

    private static string GetPeerListCell(nint listView, int row, int column)
    {
        var buffer = Marshal.AllocHGlobal(1024);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            var item = new NativeListViewItem { Item = row, SubItem = column, Text = buffer, TextCapacity = 512 };
            Marshal.StructureToPtr(item, itemPointer, false);
            _ = SendMessageW(listView, LvmGetItemTextW, row, itemPointer);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(buffer); }
    }

    private static void VerifyPeerShellCommandMenu(nint shellView, nint shellFolder, nint owner, IReadOnlyList<nint> relativePidls,
        IReadOnlyList<string> requiredVerbs)
    {
        var children = Marshal.AllocHGlobal(IntPtr.Size * relativePidls.Count);
        nint contextMenu = 0;
        object? contextObject = null;
        try
        {
            for (var index = 0; index < relativePidls.Count; index++)
                Marshal.WriteIntPtr(children, index * IntPtr.Size, relativePidls[index]);
            var menuId = ContextMenuId;
            Check(At<GetUIObjectOf>(shellFolder, 10)(shellFolder, owner, (uint)relativePidls.Count, children,
                in menuId, 0, out contextMenu));
            if (contextMenu == 0) throw new InvalidOperationException("The selected Shell row did not provide an IContextMenu.");
            contextObject = Marshal.GetObjectForIUnknown(contextMenu);
            VerifyPeerContextMenuPopup(contextMenu, owner, relativePidls.Count);
        }
        finally
        {
            if (contextMenu != 0) Marshal.Release(contextMenu);
            Marshal.FreeHGlobal(children);
        }

        using var commands = new NativeShellCommandMenu(contextObject!, owner);
        var enabledVerbs = requiredVerbs.Where(commands.IsEnabled).ToArray();
        if (enabledVerbs.Length != requiredVerbs.Count)
            throw new InvalidOperationException($"The product Shell command adapter did not expose all selected-row verbs: enabled=[{string.Join(',', enabledVerbs)}].");
        Console.WriteLine($"ishellview-peer-selected-context-menu: selected-count={relativePidls.Count}; source=IShellFolder.GetUIObjectOf; adapter=NativeShellCommandMenu; enabled=[{string.Join(',', enabledVerbs)}]; popup-tracked-and-cancelled=True; command-invoked=False");

        var commandStates = ShellFileCommands.Query(background => OpenPeerShellCommandMenu(
            background, shellView, shellFolder, owner, relativePidls));
        if (!commandStates.GetValueOrDefault(ShellFileCommand.Copy) ||
            !commandStates.GetValueOrDefault(ShellFileCommand.Delete) ||
            !commandStates.GetValueOrDefault(ShellFileCommand.NewFolder))
            throw new InvalidOperationException($"The product ShellFileCommands query did not combine selected and background verbs correctly: {string.Join(',', commandStates.Select(pair => $"{pair.Key}={pair.Value}"))}.");
        Console.WriteLine($"ishellview-peer-command-matrix: selected-count={relativePidls.Count}; copy={commandStates[ShellFileCommand.Copy]}; delete={commandStates[ShellFileCommand.Delete]}; new-folder={commandStates[ShellFileCommand.NewFolder]}; query=ShellFileCommands.Query; popup-tracked-and-cancelled=True");
    }

    private static void VerifyPeerContextMenuPopup(nint contextMenu, nint owner, int selectedCount)
    {
        const int EnforceDefault = 0x0010;
        const int ExtendedVerbs = 0x0100;
        var menu = CreatePopupMenu();
        if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the isolated peer context menu.");
        try
        {
            var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff,
                EnforceDefault | ExtendedVerbs);
            Check(queryHr);
            var itemCount = GetMenuItemCount(menu);
            if (itemCount <= 0) throw new InvalidOperationException("The selected Shell context menu returned no popup items.");
            VerifyPeerContextMenuSubmenuCallbacks(contextMenu, selectedCount);

            var popup = TrackPopupMenuAndCancel(owner, menu, contextMenu);
            if (popup.SelectedCommand != 0 || popup.InitPopupCount != 1 ||
                (popup.InitResult != 0 && !popup.ForwardedToOwnerProc))
                throw new InvalidOperationException($"The selected Shell context menu did not enter and leave the native popup loop cleanly: selected={popup.SelectedCommand}; init-count={popup.InitPopupCount}; init-hr=0x{popup.InitResult:X8}; forwarded={popup.ForwardedToOwnerProc}.");
            Console.WriteLine($"ishellview-peer-selected-context-menu-popup: selected-count={selectedCount}; query-hr=0x{queryHr:X8}; items={itemCount}; init-popup-count={popup.InitPopupCount}; init-hr=0x{popup.InitResult:X8}; context-menu3-query=0x{popup.ContextMenu3QueryHr:X8}; context-menu2-query=0x{popup.ContextMenu2QueryHr:X8}; context-menu3-message=0x{popup.ContextMenu3MessageHr:X8}; context-menu2-attempted={popup.ContextMenu2Attempted}; context-menu2-message=0x{popup.ContextMenu2MessageHr:X8}; forwarded-to-owner-proc={popup.ForwardedToOwnerProc}; popup-cancelled=True; command-invoked=False");

            const int DefaultOnly = 0x0001;
            var defaultMenu = CreatePopupMenu();
            if (defaultMenu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the default-only Shell context menu.");
            try
            {
                var defaultQueryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, defaultMenu, 0, 1, 0x7fff, DefaultOnly);
                Check(defaultQueryHr);
                var defaultItemCount = GetMenuItemCount(defaultMenu);
                if (defaultItemCount <= 0) throw new InvalidOperationException("CMF_DEFAULTONLY returned no Shell context menu item.");
                var defaultPopup = TrackPopupMenuAndCancel(owner, defaultMenu, contextMenu);
                if (defaultPopup.SelectedCommand != 0 || defaultPopup.InitPopupCount != 1 ||
                    (defaultPopup.InitResult != 0 && !defaultPopup.ForwardedToOwnerProc))
                    throw new InvalidOperationException($"The default-only Shell menu did not enter and leave its popup loop cleanly: selected={defaultPopup.SelectedCommand}; init-count={defaultPopup.InitPopupCount}; init-hr=0x{defaultPopup.InitResult:X8}; forwarded={defaultPopup.ForwardedToOwnerProc}.");
                Console.WriteLine($"ishellview-peer-selected-context-menu-popup: mode=default-only; selected-count={selectedCount}; query-hr=0x{defaultQueryHr:X8}; items={defaultItemCount}; init-hr=0x{defaultPopup.InitResult:X8}; context-menu3-message=0x{defaultPopup.ContextMenu3MessageHr:X8}; forwarded-to-owner-proc={defaultPopup.ForwardedToOwnerProc}; popup-cancelled=True; command-invoked=False");
            }
            finally { _ = DestroyMenu(defaultMenu); }
        }
        finally { _ = DestroyMenu(menu); }
    }

    private static void VerifyPeerContextMenuSubmenuCallbacks(nint contextMenu, int selectedCount)
    {
        const int EnforceDefault = 0x0010;
        const int ExtendedVerbs = 0x0100;
        var contextMenu3Id = new Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
        var menu = CreatePopupMenu();
        if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the submenu callback diagnostic menu.");
        nint contextMenu3 = 0;
        try
        {
            Check(At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff,
                EnforceDefault | ExtendedVerbs));
            var queryHr = Marshal.QueryInterface(contextMenu, in contextMenu3Id, out contextMenu3);
            if (queryHr < 0)
            {
                Console.WriteLine($"ishellview-peer-context-menu-submenu-callbacks: selected-count={selectedCount}; context-menu3-query=0x{queryHr:X8}; submenu-count=0");
                return;
            }

            var results = new List<string>();
            var menuCount = GetMenuItemCount(menu);
            for (var index = 0; index < menuCount; index++)
            {
                var submenu = GetSubMenu(menu, index);
                if (submenu == 0) continue;
                var caption = new System.Text.StringBuilder(128);
                _ = GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400); // MF_BYPOSITION
                var hr = At<MenuMessage2>(contextMenu3, 7)(contextMenu3, 0x0117, submenu, index, out _);
                results.Add($"{caption}:0x{hr:X8}");
            }
            Console.WriteLine($"ishellview-peer-context-menu-submenu-callbacks: selected-count={selectedCount}; context-menu3-query=0x{queryHr:X8}; submenu-count={results.Count}; results=[{string.Join('|', results)}]");
        }
        finally
        {
            if (contextMenu3 != 0) Marshal.Release(contextMenu3);
            _ = DestroyMenu(menu);
        }
    }

    private static void VerifyPeerMultiSelectionCopy(nint shellFolder, nint owner, nint listView,
        IReadOnlyList<int> initialIds, int selectedRowId, int secondarySelectedRowId,
        IReadOnlyList<nint> relativePidls, IReadOnlyList<string> sourcePaths)
    {
        if (relativePidls.Count != 2 || sourcePaths.Count != 2)
            throw new ArgumentException("The peer copy integration contract requires exactly two selected rows.");
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var targetPath = Path.Combine(fixtureRoot, "peer-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetPath);
        nint children = 0, dataObject = 0, operation = 0, destinationItem = 0;
        nint dropTargetPidl = 0, dropTargetFolder = 0, dropTarget = 0, dropEnumerator = 0;
        try
        {
            children = Marshal.AllocHGlobal(IntPtr.Size * relativePidls.Count);
            for (var index = 0; index < relativePidls.Count; index++)
                Marshal.WriteIntPtr(children, index * IntPtr.Size, relativePidls[index]);
            var dataObjectId = DataObjectId;
            Check(At<GetUIObjectOf>(shellFolder, 10)(shellFolder, owner, (uint)relativePidls.Count,
                children, in dataObjectId, 0, out dataObject));
            if (dataObject == 0) throw new InvalidOperationException("The selected peer rows did not provide a Shell IDataObject.");

            var operationClassId = FileOperationClassId;
            var operationId = FileOperationId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref operationClassId, 0, 1, ref operationId, out operation));
            Marshal.ThrowExceptionForHR(At<SetOperationFlags>(operation, 5)(operation,
                0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000)); // Silent, no prompts/errors, no copy hooks.
            var shellItemId = ShellItemId;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(targetPath, 0, ref shellItemId, out destinationItem));
            Marshal.ThrowExceptionForHR(At<CopyItems>(operation, 17)(operation, dataObject, destinationItem));
            Marshal.ThrowExceptionForHR(At<PerformOperations>(operation, 21)(operation));
            Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("The peer multi-selection Shell copy was aborted.");

            for (var index = 0; index < sourcePaths.Count; index++)
            {
                var name = Path.GetFileName(sourcePaths[index]);
                var sourcePayload = Path.Combine(sourcePaths[index], "payload.bin");
                var targetPayload = Path.Combine(targetPath, name, "payload.bin");
                if (!Directory.Exists(sourcePaths[index]) || !Directory.Exists(Path.Combine(targetPath, name)) || !File.Exists(targetPayload) ||
                    !File.ReadAllBytes(sourcePayload).SequenceEqual(File.ReadAllBytes(targetPayload)))
                    throw new IOException($"The selected Shell IDataObject did not copy the complete directory '{name}'.");
            }

            var dropPath = Path.Combine(targetPath, "drop-target");
            Directory.CreateDirectory(dropPath);
            Check(SHParseDisplayName(dropPath, 0, out dropTargetPidl, 0, out _));
            var shellFolderId = ShellFolderId;
            Check(SHBindToObject(0, dropTargetPidl, 0, in shellFolderId, out dropTargetFolder));
            var dropTargetId = DropTargetId;
            Check(At<CreateViewObject>(dropTargetFolder, 8)(dropTargetFolder, owner, ref dropTargetId, out dropTarget));
            var requestedEffect = 1u; // DROPEFFECT_COPY; exercise target callbacks without OLE DoDragDrop/input.
            var point = new NativePoint { X = 1, Y = 1 };
            var enterHr = At<DropTargetDragEnter>(dropTarget, 3)(dropTarget, dataObject, 1, point, ref requestedEffect);
            var entered = enterHr >= 0 && (requestedEffect & 1u) != 0;
            var dropHr = unchecked((int)0x80004005);
            var dropEffect = 1u;
            if (entered)
                dropHr = At<DropTargetDrop>(dropTarget, 6)(dropTarget, dataObject, 1, point, ref dropEffect);
            else
                _ = At<DropTargetDragLeave>(dropTarget, 5)(dropTarget);
            if (!entered || dropHr < 0 || (dropEffect & 1u) == 0)
                throw new IOException($"The selected peer IDataObject was rejected by the Shell drop target: enter=0x{enterHr:X8}; drop=0x{dropHr:X8}; effect={dropEffect}.");
            foreach (var sourcePath in sourcePaths)
            {
                var name = Path.GetFileName(sourcePath);
                var sourcePayload = Path.Combine(sourcePath, "payload.bin");
                var dropPayload = Path.Combine(dropPath, name, "payload.bin");
                if (!Directory.Exists(sourcePath) || !File.Exists(dropPayload) ||
                    !File.ReadAllBytes(sourcePayload).SequenceEqual(File.ReadAllBytes(dropPayload)))
                    throw new IOException($"The Shell drop target did not copy the selected directory '{name}' intact.");
            }

            Check(At<EnumObjects>(dropTargetFolder, 4)(dropTargetFolder, owner, 0x20 | 0x40, out dropEnumerator));
            if (dropEnumerator == 0) throw new InvalidOperationException("The destination Shell folder returned no enumerator.");
            var droppedShellPaths = new List<string>();
            try
            {
                while (true)
                {
                    var nextHr = At<NextShellIdList>(dropEnumerator, 3)(dropEnumerator, 1, out var childPidl, 0);
                    if (nextHr == 1) break; // S_FALSE: the target's native folder enumeration is complete.
                    Marshal.ThrowExceptionForHR(nextHr);
                    var absoluteChild = ILCombine(dropTargetPidl, childPidl);
                    if (absoluteChild == 0)
                    {
                        ILFree(childPidl);
                        throw new InvalidOperationException("Could not combine a dropped child PIDL with its destination folder.");
                    }
                    try
                    {
                        Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absoluteChild, SigDnFileSystemPath, out var childPathPointer));
                        try { droppedShellPaths.Add(Marshal.PtrToStringUni(childPathPointer) ?? string.Empty); }
                        finally { Marshal.FreeCoTaskMem(childPathPointer); }
                    }
                    finally
                    {
                        ILFree(absoluteChild);
                        ILFree(childPidl);
                    }
                }
            }
            finally
            {
                Marshal.Release(dropEnumerator);
                dropEnumerator = 0;
            }
            var droppedShellNames = droppedShellPaths.Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var expectedDroppedNames = sourcePaths.Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!droppedShellNames.SequenceEqual(expectedDroppedNames, StringComparer.OrdinalIgnoreCase))
                throw new IOException($"The destination Shell folder did not enumerate the dropped rows: [{string.Join(',', droppedShellNames)}].");

            Application.DoEvents();
            var selectedIndex = Array.IndexOf(initialIds.ToArray(), selectedRowId);
            var secondaryIndex = Array.IndexOf(initialIds.ToArray(), secondarySelectedRowId);
            var selectionFocusPreserved = selectedIndex >= 0 && secondaryIndex >= 0 &&
                GetPeerListItemState(listView, selectedIndex, 0x0003) == 0x0003 &&
                GetPeerListItemState(listView, secondaryIndex, 0x0002) == 0x0002;
            if (!ReadPeerListRowIds(listView).SequenceEqual(initialIds) || !selectionFocusPreserved)
                throw new InvalidOperationException("The peer copy changed source row identities, multi-selection, or the independent focus anchor.");
            Console.WriteLine($"ishellview-peer-selected-copy: selected-count={relativePidls.Count}; source=selected-peer-row-IDataObject; operation=IFileOperation.CopyItems; copied-directories=[{string.Join(',', sourcePaths.Select(Path.GetFileName))}]; sources-retained=True; source-row-ids-retained=True; selection-focus-preserved={selectionFocusPreserved}; clipboard-touched=False; aborted=False");
            Console.WriteLine($"ishellview-peer-selected-drop: selected-count={relativePidls.Count}; target=Shell-folder-IDropTarget; drag-enter-hr=0x{enterHr:X8}; drop-hr=0x{dropHr:X8}; effect={dropEffect}; copied-directories=[{string.Join(',', droppedShellNames)}]; native-enumeration=True; sources-retained=True; clipboard-touched=False; DoDragDrop=False");
        }
        finally
        {
            if (dropTarget != 0) Marshal.Release(dropTarget);
            if (dropEnumerator != 0) Marshal.Release(dropEnumerator);
            if (dropTargetFolder != 0) Marshal.Release(dropTargetFolder);
            if (dropTargetPidl != 0) ILFree(dropTargetPidl);
            if (destinationItem != 0) Marshal.Release(destinationItem);
            if (operation != 0) Marshal.Release(operation);
            if (dataObject != 0) Marshal.Release(dataObject);
            if (children != 0) Marshal.FreeHGlobal(children);
            if (Directory.Exists(targetPath)) Directory.Delete(targetPath, recursive: true);
        }
    }

    private static IShellCommandMenu? OpenPeerShellCommandMenu(bool background, nint shellView,
        nint shellFolder, nint owner, IReadOnlyList<nint> relativePidls)
    {
        nint contextMenu = 0;
        if (background)
        {
            var menuId = ContextMenuId;
            Check(At<GetItemObject>(shellView, 15)(shellView, 0, ref menuId, out contextMenu)); // SVGIO_BACKGROUND
        }
        else
        {
            var children = Marshal.AllocHGlobal(IntPtr.Size * relativePidls.Count);
            try
            {
                for (var index = 0; index < relativePidls.Count; index++)
                    Marshal.WriteIntPtr(children, index * IntPtr.Size, relativePidls[index]);
                var menuId = ContextMenuId;
                Check(At<GetUIObjectOf>(shellFolder, 10)(shellFolder, owner, (uint)relativePidls.Count,
                    children, in menuId, 0, out contextMenu));
            }
            finally { Marshal.FreeHGlobal(children); }
        }

        if (contextMenu == 0) return null;
        try { return new NativeShellCommandMenu(Marshal.GetObjectForIUnknown(contextMenu), owner); }
        finally { Marshal.Release(contextMenu); }
    }

    private static void VerifyPeerListDetails(nint listView, IReadOnlyList<PeerShellRow> rows)
    {
        if (SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32() != rows.Count)
            throw new Exception("The peer ListView details row count differs from the Shell model.");
        var header = SendMessageW(listView, 0x101F, 0, 0);
        if (header == 0 || SendMessageW(header, 0x1200, 0, 0).ToInt32() != rows[0].DetailTitles.Length)
            throw new Exception("The peer ListView details header count differs from Shell IShellFolder2.");
        var buffer = Marshal.AllocHGlobal(512 * sizeof(char));
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            for (var row = 0; row < rows.Count; row++)
            {
                for (var column = 0; column < rows[row].DetailTitles.Length; column++)
                {
                    Marshal.WriteInt16(buffer, 0);
                    Marshal.StructureToPtr(new NativeListViewItem { Item = row, SubItem = column, Text = buffer, TextCapacity = 512 }, itemPointer, false);
                    _ = SendMessageW(listView, LvmGetItemTextW, row, itemPointer);
                    var actual = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                    if (!string.Equals(actual, rows[row].DetailValues[column], StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Peer ListView detail mismatch at row {row}, column {column} ('{rows[row].DetailTitles[column]}'): expected='{rows[row].DetailValues[column]}', actual='{actual}'.");
                }
            }
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(buffer); }
    }

    private static string[] ReadPeerListNames(nint listView)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        var rows = new string[count];
        var buffer = Marshal.AllocHGlobal(1024);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            for (var index = 0; index < count; index++)
            {
                var item = new NativeListViewItem { Item = index, SubItem = 0, Text = buffer, TextCapacity = 512 };
                Marshal.StructureToPtr(item, itemPointer, false);
                _ = SendMessageW(listView, LvmGetItemTextW, index, itemPointer);
                rows[index] = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            return rows;
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(buffer); }
    }

    private static int[] ReadPeerListRowIds(nint listView)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        var ids = new int[count];
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            for (var index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(new NativeListViewItem { Mask = 0x0004, Item = index }, itemPointer, false);
                if (SendMessageW(listView, LvmGetItemW, 0, itemPointer) == 0)
                    throw new InvalidOperationException($"Could not read peer ListView row identity {index}.");
                ids[index] = Marshal.PtrToStructure<NativeListViewItem>(itemPointer).Parameter.ToInt32();
            }
            return ids;
        }
        finally { Marshal.FreeHGlobal(itemPointer); }
    }

    private static (List<PeerShellRow> Rows, nint SystemImageList) CapturePeerShellRows(nint folderView,
        nint folderPidl, nint shellFolder, IReadOnlyList<PeerShellRow> previous, ref int nextId,
        IReadOnlyDictionary<string, string>? renamedParsingNames = null)
    {
        Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
        var enumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
        nint shellFolder2 = 0;
        try
        {
            var folder2Id = ShellFolder2Id;
            Check(Marshal.QueryInterface(shellFolder, in folder2Id, out shellFolder2));
            var detailTitles = ReadShellDetailColumnTitles(shellFolder2);
            if (detailTitles.Length < 2)
                throw new InvalidOperationException("The Shell folder did not expose multiple standard details columns.");
            var byIdentity = enumeration.Rows.ToDictionary(row => GetPidlIdentity(row.AbsolutePidl), StringComparer.Ordinal);
            var current = new List<PeerShellRow>(count);
            for (var index = 0; index < count; index++)
            {
                Check(At<GetItem>(folderView, 6)(folderView, index, out var childPidl));
                nint absolutePidl = 0, retainedPidl = 0;
                try
                {
                    absolutePidl = ILCombine(folderPidl, childPidl);
                    if (absolutePidl == 0) throw new InvalidOperationException("Could not combine the view item with its parent PIDL.");
                    var identity = GetPidlIdentity(absolutePidl);
                    if (!byIdentity.TryGetValue(identity, out var row))
                    {
                        Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolutePidl, SigDnFileSystemPath, out var viewPathPointer));
                        string viewPath;
                        try { viewPath = Marshal.PtrToStringUni(viewPathPointer) ?? "<unknown>"; }
                        finally { Marshal.FreeCoTaskMem(viewPathPointer); }
                        row = enumeration.Rows.SingleOrDefault(candidate => string.Equals(candidate.FileSystemPath,
                            viewPath, StringComparison.OrdinalIgnoreCase));
                        if (row is null)
                        {
                            var enumeratedPaths = string.Join(" | ", enumeration.Rows.Select(candidate => candidate.FileSystemPath ?? candidate.ParsingName));
                            throw new InvalidOperationException($"The Shell view row '{viewPath}' identity was absent from folder enumeration. Enumerated=[{enumeratedPaths}].");
                        }
                    }
                    retainedPidl = ILClone(row.AbsolutePidl);
                    if (retainedPidl == 0) throw new InvalidOperationException("Could not retain a projected Shell row PIDL.");
                    var groupTitle = !string.IsNullOrWhiteSpace(row.TypeName)
                        ? row.TypeName
                        : (row.Attributes & 0x20000000u) != 0
                            ? "File folder"
                            : string.IsNullOrWhiteSpace(row.FileSystemPath) || string.IsNullOrWhiteSpace(Path.GetExtension(row.FileSystemPath))
                                ? "File"
                                : Path.GetExtension(row.FileSystemPath).TrimStart('.').ToUpperInvariant() + " file";
                    var detailValues = ReadShellDetailValues(shellFolder2, row.RelativePidl, detailTitles.Length);
                    var retainedRelativePidl = ILClone(row.RelativePidl);
                    if (retainedRelativePidl == 0) throw new InvalidOperationException("Could not retain a projected Shell child PIDL.");
                    current.Add(new PeerShellRow(0, retainedPidl, retainedRelativePidl, row.DisplayName, row.ParsingName,
                        row.FileSystemPath, groupTitle, row.SystemImageIndex, detailTitles, detailValues)
                    { FileSystemIdentity = row.FileSystemIdentity });
                    retainedPidl = 0;
                }
                finally
                {
                    if (absolutePidl != 0) ILFree(absolutePidl);
                    if (retainedPidl != 0) ILFree(retainedPidl);
                    Marshal.FreeCoTaskMem(childPidl);
                }
            }

            var assigned = new List<PeerShellRow>(current.Count);
            var usedPreviousIds = new HashSet<int>();
            foreach (var row in current)
            {
                var old = previous.FirstOrDefault(candidate => ILIsEqual(candidate.AbsolutePidl, row.AbsolutePidl));
                if (old is null && row.FileSystemIdentity is { } identity)
                    old = previous.FirstOrDefault(candidate => candidate.FileSystemIdentity == identity);
                if (old is null && renamedParsingNames is not null &&
                    renamedParsingNames.TryGetValue(row.ParsingName, out var oldParsingName))
                    old = previous.FirstOrDefault(candidate => string.Equals(candidate.ParsingName, oldParsingName,
                        StringComparison.OrdinalIgnoreCase));
                if (old is null)
                    assigned.Add(row with { Id = ++nextId });
                else
                {
                    usedPreviousIds.Add(old.Id);
                    ILFree(row.AbsolutePidl);
                    assigned.Add(row with { Id = old.Id, AbsolutePidl = old.AbsolutePidl });
                    if (old.RelativePidl != 0) ILFree(old.RelativePidl);
                }
            }
            foreach (var old in previous)
                if (!usedPreviousIds.Contains(old.Id))
                {
                    if (old.AbsolutePidl != 0) ILFree(old.AbsolutePidl);
                    if (old.RelativePidl != 0) ILFree(old.RelativePidl);
                }
            return (assigned, enumeration.SystemImageList);
        }
        finally
        {
            if (shellFolder2 != 0) Marshal.Release(shellFolder2);
            ShellListViewEnumerator.FreeRows(enumeration.Rows);
        }
    }

    private static string[] ReadShellDetailColumnTitles(nint shellFolder2)
    {
        var titles = new string[4];
        for (uint index = 0; index < titles.Length; index++)
        {
            var hr = ReadShellDetail(shellFolder2, 0, index, out var title);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            titles[index] = title;
        }
        if (string.IsNullOrWhiteSpace(titles[0]))
            throw new InvalidOperationException("IShellFolder2 returned no name column title.");
        return titles;
    }

    private static string[] ReadShellDetailValues(nint shellFolder2, nint relativePidl, int count)
    {
        var values = new string[count];
        for (uint index = 0; index < count; index++)
        {
            var hr = ReadShellDetail(shellFolder2, relativePidl, index, out values[index]);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        return values;
    }

    private static void WaitForPeerShellItem(nint folderView, string path, bool shouldExist, int expectedCount)
    {
        var itemName = Path.GetFileName(path);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var stableSamples = 0;
        while (DateTime.UtcNow < deadline && stableSamples < 3)
        {
            Application.DoEvents();
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
            var names = count > 0 ? ReadVisibleItemNames(folderView, count) : [];
            var itemPresent = names.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            stableSamples = count == expectedCount && itemPresent == shouldExist ? stableSamples + 1 : 0;
            if (stableSamples < 3) Thread.Sleep(50);
        }
        if (stableSamples < 3)
            throw new TimeoutException($"The Shell view did not settle to {expectedCount} rows with '{itemName}' present={shouldExist}.");
    }

    private static void WaitForPeerShellRename(nint folderView, string oldPath, string newPath, int expectedCount)
    {
        var oldName = Path.GetFileName(oldPath);
        var newName = Path.GetFileName(newPath);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var stableSamples = 0;
        while (DateTime.UtcNow < deadline && stableSamples < 3)
        {
            Application.DoEvents();
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
            var names = count > 0 ? ReadVisibleItemNames(folderView, count) : [];
            var renameSettled = count == expectedCount && names.Contains(newName, StringComparer.OrdinalIgnoreCase) &&
                !names.Contains(oldName, StringComparer.OrdinalIgnoreCase);
            stableSamples = renameSettled ? stableSamples + 1 : 0;
            if (stableSamples < 3) Thread.Sleep(50);
        }
        if (stableSamples < 3)
            throw new TimeoutException($"The Shell view did not settle after renaming '{oldName}' to '{newName}'.");
    }

    private static int ReadShellDetail(nint shellFolder2, nint item, uint column, out string text)
    {
        var details = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeShellDetails>());
        try
        {
            Marshal.StructureToPtr(default(NativeShellDetails), details, false);
            var hr = At<GetDetailsOf>(shellFolder2, 18)(shellFolder2, item, column, details);
            if (hr < 0)
            {
                text = string.Empty;
                return hr;
            }
            var value = Marshal.PtrToStructure<NativeShellDetails>(details);
            if (value.Text.Type == 0)
            {
                if (value.Text.Pointer == 0) text = string.Empty;
                else
                {
                    try { text = Marshal.PtrToStringUni(value.Text.Pointer) ?? string.Empty; }
                    finally { Marshal.FreeCoTaskMem(value.Text.Pointer); }
                }
                return hr;
            }
            var buffer = new System.Text.StringBuilder(512);
            var strret = IntPtr.Add(details, Marshal.OffsetOf<NativeShellDetails>(nameof(NativeShellDetails.Text)).ToInt32());
            hr = StrRetToBufW(strret, item, buffer, (uint)buffer.Capacity);
            text = hr < 0 ? string.Empty : buffer.ToString();
            return hr;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static (List<PeerShellRow> Rows, bool ExistingIdentitiesPreserved) ProjectPeerShellOrder(nint listView,
        nint folderView, nint folderPidl, nint shellFolder, IReadOnlyList<PeerShellRow> previous,
        ref int nextId, int expectedCount, bool groupByType = false,
        IReadOnlyDictionary<string, string>? renamedParsingNames = null)
    {
        var projection = CapturePeerShellRows(folderView, folderPidl, shellFolder, previous, ref nextId, renamedParsingNames);
        if (projection.Rows.Count != expectedCount)
            throw new Exception($"The Shell row projection count differs: expected={expectedCount}, actual={projection.Rows.Count}.");
        var idsPreserved = projection.Rows.Count == previous.Count &&
            projection.Rows.Select(row => row.Id).ToHashSet().SetEquals(previous.Select(row => row.Id));
        PopulatePeerList(listView, projection.Rows, groupByType);
        if (!ReadPeerListNames(listView).SequenceEqual(projection.Rows.Select(row => row.Name), StringComparer.OrdinalIgnoreCase) ||
            !ReadPeerListRowIds(listView).SequenceEqual(projection.Rows.Select(row => row.Id)))
            throw new Exception("The independent ListView did not match Shell row order and stable PIDL identities after projection.");
        VerifyPeerListDetails(listView, projection.Rows);
        return (projection.Rows, idsPreserved);
    }

    private static void VerifyPeerListGrouping(nint listView, IReadOnlyList<PeerShellRow> rows)
    {
        var expected = rows.Select(row => row.TypeName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var enabled = SendMessageW(listView, LvmIsGroupViewEnabled, 0, 0).ToInt32() != 0;
        var actual = SendMessageW(listView, LvmGetGroupCount, 0, 0).ToInt32();
        if (!enabled || actual != expected)
            throw new InvalidOperationException($"Peer ListView group projection mismatch: enabled={enabled}, groups={actual}, expected={expected}.");
    }

    private static string GetPidlIdentity(nint pidl)
    {
        var size = ILGetSize(pidl);
        if (size == 0 || size > 65_536) throw new InvalidOperationException($"Invalid Shell PIDL byte count: {size}.");
        var bytes = new byte[(int)size];
        Marshal.Copy(pidl, bytes, 0, bytes.Length);
        return Convert.ToHexString(bytes);
    }

    // ILIsEqual accepts a common PIDL prefix on this nested namespace; exact location identity also needs equal lengths.
    private static bool PidlExactlyEquals(nint left, nint right)
    {
        if (left == 0 || right == 0 || ILGetSize(left) != ILGetSize(right)) return false;
        return ILIsEqual(left, right);
    }

    private static string DescribePidl(nint pidl)
    {
        var size = ILGetSize(pidl);
        if (size < 2 || size > 65_536) return $"invalid-size:{size}";
        var bytes = new byte[(int)size];
        Marshal.Copy(pidl, bytes, 0, bytes.Length);
        var parts = new List<string>();
        var offset = 0;
        while (offset + sizeof(ushort) <= bytes.Length)
        {
            var cb = BitConverter.ToUInt16(bytes, offset);
            if (cb == 0) return $"size:{size};items:[{string.Join('|', parts)}];terminator:{offset}";
            if (cb < sizeof(ushort) || offset + cb > bytes.Length) return $"size:{size};invalid-item-at:{offset};cb:{cb}";
            parts.Add($"{offset}:{cb}:{Convert.ToHexString(bytes.AsSpan(offset, Math.Min((int)cb, 24)))}");
            offset += cb;
        }
        return $"size:{size};unterminated;items:[{string.Join('|', parts)}]";
    }

    private static void FreePeerShellRows(IEnumerable<PeerShellRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
            if (row.RelativePidl != 0) ILFree(row.RelativePidl);
        }
    }

    private static bool FocusedItemMatches(nint folderView, nint expectedPidl)
    {
        if (At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex) < 0 || focusedIndex < 0)
            return false;
        if (At<GetItem>(folderView, 6)(folderView, focusedIndex, out var focusedPidl) < 0 || focusedPidl == 0)
            return false;
        try { return ILIsEqual(focusedPidl, expectedPidl); }
        finally { Marshal.FreeCoTaskMem(focusedPidl); }
    }

    private static void VerifyColumnHeaderControl(nint view, nint owner)
    {
        var listView = FindChildByClass(owner, "SysListView32");
        if (listView == 0) throw new Exception("The Details view has no native list control.");
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        if (header == 0) throw new Exception("The Details view did not expose its native column header control.");
        var headerClass = new System.Text.StringBuilder(64);
        if (GetClassNameW(header, headerClass, headerClass.Capacity) <= 0 || headerClass.ToString() != "SysHeader32")
            throw new Exception($"The Details column header has an unexpected window class: {headerClass}.");
        var headerCount = SendMessageW(header, 0x1200, 0, 0).ToInt32(); // HDM_GETITEMCOUNT
        Check(Marshal.QueryInterface(view, in ColumnManagerId, out var columnManager));
        try
        {
            Check(At<GetShellColumnCount>(columnManager, 5)(columnManager, 2, out var visibleCount)); // CM_ENUM_VISIBLE
            if (headerCount <= 0 || headerCount != visibleCount)
                throw new Exception($"The native header and Shell visible-column counts disagree: header={headerCount}, shell={visibleCount}.");
            Console.WriteLine($"ishellview-column-header: class=SysHeader32; header-items={headerCount}; shell-visible-columns={visibleCount}");
        }
        finally { Marshal.Release(columnManager); }
    }

    private static void VerifyColumnHeaderContextMenu(nint view, nint owner)
    {
        var listView = FindChildByClass(owner, "SysListView32");
        var header = listView == 0 ? 0 : SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        if (header == 0) throw new Exception("Could not locate the Details column header for its isolated context-menu probe.");
        var headerRect = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        var capturedItems = new List<(uint Id, string Text)>();
        var capturedSubmenus = new List<(string Caption, List<(int Id, string Text)> Items)>();
        var initializedSubmenus = new HashSet<nint>();
        nint capturedRootMenu = 0;
        nint capturedPopupOwner = 0;
        var capturedPopupOwnerChain = "(not captured)";
        HookProc? hookCallback = null;
        nint hook = 0;
        try
        {
            if (!GetWindowRect(header, headerRect) || SendMessageW(header, 0x1207, 0, headerRect) == 0) // HDM_GETITEMRECT
                throw new Exception("Could not locate a column header hit point.");
            var itemRect = Marshal.PtrToStructure<NativeRect>(headerRect);
            if (!GetWindowRect(header, headerRect)) throw new Exception("Could not read the header's screen bounds.");
            var screenRect = Marshal.PtrToStructure<NativeRect>(headerRect);
            var x = screenRect.Left + (itemRect.Left + itemRect.Right) / 2;
            var y = screenRect.Top + (itemRect.Top + itemRect.Bottom) / 2;
            var threadId = GetCurrentThreadId();
            hookCallback = (code, wParam, lParam) =>
            {
                try
                {
                    if (code >= 0 && lParam != 0)
                    {
                        var message = Marshal.PtrToStructure<CallWindowProcResult>(lParam);
                        if (message.Message == 0x0117 && message.WParam != 0) // WM_INITMENUPOPUP after Shell initialization
                        {
                            var menu = unchecked((nint)message.WParam);
                            if (capturedRootMenu != 0) return CallNextHookEx(hook, code, wParam, lParam);
                            capturedRootMenu = menu;
                            capturedPopupOwner = message.Window;
                            capturedPopupOwnerChain = DescribeWindowChain(message.Window);
                            var count = GetMenuItemCount(menu);
                            for (var index = 0; index < count; index++)
                            {
                                var text = new System.Text.StringBuilder(256);
                                if (GetMenuStringW(menu, (uint)index, text, text.Capacity, 0x400) > 0 &&
                                    text.Length > 0)
                                    capturedItems.Add((GetMenuItemID(menu, index), text.ToString()));
                            }
                            // The real popup opens submenus lazily. Ask the same owner to initialize
                            // each child while the tracked root menu is still alive, then read it back.
                            for (var index = 0; index < count; index++)
                            {
                                var submenu = GetSubMenu(menu, index);
                                if (submenu == 0 || !initializedSubmenus.Add(submenu)) continue;
                                var caption = new System.Text.StringBuilder(256);
                                _ = GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400);
                                _ = SendMessageW(message.Window, 0x0117, submenu, (nint)index);
                                var childCount = GetMenuItemCount(submenu);
                                var childItems = new List<(int Id, string Text)>();
                                for (var childIndex = 0; childIndex < childCount; childIndex++)
                                {
                                    var childText = new System.Text.StringBuilder(256);
                                    if (GetMenuStringW(submenu, (uint)childIndex, childText, childText.Capacity, 0x400) > 0 &&
                                        childText.Length > 0)
                                        childItems.Add((unchecked((int)GetMenuItemID(submenu, childIndex)), childText.ToString()));
                                }
                                capturedSubmenus.Add((caption.ToString(), childItems));
                            }
                            _ = EndMenu(); // Close the isolated popup before it can await user input.
                        }
                    }
                }
                catch { _ = EndMenu(); }
                return CallNextHookEx(hook, code, wParam, lParam);
            };
            hook = SetWindowsHookExW(12, hookCallback, 0, threadId); // WH_CALLWNDPROCRET, current probe thread only
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not install the isolated menu observer.");
            var position = unchecked((nint)(((y & 0xffff) << 16) | (x & 0xffff)));
            _ = SendMessageW(header, 0x007B, header, position); // WM_CONTEXTMENU
        }
        finally
        {
            _ = EndMenu();
            if (hook != 0) _ = UnhookWindowsHookEx(hook);
            Marshal.FreeHGlobal(headerRect);
        }

        if (capturedItems.Count < 4)
            throw new Exception($"The native column header did not produce a populated Shell menu on the isolated desktop: items={capturedItems.Count}.");
        var backgroundCaptions = ReadBackgroundMenuCaptions(view);
        var commonCaptions = capturedItems.Select(item => item.Text)
            .Intersect(backgroundCaptions, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (commonCaptions.Length < 5)
            throw new Exception($"The column header popup and background IContextMenu shared too few exact captions: {commonCaptions.Length}.");
        var backgroundCommands = ReadBackgroundMenuCommandDescriptors(view);
        var backgroundSubmenus = ReadBackgroundMenuSubmenus(view);
        var idOffsetMatches = new Dictionary<int, List<string>>();
        foreach (var actual in capturedItems.Where(item => item.Id != uint.MaxValue))
        foreach (var command in backgroundCommands.Where(command =>
                     command.Verb is "refresh" or "paste" or "undo" or "redo" &&
                     string.Equals(command.Caption, actual.Text, StringComparison.OrdinalIgnoreCase)))
        {
            var offset = unchecked((int)actual.Id - command.Id);
            if (!idOffsetMatches.TryGetValue(offset, out var matched)) idOffsetMatches[offset] = matched = [];
            matched.Add(command.Verb);
        }
        var strongestOffset = idOffsetMatches.OrderByDescending(pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count()).FirstOrDefault();
        var mappedVerbs = strongestOffset.Value?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (!mappedVerbs.Contains("refresh", StringComparer.OrdinalIgnoreCase) || mappedVerbs.Length < 2)
            throw new Exception($"Could not map the header popup's standard leaf commands to one background IContextMenu ID base: [{string.Join(',', mappedVerbs)}].");
        var canonicalSubmenuVerbs = backgroundCommands
            .Where(command => command.Caption is "查看(&V)" or "排序方式(&O)" or "分组依据(&P)")
            .Select(command => $"{command.Caption}:{command.Verb}").ToArray();
        var mappedSubmenuRows = new List<string>();
        foreach (var expectedVerb in new[] { "view", "arrange", "groupby" })
        {
            var canonicalCommand = backgroundCommands.FirstOrDefault(command =>
                string.Equals(command.Verb, expectedVerb, StringComparison.OrdinalIgnoreCase));
            if (canonicalCommand is null)
                throw new Exception($"The background IContextMenu did not expose the '{expectedVerb}' submenu command.");
            var expectedCaption = canonicalCommand.Caption;
            var actualSubmenu = capturedSubmenus.FirstOrDefault(submenu => submenu.Caption == expectedCaption);
            var backgroundSubmenu = backgroundSubmenus.FirstOrDefault(submenu => submenu.Caption == expectedCaption);
            if (actualSubmenu.Items is null || backgroundSubmenu.Items is null)
                throw new Exception($"Could not locate both initialized menu trees for the standard submenu '{expectedCaption}'.");
            var matches = from actual in actualSubmenu.Items
                          join queried in backgroundSubmenu.Items on actual.Text equals queried.Text
                          where actual.Id != -1 && queried.Id != -1
                          select unchecked(actual.Id - queried.Id);
            var mappedOffsets = matches.ToArray();
            var offsets = mappedOffsets.Distinct().ToArray();
            if (mappedOffsets.Length != actualSubmenu.Items.Count || mappedOffsets.Length != backgroundSubmenu.Items.Count ||
                offsets.Length != 1 || offsets[0] != strongestOffset.Key)
                throw new Exception($"The '{expectedCaption}' submenu items did not map one-for-one to the background command ID base: mapped={mappedOffsets.Length}, header={actualSubmenu.Items.Count}, background={backgroundSubmenu.Items.Count}, offsets=[{string.Join(',', offsets)}], expected={strongestOffset.Key}.");
            mappedSubmenuRows.Add($"{expectedCaption}:{actualSubmenu.Items.Count(item => item.Id != -1)} items");
        }
        VerifyHeaderMenuSortByType(capturedSubmenus, capturedPopupOwner, view, strongestOffset.Key);
        Console.WriteLine($"ishellview-column-header-menu: owner=0x{capturedPopupOwner:X}; owner-chain=[{capturedPopupOwnerChain}]; items=[{string.Join('|', capturedItems.Select(item => item.Text))}]; canonical-submenu-verbs=[{string.Join(',', canonicalSubmenuVerbs)}]; canonical-leaf-verbs=[{string.Join(',', mappedVerbs)}]; mapped-submenus=[{string.Join(',', mappedSubmenuRows)}]; popup-command-id-base-offset={strongestOffset.Key}; popup-cancelled=True");
    }

    internal static (uint SelectedCommand, int InitPopupCount, int InitResult, bool UsedContextMenu3,
        int ContextMenu3QueryHr, int ContextMenu2QueryHr, int ContextMenu3MessageHr, int ContextMenu2MessageHr,
        bool ContextMenu2Attempted, bool ForwardedToOwnerProc)
        TrackPopupMenuAndCancel(nint owner, nint menu, nint contextMenu)
    {
        const uint WmInitMenuPopup = 0x0117;
        const int ENotImpl = unchecked((int)0x80004001);
        var contextMenu3Id = new Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
        var contextMenu2Id = new Guid("000214F4-0000-0000-C000-000000000046");
        nint context3 = 0, context2 = 0, hook = 0;
        var initPopupCount = 0;
        var initResult = ENotImpl;
        var usedContextMenu3 = false;
        var contextMenu3QueryHr = 0;
        var contextMenu2QueryHr = 0;
        var contextMenu3MessageHr = ENotImpl;
        var contextMenu2MessageHr = ENotImpl;
        var contextMenu2Attempted = false;
        var forwardedToOwnerProc = false;
        HookProc? hookCallback = null;
        SubclassProc? windowCallback = null;
        var subclassId = (nuint)0xE7D1;
        try
        {
            contextMenu3QueryHr = Marshal.QueryInterface(contextMenu, in contextMenu3Id, out context3);
            contextMenu2QueryHr = Marshal.QueryInterface(contextMenu, in contextMenu2Id, out context2);
            windowCallback = (window, messageId, wParam, lParam, id, referenceData) =>
            {
                if (messageId == WmInitMenuPopup && wParam == menu)
                {
                    initPopupCount++;
                    if (context3 != 0)
                    {
                        contextMenu3MessageHr = At<MenuMessage2>(context3, 7)(context3, WmInitMenuPopup, menu, lParam, out var handledResult);
                        initResult = contextMenu3MessageHr;
                        usedContextMenu3 = initResult == 0;
                        if (usedContextMenu3) return handledResult;
                    }
                    if (context2 != 0 && (!usedContextMenu3 && initResult is 1 or ENotImpl))
                    {
                        contextMenu2Attempted = true;
                        contextMenu2MessageHr = At<MenuMessage>(context2, 6)(context2, WmInitMenuPopup, menu, lParam);
                        initResult = contextMenu2MessageHr;
                        if (initResult == 0) return 0;
                    }
                    // Preserve the peer window's native menu-message chain when an extension returns an error.
                    forwardedToOwnerProc = true;
                }
                return DefSubclassProc(window, messageId, wParam, lParam);
            };
            if (!SetWindowSubclass(owner, windowCallback, subclassId, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not forward Shell menu initialization on the isolated owner window.");
            hookCallback = (code, wParam, lParam) =>
            {
                if (code >= 0 && lParam != 0)
                {
                    var message = Marshal.PtrToStructure<CallWindowProcResult>(lParam);
                    if (message.Message == WmInitMenuPopup && message.WParam == unchecked((nuint)menu) && message.Window == owner)
                        _ = EndMenu(); // Close after the owner has forwarded WM_INITMENUPOPUP.
                }
                return CallNextHookEx(hook, code, wParam, lParam);
            };
            hook = SetWindowsHookExW(12, hookCallback, 0, GetCurrentThreadId()); // WH_CALLWNDPROCRET, this probe thread only.
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not observe the isolated menu loop.");
            var selected = TrackPopupMenu(menu, 0x0100, 80, 80, 0, owner, 0); // TPM_RETURNCMD; the hook cancels at first initialization.
            return (selected, initPopupCount, initResult, usedContextMenu3,
                contextMenu3QueryHr, contextMenu2QueryHr, contextMenu3MessageHr, contextMenu2MessageHr,
                contextMenu2Attempted, forwardedToOwnerProc);
        }
        finally
        {
            _ = EndMenu();
            if (hook != 0) _ = UnhookWindowsHookEx(hook);
            _ = RemoveWindowSubclass(owner, windowCallback!, subclassId);
            if (context2 != 0) Marshal.Release(context2);
            if (context3 != 0) Marshal.Release(context3);
            GC.KeepAlive(windowCallback);
            GC.KeepAlive(hookCallback);
        }
    }

    private static void VerifyHeaderMenuSortByType(
        IReadOnlyList<(string Caption, List<(int Id, string Text)> Items)> capturedSubmenus,
        nint popupOwner, nint view, int popupCommandIdBaseOffset)
    {
        var sortMenu = capturedSubmenus.FirstOrDefault(submenu => submenu.Caption.StartsWith("排序方式", StringComparison.Ordinal));
        var typeItem = sortMenu.Items?.FirstOrDefault(item => item.Text.Contains("类型", StringComparison.Ordinal));
        if (typeItem is null || typeItem.Value.Id < 0)
            throw new Exception("The isolated column-header Sort menu did not expose a callable Type item.");

        var folderView2Id = FolderView2Id;
        Check(Marshal.QueryInterface(view, in folderView2Id, out var folderView2));
        try
        {
            var contextMenuId = ContextMenuId;
            Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu)); // SVGIO_BACKGROUND
            try
            {
                var menu = CreatePopupMenu();
                if (menu == 0) throw new Exception("Could not create the Shell context menu for header-command dispatch.");
                try
                {
                    Check(At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800));
                    // Recreate the host-side mapping from the captured header ID to this view's
                    // IContextMenu range. A bare WM_COMMAND to DefView did not update the sort;
                    // the header and background submenu mapping is checked above.
                    var commandOffset = checked(typeItem.Value.Id - popupCommandIdBaseOffset - 1);
                    var invoke = new NativeInvokeInfo
                    {
                        Size = Marshal.SizeOf<NativeInvokeInfo>(),
                        Window = popupOwner,
                        Verb = (nint)commandOffset,
                        Show = 0
                    };
                    Check(At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invoke));
                }
                finally { DestroyMenu(menu); }
            }
            finally { Marshal.Release(contextMenu); }

            Check(At<GetSortColumnCount>(folderView2, 26)(folderView2, out var count));
            var actual = new NativeSortColumn[count];
            if (count > 0) Check(At<GetSortColumns>(folderView2, 28)(folderView2, actual, count));
            if (count < 1 || actual[0].Key.FormatId != PropertyFormatId || actual[0].Key.PropertyId != 4)
                throw new Exception($"The column-header Type command did not set the Shell sort key: count={count}, key={(count == 0 ? "none" : $"{actual[0].Key.FormatId:B}/{actual[0].Key.PropertyId}")}.");
            Console.WriteLine($"ishellview-column-header-command: sort-by-type=True; command-id={typeItem.Value.Id}; primary-property-id={actual[0].Key.PropertyId}; sort-columns={count}");
        }
        finally { Marshal.Release(folderView2); }
    }

    private static IReadOnlyList<string> ReadBackgroundMenuCaptions(nint view) =>
        ReadBackgroundMenuCommandDescriptors(view).Select(command => command.Caption).Where(caption => caption.Length > 0).ToArray();

    private static IReadOnlyList<NativeMenuCommandDescriptor> ReadBackgroundMenuCommandDescriptors(nint view)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu));
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create an in-memory background Shell menu for command ID comparison.");
            try
            {
                var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryHr);
                var commandCount = Math.Min(queryHr & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(512);
                try
                {
                    List<NativeMenuCommandDescriptor> commands = [];
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        var id = checked((int)offset + 1);
                        var caption = new System.Text.StringBuilder(256);
                        if (GetMenuStringW(menu, (uint)id, caption, caption.Capacity, 0) <= 0) continue;
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) != 0) continue;
                        var verb = Marshal.PtrToStringUni(buffer);
                        if (!string.IsNullOrWhiteSpace(verb)) commands.Add(new NativeMenuCommandDescriptor(id, verb, caption.ToString()));
                    }
                    return commands;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static IReadOnlyList<(string Caption, List<(int Id, string Text)> Items)> ReadBackgroundMenuSubmenus(nint view)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu));
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create a background Shell menu tree for submenu comparison.");
            try
            {
                Check(At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800));
                List<(string Caption, List<(int Id, string Text)> Items)> submenus = [];
                for (var index = 0; index < GetMenuItemCount(menu); index++)
                {
                    var submenu = GetSubMenu(menu, index);
                    if (submenu == 0) continue;
                    var caption = new System.Text.StringBuilder(256);
                    _ = GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400);
                    List<(int Id, string Text)> items = [];
                    for (var childIndex = 0; childIndex < GetMenuItemCount(submenu); childIndex++)
                    {
                        var text = new System.Text.StringBuilder(256);
                        if (GetMenuStringW(submenu, (uint)childIndex, text, text.Capacity, 0x400) > 0 && text.Length > 0)
                            items.Add((unchecked((int)GetMenuItemID(submenu, childIndex)), text.ToString()));
                    }
                    submenus.Add((caption.ToString(), items));
                }
                return submenus;
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }


    private static int FindItemIndex(nint folderView, nint expectedPidl)
    {
        if (At<ItemCount>(folderView, 7)(folderView, 2, out var count) < 0) return -1;
        for (var index = 0; index < count; index++)
        {
            if (At<GetItem>(folderView, 6)(folderView, index, out var itemPidl) < 0 || itemPidl == 0) continue;
            try { if (ILIsEqual(itemPidl, expectedPidl)) return index; }
            finally { Marshal.FreeCoTaskMem(itemPidl); }
        }
        return -1;
    }

    private static bool FocusedItemNameMatches(nint folderView, string expectedName)
    {
        if (At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex) < 0 || focusedIndex < 0 ||
            At<ItemCount>(folderView, 7)(folderView, 2, out var count) < 0 || focusedIndex >= count)
            return false;
        return string.Equals(ReadVisibleItemNames(folderView, count)[focusedIndex], expectedName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static int FindItemIndexByName(nint folderView, string expectedName)
    {
        if (At<ItemCount>(folderView, 7)(folderView, 2, out var count) < 0) return -1;
        var names = ReadVisibleItemNames(folderView, count);
        return Array.FindIndex(names, name => name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static void InvokeBackgroundVerb(nint view, nint owner, string expectedVerb) =>
        Check(InvokeBackgroundVerbRaw(view, owner, expectedVerb));

    private static int InvokeBackgroundVerbRaw(nint view, nint owner, string expectedVerb, uint invokeMask = 0,
        bool canonicalString = false)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu)); // SVGIO_BACKGROUND.
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create the in-memory native Shell menu.");
            try
            {
                var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryHr);
                var commandCount = Math.Min(queryHr & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(512);
                uint? commandOffset = null;
                try
                {
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) == 0 &&
                            string.Equals(Marshal.PtrToStringUni(buffer), expectedVerb, StringComparison.OrdinalIgnoreCase))
                        {
                            commandOffset = offset;
                            break;
                        }
                    }
                    if (commandOffset is null) throw new Exception($"The Shell background menu did not expose the '{expectedVerb}' canonical verb.");
                    if (string.Equals(expectedVerb, "newfolder", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"ishellview-newfolder-map: command-offset={commandOffset.Value}; invocation={(canonicalString ? "ansi-verb" : "offset")}");
                }
                finally { Marshal.FreeHGlobal(buffer); }

                nint ansiVerb = 0;
                try
                {
                    var invoke = new NativeInvokeInfo
                    {
                        Size = Marshal.SizeOf<NativeInvokeInfo>(),
                        Mask = invokeMask,
                        Window = owner,
                        Verb = (nint)commandOffset.Value,
                        Show = 1
                    };
                    if (canonicalString)
                    {
                        ansiVerb = Marshal.StringToCoTaskMemAnsi(expectedVerb);
                        invoke.Verb = ansiVerb;
                    }
                    return At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invoke);
                }
                finally
                {
                    if (ansiVerb != 0) Marshal.FreeCoTaskMem(ansiVerb);
                }
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static IReadOnlyList<string> ReadContextMenuVerbs(nint view, uint itemScope)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, itemScope, ref contextMenuId, out var contextMenu));
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create an in-memory Shell item menu.");
            try
            {
                var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryHr);
                var commandCount = Math.Min(queryHr & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(512);
                try
                {
                    List<string> verbs = [];
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) == 0 &&
                            !string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(buffer)))
                            verbs.Add(Marshal.PtrToStringUni(buffer)!);
                    }
                    return verbs;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static nint FindChildByClass(nint parent, string className)
    {
        nint match = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, name, name.Capacity) > 0 && name.ToString() == className)
            {
                match = window;
                return false;
            }
            return true;
        }, 0);
        return match;
    }

    private static string DescribeWindowChain(nint window)
    {
        var chain = new List<string>();
        for (var depth = 0; window != 0 && depth < 8 && IsWindow(window); depth++)
        {
            var className = new System.Text.StringBuilder(128);
            _ = GetClassNameW(window, className, className.Capacity);
            chain.Add($"0x{window:X}:{className}");
            window = GetParent(window);
        }
        return string.Join(" <- ", chain);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;
        public nint VerbW;
        public nint ParametersW;
        public nint DirectoryW;
        public nint TitleW;
        public System.Drawing.Point InvokePoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSortColumn
    {
        public ShellColumnManager.NativeKey Key;
        public int Direction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHeaderItem
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextCapacity;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint ColumnCount;
        public nint Columns;
        public nint ColumnFormats;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeListViewColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public nint Text;
        public int TextCapacity;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeListViewGroup
    {
        public uint Size;
        public uint Mask;
        public nint Header;
        public int HeaderCapacity;
        public nint Footer;
        public int FooterCapacity;
        public int GroupId;
        public uint StateMask;
        public uint State;
        public uint Align;
        public nint Subtitle;
        public uint SubtitleCapacity;
        public nint Task;
        public uint TaskCapacity;
        public nint DescriptionTop;
        public uint DescriptionTopCapacity;
        public nint DescriptionBottom;
        public uint DescriptionBottomCapacity;
        public int TitleImage;
        public int ExtendedImage;
        public int FirstItem;
        public uint ItemCount;
        public nint SubsetTitle;
        public uint SubsetTitleCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStrRet { public uint Type; public nint Pointer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeShellDetails { public int Format, Width; public NativeStrRet Text; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private sealed record NativeMenuCommandDescriptor(int Id, string Verb, string Caption);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NativeListViewCompare(nint first, nint second, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSyntheticSortCompareCountForColumn(uint column);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUIObjectOf(nint self, nint owner, uint count, nint childPidls, in Guid interfaceId, nint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RenameItem(nint self, nint item, [MarshalAs(UnmanagedType.LPWStr)] string newName, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int NewItem(nint self, nint destinationFolder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItem(nint self, nint item, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumObjects(nint self, nint owner, uint flags, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NextShellIdList(nint self, uint count, out nint element, nint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropTargetDragEnter(nint self, nint dataObject, uint keyState, NativePoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropTargetDragLeave(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropTargetDrop(nint self, nint dataObject, uint keyState, NativePoint point, ref uint effect);
    private sealed record PeerShellRow(int Id, nint AbsolutePidl, nint RelativePidl, string Name, string ParsingName, string? FileSystemPath, string TypeName, int SystemImageIndex,
        string[] DetailTitles, string[] DetailValues)
    {
        internal ShellFileSystemIdentity? FileSystemIdentity { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CallWindowProcResult
    {
        public nint Result, LParam;
        public nuint WParam;
        public uint Message;
        public nint Window;
    }

    private sealed class FilteredHost : StreamViewHost,
        IComDialogBrowser, IComDialogBrowser2, IComDialogBrowser3
    {
        // Keep the isolated host's non-filter browser callbacks aligned with the
        // product site; only IncludeObject remains specialized for this fixture.
        private readonly ShellBrowserSite _browserCallbacks = new() { ShellFolderFilterActive = true };
        internal ShellFolderFilter Filter { get; }
        private int _includeObjectCalls;
        private int _defaultCommandCalls;
        private int _browseObjectCalls;
        private nint _pendingFolder, _pendingView, _pendingWindow;
        internal int IncludeObjectCalls => Volatile.Read(ref _includeObjectCalls);
        internal int DefaultCommandCalls => Volatile.Read(ref _defaultCommandCalls);
        internal int BrowseObjectCalls => Volatile.Read(ref _browseObjectCalls);
        internal long CallbackSelectionVersion => _browserCallbacks.SelectionVersion;
        internal bool BrowseTargetIsChild { get; private set; }
        internal bool BrowseTargetIsRoot { get; private set; }
        internal int HistoryIndex => NavigationHistory!.Index;
        internal nint CurrentPidl => NavigationHistory!.CurrentPidl;
        internal nint PendingView => _pendingView;

        internal FilteredHost(nint parent, ShellFolderFilter filter, nint rootPidl, nint expectedChildPidl,
            Func<nint, uint, int> navigate) : base(parent, [])
        {
            Filter = filter;
            NavigationHistory = new ShellViewNavigationHistory(rootPidl, ClonePidl, Marshal.FreeCoTaskMem,
                PidlExactlyEquals, TryGetParentPidl, ILCombine, (target, flags) =>
                {
                    BrowseTargetIsChild = ILIsEqual(target, expectedChildPidl);
                    BrowseTargetIsRoot = ILIsEqual(target, rootPidl);
                    return navigate(target, flags);
                });
        }

        internal void SetPendingView(nint folder, nint view, nint window)
        {
            _pendingFolder = folder;
            _pendingView = view;
            _pendingWindow = window;
            View = view;
        }

        internal (nint Folder, nint View, nint Window) TakePendingView()
        {
            var result = (_pendingFolder, _pendingView, _pendingWindow);
            _pendingFolder = _pendingView = _pendingWindow = 0;
            return result;
        }

        internal void DisposePendingView()
        {
            if (_pendingView != 0)
            {
                _ = At<DestroyViewWindow>(_pendingView, 10)(_pendingView);
                Marshal.Release(_pendingView);
            }
            if (_pendingFolder != 0) Marshal.Release(_pendingFolder);
            _pendingFolder = _pendingView = _pendingWindow = 0;
        }

        public override int BrowseObject(nint pidl, uint flags)
        {
            Interlocked.Increment(ref _browseObjectCalls);
            return base.BrowseObject(pidl, flags);
        }

        public int OnDefaultCommand(nint shellView)
        {
            Interlocked.Increment(ref _defaultCommandCalls);
            var folderViewId = FolderViewId;
            if (Marshal.QueryInterface(shellView, in folderViewId, out var folderView) < 0) return 0;
            try
            {
                if (At<GetFocusedItem>(folderView, 10)(folderView, out var index) < 0 || index < 0) return 0;
                if (At<GetItem>(folderView, 6)(folderView, index, out var childPidl) < 0 || childPidl == 0) return 0;
                try
                {
                    var absolutePidl = ILCombine(CurrentPidl, childPidl);
                    if (absolutePidl == 0) return 0;
                    try { return BrowseObject(absolutePidl, 0); }
                    finally { Marshal.FreeCoTaskMem(absolutePidl); }
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            finally { Marshal.Release(folderView); }
        }
        public int OnStateChange(nint shellView, uint change) => _browserCallbacks.OnStateChange(shellView, change);
        public int IncludeObject(nint shellView, nint childPidl)
        {
            Interlocked.Increment(ref _includeObjectCalls);
            var folderViewId = FolderViewId;
            var queryHr = Marshal.QueryInterface(shellView, in folderViewId, out var folderView);
            if (queryHr < 0) return 0; // Metadata failure must keep an item visible.
            try
            {
                var shellFolderId = ShellFolderId;
                var getFolderHr = At<GetFolder>(folderView, 5)(folderView, ref shellFolderId, out var shellFolder);
                if (getFolderHr < 0 || shellFolder == 0) return 0;
                try { return Filter.ShouldShow(shellFolder, 0, childPidl); }
                finally { Marshal.Release(shellFolder); }
            }
            finally { Marshal.Release(folderView); }
        }
        public int Notify(nint shellView, uint notification) => _browserCallbacks.Notify(shellView, notification);
        public int GetDefaultMenuText(nint shellView, nint text, int capacity) =>
            _browserCallbacks.GetDefaultMenuText(shellView, text, capacity);
        public int GetViewFlags(out uint flags) => _browserCallbacks.GetViewFlags(out flags);
        public int OnColumnClicked(nint shellView, int column) => _browserCallbacks.OnColumnClicked(shellView, column);
        public int GetCurrentFilter(nint text, int capacity) => _browserCallbacks.GetCurrentFilter(text, capacity);
        public int OnPreViewCreated(nint shellView) => _browserCallbacks.OnPreViewCreated(shellView);

    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous,
        ref StreamProbeSettings settings, nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MenuMessage(nint self, uint message, nint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MenuMessage2(nint self, uint message, nint wParam, nint lParam, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint itemPidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemDisplayName(nint self, uint nameKind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroupBy(nint self, ref ShellColumnManager.NativeKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroupBy(nint self, out ShellColumnManager.NativeKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, [In] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellColumnCount(nint self, uint flags, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint HookProc(int code, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumns(nint self, [Out] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MapColumnToScid(nint self, uint column, out ShellColumnManager.NativeKey key);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributesOf(nint self, uint count, nint itemPidls, nint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFocusedItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TranslateAccelerator(nint self, ref NativeMessage message);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewModeAndIconSize(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetCurrentViewMode(nint self, uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewModeAndIconSize(nint self, out uint mode, out int iconSize);
    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path,
        nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer,
        uint context, ref Guid interfaceId, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(
        string path, nint bind, ref Guid interfaceId, out nint item);
    [DllImport("shell32.dll")] private static extern int SHCreateItemWithParent(nint parentPidl, nint parentFolder,
        nint childPidl, ref Guid iid, out nint item);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind,
        in Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint first, nint second);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")] private static extern bool ILRemoveLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);

    private static nint TryGetParentPidl(nint absolutePidl)
    {
        var parentPidl = ILClone(absolutePidl);
        if (parentPidl == 0 || ILRemoveLastID(parentPidl)) return parentPidl;
        Marshal.FreeCoTaskMem(parentPidl);
        return 0;
    }
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string path1, string? path2);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrRetToBufW(nint strret, nint pidl, System.Text.StringBuilder buffer, uint capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex,
        string cls, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, nint rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendHeaderMessage(nint window, uint message, nint wParam, ref NativeHeaderItem item);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState([In] byte[] state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SetWindowsHookExW(int hookType, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint parameters);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuStringW(nint menu, uint item, System.Text.StringBuilder text, int maxLength, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
}
