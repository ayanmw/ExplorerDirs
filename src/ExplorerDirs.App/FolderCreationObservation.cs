using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal sealed record FolderCreationSnapshot(IReadOnlyList<string> Directories, bool Failed);

// Tracks only short-lived directory creation during the Shell NewFolder verb.
// No parent enumeration runs on the UI thread.
internal sealed class FolderCreationObservation : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private readonly HashSet<string> _created = new(StringComparer.OrdinalIgnoreCase);
    private bool _failed;

    private FolderCreationObservation(string directory)
    {
        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false
        };
        _watcher.Created += (_, args) =>
        {
            // This check happens on the watcher's callback thread, not the UI thread.
            if (!Directory.Exists(args.FullPath)) return;
            lock (_gate) _created.Add(args.FullPath);
        };
        _watcher.Error += (_, _) => { lock (_gate) _failed = true; };
        try { _watcher.EnableRaisingEvents = true; }
        catch
        {
            _watcher.Dispose();
            throw;
        }
    }

    public static FolderCreationObservation? TryStart(string directory)
    {
        try
        {
            // UNC and mapped/network or removable drives should not acquire a
            // second live handle merely to help automatic rename.
            if (directory.StartsWith(@"\\", StringComparison.Ordinal) ||
                GetDriveType(Path.GetPathRoot(directory) ?? "") != 3) return null;
            return new FolderCreationObservation(directory);
        }
        catch (Exception) { return null; }
    }

    public FolderCreationSnapshot Snapshot()
    {
        lock (_gate) return new FolderCreationSnapshot(_created.ToArray(), _failed);
    }

    public void Dispose() => _watcher.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string rootPath);
}
