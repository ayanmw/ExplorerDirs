using System.Runtime.InteropServices;
using System.Windows.Media;

namespace ExplorerDirs.App;

internal readonly record struct DwmColorSetting(uint Attribute, uint ColorRef);

internal static class NativeWindowFrameTheme
{
    private const uint DwmColorDefault = 0xffffffff;
    private const uint DwmAttributeBorderColor = 34;
    private const uint DwmAttributeCaptionColor = 35;
    private const uint DwmAttributeTextColor = 36;

    internal static IReadOnlyList<DwmColorSetting> BuildSettings(ThemeColors? colors)
    {
        if (colors is not { } palette)
            return [
                new(DwmAttributeBorderColor, DwmColorDefault),
                new(DwmAttributeCaptionColor, DwmColorDefault),
                new(DwmAttributeTextColor, DwmColorDefault)
            ];

        return [
            new(DwmAttributeBorderColor, ToColorRef(palette.Border)),
            new(DwmAttributeCaptionColor, ToColorRef(palette.WindowBackground)),
            new(DwmAttributeTextColor, ToColorRef(palette.WindowText))
        ];
    }

    internal static void Apply(IntPtr hwnd, ThemeColors? colors)
    {
        if (hwnd == IntPtr.Zero) return;
        foreach (var setting in BuildSettings(colors))
        {
            var colorRef = setting.ColorRef;
            // These color attributes are supported on Windows 11 22000+; older systems reject them.
            _ = DwmSetWindowAttribute(hwnd, setting.Attribute, ref colorRef, sizeof(uint));
        }
    }

    internal static uint ToColorRef(Color color) =>
        (uint)(color.R | (color.G << 8) | (color.B << 16));

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref uint value, uint valueSize);
}
