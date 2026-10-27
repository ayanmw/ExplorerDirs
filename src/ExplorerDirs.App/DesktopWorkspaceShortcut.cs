using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.IO;
using System.Text;

namespace ExplorerDirs.App;

internal static class DesktopWorkspaceShortcut
{
    internal static void CreateWithWorkspace(string shortcutPath, string executablePath, string? assemblyPath,
        string qdrPath, string workingDirectory, string text, byte[]? expectedShortcut = null)
    {
        QdrExporter.Save(qdrPath, expected: null, text);
        try { Create(shortcutPath, executablePath, assemblyPath, qdrPath, workingDirectory, expectedShortcut); }
        catch
        {
            // Keep cleanup on the same worker that completed the write; UI cancellation cannot race it.
            try { File.Delete(qdrPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static void Create(string shortcutPath, string executablePath, string? assemblyPath,
        string qdrPath, string workingDirectory, byte[]? expectedShortcut = null)
    {
        var fullShortcut = Path.GetFullPath(shortcutPath);
        var fullExecutable = Path.GetFullPath(executablePath);
        var fullQdr = Path.GetFullPath(qdrPath);
        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        if (!Path.GetExtension(fullShortcut).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(fullQdr).Equals(".qdr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("桌面工作区项必须使用 .lnk，并指向 .qdr 状态文件。");

        var target = fullExecutable;
        var arguments = Quote(fullQdr);
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            target = fullExecutable;
            arguments = Quote(Path.GetFullPath(assemblyPath)) + " " + arguments;
        }

        var temporary = fullShortcut + $".{Guid.NewGuid():N}.tmp.lnk";
        IShellLinkW? link = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullShortcut)!);
            var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"),
                throwOnError: true)!;
            link = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(fullWorkingDirectory);
            link.SetDescription("Explorer Dirs · 打开已保存的多窗格工作区");
            ((IPersistFile)link).Save(temporary, false);
            Publish(temporary, fullShortcut, expectedShortcut);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Publish(string temporary, string target, byte[]? expected)
    {
        using var mutex = new Mutex(false, PortableIniWriter.MutexName(target));
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(500)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("快捷方式正由其他实例写入，请稍后重试。");
            var current = File.Exists(target) ? File.ReadAllBytes(target) : null;
            // Overwrite permission belongs to the exact bytes confirmed by the user, not just a filename.
            if (current is null ? expected is not null : expected is null || !current.SequenceEqual(expected))
                throw new IOException("快捷方式在确认后已被修改，未覆盖；请重新选择保存位置。");
            File.Move(temporary, target, overwrite: expected is not null);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
    internal static (string Target, string Arguments) ReadTargetAndArguments(string shortcutPath)
    {
        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"),
            throwOnError: true)!;
        var link = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
        try
        {
            ((IPersistFile)link).Load(Path.GetFullPath(shortcutPath), 0);
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH
            link.GetArguments(arguments, arguments.Capacity);
            return (target.ToString(), arguments.ToString());
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath,
            IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath,
            int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
