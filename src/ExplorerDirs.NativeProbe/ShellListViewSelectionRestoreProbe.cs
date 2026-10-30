using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Hidden SysListView32 contract for restoring selection/focus after Shell rows are reconciled.
internal static class ShellListViewSelectionRestoreProbe
{
    private const uint IccListViewClasses = 0x00000001;
    private const int LvmInsertItemW = 0x104D;
    private const int LvmGetItemW = 0x104B;
    private const int LvmDeleteAllItems = 0x1009;
    private const int LvmGetItemCount = 0x1004;
    private const int LvmGetNextItem = 0x100C;
    private const int LvmGetItemState = 0x102C;
    private const int LvmSetItemState = 0x102B;
    private const uint LvifText = 0x0001;
    private const uint LvifParam = 0x0004;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const uint LvinSelected = 0x0002;
    private const uint LvinFocused = 0x0001;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-list-selection-" + Guid.NewGuid().ToString("N"));
        var names = new[] { "alpha", "beta", "gamma" };
        nint parent = 0, listView = 0, folderPidl = 0, shellFolder = 0;
        ShellListViewEnumeration? current = null;
        try
        {
            Directory.CreateDirectory(fixture);
            foreach (var name in names) Directory.CreateDirectory(Path.Combine(fixture, name));
            var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = IccListViewClasses };
            if (!InitCommonControlsEx(ref init))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ListView class initialization failed.");

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden selection restore probe",
                unchecked((int)0x80000000), 0, 0, 0, 0, 0, 0, GetModuleHandleW(null), 0);
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Hidden parent creation failed.");
            listView = CreateWindowExW(0, "SysListView32", "",
                0x40000000 | 0x10000000 | 0x00000001, 0, 0, 320, 240, parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Hidden ListView creation failed.");

            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out shellFolder));
            current = EnumerateSorted(folderPidl, shellFolder);
            if (!current.Rows.Select(row => row.DisplayName).Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(names.Order(StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Initial Shell enumeration did not return the expected fixture rows.");

            var initialOrder = current.Rows.Select(row => row.DisplayName).ToArray();
            ProjectRows(listView, current.Rows);
            var betaId = current.Rows.Single(row => row.DisplayName == "beta").Id;
            var gammaId = current.Rows.Single(row => row.DisplayName == "gamma").Id;
            SetState(listView, FindIndexById(listView, betaId), LvisSelected, LvisSelected);
            SetState(listView, FindIndexById(listView, gammaId), LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            var selection = CaptureSelection(listView, current.Rows);

            Directory.CreateDirectory(Path.Combine(fixture, "delta"));
            SHChangeNotify(0x00001000, 0x00000005, fixture, null);
            var addedEnumeration = EnumerateSorted(folderPidl, shellFolder);
            var added = ShellListViewRowModel.Reconcile(current.Rows, addedEnumeration.Rows, NextId(current.Rows));
            ShellListViewEnumerator.FreeRows(added.RetiredRows);
            current = addedEnumeration with { Rows = added.Rows.ToList() };
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, selection);
            var afterAdd = ReadSelection(listView, current.Rows);
            var addRestored = afterAdd.SelectedRowIds.SetEquals([betaId, gammaId]) && afterAdd.FocusedRowId == gammaId;
            var orderChanged = !initialOrder.SequenceEqual(current.Rows.Select(row => row.DisplayName));
            var addedUnselected = ReadState(listView, FindIndexById(listView,
                current.Rows.Single(row => row.DisplayName == "delta").Id)) == 0;

            Directory.Delete(Path.Combine(fixture, "beta"));
            SHChangeNotify(0x00000004, 0x00000005, Path.Combine(fixture, "beta"), null);
            var beforeDelete = CaptureSelection(listView, current.Rows);
            var deletedEnumeration = EnumerateSorted(folderPidl, shellFolder);
            var deleted = ShellListViewRowModel.Reconcile(current.Rows, deletedEnumeration.Rows, added.NextId);
            var deletedRows = deleted.RetiredRows;
            current = deletedEnumeration with { Rows = deleted.Rows.ToList() };
            ShellListViewEnumerator.FreeRows(deletedRows);
            ProjectRows(listView, current.Rows);
            ApplySelection(listView, current.Rows, beforeDelete);
            var afterDelete = ReadSelection(listView, current.Rows);
            var deleteRestored = afterDelete.SelectedRowIds.SetEquals([gammaId]) && afterDelete.FocusedRowId == gammaId;
            var deletedIdAbsent = current.Rows.All(row => row.Id != betaId);
            var passed = addRestored && orderChanged && addedUnselected && deleteRestored && deletedIdAbsent &&
                GetForegroundWindow() == foreground && !IsWindowVisible(parent);
            Console.WriteLine($"shell-listview-selection-restore: selected-focus-after-add={addRestored}; " +
                $"provider-order-changed={orderChanged}; new-row-unselected={addedUnselected}; " +
                $"selected-focus-after-delete={deleteRestored}; deleted-id-absent={deletedIdAbsent}; " +
                $"row-count={SendMessageW(listView, LvmGetItemCount, 0, 0)}; hidden={ !IsWindowVisible(parent)}; " +
                $"foreground-unchanged={GetForegroundWindow() == foreground}; passed={passed}");
            return passed ? 0 : 1;
        }
        finally
        {
            if (current is not null) ShellListViewEnumerator.FreeRows(current.Rows);
            if (shellFolder != 0) Marshal.Release(shellFolder);
            if (folderPidl != 0) ILFree(folderPidl);
            if (parent != 0 && IsWindow(parent)) _ = DestroyWindow(parent);
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static ShellListViewEnumeration EnumerateSorted(nint folderPidl, nint shellFolder)
    {
        var enumeration = ShellListViewEnumerator.Enumerate(folderPidl, shellFolder);
        return enumeration with
        {
            Rows = enumeration.Rows.OrderByDescending(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static ShellListViewSelectionSnapshot CaptureSelection(nint listView, IReadOnlyList<ShellListViewRow> rows)
    {
        var selectedIds = new List<long>();
        var index = -1;
        while ((index = SendMessageW(listView, LvmGetNextItem, index, (nint)LvinSelected).ToInt32()) >= 0)
            selectedIds.Add(GetItemId(listView, index));
        var focusedIndex = SendMessageW(listView, LvmGetNextItem, -1, (nint)LvinFocused).ToInt32();
        var focusedId = focusedIndex >= 0 ? GetItemId(listView, focusedIndex) : (long?)null;
        return ShellListViewSelectionSnapshot.Capture(rows, selectedIds, focusedId);
    }

    private static (HashSet<long> SelectedRowIds, long? FocusedRowId) ReadSelection(
        nint listView, IReadOnlyList<ShellListViewRow> rows)
    {
        var selected = new HashSet<long>();
        long? focused = null;
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        for (var index = 0; index < count; index++)
        {
            var state = ReadState(listView, index);
            var id = GetItemId(listView, index);
            if ((state & LvisSelected) != 0) selected.Add(id);
            if ((state & LvisFocused) != 0) focused = id;
        }
        // Resolve once to reject any state whose identity is not in the current row model.
        var resolved = ShellListViewSelectionSnapshot.Capture(rows, selected, focused).Resolve(rows);
        return (resolved.Where(row => row.Selected).Select(row => row.RowId).ToHashSet(),
            resolved.SingleOrDefault(row => row.Focused)?.RowId);
    }

    private static void ApplySelection(nint listView, IReadOnlyList<ShellListViewRow> rows,
        ShellListViewSelectionSnapshot snapshot)
    {
        SetState(listView, -1, 0, LvisSelected | LvisFocused);
        foreach (var rowState in snapshot.Resolve(rows))
        {
            var state = (rowState.Selected ? LvisSelected : 0) | (rowState.Focused ? LvisFocused : 0);
            if (state == 0) continue;
            SetState(listView, FindIndexById(listView, rowState.RowId), state, LvisSelected | LvisFocused);
        }
    }

    private static void ProjectRows(nint listView, IReadOnlyList<ShellListViewRow> rows)
    {
        _ = SendMessageW(listView, LvmDeleteAllItems, 0, 0);
        for (var index = 0; index < rows.Count; index++)
        {
            var text = Marshal.StringToHGlobalUni(rows[index].DisplayName);
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
            try
            {
                var item = new ListViewItem
                {
                    Mask = LvifText | LvifParam,
                    Item = index,
                    Text = text,
                    TextMax = rows[index].DisplayName.Length,
                    Param = new nint(rows[index].Id)
                };
                Marshal.StructureToPtr(item, memory, false);
                if (SendMessageW(listView, LvmInsertItemW, 0, memory).ToInt32() < 0)
                    throw new InvalidOperationException("The hidden SysListView32 rejected a projected Shell row.");
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
                Marshal.FreeHGlobal(text);
            }
        }
    }

    private static int FindIndexById(nint listView, long rowId)
    {
        var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
        for (var index = 0; index < count; index++) if (GetItemId(listView, index) == rowId) return index;
        return -1;
    }

    private static long GetItemId(nint listView, int index)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            var item = new ListViewItem { Mask = LvifParam, Item = index };
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmGetItemW, 0, memory) == 0)
                throw new InvalidOperationException($"Could not read the row ID at index {index}.");
            return Marshal.PtrToStructure<ListViewItem>(memory).Param.ToInt64();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static uint ReadState(nint listView, int index) =>
        unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)));

    private static void SetState(nint listView, int index, uint state, uint mask)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(new ListViewItem { State = state, StateMask = mask }, memory, false);
            _ = SendMessageW(listView, LvmSetItemState, index, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static long NextId(IReadOnlyList<ShellListViewRow> rows) => rows.Count == 0 ? 1 : rows.Max(row => row.Id) + 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct InitCommonControls
    {
        public uint Size; public uint Classes;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ListViewItem
    {
        public uint Mask; public int Item; public int SubItem; public uint State; public uint StateMask;
        public nint Text; public int TextMax; public int Image; public nint Param; public int Indent;
        public int GroupId; public uint ColumnCount; public nint Columns; public nint ColumnFormats; public int Group;
    }
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "IsWindow", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint result);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern void ILFree(nint pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string item1, string? item2);
}
