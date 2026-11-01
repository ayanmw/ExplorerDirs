using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class RecursiveDirectoryScannerSmoke
{
    internal static void RunIncrementalDirectoryTreePublishProbe()
    {
        const int folderCount = 8;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-IncrementalTreePublish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folderPaths = new string[folderCount];
            for (var index = 0; index < folderCount; index++)
            {
                var folder = Path.Combine(root, $"folder-{index:D2}");
                var nested = Path.Combine(folder, "nested");
                Directory.CreateDirectory(nested);
                File.WriteAllBytes(Path.Combine(folder, "payload.bin"), new byte[index + 3]);
                File.WriteAllBytes(Path.Combine(nested, "deep.bin"), new byte[index + 5]);
                folderPaths[index] = Path.GetFullPath(folder);
            }

            var rootPath = Path.GetFullPath(root);
            var requestedPaths = folderPaths.Append(rootPath).ToArray();
            var requestedFolders = folderPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ownerThread = Environment.CurrentManagedThreadId;
            var published = new ConcurrentQueue<(string Path, ShellListViewRowScanResult Result, int WorkerThread)>();
            var pendingOwnerUpdates = new ConcurrentQueue<(string Path, ShellListViewRowScanResult Result)>();
            IReadOnlyList<ShellListViewRow> ownerRows = folderPaths.Select((path, index) =>
            {
                var identity = ShellListViewRowIdentity.GetFileSystemIdentity(path) ??
                    throw new InvalidOperationException("The incremental row fixture did not produce a filesystem identity.");
                return new ShellListViewRow(index + 1, 0, 0, Path.GetFileName(path), path, path,
                    0x20000000, null, true, index) { FileSystemIdentity = identity };
            }).ToArray();
            using var firstFolderReady = new ManualResetEventSlim();
            using var releaseFirstFolder = new ManualResetEventSlim();
            var firstCallbackEntered = 0;
            var completedTask = Task.Run(() => HandleBoundRecursiveDirectoryScanner.ScanDirectoryTree(
                rootPath, requestedPaths, CancellationToken.None, completedDirectory: (path, result) =>
                {
                    published.Enqueue((path, result, Environment.CurrentManagedThreadId));
                    pendingOwnerUpdates.Enqueue((path, result));
                    if (requestedFolders.Contains(path) && Interlocked.Exchange(ref firstCallbackEntered, 1) == 0)
                    {
                        firstFolderReady.Set();
                        if (!releaseFirstFolder.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("The incremental publish probe did not release the first folder callback.");
                    }
                }));

            if (!firstFolderReady.Wait(TimeSpan.FromSeconds(5)))
            {
                releaseFirstFolder.Set();
                throw new TimeoutException("The handle-bound tree scan did not publish a completed sibling folder.");
            }
            var firstFolderUpdates = published.Where(item => requestedFolders.Contains(item.Path)).ToArray();
            if (completedTask.IsCompleted || firstFolderUpdates.Length == 0)
            {
                releaseFirstFolder.Set();
                throw new InvalidOperationException("A completed folder value was not available on the worker before the full tree scan completed.");
            }
            var firstPublished = firstFolderUpdates[0];
            var firstResult = firstPublished.Result;
            var expectedBytes = (ulong)(new FileInfo(Path.Combine(firstPublished.Path, "payload.bin")).Length +
                new FileInfo(Path.Combine(firstPublished.Path, "nested", "deep.bin")).Length);
            if (firstPublished.WorkerThread == ownerThread || firstResult.Summary.Bytes != expectedBytes)
            {
                releaseFirstFolder.Set();
                throw new InvalidOperationException("A completed folder value was not available on the worker before the full tree scan completed.");
            }

            // The owner applies a queued result while the worker is still paused in this callback.
            if (!pendingOwnerUpdates.TryDequeue(out var firstOwnerUpdate) || firstOwnerUpdate.Path != firstPublished.Path)
            {
                releaseFirstFolder.Set();
                throw new InvalidOperationException("The owner queue did not contain the completed directory row update.");
            }
            ownerRows = ShellListViewRowModel.ApplyRecursiveScanResult(ownerRows, firstOwnerUpdate.Path, firstOwnerUpdate.Result);
            var firstAppliedRow = ownerRows.Single(row => string.Equals(row.FileSystemPath, firstPublished.Path, StringComparison.OrdinalIgnoreCase));
            if (firstAppliedRow.RecursiveBytes != expectedBytes || ownerRows.Count(row => row.RecursiveSummary is not null) != 1)
            {
                releaseFirstFolder.Set();
                throw new InvalidOperationException("Applying one queued directory result changed the wrong Shell rows or failed to update its target row.");
            }
            releaseFirstFolder.Set();
            var finalResults = completedTask.GetAwaiter().GetResult();
            var completions = published.ToArray();
            while (pendingOwnerUpdates.TryDequeue(out var update))
                ownerRows = ShellListViewRowModel.ApplyRecursiveScanResult(ownerRows, update.Path, update.Result);
            if (completions.Length != requestedPaths.Length || completions[^1].Path != rootPath ||
                completions.Any(item => item.Result != finalResults[item.Path]) ||
                ownerRows.Any(row => row.RecursiveSummary is not { } summary ||
                    !finalResults.TryGetValue(row.FileSystemPath!, out var result) || summary != result.Summary))
                throw new InvalidOperationException("Incremental directory callbacks did not reconcile with the final handle-bound scan results.");

            var independentCallbacks = new ConcurrentQueue<(string Path, ShellListViewRowScanResult Result)>();
            var independentResults = HandleBoundRecursiveDirectoryScanner.Scan(folderPaths, CancellationToken.None,
                completedDirectory: (path, result) => independentCallbacks.Enqueue((path, result)));
            var independentCompletions = independentCallbacks.ToArray();
            if (independentCompletions.Length != folderCount ||
                independentCompletions.Any(item => independentResults[item.Path] != item.Result) ||
                !independentResults.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(folderPaths))
                throw new InvalidOperationException("Independent directory scan callbacks did not reconcile with the returned result map.");

            using var cancellation = new CancellationTokenSource();
            var cancelledCallbacks = new ConcurrentQueue<string>();
            var cancelledTask = Task.Run(() => HandleBoundRecursiveDirectoryScanner.ScanDirectoryTree(
                rootPath, requestedPaths, cancellation.Token, completedDirectory: (path, _) =>
                {
                    cancelledCallbacks.Enqueue(path);
                    if (requestedFolders.Contains(path)) cancellation.Cancel();
                }));
            var cancelled = false;
            try { _ = cancelledTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled || cancelledCallbacks.Count != 1 || cancelledCallbacks.Contains(rootPath))
                throw new InvalidOperationException("Cancellation after a partial folder publish did not prevent the root total and remaining callbacks.");

            var missingChildRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs-MissingTreeChild-" + Guid.NewGuid().ToString("N"));
            var missingChildPath = Path.Combine(missingChildRoot, "vanishing-folder");
            var displacedChildPath = missingChildRoot + ".displaced";
            Directory.CreateDirectory(missingChildPath);
            File.WriteAllBytes(Path.Combine(missingChildPath, "payload.bin"), [1, 2, 3]);
            var childMovedBeforeOpen = false;
            ShellListViewRowScanResult? missingChildCallback = null;
            try
            {
                var missingChildResults = HandleBoundRecursiveDirectoryScanner.ScanDirectoryTree(
                    missingChildRoot, [missingChildRoot, missingChildPath], CancellationToken.None,
                    beforeChildOpen: (_, childPath) =>
                    {
                        if (string.Equals(childPath, missingChildPath, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Move(childPath, displacedChildPath);
                            childMovedBeforeOpen = true;
                        }
                    },
                    completedDirectory: (path, result) =>
                    {
                        if (string.Equals(path, missingChildPath, StringComparison.OrdinalIgnoreCase))
                            missingChildCallback = result;
                    });
                if (!childMovedBeforeOpen || !missingChildResults.TryGetValue(missingChildPath, out var missingChild) ||
                    missingChild.Summary.Bytes != 0 || missingChild.Summary.Unreadable != 1 ||
                    missingChildCallback != missingChild || missingChildResults[missingChildRoot].Summary.Unreadable != 1)
                    throw new InvalidOperationException("A requested directory that disappears before opening did not publish an unreadable row result.");
            }
            finally
            {
                if (Directory.Exists(displacedChildPath)) Directory.Move(displacedChildPath, missingChildPath);
                Directory.Delete(missingChildRoot, recursive: true);
            }

            Console.WriteLine($"incremental-directory-tree-publish: requested-folders={folderCount}; " +
                $"first-folder-published-before-root-completion=True; owner-thread={ownerThread}; " +
                $"worker-thread={firstPublished.WorkerThread}; completed-callbacks={completions.Length}; " +
                "owner-row-model-applied-incrementally=True; " +
                $"independent-directory-callbacks={independentCompletions.Length}; " +
                $"root-published-last={completions[^1].Path == rootPath}; cancellation-after-one-folder=True; " +
                "missing-child-publishes-unreadable-row=True; " +
                $"root-published-after-cancel=False; handles-released-after-cancel=True");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static void RunSiblingPercentageStrategyProbe()
    {
        const int folderCount = 64;
        const int filesPerFolder = 16;
        const int repeatedRuns = 5;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-SiblingPercentageStrategy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "root-file.bin"), new byte[13]);
            for (var folderIndex = 0; folderIndex < folderCount; folderIndex++)
            {
                var folder = Path.Combine(root, $"folder-{folderIndex:D3}");
                var nested = Path.Combine(folder, "nested");
                Directory.CreateDirectory(nested);
                for (var fileIndex = 0; fileIndex < filesPerFolder; fileIndex++)
                    File.WriteAllBytes(Path.Combine(folder, $"payload-{fileIndex:D2}.bin"), new byte[32 + fileIndex]);
                File.WriteAllBytes(Path.Combine(nested, "nested.bin"), new byte[folderIndex % 17 + 1]);
            }

            var rootPath = Path.GetFullPath(root);
            var folderPaths = Directory.GetDirectories(rootPath).Select(Path.GetFullPath).ToArray();
            var requestedPaths = folderPaths.Append(rootPath).ToArray();
            var directFileBytes = Directory.GetFiles(rootPath)
                .Select(path => (ulong)new FileInfo(path).Length).Aggregate(0UL, checked((sum, value) => sum + value));

            (ulong TotalBytes, long Entries, IReadOnlyDictionary<string, RecursiveDirectoryScanResult> Folders)
                ScanOneTree()
            {
                long entries = 0;
                var results = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(rootPath, requestedPaths,
                    CancellationToken.None, count => entries = count);
                var total = results[rootPath].Bytes;
                return (total, entries, results);
            }

            (ulong TotalBytes, long Entries, IReadOnlyDictionary<string, RecursiveDirectoryScanResult> Folders)
                ScanEachSibling()
            {
                var total = directFileBytes;
                long entries = 0;
                var folderResults = new Dictionary<string, RecursiveDirectoryScanResult>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in folderPaths)
                {
                    long childEntries = 0;
                    var childResults = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(path, [path],
                        CancellationToken.None, count => childEntries = count);
                    var summary = childResults[Path.GetFullPath(path)];
                    entries = checked(entries + childEntries);
                    total = checked(total + summary.Bytes);
                    folderResults.Add(Path.GetFullPath(path), summary);
                }
                return (total, entries, folderResults);
            }

            var expected = ScanOneTree();
            var siblingExpected = ScanEachSibling();
            if (expected.TotalBytes != siblingExpected.TotalBytes ||
                folderPaths.Any(path => !expected.Folders.TryGetValue(path, out var onePass) ||
                    !siblingExpected.Folders.TryGetValue(path, out var perFolder) || onePass != perFolder))
                throw new InvalidOperationException("Single-tree and per-sibling scans did not produce identical row sizes and directory totals.");

            var onePassSamples = new (long Milliseconds, long AllocatedBytes, long Entries)[repeatedRuns];
            var siblingSamples = new (long Milliseconds, long AllocatedBytes, long Entries)[repeatedRuns];
            for (var index = 0; index < repeatedRuns; index++)
            {
                onePassSamples[index] = Measure(ScanOneTree);
                siblingSamples[index] = Measure(ScanEachSibling);
            }
            Console.WriteLine($"sibling-percentage-scan-strategy: folders={folderCount}; nested-folders={folderCount}; " +
                $"files={folderCount * (filesPerFolder + 1) + 1}; warmup=1; repetitions={repeatedRuns}; " +
                $"same-values=True; direct-file-bytes-reused={directFileBytes}; " +
                $"single-tree={Format(onePassSamples)}; per-sibling={Format(siblingSamples)}; " +
                "scope=warm-local-temp-filesystem; per-sibling-uses-preexisting-shell-row-paths-and-file-sizes=True; product-view-changed=False");

            static (long Milliseconds, long AllocatedBytes, long Entries) Measure(
                Func<(ulong TotalBytes, long Entries, IReadOnlyDictionary<string, RecursiveDirectoryScanResult> Folders)> scan)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                var result = scan();
                timer.Stop();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                return (timer.ElapsedMilliseconds, allocated, result.Entries);
            }

            static string Format((long Milliseconds, long AllocatedBytes, long Entries)[] samples)
            {
                var elapsed = samples.Select(sample => sample.Milliseconds).Order().ToArray();
                var allocations = samples.Select(sample => sample.AllocatedBytes).Order().ToArray();
                var entries = samples.Select(sample => sample.Entries).Distinct().ToArray();
                if (entries.Length != 1) throw new InvalidOperationException("A scan strategy changed its entry count between repetitions.");
                return $"median-ms={elapsed[elapsed.Length / 2]}; min-ms={elapsed[0]}; max-ms={elapsed[^1]}; " +
                    $"median-allocated={allocations[allocations.Length / 2]}; entries={entries[0]}";
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static void RunAllocationProbe()
    {
        const int directoryCount = 5000;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RecursiveScanAllocation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < directoryCount; index++)
                Directory.CreateDirectory(Path.Combine(root, $"folder-{index:D5}"));

            var rootPath = Path.GetFullPath(root);
            var requestedPaths = new[]
            {
                rootPath,
                Path.Combine(rootPath, "folder-00000"),
                Path.Combine(rootPath, "folder-02500"),
                Path.Combine(rootPath, "folder-04999")
            };
            // Warm both paths first so the comparison is not dominated by the first metadata-cache pass.
            _ = RecursiveDirectoryScanner.ScanDirectoryTree(root, CancellationToken.None);
            _ = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(root, requestedPaths, CancellationToken.None);
            var full = Measure(progress => RecursiveDirectoryScanner.ScanDirectoryTree(root, CancellationToken.None, progress), rootPath);
            var selected = Measure(progress => RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                root, requestedPaths, CancellationToken.None, progress), rootPath);
            var fullRetained = MeasureRetained(() => RecursiveDirectoryScanner.ScanDirectoryTree(root, CancellationToken.None));
            var selectedRetained = MeasureRetained(() => RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                root, requestedPaths, CancellationToken.None));
            if (full.ResultCount != directoryCount + 1 || selected.ResultCount != requestedPaths.Length ||
                full.Root.Directories != directoryCount || selected.Root != full.Root)
                throw new InvalidOperationException("The full and requested-path scans returned different root totals or result counts.");

            var saved = full.AllocatedBytes - selected.AllocatedBytes;
            var savedPercent = full.AllocatedBytes == 0 ? 0 : 100d * saved / full.AllocatedBytes;
            var retainedDelta = fullRetained - selectedRetained;
            if (full.PeakLiveBytes - selected.PeakLiveBytes < 500_000)
                throw new InvalidOperationException("The requested-path scan did not reduce sampled peak live heap by at least 500,000 bytes.");
            Console.WriteLine($"recursive-directory-scan-allocation: directories={directoryCount}; " +
                $"full-results={full.ResultCount}; requested-results={selected.ResultCount}; " +
                $"full-allocated={full.AllocatedBytes}; requested-allocated={selected.AllocatedBytes}; " +
                $"delta={saved}; delta-percent={savedPercent:F1}; " +
                $"full-retained-estimate={fullRetained}; requested-retained-estimate={selectedRetained}; " +
                $"retained-delta-estimate={retainedDelta}; " +
                $"full-peak-live-estimate={full.PeakLiveBytes}; requested-peak-live-estimate={selected.PeakLiveBytes}; " +
                $"full-ms={full.ElapsedMilliseconds}; requested-ms={selected.ElapsedMilliseconds}; " +
                "scope=managed-allocations-and-approximate-live-heap-only");
            RunFileEntryAllocationProbe();
        }
        finally { Directory.Delete(root, recursive: true); }

        static void RunFileEntryAllocationProbe()
        {
            const int directoryCount = 120;
            const int filesPerDirectory = 25;
            const long maxAllocatedBytes = 1_175_000;
            var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-FileEntryAllocation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                for (var directoryIndex = 0; directoryIndex < directoryCount; directoryIndex++)
                {
                    var directory = Path.Combine(root, $"folder-{directoryIndex:D3}");
                    Directory.CreateDirectory(directory);
                    for (var fileIndex = 0; fileIndex < filesPerDirectory; fileIndex++)
                        File.WriteAllBytes(Path.Combine(directory, $"payload-{fileIndex:D2}.bin"),
                            new byte[32 + fileIndex]);
                }

                var requestedPaths = Directory.GetDirectories(root);
                _ = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(root, requestedPaths, CancellationToken.None);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var results = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                    root, requestedPaths, CancellationToken.None);
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                const ulong expectedBytesPerDirectory = 1100;
                if (results.Count != directoryCount || results.Values.Any(result =>
                        result.Bytes != expectedBytesPerDirectory || result.Files != filesPerDirectory ||
                        result.Directories != 0 || result.Unreadable != 0))
                    throw new InvalidOperationException("File-entry allocation fixture produced different recursive summaries.");
                if (allocatedBytes >= maxAllocatedBytes)
                    throw new InvalidOperationException(
                        $"File-entry allocation budget exceeded: {allocatedBytes} >= {maxAllocatedBytes}.");
                Console.WriteLine($"file-entry-scan-allocation: folders={results.Count}; allocated={allocatedBytes}; " +
                    $"budget={maxAllocatedBytes}; bytes-per-folder={expectedBytesPerDirectory}; status=PASS");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        static (long AllocatedBytes, int ResultCount, RecursiveDirectoryScanResult Root,
            long ElapsedMilliseconds, long PeakLiveBytes) Measure(
            Func<Action<long>?, IReadOnlyDictionary<string, RecursiveDirectoryScanResult>> scan, string rootPath)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long peakLiveBytes = 0;
            void SampleLiveHeap(long _) => peakLiveBytes = Math.Max(peakLiveBytes, GC.GetTotalMemory(forceFullCollection: true));
            var clock = Stopwatch.StartNew();
            var results = scan(SampleLiveHeap);
            clock.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            return (allocated, results.Count, results[rootPath], clock.ElapsedMilliseconds, peakLiveBytes);
        }

        static long MeasureRetained(Func<IReadOnlyDictionary<string, RecursiveDirectoryScanResult>> scan)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(forceFullCollection: true);
            var results = scan();
            var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
            GC.KeepAlive(results);
            return retained;
        }
    }

    internal static void Run()
    {
        ShellListViewRowPresentationSmoke.Run();
        DirectoryTreeScanCoordinatorSmoke.Run();
        ShellListViewRowModelSmoke.Run();
        var progressThrottle = new RecursiveScanProgressThrottle(TimeSpan.FromMilliseconds(250));
        Check(progressThrottle.ShouldReport(1000), "recursive scan progress reports immediately");
        Check(!progressThrottle.ShouldReport(1000 + Stopwatch.Frequency / 8),
            "recursive scan progress suppresses bursts inside the display interval");
        Check(progressThrottle.ShouldReport(1000 + Stopwatch.Frequency / 4),
            "recursive scan progress resumes when the display interval elapses");

        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RecursiveSize-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "child");
        var grandchild = Path.Combine(child, "grandchild");
        var linkTarget = root + "-link-target";
        var directoryLink = Path.Combine(root, "linked-target");
        var standalone = Path.Combine(root + "-standalone.bin");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(grandchild);
        Directory.CreateDirectory(linkTarget);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "root.bin"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(child, "nested.bin"), [1, 2, 3, 4, 5]);
            File.WriteAllBytes(Path.Combine(grandchild, "deep.bin"), [1, 2]);
            var result = RecursiveDirectoryScanner.Scan(root, CancellationToken.None);
            Check(result.Bytes == 10 && result.Files == 3 && result.Directories == 2 && result.Unreadable == 0,
                "recursive scan totals nested file sizes and counts");
            var treeResults = RecursiveDirectoryScanner.ScanDirectoryTree(root, CancellationToken.None);
            Check(treeResults.TryGetValue(Path.GetFullPath(root), out var treeRoot) && treeRoot == result &&
                  treeResults.TryGetValue(Path.GetFullPath(child), out var treeChild) &&
                  treeChild.Bytes == 7 && treeChild.Files == 2 && treeChild.Directories == 1 && treeChild.Unreadable == 0 &&
                  treeResults.TryGetValue(Path.GetFullPath(grandchild), out var treeGrandchild) &&
                  treeGrandchild.Bytes == 2 && treeGrandchild.Files == 1 && treeGrandchild.Directories == 0,
                "single-pass directory tree scan returns recursive totals for root, child, and grandchild folders");
            var visibleFolderPaths = new[] { Path.GetFullPath(child), Path.GetFullPath(grandchild) };
            var visibleFolderResults = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                root, visibleFolderPaths, CancellationToken.None);
            Check(visibleFolderResults.Count == 2 &&
                  visibleFolderResults[visibleFolderPaths[0]].Bytes == 7 &&
                  visibleFolderResults[visibleFolderPaths[1]].Bytes == 2 &&
                  !visibleFolderResults.ContainsKey(Path.GetFullPath(root)),
                "visible-folder scan returns requested recursive rows without retaining unrequested directory results");

            var longPathRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RecursiveLongPath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(longPathRoot);
            try
            {
                var current = longPathRoot;
                string? firstChild = null;
                const int depth = 28;
                for (var index = 0; index < depth; index++)
                {
                    current = Path.Combine(current, $"depth-{index:D2}-long");
                    firstChild ??= current;
                    Directory.CreateDirectory(current);
                    File.WriteAllBytes(Path.Combine(current, "payload.bin"), [1]);
                }
                var longPathResults = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(
                    longPathRoot, [firstChild!], CancellationToken.None);
                Check(longPathResults.TryGetValue(firstChild!, out var longPathResult) &&
                      longPathResult.Bytes == depth && longPathResult.Files == depth &&
                      longPathResult.Directories == depth - 1 && longPathResult.Unreadable == 0,
                    "file-entry scanning preserves recursive totals beyond the legacy MAX_PATH boundary");
            }
            finally { Directory.Delete(longPathRoot, recursive: true); }

            var hiddenRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RecursiveHidden-" + Guid.NewGuid().ToString("N"));
            var hiddenFile = Path.Combine(hiddenRoot, "hidden.bin");
            var hiddenDirectory = Path.Combine(hiddenRoot, "hidden-dir");
            var hiddenChildFile = Path.Combine(hiddenDirectory, "hidden-child.bin");
            Directory.CreateDirectory(hiddenDirectory);
            try
            {
                File.WriteAllBytes(hiddenFile, new byte[13]);
                File.SetAttributes(hiddenFile, FileAttributes.Hidden | FileAttributes.System);
                File.WriteAllBytes(hiddenChildFile, new byte[17]);
                File.SetAttributes(hiddenDirectory, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
                var hiddenResult = RecursiveDirectoryScanner.ScanDirectoryTree(hiddenRoot, CancellationToken.None)
                    [Path.GetFullPath(hiddenRoot)];
                Check(hiddenResult.Bytes == 30 && hiddenResult.Files == 2 && hiddenResult.Directories == 1 &&
                      hiddenResult.Unreadable == 0,
                    "file-entry scanning includes hidden and system entries like Explorer's full folder enumeration");
            }
            finally
            {
                if (File.Exists(hiddenFile)) File.SetAttributes(hiddenFile, FileAttributes.Normal);
                if (File.Exists(hiddenChildFile)) File.SetAttributes(hiddenChildFile, FileAttributes.Normal);
                if (Directory.Exists(hiddenDirectory)) File.SetAttributes(hiddenDirectory, FileAttributes.Directory);
                if (Directory.Exists(hiddenRoot)) Directory.Delete(hiddenRoot, recursive: true);
            }

            using var canceledPathSelection = new CancellationTokenSource();
            canceledPathSelection.Cancel();
            var requestedPathsEnumerated = false;
            try
            {
                _ = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(root, EnumerateRequestedPaths(),
                    canceledPathSelection.Token);
                throw new Exception("a canceled visible-folder scan enumerated its requested paths");
            }
            catch (OperationCanceledException) { }
            Check(!requestedPathsEnumerated,
                "visible-folder scan honors cancellation before materializing requested paths");

            using var cancellationDuringPathSelection = new CancellationTokenSource();
            var requestedPathCount = 0;
            try
            {
                _ = RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(root, EnumerateThenCancel(),
                    cancellationDuringPathSelection.Token);
                throw new Exception("a visible-folder scan continued after path selection was canceled");
            }
            catch (OperationCanceledException) { }
            Check(requestedPathCount == 2,
                "visible-folder scan stops materializing paths as soon as cancellation is requested");

            IEnumerable<string> EnumerateRequestedPaths()
            {
                requestedPathsEnumerated = true;
                yield return child;
            }

            IEnumerable<string> EnumerateThenCancel()
            {
                requestedPathCount++;
                yield return child;
                cancellationDuringPathSelection.Cancel();
                requestedPathCount++;
                yield return grandchild;
                requestedPathCount++;
                yield return root;
            }

            Check(RecursiveSizePercentage.Calculate(10, 14) == 100m * 10 / 14 &&
                  RecursiveSizePercentage.Calculate(4, 14) == 100m * 4 / 14 &&
                  RecursiveSizePercentage.Calculate(10, 12) == 100m * 10 / 12 &&
                  RecursiveSizePercentage.Calculate(2, 12) == 100m * 2 / 12,
                "relative size percentages use the current directory total and recalculate after a sibling changes");
            Check(RecursiveSizePercentage.Calculate(0, 0) is null,
                "relative size percentage is unavailable when the current directory has no bytes");

            File.WriteAllBytes(standalone, new byte[7]);
            var selectionResult = RecursiveDirectoryScanner.ScanSelection(
                [root, child, root, standalone], CancellationToken.None);
            Check(selectionResult.Bytes == 17 && selectionResult.Files == 4 && selectionResult.Directories == 2 &&
                  selectionResult.Unreadable == 0,
                "recursive selection scan counts files and folders while deduplicating nested and repeated selections");

            var empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(empty);
            var emptyResult = RecursiveDirectoryScanner.Scan(empty, CancellationToken.None);
            Check(emptyResult.Bytes == 0 && emptyResult.Files == 0 && emptyResult.Directories == 0,
                "recursive scan handles an empty directory");

            var progressDirectory = Path.Combine(root, "progress");
            Directory.CreateDirectory(progressDirectory);
            for (var index = 0; index < 600; index++)
                File.WriteAllBytes(Path.Combine(progressDirectory, $"item-{index:D3}.bin"), []);
            var progressReports = new List<long>();
            var progressResult = RecursiveDirectoryScanner.Scan(progressDirectory, CancellationToken.None,
                progressReports.Add);
            Check(progressResult.Files == 600 && progressReports.SequenceEqual([256L, 512L, 600L]),
                "recursive scan reports monotonic checked-entry progress in batches and on completion");
            var treeProgressReports = new List<long>();
            var progressTreeResults = RecursiveDirectoryScanner.ScanDirectoryTree(progressDirectory,
                CancellationToken.None, treeProgressReports.Add);
            Check(progressTreeResults[Path.GetFullPath(progressDirectory)].Files == 600 &&
                  treeProgressReports.SequenceEqual([256L, 512L, 600L]),
                "single-pass directory-tree entry scanning preserves batched progress callbacks");

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                RecursiveDirectoryScanner.Scan(root, canceled.Token);
                throw new Exception("a canceled recursive scan completed successfully");
            }
            catch (OperationCanceledException) { }

            var cancellationRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RecursiveTreeCancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cancellationRoot);
            try
            {
                for (var index = 0; index < 300; index++)
                    Directory.CreateDirectory(Path.Combine(cancellationRoot, $"child-{index:D3}"));
                using var duringScanCancellation = new CancellationTokenSource();
                var canceledDuringEnumeration = false;
                try
                {
                    RecursiveDirectoryScanner.ScanDirectoryTree(cancellationRoot, duringScanCancellation.Token,
                        _ => duringScanCancellation.Cancel());
                }
                catch (OperationCanceledException) { canceledDuringEnumeration = true; }
                Check(canceledDuringEnumeration,
                    "directory tree scan observes cancellation during enumeration");
            }
            finally { Directory.Delete(cancellationRoot, recursive: true); }
            Check(!Directory.Exists(cancellationRoot),
                "canceled directory tree scan releases active enumerators for fixture cleanup");

            Check(!RecursiveDirectoryScanner.ShouldDescend(FileAttributes.Directory | FileAttributes.ReparsePoint) &&
                  RecursiveDirectoryScanner.ShouldDescend(FileAttributes.Directory),
                "recursive scan skips reparse-point directories");

            File.WriteAllBytes(Path.Combine(linkTarget, "outside.bin"), new byte[11]);
            var junction = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            junction.ArgumentList.Add("/c");
            junction.ArgumentList.Add("mklink");
            junction.ArgumentList.Add("/J");
            junction.ArgumentList.Add(directoryLink);
            junction.ArgumentList.Add(linkTarget);
            using var junctionProcess = System.Diagnostics.Process.Start(junction);
            if (junctionProcess is null)
                throw new InvalidOperationException("Could not start the isolated junction fixture command.");
            junctionProcess.WaitForExit();
            if (junctionProcess.ExitCode != 0)
            {
                Console.WriteLine("Reparse-point traversal smoke: SKIPPED (the isolated junction fixture could not be created)");
                return;
            }
            Check((File.GetAttributes(directoryLink) & FileAttributes.ReparsePoint) != 0,
                "the isolated junction fixture is a real filesystem reparse point");
            var selectedLinkResult = RecursiveDirectoryScanner.ScanSelection([directoryLink], CancellationToken.None);
            Check(selectedLinkResult.Bytes == 0 && selectedLinkResult.Files == 0 &&
                  selectedLinkResult.Directories == 0 && selectedLinkResult.Unreadable == 1,
                "recursive selection scan skips a selected reparse point without counting its target");
            var selectedLinkChildResult = RecursiveDirectoryScanner.ScanSelection(
                [Path.Combine(directoryLink, "outside.bin")], CancellationToken.None);
            Check(selectedLinkChildResult.Bytes == 0 && selectedLinkChildResult.Files == 0 &&
                  selectedLinkChildResult.Directories == 0 && selectedLinkChildResult.Unreadable == 1,
                "recursive selection scan does not follow a selected path through a reparse-point ancestor");
            var linkedResult = RecursiveDirectoryScanner.Scan(root, CancellationToken.None);
            Check(linkedResult.Bytes == 10 && linkedResult.Files == 603 && linkedResult.Directories == 4 &&
                  linkedResult.Unreadable == 1,
                $"recursive scan skips a real directory reparse point without counting its target contents " +
                $"(attributes={File.GetAttributes(directoryLink)}, result={linkedResult})");
            var linkedTreeResults = RecursiveDirectoryScanner.ScanDirectoryTree(root, CancellationToken.None);
            Check(linkedTreeResults[Path.GetFullPath(root)] == linkedResult &&
                  !linkedTreeResults.ContainsKey(Path.GetFullPath(directoryLink)),
                "single-pass directory tree scan matches reparse-point totals without descending into the link");
            var handleLinkedTreeResults = HandleBoundRecursiveDirectoryScanner.ScanDirectoryTree(
                root, [root, directoryLink], CancellationToken.None);
            Check(handleLinkedTreeResults.Count == 2 &&
                  handleLinkedTreeResults[Path.GetFullPath(root)].Summary == linkedResult &&
                  handleLinkedTreeResults[Path.GetFullPath(directoryLink)].Summary ==
                  new RecursiveDirectoryScanResult(0, 0, 0, 1),
                "handle-bound row scan reports reparse points without traversing their targets");
            var handleReparseRootResult = HandleBoundRecursiveDirectoryScanner.ScanDirectoryTree(
                directoryLink, [directoryLink], CancellationToken.None);
            Check(handleReparseRootResult[Path.GetFullPath(directoryLink)].Summary ==
                  new RecursiveDirectoryScanResult(0, 0, 0, 1),
                "handle-bound scan reports a reparse point selected as the traversal root without following it");
        }
        finally
        {
            if (Directory.Exists(directoryLink)) Directory.Delete(directoryLink);
            if (File.Exists(standalone)) File.Delete(standalone);
            Directory.Delete(root, recursive: true);
            Directory.Delete(linkTarget, recursive: true);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
