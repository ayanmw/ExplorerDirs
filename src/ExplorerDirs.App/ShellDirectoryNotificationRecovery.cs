namespace ExplorerDirs.App;

internal static class ShellDirectoryNotificationRecovery
{
    internal static bool TryRunTimerTick(Action tick, Action<Exception> onFailure)
    {
        try
        {
            tick();
            return true;
        }
        catch (Exception error)
        {
            try { onFailure(error); }
            catch (Exception) { /* A failed fallback must not escape a Dispatcher timer callback. */ }
            return false;
        }
    }
}
