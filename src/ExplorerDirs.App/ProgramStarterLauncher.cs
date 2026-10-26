using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace ExplorerDirs.App;

internal sealed record ProgramStarterLaunchResult(bool Started, string? Error);

internal sealed class ProgramStarterLauncher(Func<ProcessStartInfo, bool>? startProcess = null)
{
    private readonly Func<ProcessStartInfo, bool> _startProcess = startProcess ?? StartProcess;

    public static ProcessStartInfo Prepare(ProgramStarterCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.FileName))
            throw new ArgumentException("程序路径不能为空。", nameof(command));
        if (!Path.IsPathFullyQualified(command.WorkingDirectory))
            throw new ArgumentException("工作目录必须是完整路径。", nameof(command));
        if (!Directory.Exists(command.WorkingDirectory))
            throw new DirectoryNotFoundException($"工作目录不存在：{command.WorkingDirectory}");

        var fileName = ResolveExecutable(command.FileName);
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false
        };
        foreach (var argument in command.Arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public ProgramStarterLaunchResult Start(ProgramStarterCommand command)
    {
        try
        {
            return _startProcess(Prepare(command))
                ? new ProgramStarterLaunchResult(true, null)
                : new ProgramStarterLaunchResult(false, "Windows 未能启动所选程序。");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                                   Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return new ProgramStarterLaunchResult(false, ex.Message);
        }
    }

    private static string ResolveExecutable(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            if (!File.Exists(fileName)) throw new FileNotFoundException("程序文件不存在。", fileName);
            return fileName;
        }
        if (Path.IsPathRooted(fileName))
            throw new ArgumentException("程序路径必须是完整路径或相对于应用程序目录的路径。", nameof(fileName));
        if (!fileName.Contains(Path.DirectorySeparatorChar) && !fileName.Contains(Path.AltDirectorySeparatorChar))
            return fileName;

        var resolved = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, fileName));
        if (!File.Exists(resolved)) throw new FileNotFoundException("相对程序文件不存在。", resolved);
        return resolved;
    }

    private static bool StartProcess(ProcessStartInfo info) => Process.Start(info) is not null;
}
