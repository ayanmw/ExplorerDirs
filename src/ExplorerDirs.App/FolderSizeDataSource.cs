using System.IO;

namespace ExplorerDirs.App;

internal enum FolderSizeSource { Auto, Everything, BuiltIn }
internal sealed record FolderSizeValue(ulong? Bytes, FolderSizeSource Source, long? Unreadable = null, string? Detail = null);

// Shared by list consumers and explicit space analysis. Consumers retain their bounded
// snapshots; Everything retains its own index. This layer does not duplicate either cache.
internal sealed class FolderSizeDataSource
{
    private static readonly SemaphoreSlim ScanGate = new(2, 2);
    private readonly Func<string, uint, CancellationToken, Task<IndexedSizePage>> _query;
    private readonly Func<string, CancellationToken, RecursiveDirectoryScanResult> _scan;

    internal FolderSizeDataSource(
        Func<string, uint, CancellationToken, Task<IndexedSizePage>>? query = null,
        Func<string, CancellationToken, RecursiveDirectoryScanResult>? scan = null)
    {
        _query = query ?? EverythingSizeClient.QueryChildrenAsync;
        _scan = scan ?? ((path, cancellation) => RecursiveDirectoryScanner.Scan(path, cancellation));
    }

    internal async Task<IReadOnlyDictionary<string, FolderSizeValue>> ResolveAsync(
        string parent, IEnumerable<string> directories, FolderSizeSource source, CancellationToken cancellationToken,
        IProgress<KeyValuePair<string, FolderSizeValue>>? progress = null)
    {
        var requested = directories.Select(Path.GetFullPath).Select(Path.TrimEndingDirectorySeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Any(path => !FavoritesStore.SamePath(Path.GetDirectoryName(path) ?? "", parent)))
            throw new ArgumentException("大小批量查询只能包含同一目录的直接子目录。", nameof(directories));
        var requestedSet = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, FolderSizeValue>(StringComparer.OrdinalIgnoreCase);
        string? unavailable = null;
        if (source != FolderSizeSource.BuiltIn && requested.Length > 0)
        {
            uint offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await _query(parent, offset, cancellationToken).ConfigureAwait(false);
                if (page.Status != EverythingSizeStatus.Available)
                { unavailable = page.Detail ?? $"Everything: {page.Status}"; break; }
                foreach (var item in page.Items)
                {
                    var path = Path.TrimEndingDirectorySeparator(item.Path);
                    if (item.IsDirectory && item.Bytes is { } bytes && requestedSet.Contains(path))
                    {
                        result[path] = new(bytes, FolderSizeSource.Everything);
                        progress?.Report(new(path, result[path]));
                    }
                }
                if (result.Count == requested.Length || page.Items.Count == 0) break;
                offset = checked(offset + (uint)page.Items.Count);
                if (offset >= page.Total) break;
            }
        }
        var pending = requested.Where(path => !result.ContainsKey(path)).ToArray();
        if (source == FolderSizeSource.Everything)
        {
            foreach (var path in pending)
            {
                result[path] = new(null, source, Detail: unavailable ?? "Everything 没有该目录的大小索引。");
                progress?.Report(new(path, result[path]));
            }
        }
        else
        {
            // A bounded number of workers avoids one task/thread per directory in large lists.
            var cursor = -1;
            async Task ScanWorker()
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref cursor);
                    if (index >= pending.Length) return;
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = pending[index];
                    FolderSizeValue value;
                    await ScanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var scan = await Task.Run(() => _scan(path, cancellationToken), cancellationToken).ConfigureAwait(false);
                        value = new(scan.Bytes, FolderSizeSource.BuiltIn, scan.Unreadable,
                            scan.Unreadable > 0 ? "包含不可读或跳过项目，大小为可访问部分。" : null);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { value = new(null, FolderSizeSource.BuiltIn, Detail: error.Message); }
                    finally { ScanGate.Release(); }
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (result) result[path] = value;
                    progress?.Report(new(path, value));
                }
            }
            await Task.WhenAll(ScanWorker(), ScanWorker()).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
