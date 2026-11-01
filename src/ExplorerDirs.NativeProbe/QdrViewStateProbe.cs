using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

internal static class QdrViewStateProbe
{
    public static int Run(string path)
    {
        var imported = QdrImporter.Load(path);
        var tabs = imported.Panes.SelectMany(pane => pane).ToArray();
        var displayKeys = tabs.SelectMany(tab => tab.View.DisplayColumns ?? [])
            .Select(column => Format(column.Key.FormatId, column.Key.PropertyId))
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} x{group.Count()}");
        var sortKeys = tabs.SelectMany(tab => tab.View.SortColumns)
            .Select(column => $"{Format(column.Key.FormatId, column.Key.PropertyId)}/{column.Direction}")
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} x{group.Count()}");
        var groupKeys = tabs.Where(tab => tab.View.GroupKey is not null)
            .Select(tab => $"{Format(tab.View.GroupKey!.FormatId, tab.View.GroupKey.PropertyId)}/{(tab.View.GroupAscending ? 1 : -1)}")
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} x{group.Count()}");
        var modes = tabs.Where(tab => tab.View.DisplayColumns is not null)
            .GroupBy(tab => $"mode={tab.View.Mode},icon={tab.View.IconSize},grouped={tab.View.GroupKey is not null}")
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} x{group.Count()}");

        Console.WriteLine($"qdr-viewstate: tabs={tabs.Length}; panes=[{string.Join('/', imported.Panes.Select(pane => pane.Count))}]; snapshot-tabs={tabs.Count(tab => tab.View.DisplayColumns is not null)}; sorted-tabs={tabs.Count(tab => tab.View.SortColumns.Count > 0)}; warnings={imported.Warnings.Count}");
        Console.WriteLine($"qdr-viewstate: display-keys=[{string.Join("; ", displayKeys)}]");
        Console.WriteLine($"qdr-viewstate: sort-keys=[{string.Join("; ", sortKeys)}]");
        Console.WriteLine($"qdr-viewstate: group-keys=[{string.Join("; ", groupKeys)}]");
        Console.WriteLine($"qdr-viewstate: mode-shapes=[{string.Join("; ", modes)}]");
        return 0;
    }

    private static string Format(Guid formatId, uint propertyId) => $"{{{formatId:D}}}:{propertyId}";
}
