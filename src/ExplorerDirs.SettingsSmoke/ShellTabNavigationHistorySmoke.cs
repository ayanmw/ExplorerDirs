using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellTabNavigationHistorySmoke
{
    public static void Run()
    {
        var history = new ShellTabNavigationHistory(@"C:\start");
        history.UpdateCurrentSelection([@"C:\start\start.txt"]);
        history.Record(@"C:\middle");
        history.UpdateCurrentSelection([@"C:\middle\middle-a.txt", @"C:\middle\middle-b.txt"]);
        history.Record("search-ms:query=one");
        history.UpdateCurrentSelection([@"C:\results\match.txt"]);
        Check(history.TryBeginMove(back: true, out var backTarget) && backTarget == @"C:\middle",
            "cloned view history exposes the prior ordinary Shell location");
        Check(history.CommitLocation(@"C:\middle") && history.Current == @"C:\middle" && history.CanGoForward &&
              history.CurrentSelection.SequenceEqual([@"C:\middle\middle-a.txt", @"C:\middle\middle-b.txt"]),
            "a completed back navigation restores the target stop and its saved selection");
        history.Record(@"C:\branch");
        Check(!history.CanGoForward && history.Snapshot().Select(stop => stop.Location)
                  .SequenceEqual([@"C:\start", @"C:\middle", @"C:\branch"]) &&
              history.Snapshot()[0].SelectedItems.SequenceEqual([@"C:\start\start.txt"]) &&
              history.Snapshot()[1].SelectedItems.SequenceEqual([@"C:\middle\middle-a.txt", @"C:\middle\middle-b.txt"]),
            "a new navigation truncates the cloned forward branch");
        Check(history.TryBeginMove(back: true, out var pending) && pending == @"C:\middle" &&
              !history.TryBeginMove(back: true, out _) && history.CancelPendingMove(),
            "only one history traversal can be pending and a failed request can be cancelled");

        var cloned = ShellTabNavigationHistory.FromSnapshot(history.Snapshot(), history.Index);
        Check(cloned.Current == history.Current && cloned.TryBeginMove(back: true, out var cloneTarget) &&
              cloneTarget == @"C:\middle" && cloned.CommitLocation(@"C:\middle") &&
              cloned.CurrentSelection.SequenceEqual([@"C:\middle\middle-a.txt", @"C:\middle\middle-b.txt"]),
            "a cloned view keeps independent locations and per-stop selections");

        var namesRead = 0;
        var searchSelectionIndexes = ShellSelectionMatch.FindViewIndexes(EnumerateNames(),
            [@"c:\RESULTS\match-a.txt", @"C:\results\match-b.txt"]);
        Check(searchSelectionIndexes.SequenceEqual([1, 0]) && namesRead == 2,
            "search result selections map by name, keep saved order and stop reading once all items match");
        Check(ShellSelectionMatch.FindViewIndexes(
                  [@"C:\results\match-a.txt"], [@"C:\results\match-a.txt", @"C:\results\late.txt"]).Length == 0,
            "search selection matching refuses a partial result set so the caller can retry safely");
        Check(ShellSelectionMatch.IsSameItem(@"C:\results\match-a.txt", @"c:\RESULTS\MATCH-A.TXT") &&
              !ShellSelectionMatch.IsSameItem(@"C:\results\match-a.txt", @"C:\results\new-first.txt") &&
              !ShellSelectionMatch.IsSameItem(@"C:\results\match-a.txt", null),
            "search selection identity validation rejects a row replaced by a newly arrived result");

        var search = new ShellTabNavigationHistory(@"C:\scope");
        search.UpdateCurrentSelection([@"C:\scope\before.txt"]);
        const string searchUri = "search-ms:query=report&crumb=location:C%3A%5Cscope";
        search.Record(searchUri);
        search.UpdateCurrentSelection([@"C:\scope\report.txt"]);
        Check(search.TryBeginMove(back: true, out var searchBack) && searchBack == @"C:\scope" &&
              search.CommitLocation(@"C:\scope") && search.TryBeginMove(back: false, out var searchForward) &&
              searchForward == searchUri && search.CommitLocation(searchUri) &&
              search.CurrentSelection.SequenceEqual([@"C:\scope\report.txt"]),
            "search result URIs and their selection snapshot remain distinct history stops");

        var bounded = new ShellTabNavigationHistory(@"C:\0");
        for (var index = 1; index <= 120; index++) bounded.Record($@"C:\{index}");
        Check(bounded.Snapshot().Count == 100 && bounded.Snapshot()[0].Location == @"C:\21" &&
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
              cloneCode.Contains("CaptureSelectedShellParsingNames", StringComparison.Ordinal) &&
              cloneCode.Contains("var viewGeneration = state.Browser.ViewGeneration;", StringComparison.Ordinal) &&
              cloneCode.Contains("var navigationVersion = state.Browser.NavigationVersion;", StringComparison.Ordinal) &&
              cloneCode.Contains("var selectionVersion = state.Browser.SelectionVersion;", StringComparison.Ordinal) &&
              cloneCode.Contains("var cloneSearchUri = state.Browser.CurrentSearchUri;", StringComparison.Ordinal) &&
              cloneCode.Contains("state.Browser.NavigationVersion != navigationVersion", StringComparison.Ordinal) &&
              cloneCode.Contains("state.Browser.ViewGeneration != viewGeneration", StringComparison.Ordinal) &&
              cloneCode.Contains("state.Browser.SelectionVersion != selectionVersion", StringComparison.Ordinal) &&
              cloneCode.Contains("state.Browser.CurrentPath, clonePath", StringComparison.Ordinal) &&
              cloneCode.Contains("state.Browser.CurrentSearchUri, cloneSearchUri", StringComparison.Ordinal) &&
              paneSource.Contains("var selectedItems = browser.CaptureSelectedShellParsingNames();", StringComparison.Ordinal) &&
              paneSource.Contains("state.Browser.ViewGeneration != viewGeneration", StringComparison.Ordinal) &&
              paneSource.Contains("state.NavigationHistory.CurrentSelection", StringComparison.Ordinal) &&
              paneSource.Contains("browser.QueueShellItemSelections(selectionToRestore)", StringComparison.Ordinal) &&
              paneSource.Contains("state.HistoryReplayReady = false", StringComparison.Ordinal) &&
              paneSource.Contains("state.HistoryReplayReady = true", StringComparison.Ordinal),
            "the tab menu separates duplicate behavior while clone history captures and restores each stop's selection");

        var browserSource = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));
        Check(browserSource.Contains("const uint allItemsInViewOrder = 0x80000002", StringComparison.Ordinal) &&
              browserSource.Contains("ShellSelectionMatch.IsSameItem(selectedParsingNames[index], currentParsingName)", StringComparison.Ordinal) &&
              browserSource.Contains("shellView.SelectItem(childPidl", StringComparison.Ordinal) &&
              browserSource.Contains("_createdFileSelectionAttempts % 5 != 1", StringComparison.Ordinal) &&
              browserSource.Contains("SupportsSearchLocation: _location.IsSearchActive", StringComparison.Ordinal),
            "search result selections validate the live Shell item and select it by PIDL instead of a shifting row index");

        IEnumerable<string> EnumerateNames()
        {
            foreach (var name in new[] { @"C:\results\match-b.txt", @"C:\results\match-a.txt", @"C:\results\other.txt" })
            {
                namesRead++;
                yield return name;
            }
        }

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
