namespace ExplorerDirs.NativeProbe;

internal static class ProjectedDispatchSmoke
{
    internal static int Run()
    {
        static void Require(bool value) { if (!value) throw new InvalidOperationException("Projected dispatch retirement contract failed."); }
        foreach (var retireAt in new[] { "before", "dispatch", "ready", "ack" })
        {
            var queue = new ProjectedChangeLedger();
            var first = queue.Record(ProjectedChangeKind.Deleted, "one")!;
            queue.Record(ProjectedChangeKind.Deleted, "two");
            var pump = new ProjectedChangeDispatch(queue, 1);
            var current = retireAt != "before";
            var sent = 0;
            var acknowledged = 0;
            ProjectedDispatchResult Dispatch(ProjectedChangeLedger.Work work)
            {
                sent++;
                if (retireAt == "dispatch") current = false;
                // Simulate synchronous Shell COM reentry. Nested work must not send twice.
                pump.Advance(() => current, Dispatch, Ready, Ack);
                return new(7);
            }
            bool Ready(ProjectedChangeLedger.Work work)
            {
                if (retireAt == "ready") current = false;
                return true;
            }
            void Ack(ProjectedChangeLedger.Work work, ulong ticket)
            {
                Require(ticket == 7 && ReferenceEquals(work, first));
                acknowledged++;
                if (retireAt == "ack") current = false;
            }
            pump.Advance(() => current, Dispatch, Ready, Ack);
            pump.Advance(() => current, Dispatch, Ready, Ack);
            Require(sent == (retireAt == "before" ? 0 : 1));
            Require(acknowledged == (retireAt == "ack" ? 1 : 0));
            Require(pump.PendingCount == 0 && queue.Snapshot == (0, 0, true));
        }
        var normal = new ProjectedChangeLedger();
        for (var i = 0; i < 17; i++) normal.Record(ProjectedChangeKind.Created, $"item-{i}");
        var dispatcher = new ProjectedChangeDispatch(normal, 8);
        var dispatched = 0;
        dispatcher.Advance(() => true, _ => { dispatched++; return new ProjectedDispatchResult(0); }, _ => false, (_, _) => throw new InvalidOperationException());
        Require(dispatched == 8 && dispatcher.PendingCount == 8 && normal.Snapshot.Queued == 9);
        dispatcher.Advance(() => true, _ => { dispatched++; return new ProjectedDispatchResult(0); }, _ => false, (_, _) => throw new InvalidOperationException());
        Require(dispatched == 8);
        Require(dispatcher.LastUnconfirmed?.Name == "item-0");
        for (var i = 0; i < 4; i++) dispatcher.Advance(() => true, _ => { dispatched++; return new ProjectedDispatchResult(0); }, _ => true, (_, _) => throw new InvalidOperationException());
        Require(dispatched == 17 && dispatcher.PendingCount == 0 && normal.Snapshot == (0, 0, false));
        Require(dispatcher.LastUnconfirmed is null);
        var replaced = new ProjectedChangeLedger();
        for (var i = 0; i < 17; i++) replaced.Record(ProjectedChangeKind.Deleted, $"recreated-{i}");
        var replacementPump = new ProjectedChangeDispatch(replaced, 8);
        var reconciledCount = 0;
        for (var turn = 0; turn < 3; turn++)
        {
            replacementPump.Advance(() => true, _ => { reconciledCount++; return new(0, Reconciled: true); },
                _ => throw new InvalidOperationException("Reconciled deletion must not wait for absence."),
                (_, _) => throw new InvalidOperationException("Reconciled deletion must not acknowledge an old ticket."));
            Require(reconciledCount == Math.Min((turn + 1) * 8, 17) && replacementPump.PendingCount == 0);
        }
        Require(replaced.Snapshot == (0, 0, false));
        var chained = new ProjectedChangeLedger();
        chained.Record(ProjectedChangeKind.Renamed, "old", "new");
        var chainedPump = new ProjectedChangeDispatch(chained, 8);
        var confirmations = 0;
        chainedPump.Advance(() => true, _ => new(11), _ => true, (_, _) => { });
        chained.Record(ProjectedChangeKind.Deleted, "new");
        chainedPump.Advance(() => true, _ => new(12), _ => true, (_, ticket) => { Require(ticket == 11); confirmations++; });
        chainedPump.Advance(() => true, _ => throw new InvalidOperationException(), _ => true, (_, ticket) => { Require(ticket == 12); confirmations++; });
        Require(confirmations == 2 && chained.Snapshot == (0, 0, false));
        var obsoleteChange = new ProjectedChangeLedger();
        obsoleteChange.Record(ProjectedChangeKind.Changed, "gone");
        var obsoletePump = new ProjectedChangeDispatch(obsoleteChange, 8);
        obsoletePump.Advance(() => true, _ => new(19, ConfirmationKind: ProjectedChangeKind.Deleted), _ => false, (_, _) => { });
        var removalAcknowledged = false;
        obsoletePump.Advance(() => true, _ => throw new InvalidOperationException(), work => work.Kind == ProjectedChangeKind.Deleted,
            (work, ticket) => { Require(work.Kind == ProjectedChangeKind.Deleted && ticket == 19); removalAcknowledged = true; });
        Require(removalAcknowledged && obsoleteChange.IsSettled && obsoleteChange.Snapshot == (0, 0, false));
        var overflowing = new ProjectedChangeLedger(1);
        overflowing.Record(ProjectedChangeKind.Created, "initial");
        var overflowingPump = new ProjectedChangeDispatch(overflowing, 8);
        overflowingPump.Advance(() => true, _ =>
        {
            overflowing.Record(ProjectedChangeKind.Created, "overflow");
            return new(0);
        }, _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException());
        Require(overflowing.Overflowed && overflowingPump.PendingCount == 0 && overflowing.Snapshot == (0, 0, true));
        var failed = new ProjectedChangeLedger();
        failed.Record(ProjectedChangeKind.Created, "failed");
        var broken = new ProjectedChangeDispatch(failed, 8);
        try
        {
            broken.Advance(() => true, _ => throw new IOException("injected dispatch failure"), _ => true, (_, _) => { });
            throw new InvalidOperationException("Expected dispatch failure.");
        }
        catch (IOException)
        {
            Require(broken.PendingCount == 0 && failed.Snapshot == (0, 0, true));
        }
        Console.WriteLine("Projected dispatch: PASS bounded batches; deferred confirmation; reentrant dispatch; retirement before/during dispatch/read/ack; no windows.");
        return 0;
    }
}
