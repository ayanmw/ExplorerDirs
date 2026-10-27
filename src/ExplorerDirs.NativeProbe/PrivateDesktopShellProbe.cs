using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

// Runs focus-sensitive Shell probes on a separate, never-switched Windows Desktop.
// This keeps SetFocus activation inside the probe desktop instead of the user's desktop.
internal static class PrivateDesktopShellProbe
{
    internal static int RunHostFilter() => Run("host-filter", ShellViewHostFilterProbe.RunForPrivateDesktop);

    internal static int RunHostMenuCommands() => Run("host-menu-commands", ShellViewHostFilterProbe.RunMenuCommandsForPrivateDesktop);

    internal static int RunHostNewFolder() => Run("host-newfolder", ShellViewHostFilterProbe.RunNewFolderForPrivateDesktop);

    internal static int RunHostFilterVirtualScope(string scope) =>
        Run($"host-filter-{scope}", () => ShellViewHostFilterProbe.RunVirtualScopeForPrivateDesktop(scope));

    internal static int RunColor() => Run("color", () => ShellViewColorSpike.Run(visible: true,
        deferParentCleanup: true));

    internal static int RunMenuActivation() => Run("menu-activation", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, probeMenuActivation: true));

    internal static int RunSessionRestore(bool synchronousCleanupProbe = false) => Run(
        synchronousCleanupProbe ? "session-restore-sync-cleanup" : "session-restore",
        () => ShellViewColorSpike.Run(visible: true, deferParentCleanup: true, probeSessionRestore: true,
            synchronousCleanupProbe: synchronousCleanupProbe), TimeSpan.FromSeconds(110));

    internal static int RunDropTarget() => Run("drop-target", ShellViewDropTargetProbe.Run);

    internal static int RunSessionRestoreConsumer(string fixture, string stateDirectory, Guid sessionId,
        bool synchronousCleanupProbe = false) =>
        Run(synchronousCleanupProbe ? "session-restore-consumer-sync-cleanup" : "session-restore-consumer",
            () => ShellViewColorSpike.RunSessionRestoreConsumer(fixture, stateDirectory, sessionId,
                terminateProcessAfterProbe: !synchronousCleanupProbe,
                synchronousCleanupProbe: synchronousCleanupProbe),
            TimeSpan.FromSeconds(synchronousCleanupProbe ? 55 : 100));

    internal static int RunReadOnlyColorRule() => Run("color-readonly-rule", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, readOnlyRuleProbe: true));

    internal static int RunCommandStateReadOnly() =>
        Run("command-state-readonly", () => ShellViewColorSpike.Run(queryCommandStatesOnly: true));

    internal static int RunStaticShellColumnAdapter(bool strictInterfaces = false, bool prototypeClass = false, bool activateView = false, bool wrappedPidls = false) =>
        Run(wrappedPidls ? "static-shell-column-wrapped-adapter" : activateView ? "static-shell-column-adapter-activated-strict" : prototypeClass ? "static-shell-column-adapter-classid" : strictInterfaces ? "static-shell-column-adapter-strict" : "static-shell-column-adapter",
            () => StaticShellFolderAdapterProbe.Run(strictInterfaces, prototypeClass, activateView, wrappedPidls));

    internal static int RunCommonListViewColorBaseline() => Run("common-listview-color-baseline",
        CommonListViewColorBaseline.Run);

    internal static int RunDesktopOwnershipSmoke()
    {
        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        var originalForeground = GetForegroundWindow();
        var probeDesktop = CreateProbeDesktop("ExplorerDirs-Ownership-Smoke-" + Guid.NewGuid().ToString("N"));
        var currentDesktop = GetThreadDesktop(GetCurrentThreadId());
        var currentForeground = GetForegroundWindow();
        var closed = CloseDesktop(probeDesktop);
        var passed = originalDesktop != 0 && probeDesktop != 0 && originalDesktop == currentDesktop &&
            originalForeground == currentForeground && closed;
        Console.WriteLine($"private-desktop-owner-smoke: original-thread-desktop=0x{originalDesktop:X}; current-thread-desktop=0x{currentDesktop:X}; foreground-before=0x{originalForeground:X}; foreground-after=0x{currentForeground:X}; close-created-desktop={closed}; passed={passed}");
        return passed ? 0 : 1;
    }

    internal static int RunWindowDestroyControl() => Run("window-destroy-control", () =>
    {
        var foreground = GetForegroundWindow();
        var window = CreateWindowExW(0x00000080u, "STATIC", "ExplorerDirs isolated destroy control",
            0x10CF0000u, 20, 20, 160, 80, 0, 0, 0, 0);
        if (window == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the isolated STATIC control.");

        Console.WriteLine($"window-destroy-control: hwnd=0x{window:X}; class=Static; visible={IsWindowVisible(window)}; foreground-unchanged={GetForegroundWindow() == foreground}");
        var destroyed = DestroyWindow(window);
        var remainsValid = IsWindow(window);
        Console.WriteLine($"window-destroy-control: destroy-returned={destroyed}; still-valid={remainsValid}; foreground-unchanged={GetForegroundWindow() == foreground}");
        return destroyed && !remainsValid && GetForegroundWindow() == foreground ? 0 : 1;
    }, TimeSpan.FromSeconds(10));

    internal static int RunSharedDesktopWindowDestroyControl() => Run("shared-desktop-window-destroy-control", () =>
    {
        var foreground = GetForegroundWindow();
        var sentinel = CreateWindowExW(0x00000080u, "STATIC", "ExplorerDirs isolated parent sentinel",
            0x10CF0000u, 20, 20, 160, 80, 0, 0, 0, 0);
        if (sentinel == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the parent sentinel.");

        using var child = StartWindowDestroyChild();
        if (!child.WaitForExit(15_000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            Console.Error.WriteLine("shared-desktop-window-destroy-control: child-timeout=True");
            _ = DestroyWindow(sentinel);
            return 124;
        }

        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output)) Console.Write(output);
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.Write(error);
        var parentDestroyed = DestroyWindow(sentinel);
        var passed = child.ExitCode == 0 && parentDestroyed && !IsWindow(sentinel) && GetForegroundWindow() == foreground;
        Console.WriteLine($"shared-desktop-window-destroy-control: child-exit={child.ExitCode}; parent-destroyed={parentDestroyed}; foreground-unchanged={GetForegroundWindow() == foreground}; passed={passed}");
        return passed ? 0 : 1;
    }, TimeSpan.FromSeconds(20));

    private static Process StartWindowDestroyChild()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the NativeProbe executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(PrivateDesktopShellProbe).Assembly.Location);
        start.ArgumentList.Add("--window-destroy-current-desktop-child");
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the window-destroy child.");
    }

    internal static int RunWindowDestroyControlOnCurrentDesktop()
    {
        var foreground = GetForegroundWindow();
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var window = CreateWindowExW(0x00000080u, "STATIC", "ExplorerDirs shared-desktop child control",
            0x10CF0000u, 30, 30, 160, 80, 0, 0, 0, 0);
        if (window == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the child control.");
        Console.WriteLine($"shared-desktop-window-destroy-child: pid={Environment.ProcessId}; tid={GetCurrentThreadId()}; desktop=0x{desktop:X}; hwnd=0x{window:X}; visible={IsWindowVisible(window)}");
        var destroyed = DestroyWindow(window);
        var remainsValid = IsWindow(window);
        var passed = destroyed && !remainsValid && GetForegroundWindow() == foreground;
        Console.WriteLine($"shared-desktop-window-destroy-child: destroy-returned={destroyed}; still-valid={remainsValid}; foreground-unchanged={GetForegroundWindow() == foreground}; passed={passed}");
        return passed ? 0 : 1;
    }

    private static int Run(string kind, Func<int> probe, TimeSpan? timeout = null)
    {
        var foregroundBefore = GetForegroundWindow();
        var desktopName = "ExplorerDirs-Probe-" + Guid.NewGuid().ToString("N");
        var desktop = CreateProbeDesktop(desktopName);

        var result = 1;
        Exception? failure = null;
        var probeReturned = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            if (!SetThreadDesktop(desktop))
            {
                failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not assign the probe thread to its isolated desktop.");
                probeReturned.Set();
                return;
            }

            try
            {
                result = probe();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                // Returning from the Shell probe is enough to report assertions. The STA
                // may stall while tearing down its private Desktop, so process exit owns cleanup.
                probeReturned.Set();
            }
        })
        {
            IsBackground = true,
            Name = "ExplorerDirs isolated Shell probe"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        if (!probeReturned.Wait(timeout ?? TimeSpan.FromSeconds(25)))
        {
            var foregroundAtTimeout = GetForegroundWindow();
            Console.Error.WriteLine($"private-desktop-probe: kind={kind}; timeout=True; foreground-before=0x{foregroundBefore:X}; foreground-at-timeout=0x{foregroundAtTimeout:X}; probe did not return before the deadline");
            return 124;
        }

        var foregroundAfter = GetForegroundWindow();
        var foregroundUnchanged = foregroundBefore == foregroundAfter;
        Console.WriteLine($"private-desktop-probe: kind={kind}; name={desktopName}; foreground-before=0x{foregroundBefore:X}; foreground-after=0x{foregroundAfter:X}; foreground-unchanged={foregroundUnchanged}; process-exit-owns-desktop-cleanup=True");

        if (failure is not null)
        {
            Console.Error.WriteLine($"private-desktop-probe failure ({kind}): " + failure);
            return 1;
        }

        if (!foregroundUnchanged)
            return 1;

        return result;
    }

    private static nint CreateProbeDesktop(string name)
    {
        nint desktop = 0;
        var error = 0;
        var creator = new Thread(() =>
        {
            desktop = CreateDesktopW(name, null, 0, 0, 0x01FF, 0);
            if (desktop == 0) error = Marshal.GetLastWin32Error();
        }) { IsBackground = true, Name = "ExplorerDirs private Desktop creator" };
        creator.Start();
        creator.Join();
        if (desktop == 0)
            throw new System.ComponentModel.Win32Exception(error, "Could not create the isolated probe desktop.");
        return desktop;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string desktop, string? device, nint devmode, uint flags, uint desiredAccess, nint security);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(nint desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);
}
