using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class StatusBarVisibilitySmoke
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "StatusBar.ini");
        var store = new StatusBarVisibilityStore(path);
        Require(store.Load() && store.LoadWarning is null,
            "a missing status-bar preference keeps the bar visible by default");
        store.Save(false);
        Require(!new StatusBarVisibilityStore(path).Load(),
            "status-bar visibility round-trips through its portable INI");

        var externalEdit = File.ReadAllText(path) + "; external change\n";
        File.WriteAllText(path, externalEdit);
        var conflict = false;
        try { store.Save(true); }
        catch (IOException) { conflict = true; }
        Require(conflict && File.ReadAllText(path) == externalEdit,
            "an external status-bar preference edit is preserved");

        File.WriteAllText(path, "; test future format protection\n[StatusBar]\nFormatVersion=2\nShow=false\n");
        var future = new StatusBarVisibilityStore(path);
        Require(future.Load() && future.LoadWarning is not null,
            "a future status-bar preference format uses the visible default and warns");
        var futureConflict = false;
        try { future.Save(false); }
        catch (IOException) { futureConflict = true; }
        Require(futureConflict && File.ReadAllText(path).Contains("FormatVersion=2", StringComparison.Ordinal),
            "future status-bar preference data is not overwritten");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"Status bar preference smoke failed: {message}");
    }
}
