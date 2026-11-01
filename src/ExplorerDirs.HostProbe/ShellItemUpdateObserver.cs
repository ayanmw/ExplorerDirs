using System.Runtime.InteropServices;
using System.Windows.Interop;

// Counts Shell item IDs delivered to a hidden registration for one temporary namespace root.
internal sealed class ShellItemUpdateObserver : IDisposable
{
    private static int _nextMessage = 0x8023;
    private const int UpdateItem = 0x00002000;
    private readonly HwndSource _source;
    private readonly uint _registration;
    private readonly int _message;
    private readonly object _sync = new();
    private int _updatedItemPidls;
    private readonly Dictionary<int, int> _eventCounts = new();
    private readonly HashSet<string> _updatedItemNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _updatedItemParsingNames = new(StringComparer.OrdinalIgnoreCase);

    internal int UpdatedItemPidls
    {
        get { lock (_sync) return _updatedItemPidls; }
    }
    internal int TotalEvents { get { lock (_sync) return _eventCounts.Values.Sum(); } }
    internal string[] UpdatedItemNames { get { lock (_sync) return _updatedItemNames.Order(StringComparer.OrdinalIgnoreCase).ToArray(); } }
    internal string[] UpdatedItemParsingNames { get { lock (_sync) return _updatedItemParsingNames.Order(StringComparer.OrdinalIgnoreCase).ToArray(); } }
    internal string EventSummary
    {
        get { lock (_sync) return string.Join(',', _eventCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key:X}:{pair.Value}")); }
    }

    internal ShellItemUpdateObserver(HwndSource source, string parsingName)
    {
        _source = source;
        _message = Interlocked.Increment(ref _nextMessage);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, 0, out var rootPidl, 0, out _));
        try
        {
            _source.AddHook(OnMessage);
            var entry = new Entry { Pidl = rootPidl, Recursive = 1 };
            _registration = SHChangeNotifyRegister(_source.Handle, 0x8000 | 0x1000 | 0x0003,
                0x7FFFFFFF, _message, 1, ref entry);
            if (_registration == 0)
            {
                _source.RemoveHook(OnMessage);
                throw new InvalidOperationException($"SHChangeNotifyRegister failed with {Marshal.GetLastPInvokeError()}.");
            }
        }
        finally { Marshal.FreeCoTaskMem(rootPidl); }
    }

    internal static ShellItemUpdateObserver ForDesktop(HwndSource source)
    {
        Marshal.ThrowExceptionForHR(SHGetSpecialFolderLocation(0, 0, out var desktopPidl));
        try { return new ShellItemUpdateObserver(source, desktopPidl); }
        finally { Marshal.FreeCoTaskMem(desktopPidl); }
    }

    private ShellItemUpdateObserver(HwndSource source, nint rootPidl)
    {
        _source = source;
        _message = Interlocked.Increment(ref _nextMessage);
        _source.AddHook(OnMessage);
        var entry = new Entry { Pidl = rootPidl, Recursive = 1 };
        _registration = SHChangeNotifyRegister(_source.Handle, 0x8000 | 0x1000 | 0x0003,
            0x7FFFFFFF, _message, 1, ref entry);
        if (_registration == 0)
        {
            _source.RemoveHook(OnMessage);
            throw new InvalidOperationException($"SHChangeNotifyRegister failed with {Marshal.GetLastPInvokeError()}.");
        }
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != _message) return 0;
        var locked = SHChangeNotificationLock(wParam, unchecked((uint)lParam), out var pidls, out var eventId);
        if (locked == 0) return 0;
        try
        {
            lock (_sync) _eventCounts[eventId] = _eventCounts.GetValueOrDefault(eventId) + 1;
            if ((eventId & UpdateItem) != 0 && pidls != 0)
            {
                for (var index = 0; index < 2; index++)
                {
                    var pidl = Marshal.ReadIntPtr(pidls, index * IntPtr.Size);
                    if (pidl == 0) continue;
                    lock (_sync) _updatedItemPidls++;
                    if (SHGetNameFromIDList(pidl, 0, out var name) >= 0 && name != 0)
                    {
                        try
                        {
                            var path = Marshal.PtrToStringUni(name);
                            if (!string.IsNullOrWhiteSpace(path))
                                lock (_sync) _updatedItemNames.Add(path);
                        }
                        finally { Marshal.FreeCoTaskMem(name); }
                    }
                    if (SHGetNameFromIDList(pidl, 0x80028000, out var parsingName) >= 0 && parsingName != 0)
                    {
                        try
                        {
                            var value = Marshal.PtrToStringUni(parsingName);
                            if (!string.IsNullOrWhiteSpace(value))
                                lock (_sync) _updatedItemParsingNames.Add(value);
                        }
                        finally { Marshal.FreeCoTaskMem(parsingName); }
                    }
                }
            }
        }
        finally { _ = SHChangeNotificationUnlock(locked); }
        handled = true;
        return 0;
    }

    public void Dispose()
    {
        if (_registration != 0) _ = SHChangeNotifyDeregister(_registration);
        _source.RemoveHook(OnMessage);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Entry { public nint Pidl; public int Recursive; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(
        string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", SetLastError = true)] private static extern uint SHChangeNotifyRegister(
        nint window, int sources, int events, int message, int count, ref Entry entries);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetSpecialFolderLocation(
        nint owner, int folder, out nint item);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Lock")] private static extern nint SHChangeNotificationLock(
        nint change, uint process, out nint pidls, out int events);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Unlock")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHChangeNotificationUnlock(nint locked);
    [DllImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHChangeNotifyDeregister(uint registration);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
}
