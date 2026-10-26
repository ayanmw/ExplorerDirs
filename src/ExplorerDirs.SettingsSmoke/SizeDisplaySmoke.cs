using System.Globalization;
using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class SizeDisplaySmoke
{
    public static void Run(string root)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check(ByteSizeFormatter.Format(1536, SizeDisplayOptions.Default) == "1.5 KB",
                "automatic units retain the existing two-decimal size display");
            Check(ByteSizeFormatter.Format(1536, new(SizeDisplayUnit.Bytes, 0, true)) == "1,536 B",
                "fixed bytes support integer display with thousands separation");
            Check(ByteSizeFormatter.Format(1_500_000, new(SizeDisplayUnit.Kilobytes, 1, true)) == "1,464.8 KB",
                "fixed units combine configured precision and thousands separation");
            var scan = new RecursiveDirectoryScanResult(1536, 12, 4, 1);
            Check(ByteSizeFormatter.FormatRecursiveDirectorySummary(scan, SizeDisplayOptions.Default)
                    .Contains("12 个文件 · 4 个子目录", StringComparison.Ordinal),
                "F9 directory summaries show item counts by default");
            var noCounts = new SizeDisplayOptions(SizeDisplayUnit.Auto, 2, false, false);
            Check(!ByteSizeFormatter.FormatRecursiveDirectorySummary(scan, noCounts).Contains("个文件", StringComparison.Ordinal) &&
                  ByteSizeFormatter.FormatRecursiveDirectorySummary(scan, noCounts).Contains("无法读取/已跳过 1 处", StringComparison.Ordinal),
                "F9 can hide counts while retaining the incomplete-scan warning");

            var path = Path.Combine(root, "SizeDisplay.ini");
            var store = new SizeDisplayPreferenceStore(path);
            Check(store.Load() == SizeDisplayOptions.Default && store.LoadWarning is null,
                "missing size display preference keeps the current formatting default");
            var configured = new SizeDisplayOptions(SizeDisplayUnit.Gigabytes, 3, true, false);
            store.Save(configured);
            Check(new SizeDisplayPreferenceStore(path).Load() == configured,
                "size display options round-trip through the portable INI");

            var externalStore = new SizeDisplayPreferenceStore(path);
            externalStore.Load();
            File.AppendAllText(path, "; external edit" + Environment.NewLine);
            var externalConflict = false;
            try { externalStore.Save(SizeDisplayOptions.Default); }
            catch (IOException) { externalConflict = true; }
            Check(externalConflict && File.ReadAllText(path).Contains("external edit", StringComparison.Ordinal),
                "external size display edits are preserved");

            var futureContent = File.ReadAllText(path).Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal);
            File.WriteAllText(path, futureContent);
            var futureStore = new SizeDisplayPreferenceStore(path);
            Check(futureStore.Load() == SizeDisplayOptions.Default && futureStore.LoadWarning is not null,
                "future size display formats load defaults with a warning");
            var futureProtected = false;
            try { futureStore.Save(configured); }
            catch (IOException) { futureProtected = true; }
            Check(futureProtected && File.ReadAllText(path).Contains("FormatVersion=2", StringComparison.Ordinal),
                "future size display formats are not overwritten");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        Console.WriteLine("Size display smoke: PASS");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
