using System.Reflection;
using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

internal static class ShellViewDropTargetRegistrationSmoke
{
    private const int DragDropAlreadyRegistered = unchecked((int)0x80040101);

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        nint window = 0, target = 0;
        object? registration = null;
        try
        {
            window = CreateWindowExW(0, "STATIC", "Explorer Dirs drop target registration smoke",
                0, -10000, -10000, 40, 40, 0, 0, 0, 0);
            if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var registrationType = typeof(ShellViewDropTargetRegistrationSmoke).Assembly.GetType(
                "ExplorerDirs.NativeProbe.ShellViewDropTargetRegistration", throwOnError: false);
            if (registrationType is null)
                throw new InvalidOperationException("The isolated Shell view host has no IDropTarget window registration owner.");

            var constructor = registrationType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, [typeof(nint), typeof(nint)], modifiers: null);
            if (constructor is null || !typeof(IDisposable).IsAssignableFrom(registrationType))
                throw new InvalidOperationException("The Shell drop target registration owner must bind an HWND and IDropTarget, and revoke through IDisposable.");

            target = Marshal.GetComInterfaceForObject(new SmokeDropTarget(), typeof(ISmokeDropTarget));
            registration = constructor.Invoke([window, target]);
            var duplicateResult = RegisterDragDrop(window, target);
            if (duplicateResult != DragDropAlreadyRegistered)
                throw new InvalidOperationException($"The test window was not registered exactly once: duplicate HRESULT=0x{duplicateResult:X8}.");

            ((IDisposable)registration).Dispose();
            var reRegisterResult = RegisterDragDrop(window, target);
            if (reRegisterResult < 0)
                Marshal.ThrowExceptionForHR(reRegisterResult);
            Check(RevokeDragDrop(window));
            ((IDisposable)registration).Dispose();

            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The hidden drop-target registration smoke changed the foreground window.");
            Console.WriteLine("shell-view-drop-target-registration-smoke=passed; register-once=True; revoke-before-window-destroy=True; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            if (registration is IDisposable disposable) disposable.Dispose();
            if (target != 0) Marshal.Release(target);
            if (window != 0) _ = DestroyWindow(window);
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
