using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Hidden-window contract for Shell change notifications and row snapshot reconciliation.
internal static class ShellChangeReconciliationProbe
{
    private const int ChangeMessage = 0x8000 + 0x321;
    // Keep the distinct folder-rename bit; 0x00002000 is UPDATEITEM, not RENAMEFOLDER.
    private const uint WatchEvents = 0x00000001 | 0x00000002 | 0x00000004 | 0x00000008 |
                                    0x00000200 | 0x00001000 | 0x00002000 | 0x00020000;
    private const uint ShellAndInterruptSources = 0x00000003 | 0x00008000;
    private const uint PathUnicode = 0x00000005;
    private const uint Created = 0x00000002;
    private const uint Deleted = 0x00000004;
    private const uint RenamedItem = 0x00000001;
    private const uint RenamedFolder = 0x00020000;
    private const uint UpdatedItem = 0x00002000;
    private const uint UpdatedDirectory = 0x00001000;
    private const int LvmInsertItemW = 0x104D;
    private const int LvmSetItemTextW = 0x1074;
    private const int LvmDeleteAllItems = 0x1009;
    private const int LvmGetItemCount = 0x1004;
    private const int LvmGetNextItem = 0x100C;
    private const int LvmGetItemState = 0x102C;
    private const int LvmSetItemState = 0x102B;
    private const int LvmInsertColumnW = 0x1061;
    private const uint LvifText = 0x0001;
    private const uint LvifParam = 0x0004;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const uint LvinSelected = 0x0002;
    private const uint LvinFocused = 0x0001;
    private const uint SigDnDesktopAbsoluteParsing = 0x80028000;
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
        nint listParent = 0;
        nint listView = 0;
        uint registration = 0;
        ShellListViewEnumeration? current = null;
        using var scanCoordinator = new DirectoryTreeScanCoordinator();
        using var window = new ChangeWindow();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out shellFolder));
            current = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            var initialScans = ScanLatestPaths(scanCoordinator, [sizeDirectory], out var initialScanWorker,
                out var initialScanPublishedOnOwner);
            current = current with
            {
                Rows = ShellListViewRowModel.ApplyRecursiveScanResults(current.Rows, initialScans).ToList()
            };
            listParent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden row reconciliation probe",
                unchecked((int)0x80000000), 0, 0, 360, 220, 0, 0, GetModuleHandleW(null), 0);
            if (listParent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Hidden list parent creation failed.");
            listView = CreateWindowExW(0, "SysListView32", "",
                0x40000000 | 0x00000001, 0, 0, 340, 200, listParent, 0, GetModuleHandleW(null), 0);
            if (listView == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Hidden ListView creation failed.");
            AddColumn(listView, "Name", 180, 0);
            AddColumn(listView, "Recursive size", 100, 1);
            AddColumn(listView, "Contents", 130, 2);
            ProjectRows(listView, current.Rows);
            var stableId = current.Rows.Single(row => row.DisplayName == "stable.txt").Id;
            var sizeFolderId = current.Rows.Single(row => row.DisplayName == "size-folder").Id;
            var initialRecursiveBytes = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveBytes;
            var initialRecursiveSummary = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveSummary!;
            SetRowState(listView, FindIndexById(listView, stableId), LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            var selection = CaptureSelection(listView, current.Rows);
            var watch = new ShellChangeEntry { Pidl = folderPidl, Recursive = true };
            registration = SHChangeNotifyRegister(window.Handle, ShellAndInterruptSources, WatchEvents,
                ChangeMessage, 1, ref watch);
            if (registration == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SHChangeNotifyRegister failed.");

            File.WriteAllBytes(sizePayloadPath, new byte[7]);
            SHChangeNotify(UpdatedItem, PathUnicode, sizeDirectory, null);
            var updatedItemEvent = window.WaitFor([UpdatedItem], TimeSpan.FromSeconds(5));
            var itemUpdatedEnumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            var itemUpdated = ShellListViewRowModel.Reconcile(current.Rows, itemUpdatedEnumeration.Rows, NextId(current.Rows));
            ShellListViewEnumerator.FreeRows(itemUpdated.RetiredRows);
            var itemUpdatedScans = ScanLatestPaths(scanCoordinator, [sizeDirectory], out var itemUpdateScanWorker,
                out var itemUpdateScanPublishedOnOwner);
            current = itemUpdatedEnumeration with
            {
                Rows = ShellListViewRowModel.ApplyRecursiveScanResults(itemUpdated.Rows, itemUpdatedScans).ToList()
            };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var itemUpdatedBytes = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveBytes;
            var itemUpdatedValue = GetItemText(listView, FindIndexById(listView, sizeFolderId), 1);
            var itemUpdatedIdentityPreserved = current.Rows.Single(row => row.DisplayName == "stable.txt").Id == stableId &&
                current.Rows.Single(row => row.Id == sizeFolderId).Id == sizeFolderId;
            var itemUpdatedSelectionPreserved = HasStableSelection(current.Rows) && HasStableListViewSelection(listView, stableId);
            if (itemUpdatedBytes != 7 || itemUpdatedValue != "7 B" || !itemUpdatedIdentityPreserved || !itemUpdatedSelectionPreserved ||
                itemUpdateScanWorker == Environment.CurrentManagedThreadId || !itemUpdateScanPublishedOnOwner)
                throw new InvalidOperationException("SHCNE_UPDATEITEM did not refresh the derived folder row while preserving row identity and selection.");
            Console.WriteLine($"shell-change-reconciliation: event=UPDATEITEM; event-received=True; recursive-size={itemUpdatedBytes}; displayed={itemUpdatedValue}; stable-id-preserved={itemUpdatedIdentityPreserved}; selection-preserved={itemUpdatedSelectionPreserved}; worker-thread={itemUpdateScanWorker}; owner-thread={Environment.CurrentManagedThreadId}");

            var createdPath = Path.Combine(fixture, "created.txt");
            File.WriteAllText(createdPath, "created");
            SHChangeNotify(Created, PathUnicode, createdPath, null);
            var createEvent = window.WaitFor([Created], TimeSpan.FromSeconds(5));
            var addedEnumeration = WaitForRow(folderPidl, shellFolder, row => row.DisplayName == "created.txt");
            var added = ShellListViewRowModel.Reconcile(current.Rows, addedEnumeration.Rows, NextId(current.Rows));
            ShellListViewEnumerator.FreeRows(added.RetiredRows);
            current = addedEnumeration with { Rows = added.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
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
            var updatedScans = ScanLatestPaths(scanCoordinator, [sizeDirectory], out var updateScanWorker,
                out var updateScanPublishedOnOwner);
            current = updatedEnumeration with
            {
                Rows = ShellListViewRowModel.ApplyRecursiveScanResults(updated.Rows,
                    updatedScans).ToList()
            };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var recursiveBytesAfterReconciliation = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveBytes;
            var recursiveSummaryAfterReconciliation = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveSummary!;
            var projectedSize = GetItemText(listView, FindIndexById(listView, sizeFolderId), 1);
            var projectedContents = GetItemText(listView, FindIndexById(listView, sizeFolderId), 2);
            var recursiveValueUpdated = initialRecursiveBytes == 4 && recursiveBytesAfterReconciliation == 12;
            var recursiveCountsUpdated = initialRecursiveSummary.Files == 1 && initialRecursiveSummary.Directories == 0 &&
                recursiveSummaryAfterReconciliation.Files == 2 && recursiveSummaryAfterReconciliation.Directories == 1;
            var projectedDerivedValuesUpdated = projectedSize == "12 B" && projectedContents == "files: 2, folders: 1";
            var updateIdPreserved = current.Rows.Single(row => row.DisplayName == "stable.txt").Id == stableId;
            var selectionAfterUpdatePreserved = HasStableSelection(current.Rows) && HasStableListViewSelection(listView, stableId);

            var renamedPath = Path.Combine(fixture, "renamed.txt");
            File.Move(createdPath, renamedPath);
            SHChangeNotify(RenamedItem, PathUnicode, createdPath, renamedPath);
            var renameEvent = window.WaitFor([RenamedItem], TimeSpan.FromSeconds(5));
            var renamedEnumeration = WaitForRow(folderPidl, shellFolder, row => row.DisplayName == "renamed.txt");
            renamedEnumeration = renamedEnumeration with
            {
                Rows = renamedEnumeration.Rows.OrderByDescending(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
            };
            var renameIdentities = window.RenamedParsingNames;
            var renamed = ShellListViewRowModel.Reconcile(current.Rows, renamedEnumeration.Rows, updated.NextId,
                renameIdentities);
            ShellListViewEnumerator.FreeRows(renamed.RetiredRows);
            current = renamedEnumeration with { Rows = renamed.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var renamedId = current.Rows.Single(row => row.DisplayName == "renamed.txt").Id;
            var selectionAfterReorderPreserved = HasStableSelection(current.Rows) && HasStableListViewSelection(listView, stableId);

            File.Delete(renamedPath);
            SHChangeNotify(Deleted, PathUnicode, renamedPath, null);
            var deleteEvent = window.WaitFor([Deleted], TimeSpan.FromSeconds(5));
            var deletedEnumeration = WaitForRow(folderPidl, shellFolder,
                row => row.DisplayName == "renamed.txt", expectMatch: false);
            var deleted = ShellListViewRowModel.Reconcile(current.Rows, deletedEnumeration.Rows, renamed.NextId);
            var retiredRenamedId = deleted.RetiredRows.Any(row => row.Id == renamedId);
            ShellListViewEnumerator.FreeRows(deleted.RetiredRows);
            current = deletedEnumeration with { Rows = deleted.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);

            var renamedSizeDirectory = Path.Combine(fixture, "renamed-size-folder");
            Directory.Move(sizeDirectory, renamedSizeDirectory);
            SHChangeNotify(RenamedFolder, PathUnicode, sizeDirectory, renamedSizeDirectory);
            var renameFolderEvent = window.WaitFor([RenamedFolder], TimeSpan.FromSeconds(5));
            var renamedFolderEnumeration = WaitForRow(folderPidl, shellFolder,
                row => row.DisplayName == "renamed-size-folder");
            var renamedFolderReconciliation = ShellListViewRowModel.Reconcile(current.Rows,
                renamedFolderEnumeration.Rows, deleted.NextId, window.RenamedParsingNames);
            ShellListViewEnumerator.FreeRows(renamedFolderReconciliation.RetiredRows);
            current = renamedFolderEnumeration with { Rows = renamedFolderReconciliation.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var renamedSizeFolder = current.Rows.Single(row => row.DisplayName == "renamed-size-folder");
            var renamedSizeFolderRowIndex = FindIndexById(listView, renamedSizeFolder.Id);
            var folderRenameIdPreserved = renamedSizeFolder.Id == sizeFolderId;
            // A same-identity directory rename can keep its last completed summary visible while the fresh scan runs.
            var folderRenameCachedSummaryPreserved = renamedSizeFolder.RecursiveBytes == 12 &&
                renamedSizeFolder.RecursiveSummary?.Files == 2 && renamedSizeFolder.RecursiveSummary.Directories == 1 &&
                renamedSizeFolderRowIndex >= 0 && GetItemText(listView, renamedSizeFolderRowIndex, 1) == "12 B";

            var renamedFolderScans = ScanLatestPaths(scanCoordinator, [renamedSizeDirectory],
                out var folderRenameScanWorker, out var folderRenameScanPublishedOnOwner);
            current = current with
            {
                Rows = ShellListViewRowModel.ApplyRecursiveScanResults(current.Rows, renamedFolderScans).ToList()
            };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            renamedSizeFolder = current.Rows.Single(row => row.DisplayName == "renamed-size-folder");
            var folderRenameRescanFresh = renamedSizeFolder.RecursiveBytes == 12 &&
                renamedSizeFolder.RecursiveSummary?.Files == 2 && renamedSizeFolder.RecursiveSummary.Directories == 1 &&
                GetItemText(listView, FindIndexById(listView, renamedSizeFolder.Id), 1) == "12 B";

            var stableIdentityPreserved = current.Rows.Single(row => row.DisplayName == "stable.txt").Id == stableId;
            var sizeFolderIdentityPreserved = current.Rows.Single(row => row.DisplayName == "renamed-size-folder").Id == sizeFolderId;
            var selectionAfterDeletePreserved = HasStableSelection(current.Rows) && HasStableListViewSelection(listView, stableId);
            var renameIdentityPreserved = renamedId == createdId;
            var addedNewIdentity = createdId != stableId && renamedId != stableId;
            Console.WriteLine($"shell-change-reconcile: create-notified={createEvent}; update-notified={updateEvent}; update-item-notified={updatedItemEvent}; rename-notified={renameEvent}; rename-folder-notified={renameFolderEvent}; delete-notified={deleteEvent}; " +
                $"stable-id-preserved={stableIdentityPreserved}; update-id-preserved={updateIdPreserved}; rename-id-preserved={renameIdentityPreserved}; deleted-id-retired={retiredRenamedId}; " +
                $"folder-rename-id-preserved={folderRenameIdPreserved};folder-rename-cached-summary-preserved={folderRenameCachedSummaryPreserved};folder-rename-rescan-fresh={folderRenameRescanFresh};" +
                $"recursive-bytes-initial/after-update={initialRecursiveBytes}/{recursiveBytesAfterReconciliation};recursive-value-updated={recursiveValueUpdated};size-folder-id-preserved={sizeFolderIdentityPreserved};" +
                $"recursive-files-initial/after-update={initialRecursiveSummary.Files}/{recursiveSummaryAfterReconciliation.Files};" +
                $"recursive-directories-initial/after-update={initialRecursiveSummary.Directories}/{recursiveSummaryAfterReconciliation.Directories};recursive-counts-updated={recursiveCountsUpdated};" +
                $"projected-size/contents={projectedSize}/{projectedContents};projected-derived-values-updated={projectedDerivedValuesUpdated};" +
                $"scan-workers={initialScanWorker}/{updateScanWorker}/{folderRenameScanWorker};scan-on-worker={initialScanWorker != Environment.CurrentManagedThreadId}/{updateScanWorker != Environment.CurrentManagedThreadId}/{folderRenameScanWorker != Environment.CurrentManagedThreadId};" +
                $"scan-published-on-owner={initialScanPublishedOnOwner}/{updateScanPublishedOnOwner}/{folderRenameScanPublishedOnOwner};" +
                $"selection-after-update/reorder/delete={selectionAfterUpdatePreserved}/{selectionAfterReorderPreserved}/{selectionAfterDeletePreserved}; " +
                $"listview-row-count={SendMessageW(listView, LvmGetItemCount, 0, 0)};selection-focused-row-preserved={selectionAfterUpdatePreserved}/{selectionAfterReorderPreserved}/{selectionAfterDeletePreserved};" +
                $"final-row-count={current.Rows.Count}; foreground-unchanged={GetForegroundWindow() == foreground}; hidden-window={IsWindowVisible(window.Handle) == false && !IsWindowVisible(listParent)}");
            if (!createEvent || !updateEvent || !updatedItemEvent || !renameEvent || !renameFolderEvent || !deleteEvent || !stableIdentityPreserved || !updateIdPreserved ||
                !renameIdentityPreserved || !addedNewIdentity ||
                !folderRenameIdPreserved || !folderRenameCachedSummaryPreserved || !folderRenameRescanFresh ||
                !retiredRenamedId || !recursiveValueUpdated || !recursiveCountsUpdated || !projectedDerivedValuesUpdated || !sizeFolderIdentityPreserved ||
                initialScanWorker == Environment.CurrentManagedThreadId || updateScanWorker == Environment.CurrentManagedThreadId ||
                folderRenameScanWorker == Environment.CurrentManagedThreadId ||
                !initialScanPublishedOnOwner || !updateScanPublishedOnOwner || !folderRenameScanPublishedOnOwner ||
                !selectionAfterUpdatePreserved || !selectionAfterReorderPreserved || !selectionAfterDeletePreserved ||
                current.Rows.Count != 2 || SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32() != current.Rows.Count ||
                GetForegroundWindow() != foreground || IsWindowVisible(window.Handle) || IsWindowVisible(listParent))
                throw new InvalidOperationException("Shell change notification reconciliation contract failed.");
            return 0;

            bool HasStableSelection(IReadOnlyList<ShellListViewRow> rows) => selection.Resolve(rows)
                .Any(state => state.RowId == stableId && state.Selected && state.Focused);
        }
        finally
        {
            if (registration != 0) _ = SHChangeNotifyDeregister(registration);
            if (listParent != 0) _ = DestroyWindow(listParent);
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

    private static void AddColumn(nint listView, string label, int width, int index)
    {
        var text = Marshal.StringToHGlobalUni(label);
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewColumn>());
        try
        {
            Marshal.StructureToPtr(new ListViewColumn
            {
                Mask = 0x0001 | 0x0002 | 0x0004,
                Width = width,
                SubItem = index,
                Text = text,
                TextMax = label.Length
            }, memory, false);
            if (SendMessageW(listView, LvmInsertColumnW, index, memory).ToInt32() < 0)
                throw new InvalidOperationException("The hidden ListView rejected a derived-value column.");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(text);
        }
    }

    private static void ProjectRows(nint listView, IReadOnlyList<ShellListViewRow> rows)
    {
        _ = SendMessageW(listView, LvmDeleteAllItems, 0, 0);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            InsertItem(listView, index, 0, row.DisplayName, row.Id);
            InsertItem(listView, index, 1, row.RecursiveBytes is { } bytes ? $"{bytes} B" : "");
            var summary = row.RecursiveSummary;
            InsertItem(listView, index, 2, summary is null ? "" : $"files: {summary.Files}, folders: {summary.Directories}");
        }
    }

    private static void InsertItem(nint listView, int index, int subItem, string value, long rowId = 0)
    {
        var text = Marshal.StringToHGlobalUni(value);
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(new ListViewItem
            {
                Mask = subItem == 0 ? LvifText | LvifParam : LvifText,
                Item = index,
                SubItem = subItem,
                Text = text,
                TextMax = value.Length,
                Param = new nint(rowId)
            }, memory, false);
            var message = subItem == 0 ? LvmInsertItemW : LvmSetItemTextW;
            if (SendMessageW(listView, message, index, memory).ToInt32() < 0)
                throw new InvalidOperationException("The hidden ListView rejected a Shell row value.");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(text);
        }
    }

    private static string GetItemText(nint listView, int index, int subItem)
    {
        var buffer = Marshal.AllocHGlobal(1024);
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.WriteInt16(buffer, 0);
            Marshal.StructureToPtr(new ListViewItem
            {
                Mask = LvifText,
                Item = index,
                SubItem = subItem,
                Text = buffer,
                TextMax = 512
            }, memory, false);
            if (SendMessageW(listView, 0x104B, 0, memory) == 0)
                throw new InvalidOperationException("The hidden ListView row text could not be read back.");
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int FindIndexById(nint listView, long rowId)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        for (var index = 0; index < count; index++)
            if (GetItemId(listView, index) == rowId) return index;
        return -1;
    }

    private static long GetItemId(nint listView, int index)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(new ListViewItem { Mask = LvifParam, Item = index }, memory, false);
            if (SendMessageW(listView, 0x104B, 0, memory) == 0)
                throw new InvalidOperationException("The hidden ListView Shell row identity could not be read back.");
            return Marshal.PtrToStructure<ListViewItem>(memory).Param.ToInt64();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static ShellListViewSelectionSnapshot CaptureSelection(nint listView,
        IReadOnlyList<ShellListViewRow> rows)
    {
        var selectedIds = new List<long>();
        var index = -1;
        while ((index = SendMessageW(listView, LvmGetNextItem, index, (nint)LvinSelected).ToInt32()) >= 0)
            selectedIds.Add(GetItemId(listView, index));
        var focusedIndex = SendMessageW(listView, LvmGetNextItem, -1, (nint)LvinFocused).ToInt32();
        return ShellListViewSelectionSnapshot.Capture(rows, selectedIds,
            focusedIndex < 0 ? null : GetItemId(listView, focusedIndex));
    }

    private static void ApplySelection(nint listView, IReadOnlyList<ShellListViewRow> rows,
        ShellListViewSelectionSnapshot snapshot)
    {
        SetRowState(listView, -1, 0, LvisSelected | LvisFocused);
        foreach (var state in snapshot.Resolve(rows))
        {
            var flags = (state.Selected ? LvisSelected : 0) | (state.Focused ? LvisFocused : 0);
            if (flags != 0) SetRowState(listView, FindIndexById(listView, state.RowId), flags, LvisSelected | LvisFocused);
        }
    }

    private static void SetRowState(nint listView, int index, uint state, uint mask)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(new ListViewItem { State = state, StateMask = mask }, memory, false);
            _ = SendMessageW(listView, LvmSetItemState, index, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static bool HasStableListViewSelection(nint listView, long stableId)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        var selectedStableRow = false;
        var focusedStableRow = false;
        for (var index = 0; index < count; index++)
        {
            if (GetItemId(listView, index) != stableId) continue;
            var state = unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)));
            selectedStableRow = (state & LvisSelected) != 0;
            focusedStableRow = (state & LvisFocused) != 0;
            break;
        }
        return selectedStableRow && focusedStableRow;
    }

    private static IReadOnlyDictionary<string, ShellListViewRowScanResult> ScanLatestPaths(
        DirectoryTreeScanCoordinator coordinator, IReadOnlyList<string> paths,
        out int workerThreadId, out bool publishedOnOwnerThread)
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var observedWorkerThreadId = 0;
        var publishedOnOwner = false;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ownerContext = new DispatcherSynchronizationContext(dispatcher);
        IReadOnlyDictionary<string, ShellListViewRowScanResult>? result = null;
        var scan = coordinator.RunLatestAsync(
            cancellationToken =>
            {
                observedWorkerThreadId = Environment.CurrentManagedThreadId;
                return HandleBoundRecursiveDirectoryScanner.Scan(paths, cancellationToken);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ListViewItem
    {
        public uint Mask; public int Item; public int SubItem; public uint State; public uint StateMask;
        public nint Text; public int TextMax; public int Image; public nint Param; public int Indent;
        public int GroupId; public uint ColumnCount; public nint Columns; public nint ColumnFormats; public int Group;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ListViewColumn
    {
        public uint Mask; public int Format; public int Width; public int SubItem; public nint Text;
        public int TextMax; public int Image; public int Order; public int MinWidth; public int DefaultWidth; public int IdealWidth;
    }

    private sealed class ChangeWindow : NativeWindow, IDisposable
    {
        private readonly List<uint> _events = [];
        private readonly Dictionary<string, string> _renamedParsingNames = new(StringComparer.OrdinalIgnoreCase);
        internal IReadOnlyDictionary<string, string> RenamedParsingNames => _renamedParsingNames;

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
                    unchecked((uint)message.LParam.ToInt64()), out var pidls, out var eventId);
                if (changeLock != 0)
                {
                    try
                    {
                        _events.Add(unchecked((uint)eventId));
                        if ((unchecked((uint)eventId) == RenamedItem || unchecked((uint)eventId) == RenamedFolder) &&
                            pidls != 0)
                        {
                            var oldPidl = Marshal.ReadIntPtr(pidls);
                            var newPidl = Marshal.ReadIntPtr(pidls, IntPtr.Size);
                            var oldParsingName = AbsoluteParsingName(oldPidl);
                            var newParsingName = AbsoluteParsingName(newPidl);
                            if (oldParsingName is not null && newParsingName is not null)
                                _renamedParsingNames[newParsingName] = oldParsingName;
                        }
                    }
                    finally { _ = SHChangeNotificationUnlock(changeLock); }
                }
            }
            base.WndProc(ref message);
        }

        private static string? AbsoluteParsingName(nint pidl)
        {
            if (pidl == 0 || SHGetNameFromIDList(pidl, SigDnDesktopAbsoluteParsing, out var name) < 0) return null;
            try { return Marshal.PtrToStringUni(name); }
            finally { Marshal.FreeCoTaskMem(name); }
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
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string item1, string? item2);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern void ILFree(nint pidl);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
}
