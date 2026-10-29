using System.Diagnostics;
using System.IO;
using LinqExpression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

// Exercises PaneControl's real new/duplicate tab entry points in a never-shown fixture window.
internal static class QuickFilterTabStateProbe
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run()
    {
        var fixtureParent = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var fixtureRoot = Path.Combine(fixtureParent, "quick-filter-tabs-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(fixtureRoot, "pane-0");
        var siblingDirectory = Path.Combine(fixtureRoot, "sibling");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(siblingDirectory);
        File.WriteAllText(Path.Combine(directory, "initial.txt"), "initial");
        var hiddenPath = Path.Combine(directory, "hidden.txt");
        var superHiddenPath = Path.Combine(directory, "super-hidden.txt");
        File.WriteAllText(hiddenPath, "hidden");
        File.SetAttributes(hiddenPath, FileAttributes.Hidden);
        File.WriteAllText(superHiddenPath, "super-hidden");
        File.SetAttributes(superHiddenPath, FileAttributes.Hidden | FileAttributes.System);
        Directory.CreateDirectory(Path.Combine(directory, "child"));
        var favoritesPath = Path.Combine(fixtureRoot, "QuickFilterFavorites.ini");
        var foregroundBefore = GetForegroundWindow();
        HwndSource? source = null;
        object? pane = null;
        var disposedBrowsers = new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            var app = Assembly.Load("ExplorerDirs.App");
            var viewStateType = app.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var quickFilterType = app.GetType("ExplorerDirs.App.QuickFilterSettings", throwOnError: true)!;
            var columnFilterType = app.GetType("ExplorerDirs.App.ColumnValueFilter", throwOnError: true)!;
            var tabSettingsType = app.GetType("ExplorerDirs.App.TabSettings", throwOnError: true)!;
            var favoritesStoreType = app.GetType("ExplorerDirs.App.QuickFilterFavoritesStore", throwOnError: true)!;
            var historyStoreType = app.GetType("ExplorerDirs.App.QuickFilterHistoryStore", throwOnError: true)!;
            var paneType = app.GetType("ExplorerDirs.App.PaneControl", throwOnError: true)!;
            var defaultView = viewStateType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var viewFactoryType = typeof(Func<>).MakeGenericType(viewStateType);
            var viewFactory = LinqExpression.Lambda(viewFactoryType,
                LinqExpression.Call(defaultView, LinqExpression.Constant(4u), LinqExpression.Constant(-1))).Compile();
            var propertyKeyType = app.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var sortColumnType = app.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!;
            var displayColumnType = app.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var propertyFormat = Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var nameKey = Activator.CreateInstance(propertyKeyType, [propertyFormat, 10u])!;
            var typeKey = Activator.CreateInstance(propertyKeyType, [propertyFormat, 4u])!;
            var sizeKey = Activator.CreateInstance(propertyKeyType, [propertyFormat, 12u])!;
            var sortColumns = Array.CreateInstance(sortColumnType, 1);
            sortColumns.SetValue(Activator.CreateInstance(sortColumnType, [nameKey, 1]), 0);
            var displayColumns = Array.CreateInstance(displayColumnType, 3);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [nameKey, 271]), 0);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [typeKey, 193]), 1);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [sizeKey, 137]), 2);
            var initialView = Activator.CreateInstance(viewStateType, InstanceMembers, null,
                new object?[] { 4u, 16, sortColumns, typeKey, true, displayColumns }, null)!;
            var expectedView = (object)initialView;
            var columnFilterConstructor = columnFilterType.GetConstructors(InstanceMembers)
                .Single(constructor => constructor.GetParameters().Length == 2);
            var columnFilterArray = Array.CreateInstance(columnFilterType, 1);
            columnFilterArray.SetValue(columnFilterConstructor.Invoke([nameKey, new[] { "initial.txt" }]), 0);
            var filter = Activator.CreateInstance(quickFilterType,
                new object?[] { string.Empty, false, true, columnFilterArray })!;
            var emptyFilter = quickFilterType.GetProperty("Empty", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null);
            // Reflection does not apply optional constructor defaults; pass pane visibility flags explicitly.
            var initialTab = Activator.CreateInstance(tabSettingsType,
                new object?[] { directory, initialView, filter, false, false })!;
            var tabArray = SaveAndReloadTabSettings(app, tabSettingsType, initialTab, fixtureRoot);
            var favoritesStore = Activator.CreateInstance(favoritesStoreType, InstanceMembers, null,
                [favoritesPath], null)!;
            var historyStore = Activator.CreateInstance(historyStoreType, InstanceMembers, null,
                [Path.Combine(fixtureRoot, "QuickFilterHistory.ini")], null)!;
            var hiddenItemsPreferenceType = app.GetType("ExplorerDirs.App.HiddenItemsPreference", throwOnError: true)!;
            var followWindows = Enum.Parse(hiddenItemsPreferenceType, "FollowWindows");
            var hideHidden = Enum.Parse(hiddenItemsPreferenceType, "Hide");
            var foregroundBeforePane = GetForegroundWindow();
            pane = Activator.CreateInstance(paneType, InstanceMembers, null,
                new object?[] { tabArray, viewFactory, null, 0, favoritesStore, historyStore, hideHidden, false, null, false }, null)!;
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforePane, GetForegroundWindow(),
                "creating the quick-filter PaneControl ExplorerBrowserHost");

            var foregroundBeforeSource = GetForegroundWindow();
            source = new HwndSource(new HwndSourceParameters("Explorer Dirs hidden quick-filter tab probe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 900, Height = 500
            });
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeSource, GetForegroundWindow(),
                "creating the quick-filter probe HwndSource");
            var foregroundBeforeAttach = GetForegroundWindow();
            source.RootVisual = (UIElement)pane;
            ((UIElement)pane).Measure(new Size(900, 500));
            ((UIElement)pane).Arrange(new Rect(0, 0, 900, 500));
            ((UIElement)pane).UpdateLayout();
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeAttach, GetForegroundWindow(),
                "attaching the quick-filter PaneControl");
            if (IsWindowVisible(source.Handle)) throw new Exception("Quick-filter probe parent became visible during attachment.");

            var foregroundBeforeInitialReady = GetForegroundWindow();
            WaitFor(() => GetStates(pane).Count == 1 && IsBrowserReady(GetStates(pane)[0].Browser),
                TimeSpan.FromSeconds(10), "Initial filtered Shell tab did not become ready.");
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeInitialReady, GetForegroundWindow(),
                "waiting for the initial quick-filter Shell tab");
            var countShellItems = typeof(Program).GetMethod("Count", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            int ShellItemCount(object browser) => (int)countShellItems.Invoke(null, [browser])!;
            void WaitForStableItemCount(object browser, int expected, string description)
            {
                var stable = 0;
                var last = -1;
                try
                {
                    WaitFor(() =>
                    {
                        last = ShellItemCount(browser);
                        stable = last == expected ? stable + 1 : 0;
                        return stable >= 3;
                    }, TimeSpan.FromSeconds(10), description);
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException($"{description}; last-count={last}.", ex);
                }
            }
            var stableItemReads = 0;
            var lastItemCount = -1;
            try
            {
                WaitFor(() =>
                {
                    var browser = GetStates(pane)[0].Browser;
                    lastItemCount = ShellItemCount(browser);
                    stableItemReads = lastItemCount == 1 ? stableItemReads + 1 : 0;
                    return stableItemReads >= 3;
                }, TimeSpan.FromSeconds(10), "Filtered Shell enumeration did not stabilize at its one matching file.");
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Filtered Shell enumeration did not stabilize at its one matching file; last-count={lastItemCount}.", ex);
            }
            var initialSnapshot = GetStates(pane)[0];
            AssertFilter(initialSnapshot.Filter, filter, "initial tab retains its applied filter");
            if (!SameViewSettings(initialSnapshot.View, expectedView))
                throw new Exception($"Initial Shell tab did not restore its valid Details view state: {DescribeView(initialSnapshot.View)}.");

            var foregroundBeforeNewTab = GetForegroundWindow();
            paneType.GetMethod("OpenPathInNewTab")!.Invoke(pane, [directory]);
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeNewTab, GetForegroundWindow(),
                "creating a new quick-filter Shell tab");
            var foregroundBeforeNewTabReady = GetForegroundWindow();
            WaitFor(() => GetStates(pane).Count == 2 && IsBrowserReady(GetStates(pane)[1].Browser),
                TimeSpan.FromSeconds(10), "New Shell tab did not become ready.");
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeNewTabReady, GetForegroundWindow(),
                "waiting for the new quick-filter Shell tab");
            var afterNew = GetStates(pane);
            AssertFilter(afterNew[0].Filter, filter, "opening a new tab leaves the existing filter intact");
            AssertFilter(afterNew[1].Filter, emptyFilter, "new tab starts without a filter");

            paneType.GetMethod("SelectTabByNumber")!.Invoke(pane, [1]);
            var foregroundBeforeDuplicate = GetForegroundWindow();
            paneType.GetMethod("DuplicateCurrentTab")!.Invoke(pane, null);
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeDuplicate, GetForegroundWindow(),
                "creating a duplicated quick-filter Shell tab");
            var foregroundBeforeDuplicateReady = GetForegroundWindow();
            WaitFor(() => GetStates(pane).Count == 3 && IsBrowserReady(GetStates(pane)[2].Browser),
                TimeSpan.FromSeconds(10), "Duplicated Shell tab did not become ready.");
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeDuplicateReady, GetForegroundWindow(),
                "waiting for the duplicated quick-filter Shell tab");
            var afterDuplicate = GetStates(pane);
            AssertFilter(afterDuplicate[0].Filter, filter, "duplicating a tab leaves its source filter intact");
            AssertFilter(afterDuplicate[1].Filter, emptyFilter, "duplicating another tab leaves its filter intact");
            AssertFilter(afterDuplicate[2].Filter, filter, "duplicate copies the source filter");
            var tabItemCounts = new int[3];
            try
            {
                WaitFor(() =>
                {
                    for (var index = 0; index < tabItemCounts.Length; index++)
                        tabItemCounts[index] = ShellItemCount(afterDuplicate[index].Browser);
                    return tabItemCounts[0] == 1 && tabItemCounts[1] == 2 && tabItemCounts[2] == 1;
                }, TimeSpan.FromSeconds(10), "New and duplicated tabs did not apply their independent column filter state.");
            }
            catch (TimeoutException ex)
            {
                var filterCalls = afterDuplicate.Select(snapshot =>
                {
                    var activeFilter = snapshot.Browser.GetType().GetField("_quickFilter", InstanceMembers)!.GetValue(snapshot.Browser);
                    return activeFilter is null ? -1 :
                        (int)activeFilter.GetType().GetProperty("ShouldShowCalls", InstanceMembers)!.GetValue(activeFilter)!;
                }).ToArray();
                throw new TimeoutException($"New and duplicated tabs did not apply their independent column filter state; counts={string.Join(',', tabItemCounts)}; filter-calls={string.Join(',', filterCalls)}.", ex);
            }
            var sourceViewStyle = GetViewStyle(afterDuplicate[0].View);
            var newTabViewStyle = GetViewStyle(afterDuplicate[1].View);
            var duplicateViewStyle = GetViewStyle(afterDuplicate[2].View);
            if (newTabViewStyle.Mode != 4 || SameViewSettings(afterDuplicate[1].View, afterDuplicate[0].View))
                throw new Exception($"New tab did not use the default Details state independently of the customized source: source={DescribeView(afterDuplicate[0].View)}, new={DescribeView(afterDuplicate[1].View)}.");
            if (duplicateViewStyle != sourceViewStyle)
                throw new Exception($"Duplicate did not retain the source view style: source={sourceViewStyle}, duplicate={duplicateViewStyle}.");
            if (!SameViewSettings(afterDuplicate[2].View, afterDuplicate[0].View))
                throw new Exception($"Duplicate did not preserve the source Shell view settings: source={DescribeView(afterDuplicate[0].View)}, duplicate={DescribeView(afterDuplicate[2].View)}.");

            var selectFolder = typeof(Program).GetMethod("SelectOnlyItem", BindingFlags.Static | BindingFlags.NonPublic)!;
            // The pane shortcut delegate operates on its current tab, so select the target tab before invoking it.
            paneType.GetMethod("SelectTabByNumber")!.Invoke(pane, [2]);
            selectFolder.Invoke(null, [afterDuplicate[1].Browser.GetType(), afterDuplicate[1].Browser,
                directory, "child", true]);
            var shortcutType = app.GetType("ExplorerDirs.App.ExplorerShortcut", throwOnError: true)!;
            var openSelection = Enum.Parse(shortcutType, "OpenSelectionInNewTab");
            var shortcutHandler = afterDuplicate[1].Browser.GetType()
                .GetProperty("ShortcutHandler", InstanceMembers)!.GetValue(afterDuplicate[1].Browser) as Delegate
                ?? throw new Exception("PaneControl did not attach its Shell shortcut handler.");
            var foregroundBeforeSelectedFolder = GetForegroundWindow();
            if (!Equals(shortcutHandler.DynamicInvoke(openSelection), true))
                throw new Exception("Shift+Enter did not consume a selected folder.");
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeSelectedFolder, GetForegroundWindow(),
                "creating a selected-folder quick-filter Shell tab");
            var foregroundBeforeSelectedFolderReady = GetForegroundWindow();
            WaitFor(() => GetStates(pane).Count == 4 && IsBrowserReady(GetStates(pane)[3].Browser),
                TimeSpan.FromSeconds(10), "Selected-folder tab did not become ready.");
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeSelectedFolderReady, GetForegroundWindow(),
                "waiting for the selected-folder quick-filter Shell tab");
            var afterSelectionTab = GetStates(pane);
            if (!string.Equals(afterSelectionTab[3].Path, Path.Combine(directory, "child"), StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Selected-folder tab opened the wrong path: {afterSelectionTab[3].Path}.");
            AssertFilter(afterSelectionTab[3].Filter, emptyFilter, "selected-folder tab uses the new-tab filter default");
            if (GetViewStyle(afterSelectionTab[3].View) != newTabViewStyle)
                throw new Exception($"Selected-folder tab did not use the configured new-tab view style: expected={newTabViewStyle}, actual={GetViewStyle(afterSelectionTab[3].View)}.");
            selectFolder.Invoke(null, [afterSelectionTab[1].Browser.GetType(), afterSelectionTab[1].Browser,
                directory, "initial.txt", true]);
            if (!Equals(shortcutHandler.DynamicInvoke(openSelection), false) || GetStates(pane).Count != 4)
                throw new Exception("Shift+Enter was consumed for a file selection instead of remaining with Shell.");

            // Exercise explicit per-app visibility through the production pane and Shell filter.
            // The system-hidden fixture must remain excluded because the app never requests
            // SHCONTF_INCLUDESUPERHIDDEN.
            var setHiddenPreference = paneType.GetMethod("SetHiddenItemsPreference", InstanceMembers)!;
            var showHidden = Enum.Parse(hiddenItemsPreferenceType, "Show");
            setHiddenPreference.Invoke(pane, [showHidden]);
            WaitForStableItemCount(afterNew[1].Browser, 3,
                "Show-hidden preference did not reveal the ordinary hidden file while keeping the super-hidden file excluded");
            setHiddenPreference.Invoke(pane, [hideHidden]);
            WaitForStableItemCount(afterNew[1].Browser, 2,
                "Hide-hidden preference did not hide the ordinary hidden file");
            setHiddenPreference.Invoke(pane, [followWindows]);

            VerifyTabHistoryIsolation(pane, afterSelectionTab[1].Browser, afterSelectionTab[3].Browser,
                directory, Path.Combine(directory, "child"), siblingDirectory);

            if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
            if (GetForegroundWindow() != foregroundBefore) throw new Exception("The probe changed the foreground window.");
            Console.WriteLine($"quick-filter-tabs: disk-session-restore/new/duplicate/selected-folder/history-isolation PASS; hidden-items=Show:3/Hide:2/super-hidden-excluded; duplicate-view={DescribeView(afterDuplicate[2].View)}; file-selection-unhandled=True; source-isolation=True; parent-hidden=True; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            if (pane is not null)
                try
                {
                    foreach (var state in GetStates(pane))
                        if (disposedBrowsers.Add(state.Browser))
                            state.Browser.GetType().GetMethods(InstanceMembers)
                                .Single(method => method.Name == "Dispose" && method.GetParameters().Length == 0)
                                .Invoke(state.Browser, null);
                }
                catch (Exception cleanupError) { Console.Error.WriteLine($"Probe browser cleanup failed: {cleanupError.Message}"); }
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            foreach (var path in new[] { hiddenPath, superHiddenPath })
                if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            if (Directory.Exists(fixtureRoot) && Path.GetDirectoryName(fixtureRoot) == fixtureParent)
                Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static Array SaveAndReloadTabSettings(Assembly app, Type tabSettingsType, object tab, string fixtureRoot)
    {
        var paneTabsType = typeof(List<>).MakeGenericType(tabSettingsType);
        var paneTabs = Activator.CreateInstance(paneTabsType)!;
        paneTabsType.GetMethod("Add")!.Invoke(paneTabs, [tab]);

        var panesType = typeof(List<>).MakeGenericType(paneTabsType);
        var panes = Activator.CreateInstance(panesType)!;
        panesType.GetMethod("Add")!.Invoke(panes, [paneTabs]);
        var sessionStateType = app.GetType("ExplorerDirs.App.SessionState", throwOnError: true)!;
        var sessionState = Activator.CreateInstance(sessionStateType, InstanceMembers, null,
            [4, 4u, panes, 0, null, -1, null, null, null, null], null)!;

        var sessionPath = Path.Combine(fixtureRoot, "session.ini");
        var legacyPath = Path.Combine(fixtureRoot, "missing-legacy-session.json");
        var sessionStoreType = app.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
        var writer = Activator.CreateInstance(sessionStoreType, InstanceMembers, null,
            [sessionPath, legacyPath], null)!;
        sessionStoreType.GetMethod("Save")!.Invoke(writer, [sessionState]);

        // Use a distinct store instance to model the next application launch reading the persisted snapshot.
        var reader = Activator.CreateInstance(sessionStoreType, InstanceMembers, null,
            [sessionPath, legacyPath], null)!;
        var restoredState = sessionStoreType.GetMethod("Load")!.Invoke(reader, null)!;
        var restoredPanes = (System.Collections.IEnumerable)sessionStateType
            .GetProperty("Panes", InstanceMembers)!.GetValue(restoredState)!;
        var restoredTabs = (System.Collections.IEnumerable)restoredPanes.Cast<object>().First();
        var restoredTabItems = restoredTabs.Cast<object>().ToArray();
        if (restoredTabItems.Length != 1)
            throw new Exception($"Disk session restored {restoredTabItems.Length} tabs instead of one.");

        var result = Array.CreateInstance(tabSettingsType, restoredTabItems.Length);
        result.SetValue(restoredTabItems[0], 0);
        return result;
    }

    private static IReadOnlyList<TabSnapshot> GetStates(object pane)
    {
        var order = (System.Collections.IEnumerable)pane.GetType()
            .GetField("_tabOrder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!;
        return order.Cast<object>().Select(tab =>
        {
            var state = tab.GetType().GetProperty("Tag")!.GetValue(tab)!;
            var browser = state.GetType().GetProperty("Browser")!.GetValue(state)!;
            var filter = browser.GetType().GetProperty("QuickFilter", InstanceMembers)!.GetValue(browser);
            var path = (string)state.GetType().GetProperty("Path", InstanceMembers)!.GetValue(state)!;
            var view = browser.GetType().GetMethod("CachedViewState", InstanceMembers)!.Invoke(browser, null)!;
            return new TabSnapshot(browser, filter, path, view);
        }).ToArray();
    }

    private static void VerifyTabHistoryIsolation(object pane, object firstBrowser, object secondBrowser,
        string root, string child, string sibling)
    {
        var paneType = pane.GetType();
        var select = paneType.GetMethod("SelectTabByNumber")!;
        var paneGoBack = paneType.GetMethod("GoBack")!;
        var paneGoForward = paneType.GetMethod("GoForward")!;
        var browserType = firstBrowser.GetType();
        var navigate = browserType.GetMethod("Navigate", InstanceMembers)!;
        var currentPath = browserType.GetProperty("CurrentPath", InstanceMembers)!;
        var canGoBack = browserType.GetProperty("CanGoBack", InstanceMembers)!;
        var canGoForward = browserType.GetProperty("CanGoForward", InstanceMembers)!;

        // Hidden Shell hosts must retain independent Travel Logs while PaneControl switches selected tabs.
        select.Invoke(pane, [2]);
        navigate.Invoke(firstBrowser, [child]);
        WaitFor(() => string.Equals((string)currentPath.GetValue(firstBrowser)!, child, StringComparison.OrdinalIgnoreCase) &&
                      (bool)canGoBack.GetValue(firstBrowser)!, TimeSpan.FromSeconds(10),
            "First tab did not create its independent back-history entry.");

        select.Invoke(pane, [4]);
        navigate.Invoke(secondBrowser, [sibling]);
        WaitFor(() => string.Equals((string)currentPath.GetValue(secondBrowser)!, sibling, StringComparison.OrdinalIgnoreCase) &&
                      (bool)canGoBack.GetValue(secondBrowser)!, TimeSpan.FromSeconds(10),
            "Second tab did not create its independent back-history entry.");

        select.Invoke(pane, [2]);
        EnsureHistory(firstBrowser, child, back: true, forward: false, currentPath, canGoBack, canGoForward,
            "first tab after switching away and back");
        paneGoBack.Invoke(pane, null);
        WaitFor(() => string.Equals((string)currentPath.GetValue(firstBrowser)!, root, StringComparison.OrdinalIgnoreCase) &&
                      (bool)canGoForward.GetValue(firstBrowser)!, TimeSpan.FromSeconds(10),
            "PaneControl Back did not restore the selected first tab's root location.");

        select.Invoke(pane, [4]);
        EnsureHistory(secondBrowser, sibling, back: true, forward: false, currentPath, canGoBack, canGoForward,
            "second tab after navigating the first tab Back");
        select.Invoke(pane, [2]);
        EnsureHistory(firstBrowser, root, back: false, forward: true, currentPath, canGoBack, canGoForward,
            "first tab before Forward");
        paneGoForward.Invoke(pane, null);
        WaitFor(() => string.Equals((string)currentPath.GetValue(firstBrowser)!, child, StringComparison.OrdinalIgnoreCase) &&
                      !(bool)canGoForward.GetValue(firstBrowser)!, TimeSpan.FromSeconds(10),
            "PaneControl Forward did not restore the selected first tab's child location.");
        Console.WriteLine("tab-history-isolation: PaneControl Back/Forward targeted the selected production Shell tab; another tab retained its independent location and history state");
    }

    private static void EnsureHistory(object browser, string expectedPath, bool back, bool forward,
        PropertyInfo currentPath, PropertyInfo canGoBack, PropertyInfo canGoForward, string phase)
    {
        var actualPath = (string)currentPath.GetValue(browser)!;
        var actualBack = (bool)canGoBack.GetValue(browser)!;
        var actualForward = (bool)canGoForward.GetValue(browser)!;
        if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            actualBack != back || actualForward != forward)
            throw new Exception($"Tab Shell history was not isolated at {phase}: path={actualPath}; back={actualBack}; forward={actualForward}.");
    }

    private static bool IsBrowserReady(object browser)
    {
        // A live child HWND exists before navigation restores the saved Shell view settings.
        if (!(bool)browser.GetType().GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!)
            return false;
        var viewWindow = (nint)browser.GetType().GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
        if (viewWindow == 0 || (bool)browser.GetType().GetProperty("ViewCaptureFailed")!.GetValue(browser)!) return false;
        try
        {
            browser.GetType().GetMethod("CaptureViewState")!.Invoke(browser, null);
            return !(bool)browser.GetType().GetProperty("ViewCaptureFailed")!.GetValue(browser)!;
        }
        catch (TargetInvocationException) { return false; }
    }

    private static (uint Mode, int IconSize) GetViewStyle(object view) =>
        ((uint)view.GetType().GetProperty("Mode")!.GetValue(view)!,
         (int)view.GetType().GetProperty("IconSize")!.GetValue(view)!);

    private static bool SameViewSettings(object left, object right) =>
        GetViewStyle(left) == GetViewStyle(right) &&
        SequencePropertyEquals(left, right, "SortColumns") &&
        Equals(left.GetType().GetProperty("GroupKey")!.GetValue(left), right.GetType().GetProperty("GroupKey")!.GetValue(right)) &&
        Equals(left.GetType().GetProperty("GroupAscending")!.GetValue(left), right.GetType().GetProperty("GroupAscending")!.GetValue(right)) &&
        SequencePropertyEquals(left, right, "DisplayColumns");

    private static bool SequencePropertyEquals(object left, object right, string property)
    {
        var leftValue = left.GetType().GetProperty(property)!.GetValue(left) as System.Collections.IEnumerable;
        var rightValue = right.GetType().GetProperty(property)!.GetValue(right) as System.Collections.IEnumerable;
        if (leftValue is null || rightValue is null) return leftValue is null && rightValue is null;
        return leftValue.Cast<object>().SequenceEqual(rightValue.Cast<object>());
    }

    private static string DescribeView(object view) =>
        $"mode={GetViewStyle(view)}, sort=[{string.Join(",", ((System.Collections.IEnumerable)view.GetType().GetProperty("SortColumns")!.GetValue(view)!).Cast<object>())}], " +
        $"group={view.GetType().GetProperty("GroupKey")!.GetValue(view)}:{view.GetType().GetProperty("GroupAscending")!.GetValue(view)}, " +
        $"columns=[{string.Join(",", ((System.Collections.IEnumerable)view.GetType().GetProperty("DisplayColumns")!.GetValue(view)!).Cast<object>())}]";

    private static void WaitFor(Func<bool> predicate, TimeSpan timeout, string error)
    {
        var end = Stopwatch.StartNew();
        while (end.Elapsed < timeout)
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            if (predicate()) return;
            Thread.Sleep(10);
        }
        if (!predicate()) throw new TimeoutException(error);
    }

    private static void AssertFilter(object? actual, object? expected, string message)
    {
        if (actual is null || expected is null)
        {
            if (!Equals(actual, expected)) throw new Exception($"{message}: expected={expected}, actual={actual}.");
            return;
        }

        var actualType = actual.GetType();
        var expectedType = expected.GetType();
        foreach (var propertyName in new[] { "Pattern", "ExcludeMatches", "FilterFolders" })
        {
            if (!Equals(actualType.GetProperty(propertyName)!.GetValue(actual),
                    expectedType.GetProperty(propertyName)!.GetValue(expected)))
                throw new Exception($"{message}: quick-filter property {propertyName} differed.");
        }

        var actualColumns = ((System.Collections.IEnumerable)actualType.GetProperty("ColumnFilters")!
            .GetValue(actual)!).Cast<object>().ToArray();
        var expectedColumns = ((System.Collections.IEnumerable)expectedType.GetProperty("ColumnFilters")!
            .GetValue(expected)!).Cast<object>().ToArray();
        if (actualColumns.Length != expectedColumns.Length)
            throw new Exception($"{message}: expected {expectedColumns.Length} column filters, actual {actualColumns.Length}.");

        for (var index = 0; index < actualColumns.Length; index++)
        {
            if (actualColumns[index] is null || expectedColumns[index] is null)
                throw new Exception($"{message}: null column-filter entry at {index}; expected=[{string.Join(";", expectedColumns.Select(column => column?.ToString() ?? "<null>"))}], actual=[{string.Join(";", actualColumns.Select(column => column?.ToString() ?? "<null>"))}].");
            var actualColumnType = actualColumns[index].GetType();
            var expectedColumnType = expectedColumns[index].GetType();
            var actualKey = actualColumnType.GetProperty("Key", InstanceMembers)!.GetValue(actualColumns[index]);
            var expectedKey = expectedColumnType.GetProperty("Key", InstanceMembers)!.GetValue(expectedColumns[index]);
            var actualValues = ((System.Collections.IEnumerable)actualColumnType.GetProperty("Values", InstanceMembers)!
                .GetValue(actualColumns[index])!).Cast<string>();
            var expectedValues = ((System.Collections.IEnumerable)expectedColumnType.GetProperty("Values", InstanceMembers)!
                .GetValue(expectedColumns[index])!).Cast<string>();
            if (!Equals(actualKey, expectedKey) || !actualValues.SequenceEqual(expectedValues, StringComparer.Ordinal))
                throw new Exception($"{message}: column filter {index} differed.");
        }
    }

    private sealed record TabSnapshot(object Browser, object? Filter, string Path, object View);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
