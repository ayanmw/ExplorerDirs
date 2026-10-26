namespace ExplorerDirs.App;

internal sealed class ViewStyleWheelAccumulator
{
    private int _remainder;

    internal int Add(int delta)
    {
        // WM_MOUSEWHEEL uses 120 for one notch, but precision wheels may send fractions.
        var total = _remainder + delta;
        var notches = total / 120;
        _remainder = total % 120;
        return notches;
    }
}

internal static class ViewStyleWheelDispatch
{
    internal static int GetFallbackDelta(int completedNotches, bool viewStyleApplied) =>
        viewStyleApplied ? 0 : completedNotches * 120;
}
