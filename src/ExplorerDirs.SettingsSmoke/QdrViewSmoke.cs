using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class QdrViewSmoke
{
    public static void Run()
    {
        var key = new PropertyKeyState(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10);
        var sort = new byte[28];
        Write(sort, 0, 1); key.FormatId.TryWriteBytes(sort.AsSpan(4)); Write(sort, 20, 10); Write(sort, 24, -1);
        var columns = new byte[48];
        Write(columns, 0, unchecked((int)0xfddfdffd)); Write(columns, 4, 16);
        Write(columns, 16, 1); Write(columns, 20, 24);
        key.FormatId.TryWriteBytes(columns.AsSpan(24)); Write(columns, 40, 10); Write(columns, 44, 321);
        var properties = new Dictionary<string, object>
        {
            ["Mode"] = 4u, ["IconSize"] = 48u, ["GroupView"] = true,
            ["GroupByKey:FMTID"] = key.FormatId.ToString("B"), ["GroupByKey:PID"] = 10u,
            ["GroupByDirection"] = uint.MaxValue, ["Sort"] = sort, ["ColInfo"] = columns
        };
        var serialized = Serialize(properties);
        var imported = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\fixture\nColsAndSort1=" + serialized,
            (value, _) => value).Panes[0][0].View;
        Require(imported.Mode == 4 && imported.IconSize == 48 && imported.SortColumns.Count == 1 &&
            imported.SortColumns[0] == new SortColumnState(key, -1) && imported.GroupKey == key &&
            !imported.GroupAscending && imported.DisplayColumns is { Count: 1 } && imported.DisplayColumns[0].Width == 321,
            "QDR native property storage imports icon size, sorting, grouping and column widths");
        var explicitMode = QdrImporter.Parse("Q-Dir Version=12.79\nDir1=C:\\fixture\nViewMode1=3\nColsAndSort1=" + serialized,
            (value, _) => value).Panes[0][0].View;
        Require(explicitMode.Mode == 3 && explicitMode.IconSize == 48, "explicit tab mode overrides stored mode only");
        var plainGroup = new Dictionary<string, object>(properties)
        { ["GroupView"] = false, ["GroupByKey:FMTID"] = Guid.Empty.ToString("B"), ["GroupByKey:PID"] = 0u };
        Require(QdrViewSettings.TryRead(Serialize(plainGroup), out var ungrouped) && ungrouped.GroupKey is null,
            "disabled grouping accepts the native empty group key");
        var roundTrip = SessionStore.Parse(SessionStore.Render(new SessionState(4, 4,
            [[new TabSettings(@"C:\fixture", imported)], [], [], []])))!.Panes[0][0].View;
        Require(roundTrip.IconSize == 48 && roundTrip.GroupKey == key && !roundTrip.GroupAscending &&
            roundTrip.SortColumns.SequenceEqual(imported.SortColumns) && roundTrip.DisplayColumns!.SequenceEqual(imported.DisplayColumns!),
            "imported settings survive portable INI round trip");

        foreach (var malformed in new[] { serialized + "0", serialized.Replace("CODEMODE1", "CODEMODE2"),
                     "%GG" + serialized[3..], "%00" + serialized[3..], serialized[..^1], new string('x', 200001) })
            Reject(malformed);
        foreach (var (name, invalid) in new (string, object)[]
        {
            ("Mode", 99u), ("IconSize", 1025u), ("GroupView", 1u), ("GroupByDirection", 0u),
            ("GroupByKey:FMTID", "invalid"), ("GroupByKey:FMTID", Guid.Empty.ToString("B")),
            ("Sort", sort[..^1]), ("ColInfo", columns[..^1]), ("ColInfo", "wrong type")
        })
        {
            var invalidProperties = new Dictionary<string, object>(properties) { [name] = invalid };
            Reject(Serialize(invalidProperties));
        }
        var missing = new Dictionary<string, object>(properties);
        missing.Remove("ColInfo"); Reject(Serialize(missing));
        var duplicateColumns = new byte[72];
        columns.CopyTo(duplicateColumns, 0); columns.AsSpan(24).CopyTo(duplicateColumns.AsSpan(48));
        Write(duplicateColumns, 16, 2);
        Reject(Serialize(new Dictionary<string, object>(properties) { ["ColInfo"] = duplicateColumns }));
        var invalidSort = (byte[])sort.Clone(); Write(invalidSort, 24, 0);
        Reject(Serialize(new Dictionary<string, object>(properties) { ["Sort"] = invalidSort }));
    }

    private static void Reject(string value)
    {
        Require(!QdrViewSettings.TryRead(value, out var view) && view.Mode == 4 && view.DisplayColumns is null &&
            view.SortColumns.Count == 0 && view.GroupKey is null && view.IconSize == -1,
            "unknown or malformed view storage must discard the whole snapshot");
        var imported = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\fixture\nViewMode1=3\nColsAndSort1=" + value,
            (path, _) => path);
        Require(imported.TabCount == 1 && imported.Panes[0][0].View.Mode == 3 &&
            imported.Warnings.Any(warning => warning.Contains("格式无法识别")),
            "unsupported view storage retains the tab and explicit mode with a visible warning");
    }

    private static void Write(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Require(bool valid, string message)
    { if (!valid) throw new Exception("Failed: " + message); }

    internal static string Serialize(Dictionary<string, object> properties)
    {
        var namedId = new Guid("71604b0f-97b0-4764-8577-2f13e98a1422");
        var persistId = new Guid("e318ad57-0aa0-450f-aca5-6fab7103d917");
        Marshal.ThrowExceptionForHR(PSCreateMemoryPropertyStore(in namedId, out var named));
        var variant = Marshal.AllocCoTaskMem(24);
        IntPtr persist = IntPtr.Zero, storage = IntPtr.Zero;
        try
        {
            var set = Marshal.GetDelegateForFunctionPointer<SetNamed>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(named), 4 * IntPtr.Size));
            foreach (var (name, value) in properties)
            {
                Marshal.Copy(new byte[24], 0, variant, 24);
                switch (value)
                {
                    case uint number: Marshal.WriteInt16(variant, 19); Marshal.WriteInt32(variant, 8, unchecked((int)number)); break;
                    case bool boolean: Marshal.WriteInt16(variant, 11); Marshal.WriteInt16(variant, 8, (short)(boolean ? -1 : 0)); break;
                    case string text: Marshal.WriteInt16(variant, 8); Marshal.WriteIntPtr(variant, 8, Marshal.StringToBSTR(text)); break;
                    case byte[] bytes:
                        Marshal.WriteInt16(variant, 66);
                        var stream = SHCreateMemStream(bytes, (uint)bytes.Length);
                        if (stream == IntPtr.Zero) throw new Exception("stream allocation failed");
                        Marshal.WriteIntPtr(variant, 8, stream); break;
                    default: throw new ArgumentException("unsupported fixture value");
                }
                try { Marshal.ThrowExceptionForHR(set(named, name, variant)); }
                finally { PropVariantClear(variant); }
            }
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(named, in persistId, out persist));
            var get = Marshal.GetDelegateForFunctionPointer<GetStorage>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(persist), 5 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(get(persist, out storage, out var count));
            var envelope = new byte[52 + count];
            Write(envelope, 0, 28); Write(envelope, 28, unchecked((int)0xf1f1f1f1));
            Write(envelope, 32, 20); Write(envelope, 48, (int)count);
            Marshal.Copy(storage, envelope, 52, (int)count);
            return string.Concat(envelope.Select(value => "%" + value.ToString("X2"))) + "|CODEMODE1|0|" + envelope.Length;
        }
        finally
        {
            if (storage != IntPtr.Zero) Marshal.FreeCoTaskMem(storage);
            if (persist != IntPtr.Zero) Marshal.Release(persist);
            Marshal.FreeCoTaskMem(variant); Marshal.Release(named);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetNamed(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetStorage(IntPtr self, out IntPtr data, out uint length);
    [DllImport("propsys.dll")] private static extern int PSCreateMemoryPropertyStore(in Guid id, out IntPtr result);
    [DllImport("shlwapi.dll")] private static extern IntPtr SHCreateMemStream(byte[] data, uint length);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(IntPtr variant);
}
