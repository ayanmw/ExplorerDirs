using ExplorerDirs.App;

internal static class FocusTraversalSmoke
{
    internal static void Run()
    {
        var stopCounts = new[] { 15, 14, 12, 11 };
        var totalStops = stopCounts.Sum();
        for (var pane = 0; pane < stopCounts.Length; pane++)
        for (var stop = 0; stop < stopCounts[pane]; stop++)
        {
            var next = FocusTraversal.Move(pane, stop, stopCounts, false);
            Check(FocusTraversal.Move(next.Pane, next.Stop, stopCounts, true) == (pane, stop),
                "Shift+Tab reverses every Tab transition across panes");
            var previous = FocusTraversal.Move(pane, stop, stopCounts, true);
            Check(FocusTraversal.Move(previous.Pane, previous.Stop, stopCounts, false) == (pane, stop),
                "Tab reverses every Shift+Tab transition across panes");

            var visited = new HashSet<(int Pane, int Stop)> { (pane, stop) };
            var cursor = (Pane: pane, Stop: stop);
            for (var index = 1; index < totalStops; index++)
            {
                cursor = FocusTraversal.Move(cursor.Pane, cursor.Stop, stopCounts, false);
                Check(visited.Add(cursor), "a full Tab cycle visits every focus stop once");
            }
            Check(FocusTraversal.Move(cursor.Pane, cursor.Stop, stopCounts, false) == (pane, stop),
                "a full Tab cycle returns to its starting focus stop");
        }

        IReadOnlyList<int>[] majorStops = [[4, 8, 9, 10], [4, 8, 9], [4, 8, 9, 10], [4, 8, 9]];
        for (var pane = 0; pane < majorStops.Length; pane++)
        foreach (var stop in majorStops[pane])
        {
            var next = FocusTraversal.MoveMajor(pane, stop, majorStops, false);
            Check(FocusTraversal.MoveMajor(next.Pane, next.Stop, majorStops, true) == (pane, stop),
                "Shift+F6 reverses every F6 transition across major pane regions");
            var previous = FocusTraversal.MoveMajor(pane, stop, majorStops, true);
            Check(FocusTraversal.MoveMajor(previous.Pane, previous.Stop, majorStops, false) == (pane, stop),
                "F6 reverses every Shift+F6 transition across major pane regions");
        }

        for (var options = 0; options < 512; options++)
        {
            var hasSearch = (options & 1) != 0;
            var hasFilter = (options & 2) != 0;
            var hasTree = (options & 4) != 0;
            var hasColumnHeader = (options & 8) != 0;
            var hasTreeButton = (options & 16) != 0;
            var hasAddressHistoryButton = (options & 32) != 0;
            var hasAddressMenuButton = (options & 64) != 0;
            var hasPaneViewStyleButton = (options & 128) != 0;
            var hasTabHeader = (options & 256) != 0;
            var map = PaneFocusStopMap.Create(hasSearch, hasFilter, hasTree, hasColumnHeader,
                paneTreeButtonVisible: hasTreeButton,
                paneViewStyleButtonVisible: hasPaneViewStyleButton,
                addressHistoryButtonVisible: hasAddressHistoryButton,
                addressPathButtonVisible: hasAddressMenuButton,
                tabHeaderVisible: hasTabHeader);
            var expectedSearchCount = hasSearch ? 3 : 0;
            var expectedFilterCount = hasFilter ? 8 : 0;
            // The per-pane view-style button is keyboard-focusable in the actual toolbar;
            // it must remain in the custom Tab cycle instead of being skipped.
            var expectedNavigationStops = 8 + (hasTreeButton ? 1 : 0) + (hasPaneViewStyleButton ? 1 : 0) +
                (hasAddressHistoryButton ? 1 : 0) + (hasAddressMenuButton ? 1 : 0);
            var expectedPaneViewStyle = hasPaneViewStyleButton ? 4 + (hasTreeButton ? 1 : 0) : -1;
            var expectedAddress = 4 + (hasTreeButton ? 1 : 0) + (hasPaneViewStyleButton ? 1 : 0);
            var expectedAddressMenu = hasAddressMenuButton ? expectedAddress + 1 : -1;
            var expectedAddressHistory = hasAddressHistoryButton ? expectedAddress + 1 + (hasAddressMenuButton ? 1 : 0) : -1;
            var expectedGo = expectedAddress + 1 + (hasAddressHistoryButton ? 1 : 0) + (hasAddressMenuButton ? 1 : 0);
            var nextAfterFilter = expectedNavigationStops + expectedSearchCount + expectedFilterCount;
            var expectedTabHeader = hasTabHeader ? nextAfterFilter : -1;
            var expectedTree = nextAfterFilter + (hasTabHeader ? 1 : 0);
            var expectedColumnHeader = nextAfterFilter + (hasTabHeader ? 1 : 0) + (hasTree ? 1 : 0);
            var expectedFileView = expectedColumnHeader + (hasColumnHeader ? 1 : 0);
            Check(map.NavigationStopCount == expectedNavigationStops && map.TreeButton == (hasTreeButton ? 4 : -1) &&
                  map.PaneViewStyle == expectedPaneViewStyle &&
                  map.AddressMenu == expectedAddressMenu && map.AddressHistory == expectedAddressHistory && map.Address == expectedAddress && map.Go == expectedGo &&
                  map.NewTab == expectedGo + 1 && map.CloseTab == expectedGo + 2 &&
                  map.SearchStart == expectedNavigationStops && map.SearchCount == expectedSearchCount &&
                  map.FilterStart == expectedNavigationStops + expectedSearchCount && map.FilterCount == expectedFilterCount &&
                  map.TabHeader == expectedTabHeader && map.Tree == (hasTree ? expectedTree : -1) &&
                  map.ColumnHeader == expectedColumnHeader && map.FileView == expectedFileView &&
                  map.StatusTools == expectedFileView + 1,
                "visible pane tree button and optional controls occupy their specified Tab stops in order");
            var expectedStops = new List<int>(Enumerable.Range(0, expectedNavigationStops));
            if (hasSearch) expectedStops.AddRange([map.SearchStart, map.SearchStart + 1, map.SearchStart + 2]);
            if (hasFilter) expectedStops.AddRange(Enumerable.Range(map.FilterStart, 8));
            if (hasTabHeader) expectedStops.Add(map.TabHeader);
            if (hasTree) expectedStops.Add(map.Tree);
            if (hasColumnHeader) expectedStops.Add(map.ColumnHeader);
            expectedStops.AddRange([map.FileView, map.StatusTools]);
            Check(map.Count == expectedStops.Count && expectedStops.Distinct().Count() == expectedStops.Count &&
                  expectedStops.Order().SequenceEqual(Enumerable.Range(0, map.Count)),
                "every dynamic pane focus layout assigns each Tab stop exactly once");
            var expectedMajorStops = new List<int> { expectedAddress };
            if (hasSearch) expectedMajorStops.Add(expectedNavigationStops);
            if (hasFilter) expectedMajorStops.Add(expectedNavigationStops + expectedSearchCount);
            if (hasTabHeader) expectedMajorStops.Add(expectedTabHeader);
            if (hasTree) expectedMajorStops.Add(expectedTree);
            if (hasColumnHeader) expectedMajorStops.Add(expectedColumnHeader);
            expectedMajorStops.AddRange([expectedFileView, expectedFileView + 1]);
            Check(map.MajorStops.SequenceEqual(expectedMajorStops),
                "F6 maps only to visible navigation, bars, tab, tree, headers, file view, and status tools");

            var tabCursor = (Pane: 0, Stop: 0);
            for (var index = 0; index < map.Count; index++)
            {
                Check(tabCursor == (0, index), "Tab follows the pane's declared focus order");
                tabCursor = FocusTraversal.Move(tabCursor.Pane, tabCursor.Stop, [map.Count], false);
            }
            Check(tabCursor == (0, 0), "Tab wraps after all dynamic pane focus stops");

            var majorCursor = (Pane: 0, Stop: map.MajorStops[0]);
            for (var index = 0; index < map.MajorStops.Count; index++)
            {
                Check(majorCursor == (0, map.MajorStops[index]), "F6 follows visible major focus regions");
                majorCursor = FocusTraversal.MoveMajor(majorCursor.Pane, majorCursor.Stop,
                    [map.MajorStops], false);
            }
            Check(majorCursor == (0, map.MajorStops[0]), "F6 wraps after all visible major regions");
        }

        var sharedTreePane = PaneFocusStopMap.Create(false, false, treeVisible: true, hasColumnHeader: true);
        Check(sharedTreePane.MajorStops.Contains(sharedTreePane.Tree) &&
              sharedTreePane.ColumnHeader == sharedTreePane.Tree + 1,
            "a shared Shell tree occupies the same Tab and F6 position as a pane tree");
        Check(FocusTraversal.TreeTarget(TreeDisplayMode.Shared, paneTreeVisible: false) == TreeFocusTarget.Shared &&
              FocusTraversal.TreeTarget(TreeDisplayMode.PerPane, paneTreeVisible: true) == TreeFocusTarget.Pane &&
              FocusTraversal.TreeTarget(TreeDisplayMode.PerPane, paneTreeVisible: false) == TreeFocusTarget.None &&
              FocusTraversal.TreeTarget(TreeDisplayMode.None, paneTreeVisible: true) == TreeFocusTarget.None,
            "focus routing selects exactly the tree instance visible in the current mode");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Focus traversal: {label}");
    }
}
