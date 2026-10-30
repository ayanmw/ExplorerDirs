namespace ExplorerDirs.App;

internal readonly record struct ShellTabSelectionCaptureToken(
    string Location, long NavigationVersion, long ViewGeneration, long SelectionVersion)
{
    internal bool Matches(string currentLocation, string historyLocation, long navigationVersion,
        long viewGeneration, long selectionVersion) =>
        NavigationVersion == navigationVersion &&
        ViewGeneration == viewGeneration &&
        SelectionVersion == selectionVersion &&
        string.Equals(Location, currentLocation, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Location, historyLocation, StringComparison.OrdinalIgnoreCase);
}
