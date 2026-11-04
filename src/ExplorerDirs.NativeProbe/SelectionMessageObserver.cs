using System.Runtime.InteropServices;

// Probe-only observer. Keep native callbacks free of console IO and Shell calls;
// record bounded numeric messages to distinguish selection mutation from repaint.
internal sealed class SelectionMessageObserver : IDisposable
{
    private readonly nint _window;
    private readonly nint _parent;
    private readonly Callback _callback;
    private readonly List<string> _messages = [];
    private int _overflow;

    internal SelectionMessageObserver(nint window)
    {
        _window = window;
        _callback = Observe;
        if (!SetWindowSubclass(window, _callback, 18271, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        _parent = GetParent(window);
        if (!SetWindowSubclass(_parent, _callback, 18271, 0))
        {
            _ = RemoveWindowSubclass(window, _callback, 18271);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private nint Observe(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if (window == _parent && message == 0x4E && lParam != 0 && Marshal.ReadIntPtr(lParam) == _window)
        {
            var code = Marshal.ReadInt32(lParam, IntPtr.Size * 2);
            if (code == -101) // LVN_ITEMCHANGED, NMLISTVIEW starts after aligned NMHDR.
            {
                var offset = IntPtr.Size == 8 ? 24 : 12;
                if (_messages.Count < 128)
                    _messages.Add($"item-changed;row={Marshal.ReadInt32(lParam, offset)};new=0x{Marshal.ReadInt32(lParam, offset + 8):X};old=0x{Marshal.ReadInt32(lParam, offset + 12):X};changed=0x{Marshal.ReadInt32(lParam, offset + 16):X}");
                else _overflow++;
            }
        }
        if (window == _window && (message is 0x102B or 0x104C or 0x104D or 0x1008 or 0x1009))
        {
            if (_messages.Count < 128)
            {
                // LVITEM's first five fields are 32-bit even on x64.
                var state = lParam != 0 && (message is 0x102B or 0x104C or 0x104D)
                    ? $";state=0x{Marshal.ReadInt32(lParam, 12):X};mask=0x{Marshal.ReadInt32(lParam, 16):X}" : "";
                _messages.Add($"msg=0x{message:X};row={wParam}{state}");
            }
            else _overflow++;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    internal void Drain(string stage)
    {
        foreach (var value in _messages) Console.WriteLine($"selection-message: stage={stage};{value}");
        if (_overflow != 0) Console.WriteLine($"selection-message: stage={stage};overflow={_overflow}");
        _messages.Clear();
        _overflow = 0;
    }

    public void Dispose()
    {
        if (IsWindow(_window)) _ = RemoveWindowSubclass(_window, _callback, 18271);
        if (IsWindow(_parent)) _ = RemoveWindowSubclass(_parent, _callback, 18271);
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint Callback(nint window, uint message, nint wParam, nint lParam, nuint id, nint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, Callback callback, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Callback callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
}
