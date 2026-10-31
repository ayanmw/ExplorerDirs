using System.Diagnostics;
using System.Collections.Concurrent;
using ExplorerDirs.App;

// Isolated STA harness for the product's watcher delta queue and count batch.
// It never owns or creates a production ExplorerBrowser.
internal sealed class ProbeShellDirectoryMonitor : IDisposable
{
    private readonly string _path;
    private readonly ShellDirectoryChangeQueue _queue = new();
    private readonly ShellDirectoryChangeBatch _batch;
    private readonly ShellDirectoryWatcherRecovery _recovery = new();
    private readonly ShellDirectoryWatcherEventGate<FileSystemWatcher> _watcherEventGate = new();
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private readonly object _watcherGate = new();
    private FileSystemWatcher? _watcher;
    private long _lastEventAt;
    private int _diagnosticCount;
    private int _watcherGeneration;
    private int _acceptedEvents;
    private bool _disposed;
    internal int RefreshCount { get; private set; }
    internal string Operation { get; set; } = "idle";
    internal string Diagnostics => string.Join('|', _diagnostics);
    internal int WatcherGeneration => Volatile.Read(ref _watcherGeneration);
    internal int AcceptedEventCount => Volatile.Read(ref _acceptedEvents);
    internal string ChangeQueueSnapshot => _queue.DiagnosticSnapshot;

    internal ProbeShellDirectoryMonitor(string path, int initialCount)
    {
        _path = path;
        _batch = new ShellDirectoryChangeBatch(initialCount);
        StartWatcher();
    }

