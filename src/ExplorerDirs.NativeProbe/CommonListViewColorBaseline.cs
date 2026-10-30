using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Control-only baseline used to separate common-control custom draw behavior from Shell DefView.
internal static class CommonListViewColorBaseline
{
    private const uint WmNotify = 0x004E;
    private const int NmCustomDraw = -12;
    private const uint CddsPrePaint = 0x00000001;
    private const uint CddsItemPrePaint = 0x00010001;
    private const uint CddsItemPostPaint = 0x00010002;
    private const int LvmInsertItemW = 0x104D;
    private const int LvmInsertColumnW = 0x1061;
    private const int LvmSetImageList = 0x1003;
    private const int LvmGetImageList = 0x1002;
    private const int LvsilSmall = 1;
    private const int LvmGetItemW = 0x104B;
    private const int LvmSetItemW = 0x104C;
    private const int LvmGetItemTextW = 0x1073;
    private const int LvmGetNextItem = 0x100C;
    private const int LvmSortItemsEx = 0x1051;
    private const int LvmSetItemState = 0x102B;
    private const int LvmGetItemState = 0x102C;
    private const int LvmSetView = 0x108E;
    private const int LvmRedrawItems = 0x1015;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const uint CdrfNotifyItemDraw = 0x20;
    private const uint CdrfNewFontAndPostPaint = 0x12;
    private static readonly DrawProc Callback = OnDraw;
    private static readonly ListViewCompareProc SizeCompare = CompareBySize;
    private static Dictionary<long, ShellRow>? _shellRowsById;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Dictionary<nuint, uint> Colors = new() { [0] = 0x00F0E0D0, [1] = 0x00D0F0E0 };
    private static readonly Dictionary<nuint, uint> Samples = [];
    private static readonly Dictionary<nuint, string> Writes = [];
    private static readonly Dictionary<nuint, Rect> LabelBounds = [];
    private static nint _listView;
    private static int _prepaint;
    private static int _itemPrepaint;
    private static int _itemPostpaint;

