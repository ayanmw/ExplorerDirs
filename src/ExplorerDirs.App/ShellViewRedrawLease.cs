using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal sealed class ShellViewRedrawLease : IDisposable
{
    private Func<bool, int>? _setRedraw;
    private Action? _release;

    internal static bool ShouldSuppressForRefresh(bool preserveLastFrame, bool directoryNotificationUsed) =>
        preserveLastFrame && !directoryNotificationUsed;

    private ShellViewRedrawLease(Func<bool, int> setRedraw, Action? release = null)
    {
        _setRedraw = setRedraw;
        _release = release;
    }

    internal static ShellViewRedrawLease? TrySuppress(Func<bool, int> setRedraw)
    {
        ArgumentNullException.ThrowIfNull(setRedraw);
        return setRedraw(false) >= 0 ? new ShellViewRedrawLease(setRedraw) : null;
    }

    internal static ShellViewRedrawLease? TrySuppress(IntPtr folderView2)
    {
        if (folderView2 == IntPtr.Zero) return null;
        var setRedraw = Marshal.GetDelegateForFunctionPointer<SetRedrawDelegate>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(folderView2), 39 * IntPtr.Size));
        Marshal.AddRef(folderView2);
        if (setRedraw(folderView2, 0) < 0)
        {
            Marshal.Release(folderView2);
            return null;
        }
        return new ShellViewRedrawLease(redraw => setRedraw(folderView2, redraw ? 1 : 0),
            () => Marshal.Release(folderView2));
    }

    internal static T RunSuppressed<T>(ShellViewRedrawLease? lease, Func<T> action)
    {
        using (lease) return action();
    }

    public void Dispose()
    {
        var setRedraw = Interlocked.Exchange(ref _setRedraw, null);
        var release = Interlocked.Exchange(ref _release, null);
        try { if (setRedraw is not null) _ = setRedraw(true); }
        finally { release?.Invoke(); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetRedrawDelegate(IntPtr instance, int redrawOn);
}
