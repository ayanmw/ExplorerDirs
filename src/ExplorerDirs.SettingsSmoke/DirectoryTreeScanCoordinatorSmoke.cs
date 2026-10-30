using System.Collections.Concurrent;
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
        VerifyQueuedStalePublication();

        Console.WriteLine("Directory tree scan coordinator smoke: PASS");
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

    private static void PumpDispatcherUntil(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        task.GetAwaiter().GetResult();
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
