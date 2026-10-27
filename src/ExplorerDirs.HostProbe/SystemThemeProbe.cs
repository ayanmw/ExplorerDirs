using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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
            var app = new Application();
            var controllerType = assembly.GetType("ExplorerDirs.App.ApplicationThemeController", true)!;
            var preferenceType = assembly.GetType("ExplorerDirs.App.ThemePreference", true)!;
            foreach (var mode in new[] { "System", "Light", "Dark", "System" })
            {
                using var controller = (IDisposable)Activator.CreateInstance(controllerType, app, root)!;
                controllerType.GetMethod("SetPreference")!.Invoke(controller, [Enum.Parse(preferenceType, mode)]);
                var expected = mode == "System" ? (expectedDark ? "Dark" : "Light") : mode;
                Check(controllerType.GetProperty("Resolved")!.GetValue(controller)!.ToString() == expected,
                    $"{mode} resolves to {expected} in the actual controller");
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
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
