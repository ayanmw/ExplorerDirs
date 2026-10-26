using ExplorerDirs.App;
using System.IO;

internal static class QuickFilterFavoritesSmoke
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "quick-filter-favorites", "QuickFilterFavorites.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new QuickFilterFavoritesStore(path);
        var original = new[]
        {
            new QuickFilterFavorite("Text files", new QuickFilterSettings("*.txt;*.md", false, false)),
            new QuickFilterFavorite("Temporary", new QuickFilterSettings("*.tmp=old", true, true))
        };
        store.Save(original);
        var loaded = store.Load();
        Require(loaded.Count == 2 && loaded[0].Name == original[0].Name &&
            loaded[0].Settings.Pattern == "*.txt;*.md" && loaded[1].Settings.Pattern == "*.tmp=old" &&
            loaded[1].Settings.ExcludeMatches && loaded[1].Settings.FilterFolders,
            "names, patterns and modes round-trip");

        store.Save([loaded[0], new QuickFilterFavorite("text files", new QuickFilterSettings("*.csv", true, true))]);
        loaded = store.Load();
        Require(loaded.Count == 1 && loaded[0].Name == "text files" &&
            loaded[0].Settings is { Pattern: "*.csv", ExcludeMatches: true, FilterFolders: true },
            "case-insensitive name save replaces the existing preset");
        store.Save([]);
        Require(store.Load().Count == 0, "deleting the final preset persists an empty store");

        var brokenPath = Path.Combine(Path.GetDirectoryName(path)!, "broken.ini");
        var broken = "[QuickFilterFavorites]\nFormatVersion=1\nCount=2\n" +
            "Name0=Keep me\nPattern0=*.txt\nExclude0=false\nFolders0=false\n";
        File.WriteAllText(brokenPath, broken);
        var recoveredStore = new QuickFilterFavoritesStore(brokenPath);
        Require(recoveredStore.Load().Single().Name == "Keep me" && recoveredStore.LoadWarning is not null &&
            Directory.GetFiles(Path.GetDirectoryName(path)!, "broken.ini.invalid-*.bak").Length == 1,
            "incomplete preset file backs up its source and recovers valid records");

        var futurePath = Path.Combine(Path.GetDirectoryName(path)!, "future.ini");
        const string future = "[QuickFilterFavorites]\nFormatVersion=2\nCount=0\n";
        File.WriteAllText(futurePath, future);
        var futureStore = new QuickFilterFavoritesStore(futurePath);
        futureStore.Load();
        Throws<IOException>(() => futureStore.Save([]), "future preset format protects the file");
        Require(File.ReadAllText(futurePath) == future, "future preset bytes stay unchanged");

        Throws<ArgumentException>(() => store.Save([new QuickFilterFavorite("bad\nname", QuickFilterSettings.Empty)]),
            "newline in preset name is rejected");
        Throws<ArgumentException>(() => store.Save([new QuickFilterFavorite("  ", QuickFilterSettings.Empty)]),
            "empty preset names are rejected");
        Throws<IOException>(() => store.Save(Enumerable.Range(0, 201)
            .Select(i => new QuickFilterFavorite($"preset-{i}", QuickFilterSettings.Empty))),
            "preset count is bounded");

        var concurrentPath = Path.Combine(Path.GetDirectoryName(path)!, "concurrent.ini");
        var concurrent = new QuickFilterFavoritesStore(concurrentPath);
        concurrent.Load();
        concurrent.Save(original);
        var staleWriter = new QuickFilterFavoritesStore(concurrentPath);
        staleWriter.Load();
        File.WriteAllText(concurrentPath, "external edit");
        Throws<IOException>(() => staleWriter.Save([]), "external edits are not overwritten");
        Require(File.ReadAllText(concurrentPath) == "external edit", "external preset edit remains unchanged");
        Console.WriteLine("Quick filter favorites smoke: PASS");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Quick filter favorites: " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Quick filter favorites: " + message);
    }
}
