using System.Reflection;
using System.Runtime.InteropServices;

internal static class NativeThemeColorProbe
{
    internal static int Run()
    {
        var type = Assembly.Load("ExplorerDirs.App").GetType("ExplorerDirs.App.NativeShellTheme", true)!;
        var members = BindingFlags.NonPublic | BindingFlags.Static;
        type.GetMethod("SetProcessTheme", members)!.Invoke(null, [true]);
        var apply = type.GetMethod("ApplyView", members)!;
        var fixture = new ThemeVisualFixture();
        var pointer = Marshal.GetIUnknownForObject(fixture);
        try
        {
            Check((int)apply.Invoke(null, [pointer])! == 0 && fixture.Calls.SequenceEqual(new uint[] { 1, 2, 3, 4, 5 }),
                "IVisualProperties SetColor uses its SDK slot and all five documented color roles");
            Check(fixture.Colors[1] == 0xEEEEEE && fixture.Colors[2] == 0x202020 && fixture.Colors[5] == 0x202020,
                "dark content text and background use contrasting explicit native colors");
            fixture.Calls.Clear();
            fixture.Reject = 2;
            Check((int)apply.Invoke(null, [pointer])! == unchecked((int)0x80004005) && fixture.Calls.Count == 5,
                "a failed native color role propagates failure while remaining roles are attempted");
        }
        finally { Marshal.Release(pointer); }
        var unsupported = Marshal.GetIUnknownForObject(new object());
        try
        {
            Check((int)apply.Invoke(null, [unsupported])! == unchecked((int)0x80004002),
                "a provider without IVisualProperties is explicitly unsupported");
        }
        finally { Marshal.Release(unsupported); }
        CheckTooltipNotifications(type, members);
        CheckMenuOpeningDoesNotRethemeOwner(type, members);
        CheckMenuHookLifetime(type, members);
        CheckMenuClip(type, members);
        CheckMenuPrint(type, members);
        CheckTreeColors(type, members);
        CheckScrollFrame(type, members);
        CheckSubmenuPlacement(type, members);
        Console.WriteLine("Native theme colors: PASS (COM and hidden native fixtures; no visible windows or Shell views)");
        return 0;
    }

