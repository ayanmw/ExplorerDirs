using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Coordinates stay in the native WINDOWPLACEMENT workspace coordinate system,
// including negative monitor coordinates. Do not restore these using WPF DIP bounds.
internal sealed record WindowPlacementState(int Left, int Top, int Right, int Bottom, bool Maximized)
{
    internal bool IsValid => (long)Right - Left is > 0 and <= int.MaxValue &&
        (long)Bottom - Top is > 0 and <= int.MaxValue;
}

internal static class NativeWindowPlacement
{
    internal static WindowPlacementState Capture(nint window, bool lastNonMinimizedMaximized)
    {
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(window, ref placement)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return FromNative(placement, lastNonMinimizedMaximized);
    }

    internal static void Restore(nint window, WindowPlacementState state)
    {
        var placement = ToNative(state);
        // Windows also relocates a completely off-screen saved position when the
        // monitor configuration has changed. Keep Get/SetWindowPlacement paired.
        if (!SetWindowPlacement(window, in placement)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static WindowPlacementState FromNative(Placement placement, bool lastNonMinimizedMaximized)
    {
        var rect = placement.NormalPosition;
        var maximized = placement.ShowCommand == 3 ||
            (placement.ShowCommand == 2 && (lastNonMinimizedMaximized || (placement.Flags & 2) != 0));
        var state = new WindowPlacementState(rect.Left, rect.Top, rect.Right, rect.Bottom, maximized);
        if (!state.IsValid) throw new InvalidOperationException("窗口还原位置无效，已保留之前的配置。");
        return state;
    }

    internal static Placement ToNative(WindowPlacementState state)
    {
        if (!state.IsValid) throw new ArgumentOutOfRangeException(nameof(state));
        return new Placement
        {
            Length = Marshal.SizeOf<Placement>(),
            ShowCommand = state.Maximized ? 3 : 1, // Never reopen minimized.
            MinPosition = new Point { X = -1, Y = -1 },
            MaxPosition = new Point { X = -1, Y = -1 },
            NormalPosition = new Rect { Left = state.Left, Top = state.Top, Right = state.Right, Bottom = state.Bottom }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Placement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref Placement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint window, in Placement placement);
}
