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

    public static (int Pane, int Stop) MoveMajor(int pane, int stop,
        IReadOnlyList<IReadOnlyList<int>> majorStops, bool reverse)
    {
        if (majorStops.Count == 0 || majorStops.Any(stops => stops.Count == 0))
            throw new ArgumentException("Every visible pane needs a major focus region.", nameof(majorStops));

        pane = Math.Clamp(pane, 0, majorStops.Count - 1);
        var current = majorStops[pane];
        if (stop < 0)
            return (pane, reverse ? current[^1] : current[0]);

        if (reverse)
        {
            for (var index = current.Count - 1; index >= 0; index--)
                if (current[index] < stop) return (pane, current[index]);
            pane = (pane + majorStops.Count - 1) % majorStops.Count;
            return (pane, majorStops[pane][^1]);
        }

        foreach (var region in current)
            if (region > stop) return (pane, region);
        pane = (pane + 1) % majorStops.Count;
        return (pane, majorStops[pane][0]);
    }
}
