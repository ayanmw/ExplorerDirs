using System.Runtime.InteropServices;

// Control-only baseline used to separate common-control custom draw behavior from Shell DefView.
internal static class CommonListViewColorBaseline
{
    private const uint WmNotify = 0x004E;
    private const int NmCustomDraw = -12;
    private const uint CddsPrePaint = 0x00000001;
    private const uint CddsItemPrePaint = 0x00010001;
    private const uint CddsItemPostPaint = 0x00010002;
    private const int LvmInsertItemW = 0x104D;
    private const int LvmInsertColumnW = 0x1061;
    private const int LvmGetItemW = 0x104B;
    private const int LvmSetItemW = 0x104C;
    private const int LvmGetItemTextW = 0x1073;
    private const int LvmSortItemsEx = 0x1051;
    private const int LvmSetItemState = 0x102B;
    private const int LvmGetItemState = 0x102C;
    private const int LvmSetView = 0x108E;
    private const int LvmRedrawItems = 0x1015;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const uint CdrfNotifyItemDraw = 0x20;
    private const uint CdrfNewFontAndPostPaint = 0x12;
    private static readonly DrawProc Callback = OnDraw;
    private static readonly ListViewCompareProc SizeCompare = CompareBySize;
    private static Dictionary<long, ShellRow>? _shellRowsById;
    private static readonly Dictionary<nuint, uint> Colors = new() { [0] = 0x00F0E0D0, [1] = 0x00D0F0E0 };
    private static readonly Dictionary<nuint, uint> Samples = [];
    private static readonly Dictionary<nuint, string> Writes = [];
    private static readonly Dictionary<nuint, Rect> LabelBounds = [];
    private static nint _listView;
    private static int _prepaint;
    private static int _itemPrepaint;
    private static int _itemPostpaint;

