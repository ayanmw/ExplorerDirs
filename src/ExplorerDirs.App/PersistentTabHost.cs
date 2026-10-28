using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

// Keep visited views attached during tab switches to avoid reparenting and focus
// churn. An explicit cross-pane transfer relies on WPF parking/reparenting the
// same native child; the lifetime probe confirms it is not rebuilt on that path.
public sealed class PersistentTabHost : Grid
{
    public void Show(UIElement element)
    {
        if (!Children.Contains(element))
        {
            element.Visibility = Visibility.Collapsed;
            Children.Add(element);
        }
        foreach (UIElement child in Children)
            if (!ReferenceEquals(child, element)) child.Visibility = Visibility.Collapsed;
        element.Visibility = Visibility.Visible;
    }

    public void Remove(UIElement element) => Children.Remove(element);
}
