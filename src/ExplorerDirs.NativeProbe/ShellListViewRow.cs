using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Shared prototype row identity for filesystem and virtual Shell namespace items.
internal sealed record ShellListViewRow(
    long Id,
    nint AbsolutePidl,
    nint RelativePidl,
    string DisplayName,
    string ParsingName,
    string? FileSystemPath,
    uint Attributes,
    ulong? RecursiveBytes,
    bool FromShellEnumeration,
    int SystemImageIndex)
{
    internal RecursiveDirectoryScanResult? RecursiveSummary { get; init; }
    internal ShellFileSystemIdentity? FileSystemIdentity { get; init; }
    internal string TypeName { get; init; } = string.Empty;
}

internal readonly record struct ShellFileSystemIdentity(
    ulong VolumeSerialNumber,
    ulong FileIdLow,
    ulong FileIdHigh);
