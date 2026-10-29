using ExplorerDirs.App;
using System.IO;

internal static class QuickFilterSessionSmoke
{
    internal static void Run(string root)
    {
        var typeKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 4);
        var filter = new QuickFilterSettings("*.txt;*.md", true, true,
            [new ColumnValueFilter(typeKey, ["TXT 文件", "BIN 文件"])]);
        var state = new SessionState(4, 4,
            [[new TabSettings(@"C:\filter-fixture", ViewState.Default(), filter)], [], [], []],
            QdrColorMetadata: new QdrColorMetadata("1", "0", "dark", ["0=*.jpg=1=FF0000=-1=1=-1=0"]));
        var rendered = SessionStore.Render(state);
        Require(rendered.Contains("FormatVersion=13", StringComparison.Ordinal), "session format advances to v13 for opaque Q-Dir color metadata");
        Require(rendered.Contains("FilterPattern=*.txt;*.md", StringComparison.Ordinal) &&
            rendered.Contains("FilterExcludeMatches=true", StringComparison.Ordinal) &&
            rendered.Contains("FilterFolders=true", StringComparison.Ordinal), "filter settings serialize per tab");
        var restored = SessionStore.ParseComplete(rendered)?.Panes[0][0].QuickFilter;
        Require(restored is { Pattern: "*.txt;*.md", ExcludeMatches: true, FilterFolders: true },
            "filter settings round-trip");
        Require(restored?.ColumnFilters.Count == 1 && restored.ColumnFilters[0].Key == typeKey &&
            restored.ColumnFilters[0].Values.SequenceEqual(["TXT 文件", "BIN 文件"]),
            "column-value criteria round-trip per tab");
        var restoredColors = SessionStore.ParseComplete(rendered)?.QdrColorMetadata;
        Require(restoredColors is { } colors && state.QdrColorMetadata is { } expectedColors &&
            colors.UseColor == expectedColors.UseColor && colors.UseTreeColor == expectedColors.UseTreeColor &&
            colors.ColorTheme == expectedColors.ColorTheme && colors.Filter2Rows.SequenceEqual(expectedColors.Filter2Rows),
            "opaque Q-Dir color switches and raw Filter2 rows round-trip in the portable session");

        var colorMetadataLine = rendered.Split(Environment.NewLine)
            .Single(line => line.StartsWith("QdrColorMetadata=", StringComparison.Ordinal));
        var legacy = rendered.Replace("FormatVersion=13", "FormatVersion=5", StringComparison.Ordinal)
            .Replace(colorMetadataLine + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("FilterPattern=*.txt;*.md" + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("FilterExcludeMatches=true" + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("FilterFolders=true" + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("PreviewPaneVisible=false" + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("DetailsPaneVisible=false" + Environment.NewLine, "", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(legacy)?.Panes[0][0].QuickFilter == QuickFilterSettings.Empty,
            "v5 profile defaults to no quick filter");
        foreach (var version in new[] { 1, 2, 3, 4, 5 })
        {
            var oldProfile = legacy.Replace("FormatVersion=5", $"FormatVersion={version}", StringComparison.Ordinal);
            Require(SessionStore.ParseComplete(oldProfile)?.Panes[0][0].QuickFilter == QuickFilterSettings.Empty,
                $"v{version} profile defaults to no quick filter");
        }
        var malformed = rendered.Replace("FilterExcludeMatches=true", "FilterExcludeMatches=maybe", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(malformed) is null, "malformed filter booleans reject incomplete profile snapshots");
        var malformedColumnValues = rendered.Replace("ColumnFilter0Values=", "ColumnFilter0Values=bad!", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(malformedColumnValues) is null,
            "malformed column-value payload rejects an incomplete profile snapshot");
        var malformedColorPayload = rendered.Replace("QdrColorMetadata=", "QdrColorMetadata=bad!", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(malformedColorPayload) is null,
            "malformed opaque Q-Dir color payload rejects an incomplete profile snapshot");
        var v12 = rendered.Replace("FormatVersion=13", "FormatVersion=12", StringComparison.Ordinal)
            .Replace(colorMetadataLine + Environment.NewLine, "", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(v12)?.QdrColorMetadata is null,
            "v12 sessions upgrade with no fabricated Q-Dir color metadata");
        var v10 = rendered.Replace("FormatVersion=13", "FormatVersion=10", StringComparison.Ordinal)
            .Replace("ColumnFilterCount=1" + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("ColumnFilter0Key=" + typeKey + Environment.NewLine, "", StringComparison.Ordinal)
            .Replace("ColumnFilter0Values=" + Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] { "TXT 文件", "BIN 文件" })) + Environment.NewLine, "", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(v10)?.Panes[0][0].QuickFilter is { HasPattern: true, HasColumnFilters: false },
            "v10 profile keeps the name filter and defaults column-value filters to empty");

        var durablePath = Path.Combine(root, "quick-filter-restart.ini");
        new SessionStore(durablePath, Path.Combine(root, "missing-quick-filter-restart.json")).Save(state);
        var reopened = new SessionStore(durablePath, Path.Combine(root, "missing-quick-filter-restart.json"))
            .Load()?.Panes[0][0].QuickFilter;
        Require(reopened is { Pattern: "*.txt;*.md", ExcludeMatches: true, FilterFolders: true } &&
            reopened.ColumnFilters.Count == 1 && reopened.ColumnFilters[0].Key == typeKey &&
            reopened.ColumnFilters[0].Values.SequenceEqual(["TXT 文件", "BIN 文件"]),
            "column-value filters survive a disk write and reload through a fresh session store");
        var reopenedColors = new SessionStore(durablePath, Path.Combine(root, "missing-quick-filter-restart.json"))
            .Load()?.QdrColorMetadata;
        Require(reopenedColors is { UseColor: "1", UseTreeColor: "0", ColorTheme: "dark" } &&
            reopenedColors.Filter2Rows.SequenceEqual(["0=*.jpg=1=FF0000=-1=1=-1=0"]),
            "opaque Q-Dir color metadata survives a disk write and reload through a fresh session store");

        var migrationPath = Path.Combine(root, "quick-filter-v5.ini");
        File.WriteAllText(migrationPath, legacy);
        var store = new SessionStore(migrationPath, Path.Combine(root, "missing-filter-session.json"));
        var loaded = store.Load() ?? throw new Exception("v5 profile failed to load");
        store.Save(loaded);
        var backups = Directory.GetFiles(root, "quick-filter-v5.ini.before-v13-*.bak");
        Require(backups.Length == 1 && File.ReadAllText(backups[0]) == legacy &&
            File.ReadAllText(migrationPath).Contains("FormatVersion=13", StringComparison.Ordinal),
            "v5 upgrade keeps the original snapshot before writing v13");
        Console.WriteLine("Quick filter session smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Quick filter session: " + message);
    }
}


