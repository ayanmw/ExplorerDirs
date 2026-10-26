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
