using System.Windows.Controls;
using System.Windows.Shapes;

namespace ExplorerDirs.App;

internal static class ViewStyleMenu
{
    internal static void Populate(ItemsControl menu, Action<ViewStyle> apply)
    {
        for (var index = 0; index < ViewStyle.All.Count; index++)
        {
            var style = ViewStyle.All[index];
            var item = new MenuItem
            {
                Header = style.Name, Tag = style, IsCheckable = true,
                Icon = CreateMenuIcon(style),
                InputGestureText = $"Ctrl+Shift+{index + 1}"
            };
            item.Click += (_, _) => apply(style);
            menu.Items.Add(item);
        }
    }

    internal static void Update(ItemsControl menu, (uint Mode, int IconSize)? current)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not ViewStyle style) continue;
            item.IsEnabled = current.HasValue;
            // A custom Ctrl+wheel icon size need not match a named preset.
            // Do not claim a nearby preset is the exact current Shell state.
            item.IsChecked = current is { } value && value.Mode == style.Mode &&
                (style.IconSize < 0 || value.IconSize == style.IconSize);
        }
    }

    internal static Path CreateMenuIcon(ViewStyle style)
    {
        var index = -1;
        for (var candidate = 0; candidate < ViewStyle.All.Count; candidate++)
        {
            if (ViewStyle.All[candidate] != style) continue;
            index = candidate;
            break;
        }
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(style));

        return ExplorerChromeIcons.CreatePath(ViewStyleIconSet.All[index]);
    }

    internal static StackPanel CreateToolbarIcon() =>
        ExplorerChromeIcons.CreateButtonContent(ViewStyleIconSet.All[5]);
}
