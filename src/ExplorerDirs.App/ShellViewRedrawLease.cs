using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace ExplorerDirs.App;

internal sealed class ShellViewRedrawLease : IDisposable
{
    private static readonly object NativeLeaseGate = new();
    private static readonly Dictionary<nint, NativeLeaseState> NativeLeases = [];
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");

    private Func<bool, int>? _setRedraw;
    private Action? _release;

    private sealed class NativeLeaseState(SetRedrawDelegate setRedraw, IntPtr view)
    {
        internal readonly SetRedrawDelegate SetRedraw = setRedraw;
        internal readonly IntPtr View = view;
        internal int Count = 1;
    }

    internal static bool ShouldSuppressForRefresh(bool preserveLastFrame) => preserveLastFrame;

    private ShellViewRedrawLease(Func<bool, int>? setRedraw, Action? release = null)
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
        var unknownId = UnknownId;
        if (Marshal.QueryInterface(folderView2, in unknownId, out var identity) < 0 || identity == IntPtr.Zero)
            return null;
        var setRedraw = Marshal.GetDelegateForFunctionPointer<SetRedrawDelegate>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(folderView2), 39 * IntPtr.Size));
        // SetRedraw is a binary switch; overlapping refreshes must share one owner count or an early
        // lease disposal can re-enable painting while another operation still depends on the hold.
        lock (NativeLeaseGate)
        {
            if (NativeLeases.TryGetValue(identity, out var activeLease))
            {
                activeLease.Count++;
                Marshal.Release(identity);
                return new ShellViewRedrawLease(null, () => ReleaseNativeLease(identity));
            }

            if (setRedraw(folderView2, 0) < 0)
            {
                Marshal.Release(identity);
                return null;
            }
            Marshal.AddRef(folderView2);
            NativeLeases.Add(identity, new NativeLeaseState(setRedraw, folderView2));
            return new ShellViewRedrawLease(null, () => ReleaseNativeLease(identity));
        }
    }

    private static void ReleaseNativeLease(nint identity)
    {
        NativeLeaseState? retired = null;
        lock (NativeLeaseGate)
        {
            if (!NativeLeases.TryGetValue(identity, out var activeLease)) return;
            if (--activeLease.Count == 0)
            {
                NativeLeases.Remove(identity);
                retired = activeLease;
            }
        }

        if (retired is null) return;
        try { _ = retired.SetRedraw(retired.View, 1); }
        finally
        {
            Marshal.Release(retired.View);
            Marshal.Release(identity);
        }
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
