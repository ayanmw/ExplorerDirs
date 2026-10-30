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

        Console.WriteLine("Directory tree scan coordinator smoke: PASS");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
