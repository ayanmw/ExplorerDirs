namespace ExplorerDirs.NativeProbe;

internal sealed class DirectoryTreeScanCoordinator : IDisposable
{
    private readonly object _gate = new();
    private DirectoryTreeScanTicket? _current;
    private long _generation;
    private bool _disposed;

    internal DirectoryTreeScanTicket Begin()
    {
        DirectoryTreeScanTicket? retired;
        DirectoryTreeScanTicket ticket;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            retired = _current;
            ticket = new DirectoryTreeScanTicket(++_generation, new CancellationTokenSource());
            _current = ticket;
        }
        Retire(retired);
        return ticket;
    }

    internal bool IsCurrent(DirectoryTreeScanTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_gate)
            return !_disposed && ReferenceEquals(_current, ticket) && !ticket.CancellationToken.IsCancellationRequested;
    }

    internal void CancelCurrent()
    {
        DirectoryTreeScanTicket? retired;
        lock (_gate)
        {
            retired = _current;
            _current = null;
            _generation++;
        }
        Retire(retired);
    }

    public void Dispose()
    {
        DirectoryTreeScanTicket? retired;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            retired = _current;
            _current = null;
            _generation++;
        }
        Retire(retired);
    }

    private static void Retire(DirectoryTreeScanTicket? ticket)
    {
        if (ticket is null) return;
        try { ticket.Cancel(); }
        finally { ticket.Dispose(); }
    }
}

internal sealed class DirectoryTreeScanTicket(long generation, CancellationTokenSource cancellation)
    : IDisposable
{
    internal long Generation { get; } = generation;
    internal CancellationToken CancellationToken { get; } = cancellation.Token;

    internal void Cancel() => cancellation.Cancel();
    public void Dispose() => cancellation.Dispose();
}
