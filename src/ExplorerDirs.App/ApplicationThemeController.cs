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
    private Color _systemAccent = ThemePalette.DefaultAccent;
    private Color? _customAccentColor;
    private int _disposed;
    private int _systemRefreshQueued;
    private bool _hasAppliedTheme;
    private ThemePreference _appliedPreference;

    public ThemePreference Preference { get; private set; }
    public ResolvedTheme Resolved { get; private set; }
    public ThemeColors? CurrentColors { get; private set; }
    public Color? CustomAccentColor => _customAccentColor;
    public string? LoadWarning => _store.LoadWarning;
    public event EventHandler? ThemeChanged;

    public ApplicationThemeController(Application application, string profileDirectory)
    {
        _application = application;
        _dispatcher = application.Dispatcher;
        _store = new ThemePreferenceStore(Path.Combine(profileDirectory, "Theme.ini"));
        Preference = _store.Load();
        _customAccentColor = _store.CustomAccentColor;
        try
        {
            _systemThemeMonitor = new SystemThemeMonitor();
            _systemThemeMonitor.ColorValuesChanged += OnSystemColorValuesChanged;
            try { _systemIsDark = _systemThemeMonitor.IsDark; }
            catch (Exception) { _systemIsDark = false; }
            try { _systemAccent = _systemThemeMonitor.Accent; }
            catch (Exception) { _systemAccent = ThemePalette.DefaultAccent; }
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

    public void SetCustomAccentColor(Color? color)
    {
        _store.SaveAccentColor(color);
        _customAccentColor = color;
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

    private void OnSystemColorValuesChanged(object? sender, EventArgs e) => QueueSystemRefresh();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => QueueSystemRefresh();

    private void QueueSystemRefresh()
    {
        // UISettings can notify on a worker thread and Windows emits several
        // property changes per update. One dispatcher pass reads the latest state.
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _systemRefreshQueued, 1) != 0) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _systemRefreshQueued, 0);
            if (Volatile.Read(ref _disposed) != 0) return;
            try { if (_systemThemeMonitor is not null) _systemIsDark = _systemThemeMonitor.IsDark; }
            catch (Exception) { }
            try { if (_systemThemeMonitor is not null) _systemAccent = _systemThemeMonitor.Accent; }
            catch (Exception) { }
            Apply();
        }));
    }

    private void Apply()
    {
        // A queued SystemParameters callback may outlive its controller. It must
        // not replace resources or notify windows after disposal.
        if (Volatile.Read(ref _disposed) != 0) return;
        var resolved = ThemePalette.Resolve(Preference, _systemIsDark);
        var highContrast = SystemParameters.HighContrast;
        ThemeColors? colors = highContrast ? null : ThemePalette.For(resolved, _customAccentColor ?? _systemAccent);
        var visualChanged = !_hasAppliedTheme || colors != CurrentColors || highContrast;
        var preferenceChanged = !_hasAppliedTheme || Preference != _appliedPreference;
        Resolved = resolved;
        if (!visualChanged && !preferenceChanged) return;
        _hasAppliedTheme = true;
        _appliedPreference = Preference;
        if (visualChanged)
        {
            NativeShellTheme.SetProcessTheme(resolved == ResolvedTheme.Dark && !highContrast);
            CurrentColors = colors;
            if (colors is { } palette) SetThemeResources(palette);
            else ClearThemeResources();
        }
        // System -> fixed can resolve to the same colors; menus must still
        // reflect the saved preference even though resources need no repaint.
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetThemeResources(ThemeColors colors)
    {
        SetBrush("ExplorerDirs.WindowBackgroundBrush", colors.WindowBackground);
        SetBrush("ExplorerDirs.WindowTextBrush", colors.WindowText);
        SetBrush("ExplorerDirs.ControlBackgroundBrush", colors.ControlBackground);
        SetBrush("ExplorerDirs.ControlTextBrush", colors.ControlText);
        SetBrush("ExplorerDirs.BorderBrush", colors.Border);
        SetBrush("ExplorerDirs.MutedTextBrush", colors.MutedText);
        SetBrush("ExplorerDirs.AccentBrush", colors.Accent);
        SetBrush("ExplorerDirs.AccentTextBrush", colors.AccentText);
        SetBrush("ExplorerDirs.SelectionBackgroundBrush", colors.SelectionBackground);
        SetBrush("ExplorerDirs.SelectionTextBrush", colors.SelectionText);
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
        // Preserve unchanged brush identities so accent-only updates do not
        // invalidate every neutral surface through DynamicResource replacement.
        if (_application.Resources[key] is SolidColorBrush existing && existing.Color == color) return;
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
            SystemColors.HighlightTextBrushKey, SystemColors.InfoBrushKey, SystemColors.InfoTextBrushKey
        })
            _application.Resources.Remove(key);

        // Semantic aliases must resolve to the live Windows colors in high contrast instead of disappearing.
        _application.Resources["ExplorerDirs.WindowBackgroundBrush"] = SystemColors.WindowBrush;
        _application.Resources["ExplorerDirs.WindowTextBrush"] = SystemColors.WindowTextBrush;
        _application.Resources["ExplorerDirs.ControlBackgroundBrush"] = SystemColors.ControlBrush;
        _application.Resources["ExplorerDirs.ControlTextBrush"] = SystemColors.ControlTextBrush;
        _application.Resources["ExplorerDirs.BorderBrush"] = SystemColors.ControlDarkBrush;
        _application.Resources["ExplorerDirs.MutedTextBrush"] = SystemColors.GrayTextBrush;
        _application.Resources["ExplorerDirs.AccentBrush"] = SystemColors.HighlightBrush;
        _application.Resources["ExplorerDirs.AccentTextBrush"] = SystemColors.HighlightTextBrush;
        _application.Resources["ExplorerDirs.SelectionBackgroundBrush"] = SystemColors.HighlightBrush;
        _application.Resources["ExplorerDirs.SelectionTextBrush"] = SystemColors.HighlightTextBrush;
    }
}
