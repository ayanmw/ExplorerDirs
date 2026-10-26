using System.IO;

namespace ExplorerDirs.App;

internal sealed record AddressPathMenuEntry(string Header, string Identity, bool IsAncestor);

internal static class AddressPathMenu
{
    private static readonly (string Header, string Identity)[] CommonLocations =
    [
        ("桌面", "shell:Desktop"),
        ("用户文件夹", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        ("下载", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        ("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ("此电脑", "shell:MyComputerFolder"),
        ("库", "shell:Libraries"),
        ("网络", "shell:NetworkPlacesFolder"),
        ("控制面板", "shell:ControlPanelFolder"),
        ("回收站", "shell:RecycleBinFolder")
    ];

    internal static IReadOnlyList<AddressPathMenuEntry> Build(string currentPath,
        IEnumerable<(string Header, string Identity)>? commonLocations = null)
    {
        var entries = new List<AddressPathMenuEntry>();
        foreach (var segment in BreadcrumbPath.Build(currentPath).Reverse())
            entries.Add(new(segment.Label, segment.Path, IsAncestor: true));

        foreach (var (header, identity) in commonLocations ?? CommonLocations)
        {
            if (string.IsNullOrWhiteSpace(identity) || entries.Any(entry =>
                    string.Equals(entry.Identity, identity, StringComparison.OrdinalIgnoreCase))) continue;
            entries.Add(new(header, identity, IsAncestor: false));
        }
        return entries;
    }
}
