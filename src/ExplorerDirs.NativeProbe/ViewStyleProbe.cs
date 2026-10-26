using System.Runtime.InteropServices;
using System.Diagnostics;
using ExplorerDirs.App;

// Exercises COM view changes on the probe's own hidden fixture. No input/activation.
internal static class ViewStyleProbe
{
    internal static void Run(nint browser, Action<uint, string>? sampleHeaderState = null, bool synchronizeListView = false)
    {
        var iid = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
        Check(At<GetInterface>(browser, 17)(browser, ref iid, out var view));
        nint shellView = 0;
        try
        {
            if (synchronizeListView)
            {
                var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
                Check(At<GetInterface>(browser, 17)(browser, ref shellViewId, out shellView));
                Check(At<GetWindow>(shellView, 3)(shellView, out var viewWindow));
                if (FindChildByClass(viewWindow, "SysListView32") == 0)
                {
                    Console.WriteLine("view-style-synchronized: direct-listview=unavailable; child-classes=" +
                        string.Join(',', ReadChildClasses(viewWindow)) + "; shell-view-style-tests=continue-without-lvm");
                    synchronizeListView = false;
                }
            }
            var sort = new SortColumn
            {
                Key = new PropertyKey { Id = new("B725F130-47EF-101A-A5F1-02608C9EEBAC"), Property = 10 },
                Direction = -1
            };
            Check(At<Sorts>(view, 27)(view, [sort], 1));
            var group = new PropertyKey { Id = sort.Key.Id, Property = 4 }; // ItemTypeText
            Check(At<SetGroup>(view, 17)(view, ref group, 1));
            var itemCountClock = Stopwatch.StartNew();
            var count = -1;
            var countResult = unchecked((int)0x80004005);
            // Navigation can resolve before the Shell finishes enumerating this probe's fixture.
            do
            {
                Application.DoEvents();
                countResult = At<ItemCount>(view, 7)(view, 2, out count);
                if (countResult >= 0 && count == 3) break;
                Thread.Sleep(10);
            } while (itemCountClock.Elapsed < TimeSpan.FromSeconds(3));
            if (countResult < 0 || count != 3)
                throw new Exception($"Fixture item enumeration did not settle to 3 items; hr/count=0x{countResult:X8}/{count}.");
            // Use the production COM wrapper against real columns, not a fake interface.
            // Deliberately differ from the Shell's usual name/date/type/size order.
            var columns = new DisplayColumnState[]
            {
                new(new(sort.Key.Id, 10), 271), // Name
                new(new(sort.Key.Id, 12), 137), // Size
                new(new(sort.Key.Id, 4), 193)   // Type
            };
            Check(ShellColumnManager.Apply(view, columns));
            CheckColumns(view, columns, "custom-details-setup");
            var failures = new List<string>();
            foreach (var grouped in new[] { false, true })
            {
                var groupKey = grouped ? group : default;
                Check(At<SetGroup>(view, 17)(view, ref groupKey, 1));
                Pump();
                // Group setup can clear selection; establish each mode-test baseline afterwards.
                // Never request native focus; compare identities, not indices.
                Check(At<Select>(view, 15)(view, 0, 5));
                Check(At<Select>(view, 15)(view, 1, 1));
                var selection = ReadSelection(view);
                if (selection.Count != 2) throw new Exception("Fixture multi-selection was not created.");
                var baseline = ReadState(view);
                var expected = $"{sort.Key.Id}/{sort.Key.Property}/{sort.Direction}|" +
                    $"{groupKey.Id}/{groupKey.Property}/{(grouped ? 1 : 0)}";
                if (baseline != expected) throw new Exception("Sort/group setup was not accepted.");
                foreach (var style in ViewStyle.All.Concat([
                    new ViewStyle("legacy-small-icons", 2, -1),
                    new ViewStyle("return-details", 4, -1)]))
                {
                    var requestedSize = ViewStyle.NativeIconSize(style.Mode, style.IconSize);
                    Check(At<SetMode>(view, 35)(view, style.Mode, requestedSize));
                    var listViewMode = -1;
                    if (synchronizeListView && TryGetListViewMode(style.Mode, out var requestedListViewMode))
                    {
                        Check(At<GetWindow>(shellView, 3)(shellView, out var viewWindow));
                        var listView = FindChildByClass(viewWindow, "SysListView32");
                        if (listView == 0)
                            throw new Exception("The current ExplorerBrowser view has no native SysListView32; child-classes=" +
                                string.Join(',', ReadChildClasses(viewWindow)) + ".");
                        _ = SendMessageW(listView, 0x108E, (nint)requestedListViewMode, 0); // LVM_SETVIEW.
                        listViewMode = SendMessageW(listView, 0x108F, 0, 0).ToInt32(); // LVM_GETVIEW.
                        if (listViewMode != requestedListViewMode)
                            throw new Exception($"ExplorerBrowser ListView mode did not match {style.Name}: requested={requestedListViewMode}, actual={listViewMode}.");
                    }
                    if (sampleHeaderState is not null)
                    {
                        Check(At<GetMode>(view, 36)(view, out var immediateMode, out _));
                        sampleHeaderState(immediateMode, "immediate");
                        Pump(50);
                        sampleHeaderState(immediateMode, "50ms");
                        Pump(200);
                        sampleHeaderState(immediateMode, "250ms");
                        Pump(750);
                        sampleHeaderState(immediateMode, "1000ms");
                    }
                    Pump();
                    Check(At<GetMode>(view, 36)(view, out var mode, out var size));
                    var sameSelection = selection.SetEquals(ReadSelection(view));
                    var sameState = baseline == ReadState(view);
                    Console.WriteLine($"view-style: grouped={grouped}; requested={style.Mode}/{requestedSize}; actual={mode}/{size}; list-view={listViewMode}; selection-identities={sameSelection}; sort-group={sameState}");
                    if (mode != style.Mode || size <= 0 || (requestedSize > 0 && size != requestedSize))
                        failures.Add($"grouped={grouped}: requested {style.Mode}/{style.IconSize}, actual {mode}/{size}");
                    if (!sameSelection || !sameState)
                        failures.Add($"grouped={grouped}: state changed at {style.Mode}/{style.IconSize}");
                    // The app saves columns in every mode, so check the snapshot even
                    // while Details is not displayed, as well as after returning to it.
                    CheckColumns(view, columns, $"mode-{mode}-grouped-{grouped}");
                }
            }
            if (failures.Count != 0) throw new Exception(string.Join("; ", failures));
        }
        finally
        {
            if (shellView != 0) Marshal.Release(shellView);
            Marshal.Release(view);
        }
    }

