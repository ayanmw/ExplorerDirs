using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class PaneTabTransferProbe
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(bool unhostedCopyOrder = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.TabTransferProbe", Guid.NewGuid().ToString("N"));
        var sourceA = Path.Combine(root, "source-current");
        var sourceB = Path.Combine(root, "source-other");
        var targetPath = Path.Combine(root, "target");
        foreach (var path in new[] { sourceA, sourceB, targetPath })
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "probe.txt"), path);
        }

        HwndSource? host = null;
        UIElement? sourcePane = null;
        UIElement? targetPane = null;
        var foregroundBefore = GetForegroundWindow();
        try
        {
            var app = Assembly.Load("ExplorerDirs.App");
            var paneType = app.GetType("ExplorerDirs.App.PaneControl", throwOnError: true)!;
            var viewStateType = app.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var tabSettingsType = app.GetType("ExplorerDirs.App.TabSettings", throwOnError: true)!;
            var favoritesStoreType = app.GetType("ExplorerDirs.App.QuickFilterFavoritesStore", throwOnError: true)!;
            var historyStoreType = app.GetType("ExplorerDirs.App.QuickFilterHistoryStore", throwOnError: true)!;
            var hiddenPreferenceType = app.GetType("ExplorerDirs.App.HiddenItemsPreference", throwOnError: true)!;
            var defaultView = viewStateType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var viewFactoryType = typeof(Func<>).MakeGenericType(viewStateType);
            var viewFactory = System.Linq.Expressions.Expression.Lambda(viewFactoryType,
                System.Linq.Expressions.Expression.Call(defaultView,
                    System.Linq.Expressions.Expression.Constant(4u), System.Linq.Expressions.Expression.Constant(-1))).Compile();
            var view = defaultView.Invoke(null, [4u, -1])!;
            var emptyFilter = app.GetType("ExplorerDirs.App.QuickFilterSettings", true)!
                .GetProperty("Empty", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null);
            var favoriteStore = Activator.CreateInstance(favoritesStoreType, InstanceMembers, null,
                [Path.Combine(root, "QuickFilterFavorites.ini")], null)!;
            var historyStore = Activator.CreateInstance(historyStoreType, InstanceMembers, null,
                [Path.Combine(root, "QuickFilterHistory.ini")], null)!;
            var followWindows = Enum.Parse(hiddenPreferenceType, "FollowWindows");
            var sourceTabs = Array.CreateInstance(tabSettingsType, 2);
            sourceTabs.SetValue(Activator.CreateInstance(tabSettingsType, [sourceA, view, emptyFilter, false, false]), 0);
            sourceTabs.SetValue(Activator.CreateInstance(tabSettingsType, [sourceB, view, emptyFilter, false, false]), 1);
            var targetTabs = Array.CreateInstance(tabSettingsType, 1);
            targetTabs.SetValue(Activator.CreateInstance(tabSettingsType, [targetPath, view, emptyFilter, false, false]), 0);
            sourcePane = (UIElement)Activator.CreateInstance(paneType, InstanceMembers, null,
                [sourceTabs, viewFactory, null, 0, favoriteStore, historyStore, followWindows, false, null], null)!;
            targetPane = (UIElement)Activator.CreateInstance(paneType, InstanceMembers, null,
                [targetTabs, viewFactory, null, 0, favoriteStore, historyStore, followWindows, false, null], null)!;

            if (unhostedCopyOrder)
            {
                CheckUnhostedActivePaneGeometry(sourcePane);
                CheckUnhostedCopyOrder(sourcePane, targetPane);
                CheckUnhostedSettledPath(sourcePane, targetPane);
                CheckUnhostedReopenHistory(sourcePane);
                CheckUnhostedFilterFailures(sourcePane);
                CheckUnhostedFilterGroupSnapshot(sourcePane);
                CheckUnhostedHiddenHover(sourcePane);
                CheckUnhostedRetiredScanFailure(sourcePane, root);
                CheckUnhostedDeferredFocus(sourcePane);
                CheckUnhostedTabLimit(sourcePane, targetPane);
                EnsureForegroundUnchanged(foregroundBefore, "checking unattached pane tab order");
                Console.WriteLine("Unhosted tab copy order: PASS (production pane state; no HWND or Shell view created)");
                return 0;
            }

            var rootGrid = new Grid();
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(500) });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(700) });
            Grid.SetColumn(targetPane, 1);
            rootGrid.Children.Add(sourcePane);
            rootGrid.Children.Add(targetPane);
            host = new HwndSource(new HwndSourceParameters("Explorer Dirs hidden tab transfer probe")
            {
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000000, // WS_EX_NOACTIVATE keeps the hidden probe from stealing user focus.
                Width = 1200,
                Height = 600
            });
            host.RootVisual = rootGrid;
            rootGrid.Measure(new Size(1200, 600));
            rootGrid.Arrange(new Rect(0, 0, 1200, 600));
            rootGrid.UpdateLayout();
            EnsureForegroundUnchanged(foregroundBefore, "building hidden panes");

            var sourceTabsControl = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(sourcePane)!;
            var targetTabsControl = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(targetPane)!;
            PumpUntil(() => sourceTabsControl.Items.Count == 2 && targetTabsControl.Items.Count == 1 &&
                ReadBrowserPath(sourceTabsControl.SelectedItem!) == sourceA,
                TimeSpan.FromSeconds(15), "initial Shell views became ready");
            var movedTab = (TabItem)sourceTabsControl.SelectedItem!;
            var state = movedTab.Tag!;
            var stateType = state.GetType();
            var browserProperty = stateType.GetProperty("Browser", InstanceMembers)!;
            var browser = browserProperty.GetValue(state)!;
            var browserType = browser.GetType();
            var navigate = browserType.GetMethod("Navigate", InstanceMembers)!;
            navigate.Invoke(browser, [sourceB]);
            PumpUntil(() => ReadBrowserPath(movedTab) == sourceB &&
                (bool)browserType.GetProperty("CanGoBack", InstanceMembers)!.GetValue(browser)!,
                TimeSpan.FromSeconds(15), "the source browser created a back-history entry");

            var hostWindowField = browserType.GetField("_hostWindow", InstanceMembers)!;
            var shellHostWindow = (IntPtr)hostWindowField.GetValue(browser)!;
            var oldParent = GetParent(shellHostWindow);
            Ensure(GetWindowRect(shellHostWindow, out var oldRect), "read the source HwndHost bounds");
            var transfer = paneType.GetMethod("TransferTabTo", InstanceMembers)!;
            var moved = (bool)transfer.Invoke(sourcePane, [state, targetPane, null, true])!;
            Ensure(moved, "PaneControl accepted the cross-pane move");
            PumpUntil(() => targetTabsControl.Items.Count == 2 && sourceTabsControl.Items.Count == 1 &&
                ReferenceEquals(targetTabsControl.SelectedItem, movedTab),
                TimeSpan.FromSeconds(10), "the original TabItem moved between panes");
            Ensure(ReferenceEquals(browserProperty.GetValue(state), browser), "the ExplorerBrowserHost object was retained");
            Ensure((IntPtr)hostWindowField.GetValue(browser)! == shellHostWindow && IsWindow(shellHostWindow),
                "the native ExplorerBrowser host HWND was retained");
            var browserLayerField = paneType.GetField("BrowserLayer", InstanceMembers)!;
            var sourceBrowserLayer = (UIElement)browserLayerField.GetValue(sourcePane)!;
            var targetBrowserLayer = (UIElement)browserLayerField.GetValue(targetPane)!;
            Ensure(!((Panel)sourceBrowserLayer).Children.Contains((UIElement)browser) &&
                   ((Panel)targetBrowserLayer).Children.Contains((UIElement)browser),
                "the retained HwndHost moved from the source to the destination pane visual host");
            Ensure(GetParent(shellHostWindow) == oldParent,
                "both pane HwndHosts retain their valid shared WPF native parent");
            PumpUntil(() => GetWindowRect(shellHostWindow, out var bounds) && bounds.Right - bounds.Left != oldRect.Right - oldRect.Left,
                TimeSpan.FromSeconds(5), "WPF resized the retained browser host to the destination pane");
            Ensure(string.Equals(ReadBrowserPath(movedTab), sourceB, StringComparison.OrdinalIgnoreCase),
                "the current navigation location survived the move");
            Ensure((bool)browserType.GetProperty("CanGoBack", InstanceMembers)!.GetValue(browser)!,
                "the original Shell navigation history remains available after the move");

            paneType.GetMethod("GoBack", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(ReadBrowserPath(movedTab), sourceA, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)stateType.GetProperty("Path", InstanceMembers)!.GetValue(state)!, sourceA,
                    StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "Back navigated through the pre-move Shell history");

            var copy = (bool)paneType.GetMethod("CopyTabFrom", InstanceMembers)!
                .Invoke(sourcePane, [state, null, true])!;
            Ensure(copy, "Ctrl-drag copy can create a tab in a pane whose only tab was moved away");
            PumpUntil(() => sourceTabsControl.Items.Count == 2 &&
                string.Equals(ReadBrowserPath(sourceTabsControl.SelectedItem!), sourceA, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the independent copied browser became ready");
            var copiedState = ((TabItem)sourceTabsControl.SelectedItem!).Tag!;
            var copiedBrowser = browserProperty.GetValue(copiedState)!;
            Ensure(!ReferenceEquals(copiedState, state) && !ReferenceEquals(copiedBrowser, browser),
                "copy creates an independent tab and Shell host");

            paneType.GetMethod("CloseTab", InstanceMembers)!.Invoke(targetPane, [state]);
            Ensure(targetTabsControl.Items.Count == 1 && !targetTabsControl.Items.Contains(movedTab),
                "the transferred tab can be closed normally");
            Ensure(!IsWindow(shellHostWindow) && browserType.GetField("_browser", InstanceMembers)!.GetValue(browser) is null,
                "closing the transferred tab destroys the original HWND and releases the browser");

            EnsureForegroundUnchanged(foregroundBefore, "moving and navigating a tab");
            Console.WriteLine("Cross-pane tab transfer host probe: PASS (move retains TabState/HWND/history; copy is independent; close releases the moved host)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (unhostedCopyOrder)
            {
                DisposeUnhostedBrowsers(sourcePane);
                DisposeUnhostedBrowsers(targetPane);
            }
            try { host?.Dispose(); } catch { }
            try { (sourcePane as IDisposable)?.Dispose(); } catch { }
            try { (targetPane as IDisposable)?.Dispose(); } catch { }
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void CheckUnhostedRetiredScanFailure(UIElement pane, string root)
    {
        var type = pane.GetType();
        var tabs = (TabControl)type.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var owner = ((TabItem)tabs.SelectedItem).Tag;
        var resultField = type.GetField("_lastRecursiveSizeResult", InstanceMembers)!;
        var pathField = type.GetField("_lastRecursiveSizePath", InstanceMembers)!;
        var ownerField = type.GetField("_lastRecursiveSizeOwner", InstanceMembers)!;
        var previous = (resultField.GetValue(pane), pathField.GetValue(pane), ownerField.GetValue(pane));
        var result = Activator.CreateInstance(resultField.FieldType, [123UL, 1L, 0L, 0L])!;
        resultField.SetValue(pane, result);
        pathField.SetValue(pane, root);
        ownerField.SetValue(pane, owner);
        using var retired = new CancellationTokenSource();
        try
        {
            var task = (Task)type.GetMethod("ScanRecursiveDirectoryAsync", InstanceMembers)!.Invoke(pane,
                [owner, Path.Combine(root, "missing-scan-" + Guid.NewGuid().ToString("N")), retired, null])!;
            PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5), "retired local scan failure completed");
            task.GetAwaiter().GetResult();
            Ensure(ReferenceEquals(resultField.GetValue(pane), result) &&
                Equals(pathField.GetValue(pane), root) && ReferenceEquals(ownerField.GetValue(pane), owner),
                "a retired scan failure preserves a newer recursive-size result");
        }
        finally
        {
            resultField.SetValue(pane, previous.Item1);
            pathField.SetValue(pane, previous.Item2);
            ownerField.SetValue(pane, previous.Item3);
        }
    }

    private static void CheckUnhostedHiddenHover(UIElement pane)
    {
        var type = pane.GetType();
        var tabs = (TabControl)type.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var original = tabs.SelectedItem;
        var target = tabs.Items.OfType<TabItem>().First(tab => !ReferenceEquals(tab, original));
        var gate = type.GetField("_tabHoverActivationGate", InstanceMembers)!.GetValue(pane)!;
        var previousVisibility = pane.Visibility;
        pane.Visibility = Visibility.Collapsed;
        try
        {
            gate.GetType().GetMethod("Begin", InstanceMembers)!.Invoke(gate, [target, false, true, 1000]);
            type.GetMethod("TabHoverActivationTimer_Tick", InstanceMembers)!.Invoke(pane, [null, EventArgs.Empty]);
            Ensure(ReferenceEquals(tabs.SelectedItem, original),
                "a retired hover timer cannot switch tabs in a hidden pane");
            Ensure(!(bool)gate.GetType().GetProperty("HasPendingTarget", InstanceMembers)!.GetValue(gate)!,
                "a hidden pane consumes its obsolete hover target");
        }
        finally
        {
            pane.Visibility = previousVisibility;
            tabs.SelectedItem = original;
        }
    }

    private static void CheckUnhostedCopyOrder(UIElement source, UIElement destination)
    {
        var paneType = source.GetType();
        var sourceTabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(source)!;
        var targetTabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(destination)!;
        var order = (List<TabItem>)paneType.GetField("_tabOrder", InstanceMembers)!.GetValue(destination)!;
        var original = (TabItem)sourceTabs.Items[0];
        var ordinary = (TabItem)targetTabs.Items[0];
        paneType.GetMethod("TogglePinned", InstanceMembers)!.Invoke(source, [original.Tag]);
        var copyMethod = paneType.GetMethod("CopyTabFrom", InstanceMembers)!;

        TabItem Copy(TabItem? target, bool after)
        {
            Ensure((bool)copyMethod.Invoke(destination, [original.Tag, target, after])!, "copy accepted");
            return (TabItem)targetTabs.SelectedItem!;
        }

        var first = Copy(null, true);
        Ensure(order.SequenceEqual(new[] { first, ordinary }),
            "a copied pinned tab stays before ordinary tabs when dropped on blank tab-strip space");
        var before = Copy(first, false);
        var after = Copy(first, true);
        var appended = Copy(null, true);
        Ensure(order.SequenceEqual(new[] { before, first, after, appended, ordinary }),
            "before, after and blank-space copies all preserve the pinned partition");
        Ensure((int)paneType.GetProperty("SelectedTabIndex", InstanceMembers)!.GetValue(destination)! == 3,
            "selected-tab persistence follows the displayed copy position");
        var snapshot = (System.Collections.IEnumerable)paneType.GetMethod("SnapshotTabs", InstanceMembers)!
            .Invoke(destination, null)!;
        var savedPaths = snapshot.Cast<object>().Select(tab =>
            (string)tab.GetType().GetProperty("Path", InstanceMembers)!.GetValue(tab)!).ToArray();
        Ensure(savedPaths.SequenceEqual(order.Select(ReadBrowserPath)), "session snapshot follows displayed order");

        paneType.GetMethod("TogglePinned", InstanceMembers)!.Invoke(source, [original.Tag]);
        var beforeOrdinary = Copy(ordinary, false);
        var afterOrdinary = Copy(ordinary, true);
        var appendedOrdinary = Copy(null, true);
        Ensure(order.SequenceEqual(new[] { before, first, after, appended,
            beforeOrdinary, ordinary, afterOrdinary, appendedOrdinary }),
            "ordinary copies preserve target-relative positions and append after the pinned partition");
        Ensure(sourceTabs.Items.Count == 2 && sourceTabs.Items.Contains(original), "copy preserves the source tab");
        var browsers = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var tab in sourceTabs.Items.Cast<TabItem>().Concat(targetTabs.Items.Cast<TabItem>()))
        {
            var browser = tab.Tag.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(tab.Tag)!;
            Ensure(browsers.Add(browser), "each copied tab owns an independent host object");
            Ensure(browser.GetType().GetField("_browser", InstanceMembers)!.GetValue(browser) is null &&
                (nint)browser.GetType().GetField("_hostWindow", InstanceMembers)!.GetValue(browser)! == 0,
                "unattached panes never created a Shell browser or native host");
        }
    }

    private static void CheckUnhostedSettledPath(UIElement source, UIElement destination)
    {
        var paneType = source.GetType();
        var tabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(source)!;
        var original = (TabItem)tabs.Items[0];
        tabs.SelectedItem = original;
        var state = original.Tag;
        var stateType = state.GetType();
        var browser = stateType.GetProperty("Browser", InstanceMembers)!.GetValue(state)!;
        var oldPath = (string)stateType.GetProperty("Path", InstanceMembers)!.GetValue(state)!;
        var nextPath = ReadBrowserPath(tabs.Items[1]);
        Ensure(oldPath != nextPath, "settled-path fixture starts with distinct directories");
        // An unattached host's Navigate updates its directory without publishing
        // the pane notice. This models the production interval before queued
        // Navigated delivery; it does not model native navigation timing.
        browser.GetType().GetMethod("Navigate", InstanceMembers)!.Invoke(browser, [nextPath]);
        var address = (TextBox)paneType.GetField("Address", InstanceMembers)!.GetValue(source)!;
        Ensure(address.Text == oldPath,
            "pane presentation still has the earlier directory before notification");
        Ensure((string)paneType.GetProperty("CurrentPath", InstanceMembers)!.GetValue(source)! == nextPath,
            "current-directory commands use the settled path before pane notification");
        stateType.GetProperty("SearchRequestPending", InstanceMembers)!.SetValue(state, true);
        paneType.GetMethod("CloseSearch", InstanceMembers)!.Invoke(source, null);
        Ensure(ReadBrowserPath(original) == nextPath,
            "closing a pending search does not navigate back to the stale pane directory");
        var snapshots = ((System.Collections.IEnumerable)paneType.GetMethod("SnapshotTabs", InstanceMembers)!
            .Invoke(source, null)!).Cast<object>().ToArray();
        Ensure((string)snapshots[0].GetType().GetProperty("Path", InstanceMembers)!.GetValue(snapshots[0])! == nextPath,
            "session snapshot uses the browser's settled directory before its pane notice");
        paneType.GetMethod("TogglePinned", InstanceMembers)!.Invoke(source, [state]);
        var pins = ((System.Collections.IEnumerable)paneType.GetMethod("SnapshotPins", InstanceMembers)!
            .Invoke(source, [0])!).Cast<object>().ToArray();
        Ensure(pins.Length == 1 && (string)pins[0].GetType().GetProperty("Path", InstanceMembers)!.GetValue(pins[0])! == nextPath,
            "pinned metadata agrees with the settled session directory");
        paneType.GetMethod("TogglePinned", InstanceMembers)!.Invoke(source, [state]);
        paneType.GetField("_newTabUsesCurrentFolder", InstanceMembers)!.SetValue(source, true);
        paneType.GetMethod("NewTab", InstanceMembers)!.Invoke(source, null);
        Ensure(ReadBrowserPath(tabs.SelectedItem!) == nextPath,
            "new-tab current-folder mode follows the settled directory");
        tabs.SelectedItem = original;
        paneType.GetMethod("FocusAddress", InstanceMembers)!.Invoke(source, null);
        Ensure(address.Text == nextPath, "address editing starts from the settled directory");
        paneType.GetMethod("DuplicateCurrentTab", InstanceMembers)!.Invoke(source, null);
        Ensure(ReadBrowserPath(tabs.SelectedItem!) == nextPath,
            "duplicating before notice delivery opens the settled directory");
        Ensure((bool)paneType.GetMethod("CopyTabFrom", InstanceMembers)!.Invoke(destination, [state, null, true])!,
            "cross-pane copy accepts the settled tab");
        var targetTabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(destination)!;
        Ensure(ReadBrowserPath(targetTabs.SelectedItem!) == nextPath,
            "cross-pane copy also opens the settled directory");
        paneType.GetMethod("CloseTab", InstanceMembers)!.Invoke(source, [state]);
        paneType.GetMethod("ReopenClosedTab", InstanceMembers)!.Invoke(source, null);
        Ensure(ReadBrowserPath(tabs.SelectedItem!) == nextPath,
            "close and reopen before notice delivery retain the settled directory");
        foreach (var tab in tabs.Items.Cast<TabItem>().Concat(targetTabs.Items.Cast<TabItem>()))
        {
            var host = stateType.GetProperty("Browser", InstanceMembers)!.GetValue(tab.Tag)!;
            Ensure(host.GetType().GetField("_browser", InstanceMembers)!.GetValue(host) is null &&
                (nint)host.GetType().GetField("_hostWindow", InstanceMembers)!.GetValue(host)! == 0,
                "settled-path snapshot checks never create a native Shell view");
        }
    }

    private static void CheckUnhostedFilterGroupSnapshot(UIElement pane)
    {
        var paneType = pane.GetType();
        var tabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var state = ((TabItem)tabs.SelectedItem!).Tag;
        var browser = state.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(state)!;
        var hostType = browser.GetType();
        var keyType = hostType.Assembly.GetType("ExplorerDirs.App.PropertyKeyState", true)!;
        var key = Activator.CreateInstance(keyType, [Guid.NewGuid(), 42u])!;
        var request = (long)hostType.GetField("_quickFilterRequestVersion", InstanceMembers)!.GetValue(browser)!;
        hostType.GetMethod("QueueQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser, [key, false, 5, request]);
        try
        {
            var cached = hostType.GetMethod("CachedViewState", InstanceMembers)!.Invoke(browser, null)!;
            var viewType = cached.GetType();
            Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(cached), key) &&
                !(bool)viewType.GetProperty("GroupAscending", InstanceMembers)!.GetValue(cached)!,
                "pending filter grouping is retained in the saved cache before any native capture");
            var timer = hostType.GetField("_quickFilterGroupRestoreTimer", InstanceMembers)!.GetValue(browser);
            var retiredKey = Activator.CreateInstance(keyType, [Guid.NewGuid(), 43u])!;
            hostType.GetMethod("QueueQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser,
                [retiredKey, true, 5, request - 1]);
            var afterRetired = hostType.GetMethod("CachedViewState", InstanceMembers)!.Invoke(browser, null)!;
            Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(afterRetired), key) &&
                ReferenceEquals(timer, hostType.GetField("_quickFilterGroupRestoreTimer", InstanceMembers)!.GetValue(browser)),
                "a retired refresh callback cannot replace the current group intent or timer");
            // Model a complete native snapshot taken while grouping is disabled.
            // Other fields must still come from that snapshot, not the old cache.
            viewType.GetProperty("GroupKey", InstanceMembers)!.SetValue(cached, null);
            viewType.GetProperty("Mode", InstanceMembers)!.SetValue(cached, 3u);
            viewType.GetProperty("IconSize", InstanceMembers)!.SetValue(cached, 24);
            var merge = hostType.GetMethod("PreserveQuickFilterGroup", InstanceMembers)!;
            void Check(object merged)
            {
                Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(merged), key) &&
                    !(bool)viewType.GetProperty("GroupAscending", InstanceMembers)!.GetValue(merged)! &&
                    (uint)viewType.GetProperty("Mode", InstanceMembers)!.GetValue(merged)! == 3 &&
                    (int)viewType.GetProperty("IconSize", InstanceMembers)!.GetValue(merged)! == 24,
                    "temporary ungrouping preserves group identity without discarding the current view mode");
            }
            Check(merge.Invoke(browser, [cached])!);
            hostType.GetMethod("CancelPendingQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser, null);
            Check(merge.Invoke(browser, [cached])!);
            Ensure(hostType.GetField("_quickFilterGroupRestoreTimer", InstanceMembers)!.GetValue(browser) is null,
                "retiring a group timer keeps the desired settings without leaving the timer alive");
            hostType.GetMethod("QueueQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser, [key, false, 0, request]);
            // Zero initial rows tries to restore immediately; without a native
            // view it fails, but that must not erase the saved grouping intent.
            var snapshots = ((System.Collections.IEnumerable)paneType.GetMethod("SnapshotTabs", InstanceMembers)!
                .Invoke(pane, null)!).Cast<object>().ToArray();
            var selected = (int)paneType.GetProperty("SelectedTabIndex", InstanceMembers)!.GetValue(pane)!;
            var savedView = snapshots[selected].GetType().GetProperty("View", InstanceMembers)!.GetValue(snapshots[selected])!;
            Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(savedView), key) &&
                !(bool)viewType.GetProperty("GroupAscending", InstanceMembers)!.GetValue(savedView)!,
                "a failed group restore still saves the intended key and direction rather than None");
            hostType.GetMethod("QueueQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser, [key, false, 5, request]);
            var observed = viewType.GetMethod("Copy", InstanceMembers)!.Invoke(cached, null)!;
            viewType.GetProperty("GroupKey", InstanceMembers)!.SetValue(observed, retiredKey);
            viewType.GetProperty("GroupAscending", InstanceMembers)!.SetValue(observed, true);
            var accepted = merge.Invoke(browser, [observed])!;
            Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(accepted), retiredKey) &&
                (bool)viewType.GetProperty("GroupAscending", InstanceMembers)!.GetValue(accepted)!,
                "a newly observed native group takes precedence over the filter's older intent");
            Ensure(hostType.GetField("_quickFilterGroupRestoreTimer", InstanceMembers)!.GetValue(browser) is null &&
                hostType.GetField("_pendingQuickFilterGroupRestore", InstanceMembers)!.GetValue(browser) is null &&
                (long)hostType.GetField("_quickFilterRequestVersion", InstanceMembers)!.GetValue(browser)! > request,
                "accepting a different native group retires the old timer and queued refresh request");
            request = (long)hostType.GetField("_quickFilterRequestVersion", InstanceMembers)!.GetValue(browser)!;
            hostType.GetMethod("QueueQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser, [retiredKey, true, 5, request]);
            var remembered = hostType.GetMethod("CachedViewState", InstanceMembers)!.Invoke(browser, null)!;
            Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(remembered), retiredKey) &&
                (bool)viewType.GetProperty("GroupAscending", InstanceMembers)!.GetValue(remembered)! &&
                hostType.GetField("_quickFilterGroupRestoreTimer", InstanceMembers)!.GetValue(browser) is not null,
                "remembering a newly confirmed group replaces an older cache without cancelling its own queue");
            viewType.GetProperty("GroupAscending", InstanceMembers)!.SetValue(observed, false);
            accepted = merge.Invoke(browser, [observed])!;
            Ensure(Equals(viewType.GetProperty("GroupKey", InstanceMembers)!.GetValue(accepted), retiredKey) &&
                !(bool)viewType.GetProperty("GroupAscending", InstanceMembers)!.GetValue(accepted)! &&
                hostType.GetField("_quickFilterGroupRestoreTimer", InstanceMembers)!.GetValue(browser) is null,
                "a native direction-only change also takes precedence over the old restore intent");
            Ensure(hostType.GetField("_browser", InstanceMembers)!.GetValue(browser) is null &&
                (nint)hostType.GetField("_hostWindow", InstanceMembers)!.GetValue(browser)! == 0,
                "group snapshot checks never create a native view");
        }
        finally { hostType.GetMethod("CancelPendingQuickFilterGroupRestore", InstanceMembers)!.Invoke(browser, null); }
    }

    private static void CheckUnhostedFilterFailures(UIElement pane)
    {
        var paneType = pane.GetType();
        var tabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var state = ((TabItem)tabs.SelectedItem!).Tag;
        var browser = state.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(state)!;
        var hostType = browser.GetType();
        var filterProperty = hostType.GetProperty("QuickFilter", InstanceMembers)!;
        var filterType = filterProperty.PropertyType;
        var pattern = filterType.GetProperty("Pattern", InstanceMembers)!;
        var initial = Activator.CreateInstance(filterType, ["*.txt", false, false])!;
        Ensure((bool)hostType.GetMethod("SetQuickFilter", InstanceMembers)!.Invoke(browser, [initial])!,
            "an unattached tab accepts a filter for its future view");
        paneType.GetMethod("FocusQuickFilter", InstanceMembers)!.Invoke(pane, null);
        var editor = (ComboBox)paneType.GetField("QuickFilterBox", InstanceMembers)!.GetValue(pane)!;
        var bar = (FrameworkElement)paneType.GetField("QuickFilterBar", InstanceMembers)!.GetValue(pane)!;
        var status = (TextBlock)paneType.GetField("Status", InstanceMembers)!.GetValue(pane)!;
        var changed = hostType.GetEvent("QuickFilterChanged", InstanceMembers)!;
        var reject = false;
        var rejectRollback = false;
        Action failOnce = () =>
        {
            if (!reject && !rejectRollback) return;
            reject = false;
            throw new COMException("synthetic filter notification failure", unchecked((int)0x80004005));
        };
        var callback = System.Linq.Expressions.Expression.Lambda(changed.EventHandlerType!,
            System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(failOnce)),
            System.Linq.Expressions.Expression.Parameter(filterType)).Compile();
        changed.GetAddMethod(true)!.Invoke(browser, [callback]);
        try
        {
            reject = true;
            editor.Text = "*.log";
            var mode = (ComboBox)paneType.GetField("QuickFilterMode", InstanceMembers)!.GetValue(pane)!;
            var folders = (CheckBox)paneType.GetField("FilterFoldersCheckBox", InstanceMembers)!.GetValue(pane)!;
            mode.SelectedIndex = 1;
            folders.IsChecked = true;
            paneType.GetMethod("ApplyQuickFilter", InstanceMembers)!.Invoke(pane, null);
            Ensure(ReferenceEquals(filterProperty.GetValue(browser), initial) && editor.Text == "*.txt" &&
                mode.SelectedIndex == 0 && folders.IsChecked == false &&
                status.Text.StartsWith("应用筛选失败", StringComparison.Ordinal),
                "failed apply rolls back settings/editor and reports locally without escaping the pane");
            var history = paneType.GetField("_quickFilterHistoryStore", InstanceMembers)!.GetValue(pane)!;
            Ensure(!((IEnumerable<string>)history.GetType().GetMethod("Load", InstanceMembers)!.Invoke(history, null)!).Contains("*.log"),
                "rejected filter changes are not recorded as successful history");
            reject = true;
            paneType.GetMethod("ClearQuickFilter", InstanceMembers)!.Invoke(pane, null);
            Ensure((string)pattern.GetValue(filterProperty.GetValue(browser))! == "*.txt" && editor.Text == "*.txt" &&
                bar.Visibility == Visibility.Visible && status.Text.StartsWith("清除筛选失败", StringComparison.Ordinal),
                "failed clear keeps the previous filter and its editor available");
            rejectRollback = true;
            editor.Text = "*.bad";
            paneType.GetMethod("ApplyQuickFilter", InstanceMembers)!.Invoke(pane, null);
            Ensure((string)pattern.GetValue(filterProperty.GetValue(browser))! == "*.txt" &&
                status.Text.Contains("恢复原状态也未完整完成", StringComparison.Ordinal),
                "rollback notification failure is reported explicitly without escaping the pane");
        }
        finally { changed.GetRemoveMethod(true)!.Invoke(browser, [callback]); }
        editor.Text = "*.csv";
        paneType.GetMethod("ApplyQuickFilter", InstanceMembers)!.Invoke(pane, null);
        Ensure((string)pattern.GetValue(filterProperty.GetValue(browser))! == "*.csv",
            "a later successful filter can replace the restored one");
        Ensure(hostType.GetField("_browser", InstanceMembers)!.GetValue(browser) is null &&
            (nint)hostType.GetField("_hostWindow", InstanceMembers)!.GetValue(browser)! == 0,
            "filter failure checks never create a Shell view");
    }

    private static void CheckUnhostedReopenHistory(UIElement pane)
    {
        var paneType = pane.GetType();
        var tabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var original = (TabItem)tabs.Items[0];
        var stateType = original.Tag.GetType();
        var historyProperty = stateType.GetProperty("AddressHistory", InstanceMembers)!;
        var originalHistory = historyProperty.GetValue(original.Tag)!;
        var historyType = originalHistory.GetType();
        var record = historyType.GetMethod("Record", InstanceMembers)!;
        var paths = historyType.GetProperty("Paths", InstanceMembers)!;
        var currentPath = ReadBrowserPath(original);
        record.Invoke(originalHistory, [Path.GetDirectoryName(currentPath)!]);
        record.Invoke(originalHistory, [currentPath]);
        var expected = ((IEnumerable<string>)paths.GetValue(originalHistory)!).ToArray();
        Ensure(expected.Length > 1, "closed-tab fixture has more than its current address");

        paneType.GetMethod("CloseTab", InstanceMembers)!.Invoke(pane, [original.Tag]);
        Ensure(!tabs.Items.Contains(original), "ordinary tab closes before restoration");
        // The closed snapshot must not retain mutable history from its disposed
        // tab, nor share it with a later duplicate of the reopened tab.
        record.Invoke(originalHistory, ["shell:RecycleBinFolder"]);
        paneType.GetMethod("ReopenClosedTab", InstanceMembers)!.Invoke(pane, null);
        var reopened = (TabItem)tabs.SelectedItem!;
        var reopenedHistory = historyProperty.GetValue(reopened.Tag)!;
        Ensure(((IEnumerable<string>)paths.GetValue(reopenedHistory)!).SequenceEqual(expected),
            "reopening a closed tab preserves its complete address dropdown history");
        Ensure(!ReferenceEquals(originalHistory, reopenedHistory) && ReadBrowserPath(reopened) == currentPath,
            "restored history is independent and the closed directory is retained");
        paneType.GetMethod("DuplicateCurrentTab", InstanceMembers)!.Invoke(pane, null);
        var duplicate = (TabItem)tabs.SelectedItem!;
        var duplicateHistory = historyProperty.GetValue(duplicate.Tag)!;
        record.Invoke(duplicateHistory, ["shell:ThisPCFolder"]);
        Ensure(((IEnumerable<string>)paths.GetValue(reopenedHistory)!).SequenceEqual(expected),
            "editing a duplicate's history does not change the restored tab");
        foreach (var tab in new[] { reopened, duplicate })
        {
            var browser = stateType.GetProperty("Browser", InstanceMembers)!.GetValue(tab.Tag)!;
            Ensure(browser.GetType().GetField("_browser", InstanceMembers)!.GetValue(browser) is null &&
                (nint)browser.GetType().GetField("_hostWindow", InstanceMembers)!.GetValue(browser)! == 0,
                "closed-tab history checks never create a native Shell view");
        }
    }

    private static void CheckUnhostedDeferredFocus(UIElement pane)
    {
        var type = pane.GetType();
        var tabs = (TabControl)type.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var selected = tabs.SelectedItem;
        var other = tabs.Items.Cast<TabItem>().First(tab => !ReferenceEquals(tab, selected));
        var permission = type.GetProperty("MayRestoreFileFocus", InstanceMembers)!;
        var previous = permission.GetValue(pane);
        var reads = 0;
        permission.SetValue(pane, (Func<bool>)(() => { reads++; return true; }));
        var queue = type.GetMethod("RestoreFileFocusWhenActive", InstanceMembers)!;
        void Drain()
        {
            var frame = new DispatcherFrame();
            pane.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        try
        {
            queue.Invoke(pane, null);
            tabs.SelectedItem = other;
            Drain();
            Ensure(reads == 0, "a deferred focus request cannot authorize focus for a replacement tab");
            queue.Invoke(pane, null);
            Drain();
            Ensure(reads == 1, "the unchanged tab still reaches the current focus-intent gate");
            foreach (var tab in tabs.Items.Cast<TabItem>())
            {
                var browser = (HwndHost)tab.Tag.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(tab.Tag)!;
                Ensure(browser.Handle == 0, "deferred-focus checks create no HWND");
            }
        }
        finally
        {
            permission.SetValue(pane, previous);
            tabs.SelectedItem = selected;
        }
    }

    private static void CheckUnhostedTabLimit(UIElement source, UIElement destination)
    {
        var type = destination.GetType();
        var tabs = (TabControl)type.GetField("Tabs", InstanceMembers)!.GetValue(destination)!;
        var sourceTabs = (TabControl)type.GetField("Tabs", InstanceMembers)!.GetValue(source)!;
        var create = type.GetMethod("NewTab", InstanceMembers)!;
        while (tabs.Items.Count < 99) create.Invoke(destination, null);
        var snapshots = ((System.Collections.IEnumerable)type.GetMethod("SnapshotTabs", InstanceMembers)!
            .Invoke(source, null)!).Cast<object>().Take(2).ToArray();
        Ensure(snapshots.Length == 2, "import limit fixture has two source tabs");
        var incoming = Array.CreateInstance(snapshots[0].GetType(), 2);
        incoming.SetValue(snapshots[0], 0);
        incoming.SetValue(snapshots[1], 1);
        var beforeImport = tabs.SelectedItem;
        var rejected = false;
        try { type.GetMethod("ImportTabs", InstanceMembers)!.Invoke(destination, [incoming, 0, null]); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
        Ensure(rejected && tabs.Items.Count == 99 && ReferenceEquals(tabs.SelectedItem, beforeImport),
            "an oversized import batch is rejected before adding any tab or changing selection");
        var finalSlot = Array.CreateInstance(snapshots[0].GetType(), 1);
        finalSlot.SetValue(snapshots[0], 0);
        type.GetMethod("ImportTabs", InstanceMembers)!.Invoke(destination, [finalSlot, 0, null]);
        Ensure(tabs.Items.Count == 100, "an import can fill the final supported slot");
        var selected = tabs.SelectedItem;
        create.Invoke(destination, null);
        Ensure(tabs.Items.Count == 100 && ReferenceEquals(tabs.SelectedItem, selected),
            "NewTab at the persisted limit preserves count and selection");
        type.GetMethod("OpenPathInNewTab", InstanceMembers)!.Invoke(destination, [ReadBrowserPath(selected!)]);
        type.GetMethod("DuplicateCurrentTab", InstanceMembers)!.Invoke(destination, null);
        Ensure(tabs.Items.Count == 100, "open-path and duplicate cannot create unsavable tabs");
        var sourceTab = (TabItem)sourceTabs.Items[0];
        var sourceCount = sourceTabs.Items.Count;
        Ensure(!(bool)type.GetMethod("CopyTabFrom", InstanceMembers)!.Invoke(destination, [sourceTab.Tag, null, true])!,
            "cross-pane copy rejects a full destination");
        Ensure(!(bool)type.GetMethod("TransferTabTo", InstanceMembers)!.Invoke(source, [sourceTab.Tag, destination, null, true])! &&
            sourceTabs.Items.Count == sourceCount && tabs.Items.Count == 100 && sourceTabs.Items.Contains(sourceTab),
            "cross-pane move rejects before detaching the source tab");
        type.GetMethod("CloseCurrentTab", InstanceMembers)!.Invoke(destination, null);
        var closed = type.GetField("_closedTabs", InstanceMembers)!.GetValue(destination)!;
        var count = closed.GetType().GetProperty("Count")!;
        var closedBefore = (int)count.GetValue(closed)!;
        create.Invoke(destination, null);
        type.GetMethod("ReopenClosedTab", InstanceMembers)!.Invoke(destination, null);
        Ensure(tabs.Items.Count == 100 && (int)count.GetValue(closed)! == closedBefore,
            "reopen at capacity keeps the closed-tab snapshot for later");
        type.GetMethod("CloseCurrentTab", InstanceMembers)!.Invoke(destination, null);
        type.GetMethod("ReopenClosedTab", InstanceMembers)!.Invoke(destination, null);
        Ensure(tabs.Items.Count == 100 && (int)count.GetValue(closed)! == closedBefore,
            "after freeing a slot the most recently closed tab can be restored normally");
        foreach (var tab in tabs.Items.Cast<TabItem>())
        {
            var browser = (HwndHost)tab.Tag.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(tab.Tag)!;
            Ensure(browser.Handle == 0, "tab-limit checks create no HWND");
        }
    }

    private static void DisposeUnhostedBrowsers(UIElement? pane)
    {
        if (pane is null) return;
        var tabs = (TabControl)pane.GetType().GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        foreach (var tab in tabs.Items.Cast<TabItem>())
            ((IDisposable)tab.Tag.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(tab.Tag)!).Dispose();
    }

    private static string ReadBrowserPath(object tab)
    {
        var state = ((TabItem)tab).Tag!;
        var browser = state.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(state)!;
        return (string)browser.GetType().GetProperty("CurrentPath", InstanceMembers)!.GetValue(browser)!;
    }

    private static void PumpUntil(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        var deadline = DateTime.UtcNow + timeout;
        var succeeded = false;
        timer.Tick += (_, _) =>
        {
            succeeded = predicate();
            if (succeeded || DateTime.UtcNow >= deadline)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!succeeded) throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static void CheckUnhostedActivePaneGeometry(UIElement pane)
    {
        var type = pane.GetType();
        var activate = type.GetMethod("SetActivePaneVisual", InstanceMembers)!;
        var border = (Border)type.GetField("RootBorder", InstanceMembers)!.GetValue(pane)!;
        Size Arrange(bool active)
        {
            activate.Invoke(pane, [active]);
            pane.Measure(new Size(800, 600));
            pane.Arrange(new Rect(0, 0, 800, 600));
            pane.UpdateLayout();
            return border.Child.RenderSize;
        }
        var inactive = Arrange(false);
        var active = Arrange(true);
        Ensure(active == inactive, "activating a pane must not resize its navigation/file-view content");
        for (var i = 0; i < 10; i++)
            Ensure(Arrange(i % 2 == 0) == inactive, "repeated pane activation must keep the native host allocation stable");
        var expression = border.ReadLocalValue(Border.BorderBrushProperty);
        Arrange(false);
        Ensure(ReferenceEquals(expression, border.ReadLocalValue(Border.BorderBrushProperty)),
            "repeated inactive notifications preserve the existing resource reference");
        Console.WriteLine("PASS unhosted pane activation preserves content geometry and duplicate highlight references");
    }
    private static void EnsureForegroundUnchanged(IntPtr expected, string action) =>
        Ensure(GetForegroundWindow() == expected, $"foreground changed while {action}");

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
