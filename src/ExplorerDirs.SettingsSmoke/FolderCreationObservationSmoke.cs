using ExplorerDirs.App;
using System.IO;
using System.Threading;

namespace ExplorerDirs.SettingsSmoke;

internal static class FolderCreationObservationSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NewFolderObservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var observation = FolderCreationObservation.TryStart(root)
                ?? throw new InvalidOperationException("The isolated local folder observer could not start.");
            File.WriteAllText(Path.Combine(root, "ordinary-file.txt"), "fixture");
            var createdDirectory = Path.Combine(root, "created-folder");
            Directory.CreateDirectory(createdDirectory);

            if (!SpinWait.SpinUntil(() => observation.Snapshot().Directories.Count == 1,
                    TimeSpan.FromSeconds(3)))
                throw new InvalidOperationException("The folder observer did not capture the new directory.");

            var snapshot = observation.Snapshot();
            if (snapshot.Failed || snapshot.Directories.Count != 1 ||
                !string.Equals(snapshot.Directories[0], createdDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The folder observer must return the unique created directory and ignore files.");

            Console.WriteLine("Folder creation observation smoke: PASS");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
