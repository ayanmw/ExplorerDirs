using ExplorerDirs.App;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
var first = new TabSettings(@"C:\项目=测试", new ViewState(4, 16,
    [new SortColumnState(sortKey, -1)], groupKey, false));
var second = new TabSettings(@"C:\项目=测试", ViewState.Default(6));
var state = new SessionState(2, 3, [[first, second], [], [], []]);

try
{
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
    Check(KeyboardShortcuts.Resolve(0x08, 0) == ExplorerShortcut.Parent, "Backspace goes to parent");
    Check(KeyboardShortcuts.Resolve(0x26, 1) == ExplorerShortcut.Parent, "Alt+Up goes to parent");
    Check(KeyboardShortcuts.Resolve(0x25, 1) == ExplorerShortcut.Back, "Alt+Left goes back");
    Check(KeyboardShortcuts.Resolve(0x27, 1) == ExplorerShortcut.Forward, "Alt+Right goes forward");
    Check(KeyboardShortcuts.Resolve(0x73, 0) == ExplorerShortcut.Address,
          "F4 selects the editable address field as in current File Explorer");
    Check(KeyboardShortcuts.Resolve(0x09, 0) == ExplorerShortcut.NextFocusStop &&
          KeyboardShortcuts.Resolve(0x09, 4) == ExplorerShortcut.PreviousFocusStop,
          "Tab and Shift+Tab traverse pane focus stops");
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
    Check(KeyboardShortcuts.Resolve(0x08, 2) == ExplorerShortcut.None,
          "Ctrl+Backspace remains available to text controls");
    Check(KeyboardShortcuts.Resolve(0x4e, 6) == ExplorerShortcut.NewFolder,
          "Ctrl+Shift+N routes to the Shell background New Folder command");
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

    var navigationGate = new DirectoryNavigationGate();
    using var oldProbeStarted = new ManualResetEventSlim();
    using var releaseOldProbe = new ManualResetEventSlim();
    var oldProbe = navigationGate.ProbeAsync(@"\\offline\share", _ =>
    {
        oldProbeStarted.Set();
        releaseOldProbe.Wait();
        return false;
    });
    try
    {
        Check(oldProbeStarted.Wait(TimeSpan.FromSeconds(3)), "first directory probe starts in background");
        var newProbe = navigationGate.ProbeAsync(@"C:\ready", _ => true).GetAwaiter().GetResult();
        Check(navigationGate.IsCurrent(newProbe) && newProbe.Exists,
              "new navigation can finish while older probe waits");
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
