internal static class PrivateDesktopIsolationPolicy
{
    internal static bool IsClean(bool foregroundChangedObserved, bool foregroundUnchangedAtExit,
        bool inputChangedObserved) =>
        !foregroundChangedObserved && foregroundUnchangedAtExit && !inputChangedObserved;
}