    private static void CheckSubmenuPlacement(Type type, BindingFlags members)
    {
        var parent = CreateWindowEx(0x181, "STATIC", "", 0x84800000, 500, 200, 300, 200, 0, 0, 0, 0);
        Check(parent != 0, "hidden submenu parent fixture is created");
        try
        {
            var clip = type.GetMethod("ClipMenuFrame", members)!;
            var place = type.GetMethod("PositionSubmenu", members)!;
            clip.Invoke(null, [parent]);
            GetWindowRect(parent, out var parentBounds);
            var region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                GetWindowRgn(parent, region); GetRgnBox(region, out var parentRegion);
                foreach (var right in new[] { true, false })
                {
                    var child = CreateWindowEx(0x181, "STATIC", "", 0x84800000,
                        right ? parentBounds.Right - 3 : parentBounds.Left + 3 - 150, 240, 150, 120, 0, 0, 0, 0);
                    Check(child != 0, "hidden submenu fixture is created");
                    try
                    {
                        clip.Invoke(null, [child]);
                        GetWindowRect(child, out var before); GetWindowRgn(child, region); GetRgnBox(region, out var childRegion);
                        var overlap = right ? parentBounds.Right - before.Left : before.Right - parentBounds.Left;
                        place.Invoke(null, [parent, child]);
                        GetWindowRect(child, out var after);
                        var visibleOverlap = right ? parentBounds.Left + parentRegion.Right - after.Left - childRegion.Left
                            : after.Left + childRegion.Right - parentBounds.Left - parentRegion.Left;
                        Check(visibleOverlap == overlap && after.Top == before.Top && after.Right - after.Left == before.Right - before.Left,
                            $"{(right ? "right" : "left")} submenu restores native overlap between visible edges without resizing");
                        place.Invoke(null, [parent, child]); GetWindowRect(child, out var repeated);
                        Check(repeated.Equals(after), "repeated submenu placement does not accumulate an offset");
                    }
                    finally { DestroyWindow(child); }
                }
            }
            finally { DeleteObject(region); }
        }
        finally { DestroyWindow(parent); }
    }

    private static void CheckTreeColors(Type type, BindingFlags members)
    {
        var tree = CreateWindowEx(0, "SysTreeView32", "", 0x80000000, 0, 0, 200, 200, 0, 0, 0, 0);
        Check(tree != 0, "hidden native tree fixture is available without a Shell namespace host");
        try
        {
            foreach (var dark in new[] { true, false, true })
            {
                type.GetMethod("SetProcessTheme", members)!.Invoke(null, [dark]);
                type.GetMethod("Apply", members)!.Invoke(null, [tree]);
                var background = (uint)SendMessage(tree, 0x111F, 0, 0).ToInt64();
                var text = (uint)SendMessage(tree, 0x1120, 0, 0).ToInt64();
                Check(background == (dark ? 0x202020u : GetSysColor(5)) && text == (dark ? 0xEEEEEEu : GetSysColor(8)),
                    $"native tree reads back matching {(dark ? "dark" : "light")} background and text");
            }
        }
        finally { DestroyWindow(tree); }
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);

    private static void CheckMenuClip(Type type, BindingFlags members)
    {
        var window = CreateWindowEx(0x181, "STATIC", "", 0x84800000, 100, 100, 300, 200, 0, 0, 0, 0);
        Check(window != 0, "hidden bordered HWND fixture is available for native region clipping");
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            GetWindowRect(window, out var outer);
            GetClientRect(window, out var client);
            var originalClient = client;
            MapWindowPoints(window, 0, ref client, 2);

            var inset = Math.Max(1, GetSystemMetricsForDpi(45, GetDpiForWindow(window)));
            var expected = new RegionRect { Left = client.Left - outer.Left + inset, Top = client.Top - outer.Top + inset,
                Right = client.Right - outer.Left - inset, Bottom = client.Bottom - outer.Top - inset };
            var style = GetWindowLongPtr(window, -16);
            var extended = GetWindowLongPtr(window, -20);
            var clip = type.GetMethod("ClipMenuFrame", members)!;
            clip.Invoke(null, [window]);
            Check(GetWindowRgn(window, region) != 0 && GetRgnBox(region, out var box) != 0 && box.Equals(expected),
                "window region excludes the measured surround and one system edge width");
            GetWindowRect(window, out var afterOuter); GetClientRect(window, out var afterClient);
            Check(outer.Equals(afterOuter) && originalClient.Equals(afterClient) &&
                  style == GetWindowLongPtr(window, -16) && extended == GetWindowLongPtr(window, -20),
                "clipping preserves native window/client geometry and window styles");
            clip.Invoke(null, [window]);
            GetWindowRgn(window, region); GetRgnBox(region, out box);
            Check(box.Equals(expected), "repeat menu clipping retains the same native client region");
            var resetRegion = CreateRectRgn(0, 0, outer.Right - outer.Left, outer.Bottom - outer.Top);
            if (SetWindowRgn(window, resetRegion, true) == 0) { DeleteObject(resetRegion); throw new InvalidOperationException("Cannot simulate a native region reset."); }
            clip.Invoke(null, [window]);
            GetWindowRgn(window, region); GetRgnBox(region, out box);
            Check(box.Equals(expected), "a native popup region reset is repaired even when its size did not change");
            foreach (var dark in new[] { false, true })
            {
                type.GetMethod("SetProcessTheme", members)!.Invoke(null, [dark]);
                clip.Invoke(null, [window]);
                Check(GetProp(window, "ExplorerDirs.MenuBorder") == (dark ? 2 : 1),
                    "single menu boundary updates its theme even when clipping geometry is unchanged");
                GetWindowRgn(window, region); GetRgnBox(region, out box);
                Check(box.Equals(expected), "changing the boundary theme preserves the native clipping region");
            }
        }
        finally { DeleteObject(region); DestroyWindow(window); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RegionRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out RegionRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out RegionRect rect);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from, nint to, ref RegionRect rect, uint count);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(nint region, out RegionRect rect);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);

    private static void CheckScrollFrame(Type type, BindingFlags members)
    {
        var window = CreateWindowEx(0, "STATIC", "", 0x80100000, 0, 0, 300, 200, 0, 0, 0, 0);
        var frames = 0; var content = 0;
        var refresh = type.GetMethod("RefreshScrollFrame", members)!;
        ThemeSubclass callback = (hwnd, message, wp, lp, id, data) =>
        {
            if (message == 0x0085)
            {
                frames++;
                refresh.Invoke(null, [hwnd, true]); // Reentrant native frame notification must not recurse.
            }
            if (message is 0x000F or 0x0014) content++;
            return ThemeDefSubclassProc(hwnd, message, wp, lp);
        };
        Check(window != 0 && ThemeSetWindowSubclass(window, callback, 73, 0), "hidden scrollbar frame fixture observes native paint messages");
        try
        {
            refresh.Invoke(null, [window, false]);
            Check(frames == 1 && content == 0, "scrollbar repair requests one frame paint without client paint or erase");
            for (var i = 0; i < 20; i++) refresh.Invoke(null, [window, false]);
            Check(frames == 1, "20 unchanged frame checks produce no additional repaint");
            refresh.Invoke(null, [window, true]);
            Check(frames == 2 && content == 0, "a new native paint bypasses the theme-scan cache without recursing");
            type.GetMethod("SetProcessTheme", members)!.Invoke(null, [false]);
            refresh.Invoke(null, [window, false]);
            Check(frames == 3 && content == 0, "light theme change repaints only the scrollbar frame");
            type.GetMethod("SetProcessTheme", members)!.Invoke(null, [true]);
            refresh.Invoke(null, [window, false]);
            Check(frames == 4 && content == 0, "dark theme return repaints only the scrollbar frame");
        }
        finally { ThemeRemoveWindowSubclass(window, callback, 73); DestroyWindow(window); GC.KeepAlive(callback); }
    }
    private static void CheckMenuPrint(Type type, BindingFlags members)
    {
        var window = CreateWindowEx(0x181, "STATIC", "", 0x84800000, 0, 0, 300, 200, 0, 0, 0, 0);
        var dc = CreateCompatibleDC(0);
        var bitmap = CreateBitmap(300, 200, 1, 32, 0);
        var previous = SelectObject(dc, bitmap);
        var callback = (Delegate)type.GetField("InitialMenuProcedure", members)!.GetValue(null)!;
        var pointer = Marshal.GetFunctionPointerForDelegate(callback);
        // Model a renderer forwarding its supplied DC to PRINTCLIENT. The outer
        // print pass must own decoration, regardless of the nested DC origin.
        ThemeSubclass renderer = (hwnd, message, wp, lp, id, data) =>
        {
            if (message == 0x0317) return SendMessage(hwnd, 0x0318, wp, lp);
            if (message == 0x0318)
            {
                var fill = new RegionRect { Right = 300, Bottom = 200 };
                var previousColor = SetDCBrushColor((nint)wp, 0x202020);
                try { FillRectForPrint((nint)wp, ref fill, GetStockObject(18)); }
                finally { SetDCBrushColor((nint)wp, previousColor); }
                return 0;
            }
            return ThemeDefSubclassProc(hwnd, message, wp, lp);
        };
        Check(ThemeSetWindowSubclass(window, renderer, 72, 0), "nested print renderer fixture attaches below the menu decoration");
        try
        {
            Check(window != 0 && dc != 0 && bitmap != 0 && SetSubclassPointer(window, pointer, 0xED7604, 0),
                "hidden menu print fixture uses a supplied offscreen DC");
            type.GetMethod("ClipMenuFrame", members)!.Invoke(null, [window]);
            GetWindowRgnBoxForPrint(window, out var edge);
            foreach (var dark in new[] { true, false, true })
            foreach (var message in new uint[] { 0x0317, 0x0318 })
            {
                type.GetMethod("SetProcessTheme", members)!.Invoke(null, [dark]);
                var area = new RegionRect { Right = 300, Bottom = 200 };
                SetDCBrushColor(dc, 0xFF00FF);
                FillRectForPrint(dc, ref area, GetStockObject(18));
                var printedEdge = edge;
                if (message == 0x0318)
                {
                    GetWindowRect(window, out var bounds);
                    MapWindowPoints(0, window, ref bounds, 2);
                    printedEdge.Left += bounds.Left; printedEdge.Top += bounds.Top;
                }
                SendMessage(window, message, (nuint)dc, 0xE);
                Check(GetPixel(dc, printedEdge.Left, printedEdge.Top) == (dark ? 0x565656u : 0xA0A0A0u),
                    $"{message:X} animation image contains the final {(dark ? "dark" : "light")} outline before show");
                Check(GetPixel(dc, 0, 0) == 0xFF00FF,
                    "menu print excludes the old outer frame instead of painting a second border");
                if (message == 0x0317)
                    Check(GetPixel(dc, edge.Right - 4, edge.Top + 10) == 0x202020,
                        "nested PRINTCLIENT leaves no second inset border inside the menu fill");
                Check((nint)type.GetField("_printingMenu", members)!.GetValue(null)! == 0,
                    "print decoration ownership is restored after nested rendering");
                GetClipBox(dc, out var afterClip);
                Check(afterClip.Left == 0 && afterClip.Top == 0 && afterClip.Right == 300 && afterClip.Bottom == 200 &&
                    SetDCBrushColor(dc, 0xFF00FF) == 0xFF00FF,
                    "menu print restores the caller DC clipping and brush color");
            }
        }
        finally
        {
            RemoveSubclassPointer(window, pointer, 0xED7604);
            ThemeRemoveWindowSubclass(window, renderer, 72);
            SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); DestroyWindow(window); GC.KeepAlive(callback);
        }
    }
    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")] private static extern bool SetSubclassPointer(nint window, nint callback, nuint id, nint data);
    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")] private static extern bool RemoveSubclassPointer(nint window, nint callback, nuint id);
    [DllImport("user32.dll", EntryPoint = "GetWindowRgnBox")] private static extern int GetWindowRgnBoxForPrint(nint window, out RegionRect rect);
    [DllImport("user32.dll", EntryPoint = "FillRect")] private static extern int FillRectForPrint(nint dc, ref RegionRect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, uint planes, uint bits, nint data);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern uint SetDCBrushColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern int GetClipBox(nint dc, out RegionRect rect);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    private static void CheckMenuOpeningDoesNotRethemeOwner(Type type, BindingFlags members)
    {
        if (System.Windows.SystemParameters.HighContrast) return;
        var owner = CreateWindowEx(0, "STATIC", "", 0x80000000, 0, 0, 100, 100, 0, 0, 0, 0);
        var themeChanges = 0;
        ThemeSubclass callback = (window, message, wp, lp, id, data) =>
        {
            if (message == 0x031A) themeChanges++;
            return ThemeDefSubclassProc(window, message, wp, lp);
        };
        Check(owner != 0 && ThemeSetWindowSubclass(owner, callback, 71, 0), "hidden owner observes actual WM_THEMECHANGED messages");
        var begin = type.GetMethod("BeginMenu", members)!;
        var end = type.GetMethod("EndMenu", members)!;
        try
        {
            for (var i = 0; i < 10; i++)
            {
                begin.Invoke(null, [owner]);
                end.Invoke(null, null);
            }
            Check(themeChanges == 0, "opening 10 menus does not invalidate the file-view owner's theme");
            type.GetMethod("SetProcessTheme", members)!.Invoke(null, [false]);
            type.GetMethod("Apply", members)!.Invoke(null, [owner]);
            Check(themeChanges > 0, "an explicit theme application still sends its native theme change");
            type.GetMethod("SetProcessTheme", members)!.Invoke(null, [true]);
        }
        finally { end.Invoke(null, null); ThemeRemoveWindowSubclass(owner, callback, 71); DestroyWindow(owner); GC.KeepAlive(callback); }
    }
    private delegate nint ThemeSubclass(nint window, uint message, nuint wp, nint lp, nuint id, nint data);
    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")] private static extern bool ThemeSetWindowSubclass(nint window, ThemeSubclass callback, nuint id, nint data);
    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")] private static extern bool ThemeRemoveWindowSubclass(nint window, ThemeSubclass callback, nuint id);
    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")] private static extern nint ThemeDefSubclassProc(nint window, uint message, nuint wp, nint lp);
    private static void CheckMenuHookLifetime(Type type, BindingFlags members)
    {
        if (System.Windows.SystemParameters.HighContrast) return;
        var begin = type.GetMethod("BeginMenu", members)!;
        var end = type.GetMethod("EndMenu", members)!;
        var field = type.GetField("_menuHook", members)!;
        var creation = type.GetField("_creationHook", members)!;
        try
        {
            begin.Invoke(null, [(nint)0]);
            var handle = (nint)field.GetValue(null)!;
            Check(handle != 0, "native menu observer installs on the current probe thread without showing a menu");
            var creationHandle = (nint)creation.GetValue(null)!;
            Check(creationHandle != 0, "early menu creation hook installs on the same thread");
            Check(!(bool)type.GetMethod("ApplyMenuFramesFromOwner", members)!.Invoke(null, null)!,
                "owner message skips a duplicate frame pass while the return hook is installed");
            var receiver = CreateWindowEx(0, "STATIC", "", 0x80000000, 0, 0, 10, 10, 0, 0, 0, 0);
            Check(receiver != 0, "hidden native menu message receiver is available");
            try
            {
                var timestamp = type.GetField("_lastMenuPaintTimestamp", members)!;
                timestamp.SetValue(null, 0L);
                SendMessage(receiver, 0x011F, 0, 0);
                Check((long)timestamp.GetValue(null)! != 0,
                    "actual sent menu message reaches the thread return hook after native processing");
            }
            finally { DestroyWindow(receiver); }
            begin.Invoke(null, [(nint)0]);
            Check((nint)field.GetValue(null)! == handle && (nint)creation.GetValue(null)! == creationHandle,
                "nested menu loops share both thread-local hook installations");
            end.Invoke(null, null);
            Check((nint)field.GetValue(null)! == handle, "leaving a nested menu keeps the outer observer alive");
            end.Invoke(null, null);
            Check((nint)field.GetValue(null)! == 0 && (nint)creation.GetValue(null)! == 0,
                "leaving the outer menu releases both native hooks");
            end.Invoke(null, null);
            Check((nint)field.GetValue(null)! == 0, "repeated menu cleanup is harmless");
            Check((bool)type.GetMethod("ApplyMenuFramesFromOwner", members)!.Invoke(null, null)!,
                "owner frame processing remains available when no hook is installed");
        }
        finally { end.Invoke(null, null); end.Invoke(null, null); }
    }

    private static void CheckTooltipNotifications(Type type, BindingFlags members)
    {
        InitCommonControls();
        var window = CreateWindowEx(0, "tooltips_class32", "", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Check(window != 0, "hidden native tooltip fixture is created without a Shell view");
        var notification = Marshal.AllocHGlobal(Marshal.SizeOf<TooltipHeader>());
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var posted = 0;
        System.Windows.Threading.DispatcherHookEventHandler hook = (_, _) => posted++;
        dispatcher.Hooks.OperationPosted += hook;
        try
        {
            Marshal.StructureToPtr(new TooltipHeader { Window = window, Code = -530 }, notification, false);
            var apply = type.GetMethod("ApplyTooltip", members)!;
            apply.Invoke(null, [notification]);
            apply.Invoke(null, [notification]);
            Check(posted == 1 && GetProp(window, "ExplorerDirs.TooltipTheme") == 2,
                "repeated tooltip notifications coalesce before deferred theme application");
            var frame = new System.Windows.Threading.DispatcherFrame();
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            var applyNative = type.GetMethod("Apply", members)!;
            applyNative.Invoke(null, [window]);
            Check(GetProp(window, "ExplorerDirs.NativeTheme") == 2,
                "native child theme is stamped before repeat refreshes");
            applyNative.Invoke(null, [window]);
            Check(GetProp(window, "ExplorerDirs.NativeTheme") == 2,
                "repeat native child refresh retains the same applied-theme stamp");
            posted = 0;
            apply.Invoke(null, [notification]);
            Check(posted == 0, "an already themed tooltip does not requeue SetWindowTheme");
            var menuTheme = type.GetMethod("ApplyMenuWindow", members)!;
            Check((bool)menuTheme.Invoke(null, [window])! && GetProp(window, "ExplorerDirs.MenuTheme") == 2,
                "native menu theme applies dark once (hidden HWND state fixture)");
            Check(!(bool)menuTheme.Invoke(null, [window])!, "repeat dark application is skipped");
            type.GetMethod("SetProcessTheme", members)!.Invoke(null, [false]);
            Check((bool)menuTheme.Invoke(null, [window])! && GetProp(window, "ExplorerDirs.MenuTheme") == 1,
                "native menu restores light on the same HWND after dark");
            Check(!(bool)menuTheme.Invoke(null, [window])!, "repeat light application is skipped");
            apply.Invoke(null, [notification]);
            Check(posted == 1 && GetProp(window, "ExplorerDirs.TooltipTheme") == 1,
                "switching to light invalidates the tooltip color stamp once");
            frame = new System.Windows.Threading.DispatcherFrame();
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            type.GetMethod("SetProcessTheme", members)!.Invoke(null, [true]);
            Check((bool)menuTheme.Invoke(null, [window])! && GetProp(window, "ExplorerDirs.MenuTheme") == 2,
                "native menu can return to dark after light on the same HWND");
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= hook;
            Marshal.FreeHGlobal(notification);
            DestroyWindow(window);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct TooltipHeader { public nint Window; public nuint Id; public int Code; }
    [DllImport("comctl32.dll")] private static extern void InitCommonControls();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint window, string name);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}

// SDK IVisualProperties vtable prefix, slots 3..5. This fixture exercises
// SetColor only, never advertises support for a real file-view implementation.
[ComVisible(true), Guid("e693cf68-d967-4112-8763-99172aee5e5a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThemeVisualPrefix
{
    [PreserveSig] int SetWatermark(nint bitmap, uint flags);
    [PreserveSig] int SetColor(uint role, uint color);
    [PreserveSig] int GetColor(uint role, out uint color);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ThemeVisualFixture : IThemeVisualPrefix
{
    public readonly Dictionary<uint, uint> Colors = [];
    public readonly List<uint> Calls = [];
    public uint Reject;
    public int SetWatermark(nint bitmap, uint flags) => 0;
    public int SetColor(uint role, uint color)
    {
        Calls.Add(role);
        if (role == Reject) return unchecked((int)0x80004005);
        Colors[role] = color;
        return 0;
    }
    public int GetColor(uint role, out uint color) { color = Colors.GetValueOrDefault(role); return 0; }
}
