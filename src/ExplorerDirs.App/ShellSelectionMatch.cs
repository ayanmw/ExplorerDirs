namespace ExplorerDirs.App;

internal static class ShellSelectionMatch
{
    internal static int[] FindViewIndexes(IEnumerable<string> viewParsingNames,
        IReadOnlyList<string> selectedParsingNames)
    {
        var selectedIndexesByName = new Dictionary<string, int>(selectedParsingNames.Count,
            StringComparer.OrdinalIgnoreCase);
        foreach (var name in selectedParsingNames)
        {
            if (!string.IsNullOrWhiteSpace(name)) selectedIndexesByName.TryAdd(name, selectedIndexesByName.Count);
        }
        if (selectedIndexesByName.Count == 0) return [];

        int[] matchedViewIndexes = new int[selectedIndexesByName.Count];
        bool[] matched = new bool[selectedIndexesByName.Count];
        var matchCount = 0;
        var viewIndex = 0;
        foreach (var name in viewParsingNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && selectedIndexesByName.TryGetValue(name, out var selectedIndex) &&
                !matched[selectedIndex])
            {
                matched[selectedIndex] = true;
                matchedViewIndexes[selectedIndex] = viewIndex;
                if (++matchCount == matched.Length) return matchedViewIndexes;
            }
            viewIndex++;
        }
        return [];
    }
}
