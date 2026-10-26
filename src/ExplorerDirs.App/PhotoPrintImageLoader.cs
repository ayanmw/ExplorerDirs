using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal sealed record PhotoPrintImageAsset(string Path, BitmapSource Source, BitmapSource PreviewSource)
{
    internal double AspectRatio => (double)Source.PixelWidth / Source.PixelHeight;
}

internal sealed record PhotoPrintImageLoadResult(
    IReadOnlyList<PhotoPrintImageAsset> Images,
    IReadOnlyList<PhotoPrintRejectedItem> RejectedItems);

internal static class PhotoPrintImageLoader
{
    internal const int MaximumPreviewDimension = 1600;

    internal static Task<PhotoPrintImageLoadResult> DecodeAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Task.Run(() => Decode(paths, cancellationToken), cancellationToken);
    }

    private static PhotoPrintImageLoadResult Decode(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        List<PhotoPrintImageAsset> images = new(paths.Count);
        List<PhotoPrintRejectedItem> rejected = [];
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayName = System.IO.Path.GetFileName(path);
            try
            {
                BitmapSource source;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) throw new InvalidDataException("图片没有可读取的帧。");
                    source = decoder.Frames[0];
                }
                if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
                    throw new InvalidDataException("图片尺寸无效。");
                if (!source.IsFrozen) source.Freeze();
                var scale = Math.Min(1d, (double)MaximumPreviewDimension / Math.Max(source.PixelWidth, source.PixelHeight));
                BitmapSource preview = scale < 1
                    ? new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale))
                    : source;
                if (!preview.IsFrozen) preview.Freeze();
                images.Add(new PhotoPrintImageAsset(path, source, preview));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
                                       ArgumentException or FormatException or InvalidOperationException or ExternalException)
            {
                rejected.Add(new PhotoPrintRejectedItem(displayName, $"无法读取图片：{ex.Message}"));
            }
        }
        return new PhotoPrintImageLoadResult(images, rejected);
    }
}
