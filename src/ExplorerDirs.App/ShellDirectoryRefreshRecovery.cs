using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ExplorerDirs.App;

internal sealed class ShellDirectoryRefreshRecovery
{
    private readonly object _gate = new();
    private int _failedAttempts;

    internal static bool TryInvoke(Action refresh, Action<Exception> onFailure)
    {
        try
        {
            refresh();
            return true;
        }
        catch (Exception error)
        {
            onFailure(error);
            return false;
        }
    }

    internal static bool TryRunMonitorTick(Action tick, Action<Exception> recover)
    {
        try
        {
            tick();
            return true;
        }
        catch (Exception error)
        {
            try { recover(error); }
            catch (Exception recoveryError)
            {
                Trace.TraceError($"Explorer Dirs: directory monitor recovery failed; hr=0x{recoveryError.HResult:X8}");
            }
            return false;
        }
    }

    internal static async Task RetryAfterRecoveryFailureAsync(TimeSpan delay, Action retry,
        Action<Exception> onFailure)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        try { retry(); }
        catch (Exception error)
        {
            try { onFailure(error); }
            catch (Exception observerError)
            {
                Trace.TraceError($"Explorer Dirs: monitor retry observer failed; hr=0x{observerError.HResult:X8}");
            }
        }
    }

    internal static COMException? FailureForHResult(int hresult, string operation) => hresult < 0
        ? new COMException($"{operation} failed with HRESULT 0x{hresult:X8}.", hresult)
        : null;

    internal TimeSpan RecordFailure()
    {
        lock (_gate)
        {
            var shift = Math.Min(_failedAttempts, 6);
            var milliseconds = Math.Min(500L << shift, 30000);
            _failedAttempts = Math.Min(_failedAttempts + 1, 6);
            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }

    internal void RecordSuccess()
    {
        lock (_gate) _failedAttempts = 0;
    }
}