    internal static int Run()
    {
        var desktopBefore = GetForegroundWindow();
        nint parent = 0, listView = 0;
        var parentSubclassed = false;
        var report = new List<string>();
        try
        {
            var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = 1 };
            if (!InitCommonControlsEx(ref init))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ListView common controls initialization failed.");

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs common ListView color baseline",
                0x10000000 | 0x00CF0000, 80, 80, 680, 420, 0, 0, GetModuleHandleW(null), 0);
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Baseline parent creation failed.");
            ShowWindow(parent, 4); // SW_SHOWNOACTIVATE, confined to the private Desktop.
            listView = CreateWindowExW(0, "SysListView32", "",
                0x40000000 | 0x10000000 | 0x00000001 | 0x00000004,
                8, 8, 640, 360, parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Standard ListView creation failed.");
            _listView = listView;
            if (!SetWindowSubclass(parent, Callback, 73, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Baseline custom-draw observer setup failed.");
            parentSubclassed = true;

            AddColumn(listView, "Name", 340);
            AddColumn(listView, "Recursive size", 140);
            InsertItem(listView, 0, "large-folder", 10, "10 B");
            InsertItem(listView, 1, "small-folder", 4, "4 B");
            if (!IsWindowVisible(parent) || !IsWindowVisible(listView))
                Console.WriteLine($"common-listview-color-baseline: initial-visible-state=parent:{IsWindowVisible(parent)},list:{IsWindowVisible(listView)}");

            foreach (var style in new[]
            {
                (Name: "Icon", View: 0), (Name: "Details", View: 1), (Name: "SmallIcon", View: 2),
                (Name: "List", View: 3), (Name: "Tile", View: 4), (Name: "Content", View: 6)
            })
            {
                _prepaint = 0;
                _itemPrepaint = 0;
                _itemPostpaint = 0;
                Samples.Clear();
                Writes.Clear();
                LabelBounds.Clear();
                SendMessageW(listView, LvmSetView, style.View, 0);
                for (var index = 0; index < 2; index++)
                    if (TryGetItemRect(listView, index, 2, out var labelBounds))
                        LabelBounds[(nuint)index] = labelBounds;
                InvalidateRect(listView, 0, true);
                SendMessageW(listView, LvmRedrawItems, 0, 1);
                UpdateWindow(listView);
                PumpMessages();
                var pixels = string.Join(',', Samples.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}=0x{pair.Value:X6}"));
                var fields = string.Join(',', Writes.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
                var rects = string.Join(',', LabelBounds.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}={pair.Value.Left},{pair.Value.Top},{pair.Value.Right},{pair.Value.Bottom}"));
                report.Add($"{style.Name}:view={style.View};prepaint={_prepaint};item-prepaint={_itemPrepaint};item-postpaint={_itemPostpaint};" +
                    $"pixels={pixels};fields={fields};label-rects={rects};visible={IsWindowVisible(listView)}");
            }

            var initialRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            SetItemText(listView, 1, 1, "16 B");
            SetItemParam(listView, 1, 16);
            var liveValue = GetItemText(listView, 1, 1);
            var sortResult = SendMessageW(listView, LvmSortItemsEx, 0,
                Marshal.GetFunctionPointerForDelegate(SizeCompare));
            var sortedRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            var derivedValueUpdatedAndSorted = initialRows == "large-folder:10,small-folder:4" &&
                liveValue == "16 B" && sortResult != 0 && sortedRows == "large-folder:10,small-folder:16";
            InvalidateRect(listView, 0, true);
            UpdateWindow(listView);
            PumpMessages();
            report.Add($"derived-value-update=True;live-cell={liveValue};size-sort=True;rows={sortedRows}");

            var foregroundUnchanged = GetForegroundWindow() == desktopBefore;
            Console.WriteLine($"common-listview-color-baseline: views={string.Join('|', report)}; " +
                $"visible-test=True; foreground-unchanged={foregroundUnchanged}; shell-view=False");
            return foregroundUnchanged && derivedValueUpdatedAndSorted ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("common-listview-color-baseline: " + ex);
            return 1;
        }
        finally
        {
            if (parentSubclassed && parent != 0) RemoveWindowSubclass(parent, Callback, 73);
            if (parent != 0 && IsWindow(parent)) DestroyWindow(parent);
            _listView = 0;
        }
    }

    private static void AddColumn(nint listView, string text, int width)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var column = new ListViewColumn { Mask = 0x0002 | 0x0004, Width = width, Text = textPointer, TextMax = text.Length };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewColumn>());
        try
        {
            Marshal.StructureToPtr(column, memory, false);
            if (SendMessageW(listView, LvmInsertColumnW, 0, memory) == -1)
                throw new Exception("Standard ListView column insertion failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    internal static int RunDataContract()
    {
        var foregroundBefore = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs.ListViewShellRows", Guid.NewGuid().ToString("N"));
        var shellRows = new List<ShellRow>();
        nint parent = 0, listView = 0;
        try
        {
            var largeFolder = Path.Combine(fixture, "large-folder");
            var smallFolder = Path.Combine(fixture, "small-folder");
            Directory.CreateDirectory(largeFolder);
            Directory.CreateDirectory(smallFolder);
            File.WriteAllBytes(Path.Combine(largeFolder, "payload.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(smallFolder, "payload.bin"), new byte[4]);
            shellRows.Add(CreateShellRow(1, largeFolder, 10));
            shellRows.Add(CreateShellRow(2, smallFolder, 4));
            _shellRowsById = shellRows.ToDictionary(row => row.Id);

            var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = 1 };
            if (!InitCommonControlsEx(ref init))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "ListView common controls initialization failed.");
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden ListView data contract",
                0x00CF0000, 0, 0, 400, 200, 0, 0, GetModuleHandleW(null), 0);
            if (parent == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Hidden ListView parent creation failed.");
            listView = CreateWindowExW(0, "SysListView32", "",
                0x40000000 | 0x00000001 | 0x00000004,
                0, 0, 380, 180, parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Hidden standard ListView creation failed.");

            _listView = listView;
            AddColumn(listView, "Name", 240);
            AddColumn(listView, "Recursive size", 100);
            InsertItem(listView, 0, shellRows[0].DisplayName, shellRows[0].Id, "10 B");
            InsertItem(listView, 1, shellRows[1].DisplayName, shellRows[1].Id, "4 B");
            var initialRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            var initialShellPaths = string.Join('|', Enumerable.Range(0, 2).Select(index =>
                _shellRowsById[GetItemParam(listView, index)].ParsingName));
            SetItemState(listView, -1, 0, LvisSelected | LvisFocused);
            SetItemState(listView, 1, LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            _shellRowsById[2] = _shellRowsById[2] with { RecursiveBytes = 2 };
            SetItemText(listView, 1, 1, "2 B");
            var liveValue = GetItemText(listView, 1, 1);
            var sortResult = SendMessageW(listView, LvmSortItemsEx, 0,
                Marshal.GetFunctionPointerForDelegate(SizeCompare));
            var sortedRows = $"{GetItemText(listView, 0, 0)}:{GetItemParam(listView, 0)}," +
                $"{GetItemText(listView, 1, 0)}:{GetItemParam(listView, 1)}";
            var firstState = GetItemState(listView, 0);
            var secondState = GetItemState(listView, 1);
            var selectedFocusedCount = (firstState == (LvisSelected | LvisFocused) ? 1 : 0) +
                (secondState == (LvisSelected | LvisFocused) ? 1 : 0);
            var selectedName = selectedFocusedCount == 1
                ? GetItemText(listView, firstState == (LvisSelected | LvisFocused) ? 0 : 1, 0)
                : "none";
            var selectedId = selectedFocusedCount == 1
                ? GetItemParam(listView, firstState == (LvisSelected | LvisFocused) ? 0 : 1)
                : 0;
            var shellIdentityPreserved = shellRows.All(row => row.AbsolutePidl != 0 &&
                _shellRowsById.TryGetValue(row.Id, out var current) && current.AbsolutePidl == row.AbsolutePidl);
            var passed = initialRows == "large-folder:1,small-folder:2" &&
                initialShellPaths == $"{largeFolder}|{smallFolder}" && liveValue == "2 B" &&
                sortResult != 0 && sortedRows == "small-folder:2,large-folder:1" &&
                selectedName == "small-folder" && selectedFocusedCount == 1 &&
                selectedId == 2 && shellIdentityPreserved &&
                !IsWindowVisible(parent) && !IsWindowVisible(listView) && GetForegroundWindow() == foregroundBefore;
            Console.WriteLine($"common-listview-data-contract: initial={initialRows};shell-paths={initialShellPaths};" +
                $"updated-cell={liveValue};sort-result={sortResult != 0};sorted={sortedRows};" +
                $"selected-focused-after-sort={selectedName};selected-shell-row-id={selectedId};" +
                $"shell-pidl-identities-preserved={shellIdentityPreserved};" +
                $"selected-focused-count={selectedFocusedCount};visible=False;" +
                $"foreground-unchanged={GetForegroundWindow() == foregroundBefore};passed={passed}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("common-listview-data-contract: " + ex);
            return 1;
        }
        finally
        {
            if (parent != 0 && IsWindow(parent)) DestroyWindow(parent);
            _listView = 0;
            _shellRowsById = null;
            foreach (var row in shellRows)
                if (row.AbsolutePidl != 0) Marshal.FreeCoTaskMem(row.AbsolutePidl);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
        }
    }

    private static ShellRow CreateShellRow(long id, string path, ulong recursiveBytes)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try
        {
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, 0, out var displayName));
            try
            {
                var display = Marshal.PtrToStringUni(displayName);
                if (string.IsNullOrWhiteSpace(display)) throw new InvalidOperationException("Shell returned an empty normal display name.");
                Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, 0x80058000, out var parsingName));
                try
                {
                    var parsing = Marshal.PtrToStringUni(parsingName);
                    if (!string.Equals(Path.GetFullPath(parsing ?? string.Empty), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Shell PIDL parsing identity did not round-trip to its fixture path.");
                    return new ShellRow(id, pidl, display, parsing!, recursiveBytes);
                }
                finally { Marshal.FreeCoTaskMem(parsingName); }
            }
            finally { Marshal.FreeCoTaskMem(displayName); }
        }
        catch
        {
            Marshal.FreeCoTaskMem(pidl);
            throw;
        }
    }

    private static void InsertItem(nint listView, int index, string text, long size, string sizeText)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var item = new ListViewItem { Mask = 0x0001 | 0x0004, Item = index, Text = textPointer,
            TextMax = text.Length, Param = (nint)size };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmInsertItemW, 0, memory).ToInt32() != index)
                throw new Exception($"Standard ListView item {index} insertion failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
        SetItemText(listView, index, 1, sizeText);
    }

    private static void SetItemText(nint listView, int index, int subItem, string text)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var item = new ListViewItem { Mask = 0x0001, Item = index, SubItem = subItem,
            Text = textPointer, TextMax = text.Length };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmSetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index}, subitem {subItem} update failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void SetItemParam(nint listView, int index, long value)
    {
        var item = new ListViewItem { Mask = 0x0004, Item = index, Param = (nint)value };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmSetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index} derived-value update failed.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string GetItemText(nint listView, int index, int subItem)
    {
        var buffer = Marshal.AllocHGlobal(256 * sizeof(char));
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var item = new ListViewItem { Item = index, SubItem = subItem, Text = buffer, TextMax = 256 };
            Marshal.StructureToPtr(item, memory, false);
            SendMessageW(listView, LvmGetItemTextW, index, memory);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long GetItemParam(nint listView, int index)
    {
        var item = new ListViewItem { Mask = 0x0004, Item = index };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            if (SendMessageW(listView, LvmGetItemW, 0, memory) == 0)
                throw new Exception($"Standard ListView item {index} data read failed.");
            return Marshal.PtrToStructure<ListViewItem>(memory).Param.ToInt64();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static uint GetItemState(nint listView, int index) =>
        unchecked((uint)SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)));

