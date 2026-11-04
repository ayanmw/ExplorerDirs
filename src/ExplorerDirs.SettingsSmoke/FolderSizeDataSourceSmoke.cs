using System.IO;
using ExplorerDirs.App;

internal static class FolderSizeDataSourceSmoke
{
    internal static async Task Run()
    {
        var root = @"C:\source-fixture";
        var empty = root + @"\empty"; var missing = root + @"\missing";
        var scans = 0; var queries = 0;
        Task<IndexedSizePage> Query(string path, uint offset, CancellationToken token)
        {
            queries++; return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available,
                [new(empty, true, 0)], 1, offset));
        }
        RecursiveDirectoryScanResult Scan(string path, CancellationToken token)
        { Interlocked.Increment(ref scans); return new(37, 1, 0, 0); }
        var resolver = new FolderSizeDataSource(Query, Scan);
        var only = await resolver.ResolveAsync(root, [empty, missing], FolderSizeSource.Everything, CancellationToken.None);
        if (scans != 0 || only[empty].Bytes != 0 || only[missing].Bytes is not null)
            throw new Exception("Everything-only scanned or confused zero and unavailable.");
        var automatic = await resolver.ResolveAsync(root, [empty, missing], FolderSizeSource.Auto, CancellationToken.None);
        if (scans != 1 || automatic[empty].Source != FolderSizeSource.Everything || automatic[missing].Bytes != 37)
            throw new Exception("Automatic mode did not restrict fallback scans to missing values.");
        var queryCount = queries;
        await resolver.ResolveAsync(root, [missing], FolderSizeSource.BuiltIn, CancellationToken.None);
        if (queries != queryCount || scans != 2) throw new Exception("Built-in source contacted Everything.");
        var unavailable = new FolderSizeDataSource((_, _, _) => Task.FromResult(new IndexedSizePage(EverythingSizeStatus.IndexUnavailable, [])), Scan);
        var fallback = await unavailable.ResolveAsync(root, [missing], FolderSizeSource.Auto, CancellationToken.None);
        if (fallback[missing].Bytes != 37) throw new Exception("Missing Everything index did not fall back.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await resolver.ResolveAsync(root, [missing], FolderSizeSource.Auto, cancellation.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        await CheckRunningCancellation(root);
        await CheckPagedResults(root);
        await CheckQueryFailures(root);

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "folder-size-options-" + Guid.NewGuid().ToString("N") + ".ini");
        var store = new FolderSizeOptionsStore(settingsPath);
        if (store.Load() != new FolderSizeOptions()) throw new Exception("Defaults changed.");
        store.Save(new(false, FolderSizeSource.Everything));
        if (new FolderSizeOptionsStore(settingsPath).Load() != new FolderSizeOptions(false, FolderSizeSource.Everything))
            throw new Exception("Size options did not persist.");
        File.AppendAllText(settingsPath, "; external edit\n");
        try { store.Save(new()); throw new Exception("Settings conflict overwritten."); } catch (IOException) { }
        File.WriteAllText(settingsPath, "[FolderSize]\nFormatVersion=2\nEnabled=True\nSource=Auto\n");
        store.Load();
        try { store.Save(new()); throw new Exception("Future settings overwritten."); } catch (IOException) { }
        Console.WriteLine("Folder size data source smoke: PASS (source selection, zero/unknown, restricted fallback, cancellation, portable options and write protection; no IPC/UI)");
    }

    private static async Task CheckQueryFailures(string root)
    {
        var empty = root + @"\empty"; var missing = root + @"\missing";
        foreach (var partial in new[] { false, true })
        foreach (var failure in new[] { 1, 2, 3, 4 })
        foreach (var source in new[] { FolderSizeSource.Everything, FolderSizeSource.Auto })
        {
            using var cancellation = new CancellationTokenSource();
            var scans = 0; var progress = new CountProgress();
            var resolver = new FolderSizeDataSource((_, offset, _) =>
            {
                if (partial && offset == 0) return Task.FromResult(new IndexedSizePage(
                    EverythingSizeStatus.Available, [new(empty, true, 0)], 2));
                if (failure == 1) throw new IOException("Injected Everything setup failure.");
                if (failure == 2) return Task.FromException<IndexedSizePage>(new IOException("Injected Everything task failure."));
                cancellation.Cancel();
                if (failure == 3) return Task.FromCanceled<IndexedSizePage>(cancellation.Token);
                throw new IOException("Injected query error racing cancellation.");
            }, (_, _) => { Interlocked.Increment(ref scans); return new(37, 1, 0, 0); });
            var task = resolver.ResolveAsync(root, [empty, missing], source, cancellation.Token, progress);
            if (failure >= 3)
            {
                await ExpectCancelled(task);
                if (scans != 0 || progress.Count != (partial ? 1 : 0))
                    throw new Exception("A cancelled failing query started fallback or published retired results.");
                continue;
            }
            var values = await task;
            var expectedScans = source == FolderSizeSource.Auto ? (partial ? 1 : 2) : 0;
            if (values.Count != 2 || scans != expectedScans || progress.Count != 2 ||
                partial && (values[empty].Bytes != 0 || values[empty].Source != FolderSizeSource.Everything))
                throw new Exception("A failed Everything page lost prior values or scanned already-indexed directories.");
            if (source == FolderSizeSource.Auto && (values[missing].Bytes != 37 || values[missing].Source != FolderSizeSource.BuiltIn) ||
                source == FolderSizeSource.Everything && (values[missing].Bytes is not null ||
                    values[missing].Detail?.Contains("Injected", StringComparison.Ordinal) != true))
                throw new Exception("Query exceptions did not preserve explicit source/fallback/unknown semantics.");
        }
    }

    private static async Task CheckRunningCancellation(string root)
    {
        using var started = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        using var waitingCancellation = new CancellationTokenSource();
        var active = 0;
        var progress = new CountProgress();
        RecursiveDirectoryScanResult BlockingScan(string path, CancellationToken token)
        {
            Interlocked.Increment(ref active);
            try
            {
                started.Signal();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Scan fixture was not released.");
                token.ThrowIfCancellationRequested();
                return new(1, 1, 0, 0);
            }
            finally { Interlocked.Decrement(ref active); }
        }
        var resolver = new FolderSizeDataSource(scan: BlockingScan);
        var running = resolver.ResolveAsync(root, [root + @"\a", root + @"\b", root + @"\c"],
            FolderSizeSource.BuiltIn, cancellation.Token, progress);
        Task<IReadOnlyDictionary<string, FolderSizeValue>>? waiting = null;
        try
        {
            if (!await Task.Run(() => started.Wait(TimeSpan.FromSeconds(3))))
                throw new Exception("Two bounded scan workers did not start.");
            cancellation.Cancel();
            waiting = resolver.ResolveAsync(root, [root + @"\waiting"], FolderSizeSource.BuiltIn, waitingCancellation.Token);
            if (await Task.WhenAny(waiting, Task.Delay(80)) == waiting || Volatile.Read(ref active) != 2 || running.IsCompleted)
                throw new Exception("Cancelling a wait released scan slots before the underlying scans exited.");
            waitingCancellation.Cancel();
            await ExpectCancelled(waiting);
        }
        finally
        {
            cancellation.Cancel(); waitingCancellation.Cancel(); release.Set();
            await ExpectCancelled(running);
            if (waiting is not null) await ExpectCancelled(waiting);
        }
        if (active != 0 || progress.Count != 0) throw new Exception("Retired scans retained workers or published progress.");
        var fresh = new FolderSizeDataSource(scan: (_, _) => new(7, 1, 0, 0));
        var values = await fresh.ResolveAsync(root, [root + @"\fresh"], FolderSizeSource.BuiltIn, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3));
        if (values[root + @"\fresh"].Bytes != 7) throw new Exception("Cancelled scans leaked shared scan slots.");
    }

    private static async Task ExpectCancelled(Task task)
    {
        try { await task; throw new Exception("Running scan cancellation was ignored."); }
        catch (OperationCanceledException) { }
    }

    private static async Task CheckPagedResults(string root)
    {
        var rows = Enumerable.Range(0, EverythingSizeClient.PageSize * 2 + 1)
            .Select(index => new IndexedSizeItem(root + $@"\folder-{index:D5}", true,
                index == EverythingSizeClient.PageSize ? null : (ulong)index)).ToArray();
        var offsets = new List<uint>(); var scans = 0;
        var resolver = new FolderSizeDataSource((_, offset, _) =>
        {
            offsets.Add(offset);
            return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available,
                rows.Skip((int)offset).Take(EverythingSizeClient.PageSize).ToArray(), (uint)rows.Length, offset));
        }, (_, _) => { scans++; return new(37, 1, 0, 0); });
        var values = await resolver.ResolveAsync(root, rows.Select(row => row.Path), FolderSizeSource.Everything, CancellationToken.None);
        if (!offsets.SequenceEqual(new uint[] { 0, 2048, 4096 }) || scans != 0 || values.Count != rows.Length ||
            values[rows[0].Path].Bytes != 0 || values[rows[^1].Path].Bytes != 4096 || values[rows[2048].Path].Bytes is not null)
            throw new Exception("Paged Everything results lost offsets, unknown/zero or last-page values.");
        offsets.Clear();
        values = await resolver.ResolveAsync(root, rows.Select(row => row.Path), FolderSizeSource.Auto, CancellationToken.None);
        if (scans != 1 || values[rows[2048].Path].Bytes != 37 || offsets.Count != 3)
            throw new Exception("Paged automatic resolution scanned already indexed directories.");

        using var cancelled = new CancellationTokenSource();
        var progress = new CountProgress();
        var retiring = new FolderSizeDataSource((_, _, _) =>
        {
            cancelled.Cancel();
            return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available, rows.Take(2048).ToArray(), (uint)rows.Length));
        });
        await ExpectCancelled(retiring.ResolveAsync(root, rows.Select(row => row.Path), FolderSizeSource.Everything, cancelled.Token, progress));
        if (progress.Count != 0) throw new Exception("A retired Everything page published progress after cancellation.");
    }

    private sealed class CountProgress : IProgress<KeyValuePair<string, FolderSizeValue>>
    {
        internal int Count;
        public void Report(KeyValuePair<string, FolderSizeValue> value) => Interlocked.Increment(ref Count);
    }
}
