namespace ExplorerDirs.App;

internal sealed record ShellRefreshRequest(object Browser, long NavigationVersion, string Path)
{
    internal bool IsCurrent(object? browser, long navigationVersion, string? path) =>
        ReferenceEquals(Browser, browser) && NavigationVersion == navigationVersion &&
        string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);
}

internal static class ShellRefreshPolicy
{
    internal static int Invoke(bool manual, bool softRefresh, Func<bool, int> setNoEnumRefresh,
        Func<int> refresh, Func<bool> isCurrent, Func<bool> currentSoftPreference)
    {
        if (!isCurrent()) return unchecked((int)0x80004004);
        if (!manual || !softRefresh) return refresh();
        // F5 explicitly requests complete enumeration even while automatic updates are soft.
        var prepared = setNoEnumRefresh(false);
        if (prepared < 0) return prepared;
        try { return isCurrent() ? refresh() : unchecked((int)0x80004004); }
        finally
        {
            // COM can reenter navigation or preference changes. Never write to a retired view.
            if (isCurrent())
                System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(setNoEnumRefresh(currentSoftPreference()));
        }
    }
}
