using System.Runtime.InteropServices;
using System.Diagnostics;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

// Isolated architecture spike: test whether the stock DefView used by IShellView
// exposes a supported item-color hook while retaining native Shell operations.
// The default host and fixture are hidden and temporary; no ExplorerBrowser or input is used.
internal static class ShellViewColorSpike
{
    private const uint ShellRenameItem = 0x00000001;
    private const uint ShellCreate = 0x00000002;
    private const uint ShellDelete = 0x00000004;
    private const uint PathAndFlushNoWait = 0x00002005;
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid FolderViewOptionsId = new("3CC974D2-B302-4D36-AD3E-06D93F695D3F");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid FolderFilterSiteId = new("C0A651F5-B48B-11D2-B5ED-006097C686F6");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly SubclassProc DrawObserver = ObserveCustomDraw;
    private static readonly Dictionary<nuint, Rect> CustomDrawBounds = [];
    private static readonly Dictionary<nuint, nint> CustomDrawDeviceContexts = [];
    private static readonly Dictionary<nuint, uint> CustomDrawDeviceContextTypes = [];
    private static readonly Dictionary<nuint, uint> CustomDrawItemStates = [];
    private static ShellListSelectionSnapshot _customDrawSelection = ShellListSelectionSnapshot.Empty();
    private static readonly Dictionary<nuint, uint> CustomDrawPostPaintPixels = [];
    private static readonly Dictionary<nuint, string> CustomDrawColors = [];
    private static readonly Dictionary<nuint, Rect> CustomDrawLabelBounds = [];
    private static readonly Dictionary<nuint, uint> CustomDrawBackgrounds = [];
    private static readonly Dictionary<nuint, uint> CustomDrawTextColors = [];
    private static readonly Dictionary<nuint, ShellItemDrawFontStyle> CustomDrawFontStyles = [];
    private static readonly Dictionary<nuint, ShellViewFontSelection> CustomDrawFontSelections = [];
    private static bool _readOnlyRuleProbe;
    private static readonly List<string> CustomDrawEventDetails = [];
    private static nint _observedListView;
    private static int _customDrawPrepaint;
    private static int _customDrawItems;

