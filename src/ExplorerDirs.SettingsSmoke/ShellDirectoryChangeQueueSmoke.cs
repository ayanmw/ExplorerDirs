using System.Threading;
using System.Threading.Tasks;

internal static class ShellDirectoryChangeQueueSmoke
{
    internal static void Run()
    {
        var queue = new ExplorerDirs.App.ShellDirectoryChangeQueue();
        var scheduled = 0;
        Parallel.For(0, 3000, _ =>
        {
            if (queue.Enqueue(1, forceRefresh: false)) Interlocked.Increment(ref scheduled);
        });
        Check(scheduled == 1, "one dispatcher operation is queued for a burst of 3000 events");
        var batch = queue.DrainWhileDispatchPending();
        Check(batch.Delta == 3000 && !batch.ForceRefresh,
            "the dispatcher handoff drains the complete net change while keeping the timer window active");

        var replacementQueue = new ExplorerDirs.App.ShellDirectoryChangeQueue();
        Check(replacementQueue.Enqueue(1, forceRefresh: false), "a created item starts the replacement batch");
        var replacementCreate = replacementQueue.DrainWhileDispatchPending();
        Check(replacementCreate.Delta == 1 && !replacementCreate.ForceRefresh,
            "a create-only handoff does not force a refresh");
        Check(!replacementQueue.Enqueue(-1, forceRefresh: false),
            "a delete joins the active create dispatch window");
        var replacementDelete = replacementQueue.Drain();
        Check(replacementDelete.Delta == -1 && replacementDelete.ForceRefresh,
            "a balanced create/delete window preserves identity-change refresh intent");

        Check(!queue.Enqueue(10, forceRefresh: false), "changes during the timer window do not queue another dispatcher handoff");
        Check(!queue.Enqueue(-2, forceRefresh: true), "forced refresh state also coalesces during the timer window");
        batch = queue.DrainWhileDispatchPending();
        Check(batch.Delta == 8 && batch.ForceRefresh,
            "the next timer drain receives changes accumulated during the active window");

        Check(!queue.Enqueue(-3000, forceRefresh: false), "additional changes remain on the active dispatcher window");
        batch = queue.Drain();
        Check(batch.Delta == -3000 && batch.ForceRefresh,
            "the timer drain preserves identity-change refresh intent across earlier dispatcher drains");
        Check(queue.Enqueue(1, forceRefresh: true), "a new timer window can queue after the prior window drains");
        batch = queue.Drain();
        Check(batch.Delta == 1 && batch.ForceRefresh,
            "the new window retains its own delta and forced refresh state");

        var concurrentQueue = new ExplorerDirs.App.ShellDirectoryChangeQueue();
        using var startConcurrentBurst = new ManualResetEventSlim(false);
        using var producerPaused = new ManualResetEventSlim(false);
        using var firstOverlapDrain = new ManualResetEventSlim(false);
        var producersRemaining = 4;
        var drainedDelta = 0;
        var drainedForceRefresh = 0;
        var overlappingDrains = 0;
        var consumer = new Thread(() =>
        {
            startConcurrentBurst.Wait();
            if (!producerPaused.Wait(TimeSpan.FromSeconds(15))) return;
            while (Volatile.Read(ref producersRemaining) > 0)
            {
                var changes = concurrentQueue.Drain();
                Interlocked.Add(ref drainedDelta, changes.Delta);
                if (changes.ForceRefresh) Interlocked.Exchange(ref drainedForceRefresh, 1);
                if (changes.Delta != 0 && Volatile.Read(ref producersRemaining) > 0)
                {
                    Interlocked.Increment(ref overlappingDrains);
                    firstOverlapDrain.Set();
                }
                Thread.Yield();
            }
            var finalChanges = concurrentQueue.Drain();
            Interlocked.Add(ref drainedDelta, finalChanges.Delta);
            if (finalChanges.ForceRefresh) Interlocked.Exchange(ref drainedForceRefresh, 1);
        });
        consumer.Start();
        var producers = Enumerable.Range(0, producersRemaining).Select(producerIndex => Task.Run(() =>
        {
            startConcurrentBurst.Wait();
            try
            {
                for (var index = 0; index < 5000; index++)
                {
                    concurrentQueue.Enqueue(1, forceRefresh: index % 97 == 0);
                    if (producerIndex == 0 && index == 99)
                    {
                        producerPaused.Set();
                        if (!firstOverlapDrain.Wait(TimeSpan.FromSeconds(15)))
                            throw new TimeoutException("The queue consumer did not drain while a producer was held active.");
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref producersRemaining);
            }
        })).ToArray();
        startConcurrentBurst.Set();
        Task.WaitAll(producers);
        consumer.Join();
        Check(drainedDelta == 20_000,
            "concurrent watcher producers and timer drains preserve every event delta");
        Check(drainedForceRefresh == 1,
            "concurrent watcher producers and timer drains preserve forced-refresh intent");
        Check(overlappingDrains > 0,
            "the queue drains a nonempty batch while a producer is deliberately held active");

        queue.Invalidate();
        Check(!queue.Enqueue(1, forceRefresh: false), "invalidated monitor generations reject late events");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell directory event queue: {label}");
    }
}
