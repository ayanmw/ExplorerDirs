using System.Runtime.InteropServices;

// Isolated probe: observe the Shell's own arrow updates without querying its view
// during a comparison or identity callback. The publisher only stores atomic policy.
internal sealed class SizeSortHeaderObserver : IDisposable
{
    private readonly nint _header;
    private readonly int _sizeColumn;
    private readonly Func<int, int> _publish;
    private readonly Callback _callback;
    internal int Updates { get; private set; }
    internal int LastResult { get; private set; }

    internal SizeSortHeaderObserver(nint header, int sizeColumn, Func<int, int> publish)
    {
        _header = header;
        _sizeColumn = sizeColumn;
        _publish = publish;
        _callback = Observe;
        if (header == 0 || !SetWindowSubclass(header, _callback, 18272, 0))
            throw new InvalidOperationException("Could not observe the native size column header.");
    }

    private nint Observe(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if ((message is 0x1204 or 0x120C) && wParam.ToInt32() == _sizeColumn && lParam != 0 &&
            (Marshal.ReadInt32(lParam) & 4) != 0) // HDM_SETITEM A/W, HDI_FORMAT.
        {
            var format = Marshal.ReadInt32(lParam, IntPtr.Size == 8 ? 28 : 20);
            if ((format & 0x600) is 0x200 or 0x400) // HDF_SORTDOWN / HDF_SORTUP.
            {
                LastResult = _publish((format & 0x400) != 0 ? 1 : -1);
                Updates++;
            }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (IsWindow(_header)) _ = RemoveWindowSubclass(_header, _callback, 18272);
        GC.KeepAlive(_callback);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint Callback(nint window, uint message, nint wParam, nint lParam, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, Callback callback, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Callback callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
}
