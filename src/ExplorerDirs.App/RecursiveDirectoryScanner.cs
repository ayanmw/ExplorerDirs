using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.App;

internal sealed record RecursiveDirectoryScanResult(ulong Bytes, long Files, long Directories, long Unreadable);

internal sealed class RecursiveScanProgressThrottle
{
    private readonly long _intervalTicks;
    private long _lastReportTimestamp;
    private bool _hasReported;

    internal RecursiveScanProgressThrottle(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _intervalTicks = (long)Math.Ceiling(interval.TotalSeconds * Stopwatch.Frequency);
    }

    internal bool ShouldReport(long timestamp)
    {
        if (_hasReported && timestamp - _lastReportTimestamp < _intervalTicks) return false;
        _lastReportTimestamp = timestamp;
        _hasReported = true;
        return true;
    }
}

internal static class RecursiveDirectoryScanner
{
    private const int ProgressEntryInterval = 256;

    internal static RecursiveDirectoryScanResult ScanSelection(IEnumerable<string> selectedPaths,
        CancellationToken cancellationToken, Action<long>? progress = null)
    {
        var roots = new List<(string Path, bool IsDirectory)>();
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reparsePathCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long unreadable = 0;
        foreach (var selectedPath in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(selectedPath))
            {
                unreadable++;
                continue;
            }
            try
            {
                var path = Path.GetFullPath(selectedPath);
                var attributes = File.GetAttributes(path);
                if (HasReparsePointInPath(path, attributes, reparsePathCache))
                {
                    unreadable++;
                    continue;
                }
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (uniquePaths.Add(path))
                    roots.Add((path, isDirectory));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                unreadable++;
            }
        }

        // Walk each selected path's ancestors so a parent selection covers descendants without O(n²) comparisons.
        var selectedDirectories = roots.Where(root => root.IsDirectory)
            .Select(root => root.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        roots.RemoveAll(candidate =>
        {
            var ancestor = Path.GetDirectoryName(candidate.Path);
            while (ancestor is not null)
            {
                if (selectedDirectories.Contains(ancestor)) return true;
                ancestor = Path.GetDirectoryName(ancestor);
            }
            return false;
        });

        ulong bytes = 0;
        long files = 0;
        long directories = 0;
        long progressOffset = 0;
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (root.IsDirectory)
            {
                var result = Scan(root.Path, cancellationToken,
                    entriesChecked => progress?.Invoke(checked(progressOffset + entriesChecked)));
                bytes = checked(bytes + result.Bytes);
                files = checked(files + result.Files);
                directories = checked(directories + result.Directories);
                unreadable = checked(unreadable + result.Unreadable);
                progressOffset = checked(progressOffset + result.Files + result.Directories + result.Unreadable);
            }
            else
            {
                try
                {
                    bytes = checked(bytes + (ulong)new FileInfo(root.Path).Length);
                    files++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or OverflowException)
                {
                    unreadable++;
                }
                progressOffset++;
                progress?.Invoke(progressOffset);
            }
        }
        return new(bytes, files, directories, unreadable);
    }

    private static bool HasReparsePointInPath(string path, FileAttributes attributes,
        Dictionary<string, bool> cache)
    {
        var checkedPaths = new List<string>();
        var currentPath = path;
        var currentAttributes = attributes;
        while (true)
        {
            if (cache.TryGetValue(currentPath, out var cachedResult))
            {
                foreach (var checkedPath in checkedPaths) cache[checkedPath] = cachedResult;
                return cachedResult;
            }
            if ((currentAttributes & FileAttributes.ReparsePoint) != 0)
            {
                cache[currentPath] = true;
                foreach (var checkedPath in checkedPaths) cache[checkedPath] = true;
                return true;
            }

            checkedPaths.Add(currentPath);
            var parent = Path.GetDirectoryName(currentPath);
            if (parent is null || string.Equals(parent, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var checkedPath in checkedPaths) cache[checkedPath] = false;
                return false;
            }
            currentPath = parent;
            currentAttributes = File.GetAttributes(currentPath);
        }
    }

    internal static RecursiveDirectoryScanResult Scan(
        string root, CancellationToken cancellationToken, Action<long>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.Directory) == 0)
            throw new DirectoryNotFoundException($"不是文件夹：{root}");
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            return new(0, 0, 0, 1);

        var pending = new Stack<string>();
        pending.Push(root);
        ulong bytes = 0;
        long files = 0;
        long directories = 0;
        long unreadable = 0;
        long entriesChecked = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entriesChecked++;
                    if (entriesChecked % ProgressEntryInterval == 0) progress?.Invoke(entriesChecked);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(path); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    { unreadable++; continue; }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!ShouldDescend(attributes)) { unreadable++; continue; }
                        directories++;
                        pending.Push(path);
                        continue;
                    }
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    { unreadable++; continue; }

                    try
                    {
                        bytes = checked(bytes + (ulong)new FileInfo(path).Length);
                        files++;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or OverflowException)
                    { unreadable++; }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            { unreadable++; }
        }

        if (entriesChecked > 0 && entriesChecked % ProgressEntryInterval != 0)
            progress?.Invoke(entriesChecked);
        return new(bytes, files, directories, unreadable);
    }

    internal static bool ShouldDescend(FileAttributes attributes) =>
        (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
}
