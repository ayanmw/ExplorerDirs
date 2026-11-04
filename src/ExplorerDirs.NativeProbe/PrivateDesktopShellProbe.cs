using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerDirs.NativeProbe;
using Microsoft.Win32.SafeHandles;

// Runs focus-sensitive Shell probes on a separate, never-switched Windows Desktop.
// This keeps SetFocus activation inside the probe desktop instead of the user's desktop.
internal static class PrivateDesktopShellProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WctCoGetCallState(int threadId, out uint callState);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WctCoGetActivationState(Guid classId, uint processId, out uint activationState);
    private static WctCoGetCallState? CoGetCallStateCallback;
    private static WctCoGetActivationState? CoGetActivationStateCallback;
    private static string _activeProbeStage = "idle";

    internal static void ReportStage(string stage) => Volatile.Write(ref _activeProbeStage, stage);

    internal static int RunMiniDumpCaptureSmoke(string path)
    {
        try
        {
            CaptureMiniDump(path);
            var dump = File.ReadAllBytes(path);
            if (dump.Length < 32 || !dump.AsSpan(0, 4).SequenceEqual("MDMP"u8))
                throw new InvalidDataException("The captured file does not have a minidump signature/header.");
            var pinnedDump = GCHandle.Alloc(dump, GCHandleType.Pinned);
            try
            {
                if (!MiniDumpReadDumpStream(pinnedDump.AddrOfPinnedObject(), 3, out _, out var threads, out var threadBytes) ||
                    threadBytes < 4 || Marshal.ReadInt32(threads) < 1)
                    throw new InvalidDataException("The minidump has no readable thread list.");
                if (!MiniDumpReadDumpStream(pinnedDump.AddrOfPinnedObject(), 17, out _, out _, out _))
                    throw new InvalidDataException("The minidump has no thread-info stream.");
                Console.WriteLine($"native-minidump-smoke: signature=MDMP; thread-count={Marshal.ReadInt32(threads)}; thread-info=True; bytes={dump.Length}; path={path}");
            }
            finally { pinnedDump.Free(); }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"native-minidump-smoke: failed={exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    internal static int RunHostFilter() => Run("host-filter", ShellViewHostFilterProbe.RunForPrivateDesktop);

    internal static int RunProductSizeDisplay(string adapterPath, bool explorerBrowser = false, bool rootControl = false,
        ExplorerDirs.App.FolderSizeSource source = ExplorerDirs.App.FolderSizeSource.BuiltIn, bool sortPolicyExperiment = false, bool? encodedSortPolicy = null, bool columnStateSortPolicy = false, int encodedSortDirection = 1, bool dynamicSortPolicy = false, bool nativeSortEntrypoints = false, uint nativeSortMode = 4) => Run("product-size-display",
        () => SyntheticShellSortProbe.RunProductSizeDisplay(adapterPath, explorerBrowser, rootControl, source, sortPolicyExperiment, encodedSortPolicy, columnStateSortPolicy, encodedSortDirection, dynamicSortPolicy, nativeSortEntrypoints, nativeSortMode), TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunProductFourPaneHost(string hostProbePath, bool large = false, bool observeNotifications = true, bool forceSortDeadline = false) => Run("product-four-pane-host",
        () => ProductFourPaneHostProbe.Run(hostProbePath, large, observeNotifications, forceSortDeadline), TimeSpan.FromSeconds(large ? 180 : forceSortDeadline ? 90 : 60), initializeOle: true);

    internal static int RunHostMenuCommands() => Run("host-menu-commands", ShellViewHostFilterProbe.RunMenuCommandsForPrivateDesktop);

    internal static int RunHostNewFolder() => Run("host-newfolder", ShellViewHostFilterProbe.RunNewFolderForPrivateDesktop);

    internal static int RunHostRecursiveSizeProjection() => Run("host-recursive-size-projection",
        ShellViewHostFilterProbe.RunRecursiveSizeProjectionForPrivateDesktop);

    internal static int RunHostMultiViewRecursiveSizeProjection() => Run("host-multiview-recursive-size-projection",
        ShellViewHostFilterProbe.RunMultiViewRecursiveSizeProjectionForPrivateDesktop);

    internal static int RunNativeViewLifetime(bool reuseBrowserHost = false) => Run(
        reuseBrowserHost ? "native-view-lifetime-reuse-host" : "native-view-lifetime",
        () => ShellViewNativeLifetimeProbe.Run(reuseBrowserHost), TimeSpan.FromMinutes(2));

    internal static int RunNativeViewObjectOnly() => Run("native-view-object-only",
        ShellViewNativeLifetimeProbe.RunCreateObjectOnly, TimeSpan.FromMinutes(1));

    internal static int RunNativeViewStageTrace() => Run("native-view-stage-trace",
        ShellViewNativeLifetimeProbe.RunViewStageTrace, TimeSpan.FromMinutes(1));

    internal static int RunNativeViewRefreshLifecycle() => Run("native-view-refresh-lifecycle",
        ShellViewRefreshLifecycleProbe.Run, TimeSpan.FromMinutes(1));

    internal static int RunHostFilterVirtualScope(string scope) =>
        Run($"host-filter-{scope}", () => ShellViewHostFilterProbe.RunVirtualScopeForPrivateDesktop(scope));

    internal static int RunColor() => Run("color", () => ShellViewColorSpike.Run(visible: true,
        deferParentCleanup: true), TimeSpan.FromSeconds(60));

    internal static int RunZipColorPixels() => Run("zip-color-pixels", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, virtualZipProviderProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunZipViewBackgroundMenu() => Run("zip-view-background-menu", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, zipBackgroundMenuProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunZipViewExtractCommand() => Run("zip-view-extract-command", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, zipExtractInvokeProbe: true), TimeSpan.FromSeconds(20));

    internal static int RunZipViewCopyItems() => Run("zip-view-copy-items", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, zipCopyItemsProbe: true), TimeSpan.FromSeconds(30));

    internal static int RunZipCrossVolumeMove() => Run("zip-cross-volume-move", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, zipCrossVolumeMoveProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunZipFolderCrossVolumeMove() => Run("zip-folder-cross-volume-move", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, zipFolderCrossVolumeMoveProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunZipViewDeleteItems() => Run("zip-view-delete-items", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, zipDeleteItemsProbe: true), TimeSpan.FromSeconds(30));

    internal static int RunThisPcColorPixels() => Run("this-pc-color-pixels", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, virtualThisPcColorProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunLibrariesColorPixels() => Run("libraries-color-pixels", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, virtualLibrariesColorProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunDesktopColorPixels() => Run("desktop-color-pixels", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, virtualDesktopColorProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunNetworkPlacesRowMap() => Run("network-places-row-map", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, networkPlacesRowMapProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunNetworkPlacesColorPixels() => Run("network-places-color-pixels", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, virtualNetworkPlacesColorProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunNetworkPlacesChildDropTargets() => Run("network-places-child-drop-targets", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, networkPlacesChildDropTargetProbe: true), TimeSpan.FromSeconds(60));

    internal static int RunExplorerBrowserColorNotifications() =>
        Run("explorerbrowser-color-notifications", ExplorerBrowserNotifyProbe.Run, TimeSpan.FromSeconds(25));

    internal static int RunExplorerBrowserWrapperObject() =>
        Run("explorerbrowser-wrapper-object", ExplorerBrowserWrapperProbe.Run, TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunExplorerBrowserRegisteredNamespace() =>
        Run("explorerbrowser-registered-namespace", () => ExplorerBrowserWrapperProbe.RunNamespace(),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunExplorerBrowserVolatileRegisteredNamespace() =>
        Run("explorerbrowser-volatile-registered-namespace", () => ExplorerBrowserWrapperProbe.RunNamespace(volatileRegistryRegistration: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunExplorerBrowserVolatileHiddenNamespace() =>
        Run("explorerbrowser-volatile-hidden-namespace", () => ExplorerBrowserWrapperProbe.RunNamespace(
                volatileRegistryRegistration: true, hideNamespaceRootFromDesktop: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunExplorerBrowserVolatileNonEnumeratedNamespace() =>
        Run("explorerbrowser-volatile-nonenumerated-namespace", () => ExplorerBrowserWrapperProbe.RunNamespace(
                volatileRegistryRegistration: true, nonEnumeratedNamespaceRoot: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunExplorerBrowserNamespaceOwnerExit() =>
        Run("explorerbrowser-namespace-owner-exit", () => ExplorerBrowserWrapperProbe.RunNamespace(
                volatileRegistryRegistration: true, nonEnumeratedNamespaceRoot: true, probeNamespaceOwnerExit: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunShellNamespaceProfileOwners() =>
        Run("shell-namespace-profile-owners", ShellNamespaceOwnerProbe.Run,
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunShellLinkProfileClone() =>
        Run("shell-link-profile-clone", ShellLinkProfileCloneProbe.Run,
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunColumnFilterCapability() =>
        Run("column-filter-capability", ColumnFilterCapabilityProbe.Run, TimeSpan.FromSeconds(35));

    internal static int RunMenuActivation() => Run("menu-activation", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, probeMenuActivation: true));

    internal static int RunShellViewWindowDestroyControl() => Run("shell-view-window-destroy-control",
        () => ShellViewColorSpike.Run(visible: true, deferParentCleanup: true,
            probeWindowDestroyWithShellView: true), TimeSpan.FromSeconds(30));

    internal static int RunSinglePaneSynchronousCleanup() => Run("single-pane-sync-cleanup",
        ShellViewColorSpike.RunSinglePaneSynchronousCleanup, TimeSpan.FromSeconds(35));

    internal static int RunMultiPaneSynchronousCleanup(int paneCount) => Run($"{paneCount}-pane-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(paneCount), TimeSpan.FromSeconds(45));

    internal static int RunFourPaneMenuSynchronousCleanup() => Run("four-pane-menu-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(4, activateMenus: true), TimeSpan.FromSeconds(60));

    internal static int RunSinglePaneMenuSynchronousCleanup() => Run("single-pane-menu-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuNoFocusSynchronousCleanup() => Run("single-pane-menu-nofocus-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            menuActivationState: 1), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuActiveSynchronousCleanup() => Run("single-pane-menu-active-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            deactivateMenus: false), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuDetachedImeSynchronousCleanup() => Run("single-pane-menu-detached-ime-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            detachImeContextBeforeParentDestroy: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuDisabledImeSynchronousCleanup() => Run("single-pane-menu-disabled-ime-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            disableImeForProbeThread: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneMenuOleInitializedSynchronousCleanup() => Run("single-pane-menu-ole-init-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true),
        TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunSinglePaneMenuWpfDispatcherSynchronousCleanup(bool initializeOle = false) => Run(
        initializeOle ? "single-pane-menu-wpf-ole-sync-cleanup" : "single-pane-menu-wpf-dispatcher-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            useWpfDispatcherPump: true), TimeSpan.FromSeconds(45), initializeOle);

    internal static int RunSinglePaneMenuWpfHostSynchronousCleanup() => Run("single-pane-menu-wpf-host-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            useWpfDispatcherPump: true, useWpfHwndSourceParent: true, useWpfHwndHostPane: true,
            restoreParentFocusBeforeDestroy: false),
        TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunFourPaneMenuWpfHostSynchronousCleanup() => Run("four-pane-menu-wpf-host-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(4, activateMenus: true,
            useWpfDispatcherPump: true, useWpfHwndSourceParent: true, useWpfHwndHostPane: true,
            restoreParentFocusBeforeDestroy: false),
        TimeSpan.FromSeconds(60), initializeOle: true);

    internal static int RunFourPaneWpfHostKeyboardConsumer() => Run("four-pane-wpf-host-keyboard-consumer",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(4, activateMenus: true,
            useWpfDispatcherPump: true, useWpfHwndSourceParent: true, useWpfHwndHostPane: true,
            restoreParentFocusBeforeDestroy: false, probeWpfKeyboard: true),
        TimeSpan.FromSeconds(75), initializeOle: true);

    internal static int RunFourPaneWpfHostDirectoryIsolation() => Run("four-pane-wpf-host-directory-isolation",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(4, activateMenus: true,
            useWpfDispatcherPump: true, useWpfHwndSourceParent: true, useWpfHwndHostPane: true,
            restoreParentFocusBeforeDestroy: false, probeWpfKeyboard: true, probeWpfDirectoryIsolation: true),
        TimeSpan.FromSeconds(75), initializeOle: true);

    internal static int RunSinglePaneMenuDetachedViewImeSynchronousCleanup() => Run("single-pane-menu-detached-view-ime-sync-cleanup",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            detachShellViewImeContexts: true), TimeSpan.FromSeconds(45));

    internal static int RunSinglePaneLateCoordinatorDispose() => Run("single-pane-menu-late-coordinator-dispose",
        () => ShellViewColorSpike.RunMultiPaneSynchronousCleanup(1, activateMenus: true,
            disposeMenuCoordinatorBeforeParent: false), TimeSpan.FromSeconds(45));

    internal static int RunSessionRestore(bool synchronousCleanupProbe = false) => Run(
        synchronousCleanupProbe ? "session-restore-sync-cleanup" : "session-restore",
        () => ShellViewColorSpike.Run(visible: true, deferParentCleanup: true, probeSessionRestore: true,
            synchronousCleanupProbe: synchronousCleanupProbe), TimeSpan.FromSeconds(110));

    internal static int RunDropTarget() => Run("drop-target", ShellViewDropTargetProbe.Run);

    internal static int RunDropTargetAfterViewWindowRecreation() =>
        Run("drop-target-after-view-window-recreation", ShellViewDropTargetProbe.RunAfterViewWindowRecreation);

    internal static int RunSessionRestoreConsumer(string fixture, string stateDirectory, Guid sessionId,
        bool synchronousCleanupProbe = false) =>
        Run(synchronousCleanupProbe ? "session-restore-consumer-sync-cleanup" : "session-restore-consumer",
            () => ShellViewColorSpike.RunSessionRestoreConsumer(fixture, stateDirectory, sessionId,
                terminateProcessAfterProbe: !synchronousCleanupProbe,
                synchronousCleanupProbe: synchronousCleanupProbe),
            TimeSpan.FromSeconds(synchronousCleanupProbe ? 55 : 100));

    internal static int RunReadOnlyColorRule() => Run("color-readonly-rule", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, readOnlyRuleProbe: true));

    internal static int RunReadOnlyColorNotificationDiagnostic() => Run("color-readonly-notification",
        () => ShellViewColorSpike.Run(visible: true, deferParentCleanup: true, readOnlyRuleProbe: true,
            readOnlyNotificationDiagnostic: true), TimeSpan.FromSeconds(75));

    internal static int RunReadOnlyColorSynchronousNotificationDiagnostic() => Run(
        "color-readonly-notification-sync",
        () => ShellViewColorSpike.Run(visible: true, deferParentCleanup: true, readOnlyRuleProbe: true,
            readOnlyNotificationDiagnostic: true, readOnlyNotificationSynchronous: true),
        TimeSpan.FromSeconds(40));

    internal static int RunDirectoryColorRule() => Run("color-directory-rule", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, directoryRuleProbe: true));

    internal static int RunHiddenColorRule() => Run("color-hidden-rule", () => ShellViewColorSpike.Run(
        visible: true, deferParentCleanup: true, hiddenRuleProbe: true));

    internal static int RunCommandStateReadOnly() =>
        Run("command-state-readonly", () => ShellViewColorSpike.Run(queryCommandStatesOnly: true));

    internal static int RunStaticShellColumnAdapter(bool strictInterfaces = false, bool prototypeClass = false, bool activateView = false, bool wrappedPidls = false, bool legacyDetailsCallbackProbe = false) =>
        Run(legacyDetailsCallbackProbe ? "static-shell-column-legacy-details-callback" : wrappedPidls ? "static-shell-column-wrapped-adapter" : activateView ? "static-shell-column-adapter-activated-strict" : prototypeClass ? "static-shell-column-adapter-classid" : strictInterfaces ? "static-shell-column-adapter-strict" : "static-shell-column-adapter",
            () => StaticShellFolderAdapterProbe.Run(strictInterfaces, prototypeClass, activateView, wrappedPidls, legacyDetailsCallbackProbe));

    internal static int RunStaticShellItemBoundView() =>
        Run("static-shell-item-bound-view", StaticShellFolderAdapterProbe.RunShellItemBoundView,
            TimeSpan.FromSeconds(90), initializeOle: true);

    internal static int RunSyntheticShellSizeSort() =>
        Run("synthetic-shell-size-sort", () => SyntheticShellSortProbe.Run(), TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunDirectIshellViewNoRegistrationPrototype() =>
        Run("synthetic-direct-ishellview", () => SyntheticShellSortProbe.RunDirectIshellViewNoRegistrationPrototype(),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunDirectIshellViewClassOnlyPrototype() =>
        Run("synthetic-direct-ishellview-class-only",
            () => SyntheticShellSortProbe.RunDirectIshellViewNoRegistrationPrototype(retainClassRegistration: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunDirectIshellViewRunningFactoryPrototype() =>
        Run("synthetic-direct-ishellview-running-factory",
            () => SyntheticShellSortProbe.RunDirectIshellViewNoRegistrationPrototype(registerRunningClassFactory: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunDirectIshellViewVolatileClassPrototype() =>
        Run("synthetic-direct-ishellview-volatile-class",
            () => SyntheticShellSortProbe.RunDirectIshellViewNoRegistrationPrototype(volatileClassRegistration: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunDirectIshellViewNamespaceRootParsePrototype() =>
        Run("synthetic-direct-ishellview-namespace-root-parse",
            () => SyntheticShellSortProbe.RunDirectIshellViewNoRegistrationPrototype(
                volatileClassRegistration: true, parseNamespaceRootWithoutDesktopNamespace: true),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunSyntheticShellTwoViewTargetSync() =>
        Run("synthetic-shell-two-view-target-sync", () => SyntheticShellSortProbe.Run(twoViewDistinctLocations: true), TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunShellNamespaceOwnerCrashRecovery() =>
        Run("shell-namespace-owner-crash-recovery", ShellNamespaceOwnerProbe.RunCrashRecovery,
            TimeSpan.FromSeconds(45), initializeOle: false);

    internal static int RunSyntheticShellPathIdentityTwoView() =>
        Run("synthetic-shell-path-identity-two-view", () => SyntheticShellSortProbe.Run(
            twoViewDistinctLocations: true, twoViewPathIdentityLocations: true),
            TimeSpan.FromSeconds(60), initializeOle: true);

    internal static int RunSyntheticShellFocusRecoveryExperiment() =>
        Run("synthetic-shell-focus-recovery", () => SyntheticShellSortProbe.Run(focusRecoveryExperiment: true), TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSort() =>
        Run("synthetic-recursive-directory-size-sort", () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSort(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithSlowColumn() =>
        Run("synthetic-recursive-directory-size-slow-column", () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithSlowColumn(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithWpfHost() =>
        Run("synthetic-recursive-directory-size-sort-wpf-host", () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithWpfHost(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunRecursiveSizeViewStateRestoreProbe() =>
        Run("recursive-size-view-state-restore",
            () => SyntheticShellSortProbe.RunRecursiveSizeViewStateRestoreProbe(),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunQdrImportedViewStateRestoreProbe(string path, uint? mode = null,
        bool requireDescending = false) =>
        Run("qdr-imported-view-state-restore",
            () => SyntheticShellSortProbe.RunQdrImportedViewStateRestoreProbe(path, mode, requireDescending),
            TimeSpan.FromSeconds(45), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithOnDemandColumnWpfHost() =>
        Run("synthetic-recursive-directory-size-on-demand-column-wpf-host",
            () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithOnDemandColumnWpfHost(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithAsyncOnDemandColumnWpfHost() =>
        Run("synthetic-recursive-directory-size-async-on-demand-column-wpf-host",
            () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithAsyncOnDemandColumnWpfHost(),
            TimeSpan.FromSeconds(90), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeLargeBackgroundRefreshWpfHost() =>
        Run("synthetic-recursive-directory-size-large-background-refresh-wpf-host",
            () => SyntheticShellSortProbe.RunRecursiveDirectorySizeLargeBackgroundRefreshWpfHost(),
            TimeSpan.FromSeconds(90), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithFormattedColumnWpfHost() =>
        Run("synthetic-recursive-directory-size-formatted-column-wpf-host",
            () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithFormattedColumnWpfHost(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithFixedKilobyteColumnWpfHost() =>
        Run("synthetic-recursive-directory-size-fixed-kilobyte-column-wpf-host",
            () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithFixedKilobyteColumnWpfHost(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunSyntheticRecursiveDirectorySizeSortWithGroupedBytesWpfHost() =>
        Run("synthetic-recursive-directory-size-grouped-bytes-wpf-host",
            () => SyntheticShellSortProbe.RunRecursiveDirectorySizeSortWithGroupedBytesWpfHost(),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunRelativePercentColumnMetadata(uint type) =>
        Run($"synthetic-relative-percent-column-type-{type:X}",
            () => SyntheticShellSortProbe.RunRelativePercentColumnMetadata(type),
            TimeSpan.FromSeconds(30), initializeOle: true);

    internal static int RunSyntheticShellFactorySnapshotCache() =>
        Run("synthetic-shell-factory-snapshot-cache", () => SyntheticShellSortProbe.RunFactorySnapshotCacheSmoke(),
            TimeSpan.FromSeconds(20), initializeOle: true);

    internal static int RunCommonListViewColorBaseline() => Run("common-listview-color-baseline",
        CommonListViewColorBaseline.Run);

    internal static int RunDesktopOwnershipSmoke()
    {
        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        var originalForeground = GetForegroundWindow();
        var probeDesktop = CreateProbeDesktop("ExplorerDirs-Ownership-Smoke-" + Guid.NewGuid().ToString("N"));
        var currentDesktop = GetThreadDesktop(GetCurrentThreadId());
        var currentForeground = GetForegroundWindow();
        var closed = CloseDesktop(probeDesktop);
        var passed = originalDesktop != 0 && probeDesktop != 0 && originalDesktop == currentDesktop &&
            originalForeground == currentForeground && closed;
        Console.WriteLine($"private-desktop-owner-smoke: original-thread-desktop=0x{originalDesktop:X}; current-thread-desktop=0x{currentDesktop:X}; foreground-before=0x{originalForeground:X}; foreground-after=0x{currentForeground:X}; close-created-desktop={closed}; passed={passed}");
        return passed ? 0 : 1;
    }

    internal static int RunWindowDestroyControl() => Run("window-destroy-control", () =>
    {
        var foreground = GetForegroundWindow();
        var window = CreateWindowExW(0x00000080u, "STATIC", "ExplorerDirs isolated destroy control",
            0x10CF0000u, 20, 20, 160, 80, 0, 0, 0, 0);
        if (window == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the isolated STATIC control.");

        Console.WriteLine($"window-destroy-control: hwnd=0x{window:X}; class=Static; visible={IsWindowVisible(window)}; foreground-unchanged={GetForegroundWindow() == foreground}");
        var destroyed = DestroyWindow(window);
        var remainsValid = IsWindow(window);
        Console.WriteLine($"window-destroy-control: destroy-returned={destroyed}; still-valid={remainsValid}; foreground-unchanged={GetForegroundWindow() == foreground}");
        return destroyed && !remainsValid && GetForegroundWindow() == foreground ? 0 : 1;
    }, TimeSpan.FromSeconds(10));

    internal static int RunWindowVisibilityControl() => Run("window-visibility-control", () =>
    {
        const uint wsPopup = 0x80000000u;
        const int swShowNoActivate = 4;
        return ProbeWindowVisibilityCase("show-noactivate", wsPopup, showNoActivate: true,
            expectedVisible: true, swShowNoActivate) ? 0 : 1;
    }, TimeSpan.FromSeconds(10));

    private static bool ProbeWindowVisibilityCase(string label, uint style, bool showNoActivate,
        bool expectedVisible, int showCommand = 0)
    {
        var foregroundBefore = GetForegroundWindow();
        var activeBefore = GetActiveWindow();
        Console.WriteLine($"window-visibility-control: case={label}; stage=create-start; style=0x{style:X8}");
        Console.Out.Flush();
        var window = CreateWindowExW(0, "STATIC", $"ExplorerDirs {label} control",
            style, 20, 20, 160, 80, 0, 0, 0, 0);
        if (window == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"Could not create the {label} STATIC control.");
        Console.WriteLine($"window-visibility-control: case={label}; stage=create-complete; hwnd=0x{window:X}");
        Console.Out.Flush();

        try
        {
            if (showNoActivate)
            {
                Console.WriteLine($"window-visibility-control: case={label}; stage=show-noactivate-start; command={showCommand}");
                Console.Out.Flush();
                _ = ShowWindow(window, showCommand);
                Console.WriteLine($"window-visibility-control: case={label}; stage=show-noactivate-complete");
                Console.Out.Flush();
            }

            var visible = IsWindowVisible(window);
            var foregroundAfter = GetForegroundWindow();
            var activeAfter = GetActiveWindow();
            Console.WriteLine($"window-visibility-control: case={label}; stage=destroy-start; visible={visible}; foreground-before=0x{foregroundBefore:X}; foreground-after=0x{foregroundAfter:X}; active-before=0x{activeBefore:X}; active-after=0x{activeAfter:X}");
            Console.Out.Flush();
            var destroyed = DestroyWindow(window);
            var stillValid = IsWindow(window);
            var passed = visible == expectedVisible && destroyed && !stillValid;
            Console.WriteLine($"window-visibility-control: case={label}; stage=destroy-complete; hwnd=0x{window:X}; visible={visible}; expected-visible={expectedVisible}; destroy-returned={destroyed}; still-valid={stillValid}; passed={passed}");
            Console.Out.Flush();
            return passed;
        }
        finally
        {
            if (IsWindow(window)) _ = DestroyWindow(window);
        }
    }

    internal static int RunSharedDesktopWindowDestroyControl() => Run("shared-desktop-window-destroy-control", () =>
    {
        var foreground = GetForegroundWindow();
        var sentinel = CreateWindowExW(0x00000080u, "STATIC", "ExplorerDirs isolated parent sentinel",
            0x10CF0000u, 20, 20, 160, 80, 0, 0, 0, 0);
        if (sentinel == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the parent sentinel.");

        using var child = StartWindowDestroyChild();
        if (!child.WaitForExit(15_000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            Console.Error.WriteLine("shared-desktop-window-destroy-control: child-timeout=True");
            _ = DestroyWindow(sentinel);
            return 124;
        }

        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output)) Console.Write(output);
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.Write(error);
        var parentDestroyed = DestroyWindow(sentinel);
        var passed = child.ExitCode == 0 && parentDestroyed && !IsWindow(sentinel) && GetForegroundWindow() == foreground;
        Console.WriteLine($"shared-desktop-window-destroy-control: child-exit={child.ExitCode}; parent-destroyed={parentDestroyed}; foreground-unchanged={GetForegroundWindow() == foreground}; passed={passed}");
        return passed ? 0 : 1;
    }, TimeSpan.FromSeconds(20));

    private static Process StartWindowDestroyChild()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the NativeProbe executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(PrivateDesktopShellProbe).Assembly.Location);
        start.ArgumentList.Add("--window-destroy-current-desktop-child");
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the window-destroy child.");
    }

    internal static int RunWindowDestroyControlOnCurrentDesktop()
    {
        var foreground = GetForegroundWindow();
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var window = CreateWindowExW(0x00000080u, "STATIC", "ExplorerDirs shared-desktop child control",
            0x10CF0000u, 30, 30, 160, 80, 0, 0, 0, 0);
        if (window == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the child control.");
        Console.WriteLine($"shared-desktop-window-destroy-child: pid={Environment.ProcessId}; tid={GetCurrentThreadId()}; desktop=0x{desktop:X}; hwnd=0x{window:X}; visible={IsWindowVisible(window)}");
        var destroyed = DestroyWindow(window);
        var remainsValid = IsWindow(window);
        var passed = destroyed && !remainsValid && GetForegroundWindow() == foreground;
        Console.WriteLine($"shared-desktop-window-destroy-child: destroy-returned={destroyed}; still-valid={remainsValid}; foreground-unchanged={GetForegroundWindow() == foreground}; passed={passed}");
        return passed ? 0 : 1;
    }

    private static int Run(string kind, Func<int> probe, TimeSpan? timeout = null, bool initializeOle = false)
    {
        var foregroundBefore = GetForegroundWindow();
        var foregroundBeforeDescription = DescribeWindow(foregroundBefore);
        var lastInputBefore = GetLastInputTick();
        Console.WriteLine($"private-desktop-probe: kind={kind}; monitor-thread={GetCurrentThreadId()}; monitor-desktop=0x{GetThreadDesktop(GetCurrentThreadId()):X}; foreground-baseline=[{foregroundBeforeDescription}]; last-input-tick={lastInputBefore}");
        ReportStage($"{kind}: desktop-created");
        var desktopName = "ExplorerDirs-Probe-" + Guid.NewGuid().ToString("N");
        var desktop = CreateProbeDesktop(desktopName);

        var result = 1;
        Exception? failure = null;
        var workerThreadId = 0;
        var probeReturned = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            Interlocked.Exchange(ref workerThreadId, unchecked((int)GetCurrentThreadId()));
            ReportStage($"{kind}: worker-started");
            if (!SetThreadDesktop(desktop))
            {
                ReportStage($"{kind}: set-thread-desktop-failed");
                failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not assign the probe thread to its isolated desktop.");
                probeReturned.Set();
                return;
            }

            ReportStage($"{kind}: thread-on-private-desktop");
            var oleInitialized = false;
            try
            {
                if (initializeOle)
                {
                    var oleResult = OleInitialize(0);
                    Console.WriteLine($"private-desktop-probe: ole-initialize=0x{oleResult:X8}; thread={GetCurrentThreadId()}");
                    if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
                    oleInitialized = true;
                }
                ReportStage($"{kind}: probe-entered");
                result = probe();
            }
            catch (Exception exception)
            {
                ReportStage($"{kind}: probe-threw-{exception.GetType().Name}");
                failure = exception;
            }
            finally
            {
                if (oleInitialized) OleUninitialize();
                // Returning from the Shell probe is enough to report assertions. The STA
                // may stall while tearing down its private Desktop, so process exit owns cleanup.
                ReportStage($"{kind}: probe-returned");
                probeReturned.Set();
            }
        })
        {
            IsBackground = true,
            Name = "ExplorerDirs isolated Shell probe"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        var timeoutLimit = timeout ?? TimeSpan.FromSeconds(25);
        var waitTimer = Stopwatch.StartNew();
        var foregroundChangeObserved = false;
        var inputChangeObserved = false;
        while (!probeReturned.Wait(TimeSpan.FromMilliseconds(25)) && waitTimer.Elapsed < timeoutLimit)
        {
            var currentForeground = GetForegroundWindow();
            if (!foregroundChangeObserved && currentForeground != foregroundBefore)
            {
                foregroundChangeObserved = true;
                Console.Error.WriteLine($"private-desktop-probe: kind={kind}; first-foreground-change=True; stage={Volatile.Read(ref _activeProbeStage)}; elapsed-ms={waitTimer.Elapsed.TotalMilliseconds:F0}; foreground-before=[{foregroundBeforeDescription}]; foreground-now=[{DescribeWindow(currentForeground)}]; last-input-before={lastInputBefore}; last-input-at-change={GetLastInputTick()}");
            }

            var currentLastInput = GetLastInputTick();
            if (!inputChangeObserved && !string.Equals(currentLastInput, lastInputBefore, StringComparison.Ordinal))
            {
                inputChangeObserved = true;
                Console.Error.WriteLine($"private-desktop-probe: kind={kind}; input-change-detected=True; stage={Volatile.Read(ref _activeProbeStage)}; elapsed-ms={waitTimer.Elapsed.TotalMilliseconds:F0}; last-input-before={lastInputBefore}; last-input-now={currentLastInput}");
            }
        }

        if (!probeReturned.IsSet)
        {
            var foregroundAtTimeout = GetForegroundWindow();
            var threadId = unchecked((uint)Volatile.Read(ref workerThreadId));
            Console.Error.WriteLine($"private-desktop-probe: kind={kind}; timeout=True; worker-tid={threadId}; stage={Volatile.Read(ref _activeProbeStage)}; foreground-change-observed={foregroundChangeObserved}; foreground-before=[{foregroundBeforeDescription}]; foreground-at-timeout=[{DescribeWindow(foregroundAtTimeout)}]; last-input-before={lastInputBefore}; last-input-at-timeout={GetLastInputTick()}; probe did not return before the deadline");
            CaptureTimeoutDumpIfRequested();
            CaptureWaitChain(threadId);
            return 124;
        }

        var foregroundAfter = GetForegroundWindow();
        if (!foregroundChangeObserved && foregroundAfter != foregroundBefore)
        {
            foregroundChangeObserved = true;
            Console.Error.WriteLine($"private-desktop-probe: kind={kind}; foreground-change-detected-at-completion=True; stage={Volatile.Read(ref _activeProbeStage)}; elapsed-ms={waitTimer.Elapsed.TotalMilliseconds:F0}; foreground-before=[{foregroundBeforeDescription}]; foreground-after=[{DescribeWindow(foregroundAfter)}]; last-input-before={lastInputBefore}; last-input-at-completion={GetLastInputTick()}");
        }
        var foregroundUnchanged = foregroundBefore == foregroundAfter;
        var lastInputAtReturn = GetLastInputTick();
        inputChangeObserved |= !string.Equals(lastInputAtReturn, lastInputBefore, StringComparison.Ordinal);
        var isolationClean = PrivateDesktopIsolationPolicy.IsClean(foregroundChangeObserved,
            foregroundUnchanged, inputChangeObserved);
        Console.WriteLine($"private-desktop-probe: kind={kind}; name={desktopName}; stage-at-return={Volatile.Read(ref _activeProbeStage)}; foreground-change-observed={foregroundChangeObserved}; foreground-before=[{foregroundBeforeDescription}]; foreground-after=[{DescribeWindow(foregroundAfter)}]; last-input-before={lastInputBefore}; last-input-at-return={lastInputAtReturn}; input-change-observed={inputChangeObserved}; foreground-unchanged={foregroundUnchanged}; isolation-clean={isolationClean}; process-exit-owns-desktop-cleanup=True");

        if (failure is not null)
        {
            Console.Error.WriteLine($"private-desktop-probe failure ({kind}): " + failure);
            return 1;
        }

        if (!isolationClean)
            return 1;

        return result;
    }

    private static string DescribeWindow(nint window)
    {
        if (window == 0) return "hwnd=0";
        var threadId = GetWindowThreadProcessId(window, out var processId);
        var className = new StringBuilder(256);
        var classLength = GetClassNameW(window, className, className.Capacity);
        var processName = "unavailable";
        if (processId != 0)
        {
            try { processName = Process.GetProcessById(unchecked((int)processId)).ProcessName; }
            catch (Exception exception) { processName = "unavailable-" + exception.GetType().Name; }
        }
        return $"hwnd=0x{window:X};pid={processId};tid={threadId};process={processName};class={(classLength > 0 ? className.ToString() : "unavailable")}";
    }

    private static string GetLastInputTick()
    {
        var info = new LastInputInfo { Size = unchecked((uint)Marshal.SizeOf<LastInputInfo>()) };
        return GetLastInputInfo(ref info) ? info.Time.ToString() : "unavailable";
    }

    private static void CaptureTimeoutDumpIfRequested()
    {
        var path = Environment.GetEnvironmentVariable("EXPLORER_DIRS_NATIVEPROBE_TIMEOUT_DUMP_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            CaptureMiniDump(path);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"native-minidump: failed={exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void CaptureMiniDump(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("A minidump output path must be absolute.", nameof(path));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        const uint miniDumpWithThreadInfo = 0x00001000;
        if (!MiniDumpWriteDump(GetCurrentProcess(), unchecked((uint)Environment.ProcessId),
                stream.SafeFileHandle, miniDumpWithThreadInfo, 0, 0, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "MiniDumpWriteDump failed.");
        stream.Flush(flushToDisk: true);
        Console.Error.WriteLine($"native-minidump: captured=True; bytes={stream.Length}; path={path}");
    }

    private static void CaptureWaitChain(uint threadId)
    {
        if (threadId == 0)
        {
            Console.Error.WriteLine("wait-chain: worker thread id unavailable");
            return;
        }

        var session = OpenThreadWaitChainSession(0, 0);
        if (session == 0)
        {
            Console.Error.WriteLine($"wait-chain: OpenThreadWaitChainSession failed; win32={Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            RegisterWaitChainCOMCallback(GetComCallStateCallback(), GetComActivationStateCallback());
            const uint nodeCapacity = 16;
            var nodes = new WaitChainNode[nodeCapacity];
            var count = nodeCapacity;
            var flags = 0x1u | 0x2u | 0x4u; // Include out-of-process, COM, and critical-section owners.
            if (!GetThreadWaitChain(session, 0, flags, threadId, ref count, nodes, out var cycle))
            {
                Console.Error.WriteLine($"wait-chain: query failed; tid={threadId}; win32={Marshal.GetLastWin32Error()}");
                return;
            }

            Console.Error.WriteLine($"wait-chain: tid={threadId}; nodes={count}; cycle={cycle}");
            for (var index = 0; index < Math.Min(count, nodeCapacity); index++)
            {
                var node = nodes[index];
                if (node.ObjectType == 8) // WctThreadType, from the Windows SDK wct.h enum.
                {
                    Console.Error.WriteLine($"wait-chain: [{index}] type={WaitChainObjectType(node.ObjectType)} status={node.ObjectStatus}; pid={node.ProcessId}; tid={node.ThreadId}; switches={node.ContextSwitches}");
                    DescribeWaitChainThread(node.ProcessId, node.ThreadId);
                }
                else
                    Console.Error.WriteLine($"wait-chain: [{index}] type={WaitChainObjectType(node.ObjectType)} status={node.ObjectStatus}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"wait-chain: capture failed; {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            CloseThreadWaitChainSession(session);
        }
    }

    private static void DescribeWaitChainThread(uint processId, uint threadId)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            if (processId == Environment.ProcessId)
            {
                var thread = process.Threads.Cast<ProcessThread>()
                    .FirstOrDefault(candidate => candidate.Id == unchecked((int)threadId));
                Console.Error.WriteLine(thread is null
                    ? $"wait-chain-thread: pid={processId}; tid={threadId}; state=not-found"
                    : $"wait-chain-thread: pid={processId}; tid={threadId}; state={thread.ThreadState}; wait-reason={(thread.ThreadState == System.Diagnostics.ThreadState.Wait ? thread.WaitReason.ToString() : "n/a")}");
            }
            else
            {
                Console.Error.WriteLine($"wait-chain-process: pid={processId}; name={process.ProcessName}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"wait-chain-owner: pid={processId}; tid={threadId}; lookup={exception.GetType().Name}");
        }
    }

    private static string WaitChainObjectType(uint type) => type switch
    {
        1 => "CriticalSection",
        2 => "SendMessage",
        3 => "Mutex",
        4 => "ALPC",
        5 => "COM",
        6 => "ThreadWait",
        7 => "ProcessWait",
        8 => "Thread",
        9 => "COMActivation",
        10 => "Unknown",
        11 => "SocketIO",
        12 => "SMBIO",
        _ => $"Unknown({type})"
    };

    private static WctCoGetCallState GetComCallStateCallback()
    {
        return CoGetCallStateCallback ??= Marshal.GetDelegateForFunctionPointer<WctCoGetCallState>(
            GetProcAddress(GetModuleHandleW("ole32.dll"), "CoGetCallState"));
    }

    private static WctCoGetActivationState GetComActivationStateCallback()
    {
        return CoGetActivationStateCallback ??= Marshal.GetDelegateForFunctionPointer<WctCoGetActivationState>(
            GetProcAddress(GetModuleHandleW("ole32.dll"), "CoGetActivationState"));
    }

    private static nint CreateProbeDesktop(string name)
    {
        nint desktop = 0;
        var error = 0;
        var creator = new Thread(() =>
        {
            desktop = CreateDesktopW(name, null, 0, 0, 0x01FF, 0);
            if (desktop == 0) error = Marshal.GetLastWin32Error();
        }) { IsBackground = true, Name = "ExplorerDirs private Desktop creator" };
        creator.Start();
        creator.Join();
        if (desktop == 0)
            throw new System.ComponentModel.Win32Exception(error, "Could not create the isolated probe desktop.");
        return desktop;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string desktop, string? device, nint devmode, uint flags, uint desiredAccess, nint security);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(nint desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint Time;
    }

    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("dbghelp.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(nint process, uint processId, SafeFileHandle file,
        uint dumpType, nint exceptionInformation, nint userStreamInformation, nint callbackInformation);

    [DllImport("dbghelp.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpReadDumpStream(nint baseOfDump, uint streamNumber,
        out nint directory, out nint streamPointer, out uint streamSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string procedureName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    [StructLayout(LayoutKind.Explicit, Size = 280)]
    private struct WaitChainNode
    {
        [FieldOffset(0)] public uint ObjectType;
        [FieldOffset(4)] public uint ObjectStatus;
        [FieldOffset(8)] public uint ProcessId;
        [FieldOffset(12)] public uint ThreadId;
        [FieldOffset(16)] public uint WaitTime;
        [FieldOffset(20)] public uint ContextSwitches;
    }

    [DllImport("advapi32.dll")]
    private static extern nint OpenThreadWaitChainSession(uint flags, nint callback);

    [DllImport("advapi32.dll")]
    private static extern void CloseThreadWaitChainSession(nint session);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadWaitChain(nint session, nuint context, uint flags, uint threadId,
        ref uint nodeCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] WaitChainNode[] nodeInfo,
        [MarshalAs(UnmanagedType.Bool)] out bool isCycle);

    [DllImport("advapi32.dll")]
    private static extern void RegisterWaitChainCOMCallback(WctCoGetCallState callStateCallback,
        WctCoGetActivationState activationStateCallback);
}
