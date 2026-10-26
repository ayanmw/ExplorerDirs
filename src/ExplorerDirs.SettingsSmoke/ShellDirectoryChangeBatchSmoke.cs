using ExplorerDirs.App;

internal static class ShellDirectoryChangeBatchSmoke
{
    internal static void Run()
    {
        var updatedByShell = new ShellDirectoryChangeBatch(10);
        Check(!updatedByShell.HasPendingChanges, "an idle monitor tick has no pending directory changes");
        updatedByShell.Created();
        Check(updatedByShell.HasPendingChanges, "a queued create marks a directory batch as pending");
        Check(!updatedByShell.Complete(11), "native Shell create needs no fallback refresh");
        Check(!updatedByShell.HasPendingChanges, "a completed directory batch clears its pending-change marker");

        var missedCreate = new ShellDirectoryChangeBatch(10);
        missedCreate.Created();
        Check(missedCreate.Complete(10), "missed Shell create requests fallback refresh");
        Check(!missedCreate.Complete(11), "fallback create advances the expected count");

        var missedDelete = new ShellDirectoryChangeBatch(10);
        missedDelete.Deleted();
        Check(missedDelete.Complete(10), "missed Shell delete requests fallback refresh");

        var transient = new ShellDirectoryChangeBatch(10);
        transient.RecordChanges(1, forceRefresh: false);
        transient.RecordChanges(-1, forceRefresh: false);
        Check(transient.Complete(10), "balanced create and delete events request reconciliation for possible item replacement");

        var replacementAcrossWindows = new ShellDirectoryChangeBatch(10);
        replacementAcrossWindows.RecordChanges(1, forceRefresh: false);
        replacementAcrossWindows.RecordChanges(-1, forceRefresh: false);
        Check(replacementAcrossWindows.Complete(10),
            "balanced create and delete events across queue drains still request reconciliation");

        var queuedReplacement = new ShellDirectoryChangeBatch(10);
        var replacementEvents = new ShellDirectoryChangeQueue();
        replacementEvents.Enqueue(1, forceRefresh: false);
        var createHandoff = replacementEvents.DrainWhileDispatchPending();
        queuedReplacement.RecordChanges(createHandoff.Delta, createHandoff.ForceRefresh);
        replacementEvents.Enqueue(-1, forceRefresh: false);
        var deleteHandoff = replacementEvents.Drain();
        queuedReplacement.RecordChanges(deleteHandoff.Delta, deleteHandoff.ForceRefresh);
        Check(queuedReplacement.Complete(10),
            "production queue-to-batch handoffs refresh a same-count item replacement");

        var renamed = new ShellDirectoryChangeBatch(10);
        renamed.Renamed();
        Check(renamed.Complete(10), "rename forces refresh even when the count is unchanged");

        var overflow = new ShellDirectoryChangeBatch(10);
        overflow.Failed();
        Check(overflow.Complete(10), "watcher failure forces refresh");

        var aggregated = new ShellDirectoryChangeBatch(10);
        aggregated.RecordChanges(3000, forceRefresh: false);
        Check(!aggregated.Complete(3010), "a merged event delta matches the final Shell count");
        aggregated.RecordChanges(-2, forceRefresh: false);
        Check(aggregated.Complete(3010), "a merged deletion delta detects a stale Shell count");

        var drifted = new ShellDirectoryChangeBatch(3);
        drifted.RecordChanges(2997, forceRefresh: false);
        Check(drifted.Complete(3), "a missed create batch requests refresh");
        drifted.Rebase(3003);
        drifted.RecordChanges(-3000, forceRefresh: false);
        Check(!drifted.Complete(3), "a later delete batch uses the post-refresh count instead of a missed-event estimate");

        var changesDuringRefresh = new ShellDirectoryChangeBatch(3);
        changesDuringRefresh.RecordChanges(1, forceRefresh: false);
        Check(changesDuringRefresh.Complete(3), "a stale view starts fallback refresh");
        changesDuringRefresh.RecordChanges(-1, forceRefresh: false);
        Check(!changesDuringRefresh.CompleteRefresh(4), "changes arriving during refresh remain pending when the view is not settled");
        Check(changesDuringRefresh.ExpectedCountAfterChanges == 3, "in-flight watcher changes remain part of the expected count");
        Check(changesDuringRefresh.CompleteRefresh(3), "a refresh completion matching pending changes rebases cleanly");
        Check(changesDuringRefresh.ExpectedCountAfterChanges == 3, "a settled refresh establishes the actual count as the new baseline");

        var staleRefreshCompletion = new ShellDirectoryChangeBatch(4);
        staleRefreshCompletion.RecordChanges(1, forceRefresh: false);
        Check(!staleRefreshCompletion.CompleteRefresh(3),
            "the first incomplete refresh retains the batch before testing reentrant completion");
        var staleRefreshQueue = new ShellDirectoryChangeQueue();
        staleRefreshQueue.Enqueue(-1, forceRefresh: false);
        var completionStillCurrent = true;
        Check(!staleRefreshCompletion.CompleteRefresh(staleRefreshQueue, () =>
            {
                completionStillCurrent = false;
                return 99;
            }, () => completionStillCurrent) && staleRefreshCompletion.ExpectedCountAfterChanges == 4,
            "a refresh count read that reenters navigation does not settle the retired batch");

        var queuedChangesDuringRefresh = new ShellDirectoryChangeBatch(4);
        queuedChangesDuringRefresh.RecordChanges(1, forceRefresh: false);
        Check(queuedChangesDuringRefresh.Complete(4), "a stale count starts a refresh before queued events arrive");
        var refreshQueue = new ShellDirectoryChangeQueue();
        Check(refreshQueue.Enqueue(-1, forceRefresh: false), "a watcher event is pending while refresh completion is queued");
        Check(queuedChangesDuringRefresh.CompleteRefresh(refreshQueue, () => 4),
            "refresh completion drains pending watcher events before reconciling the count");
        Check(queuedChangesDuringRefresh.LastRefreshDrainedDelta == -1,
            "refresh reconciliation records and applies the queued event delta");
        Check(queuedChangesDuringRefresh.LastRefreshExpectedCount == 4 &&
              queuedChangesDuringRefresh.LastRefreshObservedCount == 4,
            "refresh diagnostics preserve the reconciled target and observed Shell count");
        Check(queuedChangesDuringRefresh.ExpectedCountAfterChanges == 4,
            "the drained in-flight event and settled count establish the same baseline");

        var eventDuringCountRead = new ShellDirectoryChangeBatch(4);
        eventDuringCountRead.RecordChanges(1, forceRefresh: false);
        Check(eventDuringCountRead.Complete(4), "a stale count starts a refresh before the count read");
        var countReadQueue = new ShellDirectoryChangeQueue();
        Check(eventDuringCountRead.CompleteRefresh(countReadQueue, () =>
        {
            Check(countReadQueue.Enqueue(1, forceRefresh: false),
                "an event arriving after refresh drain schedules a later queue dispatch");
            return 5;
        }), "the settled refresh establishes its baseline before the later event is applied");
        var laterChanges = countReadQueue.Drain();
        eventDuringCountRead.RecordChanges(laterChanges.Delta, laterChanges.ForceRefresh);
        Check(eventDuringCountRead.ExpectedCountAfterChanges == 6,
            "an event arriving during the refresh count read remains pending after baseline reconciliation");

        var boundedRefreshRetry = new ShellDirectoryChangeBatch(3);
        boundedRefreshRetry.RecordChanges(1, forceRefresh: false);
        Check(boundedRefreshRetry.Complete(3), "a stale view starts the first fallback");
        Check(!boundedRefreshRetry.CompleteRefresh(3), "the first incomplete refresh retains the expected count");
        Check(boundedRefreshRetry.Complete(3), "a retained mismatch can request one retry");
        Check(boundedRefreshRetry.CompleteRefresh(3), "a second incomplete refresh accepts the actual count to stop a refresh loop");
        Check(boundedRefreshRetry.ExpectedCountAfterChanges == 3, "bounded refresh recovery rebases after its retry limit");

        var transientEmptyView = new ShellDirectoryChangeBatch(3);
        transientEmptyView.RecordChanges(10000, forceRefresh: true);
        Check(transientEmptyView.Complete(3), "an overflow forces the first recovery refresh");
        Check(!transientEmptyView.CompleteRefresh(0), "the first transient empty view keeps the original baseline");
        Check(transientEmptyView.Complete(3), "a second stale count requests a bounded retry");
        Check(transientEmptyView.CompleteRefresh(0), "a repeated empty view starts bounded background reconciliation");
        Check(transientEmptyView.ExpectedCountAfterChanges == 10003,
            "a transient empty Shell view does not discard the last expected item count");
        transientEmptyView.RecordChanges(-10000, forceRefresh: false);
        Check(transientEmptyView.ExpectedCountAfterChanges == 3,
            "later delete events apply to the preserved nonempty count instead of producing a negative target");
        var emptyViewWaits = 0;
        while (emptyViewWaits < 180 && transientEmptyView.ShouldDeferRefresh(0, recheckPending: true))
            emptyViewWaits++;
        Check(emptyViewWaits == 180,
            "the logical item count is retained while the Shell view remains transiently empty");
        Check(transientEmptyView.ShouldDeferRefresh(0, recheckPending: true),
            "an empty-view retry returns to the bounded mismatch policy after its wait window");

        var recoveredEmptyView = new ShellDirectoryChangeBatch(4);
        recoveredEmptyView.RecordChanges(1, forceRefresh: true);
        Check(recoveredEmptyView.Complete(4), "an overflow starts the first empty-view recovery refresh");
        Check(!recoveredEmptyView.CompleteRefresh(0), "the first empty refresh retains the expected count");
        Check(recoveredEmptyView.Complete(4), "a repeated stale count starts the second recovery refresh");
        Check(recoveredEmptyView.CompleteRefresh(0), "a repeated empty refresh starts the transient recovery window");
        Check(!recoveredEmptyView.ShouldDeferRefresh(5, recheckPending: false) &&
              !recoveredEmptyView.Complete(5),
            "matching the recovered Shell count settles and closes the empty-view recovery window");
        recoveredEmptyView.RecordChanges(1, forceRefresh: false);
        Check(recoveredEmptyView.ShouldDeferRefresh(0, recheckPending: false) &&
              !recoveredEmptyView.ShouldDeferRefresh(0, recheckPending: true),
            "a later independent mismatch uses its ordinary settle interval, not the prior empty-view window");

        var singleItemMismatch = new ShellDirectoryChangeBatch(3);
        singleItemMismatch.RecordChanges(1, forceRefresh: false);
        Check(!singleItemMismatch.ShouldDeferRefresh(3, recheckPending: false),
            "an isolated single stale item proceeds after the initial quiet-period check");
        Check(singleItemMismatch.ShouldDeferRefresh(3, recheckPending: false, largeBurstActive: true),
            "a single stale item waits while a large filesystem burst is still active");
        Check(!singleItemMismatch.ShouldDeferRefresh(3, recheckPending: true, largeBurstActive: false),
            "a single stale item proceeds after the burst quiet window ends");

        var smallPostRefreshMismatch = new ShellDirectoryChangeBatch(3);
        smallPostRefreshMismatch.RecordChanges(2, forceRefresh: false);
        Check(smallPostRefreshMismatch.ShouldDeferRefresh(3, recheckPending: false),
            "a small mismatch gets an initial settle interval");
        Check(!smallPostRefreshMismatch.ShouldDeferRefresh(3, recheckPending: true),
            "a stable small mismatch proceeds after one additional settle interval");

        var largePostRefreshMismatch = new ShellDirectoryChangeBatch(3);
        largePostRefreshMismatch.RecordChanges(3000, forceRefresh: false);
        Check(largePostRefreshMismatch.Complete(3), "a large stale view starts fallback");
        Check(!largePostRefreshMismatch.CompleteRefresh(3), "an incomplete large refresh retains its target");
        Check(largePostRefreshMismatch.ShouldDeferRefresh(3, recheckPending: false),
            "a large post-refresh count mismatch gets the extended settle window");
        for (var check = 1; check < 4; check++)
            Check(largePostRefreshMismatch.ShouldDeferRefresh(3, recheckPending: true),
                "a stable large post-refresh mismatch gets a short grace window");
        Check(!largePostRefreshMismatch.ShouldDeferRefresh(3, recheckPending: true),
            "a stable large post-refresh mismatch retries before the total bound");

        var intermittentUnreadableCount = new ShellDirectoryChangeBatch(3);
        intermittentUnreadableCount.RecordChanges(3000, forceRefresh: false);
        Check(intermittentUnreadableCount.ShouldDeferRefresh(1000, recheckPending: false),
            "a large mismatch starts its stable-count window");
        for (var check = 0; check < 3; check++)
            Check(intermittentUnreadableCount.ShouldDeferRefresh(1000, recheckPending: true),
                "large mismatch waits for consecutive stable count samples");
        Check(intermittentUnreadableCount.ShouldDeferRefresh(null, recheckPending: true),
            "an unavailable Shell count waits without triggering refresh");
        Check(intermittentUnreadableCount.ShouldDeferRefresh(1000, recheckPending: true),
            "an unreadable Shell count breaks the consecutive-stability window");

        var activeLargeBurst = new ShellDirectoryChangeBatch(3);
        activeLargeBurst.RecordChanges(3000, forceRefresh: false);
        for (var check = 0; check < 12; check++)
            activeLargeBurst.ShouldDeferRefresh(1000, recheckPending: true);
        Check(activeLargeBurst.ShouldDeferRefresh(1000, recheckPending: true, largeBurstActive: true),
            "fresh watcher activity restarts the large-burst settle window instead of refreshing mid-operation");
        Check(activeLargeBurst.ShouldDeferRefresh(1000, recheckPending: true, largeBurstActive: false),
            "large-burst fallback resumes through the existing bounded settle window after watcher activity stops");

        var forcedLargeBatch = new ShellDirectoryChangeBatch(3);
        forcedLargeBatch.RecordChanges(3000, forceRefresh: true);
        Check(!forcedLargeBatch.ShouldDeferRefresh(1000, recheckPending: false, largeBurstActive: true),
            "rename and watcher-overflow forced refreshes bypass the large-burst quiet wait");

        var renameDuringRefresh = new ShellDirectoryChangeBatch(3);
        renameDuringRefresh.RecordChanges(1, forceRefresh: false);
        Check(renameDuringRefresh.Complete(3), "a stale view starts fallback before rename");
        renameDuringRefresh.Renamed();
        Check(!renameDuringRefresh.CompleteRefresh(4) && renameDuringRefresh.ForceRefresh,
            "a rename arriving during refresh is not cleared by the completion callback");

        var partialNativeUpdate = new ShellDirectoryChangeBatch(3);
        partialNativeUpdate.RecordChanges(3000, forceRefresh: false);
        Check(partialNativeUpdate.ShouldDeferRefresh(2700, recheckPending: false),
            "a transient partial Shell count gets one settle interval");
        Check(partialNativeUpdate.ShouldDeferRefresh(2922, recheckPending: true),
            "a changing partial Shell count gets another settle interval");
        Check(partialNativeUpdate.ShouldDeferRefresh(2922, recheckPending: true),
            "a brief stable interval does not end a large Shell enumeration");
        Check(partialNativeUpdate.ShouldDeferRefresh(3000, recheckPending: true),
            "later Shell count progress resets the stall grace window");
        for (var check = 1; check < 4; check++)
            Check(partialNativeUpdate.ShouldDeferRefresh(3000, recheckPending: true),
                "a progressing enumeration gets a brief stable grace window");
        Check(!partialNativeUpdate.ShouldDeferRefresh(3000, recheckPending: true),
            "a stable partial count proceeds to fallback after the grace window");
        var stalledNativeUpdate = new ShellDirectoryChangeBatch(3);
        stalledNativeUpdate.RecordChanges(3000, forceRefresh: false);
        Check(stalledNativeUpdate.ShouldDeferRefresh(1000, recheckPending: false),
            "the first partial count starts the large enumeration wait");
        Check(stalledNativeUpdate.ShouldDeferRefresh(1200, recheckPending: true),
            "Shell count progress keeps the enumeration wait open");
        for (var check = 1; check < 4; check++)
            Check(stalledNativeUpdate.ShouldDeferRefresh(1200, recheckPending: true),
                "a stalled large count gets a short grace window");
        Check(!stalledNativeUpdate.ShouldDeferRefresh(1200, recheckPending: true),
            "a stable large mismatch reaches fallback before the overall bound");
        var progressingNativeUpdate = new ShellDirectoryChangeBatch(3);
        progressingNativeUpdate.RecordChanges(3000, forceRefresh: false);
        Check(progressingNativeUpdate.ShouldDeferRefresh(1000, recheckPending: false), "initial partial enumeration is deferred");
        for (var count = 1100; count <= 2000; count += 100)
            Check(progressingNativeUpdate.ShouldDeferRefresh(count, recheckPending: true),
                "continued Shell enumeration receives another settle interval");
        Check(!progressingNativeUpdate.ShouldDeferRefresh(2100, recheckPending: true),
            "continuing progress still reaches the overall deferral bound");
        var accurateNativeUpdate = new ShellDirectoryChangeBatch(3);
        accurateNativeUpdate.RecordChanges(1, forceRefresh: false);
        Check(!accurateNativeUpdate.ShouldDeferRefresh(4, recheckPending: false),
            "an already-current Shell count does not wait for the extra interval");

        var unreadableViewCount = new ShellDirectoryChangeBatch(3);
        unreadableViewCount.RecordChanges(1, forceRefresh: false);
        Check(unreadableViewCount.ShouldDeferRefresh(null, recheckPending: false),
            "an unavailable Shell count waits for a readable count instead of repeatedly refreshing");
        Check(unreadableViewCount.ExpectedCountAfterChanges == 4,
            "waiting for a readable count retains the pending watcher delta");
        Check(!unreadableViewCount.ShouldDeferRefresh(3, recheckPending: true),
            "a readable stale count resumes the ordinary mismatch fallback");

        var forcedUnreadableViewCount = new ShellDirectoryChangeBatch(3);
        forcedUnreadableViewCount.Failed();
        Check(!forcedUnreadableViewCount.ShouldDeferRefresh(null, recheckPending: false),
            "watcher failures still force refresh when the Shell count is unavailable");
        Check(forcedUnreadableViewCount.Complete(null),
            "a forced recovery refresh can start without a readable count");
        Check(!forcedUnreadableViewCount.CompleteRefresh(null),
            "an unreadable completion does not claim that refresh state settled");
        Check(forcedUnreadableViewCount.ShouldDeferRefresh(null, recheckPending: true),
            "a forced refresh that still cannot read the count does not repeat indefinitely");

        var transientEmptyThenStableMismatch = new ShellDirectoryChangeBatch(4);
        transientEmptyThenStableMismatch.RecordChanges(1, forceRefresh: false);
        Check(transientEmptyThenStableMismatch.Complete(4), "a one-item mismatch starts the initial fallback");
        Check(!transientEmptyThenStableMismatch.CompleteRefresh(0), "the first empty refresh keeps the expected count");
        Check(transientEmptyThenStableMismatch.Complete(4), "the retained mismatch starts its bounded retry");
        Check(transientEmptyThenStableMismatch.CompleteRefresh(0), "a repeated empty refresh enters transient-empty recovery");
        for (var check = 0; check < 4; check++)
            Check(transientEmptyThenStableMismatch.ShouldDeferRefresh(4, recheckPending: check > 0),
                "a stable nonempty count after a transient empty result gets a settle window");
        Check(!transientEmptyThenStableMismatch.ShouldDeferRefresh(4, recheckPending: true),
            "a stable nonempty count eventually ends transient-empty recovery");
        Check(transientEmptyThenStableMismatch.ExpectedCountAfterChanges == 5 &&
              transientEmptyThenStableMismatch.Complete(4),
            "stable stale count keeps the filesystem target and starts one bounded recovery refresh");
        Check(!transientEmptyThenStableMismatch.CompleteRefresh(4) &&
              transientEmptyThenStableMismatch.Complete(4) &&
              transientEmptyThenStableMismatch.CompleteRefresh(0),
            "the recovery refresh retains its target if Shell still reports a transient empty view");
        for (var check = 0; check < 4; check++)
            Check(transientEmptyThenStableMismatch.ShouldDeferRefresh(4, recheckPending: check > 0),
                "a second unresolved transient-empty cycle still gets its bounded settle window");
        Check(!transientEmptyThenStableMismatch.ShouldDeferRefresh(4, recheckPending: true),
            "a second unresolved cycle reaches the existing stable-count fallback");
        Check(!transientEmptyThenStableMismatch.Complete(4) &&
              transientEmptyThenStableMismatch.ExpectedCountAfterChanges == 4,
            "a second unresolved recovery returns to the bounded stable-count fallback");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell directory change batch: {label}");
    }
}
