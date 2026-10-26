using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal enum PasteSpecialFormat { Png, Bmp, Jpeg, Utf8, Utf16, Ansi }

internal static class PasteSpecialOutput
{
    internal static string Extension(PasteSpecialFormat format) => format switch
    {
        PasteSpecialFormat.Png => ".png",
        PasteSpecialFormat.Bmp => ".bmp",
        PasteSpecialFormat.Jpeg => ".jpg",
        PasteSpecialFormat.Utf8 or PasteSpecialFormat.Utf16 or PasteSpecialFormat.Ansi => ".txt",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    internal static byte[] EncodeText(string text, PasteSpecialFormat format, Encoding? ansiEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoding = format switch
        {
            PasteSpecialFormat.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            PasteSpecialFormat.Utf16 => new UnicodeEncoding(bigEndian: false, byteOrderMark: true,
                throwOnInvalidBytes: true),
            PasteSpecialFormat.Ansi => ansiEncoding ?? GetWindowsAnsiEncoding(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Text output requires a text format.")
        };
        var bytes = encoding.GetBytes(text);
        return format == PasteSpecialFormat.Utf16
            ? encoding.GetPreamble().Concat(bytes).ToArray()
            : bytes;
    }

    internal static byte[] EncodeImage(BitmapSource image, PasteSpecialFormat format)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapEncoder encoder = format switch
        {
            PasteSpecialFormat.Png => new PngBitmapEncoder(),
            PasteSpecialFormat.Bmp => new BmpBitmapEncoder(),
            PasteSpecialFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = 90 },
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Image output requires an image format.")
        };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    internal static string WriteUniqueFile(string directory, PasteSpecialFormat format, byte[] content,
        DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(content);
        var stem = $"Clipboard-{timestamp.LocalDateTime:yyyyMMdd-HHmmss}";
        var extension = Extension(format);
        for (var suffix = 1; suffix <= 1000; suffix++)
        {
            var fileName = suffix == 1 ? $"{stem}{extension}" : $"{stem} ({suffix}){extension}";
            var path = Path.Combine(directory, fileName);
            FileStream output;
            try
            {
                output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent create can win after the next candidate was checked; preserve it.
                continue;
            }

            try
            {
                using (output)
                    output.Write(content);
                return path;
            }
            catch
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }
        throw new IOException("无法为剪贴板内容分配不冲突的文件名。");
    }

    private static Encoding GetWindowsAnsiEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetACP(), EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
