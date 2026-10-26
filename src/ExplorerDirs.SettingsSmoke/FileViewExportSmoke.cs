using System.IO;
using System.Text;
using ExplorerDirs.App;
using OfficeIMO.Excel;

internal static class FileViewExportSmoke
{
    internal static void Run()
    {
        var snapshot = new FileViewExportSnapshot(
            ["Name", "Details"],
            [
                ["a,\"b.txt", "line1\r\nline2"],
                ["=SUM(1,1)", "<&>\""]
            ]);

        var csv = WriteText(FileViewExportFormat.Csv, snapshot);
        Require(HasUtf8Bom(WriteBytes(FileViewExportFormat.Csv, snapshot)), "CSV is emitted with a UTF-8 BOM");
        Require(csv == "Name,Details\r\n\"a,\"\"b.txt\",\"line1\r\nline2\"\r\n\"\t=SUM(1,1)\",\"<&>\"\"\"\r\n",
            "CSV quotes delimiters, quotes, and embedded newlines while neutralizing formula cells");

        var formulaSnapshot = new FileViewExportSnapshot(
            ["Name"],
            [["=SUM(1,1)"], ["+1"], ["-1"], ["@cmd"], [" \t=1+1"], ["＝1+1"], ["ordinary.txt"]]);
        foreach (var format in new[] { FileViewExportFormat.Csv, FileViewExportFormat.Text })
        {
            var output = WriteText(format, formulaSnapshot);
            Require(output.Contains("\"\t=SUM(1,1)\"", StringComparison.Ordinal) &&
                    output.Contains("\t+1", StringComparison.Ordinal) &&
                    output.Contains("\t-1", StringComparison.Ordinal) &&
                    output.Contains("\t@cmd", StringComparison.Ordinal) &&
                    output.Contains("\t \t=1+1", StringComparison.Ordinal) &&
                    output.Contains("\t＝1+1", StringComparison.Ordinal) &&
                    output.Contains("ordinary.txt", StringComparison.Ordinal),
                $"{format} neutralizes spreadsheet formulas while preserving ordinary text");
        }

        var text = WriteText(FileViewExportFormat.Text, snapshot);
        Require(HasUtf8Bom(WriteBytes(FileViewExportFormat.Text, snapshot)), "tab-separated text is emitted with a UTF-8 BOM");
        Require(text == "Name\tDetails\r\n\"a,\"\"b.txt\"\t\"line1\r\nline2\"\r\n\"\t=SUM(1,1)\"\t\"<&>\"\"\"\r\n",
            "tab-separated text quotes embedded delimiters and newlines and neutralizes formula cells");

        var html = WriteText(FileViewExportFormat.Html, snapshot);
        Require(HasUtf8Bom(WriteBytes(FileViewExportFormat.Html, snapshot)), "HTML is emitted with a UTF-8 BOM");
        Require(html.Contains("<th>Name</th><th>Details</th>", StringComparison.Ordinal) &&
                html.Contains("a,&quot;b.txt", StringComparison.Ordinal) &&
                html.Contains("&lt;&amp;&gt;&quot;", StringComparison.Ordinal) &&
                !html.Contains("<a,", StringComparison.Ordinal),
            "HTML encodes headers and cells instead of interpreting file names as markup");

        var xls = WriteBytes(FileViewExportFormat.Xls, snapshot);
        var xlsPath = Path.Combine(Path.GetTempPath(), $"explorer-dirs-export-smoke-{Guid.NewGuid():N}.xls");
        try
        {
            File.WriteAllBytes(xlsPath, xls);
            using var workbook = ExcelDocument.Load(xlsPath);
            var sheet = workbook["View"];
            Require(sheet.CellAt(1, 1).GetValue().Value?.ToString() == "Name" &&
                    sheet.CellAt(2, 1).GetValue().Value?.ToString() == "a,\"b.txt" &&
                    sheet.CellAt(3, 1).GetValue().Value?.ToString() == "=SUM(1,1)",
                "XLS is a readable BIFF workbook and file names are stored as strings");
        }
        finally
        {
            if (File.Exists(xlsPath)) File.Delete(xlsPath);
        }

        Throws<InvalidDataException>(() => WriteBytes(FileViewExportFormat.Csv,
                new FileViewExportSnapshot(["Name", "Details"], [["only-one-cell"]])),
            "export rejects rows whose cell count differs from visible columns");

        var wideSnapshot = new FileViewExportSnapshot(
            Enumerable.Range(0, 257).Select(index => $"Column {index}").ToArray(), []);
        Throws<InvalidDataException>(() => WriteBytes(FileViewExportFormat.Xls, wideSnapshot),
            "XLS rejects more than 256 columns");
        Require(WriteText(FileViewExportFormat.Csv, wideSnapshot).Split(',').Length == 257,
            "CSV has no XLS-specific column limit");
    }

    private static string WriteText(FileViewExportFormat format, FileViewExportSnapshot snapshot) =>
        Encoding.UTF8.GetString(WriteBytes(format, snapshot)).TrimStart('\uFEFF');

    private static byte[] WriteBytes(FileViewExportFormat format, FileViewExportSnapshot snapshot)
    {
        using var output = new MemoryStream();
        FileViewExportWriter.Write(output, format, snapshot);
        return output.ToArray();
    }

    private static bool HasUtf8Bom(byte[] bytes) => bytes.Length >= 3 &&
        bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("File view export regression: " + description);
    }

    private static void Throws<TException>(Action action, string description) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new Exception("File view export regression: " + description);
    }
}
