using System.IO;

namespace ExplorerDirs.App;

internal sealed record SpaceAnalysisEntry(string Path, string Name, bool IsDirectory, ulong? Bytes,
    string Source, string? Detail = null)
{
    public string DisplaySize => Bytes is { } bytes ? ByteSizeFormatter.Format(bytes) : "不可用";
    public string DisplaySource => Source;
    public string DisplayName => Name;
    public string? DisplayDetail => Detail;
    public decimal Percentage { get; init; }
    public string DisplayPercentage => Bytes.HasValue ? $"{Percentage:0.##}%" : "—";
}

internal sealed record SpaceAnalysisSnapshot(string Path, IReadOnlyList<SpaceAnalysisEntry> Entries, decimal KnownBytes)
{
    internal bool IsPartial => Entries.Any(item => item.Bytes is null || item.Detail is not null);

    internal IReadOnlyList<(SpaceAnalysisEntry? Entry, decimal Weight)> CreateMapItems()
    {
        var shown = new List<(SpaceAnalysisEntry? Entry, decimal Weight)>(151);
        // ReadAsync stores descending sizes and the complete known total. Resizing the
        // map must not enumerate a large snapshot again just to sum the hidden tail.
        var otherBytes = KnownBytes;
        foreach (var entry in Entries)
        {
            if (entry.Bytes is not > 0) break;
            shown.Add((entry, entry.Bytes.Value));
            otherBytes -= entry.Bytes.Value;
            if (shown.Count == 150) break;
        }
        if (otherBytes > 0) shown.Add((null, otherBytes));
        return shown;
    }

    internal static async Task<SpaceAnalysisSnapshot> ReadAsync(string directory, FolderSizeSource source,
        CancellationToken cancellationToken, IProgress<KeyValuePair<string, FolderSizeValue>>? progress = null,
        FolderSizeDataSource? dataSource = null)
    {
        var entries = await Task.Run(() =>
        {
            var result = new List<SpaceAnalysisEntry>();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(path);
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    var reparse = (attributes & FileAttributes.ReparsePoint) != 0;
                    result.Add(new(path, System.IO.Path.GetFileName(path), isDirectory,
                        reparse || isDirectory ? null : checked((ulong)new FileInfo(path).Length),
                        reparse ? "跳过链接" : isDirectory ? "等待" : "文件系统",
                        reparse ? "不遍历重解析点，避免重复统计或进入链接目标。" : null));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { result.Add(new(path, System.IO.Path.GetFileName(path), false, null, "不可用", error.Message)); }
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
        var values = await (dataSource ?? new FolderSizeDataSource()).ResolveAsync(directory,
            entries.Where(item => item.IsDirectory && item.Detail is null).Select(item => item.Path), source, cancellationToken,
            progress).ConfigureAwait(false);
        var completed = entries.Select(item => values.TryGetValue(item.Path, out var value)
            ? item with { Bytes = value.Bytes, Source = value.Source == FolderSizeSource.Everything ? "Everything" : "内置扫描", Detail = value.Detail }
            : item).ToArray();
        var total = completed.Sum(item => (decimal)(item.Bytes ?? 0));
        return new(directory, completed.Select(item => item with { Percentage = total > 0 && item.Bytes is { } bytes ? 100m * bytes / total : 0 })
            .OrderByDescending(item => item.Bytes).ToArray(), total);
    }
}
