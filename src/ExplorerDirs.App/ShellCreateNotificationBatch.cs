using System.Diagnostics;

namespace ExplorerDirs.App;

internal sealed class ShellCreateNotificationBatch
{
    private readonly object _gate = new();
    private string? _latestPath;
    private bool _dispatchPending;
    private long _lastRecordedAt;

    internal bool TryRecordForDispatch(string path)
    {
        lock (_gate)
        {
            _latestPath = path;
            _lastRecordedAt = Stopwatch.GetTimestamp();
            if (_dispatchPending) return false;
            _dispatchPending = true;
            return true;
        }
    }

    internal string? TakeForDispatcher()
    {
        lock (_gate)
        {
            // Keep one dispatcher handoff pending until the delayed notification
            // consumes this burst; clearing it here can enqueue one callback per file.
            return _latestPath;
        }
    }

    internal string? TakeLatest()
    {
        lock (_gate)
        {
            var path = _latestPath;
            _latestPath = null;
            _dispatchPending = false;
            return path;
        }
    }

    internal bool TryTakeLatestAfterQuietPeriod(TimeSpan quietPeriod, out string? path)
    {
        lock (_gate)
        {
            path = null;
            if (_latestPath is null)
            {
                _dispatchPending = false;
                return false;
            }
            if (Stopwatch.GetElapsedTime(_lastRecordedAt) < quietPeriod) return false;

            path = _latestPath;
            _latestPath = null;
            _dispatchPending = false;
            return true;
        }
    }
}
