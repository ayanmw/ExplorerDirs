using System.Runtime.InteropServices;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

// Opt-in capability probes; individual commands define their window and input boundaries.
// Results concern native components, not the application's GUI/focus behavior.
internal static class Program
{
    static Guid BrowserClass = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    static Guid BrowserId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    static Guid ProviderId = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    static Guid TravelId = new("7EBFDD80-AD18-11D3-A4C5-00C04F72D6B8");
    static bool InspectNavigationInterfaces;
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--inspect-qdr-viewstate")
            return QdrViewStateProbe.Run(args[1]);
        if (args.Length == 1 && args[0] == "--virtual-zip-color-rules")
            return VirtualZipColorRuleProbe.Run();
        if (args.Length == 1 && args[0] == "--search-identity")
            return SearchFolderProbe.Run();
        if (args.Length == 1 && args[0] == "--search-virtual-scopes")
            return SearchFolderProbe.ProbeVirtualScopes();
        if (args.Length == 2 && args[0] == "--search-enumerate-uri")
            return SearchFolderProbe.EnumerateVirtualSearch(args[1]);
        if (args.Length == 1 && args[0] == "--live-updates")
            return LiveUpdateProbe.Run();
        if (args.Length == 1 && args[0] == "--refresh-comparison")
            return RefreshComparisonProbe.Run();
        if (args.Length == 1 && args[0] == "--live-updates-wpf")
            return LiveUpdateProbe.Run(useWpfPump: true);
        if (args.Length == 1 && args[0] == "--live-updates-view-flags")
            return LiveUpdateProbe.RunViewFlagMatrix();
        if (args.Length == 1 && args[0] == "--live-updates-view-flags-wpf")
            return LiveUpdateProbe.RunViewFlagMatrix(useWpfPump: true);
        if (args.Length == 1 && args[0] == "--qdr-native-stream")
            return QdrNativeViewProbe.Run();
        if (args.Length == 1 && args[0] == "--shell-change-reconciliation")
            return ShellChangeReconciliationProbe.Run();
        if (args.Length == 1 && args[0] == "--shell-watcher-recovery-contract")
            return ShellWatcherRecoveryProbe.Run();
        if (args.Length == 1 && args[0] == "--shell-listview-selection-restore")
            return ShellListViewSelectionRestoreProbe.Run();
        if (args.Length == 1 && args[0] == "--shell-view-state-stream-smoke")
            return ShellViewStateStreamSmoke.Run();
        if (args.Length == 1 && args[0] == "--shell-view-real-pidl-navigation-smoke")
            return ShellViewNavigationRealPidlSmoke.Run();
        if (args.Length == 4 && args[0] is "--shell-view-state-store-child-write" or "--shell-view-state-store-child-read" or
                "--shell-view-state-store-child-race-write")
            return ShellViewStateStreamSmoke.RunChildCommand(args);
        if (args.Length == 1 && args[0] == "--static-shell-column-contract-smoke")
            return StaticShellColumnContractSmoke.Run();
        if (args.Length == 1 && args[0] == "--wrapped-pidl-contract-smoke")
            return WrappedPidlContractSmoke.Run();
        if (args.Length == 1 && args[0] == "--static-shell-column-classid-smoke")
            return StaticShellFolderAdapterProbe.RunFolderContract(requireIdentity: true, strictInterfaces: true, prototypeClass: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-classid-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellColumnAdapter(strictInterfaces: true, prototypeClass: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-identity-strict-smoke")
            return StaticShellFolderAdapterProbe.RunFolderContract(requireIdentity: true, strictInterfaces: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-adapter-strict-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellColumnAdapter(strictInterfaces: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-identity-smoke")
            return StaticShellFolderAdapterProbe.RunFolderContract(requireIdentity: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-folder-smoke")
            return StaticShellFolderAdapterProbe.RunFolderContract();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-size-cache-unload-smoke")
            return RecursiveSizeCacheLifecycleProbe.Run();
        if (args.Length == 1 && args[0] == "--synthetic-child-notification-routing-smoke")
            return StaticShellFolderAdapterProbe.RunChildNotificationRoutingContractSmoke();
        if (args.Length == 1 && args[0] == "--static-shell-column-wrapped-folder-smoke")
            return StaticShellFolderAdapterProbe.RunFolderContract(wrappedPidls: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-parent-item-smoke")
            return StaticShellFolderAdapterProbe.RunFolderContract(wrappedPidls: true, shellItemParentProbe: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-adapter-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellColumnAdapter();
        if (args.Length == 1 && args[0] == "--static-shell-column-legacy-details-callback-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellColumnAdapter(legacyDetailsCallbackProbe: true);
        if (args.Length == 1 && args[0] == "--static-shell-column-wrapped-adapter-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellColumnAdapter(wrappedPidls: true);
        if (args.Length == 1 && args[0] == "--static-shell-item-bound-view-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellItemBoundView();
        if (args.Length == 1 && args[0] == "--synthetic-shell-size-sort-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticShellSizeSort();
        if (args.Length == 1 && args[0] == "--synthetic-shell-two-view-target-sync-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticShellTwoViewTargetSync();
        if (args.Length == 1 && args[0] == "--synthetic-shell-focus-recovery-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticShellFocusRecoveryExperiment();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSort();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-slow-column-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithSlowColumn();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithWpfHost();
        if (args.Length == 1 && args[0] == "--recursive-size-view-state-restore-private-desktop")
            return PrivateDesktopShellProbe.RunRecursiveSizeViewStateRestoreProbe();
        if (args.Length == 2 && args[0] == "--qdr-imported-view-state-restore-private-desktop")
            return PrivateDesktopShellProbe.RunQdrImportedViewStateRestoreProbe(args[1]);
        if (args.Length == 3 && args[0] == "--qdr-imported-view-state-restore-private-desktop" &&
            uint.TryParse(args[2], out var qdrViewMode))
            return PrivateDesktopShellProbe.RunQdrImportedViewStateRestoreProbe(args[1], qdrViewMode);
        if (args.Length == 3 && args[0] == "--qdr-imported-view-state-restore-private-desktop" &&
            args[2] == "descending")
            return PrivateDesktopShellProbe.RunQdrImportedViewStateRestoreProbe(args[1], requireDescending: true);
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-on-demand-column-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithOnDemandColumnWpfHost();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-async-on-demand-column-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithAsyncOnDemandColumnWpfHost();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-large-background-refresh-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeLargeBackgroundRefreshWpfHost();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-formatted-column-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithFormattedColumnWpfHost();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-fixed-kilobyte-column-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithFixedKilobyteColumnWpfHost();
        if (args.Length == 1 && args[0] == "--synthetic-recursive-directory-size-grouped-bytes-wpf-host-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticRecursiveDirectorySizeSortWithGroupedBytesWpfHost();
        if (args.Length == 1 && args[0] == "--synthetic-relative-percent-column-int-private-desktop")
            return PrivateDesktopShellProbe.RunRelativePercentColumnMetadata(0x2);
        if (args.Length == 1 && args[0] == "--synthetic-relative-percent-column-string-private-desktop")
            return PrivateDesktopShellProbe.RunRelativePercentColumnMetadata(0x1);
        if (args.Length == 1 && args[0] == "--synthetic-relative-percent-column-default-private-desktop")
            return PrivateDesktopShellProbe.RunRelativePercentColumnMetadata(0x0);
        if (args.Length == 1 && args[0] == "--synthetic-shell-factory-snapshot-cache-private-desktop")
            return PrivateDesktopShellProbe.RunSyntheticShellFactorySnapshotCache();
        if (args.Length == 1 && args[0] == "--synthetic-shell-folder-tree-smoke")
            return SyntheticShellSortProbe.RunFolderTreeContractSmoke();
        if (args.Length == 1 && args[0] == "--synthetic-on-demand-recursive-size-column-smoke")
            return SyntheticShellSortProbe.RunOnDemandRecursiveSizeColumnSmoke();
        if (args.Length == 1 && args[0] == "--synthetic-async-on-demand-recursive-size-column-smoke")
            return SyntheticShellSortProbe.RunAsyncOnDemandRecursiveSizeColumnSmoke();
        if (args.Length == 1 && args[0] == "--synthetic-folder-enumeration-scan-cost-probe")
            return SyntheticShellSortProbe.RunFolderEnumerationScanCostProbe();
        if (args.Length == 1 && args[0] == "--synthetic-dll-unload-contract-smoke")
            return SyntheticShellSortProbe.RunNativeLibraryUnloadContractSmoke();
        if (args.Length == 1 && args[0] == "--static-shell-column-adapter-activated-strict-private-desktop")
            return PrivateDesktopShellProbe.RunStaticShellColumnAdapter(strictInterfaces: true, activateView: true);
        if (args.Length == 1 && args[0] == "--ishellview-color-spike")
            return ShellViewColorSpike.Run();
        if (args.Length == 1 && args[0] == "--shell-view-color-policy-smoke")
            return ShellViewColorPolicySmoke.Run();
        if (args.Length == 1 && args[0] == "--shell-menu-group-routing-smoke")
            return ShellMenuGroupRoutingSmoke.Run();
        if (args.Length == 1 && args[0] == "--shell-view-drop-target-registration-smoke")
            return ShellViewDropTargetRegistrationSmoke.Run();
        if (args.Length == 1 && args[0] == "--shell-view-drop-target-cleanup-smoke")
            return ShellViewDropTargetCleanupSmoke.Run();
        if (args.Length == 1 && args[0] == "--private-desktop-isolation-policy-smoke")
            return PrivateDesktopIsolationPolicySmoke.Run();
        if (args.Length == 1 && args[0] == "--shell-view-drop-target-private-desktop")
            return PrivateDesktopShellProbe.RunDropTarget();
        if (args.Length == 1 && args[0] == "--shell-view-drop-target-after-recreate-private-desktop")
            return PrivateDesktopShellProbe.RunDropTargetAfterViewWindowRecreation();
        if (args.Length == 2 && args[0] == "--shell-view-ole-drop-copy" && args[1] == "--AllowInteractiveDesktopControl")
            return ShellViewDropTargetProbe.RunInteractiveOleCopy();
        if (args.Length == 2 && args[0] == "--shell-view-ole-drop-move" && args[1] == "--AllowInteractiveDesktopControl")
            return ShellViewDropTargetProbe.RunInteractiveOleMove();
        if (args.Length == 1 && args[0] == "--ishellview-command-state-readonly")
            return ShellViewColorSpike.Run(queryCommandStatesOnly: true);
        if (args.Length == 1 && args[0] == "--ishellview-command-state-readonly-private-desktop")
            return PrivateDesktopShellProbe.RunCommandStateReadOnly();
        if (args.Length == 1 && args[0] == "--ishellview-host-filter")
            return ShellViewHostFilterProbe.Run();
        if (args.Length == 1 && args[0] == "--ishellview-host-filter-private-desktop")
            return PrivateDesktopShellProbe.RunHostFilter();
        if (args.Length == 1 && args[0] == "--ishellview-native-pidl-lifetime")
            return ShellViewNativePidlLifetimeProbe.Run();
        if (args.Length == 1 && args[0] == "--ishellview-native-view-lifetime-private-desktop")
            return PrivateDesktopShellProbe.RunNativeViewLifetime();
        if (args.Length == 1 && args[0] == "--ishellview-native-view-lifetime-reuse-host-private-desktop")
            return PrivateDesktopShellProbe.RunNativeViewLifetime(reuseBrowserHost: true);
        if (args.Length == 1 && args[0] == "--ishellview-native-view-object-only-private-desktop")
            return PrivateDesktopShellProbe.RunNativeViewObjectOnly();
        if (args.Length == 1 && args[0] == "--ishellview-native-view-stage-trace-private-desktop")
            return PrivateDesktopShellProbe.RunNativeViewStageTrace();
        if (args.Length == 1 && args[0] == "--ishellview-refresh-lifecycle-private-desktop")
            return PrivateDesktopShellProbe.RunNativeViewRefreshLifecycle();
        if (args.Length == 1 && args[0] == "--private-desktop-owner-smoke")
            return PrivateDesktopShellProbe.RunDesktopOwnershipSmoke();
        if (args.Length == 2 && args[0] == "--native-minidump-smoke")
            return PrivateDesktopShellProbe.RunMiniDumpCaptureSmoke(args[1]);
        if (args.Length == 1 && args[0] == "--private-desktop-window-destroy-control")
            return PrivateDesktopShellProbe.RunWindowDestroyControl();
        if (args.Length == 1 && args[0] == "--private-desktop-window-visibility-control")
            return PrivateDesktopShellProbe.RunWindowVisibilityControl();
        if (args.Length == 1 && args[0] == "--private-desktop-shared-window-destroy-control")
            return PrivateDesktopShellProbe.RunSharedDesktopWindowDestroyControl();
        if (args.Length == 1 && args[0] == "--private-desktop-shell-view-window-destroy-control")
            return PrivateDesktopShellProbe.RunShellViewWindowDestroyControl();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneSynchronousCleanup();
        if (args.Length == 2 && args[0] == "--private-desktop-multi-pane-sync-cleanup" &&
            int.TryParse(args[1], out var cleanupPaneCount))
            return PrivateDesktopShellProbe.RunMultiPaneSynchronousCleanup(cleanupPaneCount);
        if (args.Length == 1 && args[0] == "--private-desktop-four-pane-menu-sync-cleanup")
            return PrivateDesktopShellProbe.RunFourPaneMenuSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-nofocus-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuNoFocusSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-active-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuActiveSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-detached-ime-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuDetachedImeSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-disabled-ime-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuDisabledImeSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-ole-init-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuOleInitializedSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-wpf-dispatcher-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuWpfDispatcherSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-wpf-ole-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuWpfDispatcherSynchronousCleanup(initializeOle: true);
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-wpf-host-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuWpfHostSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-four-pane-menu-wpf-host-sync-cleanup")
            return PrivateDesktopShellProbe.RunFourPaneMenuWpfHostSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-four-pane-wpf-host-keyboard-consumer")
            return PrivateDesktopShellProbe.RunFourPaneWpfHostKeyboardConsumer();
        if (args.Length == 1 && args[0] == "--private-desktop-four-pane-wpf-host-directory-isolation")
            return PrivateDesktopShellProbe.RunFourPaneWpfHostDirectoryIsolation();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-detached-view-ime-sync-cleanup")
            return PrivateDesktopShellProbe.RunSinglePaneMenuDetachedViewImeSynchronousCleanup();
        if (args.Length == 1 && args[0] == "--private-desktop-single-pane-menu-late-coordinator-dispose")
            return PrivateDesktopShellProbe.RunSinglePaneLateCoordinatorDispose();
        if (args.Length == 1 && args[0] == "--window-destroy-current-desktop-child")
            return PrivateDesktopShellProbe.RunWindowDestroyControlOnCurrentDesktop();
        if (args.Length == 1 && args[0] == "--ishellview-menu-command-private-desktop")
            return PrivateDesktopShellProbe.RunHostMenuCommands();
        if (args.Length == 1 && args[0] == "--ishellview-newfolder-private-desktop")
            return PrivateDesktopShellProbe.RunHostNewFolder();
        if (args.Length == 1 && args[0] == "--ishellview-recursive-size-projection-private-desktop")
            return PrivateDesktopShellProbe.RunHostRecursiveSizeProjection();
        if (args.Length == 1 && args[0] == "--ishellview-recursive-size-multiview-private-desktop")
            return PrivateDesktopShellProbe.RunHostMultiViewRecursiveSizeProjection();
        if (args.Length == 2 && args[0] == "--ishellview-host-filter-virtual-private-desktop")
            return PrivateDesktopShellProbe.RunHostFilterVirtualScope(args[1]);
        if (args.Length == 1 && args[0] == "--ishellview-color-spike-private-desktop")
            return PrivateDesktopShellProbe.RunColor();
        if (args.Length == 1 && args[0] == "--ishellview-zip-color-pixels-private-desktop")
            return PrivateDesktopShellProbe.RunZipColorPixels();
        if (args.Length == 1 && args[0] == "--ishellview-zip-background-menu-private-desktop")
            return PrivateDesktopShellProbe.RunZipViewBackgroundMenu();
        if (args.Length == 1 && args[0] == "--ishellview-zip-extract-private-desktop")
            return PrivateDesktopShellProbe.RunZipViewExtractCommand();
        if (args.Length == 1 && args[0] == "--ishellview-zip-copy-items-private-desktop")
            return PrivateDesktopShellProbe.RunZipViewCopyItems();
        if (args.Length == 1 && args[0] == "--ishellview-zip-move-cross-volume-private-desktop")
            return PrivateDesktopShellProbe.RunZipCrossVolumeMove();
        if (args.Length == 1 && args[0] == "--ishellview-zip-folder-move-cross-volume-private-desktop")
            return PrivateDesktopShellProbe.RunZipFolderCrossVolumeMove();
        if (args.Length == 1 && args[0] == "--ishellview-zip-delete-items-private-desktop")
            return PrivateDesktopShellProbe.RunZipViewDeleteItems();
        if (args.Length == 1 && args[0] == "--ishellview-this-pc-color-pixels-private-desktop")
            return PrivateDesktopShellProbe.RunThisPcColorPixels();
        if (args.Length == 1 && args[0] == "--ishellview-libraries-color-pixels-private-desktop")
            return PrivateDesktopShellProbe.RunLibrariesColorPixels();
        if (args.Length == 1 && args[0] == "--ishellview-desktop-color-pixels-private-desktop")
            return PrivateDesktopShellProbe.RunDesktopColorPixels();
        if (args.Length == 1 && args[0] == "--ishellview-network-places-row-map-private-desktop")
            return PrivateDesktopShellProbe.RunNetworkPlacesRowMap();
        if (args.Length == 1 && args[0] == "--ishellview-network-places-color-pixels-private-desktop")
            return PrivateDesktopShellProbe.RunNetworkPlacesColorPixels();
        if (args.Length == 1 && args[0] == "--ishellview-network-places-child-drop-targets-private-desktop")
            return PrivateDesktopShellProbe.RunNetworkPlacesChildDropTargets();
        if (args.Length == 1 && args[0] == "--explorerbrowser-color-notifications-private-desktop")
            return PrivateDesktopShellProbe.RunExplorerBrowserColorNotifications();
        if (args.Length == 1 && args[0] == "--explorerbrowser-wrapper-object-private-desktop")
            return PrivateDesktopShellProbe.RunExplorerBrowserWrapperObject();
        if (args.Length == 1 && args[0] == "--explorerbrowser-registered-namespace-private-desktop")
            return PrivateDesktopShellProbe.RunExplorerBrowserRegisteredNamespace();
        if (args.Length == 3 && args[0] == "--shell-change-create-file-child")
            return ExplorerBrowserWrapperProbe.RunExternalShellNotificationChild(args[1], args[2]);
        if (args.Length == 1 && args[0] == "--column-filter-capability-private-desktop")
            return PrivateDesktopShellProbe.RunColumnFilterCapability();
        if (args.Length == 1 && args[0] == "--ishellview-menu-activation-private-desktop")
            return PrivateDesktopShellProbe.RunMenuActivation();
        if (args.Length == 1 && args[0] == "--ishellview-session-restore-private-desktop")
            return PrivateDesktopShellProbe.RunSessionRestore();
        if (args.Length == 1 && args[0] == "--ishellview-session-restore-sync-cleanup-private-desktop")
            return PrivateDesktopShellProbe.RunSessionRestore(synchronousCleanupProbe: true);
        if (args.Length == 4 && args[0] == "--ishellview-session-restore-consumer-private-desktop" &&
            Guid.TryParse(args[3], out var restoreSessionId))
            return PrivateDesktopShellProbe.RunSessionRestoreConsumer(args[1], args[2], restoreSessionId);
        if (args.Length == 5 && args[0] == "--ishellview-session-restore-consumer-private-desktop" &&
            args[4] == "--sync-cleanup" && Guid.TryParse(args[3], out restoreSessionId))
            return PrivateDesktopShellProbe.RunSessionRestoreConsumer(args[1], args[2], restoreSessionId,
                synchronousCleanupProbe: true);
        if (args.Length == 1 && args[0] == "--ishellview-color-readonly-private-desktop")
            return PrivateDesktopShellProbe.RunReadOnlyColorRule();
        if (args.Length == 1 && args[0] == "--ishellview-color-readonly-notification-diagnostic-private-desktop")
            return PrivateDesktopShellProbe.RunReadOnlyColorNotificationDiagnostic();
        if (args.Length == 1 && args[0] == "--ishellview-color-readonly-notification-sync-diagnostic-private-desktop")
            return PrivateDesktopShellProbe.RunReadOnlyColorSynchronousNotificationDiagnostic();
        if (args.Length == 1 && args[0] == "--ishellview-color-directory-private-desktop")
            return PrivateDesktopShellProbe.RunDirectoryColorRule();
        if (args.Length == 1 && args[0] == "--ishellview-color-hidden-private-desktop")
            return PrivateDesktopShellProbe.RunHiddenColorRule();
        if (args.Length == 1 && args[0] == "--common-listview-color-baseline-private-desktop")
            return PrivateDesktopShellProbe.RunCommonListViewColorBaseline();
        if (args.Length == 1 && args[0] == "--common-listview-data-contract")
            return CommonListViewColorBaseline.RunDataContract();
        if (args.Length == 1 && args[0] == "--common-listview-virtual-folder-contract")
            return CommonListViewVirtualFolderContract.Run();
        if (args.Length == 1 && args[0] == "--filesystem-folder-background-menu")
            return FileSystemFolderBackgroundMenuProbe.Run();
        if (args.Length == 1 && args[0] == "--zip-background-menu-commands")
            return ZipBackgroundMenuCommandProbe.Run();
        if (args.Length == 3 && args[0] == "--zip-background-menu-verb-child")
            return ZipBackgroundMenuCommandProbe.RunVerbChild(args[1], args[2]);
        if (args.Length == 1 && args[0] == "--common-listview-relative-size-contract")
            return CommonListViewColorBaseline.RunRelativeSizeContract();
        if (args.Length == 1 && args[0] == "--common-listview-navigation-contract")
            return CommonListViewColorBaseline.RunNavigationContract();
        if (args.Length == 1 && args[0] == "--ishellview-grouped-filter-export")
            return ShellViewHostFilterProbe.RunGroupedExportOnly();
        if (args.Length == 1 && args[0] == "--ishellview-color-spike-offscreen")
            return ShellViewColorSpike.Run(visible: true, offscreen: true);
        if (args.Length == 1 && args[0] == "--auto-column-width")
            return ShellAutoColumnWidthProbe.Run();
        if (args.Length == 1 && args[0] == "--photo-print-verb")
            return PhotoPrintVerbProbe.Run();
        if (args.Length == 1 && args[0] == "--ishellview-selection-batch-spike")
            return ShellViewSelectionBatchSpike.Run();
        if (args.Length == 1 && args[0] == "--ishellview-selection-enumeration")
            return ShellViewSelectionEnumerationProbe.Run();
        if (args.Length == 1 && args[0] == "--ishellview-selection-enumeration-restore")
            return ShellViewSelectionEnumerationProbe.RunRestoreDuringRefresh();
        if (args.Length == 1 && args[0] == "--ishellview-selection-enumeration-restore-stale")
            return ShellViewSelectionEnumerationProbe.RunRestoreDuringRefresh(omitLastTarget: true);
        if (args.Length == 1 && args[0] == "--ishellview-selection-enumeration-restore-stale-unselected-focus")
            return ShellViewSelectionEnumerationProbe.RunRestoreDuringRefresh(omitLastTarget: true, unselectedFocus: true);
        if (args.Length == 1 && args[0] == "--file-operation-collision")
            return ShellFileOperationConflictProbe.Run();
        if (args.Length == 1 && args[0] == "--folder-view-options-unhosted")
            return FolderViewOptionsProbe.Run();
        if (args.Length == 2 && args[0] == "--ishellview-color-spike-visible" && args[1] == "--AllowInteractiveDesktopControl")
            return ShellViewColorSpike.Run(visible: true);
        if (args.Length == 1 && args[0] == "--selection-properties")
            return SelectionPropertyProbe.Run();
        if (args.Length == 1 && args[0] == "--selection-properties-scale")
            return SelectionPropertyProbe.RunScale();
        if (args.Length == 1 && args[0] == "--standalone-tree")
            return StandaloneTreeProbe.Run();
        if (args.Length == 1 && args[0] == "--tree-host-core")
            return StandaloneTreeProbe.RunCore();
        if (args.Length == 1 && args[0] == "--tree-icon-render-ab")
            return StandaloneTreeProbe.RunIconRenderAb();
        if (args.Length == 1 && args[0] == "--tree-icon-initial-size-ab")
            return StandaloneTreeProbe.RunIconInitializationSizeAb();
        if (args.Length == 1 && args[0] == "--tree-icon-callback-smoke")
            return StandaloneTreeProbe.RunIconContract();
        if (args.Length != 1 || args[0] is not ("--selection-commands" or "--search-folder-navigation" or "--navigation-capability" or "--navigation-cancel" or "--menu-capability" or "--color-capability" or "--pane-capability" or "--pane-capability-selected" or "--pane-capability-off" or "--pane-same-location" or "--view-style-capability" or "--view-style-synchronized-hosted" or "--view-data-capability" or "--view-property-refresh-capability" or "--folder-filter-capability" or "--folder-header-options-hosted"))
        {
            Console.WriteLine("Usage: --navigation-capability (hidden native Shell; creates isolated fixture folders)");
            Console.WriteLine("       --live-updates / --live-updates-wpf (four hidden views, native file changes, optional WPF pump)");
            Console.WriteLine("       --live-updates-view-flags (same matrix plus production site with CDB2GVF_NOINCLUDEITEM disabled)");
            Console.WriteLine("       --live-updates-view-flags-wpf (same view-flag matrix with a WPF Dispatcher pump)");
            Console.WriteLine("       --qdr-native-stream (native Shell directly restores the exported view stream; no input)");
            Console.WriteLine("       --shell-view-state-stream-smoke (IShellBrowser view-state stream save/restore without a Shell view)");
            Console.WriteLine("       --static-shell-column-contract-smoke (numeric static IShellFolder2 column policy only; no Shell window)");
            Console.WriteLine("       --wrapped-pidl-contract-smoke (child-PIDL envelope round-trip; no Shell view or window)");
            Console.WriteLine("       --static-shell-column-folder-smoke (native IShellFolder2 adapter CompareIDs; no window)");
            Console.WriteLine("       --static-shell-column-wrapped-folder-smoke (wrapped child PIDLs through native adapter contracts; no window)");
            Console.WriteLine("       --static-shell-column-adapter-private-desktop (native IShellFolder2/DefView probe on an isolated Windows Desktop)");
            Console.WriteLine("       --synthetic-shell-size-sort-private-desktop (unregistered synthetic IShellFolder2/DefView Size-sort probe)");
            Console.WriteLine("       --synthetic-recursive-directory-size-private-desktop (isolated DefView folder size text and sort check)");
            Console.WriteLine("       --synthetic-recursive-directory-size-slow-column-private-desktop (same check with SHCOLSTATE_SLOW on the recursive column)");
            Console.WriteLine("       --synthetic-recursive-directory-size-wpf-host-private-desktop (same check with a WPF HwndSource root for cleanup comparison)");
            Console.WriteLine("       --synthetic-shell-folder-tree-smoke (synthetic folder enumeration/binding contract only; no Shell view)");
            Console.WriteLine("       --static-shell-column-wrapped-adapter-private-desktop (wrapped PIDL IShellFolder2/DefView probe on an isolated Windows Desktop)");
            Console.WriteLine("       --file-operation-collision (isolated IFileOperation copy collision; preserves the existing destination)");
            Console.WriteLine("       --ishellview-color-spike (isolated native IShellView color-hook feasibility probe; no ExplorerBrowser or input)");
            Console.WriteLine("       --shell-view-color-policy-smoke (selection snapshot and rule-color policy only; no Shell window or files)");
            Console.WriteLine("       --shell-view-drop-target-cleanup-smoke (hidden HWND cleanup contract for interactive and Private Desktop probes)");
            Console.WriteLine("       --shell-view-ole-drop-copy --AllowInteractiveDesktopControl (visible temporary Shell source/target; hold Ctrl for a physical copy drag)");
            Console.WriteLine("       --shell-view-ole-drop-move --AllowInteractiveDesktopControl (visible temporary Shell source/target; hold Shift for a physical move drag)");
            Console.WriteLine("       --ishellview-command-state-readonly (query background Undo/Redo twice; no file operations)");
            Console.WriteLine("       --ishellview-command-state-readonly-private-desktop (same read-only query on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-host-filter (hidden direct IShellView ICommDlgBrowser3 filtering fixture)");
            Console.WriteLine("       --ishellview-host-filter-private-desktop (same keyboard/navigation probe on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-native-pidl-lifetime (real Shell PIDL history stress; no Shell view window)");
            Console.WriteLine("       --ishellview-native-view-lifetime-private-desktop (repeated stock IShellView HWND/COM create-destroy on an isolated Desktop)");
            Console.WriteLine("       --ishellview-native-view-lifetime-reuse-host-private-desktop (same test reusing one IShellBrowser host)");
            Console.WriteLine("       --ishellview-native-view-object-only-private-desktop (repeated CreateViewObject/Release without a view window)");
            Console.WriteLine("       --ishellview-native-view-stage-trace-private-desktop (measure process handles across one stock IShellView lifecycle)");
            Console.WriteLine("       --ishellview-refresh-lifecycle-private-desktop (trace stock view/list HWND and row count across refresh and mode changes)");
            Console.WriteLine("       --private-desktop-owner-smoke (create and close an isolated Desktop without changing this thread's desktop or foreground)");
            Console.WriteLine("       --private-desktop-isolation-policy-smoke (test the isolation result policy without creating windows)");
            Console.WriteLine("       --native-minidump-smoke <absolute-path> (write and inspect an opt-in diagnostic minidump; no Shell window)");
            Console.WriteLine("       --private-desktop-window-destroy-control (create and destroy one isolated STATIC window without Shell view creation or foreground change)");
            Console.WriteLine("       --private-desktop-window-visibility-control (show and destroy a SW_SHOWNOACTIVATE STATIC window without Shell view creation)");
            Console.WriteLine("       --private-desktop-shared-window-destroy-control (two processes create/destroy STATIC windows on one isolated Desktop, without Shell views)");
            Console.WriteLine("       --private-desktop-shell-view-window-destroy-control (destroy a plain STATIC window while one real IShellView is active on the same isolated Desktop)");
            Console.WriteLine("       --private-desktop-single-pane-sync-cleanup (synchronously destroy one IShellView, its pane, and its parent on an isolated Desktop)");
            Console.WriteLine("       --private-desktop-multi-pane-sync-cleanup <2|3|4> (synchronously destroy multiple IShellViews and their parent on an isolated Desktop)");
            Console.WriteLine("       --private-desktop-four-pane-menu-sync-cleanup (activate and route native Shell menus in four panes, then synchronously clean them up)");
            Console.WriteLine("       --private-desktop-single-pane-menu-ole-init-sync-cleanup (repeat single-pane menu teardown with OLE initialized on the probe STA)");
            Console.WriteLine("       --private-desktop-single-pane-menu-wpf-dispatcher-sync-cleanup (repeat single-pane menu teardown with a WPF DispatcherFrame pump)");
            Console.WriteLine("       --private-desktop-single-pane-menu-wpf-ole-sync-cleanup (same probe with OLE initialized and WPF DispatcherFrame pumping)");
            Console.WriteLine("       --private-desktop-single-pane-menu-wpf-host-sync-cleanup (same probe hosted under a WPF HwndSource root)");
            Console.WriteLine("       --private-desktop-four-pane-menu-wpf-host-sync-cleanup (four Shell panes hosted by WPF HwndHost children; activates/deactivates each menu before teardown)");
            Console.WriteLine("       --private-desktop-four-pane-wpf-host-keyboard-consumer (Ctrl+A, F2 arrow routing, menu activation, and synchronous cleanup through four WPF-hosted Shell panes)");
            Console.WriteLine("       --private-desktop-four-pane-wpf-host-directory-isolation (Shell file updates reach three WPF panes while an isolated fourth pane remains unchanged)");
            Console.WriteLine("       --ishellview-host-filter-virtual-private-desktop <scope> (one virtual Shell scope on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-color-spike-private-desktop (visible color-pixel probe on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-zip-color-pixels-private-desktop (ZIP virtual-item color pixels on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-zip-background-menu-private-desktop (query ZIP IShellView background-menu commands on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-zip-extract-private-desktop (diagnose ZIP extract verb dispatch against an isolated fixture)");
            Console.WriteLine("       --ishellview-zip-copy-items-private-desktop (copy a virtual ZIP entry through IFileOperation on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-zip-move-cross-volume-private-desktop (move a virtual ZIP entry to another volume and verify source-view convergence)");
        Console.WriteLine("       --ishellview-zip-folder-move-cross-volume-private-desktop (move a virtual ZIP folder first to another volume and observe Shell change notifications)");
            Console.WriteLine("       --ishellview-zip-delete-items-private-desktop (probe deleting a virtual ZIP entry through IFileOperation on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-this-pc-color-pixels-private-desktop (This PC virtual-item color pixels on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-libraries-color-pixels-private-desktop (Libraries virtual-item color pixels on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-desktop-color-pixels-private-desktop (Desktop Shell item color pixels on a separate Windows Desktop)");
            Console.WriteLine("       --ishellview-network-places-row-map-private-desktop (compare Network Places Shell items with DefView rows; labels are redacted)");
            Console.WriteLine("       --ishellview-network-places-color-pixels-private-desktop (identity-map Network Places Shell rows before isolated color sampling)");
            Console.WriteLine("       --ishellview-network-places-child-drop-targets-private-desktop (query child IDropTarget availability without invoking Drop)");
            Console.WriteLine("       --ishellview-color-readonly-private-desktop (same pixel probe with a temporary read-only #RO fixture)");
            Console.WriteLine("       --ishellview-color-readonly-notification-diagnostic-private-desktop (diagnose UPDATEITEM delivery and cleanup stages)");
            Console.WriteLine("       --ishellview-color-readonly-notification-sync-diagnostic-private-desktop (compare synchronous UPDATEITEM notification delivery)");
            Console.WriteLine("       --ishellview-color-directory-private-desktop (same pixel probe with a #DIR folder fixture)");
            Console.WriteLine("       --ishellview-color-hidden-private-desktop (same pixel probe with an explicitly included #Hidden fixture)");
            Console.WriteLine("       --common-listview-color-baseline-private-desktop (standard ListView custom-draw control comparison)");
            Console.WriteLine("       --common-listview-data-contract (hidden standard ListView update/sort contract)");
            Console.WriteLine("       --common-listview-virtual-folder-contract (hidden ListView rows from virtual Control Panel Shell items)");
            Console.WriteLine("       --filesystem-folder-background-menu (filesystem IShellFolder background menu; no Shell view or popup)");
            Console.WriteLine("       --common-listview-relative-size-contract (hidden Shell row relative-size update contract)");
            Console.WriteLine("       --common-listview-navigation-contract (hidden Shell folder PIDL, parent, and history contract)");
        Console.WriteLine("       --ishellview-color-spike-offscreen (same probe with WS_VISIBLE window outside the virtual desktop; no input)");
            Console.WriteLine("       --auto-column-width (hidden native IShellView Ctrl+NumPad+ column-width probe)");
            Console.WriteLine("       --photo-print-verb (query the canonical Shell Print verb for one isolated temporary PNG; does not display or invoke it)");
            Console.WriteLine("       --folder-view-options-unhosted (uninitialized ExplorerBrowser COM object; tests FVO setter without a parent window)");
            Console.WriteLine("       --folder-header-options-hosted (OPT-IN hidden ExplorerBrowser: sets FVO_CUSTOMPOSITION before Initialize and records OnViewCreated folder flags)");
            Console.WriteLine("       --ishellview-selection-batch-spike (isolated native selection restore, direct and stale-PIDL fallback; no ExplorerBrowser or input)");
            Console.WriteLine("       --ishellview-selection-enumeration-restore[-stale[-unselected-focus]] (hidden stock IShellView selection/focus recovery)");
            Console.WriteLine("       --ishellview-color-spike-visible --AllowInteractiveDesktopControl (show only the isolated test view; foreground is guarded)");
            Console.WriteLine("       --virtual-zip-color-rules (evaluate Q-Dir color rules from virtual ZIP Shell rows without filesystem paths)");
            Console.WriteLine("       --search-virtual-scopes (read-only Shell parsing and search-folder scope creation; no result enumeration)");
            Console.WriteLine("       --selection-properties (read-only Shell properties of isolated fixture files)");
            Console.WriteLine("       --selection-properties-scale (timed direct Shell aggregation for 25/250/1000 isolated files; no ExplorerBrowser)");
            Console.WriteLine("       --menu-capability (read-only native menu states; never invokes commands)");
            Console.WriteLine("       --selection-commands (selection changes in a hidden fixture view; no file operations)");
            Console.WriteLine("       --color-capability (hidden child classes and available ListView colors)");
            Console.WriteLine("       --explorerbrowser-color-notifications-private-desktop (observe Shell view WM_NOTIFY without switching the user desktop)");
            Console.WriteLine("       --column-filter-capability-private-desktop (probe native header dropdown and IShellFolder2 item properties on an isolated Windows Desktop)");
            Console.WriteLine("       --pane-capability (experimental hidden native navigation/preview frames)");
            Console.WriteLine("       --pane-capability-selected (same experiment after selecting an isolated text fixture)");
            Console.WriteLine("       --pane-capability-off (same experiment with initial pane state off)");
            Console.WriteLine("       --navigation-cancel (host veto of isolated navigation; no error dialogs)");
            Console.WriteLine("       --pane-same-location (tree preference refresh without changing location)");
            Console.WriteLine("       --standalone-tree (independent hidden native tree, isolated roots only)");
            Console.WriteLine("       --tree-host-core (production tree wrapper in an isolated hidden parent)");
            Console.WriteLine("       --tree-icon-render-ab (hidden NamespaceTreeControl default-vs-explicit icon rendering comparison)");
            Console.WriteLine("       --tree-icon-callback-smoke (production Shell icon callback contract; no window or input)");
            Console.WriteLine("       --view-style-capability (native view modes and selection/sort/group preservation; no wheel input)");
            Console.WriteLine("       --view-style-synchronized-hosted (opt-in hidden ExplorerBrowser plus product ShellBrowserSite; syncs standard ListView modes)");
            Console.WriteLine("       --view-data-capability (hidden current-view order, visible columns and cached item properties)");
            Console.WriteLine("       --view-property-refresh-capability (hidden SetViewProperty cache behavior after IShellView.Refresh)");
            Console.WriteLine("       --folder-filter-capability (read-only IFolderFilterSite queries on ExplorerBrowser and IShellView)");
            return args.Length == 0 ? 0 : 2;
        }
        InspectNavigationInterfaces = args[0] == "--navigation-capability";
        var foreground = GetForegroundWindow();
        nint browser = 0, parent = 0;
        bool initialized = false;
        var events = new NavigationProbeEvents();
        if (args[0] == "--folder-header-options-hosted")
            events.InspectView = FolderHeaderOptionsProbe.InspectView;
        uint cookie = 0;
        bool advised = false;
        using var commandProbe = InspectNavigationInterfaces ? new NavigationCommandProbe() : null;
        ShellHistoryEvents? productionHistory = null;
        string? fixture = null;
        var paneSite = args[0] is "--pane-capability" or "--pane-capability-selected" or "--pane-capability-off" or "--pane-same-location"
            ? new PaneProbeSite { Enabled = args[0] != "--pane-capability-off" } : null;
        var shellBrowserSite = args[0] is "--folder-header-options-hosted" or "--view-style-synchronized-hosted" ? new ShellBrowserSite() : null;
        object? browserObject = null;
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref BrowserClass, 0, 1, ref BrowserId, out browser));
            if (args[0] == "--folder-header-options-hosted")
                FolderHeaderOptionsProbe.EnableCustomPosition(browser);
            if (shellBrowserSite is not null)
            {
                browserObject = Marshal.GetObjectForIUnknown(browser);
                ShellBrowserSite.SetSite(browserObject, shellBrowserSite);
            }
            commandProbe?.Subscribe(browser);
            if (commandProbe is not null)
            {
                productionHistory = ShellHistoryEvents.Subscribe(browser, () => { });
                commandProbe.Production = productionHistory;
            }
            if (paneSite is not null)
                Marshal.ThrowExceptionForHR(PaneProbeSite.IUnknown_SetSite(browser, paneSite));
            Probe(browser, "created");
            // WS_POPUP without WS_VISIBLE. No ShowWindow/SetFocus/activation.
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs hidden capability probe",
                0x80000000, 0, 0, 640, 480, 0, 0, 0, 0);
            if (parent == 0) throw new Exception("Hidden parent creation failed.");
            var rect = new Rect { Right = 640, Bottom = 480 };
            var settings = new Settings { Mode = 4 };
            Marshal.ThrowExceptionForHR(Method<Initialize>(browser, 3)(browser, parent, ref rect, ref settings));
            initialized = true;
            // Match the app: no border and no persisted Shell view state.
            Marshal.ThrowExceptionForHR(Method<SetOptions>(browser, 11)(browser, 0x40u | 0x80u | (paneSite is null ? 0u : 2u)));
            Marshal.ThrowExceptionForHR(Method<Advise>(browser, 9)(browser, events, out cookie));
            advised = true;
            Probe(browser, "initialized");
            // A unique fixture avoids invoking handlers for user files
            // and lets concurrent runs keep independent location expectations.
            fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            File.WriteAllBytes(Path.Combine(fixture, "three.txt"), new byte[3]);
            File.WriteAllBytes(Path.Combine(fixture, "five.txt"), new byte[5]);
            File.WriteAllBytes(Path.Combine(fixture, "child", "nested.txt"), new byte[7]);
            Navigate(browser, fixture);
            VerifyLocation(browser, fixture, "first-directory");
            Probe(browser, "first-directory");
            commandProbe?.Check("first-directory", false, false);
            if (args[0] == "--folder-header-options-hosted")
            {
                var secondViewPath = Path.Combine(fixture, "child");
                File.WriteAllBytes(Path.Combine(secondViewPath, "alternate.txt"), new byte[4]);
                File.WriteAllBytes(Path.Combine(secondViewPath, "six.txt"), new byte[6]);
                Navigate(browser, secondViewPath);
                VerifyLocation(browser, secondViewPath, "second-directory");
                if (events.ViewFlagCalls < 2 || events.ViewFlagFailures != 0)
                    throw new Exception("OnViewCreated did not successfully clear the column-header suppression flags.");
                ViewStyleProbe.Run(browser, (mode, phase) => ShellFrameProbe.ReportCurrentViewHeader(browser, $"view-mode-{mode}-{phase}"));
                shellBrowserSite?.ResetCallbackTimings();
                ShellFrameProbe.ProbeHeaderClick(browser);
                Console.WriteLine($"folder-header-options-hosted: direct-notify-callbacks={shellBrowserSite?.CallbackTimingSnapshot}");
                shellBrowserSite?.ResetCallbackTimings();
                ShellFrameProbe.ProbeHeaderMouseMessages(browser);
                Console.WriteLine($"folder-header-options-hosted: header-mouse-callbacks={shellBrowserSite?.CallbackTimingSnapshot}");
                VerifyLocation(browser, secondViewPath, "after-header-flag-view-state-check");
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Folder header option probe changed the hidden desktop boundary.");
                Console.WriteLine($"folder-header-options-hosted: on-view-created-count={events.ViewFlagCalls}; " +
                    "view-flags-applied=True; view-state-check=passed; parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            if (args[0] == "--folder-filter-capability")
            {
                return FolderFilterCapabilityProbe.Run(browser, fixture, parent, foreground);
            }
            if (args[0] == "--selection-commands")
            {
                SelectionCommandProbe.Run(browser);
                VerifyLocation(browser, fixture, "after-selection-commands");
                if (events.Started != 1 || events.Completed != 1 || events.Failed != 0)
                    throw new Exception("Selection commands unexpectedly navigated.");
                var emptyFixture = fixture + "-empty";
                Directory.CreateDirectory(emptyFixture);
                Navigate(browser, emptyFixture);
                VerifyLocation(browser, emptyFixture, "empty-selection-fixture");
                SelectionCommandProbe.CheckEmpty(browser);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Selection probe desktop boundary changed.");
                Console.WriteLine("selection-commands: no-navigation=True; parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            if (args[0] == "--search-folder-navigation")
            {
                SearchFolderProbe.Navigate(browser, fixture, events);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Search probe changed hidden desktop boundary.");
                return 0;
            }
            if (args[0] is "--view-style-capability" or "--view-style-synchronized-hosted")
            {
                ViewStyleProbe.Run(browser, synchronizeListView: args[0] == "--view-style-synchronized-hosted");
                VerifyLocation(browser, fixture, "after-view-styles");
                if (events.Started != 1 || events.Completed != 1 || events.Failed != 0)
                    throw new Exception("View style changes unexpectedly navigated.");
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Hidden view style probe changed desktop boundary.");
                Console.WriteLine("view-styles: no-navigation=True; parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            if (args[0] == "--view-data-capability")
            {
                ViewDataProbe.Run(browser, fixture);
                VerifyLocation(browser, fixture, "after-view-data-probe");
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Hidden view data probe changed the desktop boundary.");
                Console.WriteLine("view-data: parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            if (args[0] == "--view-property-refresh-capability")
            {
                ViewDataProbe.RunRefreshCapability(browser, fixture);
                if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                    throw new Exception("Hidden view-property refresh probe changed desktop boundary.");
                Console.WriteLine("view-property-refresh: parent-visible=False; foreground-unchanged=True");
                return 0;
            }
            if (args[0] == "--navigation-cancel")
            {
                events.CancelNext = true;
                Navigate(browser, Path.Combine(fixture, "child"));
                VerifyLocation(browser, fixture, "cancel-retains-directory");
                Console.WriteLine($"cancel-callbacks: pending={events.Readiness.Pending}; " +
                    $"started={events.Started}; completed={events.Completed}; failed={events.Failed}; vetoed={events.Vetoed}");
                if (events.Vetoed != 1) throw new Exception("Native navigation did not reach the veto callback.");
                if (events.Readiness.Pending || events.Failed != 1 || events.Completed != 1)
                    throw new Exception("Veto did not settle through the expected native failure callback.");
                VerifySelectedSize(browser);
                Navigate(browser, Path.Combine(fixture, "child"));
                VerifyLocation(browser, Path.Combine(fixture, "child"), "after-cancel-navigation");
                CheckReadiness(events, "after-cancel-navigation");
                Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                Pump();
                VerifyLocation(browser, fixture, "after-cancel-back");
                Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                return 0;
            }
            if (paneSite is not null)
            {
                if (args[0] == "--pane-same-location")
                {
                    var child = Path.Combine(fixture, "child");
                    Navigate(browser, child);
                    VerifyLocation(browser, child, "tree-before-same-location");
                    var before = CaptureViewIdentity(browser, selectFirst: true);
                    paneSite.Report("tree-before-same-location");
                    ShellFrameProbe.Report(parent, "tree-before-same-location");
                    paneSite.Enabled = false;
                    // Use documented BrowseToIDList flags only. In particular,
                    // SBSP_WRITENOHISTORY is not in that method's supported list.
                    Navigate(browser, child);
                    VerifyLocation(browser, child, "tree-after-same-location");
                    var after = CaptureViewIdentity(browser, selectFirst: false);
                    paneSite.Report("tree-after-same-location");
                    ShellFrameProbe.Report(parent, "tree-after-same-location");
                    Console.WriteLine($"same-location: hwnd-preserved={before.Window == after.Window}; " +
                        $"mode-preserved={before.Mode == after.Mode}; selection-before={before.Selected}; selection-after={after.Selected}");
                    Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                    Pump();
                    var backReachedParent = MatchesLocation(browser, fixture);
                    Console.WriteLine($"same-location: first-back-reached-parent={backReachedParent}");
                    if (!backReachedParent)
                    {
                        VerifyLocation(browser, child, "same-location-back-duplicate");
                        Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                        Pump();
                    }
                    VerifyLocation(browser, fixture, "same-location-back-parent");
                    Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x8000));
                    Pump();
                    VerifyLocation(browser, child, "same-location-forward");
                    CheckReadiness(events, "same-location");
                    Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                    return 0;
                }
                if (args[0] == "--pane-capability-selected")
                {
                    SelectShellItem(browser, Path.Combine(fixture, "three.txt"));
                    ShellFrameProbe.Report(parent, "frames-after-file-selection");
                    Console.WriteLine($"selected-fixture=True; parent-visible={IsWindowVisible(parent)}; " +
                        $"foreground-unchanged={GetForegroundWindow() == foreground}");
                    return 0;
                }
                paneSite.Report("frames-initial");
                ShellFrameProbe.Report(parent, "frames-initial");
                if (!paneSite.Enabled)
                {
                    Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                    return 0;
                }
                paneSite.Enabled = false;
                Marshal.ThrowExceptionForHR(Method<SetOptions>(browser, 11)(browser, 0xC2));
                Pump();
                paneSite.Report("same-options-after-off");
                ShellFrameProbe.Report(parent, "same-options-after-off");
                RefreshView(browser);
                Pump();
                paneSite.Report("refresh-after-off");
                ShellFrameProbe.Report(parent, "refresh-after-off");
                Marshal.ThrowExceptionForHR(Method<SetOptions>(browser, 11)(browser, 0xC0));
                Pump();
                Marshal.ThrowExceptionForHR(Method<SetOptions>(browser, 11)(browser, 0xC2));
                Pump();
                paneSite.Report("frame-options-cycle");
                ShellFrameProbe.Report(parent, "frame-options-cycle");
                VerifyLocation(browser, fixture, "frame-options-cycle");
                Navigate(browser, Path.Combine(fixture, "child"));
                VerifyLocation(browser, Path.Combine(fixture, "child"), "frames-off-navigation");
                paneSite.Report("frames-off-navigation");
                ShellFrameProbe.Report(parent, "frames-off-navigation");
                Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                Pump();
                VerifyLocation(browser, fixture, "frames-back");
                Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                return 0;
            }
            if (args[0] is "--menu-capability" or "--color-capability")
            {
                if (args[0] == "--color-capability") ShellColorProbe.Report(parent, "before-menu");
                VerifyMenuStates(browser, parent);
                if (args[0] == "--color-capability")
                {
                    Pump();
                    ShellColorProbe.Report(parent, "after-menu");
                    Navigate(browser, Path.Combine(fixture, "child"));
                    VerifyLocation(browser, Path.Combine(fixture, "child"), "after-menu-navigation");
                    ShellColorProbe.Report(parent, "new-view-after-menu");
                }
                Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
                return 0;
            }
            Navigate(browser, Path.Combine(fixture, "child"));
            VerifyLocation(browser, Path.Combine(fixture, "child"), "child-directory");
            Probe(browser, "child-directory");
            commandProbe?.Check("child-directory", true, false);
            Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
            Pump();
            VerifyLocation(browser, fixture, "back");
            ProbeViewTravel(browser, "back");
            commandProbe?.Check("back", false, true);
            Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x8000));
            Pump();
            VerifyLocation(browser, Path.Combine(fixture, "child"), "forward");
            ProbeViewTravel(browser, "forward");
            commandProbe?.Check("forward", true, false);
            CheckReadiness(events, "normal-history");
            Navigate(browser, fixture, wait: false);
            Navigate(browser, Path.Combine(fixture, "child"), wait: false);
            Navigate(browser, fixture, wait: false);
            Pump();
            VerifyLocation(browser, fixture, "rapid-revisit");
            CheckReadiness(events, "rapid-revisit");
            VerifySelectedSize(browser);
            if (commandProbe is not null)
            {
                commandProbe.Check("rapid-revisit", true, false);
                Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                Pump();
                VerifyLocation(browser, Path.Combine(fixture, "child"), "middle-history");
                commandProbe.Check("middle-history", true, true);
                var branch = Path.Combine(fixture, "branch");
                Directory.CreateDirectory(branch);
                var failuresBefore = events.Failed;
                events.CancelNext = true;
                Navigate(browser, branch);
                VerifyLocation(browser, Path.Combine(fixture, "child"), "veto-keeps-history");
                commandProbe.Check("veto-keeps-history", true, true);
                if (events.Failed != failuresBefore + 1 || events.Readiness.Pending)
                    throw new Exception("Cancelled branch did not settle.");
                Navigate(browser, branch);
                VerifyLocation(browser, branch, "new-branch");
                commandProbe.Check("new-branch", true, false);
                Navigate(browser, branch);
                VerifyLocation(browser, branch, "same-path-new-entry");
                commandProbe.Check("same-path-new-entry", true, false);
                Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                Pump();
                VerifyLocation(browser, branch, "same-path-back");
                commandProbe.Check("same-path-back", true, true);
                Marshal.ThrowExceptionForHR(Method<Browse>(browser, 13)(browser, 0, 0x4000));
                Pump();
                VerifyLocation(browser, Path.Combine(fixture, "child"), "past-same-path");
                commandProbe.Check("past-same-path", true, true);
            }
            if (IsWindowVisible(parent) || GetForegroundWindow() != foreground)
                throw new Exception("Hidden navigation probe changed desktop boundary.");
            Console.WriteLine($"parent-visible={IsWindowVisible(parent)}; foreground-unchanged={GetForegroundWindow() == foreground}");
            return 0;
        }
        finally
        {
            try
            {
                try { productionHistory?.Dispose(); }
                finally { commandProbe?.Dispose(); }
            }
            finally
            {
                // A failed connection-point Unadvise must still tear down the
                // native browser and its hidden parent before reporting failure.
                try
                {
                    if (advised) Method<Unadvise>(browser, 10)(browser, cookie);
                    GC.KeepAlive(events);
                    if (initialized) Method<Destroy>(browser, 4)(browser);
                    if (shellBrowserSite is not null && browser != 0)
                        ShellBrowserSite.SetSite(browserObject!, null);
                    GC.KeepAlive(shellBrowserSite);
                    if (paneSite is not null && browser != 0)
                        Marshal.ThrowExceptionForHR(PaneProbeSite.IUnknown_SetSite(browser, null));
                    GC.KeepAlive(paneSite);
                }
                finally
                {
                    try
                    {
                        if (browserObject is not null) Marshal.ReleaseComObject(browserObject);
                        if (browser != 0) Marshal.Release(browser);
                    }
                    finally
                    {
                        if (parent != 0) DestroyWindow(parent);
                        if ((args[0] is "--view-data-capability" or "--view-property-refresh-capability") && fixture is not null)
                            DeleteGeneratedViewDataFixture(fixture);
                    }
                }
            }
        }
    }

    static void DeleteGeneratedViewDataFixture(string fixture)
    {
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var fullPath = Path.GetFullPath(fixture);
        if (!string.Equals(Path.GetDirectoryName(fullPath), fixturesRoot, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _))
            throw new InvalidOperationException("Refusing to remove a probe fixture outside the generated fixture root.");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
    }
    static void CheckReadiness(NavigationProbeEvents events, string stage)
    {
        Console.WriteLine($"{stage}: pending={events.Readiness.Pending}; started={events.Started}; completed={events.Completed}; failed={events.Failed}");
        if (events.Readiness.Pending) throw new Exception("Readiness did not settle after real native callbacks.");
    }

    static (nint Window, uint Mode, int Selected) CaptureViewIdentity(nint browser, bool selectFirst)
    {
        var folderViewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        nint folderView = 0, shellView = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref folderViewId, out folderView));
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref shellViewId, out shellView));
            if (selectFirst)
                Marshal.ThrowExceptionForHR(Method<SelectItem>(folderView, 15)(folderView, 0, 5)); // select + deselect others, no focus
            Marshal.ThrowExceptionForHR(Method<GetWindowHandle>(shellView, 3)(shellView, out var window));
            Marshal.ThrowExceptionForHR(Method<GetMode>(folderView, 3)(folderView, out var mode));
            Marshal.ThrowExceptionForHR(Method<ItemCount>(folderView, 7)(folderView, 1, out var selected));
            return (window, mode, selected);
        }
        finally
        {
            if (shellView != 0) Marshal.Release(shellView);
            if (folderView != 0) Marshal.Release(folderView);
        }
    }

    static bool MatchesLocation(nint browser, string expected)
    {
        nint actual = 0, expectedId = 0;
        try
        {
            actual = ShellCurrentLocation.ReadPidl(browser);
            if (actual == 0) throw new Exception("Current location unavailable.");
            Marshal.ThrowExceptionForHR(SHParseDisplayName(expected, 0, out expectedId, 0, out _));
            return ILIsEqual(actual, expectedId);
        }
        finally
        {
            if (actual != 0) Marshal.FreeCoTaskMem(actual);
            if (expectedId != 0) Marshal.FreeCoTaskMem(expectedId);
        }
    }

    static void RefreshView(nint browser)
    {
        var id = new Guid("000214E3-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref id, out var view));
        try { Marshal.ThrowExceptionForHR(Method<Destroy>(view, 8)(view)); } // IShellView::Refresh
        finally { Marshal.Release(view); }
    }

    static void VerifySelectedSize(nint browser)
    {
        nint view = 0, items = 0;
        try
        {
            var viewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
            var arrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref viewId, out view));
            Marshal.ThrowExceptionForHR(Method<ItemCount>(view, 7)(view, 2, out var count));
            if (count != 3) throw new Exception("Unexpected hidden-view fixture count.");
            Marshal.ThrowExceptionForHR(Method<GetItems>(view, 8)(view, 2, ref arrayId, out items));
            var folderSize = ShellSelectionSize.Read(items, count);
            Marshal.Release(items);
            items = 0;
            if (folderSize != 8) throw new Exception("Hidden view current-folder size mismatch.");
            for (var i = 0; i < count; i++)
                // SVSI_SELECT only. No focus, activation, keyboard, or mouse.
                Marshal.ThrowExceptionForHR(Method<SelectItem>(view, 15)(view, i, 1));
            Marshal.ThrowExceptionForHR(Method<ItemCount>(view, 7)(view, 1, out var selected));
            Marshal.ThrowExceptionForHR(Method<GetItems>(view, 8)(view, 1, ref arrayId, out items));
            var size = ShellSelectionSize.Read(items, selected);
            if (selected != 3 || size != 8) throw new Exception("Hidden view selection size mismatch.");
            Console.WriteLine("hidden-view: current-folder-known-size=8; selected=3; selected-known-size=8; folder-content-excluded=True");
        }
        finally
        {
            if (items != 0) Marshal.Release(items);
            if (view != 0) Marshal.Release(view);
        }
    }

    static void VerifyMenuStates(nint browser, nint owner)
    {
        nint view = 0, shellView = 0;
        try
        {
            var viewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
            var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref viewId, out view));
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref shellViewId, out shellView));
            ReportDropTargetQuery(shellView, "IShellView");
            ReportDropTargetQuery(browser, "ExplorerBrowser");
            Marshal.ThrowExceptionForHR(Method<ItemCount>(view, 7)(view, 2, out var count));
            for (var i = 0; i < count; i++)
                Marshal.ThrowExceptionForHR(Method<SelectItem>(view, 15)(view, i, 0));
            var empty = ShellFileCommands.Query(Open);
            Print("empty-selection", empty);
            if (!empty[ShellFileCommand.NewFolder]) throw new Exception("Writable fixture NewFolder command unavailable.");
            if (ShellFileCommands.All.Any(info => !info.Background && empty[info.Command]))
                throw new Exception("No-selection command unexpectedly enabled.");
            Marshal.ThrowExceptionForHR(Method<SelectItem>(view, 15)(view, 0, 5));
            var selected = ShellFileCommands.Query(Open);
            Print("one-selected-item", selected);
            if (!selected[ShellFileCommand.NewFolder]) throw new Exception("NewFolder must keep using the background with an item selected.");
            foreach (var info in ShellFileCommands.All.Where(info => !info.Background))
                if (!selected[info.Command])
                    throw new Exception($"Expected native selection command unavailable: {info.Command}");
            BackgroundCommandProbe.Run(shellView);

            IShellCommandMenu? Open(bool background)
            {
                Marshal.ThrowExceptionForHR(Method<ItemCount>(view, 7)(view, 1, out var selectedCount));
                if (!background && selectedCount == 0) return null;
                nint context = 0;
                try
                {
                    var id = new Guid("000214E4-0000-0000-C000-000000000046");
                    Marshal.ThrowExceptionForHR(Method<GetItems>(shellView, 15)(
                        shellView, background ? 0u : 1u, ref id, out context));
                    return new NativeShellCommandMenu(Marshal.GetObjectForIUnknown(context), owner);
                }
                finally { if (context != 0) Marshal.Release(context); }
            }
        }
        finally
        {
            if (shellView != 0) Marshal.Release(shellView);
            if (view != 0) Marshal.Release(view);
        }
        static void Print(string label, Dictionary<ShellFileCommand, bool> states) =>
            Console.WriteLine(label + ": " + string.Join("; ", states.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    static void Navigate(nint browser, string path, bool wait = true)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try { Console.WriteLine($"browse={Method<Browse>(browser, 13)(browser, pidl, 0):X8}"); }
        finally { Marshal.FreeCoTaskMem(pidl); }
        if (wait) Pump();
    }
    static void SelectShellItem(nint browser, string path)
    {
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        nint view = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref shellViewId, out view));
            Marshal.ThrowExceptionForHR(Method<SelectShellItemDelegate>(view, 11)(view, pidl, 1)); // SVSI_SELECT; no keyboard focus.
            Pump();
        }
        finally
        {
            if (view != 0) Marshal.Release(view);
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    static void ReportDropTargetQuery(nint source, string label)
    {
        var dropTargetId = new Guid("00000122-0000-0000-C000-000000000046"); // IID_IDropTarget
        var hr = Marshal.QueryInterface(source, in dropTargetId, out var target);
        Console.WriteLine($"drop-target-query {label}=0x{hr:X8}");
        if (target != 0) Marshal.Release(target);
    }
    static void Pump()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
    }
    static void VerifyLocation(nint browser, string expected, string stage)
    {
        var folderViewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        var persistFolderId = new Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
        nint view = 0, folder = 0, actual = 0, expectedId = 0, published = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Method<GetInterface>(browser, 17)(browser, ref folderViewId, out view));
            Marshal.ThrowExceptionForHR(Method<GetInterface>(view, 5)(view, ref persistFolderId, out folder));
            Marshal.ThrowExceptionForHR(Method<GetPidl>(folder, 5)(folder, out actual));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(expected, 0, out expectedId, 0, out _));
            var equal = ILIsEqual(actual, expectedId);
            Console.WriteLine($"{stage}: actual-folder-match={equal}");
            if (!equal) throw new Exception("Native location did not match expected fixture.");
            published = ShellCurrentLocation.ReadPidl(browser);
            if (published == 0 || !ILIsEqual(published, expectedId))
                throw new Exception("Production current-location reader disagrees with actual folder.");
        }
        finally
        {
            if (actual != 0) Marshal.FreeCoTaskMem(actual);
            if (published != 0) Marshal.FreeCoTaskMem(published);
            if (expectedId != 0) Marshal.FreeCoTaskMem(expectedId);
            if (folder != 0) Marshal.Release(folder);
            if (view != 0) Marshal.Release(view);
        }
    }
    static void Probe(nint browser, string stage)
    {
        ProbeViewTravel(browser, stage);
        var hr = Marshal.QueryInterface(browser, in ProviderId, out var provider);
        Console.Write($"{stage}: provider={hr:X8}");
        if (provider == 0) { Console.WriteLine(); return; }
        nint travel = 0;
        try
        {
            hr = Method<QueryService>(provider, 3)(provider, ref TravelId, ref TravelId, out travel);
            Console.Write($"; travel={hr:X8}");
            if (hr == 0 && travel != 0)
                foreach (uint flags in new uint[] { 0x10, 0x20 })
                {
                    var countHr = Method<GetCount>(travel, 7)(travel, flags, out var count);
                    Console.Write($"; count[{flags:X}]={countHr:X8}/{count}");
                }
            Console.WriteLine();
        }
        finally { if (travel != 0) Marshal.Release(travel); Marshal.Release(provider); }
    }
    static void ProbeViewTravel(nint browser, string stage)
    {
        ProbeStateInterfaces(browser, stage + "/browser");
        var viewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var siteId = new Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352");
        nint view = 0, site = 0, provider = 0;
        try
        {
            var hr = Method<GetInterface>(browser, 17)(browser, ref viewId, out view);
            Console.WriteLine($"{stage}/view: get={hr:X8}");
            if (hr != 0 || view == 0) return;
            ProbeStateInterfaces(view, stage + "/view");
            hr = Marshal.QueryInterface(view, in ProviderId, out provider);
            Console.WriteLine($"{stage}/view: provider={hr:X8}");
            if (hr == 0 && provider != 0) ProbeTravelProvider(provider, stage + "/view");
            if (provider != 0) { Marshal.Release(provider); provider = 0; }
            hr = Marshal.QueryInterface(view, in siteId, out site);
            Console.WriteLine($"{stage}/view: object-with-site={hr:X8}");
            if (hr != 0 || site == 0) return;
            // IObjectWithSite::GetSite, after IUnknown and SetSite.
            hr = Method<GetInterface>(site, 4)(site, ref ProviderId, out provider);
            Console.WriteLine($"{stage}/view-site: provider={hr:X8}");
            if (hr == 0 && provider != 0) ProbeTravelProvider(provider, stage + "/view-site");
        }
        finally
        {
            if (provider != 0) Marshal.Release(provider);
            if (site != 0) Marshal.Release(site);
            if (view != 0) Marshal.Release(view);
        }
    }
    static void ProbeTravelProvider(nint provider, string label)
    {
        QueryCounts(provider, label);
        var topBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        nint topProvider = 0;
        try
        {
            var hr = Method<QueryService>(provider, 3)(provider, ref topBrowser, ref ProviderId, out topProvider);
            Console.WriteLine($"{label}/top-browser: provider={hr:X8}");
            if (hr == 0 && topProvider != 0) QueryCounts(topProvider, label + "/top-browser");
        }
        finally { if (topProvider != 0) Marshal.Release(topProvider); }

        static void QueryCounts(nint source, string stage)
        {
            ProbeStateInterfaces(source, stage);
            nint travel = 0;
            try
            {
                // tlogstg.h defines SID_STravelLogCursor as IID_ITravelLogStg.
                var hr = Method<QueryService>(source, 3)(source, ref TravelId, ref TravelId, out travel);
                Console.Write($"{stage}: travel={hr:X8}");
                if (hr == 0 && travel != 0)
                    foreach (uint flags in new uint[] { 0x10, 0x20 })
                    {
                        var countHr = Method<GetCount>(travel, 7)(travel, flags, out var count);
                        Console.Write($"; count[{flags:X}]={countHr:X8}/{count}");
                    }
                Console.WriteLine();
            }
            finally { if (travel != 0) Marshal.Release(travel); }
        }
    }
    static void ProbeStateInterfaces(nint source, string stage)
    {
        if (!InspectNavigationInterfaces) return;
        // These public interfaces belong to different COM contracts. Query
        // support rather than assuming ExplorerBrowser is the WebBrowser control.
        var containerId = new Guid("B196B284-BAB4-101A-B69C-00AA00341D07");
        var eventsId = new Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D");
        var webBrowserId = new Guid("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E");
        nint container = 0, point = 0, travel = 0, webBrowser = 0;
        try
        {
            var travelHr = Marshal.QueryInterface(source, in TravelId, out travel);
            var webHr = Marshal.QueryInterface(source, in webBrowserId, out webBrowser);
            var containerHr = Marshal.QueryInterface(source, in containerId, out container);
            Console.Write($"{stage}/state-interfaces: travel-direct={travelHr:X8}; " +
                $"web-browser={webHr:X8}; connection-container={containerHr:X8}");
            if (travelHr == 0 && travel != 0)
                foreach (uint flags in new uint[] { 0x10, 0x20 })
                {
                    var countHr = Method<GetCount>(travel, 7)(travel, flags, out var count);
                    Console.Write($"; count[{flags:X}]={countHr:X8}/{count}");
                }
            if (containerHr == 0 && container != 0)
            {
                // IConnectionPointContainer: IUnknown, EnumConnectionPoints,
                // FindConnectionPoint. This is discovery only; no event sink.
                var pointHr = Method<GetInterface>(container, 4)(container, ref eventsId, out point);
                Console.Write($"; web-events={pointHr:X8}");
            }
            Console.WriteLine();
        }
        finally
        {
            if (point != 0) Marshal.Release(point);
            if (container != 0) Marshal.Release(container);
            if (webBrowser != 0) Marshal.Release(webBrowser);
            if (travel != 0) Marshal.Release(travel);
        }
    }
    static T Method<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Settings { public uint Mode, Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Initialize(nint self, nint parent, ref Rect rect, ref Settings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Destroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetOptions(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Advise(nint self,
        [MarshalAs(UnmanagedType.Interface)] IProbeBrowserEvents events, out uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Unadvise(nint self, uint cookie);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int QueryService(nint self, ref Guid service, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetCount(nint self, uint flags, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetInterface(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetPidl(nint self, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int ItemCount(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SelectShellItemDelegate(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetWindowHandle(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetMode(nint self, out uint mode);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll")] static extern bool ILIsEqual(nint left, nint right);
}

[ComVisible(true), Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeBrowserEvents
{
    [PreserveSig] int OnNavigationPending(nint pidl);
    [PreserveSig] int OnViewCreated(nint view);
    [PreserveSig] int OnNavigationComplete(nint pidl);
    [PreserveSig] int OnNavigationFailed(nint pidl);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class NavigationProbeEvents : IProbeBrowserEvents
{
    internal Func<nint, bool>? InspectView;
    internal int ViewFlagCalls;
    internal int ViewFlagFailures;
    internal Action<string, nint>? InspectLocation;
    internal ShellViewReadiness Readiness { get; } = new();
    internal int Started, Completed, Failed;
    internal bool CancelNext;
    internal int Vetoed;
    private static string? Identity(nint pidl) =>
        ShellSearchLocation.TryGetParsingName(pidl, out var identity) ? identity : null;
    public int OnNavigationPending(nint pidl)
    {
        InspectLocation?.Invoke("pending", pidl);
        Started++;
        Readiness.Begin(Identity(pidl));
        if (!CancelNext) return 0;
        CancelNext = false;
        Vetoed++;
        return unchecked((int)0x80004004); // Documented host veto: E_ABORT.
    }
    public int OnViewCreated(nint view)
    {
        if (InspectView is null) return 0;
        ViewFlagCalls++;
        try
        {
            if (!InspectView(view)) ViewFlagFailures++;
        }
        catch (Exception ex)
        {
            ViewFlagFailures++;
            Console.WriteLine($"OnViewCreated folder flag probe failed: {ex.Message}");
        }
        return 0;
    }
    public int OnNavigationComplete(nint pidl) { InspectLocation?.Invoke("complete", pidl); Completed++; Readiness.Complete(Identity(pidl)); return 0; }
    public int OnNavigationFailed(nint pidl) { Failed++; Readiness.Fail(Identity(pidl)); return 0; }
}
