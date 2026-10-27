using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ExplorerDirs.App;

// Uses ordinary hidden STATIC windows only. The production browser object stays
// unattached, so its BuildWindowCore and ExplorerBrowser COM creation never run.
internal static class TabHostLifetimeProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        using var source = new HwndSource(new HwndSourceParameters("Explorer Dirs non-Shell tab lifetime probe")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x08000000,
            Width = 600,
            Height = 300
        });
        var left = new PersistentTabHost();
        var right = new PersistentTabHost();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        root.Children.Add(left);
        root.Children.Add(right);
        source.RootVisual = root;
        root.Measure(new Size(600, 300));
        root.Arrange(new Rect(0, 0, 600, 300));

        using (var child = new CountingHost())
        {
            left.Show(child);
            PumpLayout(root);
            var original = child.Handle;
            Ensure(original != 0 && child.BuildCount == 1, "one native child created");
            for (var round = 0; round < 8; round++)
            {
                var from = round % 2 == 0 ? left : right;
                var to = round % 2 == 0 ? right : left;
                from.Remove(child);
                PumpLayout(root);
                Ensure(child.Handle == original && IsWindow(original) && child.DestroyCount == 0,
                    "WPF parks a detached child without invoking DestroyWindowCore");
                to.Show(child);
                PumpLayout(root);
                Ensure(child.Handle == original && child.BuildCount == 1 && child.DestroyCount == 0,
                    "WPF reattaches the same child without invoking BuildWindowCore again");
            }
            left.Remove(child);
            PumpLayout(root);
            child.Dispose();
            Ensure(child.DestroyCount == 1 && !IsWindow(original), "closing a moved child destroys its window exactly once");
        }
        Ensure(GetForegroundWindow() == foreground, "ordinary hidden host did not change foreground");
        CheckProductionCleanup(source.Handle);
        Ensure(GetForegroundWindow() == foreground, "production cleanup did not change foreground");
        Console.WriteLine("Tab host lifetime probe: PASS (WPF reparenting and production cleanup; no Shell view created)");
        return 0;
    }

    private static void CheckProductionCleanup(nint parent)
    {
        var app = Assembly.Load("ExplorerDirs.App");
        var hostType = app.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var viewType = app.GetType("ExplorerDirs.App.ViewState", true)!;
        var view = viewType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [4u, -1]);
        using var instance = (HwndHost)Activator.CreateInstance(hostType, Members, null,
            [Path.GetTempPath(), view!], null)!;
        var browserField = hostType.GetField("_browser", Members)!;
        Ensure(browserField.GetValue(instance) is null, "unattached production object has no ExplorerBrowser COM object");
        var native = CreateWindowEx(0, "STATIC", "", 0x40000000, 0, 0, 1, 1, parent, 0, 0, 0);
        Ensure(native != 0, "created the standalone production cleanup fixture");
        try
        {
            var hostWindow = hostType.GetField("_hostWindow", Members)!;
            hostWindow.SetValue(instance, native);
            // Reproduce the former transfer preparation if that API still exists.
            // The old guard stayed set when WPF parked/reparented instead of rebuilding.
            hostType.GetMethod("PrepareForRehost", Members)?.Invoke(instance, null);
            hostType.GetMethod("DestroyWindowCore", Members)!.Invoke(instance, [new HandleRef(instance, native)]);
            Ensure(!IsWindow(native) && (nint)hostWindow.GetValue(instance)! == 0,
                "production DestroyWindowCore releases the child even after a prior pane transfer");
            Ensure(browserField.GetValue(instance) is null, "cleanup did not create a Shell browser");
        }
        finally { if (IsWindow(native)) DestroyWindow(native); }
    }

    private static void PumpLayout(FrameworkElement root)
    {
        root.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class CountingHost : HwndHost
    {
        internal int BuildCount { get; private set; }
        internal int DestroyCount { get; private set; }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            BuildCount++;
            var hwnd = CreateWindowEx(0, "STATIC", "", 0x40000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
            if (hwnd == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new HandleRef(this, hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyCount++;
            DestroyWindow(hwnd.Handle);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(int extendedStyle, string className, string title, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
