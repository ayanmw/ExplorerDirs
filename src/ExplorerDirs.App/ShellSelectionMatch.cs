namespace ExplorerDirs.App;

internal static class ShellSelectionMatch
{
    internal static int[] FindViewIndexes(IReadOnlyList<string> viewParsingNames,
        IReadOnlyList<string> selectedParsingNames)
    {
        var indexesByName = new Dictionary<string, int>(viewParsingNames.Count,
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < viewParsingNames.Count; index++)
        {
            var name = viewParsingNames[index];
            if (!string.IsNullOrWhiteSpace(name)) indexesByName.TryAdd(name, index);
        }

        List<int> matches = new(selectedParsingNames.Count);
        HashSet<int> usedIndexes = [];
        foreach (var name in selectedParsingNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !indexesByName.TryGetValue(name, out var index) ||
                !usedIndexes.Add(index)) continue;
            matches.Add(index);
        }
        return matches.ToArray();
    }
}
