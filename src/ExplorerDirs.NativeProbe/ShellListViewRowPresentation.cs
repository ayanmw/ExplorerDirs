using ExplorerDirs.App;
using System.Globalization;

namespace ExplorerDirs.NativeProbe;

internal static class ShellListViewRowPresentation
{
    internal static string FormatRecursiveSize(ShellListViewRow row, SizeDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(options);
        return row.RecursiveBytes is { } bytes ? ByteSizeFormatter.Format(bytes, options) : string.Empty;
    }

    internal static string FormatRecursiveContents(ShellListViewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.RecursiveSummary is { } summary
            ? $"files: {summary.Files}, folders: {summary.Directories}"
            : string.Empty;
    }

    internal static string FormatRelativeSize(ShellListViewRow row, ulong directoryBytes)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.RecursiveBytes is not { } bytes) return string.Empty;
        var percentage = RecursiveSizePercentage.Calculate(bytes, directoryBytes);
        return percentage is { } value ? value.ToString("G", CultureInfo.InvariantCulture) + "%" : string.Empty;
    }
}
