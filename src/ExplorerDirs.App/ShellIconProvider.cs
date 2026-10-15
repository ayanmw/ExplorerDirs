using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerDirs.App;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string path, bool isDirectory)
    {
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

        try
        {
            var icon = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            icon.Freeze();
            Cache[key] = icon;
            return icon;
        }
        finally
        {
            DestroyIcon(info.Icon);
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

public sealed class FileIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FileEntry entry ? ShellIconProvider.GetIcon(entry.FullPath, entry.IsDirectory) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
