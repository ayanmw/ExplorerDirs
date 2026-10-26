using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ProgramStarterEntryCommitSmoke
{
    internal static void Run()
    {
        var original = new ProgramStarterEntry(Guid.NewGuid(), "原有", "viewer.exe", "", "");
        var external = new ProgramStarterEntry(Guid.NewGuid(), "外部更新", "other.exe", "", "");
        var proposed = new[]
        {
            original,
            new ProgramStarterEntry(Guid.NewGuid(), "新增", "editor.exe", "", "")
        };
        IReadOnlyList<ProgramStarterEntry> displayed = [original];
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs.ProgramStarterEntryCommitSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ProgramStarter.ini");
            var writer = new ProgramStarterStore(path);
            _ = writer.Load();
            writer.Save([original]);
            var stale = new ProgramStarterStore(path);
            _ = stale.Load();
            writer.Save([original, external]);

            try
            {
                ProgramStarterEntryCommit.Commit(proposed, stale.Save,
                saved => displayed = saved);
                throw new Exception("stale Program Starter save was accepted");
            }
            catch (IOException)
            {
                Check(displayed.Count == 1 && displayed[0] == original &&
                      new ProgramStarterStore(path).Load().Select(entry => entry.Name)
                          .SequenceEqual(["原有", "外部更新"]),
                    "a stale configuration conflict preserves the current list and external file contents");
            }

            var current = new ProgramStarterStore(path);
            _ = current.Load();
            ProgramStarterEntryCommit.Commit(proposed, current.Save, saved => displayed = saved);
            Check(displayed.Count == 2 && displayed[1].Name == "新增" &&
                  new ProgramStarterStore(path).Load().Select(entry => entry.Name).SequenceEqual(["原有", "新增"]),
                "a successful configuration write applies and persists the proposed command list");
            Console.WriteLine("Program Starter entry commit smoke: PASS");
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            var tempParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            var expectedPrefix = "ExplorerDirs.ProgramStarterEntryCommitSmoke-";
            if (Path.GetDirectoryName(resolved) == tempParent &&
                Path.GetFileName(resolved).StartsWith(expectedPrefix, StringComparison.Ordinal))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception("Program Starter entry commit: " + message);
        Console.WriteLine("PASS " + message);
    }
}
