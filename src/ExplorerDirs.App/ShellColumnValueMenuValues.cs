namespace ExplorerDirs.App;

internal static class ShellColumnValueMenuValues
{
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
}
