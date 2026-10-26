using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellSelectionRecoverySuppressionSmoke
{
    internal static void Run()
    {
        var suppression = new ShellSelectionRecoverySuppression();
        var retiredNavigationRefresh = suppression.Acquire(41);
        var firstRefresh = suppression.Acquire(42);
        var secondRefresh = suppression.Acquire(42);

        firstRefresh.Dispose();
        Check(suppression.IsSuppressed(42),
            "completion of one overlapping refresh keeps selection recovery suppressed for the other refresh");
        Check(!suppression.IsSuppressed(43),
            "refresh suppression is scoped to the matching navigation version");

        secondRefresh.Dispose();
        Check(!suppression.IsSuppressed(42) && suppression.IsSuppressed(41),
            "a retired navigation refresh does not hold suppression for the current navigation");

        firstRefresh.Dispose();
        retiredNavigationRefresh.Dispose();
        Check(!suppression.IsSuppressed(41) && !suppression.IsSuppressed(42),
            "a completed refresh suppression lease can be disposed more than once");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine($"PASS {description}");
    }
}
