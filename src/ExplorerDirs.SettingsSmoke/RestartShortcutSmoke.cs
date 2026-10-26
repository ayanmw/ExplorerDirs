using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class RestartShortcutSmoke
{
    internal static void Run()
    {
        Check(KeyboardShortcuts.Resolve(0x7A, 1) == ExplorerShortcut.RestartApplication &&
              KeyboardShortcuts.Resolve(0x7A, 0) == ExplorerShortcut.ToggleMaximize,
            "Alt+F11 restarts the app without changing F11 maximize behavior");

        var repoRoot = FindRepoRoot();
        var mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var markup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        Check(mainWindow.Contains("case ExplorerShortcut.RestartApplication:", StringComparison.Ordinal) &&
              mainWindow.Contains("RequestRestartApplication();", StringComparison.Ordinal) &&
              mainWindow.Contains("_restartApplication();", StringComparison.Ordinal) &&
              mainWindow.Contains("MessageBoxButton.YesNo", StringComparison.Ordinal),
            "restart shortcut uses the existing confirmation and session-safe restart callback");
        Check(markup.Contains("InputGestureText=\"Alt+F11\"", StringComparison.Ordinal),
            "the File menu advertises the restart shortcut");

        Console.WriteLine("Restart shortcut smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "MainWindow.xaml")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Restart shortcut smoke: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
