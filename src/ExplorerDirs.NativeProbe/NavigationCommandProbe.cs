using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Capability experiment only. No application navigation policy depends on it.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class NavigationCommandProbe : IProbeWebBrowserEvents, IDisposable
{
    private nint _point;
    private uint _cookie;
    private bool _advised;
    internal bool? Back { get; private set; }
    internal bool? Forward { get; private set; }
    internal int Notifications { get; private set; }
    internal ShellHistoryEvents? Production { get; set; }

    internal void Subscribe(nint browser)
    {
        var containerId = new Guid("B196B284-BAB4-101A-B69C-00AA00341D07");
        var eventsId = typeof(IProbeWebBrowserEvents).GUID;
        nint container = 0, sink = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(browser, in containerId, out container));
            Marshal.ThrowExceptionForHR(Method<Find>(container, 4)(container, ref eventsId, out _point));
            sink = Marshal.GetIUnknownForObject(this);
            Marshal.ThrowExceptionForHR(Method<Advise>(_point, 5)(_point, sink, out _cookie));
            _advised = true;
        }
        finally
        {
            if (sink != 0) Marshal.Release(sink);
            if (container != 0) Marshal.Release(container);
        }
    }

    public void CommandStateChange(int command, bool enabled)
    {
        Notifications++;
        if (command == 1) Forward = enabled; // CSC_NAVIGATEFORWARD
        if (command == 2) Back = enabled; // CSC_NAVIGATEBACK
        Console.WriteLine($"command-state-event: command={command}; enabled={enabled}");
    }

    internal void Report(string stage)
    {
        Console.WriteLine($"{stage}/command-state: back={Back?.ToString() ?? "unknown"}; " +
            $"forward={Forward?.ToString() ?? "unknown"}; notifications={Notifications}");
    }

    internal void Check(string stage, bool back, bool forward)
    {
        Report(stage);
        if (Back != back || Forward != forward)
            throw new InvalidOperationException($"Native command state did not match history at {stage}.");
        if (Production is not { Connected: true } || Production.Back != back || Production.Forward != forward ||
            Production.CanNavigate(true, false, false) != back ||
            Production.CanNavigate(false, false, false) != forward)
            throw new InvalidOperationException($"Production history subscriber did not match native state at {stage}.");
        Console.WriteLine($"{stage}/production-history: matched=True");
    }

    public void Dispose()
    {
        if (_point == 0) return;
        try
        {
            if (_advised) Marshal.ThrowExceptionForHR(Method<Unadvise>(_point, 6)(_point, _cookie));
        }
        finally { Marshal.Release(_point); _point = 0; _advised = false; }
        GC.KeepAlive(this);
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

[ComVisible(true), Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D"),
 InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IProbeWebBrowserEvents
{
    [DispId(105)]
    void CommandStateChange(int command, [MarshalAs(UnmanagedType.VariantBool)] bool enabled);
}
