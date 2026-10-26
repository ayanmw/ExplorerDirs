namespace ExplorerDirs.App;

// Keeps a watcher active while the Shell view settles, then backs off polling
// without abandoning the monitor on slow directory enumerations.
internal sealed class ShellDirectoryMonitorStartup
{
    private const int StableSamplesRequired = 5;
    private const int FastSamples = 40;
    private int? _lastCount;
    private int _stableSamples;
    private int _sampleCount;

    internal int NextIntervalMilliseconds => _sampleCount < FastSamples ? 250 : 1000;

    internal static bool TryReadCount(Func<int?> read, out int? count, Action<Exception> reportFailure)
    {
        try
        {
            count = read();
            return true;
        }
        catch (Exception error)
        {
            count = null;
            reportFailure(error);
            return false;
        }
    }

    internal int? Observe(int? count)
    {
        if (_sampleCount < FastSamples) _sampleCount++;
        if (count is null)
        {
            _lastCount = null;
            _stableSamples = 0;
            return null;
        }

        if (_lastCount == count) _stableSamples++;
        else
        {
            _lastCount = count;
            _stableSamples = 1;
        }

        return _stableSamples >= StableSamplesRequired ? count : null;
    }
}
