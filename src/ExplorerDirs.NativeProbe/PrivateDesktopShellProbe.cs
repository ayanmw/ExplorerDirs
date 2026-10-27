using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

// Runs focus-sensitive Shell probes on a separate, never-switched Windows Desktop.
// This keeps SetFocus activation inside the probe desktop instead of the user's desktop.
internal static class PrivateDesktopShellProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WctCoGetCallState(int threadId, out uint callState);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WctCoGetActivationState(Guid classId, uint processId, out uint activationState);
    private static WctCoGetCallState? CoGetCallStateCallback;
    private static WctCoGetActivationState? CoGetActivationStateCallback;

    internal static int RunHostFilter() => Run("host-filter", ShellViewHostFilterProbe.RunForPrivateDesktop);

    internal static int RunHostMenuCommands() => Run("host-menu-commands", ShellViewHostFilterProbe.RunMenuCommandsForPrivateDesktop);

    internal static int RunHostNewFolder() => Run("host-newfolder", ShellViewHostFilterProbe.RunNewFolderForPrivateDesktop);

    internal static int RunHostFilterVirtualScope(string scope) =>
        Run($"host-filter-{scope}", () => ShellViewHostFilterProbe.RunVirtualScopeForPrivateDesktop(scope));

    internal static int RunColor() => Run("color", () => ShellViewColorSpike.Run(visible: true,
        deferParentCleanup: true));

    internal static int RunMenuActivation() => Run("menu-activation", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, probeMenuActivation: true));

    internal static int RunShellViewWindowDestroyControl() => Run("shell-view-window-destroy-control",
        () => ShellViewColorSpike.Run(visible: true, deferParentCleanup: true,
            probeWindowDestroyWithShellView: true), TimeSpan.FromSeconds(30));

    internal static int RunSinglePaneSynchronousCleanup() => Run("single-pane-sync-cleanup",
        ShellViewColorSpike.RunSinglePaneSynchronousCleanup, TimeSpan.FromSeconds(35));

    internal static int RunMultiPaneSynchronousCleanup(int paneCount) => Run($"{paneCount}-pane-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(paneCount), TimeSpan.FromSeconds(45));

    internal static int RunFourPaneMenuSynchronousCleanup() => Run("four-pane-menu-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(4, activateMenus: true), TimeSpan.FromSeconds(60));

    internal static int RunSinglePaneMenuSynchronousCleanup() => Run("single-pane-menu-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuNoFocusSynchronousCleanup() => Run("single-pane-menu-nofocus-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            menuActivationState: 1), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuActiveSynchronousCleanup() => Run("single-pane-menu-active-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            deactivateMenus: false), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuDetachedImeSynchronousCleanup() => Run("single-pane-menu-detached-ime-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            detachImeContextBeforeParentDestroy: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuDisabledImeSynchronousCleanup() => Run("single-pane-menu-disabled-ime-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            disableImeForProbeThread: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuOleInitializedSynchronousCleanup() => Run("single-pane-menu-ole-init-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true),
        TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunSinglePaneMenuWpfDispatcherSynchronousCleanup(bool initializeOle = false) => Run(
        initializeOle ? "single-pane-menu-wpf-ole-sync-cleanup" : "single-pane-menu-wpf-dispatcher-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            useWpfDispatcherPump: true), TimeSpan.FromSeconds(45), initializeOle);

    internal static int RunSinglePaneMenuWpfHostSynchronousCleanup() => Run("single-pane-menu-wpf-host-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            useWpfDispatcherPump: true, useWpfHwndSourceParent: true, useWpfHwndHostPane: true,
            restoreParentFocusBeforeDestroy: false),
        TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunFourPaneMenuWpfHostSynchronousCleanup() => Run("four-pane-menu-wpf-host-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(4, activateMenus: true,
            useWpfDispatcherPump: true, useWpfHwndSourceParent: true, useWpfHwndHostPane: true,
            restoreParentFocusBeforeDestroy: false),
        TimeSpan.FromSeconds(60), initializeOle: true);

    internal static int RunSinglePaneMenuDetachedViewImeSynchronousCleanup() => Run("single-pane-menu-detached-view-ime-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            detachShellViewImeContexts: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneLateCoordinatorDispose() => Run("single-pane-menu-late-coordinator-dispose",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            disposeMenuCoordinatorBeforeParent: false), TimeSpan.FromSeconds(45));

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

    internal static int RunStaticShellColumnAdapter(bool strictInterfaces = false, bool prototypeClass = false, bool activateView = false, bool wrappedPidls = false, bool legacyDetailsCallbackProbe = false) =>
        Run(legacyDetailsCallbackProbe ? "static-shell-column-legacy-details-callback" : wrappedPidls ? "static-shell-column-wrapped-adapter" : activateView ? "static-shell-column-adapter-activated-strict" : prototypeClass ? "static-shell-column-adapter-classid" : strictInterfaces ? "static-shell-column-adapter-strict" : "static-shell-column-adapter",
            () => StaticShellFolderAdapterProbe.Run(strictInterfaces, prototypeClass, activateView, wrappedPidls, legacyDetailsCallbackProbe));

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

    private static int Run(string kind, Func<int> probe, TimeSpan? timeout = null, bool initializeOle = false)
    {
        var foregroundBefore = GetForegroundWindow();
        var desktopName = "ExplorerDirs-Probe-" + Guid.NewGuid().ToString("N");
        var desktop = CreateProbeDesktop(desktopName);

        var result = 1;
        Exception? failure = null;
        var workerThreadId = 0;
        var probeReturned = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            Interlocked.Exchange(ref workerThreadId, unchecked((int)GetCurrentThreadId()));
            if (!SetThreadDesktop(desktop))
            {
                failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not assign the probe thread to its isolated desktop.");
                probeReturned.Set();
                return;
            }

            var oleInitialized = false;
            try
            {
                if (initializeOle)
                {
                    var oleResult = OleInitialize(0);
                    Console.WriteLine($"private-desktop-probe: ole-initialize=0x{oleResult:X8}; thread={GetCurrentThreadId()}");
                    if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
                    oleInitialized = true;
                }
                result = probe();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (oleInitialized) OleUninitialize();
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
            var threadId = unchecked((uint)Volatile.Read(ref workerThreadId));
            Console.Error.WriteLine($"private-desktop-probe: kind={kind}; timeout=True; worker-tid={threadId}; foreground-before=0x{foregroundBefore:X}; foreground-at-timeout=0x{foregroundAtTimeout:X}; probe did not return before the deadline");
            CaptureWaitChain(threadId);
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

    private static void CaptureWaitChain(uint threadId)
    {
        if (threadId == 0)
        {
            Console.Error.WriteLine("wait-chain: worker thread id unavailable");
            return;
        }

        var session = OpenThreadWaitChainSession(0, 0);
        if (session == 0)
        {
            Console.Error.WriteLine($"wait-chain: OpenThreadWaitChainSession failed; win32={Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            RegisterWaitChainCOMCallback(GetComCallStateCallback(), GetComActivationStateCallback());
            const uint nodeCapacity = 16;
            var nodes = new WaitChainNode[nodeCapacity];
            var count = nodeCapacity;
            var flags = 0x1u | 0x2u | 0x4u; // Include out-of-process, COM, and critical-section owners.
            if (!GetThreadWaitChain(session, 0, flags, threadId, ref count, nodes, out var cycle))
            {
                Console.Error.WriteLine($"wait-chain: query failed; tid={threadId}; win32={Marshal.GetLastWin32Error()}");
                return;
            }

            Console.Error.WriteLine($"wait-chain: tid={threadId}; nodes={count}; cycle={cycle}");
            for (var index = 0; index < Math.Min(count, nodeCapacity); index++)
            {
                var node = nodes[index];
                if (node.ObjectType == 8) // WctThreadType, from the Windows SDK wct.h enum.
                {
                    Console.Error.WriteLine($"wait-chain: [{index}] type={WaitChainObjectType(node.ObjectType)} status={node.ObjectStatus}; pid={node.ProcessId}; tid={node.ThreadId}; switches={node.ContextSwitches}");
                    DescribeWaitChainThread(node.ProcessId, node.ThreadId);
                }
                else
                    Console.Error.WriteLine($"wait-chain: [{index}] type={WaitChainObjectType(node.ObjectType)} status={node.ObjectStatus}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"wait-chain: capture failed; {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            CloseThreadWaitChainSession(session);
        }
    }

    private static void DescribeWaitChainThread(uint processId, uint threadId)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            if (processId == Environment.ProcessId)
            {
                var thread = process.Threads.Cast<ProcessThread>()
                    .FirstOrDefault(candidate => candidate.Id == unchecked((int)threadId));
                Console.Error.WriteLine(thread is null
                    ? $"wait-chain-thread: pid={processId}; tid={threadId}; state=not-found"
                    : $"wait-chain-thread: pid={processId}; tid={threadId}; state={thread.ThreadState}; wait-reason={(thread.ThreadState == System.Diagnostics.ThreadState.Wait ? thread.WaitReason.ToString() : "n/a")}");
            }
            else
            {
                Console.Error.WriteLine($"wait-chain-process: pid={processId}; name={process.ProcessName}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"wait-chain-owner: pid={processId}; tid={threadId}; lookup={exception.GetType().Name}");
        }
    }

    private static string WaitChainObjectType(uint type) => type switch
    {
        1 => "CriticalSection",
        2 => "SendMessage",
        3 => "Mutex",
        4 => "ALPC",
        5 => "COM",
        6 => "ThreadWait",
        7 => "ProcessWait",
        8 => "Thread",
        9 => "COMActivation",
        10 => "Unknown",
        11 => "SocketIO",
        12 => "SMBIO",
        _ => $"Unknown({type})"
    };

    private static WctCoGetCallState GetComCallStateCallback()
    {
        return CoGetCallStateCallback ??= Marshal.GetDelegateForFunctionPointer<WctCoGetCallState>(
            GetProcAddress(GetModuleHandleW("ole32.dll"), "CoGetCallState"));
    }

    private static WctCoGetActivationState GetComActivationStateCallback()
    {
        return CoGetActivationStateCallback ??= Marshal.GetDelegateForFunctionPointer<WctCoGetActivationState>(
            GetProcAddress(GetModuleHandleW("ole32.dll"), "CoGetActivationState"));
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string procedureName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    [StructLayout(LayoutKind.Explicit, Size = 280)]
    private struct WaitChainNode
    {
        [FieldOffset(0)] public uint ObjectType;
        [FieldOffset(4)] public uint ObjectStatus;
        [FieldOffset(8)] public uint ProcessId;
        [FieldOffset(12)] public uint ThreadId;
        [FieldOffset(16)] public uint WaitTime;
        [FieldOffset(20)] public uint ContextSwitches;
    }

    [DllImport("advapi32.dll")]
    private static extern nint OpenThreadWaitChainSession(uint flags, nint callback);

    [DllImport("advapi32.dll")]
    private static extern void CloseThreadWaitChainSession(nint session);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadWaitChain(nint session, nuint context, uint flags, uint threadId,
        ref uint nodeCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] WaitChainNode[] nodeInfo,
        [MarshalAs(UnmanagedType.Bool)] out bool isCycle);

    [DllImport("advapi32.dll")]
    private static extern void RegisterWaitChainCOMCallback(WctCoGetCallState callStateCallback,
        WctCoGetActivationState activationStateCallback);
}
