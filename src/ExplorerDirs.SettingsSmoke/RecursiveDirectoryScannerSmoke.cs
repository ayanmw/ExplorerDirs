using ExplorerDirs.App;
using System.Diagnostics;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class RecursiveDirectoryScannerSmoke
{
    internal static void Run()
    {
        var progressThrottle = new RecursiveScanProgressThrottle(TimeSpan.FromMilliseconds(250));
        Check(progressThrottle.ShouldReport(1000), "recursive scan progress reports immediately");
        Check(!progressThrottle.ShouldReport(1000 + Stopwatch.Frequency / 8),
            "recursive scan progress suppresses bursts inside the display interval");
        Check(progressThrottle.ShouldReport(1000 + Stopwatch.Frequency / 4),
            "recursive scan progress resumes when the display interval elapses");

        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RecursiveSize-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "child");
        var linkTarget = root + "-link-target";
        var directoryLink = Path.Combine(root, "linked-target");
        var standalone = Path.Combine(root + "-standalone.bin");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(linkTarget);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "root.bin"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(child, "nested.bin"), [1, 2, 3, 4, 5]);
            var result = RecursiveDirectoryScanner.Scan(root, CancellationToken.None);
            Check(result.Bytes == 8 && result.Files == 2 && result.Directories == 1 && result.Unreadable == 0,
                "recursive scan totals nested file sizes and counts");

            File.WriteAllBytes(standalone, new byte[7]);
            var selectionResult = RecursiveDirectoryScanner.ScanSelection(
                [root, child, root, standalone], CancellationToken.None);
            Check(selectionResult.Bytes == 15 && selectionResult.Files == 3 && selectionResult.Directories == 1 &&
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
            Check(linkedResult.Bytes == 8 && linkedResult.Files == 602 && linkedResult.Directories == 3 &&
                  linkedResult.Unreadable == 1,
                $"recursive scan skips a real directory reparse point without counting its target contents " +
                $"(attributes={File.GetAttributes(directoryLink)}, result={linkedResult})");
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
