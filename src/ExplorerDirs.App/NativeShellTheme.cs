using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerDirs.App;

// Windows 10 2004+ compatibility bridge, matching the versioned uxtheme
// entry points used by Microsoft's PowerToys. Not a public Shell contract.
internal static class NativeShellTheme
{
    private static bool _dark;
    private static readonly Subclass HeaderParentProcedure = HeaderParentMessage;
    private static readonly Subclass BackgroundProcedure = BackgroundMessage;
    private static readonly Subclass MenuFrameProcedure = MenuFrameMessage;
    internal static void SetProcessTheme(bool dark)
    {
        _dark = dark;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        try { SetPreferredAppMode(dark ? 2 : 3); FlushMenuThemes(); }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    internal static void Apply(nint window)
    {
        if (window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        try
        {
            ApplyOne(window);
            EnumChildWindows(window, (child, _) => { ApplyOne(child); return true; }, 0);
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    private static void ApplyOne(nint window)
    {
        // Applying the same theme sends WM_THEMECHANGED again and can recreate
        // child controls. Stamp before the native call to break refresh reentrancy.
        const string property = "ExplorerDirs.NativeTheme";
        var token = _dark ? (nint)2 : (nint)1;
        if (GetProp(window, property) == token || !SetProp(window, property, token)) return;
        _ = SetWindowSubclass(window, BackgroundProcedure, 0xED7603, 0);
        AllowDarkModeForWindow(window, _dark);
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        SetWindowTheme(window, _dark ? (name.ToString() == "SysHeader32" ? "DarkMode_ItemsView" : "DarkMode_Explorer") : "Explorer", null);
        if (name.ToString() == "SysHeader32")
            _ = SetWindowSubclass(GetParent(window), HeaderParentProcedure, 0xED7601, 0);
    }

    // Newly created hosts can erase before OnViewCreated supplies IVisualProperties.
    // Fill only the native erase pass; leave Shell content, input and painting intact.
    private static nint BackgroundMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nint data)
    {
        if (_dark && message == 0x0014 && wParam != 0 && GetClientRect(window, out var rect))
        {
            var previous = SetDCBrushColor((nint)wParam, 0x202020);
            try { FillRect((nint)wParam, ref rect, GetStockObject(18)); }
            finally { SetDCBrushColor((nint)wParam, previous); }
            return 1;
        }
        if (message == 0x0082) RemoveWindowSubclass(window, BackgroundProcedure, id);
        return DefSubclassProc(window, message, wParam, lParam);
    }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref NativeRect rect, nint brush);

    internal static int ApplyView(nint view)
    {
        var iid = new Guid("e693cf68-d967-4112-8763-99172aee5e5a");
        var query = Marshal.QueryInterface(view, in iid, out var properties);
        if (query < 0) return query;
        try
        {
            var setColor = Marshal.GetDelegateForFunctionPointer<SetColor>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(properties), 4 * nint.Size));
            var background = _dark ? 0x202020u : GetSysColor(5);
            var text = _dark ? 0xEEEEEEu : GetSysColor(8);
            var failure = 0;
            foreach (var entry in new[] { (1u, text), (2u, background), (3u, background), (4u, text), (5u, background) })
            {
                var result = setColor(properties, entry.Item1, entry.Item2);
                if (failure == 0 && result < 0) failure = result;
            }
            return failure;
        }
        finally { Marshal.Release(properties); }
    }

