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

        Check(!batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out _),
            "a delete burst waits for the full quiet period");
        Thread.Sleep(150);
        Check(!batch.TryRecordForDispatch(@"C:\files\last-folder-updated"),
            "a new delete during the quiet period reuses the pending handoff");
        Thread.Sleep(160);
        Check(!batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out _),
            "a new delete restarts the quiet period without another dispatcher callback");
        Thread.Sleep(120);
        Check(batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out var latest) &&
              latest == @"C:\files\last-folder-updated",
            "the quiet-period drain returns the newest path and clears pending state");
        Check(batch.TryRecordForDispatch(@"C:\files\next-burst"),
            "a subsequent delete burst queues exactly one new handoff");
        Check(ShellDeletedItemNotification.ShouldNotifyPath(@"C:\files\removed-folder", isFolder: true),
            "a confirmed folder path can use the RMDIR notification");
        Check(!ShellDeletedItemNotification.ShouldNotifyPath(@"C:\files\removed-file.txt", isFolder: false),
            "a file path is not sent as a RMDIR notification");
        Check(!ShellDeletedItemNotification.ShouldNotifyPath(@"relative-folder", isFolder: true),
            "relative folder paths are excluded from RMDIR notifications");
        Check(!ShellDeletedItemNotification.ShouldNotifyPath(new string('x', 260), isFolder: true),
            "paths at or beyond MAX_PATH are excluded from RMDIR notifications");
        Check(ShellDeletedItemNotification.CanScanViewForDeletedFolder(512, 512),
            "deleted-folder identity scan is allowed at the bounded small-view limit");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(513, 513),
            "deleted-folder identity scan is skipped above the bounded small-view limit");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(null, 3),
            "deleted-folder identity scan is skipped when the Shell view count is unknown");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(0, 10003),
            "a transient empty Shell count cannot authorize scanning a large logical view");
        Check(!ShellDeletedItemNotification.CanScanViewForDeletedFolder(0, 0),
            "an empty view skips an identity scan with no candidate rows");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
