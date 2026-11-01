using System.IO;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellListViewRowPresentationSmoke
{
    internal static void Run()
    {
        var root = FindRepoRoot();
        var prototype = File.ReadAllText(Path.Combine(root, "src", "ExplorerDirs.NativeProbe",
            "CommonListViewColorBaseline.cs"));
        Check(prototype.Contains("ShellListViewRowPresentation.FormatRecursiveSize", StringComparison.Ordinal),
            "isolated Shell-row size column uses the shared unit and precision formatter");
        Check(prototype.Contains("ShellListViewRowPresentation.FormatRecursiveContents", StringComparison.Ordinal) &&
              prototype.Contains("ShellListViewRowPresentation.FormatRelativeSize", StringComparison.Ordinal),
            "isolated Shell-row content and relative-size columns use the shared presenter");

        var row = new ShellListViewRow(1, 0, 0, "folder", "C:\\folder", "C:\\folder", 0,
            1_572_864, true, 0)
        {
            RecursiveSummary = new RecursiveDirectoryScanResult(1_572_864, 3, 2, 0)
        };
        var compact = new SizeDisplayOptions(SizeDisplayUnit.Auto, 1, UseThousandsSeparator: true);
        var exactBytes = new SizeDisplayOptions(SizeDisplayUnit.Bytes, 0, UseThousandsSeparator: true);
        Check(ShellListViewRowPresentation.FormatRecursiveSize(row, compact) == "1.5 MB" &&
              ShellListViewRowPresentation.FormatRecursiveSize(row, exactBytes) == "1,572,864 B",
            "recursive size rows honor the selected unit, precision, and thousands separator");
        var largeRecursiveBytes = (5UL << 30) + 10;
        Check(ShellListViewRowPresentation.FormatRecursiveSize(
                  row with { RecursiveBytes = largeRecursiveBytes },
                  new SizeDisplayOptions(SizeDisplayUnit.Bytes, 0, UseThousandsSeparator: false)) ==
              "5368709130 B",
            "recursive size presentation preserves values above 4 GiB without 32-bit truncation");
        Check(ShellListViewRowPresentation.FormatRecursiveSize(row with { RecursiveBytes = null }, compact) == string.Empty,
            "unscanned recursive size rows stay blank instead of displaying a fabricated zero");
        Check(ShellListViewRowPresentation.FormatRecursiveContents(row) == "files: 3, folders: 2" &&
              ShellListViewRowPresentation.FormatRecursiveContents(row with { RecursiveSummary = null }) == string.Empty,
            "recursive contents show scanned file/folder counts and leave unscanned rows blank");
        Check(ShellListViewRowPresentation.FormatRelativeSize(row, 3_145_728) == "50%" &&
              ShellListViewRowPresentation.FormatRelativeSize(row, 0) == string.Empty &&
              ShellListViewRowPresentation.FormatRelativeSize(row with { RecursiveBytes = null }, 3_145_728) == string.Empty,
            "relative size uses the directory total and leaves unavailable percentages blank");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Explorer Dirs repository root.");
    }

    private static void Check(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Shell ListView row presentation smoke failed: " + behavior);
        Console.WriteLine("PASS " + behavior);
    }
}
