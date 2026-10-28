using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.UI.ViewManagement;

internal static class SystemThemeProbe
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var assembly = Assembly.Load("ExplorerDirs.App");
        var type = assembly.GetType("ExplorerDirs.App.SystemThemeMonitor", true)!;
        using var monitor = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        var color = new UISettings().GetColorValue(UIColorType.Foreground);
        var expectedDark = 5 * color.G + 2 * color.R + color.B > 8 * 128;
        var actualDark = (bool)type.GetProperty("IsDark")!.GetValue(monitor)!;
        Check(actualDark == expectedDark, "production monitor matches Windows foreground theme semantics");

        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.ThemeProbe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var appType = assembly.GetType("ExplorerDirs.App.App", true)!;
            // App construction queues production startup. Abort that initial work
            // before pumping lifecycle callbacks: the fixture must open no sessions.
            var initialOperations = new List<System.Windows.Threading.DispatcherOperation>();
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Windows.Threading.DispatcherHookEventHandler posted = (_, e) => initialOperations.Add(e.Operation);
            dispatcher.Hooks.OperationPosted += posted;
            Application app;
            try { app = (Application)Activator.CreateInstance(appType)!; }
            finally { dispatcher.Hooks.OperationPosted -= posted; }
            foreach (var operation in initialOperations) operation.Abort();
            appType.GetMethod("InitializeComponent")!.Invoke(app, null);
            var controllerType = assembly.GetType("ExplorerDirs.App.ApplicationThemeController", true)!;
            var preferenceType = assembly.GetType("ExplorerDirs.App.ThemePreference", true)!;
            foreach (var mode in new[] { "System", "Light", "Dark", "System" })
            {
                using var controller = (IDisposable)Activator.CreateInstance(controllerType, app, root)!;
                controllerType.GetMethod("SetPreference")!.Invoke(controller, [Enum.Parse(preferenceType, mode)]);
                var expected = mode == "System" ? (expectedDark ? "Dark" : "Light") : mode;
                Check(controllerType.GetProperty("Resolved")!.GetValue(controller)!.ToString() == expected,
                    $"{mode} resolves to {expected} in the actual controller");
                if (!SystemParameters.HighContrast)
                    CheckMenuSurfaces(app, controllerType.GetProperty("CurrentColors")!.GetValue(controller)!);
                using var restored = (IDisposable)Activator.CreateInstance(controllerType, app, root)!;
                Check(controllerType.GetProperty("Preference")!.GetValue(restored)!.ToString() == mode &&
                    controllerType.GetProperty("Resolved")!.GetValue(restored)!.ToString() == expected,
                    $"{mode} survives controller recreation through portable Theme.ini");
            }
            CheckRefreshLifecycle(app, controllerType, preferenceType, root);
            Check(app.Windows.Count == 0, "theme checks create no application windows");
        }
        finally { Directory.Delete(root, recursive: true); }
        Check(GetForegroundWindow() == foreground, "theme checks leave the foreground unchanged");
        Console.WriteLine($"System theme probe: PASS; WindowsIsDark={actualDark}; no Shell views or input");
        return 0;
    }

    private static void CheckRefreshLifecycle(Application app, Type controllerType, Type preferenceType, string root)
    {
        var members = BindingFlags.NonPublic | BindingFlags.Instance;
        var controller = (IDisposable)Activator.CreateInstance(controllerType, app, root)!;
        var changes = 0;
        EventHandler changed = (_, _) => changes++;
        controllerType.GetEvent("ThemeChanged")!.AddEventHandler(controller, changed);
        var brush = app.Resources["ExplorerDirs.WindowBackgroundBrush"];
        var parameters = controllerType.GetMethod("OnSystemParametersChanged", members)!;
        var systemColor = controllerType.GetMethod("OnSystemColorValuesChanged", members)!;
        void Drain() => app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        try
        {
            var queued = 0;
            System.Windows.Threading.DispatcherHookEventHandler posted = (_, e) =>
            {
                if (e.Operation.Priority == System.Windows.Threading.DispatcherPriority.Background) queued++;
            };
            app.Dispatcher.Hooks.OperationPosted += posted;
            for (var i = 0; i < 20; i++)
                parameters.Invoke(controller, [null, new System.ComponentModel.PropertyChangedEventArgs("WindowGlassColor")]);
            systemColor.Invoke(controller, [null, EventArgs.Empty]);
            app.Dispatcher.Hooks.OperationPosted -= posted;
            Check(queued == 1, "a burst of system property and color notifications queues one theme pass");
            Drain();
            if (!SystemParameters.HighContrast)
                Check(changes == 0 && ReferenceEquals(brush, app.Resources["ExplorerDirs.WindowBackgroundBrush"]),
                    "unchanged system notifications preserve brushes and do not refresh every pane");
            changes = 0;
            var current = controllerType.GetProperty("Resolved")!.GetValue(controller)!.ToString();
            controllerType.GetMethod("SetPreference")!.Invoke(controller, [Enum.Parse(preferenceType, current!)]);
            Check(changes == 1 && ReferenceEquals(brush, app.Resources["ExplorerDirs.WindowBackgroundBrush"]),
                "switching System to the same fixed colors updates preference UI without replacing brushes");
            changes = 0;
            var next = current == "Dark" ? "Light" : "Dark";
            controllerType.GetMethod("SetPreference")!.Invoke(controller, [Enum.Parse(preferenceType, next)]);
            Check(changes == 1, "an actual theme preference change still publishes its update");
            if (!SystemParameters.HighContrast)
            {
                brush = app.Resources["ExplorerDirs.WindowBackgroundBrush"];
                var accent = Color.FromRgb(41, 103, 167);
                controllerType.GetMethod("SetCustomAccentColor")!.Invoke(controller, [accent]);
                Check(ReferenceEquals(brush, app.Resources["ExplorerDirs.WindowBackgroundBrush"]),
                    "accent changes preserve neutral surface brush identity");
                changes = 0;
                controllerType.GetMethod("SetCustomAccentColor")!.Invoke(controller, [accent]);
                Check(changes == 0, "reapplying an identical accent does not request another theme repaint");
            }
            parameters.Invoke(controller, [null, new System.ComponentModel.PropertyChangedEventArgs("HighContrast")]);
            controller.Dispose();
            changes = 0;
            brush = app.Resources["ExplorerDirs.WindowBackgroundBrush"];
            Drain();
            Check(changes == 0 && ReferenceEquals(brush, app.Resources["ExplorerDirs.WindowBackgroundBrush"]),
                "a queued system callback cannot repaint after controller disposal");
        }
        finally { controller.Dispose(); }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private static void CheckMenuSurfaces(Application app, object colors)
    {
        Color ColorFor(string role) => (Color)colors.GetType().GetProperty(role)!.GetValue(colors)!;
        var item = new MenuItem { Header = "主题", Width = 260, Height = 28,
            Style = (Style)app.FindResource(typeof(MenuItem)) };
        item.Measure(new Size(260, 28));
        item.Arrange(new Rect(0, 0, 260, 28));
        item.ApplyTemplate();
        Check(ReadPixel(item) == ColorFor("ControlBackground"), "normal menu renders the active palette instead of an Aero white surface");
        var key = (DependencyPropertyKey)typeof(MenuItem).GetField("IsHighlightedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        item.SetValue(key, true);
        Check(ReadPixel(item) == ColorFor("SelectionBackground") &&
              ((SolidColorBrush)item.Foreground).Color == ColorFor("SelectionText"),
            "highlighted menu renders matching selection background and readable text");
        item.SetValue(key, false);
        item.IsEnabled = false;
        Check(ReadPixel(item) == ColorFor("ControlBackground") &&
              ((SolidColorBrush)item.Foreground).Color == ColorFor("MutedText"),
            "disabled menu keeps its themed surface and muted text");
        var tip = new ToolTip { Content = "文件信息", Width = 260, Height = 28,
            Style = (Style)app.FindResource(typeof(ToolTip)) };
        tip.Measure(new Size(260, 28));
        tip.Arrange(new Rect(0, 0, 260, 28));
        Check(ReadPixel(tip, 200, 6) == ColorFor("ControlBackground") &&
              ((SolidColorBrush)tip.Foreground).Color == ColorFor("ControlText"),
            "WPF tooltip renders matching background and readable text without opening a popup");
        foreach (var control in new Control[] { new DataGrid(), new DataGridRow(), new DataGridCell(), new System.Windows.Controls.Primitives.DataGridColumnHeader() })
        {
            control.Style = app.TryFindResource(control.GetType()) as Style;
            Check(control.Background is SolidColorBrush background && background.Color == ColorFor("ControlBackground") &&
                  control.Foreground is SolidColorBrush foreground && foreground.Color == ColorFor("ControlText"),
                $"{control.GetType().Name} uses paired palette colors in secondary windows");
        }
        var context = new ContextMenu { Width = 260, Height = 28, Style = (Style)app.FindResource(typeof(ContextMenu)) };
        context.Measure(new Size(260, 28));
        context.Arrange(new Rect(0, 0, 260, 28));
        Check(ReadPixel(context, 200, 6) == ColorFor("ControlBackground"),
            "tab context menu template paints the themed surface without an Aero gutter");
        var toolbar = new ToolBar { Width = 260, Style = (Style)app.FindResource(typeof(ToolBar)) };
        toolbar.Items.Add(new Button { Content = "导航" });
        toolbar.Measure(new Size(260, 28));
        toolbar.Arrange(new Rect(0, 0, 260, 28));
        toolbar.ApplyTemplate();
        Check(toolbar.Template.FindName("PART_ToolBarPanel", toolbar) is not null &&
              toolbar.Template.FindName("PART_ToolBarOverflowPanel", toolbar) is not null,
            "the themed toolbar instantiates both required native WPF layout parts");
    }

    private static Color ReadPixel(FrameworkElement element, int x = 3, int y = 3)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(260, 28, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
