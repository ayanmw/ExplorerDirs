using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ViewStyleIconSmoke
{
    internal static void Run()
    {
        var geometries = ViewStyleIconSet.All;
        Check(geometries.Count == ViewStyle.All.Count && geometries.Count == 8,
            "one vector geometry is defined for each Explorer view style");
        Check(geometries.All(geometry => !geometry.IsEmpty() &&
              geometry.Bounds.X >= 0 && geometry.Bounds.Y >= 0 &&
              geometry.Bounds.Right <= 16 && geometry.Bounds.Bottom <= 16),
            "all view geometries parse and fit the 16-unit icon grid");
        Check(geometries.Select(geometry => geometry.ToString()).Distinct(StringComparer.Ordinal).Count() == 8,
            "each named view style has a distinct vector geometry");

        var repoRoot = FindRepoRoot();
        var menuCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ViewStyleMenu.cs"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var paneMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        Check(menuCode.Contains("CreateMenuIcon(style", StringComparison.Ordinal) &&
              menuCode.Contains("ExplorerDirs.ControlTextBrush", StringComparison.Ordinal) &&
              menuCode.Contains("item.Click += (_, _) => apply(style)", StringComparison.Ordinal),
            "view menu icons use the semantic text role and preserve Shell command dispatch");
        Check(paneCode.Contains("ViewStyleMenu.CreateToolbarIcon()", StringComparison.Ordinal) &&
              paneCode.Contains("ToolbarViewStyleButton.Content", StringComparison.Ordinal) &&
              paneCode.Contains("ViewStyleButton.Content", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"查看方式\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"当前窗格查看方式\"", StringComparison.Ordinal),
            "both pane view buttons use the vector icon and retain accessibility names");

        Console.WriteLine("View style icon smoke: PASS");

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "ViewStyleMenu.cs")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("View style icon smoke: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
