using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ExplorerDirs.App;

// Only the CODEMODE1 envelope and stream layouts observed in Q-Dir 12.79 are supported.
// Windows owns the serialized property storage format; read it through propsys.
internal static class QdrViewSettings
{
    internal static bool TryRead(string text, out ViewState view)
    {
        view = ViewState.Default();
        if (text.Length > 200000) return false;
        var fields = text.Split('|');
        if (fields.Length != 4 || fields[1] != "CODEMODE1" ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length is < 60 or > 65536 || fields[0].Length != length * 3) return false;
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            if (fields[0][i * 3] != '%' || !byte.TryParse(fields[0].AsSpan(i * 3 + 1, 2),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return false;
        if (U32(bytes, 0) != 28 || U32(bytes, 28) != 0xf1f1f1f1 || U32(bytes, 32) != 20 ||
            U32(bytes, 48) != length - 52) return false;
        foreach (var offset in new[] { 4, 8, 12, 16, 20, 24, 36, 40, 44 })
            if (U32(bytes, offset) != 0) return false;
        // The signed field preceding the byte count is opaque, not a verified checksum.
        using var properties = new Properties(bytes.AsSpan(52).ToArray());
        try
        {
            var mode = properties.UInt("Mode");
            var size = unchecked((int)properties.UInt("IconSize"));
            var grouped = properties.Bool("GroupView");
            var direction = unchecked((int)properties.UInt("GroupByDirection"));
            if (mode is < 1 or > 8 || size is < -1 or > 1024 || direction is not (1 or -1)) return false;
            if (!Guid.TryParse(properties.Text("GroupByKey:FMTID"), out var groupId)) return false;
            var groupPid = properties.UInt("GroupByKey:PID");
            if (grouped && groupId == Guid.Empty) return false;
            var sort = ReadSort(properties.Stream("Sort"));
            var columns = ReadColumns(properties.Stream("ColInfo"));
            view = new ViewState(mode, size, sort,
                grouped ? new PropertyKeyState(groupId, groupPid) : null, direction == 1, columns);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or COMException or InvalidCastException or ArgumentException)
        { return false; }
    }

    private static IReadOnlyList<SortColumnState> ReadSort(byte[] bytes)
    {
        if (bytes.Length < 4 || U32(bytes, 0) > 16 || bytes.Length != 4 + U32(bytes, 0) * 24)
            throw new InvalidDataException();
        var result = new List<SortColumnState>();
        for (var offset = 4; offset < bytes.Length; offset += 24)
        {
            var key = Key(bytes, offset);
            var direction = unchecked((int)U32(bytes, offset + 20));
            if (key.FormatId == Guid.Empty || direction is not (1 or -1) || result.Any(item => item.Key == key))
                throw new InvalidDataException();
            result.Add(new SortColumnState(key, direction));
        }
        return result;
    }

    private static IReadOnlyList<DisplayColumnState> ReadColumns(byte[] bytes)
    {
        if (bytes.Length < 24 || U32(bytes, 0) != 0xfddfdffd || U32(bytes, 4) != 16 ||
            U32(bytes, 8) != 0 || U32(bytes, 12) != 0 || U32(bytes, 16) > ColumnLayout.MaximumColumns ||
            U32(bytes, 20) != 24 || bytes.Length != 24 + U32(bytes, 16) * 24)
            throw new InvalidDataException();
        var result = new List<DisplayColumnState>();
        for (var offset = 24; offset < bytes.Length; offset += 24)
            result.Add(new DisplayColumnState(Key(bytes, offset), unchecked((int)U32(bytes, offset + 20))));
        if (!ColumnLayout.IsValid(result)) throw new InvalidDataException();
        return result;
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static PropertyKeyState Key(byte[] bytes, int offset) =>
        new(new Guid(bytes.AsSpan(offset, 16)), U32(bytes, offset + 16));

    private sealed class Properties : IDisposable
    {
        private readonly IntPtr storage;
        private readonly uint length;
        public Properties(byte[] bytes)
        {
            length = (uint)bytes.Length;
            storage = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, storage, bytes.Length);
        }
        public uint UInt(string name) => Read(name, 19, value => unchecked((uint)Marshal.ReadInt32(value, 8)));
        public bool Bool(string name) => Read(name, 11, value => Marshal.ReadInt16(value, 8) switch
        { 0 => false, -1 => true, _ => throw new InvalidDataException() });
        public string Text(string name) => Read(name, 8, value =>
        {
            var pointer = Marshal.ReadIntPtr(value, 8);
            return pointer != IntPtr.Zero ? Marshal.PtrToStringBSTR(pointer) : throw new InvalidDataException();
        });
        public byte[] Stream(string name) => Read(name, 66, value =>
        {
            var pointer = Marshal.ReadIntPtr(value, 8);
            if (pointer == IntPtr.Zero) throw new InvalidDataException();
            var instance = Marshal.GetObjectForIUnknown(pointer);
            var count = Marshal.AllocCoTaskMem(4);
            try
            {
                var stream = (IStream)instance;
                stream.Stat(out var stat, 1);
                if (stat.cbSize is < 0 or > 65536) throw new InvalidDataException();
                var bytes = new byte[(int)stat.cbSize];
                stream.Seek(0, 0, IntPtr.Zero);
                Marshal.WriteInt32(count, 0);
                stream.Read(bytes, bytes.Length, count);
                if (Marshal.ReadInt32(count) != bytes.Length) throw new InvalidDataException();
                return bytes;
            }
            finally { Marshal.FreeCoTaskMem(count); Marshal.ReleaseComObject(instance); }
        });
        private T Read<T>(string name, short type, Func<IntPtr, T> get)
        {
            var variant = Marshal.AllocCoTaskMem(24);
            Marshal.Copy(new byte[24], 0, variant, 24);
            try
            {
                Marshal.ThrowExceptionForHR(PSGetNamedPropertyFromPropertyStorage(storage, length, name, variant));
                if (Marshal.ReadInt16(variant) != type) throw new InvalidDataException();
                return get(variant);
            }
            finally { PropVariantClear(variant); Marshal.FreeCoTaskMem(variant); }
        }
        public void Dispose() => Marshal.FreeCoTaskMem(storage);
    }

    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    private static extern int PSGetNamedPropertyFromPropertyStorage(IntPtr storage, uint length, string name, IntPtr value);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(IntPtr variant);
}
