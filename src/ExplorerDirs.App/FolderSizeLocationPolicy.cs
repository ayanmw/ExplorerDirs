using System.IO;

namespace ExplorerDirs.App;

internal static class FolderSizeLocationPolicy
{
    internal static string LogicalPath(string identity, Guid? profileClassId) =>
        QdrShellPathCodec.TryGetProviderPath(identity, profileClassId, out var path) ? path : identity;

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
