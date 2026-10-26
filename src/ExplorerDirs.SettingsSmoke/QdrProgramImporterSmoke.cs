using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class QdrProgramImporterSmoke
{
    public static void Run()
    {
        var assembly = typeof(UiPreferencesStore).Assembly;
        var importerType = assembly.GetType("ExplorerDirs.App.QdrProgramImporter", throwOnError: false)
            ?? throw new Exception("QdrProgramImporter must classify Q-Dir [Program] rows without executing them.");
        var parse = importerType.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("QdrProgramImporter.Parse is required.");
        var source = string.Join("\r\n", [
            "[Other]",
            "Ignored=C:\\outside.exe=%curdir%",
            "[Program]",
            "; comment",
            "",
            "Compare Tool=\"C:\\Program Files\\Compare.exe\"=%sel_files%",
            "Equals Path=\"C:\\Tools\\Build=Compare.exe\"=%curdir%",
            "CSV 编辑=..\\Tools\\Csv.exe=\"%curdir%\"",
            "Cmd=%sysdir%/cmd.exe",
            "Filter=..\\Tools\\Search.exe=\"--include=*.log\" \"--mode=a=b\"",
            "Folder Backup=..\\Tools\\Backup.exe=%sel_files%=%curdir%",
            "portable_install=1",
            "missing-fields",
            "ambiguous=C:\\Tools\\app.exe=%curdir%=C:\\work=extra",
            "=C:\\Tools\\empty-name.exe=%curdir%",
            "bad-quote=\"C:\\Tools\\bad.exe=%curdir%",
            "Relative Work=C:\\Tools\\app.exe=--preview=..\\work",
            "unquoted-equals=C:\\Tools\\app.exe=--option=value"
        ]);
        var preview = parse.Invoke(null, [source])!;
        var entries = (IEnumerable)preview.GetType().GetProperty("Entries")!.GetValue(preview)!;
        var accepted = entries.Cast<object>().ToArray();
        var skipped = ((IEnumerable)preview.GetType().GetProperty("SkippedRows")!.GetValue(preview)!).Cast<object>().ToArray();
        Check(accepted.Length == 7, "documented two-, three-, and four-field program rows are imported");
        Check(Property(accepted[0], "Name") == "Compare Tool" &&
              Property(accepted[0], "ExecutablePath") == @"C:\Program Files\Compare.exe" &&
              Property(accepted[0], "Arguments") == "%sel_files%" && Property(accepted[0], "WorkingDirectory") == "",
            "quoted executable paths with spaces and optional working directories are preserved");
        Check(Property(accepted[1], "Name") == "Equals Path" &&
              Property(accepted[1], "ExecutablePath") == @"C:\Tools\Build=Compare.exe" &&
              Property(accepted[1], "Arguments") == "%curdir%",
            "equals signs inside quoted executable paths do not split Q-Dir fields");
        Check(Property(accepted[2], "Name") == "CSV 编辑" && Property(accepted[2], "Arguments") == "\"%curdir%\"",
            "Unicode command names and quoted arguments are preserved");
        Check(Property(accepted[3], "Name") == "Cmd" && Property(accepted[3], "ExecutablePath") == "%sysdir%/cmd.exe" &&
              Property(accepted[3], "Arguments") == "" && Property(accepted[3], "WorkingDirectory") == "",
            "name and executable rows without arguments are imported with empty optional fields");
        Check(Property(accepted[4], "Arguments") == "\"--include=*.log\" \"--mode=a=b\"" &&
              Property(accepted[4], "WorkingDirectory") == "",
            "quoted argument values containing equals signs remain intact");
        Check(Property(accepted[5], "WorkingDirectory") == "%curdir%" &&
              Property(accepted[6], "Arguments") == "--preview" &&
              Property(accepted[6], "WorkingDirectory") == @"..\work" &&
              accepted.Select(item => item.GetType().GetProperty("Id")!.GetValue(item)).Distinct().Count() == 7,
            "four-field rows and unique IDs are produced");
        Check(skipped.Length == 5 && skipped.Select(item => (int)item.GetType().GetProperty("LineNumber")!.GetValue(item)!)
                .SequenceEqual([13, 14, 15, 16, 18]) &&
              skipped.All(item => !string.IsNullOrWhiteSpace(Property(item, "Reason"))),
            "malformed and ambiguous rows, empty names, and unmatched quotes are explained while metadata is ignored");
        Check(Property(skipped[1], "Reason").Contains("无法唯一识别", StringComparison.Ordinal),
            "rows with multiple unquoted argument/work-directory delimiters are explicitly skipped");
        Check(Property(skipped[4], "Reason").Contains("无法唯一识别", StringComparison.Ordinal),
            "a single unquoted equals after the argument boundary is not silently reinterpreted as a working directory");
        Check(source.Contains("[Program]", StringComparison.Ordinal) && accepted.All(item =>
                  Property(item, "ExecutablePath").Length > 0),
            "import preview is pure and returns command data without applying or launching it");

        var acceptanceFixture = string.Join("\r\n", [
            "[Program]",
            "Preview Tool=\"C:\\Program Files\\Explorer Dirs Test\\viewer.exe\"=--preview=\"C:\\Program Files\\Explorer Dirs Test\"",
            "No Args Tool=\"C:\\Program Files\\Explorer Dirs Test\\no-args.exe\""
        ]);
        var acceptancePreview = parse.Invoke(null, [acceptanceFixture])!;
        var acceptanceEntries = ((IEnumerable)acceptancePreview.GetType().GetProperty("Entries")!.GetValue(acceptancePreview)!)
            .Cast<object>().ToArray();
        Check(acceptanceEntries.Length == 2 &&
              Property(acceptanceEntries[0], "Name") == "Preview Tool" &&
              Property(acceptanceEntries[0], "ExecutablePath") == @"C:\Program Files\Explorer Dirs Test\viewer.exe" &&
              Property(acceptanceEntries[0], "Arguments") == "--preview" &&
              Property(acceptanceEntries[0], "WorkingDirectory") == @"C:\Program Files\Explorer Dirs Test" &&
              Property(acceptanceEntries[1], "Name") == "No Args Tool" &&
              Property(acceptanceEntries[1], "ExecutablePath") == @"C:\Program Files\Explorer Dirs Test\no-args.exe" &&
              Property(acceptanceEntries[1], "Arguments").Length == 0 &&
              Property(acceptanceEntries[1], "WorkingDirectory").Length == 0,
            "the isolated desktop-acceptance fixture imports parameterized and no-argument rows exactly");

        var formatterType = assembly.GetType("ExplorerDirs.App.ProgramStarterEntryFormatter", throwOnError: false)
            ?? throw new Exception("ProgramStarterEntryFormatter must expose all imported command fields for review.");
        var formatEntry = formatterType.GetMethod("FormatForReview", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("ProgramStarterEntryFormatter.FormatForReview is required for import review.");
        var previewLine = (string)formatEntry.Invoke(null, [accepted[5]])!;
        Check(previewLine.Contains("Folder Backup", StringComparison.Ordinal) &&
              previewLine.Contains(@"..\Tools\Backup.exe", StringComparison.Ordinal) &&
              previewLine.Contains("%sel_files%", StringComparison.Ordinal) &&
              previewLine.Contains("%curdir%", StringComparison.Ordinal),
            "Q-Dir import preview shows the working-directory field alongside name, path, and arguments");

        var readConfiguration = importerType.GetMethod("ReadConfiguration", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("QdrProgramImporter.ReadConfiguration must preserve legacy Windows INI text.");
        var encodingRoot = Path.Combine(Path.GetTempPath(), "explorer-dirs-qdr-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(encodingRoot);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var legacyPath = Path.Combine(encodingRoot, "Q-Dir.ini");
            var legacyEncoding = Encoding.GetEncoding(GetActiveCodePage(), EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            File.WriteAllBytes(legacyPath, legacyEncoding.GetBytes("[Program]\r\n中文工具=C:\\工具\\viewer.exe=%curdir%\r\n"));
            var decoded = (string)readConfiguration.Invoke(null, [legacyPath])!;
            var legacyPreview = parse.Invoke(null, [decoded])!;
            var legacyEntries = ((IEnumerable)legacyPreview.GetType().GetProperty("Entries")!.GetValue(legacyPreview)!)
                .Cast<object>().ToArray();
            Check(legacyEntries.Length == 1 && Property(legacyEntries[0], "Name") == "中文工具" &&
                  Property(legacyEntries[0], "ExecutablePath") == @"C:\工具\viewer.exe",
                "legacy Windows-ACP Q-Dir INI preserves Unicode names and executable paths");

            var utf8Path = Path.Combine(encodingRoot, "utf8.ini");
            File.WriteAllText(utf8Path, "[Program]\r\n中文工具=C:\\工具\\viewer.exe=%curdir%\r\n", new UTF8Encoding(true));
            Check(((string)readConfiguration.Invoke(null, [utf8Path])!).Contains("中文工具", StringComparison.Ordinal),
                "UTF-8 BOM Q-Dir INI remains readable");

            var utf16Path = Path.Combine(encodingRoot, "utf16.ini");
            File.WriteAllText(utf16Path, "[Program]\r\n中文工具=C:\\工具\\viewer.exe=%curdir%\r\n", Encoding.Unicode);
            Check(((string)readConfiguration.Invoke(null, [utf16Path])!).Contains("中文工具", StringComparison.Ordinal),
                "UTF-16 BOM Q-Dir INI remains readable");
        }
        finally
        {
            Directory.Delete(encodingRoot, recursive: true);
        }
        Console.WriteLine("Q-Dir Program importer smoke: PASS");

        static string Property(object value, string name) =>
            (string?)value.GetType().GetProperty(name)!.GetValue(value) ?? "";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Q-Dir Program importer: " + message);
        Console.WriteLine("PASS " + message);
    }

    private static int GetActiveCodePage()
    {
        return ActiveCodePage();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetACP")]
        static extern int ActiveCodePage();
    }
}
