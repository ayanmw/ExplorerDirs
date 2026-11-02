using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

// Runs the actual production HwndHost in a never-shown WPF source.
// Synthetic messages and child focus stay within this test host; it never uses desktop input or the clipboard.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 6 && args[0] == "--shell-provider-session-child" &&
            Guid.TryParse(args[3], out var sessionChildClassId) &&
            bool.TryParse(args[5], out var sessionChildIgnoreFolderSort))
            return ShellProviderLocationProbe.RunSessionRecoveryChild(args[1], args[2], sessionChildClassId,
                args[4], sessionChildIgnoreFolderSort);
        if (args.Length == 5 && args[0] == "--shell-provider-enumeration-child" &&
            Guid.TryParse(args[1], out var enumerationClassId))
            return ShellProviderEnumerationChildProbe.Run(enumerationClassId, args[2], args[3], args[4]);
        if (args is ["--column-header-focus"]) return ColumnHeaderFocusProbe.Run();
        if (args is ["--shell-provider-location"]) return ShellProviderLocationProbe.Run();
        if (args is ["--profile-owner-shell-host"]) return ProfileOwnerShellHostProbe.Run();
        if (args is ["--profile-owner-multi-root-shell-host"]) return ProfileOwnerMultiRootShellHostProbe.Run();
        if (args is ["--profile-owner-multi-root-shell-host-large"]) return ProfileOwnerMultiRootShellHostProbe.Run(large: true);
        if (args is ["--profile-owner-multi-root-shell-host-cache-near-boundary"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1000);
        if (args is ["--profile-owner-multi-root-shell-host-cache-boundary"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1024, cacheCapacity: 4096);
        if (args is ["--profile-owner-multi-root-shell-host-cache-boundary-product"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1024, diagnosticAdapter: false);
        if (args is ["--profile-owner-multi-root-shell-host-cache-overflow"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1025, cacheCapacity: 4096);
        if (args is ["--profile-owner-multi-root-shell-host-cache-overflow-8192"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1025, cacheCapacity: 8192);
        if (args is ["--profile-owner-multi-root-shell-host-cache-overflow-8192-diagnostic-observer-on"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1025, cacheCapacity: 8192, captureNotificationIdentities: true);
        if (args is ["--profile-owner-multi-root-shell-host-cache-overflow-product"]) return ProfileOwnerMultiRootShellHostProbe.Run(folderCount: 1025, diagnosticAdapter: false);
        if (args is ["--profile-owner-session-recovery"]) return ProfileOwnerSessionRecoveryProbe.RunParent();
        if (args is ["--profile-owner-session-recovery-child", var sessionPath, var profile])
            return ProfileOwnerSessionRecoveryProbe.RunChild(sessionPath, profile);
        if (args is ["--shell-provider-product-async"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync();
        if (args is ["--shell-provider-product-async-large-sort-readiness"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("large-readiness");
        if (args is ["--shell-provider-product-async-single-sort"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("single-sort-verb");
        if (args is ["--shell-provider-product-async-set-sort-columns"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("set-sort-columns");
        if (args is ["--shell-provider-product-async-header-click"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("header-click");
        if (args is ["--shell-provider-product-async-notification-burst"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-burst");
        if (args is ["--shell-provider-product-async-notification-burst-flush"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-burst-flush");
        if (args is ["--shell-provider-product-async-notification-burst-spaced"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-burst-spaced");
        if (args is ["--shell-provider-product-async-notification-updatedir"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-updatedir");
        if (args is ["--shell-provider-product-async-notification-updatedir-large"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-updatedir-large");
        if (args is ["--shell-provider-product-async-notification-updatedir-sort"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-updatedir-sort");
        if (args is ["--shell-provider-product-async-notification-updatedir-paired-sort"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-updatedir-paired-sort");
        if (args is ["--shell-provider-product-async-notification-updatedir-paired-sort-redraw"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-updatedir-paired-sort-redraw");
        if (args is ["--shell-provider-product-async-notification-items-flush"])
            return ShellProviderLocationProbe.RunProductAdapterOnDemandAsync("notification-items-flush");
        if (args is ["--shell-provider-location-no-desktop-namespace"])
            return ShellProviderLocationProbe.Run(registerDesktopNamespace: false);
        if (args is ["--shell-provider-location-descending"]) return ShellProviderLocationProbe.Run(descending: true);
        if (args is ["--shell-provider-location-ignore-folder-size"])
            return ShellProviderLocationProbe.Run(ignoreFolderSizeForSort: true);
        if (args is ["--shell-provider-location-ignore-folder-size-descending"])
            return ShellProviderLocationProbe.Run(descending: true, ignoreFolderSizeForSort: true);
        if (args is ["--system-theme"]) return SystemThemeProbe.Run();
        if (args is ["--native-theme-colors"]) return NativeThemeColorProbe.Run();
        if (args is ["--folder-navigation-callbacks"]) return FolderNavigationCallbacksProbe.Run();
        if (args is ["--folder-navigation-provider-callbacks", var callbackAdapter])
            return FolderNavigationCallbacksProbe.RunProvider(callbackAdapter);
        if (args is ["--pane-tab-transfer"]) return PaneTabTransferProbe.Run();
        if (args is ["--tab-host-lifetime"]) return TabHostLifetimeProbe.Run();
        if (args is ["--pane-tab-copy-order"]) return PaneTabTransferProbe.Run(unhostedCopyOrder: true);
        if (args is ["--window-close-lifetime"]) return WindowCloseLifetimeProbe.Run();
        if (args is ["--address-editing"]) return AddressEditingProbe.Run();
        if (args is ["--navigation-focus-requests"]) return NavigationFocusRequestProbe.Run();
        if (args is ["--shell-update-notification-smoke"])
            return ShellUpdateNotificationProbe.Run();
        if (args is ["--shell-change-registration-stress"])
            return ShellUpdateNotificationProbe.RunRegistrationLifecycleStress();
        if (args is ["--shell-change-registration-once"])
            return ShellUpdateNotificationProbe.RunRegistrationLifecycleStress(1);
        if (args is ["--shell-change-same-process-delivery-stress"])
            return ShellUpdateNotificationProbe.RunSameProcessDeliveryStress();
        if (args.Length == 4 && args[0] == "--shell-change-same-process-delivery-stress" &&
            int.TryParse(args[1], out var deliveryCycles) && deliveryCycles is >= 1 and <= 40 &&
            int.TryParse(args[2], out var deliverySetup) && deliverySetup is >= 0 and <= 5000 &&
            int.TryParse(args[3], out var deliverySettle) && deliverySettle is >= 0 and <= 5000)
            return ShellUpdateNotificationProbe.RunSameProcessDeliveryStress(deliveryCycles, deliverySetup, deliverySettle);
        if (args.Length == 5 && args[0] == "--shell-change-same-process-delivery-stress" &&
            int.TryParse(args[1], out var abCycles) && abCycles is >= 2 and <= 40 && abCycles % 2 == 0 &&
            int.TryParse(args[2], out var abSetup) && abSetup is >= 0 and <= 5000 &&
            int.TryParse(args[3], out var settleA) && settleA is >= 0 and <= 5000 &&
            int.TryParse(args[4], out var settleB) && settleB is >= 0 and <= 5000)
            return ShellUpdateNotificationProbe.RunSameProcessDeliveryStress(abCycles, abSetup, settleA, settleB);
        if (args.Length == 4 && args[0] == "--shell-change-same-process-fresh-window-delivery-stress" &&
            int.TryParse(args[1], out var windowCycles) && windowCycles is >= 1 and <= 40 &&
            int.TryParse(args[2], out var windowSetup) && windowSetup is >= 0 and <= 5000 &&
            int.TryParse(args[3], out var windowSettle) && windowSettle is >= 0 and <= 5000)
            return ShellUpdateNotificationProbe.RunSameProcessDeliveryStress(windowCycles, windowSetup, windowSettle,
                freshWindowPerCycle: true);
        if (args.Length == 5 && args[0] == "--shell-change-same-process-fresh-window-delivery-stress" &&
            int.TryParse(args[1], out var alternatingWindowCycles) && alternatingWindowCycles is >= 2 and <= 40 && alternatingWindowCycles % 2 == 0 &&
            int.TryParse(args[2], out var alternatingWindowSetup) && alternatingWindowSetup is >= 0 and <= 5000 &&
            int.TryParse(args[3], out var alternatingWindowSettleA) && alternatingWindowSettleA is >= 0 and <= 5000 &&
            int.TryParse(args[4], out var alternatingWindowSettleB) && alternatingWindowSettleB is >= 0 and <= 5000)
            return ShellUpdateNotificationProbe.RunSameProcessDeliveryStress(alternatingWindowCycles,
                alternatingWindowSetup, alternatingWindowSettleA, alternatingWindowSettleB, freshWindowPerCycle: true);
        if (args.Length == 2 && args[0] == "--shell-change-same-registration-delivery-control" &&
            int.TryParse(args[1], out var notificationCount) && notificationCount is >= 1 and <= 40)
            return ShellUpdateNotificationProbe.RunSameRegistrationDeliveryControl(notificationCount);
        if (args.Length == 3 && args[0] == "--shell-change-same-registration-delivery-control" &&
            int.TryParse(args[1], out var spacedNotificationCount) && spacedNotificationCount is >= 1 and <= 40 &&
            int.TryParse(args[2], out var notificationSpacing) && notificationSpacing is >= 0 and <= 5000)
            return ShellUpdateNotificationProbe.RunSameRegistrationDeliveryControl(spacedNotificationCount, 150, notificationSpacing);
        if (args is ["--shell-change-registration-fresh-window-stress"])
            return ShellUpdateNotificationProbe.RunFreshWindowRegistrationLifecycleStress();
        if (args.Length == 2 && args[0] == "--shell-change-registration-fresh-window-stress" &&
            int.TryParse(args[1], out var settleMilliseconds) && settleMilliseconds is >= 0 and <= 1000)
            return ShellUpdateNotificationProbe.RunFreshWindowRegistrationLifecycleStress(settleMilliseconds: settleMilliseconds);
        if (args is ["--foreground-guard-smoke"])
            return ForegroundGuardSmoke.Run();
        if (args is ["--message-pump-hook-smoke"])
        {
            try
            {
                if (!ThreadMessagePumpTraceProbe.RunHookSmoke()) throw new Exception("Message pump hook smoke failed; see diagnostics above.");
                Console.WriteLine("message-pump-hook-smoke PASS");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args is ["--dispatcher-queue-latency-smoke"])
        {
            try
            {
                if (!DispatcherQueueLatencyProbe.RunSmoke())
                    throw new InvalidOperationException("Dispatcher queue latency probe smoke did not observe every priority.");
                Console.WriteLine("dispatcher-queue-latency-smoke PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
        if (args is ["--shell-panes"]) args = ["--preview-pane"];
        if (args is ["--shell-tree-key-routing"])
        {
            try { return ShellTreeKeyboardProbe.Run(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args is ["--shell-tree-icons"])
        {
            try { return ShellTreeIconHostProbe.Run(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args is ["--quick-filter-tabs"])
        {
            try { return QuickFilterTabStateProbe.Run(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Length is 1 or 2 && args[0] == "--watcher-overflow-probe")
        {
            var requestedCount = args.Length == 2 && int.TryParse(args[1], out var parsedCount) ? parsedCount : 50000;
            return RunWatcherOverflowProbe(requestedCount);
        }
        if (args is ["--environment-refresh-hook"]) return EnvironmentRefreshProbe.Run();
        if (args.Length > 0 && args[0] == "--fixture-shell-operation")
        {
            try { return ProcessFixtureOperations.RunChild(args); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        }
        if (args.Length == 0 || args[0] is not ("--preview-pane" or "--cross-pane-drop-effects" or "--cross-pane-drop-multi" or "--cross-pane-drop" or "--live-updates" or "--live-updates-without-subclass" or "--live-shell-operations" or "--live-shell-independent-sta" or "--live-shell-child-process" or "--search-navigation" or "--quick-filter" or "--column-value-filter" or "--file-view-export" or "--selected-paths-clipboard" or "--file-command-states" or "--file-command-states-early" or "--ctrl-wheel-routing" or "--ctrl-wheel-window-chain" or "--auto-column-width" or "--rename-editor-key-routing" or "--live-shell-rename-editor" or "--live-shell-rename-editor-mode" or "--live-shell-rename-editor-pane-0" or "--live-shell-rename-editor-pane-1" or "--live-shell-rename-editor-pane-2" or "--live-shell-rename-editor-pane-3" or "--live-shell-hwnd-tree" or "--live-shell-visibility-updates" or "--live-shell-visibility-updates-pane-0" or "--live-shell-visibility-updates-pane-1" or "--live-shell-visibility-updates-pane-2" or "--live-shell-visibility-updates-pane-3" or "--live-shell-product-monitor" or "--live-shell-product-monitor-no-parent-observer" or "--live-shell-changed-metadata" or "--live-shell-watcher-recovery" or "--live-shell-product-monitor-early-updatedir" or "--live-shell-product-monitor-early-create" or "--live-shell-product-monitor-early-none" or "--live-shell-product-monitor-early-mkdir" or "--live-shell-product-monitor-early-mkdir-none" or "--live-shell-product-monitor-early-rmdir" or "--live-shell-product-monitor-early-rmdir-none" or "--live-shell-product-monitor-rmdir-boundary" or "--live-shell-product-monitor-early-delete" or "--live-shell-product-monitor-early-delete-none" or "--live-shell-cross-volume" or "--live-shell-directory-stress" or "--live-shell-directory-stress-native" or "--live-shell-directory-stress-large" or "--live-shell-selection-delete" or "--live-shell-selection-delete-after-input" or "--live-shell-selection-delete-watcher" or "--live-shell-selection-delete-native" or "--live-shell-selection-user-clear" or "--live-shell-selection-partial-snapshot" or "--live-shell-empty-directory-deferred-restore" or "--live-shell-deferred-restore-user-input" or "--live-shell-deferred-restore-stable-selection" or "--live-shell-deferred-restore-selection-mismatch" or "--live-shell-refresh-deferred-user-input" or "--live-shell-refresh-deferred-mouse" or "--live-shell-refresh-deferred-watcher-overflow" or "--live-shell-selection-large" or "--shell-selection-invert-modes" or "--live-shell-refresh" or "--live-shell-refresh-unselected-focus" or "--live-shell-refresh-unselected" or "--live-shell-refresh-one-pane" or "--live-shell-refresh-one-pane-unselected" or "--live-shell-refresh-one-pane-3" or "--live-shell-refresh-one-pane-3-dual" or "--live-shell-refresh-one-pane-3-single" or "--live-shell-refresh-one-pane-3-single-dual" or "--live-shell-refresh-one-pane-3-single-fallback" or "--live-shell-refresh-one-pane-3-multiselect-fallback" or "--live-shell-refresh-one-pane-unselected-3" or "--live-shell-refresh-one-pane-unselected-3-singleobserver" or "--live-shell-refresh-one-pane-unselected-3-filesystem-control" or "--live-shell-refresh-pane-0" or "--live-shell-refresh-pane-1" or "--live-shell-refresh-pane-2" or "--live-shell-refresh-pane-3" or "--live-shell-delete-selected-pane-0" or "--live-shell-delete-selected-pane-1" or "--live-shell-delete-selected-pane-2" or "--live-shell-delete-selected-pane-3" or "--live-shell-delete-selected-pane-3-fallback" or "--shell-change-registration-after-hosts" or "--probe-error-smoke")) return 2;
        var acceptsLargeSelectionArgument = args.Length == 2 && args[0] == "--live-shell-selection-large" &&
            int.TryParse(args[1], out var requestedSelectionCount) && requestedSelectionCount is >= 4 and <= 10001;
        var acceptsBurstArgument = args.Length == 2 &&
            (args[0] is "--live-shell-directory-stress" or "--live-shell-directory-stress-native" or "--live-shell-directory-stress-large") &&
            int.TryParse(args[1], out _);
        var acceptsRmdirBoundaryArgument = args.Length == 2 && args[0] == "--live-shell-product-monitor-rmdir-boundary" &&
            int.TryParse(args[1], out var requestedRmdirViewCount) && requestedRmdirViewCount is 512 or 513;
        var renameModeIndexes = args.Length == 2 && args[0] == "--live-shell-rename-editor-mode"
            ? args[1].Split(':') : Array.Empty<string>();
        var acceptsRenameEditorModeArgument = renameModeIndexes.Length == 2 &&
            int.TryParse(renameModeIndexes[0], out var requestedRenamePane) && requestedRenamePane is >= 0 and <= 3 &&
            int.TryParse(renameModeIndexes[1], out var requestedRenameStyle) && requestedRenameStyle is >= 0 and <= 7;
        if (args.Length != 1 && !acceptsBurstArgument && !acceptsLargeSelectionArgument && !acceptsRmdirBoundaryArgument &&
            !acceptsRenameEditorModeArgument && (args.Length != 3 || args[1] != "--profile")) return 2;
        var traceShellDiagnostics = args[0] is "--live-updates" or "--live-shell-operations" or "--live-shell-independent-sta" or "--live-shell-child-process" or "--live-shell-changed-metadata" ||
            args[0] is "--live-shell-product-monitor" or "--live-shell-product-monitor-no-parent-observer" or "--live-shell-product-monitor-early-updatedir" or "--live-shell-product-monitor-early-create" or "--live-shell-product-monitor-early-none" or "--live-shell-product-monitor-early-mkdir" or "--live-shell-product-monitor-early-mkdir-none" or "--live-shell-product-monitor-early-rmdir" or "--live-shell-product-monitor-early-rmdir-none" or "--live-shell-product-monitor-rmdir-boundary" or "--live-shell-product-monitor-early-delete" or "--live-shell-product-monitor-early-delete-none" or "--live-shell-cross-volume" or "--live-shell-directory-stress-large" or "--live-shell-selection-delete" or "--live-shell-selection-delete-watcher" or "--live-shell-selection-delete-native" or "--live-shell-selection-delete-after-input" or "--live-shell-selection-user-clear" or "--live-shell-empty-directory-deferred-restore" or "--live-shell-deferred-restore-user-input" or "--live-shell-deferred-restore-stable-selection" or "--live-shell-deferred-restore-selection-mismatch" or "--live-shell-refresh-deferred-user-input" or "--live-shell-refresh-deferred-mouse" or "--live-shell-selection-large" ||
            args[0].StartsWith("--live-shell-refresh", StringComparison.Ordinal) || args[0].StartsWith("--live-shell-delete-selected", StringComparison.Ordinal);
        if (traceShellDiagnostics && Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE") is null)
            Environment.SetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE", "1");
        if (traceShellDiagnostics && Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE") != "0" &&
            Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING") is null)
            Environment.SetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TIMING", "1");
        var assembly = Assembly.Load("ExplorerDirs.App");
        var hostType = assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", true)!;
        var stateType = assembly.GetType("ExplorerDirs.App.ViewState", true)!;
        var state = stateType.GetMethod("Default")!.Invoke(null, new object[] { 4u, -1 });
        var savedViews = new List<object>();
        if (args.Length == 3)
        {
            // Read only: never save, and never navigate to paths from the profile.
            var storeType = assembly.GetType("ExplorerDirs.App.SessionStore", true)!;
            var loaded = storeType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { File.ReadAllText(Path.GetFullPath(args[2])) })
                ?? throw new Exception("Profile could not be parsed.");
            foreach (var pane in (System.Collections.IEnumerable)loaded.GetType().GetProperty("Panes")!.GetValue(loaded)!)
            {
                var tab = ((System.Collections.IEnumerable)pane).Cast<object>().FirstOrDefault();
                savedViews.Add(tab is null ? state! : tab.GetType().GetProperty("View")!.GetValue(tab)!);
            }
            if (savedViews.Count != 4) throw new Exception("Expected four saved pane states.");
        }
        var changedBurstCount = 2;
        if (args[0] == "--live-shell-changed-metadata" &&
            int.TryParse(Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_CHANGED_BURST_COUNT"), out var requestedChangedBurstCount))
        {
            if (requestedChangedBurstCount is < 2 or > 160)
                throw new ArgumentOutOfRangeException(nameof(args), "The Changed metadata burst count must be between 2 and 160.");
            changedBurstCount = requestedChangedBurstCount;
        }
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        var crossVolumeProbe = args[0] == "--live-shell-cross-volume";
        var uncTargetProbe = crossVolumeProbe && Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_UNC_TARGET") == "1";
        var junctionTargetProbe = crossVolumeProbe && Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_JUNCTION_TARGET") == "1";
        string? crossVolumeRoot = null;
        string? crossVolumeCleanupParent = null;
        string? junctionPath = null;
        if (crossVolumeProbe)
        {
            if (junctionTargetProbe)
            {
                crossVolumeCleanupParent = root;
                crossVolumeRoot = Path.Combine(root, "pane-1-target");
                junctionPath = Path.Combine(root, "pane-1-junction");
                try
                {
                    Directory.CreateDirectory(root);
                    Directory.CreateDirectory(crossVolumeRoot);
                    CreateDirectoryJunction(junctionPath, crossVolumeRoot);
                }
                catch
                {
                    if (Directory.Exists(junctionPath) &&
                        (File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint) != 0)
                        Directory.Delete(junctionPath, recursive: false);
                    DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
                    throw;
                }
            }
            else if (uncTargetProbe)
            {
                var sourceVolume = Path.GetPathRoot(root) ?? throw new InvalidOperationException("Probe root has no volume.");
                var shareRoot = $"\\\\localhost\\{sourceVolume[0]}$";
                var relativeRoot = Path.GetRelativePath(sourceVolume, root);
                crossVolumeCleanupParent = Path.Combine(shareRoot, Path.GetDirectoryName(relativeRoot)!);
                crossVolumeRoot = Path.Combine(crossVolumeCleanupParent, "unc-" + Guid.NewGuid().ToString("N"));
                if (!Directory.Exists(crossVolumeCleanupParent))
                    throw new DirectoryNotFoundException("UNC fixture parent is unavailable through the local administrative share.");
            }
            else
            {
                var primaryVolume = Path.GetPathRoot(root);
                var otherVolume = DriveInfo.GetDrives().FirstOrDefault(drive => drive.DriveType == DriveType.Fixed &&
                    drive.IsReady && !string.Equals(drive.RootDirectory.FullName, primaryVolume, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Cross-volume probe requires another ready fixed drive.");
                var tempRoot = Path.GetTempPath();
                if (!string.Equals(Path.GetPathRoot(tempRoot), otherVolume.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                    tempRoot = Path.Combine(otherVolume.RootDirectory.FullName, "Temp");
                crossVolumeCleanupParent = Path.Combine(tempRoot, "ExplorerDirs.HostProbe");
                crossVolumeRoot = Path.Combine(crossVolumeCleanupParent, Guid.NewGuid().ToString("N"));
            }
        }
        var hosts = new List<HwndHost>();
        var folders = new List<string>();
        var largeSelectionFixtureItemCount = args[0] == "--live-shell-selection-large" && args.Length == 2
            ? int.Parse(args[1]) : 10001;
        var foregroundBeforeProbe = GetForegroundWindow();
        var grid = new Grid { Width = 1000, Height = 600 };
        for (var i = 0; i < 2; i++) { grid.RowDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); }
        // Explicitly omit WS_VISIBLE. Never call ShowWindow or Activate.
        using var source = new HwndSource(new HwndSourceParameters("ExplorerDirs hidden production host probe")
        {
            WindowStyle = unchecked((int)0x80000000), Width = 1000, Height = 600
        });
        try
        {
            if (args[0] == "--probe-error-smoke")
                throw new InvalidOperationException("Injected HostProbe failure-boundary smoke.");
            for (var i = 0; i < 4; i++)
            {
                var folderRoot = crossVolumeProbe && i == 1
                    ? junctionTargetProbe ? junctionPath! : crossVolumeRoot!
                    : root;
                var folder = junctionTargetProbe && i == 1 ? folderRoot : Path.Combine(folderRoot, $"pane-{i}");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "initial.txt"), "initial");
                if (i == 0 && args[0] == "--auto-column-width")
                {
                    File.WriteAllText(Path.Combine(folder, "auto-fit-column-width-long-fixture-name.txt"), "long name fixture");
                    File.WriteAllText(Path.Combine(folder, "second-auto-fit-column-width-long-fixture-name.bin"), "second long name fixture");
                }
                if (i == 0 && args[0] == "--live-shell-changed-metadata")
                {
                    File.WriteAllText(Path.Combine(folder, "second.txt"), "second");
                    for (var changedIndex = 2; changedIndex < changedBurstCount; changedIndex++)
                        File.WriteAllText(Path.Combine(folder, $"changed-{changedIndex:D3}.txt"), "x");
                }
                if (i == 0 && args[0] == "--file-view-export")
                    File.WriteAllText(Path.Combine(folder, "unselected.txt"), "unselected");
                if (i == 0 && args[0] == "--selected-paths-clipboard")
                    File.WriteAllText(Path.Combine(folder, "second.txt"), "second");
                if (i == 0 && args[0] is "--cross-pane-drop" or "--cross-pane-drop-multi" or "--cross-pane-drop-effects")
                {
                    if (args[0] == "--cross-pane-drop")
                        File.WriteAllText(Path.Combine(folder, "drop-source.txt"), "native drop fixture");
                    else if (args[0] == "--cross-pane-drop-effects")
                    {
                        File.WriteAllText(Path.Combine(folder, "drop-control.txt"), "control copy fixture");
                        File.WriteAllText(Path.Combine(folder, "drop-shift.txt"), "shift move fixture");
                        File.WriteAllText(Path.Combine(folder, "drop-default.txt"), "default move fixture");
                    }
                    else
                    {
                        File.WriteAllText(Path.Combine(folder, "drop-source-1.txt"), "native drop fixture one");
                        File.WriteAllText(Path.Combine(folder, "drop-source-2.txt"), "native drop fixture two");
                    }
                }
                if (args[0].StartsWith("--live-shell-refresh", StringComparison.Ordinal) || args[0].StartsWith("--live-shell-delete-selected", StringComparison.Ordinal) || args[0] is "--ctrl-wheel-routing" or "--live-shell-product-monitor" or "--live-shell-product-monitor-no-parent-observer" or "--live-shell-watcher-recovery" or "--live-shell-product-monitor-early-updatedir" or "--live-shell-product-monitor-early-create" or "--live-shell-product-monitor-early-none" or "--live-shell-product-monitor-early-mkdir" or "--live-shell-product-monitor-early-mkdir-none" or "--live-shell-product-monitor-early-rmdir" or "--live-shell-product-monitor-early-rmdir-none" or "--live-shell-product-monitor-rmdir-boundary" or "--live-shell-product-monitor-early-delete" or "--live-shell-product-monitor-early-delete-none" or "--live-shell-cross-volume" or "--live-shell-directory-stress" or "--live-shell-directory-stress-native" or "--live-shell-directory-stress-large" or "--live-shell-selection-delete" or "--live-shell-selection-delete-watcher" or "--live-shell-selection-delete-native" or "--live-shell-selection-user-clear" or "--live-shell-selection-partial-snapshot" or "--live-shell-empty-directory-deferred-restore" or "--live-shell-deferred-restore-user-input" or "--live-shell-deferred-restore-stable-selection" or "--live-shell-deferred-restore-selection-mismatch" or "--live-shell-refresh-deferred-user-input" or "--live-shell-selection-delete-after-input" or "--live-shell-selection-large" or "--shell-selection-invert-modes")
                {
                    File.WriteAllText(Path.Combine(folder, "second.txt"), "second");
                    File.WriteAllText(Path.Combine(folder, "third.txt"), "third");
                }
                if (i == 0 && args[0] == "--live-shell-product-monitor-rmdir-boundary")
                {
                    // The RMDIR folder added later makes the stale-delete view count exactly 512 or 513.
                    for (var itemIndex = 0; itemIndex < int.Parse(args[1]) - 4; itemIndex++)
                        File.WriteAllText(Path.Combine(folder, $"rmdir-boundary-{itemIndex:D4}.txt"), "boundary");
                }
                if (i == 0 && (args[0] is "--live-shell-selection-delete" or "--live-shell-selection-delete-after-input" or "--live-shell-selection-delete-watcher" or "--live-shell-selection-delete-native"))
                    File.WriteAllText(Path.Combine(folder, "selection-delete-target.txt"), "initial");
                if (args[0] == "--live-shell-selection-large" && i == 0)
                {
                    // Populate before ExplorerBrowser binds so this stress case measures selection
                    // restore rather than processing thousands of individual Shell create events.
                    for (var itemIndex = 0; itemIndex < largeSelectionFixtureItemCount - 4; itemIndex++)
                        File.WriteAllText(Path.Combine(folder, $"selection-{itemIndex:D4}.txt"), itemIndex.ToString());
                    File.WriteAllText(Path.Combine(folder, "selection-delete-target.txt"), "initial");
                }
                if (args[0] == "--search-navigation")
                    File.WriteAllText(Path.Combine(folder, "second.txt"), "second");
                if (args[0] == "--quick-filter")
                {
                    File.WriteAllText(Path.Combine(folder, "keep.txt"), "keep");
                    File.WriteAllText(Path.Combine(folder, "drop.tmp"), "drop");
                    Directory.CreateDirectory(Path.Combine(folder, "folder.tmp"));
                }
                if (args[0] == "--column-value-filter")
                    File.WriteAllText(Path.Combine(folder, "sample.bin"), "binary fixture");
                folders.Add(folder);
                var initialView = savedViews.Count == 4 ? savedViews[i] :
                    args[0] == "--search-navigation" ? SearchHostProbe.CreateViewState(stateType, i) : state;
                var foregroundBeforeHostCreation = GetForegroundWindow();
                var host = args[0] == "--preview-pane"
                    ? (HwndHost)Activator.CreateInstance(hostType,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        binder: null, args: [folder, initialView, false], culture: null)!
                    : (HwndHost)Activator.CreateInstance(hostType, folder, initialView)!;
                hosts.Add(host);
                HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeHostCreation, GetForegroundWindow(),
                    $"ExplorerBrowserHost construction for pane {i}");
                Grid.SetRow(host, i / 2); Grid.SetColumn(host, i % 2);
                grid.Children.Add(host);
            }
            var browserSites = hosts.Select(host => hostType.GetField("_browserSite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).ToArray();
            var notificationProperty = browserSites[0].GetType().GetProperty("NotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var nativeMessageProperty = hostType.GetProperty("NativeUserMessageSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            string BrowserSiteNotifications() => string.Join('|', browserSites.Select((site, index) =>
            {
                var snapshot = (string)nativeMessageProperty.GetValue(hosts[index])!;
                var samplesAt = snapshot.IndexOf("; samples=", StringComparison.Ordinal);
                var counts = samplesAt < 0 ? snapshot : snapshot[..samplesAt];
                return $"pane{index}={notificationProperty.GetValue(site)}; view-messages={counts}";
            }));
            var messageCountsField = hostType.GetField("_nativeUserMessages", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var messageSamplesField = hostType.GetField("_nativeUserMessageSamples", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Dictionary<uint, int>[] messageBaseline = [];
            int[] messageSampleBaseline = [];
            void CaptureMessageBaseline() => messageBaseline = hosts.Select(host =>
                new Dictionary<uint, int>((Dictionary<uint, int>)messageCountsField.GetValue(host)!)).ToArray();
            void CaptureMessageSamples() => messageSampleBaseline = hosts.Select(host =>
                ((List<string>)messageSamplesField.GetValue(host)!).Count).ToArray();
            void CaptureDiagnosticsBaseline() { CaptureMessageBaseline(); CaptureMessageSamples(); }
            string ViewMessageDelta() => string.Join('|', hosts.Select((host, index) =>
            {
                var current = (Dictionary<uint, int>)messageCountsField.GetValue(host)!;
                var delta = current.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key:X}:{pair.Value - (index < messageBaseline.Length ? messageBaseline[index].GetValueOrDefault(pair.Key) : 0)}")
                    .Where(value => !value.EndsWith(":0", StringComparison.Ordinal));
                return $"pane{index}=" + (string.Join(',', delta) is { Length: > 0 } values ? values : "none");
            }));
            string ViewMessageSamples() => string.Join('|', hosts.Select((host, index) =>
            {
                var samples = (List<string>)messageSamplesField.GetValue(host)!;
                var start = index < messageSampleBaseline.Length ? messageSampleBaseline[index] : 0;
                return $"pane{index}=" + (start < samples.Count ? string.Join(',', samples.Skip(start)) : "none");
            }));
            var foregroundBeforeAttach = GetForegroundWindow();
            source.RootVisual = grid;
            grid.Measure(new Size(1000, 600)); grid.Arrange(new Rect(0, 0, 1000, 600)); grid.UpdateLayout();
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeAttach, GetForegroundWindow(),
                "attaching ExplorerBrowserHost instances");
            if (IsWindowVisible(source.Handle))
                throw new InvalidOperationException("Hidden HostProbe parent became visible during ExplorerBrowserHost attachment.");
            var foregroundBeforeReadiness = GetForegroundWindow();
            if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("ExplorerBrowserHost instances did not attach their Shell views within 10 seconds.");
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeReadiness, GetForegroundWindow(),
                "waiting for ExplorerBrowserHost Shell views");
            if (IsWindowVisible(source.Handle))
                throw new InvalidOperationException("Hidden HostProbe parent became visible while waiting for Shell views.");
            if (args[0] == "--shell-selection-invert-modes")
            {
                var result = VerifyProductionInvertSelectionModes(hostType, hosts, folders);
                HostProbeForegroundGuard.EnsureUnchanged(foregroundBeforeProbe, GetForegroundWindow(),
                    "production ExplorerBrowser invert-selection mode matrix");
                if (IsWindowVisible(source.Handle))
                    throw new Exception("Invert-selection mode probe showed its hidden parent.");
                Console.WriteLine($"shell-selection-invert-modes matrix={result}; foreground-unchanged=True; parent-visible=False");
                return 0;
            }
            if (args[0] == "--shell-change-registration-after-hosts")
            {
                var foreground = GetForegroundWindow();
                var registrationClock = Stopwatch.StartNew();
                var registrationRoot = Path.Combine(root, "registration-target");
                Directory.CreateDirectory(registrationRoot);
                bool RegisterOnce(string label, string target)
                {
                    try
                    {
                        var registrationObserver = new ChangeNotificationObserver(source, target, registrationClock, resolveTargetPaths: false);
                        registrationObserver.Dispose();
                        if (!registrationObserver.DeregistrationSucceeded)
                            throw new Exception("SHChangeNotifyDeregister returned false.");
                        Console.WriteLine($"{label}=success elapsed-ms={registrationClock.ElapsedMilliseconds}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{label}=failed error={ex.Message}");
                        return false;
                    }
                }
                var rootRegistration = RegisterOnce("registration-after-four-hosts-root", root);
                // Shell may finish deregistration asynchronously; pump the owning STA before reusing it.
                ShellUpdateNotificationProbe.PumpDispatcherFor(150);
                var emptyTargetRegistration = RegisterOnce("registration-after-four-hosts-empty-target", registrationRoot);
                if (GetForegroundWindow() != foreground || IsWindowVisible(source.Handle))
                    throw new Exception("Registration probe changed the foreground window or showed its hidden parent.");
                Console.WriteLine($"registration-after-four-hosts foreground-unchanged=true parent-hidden=true root={rootRegistration} empty-target={emptyTargetRegistration}");
                return rootRegistration && emptyTargetRegistration ? 0 : 1;
            }
            if (args[0] == "--auto-column-width")
            {
                if (!WaitForExpectedCounts(hosts, [3, 1, 1, 1], TimeSpan.FromSeconds(10)))
                    throw new Exception("Auto-column-width Shell fixtures did not finish navigation.");
                return AutoColumnWidthProbe.Run(hostType, hosts, source, foregroundBeforeProbe);
            }
            if (args[0] == "--file-view-export")
            {
                var foreground = GetForegroundWindow();
                if (!WaitForExpectedCounts(hosts, [2, 1, 1, 1], TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("File-view export Shell fixtures did not finish navigation.");
                SelectOnlyItem(hostType, hosts[0], folders[0], "initial.txt");
                var selected = SelectedPaths(hostType, hosts[0], folders[0]);
                var capture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var snapshot = capture.Invoke(hosts[0], null)!;
                var rows = ((System.Collections.IEnumerable)snapshot.GetType().GetProperty("Rows")!.GetValue(snapshot)!).Cast<System.Collections.IEnumerable>()
                    .Select(row => row.Cast<object?>().Select(value => value?.ToString() ?? string.Empty).ToArray()).ToArray();
                var includesSelected = rows.Any(row => row.Any(cell => cell.Contains("initial.txt", StringComparison.OrdinalIgnoreCase)));
                var includesUnselected = rows.Any(row => row.Any(cell => cell.Contains("unselected.txt", StringComparison.OrdinalIgnoreCase)));
                var stillOnlySelected = selected.SequenceEqual([Path.Combine(folders[0], "initial.txt")], StringComparer.OrdinalIgnoreCase);
                Console.WriteLine($"file-view-export rows={rows.Length} selected-included={includesSelected} unselected-included={includesUnselected} selection-unchanged={stillOnlySelected}");
                if (rows.Length != 2 || !includesSelected || !includesUnselected || !stillOnlySelected)
                    throw new Exception("Current-view export did not include both selected and unselected visible items without changing selection.");
                if (GetForegroundWindow() != foreground || IsWindowVisible(source.Handle))
                    throw new Exception("File-view export probe changed foreground or showed its hidden parent.");
                return 0;
            }
            if (args[0] == "--selected-paths-clipboard")
            {
                if (!WaitForExpectedCounts(hosts, [2, 1, 1, 1], TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Selected-path Shell fixtures did not finish navigation.");
                var foreground = GetForegroundWindow();
                var capture = hostType.GetMethod("CaptureSelectedPathsForClipboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
                SelectOnlyItem(hostType, hosts[0], folders[0], "initial.txt");
                var single = ((System.Collections.IEnumerable)capture.Invoke(hosts[0], null)!).Cast<string>().ToArray();
                var initialPath = Path.Combine(folders[0], "initial.txt");
                if (!single.SequenceEqual([initialPath], StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Single-item path capture returned an unexpected path set: {string.Join('|', single)}.");
                SelectAll(hostType, hosts[0]);
                var multiple = ((System.Collections.IEnumerable)capture.Invoke(hosts[0], null)!).Cast<string>().ToArray();
                var expected = new[] { initialPath, Path.Combine(folders[0], "second.txt") };
                if (!multiple.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(expected.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Multi-item path capture returned an unexpected path set: {string.Join('|', multiple)}.");
                if (GetForegroundWindow() != foreground || IsWindowVisible(source.Handle))
                    throw new Exception("Selected-path capture changed foreground or showed its hidden parent.");
                Console.WriteLine($"selected-paths-clipboard single={string.Join('|', single)} multiple-count={multiple.Length} foreground-unchanged=True parent-visible=False clipboard-untouched=True");
                return 0;
            }
            if (args[0] == "--live-shell-changed-metadata")
                return RunChangedMetadataProbe(hostType, hosts[0], folders[0], root, source, changedBurstCount);
            if (args[0] == "--preview-pane")
            {
                var foreground = GetForegroundWindow();
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Preview-pane Shell fixtures did not finish navigation.");
                SelectOnlyItem(hostType, hosts[0], folders[0], "initial.txt");
                WaitForPredicate(() => false, TimeSpan.FromMilliseconds(500));
                foreach (var (host, index) in hosts.Select((host, index) => (host, index)))
                {
                    var hostWindow = (nint)hostType.GetField("_hostWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    var children = ChildWindowDescriptions(hostWindow);
                    var relevantChildren = index == 0 ? children.Select(description => description + ":bounds=" + WindowBounds(ParseHandle(description))) :
                        children.Where(description => description.Contains(":Shell Preview Extension Host:", StringComparison.Ordinal))
                            .Select(description => description + ":bounds=" + WindowBounds(ParseHandle(description)));
                    Console.WriteLine($"preview-pane pane={index} host={DescribeWindow(hostWindow)} view={DescribeWindow(viewWindow)} bounds={WindowBounds(hostWindow)};{string.Join(';', relevantChildren)}");
                }
                var probeHost = hosts[0];
                var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probeHost)!;
                var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
                var getOptions = browserInterface.GetMethod("GetOptions")!;
                var viewWindowBeforeToggle = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probeHost);
                var setPreviewPaneVisible = hostType.GetMethod("SetPreviewPaneVisible", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var setDetailsPaneVisible = hostType.GetMethod("SetDetailsPaneVisible", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var browserSite = hostType.GetField("_browserSite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probeHost)!;
                var paneVisibilityType = assembly.GetType("ExplorerDirs.App.ExplorerPaneVisibility", true)!;
                var previewPaneId = (Guid)paneVisibilityType.GetField("PreviewPaneId", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                var detailsPaneId = (Guid)paneVisibilityType.GetField("DetailsPaneId", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                var getPaneState = browserSite.GetType().GetMethod("GetPaneState")!;
                uint PaneState(Guid pane)
                {
                    var arguments = new object?[] { pane, 0u };
                    getPaneState.Invoke(browserSite, arguments);
                    return (uint)arguments[1]!;
                }
                var previewOn = (bool)setPreviewPaneVisible.Invoke(probeHost, [true])!;
                var previewStateOn = PaneState(previewPaneId) == 0x00030001u && PaneState(detailsPaneId) == 0x00030002u;
                var onOptionsResult = new object?[] { 0u };
                getOptions.Invoke(browser, onOptionsResult);
                var framesEnabled = (((uint)onOptionsResult[0]!) & 0x2u) != 0;
                var detailsOn = (bool)setDetailsPaneVisible.Invoke(probeHost, [true])!;
                var detailsStateOn = PaneState(previewPaneId) == 0x00030002u && PaneState(detailsPaneId) == 0x00030001u;
                var previewRestored = (bool)setPreviewPaneVisible.Invoke(probeHost, [true])!;
                var previewStateRestored = PaneState(previewPaneId) == 0x00030001u && PaneState(detailsPaneId) == 0x00030002u;
                var previewOff = (bool)setPreviewPaneVisible.Invoke(probeHost, [false])!;
                var previewStateOff = PaneState(previewPaneId) == 0x00030002u && PaneState(detailsPaneId) == 0x00030002u;
                var detailsOnAgain = (bool)setDetailsPaneVisible.Invoke(probeHost, [true])!;
                var detailsOff = (bool)setDetailsPaneVisible.Invoke(probeHost, [false])!;
                var detailsStateOff = PaneState(previewPaneId) == 0x00030002u && PaneState(detailsPaneId) == 0x00030002u;
                var offOptionsResult = new object?[] { 0u };
                getOptions.Invoke(browser, offOptionsResult);
                var framesDisabled = (((uint)offOptionsResult[0]!) & 0x2u) == 0;
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(5)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(5)))
                    throw new Exception("ExplorerBrowser did not reattach its file view after toggling native frames.");
                var activePathAfterToggle = (string)hostType.GetProperty("CurrentPath")!.GetValue(probeHost)!;
                var selectedAfterToggle = SelectedPaths(hostType, probeHost, folders[0]);
                var viewWindowAfterToggle = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probeHost);
                var viewIdentityPreserved = Equals(viewWindowBeforeToggle, viewWindowAfterToggle);
                if (!previewOn || !detailsOn || !previewRestored || !previewOff || !detailsOnAgain || !detailsOff ||
                    !previewStateOn || !detailsStateOn || !previewStateRestored || !previewStateOff || !detailsStateOff ||
                    !framesDisabled || !framesEnabled || !viewIdentityPreserved ||
                    !string.Equals(activePathAfterToggle, folders[0], StringComparison.OrdinalIgnoreCase) ||
                    !selectedAfterToggle.Contains(Path.Combine(folders[0], "initial.txt"), StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Toggling native preview/details panes did not preserve the current Shell view: calls={previewOn}/{detailsOn}/{previewRestored}/{previewOff}/{detailsOnAgain}/{detailsOff}, pane-states={previewStateOn}/{detailsStateOn}/{previewStateRestored}/{previewStateOff}/{detailsStateOff}, frames-off={framesDisabled}, frames-on={framesEnabled}, view-same={viewIdentityPreserved}, path={activePathAfterToggle}, selected={string.Join('|', selectedAfterToggle)}.");
                Console.WriteLine($"shell-pane-toggle preview/details/preview/off/details/off={previewOn}/{detailsOn}/{previewRestored}/{previewOff}/{detailsOnAgain}/{detailsOff} pane-states={previewStateOn}/{detailsStateOn}/{previewStateRestored}/{previewStateOff}/{detailsStateOff} frames-off={framesDisabled} frames-on={framesEnabled} view-identity-preserved={viewIdentityPreserved} path-preserved=True selection-preserved=True count={Count(probeHost)}");
                if (GetForegroundWindow() != foreground || IsWindowVisible(source.Handle))
                    throw new Exception("Preview-pane probe changed foreground or showed its hidden parent.");
                Console.WriteLine("preview-pane foreground-unchanged=True parent-visible=False");
                return 0;
            }
            if (args[0] == "--quick-filter")
            {
                if (!WaitForCounts(hosts, expected: 4, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Quick-filter Shell fixtures did not finish navigation.");
                try
                {
                    var result = QuickFilterProbe.Run(hostType, hosts, folders, Count, ViewItemPaths);
                    if (IsWindowVisible(source.Handle) || GetForegroundWindow() != foregroundBeforeProbe)
                        throw new Exception("Quick-filter probe showed its hidden parent or changed the foreground window.");
                    Console.WriteLine("quick-filter: parent-hidden=True foreground-unchanged=True");
                    return result;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                    return 1;
                }
            }
            if (args[0] == "--column-value-filter")
            {
                if (!WaitForCounts(hosts, expected: 2, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Column-value filter Shell fixtures did not finish navigation.");
                try
                {
                    var result = ColumnValueFilterProbe.Run(hostType, hosts, folders, Count, ViewItemPaths);
                    if (IsWindowVisible(source.Handle) || GetForegroundWindow() != foregroundBeforeProbe)
                        throw new Exception("Column-value filter probe showed its hidden parent or changed the foreground window.");
                    Console.WriteLine("column-value-filter: parent-hidden=True foreground-unchanged=True");
                    return result;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                    return 1;
                }
            }
            if (args[0] is "--file-command-states" or "--file-command-states-early")
            {
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("File-command Shell fixtures did not finish navigation.");
                var query = hostType.GetMethod("QueryFileCommands", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var commandType = hostType.Assembly.GetType("ExplorerDirs.App.ShellFileCommand", true)!;
                bool Enabled(object states, string name) =>
                    (bool)states.GetType().GetProperty("Item")!.GetValue(states, [Enum.Parse(commandType, name)])!;
                string Snapshot(object states) => string.Join(',', new[] { "Cut", "Copy", "Delete", "Rename", "Properties" }
                    .Select(name => $"{name}={Enabled(states, name)}"));
                bool SelectionVerbs(object states, bool expected) =>
                    new[] { "Cut", "Copy", "Delete", "Rename", "Properties" }.All(name => Enabled(states, name) == expected);
                bool FixturesUnchanged() => folders.All(folder =>
                    Directory.EnumerateFileSystemEntries(folder).SequenceEqual([Path.Combine(folder, "initial.txt")], StringComparer.OrdinalIgnoreCase) &&
                    File.ReadAllText(Path.Combine(folder, "initial.txt")) == "initial");

                var foreground = GetForegroundWindow();
                if (!FixturesUnchanged()) throw new Exception("File-command fixture was not pristine before menu-state queries.");
                var emptyBefore = query.Invoke(hosts[0], null)!;
                if (!SelectionVerbs(emptyBefore, false))
                    throw new Exception($"Empty selection exposed selection-only Shell commands: {Snapshot(emptyBefore)}.");
                SelectOnlyItem(hostType, hosts[0], folders[0], "initial.txt");
                var selected = query.Invoke(hosts[0], null)!;
                if (!SelectionVerbs(selected, true))
                    throw new Exception($"Single selection did not expose expected Shell commands: {Snapshot(selected)}.");
                var neighboringPane = query.Invoke(hosts[1], null)!;
                if (!SelectionVerbs(neighboringPane, false))
                    throw new Exception($"Pane 0 selection leaked into pane 1 commands: {Snapshot(neighboringPane)}.");
                if (!FixturesUnchanged()) throw new Exception("Read-only menu-state queries changed fixture files.");

                var menuInterface = hostType.Assembly.GetType("ExplorerDirs.App.IShellCommandMenu", true)!;
                var openMenu = hostType.GetMethod("OpenFileCommandMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var openDelegateType = typeof(Func<,>).MakeGenericType(typeof(bool), menuInterface);
                var openDelegate = Delegate.CreateDelegate(openDelegateType, hosts[0], openMenu);
                var commandsType = hostType.Assembly.GetType("ExplorerDirs.App.ShellFileCommands", true)!;
                var invokeCommand = commandsType.GetMethod("Invoke", BindingFlags.Static | BindingFlags.NonPublic)!;
                var initialCountProperty = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var createdEventProperty = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var changeWatcherField = hostType.GetField("_directoryChangeWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var folderObservationType = hostType.Assembly.GetType("ExplorerDirs.App.FolderCreationObservation", true)!;
                var folderObservation = folderObservationType.GetMethod("TryStart", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [folders[0]]);
                if (folderObservation is null)
                    throw new Exception("Could not start the production short-lived folder creation observation.");
                var folderSnapshot = folderObservationType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.Public)!;
                bool WatcherReady() => initialCountProperty.GetValue(hosts[0]) is int value && value == 1 &&
                    changeWatcherField.GetValue(hosts[0]) is FileSystemWatcher watcher && watcher.EnableRaisingEvents;
                var requireReadyWatcher = args[0] == "--file-command-states";
                if (requireReadyWatcher && !WaitForPredicate(WatcherReady, TimeSpan.FromSeconds(8)))
                    throw new Exception("Could not establish the production directory watcher before invoking NewFolder.");
                var watcherReadyBeforeInvoke = WatcherReady();
                var watcherInitialBeforeInvoke = initialCountProperty.GetValue(hosts[0]);
                var createdEventsBefore = (int)createdEventProperty.GetValue(hosts[0])!;
                var invokeResult = invokeCommand.Invoke(null,
                    [Enum.Parse(commandType, "NewFolder"), openDelegate, (Func<bool>)(() => true)]);
                if (invokeResult is not int hresult || hresult < 0)
                    throw new Exception($"Production NewFolder command was unavailable or rejected: {invokeResult ?? "null"}.");
                var expectedAfterNewFolder = new[] { 2, 1, 1, 1 };
                if (!WaitForPredicate(() =>
                {
                    var currentSnapshot = folderSnapshot.Invoke(folderObservation, null)!;
                    return ((string[])currentSnapshot.GetType().GetProperty("Directories")!.GetValue(currentSnapshot)!).Length > 0;
                }, TimeSpan.FromSeconds(3)))
                    throw new Exception("The production short-lived folder observation missed the NewFolder result.");
                var snapshot = folderSnapshot.Invoke(folderObservation, null)!;
                var observedDirectories = (string[])snapshot.GetType().GetProperty("Directories")!.GetValue(snapshot)!;
                var observationFailed = (bool)snapshot.GetType().GetProperty("Failed")!.GetValue(snapshot)!;
                var recoveryRefreshInvoked = false;
                string? focusBeforeRecoveryRefresh = null;
                string? focusAtFirstVisible = null;
                string? focusAfterRecoveryRefresh = null;
                string[] selectionBeforeRecoveryRefresh = [];
                string[] selectionAfterRecoveryRefresh = [];
                var refreshRestoreMilliseconds = hostType.GetProperty("DirectoryChangeLastRefreshRestoreMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var refreshRestoreBaseline = (long)refreshRestoreMilliseconds.GetValue(hosts[0])!;
                var focusedPathMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (!requireReadyWatcher && !observationFailed && observedDirectories.Length == 1)
                {
                    focusBeforeRecoveryRefresh = (string?)focusedPathMethod.Invoke(hosts[0], [folders[0]]);
                    selectionBeforeRecoveryRefresh = SelectedPaths(hostType, hosts[0], folders[0]).ToArray();
                    var refreshGateType = hostType.Assembly.GetType("ExplorerDirs.App.ShellNewFolderRefreshGate", true)!;
                    var refreshGate = Activator.CreateInstance(refreshGateType, nonPublic: true)!;
                    recoveryRefreshInvoked = (bool)refreshGateType.GetMethod("TryRequestRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(refreshGate,
                        [snapshot, focusBeforeRecoveryRefresh, true, true])!;
                    if (recoveryRefreshInvoked)
                    {
                        // Exercise the same one-shot gate and existing Shell Refresh path as the
                        // production callback, while explicitly bypassing only window activation.
                        hostType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public)!.Invoke(hosts[0], null);
                    }
                }
                var folderVisible = WaitForExpectedCounts(hosts, expectedAfterNewFolder, TimeSpan.FromSeconds(8));
                if (recoveryRefreshInvoked && folderVisible)
                {
                    if (!WaitForPredicate(() => (long)refreshRestoreMilliseconds.GetValue(hosts[0])! > refreshRestoreBaseline,
                            TimeSpan.FromSeconds(8)))
                        throw new Exception("The Shell view count converged but the asynchronous refresh selection/focus callback did not complete.");
                    selectionAfterRecoveryRefresh = SelectedPaths(hostType, hosts[0], folders[0]).ToArray();
                }
                if (folderVisible) focusAtFirstVisible = (string?)focusedPathMethod.Invoke(hosts[0], [folders[0]]);
                if (recoveryRefreshInvoked && folderVisible)
                    focusAfterRecoveryRefresh = (string?)focusedPathMethod.Invoke(hosts[0], [folders[0]]);
                var recoverySelectionPreserved = !recoveryRefreshInvoked || selectionBeforeRecoveryRefresh.SequenceEqual(
                    selectionAfterRecoveryRefresh, StringComparer.OrdinalIgnoreCase);
                var recoveryFocusPreserved = !recoveryRefreshInvoked || string.Equals(focusBeforeRecoveryRefresh,
                    focusAfterRecoveryRefresh, StringComparison.OrdinalIgnoreCase);
                if (!recoverySelectionPreserved || !recoveryFocusPreserved)
                    throw new Exception($"The one-shot recovery refresh changed the existing selection/focus: selection-before={string.Join('|', selectionBeforeRecoveryRefresh)}; selection-after={string.Join('|', selectionAfterRecoveryRefresh)}; focus-before={focusBeforeRecoveryRefresh}; focus-after={focusAfterRecoveryRefresh}.");
                if (!folderVisible)
                {
                    var initialCount = initialCountProperty.GetValue(hosts[0]);
                    var commandCreatedEvents = createdEventProperty.GetValue(hosts[0]);
                    var refreshes = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                    var changeWatcher = changeWatcherField.GetValue(hosts[0]);
                    throw new Exception($"NewFolder command was accepted but the Shell view did not show the folder: counts={string.Join(',', hosts.Select(Count))}; disk-entries={string.Join('|', Directory.EnumerateFileSystemEntries(folders[0]).Select(Path.GetFileName))}; disk-folders={string.Join('|', Directory.EnumerateDirectories(folders[0]).Select(Path.GetFileName))}; observed-folders={string.Join('|', observedDirectories.Select(Path.GetFileName))}; observation-failed={observationFailed}; hresult={hresult:X8}; watcher-ready-before-invoke={watcherReadyBeforeInvoke}; initial-before-invoke={watcherInitialBeforeInvoke ?? "null"}; watcher={changeWatcher is not null}; watcher-enabled={changeWatcher?.GetType().GetProperty("EnableRaisingEvents")?.GetValue(changeWatcher)}; initial={initialCount}; created-events={commandCreatedEvents}; created-event-delta={(int)commandCreatedEvents! - createdEventsBefore}; fallback-refreshes={refreshes}; recovery-refresh-invoked={recoveryRefreshInvoked}.");
                }
                var createdFolders = Directory.EnumerateDirectories(folders[0]).ToArray();
                var paneZeroEntries = Directory.EnumerateFileSystemEntries(folders[0]).ToArray();
                var otherPanesUnchanged = folders.Skip(1).All(folder =>
                    Directory.EnumerateFileSystemEntries(folder).SequenceEqual([Path.Combine(folder, "initial.txt")], StringComparer.OrdinalIgnoreCase) &&
                    File.ReadAllText(Path.Combine(folder, "initial.txt")) == "initial");
                if (createdFolders.Length != 1 || !File.Exists(Path.Combine(folders[0], "initial.txt")) ||
                    paneZeroEntries.Length != 2 || !paneZeroEntries.Contains(Path.Combine(folders[0], "initial.txt"), StringComparer.OrdinalIgnoreCase) ||
                    Directory.EnumerateFileSystemEntries(createdFolders.SingleOrDefault() ?? string.Empty).Any() || !otherPanesUnchanged)
                    throw new Exception($"NewFolder command did not produce exactly one empty fixture folder: {string.Join('|', createdFolders)}.");
                var createdEventsAfter = (int)createdEventProperty.GetValue(hosts[0])!;
                var fallbackRefreshes = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                if (GetForegroundWindow() != foreground || IsWindowVisible(source.Handle))
                    throw new Exception("Command-state query changed foreground or showed the hidden host.");
                Console.WriteLine($"file-command-states mode={(requireReadyWatcher ? "ready" : "early")} watcher-ready-before-invoke={watcherReadyBeforeInvoke} watcher-initial-before-invoke={watcherInitialBeforeInvoke ?? "null"} observed-folders={string.Join('|', observedDirectories.Select(Path.GetFileName))} observation-failed={observationFailed} recovery-refresh-invoked={recoveryRefreshInvoked} recovery-selection-preserved={recoverySelectionPreserved} recovery-focus-preserved={recoveryFocusPreserved} selected-before-refresh={string.Join('|', selectionBeforeRecoveryRefresh.Select(Path.GetFileName))} selected-after-refresh={string.Join('|', selectionAfterRecoveryRefresh.Select(Path.GetFileName))} focus-at-first-visible={focusAtFirstVisible ?? "null"} focus-before-recovery-refresh={focusBeforeRecoveryRefresh ?? "null"} focus-after-recovery-refresh={focusAfterRecoveryRefresh ?? "null"} empty=[{Snapshot(emptyBefore)}] selected=[{Snapshot(selected)}] neighboring-empty=[{Snapshot(neighboringPane)}] new-folder-hresult={hresult:X8} created-folder-view-visible=True watcher-created-delta={createdEventsAfter - createdEventsBefore} fallback-refreshes={fallbackRefreshes} unrelated-panes-unchanged=True fixtures-cleaned-on-exit=True foreground-unchanged=True parent-visible=False");
                (folderObservation as IDisposable)?.Dispose();
                return 0;
            }
            if (args[0] == "--live-shell-hwnd-tree")
            {
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation and attach before the HWND snapshot.");
                void Snapshot(string phase)
                {
                    var threadId = GetWindowThreadProcessId(source.Handle, out var processId);
                    Console.WriteLine($"thread-windows phase={phase} thread={threadId} process={processId} windows={string.Join(';', ThreadWindowDescriptions(threadId))}");
                    for (var i = 0; i < hosts.Count; i++)
                    {
                        var hostWindow = (nint)hostType.GetField("_hostWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[i])!;
                        var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[i])!;
                        Console.WriteLine($"hwnd-tree phase={phase} pane={i} wpf-visible={hosts[i].IsVisible} host={DescribeWindow(hostWindow)} view={DescribeWindow(viewWindow)} descendants={string.Join(';', ChildWindowDescriptions(hostWindow))}");
                    }
                }

                Snapshot("initial");
                hosts[0].Visibility = Visibility.Collapsed;
                grid.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Snapshot("pane0-collapsed");
                hosts[0].Visibility = Visibility.Visible;
                grid.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Snapshot("pane0-restored");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return 0;
            }
            if (args[0].StartsWith("--live-shell-visibility-updates", StringComparison.Ordinal))
            {
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation and attach before visibility testing.");
                var hiddenPane = args[0] == "--live-shell-visibility-updates" ? 0 : int.Parse(args[0]["--live-shell-visibility-updates-pane-".Length..]);
                var visiblePane = hiddenPane ^ 1;
                hosts[hiddenPane].Visibility = Visibility.Collapsed;
                grid.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                var hiddenName = "created-hidden-pane.txt";
                var visibleName = "created-visible-pane.txt";
                File.WriteAllText(Path.Combine(folders[hiddenPane], hiddenName), hiddenName);
                File.WriteAllText(Path.Combine(folders[visiblePane], visibleName), visibleName);
                var expected = Enumerable.Repeat(1, hosts.Count).ToArray();
                expected[hiddenPane] = 2;
                expected[visiblePane] = 2;
                var updatedWhileCollapsed = WaitForExpectedCounts(hosts, expected, TimeSpan.FromSeconds(5));
                Console.WriteLine($"visibility-update collapsed-pane={hiddenPane} visible-pane={visiblePane} counts-while-collapsed={string.Join(',', hosts.Select(Count))} updated-while-collapsed={updatedWhileCollapsed}");
                hosts[hiddenPane].Visibility = Visibility.Visible;
                grid.UpdateLayout();
                var updatedAfterRestore = WaitForExpectedCounts(hosts, expected, TimeSpan.FromSeconds(5));
                Console.WriteLine($"visibility-update counts-after-restore={string.Join(',', hosts.Select(Count))} updated-after-restore={updatedAfterRestore}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return 0;
            }
            if (crossVolumeProbe)
            {
                if (!WaitForPredicate(() => Count(hosts[0]) == 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Cross-volume Shell views did not finish navigation.");
                var initialCount = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (!WaitForPredicate(() => hosts.All(host => initialCount.GetValue(host) is int), TimeSpan.FromSeconds(6)) ||
                    hosts.Any(host => (int)initialCount.GetValue(host)! != 3))
                    throw new Exception("Cross-volume panes did not establish stable three-item monitor baselines.");

                var crossCreatedEventCount = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var crossDeletedEventCount = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeCreateNotifications = hostType.GetProperty("DirectoryChangeNativeCreateNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var fallbackCounts = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (junctionTargetProbe)
                {
                    var targetCreatedBefore = (int)crossCreatedEventCount.GetValue(hosts[1])!;
                    var targetDeletedBefore = (int)crossDeletedEventCount.GetValue(hosts[1])!;
                    var createdPath = Path.Combine(crossVolumeRoot!, "junction-created.txt");
                    File.WriteAllText(createdPath, "junction");
                    var createdWatcher = WaitForPredicate(() =>
                        (int)crossCreatedEventCount.GetValue(hosts[1])! > targetCreatedBefore, TimeSpan.FromSeconds(5));
                    var createdVisible = createdWatcher && WaitForExpectedCounts(hosts, [3, 4, 3, 3], TimeSpan.FromSeconds(10));
                    File.Delete(createdPath);
                    var deletedWatcher = WaitForPredicate(() =>
                        (int)crossDeletedEventCount.GetValue(hosts[1])! > targetDeletedBefore, TimeSpan.FromSeconds(5));
                    var deletedVisible = deletedWatcher && WaitForExpectedCounts(hosts, [3, 3, 3, 3], TimeSpan.FromSeconds(10));
                    Console.WriteLine($"junction target={folders[1]} created-watcher={createdWatcher} created-visible={createdVisible} deleted-watcher={deletedWatcher} deleted-visible={deletedVisible} view-counts={string.Join('/', hosts.Select(Count))} fallbacks={string.Join('/', hosts.Select(host => fallbackCounts.GetValue(host)))}");
                    if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                    return createdVisible && deletedVisible ? 0 : 1;
                }
                var sourceCreateBefore = (int)crossCreatedEventCount.GetValue(hosts[0])!;
                var targetCreateBefore = (int)crossCreatedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.CopyAcrossVolumes(root, folders[0], "initial.txt",
                    crossVolumeRoot!, folders[1], "cross-volume-copy.txt");
                var copySourceUntouched = (int)crossCreatedEventCount.GetValue(hosts[0])! == sourceCreateBefore;
                var copyTargetWatcher = WaitForPredicate(() => (int)crossCreatedEventCount.GetValue(hosts[1])! > targetCreateBefore,
                    TimeSpan.FromSeconds(5));
                var copiedCounts = new[] { 3, 4, 3, 3 };
                var crossVolumeCopyVisible = copyTargetWatcher && WaitForExpectedCounts(hosts, copiedCounts, TimeSpan.FromSeconds(10)) &&
                    File.Exists(Path.Combine(folders[0], "initial.txt")) &&
                    File.ReadAllText(Path.Combine(folders[1], "cross-volume-copy.txt")) == "initial";
                var targetDeleteBefore = (int)crossDeletedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.DeleteAcrossVolumeFixture(crossVolumeRoot!, folders[1], "cross-volume-copy.txt");
                var copyCleanupWatcher = WaitForPredicate(() => (int)crossDeletedEventCount.GetValue(hosts[1])! > targetDeleteBefore,
                    TimeSpan.FromSeconds(5));
                var initialCounts = Enumerable.Repeat(3, hosts.Count).ToArray();
                var copyCleanupVisible = copyCleanupWatcher && WaitForExpectedCounts(hosts, initialCounts, TimeSpan.FromSeconds(10));

                var sourceDeleteBefore = (int)crossDeletedEventCount.GetValue(hosts[0])!;
                var targetMoveCreateBefore = (int)crossCreatedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.MoveAcrossVolumes(root, folders[0], "initial.txt",
                    crossVolumeRoot!, folders[1], "cross-volume-moved.txt");
                var moveSourceWatcher = WaitForPredicate(() => (int)crossDeletedEventCount.GetValue(hosts[0])! > sourceDeleteBefore,
                    TimeSpan.FromSeconds(5));
                var moveTargetWatcher = WaitForPredicate(() => (int)crossCreatedEventCount.GetValue(hosts[1])! > targetMoveCreateBefore,
                    TimeSpan.FromSeconds(5));
                var movedCounts = new[] { 2, 4, 3, 3 };
                var crossVolumeMoveVisible = moveSourceWatcher && moveTargetWatcher &&
                    WaitForExpectedCounts(hosts, movedCounts, TimeSpan.FromSeconds(10)) &&
                    !File.Exists(Path.Combine(folders[0], "initial.txt")) &&
                    File.ReadAllText(Path.Combine(folders[1], "cross-volume-moved.txt")) == "initial";
                Console.WriteLine($"cross-volume target-kind={(junctionTargetProbe ? "junction" : uncTargetProbe ? "UNC" : "fixed-volume")} source={Path.GetPathRoot(folders[0])} target={Path.GetPathRoot(folders[1])} copy-visible={crossVolumeCopyVisible} copy-source-watcher-unchanged={copySourceUntouched} copy-target-watcher={copyTargetWatcher} copy-cleanup-visible={copyCleanupVisible} move-visible={crossVolumeMoveVisible} move-source-deleted={moveSourceWatcher} move-target-created={moveTargetWatcher} view-counts={string.Join('/', hosts.Select(Count))} target-native-create-notifications={nativeCreateNotifications.GetValue(hosts[1])} fallbacks={string.Join('/', hosts.Select(host => fallbackCounts.GetValue(host)))}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return crossVolumeCopyVisible && copySourceUntouched && copyCleanupVisible && crossVolumeMoveVisible ? 0 : 1;
            }
            if (args[0] == "--live-shell-selection-user-clear")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the user-clear probe.");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                SelectAll(hostType, hosts[0]);
                var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                SendMessage(viewWindow, 0x0100, (nint)0x10, (nint)1); // Synthetic Shift input advances the product input generation.
                SendMessage(viewWindow, 0x0101, (nint)0x10, (nint)unchecked((int)0xC0000001));
                SelectNone(hostType, hosts[0]);
                WaitForDelay(TimeSpan.FromMilliseconds(500));
                var selectedAfterClear = SelectedPaths(hostType, hosts[0], folders[0]);
                Console.WriteLine($"shell-selection-user-clear selected-after-clear={string.Join(',', selectedAfterClear.Select(Path.GetFileName))} selection-remains-clear={selectedAfterClear.Length == 0}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return selectedAfterClear.Length == 0 ? 0 : 1;
            }
            if (args[0] == "--live-shell-selection-partial-snapshot")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the partial-selection probe.");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                SelectAll(hostType, hosts[0]);
                var fullSelection = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (fullSelection.Length != 3) throw new Exception($"Expected a three-item baseline selection, got {fullSelection.Length}.");
                InvokeShellSelectionChanged(hostType, hosts[0]);
                DeselectItem(hostType, hosts[0], folders[0], "third.txt");
                var partialSelection = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (partialSelection.Length != 2) throw new Exception($"Could not establish the partial selection: {string.Join(',', partialSelection)}.");
                InvokeShellSelectionChanged(hostType, hosts[0]);
                var retainedSnapshot = hostType.GetField("_lastNativeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                var retainedSnapshotCount = retainedSnapshot is null ? 0 : (int)retainedSnapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(retainedSnapshot)!;
                SelectNone(hostType, hosts[0]);
                InvokeShellSelectionChanged(hostType, hosts[0]);
                var restored = WaitForPredicate(() =>
                {
                    var selected = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    return selected.SequenceEqual(fullSelection, StringComparer.OrdinalIgnoreCase);
                }, TimeSpan.FromSeconds(5));
                var finalSelection = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                Console.WriteLine($"selection-partial-snapshot full={fullSelection.Length} partial={partialSelection.Length} retained-snapshot={retainedSnapshotCount} restored={restored} final={string.Join(',', finalSelection.Select(Path.GetFileName))}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return retainedSnapshotCount == fullSelection.Length && restored ? 0 : 1;
            }
            if (args[0] == "--live-shell-empty-directory-deferred-restore")
            {
                if (!WaitForPredicate(() => Count(hosts[0]) == 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the empty-directory deferred-restore probe.");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                SelectAll(hostType, hosts[0]);
                var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], null)!;
                object snapshot;
                try
                {
                    var snapshotType = hostType.Assembly.GetType("ExplorerDirs.App.ShellViewSelectionSnapshot", true)!;
                    snapshot = snapshotType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [view])!
                        ?? throw new Exception("Could not capture the three-item selection before emptying the folder.");
                }
                finally { Marshal.Release(view); }
                var selectedCount = (int)snapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!;
                if (selectedCount != 3) throw new Exception($"Expected three saved selections, got {selectedCount}.");

                foreach (var name in new[] { "initial.txt", "second.txt", "third.txt" })
                    File.Delete(Path.Combine(folders[0], name));
                var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
                var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
                var viewObject = new object?[] { shellViewId, null };
                Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, viewObject)!);
                var shellView = viewObject[1]!;
                try
                {
                    var shellViewInterface = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!;
                    Marshal.ThrowExceptionForHR((int)shellViewInterface.GetMethod("Refresh")!.Invoke(shellView, null)!);
                }
                finally { Marshal.ReleaseComObject(shellView); }
                if (!WaitForPredicate(() => Count(hosts[0]) == 0, TimeSpan.FromSeconds(10)))
                    throw new Exception($"The Shell view did not become empty after deleting its three fixture items; count={Count(hosts[0])}.");

                var readiness = hostType.GetField("_viewReadiness", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var navigationVersion = (long)readiness.GetType().GetProperty("NavigationVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(readiness)!;
                var inputVersion = (long)hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var path = (string)hostType.GetProperty("CurrentPath")!.GetValue(hosts[0])!;
                var scheduled = (bool)hostType.GetMethod("ScheduleDeferredRefreshSelectionRestore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], [snapshot, path, navigationVersion, inputVersion, false])!;
                if (!scheduled)
                {
                    ((IDisposable)snapshot).Dispose();
                    throw new Exception("Could not schedule the captured selection against the stable empty view.");
                }
                var pending = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var retryClock = System.Diagnostics.Stopwatch.StartNew();
                var settled = WaitForPredicate(() => !(bool)pending.GetValue(hosts[0])!, TimeSpan.FromSeconds(5));
                retryClock.Stop();
                if (!settled) throw new Exception("The bounded empty-view selection retry did not settle.");
                var timelineProperty = hostType.GetProperty("ShellSelectionRestoreTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var timelineAtSettle = (string)timelineProperty.GetValue(hosts[0])!;
                WaitForDelay(TimeSpan.FromMilliseconds(300));
                var timelineAfterQuiet = (string)timelineProperty.GetValue(hosts[0])!;
                var deferredSteps = timelineAtSettle.Split('|').Where(step => step.Contains("source=deferred-refresh", StringComparison.Ordinal)).ToArray();
                var deferredBatches = deferredSteps.Count(step => step.Contains("selection-batch-api count=", StringComparison.Ordinal));
                var fallbackBatches = timelineAtSettle.Split('|').Count(step =>
                    step.Contains("source=deferred-fallback", StringComparison.Ordinal) &&
                    step.Contains("selection-batch-api count=", StringComparison.Ordinal));
                var fallbackBatchCountAfterQuiet = timelineAfterQuiet.Split('|').Count(step =>
                    step.Contains("source=deferred-fallback", StringComparison.Ordinal) &&
                    step.Contains("selection-batch-api count=", StringComparison.Ordinal));
                var emptyViewDeferrals = timelineAtSettle.Split('|').Count(step => step.Contains("selection-restore-deferred-empty-view count=0", StringComparison.Ordinal));
                var finalCount = Count(hosts[0]);
                var finalSelection = SelectedPaths(hostType, hosts[0], folders[0]);
                var oneFinalAttempt = fallbackBatches > 0 && fallbackBatchCountAfterQuiet == fallbackBatches;
                var stayedDeferredUntilFallback = deferredBatches == 0 && emptyViewDeferrals > 0;
                var retryWindowElapsed = retryClock.ElapsedMilliseconds >= 1400;
                var parentVisible = IsWindowVisible(source.Handle);
                var success = retryWindowElapsed && finalCount == 0 && finalSelection.Length == 0 &&
                    oneFinalAttempt && stayedDeferredUntilFallback && !parentVisible;
                Console.WriteLine($"shell-empty-directory-deferred-restore scheduled={scheduled} settled={settled} retry-ms={retryClock.ElapsedMilliseconds} retry-window-elapsed={retryWindowElapsed} deferrals={emptyViewDeferrals} deferred-batch-calls={deferredBatches} fallback-batch-calls={fallbackBatches} final-count={finalCount} final-selection={finalSelection.Length} one-final-attempt={oneFinalAttempt} stayed-deferred-until-fallback={stayedDeferredUntilFallback} parent-visible={parentVisible} success={success}");
                if (parentVisible) throw new Exception("Probe parent became visible.");
                return success ? 0 : 1;
            }
            if (args[0] == "--live-shell-deferred-restore-user-input")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the deferred-restore cancellation probe.");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);

                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");
                var snapshotType = hostType.Assembly.GetType("ExplorerDirs.App.ShellViewSelectionSnapshot", true)!;
                var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null)!;
                object snapshot;
                try { snapshot = snapshotType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [view])!; }
                finally { Marshal.Release(view); }
                var readiness = hostType.GetField("_viewReadiness", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var navigationVersion = (long)readiness.GetType().GetProperty("NavigationVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(readiness)!;
                var inputVersionField = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var path = (string)hostType.GetProperty("CurrentPath")!.GetValue(hosts[0])!;
                var scheduled = (bool)hostType.GetMethod("ScheduleDeferredRefreshSelectionRestore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], [snapshot, path, navigationVersion, (long)inputVersionField.GetValue(hosts[0])!, true])!;
                if (!scheduled) throw new Exception("Could not schedule the old selection snapshot for deferred restore.");

                var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                SendMessage(viewWindow, 0x0100, (nint)0x10, (nint)1); // Real native-input path cancels the pending old snapshot.
                SendMessage(viewWindow, 0x0101, (nint)0x10, (nint)unchecked((int)0xC0000001));
                SelectOnlyItem(hostType, hosts[0], folders[0], "third.txt", takeFocus: true);
                var settledInputField = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var restorePendingField = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settled = WaitForPredicate(() =>
                    (long)settledInputField.GetValue(hosts[0])! == (long)inputVersionField.GetValue(hosts[0])! &&
                    !(bool)restorePendingField.GetValue(hosts[0])!, TimeSpan.FromSeconds(15));
                WaitForDelay(TimeSpan.FromMilliseconds(1600)); // Exceeds the complete deferred-restore retry window.
                var selectedAfterInput = SelectedPaths(hostType, hosts[0], folders[0]);
                var focusAfterInput = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], [folders[0]]);
                var selectionPreserved = selectedAfterInput.SequenceEqual([Path.Combine(folders[0], "third.txt")], StringComparer.OrdinalIgnoreCase);
                var focusPreserved = string.Equals(focusAfterInput, Path.Combine(folders[0], "third.txt"), StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"shell-deferred-restore-user-input scheduled={scheduled} settled={settled} old-restore-pending={(bool)restorePendingField.GetValue(hosts[0])!} selected={string.Join(',', selectedAfterInput.Select(Path.GetFileName))} focus={Path.GetFileName(focusAfterInput)} selection-preserved={selectionPreserved} focus-preserved={focusPreserved}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return settled && selectionPreserved && focusPreserved ? 0 : 1;
            }
            if (args[0] == "--live-shell-deferred-restore-stable-selection")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the stable-selection retry probe.");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");

                var snapshotType = hostType.Assembly.GetType("ExplorerDirs.App.ShellViewSelectionSnapshot", true)!;
                var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null)!;
                object snapshot;
                try { snapshot = snapshotType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [view])!; }
                finally { Marshal.Release(view); }
                var readiness = hostType.GetField("_viewReadiness", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var navigationVersion = (long)readiness.GetType().GetProperty("NavigationVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(readiness)!;
                var inputVersion = (long)hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var path = (string)hostType.GetProperty("CurrentPath")!.GetValue(hosts[0])!;
                var scheduled = (bool)hostType.GetMethod("ScheduleDeferredRefreshSelectionRestore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], [snapshot, path, navigationVersion, inputVersion, true])!;
                if (!scheduled) throw new Exception("Could not schedule a deferred focus retry with an already-restored selection.");

                // Simulate a Shell selection-version notification while the exact selected PIDL set remains unchanged.
                var callbackView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], null)!;
                try
                {
                    var shellSite = hostType.GetField("_browserSite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                    shellSite.GetType().GetMethod("OnStateChange")!.Invoke(shellSite, [callbackView, 2u]);
                }
                finally { Marshal.Release(callbackView); }

                var pending = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settled = WaitForPredicate(() => !(bool)pending.GetValue(hosts[0])!, TimeSpan.FromSeconds(5));
                var selected = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focus = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], [folders[0]]);
                var expected = new[] { Path.Combine(folders[0], "second.txt"), Path.Combine(folders[0], "third.txt") }
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var timeline = (string)hostType.GetProperty("ShellSelectionRestoreTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var deferredSteps = timeline.Split('|').Where(step => step.Contains("source=deferred-refresh", StringComparison.Ordinal)).ToArray();
                var skippedSelectionBatch = deferredSteps.Length > 0 && deferredSteps.All(step => !step.Contains("selection-batch-api", StringComparison.Ordinal));
                var exactSelectionMatched = deferredSteps.Any(step => step.Contains("selection-current matched=True", StringComparison.Ordinal));
                var selectionPreserved = selected.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
                var focusPreserved = string.Equals(focus, Path.Combine(folders[0], "initial.txt"), StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"shell-deferred-restore-stable-selection scheduled={scheduled} settled={settled} exact-selection-matched={exactSelectionMatched} selection-batch-skipped={skippedSelectionBatch} selected={string.Join(',', selected.Select(Path.GetFileName))} focus={Path.GetFileName(focus)} selection-preserved={selectionPreserved} focus-preserved={focusPreserved} steps={string.Join('|', deferredSteps)}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return settled && exactSelectionMatched && skippedSelectionBatch && selectionPreserved && focusPreserved ? 0 : 1;
            }
            if (args[0] == "--live-shell-deferred-restore-selection-mismatch")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the selection-mismatch retry probe.");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");

                var snapshotType = hostType.Assembly.GetType("ExplorerDirs.App.ShellViewSelectionSnapshot", true)!;
                var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], null)!;
                object snapshot;
                try { snapshot = snapshotType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [view])!; }
                finally { Marshal.Release(view); }
                var readiness = hostType.GetField("_viewReadiness", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var navigationVersion = (long)readiness.GetType().GetProperty("NavigationVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(readiness)!;
                var inputVersion = (long)hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var path = (string)hostType.GetProperty("CurrentPath")!.GetValue(hosts[0])!;
                var scheduled = (bool)hostType.GetMethod("ScheduleDeferredRefreshSelectionRestore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], [snapshot, path, navigationVersion, inputVersion, true])!;
                if (!scheduled) throw new Exception("Could not schedule a deferred restore before changing the Shell selection.");

                SelectOnlyItem(hostType, hosts[0], folders[0], "initial.txt", takeFocus: true);
                var callbackView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], null)!;
                try
                {
                    var shellSite = hostType.GetField("_browserSite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                    shellSite.GetType().GetMethod("OnStateChange")!.Invoke(shellSite, [callbackView, 2u]);
                }
                finally { Marshal.Release(callbackView); }

                var pending = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settled = WaitForPredicate(() => !(bool)pending.GetValue(hosts[0])!, TimeSpan.FromSeconds(5));
                var selected = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focus = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], [folders[0]]);
                var expected = new[] { Path.Combine(folders[0], "second.txt"), Path.Combine(folders[0], "third.txt") }
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var timeline = (string)hostType.GetProperty("ShellSelectionRestoreTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(hosts[0])!;
                var deferredSteps = timeline.Split('|').Where(step => step.Contains("source=deferred-refresh", StringComparison.Ordinal)).ToArray();
                var currentMismatchDetected = deferredSteps.Any(step => step.Contains("selection-current matched=False", StringComparison.Ordinal));
                var selectionBatchRestored = deferredSteps.Any(step => step.Contains("selection-batch-api count=2,succeeded=True", StringComparison.Ordinal));
                var selectionPreserved = selected.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
                var focusPreserved = string.Equals(focus, Path.Combine(folders[0], "initial.txt"), StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"shell-deferred-restore-selection-mismatch scheduled={scheduled} settled={settled} current-mismatch-detected={currentMismatchDetected} selection-batch-restored={selectionBatchRestored} selected={string.Join(',', selected.Select(Path.GetFileName))} focus={Path.GetFileName(focus)} selection-preserved={selectionPreserved} focus-preserved={focusPreserved} steps={string.Join('|', deferredSteps)}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return settled && currentMismatchDetected && selectionBatchRestored && selectionPreserved && focusPreserved ? 0 : 1;
            }
            if (args[0] is "--live-shell-refresh-deferred-user-input" or "--live-shell-refresh-deferred-mouse" or "--live-shell-refresh-deferred-watcher-overflow")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the real-refresh input probe.");
                var watcherMode = args[0] is "--live-shell-refresh-deferred-mouse" or "--live-shell-refresh-deferred-watcher-overflow";
                var actualOverflowMode = args[0] == "--live-shell-refresh-deferred-watcher-overflow";
                var watcherInitialCount = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var watcherCreateCount = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var watcherDeleteCount = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var watcherFallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (watcherMode)
                {
                    if (!WaitForPredicate(() => hosts.All(host => (int?)watcherInitialCount.GetValue(host) == 3), TimeSpan.FromSeconds(6)))
                        throw new Exception("The product directory watchers did not establish their initial counts before the multi-pane live-watcher probe.");
                }
                else
                {
                    hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                }
                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");
                var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var inputVersionField = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settledInputField = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var restorePendingField = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var refreshMethod = hostType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public)!;
                var watcherDeleteBefore = hosts.Select(host => (int)watcherDeleteCount.GetValue(host)!).ToArray();
                var watcherCreateBefore = hosts.Select(host => (int)watcherCreateCount.GetValue(host)!).ToArray();
                var watcherFallbackBefore = hosts.Select(host => (int)watcherFallbackCount.GetValue(host)!).ToArray();
                var actualWatcherErrors = 0;
                File.Delete(Path.Combine(folders[0], "initial.txt"));
                refreshMethod.Invoke(hosts[0], null);
                var deferredReached = WaitForPredicate(() => (bool)restorePendingField.GetValue(hosts[0])!, TimeSpan.FromSeconds(5));
                if (!deferredReached)
                    throw new Exception($"Real Shell refresh did not enter deferred focus restoration after the focused item disappeared; count={Count(hosts[0])}, selection-restored={hostType.GetProperty("DirectoryChangeLastRefreshSelectionRestored", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])}, focus-restored={hostType.GetProperty("DirectoryChangeLastRefreshFocusRestored", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])}, count-at-restore={hostType.GetProperty("DirectoryChangeLastRefreshViewCountAtRestore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])}.");

                var inputVersionBefore = (long)inputVersionField.GetValue(hosts[0])!;
                if (args[0] == "--live-shell-refresh-deferred-mouse")
                {
                    SendMessage(viewWindow, 0x0201, (nint)1, (nint)0x00020002); // Left-button down at the hidden view origin.
                    SendMessage(viewWindow, 0x0202, IntPtr.Zero, (nint)0x00020002);
                }
                else
                {
                    SendMessage(viewWindow, 0x0100, (nint)0x10, (nint)1);
                    SendMessage(viewWindow, 0x0101, (nint)0x10, (nint)unchecked((int)0xC0000001));
                }
                var inputRecorded = (long)inputVersionField.GetValue(hosts[0])! > inputVersionBefore;
                if (!inputRecorded) throw new Exception("The hidden Shell view did not route the synthetic input into the product input-generation hook.");
                SelectOnlyItem(hostType, hosts[0], folders[0], "third.txt", takeFocus: true);
                SelectOnlyItem(hostType, hosts[1], folders[1], "second.txt", takeFocus: true);
                if (watcherMode)
                {
                    File.Move(Path.Combine(folders[1], "third.txt"), Path.Combine(folders[1], "renamed-third.txt"));
                    File.Delete(Path.Combine(folders[1], "initial.txt"));
                    var paneThreeWatcher = (FileSystemWatcher)hostType.GetField("_directoryChangeWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[3])!;
                    if (actualOverflowMode)
                    {
                        paneThreeWatcher.Error += (_, _) => Interlocked.Increment(ref actualWatcherErrors);
                    }
                    else
                    {
                        var errorCallback = typeof(FileSystemWatcher).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                            .Single(method => method.Name == "OnError" && method.GetParameters() is [{ ParameterType: var type }] && type == typeof(ErrorEventArgs));
                        errorCallback.Invoke(paneThreeWatcher, [new ErrorEventArgs(new InternalBufferOverflowException("Synthetic watcher overflow path probe."))]);
                    }
                }
                const int primaryPaneBurstSize = 10000;
                const int secondaryPaneBurstSize = 1000;
                int[] paneBurstSizes = actualOverflowMode
                    ? [0, 0, 0, 10000]
                    : [primaryPaneBurstSize, secondaryPaneBurstSize, secondaryPaneBurstSize, secondaryPaneBurstSize];
                var fixtureWrites = new Task[hosts.Count];
                if (watcherMode)
                {
                    for (var pane = 0; pane < hosts.Count; pane++)
                    {
                        var paneIndex = pane;
                        fixtureWrites[pane] = Task.Run(() =>
                        {
                            if (actualOverflowMode && paneIndex == 3)
                            {
                                Parallel.For(0, paneBurstSizes[paneIndex], i =>
                                    File.WriteAllText(Path.Combine(folders[paneIndex], $"extra-{i:D5}.txt"), "extra"));
                                return;
                            }
                            for (var i = 0; i < paneBurstSizes[paneIndex]; i++)
                                File.WriteAllText(Path.Combine(folders[paneIndex], $"extra-{i:D5}.txt"), "extra");
                        });
                    }
                    if (!WaitForPredicate(() => fixtureWrites.All(task => task.IsCompleted), TimeSpan.FromSeconds(60)))
                        throw new TimeoutException("The concurrent pane burst did not finish writing its isolated fixture files.");
                    foreach (var write in fixtureWrites) write.GetAwaiter().GetResult();
                }
                var watcherCreateObserved = !watcherMode || WaitForPredicate(() => hosts.Select((host, pane) =>
                    actualOverflowMode && pane == 3 || (int)watcherCreateCount.GetValue(host)! >= watcherCreateBefore[pane] + paneBurstSizes[pane]).All(observed => observed),
                    TimeSpan.FromSeconds(actualOverflowMode ? 60 : 20));
                var watcherDeleteObserved = !watcherMode || WaitForPredicate(() =>
                    (int)watcherDeleteCount.GetValue(hosts[0])! > watcherDeleteBefore[0] &&
                    (int)watcherDeleteCount.GetValue(hosts[1])! > watcherDeleteBefore[1], TimeSpan.FromSeconds(5));
                bool watcherBatchSettled = true;
                int[] expectedPaneCounts = [2 + paneBurstSizes[0], 2 + paneBurstSizes[1], 3 + paneBurstSizes[2], 3 + paneBurstSizes[3]];
                var refreshCountSettled = watcherMode
                    ? WaitForPredicate(() => hosts.Select(Count).SequenceEqual(expectedPaneCounts), TimeSpan.FromSeconds(actualOverflowMode ? 120 : 30)) && watcherCreateObserved
                    : WaitForPredicate(() => Count(hosts[0]) == 2, TimeSpan.FromSeconds(5));
                if (watcherMode)
                {
                    var paneBatches = hosts.Select(host => hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).ToArray();
                    var pendingDeltas = paneBatches.Select(batch => batch.GetType().GetField("_pendingDelta", BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
                    var expectedCounts = paneBatches.Select(batch => batch.GetType().GetProperty("ExpectedCountAfterChanges", BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
                    watcherBatchSettled = WaitForPredicate(() => paneBatches.Select((batch, pane) =>
                        (int)pendingDeltas[pane].GetValue(batch)! == 0 && (int?)expectedCounts[pane].GetValue(batch) == expectedPaneCounts[pane]).All(settledBatch => settledBatch),
                        TimeSpan.FromSeconds(actualOverflowMode ? 60 : 15));
                }
                var settled = WaitForPredicate(() =>
                    (long)settledInputField.GetValue(hosts[0])! == (long)inputVersionField.GetValue(hosts[0])! &&
                    !(bool)restorePendingField.GetValue(hosts[0])!, TimeSpan.FromSeconds(15));
                var forcedRefresh = hostType.GetProperty("DirectoryChangeLastForcedRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var forcedRefreshObserved = !watcherMode || (actualOverflowMode
                    ? actualWatcherErrors > 0 && (int)watcherFallbackCount.GetValue(hosts[3])! > watcherFallbackBefore[3]
                    : WaitForPredicate(() => (bool)forcedRefresh.GetValue(hosts[1])! && (bool)forcedRefresh.GetValue(hosts[3])!, TimeSpan.FromSeconds(15)));
                WaitForDelay(TimeSpan.FromMilliseconds(1600));
                var selectedAfterInput = SelectedPaths(hostType, hosts[0], folders[0]);
                var focusAfterInput = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], [folders[0]]);
                var selectionPreserved = selectedAfterInput.SequenceEqual([Path.Combine(folders[0], "third.txt")], StringComparer.OrdinalIgnoreCase);
                var focusPreserved = string.Equals(focusAfterInput, Path.Combine(folders[0], "third.txt"), StringComparison.OrdinalIgnoreCase);
                var secondarySelection = SelectedPaths(hostType, hosts[1], folders[1]);
                var secondaryFocus = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[1], [folders[1]]);
                var secondarySelectionPreserved = secondarySelection.SequenceEqual([Path.Combine(folders[1], "second.txt")], StringComparer.OrdinalIgnoreCase);
                var secondaryFocusPreserved = string.Equals(secondaryFocus, Path.Combine(folders[1], "second.txt"), StringComparison.OrdinalIgnoreCase);
                var inputVersion = (long)inputVersionField.GetValue(hosts[0])!;
                var settledInputVersion = (long)settledInputField.GetValue(hosts[0])!;
                var restorePending = (bool)restorePendingField.GetValue(hosts[0])!;
                var fallbackDeltas = hosts.Select((host, pane) => (int)watcherFallbackCount.GetValue(host)! - watcherFallbackBefore[pane]).ToArray();
                if (watcherMode)
                {
                    var finalBatches = hosts.Select(host => hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).ToArray();
                    var finalPendingFields = finalBatches.Select(batch => batch.GetType().GetField("_pendingDelta", BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
                    var finalExpectedProperties = finalBatches.Select(batch => batch.GetType().GetProperty("ExpectedCountAfterChanges", BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
                    watcherBatchSettled = WaitForPredicate(() => finalBatches.Select((batch, pane) =>
                        (int)finalPendingFields[pane].GetValue(batch)! == 0 &&
                        (int?)finalExpectedProperties[pane].GetValue(batch) == expectedPaneCounts[pane]).All(isSettled => isSettled),
                        TimeSpan.FromSeconds(actualOverflowMode ? 30 : 15));
                }
                var pendingByPane = hosts.Select(host =>
                {
                    var batch = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    return (int)batch.GetType().GetField("_pendingDelta", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch)!;
                }).ToArray();
                var expectedByPane = hosts.Select(host =>
                {
                    var batch = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    return batch.GetType().GetProperty("ExpectedCountAfterChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch);
                }).ToArray();
                var overflowBatch = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[3])!;
                var remainingEmptyViewRechecks = (int)overflowBatch.GetType().GetField("_transientEmptyViewRechecksRemaining", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overflowBatch)!;
                var overflowTimer = (DispatcherTimer?)hostType.GetField("_directoryChangeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[3]);
                var watcherErrorStatus = (string?)hostType.GetProperty("LastDirectoryChangeWatcherError", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(hosts[3]);
                var watcherErrorWasRetained = !actualOverflowMode || watcherErrorStatus?.Contains("InternalBufferOverflowException", StringComparison.Ordinal) == true;
                Console.WriteLine($"shell-refresh-deferred-user-input input={(actualOverflowMode ? "keyboard+real-watcher-overflow" : watcherMode ? "mouse+multi-pane-watcher" : "keyboard")} watcher-errors={actualWatcherErrors} watcher-error-retained={watcherErrorWasRetained} watcher-error-status={watcherErrorStatus} pane-bursts={(watcherMode ? string.Join(',', paneBurstSizes) : "0")} input-recorded={inputRecorded} watcher-delete-observed={watcherDeleteObserved} watcher-create-observed={watcherCreateObserved} forced-refresh-observed={forcedRefreshObserved} fallback-deltas={string.Join(',', fallbackDeltas)} pending-deltas={string.Join(',', pendingByPane)} expected-counts={string.Join(',', expectedByPane)} overflow-rechecks-remaining={remainingEmptyViewRechecks} overflow-timer-enabled={overflowTimer?.IsEnabled} final-counts={string.Join(',', hosts.Select(Count))} watcher-batches-settled={watcherBatchSettled} deferred-reached={deferredReached} refresh-counts-settled={refreshCountSettled} settled={settled} restore-pending={restorePending} input-version={inputVersion}/{settledInputVersion} selected={string.Join(',', selectedAfterInput.Select(Path.GetFileName))} focus={Path.GetFileName(focusAfterInput)} selection-preserved={selectionPreserved} focus-preserved={focusPreserved} secondary-selected={string.Join(',', secondarySelection.Select(Path.GetFileName))} secondary-focus={Path.GetFileName(secondaryFocus)} secondary-selection-preserved={secondarySelectionPreserved} secondary-focus-preserved={secondaryFocusPreserved}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return watcherDeleteObserved && refreshCountSettled && watcherCreateObserved && watcherBatchSettled && settled && forcedRefreshObserved &&
                    (!actualOverflowMode || actualWatcherErrors > 0 && watcherErrorWasRetained) && selectionPreserved && focusPreserved && secondarySelectionPreserved && secondaryFocusPreserved ? 0 : 1;
            }
            if (args[0] == "--live-shell-selection-large")
            {
                var probeClock = Stopwatch.StartNew();
                int[] initialCounts = [largeSelectionFixtureItemCount, 3, 3, 3];
                if (!WaitForExpectedCounts(hosts, initialCounts, TimeSpan.FromSeconds(60)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(60)))
                    throw new Exception("Shell views did not finish navigation before the large-selection probe.");
                Console.WriteLine($"shell-selection-large phase=navigation-ready elapsed-ms={probeClock.ElapsedMilliseconds} counts={string.Join(',', hosts.Select(Count))}");
                hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                var populatedCount = largeSelectionFixtureItemCount;
                if (!WaitForExpectedCounts(hosts, [populatedCount, 3, 3, 3], TimeSpan.FromSeconds(60)))
                    throw new Exception("Shell view did not settle the generated large-selection fixture.");
                Console.WriteLine($"shell-selection-large phase=select-all-start elapsed-ms={probeClock.ElapsedMilliseconds}");
                // The product suppresses intermediate selection snapshots until a keyboard command settles.
                // Model that input gate; calling the helper cold would make each Shell callback re-enumerate
                // an ever-growing selection and benchmark an artificial O(n^2) path.
                var selectAllClock = Stopwatch.StartNew();
                hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], null);
                var useNativeMenuSelectAll = Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_SHELL_SELECT_ALL") == "1";
                if (useNativeMenuSelectAll)
                {
                    if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_SHELL_SELECT_ALL_ALL_MODES") == "1")
                        VerifyMenuSelectAllAcrossViewStyles(hostType, hosts, folders);
                    var handled = InvokeMenuSelectAllNative(hostType, hosts[0], out var nativeShortcutMilliseconds);
                    var nativeSelection = SelectedPaths(hostType, hosts[0], folders[0]);
                    Console.WriteLine($"shell-selection-large phase=menu-native-select-all handled={handled} elapsed-ms={nativeShortcutMilliseconds} selected={nativeSelection.Length}");
                    if (nativeSelection.Length != populatedCount)
                        throw new Exception($"Ctrl+A native routing did not select the full view; selected={nativeSelection.Length}, expected={populatedCount}.");
                }
                else SelectAll(hostType, hosts[0]);
                selectAllClock.Stop();
                var deselectClock = Stopwatch.StartNew();
                DeselectItem(hostType, hosts[0], folders[0], "selection-delete-target.txt");
                deselectClock.Stop();
                Console.WriteLine($"shell-selection-large phase-timing select-all-ms={selectAllClock.ElapsedMilliseconds} deselect-target-ms={deselectClock.ElapsedMilliseconds}");
                Console.WriteLine($"shell-selection-large phase=select-all-complete elapsed-ms={probeClock.ElapsedMilliseconds}");
                var selectedBefore = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                Console.WriteLine($"shell-selection-large phase=selection-sampled elapsed-ms={probeClock.ElapsedMilliseconds} selected={selectedBefore.Length}");
                if (selectedBefore.Length != populatedCount - 1)
                    throw new Exception($"Expected {populatedCount - 1} selected items with the preloaded delete target excluded, got {selectedBefore.Length}.");
                var inputVersion = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settledInputVersion = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var selectionInput = (long)inputVersion.GetValue(hosts[0])!;
                if (!WaitForPredicate(() => (long)settledInputVersion.GetValue(hosts[0])! == selectionInput, TimeSpan.FromSeconds(5)))
                    throw new Exception("The large selection did not settle before deletion.");
                var largeSelectionNotificationSnapshot = browserSites[0].GetType()
                    .GetProperty("NotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var callbacksBeforeDelete = largeSelectionNotificationSnapshot.GetValue(browserSites[0]);
                var operationClock = Stopwatch.StartNew();
                ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: false, deleteFrom: "selection-delete-target.txt");
                var deleted = WaitForExpectedCounts(hosts, [populatedCount, 3, 3, 3], TimeSpan.FromSeconds(60));
                WaitForDelay(TimeSpan.FromMilliseconds(500));
                operationClock.Stop();
                var selectedAfter = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var callbacksAfterDelete = largeSelectionNotificationSnapshot.GetValue(browserSites[0]);
                var preserved = selectedBefore.SequenceEqual(selectedAfter, StringComparer.OrdinalIgnoreCase);
                var restoreMilliseconds = hostType.GetProperty("NativeSelectionRestoreMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                var restoreWithinBudget = restoreMilliseconds is long milliseconds && milliseconds >= 0 && milliseconds <= 2000;
                var restoreNotNeeded = restoreMilliseconds is long elapsed && elapsed == -1 && preserved;
                Console.WriteLine($"shell-selection-large count-before={selectedBefore.Length} count-after={selectedAfter.Length} selection-preserved={preserved} delete-visible={deleted} elapsed-ms={operationClock.ElapsedMilliseconds} native-restore-ms={restoreMilliseconds} restore-within-2000ms={restoreWithinBudget} restore-not-needed={restoreNotNeeded} callbacks-before={callbacksBeforeDelete} callbacks-after={callbacksAfterDelete} fallback-count={hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return deleted && preserved && (restoreWithinBudget || restoreNotNeeded) ? 0 : 1;
            }
                if (args[0] is "--live-shell-selection-delete" or "--live-shell-selection-delete-after-input" or "--live-shell-selection-delete-watcher" or "--live-shell-selection-delete-native")
            {
                var countsReady = WaitForExpectedCounts(hosts, [4, 3, 3, 3], TimeSpan.FromSeconds(15));
                var attachedViewsReady = WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10));
                if (!countsReady || !attachedViewsReady)
                {
                    var viewWindowField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var viewWindows = hosts.Select(host => ((nint)viewWindowField.GetValue(host)!).ToString("X")).ToArray();
                    throw new Exception($"Shell views did not finish navigation before the selection-delete probe: counts-ready={countsReady}, counts={string.Join(',', hosts.Select(Count))}, attached-views-ready={attachedViewsReady}, view-hwnds={string.Join(',', viewWindows)}.");
                }
                var watcherMode = args[0] == "--live-shell-selection-delete-watcher";
                var nativeOnlyMode = args[0] == "--live-shell-selection-delete-native";
                if (!watcherMode)
                    hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                var selectionDeletedEvents = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var fallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var deletedEventsBefore = (int)selectionDeletedEvents.GetValue(hosts[0])!;
                var fallbackBefore = (int)fallbackCount.GetValue(hosts[0])!;
                if (watcherMode && !WaitForPredicate(() =>
                        hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]) is int count && count == 4 &&
                        hostType.GetField("_directoryChangeWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]) is FileSystemWatcher watcher && watcher.EnableRaisingEvents,
                        TimeSpan.FromSeconds(8)))
                    throw new Exception("The selection-delete watcher did not establish its four-item baseline.");
                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");
                DeselectItem(hostType, hosts[0], folders[0], "selection-delete-target.txt");
                var deleteName = nativeOnlyMode ? "selection-delete-copy.txt" : "selection-delete-target.txt";
                if (nativeOnlyMode)
                {
                    ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: true, copyName: deleteName);
                    if (!WaitForExpectedCounts(hosts, [5, 3, 3, 3], TimeSpan.FromSeconds(10)))
                        throw new Exception($"The native selection-delete probe copy did not become visible; counts={string.Join(',', hosts.Select(Count))}.");
                    // The Shell can change row selection while inserting a copied item; rebuild the intended fixture selection after enumeration settles.
                    SelectAll(hostType, hosts[0]);
                    DeselectItem(hostType, hosts[0], folders[0], "selection-delete-target.txt");
                    DeselectItem(hostType, hosts[0], folders[0], deleteName);
                    SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");
                }
                var focusMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var selectedBefore = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var expectedSelection = new[] { Path.Combine(folders[0], "second.txt"), Path.Combine(folders[0], "third.txt") }
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (!selectedBefore.SequenceEqual(expectedSelection, StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Selection-delete fixture was not prepared with the two surviving selected rows: {string.Join('|', selectedBefore)}.");
                var lastNativeSelectionField = hostType.GetField("_lastNativeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var inputVersionField = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settledInputVersionField = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                // Direct Shell COM selection bypasses the native-input hook; settle the same two-item baseline a real selection gesture would leave.
                hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(hosts[0], [IntPtr.Zero]);
                var selectionInputVersion = (long)inputVersionField.GetValue(hosts[0])!;
                if (!WaitForPredicate(() =>
                    {
                        var snapshot = lastNativeSelectionField.GetValue(hosts[0]);
                        var count = snapshot is null ? 0 :
                            (int)snapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(snapshot)!;
                        return (long)settledInputVersionField.GetValue(hosts[0])! == selectionInputVersion && count == selectedBefore.Length;
                    }, TimeSpan.FromSeconds(5)))
                    throw new Exception("Selection-delete fixture did not settle its explicitly prepared native selection baseline.");
                var savedSelection = lastNativeSelectionField.GetValue(hosts[0]);
                var savedSelectionCount = savedSelection is null ? 0 :
                    (int)savedSelection.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(savedSelection)!;
                if (savedSelectionCount != selectedBefore.Length)
                    throw new Exception($"Selection-delete fixture has a stale native selection baseline: view={selectedBefore.Length}, saved={savedSelectionCount}.");
                var focusBefore = (string?)focusMethod.Invoke(hosts[0], [folders[0]]);
                var notificationSnapshot = browserSites[0].GetType().GetProperty("NotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var notificationsBeforeDelete = notificationSnapshot.GetValue(browserSites[0]);
                var inputVersionBeforeDelete = (long)inputVersionField.GetValue(hosts[0])!;
                var inputRecorded = args[0] != "--live-shell-selection-delete-after-input";

                if (args[0] == "--live-shell-selection-delete-after-input")
                {
                    var hostWindow = (nint)hostType.GetField("_hostWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                    SendMessage(hostWindow, 0x0100, (nint)0x2E, (nint)1); // Model Delete arriving immediately before the native removal.
                    inputRecorded = (long)inputVersionField.GetValue(hosts[0])! > inputVersionBeforeDelete;
                    var postInputVersion = (long)inputVersionField.GetValue(hosts[0])!;
                    var inputSelectionSettled = inputRecorded && WaitForPredicate(() =>
                    {
                        var snapshot = lastNativeSelectionField.GetValue(hosts[0]);
                        var count = snapshot is null ? 0 :
                            (int)snapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(snapshot)!;
                        return (long)settledInputVersionField.GetValue(hosts[0])! == postInputVersion &&
                            count == expectedSelection.Length;
                    }, TimeSpan.FromSeconds(5));
                    var selectedAfterInput = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (!inputSelectionSettled || !selectedAfterInput.SequenceEqual(expectedSelection, StringComparer.OrdinalIgnoreCase))
                        throw new Exception($"The post-input selection baseline did not settle before deletion: settled={inputSelectionSettled}, selected={string.Join('|', selectedAfterInput.Select(Path.GetFileName))}.");
                }
                var deleteClock = Stopwatch.StartNew();
                ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: false, deleteFrom: deleteName);
                var deleteOperationReturnMilliseconds = deleteClock.ElapsedMilliseconds;
                bool deleted;
                long deleteViewSettledMilliseconds;
                long deleteBatchSettledMilliseconds;
                if (watcherMode)
                {
                    if (!WaitForPredicate(() => (int)selectionDeletedEvents.GetValue(hosts[0])! > deletedEventsBefore,
                            TimeSpan.FromSeconds(5)))
                        throw new Exception("The product watcher did not observe deletion of the seeded selection fixture.");
                    deleted = WaitForExpectedCounts(hosts, [3, 3, 3, 3], TimeSpan.FromSeconds(15));
                    deleteViewSettledMilliseconds = deleteClock.ElapsedMilliseconds;
                    var batch = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                    var pendingDelta = batch.GetType().GetField("_pendingDelta", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var expectedCount = batch.GetType().GetProperty("ExpectedCountAfterChanges", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var batchSettled = WaitForPredicate(() => (int)pendingDelta.GetValue(batch)! == 0 &&
                        (int?)expectedCount.GetValue(batch) == 3, TimeSpan.FromSeconds(8));
                    deleted &= batchSettled;
                    deleteBatchSettledMilliseconds = deleteClock.ElapsedMilliseconds;
                }
                else
                {
                    if (!nativeOnlyMode)
                    {
                        var updatedFolderPath = Marshal.StringToCoTaskMemUni(folders[0]);
                        try { SHChangeNotify(0x00001000, 0x00001005, updatedFolderPath, IntPtr.Zero); }
                        finally { Marshal.FreeCoTaskMem(updatedFolderPath); }
                    }
                    var expectedAfterDelete = nativeOnlyMode ? new[] { 4, 3, 3, 3 } : new[] { 3, 3, 3, 3 };
                    deleted = WaitForExpectedCounts(hosts, expectedAfterDelete, TimeSpan.FromSeconds(15));
                    deleteViewSettledMilliseconds = deleteClock.ElapsedMilliseconds;
                    deleteBatchSettledMilliseconds = deleteViewSettledMilliseconds;
                }
                WaitForDelay(TimeSpan.FromMilliseconds(500));
                var selectedAfterDelete = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusAfterDelete = (string?)focusMethod.Invoke(hosts[0], [folders[0]]);
                var notificationsAfterDelete = notificationSnapshot.GetValue(browserSites[0]);
                var correctDiskState = !File.Exists(Path.Combine(folders[0], deleteName));
                var selectionPreserved = selectedBefore.SequenceEqual(selectedAfterDelete, StringComparer.OrdinalIgnoreCase);
                var fallbackDelta = (int)fallbackCount.GetValue(hosts[0])! - fallbackBefore;
                var fallbackExpected = hostType.GetProperty("DirectoryChangeLastExpectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                var fallbackActual = hostType.GetProperty("DirectoryChangeLastActualCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                var fallbackForced = hostType.GetProperty("DirectoryChangeLastForcedRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                var selectionTimeline = hostType.GetProperty("ShellSelectionCallbackTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                Console.WriteLine($"shell-selection-delete native-monitor={(watcherMode ? "on" : "off")} updatedir-notification={!watcherMode && !nativeOnlyMode} watcher-deleted={(int)selectionDeletedEvents.GetValue(hosts[0])! - deletedEventsBefore} after-input={args[0] == "--live-shell-selection-delete-after-input"} input-recorded={inputRecorded} delete-operation-return-ms={deleteOperationReturnMilliseconds} delete-view-settled-ms={deleteViewSettledMilliseconds} delete-batch-settled-ms={deleteBatchSettledMilliseconds} delete-visible={deleted} disk-correct={correctDiskState} selected-before={string.Join(',', selectedBefore.Select(Path.GetFileName))} selected-after-delete={string.Join(',', selectedAfterDelete.Select(Path.GetFileName))} focus-before={Path.GetFileName(focusBefore)} focus-after-delete={Path.GetFileName(focusAfterDelete)} selection-preserved={selectionPreserved} focus-preserved={string.Equals(focusBefore, focusAfterDelete, StringComparison.OrdinalIgnoreCase)} callbacks-before={notificationsBeforeDelete} callbacks-after-delete={notificationsAfterDelete} selection-timeline={selectionTimeline} fallback-delta={fallbackDelta} fallback-expected={fallbackExpected ?? "none"} fallback-actual={fallbackActual ?? "none"} fallback-forced={fallbackForced}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return deleted && correctDiskState && selectionPreserved && inputRecorded ? 0 : 1;
            }
                if (args[0] == "--live-shell-watcher-recovery")
                {
                    if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                        !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                        throw new Exception("Shell views did not finish navigation before the watcher-recovery probe.");
                    var initialCount = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var watcherField = hostType.GetField("_directoryChangeWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var createdCount = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var fallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    if (!WaitForPredicate(() => initialCount.GetValue(hosts[0]) is int value && value == 3 &&
                            watcherField.GetValue(hosts[0]) is FileSystemWatcher watcher && watcher.EnableRaisingEvents,
                            TimeSpan.FromSeconds(8)))
                        throw new Exception("The production watcher did not establish its three-item baseline.");

                    var firstWatcher = (FileSystemWatcher)watcherField.GetValue(hosts[0])!;
                    var errorHandlerField = typeof(FileSystemWatcher).GetField("_onErrorHandler",
                        BindingFlags.Instance | BindingFlags.NonPublic) ??
                        throw new MissingFieldException(typeof(FileSystemWatcher).FullName, "_onErrorHandler");
                    var errorHandler = (ErrorEventHandler?)errorHandlerField.GetValue(firstWatcher) ??
                        throw new InvalidOperationException("The product watcher has no Error callback.");
                    errorHandler(firstWatcher, new ErrorEventArgs(new InternalBufferOverflowException()));
                    if (!WaitForPredicate(() => !ReferenceEquals(watcherField.GetValue(hosts[0]), firstWatcher),
                            TimeSpan.FromSeconds(5)))
                        throw new Exception("The injected watcher Error did not retire the production watcher.");
                    if (!WaitForPredicate(() => watcherField.GetValue(hosts[0]) is FileSystemWatcher recovered &&
                            recovered.EnableRaisingEvents && !ReferenceEquals(recovered, firstWatcher),
                            TimeSpan.FromSeconds(8)))
                        throw new Exception("The production monitor did not recreate an enabled watcher after Error.");

                    var recoveredWatcher = (FileSystemWatcher)watcherField.GetValue(hosts[0])!;
                    var createdBefore = (int)createdCount.GetValue(hosts[0])!;
                    var fallbackBefore = (int)fallbackCount.GetValue(hosts[0])!;
                    File.WriteAllText(Path.Combine(folders[0], "after-recovery.txt"), "recovered");
                    var watcherObservedChange = WaitForPredicate(() => (int)createdCount.GetValue(hosts[0])! > createdBefore,
                        TimeSpan.FromSeconds(5));
                    var viewRecovered = WaitForExpectedCounts(hosts, [4, 3, 3, 3], TimeSpan.FromSeconds(10));
                    var refreshedWatcher = ReferenceEquals(recoveredWatcher, watcherField.GetValue(hosts[0]));
                    Console.WriteLine($"shell-watcher-recovery error-triggered={!ReferenceEquals(watcherField.GetValue(hosts[0]), firstWatcher)} watcher-enabled={recoveredWatcher.EnableRaisingEvents} watcher-observed-change={watcherObservedChange} view-recovered={viewRecovered} fallback-delta={(int)fallbackCount.GetValue(hosts[0])! - fallbackBefore} counts={string.Join('/', hosts.Select(Count))}");
                    if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                    return watcherObservedChange && viewRecovered && refreshedWatcher && recoveredWatcher.EnableRaisingEvents ? 0 : 1;
                }
                if (args[0] is "--live-shell-product-monitor" or "--live-shell-product-monitor-no-parent-observer" or "--live-shell-product-monitor-early-updatedir" or "--live-shell-product-monitor-early-create" or "--live-shell-product-monitor-early-none" or "--live-shell-product-monitor-early-mkdir" or "--live-shell-product-monitor-early-mkdir-none" or "--live-shell-product-monitor-early-rmdir" or "--live-shell-product-monitor-early-rmdir-none" or "--live-shell-product-monitor-rmdir-boundary" or "--live-shell-product-monitor-early-delete" or "--live-shell-product-monitor-early-delete-none")
            {
                var monitorBaselineCount = args[0] == "--live-shell-product-monitor-rmdir-boundary" ? int.Parse(args[1]) - 1 : 3;
                if (!WaitForExpectedCounts(hosts, new[] { monitorBaselineCount, 3, 3, 3 }, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the product monitor probe.");
                var monitorInitialCount = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (!WaitForPredicate(() => monitorInitialCount.GetValue(hosts[0]) is int, TimeSpan.FromSeconds(6)) ||
                    (int)monitorInitialCount.GetValue(hosts[0])! != monitorBaselineCount)
                    throw new Exception($"The product monitor did not establish the settled {monitorBaselineCount}-item baseline: {monitorInitialCount.GetValue(hosts[0]) ?? "unknown"}.");

                if (args[0] is "--live-shell-product-monitor-early-mkdir" or "--live-shell-product-monitor-early-mkdir-none")
                {
                    var earlyFolderCreatedEvents = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyFolderDeletedEvents = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var fallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var createdBefore = (int)earlyFolderCreatedEvents.GetValue(hosts[0])!;
                    var deletedBefore = (int)earlyFolderDeletedEvents.GetValue(hosts[0])!;
                    var fallbackBefore = (int)fallbackCount.GetValue(hosts[0])!;
                    var operationClock = Stopwatch.StartNew();
                    ShellFixtureOperations.CreateFolder(root, folders[0], "product-created-folder");
                    if (!WaitForPredicate(() => (int)earlyFolderCreatedEvents.GetValue(hosts[0])! > createdBefore, TimeSpan.FromSeconds(5)))
                        throw new Exception("The production watcher did not observe the created-folder fixture.");
                    var watcherSeenMs = operationClock.ElapsedMilliseconds;
                    var countBeforeNotification = Count(hosts[0]);
                    var sendMkdir = args[0] == "--live-shell-product-monitor-early-mkdir";
                    var earlyFolderPath = Marshal.StringToCoTaskMemUni(Path.Combine(folders[0], "product-created-folder"));
                    try
                    {
                        if (sendMkdir) SHChangeNotify(0x00000008, 0x00001005, earlyFolderPath, IntPtr.Zero);
                    }
                    finally { Marshal.FreeCoTaskMem(earlyFolderPath); }
                    var countsWithFolder = new[] { 4, 3, 3, 3 };
                    var visibleWithin250Ms = WaitForExpectedCounts(hosts, countsWithFolder, TimeSpan.FromMilliseconds(250));
                    var visibleAtMs = visibleWithin250Ms ? operationClock.ElapsedMilliseconds : (long?)null;
                    var eventuallyVisible = visibleWithin250Ms || WaitForExpectedCounts(hosts, countsWithFolder, TimeSpan.FromSeconds(4));
                    var fallbackAfterCreate = (int)fallbackCount.GetValue(hosts[0])! - fallbackBefore;
                    ShellFixtureOperations.DeleteEmptyFolder(root, folders[0], "product-created-folder");
                    var deleteObserved = WaitForPredicate(() => (int)earlyFolderDeletedEvents.GetValue(hosts[0])! > deletedBefore, TimeSpan.FromSeconds(5));
                    var cleaned = deleteObserved && WaitForExpectedCounts(hosts, new[] { 3, 3, 3, 3 }, TimeSpan.FromSeconds(5));
                    Console.WriteLine($"early-folder-notification kind={(sendMkdir ? "MKDIR" : "NONE")} stale-before-notify={countBeforeNotification == 3} before-count={countBeforeNotification} watcher-seen-ms={watcherSeenMs} visible-within-250ms={visibleWithin250Ms} visible-at-ms={visibleAtMs?.ToString() ?? "none"} eventually-visible={eventuallyVisible} fallback-count={fallbackAfterCreate} delete-watcher={deleteObserved} cleanup-visible={cleaned} counts={string.Join(',', hosts.Select(Count))}");
                    return countBeforeNotification == 3 && eventuallyVisible && cleaned && IsWindowVisible(source.Handle) == false ? 0 : 1;
                }

                if (args[0] is "--live-shell-product-monitor-early-rmdir" or "--live-shell-product-monitor-early-rmdir-none" or "--live-shell-product-monitor-rmdir-boundary")
                {
                    var earlyRmdirCreatedEvents = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyRmdirDeletedEvents = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyRmdirNativeNotifications = hostType.GetProperty("DirectoryChangeNativeFolderDeleteNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyRmdirLastResult = hostType.GetProperty("DirectoryChangeLastFolderDeleteNotificationResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyRmdirFallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    ShellFixtureOperations.CreateFolder(root, folders[0], "product-rmdir-folder");
                    var expectedDeleteViewCount = args[0] == "--live-shell-product-monitor-rmdir-boundary" ? int.Parse(args[1]) : 4;
                    if (!WaitForExpectedCounts(hosts, new[] { expectedDeleteViewCount, 3, 3, 3 }, TimeSpan.FromSeconds(5)))
                        throw new Exception("The RMDIR probe folder did not become visible before deletion testing.");
                    var rmdirCreatedBefore = (int)earlyRmdirCreatedEvents.GetValue(hosts[0])!;
                    var rmdirDeletedBefore = (int)earlyRmdirDeletedEvents.GetValue(hosts[0])!;
                    var rmdirNativeNotificationsBefore = (int)earlyRmdirNativeNotifications.GetValue(hosts[0])!;
                    var rmdirFallbackBefore = (int)earlyRmdirFallbackCount.GetValue(hosts[0])!;
                    var rmdirClock = Stopwatch.StartNew();
                    ShellFixtureOperations.DeleteEmptyFolder(root, folders[0], "product-rmdir-folder");
                    if (!WaitForPredicate(() => (int)earlyRmdirDeletedEvents.GetValue(hosts[0])! > rmdirDeletedBefore, TimeSpan.FromSeconds(5)))
                        throw new Exception("The production watcher did not observe the deleted-folder fixture.");
                    var rmdirWatcherSeenMs = rmdirClock.ElapsedMilliseconds;
                    var countBeforeRmdirNotification = Count(hosts[0]);
                    var staleDeletedItemKind = ShellViewItemKind(hostType, hosts[0], Path.Combine(folders[0], "product-rmdir-folder"));
                    var sendRmdir = args[0] is "--live-shell-product-monitor-early-rmdir" or "--live-shell-product-monitor-rmdir-boundary";
                    var deletedFolderPath = Marshal.StringToCoTaskMemUni(Path.Combine(folders[0], "product-rmdir-folder"));
                    try
                    {
                        if (sendRmdir) SHChangeNotify(0x00000010, 0x00001005, deletedFolderPath, IntPtr.Zero);
                    }
                    finally { Marshal.FreeCoTaskMem(deletedFolderPath); }
                    var countsAfterDelete = new[] { monitorBaselineCount, 3, 3, 3 };
                    var deletedWithin250Ms = WaitForExpectedCounts(hosts, countsAfterDelete, TimeSpan.FromMilliseconds(250));
                    var deletedAtMs = deletedWithin250Ms ? rmdirClock.ElapsedMilliseconds : (long?)null;
                    var eventuallyDeleted = deletedWithin250Ms || WaitForExpectedCounts(hosts, countsAfterDelete, TimeSpan.FromSeconds(4));
                    var rmdirFallbacks = (int)earlyRmdirFallbackCount.GetValue(hosts[0])! - rmdirFallbackBefore;
                    var rmdirNativeNotifications = (int)earlyRmdirNativeNotifications.GetValue(hosts[0])! - rmdirNativeNotificationsBefore;
                    var deleteWatcherStable = (int)earlyRmdirCreatedEvents.GetValue(hosts[0])! == rmdirCreatedBefore;
                    Console.WriteLine($"early-folder-notification manual-kind={(sendRmdir ? "RMDIR" : "NONE")} expected-delete-view-count={expectedDeleteViewCount} product-rmdir-notifications={rmdirNativeNotifications} product-last-result={earlyRmdirLastResult.GetValue(hosts[0])} stale-before-notify={countBeforeRmdirNotification == expectedDeleteViewCount} stale-item-kind={staleDeletedItemKind} before-count={countBeforeRmdirNotification} watcher-seen-ms={rmdirWatcherSeenMs} visible-within-250ms={deletedWithin250Ms} visible-at-ms={deletedAtMs?.ToString() ?? "none"} eventually-visible={eventuallyDeleted} fallback-count={rmdirFallbacks} create-watcher-stable={deleteWatcherStable} counts={string.Join(',', hosts.Select(Count))}");
                    var automaticFolderNotificationVerified = sendRmdir || rmdirNativeNotifications == 1 &&
                        string.Equals((string?)earlyRmdirLastResult.GetValue(hosts[0]), "Notified", StringComparison.Ordinal);
                    var naturalShellRemovalBeforeGate = rmdirNativeNotifications == 0 &&
                        string.Equals((string?)earlyRmdirLastResult.GetValue(hosts[0]), "not-attempted", StringComparison.Ordinal) &&
                        deletedWithin250Ms && rmdirFallbacks == 0;
                    var boundaryNotificationVerified = args[0] != "--live-shell-product-monitor-rmdir-boundary" ||
                        (expectedDeleteViewCount <= 512
                            ? rmdirNativeNotifications == 1 && string.Equals((string?)earlyRmdirLastResult.GetValue(hosts[0]), "Notified", StringComparison.Ordinal) || naturalShellRemovalBeforeGate
                            : rmdirNativeNotifications == 0 &&
                              ((string.Equals((string?)earlyRmdirLastResult.GetValue(hosts[0]), "SkippedLargeView", StringComparison.Ordinal) && rmdirFallbacks > 0) ||
                               naturalShellRemovalBeforeGate));
                    return countBeforeRmdirNotification == expectedDeleteViewCount && staleDeletedItemKind == "folder" && eventuallyDeleted &&
                        deleteWatcherStable && automaticFolderNotificationVerified && boundaryNotificationVerified && IsWindowVisible(source.Handle) == false ? 0 : 1;
                }

                if (args[0] is "--live-shell-product-monitor-early-delete" or "--live-shell-product-monitor-early-delete-none")
                {
                    var deleteCreatedEvents = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var deleteEvents = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var deleteFallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var deleteNativeRmdirCount = hostType.GetProperty("DirectoryChangeNativeFolderDeleteNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var deleteLastRmdirResult = hostType.GetProperty("DirectoryChangeLastFolderDeleteNotificationResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var targetPath = Path.Combine(folders[0], "product-early-delete.txt");
                    File.Copy(Path.Combine(folders[0], "initial.txt"), targetPath);
                    if (!WaitForExpectedCounts(hosts, new[] { 4, 3, 3, 3 }, TimeSpan.FromSeconds(5)))
                        throw new Exception("The DELETE probe file did not become visible before deletion testing.");
                    var createdBefore = (int)deleteCreatedEvents.GetValue(hosts[0])!;
                    var deletedBefore = (int)deleteEvents.GetValue(hosts[0])!;
                    var fallbackBefore = (int)deleteFallbackCount.GetValue(hosts[0])!;
                    var nativeRmdirBefore = (int)deleteNativeRmdirCount.GetValue(hosts[0])!;
                    var deleteNaturalObservationMilliseconds = 0;
                    if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS") is { } deleteObservationText &&
                        (!int.TryParse(deleteObservationText, out deleteNaturalObservationMilliseconds) || deleteNaturalObservationMilliseconds is < 100 or > 5000))
                        throw new ArgumentOutOfRangeException("EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS", "Use an observation window from 100 to 5000 milliseconds.");
                    var deleteProductTimer = (DispatcherTimer)hostType.GetField("_directoryChangeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                    if (deleteNaturalObservationMilliseconds > 0)
                    {
                        deleteProductTimer.Stop();
                        deleteProductTimer.Interval = TimeSpan.FromMilliseconds(deleteNaturalObservationMilliseconds + 1000);
                    }
                    var deleteClock = Stopwatch.StartNew();
                    File.Delete(targetPath);
                    if (!WaitForPredicate(() => (int)deleteEvents.GetValue(hosts[0])! > deletedBefore, TimeSpan.FromSeconds(5)))
                        throw new Exception("The production watcher did not observe the deleted-file fixture.");
                    var watcherSeenMs = deleteClock.ElapsedMilliseconds;
                    var countBeforeNotify = Count(hosts[0]);
                    var staleItemKind = ShellViewItemKind(hostType, hosts[0], targetPath);
                    long? naturalDeleteVisibleMilliseconds = null;
                    var fallbackDuringDeleteWindow = 0;
                    if (deleteNaturalObservationMilliseconds > 0)
                    {
                        var observationClock = Stopwatch.StartNew();
                        var frame = new DispatcherFrame();
                        var observationTimer = new DispatcherTimer(DispatcherPriority.Background)
                        {
                            Interval = TimeSpan.FromMilliseconds(25)
                        };
                        observationTimer.Tick += (_, _) =>
                        {
                            if (Count(hosts[0]) == 3)
                                naturalDeleteVisibleMilliseconds ??= observationClock.ElapsedMilliseconds;
                            if (observationClock.ElapsedMilliseconds >= deleteNaturalObservationMilliseconds)
                            {
                                observationTimer.Stop();
                                frame.Continue = false;
                            }
                        };
                        observationTimer.Start();
                        Dispatcher.PushFrame(frame);
                        fallbackDuringDeleteWindow = (int)deleteFallbackCount.GetValue(hosts[0])! - fallbackBefore;
                        deleteProductTimer.Stop();
                        deleteProductTimer.Interval = TimeSpan.FromMilliseconds(250);
                        deleteProductTimer.Start();
                    }
                    var sendDelete = args[0] == "--live-shell-product-monitor-early-delete";
                    var nativePath = Marshal.StringToCoTaskMemUni(targetPath);
                    try
                    {
                        // SHCNE_DELETE with SHCNF_PATH | SHCNF_FLUSH; the no-notify twin is the control.
                        if (sendDelete) SHChangeNotify(0x00000002, 0x00001005, nativePath, IntPtr.Zero);
                    }
                    finally { Marshal.FreeCoTaskMem(nativePath); }
                    if (deleteNaturalObservationMilliseconds == 0)
                    {
                        while (deleteClock.ElapsedMilliseconds < 700 && Count(hosts[0]) != 3)
                            Thread.Sleep(10);
                    }
                    var deletedWithin700Ms = deleteNaturalObservationMilliseconds > 0
                        ? naturalDeleteVisibleMilliseconds is <= 700
                        : Count(hosts[0]) == 3;
                    var deletedAtMs = deleteNaturalObservationMilliseconds > 0
                        ? naturalDeleteVisibleMilliseconds is <= 700 ? watcherSeenMs + naturalDeleteVisibleMilliseconds : null
                        : deletedWithin700Ms ? deleteClock.ElapsedMilliseconds : (long?)null;
                    var eventuallyDeleted = deletedWithin700Ms || WaitForExpectedCounts(hosts, new[] { 3, 3, 3, 3 }, TimeSpan.FromSeconds(4));
                    var fallbackDelta = (int)deleteFallbackCount.GetValue(hosts[0])! - fallbackBefore;
                    var nativeRmdirDelta = (int)deleteNativeRmdirCount.GetValue(hosts[0])! - nativeRmdirBefore;
                    var createWatcherStable = (int)deleteCreatedEvents.GetValue(hosts[0])! == createdBefore;
                    Console.WriteLine($"early-file-notification manual-kind={(sendDelete ? "DELETE" : "NONE")} natural-observation-ms={deleteNaturalObservationMilliseconds} natural-delete-visible-ms={naturalDeleteVisibleMilliseconds?.ToString() ?? "none"} fallback-during-natural-window={fallbackDuringDeleteWindow} product-rmdir-notifications={nativeRmdirDelta} product-last-result={deleteLastRmdirResult.GetValue(hosts[0])} stale-before-notify={countBeforeNotify == 4} stale-item-kind={staleItemKind} before-count={countBeforeNotify} watcher-seen-ms={watcherSeenMs} visible-within-700ms={deletedWithin700Ms} visible-at-ms={deletedAtMs?.ToString() ?? "none"} eventually-visible={eventuallyDeleted} fallback-count={fallbackDelta} create-watcher-stable={createWatcherStable} counts={string.Join(',', hosts.Select(Count))}");
                    return countBeforeNotify == 4 && staleItemKind == "file" && nativeRmdirDelta == 0 && eventuallyDeleted && createWatcherStable && fallbackDuringDeleteWindow == 0 && IsWindowVisible(source.Handle) == false ? 0 : 1;
                }

                if (args[0] is "--live-shell-product-monitor-early-updatedir" or "--live-shell-product-monitor-early-create" or "--live-shell-product-monitor-early-none")
                {
                    if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_OPERATION") == "rename")
                    {
                        var renameObservationText = Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS");
                        if (!int.TryParse(renameObservationText, out var renameObservationMilliseconds) || renameObservationMilliseconds is < 100 or > 5000)
                            throw new ArgumentOutOfRangeException("EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS", "Rename observation requires a window from 100 to 5000 milliseconds.");
                        if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_COUNT") is not null)
                            throw new ArgumentException("Natural rename mode cannot be combined with a burst count.");
                        return RunNaturalRenameObservation(hostType, hosts, folders[0], renameObservationMilliseconds, source);
                    }
                    var earlyCreatedEvents = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyDeletedEvents = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var fallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var earlyNativeCreateNotificationCount = hostType.GetProperty("DirectoryChangeNativeCreateNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var fallbackTimeline = hostType.GetProperty("DirectoryChangeFallbackTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var createdBefore = (int)earlyCreatedEvents.GetValue(hosts[0])!;
                    var deletedBefore = (int)earlyDeletedEvents.GetValue(hosts[0])!;
                    var fallbackBefore = (int)fallbackCount.GetValue(hosts[0])!;
                    var nativeCreateBefore = (int)earlyNativeCreateNotificationCount.GetValue(hosts[0])!;
                    var resetEarlyDiagnosticTrace = hostType.GetMethod("ResetDiagnosticTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    foreach (var host in hosts) resetEarlyDiagnosticTrace.Invoke(host, null);
                    CaptureDiagnosticsBaseline();
                    var naturalObservationMilliseconds = 0;
                    if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS") is { } naturalObservationText &&
                        (!int.TryParse(naturalObservationText, out naturalObservationMilliseconds) || naturalObservationMilliseconds is < 100 or > 5000))
                        throw new ArgumentOutOfRangeException("EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS", "Use an observation window from 100 to 5000 milliseconds.");
                    var naturalBurstCount = 0;
                    if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_COUNT") is { } naturalBurstText &&
                        (!int.TryParse(naturalBurstText, out naturalBurstCount) || naturalBurstCount is < 2 or > 32 || naturalObservationMilliseconds == 0))
                        throw new ArgumentOutOfRangeException("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_COUNT", "Use 2 to 32 files with a natural observation window enabled.");
                    var naturalBurstKind = Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_KIND") ?? "filesystem";
                    if (naturalBurstKind is not ("filesystem" or "ifileoperation") ||
                        naturalBurstCount == 0 && Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_KIND") is not null)
                        throw new ArgumentException("Natural burst kind is filesystem or ifileoperation and requires a burst count.");
                    var naturalBurstStageSize = 0;
                    if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_STAGE_SIZE") is { } naturalStageText &&
                        (!int.TryParse(naturalStageText, out naturalBurstStageSize) || naturalBurstStageSize < 1 ||
                         naturalBurstCount == 0 || naturalBurstKind != "ifileoperation" || naturalBurstStageSize >= naturalBurstCount ||
                         naturalBurstCount % naturalBurstStageSize != 0))
                        throw new ArgumentOutOfRangeException("EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_STAGE_SIZE", "Use a smaller divisor of an IFileOperation burst count.");
                    var naturalBurstPaths = Enumerable.Range(0, naturalBurstCount)
                        .Select(index => Path.Combine(folders[0], $"natural-burst-{index:D2}.txt")).ToArray();
                    var naturalBurstStageTrace = new List<string>();
                    DispatcherTimer? delayedCreateNotificationTimer = null;
                    var productDirectoryTimer = (DispatcherTimer)hostType.GetField("_directoryChangeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                    var createNotificationTimerField = hostType.GetField("_directoryChangeCreateNotificationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    if (naturalObservationMilliseconds > 0)
                    {
                        // Hold only this probe's product fallback timer and coalesced SHCNE_CREATE
                        // route so the hosted Shell view gets a bounded natural-update window.
                        if (createNotificationTimerField.GetValue(hosts[0]) is not null)
                            throw new InvalidOperationException("A create-notification timer was already active before the natural-update observation.");
                        productDirectoryTimer.Stop();
                        productDirectoryTimer.Interval = TimeSpan.FromMilliseconds(naturalObservationMilliseconds + 1000);
                        delayedCreateNotificationTimer = new DispatcherTimer(DispatcherPriority.ContextIdle)
                        {
                            Interval = TimeSpan.FromSeconds(30)
                        };
                        createNotificationTimerField.SetValue(hosts[0], delayedCreateNotificationTimer);
                    }
                    var operationClock = Stopwatch.StartNew();
                    if (naturalBurstCount == 0)
                        ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: true, copyName: "product-created.txt");
                    else if (naturalBurstKind == "ifileoperation")
                    {
                        var stageSize = naturalBurstStageSize == 0 ? naturalBurstCount : naturalBurstStageSize;
                        for (var offset = 0; offset < naturalBurstCount; offset += stageSize)
                        {
                            var stagePaths = naturalBurstPaths.Skip(offset).Take(stageSize).ToArray();
                            ShellFixtureOperations.CopyMany(root, folders[0], "initial.txt",
                                stagePaths.Select(path => Path.GetFileName(path)!).ToArray());
                            if (offset + stageSize < naturalBurstCount)
                            {
                                WaitForDelay(TimeSpan.FromMilliseconds(250));
                                var names = Names(hosts[0], 3 + naturalBurstCount);
                                naturalBurstStageTrace.Add($"{offset + stageSize}@{operationClock.ElapsedMilliseconds}:count={Count(hosts[0])},visible-fixtures={naturalBurstPaths.Count(path => names.Contains(Path.GetFileName(path)!, StringComparison.OrdinalIgnoreCase))}");
                            }
                        }
                    }
                    else
                        foreach (var path in naturalBurstPaths) File.WriteAllText(path, "natural burst fixture");
                    var operationMilliseconds = operationClock.ElapsedMilliseconds;
                    var expectedCreatedEvents = createdBefore + Math.Max(1, naturalBurstCount);
                    if (!WaitForPredicate(() => (int)earlyCreatedEvents.GetValue(hosts[0])! >= expectedCreatedEvents, TimeSpan.FromSeconds(5)))
                        throw new Exception($"The production watcher observed fewer Created events than the fixture operation produced: expected at least {Math.Max(1, naturalBurstCount)}.");
                    var watcherMilliseconds = operationClock.ElapsedMilliseconds;
                    var countBeforeNotify = Count(hosts[0]);
                    var expectedCreatedCount = 3 + Math.Max(1, naturalBurstCount);
                    var countsAfterCopy = new[] { expectedCreatedCount, 3, 3, 3 };
                    var expectedCreatedNames = naturalBurstCount == 0
                        ? new[] { "product-created.txt" }
                        : naturalBurstPaths.Select(path => Path.GetFileName(path)!).ToArray();
                    bool CreatedItemsVisible() => Count(hosts[0]) == expectedCreatedCount &&
                        expectedCreatedNames.All(name => Names(hosts[0], expectedCreatedCount).Contains(name, StringComparison.OrdinalIgnoreCase));
                    var browserSiteBeforeNotify = BrowserSiteNotifications();
                    var isCreateNotification = args[0] == "--live-shell-product-monitor-early-create";
                    var isNoNotification = args[0] == "--live-shell-product-monitor-early-none";
                    var notificationPath = isCreateNotification ? Path.Combine(folders[0], "product-created.txt") : folders[0];
                    var notificationPathMemory = Marshal.StringToCoTaskMemUni(notificationPath);
                    var notifyClock = Stopwatch.StartNew();
                    try
                    {
                        if (!isNoNotification)
                            SHChangeNotify(isCreateNotification ? 0x00000002 : 0x00001000, 0x00001005, notificationPathMemory, IntPtr.Zero);
                    }
                    finally { Marshal.FreeCoTaskMem(notificationPathMemory); }
                    var notifyCallMilliseconds = notifyClock.Elapsed.TotalMilliseconds;
                    var notifyAtMilliseconds = operationClock.ElapsedMilliseconds;
                    long? naturalFirstVisibleMilliseconds = null;
                    var naturalObservationFallbacks = 0;
                    if (naturalObservationMilliseconds > 0)
                    {
                        var observationClock = Stopwatch.StartNew();
                        var frame = new DispatcherFrame();
                        var observationTimer = new DispatcherTimer(DispatcherPriority.Background)
                        {
                            Interval = TimeSpan.FromMilliseconds(25)
                        };
                        observationTimer.Tick += (_, _) =>
                        {
                            if (CreatedItemsVisible())
                                naturalFirstVisibleMilliseconds ??= observationClock.ElapsedMilliseconds;
                            if (observationClock.ElapsedMilliseconds >= naturalObservationMilliseconds)
                            {
                                observationTimer.Stop();
                                frame.Continue = false;
                            }
                        };
                        observationTimer.Start();
                        Dispatcher.PushFrame(frame);
                        naturalObservationFallbacks = (int)fallbackCount.GetValue(hosts[0])! - fallbackBefore;
                        productDirectoryTimer.Stop();
                        productDirectoryTimer.Interval = TimeSpan.FromMilliseconds(250);
                        productDirectoryTimer.Start();
                    }
                    var updatedWithin250Ms = naturalObservationMilliseconds == 0
                        ? WaitForExpectedCounts(hosts, countsAfterCopy, TimeSpan.FromMilliseconds(250))
                        : naturalFirstVisibleMilliseconds is <= 250;
                    var updatedWithin250MsAt = naturalObservationMilliseconds == 0
                        ? updatedWithin250Ms ? operationClock.ElapsedMilliseconds : (long?)null
                        : naturalFirstVisibleMilliseconds is <= 250 ? watcherMilliseconds + naturalFirstVisibleMilliseconds : null;
                    var browserSiteAfter250Ms = BrowserSiteNotifications();
                    var viewMessagesAfter250Ms = ViewMessageSamples();
                    var naturallyUpdated = naturalFirstVisibleMilliseconds is not null;
                    var eventuallyUpdated = naturallyUpdated || updatedWithin250Ms ||
                        WaitForPredicate(CreatedItemsVisible, TimeSpan.FromSeconds(4));
                    var firstVisibleMilliseconds = eventuallyUpdated ? operationClock.ElapsedMilliseconds : (long?)null;
                    var fallbackAfterCopy = (int)fallbackCount.GetValue(hosts[0])! - fallbackBefore;
                    var fallbackDuringCopy = fallbackTimeline.GetValue(hosts[0]);
                    if (naturalBurstCount == 0)
                        ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: false, deleteFrom: "product-created.txt");
                    else if (naturalBurstKind == "ifileoperation")
                        ShellFixtureOperations.DeleteMany(root, folders[0], expectedCreatedNames);
                    else
                        foreach (var path in naturalBurstPaths) File.Delete(path);
                    var expectedDeletedEvents = deletedBefore + Math.Max(1, naturalBurstCount);
                    var deleteObserved = WaitForPredicate(() => (int)earlyDeletedEvents.GetValue(hosts[0])! >= expectedDeletedEvents, TimeSpan.FromSeconds(5));
                    var staleDeletedFileKind = ShellViewItemKind(hostType, hosts[0], Path.Combine(folders[0], expectedCreatedNames[0]));
                    var cleanupCounts = Enumerable.Repeat(3, hosts.Count).ToArray();
                    var cleanupVisible = deleteObserved && WaitForPredicate(() =>
                        hosts.Select(Count).SequenceEqual(cleanupCounts) &&
                        expectedCreatedNames.All(name => !Names(hosts[0], expectedCreatedCount).Contains(name, StringComparison.OrdinalIgnoreCase)), TimeSpan.FromSeconds(5));
                    if (delayedCreateNotificationTimer is not null)
                    {
                        delayedCreateNotificationTimer.Stop();
                        createNotificationTimerField.SetValue(hosts[0], null);
                    }
                    var nativeCreateDuringObservation = (int)earlyNativeCreateNotificationCount.GetValue(hosts[0])! - nativeCreateBefore;
                    if (naturalBurstStageTrace.Count > 0)
                        Console.WriteLine($"natural-burst-stages kind={naturalBurstKind} stage-size={naturalBurstStageSize} trace={string.Join('|', naturalBurstStageTrace)}");
                    Console.WriteLine($"early-notification kind={(isCreateNotification ? "CREATE" : isNoNotification ? "NONE" : "UPDATEDIR")} burst-count={naturalBurstCount} burst-kind={naturalBurstKind} created-items-visible={eventuallyUpdated} natural-observation-ms={naturalObservationMilliseconds} natural-first-visible-ms={naturalFirstVisibleMilliseconds?.ToString() ?? "none"} fallback-during-natural-window={naturalObservationFallbacks} app-create-notification-delta={nativeCreateDuringObservation} watcher-created-delta={(int)earlyCreatedEvents.GetValue(hosts[0])! - createdBefore} stale-before-notify={countBeforeNotify == 3} before-count={countBeforeNotify} operation-ms={operationMilliseconds} watcher-seen-ms={watcherMilliseconds} notify-at-ms={notifyAtMilliseconds} notify-call-ms={notifyCallMilliseconds:F1} updated-within-250ms={updatedWithin250Ms} updated-within-250ms-at={updatedWithin250MsAt?.ToString() ?? "none"} first-visible-ms={firstVisibleMilliseconds?.ToString() ?? "none"} fallback-count={fallbackAfterCopy} fallback-timeline={fallbackDuringCopy} browser-site-before-notify={browserSiteBeforeNotify} browser-site-after-250ms={browserSiteAfter250Ms} view-messages-after-250ms={viewMessagesAfter250Ms} deleted-item-kind={staleDeletedFileKind} cleanup-visible={cleanupVisible} counts={string.Join(',', hosts.Select(Count))}");
                    return eventuallyUpdated && cleanupVisible && (naturalBurstCount > 0 || staleDeletedFileKind == "file") &&
                        (naturalObservationMilliseconds == 0 || naturalObservationFallbacks == 0 && nativeCreateDuringObservation == 0) &&
                        IsWindowVisible(source.Handle) == false ? 0 : 1;
                }

                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folders[0], "initial.txt");
                var selectedBefore = SelectedPaths(hostType, hosts[0], folders[0]);
                var focusMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var focusBefore = (string?)focusMethod.Invoke(hosts[0], [folders[0]]);
                if (focusBefore is null || selectedBefore.Contains(focusBefore, StringComparer.OrdinalIgnoreCase))
                    throw new Exception("The product monitor could not establish an unselected focused item before refresh recovery.");
                // Selection and focus are set through Shell COM here, so establish the same
                // saved selection baseline the product would have captured after native input.
                var noteNativeUserInput = hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeInputVersion = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settledNativeInputVersion = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastNativeSelection = hostType.GetField("_lastNativeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
                noteNativeUserInput.Invoke(hosts[0], [IntPtr.Zero]);
                var selectionBaselineInputVersion = (long)nativeInputVersion.GetValue(hosts[0])!;
                if (!WaitForPredicate(() =>
                    {
                        var snapshot = lastNativeSelection.GetValue(hosts[0]);
                        var count = snapshot is null ? 0 :
                            (int)snapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(snapshot)!;
                        return (long)settledNativeInputVersion.GetValue(hosts[0])! == selectionBaselineInputVersion &&
                            count == selectedBefore.Length &&
                            SelectedPaths(hostType, hosts[0], folders[0]).SequenceEqual(selectedBefore, StringComparer.OrdinalIgnoreCase) &&
                            string.Equals((string?)focusMethod.Invoke(hosts[0], [folders[0]]), focusBefore, StringComparison.OrdinalIgnoreCase);
                    }, TimeSpan.FromSeconds(5)))
                    throw new Exception("The product monitor could not settle its explicit selection/focus baseline.");
                var shellNotificationSnapshot = browserSites[0].GetType().GetProperty("NotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var notificationsBeforeCopy = shellNotificationSnapshot.GetValue(browserSites[0]);
                var createdEventCount = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeCreateNotificationCount = hostType.GetProperty("DirectoryChangeNativeCreateNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeFolderDeleteNotificationCount = hostType.GetProperty("DirectoryChangeNativeFolderDeleteNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var refreshCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var createdEventsBefore = (int)createdEventCount.GetValue(hosts[0])!;
                var fallbackBeforeCopy = (int)refreshCount.GetValue(hosts[0])!;
                var resetDiagnosticTrace = hostType.GetMethod("ResetDiagnosticTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var fallbackTimelineProperty = hostType.GetProperty("DirectoryChangeFallbackTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fallbackTimelineProperty is null) throw new Exception("The product host does not expose fallback timestamps for this diagnostic run.");
                foreach (var host in hosts) resetDiagnosticTrace.Invoke(host, null);
                CaptureDiagnosticsBaseline();
                var shellTimelineClock = Stopwatch.StartNew();
                using var shellTimelineObserver = args[0] == "--live-shell-product-monitor"
                    ? new ChangeNotificationObserver(source, root, shellTimelineClock, resolveTargetPaths: false)
                    : null;
                // Exercise the real Shell copy API against the isolated fixture;
                // unlike the earlier File.Copy probe, this follows IFileOperation.
                var copyTimeline = Stopwatch.StartNew();
                ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: true, copyName: "product-created.txt");
                var copyOperationMilliseconds = copyTimeline.ElapsedMilliseconds;
                if (!WaitForPredicate(() => (int)createdEventCount.GetValue(hosts[0])! > createdEventsBefore, TimeSpan.FromSeconds(5)))
                    throw new Exception("The production FileSystemWatcher did not observe the IFileOperation-created file.");
                var watcherObservedMilliseconds = copyTimeline.ElapsedMilliseconds;
                var createdCounts = Enumerable.Repeat(3, hosts.Count).ToArray();
                createdCounts[0] = 4;
                if (!WaitForExpectedCounts(hosts, createdCounts, TimeSpan.FromSeconds(5)))
                    throw new Exception("The production view did not show the IFileOperation-created file.");
                var viewFirstShowMilliseconds = copyTimeline.ElapsedMilliseconds;
                var notificationsWhenCopyVisible = shellNotificationSnapshot.GetValue(browserSites[0]);
                var fallbacksWhenCopyVisible = (int)refreshCount.GetValue(hosts[0])! - fallbackBeforeCopy;

                // Inject a count delta after the real create settles, exercising
                // mismatch, retry, and recovery independently of the copy result.
                WaitForDelay(TimeSpan.FromSeconds(1));
                var notificationsAfterCopy = shellNotificationSnapshot.GetValue(browserSites[0]);
                var copyFallbackDelta = (int)refreshCount.GetValue(hosts[0])! - fallbackBeforeCopy;
                var lastExpectedCount = hostType.GetProperty("DirectoryChangeLastExpectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastActualCount = hostType.GetProperty("DirectoryChangeLastActualCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastForcedRefresh = hostType.GetProperty("DirectoryChangeLastForcedRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var copyFallbackExpected = lastExpectedCount.GetValue(hosts[0]);
                var copyFallbackActual = lastActualCount.GetValue(hosts[0]);
                var copyFallbackForced = lastForcedRefresh.GetValue(hosts[0]);
                var createNotificationTimeline = hostType.GetProperty("DirectoryChangeNativeCreateNotificationTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var createNotificationResult = hostType.GetProperty("DirectoryChangeLastCreateNotificationResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Console.WriteLine($"copy-timeline mode={args[0]} operation-ms={copyOperationMilliseconds} watcher-seen-ms={watcherObservedMilliseconds} view-first-show-ms={viewFirstShowMilliseconds} watcher-created-events={createdEventCount.GetValue(hosts[0])} app-native-create-notifications={nativeCreateNotificationCount.GetValue(hosts[0])} app-create-notification-timeline={createNotificationTimeline.GetValue(hosts[0])} app-create-notification-result={createNotificationResult.GetValue(hosts[0])} parent-shell-timeline={shellTimelineObserver?.Summary ?? "disabled"} shell-notifications-before={notificationsBeforeCopy} shell-notifications-visible={notificationsWhenCopyVisible} fallbacks-visible={fallbacksWhenCopyVisible} shell-notifications-after={notificationsAfterCopy} fallbacks-after={copyFallbackDelta} fallback-timeline={fallbackTimelineProperty.GetValue(hosts[0])} last-expected={copyFallbackExpected ?? "none"} last-actual={copyFallbackActual ?? "none"} last-forced={copyFallbackForced} view-message-samples={ViewMessageSamples()}");
                if ((args[0] is "--live-shell-product-monitor" or "--live-shell-product-monitor-no-parent-observer") && (int)nativeCreateNotificationCount.GetValue(hosts[0])! != 1)
                    throw new Exception("The production watcher did not issue exactly one native Shell create notification for the copied file.");
                var batch = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var expectedAfterMismatchRecovery = batch.GetType().GetProperty("ExpectedCountAfterChanges", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if ((int?)expectedAfterMismatchRecovery.GetValue(batch) != 4)
                    throw new Exception($"The real create batch did not settle at four before mismatch injection: {expectedAfterMismatchRecovery.GetValue(batch) ?? "unknown"}.");
                var mismatchFallbackStart = (int)refreshCount.GetValue(hosts[0])!;
                batch.GetType().GetMethod("RecordChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(batch, [1, false]);
                ((DispatcherTimer)hostType.GetField("_directoryChangeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!).Start();
                if (!WaitForPredicate(() => (int)refreshCount.GetValue(hosts[0])! > mismatchFallbackStart, TimeSpan.FromSeconds(5)))
                    throw new Exception("The product monitor did not invoke the count-mismatch fallback refresh.");
                var refreshInProgress = hostType.GetField("_directoryRefreshInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (!WaitForPredicate(() => (int)refreshCount.GetValue(hosts[0])! > mismatchFallbackStart &&
                    !(bool)refreshInProgress.GetValue(hosts[0])! &&
                    (int?)expectedAfterMismatchRecovery.GetValue(batch) == 4, TimeSpan.FromSeconds(8)))
                    throw new Exception("The product monitor did not settle a transient-empty refresh to the stable four-item view.");
                var recoveredExpectedCount = (int?)expectedAfterMismatchRecovery.GetValue(batch);
                if (recoveredExpectedCount != 4)
                {
                    var readMonitorCount = hostType.GetMethod("ReadDirectoryMonitorItemCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var actualNow = readMonitorCount.Invoke(hosts[0], null);
                    var lastExpected = hostType.GetProperty("DirectoryChangeLastExpectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                    var lastActual = hostType.GetProperty("DirectoryChangeLastActualCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0]);
                    var callbackExpected = batch.GetType().GetProperty("LastRefreshExpectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch);
                    var callbackObserved = batch.GetType().GetProperty("LastRefreshObservedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch);
                    var callbackDrainedDelta = batch.GetType().GetProperty("LastRefreshDrainedDelta", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch);
                    throw new Exception($"The count mismatch retry did not rebase to the settled four-item view: batch={recoveredExpectedCount?.ToString() ?? "null"}, view-now={Count(hosts[0])}, monitor-now={actualNow ?? "null"}, callback-expected={callbackExpected ?? "null"}, callback-observed={callbackObserved ?? "null"}, callback-drained-delta={callbackDrainedDelta}, last-refresh-expected={lastExpected ?? "null"}, last-refresh-actual={lastActual ?? "null"}, fallback-count={refreshCount.GetValue(hosts[0])}, refresh-in-progress={refreshInProgress.GetValue(hosts[0])}, refresh-status={hostType.GetProperty("LastShellRefreshStatus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(hosts[0]) ?? "unknown"}, batch-state={batch.GetType().GetProperty("DiagnosticSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(batch) ?? "unknown"}.");
                }
                var mismatchFallbackDelta = (int)refreshCount.GetValue(hosts[0])! - mismatchFallbackStart;
                if (mismatchFallbackDelta != 2)
                    throw new Exception($"The synthetic mismatch used {mismatchFallbackDelta} fallback refreshes before settling the stable nonempty view instead of one initial refresh plus one bounded retry.");

                // Queue one more watcher delta from another thread as soon as
                // the next fallback is in progress. This races the async refresh
                // completion against its lower-priority ContextIdle drain.
                var inFlightFallbackStart = (int)refreshCount.GetValue(hosts[0])!;
                batch.GetType().GetMethod("RecordChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(batch, [1, false]);
                var queue = hostType.GetField("_directoryChangeQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var directoryChangeVersion = hostType.GetField("_directoryChangeVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var queueDirectoryChange = hostType.GetMethod("QueueDirectoryChange", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var inFlightInjection = Task.Run(() =>
                {
                    var deadline = Stopwatch.StartNew();
                    while (!(bool)refreshInProgress.GetValue(hosts[0])!)
                    {
                        if (deadline.Elapsed > TimeSpan.FromSeconds(6)) return false;
                        Thread.Sleep(1);
                    }
                    queueDirectoryChange.Invoke(hosts[0], [directoryChangeVersion.GetValue(hosts[0])!, queue, -1, false]);
                    return true;
                });
                ((DispatcherTimer)hostType.GetField("_directoryChangeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!).Start();
                if (!WaitForPredicate(() => (int)refreshCount.GetValue(hosts[0])! > inFlightFallbackStart,
                    TimeSpan.FromSeconds(5)) ||
                    !WaitForPredicate(() => inFlightInjection.IsCompleted, TimeSpan.FromSeconds(5)) ||
                    !inFlightInjection.GetAwaiter().GetResult())
                    throw new Exception("Could not queue a watcher delta during the production fallback refresh.");
                if (!WaitForPredicate(() => !(bool)refreshInProgress.GetValue(hosts[0])! &&
                    (int?)expectedAfterMismatchRecovery.GetValue(batch) == 4, TimeSpan.FromSeconds(5)))
                    throw new Exception("The in-flight watcher delta did not reconcile to the settled count.");
                WaitForDelay(TimeSpan.FromSeconds(2));
                var inFlightFallbackDelta = (int)refreshCount.GetValue(hosts[0])! - inFlightFallbackStart;
                if (inFlightFallbackDelta != 1)
                    throw new Exception($"A watcher delta queued during Refresh caused {inFlightFallbackDelta} fallback refreshes.");
                var inFlightDrainedDelta = (int)batch.GetType().GetProperty("LastRefreshDrainedDelta", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch)!;
                // ContextIdle may drain the queued delta before Refresh's Background
                // completion callback, so assert the reconciled batch/count invariant
                // above rather than requiring one specific drain site to observe it.

                var selectedAfterRefresh = SelectedPaths(hostType, hosts[0], folders[0]);
                var focusAfterRefresh = (string?)focusMethod.Invoke(hosts[0], [folders[0]]);
                var lastRefreshExpected = batch.GetType().GetProperty("LastRefreshExpectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch);
                var lastRefreshObserved = batch.GetType().GetProperty("LastRefreshObservedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch);
                var selectionRestored = selectedBefore.SequenceEqual(selectedAfterRefresh, StringComparer.OrdinalIgnoreCase);
                var focusRestored = string.Equals(focusBefore, focusAfterRefresh, StringComparison.OrdinalIgnoreCase);

                var targetCreatedEventCount = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var deletedEventCount = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var sourceCreatedEventsBeforeCrossPaneCopy = (int)createdEventCount.GetValue(hosts[0])!;
                var targetCreatedEventsBeforeCrossPaneCopy = (int)targetCreatedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.CopyBetweenFixtures(root, folders[0], "initial.txt", folders[1], "cross-pane-copy.txt");
                var crossPaneTargetWatcherObserved = WaitForPredicate(() =>
                    (int)targetCreatedEventCount.GetValue(hosts[1])! > targetCreatedEventsBeforeCrossPaneCopy, TimeSpan.FromSeconds(5));
                var crossPaneCounts = new[] { 4, 4, 3, 3 };
                var crossPaneCopyVisible = crossPaneTargetWatcherObserved && WaitForExpectedCounts(hosts, crossPaneCounts, TimeSpan.FromSeconds(5));
                var sourceWatcherUnchanged = (int)createdEventCount.GetValue(hosts[0])! == sourceCreatedEventsBeforeCrossPaneCopy;
                var deletedEventsBeforeCrossPaneCopy = (int)deletedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.Run(root, new[] { folders[1] }, copy: false, deleteFrom: "cross-pane-copy.txt");
                var crossPaneDeleteObserved = WaitForPredicate(() =>
                    (int)deletedEventCount.GetValue(hosts[1])! > deletedEventsBeforeCrossPaneCopy, TimeSpan.FromSeconds(5));
                var crossPaneDeleteCounts = new[] { 4, 3, 3, 3 };
                var crossPaneDeleteVisible = crossPaneDeleteObserved && WaitForExpectedCounts(hosts, crossPaneDeleteCounts, TimeSpan.FromSeconds(5));

                ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: true, copyName: "cross-pane-move-source.txt");
                var moveSourceCreatedCounts = new[] { 5, 3, 3, 3 };
                if (!WaitForExpectedCounts(hosts, moveSourceCreatedCounts, TimeSpan.FromSeconds(5)))
                    throw new Exception("The Shell move source fixture did not appear in its source pane.");
                var sourceDeletedBeforeCrossPaneMove = (int)deletedEventCount.GetValue(hosts[0])!;
                var targetCreatedBeforeCrossPaneMove = (int)targetCreatedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.MoveBetweenFixtures(root, folders[0], "cross-pane-move-source.txt", folders[1], "cross-pane-moved.txt");
                var crossPaneMoveSourceWatcher = WaitForPredicate(() =>
                    (int)deletedEventCount.GetValue(hosts[0])! > sourceDeletedBeforeCrossPaneMove, TimeSpan.FromSeconds(5));
                var crossPaneMoveTargetWatcher = WaitForPredicate(() =>
                    (int)targetCreatedEventCount.GetValue(hosts[1])! > targetCreatedBeforeCrossPaneMove, TimeSpan.FromSeconds(5));
                var crossPaneMoveCounts = new[] { 4, 4, 3, 3 };
                var crossPaneMoveVisible = crossPaneMoveSourceWatcher && crossPaneMoveTargetWatcher &&
                    WaitForExpectedCounts(hosts, crossPaneMoveCounts, TimeSpan.FromSeconds(5)) &&
                    !Names(hosts[0]).Contains("cross-pane-move-source.txt", StringComparison.OrdinalIgnoreCase) &&
                    Names(hosts[1]).Contains("cross-pane-moved.txt", StringComparison.OrdinalIgnoreCase);
                var deletedEventsBeforeCrossPaneMove = (int)deletedEventCount.GetValue(hosts[1])!;
                ShellFixtureOperations.Run(root, new[] { folders[1] }, copy: false, deleteFrom: "cross-pane-moved.txt");
                var crossPaneMoveCleanupWatcher = WaitForPredicate(() =>
                    (int)deletedEventCount.GetValue(hosts[1])! > deletedEventsBeforeCrossPaneMove, TimeSpan.FromSeconds(5));
                var crossPaneMoveCleanupCounts = new[] { 4, 3, 3, 3 };
                var crossPaneMoveCleanupVisible = crossPaneMoveCleanupWatcher &&
                    WaitForExpectedCounts(hosts, crossPaneMoveCleanupCounts, TimeSpan.FromSeconds(5));

                var countBeforeRenameRefresh = (int)refreshCount.GetValue(hosts[0])!;
                ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: false,
                    moveFrom: "product-created.txt", moveName: "product-renamed.txt");
                var renamed = WaitForPredicate(() => Names(hosts[0]).Contains("product-renamed.txt", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
                var renameFallback = WaitForPredicate(() => (int)refreshCount.GetValue(hosts[0])! > countBeforeRenameRefresh,
                    TimeSpan.FromSeconds(5));
                var renameForcedRefresh = renameFallback && (bool)hostType.GetProperty("DirectoryChangeLastForcedRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var deletedEventsBefore = (int)deletedEventCount.GetValue(hosts[0])!;
                ShellFixtureOperations.Run(root, new[] { folders[0] }, copy: false, deleteFrom: "product-renamed.txt");
                var deleteWatcherObserved = WaitForPredicate(() => (int)deletedEventCount.GetValue(hosts[0])! > deletedEventsBefore,
                    TimeSpan.FromSeconds(5));
                var deletedCounts = Enumerable.Repeat(3, hosts.Count).ToArray();
                var deleted = deleteWatcherObserved && WaitForExpectedCounts(hosts, deletedCounts, TimeSpan.FromSeconds(5));
                var folderCreateEventsBefore = (int)createdEventCount.GetValue(hosts[0])!;
                var folderDeleteEventsBefore = (int)deletedEventCount.GetValue(hosts[0])!;
                var folderCreateFallbackBefore = (int)refreshCount.GetValue(hosts[0])!;
                var nativeCreateNotificationsBeforeFolder = (int)nativeCreateNotificationCount.GetValue(hosts[0])!;
                var folderPath = Path.Combine(folders[0], "product-created-folder");
                ShellFixtureOperations.CreateFolder(root, folders[0], "product-created-folder");
                var folderCreateWatcher = WaitForPredicate(() =>
                    (int)createdEventCount.GetValue(hosts[0])! > folderCreateEventsBefore, TimeSpan.FromSeconds(5));
                var folderCounts = new[] { 4, 3, 3, 3 };
                var folderCreatedVisible = folderCreateWatcher && WaitForExpectedCounts(hosts, folderCounts, TimeSpan.FromSeconds(5));
                WaitForDelay(TimeSpan.FromMilliseconds(100));
                var folderCreateFallbacks = (int)refreshCount.GetValue(hosts[0])! - folderCreateFallbackBefore;
                var folderUsedFileCreateNotification =
                    (int)nativeCreateNotificationCount.GetValue(hosts[0])! != nativeCreateNotificationsBeforeFolder;
                var folderDeleteFallbackBefore = (int)refreshCount.GetValue(hosts[0])!;
                var folderDeleteNotificationBefore = (int)nativeFolderDeleteNotificationCount.GetValue(hosts[0])!;
                ShellFixtureOperations.DeleteEmptyFolder(root, folders[0], "product-created-folder");
                var folderDeleteWatcher = WaitForPredicate(() =>
                    (int)deletedEventCount.GetValue(hosts[0])! > folderDeleteEventsBefore, TimeSpan.FromSeconds(5));
                var folderDeletedVisible = folderDeleteWatcher && WaitForExpectedCounts(hosts, deletedCounts, TimeSpan.FromSeconds(5));
                var folderDeleteFallbacks = (int)refreshCount.GetValue(hosts[0])! - folderDeleteFallbackBefore;
                var folderDeleteNotifications = (int)nativeFolderDeleteNotificationCount.GetValue(hosts[0])! - folderDeleteNotificationBefore;
                Console.WriteLine($"product-directory-monitor created={string.Join(',', hosts.Select(Count))} fallback-refreshes={refreshCount.GetValue(hosts[0])} mismatch-fallbacks={mismatchFallbackDelta} in-flight-fallbacks={inFlightFallbackDelta} last-refresh-expected={lastRefreshExpected ?? "null"} last-refresh-observed={lastRefreshObserved ?? "null"} last-refresh-drained-delta={inFlightDrainedDelta} mismatch-recovered-count={recoveredExpectedCount} selection-restored={selectionRestored} focus-restored={focusRestored} shell-cross-pane-copy-visible={crossPaneCopyVisible} shell-cross-pane-target-watcher={crossPaneTargetWatcherObserved} shell-cross-pane-source-watcher-unchanged={sourceWatcherUnchanged} shell-cross-pane-delete-visible={crossPaneDeleteVisible} shell-cross-pane-delete-watcher={crossPaneDeleteObserved} shell-cross-pane-move-visible={crossPaneMoveVisible} shell-cross-pane-move-source-deleted={crossPaneMoveSourceWatcher} shell-cross-pane-move-target-created={crossPaneMoveTargetWatcher} shell-cross-pane-move-cleanup-visible={crossPaneMoveCleanupVisible} shell-move-visible={renamed} shell-move-fallback={renameFallback} shell-move-forced-refresh={renameForcedRefresh} shell-delete-watcher-event={deleteWatcherObserved} shell-delete-visible={deleted} folder-create-watcher={folderCreateWatcher} folder-create-visible={folderCreatedVisible} folder-create-fallbacks={folderCreateFallbacks} folder-create-notified-as-file={folderUsedFileCreateNotification} folder-delete-watcher={folderDeleteWatcher} folder-delete-visible={folderDeletedVisible} folder-delete-fallbacks={folderDeleteFallbacks} folder-delete-native-rmdir-notifications={folderDeleteNotifications} names={Names(hosts[0])} target-names={Names(hosts[1])}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return selectionRestored && focusRestored && crossPaneCopyVisible && sourceWatcherUnchanged && crossPaneDeleteVisible &&
                    crossPaneMoveVisible && crossPaneMoveCleanupVisible && renamed && renameFallback && renameForcedRefresh && deleted &&
                    folderCreatedVisible && !folderUsedFileCreateNotification && folderDeletedVisible ? 0 : 1;
            }
            if (args[0] is "--live-shell-directory-stress" or "--live-shell-directory-stress-native" or "--live-shell-directory-stress-large")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)) ||
                    !WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell views did not finish navigation before the directory stress probe.");
                var productMonitorEnabled = args[0] is "--live-shell-directory-stress" or "--live-shell-directory-stress-large";
                if (!productMonitorEnabled)
                    hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hosts[0], null);
                var initialMonitorCount = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var startSampleCount = hostType.GetProperty("DirectoryChangeStartSampleCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (productMonitorEnabled && !WaitForPredicate(() => initialMonitorCount.GetValue(hosts[0]) is int,
                    TimeSpan.FromSeconds(6)))
                    throw new Exception("The product directory monitor did not obtain an initial Shell count.");
                var monitorStartCount = initialMonitorCount.GetValue(hosts[0]);
                var monitorStartSamples = startSampleCount.GetValue(hosts[0]);
                WriteDirectoryStressMemoryCheckpoint("four-pane-ready");

                var folder = folders[0];
                SelectAll(hostType, hosts[0]);
                SetUnselectedFocus(hostType, hosts[0], folder, "initial.txt");
                var selectedBefore = SelectedPaths(hostType, hosts[0], folder)
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var expectedSelection = new[] { "second.txt", "third.txt" }
                    .Select(name => Path.Combine(folder, name)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var focusBefore = (string?)focusMethod.Invoke(hosts[0], [folder]);
                if (!selectedBefore.SequenceEqual(expectedSelection, StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(focusBefore, Path.Combine(folder, "initial.txt"), StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Could not establish the stress probe's multiselection and independent focus item.");

                // Direct Shell selection calls do not pass through the product's native-input hook; settle a matching snapshot before exercising refresh recovery.
                // Reflection does not apply C# optional defaults, so pass the native window argument explicitly.
                var noteNativeUserInput = hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeInputVersion = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var settledNativeInputVersion = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastNativeSelection = hostType.GetField("_lastNativeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
                noteNativeUserInput.Invoke(hosts[0], [IntPtr.Zero]);
                var baselineInputVersion = (long)nativeInputVersion.GetValue(hosts[0])!;
                var selectionBaselineSettled = WaitForPredicate(() =>
                {
                    if ((long)settledNativeInputVersion.GetValue(hosts[0])! != baselineInputVersion) return false;
                    var snapshot = lastNativeSelection.GetValue(hosts[0]);
                    return snapshot is not null && (int)snapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(snapshot)! == selectedBefore.Length;
                }, TimeSpan.FromSeconds(5));
                if (!selectionBaselineSettled)
                    throw new Exception("The stress probe could not capture the explicitly prepared selection as the product's current input baseline.");

                // Shell may transiently add the focused row to a confirmed multi-selection during a view rebuild.
                // Exercise that callback directly so the recovery gate is tested independently of timing luck.
                SetSelectedFocus(hostType, hosts[0], folder, "initial.txt");
                var transientSelectionReady = WaitForPredicate(() =>
                    SelectedPaths(hostType, hosts[0], folder).Length == 3, TimeSpan.FromSeconds(2));
                if (!transientSelectionReady)
                    throw new Exception("The probe could not prepare the transient focused-row selection.");
                InvokeShellSelectionChanged(hostType, hosts[0]);
                var savedSnapshot = lastNativeSelection.GetValue(hosts[0]);
                var savedSnapshotCount = savedSnapshot is null ? 0 :
                    (int)savedSnapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(savedSnapshot)!;
                if (savedSnapshotCount != selectedBefore.Length)
                    throw new Exception($"The transient Shell focused-row selection replaced the confirmed snapshot: {savedSnapshotCount}.");
                SetUnselectedFocus(hostType, hosts[0], folder, "initial.txt");
                var baselineSelectionRestored = WaitForPredicate(() =>
                    SelectedPaths(hostType, hosts[0], folder).Order(StringComparer.OrdinalIgnoreCase)
                        .SequenceEqual(selectedBefore, StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(2));
                if (!baselineSelectionRestored)
                    throw new Exception("The probe could not restore the original multiselection after the transient callback check.");
                InvokeShellSelectionChanged(hostType, hosts[0]);

                var resetDiagnosticTrace = hostType.GetMethod("ResetDiagnosticTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (var host in hosts) resetDiagnosticTrace.Invoke(host, null);
                var shellSiteType = browserSites[0].GetType();
                shellSiteType.GetMethod("ResetStateChangeTrace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browserSites[0], null);
                var diagnosticStartTicks = (long)hostType.GetField("_nativeUserMessageTraceStart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var shellNotificationSnapshot = shellSiteType
                    .GetProperty("NotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var shellStateChangeTrace = shellSiteType
                    .GetProperty("StateChangeTraceSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var shellSelectionVersion = shellSiteType
                    .GetProperty("SelectionVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var shellNotificationsAtStressStart = shellNotificationSnapshot.GetValue(browserSites[0]);
                var shellSelectionVersionAtStressStart = shellSelectionVersion.GetValue(browserSites[0]);
                using var messagePumpTrace = Environment.GetEnvironmentVariable("EXPLORER_DIRS_MESSAGE_PUMP_TRACE") == "1"
                    ? new ThreadMessagePumpTraceProbe()
                    : null;
                var callbackTimingSnapshot = browserSites[0].GetType()
                    .GetProperty("CallbackTimingSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var viewMessageTimingSnapshot = hostType
                    .GetProperty("NativeViewMessageTimingSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var viewMessageSlowTimelineSnapshot = hostType
                    .GetProperty("NativeViewMessageSlowTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directoryRefreshCallTimelineSnapshot = hostType
                    .GetProperty("DirectoryRefreshCallTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directoryChangeStageTimingSnapshot = hostType
                    .GetProperty("DirectoryChangeStageTimingSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;

                var burstSize = args.Length > 1 && int.TryParse(args[1], out var configuredBurstSize)
                    ? configuredBurstSize
                    : args[0] == "--live-shell-directory-stress-large" ? 10000 : 3000;
                if (burstSize is < 1 or > 50000)
                    throw new ArgumentOutOfRangeException(nameof(args), "The directory stress burst must be between 1 and 50,000 files.");
                var fallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var fallbackStartTicksField = hostType.GetField("_directoryChangeFallbackStartedAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var fallbackCompleteTicksField = hostType.GetField("_directoryChangeFallbackCompletedAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var folderDeleteLookupCount = hostType.GetProperty("DirectoryChangeFolderDeleteLookupCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastFolderDeleteLookupResult = hostType.GetProperty("DirectoryChangeLastFolderDeleteNotificationResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastExpectedCount = hostType.GetProperty("DirectoryChangeLastExpectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastActualCount = hostType.GetProperty("DirectoryChangeLastActualCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastForcedRefresh = hostType.GetProperty("DirectoryChangeLastForcedRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastRefreshSelectedCount = hostType.GetProperty("DirectoryChangeLastRefreshSelectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastRefreshHadFocus = hostType.GetProperty("DirectoryChangeLastRefreshHadFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastRefreshRestoreMilliseconds = hostType.GetProperty("DirectoryChangeLastRefreshRestoreMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastRefreshViewCountAtRestore = hostType.GetProperty("DirectoryChangeLastRefreshViewCountAtRestore", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastRefreshSelectionRestored = hostType.GetProperty("DirectoryChangeLastRefreshSelectionRestored", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lastRefreshFocusRestored = hostType.GetProperty("DirectoryChangeLastRefreshFocusRestored", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var shellSelectionCallbackTimeline = hostType.GetProperty("ShellSelectionCallbackTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var shellSelectionRestoreTimeline = hostType.GetProperty("ShellSelectionRestoreTimelineSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeSelectionRestoreMilliseconds = hostType.GetProperty("NativeSelectionRestoreMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directoryRefreshInProgress = hostType.GetField("_directoryRefreshInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directorySelectionRestorePending = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directoryChangeBatchField = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directoryChangeQueueField = hostType.GetField("_directoryChangeQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var changeBatchType = assembly.GetType("ExplorerDirs.App.ShellDirectoryChangeBatch", true)!;
                var changeQueueType = assembly.GetType("ExplorerDirs.App.ShellDirectoryChangeQueue", true)!;
                var batchDiagnosticSnapshot = changeBatchType.GetProperty("DiagnosticSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var queueDiagnosticSnapshot = changeQueueType.GetProperty("DiagnosticSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var productionCreatedEvents = hostType.GetProperty("DirectoryChangeCreatedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var productionDeletedEvents = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var nativeCreateNotifications = hostType.GetProperty("DirectoryChangeNativeCreateNotificationCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                string LastFallbackDetails() => $"expected={lastExpectedCount.GetValue(hosts[0]) ?? "unknown"},actual={lastActualCount.GetValue(hosts[0]) ?? "unknown"},forced={lastForcedRefresh.GetValue(hosts[0])}";
                string MonitorStateAtFallback()
                {
                    var batch = directoryChangeBatchField.GetValue(hosts[0]);
                    var queue = directoryChangeQueueField.GetValue(hosts[0]);
                    return $"created-events={productionCreatedEvents.GetValue(hosts[0])},deleted-events={productionDeletedEvents.GetValue(hosts[0])},batch={batchDiagnosticSnapshot.GetValue(batch) ?? "none"},queue={queueDiagnosticSnapshot.GetValue(queue) ?? "none"}";
                }
                var dispatcherClock = Stopwatch.StartNew();
                using var dispatcherQueueLatencyProbe = Environment.GetEnvironmentVariable("EXPLORER_DIRS_DISPATCHER_QUEUE_LATENCY_TRACE") == "1"
                    ? new DispatcherQueueLatencyProbe(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(250))
                    : null;
                var lastHeartbeat = 0L;
                var maxHeartbeatGap = 0L;
                var maxHeartbeatWork = 0L;
                var maxCountRead = 0L;
                var maxSelectionRead = 0L;
                var maxFocusRead = 0L;
                var dispatcherThreadId = GetWindowThreadProcessId(hosts[0].Handle, out _);
                using var dedicatedHeartbeat = new DedicatedThreadHeartbeatProbe(dispatcherThreadId);
                var dispatcherPriorityGaps = new Dictionary<DispatcherPriority, long>
                {
                    [DispatcherPriority.Normal] = 0,
                    [DispatcherPriority.Input] = 0,
                    [DispatcherPriority.ContextIdle] = 0
                };
                var dispatcherPriorityLastTicks = dispatcherPriorityGaps.Keys.ToDictionary(priority => priority, _ => 0L);
                var dispatcherPriorityTickCounts = dispatcherPriorityGaps.Keys.ToDictionary(priority => priority, _ => 0);
                var dispatcherPriorityMaxGapAt = dispatcherPriorityGaps.Keys.ToDictionary(priority => priority, _ => 0d);
                var dispatcherPriorityMaxGapFromTicks = dispatcherPriorityGaps.Keys.ToDictionary(priority => priority, _ => 0L);
                var dispatcherPriorityMaxGapToTicks = dispatcherPriorityGaps.Keys.ToDictionary(priority => priority, _ => 0L);
                var dispatcherPriorityTimers = new List<DispatcherTimer>();
                var dispatcherGapTimeline = new List<string>();
                var fallbackTimeline = new List<string>();
                var directoryCountTimeline = new List<string>();
                var refreshCaptureTimeline = new List<string>();
                var refreshCompletionTimeline = new List<string>();
                var selectionTimeline = new List<string> { $"t=0ms,count={selectedBefore.Length},focus={Path.GetFileName(focusBefore)}" };
                var lastSelectionSampleAt = 0L;
                var lastDirectoryCountSampleAt = -250L;
                var targetCounts = Enumerable.Repeat(3, hosts.Count).ToArray();
                targetCounts[0] += burstSize;
                var diagnosticTargetCount = targetCounts[0];
                Task? activeOperation = null;
                var lastObservedFallbackCount = 0;
                var lastObservedRefreshCompletionCount = 0;
                var fallbackAtMs = -1L;
                bool? operationCompleteAtFallback = null;
                var heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
                heartbeat.Tick += (_, _) =>
                {
                    var heartbeatStarted = Stopwatch.GetTimestamp();
                    var now = dispatcherClock.ElapsedMilliseconds;
                    maxHeartbeatGap = Math.Max(maxHeartbeatGap, now - lastHeartbeat);
                    lastHeartbeat = now;
                    var currentFallbackCount = (int)fallbackCount.GetValue(hosts[0])!;
                    if (currentFallbackCount > lastObservedFallbackCount)
                    {
                        fallbackAtMs = now;
                        operationCompleteAtFallback = activeOperation?.IsCompleted;
                        var fallbackStartedAt = fallbackStartTicksField.GetValue(hosts[0]);
                        var fallbackCompletedAt = fallbackCompleteTicksField.GetValue(hosts[0]);
                        fallbackTimeline.Add($"t={now}ms,probe-ticks={heartbeatStarted},fallback-start-ticks={fallbackStartedAt},fallback-complete-ticks={fallbackCompletedAt},count={currentFallbackCount},expected={lastExpectedCount.GetValue(hosts[0]) ?? "unknown"},actual={lastActualCount.GetValue(hosts[0]) ?? "unknown"},forced={lastForcedRefresh.GetValue(hosts[0])},operation-complete={operationCompleteAtFallback},{MonitorStateAtFallback()}");
                        refreshCaptureTimeline.Add($"fallback={currentFallbackCount},selected={lastRefreshSelectedCount.GetValue(hosts[0])},focus={lastRefreshHadFocus.GetValue(hosts[0])}");
                        lastObservedFallbackCount = currentFallbackCount;
                    }
                    var refreshInProgress = (bool)directoryRefreshInProgress.GetValue(hosts[0])!;
                    if (!refreshInProgress && currentFallbackCount > lastObservedRefreshCompletionCount)
                    {
                        var completionCountReadStartedAt = Stopwatch.GetTimestamp();
                        var completionViewCount = Count(hosts[0]);
                        var completionCountReadEndedAt = Stopwatch.GetTimestamp();
                        refreshCompletionTimeline.Add($"t={now}ms,probe-ticks={heartbeatStarted},fallback-start-ticks={fallbackStartTicksField.GetValue(hosts[0])},fallback-complete-ticks={fallbackCompleteTicksField.GetValue(hosts[0])},count-read-start-ticks={completionCountReadStartedAt},count-read-end-ticks={completionCountReadEndedAt},fallback={currentFallbackCount},view-count={completionViewCount},captured={lastRefreshSelectedCount.GetValue(hosts[0])}/{lastRefreshHadFocus.GetValue(hosts[0])},restored={lastRefreshSelectionRestored.GetValue(hosts[0])}/{lastRefreshFocusRestored.GetValue(hosts[0])},callback-ms={lastRefreshRestoreMilliseconds.GetValue(hosts[0])}");
                        lastObservedRefreshCompletionCount = currentFallbackCount;
                    }
                    // Shell state reads can synchronously wait for large-view enumeration;
                    // sample them sparsely so the probe does not create its own dispatcher backlog.
                    if (now - lastSelectionSampleAt >= 1000)
                    {
                        lastSelectionSampleAt = now;
                        var selectionStarted = Stopwatch.GetTimestamp();
                        var sampledSelection = SelectedPaths(hostType, hosts[0], folder)
                            .Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                        maxSelectionRead = Math.Max(maxSelectionRead,
                            (long)Stopwatch.GetElapsedTime(selectionStarted).TotalMilliseconds);
                        var focusStarted = Stopwatch.GetTimestamp();
                        var sampledFocus = Path.GetFileName((string?)focusMethod.Invoke(hosts[0], [folder]));
                        maxFocusRead = Math.Max(maxFocusRead,
                            (long)Stopwatch.GetElapsedTime(focusStarted).TotalMilliseconds);
                        var countStarted = Stopwatch.GetTimestamp();
                        var sampledViewCount = Count(hosts[0]);
                        var countReadEndedAt = Stopwatch.GetTimestamp();
                        maxCountRead = Math.Max(maxCountRead,
                            (long)Stopwatch.GetElapsedTime(countStarted, countReadEndedAt).TotalMilliseconds);
                        var sample = $"t={now}ms,view-count={sampledViewCount},selection-count={sampledSelection.Length},items={string.Join(',', sampledSelection)},focus={sampledFocus}";
                        if (selectionTimeline.Count == 1 ||
                            !selectionTimeline[^1].Contains($"view-count={sampledViewCount},selection-count={sampledSelection.Length},items={string.Join(',', sampledSelection)},focus={sampledFocus}", StringComparison.Ordinal))
                            selectionTimeline.Add(sample);

                        if (now - lastDirectoryCountSampleAt >= 250)
                        {
                            lastDirectoryCountSampleAt = now;
                            var batch = directoryChangeBatchField.GetValue(hosts[0]);
                            var batchState = batch is null ? "none" : batchDiagnosticSnapshot.GetValue(batch)?.ToString() ?? "unknown";
                            directoryCountTimeline.Add($"t={now}ms,count-read-start-ticks={countStarted},count-read-end-ticks={countReadEndedAt},target-count={diagnosticTargetCount},view-count={sampledViewCount},batch={batchState}");
                        }
                    }
                    maxHeartbeatWork = Math.Max(maxHeartbeatWork,
                        (long)Stopwatch.GetElapsedTime(heartbeatStarted).TotalMilliseconds);
                };
                heartbeat.Start();
                // Sample dispatch opportunities at several priorities. A delayed
                // periodic probe is not, by itself, evidence that its queue starved.
                foreach (var priority in dispatcherPriorityGaps.Keys.ToArray())
                {
                    var timer = new DispatcherTimer(priority) { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (_, _) =>
                    {
                        var now = Stopwatch.GetTimestamp();
                        dispatcherPriorityTickCounts[priority]++;
                        var previous = dispatcherPriorityLastTicks[priority];
                        if (previous != 0)
                        {
                            var gapMilliseconds = (long)Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;
                            if (gapMilliseconds > dispatcherPriorityGaps[priority])
                            {
                                dispatcherPriorityGaps[priority] = gapMilliseconds;
                                dispatcherPriorityMaxGapAt[priority] = Stopwatch.GetElapsedTime(diagnosticStartTicks, now).TotalMilliseconds;
                                dispatcherPriorityMaxGapFromTicks[priority] = previous;
                                dispatcherPriorityMaxGapToTicks[priority] = now;
                            }
                            if (gapMilliseconds >= 100 && dispatcherGapTimeline.Count < 128)
                                dispatcherGapTimeline.Add($"t={Stopwatch.GetElapsedTime(diagnosticStartTicks, now).TotalMilliseconds:F1}ms,{priority}={gapMilliseconds}");
                        }
                        dispatcherPriorityLastTicks[priority] = now;
                    };
                    dispatcherPriorityTimers.Add(timer);
                    timer.Start();
                }
                var writeClock = Stopwatch.StartNew();
                var writer = Task.Run(() =>
                {
                    for (var index = 0; index < burstSize; index++)
                        File.WriteAllText(Path.Combine(folder, $"burst-{index:D4}.txt"), string.Empty);
                });
                activeOperation = writer;
                var observedCreated = 0;
                var observedDeleted = 0;
                string? lastObservedDeletePath = null;
                var observerErrors = 0;
                using var burstWatcher = new FileSystemWatcher(folder)
                {
                    Filter = "burst-*.txt",
                    NotifyFilter = NotifyFilters.FileName,
                    InternalBufferSize = 16 * 1024
                };
                burstWatcher.Created += (_, _) => Interlocked.Increment(ref observedCreated);
                burstWatcher.Deleted += (_, eventArgs) =>
                {
                    Volatile.Write(ref lastObservedDeletePath, eventArgs.FullPath);
                    Interlocked.Increment(ref observedDeleted);
                };
                burstWatcher.Error += (_, _) => Interlocked.Increment(ref observerErrors);
                burstWatcher.EnableRaisingEvents = true;
                var writerCompleted = WaitForPredicate(() => writer.IsCompleted, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(100));
                writer.GetAwaiter().GetResult();
                writeClock.Stop();
                WaitForDelay(TimeSpan.FromMilliseconds(1200));
                var immediateCountAfterCreate = Count(hosts[0]);
                var createSettledClock = Stopwatch.StartNew();
                var completed = writerCompleted && (immediateCountAfterCreate == targetCounts[0] ||
                    WaitForExpectedCounts(hosts, targetCounts, TimeSpan.FromSeconds(15)));
                createSettledClock.Stop();
                var createRefreshSettled = !productMonitorEnabled || WaitForPredicate(() =>
                {
                    var batch = directoryChangeBatchField.GetValue(hosts[0]);
                    var expectedCount = batch?.GetType().GetProperty("ExpectedCountAfterChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch) as int?;
                    return !(bool)directoryRefreshInProgress.GetValue(hosts[0])! &&
                        !(bool)directorySelectionRestorePending.GetValue(hosts[0])! && expectedCount == Count(hosts[0]);
                }, TimeSpan.FromSeconds(12));
                var countsAfterCreate = hosts.Select(Count).ToArray();
                WriteDirectoryStressMemoryCheckpoint("after-create-settled");
                var selectedAfterCreate = SelectedPaths(hostType, hosts[0], folder).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusAfterCreate = (string?)focusMethod.Invoke(hosts[0], [folder]);
                var selectionRestoredAfterCreate = selectedBefore.SequenceEqual(selectedAfterCreate, StringComparer.OrdinalIgnoreCase);
                var focusRestoredAfterCreate = string.Equals(focusBefore, focusAfterCreate, StringComparison.OrdinalIgnoreCase);
                var fallbackAfterCreate = (int)fallbackCount.GetValue(hosts[0])!;
                var fallbackDetailsAfterCreate = LastFallbackDetails();
                var observedAfterCreate = Volatile.Read(ref observedCreated);
                var errorsAfterCreate = Volatile.Read(ref observerErrors);
                var lookupDeletedEventsBefore = (int)productionDeletedEvents.GetValue(hosts[0])!;
                var lookupClock = Stopwatch.StartNew();
                // This file was generated by this stress run; the Shell fixture helper intentionally
                // rejects arbitrary burst names, so delete only this exact generated path directly.
                var lookupTargetPath = Path.Combine(folder, $"burst-{burstSize - 1:D4}.txt");
                diagnosticTargetCount = targetCounts[0] - 1;
                File.Delete(lookupTargetPath);
                var lookupChangeObserved = !productMonitorEnabled || WaitForPredicate(() =>
                    (int)productionDeletedEvents.GetValue(hosts[0])! > lookupDeletedEventsBefore, TimeSpan.FromSeconds(5));
                var lookupCountBefore = Count(hosts[0]);
                var staleLookupClock = Stopwatch.StartNew();
                var staleLookupKind = ShellViewItemKind(hostType, hosts[0], lookupTargetPath);
                staleLookupClock.Stop();
                lookupClock.Stop();
                var staleLookupWasValid = lookupChangeObserved && lookupCountBefore == targetCounts[0] && staleLookupKind == "file";
                var targetCountAfterLookupDelete = (int[])targetCounts.Clone();
                targetCountAfterLookupDelete[0]--;
                var lookupDeleteVisible = WaitForExpectedCounts(hosts, targetCountAfterLookupDelete, TimeSpan.FromSeconds(10));
                WaitForPredicate(() => !(bool)directoryRefreshInProgress.GetValue(hosts[0])! &&
                    !(bool)directorySelectionRestorePending.GetValue(hosts[0])!, TimeSpan.FromSeconds(12));
                WaitForDelay(TimeSpan.FromMilliseconds(1200)); // Let the monitor reconcile before the bulk delete phase.
                var largeFolderDeleteRecoveredWithoutIdentityScan = true;
                if (burstSize >= 10000 && lookupDeleteVisible)
                {
                    var largeFolderPath = Path.Combine(folder, "probe-large-view-empty-folder");
                    var folderCreateEventsBefore = (int)productionCreatedEvents.GetValue(hosts[0])!;
                    var folderDeleteEventsBefore = (int)productionDeletedEvents.GetValue(hosts[0])!;
                    var folderLookupCountBefore = (int)folderDeleteLookupCount.GetValue(hosts[0])!;
                    var folderFallbackCountBefore = (int)fallbackCount.GetValue(hosts[0])!;
                    var folderVisibleCounts = (int[])targetCountAfterLookupDelete.Clone();
                    folderVisibleCounts[0]++;
                    diagnosticTargetCount = folderVisibleCounts[0];
                    var folderCycleClock = Stopwatch.StartNew();
                    Directory.CreateDirectory(largeFolderPath);
                    var folderCreatedObserved = !productMonitorEnabled || WaitForPredicate(() =>
                        (int)productionCreatedEvents.GetValue(hosts[0])! > folderCreateEventsBefore,
                        TimeSpan.FromSeconds(5));
                    var folderCreatedVisible = folderCreatedObserved && WaitForExpectedCounts(hosts, folderVisibleCounts, TimeSpan.FromSeconds(15));
                    var folderCreateVisibleAtMs = folderCreatedVisible ? folderCycleClock.ElapsedMilliseconds : (long?)null;
                    if (folderCreatedVisible)
                    {
                        var folderDeleteClock = Stopwatch.StartNew();
                        Directory.Delete(largeFolderPath);
                        var folderDeletedObserved = !productMonitorEnabled || WaitForPredicate(() =>
                            (int)productionDeletedEvents.GetValue(hosts[0])! > folderDeleteEventsBefore,
                            TimeSpan.FromSeconds(5));
                        var folderCountsSettled = folderDeletedObserved &&
                            WaitForExpectedCounts(hosts, targetCountAfterLookupDelete, TimeSpan.FromSeconds(15));
                        var folderMonitorSettled = folderCountsSettled && (!productMonitorEnabled || WaitForPredicate(() =>
                            !(bool)directoryRefreshInProgress.GetValue(hosts[0])! &&
                            !(bool)directorySelectionRestorePending.GetValue(hosts[0])!, TimeSpan.FromSeconds(12)));
                        folderDeleteClock.Stop();
                        var folderLookupDelta = (int)folderDeleteLookupCount.GetValue(hosts[0])! - folderLookupCountBefore;
                        var folderFallbackDelta = (int)fallbackCount.GetValue(hosts[0])! - folderFallbackCountBefore;
                        var folderLastDeleteResult = (string?)lastFolderDeleteLookupResult.GetValue(hosts[0]);
                        largeFolderDeleteRecoveredWithoutIdentityScan = folderMonitorSettled && (productMonitorEnabled
                            ? folderLookupDelta == 0
                            : folderLookupDelta == 0 && folderFallbackDelta == 0);
                        folderCycleClock.Stop();
                        Console.WriteLine($"large-folder-cycle mode={(productMonitorEnabled ? "product" : "native-only")} create-visible={folderCreatedVisible} create-visible-ms={folderCreateVisibleAtMs?.ToString() ?? "none"} delete-visible={folderCountsSettled} settled={folderMonitorSettled} identity-result={folderLastDeleteResult ?? "unknown"} identity-scan-skipped={folderLastDeleteResult == "SkippedLargeView"} lookup-count-delta={folderLookupDelta} fallback-delta={folderFallbackDelta} cycle-ms={folderCycleClock.ElapsedMilliseconds} delete-ms={folderDeleteClock.ElapsedMilliseconds}");
                    }
                    else
                    {
                        largeFolderDeleteRecoveredWithoutIdentityScan = false;
                        if (Directory.Exists(largeFolderPath)) Directory.Delete(largeFolderPath);
                        folderCycleClock.Stop();
                        Console.WriteLine($"large-folder-create-visible=False mode={(productMonitorEnabled ? "product" : "native-only")} watcher-observed={folderCreatedObserved} elapsed-ms={folderCycleClock.ElapsedMilliseconds}");
                    }
                    diagnosticTargetCount = targetCountAfterLookupDelete[0];
                }
                else if (burstSize >= 10000)
                {
                    // A native-only run that never showed its burst cannot provide a comparable folder-cycle sample.
                    largeFolderDeleteRecoveredWithoutIdentityScan = false;
                    Console.WriteLine($"large-folder-cycle-skipped mode={(productMonitorEnabled ? "product" : "native-only")} lookup-delete-visible={lookupDeleteVisible} reason=base-view-not-settled");
                }
                var batchLookupPath = "not-run";
                var batchLookupKind = "not-run";
                var batchLookupMilliseconds = -1L;
                var batchLookupViewCount = -1;
                var batchLookupEvents = 0;
                var batchLookupStale = true;
                var batchDeleteVisible = true;
                var includeBatchDeleteLookup = args[0] == "--live-shell-directory-stress-large" ||
                    args[0] == "--live-shell-directory-stress-native" && burstSize >= 10000;
                if (includeBatchDeleteLookup)
                {
                    const int batchDeleteCount = 128;
                    diagnosticTargetCount = targetCounts[0] - batchDeleteCount - 1;
                    var batchEventsBefore = Volatile.Read(ref observedDeleted);
                    var batchDeleteClock = Stopwatch.StartNew();
                    var batchDeleter = Task.Run(() =>
                    {
                        for (var index = 0; index < batchDeleteCount; index++)
                            File.Delete(Path.Combine(folder, $"burst-{index:D4}.txt"));
                    });
                    activeOperation = batchDeleter;
                    var batchOperationCompleted = WaitForPredicate(() => batchDeleter.IsCompleted, TimeSpan.FromSeconds(30));
                    batchDeleter.GetAwaiter().GetResult();
                    var batchWatcherCompleted = WaitForPredicate(() =>
                        Volatile.Read(ref observedDeleted) >= batchEventsBefore + batchDeleteCount,
                        TimeSpan.FromSeconds(5));
                    batchLookupEvents = Volatile.Read(ref observedDeleted) - batchEventsBefore;
                    batchLookupPath = Volatile.Read(ref lastObservedDeletePath) ?? "missing";
                    batchLookupViewCount = Count(hosts[0]);
                    var batchLookupClock = Stopwatch.StartNew();
                    batchLookupKind = ShellViewItemKind(hostType, hosts[0], batchLookupPath);
                    batchLookupClock.Stop();
                    batchLookupMilliseconds = batchLookupClock.ElapsedMilliseconds;
                    batchLookupStale = batchOperationCompleted && batchWatcherCompleted &&
                        batchLookupEvents == batchDeleteCount && batchLookupKind == "file" &&
                        batchLookupViewCount > targetCounts[0] - 1 - batchDeleteCount;
                    var batchTargetCounts = (int[])targetCounts.Clone();
                    batchTargetCounts[0] -= batchDeleteCount + 1;
                    batchDeleteVisible = WaitForExpectedCounts(hosts, batchTargetCounts, TimeSpan.FromSeconds(10));
                    batchDeleteClock.Stop();
                }
                // Native-only mode stops the product monitor, so it must not assert the product selection-recovery contract.
                var selectionSettledBeforeBulkDelete = !productMonitorEnabled || WaitForPredicate(() =>
                    !(bool)directoryRefreshInProgress.GetValue(hosts[0])! &&
                    !(bool)directorySelectionRestorePending.GetValue(hosts[0])! &&
                    SelectedPaths(hostType, hosts[0], folder).Order(StringComparer.OrdinalIgnoreCase)
                        .SequenceEqual(selectedBefore, StringComparer.OrdinalIgnoreCase) &&
                    string.Equals((string?)focusMethod.Invoke(hosts[0], [folder]), focusBefore, StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(12));
                if (!selectionSettledBeforeBulkDelete)
                {
                    Console.WriteLine($"directory-stress-selection-diagnostic refresh-in-progress={directoryRefreshInProgress.GetValue(hosts[0])} restore-pending={directorySelectionRestorePending.GetValue(hosts[0])} actual-selection={string.Join(',', SelectedPaths(hostType, hosts[0], folder).Select(Path.GetFileName))} expected-selection={string.Join(',', selectedBefore.Select(Path.GetFileName))} actual-focus={Path.GetFileName((string?)focusMethod.Invoke(hosts[0], [folder]))} expected-focus={Path.GetFileName(focusBefore)} refresh-captured-selection={lastRefreshSelectedCount.GetValue(hosts[0])} refresh-captured-focus={lastRefreshHadFocus.GetValue(hosts[0])} refresh-restore-ms={lastRefreshRestoreMilliseconds.GetValue(hosts[0])} native-restore-ms={nativeSelectionRestoreMilliseconds.GetValue(hosts[0])} refresh-completion-timeline=[{string.Join(";", refreshCompletionTimeline)}] shell-selection-callbacks=[{shellSelectionCallbackTimeline.GetValue(hosts[0])}] shell-selection-restores=[{shellSelectionRestoreTimeline.GetValue(hosts[0])}] selection-timeline=[{string.Join(";", selectionTimeline)}]");
                    throw new Exception("The Shell directory count settled before the selection and focus returned to the probe baseline.");
                }
                // A large Shell refresh can report its count before deferred selection restoration finishes;
                // report the selection and focus sampled at the settled baseline, not the earlier one-shot sample.
                selectedAfterCreate = SelectedPaths(hostType, hosts[0], folder)
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                focusAfterCreate = (string?)focusMethod.Invoke(hosts[0], [folder]);
                selectionRestoredAfterCreate = selectedBefore.SequenceEqual(selectedAfterCreate, StringComparer.OrdinalIgnoreCase);
                focusRestoredAfterCreate = string.Equals(focusBefore, focusAfterCreate, StringComparison.OrdinalIgnoreCase);
                var selectedBeforeBulkDelete = SelectedPaths(hostType, hosts[0], folder)
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusBeforeBulkDelete = (string?)focusMethod.Invoke(hosts[0], [folder]);
                var observedDeletesBeforeBulkDelete = Volatile.Read(ref observedDeleted);
                var originalCounts = Enumerable.Repeat(3, hosts.Count).ToArray();
                var deleteClock = Stopwatch.StartNew();
                diagnosticTargetCount = originalCounts[0];
                var deleter = Task.Run(() =>
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "burst-*.txt").ToArray()) File.Delete(path);
                });
                activeOperation = deleter;
                var firstDeleteObserved = WaitForPredicate(() =>
                    Volatile.Read(ref observedDeleted) > observedDeletesBeforeBulkDelete, TimeSpan.FromSeconds(5));
                var selectedAfterFirstDelete = SelectedPaths(hostType, hosts[0], folder).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusAfterFirstDelete = (string?)focusMethod.Invoke(hosts[0], [folder]);
                var countAfterFirstDelete = Count(hosts[0]);
                var firstDeleteAtMs = deleteClock.ElapsedMilliseconds;
                var deleteCompleted = WaitForPredicate(() => deleter.IsCompleted, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(100));
                deleter.GetAwaiter().GetResult();
                deleteClock.Stop();
                WaitForDelay(TimeSpan.FromMilliseconds(1200));
                var immediateCountAfterDelete = Count(hosts[0]);
                var deleteSettledClock = Stopwatch.StartNew();
                var deleted = deleteCompleted && (immediateCountAfterDelete == originalCounts[0] ||
                    WaitForExpectedCounts(hosts, originalCounts, TimeSpan.FromSeconds(15)));
                deleteSettledClock.Stop();
                var postOperationSettleClock = Stopwatch.StartNew();
                if (productMonitorEnabled) WaitForDelay(TimeSpan.FromSeconds(5));
                postOperationSettleClock.Stop();
                var countsAfterDelete = hosts.Select(Count).ToArray();
                WriteDirectoryStressMemoryCheckpoint("after-delete-settled");
                var selectedAfterDelete = SelectedPaths(hostType, hosts[0], folder).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusAfterDelete = (string?)focusMethod.Invoke(hosts[0], [folder]);
                var selectionRestoredAfterDelete = selectedBefore.SequenceEqual(selectedAfterDelete, StringComparer.OrdinalIgnoreCase);
                var focusRestoredAfterDelete = string.Equals(focusBefore, focusAfterDelete, StringComparison.OrdinalIgnoreCase);
                var observedAfterDelete = Volatile.Read(ref observedDeleted);
                var fallbackDetailsAfterDelete = LastFallbackDetails();
                var selectionRestoreTrace = (string)shellSelectionRestoreTimeline.GetValue(hosts[0])!;
                var directFocusRestoreObserved = selectionRestoreTrace.Contains("focused-item-lookup=direct-select", StringComparison.Ordinal);
                // This is opt-in in ExplorerBrowserHost; an unset variable must not require trace-only events.
                var shellCallbackTraceEnabled = Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE") == "1";
                heartbeat.Stop();
                foreach (var timer in dispatcherPriorityTimers) timer.Stop();
                dedicatedHeartbeat.Stop();
                dispatcherQueueLatencyProbe?.Stop();
                var dispatcherQueueLatencyDrained = dispatcherQueueLatencyProbe is not null && WaitForPredicate(
                    () => dispatcherQueueLatencyProbe.IsDrained, TimeSpan.FromSeconds(5));
                var finalCount = Count(hosts[0]);
                var diskCount = Directory.EnumerateFileSystemEntries(folder).Count();
                Console.WriteLine($"directory-delete-identity-scan count={folderDeleteLookupCount.GetValue(hosts[0])} last-result={lastFolderDeleteLookupResult.GetValue(hosts[0])}");
                if (batchLookupEvents != 0)
                    Console.WriteLine($"delete-batch-identity-lookup events={batchLookupEvents} queried-path={Path.GetFileName(batchLookupPath)} query-count=1 query-kind={batchLookupKind} query-ms={batchLookupMilliseconds} view-count-before-query={batchLookupViewCount} stale-match={batchLookupStale} batch-visible={batchDeleteVisible}");
                Console.WriteLine("dispatcher-priority-gaps-ms=" + string.Join(',', dispatcherPriorityGaps.OrderBy(pair => (int)pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}")));
                Console.WriteLine("dispatcher-priority-probe-ticks=" + string.Join(',', dispatcherPriorityTickCounts.OrderBy(pair => (int)pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}")));
                Console.WriteLine(dispatcherQueueLatencyProbe is null
                    ? "dispatcher-independent-queue-probe=disabled"
                    : $"dispatcher-independent-queue-probe-drained={dispatcherQueueLatencyDrained} {dispatcherQueueLatencyProbe.Summary}");
                Console.WriteLine("dispatcher-priority-max-gap-at-ms=" + string.Join(',', dispatcherPriorityGaps.Keys.OrderBy(priority => (int)priority)
                    .Select(priority => $"{priority}:{dispatcherPriorityGaps[priority]}@{dispatcherPriorityMaxGapAt[priority]:F1}")));
                if (messagePumpTrace is not null && dispatcherPriorityMaxGapFromTicks[DispatcherPriority.ContextIdle] != 0)
                {
                    var gapStart = dispatcherPriorityMaxGapFromTicks[DispatcherPriority.ContextIdle];
                    var gapEnd = dispatcherPriorityMaxGapToTicks[DispatcherPriority.ContextIdle];
                    Console.WriteLine($"dispatcher-contextidle-max-gap-window-ms={Stopwatch.GetElapsedTime(diagnosticStartTicks, gapStart).TotalMilliseconds:F1}..{Stopwatch.GetElapsedTime(diagnosticStartTicks, gapEnd).TotalMilliseconds:F1}");
                    Console.WriteLine("dispatcher-contextidle-operation-overlap=" + messagePumpTrace.DescribeDispatcherOperationOverlap(gapStart, gapEnd));
                }
                Console.WriteLine($"dedicated-thread-heartbeat-max-gap-ms={dedicatedHeartbeat.MaxGapMilliseconds}");
                Console.WriteLine($"dispatcher-thread-cpu-average-percent={dedicatedHeartbeat.DispatcherThreadCpuAveragePercent:F1} max-sample-percent={dedicatedHeartbeat.DispatcherThreadCpuMaxSamplePercent:F1} max-consecutive-busy-ms={dedicatedHeartbeat.DispatcherThreadMaxBusyMilliseconds}");
                Console.WriteLine($"shell-callback-timings={callbackTimingSnapshot.GetValue(browserSites[0])}");
                Console.WriteLine($"shell-notifications-at-stress-start={shellNotificationsAtStressStart}");
                Console.WriteLine($"shell-notifications-at-stress-end={shellNotificationSnapshot.GetValue(browserSites[0])}");
                Console.WriteLine($"shell-selection-version-at-stress-start={shellSelectionVersionAtStressStart}");
                Console.WriteLine($"shell-state-change-timeline={shellStateChangeTrace.GetValue(browserSites[0])}");
                Console.WriteLine($"native-view-message-timings={viewMessageTimingSnapshot.GetValue(hosts[0])}");
                Console.WriteLine($"native-view-message-slow-timeline={viewMessageSlowTimelineSnapshot.GetValue(hosts[0])}");
                Console.WriteLine($"dispatcher-gap-timeline={string.Join('|', dispatcherGapTimeline)}");
                Console.WriteLine($"directory-count-timeline={string.Join('|', directoryCountTimeline)}");
                Console.WriteLine($"message-pump-trace={messagePumpTrace?.Summary ?? "disabled"}");
                Console.WriteLine($"directory-refresh-call-timeline={directoryRefreshCallTimelineSnapshot.GetValue(hosts[0])}");
                Console.WriteLine($"directory-change-stage-timings={directoryChangeStageTimingSnapshot.GetValue(hosts[0])}");
                Console.WriteLine($"shell-focus-restore-direct-select={directFocusRestoreObserved}");
                Console.WriteLine($"directory-stress mode={(productMonitorEnabled ? "product" : "native-only")} monitor-start-count={monitorStartCount} monitor-start-samples={monitorStartSamples} files={burstSize} create-ms={writeClock.ElapsedMilliseconds} delete-ms={deleteClock.ElapsedMilliseconds} create-immediate={immediateCountAfterCreate} create-settled-ms={createSettledClock.ElapsedMilliseconds} create-refresh-settled={createRefreshSettled} stale-lookup-kind={staleLookupKind} stale-lookup-valid={staleLookupWasValid} stale-lookup-wall-ms={lookupClock.ElapsedMilliseconds} stale-lookup-call-ms={staleLookupClock.ElapsedMilliseconds} stale-lookup-count={lookupCountBefore} lookup-delete-visible={lookupDeleteVisible} selection-baseline-before-bulk-delete={selectionSettledBeforeBulkDelete} selected-before-bulk-delete={string.Join(',', selectedBeforeBulkDelete.Select(Path.GetFileName))} focus-before-bulk-delete={Path.GetFileName(focusBeforeBulkDelete)} first-delete-observed={firstDeleteObserved} first-delete-at-ms={firstDeleteAtMs} first-delete-count={countAfterFirstDelete} selected-after-first-delete={string.Join(',', selectedAfterFirstDelete.Select(Path.GetFileName))} focus-after-first-delete={Path.GetFileName(focusAfterFirstDelete)} delete-immediate={immediateCountAfterDelete} delete-settled-ms={deleteSettledClock.ElapsedMilliseconds} post-operation-settle-ms={postOperationSettleClock.ElapsedMilliseconds} disk-count={diskCount} view-counts={countsAfterCreate[0]}/{countsAfterDelete[0]} target={targetCountAfterLookupDelete[0]}/3 create-visible={completed} delete-visible={deleted} selection-restored={selectionRestoredAfterCreate}/{selectionRestoredAfterDelete} selected-before={string.Join(',', selectedBefore.Select(Path.GetFileName))} selected-after-create={string.Join(',', selectedAfterCreate.Select(Path.GetFileName))} selected-after-delete={string.Join(',', selectedAfterDelete.Select(Path.GetFileName))} focus-restored={focusRestoredAfterCreate}/{focusRestoredAfterDelete} focus-before={Path.GetFileName(focusBefore)} focus-after-create={Path.GetFileName(focusAfterCreate)} focus-after-delete={Path.GetFileName(focusAfterDelete)} refresh-captured-selection={lastRefreshSelectedCount.GetValue(hosts[0])} refresh-captured-focus={lastRefreshHadFocus.GetValue(hosts[0])} refresh-restore-ms={lastRefreshRestoreMilliseconds.GetValue(hosts[0])} refresh-view-count-at-restore={lastRefreshViewCountAtRestore.GetValue(hosts[0])} refresh-selection-restored={lastRefreshSelectionRestored.GetValue(hosts[0])} refresh-focus-restored={lastRefreshFocusRestored.GetValue(hosts[0])} native-selection-restore-ms={nativeSelectionRestoreMilliseconds.GetValue(hosts[0])} refresh-capture-timeline=[{string.Join(";", refreshCaptureTimeline)}] refresh-completion-timeline=[{string.Join(";", refreshCompletionTimeline)}] shell-selection-callbacks=[{shellSelectionCallbackTimeline.GetValue(hosts[0])}] shell-selection-restores=[{shellSelectionRestoreTimeline.GetValue(hosts[0])}] selection-timeline=[{string.Join(";", selectionTimeline)}] observer-events={observedAfterCreate}/{observedAfterDelete} production-events={productionCreatedEvents.GetValue(hosts[0])}/{productionDeletedEvents.GetValue(hosts[0])} native-create-notifications={nativeCreateNotifications.GetValue(hosts[0])} observer-errors={errorsAfterCreate}/{Volatile.Read(ref observerErrors)} fallback-after-create={fallbackAfterCreate} fallback-info-create=[{fallbackDetailsAfterCreate}] fallback-after-delete={fallbackCount.GetValue(hosts[0])} fallback-info-delete=[{fallbackDetailsAfterDelete}] fallback-at-ms={fallbackAtMs} operation-complete-at-fallback={operationCompleteAtFallback} fallback-timeline=[{string.Join(";", fallbackTimeline)}] max-dispatcher-heartbeat-gap-ms={maxHeartbeatGap} max-dispatcher-heartbeat-work-ms={maxHeartbeatWork} max-shell-count-read-ms={maxCountRead} max-shell-selection-read-ms={maxSelectionRead} max-shell-focus-read-ms={maxFocusRead}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return completed && deleted && diskCount == 3 && finalCount == 3 &&
                    (burstSize < 10000 || largeFolderDeleteRecoveredWithoutIdentityScan) &&
                    (!productMonitorEnabled || (selectionRestoredAfterCreate && selectionRestoredAfterDelete &&
                    focusRestoredAfterCreate && focusRestoredAfterDelete && batchLookupStale && batchDeleteVisible &&
                    (!shellCallbackTraceEnabled || directFocusRestoreObserved))) ? 0 : 1;
            }
            if (args[0] is "--live-shell-refresh" or "--live-shell-refresh-unselected-focus" or "--live-shell-refresh-unselected" or "--live-shell-refresh-one-pane" or "--live-shell-refresh-one-pane-unselected" or "--live-shell-refresh-one-pane-3" or "--live-shell-refresh-one-pane-3-dual" or "--live-shell-refresh-one-pane-3-single" or "--live-shell-refresh-one-pane-3-single-dual" or "--live-shell-refresh-one-pane-3-single-fallback" or "--live-shell-refresh-one-pane-3-multiselect-fallback" or "--live-shell-refresh-one-pane-unselected-3" or "--live-shell-refresh-one-pane-unselected-3-singleobserver" or "--live-shell-refresh-one-pane-unselected-3-filesystem-control" or "--live-shell-refresh-pane-0" or "--live-shell-refresh-pane-1" or "--live-shell-refresh-pane-2" or "--live-shell-refresh-pane-3" or "--live-shell-delete-selected-pane-0" or "--live-shell-delete-selected-pane-1" or "--live-shell-delete-selected-pane-2" or "--live-shell-delete-selected-pane-3" or "--live-shell-delete-selected-pane-3-fallback")
            {
                var deferredNotificationProbe = args[0] == "--live-shell-refresh" &&
                    Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_F5_DEFERRED_UPDATE") == "1";
                var deferredShellCopyProbe = deferredNotificationProbe &&
                    Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_F5_DEFERRED_SHELL_COPY") == "1";
                var deleteSelected = args[0].StartsWith("--live-shell-delete-selected", StringComparison.Ordinal);
                var filesystemControl = args[0] == "--live-shell-refresh-one-pane-unselected-3-filesystem-control";
                var fallbackRefresh = args[0] is "--live-shell-refresh-one-pane-3-single-fallback" or "--live-shell-refresh-one-pane-3-multiselect-fallback" or "--live-shell-delete-selected-pane-3-fallback";
                var unselectedFocus = args[0] == "--live-shell-refresh-unselected-focus";
                var noSelection = args[0] is "--live-shell-refresh-unselected" or "--live-shell-refresh-one-pane-unselected" or "--live-shell-refresh-one-pane-unselected-3" or "--live-shell-refresh-one-pane-unselected-3-singleobserver" or "--live-shell-refresh-one-pane-unselected-3-filesystem-control";
                var singleSelection = args[0] is "--live-shell-refresh-one-pane-3-single" or "--live-shell-refresh-one-pane-3-single-dual" or "--live-shell-refresh-one-pane-3-single-fallback";
                var onePaneOperation = args[0] is "--live-shell-refresh-one-pane" or "--live-shell-refresh-one-pane-unselected" or "--live-shell-refresh-one-pane-3" or "--live-shell-refresh-one-pane-3-dual" or "--live-shell-refresh-one-pane-3-single" or "--live-shell-refresh-one-pane-3-single-dual" or "--live-shell-refresh-one-pane-3-single-fallback" or "--live-shell-refresh-one-pane-3-multiselect-fallback" or "--live-shell-refresh-one-pane-unselected-3" or "--live-shell-refresh-one-pane-unselected-3-singleobserver" or "--live-shell-refresh-one-pane-unselected-3-filesystem-control" or "--live-shell-delete-selected-pane-3-fallback";
                var pane = args[0] is "--live-shell-refresh" or "--live-shell-refresh-unselected-focus" or "--live-shell-refresh-unselected" or "--live-shell-refresh-one-pane" or "--live-shell-refresh-one-pane-unselected" ? 0 :
                    args[0] is "--live-shell-refresh-one-pane-3" or "--live-shell-refresh-one-pane-3-dual" or "--live-shell-refresh-one-pane-3-single" or "--live-shell-refresh-one-pane-3-single-dual" or "--live-shell-refresh-one-pane-3-single-fallback" or "--live-shell-refresh-one-pane-3-multiselect-fallback" or "--live-shell-refresh-one-pane-unselected-3" or "--live-shell-refresh-one-pane-unselected-3-singleobserver" or "--live-shell-refresh-one-pane-unselected-3-filesystem-control" or "--live-shell-delete-selected-pane-3-fallback" ? 3 :
                    int.Parse(args[0][(deleteSelected ? "--live-shell-delete-selected-pane-".Length : "--live-shell-refresh-pane-".Length)..]);
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)))
                    throw new Exception($"Initial fixture counts were not ready: {string.Join(',', hosts.Select(Count))}.");
                if (deferredNotificationProbe)
                {
                    var setSoftRefresh = hostType.GetMethod("SetSoftRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var softRefreshResult = (int)setSoftRefresh.Invoke(hosts[pane], [true])!;
                    if (softRefreshResult < 0)
                        throw new Exception($"Could not disable automatic Shell enumeration for the target pane: 0x{softRefreshResult:X8}.");
                    var stopMonitor = hostType.GetMethod("StopDirectoryChangeMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    stopMonitor.Invoke(hosts[pane], null);
                }
                if (singleSelection) SelectOnlyItem(hostType, hosts[pane], folders[pane], "initial.txt");
                else if (!noSelection) SelectAll(hostType, hosts[pane]);
                if (unselectedFocus) SetUnselectedFocus(hostType, hosts[pane], folders[pane], "initial.txt");
                else if (!noSelection) SetSelectedFocus(hostType, hosts[pane], folders[pane], "initial.txt");
                var selectedBefore = noSelection ? [] : SelectedPaths(hostType, hosts[pane], folders[pane]);
                var expectedSelection = noSelection ? [] : (singleSelection ? new[] { "initial.txt" } : unselectedFocus ? new[] { "second.txt", "third.txt" } : new[] { "initial.txt", "second.txt", "third.txt" })
                    .Select(name => Path.Combine(folders[pane], name)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (!expectedSelection.SequenceEqual(selectedBefore, StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Could not establish pane {pane} selection: {selectedBefore}.");
                var focusMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var paintTransitionSnapshot = hostType.GetProperty("NativePaintTransitionSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var focusBefore = noSelection ? null : (string?)focusMethod.Invoke(hosts[pane], [folders[pane]]);
                if (!noSelection && string.IsNullOrWhiteSpace(focusBefore)) throw new Exception("Could not read the focused fixture item.");
                if (unselectedFocus && !string.Equals(focusBefore, Path.Combine(folders[pane], "initial.txt"), StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Could not establish unselected focus: selected={selectedBefore}; focused={focusBefore}.");
                if (!noSelection)
                {
                    // COM-built selections bypass the real-input hook; refresh must capture the settled focus/selection baseline.
                    var noteInput = hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var inputVersion = hostType.GetField("_nativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var settledInputVersion = hostType.GetField("_settledNativeUserInputVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var savedSelection = hostType.GetField("_lastNativeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    noteInput.Invoke(hosts[pane], [IntPtr.Zero]);
                    var baselineVersion = (long)inputVersion.GetValue(hosts[pane])!;
                    var baselineSettled = WaitForPredicate(() =>
                    {
                        var snapshot = savedSelection.GetValue(hosts[pane]);
                        var savedCount = snapshot is null ? 0 :
                            (int)snapshot.GetType().GetProperty("SelectedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!;
                        return (long)settledInputVersion.GetValue(hosts[pane])! == baselineVersion &&
                            savedCount == selectedBefore.Length;
                    }, TimeSpan.FromSeconds(5));
                    var selectionAfterCapture = SelectedPaths(hostType, hosts[pane], folders[pane]);
                    var focusAfterCapture = (string?)focusMethod.Invoke(hosts[pane], [folders[pane]]);
                    if (!baselineSettled || !expectedSelection.SequenceEqual(selectionAfterCapture, StringComparer.OrdinalIgnoreCase) ||
                        !string.Equals(focusBefore, focusAfterCapture, StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Could not settle the refresh selection/focus baseline: settled={baselineSettled}, selected={string.Join('|', selectionAfterCapture)}, focused={focusAfterCapture}.");
                }
                using var refreshMessageObserver = new MessageOnlyWindowObserver(GetWindowThreadProcessId(source.Handle, out _));
                var refreshClock = Stopwatch.StartNew();
                using var refreshShellObserver = new ChangeNotificationObserver(source, root, refreshClock);
                using var independentRefreshObserver = args[0] is "--live-shell-refresh-one-pane-3-dual" or "--live-shell-refresh-one-pane-3-single-dual" or "--live-shell-refresh-one-pane-unselected-3"
                    ? new StaNotificationObserver(root, refreshClock) : null;
                var refreshCreatedEvents = 0;
                var fallbackRefreshCount = 0;
                var fallbackRefreshChecks = 0;
                var fallbackRefreshErrors = 0;
                var fallbackRefreshPreCount = -1;
                var probeDispatcher = Dispatcher.CurrentDispatcher;
                var fallbackWatchPath = Path.Combine(folders[pane], deleteSelected ? "initial.txt" : "created.txt");
                var fallbackTimer = new DispatcherTimer(DispatcherPriority.Background, probeDispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(750)
                };
                fallbackTimer.Tick += (_, _) =>
                {
                    fallbackTimer.Stop();
                    if (!fallbackRefresh) return;
                    try
                    {
                        Interlocked.Increment(ref fallbackRefreshChecks);
                        var viewCount = Count(hosts[pane]);
                        var diskCount = Directory.EnumerateFileSystemEntries(folders[pane]).Count();
                        Interlocked.Exchange(ref fallbackRefreshPreCount, viewCount);
                        if (viewCount != diskCount)
                        {
                            hostType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public)!.Invoke(hosts[pane], null);
                            Interlocked.Increment(ref fallbackRefreshCount);
                        }
                    }
                    catch { Interlocked.Increment(ref fallbackRefreshErrors); }
                };
                using var refreshWatcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName };
                void QueueFallbackCheck(string fullPath)
                {
                    if (!fallbackRefresh || !string.Equals(fullPath, fallbackWatchPath, StringComparison.OrdinalIgnoreCase)) return;
                    probeDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        fallbackTimer.Stop();
                        fallbackTimer.Start();
                    }));
                }
                refreshWatcher.Created += (_, e) => { Interlocked.Increment(ref refreshCreatedEvents); QueueFallbackCheck(e.FullPath); };
                refreshWatcher.Deleted += (_, e) => QueueFallbackCheck(e.FullPath);
                refreshWatcher.EnableRaisingEvents = true;
                CaptureDiagnosticsBaseline();
                refreshMessageObserver.CaptureBaseline();
                IReadOnlyList<string> operationFolders = onePaneOperation ? new[] { folders[pane] } : folders;
                var orderBeforeDeferredShellCopy = deferredShellCopyProbe
                    ? CaptureMetadataRowNames(hostType, hosts[pane]) : [];
                if (deferredShellCopyProbe)
                    ShellFixtureOperations.Run(root, [folders[pane]], copy: true, copyName: "0-created.txt");
                else if (deferredNotificationProbe || filesystemControl)
                    File.Copy(Path.Combine(folders[pane], "initial.txt"), Path.Combine(folders[pane], "created.txt"));
                else ShellFixtureOperations.Run(root, operationFolders, copy: !deleteSelected);
                var orderAfterDeferredShellCopy = deferredShellCopyProbe
                    ? CaptureMetadataRowNames(hostType, hosts[pane]) : [];
                var expectedAfterShellOperation = Enumerable.Repeat(3, hosts.Count).ToArray();
                foreach (var operationFolder in operationFolders)
                    expectedAfterShellOperation[folders.IndexOf(operationFolder)] = deleteSelected ? 2 : 4;
                var automaticallyUpdated = !deferredNotificationProbe &&
                    WaitForExpectedCounts(hosts, expectedAfterShellOperation, TimeSpan.FromSeconds(3));
                if (fallbackRefresh) WaitForPredicate(() => fallbackRefreshChecks > 0, TimeSpan.FromSeconds(2));
                var fallbackSelectedAfter = fallbackRefreshCount == 0 ? [] : SelectedPaths(hostType, hosts[pane], folders[pane]);
                var fallbackFocusAfter = fallbackRefreshCount == 0 ? null : (string?)focusMethod.Invoke(hosts[pane], [folders[pane]]);
                var expectedFallbackSelection = deleteSelected ? selectedBefore.Where(File.Exists) : selectedBefore;
                var fallbackSelectionRestored = fallbackRefreshCount == 0 ? "not-run" : expectedFallbackSelection.SequenceEqual(fallbackSelectedAfter, StringComparer.OrdinalIgnoreCase).ToString();
                var fallbackFocusRestored = fallbackRefreshCount == 0 ? "not-run" : deleteSelected && !File.Exists(focusBefore ?? string.Empty)
                    ? fallbackFocusAfter is not null && fallbackSelectedAfter.Contains(fallbackFocusAfter, StringComparer.OrdinalIgnoreCase) ? "True" : "False"
                    : string.Equals(focusBefore, fallbackFocusAfter, StringComparison.OrdinalIgnoreCase).ToString();
                Console.WriteLine($"shell-auto-update pane={pane} counts={string.Join(',', hosts.Select(Count))} observed={automaticallyUpdated} fallback-checks={fallbackRefreshChecks} fallback-refresh-count={fallbackRefreshCount} fallback-refresh-pre-count={fallbackRefreshPreCount} fallback-refresh-errors={fallbackRefreshErrors} fallback-selection-restored={fallbackSelectionRestored} fallback-focus-restored={fallbackFocusRestored} parent-shell-notifications={refreshShellObserver.Summary} independent-shell-notifications={independentRefreshObserver?.Summary ?? "disabled"} filesystem-created={Volatile.Read(ref refreshCreatedEvents)} browser-sites={BrowserSiteNotifications()} view-message-delta={ViewMessageDelta()} view-message-samples={ViewMessageSamples()} message-only-delta={refreshMessageObserver.Delta} message-only-samples={refreshMessageObserver.SampleDelta}");
                var syntheticUpdateObserved = false;
                var syntheticCreateObserved = false;
                if ((!automaticallyUpdated || filesystemControl) && !deleteSelected && !deferredNotificationProbe)
                {
                    // Diagnostic only: publish one directory update for this generated fixture
                    // after the real operation timed out, then measure whether the native view
                    // enumerates the already-created item. This is not a product refresh path.
                    CaptureDiagnosticsBaseline();
                    refreshMessageObserver.CaptureBaseline();
                    var folderPath = Marshal.StringToCoTaskMemUni(folders[pane]);
                    try { SHChangeNotify(0x00001000, 0x00001005, folderPath, IntPtr.Zero); }
                    finally { Marshal.FreeCoTaskMem(folderPath); }
                    syntheticUpdateObserved = WaitForExpectedCounts(hosts, expectedAfterShellOperation, TimeSpan.FromSeconds(3));
                    Console.WriteLine($"shell-synthetic-updatedir pane={pane} counts={string.Join(',', hosts.Select(Count))} observed={syntheticUpdateObserved} parent-shell-notifications={refreshShellObserver.Summary} independent-shell-notifications={independentRefreshObserver?.Summary ?? "disabled"} browser-sites={BrowserSiteNotifications()} view-message-delta={ViewMessageDelta()} view-message-samples={ViewMessageSamples()} message-only-delta={refreshMessageObserver.Delta} message-only-samples={refreshMessageObserver.SampleDelta}");
                    if (!syntheticUpdateObserved && !filesystemControl)
                    {
                        // If a directory update did not reach the view, test the precise new
                        // item's create notification before the later explicit Refresh call.
                        CaptureDiagnosticsBaseline();
                        refreshMessageObserver.CaptureBaseline();
                        var createdPath = Marshal.StringToCoTaskMemUni(Path.Combine(folders[pane], "created.txt"));
                        try { SHChangeNotify(0x00000002, 0x00001005, createdPath, IntPtr.Zero); }
                        finally { Marshal.FreeCoTaskMem(createdPath); }
                        syntheticCreateObserved = WaitForExpectedCounts(hosts, expectedAfterShellOperation, TimeSpan.FromSeconds(3));
                        Console.WriteLine($"shell-synthetic-create pane={pane} counts={string.Join(',', hosts.Select(Count))} observed={syntheticCreateObserved} parent-shell-notifications={refreshShellObserver.Summary} independent-shell-notifications={independentRefreshObserver?.Summary ?? "disabled"} browser-sites={BrowserSiteNotifications()} view-message-delta={ViewMessageDelta()} view-message-samples={ViewMessageSamples()} message-only-delta={refreshMessageObserver.Delta} message-only-samples={refreshMessageObserver.SampleDelta}");
                    }
                }
                if (noSelection)
                {
                    if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                    return automaticallyUpdated || (filesystemControl && syntheticUpdateObserved) ? 0 : 1;
                }
                var beforeRefresh = hosts.Select(Count).ToArray();
                var selectedBeforeRefresh = SelectedPaths(hostType, hosts[pane], folders[pane]);
                var focusBeforeRefresh = (string?)focusMethod.Invoke(hosts[pane], [folders[pane]]);
                var refreshStatusProperty = hostType.GetProperty("LastShellRefreshStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var refreshRestoreMillisecondsProperty = hostType.GetProperty("DirectoryChangeLastRefreshRestoreMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var selectionRestorePendingField = hostType.GetField("_directorySelectionRestorePending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var refreshStatusBefore = (string?)refreshStatusProperty.GetValue(hosts[pane]);
                var refreshRestoreMillisecondsBefore = (long)refreshRestoreMillisecondsProperty.GetValue(hosts[pane])!;
                var manualRefresh = args[0] == "--live-shell-refresh"
                    ? hostType.GetMethod("RefreshFromUser", BindingFlags.Instance | BindingFlags.NonPublic)!
                    : hostType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public)!;
                var softRefreshF5Status = "not-run";
                var softRefreshF5Rows = -1;
                string[]? softRefreshOrderBeforeF5 = null;
                string[]? softRefreshOrderAfterF5 = null;
                if (deferredNotificationProbe)
                {
                    // A user F5 must respect Soft-Refresh until the preference is disabled.
                    softRefreshOrderBeforeF5 = CaptureMetadataRowNames(hostType, hosts[pane]);
                    manualRefresh.Invoke(hosts[pane], null);
                    WaitForDelay(TimeSpan.FromMilliseconds(500));
                    softRefreshF5Rows = Count(hosts[pane]);
                    softRefreshOrderAfterF5 = CaptureMetadataRowNames(hostType, hosts[pane]);
                    softRefreshF5Status = (string?)refreshStatusProperty.GetValue(hosts[pane]) ?? "";
                    var existingRowsStayedInPlace = softRefreshOrderAfterF5.Length >= softRefreshOrderBeforeF5.Length &&
                        softRefreshOrderBeforeF5.SequenceEqual(
                            softRefreshOrderAfterF5.Take(softRefreshOrderBeforeF5.Length), StringComparer.OrdinalIgnoreCase);
                    var newRowsWereAppended = softRefreshOrderAfterF5.Length <= softRefreshOrderBeforeF5.Length + 1 &&
                        softRefreshOrderAfterF5.Skip(softRefreshOrderBeforeF5.Length)
                            .All(name => string.Equals(name, "0-created.txt", StringComparison.OrdinalIgnoreCase));
                    if (softRefreshF5Rows < beforeRefresh[pane] || softRefreshF5Rows > beforeRefresh[pane] + 1 ||
                        !softRefreshF5Status.StartsWith("refreshed;", StringComparison.Ordinal) ||
                        !existingRowsStayedInPlace || !newRowsWereAppended)
                        throw new InvalidOperationException(
                            $"F5 did not preserve Soft-Refresh order: before={beforeRefresh[pane]}, after={softRefreshF5Rows}, status={softRefreshF5Status}, order-before=[{string.Join('|', softRefreshOrderBeforeF5)}], order-after=[{string.Join('|', softRefreshOrderAfterF5)}].");

                    // Once disabled, the same user F5 must perform a full refresh and reveal the deferred item.
                    var setSoftRefresh = hostType.GetMethod("SetSoftRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var softRefreshResult = (int)setSoftRefresh.Invoke(hosts[pane], [false])!;
                    if (softRefreshResult < 0)
                        throw new Exception($"Could not restore normal Shell enumeration before F5: 0x{softRefreshResult:X8}.");
                }
                CaptureDiagnosticsBaseline();
                refreshMessageObserver.CaptureBaseline();
                var rowsImmediatelyBeforeRefresh = Count(hosts[pane]);
                if (deferredNotificationProbe && !deferredShellCopyProbe && rowsImmediatelyBeforeRefresh != beforeRefresh[pane])
                    throw new InvalidOperationException(
                        $"F5 timing probe became inconclusive before the refresh call: rows changed from {beforeRefresh[pane]} to {rowsImmediatelyBeforeRefresh} while normal Shell enumeration was restored.");
                var notificationObservation = "not-run";
                var itemNotificationDelivery = Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_F5_NOTIFICATION_DELIVERY") == "flush"
                    ? 0x00001005u
                    : 0x00002005u;
                var itemNotificationDeliveryName = itemNotificationDelivery == 0x00001005u ? "flush" : "nowait";
                if (deferredNotificationProbe &&
                    Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_F5_FLUSH_NOTIFICATION") == "1")
                {
                    // Compare SHCNF_FLUSH delivery with the production F5 SHCNF_FLUSHNOWAIT route.
                    // The notification is scoped to this probe's unique temporary fixture directory.
                    var folderPath = Marshal.StringToCoTaskMemUni(folders[pane]);
                    var flushClock = Stopwatch.StartNew();
                    try { SHChangeNotify(0x00001000, 0x00001001, folderPath, IntPtr.Zero); }
                    finally { Marshal.FreeCoTaskMem(folderPath); }
                    flushClock.Stop();
                    var rowsOnReturn = Count(hosts[pane]);
                    var rowsAfter100Milliseconds = rowsOnReturn;
                    if (rowsAfter100Milliseconds == rowsImmediatelyBeforeRefresh)
                    {
                        WaitForDelay(TimeSpan.FromMilliseconds(100));
                        rowsAfter100Milliseconds = Count(hosts[pane]);
                    }
                    notificationObservation = $"before={rowsImmediatelyBeforeRefresh};on-return={rowsOnReturn};after-100ms={rowsAfter100Milliseconds};call-ms={flushClock.ElapsedMilliseconds}";
                }
                else if (deferredNotificationProbe &&
                    Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_F5_CREATE_NOTIFICATION") == "1")
                {
                    // Compare an item-level create notification with the directory-wide F5 event.
                    var createdPath = Marshal.StringToCoTaskMemUni(Path.Combine(folders[pane], "0-created.txt"));
                    var createClock = Stopwatch.StartNew();
                    try { SHChangeNotify(0x00000002, itemNotificationDelivery, createdPath, IntPtr.Zero); }
                    finally { Marshal.FreeCoTaskMem(createdPath); }
                    createClock.Stop();
                    var rowsOnReturn = Count(hosts[pane]);
                    var rowsAfter100Milliseconds = rowsOnReturn;
                    if (rowsAfter100Milliseconds == rowsImmediatelyBeforeRefresh)
                    {
                        WaitForDelay(TimeSpan.FromMilliseconds(100));
                        rowsAfter100Milliseconds = Count(hosts[pane]);
                    }
                    notificationObservation = $"item-create;delivery={itemNotificationDeliveryName};before={rowsImmediatelyBeforeRefresh};on-return={rowsOnReturn};after-100ms={rowsAfter100Milliseconds};call-ms={createClock.ElapsedMilliseconds}";
                }
                else if (deferredNotificationProbe &&
                    Environment.GetEnvironmentVariable("EXPLORER_DIRS_PROBE_F5_ITEM_RECONCILE") == "1")
                {
                    // Exercise the product watcher notification vocabulary as a bounded F5 diff.
                    var reconcileClock = Stopwatch.StartNew();
                    foreach (var name in new[] { "initial.txt", "second.txt", "third.txt" })
                    {
                        var existingPath = Marshal.StringToCoTaskMemUni(Path.Combine(folders[pane], name));
                        try { SHChangeNotify(0x00002000, itemNotificationDelivery, existingPath, IntPtr.Zero); }
                        finally { Marshal.FreeCoTaskMem(existingPath); }
                    }
                    var createdPath = Marshal.StringToCoTaskMemUni(Path.Combine(folders[pane], "0-created.txt"));
                    try { SHChangeNotify(0x00000002, itemNotificationDelivery, createdPath, IntPtr.Zero); }
                    finally { Marshal.FreeCoTaskMem(createdPath); }
                    reconcileClock.Stop();
                    var rowsOnReturn = Count(hosts[pane]);
                    WaitForDelay(TimeSpan.FromMilliseconds(100));
                    var rowsAfter100Milliseconds = Count(hosts[pane]);
                    notificationObservation = $"item-diff;delivery={itemNotificationDeliveryName};before={rowsImmediatelyBeforeRefresh};on-return={rowsOnReturn};after-100ms={rowsAfter100Milliseconds};call-ms={reconcileClock.ElapsedMilliseconds}";
                }
                manualRefresh.Invoke(hosts[pane], null);
                var expectedItems = Directory.GetFiles(folders[pane]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var deferredRefreshTimeline = new List<string>();
                var deferredRefreshClock = Stopwatch.StartNew();
                var completionCountAtRefresh = -1;
                var completionAtRefreshMilliseconds = -1L;
                var newRowsAtMilliseconds = -1L;
                bool refreshed;
                if (deferredNotificationProbe)
                {
                    var sawRefreshCompletion = false;
                    var previousSampleCount = -1;
                    var previousSampleStatus = string.Empty;
                    refreshed = WaitForPredicate(() =>
                    {
                        var count = Count(hosts[pane]);
                        var status = (string?)refreshStatusProperty.GetValue(hosts[pane]) ?? string.Empty;
                        if (status.StartsWith("refreshed;", StringComparison.Ordinal) && !sawRefreshCompletion)
                        {
                            sawRefreshCompletion = true;
                            completionCountAtRefresh = count;
                            completionAtRefreshMilliseconds = deferredRefreshClock.ElapsedMilliseconds;
                        }
                        if (count == 4 && newRowsAtMilliseconds < 0)
                            newRowsAtMilliseconds = deferredRefreshClock.ElapsedMilliseconds;
                        if (count != previousSampleCount || !string.Equals(status, previousSampleStatus, StringComparison.Ordinal))
                        {
                            var sample = $"{deferredRefreshClock.ElapsedMilliseconds}ms/count={count}/status={status}";
                            deferredRefreshTimeline.Add(sample);
                            previousSampleCount = count;
                            previousSampleStatus = status;
                        }
                        return expectedItems.All(File.Exists) && count == expectedItems.Length;
                    }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(10));
                }
                else
                {
                    refreshed = WaitForFolderItems(hostType, hosts[pane], folders[pane], expectedItems, TimeSpan.FromSeconds(10));
                }
                // Shell rows can match the directory before the asynchronous selection/focus restore finishes.
                var restorationSettled = WaitForPredicate(() =>
                {
                    var status = (string?)refreshStatusProperty.GetValue(hosts[pane]);
                    var restoreMilliseconds = (long)refreshRestoreMillisecondsProperty.GetValue(hosts[pane])!;
                    var pending = (bool)selectionRestorePendingField.GetValue(hosts[pane])!;
                    return status is not null && status != refreshStatusBefore && status.StartsWith("refreshed;", StringComparison.Ordinal) &&
                        restoreMilliseconds != refreshRestoreMillisecondsBefore && !pending;
                }, TimeSpan.FromSeconds(5));
                refreshed &= Count(hosts[pane]) == (deleteSelected ? 2 : 4);
                var selectedAfterRefresh = SelectedPaths(hostType, hosts[pane], folders[pane]);
                var focusAfterRefresh = (string?)focusMethod.Invoke(hosts[pane], [folders[pane]]);
                var selectionRestoredByRefresh = (bool)hostType.GetProperty("DirectoryChangeLastRefreshSelectionRestored", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[pane])!;
                var focusRestoredByRefresh = (bool)hostType.GetProperty("DirectoryChangeLastRefreshFocusRestored", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[pane])!;
                // Refresh must return to the user's pre-change selection; Shell may temporarily select the focused row while enumerating the new item.
                var expectedSelectionAfterRefresh = deleteSelected ? expectedSelection.Where(File.Exists).ToArray() : expectedSelection;
                var selectionPreserved = expectedSelectionAfterRefresh.SequenceEqual(selectedAfterRefresh, StringComparer.OrdinalIgnoreCase);
                var focusPreserved = deleteSelected && !File.Exists(focusBeforeRefresh ?? string.Empty)
                    ? focusAfterRefresh is not null && selectedAfterRefresh.Contains(focusAfterRefresh, StringComparer.OrdinalIgnoreCase)
                    : string.Equals(focusBeforeRefresh, focusAfterRefresh, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"shell-refresh pane={pane} delete-selected={deleteSelected} deferred-notification-probe={deferredNotificationProbe} deferred-shell-copy={deferredShellCopyProbe} shell-copy-order=[{string.Join('|', orderBeforeDeferredShellCopy)}]->[{string.Join('|', orderAfterDeferredShellCopy)}] soft-refresh-f5={softRefreshF5Rows}/{softRefreshF5Status};order=[{string.Join('|', softRefreshOrderBeforeF5 ?? [])}]->[{string.Join('|', softRefreshOrderAfterF5 ?? [])}] rows-before-refresh-call={rowsImmediatelyBeforeRefresh} flush-notification={notificationObservation} counts-before-refresh={string.Join(',', beforeRefresh)} counts-after-refresh={string.Join(',', hosts.Select(Count))} selected-count={selectedBeforeRefresh.Length}/{selectedAfterRefresh.Length} selection-restored={selectionPreserved} focused-before={focusBeforeRefresh} focused-after={focusAfterRefresh} focus-restored={focusPreserved} refresh-restoration-settled={restorationSettled} product-selection-restored={selectionRestoredByRefresh} product-focus-restored={focusRestoredByRefresh} refreshed={refreshed} completion-count={completionCountAtRefresh} completion-ms={completionAtRefreshMilliseconds} new-rows-ms={newRowsAtMilliseconds} refresh-timeline=[{string.Join(';', deferredRefreshTimeline)}] paint-transition={paintTransitionSnapshot.GetValue(hosts[pane])} view-message-delta={ViewMessageDelta()} view-message-samples={ViewMessageSamples()} message-only-delta={refreshMessageObserver.Delta} message-only-samples={refreshMessageObserver.SampleDelta}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return refreshed && restorationSettled && selectionRestoredByRefresh && focusRestoredByRefresh && selectionPreserved && focusPreserved ? 0 : 1;
            }
            if (args[0] is "--cross-pane-drop" or "--cross-pane-drop-multi" or "--cross-pane-drop-effects")
            {
                var multiDrop = args[0] == "--cross-pane-drop-multi";
                var effectDrop = args[0] == "--cross-pane-drop-effects";
                if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Cross-pane drop Shell views were not attached.");
                var initialCountProperty = hostType.GetProperty("DirectoryChangeInitialCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var watcherField = hostType.GetField("_directoryChangeWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!;
                bool MonitorsReady() => hosts.Select((host, index) =>
                    initialCountProperty.GetValue(host) is int count && count == (index == 0 ? (effectDrop ? 4 : multiDrop ? 3 : 2) : 1) &&
                    watcherField.GetValue(host) is FileSystemWatcher watcher && watcher.EnableRaisingEvents).All(ready => ready);
                if (!WaitForPredicate(MonitorsReady, TimeSpan.FromSeconds(10)))
                    throw new Exception("Production directory monitors did not reach their settled fixture baseline.");
                if (effectDrop)
                {
                    // Keep both operation effects available so modifier-key selection is tested by the target.
                    var ctrlCopy = ShellDropProbe.Transfer(hostType, hosts[1], folders[0], folders[1], ["drop-control.txt"], 0x9, 0x3, 0x1);
                    var shiftMove = ShellDropProbe.Transfer(hostType, hosts[1], folders[0], folders[1], ["drop-shift.txt"], 0x5, 0x3, 0x2);
                    var defaultMove = ShellDropProbe.Transfer(hostType, hosts[1], folders[0], folders[1], ["drop-default.txt"], 0x1, 0x3, 0x2);
                    var expectedTargetItems = new[] { "initial.txt", "drop-control.txt", "drop-shift.txt", "drop-default.txt" }
                        .Select(name => Path.Combine(folders[1], name)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    var targetViewUpdatedForAll = WaitForFolderItems(hostType, hosts[1], folders[1], expectedTargetItems, TimeSpan.FromSeconds(10));
                    var sourceViewUpdatedForAll = WaitForFolderItems(hostType, hosts[0], folders[0],
                        new[] { Path.Combine(folders[0], "initial.txt"), Path.Combine(folders[0], "drop-control.txt") }
                            .Order(StringComparer.OrdinalIgnoreCase).ToArray(), TimeSpan.FromSeconds(10));
                    var unrelatedPanesUnchangedForAll = Count(hosts[2]) == 1 && Count(hosts[3]) == 1;
                    var effectRefreshCountProperty = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Console.WriteLine($"cross-pane-shell-drop-effects copy-only={ctrlCopy == 0} move-only-shift={shiftMove == 0} move-only-default={defaultMove == 0} source-view-updated={sourceViewUpdatedForAll} target-view-updated={targetViewUpdatedForAll} source-fallback-refreshes={effectRefreshCountProperty.GetValue(hosts[0])} target-fallback-refreshes={effectRefreshCountProperty.GetValue(hosts[1])} unrelated-panes-unchanged={unrelatedPanesUnchangedForAll}");
                    return ctrlCopy == 0 && shiftMove == 0 && defaultMove == 0 && sourceViewUpdatedForAll && targetViewUpdatedForAll && unrelatedPanesUnchangedForAll ? 0 : 1;
                }
                var sourceNames = multiDrop ? new[] { "drop-source-1.txt", "drop-source-2.txt" } : ["drop-source.txt"];
                var dropResult = ShellDropProbe.Copy(hostType, hosts[1], folders[0], folders[1], sourceNames);
                Console.WriteLine($"cross-pane-shell-drop foreground-after-com-release {ShellDropProbe.DescribeForeground()}");
                var targetItems = sourceNames.Select(name => Path.Combine(folders[1], name))
                    .Append(Path.Combine(folders[1], "initial.txt")).ToArray();
                var targetViewUpdated = WaitForFolderItems(hostType, hosts[1], folders[1], targetItems, TimeSpan.FromSeconds(10));
                var unrelatedPanesUnchanged = Count(hosts[0]) == (multiDrop ? 3 : 2) && Count(hosts[2]) == 1 && Count(hosts[3]) == 1;
                var refreshCountProperty = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Console.WriteLine($"cross-pane-shell-drop multi={multiDrop} target-view-updated={targetViewUpdated} production-watcher-refreshes={refreshCountProperty.GetValue(hosts[1])} unrelated-panes-unchanged={unrelatedPanesUnchanged}");
                return dropResult == 0 && targetViewUpdated && unrelatedPanesUnchanged ? 0 : 1;
            }
            if (args[0] == "--ctrl-wheel-routing")
            {
                if (!WaitForCounts(hosts, expected: 3, TimeSpan.FromSeconds(10)))
                    throw new Exception("Ctrl-wheel fixture items did not appear in the Shell view.");
                if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell view HWNDs were not attached before the routing probe.");
                var hwnd = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var readStyle = hostType.GetMethod("ReadViewStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var setStyle = hostType.GetMethod("SetViewStyle", BindingFlags.Instance | BindingFlags.Public)!;
                var styleType = assembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
                var detailStyle = Activator.CreateInstance(styleType, "probe Details", 4u, 16)
                    ?? throw new Exception("Could not create a Details baseline.");
                setStyle.Invoke(hosts[0], [detailStyle]);
                var before = WaitForStyle(hosts[0], readStyle);
                if (!Equals(before, (4u, 16))) throw new Exception($"Shell view did not accept the Details baseline: {before}.");
                SelectAll(hostType, hosts[0]);
                var selectionBefore = SelectedPaths(hostType, hosts[0], folders[0]);
                var otherBefore = hosts.Skip(1).Select(host => readStyle.Invoke(host, null)).ToArray();
                var cycle = assembly.GetType("ExplorerDirs.App.ViewStyleWheelCycle", true)!
                    .GetMethod("Next", BindingFlags.Static | BindingFlags.Public)!;
                var handler = hostType.GetProperty("CtrlWheelHandler")!;
                handler.SetValue(hosts[0], new Func<int, bool>(delta =>
                {
                    var current = readStyle.Invoke(hosts[0], null) is ValueTuple<uint, int> style
                        ? style : throw new Exception("Current view mode became unreadable.");
                    var next = cycle.Invoke(null, [current.Item1, current.Item2, delta])!;
                    return (bool)setStyle.Invoke(hosts[0], [next])!;
                }));
                // Isolated synthetic messages to the first Shell view child only;
                // this does not use the desktop cursor or send physical input.
                var expectedModes = new uint[] { 3, 2, 1, 1, 1, 8, 6, 4 };
                for (var i = 0; i < expectedModes.Length; i++)
                {
                    SendMessage(hwnd, 0x020A, (nint)((120 << 16) | 0x0008), IntPtr.Zero);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    var current = readStyle.Invoke(hosts[0], null);
                    if (current is not ValueTuple<uint, int> currentStyle || currentStyle.Item1 != expectedModes[i])
                        throw new Exception($"Ctrl-wheel step {i + 1} selected unexpected view style: {current}.");
                    var selection = SelectedPaths(hostType, hosts[0], folders[0]);
                    if (!selection.SequenceEqual(selectionBefore, StringComparer.OrdinalIgnoreCase))
                        throw new Exception($"Ctrl-wheel step {i + 1} changed the selected items.");
                }
                var after = readStyle.Invoke(hosts[0], null);
                var otherAfter = hosts.Skip(1).Select(host => readStyle.Invoke(host, null)).ToArray();
                Console.WriteLine($"ctrl-wheel target=view-child before={before} after={after} selection-preserved={selectionBefore.SequenceEqual(SelectedPaths(hostType, hosts[0], folders[0]), StringComparer.OrdinalIgnoreCase)} selected-count={selectionBefore.Length} other-panes-unchanged={otherBefore.SequenceEqual(otherAfter)}");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return Equals(after, (4u, 16)) && otherBefore.SequenceEqual(otherAfter) ? 0 : 1;
            }
            if (args[0] == "--rename-editor-key-routing")
            {
                if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell view HWNDs were not attached before the rename-key routing probe.");
                var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var editor = CreateWindowEx(0, "EDIT", "rename-test.txt", 0x50000080, 0, 0, 200, 24,
                    viewWindow, 0, 0, 0);
                if (editor == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the hidden edit control.");
                try
                {
                    SetFocus(editor);
                    if (GetFocus() != editor)
                        throw new Exception($"The hidden edit control could not acquire thread focus: expected={editor:X} actual={GetFocus():X}.");
                    var translate = hostType.GetMethod("TranslateAcceleratorCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var text = new System.Text.StringBuilder(128);
                    foreach (var key in new[] { 0x25, 0x26, 0x27, 0x28 })
                    {
                        var msg = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)key, lParam = (nint)1 };
                        var handled = translate.Invoke(hosts[0], [msg, ModifierKeys.None]);
                        if (!Equals(handled, true) || GetFocus() != editor)
                            throw new Exception($"Rename arrow {key:X} was not consumed by the focused Shell editor; handled={handled}, focus={GetFocus():X}.");
                        GetWindowText(editor, text, text.Capacity);
                        if (text.ToString() != "rename-test.txt")
                            throw new Exception($"Rename arrow {key:X} changed the editor text unexpectedly: {text}.");
                    }
                    Console.WriteLine($"rename-editor-key-routing handled=left/up/right/down focus-retained=True text={text}");
                }
                finally { DestroyWindow(editor); }
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return 0;
            }
            if (args[0] == "--live-shell-rename-editor-mode")
            {
                if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell view HWNDs were not attached before the Shell rename view-mode probe.");
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(10)))
                    throw new Exception("Rename fixture item did not appear in the Shell view.");
                var probeIndexes = args[1].Split(':');
                var paneIndex = int.Parse(probeIndexes[0]);
                var styleIndex = int.Parse(probeIndexes[1]);
                VerifyRenameEditorAcrossViewStyles(hostType, hosts, folders, paneIndex, styleIndex);
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return 0;
            }
            if (args[0] == "--live-shell-rename-editor" ||
                args[0].StartsWith("--live-shell-rename-editor-pane-", StringComparison.Ordinal))
            {
                if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell view HWNDs were not attached before the Shell rename probe.");
                if (!WaitForCounts(hosts, expected: 1, TimeSpan.FromSeconds(10)))
                    throw new Exception("Rename fixture item did not appear in the Shell view.");
                var isRenameFocused = hostType.GetMethod("IsRenameEditorFocused", BindingFlags.Instance | BindingFlags.Public)!;
                var translate = hostType.GetMethod("TranslateAcceleratorCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var routedPanes = new List<int>();
                var paneIndexes = args[0] == "--live-shell-rename-editor"
                    ? new[] { 0 }
                    : new[] { int.Parse(args[0]["--live-shell-rename-editor-pane-".Length..]) };
                foreach (var paneIndex in paneIndexes)
                {
                    SelectOnlyItem(hostType, hosts[paneIndex], folders[paneIndex], "initial.txt", takeFocus: true);
                    var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[paneIndex])!;
                    var f2 = new MSG { hwnd = viewWindow, message = 0x0100, wParam = (nint)0x71, lParam = (nint)1 };
                    if (!Equals(translate.Invoke(hosts[paneIndex], [f2, ModifierKeys.None]), true))
                        throw new Exception($"Pane {paneIndex} production key route did not accept F2.");
                    var editorReady = WaitForPredicate(() => (bool)isRenameFocused.Invoke(hosts[paneIndex], null)!, TimeSpan.FromSeconds(2));
                    if (!editorReady)
                        throw new Exception($"Pane {paneIndex} did not enter Shell rename mode: focus={GetFocus():X} children={string.Join(';', ChildWindowDescriptions(viewWindow))}.");

                    var editor = GetFocus();
                    for (var otherPane = 0; otherPane < hosts.Count; otherPane++)
                    {
                        if (otherPane != paneIndex && (bool)isRenameFocused.Invoke(hosts[otherPane], null)!)
                            throw new Exception($"Pane {paneIndex} rename editor focus was attributed to pane {otherPane}.");
                    }
                    // Keep focus assertions honest: arrows must still edit the name, not merely be swallowed.
                    SendMessage(editor, 0x00B1, (nint)4, (nint)4); // EM_SETSEL: place the caret inside the filename.
                    if (ReadEditSelection(editor) != (4, 4))
                        throw new Exception($"Pane {paneIndex} Shell rename editor did not accept an interior caret position: {ReadEditSelection(editor)}.");
                    var left = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)0x25, lParam = (nint)1 };
                    if (!Equals(translate.Invoke(hosts[paneIndex], [left, ModifierKeys.None]), true) ||
                        GetFocus() != editor || ReadEditSelection(editor) != (3, 3))
                        throw new Exception($"Pane {paneIndex} Shell rename Left did not move the text caret: selection={ReadEditSelection(editor)} focus={GetFocus():X}.");
                    SendMessage(editor, 0x00B1, (nint)4, (nint)4);
                    var right = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)0x27, lParam = (nint)1 };
                    if (!Equals(translate.Invoke(hosts[paneIndex], [right, ModifierKeys.None]), true) ||
                        GetFocus() != editor || ReadEditSelection(editor) != (5, 5))
                        throw new Exception($"Pane {paneIndex} Shell rename Right did not move the text caret: selection={ReadEditSelection(editor)} focus={GetFocus():X}.");
                    var originalKeyboardState = new byte[256];
                    if (!GetKeyboardState(originalKeyboardState))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not capture the probe thread keyboard state before Shift+arrow rename checks.");
                    try
                    {
                        var shiftKeyboardState = originalKeyboardState.ToArray();
                        shiftKeyboardState[0x10] |= 0x80; // VK_SHIFT: let the native Edit see the modifier while the message is routed synthetically.
                        shiftKeyboardState[0xA0] |= 0x80; // VK_LSHIFT: retain the physical-key identity used by native controls.
                        if (!SetKeyboardState(shiftKeyboardState))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not set Shift state for the native rename editor probe.");
                        SendMessage(editor, 0x00B1, (nint)4, (nint)4);
                        var shiftLeft = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)0x25, lParam = (nint)1 };
                        if (!Equals(translate.Invoke(hosts[paneIndex], [shiftLeft, ModifierKeys.Shift]), true) ||
                            GetFocus() != editor || ReadEditSelection(editor) != (3, 4))
                            throw new Exception($"Pane {paneIndex} Shell rename Shift+Left did not select text inside the editor: selection={ReadEditSelection(editor)} focus={GetFocus():X}.");
                        SendMessage(editor, 0x00B1, (nint)4, (nint)4);
                        var shiftRight = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)0x27, lParam = (nint)1 };
                        if (!Equals(translate.Invoke(hosts[paneIndex], [shiftRight, ModifierKeys.Shift]), true) ||
                            GetFocus() != editor || ReadEditSelection(editor) != (4, 5))
                            throw new Exception($"Pane {paneIndex} Shell rename Shift+Right did not select text inside the editor: selection={ReadEditSelection(editor)} focus={GetFocus():X}.");
                    }
                    finally
                    {
                        if (!SetKeyboardState(originalKeyboardState))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the probe thread keyboard state after Shift+arrow rename checks.");
                    }
                    var keyboardStateAfterShiftChecks = new byte[256];
                    if (!GetKeyboardState(keyboardStateAfterShiftChecks) ||
                        !originalKeyboardState.SequenceEqual(keyboardStateAfterShiftChecks))
                        throw new Exception("The Shift+arrow rename probe did not restore the probe thread keyboard state.");
                    foreach (var key in new[] { 0x25, 0x26, 0x27, 0x28 })
                    {
                        var msg = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)key, lParam = (nint)1 };
                        var handled = translate.Invoke(hosts[paneIndex], [msg, ModifierKeys.None]);
                        if (!Equals(handled, true) || GetFocus() != editor || !(bool)isRenameFocused.Invoke(hosts[paneIndex], null)!)
                            throw new Exception($"Pane {paneIndex} Shell rename arrow {key:X} escaped the editor; handled={handled}, focus={GetFocus():X}.");
                    }
                    routedPanes.Add(paneIndex);
                }
                Console.WriteLine($"live-shell-rename-editor panes={string.Join(',', routedPanes)} handled=left/up/right/down shift-left/right-selection=True caret-left/right=True focus-retained=True cross-pane-isolation=True");
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return 0;
            }
            if (args[0] == "--ctrl-wheel-window-chain")
            {
                if (!WaitForAttachedViews(hostType, hosts, TimeSpan.FromSeconds(10)))
                    throw new Exception("Shell view HWNDs were not attached before the routing probe.");
                var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var hostWindow = (nint)hostType.GetField("_hostWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosts[0])!;
                var readStyle = hostType.GetMethod("ReadViewStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var setStyle = hostType.GetMethod("SetViewStyle", BindingFlags.Instance | BindingFlags.Public)!;
                var styleType = assembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
                var detailedStyle = Activator.CreateInstance(styleType, "probe baseline", 4u, 16)
                    ?? throw new Exception("Could not create a stable detailed-view style for the routing probe.");
                var allStyles = ((System.Collections.IEnumerable)styleType.GetProperty("All")!.GetValue(null)!).Cast<object>().ToArray();
                var candidates = new List<nint> { viewWindow };
                for (var parent = GetParent(viewWindow); parent != 0; parent = GetParent(parent))
                {
                    candidates.Add(parent);
                    if (parent == hostWindow) break;
                }
                if (!candidates.Contains(source.Handle)) candidates.Add(source.Handle);
                setStyle.Invoke(hosts[0], new[] { detailedStyle });
                var baseline = WaitForStyle(hosts[0], readStyle);
                if (baseline is null || !baseline.Equals((4u, 16)))
                    throw new Exception($"Shell did not accept the stable detailed-view baseline: {baseline}.");
                Console.WriteLine($"ctrl-wheel-chain baseline={baseline} chain={string.Join(" -> ", candidates.Select(DescribeWindow))}");
                var changed = false;
                foreach (var target in candidates)
                {
                    setStyle.Invoke(hosts[0], new[] { detailedStyle });
                    if (!Equals(WaitForStyle(hosts[0], readStyle), baseline))
                        throw new Exception("Could not restore the detailed-view baseline between window targets.");
                    SendMessage(target, 0x020A, (nint)((120 << 16) | 0x0008), IntPtr.Zero);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    var after = readStyle.Invoke(hosts[0], null);
                    var targetDescription = DescribeWindow(target);
                    Console.WriteLine($"ctrl-wheel-target={targetDescription} before={baseline} after={after}");
                    changed |= !Equals(after, baseline);
                }
                setStyle.Invoke(hosts[0], new[] { detailedStyle });
                if (!Equals(WaitForStyle(hosts[0], readStyle), baseline))
                    throw new Exception("Could not restore the detailed-view baseline before the wheel sequence.");
                var upSequence = new List<object?>();
                for (var i = 0; i < 8; i++)
                {
                    SendMessage(viewWindow, 0x020A, (nint)((120 << 16) | 0x0008), IntPtr.Zero);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    upSequence.Add(readStyle.Invoke(hosts[0], null));
                }
                Console.WriteLine($"ctrl-wheel-sequence direction=up states={string.Join(',', upSequence)}");
                setStyle.Invoke(hosts[0], new[] { detailedStyle });
                if (!Equals(WaitForStyle(hosts[0], readStyle), baseline))
                    throw new Exception("Could not restore the detailed-view baseline before the down sequence.");
                var downSequence = new List<object?>();
                for (var i = 0; i < 8; i++)
                {
                    SendMessage(viewWindow, 0x020A, (nint)(((-120 & 0xFFFF) << 16) | 0x0008), IntPtr.Zero);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    downSequence.Add(readStyle.Invoke(hosts[0], null));
                }
                Console.WriteLine($"ctrl-wheel-sequence direction=down states={string.Join(',', downSequence)}");
                foreach (var style in allStyles)
                {
                    setStyle.Invoke(hosts[0], new[] { style });
                    var modeBaseline = WaitForStyle(hosts[0], readStyle)
                        ?? throw new Exception("Shell view was unreadable while establishing the mode matrix.");
                    SendMessage(viewWindow, 0x020A, (nint)((120 << 16) | 0x0008), IntPtr.Zero);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    var up = readStyle.Invoke(hosts[0], null);
                    var baselineType = modeBaseline.GetType();
                    var exactRestoreStyle = Activator.CreateInstance(styleType, "matrix restore",
                        (uint)baselineType.GetField("Item1")!.GetValue(modeBaseline)!,
                        (int)baselineType.GetField("Item2")!.GetValue(modeBaseline)!);
                    setStyle.Invoke(hosts[0], new[] { exactRestoreStyle });
                    var restored = WaitForStyle(hosts[0], readStyle);
                    if (!Equals(restored, modeBaseline))
                    {
                        Console.WriteLine($"ctrl-wheel-style name={styleType.GetProperty("Name")!.GetValue(style)} baseline={modeBaseline} up={up} down=skipped restore={restored}");
                        continue;
                    }
                    SendMessage(viewWindow, 0x020A, (nint)(((-120 & 0xFFFF) << 16) | 0x0008), IntPtr.Zero);
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                    var down = readStyle.Invoke(hosts[0], null);
                    Console.WriteLine($"ctrl-wheel-style name={styleType.GetProperty("Name")!.GetValue(style)} baseline={modeBaseline} up={up} down={down}");
                }
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                Console.WriteLine($"ctrl-wheel-chain other-panes={string.Join('|', hosts.Skip(1).Select(h => readStyle.Invoke(h, null)))} changed={changed}");
                return changed ? 0 : 1;
            }
            if (args[0] == "--search-navigation")
            {
                // Search requests normally restore content focus. Hidden WPF
                // hosts must reject that path, even with a hidden native parent.
                foreach (var host in hosts) host.Visibility = Visibility.Hidden;
                SearchHostProbe.Run(hosts, folders, host => Names(host));
                if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
                return 0;
            }
            var sequenceTime = Stopwatch.StartNew();
            using var observer = new ChangeNotificationObserver(source, root, sequenceTime);
            using var independentObserver = args[0] is "--live-shell-independent-sta" or "--live-shell-child-process"
                ? new StaNotificationObserver(root, sequenceTime) : null;
            var createdEvents = 0; var deletedEvents = 0; var watcherErrors = 0;
            using var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName };
            watcher.Created += (_, _) => Interlocked.Increment(ref createdEvents);
            watcher.Deleted += (_, _) => Interlocked.Increment(ref deletedEvents);
            watcher.Error += (_, _) => Interlocked.Increment(ref watcherErrors);
            watcher.EnableRaisingEvents = true;
            using var childProducer = args[0] == "--live-shell-child-process" ? new ProcessFixtureOperations() : null;
            using var messageOnlyObserver = args[0] is "--live-updates" or "--live-shell-operations" or "--live-shell-independent-sta" or "--live-shell-child-process"
                ? new MessageOnlyWindowObserver(GetWindowThreadProcessId(source.Handle, out _)) : null;
            var passed = RunSequence(hosts, folders, sequenceTime, () =>
            {
                CaptureDiagnosticsBaseline();
                messageOnlyObserver?.CaptureBaseline();
                if (args[0] == "--live-updates-without-subclass")
                    foreach (var host in hosts)
                        hostType.GetMethod("DetachViewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
            }, CaptureDiagnosticsBaseline, () => "host-sta={" + observer.Summary + "}; independent-sta={" + (independentObserver?.Summary ?? "disabled") +
                $"}}; browser-sites={BrowserSiteNotifications()}; view-message-delta={ViewMessageDelta()}; view-message-samples={ViewMessageSamples()}; message-only-windows={messageOnlyObserver?.WindowList ?? "disabled"}; message-only-delta={messageOnlyObserver?.Delta ?? "disabled"}; message-only-samples={messageOnlyObserver?.SampleDelta ?? "disabled"}; filesystem={Volatile.Read(ref createdEvents)}/{Volatile.Read(ref deletedEvents)}/{Volatile.Read(ref watcherErrors)}",
                childProducer is not null ? copy => childProducer.RunAsync(root, copy) :
                args[0] is "--live-shell-operations" or "--live-shell-independent-sta" ? copy =>
                {
                    ShellFixtureOperations.Run(root, folders, copy);
                    return Task.CompletedTask;
                } : null);
            childProducer?.EnsureRunning();
            if (IsWindowVisible(source.Handle)) throw new Exception("Probe parent became visible.");
            Console.WriteLine("Hidden production host observation complete; no GUI acceptance claim.");
            return passed ? 0 : 1;
        }
        finally
        {
            foreach (var host in hosts) host.Dispose();
            if (args[0] is "--live-shell-directory-stress" or "--live-shell-directory-stress-native" or "--live-shell-directory-stress-large")
            {
                WriteDirectoryStressMemoryCheckpoint("after-host-dispose");
                if (Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_FORCE_GC_AFTER_DISPOSE") == "1")
                {
                    // This opt-in control distinguishes collectible managed garbage from process/native retention.
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
                    WriteDirectoryStressMemoryCheckpoint("after-host-dispose-forced-gc");
                }
            }
            if (junctionPath is not null) DeleteDirectoryJunction(junctionPath);
            // These hidden navigation/filter probes each allocate their own four-pane fixture under the build output.
            if (args[0] is "--search-navigation" or "--quick-filter" or "--column-value-filter" or "--file-view-export" or "--selected-paths-clipboard")
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (args[0] is "--file-command-states" or "--file-command-states-early")
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (args[0] == "--shell-change-registration-after-hosts")
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (args[0] is "--live-shell-directory-stress" or "--live-shell-directory-stress-native" or "--live-shell-directory-stress-large")
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (args[0] == "--live-shell-rename-editor" ||
                args[0].StartsWith("--live-shell-rename-editor-pane-", StringComparison.Ordinal))
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (args[0] == "--live-shell-selection-large")
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (args[0] == "--live-shell-empty-directory-deferred-restore")
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
            if (crossVolumeProbe)
            {
                DeleteGeneratedRoot(root, Path.Combine(AppContext.BaseDirectory, "fixtures"));
                if (crossVolumeRoot is not null && crossVolumeCleanupParent is not null)
                    DeleteGeneratedRoot(crossVolumeRoot, crossVolumeCleanupParent);
            }
        }

    }

    private static void WriteDirectoryStressMemoryCheckpoint(string phase)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        // Keep the observation passive: managed heap bytes may include uncollected garbage and no forced GC is triggered.
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var gc = GC.GetGCMemoryInfo();
        var lastGc = gen0 + gen1 + gen2 == 0
            ? "none"
            : $"heap-bytes={gc.HeapSizeBytes},fragmented-bytes={gc.FragmentedBytes},committed-bytes={gc.TotalCommittedBytes}";
        Console.WriteLine($"memory-checkpoint phase={phase} private-bytes={process.PrivateMemorySize64} working-set-bytes={process.WorkingSet64} managed-heap-allocated-bytes={GC.GetTotalMemory(forceFullCollection: false)} last-gc={lastGc} gen0={gen0} gen1={gen1} gen2={gen2} handles={process.HandleCount} threads={process.Threads.Count}");
    }

    private static int RunWatcherOverflowProbe(int fileCount)
    {
        if (fileCount is < 1000 or > 250000) return 2;
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs.HostProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            using var watcher = new FileSystemWatcher(fixtureRoot)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 16 * 1024
            };
            var createdEvents = 0;
            var errorEvents = 0;
            watcher.Created += (_, _) => Interlocked.Increment(ref createdEvents);
            watcher.Error += (_, _) => Interlocked.Increment(ref errorEvents);
            watcher.EnableRaisingEvents = true;
            var timer = Stopwatch.StartNew();
            Parallel.For(0, fileCount, index =>
                File.WriteAllBytes(Path.Combine(fixtureRoot, $"item-{index:D6}.tmp"), []));
            timer.Stop();
            if (!SpinWait.SpinUntil(() => Volatile.Read(ref createdEvents) + Volatile.Read(ref errorEvents) > 0, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The watcher did not deliver any create or error callback.");
            Thread.Sleep(1000);
            watcher.EnableRaisingEvents = false;
            Console.WriteLine($"watcher-overflow-probe files={fileCount} created-events={createdEvents} error-events={errorEvents} buffer-size={watcher.InternalBufferSize} producer-ms={timer.ElapsedMilliseconds}");
            return errorEvents > 0 ? 0 : 1;
        }
        finally
        {
            var resolvedRoot = Path.GetFullPath(fixtureRoot);
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExplorerDirs.HostProbe"));
            if (!resolvedRoot.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The overflow fixture escaped its dedicated temporary directory.");
            if (Directory.Exists(resolvedRoot)) Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    internal static int Count(HwndHost host)
    {
        var method = host.GetType().GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var view = (nint)method.Invoke(host, null)!;
        if (view == 0) return -1;
        try
        {
            var read = Marshal.GetDelegateForFunctionPointer<ItemCount>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(view), 7 * nint.Size));
            return read(view, 2, out var count) < 0 ? -1 : count;
        }
        finally { Marshal.Release(view); }
    }

    private static void DeleteGeneratedRoot(string path, string expectedParent)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetFullPath(expectedParent);
        if (!string.Equals(Path.GetDirectoryName(fullPath), parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Generated fixture cleanup escaped its dedicated parent directory.");
        if (!Directory.Exists(fullPath)) return;
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Generated fixture cleanup refused to recurse into a reparse point.");
        Directory.Delete(fullPath, recursive: true);
    }

    private static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Arguments = $"/d /c mklink /J \"{junctionPath}\" \"{targetPath}\"";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start cmd.exe to create the junction fixture.");
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Junction fixture creation timed out.");
        }
        if (process.ExitCode != 0 || !Directory.Exists(junctionPath) ||
            (File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint) == 0)
            throw new IOException($"Junction fixture creation failed: {process.StandardError.ReadToEnd()} {process.StandardOutput.ReadToEnd()}");
    }

    private static void DeleteDirectoryJunction(string junctionPath)
    {
        if (!Directory.Exists(junctionPath)) return;
        if ((File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint) == 0)
            throw new InvalidOperationException("Junction fixture cleanup refused to delete a normal directory.");
        Directory.Delete(junctionPath, recursive: false);
    }

    private static bool RunSequence(List<HwndHost> hosts, List<string> folders, Stopwatch sequenceTime, Action beforeCreate, Action beforeDelete, Func<string> notifications,
        Func<bool, Task>? shellOperation = null)
    {
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var stage = 0;
        var passed = true;
        var reading = false;
        var lastObservation = string.Empty;
        Exception? failure = null;
        Task? pendingOperation = null;
        long operationStartedAt = 0;
        string operationName = string.Empty;
        void BeginOperation(bool copy)
        {
            operationStartedAt = sequenceTime.ElapsedMilliseconds;
            operationName = copy ? "copy" : "delete";
            pendingOperation = shellOperation!(copy);
        }
        timer.Tick += (_, _) =>
        {
            // Native Shell calls can pump messages; do not reenter the phase transition.
            if (reading) return;
            reading = true;
            try
            {
                if (pendingOperation is not null)
                {
                    if (!pendingOperation.IsCompleted) return;
                    pendingOperation.GetAwaiter().GetResult();
                    pendingOperation = null;
                    var endedAt = sequenceTime.ElapsedMilliseconds;
                    Console.WriteLine($"shell-{operationName} disk-verified=true elapsed-ms={endedAt - operationStartedAt} start-sequence-ms={operationStartedAt} end-sequence-ms={endedAt}");
                    watch.Restart();
                }
                var counts = hosts.Select(Count).ToArray();
                var elapsed = watch.ElapsedMilliseconds;
                var observation = $"stage={stage} counts={string.Join(',', counts)}";
                if (observation != lastObservation)
                {
                    Console.WriteLine($"view-change {observation} sequence-ms={sequenceTime.ElapsedMilliseconds} phase-ms={elapsed}");
                    lastObservation = observation;
                }
                switch (stage)
                {
                    case 0 when counts.All(c => c == 1) || elapsed >= 7000:
                        Console.WriteLine($"production-initial counts={string.Join(',', counts)} elapsed-ms={elapsed}");
                        if (counts.Any(c => c != 1)) throw new Exception("Initial enumeration did not settle.");
                        stage = 1; watch.Restart();
                        break;
                    case 1 when elapsed >= 1500:
                        beforeCreate();
                        if (shellOperation is null)
                            foreach (var folder in folders) File.WriteAllText(Path.Combine(folder, "created.txt"), "created");
                        else
                            BeginOperation(true);
                        stage = 2; watch.Restart();
                        break;
                    case 2 when counts.All(c => c == 2) || elapsed >= 20000:
                        Console.WriteLine($"production-create counts={string.Join(',', counts)} elapsed-ms={elapsed} notifications={notifications()}");
                        // Keep the pane index from binding to Names' optional maximumItems parameter.
                        var createdNames = hosts.Select(host => Names(host)).ToArray();
                        Console.WriteLine("create-identities=" + string.Join(';', createdNames));
                        passed &= counts.All(c => c == 2) && createdNames.All(n => n == "created.txt|initial.txt");
                        beforeDelete();
                        if (shellOperation is null)
                            foreach (var folder in folders) File.Delete(Path.Combine(folder, "initial.txt"));
                        else
                            BeginOperation(false);
                        stage = 3; watch.Restart();
                        break;
                    case 3 when (passed && counts.All(c => c == 1)) || elapsed >= 20000:
                        Console.WriteLine($"production-delete counts={string.Join(',', counts)} elapsed-ms={elapsed} notifications={notifications()}");
                        var deletedNames = hosts.Select(host => Names(host)).ToArray();
                        Console.WriteLine("delete-identities=" + string.Join(';', deletedNames));
                        passed &= counts.All(c => c == 1) && deletedNames.All(n => n == "created.txt");
                        frame.Continue = false;
                        break;
                }
            }
            catch (Exception ex) { failure = ex; frame.Continue = false; }
            finally { reading = false; }
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (failure is not null) throw new InvalidOperationException("Probe failed.", failure);
        return passed;
    }
    private static int RunNaturalRenameObservation(Type hostType, List<HwndHost> hosts, string folder,
        int observationMilliseconds, HwndSource source)
    {
        var host = hosts[0];
        var foregroundBefore = GetForegroundWindow();
        var originalPath = Path.Combine(folder, "natural-rename-before.txt");
        var renamedPath = Path.Combine(folder, "natural-rename-after.txt");
        File.Copy(Path.Combine(folder, "initial.txt"), originalPath);
        if (!WaitForExpectedCounts(hosts, [4, 3, 3, 3], TimeSpan.FromSeconds(5)) ||
            !Names(host).Contains("natural-rename-before.txt", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The rename fixture was not visible before testing natural rename updates.");

        var createTimerField = hostType.GetField("_directoryChangeCreateNotificationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (!WaitForPredicate(() => createTimerField.GetValue(host) is null, TimeSpan.FromSeconds(5)))
            throw new IOException("The setup create notification did not settle before rename observation.");
        var directoryTimer = (DispatcherTimer)hostType.GetField("_directoryChangeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var fallbackCount = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var deletedEventCount = hostType.GetProperty("DirectoryChangeDeletedEventCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var deletedBefore = (int)deletedEventCount.GetValue(host)!;
        var batch = hostType.GetField("_directoryChangeBatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var forceProperty = batch.GetType().GetProperty("ForceRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var refreshInProgress = hostType.GetField("_directoryRefreshInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (!WaitForPredicate(() => !(bool)refreshInProgress.GetValue(host)! && !(bool)forceProperty.GetValue(batch)!, TimeSpan.FromSeconds(5)))
            throw new IOException("The setup create refresh did not settle before rename observation.");
        var fallbackBefore = (int)fallbackCount.GetValue(host)!;

        directoryTimer.Stop();
        directoryTimer.Interval = TimeSpan.FromMilliseconds(observationMilliseconds + 1000);
        var operationClock = Stopwatch.StartNew();
        File.Move(originalPath, renamedPath);
        if (!WaitForPredicate(() => (bool)forceProperty.GetValue(batch)!, TimeSpan.FromSeconds(5)))
            throw new IOException("The product directory watcher did not queue the rename as a forced refresh.");
        var watcherObservedMilliseconds = operationClock.ElapsedMilliseconds;

        long? naturalRenameVisibleMilliseconds = null;
        var observationClock = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var observationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        observationTimer.Tick += (_, _) =>
        {
            var names = Names(host);
            if (Count(host) == 4 && names.Contains("natural-rename-after.txt", StringComparison.OrdinalIgnoreCase) &&
                !names.Contains("natural-rename-before.txt", StringComparison.OrdinalIgnoreCase))
                naturalRenameVisibleMilliseconds ??= observationClock.ElapsedMilliseconds;
            if (observationClock.ElapsedMilliseconds >= observationMilliseconds)
            {
                observationTimer.Stop();
                frame.Continue = false;
            }
        };
        observationTimer.Start();
        Dispatcher.PushFrame(frame);
        var fallbackDuringObservation = (int)fallbackCount.GetValue(host)! - fallbackBefore;

        directoryTimer.Stop();
        directoryTimer.Interval = TimeSpan.FromMilliseconds(250);
        directoryTimer.Start();
        var renamedItemsVisible = WaitForPredicate(() => Count(host) == 4 &&
            Names(host).Contains("natural-rename-after.txt", StringComparison.OrdinalIgnoreCase) &&
            !Names(host).Contains("natural-rename-before.txt", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
        var fallbackAfterRename = (int)fallbackCount.GetValue(host)! - fallbackBefore;
        var finalBatchForced = hostType.GetProperty("DirectoryChangeLastForcedRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host) is true;
        var renameRefreshSettled = WaitForPredicate(() => !(bool)refreshInProgress.GetValue(host)!, TimeSpan.FromSeconds(5));

        File.Delete(renamedPath);
        var cleanupObserved = WaitForPredicate(() => (int)deletedEventCount.GetValue(host)! > deletedBefore, TimeSpan.FromSeconds(5)) &&
            WaitForPredicate(() => hosts.All(pane => Count(pane) == 3) &&
                !Names(host).Contains("natural-rename-after.txt", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
        var hiddenAndForegroundStable = !IsWindowVisible(source.Handle) && GetForegroundWindow() == foregroundBefore;
        Console.WriteLine($"natural-rename observation-ms={observationMilliseconds} natural-visible-ms={naturalRenameVisibleMilliseconds?.ToString() ?? "none"} watcher-forced-refresh=True watcher-seen-ms={watcherObservedMilliseconds} fallback-during-observation={fallbackDuringObservation} fallback-after-resume={fallbackAfterRename} forced-refresh-recorded={finalBatchForced} renamed-items-visible={renamedItemsVisible} rename-refresh-settled={renameRefreshSettled} cleanup-visible={cleanupObserved} hidden-parent={hiddenAndForegroundStable} counts={string.Join(',', hosts.Select(Count))}");
        return fallbackDuringObservation == 0 && fallbackAfterRename == 1 && finalBatchForced && renamedItemsVisible &&
            renameRefreshSettled && cleanupObserved && hiddenAndForegroundStable ? 0 : 1;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint flags, out int count);
    private static string Names(HwndHost host, int maximumItems = 8)
    {
        var method = host.GetType().GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var view = (nint)method.Invoke(host, null)!;
        if (view == 0) return "unavailable";
        nint array = 0;
        try
        {
            var iid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            var result = At<GetItems>(view, 8)(view, 2, ref iid, out array);
            if (result == unchecked((int)0x80070490) &&
                At<ItemCount>(view, 7)(view, 2, out var emptyCount) == 0 && emptyCount == 0)
                return string.Empty;
            Marshal.ThrowExceptionForHR(result);
            Marshal.ThrowExceptionForHR(At<ArrayCount>(array, 7)(array, out var count));
            if (count > maximumItems) throw new Exception($"Unexpected fixture size: {count} items exceeds the probe limit {maximumItems}.");
            var names = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(At<ItemAt>(array, 8)(array, i, out var item));
                nint name = 0;
                try
                {
                    Marshal.ThrowExceptionForHR(At<DisplayName>(item, 5)(item, 0x80058000, out name));
                    names.Add(Path.GetFileName(Marshal.PtrToStringUni(name)) ?? "missing-name");
                }
                finally { if (name != 0) Marshal.FreeCoTaskMem(name); Marshal.Release(item); }
            }
            return string.Join('|', names.Order(StringComparer.Ordinal));
        }
        finally { if (array != 0) Marshal.Release(array); Marshal.Release(view); }
    }
    private static object? WaitForStyle(HwndHost host, System.Reflection.MethodInfo readStyle)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        object? style = null;
        var deadline = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            style = readStyle.Invoke(host, null);
            if (style is not null || deadline.Elapsed >= TimeSpan.FromSeconds(5))
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return style;
    }

    private static bool WaitForCounts(IReadOnlyList<HwndHost> hosts, int expected, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var deadline = Stopwatch.StartNew();
        var matched = false;
        timer.Tick += (_, _) =>
        {
            matched = hosts.All(host => Count(host) == expected);
            if (matched || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return matched;
    }

    private static bool WaitForExpectedCounts(IReadOnlyList<HwndHost> hosts, int[] expected, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var deadline = Stopwatch.StartNew();
        var matched = false;
        timer.Tick += (_, _) =>
        {
            matched = hosts.Select(Count).SequenceEqual(expected);
            if (matched || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return matched;
    }

    private static bool WaitForPredicate(Func<bool> predicate, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = pollInterval ?? TimeSpan.FromMilliseconds(20) };
        var deadline = Stopwatch.StartNew();
        var matched = false;
        timer.Tick += (_, _) =>
        {
            matched = predicate();
            if (matched || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return matched;
    }

    private static void WaitForDelay(TimeSpan delay)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var elapsed = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (elapsed.Elapsed < delay) return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static bool WaitForAttachedViews(Type hostType, IReadOnlyList<HwndHost> hosts, TimeSpan timeout)
    {
        var field = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var deadline = Stopwatch.StartNew();
        var matched = false;
        timer.Tick += (_, _) =>
        {
            matched = hosts.All(host => (nint)field.GetValue(host)! != 0);
            if (matched || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return matched;
    }

    private static void SelectAll(Type hostType, HwndHost host)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        if (view == 0) throw new Exception("Folder view was unavailable while preparing selection.");
        try
        {
            var assembly = hostType.Assembly;
            var commandType = assembly.GetType("ExplorerDirs.App.ShellSelectionCommand", true)!;
            var command = Enum.Parse(commandType, "All");
            var apply = assembly.GetType("ExplorerDirs.App.ShellSelectionCommands", true)!
                .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
            var selected = apply.Invoke(null, [view, command, (Func<bool>)(() => true), (Func<bool>)(() => true)]);
            if (!Equals(selected, true)) throw new Exception("Could not select the fixture item.");
        }
        finally { Marshal.Release(view); }
    }

    private static bool InvokeMenuSelectAllNative(Type hostType, HwndHost host, out long elapsedMilliseconds)
    {
        var focusTarget = (nint)hostType.GetMethod("FindContentFocusWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        var originalFocus = GetFocus();
        var originalKeyboardState = new byte[256];
        if (!GetKeyboardState(originalKeyboardState)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not capture the test thread keyboard state.");
        SetFocus(focusTarget);
        try
        {
            if (GetFocus() != focusTarget)
                throw new Exception($"The hidden Shell view could not acquire probe-thread focus: target={focusTarget:X} actual={GetFocus():X}.");
            var translate = hostType.GetMethod("TrySelectAllViaNativeAccelerator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("The menu-to-Shell native Select All route was not implemented.");
            var clock = Stopwatch.StartNew();
            var handled = translate.Invoke(host, null);
            clock.Stop();
            elapsedMilliseconds = clock.ElapsedMilliseconds;
            return Equals(handled, true);
        }
        finally
        {
            if (originalFocus != 0 && IsWindow(originalFocus)) SetFocus(originalFocus);
            var keyboardStateAfter = new byte[256];
            if (!GetKeyboardState(keyboardStateAfter) || !originalKeyboardState.SequenceEqual(keyboardStateAfter))
                throw new Exception("The product Shell Select All helper did not restore the test thread keyboard state.");
        }
    }

    private static void VerifyMenuSelectAllAcrossViewStyles(Type hostType,
        IReadOnlyList<HwndHost> hosts, IReadOnlyList<string> folders)
    {
        var assembly = hostType.Assembly;
        var viewStyleType = assembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
        var styles = ((System.Collections.IEnumerable)viewStyleType.GetProperty("All")!.GetValue(null)!)
            .Cast<object>().ToArray();
        var setStyle = hostType.GetMethod("SetViewStyle", BindingFlags.Instance | BindingFlags.Public)!;
        var readStyle = hostType.GetMethod("ReadViewStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var noteInput = hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var expectedPaths = Directory.GetFiles(folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var otherPaneSelections = hosts.Skip(1)
            .Select((host, index) => SelectedPaths(hostType, host, folders[index + 1]).Order(StringComparer.OrdinalIgnoreCase).ToArray())
            .ToArray();

        foreach (var style in styles)
        {
            var name = (string)viewStyleType.GetProperty("Name")!.GetValue(style)!;
            var expectedMode = (uint)viewStyleType.GetProperty("Mode")!.GetValue(style)!;
            if (!Equals(setStyle.Invoke(hosts[0], [style]), true))
                throw new Exception($"Shell rejected the {name} baseline for the native Select All matrix.");
            var reachedStyle = WaitForPredicate(() =>
            {
                var state = readStyle.Invoke(hosts[0], null);
                return state is not null && (uint)state.GetType().GetField("Item1")!.GetValue(state)! == expectedMode;
            }, TimeSpan.FromSeconds(5));
            if (!reachedStyle) throw new Exception($"Shell did not settle into the {name} baseline.");

            noteInput.Invoke(hosts[0], null);
            var handled = InvokeMenuSelectAllNative(hostType, hosts[0], out var elapsedMilliseconds);
            var selected = SelectedPaths(hostType, hosts[0], folders[0]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var styleAfter = readStyle.Invoke(hosts[0], null);
            var modeAfter = styleAfter is null ? 0u : (uint)styleAfter.GetType().GetField("Item1")!.GetValue(styleAfter)!;
            var otherPanesPreserved = hosts.Skip(1).Select((host, index) =>
                    SelectedPaths(hostType, host, folders[index + 1]).Order(StringComparer.OrdinalIgnoreCase).ToArray())
                .Select((paths, index) => paths.SequenceEqual(otherPaneSelections[index], StringComparer.OrdinalIgnoreCase))
                .All(preserved => preserved);
            Console.WriteLine($"shell-select-all-mode name={name} mode={modeAfter} handled={handled} selected={selected.Length}/{expectedPaths.Length} identity-match={selected.SequenceEqual(expectedPaths, StringComparer.OrdinalIgnoreCase)} other-panes-preserved={otherPanesPreserved} elapsed-ms={elapsedMilliseconds}");
            if (!handled || modeAfter != expectedMode || !selected.SequenceEqual(expectedPaths, StringComparer.OrdinalIgnoreCase) ||
                !otherPanesPreserved)
                throw new Exception($"Native Select All failed or changed view/other panes in {name} mode.");
        }
    }

    private static string VerifyProductionInvertSelectionModes(Type hostType,
        IReadOnlyList<HwndHost> hosts, IReadOnlyList<string> folders)
    {
        if (hosts.Count != 4 || folders.Count != 4)
            throw new ArgumentException("The production invert-selection probe requires four ExplorerBrowser panes.");
        if (!WaitForExpectedCounts(hosts, [3, 3, 3, 3], TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The production invert-selection fixtures did not enumerate three items per pane.");

        var assembly = hostType.Assembly;
        var styleType = assembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
        var styles = ((System.Collections.IEnumerable)styleType.GetProperty("All")!.GetValue(null)!)
            .Cast<object>().ToArray();
        var setStyle = hostType.GetMethod("SetViewStyle", BindingFlags.Instance | BindingFlags.Public)!;
        var readStyle = hostType.GetMethod("ReadViewStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var noteInput = hostType.GetMethod("NoteNativeUserInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var focusedPath = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var commandType = assembly.GetType("ExplorerDirs.App.ShellSelectionCommand", true)!;
        var invert = Enum.Parse(commandType, "Invert");
        var apply = assembly.GetType("ExplorerDirs.App.ShellSelectionCommands", true)!
            .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
        var restoreFocused = assembly.GetType("ExplorerDirs.App.ShellSelectionCommands", true)!
            .GetMethod("RestoreFocusedItem", BindingFlags.Static | BindingFlags.NonPublic)!;
        var verified = new List<string>(hosts.Count * styles.Length);

        for (var target = 0; target < hosts.Count; target++)
        foreach (var style in styles)
        {
            var name = (string)styleType.GetProperty("Name")!.GetValue(style)!;
            var expectedMode = (uint)styleType.GetProperty("Mode")!.GetValue(style)!;
            if (!Equals(setStyle.Invoke(hosts[target], [style]), true))
                throw new Exception($"ExplorerBrowser rejected the {name} baseline for pane {target + 1}.");
            if (!WaitForPredicate(() =>
                {
                    var current = readStyle.Invoke(hosts[target], null);
                    return current is not null && (uint)current.GetType().GetField("Item1")!.GetValue(current)! == expectedMode;
                }, TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"ExplorerBrowser pane {target + 1} did not settle into {name}.");

            var siblingModes = hosts.Select(host => readStyle.Invoke(host, null)!).ToArray();
            noteInput.Invoke(hosts[target], [IntPtr.Zero]);
            SelectOnlyItem(hostType, hosts[target], folders[target], "initial.txt");
            var originalSelection = SelectedPaths(hostType, hosts[target], folders[target])
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var originalFocus = (string?)focusedPath.Invoke(hosts[target], [folders[target]]);
            var expectedOriginal = Path.Combine(folders[target], "initial.txt");
            if (!originalSelection.SequenceEqual([expectedOriginal], StringComparer.OrdinalIgnoreCase) ||
                !string.Equals(originalFocus, expectedOriginal, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Pane {target + 1} could not establish a single selected, focused identity before inversion in {name}.");

            var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(hosts[target], null)!;
            if (view == 0) throw new Exception($"ExplorerBrowser pane {target + 1} lost its folder view in {name}.");
            try
            {
                void InvertAndRestoreFocus()
                {
                    noteInput.Invoke(hosts[target], [IntPtr.Zero]);
                    var applied = apply.Invoke(null, [view, invert, (Func<bool>)(() => true), (Func<bool>)(() => true)]);
                    if (!Equals(applied, true)) throw new Exception($"The production selection command rejected inversion in pane {target + 1} {name}.");
                    _ = restoreFocused.Invoke(null, [view, false]);
                }

                InvertAndRestoreFocus();
                var complement = SelectedPaths(hostType, hosts[target], folders[target])
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var expectedComplement = new[]
                {
                    Path.Combine(folders[target], "second.txt"),
                    Path.Combine(folders[target], "third.txt")
                }.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var focusAfterInvert = (string?)focusedPath.Invoke(hosts[target], [folders[target]]);
                if (!complement.SequenceEqual(expectedComplement, StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(focusAfterInvert, originalFocus, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"ExplorerBrowser pane {target + 1} produced the wrong identity complement or focus in {name}: selected={string.Join('|', complement)} focus={focusAfterInvert}.");

                InvertAndRestoreFocus();
                var restoredSelection = SelectedPaths(hostType, hosts[target], folders[target])
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var restoredFocus = (string?)focusedPath.Invoke(hosts[target], [folders[target]]);
                if (!restoredSelection.SequenceEqual(originalSelection, StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(restoredFocus, originalFocus, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Two inversions did not restore pane {target + 1}'s selection and focus in {name}.");

                for (var pane = 0; pane < hosts.Count; pane++)
                {
                    if (pane != target && SelectedPaths(hostType, hosts[pane], folders[pane]).Length != 0)
                    {
                        var siblingSelection = SelectedPaths(hostType, hosts[pane], folders[pane]);
                        throw new Exception($"Inverting pane {target + 1} {name} changed sibling pane {pane + 1} selection: {string.Join('|', siblingSelection)}.");
                    }
                    var currentMode = readStyle.Invoke(hosts[pane], null);
                    if (currentMode is null || !Equals(currentMode, siblingModes[pane]))
                        throw new Exception($"Inverting pane {target + 1} {name} changed pane {pane + 1}'s view style.");
                }
            }
            finally
            {
                try
                {
                    noteInput.Invoke(hosts[target], [IntPtr.Zero]);
                    SelectNone(hostType, hosts[target]);
                    if (!WaitForPredicate(() => SelectedPaths(hostType, hosts[target], folders[target]).Length == 0,
                        TimeSpan.FromSeconds(2)))
                        throw new Exception($"Pane {target + 1} selection did not clear after the product invert-selection check in {name}.");
                }
                finally { Marshal.Release(view); }
            }
            verified.Add($"pane{target + 1}:{name}");
        }
        return string.Join(',', verified);
    }

    private static void VerifyRenameEditorAcrossViewStyles(Type hostType,
        IReadOnlyList<HwndHost> hosts, IReadOnlyList<string> folders, int paneIndex, int styleIndex)
    {
        var assembly = hostType.Assembly;
        var styleType = assembly.GetType("ExplorerDirs.App.ViewStyle", true)!;
        var styles = ((System.Collections.IEnumerable)styleType.GetProperty("All")!.GetValue(null)!)
            .Cast<object>().ToArray();
        var style = styles[styleIndex];
        var setStyle = hostType.GetMethod("SetViewStyle", BindingFlags.Instance | BindingFlags.Public)!;
        var readStyle = hostType.GetMethod("ReadViewStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var isRenameFocused = hostType.GetMethod("IsRenameEditorFocused", BindingFlags.Instance | BindingFlags.Public)!;
        var translate = hostType.GetMethod("TranslateAcceleratorCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var viewWindow = (nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hosts[paneIndex])!;
        var name = (string)styleType.GetProperty("Name")!.GetValue(style)!;
        var expectedMode = (uint)styleType.GetProperty("Mode")!.GetValue(style)!;
        if (!Equals(setStyle.Invoke(hosts[paneIndex], [style]), true))
            throw new Exception($"Pane {paneIndex} Shell rejected the {name} baseline for the rename editor probe.");
        if (!WaitForPredicate(() =>
            {
                var current = readStyle.Invoke(hosts[paneIndex], null);
                return current is not null && (uint)current.GetType().GetField("Item1")!.GetValue(current)! == expectedMode;
            }, TimeSpan.FromSeconds(5)))
            throw new Exception($"Pane {paneIndex} Shell did not settle into {name} mode before F2.");

        SelectOnlyItem(hostType, hosts[paneIndex], folders[paneIndex], "initial.txt", takeFocus: true);
        var selectedBeforeRename = SelectedPaths(hostType, hosts[paneIndex], folders[paneIndex])
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var f2 = new MSG { hwnd = viewWindow, message = 0x0100, wParam = (nint)0x71, lParam = (nint)1 };
        if (!Equals(translate.Invoke(hosts[paneIndex], [f2, ModifierKeys.None]), true) ||
            !WaitForPredicate(() => (bool)isRenameFocused.Invoke(hosts[paneIndex], null)!, TimeSpan.FromSeconds(2)))
            throw new Exception($"Pane {paneIndex} did not enter rename mode in {name} view.");

        var editor = GetFocus();
        SendMessage(editor, 0x00B1, (nint)4, (nint)4);
        foreach (var (key, expectedCaret) in new[] { (0x25, (3, 3)), (0x27, (5, 5)) })
        {
            var message = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)key, lParam = (nint)1 };
            if (!Equals(translate.Invoke(hosts[paneIndex], [message, ModifierKeys.None]), true) ||
                GetFocus() != editor || ReadEditSelection(editor) != expectedCaret)
                throw new Exception($"Pane {paneIndex} {name} rename arrow {key:X} moved focus or failed to edit the caret.");
            SendMessage(editor, 0x00B1, (nint)4, (nint)4);
        }
        foreach (var key in new[] { 0x26, 0x28 })
        {
            var message = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)key, lParam = (nint)1 };
            if (!Equals(translate.Invoke(hosts[paneIndex], [message, ModifierKeys.None]), true) ||
                GetFocus() != editor || !(bool)isRenameFocused.Invoke(hosts[paneIndex], null)!)
                throw new Exception($"Pane {paneIndex} {name} rename arrow {key:X} escaped the editor.");
        }

        var keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not capture keyboard state for the rename view-mode probe.");
        try
        {
            var shifted = keyboardState.ToArray();
            shifted[0x10] |= 0x80;
            shifted[0xA0] |= 0x80;
            if (!SetKeyboardState(shifted))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not set Shift state for the rename view-mode probe.");
            foreach (var (key, expectedSelection) in new[] { (0x25, (3, 4)), (0x27, (4, 5)) })
            {
                SendMessage(editor, 0x00B1, (nint)4, (nint)4);
                var message = new MSG { hwnd = editor, message = 0x0100, wParam = (nint)key, lParam = (nint)1 };
                if (!Equals(translate.Invoke(hosts[paneIndex], [message, ModifierKeys.Shift]), true) ||
                    GetFocus() != editor || ReadEditSelection(editor) != expectedSelection)
                    throw new Exception($"Pane {paneIndex} {name} rename Shift+arrow {key:X} failed to select editor text.");
            }
        }
        finally
        {
            if (!SetKeyboardState(keyboardState))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not restore keyboard state after the rename view-mode probe.");
        }

        foreach (var otherPane in Enumerable.Range(0, hosts.Count).Where(index => index != paneIndex))
            if ((bool)isRenameFocused.Invoke(hosts[otherPane], null)!)
                throw new Exception($"Pane {paneIndex} {name} rename editor focus was attributed to pane {otherPane}.");
        var selectedAfterRenameKeys = SelectedPaths(hostType, hosts[paneIndex], folders[paneIndex])
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!selectedBeforeRename.SequenceEqual(selectedAfterRenameKeys, StringComparer.OrdinalIgnoreCase))
            throw new Exception($"Pane {paneIndex} {name} rename arrows changed the selected file set.");

        Console.WriteLine($"rename-editor-view-mode pane={paneIndex} name={name} mode={expectedMode} caret-left-right=True up-down-contained=True shift-selection=True focus-retained=True selection-preserved=True");
    }

    private static void SelectNone(Type hostType, HwndHost host)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        if (view == 0) throw new Exception("Folder view was unavailable while clearing the fixture selection.");
        try
        {
            var assembly = hostType.Assembly;
            var commandType = assembly.GetType("ExplorerDirs.App.ShellSelectionCommand", true)!;
            var command = Enum.Parse(commandType, "None");
            var apply = assembly.GetType("ExplorerDirs.App.ShellSelectionCommands", true)!
                .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
            var selected = apply.Invoke(null, [view, command, (Func<bool>)(() => true), (Func<bool>)(() => true)]);
            if (!Equals(selected, true)) throw new Exception("Could not clear the fixture selection.");
        }
        finally { Marshal.Release(view); }
    }

    private static void InvokeShellSelectionChanged(Type hostType, HwndHost host)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, viewObject)!);
        var shellView = viewObject[1] ?? throw new Exception("Current Shell view was unavailable for the selection callback probe.");
        var shellViewPointer = Marshal.GetIUnknownForObject(shellView);
        try
        {
            hostType.GetMethod("OnShellSelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host, [shellViewPointer]);
        }
        finally
        {
            Marshal.Release(shellViewPointer);
            Marshal.ReleaseComObject(shellView);
        }
    }

    private static void SelectOnlyItem(Type hostType, HwndHost host, string folder, string name, bool takeFocus = false)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, viewObject)!);
        var shellView = viewObject[1]!;
        var selectItem = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!.GetMethod("SelectItem")!;
        Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder, name), IntPtr.Zero, out var absolutePidl, 0, out _));
        try
        {
            var relativePidl = ILFindLastID(absolutePidl);
            // Use the native view selection API so this probe varies item count without UI input.
            // SVSI_FOCUSED must be explicit; selecting an item alone can leave the previous row focused.
            Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [relativePidl, 0x1u | 0x4u | (takeFocus ? 0x10u : 0x40000000u)])!);
        }
        finally
        {
            Marshal.FreeCoTaskMem(absolutePidl);
            Marshal.ReleaseComObject(shellView);
        }
        var selected = SelectedPaths(hostType, host, folder);
        if (!selected.SequenceEqual([Path.Combine(folder, name)], StringComparer.OrdinalIgnoreCase))
            throw new Exception($"Could not establish single-item selection: {string.Join('|', selected)}.");
    }

    private static int RunChangedMetadataProbe(Type hostType, HwndHost host, string folder, string root, HwndSource source,
        int changedBurstCount)
    {
        var foreground = GetForegroundWindow();
        if (!WaitForExpectedCounts([host], [changedBurstCount], TimeSpan.FromSeconds(10)) ||
            !WaitForAttachedViews(hostType, [host], TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The changed-metadata fixture view did not become ready.");

        var styleType = hostType.Assembly.GetType("ExplorerDirs.App.ViewStyle", throwOnError: true)!;
        var detailsStyle = ((System.Collections.IEnumerable)styleType.GetProperty("All")!.GetValue(null)!)
            .Cast<object>().Single(style => (uint)styleType.GetProperty("Mode")!.GetValue(style)! == 4);
        if (!(bool)hostType.GetMethod("SetViewStyle")!.Invoke(host, [detailsStyle])!)
            throw new InvalidOperationException("Could not set the metadata probe view to Details.");

        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The metadata probe's native view was unavailable.");
        try
        {
            var assembly = hostType.Assembly;
            var keyType = assembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var columnType = assembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var keyConstructor = keyType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, [typeof(Guid), typeof(uint)], modifiers: null)!;
            var columnConstructor = columnType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, [keyType, typeof(int)], modifiers: null)!;
            var propertyFormat = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var columns = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(columnType))!;
            foreach (var (propertyId, width) in new[] { (10u, 240), (14u, 180), (4u, 160), (12u, 120) })
            {
                var key = keyConstructor.Invoke([propertyFormat, propertyId]);
                columns.Add(columnConstructor.Invoke([key, width]));
            }
            var columnManagerType = assembly.GetType("ExplorerDirs.App.ShellColumnManager", throwOnError: true)!;
            // Reflection does not supply optional parameter defaults; pass the production guard explicitly.
            var applyResult = (int)columnManagerType.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, [view, columns, null])!;
            Marshal.ThrowExceptionForHR(applyResult);
        }
        finally { Marshal.Release(view); }

        if (changedBurstCount > 2)
            return RunChangedMetadataBurstProbe(hostType, host, folder, root, source, changedBurstCount);

        var beforeInitial = CaptureMetadataRow(hostType, host, "initial.txt");
        var beforeSecond = CaptureMetadataRow(hostType, host, "second.txt");
        var sizeColumn = Array.FindIndex(beforeInitial.Columns, column => column.Contains("大小", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("Size", StringComparison.OrdinalIgnoreCase));
        if (sizeColumn < 0)
            throw new InvalidOperationException($"The Details view did not expose a size column: {string.Join('|', beforeInitial.Columns)}.");
        var sizeSortProbe = !string.Equals(Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_CHANGED_SORT"),
            "name", StringComparison.OrdinalIgnoreCase);
        var modifiedGroupingProbe = string.Equals(Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_CHANGED_GROUP"),
            "modified", StringComparison.OrdinalIgnoreCase);
        var viewPointer = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (viewPointer == 0) throw new InvalidOperationException("The metadata probe's native view was unavailable for sorting.");
        string[] beforeOrder;
        try
        {
            if (sizeSortProbe) SetSizeAscendingSort(viewPointer);
            else SetNameAscendingSort(viewPointer);
            if (modifiedGroupingProbe) SetGroup(viewPointer, 14);
            var expectedBeforeOrder = sizeSortProbe ? new[] { "second.txt", "initial.txt" } : new[] { "initial.txt", "second.txt" };
            beforeOrder = WaitForPredicate(() => CaptureMetadataRowNames(hostType, host)
                .SequenceEqual(expectedBeforeOrder, StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(5))
                ? CaptureMetadataRowNames(hostType, host)
                : throw new TimeoutException("The configured Shell sort did not produce its expected fixture order.");
        }
        finally { Marshal.Release(viewPointer); }

        SelectOnlyItem(hostType, host, folder, "initial.txt", takeFocus: true);
        var selectedBefore = SelectedPaths(hostType, host, folder);
        var focusMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var focusedBefore = (string?)focusMethod.Invoke(host, [folder]);
        if (!string.Equals(focusedBefore, Path.Combine(folder, "initial.txt"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The metadata probe did not focus its selected file: {focusedBefore ?? "none"}.");

        var refreshProperty = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fallbacksBefore = (int)refreshProperty.GetValue(host)!;
        using var notificationSource = new HwndSource(new HwndSourceParameters("Changed metadata Shell observer")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0
        });
        using var observer = new ChangeNotificationObserver(notificationSource, root, Stopwatch.StartNew(), resolveTargetPaths: true);
        var notificationClock = Stopwatch.StartNew();
        var initialPath = Path.Combine(folder, "initial.txt");
        var secondPath = Path.Combine(folder, "second.txt");
        File.WriteAllText(initialPath, new string('x', 64 * 1024));
        File.SetLastWriteTime(initialPath, DateTime.Now.AddMinutes(1));
        File.WriteAllText(secondPath, new string('y', 128 * 1024));
        File.SetLastWriteTime(secondPath, DateTime.Now.AddMinutes(2));

        var updateNotificationSeen = WaitForPredicate(() =>
            (observer.Summary.Contains("2000/", StringComparison.OrdinalIgnoreCase) ||
             observer.Summary.Contains("1000/", StringComparison.OrdinalIgnoreCase)) &&
            (observer.Summary.Contains("initial.txt", StringComparison.OrdinalIgnoreCase) ||
             observer.Summary.Contains("second.txt", StringComparison.OrdinalIgnoreCase) ||
             observer.Summary.Contains("pane-0", StringComparison.OrdinalIgnoreCase)), TimeSpan.FromSeconds(5));
        var productionDirectoryUpdateSeen = WaitForPredicate(() =>
            observer.Summary.Contains("1000:", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
        var notificationLatencyMs = notificationClock.Elapsed.TotalMilliseconds;
        var flushClock = Stopwatch.StartNew();
        var explicitFlushUsed = !string.Equals(Environment.GetEnvironmentVariable("EXPLORER_DIRS_HOSTPROBE_SKIP_FLUSH"),
            "1", StringComparison.Ordinal);
        var flushTask = explicitFlushUsed
            ? Task.Run(() =>
            {
                NotifyChangedItemFlushed(initialPath);
                NotifyChangedItemFlushed(secondPath);
            })
            : Task.CompletedTask;
        var flushCompleted = WaitForPredicate(() => flushTask.IsCompleted, TimeSpan.FromSeconds(10));
        if (flushCompleted) flushTask.GetAwaiter().GetResult();
        var flushBarrierLatencyMs = flushClock.Elapsed.TotalMilliseconds;
        var orderAfterFlush = CaptureMetadataRowNames(hostType, host);
        var initialAfterFlush = CaptureMetadataRow(hostType, host, "initial.txt");
        var secondAfterFlush = CaptureMetadataRow(hostType, host, "second.txt");
        var metadataUpdatedAfterFlush = initialAfterFlush.Row[sizeColumn] != beforeInitial.Row[sizeColumn] &&
            secondAfterFlush.Row[sizeColumn] != beforeSecond.Row[sizeColumn];
        (string[] Columns, string[] Row)? afterInitial = null;
        (string[] Columns, string[] Row)? afterSecond = null;
        string[]? afterOrder = null;
        var sizeUpdated = WaitForPredicate(() =>
        {
            try
            {
                afterInitial = CaptureMetadataRow(hostType, host, "initial.txt");
                afterSecond = CaptureMetadataRow(hostType, host, "second.txt");
                afterOrder = CaptureMetadataRowNames(hostType, host);
                return afterInitial.Value.Row[sizeColumn] != beforeInitial.Row[sizeColumn] &&
                    afterSecond.Value.Row[sizeColumn] != beforeSecond.Row[sizeColumn];
            }
            catch { return false; }
        }, TimeSpan.FromSeconds(5));
        var directoryFallbackUsed = false;
        var directoryFallbackUpdated = false;
        if (!sizeUpdated)
        {
            directoryFallbackUsed = true;
            var directoryFlush = Task.Run(() => NotifyPathEventFlushed(0x00001000, folder));
            var directoryFlushCompleted = WaitForPredicate(() => directoryFlush.IsCompleted, TimeSpan.FromSeconds(10));
            if (directoryFlushCompleted) directoryFlush.GetAwaiter().GetResult();
            if (!directoryFlushCompleted) throw new TimeoutException("The flushed directory-change notification did not return.");
            directoryFallbackUpdated = WaitForPredicate(() =>
            {
                try
                {
                    afterInitial = CaptureMetadataRow(hostType, host, "initial.txt");
                    afterSecond = CaptureMetadataRow(hostType, host, "second.txt");
                    afterOrder = CaptureMetadataRowNames(hostType, host);
                    return afterInitial.Value.Row[sizeColumn] != beforeInitial.Row[sizeColumn] &&
                        afterSecond.Value.Row[sizeColumn] != beforeSecond.Row[sizeColumn];
                }
                catch { return false; }
            }, TimeSpan.FromSeconds(2));
            sizeUpdated = directoryFallbackUpdated;
        }
        var refreshFallbackUsed = false;
        if (!sizeUpdated)
        {
            refreshFallbackUsed = true;
            hostType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public)!.Invoke(host, null);
            sizeUpdated = WaitForPredicate(() =>
            {
                try
                {
                    afterInitial = CaptureMetadataRow(hostType, host, "initial.txt");
                    afterSecond = CaptureMetadataRow(hostType, host, "second.txt");
                    afterOrder = CaptureMetadataRowNames(hostType, host);
                    return afterInitial.Value.Row[sizeColumn] != beforeInitial.Row[sizeColumn] &&
                        afterSecond.Value.Row[sizeColumn] != beforeSecond.Row[sizeColumn];
                }
                catch { return false; }
            }, TimeSpan.FromSeconds(5));
        }
        var naturalOrderUpdated = afterOrder?.SequenceEqual(["initial.txt", "second.txt"], StringComparer.OrdinalIgnoreCase) == true;
        var viewUpdateLatencyMs = notificationClock.Elapsed.TotalMilliseconds;
        var directoryOrderUpdated = naturalOrderUpdated;
        if (sizeUpdated && !naturalOrderUpdated && !directoryFallbackUsed)
        {
            directoryFallbackUsed = true;
            var directoryFlush = Task.Run(() => NotifyPathEventFlushed(0x00001000, folder));
            var directoryFlushCompleted = WaitForPredicate(() => directoryFlush.IsCompleted, TimeSpan.FromSeconds(10));
            if (directoryFlushCompleted) directoryFlush.GetAwaiter().GetResult();
            if (!directoryFlushCompleted) throw new TimeoutException("The flushed directory-change notification did not return.");
            directoryOrderUpdated = WaitForPredicate(() => CaptureMetadataRowNames(hostType, host)
                .SequenceEqual(["initial.txt", "second.txt"], StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(2));
            if (directoryOrderUpdated) afterOrder = CaptureMetadataRowNames(hostType, host);
        }
        var resortReapplied = false;
        var orderUpdatedAfterReapply = directoryOrderUpdated;
        if (sizeUpdated && !directoryOrderUpdated)
        {
            var resortView = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host, null)!;
            if (resortView == 0) throw new InvalidOperationException("The metadata probe's native view was unavailable for resorting.");
            try
            {
                SetNameAscendingSort(resortView);
                var nameOrderObserved = WaitForPredicate(() => CaptureMetadataRowNames(hostType, host)
                    .SequenceEqual(["initial.txt", "second.txt"], StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
                if (!nameOrderObserved) throw new TimeoutException("The Shell view did not apply the control name sort.");
                SetSizeAscendingSort(resortView);
                resortReapplied = true;
                orderUpdatedAfterReapply = WaitForPredicate(() => CaptureMetadataRowNames(hostType, host)
                    .SequenceEqual(["initial.txt", "second.txt"], StringComparer.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
                afterOrder = CaptureMetadataRowNames(hostType, host);
            }
            finally { Marshal.Release(resortView); }
        }
        var resortLatencyMs = notificationClock.Elapsed.TotalMilliseconds;
        var selectedAfter = SelectedPaths(hostType, host, folder);
        var focusedAfter = (string?)focusMethod.Invoke(host, [folder]);
        var selectionPreserved = selectedBefore.SequenceEqual(selectedAfter, StringComparer.OrdinalIgnoreCase);
        var focusPreserved = string.Equals(focusedBefore, focusedAfter, StringComparison.OrdinalIgnoreCase);
        var fallbackDelta = (int)refreshProperty.GetValue(host)! - fallbacksBefore;
        var hidden = !IsWindowVisible(source.Handle) && !IsWindowVisible(notificationSource.Handle);
        var foregroundUnchanged = GetForegroundWindow() == foreground;
        Console.WriteLine($"changed-metadata notification={updateNotificationSeen}; modified-grouping={modifiedGroupingProbe}; production-directory-update={productionDirectoryUpdateSeen}; before-sizes={beforeInitial.Row[sizeColumn]}|{beforeSecond.Row[sizeColumn]}; sizes-after-flush={initialAfterFlush.Row[sizeColumn]}|{secondAfterFlush.Row[sizeColumn]}; metadata-updated-after-flush={metadataUpdatedAfterFlush}; explicit-flush-used={explicitFlushUsed}; after-sizes={afterInitial?.Row[sizeColumn] ?? "unavailable"}|{afterSecond?.Row[sizeColumn] ?? "unavailable"}; before-order={string.Join('|', beforeOrder)}; order-after-notification={naturalOrderUpdated}; order-after-flush={string.Join('|', orderAfterFlush)}; flush-completed={flushCompleted}; flush-barrier-latency-ms={flushBarrierLatencyMs:F1}; directory-fallback-used={directoryFallbackUsed}; directory-fallback-updated={directoryFallbackUpdated}; directory-fallback-resorted={directoryOrderUpdated && !naturalOrderUpdated}; refresh-fallback-used={refreshFallbackUsed}; order-after-reapply={orderUpdatedAfterReapply}; resort-reapplied={resortReapplied}; notification-latency-ms={notificationLatencyMs:F1}; both-view-rows-updated-latency-ms={viewUpdateLatencyMs:F1}; order-update-latency-ms={resortLatencyMs:F1}; selection-preserved={selectionPreserved}; focus-preserved={focusPreserved}; fallback-delta={fallbackDelta}; hidden-parent={hidden}; foreground-unchanged={foregroundUnchanged}; shell-events={observer.Summary}");
        var browserSite = hostType.GetField("_browserSite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Console.WriteLine($"shell-browser-callbacks={browserSite.GetType().GetProperty("NotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browserSite)}");
        return updateNotificationSeen && flushCompleted && sizeUpdated && orderUpdatedAfterReapply &&
            (!modifiedGroupingProbe || productionDirectoryUpdateSeen) && selectionPreserved && focusPreserved &&
            fallbackDelta == 0 && hidden && foregroundUnchanged ? 0 : 1;
    }

    private static string[] CaptureMetadataRowNames(Type hostType, HwndHost host)
    {
        var snapshot = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null) ?? throw new InvalidOperationException("The current Shell file-view snapshot was empty.");
        return ((System.Collections.IEnumerable)snapshot.GetType().GetProperty("Rows")!.GetValue(snapshot)!)
            .Cast<System.Collections.IEnumerable>()
            .Select(row => row.Cast<object?>().Select(cell => cell?.ToString() ?? string.Empty).FirstOrDefault() ?? string.Empty)
            .ToArray();
    }

    private static int RunChangedMetadataBurstProbe(Type hostType, HwndHost host, string folder, string root,
        HwndSource source, int changedBurstCount)
    {
        var fileNames = new[] { "initial.txt", "second.txt" }
            .Concat(Enumerable.Range(2, changedBurstCount - 2).Select(index => $"changed-{index:D3}.txt"))
            .ToArray();
        var beforeRows = fileNames.ToDictionary(name => name, name => CaptureMetadataRow(hostType, host, name),
            StringComparer.OrdinalIgnoreCase);
        var sizeColumn = Array.FindIndex(beforeRows["initial.txt"].Columns, column =>
            column.Contains("大小", StringComparison.OrdinalIgnoreCase) || column.Contains("Size", StringComparison.OrdinalIgnoreCase));
        if (sizeColumn < 0) throw new InvalidOperationException("The Changed burst Details view has no size column.");

        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The Changed burst probe's native view was unavailable.");
        try
        {
            SetSizeAscendingSort(view);
            SetGroup(view, 14);
        }
        finally { Marshal.Release(view); }

        string[]? lastSortedRows = null;
        var stableSortedSamples = 0;
        if (!WaitForPredicate(() =>
            {
                try
                {
                    var rows = CaptureMetadataRowNames(hostType, host);
                    if (rows.Length != fileNames.Length)
                    {
                        lastSortedRows = null;
                        stableSortedSamples = 0;
                        return false;
                    }
                    stableSortedSamples = lastSortedRows is not null &&
                        lastSortedRows.SequenceEqual(rows, StringComparer.OrdinalIgnoreCase)
                        ? stableSortedSamples + 1 : 1;
                    lastSortedRows = rows;
                    return stableSortedSamples >= 3;
                }
                catch { return false; }
            }, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The grouped metadata view did not settle before selecting its fixture item.");

        // Applying Shell grouping rebuilds the native view asynchronously; do not mistake its temporary empty selection for a failed item selection.
        SelectOnlyItem(hostType, host, folder, "initial.txt", takeFocus: true);
        var selectedBefore = SelectedPaths(hostType, host, folder);
        var focusMethod = hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var focusedBefore = (string?)focusMethod.Invoke(host, [folder]);
        if (!string.Equals(focusedBefore, Path.Combine(folder, "initial.txt"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The Changed burst probe did not focus its selected file: {focusedBefore ?? "none"}.");

        var refreshProperty = hostType.GetProperty("DirectoryChangeFallbackRefreshCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fallbacksBefore = (int)refreshProperty.GetValue(host)!;
        var foregroundBeforeMutation = GetForegroundWindow();
        using var notificationSource = new HwndSource(new HwndSourceParameters("Changed burst Shell observer")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0
        });
        using var observer = new ChangeNotificationObserver(notificationSource, root, Stopwatch.StartNew(), resolveTargetPaths: true);
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < fileNames.Length; index++)
        {
            var path = Path.Combine(folder, fileNames[index]);
            File.WriteAllText(path, new string('x', (64 + index * 4) * 1024));
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(index + 1));
        }

        var exceedsItemBatch = changedBurstCount > 128;
        var notificationsObserved = !exceedsItemBatch && WaitForPredicate(() =>
            observer.Summary.Contains("1000:", StringComparison.OrdinalIgnoreCase) ||
            observer.Summary.Contains("2000:", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(8));
        var directoryUpdateObserved = !exceedsItemBatch && WaitForPredicate(() =>
            observer.Summary.Contains("1000:", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(8));
        var expectedFallbackDelta = exceedsItemBatch ? 1 : 0;
        var fallbackObserved = !exceedsItemBatch || WaitForPredicate(() =>
            (int)refreshProperty.GetValue(host)! - fallbacksBefore == expectedFallbackDelta, TimeSpan.FromSeconds(10));
        var rowsUpdated = WaitForPredicate(() =>
        {
            try
            {
                foreach (var name in fileNames)
                    if (CaptureMetadataRow(hostType, host, name).Row[sizeColumn] == beforeRows[name].Row[sizeColumn]) return false;
                return true;
            }
            catch { return false; }
        }, TimeSpan.FromSeconds(8));
        var expectedOrder = fileNames;
        var finalOrder = CaptureMetadataRowNames(hostType, host);
        var orderUpdated = finalOrder.SequenceEqual(expectedOrder, StringComparer.OrdinalIgnoreCase);
        var elapsedMs = clock.Elapsed.TotalMilliseconds;
        var selectedAfter = SelectedPaths(hostType, host, folder);
        var focusedAfter = (string?)focusMethod.Invoke(host, [folder]);
        var selectionPreserved = selectedBefore.SequenceEqual(selectedAfter, StringComparer.OrdinalIgnoreCase);
        var focusPreserved = string.Equals(focusedBefore, focusedAfter, StringComparison.OrdinalIgnoreCase);
        var fallbackDelta = (int)refreshProperty.GetValue(host)! - fallbacksBefore;
        var hidden = !IsWindowVisible(source.Handle) && !IsWindowVisible(notificationSource.Handle);
        var foregroundUnchanged = GetForegroundWindow() == foregroundBeforeMutation;
        Console.WriteLine($"changed-metadata-burst count={changedBurstCount}; exceeds-item-batch={exceedsItemBatch}; notifications-observed={notificationsObserved}; directory-update-observed={directoryUpdateObserved}; fallback-observed={fallbackObserved}; rows-updated={rowsUpdated}; final-order={string.Join('|', finalOrder)}; order-updated={orderUpdated}; elapsed-ms={elapsedMs:F1}; selection-preserved={selectionPreserved}; focus-preserved={focusPreserved}; fallback-delta={fallbackDelta}; expected-fallback-delta={expectedFallbackDelta}; hidden-parent={hidden}; foreground-unchanged={foregroundUnchanged}; shell-events={observer.Summary}");
        var stageTimings = hostType.GetProperty("DirectoryChangeStageTimingSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host);
        Console.WriteLine($"directory-change-stage-timings={stageTimings}");
        return (exceedsItemBatch || notificationsObserved && directoryUpdateObserved) && fallbackObserved && rowsUpdated && orderUpdated &&
            selectionPreserved && focusPreserved &&
            fallbackDelta == expectedFallbackDelta && hidden && foregroundUnchanged ? 0 : 1;
    }

    private static void SetSizeAscendingSort(nint view)
        => SetSort(view, 12);

    private static void SetNameAscendingSort(nint view)
        => SetSort(view, 10);

    private static void SetGroup(nint view, uint propertyId)
    {
        var group = new NativePropertyKey
        {
            FormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"),
            PropertyId = propertyId
        };
        var method = Marshal.GetDelegateForFunctionPointer<SetGroupByDelegate>(Marshal.ReadIntPtr(
            Marshal.ReadIntPtr(view), 17 * nint.Size));
        Marshal.ThrowExceptionForHR(method(view, ref group, 1));
    }

    private static void SetSort(nint view, uint propertyId)
    {
        var sort = new NativeSortColumn
        {
            Key = new NativePropertyKey { FormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = propertyId },
            Direction = 1
        };
        var method = Marshal.GetDelegateForFunctionPointer<SetSortColumnsDelegate>(Marshal.ReadIntPtr(
            Marshal.ReadIntPtr(view), 27 * nint.Size));
        Marshal.ThrowExceptionForHR(method(view, [sort], 1));
    }

    private static void NotifyChangedItemFlushed(string path)
        => NotifyPathEventFlushed(0x00002000, path);

    private static void NotifyPathEventFlushed(uint eventId, string path)
    {
        var pathMemory = Marshal.StringToCoTaskMemUni(path);
        try { SHChangeNotify(unchecked((int)eventId), 0x00001005, pathMemory, IntPtr.Zero); }
        finally { Marshal.FreeCoTaskMem(pathMemory); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePropertyKey { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSortColumn { public NativePropertyKey Key; public int Direction; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetSortColumnsDelegate(nint self, [In] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetGroupByDelegate(nint self, ref NativePropertyKey key, int ascending);

    private static (string[] Columns, string[] Row) CaptureMetadataRow(Type hostType, HwndHost host, string fileName)
    {
        var snapshot = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null) ?? throw new InvalidOperationException("The current Shell file-view snapshot was empty.");
        var columns = ((System.Collections.IEnumerable)snapshot.GetType().GetProperty("Columns")!.GetValue(snapshot)!)
            .Cast<object>().Select(column => column.ToString() ?? string.Empty).ToArray();
        var rows = ((System.Collections.IEnumerable)snapshot.GetType().GetProperty("Rows")!.GetValue(snapshot)!)
            .Cast<System.Collections.IEnumerable>().Select(row => row.Cast<object?>().Select(cell => cell?.ToString() ?? string.Empty).ToArray())
            .ToArray();
        var match = rows.SingleOrDefault(row => row.Length > 0 && string.Equals(row[0], fileName, StringComparison.OrdinalIgnoreCase));
        if (match is null) throw new InvalidOperationException($"The current Shell snapshot did not contain {fileName}: {string.Join('|', rows.Select(row => row.FirstOrDefault()))}.");
        return (columns, match);
    }

    private static (int Start, int End) ReadEditSelection(nint editor)
    {
        var start = Marshal.AllocHGlobal(sizeof(int));
        var end = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            SendMessage(editor, 0x00B0, start, end); // EM_GETSEL
            return (Marshal.ReadInt32(start), Marshal.ReadInt32(end));
        }
        finally
        {
            Marshal.FreeHGlobal(start);
            Marshal.FreeHGlobal(end);
        }
    }

    private static void DeselectItem(Type hostType, HwndHost host, string folder, string name)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, viewObject)!);
        var shellView = viewObject[1]!;
        var selectItem = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!.GetMethod("SelectItem")!;
        Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder, name), IntPtr.Zero, out var absolutePidl, 0, out _));
        try
        {
            var relativePidl = ILFindLastID(absolutePidl);
            Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [relativePidl, 0x20u | 0x40000000u])!);
        }
        finally
        {
            Marshal.FreeCoTaskMem(absolutePidl);
            Marshal.ReleaseComObject(shellView);
        }
    }

    private static void SetUnselectedFocus(Type hostType, HwndHost host, string folder, string name)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var getCurrentView = browserInterface.GetMethod("GetCurrentView")!;
        var viewObject = new object?[] { shellViewId, null };
        var result = getCurrentView.Invoke(browser, viewObject);
        Marshal.ThrowExceptionForHR((int)result!);
        var shellView = viewObject[1]!;
        var shellViewInterface = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!;
        var selectItem = shellViewInterface.GetMethod("SelectItem")!;
        var absolutePath = Path.Combine(folder, name);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(absolutePath, IntPtr.Zero, out var absolutePidl, 0, out _));
        try
        {
            var relativePidl = ILFindLastID(absolutePidl);
            // A focused-only selection clears that item's selection bit while preserving other selected rows.
            Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [relativePidl, 0x20u | 0x40000000u])!);
            Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [relativePidl, 0x10u | 0x40000000u])!);
        }
        finally
        {
            Marshal.FreeCoTaskMem(absolutePidl);
            Marshal.ReleaseComObject(shellView);
        }
    }

    private static void SetSelectedFocus(Type hostType, HwndHost host, string folder, string name)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, viewObject)!);
        var shellView = viewObject[1]!;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder, name), IntPtr.Zero, out var absolutePidl, 0, out _));
            try
            {
                var relativePidl = ILFindLastID(absolutePidl);
                var selectItem = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!.GetMethod("SelectItem")!;
                Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [relativePidl, 0x1u | 0x10u | 0x40000000u])!);
            }
            finally { Marshal.FreeCoTaskMem(absolutePidl); }
        }
        finally { Marshal.ReleaseComObject(shellView); }
    }

    private static string[] SelectedPaths(Type hostType, HwndHost host, string folder)
        => ViewItemPaths(hostType, host, folder, scope: 1);

    internal static string[] ViewItemPaths(Type hostType, HwndHost host, string folder, uint scope)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        if (view == 0) throw new Exception("Folder view was unavailable while reading selection.");
        nint parentPidl = 0, enumeration = 0;
        var items = new List<string>();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(folder, IntPtr.Zero, out parentPidl, 0, out _));
            var enumId = new Guid("000214F2-0000-0000-C000-000000000046");
            var result = At<GetItems>(view, 8)(view, scope, ref enumId, out enumeration);
            if (result == unchecked((int)0x80070490)) return [];
            Marshal.ThrowExceptionForHR(result);
            var next = At<EnumNext>(enumeration, 3);
            for (;;)
            {
                var hr = next(enumeration, 1, out var childPidl, out var fetched);
                try
                {
                    if (hr == 1 && fetched == 0 && childPidl == 0) break;
                    Marshal.ThrowExceptionForHR(hr);
                    if (childPidl == 0 || fetched != 1) throw new Exception("Shell returned an invalid selection item.");
                    var absolute = ILCombine(parentPidl, childPidl);
                    if (absolute == 0) throw new Exception("Could not combine the selected item identity.");
                    try
                    {
                        Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolute, 0x80058000, out var name));
                        try { items.Add(Marshal.PtrToStringUni(name) ?? throw new Exception("Selected item had no path.")); }
                        finally { Marshal.FreeCoTaskMem(name); }
                    }
                    finally { Marshal.FreeCoTaskMem(absolute); }
                }
                finally { if (childPidl != 0) Marshal.FreeCoTaskMem(childPidl); }
            }
        }
        finally
        {
            if (enumeration != 0) Marshal.Release(enumeration);
            if (parentPidl != 0) Marshal.FreeCoTaskMem(parentPidl);
            Marshal.Release(view);
        }
        return items.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ShellViewItemKind(Type hostType, HwndHost host, string path)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        if (view == 0) return "view-unavailable";
        nint array = 0;
        try
        {
            var itemArrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            var result = At<GetItems>(view, 8)(view, 2, ref itemArrayId, out array); // SVGIO_ALLVIEW
            if (result < 0 || array == 0) return $"items-unavailable:{result:X8}";
            if (At<ArrayCount>(array, 7)(array, out var count) < 0) return "count-unavailable";
            for (uint index = 0; index < count; index++)
            {
                if (At<ItemAt>(array, 8)(array, index, out var item) < 0 || item == 0) continue;
                try
                {
                    if (At<DisplayName>(item, 5)(item, 0x80058000, out var name) < 0 || name == 0) continue;
                    string itemPath;
                    try { itemPath = Marshal.PtrToStringUni(name) ?? ""; }
                    finally { Marshal.FreeCoTaskMem(name); }
                    if (!string.Equals(Path.GetFullPath(itemPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) continue;
                    var attributeResult = At<ShellItemAttributes>(item, 6)(item, 0x20000000, out var attributes); // SFGAO_FOLDER
                    if (attributeResult < 0) return $"attributes-unavailable:{attributeResult:X8}";
                    return (attributes & 0x20000000) != 0 ? "folder" : "file";
                }
                finally { Marshal.Release(item); }
            }
            return "not-found";
        }
        finally
        {
            if (array != 0) Marshal.Release(array);
            Marshal.Release(view);
        }
    }

    private static bool WaitForFolderItems(Type hostType, HwndHost host, string folder, string[] expected, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var deadline = Stopwatch.StartNew();
        var matched = false;
        timer.Tick += (_, _) =>
        {
            matched = ViewItemPaths(hostType, host, folder, scope: 2).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
            if (matched || deadline.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return matched;
    }

    private static string DescribeWindow(nint hwnd)
    {
        if (hwnd == 0) return "none";
        var name = new System.Text.StringBuilder(128);
        GetClassName(hwnd, name, name.Capacity);
        var style = GetWindowLongPtr(hwnd, -16).ToInt64();
        var exStyle = GetWindowLongPtr(hwnd, -20).ToInt64();
        return $"{hwnd:X}:{name}:style={style:X8}:ex={exStyle:X8}:visible-bit={(style & 0x10000000) != 0}:is-visible={IsWindowVisible(hwnd)}:parent={GetParent(hwnd):X}";
    }

    private static nint ParseHandle(string description)
    {
        var separator = description.IndexOf(':');
        return separator > 0 && long.TryParse(description.AsSpan(0, separator), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? (nint)value : 0;
    }

    private static string WindowBounds(nint hwnd)
    {
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return "none";
        return $"{rect.Left},{rect.Top},{rect.Right - rect.Left},{rect.Bottom - rect.Top}";
    }

    private static string[] ChildWindowDescriptions(nint parent)
    {
        if (parent == 0) return [];
        var windows = new List<string>();
        EnumChildWindows(parent, (hwnd, _) => { windows.Add(DescribeWindow(hwnd)); return true; }, IntPtr.Zero);
        return windows.ToArray();
    }

    private static string[] ThreadWindowDescriptions(uint threadId)
    {
        var windows = new List<string>();
        EnumThreadWindows(threadId, (hwnd, _) => { windows.Add(DescribeWindow(hwnd)); return true; }, IntPtr.Zero);
        nint previous = 0;
        while (true)
        {
            var hwnd = FindWindowEx(new nint(-3), previous, null, null); // HWND_MESSAGE
            if (hwnd == 0) break;
            previous = hwnd;
            if (GetWindowThreadProcessId(hwnd, out _) == threadId)
                windows.Add("message-only:" + DescribeWindow(hwnd));
        }
        return windows.ToArray();
    }

    private sealed class MessageOnlyWindowObserver : IDisposable
    {
        private const nuint SubclassId = 0xED01;
        private readonly SubclassProc _callback;
        private readonly Dictionary<nint, WindowTrace> _windows = [];
        private Dictionary<nint, int> _baselineCounts = [];

        internal MessageOnlyWindowObserver(uint threadId)
        {
            _callback = Observe;
            nint previous = 0;
            while (true)
            {
                var hwnd = FindWindowEx(new nint(-3), previous, null, null); // HWND_MESSAGE
                if (hwnd == 0) break;
                previous = hwnd;
                if (GetWindowThreadProcessId(hwnd, out _) != threadId) continue;
                var className = new System.Text.StringBuilder(128);
                GetClassName(hwnd, className, className.Capacity);
                if (SetWindowSubclass(hwnd, _callback, SubclassId, 0))
                    _windows.Add(hwnd, new WindowTrace(className.ToString()));
            }
        }

        internal string WindowList => _windows.Count == 0 ? "none" : string.Join(',', _windows.Select(pair => $"{pair.Key:X}/{pair.Value.ClassName}"));
        internal string Delta => string.Join('|', _windows.Select(pair =>
        {
            var count = pair.Value.Count - _baselineCounts.GetValueOrDefault(pair.Key);
            return count == 0 ? null : $"{pair.Key:X}/{pair.Value.ClassName}:{count}";
        }).Where(value => value is not null).DefaultIfEmpty("none"));
        internal string SampleDelta => string.Join('|', _windows.Select(pair =>
        {
            return pair.Value.Samples.Count == 0 ? null : $"{pair.Key:X}/{pair.Value.ClassName}=" + string.Join(',', pair.Value.Samples);
        }).Where(value => value is not null).DefaultIfEmpty("none"));

        internal void CaptureBaseline()
        {
            _baselineCounts = _windows.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
            foreach (var trace in _windows.Values) trace.Samples.Clear();
        }

        private nint Observe(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nint data)
        {
            if (message >= 0x0400 && _windows.TryGetValue(hwnd, out var trace))
            {
                trace.Count++;
                if (trace.ClassName == "WorkerW" && trace.Samples.Count < 12)
                    trace.Samples.Add($"{message:X}(w={wParam:X},l={lParam:X})");
            }
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        public void Dispose()
        {
            foreach (var hwnd in _windows.Keys)
                if (IsWindow(hwnd)) RemoveWindowSubclass(hwnd, _callback, SubclassId);
        }

        private sealed class WindowTrace(string className)
        {
            internal string ClassName { get; } = className;
            internal int Count { get; set; }
            internal List<string> Samples { get; } = [];
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ArrayCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DisplayName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ShellItemAttributes(nint self, uint requested, out uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumNext(nint self, uint count, out nint item, out uint fetched);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadTimes(nint threadHandle, out long creationTime, out long exitTime, out long kernelTime, out long userTime);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW", ExactSpelling = true)] private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", ExactSpelling = true, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW", ExactSpelling = true)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetKeyboardState([In] byte[] state);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint sfgaoOut);
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint first, nint second);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
    private delegate bool EnumWindowCallback(nint hwnd, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    private sealed class DedicatedThreadHeartbeatProbe : IDisposable
    {
        private const uint ThreadQueryLimitedInformation = 0x0800;
        private readonly ManualResetEvent _stop = new(false);
        private readonly Thread _thread;
        private readonly nint _dispatcherThreadHandle;
        private long _maxGapTicks;
        private long _dispatcherThreadWallTicks;
        private long _dispatcherThreadCpuFileTimeTicks;
        private long _dispatcherThreadMaxSampleCpuPercent;
        private long _dispatcherThreadMaxBusyTicks;
        private long _dispatcherThreadCurrentBusyTicks;
        private int _stopped;
        private int _disposed;

        internal DedicatedThreadHeartbeatProbe(uint dispatcherThreadId)
        {
            if (dispatcherThreadId != 0)
                _dispatcherThreadHandle = OpenThread(ThreadQueryLimitedInformation, false, dispatcherThreadId);
            _thread = new Thread(Run) { IsBackground = true, Name = "ExplorerDirs stress heartbeat" };
            _thread.Start();
        }

        internal long MaxGapMilliseconds => (long)(Interlocked.Read(ref _maxGapTicks) * 1000d / Stopwatch.Frequency);
        internal double DispatcherThreadCpuAveragePercent
        {
            get
            {
                var wallTicks = Interlocked.Read(ref _dispatcherThreadWallTicks);
                return wallTicks == 0 ? -1 : Math.Clamp(Interlocked.Read(ref _dispatcherThreadCpuFileTimeTicks) * 100d /
                    (wallTicks * 10_000_000d / Stopwatch.Frequency), 0, 100);
            }
        }
        internal double DispatcherThreadCpuMaxSamplePercent => Interlocked.Read(ref _dispatcherThreadMaxSampleCpuPercent) / 100d;
        internal long DispatcherThreadMaxBusyMilliseconds => (long)(Interlocked.Read(ref _dispatcherThreadMaxBusyTicks) * 1000d / Stopwatch.Frequency);

        internal void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0) _stop.Set();
            _thread.Join();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Stop();
            _stop.Dispose();
            if (_dispatcherThreadHandle != 0) CloseHandle(_dispatcherThreadHandle);
        }

        private void Run()
        {
            var previous = Stopwatch.GetTimestamp();
            var previousCpu = TryGetDispatcherThreadCpu();
            var previousCpuSampleAt = Stopwatch.GetTimestamp();
            while (!_stop.WaitOne(20))
            {
                var now = Stopwatch.GetTimestamp();
                var gap = now - previous;
                long observed;
                do
                {
                    observed = Interlocked.Read(ref _maxGapTicks);
                    if (gap <= observed) break;
                } while (Interlocked.CompareExchange(ref _maxGapTicks, gap, observed) != observed);
                if (now - previousCpuSampleAt >= Stopwatch.Frequency / 10)
                {
                    var currentCpu = TryGetDispatcherThreadCpu();
                    var cpuSampleAt = Stopwatch.GetTimestamp();
                    if (previousCpu >= 0 && currentCpu >= previousCpu)
                    {
                        var wallTicks = cpuSampleAt - previousCpuSampleAt;
                        var cpuTicks = currentCpu - previousCpu;
                        Interlocked.Add(ref _dispatcherThreadWallTicks, wallTicks);
                        Interlocked.Add(ref _dispatcherThreadCpuFileTimeTicks, cpuTicks);
                        var samplePercent = wallTicks == 0 ? 0 : Math.Clamp(cpuTicks * 100d /
                            (wallTicks * 10_000_000d / Stopwatch.Frequency), 0, 100);
                        UpdateMaximum(ref _dispatcherThreadMaxSampleCpuPercent, (long)(samplePercent * 100d));
                        if (samplePercent >= 70)
                        {
                            var busyTicks = Interlocked.Add(ref _dispatcherThreadCurrentBusyTicks, wallTicks);
                            UpdateMaximum(ref _dispatcherThreadMaxBusyTicks, busyTicks);
                        }
                        else Interlocked.Exchange(ref _dispatcherThreadCurrentBusyTicks, 0);
                    }
                    previousCpuSampleAt = cpuSampleAt;
                    previousCpu = currentCpu;
                }
                previous = now;
            }
        }

        private long TryGetDispatcherThreadCpu()
        {
            if (_dispatcherThreadHandle == 0 || !GetThreadTimes(_dispatcherThreadHandle, out _, out _, out var kernelTime, out var userTime))
                return -1;
            return kernelTime + userTime;
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long observed;
            do
            {
                observed = Interlocked.Read(ref target);
                if (value <= observed) return;
            } while (Interlocked.CompareExchange(ref target, value, observed) != observed);
        }
    }
}