    internal static void ApplyMenuFrames()
    {
        if (!_dark || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
        {
            var name = new StringBuilder(32);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "#32768") return true;
            const string property = "ExplorerDirs.MenuTheme";
            if (GetProp(window, property) == 2 || !SetProp(window, property, 2)) return true;
            AllowDarkModeForWindow(window, true);
            SetWindowTheme(window, "DarkMode_Explorer", null);
            var color = _dark ? 0x404040u : GetSysColor(16);
            _ = DwmSetWindowAttribute(window, 34, ref color, sizeof(uint));
            _ = SetWindowSubclass(window, MenuFrameProcedure, 0xED7602, 0);
            DrawMenuFrame(window);
            return true;
        }, 0);
    }

    private static nint MenuFrameMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nint data)
    {
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0082) RemoveWindowSubclass(window, MenuFrameProcedure, id);
        else if (_dark && message is 0x000F or 0x0085) DrawMenuFrame(window);
        return result;
    }

    private static void DrawMenuFrame(nint window)
    {
        if (!GetWindowRect(window, out var bounds)) return;
        var dc = GetWindowDC(window);
        if (dc == 0) return;
        var previous = SetDCBrushColor(dc, 0x404040);
        try
        {
            var rect = new NativeRect { Right = bounds.Right - bounds.Left, Bottom = bounds.Bottom - bounds.Top };
            var width = Math.Max(1, GetSystemMetricsForDpi(45, GetDpiForWindow(window))); // SM_CXEDGE
            for (var i = 0; i < width; i++)
            {
                FrameRect(dc, ref rect, GetStockObject(18)); // DC_BRUSH; no retained GDI allocation.
                rect.Left++; rect.Top++; rect.Right--; rect.Bottom--;
            }
        }
        finally { SetDCBrushColor(dc, previous); ReleaseDC(window, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern int FrameRect(nint dc, ref NativeRect rect, nint brush);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern uint SetDCBrushColor(nint dc, uint color);

    internal static void ApplyTooltip(nint notification)
    {
        if (notification == 0) return;
        var header = Marshal.PtrToStructure<NotifyHeader>(notification);
        if (header.Code is not (-521 or -530)) return; // TTN_SHOW / TTN_GETDISPINFOW
        var name = new StringBuilder(32);
        GetClassName(header.Window, name, name.Capacity);
        if (name.ToString() != "tooltips_class32") return;
        // TTN notifications are synchronous. SetWindowTheme may generate another
        // tooltip request; mark before queuing and never retheme from that callback.
        const string property = "ExplorerDirs.TooltipTheme";
        var token = _dark ? (nint)2 : (nint)1;
        if (GetProp(header.Window, property) == token || !SetProp(header.Window, property, token)) return;
        var window = header.Window;
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsWindow(window) || GetProp(window, property) != token || (_dark ? 2 : 1) != token) return;
            try { AllowDarkModeForWindow(window, _dark); }
            catch (EntryPointNotFoundException) { }
            SetWindowTheme(window, _dark ? "DarkMode_Explorer" : "Explorer", null);
            SendMessage(window, 0x0413, _dark ? 0x2D2D2Du : GetSysColor(24), 0);
            SendMessage(window, 0x0414, _dark ? 0xF1F1F1u : GetSysColor(23), 0);
        }));
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint window, string name, nint value);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetColor(nint self, uint flags, uint color);
    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);

    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool AllowDarkModeForWindow(nint window, [MarshalAs(UnmanagedType.I1)] bool allow);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint window, string app, string? list);
    private delegate bool EnumWindow(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint data);

    private static nint HeaderParentMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nint data)
    {
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0082) RemoveWindowSubclass(window, HeaderParentProcedure, id);
        if (message != 0x004E || lParam == 0) return result;
        var header = Marshal.PtrToStructure<NotifyHeader>(lParam);
        if (header.Code != -12) return result; // NM_CUSTOMDRAW
        var name = new StringBuilder(32);
        GetClassName(header.Window, name, name.Capacity);
        if (name.ToString() != "SysHeader32") return result;
        var draw = Marshal.PtrToStructure<CustomDraw>(lParam);
        if (draw.Stage == 1) return result | 0x20; // CDRF_NOTIFYITEMDRAW
        if (draw.Stage == 0x10001)
        {
            SetTextColor(draw.Dc, _dark ? 0xEEEEEEu : GetSysColor(8));
            return result | 2; // CDRF_NEWFONT preserves the supplied text color.
        }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct CustomDraw
    {
        public NotifyHeader Header; public uint Stage; public nint Dc;
        public int Left, Top, Right, Bottom; public nuint Item; public uint State; public nint ItemData;
    }
    private delegate nint Subclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, Subclass callback, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
}
