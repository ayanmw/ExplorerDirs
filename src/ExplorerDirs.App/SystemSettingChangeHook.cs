using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ExplorerDirs.App;

internal sealed class SystemSettingChangeHook : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private readonly HwndSource _source;
    private readonly Action _refresh;
    private readonly Action? _shellSettingsChanged;

    public SystemSettingChangeHook(HwndSource source, Action refresh, Action? shellSettingsChanged = null)
    {
        _source = source;
        _refresh = refresh;
        _shellSettingsChanged = shellSettingsChanged;
        _source.AddHook(WindowHook);
    }

    public void Dispose() => _source.RemoveHook(WindowHook);

    internal static bool TryScheduleRefresh(Action schedule)
    {
        try
        {
            schedule();
            return true;
        }
        catch (InvalidOperationException)
        {
            // Dispatcher.BeginInvoke can race window shutdown while a broadcast is in flight.
            return false;
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmSettingChange)
        {
            var dispatcher = _source.Dispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return IntPtr.Zero;
            if (lParam != IntPtr.Zero &&
                string.Equals(Marshal.PtrToStringUni(lParam), "Environment", StringComparison.OrdinalIgnoreCase))
                TryScheduleRefresh(() => dispatcher.BeginInvoke(_refresh));
            if (_shellSettingsChanged is not null)
                TryScheduleRefresh(() => dispatcher.BeginInvoke(_shellSettingsChanged));
        }
        return IntPtr.Zero;
    }
}
