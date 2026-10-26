namespace ExplorerDirs.App;

internal sealed class ShellCommandRefreshGate
{
    private bool _pending;
    private bool _querying;

    internal bool IsQuerying => _querying;

    internal bool BeginQuery()
    {
        if (_querying) return false;
        _querying = true;
        return true;
    }

    internal bool EndQuery()
    {
        _querying = false;
        return _pending;
    }

    internal bool Request(bool activePane, bool commandSurfaceEngaged)
    {
        if (!activePane || !commandSurfaceEngaged) return false;
        _pending = true;
        return true;
    }

    internal bool Consume(bool activePane, bool commandSurfaceEngaged)
    {
        if (_querying || !_pending) return false;
        _pending = false;
        if (!activePane || !commandSurfaceEngaged) return false;
        return true;
    }

    internal void Cancel() => _pending = false;
}
