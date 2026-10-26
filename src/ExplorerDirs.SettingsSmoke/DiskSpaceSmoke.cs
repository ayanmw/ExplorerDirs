using ExplorerDirs.App;
using System.IO;

internal static class DiskSpaceSmoke
{
    internal static async Task Run(string root)
    {
        Check(new DiskSpaceInfo(60, 100).DisplayText == "60% · 60 B 可用" &&
            new DiskSpaceInfo(0, 100).DisplayText == "0% · 0 B 可用" &&
            new DiskSpaceInfo(100, 100).DisplayText == "100% · 100 B 可用" &&
            new DiskSpaceInfo(1, 8).DisplayText == "13% · 1 B 可用" &&
            new DiskSpaceInfo(0, 0).DisplayText == "0 B 可用",
            "status text includes a rounded available percentage when total capacity is known");

        // Removing stale-completion checks lets an old directory replace new data.
        var oldRead = new TaskCompletionSource<DiskSpaceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRead = new TaskCompletionSource<DiskSpaceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long now = 0;
        var reads = 0;
        var tracker = new DiskSpaceTracker((path, _) =>
        {
            reads++;
            return path == @"C:\old" ? oldRead.Task : newRead.Task;
        }, () => now);
        var first = tracker.UpdateAsync(@"C:\old");
        var second = tracker.UpdateAsync(@"D:\new");
        newRead.SetResult(new(60, 100));
        await second;
        oldRead.SetResult(new(10, 20));
        await first;
        Check(tracker.Snapshot.Path == @"D:\new" && tracker.Snapshot.Info == new DiskSpaceInfo(60, 100),
            "late old directory cannot overwrite current capacity");
        await tracker.UpdateAsync(@"D:\new");
        Check(reads == 2, "timer ticks inside refresh interval do not repeat I/O");
        now = 10_001;
        await tracker.UpdateAsync(@"D:\new");
        Check(reads == 3, "current directory is refreshed after interval");

        var pending = new TaskCompletionSource<DiskSpaceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker = new DiskSpaceTracker((_, _) => pending.Task);
        var hidden = tracker.UpdateAsync(@"C:\hidden");
        tracker.Reset();
        pending.SetResult(new(1, 5));
        await hidden;
        Check(tracker.Snapshot == new DiskSpaceSnapshot(null, false, null),
            "hidden pane stays cleared even if query ignores cancellation");

        tracker = new DiskSpaceTracker((_, _) =>
            Task.FromException<DiskSpaceInfo?>(new OperationCanceledException("reader cancelled independently")));
        await tracker.UpdateAsync(@"C:\reader-cancelled");
        Check(tracker.Snapshot == new DiskSpaceSnapshot(@"C:\reader-cancelled", false, null),
            "an independent reader cancellation settles capacity as unavailable");

        // Cancellation must not release a slot occupied by an uncancellable OS call.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopped = new CancellationTokenSource();
        var calls = 0;
        var reader = new DiskSpaceReader(_ =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            if (!release.Task.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Test query timed out.");
            return new(5, 9);
        }, concurrency: 1, timeout: TimeSpan.FromSeconds(2));
        try
        {
            var blocked = reader.ReadAsync(@"C:\blocked", stopped.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stopped.Cancel();
            try { await blocked; throw new Exception("Cancelled read unexpectedly returned."); }
            catch (OperationCanceledException) { }
            Check(await reader.ReadAsync(@"D:\next", default).WaitAsync(TimeSpan.FromSeconds(3)) is null,
                "waiting for an occupied slot has a deadline too");
            Check(calls == 1, "cancelled worker retains slot; queued timeout does not start more I/O");
        }
        finally { release.TrySetResult(); }
        Check(await reader.ReadAsync(@"C:\resumed", default) == new DiskSpaceInfo(5, 9),
            "actual worker completion releases capacity for new requests");

        var slowRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader = new DiskSpaceReader(_ =>
        {
            slowRelease.Task.Wait(TimeSpan.FromSeconds(10));
            return new(2, 4);
        }, timeout: TimeSpan.FromMilliseconds(200));
        try
        {
            Check(await reader.ReadAsync(@"C:\slow", default) is null,
                "slow query returns unavailable within bounded waiting time");
        }
        finally { slowRelease.TrySetResult(); }

        string? requested = null;
        reader = new DiskSpaceReader(path => { requested = path; return new(0, 1UL << 42); });
        var capacity = await reader.ReadAsync(@"\\server\share\mounted", default);
        Check(requested == @"\\server\share\mounted\" && capacity?.TotalBytes == 1UL << 42 &&
            capacity.AvailableBytes == 0, "directory retained with UNC separator; zero-free and 64-bit capacity retained");
        requested = null;
        Check(await reader.ReadAsync("search-ms:query=test", default) is null && requested is null,
            "virtual locations never reach disk API");
        Check(await new DiskSpaceReader(_ => throw new IOException("offline"))
            .ReadAsync(@"\\server\offline", default) is null, "failed query remains unknown, not zero");

        var actual = await DiskSpaceReader.Shared.ReadAsync(Path.GetFullPath(root), default);
        Check(actual is { TotalBytes: > 0 } && actual.AvailableBytes <= actual.TotalBytes,
            "real local disk query returns plausible caller capacity");
    }
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("Disk space: " + message);
    }
}