    private static bool TryGetListViewMode(uint folderViewMode, out int listViewMode)
    {
        listViewMode = folderViewMode switch
        {
            1 => 0, // FVM_ICON -> LV_VIEW_ICON.
            2 => 2, // FVM_SMALLICON -> LV_VIEW_SMALLICON.
            3 => 3, // FVM_LIST -> LV_VIEW_LIST.
            4 => 1, // FVM_DETAILS -> LV_VIEW_DETAILS.
            6 => 4, // FVM_TILE -> LV_VIEW_TILE.
            _ => -1
        };
        return listViewMode >= 0;
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

    private static string[] ReadChildClasses(nint parent)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, name, name.Capacity) > 0) classes.Add(name.ToString());
            return true;
        }, 0);
        return classes.Order(StringComparer.Ordinal).ToArray();
    }

    private static void CheckColumns(nint view, IReadOnlyList<DisplayColumnState> expected, string stage)
    {
        Check(ShellColumnManager.Capture(view, out var actual));
        var same = actual is not null && expected.SequenceEqual(actual);
        Console.WriteLine($"view-columns: stage={stage}; exact-order-widths={same}; " +
            $"actual={string.Join(";", actual?.Select(c => $"{c.Key.PropertyId}/{c.Width}") ?? [])}");
        if (!same) throw new Exception($"Column order or widths changed at {stage}.");
    }

    internal static string ReadState(nint view)
    {
        Check(At<Count>(view, 26)(view, out var count));
        if (count is < 1 or > 16) throw new Exception("Invalid native sort count.");
        var sorts = new SortColumn[count];
        Check(At<Sorts>(view, 28)(view, sorts, count));
        var groupResult = At<GetGroup>(view, 18)(view, out var group, out var ascending);
        if (groupResult is not (0 or 1)) Check(groupResult);
        if (groupResult == 1) { group = default; ascending = 0; } // Native no-group state.
        return string.Join(";", sorts.Select(s => $"{s.Key.Id}/{s.Key.Property}/{s.Direction}")) +
            $"|{group.Id}/{group.Property}/{ascending}";
    }

    internal static HashSet<string> ReadSelection(nint view)
    {
        var iid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
        Check(At<GetItems>(view, 8)(view, 1, ref iid, out var array));
        try
        {
            Check(At<ArrayCount>(array, 7)(array, out var count));
            if (count > 3) throw new Exception("Unexpected selection count.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; index < count; index++)
            {
                Check(At<ItemAt>(array, 8)(array, index, out var item));
                nint name = 0;
                try
                {
                    Check(At<DisplayName>(item, 5)(item, 0x80058000, out name));
                    names.Add(Marshal.PtrToStringUni(name) ?? throw new Exception("Missing selected identity."));
                }
                finally { if (name != 0) Marshal.FreeCoTaskMem(name); Marshal.Release(item); }
            }
            return names;
        }
        finally { Marshal.Release(array); }
    }
    private static void Pump(int milliseconds = 250)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void Check(int hr) { if (hr != 0) throw new COMException("View style probe failed.", hr); }
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Id; public uint Property; }
    [StructLayout(LayoutKind.Sequential)] private struct SortColumn { public PropertyKey Key; public int Direction; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetInterface(nint self, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetMode(nint self, out uint mode, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Count(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Sorts(nint self, [In, Out] SortColumn[] sorts, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroup(nint self, ref PropertyKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroup(nint self, out PropertyKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Select(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ArrayCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DisplayName(nint self, uint kind, out nint name);
    private delegate bool EnumChildProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
