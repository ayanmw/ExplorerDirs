using System.IO;

namespace ExplorerDirs.App;

internal static class ShellMiddleClickTarget
{
    internal static bool TryGetChildPath(string parentPath, string? itemName, out string childPath)
    {
        childPath = string.Empty;
        if (!Path.IsPathFullyQualified(parentPath) || string.IsNullOrWhiteSpace(itemName) ||
            itemName is "." or ".." || Path.IsPathRooted(itemName) ||
            itemName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(itemName), itemName, StringComparison.Ordinal)) return false;
        childPath = Path.GetFullPath(Path.Combine(parentPath, itemName));
        return true;
    }

    internal static bool IsResolvedChild(string parentPath, string? itemName, string resolvedPath)
    {
        if (!TryGetChildPath(parentPath, itemName, out _) || !Path.IsPathFullyQualified(resolvedPath)) return false;
        try
        {
            var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
            var fullResolved = Path.GetFullPath(resolvedPath);
            var resolvedParent = Path.GetDirectoryName(fullResolved);
            return resolvedParent is not null &&
                   string.Equals(Path.GetFileName(fullResolved), itemName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Path.TrimEndingDirectorySeparator(resolvedParent), fullParent,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static bool IsCurrentRequest(string capturedPath, long capturedNavigationVersion,
        long capturedInputVersion, nint capturedViewWindow, string currentPath,
        long currentNavigationVersion, long currentInputVersion, nint currentViewWindow,
        bool browserAvailable, bool searchActive) =>
        browserAvailable && !searchActive && capturedViewWindow != 0 &&
        capturedViewWindow == currentViewWindow &&
        capturedNavigationVersion == currentNavigationVersion &&
        capturedInputVersion == currentInputVersion &&
        string.Equals(capturedPath, currentPath, StringComparison.OrdinalIgnoreCase);
}
