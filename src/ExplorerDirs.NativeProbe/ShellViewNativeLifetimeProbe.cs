using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

// Repeatedly creates and destroys stock IShellView windows on a Private Desktop.
// The probe reports process/window resource deltas instead of guessing that Shell caches are leaks.
internal static class ShellViewNativeLifetimeProbe
{
    private const int WarmupViews = 5;
    private const int MeasuredViews = 200;
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");

    internal static int Run(bool reuseBrowserHost = false)
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-ViewLifetime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        for (var index = 0; index < 4; index++)
            File.WriteAllText(Path.Combine(fixture, $"item-{index}.txt"), "fixture");

        nint parent = 0, folderPidl = 0, folder = 0, sharedHostPointer = 0;
        StreamViewHost? sharedHost = null;
        var samples = new List<ResourceSample>();
        Dictionary<string, int>? firstHandleTypes = null;
        Dictionary<string, int>? lastHandleTypes = null;
        IReadOnlyDictionary<long, uint>? firstSelfProcessHandles = null;
        IReadOnlyDictionary<long, uint>? lastSelfProcessHandles = null;
        try
        {
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden IShellView lifecycle probe",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0); // WS_POPUP; never shown.
            if (parent == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out folder));
            if (reuseBrowserHost)
            {
                sharedHost = new StreamViewHost(parent, []);
                sharedHostPointer = Marshal.GetComInterfaceForObject(sharedHost, typeof(IStreamProbeBrowser));
            }

            for (var index = 0; index < WarmupViews + MeasuredViews; index++)
            {
                var visibleItems = CreateAndDestroyOneView(folder, parent, sharedHost, sharedHostPointer);
                if (visibleItems != 4)
                    throw new InvalidOperationException($"View cycle {index + 1} enumerated {visibleItems}/4 fixture items.");
                var cleanupPumpDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
                while (DateTime.UtcNow < cleanupPumpDeadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }
                var remainingWindows = CountChildWindows(parent);
                if (remainingWindows != 0)
                    throw new InvalidOperationException($"View cycle {index + 1} left {remainingWindows} child HWNDs after DestroyViewWindow.");
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new InvalidOperationException($"View cycle {index + 1} crossed the hidden/foreground boundary.");

                if (index >= WarmupViews && (index - WarmupViews + 1) % 20 == 0)
                {
                    firstHandleTypes ??= TryCaptureHandleTypeCounts();
                    lastHandleTypes = TryCaptureHandleTypeCounts();
                    firstSelfProcessHandles ??= TryCaptureSelfProcessHandleEntries();
                    lastSelfProcessHandles = TryCaptureSelfProcessHandleEntries();
                    var sample = CaptureResources(index - WarmupViews + 1);
                    samples.Add(sample);
                    Console.WriteLine($"ishellview-native-lifetime-sample: cycle={sample.Cycle}; handles={sample.Handles}; gdi={sample.Gdi}; user={sample.User}; private-bytes={sample.PrivateBytes}; child-windows=0");
                }
            }

