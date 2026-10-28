using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellSelectionRestoreGateSmoke
{
    internal static void Run()
    {
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, true, 2, 2), expected: true,
            "same-view Shell callbacks retain the confirmed snapshot until new user input");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, true, 0, 0), expected: false,
            "an empty saved selection is not retained");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, false, 2, 1), expected: false,
            "user selection change replaces the previous snapshot");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(false, true, 2, 1), expected: false,
            "new view does not inherit a prior view snapshot");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, true, 2, 1), expected: true,
            "an incomplete smaller Shell selection does not replace the confirmed snapshot");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, true, 1, 2), expected: false,
            "a larger Shell selection replaces an incomplete snapshot during asynchronous enumeration");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, true, 2, 3,
            previousUnselectedFocusBecameSelected: true), expected: true,
            "a prior unselected focus item added by Shell does not replace the confirmed selection during refresh");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, true, 1, 2,
            previousUnselectedFocusBecameSelected: false), expected: false,
            "asynchronous enumeration can still grow a partial selection when focus selection is unchanged");
        Check(ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(true, false, 2, 3,
            previousUnselectedFocusBecameSelected: true), expected: false,
            "new input still replaces a snapshot when the user selects the focused item");
        Check(ShellSelectionRestoreGate.ShouldRestore(true, 2, 7, 7), expected: true,
            "unchanged user-input generation restores surviving selection");
        Check(ShellSelectionRestoreGate.ShouldRetainSnapshotAfterEmptyCallback(true, 2, 7, 7), expected: true,
            "an empty Shell callback retains the confirmed snapshot while user input is unchanged");
        Check(ShellSelectionRestoreGate.ShouldRetainSnapshotAfterEmptyCallback(true, 2, 7, 8), expected: false,
            "new user input discards the confirmed snapshot after an empty Shell callback");
        Check(ShellSelectionRestoreGate.ShouldRestore(false, 2, 7, 7), expected: false,
            "empty snapshot is not restored");
        Check(ShellSelectionRestoreGate.ShouldRestore(true, 2, 7, 8), expected: false,
            "new user input prevents stale selection restoration");

        Check(ShellSelectionRestoreGate.CanRetryDeferredRestore(true, true, true), expected: true,
            "deferred restore retries while view, input generation and time window remain valid");
        Check(ShellSelectionRestoreGate.CanRetryDeferredRestore(false, true, true), expected: false,
            "navigation change cancels a deferred refresh restore");
        Check(ShellSelectionRestoreGate.CanRetryDeferredRestore(true, false, true), expected: false,
            "new user input cancels a deferred refresh restore");
        Check(ShellSelectionRestoreGate.CanRetryDeferredRestore(true, true, false), expected: false,
            "deferred refresh restore stops after its bounded retry window");

        Check(ShellSelectionRestoreGate.ShouldRestoreSelectionAgain(false, false), expected: true,
            "selection is restored on the first deferred attempt");
        Check(ShellSelectionRestoreGate.ShouldRestoreSelectionAgain(true, false), expected: false,
            "stable selection version skips a repeated selection restore");
        Check(ShellSelectionRestoreGate.ShouldRestoreSelectionAgain(true, true), expected: true,
            "Shell selection change re-enables selection restoration");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(false, readinessChanged: true,
            selectionVersionChanged: false, finalAttempt: false), expected: true,
            "initial readiness observation allows one selection restore attempt");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(false, readinessChanged: false,
            selectionVersionChanged: false, finalAttempt: false), expected: false,
            "stable incomplete selection skips a repeated native batch");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(false, readinessChanged: false,
            selectionVersionChanged: false, retryIntervalElapsed: true, finalAttempt: false), expected: true,
            "a failed exact selection restore retries after its bounded interval even if readiness is unchanged");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(true, readinessChanged: false,
            selectionVersionChanged: false, retryIntervalElapsed: true, finalAttempt: false), expected: false,
            "a completed selection does not repeat when the retry interval elapses");
        Check(ShellSelectionRestoreGate.ShouldRetryStableReadiness(false, "1111", retryIntervalElapsed: true), expected: true,
            "stable fully positive PIDL readiness can retry a selection that is still incomplete");
        Check(ShellSelectionRestoreGate.ShouldRetryStableReadiness(false, "1101", retryIntervalElapsed: true), expected: false,
            "a missing or stale snapshot PIDL does not trigger periodic retries that could disturb surviving selections");
        Check(ShellSelectionRestoreGate.ShouldRetryStableReadiness(false, null, retryIntervalElapsed: true), expected: false,
            "unavailable PIDL readiness keeps the existing bounded recovery policy");
        Check(ShellSelectionRestoreGate.ShouldRetryStableReadiness(false, "1111", retryIntervalElapsed: false), expected: false,
            "stable positive readiness does not retry before the interval elapses");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(false, readinessChanged: true,
            selectionVersionChanged: false, finalAttempt: false), expected: true,
            "a newly locatable snapshot PIDL allows another restore attempt");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(false, readinessChanged: false,
            selectionVersionChanged: true, finalAttempt: false), expected: true,
            "a Shell selection generation change allows another restore attempt");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(false, readinessChanged: false,
            selectionVersionChanged: false, finalAttempt: true), expected: true,
            "the bounded final attempt runs even when readiness stays unchanged");
        Check(ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(true, readinessChanged: true,
            selectionVersionChanged: false, finalAttempt: false), expected: false,
            "a fully restored selection does not repeat after readiness changes");
        Check(ShellSelectionRestoreGate.ReadinessForSelectionVersion("10", selectionVersionChanged: false) == "10",
            expected: true, "positive readiness remains reusable while the Shell selection generation is stable");
        Check(ShellSelectionRestoreGate.ReadinessForSelectionVersion("10", selectionVersionChanged: true) is null,
            expected: true, "a Shell selection generation change invalidates positive readiness cache");
        Check(ShellSelectionRestoreGate.ShouldDiscardReadinessObservation(12, 12), expected: false,
            "readiness results remain eligible when the Shell selection generation stays stable during the query");
        Check(ShellSelectionRestoreGate.ShouldDiscardReadinessObservation(12, 13), expected: true,
            "readiness results are discarded when the Shell selection generation changes during the query");
        Check(ShellSelectionRestoreGate.ShouldQuerySelectionReadiness(selectionRestored: false, selectedCount: 2), expected: true,
            "incomplete restored selections continue checking PIDL readiness");
        Check(ShellSelectionRestoreGate.ShouldQuerySelectionReadiness(selectionRestored: true, selectedCount: 2), expected: false,
            "focus-only retries skip readiness scans after the full selection is restored");
        Check(ShellSelectionRestoreGate.ShouldQuerySelectionReadiness(selectionRestored: false, selectedCount: 0), expected: false,
            "empty selections do not query PIDL readiness");
        Check(ShellSelectionRestoreGate.ShouldRestoreFocus(false, false, true), expected: false,
            "focus restoration waits until the captured selection is fully restored");
        Check(ShellSelectionRestoreGate.ShouldRestoreFocus(true, false, true), expected: true,
            "focus restoration becomes eligible after the full selection is restored");
        Check(ShellSelectionRestoreGate.ShouldRestoreFocus(false, true, true), expected: true,
            "focus-only snapshots do not wait for a selection that was never captured");
        Check(ShellSelectionRestoreGate.ShouldRestoreFocus(true, false, false), expected: false,
            "focus restoration is skipped when the snapshot had no focused item");
        Check(ShellSelectionRestoreGate.ShouldDeferSelectionRestore(true, 0), expected: true,
            "selected PIDLs wait while the refreshed Shell view is explicitly empty");
        Check(ShellSelectionRestoreGate.ShouldDeferSelectionRestore(true, 3), expected: false,
            "selection restoration proceeds once the Shell view exposes items");
        Check(ShellSelectionRestoreGate.ShouldDeferSelectionRestore(true, -1), expected: false,
            "an unavailable Shell count keeps the existing recovery path");
        Check(ShellSelectionRestoreGate.ShouldDeferSelectionRestore(false, 0), expected: false,
            "focus-only snapshots do not wait for a non-existent selection");

        var gateType = typeof(ShellSelectionRestoreGate);
        var useRefreshSnapshot = gateType.GetMethod("ShouldUseRefreshSnapshot", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("Refresh must prefer a confirmed selection snapshot for the same view and input generation.");
        bool UseRefreshSnapshot(bool hasSnapshot, bool sameView, bool inputUnchanged) =>
            (bool)useRefreshSnapshot.Invoke(null, [hasSnapshot, sameView, inputUnchanged])!;
        Check(UseRefreshSnapshot(true, true, true), expected: true,
            "refresh uses the confirmed selection snapshot when view and user input are unchanged");
        Check(UseRefreshSnapshot(true, true, false), expected: false,
            "refresh ignores a snapshot superseded by user input");
        Check(UseRefreshSnapshot(true, false, true), expected: false,
            "refresh ignores a snapshot from another view");
        Check(UseRefreshSnapshot(false, true, true), expected: false,
            "refresh captures the live selection when no confirmed snapshot exists");

        Check(ShellSelectionRestoreGate.ShouldObserveSelectionCallback(false, true), false,
            "mouse range selection in progress must not trigger snapshot recovery");
        var shouldObserveCallback = gateType.GetMethod("ShouldObserveSelectionCallback", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("Selection callbacks must be ignored while deferred refresh restoration is pending.");
        Check((bool)shouldObserveCallback.Invoke(null, [false, false])!, expected: true,
            "Shell selection callbacks are observed outside deferred refresh restoration");
        Check(!(bool)shouldObserveCallback.Invoke(null, [true, false])!, expected: true,
            "Shell selection callbacks are ignored while deferred refresh restoration is pending");

        var verifyNaturalUpdate = gateType.GetMethod("ShouldVerifyNaturalUpdateSelection", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("Natural Shell updates must verify a cached selection after the view settles.");
        Check((bool)verifyNaturalUpdate.Invoke(null, [true, true, true, true])!, expected: true,
            "a same-view unchanged selection snapshot receives bounded post-update verification");
        Check(!(bool)verifyNaturalUpdate.Invoke(null, [true, false, true, true])!, expected: true,
            "navigation cancels post-update selection verification");
        Check(!(bool)verifyNaturalUpdate.Invoke(null, [true, true, false, true])!, expected: true,
            "new user input cancels post-update selection verification");
        Check(!(bool)verifyNaturalUpdate.Invoke(null, [true, true, true, false])!, expected: true,
            "post-update selection verification ends after its bounded window");

        var restoreNaturalSelection = gateType.GetMethod("ShouldRestoreNaturalUpdateSelection", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new Exception("Natural-update verification must identify a missing saved selection.");
        Check((bool)restoreNaturalSelection.Invoke(null, [2, 0])!, expected: true,
            "a late empty Shell selection restores the saved surviving items");
        Check(!(bool)restoreNaturalSelection.Invoke(null, [2, 2])!, expected: true,
            "an already present selection is not needlessly rewritten");
        Check(!(bool)restoreNaturalSelection.Invoke(null, [0, 0])!, expected: true,
            "an intentionally empty saved selection is never recreated");

        var repoRoot = FindRepoRoot();
        var hostSource = File.ReadAllText(Path.Combine(repoRoot, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs"));
        var traceStart = hostSource.IndexOf("private void TraceShellSelectionRestore(", StringComparison.Ordinal);
        var traceEnd = hostSource.IndexOf("private void ClearLastNativeSelection()", traceStart, StringComparison.Ordinal);
        var traceMethod = traceStart >= 0 && traceEnd > traceStart ? hostSource[traceStart..traceEnd] : string.Empty;
        Check(traceMethod.Contains("if (!_traceNativeUserMessages) return;", StringComparison.Ordinal), expected: true,
            "selection-restore diagnostic strings are retained only when Shell callback tracing is enabled");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "ExplorerBrowserHost.cs")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Check(bool actual, bool expected, string name)
    {
        if (actual != expected) throw new Exception($"Failed: {name}");
        Console.WriteLine($"PASS {name}");
    }
}
