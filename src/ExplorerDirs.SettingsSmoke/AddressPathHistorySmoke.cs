using ExplorerDirs.App;
using System.IO;
using System.Xml.Linq;

internal static class AddressPathHistorySmoke
{
    internal static void Run()
    {
        var history = new AddressPathHistory();
        history.Record(@"C:\one");
        history.Record(@"C:\two");
        history.Record(@"c:\ONE");
        Check(history.Paths.SequenceEqual([@"c:\ONE", @"C:\two"]),
            "address history moves a revisited path to the front without duplicates");
        var copy = history.Copy();
        copy.Record(@"D:\only-copy");
        Check(history.Paths.Count == 2 && copy.Paths[0] == @"D:\only-copy",
            "duplicated tab address histories are independent");
        for (var index = 0; index < 25; index++) history.Record($@"C:\path-{index}");
        Check(history.Paths.Count == 20 && history.Paths[0] == @"C:\path-24" &&
              history.Paths[^1] == @"C:\path-5",
            "address history retains the 20 most recently visited paths");

        var repoRoot = FindRepoRoot();
        var paneMarkup = XDocument.Load(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var backButton = paneMarkup.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "BackButton");
        var forwardButton = paneMarkup.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "ForwardButton");
        var paneCode = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        Check((string?)backButton.Attribute("PreviewMouseRightButtonUp") == "NavigationHistoryRightClick" &&
              (string?)forwardButton.Attribute("PreviewMouseRightButtonUp") == "NavigationHistoryRightClick" &&
              paneCode.Contains("ShowAddressHistory(target)", StringComparison.Ordinal),
            "right-clicking either navigation arrow opens the shared recent-path menu at the clicked button");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PaneControl.xaml")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
        }
    }
}
