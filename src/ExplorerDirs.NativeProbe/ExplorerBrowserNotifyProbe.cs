using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ExplorerDirs.App;

// Runs on PrivateDesktopShellProbe's separate Desktop. This observes the
// production ExplorerBrowser control tree without installing a product hook.
internal static class ExplorerBrowserNotifyProbe
{
    private static readonly Guid BrowserClass = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid BrowserId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private const uint NotifyMessage = 0x004E;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint RedrawAllChildren = 0x0080;
    private const nuint SubclassId = 0xED5701;
    private static readonly SubclassProc ObserverProc = Observe;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NotifyProbe-" + Guid.NewGuid().ToString("N"));
        var files = new[] { "alpha.txt", "beta.txt", "gamma.bin" };
        nint browser = 0, parent = 0;
        uint initialized = 0;
        var observer = new NotifyObserver();
        try
        {
            Directory.CreateDirectory(fixture);
            foreach (var file in files) File.WriteAllText(Path.Combine(fixture, file), file);

            var browserClass = BrowserClass;
            var browserId = BrowserId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out browser));
            parent = CreateWindowExW(0, "STATIC", "Explorer Dirs isolated color notification probe",
                0x90000000, 20, 20, 820, 620, 0, 0, 0, 0); // WS_POPUP | WS_VISIBLE on the private Desktop.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var bounds = new ProbeRect { Right = 820, Bottom = 620 };
            var settings = new ProbeSettings { Mode = 4 };
            Marshal.ThrowExceptionForHR(At<Initialize>(browser, 3)(browser, parent, ref bounds, ref settings));
            initialized = 1;
            Marshal.ThrowExceptionForHR(At<SetOptions>(browser, 11)(browser, 0x40u | 0x80u));
            Navigate(browser, fixture);
            Pump(900);

            var shellView = GetCurrentShellView(browser);
            try
            {
                Marshal.ThrowExceptionForHR(At<GetWindowHandle>(shellView, 3)(shellView, out var viewWindow));
                observer.AttachTree(parent, viewWindow);
                Console.WriteLine($"explorer-notify-probe: initial-window-classes={ChildClasses(viewWindow)}");

                // Exercise the actual production view-mode setter across the
                // supported Shell modes, then explicitly invalidate the tree.
                ViewStyleProbe.Run(browser);
                observer.AttachTree(parent, viewWindow);
                if (Marshal.QueryInterface(shellView, in FolderView2Id, out var folderView) >= 0 && folderView != 0)
                {
                    try
                    {
                        var select = At<SelectItem>(folderView, 15);
                        _ = select(folderView, 0, 5); // Set first item selected/focused on this private Desktop.
                    }
                    finally { Marshal.Release(folderView); }
                }
                InvalidateTree(viewWindow);
                Pump(900);

                observer.Report("after-view-modes-selection-redraw");
                Console.WriteLine($"explorer-notify-probe: parent-visible={IsWindowVisible(parent)}; " +
                    $"foreground-unchanged={GetForegroundWindow() == foreground}; " +
                    $"shell-view-window-valid={IsWindow(viewWindow)}");
                if (GetForegroundWindow() != foreground)
                    throw new InvalidOperationException("The Private Desktop probe changed the user's foreground window.");
            }
            finally { Marshal.Release(shellView); }
            return 0;
        }
        finally
        {
            Console.WriteLine("explorer-notify-probe: cleanup=observer-detach-start");
            observer.Dispose();
            Console.WriteLine("explorer-notify-probe: cleanup=browser-destroy-start");
            if (initialized != 0)
            {
                var destroy = At<Destroy>(browser, 4)(browser);
                if (destroy < 0) Console.WriteLine($"explorer-notify-probe: browser-destroy=0x{destroy:X8}");
            }
            Console.WriteLine("explorer-notify-probe: cleanup=browser-release-start");
            if (browser != 0) Marshal.Release(browser);
            // DestroyWindow on the ExplorerBrowser parent can block after its
            // native child views have been retired; this probe runs in its own
            // process/desktop, so process exit owns the remaining HWND cleanup.
            Console.WriteLine($"explorer-notify-probe: cleanup=parent-deferred-to-process-exit; valid={parent != 0 && IsWindow(parent)}");
            Console.WriteLine("explorer-notify-probe: cleanup=fixture-delete-start");
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Console.WriteLine("explorer-notify-probe: cleanup=complete");
        }
    }

    private static nint GetCurrentShellView(nint browser)
    {
        var iid = ShellViewId;
        Marshal.ThrowExceptionForHR(At<GetInterface>(browser, 17)(browser, ref iid, out var view));
        return view;
    }

    private static void Navigate(nint browser, string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try { Marshal.ThrowExceptionForHR(At<Browse>(browser, 13)(browser, pidl, 0)); }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static void InvalidateTree(nint window)
    {
        _ = RedrawWindow(window, 0, 0, RedrawInvalidate | RedrawUpdateNow | RedrawAllChildren);
        _ = InvalidateRect(window, 0, true);
        _ = UpdateWindow(window);
    }

    private static string ChildClasses(nint root)
    {
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        _ = EnumChildWindows(root, (window, _) =>
        {
            var value = new StringBuilder(128);
            if (GetClassNameW(window, value, value.Capacity) > 0) classes.Add(value.ToString());
            return true;
        }, 0);
        return string.Join(",", classes);
    }

    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private sealed class NotifyObserver : IDisposable
    {
        private readonly HashSet<nint> _windows = [];
        private readonly Dictionary<(string Receiver, string Sender, int Code), int> _counts = [];
        private readonly GCHandle _self;
        private bool _disposed;

        internal NotifyObserver() => _self = GCHandle.Alloc(this);
        internal int CustomDrawCount => _counts.Where(pair => pair.Key.Code == -12).Sum(pair => pair.Value);

        internal void AttachTree(nint root, nint view)
        {
            Attach(view);
            AttachAncestors(view, root);
            _ = EnumChildWindows(view, (window, _) => { Attach(window); return true; }, 0);
            Attach(root);
        }

        internal void Report(string stage)
        {
            var values = _counts.OrderBy(pair => pair.Key.Receiver, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Code)
                .Select(pair => $"{pair.Key.Receiver}<-{pair.Key.Sender}:code={pair.Key.Code}x{pair.Value}")
                .ToArray();
            Console.WriteLine($"explorer-notify-probe: stage={stage}; notify-count={_counts.Values.Sum()}; " +
                $"nm-customdraw={CustomDrawCount}; notifications={(values.Length == 0 ? "none" : string.Join("|", values))}");
        }

        private void AttachAncestors(nint window, nint stop)
        {
            for (var current = GetParent(window); current != 0; current = GetParent(current))
            {
                Attach(current);
                if (current == stop) break;
            }
        }

        private void Attach(nint window)
        {
            if (window == 0 || !_windows.Add(window)) return;
            if (!SetWindowSubclass(window, ObserverProc, SubclassId, GCHandle.ToIntPtr(_self)))
                _windows.Remove(window);
        }

        internal void Record(nint receiver, nint headerPointer)
        {
            try
            {
                var header = Marshal.PtrToStructure<NotifyHeader>(headerPointer);
                var receiverClass = ClassName(receiver);
                var senderClass = ClassName(header.WindowFrom);
                var key = (receiverClass, senderClass, header.Code);
                _counts[key] = _counts.GetValueOrDefault(key) + 1;
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var window in _windows)
                if (IsWindow(window)) _ = RemoveWindowSubclass(window, ObserverProc, SubclassId);
            _windows.Clear();
            if (_self.IsAllocated) _self.Free();
        }
    }

    private static nint Observe(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if (message == NotifyMessage && lParam != 0 && GCHandle.FromIntPtr(data).Target is NotifyObserver observer)
            observer.Record(window, lParam);
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static string ClassName(nint window)
    {
        if (window == 0) return "(null)";
        var value = new StringBuilder(128);
        return GetClassNameW(window, value, value.Capacity) > 0 ? value.ToString() : "(unknown)";
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)] private struct ProbeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ProbeSettings { public uint Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint WindowFrom; public nuint IdFrom; public int Code; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref ProbeRect rect, ref ProbeSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOptions(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetInterface(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindowHandle(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint hwnd, nint rect, nint updateRegion, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
    private delegate bool EnumChildProc(nint hwnd, nint data);
}
