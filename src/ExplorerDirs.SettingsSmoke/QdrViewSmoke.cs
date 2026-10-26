using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ExplorerDirs.App;

internal static class QdrViewSmoke
{
    public static void Run()
    {
        foreach (var (bytes, expected) in new (byte[], int)[]
        {
            ([], 0), ([1, 0, 2], 4), ([128], -128), ([255, 2, 0, 127], 127),
            ([127, 127, 127, 127, 127, 127, 127, 127], -607912448),
            ([1, 128, 255, 0, 2], 512)
        })
            Require(QdrViewSettings.ComputeChecksum(bytes) == expected,
                "native checksum uses signed bytes, skips zero multiplication and wraps at 32 bits");
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
        var checksumParts = serialized.Split('|');
        checksumParts[2] = unchecked(int.Parse(checksumParts[2],
            System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        Reject(string.Join("|", checksumParts));
        var imported = QdrImporter.Parse("Q-Dir Version=12.79\nViewMode=0\nDir1=C:\\fixture\nColsAndSort1=" + serialized,
            (value, _) => value).Panes[0][0].View;
        Require(imported.Mode == 4 && imported.IconSize == 48 && imported.SortColumns.Count == 1 &&
            imported.SortColumns[0] == new SortColumnState(key, -1) && imported.GroupKey == key &&
            !imported.GroupAscending && imported.DisplayColumns is { Count: 1 } && imported.DisplayColumns[0].Width == 321,
            "QDR native property storage imports icon size, sorting, grouping and column widths");
        var written = QdrViewWriter.Write(imported)!;
        VerifyNativeProperties(written, new(properties) { ["LogicalViewMode"] = 1u });
        Require(QdrViewWriter.Write(ViewState.Default()) is null,
            "uncaptured column layouts are not fabricated for native export");
        for (uint mode = 1; mode <= 8; mode++)
        {
            var alternateKey = new PropertyKeyState(key.FormatId, uint.MaxValue);
            var sample = imported with
            {
                Mode = mode, IconSize = mode == 1 ? -1 : 16 * (int)mode,
                GroupKey = mode % 2 == 0 ? key : null, GroupAscending = mode % 3 == 0,
                SortColumns = [new(alternateKey, 1), new(key, -1)],
                DisplayColumns = [new(alternateKey, -2), new(key, 65535)]
            };
            Require(QdrViewSettings.TryRead(QdrViewWriter.Write(sample)!, out var read) &&
                read.Mode == sample.Mode && read.IconSize == sample.IconSize &&
                read.GroupKey == sample.GroupKey && read.GroupAscending == sample.GroupAscending &&
                read.SortColumns.SequenceEqual(sample.SortColumns) &&
                read.DisplayColumns!.SequenceEqual(sample.DisplayColumns),
                "native export preserves modes, grouped/ungrouped direction, multiple keys and column boundaries");
            uint[] logicalModes = [3, 3, 4, 1, 3, 2, 3, 5];
            VerifyNativeProperties(QdrViewWriter.Write(sample)!,
                new() { ["LogicalViewMode"] = logicalModes[mode - 1] });
        }
        var maximumColumns = Enumerable.Range(1, ColumnLayout.MaximumColumns)
            .Select(i => new DisplayColumnState(new PropertyKeyState(key.FormatId, (uint)i), 120)).ToArray();
        var largeView = imported with { DisplayColumns = maximumColumns, SortColumns = [] };
        var largeWritten = QdrViewWriter.Write(largeView)!;
        Require(int.Parse(largeWritten.Split('|')[3], System.Globalization.CultureInfo.InvariantCulture) > 4096 &&
            QdrViewSettings.TryRead(largeWritten, out var largeRead) &&
            largeRead.DisplayColumns!.SequenceEqual(maximumColumns) && largeRead.SortColumns.Count == 0,
            "maximum column layout is serialized whole, never truncated to the native writer's observed 4KB cap");
        var largeColumnStream = new byte[24 + 24 * maximumColumns.Length];
        Write(largeColumnStream, 0, unchecked((int)0xfddfdffd));
        Write(largeColumnStream, 4, 16); Write(largeColumnStream, 16, maximumColumns.Length);
        Write(largeColumnStream, 20, 24);
        for (var i = 0; i < maximumColumns.Length; i++)
        {
            key.FormatId.TryWriteBytes(largeColumnStream.AsSpan(24 + i * 24));
            Write(largeColumnStream, 40 + i * 24, i + 1);
            Write(largeColumnStream, 44 + i * 24, 120);
        }
        VerifyNativeProperties(largeWritten, new(properties)
        { ["Sort"] = new byte[4], ["ColInfo"] = largeColumnStream });
        foreach (var bad in new[]
        {
            imported with { Mode = 9 }, imported with { IconSize = 1025 },
            imported with { GroupKey = new PropertyKeyState(Guid.Empty, 1) },
            imported with { SortColumns = [new(key, 0)] },
            imported with { SortColumns = [new(key, 1), new(key, -1)] },
            imported with { DisplayColumns = [new(key, -3)] },
            imported with { DisplayColumns = [] }
        })
        {
            try { QdrViewWriter.Write(bad); throw new Exception("invalid native export accepted"); }
            catch (System.IO.InvalidDataException) { }
        }
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

    // Read the produced fields directly through Windows, without our importer.
    private static void VerifyNativeProperties(string text, Dictionary<string, object> expected)
    {
        var fields = text.Split('|');
        Require(fields.Length == 4 && fields[1] == "CODEMODE1", "writer emits the native envelope marker");
        var bytes = Convert.FromHexString(fields[0].Replace("%", ""));
        Require(bytes.Length == int.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture) &&
            BinaryPrimitives.ReadInt32LittleEndian(bytes) == 28 &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)) == 0xf1f1f1f1 &&
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(32)) == 20 &&
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(48)) == bytes.Length - 52,
            "writer emits observed native header and matching lengths");
        long checksum = 0;
        foreach (var b in bytes)
        {
            var signed = b < 128 ? b : b - 256;
            checksum = ((signed == 0 ? checksum : checksum * signed) + signed) & uint.MaxValue;
        }
        Require(unchecked((int)checksum) == int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
            "exported checksum independently matches complete envelope");
        var storage = Marshal.AllocCoTaskMem(bytes.Length - 52);
        var variant = Marshal.AllocCoTaskMem(24);
        try
        {
            Marshal.Copy(bytes, 52, storage, bytes.Length - 52);
            foreach (var (name, value) in expected)
            {
                Marshal.Copy(new byte[24], 0, variant, 24);
                try
                {
                    Marshal.ThrowExceptionForHR(PSGetNamedPropertyFromPropertyStorage(storage,
                        (uint)bytes.Length - 52, name, variant));
                    var type = Marshal.ReadInt16(variant);
                    var matches = value switch
                    {
                        uint number => type == 19 && unchecked((uint)Marshal.ReadInt32(variant, 8)) == number,
                        bool boolean => type == 11 && Marshal.ReadInt16(variant, 8) == (boolean ? -1 : 0),
                        string s => type == 8 && Marshal.PtrToStringBSTR(Marshal.ReadIntPtr(variant, 8)) == s,
                        byte[] data => type == 66 && NativeStreamBytes(Marshal.ReadIntPtr(variant, 8)).SequenceEqual(data),
                        _ => false
                    };
                    Require(matches, "Windows property reader verifies exported " + name);
                }
                finally { PropVariantClear(variant); }
            }
        }
        finally { Marshal.FreeCoTaskMem(variant); Marshal.FreeCoTaskMem(storage); }
    }
    private static byte[] NativeStreamBytes(nint pointer)
    {
        var stream = (IStream)Marshal.GetObjectForIUnknown(pointer);
        var count = Marshal.AllocCoTaskMem(4);
        try
        {
            stream.Stat(out var stat, 1);
            Require(stat.cbSize is >= 0 and < 65536, "exported property stream is bounded");
            var data = new byte[(int)stat.cbSize];
            stream.Seek(0, 0, 0);
            Marshal.WriteInt32(count, 0);
            stream.Read(data, data.Length, count);
            Require(Marshal.ReadInt32(count) == data.Length, "Windows returns the complete exported stream");
            return data;
        }
        finally { Marshal.FreeCoTaskMem(count); Marshal.ReleaseComObject(stream); }
    }

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
            // Fixture implementation uses Int64 plus an explicit 32-bit mask,
            // independently of the production decoder's integer arithmetic.
            long checksum = 0;
            foreach (var value in envelope)
            {
                var signed = value < 128 ? value : value - 256;
                checksum = ((signed == 0 ? checksum : checksum * signed) + signed) & uint.MaxValue;
            }
            var signedChecksum = checksum <= int.MaxValue ? checksum : checksum - 0x100000000L;
            return string.Concat(envelope.Select(value => "%" + value.ToString("X2"))) +
                "|CODEMODE1|" + signedChecksum.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "|" + envelope.Length;
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
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    private static extern int PSGetNamedPropertyFromPropertyStorage(nint storage, uint length, string name, nint value);
    [DllImport("shlwapi.dll")] private static extern IntPtr SHCreateMemStream(byte[] data, uint length);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(IntPtr variant);
}
