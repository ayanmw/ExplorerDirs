using System.IO;

namespace ExplorerDirs.App;

internal static class SelectedPathsClipboardText
{
    internal static bool TryFormat(IReadOnlyList<string> paths, out string text)
    {
        text = string.Empty;
        if (paths.Count == 0 || paths.Any(path => !Path.IsPathFullyQualified(path))) return false;
        text = string.Join(Environment.NewLine, paths);
        return true;
    }
}
