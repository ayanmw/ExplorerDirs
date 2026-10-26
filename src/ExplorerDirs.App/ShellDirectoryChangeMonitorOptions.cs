using System.IO;

namespace ExplorerDirs.App;

internal readonly record struct ShellDirectoryChangeDelta(int Delta, bool ForceRefresh);

internal static class ShellDirectoryChangeMonitorOptions
{
    internal static NotifyFilters NotifyFilter { get; } = NotifyFilters.FileName | NotifyFilters.DirectoryName |
        NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime;

    internal static ShellDirectoryChangeDelta ForCreated() => new(1, false);
    internal static ShellDirectoryChangeDelta ForDeleted() => new(-1, false);
    internal static ShellDirectoryChangeDelta ForChanged() => new(0, false);
    internal static ShellDirectoryChangeDelta ForRenamed() => new(0, true);
    internal static ShellDirectoryChangeDelta ForError() => new(0, true);
}
