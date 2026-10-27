using System.IO;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class WindowBackgroundOperationSmoke
{
    internal static async Task RunAsync()
    {
        using var alreadyClosed = new CancellationTokenSource();
        alreadyClosed.Cancel();
        var calls = new int[1];
        var skipped = WindowBackgroundOperation.RunAsync(calls, static count => ++count[0], alreadyClosed.Token);
        try { await skipped; } catch (OperationCanceledException) { }
        Check(skipped.IsCanceled && calls[0] == 0, "a closed window never starts a new background operation");
        Check(await WindowBackgroundOperation.RunAsync(21, static number => number * 2, CancellationToken.None) == 42,
            "an active window receives its worker result");
        try
        {
            await WindowBackgroundOperation.RunAsync<int, int>(0,
                static _ => throw new IOException("synthetic worker failure"), CancellationToken.None);
            throw new InvalidOperationException("worker failure was hidden");
        }
        catch (IOException ex) when (ex.Message == "synthetic worker failure") { }

        using var closed = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = WindowBackgroundOperation.RunAsync((started, gate, finished), static state =>
        {
            state.started.SetResult(true);
            try { return state.gate.Task.GetAwaiter().GetResult(); }
            finally { state.finished.SetResult(true); }
        }, closed.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            closed.Cancel();
            try { await waiting.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
            Check(waiting.IsCanceled && !finished.Task.IsCompleted,
                "closing releases the UI wait without claiming the underlying IO was interrupted");
        }
        finally
        {
            gate.TrySetResult(42);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        Console.WriteLine("Window background operation smoke: PASS (controlled tasks; no disk IO or windows)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}

