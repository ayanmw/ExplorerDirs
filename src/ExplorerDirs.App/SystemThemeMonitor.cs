using Windows.UI.ViewManagement;
using System.Windows.Media;

namespace ExplorerDirs.App;

internal sealed class SystemThemeMonitor : IDisposable
{
    private readonly UISettings _settings = new();

    public event EventHandler? ColorValuesChanged;

    public SystemThemeMonitor() => _settings.ColorValuesChanged += OnColorValuesChanged;

    public bool IsDark
    {
        get
        {
            var foreground = _settings.GetColorValue(UIColorType.Foreground);
            // Windows exposes light foreground text in dark mode, not light mode.
            return ThemePalette.IsLightForeground(foreground.R, foreground.G, foreground.B);
        }
    }

    public Color Accent
    {
        get
        {
            var accent = _settings.GetColorValue(UIColorType.Accent);
            return Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
        }
    }

    public void Dispose() => _settings.ColorValuesChanged -= OnColorValuesChanged;

    private void OnColorValuesChanged(UISettings sender, object args) =>
        ColorValuesChanged?.Invoke(this, EventArgs.Empty);
}
