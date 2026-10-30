namespace ExplorerDirs.App;

internal static class ShellColumnValueMenuValues
{
    internal const int PageSize = 20;

    internal static string[] SortForMenu(HashSet<string> viewValues, IEnumerable<string> selectedValues)
    {
        viewValues.UnionWith(selectedValues);
        var result = viewValues.ToArray();
        Array.Sort(result, static (left, right) =>
        {
            var comparison = StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left, right);
        });
        return result;
    }

    internal static int GetPageCount(int valueCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(valueCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        return valueCount / pageSize + (valueCount % pageSize == 0 ? 0 : 1);
    }

    internal static string[] GetPage(string[] values, int pageIndex, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var start = (long)pageIndex * pageSize;
        if (start >= values.Length) return [];
        var count = Math.Min(pageSize, values.Length - (int)start);
        var page = new string[count];
        Array.Copy(values, (int)start, page, 0, count);
        return page;
    }
}
