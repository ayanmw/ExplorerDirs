using System.Runtime.InteropServices;
using System.IO.Compression;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

// Hidden direct-IShellView probe for the ICommDlgBrowser3 filtering route.
// It uses only a unique fixture and never creates ExplorerBrowser or user files.
internal static class ShellViewHostFilterProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
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
    private const int GwlStyle = -16;
    private const long LvsTypeMask = 0x3;
    private const long LvsReport = 0x1;
    private const uint LvmDeleteAllItems = 0x1009;
    private const uint LvmInsertColumnW = 0x101B;
    private const uint LvmSetItemTextW = 0x1074;
    private const uint LvmInsertItemW = 0x104D;
    private const uint LvmGetItemCount = 0x1004;
    private const uint LvmGetItemTextW = 0x1073;
    private const uint LvmGetItemW = 0x104B;
    private const uint LvmGetItemState = 0x102C;
    private const uint LvmSetItemState = 0x102B;
    private const uint LvmSortItems = 0x1030;
    private const uint LvmSetImageList = 0x1003;
    private const uint LvmInsertGroup = 0x1091;
    private const uint LvmGetGroupCount = 0x1098;
    private const uint LvmEnableGroupView = 0x109D;
    private const uint LvmRemoveAllGroups = 0x10A0;
    private const uint LvmIsGroupViewEnabled = 0x10AF;
    private const uint ShellChangeCreated = 0x00000002;
    private const uint ShellChangeDeleted = 0x00000004;
    private const uint ShellChangeRenamedItem = 0x00000001;
    private const uint ShellChangeUpdatedItem = 0x00002000;
    private const uint ShellChangeUpdatedDirectory = 0x00001000;
    private const uint ShellChangePathUnicode = 0x00000005;
    private const uint SigDnFileSystemPath = 0x80058000;

    internal static int Run() => Run(stopAfterGroupedExport: false, leaveParentForThreadExit: false);

    internal static int RunGroupedExportOnly() => Run(stopAfterGroupedExport: true, leaveParentForThreadExit: false);

    internal static int RunForPrivateDesktop() => Run(stopAfterGroupedExport: false, leaveParentForThreadExit: true);

    internal static int RunMenuCommandsForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, menuCommandsOnly: true);

    internal static int RunNewFolderForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, newFolderOnly: true);

    internal static int RunRecursiveSizeProjectionForPrivateDesktop() => Run(stopAfterGroupedExport: false,
        leaveParentForThreadExit: true, recursiveSizeProjectionOnly: true);

    internal static int RunVirtualScopeForPrivateDesktop(string scope) =>
        Run(stopAfterGroupedExport: false, leaveParentForThreadExit: true, virtualScopeOnly: scope);

    private static int Run(bool stopAfterGroupedExport, bool leaveParentForThreadExit, string? virtualScopeOnly = null,
        bool menuCommandsOnly = false, bool newFolderOnly = false, bool recursiveSizeProjectionOnly = false)
    {
        var foreground = GetForegroundWindow();
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
        string? zipPath = null;
        nint parent = 0, folderPidl = 0, childFolderPidl = 0, grandchildFolderPidl = 0, alternateFolderPidl = 0, volumeRootPidl = 0, virtualFolderPidl = 0, filePidl = 0, folder = 0, view = 0, viewPointer = 0, viewWindow = 0, folderView = 0;
        bool created = false;
        var failAfterCreateViewObject = false;
        var failAfterCreateViewWindow = false;
        nint failedNavigationWindow = 0;
        var itemCountLastHResult = 0;
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
                if (failAfterCreateViewObject)
                {
                    // Exercise rollback after COM view creation but before any replacement HWND is requested.
                    failAfterCreateViewObject = false;
                    Marshal.ThrowExceptionForHR(unchecked((int)0x80004005));
                }
                host!.View = childView;
                var settings = new StreamProbeSettings { Mode = uint.MaxValue };
                var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
                var hr = At<CreateViewWindow>(childView, 9)(childView, previousView, ref settings, viewPointer, ref bounds, out childWindow);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (failAfterCreateViewWindow)
                {
                    // Exercise rollback after the replacement HWND exists but before ownership transfers to the host.
                    failAfterCreateViewWindow = false;
                    failedNavigationWindow = childWindow;
                    Marshal.ThrowExceptionForHR(unchecked((int)0x80004005));
                }
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
            if (recursiveSizeProjectionOnly)
            {
                foreach (var (name, size) in new[] { ("size-2", 2), ("size-10", 10), ("size-100", 100) })
                {
                    var directory = Path.Combine(fixture, name);
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, "payload.bin"), new byte[size]);
                }
            }
            if (menuCommandsOnly)
                Directory.CreateDirectory(Path.Combine(fixture, "aardvark"));
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
            var expectedVisibleCount = recursiveSizeProjectionOnly ? 5 : menuCommandsOnly ? 3 : 2;
            int count;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count)); // SVGIO_ALLVIEW
                if (count == expectedVisibleCount && host.IncludeObjectCalls >= 3) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Check(At<ItemCount>(folderView, 7)(folderView, 2, out count));
            Console.WriteLine($"ishellview-host-filter: includecalls={host.IncludeObjectCalls}; filtercalls={host.Filter.ShouldShowCalls}; visible-items={count}; parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            var passed = host.IncludeObjectCalls >= 3 && host.Filter.ShouldShowCalls >= 3 && count == expectedVisibleCount &&
                !IsWindowVisible(parent) && GetForegroundWindow() == foreground;
            if (!passed)
                throw new Exception("Direct IShellView did not apply the ICommDlgBrowser3 filter to the isolated fixture.");
            if (recursiveSizeProjectionOnly)
            {
                VerifyRecursiveSizePeerProjection(folderView, folderPidl, folder, parent, count, () =>
                {
                    Check(At<DestroyViewWindow>(view, 10)(view));
                    created = false; // The projection probe destroys the real Shell view while a scan is pending.
                });
                return 0;
            }
            if (virtualScopeOnly is not null)
            {
                var scope = virtualScopeOnly switch
                {
                    "Desktop" => "shell:Desktop",
                    "ThisPC" => "shell:MyComputerFolder",
                    "RecycleBin" => "shell:RecycleBinFolder",
                    "ControlPanel" => "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
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
                VerifyNativeMenuCommands(view, folderView, parent, foreground, folderPidl);
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
                ("shell:NetworkPlacesFolder", "NetworkPlaces"),
                ("::{26EE0668-A00A-44D7-9371-BEB064C98683}", "ControlPanel")
            })
                BrowseVirtualScope(parsingName, label);

            BrowseVirtualScope(ShellSearchLocation.BuildUri(fixture, "nested"), "SearchFolder");

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
                var previousLocationPidl = ILClone(host.CurrentPidl);
                try
                {
                    if (label == "SearchFolder")
                        virtualFolderPidl = ShellSearchLocation.ParsePidl(parsingName);
                    else
                        Check(SHParseDisplayName(parsingName, 0, out virtualFolderPidl, 0, out _));
                    TraceVirtualScope($"{label}-browse-before", foreground);
                    Check(host.BrowseObject(virtualFolderPidl, 0));
                    TraceVirtualScope($"{label}-browse-after", foreground);
                    var itemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: label == "ControlPanel");
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"The isolated host did not retain the {label} PIDL.");
                    var listView = FindChildByClass(viewWindow, "SysListView32");
                    var listViewItems = listView == 0 ? -1 : SendMessageW(listView, 0x1004, 0, 0).ToInt32();
                    Console.WriteLine($"ishellview-host-filter: virtual-shell-view-created=True; target={label}; folder-view-items={itemCount}; folder-view-items-hr=0x{itemCountLastHResult:X8}; list-view-items={listViewItems}; {InspectNativeContracts(readVerbs: label is not "ZipContainer" and not "ControlPanel")}");
                    Check(host.BrowseObject(0, SbspNavigateBack));
                    var backItemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                    var backItemCountHResult = itemCountLastHResult;
                    if (!ILIsEqual(host.CurrentPidl, previousLocationPidl))
                        throw new Exception($"Back from {label} did not restore the previous absolute location.");
                    Check(host.BrowseObject(0, SbspNavigateForward));
                    var forwardItemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                    var forwardItemCountHResult = itemCountLastHResult;
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Forward to {label} did not restore its absolute PIDL identity.");
                    Console.WriteLine($"ishellview-host-filter: virtual-history-roundtrip=True; target={label}; back-items={backItemCount}; back-hr=0x{backItemCountHResult:X8}; forward-items={forwardItemCount}; forward-hr=0x{forwardItemCountHResult:X8}; view-counts-readable={backItemCount >= 0 && forwardItemCount >= 0}; pidl-identity-preserved=True");

                    // Match the filesystem failure gate: a rejected file target must not replace a virtual view or advance its travel history.
                    var activeVirtualView = host.View;
                    var historyIndexBeforeInvalidTarget = host.HistoryIndex;
                    var invalidTargetHr = host.BrowseObject(filePidl, 0);
                    if (invalidTargetHr >= 0 || host.PendingView != 0 || host.View != activeVirtualView ||
                        host.HistoryIndex != historyIndexBeforeInvalidTarget || !ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Rejecting a file target after {label} navigation changed the active virtual view or history.");
                    Console.WriteLine($"ishellview-host-filter: virtual-nonfolder-target-rejected=True; target={label}; hr=0x{invalidTargetHr:X8}; active-view-preserved=True; history-index={host.HistoryIndex}; pidl-identity-preserved=True");

                    var retainedViewBeforeCreateWindowFailure = host.View;
                    var retainedWindowBeforeCreateWindowFailure = viewWindow;
                    var historyCountBeforeCreateWindowFailure = host.NavigationHistory!.Count;
                    failAfterCreateViewObject = true;
                    var createWindowFailureHr = host.BrowseObject(virtualFolderPidl, 0);
                    if (createWindowFailureHr >= 0 || failAfterCreateViewObject ||
                        !IsWindow(retainedWindowBeforeCreateWindowFailure) || host.PendingView != 0 ||
                        host.View != retainedViewBeforeCreateWindowFailure ||
                        host.HistoryIndex != historyIndexBeforeInvalidTarget ||
                        host.NavigationHistory.Count != historyCountBeforeCreateWindowFailure ||
                        !ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Failure after CreateViewObject for {label} did not preserve the old view and history.");
                    Console.WriteLine($"ishellview-host-filter: post-create-view-object-failure-rollback=True; target={label}; hr=0x{createWindowFailureHr:X8}; previous-view-valid=True; no-pending-view=True; history-unchanged=True");

                    var retainedViewBeforeInjectedFailure = host.View;
                    var retainedWindowBeforeInjectedFailure = viewWindow;
                    var historyCountBeforeInjectedFailure = host.NavigationHistory!.Count;
                    failAfterCreateViewWindow = true;
                    var injectedFailureHr = host.BrowseObject(virtualFolderPidl, 0);
                    if (injectedFailureHr >= 0 || failAfterCreateViewWindow || failedNavigationWindow == 0 ||
                        IsWindow(failedNavigationWindow) || !IsWindow(retainedWindowBeforeInjectedFailure) ||
                        host.PendingView != 0 || host.View != retainedViewBeforeInjectedFailure ||
                        host.HistoryIndex != historyIndexBeforeInvalidTarget ||
                        host.NavigationHistory.Count != historyCountBeforeInjectedFailure ||
                        !ILIsEqual(host.CurrentPidl, virtualFolderPidl))
                        throw new Exception($"Injected CreateViewWindow handoff failure for {label} did not destroy its new window and preserve the old view/history.");
                    Console.WriteLine($"ishellview-host-filter: view-window-handoff-failure-rollback=True; target={label}; hr=0x{injectedFailureHr:X8}; failed-window-destroyed=True; previous-view-valid=True; history-unchanged=True");
                    failedNavigationWindow = 0;

                    var historyCountBeforeSamePath = host.NavigationHistory!.Count;
                    Check(host.BrowseObject(virtualFolderPidl, 0));
                    var samePathItemCount = AdoptPendingView(expectedItemCount: null,
                        allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                    if (!ILIsEqual(host.CurrentPidl, virtualFolderPidl) || host.HistoryIndex != historyIndexBeforeInvalidTarget ||
                        host.NavigationHistory.Count != historyCountBeforeSamePath)
                        throw new Exception($"Same-path navigation to {label} changed its PIDL or duplicated the travel-history entry.");
                    Console.WriteLine($"ishellview-host-filter: virtual-same-path-navigation=True; target={label}; view-created=True; view-items={samePathItemCount}; view-items-hr=0x{itemCountLastHResult:X8}; history-index={host.HistoryIndex}; history-count={host.NavigationHistory.Count}; history-unchanged=True");

                    if (label == "ThisPC")
                    {
                        var thisPcPidl = ILClone(host.CurrentPidl);
                        try
                        {
                            var historyCountBeforeRecreateLoop = host.NavigationHistory.Count;
                            var historyIndexBeforeRecreateLoop = host.HistoryIndex;
                            var retiredWindowsDestroyed = true;
                            for (var cycle = 0; cycle < 8; cycle++)
                            {
                                var retiredWindow = viewWindow;
                                Check(host.BrowseObject(thisPcPidl, 0));
                                AdoptPendingView(expectedItemCount: null,
                                    allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                                if (IsWindow(retiredWindow) || !IsWindow(viewWindow) ||
                                    host.HistoryIndex != historyIndexBeforeRecreateLoop ||
                                    host.NavigationHistory.Count != historyCountBeforeRecreateLoop ||
                                    !ILIsEqual(host.CurrentPidl, thisPcPidl))
                                    retiredWindowsDestroyed = false;
                                if (!retiredWindowsDestroyed)
                                    throw new Exception($"Repeated This PC view replacement failed at cycle {cycle + 1}: retired-window-destroyed={!IsWindow(retiredWindow)}; active-window-valid={IsWindow(viewWindow)}; history-index={host.HistoryIndex}; history-count={host.NavigationHistory.Count}.");
                            }
                            Console.WriteLine($"ishellview-host-filter: same-path-view-retirement=True; target=ThisPC; cycles=8; retired-windows-destroyed={retiredWindowsDestroyed}; active-window-valid={IsWindow(viewWindow)}; history-unchanged=True");

                            Check(host.BrowseObject(volumeRootPidl, 0));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, volumeRootPidl))
                                throw new Exception("The This PC view could not navigate into the system volume root.");

                            Check(host.BrowseObject(0, SbspParent));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, thisPcPidl))
                                throw new Exception("SBSP_PARENT from the system volume root did not return to This PC.");

                            Check(host.BrowseObject(0, SbspNavigateBack));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, volumeRootPidl))
                                throw new Exception("Back from This PC did not restore the system volume root.");

                            Check(host.BrowseObject(0, SbspNavigateForward));
                            AdoptPendingView(expectedItemCount: null,
                                allowItemCountFailure: true, returnOnFirstSuccessfulCount: true);
                            if (!ILIsEqual(host.CurrentPidl, thisPcPidl))
                                throw new Exception("Forward from the system volume root did not restore This PC.");
                            Console.WriteLine($"ishellview-host-filter: virtual-filesystem-parent-history=True; target=ThisPC; drive-root-parent=True; back-forward=True; history-index={host.HistoryIndex}; pidl-identity-preserved=True");
                        }
                        finally { Marshal.FreeCoTaskMem(thisPcPidl); }
                    }
                    TraceVirtualScope($"{label}-complete", foreground);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(virtualFolderPidl);
                    Marshal.FreeCoTaskMem(previousLocationPidl);
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

            int AdoptPendingView(int? expectedItemCount, bool allowItemCountFailure = false,
                bool returnOnFirstSuccessfulCount = false)
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
                    var itemCountHr = At<ItemCount>(folderView, 7)(folderView, 2, out itemCount);
                    itemCountLastHResult = itemCountHr;
                    if (itemCountHr < 0)
                    {
                        if (allowItemCountFailure) return -1;
                        Check(itemCountHr);
                    }
                    // Search Folder counts are index-driven; navigation history needs a readable view, not a settled result count.
                    if (returnOnFirstSuccessfulCount) return itemCount;
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
                var menuItemCount = -1;
                var verbs = new List<string>();
                var menuCaptions = new List<string>();
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
                            if (menuQueryHr >= 0)
                            {
                                menuItemCount = GetMenuItemCount(menu);
                                for (var index = 0; index < menuItemCount; index++)
                                {
                                    var caption = new System.Text.StringBuilder(256);
                                    if (GetMenuStringW(menu, (uint)index, caption, caption.Capacity, 0x400) > 0)
                                        menuCaptions.Add(caption.ToString());
                                }
                            }
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
                return $"background-menu=0x{menuHr:X8}/query=0x{menuQueryHr:X8}/items={menuItemCount}/captions=[{string.Join('|', menuCaptions)}]/{verbReport}/core-state=[{string.Join(',', coreStates)}];drop-target=0x{dropHr:X8}";
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
        VerifyShellNativeViewModes(folderView, listView);
        // Keep the following linear Down-key probe in Details, where its expected next-row behavior is defined.
        Check(setMode(folderView, 4, 16));
        _ = SendMessageW(listView, LvmSetView, 1, 0);
        Application.DoEvents();
        _ = ShowWindow(parent, 0); // SW_HIDE; restore the probe's original hidden-parent boundary.
        Console.WriteLine("ishellview-host-filter: synchronized-view-modes=" + string.Join('|', results));
    }

    private static void VerifyShellNativeViewModes(nint folderView, nint listView)
    {
        const int LvmGetView = 0x108F;
        var setMode = At<SetViewModeAndIconSize>(folderView, 35);
        var getMode = At<GetViewModeAndIconSize>(folderView, 36);
        var results = new List<string>();
        var initialNames = ReadVisibleItemNames(folderView, 2);
        var keepIndex = Array.FindIndex(initialNames, name => name.Equals("keep.txt", StringComparison.OrdinalIgnoreCase));
        if (keepIndex < 0) throw new Exception("The Shell view mode probe has no persistent selection item.");
        Check(At<SelectItem>(folderView, 15)(folderView, keepIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));

        foreach (var style in ViewStyle.All)
        {
            var requestedSize = ViewStyle.NativeIconSize(style.Mode, style.IconSize);
            Check(setMode(folderView, style.Mode, requestedSize));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            int visibleItems;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out visibleItems));
                if (visibleItems == 2) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);

            Check(getMode(folderView, out var actualMode, out var actualSize));
            var listViewMode = SendMessageW(listView, LvmGetView, 0, 0).ToInt32();
            var names = ReadVisibleItemNames(folderView, visibleItems);
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out var selectedItems));
            Check(At<GetFocusedItem>(folderView, 10)(folderView, out var focusedIndex));
            var focusedName = focusedIndex >= 0 && focusedIndex < names.Length ? names[focusedIndex] : string.Empty;
            var expectedListViewMode = style.Mode switch
            {
                1 => 0, // FVM_ICON -> LV_VIEW_ICON.
                2 => 2, // FVM_SMALLICON -> LV_VIEW_SMALLICON.
                3 => 3, // FVM_LIST -> LV_VIEW_LIST.
                4 => 1, // FVM_DETAILS -> LV_VIEW_DETAILS.
                6 => 4, // FVM_TILE -> LV_VIEW_TILE.
                _ => -1 // Content is not one of the five documented SysListView32 modes.
            };
            var expectedSizeMatches = requestedSize <= 0 || actualSize == requestedSize;
            var listViewModeMatches = expectedListViewMode < 0 || listViewMode == expectedListViewMode;
            var selectionPreserved = selectedItems == 1 && focusedName.Equals("keep.txt", StringComparison.OrdinalIgnoreCase);
            var itemsMatch = visibleItems == 2 && names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(["child", "keep.txt"], StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"ishellview-native-view-mode: name={style.Name}; requested={style.Mode}/{requestedSize}; actual={actualMode}/{actualSize}; list-view={listViewMode}; items={visibleItems}; names={string.Join(',', names)}; selection={selectedItems}/{focusedName}; item-set-preserved={itemsMatch}; selection-preserved={selectionPreserved}");

            if (actualMode != style.Mode || !expectedSizeMatches || !listViewModeMatches || !itemsMatch || !selectionPreserved)
                throw new Exception($"IShellView did not apply {style.Name} through its native view API: requested={style.Mode}/{requestedSize}, actual={actualMode}/{actualSize}, list={listViewMode}, items={visibleItems}, names={string.Join(',', names)}, selection={selectedItems}/{focusedName}.");
            results.Add($"{style.Name}={actualMode}/{actualSize}/list:{listViewMode}/items:{visibleItems}");
        }

        for (var index = 0; index < initialNames.Length; index++)
            Check(At<SelectItem>(folderView, 15)(folderView, index, 0));
        Check(At<ItemCount>(folderView, 7)(folderView, 1, out var finalSelectionCount));
        if (finalSelectionCount != 0) throw new Exception("The native view mode probe did not restore its empty selection baseline.");

        Console.WriteLine("ishellview-host-filter: shell-api-view-modes=" + string.Join('|', results));
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

    private static void VerifyNativeMenuCommands(nint view, nint folderView, nint owner, nint foreground, nint folderPidl)
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
        var folderId = ShellFolderId;
        Check(SHBindToObject(0, folderPidl, 0, in folderId, out var shellFolder));
        var nextPeerRowId = 0;
        List<PeerShellRow> peerRows = [];
        var initialProjection = CapturePeerShellRows(folderView, folderPidl, shellFolder, [], ref nextPeerRowId);
        peerRows = initialProjection.Rows;
        var peerList = CreatePeerListView(owner, initialProjection.SystemImageList);
        PopulatePeerList(peerList, peerRows);
        var peerRowsBeforeRefresh = ReadPeerListNames(peerList);
        if (peerRowsBeforeRefresh.Length != initialCount)
            throw new Exception("The independent ListView projection did not receive the initial Shell rows.");
        var peerIdsBeforeRefresh = ReadPeerListRowIds(peerList);
        if (!peerIdsBeforeRefresh.SequenceEqual(peerRows.Select(row => row.Id)))
            throw new Exception("The peer ListView did not preserve stable row IDs as its item data.");
        Check(At<GetItem>(folderView, 6)(folderView, 0, out var expectedFocusedPidl));
        var expectedFocusedName = ReadVisibleItemNames(folderView, initialCount)[0];
        List<string> menuCommandFocusRecovery = [];
        List<string> nativeSortOrders = [];
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
        void VerifyFolderSortOrder(bool ascending, string command)
        {
            var orderDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            var stableSamples = 0;
            string[] names = [];
            while (DateTime.UtcNow < orderDeadline && stableSamples < 3)
            {
                Application.DoEvents();
                names = ReadVisibleItemNames(folderView, initialCount);
                var aardvarkIndex = Array.FindIndex(names, name => string.Equals(name, "aardvark", StringComparison.OrdinalIgnoreCase));
                var childIndex = Array.FindIndex(names, name => string.Equals(name, "child", StringComparison.OrdinalIgnoreCase));
                var orderMatches = aardvarkIndex >= 0 && childIndex >= 0 &&
                    (aardvarkIndex < childIndex) == ascending;
                stableSamples = orderMatches ? stableSamples + 1 : 0;
                if (stableSamples < 3) Thread.Sleep(50);
            }
            if (stableSamples < 3)
                throw new Exception($"The Shell {command} menu did not produce the expected folder order: [{string.Join(",", names)}].");
            nativeSortOrders.Add($"{command}:[{string.Join(",", names)}]");
        }
        try
        {
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 1u | 0x10u | 0x40000000u)); // SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS
            _ = SetFocus(listView);
            if (GetFocus() != listView || !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception("The menu-command fixture could not establish its initial list and item focus.");
            using var refreshSelection = ShellViewSelectionSnapshot.Capture(folderView);
            if (refreshSelection is null || refreshSelection.SelectedCount != 1 || !refreshSelection.HasFocusedItem)
                throw new Exception("The production selection snapshot did not capture the focused refresh fixture item.");

            InvokeBackgroundVerb(view, owner, "viewlogicaldetails");
            Check(getMode(folderView, out modeAfterMenu, out iconSizeAfterMenu));
            if (modeAfterMenu != 4 || iconSizeAfterMenu != 16)
                throw new Exception($"The Shell background menu did not switch the isolated view to Details: mode={modeAfterMenu}, size={iconSizeAfterMenu}.");
            SetPeerListMode(peerList, LvsReport);
            if ((GetWindowLongPtrW(peerList, GwlStyle).ToInt64() & LvsTypeMask) != LvsReport ||
                !ReadPeerListNames(peerList).SequenceEqual(peerRowsBeforeRefresh, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Projecting Shell Details mode changed or dropped the independent ListView rows.");
            VerifyColumnHeaderControl(view, owner);
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
                if (!ReadPeerListNames(peerList).SequenceEqual(peerRowsBeforeRefresh, StringComparer.OrdinalIgnoreCase))
                    throw new Exception("The independent ListView lost its old rows while the native Shell view was refreshing.");
                listFocusPreserved = GetFocus() == listView;
                rawRefreshPreservedItem = FocusedItemMatches(folderView, expectedFocusedPidl) && selectedCount == 1;
                // Wait for Shell's item model to settle; selection recovery is performed below
                // with the same production snapshot helper used by ExplorerBrowser refresh.
                var stable = visibleCount == initialCount;
                stableRefreshSamples = stable ? stableRefreshSamples + 1 : 0;
                refreshSamples.Add($"+{refreshSampleNumber++ * 50}ms:items={visibleCount},hwnd={listFocusPreserved},item={FocusedItemMatches(folderView, expectedFocusedPidl)},selection={selectedCount}");
                if (stableRefreshSamples < 3) Thread.Sleep(50);
            } while (stableRefreshSamples < 3 && DateTime.UtcNow < deadline);
            if (visibleCount != initialCount || IsWindowVisible(owner) || GetForegroundWindow() != foreground)
                throw new Exception($"The Shell background Refresh command changed the fixture or desktop boundary: items={visibleCount}, parent-visible={IsWindowVisible(owner)}, foreground-unchanged={GetForegroundWindow() == foreground}.");
            var refreshProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, visibleCount);
            peerRows = refreshProjection.Rows;
            if (!refreshProjection.ExistingIdentitiesPreserved)
                throw new Exception("A Shell item changed identity during a refresh without a fixture mutation.");
            rawRefreshPreservedItem &= stableRefreshSamples == 3;
            (bool SelectionRestored, bool FocusRestored) recovery = default;
            var recoveryDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                Application.DoEvents();
                recovery = refreshSelection.Restore(view, allowSelectedFocusFallback: false);
                Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
                if (recovery.SelectionRestored && recovery.FocusRestored && selectedCount == 1 &&
                    FocusedItemMatches(folderView, expectedFocusedPidl)) break;
                Thread.Sleep(50);
            } while (DateTime.UtcNow < recoveryDeadline);
            if (GetFocus() != listView || !recovery.SelectionRestored || !recovery.FocusRestored ||
                !FocusedItemMatches(folderView, expectedFocusedPidl))
                throw new Exception($"The production snapshot could not restore Shell selection/focus after Refresh: result={recovery}, item={FocusedItemMatches(folderView, expectedFocusedPidl)}, selection={selectedCount}, focus-hwnd=0x{GetFocus():X}, list-hwnd=0x{listView:X}.");
            Check(At<ItemCount>(folderView, 7)(folderView, 1, out selectedCount));
            if (selectedCount != 1)
                throw new Exception($"The host could not restore the selected focused item after Refresh: {selectedCount}.");
            Console.WriteLine($"ishellview-menu-command-focus: raw-refresh-preserved-item={rawRefreshPreservedItem}; production-snapshot-restore={recovery.SelectionRestored && recovery.FocusRestored}; hwnd-focus-preserved={GetFocus() == listView}; samples=[{string.Join(';', refreshSamples)}]");
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
            var descendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount);
            peerRows = descendingProjection.Rows;
            if (!descendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("The Shell descending sort changed a row's PIDL identity.");
            Check(At<GetSortColumnCount>(view2, 26)(view2, out var sortCount));
            if (sortCount != 1) throw new Exception($"The Shell Sort menu changed the column count unexpectedly: {sortCount}.");
            var sorts = new NativeSortColumn[sortCount];
            Check(At<GetSortColumns>(view2, 28)(view2, sorts, sortCount));
            if (sorts[0].Key.FormatId != nameKey.FormatId || sorts[0].Key.PropertyId != nameKey.PropertyId || sorts[0].Direction != -1)
                throw new Exception($"The Shell Sort Descending command did not update the active sort state: key={sorts[0].Key.FormatId:B}/{sorts[0].Key.PropertyId}, direction={sorts[0].Direction}.");
            VerifyFolderSortOrder(ascending: false, "sortdescending");
            InvokeBackgroundVerb(view, owner, "sortascending");
            RestoreFocusedSelectionAfterMenuCommand("sortascending");
            var ascendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount);
            peerRows = ascendingProjection.Rows;
            if (!ascendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("The Shell ascending sort changed a row's PIDL identity.");
            Check(At<GetSortColumns>(view2, 28)(view2, sorts, sortCount));
            if (sorts[0].Direction != 1) throw new Exception($"The Shell Sort Ascending command did not restore ascending order: direction={sorts[0].Direction}.");
            VerifyFolderSortOrder(ascending: true, "sortascending");

            var typeKey = new ShellColumnManager.NativeKey { FormatId = PropertyFormatId, PropertyId = 4 };
            Check(At<SetGroupBy>(view2, 17)(view2, ref typeKey, 1));
            InvokeBackgroundVerb(view, owner, "groupdescending");
            RestoreFocusedSelectionAfterMenuCommand("groupdescending");
            var groupDescendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount, groupByType: true);
            peerRows = groupDescendingProjection.Rows;
            if (!groupDescendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("Grouping changed a Shell row's PIDL identity.");
            VerifyPeerListGrouping(peerList, peerRows);
            var groupResult = At<GetGroupBy>(view2, 18)(view2, out var groupKey, out var groupAscending);
            if (groupResult != 0 || groupKey.FormatId != typeKey.FormatId || groupKey.PropertyId != typeKey.PropertyId || groupAscending != 0)
                throw new Exception($"The Shell Group Descending command did not update the active grouping state: hr=0x{groupResult:X8}, key={groupKey.FormatId:B}/{groupKey.PropertyId}, ascending={groupAscending}.");
            InvokeBackgroundVerb(view, owner, "groupascending");
            RestoreFocusedSelectionAfterMenuCommand("groupascending");
            var groupAscendingProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                peerRows, ref nextPeerRowId, initialCount, groupByType: true);
            peerRows = groupAscendingProjection.Rows;
            if (!groupAscendingProjection.ExistingIdentitiesPreserved)
                throw new Exception("Changing group direction changed a Shell row's PIDL identity.");
            VerifyPeerListGrouping(peerList, peerRows);
            groupResult = At<GetGroupBy>(view2, 18)(view2, out groupKey, out groupAscending);
            if (groupResult != 0 || groupKey.FormatId != typeKey.FormatId || groupKey.PropertyId != typeKey.PropertyId || groupAscending != 1)
                throw new Exception($"The Shell Group Ascending command did not restore ascending order: hr=0x{groupResult:X8}, ascending={groupAscending}.");

            var originalPeerIds = peerRows.Select(row => row.Id).ToHashSet();
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(folderPidl, SigDnFileSystemPath, out var peerFixturePathPointer));
            var peerFixturePath = Marshal.PtrToStringUni(peerFixturePathPointer)
                ?? throw new InvalidOperationException("Could not resolve the native Shell fixture folder path.");
            Marshal.FreeCoTaskMem(peerFixturePathPointer);
            var createdPeerPath = Path.Combine(peerFixturePath, "ExplorerDirs-peer-change-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(createdPeerPath, "peer-shell-change");
                SHChangeNotify(ShellChangeCreated, ShellChangePathUnicode, createdPeerPath, null);
                WaitForPeerShellItem(folderView, createdPeerPath, shouldExist: true, initialCount + 1);
                var addedProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                    peerRows, ref nextPeerRowId, initialCount + 1, groupByType: true);
                peerRows = addedProjection.Rows;
                var addedPeerRow = peerRows.Single(row => string.Equals(row.Name, Path.GetFileName(createdPeerPath), StringComparison.OrdinalIgnoreCase));
                var originalRowsRetained = originalPeerIds.All(id => peerRows.Any(row => row.Id == id));
                var newRowIdentityAssigned = !originalPeerIds.Contains(addedPeerRow.Id) && peerRows.Select(row => row.Id).Distinct().Count() == peerRows.Count;
                var detailsAfterCreateAligned = originalRowsRetained && newRowIdentityAssigned;
                if (!detailsAfterCreateAligned)
                    throw new Exception("Shell create notification did not preserve existing peer PIDL rows and assign the new row a distinct identity.");

                File.Delete(createdPeerPath);
                SHChangeNotify(ShellChangeDeleted, ShellChangePathUnicode, createdPeerPath, null);
                WaitForPeerShellItem(folderView, createdPeerPath, shouldExist: false, initialCount);
                var deletedProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                    peerRows, ref nextPeerRowId, initialCount, groupByType: true);
                peerRows = deletedProjection.Rows;
                var deletedIdentityRetired = peerRows.All(row => row.Id != addedPeerRow.Id) &&
                    originalPeerIds.All(id => peerRows.Any(row => row.Id == id));
                if (!deletedIdentityRetired)
                    throw new Exception("Shell delete notification did not retire the removed peer row while preserving surviving PIDL identities.");
                Console.WriteLine($"ishellview-peer-shell-change: create=True;delete=True;existing-pidl-ids-retained={originalRowsRetained};new-row-id-assigned={newRowIdentityAssigned};deleted-row-id-retired={deletedIdentityRetired};details-reconciled=True");

                var renamedOldPath = Path.Combine(peerFixturePath, "keep.txt");
                var renamedNewPath = Path.Combine(peerFixturePath, "keep-renamed.txt");
                var renamedOldRow = peerRows.Single(row => string.Equals(row.Name, Path.GetFileName(renamedOldPath), StringComparison.OrdinalIgnoreCase));
                using var renameWatch = new ShellChangeReconciliationProbe.ChangeWindow();
                renameWatch.Register(folderPidl);
                var renameEventIndex = renameWatch.Events.Count;
                File.Move(renamedOldPath, renamedNewPath);
                SHChangeNotify(ShellChangeRenamedItem, ShellChangePathUnicode, renamedOldPath, renamedNewPath);
                if (!renameWatch.WaitForAnySince(renameEventIndex, [ShellChangeRenamedItem], TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The isolated Shell change window did not receive SHCNE_RENAMEITEM.");
                WaitForPeerShellRename(folderView, renamedOldPath, renamedNewPath, initialCount);
                var renamedProjection = ProjectPeerShellOrder(peerList, folderView, folderPidl, shellFolder,
                    peerRows, ref nextPeerRowId, initialCount, groupByType: true,
                    renamedParsingNames: renameWatch.RenamedParsingNames);
                peerRows = renamedProjection.Rows;
                var renamedPeerRow = peerRows.Single(row => string.Equals(row.Name, Path.GetFileName(renamedNewPath), StringComparison.OrdinalIgnoreCase));
                var renameIdentityPreserved = renamedPeerRow.Id == renamedOldRow.Id && renamedProjection.ExistingIdentitiesPreserved;
                var renamedDetailsUpdated = renamedPeerRow.DetailValues.Length > 0 &&
                    string.Equals(renamedPeerRow.DetailValues[0], Path.GetFileName(renamedNewPath), StringComparison.OrdinalIgnoreCase);
                var renameOtherIdsPreserved = originalPeerIds.Where(id => id != renamedOldRow.Id).All(id => peerRows.Any(row => row.Id == id));
                if (!renameIdentityPreserved || !renamedDetailsUpdated || !renameOtherIdsPreserved)
                    throw new Exception("Shell rename reconciliation did not preserve the old row ID and update the renamed row's details.");
                Console.WriteLine($"ishellview-peer-shell-change: rename=True;rename-notification=True;old-id={renamedOldRow.Id};new-id={renamedPeerRow.Id};identity-preserved={renameIdentityPreserved};details-updated={renamedDetailsUpdated};other-pidl-ids-retained={renameOtherIdsPreserved}");
            }
            finally
            {
                if (File.Exists(createdPeerPath)) File.Delete(createdPeerPath);
            }

            // This intentionally changes sort state only after the menu-command focus matrix;
            // the Shell view uses a disposable fixture and is torn down immediately afterward.
            VerifyColumnHeaderContextMenu(view, owner);
        }
        finally
        {
            var noGroup = default(ShellColumnManager.NativeKey);
            _ = At<SetGroupBy>(view2, 17)(view2, ref noGroup, 0);
            Marshal.Release(view2);
        }

        FreePeerShellRows(peerRows);
        Marshal.Release(shellFolder);
        VerifyPeerListDetails(peerList, peerRows);
        Console.WriteLine($"ishellview-menu-command: viewlogicaldetails=True; peer-view-mode=details; peer-stable-pidl-identity=True; peer-shell-icon-projection=True; peer-shell-details-columns=[{string.Join('|', peerRows[0].DetailTitles)}]; peer-rows-stable-during-refresh=True; peer-refresh-projection=True; peer-sort-projection=True; peer-listview-groups=True; peer-group-headings=[{string.Join('|', peerRows.Select(row => row.TypeName).Distinct(StringComparer.OrdinalIgnoreCase))}]; mode={modeAfterMenu}/{iconSizeAfterMenu}; refresh=True; sortdescending=True; sortascending=True; native-sort-order=[{string.Join(';', nativeSortOrders)}]; groupdescending=True; groupascending=True; focus-recovery=[{string.Join(',', menuCommandFocusRecovery)}]; visible-items={visibleCount}; parent-visible=False; foreground-unchanged=True");
    }

    private static nint CreatePeerListView(nint parent, nint systemImageList)
    {
        var listView = CreateWindowExW(0, "SysListView32", "",
            0x40000000u | 0x00000001u | 0x00000008u, 0, 0, 320, 240, parent, 0, 0, 0);
        if (listView == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the independent peer ListView.");
        _ = SendMessageW(listView, LvmSetImageList, 1, systemImageList); // LVSIL_SMALL; Shell-owned system image list.
        return listView;
    }

    private static void VerifyRecursiveSizePeerProjection(nint folderView, nint folderPidl, nint shellFolder,
        nint parent, int expectedCount, Action destroyShellView)
    {
        var foreground = GetForegroundWindow();
        var nextId = 0;
        var projection = CapturePeerShellRows(folderView, folderPidl, shellFolder, [], ref nextId);
        try
        {
            if (projection.Rows.Count != expectedCount)
                throw new InvalidOperationException($"Recursive-size peer row count mismatch: expected={expectedCount}, actual={projection.Rows.Count}.");
            var listView = CreatePeerListView(parent, projection.SystemImageList);
            PopulatePeerList(listView, projection.Rows);
            InsertPeerColumn(listView, projection.Rows[0].DetailTitles.Length, "Recursive size (on demand)");
            for (var index = 0; index < projection.Rows.Count; index++)
                SetPeerListCell(listView, index, projection.Rows[index].DetailTitles.Length, "");

            var scanPaths = projection.Rows.Where(row => row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
                .Select(row => Path.GetFullPath(row.FileSystemPath!)).ToArray();
            foreach (var expected in new[] { "size-2", "size-10", "size-100" })
                if (!scanPaths.Any(path => string.Equals(Path.GetFileName(path), expected, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"The Shell rows did not contain the recursive-size fixture '{expected}'.");

            var selectedRow = projection.Rows.Single(row => row.Name == "size-10");
            VerifyPeerShellCommandMenu(shellFolder, parent, selectedRow.RelativePidl);
            var selectedIndex = projection.Rows.IndexOf(selectedRow);
            SetPeerListItemState(listView, selectedIndex, 0x0003, 0x0003);
            var initialNames = ReadPeerListNames(listView);
            var initialIds = ReadPeerListRowIds(listView);
            var ownerThread = Environment.CurrentManagedThreadId;
            using var cancellation = new CancellationTokenSource();
            var requestedPath = Path.GetFullPath(selectedRow.FileSystemPath!);
            var scan = Task.Run(() =>
            {
                var workerThread = Environment.CurrentManagedThreadId;
                Thread.Sleep(100); // Keep the one-row on-demand request pending while the old rows remain visible.
                return (Rows: HandleBoundRecursiveDirectoryScanner.Scan([requestedPath], cancellation.Token), WorkerThread: workerThread);
            }, cancellation.Token);
            while (!scan.IsCompleted)
            {
                Application.DoEvents();
                if (!ReadPeerListNames(listView).SequenceEqual(initialNames, StringComparer.OrdinalIgnoreCase) ||
                    !ReadPeerListRowIds(listView).SequenceEqual(initialIds))
                    throw new InvalidOperationException("The peer list changed while the requested recursive value was loading.");
                Thread.Sleep(10);
            }
            var completed = scan.GetAwaiter().GetResult();
            if (completed.WorkerThread == ownerThread || Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Recursive sizes were not calculated off the Shell owner thread.");

            var sizes = completed.Rows.ToDictionary(pair => Path.GetFullPath(pair.Key), pair => pair.Value.Summary.Bytes,
                StringComparer.OrdinalIgnoreCase);
            if (sizes.Count != 1 || !sizes.ContainsKey(requestedPath))
                throw new InvalidOperationException("The on-demand request scanned folders that were not requested.");
            var recursiveSizeCache = new Dictionary<string, ulong>(sizes, StringComparer.OrdinalIgnoreCase);
            var bytesById = new Dictionary<int, ulong>();
            for (var index = 0; index < projection.Rows.Count; index++)
            {
                var row = projection.Rows[index];
                if (row.FileSystemPath is not null && sizes.TryGetValue(Path.GetFullPath(row.FileSystemPath), out var bytes))
                {
                    bytesById[row.Id] = bytes;
                    SetPeerListCell(listView, index, row.DetailTitles.Length,
                        bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
                }
            }
            var initialIndexById = initialIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
            var otherFolderRowsRemainPending = projection.Rows.Where(row => row.Id != selectedRow.Id &&
                    row.FileSystemPath is not null && Directory.Exists(row.FileSystemPath))
                .All(row => string.IsNullOrEmpty(GetPeerListCell(listView, initialIndexById[row.Id], row.DetailTitles.Length)));
            if (bytesById[selectedRow.Id] != 10 || !otherFolderRowsRemainPending)
                throw new InvalidOperationException("The on-demand scan populated an unrequested recursive-size row.");

            var remainingPaths = scanPaths.Where(path => !string.Equals(path, requestedPath, StringComparison.OrdinalIgnoreCase)).ToArray();
            var rowIndexByPath = projection.Rows.Select((row, index) => (row, index))
                .Where(item => item.row.FileSystemPath is not null)
                .ToDictionary(item => Path.GetFullPath(item.row.FileSystemPath!), item => item.index,
                    StringComparer.OrdinalIgnoreCase);
            var remainingScan = Task.Run(() =>
                HandleBoundRecursiveDirectoryScanner.Scan(remainingPaths, cancellation.Token), cancellation.Token);
            while (!remainingScan.IsCompleted)
            {
                Application.DoEvents();
                if (!ReadPeerListRowIds(listView).SequenceEqual(initialIds))
                    throw new InvalidOperationException("The peer row identities changed while additional recursive values were loading.");
                Thread.Sleep(10);
            }
            foreach (var pair in remainingScan.GetAwaiter().GetResult())
            {
                var fullPath = Path.GetFullPath(pair.Key);
                sizes[fullPath] = pair.Value.Summary.Bytes;
                recursiveSizeCache[fullPath] = pair.Value.Summary.Bytes;
                if (!rowIndexByPath.TryGetValue(fullPath, out var rowIndex))
                    throw new InvalidOperationException("A requested recursive-size result no longer maps to a Shell row.");
                var row = projection.Rows[rowIndex];
                bytesById[row.Id] = pair.Value.Summary.Bytes;
                SetPeerListCell(listView, rowIndex, row.DetailTitles.Length,
                    pair.Value.Summary.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
            }
            if (bytesById[selectedRow.Id] != 10 || bytesById[projection.Rows.Single(row => row.Name == "size-2").Id] != 2 ||
                bytesById[projection.Rows.Single(row => row.Name == "size-100").Id] != 100)
                throw new InvalidOperationException("The recursive-size peer values did not match the fixture bytes.");

            SortPeerListByRecursiveSize(listView, bytesById);

            var sortedRows = ReadPeerListNames(listView);
            var sortedIds = ReadPeerListRowIds(listView);
            var expectedOrder = projection.Rows.OrderBy(row => bytesById.ContainsKey(row.Id) ? 0 : 1)
                .ThenBy(row => bytesById.TryGetValue(row.Id, out var bytes) ? bytes : ulong.MaxValue)
                .ThenBy(row => row.Id).ToArray();
            var selectedAfterSort = Array.IndexOf(sortedIds, selectedRow.Id);
            var selectedState = selectedAfterSort < 0 ? 0 : GetPeerListItemState(listView, selectedAfterSort, 0x0003);
            if (!sortedIds.SequenceEqual(expectedOrder.Select(row => row.Id)) || selectedState != 0x0003)
                throw new InvalidOperationException($"Recursive-size sort order or selected row identity was lost: ids=[{string.Join(',', sortedIds)}], expected=[{string.Join(',', expectedOrder.Select(row => row.Id))}], selected-state=0x{selectedState:X}.");
            Console.WriteLine($"ishellview-peer-recursive-size: shell-rows={projection.Rows.Count}; requested-only-first=True; other-rows-pending={otherFolderRowsRemainPending}; worker-thread={completed.WorkerThread}; owner-thread={ownerThread}; rows-retained-during-scan=True; bytes=[size-2:2,size-10:10,size-100:100]; numeric-order=[{string.Join(',', sortedRows)}]; selected-focused-id={selectedRow.Id}; selection-focus-preserved=True; foreground-unchanged={GetForegroundWindow() == foreground}; product-view-changed=False");

            var changedDirectory = Path.GetFullPath(projection.Rows.Single(row => row.Name == "size-10").FileSystemPath!);
            var changedPayload = Path.Combine(changedDirectory, "payload.bin");
            using var changeWatch = new ShellChangeReconciliationProbe.ChangeWindow();
            changeWatch.Register(folderPidl);
            var eventIndex = changeWatch.Events.Count;
            File.WriteAllBytes(changedPayload, new byte[12]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            File.WriteAllBytes(changedPayload, new byte[16]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            File.WriteAllBytes(changedPayload, new byte[20]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            SHChangeNotify(ShellChangeUpdatedDirectory, ShellChangePathUnicode, changedDirectory, null);
            if (!changeWatch.WaitForAnySince(eventIndex, [ShellChangeUpdatedItem, ShellChangeUpdatedDirectory], TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The Shell change window did not receive the recursive-size fixture update notification.");
            var lastNotificationCount = -1;
            var lastNotificationAt = Environment.TickCount64;
            var notificationDeadline = lastNotificationAt + 5000;
            while (Environment.TickCount64 < notificationDeadline &&
                   Environment.TickCount64 - lastNotificationAt < 150)
            {
                Application.DoEvents();
                var observedCount = changeWatch.ChangeEvents.Count;
                if (observedCount != lastNotificationCount)
                {
                    lastNotificationCount = observedCount;
                    lastNotificationAt = Environment.TickCount64;
                }
                Thread.Sleep(10);
            }
            var updateEvents = changeWatch.ChangeEvents.Skip(eventIndex)
                .Where(change => change.EventId is ShellChangeUpdatedItem or ShellChangeUpdatedDirectory).ToArray();
            if (updateEvents.Length < 2)
                throw new InvalidOperationException($"Expected duplicate Shell update notifications for the burst; observed={updateEvents.Length}.");
            var dirtyDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in updateEvents)
            {
                if (change.FirstPath is not { } changedPath) continue;
                var fullChangedPath = Path.GetFullPath(changedPath);
                if (string.Equals(fullChangedPath, changedDirectory, StringComparison.OrdinalIgnoreCase) ||
                    fullChangedPath.StartsWith(changedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    dirtyDirectories.Add(changedDirectory);
            }
            if (dirtyDirectories.Count != 1)
                throw new InvalidOperationException($"The update burst did not coalesce to the changed directory: paths=[{string.Join('|', updateEvents.Select(change => change.FirstPath))}].");
            WaitForPeerShellItem(folderView, changedDirectory, shouldExist: true, expectedCount);

            var refreshedProjection = CapturePeerShellRows(folderView, folderPidl, shellFolder, projection.Rows, ref nextId);
            if (refreshedProjection.Rows.Count != expectedCount ||
                !refreshedProjection.Rows.Select(row => row.Id).ToHashSet().SetEquals(sortedIds))
                throw new InvalidOperationException("Shell re-enumeration changed peer identities during recursive-size invalidation.");
            projection = refreshedProjection;

            var cacheEntryInvalidated = recursiveSizeCache.Remove(changedDirectory);
            if (!cacheEntryInvalidated || sizes[changedDirectory] != 10)
                throw new InvalidOperationException("The Shell update did not invalidate the stale recursive-size snapshot.");
            using var refreshCancellation = new CancellationTokenSource();
            var refreshScan = Task.Run(() =>
            {
                var workerThread = Environment.CurrentManagedThreadId;
                Thread.Sleep(150); // Keep the coalesced invalidation pending while the peer list remains unchanged.
                return (Rows: HandleBoundRecursiveDirectoryScanner.Scan(dirtyDirectories, refreshCancellation.Token), WorkerThread: workerThread);
            }, refreshCancellation.Token);
            while (!refreshScan.IsCompleted)
            {
                Application.DoEvents();
                if (!ReadPeerListRowIds(listView).SequenceEqual(sortedIds) ||
                    GetPeerListItemState(listView, selectedAfterSort, 0x0003) != 0x0003)
                    throw new InvalidOperationException("The peer rows or selection changed while the coalesced recursive-size refresh was pending.");
                Thread.Sleep(10);
            }
            var refreshedScan = refreshScan.GetAwaiter().GetResult();
            if (refreshedScan.WorkerThread == ownerThread || !refreshedScan.Rows.TryGetValue(changedDirectory, out var changedSummary) ||
                changedSummary.Summary.Bytes != 20)
                throw new InvalidOperationException("The invalidated recursive-size row did not rescan to the updated value.");

            long scanGeneration = 0;
            var scanStartedGeneration = Interlocked.Read(ref scanGeneration);
            using var scanOpened = new ManualResetEventSlim();
            using var continueScan = new ManualResetEventSlim();
            var supersededScan = Task.Run(() =>
            {
                var workerThread = Environment.CurrentManagedThreadId;
                var rows = HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory], refreshCancellation.Token,
                    (_, _) =>
                    {
                        scanOpened.Set();
                        if (!continueScan.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("The in-flight recursive scan was not released by the notification race probe.");
                    });
                return (Rows: rows, WorkerThread: workerThread);
            }, refreshCancellation.Token);
            var scanOpenDeadline = Environment.TickCount64 + 5000;
            while (!scanOpened.IsSet && Environment.TickCount64 < scanOpenDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!scanOpened.IsSet)
            {
                continueScan.Set();
                throw new TimeoutException("The worker did not open the recursive-size directory before the notification race.");
            }
            if (!ReadPeerListRowIds(listView).SequenceEqual(sortedIds) ||
                GetPeerListItemState(listView, selectedAfterSort, 0x0003) != 0x0003)
                throw new InvalidOperationException("The peer selection changed before the in-flight notification race.");

            var raceEventIndex = changeWatch.ChangeEvents.Count;
            File.WriteAllBytes(changedPayload, new byte[30]);
            SHChangeNotify(ShellChangeUpdatedItem, ShellChangePathUnicode, changedPayload, null);
            SHChangeNotify(ShellChangeUpdatedDirectory, ShellChangePathUnicode, changedDirectory, null);
            if (!changeWatch.WaitForAnySince(raceEventIndex, [ShellChangeUpdatedItem, ShellChangeUpdatedDirectory], TimeSpan.FromSeconds(5)))
            {
                continueScan.Set();
                throw new TimeoutException("The Shell change window did not receive the update that supersedes the in-flight recursive scan.");
            }
            var currentScanGeneration = Interlocked.Increment(ref scanGeneration);
            continueScan.Set();
            var supersededResult = supersededScan.GetAwaiter().GetResult();
            var supersededValueDiscarded = currentScanGeneration != scanStartedGeneration &&
                supersededResult.WorkerThread != ownerThread;
            if (!supersededValueDiscarded || !supersededResult.Rows.TryGetValue(changedDirectory, out var supersededSummary) ||
                supersededSummary.Summary.Bytes != 30)
                throw new InvalidOperationException("The in-flight result was not retired after a newer Shell update arrived.");

            using var finalScanCancellation = new CancellationTokenSource();
            var finalScan = Task.Run(() => HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory], finalScanCancellation.Token));
            while (!finalScan.IsCompleted)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            var finalScanResult = finalScan.GetAwaiter().GetResult();
            if (!finalScanResult.TryGetValue(changedDirectory, out var finalSummary) || finalSummary.Summary.Bytes != 30)
                throw new InvalidOperationException("The follow-up recursive-size scan did not read the latest value after an in-flight update.");
            recursiveSizeCache[changedDirectory] = finalSummary.Summary.Bytes;

            var retiredScanGeneration = Interlocked.Read(ref scanGeneration);
            using var retiredScanCancellation = new CancellationTokenSource();
            using var retiredScanOpened = new ManualResetEventSlim();
            using var retireScan = new ManualResetEventSlim();
            var retiredScan = Task.Run(() => HandleBoundRecursiveDirectoryScanner.Scan([changedDirectory],
                retiredScanCancellation.Token, (_, _) =>
                {
                    retiredScanOpened.Set();
                    if (!retireScan.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The retired recursive scan was not released by the cancellation probe.");
                }));
            var retiredScanDeadline = Environment.TickCount64 + 5000;
            while (!retiredScanOpened.IsSet && Environment.TickCount64 < retiredScanDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!retiredScanOpened.IsSet)
            {
                retireScan.Set();
                throw new TimeoutException("The worker did not open the directory before the retirement cancellation.");
            }
            _ = Interlocked.Increment(ref scanGeneration);
            destroyShellView();
            retiredScanCancellation.Cancel();
            retireScan.Set();
            var retiredFailurePreservedValue = false;
            try { _ = retiredScan.GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                retiredFailurePreservedValue = Interlocked.Read(ref scanGeneration) != retiredScanGeneration &&
                    recursiveSizeCache.TryGetValue(changedDirectory, out var cachedBytes) && cachedBytes == 30;
            }
            if (!retiredFailurePreservedValue || !ReadPeerListRowIds(listView).SequenceEqual(sortedIds) ||
                GetPeerListItemState(listView, selectedAfterSort, 0x0003) != 0x0003)
                throw new InvalidOperationException("A retired recursive scan failure cleared the newer cached value or disturbed the peer selection.");

            PopulatePeerList(listView, projection.Rows);
            InsertPeerColumn(listView, projection.Rows[0].DetailTitles.Length, "Recursive size (on demand)");
            var updatedBytesById = new Dictionary<int, ulong>();
            for (var index = 0; index < projection.Rows.Count; index++)
            {
                var row = projection.Rows[index];
                if (row.FileSystemPath is not null && recursiveSizeCache.TryGetValue(Path.GetFullPath(row.FileSystemPath), out var bytes))
                {
                    updatedBytesById[row.Id] = bytes;
                    SetPeerListCell(listView, index, row.DetailTitles.Length,
                        bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B");
                }
            }
            var refreshedSelectedRow = projection.Rows.Single(row => row.Id == selectedRow.Id);
            SetPeerListItemState(listView, projection.Rows.IndexOf(refreshedSelectedRow), 0x0003, 0x0003);
            SortPeerListByRecursiveSize(listView, updatedBytesById);
            var refreshedNames = ReadPeerListNames(listView);
            var refreshedIds = ReadPeerListRowIds(listView);
            var expectedRefreshedOrder = projection.Rows.OrderBy(row => updatedBytesById.ContainsKey(row.Id) ? 0 : 1)
                .ThenBy(row => updatedBytesById.TryGetValue(row.Id, out var bytes) ? bytes : ulong.MaxValue)
                .ThenBy(row => row.Id).ToArray();
            var refreshedSelectedIndex = Array.IndexOf(refreshedIds, selectedRow.Id);
            if (updatedBytesById[selectedRow.Id] != 30 || !refreshedIds.SequenceEqual(expectedRefreshedOrder.Select(row => row.Id)) ||
                refreshedSelectedIndex < 0 || GetPeerListItemState(listView, refreshedSelectedIndex, 0x0003) != 0x0003 ||
                !refreshedNames.SequenceEqual(expectedRefreshedOrder.Select(row => row.Name), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The updated recursive-size value did not flow through peer sort/reselection: names=[{string.Join(',', refreshedNames)}], value={updatedBytesById.GetValueOrDefault(selectedRow.Id)}.");
            Console.WriteLine($"ishellview-peer-recursive-size-notification: shell-update-events={updateEvents.Length}; changed-directories-coalesced={dirtyDirectories.Count}; peer-rows-retained-during-worker-rescan=True; superseded-inflight-result-discarded={supersededValueDiscarded}; shell-view-destroyed-during-scan=True; retired-scan-canceled=True; newer-cache-preserved={retiredFailurePreservedValue}; follow-up-bytes={updatedBytesById[selectedRow.Id]}; peer-rows-reenumerated=True; row-ids-preserved=True; stale-cache-invalidated={cacheEntryInvalidated}; rescan-worker={refreshedScan.WorkerThread}; numeric-order=[{string.Join(',', refreshedNames)}]; selected-focused-id={selectedRow.Id}; selection-focus-preserved=True; foreground-unchanged={GetForegroundWindow() == foreground}");
        }
        finally
        {
            FreePeerShellRows(projection.Rows);
        }
    }

    private static void SetPeerListItemState(nint listView, int index, uint state, uint mask)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            Marshal.StructureToPtr(new NativeListViewItem { Item = index, State = state, StateMask = mask }, pointer, false);
            _ = SendMessageW(listView, LvmSetItemState, index, pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static uint GetPeerListItemState(nint listView, int index, uint mask) =>
        unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)mask));

    private static void SortPeerListByRecursiveSize(nint listView, Dictionary<int, ulong> bytesById)
    {
        var sortState = GCHandle.Alloc(bytesById);
        NativeListViewCompare compare = (first, second, data) =>
        {
            var values = (Dictionary<int, ulong>)GCHandle.FromIntPtr(data).Target!;
            var firstId = unchecked((int)first);
            var secondId = unchecked((int)second);
            var hasFirst = values.TryGetValue(firstId, out var firstBytes);
            var hasSecond = values.TryGetValue(secondId, out var secondBytes);
            if (hasFirst != hasSecond) return hasFirst ? -1 : 1;
            var sizeOrder = hasFirst ? firstBytes.CompareTo(secondBytes) : 0;
            return sizeOrder != 0 ? sizeOrder : firstId.CompareTo(secondId);
        };
        try
        {
            if (SendMessageW(listView, LvmSortItems, GCHandle.ToIntPtr(sortState),
                Marshal.GetFunctionPointerForDelegate(compare)).ToInt32() == 0)
                throw new InvalidOperationException("The peer ListView rejected recursive-size sorting.");
        }
        finally { sortState.Free(); GC.KeepAlive(compare); }
    }

    private static void SetPeerListMode(nint listView, long mode)
    {
        var style = GetWindowLongPtrW(listView, GwlStyle).ToInt64();
        _ = SetWindowLongPtrW(listView, GwlStyle, (nint)((style & ~LvsTypeMask) | mode));
    }

    private static void PopulatePeerList(nint listView, IReadOnlyList<PeerShellRow> rows, bool groupByType = false)
    {
        _ = SendMessageW(listView, LvmDeleteAllItems, 0, 0);
        _ = SendMessageW(listView, LvmEnableGroupView, 0, 0);
        _ = SendMessageW(listView, LvmRemoveAllGroups, 0, 0);
        RemovePeerColumns(listView);
        if (rows.Count > 0)
            for (var columnIndex = 0; columnIndex < rows[0].DetailTitles.Length; columnIndex++)
                InsertPeerColumn(listView, columnIndex, rows[0].DetailTitles[columnIndex]);
        var groupIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groupInsertResults = new List<int>();
        if (groupByType)
        {
            foreach (var title in rows.Select(row => row.TypeName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("The Shell did not provide a type label for grouping.");
                var header = Marshal.StringToHGlobalUni(title);
                var groupPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewGroup>());
                try
                {
                    var groupId = groupIds.Count + 1;
                    var group = new NativeListViewGroup
                    {
                        Size = (uint)Marshal.SizeOf<NativeListViewGroup>(), Mask = 0x0001 | 0x0010,
                        Header = header, HeaderCapacity = title.Length, GroupId = groupId
                    };
                    Marshal.StructureToPtr(group, groupPointer, false);
                    var insertResult = SendMessageW(listView, LvmInsertGroup, groupIds.Count, groupPointer).ToInt32();
                    groupInsertResults.Add(insertResult);
                    if (insertResult < 0)
                        throw new InvalidOperationException($"Could not insert the Shell type group '{title}'.");
                    groupIds.Add(title, groupId);
                }
                finally { Marshal.FreeHGlobal(groupPointer); Marshal.FreeHGlobal(header); }
            }
            var enableResult = SendMessageW(listView, LvmEnableGroupView, 1, 0).ToInt32();
            Console.WriteLine($"peer-list-groups: inserted=[{string.Join(',', groupInsertResults)}]; enable-result={enableResult}; count-after-enable={SendMessageW(listView, LvmGetGroupCount, 0, 0)}; enabled-after-enable={SendMessageW(listView, LvmIsGroupViewEnabled, 0, 0)}");
            if (enableResult < 0)
                throw new InvalidOperationException("The peer ListView rejected group view mode.");
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var firstValue = row.DetailValues.Length > 0 ? row.DetailValues[0] : row.Name;
            var text = Marshal.StringToHGlobalUni(firstValue);
            var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
            try
            {
                var item = new NativeListViewItem { Mask = 0x0001 | 0x0002 | 0x0004 | (groupByType ? 0x0100u : 0), Item = index, SubItem = 0, Text = text,
                    TextCapacity = firstValue.Length + 1, Image = row.SystemImageIndex, Parameter = row.Id,
                    GroupId = groupByType ? groupIds[row.TypeName] : 0 };
                Marshal.StructureToPtr(item, itemPointer, false);
                if (SendMessageW(listView, LvmInsertItemW, 0, itemPointer).ToInt32() != index)
                    throw new InvalidOperationException($"Could not populate peer ListView row {index}.");
            }
            finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(text); }
            for (var columnIndex = 1; columnIndex < row.DetailValues.Length; columnIndex++)
                SetPeerListCell(listView, index, columnIndex, row.DetailValues[columnIndex]);
        }
    }

    private static void RemovePeerColumns(nint listView)
    {
        var header = SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
        // Hidden empty report controls may materialize their header only after the first column is inserted.
        if (header == 0) return;
        while (SendMessageW(header, 0x1200, 0, 0).ToInt32() > 0) // HDM_GETITEMCOUNT
            if (SendMessageW(listView, 0x101C, 0, 0) == 0) // LVM_DELETECOLUMN
                throw new InvalidOperationException("Could not reset peer ListView columns.");
    }

    private static void InsertPeerColumn(nint listView, int index, string titleText)
    {
        var title = Marshal.StringToHGlobalUni(titleText);
        var columnPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewColumn>());
        try
        {
            var column = new NativeListViewColumn { Mask = 0x0001 | 0x0002 | 0x0004, Width = index == 0 ? 220 : 150, Text = title, TextCapacity = titleText.Length + 1, SubItem = index };
            Marshal.StructureToPtr(column, columnPointer, false);
            if (SendMessageW(listView, LvmInsertColumnW, index, columnPointer).ToInt32() < 0)
                throw new InvalidOperationException($"Could not add peer ListView column '{titleText}'.");
        }
        finally { Marshal.FreeHGlobal(columnPointer); Marshal.FreeHGlobal(title); }
    }

    private static void SetPeerListCell(nint listView, int row, int column, string value)
    {
        var text = Marshal.StringToHGlobalUni(value);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            Marshal.StructureToPtr(new NativeListViewItem { Item = row, SubItem = column, Text = text, TextCapacity = value.Length + 1 }, itemPointer, false);
            if (SendMessageW(listView, LvmSetItemTextW, row, itemPointer) == 0)
                throw new InvalidOperationException($"Could not populate peer ListView cell ({row}, {column}).");
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(text); }
    }

    private static string GetPeerListCell(nint listView, int row, int column)
    {
        var buffer = Marshal.AllocHGlobal(1024);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            var item = new NativeListViewItem { Item = row, SubItem = column, Text = buffer, TextCapacity = 512 };
            Marshal.StructureToPtr(item, itemPointer, false);
            _ = SendMessageW(listView, LvmGetItemTextW, row, itemPointer);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(buffer); }
    }

    private static void VerifyPeerShellCommandMenu(nint shellFolder, nint owner, nint relativePidl)
    {
        var children = Marshal.AllocHGlobal(IntPtr.Size);
        nint contextMenu = 0;
        object? contextObject = null;
        try
        {
            Marshal.WriteIntPtr(children, relativePidl);
            var menuId = ContextMenuId;
            Check(At<GetUIObjectOf>(shellFolder, 10)(shellFolder, owner, 1, children, in menuId, 0, out contextMenu));
            if (contextMenu == 0) throw new InvalidOperationException("The selected Shell row did not provide an IContextMenu.");
            contextObject = Marshal.GetObjectForIUnknown(contextMenu);
        }
        finally
        {
            if (contextMenu != 0) Marshal.Release(contextMenu);
            Marshal.FreeHGlobal(children);
        }

        using var commands = new NativeShellCommandMenu(contextObject!, owner);
        var requiredVerbs = new[] { "cut", "copy", "delete", "rename", "properties" };
        var enabledVerbs = requiredVerbs.Where(commands.IsEnabled).ToArray();
        if (enabledVerbs.Length != requiredVerbs.Length)
            throw new InvalidOperationException($"The product Shell command adapter did not expose all selected-row verbs: enabled=[{string.Join(',', enabledVerbs)}].");
        Console.WriteLine($"ishellview-peer-selected-context-menu: source=IShellFolder.GetUIObjectOf; adapter=NativeShellCommandMenu; enabled=[{string.Join(',', enabledVerbs)}]; popup-shown=False; command-invoked=False");
    }

    private static void VerifyPeerListDetails(nint listView, IReadOnlyList<PeerShellRow> rows)
    {
        if (SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32() != rows.Count)
            throw new Exception("The peer ListView details row count differs from the Shell model.");
        var header = SendMessageW(listView, 0x101F, 0, 0);
        if (header == 0 || SendMessageW(header, 0x1200, 0, 0).ToInt32() != rows[0].DetailTitles.Length)
            throw new Exception("The peer ListView details header count differs from Shell IShellFolder2.");
        var buffer = Marshal.AllocHGlobal(512 * sizeof(char));
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            for (var row = 0; row < rows.Count; row++)
            {
                for (var column = 0; column < rows[row].DetailTitles.Length; column++)
                {
                    Marshal.WriteInt16(buffer, 0);
                    Marshal.StructureToPtr(new NativeListViewItem { Item = row, SubItem = column, Text = buffer, TextCapacity = 512 }, itemPointer, false);
                    _ = SendMessageW(listView, LvmGetItemTextW, row, itemPointer);
                    var actual = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                    if (!string.Equals(actual, rows[row].DetailValues[column], StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Peer ListView detail mismatch at row {row}, column {column} ('{rows[row].DetailTitles[column]}'): expected='{rows[row].DetailValues[column]}', actual='{actual}'.");
                }
            }
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(buffer); }
    }

    private static string[] ReadPeerListNames(nint listView)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        var rows = new string[count];
        var buffer = Marshal.AllocHGlobal(1024);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            for (var index = 0; index < count; index++)
            {
                var item = new NativeListViewItem { Item = index, SubItem = 0, Text = buffer, TextCapacity = 512 };
                Marshal.StructureToPtr(item, itemPointer, false);
                _ = SendMessageW(listView, LvmGetItemTextW, index, itemPointer);
                rows[index] = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            return rows;
        }
        finally { Marshal.FreeHGlobal(itemPointer); Marshal.FreeHGlobal(buffer); }
    }

    private static int[] ReadPeerListRowIds(nint listView)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        var ids = new int[count];
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            for (var index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(new NativeListViewItem { Mask = 0x0004, Item = index }, itemPointer, false);
                if (SendMessageW(listView, LvmGetItemW, 0, itemPointer) == 0)
                    throw new InvalidOperationException($"Could not read peer ListView row identity {index}.");
                ids[index] = Marshal.PtrToStructure<NativeListViewItem>(itemPointer).Parameter.ToInt32();
            }
            return ids;
        }
        finally { Marshal.FreeHGlobal(itemPointer); }
    }

    private static (List<PeerShellRow> Rows, nint SystemImageList) CapturePeerShellRows(nint folderView,
        nint folderPidl, nint shellFolder, IReadOnlyList<PeerShellRow> previous, ref int nextId,
        IReadOnlyDictionary<string, string>? renamedParsingNames = null)
    {
        Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
        var enumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
        nint shellFolder2 = 0;
        try
        {
            var folder2Id = ShellFolder2Id;
            Check(Marshal.QueryInterface(shellFolder, in folder2Id, out shellFolder2));
            var detailTitles = ReadShellDetailColumnTitles(shellFolder2);
            if (detailTitles.Length < 2)
                throw new InvalidOperationException("The Shell folder did not expose multiple standard details columns.");
            var byIdentity = enumeration.Rows.ToDictionary(row => GetPidlIdentity(row.AbsolutePidl), StringComparer.Ordinal);
            var current = new List<PeerShellRow>(count);
            for (var index = 0; index < count; index++)
            {
                Check(At<GetItem>(folderView, 6)(folderView, index, out var childPidl));
                nint absolutePidl = 0, retainedPidl = 0;
                try
                {
                    absolutePidl = ILCombine(folderPidl, childPidl);
                    if (absolutePidl == 0) throw new InvalidOperationException("Could not combine the view item with its parent PIDL.");
                    var identity = GetPidlIdentity(absolutePidl);
                    if (!byIdentity.TryGetValue(identity, out var row))
                    {
                        Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolutePidl, SigDnFileSystemPath, out var viewPathPointer));
                        string viewPath;
                        try { viewPath = Marshal.PtrToStringUni(viewPathPointer) ?? "<unknown>"; }
                        finally { Marshal.FreeCoTaskMem(viewPathPointer); }
                        row = enumeration.Rows.SingleOrDefault(candidate => string.Equals(candidate.FileSystemPath,
                            viewPath, StringComparison.OrdinalIgnoreCase));
                        if (row is null)
                        {
                            var enumeratedPaths = string.Join(" | ", enumeration.Rows.Select(candidate => candidate.FileSystemPath ?? candidate.ParsingName));
                            throw new InvalidOperationException($"The Shell view row '{viewPath}' identity was absent from folder enumeration. Enumerated=[{enumeratedPaths}].");
                        }
                    }
                    retainedPidl = ILClone(row.AbsolutePidl);
                    if (retainedPidl == 0) throw new InvalidOperationException("Could not retain a projected Shell row PIDL.");
                    var groupTitle = !string.IsNullOrWhiteSpace(row.TypeName)
                        ? row.TypeName
                        : (row.Attributes & 0x20000000u) != 0
                            ? "File folder"
                            : string.IsNullOrWhiteSpace(row.FileSystemPath) || string.IsNullOrWhiteSpace(Path.GetExtension(row.FileSystemPath))
                                ? "File"
                                : Path.GetExtension(row.FileSystemPath).TrimStart('.').ToUpperInvariant() + " file";
                    var detailValues = ReadShellDetailValues(shellFolder2, row.RelativePidl, detailTitles.Length);
                    var retainedRelativePidl = ILClone(row.RelativePidl);
                    if (retainedRelativePidl == 0) throw new InvalidOperationException("Could not retain a projected Shell child PIDL.");
                    current.Add(new PeerShellRow(0, retainedPidl, retainedRelativePidl, row.DisplayName, row.ParsingName,
                        row.FileSystemPath, groupTitle, row.SystemImageIndex, detailTitles, detailValues)
                    { FileSystemIdentity = row.FileSystemIdentity });
                    retainedPidl = 0;
                }
                finally
                {
                    if (absolutePidl != 0) ILFree(absolutePidl);
                    if (retainedPidl != 0) ILFree(retainedPidl);
                    Marshal.FreeCoTaskMem(childPidl);
                }
            }

            var assigned = new List<PeerShellRow>(current.Count);
            var usedPreviousIds = new HashSet<int>();
            foreach (var row in current)
            {
                var old = previous.FirstOrDefault(candidate => ILIsEqual(candidate.AbsolutePidl, row.AbsolutePidl));
                if (old is null && row.FileSystemIdentity is { } identity)
                    old = previous.FirstOrDefault(candidate => candidate.FileSystemIdentity == identity);
                if (old is null && renamedParsingNames is not null &&
                    renamedParsingNames.TryGetValue(row.ParsingName, out var oldParsingName))
                    old = previous.FirstOrDefault(candidate => string.Equals(candidate.ParsingName, oldParsingName,
                        StringComparison.OrdinalIgnoreCase));
                if (old is null)
                    assigned.Add(row with { Id = ++nextId });
                else
                {
                    usedPreviousIds.Add(old.Id);
                    ILFree(row.AbsolutePidl);
                    assigned.Add(row with { Id = old.Id, AbsolutePidl = old.AbsolutePidl });
                    if (old.RelativePidl != 0) ILFree(old.RelativePidl);
                }
            }
            foreach (var old in previous)
                if (!usedPreviousIds.Contains(old.Id))
                {
                    if (old.AbsolutePidl != 0) ILFree(old.AbsolutePidl);
                    if (old.RelativePidl != 0) ILFree(old.RelativePidl);
                }
            return (assigned, enumeration.SystemImageList);
        }
        finally
        {
            if (shellFolder2 != 0) Marshal.Release(shellFolder2);
            ShellListViewEnumerator.FreeRows(enumeration.Rows);
        }
    }

    private static string[] ReadShellDetailColumnTitles(nint shellFolder2)
    {
        var titles = new string[4];
        for (uint index = 0; index < titles.Length; index++)
        {
            var hr = ReadShellDetail(shellFolder2, 0, index, out var title);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            titles[index] = title;
        }
        if (string.IsNullOrWhiteSpace(titles[0]))
            throw new InvalidOperationException("IShellFolder2 returned no name column title.");
        return titles;
    }

    private static string[] ReadShellDetailValues(nint shellFolder2, nint relativePidl, int count)
    {
        var values = new string[count];
        for (uint index = 0; index < count; index++)
        {
            var hr = ReadShellDetail(shellFolder2, relativePidl, index, out values[index]);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        return values;
    }

    private static void WaitForPeerShellItem(nint folderView, string path, bool shouldExist, int expectedCount)
    {
        var itemName = Path.GetFileName(path);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var stableSamples = 0;
        while (DateTime.UtcNow < deadline && stableSamples < 3)
        {
            Application.DoEvents();
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
            var names = count > 0 ? ReadVisibleItemNames(folderView, count) : [];
            var itemPresent = names.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            stableSamples = count == expectedCount && itemPresent == shouldExist ? stableSamples + 1 : 0;
            if (stableSamples < 3) Thread.Sleep(50);
        }
        if (stableSamples < 3)
            throw new TimeoutException($"The Shell view did not settle to {expectedCount} rows with '{itemName}' present={shouldExist}.");
    }

    private static void WaitForPeerShellRename(nint folderView, string oldPath, string newPath, int expectedCount)
    {
        var oldName = Path.GetFileName(oldPath);
        var newName = Path.GetFileName(newPath);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var stableSamples = 0;
        while (DateTime.UtcNow < deadline && stableSamples < 3)
        {
            Application.DoEvents();
            Check(At<ItemCount>(folderView, 7)(folderView, 2, out var count));
            var names = count > 0 ? ReadVisibleItemNames(folderView, count) : [];
            var renameSettled = count == expectedCount && names.Contains(newName, StringComparer.OrdinalIgnoreCase) &&
                !names.Contains(oldName, StringComparer.OrdinalIgnoreCase);
            stableSamples = renameSettled ? stableSamples + 1 : 0;
            if (stableSamples < 3) Thread.Sleep(50);
        }
        if (stableSamples < 3)
            throw new TimeoutException($"The Shell view did not settle after renaming '{oldName}' to '{newName}'.");
    }

    private static int ReadShellDetail(nint shellFolder2, nint item, uint column, out string text)
    {
        var details = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeShellDetails>());
        try
        {
            Marshal.StructureToPtr(default(NativeShellDetails), details, false);
            var hr = At<GetDetailsOf>(shellFolder2, 18)(shellFolder2, item, column, details);
            if (hr < 0)
            {
                text = string.Empty;
                return hr;
            }
            var value = Marshal.PtrToStructure<NativeShellDetails>(details);
            if (value.Text.Type == 0)
            {
                if (value.Text.Pointer == 0) text = string.Empty;
                else
                {
                    try { text = Marshal.PtrToStringUni(value.Text.Pointer) ?? string.Empty; }
                    finally { Marshal.FreeCoTaskMem(value.Text.Pointer); }
                }
                return hr;
            }
            var buffer = new System.Text.StringBuilder(512);
            var strret = IntPtr.Add(details, Marshal.OffsetOf<NativeShellDetails>(nameof(NativeShellDetails.Text)).ToInt32());
            hr = StrRetToBufW(strret, item, buffer, (uint)buffer.Capacity);
            text = hr < 0 ? string.Empty : buffer.ToString();
            return hr;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static (List<PeerShellRow> Rows, bool ExistingIdentitiesPreserved) ProjectPeerShellOrder(nint listView,
        nint folderView, nint folderPidl, nint shellFolder, IReadOnlyList<PeerShellRow> previous,
        ref int nextId, int expectedCount, bool groupByType = false,
        IReadOnlyDictionary<string, string>? renamedParsingNames = null)
    {
        var projection = CapturePeerShellRows(folderView, folderPidl, shellFolder, previous, ref nextId, renamedParsingNames);
        if (projection.Rows.Count != expectedCount)
            throw new Exception($"The Shell row projection count differs: expected={expectedCount}, actual={projection.Rows.Count}.");
        var idsPreserved = projection.Rows.Count == previous.Count &&
            projection.Rows.Select(row => row.Id).ToHashSet().SetEquals(previous.Select(row => row.Id));
        PopulatePeerList(listView, projection.Rows, groupByType);
        if (!ReadPeerListNames(listView).SequenceEqual(projection.Rows.Select(row => row.Name), StringComparer.OrdinalIgnoreCase) ||
            !ReadPeerListRowIds(listView).SequenceEqual(projection.Rows.Select(row => row.Id)))
            throw new Exception("The independent ListView did not match Shell row order and stable PIDL identities after projection.");
        VerifyPeerListDetails(listView, projection.Rows);
        return (projection.Rows, idsPreserved);
    }

    private static void VerifyPeerListGrouping(nint listView, IReadOnlyList<PeerShellRow> rows)
    {
        var expected = rows.Select(row => row.TypeName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var enabled = SendMessageW(listView, LvmIsGroupViewEnabled, 0, 0).ToInt32() != 0;
        var actual = SendMessageW(listView, LvmGetGroupCount, 0, 0).ToInt32();
        if (!enabled || actual != expected)
            throw new InvalidOperationException($"Peer ListView group projection mismatch: enabled={enabled}, groups={actual}, expected={expected}.");
    }

    private static string GetPidlIdentity(nint pidl)
    {
        var size = ILGetSize(pidl);
        if (size == 0 || size > 65_536) throw new InvalidOperationException($"Invalid Shell PIDL byte count: {size}.");
        var bytes = new byte[(int)size];
        Marshal.Copy(pidl, bytes, 0, bytes.Length);
        return Convert.ToHexString(bytes);
    }

    private static void FreePeerShellRows(IEnumerable<PeerShellRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
            if (row.RelativePidl != 0) ILFree(row.RelativePidl);
        }
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
        nint capturedPopupOwner = 0;
        var capturedPopupOwnerChain = "(not captured)";
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
                            capturedPopupOwner = message.Window;
                            capturedPopupOwnerChain = DescribeWindowChain(message.Window);
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
        VerifyHeaderMenuSortByType(capturedSubmenus, capturedPopupOwner, view, strongestOffset.Key);
        Console.WriteLine($"ishellview-column-header-menu: owner=0x{capturedPopupOwner:X}; owner-chain=[{capturedPopupOwnerChain}]; items=[{string.Join('|', capturedItems.Select(item => item.Text))}]; canonical-submenu-verbs=[{string.Join(',', canonicalSubmenuVerbs)}]; canonical-leaf-verbs=[{string.Join(',', mappedVerbs)}]; mapped-submenus=[{string.Join(',', mappedSubmenuRows)}]; popup-command-id-base-offset={strongestOffset.Key}; popup-cancelled=True");
    }

    internal static (uint SelectedCommand, int InitPopupCount, int InitResult, bool UsedContextMenu3) TrackPopupMenuAndCancel(nint owner, nint menu, nint contextMenu)
    {
        const uint WmInitMenuPopup = 0x0117;
        const int ENotImpl = unchecked((int)0x80004001);
        var contextMenu3Id = new Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
        var contextMenu2Id = new Guid("000214F4-0000-0000-C000-000000000046");
        nint context3 = 0, context2 = 0, hook = 0;
        var initPopupCount = 0;
        var initResult = ENotImpl;
        var usedContextMenu3 = false;
        HookProc? hookCallback = null;
        SubclassProc? windowCallback = null;
        var subclassId = (nuint)0xE7D1;
        try
        {
            _ = Marshal.QueryInterface(contextMenu, in contextMenu3Id, out context3);
            _ = Marshal.QueryInterface(contextMenu, in contextMenu2Id, out context2);
            windowCallback = (window, messageId, wParam, lParam, id, referenceData) =>
            {
                if (messageId == WmInitMenuPopup && wParam == menu)
                {
                    initPopupCount++;
                    if (context3 != 0)
                    {
                        initResult = At<MenuMessage2>(context3, 7)(context3, WmInitMenuPopup, menu, lParam, out var handledResult);
                        usedContextMenu3 = initResult == 0;
                        if (usedContextMenu3) return handledResult;
                    }
                    if (context2 != 0 && (!usedContextMenu3 && initResult is 1 or ENotImpl))
                    {
                        initResult = At<MenuMessage>(context2, 6)(context2, WmInitMenuPopup, menu, lParam);
                        if (initResult == 0) return 0;
                    }
                }
                return DefSubclassProc(window, messageId, wParam, lParam);
            };
            if (!SetWindowSubclass(owner, windowCallback, subclassId, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not forward Shell menu initialization on the isolated owner window.");
            hookCallback = (code, wParam, lParam) =>
            {
                if (code >= 0 && lParam != 0)
                {
                    var message = Marshal.PtrToStructure<CallWindowProcResult>(lParam);
                    if (message.Message == WmInitMenuPopup && message.WParam == unchecked((nuint)menu) && message.Window == owner)
                        _ = EndMenu(); // Close after the owner has forwarded WM_INITMENUPOPUP.
                }
                return CallNextHookEx(hook, code, wParam, lParam);
            };
            hook = SetWindowsHookExW(12, hookCallback, 0, GetCurrentThreadId()); // WH_CALLWNDPROCRET, this probe thread only.
            if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not observe the isolated menu loop.");
            var selected = TrackPopupMenu(menu, 0x0100, 80, 80, 0, owner, 0); // TPM_RETURNCMD; the hook cancels at first initialization.
            return (selected, initPopupCount, initResult, usedContextMenu3);
        }
        finally
        {
            _ = EndMenu();
            if (hook != 0) _ = UnhookWindowsHookEx(hook);
            _ = RemoveWindowSubclass(owner, windowCallback!, subclassId);
            if (context2 != 0) Marshal.Release(context2);
            if (context3 != 0) Marshal.Release(context3);
            GC.KeepAlive(windowCallback);
            GC.KeepAlive(hookCallback);
        }
    }

    private static void VerifyHeaderMenuSortByType(
        IReadOnlyList<(string Caption, List<(int Id, string Text)> Items)> capturedSubmenus,
        nint popupOwner, nint view, int popupCommandIdBaseOffset)
    {
        var sortMenu = capturedSubmenus.FirstOrDefault(submenu => submenu.Caption.StartsWith("排序方式", StringComparison.Ordinal));
        var typeItem = sortMenu.Items?.FirstOrDefault(item => item.Text.Contains("类型", StringComparison.Ordinal));
        if (typeItem is null || typeItem.Value.Id < 0)
            throw new Exception("The isolated column-header Sort menu did not expose a callable Type item.");

        var folderView2Id = FolderView2Id;
        Check(Marshal.QueryInterface(view, in folderView2Id, out var folderView2));
        try
        {
            var contextMenuId = ContextMenuId;
            Check(At<GetItemObject>(view, 15)(view, 0, ref contextMenuId, out var contextMenu)); // SVGIO_BACKGROUND
            try
            {
                var menu = CreatePopupMenu();
                if (menu == 0) throw new Exception("Could not create the Shell context menu for header-command dispatch.");
                try
                {
                    Check(At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x10 | 0x800));
                    // Recreate the host-side mapping from the captured header ID to this view's
                    // IContextMenu range. A bare WM_COMMAND to DefView did not update the sort;
                    // the header and background submenu mapping is checked above.
                    var commandOffset = checked(typeItem.Value.Id - popupCommandIdBaseOffset - 1);
                    var invoke = new NativeInvokeInfo
                    {
                        Size = Marshal.SizeOf<NativeInvokeInfo>(),
                        Window = popupOwner,
                        Verb = (nint)commandOffset,
                        Show = 0
                    };
                    Check(At<InvokeCommand>(contextMenu, 4)(contextMenu, ref invoke));
                }
                finally { DestroyMenu(menu); }
            }
            finally { Marshal.Release(contextMenu); }

            Check(At<GetSortColumnCount>(folderView2, 26)(folderView2, out var count));
            var actual = new NativeSortColumn[count];
            if (count > 0) Check(At<GetSortColumns>(folderView2, 28)(folderView2, actual, count));
            if (count < 1 || actual[0].Key.FormatId != PropertyFormatId || actual[0].Key.PropertyId != 4)
                throw new Exception($"The column-header Type command did not set the Shell sort key: count={count}, key={(count == 0 ? "none" : $"{actual[0].Key.FormatId:B}/{actual[0].Key.PropertyId}")}.");
            Console.WriteLine($"ishellview-column-header-command: sort-by-type=True; command-id={typeItem.Value.Id}; primary-property-id={actual[0].Key.PropertyId}; sort-columns={count}");
        }
        finally { Marshal.Release(folderView2); }
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

    private static string DescribeWindowChain(nint window)
    {
        var chain = new List<string>();
        for (var depth = 0; window != 0 && depth < 8 && IsWindow(window); depth++)
        {
            var className = new System.Text.StringBuilder(128);
            _ = GetClassNameW(window, className, className.Capacity);
            chain.Add($"0x{window:X}:{className}");
            window = GetParent(window);
        }
        return string.Join(" <- ", chain);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextCapacity;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint ColumnCount;
        public nint Columns;
        public nint ColumnFormats;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeListViewColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public nint Text;
        public int TextCapacity;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeListViewGroup
    {
        public uint Size;
        public uint Mask;
        public nint Header;
        public int HeaderCapacity;
        public nint Footer;
        public int FooterCapacity;
        public int GroupId;
        public uint StateMask;
        public uint State;
        public uint Align;
        public nint Subtitle;
        public uint SubtitleCapacity;
        public nint Task;
        public uint TaskCapacity;
        public nint DescriptionTop;
        public uint DescriptionTopCapacity;
        public nint DescriptionBottom;
        public uint DescriptionBottomCapacity;
        public int TitleImage;
        public int ExtendedImage;
        public int FirstItem;
        public uint ItemCount;
        public nint SubsetTitle;
        public uint SubsetTitleCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStrRet { public uint Type; public nint Pointer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeShellDetails { public int Format, Width; public NativeStrRet Text; }

    private sealed record NativeMenuCommandDescriptor(int Id, string Verb, string Caption);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NativeListViewCompare(nint first, nint second, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUIObjectOf(nint self, nint owner, uint count, nint childPidls, in Guid interfaceId, nint reserved, out nint value);
    private sealed record PeerShellRow(int Id, nint AbsolutePidl, nint RelativePidl, string Name, string ParsingName, string? FileSystemPath, string TypeName, int SystemImageIndex,
        string[] DetailTitles, string[] DetailValues)
    {
        internal ShellFileSystemIdentity? FileSystemIdentity { get; init; }
    }

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
        private readonly ShellBrowserSite _browserCallbacks = new() { ShellFolderFilterActive = true };
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MenuMessage(nint self, uint message, nint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MenuMessage2(nint self, uint message, nint wParam, nint lParam, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint itemPidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemDisplayName(nint self, uint nameKind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroupBy(nint self, ref ShellColumnManager.NativeKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroupBy(nint self, out ShellColumnManager.NativeKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, [In] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSortColumnCount(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetShellColumnCount(nint self, uint flags, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint HookProc(int code, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);
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
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);

    private static nint TryGetParentPidl(nint absolutePidl)
    {
        var parentPidl = ILClone(absolutePidl);
        if (parentPidl == 0 || ILRemoveLastID(parentPidl)) return parentPidl;
        Marshal.FreeCoTaskMem(parentPidl);
        return 0;
    }
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string path1, string? path2);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrRetToBufW(nint strret, nint pidl, System.Text.StringBuilder buffer, uint capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex,
        string cls, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, nint rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState([In] byte[] state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SetWindowsHookExW(int hookType, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint parameters);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuStringW(nint menu, uint item, System.Text.StringBuilder text, int maxLength, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
}
