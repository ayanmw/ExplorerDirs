namespace ExplorerDirs.App;

internal static class ShellTreeFolderLocation
{
    internal static bool TryNormalizeSelected(string parsingName, bool folderConfirmed,
        out string identity, out bool isFileSystemPath)
    {
        identity = string.Empty;
        isFileSystemPath = false;
        return folderConfirmed && ShellLocationIdentity.TryNormalize(parsingName, out identity, out isFileSystemPath);
    }
}
