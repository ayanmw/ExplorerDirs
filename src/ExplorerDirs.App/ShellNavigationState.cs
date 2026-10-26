namespace ExplorerDirs.App;

// A tab saves its filesystem or supported Shell folder identity even while its
// view shows virtual search results. Search URIs remain runtime-only metadata.
internal sealed class ShellNavigationState
{
    public string DirectoryPath { get; private set; }
    public bool IsFileSystemLocation { get; private set; }
    public bool CanSearchCurrentLocation => ShellLocationIdentity.TryNormalize(DirectoryPath, out _, out _);
    public string? SearchUri { get; private set; }
    public string? SearchQuery { get; private set; }
    public bool IsSearchActive => SearchUri is not null;

    internal ShellNavigationState(string initialLocation)
    {
        DirectoryPath = initialLocation;
        IsFileSystemLocation = ShellLocationIdentity.TryNormalize(initialLocation, out _, out var isFileSystem) &&
            isFileSystem;
    }

    public void CompleteDirectory(string path, bool isFileSystemLocation = true)
    {
        DirectoryPath = path;
        IsFileSystemLocation = isFileSystemLocation;
        SearchUri = null;
        SearchQuery = null;
    }

    public bool TryCompleteSearch(string uri)
    {
        if (!ShellSearchLocation.TryReadScopeAndQuery(uri, out var scope, out var query) ||
            string.IsNullOrWhiteSpace(query)) return false;
        DirectoryPath = scope;
        IsFileSystemLocation = ShellLocationIdentity.TryNormalize(scope, out _, out var isFileSystemPath) &&
            isFileSystemPath;
        SearchUri = uri;
        SearchQuery = query;
        return true;
    }
}
