using System.Diagnostics;
using System.Collections.Concurrent;
using ExplorerDirs.App;

// Isolated STA harness for the product's watcher delta queue and count batch.
// It never owns or creates a production ExplorerBrowser.
internal sealed class ProbeShellDirectoryMonitor : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ShellDirectoryChangeQueue _queue = new();
    private readonly ShellDirectoryChangeBatch _batch;
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private long _lastEventAt;
    private int _diagnosticCount;
    internal int RefreshCount { get; private set; }
    internal string Operation { get; set; } = "idle";
    internal string Diagnostics => string.Join('|', _diagnostics);

    internal ProbeShellDirectoryMonitor(string path, int initialCount)
    {
        _batch = new ShellDirectoryChangeBatch(initialCount);
        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = false,
            NotifyFilter = ShellDirectoryChangeMonitorOptions.NotifyFilter,
            InternalBufferSize = 16 * 1024
        };
        _watcher.Created += (_, args) => Record("created", ShellDirectoryChangeMonitorOptions.ForCreated(), args.FullPath);
        _watcher.Deleted += (_, args) => Record("deleted", ShellDirectoryChangeMonitorOptions.ForDeleted(), args.FullPath);
        _watcher.Changed += (_, args) => Record("changed", ShellDirectoryChangeMonitorOptions.ForChanged(), args.FullPath);
        _watcher.Renamed += (_, args) => Record("renamed", ShellDirectoryChangeMonitorOptions.ForRenamed(), args.FullPath);
        _watcher.Error += (_, _) => Record("error", ShellDirectoryChangeMonitorOptions.ForError(), null);
        _watcher.EnableRaisingEvents = true;
    }

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
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _queue.Invalidate();
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
