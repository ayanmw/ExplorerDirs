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
        CheckMenuHookLifetime(type, members);
        Console.WriteLine("Native theme colors: PASS (COM fixture and hidden tooltip; no visible windows or Shell views)");
        return 0;
    }

    private static void CheckMenuHookLifetime(Type type, BindingFlags members)
    {
        if (System.Windows.SystemParameters.HighContrast) return;
        var begin = type.GetMethod("BeginMenu", members)!;
        var end = type.GetMethod("EndMenu", members)!;
        var field = type.GetField("_menuHook", members)!;
        try
        {
            begin.Invoke(null, [(nint)0]);
            var handle = (nint)field.GetValue(null)!;
            Check(handle != 0, "native menu observer installs on the current probe thread without showing a menu");
            begin.Invoke(null, [(nint)0]);
            Check((nint)field.GetValue(null)! == handle, "nested menu loops share one thread-local installation");
            end.Invoke(null, null);
            Check((nint)field.GetValue(null)! == handle, "leaving a nested menu keeps the outer observer alive");
            end.Invoke(null, null);
            Check((nint)field.GetValue(null)! == 0, "leaving the outer menu releases the native hook");
            end.Invoke(null, null);
            Check((nint)field.GetValue(null)! == 0, "repeated menu cleanup is harmless");
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