    private static void SetItemState(nint listView, int index, uint state, uint stateMask)
    {
        var item = new ListViewItem { State = state, StateMask = stateMask };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ListViewItem>());
        try
        {
            Marshal.StructureToPtr(item, memory, false);
            SendMessageW(listView, LvmSetItemState, index, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static int CompareBySize(nint firstIndex, nint secondIndex, nint sortParameter)
    {
        var first = GetItemParam(_listView, checked((int)firstIndex.ToInt64()));
        var second = GetItemParam(_listView, checked((int)secondIndex.ToInt64()));
        if (_shellRowsById is not null)
        {
            first = checked((long)_shellRowsById[first].RecursiveBytes);
            second = checked((long)_shellRowsById[second].RecursiveBytes);
        }
        return first.CompareTo(second);
    }

    private static nint OnDraw(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmNotify && lParam != 0)
        {
            var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
            if (header.Code == NmCustomDraw && header.Window == _listView)
            {
                var draw = Marshal.PtrToStructure<CustomDraw>(lParam);
                if (draw.Stage == CddsPrePaint)
                {
                    _prepaint++;
                    return (nint)CdrfNotifyItemDraw;
                }
                if (draw.Stage == CddsItemPrePaint)
                {
                    _itemPrepaint++;
                    var color = Colors.GetValueOrDefault(draw.Item, 0x00E0E0E0u);
                    Marshal.WriteInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextColor)).ToInt32(), 0x00202020);
                    Marshal.WriteInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextBackground)).ToInt32(), unchecked((int)color));
                    Writes[draw.Item] = $"text=0x{Marshal.ReadInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextColor)).ToInt32()):X6},background=0x{Marshal.ReadInt32(lParam, Marshal.OffsetOf<CustomDraw>(nameof(CustomDraw.TextBackground)).ToInt32()):X6}";
                    return (nint)CdrfNewFontAndPostPaint;
                }
                if (draw.Stage == CddsItemPostPaint)
                {
                    _itemPostpaint++;
                    var bounds = draw.Bounds.Right > draw.Bounds.Left && draw.Bounds.Bottom > draw.Bounds.Top
                        ? draw.Bounds : LabelBounds.GetValueOrDefault(draw.Item);
                    if (draw.DeviceContext != 0 && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                    {
                        var x = (bounds.Left + bounds.Right) / 2;
                        var y = (bounds.Top + bounds.Bottom) / 2;
                        Samples[draw.Item] = GetPixel(draw.DeviceContext, x, y);
                    }
                }
            }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static void PumpMessages()
    {
        var message = new Message();
        while (PeekMessageW(out message, 0, 0, 0, 1))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static bool TryGetItemRect(nint listView, int index, int part, out Rect rect)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.WriteInt32(memory, part); // LVIR_LABEL.
            if (SendMessageW(listView, 0x100E, index, memory) == 0)
            {
                rect = default;
                return false;
            }
            rect = Marshal.PtrToStructure<Rect>(memory);
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint DrawProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ListViewCompareProc(nint first, nint second, nint sortParameter);
    [StructLayout(LayoutKind.Sequential)] private struct InitCommonControls { public uint Size; public uint Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CustomDraw
    {
        public NotifyHeader Header; public uint Stage; public nint DeviceContext; public Rect Bounds;
        public nuint Item; public uint ItemState; public nint ItemData; public uint TextColor; public uint TextBackground;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListViewItem
    {
        public uint Mask; public int Item, SubItem; public uint State, StateMask; public nint Text; public int TextMax, Image;
        public nint Param; public int Indent, GroupId, Columns; public nint ColumnIndices, ColumnFormat, Group;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListViewColumn
    {
        public uint Mask; public int Format, Width; public nint Text; public int TextMax, SubItem, Image, Order;
        public int MinWidth, DefaultWidth, IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y;
        public uint Private;
    }
    private sealed record ShellRow(long Id, nint AbsolutePidl, string DisplayName, string ParsingName, ulong RecursiveBytes);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, DrawProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, DrawProc callback, nuint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rect, bool erase);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
