namespace ExplorerDirs.App;

internal readonly struct ShellDirectoryChangeBatchData(int delta, bool forceRefresh)
{
    internal int Delta { get; } = delta;
    internal bool ForceRefresh { get; } = forceRefresh;
}

// Coalesces watcher deltas across one dispatcher callback and its timer window.
internal sealed class ShellDirectoryChangeQueue
{
    private readonly object _gate = new();
    private int _delta;
    private bool _forceRefresh;
    private bool _sawCreate;
    private bool _sawDelete;
    private bool _dispatchPending;
    private bool _invalidated;

    internal string DiagnosticSnapshot
    {
        get
        {
            lock (_gate) return $"pending={_delta},force={_forceRefresh},dispatch-pending={_dispatchPending},invalidated={_invalidated}";
        }
    }

    internal bool Enqueue(int delta, bool forceRefresh)
    {
        lock (_gate)
        {
            if (_invalidated) return false;
            _delta += delta;
            _sawCreate |= delta > 0;
            _sawDelete |= delta < 0;
            _forceRefresh |= forceRefresh;
            // Net count cannot detect replacing one item with another when Shell misses both notifications.
            _forceRefresh |= _sawCreate && _sawDelete;
            if (_dispatchPending) return false;
            _dispatchPending = true;
            return true;
        }
    }

    internal ShellDirectoryChangeBatchData Drain() => Drain(releaseDispatchPending: true);

    internal ShellDirectoryChangeBatchData DrainWhileDispatchPending() => Drain(releaseDispatchPending: false);

    private ShellDirectoryChangeBatchData Drain(bool releaseDispatchPending)
    {
        lock (_gate)
        {
            if (_invalidated) return default;
            var result = new ShellDirectoryChangeBatchData(_delta, _forceRefresh);
            _delta = 0;
            _forceRefresh = false;
            if (releaseDispatchPending)
            {
                _dispatchPending = false;
                _sawCreate = false;
                _sawDelete = false;
            }
            return result;
        }
    }

    internal void Invalidate()
    {
        lock (_gate)
        {
            _invalidated = true;
            _delta = 0;
            _forceRefresh = false;
            _sawCreate = false;
            _sawDelete = false;
            _dispatchPending = false;
        }
    }
}
