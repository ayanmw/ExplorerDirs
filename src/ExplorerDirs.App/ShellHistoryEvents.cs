using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal sealed class ShellHistoryEvents : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Action _changed;
    private readonly ShellBrowserCommandSink _sink;
    private nint _point;
    private uint _cookie;
    private bool _advised, _disposed;
    internal bool Connected => _advised && !_disposed;
    internal bool? Back { get; private set; }
    internal bool? Forward { get; private set; }
    internal bool HasKnownEmptyHistory => Connected && Back == false && Forward == false;
    internal int LastResult { get; private set; }

    private ShellHistoryEvents(Action changed)
    {
        _changed = changed;
        _sink = new ShellBrowserCommandSink(OnCommand);
    }

    internal static ShellHistoryEvents Subscribe(nint browser, Action changed)
    {
        var result = new ShellHistoryEvents(changed);
        var containerId = new Guid("B196B284-BAB4-101A-B69C-00AA00341D07");
        var eventsId = typeof(IShellBrowserCommandEvents).GUID;
        nint container = 0, sink = 0;
        try
        {
            if ((result.LastResult = Marshal.QueryInterface(browser, in containerId, out container)) < 0 ||
                container == 0) return result;
            if ((result.LastResult = Method<Find>(container, 4)(container, ref eventsId, out result._point)) < 0 ||
                result._point == 0) return result;
            sink = Marshal.GetIUnknownForObject(result._sink);
            result.LastResult = Method<Advise>(result._point, 5)(result._point, sink, out result._cookie);
            result._advised = result.LastResult == 0;
            return result;
        }
        catch (COMException ex) { result.LastResult = ex.HResult; return result; }
        finally
        {
            if (sink != 0) Marshal.Release(sink);
            if (container != 0) Marshal.Release(container);
            if (!result._advised)
            {
                result._sink.Disconnect();
                if (result._point != 0) { Marshal.Release(result._point); result._point = 0; }
                result.Back = result.Forward = null;
            }
        }
    }

    // Native directions are independent and have no navigation request token.
    // Keep notifications received while pending; only gate publication/actions.
    internal bool CanNavigate(bool back, bool pending, bool unavailable) =>
        !_disposed && !pending && !unavailable && ((back ? Back : Forward) ?? true);

    private void OnCommand(int command, bool enabled)
    {
        if (_disposed || Environment.CurrentManagedThreadId != _thread || command is not (1 or 2)) return;
        var previous = command == 1 ? Forward : Back;
        if (command == 1) Forward = enabled;
        else Back = enabled;
        if (previous == enabled) return;
        // Never unwind a managed UI callback through native event delivery.
        try { _changed(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"History state notification: {ex.GetType().Name}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sink.Disconnect(); // A failed Unadvise must not retain a live host callback.
        try
        {
            if (_advised && _point != 0)
                LastResult = Method<Unadvise>(_point, 6)(_point, _cookie);
        }
        catch (COMException ex) { LastResult = ex.HResult; }
        finally
        {
            _advised = false;
            if (_point != 0) { Marshal.Release(_point); _point = 0; }
            GC.KeepAlive(_sink);
        }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Find(nint self, ref Guid iid, out nint point);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Advise(nint self, nint sink, out uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Unadvise(nint self, uint cookie);
}
