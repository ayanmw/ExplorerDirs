using ExplorerDirs.App;

internal static class ShellChangedItemNotificationBatchSmoke
{
    internal static void Run()
    {
        var basic = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        bool RequiresDirectoryUpdate(ViewState view) => ShellChangedItemNotification.RequiresDirectoryUpdate(view);
        Check(RequiresDirectoryUpdate(ViewState.Default() with
            { SortColumns = [new(new(basic, 12), 1)] }),
            "Changed requires a directory update when sorting by size");
        Check(RequiresDirectoryUpdate(ViewState.Default() with
            { SortColumns = [new(new(basic, 10), 1), new(new(basic, 14), -1)] }),
            "Changed checks every key in a compound sort");
        Check(RequiresDirectoryUpdate(ViewState.Default() with
            { SortColumns = [new(new(basic, 13), -1)] }),
            "Changed requires a directory update when sorting by attributes");
        Check(RequiresDirectoryUpdate(ViewState.Default() with { GroupKey = new(basic, 15) }),
            "Changed requires a directory update when grouping by creation time");
        Check(!RequiresDirectoryUpdate(ViewState.Default() with
            { SortColumns = [new(new(Guid.NewGuid(), 12), 1)] }),
            "Changed metadata keys are matched only in the Shell basic property set");
        Check(!RequiresDirectoryUpdate(ViewState.Default() with
            { SortColumns = [new(new(basic, 10), 1)], GroupKey = new(basic, 4) }),
            "name sorting and type grouping do not require a Changed directory update");

        var batch = new ShellChangedItemNotificationBatch(maximumDistinctPaths: 2);
        Check(batch.TryRecordForDispatch(@"C:\files\first.txt", out var fallback) && !fallback,
            "the first changed path queues one dispatcher handoff");
        Check(!batch.TryRecordForDispatch(@"C:\files\first.txt", out fallback) && !fallback,
            "repeated changes to the same item reuse the pending handoff");
        Check(!batch.TryRecordForDispatch(@"C:\files\second.txt", out fallback) && !fallback,
            "additional changed items coalesce into the pending handoff");
        Check(!batch.TryTakeAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out var early, out var earlyFallback) &&
              early is null && !earlyFallback,
            "changed item notifications wait for the complete quiet period");
        Thread.Sleep(270);
        Check(batch.TryTakeAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out var paths, out fallback) &&
              !fallback && paths is [@"C:\files\first.txt", @"C:\files\second.txt"],
            "the quiet-period drain returns each distinct changed item once");

        var overflow = new ShellChangedItemNotificationBatch(maximumDistinctPaths: 2);
        overflow.TryRecordForDispatch(@"C:\files\one.txt", out _);
        overflow.TryRecordForDispatch(@"C:\files\two.txt", out _);
        Check(!overflow.TryRecordForDispatch(@"C:\files\three.txt", out _),
            "a batch beyond its item limit does not queue additional dispatcher work");
        Check(overflow.TryTakeAfterQuietPeriod(TimeSpan.Zero, out var overflowPaths, out var requiresFallback) &&
              overflowPaths is null && requiresFallback,
            "overflow discards the partial list and requests one full-refresh fallback");
        Check(!ShellChangedItemNotification.ShouldNotifyPath("relative.txt") &&
              !ShellChangedItemNotification.ShouldNotifyPath("C:\\" + new string('x', 259)) &&
              ShellChangedItemNotification.ShouldNotifyPath(@"C:\files\changed.txt"),
            "only absolute paths shorter than MAX_PATH can use a per-item Shell notification");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
