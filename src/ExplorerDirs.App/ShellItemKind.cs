namespace ExplorerDirs.App;

internal static class ShellItemKind
{
    // SFGAO values from shobjidl.h. Avoid SFGAO_VALIDATE here: opening a context
    // menu must not force a separate filesystem existence check.
    public const uint RequestedAttributes = 0x60400000;
    private const uint Folder = 0x20000000;
    private const uint FileSystem = 0x40000000;
    private const uint Stream = 0x00400000;

    public static bool IsFileSystemFolder(uint attributes) =>
        (attributes & (Folder | FileSystem)) == (Folder | FileSystem) &&
        (attributes & Stream) == 0;
}
