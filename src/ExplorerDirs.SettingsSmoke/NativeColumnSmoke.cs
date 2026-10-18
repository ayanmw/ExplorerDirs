using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class NativeColumnSmoke
{
    public static void Run()
    {
        var server = new ColumnTestServer();
        var unknown = Marshal.GetIUnknownForObject(server);
        try
        {
            var result = ShellColumnManager.Capture(unknown, out var captured);
            Require(result == 0 && captured is { Count: 2 } && captured[0].Width == 140 &&
                captured[1].Width == 280 && captured[0].Key.PropertyId == 10 && captured[1].Key.PropertyId == 12,
                "native adapter reads ordered keys and widths through the SDK vtable");
            var reordered = captured!.Reverse().Select(column => column with { Width = column.Width + 20 }).ToArray();
            Require(ShellColumnManager.Apply(unknown, reordered) == 0 && server.WriteCalls == 2,
                "native adapter applies the visible columns and each width");
            Require(ShellColumnManager.Capture(unknown, out var roundTrip) == 0 && roundTrip!.SequenceEqual(reordered),
                "COM capture after apply preserves order, key and width");
            server.FailRead = true;
            Require(ShellColumnManager.Capture(unknown, out var failed) < 0 && failed is null,
                "partial COM capture never returns a successful layout");
            server.FailRead = false;
            server.FailSet = true;
            var writes = server.WriteCalls;
            Require(ShellColumnManager.Apply(unknown, reordered) < 0 && server.WriteCalls == writes,
                "SetColumns failure prevents width writes");
            server.FailSet = false;
            server.CountOverride = ColumnLayout.MaximumColumns + 1;
            Require(ShellColumnManager.Capture(unknown, out failed) < 0 && failed is null,
                "unreasonable native column count is rejected before allocation");
            server.CountOverride = 0;
            Require(ShellColumnManager.Capture(unknown, out failed) < 0 && failed is null,
                "empty transient column reads cannot erase a saved layout");
        }
        finally { Marshal.Release(unknown); GC.KeepAlive(server); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception("Failed: " + message);
    }
}

// Independent SDK-shaped COM server exercises the real marshaling without a Shell window.
[ComVisible(true), Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IColumnTest
{
    [PreserveSig] int SetColumnInfo(ref TestColumnKey key, ref TestColumnInfo info);
    [PreserveSig] int GetColumnInfo(ref TestColumnKey key, ref TestColumnInfo info);
    [PreserveSig] int GetColumnCount(uint flags, out uint count);
    [PreserveSig] int GetColumns(uint flags,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] TestColumnKey[] keys, uint count);
    [PreserveSig] int SetColumns(
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] TestColumnKey[] keys, uint count);
}

[StructLayout(LayoutKind.Sequential)]
public struct TestColumnKey { public Guid FormatId; public uint PropertyId; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct TestColumnInfo
{
    public uint Size, Mask, State, Width, DefaultWidth, IdealWidth;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Name;
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ColumnTestServer : IColumnTest
{
    private TestColumnKey[] _keys =
    [
        new() { FormatId = new("b725f130-47ef-101a-a5f1-02608c9eebac"), PropertyId = 10 },
        new() { FormatId = new("b725f130-47ef-101a-a5f1-02608c9eebac"), PropertyId = 12 }
    ];
    private readonly Dictionary<uint, uint> _widths = new() { [10] = 140, [12] = 280 };
    public bool FailRead, FailSet;
    public uint? CountOverride;
    public int WriteCalls;
    public int SetColumnInfo(ref TestColumnKey key, ref TestColumnInfo info)
    {
        if (info.Size != 184 || info.Mask != 1) return unchecked((int)0x80070057);
        _widths[key.PropertyId] = info.Width;
        WriteCalls++;
        return 0;
    }
    public int GetColumnInfo(ref TestColumnKey key, ref TestColumnInfo info)
    {
        if (FailRead && key.PropertyId == 10) return unchecked((int)0x80004005);
        if (info.Size != 184 || info.Mask != 1) return unchecked((int)0x80070057);
        info.Width = _widths[key.PropertyId];
        return 0;
    }
    public int GetColumnCount(uint flags, out uint count)
    {
        count = CountOverride ?? (uint)_keys.Length;
        return flags == 2 ? 0 : unchecked((int)0x80070057);
    }
    public int GetColumns(uint flags, TestColumnKey[] keys, uint count)
    {
        if (flags != 2 || count != _keys.Length) return unchecked((int)0x80070057);
        _keys.CopyTo(keys, 0);
        return 0;
    }
    public int SetColumns(TestColumnKey[] keys, uint count)
    {
        if (FailSet) return unchecked((int)0x80004005);
        _keys = keys.Take((int)count).ToArray();
        return 0;
    }
}
