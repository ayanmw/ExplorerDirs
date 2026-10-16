namespace ExplorerDirs.App;

// A tab saves a real directory even while its Shell view shows virtual search
// results. The search URI is kept only for in-process navigation history.
internal sealed class ShellNavigationState(string initialDirectoryPath)
{
    public string DirectoryPath { get; private set; } = initialDirectoryPath;
    public string? SearchUri { get; private set; }
    public string? SearchQuery { get; private set; }
    public bool IsSearchActive => SearchUri is not null;

    public void CompleteDirectory(string path)
    {
        DirectoryPath = path;
        SearchUri = null;
        SearchQuery = null;
    }

    public bool TryCompleteSearch(string uri)
    {
        if (!ShellSearchLocation.TryReadScopeAndQuery(uri, out var scope, out var query) ||
            string.IsNullOrWhiteSpace(query)) return false;
        DirectoryPath = scope;
        SearchUri = uri;
        SearchQuery = query;
        return true;
    }
}
