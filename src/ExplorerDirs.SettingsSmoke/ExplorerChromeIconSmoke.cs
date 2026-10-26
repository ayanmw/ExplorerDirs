using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ExplorerChromeIconSmoke
{
    internal static void Run()
    {
        var geometries = ExplorerChromeIcons.All;
        Check(geometries.Count == 12,
            "navigation and tab commands have a dedicated vector geometry");
        Check(geometries.All(geometry => !geometry.IsEmpty() &&
              geometry.Bounds.X >= 0 && geometry.Bounds.Y >= 0 &&
              geometry.Bounds.Right <= 16 && geometry.Bounds.Bottom <= 16),
            "navigation and tab geometries parse within the 16-unit icon grid");
        Check(geometries.Select(geometry => geometry.ToString()).Distinct(StringComparer.Ordinal).Count() == geometries.Count,
            "navigation and tab command paths are distinct");

        var repoRoot = FindRepoRoot();
        var factory = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ExplorerChromeIcons.cs"));
        var viewMenu = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ViewStyleMenu.cs"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var paneMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        var mainCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var mainMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        Check(factory.Contains("ExplorerDirs.ControlTextBrush", StringComparison.Ordinal) &&
              factory.Contains("SetResourceReference", StringComparison.Ordinal),
            "shared icon paths use a dynamic semantic theme brush");
        Check(viewMenu.Contains("ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              viewMenu.Contains("ExplorerChromeIcons.CreateButtonContent", StringComparison.Ordinal) &&
              viewMenu.Contains("item.Click += (_, _) => apply(style)", StringComparison.Ordinal),
            "existing view style menu and toolbar use the shared factory without changing command dispatch");
        Check(paneCode.Contains("BackButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("ForwardButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("UpButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("RefreshButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("TreeButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("AddressMenuButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("AddressHistoryButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Back_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Forward_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Up_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Refresh_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Tree_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"AddressMenu_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"AddressHistory_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"后退\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"前进\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"上级目录\"", StringComparison.Ordinal),
            "pane navigation icons preserve their original handlers and explicit accessible names");
        Check(mainCode.Contains("NewTabToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("NewWindowToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("DuplicateTabToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("CloseTabToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("RefreshCurrentDirectoryButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("SearchCurrentDirectoryButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"NewTab_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"NewWindow_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"DuplicateTab_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"CloseTab_Click\"", StringComparison.Ordinal),
            "main tab/navigation toolbar icons preserve command handlers");

        Console.WriteLine("Explorer chrome icon smoke: PASS");

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
            if (!condition) throw new InvalidOperationException("Explorer chrome icon smoke: " + label);
            Console.WriteLine("PASS " + label);
        }
    }
}
