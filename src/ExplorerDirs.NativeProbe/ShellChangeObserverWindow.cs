using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExplorerDirs.NativeProbe;

internal sealed class ShellChangeObserverWindow : NativeWindow, IDisposable
{
    private const int ChangeMessage = 0x8000 + 0x32A;
    private const uint ShellAndInterruptSources = 0x00000003 | 0x00008000;
    private const uint WatchedEvents = 0x00000001 | 0x00000002 | 0x00000004 | 0x00000008 |
                                      0x00001000 | 0x00002000 | 0x00020000;
    private const uint SigDnDesktopAbsoluteParsing = 0x80028000;
    private readonly List<uint> _registrations = [];
    private readonly List<ShellChangeObservation> _observations = [];

    internal IReadOnlyList<ShellChangeObservation> Observations => _observations;

    internal ShellChangeObserverWindow()
    {
        CreateHandle(new CreateParams { Caption = "ExplorerDirs Shell change observer", Parent = new nint(-3) });
    }

    internal void Register(nint folderPidl)
    {
        var entry = new ShellChangeEntry { Pidl = folderPidl, Recursive = true };
        var registration = SHChangeNotifyRegister(Handle, ShellAndInterruptSources, WatchedEvents,
            ChangeMessage, 1, ref entry);
        if (registration == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not register the isolated Shell change observer.");
        _registrations.Add(registration);
    }

    internal void Pump(TimeSpan duration)
    {
        var deadline = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ChangeMessage)
        {
            var locked = SHChangeNotificationLock(message.WParam,
                unchecked((uint)message.LParam.ToInt64()), out var pidls, out var eventId);
            if (locked != 0)
            {
                try
                {
                    var first = pidls == 0 ? 0 : Marshal.ReadIntPtr(pidls);
                    var second = pidls == 0 ? 0 : Marshal.ReadIntPtr(pidls, IntPtr.Size);
                    _observations.Add(new ShellChangeObservation(unchecked((uint)eventId),
                        GetParsingName(first), GetParsingName(second)));
                }
                finally { _ = SHChangeNotificationUnlock(locked); }
            }
        }
        base.WndProc(ref message);
    }

    private static string? GetParsingName(nint pidl)
    {
        if (pidl == 0 || SHGetNameFromIDList(pidl, SigDnDesktopAbsoluteParsing, out var name) < 0) return null;
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    public void Dispose()
    {
        foreach (var registration in _registrations) _ = SHChangeNotifyDeregister(registration);
        _registrations.Clear();
        DestroyHandle();
    }

    internal sealed record ShellChangeObservation(uint EventId, string? FirstParsingName, string? SecondParsingName);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellChangeEntry
    {
        public nint Pidl;
        [MarshalAs(UnmanagedType.Bool)] public bool Recursive;
    }

    [DllImport("shell32.dll")]
    private static extern uint SHChangeNotifyRegister(nint window, uint sources, uint events, int message,
        int entryCount, ref ShellChangeEntry entry);

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Lock")]
    private static extern nint SHChangeNotificationLock(nint change, uint processId, out nint pidls, out int eventId);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotificationUnlock(nint changeLock);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotifyDeregister(uint registration);

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint item, uint sigdnName, out nint name);
}
