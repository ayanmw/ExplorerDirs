using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ExplorerDirs.App;

// Never shows a window or attaches an ExplorerBrowserHost to an HWND. The only
// native child in this probe is an ordinary STATIC window owned by CountingHost.
internal static class WindowCloseLifetimeProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var failures = new List<string>();
        RunCase(CheckParentDestruction, "native parent destruction");
        RunCase(CheckNativeCascade, "same-monitor native cascade");
        RunCase(CheckUnshownWindowClose, "closing one application window");
        if (GetForegroundWindow() != foreground) failures.Add("probe changed foreground");
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        if (failures.Count != 0) return 1;
        Console.WriteLine("Window close lifetime: PASS (ordinary HWND and unshown product windows; no Shell view created)");
        return 0;

        void RunCase(Action action, string name)
        {
            try { action(); Console.WriteLine($"PASS: {name}"); }
            catch (Exception ex) { failures.Add($"FAIL: {name}: {ex.GetBaseException().Message}"); }
        }
    }

    private static void CheckParentDestruction()
    {
        using var source = new HwndSource(new HwndSourceParameters("non-Shell close lifecycle")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000000,
            Width = 100, Height = 100
        });
        using var child = new CountingHost();
        source.RootVisual = child;
        child.Measure(new Size(100, 100));
        child.Arrange(new Rect(0, 0, 100, 100));
        Ensure(child.Handle != 0, "plain native child was created");
        source.Dispose();
        Ensure(child.Handle != 0 && child.DestroyCount == 0 && !IsDisposed(child),
            "source disposal parks the native child without disposing its HwndHost");
        child.Dispose();
        Ensure(child.DestroyCount == 1, "explicit host disposal releases the native child");
    }

    private static void CheckNativeCascade()
    {
        using var source = new HwndSource(new HwndSourceParameters("non-Shell cascade source")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000000,
            PositionX = 100, PositionY = 100, Width = 600, Height = 400
        });
        using var target = new HwndSource(new HwndSourceParameters("non-Shell cascade target")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000000,
            PositionX = 0, PositionY = 0, Width = 500, Height = 300
        });
        Ensure(GetWindowRect(source.Handle, out var beforeSource), "plain hidden source has a native rectangle");
        Ensure(GetWindowRect(target.Handle, out var beforeTarget), "plain hidden target has a native rectangle");
        var monitor = MonitorFromWindow(source.Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Ensure(GetMonitorInfo(monitor, ref info), "source monitor has a native work area");
        typeof(MainWindow).Assembly.GetType("ExplorerDirs.App.WindowCascade", true)!
            .GetMethod("Place", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [target.Handle, source.Handle]);
        Ensure(GetWindowRect(target.Handle, out var afterTarget), "cascade leaves its target valid");
        Ensure(GetWindowRect(source.Handle, out var afterSource), "cascade leaves its source valid");
        Ensure(afterTarget.Right - afterTarget.Left == beforeTarget.Right - beforeTarget.Left &&
            afterTarget.Bottom - afterTarget.Top == beforeTarget.Bottom - beforeTarget.Top,
            "same-DPI cascade preserves target size");
        Ensure(afterTarget.Left >= info.Work.Left && afterTarget.Top >= info.Work.Top &&
            afterTarget.Right <= info.Work.Right && afterTarget.Bottom <= info.Work.Bottom,
            "native target is constrained to the source monitor's work area");
        Ensure(afterSource.Equals(beforeSource), "cascade does not move its source window");
        Ensure(!IsWindowVisible(source.Handle) && !IsWindowVisible(target.Handle), "cascade never shows either window");
    }

    private static void CheckUnshownWindowClose()
    {
        var fixtureParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs.WindowCloseProbe"));
        var root = Path.GetFullPath(Path.Combine(fixtureParent, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var windows = new List<MainWindow>();
        var allHosts = new List<HwndHost>();
        try
        {
            foreach (var name in new[] { "first", "second" })
            {
                var profile = Path.Combine(root, name);
                Directory.CreateDirectory(profile);
                var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), Members, null,
                    [null, null, null, Path.Combine(profile, "ExplorerDirs.ini"), profile, null, null], null)!;
                windows.Add(window);
                var panes = (PaneControl[])Field(window, "_panes")!;
                panes[0].OpenPathInNewTab(root);
                panes[0].GetType().GetMethod("SetTreeVisible", Members)!.Invoke(panes[0], [true]);
                var treeModeField = typeof(MainWindow).GetField("_treeDisplayMode", Members)!;
                treeModeField.SetValue(window, Enum.Parse(treeModeField.FieldType, "Shared"));
                typeof(MainWindow).GetMethod("ApplyTreeDisplayMode", Members)!.Invoke(window, null);
                foreach (var pane in panes)
                {
                    var tabs = (TabControl)Field(pane, "Tabs")!;
                    foreach (var tab in tabs.Items.Cast<TabItem>())
                        allHosts.Add((HwndHost)tab.Tag.GetType().GetProperty("Browser", Members)!.GetValue(tab.Tag)!);
                    allHosts.Add((HwndHost)Field(pane, "AddressTreeHost")!);
                    if (Field(pane, "_treeHost") is HwndHost tree) allHosts.Add(tree);
                }
                allHosts.Add((HwndHost)Field(window, "_sharedTreeHost")!);
                Ensure(new WindowInteropHelper(window).Handle == 0, "product window has never acquired an HWND");
            }
            Ensure(allHosts.All(host => host.Handle == 0), "product hosts remained unattached");
            CheckMinimizedRestoration(windows[0]);
            var firstPane = ((PaneControl[])Field(windows[0], "_panes")!)[0];
            var secondPane = ((PaneControl[])Field(windows[1], "_panes")!)[0];
            var movedTab = (TabItem)((TabControl)Field(firstPane, "Tabs")!).Items[0];
            var movedHost = (HwndHost)movedTab.Tag.GetType().GetProperty("Browser", Members)!.GetValue(movedTab.Tag)!;
            Ensure((bool)typeof(PaneControl).GetMethod("TransferTabTo", Members)!
                .Invoke(firstPane, [movedTab.Tag, secondPane, null, true])!, "transfer a tab to the other window");
            var firstHosts = allHosts.Take(allHosts.Count / 2).Where(host => host != movedHost).ToArray();
            var secondHosts = allHosts.Skip(allHosts.Count / 2).Append(movedHost).ToArray();
            windows[0].Close();
            Ensure(firstHosts.All(IsDisposed), "closing a window disposes every file and tree host, including inactive tabs");
            Ensure(((CancellationTokenSource)Field(windows[0], "_windowOperationStop")!).IsCancellationRequested,
                "closing cancels the owning window's background waits");
            Ensure(!((CancellationTokenSource)Field(windows[1], "_windowOperationStop")!).IsCancellationRequested,
                "closing leaves sibling background waits active");
            Ensure(secondHosts.All(host => !IsDisposed(host)),
                "closing one window preserves the sibling window and the transferred tab");
            foreach (var name in new[] { "_saveTimer", "_viewPollTimer", "_fileCommandRefreshTimer" })
                Ensure(!((DispatcherTimer)Field(windows[0], name)!).IsEnabled, "closed window stops " + name);
            typeof(MainWindow).GetMethod("ScheduleSave", Members)!.Invoke(windows[0], null);
            Ensure(!((DispatcherTimer)Field(windows[0], "_saveTimer")!).IsEnabled,
                "a late view callback cannot schedule another save for the closed window");
            Ensure(File.Exists(Path.Combine(root, "first", "ExplorerDirs.ini")), "session saved before resource cleanup");
            Ensure(File.ReadAllText(Path.Combine(root, "first", "ExplorerDirs.ini")).Contains(root, StringComparison.Ordinal),
                "closing preserved the added tab in the saved session");
            windows[1].Close();
            Ensure(allHosts.All(IsDisposed), "the second window cleans up its own hosts when closed");
        }
        finally
        {
            foreach (var window in windows)
            {
                window.Close();
                foreach (var name in new[] { "_saveTimer", "_viewPollTimer", "_fileCommandRefreshTimer" })
                    ((DispatcherTimer)Field(window, name)!).Stop();
            }
            foreach (var host in allHosts) host.Dispose();
            if (!root.StartsWith(fixtureParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped its test directory.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckMinimizedRestoration(MainWindow window)
    {
        // Source review found both single-instance activation routes forced Normal,
        // losing the user's maximized state. Exercise their shared restore entry
        // without activating or showing the window or creating a Shell view.
        var restore = typeof(MainWindow).GetMethod("RestoreFromMinimized", Members)
            ?? throw new InvalidOperationException("minimized activation has no shared state-preserving restore entry");
        var lastMaximized = typeof(MainWindow).GetField("_lastNonMinimizedMaximized", Members)!;
        lastMaximized.SetValue(window, true);
        window.WindowState = WindowState.Minimized;
        restore.Invoke(window, null);
        Ensure(window.WindowState == WindowState.Maximized, "minimized maximized window restores maximized");
        lastMaximized.SetValue(window, false);
        window.WindowState = WindowState.Minimized;
        restore.Invoke(window, null);
        Ensure(window.WindowState == WindowState.Normal, "minimized ordinary window restores normally");
        lastMaximized.SetValue(window, true);
        restore.Invoke(window, null);
        Ensure(window.WindowState == WindowState.Normal, "activation does not change a nonminimized window");

        var fullScreen = typeof(MainWindow).GetField("_fullScreenRestoreState", Members)!;
        fullScreen.SetValue(window, Activator.CreateInstance(Nullable.GetUnderlyingType(fullScreen.FieldType)!));
        window.WindowState = WindowState.Minimized;
        restore.Invoke(window, null);
        Ensure(window.WindowState == WindowState.Normal && fullScreen.GetValue(window) is not null,
            "minimized immersive window restores without maximizing or clearing its F11 return state");
        fullScreen.SetValue(window, null);
        Ensure(new WindowInteropHelper(window).Handle == 0 && !window.IsVisible,
            "restoration checks never showed or attached the product window");
    }

    private static bool IsDisposed(HwndHost host) => (bool)typeof(HwndHost).GetField("_isDisposed", Members)!.GetValue(host)!;
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, Members)!.GetValue(instance);
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CountingHost : HwndHost
    {
        internal int DestroyCount { get; private set; }
        protected override HandleRef BuildWindowCore(HandleRef parent) => new(this,
            CreateWindowEx(0, "STATIC", "", 0x40000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0));
        protected override void DestroyWindowCore(HandleRef hwnd) { DestroyCount++; DestroyWindow(hwnd.Handle); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(int extendedStyle, string className, string title, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
