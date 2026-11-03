using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ExplorerDirs.App;

internal sealed class SpaceAnalysisPercentageBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not decimal percentage ||
            values[1] is not SolidColorBrush accent || percentage <= 0) return Brushes.Transparent;
        var fraction = (double)Math.Clamp(percentage / 100m, 0m, 1m);
        var color = accent.Color;
        color.A = (byte)(color.A * 0.18);
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        brush.GradientStops.Add(new(color, 0));
        brush.GradientStops.Add(new(color, fraction));
        brush.GradientStops.Add(new(Colors.Transparent, fraction));
        brush.GradientStops.Add(new(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
