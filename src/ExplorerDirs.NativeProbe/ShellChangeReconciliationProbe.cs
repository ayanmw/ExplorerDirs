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
                                    0x00000200 | 0x00000800 | 0x00001000 | 0x00002000 | 0x00020000;
    private const uint ShellAndInterruptSources = 0x00000003 | 0x00008000;
    private const uint PathUnicode = 0x00000005;
    private const uint Created = 0x00000002;
    private const uint Deleted = 0x00000004;
    private const uint RenamedItem = 0x00000001;
    private const uint RenamedFolder = 0x00020000;
    private const uint UpdatedItem = 0x00002000;
    private const uint UpdatedDirectory = 0x00001000;
    private const uint Attributes = 0x00000800;
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid FileOperationClassId = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid FileOperationId = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
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
        var attributePath = Path.Combine(fixture, "attribute.txt");
        File.WriteAllText(attributePath, "attributes");
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
            var attributeId = current.Rows.Single(row => row.DisplayName == "attribute.txt").Id;
            var sizeFolderId = current.Rows.Single(row => row.DisplayName == "size-folder").Id;
            var initialRecursiveBytes = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveBytes;
            var initialRecursiveSummary = current.Rows.Single(row => row.Id == sizeFolderId).RecursiveSummary!;
            // Exercise a multi-selection with a distinct focus anchor across each projected refresh.
            SetRowState(listView, FindIndexById(listView, stableId), LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            SetRowState(listView, FindIndexById(listView, attributeId), LvisSelected, LvisSelected);
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
            var itemUpdatedSelectionPreserved = HasStableSelection(current.Rows) && HasTwoRowSelection(listView, stableId, attributeId);
            if (itemUpdatedBytes != 7 || itemUpdatedValue != "7 B" || !itemUpdatedIdentityPreserved || !itemUpdatedSelectionPreserved ||
                itemUpdateScanWorker == Environment.CurrentManagedThreadId || !itemUpdateScanPublishedOnOwner)
                throw new InvalidOperationException("SHCNE_UPDATEITEM did not refresh the derived folder row while preserving row identity and selection.");
            Console.WriteLine($"shell-change-reconciliation: event=UPDATEITEM; event-received=True; recursive-size={itemUpdatedBytes}; displayed={itemUpdatedValue}; stable-id-preserved={itemUpdatedIdentityPreserved}; selection-preserved={itemUpdatedSelectionPreserved}; worker-thread={itemUpdateScanWorker}; owner-thread={Environment.CurrentManagedThreadId}");

            var originalAttributes = File.GetAttributes(attributePath);
            var attributesEventStart = window.Events.Count;
            File.SetAttributes(attributePath, originalAttributes | FileAttributes.ReadOnly);
            SHChangeNotify(Attributes, PathUnicode, attributePath, null);
            var attributesEvent = window.WaitForAnySince(attributesEventStart, [Attributes], TimeSpan.FromSeconds(5));
            var attributesEnumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            var attributesReconciliation = ShellListViewRowModel.Reconcile(current.Rows,
                attributesEnumeration.Rows, NextId(current.Rows));
            ShellListViewEnumerator.FreeRows(attributesReconciliation.RetiredRows);
            current = attributesEnumeration with { Rows = attributesReconciliation.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var readonlyRow = current.Rows.Single(row => row.Id == attributeId);
            var readonlyFlagUpdated = (readonlyRow.Attributes & 0x00040000) != 0; // SFGAO_READONLY
            var attributeIdentityPreserved = readonlyRow.Id == attributeId;
            var selectionAfterAttributes = HasStableSelection(current.Rows) && HasTwoRowSelection(listView, stableId, attributeId);
            Console.WriteLine($"shell-change-reconciliation: event=ATTRIBUTES-check; event-received={attributesEvent}; file-attributes=0x{(uint)File.GetAttributes(attributePath):X8}; shell-attributes=0x{readonlyRow.Attributes:X8}; readonly-flag-updated={readonlyFlagUpdated}; row-id-preserved={attributeIdentityPreserved}; selection-preserved={selectionAfterAttributes}");
            if (!attributesEvent || !readonlyFlagUpdated || !attributeIdentityPreserved || !selectionAfterAttributes)
                throw new InvalidOperationException("SHCNE_ATTRIBUTES did not refresh the row attributes while preserving row identity and selection.");
            Console.WriteLine($"shell-change-reconciliation: event=ATTRIBUTES; event-received={attributesEvent}; readonly-flag-updated={readonlyFlagUpdated}; row-id-preserved={attributeIdentityPreserved}; selection-preserved={selectionAfterAttributes}");

            File.SetAttributes(attributePath, originalAttributes);
            var restoredAttributesEventStart = window.Events.Count;
            SHChangeNotify(Attributes, PathUnicode, attributePath, null);
            var restoredAttributesEvent = window.WaitForAnySince(restoredAttributesEventStart,
                [Attributes], TimeSpan.FromSeconds(5));
            var restoredAttributesEnumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            var restoredAttributesReconciliation = ShellListViewRowModel.Reconcile(current.Rows,
                restoredAttributesEnumeration.Rows, NextId(current.Rows));
            ShellListViewEnumerator.FreeRows(restoredAttributesReconciliation.RetiredRows);
            current = restoredAttributesEnumeration with { Rows = restoredAttributesReconciliation.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var restoredAttributeRow = current.Rows.Single(row => row.Id == attributeId);
            var readonlyFlagCleared = (restoredAttributeRow.Attributes & 0x00040000) == 0;
            var restoredAttributeSelection = HasStableSelection(current.Rows) && HasTwoRowSelection(listView, stableId, attributeId);
            if (!restoredAttributesEvent || !readonlyFlagCleared || restoredAttributeRow.Id != attributeId || !restoredAttributeSelection)
                throw new InvalidOperationException("SHCNE_ATTRIBUTES did not clear the row's read-only attribute while preserving row identity and selection.");
            Console.WriteLine($"shell-change-reconciliation: event=ATTRIBUTES-restore; event-received={restoredAttributesEvent}; readonly-flag-cleared={readonlyFlagCleared}; row-id-preserved={restoredAttributeRow.Id == attributeId}; selection-preserved={restoredAttributeSelection}");

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
            var selectionAfterUpdatePreserved = HasStableSelection(current.Rows) && HasTwoRowSelection(listView, stableId, attributeId);

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
            var selectionAfterReorderPreserved = HasStableSelection(current.Rows) && HasTwoRowSelection(listView, stableId, attributeId);

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
            var selectionAfterDeletePreserved = HasStableSelection(current.Rows) && HasTwoRowSelection(listView, stableId, attributeId);
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
                $"listview-row-count={SendMessageW(listView, LvmGetItemCount, 0, 0)};multi-selection-and-focus-preserved={selectionAfterUpdatePreserved}/{selectionAfterReorderPreserved}/{selectionAfterDeletePreserved};" +
                $"final-row-count={current.Rows.Count}; foreground-unchanged={GetForegroundWindow() == foreground}; hidden-window={IsWindowVisible(window.Handle) == false && !IsWindowVisible(listParent)}");
            if (!createEvent || !updateEvent || !updatedItemEvent || !renameEvent || !renameFolderEvent || !deleteEvent || !stableIdentityPreserved || !updateIdPreserved ||
                !renameIdentityPreserved || !addedNewIdentity ||
                !folderRenameIdPreserved || !folderRenameCachedSummaryPreserved || !folderRenameRescanFresh ||
                !retiredRenamedId || !recursiveValueUpdated || !recursiveCountsUpdated || !projectedDerivedValuesUpdated || !sizeFolderIdentityPreserved ||
                initialScanWorker == Environment.CurrentManagedThreadId || updateScanWorker == Environment.CurrentManagedThreadId ||
                folderRenameScanWorker == Environment.CurrentManagedThreadId ||
                !initialScanPublishedOnOwner || !updateScanPublishedOnOwner || !folderRenameScanPublishedOnOwner ||
                !selectionAfterUpdatePreserved || !selectionAfterReorderPreserved || !selectionAfterDeletePreserved ||
                current.Rows.Count != 3 || SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32() != current.Rows.Count ||
                GetForegroundWindow() != foreground || IsWindowVisible(window.Handle) || IsWindowVisible(listParent))
                throw new InvalidOperationException("Shell change notification reconciliation contract failed.");
            VerifyCrossPaneMove(foreground);
            VerifyCrossPaneMove(foreground, crossVolume: true);
            return 0;

            bool HasStableSelection(IReadOnlyList<ShellListViewRow> rows)
            {
                var states = selection.Resolve(rows);
                return states.Count(state => state.Selected) == 2 &&
                    states.Any(state => state.RowId == stableId && state.Selected && state.Focused) &&
                    states.Any(state => state.RowId == attributeId && state.Selected && !state.Focused);
            }
        }
        finally
        {
            if (registration != 0) _ = SHChangeNotifyDeregister(registration);
            if (listParent != 0) _ = DestroyWindow(listParent);
            if (current is not null) ShellListViewEnumerator.FreeRows(current.Rows);
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (folderPidl != 0) ILFree(folderPidl);
            try { if (File.Exists(attributePath)) File.SetAttributes(attributePath, FileAttributes.Normal); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void VerifyCrossPaneMove(nint foreground, bool crossVolume = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-cross-pane-change-" + Guid.NewGuid().ToString("N"));
        var sourceFixture = crossVolume
            ? Path.Combine(Environment.CurrentDirectory, "src", ".state", "native-probe", "cross-volume-" + Guid.NewGuid().ToString("N"))
            : fixture;
        if (crossVolume && string.Equals(Path.GetPathRoot(sourceFixture), Path.GetPathRoot(fixture), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The cross-volume Shell move probe requires the checkout and temporary directory to be on different volumes.");
        var sourcePath = Path.Combine(sourceFixture, "pane-source");
        var targetPath = Path.Combine(fixture, "pane-target");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        File.WriteAllText(Path.Combine(sourcePath, "anchor-source.txt"), "source anchor");
        File.WriteAllText(Path.Combine(sourcePath, "moving.txt"), "move me");
        File.WriteAllText(Path.Combine(sourcePath, "moving-second.txt"), "move me too");
        File.WriteAllText(Path.Combine(targetPath, "anchor-target.txt"), "target anchor");

        nint fixturePidl = 0, sourcePidl = 0, targetPidl = 0, sourceFolder = 0, targetFolder = 0;
        nint sourceParent = 0, targetParent = 0, sourceList = 0, targetList = 0;
        uint registration = 0;
        var additionalRegistrations = new List<uint>();
        ShellListViewEnumeration? sourceCurrent = null, targetCurrent = null;
        ShellListViewEnumeration? pendingSource = null, pendingTarget = null;
        using var window = new ChangeWindow();
        try
        {
            if (!crossVolume)
                Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out fixturePidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(sourcePath, 0, out sourcePidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(targetPath, 0, out targetPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, sourcePidl, 0, in ShellFolderId, out sourceFolder));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, targetPidl, 0, in ShellFolderId, out targetFolder));
            sourceCurrent = ShellListViewEnumerator.Enumerate(sourcePidl, sourceFolder);
            targetCurrent = ShellListViewEnumerator.Enumerate(targetPidl, targetFolder);

            sourceParent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden source pane row projection",
                unchecked((int)0x80000000), 0, 0, 320, 180, 0, 0, GetModuleHandleW(null), 0);
            targetParent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden target pane row projection",
                unchecked((int)0x80000000), 0, 0, 320, 180, 0, 0, GetModuleHandleW(null), 0);
            if (sourceParent == 0 || targetParent == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create hidden pane row hosts.");
            sourceList = CreateWindowExW(0, "SysListView32", "", 0x40000000 | 0x00000001,
                0, 0, 300, 160, sourceParent, 0, GetModuleHandleW(null), 0);
            targetList = CreateWindowExW(0, "SysListView32", "", 0x40000000 | 0x00000001,
                0, 0, 300, 160, targetParent, 0, GetModuleHandleW(null), 0);
            if (sourceList == 0 || targetList == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create hidden pane ListViews.");
            AddColumn(sourceList, "Name", 220, 0);
            AddColumn(targetList, "Name", 220, 0);
            ProjectRows(sourceList, sourceCurrent.Rows);
            ProjectRows(targetList, targetCurrent.Rows);

            var sourceAnchorId = sourceCurrent.Rows.Single(row => row.DisplayName == "anchor-source.txt").Id;
            var movedNames = new[] { "moving.txt", "moving-second.txt" };
            var movingIds = sourceCurrent.Rows.Where(row => movedNames.Contains(row.DisplayName, StringComparer.Ordinal))
                .ToDictionary(row => row.DisplayName, row => row.Id, StringComparer.Ordinal);
            var targetAnchorId = targetCurrent.Rows.Single(row => row.DisplayName == "anchor-target.txt").Id;
            foreach (var row in sourceCurrent.Rows.Where(row => movingIds.ContainsKey(row.DisplayName)))
                SetRowState(sourceList, FindIndexById(sourceList, row.Id), LvisSelected, LvisSelected);
            SetRowState(sourceList, FindIndexById(sourceList, sourceAnchorId), LvisSelected | LvisFocused,
                LvisSelected | LvisFocused);
            SetRowState(targetList, FindIndexById(targetList, targetAnchorId), LvisSelected | LvisFocused,
                LvisSelected | LvisFocused);
            var sourceSelection = CaptureSelection(sourceList, sourceCurrent.Rows);
            var targetSelection = CaptureSelection(targetList, targetCurrent.Rows);

            if (crossVolume)
            {
                var sourceWatch = new ShellChangeEntry { Pidl = sourcePidl, Recursive = true };
                var targetWatch = new ShellChangeEntry { Pidl = targetPidl, Recursive = true };
                registration = SHChangeNotifyRegister(window.Handle, ShellAndInterruptSources, WatchEvents,
                    ChangeMessage, 1, ref sourceWatch);
                if (registration == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cross-volume source Shell change registration failed.");
                var targetRegistration = SHChangeNotifyRegister(window.Handle, ShellAndInterruptSources, WatchEvents,
                    ChangeMessage, 1, ref targetWatch);
                if (targetRegistration == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cross-volume target Shell change registration failed.");
                additionalRegistrations.Add(targetRegistration);
            }
            else
            {
                var watch = new ShellChangeEntry { Pidl = fixturePidl, Recursive = true };
                registration = SHChangeNotifyRegister(window.Handle, ShellAndInterruptSources, WatchEvents,
                    ChangeMessage, 1, ref watch);
                if (registration == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cross-pane Shell change registration failed.");
            }

            var sourceRows = sourceCurrent.Rows.Where(row => movedNames.Contains(row.DisplayName, StringComparer.Ordinal)).ToArray();
            if (sourceRows.Length != movedNames.Length) throw new IOException("The source Shell view did not enumerate every selected fixture row.");
            var childArray = Marshal.AllocCoTaskMem(IntPtr.Size * sourceRows.Length);
            nint dataObject = 0, operation = 0, destinationItem = 0;
            try
            {
                for (var index = 0; index < sourceRows.Length; index++)
                    Marshal.WriteIntPtr(childArray, index * IntPtr.Size, sourceRows[index].RelativePidl);
                var dataObjectId = DataObjectId;
                Marshal.ThrowExceptionForHR(At<GetUIObjectOf>(sourceFolder, 10)(sourceFolder, 0, (uint)sourceRows.Length,
                    childArray, in dataObjectId, 0, out dataObject));
                if (dataObject == 0) throw new IOException("The source Shell folder returned no IDataObject for the selected move rows.");

                var fileOperationClassId = FileOperationClassId;
                var fileOperationId = FileOperationId;
                var createOperationHr = CoCreateInstance(ref fileOperationClassId, 0, 1, ref fileOperationId, out operation);
                Marshal.ThrowExceptionForHR(createOperationHr);
                Marshal.ThrowExceptionForHR(At<SetOperationFlags>(operation, 5)(operation,
                    0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                var shellItemId = ShellItemId;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(targetPath, 0, ref shellItemId, out destinationItem));
                Marshal.ThrowExceptionForHR(At<MoveItems>(operation, 15)(operation, dataObject, destinationItem));
                Marshal.ThrowExceptionForHR(At<PerformOperations>(operation, 21)(operation));
                Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(operation, 22)(operation, out var aborted));
                if (aborted != 0) throw new IOException("The isolated cross-pane Shell move was aborted.");
            }
            finally
            {
                if (destinationItem != 0) Marshal.Release(destinationItem);
                if (operation != 0) Marshal.Release(operation);
                if (dataObject != 0) Marshal.Release(dataObject);
                Marshal.FreeCoTaskMem(childArray);
            }

            var filesMoved = movedNames.All(name => !File.Exists(Path.Combine(sourcePath, name)) &&
                File.Exists(Path.Combine(targetPath, name)) &&
                File.ReadAllText(Path.Combine(targetPath, name)) == (name == "moving.txt" ? "move me" : "move me too"));
            if (!filesMoved) throw new IOException("IFileOperation.MoveItems did not move every isolated row to the destination pane folder.");
            var shellMoveNotified = window.WaitForAny([Deleted, Created, RenamedItem], TimeSpan.FromSeconds(5));
            var sourceDeletedNotification = false;
            var targetCreatedNotification = false;
            if (crossVolume)
            {
                sourceDeletedNotification = window.WaitFor(() => window.ChangeEvents.Any(change => change.EventId == Deleted &&
                    movedNames.Any(name => PathEquals(change.FirstPath, Path.Combine(sourcePath, name)))), TimeSpan.FromSeconds(5));
                targetCreatedNotification = window.WaitFor(() => window.ChangeEvents.Any(change => change.EventId == Created &&
                    movedNames.Any(name => PathEquals(change.FirstPath, Path.Combine(targetPath, name)))), TimeSpan.FromSeconds(5));
            }

            pendingSource = WaitForRows(sourcePidl, sourceFolder,
                rows => movedNames.All(name => rows.All(row => row.DisplayName != name)));
            pendingTarget = WaitForRows(targetPidl, targetFolder,
                rows => movedNames.All(name => rows.Any(row => row.DisplayName == name)));
            var sourceReconciliation = ShellListViewRowModel.Reconcile(sourceCurrent.Rows,
                pendingSource.Rows, NextId(sourceCurrent.Rows));
            var targetReconciliation = ShellListViewRowModel.Reconcile(targetCurrent.Rows,
                pendingTarget.Rows, NextId(targetCurrent.Rows));
            ShellListViewEnumerator.FreeRows(sourceReconciliation.RetiredRows);
            ShellListViewEnumerator.FreeRows(targetReconciliation.RetiredRows);
            sourceCurrent = pendingSource with { Rows = sourceReconciliation.Rows.ToList() };
            pendingSource = null;
            targetCurrent = pendingTarget with { Rows = targetReconciliation.Rows.ToList() };
            pendingTarget = null;
            ProjectRows(sourceList, sourceCurrent.Rows);
            ProjectRows(targetList, targetCurrent.Rows);
            ApplySelection(sourceList, sourceCurrent.Rows, sourceSelection);
            ApplySelection(targetList, targetCurrent.Rows, targetSelection);

            var sourceAnchorPreserved = sourceCurrent.Rows.Single(row => row.DisplayName == "anchor-source.txt").Id == sourceAnchorId;
            var targetAnchorPreserved = targetCurrent.Rows.Single(row => row.DisplayName == "anchor-target.txt").Id == targetAnchorId;
            var movedSourceRowsRetired = movedNames.All(name => sourceReconciliation.RetiredRows.Any(row => row.Id == movingIds[name]) &&
                sourceCurrent.Rows.All(row => row.Id != movingIds[name]));
            // Row IDs are pane-local; a new target row may reuse a retired source pane's numeric ID.
            var movedTargetRowsAdded = movedNames.All(name => targetCurrent.Rows.Single(row => row.DisplayName == name) is { } movedTarget &&
                movedTarget.Id != targetAnchorId &&
                !targetReconciliation.RetiredRows.Any(row => row.Id == movedTarget.Id));
            var sourceSelectionPreserved = sourceSelection.Resolve(sourceCurrent.Rows)
                    .Any(state => state.RowId == sourceAnchorId && state.Selected && state.Focused) &&
                HasStableListViewSelection(sourceList, sourceAnchorId);
            var targetSelectionPreserved = targetSelection.Resolve(targetCurrent.Rows)
                    .Any(state => state.RowId == targetAnchorId && state.Selected && state.Focused) &&
                HasStableListViewSelection(targetList, targetAnchorId);
            var isolated = GetForegroundWindow() == foreground && !IsWindowVisible(sourceParent) &&
                !IsWindowVisible(sourceList) && !IsWindowVisible(targetParent) && !IsWindowVisible(targetList);
            Console.WriteLine($"shell-change-cross-pane-move: cross-volume={crossVolume};operation=IFileOperation.MoveItems;shell-change-notified={shellMoveNotified};source-deleted-notification={sourceDeletedNotification};target-created-notification={targetCreatedNotification};change-events=[{string.Join('|', window.ChangeEvents.Select(change => $"0x{change.EventId:X8}:{change.FirstPath}->{change.SecondPath}"))}];events=[{string.Join(',', window.Events.Select(value => $"0x{value:X8}"))}];" +
                $"source-count={sourceCurrent.Rows.Count};target-count={targetCurrent.Rows.Count};" +
                $"source-anchor-id-preserved={sourceAnchorPreserved};target-anchor-id-preserved={targetAnchorPreserved};" +
                $"moved-source-rows-retired={movedSourceRowsRetired};moved-target-rows-added={movedTargetRowsAdded};" +
                $"source-selection-focus-preserved={sourceSelectionPreserved};target-selection-focus-preserved={targetSelectionPreserved};" +
                $"hidden-windows={isolated};foreground-unchanged={GetForegroundWindow() == foreground}");
            if (!shellMoveNotified || (crossVolume && (!sourceDeletedNotification || !targetCreatedNotification)) ||
                sourceCurrent.Rows.Count != 1 || targetCurrent.Rows.Count != 3 ||
                !sourceAnchorPreserved || !targetAnchorPreserved || !movedSourceRowsRetired || !movedTargetRowsAdded ||
                !sourceSelectionPreserved || !targetSelectionPreserved || !isolated)
                throw new InvalidOperationException("Cross-pane move did not reconcile both Shell row models while retaining unaffected selection/focus.");

            var targetCopiedRowIds = movedNames.ToDictionary(name => name,
                name => targetCurrent.Rows.Single(row => row.DisplayName == name).Id, StringComparer.Ordinal);
            var copyEventBaseline = window.Events.Count;
            var copySourceRows = targetCurrent.Rows.Where(row => movedNames.Contains(row.DisplayName, StringComparer.Ordinal)).ToArray();
            var copyChildArray = Marshal.AllocCoTaskMem(IntPtr.Size * copySourceRows.Length);
            nint copyDataObject = 0, copyOperation = 0, copyDestinationItem = 0;
            try
            {
                for (var index = 0; index < copySourceRows.Length; index++)
                    Marshal.WriteIntPtr(copyChildArray, index * IntPtr.Size, copySourceRows[index].RelativePidl);
                var copyDataObjectId = DataObjectId;
                Marshal.ThrowExceptionForHR(At<GetUIObjectOf>(targetFolder, 10)(targetFolder, 0,
                    (uint)copySourceRows.Length, copyChildArray, in copyDataObjectId, 0, out copyDataObject));
                if (copyDataObject == 0) throw new IOException("The target Shell folder returned no IDataObject for the selected copy rows.");

                var copyOperationClassId = FileOperationClassId;
                var copyOperationId = FileOperationId;
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref copyOperationClassId, 0, 1,
                    ref copyOperationId, out copyOperation));
                Marshal.ThrowExceptionForHR(At<SetOperationFlags>(copyOperation, 5)(copyOperation,
                    0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                var copyShellItemId = ShellItemId;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(sourcePath, 0,
                    ref copyShellItemId, out copyDestinationItem));
                Marshal.ThrowExceptionForHR(At<CopyItems>(copyOperation, 17)(copyOperation,
                    copyDataObject, copyDestinationItem));
                Marshal.ThrowExceptionForHR(At<PerformOperations>(copyOperation, 21)(copyOperation));
                Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(copyOperation, 22)(copyOperation, out var copyAborted));
                if (copyAborted != 0) throw new IOException("The isolated cross-pane Shell copy was aborted.");
            }
            finally
            {
                if (copyDestinationItem != 0) Marshal.Release(copyDestinationItem);
                if (copyOperation != 0) Marshal.Release(copyOperation);
                if (copyDataObject != 0) Marshal.Release(copyDataObject);
                Marshal.FreeCoTaskMem(copyChildArray);
            }

            var filesCopied = movedNames.All(name => File.Exists(Path.Combine(sourcePath, name)) &&
                File.ReadAllText(Path.Combine(sourcePath, name)) == (name == "moving.txt" ? "move me" : "move me too"));
            if (!filesCopied)
                throw new IOException("IFileOperation.CopyItems did not produce the expected cross-pane fixture rows.");
            pendingSource = WaitForRows(sourcePidl, sourceFolder,
                rows => movedNames.All(name => rows.Any(row => row.DisplayName == name)));
            var copyNotificationObserved = window.WaitForAnySince(copyEventBaseline,
                [Created, UpdatedDirectory], TimeSpan.FromSeconds(5));
            var copyReconciliation = ShellListViewRowModel.Reconcile(sourceCurrent.Rows,
                pendingSource.Rows, NextId(sourceCurrent.Rows));
            ShellListViewEnumerator.FreeRows(copyReconciliation.RetiredRows);
            sourceCurrent = pendingSource with { Rows = copyReconciliation.Rows.ToList() };
            pendingSource = null;
            ProjectRows(sourceList, sourceCurrent.Rows);
            ApplySelection(sourceList, sourceCurrent.Rows, sourceSelection);
            var sourceAnchorPreservedAfterCopy = sourceCurrent.Rows.Single(row => row.DisplayName == "anchor-source.txt").Id == sourceAnchorId;
            var targetAnchorPreservedAfterCopy = targetCurrent.Rows.Single(row => row.DisplayName == "anchor-target.txt").Id == targetAnchorId;
            var targetCopiedRowsUnchanged = movedNames.All(name =>
                targetCurrent.Rows.Single(row => row.DisplayName == name).Id == targetCopiedRowIds[name]);
            var sourceSelectionPreservedAfterCopy = sourceSelection.Resolve(sourceCurrent.Rows)
                .Any(state => state.RowId == sourceAnchorId && state.Selected && state.Focused) &&
                HasStableListViewSelection(sourceList, sourceAnchorId);
            var targetSelectionPreservedAfterCopy = targetSelection.Resolve(targetCurrent.Rows)
                .Any(state => state.RowId == targetAnchorId && state.Selected && state.Focused) &&
                HasStableListViewSelection(targetList, targetAnchorId);
            var copyIsolated = GetForegroundWindow() == foreground && !IsWindowVisible(sourceParent) &&
                !IsWindowVisible(sourceList) && !IsWindowVisible(targetParent) && !IsWindowVisible(targetList);
            Console.WriteLine($"shell-change-cross-pane-copy: operation=IFileOperation.CopyItems;create-notification={copyNotificationObserved};" +
                $"source-count={sourceCurrent.Rows.Count};target-count={targetCurrent.Rows.Count};" +
                $"source-anchor-id-preserved={sourceAnchorPreservedAfterCopy};target-anchor-id-preserved={targetAnchorPreservedAfterCopy};" +
                $"target-copied-rows-stable={targetCopiedRowsUnchanged};source-selection-focus-preserved={sourceSelectionPreservedAfterCopy};" +
                $"target-selection-focus-preserved={targetSelectionPreservedAfterCopy};hidden-windows={copyIsolated};foreground-unchanged={GetForegroundWindow() == foreground}");
            if (!copyNotificationObserved || sourceCurrent.Rows.Count != 3 || targetCurrent.Rows.Count != 3 ||
                !sourceAnchorPreservedAfterCopy || !targetAnchorPreservedAfterCopy || !targetCopiedRowsUnchanged ||
                !sourceSelectionPreservedAfterCopy || !targetSelectionPreservedAfterCopy || !copyIsolated)
                throw new InvalidOperationException("Cross-pane copy did not reconcile the receiving row model while preserving the source and pane selection/focus.");

            var renameSourceRow = targetCurrent.Rows.Single(row => row.DisplayName == movedNames[0]);
            var renameTargetName = "copy-renamed.txt";
            var renameEventBaseline = window.Events.Count;
            var renameChild = Marshal.AllocCoTaskMem(IntPtr.Size);
            nint renameDataObject = 0, renameOperation = 0;
            try
            {
                Marshal.WriteIntPtr(renameChild, renameSourceRow.RelativePidl);
                var renameDataObjectId = DataObjectId;
                Marshal.ThrowExceptionForHR(At<GetUIObjectOf>(targetFolder, 10)(targetFolder, 0,
                    1, renameChild, in renameDataObjectId, 0, out renameDataObject));
                if (renameDataObject == 0) throw new IOException("The target Shell folder returned no IDataObject for the selected rename row.");

                var renameOperationClassId = FileOperationClassId;
                var renameOperationId = FileOperationId;
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref renameOperationClassId, 0, 1,
                    ref renameOperationId, out renameOperation));
                Marshal.ThrowExceptionForHR(At<SetOperationFlags>(renameOperation, 5)(renameOperation,
                    0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                Marshal.ThrowExceptionForHR(At<RenameItems>(renameOperation, 13)(renameOperation,
                    renameDataObject, renameTargetName));
                Marshal.ThrowExceptionForHR(At<PerformOperations>(renameOperation, 21)(renameOperation));
                Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(renameOperation, 22)(renameOperation, out var renameAborted));
                if (renameAborted != 0) throw new IOException("The isolated cross-pane Shell rename was aborted.");
            }
            finally
            {
                if (renameOperation != 0) Marshal.Release(renameOperation);
                if (renameDataObject != 0) Marshal.Release(renameDataObject);
                Marshal.FreeCoTaskMem(renameChild);
            }

            if (!File.Exists(Path.Combine(targetPath, renameTargetName)) ||
                File.Exists(Path.Combine(targetPath, movedNames[0])))
                throw new IOException("IFileOperation.RenameItems did not rename the isolated copied row as requested.");
            var renameNotificationObserved = window.WaitForAnySince(renameEventBaseline,
                [RenamedItem, UpdatedDirectory], TimeSpan.FromSeconds(5));
            pendingTarget = WaitForRows(targetPidl, targetFolder,
                rows => rows.Any(row => row.DisplayName == renameTargetName) &&
                    rows.All(row => row.DisplayName != movedNames[0]));
            var renameReconciliation = ShellListViewRowModel.Reconcile(targetCurrent.Rows,
                pendingTarget.Rows, NextId(targetCurrent.Rows), window.RenamedParsingNames);
            // Reconcile returns the entire old snapshot for PIDL release; row identity is asserted separately.
            var renamedRowIdPreserved = renameReconciliation.Rows.Single(row => row.DisplayName == renameTargetName).Id == renameSourceRow.Id;
            ShellListViewEnumerator.FreeRows(renameReconciliation.RetiredRows);
            targetCurrent = pendingTarget with { Rows = renameReconciliation.Rows.ToList() };
            pendingTarget = null;
            ProjectRows(targetList, targetCurrent.Rows);
            ApplySelection(targetList, targetCurrent.Rows, targetSelection);
            var targetAnchorPreservedAfterRename = targetCurrent.Rows.Single(row => row.DisplayName == "anchor-target.txt").Id == targetAnchorId;
            var targetSelectionPreservedAfterRename = targetSelection.Resolve(targetCurrent.Rows)
                    .Any(state => state.RowId == targetAnchorId && state.Selected && state.Focused) &&
                HasStableListViewSelection(targetList, targetAnchorId);
            var renameIsolated = GetForegroundWindow() == foreground && !IsWindowVisible(sourceParent) &&
                !IsWindowVisible(sourceList) && !IsWindowVisible(targetParent) && !IsWindowVisible(targetList);
            Console.WriteLine($"shell-change-cross-pane-rename: operation=IFileOperation.RenameItems;rename-notification={renameNotificationObserved};" +
                $"events=[{string.Join(',', window.Events.Skip(renameEventBaseline).Select(value => $"0x{value:X8}"))}];" +
                $"target-count={targetCurrent.Rows.Count};row-id-preserved={renamedRowIdPreserved};" +
                $"target-anchor-id-preserved={targetAnchorPreservedAfterRename};target-selection-focus-preserved={targetSelectionPreservedAfterRename};" +
                $"hidden-windows={renameIsolated};foreground-unchanged={GetForegroundWindow() == foreground}");
            if (!renameNotificationObserved || targetCurrent.Rows.Count != 3 || !renamedRowIdPreserved ||
                !targetAnchorPreservedAfterRename || !targetSelectionPreservedAfterRename || !renameIsolated)
                throw new InvalidOperationException("Cross-pane RenameItems did not reconcile the receiving row model after Shell rename notifications.");

            var copiedNamesAfterRename = new[] { renameTargetName, movedNames[1] };
            foreach (var row in targetCurrent.Rows.Where(row => copiedNamesAfterRename.Contains(row.DisplayName, StringComparer.Ordinal)))
                SetRowState(targetList, FindIndexById(targetList, row.Id), LvisSelected, LvisSelected);
            var deleteSelection = CaptureSelection(targetList, targetCurrent.Rows);
            var deleteEventBaseline = window.Events.Count;
            var deleteRows = targetCurrent.Rows.Where(row => copiedNamesAfterRename.Contains(row.DisplayName, StringComparer.Ordinal)).ToArray();
            var deleteRowIds = deleteRows.Select(row => row.Id).ToArray();
            var deleteChildArray = Marshal.AllocCoTaskMem(IntPtr.Size * deleteRows.Length);
            nint deleteDataObject = 0, deleteOperation = 0;
            try
            {
                for (var index = 0; index < deleteRows.Length; index++)
                    Marshal.WriteIntPtr(deleteChildArray, index * IntPtr.Size, deleteRows[index].RelativePidl);
                var deleteDataObjectId = DataObjectId;
                Marshal.ThrowExceptionForHR(At<GetUIObjectOf>(targetFolder, 10)(targetFolder, 0,
                    (uint)deleteRows.Length, deleteChildArray, in deleteDataObjectId, 0, out deleteDataObject));
                if (deleteDataObject == 0) throw new IOException("The target Shell folder returned no IDataObject for the selected delete rows.");

                var deleteOperationClassId = FileOperationClassId;
                var deleteOperationId = FileOperationId;
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref deleteOperationClassId, 0, 1,
                    ref deleteOperationId, out deleteOperation));
                Marshal.ThrowExceptionForHR(At<SetOperationFlags>(deleteOperation, 5)(deleteOperation,
                    0x0004 | 0x0008 | 0x0010 | 0x0400 | 0x00800000));
                Marshal.ThrowExceptionForHR(At<DeleteItems>(deleteOperation, 19)(deleteOperation, deleteDataObject));
                Marshal.ThrowExceptionForHR(At<PerformOperations>(deleteOperation, 21)(deleteOperation));
                Marshal.ThrowExceptionForHR(At<GetAnyOperationsAborted>(deleteOperation, 22)(deleteOperation, out var deleteAborted));
                if (deleteAborted != 0) throw new IOException("The isolated cross-pane Shell delete was aborted.");
            }
            finally
            {
                if (deleteOperation != 0) Marshal.Release(deleteOperation);
                if (deleteDataObject != 0) Marshal.Release(deleteDataObject);
                Marshal.FreeCoTaskMem(deleteChildArray);
            }

            if (deleteRows.Any(row => File.Exists(Path.Combine(targetPath, row.DisplayName))))
                throw new IOException("IFileOperation.DeleteItems did not remove every copied fixture row.");
            var deleteNotificationObserved = window.WaitForAnySince(deleteEventBaseline,
                [Deleted, UpdatedDirectory], TimeSpan.FromSeconds(5));
            pendingTarget = WaitForRows(targetPidl, targetFolder,
                rows => copiedNamesAfterRename.All(name => rows.All(row => row.DisplayName != name)));
            var deletedReconciliation = ShellListViewRowModel.Reconcile(targetCurrent.Rows,
                pendingTarget.Rows, NextId(targetCurrent.Rows));
            var deletedCopiedRowIdsRetired = deleteRowIds.Length == 2 &&
                deleteRowIds.All(id => deletedReconciliation.RetiredRows.Any(row => row.Id == id));
            ShellListViewEnumerator.FreeRows(deletedReconciliation.RetiredRows);
            targetCurrent = pendingTarget with { Rows = deletedReconciliation.Rows.ToList() };
            pendingTarget = null;
            ProjectRows(targetList, targetCurrent.Rows);
            ApplySelection(targetList, targetCurrent.Rows, deleteSelection);
            var targetAnchorPreservedAfterDelete = targetCurrent.Rows.Single(row => row.DisplayName == "anchor-target.txt").Id == targetAnchorId;
            var targetSelectionPreservedAfterDelete = deleteSelection.Resolve(targetCurrent.Rows)
                    .Any(state => state.RowId == targetAnchorId && state.Selected && state.Focused) &&
                HasStableListViewSelection(targetList, targetAnchorId);
            var sourceUnaffectedByTargetDelete = sourceCurrent.Rows.Count == 3 &&
                movedNames.All(name => File.Exists(Path.Combine(sourcePath, name)) &&
                    sourceCurrent.Rows.Any(row => row.DisplayName == name));
            var deleteIsolated = GetForegroundWindow() == foreground && !IsWindowVisible(sourceParent) &&
                !IsWindowVisible(sourceList) && !IsWindowVisible(targetParent) && !IsWindowVisible(targetList);
            Console.WriteLine($"shell-change-cross-pane-delete: operation=IFileOperation.DeleteItems;delete-notification={deleteNotificationObserved};" +
                $"events=[{string.Join(',', window.Events.Skip(deleteEventBaseline).Select(value => $"0x{value:X8}"))}];" +
                $"source-count={sourceCurrent.Rows.Count};target-count={targetCurrent.Rows.Count};" +
                $"copied-rows-retired={deletedCopiedRowIdsRetired};target-anchor-id-preserved={targetAnchorPreservedAfterDelete};" +
                $"target-selection-focus-preserved={targetSelectionPreservedAfterDelete};source-unchanged={sourceUnaffectedByTargetDelete};" +
                $"hidden-windows={deleteIsolated};foreground-unchanged={GetForegroundWindow() == foreground}");
            if (!deleteNotificationObserved || targetCurrent.Rows.Count != 1 || !deletedCopiedRowIdsRetired ||
                !targetAnchorPreservedAfterDelete || !targetSelectionPreservedAfterDelete || !sourceUnaffectedByTargetDelete || !deleteIsolated)
                throw new InvalidOperationException("Cross-pane DeleteItems did not reconcile the receiving row model after Shell deletion notifications.");
        }
        finally
        {
            if (registration != 0) _ = SHChangeNotifyDeregister(registration);
            foreach (var extraRegistration in additionalRegistrations) _ = SHChangeNotifyDeregister(extraRegistration);
            if (sourceParent != 0) _ = DestroyWindow(sourceParent);
            if (targetParent != 0) _ = DestroyWindow(targetParent);
            if (pendingSource is not null) ShellListViewEnumerator.FreeRows(pendingSource.Rows);
            if (pendingTarget is not null) ShellListViewEnumerator.FreeRows(pendingTarget.Rows);
            if (sourceCurrent is not null) ShellListViewEnumerator.FreeRows(sourceCurrent.Rows);
            if (targetCurrent is not null) ShellListViewEnumerator.FreeRows(targetCurrent.Rows);
            if (sourceFolder != 0) Marshal.Release(sourceFolder);
            if (targetFolder != 0) Marshal.Release(targetFolder);
            if (fixturePidl != 0) ILFree(fixturePidl);
            if (sourcePidl != 0) ILFree(sourcePidl);
            if (targetPidl != 0) ILFree(targetPidl);
            try { Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (crossVolume)
            {
                try { Directory.Delete(sourceFixture, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
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

    private static ShellListViewEnumeration WaitForRows(nint folderPidl, nint shellFolder,
        Func<IReadOnlyList<ShellListViewRow>, bool> predicate)
    {
        var until = Environment.TickCount64 + 5000;
        do
        {
            var enumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
            if (predicate(enumeration.Rows)) return enumeration;
            ShellListViewEnumerator.FreeRows(enumeration.Rows);
            Application.DoEvents();
            Thread.Sleep(30);
        } while (Environment.TickCount64 < until);
        throw new TimeoutException("The Shell folder enumeration did not reflect the complete multi-item move.");
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

    private static bool HasTwoRowSelection(nint listView, long focusedId, long otherSelectedId)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        var selectedIds = new HashSet<long>();
        long focusedRowId = 0;
        for (var index = 0; index < count; index++)
        {
            var rowId = GetItemId(listView, index);
            var state = unchecked((uint)SendMessageW(listView, LvmGetItemState, index,
                (nint)(LvisSelected | LvisFocused)));
            if ((state & LvisSelected) != 0) selectedIds.Add(rowId);
            if ((state & LvisFocused) != 0) focusedRowId = rowId;
        }
        return selectedIds.SetEquals([focusedId, otherSelectedId]) && focusedRowId == focusedId;
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

    private static bool PathEquals(string? actual, string expected) =>
        actual is not null && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

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

    internal sealed class ChangeWindow : NativeWindow, IDisposable
    {
        private readonly List<uint> _events = [];
        private readonly List<ShellChangeEvent> _changeEvents = [];
        private readonly Dictionary<string, string> _renamedParsingNames = new(StringComparer.OrdinalIgnoreCase);
        private uint _registration;
        internal IReadOnlyList<uint> Events => _events;
        internal IReadOnlyList<ShellChangeEvent> ChangeEvents => _changeEvents;
        internal IReadOnlyDictionary<string, string> RenamedParsingNames => _renamedParsingNames;

        internal ChangeWindow()
        {
            CreateHandle(new CreateParams { Caption = "ExplorerDirs Shell change probe", Parent = new nint(-3) });
        }

        internal void Register(nint folderPidl)
        {
            if (_registration != 0) throw new InvalidOperationException("The Shell change window is already registered.");
            var watch = new ShellChangeEntry { Pidl = folderPidl, Recursive = true };
            _registration = SHChangeNotifyRegister(Handle, ShellAndInterruptSources, WatchEvents,
                ChangeMessage, 1, ref watch);
            if (_registration == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SHChangeNotifyRegister failed.");
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

        internal bool WaitForAny(uint[] expected, TimeSpan timeout)
        {
            var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < until)
            {
                Application.DoEvents();
                if (_events.Any(expected.Contains)) return true;
                Thread.Sleep(10);
            }
            return _events.Any(expected.Contains);
        }

        internal bool WaitForAnySince(int eventIndex, uint[] expected, TimeSpan timeout)
        {
            var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < until)
            {
                Application.DoEvents();
                if (_events.Skip(eventIndex).Any(expected.Contains)) return true;
                Thread.Sleep(10);
            }
            return _events.Skip(eventIndex).Any(expected.Contains);
        }

        internal bool WaitFor(Func<bool> predicate, TimeSpan timeout)
        {
            var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < until)
            {
                Application.DoEvents();
                if (predicate()) return true;
                Thread.Sleep(10);
            }
            return predicate();
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
                        var id = unchecked((uint)eventId);
                        _events.Add(id);
                        var firstPidl = pidls == 0 ? 0 : Marshal.ReadIntPtr(pidls);
                        var secondPidl = pidls == 0 ? 0 : Marshal.ReadIntPtr(pidls, IntPtr.Size);
                        _changeEvents.Add(new ShellChangeEvent(id, AbsoluteParsingName(firstPidl), AbsoluteParsingName(secondPidl)));
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

        public void Dispose()
        {
            if (_registration != 0) _ = SHChangeNotifyDeregister(_registration);
            _registration = 0;
            DestroyHandle();
        }
    }

    internal sealed record ShellChangeEvent(uint EventId, string? FirstPath, string? SecondPath);

    [StructLayout(LayoutKind.Sequential)] private struct ShellChangeEntry { public nint Pidl; [MarshalAs(UnmanagedType.Bool)] public bool Recursive; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUIObjectOf(nint self, nint owner, uint count, nint items, in Guid iid, nint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOperationFlags(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MoveItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int RenameItems(nint self, nint dataObject, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyItems(nint self, nint dataObject, nint destination);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeleteItems(nint self, nint dataObject);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PerformOperations(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAnyOperationsAborted(nint self, out int aborted);

    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string name, nint bindContext, ref Guid interfaceId, out nint item);
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
