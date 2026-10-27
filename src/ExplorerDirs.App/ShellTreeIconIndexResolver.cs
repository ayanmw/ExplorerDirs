using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellTreeIconIndexResolver
{
    private const uint SystemImageIndex = 0x00004000;
    private const uint SmallIcon = 0x00000001;
    private const uint OpenIcon = 0x00000002;

    internal static nint GetSystemImageList()
    {
        var sample = Environment.SystemDirectory;
        var size = (uint)Marshal.SizeOf<ShellFileInfo>();
        return SHGetFileInfo(sample, 0, out _, size, SystemImageIndex | SmallIcon);
    }

    internal static bool TryResolve(string parsingName, out int normal, out int open)
    {
        normal = open = -1;
        if (string.IsNullOrWhiteSpace(parsingName)) return false;
        try
        {
            var size = (uint)Marshal.SizeOf<ShellFileInfo>();
            if (SHGetFileInfo(parsingName, 0, out var closedInfo, size, SystemImageIndex | SmallIcon) == 0 ||
                closedInfo.Index < 0) return false;

            normal = closedInfo.Index;
            if (SHGetFileInfo(parsingName, 0, out var openInfo, size,
                    SystemImageIndex | SmallIcon | OpenIcon) != 0 && openInfo.Index >= 0)
                open = openInfo.Index;
            else
                open = normal;
            return true;
        }
        catch
        {
            normal = open = -1;
            return false;
        }
    }

    internal static bool TryResolveShellItem(nint shellItem, out int normal, out int open)
    {
        normal = open = -1;
        if (shellItem == 0) return false;
        var result = SHGetIDListFromObject(shellItem, out var pidl);
        try
        {
            if (result < 0 || pidl == 0) return false;
            var size = (uint)Marshal.SizeOf<ShellFileInfo>();
            if (SHGetFileInfo(pidl, 0, out var closedInfo, size,
                    SystemImageIndex | SmallIcon | Pidl) == 0 || closedInfo.Index < 0)
                return false;

            normal = closedInfo.Index;
            if (SHGetFileInfo(pidl, 0, out var openInfo, size,
                    SystemImageIndex | SmallIcon | OpenIcon | Pidl) != 0 && openInfo.Index >= 0)
                open = openInfo.Index;
            else
                open = normal;
            return true;
        }
        catch
        {
            normal = open = -1;
            return false;
        }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info,
        uint infoSize, uint flags);

    private const uint Pidl = 0x00000008;

    [DllImport("shell32.dll")]
    private static extern int SHGetIDListFromObject(nint shellItem, out nint pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern nint SHGetFileInfo(nint pidl, uint attributes, out ShellFileInfo info,
        uint infoSize, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
}
