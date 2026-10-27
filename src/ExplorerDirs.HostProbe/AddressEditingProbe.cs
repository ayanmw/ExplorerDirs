using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ExplorerDirs.App;

// Exercise actual routed events on unshown controls, without an HWND, Shell
// view, desktop input, or a visible window. This does not prove physical focus.
internal static class AddressEditingProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs.AddressEditingProbe"));
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainWindow? window = null;
        var foreground = GetForegroundWindow();
        try
        {
            window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), Members, null,
                [null, null, null, Path.Combine(root, "ExplorerDirs.ini"), root, null, null], null)!;
            var panes = (PaneControl[])Field(window, "_panes")!;
            foreach (var pane in panes)
            {
                Call(pane, "SetClassicAddressBar", false);
                var scroll = (ScrollViewer)Field(pane, "BreadcrumbScroll")!;
                var buttons = (StackPanel)Field(pane, "BreadcrumbButtons")!;
                var address = (TextBox)Field(pane, "Address")!;
                var originalPath = address.Text;

                // This is the user's missing path: clicking blank space in the
                // modern address bar should not require the separate Edit button.
                RaiseMouseDown(scroll, MouseButton.Left);
                Ensure(address.Visibility == Visibility.Visible && !address.IsReadOnly &&
                    scroll.Visibility == Visibility.Collapsed && address.Text == originalPath &&
                    address.SelectionLength == originalPath.Length,
                    "modern address background enters editing with the whole current path selected");
                Ensure(scroll.Background is not null, "empty address space participates in hit testing");
                Call(pane, "ShowBreadcrumb");

                foreach (var button in buttons.Children.OfType<Button>())
                {
                    RaiseMouseDown(button, MouseButton.Left);
                    Ensure(address.Visibility == Visibility.Collapsed,
                        "breadcrumb buttons keep their navigation click instead of entering editing");
                }
                RaiseMouseDown(scroll, MouseButton.Right);
                Ensure(address.Visibility == Visibility.Collapsed, "right click does not enter address editing");
                foreach (var separator in buttons.Children.OfType<TextBlock>())
                {
                    RaiseMouseDown(separator, MouseButton.Left);
                    Ensure(!address.IsReadOnly, "space between path segments enters editing");
                    Call(pane, "ShowBreadcrumb");
                }
                Call(pane, "SetClassicAddressBar", true);
                pane.FocusAddress();
                Ensure(!address.IsReadOnly && address.SelectionLength == originalPath.Length,
                    "classic and keyboard address editing retain the shared behavior");
                Call(pane, "ShowBreadcrumb");
                Ensure(address.Visibility == Visibility.Visible && address.IsReadOnly,
                    "leaving classic editing restores its path display");

                CheckLateNavigationResults(pane, root);

                foreach (TabItem tab in ((TabControl)Field(pane, "Tabs")!).Items)
                {
                    var browser = (HwndHost)tab.Tag.GetType().GetProperty("Browser", Members)!.GetValue(tab.Tag)!;
                    Ensure(browser.Handle == 0 && Field(browser, "_browser") is null,
                        "the file browser remains unhosted and no Shell view was created");
                }
            }
            Ensure(new WindowInteropHelper(window).Handle == 0, "the test window has no HWND");
            Ensure(GetForegroundWindow() == foreground, "the foreground window is unchanged");
            Console.WriteLine("Address editing: PASS (four unshown panes; routed edits and delayed result notifications, no Shell view or desktop input)");
            return 0;
        }
        finally
        {
            window?.Close();
            if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped its test directory.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckLateNavigationResults(PaneControl pane, string root)
    {
        var state = pane.GetType().GetProperty("Current", Members)!.GetValue(pane)!;
        var browser = state.GetType().GetProperty("Browser", Members)!.GetValue(state)!;
        var address = (TextBox)Field(pane, "Address")!;
        var firstPath = Path.Combine(root, "first");
        var nextPath = Path.Combine(root, "next");
        pane.FocusAddress();
        address.Text = firstPath;
        Call(pane, "Go_Click", Field(pane, "GoButton")!, new RoutedEventArgs());
        address.Text = nextPath;
        Notify(browser, "Navigated", firstPath);
        Ensure(address.Text == nextPath && !address.IsReadOnly && pane.CurrentPath == firstPath,
            "a completed directory updates tab identity without replacing a newer address draft");
        Call(pane, "Go_Click", Field(pane, "GoButton")!, new RoutedEventArgs());
        Notify(browser, "Navigated", nextPath);
        Ensure(address.Text == nextPath && address.IsReadOnly && pane.CurrentPath == nextPath,
            "an unchanged address submission still leaves editing after completion");

        var search = (TextBox)Field(pane, "SearchBox")!;
        var location = Field(browser, "_location")!;
        void CompleteSearch(string query)
        {
            Call(location, "TryCompleteSearch", "search-ms:query=" + Uri.EscapeDataString(query) +
                "&crumb=location:" + Uri.EscapeDataString(nextPath));
            Notify(browser, "SearchNavigated", query);
        }
        CompleteSearch("earlier");
        search.Text = "new draft";
        CompleteSearch("earlier");
        Ensure(search.Text == "new draft", "late search success preserves newer text even after input loses focus");
        Notify(browser, "NavigationFailed", "probe failure");
        Ensure(search.Text == "new draft", "late search failure also preserves newer text");
        pane.FocusSearch();
        Ensure(search.Text == "new draft", "refocusing search does not reset its draft");
        search.Text = "  new draft  ";
        Call(pane, "SubmitSearch");
        Ensure(search.Text == "new draft", "explicit submit normalizes query whitespace without reverting to the old query");
        CompleteSearch("earlier");
        Ensure(search.Text == "new draft", "an older search result cannot replace the newer submitted query");
        CompleteSearch("new draft");
        Ensure(!(bool)Field(pane, "_searchTextDirty")!, "the matching result acknowledges submitted search text");
        search.Text = string.Empty;
        Notify(browser, "NavigationFailed", "probe failure");
        Ensure(search.Text.Length == 0, "clearing the search input is also a draft, not permission to restore the old query");
        Call(pane, "CloseSearch");
        pane.FocusSearch();
        Ensure(search.Text.Length == 0, "closing search discards the draft and returns to the directory query state");

        search.Text = "old-tab draft";
        pane.OpenPathInNewTab(root);
        Ensure(search.Text.Length == 0 && !(bool)Field(pane, "_searchTextDirty")!,
            "switching tabs resets the search editor to the selected tab's query");
        pane.FocusAddress();
        address.Text = "unsubmitted current-tab text";
        Notify(browser, "Navigated", firstPath);
        Ensure(address.Text == "unsubmitted current-tab text", "a background tab cannot replace the active tab's draft");
    }

    private static void Notify(object browser, string eventName, string value) =>
        ((EventHandler<string>?)Field(browser, eventName))?.Invoke(browser, value);

    private static void RaiseMouseDown(UIElement element, MouseButton button) => element.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = Mouse.MouseDownEvent });
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, Members)!.GetValue(instance);
    private static void Call(object instance, string name, params object[] args) =>
        instance.GetType().GetMethod(name, Members)!.Invoke(instance, args);
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
