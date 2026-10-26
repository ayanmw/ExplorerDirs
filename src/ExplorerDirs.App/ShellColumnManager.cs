using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Windows SDK ShObjIdl_core.h: IColumnManager, CM_COLUMNINFO, PROPERTYKEY.
internal static class ShellColumnManager
{
    private static readonly Guid InterfaceId = new("d8ec27bb-3f3b-4042-b10a-4acfd924d453");
    private static readonly Guid PropertyDescriptionId = new("6F79D558-3E96-4549-A1D1-7D75D2288814");
    private const int NoInterface = unchecked((int)0x80004002);
    private const int InvalidData = unchecked((int)0x8007000D);
    private const uint VisibleColumns = 2;

    public static int Capture(IntPtr folderView, out IReadOnlyList<DisplayColumnState>? columns)
    {
        columns = null;
        var result = Marshal.QueryInterface(folderView, in InterfaceId, out var manager);
        if (result < 0) return result == NoInterface ? 1 : result;
        try
        {
            result = Method<GetCount>(manager, 5)(manager, VisibleColumns, out var count);
            if (result != 0) return result < 0 ? result : InvalidData;
            if (count is 0 or > ColumnLayout.MaximumColumns) return InvalidData;
            var keys = new NativeKey[count];
            result = Method<GetColumns>(manager, 6)(manager, VisibleColumns, keys, count);
            if (result != 0) return result < 0 ? result : InvalidData;
            var captured = new List<DisplayColumnState>((int)count);
            foreach (var keyValue in keys)
            {
                var key = keyValue;
                var info = ColumnInfo.ForWidth();
                result = Method<ColumnInfoMethod>(manager, 4)(manager, ref key, ref info);
                if (result != 0) return result < 0 ? result : InvalidData;
                if ((info.Mask & 1) == 0) return InvalidData;
                captured.Add(new DisplayColumnState(new(key.FormatId, key.PropertyId), unchecked((int)info.Width)));
            }
            if (!ColumnLayout.IsValid(captured)) return InvalidData;
            columns = captured;
            return 0;
        }
        finally { Marshal.Release(manager); }
    }

    internal static int CaptureForExport(IntPtr folderView, out IReadOnlyList<ShellExportColumn>? columns)
    {
        columns = null;
        var result = Capture(folderView, out var layout);
        if (result != 0 || layout is null) return result;
        columns = layout.Select(column => new ShellExportColumn(column.Key, GetDisplayName(column.Key))).ToArray();
        return 0;
    }

    private static string GetDisplayName(PropertyKeyState propertyKey)
    {
        var key = new NativeKey { FormatId = propertyKey.FormatId, PropertyId = propertyKey.PropertyId };
        var iid = PropertyDescriptionId;
        if (PSGetPropertyDescription(in key, in iid, out var description) < 0 || description == IntPtr.Zero)
            return $"{propertyKey.FormatId:B}:{propertyKey.PropertyId}";
        try
        {
            var result = Method<GetDisplayNameMethod>(description, 6)(description, out var name);
            if (result < 0 || name == IntPtr.Zero) return $"{propertyKey.FormatId:B}:{propertyKey.PropertyId}";
            try { return Marshal.PtrToStringUni(name) ?? $"{propertyKey.FormatId:B}:{propertyKey.PropertyId}"; }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.Release(description); }
    }

    public static int Apply(IntPtr folderView, IReadOnlyList<DisplayColumnState> columns)
    {
        if (!ColumnLayout.IsValid(columns)) return InvalidData;
        var result = Marshal.QueryInterface(folderView, in InterfaceId, out var manager);
        if (result < 0) return result;
        try
        {
            var keys = columns.Select(column => new NativeKey
                { FormatId = column.Key.FormatId, PropertyId = column.Key.PropertyId }).ToArray();
            result = Method<SetColumns>(manager, 7)(manager, keys, (uint)keys.Length);
            if (result != 0) return result < 0 ? result : InvalidData;
            for (var index = 0; index < keys.Length; index++)
            {
                var info = ColumnInfo.ForWidth(unchecked((uint)columns[index].Width));
                result = Method<ColumnInfoMethod>(manager, 3)(manager, ref keys[index], ref info);
                if (result != 0) return result < 0 ? result : InvalidData;
            }
            return 0;
        }
        finally { Marshal.Release(manager); }
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeKey { public Guid FormatId; public uint PropertyId; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ColumnInfo
    {
        public uint Size, Mask, State, Width, DefaultWidth, IdealWidth;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Name;
        public static ColumnInfo ForWidth(uint width = 0) => new()
        {
            Size = (uint)Marshal.SizeOf<ColumnInfo>(), Mask = 1, Width = width, Name = string.Empty
        };

    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCount(IntPtr self, uint flags, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetColumns(IntPtr self, uint flags,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] NativeKey[] keys, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetColumns(IntPtr self,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] NativeKey[] keys, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ColumnInfoMethod(IntPtr self, ref NativeKey key, ref ColumnInfo info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDisplayNameMethod(IntPtr self, out IntPtr name);

    [DllImport("propsys.dll", PreserveSig = true)]
    private static extern int PSGetPropertyDescription(in NativeKey key, in Guid iid, out IntPtr description);
}

internal sealed record ShellExportColumn(PropertyKeyState Key, string Name);
