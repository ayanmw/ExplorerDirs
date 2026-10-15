namespace ExplorerDirs.App;

internal static class FocusTraversal
{
    public static (int Pane, int Stop) Move(int pane, int stop, IReadOnlyList<int> stopCounts, bool reverse)
    {
        if (stopCounts.Count == 0 || stopCounts.Any(count => count < 1))
            throw new ArgumentException("Every visible pane needs at least one focus stop.", nameof(stopCounts));

        pane = Math.Clamp(pane, 0, stopCounts.Count - 1);
        if (stop < 0 || stop >= stopCounts[pane])
            return (pane, reverse ? stopCounts[pane] - 1 : 0);
        if (reverse)
            return stop > 0 ? (pane, stop - 1) :
                ((pane + stopCounts.Count - 1) % stopCounts.Count,
                    stopCounts[(pane + stopCounts.Count - 1) % stopCounts.Count] - 1);
        return stop + 1 < stopCounts[pane] ? (pane, stop + 1) :
            ((pane + 1) % stopCounts.Count, 0);
    }
}
