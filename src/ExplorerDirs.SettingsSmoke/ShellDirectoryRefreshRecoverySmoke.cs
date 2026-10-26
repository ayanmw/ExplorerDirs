using ExplorerDirs.App;

using System.IO;

internal static class ShellDirectoryRefreshRecoverySmoke
{
    internal static void Run()
    {
        var failureCount = 0;
        Require(ShellDirectoryRefreshRecovery.TryInvoke(() => { }, _ => failureCount++),
            "a successful manual refresh is not reported as a failure");
        Require(!ShellDirectoryRefreshRecovery.TryInvoke(() => throw new IOException("private path"), ex =>
                {
                    if (ex is IOException) failureCount++;
                }) && failureCount == 1,
            "a synchronous Shell refresh exception is reported without escaping its dispatcher action");
        var monitorTickFailure = 0;
        Require(!ShellDirectoryRefreshRecovery.TryRunMonitorTick(
                () => throw new InvalidOperationException("count read failed"),
                ex => { if (ex is InvalidOperationException) monitorTickFailure++; }) && monitorTickFailure == 1,
            "a directory monitor tick failure is routed to refresh recovery instead of escaping the dispatcher");
        var monitorRecoveryObserverEscaped = false;
        try
        {
            ShellDirectoryRefreshRecovery.TryRunMonitorTick(
                () => throw new InvalidOperationException("count read failed"),
                _ => throw new IOException("timer restart failed"));
        }
        catch (IOException) { monitorRecoveryObserverEscaped = true; }
        Require(!monitorRecoveryObserverEscaped,
            "a directory monitor recovery callback failure cannot escape the Dispatcher timer");
        var monitorRecoveryRetryScheduled = false;
        ShellDirectoryRefreshRecovery.RetryAfterRecoveryFailureAsync(TimeSpan.FromMilliseconds(1),
            () => monitorRecoveryRetryScheduled = true, _ => { }).GetAwaiter().GetResult();
        Require(monitorRecoveryRetryScheduled,
            "a failed monitor timer recovery can be retried after its bounded backoff");
        var monitorRetryObserverEscaped = false;
        try
        {
            ShellDirectoryRefreshRecovery.RetryAfterRecoveryFailureAsync(TimeSpan.FromMilliseconds(1),
                () => throw new IOException("dispatcher unavailable"),
                _ => throw new IOException("retry scheduler failed")).GetAwaiter().GetResult();
        }
        catch (IOException) { monitorRetryObserverEscaped = true; }
        Require(!monitorRetryObserverEscaped,
            "a retry scheduler failure cannot become an unobserved monitor task exception");
        var notificationRecoveryRuns = 0;
        Require(!ShellDirectoryNotificationRecovery.TryRunTimerTick(
                () => throw new IOException("Shell notification failed"),
                ex => { if (ex is IOException) notificationRecoveryRuns++; }) && notificationRecoveryRuns == 1,
            "a Shell notification timer failure is routed to one recovery callback");
        Require(!ShellDirectoryNotificationRecovery.TryRunTimerTick(
                () => throw new IOException("Shell notification failed"),
                _ => throw new IOException("refresh queue failed")),
            "a notification recovery callback failure cannot escape the Dispatcher timer");
        Require(ShellDirectoryRefreshRecovery.FailureForHResult(0, "Refresh") is null,
            "a successful Shell HRESULT is not treated as a refresh failure");
        var failedHResult = ShellDirectoryRefreshRecovery.FailureForHResult(unchecked((int)0x80004005), "Refresh");
        Require(failedHResult?.HResult == unchecked((int)0x80004005),
            "a failed Shell HRESULT becomes a retryable exception with its original code");

        var recovery = new ShellDirectoryRefreshRecovery();
        Require(recovery.RecordFailure() == TimeSpan.FromMilliseconds(500),
            "the first synchronous Shell refresh failure retries after a short delay");
        Require(recovery.RecordFailure() == TimeSpan.FromSeconds(1),
            "repeated Shell refresh failures back off exponentially");

        var delay = TimeSpan.Zero;
        for (var failure = 2; failure < 20; failure++) delay = recovery.RecordFailure();
        Require(delay == TimeSpan.FromSeconds(30) && recovery.RecordFailure() == TimeSpan.FromSeconds(30),
            "Shell refresh retry backoff is capped at 30 seconds");

        recovery.RecordSuccess();
        Require(recovery.RecordFailure() == TimeSpan.FromMilliseconds(500),
            "a successful Shell refresh resets the retry delay");

        var concurrentRecovery = new ShellDirectoryRefreshRecovery();
        var concurrentDelays = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 32, _ => concurrentDelays.Add(
            (int)concurrentRecovery.RecordFailure().TotalMilliseconds));
        var orderedConcurrentDelays = concurrentDelays.Order().ToArray();
        var expectedConcurrentDelays = new[] { 500, 1000, 2000, 4000, 8000, 16000 }
            .Concat(Enumerable.Repeat(30000, 26)).ToArray();
        Require(orderedConcurrentDelays.SequenceEqual(expectedConcurrentDelays),
            "concurrent refresh failures receive unique ordered backoff steps capped at 30 seconds");
        Console.WriteLine("Shell directory refresh recovery smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Shell directory refresh recovery: {message}");
        Console.WriteLine("PASS " + message);
    }
}
