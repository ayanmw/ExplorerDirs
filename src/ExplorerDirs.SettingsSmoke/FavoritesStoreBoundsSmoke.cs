using System.IO;
using ExplorerDirs.App;

internal static class FavoritesStoreBoundsSmoke
{
    internal static void Run()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "favorites-bounds-" + Guid.NewGuid().ToString("N") + ".ini");
        var content = "[Favorites]\nFormatVersion=1\nCount=2147483647\nItem0=C:\\retained\n";
        File.WriteAllText(path, content);
        var store = new FavoritesStore(path);
        var reading = Task.Run(store.Load);
        // A damaged count must not tie up startup with billions of missing-index checks.
        if (!reading.Wait(TimeSpan.FromSeconds(2)))
            throw new Exception("Favorites recovery exceeded the bounded item budget for a damaged count.");
        if (reading.Result.Count != 1 || reading.Result[0] != @"C:\retained" || store.LoadWarning is null ||
            !Directory.GetFiles(AppContext.BaseDirectory, Path.GetFileName(path) + ".invalid-*.bak")
                .Any(backup => File.ReadAllText(backup) == content))
            throw new Exception("Damaged favorite count lost recovered items or the original backup.");
        File.Delete(path);
        foreach (var backup in Directory.GetFiles(AppContext.BaseDirectory, Path.GetFileName(path) + ".invalid-*.bak"))
            File.Delete(backup);
    }
}
