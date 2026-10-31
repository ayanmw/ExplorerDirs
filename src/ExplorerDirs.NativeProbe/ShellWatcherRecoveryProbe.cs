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
            if (!monitor.WaitForWatcherGeneration(firstGeneration + 1, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The replacement watcher was not enabled after the recovery backoff.");

            File.WriteAllText(Path.Combine(folder, "after-overflow.txt"), "replacement watcher");
            if (!monitor.WaitForAcceptedEvents(1, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The replacement watcher did not deliver the next filesystem event.");
            if (monitor.WatcherGeneration != firstGeneration + 1)
                throw new InvalidOperationException("Watcher recovery created more than one replacement generation.");

            Console.WriteLine($"shell-watcher-recovery: generation={firstGeneration}->{monitor.WatcherGeneration}; overflow-force-refresh=True; replacement-observed-event=True; diagnostics={monitor.Diagnostics}");
            return 0;
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
