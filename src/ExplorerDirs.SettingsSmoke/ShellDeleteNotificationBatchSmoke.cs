using ExplorerDirs.App;
using System.Diagnostics;

internal static class ShellDeleteNotificationBatchSmoke
{
    public static void Run()
    {
        var batch = new ShellDeleteNotificationBatch();
        Check(batch.TakeForDispatcher() is null, "an empty native delete notification batch has no pending path");
        Check(batch.TryRecordForDispatch(@"C:\files\first.txt"), "the first delete queues one dispatcher handoff");
        Check(!batch.TryRecordForDispatch(@"C:\files\middle.txt"), "later deletes coalesce into the pending handoff");
        Check(!batch.TryRecordForDispatch(@"C:\files\last-folder"), "a delete burst does not queue per-item dispatcher work");
        Check(batch.TakeForDispatcher() == @"C:\files\last-folder",
            "the dispatcher receives the latest path for one Shell identity lookup");

        Check(!batch.TryTakeAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out _),
            "a delete burst waits for the full quiet period");
        Thread.Sleep(150);
        Check(!batch.TryRecordForDispatch(@"C:\files\last-folder-updated"),
            "a new delete during the quiet period reuses the pending handoff");
        Thread.Sleep(160);
        Check(!batch.TryTakeAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out _),
            "a new delete restarts the quiet period without another dispatcher callback");
        Thread.Sleep(120);
        Check(batch.TryTakeAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out var latest) &&
              latest.SequenceEqual(new[] { @"C:\files\first.txt", @"C:\files\middle.txt", @"C:\files\last-folder", @"C:\files\last-folder-updated" }),
            "the quiet-period drain preserves all distinct deleted paths and clears pending state");
        Check(batch.TryRecordForDispatch(@"C:\files\next-burst"),
            "a subsequent delete burst queues exactly one new handoff");
        var duplicates = new ShellDeleteNotificationBatch();
        Check(duplicates.TryRecordForDispatch(@"C:\files\same.txt") &&
              !duplicates.TryRecordForDispatch(@"c:\FILES\SAME.txt") &&
              duplicates.TryTakeAfterQuietPeriod(TimeSpan.Zero, out var unique) && unique.Count == 1,
            "case-insensitive duplicate events share one retained path and one handoff");
        var bounded = new ShellDeleteNotificationBatch();
        var handoffs = 0;
        for (var index = 0; index < ShellDeleteNotificationBatch.MaximumPendingPaths + 17; index++)
            if (bounded.TryRecordForDispatch($@"C:\files\{index}.txt")) handoffs++;
        Check(handoffs == 1, "overflow still queues only one dispatcher handoff");
        Check(bounded.TryTakeAfterQuietPeriod(TimeSpan.Zero, out var retained) &&
              retained.Count == ShellDeleteNotificationBatch.MaximumPendingPaths &&
              retained[0] == @"C:\files\17.txt" && retained[^1] == @"C:\files\528.txt",
            "overflow retains the newest bounded distinct paths without unbounded memory");
        Check(ShellDeletedItemNotification.ShouldNotifyPath(@"C:\files\removed-folder"),
            "a confirmed folder path can use the RMDIR notification");
        Check(ShellDeletedItemNotification.ShouldNotifyPath(@"C:\files\removed-file.txt"),
            "a confirmed file path is eligible for a type-specific deletion notification");
        Check(!ShellDeletedItemNotification.ShouldNotifyPath(@"relative-folder"),
            "relative folder paths are excluded from RMDIR notifications");
        Check(!ShellDeletedItemNotification.ShouldNotifyPath(new string('x', 260)),
            "paths at or beyond MAX_PATH are excluded from RMDIR notifications");
        Check(ShellDeletedItemNotification.CanScanViewForDeletedFolder(512, 512),
            "deleted-folder identity scan is allowed at the bounded small-view limit");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(513, 513),
            "deleted-folder identity scan is skipped above the bounded small-view limit");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(null, 3),
            "deleted-folder identity scan is skipped when the Shell view count is unknown");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(0, 10003),
            "a transient empty Shell count cannot authorize scanning a large logical view");
        Check(ShellDeletedItemNotification.CanScanViewForDeletedFolder(3, null, softRefresh: true),
            "soft delete can repair a retained row before startup count sampling finishes");
        Check(ShellDeletedItemNotification.CanScanViewForDeletedFolder(1, 0, softRefresh: true),
            "soft delete can remove the last retained folder without a positive target count");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(513, null, softRefresh: true),
            "soft repair retains the native identity lookup work bound");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(0, 0),
            "an empty view skips an identity scan with no candidate rows");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
