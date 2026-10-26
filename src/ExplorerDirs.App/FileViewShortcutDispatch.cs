namespace ExplorerDirs.App;

internal static class FileViewShortcutDispatch
{
    internal static bool TryOpenSelectionInNewTab(bool hasFileViewFocus, Func<bool> openSelectedFolder)
    {
        return hasFileViewFocus && openSelectedFolder();
    }
}
