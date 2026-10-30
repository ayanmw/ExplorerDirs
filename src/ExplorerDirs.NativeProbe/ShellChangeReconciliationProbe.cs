using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Hidden-window contract for Shell change notifications and row snapshot reconciliation.
internal static class ShellChangeReconciliationProbe
{
    private const int ChangeMessage = 0x8000 + 0x321;
    private const uint WatchEvents = 0x00000001 | 0x00000002 | 0x00000004 | 0x00000008 |
                                    0x00000200 | 0x00001000 | 0x00002000;
    private const uint ShellAndInterruptSources = 0x00000003 | 0x00008000;
    private const uint PathUnicode = 0x00000005;
    private const uint Created = 0x00000002;
    private const uint Deleted = 0x00000004;
    private const uint RenamedItem = 0x00000001;
    private const uint UpdatedDirectory = 0x00001000;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-shell-change-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "stable.txt"), "stable");
        var sizeDirectory = Path.Combine(fixture, "size-folder");
        Directory.CreateDirectory(sizeDirectory);
        var sizePayloadPath = Path.Combine(sizeDirectory, "payload.bin");
        File.WriteAllBytes(sizePayloadPath, new byte[4]);
        nint folderPidl = 0;
        nint shellFolder = 0;
        uint registration = 0;
        ShellListViewEnumeration? current = null;
        using var scanCoordinator = new DirectoryTreeScanCoordinator();
        using var window = new ChangeWindow();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out shellFolder));
            current = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            var initialScans = ScanLatestPaths(scanCoordinator, fixture, [sizeDirectory], out var initialScanWorker,
                out var initialScanPublishedOnOwner);
            current = current with
            {
                Rows = ShellListViewRowModel.ApplyRecursiveScanResults(current.Rows, initialScans).ToList()
            };
            var stableId = current.Rows.Single(row => row.DisplayName == "stable.txt").Id;
            var sizeFolderId = current.Rows.Single(row => row.DisplayName == "size-folder").Id;
            var initialRecursiveBytes = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveBytes;
            var initialRecursiveSummary = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveSummary!;
            var selection = ShellListViewSelectionSnapshot.Capture(current.Rows, [stableId], focusedRowId: stableId);
            var watch = new ShellChangeEntry { Pidl = folderPidl, Recursive = true };
            registration = SHChangeNotifyRegister(window.Handle, ShellAndInterruptSources, WatchEvents,
                ChangeMessage, 1, ref watch);
            if (registration == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SHChangeNotifyRegister failed.");

            var createdPath = Path.Combine(fixture, "created.txt");
            File.WriteAllText(createdPath, "created");
            SHChangeNotify(Created, PathUnicode, createdPath, null);
            var createEvent = window.WaitFor([Created], TimeSpan.FromSeconds(5));
            var addedEnumeration = WaitForRow(folderPidl, shellFolder, row => row.DisplayName == "created.txt");
            var added = ShellListViewRowModel.Reconcile(current.Rows, addedEnumeration.Rows, NextId(current.Rows));
            ShellListViewEnumerator.FreeRows(added.RetiredRows);
            current = addedEnumeration with { Rows = added.Rows.ToList() };
            var createdId = current.Rows.Single(row => row.DisplayName == "created.txt").Id;

            File.AppendAllText(Path.Combine(fixture, "stable.txt"), " updated");
            File.WriteAllBytes(sizePayloadPath, new byte[10]);
            var nestedDirectory = Path.Combine(sizeDirectory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllBytes(Path.Combine(nestedDirectory, "nested.bin"), new byte[2]);
            SHChangeNotify(UpdatedDirectory, PathUnicode, fixture, null);
            var updateEvent = window.WaitFor([UpdatedDirectory], TimeSpan.FromSeconds(5));
            var updatedEnumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            var updated = ShellListViewRowModel.Reconcile(current.Rows, updatedEnumeration.Rows, added.NextId);
            ShellListViewEnumerator.FreeRows(updated.RetiredRows);
            var updatedScans = ScanLatestPaths(scanCoordinator, fixture, [sizeDirectory], out var updateScanWorker,
                out var updateScanPublishedOnOwner);
            current = updatedEnumeration with
            {
                Rows = ShellListViewRowModel.ApplyRecursiveScanResults(updated.Rows,
                    updatedScans).ToList()
            };
            var recursiveBytesAfterReconciliation = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveBytes;
            var recursiveSummaryAfterReconciliation = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveSummary!;
            var recursiveValueUpdated = initialRecursiveBytes == 4 && recursiveBytesAfterReconciliation == 12;
            var recursiveCountsUpdated = initialRecursiveSummary.Files == 1 && initialRecursiveSummary.Directories == 0 &&
                recursiveSummaryAfterReconciliation.Files == 2 && recursiveSummaryAfterReconciliation.Directories == 1;
            var updateIdPreserved = current.Rows.Single(row => row.DisplayName == "stable.txt").Id == stableId;
            var selectionAfterUpdatePreserved = HasStableSelection(current.Rows);

            var renamedPath = Path.Combine(fixture, "renamed.txt");
            File.Move(createdPath, renamedPath);
            SHChangeNotify(RenamedItem, PathUnicode, createdPath, renamedPath);
            var renameEvent = window.WaitFor([RenamedItem], TimeSpan.FromSeconds(5));
            var renamedEnumeration = WaitForRow(folderPidl, shellFolder, row => row.DisplayName == "renamed.txt");
            renamedEnumeration = renamedEnumeration with
            {
                Rows = renamedEnumeration.Rows.OrderByDescending(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
            };
            var renamed = ShellListViewRowModel.Reconcile(current.Rows, renamedEnumeration.Rows, updated.NextId);
            ShellListViewEnumerator.FreeRows(renamed.RetiredRows);
            current = renamedEnumeration with { Rows = renamed.Rows.ToList() };
            var renamedId = current.Rows.Single(row => row.DisplayName == "renamed.txt").Id;
            var selectionAfterReorderPreserved = HasStableSelection(current.Rows);

            File.Delete(renamedPath);
            SHChangeNotify(Deleted, PathUnicode, renamedPath, null);
            var deleteEvent = window.WaitFor([Deleted], TimeSpan.FromSeconds(5));
            var deletedEnumeration = WaitForRow(folderPidl, shellFolder,
                row => row.DisplayName == "renamed.txt", expectMatch: false);
            var deleted = ShellListViewRowModel.Reconcile(current.Rows, deletedEnumeration.Rows, renamed.NextId);
            var retiredRenamedId = deleted.RetiredRows.Any(row => row.Id == renamedId);
            ShellListViewEnumerator.FreeRows(deleted.RetiredRows);
            current = deletedEnumeration with { Rows = deleted.Rows.ToList() };

            var stableIdentityPreserved = current.Rows.Single(row => row.DisplayName == "stable.txt").Id == stableId;
            var sizeFolderIdentityPreserved = current.Rows.Single(row => row.DisplayName == "size-folder").Id == sizeFolderId;
            var selectionAfterDeletePreserved = HasStableSelection(current.Rows);
            var addedNewIdentity = createdId != stableId && renamedId != createdId && renamedId != stableId;
            Console.WriteLine($"shell-change-reconcile: create-notified={createEvent}; update-notified={updateEvent}; rename-notified={renameEvent}; delete-notified={deleteEvent}; " +
                $"stable-id-preserved={stableIdentityPreserved}; update-id-preserved={updateIdPreserved}; rename-got-new-id={addedNewIdentity}; deleted-id-retired={retiredRenamedId}; " +
                $"recursive-bytes-initial/after-update={initialRecursiveBytes}/{recursiveBytesAfterReconciliation};recursive-value-updated={recursiveValueUpdated};size-folder-id-preserved={sizeFolderIdentityPreserved};" +
                $"recursive-files-initial/after-update={initialRecursiveSummary.Files}/{recursiveSummaryAfterReconciliation.Files};" +
                $"recursive-directories-initial/after-update={initialRecursiveSummary.Directories}/{recursiveSummaryAfterReconciliation.Directories};recursive-counts-updated={recursiveCountsUpdated};" +
                $"scan-workers={initialScanWorker}/{updateScanWorker};scan-on-worker={initialScanWorker != Environment.CurrentManagedThreadId}/{updateScanWorker != Environment.CurrentManagedThreadId};" +
                $"scan-published-on-owner={initialScanPublishedOnOwner}/{updateScanPublishedOnOwner};" +
                $"selection-after-update/reorder/delete={selectionAfterUpdatePreserved}/{selectionAfterReorderPreserved}/{selectionAfterDeletePreserved}; " +
                $"final-row-count={current.Rows.Count}; foreground-unchanged={GetForegroundWindow() == foreground}; hidden-window={IsWindowVisible(window.Handle) == false}");
            if (!createEvent || !updateEvent || !renameEvent || !deleteEvent || !stableIdentityPreserved || !updateIdPreserved || !addedNewIdentity ||
                !retiredRenamedId || !recursiveValueUpdated || !recursiveCountsUpdated || !sizeFolderIdentityPreserved ||
                initialScanWorker == Environment.CurrentManagedThreadId || updateScanWorker == Environment.CurrentManagedThreadId ||
                !initialScanPublishedOnOwner || !updateScanPublishedOnOwner ||
                !selectionAfterUpdatePreserved || !selectionAfterReorderPreserved || !selectionAfterDeletePreserved ||
                current.Rows.Count != 2 || GetForegroundWindow() != foreground || IsWindowVisible(window.Handle))
                throw new InvalidOperationException("Shell change notification reconciliation contract failed.");
            return 0;

            bool HasStableSelection(IReadOnlyList<ShellListViewRow> rows) => selection.Resolve(rows)
                .Any(state => state.RowId == stableId && state.Selected && state.Focused);
        }
        finally
        {
            if (registration != 0) _ = SHChangeNotifyDeregister(registration);
            if (current is not null) ShellListViewEnumerator.FreeRows(current.Rows);
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (folderPidl != 0) ILFree(folderPidl);
            try { Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static ShellListViewEnumeration WaitForRow(nint folderPidl, nint shellFolder,
        Func<ShellListViewRow, bool> predicate, bool expectMatch = true)
    {
        var until = Environment.TickCount64 + 5000;
        do
        {
            var enumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            if (enumeration.Rows.Any(predicate) == expectMatch) return enumeration;
            ShellListViewEnumerator.FreeRows(enumeration.Rows);
            Application.DoEvents();
            Thread.Sleep(30);
        } while (Environment.TickCount64 < until);
        throw new TimeoutException("The Shell folder enumeration did not reflect the notified filesystem change.");
    }

    private static IReadOnlyDictionary<string, RecursiveDirectoryScanResult> ScanLatestPaths(
        DirectoryTreeScanCoordinator coordinator, string root, IReadOnlyList<string> paths,
        out int workerThreadId, out bool publishedOnOwnerThread)
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var observedWorkerThreadId = 0;
        var publishedOnOwner = false;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ownerContext = new DispatcherSynchronizationContext(dispatcher);
        IReadOnlyDictionary<string, RecursiveDirectoryScanResult>? result = null;
        var scan = coordinator.RunLatestAsync(
            cancellationToken =>
            {
                observedWorkerThreadId = Environment.CurrentManagedThreadId;
                return RecursiveDirectoryScanner.ScanDirectoryTreeForPaths(root, paths, cancellationToken);
            },
            ownerContext, value =>
            {
                result = value;
                publishedOnOwner = Environment.CurrentManagedThreadId == ownerThreadId;
            });
        var frame = new DispatcherFrame();
        _ = scan.ContinueWith(_ => dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false)), TaskScheduler.Default);
        if (!scan.IsCompleted) Dispatcher.PushFrame(frame);
        if (!scan.GetAwaiter().GetResult() || result is null)
            throw new InvalidOperationException("The latest recursive row scan was canceled before publication.");
        workerThreadId = observedWorkerThreadId;
        publishedOnOwnerThread = publishedOnOwner;
        return result;
    }

    private static long NextId(IReadOnlyList<ShellListViewRow> rows) => rows.Count == 0 ? 1 : rows.Max(row => row.Id) + 1;

    private sealed class ChangeWindow : NativeWindow, IDisposable
    {
        private readonly List<uint> _events = [];

        internal ChangeWindow()
        {
            CreateHandle(new CreateParams { Caption = "ExplorerDirs Shell change probe", Parent = new nint(-3) });
        }

        internal bool WaitFor(uint[] expected, TimeSpan timeout)
        {
            var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < until)
            {
                Application.DoEvents();
                if (expected.All(value => _events.Contains(value))) return true;
                Thread.Sleep(10);
            }
            return expected.All(value => _events.Contains(value));
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ChangeMessage)
            {
                var changeLock = SHChangeNotificationLock(message.WParam,
                    unchecked((uint)message.LParam.ToInt64()), out _, out var eventId);
                if (changeLock != 0)
                {
                    try { _events.Add(unchecked((uint)eventId)); }
                    finally { _ = SHChangeNotificationUnlock(changeLock); }
                }
            }
            base.WndProc(ref message);
        }

        public void Dispose() => DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)] private struct ShellChangeEntry { public nint Pidl; [MarshalAs(UnmanagedType.Bool)] public bool Recursive; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint result);
    [DllImport("shell32.dll")] private static extern uint SHChangeNotifyRegister(nint window, uint sources, uint events, int message, int entryCount, ref ShellChangeEntry entry);
    [DllImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHChangeNotifyDeregister(uint registration);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Lock")] private static extern nint SHChangeNotificationLock(nint change, uint processId, out nint pidls, out int eventId);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Unlock")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHChangeNotificationUnlock(nint changeLock);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string item1, string? item2);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern void ILFree(nint pidl);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
}
