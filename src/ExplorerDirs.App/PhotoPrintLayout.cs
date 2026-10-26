namespace ExplorerDirs.App;

internal enum PhotoPrintFitMode
{
    Fit,
    Fill
}

internal readonly record struct PhotoPrintRect(double X, double Y, double Width, double Height);
internal readonly record struct PhotoPrintUnitRect(double X, double Y, double Width, double Height);
internal sealed record PhotoPrintPlacement(int PageIndex, PhotoPrintRect Cell,
    PhotoPrintRect Destination, PhotoPrintUnitRect SourceCrop);
internal sealed record PhotoPrintLayoutResult(int PageCount, IReadOnlyList<PhotoPrintPlacement> Placements);

internal static class PhotoPrintLayout
{
    private const double PageMargin = 24;
    private const double CellGap = 12;
    private const double DipsPerInch = 96;
    private static readonly int[] SupportedCounts = [1, 2, 4, 6, 9];

    internal static PhotoPrintLayoutResult Arrange(IReadOnlyList<double> sourceAspectRatios,
        double printableWidth, double printableHeight, int photosPerPage, PhotoPrintFitMode fitMode,
        double? customWidthCentimeters = null, double? customHeightCentimeters = null,
        double printableOriginX = 0, double printableOriginY = 0)
    {
        ArgumentNullException.ThrowIfNull(sourceAspectRatios);
        if (printableWidth <= PageMargin * 2 || printableHeight <= PageMargin * 2 ||
            !double.IsFinite(printableWidth) || !double.IsFinite(printableHeight))
            throw new ArgumentOutOfRangeException(nameof(printableWidth), "Printable page dimensions must be finite and larger than the page margins.");
        if (!double.IsFinite(printableOriginX) || !double.IsFinite(printableOriginY) || printableOriginX < 0 || printableOriginY < 0)
            throw new ArgumentOutOfRangeException(nameof(printableOriginX), "Printable area origin must be finite and non-negative.");
        if (!SupportedCounts.Contains(photosPerPage))
            throw new ArgumentOutOfRangeException(nameof(photosPerPage));
        if (sourceAspectRatios.Any(ratio => !double.IsFinite(ratio) || ratio <= 0))
            throw new ArgumentOutOfRangeException(nameof(sourceAspectRatios), "Image aspect ratios must be finite and positive.");
        if ((customWidthCentimeters is null) != (customHeightCentimeters is null) ||
            customWidthCentimeters is { } width && (!double.IsFinite(width) || width <= 0) ||
            customHeightCentimeters is { } height && (!double.IsFinite(height) || height <= 0))
            throw new ArgumentOutOfRangeException(nameof(customWidthCentimeters), "Custom photo dimensions must both be finite and positive.");

        var (columns, rows) = photosPerPage switch
        {
            1 => (1, 1),
            2 => (2, 1),
            4 => (2, 2),
            6 => (3, 2),
            9 => (3, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(photosPerPage))
        };
        var availableWidth = printableWidth - PageMargin * 2;
        var availableHeight = printableHeight - PageMargin * 2;
        var slotWidth = (availableWidth - CellGap * (columns - 1)) / columns;
        var slotHeight = (availableHeight - CellGap * (rows - 1)) / rows;
        var customWidth = customWidthCentimeters * DipsPerInch / 2.54;
        var customHeight = customHeightCentimeters * DipsPerInch / 2.54;
        if (customWidth > slotWidth || customHeight > slotHeight)
            throw new ArgumentOutOfRangeException(nameof(customWidthCentimeters), "Custom photo dimensions do not fit the selected page layout.");

        List<PhotoPrintPlacement> placements = new(sourceAspectRatios.Count);
        for (var index = 0; index < sourceAspectRatios.Count; index++)
        {
            var pageIndex = index / photosPerPage;
            var slotIndex = index % photosPerPage;
            var column = slotIndex % columns;
            var row = slotIndex / columns;
            var slotX = printableOriginX + PageMargin + column * (slotWidth + CellGap);
            var slotY = printableOriginY + PageMargin + row * (slotHeight + CellGap);
            var cellWidth = customWidth ?? slotWidth;
            var cellHeight = customHeight ?? slotHeight;
            var cell = new PhotoPrintRect(slotX + (slotWidth - cellWidth) / 2,
                slotY + (slotHeight - cellHeight) / 2, cellWidth, cellHeight);
            var ratio = sourceAspectRatios[index];
            var targetRatio = cellWidth / cellHeight;
            if (fitMode == PhotoPrintFitMode.Fill)
            {
                var crop = ratio > targetRatio
                    ? new PhotoPrintUnitRect((1 - targetRatio / ratio) / 2, 0, targetRatio / ratio, 1)
                    : new PhotoPrintUnitRect(0, (1 - ratio / targetRatio) / 2, 1, ratio / targetRatio);
                placements.Add(new PhotoPrintPlacement(pageIndex, cell, cell, crop));
            }
            else if (fitMode == PhotoPrintFitMode.Fit)
            {
                var destinationWidth = ratio > targetRatio ? cellWidth : cellHeight * ratio;
                var destinationHeight = ratio > targetRatio ? cellWidth / ratio : cellHeight;
                var destination = new PhotoPrintRect(cell.X + (cellWidth - destinationWidth) / 2,
                    cell.Y + (cellHeight - destinationHeight) / 2, destinationWidth, destinationHeight);
                placements.Add(new PhotoPrintPlacement(pageIndex, cell, destination,
                    new PhotoPrintUnitRect(0, 0, 1, 1)));
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(fitMode));
            }
        }

        return new PhotoPrintLayoutResult((sourceAspectRatios.Count + photosPerPage - 1) / photosPerPage, placements);
    }
}
