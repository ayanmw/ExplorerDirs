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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info,
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
