namespace ExplorerDirs.App;

internal sealed record DirectoryProbeResult(string Path, bool Exists, int Generation);

internal sealed class DirectoryNavigationGate
{
    private int _generation;

    public Task<DirectoryProbeResult> ProbeAsync(string path, Func<string, bool> exists)
    {
        var generation = Interlocked.Increment(ref _generation);
        return Task.Run(() =>
        {
            bool available;
            try { available = exists(path); }
            catch (Exception) { available = false; }
            return new DirectoryProbeResult(path, available, generation);
        });
    }

    public bool IsCurrent(DirectoryProbeResult result) =>
        result.Generation == Volatile.Read(ref _generation);

    public void Cancel() => Interlocked.Increment(ref _generation);
}
