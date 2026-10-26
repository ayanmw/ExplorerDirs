namespace ExplorerDirs.App;

internal sealed class ShellDirectoryWatcherRecovery
{
    private const int InitialDelayMilliseconds = 250;
    private const int MaximumDelayMilliseconds = 30000;
    private readonly object _gate = new();
    private int _failedAttempts;
    private bool _pending;
    private bool _retired;

    internal static bool TryRunCallback(Action callback, Action<Exception> onFailure)
    {
        try
        {
            callback();
            return true;
        }
        catch (Exception error)
        {
            try { onFailure(error); }
            catch (Exception) { /* Recovery observers must not escape a Dispatcher callback. */ }
            return false;
        }
    }

    internal static async Task RetryAfterDispatchFailureAsync(TimeSpan delay, Action retry)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        retry();
    }

    internal bool TryBegin(out TimeSpan delay)
    {
        lock (_gate)
        {
            if (_retired || _pending)
            {
                delay = default;
                return false;
            }

            var shift = Math.Min(_failedAttempts, 16);
            var milliseconds = Math.Min((long)InitialDelayMilliseconds << shift, MaximumDelayMilliseconds);
            delay = TimeSpan.FromMilliseconds(milliseconds);
            _pending = true;
            return true;
        }
    }

    internal void Complete(bool recovered)
    {
        lock (_gate)
        {
            if (!_pending) return;
            _pending = false;
            _failedAttempts = recovered ? 0 : Math.Min(_failedAttempts + 1, 16);
        }
    }

    internal void CompleteFailedAttempt(Action cleanup)
    {
        Complete(recovered: false);
        try { cleanup(); }
        catch (Exception) { /* Recovery state must be released before best-effort timer cleanup. */ }
    }

    internal void Retire()
    {
        lock (_gate)
        {
            _retired = true;
            _pending = false;
        }
    }
}

internal sealed class ShellDirectoryWatcherEventGate<TWatcher> where TWatcher : class
{
    private readonly object _gate = new();
    private TWatcher? _current;

    internal void SetCurrent(TWatcher? watcher)
    {
        lock (_gate) _current = watcher;
    }

    internal bool TryInvoke(TWatcher watcher, Action callback)
        => TryInvoke(watcher, callback, onFailure: null);

    internal bool TryInvoke(TWatcher watcher, Action callback, Action<Exception>? onFailure)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_current, watcher)) return false;
            try
            {
                callback();
                return true;
            }
            catch (Exception error)
            {
                try { onFailure?.Invoke(error); }
                catch (Exception) { /* Watcher recovery must not escape a ThreadPool event callback. */ }
                return false;
            }
        }
    }
}
