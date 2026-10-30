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

        var row = new ShellListViewRow(1, 0, 0, "folder", "C:\\folder", "C:\\folder", 0,
            1_572_864, true, 0);
        var compact = new SizeDisplayOptions(SizeDisplayUnit.Auto, 1, UseThousandsSeparator: true);
        var exactBytes = new SizeDisplayOptions(SizeDisplayUnit.Bytes, 0, UseThousandsSeparator: true);
        Check(ShellListViewRowPresentation.FormatRecursiveSize(row, compact) == "1.5 MB" &&
              ShellListViewRowPresentation.FormatRecursiveSize(row, exactBytes) == "1,572,864 B",
            "recursive size rows honor the selected unit, precision, and thousands separator");
        Check(ShellListViewRowPresentation.FormatRecursiveSize(row with { RecursiveBytes = null }, compact) == string.Empty,
            "unscanned recursive size rows stay blank instead of displaying a fabricated zero");
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
