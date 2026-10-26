using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

// Arrange headers independently of TabControl.Items so moving a header never
// removes the selected HwndHost and its Shell travel log from the visual tree.
public sealed class TabHeaderPanel : Panel
{
    public static readonly DependencyProperty MultilineProperty = DependencyProperty.Register(
        nameof(Multiline), typeof(bool), typeof(TabHeaderPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty OrderProperty = DependencyProperty.RegisterAttached(
        "Order", typeof(int), typeof(TabHeaderPanel),
        new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetOrder(DependencyObject element) => (int)element.GetValue(OrderProperty);
    public static void SetOrder(DependencyObject element, int value) => element.SetValue(OrderProperty, value);

    public bool Multiline
    {
        get => (bool)GetValue(MultilineProperty);
        set => SetValue(MultilineProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        var rowWidth = 0.0;
        var rowHeight = 0.0;
        foreach (var child in InternalChildren.Cast<UIElement>().OrderBy(GetOrder))
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;
            if (Multiline && rowWidth > 0 && rowWidth + childWidth > availableSize.Width)
            {
                width = Math.Max(width, rowWidth);
                height += rowHeight;
                rowWidth = 0;
                rowHeight = 0;
            }
            rowWidth += childWidth;
            rowHeight = Math.Max(rowHeight, childHeight);
        }
        width = Math.Max(width, rowWidth);
        height += rowHeight;
        return new Size(Multiline ? Math.Min(width, availableSize.Width) : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = 0.0;
        var top = 0.0;
        var rowHeight = 0.0;
        foreach (var child in InternalChildren.Cast<UIElement>().OrderBy(GetOrder))
        {
            if (Multiline && left > 0 && left + child.DesiredSize.Width > finalSize.Width)
            {
                left = 0;
                top += rowHeight;
                rowHeight = 0;
            }
            child.Arrange(new Rect(left, top, child.DesiredSize.Width, child.DesiredSize.Height));
            left += child.DesiredSize.Width;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }
        return finalSize;
    }
}
