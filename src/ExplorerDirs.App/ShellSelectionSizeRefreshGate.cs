namespace ExplorerDirs.App;

internal readonly record struct ShellSelectionSizeCacheKey(long ViewGeneration, long SelectionVersion, int SelectedCount);

internal static class ShellSelectionSizeRefreshGate
{
    internal static bool ShouldRead(ShellSelectionSizeCacheKey? cached, ShellSelectionSizeCacheKey current,
        TimeSpan age, TimeSpan refreshInterval) => current.SelectedCount > 0 &&
        (cached != current || age >= refreshInterval);
}
