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

        var typeKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 4);
        var sizeKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
        var columns = new QuickFilterSettings(string.Empty, false, false,
            [new ColumnValueFilter(typeKey, ["TXT 文件", "BIN 文件"]),
             new ColumnValueFilter(sizeKey, ["1 KB"])]);
        Check(!columns.HasPattern && columns.HasColumnFilters && columns.HasAnyFilter,
            "column-only criteria activate the filter without a name pattern");
        Check(columns.MatchesColumnFilters(key => key == typeKey ? "txt 文件" : "1 KB"),
            "values within a column are case-insensitive alternatives and columns combine with AND");
        Check(!columns.MatchesColumnFilters(key => key == typeKey ? "TXT 文件" : "2 KB"),
            "a nonmatching value in any selected column rejects the item");
        Check(columns.MatchesColumnFilters(_ => null),
            "unavailable Shell properties fail open instead of hiding the item");

        var manyValues = Enumerable.Range(0, QuickFilterSettings.MaximumColumnFilterValues)
            .Select(index => $"VALUE-{index}").ToArray();
        var manyValueFilter = new ColumnValueFilter(typeKey, manyValues);
        Check(manyValueFilter.Contains("value-255"),
            "large column value lists provide case-insensitive membership lookup");
        Check(!manyValueFilter.Contains("value-256"),
            "large column value lists reject values that were not selected");

        var mutableValues = Enumerable.Range(0, 8).Select(index => $"ORIGINAL-{index}").ToList();
        var immutableFilter = new ColumnValueFilter(typeKey, mutableValues);
        mutableValues[0] = "REPLACED";
        Check(immutableFilter.Values[0] == "ORIGINAL-0" && immutableFilter.Contains("ORIGINAL-0") &&
              !immutableFilter.Contains("REPLACED"),
            "column filter values and lookup keep a stable snapshot when their input list changes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Quick filter failed: {message}");
    }
}
