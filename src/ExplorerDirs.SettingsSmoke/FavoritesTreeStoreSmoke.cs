using ExplorerDirs.App;
using System.IO;

internal static class FavoritesTreeStoreSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs-favorites-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "favorites.ini");
            var store = new FavoritesStore(path); store.Load();
            store.SaveEntries([new(@"C:\example", "旧名称")]);
            var previous = File.ReadAllText(path);
            store.SaveEntries([new("shell:Downloads", "下载", "工作"), new(@"C:\example", "目录")], ["空分组", "工作"]);
            Check(Directory.GetFiles(directory, "favorites.ini.v2-*.bak").Any(backup => File.ReadAllText(backup) == previous), "v2 upgrade keeps a recoverable snapshot");
            var restarted = new FavoritesStore(path); var entries = restarted.Load();
            Check(restarted.LoadWarning is null && entries.Count == 2 && restarted.Folders.SequenceEqual(new[] { "空分组", "工作" }), "empty groups and group order survive restart");
            restarted.Save(entries.Reverse());
            Check(new FavoritesStore(path).Load().SequenceEqual(entries.Reverse()), "ordinary save retains leaf order");
            var afterSave = new FavoritesStore(path); afterSave.Load();
            Check(afterSave.Folders.SequenceEqual(new[] { "空分组", "工作" }), "ordinary add/delete saves retain explicit groups");
            afterSave.SaveEntries([new("shell:Downloads", "下载", "重命名")], ["重命名", "新组"]);
            var renamed = new FavoritesStore(path); renamed.Load();
            Check(renamed.Folders.SequenceEqual(new[] { "重命名", "新组" }) && renamed.Describe("shell:Downloads").Group == "重命名", "rename/delete/reorder persisted groups and entries together");
            File.WriteAllText(path, "[Favorites]\nFormatVersion=3\nCount=0\nFolderCount=2000000000\n");
            var broken = new FavoritesStore(path); broken.Load();
            Check(broken.LoadWarning is not null, "oversized group count is rejected without iteration");
            File.WriteAllText(path, "[Favorites]\nFormatVersion=4\nCount=0\n");
            var future = new FavoritesStore(path); future.Load();
            try { future.SaveEntries([]); throw new InvalidOperationException("Future format was overwritten."); }
            catch (IOException) { }
            Console.WriteLine("Favorites tree persistence: PASS (empty groups, reorder, rename, malformed and future-format protection; no UI).");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); }
}
