namespace ExplorerDirs.App;

internal sealed record ShellRefreshRequest(object Browser, long NavigationVersion, string Path)
{
    internal bool IsCurrent(object? browser, long navigationVersion, string? path) =>
        ReferenceEquals(Browser, browser) && NavigationVersion == navigationVersion &&
        string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);
}
