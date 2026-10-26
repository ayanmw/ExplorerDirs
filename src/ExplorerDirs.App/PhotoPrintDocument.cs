using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal static class PhotoPrintDocument
{
    internal static FixedDocument Create(IReadOnlyList<PhotoPrintImageAsset> images,
        PhotoPrintLayoutResult layout, double pageWidth, double pageHeight, bool usePreviewSources)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth <= 0 || pageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageWidth), "Page dimensions must be finite and positive.");
        if (images.Count == 0 || images.Count != layout.Placements.Count || layout.PageCount <= 0 ||
            layout.Placements.Any(placement => placement.PageIndex < 0 || placement.PageIndex >= layout.PageCount))
            throw new InvalidDataException("Photo layout does not match the decoded image list.");

        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);
        var pages = Enumerable.Range(0, layout.PageCount)
            .Select(_ => new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White })
            .ToArray();
        foreach (var (placement, image) in layout.Placements.Select((placement, index) => (placement, images[index])))
        {
            var source = usePreviewSources ? image.PreviewSource : image.Source;
            var printImage = new Image
            {
                Source = Crop(source, placement.SourceCrop),
                Width = placement.Destination.Width,
                Height = placement.Destination.Height,
                Stretch = Stretch.Fill
            };
            FixedPage.SetLeft(printImage, placement.Destination.X);
            FixedPage.SetTop(printImage, placement.Destination.Y);
            pages[placement.PageIndex].Children.Add(printImage);
        }
        foreach (var page in pages)
        {
            page.Measure(new Size(pageWidth, pageHeight));
            page.Arrange(new Rect(0, 0, pageWidth, pageHeight));
            page.UpdateLayout();
            var content = new PageContent { Child = page };
            document.Pages.Add(content);
        }
        return document;
    }

    private static BitmapSource Crop(BitmapSource source, PhotoPrintUnitRect crop)
    {
        if (crop.X == 0 && crop.Y == 0 && crop.Width == 1 && crop.Height == 1) return source;
        var x = Math.Clamp((int)Math.Floor(crop.X * source.PixelWidth), 0, source.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(crop.Y * source.PixelHeight), 0, source.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling((crop.X + crop.Width) * source.PixelWidth), x + 1, source.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling((crop.Y + crop.Height) * source.PixelHeight), y + 1, source.PixelHeight);
        var result = new CroppedBitmap(source, new Int32Rect(x, y, right - x, bottom - y));
        result.Freeze();
        return result;
    }
}
