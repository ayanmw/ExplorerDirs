using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Isolates the native view/list lifetime across refresh and view-mode changes.
// HWND and row-count evidence does not establish what a user sees during painting.
internal static class ShellViewRefreshLifecycleProbe
{
    private const uint ShellDirectoryUpdated = 0x00001000;
    private const uint NoEnumerationRefresh = 0x00080000;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-RefreshLifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        const int initialRows = 300;
        for (var index = 0; index < initialRows; index++)
            File.WriteAllText(Path.Combine(fixture, $"item-{index}.txt"), "fixture");

        nint parent = 0, folderPidl = 0, folder = 0, view = 0, viewWindow = 0, folderView2 = 0, hostPointer = 0;
        StreamViewHost? host = null;
        var created = false;
        try
        {
            parent = CreateWindowExW(0, "STATIC", "Explorer Dirs hidden native refresh lifecycle",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0);
            if (parent == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out folder));
            var viewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref viewId, out view));
            host = new StreamViewHost(parent, []) { View = view };
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            created = true;
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView2));
            if (!WaitForCount(folderView2, initialRows))
                throw new TimeoutException($"Initial stock view enumeration did not settle at {initialRows} fixture rows.");

            var baselineViewWindow = GetViewWindow(view);
            var baselineListView = FindListView(viewWindow);
            if (baselineViewWindow == 0 || baselineViewWindow != viewWindow || baselineListView == 0)
                throw new InvalidOperationException($"Could not identify stock view HWNDs: created=0x{viewWindow:X}; get-window=0x{baselineViewWindow:X}; list-view=0x{baselineListView:X}.");
            Console.WriteLine($"shell-view-refresh-lifecycle: baseline-view=0x{baselineViewWindow:X}; baseline-list=0x{baselineListView:X}; rows={ReadRows(baselineListView)}");

            var modeResults = new List<string>();
            foreach (var mode in new[] { (Value: 3u, Name: "list"), (Value: 2u, Name: "small-icons"), (Value: 4u, Name: "details") })
            {
                Check(At<SetViewMode>(folderView2, 35)(folderView2, mode.Value, 16));
                var modeSamples = CaptureSamples(view, 150);
                modeResults.Add($"{mode.Name}:{Summarize(modeSamples)}");
            }

            var addedPath = Path.Combine(fixture, "added-during-refresh.txt");
            File.WriteAllText(addedPath, "new row");
            Check(At<Refresh>(view, 8)(view));
            var samples = CaptureSamples(view, 1000);
            var refreshSettled = WaitForCount(folderView2, initialRows + 1);
            var minRows = samples.Where(sample => sample.Rows >= 0).Select(sample => sample.Rows).DefaultIfEmpty(-1).Min();
            var zeroRows = samples.Where(sample => sample.Rows == 0).ToArray();
            var hwndChanges = CountHwndChanges(samples);
            var finalViewWindow = GetViewWindow(view);
            var finalListView = FindListView(finalViewWindow);
            var foregroundUnchanged = GetForegroundWindow() == foreground;
            var noChildWindowsAfterDestroy = false;

            Console.WriteLine($"shell-view-refresh-lifecycle: modes=[{string.Join(';', modeResults)}]; " +
                $"refresh-samples={samples.Count}; min-rows={minRows}; settled-{initialRows + 1}-rows={refreshSettled}; " +
                $"empty-samples={zeroRows.Length}; empty-window-ms={(zeroRows.Length == 0 ? 0 : zeroRows[^1].Milliseconds - zeroRows[0].Milliseconds)}; " +
                $"refresh-view=0x{finalViewWindow:X}; refresh-list=0x{finalListView:X}; " +
                $"hwnd-transitions-during-refresh={hwndChanges}; black-paint-measured=False; foreground-unchanged={foregroundUnchanged}");

            File.WriteAllText(Path.Combine(fixture, "added-by-shell-notify.txt"), "notify row");
            SHChangeNotifyPidl(ShellDirectoryUpdated, 0x00002000, folderPidl, 0);
            var notificationSamples = CaptureSamples(view, 1000);
            var notificationSettled = WaitForCount(folderView2, initialRows + 2);
            var notificationMinimum = notificationSamples.Where(sample => sample.Rows >= 0)
                .Select(sample => sample.Rows).DefaultIfEmpty(-1).Min();
            var notificationZeros = notificationSamples.Count(sample => sample.Rows == 0);
            var notificationHwndChanges = CountHwndChanges(notificationSamples);
            Console.WriteLine($"shell-view-refresh-lifecycle: updatedir-notification-samples={notificationSamples.Count}; " +
                $"min-rows={notificationMinimum}; empty-samples={notificationZeros}; " +
                $"settled-{initialRows + 2}-rows={notificationSettled}; hwnd-changes={notificationHwndChanges}; " +
                $"same-view={GetViewWindow(view) == baselineViewWindow}; same-list={FindListView(GetViewWindow(view)) == baselineListView}");

            Check(At<SetFolderFlags>(folderView2, 24)(folderView2, NoEnumerationRefresh, NoEnumerationRefresh));
            var gatedPath = Path.Combine(fixture, "added-with-auto-refresh-disabled.txt");
            File.WriteAllText(gatedPath, "explicit notify row");
            Pump(250);
            var countBeforeExplicitNotification = ReadRows(baselineListView);
            SHChangeNotifyPidl(ShellDirectoryUpdated, 0x00002000, folderPidl, 0);
            var explicitNotificationSamples = CaptureSamples(view, 1000);
            var explicitNotificationSettled = WaitForCount(folderView2, initialRows + 3);
            var explicitNotificationMinimum = explicitNotificationSamples.Where(sample => sample.Rows >= 0)
                .Select(sample => sample.Rows).DefaultIfEmpty(-1).Min();
            var explicitNotificationHwndChanges = CountHwndChanges(explicitNotificationSamples);
            Check(At<SetFolderFlags>(folderView2, 24)(folderView2, NoEnumerationRefresh, 0));
            Console.WriteLine($"shell-view-refresh-lifecycle: noenumrefresh-control={countBeforeExplicitNotification == initialRows + 2}; " +
                $"rows-before-explicit-notify={countBeforeExplicitNotification}; explicit-notify-min-rows={explicitNotificationMinimum}; " +
                $"settled-{initialRows + 3}-rows={explicitNotificationSettled}; " +
                $"hwnd-changes={explicitNotificationHwndChanges}; " +
                $"same-view={GetViewWindow(view) == baselineViewWindow}; same-list={FindListView(GetViewWindow(view)) == baselineListView}");

            Marshal.Release(folderView2);
            folderView2 = 0;
            Check(At<DestroyViewWindow>(view, 10)(view));
            created = false;
            Pump(50);
            noChildWindowsAfterDestroy = CountChildWindows(parent) == 0;
            if (!noChildWindowsAfterDestroy || !foregroundUnchanged)
                throw new InvalidOperationException($"Isolation cleanup failed: child-windows-gone={noChildWindowsAfterDestroy}; foreground-unchanged={foregroundUnchanged}.");
            return refreshSettled && hwndChanges == 0 && notificationSettled && notificationHwndChanges == 0 &&
                countBeforeExplicitNotification == initialRows + 2 && explicitNotificationSettled &&
                explicitNotificationHwndChanges == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"shell-view-refresh-lifecycle: failed={exception.GetType().Name}:{exception.Message}");
            return 1;
        }
        finally
        {
            if (folderView2 != 0) Marshal.Release(folderView2);
            if (created && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
            if (host is not null) host.View = 0;
            if (view != 0) Marshal.Release(view);
            if (hostPointer != 0) Marshal.Release(hostPointer);
            host?.Dispose();
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (parent != 0 && IsWindow(parent)) _ = DestroyWindow(parent);
            try { Directory.Delete(fixture, recursive: true); } catch { }
        }
    }

    private static bool WaitForCount(nint folderView2, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            Application.DoEvents();
            if (At<ItemCount>(folderView2, 7)(folderView2, 2, out var count) >= 0 && count == expected) return true;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    private static void Pump(int milliseconds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static List<(int Milliseconds, int Rows, nint ViewWindow, nint ListView)> CaptureSamples(nint view, int durationMilliseconds)
    {
        var samples = new List<(int Milliseconds, int Rows, nint ViewWindow, nint ListView)>();
        var watch = Stopwatch.StartNew();
        do
        {
            var currentViewWindow = GetViewWindow(view);
            var currentListView = FindListView(currentViewWindow);
            samples.Add((checked((int)watch.ElapsedMilliseconds), currentListView == 0 ? -1 : ReadRows(currentListView),
                currentViewWindow, currentListView));
            Application.DoEvents();
            Thread.Sleep(5);
        } while (watch.ElapsedMilliseconds < durationMilliseconds);
        return samples;
    }

    private static string Summarize(List<(int Milliseconds, int Rows, nint ViewWindow, nint ListView)> samples)
    {
        var zeroRows = samples.Where(sample => sample.Rows == 0).ToArray();
        return $"samples={samples.Count};min-rows={samples.Where(sample => sample.Rows >= 0).Select(sample => sample.Rows).DefaultIfEmpty(-1).Min()};" +
            $"empty-samples={zeroRows.Length};hwnd-changes={CountHwndChanges(samples)};" +
            $"view=0x{samples[^1].ViewWindow:X};list=0x{samples[^1].ListView:X};rows={samples[^1].Rows}";
    }

    private static int CountHwndChanges(List<(int Milliseconds, int Rows, nint ViewWindow, nint ListView)> samples) =>
        samples.Zip(samples.Skip(1), (previous, next) =>
                (ViewChanged: previous.ViewWindow != next.ViewWindow, ListChanged: previous.ListView != next.ListView))
            .Sum(change => (change.ViewChanged ? 1 : 0) + (change.ListChanged ? 1 : 0));

    private static int ReadRows(nint listView) => unchecked((int)SendMessageW(listView, 0x1004, 0, 0)); // LVM_GETITEMCOUNT

    private static nint GetViewWindow(nint view)
    {
        Check(At<GetWindow>(view, 3)(view, out var window));
        return window;
    }

    private static nint FindListView(nint root)
    {
        nint result = 0;
        _ = EnumChildWindows(root, (window, _) =>
        {
            var className = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, className, className.Capacity) > 0 && className.ToString() == "SysListView32")
            {
                result = window;
                return false;
            }
            return true;
        }, 0);
        return result;
    }

    private static int CountChildWindows(nint parent)
    {
        var count = 0;
        _ = EnumChildWindows(parent, (_, _) => { count++; return true; }, 0);
        return count;
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    [StructLayout(LayoutKind.Sequential)] private struct StreamProbeSettings { public uint Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct StreamProbeRect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect rect, out nint hwnd);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint hwnd);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Refresh(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewMode(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderFlags(nint self, uint mask, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool EnumChildProc(nint hwnd, nint parameter);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")] private static extern void SHChangeNotifyPidl(uint eventId, uint flags, nint item1, nint item2);
}
