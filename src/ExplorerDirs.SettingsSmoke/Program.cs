using ExplorerDirs.App;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

var root = Path.Combine(AppContext.BaseDirectory, "profile-smoke");
Directory.CreateDirectory(root);
var ini = Path.Combine(root, "ExplorerDirs.ini");
var legacy = Path.Combine(root, "session.json");
var store = new SessionStore(ini, legacy);
var sortKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);
var groupKey = new PropertyKeyState(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
var mutableSorts = new List<SortColumnState> { new(sortKey, 1) };
var cachedView = new ViewState(4, 16, mutableSorts, groupKey, true).Copy();
mutableSorts.Clear();
var first = new TabSettings(@"C:\项目=测试", new ViewState(4, 16,
    [new SortColumnState(sortKey, -1)], groupKey, false));
var second = new TabSettings(@"C:\项目=测试", ViewState.Default(6));
var state = new SessionState(2, 3, [[first, second], [], [], []]);

try
{
    Check(cachedView.SortColumns.Count == 1 && cachedView.SortColumns[0].Key == sortKey,
          "cached view state keeps independent sort columns after its source changes");
    var shellView = new FakeShellViewStateSource
    {
        Mode = 3,
        IconSize = 24,
        Sorts = [new SortColumnState(sortKey, 1)],
        Group = groupKey,
        GroupAscending = false
    };
    var captured = ShellViewStateCapture.Capture(cachedView, shellView, out var captureComplete);
    Check(captureComplete && captured.Mode == 3 && captured.IconSize == 24 &&
          captured.SortColumns.SequenceEqual(shellView.Sorts) && captured.GroupKey == groupKey &&
          !captured.GroupAscending, "complete Shell read replaces every cached view field");
    foreach (var failedStep in new[] { "mode", "count", "columns", "group", "invalid-count", "short-columns" })
    {
        shellView.FailedStep = failedStep;
        captured = ShellViewStateCapture.Capture(cachedView, shellView, out captureComplete);
        Check(!captureComplete && captured.Mode == cachedView.Mode &&
              captured.IconSize == cachedView.IconSize &&
              captured.SortColumns.SequenceEqual(cachedView.SortColumns) &&
              captured.GroupKey == cachedView.GroupKey &&
              captured.GroupAscending == cachedView.GroupAscending,
              $"{failedStep} leaves the last complete Shell view state intact");
    }
    shellView.FailedStep = null;
    captured = ShellViewStateCapture.Capture(cachedView, shellView, out captureComplete);
    Check(captureComplete && captured.Mode == 3,
          "a later complete Shell read clears the capture failure");
    var noSortView = new FakeShellViewStateSource
    {
        Mode = 4,
        IconSize = 16,
        FailedStep = "columns"
    };
    captured = ShellViewStateCapture.Capture(cachedView, noSortView, out captureComplete);
    Check(captureComplete && captured.SortColumns.Count == 0 && captured.GroupKey is null,
          "zero sort columns clear the cache without querying a column array");
    var mutableShellSorts = new List<SortColumnState> { new(sortKey, -1) };
    captured = ShellViewStateCapture.Capture(cachedView, new FakeShellViewStateSource
    {
        Mode = 4,
        IconSize = 16,
        Sorts = mutableShellSorts
    }, out captureComplete);
    mutableShellSorts.Clear();
    Check(captureComplete && captured.SortColumns.Count == 1,
          "captured sort columns do not alias the Shell reader's list");
    Check(StartupDirectory.Parse([]) == new StartupDirectoryRequest(null, null),
          "startup without a directory restores the normal session");
    Check(StartupDirectory.Parse([@"C:\Users\marvin"]).Directory == @"C:\Users\marvin" &&
          StartupDirectory.Parse([@"\\server\share\docs"]).Directory == @"\\server\share\docs",
          "local and UNC directory arguments keep their normalized paths");
    Check(StartupDirectory.Parse(["."]).Directory == Path.GetFullPath("."),
          "relative startup directory resolves against the caller's current directory");
    Check(StartupDirectory.Parse(["", "extra"]).Directory is null &&
          StartupDirectory.Parse(["\0"]).Warning is not null,
          "unsupported or invalid arguments do not become a navigation target");
    var routerProfile = Path.Combine(root, "router-smoke", "ExplorerDirs.ini");
    var routerOtherProfile = Path.Combine(root, "router-other", "ExplorerDirs.ini");
    Check(FolderOpenRouter.PipeName(routerProfile) != FolderOpenRouter.PipeName(routerOtherProfile),
          "folder routing is scoped to its portable profile");
    var routedPath = Path.Combine(root, "routed-folder");
    Check(FolderOpenRouter.TryForwardAsync(routerProfile, routedPath,
              TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult() == FolderForwardResult.Unavailable,
          "no running window leaves the directory request with the launcher");
    var routedPaths = new List<string>();
    using (var router = FolderOpenRouter.TryStart(routerProfile, path =>
           {
               lock (routedPaths) routedPaths.Add(path);
               return Task.FromResult(!path.EndsWith("deny", StringComparison.Ordinal));
           }) ?? throw new Exception("folder router did not start"))
    {
        Check(FolderOpenRouter.TryStart(routerProfile, _ => Task.FromResult(true)) is null,
              "only one window owns the folder route for a profile");
        Check(FolderOpenRouter.TryForwardAsync(routerProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "a running window acknowledges a newly opened directory tab");
        Check(FolderOpenRouter.TryForwardAsync(routerProfile, Path.Combine(root, "deny"), TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected,
              "a rejected folder request is not reported as opened");
        Check(FolderOpenRouter.TryForwardAsync(routerProfile, "relative", TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Rejected,
              "the pipe does not accept relative paths from another process");
        var parallelPaths = Enumerable.Range(0, 4)
            .Select(index => Path.Combine(root, "parallel-" + index)).ToArray();
        var parallelForwards = parallelPaths.Select(path => FolderOpenRouter.TryForwardAsync(
            routerProfile, path, TimeSpan.FromSeconds(3))).ToArray();
        Task.WaitAll(parallelForwards);
        Check(parallelForwards.All(task => task.Result == FolderForwardResult.Accepted),
              "simultaneous directory requests all reach the existing process");
    }
    Check(routedPaths.Count == 6 && routedPaths[0] == routedPath,
          "only valid absolute folder requests reach the window callback");
    var unexpectedProfile = Path.Combine(root, "router-unexpected", "ExplorerDirs.ini");
    using (var unexpectedServer = new NamedPipeServerStream(FolderOpenRouter.PipeName(unexpectedProfile),
           PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
           PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance))
    {
        var unexpectedResponse = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await unexpectedServer.WaitForConnectionAsync(deadline.Token);
            var header = new byte[4];
            await unexpectedServer.ReadExactlyAsync(header, deadline.Token);
            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await unexpectedServer.ReadExactlyAsync(payload, deadline.Token);
            await unexpectedServer.WriteAsync(new byte[] { 2 }, deadline.Token);
        });
        var result = FolderOpenRouter.TryForwardAsync(unexpectedProfile, routedPath,
            TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        unexpectedResponse.GetAwaiter().GetResult();
        Check(result == FolderForwardResult.Uncertain,
              "an unknown pipe response does not pretend the directory was rejected");
    }
    var stalledProfile = Path.Combine(root, "router-stalled", "ExplorerDirs.ini");
    using (var stalledRouter = FolderOpenRouter.TryStart(stalledProfile,
               _ => Task.FromResult(true)) ?? throw new Exception("stalled router did not start"))
    using (var stalledClient = new NamedPipeClientStream(".", FolderOpenRouter.PipeName(stalledProfile),
               PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        stalledClient.Connect(2000); // Connect, then never send the request header.
        Check(FolderOpenRouter.TryForwardAsync(stalledProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "a connected client that sends no request cannot block later folder launches indefinitely");
        using var partialClient = new NamedPipeClientStream(".", FolderOpenRouter.PipeName(stalledProfile),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        partialClient.Connect(2000);
        partialClient.Write(new byte[] { 8, 0, 0, 0 }); // Claim a path but send no payload.
        partialClient.Flush();
        Check(FolderOpenRouter.TryForwardAsync(stalledProfile, routedPath, TimeSpan.FromSeconds(2))
                  .GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "an incomplete directory payload cannot block later folder launches indefinitely");
    }
    var startingProfile = Path.Combine(root, "router-starting", "ExplorerDirs.ini");
    var windowReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using (var startingRouter = FolderOpenRouter.TryStart(startingProfile, async _ =>
           {
               await windowReady.Task;
               return true;
           }) ?? throw new Exception("starting folder router did not start"))
    {
        var pendingForward = FolderOpenRouter.TryForwardAsync(startingProfile, routedPath,
            TimeSpan.FromSeconds(2));
        Check(!pendingForward.Wait(TimeSpan.FromMilliseconds(100)),
              "a folder request waits while the receiving window initializes");
        windowReady.SetResult(true);
        Check(pendingForward.GetAwaiter().GetResult() == FolderForwardResult.Accepted,
              "the startup race forwards after the first window becomes ready");
    }
    Check(ShellItemKind.IsFileSystemFolder(0x60000000) &&
          !ShellItemKind.IsFileSystemFolder(0x40000000) &&
          !ShellItemKind.IsFileSystemFolder(0x60400000) &&
          !ShellItemKind.IsFileSystemFolder(0x20000000),
          "only ordinary filesystem folders get the new-tab directory command");
    Check(ShellMenuCommand.TryGetMenuId((nint)0xE000, IntPtr.Zero, out var menuId) &&
          menuId == 0xE000 &&
          !ShellMenuCommand.TryGetMenuId((nint)0x1E000, IntPtr.Zero, out _) &&
          !ShellMenuCommand.TryGetMenuId((nint)0xE000, (nint)123, out _) &&
          !ShellMenuCommand.TryGetMenuId(IntPtr.Zero, IntPtr.Zero, out _),
          "custom menu commands ignore accelerators, control notifications, and separator IDs");
    var menuTargets = new ShellMenuTargetTracker();
    var backgroundPoint = new ShellMenuPoint(12, 88);
    var probeCalls = 0;
    Task<ShellMenuTarget> BackgroundProbe(ShellMenuPoint point)
    {
        probeCalls++;
        Check(point == backgroundPoint, "background probe receives the mouse location");
        return Task.FromResult(ShellMenuTarget.Background);
    }
    menuTargets.MouseDown(backgroundPoint, BackgroundProbe);
    menuTargets.ContextMenu((nint)((88 << 16) | 12), BackgroundProbe);
    Check(menuTargets.Resolve() == ShellMenuTarget.Background && probeCalls == 1,
          "background right-click ignores a still-selected Shell item and reuses its point probe");
    long menuClock = 100;
    var staleMenuTarget = new ShellMenuTargetTracker(() => menuClock);
    staleMenuTarget.MouseDown(backgroundPoint, _ => Task.FromResult(ShellMenuTarget.Item));
    menuClock += 5001;
    Check(staleMenuTarget.Resolve() == ShellMenuTarget.Unknown,
          "old right-button hit cannot leak into an unrelated later menu");
    menuTargets.MouseDown(new ShellMenuPoint(30, 90),
        _ => Task.FromResult(ShellMenuTarget.Item));
    menuTargets.ContextMenu((nint)((90 << 16) | 30),
        _ => throw new Exception("matching menu point started a second probe"));
    Check(menuTargets.Resolve() == ShellMenuTarget.Item,
          "item right-click can use the Shell selection after hit confirmation");
    var slowMenuProbe = new TaskCompletionSource<ShellMenuTarget>();
    menuTargets.MouseDown(new ShellMenuPoint(40, 90), _ => slowMenuProbe.Task);
    Check(menuTargets.Resolve() == ShellMenuTarget.Unknown,
          "unfinished mouse hit test cannot borrow the Shell selection");
    menuTargets.ContextMenu((nint)(-1), _ => throw new Exception("keyboard menu probed the mouse"));
    slowMenuProbe.SetResult(ShellMenuTarget.Item);
    Check(menuTargets.Resolve() == ShellMenuTarget.KeyboardSelection,
          "keyboard context menu uses selection rather than a stale mouse result");
    menuTargets.Reset();
    long keyboardMenuClock = 20;
    var keyboardMenuTarget = new ShellMenuTargetTracker(() => keyboardMenuClock);
    keyboardMenuTarget.KeyboardMenuRequested();
    Check(keyboardMenuTarget.Resolve() == ShellMenuTarget.KeyboardSelection,
          "Shell menu key can establish keyboard origin before WM_CONTEXTMENU arrives");
    keyboardMenuClock += 5001;
    Check(keyboardMenuTarget.Resolve() == ShellMenuTarget.Unknown,
          "unhandled Shell menu key does not leave a permanent selection target");
    var changedPoint = new ShellMenuPoint(55, 91);
    menuTargets.MouseDown(new ShellMenuPoint(54, 91),
        _ => Task.FromResult(ShellMenuTarget.Item));
    menuTargets.ContextMenu((nint)((91 << 16) | 55), point =>
        Task.FromResult(point == changedPoint ? ShellMenuTarget.Background : ShellMenuTarget.Item));
    Check(menuTargets.Resolve() == ShellMenuTarget.Background,
          "context message location replaces a stale right-button location");
    menuTargets.Reset();
    menuTargets.ContextMenu((nint)((91 << 16) | 55), _ =>
        Task.FromException<ShellMenuTarget>(new InvalidOperationException("probe unavailable")));
    Check(menuTargets.Resolve() == ShellMenuTarget.Unknown,
          "failed hit test cannot borrow a selected item");
    menuTargets.Reset();
    Check(menuTargets.Resolve() == ShellMenuTarget.Unknown &&
          ShellMenuTargetTracker.DecodeContextPoint(unchecked((nint)0xFFF8000C)) == new ShellMenuPoint(12, -8),
          "closed menus forget hit targets and signed screen coordinates decode correctly");
    Check(KeyboardShortcuts.Resolve(0x08, 0) == ExplorerShortcut.Parent, "Backspace goes to parent");
    Check(KeyboardShortcuts.Resolve(0x26, 1) == ExplorerShortcut.Parent, "Alt+Up goes to parent");
    Check(KeyboardShortcuts.Resolve(0x25, 1) == ExplorerShortcut.Back, "Alt+Left goes back");
    Check(KeyboardShortcuts.Resolve(0x27, 1) == ExplorerShortcut.Forward, "Alt+Right goes forward");
    Check(KeyboardShortcuts.Resolve(0x73, 0) == ExplorerShortcut.Address,
          "F4 selects the editable address field as in current File Explorer");
    Check(KeyboardShortcuts.Resolve(0x09, 0) == ExplorerShortcut.NextFocusStop &&
          KeyboardShortcuts.Resolve(0x09, 4) == ExplorerShortcut.PreviousFocusStop,
          "Tab and Shift+Tab traverse pane focus stops");
    Check(KeyboardShortcuts.Resolve(0x75, 0) == ExplorerShortcut.NextFocusRegion &&
          KeyboardShortcuts.Resolve(0x75, 4) == ExplorerShortcut.PreviousFocusRegion,
          "F6 and Shift+F6 traverse major regions in opposite directions");
    Check(KeyboardShortcuts.NextPane(3, 4, false) == 0 &&
          KeyboardShortcuts.NextPane(0, 4, true) == 3 &&
          KeyboardShortcuts.NextPane(0, 1, false) == 0,
          "visible pane focus wraps");
    var focusStops = new[] { 11, 10, 11, 10 };
    Check(FocusTraversal.Move(0, 9, focusStops, false) == (0, 10) &&
          FocusTraversal.Move(0, 10, focusStops, false) == (1, 0) &&
          FocusTraversal.Move(1, 0, focusStops, true) == (0, 10) &&
          FocusTraversal.Move(0, 0, focusStops, true) == (3, 9) &&
          FocusTraversal.Move(2, -1, focusStops, true) == (2, 10),
          "forward and reverse tab order includes Details header and wraps panes");
    IReadOnlyList<int>[] majorStops = [[4, 8, 9, 10], [4, 8, 9], [4, 8, 9, 10], [4, 8, 9]];
    Check(FocusTraversal.MoveMajor(0, 4, majorStops, false) == (0, 8) &&
          FocusTraversal.MoveMajor(0, 8, majorStops, false) == (0, 9) &&
          FocusTraversal.MoveMajor(0, 9, majorStops, false) == (0, 10) &&
          FocusTraversal.MoveMajor(0, 10, majorStops, false) == (1, 4) &&
          FocusTraversal.MoveMajor(1, 4, majorStops, true) == (0, 10) &&
          FocusTraversal.MoveMajor(0, 4, majorStops, true) == (3, 9) &&
          FocusTraversal.MoveMajor(2, 6, majorStops, false) == (2, 8) &&
          FocusTraversal.MoveMajor(2, -1, majorStops, true) == (2, 10),
          "F6 skips fine controls, includes Details header, and wraps visible panes");
    Check(KeyboardShortcuts.Resolve(0x08, 2) == ExplorerShortcut.None,
          "Ctrl+Backspace remains available to text controls");
    Check(KeyboardShortcuts.Resolve(0x4e, 6) == ExplorerShortcut.NewFolder,
          "Ctrl+Shift+N routes to the Shell background New Folder command");
    Check(Enumerable.Range(1, 9).All(number =>
              KeyboardShortcuts.Resolve(0x30 + number, 2) == ExplorerShortcut.Tab1 + (number - 1)) &&
          KeyboardShortcuts.Resolve(0x30, 2) == ExplorerShortcut.None &&
          KeyboardShortcuts.Resolve(0x31, 6) == ExplorerShortcut.None,
          "Ctrl+1-9 selects tab positions without taking Shell view-style shortcuts");
    Check(KeyboardShortcuts.Resolve(0x72, 0) == ExplorerShortcut.Search &&
          KeyboardShortcuts.Resolve(0x45, 2) == ExplorerShortcut.Search &&
          KeyboardShortcuts.Resolve(0x46, 2) == ExplorerShortcut.Search,
          "F3 and Ctrl+E/F focus current-directory search");
    var searchFocusStops = new[] { 14, 13, 11, 10 };
    Check(FocusTraversal.Move(0, 7, searchFocusStops, false) == (0, 8) &&
          FocusTraversal.Move(0, 8, searchFocusStops, false) == (0, 9) &&
          FocusTraversal.Move(0, 9, searchFocusStops, false) == (0, 10) &&
          FocusTraversal.Move(0, 10, searchFocusStops, false) == (0, 11) &&
          FocusTraversal.Move(0, 11, searchFocusStops, true) == (0, 10) &&
          FocusTraversal.Move(0, 10, searchFocusStops, true) == (0, 9) &&
          FocusTraversal.Move(0, 13, searchFocusStops, false) == (1, 0) &&
          FocusTraversal.Move(1, 0, searchFocusStops, true) == (0, 13),
          "visible search controls extend each pane's reversible Tab order");
    var searchScope = Path.Combine(root, "search scope & 中文");
    Directory.CreateDirectory(searchScope);
    var searchUri = ShellSearchLocation.BuildUri(searchScope, "name:报告 & #1");
    Check(searchUri.StartsWith("search-ms:query=", StringComparison.Ordinal) &&
          searchUri.Contains("%26", StringComparison.Ordinal) &&
          searchUri.Contains("%23", StringComparison.Ordinal) &&
          searchUri.Contains("crumb=location:", StringComparison.Ordinal) &&
          ShellSearchLocation.TryReadScopeAndQuery(searchUri, out var parsedScope, out var parsedQuery) &&
          parsedScope == searchScope && parsedQuery == "name:报告 & #1",
          "search query and scope round-trip without confusing URI separators");
    var searchPidl = ShellSearchLocation.ParsePidl(searchUri);
    try
    {
        Check(searchPidl != IntPtr.Zero && ShellSearchLocation.IsSearchPidl(searchPidl) &&
              ShellSearchLocation.TryGetParsingName(searchPidl, out var shellSearchName) &&
              ShellSearchLocation.TryReadScopeAndQuery(shellSearchName, out var shellScope, out var shellQuery) &&
              shellScope == searchScope && shellQuery == "name:报告 & #1",
              "Windows search PIDL preserves the scoped query for travel-log restoration");
    }
    finally { Marshal.FreeCoTaskMem(searchPidl); }
    var searchNavigation = new ShellNavigationState(searchScope);
    Check(searchNavigation.TryCompleteSearch(searchUri) &&
          searchNavigation.DirectoryPath == searchScope &&
          searchNavigation.SearchQuery == "name:报告 & #1" &&
          searchNavigation.IsSearchActive,
          "search results keep the scope as the tab's file-system directory");
    searchNavigation.CompleteDirectory(Path.Combine(searchScope, "child"));
    Check(!searchNavigation.IsSearchActive && searchNavigation.SearchQuery is null &&
          searchNavigation.DirectoryPath == Path.Combine(searchScope, "child") &&
          searchNavigation.TryCompleteSearch(searchUri) &&
          searchNavigation.DirectoryPath == searchScope && searchNavigation.SearchQuery == "name:报告 & #1" &&
          !searchNavigation.TryCompleteSearch("shell:unknown") && searchNavigation.IsSearchActive,
          "folder navigation clears search while travel-log search restores its query");
    var browserSite = new ShellBrowserSite();
    var serviceId = ShellBrowserSite.BrowserFrameServiceId;
    var browser2Id = ShellBrowserSite.DialogBrowser2Id;
    var serviceResult = browserSite.QueryService(ref serviceId, ref browser2Id, out var browser2Pointer);
    var flagResult = browserSite.GetViewFlags(out var searchFlags);
    try
    {
        var nativeFlags = 0u;
        var nativeFlagResult = -1;
        if (browser2Pointer != IntPtr.Zero)
        {
            var vtable = Marshal.ReadIntPtr(browser2Pointer);
            var getFlagsPointer = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
            nativeFlagResult = Marshal.GetDelegateForFunctionPointer<NativeGetViewFlags>(getFlagsPointer)(
                browser2Pointer, out nativeFlags);
        }
        Check(serviceResult == 0 && browser2Pointer != IntPtr.Zero &&
              flagResult == 0 &&
              searchFlags == ShellBrowserSite.NoIncludeItemFlag &&
              nativeFlagResult == 0 && nativeFlags == ShellBrowserSite.NoIncludeItemFlag,
              $"browser site exposes background search view flags through the Shell service " +
              $"(service=0x{serviceResult:X8}, pointer={browser2Pointer != IntPtr.Zero}, flags=0x{searchFlags:X8})");
    }
    finally { if (browser2Pointer != IntPtr.Zero) Marshal.Release(browser2Pointer); }
    var browserId = typeof(IComDialogBrowser).GUID;
    var baseServiceResult = browserSite.QueryService(ref serviceId, ref browserId, out var browserPointer);
    try { Check(baseServiceResult == 0 && browserPointer != IntPtr.Zero,
                "browser site also exposes the base dialog-browser interface"); }
    finally { if (browserPointer != IntPtr.Zero) Marshal.Release(browserPointer); }
    Exception? siteFailure = null;
    var siteThread = new Thread(() =>
    {
        object? shellBrowser = null;
        try
        {
            shellBrowser = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB"), true)!);
            ShellBrowserSite.SetSite(shellBrowser!, new ShellBrowserSite());
            var providerId = typeof(IShellServiceProvider).GUID;
            var getSiteResult = ((ITestObjectWithSite)shellBrowser!).GetSite(ref providerId, out var provider);
            try { Check(getSiteResult == 0 && provider != IntPtr.Zero,
                        "headless ExplorerBrowser retains the background-search service site"); }
            finally { if (provider != IntPtr.Zero) Marshal.Release(provider); }
        }
        catch (Exception ex) { siteFailure = ex; }
        finally
        {
            if (shellBrowser is not null)
            {
                ShellBrowserSite.SetSite(shellBrowser, null);
                Marshal.ReleaseComObject(shellBrowser);
            }
        }
    });
    siteThread.SetApartmentState(ApartmentState.STA);
    siteThread.Start();
    siteThread.Join();
    if (siteFailure is not null) throw siteFailure;
    var observedDirectory = Path.Combine(root, "new-folder-observation");
    Directory.CreateDirectory(observedDirectory);
    using (var observation = FolderCreationObservation.TryStart(observedDirectory)
           ?? throw new Exception("local directory observation could not start"))
    {
        var createdOne = Path.Combine(observedDirectory, "one-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(createdOne);
        Check(SpinWait.SpinUntil(() => observation.Snapshot().Directories.Contains(createdOne),
                  TimeSpan.FromSeconds(3)),
              "new directory is observed without enumerating the parent");
        var createdFile = Path.Combine(observedDirectory, "file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(createdFile, "sample");
        Check(!SpinWait.SpinUntil(() => observation.Snapshot().Directories.Count > 1,
                  TimeSpan.FromMilliseconds(400)),
              "a new file is not mistaken for the folder to rename");
        File.Delete(createdFile);
        var createdTwo = Path.Combine(observedDirectory, "two-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(createdTwo);
        Check(SpinWait.SpinUntil(() => observation.Snapshot().Directories.Count >= 2,
                  TimeSpan.FromSeconds(3)),
              "concurrent creations remain ambiguous rather than choosing the wrong folder");
        Directory.Delete(createdOne);
        Directory.Delete(createdTwo);
    }
    Check(FolderCreationObservation.TryStart(@"\\offline\share\folder") is null,
          "UNC path skips the extra directory watcher");
    store.Save(state);
    var loaded = store.Load() ?? throw new Exception("INI did not load");
    Check(store.LoadWarning is null, "complete INI needs no recovery");
    Check(loaded.Layout == 2 && loaded.DefaultViewMode == 3, "global settings");
    Check(loaded.Panes[0].Count == 2, "two tabs with same path");
    Check(loaded.Panes[0][0].Path == first.Path, "Unicode path with equals sign");
    Check(loaded.Panes[0][0].View.SortColumns[0] == first.View.SortColumns[0], "sort key and direction");
    Check(loaded.Panes[0][0].View.GroupKey == groupKey && !loaded.Panes[0][0].View.GroupAscending, "group key and direction");
    Check(loaded.Panes[0][1].View.Mode == 6 && loaded.Panes[0][1].View.GroupKey is null, "independent tab state");
    var activeSelection = state with { ActivePane = 1, ActiveTabIndices = [1, 0, 0, 0] };
    var selectedProfile = SessionStore.Render(activeSelection);
    var selectedSession = SessionStore.Parse(selectedProfile) ?? throw new Exception("active tabs did not parse");
    Check(selectedSession.ActivePane == 1 && selectedSession.ActiveTabIndices?.SequenceEqual([1, 0, 0, 0]) == true,
        "active pane and displayed tab positions round-trip in the portable session");
    var oldSelectionProfile = selectedProfile.Replace("ActivePane=1" + Environment.NewLine, string.Empty,
        StringComparison.Ordinal).Replace("ActiveTab=1" + Environment.NewLine, string.Empty,
        StringComparison.Ordinal).Replace("ActiveTab=0" + Environment.NewLine, string.Empty,
        StringComparison.Ordinal);
    var oldSelectionIni = Path.Combine(root, "old-selection.ini");
    File.WriteAllText(oldSelectionIni, oldSelectionProfile);
    var oldSelectionStore = new SessionStore(oldSelectionIni, Path.Combine(root, "missing-legacy.json"));
    var oldSelection = oldSelectionStore.Load() ?? throw new Exception("older session format did not load");
    Check(oldSelectionStore.LoadWarning is null && oldSelection.ActivePane == 0 &&
          oldSelection.ActiveTabIndices?.All(index => index == 0) == true,
        "older session without selection fields remains complete and selects first tabs");
    var badSelectionIni = Path.Combine(root, "bad-selection.ini");
    File.WriteAllText(badSelectionIni,
        selectedProfile.Replace("ActiveTab=1", "ActiveTab=99", StringComparison.Ordinal));
    var badSelectionStore = new SessionStore(badSelectionIni, Path.Combine(root, "missing-legacy.json"));
    var selectionRecovered = badSelectionStore.Load() ?? throw new Exception("damaged active tab did not recover");
    Check(selectionRecovered.Panes[0].Count == 2 && selectionRecovered.ActiveTabIndices?[0] == 0 &&
          badSelectionStore.LoadWarning is not null &&
          Directory.GetFiles(root, "bad-selection.ini.invalid-*.bak").Length == 1,
        "invalid active tab falls back without losing tabs and backs up the source INI");
    var lastWrite = File.GetLastWriteTimeUtc(ini);
    store.Save(state);
    Check(File.GetLastWriteTimeUtc(ini) == lastWrite, "unchanged settings avoid write");

    var conflictIni = Path.Combine(root, "conflict.ini");
    var conflictStore = new SessionStore(conflictIni, Path.Combine(root, "missing-legacy.json"));
    conflictStore.Save(state);
    conflictStore.Load();
    var externalEdit = File.ReadAllText(conflictIni) + "; changed by another instance" + Environment.NewLine;
    File.WriteAllText(conflictIni, externalEdit);
    try
    {
        conflictStore.Save(state with { Layout = 3 });
        throw new Exception("external session edit was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(conflictIni) == externalEdit,
        "external session edits remain untouched after a stale save");

    var raceIni = Path.Combine(root, "race.ini");
    using var raceStart = new Barrier(2);
    Task<bool> RaceSave(int layout) => Task.Run(() =>
    {
        var instance = new SessionStore(raceIni, Path.Combine(root, "missing-legacy.json"));
        Check(instance.Load() is null && raceStart.SignalAndWait(TimeSpan.FromSeconds(5)),
            "both instances load the same absent session before writing");
        try { instance.Save(state with { Layout = layout }); return true; }
        catch (IOException) { return false; }
    });
    var firstRace = RaceSave(1);
    var secondRace = RaceSave(2);
    Task.WaitAll(firstRace, secondRace);
    var raceState = SessionStore.Parse(File.ReadAllText(raceIni));
    Check(firstRace.Result != secondRace.Result && raceState is not null &&
          raceState.Layout == (firstRace.Result ? 1 : 2),
          "simultaneous instances produce one intact session without stale overwrite");

    var recoveryIni = Path.Combine(root, "recovery.ini");
    var recoveryStore = new SessionStore(recoveryIni, Path.Combine(root, "missing-legacy.json"));
    var completeProfile = SessionStore.Render(state);
    var truncatedProfile = completeProfile[..completeProfile.IndexOf("[Pane0.Tab1]", StringComparison.Ordinal)];
    File.WriteAllText(recoveryIni, truncatedProfile);
    var recovered = recoveryStore.Load() ?? throw new Exception("truncated INI did not recover");
    Check(recovered.Panes[0].Count == 1 && recovered.Panes[0][0].Path == first.Path,
        "truncated INI preserves readable tab");
    Check(recoveryStore.LoadWarning is not null &&
          Directory.GetFiles(root, "recovery.ini.invalid-*.bak").Length == 1,
        "truncated INI is reported and backed up");
    recoveryStore.Save(recovered);
    Check(SessionStore.Parse(File.ReadAllText(recoveryIni))?.Panes[0].Count == 1,
        "recovered INI can be saved cleanly");

    File.WriteAllText(recoveryIni, completeProfile.Replace("TabCount=2", "TabCount=0", StringComparison.Ordinal));
    var countRecovered = recoveryStore.Load() ?? throw new Exception("wrong tab count did not recover");
    Check(countRecovered.Panes[0].Count == 2, "orphaned tab sections survive damaged tab count");
    Check(Directory.GetFiles(root, "recovery.ini.invalid-*.bak").Length == 2,
        "damaged tab count is backed up");

    var headerlessProfile = completeProfile[completeProfile.IndexOf("[Pane0]", StringComparison.Ordinal)..];
    File.WriteAllText(recoveryIni, headerlessProfile);
    var headerlessRecovered = recoveryStore.Load() ?? throw new Exception("missing General section did not recover");
    Check(headerlessRecovered.Layout == 4 && headerlessRecovered.Panes[0].Count == 2,
        "missing General section falls back to layout default and preserves tabs");
    Check(Directory.GetFiles(root, "recovery.ini.invalid-*.bak").Length == 3,
        "missing General section is backed up");

    var sortRecoveryIni = Path.Combine(root, "sort-recovery.ini");
    var staleSortCountProfile = completeProfile.Replace("SortCount=1", "SortCount=0", StringComparison.Ordinal);
    File.WriteAllText(sortRecoveryIni, staleSortCountProfile);
    var sortRecoveryStore = new SessionStore(sortRecoveryIni, Path.Combine(root, "missing-legacy.json"));
    var sortRecovered = sortRecoveryStore.Load() ?? throw new Exception("stale sort count did not recover");
    Check(sortRecovered.Panes[0][0].View.SortColumns.SequenceEqual(first.View.SortColumns),
        "orphaned sort column survives damaged sort count");
    Check(sortRecoveryStore.LoadWarning is not null &&
          Directory.GetFiles(root, "sort-recovery.ini.invalid-*.bak").Length == 1,
        "damaged sort count is reported and backed up");
    sortRecoveryStore.Save(sortRecovered);
    Check(SessionStore.Parse(File.ReadAllText(sortRecoveryIni))?.Panes[0][0].View.SortColumns
        .SequenceEqual(first.View.SortColumns) == true, "recovered sort remains after saving");

    File.Delete(ini);
    File.WriteAllText(legacy, JsonSerializer.Serialize(new { Layout = 4, Panes = new[]
    {
        new[] { @"C:\old" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()
    } }));
    var migrated = store.Load() ?? throw new Exception("legacy session did not migrate");
    Check(migrated.Panes[0][0].Path == @"C:\old" && migrated.Panes[0][0].View.Mode == 4, "legacy paths and default view");
    Check(File.Exists(ini) && File.Exists(legacy), "migration preserves legacy file");

    File.WriteAllText(ini, "[invalid]\nVersion=broken\n");
    Check(store.Load() is null, "invalid INI falls back");
    store.Save(state);
    var backups = Directory.GetFiles(root, "ExplorerDirs.ini.invalid-*.bak");
    Check(backups.Length == 1 && File.ReadAllText(backups[0]).Contains("Version=broken"),
        "invalid INI is preserved before overwrite");

    var futureIni = Path.Combine(root, "future.ini");
    var futureProfile = completeProfile.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal);
    File.WriteAllText(futureIni, futureProfile);
    var futureStore = new SessionStore(futureIni, Path.Combine(root, "missing-legacy.json"));
    Check(futureStore.Load() is null && futureStore.LoadWarning is not null,
        "newer format is not interpreted as this version");
    try
    {
        futureStore.Save(state);
        throw new Exception("newer format was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(futureIni) == futureProfile, "newer format stays untouched");

    var duplicateFutureIni = Path.Combine(root, "duplicate-future.ini");
    var duplicateFutureProfile = completeProfile.Replace("FormatVersion=1",
        "FormatVersion=2" + Environment.NewLine + "FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(duplicateFutureIni, duplicateFutureProfile);
    var duplicateFutureStore = new SessionStore(duplicateFutureIni, Path.Combine(root, "missing-legacy.json"));
    Check(duplicateFutureStore.Load() is null && duplicateFutureStore.LoadWarning is not null,
        "a newer duplicate format version must not be interpreted as version one");
    try
    {
        duplicateFutureStore.Save(state);
        throw new Exception("duplicated newer format was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(duplicateFutureIni) == duplicateFutureProfile,
        "duplicated newer format stays untouched");

    var crashLogs = new CrashLog(Path.Combine(root, "crash-logs"));
    Exception simulatedCrash;
    try { throw new InvalidOperationException(@"PRIVATE_PATH_C:\Users\marvin\secret.txt"); }
    catch (Exception ex) { simulatedCrash = ex; }
    var crashFile = crashLogs.TryWrite(CrashEventKind.DispatcherUnhandled, simulatedCrash);
    Check(crashFile is not null && File.Exists(crashFile), "managed crash report is written");
    var report = File.ReadAllText(crashFile!);
    Check(report.Contains("InvalidOperationException", StringComparison.Ordinal) &&
          report.Contains("DispatcherUnhandled", StringComparison.Ordinal),
          "crash report retains diagnostic event and exception type");
    Check(!report.Contains("PRIVATE_PATH", StringComparison.Ordinal) &&
          !report.Contains(@"C:\Users\marvin", StringComparison.Ordinal),
          "crash report omits exception messages and private paths");
    Check(crashLogs.TryWrite(CrashEventKind.AppDomainUnhandled, simulatedCrash) is null &&
          Directory.GetFiles(Path.Combine(root, "crash-logs"), "*.json").Length == 1,
          "the same managed exception is logged once");

    var runMarkerDirectory = Path.Combine(root, "run-marker-smoke");
    Directory.CreateDirectory(runMarkerDirectory);
    foreach (var old in Directory.GetFiles(runMarkerDirectory, "*.json")) File.Delete(old);
    File.WriteAllText(Path.Combine(runMarkerDirectory, "run-dead.json"),
        "{\"ProcessId\":2147483647,\"StartedUtcTicks\":1,\"AppVersion\":\"test\"}");
    using (var firstRun = RunMarkerTracker.TryStart(runMarkerDirectory)
           ?? throw new Exception("run marker could not start"))
    {
        Check(firstRun.PriorUncleanExit && File.Exists(Path.Combine(runMarkerDirectory, "abrupt-dead.json")),
              "terminated process marker becomes an abrupt-exit report");
        using var secondRun = RunMarkerTracker.TryStart(runMarkerDirectory)
            ?? throw new Exception("second run marker could not start");
        Check(!secondRun.PriorUncleanExit &&
              Directory.GetFiles(runMarkerDirectory, "run-*.json").Length == 2,
              "another live instance is not mistaken for a crash");
    }
    Check(Directory.GetFiles(runMarkerDirectory, "run-*.json").Length == 0 &&
          Directory.GetFiles(runMarkerDirectory, "abrupt-*.json").Length == 1,
          "clean exits remove only their own live markers");
    var abruptReport = File.ReadAllText(Path.Combine(runMarkerDirectory, "abrupt-dead.json"));
    Check(!abruptReport.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase),
          "abrupt-exit report contains no browsing path");
    var corruptMarker = Path.Combine(runMarkerDirectory, "run-corrupt.json");
    File.WriteAllText(corruptMarker, "{incomplete");
    using (var recoveredRun = RunMarkerTracker.TryStart(runMarkerDirectory)
           ?? throw new Exception("run marker recovery could not start"))
    {
        Check(!recoveredRun.PriorUncleanExit && File.ReadAllText(corruptMarker) == "{incomplete",
              "unreadable run evidence remains untouched without false crash attribution");
    }
    var collisionDirectory = Path.Combine(root, "run-marker-collision");
    Directory.CreateDirectory(collisionDirectory);
    var previousAbrupt = Path.Combine(collisionDirectory, "abrupt-dead.json");
    File.WriteAllText(previousAbrupt, "older report");
    File.WriteAllText(Path.Combine(collisionDirectory, "run-dead.json"),
        "{\"ProcessId\":2147483647,\"StartedUtcTicks\":1,\"AppVersion\":\"test\"}");
    using (var recoveredCollision = RunMarkerTracker.TryStart(collisionDirectory)
           ?? throw new Exception("run marker collision recovery could not start"))
    {
        Check(recoveredCollision.PriorUncleanExit && File.ReadAllText(previousAbrupt) == "older report" &&
              !File.Exists(Path.Combine(collisionDirectory, "run-dead.json")) &&
              Directory.GetFiles(collisionDirectory, "abrupt-dead-*.json").Length == 1,
              "an existing abrupt report is preserved while the new ended run is archived");
    }
    using (var nextRun = RunMarkerTracker.TryStart(collisionDirectory)
           ?? throw new Exception("next run marker could not start"))
    {
        Check(!nextRun.PriorUncleanExit,
              "an archived marker does not cause another unclean-exit warning");
    }

    var machineVariables = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
    {
        ["TOOLS"] = new(@"C:\new-tools", false),
        ["Path"] = new(@"C:\Windows\System32;%TOOLS%", true),
        ["LINK"] = new(@"%TOOLS%\bin", true),
        ["PLAIN"] = new(@"%TOOLS%\literal", false),
        ["MISSING_LINK"] = new(@"%DELETED%\bin", true)
    };
    var userVariables = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
    {
        ["Path"] = new(@"%PATH%;C:\user-bin", true),
        ["USER_TOOL"] = new(@"%TOOLS%\user", true)
    };
    var oldProcess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TOOLS"] = @"C:\old-tools",
        ["DELETED"] = @"C:\stale",
        ["USERPROFILE"] = @"C:\Users\example"
    };
    var refreshPlan = EnvironmentRefresh.BuildPlan(machineVariables, userVariables, oldProcess,
        ["TOOLS", "Path", "DELETED"]);
    Check(refreshPlan.Values["LINK"] == @"C:\new-tools\bin" &&
          refreshPlan.Values["USER_TOOL"] == @"C:\new-tools\user",
          "expandable values use the fresh persisted snapshot");
    Check(refreshPlan.Values["Path"] == @"C:\Windows\System32;C:\new-tools;C:\user-bin",
          "machine and user PATH combine without stale or duplicate machine segments");
    Check(refreshPlan.Values["PLAIN"] == @"%TOOLS%\literal" &&
          refreshPlan.Values["MISSING_LINK"] == @"%DELETED%\bin",
          "plain registry strings stay literal and deleted names do not use stale process values");
    Check(refreshPlan.Removed.Contains("DELETED", StringComparer.OrdinalIgnoreCase),
          "deleted persisted variable is removed from the process");
    var normalUserVariables = new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
    {
        ["Path"] = new(@"C:\user-bin", false),
        ["FROM_PROCESS"] = new(@"%USERPROFILE%\Documents", true)
    };
    var normalPathPlan = EnvironmentRefresh.BuildPlan(machineVariables, normalUserVariables,
        oldProcess, ["TOOLS", "Path", "DELETED"]);
    Check(normalPathPlan.Values["Path"] == @"C:\Windows\System32;C:\new-tools;C:\user-bin" &&
          normalPathPlan.Values["FROM_PROCESS"] == @"C:\Users\example\Documents",
          "ordinary user PATH appends and process-only references remain available");

    var fakeEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["STALE"] = "old-delete", ["UPDATED"] = "old-value", ["FAIL"] = null
    };
    var failingPlan = new EnvironmentRefreshPlan(
        new Dictionary<string, string> { ["UPDATED"] = "new-value", ["FAIL"] = "rejected" },
        ["STALE"]);
    try
    {
        EnvironmentRefresh.ApplyPlan(failingPlan,
            name => fakeEnvironment.GetValueOrDefault(name),
            (name, value) =>
            {
                if (name == "FAIL") throw new IOException("simulated process environment write failure");
                fakeEnvironment[name] = value;
            });
        throw new Exception("partial environment refresh was reported as successful");
    }
    catch (IOException) { }
    Check(fakeEnvironment["STALE"] == "old-delete" && fakeEnvironment["UPDATED"] == "old-value" &&
          fakeEnvironment["FAIL"] is null,
          "failed environment update restores earlier deletions and assignments");
    var rejectedOversize = new EnvironmentRefreshPlan(
        new Dictionary<string, string> { ["UPDATED"] = "new-value", ["FAIL"] = new string('x', 32767) },
        ["STALE"]);
    try
    {
        EnvironmentRefresh.ApplyPlan(rejectedOversize,
            name => fakeEnvironment.GetValueOrDefault(name),
            (name, value) => fakeEnvironment[name] = value);
        throw new Exception("oversized environment value was accepted");
    }
    catch (ArgumentException) { }
    Check(fakeEnvironment["STALE"] == "old-delete" && fakeEnvironment["UPDATED"] == "old-value",
          "invalid environment plan is rejected before changing any value");

    // This affects only the short-lived console test process; no persistent registry values are changed.
    Check(new EnvironmentRefresh().Reload() > 0, "live Windows environment snapshot can be refreshed");

    ProcessStartInfo? commandInfo = null;
    using var commandStarted = new ManualResetEventSlim();
    using var releaseCommand = new ManualResetEventSlim();
    var commandLaunch = TerminalLauncher.OpenAsync(root, CancellationToken.None, info =>
    {
        commandInfo = info;
        commandStarted.Set();
        releaseCommand.Wait();
    });
    try
    {
        Check(commandStarted.Wait(TimeSpan.FromSeconds(3)) && !commandLaunch.IsCompleted,
              "slow command launch does not hold the calling thread");
    }
    finally { releaseCommand.Set(); }
    commandLaunch.GetAwaiter().GetResult();
    Check(commandInfo?.FileName == Path.Combine(Environment.SystemDirectory, "cmd.exe") &&
          Path.IsPathFullyQualified(commandInfo.FileName) && File.Exists(commandInfo.FileName) &&
          commandInfo.WorkingDirectory == root && !commandInfo.UseShellExecute,
          "terminal launch uses the system command interpreter and requested directory");
    const string childVariable = "EXPLORER_DIRS_CHILD_SMOKE";
    const string removedChildVariable = "EXPLORER_DIRS_REMOVED_SMOKE";
    var oldChildValue = Environment.GetEnvironmentVariable(childVariable, EnvironmentVariableTarget.Process);
    var oldRemovedValue = Environment.GetEnvironmentVariable(removedChildVariable,
        EnvironmentVariableTarget.Process);
    var expectedChildValue = "updated-" + Guid.NewGuid().ToString("N");
    try
    {
        Environment.SetEnvironmentVariable(removedChildVariable, "stale",
            EnvironmentVariableTarget.Process);
        var childPlan = EnvironmentRefresh.BuildPlan(
            new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase)
            {
                [childVariable] = new(expectedChildValue, false)
            },
            new Dictionary<string, PersistedVariable>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [removedChildVariable]);
        EnvironmentRefresh.ApplyPlan(childPlan);
        Check(Environment.GetEnvironmentVariable(removedChildVariable,
                  EnvironmentVariableTarget.Process) is null,
              "applying the environment plan removes a deleted persistent variable");
        string? childOutput = null;
        TerminalLauncher.OpenAsync(root, CancellationToken.None, info =>
        {
            info.Arguments = "/d /c echo %EXPLORER_DIRS_CHILD_SMOKE%";
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            using var child = Process.Start(info) ?? throw new Exception("hidden command did not start");
            if (!child.WaitForExit(5000))
            {
                child.Kill();
                throw new Exception("hidden command did not exit in time");
            }
            if (child.ExitCode != 0) throw new Exception("hidden command failed");
            childOutput = child.StandardOutput.ReadToEnd().Trim();
        }).GetAwaiter().GetResult();
        Check(childOutput == expectedChildValue,
              "a new command process inherits a freshly applied environment plan");
    }
    finally
    {
        Environment.SetEnvironmentVariable(childVariable, oldChildValue,
            EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(removedChildVariable, oldRemovedValue,
            EnvironmentVariableTarget.Process);
    }
    using var canceledCommand = new CancellationTokenSource();
    canceledCommand.Cancel();
    var launchAfterCancel = false;
    try
    {
        TerminalLauncher.OpenAsync(root, canceledCommand.Token,
            _ => launchAfterCancel = true).GetAwaiter().GetResult();
        throw new Exception("canceled terminal request unexpectedly started");
    }
    catch (OperationCanceledException) { }
    Check(!launchAfterCancel, "a canceled window cannot start a delayed terminal");

    Check(DirectoryProbe.Check(@"C:\allowed", _ => FileAttributes.Directory, _ => false) ==
          DirectoryProbeStatus.Directory &&
          DirectoryProbe.Check(@"C:\ordinary-file", _ => FileAttributes.Archive, _ => false) ==
          DirectoryProbeStatus.Unavailable,
          "directory preflight distinguishes a directory from an ordinary file");
    var probeFile = Path.Combine(root, "navigation-probe.txt");
    File.WriteAllText(probeFile, "probe");
    Check(DirectoryProbe.Check(root) == DirectoryProbeStatus.Directory &&
          DirectoryProbe.Check(probeFile) == DirectoryProbeStatus.Unavailable &&
          DirectoryProbe.Check(Path.Combine(root, "missing-directory")) == DirectoryProbeStatus.Unavailable,
          "real filesystem directory, file, and missing path classification");
    Check(DirectoryProbe.Check(@"C:\protected", _ => throw new UnauthorizedAccessException(), _ => false) ==
          DirectoryProbeStatus.AccessDenied &&
          DirectoryProbe.Check(@"C:\protected", _ => throw new System.Security.SecurityException(), _ => false) ==
          DirectoryProbeStatus.AccessDenied,
          "access denial is deferred to the Shell rather than misreported as missing");
    Check(DirectoryProbe.Check(@"\\server\share\", _ => throw new IOException(), _ => true) ==
          DirectoryProbeStatus.Directory &&
          DirectoryProbe.Check(@"\\server\offline\", _ => throw new IOException(), _ => false) ==
          DirectoryProbeStatus.Unavailable &&
          DirectoryProbe.Check(@"C:\missing", _ => throw new DirectoryNotFoundException(),
              _ => throw new Exception("local paths must not get a second filesystem probe")) ==
          DirectoryProbeStatus.Unavailable,
          "UNC share roots keep the existing Directory.Exists fallback");
    var navigationGate = new DirectoryNavigationGate();
    using var oldProbeStarted = new ManualResetEventSlim();
    using var releaseOldProbe = new ManualResetEventSlim();
    var oldProbe = navigationGate.ProbeAsync(@"\\offline\share", _ =>
    {
        oldProbeStarted.Set();
        releaseOldProbe.Wait();
        return DirectoryProbeStatus.Unavailable;
    });
    try
    {
        Check(oldProbeStarted.Wait(TimeSpan.FromSeconds(3)), "first directory probe starts in background");
        var newProbe = navigationGate.ProbeAsync(@"C:\ready", _ => DirectoryProbeStatus.Directory).GetAwaiter().GetResult();
        Check(navigationGate.IsCurrent(newProbe) && newProbe.CanNavigate,
              "new navigation can finish while older probe waits");
        var deniedProbe = navigationGate.ProbeAsync(@"C:\protected", _ => DirectoryProbeStatus.AccessDenied)
            .GetAwaiter().GetResult();
        Check(navigationGate.IsCurrent(deniedProbe) && deniedProbe.CanNavigate,
              "access denied reaches Shell navigation for its own error handling");
    }
    finally { releaseOldProbe.Set(); }
    Check(!navigationGate.IsCurrent(oldProbe.GetAwaiter().GetResult()),
        "late result from an older directory probe cannot replace the newer navigation");

    Check(TabReorder.Move(["A", "B", "C", "D"], 0, 2, false).SequenceEqual(["B", "A", "C", "D"]),
        "dragging a first tab before a later tab uses the correct insertion index");
    Check(TabReorder.Move(["A", "B", "C", "D"], 0, 2, true).SequenceEqual(["B", "C", "A", "D"]),
        "dragging a first tab after a later tab uses the correct insertion index");
    Check(TabReorder.Move(["A", "B", "C", "D"], 3, 1, false).SequenceEqual(["A", "D", "B", "C"]),
        "dragging a last tab to the left preserves other tab order");
    Check(TabReorder.Move(["A", "B", "C", "D"], 2, 2, true).SequenceEqual(["A", "B", "C", "D"]),
        "dropping on the same tab leaves order unchanged");
    Exception? layoutFailure = null;
    var layoutThread = new Thread(() =>
    {
        try
        {
            var panel = new TabHeaderPanel();
            var firstHeader = new Border { Width = 20, Height = 10 };
            var secondHeader = new Border { Width = 30, Height = 10 };
            panel.Children.Add(firstHeader);
            panel.Children.Add(secondHeader);
            TabHeaderPanel.SetOrder(firstHeader, 1);
            TabHeaderPanel.SetOrder(secondHeader, 0);
            panel.Measure(new Size(100, 10));
            panel.Arrange(new Rect(0, 0, 100, 10));
            Check(VisualTreeHelper.GetOffset(secondHeader).X == 0 &&
                  VisualTreeHelper.GetOffset(firstHeader).X == 30,
                  "tab headers arrange by display order");
            TabHeaderPanel.SetOrder(firstHeader, 0);
            TabHeaderPanel.SetOrder(secondHeader, 1);
            panel.Arrange(new Rect(0, 0, 100, 10));
            Check(VisualTreeHelper.GetOffset(firstHeader).X == 0 &&
                  VisualTreeHelper.GetOffset(secondHeader).X == 20 &&
                  ReferenceEquals(panel.Children[0], firstHeader),
                  "reordering headers leaves child controls attached");
            var contentHost = new PersistentTabHost();
            var firstContent = new Border();
            var secondContent = new Border();
            contentHost.Show(firstContent);
            contentHost.Show(secondContent);
            Check(contentHost.Children.Count == 2 &&
                  ReferenceEquals(VisualTreeHelper.GetParent(firstContent), contentHost) &&
                  firstContent.Visibility == Visibility.Collapsed &&
                  secondContent.Visibility == Visibility.Visible,
                  "switching tabs keeps the previous view in the visual tree");
            contentHost.Show(firstContent);
            Check(contentHost.Children.Count == 2 && firstContent.Visibility == Visibility.Visible &&
                  secondContent.Visibility == Visibility.Collapsed,
                  "returning to a tab reuses its existing view object");
            contentHost.Remove(secondContent);
            Check(contentHost.Children.Count == 1 &&
                  VisualTreeHelper.GetParent(secondContent) is null,
                  "closing a tab removes only its own view");
            var paneGrid = new Grid();
            var panes = Enumerable.Range(0, 4).Select(_ => new Border()).ToArray();
            PaneLayoutHost.Apply(paneGrid, panes, LayoutPreset.Find(4));
            PaneLayoutHost.Apply(paneGrid, panes, LayoutPreset.Find(1));
            Check(paneGrid.Children.Count == 4 && panes[0].Visibility == Visibility.Visible &&
                  panes.Skip(1).All(pane => pane.Visibility == Visibility.Collapsed) &&
                  panes.All(pane => ReferenceEquals(VisualTreeHelper.GetParent(pane), paneGrid)),
                  "hiding panes keeps every pane in the visual tree");
            PaneLayoutHost.Apply(paneGrid, panes, LayoutPreset.Find(4));
            Check(panes.All(pane => pane.Visibility == Visibility.Visible) &&
                  Grid.GetRow(panes[3]) == 1 && Grid.GetColumn(panes[3]) == 1,
                  "restoring four panes reuses their original controls");
        }
        catch (Exception ex) { layoutFailure = ex; }
    });
    layoutThread.SetApartmentState(ApartmentState.STA);
    layoutThread.Start();
    layoutThread.Join();
    if (layoutFailure is not null) throw layoutFailure;

    var driveCrumbs = BreadcrumbPath.Build(@"C:\Users\marvin");
    Check(driveCrumbs.Count == 3 && driveCrumbs[0] == new BreadcrumbSegment("C:", @"C:\") &&
          driveCrumbs[1] == new BreadcrumbSegment("Users", @"C:\Users") &&
          driveCrumbs[2] == new BreadcrumbSegment("marvin", @"C:\Users\marvin"),
          "drive breadcrumbs navigate to each directory level");
    var networkCrumbs = BreadcrumbPath.Build(@"\\server\share\docs\2026");
    Check(networkCrumbs.Count == 3 &&
          Path.TrimEndingDirectorySeparator(networkCrumbs[0].Path) == @"\\server\share" &&
          networkCrumbs[0].Label == @"\\server\share" &&
          networkCrumbs[2].Path == @"\\server\share\docs\2026",
          "UNC breadcrumbs keep the server and share as one root");
    Check(BreadcrumbPath.Build(@"C:\").Count == 1 &&
          BreadcrumbPath.Build(@"folder\child").Count == 1,
          "root and relative paths do not gain misleading extra segments");

    var uiIni = Path.Combine(root, "UiPreferences.ini");
    var uiStore = new UiPreferencesStore(uiIni);
    Check(!uiStore.Load() && uiStore.LoadWarning is null, "missing appearance profile uses normal spacing");
    uiStore.Save(true);
    Check(uiStore.Load() && uiStore.LoadWarning is null, "compact spacing round-trips in portable INI");
    var uiContent = File.ReadAllText(uiIni);
    File.AppendAllText(uiIni, "; edited elsewhere" + Environment.NewLine);
    try { uiStore.Save(false); throw new Exception("external appearance edit was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(uiIni).Contains("edited elsewhere", StringComparison.Ordinal),
        "external appearance changes remain untouched");
    File.WriteAllText(uiIni, uiContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal));
    Check(!uiStore.Load() && uiStore.LoadWarning is not null,
        "future appearance format is protected");
    try { uiStore.Save(false); throw new Exception("future appearance format was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(uiIni).Contains("FormatVersion=2", StringComparison.Ordinal),
        "future appearance file stays untouched");
    const string hugeVersion = "999999999999999999999999999999999999";
    var hugeUiContent = uiContent.Replace("FormatVersion=1",
        $"FormatVersion={hugeVersion}{Environment.NewLine}FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(uiIni, hugeUiContent);
    Check(!uiStore.Load() && uiStore.LoadWarning is not null,
        "oversized future appearance version is protected even with a later version one");
    try { uiStore.Save(false); throw new Exception("oversized future appearance format was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(uiIni) == hugeUiContent, "oversized future appearance file stays untouched");
    File.WriteAllText(uiIni, uiContent.Replace("Compact=true", "Compact=broken", StringComparison.Ordinal));
    Check(!uiStore.Load() && uiStore.LoadWarning is not null &&
          Directory.GetFiles(root, "UiPreferences.ini.invalid-*.bak").Length == 1,
          "invalid appearance file is backed up before recovery");
    uiStore.Save(false);
    Check(!uiStore.Load() && uiStore.LoadWarning is null,
          "recovered appearance settings can be saved cleanly");

    var pinsIni = Path.Combine(root, "PinnedTabs.ini");
    if (File.Exists(pinsIni)) File.Delete(pinsIni);
    foreach (var backup in Directory.GetFiles(root, "PinnedTabs.ini.invalid-*.bak")) File.Delete(backup);
    var pinsStore = new PinnedTabsStore(pinsIni);
    Check(pinsStore.Load().Count == 0, "missing pinned tabs profile starts empty");
    var pinned = new[] { new PinnedTabEntry(0, 1, second.Path) };
    pinsStore.Save(pinned);
    var loadedPins = pinsStore.Load();
    Check(loadedPins.SequenceEqual(pinned) &&
          PinnedTabsStore.MatchPane([first, second], loadedPins, 0).SequenceEqual([1]) &&
          !PinnedTabsStore.MatchPane([first, new TabSettings(@"C:\other", ViewState.Default())], loadedPins, 0).Any(),
          "pinned duplicate path matches its saved position and ignores a changed session slot");
    var pinsContent = File.ReadAllText(pinsIni);
    File.AppendAllText(pinsIni, "; changed by another instance" + Environment.NewLine);
    try { pinsStore.Save([]); throw new Exception("external pin edit was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(pinsIni).Contains("changed by another instance", StringComparison.Ordinal),
          "external pinned tab edits remain untouched");
    File.WriteAllText(pinsIni, pinsContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal));
    Check(pinsStore.Load().Count == 0 && pinsStore.LoadWarning is not null,
          "future pinned tab format is protected");
    try { pinsStore.Save([]); throw new Exception("future pinned tab format was overwritten"); }
    catch (IOException) { }
    var hugePinsContent = pinsContent.Replace("FormatVersion=1",
        $"FormatVersion={hugeVersion}{Environment.NewLine}FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(pinsIni, hugePinsContent);
    Check(pinsStore.Load().Count == 0 && pinsStore.LoadWarning is not null,
          "oversized future pinned tab version is protected even with a later version one");
    try { pinsStore.Save([]); throw new Exception("oversized future pinned tabs format was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(pinsIni) == hugePinsContent, "oversized future pinned tabs file stays untouched");
    File.WriteAllText(pinsIni, pinsContent.Replace("Count=1", "Count=2", StringComparison.Ordinal));
    Check(pinsStore.Load().Count == 1 && pinsStore.LoadWarning is not null,
          "pinned tab recovery remains readable");
    Check(Directory.GetFiles(root, "PinnedTabs.ini.invalid-*.bak").Length == 1,
          "invalid pinned tab file is backed up before recovery");

    var favoritesIni = Path.Combine(root, "Favorites.ini");
    var favoritesStore = new FavoritesStore(favoritesIni);
    Check(favoritesStore.Load().Count == 0, "missing favorites profile starts empty");
    favoritesStore.Save([@"C:\项目=测试", @"\\server\share\tools", @"c:\项目=测试\"]);
    var favorites = favoritesStore.Load();
    Check(favoritesStore.LoadWarning is null && favorites.Count == 2 &&
          favorites[0] == @"C:\项目=测试" && favorites[1] == @"\\server\share\tools",
          "favorites round-trip Unicode, equals signs, UNC paths, and case-insensitive duplicates");
    var lockedFavoritesIni = Path.Combine(root, "locked-favorites.ini");
    var lockedFavorites = new FavoritesStore(lockedFavoritesIni);
    Check(lockedFavorites.Load().Count == 0, "locked favorites test starts without a file");
    using (var heldWrite = new Mutex(false, PortableIniWriter.MutexName(lockedFavoritesIni)))
    {
        Check(heldWrite.WaitOne(TimeSpan.FromSeconds(1)), "test holds favorites write gate");
        try
        {
            var blocked = Task.Run(() =>
            {
                try { lockedFavorites.Save([@"C:\blocked"]); return false; }
                catch (IOException) { return true; }
            }).GetAwaiter().GetResult();
            Check(blocked && !File.Exists(lockedFavoritesIni),
                "favorites writer does not bypass another instance's write gate");
        }
        finally { heldWrite.ReleaseMutex(); }
    }
    lockedFavorites.Save([@"C:\allowed"]);
    Check(lockedFavorites.Load().Single() == @"C:\allowed", "favorites save succeeds after write gate releases");
    var favoriteContent = File.ReadAllText(favoritesIni);
    File.AppendAllText(favoritesIni, "; edit in another instance" + Environment.NewLine);
    try
    {
        favoritesStore.Save([@"C:\new"]);
        throw new Exception("concurrent favorites edit was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(favoritesIni).Contains("edit in another instance", StringComparison.Ordinal),
          "concurrent favorites edit is not overwritten");
    File.WriteAllText(favoritesIni, favoriteContent.Replace("Count=2", "Count=3", StringComparison.Ordinal));
    var recoveredFavorites = favoritesStore.Load();
    Check(recoveredFavorites.Count == 2 && favoritesStore.LoadWarning is not null &&
          Directory.GetFiles(root, "Favorites.ini.invalid-*.bak").Length == 1,
          "damaged favorites count is backed up and readable entries survive");
    favoritesStore.Save(recoveredFavorites);
    var savedFavorites = favoritesStore.Load();
    Check(savedFavorites.Count == 2 && favoritesStore.LoadWarning is null,
          "recovered favorites can be saved");
    var futureFavorites = favoriteContent.Replace("FormatVersion=1", "FormatVersion=2", StringComparison.Ordinal);
    File.WriteAllText(favoritesIni, futureFavorites);
    Check(favoritesStore.Load().Count == 0 && favoritesStore.LoadWarning is not null,
          "newer favorites format is not interpreted");
    try
    {
        favoritesStore.Save([@"C:\new"]);
        throw new Exception("newer favorites format was overwritten");
    }
    catch (IOException) { }
    Check(File.ReadAllText(favoritesIni) == futureFavorites, "newer favorites profile stays untouched");
    var hugeFavorites = favoriteContent.Replace("FormatVersion=1",
        $"FormatVersion={hugeVersion}{Environment.NewLine}FormatVersion=1", StringComparison.Ordinal);
    File.WriteAllText(favoritesIni, hugeFavorites);
    Check(favoritesStore.Load().Count == 0 && favoritesStore.LoadWarning is not null,
          "oversized future favorites version is protected even with a later version one");
    try { favoritesStore.Save([@"C:\new"]); throw new Exception("oversized future favorites was overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(favoritesIni) == hugeFavorites, "oversized future favorites file stays untouched");
    Console.WriteLine("Settings smoke: PASS");
}
finally
{
    // The only recursive cleanup target is this fixed child of the test output directory.
    var resolved = Path.GetFullPath(root);
    var expectedParent = Path.GetFullPath(AppContext.BaseDirectory);
    if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(expectedParent))
        Directory.Delete(resolved, true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Failed: {name}");
}

[ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITestObjectWithSite
{
    [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);
    [PreserveSig] int GetSite(ref Guid iid, out IntPtr result);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int NativeGetViewFlags(IntPtr self, out uint flags);

internal sealed class FakeShellViewStateSource : IShellViewStateSource
{
    public uint Mode { get; init; }
    public int IconSize { get; init; }
    public IReadOnlyList<SortColumnState> Sorts { get; init; } = [];
    public PropertyKeyState? Group { get; init; }
    public bool GroupAscending { get; init; }
    public string? FailedStep { get; set; }

    public int GetMode(out uint mode, out int iconSize)
    {
        mode = Mode;
        iconSize = IconSize;
        return FailedStep == "mode" ? -1 : 0;
    }

    public int GetSortColumnCount(out int count)
    {
        count = FailedStep == "invalid-count" ? 17 : Sorts.Count;
        return FailedStep == "count" ? -1 : 0;
    }

    public int GetSortColumns(int count, out IReadOnlyList<SortColumnState> columns)
    {
        columns = FailedStep == "short-columns" ? [] : Sorts;
        return FailedStep == "columns" ? -1 : 0;
    }

    public int GetGroupBy(out PropertyKeyState? key, out bool ascending)
    {
        key = Group;
        ascending = GroupAscending;
        return FailedStep == "group" ? -1 : 0;
    }
}
