using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellSelectionFolderResolver
{
    private const uint FolderAttribute = 0x20000000;

    internal static bool TryResolve(string? absoluteParsingName, uint shellAttributes, out string identity)
    {
        identity = string.Empty;
        if ((shellAttributes & FolderAttribute) == 0 ||
            !ShellLocationIdentity.TryNormalize(absoluteParsingName ?? string.Empty, out var normalized, out _) ||
            !ShellLocationIdentity.TryGetFolderPidl(normalized, out var pidl))
            return false;

        try
        {
            identity = normalized;
            return true;
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }
}
