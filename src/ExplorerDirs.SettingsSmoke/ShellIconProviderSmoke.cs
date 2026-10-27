using ExplorerDirs.App;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellIconProviderSmoke
{
    internal static void Run()
    {
        var folder = ShellIconProvider.GetIcon(Environment.SystemDirectory, isDirectory: true);
        var thisPc = ShellIconProvider.GetIcon("shell:MyComputerFolder", isDirectory: true);
        var recycleBin = ShellIconProvider.GetIcon("shell:RecycleBinFolder", isDirectory: true);
        Check(folder is not null && thisPc is not null && recycleBin is not null,
            "file-system and virtual Shell folder icons are available");

        var folderPixels = GetPixels(folder!);
        var thisPcPixels = GetPixels(thisPc!);
        var recycleBinPixels = GetPixels(recycleBin!);
        Check(!folderPixels.SequenceEqual(thisPcPixels),
            "This PC uses its Shell icon rather than the generic folder icon");
        Check(!folderPixels.SequenceEqual(recycleBinPixels),
            "Recycle Bin uses its Shell icon rather than the generic folder icon");
        Check(!thisPcPixels.SequenceEqual(recycleBinPixels),
            "distinct virtual folders retain distinct Shell icons");
        Console.WriteLine("Shell icon provider smoke: PASS (folder, This PC and Recycle Bin icons are distinct)");
    }

    private static byte[] GetPixels(ImageSource source)
    {
        if (source is not BitmapSource bitmap) throw new InvalidOperationException("Shell icon is not a bitmap.");
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"Shell icon provider: {message}");
    }
}
