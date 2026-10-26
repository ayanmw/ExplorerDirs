using ExplorerDirs.App;
using System.IO;

internal static class QuickFilterSessionSmoke
{
    internal static void Run(string root)
    {
        var filter = new QuickFilterSettings("*.txt;*.md", true, true);
        var state = new SessionState(4, 4,
            [[new TabSettings(@"C:\filter-fixture", ViewState.Default(), filter)], [], [], []]);
        var rendered = SessionStore.Render(state);
        Require(rendered.Contains("FormatVersion=10", StringComparison.Ordinal), "session format advances to v10 for tree display modes");
        Require(rendered.Contains("FilterPattern=*.txt;*.md", StringComparison.Ordinal) &&
            rendered.Contains("FilterExcludeMatches=true", StringComparison.Ordinal) &&
            rendered.Contains("FilterFolders=true", StringComparison.Ordinal), "filter settings serialize per tab");
        var restored = SessionStore.ParseComplete(rendered)?.Panes[0][0].QuickFilter;
        Require(restored is { Pattern: "*.txt;*.md", ExcludeMatches: true, FilterFolders: true },
            "filter settings round-trip");

        var legacy = rendered.Replace("FormatVersion=10", "FormatVersion=5", StringComparison.Ordinal)
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

        var migrationPath = Path.Combine(root, "quick-filter-v5.ini");
        File.WriteAllText(migrationPath, legacy);
        var store = new SessionStore(migrationPath, Path.Combine(root, "missing-filter-session.json"));
        var loaded = store.Load() ?? throw new Exception("v5 profile failed to load");
        store.Save(loaded);
        var backups = Directory.GetFiles(root, "quick-filter-v5.ini.before-v10-*.bak");
        Require(backups.Length == 1 && File.ReadAllText(backups[0]) == legacy &&
            File.ReadAllText(migrationPath).Contains("FormatVersion=10", StringComparison.Ordinal),
            "v5 upgrade keeps the original snapshot before writing v9");
        Console.WriteLine("Quick filter session smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Quick filter session: " + message);
    }
}


