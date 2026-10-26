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
    private const int LvmSetView = 0x108E;
    private const int LvmRedrawItems = 0x1015;
    private const uint CdrfNotifyItemDraw = 0x20;
    private const uint CdrfNewFontAndPostPaint = 0x12;
    private static readonly DrawProc Callback = OnDraw;
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
                0x40000000 | 0x10000000 | 0x00000001 | 0x00000004 | 0x00001000,
                8, 8, 640, 360, parent, 0, GetModuleHandleW(null), 0);
            if (listView == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Standard ListView creation failed.");
            _listView = listView;
            if (!SetWindowSubclass(parent, Callback, 73, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Baseline custom-draw observer setup failed.");
            parentSubclassed = true;

            AddColumn(listView, "Name", 440);
            InsertItem(listView, 0, "blue-rule-target.txt");
            InsertItem(listView, 1, "green-rule-target.txt");
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

            var foregroundUnchanged = GetForegroundWindow() == desktopBefore;
            Console.WriteLine($"common-listview-color-baseline: views={string.Join('|', report)}; " +
                $"visible-test=True; foreground-unchanged={foregroundUnchanged}; shell-view=False");
            return foregroundUnchanged ? 0 : 1;
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

    private static void InsertItem(nint listView, int index, string text)
    {
        var textPointer = Marshal.StringToHGlobalUni(text);
        var item = new ListViewItem { Mask = 1, Item = index, Text = textPointer, TextMax = text.Length };
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
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, DrawProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, DrawProc callback, nuint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rect, bool erase);
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
