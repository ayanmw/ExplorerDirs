namespace ExplorerDirs.NativeProbe;

internal static class ProjectedChangeLedgerSmoke
{
    internal static int Run()
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var visit = new ProjectedChangeLedger();
        var deleted = visit.Record(ProjectedChangeKind.Deleted, "same")!;
        var recreated = visit.Record(ProjectedChangeKind.Created, "same")!;
        Require(!visit.IsCurrent(deleted) && visit.IsCurrent(recreated), "Recreation did not supersede old work.");
        Require(visit.TryTake(out var first) && ReferenceEquals(first, deleted), "FIFO deletion was silently dropped.");
        visit.Release(deleted);
        Require(visit.IsCurrent(recreated), "Old completion erased recreated work.");
        Require(visit.TryTake(out var second) && ReferenceEquals(second, recreated), "FIFO creation was lost.");
        visit.Release(recreated);
        Require(visit.Snapshot == (0, 0, false), "Completed work retained per-path history.");

        var rename1 = visit.Record(ProjectedChangeKind.Renamed, "a", "b")!;
        var rename2 = visit.Record(ProjectedChangeKind.Renamed, "b", "c")!;
        Require(!visit.IsCurrent(rename1) && visit.IsCurrent(rename2), "Rename endpoints were not both versioned.");
        visit.TryTake(out _); visit.Release(rename1);
        Require(visit.IsCurrent(rename2), "Releasing an older rename erased its successor.");
        visit.TryTake(out _); visit.Release(rename2);
        Require(visit.Snapshot == (0, 0, false), "Superseded rename retained an orphan source key.");
        var caseRename = visit.Record(ProjectedChangeKind.Renamed, "case", "CASE")!;
        Require(caseRename.Name == "case" && caseRename.NewName == "CASE" && visit.IsCurrent(caseRename), "Case rename lost spelling.");
        visit.TryTake(out _); visit.Release(caseRename);
        Require(visit.Snapshot == (0, 0, false), "Case rename double-counted its path key.");

        var retired = visit.Record(ProjectedChangeKind.Changed, "old")!;
        visit.TryTake(out _);
        visit.Invalidate();
        var nextVisit = new ProjectedChangeLedger();
        var fresh = nextVisit.Record(ProjectedChangeKind.Changed, "old")!;
        Require(!visit.IsCurrent(retired) && !nextVisit.IsCurrent(retired), "Old visit work became current again.");
        nextVisit.Release(retired);
        Require(nextVisit.IsCurrent(fresh), "Old owner completed new owner work.");
        Require(visit.Record(ProjectedChangeKind.Created, "late") is null && visit.Snapshot == (0, 0, true), "Invalidated visit accepted work.");
        nextVisit.TryTake(out _); nextVisit.Release(fresh);

        var burst = new ProjectedChangeLedger();
        Parallel.For(0, 4096, index => burst.Record(ProjectedChangeKind.Changed, $"item-{index}"));
        long previous = 0; var count = 0;
        while (burst.TryTake(out var item))
        {
            Require(item.Sequence > previous && burst.IsCurrent(item), "Concurrent producer order/current work was corrupted.");
            previous = item.Sequence; count++; burst.Release(item);
        }
        Require(count == 4096 && burst.Snapshot == (0, 0, false), "Burst lost events or retained history.");
        var pending = burst.Record(ProjectedChangeKind.Deleted, "pending")!;
        burst.Invalidate();
        Require(!burst.TryTake(out _) && !burst.IsCurrent(pending) && burst.Snapshot == (0, 0, true), "Close retained queued work.");
        var bounded = new ProjectedChangeLedger(2);
        var inFlight = bounded.Record(ProjectedChangeKind.Renamed, "before", "after")!;
        bounded.TryTake(out _);
        bounded.Record(ProjectedChangeKind.Created, "second");
        Require(bounded.Record(ProjectedChangeKind.Created, "overflow") is null, "In-flight work did not count toward capacity.");
        Require(bounded.Overflowed && bounded.Snapshot == (0, 0, true) && !bounded.IsCurrent(inFlight), "Overflow silently lost work without invalidating the visit.");
        bounded.Release(inFlight);
        bounded.Invalidate();
        Require(bounded.Overflowed && bounded.Record(ProjectedChangeKind.Changed, "late") is null, "Late completion cleared the overflow state.");
        var reusable = new ProjectedChangeLedger(1);
        var completed = reusable.Record(ProjectedChangeKind.Created, "one")!;
        reusable.TryTake(out _); reusable.Release(completed); reusable.Release(completed);
        Require(reusable.Record(ProjectedChangeKind.Created, "two") is not null && !reusable.Overflowed, "Release did not restore bounded capacity.");
        Console.WriteLine("Projected change ledger: PASS recreation, rename chain, retired visits, 4096 concurrent records; bounded queued/in-flight capacity; overflow retirement; idempotent release; no window or file mutation.");
        return 0;
    }
}
