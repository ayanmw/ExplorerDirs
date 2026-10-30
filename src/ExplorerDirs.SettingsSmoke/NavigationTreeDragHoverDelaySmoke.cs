using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class NavigationTreeDragHoverDelaySmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.TreeDragHover." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "NavigationTreeDragHoverDelay.ini");
            var store = new NavigationTreeDragHoverDelayStore(path);
            Check(store.Load() == NavigationTreeDragHoverDelay.DefaultMilliseconds,
                "missing hover-expand delay defaults to one second");
            foreach (var milliseconds in NavigationTreeDragHoverDelay.AllowedMilliseconds)
            {
                store.Save(milliseconds);
                Check(new NavigationTreeDragHoverDelayStore(path).Load() == milliseconds,
                    $"hover-expand delay {milliseconds} ms persists through portable INI");
            }

            try { store.Save(900); throw new Exception("unsupported hover-expand delay was accepted"); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS unsupported hover-expand delay is rejected"); }

            File.WriteAllText(path, "[NavigationTree]\nFormatVersion=2\nDragHoverExpandDelayMilliseconds=500\n");
            var future = new NavigationTreeDragHoverDelayStore(path);
            Check(future.Load() == NavigationTreeDragHoverDelay.DefaultMilliseconds && future.LoadWarning is not null,
                "future hover-expand delay format falls back with a warning");
            try { future.Save(500); throw new Exception("future hover-expand delay format was overwritten"); }
            catch (IOException) { Console.WriteLine("PASS future hover-expand delay format is protected"); }
            Check(File.ReadAllText(path).Contains("FormatVersion=2", StringComparison.Ordinal),
                "future hover-expand delay source remains unchanged");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS " + message);
    }
}
