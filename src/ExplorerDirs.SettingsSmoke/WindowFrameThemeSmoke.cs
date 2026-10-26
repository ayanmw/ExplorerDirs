using ExplorerDirs.App;
using System.IO;
using System.Windows.Media;

internal static class WindowFrameThemeSmoke
{
    internal static void Run()
    {
        var light = ThemePalette.For(ResolvedTheme.Light, Color.FromRgb(0x12, 0x34, 0x56));
        var themed = NativeWindowFrameTheme.BuildSettings(light);
        Check(NativeWindowFrameTheme.ToColorRef(Color.FromRgb(0x12, 0x34, 0x56)) == 0x00563412 &&
              themed.Count == 3 &&
              themed.Single(setting => setting.Attribute == 34).ColorRef == 0x00a0a0a0 &&
              themed.Single(setting => setting.Attribute == 35).ColorRef == 0x00ffffff &&
              themed.Single(setting => setting.Attribute == 36).ColorRef == 0x001c1c1c,
            "caption, caption text, and border map theme RGB colors to DWM COLORREF values");

        var system = NativeWindowFrameTheme.BuildSettings(null);
        Check(system.Count == 3 && system.All(setting => setting.ColorRef == 0xffffffff),
            "high contrast resets every custom DWM frame color to the Windows default");

        var repoRoot = FindRepoRoot();
        var mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var photoPrintWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PhotoPrintWindow.xaml.cs"));
        Check(mainWindow.Contains("NativeWindowFrameTheme.Apply", StringComparison.Ordinal) &&
              mainWindow.Contains("SourceInitialized", StringComparison.Ordinal) &&
              mainWindow.Contains("ThemeController_ThemeChanged", StringComparison.Ordinal) &&
              photoPrintWindow.Contains("NativeWindowFrameTheme.Apply", StringComparison.Ordinal) &&
              photoPrintWindow.Contains("SourceInitialized", StringComparison.Ordinal),
            "main and photo-print windows apply frame colors at creation and theme updates");

        Console.WriteLine("Window frame theme smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "App.xaml")))
                    return directory.FullName;
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Window frame theme: " + description);
            Console.WriteLine("PASS " + description);
        }
    }
}
