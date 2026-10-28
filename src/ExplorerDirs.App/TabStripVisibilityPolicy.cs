namespace ExplorerDirs.App;

internal static class TabStripVisibilityPolicy
{
    internal static bool ShouldShow(TabStripVisibilityMode mode, int tabCount) =>
        tabCount > 0 && mode switch
        {
            TabStripVisibilityMode.Always => true,
            TabStripVisibilityMode.Multiple => tabCount > 1,
            TabStripVisibilityMode.Hidden => false,
            _ => false
        };
}
