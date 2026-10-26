using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellTreeFolderLocationSmoke
{
    internal static void Run()
    {
        var fileSystemPath = Path.GetTempPath();
        Check(ShellTreeFolderLocation.TryNormalizeSelected(fileSystemPath, folderConfirmed: true,
                  out var normalizedPath, out var isFileSystem) &&
              string.Equals(normalizedPath, Path.GetFullPath(fileSystemPath), StringComparison.OrdinalIgnoreCase) &&
              isFileSystem,
            "a Shell-confirmed filesystem folder keeps its canonical path identity");

        const string virtualFolder = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        Check(ShellTreeFolderLocation.TryNormalizeSelected(virtualFolder, folderConfirmed: true,
                  out var normalizedShellName, out isFileSystem) &&
              normalizedShellName == virtualFolder && !isFileSystem,
            "a Shell-confirmed virtual folder keeps its absolute parsing identity");

        Check(!ShellTreeFolderLocation.TryNormalizeSelected(fileSystemPath, folderConfirmed: false, out _, out _),
            "a non-folder Shell selection cannot become a tree navigation target");
        Check(!ShellTreeFolderLocation.TryNormalizeSelected("https://example.invalid/folder", folderConfirmed: true, out _, out _),
            "a Shell folder's protocol-looking display name is rejected");
        Check(!ShellTreeFolderLocation.TryNormalizeSelected("search-ms:query=folder", folderConfirmed: true, out _, out _),
            "search result identities are not persisted as address tree targets");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
