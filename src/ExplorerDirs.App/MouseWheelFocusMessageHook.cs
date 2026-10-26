using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ExplorerDirs.App;

// ComponentDispatcher only supports keyboard messages. This thread-local
// GetMessage hook sees the posted wheel message before the UI thread dispatches it.
internal static class MouseWheelFocusMessageHook
{
    private const int WhGetMessage = 3;
    private const uint WmMouseWheel = 0x020A;
    private static readonly HashSet<MainWindow> Windows = [];
    private static readonly HookProcedure Procedure = HookCallback;
    private static nint _hook;
    private static bool _enabled;

    internal static void Register(MainWindow window, bool enabled)
    {
        if (!Windows.Add(window)) return;
        try { SetEnabled(enabled); }
        catch
        {
            Windows.Remove(window);
            throw;
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        if (enabled && _hook == 0)
        {
            _hook = SetWindowsHookExW(WhGetMessage, Procedure, 0, GetCurrentThreadId());
            if (_hook == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "无法启用鼠标悬停滚轮路由。");
        }

        _enabled = enabled;
        foreach (var window in Windows)
            window.SetMouseWheelFocusEnabled(enabled);

        if (!enabled && _hook != 0 && UnhookWindowsHookEx(_hook)) _hook = 0;
    }

    internal static void Unregister(MainWindow window)
    {
        Windows.Remove(window);
        window.SetMouseWheelFocusEnabled(false);
        if (Windows.Count == 0) SetEnabled(false);
    }

    private static nint HookCallback(int code, nint retrievalMode, nint messagePointer)
    {
        try
        {
            if (_enabled &&
                MouseWheelFocusRouter.ShouldProcessGetMessage(code, unchecked((nuint)retrievalMode.ToInt64())) &&
                messagePointer != 0)
            {
                var message = Marshal.PtrToStructure<MSG>(messagePointer);
                if (unchecked((uint)message.message) == WmMouseWheel)
                {
                    var point = MouseWheelFocusRouter.GetScreenPoint(message.lParam);
                    var hovered = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
                    foreach (var window in Windows.ToArray())
                    {
                        if (!window.TryRetargetMouseWheel(ref message, hovered)) continue;
                        Marshal.StructureToPtr(message, messagePointer, false);
                        break;
                    }
                }
            }
        }
        catch
        {
            // Never let managed exceptions cross the native hook callback boundary.
        }

        return CallNextHookEx(_hook, code, retrievalMode, messagePointer);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hookId, HookProcedure callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);
}
