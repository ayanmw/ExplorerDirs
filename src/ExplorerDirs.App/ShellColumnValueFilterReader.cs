using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Reads the same localized display text Shell exposes for a column so persisted
// checklist values can be matched without inventing a second formatting rule.
internal static class ShellColumnValueFilterReader
{
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");

    internal static bool TryGetDisplayValue(nint shellFolder, nint itemPidl,
        PropertyKeyState propertyKey, out string value)
    {
        value = string.Empty;
        if (shellFolder == 0 || itemPidl == 0 || propertyKey.FormatId == Guid.Empty ||
            Marshal.QueryInterface(shellFolder, in ShellFolder2Id, out var folder2) < 0 || folder2 == 0)
            return false;

        nint key = 0;
        nint variant = 0;
        nint propVariant = 0;
        nint display = 0;
        try
        {
            key = Marshal.AllocHGlobal(Marshal.SizeOf<NativePropertyKey>());
            variant = Marshal.AllocHGlobal(Marshal.SizeOf<NativeVariant>());
            propVariant = Marshal.AllocHGlobal(Marshal.SizeOf<NativePropVariant>());
            Marshal.StructureToPtr(new NativePropertyKey
            {
                FormatId = propertyKey.FormatId,
                PropertyId = propertyKey.PropertyId
            }, key, false);
            Zero(variant, Marshal.SizeOf<NativeVariant>());
            Zero(propVariant, Marshal.SizeOf<NativePropVariant>());

            if (At<GetDetailsEx>(folder2, 17)(folder2, itemPidl, key, variant) < 0 ||
                InitPropVariantFromVariant(variant, propVariant) < 0 ||
                PSFormatForDisplayAlloc(key, propVariant, 0, out display) < 0 || display == 0)
                return false;

            value = Marshal.PtrToStringUni(display) ?? string.Empty;
            return true;
        }
        finally
        {
            if (display != 0) Marshal.FreeCoTaskMem(display);
            if (propVariant != 0)
            {
                PropVariantClear(propVariant);
                Marshal.FreeHGlobal(propVariant);
            }
            if (variant != 0)
            {
                VariantClear(variant);
                Marshal.FreeHGlobal(variant);
            }
            if (key != 0) Marshal.FreeHGlobal(key);
            Marshal.Release(folder2);
        }
    }

    private static void Zero(nint buffer, int length)
    {
        for (var offset = 0; offset < length; offset++) Marshal.WriteByte(buffer, offset, 0);
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeVariant
    {
        public ushort Type;
        private ushort _reserved1, _reserved2, _reserved3;
        private NativeVariantData _data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePropVariant
    {
        public ushort Type;
        private ushort _reserved1, _reserved2, _reserved3;
        private NativeVariantData _data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeVariantData
    {
        public nint First;
        public nint Second;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDetailsEx(nint self, nint childPidl, nint propertyKey, nint variant);

    [DllImport("propsys.dll", ExactSpelling = true)]
    private static extern int InitPropVariantFromVariant(nint variant, nint propVariant);

    [DllImport("propsys.dll", ExactSpelling = true)]
    private static extern int PSFormatForDisplayAlloc(nint propertyKey, nint propVariant,
        uint formatFlags, out nint display);

    [DllImport("propsys.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(nint propVariant);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int VariantClear(nint variant);
}
