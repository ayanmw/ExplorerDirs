using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

internal static class PaneLayoutHost
{
    public static void Apply(Grid grid, IReadOnlyList<UIElement> panes, LayoutPreset layout)
    {
        if (layout.Slots.Length > panes.Count) throw new ArgumentException("布局窗格数超过可用窗格数。", nameof(layout));
        if (grid.Children.Count == 0)
        {
            foreach (var pane in panes) grid.Children.Add(pane);
        }
        else if (grid.Children.Count != panes.Count ||
                 panes.Where((pane, index) => !ReferenceEquals(grid.Children[index], pane)).Any())
            throw new InvalidOperationException("布局容器中的窗格对象已改变。");

        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        for (var column = 0; column < layout.Columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < layout.Rows; row++)
            grid.RowDefinitions.Add(new RowDefinition());
        for (var index = 0; index < panes.Count; index++)
        {
            var pane = panes[index];
            if (index >= layout.Slots.Length)
            {
                pane.Visibility = Visibility.Collapsed;
                Grid.SetColumn(pane, 0);
                Grid.SetRow(pane, 0);
                Grid.SetColumnSpan(pane, 1);
                Grid.SetRowSpan(pane, 1);
                continue;
            }
            var slot = layout.Slots[index];
            Grid.SetColumn(pane, slot.Column);
            Grid.SetRow(pane, slot.Row);
            Grid.SetColumnSpan(pane, slot.ColumnSpan);
            Grid.SetRowSpan(pane, slot.RowSpan);
            pane.Visibility = Visibility.Visible;
        }
    }
}
