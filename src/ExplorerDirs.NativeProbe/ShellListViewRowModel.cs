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
        long nextId,
        IReadOnlyDictionary<string, string>? previousParsingNameByRenamedParsingName = null)
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
            ShellListViewRow? previous = null;
            var matchedPrevious = TryTakePrevious(enumerated.ParsingName, out previous);
            var matchedByRenameMapping = false;
            if (!matchedPrevious && previousParsingNameByRenamedParsingName is not null &&
                previousParsingNameByRenamedParsingName.TryGetValue(enumerated.ParsingName, out var previousParsingName))
            {
                matchedPrevious = TryTakePrevious(previousParsingName, out previous);
                matchedByRenameMapping = matchedPrevious;
            }

            if (matchedPrevious && previous is not null)
            {
                var filesystemFolder = IsFilesystemFolder(previous) && IsFilesystemFolder(enumerated);
                var currentFileSystemIdentity = enumerated.FileSystemIdentity;
                if (filesystemFolder && previous.RecursiveSummary is not null && currentFileSystemIdentity is null)
                    currentFileSystemIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(enumerated.FileSystemPath!);
                if (matchedByRenameMapping && filesystemFolder && previous.FileSystemIdentity is { } oldIdentity &&
                    currentFileSystemIdentity is { } newIdentity && oldIdentity != newIdentity)
                {
                    // A delayed rename mapping must not transfer selection identity to a replacement at the new path.
                    enumerated = enumerated with { FileSystemIdentity = currentFileSystemIdentity };
                    matchedPrevious = false;
                    previous = null;
                }
                if (matchedPrevious && previous is not null)
                {
                    // Renaming changes a directory's path, not its identity; keep its measured summary when the file ID still matches.
                    var preserveRecursiveScan = filesystemFolder && previous.FileSystemIdentity is { } previousIdentity &&
                        currentFileSystemIdentity is { } currentIdentity && previousIdentity == currentIdentity;
                    rows[index] = enumerated with
                    {
                        Id = previous.Id,
                        RecursiveBytes = preserveRecursiveScan ? previous.RecursiveBytes : enumerated.RecursiveBytes,
                        RecursiveSummary = preserveRecursiveScan ? previous.RecursiveSummary : enumerated.RecursiveSummary,
                        FileSystemIdentity = currentFileSystemIdentity
                    };
                    continue;
                }
            }

            rows[index] = enumerated with { Id = nextId++ };
        }

        // Every previous PIDL belongs to the previous snapshot; return all of them
        // so the caller can release them after it has transferred the new snapshot.
        return new ShellListViewRowReconciliation(rows, previousRows.ToArray(), nextId);

        bool TryTakePrevious(string parsingName, out ShellListViewRow? row)
        {
            row = null;
            if (!previousByParsingName.TryGetValue(parsingName, out var matches) || !matches.TryDequeue(out var found))
                return false;
            row = found;
            return true;
        }
    }

    private static bool IsFilesystemFolder(ShellListViewRow row) =>
        row.FileSystemPath is not null && (row.Attributes & SfgaoFolder) != 0;

    internal static IReadOnlyList<ShellListViewRow> ApplyRecursiveScanResults(
        IReadOnlyList<ShellListViewRow> rows,
        IReadOnlyDictionary<string, ShellListViewRowScanResult> results)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(results);
        var updated = new ShellListViewRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (IsFilesystemFolder(row))
            {
                if (results.TryGetValue(Path.GetFullPath(row.FileSystemPath!), out var scanResult))
                    row = ApplyRecursiveScanResult(row, scanResult);
                else
                    // A completed scan without this folder must not leave an older value looking current.
                    row = row with { RecursiveBytes = null, RecursiveSummary = null, FileSystemIdentity = null };
            }
            updated[index] = row;
        }
        return updated;
    }

    internal static IReadOnlyList<ShellListViewRow> ApplyRecursiveScanResult(
        IReadOnlyList<ShellListViewRow> rows,
        string path,
        ShellListViewRowScanResult result)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(result);
        var fullPath = Path.GetFullPath(path);
        var updated = new ShellListViewRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (IsFilesystemFolder(row) &&
                string.Equals(Path.GetFullPath(row.FileSystemPath!), fullPath, StringComparison.OrdinalIgnoreCase))
                row = ApplyRecursiveScanResult(row, result);
            updated[index] = row;
        }
        return updated;
    }

    private static ShellListViewRow ApplyRecursiveScanResult(
        ShellListViewRow row,
        ShellListViewRowScanResult scanResult)
    {
        var currentIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(row.FileSystemPath!);
        if (row.FileSystemIdentity is not { } previousIdentity ||
            previousIdentity != scanResult.Identity || currentIdentity != scanResult.Identity)
        {
            // The handle scan, row snapshot, and current path must all identify the same folder.
            return row with
            {
                RecursiveBytes = null,
                RecursiveSummary = null,
                FileSystemIdentity = currentIdentity
            };
        }
        return row with
        {
            RecursiveBytes = scanResult.Summary.Bytes,
            RecursiveSummary = scanResult.Summary,
            FileSystemIdentity = currentIdentity ?? row.FileSystemIdentity
        };
    }

    internal static IReadOnlyList<ShellListViewRowRecursiveSizeSnapshot> ProjectRecursiveSizeSnapshots(
        IReadOnlyList<ShellListViewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var snapshots = new List<ShellListViewRowRecursiveSizeSnapshot>();
        foreach (var row in rows)
        {
            if (!IsFilesystemFolder(row) || row.RecursiveSummary is not { } summary ||
                row.RecursiveBytes != summary.Bytes || row.FileSystemIdentity is not { } identity ||
                ShellListViewRowIdentity.GetFileSystemIdentity(row.FileSystemPath!) != identity)
                continue;

            snapshots.Add(new ShellListViewRowRecursiveSizeSnapshot(row.ParsingName, identity, summary));
        }
        return snapshots;
    }
}

internal sealed record ShellListViewRowRecursiveSizeSnapshot(
    string ParsingName,
    ShellFileSystemIdentity Identity,
    RecursiveDirectoryScanResult Summary);

internal sealed record ShellListViewRowReconciliation(
    IReadOnlyList<ShellListViewRow> Rows,
    IReadOnlyList<ShellListViewRow> RetiredRows,
    long NextId);
