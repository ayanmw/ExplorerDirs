using ExplorerDirs.App;

internal static class DirectoryMonitorStartupSmoke
{
    internal static void Run()
    {
        var queue = new ExplorerDirs.App.ShellDirectoryChangeQueue();
        var request = new ExplorerDirs.App.ShellDirectoryMonitorRequest(3, 7, @"C:\current", queue);
        Check(request.IsCurrent(3, 7, viewIsCurrent: true, @"C:\current", queue),
            "a startup sample may be applied while its monitor, navigation, path and queue are still current");
        Check(!request.IsCurrent(4, 7, viewIsCurrent: true, @"C:\current", queue),
            "a reentrant navigation cannot apply a sample from a retired monitor generation");
        Check(!request.IsCurrent(3, 8, viewIsCurrent: true, @"C:\current", queue),
            "a stale request is rejected even if a newer view is currently ready");
        Check(!request.IsCurrent(3, 8, viewIsCurrent: false, @"C:\current", queue),
            "a reentrant navigation cannot apply a sample after the Shell view changes");
        Check(!request.IsCurrent(3, 7, viewIsCurrent: true, @"C:\other", queue),
            "a startup sample cannot apply to a different completed directory");
        Check(!request.IsCurrent(3, 7, viewIsCurrent: true, @"C:\current", new ExplorerDirs.App.ShellDirectoryChangeQueue()),
            "a startup sample cannot apply after a newer watcher queue replaces its queue");

        var currentAfterRead = true;
        Check(!ShellDirectoryMonitorCountRead.TryRead(() =>
            {
                currentAfterRead = false;
                return 99;
            }, () => currentAfterRead, out var staleCount) && staleCount == 99,
            "a Shell count read that reenters navigation is discarded before monitor state is updated");
        Check(ShellDirectoryMonitorCountRead.TryRead(() => 12, () => true, out var currentCount) && currentCount == 12,
            "a count read from the still-current monitor is available for reconciliation");

        var reportedReadFailure = false;
        Check(!ShellDirectoryMonitorStartup.TryReadCount(
                () => throw new InvalidOperationException("count unavailable"), out var failedCount,
                error => reportedReadFailure = error is InvalidOperationException) &&
              failedCount is null && reportedReadFailure,
            "an exceptional startup count read is reported and can be retried without escaping the timer");

        var startup = new ShellDirectoryMonitorStartup();
        Check(startup.NextIntervalMilliseconds == 250, "startup uses a short interval while Shell count settles");
        for (var sample = 0; sample < 4; sample++)
            Check(startup.Observe(12) is null, "startup does not publish a baseline before five stable counts");
        Check(startup.Observe(12) == 12, "five identical Shell counts establish the initial baseline");

        var reset = new ShellDirectoryMonitorStartup();
        reset.Observe(12);
        reset.Observe(12);
        reset.Observe(null);
        reset.Observe(12);
        Check(reset.Observe(12) is null, "an unreadable count resets the stable sample sequence");

        var slowEnumeration = new ShellDirectoryMonitorStartup();
        for (var sample = 0; sample < 40; sample++)
            Check(slowEnumeration.Observe(12 + sample % 2) is null,
                "an unstable Shell count does not end startup after the former retry limit");
        Check(slowEnumeration.NextIntervalMilliseconds == 1000,
            "startup backs off to one-second sampling after forty fast checks");
        for (var sample = 0; sample < 4; sample++)
            Check(slowEnumeration.Observe(30) is null, "slow enumeration continues waiting for a stable count");
        Check(slowEnumeration.Observe(30) == 30,
            "a directory that settles after the fast window can still establish its baseline");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Directory monitor startup: {label}");
    }
}
