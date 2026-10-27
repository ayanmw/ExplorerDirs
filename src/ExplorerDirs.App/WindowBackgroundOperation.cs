namespace ExplorerDirs.App;

internal static class WindowBackgroundOperation
{
    internal static Task<TResult> RunAsync<TState, TResult>(TState state, Func<TState, TResult> operation,
        CancellationToken windowClosed)
    {
        if (windowClosed.IsCancellationRequested) return Task.FromCanceled<TResult>(windowClosed);
        return WaitAsync(Task.Run(() => operation(state)), windowClosed);
    }

    private static async Task<TResult> WaitAsync<TResult>(Task<TResult> worker, CancellationToken windowClosed)
    {
        try { return await worker.WaitAsync(windowClosed).ConfigureAwait(false); }
        catch (OperationCanceledException) when (windowClosed.IsCancellationRequested)
        {
            // Closing only detaches the UI wait. Confirmed writes may finish;
            // observe late errors without retaining the window in the worker.
            _ = worker.ContinueWith(static completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
