using System.IO;

namespace ExplorerDirs.App;

internal sealed record AddressPathMenuEntry(string Header, string Identity, bool IsAncestor,
    string? ParentIdentity, int Depth, bool IsCurrent);
internal sealed record AddressPathMenuLocation(string Header, string Identity, string? ParentIdentity = null);

internal static class AddressPathMenu
{
    private const string ThisPcIdentity = "shell:MyComputerFolder";

    internal static IReadOnlyList<AddressPathMenuEntry> Build(string currentPath,
        IEnumerable<AddressPathMenuLocation>? commonLocations = null)
    {
        var entries = new List<AddressPathMenuEntry>();
        var pathSegments = BreadcrumbPath.Build(currentPath);
        for (var index = 0; index < pathSegments.Count; index++)
        {
            var segment = pathSegments[index];
            entries.Add(new(segment.Label, segment.Path, IsAncestor: true,
                ParentIdentity: index == 0 ? null : pathSegments[index - 1].Path,
                Depth: index, IsCurrent: index == pathSegments.Count - 1));
        }

        foreach (var location in commonLocations ?? BuildCommonLocations())
        {
            if (string.IsNullOrWhiteSpace(location.Identity) ||
                (location.ParentIdentity is null && entries.Any(entry =>
                    string.Equals(entry.Identity, location.Identity, StringComparison.OrdinalIgnoreCase)))) continue;
            entries.Add(new(location.Header, location.Identity, IsAncestor: false,
                location.ParentIdentity, Depth: location.ParentIdentity is null ? 0 : 1, IsCurrent: false));
        }
        return entries;
    }

    internal static IReadOnlyList<AddressPathMenuEntry> BuildComputerMenu() =>
        Build(ThisPcIdentity, BuildComputerLocations().Skip(1)
            .Select(location => location with { ParentIdentity = null }));

    private static IReadOnlyList<AddressPathMenuLocation> BuildCommonLocations()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locations = new List<AddressPathMenuLocation>
        {
            new("桌面", "shell:Desktop"),
            new("用户文件夹", profile),
            // The known folder may be redirected; do not assume its default profile path.
            new("下载", "shell:Downloads"),
            new("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new("此电脑", ThisPcIdentity)
        };

        locations.AddRange(BuildComputerLocations().Skip(1));

        locations.AddRange(
        [
            new("库", "shell:Libraries"),
            new("网络", "shell:NetworkPlacesFolder"),
            new("控制面板", "shell:ControlPanelFolder"),
            new("回收站", "shell:RecycleBinFolder")
        ]);
        return locations;
    }

    internal static IReadOnlyList<AddressPathMenuLocation> DefaultUserLocations() =>
    [
        new("主目录", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        new("桌面", "shell:Desktop"), new("下载", "shell:Downloads"),
        new("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
    ];

    private static IReadOnlyList<AddressPathMenuLocation> BuildComputerLocations()
    {
        var locations = new List<AddressPathMenuLocation> { new("此电脑", ThisPcIdentity) };
        foreach (var driveRoot in Environment.GetLogicalDrives())
        {
            var drive = new DriveInfo(driveRoot);
            var volumeLabel = GetFixedDriveVolumeLabel(drive);
            locations.Add(new(GetDriveHeader(driveRoot, drive.DriveType, volumeLabel),
                driveRoot, ThisPcIdentity));
        }
        return locations;
    }

    internal static string GetDriveHeader(string root, DriveType driveType, string volumeLabel)
    {
        var driveLetter = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (driveType == DriveType.Fixed && !string.IsNullOrWhiteSpace(volumeLabel))
            return $"{volumeLabel.Trim()} ({driveLetter})";

        var typeName = driveType switch
        {
            DriveType.Fixed => "本地磁盘",
            DriveType.Removable => "可移动磁盘",
            DriveType.CDRom => "光盘驱动器",
            DriveType.Network => "网络驱动器",
            DriveType.Ram => "RAM 磁盘",
            _ => "驱动器"
        };
        return $"{typeName} ({driveLetter})";
    }

    private static string GetFixedDriveVolumeLabel(DriveInfo drive)
    {
        // Network drives can block while reading metadata, so only query ready local disks.
        if (drive.DriveType != DriveType.Fixed) return string.Empty;
        try { return drive.IsReady ? drive.VolumeLabel : string.Empty; }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }
}
