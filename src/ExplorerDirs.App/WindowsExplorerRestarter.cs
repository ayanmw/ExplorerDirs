using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ExplorerDirs.App;

internal static class WindowsExplorerRestarter
{
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x00000102;
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

        using var shellProcessHandle = OpenProcess(
            ProcessTerminate | ProcessQueryLimitedInformation | Synchronize, false, shellProcessId);
        if (shellProcessHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开桌面 Shell 进程；未结束任何进程。");

        var openedProcessId = GetProcessId(shellProcessHandle);
        var executablePath = ReadProcessImagePath(shellProcessHandle);
        if (!MatchesExpectedExecutable(executablePath, explorerPath))
            throw new InvalidOperationException("桌面 Shell 不是由 Windows 自带的 explorer.exe 承载；未结束任何进程。");

        if (!IsExpectedShellProcess(shellProcessId, openedProcessId,
                TryGetShellProcessId(out var currentProcessId) ? currentProcessId : 0,
                executablePath, explorerPath))
            throw new InvalidOperationException("桌面 Shell 已变化；未结束任何进程。");

        if (!TerminateProcess(shellProcessHandle, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法结束桌面 Shell 进程。");

        var waitResult = WaitForSingleObject(shellProcessHandle, (uint)ShellRestartGracePeriod.TotalMilliseconds);
        if (waitResult == WaitTimeout)
            throw new TimeoutException("Windows Shell 进程未能在 3 秒内退出。");
        if (waitResult != WaitObject0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "等待 Windows Shell 进程退出失败。");

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

    internal static bool IsExpectedShellProcess(uint originalShellProcessId, uint openedHandleProcessId,
        uint currentShellProcessId, string? actualPath, string expectedPath) =>
        originalShellProcessId != 0 && openedHandleProcessId == originalShellProcessId &&
        currentShellProcessId == originalShellProcessId &&
        MatchesExpectedExecutable(actualPath, expectedPath);

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
            using var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, currentProcessId);
            if (processHandle.IsInvalid) return (currentProcessId, null);
            return (GetProcessId(processHandle), ReadProcessImagePath(processHandle));
        }
        catch (Win32Exception)
        {
            // A Shell process can exit between reading GetShellWindow and opening its process handle.
            return (currentProcessId, null);
        }
    }

    private static string? ReadProcessImagePath(SafeProcessHandle processHandle)
    {
        var path = new StringBuilder(32768);
        uint pathLength = (uint)path.Capacity;
        if (!QueryFullProcessImageName(processHandle, 0, path, ref pathLength)) return null;
        path.Length = checked((int)pathLength);
        return path.ToString();
    }

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle processHandle, uint flags,
        StringBuilder executableName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(SafeProcessHandle processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
