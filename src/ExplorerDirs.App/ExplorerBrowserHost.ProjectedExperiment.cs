#if EXPLORERDIRS_PROJECTED_NOTIFY_EXPERIMENT
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ExplorerDirs.NativeProbe;

namespace ExplorerDirs.App;

// Build-only experiment. Requires the matching prototype native DLL; absent
// capability leaves the normal monitor in place. No release enables this flag.
internal sealed partial class ExplorerBrowserHost
{
    private ProjectedChangeLedger? _projectedQueue;
    private FileSystemWatcher? _projectedWatcher;
    private DispatcherTimer? _projectedTimer;
    private nint _projectedBridge;
    private static readonly Guid ProjectedBridgeExperimentId = new("412ED4D4-9750-4525-8C90-345CBDB5FE95");

    private bool TryStartProjectedMonitorExperiment(string path, long navigationVersion)
    {
        if (nint.Size != 8) return false;
        var monitorVersion = _directoryChangeVersion;
        var view = GetFolderView2Pointer();
        if (view == 0) return false;
        nint bridge = 0;
        try
        {
            var id = ProjectedBridgeExperimentId;
            if (GetMethod<ProjectedGetFolder>(view, 5)(view, ref id, out bridge) != 0) return false;
        }
        finally { Marshal.Release(view); }
        if (monitorVersion != _directoryChangeVersion || navigationVersion != _viewReadiness.NavigationVersion ||
            !_viewReadiness.IsCurrent(navigationVersion) ||
            !string.Equals(path, _completedDiskDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Marshal.Release(bridge);
            return true; // The newer navigation owns its own monitor.
        }
        var queue = new ProjectedChangeLedger();
        var dispatch = new ProjectedChangeDispatch(queue, 8);
        var viewWindow = _viewWindow;
        Exception? watcherError = null;
        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = ShellDirectoryChangeMonitorOptions.NotifyFilter,
                InternalBufferSize = 16 * 1024
            };
        }
        catch { Marshal.Release(bridge); throw; }
        watcher.Created += (_, e) => queue.Record(ProjectedChangeKind.Created, e.Name!);
        watcher.Changed += (_, e) => queue.Record(ProjectedChangeKind.Changed, e.Name!);
        watcher.Deleted += (_, e) => queue.Record(ProjectedChangeKind.Deleted, e.Name!);
        watcher.Renamed += (_, e) => queue.Record(ProjectedChangeKind.Renamed, e.OldName!, e.Name);
        watcher.Error += (_, e) => Interlocked.Exchange(ref watcherError, e.GetException());
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var progress = System.Diagnostics.Stopwatch.StartNew();
        uint? enumerationBaseline = null;
        uint lastEnumerations = 0;
        var dispatched = 0;
        var lastLoggedDispatch = 0;
        var lastLoggedSettled = 0;
        bool Current() => ReferenceEquals(_projectedQueue, queue) &&
            _directoryChangeVersion == monitorVersion && _viewReadiness.NavigationVersion == navigationVersion &&
            _viewReadiness.IsCurrent(navigationVersion) && _viewWindow == viewWindow &&
            string.Equals(_completedDiskDirectory, path, StringComparison.OrdinalIgnoreCase);
        timer.Tick += (_, _) =>
        {
            if (!Current()) { queue.Invalidate(); timer.Stop(); return; }
            // Navigation can synchronously dispose the owner during any Shell call.
            // Keep this turn's capability alive until Advance has stopped using it.
            Marshal.AddRef(bridge);
            try
            {
                if (Volatile.Read(ref watcherError) is { } error) throw error;
                if (dispatch.PendingCount > 0 && progress.Elapsed > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("Projected experiment confirmation timed out.");
                dispatch.Advance(Current,
                    work =>
                    {
                        progress.Restart();
                        if (enumerationBaseline is null)
                        {
                            Marshal.ThrowExceptionForHR(GetMethod<ProjectedEnumerationCount>(bridge, 5)(bridge, out var initial));
                            if (!Current()) return new ProjectedDispatchResult(0, Reconciled: true);
                            enumerationBaseline = initial;
                        }
                        dispatched++;
                        ulong ticket = 0;
                        var result = work.Kind switch
                        {
                            ProjectedChangeKind.Created or ProjectedChangeKind.Changed => GetMethod<ProjectedArrival>(bridge, 8)(bridge, work.Name),
                            ProjectedChangeKind.Deleted => GetMethod<ProjectedRemoval>(bridge, 4)(bridge, work.Name, out ticket),
                            _ => GetMethod<ProjectedRename>(bridge, 9)(bridge, work.Name, work.NewName!, out ticket)
                        };
                        if (result == 1 && work.Kind == ProjectedChangeKind.Deleted && Current())
                        {
                            // Native refusal confirms the path exists again. Publish its
                            // current facts; never acknowledge the obsolete removal.
                            var reconciled = GetMethod<ProjectedArrival>(bridge, 8)(bridge, work.Name);
                            if (reconciled != 0) throw new InvalidOperationException($"Projected recreate: 0x{reconciled:X8}");
                            return new ProjectedDispatchResult(0, Reconciled: true);
                        }
                        if (result != 0) throw new InvalidOperationException($"Projected {work.Kind}: 0x{result:X8}");
                        return new ProjectedDispatchResult(ticket);
                    },
                    work => work.Kind switch
                    {
                        ProjectedChangeKind.Created => ProjectedRowExists(viewWindow, work.Name, Current),
                        ProjectedChangeKind.Deleted => !ProjectedRowExists(viewWindow, work.Name, Current),
                        ProjectedChangeKind.Renamed => !ProjectedRowExists(viewWindow, work.Name, Current) && ProjectedRowExists(viewWindow, work.NewName!, Current),
                        _ => true
                    },
                    (work, ticket) =>
                    {
                        var result = GetMethod<ProjectedAcknowledge>(bridge, 7)(bridge, work.Name, ticket);
                        // Native S_FALSE preserves a newer generation or an already
                        // retired fact. It emits no notification and is safe to retain.
                        if (result == 1) return;
                        if (result != 0) throw new InvalidOperationException($"Projected confirmation: 0x{result:X8}");
                    });
                if (Current() && enumerationBaseline is { } baseline)
                {
                    Marshal.ThrowExceptionForHR(GetMethod<ProjectedEnumerationCount>(bridge, 5)(bridge, out var enumerations));
                    if (!Current()) return;
                    if (lastLoggedDispatch != dispatched)
                    {
                        LogProjectedExperiment($"dispatch path={path};owner={viewWindow:X};events={dispatched}");
                        lastLoggedDispatch = dispatched;
                    }
                    if (dispatch.PendingCount == 0 && queue.Snapshot.Queued == 0 &&
                        (lastLoggedSettled != dispatched || lastEnumerations != enumerations))
                    {
                        LogProjectedExperiment($"settled path={path};owner={viewWindow:X};events={dispatched};enumerations={baseline}->{enumerations}");
                        lastLoggedSettled = dispatched;
                        lastEnumerations = enumerations;
                    }
                }
            }
            catch (Exception error)
            {
                if (Current())
                {
                    LastShellRefreshStatus = $"projected-experiment-stopped={error.GetType().Name},hr=0x{error.HResult:X8}";
                    LogProjectedExperiment($"error path={path};owner={viewWindow:X};status={LastShellRefreshStatus};message={error.Message}");
                    StopProjectedMonitorExperiment();
                }
            }
            finally { Marshal.Release(bridge); }
        };
        _projectedQueue = queue;
        _projectedWatcher = watcher;
        _projectedTimer = timer;
        _projectedBridge = bridge;
        Marshal.AddRef(bridge);
        try
        {
            // Isolation only: otherwise the prototype and normal native subscription
            // would both translate the same physical event.
            Marshal.ThrowExceptionForHR(GetMethod<ProjectedPause>(bridge, 3)(bridge));
            if (!Current()) return true;
            watcher.EnableRaisingEvents = true;
            timer.Start();
            LastShellRefreshStatus = "projected-experiment-monitor-started";
            LogProjectedExperiment($"started path={path};owner={viewWindow:X};navigation={navigationVersion}");
            return true;
        }
        catch
        {
            if (ReferenceEquals(_projectedQueue, queue)) StopProjectedMonitorExperiment();
            throw;
        }
        finally { Marshal.Release(bridge); }
    }

