using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class NavigationTreeWidthsSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.TreeWidths." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "NavigationTreeWidths.ini");
            var defaults = NavigationTreeWidths.Defaults;
            Check(defaults.Shared == 190 &&
                Enumerable.Range(0, 4).All(index => defaults.GetPaneWidth(index) == 160),
                "shared and per-pane tree widths have documented defaults");

            var store = new NavigationTreeWidthsStore(path);
            Check(store.Load() == defaults, "missing width settings use defaults");
            var configured = defaults with { Shared = 240, Pane0 = 140, Pane1 = 180, Pane2 = 220, Pane3 = 300 };
            store.Save(configured);
            Check(new NavigationTreeWidthsStore(path).Load() == configured,
                "shared and four independent pane widths round-trip");
            Check(!configured.WithPaneWidth(2, 95).IsValid &&
                  !configured.WithPaneWidth(1, 601).IsValid,
                "tree width validation rejects values outside 96 to 600 DIP");
            try
            {
                store.Save(configured with { Shared = 601 });
                throw new Exception("invalid tree width was saved");
            }
            catch (ArgumentOutOfRangeException) { }
            File.WriteAllText(path, "external edit\n");
            try
            {
                store.Save(configured);
                throw new Exception("concurrent tree width edit was overwritten");
            }
            catch (IOException) { }
            Check(File.ReadAllText(path) == "external edit\n",
                "settings changed by another writer are not overwritten");

            File.WriteAllText(path,
                "[General]\nFormatVersion=1\n[Widths]\nShared=225\nPane0=155\nPane1=invalid\nPane2=190\n");
            var damagedStore = new NavigationTreeWidthsStore(path);
            var recovered = damagedStore.Load();
            Check(recovered == (defaults with { Shared = 225, Pane0 = 155, Pane2 = 190 }) &&
                  damagedStore.LoadWarning is not null &&
                  Directory.GetFiles(root, "NavigationTreeWidths.ini.invalid-*.bak").Length == 1,
                "invalid and missing widths back up the file while retaining valid values and defaulting the rest");

            File.WriteAllText(path,
                "[General]\nFormatVersion=2\n[Widths]\nShared=250\nPane0=160\nPane1=160\nPane2=160\nPane3=160\n");
            var newerStore = new NavigationTreeWidthsStore(path);
            _ = newerStore.Load();
            var newerContents = File.ReadAllText(path);
            try
            {
                newerStore.Save(defaults);
                throw new Exception("newer tree width format was overwritten");
            }
            catch (IOException) { }
            Check(File.ReadAllText(path) == newerContents,
                "newer width format is protected from overwrite");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS " + message);
    }
}
