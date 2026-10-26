using System.Runtime.InteropServices;

// Runs focus-sensitive Shell probes on a separate, never-switched Windows Desktop.
// This keeps SetFocus activation inside the probe desktop instead of the user's desktop.
internal static class PrivateDesktopShellProbe
{
    internal static int RunHostFilter() => Run("host-filter", ShellViewHostFilterProbe.RunForPrivateDesktop);

    internal static int RunHostMenuCommands() => Run("host-menu-commands", ShellViewHostFilterProbe.RunMenuCommandsForPrivateDesktop);

    internal static int RunHostFilterVirtualScope(string scope) =>
        Run($"host-filter-{scope}", () => ShellViewHostFilterProbe.RunVirtualScopeForPrivateDesktop(scope));

    internal static int RunColor() => Run("color", () => ShellViewColorSpike.Run(visible: true,
        deferParentCleanup: true));

    internal static int RunReadOnlyColorRule() => Run("color-readonly-rule", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, readOnlyRuleProbe: true));

    internal static int RunCommandStateReadOnly() =>
        Run("command-state-readonly", () => ShellViewColorSpike.Run(queryCommandStatesOnly: true));

    internal static int RunCommonListViewColorBaseline() => Run("common-listview-color-baseline",
        CommonListViewColorBaseline.Run);

    private static int Run(string kind, Func<int> probe)
    {
        var foregroundBefore = GetForegroundWindow();
        var desktopName = "ExplorerDirs-Probe-" + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktopW(desktopName, null, 0, 0, 0x01FF, 0);
        if (desktop == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the isolated probe desktop.");

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

        if (!probeReturned.Wait(TimeSpan.FromSeconds(25)))
        {
            Console.Error.WriteLine($"private-desktop-probe: kind={kind}; timeout=True; probe did not return before the deadline");
            return 124;
        }

        var foregroundAfter = GetForegroundWindow();
        var foregroundUnchanged = foregroundBefore == foregroundAfter;
        Console.WriteLine($"private-desktop-probe: kind={kind}; name={desktopName}; foreground-unchanged={foregroundUnchanged}; process-exit-owns-desktop-cleanup=True");

        if (failure is not null)
        {
            Console.Error.WriteLine($"private-desktop-probe failure ({kind}): " + failure);
            return 1;
        }

        if (!foregroundUnchanged)
            return 1;

        return result;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string desktop, string? device, nint devmode, uint flags, uint desiredAccess, nint security);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(nint desktop);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
