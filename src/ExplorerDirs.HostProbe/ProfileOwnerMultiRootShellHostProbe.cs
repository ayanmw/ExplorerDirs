using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class ProfileOwnerMultiRootShellHostProbe
{
    private const int LargeFolderCount = 500;

    internal static object CreateViewStateForProbe(Assembly appAssembly) => CreateViewState(appAssembly, 0);
    internal static string FindProductAdapterForProbe() => FindProductAdapter();

    internal static int Run(bool large = false, int folderCount = 0, bool diagnosticAdapter = true,
        int cacheCapacity = 8192, bool captureNotificationIdentities = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProfileOwnerMultiRoot-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var folders = Enumerable.Range(0, 4).Select(index => Path.Combine(root, $"pane-{index + 1}")).ToArray();
        var itemCount = folderCount > 0 ? folderCount : large ? LargeFolderCount : 2;
        large = itemCount > 2;
        var expectedSizes = large
            ? Enumerable.Range(0, 4).Select(pane => Enumerable.Range(0, itemCount)
                .Select(index => (ulong)((pane + 1) * 10_000 + itemCount - index)).Order().ToArray()).ToArray()
            : new[] { new ulong[] { 5, 29 }, new ulong[] { 7, 31 }, new ulong[] { 11, 37 }, new ulong[] { 13, 41 } };
        var expectedItemCount = itemCount;
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        object? owner = null;
        HwndSource? source = null;
        nint adapterModule = 0;
        var hosts = new List<HwndHost>();
        var foreground = GetForegroundWindow();
        try
        {
            Directory.CreateDirectory(profile);
            for (var index = 0; index < folders.Length; index++)
            {
                Directory.CreateDirectory(folders[index]);
                if (large)
                {
                    for (var itemIndex = 0; itemIndex < expectedItemCount; itemIndex++)
                    {
                        var name = $"folder-extra-{itemIndex:D4}";
                        var bytes = checked((int)((index + 1) * 10_000 + expectedItemCount - itemIndex));
                        CreateFolder(folders[index], name, bytes);
                    }
                }
                else
                {
                    CreateFolder(folders[index], "folder-small", checked((int)expectedSizes[index][0]));
                    CreateFolder(folders[index], "folder-large", checked((int)expectedSizes[index][1]));
                }
            }

            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var ownerType = appAssembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", throwOnError: true)!;
            var adapterPath = large && diagnosticAdapter ? FindProductDiagnosticAdapter() : FindProductAdapter();
            if (large && diagnosticAdapter)
            {
                adapterModule = NativeLibrary.Load(adapterPath);
                Marshal.GetDelegateForFunctionPointer<SetProductCompareTraceEnabled>(
                    NativeLibrary.GetExport(adapterModule, "SetProductCompareTraceEnabled"))(0);
                Marshal.GetDelegateForFunctionPointer<SetProductNotificationIdentityCaptureEnabled>(
                    NativeLibrary.GetExport(adapterModule, "SetProductNotificationIdentityCaptureEnabledForProbe"))(
                        captureNotificationIdentities ? 1 : 0);
                var configureCache = Marshal.GetDelegateForFunctionPointer<ConfigureCacheCapacity>(
                    NativeLibrary.GetExport(adapterModule, "SetSyntheticRecursiveSizeCacheCapacityForProbe"));
                var configureResult = configureCache(cacheCapacity);
                if (configureResult < 0) Marshal.ThrowExceptionForHR(configureResult);
            }
            owner = ownerType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [classId, profile, adapterPath]);
            if (owner is not IDisposable) throw new InvalidOperationException("The production profile owner is not disposable.");

            var codec = appAssembly.GetType("ExplorerDirs.App.QdrShellPathCodec", throwOnError: true)!;
            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            var hostNavigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hostViewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var readCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var readValues = typeof(ShellProviderLocationProbe).GetMethod("ReadRecursiveValues",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var readNativeRows = typeof(ShellProviderLocationProbe).GetMethod("ReadNativeListViewSnapshot",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var selectItem = typeof(ShellProviderLocationProbe).GetMethod("SelectShellViewItemByPath",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var readSelection = typeof(ShellProviderLocationProbe).GetMethod("ReadNativeListViewSelectionState",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            var grid = new Grid { Width = 960, Height = 640 };
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < folders.Length; index++)
            {
                var identity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [folders[index], classId])!;
                var viewState = CreateViewState(appAssembly, index % 2 == 0 ? 1 : -1);
                var host = (HwndHost)Activator.CreateInstance(hostType, identity, viewState)!;
                hosts.Add(host);
                Grid.SetRow(host, index / 2);
                Grid.SetColumn(host, index % 2);
                grid.Children.Add(host);
            }
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs four-pane product Shell host probe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 960, Height = 640
            });
            source.RootVisual = grid;
            grid.Measure(new Size(960, 640));
            grid.Arrange(new Rect(0, 0, 960, 640));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The four-pane product Shell host parent became visible.");

            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                var currentHost = host;
                var expectedIdentity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [folders[index], classId])!;
                WaitUntil(() => Equals(hostNavigated.GetValue(currentHost), true) &&
                    (nint)hostViewWindow.GetValue(currentHost)! != 0,
                    TimeSpan.FromSeconds(15), $"Product pane {index + 1} did not navigate.");
                if (!string.Equals((string)hostType.GetProperty("CurrentPath")!.GetValue(host)!, expectedIdentity,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Product pane {index + 1} lost its independent provider identity.");
                WaitUntil(() =>
                {
                    var counts = readCounts.Invoke(host, [false, false]);
                    return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == expectedItemCount;
                }, TimeSpan.FromSeconds(30), $"Product pane {index + 1} did not enumerate its own {expectedItemCount} rows.");
            }

            var paneValues = new string[hosts.Count][];
            var paneValueReadyMilliseconds = new long[hosts.Count];
            var paneSortReadyMilliseconds = new long[hosts.Count];
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                var valueReadyClock = Stopwatch.StartNew();
                WaitUntil(() =>
                {
                    paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, expectedItemCount])!;
                    return paneValues[index].Select(value => ulong.TryParse(value, out var bytes) ? bytes : ulong.MaxValue)
                        .Order().SequenceEqual(expectedSizes[index]);
                }, TimeSpan.FromSeconds(45), $"Product pane {index + 1} received another pane's recursive values.",
                    pollMilliseconds: 250);
                paneValueReadyMilliseconds[index] = valueReadyClock.ElapsedMilliseconds;

                var captured = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(host, null)!;
                var capturedColumns = ((System.Collections.IEnumerable)captured.GetType()
                    .GetProperty("DisplayColumns")!.GetValue(captured)!).Cast<object>().ToArray();
                if (capturedColumns.Length != 3 || !capturedColumns.Any(column =>
                        column.GetType().GetProperty("Key")!.GetValue(column)!.ToString() ==
                        "{b725f130-47ef-101a-a5f1-02608c9eebac}:12"))
                    throw new InvalidOperationException($"Product pane {index + 1} did not retain its own recursive column state.");

                var expectedOrder = index % 2 == 0 ? expectedSizes[index] : expectedSizes[index].Reverse();
                var expectedNames = large
                    ? (index % 2 == 0 ? Enumerable.Range(0, expectedItemCount).Reverse() : Enumerable.Range(0, expectedItemCount))
                        .Select(itemIndex => $"folder-extra-{itemIndex:D4}").ToArray()
                    : index % 2 == 0 ? ["folder-small", "folder-large"] : ["folder-large", "folder-small"];
                var viewWindow = (nint)hostViewWindow.GetValue(host)!;
                try
                {
                    var sortReadyClock = Stopwatch.StartNew();
                    WaitUntil(() =>
                    {
                        // Poll the native row order cheaply while DefView sorts; read COM column values only after candidate order appears.
                        var nativeRows = readNativeRows.Invoke(null, [viewWindow])!;
                        var actualNames = (string[])nativeRows.GetType().GetField("Item2")!.GetValue(nativeRows)!;
                        if (!actualNames.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase)) return false;
                        paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, expectedItemCount])!;
                        return paneValues[index].Select(value => ulong.TryParse(value, out var bytes) ? bytes : ulong.MaxValue)
                            .SequenceEqual(expectedOrder);
                    }, TimeSpan.FromSeconds(75), $"Product pane {index + 1} did not restore its independent saved recursive-size sort.",
                        pollMilliseconds: 100);
                    paneSortReadyMilliseconds[index] = sortReadyClock.ElapsedMilliseconds;
                    var nativeRows = readNativeRows.Invoke(null, [viewWindow])!;
                    var namesAfterSort = (string[])nativeRows.GetType().GetField("Item2")!.GetValue(nativeRows)!;
                    if (!namesAfterSort.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Product pane {index + 1} Shell values sorted, but native ListView names disagreed with Shell row order: {namesAfterSort.Length} rows; first=[{string.Join('|', namesAfterSort.Take(5))}]; last=[{string.Join('|', namesAfterSort.TakeLast(5))}].");
                }
                catch (TimeoutException exception)
                {
                    paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, expectedItemCount])!;
                    var nativeRows = readNativeRows.Invoke(null, [viewWindow])!;
                    var actualNames = (string[])nativeRows.GetType().GetField("Item2")!.GetValue(nativeRows)!;
                    var counts = readCounts.Invoke(host, [false, false]);
                    var totalRows = counts is null ? -1 :
                        (int)counts.GetType().GetProperty("Total")!.GetValue(counts)!;
                    var sortState = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                        .Invoke(host, null)!;
                    var actualSorts = ((System.Collections.IEnumerable)sortState.GetType()
                            .GetProperty("SortColumns")!.GetValue(sortState)!).Cast<object>()
                        .Select(column => $"{column.GetType().GetProperty("Key")!.GetValue(column)}/{column.GetType().GetProperty("Direction")!.GetValue(column)}");
                    var failureValueEdges = paneValues[index].Length == 0 ? "empty" :
                        $"{paneValues[index].Length}:{string.Join('|', paneValues[index].Take(3))}..{string.Join('|', paneValues[index].TakeLast(3))}";
                    var failureNameEdges = actualNames.Length == 0 ? "empty" :
                        $"{actualNames.Length}:{string.Join('|', actualNames.Take(4))}..{string.Join('|', actualNames.TakeLast(4))}";
                    var expectedDirection = index % 2 == 0 ? 1 : -1;
                    var nameMismatches = Enumerable.Range(0, Math.Min(actualNames.Length, expectedNames.Length))
                        .Where(row => !string.Equals(actualNames[row], expectedNames[row], StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var mismatchSamples = string.Join('|', nameMismatches.Take(8).Select(row =>
                        $"{row}:{actualNames[row]}→{expectedNames[row]}"));
                    var numericValues = paneValues[index]
                        .Select(value => ulong.TryParse(value, out var bytes) ? (ulong?)bytes : null)
                        .ToArray();
                    var sortViolations = Enumerable.Range(1, Math.Max(0, numericValues.Length - 1))
                        .Count(row => numericValues[row - 1].HasValue && numericValues[row].HasValue &&
                            (expectedDirection > 0
                                ? numericValues[row - 1] > numericValues[row]
                                : numericValues[row - 1] < numericValues[row]));
                    var failureCacheMetrics = adapterModule == 0 ? "diagnostic-counters=unavailable" : ReadCacheMetrics(adapterModule);
                    throw new InvalidOperationException($"Product pane {index + 1} saved sort timed out: value-ready-ms={paneValueReadyMilliseconds[index]}; rows={totalRows}/{expectedItemCount}; actual-names={failureNameEdges}; actual-values={failureValueEdges}; name-mismatches={nameMismatches.Length}; first-mismatches=[{mismatchSamples}]; adjacent-sort-violations={sortViolations}; actual-sort=[{string.Join('|', actualSorts)}]; cache=[{failureCacheMetrics}]; expected-direction={expectedDirection}.", exception);
                }
            }

            // The production initial-sort timers run concurrently; inject selections only after every
            // pane has settled so this probe measures per-pane sort state, not synthetic input races.
            for (var index = 0; index < hosts.Count; index++)
                selectItem.Invoke(null, [hostType, hosts[index], Path.Combine(folders[index], SelectionName(index, expectedItemCount))]);

            var paneSelections = hosts.Select(host =>
            {
                var viewWindow = (nint)hostViewWindow.GetValue(host)!;
                var selection = readSelection.Invoke(null, [viewWindow])!;
                return (string[])selection.GetType().GetField("Item1")!.GetValue(selection)!;
            }).ToArray();
            for (var index = 0; index < paneSelections.Length; index++)
            {
                var expectedSelection = SelectionName(index, expectedItemCount);
                if (!paneSelections[index].SequenceEqual([expectedSelection], StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Selection crossed pane {index + 1} identity: [{string.Join('|', paneSelections[index])}].");
            }

            var readFocusedRow = typeof(ShellProviderLocationProbe).GetMethod("ReadShellFocusedRow",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var paneFocus = new string?[hosts.Count];
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, expectedItemCount])!;

                var viewWindow = (nint)hostViewWindow.GetValue(host)!;
                var selection = readSelection.Invoke(null, [viewWindow])!;
                var selected = (string[])selection.GetType().GetField("Item1")!.GetValue(selection)!;
                var listViewFocus = (string?)selection.GetType().GetField("Item2")!.GetValue(selection);
                paneFocus[index] = (string?)readFocusedRow.Invoke(null, [hostType, host]);
                var expectedFocused = SelectionName(index, expectedItemCount);
                if (!selected.SequenceEqual([expectedFocused], StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(listViewFocus, expectedFocused, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(paneFocus[index], expectedFocused, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Product pane {index + 1} lost its independent selection/focus after sort: selected=[{string.Join('|', selected)}]; listview-focus={listViewFocus}; shell-focus={paneFocus[index]}.");
            }

            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The product panes changed the foreground window.");
            var valueEdges = paneValues.Select(values => $"{values.Length}:{values[0]}..{values[^1]}");
            var cacheMetrics = adapterModule == 0 ? "diagnostic-counters=unavailable" : ReadCacheMetrics(adapterModule);
            Console.WriteLine($"profile-owner-multi-root-shell-host: large={large}; diagnostic-notification-identity-capture={(adapterModule != 0 ? captureNotificationIdentities.ToString() : "unavailable")}; simultaneous-hosts={hosts.Count}; rows-per-pane={expectedItemCount}; roots=independent; value-ready-ms=[{string.Join('|', paneValueReadyMilliseconds)}]; sort-ready-ms=[{string.Join('|', paneSortReadyMilliseconds)}]; recursive-sort-value-edges=[{string.Join(" || ", valueEdges)}]; source-cache-capacity={cacheCapacity}; cache=[{cacheMetrics}]; selections=[{string.Join('|', paneSelections.Select(values => string.Join(',', values)))}]; focused-rows=[{string.Join('|', paneFocus)}]; parents-hidden=True; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else
            {
                foreach (var host in hosts)
                    if (host is IDisposable disposable) disposable.Dispose();
            }
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            if (owner is IDisposable lifetime) lifetime.Dispose();
            if (adapterModule != 0) NativeLibrary.Free(adapterModule);
            var registrationRemains = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(classKeyPath) is not null ||
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            HostProbeForegroundGuard.EnsureUnchanged(foreground,
                GetForegroundWindow(), "profile-owned multi-root Shell hosts");
            if (registrationRemains)
                throw new InvalidOperationException("The production profile owner left volatile Shell registration behind.");
        }
    }

    private static void CreateFolder(string root, string name, int bytes)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "payload.bin"), new byte[bytes]);
    }

    private static string SelectionName(int paneIndex, int itemCount) => itemCount > 2
        ? $"folder-extra-{(paneIndex % 2 == 0 ? itemCount - 1 : 0):D4}"
        : paneIndex % 2 == 0 ? "folder-small" : "folder-large";

    private static object CreateViewState(Assembly appAssembly, int recursiveSortDirection)
    {
        var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
        var keyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
        var columnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
        var basic = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
        var columns = Array.CreateInstance(columnType, 3);
        columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType, [basic, 10u])!, 240]), 0);
        columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType,
            [Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u])!, 176]), 1);
        columns.SetValue(Activator.CreateInstance(columnType, [Activator.CreateInstance(keyType, [basic, 4u])!, 160]), 2);
        var sortColumnType = appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!;
        var sortColumns = Array.CreateInstance(sortColumnType, recursiveSortDirection == 0 ? 0 : 1);
        if (recursiveSortDirection != 0)
            sortColumns.SetValue(Activator.CreateInstance(sortColumnType,
                [Activator.CreateInstance(keyType, [Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u])!, recursiveSortDirection]), 0);
        var constructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate => candidate.GetParameters().Length == 6);
        return constructor.Invoke([4u, 16, sortColumns, null, true, columns]);
    }

    private static string FindProductAdapter()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-product-x64",
                "ExplorerDirs.ShellNamespace.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Build the product Shell namespace adapter before this probe.");
    }

    private static string FindProductDiagnosticAdapter()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-product-diagnostics-x64",
                "ExplorerDirs.ShellNamespace.Diagnostics.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Build the diagnostic-only product Shell namespace adapter before this probe.");
    }

    private static string ReadCacheMetrics(nint module)
    {
        var snapshots = Marshal.GetDelegateForFunctionPointer<GetCounter32>(
            NativeLibrary.GetExport(module, "GetSyntheticRecursiveSizeCacheSnapshotCountForProbe"))();
        var payload = Marshal.GetDelegateForFunctionPointer<GetCounter32>(
            NativeLibrary.GetExport(module, "GetSyntheticRecursiveSizeCachePayloadBytesForProbe"))();
        var hits = Marshal.GetDelegateForFunctionPointer<GetCounter64>(
            NativeLibrary.GetExport(module, "GetSyntheticRecursiveSizeCacheLookupHitsForProbe"))();
        var misses = Marshal.GetDelegateForFunctionPointer<GetCounter64>(
            NativeLibrary.GetExport(module, "GetSyntheticRecursiveSizeCacheLookupMissesForProbe"))();
        var evictions = Marshal.GetDelegateForFunctionPointer<GetCounter64>(
            NativeLibrary.GetExport(module, "GetSyntheticRecursiveSizeCacheEvictionsForProbe"))();
        var scans = Marshal.GetDelegateForFunctionPointer<GetCounter32>(
            NativeLibrary.GetExport(module, "GetSyntheticOnDemandSizeScanCount"))();
        var metricsCalls = Marshal.GetDelegateForFunctionPointer<GetCounter32>(
            NativeLibrary.GetExport(module, "GetSyntheticDirectoryMetricsCallCount"))();
        return $"entries={snapshots};payload={payload};hits={hits};misses={misses};evictions={evictions};scans={scans};metric-calls={metricsCalls}";
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message, int pollMilliseconds = 20)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(pollMilliseconds)
        };
        var clock = Stopwatch.StartNew();
        var complete = false;
        timer.Tick += (_, _) =>
        {
            complete = condition();
            if (complete || clock.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!complete) throw new TimeoutException(message);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool IsWindowVisible(nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ConfigureCacheCapacity(int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetProductCompareTraceEnabled(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetProductNotificationIdentityCaptureEnabled(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter32();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long GetCounter64();

}
