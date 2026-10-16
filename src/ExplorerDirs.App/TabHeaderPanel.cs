using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

// Arrange headers independently of TabControl.Items so moving a header never
// removes the selected HwndHost and its Shell travel log from the visual tree.
public sealed class TabHeaderPanel : Panel
{
    public static readonly DependencyProperty OrderProperty = DependencyProperty.RegisterAttached(
        "Order", typeof(int), typeof(TabHeaderPanel),
        new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetOrder(DependencyObject element) => (int)element.GetValue(OrderProperty);
    public static void SetOrder(DependencyObject element, int value) => element.SetValue(OrderProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = 0.0;
        foreach (var child in InternalChildren.Cast<UIElement>().OrderBy(GetOrder))
        {
            child.Arrange(new Rect(left, 0, child.DesiredSize.Width, finalSize.Height));
            left += child.DesiredSize.Width;
        }
        return finalSize;
    }
}
