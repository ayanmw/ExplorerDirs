using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.NativeProbe;

// Apply asynchronous filesystem scans without replacing Shell row identities or virtual items.
internal static class ShellListViewRowModel
{
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
                rows[index] = enumerated with
                {
                    Id = previous.Id,
                    RecursiveBytes = previous.RecursiveBytes,
                    RecursiveSummary = previous.RecursiveSummary
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

internal sealed record ShellListViewRowReconciliation(
    IReadOnlyList<ShellListViewRow> Rows,
    IReadOnlyList<ShellListViewRow> RetiredRows,
    long NextId);
