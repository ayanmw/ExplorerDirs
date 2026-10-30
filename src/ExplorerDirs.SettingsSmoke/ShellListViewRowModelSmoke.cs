using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellListViewRowModelSmoke
{
    internal static void Run()
    {
        var targetPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs-row-model", "small"));
        var target = new ExplorerDirs.NativeProbe.ShellListViewRow(
            7, 0x101, 0x201, "small", targetPath, targetPath, 0x10, 4,
            FromShellEnumeration: true, SystemImageIndex: 12);
        var siblingPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs-row-model", "sibling"));
        var sibling = target with
        {
            Id = 8,
            AbsolutePidl = 0x102,
            RelativePidl = 0x202,
            DisplayName = "sibling",
            ParsingName = siblingPath,
            FileSystemPath = siblingPath,
            RecursiveBytes = 9,
            SystemImageIndex = 13
        };
        var virtualRow = target with
        {
            Id = 9,
            AbsolutePidl = 0x103,
            RelativePidl = 0x203,
            DisplayName = "virtual folder",
            ParsingName = "::{00000000-0000-0000-0000-000000000001}",
            FileSystemPath = null,
            RecursiveBytes = null,
            SystemImageIndex = 14
        };
        var rows = new[] { target, sibling, virtualRow };
        var scan = new Dictionary<string, RecursiveDirectoryScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [targetPath] = new(20, 3, 1, 0)
        };

        var updated = ExplorerDirs.NativeProbe.ShellListViewRowModel.ApplyRecursiveScanResults(rows, scan);

        Check(updated.Count == rows.Length && updated.Select(row => row.Id).SequenceEqual(rows.Select(row => row.Id)),
            "a scan updates row data without changing native Shell row identity or order");
        Check(updated[0].RecursiveBytes == 20 && updated[0].RecursiveSummary == scan[targetPath] &&
              updated[0].AbsolutePidl == target.AbsolutePidl && updated[0].RelativePidl == target.RelativePidl &&
              updated[0].SystemImageIndex == target.SystemImageIndex,
            "filesystem scan values update while PIDLs and system icon identity remain stable");
        Check(updated[1] == sibling && updated[2] == virtualRow,
            "rows outside the scan and virtual Shell rows retain their previous values");
        Check(target.RecursiveBytes == 4 && target.RecursiveSummary is null,
            "publishing a scan snapshot does not mutate the previous row snapshot");
        Console.WriteLine("Shell ListView row model smoke: PASS");

        static void Check(bool condition, string behavior)
        {
            if (!condition) throw new InvalidOperationException("Shell ListView row model smoke failed: " + behavior);
        }
    }
}
