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

        var folderSizePath = Path.Combine(root, "StatusBarFolderSize.ini");
        var folderSize = new StatusBarFolderSizeWhenEmptyStore(folderSizePath);
        Require(!folderSize.Load() && folderSize.LoadWarning is null,
            "current-folder size without a selection defaults to disabled");
        folderSize.Save(true);
        Require(new StatusBarFolderSizeWhenEmptyStore(folderSizePath).Load(),
            "current-folder size preference round-trips independently");

        var checkBoxesPath = Path.Combine(root, "SelectionCheckBoxes.ini");
        var checkBoxes = new SelectionCheckBoxesStore(checkBoxesPath);
        Require(!checkBoxes.Load() && checkBoxes.LoadWarning is null,
            "Q-Dir-style item checkboxes default to disabled");
        checkBoxes.Save(true);
        Require(new SelectionCheckBoxesStore(checkBoxesPath).Load(),
            "item checkbox preference round-trips through its portable INI");

        var fullRowPath = Path.Combine(root, "FullRowSelection.ini");
        var fullRow = new FullRowSelectionStore(fullRowPath);
        Require(!fullRow.Load() && !fullRow.HasExplicitValue && fullRow.LoadWarning is null,
            "missing full-row selection preference follows the Shell default without an explicit override");
        fullRow.Save(true);
        var enabledFullRow = new FullRowSelectionStore(fullRowPath);
        Require(enabledFullRow.Load() && enabledFullRow.HasExplicitValue,
            "full-row selection preference explicitly enables and round-trips");
        enabledFullRow.Save(false);
        var disabledFullRow = new FullRowSelectionStore(fullRowPath);
        Require(!disabledFullRow.Load() && disabledFullRow.HasExplicitValue,
            "full-row selection preference explicitly disables and round-trips");

        File.WriteAllText(fullRowPath, "[FullRowSelection]\nFormatVersion=2\nFullRowSelect=true\n");
        var futureFullRow = new FullRowSelectionStore(fullRowPath);
        Require(!futureFullRow.Load() && !futureFullRow.HasExplicitValue && futureFullRow.LoadWarning is not null,
            "future full-row selection formats fall back to the Shell default and warn");
        var futureFullRowConflict = false;
        try { futureFullRow.Save(true); }
        catch (IOException) { futureFullRowConflict = true; }
        Require(futureFullRowConflict && File.ReadAllText(fullRowPath).Contains("FormatVersion=2", StringComparison.Ordinal),
            "future full-row selection preference data is not overwritten");

        File.WriteAllText(fullRowPath, "[FullRowSelection]\nFormatVersion=1\nFullRowSelect=invalid\n");
        var invalidFullRow = new FullRowSelectionStore(fullRowPath);
        Require(!invalidFullRow.Load() && !invalidFullRow.HasExplicitValue && invalidFullRow.LoadWarning is not null &&
                Directory.GetFiles(root, "FullRowSelection.ini.invalid-*.bak").Length == 1,
            "invalid full-row selection settings are backed up and return to following Shell defaults");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"Status bar preference smoke failed: {message}");
    }
}
