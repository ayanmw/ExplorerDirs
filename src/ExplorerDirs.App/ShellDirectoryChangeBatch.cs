namespace ExplorerDirs.App;

// Compares the settled Shell count with net filesystem item changes and refresh
// requests from one queue window. Native Shell updates remain the primary path.
internal sealed class ShellDirectoryChangeBatch
{
    private const int MaximumLargeMismatchRefreshChecks = 12;
    private const int StableLargeMismatchRefreshChecks = 4;
    private int? _expectedCount;
    private int _pendingDelta;
    private bool _forceRefresh;
    private bool _sawCreate;
    private bool _sawDelete;
    private int _deferredRefreshChecks;
    private int? _lastDeferredActualCount;
    private int _stableDeferredRefreshChecks;
    private int _consecutiveRefreshMismatches;
    private int _transientEmptyViewRechecksRemaining;
    private bool _transientEmptyMismatchRetryUsed;
    internal int LastRefreshDrainedDelta { get; private set; }
    internal int? LastRefreshExpectedCount { get; private set; }
    internal int? LastRefreshObservedCount { get; private set; }
    internal string DiagnosticSnapshot =>
        $"baseline={_expectedCount?.ToString() ?? "null"},pending={_pendingDelta},expected={ExpectedCountAfterChanges?.ToString() ?? "null"},deferred={_deferredRefreshChecks},refresh-mismatches={_consecutiveRefreshMismatches}";

    internal ShellDirectoryChangeBatch(int initialCount) => _expectedCount = initialCount;
    internal int? ExpectedCountAfterChanges => _expectedCount is { } expected ? expected + _pendingDelta : null;
    internal bool ForceRefresh => _forceRefresh;
    internal bool HasPendingChanges => _pendingDelta != 0 || _forceRefresh;

    internal void Created() => RecordChanges(1, forceRefresh: false);
    internal void Deleted() => RecordChanges(-1, forceRefresh: false);
    internal void Renamed() => _forceRefresh = true;
    internal void Failed() => _forceRefresh = true;
    internal void RecordChanges(int delta, bool forceRefresh)
    {
        if (delta != 0) _transientEmptyMismatchRetryUsed = false;
        _pendingDelta += delta;
        _sawCreate |= delta > 0;
        _sawDelete |= delta < 0;
        // Equal create/delete deltas may still replace a visible item while preserving the count.
        _forceRefresh |= forceRefresh || (_sawCreate && _sawDelete);
    }

    internal void Rebase(int actualCount)
    {
        _expectedCount = actualCount;
        _pendingDelta = 0;
        _forceRefresh = false;
        _sawCreate = false;
        _sawDelete = false;
        _deferredRefreshChecks = 0;
        _lastDeferredActualCount = null;
        _stableDeferredRefreshChecks = 0;
        _consecutiveRefreshMismatches = 0;
        _transientEmptyViewRechecksRemaining = 0;
    }

    internal bool CompleteNaturalUpdate(int? actualCount)
    {
        // Shell item notifications can lag the count check. Keep their expected
        // delta available to delayed delete repair until the actual rows catch up.
        if (actualCount is not { } actual || ExpectedCountAfterChanges != actual) return false;
        Rebase(actual);
        return true;
    }

    internal bool CompleteRefresh(int? actualCount)
    {
        var expected = ExpectedCountAfterChanges;
        LastRefreshExpectedCount = expected;
        LastRefreshObservedCount = actualCount;
        if (actualCount is not { } actual || _forceRefresh || expected is not { } target || actual != target)
        {
            _deferredRefreshChecks = 0;
            if (actualCount is { } observed && ++_consecutiveRefreshMismatches >= 2)
            {
                // A second refresh still cannot reconcile this Shell view. Accept
                // its current count to avoid an endless refresh loop. A transient
                // empty result must retain the logical count so later watcher
                // deletions do not turn that count negative.
                var expectedBeforeRebase = expected;
                if (expectedBeforeRebase is > 0 && observed == 0)
                {
                    Rebase(expectedBeforeRebase.Value);
                    _transientEmptyViewRechecksRemaining = 180;
                }
                else Rebase(observed);
                return true;
            }
            return false;
        }

        Rebase(actual);
        _transientEmptyMismatchRetryUsed = false;
        return true;
    }

    internal bool CompleteRefresh(ShellDirectoryChangeQueue queue, Func<int?> readActualCount)
        => CompleteRefresh(queue, readActualCount, static () => true);

    internal bool CompleteRefresh(ShellDirectoryChangeQueue queue, Func<int?> readActualCount,
        Func<bool> requestIsCurrent)
    {
        var changes = queue.Drain();
        LastRefreshDrainedDelta = changes.Delta;
        RecordChanges(changes.Delta, changes.ForceRefresh);
        var actualCount = readActualCount();
        return requestIsCurrent() && CompleteRefresh(actualCount);
    }

    internal bool ConsumeTransientEmptyViewRecheck()
    {
        if (_transientEmptyViewRechecksRemaining <= 0) return false;
        _transientEmptyViewRechecksRemaining--;
        return true;
    }

