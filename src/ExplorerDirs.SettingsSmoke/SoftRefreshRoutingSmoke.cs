using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class SoftRefreshRoutingSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var pane = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var host = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));

        var preferenceRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.SoftRefreshSmoke.{Guid.NewGuid():N}");
        Directory.CreateDirectory(preferenceRoot);
        try
        {
            var preferencePath = Path.Combine(preferenceRoot, "SoftRefresh.ini");
            var missingPreference = new SoftRefreshStore(preferencePath);
            Check(!missingPreference.Load(), "missing Soft-Refresh preference defaults off");
            missingPreference.Save(true);
            Check(new SoftRefreshStore(preferencePath).Load(), "Soft-Refresh persists through its portable profile INI");
        }
        finally { Directory.Delete(preferenceRoot, recursive: true); }

        Check(markup.Contains("x:Name=\"SoftRefreshMenu\"", StringComparison.Ordinal) &&
              markup.Contains("Header=\"Soft-Refresh（不重枚举/防止自动重排）\"", StringComparison.Ordinal) &&
              markup.Contains("ToolTip=\"阻止 Shell 刷新时重新枚举当前列表", StringComparison.Ordinal),
            "View menu names Soft-Refresh and explains re-enumeration behavior");
        Check(window.Contains("SoftRefresh.ini", StringComparison.Ordinal) &&
              window.Contains("pane.SetSoftRefresh(next)", StringComparison.Ordinal),
            "the global portable preference is applied to every existing pane");
        Check(pane.Contains("_softRefresh);", StringComparison.Ordinal) &&
              window.Contains("pane.SetSoftRefresh(next)", StringComparison.Ordinal) &&
              host.Contains("SetNoEnumRefresh(view, _softRefresh)", StringComparison.Ordinal) &&
              host.Contains("ShellFolderViewOptions.SetNoEnumRefresh(view, _softRefresh)", StringComparison.Ordinal),
            "existing and newly created Shell views receive the Soft-Refresh flag");

        Console.WriteLine("Soft-refresh routing smoke: PASS");

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

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Soft-refresh routing smoke: " + message);
            Console.WriteLine("PASS " + message);
        }
    }
}
