namespace ExplorerDirs.App;

internal static class TabRecoveryShortcut
{
    internal static void Dispatch(bool allowSelectedFolder, Func<bool> openSelectedFolder, Action reopenClosedTab)
    {
        if (!allowSelectedFolder || !openSelectedFolder()) reopenClosedTab();
    }
}
