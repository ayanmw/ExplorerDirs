namespace ExplorerDirs.App;

internal static class ShellRefreshCompletion
{
    internal static void Run(Action complete, Func<bool> requestIsCurrent, Action? continuation,
        Action<Exception> reportFailure)
    {
        try { complete(); }
        catch (Exception error)
        {
            ReportFailureIfCurrent(error, requestIsCurrent, reportFailure);
        }

        if (!requestIsCurrent()) return;
        try { continuation?.Invoke(); }
        catch (Exception error)
        {
            ReportFailureIfCurrent(error, requestIsCurrent, reportFailure);
        }
    }

    private static void ReportFailureIfCurrent(Exception error, Func<bool> requestIsCurrent,
        Action<Exception> reportFailure)
    {
        if (!requestIsCurrent()) return;
        try { reportFailure(error); }
        catch { /* A status observer must not prevent refresh cleanup or continuation. */ }
    }
}
