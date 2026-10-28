using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class WindowsExplorerRestarter
{
    private static readonly TimeSpan ShellRestartGracePeriod = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FallbackShellRecoveryPeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShellRecoveryPollInterval = TimeSpan.FromMilliseconds(100);

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

        if (!IsCurrentShellProcess(shellProcessId, () =>
                TryGetShellProcessId(out var currentProcessId) ? currentProcessId : 0))
            throw new InvalidOperationException("桌面 Shell 已变化；未结束任何进程。");

        shellProcess.Kill();
        if (!shellProcess.WaitForExit((int)ShellRestartGracePeriod.TotalMilliseconds))
            throw new TimeoutException("Windows Shell 进程未能在 3 秒内退出。");

        // Windows may automatically relaunch the registered shell after its process exits.
        if (WaitForExpectedReplacementShell(shellProcessId, explorerPath,
                ReadCurrentShellProcessIdentity, ShellRestartGracePeriod, ShellRecoveryPollInterval, Thread.Sleep))
            return;

        using var startedProcess = Process.Start(new ProcessStartInfo(explorerPath)
        {
            UseShellExecute = true
        });
        if (startedProcess is null)
            throw new InvalidOperationException("Windows 资源管理器未能启动。");

        if (!WaitForExpectedReplacementShell(shellProcessId, explorerPath,
                ReadCurrentShellProcessIdentity, FallbackShellRecoveryPeriod,
                ShellRecoveryPollInterval, Thread.Sleep))
            throw new TimeoutException("Windows 资源管理器进程已启动，但桌面 Shell 未在 10 秒内恢复。");
    }

    internal static bool WaitForExpectedReplacementShell(uint previousProcessId, string expectedPath,
        Func<(uint ProcessId, string? ExecutablePath)> readShellIdentity,
        int maxAttempts, Action<TimeSpan> waitBetweenAttempts)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var (processId, executablePath) = readShellIdentity();
            if (IsExpectedReplacementShell(previousProcessId, processId, executablePath, expectedPath))
                return true;

            if (attempt + 1 < maxAttempts)
                waitBetweenAttempts(ShellRecoveryPollInterval);
        }

        return false;
    }

    private static bool WaitForExpectedReplacementShell(uint previousProcessId, string expectedPath,
        Func<(uint ProcessId, string? ExecutablePath)> readShellIdentity, TimeSpan timeout,
        TimeSpan pollInterval, Action<TimeSpan> waitBetweenAttempts)
    {
        var maxAttempts = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds / pollInterval.TotalMilliseconds) + 1);
        return WaitForExpectedReplacementShell(previousProcessId, expectedPath,
            readShellIdentity, maxAttempts, waitBetweenAttempts);
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

    internal static bool IsCurrentShellProcess(uint expectedProcessId, Func<uint> readCurrentProcessId) =>
        expectedProcessId != 0 && readCurrentProcessId() == expectedProcessId;

    internal static bool IsExpectedReplacementShell(uint previousProcessId, uint currentProcessId,
        string? actualPath, string? expectedPath) =>
        previousProcessId != 0 && currentProcessId != 0 && currentProcessId != previousProcessId &&
        MatchesExpectedExecutable(actualPath, expectedPath);

    private static (uint ProcessId, string? ExecutablePath) ReadCurrentShellProcessIdentity()
    {
        if (!TryGetShellProcessId(out var currentProcessId))
            return (0, null);

        try
        {
            using var process = Process.GetProcessById((int)currentProcessId);
            return (currentProcessId, process.MainModule?.FileName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // A Shell process can exit between reading GetShellWindow and opening its process handle.
            return (currentProcessId, null);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
