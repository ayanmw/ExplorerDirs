namespace ExplorerDirs.App;

internal readonly record struct QuickLinkAvailabilityResult(bool CanAdd, Exception? Error);

internal static class QuickLinkAvailability
{
    internal static QuickLinkAvailabilityResult Evaluate(Func<bool> probe)
    {
        try { return new QuickLinkAvailabilityResult(probe(), null); }
        catch (Exception error) { return new QuickLinkAvailabilityResult(false, error); }
    }
}
