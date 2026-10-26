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
    Color Accent,
    Color AccentText,
    Color SelectionBackground,
    Color SelectionText,
    Color TooltipBackground,
    Color TooltipText);

internal static class ThemePalette
{
    public static Color DefaultAccent => Color.FromRgb(0, 103, 192);

    public static ResolvedTheme Resolve(ThemePreference preference, bool systemIsDark) => preference switch
    {
        ThemePreference.System => systemIsDark ? ResolvedTheme.Dark : ResolvedTheme.Light,
        ThemePreference.Light => ResolvedTheme.Light,
        ThemePreference.Dark => ResolvedTheme.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(preference))
    };

    public static ThemeColors For(ResolvedTheme theme) => For(theme, DefaultAccent);

    public static ThemeColors For(ResolvedTheme theme, Color accent)
    {
        var neutral = theme switch
        {
            ResolvedTheme.Light => new ThemeColors(
                Color.FromRgb(255, 255, 255), Color.FromRgb(28, 28, 28),
                Color.FromRgb(243, 243, 243), Color.FromRgb(28, 28, 28),
                Color.FromRgb(160, 160, 160), Color.FromRgb(90, 90, 90),
                accent, ContrastingText(accent), Color.FromRgb(0, 103, 192), Color.FromRgb(255, 255, 255),
                Color.FromRgb(255, 255, 225), Color.FromRgb(28, 28, 28)),
            ResolvedTheme.Dark => new ThemeColors(
                Color.FromRgb(32, 32, 32), Color.FromRgb(241, 241, 241),
                Color.FromRgb(45, 45, 45), Color.FromRgb(241, 241, 241),
                Color.FromRgb(86, 86, 86), Color.FromRgb(174, 174, 174),
                accent, ContrastingText(accent), Color.FromRgb(9, 71, 113), Color.FromRgb(255, 255, 255),
                Color.FromRgb(45, 45, 45), Color.FromRgb(241, 241, 241)),
            _ => throw new ArgumentOutOfRangeException(nameof(theme))
        };

        var mix = theme == ResolvedTheme.Light ? 0.18 : 0.34;
        return neutral with { SelectionBackground = Blend(neutral.ControlBackground, accent, mix), SelectionText = ContrastingText(Blend(neutral.ControlBackground, accent, mix)) };
    }

    private static Color Blend(Color background, Color foreground, double foregroundWeight) => Color.FromRgb(
        (byte)Math.Round(background.R * (1 - foregroundWeight) + foreground.R * foregroundWeight),
        (byte)Math.Round(background.G * (1 - foregroundWeight) + foreground.G * foregroundWeight),
        (byte)Math.Round(background.B * (1 - foregroundWeight) + foreground.B * foregroundWeight));

    private static Color ContrastingText(Color background) =>
        ContrastRatio(background, Colors.Black) >= ContrastRatio(background, Colors.White)
            ? Colors.Black
            : Colors.White;

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
    }

    public static bool IsLightForeground(byte red, byte green, byte blue) =>
        5 * green + 2 * red + blue > 8 * 128;
}
