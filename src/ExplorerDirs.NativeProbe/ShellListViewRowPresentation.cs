using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

internal static class ShellListViewRowPresentation
{
    internal static string FormatRecursiveSize(ShellListViewRow row, SizeDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(options);
        return row.RecursiveBytes is { } bytes ? ByteSizeFormatter.Format(bytes, options) : string.Empty;
    }
}
