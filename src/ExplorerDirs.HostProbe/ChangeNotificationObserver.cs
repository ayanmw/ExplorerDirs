using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Interop;
using System.Diagnostics;
using System.Globalization;

// Observes Shell delivery to our hidden probe window. Never refreshes a view.
internal sealed class ChangeNotificationObserver : IDisposable
{
    private const int Message = 0x8021;
    private readonly HwndSource source;
    private readonly string root;
    private readonly uint registration;
    private readonly Dictionary<int, int> counts = new();
    private readonly Dictionary<string, int> targets = new();
    private readonly Stopwatch clock;
    private readonly bool resolveTargetPaths;
    private readonly List<string> timeline = new();
    private readonly object summaryLock = new();
    private const int RegistrationSources = 0x8000 | 0x1000 | 3;
    private static int RegistrationEvents => int.TryParse(
        Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_EVENT_MASK"),
        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var configured)
        ? configured : 0x7FFFFFFF;
    private int omitted;
    private int lockFailures;
    private bool disposed;
    internal bool DeregistrationSucceeded { get; private set; } = true;
    internal string Summary
    {
        get
        {
            lock (summaryLock)
                return string.Join(',', counts.OrderBy(p => p.Key).Select(p => $"{p.Key:X}:{p.Value}")) + $"; lock-failures={lockFailures}; targets=" + string.Join(',', targets.Select(p => $"{p.Key}:{p.Value}")) + "; timeline-ms=" + string.Join(',', timeline) + $"; omitted={omitted}";
        }
    }
    internal int TargetCount(string target)
    {
        lock (summaryLock) return targets.GetValueOrDefault(target);
    }

    internal ChangeNotificationObserver(HwndSource source, string root, Stopwatch clock, bool resolveTargetPaths = true)
    {
        this.source = source;
        this.root = Path.GetFullPath(root);
        this.clock = clock;
        this.resolveTargetPaths = resolveTargetPaths;
        Marshal.ThrowExceptionForHR(SHParseDisplayName(root, 0, out var pidl, 0, out _));
        try
        {
            source.AddHook(OnMessage);
            var entry = new Entry { Pidl = pidl, Recursive = 1 };
            registration = SHChangeNotifyRegister(source.Handle, RegistrationSources,
                RegistrationEvents, Message, 1, ref entry);
            if (registration == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                source.RemoveHook(OnMessage);
                throw new Exception($"Shell change registration failed: hwnd=0x{source.Handle:X}, root='{this.root}', " +
                    $"pidl=0x{pidl:X}, sources=0x{RegistrationSources:X}, events=0x{RegistrationEvents:X}, " +
                    $"message=0x{Message:X}, entries=1, last-error={error}.");
            }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != Message) return 0;
        var receivedAt = clock.ElapsedMilliseconds;
        var locked = SHChangeNotification_Lock(wParam, unchecked((uint)lParam), out var pidls, out var change);
        if (locked != 0)
        {
            try
            {
                var target = resolveTargetPaths ? Target(pidls == 0 ? 0 : Marshal.ReadIntPtr(pidls)) : "path-omitted";
                var label = $"{change:X}/{target}";
                // Resolve the PIDL before taking the snapshot lock: Shell name
                // resolution can pump messages, and the main thread reads Summary.
                lock (summaryLock)
                {
                    counts[change] = counts.GetValueOrDefault(change) + 1;
                    targets[label] = targets.GetValueOrDefault(label) + 1;
                    if (timeline.Count < 64) timeline.Add($"{receivedAt}/{label}");
                    else omitted++;
                }
            }
            finally { SHChangeNotification_Unlock(locked); }
        }
        else { lock (summaryLock) lockFailures++; }
        handled = true;
        return 0;
    }

    private string Target(nint pidl)
    {
        if (pidl == 0 || SHGetNameFromIDList(pidl, 0x80058000, out var name) < 0) return "unknown";
        try
        {
            var path = Marshal.PtrToStringUni(name);
            // Log only the generated fixture-relative identity; suppress everything else.
            return path is not null && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(root, path) : "root-or-other";
        }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (registration != 0) DeregistrationSucceeded = SHChangeNotifyDeregister(registration);
        source.RemoveHook(OnMessage);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Entry { public nint Pidl; public int Recursive; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll", SetLastError = true)] private static extern uint SHChangeNotifyRegister(nint hwnd, int sources, int events, uint message, int count, ref Entry entry);
    [DllImport("shell32.dll")] private static extern bool SHChangeNotifyDeregister(uint registration);
    [DllImport("shell32.dll")] private static extern nint SHChangeNotification_Lock(nint change, uint process, out nint pidls, out int events);
    [DllImport("shell32.dll")] private static extern bool SHChangeNotification_Unlock(nint locked);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(nint pidl, uint kind, out nint name);
}
