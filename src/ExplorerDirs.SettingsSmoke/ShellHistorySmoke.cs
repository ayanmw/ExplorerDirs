using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class ShellHistorySmoke
{
    internal static void Run()
    {
        VerifyIndependentSubscriptions();
        var server = new HistoryTestServer { NotifyOnUnadvise = true };
        var unknown = Marshal.GetIUnknownForObject(server);
        var notifications = 0;
        try
        {
            using var history = ShellHistoryEvents.Subscribe(unknown, () => notifications++);
            Check(history.Connected && server.Advised == 1, "subscribes to the documented connection point");
            Check(history.CanNavigate(true, false, false) && history.CanNavigate(false, false, false),
                "unknown direction states preserve navigation attempts");
            var sink = server.Sink!;
            VerifyNativeDispatch(sink);
            Check(history.Back == true, "IDispatch marshals native command and VARIANT_BOOL");
            sink.CommandStateChange(2, false);
            sink.CommandStateChange(1, true);
            Check(!history.HasKnownEmptyHistory, "an available direction is not treated as empty history");
            Check(!history.CanNavigate(true, false, false) && history.CanNavigate(false, false, false),
                "native directions remain independent");
            Check(!history.CanNavigate(false, true, false) && !history.CanNavigate(false, false, true),
                "pending or unavailable view blocks actions without losing native state");
            sink.CommandStateChange(2, true);
            Check(history.CanNavigate(true, false, false), "pending-period notification remains available when settled");
            sink.CommandStateChange(1, false);
            sink.CommandStateChange(2, false);
            Check(history.HasKnownEmptyHistory, "known disabled back and forward commands confirm empty history");
            sink.CommandStateChange(2, true);
            sink.CommandStateChange(1, true);
            var beforeUnknown = notifications;
            sink.CommandStateChange(-1, false);
            Check(notifications == beforeUnknown && history.Back == true && history.Forward == true,
                "unrelated commands cannot clear navigation states");
            var cancel = true;
            sink.WindowClosing(false, ref cancel);
            int cx = 128, cy = 256;
            sink.ClientToHostWindow(ref cx, ref cy);
            Check(cancel && cx == 128 && cy == 256, "ignored events preserve in/out arguments");
            server.UnadviseResult = unchecked((int)0x80004005);
            var beforeDispose = notifications;
            history.Dispose();
            var afterDispose = notifications;
            sink.CommandStateChange(2, false);
            Check(server.Unadvised == 1 && afterDispose == beforeDispose && notifications == afterDispose &&
                !history.CanNavigate(true, false, false),
                "failed unadvise still disconnects callbacks and disables disposed state");
            history.Dispose();
            Check(server.Unadvised == 1, "dispose is idempotent");
        }
        finally { Marshal.Release(unknown); GC.KeepAlive(server); }

        var unsupported = Marshal.GetIUnknownForObject(new object());
        try
        {
            using var history = ShellHistoryEvents.Subscribe(unsupported, () => throw new Exception("unexpected callback"));
            Check(!history.Connected && history.CanNavigate(true, false, false),
                "unsupported subscriptions leave history unknown instead of disabling back");
        }
        finally { Marshal.Release(unsupported); }
        var synchronous = new HistoryTestServer { NotifyOnAdvise = true };
        unknown = Marshal.GetIUnknownForObject(synchronous);
        try
        {
            var syncCallbacks = 0;
            using var history = ShellHistoryEvents.Subscribe(unknown, () => syncCallbacks++);
            Check(history.Connected && history.Back == false && syncCallbacks == 1,
                "synchronous Advise notifications are cached before Subscribe returns");
        }
        finally { Marshal.Release(unknown); GC.KeepAlive(synchronous); }
        foreach (var adviseResult in new[] { unchecked((int)0x80004002), 1 })
        {
            var rejected = new HistoryTestServer { AdviseResult = adviseResult };
            unknown = Marshal.GetIUnknownForObject(rejected);
            try
            {
                using var history = ShellHistoryEvents.Subscribe(unknown, () => { });
                Check(!history.Connected && rejected.Unadvised == 0,
                    "failed or non-S_OK Advise does not invent an active cookie");
            }
            finally { Marshal.Release(unknown); GC.KeepAlive(rejected); }
        }
    }
    private static void VerifyIndependentSubscriptions()
    {
        var first = new HistoryTestServer();
        var second = new HistoryTestServer();
        var firstUnknown = Marshal.GetIUnknownForObject(first);
        var secondUnknown = Marshal.GetIUnknownForObject(second);
        try
        {
            using var a = ShellHistoryEvents.Subscribe(firstUnknown, () => { });
            using var b = ShellHistoryEvents.Subscribe(secondUnknown, () => { });
            first.Sink!.CommandStateChange(2, false);
            second.Sink!.CommandStateChange(2, true);
            second.Sink.CommandStateChange(1, false);
            Check(a.Back == false && a.Forward is null && b.Back == true && b.Forward == false,
                "browser instances retain independent native history states");
            a.Dispose();
            first.Sink.CommandStateChange(1, true);
            Check(!a.CanNavigate(false, false, false) && b.Back == true && b.Forward == false,
                "late callbacks from a retired instance cannot modify another browser");
        }
        finally
        {
            Marshal.Release(firstUnknown);
            Marshal.Release(secondUnknown);
            GC.KeepAlive(first);
            GC.KeepAlive(second);
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Shell history: " + message);
    }

    private static void VerifyNativeDispatch(ShellBrowserCommandSink sink)
    {
        var dispatch = Marshal.GetIDispatchForObject(sink);
        var size = IntPtr.Size == 8 ? 24 : 16; // VARIANT includes the two-pointer BRECORD union.
        var args = Marshal.AllocCoTaskMem(size * 2);
        var cancel = Marshal.AllocCoTaskMem(2);
        try
        {
            Marshal.Copy(new byte[size * 2], 0, args, size * 2);
            Marshal.WriteInt16(args, 0, 11); // reversed arguments: Enable VT_BOOL, Command VT_I4
            Marshal.WriteInt16(args, 8, -1);
            Marshal.WriteInt16(args, size, 3);
            Marshal.WriteInt32(args, size + 8, 2);
            var parameters = new System.Runtime.InteropServices.ComTypes.DISPPARAMS { rgvarg = args, cArgs = 2 };
            var invoke = Marshal.GetDelegateForFunctionPointer<DispatchInvoke>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(dispatch), 6 * IntPtr.Size));
            var empty = Guid.Empty;
            Check(invoke(dispatch, 105, ref empty, 0, 1, ref parameters, 0, 0, out _) == 0,
                "native command dispatch succeeds");
            Marshal.Copy(new byte[size * 2], 0, args, size * 2);
            Marshal.WriteInt16(cancel, -1);
            Marshal.WriteInt16(args, 0, 0x400B); // Cancel VT_BYREF|VT_BOOL
            Marshal.WriteIntPtr(args, 8, cancel);
            Marshal.WriteInt16(args, size, 11); // IsChildWindow VT_BOOL false
            Check(invoke(dispatch, 263, ref empty, 0, 1, ref parameters, 0, 0, out _) == 0 &&
                Marshal.ReadInt16(cancel) == -1, "unused native event succeeds without changing cancellation");
        }
        finally { Marshal.FreeCoTaskMem(cancel); Marshal.FreeCoTaskMem(args); Marshal.Release(dispatch); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DispatchInvoke(nint self, int dispid, ref Guid iid, uint lcid, ushort flags,
        ref System.Runtime.InteropServices.ComTypes.DISPPARAMS parameters, nint result, nint exception, out uint argumentError);
}

[ComVisible(true), Guid("B196B284-BAB4-101A-B69C-00AA00341D07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IHistoryTestContainer
{
    [PreserveSig] int EnumConnectionPoints(out nint points);
    [PreserveSig] int FindConnectionPoint(ref Guid iid, out nint point);
}
[ComVisible(true), Guid("B196B286-BAB4-101A-B69C-00AA00341D07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IHistoryTestPoint
{
    [PreserveSig] int GetConnectionInterface(out Guid iid);
    [PreserveSig] int GetConnectionPointContainer(out nint container);
    [PreserveSig] int Advise(nint sink, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int EnumConnections(out nint connections);
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class HistoryTestServer : IHistoryTestContainer, IHistoryTestPoint
{
    public ShellBrowserCommandSink? Sink;
    public int Advised, Unadvised, AdviseResult, UnadviseResult;
    public bool NotifyOnAdvise, NotifyOnUnadvise;
    public int EnumConnectionPoints(out nint points) { points = 0; return unchecked((int)0x80004001); }
    public int FindConnectionPoint(ref Guid iid, out nint point)
    {
        point = 0;
        if (iid != typeof(IShellBrowserCommandEvents).GUID) return unchecked((int)0x80040200);
        point = Marshal.GetComInterfaceForObject(this, typeof(IHistoryTestPoint));
        return 0;
    }
    public int GetConnectionInterface(out Guid iid) { iid = typeof(IShellBrowserCommandEvents).GUID; return 0; }
    public int GetConnectionPointContainer(out nint container)
    { container = Marshal.GetComInterfaceForObject(this, typeof(IHistoryTestContainer)); return 0; }
    public int Advise(nint sink, out uint cookie)
    {
        cookie = 0;
        if (AdviseResult != 0) return AdviseResult;
        Sink = (ShellBrowserCommandSink)Marshal.GetObjectForIUnknown(sink);
        Advised++;
        cookie = 42;
        if (NotifyOnAdvise) Sink.CommandStateChange(2, false);
        return 0;
    }
    public int Unadvise(uint cookie)
    {
        if (cookie != 42) throw new Exception("Wrong cookie");
        Unadvised++;
        if (NotifyOnUnadvise) Sink!.CommandStateChange(1, true);
        return UnadviseResult;
    }
    public int EnumConnections(out nint connections) { connections = 0; return unchecked((int)0x80004001); }
}
