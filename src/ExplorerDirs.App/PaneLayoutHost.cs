using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;

namespace ExplorerDirs.App;

internal static class PaneLayoutHost
{
    private const double DividerSize = 5;
    private const double MinimumPaneHeight = 100;

    // Strip rows exposed clipping at the old 420-DIP window floor. Preserve
    // its two-row chrome allowance, adding a full pane and gap per extra row.
    internal static double MinimumWindowHeight(LayoutPreset layout) =>
        420 + Math.Max(0, layout.Rows - 2) * (MinimumPaneHeight + DividerSize);

    public static void Apply(PaneLayoutGrid grid, IReadOnlyList<UIElement> panes, LayoutPreset layout,
        LayoutProportions? proportions = null, Action<LayoutProportions>? changed = null)
    {
        if (layout.Slots.Length > panes.Count) throw new ArgumentException("布局窗格数超过可用窗格数。", nameof(layout));
        proportions ??= LayoutProportions.Equal;
        if (!proportions.IsValidFor(layout)) throw new ArgumentException("布局比例无效。", nameof(proportions));
        var stripWeights = proportions.StripWeights(Math.Max(layout.Rows, layout.Columns));
        if (grid.Children.Count == 0)
        {
            foreach (var pane in panes) grid.Children.Add(pane);
        }
        else if (grid.Children.Count < panes.Count ||
                 panes.Where((pane, index) => !ReferenceEquals(grid.Children[index], pane)).Any() ||
                 grid.Children.Cast<UIElement>().Skip(panes.Count).Any(child => child is not Thumb))
            throw new InvalidOperationException("布局容器中的窗格对象已改变。");

        // Only dividers are replaced. Removing a pane would destroy hosted HWNDs
        // and the per-tab Shell travel log when switching layouts.
        while (grid.Children.Count > panes.Count) grid.Children.RemoveAt(grid.Children.Count - 1);
        grid.ConfigureQuad(layout.Id == 4 ? proportions : null);
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        for (var column = 0; column < layout.Columns; column++)
        {
            if (column > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerSize) });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(layout.Columns >= 3 ? stripWeights[column] :
                    column == 0 ? proportions.Horizontal : 1 - proportions.Horizontal, GridUnitType.Star),
                MinWidth = 120
            });
        }
        for (var row = 0; row < layout.Rows; row++)
        {
            if (row > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DividerSize) });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(layout.Rows >= 3 ? stripWeights[row] :
                    row == 0 ? proportions.Vertical : 1 - proportions.Vertical, GridUnitType.Star),
                MinHeight = MinimumPaneHeight
            });
        }
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
            Grid.SetColumn(pane, slot.Column * 2);
            Grid.SetRow(pane, slot.Row * 2);
            Grid.SetColumnSpan(pane, slot.ColumnSpan * 2 - 1);
            Grid.SetRowSpan(pane, slot.RowSpan * 2 - 1);
            pane.Visibility = Visibility.Visible;
        }

        if (layout.Id == 4)
        {
            grid.AddQuadDividers(changed);
            return;
        }

        // A T layout has a spanning pane. The cross-divider must stop at its
        // edge, not overlap the native window occupying that spanning cell.
        for (var boundary = 1; boundary < layout.Columns; boundary++) AddDividers(true, layout.Rows, boundary);
        for (var boundary = 1; boundary < layout.Rows; boundary++) AddDividers(false, layout.Columns, boundary);

        void AddDividers(bool columns, int bands, int boundary)
        {
            var first = -1;
            for (var band = 0; band <= bands; band++)
            {
                var available = band < bands && !layout.Slots.Any(slot => columns
                    ? slot.Column < boundary && slot.Column + slot.ColumnSpan > boundary && band >= slot.Row && band < slot.Row + slot.RowSpan
                    : slot.Row < boundary && slot.Row + slot.RowSpan > boundary && band >= slot.Column && band < slot.Column + slot.ColumnSpan);
                if (available) { if (first < 0) first = band; continue; }
                if (first < 0) continue;
                var splitter = new GridSplitter
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Cursor = columns ? Cursors.SizeWE : Cursors.SizeNS,
                    Focusable = false,
                    IsTabStop = false,
                    ShowsPreview = false
                };
                splitter.SetResourceReference(Control.BackgroundProperty, "ExplorerDirs.BorderBrush");
                AutomationProperties.SetName(splitter, columns ? "调整左右窗格宽度" : "调整上下窗格高度");
                Grid.SetColumn(splitter, columns ? boundary * 2 - 1 : first * 2);
                Grid.SetRow(splitter, columns ? first * 2 : boundary * 2 - 1);
                Grid.SetColumnSpan(splitter, columns ? 1 : (band - first) * 2 - 1);
                Grid.SetRowSpan(splitter, columns ? (band - first) * 2 - 1 : 1);
                GridLength[]? beforeDrag = null;
                splitter.DragStarted += (_, _) => beforeDrag = columns
                    ? grid.ColumnDefinitions.Select(definition => definition.Width).ToArray()
                    : grid.RowDefinitions.Select(definition => definition.Height).ToArray();
                splitter.DragCompleted += (_, e) =>
                {
                    if (grid.Children.Contains(splitter))
                    {
                        // GridSplitter changes star weights during DragDelta but
                        // does not undo them on capture-loss cancellation. Restore
                        // chosen weights, not minimum-clamped actual dimensions.
                        if (e.Canceled && beforeDrag is not null)
                        {
                            for (var index = 0; index < beforeDrag.Length; index++)
                                if (columns) grid.ColumnDefinitions[index].Width = beforeDrag[index];
                                else grid.RowDefinitions[index].Height = beforeDrag[index];
                        }
                        else if (!e.Canceled) changed?.Invoke(Capture(grid, layout, proportions));
                    }
                    beforeDrag = null;
                };
                grid.Children.Add(splitter);
                first = -1;
            }
        }
    }

    internal static LayoutProportions Capture(PaneLayoutGrid grid, LayoutPreset layout, LayoutProportions previous)
    {
        if (layout.Id == 4) return grid.QuadProportions ?? previous;
        if (Math.Max(layout.Rows, layout.Columns) >= 3)
        {
            var weights = layout.Columns >= 3
                ? grid.ColumnDefinitions.Where((_, index) => index % 2 == 0).Select(d => d.Width.Value).ToArray()
                : grid.RowDefinitions.Where((_, index) => index % 2 == 0).Select(d => d.Height.Value).ToArray();
            return new(0.5, 0.5, new(weights[0], weights[1], weights[2], weights.Length == 4 ? weights[3] : 0));
        }
        return new(
            layout.Columns == 2 ? Fraction(grid.ColumnDefinitions[0].Width.Value, grid.ColumnDefinitions[2].Width.Value) : previous.Horizontal,
            layout.Rows == 2 ? Fraction(grid.RowDefinitions[0].Height.Value, grid.RowDefinitions[2].Height.Value) : previous.Vertical);
    }

    private static double Fraction(double first, double second) => first / (first + second);
}
