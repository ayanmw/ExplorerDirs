namespace ExplorerDirs.App;

internal static class ShellSelectionRestoreGate
{
    internal static bool ShouldKeepExistingSnapshot(bool sameView, bool userInputUnchanged,
        int previousSelectedCount, int currentSelectedCount, bool previousUnselectedFocusBecameSelected = false)
        => sameView && userInputUnchanged && previousSelectedCount > 0 && currentSelectedCount > 0 &&
            (previousSelectedCount >= currentSelectedCount ||
             previousUnselectedFocusBecameSelected);

    internal static bool ShouldUseRefreshSnapshot(bool hasSnapshot, bool sameView, bool userInputUnchanged)
        => hasSnapshot && sameView && userInputUnchanged;

    internal static bool ShouldObserveSelectionCallback(bool deferredRefreshRestorePending, bool pointerSelectionInProgress = false)
        => !deferredRefreshRestorePending && !pointerSelectionInProgress;

    internal static bool ShouldRestore(bool hasSnapshot, int selectedCount, long snapshotInputVersion, long currentInputVersion)
        => hasSnapshot && selectedCount > 0 && snapshotInputVersion == currentInputVersion;

    internal static bool ShouldRetainSnapshotAfterEmptyCallback(bool hasSnapshot, int selectedCount,
        long snapshotInputVersion, long currentInputVersion)
        => hasSnapshot && selectedCount > 0 && snapshotInputVersion == currentInputVersion;

    internal static bool CanRetryDeferredRestore(bool sameView, bool userInputUnchanged, bool withinRetryWindow)
        => sameView && userInputUnchanged && withinRetryWindow;

    internal static bool ShouldRestoreSelectionAgain(bool selectionWasRestored, bool selectionVersionChanged)
        => !selectionWasRestored || selectionVersionChanged;

    internal static bool ShouldAttemptSelectionRestore(bool selectionWasRestored, bool readinessChanged,
        bool selectionVersionChanged, bool finalAttempt, bool retryIntervalElapsed = false)
        => !selectionWasRestored && (readinessChanged || selectionVersionChanged || retryIntervalElapsed || finalAttempt);

    internal static bool ShouldRetryStableReadiness(bool selectionWasRestored, string? readiness,
        bool retryIntervalElapsed)
        => !selectionWasRestored && retryIntervalElapsed && readiness is { Length: > 0 } &&
            readiness.All(state => state == '1');

    internal static string? ReadinessForSelectionVersion(string? readiness, bool selectionVersionChanged)
        => selectionVersionChanged ? null : readiness;

    internal static bool ShouldDiscardReadinessObservation(long observedSelectionVersion,
        long currentSelectionVersion) => observedSelectionVersion != currentSelectionVersion;

    internal static bool ShouldQuerySelectionReadiness(bool selectionRestored, int selectedCount)
        => !selectionRestored && selectedCount > 0;

    internal static bool ShouldRestoreFocus(bool selectionWasRestored, bool selectionIsEmpty,
        bool hasFocusedItem) => hasFocusedItem && (selectionWasRestored || selectionIsEmpty);

    internal static bool ShouldDeferSelectionRestore(bool hasSelection, int visibleItemCount)
        => hasSelection && visibleItemCount == 0;

    internal static bool ShouldVerifyNaturalUpdateSelection(bool hasSnapshot, bool sameView,
        bool userInputUnchanged, bool withinVerificationWindow)
        => hasSnapshot && sameView && userInputUnchanged && withinVerificationWindow;

    internal static bool ShouldRestoreNaturalUpdateSelection(int savedSelectedCount, int currentSelectedCount)
        => savedSelectedCount > 0 && currentSelectedCount == 0;
}
