using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellNamespaceProfileCloneSmoke
{
    internal static void Run()
    {
        var sourceClassId = Guid.Parse("19bc1f15-7afd-4d31-a2d9-97e63d4ca261");
        var cloneClassId = Guid.Parse("8cf9d7d7-2cf6-44b7-9aab-f1bbfd290a3c");
        var sourceIdentity = $"::{{{sourceClassId:D}}}\\edpath:0043003A005C0073";
        var cloneIdentity = $"::{{{cloneClassId:D}}}\\edpath:0043003A005C0073";
        var sortKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);
        var tab = new TabSettings(sourceIdentity,
            new ViewState(4, -1, [new SortColumnState(sortKey, -1)], null, true));
        var state = new SessionState(2, 4, [[tab], [], [], []], 0, [0, 0, 0, 0]);

        var sourceText = SessionStore.Render(state);
        var clonedText = sourceText.Replace(sourceClassId.ToString("D"), cloneClassId.ToString("D"),
            StringComparison.OrdinalIgnoreCase);
        var restored = SessionStore.ParseComplete(clonedText);
        if (restored is null || restored.Panes[0].Count != 1 ||
            !string.Equals(restored.Panes[0][0].Path, cloneIdentity, StringComparison.Ordinal) ||
            restored.Panes[0][0].View.Mode != 4 ||
            restored.Panes[0][0].View.SortColumns.Count != 1 ||
            restored.Panes[0][0].View.SortColumns[0].Direction != -1)
            throw new InvalidOperationException("A cloned provider identity did not parse as a complete SessionStore profile.");
        if (clonedText.Contains(sourceClassId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The cloned SessionStore profile retained the source CLSID.");

        Console.WriteLine("Shell namespace profile clone SessionStore round-trip: PASS");
    }
}
