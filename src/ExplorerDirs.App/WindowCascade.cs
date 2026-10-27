using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// New windows use screen pixels throughout. Saved WINDOWPLACEMENT workspace
// coordinates belong only to Get/SetWindowPlacement, never SetWindowPos.
internal static class WindowCascade
{
    internal static (int Left, int Top) Calculate(Rect source, Rect work, int width, int height, int offset) =>
        ((int)Math.Clamp((long)source.Left + offset, work.Left, Math.Max((long)work.Left, (long)work.Right - width)),
         (int)Math.Clamp((long)source.Top + offset, work.Top, Math.Max((long)work.Top, (long)work.Bottom - height)));

    internal static void Place(nint window, nint sourceWindow)
    {
        var monitor = MonitorFromWindow(sourceWindow, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetWindowRect(sourceWindow, out var source))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        // Iconic window rectangles are not their restored screen positions.
        // Place beside the same monitor's work-area origin without converting
        // WINDOWPLACEMENT coordinates or restoring/activating the source.
        if (IsIconic(sourceWindow)) source = info.Work;
        var dpi = GetDpiForWindow(sourceWindow);
        var offset = (int)Math.Round(32 * (dpi == 0 ? 96 : dpi) / 96d);
        Move();
        // A per-monitor DPI transition can synchronously resize the WPF window.
        // Constrain once more using its actual resulting size, without resizing it.
        Move();

        void Move()
        {
            if (!GetWindowRect(window, out var current))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var position = Calculate(source, info.Work, current.Right - current.Left, current.Bottom - current.Top, offset);
            if (current.Left == position.Left && current.Top == position.Top) return;
            if (!SetWindowPos(window, 0, position.Left, position.Top, 0, 0, 0x0015)) // NOSIZE | NOZORDER | NOACTIVATE
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
