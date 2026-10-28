using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Interop;
using System.Windows.Threading;
using ExplorerDirs.App;

// Drives the production WM_SETTINGCHANGE hook with an isolated hidden HWND.
// The temporary user variable is unique, is never broadcast to other windows,
// and is removed from both the registry and this process before the probe exits.
internal static class EnvironmentRefreshProbe
{
    private const int WmSettingChange = 0x001A;

    internal static int Run()
    {
        var variable = "EXPLORER_DIRS_HOOK_SMOKE_" + Guid.NewGuid().ToString("N");
        var value = "fresh-" + Guid.NewGuid().ToString("N");
        var processBefore = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process);
        string? registryBefore = null;
        RegistryValueKind? registryKindBefore = null;
        using var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true)
            ?? throw new InvalidOperationException("Could not access the current user's Environment key.");
        if (key.GetValueNames().Contains(variable, StringComparer.OrdinalIgnoreCase))
        {
            registryKindBefore = key.GetValueKind(variable);
            registryBefore = key.GetValue(variable, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        HwndSource? source = null;
        IDisposable? settingChangeHook = null;
        try
        {
            // Avoid MainWindow here: its HWND initialization restores user window placement.
            var parameters = new HwndSourceParameters("ExplorerDirsEnvironmentRefreshProbe")
            {
                Width = 1,
                Height = 1,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = 0
            };
            source = new HwndSource(parameters);
            var hwnd = source.Handle;
            if (IsWindowVisible(hwnd)) throw new InvalidOperationException("Test window unexpectedly became visible.");
            var appAssembly = typeof(MainWindow).Assembly;
            var environmentRefreshType = appAssembly.GetType("ExplorerDirs.App.EnvironmentRefresh", true)!;
            var environmentRefresh = Activator.CreateInstance(environmentRefreshType)!;
            var reload = environmentRefreshType.GetMethod("Reload", BindingFlags.Instance | BindingFlags.Public)!;
            Action refresh = () => reload.Invoke(environmentRefresh, null);
            var hookType = appAssembly.GetType("ExplorerDirs.App.SystemSettingChangeHook", true)!;
            var hookConstructor = hookType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [typeof(HwndSource), typeof(Action), typeof(Action)], null)!;
            settingChangeHook = (IDisposable)hookConstructor.Invoke([source, refresh, null]);
            var nativeMessageObserved = false;
            source.AddHook((IntPtr _, int message, IntPtr _, IntPtr _, ref bool _) =>
            {
                if (message == WmSettingChange) nativeMessageObserved = true;
                return IntPtr.Zero;
            });

            key.SetValue(variable, value, RegistryValueKind.String);
            var text = Marshal.StringToHGlobalUni("Environment");
            bool nativeRefresh;
            try
            {
                SendMessage(hwnd, WmSettingChange, IntPtr.Zero, text);
                nativeRefresh = PumpUntilValue(variable, value, TimeSpan.FromMilliseconds(500));
            }
            finally { Marshal.FreeHGlobal(text); }
            if (!nativeRefresh)
                throw new TimeoutException("The production WM_SETTINGCHANGE hook did not refresh the process environment.");
            PumpUntil(() => Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process) == value,
                TimeSpan.FromSeconds(5), "The environment refresh hook did not apply the new persisted variable.");

            string? childOutput = null;
            var launcher = typeof(MainWindow).Assembly.GetType("ExplorerDirs.App.TerminalLauncher", true)!
                .GetMethod("OpenAsync", BindingFlags.Public | BindingFlags.Static)!;
            childOutput = RunCommand(launcher, variable, $"/d /c echo %{variable}%");
            if (childOutput != value)
                throw new InvalidOperationException($"New cmd.exe inherited '{childOutput}', expected the refreshed value.");

            key.DeleteValue(variable, throwOnMissingValue: false);
            var removalText = Marshal.StringToHGlobalUni("Environment");
            bool nativeRemoval;
            try
            {
                SendMessage(hwnd, WmSettingChange, IntPtr.Zero, removalText);
                nativeRemoval = PumpUntilMissing(variable, TimeSpan.FromMilliseconds(500));
            }
            finally { Marshal.FreeHGlobal(removalText); }
            if (!nativeRemoval)
                throw new TimeoutException("The production WM_SETTINGCHANGE hook did not remove the deleted process variable.");
            PumpUntil(() => Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process) is null,
                TimeSpan.FromSeconds(5), "The environment refresh hook did not remove the deleted variable.");
            childOutput = RunCommand(launcher, variable, $"/d /c if defined {variable} (echo defined) else (echo missing)");
            if (childOutput != "missing")
                throw new InvalidOperationException($"New cmd.exe still inherited a deleted variable: '{childOutput}'.");
            if (!nativeMessageObserved)
                throw new InvalidOperationException("The hidden native HWND did not receive WM_SETTINGCHANGE.");
            if (IsWindowVisible(hwnd))
                throw new InvalidOperationException("The environment refresh HWND became visible.");

            Console.WriteLine($"environment-refresh-hook message=WM_SETTINGCHANGE native-message-observed={nativeMessageObserved} production-hook=native-window-message add-value-refreshed=True child-cmd-inherited=True delete-value-removed=True child-cmd-no-longer-inherits=True hwnd-visible=False other-windows-broadcast=False");
            return 0;
        }
        finally
        {
            try
            {
                if (registryKindBefore is { } oldKind && registryBefore is not null)
                    key.SetValue(variable, registryBefore, oldKind);
                else
                    key.DeleteValue(variable, throwOnMissingValue: false);
                if (registryBefore is null && key.GetValueNames().Contains(variable, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Temporary user environment value was not removed.");
            }
            finally
            {
                try
                {
                    Environment.SetEnvironmentVariable(variable, processBefore, EnvironmentVariableTarget.Process);
                    if (Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process) != processBefore)
                        throw new InvalidOperationException("Temporary process environment value was not restored.");
                }
                finally
                {
                    settingChangeHook?.Dispose();
                    source?.Dispose();
                }
            }
        }
    }

    private static void PumpUntil(Func<bool> predicate, TimeSpan timeout, string failure)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        var deadline = Stopwatch.StartNew();
        var complete = false;
        timer.Tick += (_, _) =>
        {
            complete = predicate();
            if (complete || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!complete) throw new TimeoutException(failure);
    }

    private static bool PumpUntilMissing(string variable, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var deadline = Stopwatch.StartNew();
        var complete = false;
        timer.Tick += (_, _) =>
        {
            complete = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process) is null;
            if (complete || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return complete;
    }

    private static string RunCommand(MethodInfo launcher, string variable, string arguments)
    {
        string? output = null;
        var launch = (Task)launcher.Invoke(null, [Environment.CurrentDirectory, CancellationToken.None,
            (Action<ProcessStartInfo>)(info =>
            {
                info.Arguments = arguments;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                using var child = Process.Start(info) ?? throw new InvalidOperationException("cmd.exe did not start.");
                if (!child.WaitForExit(5000))
                {
                    child.Kill();
                    throw new TimeoutException("cmd.exe did not finish reading its inherited environment.");
                }
                if (child.ExitCode != 0) throw new InvalidOperationException("cmd.exe returned a failure code.");
                output = child.StandardOutput.ReadToEnd().Trim();
            })])!;
        launch.GetAwaiter().GetResult();
        return output ?? throw new InvalidOperationException($"cmd.exe produced no output for {variable}.");
    }

    private static bool PumpUntilValue(string variable, string value, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        var deadline = Stopwatch.StartNew();
        var complete = false;
        timer.Tick += (_, _) =>
        {
            complete = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process) == value;
            if (complete || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return complete;
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
