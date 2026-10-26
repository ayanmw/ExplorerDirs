using System.Text;
using System.IO;
using ExplorerDirs.App;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.SettingsSmoke;

internal static class PasteSpecialOutputSmoke
{
    internal static void Run()
    {
        Check(PasteSpecialOutput.Extension(PasteSpecialFormat.Png) == ".png", "PNG extension");
        Check(PasteSpecialOutput.Extension(PasteSpecialFormat.Bmp) == ".bmp", "BMP extension");
        Check(PasteSpecialOutput.Extension(PasteSpecialFormat.Jpeg) == ".jpg", "JPEG extension");
        Check(PasteSpecialOutput.Extension(PasteSpecialFormat.Utf8) == ".txt", "UTF-8 extension");
        Check(PasteSpecialOutput.Extension(PasteSpecialFormat.Utf16) == ".txt", "UTF-16 extension");
        Check(PasteSpecialOutput.Extension(PasteSpecialFormat.Ansi) == ".txt", "ANSI extension");

        var text = "A中文";
        Check(PasteSpecialOutput.EncodeText(text, PasteSpecialFormat.Utf8)
            .SequenceEqual(new UTF8Encoding(false).GetBytes(text)), "UTF-8 encoding");
        Check(PasteSpecialOutput.EncodeText(text, PasteSpecialFormat.Utf16)
            .SequenceEqual(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text))), "UTF-16LE BOM");
        Throws<EncoderFallbackException>(() => PasteSpecialOutput.EncodeText(
            text, PasteSpecialFormat.Ansi, Encoding.GetEncoding(20127,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)), "ANSI rejects lossy conversion");

        var pixels = new byte[] { 0x20, 0x80, 0xE0, 0xFF };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        foreach (var format in new[] { PasteSpecialFormat.Png, PasteSpecialFormat.Bmp, PasteSpecialFormat.Jpeg })
        {
            var encoded = PasteSpecialOutput.EncodeImage(bitmap, format);
            using var stream = new MemoryStream(encoded);
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            Check(decoded.Frames.Count == 1 && decoded.Frames[0].PixelWidth == 1 && decoded.Frames[0].PixelHeight == 1,
                $"{format} image round-trip dimensions");
        }

        var root = Path.Combine(Path.GetTempPath(), $"explorer-dirs-paste-special-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var timestamp = new DateTimeOffset(2026, 10, 25, 12, 34, 56, TimeSpan.Zero);
            var first = PasteSpecialOutput.WriteUniqueFile(root, PasteSpecialFormat.Utf8, [1, 2], timestamp);
            var second = PasteSpecialOutput.WriteUniqueFile(root, PasteSpecialFormat.Utf8, [3, 4], timestamp);
            var stem = $"Clipboard-{timestamp.LocalDateTime:yyyyMMdd-HHmmss}";
            Check(Path.GetFileName(first) == $"{stem}.txt", "first timestamp file name");
            Check(Path.GetFileName(second) == $"{stem} (2).txt", "collision suffix");
            Check(File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2 }), "existing file preserved");
            Check(File.ReadAllBytes(second).SequenceEqual(new byte[] { 3, 4 }), "new collision file content");
        }
        finally { Directory.Delete(root, recursive: true); }

        Console.WriteLine("Paste Special output smoke: PASS");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Paste Special output smoke failed: {name}");
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Paste Special output smoke failed: {name}");
    }
}
