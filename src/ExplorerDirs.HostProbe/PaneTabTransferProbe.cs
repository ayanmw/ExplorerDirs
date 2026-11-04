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
        File.WriteAllText(Path.Combine(sourceA, "probe-2.txt"), "second selected item");

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
                [sourceTabs, viewFactory, null, 0, favoriteStore, historyStore, followWindows, false, null, false, false, null], null)!;
            targetPane = (UIElement)Activator.CreateInstance(paneType, InstanceMembers, null,
                [targetTabs, viewFactory, null, 0, favoriteStore, historyStore, followWindows, false, null, false, false, null], null)!;

            if (unhostedCopyOrder)
            {
                CheckUnhostedActivePaneGeometry(sourcePane);
                CheckUnhostedTabChrome(sourcePane);
                CheckUnhostedTabChrome(targetPane);
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

            WaitForShellItemCount(browserType, browser, 2, "the returned A location finished enumerating");
            SelectShellItems(browserType, browser, sourceA, ["probe.txt", "probe-2.txt"]);
            IReadOnlyList<string>? selectedPaths = null;
            PumpUntil(() =>
            {
                try
                {
                    selectedPaths = (IReadOnlyList<string>)browserType
                        .GetMethod("CaptureSelectedShellParsingNames", InstanceMembers)!.Invoke(browser, null)!;
                    return selectedPaths.Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)
                        .SequenceEqual(["probe-2.txt", "probe.txt"], StringComparer.OrdinalIgnoreCase);
                }
                catch { return false; }
            }, TimeSpan.FromSeconds(5), "the source Shell selection became readable");
            Ensure(selectedPaths!.Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(["probe-2.txt", "probe.txt"], StringComparer.OrdinalIgnoreCase),
                "the source Shell view starts with both clone-selection fixtures selected");

            var copy = (bool)paneType.GetMethod("CopyTabFrom", InstanceMembers)!
                .Invoke(sourcePane, [state, null, true])!;
            Ensure(copy, "Ctrl-drag copy can create a tab in a pane whose only tab was moved away");
            var pendingCopy = (TabItem)sourceTabsControl.SelectedItem!;
            var pendingCopyState = pendingCopy.Tag!;
            var startupSelections = (IReadOnlyList<string>?)pendingCopyState.GetType()
                .GetProperty("StartupSelectItems", InstanceMembers)!.GetValue(pendingCopyState);
            Ensure(startupSelections is not null && startupSelections.Select(Path.GetFileName)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(["probe-2.txt", "probe.txt"], StringComparer.OrdinalIgnoreCase),
                "copy carries selected Shell parsing names into the cloned tab's startup restore request");
            PumpUntil(() => sourceTabsControl.Items.Count == 2 &&
                string.Equals(ReadBrowserPath(sourceTabsControl.SelectedItem!), sourceA, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the independent copied browser became ready");
            var copiedState = ((TabItem)sourceTabsControl.SelectedItem!).Tag!;
            var copiedBrowser = browserProperty.GetValue(copiedState)!;
            Ensure(!ReferenceEquals(copiedState, state) && !ReferenceEquals(copiedBrowser, browser),
                "copy creates an independent tab and Shell host");
            WaitForShellItemCount(browserType, copiedBrowser, 2, "the copied Shell view finished enumerating before selection");
            ApplyCopiedShellSelections(browserType, copiedBrowser, sourceA, ["probe.txt", "probe-2.txt"]);
            PumpUntil(() => TryReadSelectedNames(browserType, copiedBrowser, out var copiedSelectedPaths) &&
                copiedSelectedPaths.SequenceEqual(["probe-2.txt", "probe.txt"], StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5), "the copied Shell selection became readable after view readiness");
            var queueSelections = browserType.GetMethods(InstanceMembers).Single(method =>
                method.Name == "QueueFileSystemSelections" && method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(IReadOnlyList<string>));
            var survivingPath = Path.Combine(sourceA, "probe.txt");
            var vanishedPath = Path.Combine(sourceA, "deleted-before-restore.txt");
            var queued = (bool)queueSelections.Invoke(copiedBrowser, [new[] { survivingPath, vanishedPath }])!;
            Ensure(queued, "a vanished copied item does not cancel restoration of surviving selected files");
            var queuedPidls = (System.Collections.ICollection)browserType
                .GetField("_createdFileSelectionPidls", InstanceMembers)!.GetValue(copiedBrowser)!;
            Ensure(queuedPidls.Count == 1, "the queued restore omits the vanished path and keeps the surviving path");
            browserType.GetMethod("CancelCreatedFileSelection", InstanceMembers)!.Invoke(copiedBrowser, [null]);
            var noSurvivors = (bool)queueSelections.Invoke(copiedBrowser,
                [new[] { vanishedPath, Path.Combine(sourceA, "also-deleted.txt") }])!;
            Ensure(!noSurvivors && browserType.GetField("_createdFileSelectionPidls", InstanceMembers)!
                    .GetValue(copiedBrowser) is null,
                "an entirely vanished copied selection creates no empty restore request");

            SelectShellItems(browserType, browser, sourceA, ["probe-2.txt"]);
            var navigationHistory = stateType.GetProperty("NavigationHistory", InstanceMembers)!.GetValue(state)!;
            var navigationHistoryType = navigationHistory.GetType();
            var historyCurrentSelection = navigationHistoryType.GetProperty("CurrentSelection", InstanceMembers)!;
            PumpUntil(() => ReadSelectedNames(browserType, browser).SequenceEqual(["probe-2.txt"], StringComparer.OrdinalIgnoreCase) &&
                ((IReadOnlyList<string>)historyCurrentSelection.GetValue(navigationHistory)!).Select(Path.GetFileName)
                    .SequenceEqual(["probe-2.txt"], StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5), "the source history captured its current A-location selection");
            navigate.Invoke(browser, [sourceB]);
            PumpUntil(() => string.Equals(ReadBrowserPath(movedTab), sourceB, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)navigationHistoryType.GetProperty("Current", InstanceMembers)!.GetValue(navigationHistory)!,
                    sourceB, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the source history recorded its B location");
            WaitForShellItemCount(browserType, browser, 1, "the B location finished enumerating");
            SelectShellItems(browserType, browser, sourceB, ["probe.txt"]);
            PumpUntil(() => TryReadSelectedNames(browserType, browser, out var selected) &&
                selected.SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase) &&
                ((IReadOnlyList<string>)historyCurrentSelection.GetValue(navigationHistory)!).Select(Path.GetFileName)
                    .SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5), "the source history captured its B-location selection");

            paneType.GetMethod("CloneView", InstanceMembers)!.Invoke(targetPane, [state]);
            PumpUntil(() => targetTabsControl.Items.Count == 3 &&
                string.Equals(ReadBrowserPath(targetTabsControl.SelectedItem!), sourceB, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the independent cloned view opened at the current B location");
            var clonedTab = (TabItem)targetTabsControl.SelectedItem!;
            var clonedState = clonedTab.Tag!;
            var clonedBrowser = browserProperty.GetValue(clonedState)!;
            var clonedNavigationHistory = clonedState.GetType().GetProperty("NavigationHistory", InstanceMembers)!.GetValue(clonedState)!;
            var clonedHistoryType = clonedNavigationHistory.GetType();
            var clonedSelection = clonedHistoryType.GetProperty("CurrentSelection", InstanceMembers)!;
            WaitForShellItemCount(browserType, clonedBrowser, 1, "the cloned B location finished enumerating");
            Ensure(((IReadOnlyList<string>)clonedSelection.GetValue(clonedNavigationHistory)!).Select(Path.GetFileName)
                    .SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase),
                "the clone starts with the B-location selection snapshot");
            ApplyCopiedShellSelections(browserType, clonedBrowser, sourceB, ["probe.txt"]);
            Ensure(ReadSelectedNames(browserType, clonedBrowser).SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase),
                "the production Shell selection batch applies the cloned B-location snapshot");
            targetPane.GetType().GetMethod("GoBack", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(ReadBrowserPath(clonedTab), sourceA, StringComparison.OrdinalIgnoreCase) &&
                ReadHistoryReplayReady(clonedState) &&
                ((IReadOnlyList<string>)clonedSelection.GetValue(clonedNavigationHistory)!).Select(Path.GetFileName)
                    .SequenceEqual(["probe-2.txt"], StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the cloned Back selected the A-location history snapshot");
            WaitForShellItemCount(browserType, clonedBrowser, 2, "the cloned A location finished enumerating");
            ApplyCopiedShellSelections(browserType, clonedBrowser, sourceA, ["probe-2.txt"]);
            Ensure(ReadSelectedNames(browserType, clonedBrowser).SequenceEqual(["probe-2.txt"], StringComparer.OrdinalIgnoreCase),
                "the production Shell selection batch applies the cloned A-location snapshot");
            targetPane.GetType().GetMethod("GoForward", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(ReadBrowserPath(clonedTab), sourceB, StringComparison.OrdinalIgnoreCase) &&
                ReadHistoryReplayReady(clonedState) &&
                ((IReadOnlyList<string>)clonedSelection.GetValue(clonedNavigationHistory)!).Select(Path.GetFileName)
                    .SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the cloned Forward selected the B-location history snapshot");
            WaitForShellItemCount(browserType, clonedBrowser, 1, "the forwarded B location finished enumerating");
            ApplyCopiedShellSelections(browserType, clonedBrowser, sourceB, ["probe.txt"]);
            Ensure(ReadSelectedNames(browserType, clonedBrowser).SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase),
                "the production Shell selection batch reapplies the cloned B-location snapshot");
            Ensure((int)clonedHistoryType.GetProperty("Index", InstanceMembers)!.GetValue(clonedNavigationHistory)! ==
                (int)navigationHistoryType.GetProperty("Index", InstanceMembers)!.GetValue(navigationHistory)!,
                "the cloned navigation cursor advanced independently to the matching history stop");
            Ensure(string.Equals(ReadBrowserPath(movedTab), sourceB, StringComparison.OrdinalIgnoreCase) &&
                ((IReadOnlyList<string>)historyCurrentSelection.GetValue(navigationHistory)!).Select(Path.GetFileName)
                    .SequenceEqual(["probe.txt"], StringComparer.OrdinalIgnoreCase),
                "back/forward replay in the clone leaves the source tab location and history selection unchanged");
            targetPane.GetType().GetMethod("CloseTab", InstanceMembers)!.Invoke(targetPane, [clonedState]);
            Ensure(targetTabsControl.Items.Count == 2, "the cloned view can be closed independently");

            browserType.GetMethod("Search", InstanceMembers)!.Invoke(browser, ["probe", sourceB]);
            PumpUntil(() => browserType.GetProperty("CurrentSearchUri", InstanceMembers)!.GetValue(browser) is string,
                TimeSpan.FromSeconds(15), "the source tab entered a scoped Shell search");
            WaitForShellItemCountAtLeast(browserType, browser, 1, "the source Shell search populated");
            SelectShellViewIndex(browserType, browser, 0);
            var searchSelection = ReadSelectedShellNames(browserType, browser);
            Ensure(searchSelection.Length == 1, "a concrete Shell search result was selected before cloning");
            var searchHistorySelection = navigationHistoryType.GetProperty("CurrentSelection", InstanceMembers)!;
            // Direct SelectItem does not promise a user selection callback; clone must snapshot the live view.
            var sourceSearchUri = (string)browserType.GetProperty("CurrentSearchUri", InstanceMembers)!.GetValue(browser)!;

            paneType.GetMethod("CloneView", InstanceMembers)!.Invoke(targetPane, [state]);
            Ensure(((IReadOnlyList<string>)searchHistorySelection.GetValue(navigationHistory)!)
                    .SequenceEqual(searchSelection, StringComparer.OrdinalIgnoreCase),
                "cloning captures the live Shell search selection even if the source history callback is pending");
            PumpUntil(() => targetTabsControl.Items.Count == 3 &&
                string.Equals(browserType.GetProperty("CurrentSearchUri", InstanceMembers)!.GetValue(
                    browserProperty.GetValue(((TabItem)targetTabsControl.SelectedItem!).Tag!)) as string,
                    sourceSearchUri, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the cloned view reopened the same scoped Shell search");
            var searchCloneTab = (TabItem)targetTabsControl.SelectedItem!;
            var searchCloneState = searchCloneTab.Tag!;
            var searchCloneBrowser = browserProperty.GetValue(searchCloneState)!;
            var searchCloneHistory = searchCloneState.GetType().GetProperty("NavigationHistory", InstanceMembers)!.GetValue(searchCloneState)!;
            var searchCloneSelection = searchCloneHistory.GetType().GetProperty("CurrentSelection", InstanceMembers)!;
            Ensure(((IReadOnlyList<string>)searchCloneSelection.GetValue(searchCloneHistory)!)
                    .SequenceEqual(searchSelection, StringComparer.OrdinalIgnoreCase),
                "the cloned search history retained the selected Shell parsing name");
            Ensure(((IReadOnlyList<string>)searchCloneState.GetType().GetProperty("StartupSelectItems", InstanceMembers)!
                    .GetValue(searchCloneState)!).SequenceEqual(searchSelection, StringComparer.OrdinalIgnoreCase),
                "the cloned search queued the saved Shell selection for asynchronous restoration");
            WaitForShellItemCountAtLeast(browserType, searchCloneBrowser, 1, "the cloned Shell search populated");
            SelectShellViewIndex(browserType, searchCloneBrowser, 0);
            Ensure(ReadSelectedShellNames(browserType, searchCloneBrowser)
                    .SequenceEqual(searchSelection, StringComparer.OrdinalIgnoreCase),
                "the cloned Shell search resolves the saved result to the same native view item");
            targetPane.GetType().GetMethod("GoBack", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(ReadBrowserPath(searchCloneTab), sourceB, StringComparison.OrdinalIgnoreCase) &&
                ReadHistoryReplayReady(searchCloneState), TimeSpan.FromSeconds(15),
                "the cloned search history navigated Back to its filesystem scope");
            targetPane.GetType().GetMethod("GoForward", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(browserType.GetProperty("CurrentSearchUri", InstanceMembers)!
                    .GetValue(searchCloneBrowser) as string, sourceSearchUri, StringComparison.OrdinalIgnoreCase) &&
                ReadHistoryReplayReady(searchCloneState) &&
                ((IReadOnlyList<string>)searchCloneSelection.GetValue(searchCloneHistory)!)
                    .SequenceEqual(searchSelection, StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the cloned search history replayed Forward with its selection snapshot");
            targetPane.GetType().GetMethod("CloseTab", InstanceMembers)!.Invoke(targetPane, [searchCloneState]);
            Ensure(targetTabsControl.Items.Count == 2, "the cloned search view closes independently");
            navigate.Invoke(browser, [sourceB]);
            PumpUntil(() => string.Equals(ReadBrowserPath(movedTab), sourceB, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the source returned to its filesystem path after the search clone check");

            var thisPc = "shell:MyComputerFolder";
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;
            navigate.Invoke(browser, [thisPc]);
            PumpUntil(() => string.Equals(ReadBrowserPath(movedTab), thisPc, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)navigationHistoryType.GetProperty("Current", InstanceMembers)!.GetValue(navigationHistory)!,
                    thisPc, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the source tab navigated to This PC");
            WaitForShellItemCountAtLeast(browserType, browser, 1, "This PC populated its Shell view");
            SelectShellParsingName(browserType, browser, thisPc, systemDrive);
            PumpUntil(() => ReadSelectedShellNames(browserType, browser).Any(name => IsSameDriveName(name, systemDrive)) &&
                ((IReadOnlyList<string>)historyCurrentSelection.GetValue(navigationHistory)!).Any(name => IsSameDriveName(name, systemDrive)),
                TimeSpan.FromSeconds(5), "the source history captured the selected This PC drive");
            paneType.GetMethod("CloneView", InstanceMembers)!.Invoke(targetPane, [state]);
            PumpUntil(() => targetTabsControl.Items.Count == 3 &&
                string.Equals(ReadBrowserPath(targetTabsControl.SelectedItem!), thisPc, StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15), "the virtual Shell location cloned at This PC");
            var virtualCloneTab = (TabItem)targetTabsControl.SelectedItem!;
            var virtualCloneState = virtualCloneTab.Tag!;
            var virtualCloneBrowser = browserProperty.GetValue(virtualCloneState)!;
            var virtualCloneHistory = virtualCloneState.GetType().GetProperty("NavigationHistory", InstanceMembers)!.GetValue(virtualCloneState)!;
            var virtualCloneSelection = virtualCloneHistory.GetType().GetProperty("CurrentSelection", InstanceMembers)!;
            WaitForShellItemCountAtLeast(browserType, virtualCloneBrowser, 1, "the cloned This PC view populated");
            Ensure(((IReadOnlyList<string>)virtualCloneSelection.GetValue(virtualCloneHistory)!)
                    .Any(name => IsSameDriveName(name, systemDrive)),
                "the cloned virtual Shell history retains the selected drive identity");
            SelectShellParsingName(browserType, virtualCloneBrowser, thisPc, systemDrive);
            Ensure(ReadSelectedShellNames(browserType, virtualCloneBrowser).Any(name => IsSameDriveName(name, systemDrive)),
                "the production direct-child PIDL route selects the drive in the cloned This PC view");
            targetPane.GetType().GetMethod("GoBack", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(ReadBrowserPath(virtualCloneTab), sourceB, StringComparison.OrdinalIgnoreCase) &&
                ReadHistoryReplayReady(virtualCloneState),
                TimeSpan.FromSeconds(15), "the virtual clone returned to its preceding filesystem location");
            targetPane.GetType().GetMethod("GoForward", InstanceMembers)!.Invoke(targetPane, null);
            PumpUntil(() => string.Equals(ReadBrowserPath(virtualCloneTab), thisPc, StringComparison.OrdinalIgnoreCase) &&
                ReadHistoryReplayReady(virtualCloneState) &&
                ((IReadOnlyList<string>)virtualCloneSelection.GetValue(virtualCloneHistory)!)
                    .Any(name => IsSameDriveName(name, systemDrive)),
                TimeSpan.FromSeconds(15), "the virtual clone returned to This PC with its saved drive selection");
            WaitForShellItemCountAtLeast(browserType, virtualCloneBrowser, 1, "the forwarded This PC view populated");
            SelectShellParsingName(browserType, virtualCloneBrowser, thisPc, systemDrive);
            Ensure(ReadSelectedShellNames(browserType, virtualCloneBrowser).Any(name => IsSameDriveName(name, systemDrive)),
                "the production direct-child PIDL route reapplies the drive selection after virtual-location Forward");
            targetPane.GetType().GetMethod("CloseTab", InstanceMembers)!.Invoke(targetPane, [virtualCloneState]);
            Ensure(targetTabsControl.Items.Count == 2, "the virtual Shell clone closes independently");

            paneType.GetMethod("CloseTab", InstanceMembers)!.Invoke(targetPane, [state]);
            Ensure(targetTabsControl.Items.Count == 1 && !targetTabsControl.Items.Contains(movedTab),
                "the transferred tab can be closed normally");
            Ensure(!IsWindow(shellHostWindow) && browserType.GetField("_browser", InstanceMembers)!.GetValue(browser) is null,
                "closing the transferred tab destroys the original HWND and releases the browser");

            EnsureForegroundUnchanged(foregroundBefore, "moving and navigating a tab");
            Console.WriteLine("Pane tab transfer and clone host probe: PASS (move retains TabState/HWND/history; copy is independent; filesystem, This PC, and search-folder clone history/selection identity pass; close releases hosts)");
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
        var startupProperty = original.Tag.GetType().GetProperty("StartupSelectItems", InstanceMembers)!;
        var startupSelection = new[] { Path.Combine(ReadBrowserPath(original), "probe.txt") };
        startupProperty.SetValue(original.Tag, startupSelection);
        paneType.GetMethod("TogglePinned", InstanceMembers)!.Invoke(source, [original.Tag]);
        var copyMethod = paneType.GetMethod("CopyTabFrom", InstanceMembers)!;

        TabItem Copy(TabItem? target, bool after)
        {
            Ensure((bool)copyMethod.Invoke(destination, [original.Tag, target, after])!, "copy accepted");
            return (TabItem)targetTabs.SelectedItem!;
        }

        var first = Copy(null, true);
        var copiedSelection = (IReadOnlyList<string>)startupProperty.GetValue(first.Tag)!;
        Ensure(copiedSelection.SequenceEqual(startupSelection) && !ReferenceEquals(copiedSelection, startupSelection),
            "an uncreated tab copies its queued selection without sharing mutable storage or creating Shell");
        startupProperty.SetValue(original.Tag, null);
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

    private static void CheckUnhostedTabChrome(UIElement pane)
    {
        var type = pane.GetType();
        var tabs = (TabControl)type.GetField("Tabs", InstanceMembers)!.GetValue(pane)!;
        var edit = (Button)type.GetField("GoButton", InstanceMembers)!.GetValue(pane)!;
        Ensure(type.GetField("NewTabButton", InstanceMembers) is null &&
            type.GetField("CloseTabButton", InstanceMembers) is null,
            "tab strip does not retain redundant New/Close buttons");
        var row = (FrameworkElement)type.GetField("TabStripRow", InstanceMembers)!.GetValue(pane)!;
        Ensure(edit.Visibility == Visibility.Collapsed && !edit.IsTabStop,
            "removed address editor has no visible button or keyboard stop");
        Ensure(row.IsAncestorOf(tabs), "tab header row contains the strip without a separate actions panel");
        var setMode = type.GetMethod("SetTabStripVisibilityMode", InstanceMembers)!;
        var modeType = setMode.GetParameters()[0].ParameterType;
        try
        {
            setMode.Invoke(pane, [Enum.Parse(modeType, "Multiple")]);
            Ensure(row.Visibility == (tabs.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed),
                "multiple-only mode hides the complete row for a single tab");
            setMode.Invoke(pane, [Enum.Parse(modeType, "Hidden")]);
            Ensure(tabs.Visibility == Visibility.Collapsed && row.Visibility == Visibility.Collapsed,
                "hiding the tab strip hides its action row rather than leaving an empty toolbar");
            var map = type.GetProperty("FocusMap", InstanceMembers)!.GetValue(pane)!;
            Ensure((int)map.GetType().GetProperty("NewTab", InstanceMembers)!.GetValue(map)! == -1 &&
                (int)map.GetType().GetProperty("CloseTab", InstanceMembers)!.GetValue(map)! == -1,
                "hidden tab actions do not retain keyboard focus stops");
        }
        finally { setMode.Invoke(pane, [Enum.Parse(modeType, "Always")]); }
        CheckUnhostedTabOverflow(pane, tabs);
    }

    private static void CheckUnhostedTabOverflow(UIElement pane, TabControl tabs)
    {
        var originals = tabs.Items.OfType<TabItem>().Select(tab => (Tab: tab, Width: tab.ReadLocalValue(FrameworkElement.WidthProperty))).ToArray();
        foreach (var item in originals) item.Tab.Width = 160;
        tabs.ApplyTemplate();
        var left = (Button)tabs.Template.FindName("ScrollTabsLeft", tabs);
        var right = (Button)tabs.Template.FindName("ScrollTabsRight", tabs);
        var scroll = (ScrollViewer)tabs.Template.FindName("TabHeaderScroll", tabs);
        var setMultiline = pane.GetType().GetMethod("SetMultilineTabHeaders", InstanceMembers)!;
        void Layout(double width)
        {
            for (var iteration = 0; iteration < 4; iteration++)
            {
                pane.InvalidateMeasure(); pane.Measure(new Size(width, 600)); pane.Arrange(new Rect(0, 0, width, 600));
                pane.UpdateLayout();
                pane.GetType().GetMethod("UpdateTabScrollButtons", InstanceMembers)!.Invoke(pane, null);
            }
        }
        try
        {
            Layout(800);
            Ensure(left.Visibility == Visibility.Collapsed && right.Visibility == Visibility.Collapsed,
                "tab arrows remain hidden when headers fit");
            Layout(160);
            Ensure(left.Visibility == Visibility.Visible && right.Visibility == Visibility.Visible && left.Width >= 28 && right.Width >= 28,
                $"overflow exposes both comfortably sized tab-scroll targets: left={left.Visibility}/{left.ActualWidth}; right={right.Visibility}/{right.ActualWidth}; extent={scroll.ExtentWidth}; viewport={scroll.ViewportWidth}; tabs={tabs.ActualWidth}; headers={string.Join(',', originals.Select(item => item.Tab.DesiredSize.Width))}");
            var scrollOrigin = scroll.TranslatePoint(new Point(0, 0), tabs);
            var leftOrigin = left.TranslatePoint(new Point(0, 0), tabs);
            var rightOrigin = right.TranslatePoint(new Point(0, 0), tabs);
            Ensure(leftOrigin.X >= scrollOrigin.X + scroll.ActualWidth - 0.5 &&
                rightOrigin.X >= leftOrigin.X + left.ActualWidth - 0.5,
                "both tab-scroll buttons sit together after the header viewport at the far right");
            Ensure(tabs.Items.OfType<TabItem>().All(tab => tab.BorderThickness == new Thickness(0, 3, 0, 0)),
                "selected tab accent reserves a three-pixel top edge without selection-dependent resizing");
            scroll.ScrollToHorizontalOffset(0); Layout(160);
            Ensure(!left.IsEnabled && right.IsEnabled, "tab-scroll controls reflect the start boundary");
            scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); Layout(160);
            Ensure(left.IsEnabled && !right.IsEnabled, "tab-scroll controls reflect the end boundary");
            setMultiline.Invoke(pane, [true]); Layout(160);
            Ensure(left.Visibility == Visibility.Collapsed && right.Visibility == Visibility.Collapsed,
                "multiline tab headers do not retain horizontal scroll arrows");
            setMultiline.Invoke(pane, [false]); Layout(160);
            Ensure(left.Visibility == Visibility.Visible && right.Visibility == Visibility.Visible,
                "returning to one row restores overflow controls");
            Layout(800);
            Ensure(left.Visibility == Visibility.Collapsed && right.Visibility == Visibility.Collapsed,
                "restoring width hides arrows without preserving their own overflow");
        }
        finally
        {
            setMultiline.Invoke(pane, [false]);
            foreach (var item in originals)
                if (item.Width == DependencyProperty.UnsetValue) item.Tab.ClearValue(FrameworkElement.WidthProperty);
                else item.Tab.SetValue(FrameworkElement.WidthProperty, item.Width);
            Layout(800);
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
        // The address editor keeps a directory separator; compare directory identity, not editor formatting.
        Ensure(Path.TrimEndingDirectorySeparator(address.Text) == Path.TrimEndingDirectorySeparator(oldPath),
            $"pane presentation still has the earlier directory before notification: address={address.Text}; old={oldPath}; next={nextPath}");
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
        var sourceBrowser = stateType.GetProperty("Browser", InstanceMembers)!.GetValue(state)!;
        var savedView = sourceBrowser.GetType().GetField("_viewState", InstanceMembers)!.GetValue(sourceBrowser)!;
        savedView.GetType().GetProperty("MixedSizeSort", InstanceMembers)!.SetValue(savedView, true);
        void CheckMixedPolicy(object tab, bool expected, string operation)
        {
            var tabState = ((TabItem)tab).Tag!;
            var tabBrowser = tabState.GetType().GetProperty("Browser", InstanceMembers)!.GetValue(tabState)!;
            var tabView = tabBrowser.GetType().GetMethod("CachedViewState", InstanceMembers)!.Invoke(tabBrowser, null)!;
            Ensure((bool)tabView.GetType().GetProperty("MixedSizeSort", InstanceMembers)!.GetValue(tabView)! == expected,
                operation + " preserves the expected per-tab mixed sort policy");
        }
        paneType.GetField("_newTabUsesCurrentFolder", InstanceMembers)!.SetValue(source, true);
        paneType.GetMethod("NewTab", InstanceMembers)!.Invoke(source, null);
        Ensure(ReadBrowserPath(tabs.SelectedItem!) == nextPath,
            "new-tab current-folder mode follows the settled directory");
        CheckMixedPolicy(tabs.SelectedItem!, false, "new tab");
        tabs.SelectedItem = original;
        paneType.GetMethod("FocusAddress", InstanceMembers)!.Invoke(source, null);
        Ensure(Path.TrimEndingDirectorySeparator(address.Text) == Path.TrimEndingDirectorySeparator(nextPath),
            "address editing starts from the settled directory");
        paneType.GetMethod("DuplicateCurrentTab", InstanceMembers)!.Invoke(source, null);
        Ensure(ReadBrowserPath(tabs.SelectedItem!) == nextPath,
            "duplicating before notice delivery opens the settled directory");
        CheckMixedPolicy(tabs.SelectedItem!, true, "duplicate");
        Ensure((bool)paneType.GetMethod("CopyTabFrom", InstanceMembers)!.Invoke(destination, [state, null, true])!,
            "cross-pane copy accepts the settled tab");
        var targetTabs = (TabControl)paneType.GetField("Tabs", InstanceMembers)!.GetValue(destination)!;
        Ensure(ReadBrowserPath(targetTabs.SelectedItem!) == nextPath,
            "cross-pane copy also opens the settled directory");
        CheckMixedPolicy(targetTabs.SelectedItem!, true, "cross-pane copy");
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
        var initial = Activator.CreateInstance(filterType, ["*.txt", false, false, null])!;
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
        var reopenedBrowser = stateType.GetProperty("Browser", InstanceMembers)!.GetValue(reopened.Tag)!;
        reopenedBrowser.GetType().GetField("_viewRestoreFailed", InstanceMembers)!.SetValue(reopenedBrowser, true);
        var beforeDuplicate = tabs.Items.Count;
        paneType.GetMethod("DuplicateCurrentTab", InstanceMembers)!.Invoke(pane, null);
        Ensure(tabs.Items.Count == beforeDuplicate + 1,
            "ordinary duplication remains available using confirmed settings after a native restore failure");
        reopenedBrowser.GetType().GetField("_viewRestoreFailed", InstanceMembers)!.SetValue(reopenedBrowser, false);
        reopenedBrowser.GetType().GetProperty("ViewCaptureFailed", InstanceMembers)!.SetValue(reopenedBrowser, false);
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

    private static bool ReadHistoryReplayReady(object state) =>
        (bool)state.GetType().GetProperty("HistoryReplayReady", InstanceMembers)!.GetValue(state)!;

    private static string[] ReadSelectedNames(Type hostType, object browser)
    {
        var paths = (IReadOnlyList<string>)hostType.GetMethod("CaptureSelectedFileSystemPaths", InstanceMembers)!
            .Invoke(browser, null)!;
        return paths.Select(path => Path.GetFileName(path) ?? path).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] ReadSelectedShellNames(Type hostType, object browser) =>
        ((IReadOnlyList<string>)hostType.GetMethod("CaptureSelectedShellParsingNames", InstanceMembers)!
            .Invoke(browser, null)!).ToArray();

    private static void SelectShellViewIndex(Type hostType, object browser, int index)
    {
        var shellBrowser = hostType.GetField("_browser", InstanceMembers)!.GetValue(browser)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var shellViewArgs = new object?[] { shellViewId, null };
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!
            .Invoke(shellBrowser, shellViewArgs)!);
        var shellView = shellViewArgs[1]!;
        var folderViewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        var folderViewArgs = new object?[] { folderViewId, null };
        object? childPidl = null;
        object? folderView = null;
        try
        {
            Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!
                .Invoke(shellBrowser, folderViewArgs)!);
            folderView = folderViewArgs[1]!;
            var itemArgs = new object?[] { index, IntPtr.Zero };
            Marshal.ThrowExceptionForHR((int)hostType.GetNestedType("IFolderView", BindingFlags.NonPublic)!
                .GetMethod("Item")!.Invoke(folderView, itemArgs)!);
            childPidl = itemArgs[1];
            Ensure(childPidl is IntPtr pointer && pointer != IntPtr.Zero,
                "the Shell search result row exposed its current child PIDL");
            Marshal.ThrowExceptionForHR((int)hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!
                .GetMethod("SelectItem")!.Invoke(shellView, [childPidl, 0x15u])!);
        }
        finally
        {
            if (childPidl is IntPtr pointer && pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            if (folderView is not null && Marshal.IsComObject(folderView)) Marshal.ReleaseComObject(folderView);
            if (Marshal.IsComObject(shellView)) Marshal.ReleaseComObject(shellView);
        }
    }

    private static bool IsSameDriveName(string parsingName, string drivePath)
    {
        var drive = drivePath.TrimEnd('\\');
        return parsingName.TrimEnd('\\').EndsWith(drive, StringComparison.OrdinalIgnoreCase);
    }

    private static void SelectShellParsingName(Type hostType, object browser, string parentIdentity,
        string itemParsingName)
    {
        var app = Assembly.Load("ExplorerDirs.App");
        var resolver = app.GetType("ExplorerDirs.App.ShellSelectionPidl", throwOnError: true)!;
        var resolverArgs = new object?[] { parentIdentity, itemParsingName, IntPtr.Zero };
        Ensure((bool)resolver.GetMethod("TryGetDirectChild", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, resolverArgs)!, "Shell selected item resolves to a direct child PIDL");
        var childPidl = (IntPtr)resolverArgs[2]!;
        Ensure(childPidl != IntPtr.Zero, "Shell direct-child resolver returned an owned PIDL");
        var shellBrowser = hostType.GetField("_browser", InstanceMembers)!.GetValue(browser)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!
            .Invoke(shellBrowser, viewObject)!);
        var shellView = viewObject[1]!;
        try
        {
            var selectItem = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!.GetMethod("SelectItem")!;
            Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [childPidl, 0x15u])!);
        }
        finally
        {
            Marshal.FreeCoTaskMem(childPidl);
            if (Marshal.IsComObject(shellView)) Marshal.ReleaseComObject(shellView);
        }
    }

    private static bool TryReadSelectedNames(Type hostType, object browser, out string[] names)
    {
        try
        {
            names = ReadSelectedNames(hostType, browser);
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            names = [];
            return false;
        }
    }

    private static void WaitForShellItemCount(Type hostType, object browser, int expectedCount, string description,
        bool atLeast = false)
    {
        var browserObject = hostType.GetField("_browser", InstanceMembers)!.GetValue(browser)!;
        var viewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        var viewObject = new object?[] { viewId, null };
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        PumpUntil(() =>
        {
            try
            {
                viewObject[1] = null;
                var hr = (int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browserObject, viewObject)!;
                if (hr < 0 || viewObject[1] is null) return false;
                var folderViewType = hostType.GetNestedType("IFolderView", BindingFlags.NonPublic)!;
                var folderView = viewObject[1]!;
                try
                {
                    var countArgs = new object?[] { 2u, 0 };
                    var countHr = (int)folderViewType.GetMethod("ItemCount")!.Invoke(folderView, countArgs)!;
                    var count = (int)countArgs[1]!;
                    return countHr >= 0 && (atLeast ? count >= expectedCount : count == expectedCount);
                }
                finally { if (Marshal.IsComObject(folderView)) Marshal.ReleaseComObject(folderView); }
            }
            catch (TargetInvocationException ex) when (ex.InnerException is COMException) { return false; }
        }, TimeSpan.FromSeconds(10), description);
    }

    private static void WaitForShellItemCountAtLeast(Type hostType, object browser, int expectedMinimum,
        string description) => WaitForShellItemCount(hostType, browser, expectedMinimum, description, atLeast: true);

    private static void SelectShellItems(Type hostType, object browser, string folder, IReadOnlyList<string> names)
    {
        var shellBrowser = hostType.GetField("_browser", InstanceMembers)!.GetValue(browser)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(shellBrowser, viewObject)!);
        var shellView = viewObject[1]!;
        var selectItem = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!.GetMethod("SelectItem")!;
        try
        {
            for (var index = 0; index < names.Count; index++)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder, names[index]), 0,
                    out var absolutePidl, 0, out _));
                try
                {
                    var flags = index == 0 ? 0x15u : 0x11u;
                    Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView,
                        [ILFindLastID(absolutePidl), flags])!);
                }
                finally { Marshal.FreeCoTaskMem(absolutePidl); }
            }
        }
        finally { Marshal.ReleaseComObject(shellView); }
    }

    private static void ApplyCopiedShellSelections(Type hostType, object browser, string folder,
        IReadOnlyList<string> names)
    {
        var shellBrowser = hostType.GetField("_browser", InstanceMembers)!.GetValue(browser)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(shellBrowser, viewObject)!);
        var shellView = viewObject[1]!;
        var absolutePidls = new List<nint>(names.Count);
        try
        {
            foreach (var name in names)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder, name), 0,
                    out var absolutePidl, 0, out _));
                absolutePidls.Add(absolutePidl);
            }
            // Route through the production batch helper so this checks the same native flags and
            // per-item continuation logic without activating the hidden test window.
            var helper = hostType.GetMethod("ApplyFileSystemSelectionBatch", BindingFlags.Static | BindingFlags.NonPublic)!;
            var result = helper.Invoke(null, [shellView, absolutePidls, (Func<bool>)(() => true)])!;
            Ensure((bool)result.GetType().GetField("Item1")!.GetValue(result)!,
                "production Shell selection batch applies every copied item");
        }
        finally
        {
            foreach (var pidl in absolutePidls) Marshal.FreeCoTaskMem(pidl);
            Marshal.ReleaseComObject(shellView);
        }
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
    private static void EnsureForegroundUnchanged(IntPtr expected, string action)
    {
        var actual = GetForegroundWindow();
        if (actual == expected) return;
        GetWindowThreadProcessId(expected, out var expectedProcessId);
        GetWindowThreadProcessId(actual, out var actualProcessId);
        throw new InvalidOperationException($"foreground changed while {action}: expected=0x{expected:X} pid={expectedProcessId}, actual=0x{actual:X} pid={actualProcessId}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
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
