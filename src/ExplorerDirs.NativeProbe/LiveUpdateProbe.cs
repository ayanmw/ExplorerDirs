using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Background evidence only: no visible parent, input, clipboard or activation.
// Compare four independently hosted native views with and without the production site.
internal static class LiveUpdateProbe
{
    private static bool wpfPump;
    internal static int Run(bool useWpfPump = false)
        => RunModes(useWpfPump, [("no-site", 0), ("production-site", 1)]);

    internal static int RunViewFlagMatrix(bool useWpfPump = false)
        => RunModes(useWpfPump, [("no-site", 0), ("production-noincludeitem", 1), ("production-flags-zero", 2)]);

    private static int RunModes(bool useWpfPump, (string Name, int SiteMode)[] modes)
    {
        wpfPump = useWpfPump;
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var succeeded = true;
        foreach (var mode in modes)
        {
            var panes = new List<Pane>();
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    var folder = Path.Combine(root, $"{mode.Name}-{i}");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "initial.txt"), "initial");
                    panes.Add(new Pane(folder, mode.SiteMode));
                }
                PumpUntil(() => panes.All(p => p.Count() == 1), 5000);
                foreach (var pane in panes)
                    if (pane.Count() != 1) throw new Exception("Initial native enumeration did not settle.");
                foreach (var pane in panes)
                    File.WriteAllText(Path.Combine(pane.Folder, "created.txt"), "created");
                var elapsed = PumpUntil(() => panes.All(p => p.Count() == 2), 5000);
                Console.WriteLine($"live-create site={mode.Name} counts={string.Join(',', panes.Select(p => p.Count()))} flag-calls={string.Join(',', panes.Select(p => p.ViewFlagRequests))} elapsed-ms={elapsed}");
                var created = panes.All(p => p.Count() == 2);
                // Only delete files created by this probe in its unique fixture directory.
                foreach (var pane in panes) File.Delete(Path.Combine(pane.Folder, "initial.txt"));
                elapsed = PumpUntil(() => created && panes.All(p => p.Count() == 1), 5000);
                Console.WriteLine($"live-delete site={mode.Name} counts={string.Join(',', panes.Select(p => p.Count()))} flag-calls={string.Join(',', panes.Select(p => p.ViewFlagRequests))} elapsed-ms={elapsed}");
                succeeded &= created && panes.All(p => p.Count() == 1);
                foreach (var pane in panes)
                    if (IsWindowVisible(pane.Parent)) throw new Exception("Hidden parent unexpectedly visible.");
            }
            finally { foreach (var pane in panes) pane.Dispose(); }
        }
        Console.WriteLine("Observation complete; hidden counts do not establish visible GUI acceptance.");
        return succeeded ? 0 : 1;
    }

    private static long PumpUntil(Func<bool> condition, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        if (wpfPump)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (condition() || clock.ElapsedMilliseconds >= milliseconds) frame.Continue = false;
            };
            timer.Start();
            try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            return clock.ElapsedMilliseconds;
        }
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            System.Windows.Forms.Application.DoEvents();
            if (condition()) break;
            Thread.Sleep(10);
        }
        return clock.ElapsedMilliseconds;
    }

    private sealed class Pane : IDisposable
    {
        internal readonly string Folder;
        internal nint Parent;
        private nint browser;
        private bool initialized;
        private readonly object? site;
        internal int ViewFlagRequests => site is NoIncludeItemBrowserSite probeSite ? probeSite.ViewFlagRequests : -1;

        internal Pane(string folder, int siteMode)
        {
            Folder = folder;
            try
            {
                var clsid = new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
                var iid = new Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
                Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out browser));
                if (siteMode != 0)
                {
                    site = siteMode == 1 ? new ShellBrowserSite() : new NoIncludeItemBrowserSite();
                    Check(IUnknown_SetSite(browser, site));
                }
                Parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden live-update probe",
                    0x80000000, 0, 0, 640, 480, 0, 0, 0, 0);
                if (Parent == 0) throw new Exception("Could not create hidden parent.");
                var rect = new Rect { Right = 640, Bottom = 480 };
                var settings = new Settings { Mode = 4 };
                Check(At<Initialize>(browser, 3)(browser, Parent, ref rect, ref settings));
                initialized = true;
                Check(At<Options>(browser, 11)(browser, 0x40 | 0x80));
                Check(SHParseDisplayName(folder, 0, out var pidl, 0, out _));
                try { Check(At<Browse>(browser, 13)(browser, pidl, 0)); }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            catch { Dispose(); throw; }
        }

        internal int Count()
        {
            var iid = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
            if (At<GetInterface>(browser, 17)(browser, ref iid, out var view) < 0) return -1;
            try
            {
                var hr = At<ItemCount>(view, 7)(view, 2, out var count);
                return hr < 0 ? -1 : count;
            }
            finally { Marshal.Release(view); }
        }

        public void Dispose()
        {
            if (browser != 0)
            {
                if (initialized) At<Destroy>(browser, 4)(browser);
                IUnknown_SetSite(browser, null);
                Marshal.Release(browser);
                browser = 0;
            }
            if (Parent != 0) { DestroyWindow(Parent); Parent = 0; }
            GC.KeepAlive(site);
        }
    }

    private static T At<T>(nint obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Settings { public uint Mode, Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref Rect rect, ref Settings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Options(nint self, uint options);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetInterface(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shlwapi.dll")] private static extern int IUnknown_SetSite(nint browser, [MarshalAs(UnmanagedType.IUnknown)] object? site);
}
