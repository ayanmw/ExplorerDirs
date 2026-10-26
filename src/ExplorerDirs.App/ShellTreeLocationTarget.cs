namespace ExplorerDirs.App;

internal readonly record struct ShellTreeLocationTarget(bool UseVirtualRoot, string? FileSystemPath)
{
    internal static ShellTreeLocationTarget For(bool isFileSystemLocation, string path) =>
        isFileSystemLocation ? new(false, path) : new(true, null);
}
