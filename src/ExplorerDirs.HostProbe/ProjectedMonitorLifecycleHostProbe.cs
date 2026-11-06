using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class ProjectedMonitorLifecycleHostProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static int Run(string adapter)
    {
        var assembly = Assembly.Load("ExplorerDirs.App");
        var hostType = assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var queueField = hostType.GetField("_projectedQueue", Instance) ?? throw new InvalidOperationException("Build HostProbe with ProjectedNotificationExperiment=true.");
        var ownerType = assembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", true)!;
        var stateType = assembly.GetType("ExplorerDirs.App.ViewState", true)!;
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProjectedLifecycle-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(profile); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        File.WriteAllBytes(Path.Combine(first, "first.bin"), [1]);
        File.WriteAllBytes(Path.Combine(second, "second.bin"), [2]);
        var id = Guid.NewGuid();
        var owner = ownerType.GetMethod("Start", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [id, profile, Path.GetFullPath(adapter)])!;
        HwndHost? host = null;
        HwndSource? source = null;
        nint oldCapability = 0, middleCapability = 0, finalCapability = 0;
        try
        {
            var codec = assembly.GetType("ExplorerDirs.App.QdrShellPathCodec", true)!;
            string Identity(string path) => (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [path, id])!;
            var state = stateType.GetMethod("Default", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [4u, 16])!;
            host = (HwndHost)Activator.CreateInstance(hostType, Identity(first), state)!;
            hostType.GetMethod("ConfigureFolderSizeLocations", Instance)!.Invoke(host, [id, true]);
            var factoryType = assembly.GetType("ExplorerDirs.App.FolderSizeSortContextFactory", true)!;
            var factory = Delegate.CreateDelegate(factoryType, owner, ownerType.GetMethod("TryCreateSortContext", Instance)!);
            hostType.GetMethod("ConfigureFolderSizeSortContextFactory", Instance)!.Invoke(host, [factory]);
            source = new HwndSource(new HwndSourceParameters("Private projected monitor lifecycle")
                { WindowStyle = unchecked((int)0x80000000), Width = 720, Height = 480 });
            source.RootVisual = host;
            var read = typeof(ShellProviderLocationProbe).GetMethod("ReadNativeListViewSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
            string[] Names()
            {
                var view = (nint)hostType.GetField("_viewWindow", Instance)!.GetValue(host)!;
                if (view == 0) return [];
                var rows = ((int Count, string[] Names))read.Invoke(null, [view])!;
                return rows.Names;
            }
            object? Queue() => queueField.GetValue(host);
            static (int Queued, int Paths, bool Invalidated) Snapshot(object queue) =>
                ((int, int, bool))queue.GetType().GetProperty("Snapshot", Instance)!.GetValue(queue)!;
            void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            Wait(() => Names().Contains("first.bin") && Queue() is not null, "first navigation/monitor");
            var oldQueue = Queue()!;
            oldCapability = (nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)!;
            Marshal.AddRef(oldCapability);
            File.WriteAllBytes(Path.Combine(first, "queued-before-navigation.bin"), [3]);
            Require(SpinWait.SpinUntil(() => Snapshot(oldQueue).Queued > 0, TimeSpan.FromSeconds(3)), "No actual watcher work queued before navigation.");
            hostType.GetMethod("Navigate", Instance)!.Invoke(host, [Identity(second)]);
            Wait(() => Names().Contains("second.bin") && Queue() is { } next && !ReferenceEquals(next, oldQueue), "second navigation/monitor");
            Require(Snapshot(oldQueue) == (0, 0, true), "Old navigation retained event facts.");
            Require(At<Arrival>(oldCapability, 8)(oldCapability, "first.bin") == unchecked((int)0x800401FD), "Retired native capability accepted arrival.");
            File.WriteAllBytes(Path.Combine(first, "late-old-directory.bin"), [4]);
            File.WriteAllBytes(Path.Combine(second, "live-second.bin"), [5]);
            Wait(() => Names().Contains("live-second.bin"), "new visit receives actual change");
            Require(!Names().Any(name => name.Contains("old-directory", StringComparison.Ordinal) || name.StartsWith("queued-before", StringComparison.Ordinal)), "Old directory row leaked into new visit.");
            var middleQueue = Queue()!;
            middleCapability = (nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)!;
            Marshal.AddRef(middleCapability);
            File.WriteAllBytes(Path.Combine(second, "queued-before-return.bin"), [8]);
            Require(SpinWait.SpinUntil(() => Snapshot(middleQueue).Queued > 0, TimeSpan.FromSeconds(3)), "No watcher work queued before same-path revisit.");
            hostType.GetMethod("Navigate", Instance)!.Invoke(host, [Identity(first)]);
            Wait(() => Names().Contains("first.bin") && !Names().Contains("second.bin") &&
                Queue() is { } returned && !ReferenceEquals(returned, oldQueue) && !ReferenceEquals(returned, middleQueue), "same-path revisit monitor");
            Require(Snapshot(oldQueue) == (0, 0, true) && Snapshot(middleQueue) == (0, 0, true), "Revisit revived a retired managed queue.");
            Require(At<Arrival>(oldCapability, 8)(oldCapability, "first.bin") == unchecked((int)0x800401FD), "Same-path revisit revived an old native capability.");
            Require(At<Arrival>(middleCapability, 8)(middleCapability, "second.bin") == unchecked((int)0x800401FD), "Leaving second visit retained its native capability.");
            File.WriteAllBytes(Path.Combine(first, "live-return.bin"), [9]);
            Wait(() => Names().Contains("live-return.bin"), "returned visit receives actual change");
            var closingQueue = Queue()!;
            finalCapability = (nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)!;
            Marshal.AddRef(finalCapability);
            File.WriteAllBytes(Path.Combine(first, "queued-before-close.bin"), [6]);
            Require(SpinWait.SpinUntil(() => Snapshot(closingQueue).Queued > 0, TimeSpan.FromSeconds(3)), "No actual watcher work queued before close.");
            host.Dispose();
            source.RootVisual = null;
            Require(Snapshot(closingQueue) == (0, 0, true), "Close retained queued watcher work.");
            Require(At<Arrival>(finalCapability, 8)(finalCapability, "first.bin") == unchecked((int)0x800401FD), "Closed native capability accepted arrival.");
            var count = At<Counts>(finalCapability, 6);
            Marshal.ThrowExceptionForHR(count(finalCapability, out var roots, out var facts));
            Require(roots == 0 && facts == 0, $"Closed native metadata retained roots/facts {roots}/{facts}.");
            File.WriteAllBytes(Path.Combine(first, "after-close.bin"), [7]);
            Console.WriteLine("projected-monitor-lifecycle: PASS real queued watcher events retired on A-B-A navigation and close; returned visit updates; old rows absent; retired capabilities never revived; roots/facts=0; no input.");
            return 0;
        }
        finally
        {
            host?.Dispose();
            if (source is not null) { source.RootVisual = null; source.Dispose(); }
            if (oldCapability != 0) Marshal.Release(oldCapability);
            if (middleCapability != 0) Marshal.Release(middleCapability);
            if (finalCapability != 0) Marshal.Release(finalCapability);
            ((IDisposable)owner).Dispose();
            Directory.Delete(root, true);
        }
    }

    private static void Wait(Func<bool> condition, string stage)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            if (condition()) return;
            Thread.Sleep(10);
        }
        throw new TimeoutException("Projected lifecycle: " + stage);
    }
    private static T At<T>(nint pointer, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Arrival(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Counts(nint self, out uint roots, out uint facts);
}
