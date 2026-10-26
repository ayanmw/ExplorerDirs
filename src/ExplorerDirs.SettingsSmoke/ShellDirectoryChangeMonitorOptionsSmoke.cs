using System.IO;
using System.Threading;
using ExplorerDirs.App;

internal static class ShellDirectoryChangeMonitorOptionsSmoke
{
    internal static void Run()
    {
        var required = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
            NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime;
        Check((ShellDirectoryChangeMonitorOptions.NotifyFilter & required) == required,
            "the directory watcher observes entry and file metadata changes");

        Check(ShellDirectoryChangeMonitorOptions.ForCreated() == new ShellDirectoryChangeDelta(1, false),
            "create changes the expected item count without forcing an unnecessary refresh");
        Check(ShellDirectoryChangeMonitorOptions.ForDeleted() == new ShellDirectoryChangeDelta(-1, false),
            "delete changes the expected item count without forcing an unnecessary refresh");
        Check(ShellDirectoryChangeMonitorOptions.ForChanged() == new ShellDirectoryChangeDelta(0, false),
            "content and metadata changes use per-item notification without changing the item count");
        Check(ShellDirectoryChangeMonitorOptions.ForRenamed() == new ShellDirectoryChangeDelta(0, true),
            "rename forces a refresh without changing the item count");
        Check(ShellDirectoryChangeMonitorOptions.ForError() == new ShellDirectoryChangeDelta(0, true),
            "watcher errors force a reconciliation refresh without changing the item count");

        var metadataBatch = new ShellDirectoryChangeBatch(2);
        var metadataChange = ShellDirectoryChangeMonitorOptions.ForChanged();
        metadataBatch.RecordChanges(metadataChange.Delta, metadataChange.ForceRefresh);
        Check(!metadataBatch.ShouldDeferRefresh(actualCount: 2, recheckPending: false) &&
              !metadataBatch.Complete(actualCount: 2),
            "a metadata-only change does not trigger the whole-view fallback when the count is unchanged");

        VerifyFileWriteProducesChangedEvent();
        VerifyBalancedCreateDeleteEventsRequestRefresh();
    }

    private static void VerifyFileWriteProducesChangedEvent()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"explorer-dirs-watcher-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "sample.txt");
            File.WriteAllText(path, "before");
            using var changed = new ManualResetEventSlim();
            using var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = ShellDirectoryChangeMonitorOptions.NotifyFilter
            };
            watcher.Changed += (_, eventArgs) =>
            {
                if (string.Equals(eventArgs.Name, "sample.txt", StringComparison.OrdinalIgnoreCase))
                    changed.Set();
            };
            watcher.EnableRaisingEvents = true;

            File.AppendAllText(path, "-after");
            Check(changed.Wait(TimeSpan.FromSeconds(5)),
                "the configured Windows watcher reports a content-size change to an existing file");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void VerifyBalancedCreateDeleteEventsRequestRefresh()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"explorer-dirs-replacement-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "old.txt"), "old");
            var queue = new ShellDirectoryChangeQueue();
            var batch = new ShellDirectoryChangeBatch(initialCount: 1);
            using var deleted = new ManualResetEventSlim();
            using var created = new ManualResetEventSlim();
            using var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = ShellDirectoryChangeMonitorOptions.NotifyFilter
            };
            watcher.Deleted += (_, eventArgs) =>
            {
                if (!string.Equals(eventArgs.Name, "old.txt", StringComparison.OrdinalIgnoreCase)) return;
                var change = ShellDirectoryChangeMonitorOptions.ForDeleted();
                queue.Enqueue(change.Delta, change.ForceRefresh);
                deleted.Set();
            };
            watcher.Created += (_, eventArgs) =>
            {
                if (!string.Equals(eventArgs.Name, "new.txt", StringComparison.OrdinalIgnoreCase)) return;
                var change = ShellDirectoryChangeMonitorOptions.ForCreated();
                queue.Enqueue(change.Delta, change.ForceRefresh);
                created.Set();
            };
            watcher.EnableRaisingEvents = true;

            File.Delete(Path.Combine(folder, "old.txt"));
            Check(deleted.Wait(TimeSpan.FromSeconds(5)),
                "the live watcher reports deletion before a replacement is created");
            var deleteHandoff = queue.DrainWhileDispatchPending();
            batch.RecordChanges(deleteHandoff.Delta, deleteHandoff.ForceRefresh);

            File.WriteAllText(Path.Combine(folder, "new.txt"), "new");
            Check(created.Wait(TimeSpan.FromSeconds(5)),
                "the live watcher reports creation in the same coalesced refresh window");
            var createHandoff = queue.Drain();
            batch.RecordChanges(createHandoff.Delta, createHandoff.ForceRefresh);
            Check(batch.ExpectedCountAfterChanges == 1 && batch.Complete(actualCount: 1),
                "a real equal-count delete/create replacement requests a Shell reconciliation refresh");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception($"Shell directory change monitor: {description}");
        Console.WriteLine($"PASS {description}");
    }
}
