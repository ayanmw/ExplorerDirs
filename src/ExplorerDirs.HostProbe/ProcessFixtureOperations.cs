using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

// Diagnostic producer only: the child touches generated fixtures and creates no
// view. The host keeps pumping while IFileOperation runs in another process.
internal sealed class ProcessFixtureOperations : IDisposable
{
    private readonly List<Process> children = new();
    internal static int RunChild(string[] args)
    {
        if (args.Length != 3 || args[1] is not ("copy" or "delete")) return 2;
        var root = Path.GetFullPath(args[2]);
        var fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        if (!string.Equals(Path.GetDirectoryName(root), fixtures, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(root), "N", out _) ||
            !Directory.Exists(root) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(fixtures) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Child operation rejected a non-fixture root.");
        var folders = Enumerable.Range(0, 4).Select(i => Path.Combine(root, $"pane-{i}")).ToArray();
        ShellFixtureOperations.Run(root, folders, args[1] == "copy");
        // Console.In's synchronized reader can block even through ReadLineAsync;
        // keep pipe waiting off this STA so readiness and Shell pumping proceed.
        var stop = Task.Run(() => Console.In.ReadLine());
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (stop.IsCompleted) frame.Continue = false; };
        timer.Start();
        Console.WriteLine("child-shell-operation disk-verified=true");
        Console.Out.Flush();
        // Keep the producer alive and pumping until observation ends. Exiting
        // immediately after IFileOperation would add a notification confounder.
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        var command = stop.GetAwaiter().GetResult();
        if (command is not (null or "stop")) throw new IOException("Unexpected child stop command.");
        return 0;
    }

    internal async Task RunAsync(string root, bool copy)
    {
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("--fixture-shell-operation");
        start.ArgumentList.Add(copy ? "copy" : "delete");
        start.ArgumentList.Add(root);
        var process = Process.Start(start) ?? throw new IOException("Could not start fixture producer.");
        children.Add(process);
        var output = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (output != "child-shell-operation disk-verified=true")
            throw new IOException("Fixture producer did not confirm disk results.");
        EnsureRunning();
    }

    internal void EnsureRunning()
    {
        if (children.Any(p => p.HasExited)) throw new IOException("Fixture producer exited during observation.");
    }

    public void Dispose()
    {
        Exception? failure = null;
        foreach (var process in children)
        {
            try
            {
                if (process.HasExited) throw new IOException("Fixture producer exited before shutdown.");
                process.StandardInput.WriteLine("stop");
                process.StandardInput.Flush();
                if (!process.WaitForExit(5000)) throw new TimeoutException("Fixture producer did not stop.");
                if (process.ExitCode != 0 || process.StandardError.ReadToEnd().Length != 0)
                    throw new IOException("Fixture producer shutdown failed.");
            }
            catch (Exception ex) { failure ??= ex; }
            finally
            {
                try
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                }
                finally { process.Dispose(); }
            }
        }
        if (failure is not null) throw new IOException("Fixture producer cleanup failed.", failure);
    }
}
