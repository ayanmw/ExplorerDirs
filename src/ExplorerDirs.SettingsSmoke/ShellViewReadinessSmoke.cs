using ExplorerDirs.App;

internal static class ShellViewReadinessSmoke
{
    internal static void Run()
    {
        var state = new ShellViewReadiness();
        state.Begin("home");
        state.Complete("home");
        state.Begin("offline");
        var beforeFailure = state.Generation;
        state.Fail("offline");
        Check(!state.Pending && state.Generation > beforeFailure,
            "current failure ends pending and invalidates in-flight view reads");

        state.Begin("old");
        state.Begin("new");
        state.Fail("old");
        Check(state.Pending, "old failure cannot unlock newer navigation");
        state.Complete("new");
        Check(!state.Pending, "new completion unlocks view");

        state.Begin("older-success");
        state.Begin("newer-pending");
        state.Complete("older-success");
        Check(state.Pending, "old success cannot unlock newer navigation either");
        state.Fail("newer-pending");
        Check(!state.Pending, "current failure still releases view after older completion");

        state.Begin("same");
        state.Begin("same");
        state.Fail("same");
        Check(state.Pending, "same-location callbacks are ambiguous until all matching requests finish");
        state.Complete("same");
        Check(!state.Pending, "same-location requests eventually finish");

        state.Begin("revisit");
        state.Begin("between");
        state.Complete("between");
        state.Begin("revisit");
        state.Fail("revisit");
        Check(state.Pending, "old revisit remains tracked across completion of another location");
        state.Fail("revisit");
        Check(!state.Pending, "last revisit failure releases pending");

        state.Begin("current");
        state.Fail("unrelated");
        state.Fail(null);
        Check(state.Pending, "unmatched or unreadable identities do not release a known request");
        state.Complete("current");
        state.Begin(null);
        state.Begin(null);
        state.Fail(null);
        Check(state.Pending, "unidentified overlapping callbacks are treated conservatively");
        state.Complete(null);
        Check(!state.Pending, "unidentified batch can settle after all callbacks");

        state.Begin("dispose");
        state.Reset();
        state.Complete("dispose");
        state.Fail("dispose");
        Check(state.Pending, "late callback after reset cannot reopen a destroyed host");

        var publishing = new ShellViewReadiness();
        publishing.Begin("old-address");
        publishing.Begin("current-address");
        Check(publishing.Complete("current-address"), "current completion authorizes publication");
        var currentVersion = publishing.NavigationVersion;
        Check(!publishing.Complete("old-address"), "late old completion cannot publish address or capacity");
        Check(publishing.IsCurrent(currentVersion),
            "settling an old request does not invalidate queued work for the current location");
        Check(!publishing.Fail("unrelated"), "unmatched failure cannot publish an error");
        publishing.Begin("other-address");
        Check(!publishing.IsCurrent(currentVersion), "new navigation invalidates queued restoration immediately");
        Check(publishing.Fail("other-address"), "current failure authorizes its error");
        publishing.Begin("current-address");
        Check(publishing.Complete("current-address") && !publishing.IsCurrent(currentVersion),
            "revisiting the same path does not revive callbacks from an earlier visit");
        var beforeReset = publishing.NavigationVersion;
        publishing.Reset();
        Check(!publishing.IsCurrent(beforeReset) && !publishing.Complete("current-address"),
            "reset invalidates publication tokens and rejects late completions");

        publishing.Begin("duplicate");
        publishing.Begin("duplicate");
        Check(!publishing.Complete("duplicate") && publishing.Pending,
            "ambiguous first duplicate completion cannot publish");
        Check(publishing.Fail("duplicate") && !publishing.Pending,
            "last matching callback settles the ambiguous batch");
        Check(publishing.RequiresLocationRead,
            "mixed duplicate callbacks require actual view location, not the last failure target");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Shell readiness: " + message);
    }
}
