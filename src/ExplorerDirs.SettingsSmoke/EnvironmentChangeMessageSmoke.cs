using ExplorerDirs.App;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ExplorerDirs.SettingsSmoke;

internal static class EnvironmentChangeMessageSmoke
{
    private const int WmSettingChange = 0x001A;

    internal static void Run()
    {
        var refreshScheduled = false;
        Check(SystemSettingChangeHook.TryScheduleRefresh(() => refreshScheduled = true) && refreshScheduled,
            "a live Dispatcher accepts the environment refresh callback");
        Check(!SystemSettingChangeHook.TryScheduleRefresh(
                () => throw new InvalidOperationException("dispatcher is shutting down")),
            "a Dispatcher shutdown race does not escape the native settings-message callback");

        Exception? failure = null;
        var handledEnvironmentChanges = 0;
        var phase = "thread start";
        var thread = new Thread(() =>
        {
            try { handledEnvironmentChanges = RunOnStaThread(value => Volatile.Write(ref phase, value)); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException($"Environment setting-change message smoke timed out in '{Volatile.Read(ref phase)}'.");
        if (failure is not null) throw new InvalidOperationException("Environment setting-change message smoke failed.", failure);
        Check(handledEnvironmentChanges == 1,
            "WM_SETTINGCHANGE with Environment name schedules one refresh");
    }

    private static int RunOnStaThread(Action<string> setPhase)
    {
        setPhase("create HwndSource");
        var foregroundBefore = GetForegroundWindow();
        var refreshCount = 0;
        var shellSettingRefreshCount = 0;
        var source = new HwndSource(new HwndSourceParameters("EnvironmentChangeSmoke")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0
        });
        setPhase("install system setting-change hook");
        using (var hook = new SystemSettingChangeHook(source, () => refreshCount++, () => shellSettingRefreshCount++))
        {
            setPhase("send setting-change messages");
            Send(source.Handle, WmSettingChange, IntPtr.Zero, IntPtr.Zero);
            SendSetting(source.Handle, WmSettingChange, "UserPreferencesMask");
            SendSetting(source.Handle, WmSettingChange + 1, "Environment");
            setPhase("pump dispatcher after unrelated messages");
            PumpDispatcher(source.Dispatcher);
            Check(refreshCount == 0, "unrelated messages do not schedule environment refresh");
            Check(shellSettingRefreshCount == 2,
                "WM_SETTINGCHANGE schedules a Shell preference recheck regardless of the setting name");
            setPhase("send environment setting-change message");
            SendSetting(source.Handle, WmSettingChange, "eNvIrOnMeNt");
            setPhase("pump dispatcher after environment message");
            PumpDispatcher(source.Dispatcher);
        }
        setPhase("dispose HwndSource");
        source.Dispose();
        Check(GetForegroundWindow() == foregroundBefore,
            "hidden message target does not change the foreground window");
        Check(shellSettingRefreshCount == 3,
            "Environment notification also schedules one Shell preference recheck");
        setPhase("complete");
        return refreshCount;
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void SendSetting(IntPtr hwnd, int message, string name)
    {
        var pointer = Marshal.StringToHGlobalUni(name);
        try { Send(hwnd, message, IntPtr.Zero, pointer); }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Environment message: {label}");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr Send(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
