using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.App;

internal static class WindowsMagnifierLauncher
{
    internal static ProcessStartInfo CreateStartInfo() => new(
        Path.Combine(Environment.SystemDirectory, "Magnify.exe"))
    {
        UseShellExecute = true
    };

    internal static bool IsRunning()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("Magnify"); }
        catch (System.ComponentModel.Win32Exception) { return true; } // Yield the overlapping system shortcut when process state is unknown.
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    internal static void Open()
    {
        using var process = Process.Start(CreateStartInfo());
        if (process is null) throw new InvalidOperationException("Windows 放大镜未能启动。");
    }
}
