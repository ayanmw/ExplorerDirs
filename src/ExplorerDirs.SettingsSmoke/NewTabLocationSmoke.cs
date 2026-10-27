using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class NewTabLocationSmoke
{
    internal static void Run()
    {
        CheckPortablePreference();
        CheckWiring();
        Console.WriteLine("New tab location smoke: PASS");
    }

    private static void CheckPortablePreference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.NewTabLocation.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "NewTabLocation.ini");
        try
        {
            var store = new PortableBooleanPreference(path, "Tabs", "UseCurrentFolder", "新标签目录");
            Check(!store.Load(), "new tabs default to the existing Home behavior");
            store.Save(true);
            Check(new PortableBooleanPreference(path, "Tabs", "UseCurrentFolder", "新标签目录").Load(),
                "the current-folder choice persists in a portable INI");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void CheckWiring()
    {
        var repoRoot = FindRepoRoot();
        var markup = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml"));
        var windowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        Check(markup.Contains("NewTabUsesCurrentFolderMenu", StringComparison.Ordinal) &&
              markup.Contains("Click=\"NewTabUsesCurrentFolder_Click\"", StringComparison.Ordinal),
            "the tab menu exposes a current-folder default toggle");
        Check(windowCode.Contains("NewTabLocation.ini", StringComparison.Ordinal) &&
              windowCode.Contains("SetNewTabUsesCurrentFolder", StringComparison.Ordinal),
            "the preference loads from the profile and is applied to panes");
        Check(paneCode.Contains("public void NewTab() => AddTab(_newTabUsesCurrentFolder ? CurrentPath : Home, _newTabView());",
                StringComparison.Ordinal),
            "regular new tabs use the active pane path only when enabled");
        Check(paneCode.Contains("public void OpenPathInNewTab(string path) => AddTab(path, _newTabView());",
                StringComparison.Ordinal),
            "explicit open-in-new-tab keeps its requested folder");
    }

    private static string FindRepoRoot()
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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
