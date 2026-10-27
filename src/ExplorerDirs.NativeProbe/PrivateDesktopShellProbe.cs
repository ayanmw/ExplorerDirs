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

    internal static int RunSessionRestore() => Run("session-restore", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, probeSessionRestore: true), TimeSpan.FromSeconds(110));

    internal static int RunDropTarget() => Run("drop-target", ShellViewDropTargetProbe.Run);

    internal static int RunSessionRestoreConsumer(string fixture, string stateDirectory, Guid sessionId) =>
        Run("session-restore-consumer", () => ShellViewColorSpike.RunSessionRestoreConsumer(
            fixture, stateDirectory, sessionId, terminateProcessAfterProbe: true), TimeSpan.FromSeconds(100));

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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);
}
