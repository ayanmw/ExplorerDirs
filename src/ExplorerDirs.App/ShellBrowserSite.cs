using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ExplorerDirs.App;

// DefView asks the ExplorerBrowser frame for ICommDlgBrowser2. Without the
// NOINCLUDEITEM flag, search-folder filtering may run on the UI thread.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class ShellBrowserSite : IShellServiceProvider, IComDialogBrowser, IComDialogBrowser2, IComDialogBrowser3, IExplorerPaneVisibility
{
    internal static readonly Guid BrowserFrameServiceId = new("000214F1-0000-0000-C000-000000000046");
    internal static readonly Guid DialogBrowser2Id = new("10339516-2894-11D2-9039-00C04F8EEB3E");
    internal static readonly Guid ExplorerPaneVisibilityServiceId = new("E07010EC-BC17-44C0-97B0-46C7C95B9EDC");
    internal const uint NoIncludeItemFlag = 0x00000010;
    internal ExplorerShellPane VisibleShellPane { get; set; }
    internal bool QuickFilterActive { get; set; }
    internal long SelectionVersion { get; private set; }
    internal event Action<IntPtr>? SelectionChanged;
    private readonly bool _traceStateChanges = Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE") == "1" ||
        Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING") == "1";
    private readonly bool _traceCallbacks = Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING") == "1";
    private readonly Dictionary<uint, int> _stateChanges = [];
    private readonly Dictionary<uint, int> _notifications = [];
    private readonly List<string> _stateChangeTraceSamples = [];
    private long _stateChangeTraceStart = Stopwatch.GetTimestamp();
    private readonly ShellCallbackTiming _defaultCommandTiming = new();
    private readonly ShellCallbackTiming _stateChangeTiming = new();
    private readonly ShellCallbackTiming _includeObjectTiming = new();
    private readonly ShellCallbackTiming _notifyTiming = new();
    private readonly ShellCallbackTiming _defaultMenuTextTiming = new();
    private readonly ShellCallbackTiming _viewFlagsTiming = new();
    private readonly ShellCallbackTiming _columnClickedTiming = new();
    private readonly ShellCallbackTiming _currentFilterTiming = new();
    private readonly ShellCallbackTiming _previewCreatedTiming = new();
    internal string NotificationSnapshot =>
        "state=" + string.Join(',', _stateChanges.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}")) +
        "; notify=" + string.Join(',', _notifications.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
    internal string StateChangeTraceSnapshot => string.Join('|', _stateChangeTraceSamples);

    internal void ResetStateChangeTrace()
    {
        _stateChanges.Clear();
        _stateChangeTraceSamples.Clear();
        _stateChangeTraceStart = Stopwatch.GetTimestamp();
    }
    internal string CallbackTimingSnapshot => string.Join(';', new[]
    {
        $"default={_defaultCommandTiming.Snapshot}", $"state={_stateChangeTiming.Snapshot}",
        $"include={_includeObjectTiming.Snapshot}", $"notify={_notifyTiming.Snapshot}",
        $"menu-text={_defaultMenuTextTiming.Snapshot}", $"view-flags={_viewFlagsTiming.Snapshot}",
        $"column-click={_columnClickedTiming.Snapshot}", $"current-filter={_currentFilterTiming.Snapshot}",
        $"preview-created={_previewCreatedTiming.Snapshot}"
    });

    internal void ResetCallbackTimings()
    {
        _defaultCommandTiming.Reset();
        _stateChangeTiming.Reset();
        _includeObjectTiming.Reset();
        _notifyTiming.Reset();
        _defaultMenuTextTiming.Reset();
        _viewFlagsTiming.Reset();
        _columnClickedTiming.Reset();
        _currentFilterTiming.Reset();
        _previewCreatedTiming.Reset();
    }

    public int QueryService(ref Guid service, ref Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (service != BrowserFrameServiceId && service != ExplorerPaneVisibilityServiceId)
            return unchecked((int)0x80004002); // E_NOINTERFACE
        var unknown = Marshal.GetIUnknownForObject(this);
        try
        {
            var hr = Marshal.QueryInterface(unknown, in iid, out result);
            return hr;
        }
        finally { Marshal.Release(unknown); }
    }

    public int GetPaneState(ref Guid pane, out uint state)
    {
        state = ExplorerPaneVisibility.GetState(pane, VisibleShellPane);
        return 0;
    }

    // S_FALSE leaves file opening and context-menu text to the native view.
    public int OnDefaultCommand(IntPtr shellView)
    {
        var startedAt = _traceCallbacks ? _defaultCommandTiming.Start() : 0;
        try { return 1; }
        finally { _defaultCommandTiming.Stop(startedAt); }
    }
    public int OnStateChange(IntPtr shellView, uint change)
    {
        var startedAt = _traceCallbacks ? _stateChangeTiming.Start() :
            _traceStateChanges ? Stopwatch.GetTimestamp() : 0;
        var traceSampleIndex = -1;
        try
        {
            // Event counts are useful for low-overhead traces even when per-callback timing is disabled.
            if (_traceStateChanges)
            {
                _stateChanges[change] = _stateChanges.GetValueOrDefault(change) + 1;
                if (_stateChangeTraceSamples.Count < 128)
                {
                    traceSampleIndex = _stateChangeTraceSamples.Count;
                    _stateChangeTraceSamples.Add(string.Empty);
                }
            }
            if (change is 2 or 3 or 4) SelectionVersion++; // CDBOSC_SELCHANGE / RENAME / STATECHANGE
            if (change == 2) SelectionChanged?.Invoke(shellView);
            return 0;
        }
        finally
        {
            _stateChangeTiming.Stop(_traceCallbacks ? startedAt : 0);
            if (traceSampleIndex >= 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                _stateChangeTraceSamples[traceSampleIndex] =
                    $"t={Stopwatch.GetElapsedTime(_stateChangeTraceStart, startedAt).TotalMilliseconds:F1}ms,change={change},start-ticks={startedAt},elapsed-ms={elapsed:F3}";
            }
        }
    }
    public int IncludeObject(IntPtr shellView, IntPtr childPidl)
    {
        var startedAt = _traceCallbacks ? _includeObjectTiming.Start() : 0;
        try { return 0; }
        finally { _includeObjectTiming.Stop(startedAt); }
    }
    public int Notify(IntPtr shellView, uint notification)
    {
        var startedAt = _traceCallbacks ? _notifyTiming.Start() : 0;
        try
        {
            if (_traceCallbacks) _notifications[notification] = _notifications.GetValueOrDefault(notification) + 1;
            return 0;
        }
        finally { _notifyTiming.Stop(startedAt); }
    }
    public int GetDefaultMenuText(IntPtr shellView, IntPtr text, int capacity)
    {
        var startedAt = _traceCallbacks ? _defaultMenuTextTiming.Start() : 0;
        try { return 1; }
        finally { _defaultMenuTextTiming.Stop(startedAt); }
    }
    public int GetViewFlags(out uint flags)
    {
        var startedAt = _traceCallbacks ? _viewFlagsTiming.Start() : 0;
        try
        {
            // The native folder filter is bypassed when NOINCLUDEITEM is set; let
            // DefView consult the filter for ordinary filtered directories.
            flags = QuickFilterActive ? 0 : NoIncludeItemFlag;
            return 0;
        }
        finally { _viewFlagsTiming.Stop(startedAt); }
    }

    // Expose the complete modern contract, including its inherited slots.
    // Some initial live-update comparisons improved with v3, but the exact release
    // still has counterexamples: this contract alone is not a notification fix.
    public int OnColumnClicked(IntPtr shellView, int column)
    {
        var startedAt = _traceCallbacks ? _columnClickedTiming.Start() : 0;
        try { return 0; }
        finally { _columnClickedTiming.Stop(startedAt); }
    }
    public int GetCurrentFilter(IntPtr text, int capacity)
    {
        var startedAt = _traceCallbacks ? _currentFilterTiming.Start() : 0;
        try
        {
            if (text == IntPtr.Zero || capacity <= 0) return unchecked((int)0x80070057);
            Marshal.WriteInt16(text, 0); // No host filename filter; always terminate the output.
            return 0;
        }
        finally { _currentFilterTiming.Stop(startedAt); }
    }
    public int OnPreViewCreated(IntPtr shellView)
    {
        var startedAt = _traceCallbacks ? _previewCreatedTiming.Start() : 0;
        try { return 0; }
        finally { _previewCreatedTiming.Stop(startedAt); }
    }

    internal static void SetSite(object browser, ShellBrowserSite? site)
    {
        var hr = IUnknown_SetSite(browser, site);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("shlwapi.dll", PreserveSig = true)]
    private static extern int IUnknown_SetSite(
        [MarshalAs(UnmanagedType.IUnknown)] object browser,
        [MarshalAs(UnmanagedType.IUnknown)] object? site);

    private sealed class ShellCallbackTiming
    {
        private long _calls;
        private long _samples;
        private long _sampledTicks;
        private long _maxTicks;

        internal string Snapshot
        {
            get
            {
                var samples = Interlocked.Read(ref _samples);
                var sampledTicks = Interlocked.Read(ref _sampledTicks);
                var maxTicks = Interlocked.Read(ref _maxTicks);
                return $"calls={Interlocked.Read(ref _calls)},samples={samples},sampled-ms={ToMilliseconds(sampledTicks):F3},max-ms={ToMilliseconds(maxTicks):F3}";
            }
        }

        internal long Start()
        {
            Interlocked.Increment(ref _calls);
            // This is enabled only by opt-in diagnostics; time every callback to catch rare stalls.
            return Stopwatch.GetTimestamp();
        }

        internal void Stop(long startedAt)
        {
            if (startedAt == 0) return;
            var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            Interlocked.Increment(ref _samples);
            Interlocked.Add(ref _sampledTicks, elapsedTicks);
            long observed;
            do
            {
                observed = Interlocked.Read(ref _maxTicks);
                if (elapsedTicks <= observed) return;
            } while (Interlocked.CompareExchange(ref _maxTicks, elapsedTicks, observed) != observed);
        }

        internal void Reset()
        {
            Interlocked.Exchange(ref _calls, 0);
            Interlocked.Exchange(ref _samples, 0);
            Interlocked.Exchange(ref _sampledTicks, 0);
            Interlocked.Exchange(ref _maxTicks, 0);
        }

        private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }
}

[ComVisible(true), Guid("E07010EC-BC17-44C0-97B0-46C7C95B9EDC"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerPaneVisibility
{
    [PreserveSig] int GetPaneState(ref Guid pane, out uint state);
}

[ComVisible(true), Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
}

[ComVisible(true), Guid("000214F1-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IComDialogBrowser
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView, uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView, IntPtr childPidl);
}

[ComVisible(true), Guid("10339516-2894-11D2-9039-00C04F8EEB3E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IComDialogBrowser2
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView, uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView, IntPtr childPidl);
    [PreserveSig] int Notify(IntPtr shellView, uint notification);
    [PreserveSig] int GetDefaultMenuText(IntPtr shellView, IntPtr text, int capacity);
    [PreserveSig] int GetViewFlags(out uint flags);
}

[ComVisible(true), Guid("c8ad25a1-3294-41ee-8165-71174bd01c57"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IComDialogBrowser3
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView, uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView, IntPtr childPidl);
    [PreserveSig] int Notify(IntPtr shellView, uint notification);
    [PreserveSig] int GetDefaultMenuText(IntPtr shellView, IntPtr text, int capacity);
    [PreserveSig] int GetViewFlags(out uint flags);
    [PreserveSig] int OnColumnClicked(IntPtr shellView, int column);
    [PreserveSig] int GetCurrentFilter(IntPtr text, int capacity);
    [PreserveSig] int OnPreViewCreated(IntPtr shellView);
}