    internal static int Run()
    {
        var desktopBefore = GetForegroundWindow();
        nint parent = 0, listView = 0;
        var parentSubclassed = false;
        var report = new List<string>();
        try
        {
            var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = 1 };
            if (!InitCommonControlsEx(ref init))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ListView common controls initialization failed.");

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs common ListView color baseline",
                0x10000000 | 0x00CF0000, 80, 80, 680, 420, 0, 0, GetModuleHandleW(null), 0);
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Baseline parent creation failed.");
            ShowWindow(parent, 4); // SW_SHOWNOACTIVATE, confined to the private Desktop.
            listView = CreateWindowExW(0, "SysListView32", "",
                0x40000000 | 0x10000000 | 0x00000001 | 0x00000004,
                8, 8, 640, 360, parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Standard ListView creation failed.");
            _listView = listView;
            if (!SetWindowSubclass(parent, Callback, 73, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Baseline custom-draw observer setup failed.");
            parentSubclassed = true;

            AddColumn(listView, "Name", 340);
            AddColumn(listView, "Recursive size", 140);
            InsertItem(listView, 0, "large-folder", 10, -1, "10 B");
            InsertItem(listView, 1, "small-folder", 4, -1, "4 B");
            if (!IsWindowVisible(parent) || !IsWindowVisible(listView))
                Console.WriteLine($"common-listview-color-baseline: initial-visible-state=parent:{IsWindowVisible(parent)},list:{IsWindowVisible(listView)}");

            foreach (var style in new[]
            {
                (Name: "Icon", View: 0), (Name: "Details", View: 1), (Name: "SmallIcon", View: 2),
                (Name: "List", View: 3), (Name: "Tile", View: 4), (Name: "Content", View: 6)
            })
            {
                _prepaint = 0;
                _itemPrepaint = 0;
                _itemPostpaint = 0;
                Samples.Clear();
                Writes.Clear();
                LabelBounds.Clear();
                SendMessageW(listView, LvmSetView, style.View, 0);
                for (var index = 0; index < 2; index++)
                    if (TryGetItemRect(listView, index, 2, out var labelBounds))
                        LabelBounds[(nuint)index] = labelBounds;
                InvalidateRect(listView, 0, true);
                SendMessageW(listView, LvmRedrawItems, 0, 1);
                UpdateWindow(listView);
                PumpMessages();
                var pixels = string.Join(',', Samples.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}=0x{pair.Value:X6}"));
                var fields = string.Join(',', Writes.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
                var rects = string.Join(',', LabelBounds.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}={pair.Value.Left},{pair.Value.Top},{pair.Value.Right},{pair.Value.Bottom}"));
                report.Add($"{style.Name}:view={style.View};prepaint={_prepaint};item-prepaint={_itemPrepaint};item-postpaint={_itemPostpaint};" +
                    $"pixels={pixels};fields={fields};label-rects={rects};visible={IsWindowVisible(listView)}");
            }

            var initialRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            SetItemText(listView, 1, 1, "16 B");
            SetItemParam(listView, 1, 16);
            var liveValue = GetItemText(listView, 1, 1);
            var sortResult = SendMessageW(listView, LvmSortItemsEx, 0,
                Marshal.GetFunctionPointerForDelegate(SizeCompare));
            var sortedRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            var derivedValueUpdatedAndSorted = initialRows == "large-folder:10,small-folder:4" &&
                liveValue == "16 B" && sortResult != 0 && sortedRows == "large-folder:10,small-folder:16";
            InvalidateRect(listView, 0, true);
            UpdateWindow(listView);
            PumpMessages();
            report.Add($"derived-value-update=True;live-cell={liveValue};size-sort=True;rows={sortedRows}");

            var foregroundUnchanged = GetForegroundWindow() == desktopBefore;
            Console.WriteLine($"common-listview-color-baseline: views={string.Join('|', report)}; " +
                $"visible-test=True; foreground-unchanged={foregroundUnchanged}; shell-view=False");
            return foregroundUnchanged && derivedValueUpdatedAndSorted ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("common-listview-color-baseline: " + ex);
            return 1;
        }
        finally
        {
            if (parentSubclassed && parent != 0) RemoveWindowSubclass(parent, Callback, 73);
            if (parent != 0 && IsWindow(parent)) DestroyWindow(parent);
            _listView = 0;
        }
    }

    private static void AddColumn(nint listView, string text, int width)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var column = new ListViewColumn { Mask = 0x0002 | 0x0004, Width = width, Text = textPointer, TextMax = text.Length };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewColumn>());
        try
        {
            Marshal.StructureToPtr(column, memory, false);
            if (SendMessageW(listView, LvmInsertColumnW, 0, memory) == -1)
                throw new Exception("Standard ListView column insertion failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    internal static int RunDataContract()
    {
        var foregroundBefore = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs.ListViewShellRows", Guid.NewGuid().ToString("N"));
        var shellRows = new List<ShellRow>();
        nint parent = 0, listView = 0, fixturePidl = 0, shellFolder = 0;
        try
        {
            var largeFolder = Path.Combine(fixture, "large-folder");
            var smallFolder = Path.Combine(fixture, "small-folder");
            Directory.CreateDirectory(largeFolder);
            Directory.CreateDirectory(smallFolder);
            File.WriteAllBytes(Path.Combine(largeFolder, "payload.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(smallFolder, "payload.bin"), new byte[4]);
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out fixturePidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, fixturePidl, 0, in ShellFolderId, out shellFolder));
            var enumeration = EnumerateShellRows(fixturePidl, shellFolder, new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFullPath(largeFolder)] = 10,
                [Path.GetFullPath(smallFolder)] = 4
            });
            shellRows.AddRange(enumeration.Rows);
            _shellRowsById = shellRows.ToDictionary(row => row.Id);

            var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = 1 };
            if (!InitCommonControlsEx(ref init))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "ListView common controls initialization failed.");
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden ListView data contract",
                0x00CF0000, 0, 0, 400, 200, 0, 0, GetModuleHandleW(null), 0);
            if (parent == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Hidden ListView parent creation failed.");
            listView = CreateWindowExW(0, "SysListView32", "",
                0x40000000 | 0x00000001 | 0x00000008,
                0, 0, 380, 180, parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Hidden standard ListView creation failed.");

            _listView = listView;
            SendMessageW(listView, LvmSetImageList, LvsilSmall, enumeration.SystemImageList);
            var attachedImageList = SendMessageW(listView, LvmGetImageList, LvsilSmall, 0);
            AddColumn(listView, "Name", 240);
            AddColumn(listView, "Recursive size", 100);
            InsertItem(listView, 0, shellRows[0].DisplayName, shellRows[0].Id, shellRows[0].SystemImageIndex, "10 B");
            InsertItem(listView, 1, shellRows[1].DisplayName, shellRows[1].Id, shellRows[1].SystemImageIndex, "4 B");
            var rowIconsBound = shellRows.All(row => Enumerable.Range(0, 2).Any(index =>
                GetItemParam(listView, index) == row.Id && GetItemImage(listView, index) == row.SystemImageIndex));
            var initialRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            var initialShellPaths = string.Join('|', Enumerable.Range(0, 2).Select(index =>
                _shellRowsById[GetItemParam(listView, index)].ParsingName));
            SetItemState(listView, -1, 0, LvisSelected | LvisFocused);
            SetItemState(listView, 1, LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            _shellRowsById[2] = _shellRowsById[2] with { RecursiveBytes = 2 };
            SetItemText(listView, 1, 1, "2 B");
            var liveValue = GetItemText(listView, 1, 1);
            var sortResult = SendMessageW(listView, LvmSortItemsEx, 0,
                Marshal.GetFunctionPointerForDelegate(SizeCompare));
            var sortedRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            var firstState = GetItemState(listView, 0);
            var secondState = GetItemState(listView, 1);
            var selectedFocusedCount = (firstState == (LvisSelected | LvisFocused) ? 1 : 0) +
                (secondState == (LvisSelected | LvisFocused) ? 1 : 0);
            var selectedName = selectedFocusedCount == 1
                ? GetItemText(listView, firstState == (LvisSelected | LvisFocused) ? 0 : 1, 0)
                : "none";
            var selectedId = selectedFocusedCount == 1
                ? GetItemParam(listView, firstState == (LvisSelected | LvisFocused) ? 0 : 1)
                : 0;
            var shellIdentityPreserved = shellRows.All(row => row.AbsolutePidl != 0 &&
                _shellRowsById.TryGetValue(row.Id, out var current) && current.AbsolutePidl == row.AbsolutePidl);
            var shellEnumerationBacked = shellRows.Count == 2 && shellRows.All(row => row.FromShellEnumeration);
            var shellIconsAvailable = shellRows.All(row => row.SystemImageIndex >= 0);
            var shellImageListAttached = enumeration.SystemImageList != 0 && attachedImageList == enumeration.SystemImageList;
            var sortedRowIconsBound = shellRows.All(row => Enumerable.Range(0, 2).Any(index =>
                GetItemParam(listView, index) == row.Id && GetItemImage(listView, index) == row.SystemImageIndex));
            var foregroundBeforeItemMenu = GetForegroundWindow();
            var itemMenu = QueryItemContextMenu(shellFolder, shellRows[0].RelativePidl);
            var foregroundAfterItemMenu = GetForegroundWindow();
            var backgroundMenu = QueryBackgroundContextMenu(shellFolder);
            var foregroundAfterBackgroundMenu = GetForegroundWindow();
            var itemShellMenuAvailable = itemMenu.CommandCount > 0;
            var backgroundShellMenuAvailable = backgroundMenu.CommandCount > 0;
            var itemMenuForegroundUnchanged = foregroundAfterItemMenu == foregroundBeforeItemMenu;
            var backgroundMenuForegroundUnchanged = foregroundAfterBackgroundMenu == foregroundAfterItemMenu;
            var backgroundMenuLabels = backgroundMenu.AllLabels;
            var backgroundMenuContainsView = MenuContainsAny(backgroundMenuLabels, "查看", "视图", "View");
            var backgroundMenuContainsSort = MenuContainsAny(backgroundMenuLabels, "排序方式", "Sort by");
            var backgroundMenuContainsGroup = MenuContainsAny(backgroundMenuLabels, "分组依据", "分组", "Group by");
            var backgroundMenuContainsRefresh = MenuContainsAny(backgroundMenuLabels, "刷新", "Refresh");
            var selectedShellOperations = ExerciseSelectedShellRowOperations(listView, shellFolder, selectedId, fixture);
            var passed = initialRows == "large-folder:1,small-folder:2" &&
                initialShellPaths == $"{largeFolder}|{smallFolder}" && liveValue == "2 B" &&
                sortResult != 0 && sortedRows == "small-folder:2,large-folder:1" &&
                selectedName == "small-folder" && selectedFocusedCount == 1 &&
                selectedId == 2 && shellIdentityPreserved && shellEnumerationBacked && shellIconsAvailable &&
                shellImageListAttached && rowIconsBound && sortedRowIconsBound &&
                itemShellMenuAvailable && backgroundShellMenuAvailable &&
                itemMenuForegroundUnchanged && backgroundMenuForegroundUnchanged &&
                selectedShellOperations &&
                !IsWindowVisible(parent) && !IsWindowVisible(listView) && GetForegroundWindow() == foregroundBefore;
            Console.WriteLine($"common-listview-data-contract: initial={initialRows};shell-paths={initialShellPaths};" +
                $"updated-cell={liveValue};sort-result={sortResult != 0};sorted={sortedRows};" +
                $"selected-focused-after-sort={selectedName};selected-shell-row-id={selectedId};" +
                $"shell-pidl-identities-preserved={shellIdentityPreserved};" +
                $"shell-enumeration-backed={shellEnumerationBacked};" +
                $"shell-icons-available={shellIconsAvailable};" +
                $"system-image-list-attached={shellImageListAttached};row-icons-bound={rowIconsBound};" +
                $"sorted-row-icons-bound={sortedRowIconsBound};" +
                $"item-shell-menu={itemShellMenuAvailable};background-shell-menu={backgroundShellMenuAvailable};" +
                $"item-shell-menu-commands={itemMenu.CommandCount};background-shell-menu-commands={backgroundMenu.CommandCount};" +
                $"item-shell-verbs={string.Join(',', itemMenu.Verbs.Take(12))};" +
                $"background-shell-verbs={string.Join(',', backgroundMenu.Verbs.Take(12))};" +
                $"background-standard-entries=view:{backgroundMenuContainsView},sort:{backgroundMenuContainsSort}," +
                $"group:{backgroundMenuContainsGroup},refresh:{backgroundMenuContainsRefresh};" +
                $"folder-background-root-items={string.Join('|', backgroundMenu.RootLabels)};" +
                $"folder-background-all-items={string.Join('|', backgroundMenu.AllLabels)};" +
                $"selected-row-shell-operations={selectedShellOperations};" +
                $"item-menu-foreground-unchanged={itemMenuForegroundUnchanged};" +
                $"background-menu-foreground-unchanged={backgroundMenuForegroundUnchanged};" +
                $"foreground-handles=0x{foregroundBefore.ToInt64():X}/0x{foregroundAfterItemMenu.ToInt64():X}/0x{foregroundAfterBackgroundMenu.ToInt64():X};" +
                $"selected-focused-count={selectedFocusedCount};visible=False;" +
                $"foreground-unchanged={GetForegroundWindow() == foregroundBefore};passed={passed}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("common-listview-data-contract: " + ex);
            return 1;
        }
        finally
        {
            if (parent != 0 && IsWindow(parent)) DestroyWindow(parent);
            _listView = 0;
            _shellRowsById = null;
            foreach (var row in shellRows)
            {
                if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
                if (row.RelativePidl != 0) ILFree(row.RelativePidl);
            }
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (fixturePidl != 0) Marshal.FreeCoTaskMem(fixturePidl);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            var fixtureRoot = Path.GetDirectoryName(fixture)!;
            if (Directory.Exists(fixtureRoot) && !Directory.EnumerateFileSystemEntries(fixtureRoot).Any())
                Directory.Delete(fixtureRoot);
        }
    }

    internal static int RunNavigationContract()
    {
        var foregroundBefore = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs.ListViewNavigation", Guid.NewGuid().ToString("N"));
        var folderPidls = new List<nint>();
        var enumeratedRows = new List<ShellRow>();
        var boundFolders = new List<nint>();
        try
        {
            var child = Path.Combine(fixture, "child");
            var grandchild = Path.Combine(child, "grandchild");
            var branch = Path.Combine(fixture, "branch");
            Directory.CreateDirectory(grandchild);
            Directory.CreateDirectory(branch);
            File.WriteAllBytes(Path.Combine(fixture, "root.txt"), new byte[3]);
            File.WriteAllBytes(Path.Combine(child, "child.txt"), new byte[4]);
            File.WriteAllBytes(Path.Combine(grandchild, "leaf.txt"), new byte[5]);
            File.WriteAllBytes(Path.Combine(branch, "branch.txt"), new byte[6]);

            var expectedChildren = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [fixture] = ["branch", "child", "root.txt"],
                [child] = ["child.txt", "grandchild"],
                [grandchild] = ["leaf.txt"],
                [branch] = ["branch.txt"]
            };
            var recursiveSizes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFullPath(Path.Combine(fixture, "branch"))] = 6,
                [Path.GetFullPath(Path.Combine(fixture, "child"))] = 9,
                [Path.GetFullPath(Path.Combine(fixture, "root.txt"))] = 3,
                [Path.GetFullPath(Path.Combine(child, "child.txt"))] = 4,
                [Path.GetFullPath(Path.Combine(child, "grandchild"))] = 5,
                [Path.GetFullPath(Path.Combine(grandchild, "leaf.txt"))] = 5,
                [Path.GetFullPath(Path.Combine(branch, "branch.txt"))] = 6
            };

            var enumeratedByLocation = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var location in expectedChildren.Keys)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(location, 0, out var pidl, 0, out _));
                folderPidls.Add(pidl);
                Marshal.ThrowExceptionForHR(SHBindToObject(0, pidl, 0, in ShellFolderId, out var shellFolder));
                boundFolders.Add(shellFolder);
                var expectedAtLocation = expectedChildren[location].ToDictionary(
                    name => Path.GetFullPath(Path.Combine(location, name)),
                    name => recursiveSizes[Path.GetFullPath(Path.Combine(location, name))],
                    StringComparer.OrdinalIgnoreCase);
                var shellRows = EnumerateShellRows(pidl, shellFolder, expectedAtLocation);
                enumeratedRows.AddRange(shellRows.Rows);
                var actualNames = shellRows.Rows.Select(row => row.DisplayName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                var expectedNames = expectedChildren[location].OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                if (!actualNames.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase) ||
                    shellRows.Rows.Any(row => !row.FromShellEnumeration || row.AbsolutePidl == 0 || row.RelativePidl == 0))
                    throw new InvalidOperationException($"Shell row enumeration did not match the fixture at {location}.");
                enumeratedByLocation[location] = actualNames;
            }

            Marshal.ThrowExceptionForHR(SHParseDisplayName(child, 0, out var childPidl, 0, out _));
            folderPidls.Add(childPidl);
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out var rootPidl, 0, out _));
            folderPidls.Add(rootPidl);
            var parentPidl = ILClone(childPidl);
            if (parentPidl == 0) throw new InvalidOperationException("Could not clone the child PIDL to derive its parent.");
            var shellParentMatches = false;
            try
            {
                shellParentMatches = ILRemoveLastID(parentPidl) && ILIsEqual(parentPidl, rootPidl);
            }
            finally { ILFree(parentPidl); }

            var history = new ShellTabNavigationHistory(fixture);
            history.Record(child);
            history.Record(grandchild);
            var backToChild = history.TryBeginMove(back: true, out var backChild) && backChild == child &&
                history.CommitLocation(child);
            var backToRoot = history.TryBeginMove(back: true, out var backRoot) && backRoot == fixture &&
                history.CommitLocation(fixture);
            var forwardToChild = history.TryBeginMove(back: false, out var forwardChild) && forwardChild == child &&
                history.CommitLocation(child);
            var returnedToRootBeforeBranch = history.TryBeginMove(back: true, out backRoot) && backRoot == fixture &&
                history.CommitLocation(fixture);
            history.Record(branch);
            var branchReplacedForwardHistory = returnedToRootBeforeBranch &&
                history.Snapshot().Select(stop => stop.Location).SequenceEqual(new[] { fixture, branch }, StringComparer.OrdinalIgnoreCase);
            var forwardUnavailableAfterBranch = !history.CanGoForward;
            var passed = shellParentMatches && backToChild && backToRoot && forwardToChild &&
                branchReplacedForwardHistory && forwardUnavailableAfterBranch &&
                enumeratedByLocation.Count == expectedChildren.Count && GetForegroundWindow() == foregroundBefore;
            Console.WriteLine($"common-listview-navigation-contract: shell-rows-by-location={enumeratedByLocation.Count};" +
                $"root-rows={string.Join('|', enumeratedByLocation[fixture])};" +
                $"child-rows={string.Join('|', enumeratedByLocation[child])};" +
                $"grandchild-rows={string.Join('|', enumeratedByLocation[grandchild])};" +
                $"branch-rows={string.Join('|', enumeratedByLocation[branch])};" +
                $"shell-parent-pidl-matches={shellParentMatches};back-child={backToChild};back-root={backToRoot};" +
                $"forward-child={forwardToChild};branch-clears-forward-history={branchReplacedForwardHistory};" +
                $"forward-unavailable-after-branch={forwardUnavailableAfterBranch};foreground-unchanged={GetForegroundWindow() == foregroundBefore};passed={passed}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("common-listview-navigation-contract: " + ex);
            return 1;
        }
        finally
        {
            foreach (var row in enumeratedRows)
            {
                if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
                if (row.RelativePidl != 0) ILFree(row.RelativePidl);
            }
            foreach (var shellFolder in boundFolders) Marshal.Release(shellFolder);
            foreach (var pidl in folderPidls) Marshal.FreeCoTaskMem(pidl);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            var fixtureRoot = Path.GetDirectoryName(fixture)!;
            if (Directory.Exists(fixtureRoot) && !Directory.EnumerateFileSystemEntries(fixtureRoot).Any())
                Directory.Delete(fixtureRoot);
        }
    }

    private static ListShellRows EnumerateShellRows(nint folderPidl, nint shellFolder,
        IReadOnlyDictionary<string, ulong> recursiveSizes)
    {
        Marshal.ThrowExceptionForHR(At<EnumObjects>(shellFolder, 4)(shellFolder, 0, 0x60, out var enumerator));
        var rows = new List<ShellRow>();
        nint systemImageList = 0;
        try
        {
            while (true)
            {
                var nextHr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var relativePidl, out var fetched);
                if (nextHr < 0) Marshal.ThrowExceptionForHR(nextHr);
                if (fetched == 0) break;
                if (relativePidl == 0) throw new InvalidOperationException("Shell enumeration returned a null child PIDL.");
                nint absolutePidl = 0;
                nint relativePidlClone = 0;
                try
                {
                    absolutePidl = ILCombine(folderPidl, relativePidl);
                    if (absolutePidl == 0) throw new InvalidOperationException("Could not combine the Shell parent and enumerated child PIDLs.");
                    Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolutePidl, 0, out var displayName));
                    string? display;
                    try { display = Marshal.PtrToStringUni(displayName); }
                    finally { Marshal.FreeCoTaskMem(displayName); }
                    if (string.IsNullOrWhiteSpace(display)) throw new InvalidOperationException("Shell returned an empty normal display name.");
                    Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolutePidl, 0x80058000, out var parsingName));
                    string? parsing;
                    try { parsing = Marshal.PtrToStringUni(parsingName); }
                    finally { Marshal.FreeCoTaskMem(parsingName); }
                    var fullPath = Path.GetFullPath(parsing ?? string.Empty);
                    if (!recursiveSizes.TryGetValue(fullPath, out var recursiveBytes))
                        throw new InvalidOperationException($"Shell enumerated an unexpected fixture child: {fullPath}");
                    var systemImageIndex = GetShellSystemImageIndex(fullPath, ref systemImageList);
                    relativePidlClone = ILClone(relativePidl);
                    if (relativePidlClone == 0) throw new InvalidOperationException("Could not retain the enumerated relative PIDL for native Shell commands.");
                    rows.Add(new ShellRow(0, absolutePidl, relativePidlClone, display, parsing!, recursiveBytes,
                        FromShellEnumeration: true, SystemImageIndex: systemImageIndex));
                    absolutePidl = 0;
                    relativePidlClone = 0;
                }
                finally
                {
                    if (absolutePidl != 0) ILFree(absolutePidl);
                    if (relativePidlClone != 0) ILFree(relativePidlClone);
                    ILFree(relativePidl);
                }
            }
            rows.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
            if (rows.Count != recursiveSizes.Count)
                throw new InvalidOperationException($"Shell enumeration returned {rows.Count} rows; expected {recursiveSizes.Count}.");
            for (var index = 0; index < rows.Count; index++)
                rows[index] = rows[index] with { Id = index + 1 };
            return new ListShellRows(rows, systemImageList);
        }
        catch
        {
            foreach (var row in rows)
            {
                if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
                if (row.RelativePidl != 0) ILFree(row.RelativePidl);
            }
            throw;
        }
        finally { Marshal.Release(enumerator); }
    }

    private static int GetShellSystemImageIndex(string path, ref nint imageList)
    {
        var result = SHGetFileInfo(path, 0, out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), 0x4000 | 0x1); // SHGFI_SYSICONINDEX | SHGFI_SMALLICON.
        if (result == 0 || info.ImageIndex < 0)
            throw new InvalidOperationException($"Shell did not return a system icon for {path}.");
        if (imageList == 0) imageList = result;
        else if (imageList != result) throw new InvalidOperationException("Shell returned different shared image lists for one view.");
        return info.ImageIndex;
    }

    private static ContextMenuQuery QueryItemContextMenu(nint shellFolder, nint relativePidl)
    {
        var childArray = Marshal.AllocHGlobal(IntPtr.Size);
        nint contextMenu = 0, menu = 0;
        try
        {
            Marshal.WriteIntPtr(childArray, relativePidl);
            Marshal.ThrowExceptionForHR(At<GetUiObjectOf>(shellFolder, 10)(shellFolder, 0, 1,
                childArray, in ContextMenuId, 0, out contextMenu));
            return QueryContextMenu(contextMenu, out menu);
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
            Marshal.FreeHGlobal(childArray);
        }
    }

    private static ContextMenuQuery QueryBackgroundContextMenu(nint shellFolder)
    {
        nint contextMenu = 0, menu = 0;
        try
        {
            Marshal.ThrowExceptionForHR(At<CreateViewObject>(shellFolder, 8)(shellFolder, 0, in ContextMenuId, out contextMenu));
            return QueryContextMenu(contextMenu, out menu);
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
        }
    }

    private static bool ExerciseSelectedShellRowOperations(nint listView, nint shellFolder, long selectedRowId, string fixture)
    {
        var selectedIndex = SendMessageW(listView, LvmGetNextItem, -1, (nint)LvisSelected).ToInt32();
        if (selectedIndex < 0 || GetItemParam(listView, selectedIndex) != selectedRowId)
            throw new InvalidOperationException("The selected ListView row no longer maps to the expected Shell identity.");
        if (selectedRowId <= 0 || _shellRowsById is null ||
            !_shellRowsById.TryGetValue(selectedRowId, out var row) || row.AbsolutePidl == 0)
            throw new InvalidOperationException("The selected ListView row has no retained Shell absolute PIDL.");
        var selectedRows = _shellRowsById.Values.OrderBy(candidate => candidate.Id).ToArray();
        if (selectedRows.Length != 2 || selectedRows.All(candidate => candidate.Id != selectedRowId))
            throw new InvalidOperationException("The multi-selection Shell data-object fixture must contain exactly two enumerated rows.");

        SetItemState(listView, -1, 0, LvisSelected | LvisFocused);
        for (var index = 0; index < selectedRows.Length; index++)
        {
            SetItemState(listView, index, LvisSelected, LvisSelected);
            if (GetItemParam(listView, index) == selectedRowId)
                SetItemState(listView, index, LvisFocused, LvisFocused);
        }
        var selectedRowItems = Enumerable.Range(0, selectedRows.Length)
            .Where(index => (GetItemState(listView, index) & LvisSelected) != 0)
            .Select(index => _shellRowsById[GetItemParam(listView, index)])
            .ToArray();
        if (selectedRowItems.Length != 2 || selectedRowItems.All(candidate => candidate.Id != selectedRowId))
            throw new InvalidOperationException("The ListView did not retain the expected two selected Shell rows.");

        var destinationPath = Path.Combine(fixture, "copy-destination");
        var moveDestinationPath = Path.Combine(fixture, "move-destination");
        Directory.CreateDirectory(destinationPath);
        Directory.CreateDirectory(moveDestinationPath);
        var foregroundBeforeOperations = GetForegroundWindow();
        var itemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        var childArray = Marshal.AllocHGlobal(IntPtr.Size * selectedRowItems.Length);
        nint destinationItem = 0, operation = 0, moveSourceItem = 0, moveDestinationItem = 0, moveOperation = 0,
            renameItem = 0, renameOperation = 0, deleteItem = 0, deleteOperation = 0, selectedDataObject = 0;
        try
        {
            for (var index = 0; index < selectedRowItems.Length; index++)
                Marshal.WriteIntPtr(childArray, index * IntPtr.Size, selectedRowItems[index].RelativePidl);
            Marshal.ThrowExceptionForHR(At<GetUiObjectOf>(shellFolder, 10)(shellFolder, 0, (uint)selectedRowItems.Length,
                childArray, in DataObjectId, 0, out selectedDataObject));
            if (selectedDataObject == 0) return false;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(destinationPath, 0, ref itemId, out destinationItem));
            var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, 0, 1, ref interfaceId, out operation));
            const uint flags = 0x0004 | 0x0010 | 0x0400 | 0x00800000; // Silent, no confirmation/error UI, no copy hooks.
            Marshal.ThrowExceptionForHR(At<SetOperationFlags>(operation, 5)(operation, flags));
            Marshal.ThrowExceptionForHR(At<CopyItems>(operation, 17)(operation, selectedDataObject, destinationItem, 0));
            Marshal.ThrowExceptionForHR(At<PerformOperations>(operation, 21)(operation));
            Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            var copiedFolder = Path.Combine(destinationPath, Path.GetFileName(row.ParsingName));
            var copiedPayload = Path.Combine(copiedFolder, "payload.bin");
            var sourcePayload = Path.Combine(row.ParsingName, "payload.bin");
            var sourceLength = File.Exists(sourcePayload) ? new FileInfo(sourcePayload).Length : -1;
            var copiedLength = File.Exists(copiedPayload) ? new FileInfo(copiedPayload).Length : -1;
            // RecursiveBytes was deliberately changed above for the sort check; compare the Shell copy with its disk source instead.
            var allSelectedRowsCopied = selectedRowItems.All(selectedRow =>
            {
                var selectedSource = Path.Combine(selectedRow.ParsingName, "payload.bin");
                var selectedCopy = Path.Combine(destinationPath, Path.GetFileName(selectedRow.ParsingName), "payload.bin");
                return File.Exists(selectedSource) && File.Exists(selectedCopy) &&
                    File.ReadAllBytes(selectedSource).SequenceEqual(File.ReadAllBytes(selectedCopy));
            });
            var copied = aborted == 0 && allSelectedRowsCopied && sourceLength >= 0 && copiedLength == sourceLength &&
                File.ReadAllBytes(sourcePayload).SequenceEqual(File.ReadAllBytes(copiedPayload));
            var foregroundAfterCopy = GetForegroundWindow();
            if (!copied || !File.Exists(sourcePayload)) return false;

            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(copiedFolder, 0, ref itemId, out moveSourceItem));
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(moveDestinationPath, 0, ref itemId, out moveDestinationItem));
            var moveClassId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var moveInterfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref moveClassId, 0, 1, ref moveInterfaceId, out moveOperation));
            Marshal.ThrowExceptionForHR(At<SetOperationFlags>(moveOperation, 5)(moveOperation, flags));
            Marshal.ThrowExceptionForHR(At<MoveItem>(moveOperation, 14)(moveOperation, moveSourceItem,
                moveDestinationItem, "moved-selected-folder", 0));
            Marshal.ThrowExceptionForHR(At<PerformOperations>(moveOperation, 21)(moveOperation));
            Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(moveOperation, 22)(moveOperation, out var moveAborted));
            var movedPayload = Path.Combine(moveDestinationPath, "moved-selected-folder", "payload.bin");
            var moved = moveAborted == 0 && File.Exists(sourcePayload) && !Directory.Exists(copiedFolder) && File.Exists(movedPayload) &&
                new FileInfo(movedPayload).Length == sourceLength &&
                File.ReadAllBytes(sourcePayload).SequenceEqual(File.ReadAllBytes(movedPayload));
            var foregroundAfterMove = GetForegroundWindow();
            if (!moved) return false;

            var movedFolder = Path.Combine(moveDestinationPath, "moved-selected-folder");
            var renamedFolder = Path.Combine(moveDestinationPath, "renamed-selected-folder");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(movedFolder, 0, ref itemId, out renameItem));
            var renameClassId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var renameInterfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref renameClassId, 0, 1, ref renameInterfaceId, out renameOperation));
            Marshal.ThrowExceptionForHR(At<SetOperationFlags>(renameOperation, 5)(renameOperation, flags));
            Marshal.ThrowExceptionForHR(At<RenameItem>(renameOperation, 12)(renameOperation, renameItem,
                "renamed-selected-folder", 0));
            Marshal.ThrowExceptionForHR(At<PerformOperations>(renameOperation, 21)(renameOperation));
            Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(renameOperation, 22)(renameOperation, out var renameAborted));
            var renamedPayload = Path.Combine(renamedFolder, "payload.bin");
            var renamed = renameAborted == 0 && File.Exists(sourcePayload) && !Directory.Exists(movedFolder) &&
                File.Exists(renamedPayload) && new FileInfo(renamedPayload).Length == sourceLength &&
                File.ReadAllBytes(sourcePayload).SequenceEqual(File.ReadAllBytes(renamedPayload));
            var foregroundAfterRename = GetForegroundWindow();
            if (!renamed) return false;

            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(renamedFolder, 0, ref itemId, out deleteItem));
            var deleteClassId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var deleteInterfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref deleteClassId, 0, 1, ref deleteInterfaceId, out deleteOperation));
            Marshal.ThrowExceptionForHR(At<SetOperationFlags>(deleteOperation, 5)(deleteOperation, flags));
            Marshal.ThrowExceptionForHR(At<DeleteItem>(deleteOperation, 18)(deleteOperation, deleteItem, 0));
            Marshal.ThrowExceptionForHR(At<PerformOperations>(deleteOperation, 21)(deleteOperation));
            Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(deleteOperation, 22)(deleteOperation, out var deleteAborted));
            var deleted = deleteAborted == 0 && !Directory.Exists(renamedFolder) && !File.Exists(renamedPayload);
            var foregroundAfterDelete = GetForegroundWindow();
            Console.WriteLine($"common-listview-shell-operation: selected={row.DisplayName};copy-aborted={aborted};" +
                $"move-aborted={moveAborted};rename-aborted={renameAborted};delete-aborted={deleteAborted};copy-payload-matches-source={copied};" +
                $"copy-items-source=selected-Shell-IDataObject;selected-row-count={selectedRowItems.Length};all-selected-rows-copied={allSelectedRowsCopied};" +
                $"move-payload-matches-source={moved};rename-payload-matches-source={renamed};" +
                $"delete-removed={deleted};source-preserved={File.Exists(sourcePayload)};copy-source-removed={moved};move-source-renamed={renamed};" +
                $"foreground=0x{foregroundBeforeOperations.ToInt64():X}/0x{foregroundAfterCopy.ToInt64():X}/" +
                $"0x{foregroundAfterMove.ToInt64():X}/0x{foregroundAfterRename.ToInt64():X}/0x{foregroundAfterDelete.ToInt64():X}");
            return renamed && deleted;
        }
        finally
        {
            if (deleteOperation != 0) Marshal.Release(deleteOperation);
            if (deleteItem != 0) Marshal.Release(deleteItem);
            if (selectedDataObject != 0) Marshal.Release(selectedDataObject);
            if (renameOperation != 0) Marshal.Release(renameOperation);
            if (renameItem != 0) Marshal.Release(renameItem);
            if (moveOperation != 0) Marshal.Release(moveOperation);
            if (moveDestinationItem != 0) Marshal.Release(moveDestinationItem);
            if (moveSourceItem != 0) Marshal.Release(moveSourceItem);
            if (operation != 0) Marshal.Release(operation);
            if (destinationItem != 0) Marshal.Release(destinationItem);
            Marshal.FreeHGlobal(childArray);
        }
    }

    private static ContextMenuQuery QueryContextMenu(nint contextMenu, out nint menu)
    {
        menu = CreatePopupMenu();
        if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Native Shell menu handle creation failed.");
        var result = At<QueryContextMenuNative>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x0800);
        Marshal.ThrowExceptionForHR(result);
        var commandCount = (uint)(result & 0xffff);
        return new ContextMenuQuery(commandCount, GetContextMenuVerbs(contextMenu, commandCount),
            GetRootMenuLabels(menu), GetAllMenuLabels(menu));
    }

    private static string[] GetRootMenuLabels(nint menu)
    {
        var labels = new List<string>();
        var count = GetMenuItemCount(menu);
        for (var position = 0; position < count; position++)
        {
            var text = new System.Text.StringBuilder(512);
            if (GetMenuStringW(menu, (uint)position, text, text.Capacity, 0x400) > 0)
                labels.Add(text.ToString());
        }
        return labels.ToArray();
    }

    private static string[] GetAllMenuLabels(nint menu)
    {
        var labels = new List<string>();
        var visited = new HashSet<nint>();
        AppendMenuLabels(menu, string.Empty, labels, visited);
        return labels.ToArray();
    }

    private static void AppendMenuLabels(nint menu, string parentPath, List<string> labels, HashSet<nint> visited)
    {
        if (menu == 0 || !visited.Add(menu)) return;
        var count = GetMenuItemCount(menu);
        for (var position = 0; position < count; position++)
        {
            var text = new System.Text.StringBuilder(512);
            _ = GetMenuStringW(menu, (uint)position, text, text.Capacity, 0x400);
            var item = text.ToString();
            var path = string.IsNullOrEmpty(parentPath) ? item : parentPath + " > " + item;
            if (!string.IsNullOrWhiteSpace(item)) labels.Add(path);
            AppendMenuLabels(GetSubMenu(menu, position), path, labels, visited);
        }
    }

    private static string[] GetContextMenuVerbs(nint contextMenu, uint commandCount)
    {
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            var verbs = new List<string>();
            for (uint offset = 0; offset < commandCount; offset++)
            {
                Marshal.WriteInt16(buffer, 0);
                Marshal.WriteInt16(buffer, 1022, 0);
                if (At<GetCommandString>(contextMenu, 5)(contextMenu, offset, 4, 0, buffer, 512) != 0) continue;
                var verb = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrWhiteSpace(verb)) verbs.Add(verb);
            }
            return verbs.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool MenuContainsAny(IEnumerable<string> labels, params string[] candidates) =>
        labels.Any(label => candidates.Any(candidate => NormalizeMenuLabel(label)
            .StartsWith(candidate, StringComparison.OrdinalIgnoreCase)));

    private static string NormalizeMenuLabel(string label)
    {
        var normalized = label.Replace("&", string.Empty, StringComparison.Ordinal).Trim();
        var accelerator = normalized.LastIndexOf('(');
        if (accelerator > 0 && normalized.EndsWith(')') && normalized.Length - accelerator <= 4)
            normalized = normalized[..accelerator].TrimEnd();
        return normalized;
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private static void InsertItem(nint listView, int index, string text, long size, int imageIndex, string sizeText)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var item = new ListViewItem { Mask = 0x0001 | 0x0002 | 0x0004, Item = index, Text = textPointer,
            TextMax = text.Length, Image = imageIndex, Param = (nint)size };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmInsertItemW, 0, memory).ToInt32() != index)
                throw new Exception($"Standard ListView item {index} insertion failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
        SetItemText(listView, index, 1, sizeText);
    }

    private static void SetItemText(nint listView, int index, int subItem, string text)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var item = new ListViewItem { Mask = 0x0001, Item = index, SubItem = subItem,
            Text = textPointer, TextMax = text.Length };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmSetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index}, subitem {subItem} update failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void SetItemParam(nint listView, int index, long value)
    {
        var item = new ListViewItem { Mask = 0x0004, Item = index, Param = (nint)value };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmSetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index} derived-value update failed.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string GetItemText(nint listView, int index, int subItem)
    {
        var buffer = Marshal.AllocHGlobal(256 * sizeof(char));
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var item = new ListViewItem { Item = index, SubItem = subItem, Text = buffer, TextMax = 256 };
            Marshal.StructureToPtr(item, memory, false);
            SendMessageW(listView, LvmGetItemTextW, index, memory);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long GetItemParam(nint listView, int index)
    {
        var item = new ListViewItem { Mask = 0x0004, Item = index };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmGetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index} data read failed.");
            return Marshal.PtrToStructure<ListViewItem>(memory).Param.ToInt64();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static int GetItemImage(nint listView, int index)
    {
        var item = new ListViewItem { Mask = 0x0002, Item = index, Image = -1 };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmGetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index} image query failed.");
            return Marshal.PtrToStructure<ListViewItem>(memory).Image;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static uint GetItemState(nint listView, int index) =>
        unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)));

    private static void SetItemState(nint listView, int index, uint state, uint stateMask)
    {
        var item = new ListViewItem { State = state, StateMask = stateMask };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            SendMessageW(listView, LvmSetItemState, index, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static int CompareBySize(nint firstIndex, nint secondIndex, nint sortParameter)
    {
        var first = GetItemParam(_listView, checked((int)firstIndex.ToInt64()));
        var second = GetItemParam(_listView, checked((int)secondIndex.ToInt64()));
        if (_shellRowsById is not null)
        {
            first = checked((long)_shellRowsById[first].RecursiveBytes);
            second = checked((long)_shellRowsById[second].RecursiveBytes);
        }
        return first.CompareTo(second);
    }

    private static nint OnDraw(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmNotify && lParam != 0)
        {
            var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
            if (header.Code == NmCustomDraw && header.Window == _listView)
            {
                var draw = Marshal.PtrToStructure<CustomDraw>(lParam);
                if (draw.Stage == CddsPrePaint)
                {
                    _prepaint++;
                    return (nint)CdrfNotifyItemDraw;
                }
                if (draw.Stage == CddsItemPrePaint)
                {
                    _itemPrepaint++;
                    var color = Colors.GetValueOrDefault(draw.Item, 0x00E0E0E0u);
                    Marshal.WriteInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextColor)).ToInt32(), 0x00202020);
                    Marshal.WriteInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextBackground)).ToInt32(), unchecked((int)color));
                    Writes[draw.Item] = $"text=0x{Marshal.ReadInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextColor)).ToInt32()):X6},background=0x{Marshal.ReadInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextBackground)).ToInt32()):X6}";
                    return (nint)CdrfNewFontAndPostPaint;
                }
                if (draw.Stage == CddsItemPostPaint)
                {
                    _itemPostpaint++;
                    var bounds = draw.Bounds.Right > draw.Bounds.Left && draw.Bounds.Bottom > draw.Bounds.Top
                        ? draw.Bounds : LabelBounds.GetValueOrDefault(draw.Item);
                    if (draw.DeviceContext != 0 && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                    {
                        var x = (bounds.Left + bounds.Right) / 2;
                        var y = (bounds.Top + bounds.Bottom) / 2;
                        Samples[draw.Item] = GetPixel(draw.DeviceContext, x, y);
                    }
                }
            }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static void PumpMessages()
    {
        var message = new Message();
        while (PeekMessageW(out message, 0, 0, 0, 1))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static bool TryGetItemRect(nint listView, int index, int part, out Rect rect)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.WriteInt32(memory, part); // LVIR_LABEL.
            if (SendMessageW(listView, 0x100E, index, memory) == 0)
            {
                rect = default;
                return false;
            }
            rect = Marshal.PtrToStructure<Rect>(memory);
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint DrawProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ListViewCompareProc(nint first, nint second, nint sortParameter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumObjects(nint self, nint owner, uint flags, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumNext(nint self, uint count, out nint item, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUiObjectOf(nint self, nint owner, uint count, nint childPidls, in Guid iid, nint reserved, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, in Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenuNative(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint buffer, uint maxLength);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyItems(nint self, nint items, nint destination, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int MoveItem(nint self, nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RenameItem(nint self, nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItem(nint self, nint item, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [StructLayout(LayoutKind.Sequential)] private struct InitCommonControls { public uint Size; public uint Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CustomDraw
    {
        public NotifyHeader Header; public uint Stage; public nint DeviceContext; public Rect Bounds;
        public nuint Item; public uint ItemState; public nint ItemData; public uint TextColor; public uint TextBackground;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListViewItem
    {
        public uint Mask; public int Item, SubItem; public uint State, StateMask; public nint Text; public int TextMax, Image;
        public nint Param; public int Indent, GroupId, Columns; public nint ColumnIndices, ColumnFormat, Group;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListViewColumn
    {
        public uint Mask; public int Format, Width; public nint Text; public int TextMax, SubItem, Image, Order;
        public int MinWidth, DefaultWidth, IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y;
        public uint Private;
    }
    private sealed record ShellRow(long Id, nint AbsolutePidl, nint RelativePidl, string DisplayName, string ParsingName,
        ulong RecursiveBytes, bool FromShellEnumeration = false, int SystemImageIndex = -1);
    private sealed record ListShellRows(List<ShellRow> Rows, nint SystemImageList);
    private sealed record ContextMenuQuery(uint CommandCount, string[] Verbs, string[] RootLabels, string[] AllLabels);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon;
        public int ImageIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName;
    }
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, DrawProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, DrawProc callback, nuint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rect, bool erase);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHCreateItemFromIDList(nint pidl, ref Guid interfaceId, out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string name, nint bindContext, ref Guid interfaceId, out nint item);
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint instance);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint result);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern bool ILRemoveLastID(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern void ILFree(nint pidl);
    [DllImport("user32.dll", PreserveSig = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", PreserveSig = true)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW", PreserveSig = true)] private static extern int GetMenuStringW(nint menu, uint item, System.Text.StringBuilder text, int maxLength, uint flags);
    [DllImport("user32.dll", PreserveSig = true)] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", PreserveSig = true)] private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
