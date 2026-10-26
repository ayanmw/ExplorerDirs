using ExplorerDirs.App;

internal static class ShellQuickFilterSmoke
{
    internal static void Run()
    {
        var include = new QuickFilterSettings(" *.txt ; ;report?.csv ", false, false);
        Check(include.Matches("NOTES.TXT"), "wildcard matching is case-insensitive");
        Check(include.Matches("report1.csv"), "semicolon alternatives and question wildcard match");
        Check(!include.Matches("report12.csv"), "question wildcard matches one character");
        Check(include.ShouldShow("folder.bin", isFolder: true), "folders pass through when folder filtering is disabled");
        Check(include.ShouldShow("folder.txt", isFolder: true), "matching folder is visible when folder filtering is disabled");
        Check(include.ShouldShow("notes.txt", isFolder: false), "include mode keeps a matching file");
        Check(!include.ShouldShow("notes.bin", isFolder: false), "include mode hides a nonmatching file");
        var multiline = new QuickFilterSettings("*.txt\r\n*.md", false, false);
        Check(multiline.Pattern == "*.txt;*.md" && multiline.Matches("readme.md"),
            "pasted multiline patterns are normalized to safe alternatives");

        var exclude = new QuickFilterSettings("*.tmp;*.bak", true, true);
        Check(!exclude.ShouldShow("draft.tmp", isFolder: false), "exclude mode hides matching files");
        Check(exclude.ShouldShow("draft.txt", isFolder: false), "exclude mode keeps nonmatching files");
        Check(!exclude.ShouldShow("old.bak", isFolder: true), "folder filtering applies the selected rule to folders");

        Check(QuickFilterSettings.Empty.ShouldShow("anything.bin", isFolder: false), "empty filter keeps all files");
        Check(!QuickFilterSettings.Empty.Matches("anything.bin"), "empty filter has no matching patterns");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Quick filter failed: {message}");
    }
}