            if (samples.Count < 3)
                throw new InvalidOperationException("The lifecycle probe did not capture enough post-warmup resource samples.");
            var first = samples[0];
            var last = samples[^1];
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
            var afterCollection = CaptureResources(last.Cycle);
            var handleTypeDelta = firstHandleTypes is not null && lastHandleTypes is not null
                ? string.Join(',', lastHandleTypes.Keys.Union(firstHandleTypes.Keys, StringComparer.Ordinal)
                    .Select(name => (Name: name, Delta: lastHandleTypes.GetValueOrDefault(name) - firstHandleTypes.GetValueOrDefault(name)))
                    .Where(item => item.Delta != 0).OrderByDescending(item => item.Delta)
                    .Select(item => $"{item.Name}:{item.Delta:+#;-#;0}"))
                : "unavailable";
            var addedSelfProcessHandleEntries = firstSelfProcessHandles is not null && lastSelfProcessHandles is not null
                ? lastSelfProcessHandles.Where(entry => !firstSelfProcessHandles.ContainsKey(entry.Key)).ToArray()
                : null;
            var addedSelfProcessHandles = addedSelfProcessHandleEntries is null
                ? "unavailable"
                : $"count={addedSelfProcessHandleEntries.Length};access-masks=[{string.Join(',', addedSelfProcessHandleEntries
                    .GroupBy(entry => entry.Value).OrderByDescending(group => group.Count())
                    .Select(group => $"0x{group.Key:X}:{group.Count()}"))}];" +
                  $"sample-values=[{string.Join(',', addedSelfProcessHandleEntries.Take(8).Select(entry => $"0x{entry.Key:X}"))}]";
            Console.WriteLine($"ishellview-native-lifetime: views={WarmupViews + MeasuredViews}; warmup={WarmupViews}; measured={MeasuredViews}; " +
                $"browser-host-reused={reuseBrowserHost}; " +
                $"handles-delta={last.Handles - first.Handles}; gdi-delta={last.Gdi - first.Gdi}; user-delta={last.User - first.User}; " +
                $"private-bytes-delta={last.PrivateBytes - first.PrivateBytes}; post-gc-handles-delta={afterCollection.Handles - first.Handles}; " +
                $"post-gc-private-bytes-delta={afterCollection.PrivateBytes - first.PrivateBytes}; handle-type-delta=[{handleTypeDelta}]; " +
                $"added-self-process-handles-from-first-to-last-sample=[{addedSelfProcessHandles}]; " +
                $"child-windows-after-each-destroy=0; parent-visible=False; foreground-unchanged=True");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ishellview-native-lifetime: failed={exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally
        {
            if (sharedHost is not null)
            {
                sharedHost.View = 0;
                sharedHost.Dispose();
                if (sharedHostPointer != 0) Marshal.Release(sharedHostPointer);
                GC.KeepAlive(sharedHost);
            }
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (parent != 0 && IsWindow(parent) && !DestroyWindow(parent))
                Console.Error.WriteLine("ishellview-native-lifetime: hidden-parent-destroy-failed=True");
            try { Directory.Delete(fixture, recursive: true); }
            catch (Exception exception) { Console.Error.WriteLine($"ishellview-native-lifetime: fixture-cleanup-failed={exception.GetType().Name}"); }
            if (GetForegroundWindow() != foreground)
                Console.Error.WriteLine("ishellview-native-lifetime: foreground-changed-during-cleanup=True");
        }
    }

    internal static int RunCreateObjectOnly()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-ViewObjectLifetime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        nint parent = 0, folderPidl = 0, folder = 0;
        try
        {
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden IShellView object-only probe",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0);
            if (parent == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out folder));
            var before = CaptureResources(0);
            var beforeTypes = TryCaptureHandleTypeCounts();
            for (var index = 0; index < 200; index++)
            {
                var viewId = ShellViewId;
                Check(At<CreateViewObject>(folder, 8)(folder, parent, ref viewId, out var view));
                Marshal.Release(view);
                if ((index + 1) % 40 == 0) Application.DoEvents();
            }
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            var after = CaptureResources(200);
            var afterTypes = TryCaptureHandleTypeCounts();
            var typeDelta = beforeTypes is not null && afterTypes is not null
                ? string.Join(',', afterTypes.Keys.Union(beforeTypes.Keys, StringComparer.Ordinal)
                    .Select(name => (Name: name, Delta: afterTypes.GetValueOrDefault(name) - beforeTypes.GetValueOrDefault(name)))
                    .Where(item => item.Delta != 0).OrderByDescending(item => item.Delta)
                    .Select(item => $"{item.Name}:{item.Delta:+#;-#;0}"))
                : "unavailable";
            Console.WriteLine($"ishellview-native-view-object-only: iterations=200; handles-delta={after.Handles - before.Handles}; " +
                $"gdi-delta={after.Gdi - before.Gdi}; user-delta={after.User - before.User}; private-bytes-delta={after.PrivateBytes - before.PrivateBytes}; " +
                $"handle-type-delta=[{typeDelta}]; no-view-window=True");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ishellview-native-view-object-only: failed={exception.GetType().Name}:{exception.Message}");
            return 1;
        }
        finally
        {
            if (folder != 0) Marshal.Release(folder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (parent != 0 && IsWindow(parent)) _ = DestroyWindow(parent);
            try { Directory.Delete(fixture, recursive: true); } catch { }
        }
    }

    internal static int RunViewStageTrace()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-ViewStage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        for (var index = 0; index < 4; index++)
            File.WriteAllText(Path.Combine(fixture, $"item-{index}.txt"), "fixture");
        nint parent = 0, folderPidl = 0, folder = 0, view = 0, viewWindow = 0, folderView = 0, hostPointer = 0;
        StreamViewHost? host = null;
        var viewWindowCreated = false;
        var measurements = new List<(string Stage, int Handles, int? SelfProcessHandles, Dictionary<string, int>? Types,
            IReadOnlyDictionary<long, uint>? SelfProcessHandleEntries)>();

        void Capture(string stage)
        {
            if (!GetProcessHandleCount(GetCurrentProcess(), out var count))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessHandleCount failed.");
            var handleTypes = TryCaptureHandleTypeCounts();
            var selfProcessPrefix = $"Process[{Environment.ProcessId}:";
            var selfProcessHandles = handleTypes?
                .Where(item => item.Key.StartsWith(selfProcessPrefix, StringComparison.Ordinal))
                .Sum(item => item.Value);
            var selfProcessHandleEntries = TryCaptureSelfProcessHandleEntries();
            measurements.Add((stage, count, selfProcessHandles, handleTypes, selfProcessHandleEntries));
        }

        try
        {
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden IShellView stage trace",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0);
            if (parent == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out folder));
            Capture("baseline");

            var viewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref viewId, out view));
            Capture("CreateViewObject");
            host = new StreamViewHost(parent, []) { View = view };
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            Capture("IShellBrowser interface");
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            viewWindowCreated = true;
            Capture("CreateViewWindow");

            var folderViewId = FolderViewId;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            int itemCount;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out itemCount));
                if (itemCount == 4) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            if (itemCount != 4) throw new InvalidOperationException($"The staged view enumerated {itemCount}/4 fixture items.");
            Capture("enumeration settled");

            Marshal.Release(folderView);
            folderView = 0;
            Check(At<DestroyViewWindow>(view, 10)(view));
            viewWindowCreated = false;
            Capture("DestroyViewWindow returned");
            var destroyDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            while (IsWindow(viewWindow) && DateTime.UtcNow < destroyDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            if (IsWindow(viewWindow)) throw new InvalidOperationException("DestroyViewWindow did not invalidate its HWND during the stage trace.");
            Capture("destroy message pump settled");

            host.View = 0;
            Marshal.Release(view);
            view = 0;
            Capture("IShellView released");
            Marshal.Release(hostPointer);
            hostPointer = 0;
            Capture("IShellBrowser interface released");
            host.Dispose();
            host = null;
            Capture("host disposed");
            if (CountChildWindows(parent) != 0 || IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The staged view cleanup crossed the hidden/foreground boundary.");

            var baselineHandles = measurements[0].Handles;
            var baselineTypes = measurements[0].Types;
            var finalTypes = measurements[^1].Types;
            var typeDelta = baselineTypes is null || finalTypes is null
                ? "unavailable"
                : string.Join(',', baselineTypes.Keys.Union(finalTypes.Keys, StringComparer.Ordinal)
                    .Select(name => (Name: name, Delta: finalTypes.GetValueOrDefault(name) - baselineTypes.GetValueOrDefault(name)))
                    .Where(item => item.Delta != 0)
                    .Select(item => $"{item.Name}:{item.Delta:+#;-#;0}"));
            var baselineSelfHandles = measurements[0].SelfProcessHandleEntries;
            var selfHandleTimeline = string.Join(';', measurements.Select(item =>
            {
                if (baselineSelfHandles is null || item.SelfProcessHandleEntries is null)
                    return $"{item.Stage}=unavailable";
                var added = item.SelfProcessHandleEntries
                    .Where(entry => !baselineSelfHandles.ContainsKey(entry.Key))
                    .Select(entry => $"0x{entry.Key:X}:access=0x{entry.Value:X}");
                return $"{item.Stage}=[{string.Join(',', added)}]";
            }));
            Console.WriteLine($"ishellview-native-view-stage-trace: {string.Join(';', measurements.Select(item => $"{item.Stage}={item.Handles}(delta={item.Handles - baselineHandles:+#;-#;0},self-process-handles={item.SelfProcessHandles?.ToString() ?? "unavailable"})"))}; final-handle-type-delta=[{typeDelta}]; added-self-process-handle-values=[{selfHandleTimeline}]; child-windows=0; foreground-unchanged=True");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ishellview-native-view-stage-trace: failed={exception.GetType().Name}:{exception.Message}");
            return 1;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            if (viewWindowCreated && view != 0) _ = At<DestroyViewWindow>(view, 10)(view);
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

    private static int CreateAndDestroyOneView(nint folder, nint parent, StreamViewHost? sharedHost,
        nint sharedHostPointer)
    {
        nint view = 0, viewWindow = 0, hostPointer = 0, folderView = 0;
        var created = false;
        var ownsHost = sharedHost is null;
        var host = sharedHost ?? new StreamViewHost(parent, []);
        try
        {
            var viewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref viewId, out view));
            host.View = view;
            hostPointer = sharedHost is null ? Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser)) : sharedHostPointer;
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 640, Bottom = 480 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            created = true;

            var folderViewId = FolderViewId;
            Check(Marshal.QueryInterface(view, in folderViewId, out folderView));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            int count;
            do
            {
                Application.DoEvents();
                Check(At<ItemCount>(folderView, 7)(folderView, 2, out count)); // SVGIO_ALLVIEW
                if (count == 4) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            if (count != 4) throw new InvalidOperationException($"IShellView did not settle at four rows: {count}.");
            return count;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            if (created)
            {
                Check(At<DestroyViewWindow>(view, 10)(view));
                var destroyDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                while (IsWindow(viewWindow) && DateTime.UtcNow < destroyDeadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                if (IsWindow(viewWindow))
                    throw new InvalidOperationException($"DestroyViewWindow returned while its HWND remained live after message pumping: hwnd=0x{viewWindow:X}; parent=0x{GetParent(viewWindow):X}.");
            }
            host.View = 0;
            if (view != 0) Marshal.Release(view);
            if (ownsHost)
            {
                if (hostPointer != 0) Marshal.Release(hostPointer);
                host.Dispose();
                GC.KeepAlive(host);
            }
        }
    }

    private static ResourceSample CaptureResources(int cycle)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new ResourceSample(cycle, process.HandleCount, GetGuiResources(process.Handle, 0),
            GetGuiResources(process.Handle, 1), process.PrivateMemorySize64);
    }

    private static Dictionary<string, int>? TryCaptureHandleTypeCounts()
    {
        if (!Environment.Is64BitProcess) return null;
        const int processHandleInformation = 51;
        const int objectTypeInformation = 2;
        const int headerSize = 16;
        const int entrySize = 40;
        const int bufferSize = 1024 * 1024;
        var snapshot = Marshal.AllocHGlobal(bufferSize);
        var typeInformation = Marshal.AllocHGlobal(1024);
        var processInformation = Marshal.AllocHGlobal(48);
        try
        {
            var status = NtQueryInformationProcess(GetCurrentProcess(), processHandleInformation,
                snapshot, bufferSize, out _);
            if (status < 0) throw new InvalidOperationException($"NtQueryInformationProcess failed: 0x{status:X8}.");
            var handleCount = Marshal.ReadInt64(snapshot);
            if (handleCount < 0 || handleCount > (bufferSize - headerSize) / entrySize)
                throw new InvalidOperationException($"Unexpected process handle snapshot count: {handleCount}.");

            var namesByType = new Dictionary<int, string>();
            var countsByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < handleCount; index++)
            {
                var entry = snapshot + headerSize + checked((int)index * entrySize);
                var handle = Marshal.ReadIntPtr(entry);
                var typeIndex = Marshal.ReadInt32(entry, 28);
                if (!namesByType.TryGetValue(typeIndex, out var typeName))
                {
                    status = NtQueryObject(handle, objectTypeInformation, typeInformation, 1024, out _);
                    if (status < 0) throw new InvalidOperationException($"NtQueryObject failed for type index {typeIndex}: 0x{status:X8}.");
                    var nameLength = unchecked((ushort)Marshal.ReadInt16(typeInformation));
                    var nameBuffer = Marshal.ReadIntPtr(typeInformation, 8);
                    typeName = Marshal.PtrToStringUni(nameBuffer, nameLength / sizeof(char)) ?? "<unnamed>";
                    namesByType.Add(typeIndex, typeName);
                }
                if (typeName == "Process")
                {
                    status = NtQueryInformationProcess(handle, 0, processInformation, 48, out _); // ProcessBasicInformation
                    if (status >= 0)
                    {
                        var targetProcessId = Marshal.ReadIntPtr(processInformation, 32).ToInt64();
                        var targetName = "unknown";
                        try
                        {
                            using var targetProcess = Process.GetProcessById(checked((int)targetProcessId));
                            targetName = targetProcess.ProcessName;
                        }
                        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
                        {
                            targetName = "exited";
                        }
                        typeName = $"Process[{targetProcessId}:{targetName}]";
                    }
                    else typeName = "Process[unqueryable]";
                }
                countsByName[typeName] = countsByName.GetValueOrDefault(typeName) + 1;
            }
            return countsByName;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"ishellview-native-lifetime: handle-type-snapshot-unavailable={exception.GetType().Name}:{exception.Message}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(typeInformation);
            Marshal.FreeHGlobal(processInformation);
            Marshal.FreeHGlobal(snapshot);
        }
    }

    private static IReadOnlyDictionary<long, uint>? TryCaptureSelfProcessHandleEntries()
    {
        if (!Environment.Is64BitProcess) return null;
        const int processHandleInformation = 51;
        const int objectTypeInformation = 2;
        const int headerSize = 16;
        const int entrySize = 40;
        const int bufferSize = 1024 * 1024;
        var snapshot = Marshal.AllocHGlobal(bufferSize);
        var typeInformation = Marshal.AllocHGlobal(1024);
        var processInformation = Marshal.AllocHGlobal(48);
        try
        {
            var status = NtQueryInformationProcess(GetCurrentProcess(), processHandleInformation,
                snapshot, bufferSize, out _);
            if (status < 0) throw new InvalidOperationException($"NtQueryInformationProcess(handle table) failed: 0x{status:X8}.");
            var handleCount = Marshal.ReadInt64(snapshot);
            if (handleCount < 0 || handleCount > (bufferSize - headerSize) / entrySize)
                throw new InvalidOperationException($"Unexpected process handle snapshot count: {handleCount}.");

            // PROCESS_HANDLE_SNAPSHOT_INFORMATION entries place HANDLE first,
            // GrantedAccess at 24, and ObjectTypeIndex at 28 on x64.
            var namesByType = new Dictionary<int, string>();
            var result = new Dictionary<long, uint>();
            for (var index = 0; index < handleCount; index++)
            {
                var entry = snapshot + headerSize + checked((int)index * entrySize);
                var handle = Marshal.ReadIntPtr(entry);
                var typeIndex = Marshal.ReadInt32(entry, 28);
                if (typeIndex == 0) continue;
                if (!namesByType.TryGetValue(typeIndex, out var typeName))
                {
                    status = NtQueryObject(handle, objectTypeInformation, typeInformation, 1024, out _);
                    // Handles may close while the process handle table snapshot is being decoded.
                    if (status < 0) continue;
                    var nameLength = unchecked((ushort)Marshal.ReadInt16(typeInformation));
                    var nameBuffer = Marshal.ReadIntPtr(typeInformation, 8);
                    typeName = Marshal.PtrToStringUni(nameBuffer, nameLength / sizeof(char)) ?? "<unnamed>";
                    namesByType.Add(typeIndex, typeName);
                }
                if (typeName != "Process") continue;
                status = NtQueryInformationProcess(handle, 0, processInformation, 48, out _);
                if (status < 0 || Marshal.ReadIntPtr(processInformation, 32).ToInt64() != Environment.ProcessId) continue;
                var grantedAccess = unchecked((uint)Marshal.ReadInt32(entry, 24));
                result[handle.ToInt64()] = grantedAccess;
            }
            return result;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"ishellview-native-lifetime: self-process-handle-snapshot-unavailable={exception.GetType().Name}:{exception.Message}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(typeInformation);
            Marshal.FreeHGlobal(processInformation);
            Marshal.FreeHGlobal(snapshot);
        }
    }

    private static int CountChildWindows(nint parent)
    {
        var count = 0;
        EnumChildWindows(parent, (_, _) => { count++; return true; }, 0);
        return count;
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    private readonly record struct ResourceSample(int Cycle, int Handles, int Gdi, int User, long PrivateBytes);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern int GetGuiResources(nint process, uint flags);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessHandleCount(nint process, out int handleCount);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(nint process, int informationClass,
        nint information, int informationLength, out int returnLength);
    [DllImport("ntdll.dll")] private static extern int NtQueryObject(nint handle, int informationClass,
        nint information, int informationLength, out int returnLength);
}
