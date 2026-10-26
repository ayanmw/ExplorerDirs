using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class TreeSelectionAutoExpandSmoke
{
    internal static void Run()
    {
        Check(TreeSelectionAutoExpand.ShouldSync(enabled: true, currentPane: true, treeVisible: true,
                  selectedCount: 1, selectedFolder: true),
            "one selected folder in a visible current pane syncs to its tree");
        Check(!TreeSelectionAutoExpand.ShouldSync(enabled: true, currentPane: false, treeVisible: true,
                  selectedCount: 1, selectedFolder: true) &&
              !TreeSelectionAutoExpand.ShouldSync(enabled: true, currentPane: true, treeVisible: false,
                  selectedCount: 1, selectedFolder: true) &&
              !TreeSelectionAutoExpand.ShouldSync(enabled: true, currentPane: true, treeVisible: true,
                  selectedCount: 2, selectedFolder: true) &&
              !TreeSelectionAutoExpand.ShouldSync(enabled: true, currentPane: true, treeVisible: true,
                  selectedCount: 1, selectedFolder: false) &&
              !TreeSelectionAutoExpand.ShouldSync(enabled: false, currentPane: true, treeVisible: true,
                  selectedCount: 1, selectedFolder: true),
            "hidden panes, multi-selection, non-folders, and disabled preference do not sync");

        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.TreeSelection." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "NavigationTreeSelection.ini");
            var preference = new PortableBooleanPreference(path, "NavigationTree", "SelectFromListAndExpand",
                "目录树自动展开", defaultValue: true);
            Check(preference.Load(), "missing selection-sync preference defaults to enabled");
            preference.Save(false);
            Check(!new PortableBooleanPreference(path, "NavigationTree", "SelectFromListAndExpand",
                    "目录树自动展开", defaultValue: true).Load(),
                "selection-sync preference persists independently in portable INI");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS " + message);
    }
}
