using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ExplorerChromeIconSmoke
{
    internal static void Run()
    {
        var geometries = ExplorerChromeIcons.All;
        Check(geometries.Count == 27,
            "navigation, tab, and file command controls have dedicated vector geometries");
        Check(geometries.All(geometry => !geometry.IsEmpty() &&
              geometry.Bounds.X >= 0 && geometry.Bounds.Y >= 0 &&
              geometry.Bounds.Right <= 16 && geometry.Bounds.Bottom <= 16),
            "navigation and tab geometries parse within the 16-unit icon grid");
        Check(geometries.Select(geometry => geometry.ToString()).Distinct(StringComparer.Ordinal).Count() == geometries.Count,
            "navigation, tab, and file command paths are distinct");

        var fileCommands = ShellFileCommands.All;
        Check(fileCommands.Count == 9 && fileCommands
                  .Select(command => ExplorerChromeIcons.ForCommand(command.Command).ToString())
                  .Distinct(StringComparer.Ordinal).Count() == fileCommands.Count,
            "every Shell file command maps to its own vector geometry");

        var repoRoot = FindRepoRoot();
        var factory = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ExplorerChromeIcons.cs"));
        var appMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "App.xaml"));
        var viewMenu = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ViewStyleMenu.cs"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var paneMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        var mainCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var mainMarkup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        Check(factory.Contains("ExplorerDirs.ControlTextBrush", StringComparison.Ordinal) &&
              factory.Contains("SetResourceReference", StringComparison.Ordinal),
            "shared icon paths use a dynamic semantic theme brush");
        Check(appMarkup.Contains("TargetType=\"{x:Type Path}\"", StringComparison.Ordinal) &&
              appMarkup.Contains("RelativeSource AncestorType={x:Type Control}", StringComparison.Ordinal) &&
              appMarkup.Contains("IsEnabled", StringComparison.Ordinal) &&
              appMarkup.Contains("ExplorerDirs.MutedTextBrush", StringComparison.Ordinal),
            "vector icons use the muted semantic color when their owning control is disabled");
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
              paneCode.Contains("AddressComputerButton.Content = new Image", StringComparison.Ordinal) &&
              paneCode.Contains("AddressHistoryButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Back_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Forward_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Up_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Refresh_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"Tree_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"AddressMenu_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"此电脑\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"AddressComputer_Click\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("Click=\"AddressHistory_Click\"", StringComparison.Ordinal) &&
              paneCode.Contains("addressComputerButtonVisible: true", StringComparison.Ordinal) &&
              paneCode.Contains("AddressComputerButton, AddressHistoryButton", StringComparison.Ordinal) &&
              // The common-location catalog now shares this icon's menu with drives and favorites.
              paneCode.Contains("AddressPathMenu.BuildComputerMenu()", StringComparison.Ordinal) &&
              paneCode.Contains("PlacementTarget = AddressComputerButton", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"后退\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"前进\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"上级目录\"", StringComparison.Ordinal),
            "pane navigation icons preserve their original handlers and explicit accessible names");
        Check(paneCode.Contains("NewTabButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneCode.Contains("CloseTabButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"新建标签页\"", StringComparison.Ordinal) &&
              paneMarkup.Contains("AutomationProperties.Name=\"关闭标签页\"", StringComparison.Ordinal),
            "pane tab controls use the shared vector icons and preserve accessible names");
        Check(mainCode.Contains("NewTabToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("NewWindowToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("DuplicateTabToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("CloseTabToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("RefreshCurrentDirectoryButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("SearchCurrentDirectoryButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"NewTab_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"NewWindow_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Header=\"复制当前窗口\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("ToolTip=\"复制当前窗口及其标签和布局 (Ctrl+N)\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"DuplicateTab_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"CloseTab_Click\"", StringComparison.Ordinal),
            "main tab/navigation toolbar icons preserve command handlers");
        Check(factory.Contains("CreateLabeledButtonContent", StringComparison.Ordinal) &&
              factory.Contains("CreateMenuIcon", StringComparison.Ordinal) &&
              mainCode.Contains("ExplorerChromeIcons.CreateLabeledButtonContent(info.Command, info.Label)", StringComparison.Ordinal) &&
              mainCode.Contains("ExplorerChromeIcons.CreateMenuIcon(info.Command)", StringComparison.Ordinal),
            "file command toolbar and menu create icons from the existing command identity");
        Check(mainCode.Contains("PrintFileViewToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("FolderSizeToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("RefreshEnvironmentToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainCode.Contains("OpenTerminalToolbarButton.Content = ExplorerChromeIcons.CreatePath", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"PrintFileView_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"CalculateDirectorySize_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"RefreshEnvironment_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"OpenTerminal_Click\"", StringComparison.Ordinal),
            "print, folder size, environment, and terminal buttons keep their original handlers");
        Check(mainCode.Contains("PreviewPaneToggle.Content = ExplorerChromeIcons.CreateLabeledButtonContent", StringComparison.Ordinal) &&
              mainCode.Contains("DetailsPaneToggle.Content = ExplorerChromeIcons.CreateLabeledButtonContent", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"PreviewPaneToggle_Click\"", StringComparison.Ordinal) &&
              mainMarkup.Contains("Click=\"DetailsPaneToggle_Click\"", StringComparison.Ordinal),
            "preview and details toggles use labeled vector icons without changing their commands");

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
