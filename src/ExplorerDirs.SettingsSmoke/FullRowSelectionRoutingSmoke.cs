using System.IO;
using ExplorerDirs.App;

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

        var preferenceRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.DisableGroupingSmoke.{Guid.NewGuid():N}");
        Directory.CreateDirectory(preferenceRoot);
        try
        {
            var preferencePath = Path.Combine(preferenceRoot, "DisableGrouping.ini");
            var missingPreference = new PortableBooleanPreference(preferencePath, "View", "DisableGrouping", "全局分组");
            Check(!missingPreference.Load(), "missing global grouping preference defaults to enabled grouping");
            missingPreference.Save(true);
            Check(new PortableBooleanPreference(preferencePath, "View", "DisableGrouping", "全局分组").Load(),
                "global grouping preference persists through the portable profile INI");
        }
        finally { Directory.Delete(preferenceRoot, recursive: true); }

        Check(markup.Contains("x:Name=\"DisableGroupingMenu\" Header=\"停用所有视图分组\" IsCheckable=\"True\"", StringComparison.Ordinal),
            "View menu exposes the profile-wide grouping override");
        Check(window.Contains("\"DisableGrouping.ini\"", StringComparison.Ordinal) &&
              window.Contains("pane.SetGroupingDisabled(next)", StringComparison.Ordinal),
            "the profile preference is saved and routed across existing panes");
        Check(pane.Contains("_disableGrouping, _softRefresh);", StringComparison.Ordinal) &&
              host.Contains("ShellFolderViewOptions.SetNoGrouping(view, _disableGrouping)", StringComparison.Ordinal),
            "new tabs apply the grouping override when their Shell view is created");

        Check(markup.Contains("x:Name=\"FullRowSelectionMenu\" Header=\"整行选择\" IsCheckable=\"True\"", StringComparison.Ordinal),
            "View menu exposes the full-row selection preference");
        Check(window.Contains("\"FullRowSelection.ini\"", StringComparison.Ordinal) &&
              window.Contains("_fullRowSelectionStore.HasExplicitValue ? fullRowSelectionValue : null", StringComparison.Ordinal),
            "missing or invalid preference leaves new views on their Shell default");
        Check(window.Contains("pane.SetFullRowSelection(next)", StringComparison.Ordinal),
            "the preference is applied across every existing pane");
        Check(pane.Contains("_fullRowSelection,", StringComparison.Ordinal) &&
              host.Contains("if (_fullRowSelection is { } fullRowSelection)", StringComparison.Ordinal),
            "new tabs apply the explicit preference when their Shell view is created");
        // Native restore applies grouping policy after adapting the saved presentation
        // for the folder-size provider; the tab's cached grouping stays independent.
        Check(host.Contains("ShellGroupingPolicy.ForNativeRestore(presentation, _disableGrouping)", StringComparison.Ordinal) &&
              host.Contains("ShellGroupingPolicy.PreserveCachedGrouping(captured, _viewState, _disableGrouping)", StringComparison.Ordinal),
            "native suppression preserves each tab's cached grouping for later restoration");

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
