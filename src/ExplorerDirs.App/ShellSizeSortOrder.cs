using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ExplorerDirs.App;

// Verify the native row order using raw bytes, never formatted size strings.
// A successful direction command alone does not mean DefView finished its sort.
internal static class ShellSizeSortOrder
{
    internal static async Task<bool?> IsOrderedAsync(nint view, int expectedCount, int direction, bool mixed, Func<bool> isCurrent)
        => await ReadRowsAsync(view, expectedCount, direction, mixed, isCurrent, true);

    internal static Task<bool?> AreValuesReadyAsync(nint view, int expectedCount, Func<bool> isCurrent, bool allowUnavailable = false)
        => ReadRowsAsync(view, expectedCount, 1, false, isCurrent, false, allowUnavailable);

    private static async Task<bool?> ReadRowsAsync(nint view, int expectedCount, int direction, bool mixed,
        Func<bool> isCurrent, bool checkOrder, bool allowUnavailable = false)
    {
        if (!isCurrent()) return null;
        var folderId = new Guid("93f2f68c-1d1b-11d3-a30e-00c04f79abd1");
        var result = At<GetFolder>(view, 5)(view, in folderId, out var folder);
        if (folder == 0) return null;
        try
        {
            if (result < 0 || !isCurrent() || At<GetCount>(view, 7)(view, 2, out var count) < 0 ||
                !isCurrent() || count != expectedCount) return null;
            var pending = false;
            ulong previous = 0;
            var previousFolder = true;
            for (var index = 0; index < count; index++)
            {
                if (!isCurrent()) return null;
                result = At<GetItem>(view, 6)(view, index, out var item);
                if (item == 0) return null;
                try
                {
                    if (result < 0) return null;
                    var key = new NativeKey { FormatId = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), Id = 12 };
                    result = At<GetDetails>(folder, 17)(folder, item, in key, out var value);
                    ulong bytes;
                    try
                    {
                        if (!isCurrent()) return null;
                        if (result < 0 || value.Type != 21)
                        {
                            if (checkOrder) return null;
                            var terminalUnavailable = false;
                            if (allowUnavailable)
                            {
                                var statusKey = new NativeKey { FormatId = new Guid("8c5edff0-4438-4c0c-af50-a2f697f16890"), Id = 1 };
                                var statusResult = At<GetDetails>(folder, 17)(folder, item, in statusKey, out var status);
                                try { terminalUnavailable = statusResult >= 0 && status.Type == 19 && unchecked((uint)status.Bytes) == 2; } // VT_UI4
                                finally { VariantClear(ref status); }
                                if (!isCurrent()) return null;
                            }
                            pending |= !terminalUnavailable;
                        } // VT_UI8; keep reading to queue every folder.
                        bytes = value.Bytes;
                    }
                    finally { VariantClear(ref value); }
                    if (checkOrder)
                    {
                        uint attributes = 0x20000000; // SFGAO_FOLDER
                        if (!isCurrent() || At<GetAttributes>(folder, 9)(folder, 1, ref item, ref attributes) < 0 ||
                            !isCurrent()) return null;
                        var isFolder = (attributes & 0x20000000) != 0;
                        if (index > 0)
                        {
                            if (!mixed && !previousFolder && isFolder) return false;
                            if ((mixed || previousFolder == isFolder) &&
                                (direction > 0 ? previous > bytes : previous < bytes)) return false;
                        }
                        previous = bytes;
                        previousFolder = isFolder;
                    }
                }
                finally { Marshal.FreeCoTaskMem(item); }
                // Large directories must leave time for navigation and input between native reads.
                if ((index + 1) % ShellFileViewExportCapture.ReadinessBatchSize == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
            return isCurrent() && At<GetCount>(view, 7)(view, 2, out var afterCount) >= 0 &&
                isCurrent() && afterCount == expectedCount && !pending ? true : null;
        }
        finally { Marshal.Release(folder); }
    }

    private static T At<T>(nint self, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKey { public Guid FormatId; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct NativeVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public ulong Bytes;
    }
    [DllImport("oleaut32.dll")] private static extern int VariantClear(ref NativeVariant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFolder(nint self, in Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItem(nint self, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDetails(nint self, nint item, in NativeKey key, out NativeVariant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAttributes(nint self, uint count, ref nint item, ref uint attributes);
}
