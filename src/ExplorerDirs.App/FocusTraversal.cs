namespace ExplorerDirs.App;

internal enum TreeFocusTarget { None, Pane, Shared }

internal readonly record struct PaneFocusStopMap(
    int SearchStart,
    int SearchCount,
    int FilterStart,
    int FilterCount,
    int NavigationStopCount,
    int TreeButton,
    int PaneViewStyle,
    int Address,
    int AddressMenu,
    int AddressComputer,
    int AddressHistory,
    int Go,
    int NewTab,
    int CloseTab,
    int TabHeader,
    int Tree,
    int ColumnHeader,
    int FileView,
    int StatusTools,
    int Count,
    IReadOnlyList<int> MajorStops)
{
    private const int BaseNavigationStopCount = 8;

    // Keep optional bars, tab headers, the tree and native column headers in
    // one index map so Tab and F6 cannot drift when a view's stops change.
    internal static PaneFocusStopMap Create(bool searchVisible, bool quickFilterVisible,
        bool treeVisible, bool hasColumnHeader, bool paneTreeButtonVisible = false,
        bool paneViewStyleButtonVisible = true, bool addressHistoryButtonVisible = false,
        bool addressPathButtonVisible = false, bool addressComputerButtonVisible = false,
        bool tabHeaderVisible = true,
        bool statusBarVisible = true, bool goButtonVisible = true)
    {
        var navigationStopCount = BaseNavigationStopCount + (paneTreeButtonVisible ? 1 : 0) +
            (paneViewStyleButtonVisible ? 1 : 0) +
            (addressHistoryButtonVisible ? 1 : 0) + (addressPathButtonVisible ? 1 : 0) +
            (addressComputerButtonVisible ? 1 : 0) - (goButtonVisible ? 0 : 1);
        var treeButton = paneTreeButtonVisible ? 4 : -1;
        var paneViewStyle = paneViewStyleButtonVisible ? 4 + (paneTreeButtonVisible ? 1 : 0) : -1;
        var address = 4 + (paneTreeButtonVisible ? 1 : 0) + (paneViewStyleButtonVisible ? 1 : 0);
        var addressMenu = addressPathButtonVisible ? address + 1 : -1;
        var addressComputer = addressComputerButtonVisible ? address + 1 + (addressPathButtonVisible ? 1 : 0) : -1;
        var addressHistory = addressHistoryButtonVisible ? address + 1 + (addressPathButtonVisible ? 1 : 0) +
            (addressComputerButtonVisible ? 1 : 0) : -1;
        var afterAddress = address + 1 + (addressHistoryButtonVisible ? 1 : 0) + (addressPathButtonVisible ? 1 : 0) +
            (addressComputerButtonVisible ? 1 : 0);
        var go = goButtonVisible ? afterAddress : -1;
        var newTab = afterAddress + (goButtonVisible ? 1 : 0);
        var closeTab = newTab + 1;
        var searchCount = searchVisible ? 3 : 0;
        var filterCount = quickFilterVisible ? 8 : 0;
        var searchStart = navigationStopCount;
        var filterStart = searchStart + searchCount;
        var nextStop = filterStart + filterCount;
        var tabHeader = tabHeaderVisible ? nextStop : -1;
        var tree = treeVisible ? nextStop + (tabHeaderVisible ? 1 : 0) : -1;
        var columnHeader = nextStop + (tabHeaderVisible ? 1 : 0) + (treeVisible ? 1 : 0);
        var fileView = columnHeader + (hasColumnHeader ? 1 : 0);
        var statusTools = statusBarVisible ? fileView + 1 : -1;
        var majorStops = new List<int> { address };
        if (searchVisible) majorStops.Add(searchStart);
        if (quickFilterVisible) majorStops.Add(filterStart);
        if (tabHeaderVisible) majorStops.Add(tabHeader);
        if (treeVisible) majorStops.Add(tree);
        if (hasColumnHeader) majorStops.Add(columnHeader);
        majorStops.Add(fileView);
        if (statusBarVisible) majorStops.Add(statusTools);
        return new PaneFocusStopMap(searchStart, searchCount, filterStart, filterCount,
            navigationStopCount, treeButton, paneViewStyle, address, addressMenu, addressComputer,
            addressHistory, go, newTab, closeTab,
            tabHeader, tree, columnHeader, fileView, statusTools,
            fileView + (statusBarVisible ? 2 : 1), majorStops);
    }
}

internal static class FocusTraversal
{
    internal static TreeFocusTarget TreeTarget(TreeDisplayMode mode, bool paneTreeVisible) => mode switch
    {
        TreeDisplayMode.Shared => TreeFocusTarget.Shared,
        TreeDisplayMode.PerPane when paneTreeVisible => TreeFocusTarget.Pane,
        _ => TreeFocusTarget.None
    };

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