    private void StopProjectedMonitorExperiment()
    {
        _projectedQueue?.Invalidate();
        _projectedQueue = null;
        _projectedTimer?.Stop();
        _projectedTimer = null;
        var watcher = _projectedWatcher;
        _projectedWatcher = null;
        if (watcher is not null) _ = ShellDirectoryWatcherStartup.DisposeSafelyAsync(watcher.Dispose);
        var bridge = _projectedBridge;
        _projectedBridge = 0;
        if (bridge != 0) Marshal.Release(bridge);
    }

    private static void LogProjectedExperiment(string message)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "projected-monitor-experiment.log"), $"{Environment.TickCount64} {message}\n"); }
        catch (IOException) { }
    }

    private static bool ProjectedRowExists(nint viewWindow, string name, Func<bool> current)
    {
        nint list = 0;
        EnumChildWindows(viewWindow, (child, _) =>
        {
            if (WindowClass(child) != "SysListView32") return true;
            list = child;
            return false;
        }, 0);
        if (!current()) return false;
        if (list == 0) throw new InvalidOperationException("Projected experiment has no native list.");
        var text = Marshal.StringToHGlobalUni(name);
        var memory = Marshal.AllocHGlobal(40);
        try
        {
            Marshal.Copy(new byte[40], 0, memory, 40);
            Marshal.WriteInt32(memory, 2); // LVFI_STRING, exact name (not prefix).
            Marshal.WriteIntPtr(memory, 8, text);
            var index = SendMessage(list, 0x1053, -1, memory).ToInt64();
            if (index < 0 || !current()) return false;
            // Native find is case-insensitive. Case-only renames require the actual
            // displayed spelling, otherwise the old row appears to exist forever.
            var item = Marshal.AllocHGlobal(96);
            var buffer = Marshal.AllocHGlobal(1024);
            try
            {
                Marshal.Copy(new byte[96], 0, item, 96);
                Marshal.WriteIntPtr(item, 24, buffer);
                Marshal.WriteInt32(item, 32, 512);
                var length = SendMessage(list, 0x1073, (nint)index, item).ToInt32();
                return length >= 0 && string.Equals(Marshal.PtrToStringUni(buffer, length), name, StringComparison.Ordinal);
            }
            finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(item); }
        }
        finally { Marshal.FreeHGlobal(memory); Marshal.FreeHGlobal(text); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedGetFolder(nint self, ref Guid id, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedPause(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedEnumerationCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedArrival(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedRemoval(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name, out ulong ticket);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedRename(nint self, [MarshalAs(UnmanagedType.LPWStr)] string oldName, [MarshalAs(UnmanagedType.LPWStr)] string newName, out ulong ticket);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ProjectedAcknowledge(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name, ulong ticket);
}
#endif
