using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ExplorerDirs.App;

// A marker proves only that a previous process did not clean up. It cannot
// identify a crashing Shell extension or distinguish a crash from a forced kill.
internal sealed class RunMarkerTracker : IDisposable
{
    private readonly string _directory;
    private readonly string _id;
    private int _disposed;

    public bool PriorUncleanExit { get; }

    private RunMarkerTracker(string directory, string id, bool priorUncleanExit)
    {
        _directory = directory;
        _id = id;
        PriorUncleanExit = priorUncleanExit;
    }

    public static RunMarkerTracker? TryStart(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var unclean = ArchiveEndedRuns(directory);
            using var process = Process.GetCurrentProcess();
            var marker = new RunMarker(process.Id, process.StartTime.ToUniversalTime().Ticks,
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown");
            var id = Guid.NewGuid().ToString("N");
            var temporary = Path.Combine(directory, $"run-{id}.tmp");
            var active = Path.Combine(directory, $"run-{id}.json");
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker), new UTF8Encoding(false));
            File.Move(temporary, active);
            return new RunMarkerTracker(directory, id, unclean);
        }
        catch (Exception)
        {
            // Failure to record process health must never prevent file browsing.
            return null;
        }
    }

    private static bool ArchiveEndedRuns(string directory)
    {
        var detected = false;
        foreach (var active in Directory.EnumerateFiles(directory, "run-*.json"))
        {
            RunMarker? marker;
            try { marker = JsonSerializer.Deserialize<RunMarker>(File.ReadAllText(active)); }
            catch (Exception) { continue; } // Preserve unreadable evidence.
            if (marker is null || marker.ProcessId <= 0 || marker.StartedUtcTicks <= 0 ||
                IsProcessStillRunning(marker.ProcessId, marker.StartedUtcTicks)) continue;
            detected = true;
            var suffix = Path.GetFileName(active)[4..];
            try
            {
                File.Move(active, Path.Combine(directory, "abrupt-" + suffix));
            }
            catch (Exception) { /* Preserve the active marker for a later attempt. */ }
        }
        return detected;
    }

    private static bool IsProcessStillRunning(int processId, long startedUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startedUtcTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; } // Access denied is not proof of exit.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var active = Path.Combine(_directory, $"run-{_id}.json");
        var finished = Path.Combine(_directory, $"finished-{_id}.json");
        try
        {
            File.Move(active, finished);
            try { File.Delete(finished); }
            catch (Exception) { /* A finished marker is ignored on next startup. */ }
        }
        catch (Exception)
        {
            try { File.Delete(active); }
            catch (Exception) { /* The next startup may conservatively report it. */ }
        }
    }

    private sealed record RunMarker(int ProcessId, long StartedUtcTicks, string AppVersion);
}
