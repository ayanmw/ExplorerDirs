using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellSelectionSize
{
    // The caller retains ownership of the IShellItemArray interface pointer.
    internal static ulong? Read(nint items, int expectedCount)
    {
        if (items == 0 || expectedCount <= 0) return null;
        nint store = 0;
        var value = new Variant();
        try
        {
            if (Method<GetCount>(items, 7)(items, out var count) != 0 || count != expectedCount)
                return null;
            var storeId = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            // Request only cached/innate properties: never opt into slow
            // property handlers or opening offline files to fill missing size.
            if (Method<GetStore>(items, 4)(items, 8, ref storeId, out store) != 0 || store == 0)
                return null;
            var key = new Key { Format = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), Id = 12 };
            var result = Method<GetValue>(store, 5)(store, ref key, out value);
            // VT_EMPTY is unknown, including folder-only selections. An
            // actual VT_UI8 zero is a valid size (for example an empty file).
            return result == 0 && value.Type == 21 ? value.UInt64 : null;
        }
        finally
        {
            PropVariantClear(ref value);
            if (store != 0) Marshal.Release(store);
        }
    }

    private static T Method<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public ulong UInt64; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetStore(nint self, uint flags, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetValue(nint self, ref Key key, out Variant value);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
}
