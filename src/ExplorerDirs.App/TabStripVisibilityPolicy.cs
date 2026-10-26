namespace ExplorerDirs.App;

internal static class TabStripVisibilityPolicy
{
    internal static bool ShouldShow(bool alwaysShow, int tabCount) =>
        tabCount > 0 && (alwaysShow || tabCount > 1);
}
