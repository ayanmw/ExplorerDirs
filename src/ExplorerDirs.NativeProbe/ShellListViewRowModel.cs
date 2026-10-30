using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.NativeProbe;

// Apply asynchronous filesystem scans without replacing Shell row identities or virtual items.
internal static class ShellListViewRowModel
{
    internal static IReadOnlyList<ShellListViewRow> ApplyRecursiveScanResults(
        IReadOnlyList<ShellListViewRow> rows,
        IReadOnlyDictionary<string, RecursiveDirectoryScanResult> results)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(results);
        var updated = new ShellListViewRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.FileSystemPath is { } path &&
                results.TryGetValue(Path.GetFullPath(path), out var summary))
            {
                row = row with { RecursiveBytes = summary.Bytes, RecursiveSummary = summary };
            }
            updated[index] = row;
        }
        return updated;
    }
}
