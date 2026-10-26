using System.Diagnostics;

namespace ExplorerDirs.App;

internal sealed class ShellDeleteNotificationBatch
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
        lock (_gate) return _latestPath;
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
