using System.Windows.Media;

namespace ExplorerDirs.App;

internal enum ResolvedTheme
{
    Light,
    Dark
}

internal readonly record struct ThemeColors(
    Color WindowBackground,
    Color WindowText,
    Color ControlBackground,
    Color ControlText,
    Color Border,
    Color MutedText,
    Color SelectionBackground,
    Color SelectionText,
    Color TooltipBackground,
    Color TooltipText);

internal static class ThemePalette
{
    public static ResolvedTheme Resolve(ThemePreference preference, bool systemIsDark) => preference switch
    {
        ThemePreference.System => systemIsDark ? ResolvedTheme.Dark : ResolvedTheme.Light,
        ThemePreference.Light => ResolvedTheme.Light,
        ThemePreference.Dark => ResolvedTheme.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(preference))
    };

    public static ThemeColors For(ResolvedTheme theme) => theme switch
    {
        ResolvedTheme.Light => new(
            Color.FromRgb(255, 255, 255), Color.FromRgb(28, 28, 28),
            Color.FromRgb(243, 243, 243), Color.FromRgb(28, 28, 28),
            Color.FromRgb(160, 160, 160), Color.FromRgb(90, 90, 90),
            Color.FromRgb(0, 103, 192), Color.FromRgb(255, 255, 255),
            Color.FromRgb(255, 255, 225), Color.FromRgb(28, 28, 28)),
        ResolvedTheme.Dark => new(
            Color.FromRgb(32, 32, 32), Color.FromRgb(241, 241, 241),
            Color.FromRgb(45, 45, 45), Color.FromRgb(241, 241, 241),
            Color.FromRgb(86, 86, 86), Color.FromRgb(174, 174, 174),
            Color.FromRgb(9, 71, 113), Color.FromRgb(255, 255, 255),
            Color.FromRgb(45, 45, 45), Color.FromRgb(241, 241, 241)),
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    public static bool IsLightForeground(byte red, byte green, byte blue) =>
        5 * green + 2 * red + blue > 8 * 128;
}
