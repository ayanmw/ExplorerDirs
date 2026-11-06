using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

// Test-process subscription on its already isolated STA. Never forwards events.
internal sealed class AppSessionShellObserver : IDisposable
{
    private ShellChangeObserverWindow? _window;
    private readonly List<nint> _roots = [];
    private int _logged;
    private bool _comInitialized;
    private int _removedDirectories;

    internal AppSessionShellObserver(IEnumerable<string> roots)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
        _comInitialized = true;
        try
        {
            _window = new ShellChangeObserverWindow();
            _window.Observed += observation =>
            {
                if ((observation.EventId & 0x10) != 0) _removedDirectories++;
                if (++_logged <= 512)
                    Console.WriteLine($"app-shell-observed: event=0x{observation.EventId:X8}; item={Path.GetFileName(observation.FirstParsingName)}; second={Path.GetFileName(observation.SecondParsingName)}; tick={Environment.TickCount64}");
            };
            foreach (var root in roots)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(root, 0, out var pidl, 0, out _));
                _roots.Add(pidl);
                _window.Register(pidl, recursive: false);
            }
            Console.WriteLine($"app-shell-observer-ready: roots={_roots.Count}; recursive=False; forwards=False");
        }
        catch { Dispose(); throw; }
    }

    internal static int RunContract()
    {
        var root = Path.GetFullPath(Path.Combine("src", ".state", "shell-observer-contract", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            using var observer = new AppSessionShellObserver([root]);
            var child = Path.Combine(root, "external-observer-directory");
            Directory.CreateDirectory(child);
            SHChangeNotify(8, 0x2005, child, 0); // Explicit fixture notification; never product recovery.
            observer.Pump();
            Directory.Delete(child);
            SHChangeNotify(0x10, 0x2005, child, 0);
            var deadline = Environment.TickCount64 + 5000;
            while (observer._removedDirectories == 0 && Environment.TickCount64 < deadline)
            {
                observer.Pump();
                Thread.Sleep(10);
            }
            if (observer._removedDirectories == 0)
                throw new InvalidOperationException("Independent Shell observer did not receive explicit directory removal.");
            Console.WriteLine("app-shell-observer-contract: PASS directory removal received and shared notification released");
            return 0;
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal void Pump() => _window?.Pump(TimeSpan.Zero);

    public void Dispose()
    {
        _window?.Dispose();
        _window = null;
        foreach (var root in _roots) Marshal.FreeCoTaskMem(root);
        _roots.Clear();
        if (_comInitialized)
        {
            _comInitialized = false;
            CoUninitialize();
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string path, nint second);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint mask, out uint attributes);
}
