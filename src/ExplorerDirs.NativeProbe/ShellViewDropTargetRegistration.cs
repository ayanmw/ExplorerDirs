using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// The host owns this registration and must dispose it before destroying the Shell view HWND.
internal sealed class ShellViewDropTargetRegistration : IDisposable
{
    private readonly nint _window;
    private readonly uint _threadId;
    private bool _registered = true;

    internal nint Window => _window;

    internal ShellViewDropTargetRegistration(nint window, nint dropTarget)
    {
        if (window == 0 || !IsWindow(window))
            throw new ArgumentException("A live Shell view window is required.", nameof(window));
        if (dropTarget == 0)
            throw new ArgumentNullException(nameof(dropTarget));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("OLE drop targets must be registered and revoked on their owning STA thread.");

        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        var hr = RegisterDragDrop(window, dropTarget);
        if (hr < 0)
        {
            OleUninitialize();
            Marshal.ThrowExceptionForHR(hr);
        }
        _window = window;
        _threadId = GetCurrentThreadId();
    }

    public void Dispose()
    {
        if (!_registered) return;
        if (GetCurrentThreadId() != _threadId)
            throw new InvalidOperationException("The OLE drop target must be revoked on the STA thread that registered it.");

        var hr = RevokeDragDrop(_window);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        _registered = false;
        OleUninitialize();
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint window, nint dropTarget);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
