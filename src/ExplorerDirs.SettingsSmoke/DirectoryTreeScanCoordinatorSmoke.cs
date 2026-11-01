using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;

namespace ExplorerDirs.SettingsSmoke;

internal static class DirectoryTreeScanCoordinatorSmoke
{
    internal static void Run()
    {
        using var coordinator = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
        var first = coordinator.Begin();
        var second = coordinator.Begin();
        Check(first.CancellationToken.IsCancellationRequested && !coordinator.IsCurrent(first),
            "a newer directory scan cancels and retires the previous request");
        Check(!second.CancellationToken.IsCancellationRequested && coordinator.IsCurrent(second),
            "the newest directory scan remains eligible to publish");

        var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleWorker = Task.Run(async () =>
        {
            workerStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, second.CancellationToken);
                return false;
            }
            catch (OperationCanceledException) { return true; }
        });
        workerStarted.Task.GetAwaiter().GetResult();
        var third = coordinator.Begin();
        Check(staleWorker.GetAwaiter().GetResult() && !coordinator.IsCurrent(second) && coordinator.IsCurrent(third),
            "starting another scan cancels a running worker before its result can publish");

        coordinator.CancelCurrent();
        Check(third.CancellationToken.IsCancellationRequested && !coordinator.IsCurrent(third),
            "navigation or view retirement cancels the current directory scan");

        var fourth = coordinator.Begin();
        Check(fourth.Generation > third.Generation && coordinator.IsCurrent(fourth),
            "a scan started after cancellation receives a fresh current generation");
        coordinator.Dispose();
        Check(fourth.CancellationToken.IsCancellationRequested && !coordinator.IsCurrent(fourth),
            "disposing a scan owner cancels outstanding work and blocks stale publication");

        VerifyLatestAsyncPublication();
        VerifyRetirementReleasesQueuedPublication();
        VerifyLatestIncrementalPublication();
        VerifyRetirementReleasesQueuedIncrementalWorker();
        VerifyQueuedStaleIncrementalPublication();
        VerifyQueuedStalePublication();
        VerifyFilesystemChangeInvalidatesQueuedPublication();
        VerifyRetiredWorkerFailure();

        Console.WriteLine("Directory tree scan coordinator smoke: PASS");
    }

    private static void VerifyLatestIncrementalPublication()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var context = new DispatcherSynchronizationContext(dispatcher);
                var ownerThreadId = Environment.CurrentManagedThreadId;
                var workerThreadId = 0;
                var order = new List<string>();
                var request = owner.RunLatestIncrementalAsync<int, int>(
                    (token, publish) =>
                    {
                        workerThreadId = Environment.CurrentManagedThreadId;
                        token.ThrowIfCancellationRequested();
                        publish(7);
                        return 42;
                    }, context,
                    value =>
                    {
                        Check(Environment.CurrentManagedThreadId == ownerThreadId,
                            "incremental values publish on the owner synchronization context");
                        order.Add($"item:{value}");
                    },
                    result =>
                    {
                        Check(Environment.CurrentManagedThreadId == ownerThreadId,
                            "final scan result publishes on the owner synchronization context");
                        order.Add($"final:{result}");
                    });
                PumpDispatcherUntil(request);
                Check(request.GetAwaiter().GetResult() && workerThreadId != ownerThreadId &&
                      order.SequenceEqual(["item:7", "final:42"]),
                    "incremental row values are delivered before the final scan summary");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Incremental owner publication smoke failed.", failure);
    }

    private static void VerifyRetirementReleasesQueuedPublication()
    {
        using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
        using var context = new QueuedSynchronizationContext();
        var valuePublished = false;
        var request = owner.RunLatestAsync(_ => 42, context, _ => valuePublished = true);
        if (!context.WaitForPost(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The completed scan did not reach its owner context before retirement.");

        owner.CancelCurrent();
        var retiredRequestReleased = request.Wait(TimeSpan.FromSeconds(2));
        context.RunPostedCallback();
        var requestPublished = request.GetAwaiter().GetResult();
        Check(retiredRequestReleased && !requestPublished && !valuePublished,
            "retiring a scan releases its pending publication without dispatching the queued owner callback");
    }

    private static void VerifyQueuedStaleIncrementalPublication()
    {
        using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
        using var context = new QueuedSynchronizationContext();
        var progressPublished = false;
        var finalPublished = false;
        var request = owner.RunLatestIncrementalAsync<int, int>(
            (_, publish) =>
            {
                publish(7);
                return 42;
            }, context, _ => progressPublished = true, _ => finalPublished = true);
        if (!context.WaitForPost(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The incremental scan result did not reach its owner context.");

        var newerRequest = owner.Begin();
        context.RunPostedCallback();
        Check(!request.GetAwaiter().GetResult() && !progressPublished && !finalPublished && owner.IsCurrent(newerRequest),
            "a queued incremental value is discarded after its scan generation is retired");
    }

    private static void VerifyRetirementReleasesQueuedIncrementalWorker()
    {
        using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
        using var context = new QueuedSynchronizationContext();
        var itemPublished = false;
        var finalPublished = false;
        var request = owner.RunLatestIncrementalAsync<int, int>(
            (_, publish) =>
            {
                publish(7);
                return 42;
            }, context, _ => itemPublished = true, _ => finalPublished = true);
        if (!context.WaitForPost(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The incremental result did not reach its owner context before retirement.");

        owner.Dispose();
        var retiredWorkerReleased = request.Wait(TimeSpan.FromSeconds(2));
        context.RunPostedCallback();
        var publishedAfterRetirement = itemPublished || finalPublished;
        var requestPublished = request.GetAwaiter().GetResult();
        Check(retiredWorkerReleased && !requestPublished && !publishedAfterRetirement,
            "disposing an incremental scan owner releases its worker without dispatching the queued owner callback");
    }

    private static void VerifyLatestAsyncPublication()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var context = new DispatcherSynchronizationContext(dispatcher);
                var ownerThreadId = Environment.CurrentManagedThreadId;
                var oldWorkerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var oldPublished = false;
                var currentValue = 0;
                var currentPublishedOnOwner = false;
                var oldRequest = owner.RunLatestAsync(
                    token =>
                    {
                        oldWorkerStarted.SetResult();
                        token.WaitHandle.WaitOne();
                        token.ThrowIfCancellationRequested();
                        return -1;
                    }, context, _ => oldPublished = true);
                oldWorkerStarted.Task.GetAwaiter().GetResult();
                var currentRequest = owner.RunLatestAsync(
                    _ => 42, context, value =>
                    {
                        currentValue = value;
                        currentPublishedOnOwner = Environment.CurrentManagedThreadId == ownerThreadId;
                    });
                PumpDispatcherUntil(Task.WhenAll(oldRequest, currentRequest));
                Check(!oldRequest.GetAwaiter().GetResult() && !oldPublished &&
                      currentRequest.GetAwaiter().GetResult() && currentValue == 42 && currentPublishedOnOwner,
                    "only the latest completed scan publishes on the owner synchronization context");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Latest asynchronous publication smoke failed.", failure);
    }

    private static void VerifyQueuedStalePublication()
    {
        using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
        using var context = new QueuedSynchronizationContext();
        var published = false;
        var request = owner.RunLatestAsync(_ => 42, context, _ => published = true);
        if (!context.WaitForPost(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The completed scan did not reach its owner context.");

        var newerRequest = owner.Begin();
        context.RunPostedCallback();
        Check(!request.GetAwaiter().GetResult() && !published && owner.IsCurrent(newerRequest),
            "a result retired after queueing is rejected when the owner context finally dispatches it");
    }

    private static void VerifyFilesystemChangeInvalidatesQueuedPublication()
    {
        using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
        using var context = new QueuedSynchronizationContext();
        var published = false;
        var workerToken = CancellationToken.None;
        var request = owner.RunLatestAsync(token =>
        {
            workerToken = token;
            return 10;
        }, context, _ => published = true);
        if (!context.WaitForPost(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The pre-change scan did not reach the owner context.");

        owner.InvalidateCurrent();
        context.RunPostedCallback();
        Check(workerToken.IsCancellationRequested && !request.GetAwaiter().GetResult() && !published,
            "a filesystem change invalidates a completed scan whose publication is already queued");

        var fresh = owner.Begin();
        Check(owner.IsCurrent(fresh), "a post-change rescan receives a publishable current ticket");
    }

    private static void VerifyRetiredWorkerFailure()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var owner = new ExplorerDirs.NativeProbe.DirectoryTreeScanCoordinator();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var context = new DispatcherSynchronizationContext(dispatcher);
                var retiredStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var releaseRetired = new ManualResetEventSlim();
                var retiredPublished = false;
                var latestValue = 0;
                var retiredRequest = owner.RunLatestAsync<int>(_ =>
                {
                    retiredStarted.SetResult();
                    releaseRetired.Wait();
                    throw new IOException("retired scan failure");
                }, context, _ => retiredPublished = true);
                retiredStarted.Task.GetAwaiter().GetResult();
                var latestRequest = owner.RunLatestAsync(_ => 42, context, value => latestValue = value);
                releaseRetired.Set();
                PumpDispatcherUntil(() => retiredRequest.IsCompleted && latestRequest.IsCompleted);

                var retiredFailurePropagated = false;
                try { _ = retiredRequest.GetAwaiter().GetResult(); }
                catch (IOException ex) when (ex.Message == "retired scan failure")
                { retiredFailurePropagated = true; }
                Check(!retiredFailurePropagated && !retiredPublished && latestRequest.GetAwaiter().GetResult() &&
                      latestValue == 42,
                    "a retired scan failure is suppressed without interfering with the newer published result");

                var currentRequest = owner.RunLatestAsync<int>(_ => throw new IOException("current scan failure"),
                    context, _ => throw new Exception("failed current scan must not publish"));
                PumpDispatcherUntil(() => currentRequest.IsCompleted);
                var currentFailurePropagated = false;
                try { _ = currentRequest.GetAwaiter().GetResult(); }
                catch (IOException ex) when (ex.Message == "current scan failure")
                { currentFailurePropagated = true; }
                Check(currentFailurePropagated,
                    "a current scan failure still propagates for owner-thread error handling");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Retired worker failure smoke failed.", failure);
    }

    private static void PumpDispatcherUntil(Task task)
        => PumpDispatcherUntil(() => task.IsCompleted, task);

    private static void PumpDispatcherUntil(Func<bool> isComplete, Task? task = null)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) => { if (isComplete()) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        task?.GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private readonly ManualResetEventSlim _posted = new();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue((callback, state));
            _posted.Set();
        }

        internal bool WaitForPost(TimeSpan timeout) => _posted.Wait(timeout);

        internal void RunPostedCallback()
        {
            if (!_callbacks.TryDequeue(out var work))
                throw new InvalidOperationException("No owner callback is queued.");
            work.Callback(work.State);
        }

        public void Dispose() => _posted.Dispose();
    }
}
