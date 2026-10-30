using ExplorerDirs.App;
using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class RecursiveDirectoryScannerSmoke
{
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
        }
        finally { Directory.Delete(root, recursive: true); }

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
