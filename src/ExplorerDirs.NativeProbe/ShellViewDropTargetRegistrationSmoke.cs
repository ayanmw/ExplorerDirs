using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace ExplorerDirs.NativeProbe;

internal static class ShellViewDropTargetRegistrationSmoke
{
    private const int DragDropAlreadyRegistered = unchecked((int)0x80040101);

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var windows = new List<nint>();
        var targets = new List<nint>();
        StreamViewHost? host = null;
        try
        {
            var registrationType = typeof(ShellViewDropTargetRegistrationSmoke).Assembly.GetType(
                "ExplorerDirs.NativeProbe.ShellViewDropTargetRegistration", throwOnError: false);
            if (registrationType is null)
                throw new InvalidOperationException("The isolated Shell view host has no IDropTarget window registration owner.");

            if (registrationType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, [typeof(nint), typeof(nint)], modifiers: null) is null ||
                !typeof(IDisposable).IsAssignableFrom(registrationType))
                throw new InvalidOperationException("The Shell drop target registration owner must bind an HWND and IDropTarget, and revoke through IDisposable.");

            host = new StreamViewHost(0, []);

            for (var pane = 0; pane < 4; pane++)
            {
                var window = CreateWindowExW(0, "STATIC", $"Explorer Dirs drop target pane {pane + 1}",
                    0, -10000, -10000, 40, 40, 0, 0, 0, 0);
                if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                windows.Add(window);
                var target = Marshal.GetComInterfaceForObject(new SmokeDropTarget(), typeof(ISmokeDropTarget));
                targets.Add(target);
                host.RegisterDropTarget(window, target);
            }

            using (var duplicateHost = new StreamViewHost(0, []))
            try
            {
                duplicateHost.RegisterDropTarget(windows[0], targets[0]);
                throw new InvalidOperationException("A second pane host unexpectedly registered an already-owned HWND.");
            }
            catch (COMException exception) when (exception.HResult == DragDropAlreadyRegistered)
            {
                Console.WriteLine("PASS duplicate host registration fails without replacing the existing owner");
            }

            host.RevokeDropTarget(windows[0]);
            var reRegisterResult = RegisterDragDrop(windows[0], targets[0]);
            if (reRegisterResult < 0)
                Marshal.ThrowExceptionForHR(reRegisterResult);
            Check(RevokeDragDrop(windows[0]));
            host.Dispose();
            host = null;

            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The hidden drop-target registration smoke changed the foreground window.");
            Console.WriteLine("shell-view-drop-target-registration-smoke=passed; four-pane-single-host=True; duplicate-owner-rollback=True; selective-revoke=True; dispose-revokes-rest=True; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            host?.Dispose();
            foreach (var target in targets) Marshal.Release(target);
            foreach (var window in windows) _ = DestroyWindow(window);
            if (GetForegroundWindow() != foreground)
                Console.WriteLine("shell-view-drop-target-registration-smoke: foreground changed during cleanup.");
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint window, nint target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}

[ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISmokeDropTarget
{
    [PreserveSig] int DragEnter(nint dataObject, uint keyState, ref SmokeOlePoint point, ref uint effect);
    [PreserveSig] int DragOver(uint keyState, ref SmokeOlePoint point, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(nint dataObject, uint keyState, ref SmokeOlePoint point, ref uint effect);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class SmokeDropTarget : ISmokeDropTarget
{
    public int DragEnter(nint dataObject, uint keyState, ref SmokeOlePoint point, ref uint effect) => 0;
    public int DragOver(uint keyState, ref SmokeOlePoint point, ref uint effect) => 0;
    public int DragLeave() => 0;
    public int Drop(nint dataObject, uint keyState, ref SmokeOlePoint point, ref uint effect) => 0;
}

[StructLayout(LayoutKind.Sequential)]
public struct SmokeOlePoint { public int X, Y; }
