namespace ExplorerDirs.App;

internal static class ShellDirectoryWatcherStartup
{
    internal static async Task DisposeSafelyAsync(Action dispose)
    {
        try { await Task.Run(dispose); }
        catch (Exception) { /* A best-effort cleanup task must not become unobserved. */ }
    }

    internal static bool TryNotifyCompleted(Action<bool> completed, bool started, Action<Exception> reportFailure)
    {
        try
        {
            completed(started);
            return true;
        }
        catch (Exception error)
        {
            try { reportFailure(error); }
            catch (Exception) { /* A failure observer must not escape the async completion boundary. */ }
            return false;
        }
    }

    internal static async Task<bool> StartAsync<T>(Func<T> create, Action<T> activate,
        Func<bool> isCurrent, Action<T> publish, Action<T> unpublish, Action<T> dispose)
    {
        T candidate;
        try { candidate = await Task.Run(create); }
        catch (Exception) { return false; }

        var publicationAttempted = false;
        try
        {
            if (!isCurrent())
            {
                await DisposeCandidate();
                return false;
            }

            // publish may change one shared reference before a later operation throws,
            // so rollback is required even when publication did not return normally.
            publicationAttempted = true;
            publish(candidate);
            await Task.Run(() => activate(candidate));
            if (isCurrent()) return true;
        }
        catch (Exception)
        {
            // A failed provider callback means the candidate was not adopted.
        }

        if (publicationAttempted)
        {
            try { unpublish(candidate); }
            catch (Exception) { /* Disposal must still run after partial unpublication. */ }
        }
        await DisposeCandidate();
        return false;

        async Task DisposeCandidate()
        {
            try { await Task.Run(() => dispose(candidate)); }
            catch (Exception) { /* Startup failure is reported as false; cleanup cannot escape async void callers. */ }
        }
    }
}
