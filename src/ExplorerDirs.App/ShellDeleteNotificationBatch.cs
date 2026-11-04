using System.Diagnostics;

namespace ExplorerDirs.App;

internal sealed class ShellDeleteNotificationBatch
{
    internal const int MaximumPendingPaths = 512;
    private readonly object _gate = new();
    private readonly Queue<string> _paths = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private string? _latestPath;
    private bool _dispatchPending;
    private long _lastRecordedAt;

    internal bool TryRecordForDispatch(string path)
    {
        lock (_gate)
        {
            // One handoff per burst must still preserve every bounded distinct deletion.
            if (_pending.Add(path))
            {
                if (_paths.Count == MaximumPendingPaths) _pending.Remove(_paths.Dequeue());
                _paths.Enqueue(path);
            }
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

    internal bool TryTakeAfterQuietPeriod(TimeSpan quietPeriod, out IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            paths = [];
            if (_paths.Count == 0)
            {
                _dispatchPending = false;
                return false;
            }
            if (Stopwatch.GetElapsedTime(_lastRecordedAt) < quietPeriod) return false;
            paths = _paths.ToArray();
            _paths.Clear();
            _pending.Clear();
            _latestPath = null;
            _dispatchPending = false;
            return true;
        }
    }
}
