namespace ExplorerDirs.App;

internal sealed class ViewStyleWheelAccumulator
{
    private int _remainder;
    private long? _targetGeneration;

    internal int Add(int delta, long targetGeneration)
    {
        if (_targetGeneration != targetGeneration)
        {
            _remainder = 0;
            _targetGeneration = targetGeneration;
        }
        return Add(delta);
    }

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
    internal static bool TryApplyAccumulated(ViewStyleWheelAccumulator accumulator, int delta,
        long targetGeneration, Func<int, bool> apply)
    {
        var notches = accumulator.Add(delta, targetGeneration);
        return notches == 0 || apply(notches * 120);
    }

    internal static int GetFallbackDelta(int completedNotches, bool viewStyleApplied, bool targetCurrent = true) =>
        viewStyleApplied || !targetCurrent ? 0 : completedNotches * 120;
}
