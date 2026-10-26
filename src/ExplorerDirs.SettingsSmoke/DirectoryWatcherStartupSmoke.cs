using ExplorerDirs.App;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class DirectoryWatcherStartupSmoke
{
    internal static async Task RunAsync()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var current = true;
        var published = 0;
        var activated = 0;
        var disposed = 0;
        var disposalThread = 0;
        using var createStarted = new ManualResetEventSlim();
        using var allowCreateToFinish = new ManualResetEventSlim();

        var staleStart = ShellDirectoryWatcherStartup.StartAsync(
            () =>
            {
                createStarted.Set();
                allowCreateToFinish.Wait(TimeSpan.FromSeconds(5));
                Check(Environment.CurrentManagedThreadId != callerThread,
                    "watcher construction runs away from the navigation caller");
                return new object();
            },
            _ => activated++,
            () => current,
            _ => published++,
            _ => published--,
            _ =>
            {
                disposalThread = Environment.CurrentManagedThreadId;
                disposed++;
            });

        Check(createStarted.Wait(TimeSpan.FromSeconds(2)), "background watcher construction begins promptly");
        Check(!staleStart.IsCompleted, "a slow watcher constructor does not block its caller");
        current = false;
        allowCreateToFinish.Set();
        Check(!await staleStart, "a watcher created for a retired navigation is not adopted");
        Check(published == 0 && activated == 0 && disposed == 1,
            "a stale watcher is disposed without activation or publication");
        Check(disposalThread != callerThread,
            "stale watcher disposal also runs away from the navigation caller");

        var activationCallerThread = 0;
        var activationThread = 0;
        var validStart = await Task.Factory.StartNew(() =>
        {
            // Keep the initiating thread alive while Task.Run activates the watcher;
            // otherwise the thread pool may reuse its ID and make a correct background
            // activation look synchronous in this smoke.
            activationCallerThread = Environment.CurrentManagedThreadId;
            return ShellDirectoryWatcherStartup.StartAsync(
                () => new object(),
                _ =>
                {
                    activationThread = Environment.CurrentManagedThreadId;
                    activated++;
                },
                () => true,
                _ => published++,
                _ => published--,
                _ => disposed++).GetAwaiter().GetResult();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Check(validStart && activationThread != activationCallerThread,
            "watcher activation also runs away from the navigation caller");
        Check(published == 1 && activated == 1 && disposed == 1,
            "a current watcher is published before background activation");

        var failedPublished = 0;
        var failedDisposed = 0;
        var failedStart = await ShellDirectoryWatcherStartup.StartAsync(
            () => new object(),
            _ => throw new IOException("simulated watcher activation failure"),
            () => true,
            _ => failedPublished++,
            _ => failedPublished--,
            _ => failedDisposed++);
        Check(!failedStart && failedPublished == 0 && failedDisposed == 1,
            "failed watcher activation unpublishes and disposes the candidate");

        var currentAfterActivation = true;
        var latePublished = 0;
        var lateDisposed = 0;
        var retiredDuringActivation = await ShellDirectoryWatcherStartup.StartAsync(
            () => new object(),
            _ => currentAfterActivation = false,
            () => currentAfterActivation,
            _ => latePublished++,
            _ => latePublished--,
            _ => lateDisposed++);
        Check(!retiredDuringActivation && latePublished == 0 && lateDisposed == 1,
            "a monitor retired during watcher activation unpublishes and disposes the stale candidate");

        var partialPublication = 0;
        var publicationFailureDisposed = 0;
        var publicationFailure = await ShellDirectoryWatcherStartup.StartAsync(
            () => new object(), _ => { }, () => true,
            _ => { partialPublication++; throw new IOException("publish failed after partial state change"); },
            _ => partialPublication--,
            _ => publicationFailureDisposed++);
        Check(!publicationFailure && partialPublication == 0 && publicationFailureDisposed == 1,
            "a failed watcher publication is rolled back and its candidate is disposed");

        var cleanupFailure = await ShellDirectoryWatcherStartup.StartAsync(
            () => new object(), _ => throw new IOException("activation failed"), () => true,
            _ => { }, _ => throw new IOException("unpublish failed"),
            _ => throw new IOException("disposal failed"));
        Check(!cleanupFailure,
            "watcher cleanup exceptions do not escape asynchronous startup");

        var completionFailureCount = 0;
        Check(!ShellDirectoryWatcherStartup.TryNotifyCompleted(
                _ => throw new InvalidOperationException("completion callback failed"), true,
                error => completionFailureCount += error is InvalidOperationException ? 1 : 0) &&
              completionFailureCount == 1,
            "a watcher startup completion callback failure is reported without escaping async void");
        Check(!ShellDirectoryWatcherStartup.TryNotifyCompleted(
                _ => throw new InvalidOperationException("completion callback failed"), true,
                _ => throw new IOException("failure reporter failed")),
            "a watcher completion failure reporter cannot rethrow through async void");

        var disposeCompleted = false;
        await ShellDirectoryWatcherStartup.DisposeSafelyAsync(() => throw new IOException("dispose failed"));
        disposeCompleted = true;
        Check(disposeCompleted, "an asynchronous watcher disposal failure is observed and contained");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception($"Directory watcher startup: {description}");
        Console.WriteLine($"PASS {description}");
    }
}
