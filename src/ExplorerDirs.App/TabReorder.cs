namespace ExplorerDirs.App;

internal static class TabReorder
{
    public static IReadOnlyList<T> Move<T>(IReadOnlyList<T> items, int source, int target, bool after)
    {
        if (source < 0 || source >= items.Count || target < 0 || target >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(source));

        var reordered = items.ToList();
        var moved = reordered[source];
        reordered.RemoveAt(source);
        var insert = target + (after ? 1 : 0);
        if (source < insert) insert--;
        reordered.Insert(insert, moved);
        return reordered;
    }
}