    internal static int RunSessionRestoreConsumer(string fixture, string stateDirectory, Guid sessionId,
        bool terminateProcessAfterProbe = false)
    {
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullFixture = Path.GetFullPath(fixture);
        var fullStateDirectory = Path.GetFullPath(stateDirectory);
        if (!fullFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fullStateDirectory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("session-restore-consumer: fixture and state store must remain under NativeProbe fixtures.");
            return 1;
        }

        var store = new ShellViewStateStore(fullStateDirectory);
        if (!store.TryLoad(sessionId, out var persistedState))
        {
            Console.Error.WriteLine("session-restore-consumer: session state was not present before this process started.");
            return 1;
        }

        nint parent = 0, pane = 0, pidl = 0, folder = 0, view = 0, hostPointer = 0, viewWindow = 0, folderView = 0;
        var createAttempted = false;
        ShellViewColorBrowserHost? host = null;
        var result = 1;
        try
        {
            var windowStyle = 0x10CF0000u | 0x10000000u;
            var extendedStyle = 0x08000000u | 0x00000080u;
            parent = CreateWindowExW(extendedStyle, "STATIC", "ExplorerDirs session restore consumer",
                windowStyle, 100, 100, 800, 560, 0, 0, 0, 0);
            if (parent == 0) throw new InvalidOperationException("Could not create the isolated consumer parent window.");
            pane = CreateWindowExW(0, "STATIC", "ExplorerDirs session restore pane 1",
                0x50000000u | 0x02000000u | 0x04000000u, 0, 0, 390, 265, parent, 0, 0, 0);
            if (pane == 0) throw new InvalidOperationException("Could not create the first child pane.");
            Check(SHParseDisplayName(fullFixture, 0, out pidl, 0, out _));
            Check(SHBindToObject(0, pidl, 0, in ShellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, pane, ref shellViewId, out view));
            host = new ShellViewColorBrowserHost(pane, [],
                new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
                stateStore: store, stateId: sessionId);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 380, Bottom = 235 };
            createAttempted = true;
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            if (viewWindow == 0) throw new InvalidOperationException("The consumer view was created without a window.");
            if (GetParent(pane) != parent || GetParent(viewWindow) != pane)
                throw new InvalidOperationException("The restored Shell view is not hosted inside its first child pane.");
            ShowWindow(parent, 4); // SW_SHOWNOACTIVATE on the isolated desktop.
            Check(At<ActivateView>(view, 7)(view, 1)); // SVUIA_ACTIVATE_NOFOCUS.
            Pump(1000);
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            _ = WaitForShellViewItemCount(folderView, expectedCount: 2);
            Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var mode, out _));
            Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var sortCount));
            if (sortCount != 1) throw new Exception($"Restored sort count was {sortCount}, expected one.");
            var sorts = new SortColumn[sortCount];
            Check(At<GetSortColumns>(folderView, 28)(folderView, sorts, sortCount));
            var propertyFormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var expectedSort = new SortColumn
            {
                Key = new PropertyKey { FormatId = propertyFormatId, PropertyId = 10 },
                Direction = -1
            };
            var groupResult = At<GetGroupBy>(folderView, 18)(folderView, out var group, out var groupAscending);
            Check(groupResult);
            Check(ShellColumnManager.Capture(folderView, out var columns));
            var columnsExpected = new DisplayColumnState[]
            {
                new(new PropertyKeyState(propertyFormatId, 10), 280),
                new(new PropertyKeyState(propertyFormatId, 12), 120),
                new(new PropertyKeyState(propertyFormatId, 4), 160)
            };
            var restored = mode == 4 && sorts[0].Key.FormatId == expectedSort.Key.FormatId &&
                sorts[0].Key.PropertyId == expectedSort.Key.PropertyId && sorts[0].Direction == expectedSort.Direction &&
                group.FormatId == propertyFormatId && group.PropertyId == 4 && groupAscending == 1 &&
                columns is not null && columnsExpected.SequenceEqual(columns) && host.Reads > 0;
            if (!restored) throw new Exception("A new process did not restore the persisted Shell view settings.");
            var concurrentViews = VerifyConcurrentViewIsolation(fullFixture, store, sessionId, parent, view, pane, host,
                folderView, propertyFormatId, expectedSort, columnsExpected);
            Console.WriteLine($"session-restore-consumer: pid={Environment.ProcessId}; session={sessionId:D}; " +
                $"prior-state-bytes={persistedState.Length}; shell-reads={host.Reads}; mode={mode}; sort={sorts.Length}; " +
                $"group={groupResult:X8}/{groupAscending}; columns={columns?.Count ?? 0}; restored={restored}; " +
                $"four-pane-probe={concurrentViews}");
            result = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("session-restore-consumer failure: " + ex.Message);
        }
        finally
        {
            if (terminateProcessAfterProbe)
            {
                // This helper process owns its isolated Shell Desktop; releasing the STA view can stall.
                // Its parent removes the temporary fixture and state directory after this process exits.
                Console.Out.Flush();
                Console.Error.Flush();
                Environment.Exit(result);
            }
            if (folderView != 0) Marshal.Release(folderView);
            if (view != 0)
            {
                if (createAttempted)
                {
                    try { _ = At<DestroyViewWindow>(view, 10)(view); }
                    catch (Exception ex) { Console.Error.WriteLine("session-restore-consumer destroy: " + ex.Message); }
                }
                if (host is not null) host.View = 0;
                Marshal.Release(view);
            }
            if (folder != 0) Marshal.Release(folder);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (host is not null) host.Dispose();
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (parent != 0 && IsWindow(parent)) _ = DestroyWindow(parent);
            Console.WriteLine($"session-restore-consumer: result={result}; parent-visible={parent != 0 && IsWindowVisible(parent)}");
        }
        return result;
    }

    private static string VerifyConcurrentViewIsolation(string fixture, ShellViewStateStore store,
        Guid restoredSessionId, nint containerParent, nint restoredView, nint firstPane,
        ShellViewColorBrowserHost restoredHost,
        nint restoredFolderView, Guid propertyFormatId, SortColumn expectedSort,
        IReadOnlyList<DisplayColumnState> columnsExpected)
    {
        // The calling consumer exits immediately after this check; process exit owns the isolated Shell STA teardown.
        nint pane = 0, pidl = 0, folder = 0, view = 0, hostPointer = 0, viewWindow = 0, folderView = 0;
        ShellViewColorBrowserHost? host = null;
        pane = CreateWindowExW(0, "STATIC", "ExplorerDirs session restore pane 2",
            0x50000000u | 0x02000000u | 0x04000000u, 400, 0, 390, 265, containerParent, 0, 0, 0);
        if (pane == 0) throw new InvalidOperationException("Could not create the second child pane.");
        if (GetParent(firstPane) != containerParent || GetParent(pane) != containerParent || firstPane == pane)
            throw new InvalidOperationException("The two Shell pane hosts are not distinct children of one container.");
        if (!store.TryLoad(restoredSessionId, out var restoredState))
            throw new InvalidOperationException("The restored view session disappeared before its sibling was created.");
        Check(SHParseDisplayName(fixture, 0, out pidl, 0, out _));
        Check(SHBindToObject(0, pidl, 0, in ShellFolderId, out folder));
        var shellViewId = ShellViewId;
        Check(At<CreateViewObject>(folder, 8)(folder, pane, ref shellViewId, out view));
        var siblingSessionId = Guid.NewGuid();
        host = new ShellViewColorBrowserHost(pane, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
            stateStore: store, stateId: siblingSessionId);
        if (store.TryLoad(siblingSessionId, out _))
            throw new InvalidOperationException("The new sibling unexpectedly inherited another session's stream.");
        host.View = view;
        hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        var settings = new StreamProbeSettings { Mode = uint.MaxValue };
        var bounds = new StreamProbeRect { Right = 380, Bottom = 235 };
        Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
        if (viewWindow == 0) throw new InvalidOperationException("The second Shell view was created without a window.");
        if (GetParent(viewWindow) != pane)
            throw new InvalidOperationException("The sibling Shell view is not hosted inside its child pane.");
        Check(At<ActivateView>(view, 7)(view, 1)); // SVUIA_ACTIVATE_NOFOCUS.
        Pump(700);
        Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
        _ = WaitForShellViewItemCount(folderView, expectedCount: 2);
        if (host.Reads == 0)
            throw new InvalidOperationException("The concurrent Shell view did not request its own session stream.");
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 3, 16)); // FVM_LIST
        Pump(300);
        Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var siblingMode, out _));
        Check(At<GetViewModeAndIconSize>(restoredFolderView, 36)(restoredFolderView, out var restoredMode, out _));
        if (siblingMode != 3 || restoredMode != 4)
            throw new InvalidOperationException($"Concurrent Shell views shared view mode state: sibling={siblingMode}; restored={restoredMode}.");
        var siblingInitialReads = host.Reads;
        Check(At<SaveViewState>(view, 13)(view));
        Pump(350);
        var retirement = new ShellViewReferenceRetirement(
            shellView => At<DestroyViewWindow>(shellView, 10)(shellView),
            shellView => Marshal.Release(shellView),
            message => Console.WriteLine("concurrent-view retirement: " + message));
        host.View = 0;
        retirement.Retire(view, folder);
        view = folder = viewWindow = 0;
        var siblingWrites = host.Writes;
        Marshal.Release(hostPointer);
        hostPointer = 0;
        host.Dispose();
        host = null;
        if (siblingWrites == 0 || !store.TryLoad(siblingSessionId, out var siblingState) ||
            restoredState.SequenceEqual(siblingState))
            throw new InvalidOperationException("The sibling Shell view did not persist an independent view-state stream.");

        Check(SHBindToObject(0, pidl, 0, in ShellFolderId, out folder));
        shellViewId = ShellViewId;
        Check(At<CreateViewObject>(folder, 8)(folder, pane, ref shellViewId, out view));
        host = new ShellViewColorBrowserHost(pane, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
            stateStore: store, stateId: siblingSessionId);
        host.View = view;
        hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        settings = new StreamProbeSettings { Mode = uint.MaxValue };
        bounds = new StreamProbeRect { Right = 380, Bottom = 235 };
        Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
        if (viewWindow == 0) throw new InvalidOperationException("The sibling Shell view could not be recreated.");
        if (GetParent(viewWindow) != pane)
            throw new InvalidOperationException("The recreated Shell view left its child pane host.");
        Check(At<ActivateView>(view, 7)(view, 1));
        Pump(700);
        Marshal.Release(folderView);
        folderView = 0;
        Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
        _ = WaitForShellViewItemCount(folderView, expectedCount: 2);
        Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var recreatedMode, out _));
        Check(At<GetViewModeAndIconSize>(restoredFolderView, 36)(restoredFolderView, out restoredMode, out _));
        if (host.Reads == 0 || recreatedMode != 3 || restoredMode != 4)
            throw new InvalidOperationException($"Recreated sibling did not restore its own List state: sibling={recreatedMode}; original={restoredMode}; reads={host.Reads}.");
        var otherFixture = GetNotificationIsolationFixture(fixture);
        var pane3 = CreateSiblingPaneView(fixture, store, containerParent, 0, 275, 1, 48);
        var pane4 = CreateSiblingPaneView(fixture, store, containerParent, 400, 275, 6, -1,
            otherFixture, expectedItemCount: 1);
        var allViews = new[] { restoredFolderView, folderView, pane3.FolderView, pane4.FolderView };
        var allHosts = new[] { restoredHost, host, pane3.Host, pane4.Host };
        var expectedModes = new uint[] { 4, 3, 1, 6 };
        AssertPaneModes(allViews, expectedModes, "four-pane setup");
        var menuRouting = VerifyFourPaneMenuActivation(
            new[] { restoredView, view, pane3.View, pane4.View }, allHosts);
        var menuCommands = VerifyFourPaneMenuCommandRouting(
            new[] { restoredView, view, pane3.View, pane4.View }, allViews, expectedModes, containerParent, expectedSort);
        var rootFolderViews = allViews.Take(3).ToArray();
        var allPaneCounts = VerifySessionRestoreFileOperations(restoredView, restoredFolderView, fixture, propertyFormatId,
            expectedSort, columnsExpected, rootFolderViews);
        var directoryIsolation = VerifyDirectoryNotificationIsolation(rootFolderViews, pane4.FolderView,
            fixture, otherFixture);
        AssertPaneModes(allViews, expectedModes, "file operations");
        return $"restored-stream-bytes={restoredState.Length}; sibling-reads={siblingInitialReads}; " +
            $"sibling-writes={siblingWrites}; sibling-stream-bytes={siblingState.Length}; " +
            $"recreated-reads={host.Reads}; recreated-mode={recreatedMode}; original-mode={restoredMode}; " +
            $"pane3-reads={pane3.Host.Reads}; pane4-reads={pane4.Host.Reads}; " +
            $"pane-modes={string.Join(',', expectedModes)}; same-container=True; panes=4; " +
            $"menu-routing={menuRouting}; menu-commands={menuCommands}; file-updates={allPaneCounts}; " +
            $"directory-notifications={directoryIsolation}; independent=True";
    }

    private static string VerifyFourPaneMenuActivation(IReadOnlyList<nint> views,
        IReadOnlyList<ShellViewColorBrowserHost> hosts)
    {
        if (views.Count != 4 || hosts.Count != 4)
            throw new ArgumentException("Menu routing probe requires exactly four Shell pane hosts.");
        var foreground = GetForegroundWindow();
        var before = hosts.Select(host => (host.InsertMenusCallCount, host.SetMenuCallCount,
            host.RemoveMenusCallCount)).ToArray();
        var activations = new List<string>(views.Count);
        for (var index = 0; index < views.Count; index++)
        {
            Check(At<ActivateView>(views[index], 7)(views[index], 2)); // SVUIA_ACTIVATE_FOCUS.
            Pump(250);
            var current = hosts[index];
            var insertDelta = current.InsertMenusCallCount - before[index].InsertMenusCallCount;
            var setDelta = current.SetMenuCallCount - before[index].SetMenuCallCount;
            if (insertDelta <= 0 || setDelta <= 0)
                throw new InvalidOperationException($"Activating pane {index + 1} did not route Shell menu callbacks to its own host: insert={insertDelta}; set={setDelta}.");
            activations.Add($"{index + 1}:{insertDelta}/{setDelta}/{current.RemoveMenusCallCount - before[index].RemoveMenusCallCount}");
            before[index] = (current.InsertMenusCallCount, current.SetMenuCallCount, current.RemoveMenusCallCount);
        }
        if (GetForegroundWindow() != foreground)
            throw new InvalidOperationException("Activating Shell pane views changed the foreground window outside the Private Desktop probe.");
        return $"per-pane-insert-set={string.Join(',', activations)}; foreground-unchanged=True";
    }

    private static string VerifyFourPaneMenuCommandRouting(IReadOnlyList<nint> views,
        IReadOnlyList<nint> folderViews, IReadOnlyList<uint> expectedModes, nint menuOwner,
        SortColumn expectedOriginalSort)
    {
        if (views.Count != 4 || folderViews.Count != 4 || expectedModes.Count != 4)
            throw new ArgumentException("Menu command routing probe requires exactly four Shell panes.");
        var foreground = GetForegroundWindow();
        var results = new List<string>(views.Count);
        var propertyFormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        var nameKey = new PropertyKey { FormatId = propertyFormatId, PropertyId = 10 };
        var typeKey = new PropertyKey { FormatId = propertyFormatId, PropertyId = 4 };
        foreach (var folderView in folderViews)
        {
            Check(At<SetSortColumns>(folderView, 27)(folderView,
                [new SortColumn { Key = nameKey, Direction = 1 }], 1));
            var groupKey = typeKey;
            Check(At<SetGroupBy>(folderView, 17)(folderView, ref groupKey, 1));
        }
        Pump(300);
        AssertSortAndGroupDirections(-1, 1, 1, "menu test baseline");
        for (var targetIndex = 0; targetIndex < views.Count; targetIndex++)
        {
            Check(At<SetViewModeAndIconSize>(folderViews[targetIndex], 35)(folderViews[targetIndex], 3, 16)); // FVM_LIST.
            Pump(150);
            Check(At<GetViewModeAndIconSize>(folderViews[targetIndex], 36)(folderViews[targetIndex], out var listMode, out _));
            if (listMode != 3)
                throw new InvalidOperationException($"Pane {targetIndex + 1} could not enter List before its Shell menu command.");
            AssertUnchangedSiblingModes(folderViews, expectedModes, targetIndex, "before");

            InvokeBackgroundVerb(views[targetIndex], menuOwner, "viewlogicaldetails");
            Pump(250);
            Check(At<GetViewModeAndIconSize>(folderViews[targetIndex], 36)(folderViews[targetIndex], out var commandMode, out _));
            if (commandMode != 4)
                throw new InvalidOperationException($"The Shell Details menu command did not affect pane {targetIndex + 1}: mode={commandMode}.");
            AssertUnchangedSiblingModes(folderViews, expectedModes, targetIndex, "after");
            AssertSortAndGroupDirections(-1, 1, 1, $"pane {targetIndex + 1} Details");
            InvokeBackgroundVerb(views[targetIndex], menuOwner, "sortdescending");
            Pump(100);
            AssertSortAndGroupDirections(targetIndex, -1, 1, $"pane {targetIndex + 1} Sort Descending");
            InvokeBackgroundVerb(views[targetIndex], menuOwner, "sortascending");
            Pump(100);
            AssertSortAndGroupDirections(targetIndex, 1, 1, $"pane {targetIndex + 1} Sort Ascending");
            InvokeBackgroundVerb(views[targetIndex], menuOwner, "groupdescending");
            Pump(100);
            AssertSortAndGroupDirections(targetIndex, 1, 0, $"pane {targetIndex + 1} Group Descending");
            InvokeBackgroundVerb(views[targetIndex], menuOwner, "groupascending");
            Pump(100);
            AssertSortAndGroupDirections(targetIndex, 1, 1, $"pane {targetIndex + 1} Group Ascending");
            Check(At<SetViewModeAndIconSize>(folderViews[targetIndex], 35)(folderViews[targetIndex],
                expectedModes[targetIndex], expectedModes[targetIndex] == 1 ? 48 : expectedModes[targetIndex] == 3 ? 16 : -1));
            Pump(150);
            results.Add($"pane{targetIndex + 1}:details/sort/group");
        }
        Check(At<SetSortColumns>(folderViews[0], 27)(folderViews[0], [expectedOriginalSort], 1));
        var restoreGroupKey = typeKey;
        Check(At<SetGroupBy>(folderViews[0], 17)(folderViews[0], ref restoreGroupKey, 1));
        Pump(150);
        var selectionMenus = VerifyFourPaneSelectionMenus(views, folderViews);
        var keyboardRouting = VerifyFourPaneArrowRouting(views, folderViews);
        if (GetForegroundWindow() != foreground)
            throw new InvalidOperationException("Shell menu command routing changed the foreground window outside the Private Desktop probe.");
        return $"{string.Join(',', results)}; selection-menus={selectionMenus}; keyboard={keyboardRouting}; foreground-unchanged=True";

        void AssertUnchangedSiblingModes(IReadOnlyList<nint> actualViews,
            IReadOnlyList<uint> expected, int activeIndex, string phase)
        {
            for (var index = 0; index < actualViews.Count; index++)
            {
                if (index == activeIndex) continue;
                Check(At<GetViewModeAndIconSize>(actualViews[index], 36)(actualViews[index], out var mode, out _));
                if (mode != expected[index])
                    throw new InvalidOperationException($"The Shell menu command {phase} pane {activeIndex + 1} changed sibling pane {index + 1}: mode={mode}; expected={expected[index]}.");
            }
        }

        void AssertSortAndGroupDirections(int activePaneIndex, int activeSortDirection,
            int activeGroupDirection, string phase)
        {
            for (var index = 0; index < folderViews.Count; index++)
            {
                Check(At<GetSortColumnCount>(folderViews[index], 26)(folderViews[index], out var count));
                if (count != 1) throw new InvalidOperationException($"Pane {index + 1} sort count changed during {phase}: {count}.");
                var sorts = new SortColumn[count];
                Check(At<GetSortColumns>(folderViews[index], 28)(folderViews[index], sorts, count));
                Check(At<GetGroupBy>(folderViews[index], 18)(folderViews[index], out var group, out var ascending));
                var expectedSortDirection = index == activePaneIndex || activePaneIndex < 0 ? activeSortDirection : 1;
                var expectedGroupDirection = index == activePaneIndex || activePaneIndex < 0 ? activeGroupDirection : 1;
                if (sorts[0].Key.FormatId != nameKey.FormatId || sorts[0].Key.PropertyId != nameKey.PropertyId ||
                    sorts[0].Direction != expectedSortDirection || group.FormatId != typeKey.FormatId ||
                    group.PropertyId != typeKey.PropertyId || ascending != expectedGroupDirection)
                    throw new InvalidOperationException($"Shell menu command {phase} changed the wrong view state: pane={index + 1}; sort={sorts[0].Key.PropertyId}/{sorts[0].Direction}; group={group.PropertyId}/{ascending}; expected={expectedSortDirection}/{expectedGroupDirection}.");
            }
        }
    }

    private static string VerifyFourPaneSelectionMenus(IReadOnlyList<nint> views, IReadOnlyList<nint> folderViews)
    {
        if (views.Count != 4 || folderViews.Count != 4)
            throw new ArgumentException("Selection menu probe requires exactly four Shell panes.");
        var verified = new List<string>(views.Count);
        for (var index = 0; index < views.Count; index++)
        {
            const uint svgIoAllView = 2;
            Check(At<ItemCount>(folderViews[index], 7)(folderViews[index], svgIoAllView, out var itemCount));
            if (itemCount == 0) throw new InvalidOperationException($"Pane {index + 1} has no item for its selection menu probe.");
            nint contextMenu = 0, popup = 0;
            try
            {
                Check(At<SelectFolderItem>(folderViews[index], 15)(folderViews[index], 0,
                    0x1u | 0x4u | 0x10u | 0x40000000u)); // SVSI_SELECT | DESELECTOTHERS | FOCUSED | NOTAKEFOCUS.
                for (var pane = 0; pane < folderViews.Count; pane++)
                {
                    Check(At<ItemCount>(folderViews[pane], 7)(folderViews[pane], 1, out var selectedCount)); // SVGIO_SELECTION.
                    var expectedSelection = pane == index ? 1 : 0;
                    if (selectedCount != expectedSelection)
                        throw new InvalidOperationException($"Selecting pane {index + 1} leaked selection into pane {pane + 1}: expected={expectedSelection}, observed={selectedCount}.");
                }
                var contextMenuId = ContextMenuId;
                Check(At<GetItemObject>(views[index], 15)(views[index], 1, ref contextMenuId, out contextMenu)); // SVGIO_SELECTION.
                if (contextMenu == 0)
                    throw new InvalidOperationException($"Pane {index + 1} did not provide a selected-item IContextMenu.");
                var commands = QueryMenuCommands(contextMenu, out popup, out _);
                if (!commands.Any(command => command.Verb.Equals("copy", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Pane {index + 1} selected-item menu did not expose the standard copy command.");
                verified.Add($"pane{index + 1}:copy");
            }
            finally
            {
                if (popup != 0) DestroyMenu(popup);
                if (contextMenu != 0) Marshal.Release(contextMenu);
                for (var item = 0; item < itemCount; item++)
                    Check(At<SelectFolderItem>(folderViews[index], 15)(folderViews[index], item, 0));
                for (var pane = 0; pane < folderViews.Count; pane++)
                {
                    Check(At<ItemCount>(folderViews[pane], 7)(folderViews[pane], 1, out var selectedCount));
                    if (selectedCount != 0)
                        throw new InvalidOperationException($"Selection menu probe left pane {pane + 1} selected: {selectedCount} items.");
                }
            }
        }
        return string.Join(',', verified);
    }

    private static string VerifyFourPaneArrowRouting(IReadOnlyList<nint> views, IReadOnlyList<nint> folderViews)
    {
        if (views.Count != 4 || folderViews.Count != 4)
            throw new ArgumentException("Arrow routing probe requires exactly four Shell panes.");
        var foreground = GetForegroundWindow();
        var verified = new List<string>(3);
        for (var target = 0; target < 3; target++)
        {
            var windowHr = At<GetWindow>(views[target], 3)(views[target], out var viewWindow);
            Check(windowHr);
            var listView = FindChildByClass(viewWindow, "SysListView32");
            if (listView == 0) throw new InvalidOperationException($"Pane {target + 1} has no native list window for its arrow test.");
            Check(At<GetViewModeAndIconSize>(folderViews[target], 36)(folderViews[target], out var mode, out _));
            _ = SetFocus(listView);
            if (GetFocus() != listView)
                throw new InvalidOperationException($"Pane {target + 1} file list did not receive isolated keyboard focus.");

            Check(At<SelectFolderItem>(folderViews[target], 15)(folderViews[target], 0,
                0x1u | 0x4u | 0x10u)); // SVSI_SELECT | DESELECTOTHERS | FOCUSED.
            Check(At<GetFocusedItem>(folderViews[target], 10)(folderViews[target], out var before));
            if (before != 0) throw new InvalidOperationException($"Pane {target + 1} arrow fixture expected first-item focus, got {before}.");
            var siblingFocus = new int[folderViews.Count];
            for (var pane = 0; pane < folderViews.Count; pane++)
                Check(At<GetFocusedItem>(folderViews[pane], 10)(folderViews[pane], out siblingFocus[pane]));

            var message = new NativeMessage
            {
                Window = listView,
                Id = 0x0100, // WM_KEYDOWN
                WParam = mode == 1 ? 0x27u : 0x28u, // Icon grid uses Right; Details/List use Down.
                LParam = mode == 1 ? 0x014D0001 : 0x00500001
            };
            Check(At<TranslateAccelerator>(views[target], 5)(views[target], ref message));
            Pump(100);
            Check(At<GetFocusedItem>(folderViews[target], 10)(folderViews[target], out var after));
            if (after != 1)
                throw new InvalidOperationException($"Pane {target + 1} Down accelerator did not move its focused item: {before}->{after}.");
            for (var pane = 0; pane < folderViews.Count; pane++)
            {
                if (pane == target) continue;
                Check(At<GetFocusedItem>(folderViews[pane], 10)(folderViews[pane], out var siblingAfter));
                if (siblingAfter != siblingFocus[pane])
                    throw new InvalidOperationException($"Pane {target + 1} Down accelerator changed pane {pane + 1} focus: {siblingFocus[pane]}->{siblingAfter}.");
            }
            verified.Add($"pane{target + 1}/mode{mode}:{before}->{after}");
        }
        for (var pane = 0; pane < folderViews.Count; pane++)
        {
            Check(At<ItemCount>(folderViews[pane], 7)(folderViews[pane], 2, out var itemCount)); // SVGIO_ALLVIEW.
            for (var item = 0; item < itemCount; item++)
                Check(At<SelectFolderItem>(folderViews[pane], 15)(folderViews[pane], item, 0));
        }
        if (GetForegroundWindow() != foreground)
            throw new InvalidOperationException("Pane arrow routing changed the foreground window outside the Private Desktop probe.");
        return string.Join(',', verified);
    }

    private static void InvokeBackgroundVerb(nint view, nint owner, string expectedVerb)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu)); // SVGIO_BACKGROUND.
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new InvalidOperationException("Could not create an in-memory Shell context menu.");
            try
            {
                var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryResult);
                var commandCount = Math.Min(queryResult & 0xffff, 0x7fff);
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
                }
                finally { Marshal.FreeHGlobal(buffer); }
                if (commandOffset is null)
                    throw new InvalidOperationException($"The Shell background menu did not expose canonical verb '{expectedVerb}'.");
                var invoke = new NativeInvokeInfo
                {
                    Size = Marshal.SizeOf<NativeInvokeInfo>(),
                    Window = owner,
                    Verb = (nint)commandOffset.Value,
                    Show = 1
                };
                Check(At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invoke));
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static LivePaneView CreateSiblingPaneView(string fixture, ShellViewStateStore store,
        nint containerParent, int x, int y, uint expectedMode, int iconSize,
        string? location = null, int expectedItemCount = 2)
    {
        var pane = CreateWindowExW(0, "STATIC", $"ExplorerDirs session restore pane {expectedMode}",
            0x50000000u | 0x02000000u | 0x04000000u, x, y, 390, 265, containerParent, 0, 0, 0);
        if (pane == 0 || GetParent(pane) != containerParent)
            throw new InvalidOperationException("Could not create another child pane under the shared container.");
        var sessionId = Guid.NewGuid();
        if (store.TryLoad(sessionId, out _))
            throw new InvalidOperationException("A new pane unexpectedly reused a saved Shell session.");
        Check(SHParseDisplayName(location ?? fixture, 0, out var pidl, 0, out _));
        Check(SHBindToObject(0, pidl, 0, in ShellFolderId, out var folder));
        var shellViewId = ShellViewId;
        Check(At<CreateViewObject>(folder, 8)(folder, pane, ref shellViewId, out var view));
        var host = new ShellViewColorBrowserHost(pane, [],
            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
            stateStore: store, stateId: sessionId);
        host.View = view;
        var hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        var settings = new StreamProbeSettings { Mode = uint.MaxValue };
        var bounds = new StreamProbeRect { Right = 380, Bottom = 235 };
        Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out var viewWindow));
        if (viewWindow == 0 || GetParent(viewWindow) != pane)
            throw new InvalidOperationException("The additional Shell view is not parented to its pane host.");
        Check(At<ActivateView>(view, 7)(view, 1));
        Pump(500);
        Check(Marshal.QueryInterface(view, in FolderView2Id, out var folderView));
        _ = WaitForShellViewItemCount(folderView, expectedItemCount);
        if (host.Reads == 0)
            throw new InvalidOperationException("The additional pane did not request a Shell stream for its session GUID.");
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, expectedMode, iconSize));
        Pump(250);
        Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var actualMode, out _));
        if (actualMode != expectedMode)
            throw new InvalidOperationException($"Pane session {sessionId:D} expected mode {expectedMode}, observed {actualMode}.");
        return new LivePaneView(pane, view, viewWindow, folder, pidl, hostPointer, host, folderView, sessionId);
    }

    private static void AssertPaneModes(IReadOnlyList<nint> folderViews, IReadOnlyList<uint> expectedModes, string phase)
    {
        for (var index = 0; index < folderViews.Count; index++)
        {
            Check(At<GetViewModeAndIconSize>(folderViews[index], 36)(folderViews[index], out var mode, out _));
            if (mode != expectedModes[index])
                throw new InvalidOperationException($"Pane {index + 1} changed mode during {phase}: expected={expectedModes[index]}, observed={mode}.");
        }
    }

    private static string VerifySessionRestoreFileOperations(nint view, nint folderView, string fixture,
        Guid propertyFormatId, SortColumn expectedSort, IReadOnlyList<DisplayColumnState> expectedColumns,
        IReadOnlyList<nint> allFolderViews)
    {
        const uint svgIoAllView = 2;
        var sourcePath = Path.Combine(fixture, "blue-rule-target.txt");
        var copiedPath = Path.Combine(fixture, "session-copy.txt");
        var renamedPath = Path.Combine(fixture, "session-renamed.txt");
        var source = CreateShellItem(sourcePath);
        var destination = CreateShellItem(fixture);
        try
        {
            Check(At<ItemCount>(folderView, 7)(folderView, svgIoAllView, out var initialCount));
            PerformShellFileOperation(operation =>
                Check(At<CopyItem>(operation, 16)(operation, source, destination, Path.GetFileName(copiedPath), 0)));
            if (!File.Exists(copiedPath) || File.ReadAllText(copiedPath) != File.ReadAllText(sourcePath))
                throw new IOException("The restored Shell view session could not copy its fixture file through IFileOperation.");
            if (!WaitForShellView(folderView, initialCount + 1, copiedPath, null) &&
                !RefreshUntilShellViewMatches(view, folderView, initialCount + 1, copiedPath, null))
                throw new IOException("The restored Shell view did not show the copied fixture after refresh.");
            WaitForAllPaneCounts(allFolderViews, initialCount + 1, "copy");
            AssertViewSettings("copy");

            PerformShellFileOperation(operation =>
            {
                var item = CreateShellItem(copiedPath);
                try { Check(At<RenameItem>(operation, 12)(operation, item, Path.GetFileName(renamedPath), 0)); }
                finally { Marshal.Release(item); }
            });
            if (File.Exists(copiedPath) || !File.Exists(renamedPath))
                throw new IOException("The restored Shell view session could not rename its fixture through IFileOperation.");
            if (!WaitForShellView(folderView, initialCount + 1, renamedPath, copiedPath) &&
                !RefreshUntilShellViewMatches(view, folderView, initialCount + 1, renamedPath, copiedPath))
                throw new IOException("The restored Shell view did not show the renamed fixture after refresh.");
            WaitForAllPaneCounts(allFolderViews, initialCount + 1, "rename");
            AssertViewSettings("rename");

            PerformShellFileOperation(operation =>
            {
                var item = CreateShellItem(renamedPath);
                try { Check(At<DeleteItem>(operation, 18)(operation, item, 0)); }
                finally { Marshal.Release(item); }
            });
            if (File.Exists(renamedPath))
                throw new IOException("The restored Shell view session could not delete its fixture through IFileOperation.");
            if (!WaitForShellView(folderView, initialCount, null, renamedPath) &&
                !RefreshUntilShellViewMatches(view, folderView, initialCount, null, renamedPath))
                throw new IOException("The restored Shell view did not remove the deleted fixture after refresh.");
            WaitForAllPaneCounts(allFolderViews, initialCount, "delete");
            AssertViewSettings("delete");
            return "copy/rename/delete=all-4-panes-converged";
        }
        finally
        {
            Marshal.Release(destination);
            Marshal.Release(source);
        }

        static void WaitForAllPaneCounts(IReadOnlyList<nint> folderViews, int expectedCount, string operation)
        {
            for (var index = 0; index < folderViews.Count; index++)
            {
                var count = WaitForShellViewItemCount(folderViews[index], expectedCount);
                if (count != expectedCount)
                    throw new IOException($"Pane {index + 1} did not converge after {operation}: expected={expectedCount}, observed={count}.");
            }
        }

        void AssertViewSettings(string operation)
        {
            Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var mode, out _));
            Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var sortCount));
            if (sortCount != 1) throw new IOException($"View settings changed after {operation}: sort count={sortCount}.");
            var sorts = new SortColumn[sortCount];
            Check(At<GetSortColumns>(folderView, 28)(folderView, sorts, sortCount));
            var groupResult = At<GetGroupBy>(folderView, 18)(folderView, out var group, out var ascending);
            Check(groupResult);
            Check(ShellColumnManager.Capture(folderView, out var columns));
            // Opening Details through the Shell menu fits columns to the current viewport width.
            // Preserve the persisted widths at initial restore, but after that transition compare column identity and order.
            var expectedColumnKeys = expectedColumns.Select(column => column.Key);
            var actualColumnKeys = columns?.Select(column => column.Key);
            if (mode != 4 || sorts[0].Key.FormatId != expectedSort.Key.FormatId ||
                sorts[0].Key.PropertyId != expectedSort.Key.PropertyId || sorts[0].Direction != expectedSort.Direction ||
                group.FormatId != propertyFormatId || group.PropertyId != 4 || ascending != 1 ||
                actualColumnKeys is null || !expectedColumnKeys.SequenceEqual(actualColumnKeys))
                throw new IOException($"The Shell {operation} changed the restored view settings: mode={mode}; sort={sorts[0].Key.FormatId}/{sorts[0].Key.PropertyId}/{sorts[0].Direction}; group={group.FormatId}/{group.PropertyId}/{ascending}; columns={(columns is null ? "<null>" : string.Join(';', columns.Select(column => $"{column.Key.FormatId}/{column.Key.PropertyId}:{column.Width}")))}.");
        }
    }

    private static string VerifyDirectoryNotificationIsolation(IReadOnlyList<nint> rootFolderViews,
        nint otherFolderView, string fixture, string otherFixture)
    {
        var originalCount = rootFolderViews.Count == 0
            ? throw new ArgumentException("At least one root-directory view is required.", nameof(rootFolderViews))
            : GetShellViewItemCount(rootFolderViews[0]);
        var otherOriginal = Path.Combine(otherFixture, "isolated-target.txt");
        var unrelatedCopy = Path.Combine(otherFixture, "isolated-copy.txt");
        if (!ShellViewContainsPath(otherFolderView, otherOriginal) || ShellViewContainsPath(otherFolderView,
                Path.Combine(fixture, "blue-rule-target.txt")))
            throw new IOException("The fourth pane did not stay scoped to its separate fixture directory.");
        foreach (var rootView in rootFolderViews)
            if (GetShellViewItemCount(rootView) != originalCount)
                throw new IOException("The three root-directory panes did not converge after their file operations.");

        var source = CreateShellItem(otherOriginal);
        var destination = CreateShellItem(otherFixture);
        try
        {
            PerformShellFileOperation(operation =>
                Check(At<CopyItem>(operation, 16)(operation, source, destination,
                    Path.GetFileName(unrelatedCopy), 0)));
        }
        finally
        {
            Marshal.Release(destination);
            Marshal.Release(source);
        }
        if (!File.Exists(unrelatedCopy) ||
            !WaitForShellView(otherFolderView, 2, unrelatedCopy, null))
            throw new IOException("The Shell notification did not update the pane showing the destination directory.");
        foreach (var rootView in rootFolderViews)
        {
            if (GetShellViewItemCount(rootView) != originalCount || ShellViewContainsPath(rootView, unrelatedCopy))
                throw new IOException("A file notification from another directory leaked into a root-directory pane.");
        }
        if (ShellViewContainsPath(otherFolderView, Path.Combine(fixture, "session-copy.txt")) ||
            ShellViewContainsPath(otherFolderView, Path.Combine(fixture, "session-renamed.txt")))
            throw new IOException("The separate-directory pane displayed a root-directory file operation.");
        return "root-copy-rename-delete=3-panes; other-copy=other-pane-only";
    }

    private static string GetNotificationIsolationFixture(string fixture)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var path = Path.GetFullPath(Path.Combine(root,
            Path.GetFileName(fixture) + "-notification-isolation"));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Directory.Exists(path) || File.Exists(path))
            throw new IOException("The isolated notification fixture path is not a fresh child of NativeProbe fixtures.");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "isolated-target.txt"), "separate-directory");
        return path;
    }

    private static int GetShellViewItemCount(nint folderView)
    {
        Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count)); // SVGIO_ALLVIEW.
        return count;
    }

    private readonly record struct LivePaneView(nint Pane, nint View, nint ViewWindow, nint Folder, nint Pidl,
        nint HostPointer, ShellViewColorBrowserHost Host, nint FolderView, Guid SessionId);

    internal static int Run(bool visible = false, bool offscreen = false, bool deferParentCleanup = false,
        bool queryCommandStatesOnly = false, bool readOnlyRuleProbe = false, bool probeMenuActivation = false,
        bool probeSessionRestore = false)
    {
        if (offscreen && !visible) throw new ArgumentException("Offscreen sampling requires a visible Shell view.", nameof(offscreen));
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        var stateDirectory = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N") + "-view-state");
        var sessionId = Guid.NewGuid();
        var stateStore = new ShellViewStateStore(stateDirectory);
        nint parent = 0, pidl = 0, folder = 0, view = 0, hostPointer = 0, viewWindow = 0;
        nint folderView = 0, contextMenu = 0, selectionMenu = 0, dropTarget = 0, listView = 0;
        nint drawNotifyTarget = 0, backgroundMenuHandle = 0, selectionMenuHandle = 0;
        nint folderContextMenu = 0, folderMenuHandle = 0, folderChildPidl = 0, folderChildArray = 0;
        nint originalListStyle = 0;
        var viewWindowCreateAttempted = false;
        var subclassed = false;
        var listStyleAdjusted = false;
        string? readOnlyFixturePath = null;
        ShellViewColorBrowserHost? host = null;
        var viewRetirement = new ShellViewReferenceRetirement(
            shellView => At<DestroyViewWindow>(shellView, 10)(shellView),
            shellView => Marshal.Release(shellView),
            message => Console.WriteLine("ishellview-color-spike navigation: " + message));

        int Navigate(nint targetPidl, uint flags)
        {
            nint nextFolder = 0, nextView = 0, nextWindow = 0;
            var createViewWindowAttempted = false;
            var previousView = view;
            var previousFolder = folder;
            try
            {
                Check(SHBindToObject(0, targetPidl, 0, in ShellFolderId, out nextFolder));
                var shellViewId = ShellViewId;
                Check(At<CreateViewObject>(nextFolder, 8)(nextFolder, parent, ref shellViewId, out nextView));
                host!.View = nextView;
                var settings = new StreamProbeSettings { Mode = uint.MaxValue };
                var bounds = new StreamProbeRect { Right = 640, Bottom = 420 };
                // CreateViewWindow can fail before returning an HWND; still run its matching destroy path.
                createViewWindowAttempted = true;
                var createHr = At<CreateViewWindow>(nextView, 9)(nextView, previousView,
                    ref settings, hostPointer, ref bounds, out nextWindow);
                if (createHr < 0) Marshal.ThrowExceptionForHR(createHr);
                if (nextWindow == 0)
                    throw new InvalidOperationException("CreateViewWindow succeeded without returning a view window.");
                var nextListView = FindChildByClass(nextWindow, "SysListView32");
                if (nextListView != 0) _ = ApplyFixtureViewBackground(nextListView, isActiveView: true);

                view = nextView;
                folder = nextFolder;
                viewWindow = nextWindow;
                viewWindowCreateAttempted = true;
                nextView = nextFolder = nextWindow = 0;
                host.View = view;

                try
                {
                    if (previousView != 0)
                        viewRetirement.Retire(previousView, previousFolder);
                    else if (previousFolder != 0)
                        Marshal.Release(previousFolder);
                }
                catch (Exception ex)
                {
                    // The new view is already active; cleanup errors must not make history roll back.
                    Console.WriteLine("ishellview-color-spike navigation: previous-view cleanup threw: " + ex.Message);
                }
                return 0;
            }
            catch (Exception ex)
            {
                host!.View = previousView;
                if (nextView != 0)
                    viewRetirement.RetireAfterCreateAttempt(nextView, nextFolder, createViewWindowAttempted);
                else if (nextFolder != 0) Marshal.Release(nextFolder);
                return Marshal.GetHRForException(ex);
            }
        }

        try
        {
            _readOnlyRuleProbe = readOnlyRuleProbe;
            Directory.CreateDirectory(fixture);
            var fixturePaths = new[]
            {
                Path.Combine(fixture, "blue-rule-target.txt"),
                Path.Combine(fixture, "green-rule-target.txt")
            };
            File.WriteAllText(fixturePaths[0], "blue");
            File.WriteAllText(fixturePaths[1], new string('g', 128));
            File.WriteAllText(Path.Combine(fixture, "filtered-out-target.bin"), "excluded");
            if (readOnlyRuleProbe)
            {
                readOnlyFixturePath = fixturePaths[0];
                // Apply attributes before Shell enumeration; the earlier post-enumeration refresh path had no reliable item-paint callback.
                File.SetAttributes(readOnlyFixturePath, File.GetAttributes(readOnlyFixturePath) | FileAttributes.ReadOnly);
            }
            var windowStyle = visible ? 0x10CF0000u | 0x10000000u : 0x80000000u;
            var extendedStyle = visible ? 0x08000000u | 0x00000080u : 0u; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            var parentX = 100;
            var parentY = 100;
            if (offscreen)
            {
                parentX = GetSystemMetrics(76) - 761; // SM_XVIRTUALSCREEN; place the whole 760 px window to its left.
                parentY = GetSystemMetrics(77) - 521; // SM_YVIRTUALSCREEN; place the whole 520 px window above it.
                if (parentX + 760 > GetSystemMetrics(76) || parentY + 520 > GetSystemMetrics(77))
                    throw new Exception("Could not place the sampling window outside the virtual desktop.");
            }
            parent = CreateWindowExW(extendedStyle, "STATIC", "ExplorerDirs isolated IShellView color spike",
                windowStyle, parentX, parentY, 760, 520, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Sampling parent creation failed.");
            _customDrawPrepaint = 0;
            _customDrawItems = 0;
            Check(SHParseDisplayName(fixture, 0, out pidl, 0, out _));
            var shellFolderId = ShellFolderId;
            var shellViewId = ShellViewId;
            Check(SHBindToObject(0, pidl, 0, in shellFolderId, out folder));
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));

            var itemFilter = new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false));
            var navigation = new ShellViewNavigationHistory(pidl, ILClone, Marshal.FreeCoTaskMem,
                ILIsEqual, GetParentPidl, ILCombine, Navigate);
            host = new ShellViewColorBrowserHost(parent, [], itemFilter, navigation,
                stateStore, sessionId);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 420 };
            viewWindowCreateAttempted = true;
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            if (viewWindow == 0)
                throw new InvalidOperationException("CreateViewWindow succeeded without returning a view window.");
            var filterSiteHr = Marshal.QueryInterface(view, in FolderFilterSiteId, out var filterSite);
            if (filterSite != 0) Marshal.Release(filterSite);
            Console.WriteLine($"ishellview-color-spike: direct-view-ifolderfiltersite={filterSiteHr:X8}");
            if (visible)
            {
                ShowWindow(parent, 4); // SW_SHOWNOACTIVATE
                Check(At<ActivateView>(view, 7)(view, 1)); // SVUIA_ACTIVATE_NOFOCUS
            }
            if (probeMenuActivation)
            {
                Check(At<ActivateView>(view, 7)(view, 2)); // SVUIA_ACTIVATE_FOCUS
                Pump();
                var sharedMenu = GetMenu(parent);
                Console.WriteLine($"ishellview-menu-activation: insert={host.InsertMenusCallCount}; " +
                    $"set={host.SetMenuCallCount}; remove={host.RemoveMenusCallCount}; " +
                    $"shared-menu-present={sharedMenu != 0}; active-view={view != 0}; " +
                    $"foreground-unchanged={GetForegroundWindow() == foreground}");
                return 0;
            }
            Pump();

            var folderViewHr = Marshal.QueryInterface(view, in FolderView2Id, out folderView);
            var contextMenuId = ContextMenuId;
            var contextMenuHr = At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out contextMenu);
            var allItems = WaitForShellViewItemCount(folderView, expectedCount: 2);
            if (host.IncludeObjectCalls < 3 || itemFilter.ShouldShowCalls < 3)
                throw new Exception($"The color host did not route all fixture items through its Shell filter: " +
                    $"include={host.IncludeObjectCalls}; filter={itemFilter.ShouldShowCalls}.");
            Console.WriteLine($"ishellview-color-spike: filtered-items={allItems}; " +
                $"include-calls={host.IncludeObjectCalls}; filter-calls={itemFilter.ShouldShowCalls}; filtered-out=True");
            if (probeSessionRestore)
            {
                const uint expectedMode = 4; // FVM_DETAILS
                var propertyFormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
                var nameKey = new PropertyKeyState(propertyFormatId, 10);
                var sizeKey = new PropertyKeyState(propertyFormatId, 12);
                var typeKey = new PropertyKeyState(propertyFormatId, 4);
                var expectedSort = new SortColumn
                {
                    Key = new PropertyKey { FormatId = propertyFormatId, PropertyId = nameKey.PropertyId },
                    Direction = -1
                };
                var expectedColumns = new DisplayColumnState[]
                {
                    new(nameKey, 280), new(sizeKey, 120), new(typeKey, 160)
                };
                Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, expectedMode, 16));
                Check(At<SetSortColumns>(folderView, 27)(folderView, [expectedSort], 1));
                var expectedGroup = new PropertyKey { FormatId = propertyFormatId, PropertyId = typeKey.PropertyId };
                Check(At<SetGroupBy>(folderView, 17)(folderView, ref expectedGroup, 1));
                Check(ShellColumnManager.Apply(folderView, expectedColumns));
                Pump(500);
                Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var modeBeforeSave, out _));
                Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var sortCountBeforeSave));
                if (sortCountBeforeSave != 1)
                    throw new Exception($"The real Shell view did not retain the requested sort before saving: {sortCountBeforeSave}.");
                var sortBeforeSave = new SortColumn[sortCountBeforeSave];
                Check(At<GetSortColumns>(folderView, 28)(folderView, sortBeforeSave, sortCountBeforeSave));
                var groupBeforeSaveHr = At<GetGroupBy>(folderView, 18)(folderView, out var groupBeforeSave, out var groupAscendingBeforeSave);
                Check(groupBeforeSaveHr);
                Check(ShellColumnManager.Capture(folderView, out var columnsBeforeSave));
                var sortBeforeSaveMatches = sortBeforeSave[0].Key.FormatId == expectedSort.Key.FormatId &&
                    sortBeforeSave[0].Key.PropertyId == expectedSort.Key.PropertyId && sortBeforeSave[0].Direction == expectedSort.Direction;
                var groupBeforeSaveMatches = groupBeforeSave.FormatId == expectedGroup.FormatId &&
                    groupBeforeSave.PropertyId == expectedGroup.PropertyId && groupAscendingBeforeSave == 1;
                var columnsBeforeSaveMatch = columnsBeforeSave is not null && expectedColumns.SequenceEqual(columnsBeforeSave);
                if (modeBeforeSave != expectedMode || !sortBeforeSaveMatches || !groupBeforeSaveMatches || !columnsBeforeSaveMatch)
                    throw new Exception("The real Shell view did not retain the requested mode, sort, group, and columns before saving.");
                Check(At<SaveViewState>(view, 13)(view));
                Pump(350);

                var oldView = view;
                var oldFolder = folder;
                if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
                if (contextMenu != 0) { Marshal.Release(contextMenu); contextMenu = 0; }
                view = folder = viewWindow = 0;
                viewWindowCreateAttempted = false;
                host.View = 0;
                viewRetirement.Retire(oldView, oldFolder);
                var shellWrites = host.Writes;
                Marshal.Release(hostPointer);
                hostPointer = 0;
                host.Dispose();
                host = null;
                var persisted = stateStore.TryLoad(sessionId, out var savedState);
                if (!persisted || shellWrites == 0)
                    throw new Exception($"Saving the real Shell view produced no persisted stream: writes={shellWrites}; saved={persisted}.");

                if (modeBeforeSave != expectedMode || !sortBeforeSaveMatches || !groupBeforeSaveMatches || !columnsBeforeSaveMatch)
                    throw new Exception("The real Shell view did not retain the requested mode, sort, group, and columns before saving.");
                var consumer = RunSessionRestoreConsumerProcess(fixture, stateDirectory, sessionId);
                Console.WriteLine($"ishellview-session-restore: producer-pid={Environment.ProcessId}; " +
                    $"shell-write-callbacks={shellWrites}; persisted-bytes={savedState.Length}; " +
                    $"mode-before-save={modeBeforeSave}; sort={sortCountBeforeSave}; " +
                    $"group={groupBeforeSaveHr:X8}; columns={columnsBeforeSave?.Count ?? 0}; " +
                    $"consumer-exit={consumer.ExitCode}; foreground-unchanged={GetForegroundWindow() == foreground}");
                foreach (var line in consumer.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    Console.WriteLine("ishellview-session-restore-child: " + line);
                if (consumer.ExitCode != 0 || GetForegroundWindow() != foreground)
                    throw new Exception("A separate NativeProbe process did not restore the real Shell view from the persisted session stream.");
                return 0;
            }
            var selectHr = At<SelectFolderItem>(folderView, 15)(folderView, 0, 0x1); // IFolderView::SelectItem; SVSI_SELECT only.
            Pump();
            var headerFlagProbe = VerifyColumnHeaderFlag(folderView);
            var viewModeCycle = VerifyNativeViewModeCycle(folderView);
            var qdrViewModes = VerifyQdrSpecificViewModes(folderView);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedItems)); // SVGIO_SELECTION
            using var selectionSnapshot = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not capture the isolated Shell selection.");
            var selectionRestoreTrace = new List<string>();
            var selectionRestore = selectionSnapshot.Restore(view, selectionRestoreTrace.Add);
            var identityTrace = selectionRestoreTrace.FirstOrDefault(line => line.StartsWith(
                "selection-current-identity ", StringComparison.Ordinal));
            var currentSelectionMatched = selectionRestoreTrace.Any(line =>
                line.StartsWith("selection-current matched=True", StringComparison.Ordinal));
            var batchWasSkipped = !selectionRestoreTrace.Any(line =>
                line.StartsWith("selection-batch-api ", StringComparison.Ordinal));
            if (!selectionRestore.SelectionRestored ||
                !currentSelectionMatched || !batchWasSkipped ||
                identityTrace is null || !identityTrace.Contains("method=pidl-exact", StringComparison.Ordinal))
                throw new Exception("Unchanged selection did not use the exact PIDL fast path: " +
                    string.Join(";", selectionRestoreTrace));
            Console.WriteLine($"ishellview-color-spike: selection-identity={identityTrace}; " +
                $"selection-restored={selectionRestore.SelectionRestored}; batch-skipped={batchWasSkipped}; " +
                $"focus-restored={selectionRestore.FocusRestored}");
            var selectionMenuHr = At<GetItemObject>(view, 15)(view, 1, ref contextMenuId, out selectionMenu); // SVGIO_SELECTION
            var dropTargetHr = Marshal.QueryInterface(view, in DropTargetId, out dropTarget);
            double backgroundMenuQueryMilliseconds = 0, selectionMenuQueryMilliseconds = 0;
            var backgroundMenuCommands = contextMenu == 0 ? [] : QueryMenuCommands(contextMenu, out backgroundMenuHandle, out backgroundMenuQueryMilliseconds);
            var selectionMenuCommands = selectionMenu == 0 ? [] : QueryMenuCommands(selectionMenu, out selectionMenuHandle, out selectionMenuQueryMilliseconds);
            var productionCommandStates = ShellFileCommands.Query(background =>
            {
                var context = background ? contextMenu : selectionMenu;
                return context == 0 ? null : new NativeShellCommandMenu(Marshal.GetObjectForIUnknown(context), parent);
            });
            if (queryCommandStatesOnly)
            {
                var freshStatesFirst = QueryFreshBackgroundCommandStates();
                var freshStatesSecond = QueryFreshBackgroundCommandStates();
                Console.WriteLine($"ishellview-command-state-readonly: retained-context=" +
                    $"{productionCommandStates.GetValueOrDefault(ShellFileCommand.Undo)}/{productionCommandStates.GetValueOrDefault(ShellFileCommand.Redo)}; " +
                    $"fresh-context-first={freshStatesFirst.GetValueOrDefault(ShellFileCommand.Undo)}/{freshStatesFirst.GetValueOrDefault(ShellFileCommand.Redo)}; " +
                    $"fresh-context-second={freshStatesSecond.GetValueOrDefault(ShellFileCommand.Undo)}/{freshStatesSecond.GetValueOrDefault(ShellFileCommand.Redo)}; " +
                    $"no-file-operations=True; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new InvalidOperationException("The read-only command-state probe changed visibility or foreground.");
                return 0;
            }
            var backgroundMenuVerbs = backgroundMenuCommands.Select(command => command.Verb).ToArray();
            var selectionMenuVerbs = selectionMenuCommands.Select(command => command.Verb).ToArray();
            var windowHr = At<GetWindow>(view, 3)(view, out var shellViewWindow);
            InvalidateRect(viewWindow, 0, true);
            UpdateWindow(viewWindow);
            Console.WriteLine($"ishellview-color-spike: parent-visible={IsWindowVisible(parent)}; " +
                $"view-window={windowHr:X8}/{shellViewWindow != 0}; folder-view2={folderViewHr:X8}; " +
                $"view-mode-cycle={viewModeCycle}; qdr-view-modes={qdrViewModes}; " +
                $"all-view-header-flag={headerFlagProbe}; " +
                $"background-context-menu={contextMenuHr:X8}; select-item={selectHr:X8}; " +
                $"selected-items={selectedItems}; selection-context-menu={selectionMenuHr:X8}; drop-target={dropTargetHr:X8}; " +
                $"foreground-unchanged={GetForegroundWindow() == foreground}");
            var backgroundCoreVerbs = new[] { "View", "GroupBy", "Refresh", "New", "NewFolder", "Properties" }
                .Where(verb => backgroundMenuVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase)).ToArray();
            var selectionCoreVerbs = new[] { "Cut", "Copy", "Paste", "Delete", "Rename", "Properties" }
                .Where(verb => selectionMenuVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase)).ToArray();
            Console.WriteLine($"ishellview-color-spike: background-core-verbs={string.Join(',', backgroundCoreVerbs)}; " +
                $"selection-core-verbs={string.Join(',', selectionCoreVerbs)}; " +
                $"sort-command={string.Join(',', backgroundMenuVerbs.Where(verb =>
                    new[] { "Arrange", "SortAscending", "SortDescending" }.Contains(verb, StringComparer.OrdinalIgnoreCase)))}");
            Console.WriteLine($"ishellview-color-spike: shell-context-menu-query-ms=background:{backgroundMenuQueryMilliseconds:F2},selection:{selectionMenuQueryMilliseconds:F2}");
            Console.WriteLine($"ishellview-color-spike: enabled-background={string.Join(',', DescribeCommandStates(backgroundMenuCommands, ["View", "GroupBy", "Refresh", "New", "Properties", "Arrange"]))}; " +
                $"enabled-selection={string.Join(',', DescribeCommandStates(selectionMenuCommands, ["Copy", "Delete", "Rename", "Properties"]))}");
            Console.WriteLine("ishellview-color-spike: production-command-states=" + string.Join(',', productionCommandStates.Select(pair => $"{pair.Key}:{pair.Value}")));
            if (windowHr < 0 || shellViewWindow == 0) Check(windowHr);
            ShellColorProbe.Report(shellViewWindow, "ishellview-color-spike");
            listView = FindChildByClass(shellViewWindow, "SysListView32");
            _observedListView = listView;
            var itemCount = listView == 0 ? 0 : unchecked((int)SendMessageW(listView, 0x1004, 0, 0)); // LVM_GETITEMCOUNT
            if (listView != 0)
            {
                var viewBackground = ApplyFixtureViewBackground(listView, isActiveView: true);
                var actualViewBackground = unchecked((uint)SendMessageW(listView, 0x1000, 0, 0)); // LVM_GETBKCOLOR
                if (viewBackground is { } expectedViewBackground && actualViewBackground != expectedViewBackground)
                    throw new Exception($"ListView background did not retain the active Q-Dir color rule: expected=0x{expectedViewBackground:X6}; actual=0x{actualViewBackground:X6}.");
                originalListStyle = GetWindowLongPtrW(listView, -16); // GWL_STYLE
                if (((long)originalListStyle & 0x10000000) == 0) // WS_VISIBLE
                {
                    // In hidden mode the hidden ancestor still keeps this fixture off desktop;
                    // visible mode requires an explicit CLI opt-in before this code can run.
                    SetWindowLongPtrW(listView, -16, (nint)((long)originalListStyle | 0x10000000));
                    listStyleAdjusted = true;
                }
                drawNotifyTarget = GetParent(listView);
                if (drawNotifyTarget == 0) throw new Exception("ListView notification parent is unavailable.");
                if (!SetWindowSubclass(drawNotifyTarget, DrawObserver, 41, 0))
                    throw new Exception("Custom-draw observer setup failed on ListView parent.");
                subclassed = true;
            }
            if (listView != 0) RefreshFixtureColorMap(folderView, fixturePaths);
            Console.WriteLine($"ishellview-color-spike: view-background-color={(listView == 0 ? "unsupported" : $"0x{unchecked((uint)SendMessageW(listView, 0x1000, 0, 0)):X6}")}");
            var syntheticColorContract = listView != 0 && VerifyCustomDrawContract(drawNotifyTarget, listView);
            _customDrawPrepaint = 0;
            _customDrawItems = 0;
            var drawByViewMode = listView == 0 ? "<no-list-view>" : ObserveCustomDrawByViewMode(folderView, listView, fixturePaths);
            var mappingAfterSortAndGroup = listView == 0 ? "<no-list-view>" :
                VerifyItemMappingAfterSortAndGroup(folderView, listView, drawNotifyTarget, fixturePaths);
            var nativeMenuHitTest = listView == 0 ? "<no-list-view>" : visible ? "<skipped-visible>" :
                VerifyNativeMenuHitTest(folderView, listView);
            var visiblePixelCapture = "not-run";
            var directListViewModeCapture = "not-run";
            if (visible && listView != 0)
            {
                visiblePixelCapture = CaptureVisibleRowColors(folderView, listView, fixturePaths, capturePrintWindowPixels: offscreen);
                directListViewModeCapture = VerifyDirectListViewModes(view, folderView, listView, fixturePaths);
            }
            var printClientSent = false;
            if (listView != 0 && itemCount > 0 && GetDC(listView) is var dc && dc != 0)
            {
                try
                {
                    // Ask the hidden common control to paint to a DC. This is not desktop input
                    // and does not make the parent or any child window visible.
                    SendMessageW(listView, 0x0318, dc, 0x00000004); // WM_PRINTCLIENT / PRF_CLIENT
                    printClientSent = true;
                }
                finally { ReleaseDC(listView, dc); }
            }
            InvalidateRect(listView, 0, true);
            if (listView != 0 && itemCount > 0)
                SendMessageW(listView, 0x1015, 0, itemCount - 1); // LVM_REDRAWITEMS, fixture row only.
            UpdateWindow(listView);
            SendMessageW(listView, 0x000F, 0, 0); // WM_PAINT, confined to the hidden fixture view.
            Pump(visible ? 5000 : 350);
            var callbackLifetimeSamples = string.Join('|', CustomDrawDeviceContexts.Keys.Order()
                .Select(index => $"{index}:dc-type={CustomDrawDeviceContextTypes.GetValueOrDefault(index)}," +
                    $"colors={CustomDrawColors.GetValueOrDefault(index, "none")}," +
                    $"label={FormatRect(CustomDrawLabelBounds.GetValueOrDefault(index))}," +
                    $"post-paint-pixel=0x{CustomDrawPostPaintPixels.GetValueOrDefault(index):X6}"));
            Console.WriteLine($"ishellview-color-spike: custom-draw-prepaint={_customDrawPrepaint}; " +
                $"custom-draw-item={_customDrawItems}; draw-by-mode={drawByViewMode}; sort-group-identity={mappingAfterSortAndGroup}; list-items={itemCount}; print-client-requested={printClientSent}; " +
                $"callback-lifetime-samples={callbackLifetimeSamples}; " +
                $"custom-draw-events={string.Join('|', CustomDrawEventDetails.TakeLast(16))}; " +
                $"notify-parent={WindowClassName(drawNotifyTarget)}; synthetic-color-contract={syntheticColorContract}; " +
                $"list-visible-style={((long)GetWindowLongPtrW(listView, -16) & 0x10000000) != 0}; " +
                $"parent-visible={IsWindowVisible(parent)}; visible-test={visible}; offscreen-test={offscreen}; " +
                $"visible-row-pixels={visiblePixelCapture}; direct-listview-mode-capture={directListViewModeCapture}");
            Console.WriteLine($"ishellview-color-spike: native-menu-hit-test={nativeMenuHitTest}");

            if (readOnlyRuleProbe)
            {
                // Keep this diagnostic read-only after pixel capture; later Shell copy/rename/delete checks use a writable fixture.
                Console.WriteLine("ishellview-color-spike: readonly-rule-probe=True; post-capture-file-operations=skipped");
                return 0;
            }

            var commandStatesBeforeFixtureOperations = QueryFreshBackgroundCommandStates();
            var nativeCopyRefresh = VerifyNativeCopyAndViewRefresh(view, folderView, fixturePaths[0], fixture);
            Console.WriteLine($"ishellview-color-spike: native-copy-and-live-view-refresh={nativeCopyRefresh}");
            var commandStatesAfterFixtureOperations = ShellFileCommands.Query(background =>
                background ? OpenFreshBackgroundCommandMenu() : null);
            Console.WriteLine($"ishellview-color-spike: undo-redo-state-observation=" +
                $"fresh-before:{commandStatesBeforeFixtureOperations.GetValueOrDefault(ShellFileCommand.Undo)}/{commandStatesBeforeFixtureOperations.GetValueOrDefault(ShellFileCommand.Redo)}," +
                $"fresh-after:{commandStatesAfterFixtureOperations.GetValueOrDefault(ShellFileCommand.Undo)}/{commandStatesAfterFixtureOperations.GetValueOrDefault(ShellFileCommand.Redo)}," +
                "undo-invoked=False;fixture-IFileOperation-undo-flags=absent");

            if (contextMenu != 0)
            {
                Marshal.Release(contextMenu);
                contextMenu = 0;
            }
            if (selectionMenu != 0)
            {
                Marshal.Release(selectionMenu);
                selectionMenu = 0;
            }
            if (folderView != 0)
            {
                Marshal.Release(folderView);
                folderView = 0;
            }
            if (folderViewHr < 0 || contextMenuHr < 0 || selectHr < 0 || selectedItems != 1 || selectionMenuHr < 0 || dropTargetHr < 0)
                throw new Exception("The stock view did not expose expected native view, selection-menu, or drop interfaces.");
            var requiredBackgroundVerbs = new[] { "View", "GroupBy", "Refresh", "New", "NewFolder", "Properties" };
            var missingBackgroundVerbs = requiredBackgroundVerbs.Except(backgroundMenuVerbs, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missingBackgroundVerbs.Length > 0)
                throw new Exception("The native background menu omitted expected Shell commands: " + string.Join(", ", missingBackgroundVerbs));
            if (!backgroundMenuVerbs.Any(verb =>
                    new[] { "Arrange", "SortAscending", "SortDescending" }.Contains(verb, StringComparer.OrdinalIgnoreCase)))
                throw new Exception("The native background menu exposed no canonical sorting command.");
            RequireEnabled(backgroundMenuCommands, ["View", "GroupBy", "Refresh", "New", "Properties", "Arrange"]);
            var requiredSelectionVerbs = new[] { "Copy", "Delete", "Rename", "Properties" };
            var missingSelectionVerbs = requiredSelectionVerbs.Except(selectionMenuVerbs, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missingSelectionVerbs.Length > 0)
                throw new Exception("The selected-item Shell menu omitted expected commands: " + string.Join(", ", missingSelectionVerbs));
            RequireEnabled(selectionMenuCommands, requiredSelectionVerbs);
            var requiredProductionCommands = new[]
            {
                ShellFileCommand.Copy, ShellFileCommand.Delete, ShellFileCommand.Rename,
                ShellFileCommand.Properties, ShellFileCommand.NewFolder
            };
            var unavailableProductionCommands = requiredProductionCommands
                .Where(command => !productionCommandStates.GetValueOrDefault(command)).ToArray();
            if (unavailableProductionCommands.Length > 0)
                throw new Exception("The production Shell command adapter reported expected commands unavailable: " +
                    string.Join(", ", unavailableProductionCommands));

            // Create this folder only after the view-mode and item-index checks so it
            // cannot perturb the two-file color fixture or its expected view count.
            var folderFixturePath = Path.Combine(fixture, "folder-rule-target");
            Directory.CreateDirectory(folderFixturePath);
            Check(SHParseDisplayName(folderFixturePath, 0, out folderChildPidl, 0, out _));
            folderChildArray = Marshal.AllocHGlobal(nint.Size);
            Marshal.WriteIntPtr(folderChildArray, ILFindLastID(folderChildPidl));
            var folderContextMenuId = ContextMenuId;
            Check(At<GetFolderUiObject>(folder, 10)(folder, parent, 1, folderChildArray,
                ref folderContextMenuId, 0, out folderContextMenu));
            var folderMenuCommands = QueryMenuCommands(folderContextMenu, out folderMenuHandle, out var folderMenuQueryMilliseconds);
            var folderMenuVerbs = folderMenuCommands.Select(command => command.Verb).ToArray();
            var requiredFolderVerbs = new[] { "Open", "Delete", "Rename", "Properties" };
            var missingFolderVerbs = requiredFolderVerbs.Except(folderMenuVerbs, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missingFolderVerbs.Length > 0)
                throw new Exception("The selected-folder Shell menu omitted expected commands: " + string.Join(", ", missingFolderVerbs));
            Console.WriteLine($"ishellview-color-spike: shell-context-menu-query-ms=background:{backgroundMenuQueryMilliseconds:F2},file:{selectionMenuQueryMilliseconds:F2},folder:{folderMenuQueryMilliseconds:F2}; " +
                $"folder-menu-core-verbs={string.Join(',', requiredFolderVerbs)}; folder-menu-command-count={folderMenuCommands.Length}");

            if (GetForegroundWindow() != foreground || IsWindowVisible(parent) != visible)
                throw new Exception("The isolated view changed foreground or became visible.");
            Console.WriteLine("ishellview-color-spike: native-view-and-context-menu-retained=True; " +
                "product-untouched=True");
            return 0;

            IShellCommandMenu OpenFreshBackgroundCommandMenu()
            {
                nint currentBackgroundMenu = 0;
                try
                {
                    var backgroundMenuId = ContextMenuId;
                    Check(At<GetItemObject>(view, 15)(view, 0, ref backgroundMenuId, out currentBackgroundMenu));
                    return new NativeShellCommandMenu(Marshal.GetObjectForIUnknown(currentBackgroundMenu), parent);
                }
                finally
                {
                    if (currentBackgroundMenu != 0) Marshal.Release(currentBackgroundMenu);
                }
            }

            Dictionary<ShellFileCommand, bool> QueryFreshBackgroundCommandStates() =>
                ShellFileCommands.Query(background => background ? OpenFreshBackgroundCommandMenu() : null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ishellview-color-spike failure: " + ex.Message);
            return 1;
        }
        finally
        {
            if (contextMenu != 0) Marshal.Release(contextMenu);
            if (selectionMenu != 0) Marshal.Release(selectionMenu);
            if (folderContextMenu != 0) Marshal.Release(folderContextMenu);
            if (backgroundMenuHandle != 0) DestroyMenu(backgroundMenuHandle);
            if (selectionMenuHandle != 0) DestroyMenu(selectionMenuHandle);
            if (folderMenuHandle != 0) DestroyMenu(folderMenuHandle);
            if (folderChildArray != 0) Marshal.FreeHGlobal(folderChildArray);
            if (folderChildPidl != 0) Marshal.FreeCoTaskMem(folderChildPidl);
            if (dropTarget != 0) Marshal.Release(dropTarget);
            if (folderView != 0) Marshal.Release(folderView);
            if (listStyleAdjusted && listView != 0 && IsWindow(listView))
                SetWindowLongPtrW(listView, -16, originalListStyle);
            if (view != 0)
            {
                try
                {
                    viewRetirement.RetireAfterCreateAttempt(view, folder, viewWindowCreateAttempted);
                }
                catch (Exception ex) { Console.WriteLine("ishellview-color-spike cleanup: " + ex.Message); }
                view = folder = 0;
            }
            else if (folder != 0) { Marshal.Release(folder); folder = 0; }
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (subclassed && drawNotifyTarget != 0) RemoveWindowSubclass(drawNotifyTarget, DrawObserver, 41);
            var parentDestroyed = parent == 0 || !IsWindow(parent);
            if (parent != 0 && IsWindow(parent))
            {
                if (deferParentCleanup)
                    Console.WriteLine("ishellview-color-spike cleanup: parent-cleanup-deferred-to-process-exit");
                else
                {
                    parentDestroyed = DestroyWindow(parent) && !IsWindow(parent);
                    if (!parentDestroyed)
                        Console.WriteLine("ishellview-color-spike cleanup: test parent remains alive.");
                }
            }
            if (parentDestroyed) viewRetirement.ReleaseDeferredAfterParentDestroyed();
            else if (viewRetirement.DeferredCount > 0)
                Console.WriteLine("ishellview-color-spike cleanup: deferred view, folder, and host references intentionally retained until process exit.");
            if (parentDestroyed || viewRetirement.DeferredCount == 0)
            {
                if (hostPointer != 0) Marshal.Release(hostPointer);
                if (host is not null)
                {
                    var shellWrites = host.Writes;
                    host.View = 0;
                    host.Dispose();
                    var persisted = stateStore.TryLoad(sessionId, out var savedState);
                    var restored = false;
                    if (persisted)
                    {
                        using var restoredHost = new ShellViewColorBrowserHost(0, [],
                            new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)),
                            stateStore: new ShellViewStateStore(stateDirectory), stateId: sessionId);
                        if (restoredHost.GetViewStateStream(0, out var readPointer) == 0 && readPointer != 0)
                        {
                            try { restored = ReadViewStateBytes(readPointer).SequenceEqual(savedState); }
                            finally { Marshal.Release(readPointer); }
                        }
                    }
                    Console.WriteLine($"ishellview-color-spike: session-state-writeback={persisted}; shell-write-callbacks={shellWrites}; bytes={(persisted ? savedState.Length : 0)}; recreated-host-restore={restored}");
                    if (!probeMenuActivation && !probeSessionRestore && (shellWrites == 0 || !persisted || !restored))
                        throw new Exception("The real Shell view did not complete its per-session state-stream round-trip.");
                    GC.KeepAlive(host);
                }
            }
            else if (host is not null) GC.KeepAlive(host);
            var fullFixture = Path.GetFullPath(fixture);
            var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullFixture))
            {
                if (readOnlyFixturePath is not null && File.Exists(readOnlyFixturePath))
                    File.SetAttributes(readOnlyFixturePath, File.GetAttributes(readOnlyFixturePath) & ~FileAttributes.ReadOnly);
                Directory.Delete(fullFixture, recursive: true);
            }
            if (probeSessionRestore)
            {
                var notificationFixture = Path.GetFullPath(Path.Combine(fixturesRoot,
                    Path.GetFileName(fixture) + "-notification-isolation"));
                if (notificationFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(notificationFixture))
                    Directory.Delete(notificationFixture, recursive: true);
            }
            var fullStateDirectory = Path.GetFullPath(stateDirectory);
            if (fullStateDirectory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullStateDirectory))
                Directory.Delete(fullStateDirectory, recursive: true);
            _readOnlyRuleProbe = false;
            if (GetForegroundWindow() != foreground)
                Console.WriteLine("ishellview-color-spike cleanup: foreground changed.");
        }
    }

    private static void Pump(int milliseconds = 350)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
    }

    private static (int ExitCode, string Output) RunSessionRestoreConsumerProcess(
        string fixture, string stateDirectory, Guid sessionId)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the NativeProbe process executable.");
        var assembly = typeof(ShellViewColorSpike).Assembly.Location;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--ishellview-session-restore-consumer-private-desktop");
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add(stateDirectory);
        start.ArgumentList.Add(sessionId.ToString("D"));

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the isolated session restore consumer process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(22_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.WaitForExit();
            var timeoutOutput = Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            return (124, $"consumer process timed out; {timeoutOutput[0]}{timeoutOutput[1]}");
        }

        var output = Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        return (process.ExitCode, output[0] + output[1]);
    }

    private static byte[] ReadViewStateBytes(nint pointer)
    {
        var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            stream.Stat(out var stat, 1);
            if (stat.cbSize is < 0 or > 65_536)
                throw new InvalidDataException("The saved Shell view-state stream exceeds the probe limit.");
            var bytes = new byte[(int)stat.cbSize];
            var read = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                stream.Seek(0, 0, 0);
                stream.Read(bytes, bytes.Length, read);
                if (Marshal.ReadInt32(read) != bytes.Length)
                    throw new InvalidDataException("The recreated host returned an incomplete Shell view-state stream.");
                return bytes;
            }
            finally { Marshal.FreeCoTaskMem(read); }
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    private static nint FindChildByClass(nint parent, string className)
    {
        nint found = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, name, name.Capacity) > 0 && name.ToString() == className)
            {
                found = window;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static string WindowClassName(nint window)
    {
        if (window == 0) return "<none>";
        var name = new System.Text.StringBuilder(128);
        return GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : "<unknown>";
    }

    private static bool VerifyCustomDrawContract(nint notifyTarget, nint listView)
    {
        _customDrawSelection = ShellListSelectionSnapshot.Empty();
        var size = Marshal.SizeOf<CustomDraw>();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            var beforeItems = new CustomDraw
            {
                Header = new NotifyHeader { Window = listView, Code = -12 }, // NM_CUSTOMDRAW
                Stage = 0x00000001 // CDDS_PREPAINT
            };
            Marshal.StructureToPtr(beforeItems, memory, false);
            var itemDrawRequest = SendMessageW(notifyTarget, 0x004E, 0, memory); // WM_NOTIFY
            if (itemDrawRequest != 0x00000020) // CDRF_NOTIFYITEMDRAW
                throw new Exception("NM_CUSTOMDRAW prepaint did not request item notifications.");

            foreach (var (index, expectedBackground) in CustomDrawBackgrounds.OrderBy(pair => pair.Key))
            {
                var itemDraw = new CustomDraw
                {
                    Header = new NotifyHeader { Window = listView, Code = -12 },
                    Stage = 0x00010001, // CDDS_ITEMPREPAINT
                    Item = index
                };
                Marshal.StructureToPtr(itemDraw, memory, false);
                var itemResult = SendMessageW(notifyTarget, 0x004E, 0, memory);
                var colored = Marshal.PtrToStructure<CustomDraw>(memory);
                if (itemResult != 0x00000012 || colored.TextBackground != expectedBackground ||
                    colored.TextColor != CustomDrawTextColors.GetValueOrDefault(index, 0x00202020u))
                    throw new Exception($"NM_CUSTOMDRAW returned the wrong text/background colors for item index {index}.");
            }

            var selectedItem = new CustomDraw
            {
                Header = new NotifyHeader { Window = listView, Code = -12 },
                Stage = 0x00010001, // CDDS_ITEMPREPAINT
                Item = CustomDrawBackgrounds.Keys.Min(),
                ItemState = 0x0001, // CDIS_SELECTED
                TextColor = 0x00112233,
                TextBackground = 0x00445566
            };
            _customDrawSelection = ShellListSelectionSnapshot.FromSelectedIndices([selectedItem.Item]);
            Marshal.StructureToPtr(selectedItem, memory, false);
            var selectedResult = SendMessageW(notifyTarget, 0x004E, 0, memory);
            var selectedDraw = Marshal.PtrToStructure<CustomDraw>(memory);
            _customDrawSelection = ShellListSelectionSnapshot.Empty();
            if (selectedResult != 0 || selectedDraw.TextColor != 0x00112233 ||
                selectedDraw.TextBackground != 0x00445566)
                throw new Exception("A selected Shell item did not retain the native selection colors.");
            return true;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string VerifyNativeViewModeCycle(nint folderView)
    {
        var results = new List<string>();
        foreach (var style in ViewStyle.All)
        {
            var requestedSize = ViewStyle.NativeIconSize(style.Mode, style.IconSize);
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, style.Mode, requestedSize));
            Pump(100);
            Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var actualMode, out var actualSize));
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var allItems)); // SVGIO_ALLVIEW
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedItems)); // SVGIO_SELECTION
            var expectedIconSize = requestedSize > 0 ? requestedSize : (int?)null;
            if (actualMode != style.Mode || expectedIconSize is { } size && actualSize != size ||
                allItems != 2 || selectedItems != 1)
                throw new Exception($"Native view style {style.Name} did not retain its mode/size/items/selection: " +
                    $"mode={actualMode}, size={actualSize}, items={allItems}, selected={selectedItems}.");
            results.Add($"{style.Name}:{actualMode}/{actualSize};selected={selectedItems}");
        }
        return string.Join('|', results);
    }

    private static string VerifyQdrSpecificViewModes(nint folderView)
    {
        var results = new List<string>();
        foreach (var mode in new uint[] { 5, 7 }) // Q-Dir FAQ 117 lists FVM_THUMBNAIL and FVM_THUMBSTRIP.
        {
            var setLogicalHr = At<SetViewModeAndIconSize>(folderView, 35)(folderView, mode, -1);
            Pump(100);
            var logicalGetHr = At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var logicalMode, out _);
            var setFolderHr = At<SetCurrentViewMode>(folderView, 4)(folderView, mode);
            Pump(100);
            var folderGetHr = At<GetCurrentViewMode>(folderView, 3)(folderView, out var folderMode);
            results.Add($"{mode}:logical={setLogicalHr:X8}/{logicalGetHr:X8}/{logicalMode};folder={setFolderHr:X8}/{folderGetHr:X8}/{folderMode}");
        }
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 4, 16));
        return string.Join('|', results);
    }

    private static string VerifyColumnHeaderFlag(nint folderView)
    {
        const uint noHeaderInAllViews = 0x01000000;
        const uint customPosition = 0x2;
        if (Marshal.QueryInterface(folderView, in FolderViewOptionsId, out var options) < 0 || options == 0)
            return "folder-view-options=unavailable";

        try
        {
            var getOptions = At<GetFolderViewOptions>(options, 4);
            var setOptions = At<SetFolderViewOptions>(options, 3);
            var optionsResult = getOptions(options, out var originalOptions);
            if (optionsResult < 0) return $"get-options=0x{optionsResult:X8}";

            var getFlags = At<GetCurrentFolderFlags>(folderView, 25);
            var flagsResult = getFlags(folderView, out var originalFlags);
            if (flagsResult < 0) return $"get-flags=0x{flagsResult:X8}";

            var setCustomPosition = setOptions(options, customPosition, originalOptions | customPosition);
            if (setCustomPosition < 0)
                return $"options=0x{originalOptions:X8},flags=0x{originalFlags:X8},set-custom-position=0x{setCustomPosition:X8}";

            try
            {
                var setFlags = At<SetCurrentFolderFlags>(folderView, 24);
                var desiredFlags = (originalFlags ^ noHeaderInAllViews) & noHeaderInAllViews;
                var setResult = setFlags(folderView, noHeaderInAllViews, desiredFlags);
                if (setResult < 0) return $"set-header-flag=0x{setResult:X8}";

                var readResult = getFlags(folderView, out var changedFlags);
                var readBackMatches = readResult >= 0 &&
                    (changedFlags & noHeaderInAllViews) == desiredFlags;
                var restoreResult = setFlags(folderView, noHeaderInAllViews,
                    originalFlags & noHeaderInAllViews);
                var restoredOptions = setOptions(options, customPosition, originalOptions & customPosition);
                if (restoreResult < 0 || restoredOptions < 0)
                    throw new Exception($"Folder view flag restoration failed: flags=0x{restoreResult:X8}, options=0x{restoredOptions:X8}.");

                var verifyRestoreResult = getFlags(folderView, out var restoredFlags);
                var flagsRestored = verifyRestoreResult >= 0 &&
                    (restoredFlags & noHeaderInAllViews) == (originalFlags & noHeaderInAllViews);
                return $"set=0x{setResult:X8},read=0x{readResult:X8},toggle-readback={readBackMatches},restore={flagsRestored},custom-position-restored=True";
            }
            finally
            {
                setOptions(options, customPosition, originalOptions & customPosition);
            }
        }
        finally { Marshal.Release(options); }
    }

    private static string ObserveCustomDrawByViewMode(nint folderView, nint listView, string[] fixturePaths)
    {
        var results = new List<string>();
        foreach (var style in ViewStyle.All)
        {
            if (!IsWindow(listView)) throw new Exception("The Shell list-view window changed during the isolated mode cycle.");
            _customDrawPrepaint = 0;
            _customDrawItems = 0;
            CustomDrawBounds.Clear();
            var requestedSize = ViewStyle.NativeIconSize(style.Mode, style.IconSize);
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, style.Mode, requestedSize));
            Pump(100);
            RefreshFixtureColorMap(folderView, fixturePaths);
            var capture = CaptureOffscreenRowColors(listView);
            var itemIdentityMap = capture.DrawItems.Length == 0
                ? "none" : MapCustomDrawItemsToFixtureFiles(folderView, fixturePaths, capture.DrawItems);
            results.Add($"{style.Name}:{capture.Prepaint}/{capture.CustomDrawItems};row-pixels={string.Join('/', capture.RowHits)};" +
                $"bitmap-pixels={string.Join('/', capture.BitmapHits)};item-rects={capture.ItemRects};" +
                $"item-identity={itemIdentityMap};custom-draw={capture.CustomDrawDetails}");
        }
        return string.Join('|', results);
    }

    private static string MapCustomDrawItemsToFixtureFiles(nint folderView, string[] fixturePaths, nuint[] itemIndices, bool requireEveryFixture = true)
    {
        var getItem = At<GetFolderItem>(folderView, 6);
        var mappedNames = new List<string>();
        foreach (var itemIndex in itemIndices)
        {
            Check(getItem(folderView, checked((int)itemIndex), out var childPidl));
            try
            {
                string? matchedName = null;
                foreach (var path in fixturePaths)
                {
                    Check(SHParseDisplayName(path, 0, out var absolutePidl, 0, out _));
                    try
                    {
                        if (ILIsEqual(childPidl, ILFindLastID(absolutePidl)))
                        {
                            matchedName = Path.GetFileName(path);
                            break;
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(absolutePidl); }
                }

                if (matchedName is null)
                    throw new Exception($"NM_CUSTOMDRAW item index {itemIndex} did not map to either isolated fixture PIDL.");
                if (mappedNames.Contains(matchedName, StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"NM_CUSTOMDRAW reported fixture item {matchedName} more than once.");
                mappedNames.Add(matchedName);
            }
            finally { Marshal.FreeCoTaskMem(childPidl); }
        }

        if (requireEveryFixture && mappedNames.Count != fixturePaths.Length)
            throw new Exception($"Expected custom-draw identity for {fixturePaths.Length} fixture files, got {mappedNames.Count}.");
        return string.Join(',', mappedNames);
    }

    private static string VerifyItemMappingAfterSortAndGroup(nint folderView, nint listView,
        nint notifyTarget, string[] fixturePaths)
    {
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 6, -1)); // Tiles, the mode with real custom-draw callbacks.
        Pump(120);
        var propertyFormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        var nameKey = new PropertyKey { FormatId = propertyFormatId, PropertyId = 10 };
        var sizeKey = new PropertyKey { FormatId = propertyFormatId, PropertyId = 12 };
        var traces = new List<string>();
        foreach (var (label, key, direction, grouped) in new[]
        {
            ("name-desc", nameKey, -1, false),
            ("size-asc", sizeKey, 1, false),
            ("size-grouped", sizeKey, 1, true)
        })
        {
            var sort = new SortColumn { Key = key, Direction = direction };
            Check(At<SetSortColumns>(folderView, 27)(folderView, [sort], 1));
            Check(At<GetSortColumnCount>(folderView, 26)(folderView, out var sortCount));
            if (sortCount != 1) throw new Exception($"IFolderView2 sort count did not match {label}.");
            var readSorts = new SortColumn[sortCount];
            Check(At<GetSortColumns>(folderView, 28)(folderView, readSorts, sortCount));
            if (readSorts[0].Key.FormatId != key.FormatId || readSorts[0].Key.PropertyId != key.PropertyId || readSorts[0].Direction != direction)
                throw new Exception($"IFolderView2 sort readback did not match {label}.");
            var group = grouped ? sizeKey : default;
            Check(At<SetGroupBy>(folderView, 17)(folderView, ref group, 1));
            var groupResult = At<GetGroupBy>(folderView, 18)(folderView, out var actualGroup, out var groupAscending);
            if (grouped
                    ? groupResult != 0 || actualGroup.FormatId != sizeKey.FormatId || actualGroup.PropertyId != sizeKey.PropertyId || groupAscending != 1
                    : groupResult != 1)
                throw new Exception($"IFolderView2 group state did not match {label}: hr=0x{groupResult:X8}.");
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 4, 16));
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 6, -1));
            Pump(120);
            RefreshFixtureColorMap(folderView, fixturePaths);
            var callbackColorMap = VerifyCustomDrawContract(notifyTarget, listView);
            _customDrawItems = 0;
            CustomDrawBounds.Clear();
            var capture = CaptureOffscreenRowColors(listView);
            var identity = capture.DrawItems.Length == 0
                ? "no-item-callback"
                : MapCustomDrawItemsToFixtureFiles(folderView, fixturePaths, capture.DrawItems);
            var currentOrder = MapCurrentFolderItems(folderView, fixturePaths);
            traces.Add($"{label}:index-order={currentOrder};draw={identity};identity-colors={callbackColorMap}");
        }

        var noGroup = default(PropertyKey);
        Check(At<SetGroupBy>(folderView, 17)(folderView, ref noGroup, 0));
        var alphabetical = new SortColumn { Key = nameKey, Direction = 1 };
        Check(At<SetSortColumns>(folderView, 27)(folderView, [alphabetical], 1));
        Pump(120);
        return string.Join('|', traces);
    }

    private static string MapCurrentFolderItems(nint folderView, string[] fixturePaths)
    {
        Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count)); // SVGIO_ALLVIEW.
        var names = new List<string>();
        for (var index = 0; index < count; index++)
        {
            Check(At<GetFolderItem>(folderView, 6)(folderView, index, out var childPidl));
            try
            {
                string? match = null;
                foreach (var path in fixturePaths)
                {
                    Check(SHParseDisplayName(path, 0, out var absolutePidl, 0, out _));
                    try
                    {
                        if (ILIsEqual(childPidl, ILFindLastID(absolutePidl)))
                        {
                            match = Path.GetFileName(path);
                            break;
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(absolutePidl); }
                }
                if (match is null) throw new Exception($"Current Shell item index {index} did not map to a fixture PIDL.");
                names.Add(match);
            }
            finally { Marshal.FreeCoTaskMem(childPidl); }
        }
        if (names.Count != fixturePaths.Length || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fixturePaths.Length)
            throw new Exception("Current Shell view item indices did not map one-to-one to the fixture PIDLs.");
        return string.Join(',', names);
    }

    private static void RefreshFixtureColorMap(nint folderView, string[] fixturePaths)
    {
        // Resolve item identity before painting. Do not call IFolderView from inside NM_CUSTOMDRAW.
        var orderedNames = MapCurrentFolderItems(folderView, fixturePaths).Split(',');
        ShellViewColorRule[] rules = _readOnlyRuleProbe
            ? [WithFontStyle(WithTextColor(ShellViewColorRule.BackgroundOnly("#RO", ShellColorRuleDecision.Set, 0x00C0E0F0u), 0x000000C0u),
                    new ShellItemDrawFontStyle(true, null, true, null)),
                WithFontStyle(WithTextColor(ShellViewColorRule.BackgroundOnly("green-rule-*.txt", ShellColorRuleDecision.Set, 0x00D0F0E0u), 0x0000C000u),
                    new ShellItemDrawFontStyle(null, true, null, true))]
            : [WithFontStyle(WithTextColor(ShellViewColorRule.BackgroundOnly("blue-rule-*.txt", ShellColorRuleDecision.Set, 0x00F0E0D0u), 0x00C00000u),
                    new ShellItemDrawFontStyle(true, null, true, null)),
                WithFontStyle(WithTextColor(ShellViewColorRule.BackgroundOnly("green-rule-*.txt", ShellColorRuleDecision.Set, 0x00D0F0E0u), 0x0000C000u),
                    new ShellItemDrawFontStyle(null, true, null, true))];
        CustomDrawBackgrounds.Clear();
        CustomDrawTextColors.Clear();
        CustomDrawFontStyles.Clear();
        for (var index = 0; index < orderedNames.Length; index++)
        {
            var path = fixturePaths.SingleOrDefault(candidate => Path.GetFileName(candidate).Equals(
                orderedNames[index], StringComparison.OrdinalIgnoreCase));
            if (path is null) throw new Exception($"Could not map Shell item {orderedNames[index]} to its fixture path.");
            var attributes = File.GetAttributes(path);
            var color = ShellViewColorRuleEvaluator.ResolveBackground(rules, orderedNames[index], attributes);
            if (color is null)
                throw new Exception($"Expected a configured color rule for fixture item {orderedNames[index]}.");
            CustomDrawBackgrounds[(nuint)index] = color.Value;
            if (ShellViewColorRuleEvaluator.ResolveTextColor(rules, orderedNames[index], attributes) is { } textColor)
                CustomDrawTextColors[(nuint)index] = textColor;
            var fontStyle = new ShellItemDrawFontStyle(
                ShellViewColorRuleEvaluator.ResolveFontStyle(rules, orderedNames[index], attributes, static rule => rule.Bold),
                ShellViewColorRuleEvaluator.ResolveFontStyle(rules, orderedNames[index], attributes, static rule => rule.Italic),
                ShellViewColorRuleEvaluator.ResolveFontStyle(rules, orderedNames[index], attributes, static rule => rule.Underline),
                ShellViewColorRuleEvaluator.ResolveFontStyle(rules, orderedNames[index], attributes, static rule => rule.Strikeout));
            if (fontStyle.HasOverrides) CustomDrawFontStyles[(nuint)index] = fontStyle;
        }
    }

    private static uint? ApplyFixtureViewBackground(nint listView, bool isActiveView)
    {
        var rules = new[]
        {
            ShellViewColorRule.BackgroundOnly("#BG-A", ShellColorRuleDecision.Set, 0x00F0E0D0u),
            ShellViewColorRule.BackgroundOnly("#BG", ShellColorRuleDecision.Set, 0x00D0E0F0u)
        };
        var color = ShellViewColorRuleEvaluator.ResolveViewBackground(rules, isActiveView);
        if (!ShellViewColorBackgroundPolicy.TryApply(listView, color,
                (window, value) => SendMessageW(window, 0x1001, 0, unchecked((nint)(long)value)) != 0))
            throw new InvalidOperationException("The isolated Shell ListView rejected its resolved background color.");
        return color;
    }

    private static ShellViewColorRule WithTextColor(ShellViewColorRule rule, uint textColor) =>
        rule with { TextColor = ShellColorRuleProperty<uint>.Set(textColor) };

    private static ShellViewColorRule WithFontStyle(ShellViewColorRule rule, ShellItemDrawFontStyle style) => rule with
    {
        Bold = style.Bold is { } bold ? ShellColorRuleProperty<bool>.Set(bold) : rule.Bold,
        Italic = style.Italic is { } italic ? ShellColorRuleProperty<bool>.Set(italic) : rule.Italic,
        Underline = style.Underline is { } underline ? ShellColorRuleProperty<bool>.Set(underline) : rule.Underline,
        Strikeout = style.Strikeout is { } strikeout ? ShellColorRuleProperty<bool>.Set(strikeout) : rule.Strikeout
    };

    private static string CaptureVisibleRowColors(nint folderView, nint listView, string[] fixturePaths,
        bool capturePrintWindowPixels)
    {
        // Remove the fixture selection so selection highlight cannot mask either test color.
        var select = At<SelectFolderItem>(folderView, 15);
        for (var index = 0; index < 2; index++)
        {
            Check(select(folderView, index, 0)); // IFolderView::SelectItem(index, SVSI_DESELECT).
        }
        Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedItems)); // SVGIO_SELECTION
        if (selectedItems != 0)
            throw new Exception($"Could not clear the isolated selection before pixel sampling: selected={selectedItems}.");

        var results = new List<string>();
        foreach (var style in ViewStyle.All)
        {
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, style.Mode,
                ViewStyle.NativeIconSize(style.Mode, style.IconSize)));
            Pump(150);
            RefreshFixtureColorMap(folderView, fixturePaths);
            var capture = CaptureOffscreenRowColors(listView, captureVisiblePixels: true);
            var identity = capture.DrawItems.Length == 0
                ? "none"
                : MapCustomDrawItemsToFixtureFiles(folderView, fixturePaths, capture.DrawItems, requireEveryFixture: false);
            var first = capture.RowHits.ElementAtOrDefault(0);
            var second = capture.RowHits.ElementAtOrDefault(1);
            var printWindow = capturePrintWindowPixels
                ? CaptureOffscreenRowColors(listView, capturePrintWindowPixels: true)
                : default;
            var modeWindow = GetWindowRect(listView, out var listRect)
                ? $"visible={IsWindowVisible(listView)},rect={FormatRect(listRect)},style=0x{unchecked((nuint)GetWindowLongPtrW(listView, -16)):X}"
                : $"visible={IsWindowVisible(listView)},rect=unavailable";
            results.Add($"{style.Name}:prepaint={capture.Prepaint};item-notify={capture.CustomDrawItems};identity={identity};" +
                $"row-color-pixels={first}/{second};item-rects={capture.ItemRects};hwnd={modeWindow};verified={first > 0 && second > 0}");
            if (capturePrintWindowPixels)
            {
                var printFirst = printWindow.RowHits?.ElementAtOrDefault(0) ?? 0;
                var printSecond = printWindow.RowHits?.ElementAtOrDefault(1) ?? 0;
                results[^1] += $";print-window-rendered={printWindow.PrintWindowRendered};" +
                    $"print-window-notify={printWindow.CustomDrawItems};" +
                    $"print-window-row-color-pixels={printFirst}/{printSecond};" +
                    $"print-window-item-rects={printWindow.ItemRects}";
            }
        }
        return string.Join('|', results);
    }

    private static string VerifyDirectListViewModes(nint view, nint folderView, nint listView, string[] fixturePaths)
    {
        const int LvmSetView = 0x108E;
        const int LvmGetView = 0x108F;
        var getViewState = At<GetViewModeAndIconSize>(folderView, 36);
        Check(getViewState(folderView, out var originalMode, out var originalIconSize));
        var viewPairs = new[]
        {
            (Name: "Icon", ListView: 0, Shell: 1, IconSize: 48), (Name: "Details", ListView: 1, Shell: 4, IconSize: 16),
            (Name: "SmallIcon", ListView: 2, Shell: 2, IconSize: 16), (Name: "List", ListView: 3, Shell: 3, IconSize: 16),
            (Name: "Tile", ListView: 4, Shell: 6, IconSize: 48)
        };
        var results = new List<string>();
        foreach (var style in viewPairs)
        {
            var setResult = SendMessageW(listView, LvmSetView, (nint)style.ListView, 0).ToInt32();
            Pump(150);
            Check(getViewState(folderView, out var shellMode, out var shellIconSize));
            RefreshFixtureColorMap(folderView, fixturePaths);
            var capture = CaptureOffscreenRowColors(listView, captureVisiblePixels: true);
            var listViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            var pixels = string.Join('/', capture.RowHits);
            results.Add($"{style.Name}:set={setResult};lvm-view={listViewMode};shell={shellMode}/{shellIconSize};" +
                $"prepaint={capture.Prepaint};item-prepaint={capture.CustomDrawItems};pixels={pixels}");
        }

        var getSortCount = At<GetSortColumnCount>(folderView, 26);
        var getSorts = At<GetSortColumns>(folderView, 28);
        var setSorts = At<SetSortColumns>(folderView, 27);
        Check(getSortCount(folderView, out var originalSortCount));
        var originalSorts = new SortColumn[originalSortCount];
        if (originalSortCount > 0) Check(getSorts(folderView, originalSorts, originalSortCount));
        var getGroup = At<GetGroupBy>(folderView, 18);
        var setGroup = At<SetGroupBy>(folderView, 17);
        var originalGroupResult = getGroup(folderView, out var originalGroup, out var originalGroupAscending);
        var groupWasSet = originalGroupResult == 0;
        if (!groupWasSet && originalGroupResult != 1)
            throw new Exception($"Could not snapshot IFolderView2 group state: hr=0x{originalGroupResult:X8}.");

        var propertyFormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        var nameKey = new PropertyKey { FormatId = propertyFormatId, PropertyId = 10 };
        var sizeKey = new PropertyKey { FormatId = propertyFormatId, PropertyId = 12 };

        var matchedColorResults = new List<string>();
        foreach (var style in viewPairs)
        {
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, (uint)style.Shell, style.IconSize));
            Pump(150);
            var setResult = SendMessageW(listView, LvmSetView, (nint)style.ListView, 0).ToInt32();
            Pump(150);
            Check(getViewState(folderView, out var shellMode, out var shellSize));
            var listViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            if (shellMode != style.Shell || shellSize != style.IconSize || listViewMode != style.ListView)
                throw new Exception($"Unsorted Shell/ListView mode mismatch for {style.Name}: shell={shellMode}/{shellSize}, list={listViewMode}.");
            RefreshFixtureColorMap(folderView, fixturePaths);
            ClearColorProbeSelection();
            var capture = CaptureOffscreenRowColors(listView, captureVisiblePixels: true);
            var selectionReadHr = At<ItemCount>(folderView, 7)(folderView, 1, out var selectedForColorCapture);
            var listSelectedCount = SendMessageW(listView, 0x1032, 0, 0).ToInt64(); // LVM_GETSELECTEDCOUNT
            var listItemStates = string.Join(',', Enumerable.Range(0, 2).Select(index =>
                $"{index}:0x{SendMessageW(listView, 0x102C, (nint)index, 0x0002).ToInt64():X}")); // LVM_GETITEMSTATE / LVIS_SELECTED
            if (capture.CustomDrawItems < 2 || capture.RowHits.ElementAtOrDefault(0) <= 0 || capture.RowHits.ElementAtOrDefault(1) <= 0)
                throw new Exception($"Unsorted matched {style.Name} mode did not render both expected row colors: callbacks={capture.CustomDrawItems},pixels={string.Join('/', capture.RowHits)},shell-selection=0x{selectionReadHr:X8}/{selectedForColorCapture},list-selection={listSelectedCount}[{listItemStates}],draw-state={string.Join(',', CustomDrawItemStates.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:0x{pair.Value:X}"))}.");
            matchedColorResults.Add($"{style.Name}:set={setResult};shell={shellMode}/{shellSize};list={listViewMode};pixels={string.Join('/', capture.RowHits)}");

            if (style.Name == "Icon")
            {
                var selectItem = At<SelectFolderItem>(folderView, 15);
                Check(selectItem(folderView, 0, 0x0001)); // SVSI_SELECT: verify selected color remains native.
                var selectedCapture = CaptureOffscreenRowColors(listView, captureVisiblePixels: true);
                if (selectedCapture.CustomDrawItems < 2 || selectedCapture.RowHits.ElementAtOrDefault(0) != 0 ||
                    selectedCapture.RowHits.ElementAtOrDefault(1) <= 0)
                    throw new Exception("Selected Icon item did not retain native selection colors while the other row stayed colored: " +
                        $"callbacks={selectedCapture.CustomDrawItems},pixels={string.Join('/', selectedCapture.RowHits)}.");
                Check(selectItem(folderView, 0, 0)); // Restore the unselected color fixture state before the next mode.
            }
        }

        var probeSort = new SortColumn { Key = nameKey, Direction = -1 };
        Check(setSorts(folderView, [probeSort], 1));
        var probeGroup = sizeKey;
        Check(setGroup(folderView, ref probeGroup, 1));

        var matchedResults = new List<string>();
        foreach (var style in viewPairs)
        {
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, (uint)style.Shell, style.IconSize));
            Pump(150);
            var setResult = SendMessageW(listView, LvmSetView, (nint)style.ListView, 0).ToInt32();
            Pump(150);
            Check(getViewState(folderView, out var shellMode, out var shellSize));
            if (shellMode != style.Shell || shellSize != style.IconSize)
                throw new Exception($"Shell view mode did not match the requested {style.Name} mode: {shellMode}/{shellSize}, expected {style.Shell}/{style.IconSize}.");
            var listViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            Check(getSortCount(folderView, out var sortCount));
            if (sortCount != 1) throw new Exception($"Shell sort state was lost after {style.Name} layout change: count={sortCount}.");
            var currentSorts = new SortColumn[sortCount];
            Check(getSorts(folderView, currentSorts, sortCount));
            if (currentSorts[0].Key.FormatId != nameKey.FormatId || currentSorts[0].Key.PropertyId != nameKey.PropertyId || currentSorts[0].Direction != -1)
                throw new Exception($"Shell sort key/direction was lost after {style.Name} layout change.");
            var groupHr = getGroup(folderView, out var currentGroup, out var currentGroupAscending);
            if (groupHr != 0 || currentGroup.FormatId != sizeKey.FormatId || currentGroup.PropertyId != sizeKey.PropertyId || currentGroupAscending != 1)
                throw new Exception($"Shell grouping state was lost after {style.Name} layout change: hr=0x{groupHr:X8}.");
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var allItems));
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedItems));
            var contextMenuId = ContextMenuId;
            var contextMenuHr = At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var backgroundMenu);
            if (backgroundMenu != 0) Marshal.Release(backgroundMenu);
            RefreshFixtureColorMap(folderView, fixturePaths);
            var capture = CaptureOffscreenRowColors(listView, captureVisiblePixels: true);
            var pixels = string.Join('/', capture.RowHits);
            if (capture.CustomDrawItems < 2 || capture.RowHits.Any(hit => hit <= 0))
                throw new Exception($"Sorted/grouped {style.Name} mode did not render both identity-mapped fixture colors: callbacks={capture.CustomDrawItems},pixels={pixels}.");
            var selectionMenu = VerifySelectionMenuAfterViewChange(view, folderView);
            matchedResults.Add($"{style.Name}:set={setResult};lvm-view={listViewMode};shell={shellMode}/{shellSize};" +
                $"items={allItems};selected={selectedItems};sort=Name-desc;group=Size-asc;background-menu=0x{contextMenuHr:X8};" +
                $"prepaint={capture.Prepaint};item-prepaint={capture.CustomDrawItems};pixels={pixels};selection-menu={selectionMenu}");
        }

        if (groupWasSet) Check(setGroup(folderView, ref originalGroup, originalGroupAscending));
        else
        {
            var noGroup = default(PropertyKey);
            Check(setGroup(folderView, ref noGroup, 0));
        }
        Check(setSorts(folderView, originalSorts, originalSortCount));
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, originalMode, originalIconSize));
        Pump(250);
        Check(getViewState(folderView, out var restoredMode, out var restoredSize));
        if (restoredMode != originalMode || restoredSize != originalIconSize)
            throw new Exception($"Restoring the Shell view after direct ListView-mode probing failed: {restoredMode}/{restoredSize}, expected {originalMode}/{originalIconSize}.");
        return "content-state-overrides=" + string.Join('|', results) + ";matched-color-unsorted=" +
            string.Join('|', matchedColorResults) + ";matched-shell-and-listview-sorted-grouped=" +
            string.Join('|', matchedResults) + $";restored={restoredMode}/{restoredSize}";

        void ClearColorProbeSelection()
        {
            var selectItem = At<SelectFolderItem>(folderView, 15);
            Check(selectItem(folderView, 0, 0)); // SVSI_DESELECT.
            Check(selectItem(folderView, 1, 0)); // SVSI_DESELECT.
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var remaining)); // SVGIO_SELECTION.
            if (remaining != 0)
                throw new Exception($"Could not clear Shell selection before color sampling: {remaining} items remain selected.");
        }
    }

    private static string VerifySelectionMenuAfterViewChange(nint view, nint folderView)
    {
        const uint Select = 0x1;
        const uint Deselect = 0;
        var selectItem = At<SelectFolderItem>(folderView, 15);
        Check(selectItem(folderView, 0, Select));
        nint contextMenu = 0, menu = 0;
        try
        {
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedBeforeMenu));
            if (selectedBeforeMenu != 1)
                throw new Exception($"Shell selection did not retain the requested item after direct ListView mode change: {selectedBeforeMenu}.");
            var contextMenuId = ContextMenuId;
            Check(At<GetItemObject>(view, 15)(view, 1, ref contextMenuId, out contextMenu));
            var commands = QueryMenuCommands(contextMenu, out menu, out _);
            var coreVerbs = commands.Where(command => new[] { "Delete", "Rename", "Properties" }
                .Contains(command.Verb, StringComparer.OrdinalIgnoreCase)).Select(command => command.Verb).ToArray();
            if (coreVerbs.Length != 3)
                throw new Exception("The selected Shell item menu lost a core operation after direct ListView mode change: " + string.Join(',', coreVerbs));
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedAfterMenu));
            if (selectedAfterMenu != 1)
                throw new Exception($"Querying the native selection menu changed the selected item count: {selectedAfterMenu}.");
            return $"selected={selectedBeforeMenu}/{selectedAfterMenu},verbs={string.Join(',', coreVerbs)}";
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
            Check(selectItem(folderView, 0, Deselect));
        }
    }

    private static (int[] RowHits, int[] BitmapHits, string ItemRects, string CustomDrawDetails,
        int Prepaint, int CustomDrawItems, nuint[] DrawItems, bool PrintWindowRendered)
        CaptureOffscreenRowColors(nint listView, bool captureVisiblePixels = false,
            bool capturePrintWindowPixels = false)
    {
        if (!GetClientRect(listView, out var client) || client.Right <= 0 || client.Bottom <= 0)
            throw new Exception("Could not read the isolated ListView client area.");
        var width = client.Right;
        var height = client.Bottom;
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height,
                Planes = 1, BitCount = 32, Compression = 0
            }
        };
        var screenDc = GetDC(0);
        if (screenDc == 0) throw new Exception("Could not acquire a reference DC for offscreen rendering.");
        nint memoryDc = 0, bitmap = 0, previousBitmap = 0;
        var invalidObject = unchecked((nint)(-1));
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == 0) throw new Exception("Could not create an offscreen rendering DC.");
            bitmap = CreateDIBSection(screenDc, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) throw new Exception("Could not create the offscreen row-color bitmap.");
            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == 0 || previousBitmap == invalidObject)
                throw new Exception("Could not select the offscreen row-color bitmap.");
            Marshal.Copy(new byte[checked(width * height * 4)], 0, pixels, checked(width * height * 4));
            _customDrawPrepaint = 0;
            _customDrawItems = 0;
            CustomDrawBounds.Clear();
            CustomDrawDeviceContexts.Clear();
            CustomDrawDeviceContextTypes.Clear();
            CustomDrawItemStates.Clear();
            CustomDrawPostPaintPixels.Clear();
            CustomDrawColors.Clear();
            CustomDrawLabelBounds.Clear();
            var printWindowRendered = false;
            var listItemCount = SendMessageW(listView, 0x1004, 0, 0).ToInt64(); // LVM_GETITEMCOUNT
            _customDrawSelection = ShellListSelectionSnapshot.Capture(checked((int)listItemCount), index =>
                unchecked((uint)SendMessageW(listView, 0x102C, (nint)index, 0x0002).ToInt64())); // LVM_GETITEMSTATE / LVIS_SELECTED
            if (captureVisiblePixels)
            {
                // Reset observations before forcing the real visible ListView paint.
                // DefView's NM_CUSTOMDRAW rc can be empty even when ListView has valid item bounds.
                // Seed callback sampling from the control's own item geometry before painting.
                for (var index = 0; index < 2; index++)
                {
                    if (TryGetItemRect(listView, index, out var itemBounds))
                        CustomDrawBounds[(nuint)index] = itemBounds;
                    if (TryGetItemRect(listView, index, 2, out var labelBounds))
                        CustomDrawLabelBounds[(nuint)index] = labelBounds;
                }
                InvalidateRect(listView, 0, true);
                SendMessageW(listView, 0x1015, 0, 1); // LVM_REDRAWITEMS, both fixture rows.
                UpdateWindow(listView);
                var sourceDc = GetDC(listView);
                if (sourceDc == 0) throw new Exception("Could not acquire the visible fixture ListView DC.");
                try
                {
                    if (!BitBlt(memoryDc, 0, 0, width, height, sourceDc, 0, 0, 0x00CC0020))
                        throw new Exception("Could not capture visible fixture ListView pixels.");
                }
                finally { ReleaseDC(listView, sourceDc); }
            }
            else if (capturePrintWindowPixels)
            {
                // PrintWindow asks the isolated common control to render into a caller-owned DC,
                // separating control painting from pixels clipped by the offscreen desktop.
                printWindowRendered = PrintWindow(listView, memoryDc, 0x00000001); // PW_CLIENTONLY
            }
            else
            {
                SendMessageW(listView, 0x0318, memoryDc, 0x0000000C); // WM_PRINTCLIENT / PRF_CLIENT | PRF_ERASEBKGND
            }
            if (!GdiFlush()) throw new Exception("Could not flush the isolated GDI drawing batch before reading pixels.");
            var prepaint = _customDrawPrepaint;
            var customDrawItems = _customDrawItems;
            // Snapshot synchronously so later item-rectangle queries cannot be mistaken for print callbacks.
            var customDrawReport = Enumerable.Range(0, 2).Select(index =>
            {
                var key = (nuint)index;
                return CustomDrawBounds.TryGetValue(key, out var drawBounds)
                    && CustomDrawDeviceContexts.TryGetValue(key, out var drawDc)
                    ? $"{index}:rect={drawBounds.Left},{drawBounds.Top},{drawBounds.Right},{drawBounds.Bottom},dc=0x{unchecked((nuint)drawDc):X}/type-at-callback={CustomDrawDeviceContextTypes.GetValueOrDefault(key)},colors={CustomDrawColors.GetValueOrDefault(key, "none")},post-paint-pixel=0x{CustomDrawPostPaintPixels.GetValueOrDefault(key):X6},label={FormatRect(CustomDrawLabelBounds.GetValueOrDefault(key))},bitmap-dc=0x{unchecked((nuint)memoryDc):X}/type={GetObjectType(memoryDc)}"
                    : $"{index}:none";
            }).ToArray();
            var drawItems = CustomDrawBounds.Keys.Order().ToArray();
            // Sorting and grouping can change which fixture occupies each ListView index.
            // Compare each row with the color assigned to its current Shell item index.
            var expected = CustomDrawBackgrounds.OrderBy(pair => pair.Key)
                .Select(pair => ColorrefToDib(pair.Value)).ToArray();
            if (expected.Length != 2)
                throw new Exception($"Expected colors for both fixture rows before sampling; found {expected.Length}.");
            var rowHits = new int[expected.Length];
            var bitmapHits = new int[expected.Length];
            var itemRectReport = new List<string>();
            for (var index = 0; index < expected.Length; index++)
            {
                if (TryGetItemRect(listView, index, out var itemRect))
                {
                    itemRectReport.Add($"{index}:{itemRect.Left},{itemRect.Top},{itemRect.Right},{itemRect.Bottom}");
                    var left = Math.Clamp(itemRect.Left, 0, width);
                    var right = Math.Clamp(itemRect.Right, 0, width);
                    var top = Math.Clamp(itemRect.Top, 0, height);
                    var bottom = Math.Clamp(itemRect.Bottom, 0, height);
                    for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                        if (unchecked((uint)Marshal.ReadInt32(pixels, checked((y * width + x) * 4))) == expected[index])
                            rowHits[index]++;
                }
                else itemRectReport.Add($"{index}:none");
            }
            var bitmapBytes = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, bitmapBytes, 0, bitmapBytes.Length);
            for (var offset = 0; offset < bitmapBytes.Length; offset += 4)
            {
                var pixel = (uint)(bitmapBytes[offset] | bitmapBytes[offset + 1] << 8 | bitmapBytes[offset + 2] << 16);
                for (var index = 0; index < expected.Length; index++)
                    if (pixel == expected[index]) bitmapHits[index]++;
            }
            return (rowHits, bitmapHits, string.Join(',', itemRectReport), string.Join(',', customDrawReport),
                prepaint, customDrawItems, drawItems, printWindowRendered);
        }
        finally
        {
            if (previousBitmap != 0 && previousBitmap != invalidObject) SelectObject(memoryDc, previousBitmap);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memoryDc != 0) DeleteDC(memoryDc);
            ReleaseDC(0, screenDc);
        }
    }

    private static bool TryGetItemRect(nint listView, int index, out Rect rect) => TryGetItemRect(listView, index, 0, out rect);

    private static bool TryGetItemRect(nint listView, int index, int part, out Rect rect)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.WriteInt32(memory, part); // LVIR_BOUNDS or LVIR_LABEL.
            if (SendMessageW(listView, 0x100E, (nint)index, memory) == 0)
            {
                rect = default;
                return false;
            }
            rect = Marshal.PtrToStructure<Rect>(memory);
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string FormatRect(Rect rect) => $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}";

    private static string VerifyNativeMenuHitTest(nint folderView, nint listView)
    {
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 4, 16)); // Details.
        // Widen only the isolated child window so the probe has client space beyond the last Details column.
        MoveWindow(GetParent(listView), 0, 0, 1400, 420, false);
        Pump();
        if (!TryGetItemRect(listView, 0, out var bounds))
            throw new Exception("Could not read the first Details row bounds for hit testing.");
        var iconRect = GetItemPartRect(listView, 0, 1); // LVIR_ICON.
        var iconX = (iconRect.Left + iconRect.Right) / 2;
        var iconY = (iconRect.Top + iconRect.Bottom) / 2;
        var header = FindChildByClass(listView, "SysHeader32");
        if (header == 0) throw new Exception("Details header is unavailable for row-tail hit testing.");
        var headerCount = SendMessageW(header, 0x1200, 0, 0).ToInt32(); // HDM_GETITEMCOUNT.
        if (headerCount == 0) throw new Exception("Details header has no columns.");
        var lastColumn = GetHeaderItemRect(header, headerCount - 1);
        if (!GetClientRect(listView, out var listClient) || listClient.Right <= lastColumn.Right + 4)
            throw new Exception($"Widened Details view has no blank space after the final column: client={listClient.Right}, last-column={lastColumn.Right}.");
        var rowEdgeX = (lastColumn.Right + listClient.Right) / 2;
        var rowY = (bounds.Top + bounds.Bottom) / 2;
        var icon = ReadNativeListHit(listView, iconX, iconY);
        var rowEdge = ReadNativeListHit(listView, rowEdgeX, rowY);
        if (!ShellMenuTargetTracker.IsNativeListItemHit(icon.ItemIndex, icon.Flags))
            throw new Exception($"A known file icon did not report an item hit: {icon}.");
        if (ShellMenuTargetTracker.IsNativeListItemHit(rowEdge.ItemIndex, rowEdge.Flags))
            throw new Exception($"Blank space after the final Details column was reported as a file item: {rowEdge}.");
        return $"icon={icon};row-edge={rowEdge};row-edge-is-item={ShellMenuTargetTracker.IsNativeListItemHit(rowEdge.ItemIndex, rowEdge.Flags)}";
    }

    private static Rect GetHeaderItemRect(nint header, int index)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            if (SendMessageW(header, 0x1207, (nint)index, memory) == 0) // HDM_GETITEMRECT.
                throw new Exception($"Could not read Details header column {index} bounds.");
            return Marshal.PtrToStructure<Rect>(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static Rect GetItemPartRect(nint listView, int index, int part)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.WriteInt32(memory, part);
            if (SendMessageW(listView, 0x100E, (nint)index, memory) == 0)
                throw new Exception($"Could not read ListView item part {part}.");
            var rect = Marshal.PtrToStructure<Rect>(memory);
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                throw new Exception($"ListView item part {part} returned an empty rectangle.");
            return rect;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static NativeListHit ReadNativeListHit(nint listView, int x, int y)
    {
        var memory = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(memory, 0, x);
            Marshal.WriteInt32(memory, 4, y);
            Marshal.WriteInt32(memory, 8, 0);
            Marshal.WriteInt32(memory, 12, -1);
            Marshal.WriteInt32(memory, 16, -1);
            Marshal.WriteInt32(memory, 20, -1);
            var itemIndex = SendMessageW(listView, 0x1039, 0, memory).ToInt32(); // LVM_SUBITEMHITTEST.
            var flags = unchecked((uint)Marshal.ReadInt32(memory, 8));
            var item = Marshal.ReadInt32(memory, 12);
            var subItem = Marshal.ReadInt32(memory, 16);
            return new NativeListHit(itemIndex, item, subItem, flags);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private readonly record struct NativeListHit(int ItemIndex, int Item, int SubItem, uint Flags);

    private static uint ColorrefToDib(uint colorref) =>
        ((colorref & 0x000000FF) << 16) | (colorref & 0x0000FF00) | ((colorref & 0x00FF0000) >> 16);

    private static MenuCommand[] QueryMenuCommands(nint contextMenu, out nint menu, out double queryMilliseconds)
    {
        menu = CreatePopupMenu();
        if (menu == 0) throw new Exception("Temporary native context menu creation failed.");
        var query = At<QueryContextMenu>(contextMenu, 3);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = query(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800); // CMF_CANRENAME | CMF_OPTIMIZEFORINVOKE
        stopwatch.Stop();
        queryMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        Check(result);
        var commandCount = Math.Min(result & 0xffff, 0x7fff);
        var commands = new List<MenuCommand>();
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            var getCommandString = At<GetCommandString>(contextMenu, 5);
            for (uint offset = 0; offset < commandCount; offset++)
            {
                Marshal.WriteInt16(buffer, 0);
                if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) != 0) continue; // GCS_VERBW
                Marshal.WriteInt16(buffer, 510, 0);
                var verb = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrWhiteSpace(verb))
                    commands.Add(new MenuCommand(verb, offset, GetMenuState(menu, offset + 1, 0)));
            }
            return commands.ToArray();
        }
        catch
        {
            DestroyMenu(menu);
            menu = 0;
            throw;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string[] DescribeCommandStates(MenuCommand[] commands, string[] verbs) => verbs
        .Select(verb => commands.FirstOrDefault(command => command.Verb.Equals(verb, StringComparison.OrdinalIgnoreCase)))
        .Where(command => command is not null)
        .Select(command => $"{command!.Verb}:{(command.State == uint.MaxValue ? "unknown" : (command.State & 3) == 0 ? "enabled" : "disabled")}")
        .ToArray();

    private static void RequireEnabled(MenuCommand[] commands, string[] verbs)
    {
        var unavailable = verbs.Where(verb => !commands.Any(command =>
            command.Verb.Equals(verb, StringComparison.OrdinalIgnoreCase) &&
            command.State != uint.MaxValue && (command.State & 3) == 0)).ToArray();
        if (unavailable.Length > 0)
            throw new Exception("The native Shell menu did not report commands enabled: " + string.Join(", ", unavailable));
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static nint GetParentPidl(nint absolutePidl)
    {
        var parentPidl = ILClone(absolutePidl);
        if (parentPidl == 0 || ILRemoveLastID(parentPidl)) return parentPidl;
        Marshal.FreeCoTaskMem(parentPidl);
        return 0;
    }

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    private static string VerifyNativeCopyAndViewRefresh(nint view, nint folderView, string sourcePath, string fixture)
    {
        const uint SvgIoAllView = 2;
        var shellItemInterfaceId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        nint source = 0, destination = 0;
        ProbeShellDirectoryMonitor? directoryMonitor = null;
        ShellViewSelectionSnapshot? overlapSelectionSnapshot = null;
        try
        {
            Check(At<ItemCount>(folderView, 7)(folderView, SvgIoAllView, out var beforeCount));
            Check(SHCreateItemFromParsingName(sourcePath, 0, ref shellItemInterfaceId, out source));
            Check(SHCreateItemFromParsingName(fixture, 0, ref shellItemInterfaceId, out destination));
            directoryMonitor = new ProbeShellDirectoryMonitor(fixture, beforeCount);
            directoryMonitor.Operation = "copy";
            PerformShellFileOperation(operation =>
                Check(At<CopyItem>(operation, 16)(operation, source, destination, "native-copy-result.txt", 0)));

            var copiedPath = Path.Combine(fixture, "native-copy-result.txt");
            if (!File.Exists(copiedPath) || File.ReadAllText(copiedPath) != File.ReadAllText(sourcePath))
                throw new IOException("The native Shell copy did not create the expected fixture file.");
            var copyAutoUpdated = WaitForShellView(folderView, beforeCount + 1, copiedPath, null);
            var copyRecoveredByNotification = !copyAutoUpdated && NotifyThenWaitForShellView(ShellCreate, copiedPath, null, folderView, beforeCount + 1, copiedPath, null);
            var copyWatcherRefreshStart = directoryMonitor.RefreshCount;
            var copyWatcherReconciled = directoryMonitor.Reconcile(
                () => ReadShellViewCount(folderView), () => Check(At<RefreshView>(view, 8)(view)),
                () => ShellViewContainsPath(folderView, copiedPath), () => Pump(25));
            var copyWatcherRefreshes = directoryMonitor.RefreshCount - copyWatcherRefreshStart;
            var copyRecoveredByRefresh = !copyAutoUpdated && !copyRecoveredByNotification && !copyWatcherReconciled &&
                RefreshUntilShellViewMatches(view, folderView, beforeCount + 1, copiedPath, null);
            if (!copyAutoUpdated && !copyRecoveredByNotification && !copyWatcherReconciled && !copyRecoveredByRefresh)
                throw new IOException("The Shell view could not display the copied item after Refresh.");

            var renamedPath = Path.Combine(fixture, "native-renamed-result.txt");
            directoryMonitor.Operation = "rename";
            PerformShellFileOperation(operation =>
            {
                var item = CreateShellItem(copiedPath);
                try { Check(At<RenameItem>(operation, 12)(operation, item, "native-renamed-result.txt", 0)); }
                finally { Marshal.Release(item); }
            });
            if (File.Exists(copiedPath)) throw new IOException("The Shell rename left the old fixture path present.");
            var renameAutoUpdated = WaitForShellView(folderView, beforeCount + 1, renamedPath, copiedPath);
            var renameRecoveredByNotification = !renameAutoUpdated && NotifyThenWaitForShellView(ShellRenameItem, copiedPath, renamedPath, folderView, beforeCount + 1, renamedPath, copiedPath);
            var renameWatcherRefreshStart = directoryMonitor.RefreshCount;
            var renameWatcherReconciled = directoryMonitor.Reconcile(
                () => ReadShellViewCount(folderView), () => Check(At<RefreshView>(view, 8)(view)),
                () => ShellViewContainsPath(folderView, renamedPath) && !ShellViewContainsPath(folderView, copiedPath), () => Pump(25));
            var renameWatcherRefreshes = directoryMonitor.RefreshCount - renameWatcherRefreshStart;
            var renameRecoveredByRefresh = !renameAutoUpdated && !renameRecoveredByNotification && !renameWatcherReconciled &&
                RefreshUntilShellViewMatches(view, folderView, beforeCount + 1, renamedPath, copiedPath);
            if (!renameAutoUpdated && !renameRecoveredByNotification && !renameWatcherReconciled && !renameRecoveredByRefresh)
                throw new IOException("The Shell view could not display the renamed item after Refresh.");

            directoryMonitor.Operation = "delete";
            PerformShellFileOperation(operation =>
            {
                var item = CreateShellItem(renamedPath);
                try { Check(At<DeleteItem>(operation, 18)(operation, item, 0)); }
                finally { Marshal.Release(item); }
            });
            if (File.Exists(renamedPath)) throw new IOException("The Shell delete left its fixture path present.");
            var deleteAutoUpdated = WaitForShellView(folderView, beforeCount, null, renamedPath);
            var deleteRecoveredByNotification = !deleteAutoUpdated && NotifyThenWaitForShellView(ShellDelete, renamedPath, null, folderView, beforeCount, null, renamedPath);
            var deleteWatcherRefreshStart = directoryMonitor.RefreshCount;
            var deleteWatcherReconciled = directoryMonitor.Reconcile(
                () => ReadShellViewCount(folderView), () => Check(At<RefreshView>(view, 8)(view)),
                () => !ShellViewContainsPath(folderView, renamedPath), () => Pump(25));
            var deleteWatcherRefreshes = directoryMonitor.RefreshCount - deleteWatcherRefreshStart;
            var deleteRecoveredByRefresh = !deleteAutoUpdated && !deleteRecoveredByNotification && !deleteWatcherReconciled &&
                RefreshUntilShellViewMatches(view, folderView, beforeCount, null, renamedPath);
            if (!deleteAutoUpdated && !deleteRecoveredByNotification && !deleteWatcherReconciled && !deleteRecoveredByRefresh)
                throw new IOException("The Shell view could not remove the deleted item after Refresh.");

            var inFlightSeedPath = Path.Combine(fixture, "refresh-overlap-seed.txt");
            File.WriteAllText(inFlightSeedPath, "seed");
            directoryMonitor.Operation = "refresh-seed";
            var seedReconciled = directoryMonitor.Reconcile(
                () => ReadShellViewCount(folderView), () => Check(At<RefreshView>(view, 8)(view)),
                () => ShellViewContainsPath(folderView, inFlightSeedPath), () => Pump(25));
            if (!seedReconciled) throw new IOException("The watcher did not reconcile the isolated refresh-overlap seed.");
            Check(At<SelectFolderItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u)); // select one stable fixture item before refresh.
            Pump(50);

            var renamedSeedPath = Path.Combine(fixture, "refresh-overlap-renamed.txt");
            File.Move(inFlightSeedPath, renamedSeedPath);
            var duringRefreshPath = Path.Combine(fixture, "created-during-refresh.txt");
            var createdDuringRefresh = false;
            (bool SelectionRestored, bool FocusRestored)? overlapSelectionResult = null;
            var overlapSelectionTrace = new List<string>();
            var overlapSelectionCallbackInvoked = false;
            string? overlapSelectionCallbackState = null;
            var refreshesWhenOverlapCreateWasIssued = 0;
            directoryMonitor.Operation = "refresh-overlap";
            var refreshesBeforeOverlap = directoryMonitor.RefreshCount;
            var overlapReconciled = directoryMonitor.Reconcile(
                () => ReadShellViewCount(folderView), () =>
                {
                    overlapSelectionSnapshot ??= ShellViewSelectionSnapshot.Capture(folderView)
                        ?? throw new IOException("Could not capture the selection before the overlapping refresh.");
                    if (overlapSelectionSnapshot.SelectedCount == 0)
                        throw new IOException("No stable Shell selection was available before the overlapping refresh.");
                    Check(At<RefreshView>(view, 8)(view));
                },
                () => ShellViewContainsPath(folderView, renamedSeedPath) && ShellViewContainsPath(folderView, duringRefreshPath),
                () => Pump(25), afterRefreshStarted: () =>
                {
                    refreshesWhenOverlapCreateWasIssued = directoryMonitor.RefreshCount;
                    File.WriteAllText(duringRefreshPath, "arrived-while-refresh-was-pending");
                    createdDuringRefresh = true;
                }, afterRefreshSettled: () =>
                {
                    overlapSelectionCallbackInvoked = true;
                    var queryResult = Marshal.QueryInterface(view, in FolderViewId, out var refreshedFolderView);
                    if (refreshedFolderView != 0) Marshal.Release(refreshedFolderView);
                    overlapSelectionTrace.Add($"refresh-folder-view-qi=0x{queryResult:X8}");
                    overlapSelectionResult = overlapSelectionSnapshot!.Restore(view, overlapSelectionTrace.Add,
                        allowSelectedFocusFallback: false);
                    overlapSelectionCallbackState = $"restore={overlapSelectionResult.Value.SelectionRestored},focus={overlapSelectionResult.Value.FocusRestored},trace-count={overlapSelectionTrace.Count}";
                });
            var refreshesForOverlap = directoryMonitor.RefreshCount - refreshesBeforeOverlap;
            var followupRefreshesAfterOverlapCreate = directoryMonitor.RefreshCount - refreshesWhenOverlapCreateWasIssued;
            if (!createdDuringRefresh || !overlapReconciled || !File.Exists(duringRefreshPath))
                throw new IOException("A watcher change arriving during Refresh was not merged and reconciled.");
            var selectionMatchedBeforeRestore = overlapSelectionTrace.Any(line =>
                line.StartsWith("selection-current matched=True", StringComparison.Ordinal));
            if (overlapSelectionResult is not { SelectionRestored: true })
                throw new IOException($"The isolated refresh did not restore the previously selected Shell item: callback={overlapSelectionCallbackInvoked},state={overlapSelectionCallbackState ?? "not-run"},result={overlapSelectionResult?.SelectionRestored.ToString() ?? "null"},selected-before={overlapSelectionSnapshot?.SelectedCount ?? -1},trace={string.Join(";", overlapSelectionTrace)}");

            return $"copy=created,auto-update:{copyAutoUpdated},change-notify-recovered:{copyRecoveredByNotification},watcher-reconciled:{copyWatcherReconciled},watcher-refreshes:{copyWatcherRefreshes},refresh-recovered:{copyRecoveredByRefresh};" +
                $"rename=created,auto-update:{renameAutoUpdated},change-notify-recovered:{renameRecoveredByNotification},watcher-reconciled:{renameWatcherReconciled},watcher-refreshes:{renameWatcherRefreshes},refresh-recovered:{renameRecoveredByRefresh};" +
                $"delete=created,auto-update:{deleteAutoUpdated},change-notify-recovered:{deleteRecoveredByNotification},watcher-reconciled:{deleteWatcherReconciled},watcher-refreshes:{deleteWatcherRefreshes},refresh-recovered:{deleteRecoveredByRefresh};" +
                $"view-count={beforeCount}->{beforeCount + 1}->{beforeCount + 1}->{beforeCount};" +
                $"refresh-overlap=reconciled:{overlapReconciled},created-during-refresh:{createdDuringRefresh},refreshes:{refreshesForOverlap},followup-refreshes-after-create:{followupRefreshesAfterOverlapCreate},selection-restored:{overlapSelectionResult.Value.SelectionRestored},selection-already-present:{selectionMatchedBeforeRestore},hidden-focus-restored:{overlapSelectionResult.Value.FocusRestored};" +
                $"ui-shown=False;watcher-trace={directoryMonitor.Diagnostics}";
        }
        finally
        {
            overlapSelectionSnapshot?.Dispose();
            directoryMonitor?.Dispose();
            if (destination != 0) Marshal.Release(destination);
            if (source != 0) Marshal.Release(source);
        }
    }

    private static void PerformShellFileOperation(Action<nint> queue)
    {
        const uint flags = 0x0004 | 0x0010 | 0x0400 | 0x00800000; // Silent, no confirmation/error UI, no copy hooks.
        var classId = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
        var interfaceId = new Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
        nint operation = 0;
        try
        {
            Check(CoCreateInstance(ref classId, 0, 1, ref interfaceId, out operation));
            Check(At<SetOperationFlags>(operation, 5)(operation, flags));
            queue(operation);
            Check(At<PerformOperations>(operation, 21)(operation));
            Check(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
            if (aborted != 0) throw new IOException("The isolated Shell file operation was aborted.");
        }
        finally
        {
            if (operation != 0) Marshal.Release(operation);
        }
    }

    private static nint CreateShellItem(string path)
    {
        var interfaceId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        Check(SHCreateItemFromParsingName(path, 0, ref interfaceId, out var item));
        return item;
    }

    private static bool WaitForShellView(nint folderView, int expectedCount, string? presentPath, string? absentPath)
    {
        const uint SvgIoAllView = 2;
        var deadline = Environment.TickCount64 + 5000;
        var observedCount = -1;
        while (Environment.TickCount64 < deadline)
        {
            Pump(100);
            Check(At<ItemCount>(folderView, 7)(folderView, SvgIoAllView, out observedCount));
            if (observedCount != expectedCount) continue;

            var hasExpectedItem = presentPath is null || ShellViewContainsPath(folderView, presentPath);
            var oldItemRemoved = absentPath is null || !ShellViewContainsPath(folderView, absentPath);
            if (hasExpectedItem && oldItemRemoved)
                return true;
        }
        return false;
    }

    private static bool RefreshUntilShellViewMatches(nint view, nint folderView, int expectedCount,
        string? presentPath, string? absentPath)
    {
        Check(At<RefreshView>(view, 8)(view));
        return WaitForShellView(folderView, expectedCount, presentPath, absentPath);
    }

    private static bool NotifyThenWaitForShellView(uint eventId, string path, string? secondPath,
        nint folderView, int expectedCount, string? presentPath, string? absentPath)
    {
        nint second = 0;
        try
        {
            if (secondPath is not null) second = Marshal.StringToHGlobalUni(secondPath);
            SHChangeNotify(eventId, PathAndFlushNoWait, path, second);
            return WaitForShellView(folderView, expectedCount, presentPath, absentPath);
        }
        finally { if (second != 0) Marshal.FreeHGlobal(second); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, nint item2);

    private static bool ShellViewContainsPath(nint folderView, string path)
    {
        // A renamed/deleted fixture path cannot be parsed into a fresh PIDL; absence itself means no match.
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        Check(SHParseDisplayName(path, 0, out var absolutePidl, 0, out _));
        try
        {
            var targetChild = ILFindLastID(absolutePidl);
            var countResult = At<ItemCount>(folderView, 7)(folderView, 2, out var count);
            Check(countResult);
            for (var index = 0; index < count; index++)
            {
                Check(At<GetFolderItem>(folderView, 6)(folderView, index, out var childPidl));
                try
                {
                    if (ILIsEqual(childPidl, targetChild)) return true;
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            return false;
        }
        finally { Marshal.FreeCoTaskMem(absolutePidl); }
    }

    private static int? ReadShellViewCount(nint folderView)
    {
        var result = At<ItemCount>(folderView, 7)(folderView, 2, out var count);
        return result < 0 ? null : count;
    }

    private static int WaitForShellViewItemCount(nint folderView, int expectedCount, int timeoutMilliseconds = 5000)
    {
        // DefView enumeration can finish after CreateViewWindow and the first message pump.
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        var observedCount = -1;
        while (Environment.TickCount64 < deadline)
        {
            Pump(50);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out observedCount));
            if (observedCount == expectedCount) return observedCount;
        }
        throw new Exception($"The isolated Shell fixture did not finish initial enumeration: expected={expectedCount}, observed={observedCount}.");
    }

    private static nint ObserveCustomDraw(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        const uint WmNotify = 0x004E;
        const int NmCustomDraw = -12;
        const uint CddsPrePaint = 0x00000001;
        const uint CddsItemPrePaint = 0x00010001;
        const uint CddsItemPostPaint = 0x00010002;
        const nint CdrfNotifyItemDraw = 0x00000020;
        const nint CdrfNewFont = 0x00000002;
        const nint CdrfNotifyPostPaint = 0x00000010;
        if (message == WmNotify && lParam != 0)
        {
            var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
            if (header.Code == NmCustomDraw)
            {
                var draw = Marshal.PtrToStructure<CustomDraw>(lParam);
                if (CustomDrawEventDetails.Count < 128)
                {
                    var clip = new Rect();
                    var clipHr = draw.DeviceContext == 0 ? 0 : GetClipBox(draw.DeviceContext, out clip);
                    var clipText = draw.DeviceContext == 0 ? "none" : $"{clipHr}:{clip.Left},{clip.Top},{clip.Right},{clip.Bottom}";
                    CustomDrawEventDetails.Add($"sender={WindowClassName(header.Window)},is-list={header.Window == _observedListView}," +
                        $"stage=0x{draw.Stage:X},item={draw.Item},rc={draw.Bounds.Left},{draw.Bounds.Top},{draw.Bounds.Right},{draw.Bounds.Bottom}," +
                        $"dc-type={GetObjectType(draw.DeviceContext)},clip={clipText}");
                }
                if (draw.Stage == CddsPrePaint)
                {
                    _customDrawPrepaint++;
                    return CdrfNotifyItemDraw;
                }
                if (draw.Stage == CddsItemPrePaint)
                {
                    _customDrawItems++;
                    CustomDrawItemStates[draw.Item] = draw.ItemState;
                    if (!ShellViewColorPolicy.TryGetColors(draw.Item, _customDrawSelection,
                            CustomDrawTextColors, CustomDrawBackgrounds, out var colors, CustomDrawFontStyles))
                        return 0; // CDRF_DODEFAULT
                    if (draw.Bounds.Right > draw.Bounds.Left && draw.Bounds.Bottom > draw.Bounds.Top)
                        CustomDrawBounds[draw.Item] = draw.Bounds;
                    CustomDrawDeviceContexts[draw.Item] = draw.DeviceContext;
                    // NMCUSTOMDRAW.HDC is callback-scoped; inspect it before returning to the Shell.
                    CustomDrawDeviceContextTypes[draw.Item] = GetObjectType(draw.DeviceContext);
                    // COLORREF is 0x00BBGGRR. Preserve Shell's native value for properties
                    // that the matching Q-Dir rule leaves unset.
                    if (colors.Text is { } textColor)
                        Marshal.WriteInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextColor)).ToInt32(), unchecked((int)textColor));
                    if (colors.Background is { } backgroundColor)
                        Marshal.WriteInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextBackground)).ToInt32(), unchecked((int)backgroundColor));
                    if (ShellViewColorFontPolicy.TrySelect(draw.DeviceContext, colors.FontStyle, out var fontSelection))
                        CustomDrawFontSelections[draw.Item] = fontSelection;
                    CustomDrawColors[draw.Item] = $"text=0x{unchecked((uint)Marshal.ReadInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextColor)).ToInt32())):X6},background=0x{unchecked((uint)Marshal.ReadInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextBackground)).ToInt32())):X6}";
                    return CdrfNewFont | CdrfNotifyPostPaint;
                }
                if (draw.Stage == CddsItemPostPaint)
                {
                    // Some DefView notifications leave rc empty at ITEMPOSTPAINT; retain the
                    // ITEMPREPAINT bounds and sample them while the post-paint HDC is still valid.
                    if (draw.DeviceContext != 0 && CustomDrawLabelBounds.TryGetValue(draw.Item, out var itemBounds) &&
                        itemBounds.Right > itemBounds.Left && itemBounds.Bottom > itemBounds.Top)
                    {
                        var x = Math.Clamp((itemBounds.Left + itemBounds.Right) / 2, itemBounds.Left, itemBounds.Right - 1);
                        var y = Math.Clamp((itemBounds.Top + itemBounds.Bottom) / 2, itemBounds.Top, itemBounds.Bottom - 1);
                        CustomDrawPostPaintPixels[draw.Item] = GetPixel(draw.DeviceContext, x, y);
                    }
                    else CustomDrawPostPaintPixels[draw.Item] = uint.MaxValue;
                    if (CustomDrawFontSelections.Remove(draw.Item, out var fontSelection))
                        ShellViewColorFontPolicy.Restore(fontSelection);
                    return 0;
                }
            }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous,
        ref StreamProbeSettings settings, nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SaveViewState(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolderUiObject(nint self, nint owner, uint itemCount,
        nint childPidls, ref Guid iid, nint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, nuint commandId, uint flags, nint reserved, nint name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TranslateAccelerator(nint self, ref NativeMessage message);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectFolderItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFocusedItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolderItem(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItemByPidl(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewModeAndIconSize(nint self, out uint mode, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewModeAndIconSize(nint self, uint mode, int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentViewMode(nint self, out uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetCurrentViewMode(nint self, uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, [In] SortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumns(nint self, [Out] SortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroupBy(nint self, ref PropertyKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroupBy(nint self, out PropertyKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentFolderFlags(nint self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetCurrentFolderFlags(nint self, uint mask, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolderViewOptions(nint self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderViewOptions(nint self, uint mask, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int CopyItem(nint self, nint item, nint destination,
        [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int RenameItem(nint self, nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItem(nint self, nint item, nint sink);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Sequential)] private struct SortColumn { public PropertyKey Key; public int Direction; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInvokeInfo
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
    [StructLayout(LayoutKind.Sequential)] private struct CustomDraw
    {
        public NotifyHeader Header;
        public uint Stage;
        public nint DeviceContext;
        public Rect Bounds;
        public nuint Item;
        public uint ItemState;
        public nint ItemData;
        public uint TextColor;
        public uint TextBackground;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }
    private sealed record MenuCommand(string Verb, uint Offset, uint State);
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path,
        nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint absoluteParent, nint relativeChild);
    [DllImport("shell32.dll")] private static extern bool ILRemoveLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll", EntryPoint = "ILFindLastID")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind,
        in Guid iid, out nint value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, ref Guid interfaceId, out nint item);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid classId, nint outer,
        uint context, ref Guid interfaceId, out nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex,
        string cls, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage,
        out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetObjectType(nint value);
    [DllImport("gdi32.dll")] private static extern int GetClipBox(nint dc, out Rect rect);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern nint GetMenu(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
}
