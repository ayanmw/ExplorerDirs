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
                CheckUnhostedCopyOrder(sourcePane, targetPane);
                CheckUnhostedReopenHistory(sourcePane);
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
