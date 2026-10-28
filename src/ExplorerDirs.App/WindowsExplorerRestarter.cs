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

        using var shellProcess = Process.GetProcessById((int)shellProcessId);
        shellProcess.Kill();
        if (!shellProcess.WaitForExit((int)ShellRestartGracePeriod.TotalMilliseconds))
            throw new TimeoutException("Windows Shell 进程未能在 3 秒内退出。");

        // Windows may automatically relaunch the registered shell after its process exits.
        var deadline = Environment.TickCount64 + (long)ShellRestartGracePeriod.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (TryGetShellProcessId(out var currentProcessId) && currentProcessId != shellProcessId)
                return;

            Thread.Sleep(100);
        }

        var explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(explorerPath))
            throw new FileNotFoundException("找不到 Windows 资源管理器程序。", explorerPath);

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

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
