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
            var app = (Application)Activator.CreateInstance(appType)!;
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
            Check(app.Windows.Count == 0, "theme checks create no application windows");
        }
        finally { Directory.Delete(root, recursive: true); }
        Check(GetForegroundWindow() == foreground, "theme checks leave the foreground unchanged");
        Console.WriteLine($"System theme probe: PASS; WindowsIsDark={actualDark}; no Shell views or input");
        return 0;
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
    }

    private static Color ReadPixel(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(260, 28, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(3, 3, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
