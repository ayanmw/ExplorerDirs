using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Observe per-PIDL visibility while a stock native view re-enumerates a large folder.
// This probe never creates ExplorerBrowser, shows its parent, or sends input.
internal static class ShellViewSelectionEnumerationProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private const int FixtureCount = 20_000;
    private static readonly int[] TargetIndexes = [100, 5_000, 12_000, 19_998];

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        nint parent = 0, absolutePidl = 0, folder = 0, view = 0, hostPointer = 0, viewWindow = 0, folderView = 0;
        nint staleChildPidl = 0;
        StreamViewHost? host = null;
        var created = false;
        var childPidls = new List<nint>();
        try
        {
            Directory.CreateDirectory(fixture);
            for (var index = 0; index < FixtureCount; index++)
                File.WriteAllText(Path.Combine(fixture, $"item-{index:D5}.txt"), "fixture");
            var stalePath = Path.Combine(fixture, "removed-before-view.txt");
            File.WriteAllText(stalePath, "stale control");
            Check(SHParseDisplayName(stalePath, 0, out var staleAbsolutePidl, 0, out _));
            try
            {
                staleChildPidl = ILClone(ILFindLastID(staleAbsolutePidl));
                if (staleChildPidl == 0) throw new Exception("Could not retain the stale control PIDL.");
            }
            finally { Marshal.FreeCoTaskMem(staleAbsolutePidl); }
            File.Delete(stalePath);

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs selection enumeration probe",
                0x80000000, 0, 0, 640, 420, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out absolutePidl, 0, out _));
            Check(SHBindToObject(0, absolutePidl, 0, in ShellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));

            host = new StreamViewHost(parent, []);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 620, Bottom = 400 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            created = true;
            Check(Marshal.QueryInterface(view, in FolderViewId, out folderView));
            foreach (var index in TargetIndexes)
            {
                var path = Path.Combine(fixture, $"item-{index:D5}.txt");
                Check(SHParseDisplayName(path, 0, out var itemPidl, 0, out _));
                try
                {
                    var child = ILClone(ILFindLastID(itemPidl));
                    if (child == 0) throw new Exception($"Could not retain child PIDL for fixture index {index}.");
                    childPidls.Add(child);
                }
                finally { Marshal.FreeCoTaskMem(itemPidl); }
            }

            var firstAvailableCounts = Enumerable.Repeat(-1, childPidls.Count).ToArray();
            var firstAvailableElapsed = new double[childPidls.Count];
            var firstAttributeSuccessCounts = Enumerable.Repeat(-1, childPidls.Count).ToArray();
            var firstAttributeSuccessElapsed = new double[childPidls.Count];
            var firstDisplayNameSuccessCounts = Enumerable.Repeat(-1, childPidls.Count).ToArray();
            var firstDisplayNameSuccessElapsed = new double[childPidls.Count];
            var startedAt = Stopwatch.GetTimestamp();
            var deadline = Environment.TickCount64 + 30_000;
            var lastCount = -1;
            var countTransitions = 0;
            var staleAttributeFirst = (HResult: int.MinValue, Flags: 0u);
            var staleAttributeLast = staleAttributeFirst;
            var staleDisplayNameFirst = int.MinValue;
            var staleDisplayNameLast = int.MinValue;
            while (Environment.TickCount64 < deadline)
            {
                PumpMessages();
                var count = GetItemCount(folderView, 2);
                if (count != lastCount)
                {
                    countTransitions++;
                    lastCount = count;
                }
                for (var index = 0; index < childPidls.Count; index++)
                {
                    if (firstAvailableCounts[index] >= 0) continue;
                    var hr = GetItemPosition(folderView, childPidls[index], out _);
                    if (hr >= 0)
                    {
                        firstAvailableCounts[index] = count;
                        firstAvailableElapsed[index] = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    }
                    var attributes = GetValidatedAttributes(folder, childPidls[index]);
                    if (attributes.HResult >= 0 && firstAttributeSuccessCounts[index] < 0)
                    {
                        firstAttributeSuccessCounts[index] = count;
                        firstAttributeSuccessElapsed[index] = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    }
                    var displayNameHResult = GetDisplayName(folder, childPidls[index]);
                    if (displayNameHResult >= 0 && firstDisplayNameSuccessCounts[index] < 0)
                    {
                        firstDisplayNameSuccessCounts[index] = count;
                        firstDisplayNameSuccessElapsed[index] = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    }
                }
                staleAttributeLast = GetValidatedAttributes(folder, staleChildPidl);
                if (staleAttributeFirst.HResult == int.MinValue) staleAttributeFirst = staleAttributeLast;
                staleDisplayNameLast = GetDisplayName(folder, staleChildPidl);
                if (staleDisplayNameFirst == int.MinValue) staleDisplayNameFirst = staleDisplayNameLast;
                if (count == FixtureCount) break;
                Thread.Sleep(10);
            }
            if (GetItemCount(folderView, 2) != FixtureCount)
                throw new TimeoutException("The Shell view did not finish enumerating the fixture folder.");
            for (var index = 0; index < childPidls.Count; index++)
                if (firstAvailableCounts[index] < 0)
                    throw new Exception($"Existing target PIDL {TargetIndexes[index]} never became locatable.");
            if (firstAttributeSuccessCounts.Any(count => count < 0))
                throw new Exception("The parent Shell folder never validated every existing target PIDL.");
            if (firstDisplayNameSuccessCounts.Any(count => count < 0))
                throw new Exception("The parent Shell folder never resolved every existing target display name.");

            if (childPidls.Count != TargetIndexes.Length)
                throw new Exception("Not all target item identities were created.");
            if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Hidden/foreground boundary changed.");

            Console.WriteLine("ishellview-selection-enumeration: " +
                $"items={FixtureCount}; count-transitions={countTransitions}; " +
                $"target-pidl-first-visible-counts={string.Join(',', firstAvailableCounts)}; " +
                $"target-pidl-first-visible-ms={string.Join(',', firstAvailableElapsed.Select(value => value.ToString("F1")))}; " +
                $"target-pidl-first-attribute-success-counts={string.Join(',', firstAttributeSuccessCounts)}; " +
                $"target-pidl-first-attribute-success-ms={string.Join(',', firstAttributeSuccessElapsed.Select(value => value.ToString("F1")))}; " +
                $"stale-pidl-validated-attributes=first-{staleAttributeFirst.HResult:X8}/{staleAttributeFirst.Flags:X8},last-{staleAttributeLast.HResult:X8}/{staleAttributeLast.Flags:X8}; " +
                $"target-pidl-first-display-name-success-counts={string.Join(',', firstDisplayNameSuccessCounts)}; " +
                $"target-pidl-first-display-name-success-ms={string.Join(',', firstDisplayNameSuccessElapsed.Select(value => value.ToString("F1")))}; " +
                $"stale-pidl-display-name=first-{staleDisplayNameFirst:X8},last-{staleDisplayNameLast:X8}; " +
                "parent-visible=False; foreground-unchanged=True");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ishellview-selection-enumeration failure: " + ex.Message);
            return 1;
        }
        finally
        {
            if (staleChildPidl != 0) Marshal.FreeCoTaskMem(staleChildPidl);
            foreach (var pidl in childPidls) Marshal.FreeCoTaskMem(pidl);
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (created)
                {
                    Check(At<DestroyView>(view, 10)(view));
                    if (IsWindow(viewWindow)) throw new Exception("Native view cleanup left a live window.");
                }
            }
            finally
            {
                if (view != 0) Marshal.Release(view);
                if (hostPointer != 0) Marshal.Release(hostPointer);
                if (host is not null) host.View = 0;
                host?.Dispose();
                GC.KeepAlive(host);
                if (folder != 0) Marshal.Release(folder);
                if (absolutePidl != 0) Marshal.FreeCoTaskMem(absolutePidl);
                if (parent != 0 && (!DestroyWindow(parent) || IsWindow(parent)))
                    Console.WriteLine("selection enumeration cleanup: hidden parent remains alive.");
                var fullFixture = Path.GetFullPath(fixture);
                var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullFixture))
                    Directory.Delete(fullFixture, recursive: true);
            }
        }
    }

    internal static int RunRestoreDuringRefresh(bool omitLastTarget = false, bool unselectedFocus = false)
    {
        var foreground = GetForegroundWindow();
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fixture = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        var targetFixture = fixture + "-target";
        var expectedTargetCount = FixtureCount - (omitLastTarget ? 1 : 0);
        var selectedTargetCount = TargetIndexes.Length - (unselectedFocus ? 1 : 0);
        var expectedSelectionCount = selectedTargetCount - (omitLastTarget ? 1 : 0);
        var expectedReadiness = new string('1', selectedTargetCount - (omitLastTarget ? 1 : 0)) +
            (omitLastTarget ? "0" : string.Empty);
        nint parent = 0, absolutePidl = 0, folder = 0, view = 0, hostPointer = 0, viewWindow = 0, folderView = 0;
        nint targetAbsolutePidl = 0, targetFolder = 0, targetView = 0, targetHostPointer = 0, targetViewWindow = 0, targetFolderView = 0;
        var created = false;
        var targetCreated = false;
        StreamViewHost? host = null;
        StreamViewHost? targetHost = null;
        var childPidls = new List<nint>();
        ShellViewSelectionSnapshot? snapshot = null;
        try
        {
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(targetFixture);
            foreach (var index in TargetIndexes)
                File.WriteAllText(Path.Combine(fixture, $"item-{index:D5}.txt"), "source selection fixture");
            for (var index = 0; index < FixtureCount; index++)
            {
                if (omitLastTarget && index == TargetIndexes[^1]) continue;
                File.WriteAllText(Path.Combine(targetFixture, $"item-{index:D5}.txt"), "target enumeration fixture");
            }

            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs selection restore enumeration probe",
                0x80000000, 0, 0, 640, 420, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            Check(SHParseDisplayName(fixture, 0, out absolutePidl, 0, out _));
            Check(SHBindToObject(0, absolutePidl, 0, in ShellFolderId, out folder));
            var shellViewId = ShellViewId;
            Check(At<CreateViewObject>(folder, 8)(folder, parent, ref shellViewId, out view));
            host = new StreamViewHost(parent, []);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 620, Bottom = 400 };
            Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            created = true;
            Check(Marshal.QueryInterface(view, in FolderViewId, out folderView));
            PumpUntil(() => GetItemCount(folderView, 2) == TargetIndexes.Length);

            foreach (var index in TargetIndexes)
            {
                var path = Path.Combine(fixture, $"item-{index:D5}.txt");
                Check(SHParseDisplayName(path, 0, out var itemPidl, 0, out _));
                try
                {
                    var child = ILClone(ILFindLastID(itemPidl));
                    if (child == 0) throw new Exception($"Could not retain child PIDL for fixture index {index}.");
                    childPidls.Add(child);
                }
                finally { Marshal.FreeCoTaskMem(itemPidl); }
            }

            var firstSelectedIndex = unselectedFocus ? 1 : 0;
            Check(At<SelectItem>(folderView, 15)(folderView, firstSelectedIndex, 0x1u | 0x4u | 0x10u | 0x40000000u));
            if (unselectedFocus)
                Check(At<SelectItem>(folderView, 15)(folderView, 0, 0x10u | 0x40000000u));
            for (var index = firstSelectedIndex + 1; index < TargetIndexes.Length; index++)
                Check(At<SelectItem>(folderView, 15)(folderView, index, 0x1u | 0x40000000u));
            Pump();
            snapshot = ShellViewSelectionSnapshot.Capture(folderView)
                ?? throw new Exception("Could not capture the selection/focus snapshot.");
            if (snapshot.SelectedCount != selectedTargetCount || !snapshot.HasFocusedItem ||
                snapshot.FocusedItemWasSelected == unselectedFocus)
                throw new Exception($"Unexpected source selection/focus snapshot: selected={snapshot.SelectedCount},focused={snapshot.HasFocusedItem},focused-selected={snapshot.FocusedItemWasSelected}.");

            Check(SHParseDisplayName(targetFixture, 0, out targetAbsolutePidl, 0, out _));
            Check(SHBindToObject(0, targetAbsolutePidl, 0, in ShellFolderId, out targetFolder));
            Check(At<CreateViewObject>(targetFolder, 8)(targetFolder, parent, ref shellViewId, out targetView));
            targetHost = new StreamViewHost(parent, []);
            var targetBrowserSite = new ShellBrowserSite();
            targetHost.BrowserSite = targetBrowserSite;
            targetHost.View = targetView;
            targetHostPointer = Marshal.GetComInterfaceForObject(targetHost, typeof(IStreamProbeBrowser));
            Check(At<CreateViewWindow>(targetView, 9)(targetView, 0, ref settings, targetHostPointer, ref bounds, out targetViewWindow));
            targetCreated = true;
            Check(Marshal.QueryInterface(targetView, in FolderViewId, out targetFolderView));
            var activationResult = At<ActivateView>(targetView, 7)(targetView, 1); // SVUIA_ACTIVATE_NOFOCUS; hidden probe only.
            var startedAt = Stopwatch.GetTimestamp();
            var deadline = Environment.TickCount64 + 30_000;
            string? previousReadiness = null;
            var selectionRestored = false;
            var focusRestored = false;
            var readinessHistory = new List<string>();
            var restoreOutcomes = new List<string>();
            var restoreAttempts = 0;
            var readinessQueryTotal = 0;
            var finalAttempt = false;
            var lastFocusAttempt = 0L;
            var lastSelectionAttempt = 0L;
            var lastSelectionVersion = targetBrowserSite.SelectionVersion;
            var retrySelectionForShellChange = false;
            var finalReadbackSelectedCount = -1;
            var focusTrace = new List<string>();
            var immediateCount = GetItemCount(targetFolderView, 2);
            while (Environment.TickCount64 < deadline)
            {
                PumpMessages();
                var count = GetItemCount(targetFolderView, 2);
                if (ShellSelectionRestoreGate.ShouldDeferSelectionRestore(hasSelection: true, count))
                {
                    Thread.Sleep(10);
                    continue;
                }

                var selectionVersion = targetBrowserSite.SelectionVersion;
                if (selectionVersion != lastSelectionVersion)
                {
                    lastSelectionVersion = selectionVersion;
                    previousReadiness = ShellSelectionRestoreGate.ReadinessForSelectionVersion(
                        previousReadiness, selectionVersionChanged: true);
                    selectionRestored = false;
                    retrySelectionForShellChange = true;
                    continue;
                }

                var readiness = snapshot.ReadTargetReadiness(targetView, previousReadiness, out var queries);
                readinessQueryTotal += queries;
                var changed = readiness is null || previousReadiness is null ||
                    !string.Equals(readiness, previousReadiness, StringComparison.Ordinal);
                if (readiness is not null && (readinessHistory.Count == 0 || readinessHistory[^1] != readiness))
                    readinessHistory.Add(readiness);
                previousReadiness = readiness;

                if (targetBrowserSite.SelectionVersion != selectionVersion)
                {
                    lastSelectionVersion = targetBrowserSite.SelectionVersion;
                    previousReadiness = null;
                    selectionRestored = false;
                    retrySelectionForShellChange = true;
                    continue;
                }

                var retryIntervalElapsed = ShellSelectionRestoreGate.ShouldRetryStableReadiness(
                    selectionRestored, readiness, lastSelectionAttempt != 0 &&
                        Stopwatch.GetElapsedTime(lastSelectionAttempt) >= TimeSpan.FromMilliseconds(100));
                if (ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(selectionRestored, changed,
                        selectionVersionChanged: retrySelectionForShellChange, finalAttempt: false,
                        retryIntervalElapsed: retryIntervalElapsed))
                {
                    retrySelectionForShellChange = false;
                    restoreAttempts++;
                    var result = snapshot.Restore(targetView, focusTrace.Add, allowSelectedFocusFallback: false);
                    selectionRestored = result.SelectionRestored;
                    focusRestored = result.FocusRestored;
                    lastSelectionAttempt = Stopwatch.GetTimestamp();
                    using var attemptReadback = ShellViewSelectionSnapshot.Capture(targetFolderView);
                    restoreOutcomes.Add($"selection:{readiness ?? "unavailable"}:{result.SelectionRestored}/{result.FocusRestored}/selected={attemptReadback?.SelectedCount ?? -1}/version={targetBrowserSite.SelectionVersion}@{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
                }
                else if (selectionRestored && !focusRestored && snapshot.HasFocusedItem &&
                    (lastFocusAttempt == 0 || Stopwatch.GetElapsedTime(lastFocusAttempt) >= TimeSpan.FromMilliseconds(100)))
                {
                    restoreAttempts++;
                    lastFocusAttempt = Stopwatch.GetTimestamp();
                    var result = snapshot.Restore(targetView, focusTrace.Add, allowSelectedFocusFallback: false,
                        restoreSelection: false, restoreFocus: true);
                    focusRestored = result.FocusRestored;
                    restoreOutcomes.Add($"focus:{result.SelectionRestored}/{result.FocusRestored}@{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
                }

                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                if (!omitLastTarget && selectionRestored && readiness == expectedReadiness &&
                    count == expectedTargetCount)
                    break;

                if (!finalAttempt && elapsed >= TimeSpan.FromMilliseconds(1500))
                {
                    finalAttempt = true;
                    restoreAttempts++;
                    var result = snapshot.Restore(targetView, focusTrace.Add, allowSelectedFocusFallback: true);
                    selectionRestored = result.SelectionRestored;
                    focusRestored = result.FocusRestored;
                    using var attemptReadback = ShellViewSelectionSnapshot.Capture(targetFolderView);
                    finalReadbackSelectedCount = attemptReadback?.SelectedCount ?? -1;
                    restoreOutcomes.Add($"final:{result.SelectionRestored}/{result.FocusRestored}/selected={attemptReadback?.SelectedCount ?? -1}@{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
                }
                if (finalAttempt && count == expectedTargetCount &&
                    (omitLastTarget ? finalReadbackSelectedCount == expectedSelectionCount : selectionRestored)) break;
                Thread.Sleep(10);
            }

            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            using var readback = ShellViewSelectionSnapshot.Capture(targetFolderView)
                ?? throw new Exception("Could not capture the final Shell selection.");
            var focusedTargetRestored = IsFocusedItem(targetFolderView, childPidls[0]);
            var focusedTargetSelectionRestored = readback.FocusedItemWasSelected != unselectedFocus;
            if (GetItemCount(targetFolderView, 2) != expectedTargetCount ||
                (omitLastTarget
                    ? readback.SelectedCount != expectedSelectionCount || finalReadbackSelectedCount != expectedSelectionCount ||
                        !readinessHistory.Contains(expectedReadiness, StringComparer.Ordinal) || !focusRestored
                    : !selectionRestored || readback.SelectedCount != expectedSelectionCount || !focusRestored) ||
                !focusedTargetRestored || !focusedTargetSelectionRestored)
                throw new Exception($"Fresh-view restore did not recover the expected selection/focus: immediate-count={immediateCount}, final-count={GetItemCount(targetFolderView, 2)}, restored={selectionRestored}/{focusRestored}, selected={readback.SelectedCount}, focused-target={focusedTargetRestored}, focused-selected={readback.FocusedItemWasSelected}, attempts={restoreAttempts}, readiness={string.Join(',', readinessHistory)}.");
            if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Hidden/foreground boundary changed.");

            Console.WriteLine("ishellview-selection-enumeration-restore: " +
                $"scenario={(unselectedFocus ? "stale-unselected-focus" : omitLastTarget ? "stale-target" : "all-present")}; items={expectedTargetCount}; targets={TargetIndexes.Length}; expected-selected={expectedSelectionCount}; readiness={string.Join(',', readinessHistory)}; " +
                $"restore-attempts={restoreAttempts}; readiness-queries={readinessQueryTotal}; outcomes={string.Join(',', restoreOutcomes)}; " +
                $"focus-trace={string.Join(',', focusTrace.Where(line => line.StartsWith("focused-item-", StringComparison.Ordinal) || line.StartsWith("restore-result", StringComparison.Ordinal)).TakeLast(12))}; " +
                $"immediate-count={immediateCount}; elapsed-ms={elapsedMs:F1}; restored={selectionRestored}/{focusRestored}; selected={readback.SelectedCount}; focused-target={focusedTargetRestored}; focused-selected={readback.FocusedItemWasSelected}; shell-state={targetBrowserSite.NotificationSnapshot}; " +
                $"activate-no-focus=0x{activationResult:X8}; parent-visible=False; foreground-unchanged=True");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ishellview-selection-enumeration-restore failure: " + ex.Message);
            return 1;
        }
        finally
        {
            snapshot?.Dispose();
            foreach (var pidl in childPidls) Marshal.FreeCoTaskMem(pidl);
            if (targetFolderView != 0) Marshal.Release(targetFolderView);
            if (folderView != 0) Marshal.Release(folderView);
            try
            {
                if (targetCreated)
                {
                    Check(At<DestroyView>(targetView, 10)(targetView));
                    if (IsWindow(targetViewWindow)) throw new Exception("Fresh native view cleanup left a live window.");
                }
                if (created)
                {
                    Check(At<DestroyView>(view, 10)(view));
                    if (IsWindow(viewWindow)) throw new Exception("Native view cleanup left a live window.");
                }
            }
            finally
            {
                if (targetView != 0) Marshal.Release(targetView);
                if (targetHostPointer != 0) Marshal.Release(targetHostPointer);
                if (targetHost is not null) targetHost.View = 0;
                targetHost?.Dispose();
                GC.KeepAlive(targetHost);
                if (targetFolder != 0) Marshal.Release(targetFolder);
                if (targetAbsolutePidl != 0) Marshal.FreeCoTaskMem(targetAbsolutePidl);
                if (view != 0) Marshal.Release(view);
                if (hostPointer != 0) Marshal.Release(hostPointer);
                if (host is not null) host.View = 0;
                host?.Dispose();
                GC.KeepAlive(host);
                if (folder != 0) Marshal.Release(folder);
                if (absolutePidl != 0) Marshal.FreeCoTaskMem(absolutePidl);
                if (parent != 0 && (!DestroyWindow(parent) || IsWindow(parent)))
                    Console.WriteLine("selection restore enumeration cleanup: hidden parent remains alive.");
                var fullFixture = Path.GetFullPath(fixture);
                var prefix = fixturesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullFixture))
                    Directory.Delete(fullFixture, recursive: true);
                var fullTargetFixture = Path.GetFullPath(targetFixture);
                if (fullTargetFixture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullTargetFixture))
                    Directory.Delete(fullTargetFixture, recursive: true);
            }
        }
    }

    private static int GetItemCount(nint folderView, uint scope) =>
        At<ItemCount>(folderView, 7)(folderView, scope, out var count) >= 0 ? count : -1;

    private static int GetItemPosition(nint folderView, nint childPidl, out Point point) =>
        At<GetItemPositionCall>(folderView, 11)(folderView, childPidl, out point);

    private static bool IsFocusedItem(nint folderView, nint expectedChildPidl)
    {
        if (At<FocusedItem>(folderView, 10)(folderView, out var index) < 0 || index < 0) return false;
        var result = At<GetViewItem>(folderView, 6)(folderView, index, out var item);
        if (result < 0 || item == 0)
        {
            if (item != 0) Marshal.FreeCoTaskMem(item);
            return false;
        }
        try { return ILIsEqual(item, expectedChildPidl); }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    private static (int HResult, uint Flags) GetValidatedAttributes(nint shellFolder, nint childPidl)
    {
        var itemArray = Marshal.AllocHGlobal(nint.Size);
        try
        {
            Marshal.WriteIntPtr(itemArray, childPidl);
            uint attributes = 0x41000000; // SFGAO_VALIDATE | SFGAO_FILESYSTEM
            var hr = At<GetAttributesOf>(shellFolder, 9)(shellFolder, 1, itemArray, ref attributes);
            return (hr, attributes);
        }
        finally { Marshal.FreeHGlobal(itemArray); }
    }

    private static int GetDisplayName(nint shellFolder, nint childPidl)
    {
        var result = At<GetDisplayNameOf>(shellFolder, 11)(shellFolder, childPidl, 0x8001, out var displayName);
        if (result < 0) return result;
        var buffer = new System.Text.StringBuilder(1024);
        return StrRetToBufW(ref displayName, childPidl, buffer, buffer.Capacity);
    }

    private static void Pump()
    {
        for (var index = 0; index < 4; index++)
        {
            PumpMessages();
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 30_000;
        while (Environment.TickCount64 < deadline)
        {
            PumpMessages();
            if (condition()) return;
            Thread.Sleep(10);
        }
        throw new TimeoutException("The Shell view did not finish enumerating the fixture folder.");
    }

    private static void PumpMessages()
    {
        while (PeekMessageW(out var message, 0, 0, 0, 1))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
        Application.DoEvents();
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct StrRet { public uint Type; public nint Value; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings, nint browser, ref StreamProbeRect rect, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemPositionCall(nint self, nint item, out Point point);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FocusedItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewItem(nint self, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ActivateView(nint self, uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributesOf(nint self, uint count, nint items, ref uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplayNameOf(nint self, nint item, uint flags, out StrRet name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint pidl1, nint pidl2);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrRetToBufW(ref StrRet name, nint item, System.Text.StringBuilder buffer, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
}
