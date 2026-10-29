using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.App;

internal static class SystemExplorerLauncher
{
    internal static ProcessStartInfo CreateStartInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        start.ArgumentList.Add(path);
        return start;
    }

    internal static void Open(string path)
    {
        using var process = Process.Start(CreateStartInfo(path));
        if (process is null) throw new InvalidOperationException("Windows Explorer 未能启动。");
    }
}
