using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;

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
    private static readonly EnumerationOptions TreeScanEnumerationOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

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

    internal static IReadOnlyDictionary<string, RecursiveDirectoryScanResult> ScanDirectoryTree(
        string root, CancellationToken cancellationToken, Action<long>? progress = null)
        => ScanDirectoryTreeCore(root, cancellationToken, progress, resultPaths: null);

    internal static IReadOnlyDictionary<string, RecursiveDirectoryScanResult> ScanDirectoryTreeForPaths(
        string root, IEnumerable<string> resultPaths, CancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(resultPaths);
        cancellationToken.ThrowIfCancellationRequested();
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in resultPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requested.Add(Path.GetFullPath(path));
        }
        return ScanDirectoryTreeCore(root, cancellationToken, progress, requested);
    }

    private static IReadOnlyDictionary<string, RecursiveDirectoryScanResult> ScanDirectoryTreeCore(
        string root, CancellationToken cancellationToken, Action<long>? progress,
        HashSet<string>? resultPaths)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rootPath = Path.GetFullPath(root);
        var rootAttributes = File.GetAttributes(rootPath);
        if ((rootAttributes & FileAttributes.Directory) == 0)
            throw new DirectoryNotFoundException($"不是文件夹：{rootPath}");

        var rootNode = new DirectoryScanNode(rootPath);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            rootNode.Unreadable = 1;
            return IncludeResult(rootNode, resultPaths)
                ? new Dictionary<string, RecursiveDirectoryScanResult>(StringComparer.OrdinalIgnoreCase)
                    { [rootPath] = rootNode.ToResult() }
                : new Dictionary<string, RecursiveDirectoryScanResult>(StringComparer.OrdinalIgnoreCase);
        }

        var results = new Dictionary<string, RecursiveDirectoryScanResult>(
            resultPaths?.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        // Retain only active ancestors, not every sibling directory path; this bounds traversal state by depth.
        var frames = new Stack<DirectoryScanFrame>();
        long entriesChecked = 0;
        try
        {
            try { frames.Push(OpenDirectory(rootNode)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                rootNode.Unreadable = checked(rootNode.Unreadable + 1);
                CompleteNode(rootNode);
            }
            while (frames.TryPeek(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasEntry;
                try { hasEntry = frame.Entries.MoveNext(); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    frame.Node.Unreadable = checked(frame.Node.Unreadable + 1);
                    CompleteCurrentDirectory();
                    continue;
                }
                if (!hasEntry)
                {
                    CompleteCurrentDirectory();
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                entriesChecked++;
                if (entriesChecked % ProgressEntryInterval == 0) progress?.Invoke(entriesChecked);
                var entry = frame.Entries.Current;
                var attributes = entry.Attributes;

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!ShouldDescend(attributes))
                    { frame.Node.Unreadable = checked(frame.Node.Unreadable + 1); continue; }
                    var childPath = Path.GetFullPath(Path.Combine(frame.Node.Path, entry.Name));
                    var child = new DirectoryScanNode(childPath, frame.Node);
                    try { frames.Push(OpenDirectory(child)); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        child.Unreadable = checked(child.Unreadable + 1);
                        CompleteNode(child);
                    }
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { frame.Node.Unreadable = checked(frame.Node.Unreadable + 1); continue; }

                if (entry.Length < 0)
                { frame.Node.Unreadable = checked(frame.Node.Unreadable + 1); continue; }
                try
                {
                    frame.Node.Bytes = checked(frame.Node.Bytes + (ulong)entry.Length);
                    frame.Node.Files = checked(frame.Node.Files + 1);
                }
                catch (OverflowException)
                { frame.Node.Unreadable = checked(frame.Node.Unreadable + 1); }
            }
        }
        finally
        {
            while (frames.TryPop(out var frame)) frame.Dispose();
        }

        if (entriesChecked > 0 && entriesChecked % ProgressEntryInterval != 0)
            progress?.Invoke(entriesChecked);

        return results;

        DirectoryScanFrame OpenDirectory(DirectoryScanNode node)
        {
            // The find entry already carries attributes and file length, avoiding separate path-stat calls.
            var entries = new FileSystemEnumerable<DirectoryScanEntry>(node.Path,
                static (ref FileSystemEntry entry) => new(
                    entry.FileName.ToString(), entry.Attributes, entry.Length),
                TreeScanEnumerationOptions);
            return new(node, entries.GetEnumerator());
        }

        void CompleteCurrentDirectory()
        {
            var completed = frames.Pop();
            completed.Dispose();
            CompleteNode(completed.Node);
        }

        void CompleteNode(DirectoryScanNode node)
        {
            if (IncludeResult(node, resultPaths)) results.Add(node.Path, node.ToResult());
            if (node.Parent is not { } parent) return;
            parent.Bytes = checked(parent.Bytes + node.Bytes);
            parent.Files = checked(parent.Files + node.Files);
            parent.Directories = checked(parent.Directories + 1 + node.Directories);
            parent.Unreadable = checked(parent.Unreadable + node.Unreadable);
        }
    }

    private static bool IncludeResult(DirectoryScanNode node, HashSet<string>? resultPaths) =>
        resultPaths is null || resultPaths.Contains(node.Path);

    private sealed class DirectoryScanNode(string path, DirectoryScanNode? parent = null)
    {
        internal string Path { get; } = path;
        internal DirectoryScanNode? Parent { get; } = parent;
        internal ulong Bytes;
        internal long Files;
        internal long Directories;
        internal long Unreadable;

        internal RecursiveDirectoryScanResult ToResult() => new(Bytes, Files, Directories, Unreadable);
    }

    private readonly record struct DirectoryScanEntry(string Name, FileAttributes Attributes, long Length);

    private sealed class DirectoryScanFrame(DirectoryScanNode node, IEnumerator<DirectoryScanEntry> entries) : IDisposable
    {
        internal DirectoryScanNode Node { get; } = node;
        internal IEnumerator<DirectoryScanEntry> Entries { get; } = entries;

        public void Dispose() => Entries.Dispose();
    }

    internal static bool ShouldDescend(FileAttributes attributes) =>
        (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
}
