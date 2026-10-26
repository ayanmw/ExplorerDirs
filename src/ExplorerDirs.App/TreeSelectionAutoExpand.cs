namespace ExplorerDirs.App;

internal static class TreeSelectionAutoExpand
{
    internal static bool ShouldSync(bool enabled, bool currentPane, bool treeVisible,
        int selectedCount, bool selectedFolder) =>
        enabled && currentPane && treeVisible && selectedCount == 1 && selectedFolder;
}
