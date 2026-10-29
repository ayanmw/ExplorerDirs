using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellSelectionFolderResolverSmoke
{
    internal static void Run()
    {
        const uint Folder = 0x20000000;
        const uint FileSystem = 0x40000000;
        var tempDirectory = Path.GetTempPath();

        Check(ShellSelectionFolderResolver.TryResolve(tempDirectory, Folder | FileSystem, out var fileSystemIdentity) &&
              string.Equals(Path.GetFullPath(tempDirectory), fileSystemIdentity, StringComparison.OrdinalIgnoreCase),
            "a selected filesystem folder resolves to its normalized path");
        Check(ShellSelectionFolderResolver.TryResolve("shell:MyComputerFolder", Folder, out var virtualIdentity) &&
              virtualIdentity.Equals("shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase),
            "a selected virtual Shell folder resolves to its stable Shell identity");
        Check(!ShellSelectionFolderResolver.TryResolve(tempDirectory, FileSystem, out _),
            "non-folder Shell items are rejected even when their parsing name resolves");
        Check(!ShellSelectionFolderResolver.TryResolve("::{00000000-0000-0000-0000-000000000000}", Folder, out _),
            "unresolvable virtual locations are rejected");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
