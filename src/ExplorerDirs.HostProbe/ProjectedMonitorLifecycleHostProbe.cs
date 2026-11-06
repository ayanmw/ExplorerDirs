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
        HwndHost? peer = null;
        HwndSource? peerSource = null;
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
            var taken = new object?[] { null };
            Require((bool)oldQueue.GetType().GetMethod("TryTake", Instance)!.Invoke(oldQueue, taken)!, "Could not hold actual queued work in flight.");
            var keyType = assembly.GetType("ExplorerDirs.App.PropertyKeyState", true)!;
            var sortType = assembly.GetType("ExplorerDirs.App.SortColumnState", true)!;
            var sizeKey = Activator.CreateInstance(keyType, [new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 12u])!;
            var sizeSort = Activator.CreateInstance(sortType, [sizeKey, 1])!;
            hostType.GetMethod("EnableInitialSortRecoveryTrace", Instance)!.Invoke(host, null);
            hostType.GetMethod("ScheduleInitialVirtualSort", Instance)!.Invoke(host,
                [sizeSort, (Func<bool>)(() => ReferenceEquals(Queue(), oldQueue)), null, false]);
            Wait(() => ((string)hostType.GetProperty("InitialSortRecoveryStatus", Instance)!.GetValue(host)!).Contains(":notifications-pending", StringComparison.Ordinal), "sort recovery waits for in-flight event confirmation");
            Console.WriteLine("projected-sort-coordination: PASS held in-flight work defers initial sort recovery.");
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
            Wait(() => (bool)hostType.GetMethod("IsProjectedUpdateSettled", Instance)!.Invoke(host, null)!, "return visit notifications settled");
            var activeQueue = Queue()!;
            File.WriteAllBytes(Path.Combine(first, "live-return.bin"), [13, 14]);
            Require(SpinWait.SpinUntil(() => Snapshot(activeQueue).Queued > 0, TimeSpan.FromSeconds(3)), "No property change queued before removal.");
            File.Delete(Path.Combine(first, "live-return.bin"));
            Wait(() => !Names().Contains("live-return.bin") && ReferenceEquals(Queue(), activeQueue) &&
                (bool)hostType.GetMethod("IsProjectedUpdateSettled", Instance)!.Invoke(host, null)!, "obsolete property change reconciles deletion without stopping monitor");
            Console.WriteLine("projected-stale-property: PASS queued change reconciles disappeared item without stopping its visit.");
            var ephemeralName = "ephemeral-confirm.bin";
            var ephemeralPath = Path.Combine(first, ephemeralName);
            File.WriteAllBytes(ephemeralPath, [21]);
            Wait(() => Names().Contains(ephemeralName) &&
                (bool)hostType.GetMethod("IsProjectedUpdateSettled", Instance)!.Invoke(host, null)!, "ephemeral creation delivered");
            var timer = (DispatcherTimer)hostType.GetField("_projectedTimer", Instance)!.GetValue(host)!;
            var watcher = (FileSystemWatcher)hostType.GetField("_projectedWatcher", Instance)!.GetValue(host)!;
            timer.Stop(); watcher.EnableRaisingEvents = false;
            var heldQueue = Queue()!;
            var heldRecord = heldQueue.GetType().GetMethod("Record", Instance)!;
            var changeKind = assembly.GetType("ExplorerDirs.NativeProbe.ProjectedChangeKind", true)!;
            // Model a previously delivered creation awaiting its UI observation.
            // Ingress is controlled here; delivery and row absence are real Shell operations.
            heldRecord.Invoke(heldQueue, [Enum.Parse(changeKind, "Created"), ephemeralName, null]);
            var heldArguments = new object?[] { null };
            Require((bool)heldQueue.GetType().GetMethod("TryTake", Instance)!.Invoke(heldQueue, heldArguments)!, "Could not hold delivered creation.");
            File.Delete(ephemeralPath);
            heldRecord.Invoke(heldQueue, [Enum.Parse(changeKind, "Deleted"), ephemeralName, null]);
            var capability = (nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)!;
            Marshal.ThrowExceptionForHR(At<Removal>(capability, 4)(capability, ephemeralName, out _));
            Wait(() =>
            {
                Marshal.ThrowExceptionForHR(At<DeliveryStatus>(capability, 12)(capability, out var pending));
                return pending == 0 && !Names().Contains(ephemeralName);
            }, "actual removal before old creation observation");
            var confirm = hostType.GetMethod("ConfirmProjectedSupersededCreation", Instance)!;
            var viewWindow = (nint)hostType.GetField("_viewWindow", Instance)!.GetValue(host)!;
            bool Confirm(bool current) => (bool)confirm.Invoke(host,
                [heldQueue, heldArguments[0], viewWindow, first, (Func<bool>)(() => current)])!;
            Require(!Confirm(false) && Confirm(true), "Superseded creation did not require a live visit and actual absence.");
            File.WriteAllBytes(ephemeralPath, [22]);
            Require(!Confirm(true), "A physical recreation with no visible row was mistaken for confirmed absence.");
            File.Delete(ephemeralPath);
            Require(Confirm(true), "Deleted recreation did not restore the verified absence condition.");
            heldQueue.GetType().GetMethod("Release", Instance)!.Invoke(heldQueue, [heldArguments[0]]);
            watcher.EnableRaisingEvents = true; timer.Start();
            Wait(() => (bool)hostType.GetMethod("IsProjectedUpdateSettled", Instance)!.Invoke(host, null)!, "retained later deletion completes");
            Console.WriteLine("projected-superseded-creation: PASS actual native absence; live-visit/disk-recreation guards; later deletion retained (controlled ingress).");
            File.WriteAllBytes(Path.Combine(first, "live-return.bin"), [15]);
            Wait(() => Names().Contains("live-return.bin"), "monitor continues after obsolete property change");
            var overflowQueue = Queue()!;
            var record = overflowQueue.GetType().GetMethod("Record", Instance)!;
            var kind = Enum.Parse(assembly.GetType("ExplorerDirs.NativeProbe.ProjectedChangeKind", true)!, "Changed");
            // Fault injection uses the actual default capacity and queue. No file
            // operations, message pumping or production configuration override.
            for (var i = 0; i < 8193; i++) record.Invoke(overflowQueue, [kind, "first.bin", null]);
            Require((bool)overflowQueue.GetType().GetProperty("Overflowed", Instance)!.GetValue(overflowQueue)!, "Expected default-capacity overflow.");
            Wait(() => Queue() is null && (nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)! == 0, "overflow retires monitor");
            Require(((string)hostType.GetProperty("LastShellRefreshStatus", Instance)!.GetValue(host)!).Contains("InternalBufferOverflowException", StringComparison.Ordinal), "Overflow was not reported.");
            Require(Snapshot(overflowQueue) == (0, 0, true), "Overflow retained queued facts.");
            File.WriteAllBytes(Path.Combine(first, "missed-while-paused.bin"), [11]);
            File.Delete(Path.Combine(first, "live-return.bin"));
            hostType.GetMethod("RefreshFromUser", Instance)!.Invoke(host, null);
            Wait(() => Names().Contains("missed-while-paused.bin") && !Names().Contains("live-return.bin") && Queue() is { } refreshed && !ReferenceEquals(refreshed, overflowQueue), "F5 reconciles rows and restarts monitor after overflow");
            Directory.CreateDirectory(Path.Combine(first, "live-return.bin"));
            Wait(() => Names().Contains("live-return.bin"), "new folder at removed file path after F5");
            File.WriteAllBytes(Path.Combine(first, "live-after-f5.bin"), [12]);
            Wait(() => Names().Contains("live-after-f5.bin"), "real watcher updates after F5 recovery");
            var prepareRefresh = hostType.GetMethod("PrepareProjectedManualRefresh", Instance)!;
            var staleCompletion = (Action)prepareRefresh.Invoke(host, [true])!;
            var newerCompletion = (Action)prepareRefresh.Invoke(host, [true])!;
            newerCompletion();
            var latestQueue = Queue();
            Require(latestQueue is not null, "Newer refresh completion did not restore monitor.");
            staleCompletion();
            Require(ReferenceEquals(latestQueue, Queue()), "Older refresh callback replaced the newer same-path monitor.");
            hostType.GetMethod("Navigate", Instance)!.Invoke(host, [Identity(second)]);
            Wait(() => Names().Contains("second.bin") && Queue() is not null, "navigate after overflow");
            hostType.GetMethod("Navigate", Instance)!.Invoke(host, [Identity(first)]);
            Wait(() => Names().Contains("first.bin") && Queue() is { } recovered && !ReferenceEquals(recovered, overflowQueue), "recover after overflow");
            File.WriteAllBytes(Path.Combine(first, "live-after-overflow.bin"), [10]);
            Wait(() => Names().Contains("live-after-overflow.bin"), "recovered visit accepts real watcher changes");
            string[] PeerNames()
            {
                if (peer is null) return [];
                var view = (nint)hostType.GetField("_viewWindow", Instance)!.GetValue(peer)!;
                if (view == 0) return [];
                var rows = ((int Count, string[] Names))read.Invoke(null, [view])!;
                return rows.Names;
            }
            if (Environment.GetEnvironmentVariable("EXPLORERDIRS_PROJECTED_SHARED_QUEUE_CONTROL") == "1")
            {
                peer = (HwndHost)Activator.CreateInstance(hostType, Identity(first), state)!;
                hostType.GetMethod("ConfigureFolderSizeLocations", Instance)!.Invoke(peer, [id, true]);
                hostType.GetMethod("ConfigureFolderSizeSortContextFactory", Instance)!.Invoke(peer, [factory]);
                peerSource = new HwndSource(new HwndSourceParameters("Private projected shared-queue peer")
                    { WindowStyle = unchecked((int)0x80000000), Width = 720, Height = 480 });
                peerSource.RootVisual = peer;
                // A first row can appear before the peer's initial enumeration
                // finishes. Do not inject the close/create race into that startup.
                var baselineNames = Names().Order(StringComparer.Ordinal).ToArray();
                Wait(() => PeerNames().Order(StringComparer.Ordinal).SequenceEqual(baselineNames) &&
                    queueField.GetValue(peer) is not null &&
                    (bool)hostType.GetMethod("IsProjectedUpdateSettled", Instance)!.Invoke(peer, null)!,
                    "same-root peer monitor ready");
                Console.WriteLine($"projected-shared-peer-identities: first-bridge=0x{(nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)!:X};peer-bridge=0x{(nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(peer)!:X}");
            }
            var closingQueue = Queue()!;
            finalCapability = (nint)hostType.GetField("_projectedBridge", Instance)!.GetValue(host)!;
            Marshal.AddRef(finalCapability);
            File.WriteAllBytes(Path.Combine(first, "queued-before-close.bin"), [6]);
            Require(SpinWait.SpinUntil(() => Snapshot(closingQueue).Queued > 0, TimeSpan.FromSeconds(3)), "No actual watcher work queued before close.");
            host.Dispose();
            source.RootVisual = null;
            Require(Snapshot(closingQueue) == (0, 0, true), "Close retained queued watcher work.");
            Require(At<Arrival>(finalCapability, 8)(finalCapability, "first.bin") == unchecked((int)0x800401FD), "Closed native capability accepted arrival.");
            if (peer is not null)
            {
                Wait(() => PeerNames().Contains("queued-before-close.bin"), "peer applies update queued before other host closed");
                File.WriteAllBytes(Path.Combine(first, "peer-after-close.bin"), [8]);
                Wait(() => PeerNames().Contains("peer-after-close.bin") && queueField.GetValue(peer) is not null,
                    "peer continues real watcher updates after other host retirement");
                File.Delete(Path.Combine(first, "peer-after-close.bin"));
                Wait(() => !PeerNames().Contains("peer-after-close.bin") &&
                    (bool)hostType.GetMethod("IsProjectedUpdateSettled", Instance)!.Invoke(peer, null)!,
                    "peer deletion confirms after other host retirement");
                peer.Dispose();
                peerSource!.RootVisual = null;
                Console.WriteLine("projected-shared-peer: PASS real same-root host close preserves peer queued create, later create/delete and settlement");
            }
            var count = At<Counts>(finalCapability, 6);
            Marshal.ThrowExceptionForHR(count(finalCapability, out var roots, out var facts));
            Require(roots == 0 && facts == 0, $"Closed native metadata retained roots/facts {roots}/{facts}.");
            File.WriteAllBytes(Path.Combine(first, "after-close.bin"), [7]);
            Console.WriteLine("projected-monitor-lifecycle: PASS A-B-A navigation/close; overflow pauses; real F5 restores missed rows and watcher; file-to-folder after F5; stale refresh callback rejected; re-navigation updates; retired capabilities never revived; roots/facts=0; no input.");
            return 0;
        }
        finally
        {
            peer?.Dispose();
            if (peerSource is not null) { peerSource.RootVisual = null; peerSource.Dispose(); }
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Removal(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name, out ulong ticket);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeliveryStatus(nint self, out uint pending);
}
