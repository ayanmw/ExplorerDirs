using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class WindowsExplorerRestarter
{
    private static readonly TimeSpan ShellRestartGracePeriod = TimeSpan.FromSeconds(3);

    public static void Restart()
    {
        var shellWindow = GetShellWindow();
        if (shellWindow == 0 || GetWindowThreadProcessId(shellWindow, out var shellProcessId) == 0 ||
            shellProcessId == 0)
            throw new InvalidOperationException("Windows Shell 桌面窗口当前不可用。");

        var explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(explorerPath))
            throw new FileNotFoundException("找不到 Windows 资源管理器程序，未结束任何进程。", explorerPath);

        using var shellProcess = Process.GetProcessById((int)shellProcessId);
        if (!MatchesExpectedExecutable(shellProcess.MainModule?.FileName, explorerPath))
            throw new InvalidOperationException("桌面 Shell 不是由 Windows 自带的 explorer.exe 承载；未结束任何进程。");

        shellProcess.Kill();
        if (!shellProcess.WaitForExit((int)ShellRestartGracePeriod.TotalMilliseconds))
            throw new TimeoutException("Windows Shell 进程未能在 3 秒内退出。");

        // Windows may automatically relaunch the registered shell after its process exits.
        var deadline = Environment.TickCount64 + (long)ShellRestartGracePeriod.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (TryGetShellProcessId(out var currentProcessId) &&
                IsExpectedRunningReplacementShell(shellProcessId, currentProcessId, explorerPath))
                return;

            Thread.Sleep(100);
        }

        using var startedProcess = Process.Start(new ProcessStartInfo(explorerPath)
        {
            UseShellExecute = true
        });
        if (startedProcess is null)
            throw new InvalidOperationException("Windows 资源管理器未能启动。");
    }

    private static bool TryGetShellProcessId(out uint processId)
    {
        var shellWindow = GetShellWindow();
        if (shellWindow != 0 && GetWindowThreadProcessId(shellWindow, out processId) != 0)
            return processId != 0;

        processId = 0;
        return false;
    }

    internal static bool MatchesExpectedExecutable(string? actualPath, string? expectedPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath) || string.IsNullOrWhiteSpace(expectedPath))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static bool IsExpectedReplacementShell(uint previousProcessId, uint currentProcessId,
        string? actualPath, string? expectedPath) =>
        previousProcessId != 0 && currentProcessId != 0 && currentProcessId != previousProcessId &&
        MatchesExpectedExecutable(actualPath, expectedPath);

    private static bool IsExpectedRunningReplacementShell(uint previousProcessId,
        uint currentProcessId, string expectedPath)
    {
        try
        {
            using var process = Process.GetProcessById((int)currentProcessId);
            return IsExpectedReplacementShell(previousProcessId, currentProcessId,
                process.MainModule?.FileName, expectedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // A Shell process can exit between reading GetShellWindow and opening its process handle.
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
