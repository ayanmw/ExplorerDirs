using ExplorerDirs.App;
using System.IO;

internal static class PaneTreeSessionSmoke
{
    internal static void Run(string root)
    {
        var folder = Path.Combine(root, "pane-tree-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var sessionPath = Path.Combine(folder, "pane-tree-session.ini");
        var store = new SessionStore(sessionPath, Path.Combine(folder, "missing-pane-tree-session.json"));
        List<List<TabSettings>> panes =
        [
            [new(@"C:\pane-0", ViewState.Default())],
            [new(@"C:\pane-1", ViewState.Default())],
            [new(@"C:\pane-2", ViewState.Default())],
            [new(@"C:\pane-3", ViewState.Default())]
        ];
        var expected = new[] { true, false, true, false };
        var state = new SessionState(4, 4, panes, PaneTreeVisibility: expected,
            TreeDisplayMode: TreeDisplayMode.Shared,
            QdrTreeSplits: new QdrTreeSplitState("002500", "2500", null, "0007"));
        var rendered = SessionStore.Render(state);
        Require(rendered.Contains("FormatVersion=13", StringComparison.Ordinal) &&
            rendered.Contains("TreeDisplayMode=Shared", StringComparison.Ordinal) &&
            rendered.Contains("QdrTreeSplit1=002500", StringComparison.Ordinal) &&
            rendered.Contains("QdrTreeSplit4=0007", StringComparison.Ordinal) &&
            rendered.Contains("TreeVisible=True", StringComparison.Ordinal) &&
            rendered.Contains("TreeVisible=False", StringComparison.Ordinal),
            "v13 sessions persist tree mode, visibility, Q-Dir split values, and opaque color metadata");
        Require(SessionStore.ParseComplete(rendered) is { } parsed &&
            parsed.PaneTreeVisibility?.SequenceEqual(expected) == true &&
            parsed.TreeDisplayMode == TreeDisplayMode.Shared &&
            parsed.QdrTreeSplits == state.QdrTreeSplits,
            "tree mode, visibility, and exact Q-Dir split text round-trip with the session");
        var defaultTreeState = SessionStore.Render(state with { PaneTreeVisibility = null });
        Require(SessionStore.ParseComplete(defaultTreeState) is { } defaultParsed &&
            SessionStore.Render(defaultParsed) == defaultTreeState,
            "a rendered session without explicit tree visibility remains complete and stable");

        var v11 = string.Join(Environment.NewLine, rendered.Split('\n').Where(line =>
            !line.StartsWith("QdrTreeSplit", StringComparison.Ordinal)))
            .Replace("FormatVersion=13", "FormatVersion=12", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(v11)?.QdrTreeSplits is null,
            "v11 sessions remain readable without Q-Dir split metadata");
        var v9 = string.Join(Environment.NewLine, v11.Split('\n').Where(line =>
            !line.StartsWith("TreeDisplayMode=", StringComparison.Ordinal)))
            .Replace("FormatVersion=12", "FormatVersion=9", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(v9)?.TreeDisplayMode == TreeDisplayMode.PerPane,
            "v9 tree visibility migrates to per-pane mode");
        var v8 = string.Join(Environment.NewLine, rendered.Split('\n').Where(line =>
            !line.StartsWith("TreeVisible=", StringComparison.Ordinal) &&
            !line.StartsWith("TreeDisplayMode=", StringComparison.Ordinal)))
            .Replace("FormatVersion=13", "FormatVersion=8", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(v8)?.PaneTreeVisibility is null,
            "v8 sessions leave per-pane tree state to the existing profile default");

        foreach (var invalid in new[]
        {
            rendered.Replace("TreeVisible=True", "TreeVisible=maybe", StringComparison.Ordinal),
            rendered.Replace("QdrTreeSplit1=002500", "QdrTreeSplit1=25oops", StringComparison.Ordinal),
            string.Join(Environment.NewLine, rendered.Split('\n').Where(line =>
                !line.StartsWith("[Pane2]", StringComparison.Ordinal) &&
                !line.StartsWith("TreeVisible=", StringComparison.Ordinal)))
        })
            Require(SessionStore.ParseComplete(invalid) is null,
            "v11 profiles reject malformed or missing per-pane tree visibility");

        File.WriteAllText(sessionPath, v11);
        store = new SessionStore(sessionPath, Path.Combine(folder, "missing-pane-tree-session.json"));
        var loaded = store.Load()!;
        store.Save(loaded with { PaneTreeVisibility = expected });
        var backups = Directory.GetFiles(folder, "pane-tree-session.ini.before-v13-*.bak");
        Require(backups.Length == 1 && File.ReadAllText(backups[0]) == v11 &&
            File.ReadAllText(sessionPath).Contains("FormatVersion=13", StringComparison.Ordinal),
            "upgrading v12 to v13 preserves the exact original before adding Q-Dir color metadata");
    }

    private static void Require(bool valid, string message)
    { if (!valid) throw new Exception("Failed: " + message); }
}
