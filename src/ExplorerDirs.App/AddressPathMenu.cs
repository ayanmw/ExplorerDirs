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

    private static IReadOnlyList<AddressPathMenuLocation> BuildCommonLocations()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locations = new List<AddressPathMenuLocation>
        {
            new("桌面", "shell:Desktop"),
            new("用户文件夹", profile),
            new("下载", Path.Combine(profile, "Downloads")),
            new("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new("此电脑", ThisPcIdentity)
        };

        foreach (var driveRoot in Environment.GetLogicalDrives())
            locations.Add(new($"驱动器 ({driveRoot.TrimEnd(Path.DirectorySeparatorChar)})",
                driveRoot, ThisPcIdentity));

        locations.AddRange(
        [
            new("库", "shell:Libraries"),
            new("网络", "shell:NetworkPlacesFolder"),
            new("控制面板", "shell:ControlPanelFolder"),
            new("回收站", "shell:RecycleBinFolder")
        ]);
        return locations;
    }
}
