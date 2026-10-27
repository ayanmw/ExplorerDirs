using ExplorerDirs.App;
using System.IO;

internal static class QuickFilterHistoryStoreSmoke
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "quick-filter-history", "QuickFilterHistory.ini");
        var store = new QuickFilterHistoryStore(path);
        Require(store.Load().Count == 0, "a missing history file starts empty");

        store.Record("*.txt;*.md");
        store.Record("*.pdf");
        store.Record("*.TXT;*.MD");
        Require(store.Load().SequenceEqual(["*.TXT;*.MD", "*.pdf"]),
            "recording promotes a case-insensitive duplicate to the most-recent position");

        store.Remove("*.pdf");
        Require(store.Load().SequenceEqual(["*.TXT;*.MD"]), "Delete removes the selected history entry");

        foreach (var index in Enumerable.Range(0, 60)) store.Record($"pattern-{index}");
        var bounded = store.Load();
        Require(bounded.Count == QuickFilterHistoryStore.MaxEntries && bounded[0] == "pattern-59" &&
                !bounded.Contains("*.TXT;*.MD"), "history retains only its newest bounded entries");

        var futurePath = Path.Combine(Path.GetDirectoryName(path)!, "future.ini");
        const string future = "[QuickFilterHistory]\nFormatVersion=2\nCount=0\n";
        File.WriteAllText(futurePath, future);
        var futureStore = new QuickFilterHistoryStore(futurePath);
        futureStore.Load();
        Throws<IOException>(() => futureStore.Record("*.txt"), "future formats cannot be overwritten");
        Require(File.ReadAllText(futurePath) == future, "future-format bytes remain unchanged");

        var concurrentPath = Path.Combine(Path.GetDirectoryName(path)!, "concurrent.ini");
        var firstWriter = new QuickFilterHistoryStore(concurrentPath);
        firstWriter.Load();
        firstWriter.Record("*.txt");
        var staleWriter = new QuickFilterHistoryStore(concurrentPath);
        staleWriter.Load();
        File.WriteAllText(concurrentPath, "external edit");
        Throws<IOException>(() => staleWriter.Record("*.pdf"), "external edits are not overwritten");
        Require(File.ReadAllText(concurrentPath) == "external edit" && staleWriter.Load().SequenceEqual(["*.txt"]),
            "external history edits remain unchanged and a failed write is rolled back in memory");

        Require(!QuickFilterHistoryKeyRouting.ShouldRemoveSelectedEntry(0x2E, dropDownOpen: false, hasSelection: true) &&
                !QuickFilterHistoryKeyRouting.ShouldRemoveSelectedEntry(0x2E, dropDownOpen: true, hasSelection: false) &&
                QuickFilterHistoryKeyRouting.ShouldRemoveSelectedEntry(0x2E, dropDownOpen: true, hasSelection: true),
            "Delete is reserved for a selected entry only while autocomplete is open");
        Console.WriteLine("Quick filter history smoke: PASS");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Quick filter history: " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Quick filter history: " + message);
    }
}
