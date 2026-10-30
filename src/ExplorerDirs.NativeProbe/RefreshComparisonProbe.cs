using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Hidden-host comparison only: row counts cannot establish whether either route reduces visible blanking.
internal static class RefreshComparisonProbe
{
    private const uint ShellDirectoryUpdated = 0x00001000;
    private const uint PathAndFlushNoWait = 0x00002005;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RefreshComparison-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var succeeded = true;
            foreach (var route in new[] { "baseline", "updatedir", "ishellview", "ishellview-redraw-suppressed" })
            {
                var folder = Path.Combine(root, route);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "initial-1.txt"), "one");
                File.WriteAllText(Path.Combine(folder, "initial-2.txt"), "two");
                using var view = new HiddenShellView(folder);
                if (!PumpUntil(() => view.Count() == 2, 5000))
                    throw new TimeoutException($"{route}: initial Shell enumeration did not settle.");

                File.WriteAllText(Path.Combine(folder, "added.txt"), "external change");
                var elapsed = Stopwatch.StartNew();
                if (route == "updatedir") SHChangeNotify(ShellDirectoryUpdated, PathAndFlushNoWait, folder, 0);
                else if (route.StartsWith("ishellview", StringComparison.Ordinal))
                {
                    using var redraw = route.EndsWith("redraw-suppressed", StringComparison.Ordinal)
                        ? view.SuppressRedraw() : null;
                    view.Refresh();
                    var settledWhileSuppressed = PumpUntil(() => view.Count() == 3, 5000);
                    redraw?.Dispose();
                    elapsed.Stop();
                    var countWhileSuppressed = view.Count();
                    Console.WriteLine($"refresh-route={route}; settled={settledWhileSuppressed}; rows={countWhileSuppressed}; elapsed-ms={elapsed.ElapsedMilliseconds}; hidden-parent={ !IsWindowVisible(view.Parent) }");
                    succeeded &= settledWhileSuppressed && countWhileSuppressed == 3 && !IsWindowVisible(view.Parent);
                    continue;
                }
                var settled = PumpUntil(() => view.Count() == 3, 5000);
                elapsed.Stop();
                var count = view.Count();
                Console.WriteLine($"refresh-route={route}; settled={settled}; rows={count}; elapsed-ms={elapsed.ElapsedMilliseconds}; hidden-parent={ !IsWindowVisible(view.Parent) }");
                succeeded &= settled && count == 3 && !IsWindowVisible(view.Parent);
            }

            var foregroundUnchanged = GetForegroundWindow() == foreground;
            Console.WriteLine($"refresh-comparison; foreground-unchanged={foregroundUnchanged}; visible-flicker-measured=false; sorting-grouping-selection-measured=false");
            return succeeded && foregroundUnchanged ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static bool PumpUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            Application.DoEvents();
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private sealed class HiddenShellView : IDisposable
    {
        private nint _browser;
        private bool _initialized;

        internal nint Parent { get; }

        internal HiddenShellView(string folder)
        {
            var browserClass = new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
            var browserInterface = new Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserInterface, out _browser));
            Parent = CreateWindowExW(0, "STATIC", "Explorer Dirs hidden refresh comparison",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0);
            if (Parent == 0) throw new InvalidOperationException("Could not create hidden Shell host.");
            var rect = new Rect { Right = 640, Bottom = 480 };
            var settings = new BrowserSettings { Mode = 4 };
            Marshal.ThrowExceptionForHR(At<Initialize>(_browser, 3)(_browser, Parent, ref rect, ref settings));
            _initialized = true;
            Marshal.ThrowExceptionForHR(At<Options>(_browser, 11)(_browser, 0x40 | 0x80));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(folder, 0, out var pidl, 0, out _));
            try { Marshal.ThrowExceptionForHR(At<Browse>(_browser, 13)(_browser, pidl, 0)); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }

        internal int Count()
        {
            var folderViewInterface = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
            Marshal.ThrowExceptionForHR(At<GetInterface>(_browser, 17)(_browser, ref folderViewInterface, out var view));
            try
            {
                var result = At<ItemCount>(view, 7)(view, 2, out var count);
                Marshal.ThrowExceptionForHR(result);
                return count;
            }
            finally { Marshal.Release(view); }
        }

        internal IDisposable? SuppressRedraw()
        {
            var iid = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
            Marshal.ThrowExceptionForHR(At<GetInterface>(_browser, 17)(_browser, ref iid, out var view));
            try { return ExplorerDirs.App.ShellViewRedrawLease.TrySuppress(view); }
            finally { Marshal.Release(view); }
        }

        internal void Refresh()
        {
            var viewId = new Guid("000214E3-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(At<GetInterface>(_browser, 17)(_browser, ref viewId, out var view));
            try { Marshal.ThrowExceptionForHR(At<ShellRefresh>(view, 8)(view)); }
            finally { Marshal.Release(view); }
        }

        public void Dispose()
        {
            if (_browser != 0)
            {
                if (_initialized) At<Destroy>(_browser, 4)(_browser);
                Marshal.Release(_browser);
                _browser = 0;
            }
            if (Parent != 0) DestroyWindow(Parent);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BrowserSettings { public uint Mode, Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref Rect rect, ref BrowserSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Options(nint self, uint options);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetInterface(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ShellRefresh(nint self);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string item1, nint item2);
}
