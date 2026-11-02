using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

internal static class FavoriteMenuBuilder
{
    internal static void Populate(ItemCollection target, IEnumerable<FavoriteEntry> entries, Action<string> navigate)
    {
        var groups = new Dictionary<string, MenuItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var item = new MenuItem { Header = string.IsNullOrWhiteSpace(entry.Name) ? entry.Path : entry.Name,
                ToolTip = entry.Path,
                Icon = new Image { Source = ShellIconProvider.GetIcon(entry.Path, true), Width = 16, Height = 16 } };
            item.Click += (_, e) => { e.Handled = true; navigate(entry.Path); };
            if (entry.Group.Length == 0) target.Add(item);
            else
            {
                if (!groups.TryGetValue(entry.Group, out var parent))
                { parent = new MenuItem { Header = entry.Group }; groups.Add(entry.Group, parent); target.Add(parent); }
                parent.Items.Add(item);
            }
        }
    }
}
