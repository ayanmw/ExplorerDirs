using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

// Keep visited HwndHost views attached while another tab is selected. Removing
// one from the visual tree can destroy its HWND and the Shell travel log.
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
