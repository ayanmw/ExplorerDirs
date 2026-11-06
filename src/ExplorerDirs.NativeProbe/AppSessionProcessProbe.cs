using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExplorerDirs.App;
using Microsoft.Win32.SafeHandles;

// Full executable lifecycle on the already-created, never-switched probe Desktop.
// Only processes created here are queried/closed; no input injection or Shell restart.
internal static class AppSessionProcessProbe
{
    internal static int Run(string preparedRun, bool softRefresh = true, bool settleExternalChanges = false, string? adapterOverride = null, int fillerCount = 0)
    {
        preparedRun = Path.GetFullPath(preparedRun);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(preparedRun, "preparation.json")));
        if (manifest.RootElement.GetProperty("Status").GetString() != "prepared-not-run")
            throw new InvalidOperationException("Expected an explicitly prepared acceptance payload.");
        var projectedExperiment = manifest.RootElement.TryGetProperty("ProjectedNotificationExperiment", out var projectedFlag) && projectedFlag.GetBoolean();
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var desktopName = ObjectName(desktop);
        if (!desktopName.StartsWith("ExplorerDirs-Probe-", StringComparison.Ordinal))
            throw new InvalidOperationException("Full app probe requires the isolated probe Desktop.");
        var station = ObjectName(GetProcessWindowStation());
        var sourceApp = Path.Combine(preparedRun, "app");
        var stateRoot = Path.GetFullPath(Path.Combine(preparedRun, "..", "..", "app-session-process"));
        var run = Path.Combine(stateRoot, Guid.NewGuid().ToString("N"));
        var app = Path.Combine(run, "app");
        Directory.CreateDirectory(app);
        foreach (var file in Directory.EnumerateFiles(sourceApp, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceApp, file);
            if (relative.StartsWith("profile" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(app, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        if (adapterOverride is not null)
        {
            adapterOverride = Path.GetFullPath(adapterOverride);
            File.Copy(adapterOverride, Path.Combine(app, "ExplorerDirs.ShellNamespace.dll"), overwrite: true);
            Console.WriteLine($"app-session-adapter-override: path={adapterOverride}; sha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(adapterOverride)))}; run={run}");
        }
        var profile = Path.Combine(app, "profile");
        Directory.CreateDirectory(profile);
        var nameSortControl = Environment.GetEnvironmentVariable("EXPLORERDIRS_PROBE_NAME_SORT_CONTROL") == "1";
        Console.WriteLine($"app-session-sort-control: name-sort={nameSortControl}");
        var panes = new List<List<TabSettings>>();
        var key = new PropertyKeyState(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 12);
        for (var pane = 0; pane < 4; pane++)
        {
            var tabs = new List<TabSettings>();
            for (var tab = 0; tab < (pane == 0 ? 2 : 1); tab++)
            {
                var directory = Path.Combine(run, "fixtures", $"pane-{pane}-tab-{tab}");
                foreach (var (name, size) in new[] { ("small", 5), ("large", 29) })
                {
                    var child = Path.Combine(directory, name);
                    Directory.CreateDirectory(child);
                    File.WriteAllBytes(Path.Combine(child, "data.bin"), new byte[size]);
                }
                for (var filler = 0; filler < fillerCount; filler++)
                    File.WriteAllBytes(Path.Combine(directory, $"filler-{filler:D5}.txt"), new byte[1024]);
                var view = new ViewState(4, 16, [new(nameSortControl ? new(key.FormatId, 10) : key, pane % 2 == 0 ? 1 : -1)], pane == 1 ? key : null, true,
                    [new(new(key.FormatId, 10), 240), new(key, 160), new(new(key.FormatId, 4), 140)])
                    { MixedSizeSort = pane % 2 != 0 };
                tabs.Add(new(directory, view));
            }
            panes.Add(tabs);
        }
        var expected = new SessionState(4, 4, panes, 2, [1, 0, 0, 0], 16, [false, false, false, false], TreeDisplayMode.None);
        var sessionPath = Path.Combine(profile, "ExplorerDirs.ini");
        new SessionStore(sessionPath, sessionPath + ".legacy").Save(expected);
        // The seed deliberately contains historical per-tab policies. The new global
        // preference is absent, so default false must win when the App saves it back.
        var expectedPersisted = expected with
        {
            Panes = expected.Panes.Select(tabs => tabs.Select(tab =>
                tab with { View = tab.View with { MixedSizeSort = false } }).ToList()).ToList()
        };
        const string seed = ";full-process-seed-must-be-rewritten\n";
        File.WriteAllText(sessionPath, seed + File.ReadAllText(sessionPath));
        File.WriteAllText(Path.Combine(profile, "FolderSize.ini"), "[FolderSize]\nFormatVersion=1\nEnabled=true\nSource=BuiltIn\n");
        File.WriteAllText(Path.Combine(profile, "SoftRefresh.ini"), $"[View]\nFormatVersion=1\nSoftRefresh={softRefresh}\n");
        Console.WriteLine($"app-session-configuration: soft-refresh={softRefresh}");
        var executable = Path.Combine(app, "ExplorerDirs.App.exe");
        var expectedPlacement = default(WindowPlacementState);
        var pids = new List<uint>();
        for (var pass = 0; pass < 2; pass++)
        {
            var projectedLog = Path.Combine(app, "projected-monitor-experiment.log");
            var projectedLogStart = File.Exists(projectedLog) ? File.ReadAllLines(projectedLog).Length : 0;
            void VerifyProjectedMonitors(bool requireSettled = false)
            {
                if (!projectedExperiment) return;
                var lines = File.Exists(projectedLog) ? File.ReadAllLines(projectedLog).Skip(projectedLogStart).ToArray() : [];
                if (lines.Any(line => line.Contains(" error path=", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Projected App monitor stopped with an error: " + string.Join(" | ", lines));
                for (var pane = 0; pane < 4; pane++)
                {
                    var activePath = expected.Panes[pane][expected.ActiveTabIndices![pane]].Path;
                    if (!lines.Any(line => line.Contains($" started path={activePath};", StringComparison.Ordinal)))
                        throw new InvalidOperationException($"Projected App monitor was not started for pane {pane + 1}.");
                    if (requireSettled)
                    {
                        var lastDispatch = lines.LastOrDefault(line => line.Contains($" dispatch path={activePath};", StringComparison.Ordinal));
                        var lastSettled = lines.LastOrDefault(line => line.Contains($" settled path={activePath};", StringComparison.Ordinal));
                        if (lastDispatch is null || lastSettled is null ||
                            lastDispatch.Split(";events=")[1] != lastSettled.Split(";events=")[1].Split(';')[0])
                            throw new InvalidOperationException($"Projected App pane {pane + 1} still has unconfirmed work.");
                    }
                }
                foreach (var line in lines.Where(line => line.Contains(" settled path=", StringComparison.Ordinal)))
                {
                    var counts = line.Split(";enumerations=")[1].Split("->");
                    if (counts[0] != counts[1]) throw new InvalidOperationException("Projected App re-enumerated after watcher work: " + line);
                }
                Console.WriteLine($"app-projected-monitors: pass={pass + 1}; four-active-paths=True; no-errors=True");
            }
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = station + "\\" + desktopName, Flags = 0x80 };
            if (!CreateProcessW(executable, new StringBuilder('"' + executable + '"'), 0, 0, false, 0, 0, app, ref startup, out var child))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start isolated app process.");
            CloseHandle(child.Thread);
            pids.Add(child.ProcessId);
            try
            {
                nint window = 0;
                Wait(() =>
                {
                    EnsureRunning(child);
                    window = FindMainWindow(desktop, child.ProcessId);
                    return window != 0 && VisibleFileViews(window).Count == 4;
                }, TimeSpan.FromSeconds(30), "Full app did not create its four visible file views.");
                // FindMainWindow enumerates only this explicit Desktop. A foreign thread's
                // GetThreadDesktop handle is not borrowed into this process for inspection.
                GetWindowThreadProcessId(window, out var pid);
                if (pid != child.ProcessId) throw new InvalidOperationException("Owned app window PID changed.");
                string? lastReadiness = null;
                Wait(() =>
                {
                    EnsureRunning(child);
                    var lists = VisibleFileViews(window);
                    var readings = lists.Select(list => (Window: list, Rows: Message(list, 0x1004), Grouped: Message(list, 0x10AF))).ToArray();
                    var readiness = string.Join(";", readings.Select(item => $"hwnd=0x{item.Window:X},rows={item.Rows},grouped={item.Grouped}"));
                    if (readiness != lastReadiness)
                    {
                        Console.WriteLine($"app-session-readiness: pass={pass + 1}; {readiness}");
                        lastReadiness = readiness;
                    }
                    return readings.Length == 4 && readings.All(item => item.Rows == 2 + fillerCount) &&
                        readings.Any(item => item.Grouped == 1);
                }, TimeSpan.FromSeconds(60), "Full app views/grouping did not become ready.");
                VerifyProjectedMonitors();
                VerifyExternalDirectoryChanges(window, child, expected, pass, settleExternalChanges, fillerCount, adapterOverride is null ? null : Path.Combine(app, "ExplorerDirs.ShellNamespace.dll.size-trace.log"));
                if (projectedExperiment)
                {
                    string? lastProjectedFailure = null;
                    try
                    {
                        Wait(() =>
                        {
                            try { VerifyProjectedMonitors(requireSettled: true); return true; }
                            catch (InvalidOperationException error) { lastProjectedFailure = error.Message; return false; }
                        }, TimeSpan.FromSeconds(5), "Projected monitors did not settle without re-enumeration/errors.");
                    }
                    catch (TimeoutException error)
                    {
                        throw new InvalidOperationException(lastProjectedFailure ?? error.Message, error);
                    }
                }
                if (pass == 0)
                {
                    if (!GetWindowRect(window, out var bounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    // Keep the resized normal rectangle inside the initial window's monitor region.
                    var width = Math.Max(900, (bounds.Right - bounds.Left) * 85 / 100);
                    var height = Math.Max(650, (bounds.Bottom - bounds.Top) * 85 / 100);
                    if (!SetWindowPos(window, 0, bounds.Left + 24, bounds.Top + 24, width, height, 0x4014))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    Wait(() => GetWindowRect(window, out var changed) && changed.Left != bounds.Left,
                        TimeSpan.FromSeconds(5), "Owned window position did not change.");
                    expectedPlacement = NativeWindowPlacement.Capture(window, false);
                }
                else
                {
                    Wait(() => NativeWindowPlacement.Capture(window, false) == expectedPlacement,
                        TimeSpan.FromSeconds(5), "Second process did not restore the first window's native placement.");
                }
                if (!PostMessageW(window, 0x10, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Wait(() => WaitForSingleObject(child.Process, 0) == 0, TimeSpan.FromSeconds(30), "Owned app did not close normally.");
                if (!GetExitCodeProcess(child.Process, out var exit) || exit != 0)
                    throw new InvalidOperationException($"Owned app exited with code {exit}.");
                if (File.ReadAllText(sessionPath).Contains(seed, StringComparison.Ordinal))
                    throw new InvalidOperationException("App did not actually rewrite its seeded session.");
                VerifySession(expectedPersisted, new SessionStore(sessionPath, sessionPath + ".legacy").Load());
                var savedPlacement = new WindowPlacementStore(WindowPlacementStore.PathForSession(sessionPath)).Load();
                if (savedPlacement != expectedPlacement) throw new InvalidOperationException("App saved a different window placement.");
                Console.WriteLine($"app-session-process: pass={pass + 1}; pid={child.ProcessId}; exit=0; session-rewritten=True; five-tabs-four-panes=True; placement={savedPlacement}");
            }
            finally
            {
                if (WaitForSingleObject(child.Process, 0) != 0)
                {
                    try
                    {
                        using var dump = new FileStream(Path.Combine(run, $"failed-child-{child.ProcessId}.dmp"), FileMode.CreateNew, FileAccess.Write);
                        if (!MiniDumpWriteDump(child.Process, child.ProcessId, dump.SafeFileHandle, 0x1000, 0, 0, 0))
                            Console.Error.WriteLine($"app-session-child-dump-failed: {Marshal.GetLastWin32Error()}");
                    }
                    finally
                    {
                        TerminateProcess(child.Process, 2);
                        WaitForSingleObject(child.Process, 5000);
                    }
                }
                var exited = WaitForSingleObject(child.Process, 0) == 0;
                CloseHandle(child.Process);
                if (!exited) throw new InvalidOperationException($"Owned child {child.ProcessId} did not terminate; profile retained.");
                CleanupOwnedProfile(profile);
            }
        }
        File.WriteAllText(Path.Combine(run, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Pids = pids, Placement = expectedPlacement, FillerCount = fillerCount,
            Scope = "Full executable two-process lifecycle on never-switched Desktop; no visible/input acceptance",
            PreparedSourceCommit = manifest.RootElement.GetProperty("SourceCommit").GetString()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"app-session-process: PASS; independent-processes={string.Join('|', pids)}; run={run}");
        return 0;
    }

    internal static void CleanupOwnedProfile(string profile)
    {
        profile = Path.GetFullPath(profile);
        if (!profile.Contains(Path.DirectorySeparatorChar + "app-session-process" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(profile) != "profile")
            throw new InvalidOperationException("Cleanup is limited to this probe's isolated profiles.");
        var identityPath = Path.Combine(profile, "ShellNamespace.ini");
        if (!File.Exists(identityPath)) return;
        var identity = new ShellNamespaceProfileStore(identityPath).LoadOrCreate();
        if (identity is null) throw new InvalidOperationException("Cannot verify owned Shell registration identity.");
        var native = Path.Combine(Path.GetDirectoryName(profile)!, "ExplorerDirs.ShellNamespace.dll");
        typeof(ProfileShellNamespaceOwner).GetMethod("DeleteOwnedRegistration", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [identity.Value, native, profile]);
        Console.WriteLine($"app-session-process-registration-cleanup: profile-identity={identity}; ownership-checked=True");
    }

    private static void VerifyExternalDirectoryChanges(nint window, ProcessInfo child, SessionState session, int pass, bool settleExternalChanges, int fillerCount, string? tracePath)
    {
        if (settleExternalChanges)
        {
            Console.WriteLine("app-external-change-settle-control: wait-ms=3000; before-external-mutation=True");
            Thread.Sleep(3000);
        }
        var additions = session.Panes.Select((tabs, pane) => Path.Combine(
            tabs[session.ActiveTabIndices![pane]].Path, "external-notification-fixture")).ToArray();
        // Observe OS directory events independently of Shell delivery. This is evidence
        // only: never emit replacement notifications or change reconciliation deadlines.
        var observedChanges = 0;
        FileSystemWatcher Observe(int pane)
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(additions[pane])!)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false
            };
            void Record(string kind, string? name)
            {
                if (name?.StartsWith("external-", StringComparison.OrdinalIgnoreCase) != true) return;
                if (Interlocked.Increment(ref observedChanges) <= 512)
                    Console.WriteLine($"app-filesystem-observed: pane={pane + 1}; kind={kind}; item={name}; tick={Environment.TickCount64}");
            }
            watcher.Created += (_, change) => Record("create", change.Name);
            watcher.Deleted += (_, change) => Record("delete", change.Name);
            watcher.Changed += (_, change) => Record("change", change.Name);
            watcher.Renamed += (_, change) => Record("rename", change.Name);
            watcher.Error += (_, error) => Console.WriteLine($"app-filesystem-observer-error: pane={pane + 1}; error={error.GetException().GetType().Name}");
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        using var observer1 = Observe(0);
        using var observer2 = Observe(1);
        using var observer3 = Observe(2);
        using var observer4 = Observe(3);
        using var shellObserver = new AppSessionShellObserver(additions.Select(path => Path.GetDirectoryName(path)!));
        nint[] grouped = [];
        // Observer setup parses PIDLs and may pump startup callbacks. Re-establish
        // the existing four-pane/group/count fixture after that work, before mutation.
        Wait(() =>
        {
            shellObserver.Pump();
            EnsureRunning(child);
            var lists = VisibleFileViews(window);
            var candidate = lists.Where(list => Message(list, 0x10AF) == 1).ToArray();
            if (lists.Count != 4 || candidate.Length != 1 || lists.Any(list => Message(list, 0x1004) != fillerCount + 2))
                return false;
            grouped = candidate;
            return true;
        }, TimeSpan.FromSeconds(15), "Four-pane/group/count fixture did not settle after observer setup.");
        var transientEmptySamples = 0;
        var zeroPaneSamplesWithDisableFlag = 0;
        var zeroPaneSamplesWithVisibilityChange = 0;
        var zeroPaneSamplesWithoutDisableEvidence = 0;
        void AwaitRows(int count, string stage)
        {
            count += fillerCount;
            var elapsed = Stopwatch.StartNew();
            string? lastState = null;
            try
            {
                Wait(() =>
                {
                    shellObserver.Pump();
                    EnsureRunning(child);
                    var lists = VisibleFileViews(window);
                    var sampleStartedAt = Environment.TickCount64;
                    var samples = lists.Select(list =>
                    {
                        var visibleBefore = IsWindowVisible(list);
                        var disabledBefore = GetPropW(list, "SysSetRedraw") != 0;
                        var rows = Message(list, 0x1004);
                        return (Rows: rows, VisibleBefore: visibleBefore, DisabledBefore: disabledBefore,
                            VisibleAfter: IsWindowVisible(list), DisabledAfter: GetPropW(list, "SysSetRedraw") != 0);
                    }).ToArray();
                    var counts = samples.Select(sample => sample.Rows).ToArray();
                    if (counts.Any(rows => rows == 0)) transientEmptySamples++;
                    foreach (var sample in samples.Where(sample => sample.Rows == 0))
                    {
                        if (sample.DisabledBefore && sample.DisabledAfter) zeroPaneSamplesWithDisableFlag++;
                        else if (!sample.VisibleBefore || !sample.VisibleAfter) zeroPaneSamplesWithVisibilityChange++;
                        else zeroPaneSamplesWithoutDisableEvidence++;
                    }
                    // SysSetRedraw==0 also means the property is absent: do not label
                    // that as proven redraw-enabled or equate any zero count with a painted blank.
                    var rendering = string.Join('|', samples.Select(sample =>
                        $"v{(sample.VisibleBefore ? 1 : 0)}>{(sample.VisibleAfter ? 1 : 0)}/d{(sample.DisabledBefore ? 1 : 0)}>{(sample.DisabledAfter ? 1 : 0)}"));
                    var state = $"counts={string.Join('|', counts)}; grouped={string.Join('|', grouped.Select(list => Message(list, 0x10AF)))}; render-flags={rendering}";
                    if (state != lastState)
                    {
                        // Bracket these cross-process reads so zero-row observations can
                        // be correlated with native structure/redraw logs without treating
                        // the final read's timestamp as the time every pane was sampled.
                        Console.WriteLine($"app-external-change-state: stage={stage}; {state}; sample-ticks={sampleStartedAt}..{Environment.TickCount64}");
                        lastState = state;
                    }
                    return lists.Count == 4 && counts.All(rows => rows == count) &&
                        grouped.All(list => Message(list, 0x10AF) == 1);
                }, TimeSpan.FromSeconds(15), $"External {stage} did not update all four panes while retaining grouping.");
            }
            catch (TimeoutException)
            {
                // Failure-only observation cannot turn a pending assertion into a pass.
                if (fillerCount > 0)
                {
                    foreach (var list in VisibleFileViews(window))
                    {
                        try
                        {
                            var before = Message(list, 0x1004);
                            var names = new List<string>();
                            for (var row = 0; row < Math.Min(before, 2048); row++)
                            {
                                var name = OwnedAppListText.Read(child.Process, child.ProcessId, list, row, 0);
                                if (!name.StartsWith("filler-", StringComparison.Ordinal)) names.Add(name);
                            }
                            Console.WriteLine($"app-failed-row-inventory: stage={stage};hwnd=0x{list:X};before={before};after={Message(list, 0x1004)};non-filler={string.Join('|', names)}");
                        }
                        catch (Exception error) { Console.WriteLine($"app-failed-row-inventory-error: {error.GetType().Name}"); }
                    }
                }
                throw;
            }
            Mark("settled-" + stage);
            Console.WriteLine($"app-external-change: pass={pass + 1}; stage={stage}; rows-per-pane={count}; ms={elapsed.ElapsedMilliseconds}; transient-empty-samples={transientEmptySamples}; zero-pane-disable-flag={zeroPaneSamplesWithDisableFlag}; zero-pane-hidden={zeroPaneSamplesWithVisibilityChange}; zero-pane-no-disable-evidence={zeroPaneSamplesWithoutDisableEvidence}; grouped-pane-preserved=True");
        }
        void AwaitFileRow(string name, ulong bytes, string stage, string? forbiddenName = null)
        {
            var expectedSize = ByteSizeFormatter.Format(bytes, SizeDisplayOptions.Default);
            string? lastRows = null;
            Wait(() =>
            {
                EnsureRunning(child);
                var lists = VisibleFileViews(window);
                var found = 0;
                var snapshots = new List<string>();
                foreach (var list in lists)
                {
                    var count = Message(list, 0x1004);
                    if (count != 3) return false;
                    var matches = 0;
                    for (var row = 0; row < 3; row++)
                    {
                        var actualName = OwnedAppListText.Read(child.Process, child.ProcessId, list, row, 0);
                        var actualSize = OwnedAppListText.Read(child.Process, child.ProcessId, list, row, 1);
                        snapshots.Add($"{actualName}:{actualSize}");
                        if (actualName == forbiddenName) return false;
                        if (actualName == name && actualSize == expectedSize) matches++;
                    }
                    if (matches == 1) found++;
                }
                var snapshot = string.Join('|', snapshots);
                if (snapshot != lastRows)
                {
                    Console.WriteLine($"app-native-row-state: stage={stage}; rows={snapshot}");
                    lastRows = snapshot;
                }
                return lists.Count == 4 && found == 4;
            }, TimeSpan.FromSeconds(15), $"Actual native rows did not show {name} / {expectedSize} after {stage}.");
            Console.WriteLine($"app-native-row: pass={pass + 1}; stage={stage}; name={name}; size={expectedSize}; four-panes=True");
        }
        void Mark(string stage)
        {
            if (tracePath is not null) File.AppendAllText(tracePath + ".phases.log", $"{Environment.TickCount64} probe-phase={stage} pass={pass + 1}\n");
        }
        // Mutate only fresh test roots created above, without commands or input to the App.
        Mark("before-create");
        foreach (var directory in additions)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "data.bin"), new byte[64]);
        }
        AwaitRows(3, "create");
        Mark("before-delete");
        foreach (var directory in additions)
        {
            File.Delete(Path.Combine(directory, "data.bin"));
            Directory.Delete(directory);
        }
        AwaitRows(2, "delete");
        Mark("delete-completed");

        var files = additions.Select(directory => Path.Combine(Path.GetDirectoryName(directory)!, "external-file.txt")).ToArray();
        Mark("before-file-create");
        foreach (var file in files) File.WriteAllBytes(file, new byte[37]);
        AwaitRows(3, "file-create");
        Mark("file-create-completed");
        foreach (var file in files) File.Delete(file);
        AwaitRows(2, "file-delete");
        var burstFiles = files.SelectMany(file => Enumerable.Range(0, 3).Select(index => file + $".burst-{index}")).ToArray();
        foreach (var file in burstFiles) File.WriteAllBytes(file, new byte[19]);
        AwaitRows(5, "file-burst-create");
        foreach (var file in burstFiles) File.Delete(file);
        AwaitRows(2, "file-burst-delete");
        if (fillerCount > 0) return; // Large mode covers directory/file/burst changes; row-text cases remain separate.
        // Ordinary file rows retain Explorer's KB display rounding. Use whole KB
        // values so replacement metadata is distinguishable in the actual cells.
        foreach (var file in files) File.WriteAllBytes(file, new byte[37 * 1024]);
        AwaitRows(3, "same-name-seed");
        AwaitFileRow("external-file.txt", 37 * 1024, "same-name-seed");
        foreach (var file in files)
        {
            File.Delete(file);
            File.WriteAllBytes(file, new byte[83 * 1024]);
        }
        // Let the delayed delete quiet period expire before checking the replacement.
        Thread.Sleep(2000);
        AwaitRows(3, "same-name-recreate-settled");
        AwaitFileRow("external-file.txt", 83 * 1024, "same-name-recreate-settled");
        if (files.Any(file => new FileInfo(file).Length != 83 * 1024))
            throw new InvalidOperationException("Same-name replacement content was unexpectedly changed.");
        var renamed = files.Select(file => file + ".renamed").ToArray();
        for (var index = 0; index < files.Length; index++) File.Move(files[index], renamed[index]);
        AwaitFileRow("external-file.txt.renamed", 83 * 1024, "file-rename", forbiddenName: "external-file.txt");
        foreach (var file in renamed) File.Delete(file);
        AwaitRows(2, "same-name-final-delete");
    }

    private static void VerifySession(SessionState expected, SessionState? actual)
    {
        if (actual is null || actual.Layout != expected.Layout || actual.ActivePane != expected.ActivePane ||
            !actual.ActiveTabIndices!.SequenceEqual(expected.ActiveTabIndices!))
            throw new InvalidOperationException("Full app session layout/active targets did not persist.");
        for (var pane = 0; pane < 4; pane++)
        {
            if (actual.Panes[pane].Count != expected.Panes[pane].Count) throw new InvalidOperationException("Full app lost a tab.");
            for (var tab = 0; tab < actual.Panes[pane].Count; tab++)
            {
                var left = actual.Panes[pane][tab]; var right = expected.Panes[pane][tab];
                if (!string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase) || left.View.Mode != right.View.Mode ||
                    left.View.MixedSizeSort != right.View.MixedSizeSort || left.View.GroupKey != right.View.GroupKey ||
                    left.View.GroupAscending != right.View.GroupAscending || !left.View.SortColumns.SequenceEqual(right.View.SortColumns) ||
                    !left.View.DisplayColumns!.SequenceEqual(right.View.DisplayColumns!))
                    throw new InvalidOperationException($"Full app saved unexpected tab settings: pane={pane}; tab={tab}; actual={JsonSerializer.Serialize(left)}; expected={JsonSerializer.Serialize(right)}");
            }
        }
    }
    private static void EnsureRunning(ProcessInfo child)
    {
        if (WaitForSingleObject(child.Process, 0) == 0) throw new InvalidOperationException("Owned app exited before readiness.");
    }
    private static void Wait(Func<bool> predicate, TimeSpan timeout, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.Elapsed >= timeout) throw new TimeoutException(message);
            Thread.Sleep(25);
        }
    }
    private static nint Message(nint window, uint message) =>
        SendMessageTimeoutW(window, message, 0, 0, 2, 200, out var result) != 0 ? result : -1;
    private static List<nint> VisibleFileViews(nint window)
    {
        var result = new List<nint>();
        EnumChildWindows(window, (child, _) =>
        {
            var name = new StringBuilder(256); GetClassNameW(child, name, name.Capacity);
            if (name.ToString() == "SysListView32" && IsWindowVisible(child)) result.Add(child);
            return true;
        }, 0);
        return result;
    }
    private static nint FindMainWindow(nint desktop, uint pid)
    {
        nint result = 0;
        EnumDesktopWindows(desktop, (window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            var name = new StringBuilder(256); GetClassNameW(window, name, name.Capacity);
            var title = new StringBuilder(512); GetWindowTextW(window, title, title.Capacity);
            if (owner == pid && IsWindowVisible(window) && name.ToString().StartsWith("HwndWrapper", StringComparison.Ordinal) &&
                title.ToString().StartsWith("Explorer Dirs", StringComparison.Ordinal)) { result = window; return false; }
            return true;
        }, 0);
        return result;
    }
    private static string ObjectName(nint handle)
    {
        var value = new StringBuilder(256);
        if (!GetUserObjectInformationW(handle, 2, value, value.Capacity * 2, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return value.ToString();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort Show, ReservedCount; public nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string app, StringBuilder args, nint processSecurity, nint threadSecurity, bool inherit, uint flags, nint environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint timeout);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint handle, out uint exit);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(nint handle, uint code);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] private static extern nint GetProcessWindowStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder value, int bytes, out int needed);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(nint desktop, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder value, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint window, StringBuilder value, int capacity);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetPropW(nint window, string name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("dbghelp.dll", SetLastError = true)] private static extern bool MiniDumpWriteDump(nint process, uint processId, SafeFileHandle file, uint flags, nint exception, nint user, nint callback);
}
