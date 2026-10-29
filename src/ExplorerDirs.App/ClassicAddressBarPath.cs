namespace ExplorerDirs.App;

internal static class ClassicAddressBarPath
{
    internal static string Format(string path, bool isFileSystemLocation)
    {
        if (!isFileSystemLocation || path.Length == 0 || path[^1] is '\\' or '/') return path;
        return path + '\\';
    }
}
