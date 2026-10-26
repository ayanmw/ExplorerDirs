using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExplorerDirs.App;

internal sealed class ApplicationThemeController : IDisposable
{
    private readonly Application _application;
    private readonly Dispatcher _dispatcher;
    private readonly ThemePreferenceStore _store;
    private readonly SystemThemeMonitor? _systemThemeMonitor;
    private bool _systemIsDark;
    private int _disposed;

    public ThemePreference Preference { get; private set; }
    public ResolvedTheme Resolved { get; private set; }
    public string? LoadWarning => _store.LoadWarning;
    public event EventHandler? ThemeChanged;

    public ApplicationThemeController(Application application, string profileDirectory)
    {
        _application = application;
        _dispatcher = application.Dispatcher;
        _store = new ThemePreferenceStore(Path.Combine(profileDirectory, "Theme.ini"));
        Preference = _store.Load();
        try
        {
            _systemThemeMonitor = new SystemThemeMonitor();
            _systemIsDark = _systemThemeMonitor.IsDark;
            _systemThemeMonitor.ColorValuesChanged += OnSystemColorValuesChanged;
        }
        catch (Exception)
        {
            _systemIsDark = false;
        }
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Apply();
    }

    public void SetPreference(ThemePreference preference)
    {
        _store.Save(preference);
        Preference = preference;
        Apply();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (_systemThemeMonitor is not null)
        {
            _systemThemeMonitor.ColorValuesChanged -= OnSystemColorValuesChanged;
            _systemThemeMonitor.Dispose();
        }
    }

    private void OnSystemColorValuesChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || Preference != ThemePreference.System) return;
            try { _systemIsDark = _systemThemeMonitor?.IsDark ?? false; }
            catch (Exception) { return; }
            Apply();
        }));
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _dispatcher.BeginInvoke(new Action(Apply));
    }

    private void Apply()
    {
        Resolved = ThemePalette.Resolve(Preference, _systemIsDark);
        if (SystemParameters.HighContrast)
        {
            ClearThemeResources();
        }
        else
        {
            SetThemeResources(ThemePalette.For(Resolved));
        }
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetThemeResources(ThemeColors colors)
    {
        SetBrush(SystemColors.WindowBrushKey, colors.WindowBackground);
        SetBrush(SystemColors.WindowTextBrushKey, colors.WindowText);
        SetBrush(SystemColors.ControlBrushKey, colors.ControlBackground);
        SetBrush(SystemColors.ControlTextBrushKey, colors.ControlText);
        SetBrush(SystemColors.MenuBrushKey, colors.ControlBackground);
        SetBrush(SystemColors.MenuTextBrushKey, colors.ControlText);
        SetBrush(SystemColors.ControlDarkBrushKey, colors.Border);
        SetBrush(SystemColors.ControlLightBrushKey, colors.ControlBackground);
        SetBrush(SystemColors.GrayTextBrushKey, colors.MutedText);
        SetBrush(SystemColors.HighlightBrushKey, colors.SelectionBackground);
        SetBrush(SystemColors.HighlightTextBrushKey, colors.SelectionText);
        SetBrush(SystemColors.InfoBrushKey, colors.TooltipBackground);
        SetBrush(SystemColors.InfoTextBrushKey, colors.TooltipText);
    }

    private void SetBrush(object key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _application.Resources[key] = brush;
    }

    private void ClearThemeResources()
    {
        foreach (var key in new object[]
        {
            SystemColors.WindowBrushKey, SystemColors.WindowTextBrushKey,
            SystemColors.ControlBrushKey, SystemColors.ControlTextBrushKey,
            SystemColors.MenuBrushKey, SystemColors.MenuTextBrushKey,
            SystemColors.ControlDarkBrushKey, SystemColors.ControlLightBrushKey,
            SystemColors.GrayTextBrushKey, SystemColors.HighlightBrushKey,
            SystemColors.HighlightTextBrushKey, SystemColors.InfoBrushKey,
            SystemColors.InfoTextBrushKey
        })
            _application.Resources.Remove(key);
    }
}
