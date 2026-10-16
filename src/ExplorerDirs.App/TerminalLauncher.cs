using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.App;

internal static class TerminalLauncher
{
    public static Task OpenAsync(string directory, CancellationToken cancellationToken,
        Action<ProcessStartInfo>? launch = null) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            WorkingDirectory = directory,
            UseShellExecute = false
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (launch is not null)
        {
            launch(startInfo);
            return;
        }

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("命令行进程未能启动。");
    }, cancellationToken);
}
