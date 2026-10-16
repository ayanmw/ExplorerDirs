using ExplorerDirs.App;
using System.Text.Json;

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
    Check(KeyboardShortcuts.Resolve(0x08, 0) == ExplorerShortcut.Parent, "Backspace goes to parent");
    Check(KeyboardShortcuts.Resolve(0x26, 1) == ExplorerShortcut.Parent, "Alt+Up goes to parent");
    Check(KeyboardShortcuts.Resolve(0x25, 1) == ExplorerShortcut.Back, "Alt+Left goes back");
    Check(KeyboardShortcuts.Resolve(0x27, 1) == ExplorerShortcut.Forward, "Alt+Right goes forward");
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

    var favoritesIni = Path.Combine(root, "Favorites.ini");
    var favoritesStore = new FavoritesStore(favoritesIni);
    Check(favoritesStore.Load().Count == 0, "missing favorites profile starts empty");
    favoritesStore.Save([@"C:\项目=测试", @"\\server\share\tools", @"c:\项目=测试\"]);
    var favorites = favoritesStore.Load();
    Check(favoritesStore.LoadWarning is null && favorites.Count == 2 &&
          favorites[0] == @"C:\项目=测试" && favorites[1] == @"\\server\share\tools",
          "favorites round-trip Unicode, equals signs, UNC paths, and case-insensitive duplicates");
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
