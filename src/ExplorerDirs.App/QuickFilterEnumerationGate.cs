namespace ExplorerDirs.App;

internal enum QuickFilterEnumerationDecision
{
    WaitForEnumeration,
    RestoreGroup,
    RevertFilter
}

internal static class QuickFilterEnumerationGate
{
    internal static QuickFilterEnumerationDecision Decide(
        int initialItemCount, int filterCallbackCount, int visibleItemCount, TimeSpan elapsed, TimeSpan timeout)
    {
        if (initialItemCount <= 0 || filterCallbackCount > 0 || (filterCallbackCount < 0 && visibleItemCount > 0))
            return QuickFilterEnumerationDecision.RestoreGroup;
        return elapsed >= timeout
            ? QuickFilterEnumerationDecision.RevertFilter
            : QuickFilterEnumerationDecision.WaitForEnumeration;
    }
}
