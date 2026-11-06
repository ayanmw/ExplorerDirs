using System.Diagnostics;
using ExplorerDirs.NativeProbe;

public static partial class SyntheticShellSortProbe
{
    private static void VerifyProjectedWatcherView(nint bridge, nint list, string fixture,
        Func<string[]> names, Action assertSelection)
    {
        var queue = new ProjectedChangeLedger();
        using var process = Process.GetCurrentProcess();
        bool ContainsRow(string name) => OwnedAppListText.Find(process.Handle, (uint)process.Id, list, name) >= 0;
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

        void Settle(Func<bool> condition, int timeoutSeconds = 5)
        {
            const int maximumInFlight = 8;
            var elapsed = Stopwatch.StartNew();
            var quiet = Stopwatch.StartNew();
            var dispatcher = new ProjectedChangeDispatch(queue, maximumInFlight);
            while (elapsed.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                if (Volatile.Read(ref failure) is { } error) throw new InvalidOperationException("Native view watcher failed.", error);
                dispatcher.Advance(() => !queue.Snapshot.Invalidated,
                    work =>
                    {
                        quiet.Restart();
                        ulong ticket = 0;
                        var result = work.Kind switch
                        {
                            ProjectedChangeKind.Created or ProjectedChangeKind.Changed => arrive(bridge, work.Name),
                            ProjectedChangeKind.Deleted => remove(bridge, work.Name, out ticket),
                            _ => rename(bridge, work.Name, work.NewName!, out ticket)
                        };
                        if (result != 0) throw new InvalidOperationException($"Watcher dispatch {work.Kind} {work.Name}: 0x{result:X8}.");
                        processed++;
                        return ticket;
                    },
                    work => work.Kind switch
                    {
                        ProjectedChangeKind.Created => ContainsRow(work.Name),
                        ProjectedChangeKind.Deleted => !ContainsRow(work.Name),
                        ProjectedChangeKind.Renamed => !ContainsRow(work.Name) && ContainsRow(work.NewName!),
                        _ => true
                    },
                    (work, ticket) =>
                    {
                        if (acknowledge(bridge, work.Name, ticket) != 0)
                            throw new InvalidOperationException("Watcher structural confirmation rejected.");
                    });
                System.Windows.Forms.Application.DoEvents();
                if (dispatcher.PendingCount == 0 && condition() && quiet.ElapsedMilliseconds >= 150)
                {
                    assertSelection();
                    Check(enumerationCount(bridge, out var enumerations));
                    Console.WriteLine($"projected-watcher-stage: stage={++stage}; dispatched={processed}; enumerations={enumerations}; elapsed-ms={elapsed.ElapsedMilliseconds}; maximum-in-flight={maximumInFlight}");
                    return;
                }
                Thread.Sleep(10);
            }
            throw new TimeoutException($"Watcher-driven view did not settle: pending={dispatcher.PendingCount}; queued={queue.Snapshot.Queued}.");
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
            // Keep a bounded overall budget while observing native confirmation latency.
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
