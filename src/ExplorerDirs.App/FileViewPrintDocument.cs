using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal sealed record FileViewPrintSnapshot(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    FileViewPrintKind Kind,
    IReadOnlyList<BitmapSource?> Images);

internal static class FileViewPrintDocument
{
    internal static FlowDocument Create(FileViewPrintSnapshot snapshot, double printableWidth, double printableHeight)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (printableWidth <= 0 || printableHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(printableWidth), "Printable page dimensions must be positive.");
        if (snapshot.Rows.Any(row => row.Count != snapshot.Columns.Count))
            throw new InvalidDataException("The current view snapshot contains an incomplete row.");
        if (snapshot.Kind != FileViewPrintKind.DetailsTable && snapshot.Images.Count != snapshot.Rows.Count)
            throw new InvalidDataException("The icon snapshot does not match the current view rows.");

        var document = new FlowDocument
        {
            PageWidth = printableWidth,
            PageHeight = printableHeight,
            PagePadding = new Thickness(32),
            ColumnGap = 0,
            ColumnWidth = printableWidth
        };
        var contentWidth = Math.Max(1, printableWidth - document.PagePadding.Left - document.PagePadding.Right);
        document.Blocks.Add(snapshot.Kind switch
        {
            FileViewPrintKind.ThumbnailGrid => CreateThumbnailGrid(snapshot, contentWidth),
            FileViewPrintKind.CompactList => CreateCompactList(snapshot, contentWidth),
            FileViewPrintKind.TileGrid => CreateTileGrid(snapshot, contentWidth),
            _ => CreateDetailsTable(snapshot, contentWidth)
        });
        return document;
    }

    private static Table CreateDetailsTable(FileViewPrintSnapshot snapshot, double pageWidth)
    {
        var table = CreateTable(pageWidth, snapshot.Columns.Count);
        var rows = new TableRowGroup();
        var header = new TableRow();
        foreach (var column in snapshot.Columns)
            header.Cells.Add(new TableCell(new Paragraph(new Run(column))) { FontWeight = FontWeights.Bold });
        rows.Rows.Add(header);
        foreach (var values in snapshot.Rows)
        {
            var row = new TableRow();
            foreach (var value in values) row.Cells.Add(new TableCell(new Paragraph(new Run(value))));
            rows.Rows.Add(row);
        }
        table.RowGroups.Add(rows);
        return table;
    }

    private static Table CreateCompactList(FileViewPrintSnapshot snapshot, double pageWidth)
    {
        var table = CreateTable(pageWidth, 1);
        var rows = new TableRowGroup();
        foreach (var values in snapshot.Rows)
        {
            var row = new TableRow();
            var paragraph = new Paragraph();
            if (snapshot.Images[rows.Rows.Count] is { } image)
                paragraph.Inlines.Add(new InlineUIContainer(CreateImage(image, 16, 16)));
            paragraph.Inlines.Add(new Run(" " + (values.FirstOrDefault() ?? string.Empty)));
            row.Cells.Add(new TableCell(paragraph));
            rows.Rows.Add(row);
        }
        table.RowGroups.Add(rows);
        return table;
    }

    private static Table CreateThumbnailGrid(FileViewPrintSnapshot snapshot, double pageWidth)
    {
        const int columnCount = 3;
        var table = CreateTable(pageWidth, columnCount);
        var rows = new TableRowGroup();
        for (var index = 0; index < snapshot.Rows.Count; index += columnCount)
        {
            var row = new TableRow();
            for (var column = 0; column < columnCount && index + column < snapshot.Rows.Count; column++)
            {
                var itemIndex = index + column;
                var cell = new TableCell();
                if (snapshot.Images[itemIndex] is { } image)
                    cell.Blocks.Add(new BlockUIContainer(CreateImage(image, 96, 96)));
                cell.Blocks.Add(new Paragraph(new Run(snapshot.Rows[itemIndex].FirstOrDefault() ?? string.Empty))
                {
                    TextAlignment = TextAlignment.Center
                });
                row.Cells.Add(cell);
            }
            rows.Rows.Add(row);
        }
        table.RowGroups.Add(rows);
        return table;
    }

    private static Table CreateTileGrid(FileViewPrintSnapshot snapshot, double pageWidth)
    {
        const int columnCount = 2;
        var table = CreateTable(pageWidth, columnCount);
        var rows = new TableRowGroup();
        for (var index = 0; index < snapshot.Rows.Count; index += columnCount)
        {
            var row = new TableRow();
            for (var column = 0; column < columnCount && index + column < snapshot.Rows.Count; column++)
            {
                var itemIndex = index + column;
                var cell = new TableCell();
                if (snapshot.Images[itemIndex] is { } image)
                    cell.Blocks.Add(new BlockUIContainer(CreateImage(image, 48, 48)));
                for (var valueIndex = 0; valueIndex < snapshot.Columns.Count; valueIndex++)
                {
                    var value = snapshot.Rows[itemIndex][valueIndex];
                    var paragraph = new Paragraph(new Run($"{snapshot.Columns[valueIndex]}: {value}"))
                    {
                        FontWeight = valueIndex == 0 ? FontWeights.Bold : FontWeights.Normal
                    };
                    cell.Blocks.Add(paragraph);
                }
                row.Cells.Add(cell);
            }
            rows.Rows.Add(row);
        }
        table.RowGroups.Add(rows);
        return table;
    }

    private static Image CreateImage(BitmapSource source, double width, double height)
    {
        if (!source.IsFrozen) source.Freeze();
        return new Image
        {
            Source = source,
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    private static Table CreateTable(double pageWidth, int columnCount)
    {
        var effectiveColumns = Math.Max(1, columnCount);
        var table = new Table { CellSpacing = 0 };
        for (var index = 0; index < effectiveColumns; index++)
            table.Columns.Add(new TableColumn { Width = new GridLength(pageWidth / effectiveColumns) });
        return table;
    }
}
