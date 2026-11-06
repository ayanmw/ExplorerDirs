using System.Diagnostics;
using ExplorerDirs.NativeProbe;

public static partial class SyntheticShellSortProbe
{
    private static void VerifyProjectedWatcherView(nint bridge, nint list, string fixture,
        Func<string[]> names, Action assertSelection)
    {
        var queue = new ProjectedChangeLedger();
        Exception? failure = null;
        using var watcher = new FileSystemWatcher(fixture)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
        };
        watcher.Created += (_, e) => queue.Record(ProjectedChangeKind.Created, e.Name!);
        watcher.Changed += (_, e) => queue.Record(ProjectedChangeKind.Changed, e.Name!);
        watcher.Deleted += (_, e) => queue.Record(ProjectedChangeKind.Deleted, e.Name!);
        watcher.Renamed += (_, e) => queue.Record(ProjectedChangeKind.Renamed, e.OldName!, e.Name);
        watcher.Error += (_, e) => Interlocked.Exchange(ref failure, e.GetException());
        var arrive = At<RemoveProjectedChild>(bridge, 8);
        var remove = At<NotifyProjectedRemoval>(bridge, 4);
        var rename = At<RenameProjectedChild>(bridge, 9);
        var acknowledge = At<AcknowledgeProjectedRemoval>(bridge, 7);
        var processed = 0;
        var stage = 0;
        var enumerationCount = At<ReadProjectedEnumerations>(bridge, 5);

        void PumpUntil(Func<bool> condition)
        {
            var elapsed = Stopwatch.StartNew();
            do
            {
                System.Windows.Forms.Application.DoEvents();
                if (condition()) return;
                Thread.Sleep(10);
            } while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
            throw new TimeoutException("Watcher-driven native notification did not converge.");
        }

        void Settle(Func<bool> condition, int timeoutSeconds = 5)
        {
            var elapsed = Stopwatch.StartNew();
            var quiet = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                if (Volatile.Read(ref failure) is { } error) throw new InvalidOperationException("Native view watcher failed.", error);
                while (queue.TryTake(out var work))
                {
                    quiet.Restart();
                    int result;
                    ulong ticket = 0;
                    switch (work.Kind)
                    {
                        case ProjectedChangeKind.Created:
                        case ProjectedChangeKind.Changed:
                            result = arrive(bridge, work.Name);
                            break;
                        case ProjectedChangeKind.Deleted:
                            result = remove(bridge, work.Name, out ticket);
                            break;
                        default:
                            result = rename(bridge, work.Name, work.NewName!, out ticket);
                            break;
                    }
                    if (result != 0) throw new InvalidOperationException($"Watcher dispatch {work.Kind} {work.Name}: 0x{result:X8}.");
                    if (work.Kind == ProjectedChangeKind.Created)
                    {
                        // Without a delivery barrier, the 32-file burst provokes a
                        // parent re-enumeration. A message pump alone did not prevent it.
                        // This prototype confirms each arrival before sending the next;
                        // production needs asynchronous, bounded confirmation batches.
                        PumpUntil(() => names().Contains(work.Name));
                    }
                    if (work.Kind is ProjectedChangeKind.Deleted or ProjectedChangeKind.Renamed)
                    {
                        PumpUntil(() => !names().Contains(work.Name) &&
                            (work.NewName is null || names().Contains(work.NewName)));
                        // This phase intentionally waits between mutations. A superseded
                        // structural event needs reconciliation, not a guessed acknowledgement.
                        if (!queue.IsCurrent(work)) throw new InvalidOperationException("Watcher structural work superseded during confirmation.");
                        if (acknowledge(bridge, work.Name, ticket) != 0)
                            throw new InvalidOperationException("Watcher structural confirmation rejected.");
                    }
                    queue.Release(work);
                    processed++;
                }
                System.Windows.Forms.Application.DoEvents();
                if (condition() && quiet.ElapsedMilliseconds >= 150)
                {
                    assertSelection();
                    Check(enumerationCount(bridge, out var enumerations));
                    Console.WriteLine($"projected-watcher-stage: stage={++stage}; dispatched={processed}; enumerations={enumerations}; elapsed-ms={elapsed.ElapsedMilliseconds}");
                    return;
                }
                Thread.Sleep(10);
            }
            throw new TimeoutException("Watcher-driven view did not settle.");
        }

        try
        {
            watcher.EnableRaisingEvents = true;
            var initial = names().Length;
            File.WriteAllBytes(Path.Combine(fixture, "watcher-file.bin"), new byte[2048]);
            Settle(() => names().Length == initial + 1 && names().Contains("watcher-file.bin"));
            File.WriteAllBytes(Path.Combine(fixture, "watcher-file.bin"), new byte[8192]);
            Settle(() =>
            {
                var rows = names();
                var index = Array.IndexOf(rows, "watcher-file.bin");
                return index >= 0 && ReadColumnTextsForRows(list, 1, rows.Length)[index]
                    .Replace(" ", "").Replace("\u00A0", "").Equals("8KB", StringComparison.OrdinalIgnoreCase);
            });
            File.Move(Path.Combine(fixture, "watcher-file.bin"), Path.Combine(fixture, "watcher-renamed.bin"));
            Settle(() => names().Contains("watcher-renamed.bin") && !names().Contains("watcher-file.bin"));
            File.Delete(Path.Combine(fixture, "watcher-renamed.bin"));
            Settle(() => names().Length == initial && !names().Contains("watcher-renamed.bin"));
            File.WriteAllBytes(Path.Combine(fixture, "watcher-renamed.bin"), new byte[1024]);
            Settle(() => names().Length == initial + 1 && names().Contains("watcher-renamed.bin"));
            File.Delete(Path.Combine(fixture, "watcher-renamed.bin"));
            Settle(() => names().Length == initial && !names().Contains("watcher-renamed.bin"));
            Directory.CreateDirectory(Path.Combine(fixture, "watcher-folder"));
            Settle(() => names().Length == initial + 1 && names().Contains("watcher-folder"));
            Directory.Move(Path.Combine(fixture, "watcher-folder"), Path.Combine(fixture, "watcher-renamed-folder"));
            Settle(() => names().Contains("watcher-renamed-folder") && !names().Contains("watcher-folder"));
            Directory.Delete(Path.Combine(fixture, "watcher-renamed-folder"));
            Settle(() => names().Length == initial && !names().Contains("watcher-renamed-folder"));
            var burstNames = Enumerable.Range(0, 32).Select(index => $"watcher-burst-{index:D3}.bin").ToArray();
            foreach (var name in burstNames) File.WriteAllBytes(Path.Combine(fixture, name), [1]);
            // Per-item native confirmation is deliberately serialized in this prototype.
            Settle(() => names().Length == initial + burstNames.Length && burstNames.All(names().Contains), timeoutSeconds: 30);
            foreach (var name in burstNames) File.Delete(Path.Combine(fixture, name));
            Settle(() => names().Length == initial && !names().Any(name => name.StartsWith("watcher-burst-", StringComparison.Ordinal)), timeoutSeconds: 30);
            if (queue.Snapshot != (0, 0, false)) throw new InvalidOperationException("Watcher dispatch retained completed work.");
            Console.WriteLine($"projected-watcher-view: PASS real watcher file/folder create/rename/delete; same-name recreation; 32-file burst create/delete; exact 8KB update; selection/focus retained; dispatched={processed}");
        }
        finally
        {
            queue.Invalidate();
            watcher.Dispose();
        }
    }
}
