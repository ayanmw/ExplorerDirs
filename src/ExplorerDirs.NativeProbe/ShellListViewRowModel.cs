using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.NativeProbe;

// Apply asynchronous filesystem scans without replacing Shell row identities or virtual items.
internal static class ShellListViewRowModel
{
    private const uint SfgaoFolder = 0x20000000;

    internal static ShellListViewRowReconciliation Reconcile(
        IReadOnlyList<ShellListViewRow> previousRows,
        IReadOnlyList<ShellListViewRow> enumeratedRows,
        long nextId)
    {
        ArgumentNullException.ThrowIfNull(previousRows);
        ArgumentNullException.ThrowIfNull(enumeratedRows);
        if (nextId <= 0) throw new ArgumentOutOfRangeException(nameof(nextId));

        var previousByParsingName = new Dictionary<string, Queue<ShellListViewRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in previousRows)
        {
            if (!previousByParsingName.TryGetValue(previous.ParsingName, out var matches))
                previousByParsingName.Add(previous.ParsingName, matches = new Queue<ShellListViewRow>());
            matches.Enqueue(previous);
        }

        var rows = new ShellListViewRow[enumeratedRows.Count];
        for (var index = 0; index < enumeratedRows.Count; index++)
        {
            var enumerated = enumeratedRows[index];
            if (previousByParsingName.TryGetValue(enumerated.ParsingName, out var matches) && matches.TryDequeue(out var previous))
            {
                // A parsing name can survive a file/folder replacement at the same path.
                // Reuse the recursive scan only while both snapshots still identify that folder.
                var preserveRecursiveScan = IsFilesystemFolder(previous) && IsFilesystemFolder(enumerated) &&
                    string.Equals(previous.FileSystemPath, enumerated.FileSystemPath, StringComparison.OrdinalIgnoreCase);
                rows[index] = enumerated with
                {
                    Id = previous.Id,
                    RecursiveBytes = preserveRecursiveScan ? previous.RecursiveBytes : enumerated.RecursiveBytes,
                    RecursiveSummary = preserveRecursiveScan ? previous.RecursiveSummary : enumerated.RecursiveSummary
                };
            }
            else
            {
                rows[index] = enumerated with { Id = nextId++ };
            }
        }

        // Every previous PIDL belongs to the previous snapshot; return all of them
        // so the caller can release them after it has transferred the new snapshot.
        return new ShellListViewRowReconciliation(rows, previousRows.ToArray(), nextId);
    }

    private static bool IsFilesystemFolder(ShellListViewRow row) =>
        row.FileSystemPath is not null && (row.Attributes & SfgaoFolder) != 0;

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
            if (IsFilesystemFolder(row))
            {
                if (results.TryGetValue(Path.GetFullPath(row.FileSystemPath!), out var summary))
                    row = row with { RecursiveBytes = summary.Bytes, RecursiveSummary = summary };
                else
                    // A completed scan without this folder must not leave an older value looking current.
                    row = row with { RecursiveBytes = null, RecursiveSummary = null };
            }
            updated[index] = row;
        }
        return updated;
    }
}

internal sealed record ShellListViewRowReconciliation(
    IReadOnlyList<ShellListViewRow> Rows,
    IReadOnlyList<ShellListViewRow> RetiredRows,
    long NextId);
