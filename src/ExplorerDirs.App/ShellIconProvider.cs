using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiPidl = 0x000000008;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string path, bool isDirectory)
    {
        if (isDirectory && ShellLocationIdentity.TryNormalize(path, out var identity, out var isFileSystemPath) &&
            !isFileSystemPath)
        {
            var shellKey = "<shell>" + identity;
            if (Cache.TryGetValue(shellKey, out var shellCached))
                return shellCached;
            if (ShellLocationIdentity.TryGetFolderPidl(identity, out var pidl))
            {
                try
                {
                    var icon = GetIcon(pidl, ShgfiIcon | ShgfiSmallIcon | ShgfiPidl);
                    if (icon is not null)
                    {
                        Cache[shellKey] = icon;
                        return icon;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }

        var extension = Path.GetExtension(path);
        var key = isDirectory ? "<folder>" : string.IsNullOrEmpty(extension) ? "<file>" : extension;
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var sample = isDirectory ? "folder" : "sample" + extension;
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var result = SHGetFileInfo(sample, attributes, out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
            return null;

        var iconSource = CreateImageSource(info.Icon);
        Cache[key] = iconSource;
        return iconSource;
    }

    private static ImageSource? GetIcon(nint pidl, uint flags)
    {
        var result = SHGetFileInfo(pidl, 0, out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        return result == 0 || info.Icon == IntPtr.Zero ? null : CreateImageSource(info.Icon);
    }

    private static ImageSource CreateImageSource(IntPtr iconHandle)
    {
        try
        {
            var icon = Imaging.CreateBitmapSourceFromHIcon(iconHandle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            icon.Freeze();
            return icon;
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes,
        out ShellFileInfo info, uint infoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(nint pidl, uint fileAttributes,
        out ShellFileInfo info, uint infoSize, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
