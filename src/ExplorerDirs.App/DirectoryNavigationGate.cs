using System.IO;
using System.Security;

namespace ExplorerDirs.App;

internal enum DirectoryProbeStatus { Directory, AccessDenied, Unavailable }

internal static class DirectoryProbe
{
    public static DirectoryProbeStatus Check(string path,
        Func<string, FileAttributes>? getAttributes = null,
        Func<string, bool>? exists = null)
    {
        getAttributes ??= File.GetAttributes;
        exists ??= Directory.Exists;
        try
        {
            return (getAttributes(path) & FileAttributes.Directory) != 0
                ? DirectoryProbeStatus.Directory : DirectoryProbeStatus.Unavailable;
        }
        catch (UnauthorizedAccessException) { return DirectoryProbeStatus.AccessDenied; }
        catch (SecurityException) { return DirectoryProbeStatus.AccessDenied; }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // GetFileAttributes can fail on the root of a valid UNC share. Only
            // there do we need a second probe; other failures remain unavailable.
            if (!IsUncShareRoot(path)) return DirectoryProbeStatus.Unavailable;
            try { return exists(path) ? DirectoryProbeStatus.Directory : DirectoryProbeStatus.Unavailable; }
            catch (Exception) { return DirectoryProbeStatus.Unavailable; }
        }
    }

    private static bool IsUncShareRoot(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
        var root = Path.GetPathRoot(path);
        return root is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record DirectoryProbeResult(string Path, DirectoryProbeStatus Status, int Generation)
{
    public bool CanNavigate => Status != DirectoryProbeStatus.Unavailable;
}

internal sealed class DirectoryNavigationGate
{
    private int _generation;

    public Task<DirectoryProbeResult> ProbeAsync(string path,
        Func<string, DirectoryProbeStatus>? probe = null)
    {
        var generation = Interlocked.Increment(ref _generation);
        return Task.Run(() =>
        {
            DirectoryProbeStatus status;
            try { status = probe?.Invoke(path) ?? DirectoryProbe.Check(path); }
            catch (Exception) { status = DirectoryProbeStatus.Unavailable; }
            return new DirectoryProbeResult(path, status, generation);
        });
    }

    public bool IsCurrent(DirectoryProbeResult result) =>
        result.Generation == Volatile.Read(ref _generation);

    public void Cancel() => Interlocked.Increment(ref _generation);
}
