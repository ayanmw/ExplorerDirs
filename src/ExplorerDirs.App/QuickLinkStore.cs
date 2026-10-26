using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record QuickLinkEntry(string Name, string Path);

internal sealed class QuickLinkStore(string directory)
{
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private readonly string _directory = Path.GetFullPath(directory);
    public string? LoadWarning { get; private set; }

    public IReadOnlyList<QuickLinkEntry> Load()
    {
        LoadWarning = null;
        if (!Directory.Exists(_directory))
        {
            if (File.Exists(_directory))
                LoadWarning = $"快速链接路径被同名文件占用：{_directory}";
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(_directory, "*.lnk", SearchOption.TopDirectoryOnly)
                .Select(path => new QuickLinkEntry(Path.GetFileNameWithoutExtension(path), path))
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadWarning = $"无法读取快速链接目录：{ex.Message}";
            return [];
        }
    }

    public string AddFolder(string path)
    {
        var target = Path.GetFullPath(path);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException($"目录不存在：{target}");
        return AddFolderLocation(target);
    }

    public string AddFolderLocation(string identity)
    {
        if (Directory.Exists(identity))
            return AddTarget(Path.GetFullPath(identity));
        if (!ShellLocationIdentity.TryGetFolderPidl(identity, out var pidl))
            throw new DirectoryNotFoundException($"Shell 文件夹不存在或无法解析：{identity}");
        try
        {
            return SaveShortcut(ShellLocationIdentity.GetDisplayName(identity),
                shellLink => Marshal.ThrowExceptionForHR(shellLink.SetIDList(pidl)));
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    public bool CanAddLocation(string identity)
    {
        if (File.Exists(identity) || Directory.Exists(identity)) return true;
        if (!ShellLocationIdentity.TryGetFolderPidl(identity, out var pidl)) return false;
        Marshal.FreeCoTaskMem(pidl);
        return true;
    }

    public string AddLocation(string identity)
    {
        if (File.Exists(identity)) return AddTarget(identity);
        if (Directory.Exists(identity) || CanAddLocation(identity)) return AddFolderLocation(identity);
        throw new FileNotFoundException("快速链接目标不存在或不是可解析的 Shell 文件夹。", identity);
    }

    public string AddTarget(string path)
    {
        var target = Path.GetFullPath(path);
        if (!File.Exists(target) && !Directory.Exists(target))
            throw new FileNotFoundException("快速链接目标不存在。", target);

        Directory.CreateDirectory(_directory);
        var title = Directory.Exists(target)
            ? new DirectoryInfo(target).Name
            : Path.GetFileNameWithoutExtension(target);
        if (string.IsNullOrWhiteSpace(title)) title = target;
        return SaveShortcut(title, shellLink =>
        {
            Marshal.ThrowExceptionForHR(shellLink.SetPath(target));
            if (Directory.Exists(target))
                Marshal.ThrowExceptionForHR(shellLink.SetWorkingDirectory(target));
        });
    }

    public bool TryGetTarget(string shortcutPath, out string target)
    {
        target = string.Empty;
        if (!File.Exists(shortcutPath) || !Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return false;

        object? instance = null;
        try
        {
            var classType = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)!;
            instance = Activator.CreateInstance(classType);
            var shellLink = (IShellLinkW)instance!;
            Marshal.ThrowExceptionForHR(((IPersistFile)shellLink).Load(Path.GetFullPath(shortcutPath), 0));
            var pidlResult = shellLink.GetIDList(out var pidl);
            if (pidlResult >= 0 && pidl != IntPtr.Zero)
            {
                try
                {
                    if (ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var identity, out _))
                    {
                        target = identity;
                        return true;
                    }
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            else if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);

            var buffer = new StringBuilder(32768);
            var result = shellLink.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            if (result < 0 || buffer.Length == 0) return false;
            target = buffer.ToString();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }

    private string GetAvailablePath(string title)
    {
        var candidate = Path.Combine(_directory, $"{title}.lnk");
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(_directory, $"{title} ({suffix}).lnk");
        return candidate;
    }

    private string SaveShortcut(string title, Action<IShellLinkW> configure)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        title = new string(title.Select(character =>
            invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character).ToArray()).TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(title)) title = "快速链接";
        Directory.CreateDirectory(_directory);
        var shortcutPath = GetAvailablePath(title);
        var classType = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)!;
        var shellLink = (IShellLinkW)Activator.CreateInstance(classType)!;
        try
        {
            configure(shellLink);
            Marshal.ThrowExceptionForHR(((IPersistFile)shellLink).Save(shortcutPath, true));
        }
        finally { Marshal.FinalReleaseComObject(shellLink); }
        return shortcutPath;
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath(StringBuilder path, int pathLength, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemIdList);
        [PreserveSig] int SetIDList(IntPtr itemIdList);
        [PreserveSig] int GetDescription(StringBuilder description, int length);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int GetWorkingDirectory(StringBuilder directory, int length);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments(StringBuilder arguments, int length);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation(StringBuilder iconPath, int length, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        [PreserveSig] int GetCurFile(out IntPtr fileName);
    }
}
