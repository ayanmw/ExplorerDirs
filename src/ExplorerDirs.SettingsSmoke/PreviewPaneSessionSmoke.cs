using ExplorerDirs.App;
using System.IO;

internal static class PreviewPaneSessionSmoke
{
    internal static void Run(string root)
    {
        var visible = new SessionState(4, 4,
            [[new TabSettings(@"C:\preview", ViewState.Default(), previewPaneVisible: true)], [], [], []]);
        var details = new SessionState(4, 4,
            [[new TabSettings(@"C:\details", ViewState.Default(), detailsPaneVisible: true)], [], [], []]);
        var rendered = SessionStore.Render(visible);
        Require(rendered.Contains("FormatVersion=10", StringComparison.Ordinal), "shell pane session advances to v10");
        Require(rendered.Contains("PreviewPaneVisible=true", StringComparison.Ordinal), "visible preview state is written per tab");
        Require(SessionStore.ParseComplete(rendered)?.Panes[0][0].PreviewPaneVisible == true,
            "visible preview state round-trips");
        var detailsText = SessionStore.Render(details);
        Require(detailsText.Contains("DetailsPaneVisible=true", StringComparison.Ordinal), "visible details state is written per tab");
        Require(SessionStore.ParseComplete(detailsText)?.Panes[0][0].DetailsPaneVisible == true &&
                SessionStore.ParseComplete(detailsText)?.Panes[0][0].PreviewPaneVisible == false,
            "visible details state round-trips exclusively");

        var version7 = rendered.Replace("FormatVersion=10", "FormatVersion=7", StringComparison.Ordinal)
            .Replace("DetailsPaneVisible=false" + Environment.NewLine, string.Empty, StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(version7)?.Panes[0][0].PreviewPaneVisible == true &&
                SessionStore.ParseComplete(version7)?.Panes[0][0].DetailsPaneVisible == false,
            "v7 preview state remains readable and details defaults off");

        var older = version7.Replace("FormatVersion=7", "FormatVersion=6", StringComparison.Ordinal)
            .Replace("PreviewPaneVisible=true" + Environment.NewLine, string.Empty, StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(older)?.Panes[0][0].PreviewPaneVisible == false,
            "v6 sessions default preview visibility to off");
        Require(SessionStore.ParseComplete(older)?.Panes[0][0].DetailsPaneVisible == false,
            "v6 sessions default details visibility to off");

        var malformed = rendered.Replace("PreviewPaneVisible=true", "PreviewPaneVisible=maybe", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(malformed) is null, "malformed preview visibility invalidates the snapshot");
        Require(SessionStore.ParseComplete(detailsText.Replace("DetailsPaneVisible=true", "DetailsPaneVisible=maybe", StringComparison.Ordinal)) is null,
            "malformed details visibility invalidates the snapshot");
        var contradictory = detailsText.Replace("PreviewPaneVisible=false", "PreviewPaneVisible=true", StringComparison.Ordinal);
        Require(SessionStore.ParseComplete(contradictory) is null, "a session cannot request preview and details simultaneously");

        var folder = Path.Combine(root, "preview-v7-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ExplorerDirs.ini");
        File.WriteAllText(path, version7);
        var store = new SessionStore(path, Path.Combine(folder, "missing.json"));
        Require(store.Load() is not null, "v7 session remains readable before upgrade");
        store.Save(visible);
        var backups = Directory.GetFiles(folder, "ExplorerDirs.ini.before-v10-*.bak");
        Require(backups.Length == 1 && File.ReadAllText(backups[0]) == version7,
            "v7 session is backed up exactly before writing v10");
        Require(SessionStore.ParseComplete(File.ReadAllText(path))?.Panes[0][0].PreviewPaneVisible == true,
            "upgraded session retains enabled preview state");
        Console.WriteLine("Shell pane session smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}


