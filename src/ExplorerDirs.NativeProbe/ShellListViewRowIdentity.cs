using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ExplorerDirs.NativeProbe;

// Shared identity capture for the filesystem and virtual Shell ListView probes.
internal sealed record ShellListViewRowIdentity(
    string DisplayName,
    string ParsingName,
    string? FileSystemPath,
    uint Attributes,
    string TypeName,
    int SystemImageIndex)
{
    private const uint ShellFileSystem = 0x40000000;
    private const uint SigDnDesktopAbsoluteParsing = 0x80028000;
    private const uint SigDnFileSystemPath = 0x80058000;
    private const uint ShgfiSysIconIndex = 0x00004000;
    private const uint ShgfiSmallIcon = 0x00000001;
    private const uint ShgfiPidl = 0x00000008;
    private const uint ShgfiTypeName = 0x00000400;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareAll = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static ShellListViewRowIdentity Capture(nint parentFolder, nint relativePidl,
        nint absolutePidl, ref nint systemImageList)
    {
        uint attributes = uint.MaxValue;
        var childArray = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(childArray, relativePidl);
            var attributesResult = At<GetAttributesOf>(parentFolder, 9)(parentFolder, 1, childArray, ref attributes);
            Marshal.ThrowExceptionForHR(attributesResult);
        }
        finally { Marshal.FreeHGlobal(childArray); }

        var displayName = ReadName(absolutePidl, 0);
        var parsingName = ReadName(absolutePidl, SigDnDesktopAbsoluteParsing);
        string? fileSystemPath = null;
        if ((attributes & ShellFileSystem) != 0)
            fileSystemPath = ReadName(absolutePidl, SigDnFileSystemPath);

        var imageList = SHGetFileInfo(absolutePidl, 0, out var fileInfo,
            (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiPidl | ShgfiSysIconIndex | ShgfiSmallIcon | ShgfiTypeName);
        if (imageList == 0 || fileInfo.ImageIndex < 0)
            throw new InvalidOperationException("Shell did not return a system icon for this row PIDL.");
        if (systemImageList == 0) systemImageList = imageList;
        else if (systemImageList != imageList)
            throw new InvalidOperationException("Shell returned different shared image lists for one view.");

        return new ShellListViewRowIdentity(displayName, parsingName, fileSystemPath, attributes,
            fileInfo.TypeName ?? string.Empty, fileInfo.ImageIndex);
    }

    internal static string GetAbsoluteParsingName(nint pidl) => ReadName(pidl, SigDnDesktopAbsoluteParsing);

    internal static ShellFileSystemIdentity? GetFileSystemIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        using var handle = CreateFileW(path, FileReadAttributes, FileShareAll, 0, OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics, 0);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, FileIdInfoClass,
                out var fileInfo, (uint)Marshal.SizeOf<FileIdInfo>()))
            return null;
        return new ShellFileSystemIdentity(fileInfo.VolumeSerialNumber, fileInfo.FileIdLow, fileInfo.FileIdHigh);
    }

    private static string ReadName(nint pidl, uint kind)
    {
        Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, kind, out var namePointer));
        try
        {
            var name = Marshal.PtrToStringUni(namePointer);
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Shell returned an empty row name.");
            return name;
        }
        finally { Marshal.FreeCoTaskMem(namePointer); }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAttributesOf(nint self, uint count, nint children, ref uint attributes);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int ImageIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName;
    }

    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint SHGetFileInfo(nint item, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass,
        out FileIdInfo fileInformation, uint bufferSize);
}
