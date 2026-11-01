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

    internal async Task<bool> RunLatestAsync<T>(Func<CancellationToken, T> scan,
        SynchronizationContext ownerContext, Action<T> publish)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(ownerContext);
        ArgumentNullException.ThrowIfNull(publish);
        var ticket = Begin();
        T result;
        try
        {
            result = await Task.Run(() => scan(ticket.CancellationToken), ticket.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!IsCurrent(ticket))
        {
            // Retired work must not report a failure after a newer scan has taken ownership.
            return false;
        }

        if (!IsCurrent(ticket)) return false;
        return await PostToOwnerAsync(ticket, ownerContext, () => publish(result)).ConfigureAwait(false);
    }

    internal async Task<bool> RunLatestIncrementalAsync<TItem, TResult>(
        Func<CancellationToken, Action<TItem>, TResult> scan,
        SynchronizationContext ownerContext,
        Action<TItem> publishItem,
        Action<TResult> publishFinal)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(ownerContext);
        ArgumentNullException.ThrowIfNull(publishItem);
        ArgumentNullException.ThrowIfNull(publishFinal);
        var ticket = Begin();
        TResult result;
        try
        {
            result = await Task.Run(() => scan(ticket.CancellationToken, item =>
            {
                if (!IsCurrent(ticket)) throw new OperationCanceledException(ticket.CancellationToken);
                if (!PostToOwnerAsync(ticket, ownerContext, () => publishItem(item)).GetAwaiter().GetResult())
                    throw new OperationCanceledException(ticket.CancellationToken);
            }), ticket.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!IsCurrent(ticket))
        {
            // Retired incremental work must not report a failure after a newer scan has taken ownership.
            return false;
        }

        if (!IsCurrent(ticket)) return false;
        try { return await PostToOwnerAsync(ticket, ownerContext, () => publishFinal(result)).ConfigureAwait(false); }
        catch (Exception) when (!IsCurrent(ticket)) { return false; }
    }

    private async Task<bool> PostToOwnerAsync(DirectoryTreeScanTicket ticket,
        SynchronizationContext ownerContext, Action publish)
    {
        var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var retirement = ticket.CancellationToken.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetResult(false), published);
        ownerContext.Post(_ =>
        {
            if (!IsCurrent(ticket))
            {
                published.TrySetResult(false);
                return;
            }
            try
            {
                publish();
                published.TrySetResult(true);
            }
            catch (Exception exception) { published.TrySetException(exception); }
        }, null);
        return await published.Task.ConfigureAwait(false);
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

    internal void InvalidateCurrent() => CancelCurrent();

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
