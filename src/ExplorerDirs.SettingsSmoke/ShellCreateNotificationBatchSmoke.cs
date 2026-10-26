using ExplorerDirs.App;

internal static class ShellCreateNotificationBatchSmoke
{
    public static void Run()
    {
        var batch = new ShellCreateNotificationBatch();
        Check(batch.TakeLatest() is null, "an empty native create notification batch is empty");
        Check(batch.TryRecordForDispatch(@"C:\files\first.txt"), "the first create schedules dispatcher work");
        Check(!batch.TryRecordForDispatch(@"C:\files\last.txt"), "later creates coalesce before dispatcher work");
        Check(batch.TakeForDispatcher() == @"C:\files\last.txt", "the dispatcher receives the latest path from a create burst");
        Check(!batch.TryRecordForDispatch(@"C:\files\delayed-first.txt"),
            "a create during the notification window does not schedule extra dispatcher work");
        Thread.Sleep(550);
        Check(!batch.TryRecordForDispatch(@"C:\files\delayed-last.txt"),
            "creates during the notification window coalesce to their latest path");
        Check(!batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(500), out _),
            "a recent create restarts the notification quiet period");
        Thread.Sleep(550);
        Check(batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(500), out var delayedPath) &&
              delayedPath == @"C:\files\delayed-last.txt",
            "the delayed Shell notification consumes the latest path after the quiet period");
        Check(batch.TryRecordForDispatch(@"C:\files\next.txt"), "a drained create burst allows a later dispatcher callback");
        Check(batch.TakeForDispatcher() == @"C:\files\next.txt", "a later create path is preserved after draining");
        Check(batch.TakeLatest() == @"C:\files\next.txt",
            "dispatcher handoff preserves the representative path until the delayed Shell notification");
        Check(batch.TryRecordForDispatch(@"C:\files\first.txt"), "a new create schedules dispatch after the previous batch drains");
        Check(!batch.TryRecordForDispatch(@"C:\files\last.txt"), "a create burst coalesces to one representative path");
        Check(batch.TakeLatest() == @"C:\files\last.txt",
            "taking a create burst returns the latest representative path");
        Check(batch.TakeLatest() is null, "draining a create burst clears its pending path");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
