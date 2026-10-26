namespace ExplorerDirs.App;

internal sealed class ShellNewFolderRefreshGate
{
    private bool _refreshRequested;

    internal static bool TryFocusForCommand(Func<bool> isCurrent, Func<bool> focusView)
    {
        if (!isCurrent() || !focusView()) return false;
        return isCurrent();
    }

    internal bool TryRequestRefresh(FolderCreationSnapshot observation, string? focusedPath,
        bool operationCurrent, bool userInputUnchanged)
    {
        if (_refreshRequested || !operationCurrent || !userInputUnchanged || observation.Failed ||
            observation.Directories.Count != 1 ||
            string.Equals(observation.Directories[0], focusedPath, StringComparison.OrdinalIgnoreCase))
            return false;

        _refreshRequested = true;
        return true;
    }
}
