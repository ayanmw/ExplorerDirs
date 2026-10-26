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
        var children = InternalChildren.Cast<UIElement>().OrderBy(GetOrder).ToArray();
        foreach (var child in children)
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));

        return TabHeaderLayout.Calculate(children.Select(child => child.DesiredSize).ToArray(),
            availableSize.Width, Multiline).DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().OrderBy(GetOrder).ToArray();
        var layout = TabHeaderLayout.Calculate(children.Select(child => child.DesiredSize).ToArray(),
            finalSize.Width, Multiline);
        for (var index = 0; index < children.Length; index++)
        {
            children[index].Arrange(layout.Items[index]);
        }
        return finalSize;
    }
}
