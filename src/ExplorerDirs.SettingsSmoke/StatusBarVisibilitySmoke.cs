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

        var selectionSizePath = Path.Combine(root, "StatusBarSelectionSize.ini");
        var selectionSize = new StatusBarSelectionSizeStore(selectionSizePath);
        Require(selectionSize.Load() && selectionSize.LoadWarning is null,
            "status-bar selected-size display keeps the current default enabled");
        selectionSize.Save(false);
        Require(!new StatusBarSelectionSizeStore(selectionSizePath).Load(),
            "status-bar selected-size display round-trips through its own portable INI");
        Require(File.ReadAllText(path).Contains("Show=false", StringComparison.Ordinal),
            "status-bar content preference is stored separately from bar visibility");

        var checkBoxesPath = Path.Combine(root, "SelectionCheckBoxes.ini");
        var checkBoxes = new SelectionCheckBoxesStore(checkBoxesPath);
        Require(!checkBoxes.Load() && checkBoxes.LoadWarning is null,
            "Q-Dir-style item checkboxes default to disabled");
        checkBoxes.Save(true);
        Require(new SelectionCheckBoxesStore(checkBoxesPath).Load(),
            "item checkbox preference round-trips through its portable INI");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"Status bar preference smoke failed: {message}");
    }
}
