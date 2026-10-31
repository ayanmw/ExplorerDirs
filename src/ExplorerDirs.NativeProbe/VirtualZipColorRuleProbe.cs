using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Check that Shell row names and SFGAO flags can feed Q-Dir color rules without filesystem paths.
internal static class VirtualZipColorRuleProbe
{
    private const uint SfgaoFolder = 0x20000000;
    private const uint SfgaoReadOnly = 0x00040000;
    private const uint SfgaoHidden = 0x00080000;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixtureDirectory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(fixtureDirectory, "virtual-items.zip");
        nint rootPidl = 0, shellFolder = 0;
        IReadOnlyList<ShellListViewRow> rows = [];
        try
        {
            Directory.CreateDirectory(fixtureDirectory);
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var file = archive.CreateEntry("blue-rule-target.txt").Open())
                    file.WriteByte(0x41);
                using (var nestedFile = archive.CreateEntry("virtual-folder/nested.txt").Open())
                    nestedFile.WriteByte(0x42);
            }

            Marshal.ThrowExceptionForHR(SHParseDisplayName(zipPath, 0, out rootPidl, SfgaoFolder, out var rootAttributes));
            if ((rootAttributes & SfgaoFolder) == 0)
                throw new InvalidOperationException("The ZIP fixture did not resolve as a Shell folder.");
            Marshal.ThrowExceptionForHR(SHBindToObject(0, rootPidl, 0, in ShellFolderId, out shellFolder));

            var enumeration = ShellListViewEnumerator.Enumerate(rootPidl, shellFolder);
            rows = enumeration.Rows;
            if (rows.Count != 2)
                throw new InvalidOperationException($"The ZIP Shell folder should expose one file and one directory, got {rows.Count} rows.");

            var fileRule = ShellViewColorRule.BackgroundOnly("blue-rule-*.txt", ShellColorRuleDecision.Set, 0x00F0E0D0u)
                with { TextColor = ShellColorRuleProperty<uint>.Set(0x000000C0u), Bold = ShellColorRuleProperty<bool>.Set(true) };
            var directoryRule = ShellViewColorRule.BackgroundOnly("#DIR", ShellColorRuleDecision.Set, 0x00D0F0E0u)
                with { TextColor = ShellColorRuleProperty<uint>.Set(0x0000C000u) };
            var rules = new[] { fileRule, directoryRule };
            var fileRow = rows.Single(row => row.DisplayName.Equals("blue-rule-target.txt", StringComparison.OrdinalIgnoreCase));
            var directoryRow = rows.Single(row => (row.Attributes & SfgaoFolder) != 0);
            if (rows.Any(row => row.FileSystemPath is not null))
                throw new InvalidOperationException("ZIP child rows unexpectedly exposed filesystem paths.");

            var fileColor = Evaluate(fileRow, rules);
            var directoryColor = Evaluate(directoryRow, rules);
            var fileText = ResolveText(fileRow, rules);
            var directoryText = ResolveText(directoryRow, rules);
            var fileBold = ResolveBold(fileRow, rules);
            if (fileColor != 0x00F0E0D0u || directoryColor != 0x00D0F0E0u ||
                fileText != 0x000000C0u || directoryText != 0x0000C000u || fileBold != true)
                throw new InvalidOperationException($"Virtual ZIP rows did not resolve expected rules: file-bg={Format(fileColor)}; file-text={Format(fileText)}; file-bold={fileBold}; directory-bg={Format(directoryColor)}; directory-text={Format(directoryText)}.");
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The virtual ZIP color rule probe changed the foreground window.");

            Console.WriteLine($"virtual-zip-color-rules: rows={rows.Count}; file={fileRow.DisplayName}/pathless/color={Format(fileColor)}; " +
                $"file-text={Format(fileText)}/bold={fileBold}; directory={directoryRow.DisplayName}/sfgao-folder={((directoryRow.Attributes & SfgaoFolder) != 0)}/color={Format(directoryColor)}/text={Format(directoryText)}; " +
                $"all-pathless={rows.All(row => row.FileSystemPath is null)}; foreground-unchanged=True");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"virtual-zip-color-rules: {exception.GetType().Name};hr=0x{exception.HResult:X8};detail={exception.Message}");
            return 1;
        }
        finally
        {
            ShellListViewEnumerator.FreeRows(rows);
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    private static uint? Evaluate(ShellListViewRow row, IReadOnlyList<ShellViewColorRule> rules) =>
        ShellViewColorRuleEvaluator.ResolveBackground(rules, row.DisplayName,
            (row.Attributes & SfgaoFolder) != 0,
            (row.Attributes & SfgaoHidden) != 0,
            (row.Attributes & SfgaoReadOnly) != 0);

    private static uint? ResolveText(ShellListViewRow row, IReadOnlyList<ShellViewColorRule> rules) =>
        ShellViewColorRuleEvaluator.ResolveTextColor(rules, row.DisplayName,
            (row.Attributes & SfgaoFolder) != 0,
            (row.Attributes & SfgaoHidden) != 0,
            (row.Attributes & SfgaoReadOnly) != 0);

    private static bool? ResolveBold(ShellListViewRow row, IReadOnlyList<ShellViewColorRule> rules) =>
        ShellViewColorRuleEvaluator.ResolveFontStyle(rules, row.DisplayName,
            (row.Attributes & SfgaoFolder) != 0,
            (row.Attributes & SfgaoHidden) != 0,
            (row.Attributes & SfgaoReadOnly) != 0,
            static rule => rule.Bold);

    private static string Format(uint? color) => color is null ? "unset" : $"0x{color.Value:X6}";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parentFolder, nint relativeOrAbsolutePidl, nint bindContext,
        in Guid interfaceId, out nint instance);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
