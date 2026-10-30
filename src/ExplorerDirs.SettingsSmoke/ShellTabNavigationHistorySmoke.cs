using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellTabNavigationHistorySmoke
{
    public static void Run()
    {
        var history = new ShellTabNavigationHistory(@"C:\start");
        history.Record(@"C:\middle");
        history.Record("search-ms:query=one");
        Check(history.TryBeginMove(back: true, out var backTarget) && backTarget == @"C:\middle",
            "cloned view history exposes the prior ordinary Shell location");
        Check(history.CommitLocation(@"C:\middle") && history.Current == @"C:\middle" && history.CanGoForward,
            "a completed back navigation moves the cloned history cursor");
        history.Record(@"C:\branch");
        Check(!history.CanGoForward && history.Snapshot().SequenceEqual([@"C:\start", @"C:\middle", @"C:\branch"]),
            "a new navigation truncates the cloned forward branch");
        Check(history.TryBeginMove(back: true, out var pending) && pending == @"C:\middle" &&
              !history.TryBeginMove(back: true, out _) && history.CancelPendingMove(),
            "only one history traversal can be pending and a failed request can be cancelled");

        var cloned = ShellTabNavigationHistory.FromSnapshot(history.Snapshot(), history.Index);
        Check(cloned.Current == history.Current && cloned.TryBeginMove(back: true, out var cloneTarget) &&
              cloneTarget == @"C:\middle",
            "a cloned view keeps an independent copy of its location history");

        var search = new ShellTabNavigationHistory(@"C:\scope");
        const string searchUri = "search-ms:query=report&crumb=location:C%3A%5Cscope";
        search.Record(searchUri);
        Check(search.TryBeginMove(back: true, out var searchBack) && searchBack == @"C:\scope" &&
              search.CommitLocation(@"C:\scope") && search.TryBeginMove(back: false, out var searchForward) &&
              searchForward == searchUri && search.CommitLocation(searchUri),
            "search result URIs remain distinct history locations and can be replayed in both directions");

        var bounded = new ShellTabNavigationHistory(@"C:\0");
        for (var index = 1; index <= 120; index++) bounded.Record($@"C:\{index}");
        Check(bounded.Snapshot().Count == 100 && bounded.Snapshot()[0] == @"C:\21" &&
              bounded.Current == @"C:\120" && bounded.Index == 99,
            "cloned view history keeps a bounded set of recent locations");

        var repoRoot = FindRepoRoot();
        var paneSource = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "PaneControl.xaml.cs"));
        var duplicateStart = paneSource.IndexOf("private void DuplicateTab(TabState state)", StringComparison.Ordinal);
        var duplicateEnd = paneSource.IndexOf("public void RefreshCurrent()", duplicateStart, StringComparison.Ordinal);
        var duplicateCode = paneSource[duplicateStart..duplicateEnd];
        var cloneStart = paneSource.IndexOf("private void CloneView(TabState state)", StringComparison.Ordinal);
        var cloneEnd = paneSource.IndexOf("private static bool NavigateHistoryLocation", cloneStart, StringComparison.Ordinal);
        var cloneCode = paneSource[cloneStart..cloneEnd];
        Check(paneSource.Contains("\"克隆当前视图\", () => CloneView(state)", StringComparison.Ordinal) &&
              !duplicateCode.Contains("ShellTabNavigationHistory.FromSnapshot", StringComparison.Ordinal) &&
              cloneCode.Contains("ShellTabNavigationHistory.FromSnapshot", StringComparison.Ordinal) &&
              cloneCode.Contains("CaptureSelectedShellParsingNames", StringComparison.Ordinal),
            "the tab menu separates existing duplicate behavior from history-and-selection cloning");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "PaneControl.xaml.cs")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }
}
