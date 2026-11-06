namespace ExplorerDirs.NativeProbe;

internal static class ProjectedWatcherLedgerProbe
{
    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-Ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var ledger = new ProjectedChangeLedger();
        Exception? watcherFailure = null;
        using var signal = new AutoResetEvent(false);
        using var watcher = new FileSystemWatcher(fixture)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false
        };
        void Record(ProjectedChangeKind kind, string name, string? destination = null)
        {
            ledger.Record(kind, name, destination);
            signal.Set();
        }
        watcher.Created += (_, e) => Record(ProjectedChangeKind.Created, e.Name!);
        watcher.Changed += (_, e) => Record(ProjectedChangeKind.Changed, e.Name!);
        watcher.Deleted += (_, e) => Record(ProjectedChangeKind.Deleted, e.Name!);
        watcher.Renamed += (_, e) => Record(ProjectedChangeKind.Renamed, e.OldName!, e.Name);
        watcher.Error += (_, e) => { Interlocked.Exchange(ref watcherFailure, e.GetException()); signal.Set(); };
        long lastSequence = 0;
        var received = 0;
        void WaitFor(ProjectedChangeKind kind, string name, string? destination = null)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (Volatile.Read(ref watcherFailure) is { } error) throw new InvalidOperationException("Watcher failed.", error);
                while (ledger.TryTake(out var work))
                {
                    if (work.Sequence <= lastSequence) throw new InvalidOperationException("Watcher event sequence regressed.");
                    lastSequence = work.Sequence;
                    received++;
                    var match = work.Kind == kind && work.Name == name && work.NewName == destination;
                    // This stage verifies ingress, not native dispatch. Retain stale
                    // events for reconciliation rather than assuming they are redundant.
                    ledger.Release(work);
                    if (match) return;
                }
                signal.WaitOne(20);
            }
            throw new TimeoutException($"Missing watcher event: {kind} {name} -> {destination}.");
        }
        try
        {
            watcher.EnableRaisingEvents = true;
            var source = Path.Combine(fixture, "source.bin");
            var renamed = Path.Combine(fixture, "renamed.bin");
            File.WriteAllBytes(source, new byte[16]);
            WaitFor(ProjectedChangeKind.Created, "source.bin");
            File.Move(source, renamed);
            WaitFor(ProjectedChangeKind.Renamed, "source.bin", "renamed.bin");
            File.Delete(renamed);
            WaitFor(ProjectedChangeKind.Deleted, "renamed.bin");
            File.WriteAllBytes(renamed, new byte[32]);
            WaitFor(ProjectedChangeKind.Created, "renamed.bin");
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            WaitFor(ProjectedChangeKind.Created, "child");
            Directory.Delete(Path.Combine(fixture, "child"));
            WaitFor(ProjectedChangeKind.Deleted, "child");
            var expected = Enumerable.Range(0, 128).Select(index => $"burst-{index:D3}.bin").ToHashSet(StringComparer.Ordinal);
            foreach (var name in expected) File.WriteAllBytes(Path.Combine(fixture, name), [1]);
            var burstTimeout = System.Diagnostics.Stopwatch.StartNew();
            while (expected.Count > 0 && burstTimeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (Volatile.Read(ref watcherFailure) is { } error) throw new InvalidOperationException("Watcher burst failed.", error);
                while (ledger.TryTake(out var work))
                {
                    if (work.Sequence <= lastSequence) throw new InvalidOperationException("Burst event sequence regressed.");
                    lastSequence = work.Sequence;
                    received++;
                    if (work.Kind == ProjectedChangeKind.Created) expected.Remove(work.Name);
                    ledger.Release(work);
                }
                if (expected.Count > 0) signal.WaitOne(20);
            }
            if (expected.Count > 0) throw new TimeoutException($"Watcher burst lost {expected.Count} created paths.");
            ledger.Invalidate();
            // A callback already in flight at retirement must not re-open the queue.
            Record(ProjectedChangeKind.Changed, "renamed.bin");
            if (ledger.Snapshot != (0, 0, true) || ledger.TryTake(out _))
                throw new InvalidOperationException("Retired watcher visit accepted a late event.");
            Console.WriteLine($"Projected watcher ledger: PASS real file create/rename/delete/recreate; directory create/delete; 128-file burst; FIFO; retirement; received={received}; no windows.");
            return 0;
        }
        finally
        {
            ledger.Invalidate();
            // Stop and dispose before disposing the callback signal or deleting the fixture.
            watcher.Dispose();
            Directory.Delete(fixture, recursive: true);
        }
    }
}
