using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

internal static class ProfileOwnerMultiRootShellHostProbe
{
    private const int LargeFolderCount = 500;

    private static string? _sortContextAdapter;
    private static bool _retrySortForProbe;
    internal static int RunSortRetryForProbe(string adapterPath)
    {
        _retrySortForProbe = true;
        try { return RunInputDuringSortForProbe(adapterPath); }
        finally { _retrySortForProbe = false; }
    }

    private static string? _persistedSessionPath;
    private static bool _readPersistedSession;
    private static bool _unavailableSizes;
    private static bool _holdUnavailable;
    private static readonly System.Threading.ManualResetEventSlim UnknownRelease = new(true);
    private static int _pendingUnknownQueries;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ConfigureUnavailableProvider(nint query, nint invalidate, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UnavailableSizeQuery([MarshalAs(UnmanagedType.LPWStr)] string path, nint cancelled,
        out ulong bytes, out ulong files, out ulong folders, out ulong unreadable, out uint flags);
    private static int QueryUnavailable(string path, nint cancelled, out ulong bytes, out ulong files,
        out ulong folders, out ulong unreadable, out uint flags)
    {
        bytes = files = folders = unreadable = 0; flags = 0;
        Interlocked.Increment(ref _pendingUnknownQueries);
        while (!UnknownRelease.Wait(10))
            if (cancelled != 0 && Marshal.ReadInt32(cancelled) != 0) return unchecked((int)0x800704C7);
        return 0;
    }

    internal static int RunPersistedSortForProbe(string adapterPath, string sessionPath, bool read, bool unavailable, bool holdUnavailable)
    {
        _persistedSessionPath = Path.GetFullPath(sessionPath);
        _readPersistedSession = read;
        _unavailableSizes = unavailable;
        _holdUnavailable = holdUnavailable;
        _pendingUnknownQueries = 0;
        if (holdUnavailable) UnknownRelease.Reset(); else UnknownRelease.Set();
        try { return RunWithSortContextForProbe(adapterPath); }
        finally { _persistedSessionPath = null; _readPersistedSession = false; _unavailableSizes = false; _holdUnavailable = false; UnknownRelease.Set(); }
    }

    private static bool _inputDuringSort;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetProbeFolder(nint self, in Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetProbeFolderIdentity(nint self, out nint identity);
    [DllImport("shell32.dll", EntryPoint = "ILClone")]
    private static extern nint CloneProbeRoot(nint root);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VerifyProbePolicy(nint root, uint expected);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetProbeComparisonCount();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ReadProbeComparison(int index, StringBuilder text, uint capacity);
    private static bool _reconcileUserSort;
    private static uint _userSortMode = 3;
    private static bool _hideSortSizeColumn;
    internal static int RunUserSortReconciliationForProbe(string adapterPath, uint mode, bool hideSizeColumn)
    {
        _reconcileUserSort = true;
        _userSortMode = mode;
        _hideSortSizeColumn = hideSizeColumn;
        try { return RunWithSortContextForProbe(adapterPath); }
        finally { _reconcileUserSort = false; _userSortMode = 3; _hideSortSizeColumn = false; }
    }

    private static bool _renameDuringSort;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SelectProbeItem(nint self, int index, uint flags);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendProbeMessage(nint hwnd, uint message, nint wParam, nint lParam);

    internal static int RunRenameDuringSortForProbe(string adapterPath)
    {
        _renameDuringSort = true;
        try { return RunInputDuringSortForProbe(adapterPath); }
        finally { _renameDuringSort = false; }
    }

    private static bool _commandDuringSort;
    internal static int RunCommandDuringSortForProbe(string adapterPath)
    {
        _commandDuringSort = true;
        try { return RunInputDuringSortForProbe(adapterPath); }
        finally { _commandDuringSort = false; }
    }

    internal static int RunInputDuringSortForProbe(string adapterPath)
    {
        _inputDuringSort = true;
        try { return RunWithSortContextForProbe(adapterPath); }
        finally { _inputDuringSort = false; }
    }

    internal static int RunWithSortContextForProbe(string adapterPath)
    {
        _sortContextAdapter = Path.GetFullPath(adapterPath);
        try { return Run(); }
        finally { _sortContextAdapter = null; }
    }

    internal static int RunGroupedLargeForProbe(string adapterPath)
    {
        _sortContextAdapter = Path.GetFullPath(adapterPath);
        try { return Run(folderCount: 1025, diagnosticAdapter: false, initialSizeGrouping: true); }
        finally { _sortContextAdapter = null; }
    }

    internal static object CreateViewStateForProbe(Assembly appAssembly) => CreateViewState(appAssembly, 0);
    internal static string FindProductAdapterForProbe() => FindProductAdapter();

    internal static int Run(bool large = false, int folderCount = 0, bool diagnosticAdapter = true,
        int cacheCapacity = 8192, bool captureNotificationIdentities = false, bool forceSortDeadline = false, bool initialSizeGrouping = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProfileOwnerMultiRoot-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var folders = Enumerable.Range(0, forceSortDeadline ? 1 : 4).Select(index => Path.Combine(root, $"pane-{index + 1}")).ToArray();
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
        ConfigureUnavailableProvider? configureUnavailable = null;
        UnavailableSizeQuery? unavailableQuery = null;
        nint sortPolicyModule = 0, sortPolicyRoot = 0;
        var hosts = new List<HwndHost>();
        var replayViews = new List<object>();
        var sortContexts = new List<object>();
        DispatcherTimer? deadlineStall = null;
        DispatcherTimer? transientCaptureTimer = null;
        Exception? transientCaptureFailure = null;
        var transientCaptureObserved = false;
        var deadlineStalled = false;
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
            Console.WriteLine("profile-owner-multi-root-shell-host: stage=assembly-loaded");
            var ownerType = appAssembly.GetType("ExplorerDirs.App.ProfileShellNamespaceOwner", throwOnError: true)!;
            var adapterPath = _sortContextAdapter ?? ((large && diagnosticAdapter || forceSortDeadline) ? FindProductDiagnosticAdapter() : FindProductAdapter());
            if (large && diagnosticAdapter || forceSortDeadline)
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
            Console.WriteLine("profile-owner-multi-root-shell-host: stage=profile-owner-created");
            if (_unavailableSizes)
            {
                adapterModule = NativeLibrary.Load(adapterPath);
                configureUnavailable = Marshal.GetDelegateForFunctionPointer<ConfigureUnavailableProvider>(NativeLibrary.GetExport(adapterModule, "ConfigureFolderSizeProvider"));
                unavailableQuery = QueryUnavailable;
                Marshal.ThrowExceptionForHR(configureUnavailable(Marshal.GetFunctionPointerForDelegate(unavailableQuery), 0, true));
            }


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
            // Isolate the category-command contract from concurrent initial-sort races.
            // Ordinary input/rename/history probes retain all four simultaneous hosts.
            for (var index = 0; index < (_reconcileUserSort ? 1 : folders.Length); index++)
            {
                Console.WriteLine($"profile-owner-multi-root-shell-host: stage=create-host-{index + 1}");
                var identity = (string)codec.GetMethod("ToProviderIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [folders[index], classId])!;
                var viewState = CreateViewState(appAssembly, index % 2 == 0 ? 1 : -1);
                if (initialSizeGrouping)
                {
                    var keyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", true)!;
                    var sizeKey = Activator.CreateInstance(keyType, [new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 12u]);
                    viewState.GetType().GetProperty("GroupKey")!.SetValue(viewState, sizeKey);
                }

                if (_persistedSessionPath is not null)
                {
                    if (_readPersistedSession)
                    {
                        var storeType = appAssembly.GetType("ExplorerDirs.App.SessionStore", true)!;
                        var reader = Activator.CreateInstance(storeType, [_persistedSessionPath, _persistedSessionPath + ".legacy"])!;
                        var session = storeType.GetMethod("Load")!.Invoke(reader, null)!;
                        var pane = ((System.Collections.IEnumerable)session.GetType().GetProperty("Panes")!.GetValue(session)!).Cast<object>().ElementAt(index);
                        var tab = ((System.Collections.IEnumerable)pane).Cast<object>().Single();
                        viewState = tab.GetType().GetProperty("View")!.GetValue(tab)!;
                        replayViews.Add(viewState);
                    }
                    else viewState.GetType().GetProperty("MixedSizeSort")!.SetValue(viewState, index % 2 == 0);
                    if (!Equals(viewState.GetType().GetProperty("MixedSizeSort")!.GetValue(viewState), index % 2 == 0))
                        throw new InvalidOperationException("Persisted per-pane mixed policy did not reload.");
                }

                var host = (HwndHost)Activator.CreateInstance(hostType, identity, viewState)!;
                hostType.GetMethod("EnableInitialSortRecoveryTrace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
                if (_retrySortForProbe && index == 0)
                    hostType.GetField("_initialSortIncorrectResultsForProbe", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, 2);
                // Match PaneControl: provider identity is paired with its owning profile.
                hostType.GetMethod("ConfigureFolderSizeLocations", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(host, [classId, true]);
                var factoryType = appAssembly.GetType("ExplorerDirs.App.FolderSizeSortContextFactory", true)!;
                var factory = Delegate.CreateDelegate(factoryType, owner,
                    ownerType.GetMethod("TryCreateSortContext", BindingFlags.Instance | BindingFlags.NonPublic)!);
                hostType.GetMethod("ConfigureFolderSizeSortContextFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(host, [factory]);
                hosts.Add(host);
                Grid.SetRow(host, index / 2);
                Grid.SetColumn(host, index % 2);
                grid.Children.Add(host);
            }
            Console.WriteLine("profile-owner-multi-root-shell-host: stage=create-wpf-source");
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs four-pane product Shell host probe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 960, Height = 640
            });
            if (forceSortDeadline)
            {
                var getView = hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeSource = hostType.GetNestedType("NativeViewStateSource", BindingFlags.NonPublic)!;
                deadlineStall = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(10) };
                deadlineStall.Tick += (_, _) =>
                {
                    var view = (nint)getView.Invoke(hosts[0], null)!;
                    if (view == 0) return;
                    try
                    {
                        var reader = Activator.CreateInstance(nativeSource, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null, [view], null)!;
                        object?[] arguments = [1, null];
                        var result = (int)nativeSource.GetMethod("GetSortColumns")!.Invoke(reader, arguments)!;
                        if (result < 0 || arguments[1] is not System.Collections.IEnumerable columns) return;
                        var column = columns.Cast<object>().FirstOrDefault();
                        if (column is null || (int)column.GetType().GetProperty("Direction")!.GetValue(column)! != -1) return;
                        if (!ExpireSortReadinessClockForProbe()) return;
                        deadlineStall.Stop();
                        deadlineStalled = true;
                        Console.WriteLine("profile-owner-sort-deadline: observed-temporary-descending; injected-readiness-elapsed-ms=31000; dispatcher-not-blocked=True");
                    }
                    finally { Marshal.Release(view); }
                };
                deadlineStall.Start();
            }
            if (_sortContextAdapter is not null && !_readPersistedSession && !initialSizeGrouping)
            {
                transientCaptureTimer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(10) };
                transientCaptureTimer.Tick += (_, _) =>
                {
                    nint view = 0;
                    try
                    {
                        var trace = (string)hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                        if (!trace.Contains("opposite-verb-returned", StringComparison.Ordinal)) return;
                        view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null)!;
                        if (view == 0) return;
                        var sourceType = hostType.GetNestedType("NativeViewStateSource", BindingFlags.NonPublic)!;
                        var reader = Activator.CreateInstance(sourceType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null, [view], null)!;
                        object?[] args = [1, null];
                        if ((int)sourceType.GetMethod("GetSortColumns")!.Invoke(reader, args)! < 0 || args[1] is not System.Collections.IEnumerable columns) return;
                        var actual = columns.Cast<object>().SingleOrDefault();
                        if (actual is null || (int)actual.GetType().GetProperty("Direction")!.GetValue(actual)! != -1) return;
                        var captured = hostType.GetMethod("CaptureViewState")!.Invoke(hosts[0], null)!;
                        var saved = ((System.Collections.IEnumerable)captured.GetType().GetProperty("SortColumns")!.GetValue(captured)!)
                            .Cast<object>().Single();
                        if ((int)saved.GetType().GetProperty("Direction")!.GetValue(saved)! != 1)
                            throw new InvalidOperationException("Capture cached the initial recovery's temporary descending direction.");
                        if (!(bool)hostType.GetProperty("ViewCaptureFailed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(hosts[0])!)
                            throw new InvalidOperationException("Temporary recovery capture was reported as complete.");
                        if (_inputDuringSort)
                        {
                            // Exercise a gesture against actual rows, not the preceding native asynchronous sort's empty interval.
                            var inputReadyRows = readNativeRows.Invoke(null, [(nint)hostViewWindow.GetValue(hosts[0])!])!;
                            if ((int)inputReadyRows.GetType().GetField("Item1")!.GetValue(inputReadyRows)! == 0) return;
                            transientCaptureTimer.Stop();
                            // Record native rows before and after input completion; direction alone cannot prove gesture readiness.
                            var beforeInputRows = readNativeRows.Invoke(null, [(nint)hostViewWindow.GetValue(hosts[0])!])!;
                            Console.WriteLine($"sort-input-boundary: before={beforeInputRows}");
                            if (_commandDuringSort)
                            {
                                hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], [nint.Zero]);
                                var invokeCommand = hostType.GetMethod("InvokeVirtualSortVerb", BindingFlags.Instance | BindingFlags.NonPublic)!;
                                if (!(bool)invokeCommand.Invoke(hosts[0], ["sortascending", new Func<bool>(() => true)])!)
                                    throw new InvalidOperationException("User ascending command during initial recovery failed.");
                            }
                            else
                            {
                                hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                                    .Invoke(hosts[0], [(nint)hostViewWindow.GetValue(hosts[0])!]);
                                var afterInputRows = readNativeRows.Invoke(null, [(nint)hostViewWindow.GetValue(hosts[0])!])!;
                                Console.WriteLine($"sort-input-boundary: after={afterInputRows}");
                                // Only the latest gesture before the queued capture runs may win.
                                selectItem.Invoke(null, [hostType, hosts[0], Path.Combine(folders[0], "folder-small")]);
                                hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                                    .Invoke(hosts[0], [(nint)hostViewWindow.GetValue(hosts[0])!]);
                                selectItem.Invoke(null, [hostType, hosts[0], Path.Combine(folders[0], "folder-large")]);
                            }
                            if (_renameDuringSort)
                            {
                                var nativeRows = readNativeRows.Invoke(null, [(nint)hostViewWindow.GetValue(hosts[0])!])!;
                                var names = (string[])nativeRows.GetType().GetField("Item2")!.GetValue(nativeRows)!;
                                var renameIndex = Array.IndexOf(names, "folder-large");
                                if (renameIndex < 0) throw new InvalidOperationException("Rename target disappeared before editing.");
                                var selectNative = Marshal.GetDelegateForFunctionPointer<SelectProbeItem>(
                                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(view), 15 * IntPtr.Size));
                                if (selectNative(view, renameIndex, 0x1f) < 0)
                                    throw new InvalidOperationException("Native rename setup failed.");
                                PumpDispatcher(TimeSpan.FromMilliseconds(350));
                                var editing = hostType.GetMethod("IsRenameEditorFocused", BindingFlags.Instance | BindingFlags.Public)!;
                                if (!(bool)editing.Invoke(hosts[0], null)!)
                                    throw new InvalidOperationException("Initial sort ended the native rename editor.");
                                object?[] editingSort = [1, null];
                                if ((int)sourceType.GetMethod("GetSortColumns")!.Invoke(reader, editingSort)! < 0 ||
                                    editingSort[1] is not System.Collections.IEnumerable editingColumns ||
                                    (int)editingColumns.Cast<object>().Single().GetType().GetProperty("Direction")!.GetValue(editingColumns.Cast<object>().Single())! != -1)
                                    throw new InvalidOperationException("Recovery reordered the view under an active rename editor.");
                                // Cancel only the private Desktop's native Edit; no global keyboard injection.
                                SendProbeMessage(GetFocus(), 0x0100, 0x1b, 0);
                                PumpDispatcher(TimeSpan.FromMilliseconds(50));
                                if ((bool)editing.Invoke(hosts[0], null)!)
                                    throw new InvalidOperationException("Native rename editor did not cancel.");
                                Console.WriteLine("product-initial-sort-rename: editor-preserved=True; sort-paused=True; cancelled=True");
                            }
                            WaitUntil(() =>
                            {
                                if (_retrySortForProbe)
                                {
                                    var retryTrace = (string)hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                                    var retryOffset = retryTrace.LastIndexOf(":retry-incorrect-row-order", StringComparison.Ordinal);
                                    if (retryOffset < 0 || retryTrace.LastIndexOf(":finish", StringComparison.Ordinal) < retryOffset) return false;
                                }
                                object?[] completedSort = [1, null];
                                return (int)sourceType.GetMethod("GetSortColumns")!.Invoke(reader, completedSort)! >= 0 &&
                                    completedSort[1] is System.Collections.IEnumerable sorts &&
                                    (int)sorts.Cast<object>().Single().GetType().GetProperty("Direction")!.GetValue(sorts.Cast<object>().Single())! == 1 &&
                                    !(bool)hostType.GetProperty("ViewCaptureFailed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                            }, TimeSpan.FromSeconds(5), "Input-preserving recovery did not settle in the saved direction.");
                            PumpDispatcher(TimeSpan.FromMilliseconds(350));
                            if (_commandDuringSort)
                            {
                                var commandTrace = (string)hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                                if (!commandTrace.Contains("cancel-or-deadline", StringComparison.Ordinal) ||
                                    commandTrace.Contains("saved-verb-returned", StringComparison.Ordinal))
                                    throw new InvalidOperationException("Old recovery dispatched its saved direction after an explicit user command.");
                            }
                            var selection = readSelection.Invoke(null, [(nint)hostViewWindow.GetValue(hosts[0])!])!;
                            if (!_commandDuringSort && !((string[])selection.GetType().GetField("Item1")!.GetValue(selection)!).SequenceEqual(["folder-large"]))
                                throw new InvalidOperationException("Recovery overwrote the subsequent user's selection.");
                            var invokeSort = hostType.GetMethod("InvokeVirtualSortVerb", BindingFlags.Instance | BindingFlags.NonPublic)!;
                            hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], [nint.Zero]);
                            if (!(bool)invokeSort.Invoke(hosts[0], ["sortdescending", new Func<bool>(() => true)])!)
                                throw new InvalidOperationException("Subsequent user sort command failed.");
                            PumpDispatcher(TimeSpan.FromMilliseconds(350));
                            var userView = hostType.GetMethod("CaptureViewState")!.Invoke(hosts[0], null)!;
                            var userSort = ((System.Collections.IEnumerable)userView.GetType().GetProperty("SortColumns")!.GetValue(userView)!).Cast<object>().Single();
                            if ((int)userSort.GetType().GetProperty("Direction")!.GetValue(userSort)! != -1)
                                throw new InvalidOperationException("Retired recovery overwrote the subsequent user sort.");
                            if (!(bool)invokeSort.Invoke(hosts[0], ["sortascending", new Func<bool>(() => true)])!)
                                throw new InvalidOperationException("Could not restore the normal test baseline.");
                            hostType.GetMethod("CaptureViewState")!.Invoke(hosts[0], null);
                            Console.WriteLine($"product-initial-sort-input: scenario={(_renameDuringSort ? "rename" : _commandDuringSort ? "command" : "selection")}; settled-saved-direction=True; later-user-sort-retained=True");
                        }
                        if (_retrySortForProbe)
                        {
                            if ((int)hostType.GetField("_initialSortIncorrectResultsForProbe", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])! != 0)
                                throw new InvalidOperationException("The retry did not consume both injected wrong-order samples.");
                            Console.WriteLine("product-size-sort-retry: injected-wrong-order=2; native-retry=True; completed-after-retry=True; selection-preserved=True");
                        }
                        transientCaptureObserved = true;
                        transientCaptureTimer.Stop();
                        Console.WriteLine("product-initial-sort-transient-capture: native-descending=True; cached-ascending=True; incomplete=True");
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine($"sort-input-failure-trace: {hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])}");
                        transientCaptureFailure = error;
                        transientCaptureTimer.Stop();
                    }
                    finally { if (view != 0) Marshal.Release(view); }
                };
                transientCaptureTimer.Start();
            }
            Console.WriteLine("profile-owner-multi-root-shell-host: stage=attach-root-visual");
            source.RootVisual = grid;
            grid.Measure(new Size(960, 640));
            grid.Arrange(new Rect(0, 0, 960, 640));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The four-pane product Shell host parent became visible.");

            for (var index = 0; index < hosts.Count; index++)
            {
                Console.WriteLine($"profile-owner-multi-root-shell-host: stage=wait-navigation-{index + 1}");
                var host = hosts[index];
                var currentHost = host;
                var expectedIdentity = folders[index]; // Product chrome keeps the logical physical directory.
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

            if (_unavailableSizes)
            {
                var firstWindow = (nint)hostViewWindow.GetValue(hosts[0])!;
                if (_holdUnavailable)
                {
                    WaitUntil(() => Volatile.Read(ref _pendingUnknownQueries) > 0, TimeSpan.FromSeconds(5), "Pending provider callback did not start.");
                    var pendingView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null)!;
                    try
                    {
                        var reader = appAssembly.GetType("ExplorerDirs.App.ShellSizeSortOrder", true)!.GetMethod("AreValuesReadyAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                        var task = (System.Threading.Tasks.Task<bool?>)reader.Invoke(null, [pendingView, 2, new Func<bool>(() => true), true])!;
                        WaitUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5), "Pending readiness read blocked on provider work.");
                        if (task.GetAwaiter().GetResult() is not null)
                            throw new InvalidOperationException("Pending provider values were treated as completed unknown values.");
                        var recovery = (string)hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                        if (recovery.Contains("grouped-values-ready-refresh", StringComparison.Ordinal))
                            throw new InvalidOperationException("Grouped refresh started before pending queries completed.");
                    }
                    finally { if (pendingView != 0) Marshal.Release(pendingView); }
                    Console.WriteLine("product-pending-size-readiness: callbacks-held=True; nonblocking=True; pending-not-terminal=True; no-premature-refresh=True");
                    UnknownRelease.Set();
                }
                var unknownGroupWait = Stopwatch.StartNew();
                WaitUntil(() => ShellProviderLocationProbe.IsNativeGroupingEnabled(firstWindow),
                    TimeSpan.FromSeconds(5), "Confirmed unavailable size values did not materialize grouping promptly.");
                var state = hostType.GetMethod("CaptureViewState")!.Invoke(hosts[0], null)!;
                if (!Equals(state.GetType().GetProperty("GroupKey")!.GetValue(state), replayViews[0].GetType().GetProperty("GroupKey")!.GetValue(replayViews[0])))
                    throw new InvalidOperationException("Unknown size grouping lost the saved group key.");
                string[][] unknownCells = [];
                WaitUntil(() =>
                {
                    unknownCells = ShellProviderLocationProbe.ReadNativeListViewCells(firstWindow, 3);
                    return unknownCells.Length == 2;
                }, TimeSpan.FromSeconds(10), "Unknown-size native refresh did not restore its rows.");
                if (unknownCells.Length != 2 || unknownCells.Any(row => !string.IsNullOrWhiteSpace(row[1]) && row[1] != "—"))
                    throw new InvalidOperationException($"Unavailable size cells: {string.Join("/", unknownCells.Select(row => string.Join("|", row)))}.");
                var readinessType = appAssembly.GetType("ExplorerDirs.App.ShellSizeSortOrder", true)!;
                var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null)!;
                try
                {
                    bool? ReadReady(bool allowUnknown)
                    {
                        var task = (System.Threading.Tasks.Task<bool?>)readinessType.GetMethod("AreValuesReadyAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                            .Invoke(null, [view, 2, new Func<bool>(() => true), allowUnknown])!;
                        WaitUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5), "Raw readiness query did not complete.");
                        return task.GetAwaiter().GetResult();
                    }
                    WaitUntil(() => ReadReady(true) == true, TimeSpan.FromSeconds(5), "Completed unknown state was treated as pending.");
                    if (ReadReady(false) is not null) throw new InvalidOperationException("Unknown values were accepted as numeric Size values.");
                }
                finally { if (view != 0) Marshal.Release(view); }
                Console.WriteLine($"product-unavailable-size-grouping: elapsed-ms={unknownGroupWait.ElapsedMilliseconds}; terminal-unknown-readiness=True; numeric-unknown-rejected=True; preserved=True; {ShellProviderLocationProbe.ReadNativeGroupingState(firstWindow)}");
                return 0;
            }

            if (_readPersistedSession)
            {
                for (var index = 0; index < hosts.Count; index++)
                {
                    var expected = replayViews[index];
                    var replayWindow = (nint)hostViewWindow.GetValue(hosts[index])!;
                    string[] replayNames = [];
                    try
                    {
                        WaitUntil(() =>
                        {
                        var rows = readNativeRows.Invoke(null, [replayWindow])!;
                        replayNames = (string[])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                        return replayNames.SequenceEqual(index % 2 == 0 ? new[] { "folder-small", "folder-large" } : new[] { "folder-large", "folder-small" });
                    }, TimeSpan.FromSeconds(10), $"Replayed host {index + 1} did not restore actual native size row order.");
                    }
                    catch (TimeoutException error)
                    {
                        throw new InvalidOperationException($"Persisted native rows: pane={index + 1}; names={string.Join('|', replayNames)}; groups={string.Join('|', ShellProviderLocationProbe.ReadNativeGroupIds(replayWindow))}; saved-group={expected.GetType().GetProperty("GroupKey")!.GetValue(expected)}; native-grouping={ShellProviderLocationProbe.ReadNativeGroupingState(replayWindow)}; recovery={hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[index])}.", error);
                    }

                    try
                    {
                        WaitUntil(() =>
                        {
                        var actual = hostType.GetMethod("CaptureViewState")!.Invoke(hosts[index], null)!;
                        var type = actual.GetType();
                        return new[] { "Mode", "IconSize", "MixedSizeSort", "GroupKey", "GroupAscending" }
                            .All(name => Equals(type.GetProperty(name)!.GetValue(actual), type.GetProperty(name)!.GetValue(expected))) &&
                            ((System.Collections.IEnumerable)type.GetProperty("SortColumns")!.GetValue(actual)!).Cast<object>()
                                .SequenceEqual(((System.Collections.IEnumerable)type.GetProperty("SortColumns")!.GetValue(expected)!).Cast<object>()) &&
                            ((System.Collections.IEnumerable)type.GetProperty("DisplayColumns")!.GetValue(actual)!).Cast<object>()
                                .SequenceEqual(((System.Collections.IEnumerable)type.GetProperty("DisplayColumns")!.GetValue(expected)!).Cast<object>());
                    }, TimeSpan.FromSeconds(10), $"Replayed host {index + 1} did not restore its persisted view settings.");
                    }
                    catch (TimeoutException error)
                    {
                        var actual = hostType.GetMethod("CaptureViewState")!.Invoke(hosts[index], null)!;
                        throw new InvalidOperationException($"Persisted setting mismatch: actual={System.Text.Json.JsonSerializer.Serialize(actual, actual.GetType())}; expected={System.Text.Json.JsonSerializer.Serialize(expected, expected.GetType())}; recovery={hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[index])}.", error);
                    }
                }
                Console.WriteLine("product-persisted-view-settings: four-hosts=True; mode-icon-sort-group-mixed-columns=True; native-initial-row-order=True");
            }

            if (_sortContextAdapter is not null)
            {
                if (!_readPersistedSession && !initialSizeGrouping) WaitUntil(() => transientCaptureObserved || transientCaptureFailure is not null,
                    TimeSpan.FromSeconds(15), "Did not observe the real temporary sort capture boundary.");
                if (transientCaptureFailure is not null) throw new InvalidOperationException("Temporary sort capture control failed.", transientCaptureFailure);
                var contextField = hostType.GetField("_folderSizeSortContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (var host in hosts)
                    sortContexts.Add(contextField.GetValue(host) ?? throw new InvalidOperationException("Host did not create its sort context."));
                if (sortContexts.Distinct().Count() != hosts.Count) throw new InvalidOperationException("Hosts shared sort contexts.");
                // Keep startup measurement free of pre-observation history navigation.
                // Dedicated context/persisted-host cases retain the history regression.
                if (!initialSizeGrouping)
                {
                    var first = hosts[0];
                    void WaitForPath(string path, ulong[] expected)
                    {
                        WaitUntil(() => string.Equals((string)hostType.GetProperty("CurrentPath")!.GetValue(first)!, path, StringComparison.OrdinalIgnoreCase),
                            TimeSpan.FromSeconds(15), "Context navigation did not settle its physical path.");
                        WaitUntil(() => ((string[])readValues.Invoke(null, [hostType, first, expectedItemCount])!)
                            .Select(value => ulong.TryParse(value, out var bytes) ? bytes : ulong.MaxValue).Order().SequenceEqual(expected),
                            TimeSpan.FromSeconds(15), "Context navigation did not restore this directory's values.");
                        if (!ReferenceEquals(contextField.GetValue(first), sortContexts[0])) throw new InvalidOperationException("Navigation replaced the tab context.");
                    }
                    hostType.GetMethod("Navigate")!.Invoke(first, [folders[1]]);
                    WaitForPath(folders[1], expectedSizes[1]);
                    if (!(bool)hostType.GetMethod("GoBack")!.Invoke(first, null)!) throw new InvalidOperationException("Context history back failed.");
                    WaitForPath(folders[0], expectedSizes[0]);
                    if (!(bool)hostType.GetMethod("GoForward")!.Invoke(first, null)!) throw new InvalidOperationException("Context history forward failed.");
                    WaitForPath(folders[1], expectedSizes[1]);
                    if (!(bool)hostType.GetMethod("GoBack")!.Invoke(first, null)!) throw new InvalidOperationException("Context history return failed.");
                    WaitForPath(folders[0], expectedSizes[0]);
                    Console.WriteLine("product-sort-context-navigation: independent-four-contexts=True; cross-directory-reuse=True; back-forward=True; stable-physical-path=True");
                }
            }

            var paneValues = new string[hosts.Count][];
            var paneValueReadyMilliseconds = new long[hosts.Count];
            var paneSortReadyMilliseconds = new long[hosts.Count];
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                var valueReadyClock = Stopwatch.StartNew();
                Console.WriteLine($"profile-owner-multi-root-shell-host: stage=wait-values-{index + 1}");
                var firstValueRead = true;
                WaitUntil(() =>
                {
                    paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, expectedItemCount])!;
                    if (firstValueRead)
                    {
                        Console.WriteLine($"profile-owner-multi-root-shell-host: initial-values-pane-{index + 1}=[{string.Join('|', paneValues[index].Take(4))}]");
                        firstValueRead = false;
                    }
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
                Console.WriteLine($"profile-owner-multi-root-shell-host: stage=wait-sort-{index + 1}");
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
                    }, TimeSpan.FromSeconds(forceSortDeadline ? 5 : 75), $"Product pane {index + 1} did not restore its independent saved recursive-size sort.",
                        pollMilliseconds: 100);
                    paneSortReadyMilliseconds[index] = sortReadyClock.ElapsedMilliseconds;
                    var nativeRows = readNativeRows.Invoke(null, [viewWindow])!;
                    var namesAfterSort = (string[])nativeRows.GetType().GetField("Item2")!.GetValue(nativeRows)!;
                    if (!namesAfterSort.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Product pane {index + 1} Shell values sorted, but native ListView names disagreed with Shell row order: {namesAfterSort.Length} rows; first=[{string.Join('|', namesAfterSort.Take(5))}]; last=[{string.Join('|', namesAfterSort.TakeLast(5))}].");
                }
                catch (TimeoutException exception)
                {
                    for (var failedPane = 0; failedPane < hosts.Count; failedPane++)
                        Console.WriteLine($"product-sort-timeout-trace: pane={failedPane + 1}; trace={hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[failedPane])}");
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
                    var recoveryTrace = hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host);
                    Console.WriteLine($"product-initial-sort-recovery: pane={index + 1};trace=[{recoveryTrace}]");
                    throw new InvalidOperationException($"Product pane {index + 1} saved sort timed out: value-ready-ms={paneValueReadyMilliseconds[index]}; rows={totalRows}/{expectedItemCount}; actual-names={failureNameEdges}; actual-values={failureValueEdges}; name-mismatches={nameMismatches.Length}; first-mismatches=[{mismatchSamples}]; adjacent-sort-violations={sortViolations}; actual-sort=[{string.Join('|', actualSorts)}]; cache=[{failureCacheMetrics}]; expected-direction={expectedDirection}.", exception);
                }
            }

            var orderType = appAssembly.GetType("ExplorerDirs.App.ShellSizeSortOrder", throwOnError: true)!;
            var verifyOrder = orderType.GetMethod("IsOrderedAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            object? ReadVerifiedOrder(object?[] arguments)
            {
                var task = (Task)verifyOrder.Invoke(null, arguments)!;
                WaitUntil(() => task.IsCompleted, TimeSpan.FromSeconds(10), "Native order verification did not finish.");
                if (task.IsFaulted) throw task.Exception!;
                return task.GetType().GetProperty("Result")!.GetValue(task);
            }
            object ReadSettledOrder(object?[] arguments)
            {
                object? result = null;
                WaitUntil(() =>
                {
                    result = ReadVerifiedOrder(arguments);
                    return result is not null;
                }, TimeSpan.FromSeconds(5), "Native rows stayed unavailable during policy verification.");
                return result!;
            }
            for (var index = 0; index < hosts.Count; index++)
            {
                var orderView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[index], null)!;
                try
                {
                    var expectedDirection = index % 2 == 0 ? 1 : -1;
                    if (!Equals(ReadVerifiedOrder([orderView, expectedItemCount, expectedDirection, false, new Func<bool>(() => true)]), true) ||
                        !Equals(ReadVerifiedOrder([orderView, expectedItemCount, -expectedDirection, false, new Func<bool>(() => true)]), false) ||
                        ReadVerifiedOrder([orderView, expectedItemCount + 1, expectedDirection, false, new Func<bool>(() => true)]) is not null ||
                        ReadVerifiedOrder([orderView, expectedItemCount, expectedDirection, false, new Func<bool>(() => false)]) is not null)
                        throw new InvalidOperationException("Native size-order verification did not distinguish correct, wrong, changing-count and retired states.");
                }
                finally { if (orderView != 0) Marshal.Release(orderView); }
            }
            Console.WriteLine("product-size-order-verification: correct=True; wrong-direction=False; changed-count=pending; retired=pending");

            if (initialSizeGrouping)
            {
                for (var index = 0; index < hosts.Count; index++)
                {
                    var host = hosts[index];
                    var nativeWindow = (nint)hostViewWindow.GetValue(host)!;
                    WaitUntil(() => ShellProviderLocationProbe.IsNativeGroupingEnabled(nativeWindow),
                        TimeSpan.FromSeconds(10), "Large grouped startup did not enable native grouping.");
                    // Product display defaults to Auto units; the independent managed formatter
                    // supplies expected text while raw-byte and native row-order checks remain above.
                    var format = appAssembly.GetType("ExplorerDirs.App.ByteSizeFormatter", true)!.GetMethod("Format",
                        BindingFlags.Static | BindingFlags.NonPublic, null, [typeof(ulong)], null)!;
                    var expectedText = (index % 2 == 0 ? expectedSizes[index] : expectedSizes[index].Reverse())
                        .Select(bytes => (string)format.Invoke(null, [bytes])!).ToArray();
                    WaitUntil(() => ShellProviderLocationProbe.ReadNativeListViewCells(nativeWindow, 3)
                        .Select(row => row[1]).SequenceEqual(expectedText), TimeSpan.FromSeconds(10),
                        "Large grouped startup has properties but incorrect native Size cells.");
                    var captured = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                    if (captured.GetType().GetProperty("GroupKey")!.GetValue(captured) is null)
                        throw new InvalidOperationException("Large grouped startup lost its saved group.");
                }
                Console.WriteLine($"product-grouped-large-startup: hosts={hosts.Count}; rows-per-pane={expectedItemCount}; actual-size-order-and-cells=True; native-groups-enabled=True");
            }

            // Keep this targeted case ungrouped; ordinary four-host tests retain grouping coverage.
            if (!large && !_reconcileUserSort)
            {
                // Property getters can be correct while DefView leaves every folder
                // cell blank. Assert the actual displayed text in the production host.
                var readCells = typeof(ShellProviderLocationProbe).GetMethod("ReadNativeListViewCells",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                for (var index = 0; index < hosts.Count; index++)
                {
                    var window = (nint)hostViewWindow.GetValue(hosts[index])!;
                    var expected = (index % 2 == 0 ? expectedSizes[index] : expectedSizes[index].Reverse())
                        .Select(bytes => bytes + " B").ToArray();
                    WaitUntil(() => ((string[][])readCells.Invoke(null, [window, 3])!)
                            .Select(row => row[1]).SequenceEqual(expected), TimeSpan.FromSeconds(8),
                        $"Product pane {index + 1} has size properties but blank/incorrect displayed folder cells.");
                }
                Console.WriteLine($"profile-owner-multi-root-shell-host: actual-folder-size-cells=PASS ({hosts.Count} product HwndHosts).");

                var filterType = appAssembly.GetType("ExplorerDirs.App.QuickFilterSettings", true)!;
                var keyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", true)!;
                var sizeGroup = Activator.CreateInstance(keyType,
                    [new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 12u]);
                var setGroup = hostType.GetMethod("TrySetGroup", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var setFilter = hostType.GetMethod("SetQuickFilter", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var captureView = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                var firstHost = hosts[0];
                var firstWindow = (nint)hostViewWindow.GetValue(firstHost)!;
                Func<bool> current = () => hosts.Contains(firstHost);
                if (!(bool)setGroup.Invoke(firstHost, [sizeGroup, true, current])!)
                    throw new InvalidOperationException("Could not enable native size grouping before filtering.");
                var filter = Activator.CreateInstance(filterType, ["folder-small", false, true, null]);
                if (!(bool)setFilter.Invoke(firstHost, [filter])!)
                    throw new InvalidOperationException("Grouped provider quick filter was rejected.");
                WaitUntil(() =>
                {
                    var cells = (string[][])readCells.Invoke(null, [firstWindow, 3])!;
                    return cells.Length == 1 && cells[0][0] == "folder-small" && cells[0][1] == "5 B";
                }, TimeSpan.FromSeconds(10), "Grouped folder filter did not retain the visible folder's size.");
                var emptyFilter = Activator.CreateInstance(filterType, ["", false, false, null]);
                if (!(bool)setFilter.Invoke(firstHost, [emptyFilter])!)
                    throw new InvalidOperationException("Clearing grouped provider quick filter was rejected.");
                WaitUntil(() =>
                {
                    var cells = (string[][])readCells.Invoke(null, [firstWindow, 3])!;
                    var state = captureView.Invoke(firstHost, null)!;
                    var group = state.GetType().GetProperty("GroupKey")!.GetValue(state);
                    return cells.Select(row => row[1]).SequenceEqual(new[] { "5 B", "29 B" }) &&
                        Equals(group, sizeGroup);
                }, TimeSpan.FromSeconds(10), "Clearing filter lost folder totals, saved size order or size grouping.");
                if (!(bool)setGroup.Invoke(firstHost, [null, true, current])!)
                    throw new InvalidOperationException("Could not restore ungrouped provider view.");
                Console.WriteLine("profile-owner-multi-root-shell-host: grouped-size-filter-roundtrip=PASS (real folder cells, group restored).");
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
            var paneWarmReadMilliseconds = new double[hosts.Count];
            for (var index = 0; index < hosts.Count; index++)
            {
                var host = hosts[index];
                var warmRead = Stopwatch.StartNew();
                paneValues[index] = (string[])readValues.Invoke(null, [hostType, host, expectedItemCount])!;
                paneWarmReadMilliseconds[index] = warmRead.Elapsed.TotalMilliseconds;

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

            for (var traceIndex = 0; traceIndex < hosts.Count; traceIndex++)
                Console.WriteLine($"product-initial-sort-recovery: pane={traceIndex + 1};trace=[{hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[traceIndex])}]");
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The product panes changed the foreground window.");
            if (forceSortDeadline && !deadlineStalled)
                throw new InvalidOperationException("Deadline control did not observe/expire the temporary sort; result is inconclusive.");
            if (forceSortDeadline)
                Console.WriteLine("profile-owner-sort-deadline: PASS (real temporary descending, injected 31-second readiness clock, saved ascending and native row order restored).");
            var valueEdges = paneValues.Select(values => $"{values.Length}:{values[0]}..{values[^1]}");
            var cacheMetrics = adapterModule == 0 ? "diagnostic-counters=unavailable" : ReadCacheMetrics(adapterModule);
            Console.WriteLine("profile-owner-multi-root-shell-host: warm-size-read-ms=[" + string.Join('|',
                paneWarmReadMilliseconds.Select(value => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))) + "]");
            Console.WriteLine($"profile-owner-multi-root-shell-host: large={large}; diagnostic-notification-identity-capture={(adapterModule != 0 ? captureNotificationIdentities.ToString() : "unavailable")}; simultaneous-hosts={hosts.Count}; rows-per-pane={expectedItemCount}; roots=independent; value-ready-ms=[{string.Join('|', paneValueReadyMilliseconds)}]; sort-ready-ms=[{string.Join('|', paneSortReadyMilliseconds)}]; recursive-sort-value-edges=[{string.Join(" || ", valueEdges)}]; source-cache-capacity={cacheCapacity}; cache=[{cacheMetrics}]; selections=[{string.Join('|', paneSelections.Select(values => string.Join(',', values)))}]; focused-rows=[{string.Join('|', paneFocus)}]; parents-hidden=True; foreground-unchanged=True");
            if (_reconcileUserSort)
            {
                var host = hosts[0];
                var window = (nint)hostViewWindow.GetValue(host)!;
                File.WriteAllBytes(Path.Combine(folders[0], "file-middle.bin"), new byte[17]);
                hostType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public)!.Invoke(host, null);
                WaitUntil(() =>
                {
                    var rows = readNativeRows.Invoke(null, [window])!;
                    return (int)rows.GetType().GetField("Item1")!.GetValue(rows)! == 3;
                }, TimeSpan.FromSeconds(8), "Sort category fixture did not enumerate its third row.");
                PumpDispatcher(TimeSpan.FromMilliseconds(500));
                var styleType = appAssembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
                if (!(bool)hostType.GetMethod("SetViewStyle")!.Invoke(host, [Activator.CreateInstance(styleType, ["Probe", _userSortMode, _userSortMode == 1 ? 96 : -1])])!)
                    throw new InvalidOperationException("Could not select requested mode for native command reconciliation.");
                PumpDispatcher(TimeSpan.FromMilliseconds(250));
                var actualMode = hostType.GetMethod("ReadViewStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
                if ((uint)actualMode.GetType().GetField("Item1")!.GetValue(actualMode)! != _userSortMode)
                    throw new InvalidOperationException($"The native mode did not match the requested sort fixture: requested={_userSortMode}; actual={actualMode}.");
                // Use the actual Shell menu without the host's programmatic-sort suppression.
                var menu = hostType.GetMethod("OpenFileCommandMenu", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool), typeof(bool)], null)!
                    .Invoke(host, [true, true]) ?? throw new InvalidOperationException("No native background menu.");
                try
                {
                    var result = menu.GetType().GetMethod("InvokeCanonicalVerb", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, ["sortdescending"]);
                    if (result is not int code || code < 0) throw new InvalidOperationException("Native descending verb failed.");
                }
                finally { ((IDisposable)menu).Dispose(); }
                string[] names = [];
                WaitUntil(() =>
                {
                    var rows = readNativeRows.Invoke(null, [window])!;
                    names = (string[])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                    return names.SequenceEqual(["folder-large", "folder-small", "file-middle.bin"]);
                }, TimeSpan.FromSeconds(8), "Independent descending user sort did not keep folders before files.");
                var values = (string[])readValues.Invoke(null, [hostType, host, 3])!;
                if (!values.SequenceEqual(["29", "5", "17"]))
                    throw new InvalidOperationException($"Wrong independent descending values: {string.Join('|', values)}.");
                var captured = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                var sort = ((System.Collections.IEnumerable)captured.GetType().GetProperty("SortColumns")!.GetValue(captured)!).Cast<object>().Single();
                Console.WriteLine($"product-user-sort-view-state: {captured}");
                var sortKey = sort.GetType().GetProperty("Key")!.GetValue(sort)!;
                if ((Guid)sortKey.GetType().GetProperty("FormatId")!.GetValue(sortKey)! != new Guid("b725f130-47ef-101a-a5f1-02608c9eebac") ||
                    (uint)sortKey.GetType().GetProperty("PropertyId")!.GetValue(sortKey)! != 12 ||
                    (bool)hostType.GetProperty("ViewCaptureFailed")!.GetValue(host)!)
                    throw new InvalidOperationException($"Menu did not retain a complete standard size sort: {sortKey}.");
                if ((int)sort.GetType().GetProperty("Direction")!.GetValue(sort)! != -1)
                    throw new InvalidOperationException("Expected actual descending sort state after native menu command.");
                Console.WriteLine($"product-user-sort-reconciliation: mode={_userSortMode}; native-menu=True; folders-first=True; descending-values=29|5|17");
                var contextField = hostType.GetField("_folderSizeSortContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var context = contextField.GetValue(host)!;
                var activeView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
                nint activeFolder = 0, activeRoot = 0;
                try
                {
                    var persistFolder2 = new Guid("1ac3d9f0-175c-11d1-95be-00609797ea4f");
                    var getFolder = Marshal.GetDelegateForFunctionPointer<GetProbeFolder>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(activeView), 5 * IntPtr.Size));
                    Marshal.ThrowExceptionForHR(getFolder(activeView, in persistFolder2, out activeFolder));
                    var getRoot = Marshal.GetDelegateForFunctionPointer<GetProbeFolderIdentity>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(activeFolder), 5 * IntPtr.Size));
                    Marshal.ThrowExceptionForHR(getRoot(activeFolder, out activeRoot));
                    var contextMatches = (bool)context.GetType().GetMethod("Contains", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(context, [activeRoot])!;
                    Console.WriteLine($"product-user-sort-active-context: matches={contextMatches}");
                    if (!contextMatches) throw new InvalidOperationException("Active folder binding lost the tab sort context.");
                    sortPolicyRoot = CloneProbeRoot(activeRoot);
                    if (sortPolicyRoot == 0) throw new OutOfMemoryException();
                }
                finally
                {
                    if (activeRoot != 0) Marshal.FreeCoTaskMem(activeRoot);
                    if (activeFolder != 0) Marshal.Release(activeFolder);
                    if (activeView != 0) Marshal.Release(activeView);
                }

                if (_hideSortSizeColumn)
                {
                    var columns = ((System.Collections.IEnumerable)captured.GetType().GetProperty("DisplayColumns")!.GetValue(captured)!).Cast<object>()
                        .Where(column => !Equals(column.GetType().GetProperty("Key")!.GetValue(column), sortKey)).ToArray();
                    var nativeColumns = Array.CreateInstance(columns[0].GetType(), columns.Length);
                    for (var index = 0; index < columns.Length; index++) nativeColumns.SetValue(columns[index], index);
                    var hiddenView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
                    try
                    {
                        var columnManager = appAssembly.GetType("ExplorerDirs.App.ShellColumnManager", true)!;
                        var result = (int)columnManager.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public)!
                            .Invoke(null, [hiddenView, nativeColumns, new Func<bool>(() => true)])!;
                        Marshal.ThrowExceptionForHR(result);
                    }
                    finally { if (hiddenView != 0) Marshal.Release(hiddenView); }
                    var hiddenState = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                    var remaining = ((System.Collections.IEnumerable)hiddenState.GetType().GetProperty("DisplayColumns")!.GetValue(hiddenState)!).Cast<object>();
                    if (remaining.Any(column => Equals(column.GetType().GetProperty("Key")!.GetValue(column), sortKey)))
                        throw new InvalidOperationException("Size column was not actually hidden.");
                    Console.WriteLine("product-hidden-size-column: hidden=True; size-sort-retained=True");
                }
                sortPolicyModule = NativeLibrary.Load(_sortContextAdapter!);
                VerifyProbePolicy? verifyPolicy = NativeLibrary.TryGetExport(sortPolicyModule, "VerifySizeSortContextValueForProbe", out var verifyAddress)
                    ? Marshal.GetDelegateForFunctionPointer<VerifyProbePolicy>(verifyAddress) : null;
                foreach (var mixed in new[] { true, false })
                foreach (var direction in new[] { 1, -1 })
                {
                    WaitUntil(() => (bool)hostType.GetProperty("CanSetMixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!,
                        TimeSpan.FromSeconds(5), "Mixed-sort setting was not ready.");
                    selectItem.Invoke(null, [hostType, host, Path.Combine(folders[0], "folder-small")]);
                    var beforeSetting = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                    var beforeSort = ((System.Collections.IEnumerable)beforeSetting.GetType().GetProperty("SortColumns")!.GetValue(beforeSetting)!).Cast<object>().Single();
                    var beforeDirection = (int)beforeSort.GetType().GetProperty("Direction")!.GetValue(beforeSort)!;
                    var setMixed = hostType.GetMethod("SetMixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("Product mixed-sort setting entry point is missing.");
                    if (!(bool)setMixed.Invoke(host, [mixed])!)
                        throw new InvalidOperationException("Product rejected the mixed-sort setting.");
                    string[] settingNames = mixed
                        ? beforeDirection > 0 ? ["folder-small", "file-middle.bin", "folder-large"] : ["folder-large", "file-middle.bin", "folder-small"]
                        : beforeDirection > 0 ? ["folder-small", "folder-large", "file-middle.bin"] : ["folder-large", "folder-small", "file-middle.bin"];
                    WaitUntil(() =>
                    {
                        var rows = readNativeRows.Invoke(null, [window])!;
                        var settingTrace = (string)hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                        return ((string[])rows.GetType().GetField("Item2")!.GetValue(rows)!).SequenceEqual(settingNames) &&
                            settingTrace.LastIndexOf(":finish", StringComparison.Ordinal) > settingTrace.LastIndexOf(":scheduled", StringComparison.Ordinal) &&
                            !(bool)hostType.GetProperty("ViewCaptureFailed")!.GetValue(host)! &&
                            (bool)hostType.GetProperty("MixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)! == mixed;
                    }, TimeSpan.FromSeconds(8), "Mixed-sort setting alone did not apply the native order.");
                    var settingSelection = readSelection.Invoke(null, [window])!;
                    if (!((string[])settingSelection.GetType().GetField("Item1")!.GetValue(settingSelection)!).SequenceEqual(["folder-small"]))
                        throw new InvalidOperationException("Changing the mixed-sort setting lost the selected folder.");
                    var serialField = hostType.GetField("_initialSortRecoverySerial", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var beforeNoop = (long)serialField.GetValue(host)!;
                    if (!(bool)setMixed.Invoke(host, [mixed])! || (long)serialField.GetValue(host)! != beforeNoop)
                        throw new InvalidOperationException("Same-value policy update unnecessarily started sorting.");
                    var confirmedSetting = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                    VerifyMixedPolicyReload(appAssembly, Path.Combine(profile, $"mixed-{mixed}-{direction}.ini"), folders[0], confirmedSetting, mixed);
                    Console.WriteLine($"product-mixed-option: mixed={mixed}; setting-alone-order=True; selection-preserved=True; same-value-noop=True; ini-copy-default=True");
                    var nextMenu = hostType.GetMethod("OpenFileCommandMenu", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool), typeof(bool)], null)!.Invoke(host, [true, true])!;
                    try
                    {
                        var code = nextMenu.GetType().GetMethod("InvokeCanonicalVerb", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(nextMenu, [direction > 0 ? "sortascending" : "sortdescending"]);
                        if (code is not int result || result < 0) throw new InvalidOperationException("Policy switch sort verb failed.");
                    }
                    finally { ((IDisposable)nextMenu).Dispose(); }
                    var expectedPolicy = (mixed ? 1u : 0u) | (direction < 0 ? 2u : 0u);
                    string[] expectedNames = mixed
                        ? direction > 0 ? ["folder-small", "file-middle.bin", "folder-large"] : ["folder-large", "file-middle.bin", "folder-small"]
                        : direction > 0 ? ["folder-small", "folder-large", "file-middle.bin"] : ["folder-large", "folder-small", "file-middle.bin"];
                    string[] actualPolicyNames = [];
                    try
                    {
                        WaitUntil(() =>
                        {
                            var rows = readNativeRows.Invoke(null, [window])!;
                            actualPolicyNames = (string[])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                            return actualPolicyNames.SequenceEqual(expectedNames);
                        }, TimeSpan.FromSeconds(5), $"Product mixed={mixed}/direction={direction} did not produce the required native row order.");
                    }
                    catch (TimeoutException error)
                    {
                        if (NativeLibrary.TryGetExport(sortPolicyModule, "GetProductCompareDiagnosticCount", out var countAddress) &&
                            NativeLibrary.TryGetExport(sortPolicyModule, "GetProductCompareDiagnostic", out var readAddress))
                        {
                            var count = Marshal.GetDelegateForFunctionPointer<GetProbeComparisonCount>(countAddress)();
                            var read = Marshal.GetDelegateForFunctionPointer<ReadProbeComparison>(readAddress);
                            for (var index = Math.Max(0, count - 24); index < count; index++)
                            {
                                var text = new StringBuilder(4096);
                                Marshal.ThrowExceptionForHR(read(index, text, (uint)text.Capacity));
                                Console.WriteLine("product-user-sort-compare: " + text);
                            }
                        }
                        throw new InvalidOperationException($"Policy row failure: mixed={mixed}; direction={direction}; policy-verify=0x{(verifyPolicy is null ? "not-exported" : verifyPolicy(sortPolicyRoot, expectedPolicy).ToString("X8"))}; actual={string.Join('|', actualPolicyNames)}; values={string.Join('|', (string[])readValues.Invoke(null, [hostType, host, 3])!)}", error);
                    }
                    var policyView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
                    try
                    {
                        if (!Equals(ReadSettledOrder([policyView, 3, direction, mixed, new Func<bool>(() => true)]), true) ||
                            !Equals(ReadSettledOrder([policyView, 3, direction, !mixed, new Func<bool>(() => true)]), false))
                            throw new InvalidOperationException("Native order verifier did not distinguish mixed and folder-first rows.");
                    }
                    finally { if (policyView != 0) Marshal.Release(policyView); }
                    Console.WriteLine($"product-user-sort-policy-switch: mixed={mixed}; direction={direction}; names={string.Join('|', expectedNames)}");
                }

                if (_userSortMode == 3 && !_hideSortSizeColumn)
                {
                    var setGroup = hostType.GetMethod("TrySetGroup", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    if (!(bool)setGroup.Invoke(host, [sortKey, true, new Func<bool>(() => true)])!)
                        throw new InvalidOperationException("Could not enable grouping for mixed-policy preservation.");
                    WaitUntil(() =>
                    {
                        var state = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                        return Equals(state.GetType().GetProperty("GroupKey")!.GetValue(state), sortKey);
                    }, TimeSpan.FromSeconds(5), "Native size grouping did not become observable.");
                    foreach (var mixed in new[] { true, false })
                    {
                        WaitUntil(() => (bool)hostType.GetProperty("CanSetMixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!,
                            TimeSpan.FromSeconds(5), "Grouped mixed-policy setting was not ready.");
                        var beforeSerial = (long)hostType.GetField("_initialSortRecoverySerial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                        if (!(bool)hostType.GetMethod("SetMixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [mixed])!)
                            throw new InvalidOperationException("Grouped mixed-policy update was rejected.");
                        var groupedState = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                        if (!Equals(groupedState.GetType().GetProperty("GroupKey")!.GetValue(groupedState), sortKey) ||
                            !Equals(groupedState.GetType().GetProperty("MixedSizeSort")!.GetValue(groupedState), mixed) ||
                            (long)hostType.GetField("_initialSortRecoverySerial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)! != beforeSerial)
                            throw new InvalidOperationException($"Grouped policy update mismatch: group={groupedState.GetType().GetProperty("GroupKey")!.GetValue(groupedState)}; mixed={groupedState.GetType().GetProperty("MixedSizeSort")!.GetValue(groupedState)}; expected={mixed}; before-serial={beforeSerial}; after-serial={hostType.GetField("_initialSortRecoverySerial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)}.");
                        VerifyMixedPolicyReload(appAssembly, Path.Combine(profile, $"grouped-mixed-{mixed}.ini"), folders[0], groupedState, mixed);
                        // Native Size grouping assigns files and folders to separate groups.
                        // Mixed policy must not flatten those groups into one global order.
                        WaitUntil(() =>
                        {
                            var rows = readNativeRows.Invoke(null, [window])!;
                            var names = (string[])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                            if (names.Length != 3) return false;
                            var groups = ShellProviderLocationProbe.ReadNativeGroupIds(window);
                            var fileIndex = Array.IndexOf(names, "file-middle.bin");
                            var smallIndex = Array.IndexOf(names, "folder-small");
                            var largeIndex = Array.IndexOf(names, "folder-large");
                            return fileIndex >= 0 && smallIndex >= 0 && largeIndex >= 0 &&
                                groups.Length == 3 && groups[smallIndex] == groups[largeIndex] &&
                                groups[fileIndex] != groups[smallIndex] && largeIndex < smallIndex;
                        }, TimeSpan.FromSeconds(5), "Size grouping lost native categories or descending order within the folder group.");
                        Console.WriteLine($"product-grouped-row-order: mixed={mixed}; file-folder-groups=separate; folder-group-descending=True");
                    }
                    foreach (var mixed in new[] { true, false })
                    foreach (var direction in new[] { 1, -1 })
                    {
                        WaitUntil(() => (bool)hostType.GetProperty("CanSetMixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!,
                            TimeSpan.FromSeconds(8), "Previous ungroup recovery did not finish.");
                        if (!(bool)setGroup.Invoke(host, [sortKey, true, new Func<bool>(() => true)])!)
                            throw new InvalidOperationException("Could not regroup for policy transition.");
                        WaitUntil(() =>
                        {
                            var state = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                            return Equals(state.GetType().GetProperty("GroupKey")!.GetValue(state), sortKey);
                        }, TimeSpan.FromSeconds(5), "Regrouping did not become observable.");
                        if (!(bool)hostType.GetMethod("SetMixedSizeSort", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [mixed])!)
                            throw new InvalidOperationException("Grouped transition policy was rejected.");
                        var groupedMenu = hostType.GetMethod("OpenFileCommandMenu", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool), typeof(bool)], null)!.Invoke(host, [true, true])!;
                        try
                        {
                            var result = (int)groupedMenu.GetType().GetMethod("InvokeCanonicalVerb", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(groupedMenu, [direction > 0 ? "sortascending" : "sortdescending"])!;
                            Marshal.ThrowExceptionForHR(result);
                        }
                        finally { ((IDisposable)groupedMenu).Dispose(); }
                        WaitUntil(() =>
                        {
                            var state = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                            var sorts = ((System.Collections.IEnumerable)state.GetType().GetProperty("SortColumns")!.GetValue(state)!).Cast<object>().ToArray();
                            return sorts.Length == 1 && Equals(sorts[0].GetType().GetProperty("Key")!.GetValue(sorts[0]), sortKey) &&
                                Equals(sorts[0].GetType().GetProperty("Direction")!.GetValue(sorts[0]), direction) &&
                                hostType.GetField("_nativeSortReconciliation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is null;
                        }, TimeSpan.FromSeconds(5), "Grouped sort direction did not settle before ungrouping.");
                        if (!(bool)setGroup.Invoke(host, [null, true, new Func<bool>(() => true)])!)
                            throw new InvalidOperationException("Could not clear native grouping after policy changes.");
                        string[] expected = mixed
                            ? direction > 0 ? ["folder-small", "file-middle.bin", "folder-large"] : ["folder-large", "file-middle.bin", "folder-small"]
                            : direction > 0 ? ["folder-small", "folder-large", "file-middle.bin"] : ["folder-large", "folder-small", "file-middle.bin"];
                        string transitionState = "not-read";
                        try
                        {
                            WaitUntil(() =>
                            {
                                var state = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                                var rows = readNativeRows.Invoke(null, [window])!;
                                var names = (string[])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                                transitionState = $"group={state.GetType().GetProperty("GroupKey")!.GetValue(state)}; names={string.Join('|', names)}";
                                return state.GetType().GetProperty("GroupKey")!.GetValue(state) is null && names.SequenceEqual(expected);
                            }, TimeSpan.FromSeconds(8), $"Ungroup transition failed: mixed={mixed}; direction={direction}.");
                        }
                        catch (TimeoutException error)
                        {
                            throw new InvalidOperationException($"Ungroup transition state: {transitionState}; mixed={mixed}; direction={direction}.", error);
                        }
                        Console.WriteLine($"product-ungroup-policy: mixed={mixed}; direction={direction}; native-row-order=True");
                    }
                    Console.WriteLine("product-grouped-mixed-option: group-preserved=True; ungrouped-recovery=False; ini-copy-default=True");
                }

                if (_userSortMode == 4)
                {
                    var header = (nint)typeof(ShellProviderLocationProbe).GetMethod("FindSortHeader", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [window])!;
                    var click = typeof(ShellProviderLocationProbe).GetMethod("ClickHeaderColumn", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var displayColumns = ((System.Collections.IEnumerable)captured.GetType().GetProperty("DisplayColumns")!.GetValue(captured)!).Cast<object>().ToArray();
                    var sizeHeaderIndex = Array.FindIndex(displayColumns, column => Equals(column.GetType().GetProperty("Key")!.GetValue(column), sortKey));
                    if (sizeHeaderIndex < 0) throw new InvalidOperationException("No size column in detailed view.");
                    selectItem.Invoke(null, [hostType, host, Path.Combine(folders[0], "folder-small")]);
                    foreach (var headerDirection in new[] { 1, -1 })
                    {
                        click.Invoke(null, [header, sizeHeaderIndex]);
                        string[] expectedHeaderNames = headerDirection > 0
                            ? ["folder-small", "folder-large", "file-middle.bin"]
                            : ["folder-large", "folder-small", "file-middle.bin"];
                        WaitUntil(() =>
                        {
                            var rows = readNativeRows.Invoke(null, [window])!;
                            if (!((string[])rows.GetType().GetField("Item2")!.GetValue(rows)!).SequenceEqual(expectedHeaderNames)) return false;
                            var headerState = hostType.GetMethod("CaptureViewState")!.Invoke(host, null)!;
                            var headerSort = ((System.Collections.IEnumerable)headerState.GetType().GetProperty("SortColumns")!.GetValue(headerState)!).Cast<object>().Single();
                            var recoveryTrace = (string)hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                            var recoveryFinished = recoveryTrace.LastIndexOf(":finish", StringComparison.Ordinal) >
                                recoveryTrace.LastIndexOf(":scheduled", StringComparison.Ordinal);
                            return (int)headerSort.GetType().GetProperty("Direction")!.GetValue(headerSort)! == headerDirection && recoveryFinished &&
                                !(bool)hostType.GetProperty("ViewCaptureFailed")!.GetValue(host)!;
                        }, TimeSpan.FromSeconds(8), $"Native size header click did not retain folder-first direction {headerDirection}.");
                        var afterHeaderSelection = readSelection.Invoke(null, [window])!;
                        if (!((string[])afterHeaderSelection.GetType().GetField("Item1")!.GetValue(afterHeaderSelection)!).SequenceEqual(["folder-small"]))
                            throw new InvalidOperationException($"Header reconciliation lost the selected file identity: [{string.Join('|', (string[])afterHeaderSelection.GetType().GetField("Item1")!.GetValue(afterHeaderSelection)!)}]; trace={hostType.GetProperty("InitialSortRecoveryStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)}");
                        Console.WriteLine($"product-user-sort-header: direction={headerDirection}; actual-row-order=True; selected-folder-preserved=True");
                    }
                }
            }
            if (_persistedSessionPath is not null)
            {
                if (!_readPersistedSession)
                {
                    var tabType = appAssembly.GetType("ExplorerDirs.App.TabSettings", true)!;
                    var tabsType = typeof(List<>).MakeGenericType(tabType);
                    var panes = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tabsType))!;
                    for (var index = 0; index < hosts.Count; index++)
                    {
                        var capturedView = hostType.GetMethod("CaptureViewState")!.Invoke(hosts[index], null)!;
                        if (!Equals(capturedView.GetType().GetProperty("MixedSizeSort")!.GetValue(capturedView), index % 2 == 0))
                            throw new InvalidOperationException("Host capture lost its independent mixed policy.");
                        var tabs = (System.Collections.IList)Activator.CreateInstance(tabsType)!;
                        tabs.Add(Activator.CreateInstance(tabType, [folders[index], capturedView, null, false, false]));
                        panes.Add(tabs);
                    }
                    var stateType = appAssembly.GetType("ExplorerDirs.App.SessionState", true)!;
                    var session = stateType.GetConstructors().Single(value => value.GetParameters().Length == 11)
                        .Invoke([4, 4u, panes, 0, new int[4], 16, new bool[4], null, null, null, null]);
                    var storeType = appAssembly.GetType("ExplorerDirs.App.SessionStore", true)!;
                    var writer = Activator.CreateInstance(storeType, [_persistedSessionPath, _persistedSessionPath + ".legacy"])!;
                    storeType.GetMethod("Save")!.Invoke(writer, [session]);
                }
                Console.WriteLine($"product-persisted-sort: phase={(_readPersistedSession ? "read" : "write")}; pid={Environment.ProcessId}; panes=4; mixed=True|False|True|False; actual-host-sort-order=True; physical-paths=fresh-fixture");
            }
            return 0;
        }
        finally
        {
            UnknownRelease.Set(); // Unblock worker callbacks before unregister waits for them.
            if (configureUnavailable is not null) Marshal.ThrowExceptionForHR(configureUnavailable(0, 0, false));
            GC.KeepAlive(unavailableQuery);
            deadlineStall?.Stop();
            if (sortPolicyRoot != 0) Marshal.FreeCoTaskMem(sortPolicyRoot);
            if (sortPolicyModule != 0) NativeLibrary.Free(sortPolicyModule);
            transientCaptureTimer?.Stop();
            Console.WriteLine("profile-owner-multi-root-shell-host: stage=cleanup");
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            // Detaching RootVisual parks HwndHosts; explicit disposal is required to
            // exercise product browser/context teardown rather than process-exit cleanup.
            foreach (var host in hosts)
                if (host is IDisposable disposable) disposable.Dispose();
            var contextsRetired = true;
            foreach (var context in sortContexts)
            {
                try { context.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(context, [false, 1]); contextsRetired = false; }
                catch (TargetInvocationException error) when (error.InnerException is ObjectDisposedException) { }
            }
            if (sortContexts.Count != 0) Console.WriteLine($"product-sort-context-close: count={sortContexts.Count}; retired={contextsRetired}");
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            if (owner is IDisposable lifetime) lifetime.Dispose();
            if (adapterModule != 0) NativeLibrary.Free(adapterModule);
            var registrationRemains = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(classKeyPath) is not null ||
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (!contextsRetired) throw new InvalidOperationException("Closing product hosts did not retire sort contexts.");
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

    private static void VerifyMixedPolicyReload(Assembly appAssembly, string profilePath, string path, object view, bool mixed)
    {
        var tabType = appAssembly.GetType("ExplorerDirs.App.TabSettings", true)!;
        var stateType = appAssembly.GetType("ExplorerDirs.App.SessionState", true)!;
        var storeType = appAssembly.GetType("ExplorerDirs.App.SessionStore", true)!;
        var tabsType = typeof(List<>).MakeGenericType(tabType);
        var panes = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tabsType))!;
        for (var pane = 0; pane < 4; pane++)
        {
            var tabs = (System.Collections.IList)Activator.CreateInstance(tabsType)!;
            if (pane == 0)
            {
                var copy = view.GetType().GetMethod("Copy")!.Invoke(view, null)!;
                var defaultView = view.GetType().GetMethod("Default", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [4u, -1])!;
                foreach (var tabView in new[] { view, copy, defaultView })
                    tabs.Add(Activator.CreateInstance(tabType, [path, tabView, null, false, false]));
            }
            panes.Add(tabs);
        }
        var constructor = stateType.GetConstructors().Single(value => value.GetParameters().Length == 11);
        var session = constructor.Invoke([4, 4u, panes, 0, new int[4], 16, new bool[4], null, null, null, null]);
        var writer = Activator.CreateInstance(storeType, [profilePath, profilePath + ".legacy"])!;
        storeType.GetMethod("Save")!.Invoke(writer, [session]);
        var reader = Activator.CreateInstance(storeType, [profilePath, profilePath + ".legacy"])!;
        var restored = storeType.GetMethod("Load")!.Invoke(reader, null)!;
        var firstPane = ((System.Collections.IEnumerable)restored.GetType().GetProperty("Panes")!.GetValue(restored)!).Cast<object>().First();
        var flags = ((System.Collections.IEnumerable)firstPane).Cast<object>().Select(tab =>
        {
            var restoredView = tab.GetType().GetProperty("View")!.GetValue(tab)!;
            return (bool)restoredView.GetType().GetProperty("MixedSizeSort")!.GetValue(restoredView)!;
        }).ToArray();
        if (!flags.SequenceEqual([mixed, mixed, false]))
            throw new InvalidOperationException("Actual host settings did not preserve copy/default policies through INI reload.");
    }

    private static bool ExpireSortReadinessClockForProbe()
    {
        // Test only: expire the captured clock, not the UI thread. Sleeping the STA can
        // time out unrelated Shell work and does not isolate the product deadline branch.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (var field in typeof(Dispatcher).GetFields(flags))
        {
            if (field.GetValue(Dispatcher.CurrentDispatcher) is not System.Collections.IEnumerable timers) continue;
            foreach (var timer in timers.OfType<DispatcherTimer>().ToArray())
            foreach (var timerField in typeof(DispatcherTimer).GetFields(flags))
            {
                if (timerField.GetValue(timer) is not Delegate callbacks) continue;
                foreach (var callback in callbacks.GetInvocationList())
                {
                    var closure = callback.Target;
                    if (closure is null) continue;
                    var closureType = closure.GetType();
                    if (closureType.GetField("readinessWait", flags)?.GetValue(closure) is not Stopwatch clock ||
                        closureType.GetField("oppositeApplied", flags)?.GetValue(closure) is not true || !clock.IsRunning) continue;
                    var start = typeof(Stopwatch).GetField("_startTimeStamp", flags)
                        ?? throw new InvalidOperationException("This runtime does not expose the probe's expected Stopwatch field.");
                    start.SetValue(clock, Stopwatch.GetTimestamp() - 31 * Stopwatch.Frequency);
                    return true;
                }
            }
        }
        return false;
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
        // Those scan counters increment only in NATIVEPROBE builds. Their zero
        // exports in the product diagnostic DLL are not measured absence of IO.
        long Read64(string name) => Marshal.GetDelegateForFunctionPointer<GetCounter64>(NativeLibrary.GetExport(module, name))();
        int Read32(string name) => Marshal.GetDelegateForFunctionPointer<GetCounter32>(NativeLibrary.GetExport(module, name))();
        var capturedAsync = Read32("GetProductAsyncItemNotificationIdentityCount");
        var capturedView = Read32("GetProductViewItemNotificationIdentityCount");
        var droppedAsync = Read32("GetProductAsyncItemNotificationIdentityDroppedCount");
        var droppedView = Read32("GetProductViewItemNotificationIdentityDroppedCount");
        if (droppedAsync != 0 || droppedView != 0)
            throw new InvalidOperationException($"Notification identity capture is incomplete: dropped async={droppedAsync}, view={droppedView}.");
        // Resolve samples only after the value/sort timings, so identity verification cannot perturb them.
        var resolvedIdentities = 0;
        void VerifyCapturedNames(int count, string getterExport)
        {
            if (count == 0) return;
            var getter = Marshal.GetDelegateForFunctionPointer<GetNotificationIdentity>(NativeLibrary.GetExport(module, getterExport));
            foreach (var index in new[] { 0, count / 2, count - 1 }.Distinct())
            {
                var name = new StringBuilder(32768);
                Marshal.ThrowExceptionForHR(getter(index, name, (uint)name.Capacity));
                if (string.IsNullOrWhiteSpace(name.ToString()))
                    throw new InvalidOperationException("Captured notification PIDL did not resolve to a parsing name.");
                resolvedIdentities++;
            }
        }
        VerifyCapturedNames(capturedAsync, "GetProductAsyncItemNotificationIdentity");
        VerifyCapturedNames(capturedView, "GetProductViewItemNotificationIdentity");
        var comparisons = Read64("GetProductRecursiveCompareCount");
        var samples = Read64("GetProductRecursiveCompareTimingSamples");
        var sampledTicks = Read64("GetProductRecursiveCompareSampledTicks");
        var maximumTicks = Read64("GetProductRecursiveCompareMaximumSampleTicks");
        var fastHits = Read64("GetProductRecursiveCompareFastPathHits");
        var fastMisses = Read64("GetProductRecursiveCompareFastPathMisses");
        var meanMicroseconds = samples > 0 ? sampledTicks * 1_000_000.0 / Stopwatch.Frequency / samples : 0;
        var maximumMicroseconds = maximumTicks * 1_000_000.0 / Stopwatch.Frequency;
        return FormattableString.Invariant($"entries={snapshots};payload={payload};hits={hits};misses={misses};evictions={evictions};compare-count={comparisons};compare-samples={samples};sampled-mean-us={meanMicroseconds:F2};sampled-max-us={maximumMicroseconds:F2};size-fast-hits={fastHits};size-fast-misses={fastMisses};captured-notification-identities={capturedAsync}/{capturedView};resolved-notification-name-samples={resolvedIdentities};dropped-notification-identities={droppedAsync}/{droppedView};scan-counters=unavailable-in-product-diagnostics");
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetNotificationIdentity(int index, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter32();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long GetCounter64();

}
