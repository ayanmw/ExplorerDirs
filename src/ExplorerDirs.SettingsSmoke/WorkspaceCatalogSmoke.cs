using ExplorerDirs.App;
using System.IO;

internal static class WorkspaceCatalogSmoke
{
    internal static void Run(string root)
    {
        var profile = Path.Combine(root, "workspace-" + Guid.NewGuid().ToString("N"), "ExplorerDirs.ini");
        var directory = WorkspaceCatalog.DirectoryFor(profile);
        Check(directory == Path.Combine(Path.GetDirectoryName(profile)!, "Workspaces"),
            "workspace follows the selected profile");
        Check(WorkspaceCatalog.Read(directory).Entries.Count == 0 && !Directory.Exists(directory),
            "listing absent workspace does not create it");
        Directory.CreateDirectory(directory);
        var nested = Directory.CreateDirectory(Path.Combine(directory, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "ignored.qdr"), "not a top-level workspace");
        File.WriteAllText(Path.Combine(directory, "ignored.ini"), "unrelated config");
        var path = Path.Combine(directory, "中文_A&B.QDR");
        var state = new SessionState(1, 4,
            Enumerable.Range(0, 4).Select(i => new List<TabSettings> {
                new(@"C:\fixture" + i, ViewState.Default()) }).ToList(), 0, [0, 0, 0, 0]);
        QdrExporter.Save(path, null, QdrExporter.Render(state, []));
        var listing = WorkspaceCatalog.Read(directory);
        Check(listing.Entries.Count == 1 && listing.Entries[0] == new WorkspaceEntry("中文_A&B", path) &&
            !listing.Truncated, "top-level, case-insensitive extension and literal title");
        var loaded = QdrImporter.Load(listing.Entries[0].Path);
        Check(SessionStore.Render(loaded.ExtendedState!) == SessionStore.Render(state),
            "workspace reuses the complete QDR snapshot");
        var changed = state with { Layout = 4 };
        File.WriteAllText(path, QdrExporter.Render(changed, []));
        Check(QdrImporter.Load(listing.Entries[0].Path).Layout == 4,
            "click-time read observes external edits after listing");
        File.WriteAllText(path, "damaged");
        Check(WorkspaceCatalog.Read(directory).Entries.Count == 1, "menu listing does not parse snapshot content");
        try { QdrImporter.Load(path); throw new Exception("damaged workspace accepted"); }
        catch (InvalidDataException) { }
        Check(WorkspaceCatalog.IsInDirectory(path, directory + Path.DirectorySeparatorChar) &&
            !WorkspaceCatalog.IsInDirectory(Path.Combine(nested, "ignored.qdr"), directory) &&
            !WorkspaceCatalog.IsInDirectory(Path.Combine(directory + "-other", "a.qdr"), directory),
            "saved status only claims a favorite for the exact directory");
        for (var i = 0; i < 200; i++)
            File.WriteAllText(Path.Combine(directory, $"extra-{i:D3}.qdr"), "");
        var limited = WorkspaceCatalog.Read(directory);
        Check(limited.Entries.Count == 200 && limited.Truncated, "menu has a bounded entry count");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Workspace: " + message); }
}
