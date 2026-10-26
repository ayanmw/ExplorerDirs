using System.Windows.Controls;
using System.Windows.Media;
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

        var icon = new Path
        {
            Data = ViewStyleIconSet.All[index].Clone(),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        icon.SetResourceReference(Shape.StrokeProperty, "ExplorerDirs.ControlTextBrush");
        return icon;
    }

    internal static StackPanel CreateToolbarIcon()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var view = new Path
        {
            Data = ViewStyleIconSet.All[5].Clone(),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };
        view.SetResourceReference(Shape.StrokeProperty, "ExplorerDirs.ControlTextBrush");
        var dropdown = new Path
        {
            Data = ViewStyleIconSet.Dropdown.Clone(),
            Width = 7,
            Height = 6,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Margin = new System.Windows.Thickness(2, 0, 0, 0)
        };
        dropdown.SetResourceReference(Shape.StrokeProperty, "ExplorerDirs.ControlTextBrush");
        panel.Children.Add(view);
        panel.Children.Add(dropdown);
        return panel;
    }
}
