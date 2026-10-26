using System.Runtime.InteropServices;
using System.IO.Compression;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

// Hidden direct-IShellView probe for the ICommDlgBrowser3 filtering route.
// It uses only a unique fixture and never creates ExplorerBrowser or user files.
internal static class ShellViewHostFilterProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid PropertyFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid ColumnManagerId = new("D8EC27BB-3F3B-4042-B10A-4ACFD924D453");
    private const uint SbspParent = 0x2000;
    private const uint SbspNavigateBack = 0x4000;
    private const uint SbspNavigateForward = 0x8000;

    internal static int Run() => Run(stopAfterGroupedExport: false, leaveParentForThreadExit: false);

    internal static int RunGroupedExportOnly() => Run(stopAfterGroupedExport: true, leaveParentForThreadExit: false);

    internal static int RunForPrivateDesktop() => Run(stopAfterGroupedExport: false, leaveParentForThreadExit: true);

    internal static int RunMenuCommandsForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, menuCommandsOnly: true);

    internal static int RunNewFolderForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, newFolderOnly: true);

    internal static int RunVirtualScopeForPrivateDesktop(string scope) =>
        Run(stopAfterGroupedExport: false, leaveParentForThreadExit: true, virtualScopeOnly: scope);

    private static int Run(bool stopAfterGroupedExport, bool leaveParentForThreadExit, string? virtualScopeOnly = null,
        bool menuCommandsOnly = false, bool newFolderOnly = false)
    {
        var foreground = GetForegroundWindow();
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
        string? zipPath = null;
        nint parent = 0, folderPidl = 0, childFolderPidl = 0, grandchildFolderPidl = 0, alternateFolderPidl = 0, volumeRootPidl = 0, virtualFolderPidl = 0, searchFolderPidl = 0, filePidl = 0, folder = 0, view = 0, viewPointer = 0, viewWindow = 0, folderView = 0;
        bool created = false;
        FilteredHost? host = null;
        Exception? navigationError = null;

        int Navigate(nint targetPidl, uint flags)
        {
            nint childFolder = 0, childView = 0, childWindow = 0;
            var previousView = view;
            try
            {
                Check(SHBindToObject(0, targetPidl, 0, in ShellFolderId, out childFolder));
                var shellViewId = ShellViewId;
                Check(At<CreateViewObject>(childFolder, 8)(childFolder, parent, ref shellViewId, out childView));
                host!.View = childView;
                var settings = new StreamProbeSettings { Mode = uint.MaxValue };
                var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
                var hr = At<CreateViewWindow>(childView, 9)(childView, previousView, ref settings, viewPointer, ref bounds, out childWindow);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                host.SetPendingView(childFolder, childView, childWindow);
                return 0;
            }
            catch (Exception ex)
            {
                navigationError = ex;
                host!.View = previousView;
                if (childWindow != 0) _ = At<DestroyViewWindow>(childView, 10)(childView);
                if (childView != 0) Marshal.Release(childView);
                if (childFolder != 0) Marshal.Release(childFolder);
                return Marshal.GetHRForException(ex);
            }
        }
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            File.WriteAllText(Path.Combine(fixture, "child", "inside.txt"), "inside");
            Check(SHParseDisplayName(Path.Combine(fixture, "child"), 0, out childFolderPidl, 0, out _));
            File.WriteAllText(Path.Combine(fixture, "keep.txt"), "keep");
            Check(SHParseDisplayName(Path.Combine(fixture, "keep.txt"), 0, out filePidl, 0, out _));
            Check(SHParseDisplayName(Path.GetPathRoot(Environment.SystemDirectory)!, 0, out volumeRootPidl, 0, out _));
            var volumeParentPidl = TryGetParentPidl(volumeRootPidl);
            if (volumeParentPidl == 0)
                throw new Exception("The Shell drive root did not resolve its namespace parent.");
            Marshal.FreeCoTaskMem(volumeParentPidl);
            if (TryGetParentPidl(0) != 0)
                throw new Exception("The Shell desktop namespace root unexpectedly produced a parent PIDL.");
            Console.WriteLine("ishellview-host-filter: shell-parent-boundary=True; drive-root-parent=available; desktop-root-parent=none");
            File.WriteAllText(Path.Combine(fixture, "filter.bin"), "filter");
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden ICommDlgBrowser3 filter probe",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0); // WS_POPUP; never shown.
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            var shellFolderId = ShellFolderId;
            Check(SHBindToObject(0, folderPidl, 0, in shellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));

            host = new FilteredHost(parent, new ShellFolderFilter(new QuickFilterSettings("*.txt", false, false)), folderPidl, childFolderPidl, Navigate);
            List<uint> controlWindowRequests = [];
            List<string?> statusTextChanges = [];
            if (newFolderOnly)
            {
                host.ControlWindowProvider = id => { controlWindowRequests.Add(id); return 0; };
                host.StatusTextChanged += text => statusTextChanges.Add(text);
            }
            host.View = view;
            viewPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, viewPointer, ref bounds, out viewWindow));
            created = true;
            var folderViewId = FolderViewId;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int count;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count)); // SVGIO_ALLVIEW
                if (count == 2 && host.IncludeObjectCalls >= 3) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: includecalls={host.IncludeObjectCalls}; filtercalls={host.Filter.ShouldShowCalls}; visible-items={count}; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            var passed = host.IncludeObjectCalls >= 3 && host.Filter.ShouldShowCalls >= 3 && count == 2 &&
                !IsWindowVisible(parent) && GetForegroundWindow() == foreground;
            if (!passed)
                throw new Exception("Direct IShellView did not apply the ICommDlgBrowser3 filter to the isolated fixture.");
            if (virtualScopeOnly is not null)
            {
                var scope = virtualScopeOnly switch
                {
                    "Desktop" => "shell:Desktop",
                    "ThisPC" => "shell:MyComputerFolder",
                    "RecycleBin" => "shell:RecycleBinFolder",
                    "Libraries" => "shell:Libraries",
                    "NetworkPlaces" => "shell:NetworkPlacesFolder",
                    "SearchFolder" => ShellSearchLocation.BuildUri(fixture, "nested"),
                    "ZipContainer" => CreateZipFixture(),
                    _ => throw new ArgumentOutOfRangeException(nameof(virtualScopeOnly), virtualScopeOnly, "Unknown virtual Shell probe target.")
                };
                Console.WriteLine($"ishellview-host-filter: virtual-probe-start={virtualScopeOnly}");
                BrowseVirtualScope(scope, virtualScopeOnly);
                return 0;
            }
            if (menuCommandsOnly)
            {
                VerifyNativeMenuCommands(view, folderView, parent, foreground);
                return 0;
            }
            if (newFolderOnly)
            {
                _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE, still confined to the Private Desktop.
                Check(At<ActivateView>(view, 7)(view, 1)); // SVUIA_ACTIVATE_FOCUS.
                var newFolderListView = FindChildByClass(viewWindow, "SysListView32");
                if (newFolderListView == 0) throw new Exception("The activated Shell view has no native list control.");
                _ = SetFocus(newFolderListView);
                Application.DoEvents();
                var listFocusBefore = GetFocus() == newFolderListView;
                var before = Directory.GetFileSystemEntries(fixture).Select(entry => Path.GetFileName(entry)!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                var attempts = new List<string>();
                var invocationModes = new[]
                {
                    (Name: "offset-no-ui", Mask: 0x400u, Canonical: false),
                    (Name: "offset", Mask: 0u, Canonical: false),
                    (Name: "ansi-verb", Mask: 0u, Canonical: true)
                };
                foreach (var mode in invocationModes)
                {
                    var invokeHr = InvokeBackgroundVerbRaw(view, parent, "newfolder",
                        invokeMask: mode.Mask, canonicalString: mode.Canonical);
                    var settleUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
                    while (DateTime.UtcNow < settleUntil)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    var afterAttempt = Directory.GetFileSystemEntries(fixture).Select(entry => Path.GetFileName(entry)!)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                    attempts.Add($"{mode.Name}=0x{invokeHr:X8},entries=[{string.Join('|', afterAttempt)}]");
                    if (invokeHr >= 0 || !before.SequenceEqual(afterAttempt, StringComparer.OrdinalIgnoreCase)) break;
                }
                var after = Directory.GetFileSystemEntries(fixture).Select(entry => Path.GetFileName(entry)!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out var afterViewCount));
                var added = after.Except(before, StringComparer.OrdinalIgnoreCase).ToArray();
                var canonicalVerbSucceeded = attempts.Any(attempt => attempt.StartsWith("ansi-verb=0x00000000", StringComparison.Ordinal));
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out var createdSelectionCount));
                Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex));
                var focusedName = focusedIndex >= 0 && focusedIndex < afterViewCount
                    ? ReadVisibleItemNames(folderView, afterViewCount)[focusedIndex]
                    : string.Empty;
                var renameEditor = FindChildByClass(viewWindow, "Edit");
                var renameEditorAfterCreate = renameEditor != 0;
                var focusedWindowClass = new System.Text.StringBuilder(128);
                if (GetFocus() != 0) _ = GetClassNameW(GetFocus(), focusedWindowClass, focusedWindowClass.Capacity);
                if (!IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("The isolated New Folder diagnostic changed the desktop boundary.");
                if (!listFocusBefore || !canonicalVerbSucceeded || added.Length != 1 || afterViewCount != 3 ||
                    !Directory.Exists(Path.Combine(fixture, added[0])))
                    throw new Exception("The focused Shell view's newfolder canonical verb did not create exactly one visible fixture directory.");

                var addedIndex = Array.FindIndex(ReadVisibleItemNames(folderView, afterViewCount),
                    name => name.Equals(added[0], StringComparison.OrdinalIgnoreCase));
                if (addedIndex < 0) throw new Exception("The created folder has no row in the Shell view.");
                Check(At<SelectItem>(folderView, 15)(folderView, addedIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));
                _ = SetFocus(newFolderListView);
                var f2 = new NativeMessage { Window = newFolderListView, Id = 0x0100, WParam = 0x71, LParam = 0x003C0001 };
                var f2Hr = At<TranslateAccelerator>(view, 5)(view, ref f2);
                var renameDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                while (DateTime.UtcNow < renameDeadline && renameEditor == 0)
                {
                    Application.DoEvents();
                    renameEditor = FindChildByClass(viewWindow, "Edit");
                    if (renameEditor == 0) Thread.Sleep(10);
                }
                if (renameEditor == 0) throw new Exception($"Shell TranslateAccelerator F2 did not open an inline rename editor: hr=0x{f2Hr:X8}.");
                var focusInRenameEditor = GetFocus() == renameEditor;
                var arrowResults = new List<string>();
                foreach (var arrow in new[]
                {
                    (Name: "left", Key: 0x25u, LParam: 0x004B0001u),
                    (Name: "up", Key: 0x26u, LParam: 0x00480001u),
                    (Name: "right", Key: 0x27u, LParam: 0x004D0001u),
                    (Name: "down", Key: 0x28u, LParam: 0x00500001u)
                })
                {
                    var message = new NativeMessage { Window = renameEditor, Id = 0x0100, WParam = arrow.Key, LParam = (nint)arrow.LParam };
                    var arrowHr = At<TranslateAccelerator>(view, 5)(view, ref message);
                    Application.DoEvents();
                    arrowResults.Add($"{arrow.Name}=0x{arrowHr:X8},edit-alive={IsWindow(renameEditor)},edit-focus={GetFocus() == renameEditor}");
                    if (!IsWindow(renameEditor) || GetFocus() != renameEditor)
                        throw new Exception($"Shell {arrow.Name} accelerator left the inline rename editor.");
                }
                Console.WriteLine($"ishellview-newfolder-diagnostic: list-focus-before={listFocusBefore}; attempts=[{string.Join(';', attempts)}]; filesystem-before=[{string.Join('|', before)}]; filesystem-after=[{string.Join('|', after)}]; view-items-before=2; view-items-after={afterViewCount}; canonical-verb-succeeded={canonicalVerbSucceeded}; selected-count-after-create={createdSelectionCount}; focused-index-after-create={focusedIndex}; focused-name-after-create={focusedName}; rename-editor-after-create={renameEditorAfterCreate}; focused-window-class-after-create={focusedWindowClass}; f2-hr=0x{f2Hr:X8}; f2-focus-in-edit={focusInRenameEditor}; arrow-routing=[{string.Join(',', arrowResults)}]; get-control-window-ids=[{string.Join(',', controlWindowRequests)}]; status-callbacks=[{string.Join('|', statusTextChanges)}]; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                return 0;
            }
            VerifyGroupedFilteredExport(folderView);
            if (stopAfterGroupedExport)
            {
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Grouped export probe changed the hidden desktop boundary.");
                Console.WriteLine("ishellview-grouped-filter-export: parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            var initialView = view;
            var initialBackHr = host.BrowseObject(0, SbspNavigateBack);
            if (initialBackHr >= 0 || host.View != initialView || host.PendingView != 0 || host.HistoryIndex != 0 || !ILIsEqual(host.CurrentPidl, folderPidl))
                throw new Exception("Back at the first history entry changed the active view or history position.");
            Console.WriteLine("ishellview-host-filter: back-boundary-rejected=True; history-index=0; active-view-preserved=True");
            var childIndex = FindFolderIndex(folderView, folder, count);
            if (childIndex < 0) throw new Exception("The filtered view did not retain its fixture subfolder.");
            Check(At<SelectItem>(folderView, 15)(folderView, childIndex, 1)); // SVSI_SELECT
            var selectedFolderVerbs = ReadContextMenuVerbs(view, 1); // SVGIO_SELECTION
            var selectedFolderHasOpen = selectedFolderVerbs.Contains("open", StringComparer.OrdinalIgnoreCase);
            if (!selectedFolderHasOpen)
                throw new Exception("The selected Shell folder did not expose its native Open context-menu verb.");
            Check(At<SelectItem>(folderView, 15)(folderView, childIndex, 0)); // SVSI_DESELECT
            var itemNames = ReadVisibleItemNames(folderView, count);
            var fileIndex = Array.FindIndex(itemNames, name => name.Equals("keep.txt", StringComparison.OrdinalIgnoreCase));
            if (fileIndex < 0) throw new Exception("The filtered view did not retain its fixture file.");
            Check(At<SelectItem>(folderView, 15)(folderView, fileIndex, 1)); // SVSI_SELECT
            var selectedFileVerbs = ReadContextMenuVerbs(view, 1); // SVGIO_SELECTION
            Check(At<SelectItem>(folderView, 15)(folderView, fileIndex, 0)); // SVSI_DESELECT
            var backgroundVerbs = ReadContextMenuVerbs(view, 0); // SVGIO_BACKGROUND
            var selectedFileHasCopy = selectedFileVerbs.Contains("copy", StringComparer.OrdinalIgnoreCase);
            var backgroundHasRefresh = backgroundVerbs.Contains("refresh", StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"ishellview-host-filter: selected-folder-menu-has-open={selectedFolderHasOpen}; selected-file-menu-has-copy={selectedFileHasCopy}; background-menu-has-refresh={backgroundHasRefresh}");
            if (!selectedFileHasCopy || !backgroundHasRefresh)
                throw new Exception("The native context-menu scopes did not resolve file Copy and background Refresh commands independently.");
            var listView = FindChildByClass(viewWindow, "SysListView32");
            if (listView == 0) throw new Exception("The isolated Shell view has no native file list window.");
            VerifySynchronizedViewModes(folderView, listView, host, viewWindow, parent);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedCount)); // SVGIO_SELECTION
            if (selectedCount != 0) throw new Exception("The fixture view unexpectedly started with a selection.");
            var callbackSelectionVersionBefore = host.CallbackSelectionVersion;
            var keyboardState = new byte[256];
            if (!GetKeyboardState(keyboardState)) throw new Exception("Could not snapshot the isolated probe thread keyboard state.");
            var ctrlDownState = (byte[])keyboardState.Clone();
            ctrlDownState[0x11] |= 0x80; // VK_CONTROL down for the direct Shell accelerator call.
            int selectAllHr;
            try
            {
                var activateHr = At<ActivateView>(view, 6)(view, 2); // SVUIA_ACTIVATE_FOCUS.
                if (activateHr < 0) Marshal.ThrowExceptionForHR(activateHr);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Activating the isolated Shell view crossed the hidden desktop boundary.");
                _ = SetFocus(listView);
                if (GetFocus() != listView) throw new Exception("The isolated Shell view did not accept thread-local list focus.");
                if (!SetKeyboardState(ctrlDownState)) throw new Exception("Could not set the isolated probe thread modifier state.");
                var ctrlA = new NativeMessage { Window = listView, Id = 0x0100, WParam = (nuint)'A', LParam = 0x001E0001 };
                selectAllHr = At<TranslateAccelerator>(view, 5)(view, ref ctrlA);
            }
            finally
            {
                if (!SetKeyboardState(keyboardState)) throw new Exception("Could not restore the isolated probe thread keyboard state.");
            }
            Check(selectAllHr);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            Console.WriteLine($"ishellview-host-filter: accelerator-ctrl-a-hr=0x{selectAllHr:X8}; selection={selectedCount}/{count}; callback-selection-version={callbackSelectionVersionBefore}->{host.CallbackSelectionVersion}; focus={GetFocus()}; list={listView}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (selectedCount != count || host.CallbackSelectionVersion <= callbackSelectionVersionBefore)
                throw new Exception($"IShellView.TranslateAccelerator did not select the complete isolated view: {selectedCount}/{count}.");
            for (var index = 0; index < count; index++)
                Check(At<SelectItem>(folderView, 15)(folderView, index, 0)); // SVSI_DESELECT
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 0) throw new Exception("The Ctrl+A accelerator test did not restore the empty fixture selection.");
            Console.WriteLine($"ishellview-host-filter: translate-accelerator-ctrl-a=True; selected={count}; selection-restored-empty=True; thread-keyboard-state-restored=True");

            // Production calls TranslateAccelerator only after confirming focus is inside
            // _viewWindow; a no-focus WM_COMMAND fallback probe is therefore unreachable.
            _ = SetFocus(listView);
            if (GetFocus() != listView) throw new Exception("The isolated list did not regain focus after the fallback test.");

            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x10u | 0x40000000u)); // SVSI_FOCUSED | SVSI_NOTAKEFOCUS.
            Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusBeforeArrow));
            if (focusBeforeArrow != 0) throw new Exception($"Arrow-key fixture expected first row focus, got {focusBeforeArrow}.");
            var down = new NativeMessage { Window = listView, Id = 0x0100, WParam = 0x28, LParam = 0x00500001 };
            var downHr = At<TranslateAccelerator>(view, 5)(view, ref down);
            Check(downHr);
            Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusAfterArrow));
            if (focusAfterArrow != 1)
                throw new Exception($"IShellView.TranslateAccelerator did not advance the focused item with Down: {focusBeforeArrow}->{focusAfterArrow}, hr=0x{downHr:X8}.");
            for (var index = 0; index < count; index++)
                Check(At<SelectItem>(folderView, 15)(folderView, index, 0)); // Leave the isolated fixture with no selected rows.
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 0) throw new Exception("The arrow accelerator test did not restore the empty fixture selection.");
            Console.WriteLine($"ishellview-host-filter: translate-accelerator-down=True; focus={focusBeforeArrow}->{focusAfterArrow}; selection-restored-empty=True");

            Check(At<SelectItem>(folderView, 15)(folderView, childIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));
            var enter = new NativeMessage { Window = listView, Id = 0x0100, WParam = 0x0D, LParam = 0x001C0001 };
            var keyResult = At<TranslateAccelerator>(view, 5)(view, ref enter); // Same IShellView accelerator path used by ExplorerBrowserHost.
            Application.DoEvents();
            Console.WriteLine($"ishellview-host-filter: translate-accelerator-enter-result={keyResult}; default-command-calls={host.DefaultCommandCalls}; browseobject-calls={host.BrowseObjectCalls}; browse-target-is-child={host.BrowseTargetIsChild}");
            if (keyResult != 0 || host.DefaultCommandCalls != 1 || host.BrowseObjectCalls != 2 || !host.BrowseTargetIsChild)
                throw new Exception("IShellView.TranslateAccelerator did not route Enter on the focused fixture folder through OnDefaultCommand to BrowseObject.");

            if (navigationError is not null) throw new Exception("The isolated host could not create the child Shell view.", navigationError);
            if (host.PendingView == 0) throw new Exception("BrowseObject did not create a child Shell view.");
            if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            created = false;
            if (view != 0) { Marshal.Release(view); view = 0; }
            if (folder != 0) { Marshal.Release(folder); folder = 0; }
            (folder, view, viewWindow) = host.TakePendingView();
            created = true;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
                if (count == 1 && host.IncludeObjectCalls >= 4) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: child-view-items={count}; child-filter-callbacks={host.IncludeObjectCalls - 3}; child-view-created=True; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (count != 1 || host.IncludeObjectCalls < 4 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("The child IShellView did not enumerate its filtered fixture item or changed the hidden desktop boundary.");

            var childView = view;
            var invalidTargetHr = host.BrowseObject(filePidl, 0);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            if (invalidTargetHr >= 0 || host.View != childView || host.PendingView != 0 || count != 1 || host.HistoryIndex != 1 || !ILIsEqual(host.CurrentPidl, childFolderPidl))
                throw new Exception("Rejecting a non-folder navigation target changed the active child view.");
            Console.WriteLine($"ishellview-host-filter: nonfolder-target-rejected=True; active-child-view-preserved=True; history-index={host.HistoryIndex}; items={count}");

            Check(host.BrowseObject(0, SbspParent));
            if (host.PendingView == 0 || !host.BrowseTargetIsRoot)
                throw new Exception("The isolated child view could not request navigation back to its parent.");
            Marshal.Release(folderView);
            folderView = 0;
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            created = false;
            Marshal.Release(view);
            view = 0;
            Marshal.Release(folder);
            folder = 0;
            (folder, view, viewWindow) = host.TakePendingView();
            created = true;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
                if (count == 2 && host.IncludeObjectCalls >= 7) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: parent-return-items={count}; parent-include-calls={host.IncludeObjectCalls - 4}; parent-view-restored=True; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (count != 2 || host.IncludeObjectCalls < 7 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Returning to the parent did not restore its filtered Shell view.");
            Console.WriteLine("ishellview-host-filter: sbsp-parent=True; history-index=2");

            var rootView = view;
            var endForwardHr = host.BrowseObject(0, SbspNavigateForward);
            if (endForwardHr >= 0 || host.View != rootView || host.PendingView != 0 || host.HistoryIndex != 2 || !ILIsEqual(host.CurrentPidl, folderPidl))
                throw new Exception("Forward at the end of the history changed the active view or history position.");
            Console.WriteLine("ishellview-host-filter: forward-boundary-rejected=True; history-index=2; active-view-preserved=True");

            Check(host.BrowseObject(0, SbspNavigateBack));
            AdoptPendingView(expectedItemCount: 1);
            if (!host.BrowseTargetIsChild || !ILIsEqual(host.CurrentPidl, childFolderPidl))
                throw new Exception("SBSP_NAVIGATEBACK did not return to the child folder.");
            Check(host.BrowseObject(0, SbspNavigateForward));
            AdoptPendingView(expectedItemCount: 2);
            if (!host.BrowseTargetIsRoot || !ILIsEqual(host.CurrentPidl, folderPidl))
                throw new Exception("SBSP_NAVIGATEFORWARD did not return to the root folder.");
            Console.WriteLine($"ishellview-host-filter: sbsp-back-forward=True; index={host.HistoryIndex}; items={GetItemCount()}; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            if (host.HistoryIndex != 2 || GetItemCount() != 2 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Back/forward navigation did not restore the expected isolated Shell views.");

            var grandchildPath = Path.Combine(fixture, "child", "grandchild");
            Directory.CreateDirectory(grandchildPath);
            File.WriteAllText(Path.Combine(grandchildPath, "nested.txt"), "nested");
            var alternatePath = Path.Combine(fixture, "child", "alternate");
            Directory.CreateDirectory(alternatePath);
            File.WriteAllText(Path.Combine(alternatePath, "alternate.txt"), "alternate");
            Check(SHParseDisplayName(grandchildPath, 0, out grandchildFolderPidl, 0, out _));
            Check(SHParseDisplayName(alternatePath, 0, out alternateFolderPidl, 0, out _));
            Check(host.BrowseObject(childFolderPidl, 0));
            AdoptPendingView(expectedItemCount: 3);
            var nestedNavigateHr = host.BrowseObject(grandchildFolderPidl, 0);
            if (nestedNavigateHr < 0 || host.PendingView == 0 || !ILIsEqual(host.CurrentPidl, grandchildFolderPidl))
                throw new Exception($"The isolated host rejected a valid nested folder target: hr=0x{nestedNavigateHr:X8}.");
            AdoptPendingView(expectedItemCount: 1);
            Check(host.BrowseObject(0, SbspParent));
            if (!ILIsEqual(host.CurrentPidl, childFolderPidl))
                throw new Exception("SBSP_PARENT did not resolve an arbitrary nested folder to its actual parent.");
            AdoptPendingView(expectedItemCount: 3);
            Check(host.BrowseObject(0, SbspNavigateBack));
            AdoptPendingView(expectedItemCount: 1);
            if (!ILIsEqual(host.CurrentPidl, grandchildFolderPidl))
                throw new Exception("Back after nested parent navigation did not restore the nested folder.");
            Check(host.BrowseObject(0, SbspNavigateForward));
            AdoptPendingView(expectedItemCount: 3);
            if (!ILIsEqual(host.CurrentPidl, childFolderPidl) || host.HistoryIndex != 5)
                throw new Exception("Forward after nested parent navigation did not restore the child folder.");
            Check(host.BrowseObject(0, SbspNavigateBack));
            AdoptPendingView(expectedItemCount: 1);
            if (!ILIsEqual(host.CurrentPidl, grandchildFolderPidl) || host.HistoryIndex != 4)
                throw new Exception("Back before branching did not restore the grandchild history entry.");
            Check(host.BrowseObject(alternateFolderPidl, 0));
            AdoptPendingView(expectedItemCount: 1);
            if (!ILIsEqual(host.CurrentPidl, alternateFolderPidl) || host.HistoryIndex != 5)
                throw new Exception("A new navigation did not replace the old forward history branch.");
            var abandonedForwardHr = host.BrowseObject(0, SbspNavigateForward);
            if (abandonedForwardHr >= 0 || host.PendingView != 0 || host.HistoryIndex != 5 || !ILIsEqual(host.CurrentPidl, alternateFolderPidl))
                throw new Exception("Forward navigation followed an abandoned history branch.");
            Console.WriteLine($"ishellview-host-filter: arbitrary-depth-navigation=True; parent=True; back-forward=True; history-branch-truncated=True; history-index={host.HistoryIndex}; foreground-unchanged={GetForegroundWindow() == foreground}");

            foreach (var (parsingName, label) in new[]
            {
                ("shell:Desktop", "Desktop"),
                ("shell:MyComputerFolder", "ThisPC"),
                ("shell:RecycleBinFolder", "RecycleBin"),
                ("shell:Libraries", "Libraries"),
                ("shell:NetworkPlacesFolder", "NetworkPlaces")
            })
                BrowseVirtualScope(parsingName, label);

            var searchUri = ShellSearchLocation.BuildUri(fixture, "nested");
            searchFolderPidl = ShellSearchLocation.ParsePidl(searchUri);
            Check(host.BrowseObject(searchFolderPidl, 0));
            var searchItemCount = AdoptPendingView(expectedItemCount: null);
            if (!ILIsEqual(host.CurrentPidl, searchFolderPidl))
                throw new Exception("The isolated host did not retain the Search Folder PIDL.");
            Console.WriteLine($"ishellview-host-filter: search-folder-view-created=True; enumerated-items={searchItemCount}; {InspectNativeContracts(readVerbs: true)}");

            BrowseVirtualScope(CreateZipFixture(), "ZipContainer");
            return 0;

            int GetItemCount()
            {
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out var result));
                return result;
            }

            void BrowseVirtualScope(string parsingName, string label)
            {
                TraceVirtualScope($"{label}-parse-before", foreground);
                if (label == "SearchFolder")
                    virtualFolderPidl = ShellSearchLocation.ParsePidl(parsingName);
                else
                    Check(SHParseDisplayName(parsingName, 0, out virtualFolderPidl, 0, out _));
                try
                {
                    TraceVirtualScope($"{label}-browse-before", foreground);
                    Check(host.BrowseObject(virtualFolderPidl, 0));
                    TraceVirtualScope($"{label}-browse-after", foreground);
                    var itemCount = AdoptPendingView(expectedItemCount: null);
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"The isolated host did not retain the {label} PIDL.");
                    Console.WriteLine($"ishellview-host-filter: virtual-shell-view-created=True; target={label}; enumerated-items={itemCount}; {InspectNativeContracts(readVerbs: label != "ZipContainer")}");
                    TraceVirtualScope($"{label}-complete", foreground);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(virtualFolderPidl);
                    virtualFolderPidl = 0;
                }
            }

            string CreateZipFixture()
            {
                zipPath = Path.Combine(fixtureRoot, $"probe-{Guid.NewGuid():N}.zip");
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                using var entry = archive.CreateEntry("archive.txt").Open();
                entry.WriteByte(0x41);
                TraceVirtualScope("zip-fixture-created", foreground);
                return zipPath;
            }

            int AdoptPendingView(int? expectedItemCount)
            {
                if (host.PendingView == 0) throw new Exception("Shell navigation did not create a pending view.");
                if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
                if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
                created = false;
                if (view != 0) { Marshal.Release(view); view = 0; }
                if (folder != 0) { Marshal.Release(folder); folder = 0; }
                (folder, view, viewWindow) = host.TakePendingView();
                created = true;
                Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
                var waitUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                var itemCount = 0;
                do
                {
                    Application.DoEvents();
                    Check(At<ItemCount>(folderView, 7)(folderView, 2, out itemCount));
                    if (itemCount == expectedItemCount) return itemCount;
                    Thread.Sleep(10);
                } while (DateTime.UtcNow < waitUntil);
                if (expectedItemCount is null) return itemCount;
                throw new Exception($"Navigated Shell view did not enumerate expected items: expected={expectedItemCount}, actual={itemCount}.");
            }

            string InspectNativeContracts(bool readVerbs)
            {
                var menuId = ContextMenuId;
                var menuHr = At<GetItemObject>(view, 15)(view, 0, ref menuId, out var contextMenu);
                var dropHr = Marshal.QueryInterface(view, in DropTargetId, out var dropTarget);
                var menuQueryHr = unchecked((int)0x80004005);
                var verbs = new List<string>();
                var coreStates = new List<string>();
                nint menu = 0;
                nint commandBuffer = 0;
                try
                {
                    if (menuHr >= 0 && contextMenu != 0)
                    {
                        menu = CreatePopupMenu();
                        if (menu != 0)
                        {
                            menuQueryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                            if (readVerbs && menuQueryHr >= 0)
                            {
                                commandBuffer = Marshal.AllocHGlobal(512);
                                var commandCount = Math.Min(menuQueryHr & 0xffff, 0x7fff);
                                var getCommandString = At<GetCommandString>(contextMenu, 5);
                                for (uint offset = 0; offset < commandCount; offset++)
                                {
                                    Marshal.WriteInt16(commandBuffer, 0);
                                    if (getCommandString(contextMenu, offset, 4, 0, commandBuffer, 256) == 0)
                                    {
                                        var verb = Marshal.PtrToStringUni(commandBuffer);
                                        if (!string.IsNullOrWhiteSpace(verb))
                                        {
                                            verbs.Add(verb);
                                            if (verb.Equals("view", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("groupby", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("refresh", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("properties", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("empty", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("addnetworkplace", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("savesearch", StringComparison.OrdinalIgnoreCase) ||
                                                verb.Equals("newfolder", StringComparison.OrdinalIgnoreCase))
                                            {
                                                var state = GetMenuState(menu, offset + 1, 0);
                                                var stateName = state == uint.MaxValue ? "unknown" : (state & 3) == 0 ? "enabled" : "disabled";
                                                coreStates.Add($"{verb}:{stateName}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (commandBuffer != 0) Marshal.FreeHGlobal(commandBuffer);
                    if (menu != 0) DestroyMenu(menu);
                    if (contextMenu != 0) Marshal.Release(contextMenu);
                    if (dropTarget != 0) Marshal.Release(dropTarget);
                }
                var verbReport = readVerbs ? $"verbs=[{string.Join(',', verbs)}]" : "verbs=skipped-provider-compatibility";
                return $"background-menu=0x{menuHr:X8}/query=0x{menuQueryHr:X8}/{verbReport}/core-state=[{string.Join(',', coreStates)}];drop-target=0x{dropHr:X8}";
            }

        }
        finally
        {
            TraceCleanup("pending-view-before", foreground);
            host?.DisposePendingView();
            TraceCleanup("pending-view-after", foreground);
            if (created && view != 0)
            {
                TraceCleanup("destroy-view-before", foreground);
                _ = At<DestroyViewWindow>(view, 10)(view);
                TraceCleanup("destroy-view-after", foreground);
            }
            TraceCleanup("release-interfaces-before", foreground);
            if (folderView != 0) Marshal.Release(folderView);
            if (view != 0) Marshal.Release(view);
            if (viewPointer != 0) Marshal.Release(viewPointer);
            host?.Dispose();
            if (folder != 0) Marshal.Release(folder);
            if (grandchildFolderPidl != 0) Marshal.FreeCoTaskMem(grandchildFolderPidl);
            if (alternateFolderPidl != 0) Marshal.FreeCoTaskMem(alternateFolderPidl);
            if (searchFolderPidl != 0) Marshal.FreeCoTaskMem(searchFolderPidl);
            if (virtualFolderPidl != 0) Marshal.FreeCoTaskMem(virtualFolderPidl);
            if (volumeRootPidl != 0) Marshal.FreeCoTaskMem(volumeRootPidl);
            if (childFolderPidl != 0) Marshal.FreeCoTaskMem(childFolderPidl);
            if (filePidl != 0) Marshal.FreeCoTaskMem(filePidl);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            TraceCleanup("release-interfaces-after", foreground);
            if (!leaveParentForThreadExit && parent != 0 && IsWindow(parent))
            {
                TraceCleanup("destroy-parent-before", foreground);
                DestroyWindow(parent);
                TraceCleanup("destroy-parent-after", foreground);
            }
            else if (leaveParentForThreadExit && parent != 0 && IsWindow(parent))
            {
                // DestroyWindow on the owning window of a non-input Desktop stalls even after
                // the Shell view and child controls are gone; thread exit owns final HWND cleanup.
                TraceCleanup("parent-cleanup-deferred-to-thread-exit", foreground);
            }
            TraceCleanup("fixture-delete-before", foreground);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            if (zipPath is not null && File.Exists(zipPath)) File.Delete(zipPath);
            TraceCleanup("fixture-delete-after", foreground);
            TraceCleanup("keepalive-before", foreground);
            GC.KeepAlive(host);
            TraceCleanup("keepalive-after", foreground);
        }
    }

    private static void TraceCleanup(string phase, nint expectedForeground)
    {
        var foreground = GetForegroundWindow();
        Console.WriteLine($"ishellview-host-filter: cleanup={phase}; foreground=0x{foreground:X}; expected=0x{expectedForeground:X}; foreground-unchanged={foreground == expectedForeground}");
        Console.Out.Flush();
    }

    private static void TraceVirtualScope(string phase, nint expectedForeground)
    {
        var foreground = GetForegroundWindow();
        Console.WriteLine($"ishellview-host-filter: virtual-phase={phase}; foreground=0x{foreground:X}; expected=0x{expectedForeground:X}; foreground-unchanged={foreground == expectedForeground}");
        Console.Out.Flush();
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static nint ClonePidl(nint pidl)
    {
        var size = checked((int)ILGetSize(pidl));
        var clone = Marshal.AllocCoTaskMem(size);
        var bytes = new byte[size];
        Marshal.Copy(pidl, bytes, 0, size);
        Marshal.Copy(bytes, 0, clone, size);
        return clone;
    }

    private static int FindFolderIndex(nint folderView, nint shellFolder, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var hr = At<GetItem>(folderView, 6)(folderView, index, out var itemPidl);
            if (hr < 0 || itemPidl == 0) continue;
            try
            {
                var itemArray = Marshal.AllocHGlobal(nint.Size);
                var attributes = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    Marshal.WriteIntPtr(itemArray, itemPidl);
                    Marshal.WriteInt32(attributes, unchecked((int)0x20000000)); // SFGAO_FOLDER
                    if (At<GetAttributesOf>(shellFolder, 9)(shellFolder, 1, itemArray, attributes) >= 0 &&
                        (unchecked((uint)Marshal.ReadInt32(attributes)) & 0x20000000) != 0)
                        return index;
                }
                finally
                {
                    Marshal.FreeHGlobal(attributes);
                    Marshal.FreeHGlobal(itemArray);
                }
            }
            finally { Marshal.FreeCoTaskMem(itemPidl); }
        }
        return -1;
    }

    private static void VerifyGroupedFilteredExport(nint folderView)
    {
        var view2Id = FolderView2Id;
        Check(Marshal.QueryInterface(folderView, in view2Id, out var view2));
        var typeKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 4 };
        try
        {
            Check(At<SetGroupBy>(view2, 17)(view2, ref typeKey, 1));
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
            if (count != 2) throw new Exception($"Grouped filtered fixture expected 2 visible rows, got {count}.");

            var expectedOrder = ReadVisibleItemNames(folderView, count);
            var allViewSnapshot = ShellFileViewExportCapture.Capture(folderView, quickFilterActive: true);
            var allViewNames = allViewSnapshot.Rows.Select(row => row[0]).ToArray();
            if (!allViewNames.SequenceEqual(expectedOrder, StringComparer.OrdinalIgnoreCase) ||
                !allViewNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["child", "keep.txt"]))
                throw new Exception($"Grouped filtered SVGIO_ALLVIEW export mismatch: shell=[{string.Join('|', expectedOrder)}], export=[{string.Join('|', allViewNames)}].");

            var indexedSnapshot = ShellFileViewExportCapture.CaptureIndexedView(folderView);
            var indexedNames = indexedSnapshot.Rows.Select(row => row[0]).ToArray();
            if (!indexedNames.SequenceEqual(expectedOrder, StringComparer.OrdinalIgnoreCase) ||
                !indexedNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["child", "keep.txt"]))
                throw new Exception($"Grouped filtered indexed-view export mismatch: shell=[{string.Join('|', expectedOrder)}], export=[{string.Join('|', indexedNames)}].");

            Console.WriteLine($"ishellview-host-filter: grouped-filtered-export=True; svgio={string.Join('|', allViewNames)}; indexed={string.Join('|', indexedNames)}");
        }
        finally
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            _ = At<SetGroupBy>(view2, 17)(view2, ref noGroup, 0);
            Marshal.Release(view2);
        }
    }

    private static string[] ReadVisibleItemNames(nint folderView, int count)
    {
        var folderId = ShellFolderId;
        Check(At<GetFolder>(folderView, 5)(folderView, ref folderId, out var folder));
        try
        {
            var names = new string[count];
            for (var index = 0; index < count; index++)
            {
                Check(At<GetItem>(folderView, 6)(folderView, index, out var childPidl));
                try
                {
                    var itemId = ShellItemId;
                    Check(SHCreateItemWithParent(0, folder, childPidl, ref itemId, out var item));
                    try
                    {
                        Check(At<GetItemDisplayName>(item, 5)(item, 0, out var name));
                        try { names[index] = Marshal.PtrToStringUni(name) ?? string.Empty; }
                        finally { Marshal.FreeCoTaskMem(name); }
                    }
                    finally { Marshal.Release(item); }
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            return names;
        }
        finally { Marshal.Release(folder); }
    }

    private static void VerifySynchronizedViewModes(nint folderView, nint listView, FilteredHost host, nint viewWindow, nint parent)
    {
        const int LvmSetView = 0x108E;
        const int LvmGetView = 0x108F;
        var setMode = At<SetViewModeAndIconSize>(folderView, 35);
        var getMode = At<GetViewModeAndIconSize>(folderView, 36);
        var viewPairs = new[]
        {
            (Name: "Icon", ListView: 0, Shell: 1, IconSize: 48), (Name: "Details", ListView: 1, Shell: 4, IconSize: 16),
            (Name: "SmallIcon", ListView: 2, Shell: 2, IconSize: 16), (Name: "List", ListView: 3, Shell: 3, IconSize: 16),
            (Name: "Tile", ListView: 4, Shell: 6, IconSize: 48)
        };
        var results = new List<string>();
        _ = ShowWindow(parent, 4); // SW_SHOWNOACTIVATE; the parent remains on the Private Desktop.
        Check(At<ActivateView>(host.View, 7)(host.View, 1)); // SVUIA_ACTIVATE_FOCUS.
        foreach (var mode in viewPairs)
        {
            var setHr = setMode(folderView, (uint)mode.Shell, mode.IconSize);
            Check(setHr);
            Check(getMode(folderView, out var shellModeAfterSet, out var sizeAfterSet));
            var listViewSetResult = SendMessageW(listView, LvmSetView, (nint)mode.ListView, 0).ToInt32();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int visibleItems;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out visibleItems));
                if (visibleItems == 2) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(getMode(folderView, out var actualShellMode, out var iconSize));
            var actualListViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            var visibleNames = ReadVisibleItemNames(folderView, visibleItems);
            if (actualShellMode != mode.Shell || iconSize != mode.IconSize || actualListViewMode != mode.ListView || visibleItems != 2 ||
                !visibleNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).SequenceEqual(["child", "keep.txt"], StringComparer.OrdinalIgnoreCase))
                throw new Exception($"Synchronized {mode.Name} mode changed the native list layout or filtered contents: set=0x{setHr:X8},shell-immediate={shellModeAfterSet}/{sizeAfterSet},lvm-set={listViewSetResult},shell-final={actualShellMode}/{iconSize},list={actualListViewMode},items={visibleItems},names={string.Join(',', visibleNames)}.");
            var renameArrowResults = VerifyRenameArrowRouting(folderView, listView, viewWindow, host.View, visibleNames, mode.Name);
            results.Add($"{mode.Name}=shell:{actualShellMode}/{iconSize},list:{actualListViewMode},items:{visibleItems},rename-arrows:[{string.Join(',', renameArrowResults)}]");
        }
        // Keep the following linear Down-key probe in Details, where its expected next-row behavior is defined.
        Check(setMode(folderView, 4, 16));
        _ = SendMessageW(listView, LvmSetView, 1, 0);
        Application.DoEvents();
        _ = ShowWindow(parent, 0); // SW_HIDE; restore the probe's original hidden-parent boundary.
        Console.WriteLine("ishellview-host-filter: synchronized-view-modes=" + string.Join('|', results));
    }

    private static IReadOnlyList<string> VerifyRenameArrowRouting(nint folderView, nint listView, nint viewWindow, nint view,
        IReadOnlyList<string> visibleNames, string modeName)
    {
        const uint selectFocusedVisible = 0x1u | 0x4u | 0x10u | 0x40000000u;
        var itemIndex = Array.FindIndex(visibleNames.ToArray(), name => name.Equals("keep.txt", StringComparison.OrdinalIgnoreCase));
        if (itemIndex < 0) throw new Exception($"The {modeName} view has no rename fixture item.");

        Check(At<SelectItem>(folderView, 15)(folderView, itemIndex, selectFocusedVisible));
        _ = SetFocus(listView);
        var f2 = new NativeMessage { Window = listView, Id = 0x0100, WParam = 0x71, LParam = 0x003C0001 };
        var f2Hr = At<TranslateAccelerator>(view, 5)(view, ref f2);
        var renameEditor = FindChildByClass(viewWindow, "Edit");
        var renameDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < renameDeadline && (renameEditor == 0 || GetFocus() != renameEditor))
        {
            Application.DoEvents();
            renameEditor = FindChildByClass(viewWindow, "Edit");
            if (renameEditor == 0 || GetFocus() != renameEditor) Thread.Sleep(10);
        }
        if (renameEditor == 0 || GetFocus() != renameEditor)
        {
            var focusedWindow = GetFocus();
            var focusedClass = new System.Text.StringBuilder(128);
            if (focusedWindow != 0) _ = GetClassNameW(focusedWindow, focusedClass, focusedClass.Capacity);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedCount));
            throw new Exception($"The {modeName} view did not focus its inline rename editor after F2 (hr=0x{f2Hr:X8},editor=0x{renameEditor:X},focus=0x{focusedWindow:X}/{focusedClass},selected={selectedCount},list=0x{listView:X},view=0x{viewWindow:X}).");
        }

        var arrows = new[]
        {
            (Name: "left", Key: 0x25u, LParam: 0x004B0001u),
            (Name: "up", Key: 0x26u, LParam: 0x00480001u),
            (Name: "right", Key: 0x27u, LParam: 0x004D0001u),
            (Name: "down", Key: 0x28u, LParam: 0x00500001u)
        };
        var results = new List<string>(arrows.Length);
        try
        {
            foreach (var arrow in arrows)
            {
                var message = new NativeMessage { Window = renameEditor, Id = 0x0100, WParam = arrow.Key, LParam = (nint)arrow.LParam };
                var hr = At<TranslateAccelerator>(view, 5)(view, ref message);
                Application.DoEvents();
                var retained = IsWindow(renameEditor) && GetFocus() == renameEditor;
                results.Add($"{arrow.Name}:{hr:X8}/{retained}");
                if (hr < 0 || !retained)
                    throw new Exception($"The {modeName} view lost its inline rename editor or focus on {arrow.Name} (hr=0x{hr:X8}).");
            }
        }
        finally
        {
            if (IsWindow(renameEditor))
            {
                var escape = new NativeMessage { Window = renameEditor, Id = 0x0100, WParam = 0x1B, LParam = 0x00010001 };
                _ = At<TranslateAccelerator>(view, 5)(view, ref escape);
                Application.DoEvents();
            }
            Check(At<SelectItem>(folderView, 15)(folderView, itemIndex, 0));
        }
        if (IsWindow(renameEditor)) throw new Exception($"The {modeName} view did not dismiss its cancelled inline rename editor.");
        return results;
    }

    private static void VerifyNativeMenuCommands(nint view, nint folderView, nint owner, nint foreground)
    {
        var getMode = At<GetViewModeAndIconSize>(folderView, 36);
        Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 1, 48));
        Check(getMode(folderView, out var initialMode, out _));
        if (initialMode == 4) throw new Exception("The menu-command fixture unexpectedly started in Details mode.");

        // The preceding grouped-export probe intentionally leaves Type grouping active; reset it so
        // sorting focus checks measure sort behavior rather than a leftover grouped-view state.
        var groupViewId = FolderView2Id;
        Check(Marshal.QueryInterface(folderView, in groupViewId, out var groupView));
        try
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            Check(At<SetGroupBy>(groupView, 17)(groupView, ref noGroup, 0));
        }
        finally { Marshal.Release(groupView); }

        var listView = FindChildByClass(owner, "SysListView32");
        if (listView == 0) throw new Exception("The menu-command fixture has no native file list window.");
        if (At<ItemCount>(folderView, 7)(folderView, 2, out var initialCount) < 0 || initialCount == 0)
            throw new Exception("The menu-command fixture has no visible item to focus.");
        Check(At<GetItem>(folderView, 6)(folderView, 0, out var expectedFocusedPidl));
        var expectedFocusedName = ReadVisibleItemNames(folderView, initialCount)[0];
        List<string> menuCommandFocusRecovery = [];
        uint modeAfterMenu;
        int iconSizeAfterMenu, visibleCount;
        // Sort/group can briefly expose the old item count and PIDL before a second Shell rebuild; require settled focus state before measuring or restoring.
        void RestoreFocusedSelectionAfterMenuCommand(string command)
        {
            var focusSamples = new List<string>();
            (bool Hwnd, bool Item, int Selection, int Items) SampleFocus(string phase)
            {
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectionCount));
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out var itemCount));
                var sample = (Hwnd: GetFocus() == listView, Item: FocusedItemNameMatches(folderView, expectedFocusedName), Selection: selectionCount, Items: itemCount);
                focusSamples.Add($"{phase}:hwnd={sample.Hwnd},item={sample.Item},selection={sample.Selection},items={sample.Items}");
                return sample;
            }

            bool IsExpectedState((bool Hwnd, bool Item, int Selection, int Items) sample) =>
                sample.Hwnd && sample.Item && sample.Selection == 1 && sample.Items == initialCount;
            var settled = SampleFocus("0ms");
            var consecutiveStableSamples = IsExpectedState(settled) ? 1 : 0;
            var sampleNumber = 0;
            var stabilityDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (consecutiveStableSamples < 3 && DateTime.UtcNow < stabilityDeadline)
            {
                var sampleDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
                while (DateTime.UtcNow < sampleDeadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                settled = SampleFocus($"+{++sampleNumber * 50}ms");
                consecutiveStableSamples = IsExpectedState(settled) ? consecutiveStableSamples + 1 : 0;
            }
            var itemPreserved = settled.Item;
            var rawSelectedCount = settled.Selection;
            var hwndFocusPreserved = settled.Hwnd;
            var rawPreserved = consecutiveStableSamples == 3 && IsExpectedState(settled);
            if (!rawPreserved)
            {
                var restoredIndex = FindItemIndexByName(folderView, expectedFocusedName);
                if (restoredIndex < 0)
                    throw new Exception($"The focused fixture item '{expectedFocusedName}' was lost by Shell {command}.");
                Check(At<SelectItem>(folderView, 15)(folderView, restoredIndex,
                    1u | 0x10u | 0x40000000u)); // SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS
                if (GetFocus() != listView) _ = SetFocus(listView);
            }
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedCount));
            if (GetFocus() != listView || !FocusedItemNameMatches(folderView, expectedFocusedName) || selectedCount != 1)
                throw new Exception($"The host could not restore list/item focus after Shell {command}: list-focus={GetFocus() == listView}, selection={selectedCount}.");
            menuCommandFocusRecovery.Add($"{command}:raw={rawPreserved}(hwnd={hwndFocusPreserved},item={itemPreserved},selection={rawSelectedCount}),samples=[{string.Join(';', focusSamples)}],restored=True");
        }
        try
        {
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 1u | 0x10u | 0x40000000u)); // SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS
            _ = SetFocus(listView);
            if (GetFocus() != listView || !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception("The menu-command fixture could not establish its initial list and item focus.");

            InvokeBackgroundVerb(view, owner, "viewlogicaldetails");
            Check(getMode(folderView, out modeAfterMenu, out iconSizeAfterMenu));
            if (modeAfterMenu != 4 || iconSizeAfterMenu != 16)
                throw new Exception($"The Shell background menu did not switch the isolated view to Details: mode={modeAfterMenu}, size={iconSizeAfterMenu}.");
            VerifyColumnHeaderControl(view, owner);
            VerifyColumnHeaderContextMenu(view, owner);
            if (GetFocus() != listView || !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception("Switching view mode through the Shell menu lost the fixture list or focused item.");

            InvokeBackgroundVerb(view, owner, "refresh");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            var stableRefreshSamples = 0;
            var refreshSampleNumber = 0;
            var refreshSamples = new List<string>();
            bool listFocusPreserved = false;
            bool rawRefreshPreservedItem = false;
            int selectedCount = 0;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out visibleCount));
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
                listFocusPreserved = GetFocus() == listView;
                rawRefreshPreservedItem = FocusedItemMatches(folderView, expectedFocusedPidl) && selectedCount == 1;
                var stable = visibleCount == initialCount && listFocusPreserved && rawRefreshPreservedItem;
                stableRefreshSamples = stable ? stableRefreshSamples + 1 : 0;
                refreshSamples.Add($"+{refreshSampleNumber++ * 50}ms:items={visibleCount},hwnd={listFocusPreserved},item={FocusedItemMatches(folderView, expectedFocusedPidl)},selection={selectedCount}");
                if (stableRefreshSamples < 3) Thread.Sleep(50);
            } while (stableRefreshSamples < 3 && DateTime.UtcNow < deadline);
            if (visibleCount != initialCount || IsWindowVisible(owner) || GetForegroundWindow() != foreground)
                throw new Exception($"The Shell background Refresh command changed the fixture or desktop boundary: items={visibleCount}, parent-visible={IsWindowVisible(owner)}, foreground-unchanged={GetForegroundWindow() == foreground}.");
            rawRefreshPreservedItem &= stableRefreshSamples == 3;
            if (!rawRefreshPreservedItem)
            {
                // The raw Shell Refresh verb can clear the focused row even though the HWND retains focus.
                // Model the host's post-refresh recovery using the captured relative PIDL identity.
                var restoredIndex = FindItemIndex(folderView, expectedFocusedPidl);
                if (restoredIndex < 0)
                    throw new Exception("The focused fixture item disappeared from the view after Refresh and could not be restored.");
                Check(At<SelectItem>(folderView, 15)(folderView, restoredIndex,
                    1u | 0x10u | 0x40000000u)); // SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS
            }
            if (!listFocusPreserved || GetFocus() != listView || !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception($"The host could not restore the Shell focused item after Refresh: list={listFocusPreserved}, item={FocusedItemMatches(folderView, expectedFocusedPidl)}, focus-hwnd=0x{GetFocus():X}, list-hwnd=0x{listView:X}.");
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 1)
                throw new Exception($"The host could not restore the selected focused item after Refresh: {selectedCount}.");
            Console.WriteLine($"ishellview-menu-command-focus: raw-refresh-preserved-item={rawRefreshPreservedItem}; host-pidl-restore=True; hwnd-focus-preserved={listFocusPreserved}; samples=[{string.Join(';', refreshSamples)}]");
        }
        finally { Marshal.FreeCoTaskMem(expectedFocusedPidl); }

        var view2Id = FolderView2Id;
        Check(Marshal.QueryInterface(folderView, in view2Id, out var view2));
        try
        {
            var nameKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 10 };
            var sort = new NativeSortColumn { Key = nameKey, Direction = 1 };
            Check(At<SetSortColumns>(view2, 27)(view2, [sort], 1));
            InvokeBackgroundVerb(view, owner, "sortdescending");
            RestoreFocusedSelectionAfterMenuCommand("sortdescending");
            Check(At<GetSortColumnCount>(view2, 26)(view2, out var sortCount));
            if (sortCount != 1) throw new Exception($"The Shell Sort menu changed the column count unexpectedly: {sortCount}.");
            var sorts = new NativeSortColumn[sortCount];
            Check(At<GetSortColumns>(view2, 28)(view2, sorts, sortCount));
            if (sorts[0].Key.FormatId != nameKey.FormatId || sorts[0].Key.PropertyId != nameKey.PropertyId || sorts[0].Direction != -1)
                throw new Exception($"The Shell Sort Descending command did not update the active sort state: key={sorts[0].Key.FormatId:B}/{sorts[0].Key.PropertyId}, direction={sorts[0].Direction}.");
            InvokeBackgroundVerb(view, owner, "sortascending");
            RestoreFocusedSelectionAfterMenuCommand("sortascending");
            Check(At<GetSortColumns>(view2, 28)(view2, sorts, sortCount));
            if (sorts[0].Direction != 1) throw new Exception($"The Shell Sort Ascending command did not restore ascending order: direction={sorts[0].Direction}.");

            var typeKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 4 };
            Check(At<SetGroupBy>(view2, 17)(view2, ref typeKey, 1));
            InvokeBackgroundVerb(view, owner, "groupdescending");
            RestoreFocusedSelectionAfterMenuCommand("groupdescending");
            var groupResult = At<GetGroupBy>(view2, 18)(view2, out var groupKey, out var groupAscending);
            if (groupResult != 0 || groupKey.FormatId != typeKey.FormatId || groupKey.PropertyId != typeKey.PropertyId || groupAscending != 0)
                throw new Exception($"The Shell Group Descending command did not update the active grouping state: hr=0x{groupResult:X8}, key={groupKey.FormatId:B}/{groupKey.PropertyId}, ascending={groupAscending}.");
            InvokeBackgroundVerb(view, owner, "groupascending");
            RestoreFocusedSelectionAfterMenuCommand("groupascending");
            groupResult = At<GetGroupBy>(view2, 18)(view2, out groupKey, out groupAscending);
            if (groupResult != 0 || groupKey.FormatId != typeKey.FormatId || groupKey.PropertyId != typeKey.PropertyId || groupAscending != 1)
                throw new Exception($"The Shell Group Ascending command did not restore ascending order: hr=0x{groupResult:X8}, ascending={groupAscending}.");
        }
        finally
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            _ = At<SetGroupBy>(view2, 17)(view2, ref noGroup, 0);
            Marshal.Release(view2);
        }

        Console.WriteLine($"ishellview-menu-command: viewlogicaldetails=True; mode={modeAfterMenu}/{iconSizeAfterMenu}; refresh=True; sortdescending=True; sortascending=True; groupdescending=True; groupascending=True; focus-recovery=[{string.Join(',', menuCommandFocusRecovery)}]; visible-items={visibleCount}; parent-visible=False; foreground-unchanged=True");
    }

    private static bool FocusedItemMatches(nint folderView, nint expectedPidl)
    {
        if (At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex) < 0 || focusedIndex < 0)
            return false;
        if (At<GetItem>(folderView, 6)(folderView, focusedIndex, out var focusedPidl) < 0 || focusedPidl == 0)
            return false;
        try { return ILIsEqual(focusedPidl, expectedPidl); }
        finally { Marshal.FreeCoTaskMem(focusedPidl); }
    }

    private static void VerifyColumnHeaderControl(nint view, nint owner)
    {
        var listView = FindChildByClass(owner, "SysListView32");
        if (listView == 0) throw new Exception("The Details view has no native list control.");
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        if (header == 0) throw new Exception("The Details view did not expose its native column header control.");
        var headerClass = new System.Text.StringBuilder(64);
        if (GetClassNameW(header, headerClass, headerClass.Capacity) <= 0 || headerClass.ToString() != "SysHeader32")
            throw new Exception($"The Details column header has an unexpected window class: {headerClass}.");
        var headerCount = SendMessageW(header, 0x1200, 0, 0).ToInt32(); // HDM_GETITEMCOUNT
        Check(Marshal.QueryInterface(view, in ColumnManagerId, out var columnManager));
        try
        {
            Check(At<GetShellColumnCount>(columnManager, 5)(columnManager, 2, out var visibleCount)); // CM_ENUM_VISIBLE
            if (headerCount <= 0 || headerCount != visibleCount)
                throw new Exception($"The native header and Shell visible-column counts disagree: header={headerCount}, shell={visibleCount}.");
            Console.WriteLine($"ishellview-column-header: class=SysHeader32; header-items={headerCount}; shell-visible-columns={visibleCount}");
        }
        finally { Marshal.Release(columnManager); }
    }

    private static void VerifyColumnHeaderContextMenu(nint view, nint owner)
    {
        var listView = FindChildByClass(owner, "SysListView32");
        var header = listView == 0 ? 0 : SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        if (header == 0) throw new Exception("Could not locate the Details column header for its isolated context-menu probe.");
        var headerRect = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        var capturedItems = new List<(uint Id, string Text)>();
        var capturedSubmenus = new List<(string Caption, List<(int Id, string Text)> Items)>();
        var initializedSubmenus = new HashSet<nint>();
        nint capturedRootMenu = 0;
        HookProc? hookCallback = null;
        nint hook = 0;
        try
        {
            if (!GetWindowRect(header, headerRect) || SendMessageW(header, 0x1207, 0, headerRect) == 0) // HDM_GETITEMRECT
                throw new Exception("Could not locate a column header hit point.");
            var itemRect = Marshal.PtrToStructure<NativeRect>(headerRect);
            if (!GetWindowRect(header, headerRect)) throw new Exception("Could not read the header's screen bounds.");
            var screenRect = Marshal.PtrToStructure<NativeRect>(headerRect);
            var x = screenRect.Left + (itemRect.Left + itemRect.Right) / 2;
            var y = screenRect.Top + (itemRect.Top + itemRect.Bottom) / 2;
            var threadId = GetCurrentThreadId();
            hookCallback = (code, wParam, lParam) =>
            {
                try
                {
                    if (code >= 0 && lParam != 0)
                    {
                        var message = Marshal.PtrToStructure<CallWindowProcResult>(lParam);
                        if (message.Message == 0x0117 && message.WParam != 0) // WM_INITMENUPOPUP after Shell initialization
                        {
                            var menu = unchecked((nint)message.WParam);
                            if (capturedRootMenu != 0) return CallNextHookEx(hook, code, wParam, lParam);
                            capturedRootMenu = menu;
                            var count = GetMenuItemCount(menu);
                            for (var index = 0; index < count; index++)
                            {
                                var text = new System.Text.StringBuilder(256);
                                if (GetMenuStringW(menu, (uint)index, text, text.Capacity, 0x400) > 0 &&
                                    text.Length > 0)
                                    capturedItems.Add((GetMenuItemID(menu, index), text.ToString()));
                            }
                            // The real popup opens submenus lazily. Ask the same owner to initialize
                            // each child while the tracked root menu is still alive, then read it back.
                            for (var index = 0; index < count; index++)
                            {
                                var submenu = GetSubMenu(menu, index);
                                if (submenu == 0 || !initializedSubmenus.Add(submenu)) continue;
                                var caption = new System.Text.StringBuilder(256);
                                _ = GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400);
                                _ = SendMessageW(message.Window, 0x0117, submenu, (nint)index);
                                var childCount = GetMenuItemCount(submenu);
                                var childItems = new List<(int Id, string Text)>();
                                for (var childIndex = 0; childIndex < childCount; childIndex++)
                                {
                                    var childText = new System.Text.StringBuilder(256);
                                    if (GetMenuStringW(submenu, (uint)childIndex, childText, childText.Capacity, 0x400) > 0 &&
                                        childText.Length > 0)
                                        childItems.Add((unchecked((int)GetMenuItemID(submenu, childIndex)), childText.ToString()));
                                }
                                capturedSubmenus.Add((caption.ToString(), childItems));
                            }
                            _ = EndMenu(); // Close the isolated popup before it can await user input.
                        }
                    }
                }
                catch { _ = EndMenu(); }
                return CallNextHookEx(hook, code, wParam, lParam);
            };
            hook = SetWindowsHookExW(12, hookCallback, 0, threadId); // WH_CALLWNDPROCRET, current probe thread only
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not install the isolated menu observer.");
            var position = unchecked((nint)(((y & 0xffff) << 16) | (x & 0xffff)));
            _ = SendMessageW(header, 0x007B, header, position); // WM_CONTEXTMENU
        }
        finally
        {
            _ = EndMenu();
            if (hook != 0) _ = UnhookWindowsHookEx(hook);
            Marshal.FreeHGlobal(headerRect);
        }

        if (capturedItems.Count < 4)
            throw new Exception($"The native column header did not produce a populated Shell menu on the isolated desktop: items={capturedItems.Count}.");
        var backgroundCaptions = ReadBackgroundMenuCaptions(view);
        var commonCaptions = capturedItems.Select(item => item.Text)
            .Intersect(backgroundCaptions, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (commonCaptions.Length < 5)
            throw new Exception($"The column header popup and background IContextMenu shared too few exact captions: {commonCaptions.Length}.");
        var backgroundCommands = ReadBackgroundMenuCommandDescriptors(view);
        var backgroundSubmenus = ReadBackgroundMenuSubmenus(view);
        var idOffsetMatches = new Dictionary<int, List<string>>();
        foreach (var actual in capturedItems.Where(item => item.Id != uint.MaxValue))
        foreach (var command in backgroundCommands.Where(command =>
                     command.Verb is "refresh" or "paste" or "undo" or "redo" &&
                     string.Equals(command.Caption, actual.Text, StringComparison.OrdinalIgnoreCase)))
        {
            var offset = unchecked((int)actual.Id - command.Id);
            if (!idOffsetMatches.TryGetValue(offset, out var matched)) idOffsetMatches[offset] = matched = [];
            matched.Add(command.Verb);
        }
        var strongestOffset = idOffsetMatches.OrderByDescending(pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count()).FirstOrDefault();
        var mappedVerbs = strongestOffset.Value?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (!mappedVerbs.Contains("refresh", StringComparer.OrdinalIgnoreCase) || mappedVerbs.Length < 2)
            throw new Exception($"Could not map the header popup's standard leaf commands to one background IContextMenu ID base: [{string.Join(',', mappedVerbs)}].");
        var canonicalSubmenuVerbs = backgroundCommands
            .Where(command => command.Caption is "查看(&V)" or "排序方式(&O)" or "分组依据(&P)")
            .Select(command => $"{command.Caption}:{command.Verb}").ToArray();
        var mappedSubmenuRows = new List<string>();
        foreach (var expectedVerb in new[] { "view", "arrange", "groupby" })
        {
            var canonicalCommand = backgroundCommands.FirstOrDefault(command =>
                string.Equals(command.Verb, expectedVerb, StringComparison.OrdinalIgnoreCase));
            if (canonicalCommand is null)
                throw new Exception($"The background IContextMenu did not expose the '{expectedVerb}' submenu command.");
            var expectedCaption = canonicalCommand.Caption;
            var actualSubmenu = capturedSubmenus.FirstOrDefault(submenu => submenu.Caption == expectedCaption);
            var backgroundSubmenu = backgroundSubmenus.FirstOrDefault(submenu => submenu.Caption == expectedCaption);
            if (actualSubmenu.Items is null || backgroundSubmenu.Items is null)
                throw new Exception($"Could not locate both initialized menu trees for the standard submenu '{expectedCaption}'.");
            var matches = from actual in actualSubmenu.Items
                          join queried in backgroundSubmenu.Items on actual.Text equals queried.Text
                          where actual.Id != -1 && queried.Id != -1
                          select unchecked(actual.Id - queried.Id);
            var mappedOffsets = matches.ToArray();
            var offsets = mappedOffsets.Distinct().ToArray();
            if (mappedOffsets.Length != actualSubmenu.Items.Count || mappedOffsets.Length != backgroundSubmenu.Items.Count ||
                offsets.Length != 1 || offsets[0] != strongestOffset.Key)
                throw new Exception($"The '{expectedCaption}' submenu items did not map one-for-one to the background command ID base: mapped={mappedOffsets.Length}, header={actualSubmenu.Items.Count}, background={backgroundSubmenu.Items.Count}, offsets=[{string.Join(',', offsets)}], expected={strongestOffset.Key}.");
            mappedSubmenuRows.Add($"{expectedCaption}:{actualSubmenu.Items.Count(item => item.Id != -1)} items");
        }
        Console.WriteLine($"ishellview-column-header-menu: items=[{string.Join('|', capturedItems.Select(item => item.Text))}]; canonical-submenu-verbs=[{string.Join(',', canonicalSubmenuVerbs)}]; canonical-leaf-verbs=[{string.Join(',', mappedVerbs)}]; mapped-submenus=[{string.Join(',', mappedSubmenuRows)}]; popup-command-id-base-offset={strongestOffset.Key}; popup-cancelled=True");
    }

    private static IReadOnlyList<string> ReadBackgroundMenuCaptions(nint view) =>
        ReadBackgroundMenuCommandDescriptors(view).Select(command => command.Caption).Where(caption => caption.Length > 0).ToArray();

    private static IReadOnlyList<NativeMenuCommandDescriptor> ReadBackgroundMenuCommandDescriptors(nint view)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu));
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create an in-memory background Shell menu for command ID comparison.");
            try
            {
                var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryHr);
                var commandCount = Math.Min(queryHr & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(512);
                try
                {
                    List<NativeMenuCommandDescriptor> commands = [];
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        var id = checked((int)offset + 1);
                        var caption = new System.Text.StringBuilder(256);
                        if (GetMenuStringW(menu, (uint)id, caption, caption.Capacity, 0) <= 0) continue;
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) != 0) continue;
                        var verb = Marshal.PtrToStringUni(buffer);
                        if (!string.IsNullOrWhiteSpace(verb)) commands.Add(new NativeMenuCommandDescriptor(id, verb, caption.ToString()));
                    }
                    return commands;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static IReadOnlyList<(string Caption, List<(int Id, string Text)> Items)> ReadBackgroundMenuSubmenus(nint view)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu));
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create a background Shell menu tree for submenu comparison.");
            try
            {
                Check(At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800));
                List<(string Caption, List<(int Id, string Text)> Items)> submenus = [];
                for (var index = 0; index < GetMenuItemCount(menu); index++)
                {
                    var submenu = GetSubMenu(menu, index);
                    if (submenu == 0) continue;
                    var caption = new System.Text.StringBuilder(256);
                    _ = GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400);
                    List<(int Id, string Text)> items = [];
                    for (var childIndex = 0; childIndex < GetMenuItemCount(submenu); childIndex++)
                    {
                        var text = new System.Text.StringBuilder(256);
                        if (GetMenuStringW(submenu, (uint)childIndex, text, text.Capacity, 0x400) > 0 && text.Length > 0)
                            items.Add((unchecked((int)GetMenuItemID(submenu, childIndex)), text.ToString()));
                    }
                    submenus.Add((caption.ToString(), items));
                }
                return submenus;
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }


    private static int FindItemIndex(nint folderView, nint expectedPidl)
    {
        if (At<ItemCount>(folderView, 7)(folderView, 2, out var count) < 0) return -1;
        for (var index = 0; index < count; index++)
        {
            if (At<GetItem>(folderView, 6)(folderView, index, out var itemPidl) < 0 || itemPidl == 0) continue;
            try { if (ILIsEqual(itemPidl, expectedPidl)) return index; }
            finally { Marshal.FreeCoTaskMem(itemPidl); }
        }
        return -1;
    }

    private static bool FocusedItemNameMatches(nint folderView, string expectedName)
    {
        if (At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex) < 0 || focusedIndex < 0 ||
            At<ItemCount>(folderView, 7)(folderView, 2, out var count) < 0 || focusedIndex >= count)
            return false;
        return string.Equals(ReadVisibleItemNames(folderView, count)[focusedIndex], expectedName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static int FindItemIndexByName(nint folderView, string expectedName)
    {
        if (At<ItemCount>(folderView, 7)(folderView, 2, out var count) < 0) return -1;
        var names = ReadVisibleItemNames(folderView, count);
        return Array.FindIndex(names, name => name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static void InvokeBackgroundVerb(nint view, nint owner, string expectedVerb) =>
        Check(InvokeBackgroundVerbRaw(view, owner, expectedVerb));

    private static int InvokeBackgroundVerbRaw(nint view, nint owner, string expectedVerb, uint invokeMask = 0,
        bool canonicalString = false)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu)); // SVGIO_BACKGROUND.
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create the in-memory native Shell menu.");
            try
            {
                var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryHr);
                var commandCount = Math.Min(queryHr & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(512);
                uint? commandOffset = null;
                try
                {
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) == 0 &&
                            string.Equals(Marshal.PtrToStringUni(buffer), expectedVerb, StringComparison.OrdinalIgnoreCase))
                        {
                            commandOffset = offset;
                            break;
                        }
                    }
                    if (commandOffset is null) throw new Exception($"The Shell background menu did not expose the '{expectedVerb}' canonical verb.");
                    if (string.Equals(expectedVerb, "newfolder", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"ishellview-newfolder-map: command-offset={commandOffset.Value}; invocation={(canonicalString ? "ansi-verb" : "offset")}");
                }
                finally { Marshal.FreeHGlobal(buffer); }

                nint ansiVerb = 0;
                try
                {
                    var invoke = new NativeInvokeInfo
                    {
                        Size = Marshal.SizeOf<NativeInvokeInfo>(),
                        Mask = invokeMask,
                        Window = owner,
                        Verb = (nint)commandOffset.Value,
                        Show = 1
                    };
                    if (canonicalString)
                    {
                        ansiVerb = Marshal.StringToCoTaskMemAnsi(expectedVerb);
                        invoke.Verb = ansiVerb;
                    }
                    return At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invoke);
                }
                finally
                {
                    if (ansiVerb != 0) Marshal.FreeCoTaskMem(ansiVerb);
                }
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static IReadOnlyList<string> ReadContextMenuVerbs(nint view, uint itemScope)
    {
        var contextMenuId = ContextMenuId;
        Check(At<GetItemObject>(view, 15)(view, itemScope, ref contextMenuId, out var contextMenu));
        try
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create an in-memory Shell item menu.");
            try
            {
                var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                Check(queryHr);
                var commandCount = Math.Min(queryHr & 0xffff, 0x7fff);
                var buffer = Marshal.AllocHGlobal(512);
                try
                {
                    List<string> verbs = [];
                    var getCommandString = At<GetCommandString>(contextMenu, 5);
                    for (uint offset = 0; offset < commandCount; offset++)
                    {
                        Marshal.WriteInt16(buffer, 0);
                        if (getCommandString(contextMenu, offset, 4, 0, buffer, 256) == 0 &&
                            !string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(buffer)))
                            verbs.Add(Marshal.PtrToStringUni(buffer)!);
                    }
                    return verbs;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { DestroyMenu(menu); }
        }
        finally { Marshal.Release(contextMenu); }
    }

    private static nint FindChildByClass(nint parent, string className)
    {
        nint match = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, name, name.Capacity) > 0 && name.ToString() == className)
            {
                match = window;
                return false;
            }
            return true;
        }, 0);
        return match;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;
        public nint VerbW;
        public nint ParametersW;
        public nint DirectoryW;
        public nint TitleW;
        public System.Drawing.Point InvokePoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSortColumn
    {
        public ShellColumnManager.NativeKey Key;
        public int Direction;
    }

    private sealed record NativeMenuCommandDescriptor(int Id, string Verb, string Caption);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CallWindowProcResult
    {
        public nint Result, LParam;
        public nuint WParam;
        public uint Message;
        public nint Window;
    }

    private sealed class FilteredHost : StreamViewHost,
        IComDialogBrowser, IComDialogBrowser2, IComDialogBrowser3
    {
        // Keep the isolated host's non-filter browser callbacks aligned with the
        // product site; only IncludeObject remains specialized for this fixture.
        private readonly ShellBrowserSite _browserCallbacks = new() { QuickFilterActive = true };
        internal ShellFolderFilter Filter { get; }
        private int _includeObjectCalls;
        private int _defaultCommandCalls;
        private int _browseObjectCalls;
        private nint _pendingFolder, _pendingView, _pendingWindow;
        internal int IncludeObjectCalls => Volatile.Read(ref _includeObjectCalls);
        internal int DefaultCommandCalls => Volatile.Read(ref _defaultCommandCalls);
        internal int BrowseObjectCalls => Volatile.Read(ref _browseObjectCalls);
        internal long CallbackSelectionVersion => _browserCallbacks.SelectionVersion;
        internal bool BrowseTargetIsChild { get; private set; }
        internal bool BrowseTargetIsRoot { get; private set; }
        internal int HistoryIndex => NavigationHistory!.Index;
        internal nint CurrentPidl => NavigationHistory!.CurrentPidl;
        internal nint PendingView => _pendingView;

        internal FilteredHost(nint parent, ShellFolderFilter filter, nint rootPidl, nint expectedChildPidl,
            Func<nint, uint, int> navigate) : base(parent, [])
        {
            Filter = filter;
            NavigationHistory = new ShellViewNavigationHistory(rootPidl, ClonePidl, Marshal.FreeCoTaskMem,
                ILIsEqual, TryGetParentPidl, ILCombine, (target, flags) =>
                {
                    BrowseTargetIsChild = ILIsEqual(target, expectedChildPidl);
                    BrowseTargetIsRoot = ILIsEqual(target, rootPidl);
                    return navigate(target, flags);
                });
        }

        internal void SetPendingView(nint folder, nint view, nint window)
        {
            _pendingFolder = folder;
            _pendingView = view;
            _pendingWindow = window;
            View = view;
        }

        internal (nint Folder, nint View, nint Window) TakePendingView()
        {
            var result = (_pendingFolder, _pendingView, _pendingWindow);
            _pendingFolder = _pendingView = _pendingWindow = 0;
            return result;
        }

        internal void DisposePendingView()
        {
            if (_pendingView != 0)
            {
                _ = At<DestroyViewWindow>(_pendingView, 10)(_pendingView);
                Marshal.Release(_pendingView);
            }
            if (_pendingFolder != 0) Marshal.Release(_pendingFolder);
            _pendingFolder = _pendingView = _pendingWindow = 0;
        }

        public override int BrowseObject(nint pidl, uint flags)
        {
            Interlocked.Increment(ref _browseObjectCalls);
            return base.BrowseObject(pidl, flags);
        }

        public int OnDefaultCommand(nint shellView)
        {
            Interlocked.Increment(ref _defaultCommandCalls);
            var folderViewId = FolderViewId;
            if (Marshal.QueryInterface(shellView, in folderViewId, out var folderView) < 0) return 0;
            try
            {
                if (At<GetFocusedItem>(folderView, 10)(folderView, out var index) < 0 || index < 0) return 0;
                if (At<GetItem>(folderView, 6)(folderView, index, out var childPidl) < 0 || childPidl == 0) return 0;
                try
                {
                    var absolutePidl = ILCombine(CurrentPidl, childPidl);
                    if (absolutePidl == 0) return 0;
                    try { return BrowseObject(absolutePidl, 0); }
                    finally { Marshal.FreeCoTaskMem(absolutePidl); }
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            finally { Marshal.Release(folderView); }
        }
        public int OnStateChange(nint shellView, uint change) => _browserCallbacks.OnStateChange(shellView, change);
        public int IncludeObject(nint shellView, nint childPidl)
        {
            Interlocked.Increment(ref _includeObjectCalls);
            var folderViewId = FolderViewId;
            var queryHr = Marshal.QueryInterface(shellView, in folderViewId, out var folderView);
            if (queryHr < 0) return 0; // Metadata failure must keep an item visible.
            try
            {
                var shellFolderId = ShellFolderId;
                var getFolderHr = At<GetFolder>(folderView, 5)(folderView, ref shellFolderId, out var shellFolder);
                if (getFolderHr < 0 || shellFolder == 0) return 0;
                try { return Filter.ShouldShow(shellFolder, 0, childPidl); }
                finally { Marshal.Release(shellFolder); }
            }
            finally { Marshal.Release(folderView); }
        }
        public int Notify(nint shellView, uint notification) => _browserCallbacks.Notify(shellView, notification);
        public int GetDefaultMenuText(nint shellView, nint text, int capacity) =>
            _browserCallbacks.GetDefaultMenuText(shellView, text, capacity);
        public int GetViewFlags(out uint flags) => _browserCallbacks.GetViewFlags(out flags);
        public int OnColumnClicked(nint shellView, int column) => _browserCallbacks.OnColumnClicked(shellView, column);
        public int GetCurrentFilter(nint text, int capacity) => _browserCallbacks.GetCurrentFilter(text, capacity);
        public int OnPreViewCreated(nint shellView) => _browserCallbacks.OnPreViewCreated(shellView);

    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous,
        ref StreamProbeSettings settings, nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint itemPidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemDisplayName(nint self, uint nameKind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroupBy(nint self, ref ShellColumnManager.NativeKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroupBy(nint self, out ShellColumnManager.NativeKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, [In] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellColumnCount(nint self, uint flags, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint HookProc(int code, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumns(nint self, [Out] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributesOf(nint self, uint count, nint itemPidls, nint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFocusedItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TranslateAccelerator(nint self, ref NativeMessage message);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewModeAndIconSize(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewModeAndIconSize(nint self, out uint mode, out int iconSize);
    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path,
        nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHCreateItemWithParent(nint parentPidl, nint parentFolder,
        nint childPidl, ref Guid iid, out nint item);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind,
        in Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint first, nint second);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")] private static extern bool ILRemoveLastID(nint pidl);

    private static nint TryGetParentPidl(nint absolutePidl)
    {
        var parentPidl = ILClone(absolutePidl);
        if (parentPidl == 0 || ILRemoveLastID(parentPidl)) return parentPidl;
        Marshal.FreeCoTaskMem(parentPidl);
        return 0;
    }
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex,
        string cls, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, nint rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState([In] byte[] state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SetWindowsHookExW(int hookType, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuStringW(nint menu, uint item, System.Text.StringBuilder text, int maxLength, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
}
