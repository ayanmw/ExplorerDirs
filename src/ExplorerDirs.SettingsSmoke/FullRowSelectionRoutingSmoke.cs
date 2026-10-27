using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class FullRowSelectionRoutingSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var pane = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var host = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));

        Check(markup.Contains("x:Name=\"FullRowSelectionMenu\" Header=\"整行选择\" IsCheckable=\"True\"", StringComparison.Ordinal),
            "View menu exposes the full-row selection preference");
        Check(window.Contains("\"FullRowSelection.ini\"", StringComparison.Ordinal) &&
              window.Contains("_fullRowSelectionStore.HasExplicitValue ? fullRowSelectionValue : null", StringComparison.Ordinal),
            "missing or invalid preference leaves new views on their Shell default");
        Check(window.Contains("pane.SetFullRowSelection(next)", StringComparison.Ordinal),
            "the preference is applied across every existing pane");
        Check(pane.Contains("_fullRowSelection);", StringComparison.Ordinal) &&
              host.Contains("if (_fullRowSelection is { } fullRowSelection)", StringComparison.Ordinal),
            "new tabs apply the explicit preference when their Shell view is created");

        Console.WriteLine("Full-row selection routing smoke: PASS");

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
            if (!condition) throw new InvalidOperationException("Full-row selection routing smoke: " + message);
            Console.WriteLine("PASS " + message);
        }
    }
}
