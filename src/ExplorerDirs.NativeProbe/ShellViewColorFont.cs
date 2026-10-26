using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

internal readonly record struct ShellItemDrawFontStyle(
    bool? Bold, bool? Italic, bool? Underline, bool? Strikeout)
{
    internal bool HasOverrides => Bold.HasValue || Italic.HasValue || Underline.HasValue || Strikeout.HasValue;
}

internal readonly record struct ShellViewFontDescription(
    int Height, int Weight, bool Italic, bool Underline, bool Strikeout);

internal readonly record struct ShellViewFontSelection(nint DeviceContext, nint PreviousFont, nint CreatedFont);

internal static class ShellViewColorFontPolicy
{
    internal static ShellViewFontDescription Apply(ShellViewFontDescription original, ShellItemDrawFontStyle style) =>
        new(original.Height,
            style.Bold is { } bold ? bold ? 700 : 400 : original.Weight,
            style.Italic ?? original.Italic,
            style.Underline ?? original.Underline,
            style.Strikeout ?? original.Strikeout);

    internal static bool TrySelect(nint deviceContext, ShellItemDrawFontStyle style,
        out ShellViewFontSelection selection)
    {
        selection = default;
        if (deviceContext == 0 || !style.HasOverrides) return false;
        var currentFont = GetCurrentObject(deviceContext, ObjectFont);
        if (currentFont == 0 || GetObjectW(currentFont, Marshal.SizeOf<LogFont>(), out var font) == 0)
            return false;

        var description = Apply(new ShellViewFontDescription(font.Height, font.Weight,
            font.Italic != 0, font.Underline != 0, font.Strikeout != 0), style);
        font.Weight = description.Weight;
        font.Italic = description.Italic ? (byte)1 : (byte)0;
        font.Underline = description.Underline ? (byte)1 : (byte)0;
        font.Strikeout = description.Strikeout ? (byte)1 : (byte)0;
        var createdFont = CreateFontIndirectW(ref font);
        if (createdFont == 0) return false;
        var previousFont = SelectObject(deviceContext, createdFont);
        if (previousFont == 0 || previousFont == new nint(-1))
        {
            DeleteObject(createdFont);
            return false;
        }

        selection = new ShellViewFontSelection(deviceContext, previousFont, createdFont);
        return true;
    }

    internal static void Restore(ShellViewFontSelection selection)
    {
        if (selection.CreatedFont == 0) return;
        _ = SelectObject(selection.DeviceContext, selection.PreviousFont);
        _ = DeleteObject(selection.CreatedFont);
    }

    private const uint ObjectFont = 6; // OBJ_FONT.

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, Strikeout, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }

    [DllImport("gdi32.dll")] private static extern nint GetCurrentObject(nint dc, uint objectType);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", ExactSpelling = true)]
    private static extern int GetObjectW(nint value, int size, out LogFont font);
    [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectW", ExactSpelling = true)]
    private static extern nint CreateFontIndirectW(ref LogFont font);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
}

internal static class ShellViewColorFontSmoke
{
    internal static void Run()
    {
        var original = new ShellViewFontDescription(-15, 500, false, false, false);
        var allSet = ShellViewColorFontPolicy.Apply(original, new ShellItemDrawFontStyle(true, true, true, true));
        Check(allSet == new ShellViewFontDescription(-15, 700, true, true, true),
            "font styling applies bold, italic, underline, and strikeout while preserving height");
        var explicitUnset = ShellViewColorFontPolicy.Apply(
            new ShellViewFontDescription(-15, 700, true, true, true), new ShellItemDrawFontStyle(false, false, false, false));
        Check(explicitUnset == new ShellViewFontDescription(-15, 400, false, false, false),
            "explicit false font rules clear inherited styles");
        var continueStyle = ShellViewColorFontPolicy.Apply(original, new ShellItemDrawFontStyle(null, true, null, null));
        Check(continueStyle == new ShellViewFontDescription(-15, 500, true, false, false),
            "unconfigured font properties retain the native font description");

        var deviceContext = CreateCompatibleDC(0);
        Check(deviceContext != 0, "GDI creates an offscreen device context for font lifecycle verification");
        try
        {
            var originalFont = GetCurrentObject(deviceContext, ObjectFont);
            var selected = ShellViewColorFontPolicy.TrySelect(deviceContext,
                new ShellItemDrawFontStyle(true, true, true, true), out var selection);
            Check(originalFont != 0 && selected,
                "a per-item style selects a temporary font into the callback device context");
            try
            {
                Check(GetCurrentObject(deviceContext, ObjectFont) == selection.CreatedFont,
                    "the temporary styled font is active until item post-paint cleanup");
            }
            finally { ShellViewColorFontPolicy.Restore(selection); }
            Check(GetCurrentObject(deviceContext, ObjectFont) == originalFont,
                "item post-paint cleanup restores the original font before deleting the temporary font");
        }
        finally { _ = DeleteDC(deviceContext); }
    }

    private const uint ObjectFont = 6; // OBJ_FONT.

    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint GetCurrentObject(nint dc, uint objectType);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Color font style failed: {message}.");
        Console.WriteLine($"PASS {message}");
    }
}
