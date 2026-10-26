using ExplorerDirs.App;

internal static class QuickFilterEnumerationGateSmoke
{
    internal static void Run()
    {
        var timeout = TimeSpan.FromSeconds(2);
        Require(QuickFilterEnumerationGate.Decide(4, 0, 0, TimeSpan.Zero, timeout) == QuickFilterEnumerationDecision.WaitForEnumeration,
            "a temporarily empty Shell view must keep the saved group detached while enumeration has not started");
        Require(QuickFilterEnumerationGate.Decide(4, 4, 0, TimeSpan.FromMilliseconds(50), timeout) == QuickFilterEnumerationDecision.RestoreGroup,
            "completed filtering restores grouping even when no rows match");
        Require(QuickFilterEnumerationGate.Decide(4, 0, 2, TimeSpan.FromMilliseconds(50), timeout) == QuickFilterEnumerationDecision.WaitForEnumeration,
            "stale rows do not prove the new filter enumeration has started");
        Require(QuickFilterEnumerationGate.Decide(4, -1, 2, TimeSpan.FromMilliseconds(50), timeout) == QuickFilterEnumerationDecision.RestoreGroup,
            "visible rows allow group restoration after an unfiltered recovery refresh");
        Require(QuickFilterEnumerationGate.Decide(0, 0, 0, TimeSpan.Zero, timeout) == QuickFilterEnumerationDecision.RestoreGroup,
            "an initially empty folder does not wait for callbacks that cannot arrive");
        Require(QuickFilterEnumerationGate.Decide(4, 0, 0, timeout, timeout) == QuickFilterEnumerationDecision.RevertFilter,
            "enumeration timeout restores an unfiltered view instead of leaving a blank folder");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Quick-filter enumeration regression: " + description);
    }
}
