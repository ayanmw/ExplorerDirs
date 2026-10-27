using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ExplorerDirs.App;

// Actual product request state only. This host is never attached to a window,
// so ExplorerBrowser COM creation, native view activation and desktop input cannot run.
internal static class NavigationFocusRequestProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var app = Assembly.Load("ExplorerDirs.App");
        var hostType = app.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var viewType = app.GetType("ExplorerDirs.App.ViewState", true)!;
        var view = viewType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [4u, -1]);
        using (var unavailable = (HwndHost)Activator.CreateInstance(hostType, Members, null,
                   [Path.GetTempPath(), view!], null)!)
        {
            hostType.GetMethod("ApplyViewState", Members)!.Invoke(unavailable, null);
            Ensure((bool)hostType.GetProperty("ViewCaptureFailed", Members)!.GetValue(unavailable)!,
                "background-tab save protection is visible before any later capture");
            var captured = hostType.GetMethod("CaptureViewState", Members)!.Invoke(unavailable, null)!;
            Ensure((bool)hostType.GetProperty("ViewCaptureFailed", Members)!.GetValue(unavailable)!,
                "an unavailable restore target protects saved view settings even without custom columns");
            Ensure((uint)viewType.GetProperty("Mode", Members)!.GetValue(captured)! == 4 &&
                unavailable.Handle == 0 && Field(unavailable, "_browser") is null,
                "failed restore preserves the saved mode without creating a native view");
        }
        using var host = (HwndHost)Activator.CreateInstance(hostType, Members, null, [Path.GetTempPath(), view!], null)!;
        var readiness = Field(host, "_viewReadiness")!;
        var readyType = readiness.GetType();
        var restore = hostType.GetMethod("RestoreNavigationFocus", Members)!;
        var allowed = false;
        var intentReads = 0;
        hostType.GetProperty("MayRestoreFocus", Members)!.SetValue(host, (Func<bool>)(() =>
        {
            intentReads++;
            return allowed;
        }));

        long Ready(string identity)
        {
            readyType.GetMethod("Begin", Members)!.Invoke(readiness, [identity]);
            readyType.GetMethod("Complete", Members)!.Invoke(readiness, [identity]);
            return (long)readyType.GetProperty("NavigationVersion", Members)!.GetValue(readiness)!;
        }
        void Request(bool waitingForView, bool restoringAfterNavigation)
        {
            hostType.GetField("_focusWhenReady", Members)!.SetValue(host, waitingForView);
            hostType.GetField("_restoreFocusAfterNavigation", Members)!.SetValue(host, restoringAfterNavigation);
        }
        bool HasRequest() => (bool)Field(host, "_focusWhenReady")! || (bool)Field(host, "_restoreFocusAfterNavigation")!;

        var first = Ready("first");
        Request(true, true);
        restore.Invoke(host, [first]);
        Ensure(!HasRequest(), "denied navigation focus is consumed rather than carried into a later visit");
        Ensure(intentReads == 1, "navigation completion consults current focus intent");

        var second = Ready("second");
        Request(true, true);
        restore.Invoke(host, [first]);
        Ensure(HasRequest() && intentReads == 1, "an old callback cannot consume the new visit's focus request");
        allowed = true;
        restore.Invoke(host, [second]);
        Ensure(!HasRequest() && intentReads == 2, "current requests reach the guarded restorer and are consumed");
        restore.Invoke(host, [second]);
        Ensure(intentReads == 2, "a duplicate completion does not reactivate an already consumed request");

        foreach (var flags in new[] { (true, false), (false, true) })
        {
            Request(flags.Item1, flags.Item2);
            allowed = false;
            restore.Invoke(host, [second]);
            Ensure(!HasRequest(), "both initial-view and navigation focus requests retire independently");
        }
        readyType.GetMethod("Begin", Members)!.Invoke(readiness, ["still-pending"]);
        var pendingVersion = (long)readyType.GetProperty("NavigationVersion", Members)!.GetValue(readiness)!;
        Request(true, true);
        restore.Invoke(host, [pendingVersion]);
        Ensure(HasRequest(), "a pending navigation cannot consume focus before it completes");
        Ensure(host.Handle == 0 && Field(host, "_browser") is null && (nint)Field(host, "_viewWindow")! == 0,
            "no native host or Shell view was created");
        CheckWindowIntent();
        Ensure(foreground == GetForegroundWindow(), "foreground remained unchanged");
        Console.WriteLine("Navigation focus requests: PASS (actual unhosted request state; no native focus or Shell view)");
        return 0;
    }

    private static void CheckWindowIntent()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs.NavigationFocusProbe"));
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainWindow? window = null;
        try
        {
            window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), Members, null,
                [null, null, null, Path.Combine(root, "ExplorerDirs.ini"), root, null, null], null)!;
            Ensure((bool)Field(window, "_fileViewFocusDesired")!, "startup initially requests file-view focus");
            var pane = ((PaneControl[])Field(window, "_panes")!)[0];
            var search = (TextBox)Field(pane, "SearchBox")!;
            // Routed focus notification only, not Keyboard.Focus or desktop input.
            window.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, search)
            {
                RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent
            });
            Ensure(!(bool)Field(window, "_fileViewFocusDesired")!, "moving to an editor cancels initial file-focus intent");
            search.Text = "probe";
            pane.GetType().GetMethod("SubmitSearch", Members)!.Invoke(pane, null);
            var current = pane.GetType().GetProperty("Current", Members)!.GetValue(pane)!;
            Ensure(!(bool)current.GetType().GetProperty("SearchRequestPending", Members)!.GetValue(current)! &&
                !(bool)Field(window, "_fileViewFocusDesired")!,
                "a synchronous search-start failure does not request file focus");
            Ensure(new WindowInteropHelper(window).Handle == 0, "the product window was never hosted or shown");
            foreach (var item in ((TabControl)Field(pane, "Tabs")!).Items.Cast<TabItem>())
            {
                var browser = (HwndHost)item.Tag.GetType().GetProperty("Browser", Members)!.GetValue(item.Tag)!;
                Ensure(browser.Handle == 0 && Field(browser, "_browser") is null, "search test created no Shell view");
            }
        }
        finally
        {
            window?.Close();
            if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped its test directory.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static object? Field(object instance, string name) => instance.GetType().GetField(name, Members)!.GetValue(instance);
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
