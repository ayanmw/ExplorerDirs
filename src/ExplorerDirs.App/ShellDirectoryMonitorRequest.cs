namespace ExplorerDirs.App;

// A Shell count read may pump callbacks and reenter navigation. Keep the
// captured monitor identity together so its result cannot replace newer state.
internal sealed class ShellDirectoryMonitorRequest(long monitorVersion, long navigationVersion,
    string path, ShellDirectoryChangeQueue queue)
{
    internal long NavigationVersion { get; } = navigationVersion;

    internal bool IsCurrent(long currentMonitorVersion, long currentNavigationVersion, bool viewIsCurrent,
        string? completedPath, ShellDirectoryChangeQueue? currentQueue) =>
        monitorVersion == currentMonitorVersion && NavigationVersion == currentNavigationVersion && viewIsCurrent &&
        string.Equals(path, completedPath, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(queue, currentQueue);
}

internal static class ShellDirectoryMonitorCountRead
{
    // Reading IFolderView can pump callbacks. Validate only after the COM call so a
    // reentrant navigation cannot publish the old view's count into a newer monitor.
    internal static bool TryRead(Func<int?> readCount, Func<bool> requestIsCurrent, out int? count)
    {
        count = readCount();
        return requestIsCurrent();
    }
}
