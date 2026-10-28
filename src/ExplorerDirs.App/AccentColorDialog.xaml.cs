using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ExplorerDirs.App;

public partial class AccentColorDialog : Window
{
    public Color? SelectedColor { get; private set; }

    public AccentColorDialog(Color initialColor)
    {
        InitializeComponent();
        ColorText.Text = $"#{initialColor.R:X2}{initialColor.G:X2}{initialColor.B:X2}";
        Loaded += (_, _) =>
        {
            ColorText.Focus();
            ColorText.SelectAll();
        };
    }

    private void ColorText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        if (TryParseColor(ColorText.Text, out var color))
        {
            ColorPreview.Background = new SolidColorBrush(color);
            ValidationText.Text = string.Empty;
        }
        else
        {
            ValidationText.Text = "请输入 #RRGGBB 格式的颜色。";
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseColor(ColorText.Text, out var color))
        {
            ValidationText.Text = "请输入 #RRGGBB 格式的颜色。";
            return;
        }
        SelectedColor = color;
        DialogResult = true;
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        if (value.Length != 7 || value[0] != '#' ||
            !byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;
        color = Color.FromRgb(red, green, blue);
        return true;
    }
}
