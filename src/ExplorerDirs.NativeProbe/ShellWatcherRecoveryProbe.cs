using System.IO;

internal static class ShellWatcherRecoveryProbe
{
    internal static int Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-ShellWatcherRecovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using var monitor = new ProbeShellDirectoryMonitor(folder, initialCount: 0);
            var firstGeneration = monitor.WatcherGeneration;
            if (firstGeneration != 1) throw new InvalidOperationException($"Initial watcher generation was {firstGeneration}.");

            monitor.InjectWatcherErrorForProbe(new InternalBufferOverflowException("isolated recovery contract"));
            if (!monitor.ChangeQueueSnapshot.Contains("force=True", StringComparison.Ordinal))
                throw new InvalidOperationException("Watcher overflow did not request an authoritative directory refresh.");
            Directory.Delete(folder, recursive: true);
            if (!SpinWait.SpinUntil(() => monitor.Diagnostics.Contains("recovery-failed", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The probe did not observe the expected failed replacement attempt.");
            Directory.CreateDirectory(folder);
            if (!monitor.WaitForWatcherGeneration(firstGeneration + 1, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The watcher was not enabled after the directory returned and recovery retried.");

            File.WriteAllText(Path.Combine(folder, "after-overflow.txt"), "replacement watcher");
            if (!monitor.WaitForAcceptedEvents(1, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The replacement watcher did not deliver the next filesystem event.");
            if (monitor.WatcherGeneration != firstGeneration + 1)
                throw new InvalidOperationException("Watcher recovery created more than one replacement generation.");

            Console.WriteLine($"shell-watcher-recovery: generation={firstGeneration}->{monitor.WatcherGeneration}; replacement-failed-before-retry=True; overflow-force-refresh=True; replacement-observed-event=True; diagnostics={monitor.Diagnostics}");
            return 0;
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
