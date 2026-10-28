using System.IO;
using System.Net;
using System.Text;
using OfficeIMO.Excel;

namespace ExplorerDirs.App;

internal enum FileViewExportFormat
{
    Csv,
    Text,
    Html,
    Xls
}

internal sealed record FileViewExportSnapshot(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);

internal static class FileViewExportWriter
{
    private const int XlsRowsPerSheet = 65_535;
    private const int XlsMaximumSheets = 255;
    private static readonly UTF8Encoding Utf8WithBom = new(true);

    internal static void Write(Stream output, FileViewExportFormat format, FileViewExportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!output.CanWrite) throw new ArgumentException("Export output stream must be writable.", nameof(output));
        Validate(snapshot);

        switch (format)
        {
            case FileViewExportFormat.Csv:
                WriteDelimited(output, snapshot, ',');
                break;
            case FileViewExportFormat.Text:
                WriteDelimited(output, snapshot, '\t');
                break;
            case FileViewExportFormat.Html:
                WriteHtml(output, snapshot);
                break;
            case FileViewExportFormat.Xls:
                WriteXls(output, snapshot);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void Validate(FileViewExportSnapshot snapshot)
    {
        if (snapshot.Columns.Count == 0)
            throw new InvalidDataException("The current view has no exportable columns.");
        foreach (var row in snapshot.Rows)
        {
            if (row.Count != snapshot.Columns.Count || row.Any(value => value is null))
                throw new InvalidDataException("The current view snapshot contains an incomplete row.");
        }
    }

    private static void WriteDelimited(Stream output, FileViewExportSnapshot snapshot, char delimiter)
    {
        using var writer = new StreamWriter(output, Utf8WithBom, 64 * 1024, leaveOpen: true);
        WriteRecord(writer, snapshot.Columns, delimiter);
        foreach (var row in snapshot.Rows) WriteRecord(writer, row, delimiter);
        writer.Flush();
    }

    private static void WriteRecord(TextWriter writer, IReadOnlyList<string> cells, char delimiter)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0) writer.Write(delimiter);
            var cell = EscapeSpreadsheetFormula(cells[index]);
            if (cell.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0)
            {
                writer.Write('"');
                writer.Write(cell.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
            else
            {
                writer.Write(cell);
            }
        }
        writer.Write("\r\n");
    }

    private static string EscapeSpreadsheetFormula(string value)
    {
        var first = 0;
        while (first < value.Length && char.IsWhiteSpace(value[first])) first++;
        if (first == value.Length) return value;

        // Spreadsheet applications may evaluate formula-like filenames or metadata
        // when a user opens CSV/TSV. A leading tab is Excel's documented mitigation.
        return value[first] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠'
            ? "\t" + value
            : value;
    }

    private static void WriteHtml(Stream output, FileViewExportSnapshot snapshot)
    {
        using var writer = new StreamWriter(output, Utf8WithBom, 64 * 1024, leaveOpen: true);
        writer.Write("<!doctype html><html><head><meta charset=\"utf-8\"><title>Explorer view</title></head><body><table><thead><tr>");
        foreach (var column in snapshot.Columns)
        {
            writer.Write("<th>");
            writer.Write(WebUtility.HtmlEncode(column));
            writer.Write("</th>");
        }
        writer.Write("</tr></thead><tbody>");
        foreach (var row in snapshot.Rows)
        {
            writer.Write("<tr>");
            foreach (var cell in row)
            {
                writer.Write("<td>");
                writer.Write(WebUtility.HtmlEncode(cell));
                writer.Write("</td>");
            }
            writer.Write("</tr>");
        }
        writer.Write("</tbody></table></body></html>");
        writer.Flush();
    }

    private static void WriteXls(Stream output, FileViewExportSnapshot snapshot)
    {
        if (snapshot.Columns.Count > 256)
            throw new InvalidDataException("The current view exceeds the maximum column count supported by XLS.");
        if (snapshot.Rows.Count > (long)XlsRowsPerSheet * XlsMaximumSheets)
            throw new InvalidDataException("The current view exceeds the maximum row count supported by XLS.");

        using var workbook = ExcelDocument.Create();
        for (var start = 0; start < snapshot.Rows.Count || start == 0; start += XlsRowsPerSheet)
        {
            var sheetIndex = workbook.Sheets.Count;
            var sheet = workbook.AddWorksheet(sheetIndex == 0 ? "View" : $"View {sheetIndex + 1}");
            for (var column = 0; column < snapshot.Columns.Count; column++)
                sheet.CellValue(1, column + 1, snapshot.Columns[column]);
            var end = Math.Min(snapshot.Rows.Count, start + XlsRowsPerSheet);
            for (var index = start; index < end; index++)
                for (var column = 0; column < snapshot.Columns.Count; column++)
                    sheet.CellValue(index - start + 2, column + 1, snapshot.Rows[index][column]);
            if (end == snapshot.Rows.Count) break;
            if (workbook.Sheets.Count >= XlsMaximumSheets)
                throw new InvalidDataException("The current view exceeds the maximum number of XLS sheets.");
        }
        workbook.Save(output, ExcelFileFormat.Xls, null);
    }
}

internal static class FileViewExportFile
{
    internal static void WriteAtomically(string destinationPath, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(output);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
