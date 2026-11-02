using System.IO;

namespace ExplorerDirs.App;

internal static class FolderSizeLocationPolicy
{
    internal static readonly PropertyKeyState SizeColumn = new(new Guid("9e5e05ac-1936-4a75-94f7-4704b8b01923"), 1);

    internal static ViewState WithSizeColumn(ViewState view, bool enabled)
    {
        // Disabling folder totals must not remove the ordinary file-size column or change saved sorting.
        if (!enabled || view.DisplayColumns?.Any(column => column.Key == SizeColumn) == true) return view.Copy();
        if (view.DisplayColumns is { Count: > 0 } saved)
        {
            // Respect the persisted layout limit instead of replacing the user's last column.
            return saved.Count >= ColumnLayout.MaximumColumns ? view.Copy() : view with
            {
                DisplayColumns = saved.Append(new DisplayColumnState(SizeColumn, 110)).ToArray()
            };
        }
        var basic = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac");
        return view with { DisplayColumns = new DisplayColumnState[]
        {
            new(new PropertyKeyState(basic, 10), 260),
            new(new PropertyKeyState(basic, 14), 160),
            new(new PropertyKeyState(basic, 4), 140),
            new(SizeColumn, 110)
        } };
    }

    internal static string LogicalPath(string identity, Guid? profileClassId) =>
        QdrShellPathCodec.TryGetProviderPath(identity, profileClassId, out var path) ? path : identity;

    internal static string? ProviderParent(string? nativeIdentity, Guid? profileClassId)
    {
        if (nativeIdentity is null || !QdrShellPathCodec.TryGetProviderPath(nativeIdentity, profileClassId, out var path))
            return null;
        // An encoded provider root is not the filesystem root: Shell's parent would leave the physical hierarchy.
        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? "shell:MyComputerFolder";
    }

    // Call with a Shell-confirmed canonical identity and attributes, not a guessed filename extension.
    // ZIP streams remain native; a real directory named *.zip is still an ordinary directory.
    internal static string NativeIdentity(string canonicalIdentity, uint attributes, Guid? profileClassId, bool enabled)
    {
        if (QdrShellPathCodec.TryGetProviderPath(canonicalIdentity, profileClassId, out _)) return canonicalIdentity;
        return enabled && profileClassId is { } classId && classId != Guid.Empty &&
            Path.IsPathFullyQualified(canonicalIdentity) && ShellItemKind.IsFileSystemFolder(attributes)
                ? QdrShellPathCodec.ToProviderIdentity(canonicalIdentity, classId) : canonicalIdentity;
    }

    internal static bool TryGetProviderChildIdentity(string nativeParent, string logicalParent, string itemIdentity,
        Guid? profileClassId, out string childIdentity)
    {
        childIdentity = string.Empty;
        if (!QdrShellPathCodec.TryGetProviderPath(nativeParent, profileClassId, out var parent) ||
            !FavoritesStore.SamePath(parent, logicalParent)) return false;
        var physicalItem = LogicalPath(itemIdentity, profileClassId);
        if (!Path.IsPathFullyQualified(physicalItem)) return false;
        physicalItem = Path.TrimEndingDirectorySeparator(physicalItem);
        if (!FavoritesStore.SamePath(Path.GetDirectoryName(physicalItem) ?? "", logicalParent)) return false;
        var name = Path.GetFileName(physicalItem);
        if (name.Length == 0) return false;
        childIdentity = nativeParent + "\\" + name;
        return true;
    }
}
