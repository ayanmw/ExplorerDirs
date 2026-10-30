namespace ExplorerDirs.App;

internal static class RecursiveSizePercentage
{
    internal static decimal? Calculate(ulong itemBytes, ulong directoryBytes) =>
        directoryBytes == 0 ? null : 100m * itemBytes / directoryBytes;
}
