internal static class PrivateDesktopIsolationPolicySmoke
{
    internal static int Run()
    {
        Check(PrivateDesktopIsolationPolicy.IsClean(
            foregroundChangedObserved: false, foregroundUnchangedAtExit: true, inputChangedObserved: false),
            "an unchanged foreground and input tick should pass");
        Check(!PrivateDesktopIsolationPolicy.IsClean(
            foregroundChangedObserved: true, foregroundUnchangedAtExit: true, inputChangedObserved: false),
            "a transient foreground change must fail even after the foreground is restored");
        Check(!PrivateDesktopIsolationPolicy.IsClean(
            foregroundChangedObserved: false, foregroundUnchangedAtExit: true, inputChangedObserved: true),
            "user input during a probe must fail even when the foreground is stable");
        Check(!PrivateDesktopIsolationPolicy.IsClean(
            foregroundChangedObserved: false, foregroundUnchangedAtExit: false, inputChangedObserved: false),
            "a different foreground at exit must fail");
        Console.WriteLine("private-desktop-isolation-policy-smoke=passed; transient-foreground-rejected=True; input-overlap-rejected=True; stable-run-accepted=True");
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Private Desktop isolation policy smoke: " + message);
    }
}