    internal bool ShouldDeferRefresh(int? actualCount, bool recheckPending) =>
        ShouldDeferRefresh(actualCount, recheckPending, largeBurstActive: false);

    internal bool ShouldDeferRefresh(int? actualCount, bool recheckPending, bool largeBurstActive) =>
        ShouldDeferRefreshCore(actualCount, recheckPending, largeBurstActive);

    private bool ShouldDeferRefreshCore(int? actualCount, bool recheckPending, bool largeBurstActive)
    {
        if (_forceRefresh) return false;
        // A missing count cannot establish that a refresh is needed or complete. Keep the
        // watcher delta pending and retry the cheap count read instead of refreshing forever.
        // It also breaks the consecutive-stability window because the missing sample cannot
        // prove that the previously observed Shell count remained unchanged.
        if (actualCount is null)
        {
            _lastDeferredActualCount = null;
            _stableDeferredRefreshChecks = 0;
            return true;
        }
        if (ExpectedCountAfterChanges is not { } expected || actualCount == expected)
            return false;
        if (_transientEmptyViewRechecksRemaining > 0 && actualCount == 0 && expected > 0)
        {
            _transientEmptyViewRechecksRemaining--;
            _deferredRefreshChecks = 0;
            _lastDeferredActualCount = actualCount;
            _stableDeferredRefreshChecks = 0;
            return true;
        }
        if (_transientEmptyViewRechecksRemaining > 0 && actualCount is { } repopulatedCount &&
            repopulatedCount > 0 && repopulatedCount != expected)
        {
            // A refresh can briefly empty the hidden Shell view and then repopulate it
            // with a stale nonempty count. Let that count settle before another refresh;
            // once stable, accept the actual view count instead of repeating the empty cycle.
            _transientEmptyViewRechecksRemaining--;
            if (_lastDeferredActualCount == repopulatedCount) _stableDeferredRefreshChecks++;
            else _stableDeferredRefreshChecks = 0;
            _lastDeferredActualCount = repopulatedCount;
            _deferredRefreshChecks++;
            if (_stableDeferredRefreshChecks < StableLargeMismatchRefreshChecks) return true;
            if (!_transientEmptyMismatchRetryUsed)
            {
                var pendingDelta = expected - repopulatedCount;
                Rebase(repopulatedCount);
                _pendingDelta = pendingDelta;
                _transientEmptyMismatchRetryUsed = true;
            }
            else Rebase(repopulatedCount);
            return false;
        }
        var mismatch = Math.Abs((long)expected - actualCount.Value);
        var changeMagnitude = Math.Max(Math.Abs((long)_pendingDelta), mismatch);
        // The first watcher callback may be the only event delivered before the quiet timer fires,
        // even when a larger filesystem burst is still arriving through the watcher queue.
        if (largeBurstActive && changeMagnitude == 1) return true;
        // The first count check already follows the 250 ms watcher quiet period.
        // A one-item mismatch has no partial enumeration to wait for, so refresh now.
        if (changeMagnitude == 1) return false;
        // Large Shell enumeration counts can lag or rewind while the same filesystem burst is active.
        // Avoid refreshing against that partial count until the watcher has been quiet for three timer intervals.
        if (largeBurstActive && changeMagnitude >= 32)
        {
            _deferredRefreshChecks = 0;
            _lastDeferredActualCount = actualCount;
            _stableDeferredRefreshChecks = 0;
            return true;
        }
        if (changeMagnitude < 32)
        {
            if (!recheckPending)
            {
                _deferredRefreshChecks = 1;
                return true;
            }
            return false;
        }
        if (!recheckPending)
        {
            _deferredRefreshChecks = 1;
            _lastDeferredActualCount = actualCount;
            _stableDeferredRefreshChecks = 0;
            return true;
        }
        // Let progressing enumerations use the full bound, but do not leave a
        // large stale count waiting through every interval when Shell is stalled.
        _deferredRefreshChecks++;
        if (_lastDeferredActualCount == actualCount) _stableDeferredRefreshChecks++;
        else _stableDeferredRefreshChecks = 0;
        _lastDeferredActualCount = actualCount;
        return _deferredRefreshChecks < MaximumLargeMismatchRefreshChecks &&
               _stableDeferredRefreshChecks < StableLargeMismatchRefreshChecks;
    }
    internal bool Complete(int? actualCount)
    {
        var targetCount = ExpectedCountAfterChanges;
        var refresh = _forceRefresh || targetCount is null || actualCount is null || actualCount != targetCount;
        if (!refresh)
        {
            _consecutiveRefreshMismatches = 0;
            _transientEmptyMismatchRetryUsed = false;
            // Exact count agreement ends the previous transient-empty recovery episode.
            _transientEmptyViewRechecksRemaining = 0;
        }
        _expectedCount = targetCount ?? actualCount;
        _pendingDelta = 0;
        _forceRefresh = false;
        _sawCreate = false;
        _sawDelete = false;
        _deferredRefreshChecks = 0;
        _lastDeferredActualCount = null;
        _stableDeferredRefreshChecks = 0;
        return refresh;
    }
}
