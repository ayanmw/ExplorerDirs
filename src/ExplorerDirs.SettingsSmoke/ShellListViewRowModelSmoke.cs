using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;
using System.IO;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellListViewRowModelSmoke
{
    internal static void Run()
    {
        using var fixture = new TemporaryDirectory();
        var targetPath = Path.Combine(fixture.Root, "small");
        Directory.CreateDirectory(targetPath);
        // The model only scans SFGAO_FOLDER rows; keep this fixture on that production branch.
        var target = new ExplorerDirs.NativeProbe.ShellListViewRow(
            7, 0x101, 0x201, "small", targetPath, targetPath, 0x20000000, 4,
            FromShellEnumeration: true, SystemImageIndex: 12)
        {
            FileSystemIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(targetPath)
        };
        var siblingPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs-row-model", "sibling"));
        var sibling = target with
        {
            Id = 8,
            AbsolutePidl = 0x102,
            RelativePidl = 0x202,
            DisplayName = "sibling",
            ParsingName = siblingPath,
            FileSystemPath = siblingPath,
            RecursiveBytes = 9,
            SystemImageIndex = 13
        };
        var virtualRow = target with
        {
            Id = 9,
            AbsolutePidl = 0x103,
            RelativePidl = 0x203,
            DisplayName = "virtual folder",
            ParsingName = "::{00000000-0000-0000-0000-000000000001}",
            FileSystemPath = null,
            RecursiveBytes = null,
            SystemImageIndex = 14
        };
        var rows = new[] { target, sibling, virtualRow };
        var scan = new Dictionary<string, RecursiveDirectoryScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [targetPath] = new(20, 3, 1, 0)
        };

        var updated = ExplorerDirs.NativeProbe.ShellListViewRowModel.ApplyRecursiveScanResults(rows,
            new Dictionary<string, ShellListViewRowScanResult>(StringComparer.OrdinalIgnoreCase)
            {
                [targetPath] = new(target.FileSystemIdentity!.Value, scan[targetPath])
            });

        Check(updated.Count == rows.Length && updated.Select(row => row.Id).SequenceEqual(rows.Select(row => row.Id)),
            "a scan updates row data without changing native Shell row identity or order");
        Check(updated[0].RecursiveBytes == 20 && updated[0].RecursiveSummary == scan[targetPath] &&
              updated[0].AbsolutePidl == target.AbsolutePidl && updated[0].RelativePidl == target.RelativePidl &&
              updated[0].SystemImageIndex == target.SystemImageIndex,
            "filesystem scan values update while PIDLs and system icon identity remain stable");
        // A completed scan omitting a filesystem folder must drop stale size data; virtual rows remain untouched.
        Check(updated[1].RecursiveBytes is null && updated[1].RecursiveSummary is null &&
              updated[1].FileSystemIdentity is null && updated[1].Id == sibling.Id &&
              updated[1].RelativePidl == sibling.RelativePidl && updated[2] == virtualRow,
            "missing filesystem results clear stale values while virtual Shell rows retain their state");
        Check(target.RecursiveBytes == 4 && target.RecursiveSummary is null,
            "publishing a scan snapshot does not mutate the previous row snapshot");

        var changedTarget = target with
        {
            AbsolutePidl = 0x301,
            RelativePidl = 0x401,
            DisplayName = "small (updated)",
            RecursiveBytes = null,
            RecursiveSummary = null
        };
        var added = target with
        {
            Id = 0,
            AbsolutePidl = 0x303,
            RelativePidl = 0x403,
            DisplayName = "added",
            ParsingName = Path.Combine(Path.GetDirectoryName(targetPath)!, "added"),
            FileSystemPath = Path.Combine(Path.GetDirectoryName(targetPath)!, "added"),
            RecursiveBytes = null
        };
        var oldRemoved = virtualRow;
        var updatedSibling = sibling with { AbsolutePidl = 0x302, RelativePidl = 0x402, DisplayName = "sibling updated" };
        var targetWithSummary = target with { RecursiveSummary = scan[targetPath] };
        var reconciled = ExplorerDirs.NativeProbe.ShellListViewRowModel.Reconcile(
            [targetWithSummary, sibling, oldRemoved], [updatedSibling, changedTarget, added], nextId: 10);

        Check(reconciled.Rows.Select(row => row.Id).SequenceEqual([sibling.Id, target.Id, 10L]) &&
              reconciled.Rows[0].DisplayName == updatedSibling.DisplayName &&
              reconciled.Rows[1].DisplayName == changedTarget.DisplayName &&
              reconciled.Rows[1].AbsolutePidl == changedTarget.AbsolutePidl &&
              reconciled.NextId == 11,
            "re-enumeration preserves IDs through reordering, refreshes Shell data, and allocates new IDs");
        Check(reconciled.RetiredRows.Select(row => row.AbsolutePidl).ToHashSet()
                  .SetEquals([target.AbsolutePidl, sibling.AbsolutePidl, oldRemoved.AbsolutePidl]) &&
              reconciled.Rows[1].RecursiveBytes == targetWithSummary.RecursiveBytes &&
              reconciled.Rows[1].RecursiveSummary == targetWithSummary.RecursiveSummary,
            "all old PIDLs are returned for release and matching scan summaries survive metadata refresh");

        var replacement = changedTarget with { FileSystemIdentity = new(1, 99, 0) };
        var replacedFolder = ExplorerDirs.NativeProbe.ShellListViewRowModel.Reconcile(
            [targetWithSummary], [replacement], nextId: 10);
        Check(replacedFolder.Rows[0].Id == target.Id &&
              replacedFolder.Rows[0].RecursiveBytes is null && replacedFolder.Rows[0].RecursiveSummary is null,
            "a same-path replacement folder does not inherit the retired folder's recursive-size snapshot");

        var identityRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs-row-file-id-" + Guid.NewGuid().ToString("N"));
        var identityPath = Path.Combine(identityRoot, "same-path");
        var retiredPath = Path.Combine(identityRoot, "retired");
        try
        {
            Directory.CreateDirectory(identityPath);
            File.WriteAllText(Path.Combine(identityPath, "payload.txt"), "old");
            var originalIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(identityPath);
            Directory.Move(identityPath, retiredPath);
            Directory.CreateDirectory(identityPath);
            File.WriteAllText(Path.Combine(identityPath, "payload.txt"), "new");
            var replacementIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(identityPath);
            Check(originalIdentity is not null && replacementIdentity is not null && originalIdentity != replacementIdentity,
                "filesystem identity distinguishes a recreated directory at the same path");

            var retiredFolder = targetWithSummary with
            {
                ParsingName = identityPath,
                FileSystemPath = identityPath,
                FileSystemIdentity = originalIdentity
            };
            var replacementFolder = retiredFolder with
            {
                FileSystemIdentity = replacementIdentity,
                RecursiveBytes = null,
                RecursiveSummary = null
            };
            var staleScan = ExplorerDirs.NativeProbe.ShellListViewRowModel.ApplyRecursiveScanResults(
                [retiredFolder],
                new Dictionary<string, ShellListViewRowScanResult>(StringComparer.OrdinalIgnoreCase)
                {
                    [identityPath] = new(originalIdentity!.Value, new(777, 1, 0, 0))
                });
            Check(staleScan[0].RecursiveBytes is null && staleScan[0].RecursiveSummary is null &&
                  staleScan[0].FileSystemIdentity == replacementIdentity,
                "a scan result for a retired row is rejected after its path now identifies another folder");

            var unverifiedFolder = retiredFolder with { FileSystemIdentity = null };
            var unverifiedReplacementScan = ExplorerDirs.NativeProbe.ShellListViewRowModel.ApplyRecursiveScanResults(
                [unverifiedFolder],
                new Dictionary<string, ShellListViewRowScanResult>(StringComparer.OrdinalIgnoreCase)
                {
                    [identityPath] = new(originalIdentity!.Value, new(666, 1, 0, 0))
                });
            Check(unverifiedReplacementScan[0].RecursiveBytes is null &&
                  unverifiedReplacementScan[0].RecursiveSummary is null &&
                  unverifiedReplacementScan[0].FileSystemIdentity == replacementIdentity,
                "a path-only result is rejected when the row identity was unavailable at enumeration time");

            var missingIdentityPath = Path.Combine(identityRoot, "missing-after-scan");
            var movedAwayPath = Path.Combine(identityRoot, "moved-away");
            Directory.CreateDirectory(missingIdentityPath);
            var missingIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(missingIdentityPath);
            var missingFolder = targetWithSummary with
            {
                ParsingName = missingIdentityPath,
                FileSystemPath = missingIdentityPath,
                FileSystemIdentity = missingIdentity
            };
            Directory.Move(missingIdentityPath, movedAwayPath);
            var unverifiedScan = ExplorerDirs.NativeProbe.ShellListViewRowModel.ApplyRecursiveScanResults(
                [missingFolder],
                new Dictionary<string, ShellListViewRowScanResult>(StringComparer.OrdinalIgnoreCase)
                {
                    [missingIdentityPath] = new(missingIdentity!.Value, new(888, 1, 0, 0))
                });
            Check(missingIdentity is not null && unverifiedScan[0].RecursiveBytes is null &&
                  unverifiedScan[0].RecursiveSummary is null && unverifiedScan[0].FileSystemIdentity is null,
                "a path whose previous folder identity is no longer readable cannot accept a late scan result");

            var handleBoundPath = Path.Combine(identityRoot, "handle-bound");
            var handleBoundRetiredPath = Path.Combine(identityRoot, "handle-bound-retired");
            Directory.CreateDirectory(handleBoundPath);
            File.WriteAllBytes(Path.Combine(handleBoundPath, "original.bin"), new byte[6]);
            var handleBoundIdentity = ShellListViewRowIdentity.GetFileSystemIdentity(handleBoundPath);
            var handleBoundScan = HandleBoundRecursiveDirectoryScanner.Scan(
                [handleBoundPath], CancellationToken.None,
                (openedPath, openedIdentity) =>
                {
                    Check(openedPath == handleBoundPath && openedIdentity == handleBoundIdentity,
                        "the test replacement runs after the original directory handle is opened");
                    Directory.Move(handleBoundPath, handleBoundRetiredPath);
                    Directory.CreateDirectory(handleBoundPath);
                    File.WriteAllBytes(Path.Combine(handleBoundPath, "replacement.bin"), new byte[10]);
                });
            var boundResult = handleBoundScan[handleBoundPath];
            var handleBoundRow = target with
            {
                ParsingName = handleBoundPath,
                FileSystemPath = handleBoundPath,
                FileSystemIdentity = handleBoundIdentity,
                RecursiveBytes = null,
                RecursiveSummary = null
            };
            var handleBoundApplied = ExplorerDirs.NativeProbe.ShellListViewRowModel.ApplyRecursiveScanResults(
                [handleBoundRow], handleBoundScan);
            Check(handleBoundIdentity is not null && boundResult.Identity == handleBoundIdentity &&
                  boundResult.Summary.Bytes == 6 && handleBoundApplied[0].RecursiveBytes is null &&
                  handleBoundApplied[0].FileSystemIdentity != handleBoundIdentity,
                "handle enumeration retains the original object's size and row publication rejects the replacement path");

            var nestedScanRoot = Path.Combine(identityRoot, "nested-scan-root");
            var nestedScanChild = Path.Combine(nestedScanRoot, "child");
            Directory.CreateDirectory(nestedScanChild);
            File.WriteAllBytes(Path.Combine(nestedScanRoot, "root.bin"), new byte[2]);
            File.WriteAllBytes(Path.Combine(nestedScanChild, "child.bin"), new byte[6]);
            var nestedScan = HandleBoundRecursiveDirectoryScanner.Scan([nestedScanRoot], CancellationToken.None)
                [nestedScanRoot].Summary;
            Check(nestedScan.Bytes == 8 && nestedScan.Files == 2 && nestedScan.Directories == 1 &&
                  nestedScan.Unreadable == 0,
                "handle enumeration reads ordinary nested directories and combines their summaries");

            var childRaceRoot = Path.Combine(identityRoot, "child-race-root");
            var childRacePath = Path.Combine(childRaceRoot, "child");
            var childRaceRetiredPath = Path.Combine(childRaceRoot, "child-retired");
            Directory.CreateDirectory(childRacePath);
            File.WriteAllBytes(Path.Combine(childRacePath, "original.bin"), new byte[6]);
            var childReplacementApplied = false;
            var childRaceScan = HandleBoundRecursiveDirectoryScanner.Scan(
                [childRaceRoot], CancellationToken.None,
                beforeChildOpen: (parentPath, childPath) =>
                {
                    if (!childReplacementApplied && Path.GetFileName(parentPath) == "child-race-root" &&
                        Path.GetFileName(childPath) == "child")
                    {
                        Directory.Move(childRacePath, childRaceRetiredPath);
                        Directory.CreateDirectory(childRacePath);
                        File.WriteAllBytes(Path.Combine(childRacePath, "replacement.bin"), new byte[10]);
                        childReplacementApplied = true;
                    }
                });
            Check(childReplacementApplied && childRaceScan[childRaceRoot].Summary.Bytes == 0 &&
                  childRaceScan[childRaceRoot].Summary.Files == 0 &&
                  childRaceScan[childRaceRoot].Summary.Unreadable == 1,
                "a child replaced after parent enumeration is rejected when its opened ID differs from the enumerated ID");

            var identityReconciled = ExplorerDirs.NativeProbe.ShellListViewRowModel.Reconcile(
                [retiredFolder], [replacementFolder], nextId: 10);
            Check(identityReconciled.Rows[0].RecursiveBytes is null && identityReconciled.Rows[0].RecursiveSummary is null,
                "same-path Shell row reconciliation clears values when Windows file identity changed");
        }
        finally
        {
            if (Directory.Exists(identityRoot)) Directory.Delete(identityRoot, recursive: true);
        }

        var selection = ExplorerDirs.NativeProbe.ShellListViewSelectionSnapshot.Capture(
            rows, [target.Id, sibling.Id], focusedRowId: sibling.Id);
        var reorderedSelection = selection.Resolve([added, sibling, changedTarget]);
        Check(reorderedSelection.Select(state => (state.RowId, state.Selected, state.Focused))
                  .SequenceEqual([(added.Id, false, false), (sibling.Id, true, true), (target.Id, true, false)]),
            "selection and focus resolve by stable row ID after insertion, removal, and reordering");
        var retiredFocusSelection = selection.Resolve([added, changedTarget]);
        Check(retiredFocusSelection.All(state => !state.Focused) &&
              retiredFocusSelection.Single(state => state.RowId == target.Id).Selected,
            "a missing focused identity is cleared without changing selection on surviving rows");
        Console.WriteLine("Shell ListView row model smoke: PASS");

        static void Check(bool condition, string behavior)
        {
            if (!condition) throw new InvalidOperationException("Shell ListView row model smoke failed: " + behavior);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ExplorerDirs-row-model-" + Guid.NewGuid().ToString("N"));

        internal TemporaryDirectory() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
