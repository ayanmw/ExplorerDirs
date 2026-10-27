using System.IO;
using System.Text;
using ExplorerDirs.App;
using OfficeIMO.Excel;

internal static class FileViewExportSmoke
{
    internal static void Run()
    {
        var repoRoot = FindRepoRoot();
        var mainWindowCode = File.ReadAllText(Path.Combine(repoRoot,
            "src", "ExplorerDirs.App", "MainWindow.xaml.cs"));
        Require(mainWindowCode.Contains("FileViewExportFile.WriteAtomically", StringComparison.Ordinal),
            "the export command writes a complete temporary file before replacing its destination");

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

        var root = Path.Combine(Path.GetTempPath(), $"explorer-dirs-atomic-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "current-view.csv");
            File.WriteAllText(destination, "previous complete export");
            Throws<InvalidOperationException>(() => FileViewExportFile.WriteAtomically(destination, output =>
            {
                output.Write("partial"u8);
                throw new InvalidOperationException("simulated writer failure");
            }), "a failed export propagates its writer failure");
            Require(File.ReadAllText(destination) == "previous complete export" &&
                    Directory.GetFiles(root).Length == 1,
                "a failed export preserves the previous destination and removes its temporary file");

            FileViewExportFile.WriteAtomically(destination, output => output.Write("complete"u8));
            Require(File.ReadAllText(destination) == "complete" && Directory.GetFiles(root).Length == 1,
                "a successful export replaces the destination only after the new file is complete");
            CheckClosedWindowWriteAsync(destination).GetAwaiter().GetResult();
        }
        finally { Directory.Delete(root, recursive: true); }

    }

    private static async Task CheckClosedWindowWriteAsync(string destination)
    {
        using var closed = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = WindowBackgroundOperation.RunAsync((destination, started, proceed, finished), static state =>
        {
            try
            {
                FileViewExportFile.WriteAtomically(state.destination, output =>
                {
                    output.Write("confirmed "u8);
                    state.started.SetResult(true);
                    state.proceed.Task.GetAwaiter().GetResult();
                    output.Write("export"u8);
                });
                state.finished.TrySetResult(true);
                return true;
            }
            catch (Exception ex) { state.finished.TrySetException(ex); throw; }
        }, closed.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closed.Cancel();
            try { await waiting.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
            Require(waiting.IsCanceled && !finished.Task.IsCompleted,
                "closing ends the UI wait without cancelling a confirmed export writer");
            Require(File.ReadAllText(destination) == "complete",
                "a partially written export never replaces the previous destination");
            proceed.TrySetResult(true);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(File.ReadAllText(destination) == "confirmed export",
                "the confirmed atomic write completes after its UI wait was cancelled");
        }
        finally
        {
            proceed.TrySetResult(true);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
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

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "ExplorerDirs.App", "MainWindow.xaml.cs")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

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
