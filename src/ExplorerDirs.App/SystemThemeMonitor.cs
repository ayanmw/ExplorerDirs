using Windows.UI.ViewManagement;

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
            return !ThemePalette.IsLightForeground(foreground.R, foreground.G, foreground.B);
        }
    }

    public void Dispose() => _settings.ColorValuesChanged -= OnColorValuesChanged;

    private void OnColorValuesChanged(UISettings sender, object args) =>
        ColorValuesChanged?.Invoke(this, EventArgs.Empty);
}
