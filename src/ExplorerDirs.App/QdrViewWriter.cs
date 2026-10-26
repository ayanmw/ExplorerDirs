using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Writes the observed 12.79 view envelope using Windows property serialization.
// Unknown column layouts stay in the application extension, never fabricated.
internal static class QdrViewWriter
{
    internal static string? Write(ViewState view)
    {
        if (view.DisplayColumns is null) return null;
        if (!ColumnLayout.IsValid(view.DisplayColumns) || view.Mode is < 1 or > 8 ||
            view.IconSize is < -1 or > 1024 || view.SortColumns.Count > 16 ||
            view.SortColumns.Any(sort => sort.Key.FormatId == Guid.Empty || sort.Direction is not (1 or -1)) ||
            view.SortColumns.Select(sort => sort.Key).Distinct().Count() != view.SortColumns.Count ||
            view.GroupKey?.FormatId == Guid.Empty)
            throw new InvalidDataException("标签视图不能写入已支持的 Q-Dir 格式。");

        var sortBytes = new byte[4 + view.SortColumns.Count * 24];
        Put(sortBytes, 0, view.SortColumns.Count);
        for (var i = 0; i < view.SortColumns.Count; i++)
        {
            PutKey(sortBytes, 4 + i * 24, view.SortColumns[i].Key);
            Put(sortBytes, 24 + i * 24, view.SortColumns[i].Direction);
        }
        var columnBytes = new byte[24 + view.DisplayColumns.Count * 24];
        Put(columnBytes, 0, unchecked((int)0xfddfdffd));
        Put(columnBytes, 4, 16);
        Put(columnBytes, 16, view.DisplayColumns.Count);
        Put(columnBytes, 20, 24);
        for (var i = 0; i < view.DisplayColumns.Count; i++)
        {
            PutKey(columnBytes, 24 + i * 24, view.DisplayColumns[i].Key);
            Put(columnBytes, 44 + i * 24, view.DisplayColumns[i].Width);
        }
        var group = view.GroupKey ?? new PropertyKeyState(Guid.Empty, 0);
        var properties = new Dictionary<string, object>
        {
            ["Mode"] = view.Mode,
            // Native DefView otherwise restores icon modes at its default size
            // even when IconSize is present. Values are FOLDERLOGICALVIEWMODE.
            ["LogicalViewMode"] = view.Mode switch { 3 => 4u, 4 => 1u, 6 => 2u, 8 => 5u, _ => 3u },
            ["IconSize"] = unchecked((uint)view.IconSize),
            ["GroupView"] = view.GroupKey is not null,
            ["GroupByKey:FMTID"] = group.FormatId.ToString("B"),
            ["GroupByKey:PID"] = group.PropertyId,
            ["GroupByDirection"] = view.GroupAscending ? 1u : uint.MaxValue,
            ["Sort"] = sortBytes,
            ["ColInfo"] = columnBytes
        };
        var bytes = Serialize(properties);
        return string.Concat(bytes.Select(value => "%" + value.ToString("X2", CultureInfo.InvariantCulture))) +
            "|CODEMODE1|" + QdrViewSettings.ComputeChecksum(bytes).ToString(CultureInfo.InvariantCulture) +
            "|" + bytes.Length.ToString(CultureInfo.InvariantCulture);
    }

    private static byte[] Serialize(Dictionary<string, object> properties)
    {
        var namedId = new Guid("71604b0f-97b0-4764-8577-2f13e98a1422");
        var persistId = new Guid("e318ad57-0aa0-450f-aca5-6fab7103d917");
        nint named = 0, persist = 0, storage = 0, variant = 0;
        try
        {
            Marshal.ThrowExceptionForHR(PSCreateMemoryPropertyStore(in namedId, out named));
            if (named == 0) throw new InvalidDataException("无法建立视图属性存储。");
            variant = Marshal.AllocCoTaskMem(24);
            var set = Method<SetNamed>(named, 4);
            foreach (var (name, value) in properties)
            {
                Marshal.Copy(new byte[24], 0, variant, 24);
                try
                {
                    switch (value)
                    {
                        case uint number:
                            Marshal.WriteInt16(variant, 19); // VT_UI4
                            Marshal.WriteInt32(variant, 8, unchecked((int)number));
                            break;
                        case bool boolean:
                            Marshal.WriteInt16(variant, 11); // VT_BOOL
                            Marshal.WriteInt16(variant, 8, (short)(boolean ? -1 : 0));
                            break;
                        case string text:
                            Marshal.WriteIntPtr(variant, 8, Marshal.StringToBSTR(text));
                            Marshal.WriteInt16(variant, 8); // VT_BSTR
                            break;
                        case byte[] data:
                            var stream = SHCreateMemStream(data, (uint)data.Length);
                            if (stream == 0) throw new OutOfMemoryException("无法建立视图数据流。");
                            Marshal.WriteIntPtr(variant, 8, stream);
                            Marshal.WriteInt16(variant, 66); // VT_STREAM, ownership passes to PropVariantClear
                            break;
                        default: throw new InvalidDataException("不支持的视图属性类型。");
                    }
                    Marshal.ThrowExceptionForHR(set(named, name, variant));
                }
                finally { PropVariantClear(variant); }
            }
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(named, in persistId, out persist));
            if (persist == 0) throw new InvalidDataException("无法序列化视图属性。");
            Marshal.ThrowExceptionForHR(Method<GetStorage>(persist, 5)(persist, out storage, out var count));
            if (storage == 0 || count is < 8 or > 65536 - 52)
                throw new InvalidDataException("Q-Dir 视图数据长度无效或超限。");
            var envelope = new byte[52 + count];
            Put(envelope, 0, 28);
            Put(envelope, 28, unchecked((int)0xf1f1f1f1));
            Put(envelope, 32, 20);
            Put(envelope, 48, (int)count);
            Marshal.Copy(storage, envelope, 52, (int)count);
            return envelope;
        }
        finally
        {
            if (storage != 0) Marshal.FreeCoTaskMem(storage);
            if (persist != 0) Marshal.Release(persist);
            if (variant != 0) Marshal.FreeCoTaskMem(variant);
            if (named != 0) Marshal.Release(named);
        }
    }

    private static void Put(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void PutKey(byte[] bytes, int offset, PropertyKeyState key)
    {
        key.FormatId.TryWriteBytes(bytes.AsSpan(offset, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 16), key.PropertyId);
    }
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int SetNamed(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetStorage(nint self, out nint data, out uint length);
    [DllImport("propsys.dll")] private static extern int PSCreateMemoryPropertyStore(in Guid id, out nint result);
    [DllImport("shlwapi.dll")] private static extern nint SHCreateMemStream(byte[] data, uint length);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(nint variant);
}