    internal void InjectWatcherErrorForProbe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        FileSystemWatcher watcher;
        lock (_watcherGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            watcher = _watcher ?? throw new InvalidOperationException("No active watcher is available for the injected error.");
        }
        OnWatcherError(watcher, error);
    }

    internal bool WaitForWatcherGeneration(int generation, TimeSpan timeout) =>
        SpinWait.SpinUntil(() => WatcherGeneration >= generation, timeout);

    internal bool WaitForAcceptedEvents(int count, TimeSpan timeout) =>
        SpinWait.SpinUntil(() => AcceptedEventCount >= count, timeout);

    internal bool Reconcile(Func<int?> readCount, Action refresh, Func<bool> expectedItemsVisible,
        Action pump, int timeoutMilliseconds = 5000, Action? afterRefreshStarted = null,
        Action? afterRefreshSettled = null)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        var recheckPending = false;
        var afterRefreshCallbackInvoked = false;
        while (Environment.TickCount64 < deadline)
        {
            pump();
            if (Volatile.Read(ref _lastEventAt) == 0 ||
                Stopwatch.GetElapsedTime(Volatile.Read(ref _lastEventAt)) < TimeSpan.FromMilliseconds(250))
                continue;

            var changes = _queue.DrainWhileDispatchPending();
            _batch.RecordChanges(changes.Delta, changes.ForceRefresh);
            var actualCount = readCount();
            var expectedCount = _batch.ExpectedCountAfterChanges;
            if (!_batch.ForceRefresh && actualCount == expectedCount && expectedItemsVisible())
            {
                RecordBatch($"settled-count={actualCount},expected={expectedCount},force={_batch.ForceRefresh}");
                if (_batch.CompleteRefresh(_queue, readCount)) return expectedItemsVisible();
                recheckPending = false;
                continue;
            }

            var defer = _batch.ShouldDeferRefresh(actualCount, recheckPending);
            RecordBatch($"delta={changes.Delta},queue-force={changes.ForceRefresh},batch-force={_batch.ForceRefresh},actual={actualCount},expected={expectedCount},defer={defer},recheck={recheckPending}");
            if (defer)
            {
                recheckPending = true;
                continue;
            }

            var refreshRequested = _batch.Complete(actualCount);
            RecordBatch($"tick-complete,refresh-requested={refreshRequested},actual={actualCount},expected={_batch.ExpectedCountAfterChanges},state={_batch.DiagnosticSnapshot}");
            if (!refreshRequested)
            {
                if (expectedItemsVisible()) return true;
                // Shell can publish the expected count before the matching item name is readable.
                // Keep this probe waiting for the native view instead of reporting a false mismatch.
                RecordBatch("count-settled-waiting-for-items");
                continue;
            }
            expectedCount = _batch.ExpectedCountAfterChanges;
            RefreshCount++;
            RecordBatch($"refresh-{RefreshCount}-start,actual={actualCount},expected={expectedCount},force={_batch.ForceRefresh}");
            refresh();
            if (!afterRefreshCallbackInvoked && afterRefreshStarted is not null)
            {
                afterRefreshStarted();
                afterRefreshCallbackInvoked = true;
            }
            var refreshCallbackDeadline = Math.Min(deadline, Environment.TickCount64 + 500);
            while (Environment.TickCount64 < refreshCallbackDeadline)
            {
                pump();
                actualCount = readCount();
                if (actualCount == expectedCount && expectedItemsVisible())
                {
                    break;
                }
            }
            var completed = _batch.CompleteRefresh(_queue, readCount);
            RecordBatch($"refresh-{RefreshCount}-complete,completed={completed},last-expected={_batch.LastRefreshExpectedCount},last-actual={_batch.LastRefreshObservedCount},drained-delta={_batch.LastRefreshDrainedDelta},state={_batch.DiagnosticSnapshot}");
            // CompleteRefresh performs the authoritative count check; it can observe convergence just
            // after the short polling window, so run the settle callback after that check too.
            if (completed) afterRefreshSettled?.Invoke();
            if (completed) return expectedItemsVisible();
            recheckPending = false;
        }

        return false;
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_watcherGate)
        {
            if (_disposed) return;
            _disposed = true;
            _recovery.Retire();
            _watcherEventGate.SetCurrent(null);
            watcher = _watcher;
            _watcher = null;
        }
        if (watcher is not null) DisposeWatcher(watcher);
        _queue.Invalidate();
    }

    private void StartWatcher()
    {
        var watcher = CreateWatcher();
        lock (_watcherGate)
        {
            if (_disposed)
            {
                watcher.Dispose();
                throw new ObjectDisposedException(nameof(ProbeShellDirectoryMonitor));
            }
            _watcherEventGate.SetCurrent(watcher);
            try
            {
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
                Interlocked.Increment(ref _watcherGeneration);
            }
            catch
            {
                _watcherEventGate.SetCurrent(null);
                watcher.Dispose();
                throw;
            }
        }
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(_path)
        {
            IncludeSubdirectories = false,
            NotifyFilter = ShellDirectoryChangeMonitorOptions.NotifyFilter,
            InternalBufferSize = 16 * 1024
        };
        watcher.Created += (_, args) => OnWatcherEvent(watcher, "created",
            ShellDirectoryChangeMonitorOptions.ForCreated(), args.FullPath);
        watcher.Deleted += (_, args) => OnWatcherEvent(watcher, "deleted",
            ShellDirectoryChangeMonitorOptions.ForDeleted(), args.FullPath);
        watcher.Changed += (_, args) => OnWatcherEvent(watcher, "changed",
            ShellDirectoryChangeMonitorOptions.ForChanged(), args.FullPath);
        watcher.Renamed += (_, args) => OnWatcherEvent(watcher, "renamed",
            ShellDirectoryChangeMonitorOptions.ForRenamed(), args.FullPath);
        watcher.Error += (_, args) => OnWatcherError(watcher, args.GetException());
        return watcher;
    }

    private void OnWatcherEvent(FileSystemWatcher watcher, string kind, ShellDirectoryChangeDelta change, string path)
    {
        _watcherEventGate.TryInvoke(watcher, () =>
        {
            Record(kind, change, path);
            Interlocked.Increment(ref _acceptedEvents);
        });
    }

    private void OnWatcherError(FileSystemWatcher watcher, Exception error)
    {
        _watcherEventGate.TryInvoke(watcher, () =>
        {
            Record($"error:{error.GetType().Name}:hr=0x{error.HResult:X8}",
                ShellDirectoryChangeMonitorOptions.ForError(), null);
            if (_recovery.TryBegin(out var delay)) _ = RecoverWatcherAsync(watcher, delay);
        });
    }

    private async Task RecoverWatcherAsync(FileSystemWatcher? failedWatcher, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        FileSystemWatcher? retiredWatcher = null;
        var recovered = false;
        var retry = false;
        var retryDelay = default(TimeSpan);
        lock (_watcherGate)
        {
            if (_disposed)
            {
                _recovery.Complete(recovered: false);
                return;
            }
            if (!ReferenceEquals(_watcher, failedWatcher))
            {
                _recovery.Complete(recovered: true);
                return;
            }

            _watcherEventGate.SetCurrent(null);
            retiredWatcher = _watcher;
            _watcher = null;
            FileSystemWatcher? replacement = null;
            try
            {
                replacement = CreateWatcher();
                _watcherEventGate.SetCurrent(replacement);
                replacement.EnableRaisingEvents = true;
                _watcher = replacement;
                Interlocked.Increment(ref _watcherGeneration);
                recovered = true;
            }
            catch
            {
                _watcherEventGate.SetCurrent(null);
                if (replacement is not null) DisposeWatcher(replacement);
                Record("recovery-failed", ShellDirectoryChangeMonitorOptions.ForError(), null);
            }

            _recovery.Complete(recovered);
            if (!recovered) retry = _recovery.TryBegin(out retryDelay);
        }

        if (retiredWatcher is not null) DisposeWatcher(retiredWatcher);
        if (retry) _ = RecoverWatcherAsync(failedWatcher: null, retryDelay);
    }

    private static void DisposeWatcher(FileSystemWatcher watcher)
    {
        try { watcher.EnableRaisingEvents = false; }
        catch (ObjectDisposedException) { }
        finally { watcher.Dispose(); }
    }

    private void Record(string kind, ShellDirectoryChangeDelta change, string? path)
    {
        Interlocked.Exchange(ref _lastEventAt, Stopwatch.GetTimestamp());
        _queue.Enqueue(change.Delta, change.ForceRefresh);
        var extension = path is null ? "none" : Path.GetExtension(path);
        var exists = path is not null && (File.Exists(path) || Directory.Exists(path));
        if (Interlocked.Increment(ref _diagnosticCount) <= 128)
            _diagnostics.Enqueue($"event:{Operation}:{kind}:delta={change.Delta}:force={change.ForceRefresh}:ext={extension}:exists={exists}");
    }

    private void RecordBatch(string entry)
    {
        if (Interlocked.Increment(ref _diagnosticCount) <= 128)
            _diagnostics.Enqueue($"batch:{Operation}:{entry}");
    }
}
