using System.IO;

namespace ExplorerDirs.App;

internal sealed record WorkspaceEntry(string Name, string Path);
internal sealed record WorkspaceListing(IReadOnlyList<WorkspaceEntry> Entries, bool Truncated);

internal static class WorkspaceCatalog
{
    internal const int MaximumEntries = 200;

    internal static string DirectoryFor(string sessionPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sessionPath))!, "Workspaces");

    // Call from a worker: a portable profile may reside on a slow volume.
    // Read names only; importing a clicked entry performs fresh validation.
    internal static WorkspaceListing Read(string directory)
    {
        List<WorkspaceEntry> entries;
        try
        {
            entries = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path).Equals(".qdr", StringComparison.OrdinalIgnoreCase))
                .Take(MaximumEntries + 1)
                .Select(path => new WorkspaceEntry(Path.GetFileNameWithoutExtension(path), path)).ToList();
        }
        catch (DirectoryNotFoundException) { return new([], false); }
        var truncated = entries.Count > MaximumEntries;
        return new(entries.Take(MaximumEntries).OrderBy(item => item.Name,
            StringComparer.CurrentCultureIgnoreCase).ToArray(), truncated);
    }

    internal static bool IsInDirectory(string path, string directory) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), StringComparison.OrdinalIgnoreCase);
}
