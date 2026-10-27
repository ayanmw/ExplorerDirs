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
        Console.WriteLine("Native theme colors: PASS (COM fixture; no windows or Shell views)");
        return 0;
    }

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
