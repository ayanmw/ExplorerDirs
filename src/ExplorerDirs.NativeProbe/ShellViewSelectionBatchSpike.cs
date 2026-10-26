using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Exercise the production selection snapshot against the stock Shell view without
// creating ExplorerBrowser or interacting with the desktop.
internal static class ShellViewSelectionBatchSpike
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        nint parent = 0, pidl = 0, folder = 0, view = 0, hostPointer = 0, viewWindow = 0, folderView = 0, removedChildPidl = 0;
        var created = false;
        StreamViewHost? host = null;
        try
        {
            Directory.CreateDirectory(fixture);
            var firstPath = Path.Combine(fixture, "a-selected.txt");
            var secondPath = Path.Combine(fixture, "b-removed.txt");
            var extraPath = Path.Combine(fixture, "c-extra.md");
            var extraPath2 = Path.Combine(fixture, "d-extra.bin");
            File.WriteAllText(firstPath, "first");
            File.WriteAllText(secondPath, "second");
            File.WriteAllText(extraPath, "extra");
            File.WriteAllText(extraPath2, "extra two");
            Check(SHParseDisplayName(secondPath, 0, out var removedAbsolutePidl, 0, out _));
            try { removedChildPidl = ILClone(ILFindLastID(removedAbsolutePidl)); }
            finally { Marshal.FreeCoTaskMem(removedAbsolutePidl); }
            if (removedChildPidl == 0) throw new Exception("Could not retain the stale-item child PIDL.");

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs selection batch spike",
                0x80000000, 0, 0, 640, 420, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out pidl, 0, out _));
            Check(SHBindToObject(0, pidl, 0, in ShellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));

            host = new StreamViewHost(parent, []);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 620, Bottom = 400 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            created = true;
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            var itemCountBeforeEnumeration = GetItemCount(folderView, 2);
            Check(SHParseDisplayName(firstPath, 0, out var firstAbsolutePidl, 0, out _));
            int positionBeforeEnumeration;
            try
            {
                var firstChildPidl = ILFindLastID(firstAbsolutePidl);
                positionBeforeEnumeration = GetItemPosition(folderView, firstChildPidl, out _);
            }
            finally { Marshal.FreeCoTaskMem(firstAbsolutePidl); }
            PumpUntil(() => GetItemCount(folderView, 2) == 4);
            var backgroundScopeItemCount = GetItemCount(folderView, 0); // SVGIO_BACKGROUND
            var allViewItemCount = GetItemCount(folderView, 2); // SVGIO_ALLVIEW
            Check(SHParseDisplayName(firstPath, 0, out var firstReadyAbsolutePidl, 0, out _));
            int positionAfterEnumeration;
            try
            {
                var firstChildPidl = ILFindLastID(firstReadyAbsolutePidl);
                positionAfterEnumeration = GetItemPosition(folderView, firstChildPidl, out _);
            }
            finally { Marshal.FreeCoTaskMem(firstReadyAbsolutePidl); }
            if (positionAfterEnumeration < 0)
                throw new Exception($"GetItemPosition did not find an enumerated item: hr=0x{positionAfterEnumeration:X8}.");

            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u | 0x10u | 0x40000000u)); // select, focus first, no HWND focus
            Check(At<SelectItem>(folderView, 15)(folderView, 1, 0x1u | 0x40000000u)); // add second without changing focus
            Pump();
            var backgroundScopeSelectedCount = GetItemCount(folderView, 0); // SVGIO_BACKGROUND
            var selectionScopeCount = GetItemCount(folderView, 1); // SVGIO_SELECTION
            using var snapshot = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not capture the two-item Shell selection.");
            if (snapshot.SelectedCount != 2 || !snapshot.HasFocusedItem)
                throw new Exception($"Expected a two-item selection with focused PIDL, got selected={snapshot.SelectedCount},focused={snapshot.HasFocusedItem}.");
            var readiness = snapshot.ReadTargetReadiness(view, knownReady: null, out var readinessQueries);
            if (readiness != "11" || readinessQueries != 2)
                throw new Exception($"Both selected PIDLs should be queried and locatable in this fully enumerated view; readiness={readiness ?? "unavailable"}, queries={readinessQueries}.");
            var cachedReadiness = snapshot.ReadTargetReadiness(view, knownReady: "10", out var cachedReadinessQueries);
            if (cachedReadiness != "11" || cachedReadinessQueries != 1)
                throw new Exception($"Cached positive readiness should probe only the unresolved PIDL; readiness={cachedReadiness ?? "unavailable"}, queries={cachedReadinessQueries}.");

            for (var index = 0; index < 4; index++) Check(At<SelectItem>(folderView, 15)(folderView, index, 0x40000000u));
            var directTrace = new List<string>();
            var direct = snapshot.Restore(view, directTrace.Add);
            using var directReadback = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not read back the direct batch restore.");
            var directPath = directTrace.FirstOrDefault(line => line.StartsWith("selection-batch-path=", StringComparison.Ordinal));
            if (!direct.SelectionRestored || !direct.FocusRestored || directReadback.SelectedCount != 2 || directPath != "selection-batch-path=direct")
                throw new Exception($"Exact multi-selection/focus restore: selection={direct.SelectionRestored}, focus={direct.FocusRestored}, selected={directReadback.SelectedCount}, path={directPath}; " + string.Join(";", directTrace));

            var stagedSelectionTrace = new List<string>();
            var stagedSelection = snapshot.Restore(view, stagedSelectionTrace.Add,
                allowSelectedFocusFallback: false, restoreSelection: true, restoreFocus: false);
            using var stagedSelectionReadback = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not read back the selection-only restore stage.");
            if (!stagedSelection.SelectionRestored || stagedSelection.FocusRestored ||
                stagedSelectionReadback.SelectedCount != 2 ||
                !stagedSelectionTrace.Contains("focused-item-restore-deferred") ||
                stagedSelectionTrace.Any(line => line.StartsWith("focused-item-select ", StringComparison.Ordinal)))
                throw new Exception($"Selection-only stage must restore both items without invoking focus: selection={stagedSelection.SelectionRestored}, focus={stagedSelection.FocusRestored}, selected={stagedSelectionReadback.SelectedCount}; {string.Join(";", stagedSelectionTrace)}");

            var stagedFocusTrace = new List<string>();
            var stagedFocus = snapshot.Restore(view, stagedFocusTrace.Add,
                allowSelectedFocusFallback: false, restoreSelection: false, restoreFocus: true);
            using var stagedFocusReadback = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not read back the focus-only restore stage.");
            if (!stagedFocus.SelectionRestored || !stagedFocus.FocusRestored || stagedFocusReadback.SelectedCount != 2)
                throw new Exception($"Focus-only stage must preserve the exact two-item selection and restore focus: selection={stagedFocus.SelectionRestored}, focus={stagedFocus.FocusRestored}, selected={stagedFocusReadback.SelectedCount}; {string.Join(";", stagedFocusTrace)}");

            Check(At<SelectItem>(folderView, 15)(folderView, 2, 0x1u | 0x4u)); // introduce an unrelated selected item
            Check(At<SelectItem>(folderView, 15)(folderView, 3, 0x1u));
            var selectSavedItemAndClearOthers = At<SelectItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u | 0x40000000u);
            Pump();
            var seededSelectionCount = GetItemCount(folderView, 1);
            Check(At<SelectItem>(folderView, 15)(folderView, 1, 0x1u | 0x40000000u));
            Pump();
            using var seededReadback = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not read back the single-item replacement seed.");
            if (selectSavedItemAndClearOthers < 0 || seededSelectionCount != 1 || seededReadback.SelectedCount != 2)
                throw new Exception($"Single-item DESELECTOTHERS seed did not clear old items then accept the second saved item: hr=0x{selectSavedItemAndClearOthers:X8}, after-seed={seededSelectionCount}, after-batch={seededReadback.SelectedCount}.");
            Check(At<SelectItem>(folderView, 15)(folderView, 2, 0));
            Check(At<SelectItem>(folderView, 15)(folderView, 3, 0));
            Pump();
            var clearByInvalidIndex = At<SelectItem>(folderView, 15)(folderView, -1, 0x4u | 0x40000000u);
            Pump();
            var invalidIndexSelectionCount = GetItemCount(folderView, 1);
            Check(At<Item>(folderView, 6)(folderView, 2, out var extraItem));
            int deselectByPidl;
            try { deselectByPidl = At<SelectChild>(view, 14)(view, extraItem, 0x40000000u); }
            finally { Marshal.FreeCoTaskMem(extraItem); }
            Pump();
            var deselectByPidlSelectionCount = GetItemCount(folderView, 1);
            Check(At<GetSelectedItem>(folderView, 31)(folderView, 0, out var selectedIndex));
            var deselectBySelectedIndex = At<SelectItem>(folderView, 15)(folderView, selectedIndex, 0x40000000u);
            Pump();
            var deselectBySelectedIndexCount = GetItemCount(folderView, 1);
            var fallbackModes = new List<string>();
            var typeKey = new PropertyKey { Id = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), Property = 4 };
            foreach (var grouped in new[] { false, true })
            {
                var group = grouped ? typeKey : default;
                Check(At<SetGroupBy>(folderView, 17)(folderView, ref group, 1));
                Pump();
                var groupResult = At<GetGroupBy>(folderView, 18)(folderView, out var actualGroup, out _);
                if (grouped ? groupResult != 0 || actualGroup.Id != typeKey.Id || actualGroup.Property != typeKey.Property : groupResult != 1)
                    throw new Exception($"Shell group state mismatch: grouped={grouped}, hr=0x{groupResult:X8}, key={actualGroup.Id}/{actualGroup.Property}.");

                foreach (var style in ViewStyle.All)
                {
                    Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, style.Mode,
                        ViewStyle.NativeIconSize(style.Mode, style.IconSize)));
                    Pump();
                    Check(At<GetViewModeAndIconSize>(folderView, 36)(folderView, out var actualMode, out var actualSize));
                    var expectedSize = ViewStyle.NativeIconSize(style.Mode, style.IconSize);
                    if (actualMode != style.Mode || expectedSize > 0 && actualSize != expectedSize)
                        throw new Exception($"Shell did not enter {style.Name}: expected={style.Mode}/{expectedSize}, actual={actualMode}/{actualSize}.");

                    for (var index = 0; index < 4; index++)
                    Check(At<SelectItem>(folderView, 15)(folderView, index, 0x40000000u));
                    Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u | 0x10u | 0x40000000u));
                    Check(At<SelectItem>(folderView, 15)(folderView, 1, 0x1u | 0x40000000u));
                    Pump();
                    using var modeSnapshot = ShellViewSelectionSnapshot.Capture(folderView)
                        ?? throw new Exception($"Could not capture grouped={grouped} {style.Name} selection.");
                    if (modeSnapshot.SelectedCount != 2 || !modeSnapshot.HasFocusedItem)
                        throw new Exception($"Could not establish two selected items and a focused item in grouped={grouped} {style.Name}.");
                    for (var index = 0; index < 4; index++)
                        Check(At<SelectItem>(folderView, 15)(folderView, index, index == 0 ? 0x1u | 0x4u | 0x40000000u : 0x1u | 0x40000000u));
                    Pump();

                    var fallbackTrace = new List<string>();
                    var fallback = modeSnapshot.Restore(view, fallbackTrace.Add);
                    using var fallbackReadback = ShellViewSelectionSnapshot.Capture(folderView)
                        ?? throw new Exception($"Could not read back the grouped={grouped} {style.Name} replacement fallback.");
                    var fallbackPath = fallbackTrace.FirstOrDefault(line => line.StartsWith("selection-batch-path=", StringComparison.Ordinal));
                    var clearBatchCalls = fallbackTrace.Count(line => line.StartsWith("selection-clear batch count=", StringComparison.Ordinal));
                    var clearVerified = fallbackTrace.Any(line => line.StartsWith("selection-clear complete count=", StringComparison.Ordinal));
                    if (!fallback.SelectionRestored || !fallback.FocusRestored || fallbackReadback.SelectedCount != 2 || fallbackPath != "selection-batch-path=replacement-fallback" ||
                        clearBatchCalls != 1 || !clearVerified)
                        throw new Exception($"grouped={grouped} {style.Name} replacement fallback failed: restored={fallback.SelectionRestored}, selected={fallbackReadback.SelectedCount}, items={string.Join('|', ReadSelectedNames(folderView))}, path={fallbackPath}; " + string.Join(";", fallbackTrace));
                    fallbackModes.Add($"{(grouped ? "grouped" : "ungrouped")}/{style.Name}");
                    for (var index = 0; index < 4; index++)
                        Check(At<SelectItem>(folderView, 15)(folderView, index, 0));
                }
            }

            var noGroup = default(PropertyKey);
            Check(At<SetGroupBy>(folderView, 17)(folderView, ref noGroup, 0));
            Check(At<SetViewModeAndIconSize>(folderView, 35)(folderView, 4, -1));
            Pump();
            for (var index = 0; index < 4; index++) Check(At<SelectItem>(folderView, 15)(folderView, index, 0));
            Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x1u | 0x4u));
            Check(At<SelectItem>(folderView, 15)(folderView, 1, 0x1u));
            using var staleSnapshot = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not capture the no-group stale-PIDL snapshot.");
            File.Delete(secondPath);
            Check(At<RefreshView>(view, 8)(view));
            PumpUntil(() => GetItemCount(folderView, 2) == 3);
            var removedPidPositionResult = GetItemPosition(folderView, removedChildPidl, out _);
            var staleReadiness = staleSnapshot.ReadTargetReadiness(view, knownReady: null, out _);
            if (staleReadiness is null || staleReadiness.Count(value => value == '1') != 1)
                throw new Exception($"Expected exactly one surviving saved PIDL after removal; readiness={staleReadiness ?? "unavailable"}.");
            var staleTrace = new List<string>();
            var stale = staleSnapshot.Restore(view, staleTrace.Add);
            using var staleReadback = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not read back the stale-PIDL fallback.");
            var stalePath = staleTrace.FirstOrDefault(line => line.StartsWith("selection-batch-path=", StringComparison.Ordinal));
            if (stalePath != "selection-batch-path=replacement-fallback")
                throw new Exception("A removed snapshot item did not exercise the seeded fallback: " + string.Join(";", staleTrace));
            if (staleTrace.Count(line => line.StartsWith("selection-item-direct ", StringComparison.Ordinal)) != 2)
                throw new Exception("Stale-PIDL fallback did not directly ask Shell to select each saved item: " + string.Join(";", staleTrace));

            File.Delete(firstPath);
            Check(At<RefreshView>(view, 8)(view));
            PumpUntil(() => GetItemCount(folderView, 2) == 2);
            var fullyStaleTrace = new List<string>();
            var fullyStale = staleSnapshot.Restore(view, fullyStaleTrace.Add);
            var remainingSelectionCount = GetItemCount(folderView, 1);
            if (fullyStale.SelectionRestored || remainingSelectionCount != 0 ||
                !fullyStaleTrace.Contains("selection-clear complete count=0") ||
                fullyStaleTrace.Count(line => line.StartsWith("selection-item-direct ", StringComparison.Ordinal)) != 2)
                throw new Exception($"Fully stale snapshot did not leave a clean empty selection: restored={fullyStale.SelectionRestored}, selected={remainingSelectionCount}; " + string.Join(";", fullyStaleTrace));

            if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Hidden/foreground boundary changed.");
            Console.WriteLine("ishellview-selection-batch-spike: direct=restored-two-items; " +
                $"item-count-before-enumeration={itemCountBeforeEnumeration}; item-position-before-enumeration=0x{positionBeforeEnumeration:X8}; item-position-after-enumeration=0x{positionAfterEnumeration:X8}; " +
                $"item-count-background-scope={backgroundScopeItemCount}/{backgroundScopeSelectedCount}; " +
                $"item-count-selection-scope={selectionScopeCount}; item-count-all-view={allViewItemCount}; " +
                $"item-position-after-removal=0x{removedPidPositionResult:X8}; " +
                $"direct-focus-restored={direct.FocusRestored}; replacement-focus-restored=verified; " +
                $"extra-selection=fallback-restored-two-items; stale-pidl=restored-{staleReadback.SelectedCount}-survivor(s)/reported-{stale.SelectionRestored}; " +
                $"fully-stale-pidl=reported-{fullyStale.SelectionRestored}/{remainingSelectionCount}-selected; " +
                $"fallback-path={stalePath}; clear-index-minus-one=0x{clearByInvalidIndex:X8}/{invalidIndexSelectionCount}-selected; " +
                $"deselect-by-pidl=0x{deselectByPidl:X8}/{deselectByPidlSelectionCount}-selected; " +
                $"deselect-selected-index={selectedIndex}/0x{deselectBySelectedIndex:X8}/{deselectBySelectedIndexCount}-selected; " +
                $"single-item-deselect-others-seed=0x{selectSavedItemAndClearOthers:X8}/{seededSelectionCount}-then-{seededReadback.SelectedCount}; " +
                $"replacement-fallback-modes={string.Join(',', fallbackModes)}; " +
                "parent-visible=False; foreground-unchanged=True");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ishellview-selection-batch-spike failure: " + ex.Message);
            return 1;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (created)
                {
                    Check(At<DestroyView>(view, 10)(view));
                    if (IsWindow(viewWindow)) throw new Exception("Native view cleanup left a live window.");
                }
            }
            finally
            {
                if (view != 0) Marshal.Release(view);
                if (hostPointer != 0) Marshal.Release(hostPointer);
                if (host is not null) host.View = 0;
                host?.Dispose();
                GC.KeepAlive(host);
                if (removedChildPidl != 0) Marshal.FreeCoTaskMem(removedChildPidl);
                if (folder != 0) Marshal.Release(folder);
                if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
                if (parent != 0 && (!DestroyWindow(parent) || IsWindow(parent)))
                    Console.WriteLine("ishellview-selection-batch-spike cleanup: hidden parent remains alive.");
                var fullFixture = Path.GetFullPath(fixture);
                var fixturePrefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullFixture.StartsWith(fixturePrefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullFixture))
                    Directory.Delete(fullFixture, recursive: true);
            }
        }
    }

    private static int GetItemCount(nint folderView, uint scope) => At<ItemCount>(folderView, 7)(folderView, scope, out var count) >= 0 ? count : -1;

    private static int GetItemPosition(nint folderView, nint childPidl, out Point point) =>
        At<GetItemPositionCall>(folderView, 11)(folderView, childPidl, out point);

    private static string[] ReadSelectedNames(nint folderView)
    {
        var iid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
        var result = At<Items>(folderView, 8)(folderView, 1, ref iid, out var array);
        if (result == unchecked((int)0x80070490)) return [];
        Check(result);
        try
        {
            Check(At<ArrayCount>(array, 7)(array, out var count));
            var names = new List<string>();
            for (uint index = 0; index < count; index++)
            {
                Check(At<ItemAt>(array, 8)(array, index, out var item));
                nint name = 0;
                try
                {
                    Check(At<DisplayName>(item, 5)(item, 0, out name)); // SIGDN_NORMALDISPLAY
                    names.Add(Marshal.PtrToStringUni(name) ?? "<null>");
                }
                finally { if (name != 0) Marshal.FreeCoTaskMem(name); Marshal.Release(item); }
            }
            return names.ToArray();
        }
        finally { Marshal.Release(array); }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            Pump();
            if (condition()) return;
            Thread.Sleep(10);
        }
        throw new TimeoutException("The isolated Shell view did not reach the expected item count.");
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            while (PeekMessageW(out var message, 0, 0, 0, 1))
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewModeAndIconSize(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewModeAndIconSize(nint self, out uint mode, out int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroupBy(nint self, ref PropertyKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroupBy(nint self, out PropertyKey key, out int ascending);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Id; public uint Property; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectChild(nint self, nint item, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Item(nint self, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSelectedItem(nint self, int startIndex, out int itemIndex);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemPositionCall(nint self, nint item, out Point point);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Items(nint self, uint scope, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ArrayCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DisplayName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Next(nint self, uint count, out nint item, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectAndPositionItems(nint self, uint count, nint items, nint positions, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyView(nint self);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
}
