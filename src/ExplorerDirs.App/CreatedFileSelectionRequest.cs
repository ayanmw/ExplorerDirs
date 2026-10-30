namespace ExplorerDirs.App;

internal sealed record CreatedFileSelectionRequest(object Browser, string Directory,
    long NavigationVersion, long Generation, long SelectionVersion, long InputVersion,
    bool SupportsVirtualLocation = false, bool SupportsSearchLocation = false)
{
    internal bool IsCurrent(object? browser, string directory, bool isFileSystemLocation,
        bool isSearchActive, bool navigationPending, long navigationVersion, long generation,
        long selectionVersion, long inputVersion) =>
        IsLocationCurrent(browser, directory, isFileSystemLocation, isSearchActive, navigationPending,
            navigationVersion, generation) &&
        SelectionVersion == selectionVersion && InputVersion == inputVersion;

    internal bool IsLocationCurrent(object? browser, string directory, bool isFileSystemLocation,
        bool isSearchActive, bool navigationPending, long navigationVersion, long generation) =>
        ReferenceEquals(Browser, browser) &&
        string.Equals(Directory, directory, StringComparison.OrdinalIgnoreCase) &&
        (isSearchActive ? SupportsSearchLocation : isFileSystemLocation || SupportsVirtualLocation) &&
        !navigationPending &&
        NavigationVersion == navigationVersion && Generation == generation;
}
