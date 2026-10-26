using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ExplorerDirs.App;

internal enum ExplorerChromeIcon
{
    Back,
    Forward,
    Up,
    Refresh,
    Tree,
    NewTab,
    NewWindow,
    DuplicateTab,
    Close,
    Search,
    History,
    AddressDropDown
}

internal static class ExplorerChromeIcons
{
    internal static IReadOnlyList<Geometry> All { get; } = Array.AsReadOnly(new[]
    {
        Geometry.Parse("M10,2 L4,8 L10,14 M4,8 H15"),
        Geometry.Parse("M6,2 L12,8 L6,14 M1,8 H12"),
        Geometry.Parse("M8,12 V3 M4,7 L8,3 L12,7 M2,14 H14"),
        Geometry.Parse("M12,5 C10,2 5,2 3,5 L2,7 M2,3 V7 H6 M4,11 C6,14 11,14 13,11 L14,9 M14,13 V9 H10"),
        Geometry.Parse("M2,1 H7 V5 H2 Z M9,11 H14 V15 H9 Z M4.5,5 V8 H11.5 V11 M7,8 H4.5 V11"),
        Geometry.Parse("M2,2 H10 L14,6 V14 H2 Z M10,2 V6 H14 M8,8 V13 M5.5,10.5 H10.5"),
        Geometry.Parse("M1,3 H10 V14 H1 Z M4,1 H15 V12 M10,1 H15 V6"),
        Geometry.Parse("M4,2 H13 V12 H4 Z M2,4 V14 H11 M7,6 H10 M8.5,4.5 V7.5"),
        Geometry.Parse("M3,3 L13,13 M13,3 L3,13"),
        Geometry.Parse("M7,2 A5,5 0 1 0 7.1,2 M11,11 L15,15 M5,5 L9,9"),
        Geometry.Parse("M8,2 A6,6 0 1 0 8.1,2 M8,4 V8 L11,10"),
        Geometry.Parse("M2,5 L8,11 L14,5")
    });

    internal static Geometry For(ExplorerChromeIcon icon) => All[(int)icon];

    internal static Path CreatePath(Geometry geometry, double size = 16, double strokeThickness = 1.35)
    {
        var path = new Path
        {
            Data = geometry.Clone(),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeThickness = strokeThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        path.SetResourceReference(Shape.StrokeProperty, "ExplorerDirs.ControlTextBrush");
        return path;
    }

    internal static StackPanel CreateButtonContent(Geometry geometry, double iconSize = 16)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        content.Children.Add(CreatePath(geometry, iconSize, 1.25));
        var dropdown = CreatePath(For(ExplorerChromeIcon.AddressDropDown), 7, 1.2);
        dropdown.Height = 6;
        dropdown.Margin = new Thickness(2, 0, 0, 0);
        content.Children.Add(dropdown);
        return content;
    }
}
