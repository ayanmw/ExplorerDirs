using ExplorerDirs.App;
using System.IO;

internal static class ShellDirectoryWatcherRecoverySmoke
{
    internal static void Run()
    {
        var recovery = new ShellDirectoryWatcherRecovery();
        var firstWatcher = new object();
        var replacementWatcher = new object();
        var eventGate = new ShellDirectoryWatcherEventGate<object>();
        eventGate.SetCurrent(firstWatcher);
        var acceptedEvents = 0;
        Require(eventGate.TryInvoke(firstWatcher, () => acceptedEvents++),
            "the active watcher event is accepted atomically");
        eventGate.SetCurrent(replacementWatcher);
        Require(!eventGate.TryInvoke(firstWatcher, () => acceptedEvents++) &&
                eventGate.TryInvoke(replacementWatcher, () => acceptedEvents++) && acceptedEvents == 2,
            "the replacement watcher exclusively owns later event callbacks");
        eventGate.SetCurrent(null);
        Require(!eventGate.TryInvoke(replacementWatcher, () => acceptedEvents++),
            "retiring the watcher closes the event gate");

        eventGate.SetCurrent(replacementWatcher);
        Exception? eventCallbackError = null;
        Require(!eventGate.TryInvoke(replacementWatcher,
                () => throw new IOException("watcher event queue failed"), error => eventCallbackError = error) &&
                eventCallbackError is IOException { Message: "watcher event queue failed" },
            "an active watcher callback failure is contained and reported to its recovery handler");
        Require(!eventGate.TryInvoke(replacementWatcher,
                () => throw new IOException("watcher event queue failed"),
                _ => throw new IOException("watcher recovery scheduling failed")),
            "a watcher recovery observer failure cannot escape the FileSystemWatcher callback");

        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        var callbackAccepted = false;
        var callbackThread = new Thread(() => callbackAccepted = eventGate.TryInvoke(firstWatcher, () =>
        {
            callbackEntered.Set();
            if (!releaseCallback.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The watcher event gate callback was not released.");
        })) { IsBackground = true };
        eventGate.SetCurrent(firstWatcher);
        callbackThread.Start();
        Require(callbackEntered.Wait(TimeSpan.FromSeconds(2)),
            "an active watcher callback can hold the event gate while it queues work");

        using var replacementStarted = new ManualResetEventSlim();
        using var replacementCompleted = new ManualResetEventSlim();
        Thread? replacementThread = null;
        var setterThread = new Thread(() =>
        {
            replacementThread = Thread.CurrentThread;
            replacementStarted.Set();
            eventGate.SetCurrent(replacementWatcher);
            replacementCompleted.Set();
        }) { IsBackground = true };
        setterThread.Start();
        var replacementBlockedByCallback = false;
        try
        {
            Require(replacementStarted.Wait(TimeSpan.FromSeconds(2)),
                "watcher replacement starts while an old callback is active");
            replacementBlockedByCallback = SpinWait.SpinUntil(
                () => replacementThread is not null &&
                      (replacementThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(2));
        }
        finally { releaseCallback.Set(); }

        Require(callbackThread.Join(TimeSpan.FromSeconds(2)) && setterThread.Join(TimeSpan.FromSeconds(2)) &&
                callbackAccepted && replacementBlockedByCallback && replacementCompleted.IsSet &&
                !eventGate.TryInvoke(firstWatcher, () => acceptedEvents++),
            "replacement waits for an in-flight callback, then rejects later callbacks from that watcher");

        Require(recovery.TryBegin(out var initialDelay) && initialDelay == TimeSpan.FromMilliseconds(250),
            "the first watcher recovery waits for the 250 ms event-coalescing window");
        Require(!recovery.TryBegin(out _), "multiple watcher errors share one pending recovery attempt");

        recovery.Complete(recovered: false);
        Require(recovery.TryBegin(out var secondDelay) && secondDelay == TimeSpan.FromMilliseconds(500),
            "a failed watcher recovery backs off before retrying");
        recovery.Complete(recovered: true);
        Require(recovery.TryBegin(out var resetDelay) && resetDelay == TimeSpan.FromMilliseconds(250),
            "a recovered watcher resets the retry delay");
        recovery.Complete(recovered: false);

        var expectedDelay = 500;
        while (expectedDelay < 30000)
        {
            Require(recovery.TryBegin(out var delay) && delay == TimeSpan.FromMilliseconds(expectedDelay),
                "recovery retries increase their delay exponentially");
            recovery.Complete(recovered: false);
            expectedDelay = Math.Min(expectedDelay * 2, 30000);
        }
        Require(recovery.TryBegin(out var cappedDelay) && cappedDelay == TimeSpan.FromMilliseconds(30000),
            "recovery retry delay is capped at 30 seconds");
        recovery.Complete(recovered: false);
        recovery.Retire();
        Require(!recovery.TryBegin(out _), "stopping a monitor retires its pending recovery policy");

        var retiredWhilePending = new ShellDirectoryWatcherRecovery();
        Require(retiredWhilePending.TryBegin(out _), "a recovery attempt can be pending when navigation retires its monitor");
        retiredWhilePending.Retire();
        retiredWhilePending.Complete(recovered: false);
        Require(!retiredWhilePending.TryBegin(out _),
            "a late failed callback cannot rearm recovery after its monitor was retired");

        var callbackRecovery = new ShellDirectoryWatcherRecovery();
        Require(callbackRecovery.TryBegin(out _), "a recovery timer callback begins with a pending attempt");
        Require(!ShellDirectoryWatcherRecovery.TryRunCallback(
                () => throw new IOException("recovery timer setup failed"),
                _ => callbackRecovery.Complete(recovered: false)) &&
              callbackRecovery.TryBegin(out var callbackRetryDelay) &&
              callbackRetryDelay == TimeSpan.FromMilliseconds(500),
            "a failed recovery timer callback clears pending state and allows a backed-off retry");
        callbackRecovery.Complete(recovered: false);

        var reporterFailureRecovery = new ShellDirectoryWatcherRecovery();
        Require(reporterFailureRecovery.TryBegin(out _), "a recovery attempt can be pending before reporter failure");
        Require(!ShellDirectoryWatcherRecovery.TryRunCallback(
                () => throw new IOException("recovery timer setup failed"),
                _ => { reporterFailureRecovery.Complete(recovered: false); throw new IOException("report failed"); }) &&
              reporterFailureRecovery.TryBegin(out var reporterRetryDelay) &&
              reporterRetryDelay == TimeSpan.FromMilliseconds(500),
            "a throwing recovery error observer cannot strand the retry state");
        reporterFailureRecovery.Complete(recovered: false);

        var cleanupOrderingRecovery = new ShellDirectoryWatcherRecovery();
        Require(cleanupOrderingRecovery.TryBegin(out _), "timer cleanup failure starts with a pending recovery");
        cleanupOrderingRecovery.CompleteFailedAttempt(() => throw new IOException("timer stop failed"));
        Require(cleanupOrderingRecovery.TryBegin(out var cleanupRetryDelay) &&
                cleanupRetryDelay == TimeSpan.FromMilliseconds(500),
            "a timer cleanup exception cannot strand the pending recovery attempt");
        cleanupOrderingRecovery.Complete(recovered: false);

        var dispatchRetryRecovery = new ShellDirectoryWatcherRecovery();
        Require(dispatchRetryRecovery.TryBegin(out _), "dispatcher failure starts with one pending attempt");
        dispatchRetryRecovery.Complete(recovered: false);
        var dispatchRetryStarted = false;
        var dispatchRetryDelay = TimeSpan.Zero;
        ShellDirectoryWatcherRecovery.RetryAfterDispatchFailureAsync(TimeSpan.FromMilliseconds(1), () =>
        {
            dispatchRetryStarted = dispatchRetryRecovery.TryBegin(out dispatchRetryDelay);
        }).GetAwaiter().GetResult();
        Require(dispatchRetryStarted && dispatchRetryDelay == TimeSpan.FromMilliseconds(500),
            "a transient dispatcher enqueue failure retries through the existing backoff policy");
        dispatchRetryRecovery.Complete(recovered: false);

        VerifyRetirementFencesRealFileSystemWatcherEvents();
    }

    private static void VerifyRetirementFencesRealFileSystemWatcherEvents()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-WatcherGate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var gate = new ShellDirectoryWatcherEventGate<FileSystemWatcher>();
            using var acceptedEvent = new ManualResetEventSlim();
            using var retiredEvent = new ManualResetEventSlim();
            var accepted = 0;
            var delivered = 0;
            using var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false
            };
            watcher.Created += (_, _) =>
            {
                Interlocked.Increment(ref delivered);
                if (gate.TryInvoke(watcher, () =>
                    {
                        Interlocked.Increment(ref accepted);
                        acceptedEvent.Set();
                    })) return;
                retiredEvent.Set();
            };

            gate.SetCurrent(watcher);
            watcher.EnableRaisingEvents = true;
            File.WriteAllText(Path.Combine(folder, "active.txt"), "active");
            Require(acceptedEvent.Wait(TimeSpan.FromSeconds(5)) && Volatile.Read(ref accepted) == 1,
                "a real Windows FileSystemWatcher event is accepted while its watcher is current");

            // Production unpublishes a watcher before asynchronously disposing it. Keep this
            // watcher alive briefly to prove an event delivered in that window is fenced out.
            gate.SetCurrent(null);
            File.WriteAllText(Path.Combine(folder, "retired.txt"), "retired");
            Require(retiredEvent.Wait(TimeSpan.FromSeconds(5)) && Volatile.Read(ref delivered) == 2 &&
                    Volatile.Read(ref accepted) == 1,
                "a real event delivered after watcher retirement is rejected before it can queue a directory change");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Shell watcher recovery: {message}");
    }
}
