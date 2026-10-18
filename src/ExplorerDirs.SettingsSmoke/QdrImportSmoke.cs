using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerDirs.App;

internal static class QdrImportSmoke
{
    public static void Run(string root)
    {
        var plain = "Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\current\nDir1_0=C:\\first\nDir1_2=C:\\third\n" +
            "ViewMode1=3\nCurPos1_2=0\nCurPos1_0=2\nDir2=D:\\名字 & 目录\nColsAndSort1=unknown";
        var imported = QdrImporter.Parse(plain, (value, _) => value);
        Require(imported.TabCount == 4 && imported.Layout == 4 && imported.Panes[0][0].Path == @"C:\third" &&
            imported.Panes[0][1].Path == @"C:\current" && imported.SelectedTabs[0] == 1 &&
            imported.Panes[0][1].View.Mode == 3 && imported.Panes[1][0].Path == @"D:\名字 & 目录",
            "QDR preserves pane mapping, active tab gap, reordered tabs, Unicode and ampersand paths");
        Require(imported.Warnings.Any(warning => warning.Contains("排序")), "private view blobs are reported as unsupported");
        var invalidPath = QdrImporter.Parse(plain, (value, _) => value == @"C:\first" ? null : value);
        Require(invalidPath.Panes[0][invalidPath.SelectedTabs[0]].Path == @"C:\current", "skipping an invalid tab does not move the original active slot");
        Require(QdrImporter.Parse(plain.Replace("ViewMode=0", "ViewMode=99"), (v, _) => v).Layout is null,
            "unknown QDR layouts do not silently become another layout");
        foreach (var bad in new[] { "%00%00", "%FF%FF%00%00", "%02%00", "%03%00%00%00", "%GG%00%00", "%02%00%00%00%00" })
        {
            try { QdrImporter.DecodePidl(bad); throw new Exception("invalid PIDL accepted"); }
            catch (FormatException) { }
        }
        try { QdrImporter.Parse("Dir1=C:\\", (v, _) => v); throw new Exception("foreign file accepted"); }
        catch (InvalidDataException) { }
        var qdrPath = Path.Combine(root, "import.qdr");
        File.WriteAllText(qdrPath, plain, Encoding.Unicode);
        Require(QdrImporter.Load(qdrPath).TabCount == 4, "UTF16 QDR import");
        File.WriteAllText(qdrPath, plain, new UTF8Encoding(true));
        Require(QdrImporter.Load(qdrPath).TabCount == 4, "UTF8 BOM QDR import");
        var pidl = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(root, IntPtr.Zero, out pidl, 0, out _));
            var bytes = new byte[ILGetSize(pidl)];
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            var encoded = string.Concat(bytes.Select(value => "%" + value.ToString("X2")));
            var pidlQdr = $"Q-Dir Version=12.79\nViewMode=0\nFolder1={encoded}&ViewMode1=4&UNICODE=1";
            var resolved = QdrImporter.Parse(pidlQdr, QdrImporter.ResolveLocation);
            Require(resolved.TabCount == 1 && resolved.Panes[0][0].Path.Equals(root, StringComparison.OrdinalIgnoreCase),
                "native Shell PIDL resolves back to its filesystem directory");
        }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }

    private static void Require(bool valid, string message)
    { if (!valid) throw new Exception("Failed: " + message); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);
}
