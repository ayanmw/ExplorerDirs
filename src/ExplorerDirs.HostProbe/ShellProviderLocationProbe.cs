using Microsoft.Win32;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

// Exercises the production ExplorerBrowserHost against a disposable Shell folder provider.
internal static class ShellProviderLocationProbe
{
    internal static int RunProductAdapterOnDemandAsync(string sortStrategy = "scheduled")
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProductAsyncHost-" + Guid.NewGuid().ToString("N"));
        var fallbackRoot = Path.Combine(root, "fallback-root");
        var viewRoot = Path.Combine(root, "view-root");
        var smallFolder = Path.Combine(viewRoot, "folder-small");
        var largeFolder = Path.Combine(viewRoot, "folder-large");
        var notificationBurst = sortStrategy.StartsWith("notification-burst", StringComparison.Ordinal);
        var notificationDirectoryLarge = sortStrategy == "notification-updatedir-large";
        var largeSortReadiness = sortStrategy == "large-readiness";
        var notificationRedrawSuppressedSort = sortStrategy == "notification-updatedir-paired-sort-redraw";
        var notificationPairedSort = sortStrategy is "notification-updatedir-paired-sort" or "notification-updatedir-paired-sort-redraw";
        var notificationFollowupSort = sortStrategy is "notification-updatedir-sort" or "notification-updatedir-paired-sort" or "notification-updatedir-paired-sort-redraw";
        var notificationDirectory = sortStrategy is "notification-updatedir" or "notification-updatedir-large" or "notification-updatedir-sort" or "notification-updatedir-paired-sort" or "notification-updatedir-paired-sort-redraw";
        var notificationItems = sortStrategy == "notification-items-flush";
        var flushNotifications = sortStrategy is "notification-burst-flush" or "notification-items-flush";
        var notificationDelayMilliseconds = sortStrategy == "notification-burst-spaced" ? 150 :
            notificationDirectory || notificationItems ? 5000 : 0;
        var extraFolderCount = notificationBurst ? 14 : notificationDirectoryLarge ? 100 : largeSortReadiness ? 1000 : 0;
        var expectedFolderCount = 2 + extraFolderCount;
        var expectedSizeSortedNames = new[] { "folder-small", "folder-large" }
            .Concat((largeSortReadiness
                ? Enumerable.Range(0, extraFolderCount).Reverse()
                : Enumerable.Range(0, extraFolderCount)).Select(index => $"folder-extra-{index:D2}"))
            .ToArray();
        var expectedRecursiveValues = extraFolderCount > 0
            ? Enumerable.Range(101, extraFolderCount).Select(value => (ulong)value).Append(5UL).Append(29UL).Order().ToArray()
            : new[] { 5UL, 29UL };
        var expectedItemPaths = new HashSet<string>([smallFolder, largeFolder], StringComparer.OrdinalIgnoreCase);
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        nint adapter = 0;
        var classRegistered = false;
        var namespaceRegistered = false;
        var stage = "fixture";
        HwndSource? source = null;
        HwndSource? notificationSource = null;
        ShellItemUpdateObserver? itemUpdateObserver = null;
        ShellItemUpdateObserver? filesystemUpdateObserver = null;
        ShellItemUpdateObserver? desktopUpdateObserver = null;
        HwndHost? host = null;
        NativePaintMessageProbe? initialVirtualSortPaintProbe = null;
        var foregroundBefore = GetForegroundWindow();
        try
        {
            Directory.CreateDirectory(Path.Combine(fallbackRoot, "fallback-only"));
            Directory.CreateDirectory(smallFolder);
            Directory.CreateDirectory(largeFolder);
            File.WriteAllBytes(Path.Combine(smallFolder, "payload.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(largeFolder, "payload.bin"), new byte[29]);
            if (extraFolderCount > 0)
            {
                for (var index = 0; index < extraFolderCount; index++)
                {
                    var path = Path.Combine(viewRoot, $"folder-extra-{index:D2}");
                    Directory.CreateDirectory(path);
                    var payloadSize = largeSortReadiness ? extraFolderCount - index + 100 : index + 101;
                    File.WriteAllBytes(Path.Combine(path, "payload.bin"), new byte[payloadSize]);
                    expectedItemPaths.Add(path);
                }
            }

            var adapterPath = FindProductAdapterLibrary();
            if (largeSortReadiness)
                Console.WriteLine($"shell-provider-product-async-large-readiness-adapter: path={adapterPath}");
            stage = "load-product-adapter";
            adapter = NativeLibrary.Load(adapterPath);
            if (largeSortReadiness && NativeLibrary.TryGetExport(adapter, "SetProductCompareTraceEnabled", out var compareTraceSwitch))
                Marshal.GetDelegateForFunctionPointer<SetFlag>(compareTraceSwitch)(0);
            var configure = GetExport<ConfigureShellNamespaceRoot>(adapter, "ConfigureShellNamespaceRoot");
            Check(configure(ref classId, fallbackRoot));
            if ((notificationBurst || notificationDirectory || notificationItems) && NativeLibrary.TryGetExport(adapter, "SetProductViewNotificationsUseSyntheticRoot", out var notificationRootAddress))
                Marshal.GetDelegateForFunctionPointer<SetProductViewNotificationsUseSyntheticRoot>(notificationRootAddress)(1);
            if ((notificationBurst || notificationDirectory || notificationItems) && NativeLibrary.TryGetExport(adapter, "ConfigureProductNotificationDeliveryForProbe", out var notificationDeliveryAddress))
                Marshal.GetDelegateForFunctionPointer<ConfigureProductNotificationDeliveryForProbe>(notificationDeliveryAddress)(flushNotifications || notificationDirectory ? 1 : 0, (uint)notificationDelayMilliseconds);
            if (notificationDirectory && NativeLibrary.TryGetExport(adapter, "SetProductAsyncItemNotificationEnabledForProbe", out var itemNotificationModeAddress))
                Marshal.GetDelegateForFunctionPointer<SetProductAsyncItemNotificationEnabledForProbe>(itemNotificationModeAddress)(0);
            RegisterVolatileClass(classKeyPath, adapterPath);
            classRegistered = true;
            RegisterVolatileNamespace(namespaceKeyPath);
            namespaceRegistered = true;

            stage = "cross-process-provider-enumeration";
            var directProviderCount = LaunchProviderEnumerationChild(classId, adapterPath, fallbackRoot, viewRoot);
            if (directProviderCount != expectedFolderCount)
                throw new InvalidOperationException($"The isolated provider enumerated {directProviderCount} folders; expected {expectedFolderCount}.");

            stage = "construct-view-state";
            var targetIdentity = $"::{{{classId:D}}}\\edpath:{EncodePath(viewRoot)}";
            var expectedUpdatedNames = new HashSet<string>(expectedItemPaths.Select(path => Path.GetFileName(path)!), StringComparer.OrdinalIgnoreCase);
            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var propertyKeyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var sortColumnType = appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!;
            var displayColumnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var recursiveSortKey = Activator.CreateInstance(propertyKeyType,
                Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u)!;
            var sorts = Array.CreateInstance(sortColumnType, 1);
            sorts.SetValue(Activator.CreateInstance(sortColumnType, [recursiveSortKey, 1]), 0);
            var displayColumns = Array.CreateInstance(displayColumnType, 2);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [recursiveSortKey, 180]), 0);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType,
                [Activator.CreateInstance(propertyKeyType, new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10u)!, 240]), 1);
            var viewStateConstructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == 6);
            var viewState = viewStateConstructor.Invoke([4u, -1, sorts, null, true, displayColumns]);

            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            host = (HwndHost)Activator.CreateInstance(hostType, targetIdentity, viewState)!;
            var viewHeight = notificationBurst ? 1000 : 360;
            var grid = new Grid { Width = 640, Height = viewHeight };
            grid.Children.Add(host);
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs product async HostProbe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 640, Height = viewHeight
            });
            itemUpdateObserver = new ShellItemUpdateObserver(source, targetIdentity);
            filesystemUpdateObserver = new ShellItemUpdateObserver(source, viewRoot);
            notificationSource = new HwndSource(new HwndSourceParameters("ExplorerDirs Shell notification observer")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 1, Height = 1
            });
            desktopUpdateObserver = ShellItemUpdateObserver.ForDesktop(notificationSource);
            if (IsWindowVisible(notificationSource.Handle))
                throw new InvalidOperationException("The independent Shell notification observer window became visible.");
            source.RootVisual = grid;
            grid.Measure(new Size(640, 360));
            grid.Arrange(new Rect(0, 0, 640, 360));
            grid.UpdateLayout();
            if (source.Handle == 0 || IsWindowVisible(source.Handle))
                throw new InvalidOperationException("The product Shell HostProbe parent window became visible.");

            stage = "wait-for-navigation";
            var navigatedField = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindowField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WaitUntil(() => Equals(navigatedField.GetValue(host), true) && (nint)viewWindowField.GetValue(host)! != 0,
                TimeSpan.FromSeconds(15), "the product Shell view did not navigate to the temporary registered root");
            stage = "wait-for-enumeration";
            var initialListViewCounts = new HashSet<int>();
            try
            {
                WaitUntil(() =>
                {
                    initialListViewCounts.Add(ReadNativeListViewItemCount((nint)viewWindowField.GetValue(host)!));
                    var counts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(host, [false, false]);
                    return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == expectedFolderCount;
                }, TimeSpan.FromSeconds(15), $"the product Shell view did not enumerate all {expectedFolderCount} test folders");
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException($"{exception.Message}; native-listview-item-counts=[{string.Join('|', initialListViewCounts.Order())}]", exception);
            }
            if (largeSortReadiness)
                initialVirtualSortPaintProbe = new NativePaintMessageProbe(FindNativeListView((nint)viewWindowField.GetValue(host)!));

            string[] largeSortSelectedBefore = [];
            string? largeSortListViewFocusBefore = null;
            string? largeSortShellFocusBefore = null;
            if (largeSortReadiness)
            {
                var selectionTargets = new[] { "folder-extra-500", "folder-extra-501", "folder-extra-502" };
                SelectShellViewItemsByPath(hostType, host,
                    selectionTargets.Select(name => Path.Combine(viewRoot, name)).ToArray());
                var selection = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!);
                largeSortSelectedBefore = selection.Selected.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                largeSortListViewFocusBefore = selection.Focused;
                largeSortShellFocusBefore = ReadShellFocusedRow(hostType, host);
                if (!largeSortSelectedBefore.SequenceEqual(selectionTargets.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(largeSortListViewFocusBefore, "folder-extra-500", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(largeSortShellFocusBefore, "folder-extra-500", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Could not establish the initial-sort selection/focus: selected=[{string.Join('|', largeSortSelectedBefore)}]; listview-focus={largeSortListViewFocusBefore}; shell-focus={largeSortShellFocusBefore}.");
            }

            string[] selectedBeforeNotification = [];
            string? focusedRowBeforeNotification = null;
            string? shellFocusedRowBeforeNotification = null;
            if (notificationDirectory)
            {
                var selectionTargetName = notificationDirectoryLarge ? "folder-extra-00" : "folder-small";
                SelectShellViewItemByPath(hostType, host, Path.Combine(viewRoot, selectionTargetName));
                var selectionStateBefore = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!);
                selectedBeforeNotification = selectionStateBefore.Selected;
                focusedRowBeforeNotification = selectionStateBefore.Focused;
                shellFocusedRowBeforeNotification = ReadShellFocusedRow(hostType, host);
                if (!selectedBeforeNotification.SequenceEqual([selectionTargetName], StringComparer.OrdinalIgnoreCase) ||
                    !string.Equals(focusedRowBeforeNotification, selectionTargetName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(shellFocusedRowBeforeNotification, selectionTargetName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Could not establish the notification probe selection/focus: selected=[{string.Join('|', selectedBeforeNotification)}]; listview-focused-row={focusedRowBeforeNotification}; shell-focused-row={shellFocusedRowBeforeNotification}.");
            }

            stage = "async-values";
            ResetProductCompareDiagnostics(adapter);
            var readValues = typeof(ShellProviderLocationProbe).GetMethod("ReadRecursiveValues",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            string[] values = [];
            WaitUntil(() =>
            {
                values = (string[])readValues.Invoke(null, [hostType, host, expectedFolderCount])!;
                return values.Length == expectedFolderCount && values.Select(value =>
                    TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue)
                    .Order().SequenceEqual(expectedRecursiveValues);
            }, TimeSpan.FromSeconds(20), $"the product Shell view did not fill all {expectedFolderCount} recursive values asynchronously");
            var compareDiagnosticsAfterAsyncFill = largeSortReadiness ? [] : ReadProductCompareDiagnostics(adapter);
            if (compareDiagnosticsAfterAsyncFill.Length > 0)
                Console.WriteLine($"shell-provider-product-async-compare-trace: phase=async-fill; count={compareDiagnosticsAfterAsyncFill.Length}; records=[{string.Join(" || ", compareDiagnosticsAfterAsyncFill)}]");

            var readinessCheck = hostType.GetMethod("HasPendingUnresolvedColumnValues", BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, [sorts.GetValue(0)!.GetType(), typeof(Func<bool>)], modifiers: null)!;
            var readinessClock = Stopwatch.StartNew();
            var pendingValues = (bool)readinessCheck.Invoke(host, [sorts.GetValue(0), new Func<bool>(() => true)])!;
            readinessClock.Stop();
            if (largeSortReadiness && pendingValues)
                throw new InvalidOperationException("The production readiness check still reported pending values after all recursive values were observed.");
            var readinessCapture = appAssembly.GetType("ExplorerDirs.App.ShellFileViewExportCapture", throwOnError: true)!
                .GetMethod("CreateReadinessSession", BindingFlags.Static | BindingFlags.NonPublic)!;
            var folderViewPointer = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host, null)!;
            object? readinessSession;
            int capturedReadinessItemCount;
            try
            {
                var captureArguments = new object?[]
                {
                    folderViewPointer,
                    (nint)viewWindowField.GetValue(host)!,
                    sorts.GetValue(0)!.GetType().GetProperty("Key")!.GetValue(sorts.GetValue(0)),
                    0
                };
                readinessSession = readinessCapture.Invoke(null, captureArguments);
                capturedReadinessItemCount = (int)captureArguments[3]!;
            }
            finally { Marshal.Release(folderViewPointer); }
            if (readinessSession is not IDisposable disposableReadinessSession || capturedReadinessItemCount != expectedFolderCount)
                throw new InvalidOperationException("The large-view readiness session did not capture the current Shell row context once.");
            var sessionReadinessCheck = readinessSession.GetType().GetMethod("HasPendingUnresolvedValues",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var batchReadinessClock = Stopwatch.StartNew();
            var maximumBatchMilliseconds = 0d;
            var readinessBatchCount = 0;
            var readinessBatchSize = (int)appAssembly.GetType("ExplorerDirs.App.ShellFileViewExportCapture", throwOnError: true)!
                .GetField("ReadinessBatchSize", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            try
            {
                for (var startIndex = 0; startIndex < expectedFolderCount; startIndex += readinessBatchSize)
                {
                    var batchClock = Stopwatch.StartNew();
                    var arguments = new object?[] { startIndex, readinessBatchSize, 0 };
                    _ = (bool)sessionReadinessCheck.Invoke(disposableReadinessSession, arguments)!;
                    if ((int)arguments[2]! != capturedReadinessItemCount)
                        throw new InvalidOperationException("The Shell item count changed during the captured readiness pass.");
                    batchClock.Stop();
                    maximumBatchMilliseconds = Math.Max(maximumBatchMilliseconds, batchClock.Elapsed.TotalMilliseconds);
                    readinessBatchCount++;
                }
            }
            finally { disposableReadinessSession.Dispose(); }
            batchReadinessClock.Stop();

            var nativeCellsBeforeNotification = notificationDirectory || notificationItems
                ? ReadNativeListViewCells((nint)viewWindowField.GetValue(host)!, 3)
                : [];
            var sortColumnsBeforeNotification = notificationDirectory ? CaptureSortColumns(hostType, host) : [];

            if (sortStrategy == "scheduled" || notificationBurst || notificationDirectory || notificationItems)
            {
                stage = "item-update-delivery";
                if (notificationDirectory) ResetProductCompareDiagnostics(adapter);
                var notificationDeliverySamples = new List<string>();
                var notificationShellItemCounts = new HashSet<int>();
                var notificationNativeListViewCounts = new HashSet<int>();
                var notificationStateSamples = new Dictionary<int, HashSet<string>>();
                if (notificationBurst)
                {
                    var deliveryClock = Stopwatch.StartNew();
                    var nextSample = 5;
                    while (deliveryClock.Elapsed < TimeSpan.FromSeconds(30) &&
                           !expectedUpdatedNames.SetEquals(desktopUpdateObserver.UpdatedItemNames))
                    {
                        PumpDispatcher(TimeSpan.FromMilliseconds(20));
                        if (deliveryClock.Elapsed.TotalSeconds >= nextSample)
                        {
                            notificationDeliverySamples.Add($"{nextSample}s={desktopUpdateObserver.UpdatedItemNames.Length}");
                            nextSample += 5;
                        }
                    }
                }
                else
                {
                    try
                    {
                        WaitUntil(() =>
                        {
                            if (notificationDirectory || notificationItems)
                            {
                                var counts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!
                                    .Invoke(host, [false, false]);
                                if (counts is not null)
                                    notificationShellItemCounts.Add((int)counts.GetType().GetProperty("Total")!.GetValue(counts)!);
                                var shellCount = counts is null ? -1 : (int)counts.GetType().GetProperty("Total")!.GetValue(counts)!;
                                var nativeCount = ReadNativeListViewItemCount((nint)viewWindowField.GetValue(host)!);
                                notificationNativeListViewCounts.Add(nativeCount);
                                var attemptCounter = notificationDirectory
                                    ? "GetProductAsyncDirectoryNotificationAttempts"
                                    : "GetProductAsyncItemNotificationAttempts";
                                var sendsStarted = int.TryParse(ReadProductDiagnosticCounter(adapter, attemptCounter), out var attempts)
                                    ? attempts : -1;
                                if (!notificationStateSamples.TryGetValue(sendsStarted, out var samples))
                                    notificationStateSamples[sendsStarted] = samples = [];
                                samples.Add($"{shellCount}/{nativeCount}");
                            }
                            return notificationDirectory
                                ? desktopUpdateObserver.EventCount(0x00001000) > 0
                                : expectedUpdatedNames.SetEquals(desktopUpdateObserver.UpdatedItemNames);
                        },
                            TimeSpan.FromSeconds(15), "the Desktop-scope Shell observer did not receive one item update for every completed folder");
                    }
                    catch (TimeoutException) { }
                }
                var sentNotificationIdentities = ReadProductDiagnosticItemIdentities(adapter);
                var viewCallbackIdentities = ReadProductDiagnosticIdentities(adapter,
                    "GetProductViewItemNotificationIdentityCount", "GetProductViewItemNotificationIdentity");
                var sentNotificationNames = sentNotificationIdentities.Select(GetProviderItemName).ToArray();
                var viewCallbackNames = viewCallbackIdentities.Select(GetProviderItemName).ToArray();
                var receivedNotificationNames = desktopUpdateObserver.UpdatedItemParsingNames.Select(GetProviderItemName).ToArray();
                Console.WriteLine($"shell-provider-product-async-notifications: mode={sortStrategy}; adapter-item-send-attempts={ReadProductDiagnosticCounter(adapter, "GetProductAsyncItemNotificationAttempts")}; item-pidls-under-provider-root={ReadProductDiagnosticCounter(adapter, "GetProductAsyncItemNotificationUnderSyntheticRootCount")}; adapter-pidl-identities=[{string.Join('|', sentNotificationNames)}]; view-getnotify-calls={ReadProductDiagnosticCounter(adapter, "GetProductViewGetNotifyCallCount")}; view-getnotify-root={ReadProductDiagnosticString(adapter, "GetProductViewGetNotifyRootIdentity")}; view-getnotify-event-mask=0x{ReadProductDiagnosticCounter(adapter, "GetProductViewGetNotifyEventMask")}; view-callback-count={ReadProductDiagnosticCounter(adapter, "GetProductViewItemNotificationCallbackCount")}; view-callback-pidl-identities=[{string.Join('|', viewCallbackNames)}]; expected-item-names=[{string.Join('|', expectedUpdatedNames)}]; provider-root-item-pidls={itemUpdateObserver.UpdatedItemPidls}; provider-root-events=[{itemUpdateObserver.EventSummary}]; physical-root-item-names=[{string.Join('|', filesystemUpdateObserver.UpdatedItemNames)}]; physical-root-events=[{filesystemUpdateObserver.EventSummary}]; desktop-item-names=[{string.Join('|', desktopUpdateObserver.UpdatedItemNames)}]; desktop-pidl-identities=[{string.Join('|', receivedNotificationNames)}]; identities-match={sentNotificationIdentities.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(desktopUpdateObserver.UpdatedItemParsingNames.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)}; desktop-events=[{desktopUpdateObserver.EventSummary}]; burst-delivery-samples=[{string.Join('|', notificationDeliverySamples)}]");
                if (!notificationDirectory && !expectedUpdatedNames.SetEquals(desktopUpdateObserver.UpdatedItemNames))
                    throw new InvalidOperationException($"The Desktop-scope observer did not receive every changed row: expected=[{string.Join('|', expectedUpdatedNames)}]; received=[{string.Join('|', desktopUpdateObserver.UpdatedItemNames)}]; provider-root-events=[{itemUpdateObserver.EventSummary}]; physical-root-events=[{filesystemUpdateObserver.EventSummary}]; desktop-events=[{desktopUpdateObserver.EventSummary}].");
                if (notificationDirectory && desktopUpdateObserver.EventCount(0x00001000) == 0)
                    throw new InvalidOperationException($"The Desktop-scope observer did not receive SHCNE_UPDATEDIR; desktop-events=[{desktopUpdateObserver.EventSummary}].");
                if (notificationBurst)
                {
                    var rowObservation = ObserveProductRowCounts(hostType, host, TimeSpan.FromSeconds(2));
                    Console.WriteLine($"shell-provider-product-async-view-after-burst: shell-item-counts=[{string.Join('|', rowObservation.ShellItemCounts)}]; export-row-counts=[{string.Join('|', rowObservation.ExportRowCounts)}]; final-shell-items={rowObservation.FinalShellItems}; final-export-rows={rowObservation.FinalExportRows}");
                    if (rowObservation.FinalShellItems != expectedFolderCount || rowObservation.FinalExportRows != expectedFolderCount)
                        throw new InvalidOperationException($"The Shell view did not settle back to all {expectedFolderCount} rows after item updates.");
                    var burstCapture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var burstReadRows = typeof(ShellProviderLocationProbe).GetMethod("ReadExportRows", BindingFlags.Static | BindingFlags.NonPublic)!;
                    string[] burstColumns = [];
                    string[][] burstRows = [];
                    WaitUntil(() =>
                    {
                        try
                        {
                            var snapshot = burstCapture.Invoke(host, null)!;
                            var rows = burstReadRows.Invoke(null, [snapshot])!;
                            burstColumns = (string[])rows.GetType().GetField("Item1")!.GetValue(rows)!;
                            burstRows = (string[][])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                            return burstRows.Length == expectedFolderCount;
                        }
                        catch (TargetInvocationException exception) when (IsTransientShellReadError(exception)) { return false; }
                    }, TimeSpan.FromSeconds(5), "the burst Shell view did not yield all rows for sort-order inspection");
                    var sizeColumn = Array.FindIndex(burstColumns, name => name.Equals("Size", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Recursive Size", StringComparison.OrdinalIgnoreCase) || name == "大小");
                    var burstNameColumn = Array.FindIndex(burstColumns, name => name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name == "名称");
                    var burstSortValues = sizeColumn < 0 ? [] : burstRows.Select(row =>
                    {
                        var value = row[sizeColumn];
                        return (Name: Path.GetFileName(row[burstNameColumn]), Value: value,
                            Bytes: TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue);
                    }).ToArray();
                    var sizeSorted = burstSortValues.Length == expectedFolderCount && burstSortValues.Zip(
                        burstSortValues.Skip(1), (left, right) => left.Bytes <= right.Bytes).All(sorted => sorted);
                    Console.WriteLine($"shell-provider-product-async-sort-order: columns=[{string.Join('|', burstColumns)}]; size-column={sizeColumn}; ascending={sizeSorted}; rows=[{string.Join('|', burstSortValues.Select(row => $"{row.Name}:{row.Value}"))}]");
                    return 0;
                }
                if (notificationDirectory || notificationItems)
                {
                    var rowObservation = ObserveProductRowCounts(hostType, host, TimeSpan.FromSeconds(2));
                    var nativeCellsAfterDirectoryNotify = ReadNativeListViewCells((nint)viewWindowField.GetValue(host)!, 3);
                    var sortColumnsAfterNotification = notificationDirectory ? CaptureSortColumns(hostType, host) : [];
                    var viewStateAfterNotification = notificationDirectory
                        ? ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!)
                        : (Selected: [], Focused: (string?)null);
                    var selectedAfterNotification = viewStateAfterNotification.Selected;
                    var focusedRowAfterNotification = viewStateAfterNotification.Focused;
                    var shellFocusedRowAfterNotification = notificationDirectory ? ReadShellFocusedRow(hostType, host) : null;
                    var compareDiagnosticsAfterUpdate = notificationDirectory ? ReadProductCompareDiagnostics(adapter) : [];
                    string? postSortShellFocusedRow = null;
                    string? postSortListViewFocusedRow = null;
                    var explicitSortActionResult = 0;
                    var firstPairedSortActionResult = 0;
                    var rowCountAfterFirstPairedSort = -1;
                    var rowCountAfterSortInterval = -1;
                    var rowCountAfterSecondPairedSort = -1;
                    string[] sortColumnsAfterOpposite = [];
                    string[] sortColumnsAfterSavedVerb = [];
                    string[] paintMessages = [];
                    var explicitSortObservation = (Names: Array.Empty<string>(), ShellItemCounts: Array.Empty<int>(), NativeListViewCounts: Array.Empty<int>(), NativeListViewRowSnapshots: Array.Empty<string>(), ExportRowCounts: Array.Empty<int>(), TransientReadErrors: 0);
                    if (notificationFollowupSort)
                    {
                        stage = "updatedir-followed-by-shell-sort-command";
                        var exportSnapshot = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        var readExport = typeof(ShellProviderLocationProbe).GetMethod("ReadExportRows", BindingFlags.Static | BindingFlags.NonPublic)!;
                        var exported = readExport.Invoke(null, [exportSnapshot.Invoke(host, null)!])!;
                        var exportColumns = (string[])exported.GetType().GetField("Item1")!.GetValue(exported)!;
                        var exportNameColumnAfterNotify = Array.FindIndex(exportColumns, name => name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name == "名称");
                        if (exportNameColumnAfterNotify < 0) throw new InvalidOperationException("The product Shell view export did not expose the Name column after SHCNE_UPDATEDIR.");
                        ResetProductCompareDiagnostics(adapter);
                        var sortListView = FindNativeListView((nint)viewWindowField.GetValue(host)!);
                        if (notificationPairedSort)
                        {
                            using var paintProbe = new NativePaintMessageProbe(sortListView);
                            var requestedDirection = (int)sorts.GetValue(0)!.GetType().GetProperty("Direction")!.GetValue(sorts.GetValue(0))!;
                            var oppositeVerb = requestedDirection > 0 ? "sortdescending" : "sortascending";
                            var savedVerb = requestedDirection > 0 ? "sortascending" : "sortdescending";
                            if (notificationRedrawSuppressedSort)
                                _ = SendMessageW(sortListView, 0x000B, 0, 0); // WM_SETREDRAW(FALSE).
                            try
                            {
                                firstPairedSortActionResult = InvokeBackgroundShellVerb(hostType, host, oppositeVerb);
                                Check(firstPairedSortActionResult);
                                rowCountAfterFirstPairedSort = ReadNativeListViewItemCount((nint)viewWindowField.GetValue(host)!);
                                sortColumnsAfterOpposite = CaptureSortColumns(hostType, host);
                                if (notificationRedrawSuppressedSort) PumpDispatcher(TimeSpan.FromMilliseconds(100));
                                rowCountAfterSortInterval = ReadNativeListViewItemCount((nint)viewWindowField.GetValue(host)!);
                                explicitSortActionResult = InvokeBackgroundShellVerb(hostType, host, savedVerb);
                                Check(explicitSortActionResult);
                                rowCountAfterSecondPairedSort = ReadNativeListViewItemCount((nint)viewWindowField.GetValue(host)!);
                                sortColumnsAfterSavedVerb = CaptureSortColumns(hostType, host);
                                explicitSortObservation = ObserveProductSort(hostType, host, exportSnapshot, readExport,
                                    hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!, exportNameColumnAfterNotify,
                                    TimeSpan.FromSeconds(2), requestedDirection > 0 ? ["folder-small", "folder-large"] : ["folder-large", "folder-small"]);
                            }
                            finally
                            {
                                if (notificationRedrawSuppressedSort)
                                {
                                    _ = SendMessageW(sortListView, 0x000B, 1, 0); // WM_SETREDRAW(TRUE).
                                    _ = RedrawWindow(sortListView, 0, 0, 0x0001 | 0x0004 | 0x0080 | 0x0100);
                                }
                            }
                            paintMessages = paintProbe.Messages.ToArray();
                            postSortShellFocusedRow = ReadShellFocusedRow(hostType, host);
                            postSortListViewFocusedRow = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!).Focused;
                            var selectedAfterPairedSort = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!).Selected;
                            var expectedFinalOrder = requestedDirection > 0
                                ? new[] { "folder-small", "folder-large" }
                                : new[] { "folder-large", "folder-small" };
                            var finalDirectionIsSaved = sortColumnsAfterSavedVerb.Any(column =>
                                column.EndsWith(requestedDirection > 0 ? "/1" : "/-1", StringComparison.Ordinal));
                            if (!explicitSortObservation.Names.SequenceEqual(expectedFinalOrder, StringComparer.OrdinalIgnoreCase) ||
                                !finalDirectionIsSaved ||
                                !selectedBeforeNotification.SequenceEqual(selectedAfterPairedSort, StringComparer.OrdinalIgnoreCase) ||
                                !string.Equals(focusedRowBeforeNotification, postSortShellFocusedRow, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(focusedRowBeforeNotification, postSortListViewFocusedRow, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException($"The paired Shell sort did not restore the requested view state: expected-order=[{string.Join('|', expectedFinalOrder)}]; actual-order=[{string.Join('|', explicitSortObservation.Names)}]; expected-direction={requestedDirection}; actual-direction=[{string.Join('|', sortColumnsAfterSavedVerb)}]; selected=[{string.Join('|', selectedBeforeNotification)}]=>[{string.Join('|', selectedAfterPairedSort)}]; shell-focus={focusedRowBeforeNotification}=>{postSortShellFocusedRow}; listview-focus={focusedRowBeforeNotification}=>{postSortListViewFocusedRow}.");
                            var compareTraceAfterSecond = ReadProductCompareDiagnostics(adapter);
                            Console.WriteLine($"shell-provider-product-async-paired-sort: redraw-suppressed={notificationRedrawSuppressedSort}; requested-direction={requestedDirection}; opposite-verb={oppositeVerb}; opposite-hr=0x{firstPairedSortActionResult:X8}; rows-after-opposite={rowCountAfterFirstPairedSort}; rows-after-interval={rowCountAfterSortInterval}; direction-after-opposite=[{string.Join('|', sortColumnsAfterOpposite)}]; saved-verb={savedVerb}; saved-hr=0x{explicitSortActionResult:X8}; rows-after-saved={rowCountAfterSecondPairedSort}; final-direction=[{string.Join('|', sortColumnsAfterSavedVerb)}]; final-order=[{string.Join('|', explicitSortObservation.Names)}]; shell-row-counts=[{string.Join('|', explicitSortObservation.ShellItemCounts)}]; listview-row-counts=[{string.Join('|', explicitSortObservation.NativeListViewCounts)}]; row-snapshots=[{string.Join(" || ", explicitSortObservation.NativeListViewRowSnapshots)}]; selected-after={string.Join('|', selectedAfterPairedSort)}; shell-focus-after={postSortShellFocusedRow}; listview-focus-after={postSortListViewFocusedRow}; paint-messages=[{string.Join('|', paintMessages)}]; compare-trace=[{string.Join(" || ", compareTraceAfterSecond)}]");
                        }
                        else
                        {
                            explicitSortActionResult = InvokeBackgroundShellVerb(hostType, host, "sortascending");
                            Check(explicitSortActionResult);
                        }
                        if (!notificationPairedSort)
                        {
                        explicitSortObservation = ObserveProductSort(hostType, host, exportSnapshot, readExport,
                            hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!, exportNameColumnAfterNotify,
                            TimeSpan.FromSeconds(2), ["folder-small", "folder-large"]);
                        postSortShellFocusedRow = ReadShellFocusedRow(hostType, host);
                        postSortListViewFocusedRow = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!).Focused;
                        }
                    }
                    var compareDiagnosticsAfterExplicitSort = notificationFollowupSort
                        ? ReadProductCompareDiagnostics(adapter) : [];
                    var notificationTransitions = notificationStateSamples.OrderBy(pair => pair.Key)
                        .Select(pair => $"{pair.Key}:[{string.Join(',', pair.Value.Order(StringComparer.Ordinal))}]");
                    Console.WriteLine($"shell-provider-product-async-view-after-{(notificationDirectory ? "updatedir" : "item-update")}: samples-by-send-attempts=[{string.Join('|', notificationTransitions)}]; during-shell-item-counts=[{string.Join('|', notificationShellItemCounts.Order())}]; during-native-listview-counts=[{string.Join('|', notificationNativeListViewCounts.Order())}]; shell-item-counts-after=[{string.Join('|', rowObservation.ShellItemCounts)}]; export-row-counts-after=[{string.Join('|', rowObservation.ExportRowCounts)}]; final-shell-items={rowObservation.FinalShellItems}; final-export-rows={rowObservation.FinalExportRows}; sort-columns-before=[{string.Join('|', sortColumnsBeforeNotification)}]; sort-columns-after=[{string.Join('|', sortColumnsAfterNotification)}]; native-cells-before=[{string.Join(" || ", nativeCellsBeforeNotification.Select(row => string.Join('|', row)))}]; native-cells-after=[{string.Join(" || ", nativeCellsAfterDirectoryNotify.Select(row => string.Join('|', row)))}]; selected-before=[{string.Join('|', selectedBeforeNotification)}]; selected-after=[{string.Join('|', selectedAfterNotification)}]; listview-focused-row-before={focusedRowBeforeNotification}; listview-focused-row-after={focusedRowAfterNotification}; shell-focused-row-before={shellFocusedRowBeforeNotification}; shell-focused-row-after={shellFocusedRowAfterNotification}; update-compare-trace=[{string.Join(" || ", compareDiagnosticsAfterUpdate)}]; explicit-sort-hr=0x{explicitSortActionResult:X8}; explicit-sort-order=[{string.Join('|', explicitSortObservation.Names)}]; explicit-sort-shell-counts=[{string.Join('|', explicitSortObservation.ShellItemCounts)}]; explicit-sort-listview-counts=[{string.Join('|', explicitSortObservation.NativeListViewCounts)}]; explicit-sort-row-snapshots=[{string.Join(" || ", explicitSortObservation.NativeListViewRowSnapshots)}]; explicit-sort-shell-focus={postSortShellFocusedRow}; explicit-sort-listview-focus={postSortListViewFocusedRow}; explicit-sort-compare-trace=[{string.Join(" || ", compareDiagnosticsAfterExplicitSort)}]; view-callback-count={ReadProductDiagnosticCounter(adapter, "GetProductViewItemNotificationCallbackCount")}; desktop-events=[{desktopUpdateObserver.EventSummary}]");
                    if (rowObservation.FinalShellItems != expectedFolderCount || rowObservation.FinalExportRows != expectedFolderCount)
                        throw new InvalidOperationException($"The Shell view did not retain its {expectedFolderCount} rows after SHCNE_UPDATEDIR.");
                    if (notificationDirectory && (!selectedBeforeNotification.SequenceEqual(selectedAfterNotification, StringComparer.OrdinalIgnoreCase) ||
                        !string.Equals(focusedRowBeforeNotification, focusedRowAfterNotification, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(shellFocusedRowBeforeNotification, shellFocusedRowAfterNotification, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"SHCNE_UPDATEDIR changed selection/focus: selected=[{string.Join('|', selectedBeforeNotification)}]=>[{string.Join('|', selectedAfterNotification)}]; listview-focused-row={focusedRowBeforeNotification}=>{focusedRowAfterNotification}; shell-focused-row={shellFocusedRowBeforeNotification}=>{shellFocusedRowAfterNotification}.");
                    return 0;
                }
            }

            stage = "capture-sort";
            var capture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var readRows = typeof(ShellProviderLocationProbe).GetMethod("ReadExportRows", BindingFlags.Static | BindingFlags.NonPublic)!;
            var readItemCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? snapshot = null;
            object? rows = null;
            string[][] orderedRows = [];
            string[] columns = [];
            string[] names = [];
            var captureShellItemCounts = new HashSet<int>();
            var captureExportRowCounts = new HashSet<int>();
            var captureListViewCounts = new HashSet<int>();
            var captureTransientReadErrors = 0;
            try
            {
                WaitUntil(() =>
                {
                    captureListViewCounts.Add(ReadNativeListViewItemCount((nint)viewWindowField.GetValue(host)!));
                    var liveCounts = readItemCounts.Invoke(host, [false, false]);
                    if (liveCounts is not null)
                        captureShellItemCounts.Add((int)liveCounts.GetType().GetProperty("Total")!.GetValue(liveCounts)!);
                    try
                    {
                        snapshot = capture.Invoke(host, null)!;
                        rows = readRows.Invoke(null, [snapshot])!;
                        orderedRows = (string[][])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                        columns = (string[])rows.GetType().GetField("Item1")!.GetValue(rows)!;
                        captureExportRowCounts.Add(orderedRows.Length);
                        return orderedRows.Length == expectedFolderCount;
                    }
                    catch (TargetInvocationException exception) when (IsTransientShellReadError(exception))
                    {
                        captureTransientReadErrors++;
                        return false;
                    }
                }, TimeSpan.FromSeconds(10), "the product Shell view did not expose both rows after asynchronous values completed");
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException($"{exception.Message}; item-counts=[{string.Join('|', captureShellItemCounts.Order())}]; native-listview-item-counts=[{string.Join('|', captureListViewCounts.Order())}]; export-counts=[{string.Join('|', captureExportRowCounts.Order())}]; transient-read-errors={captureTransientReadErrors}", exception);
            }
            var nameColumn = Array.FindIndex(columns, name => name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name == "名称");
            if (nameColumn < 0) throw new InvalidOperationException("The product Shell view export did not expose the Name column.");
            names = orderedRows.Select(row => Path.GetFileName(row[nameColumn]) ?? row[nameColumn]).ToArray();
            var orderAfterAsyncFill = names;
            var autoResorted = names.SequenceEqual(expectedSizeSortedNames, StringComparer.OrdinalIgnoreCase);
            if (sortStrategy == "single-sort-verb")
            {
                stage = "single-sort-command";
                var result = InvokeBackgroundShellVerb(hostType, host, "sortascending");
                Check(result);
                var observation = ObserveProductSort(hostType, host, capture, readRows, readItemCounts,
                    nameColumn, TimeSpan.FromMilliseconds(400));
                ReportSortExperiment(sortStrategy, orderAfterAsyncFill, autoResorted, result, observation, classId);
            }
            else if (sortStrategy == "set-sort-columns")
            {
                stage = "set-sort-columns";
                var result = InvokeProductSetSorting(hostType, host, sorts);
                Check(result);
                var observation = ObserveProductSort(hostType, host, capture, readRows, readItemCounts,
                    nameColumn, TimeSpan.FromMilliseconds(400));
                ReportSortExperiment(sortStrategy, orderAfterAsyncFill, autoResorted, result, observation, classId);
            }
            else if (sortStrategy is "scheduled" or "large-readiness")
            {
                stage = "production-initial-sort-after-async-fill";
                if (largeSortReadiness) initialVirtualSortPaintProbe?.Clear();
                ResetProductCompareDiagnostics(adapter);
                var largeReadinessCountTrace = largeSortReadiness ? new List<string>() : null;
                var observation = largeSortReadiness
                    ? ObserveProductLargeSortWithCountSampling(hostType, host, capture, readRows, readItemCounts,
                        nameColumn, expectedSizeSortedNames, adapter, largeReadinessCountTrace!)
                    : ObserveProductSort(hostType, host, capture, readRows, readItemCounts,
                        nameColumn, TimeSpan.FromSeconds(20), expectedSizeSortedNames);
                string[] largeSortSelectedAfter = [];
                string? largeSortListViewFocusAfter = null;
                string? largeSortShellFocusAfter = null;
                if (largeSortReadiness)
                {
                    WaitUntil(() =>
                    {
                        var selection = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!);
                        var selected = selection.Selected.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                        var listViewFocus = selection.Focused;
                        var shellFocus = ReadShellFocusedRow(hostType, host);
                        return largeSortSelectedBefore.SequenceEqual(selected, StringComparer.OrdinalIgnoreCase) &&
                            string.Equals(largeSortListViewFocusBefore, listViewFocus, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(largeSortShellFocusBefore, shellFocus, StringComparison.OrdinalIgnoreCase);
                    }, TimeSpan.FromSeconds(13), "The Shell sort did not restore the selected rows and focused row within the product sort-recovery window.");
                    var largeSortOrderValid = observation.Names.SequenceEqual(expectedSizeSortedNames, StringComparer.OrdinalIgnoreCase);
                    var selection = ReadNativeListViewSelectionState((nint)viewWindowField.GetValue(host)!);
                    largeSortSelectedAfter = selection.Selected.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    largeSortListViewFocusAfter = selection.Focused;
                    largeSortShellFocusAfter = ReadShellFocusedRow(hostType, host);
                    if (!largeSortSelectedBefore.SequenceEqual(largeSortSelectedAfter, StringComparer.OrdinalIgnoreCase) ||
                        !string.Equals(largeSortListViewFocusBefore, largeSortListViewFocusAfter, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(largeSortShellFocusBefore, largeSortShellFocusAfter, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Automatic initial sort changed selection/focus: auto-resorted-before-observation={autoResorted}; sort-columns=[{string.Join('|', CaptureSortColumns(hostType, host))}]; final-order-valid={largeSortOrderValid}; first=[{string.Join('|', observation.Names.Take(4))}]; last=[{string.Join('|', observation.Names.TakeLast(4))}]; selected=[{string.Join('|', largeSortSelectedBefore)}]=>[{string.Join('|', largeSortSelectedAfter)}]; listview-focus={largeSortListViewFocusBefore}=>{largeSortListViewFocusAfter}; shell-focus={largeSortShellFocusBefore}=>{largeSortShellFocusAfter}; count-transitions=[{string.Join('|', largeReadinessCountTrace ?? [])}].");
                }
                var compareDiagnosticsAfterScheduledSort = largeSortReadiness ? [] : ReadProductCompareDiagnostics(adapter);
                if (!observation.Names.SequenceEqual(expectedSizeSortedNames, StringComparer.OrdinalIgnoreCase))
                    throw new TimeoutException("The production navigation restore did not sort by recursive size after asynchronous values completed.");
                if (largeSortReadiness)
                {
                    var compareCount = ReadProductLongCounter(adapter, "GetProductRecursiveCompareCount") ?? 0;
                    var timingSamples = ReadProductLongCounter(adapter, "GetProductRecursiveCompareTimingSamples") ?? 0;
                    var sampledTicks = ReadProductLongCounter(adapter, "GetProductRecursiveCompareSampledTicks") ?? 0;
                    var maximumSampleTicks = ReadProductLongCounter(adapter, "GetProductRecursiveCompareMaximumSampleTicks") ?? 0;
                    var fastPathHits = ReadProductLongCounter(adapter, "GetProductRecursiveCompareFastPathHits") ?? 0;
                    var fastPathMisses = ReadProductLongCounter(adapter, "GetProductRecursiveCompareFastPathMisses") ?? 0;
                    var sampledLookupMilliseconds = sampledTicks * 1000d / Stopwatch.Frequency;
                    var maximumLookupMicroseconds = maximumSampleTicks * 1_000_000d / Stopwatch.Frequency;
                    var estimatedLookupMilliseconds = timingSamples == 0 ? 0 :
                        sampledLookupMilliseconds / timingSamples * compareCount;
                    Console.WriteLine($"shell-provider-product-async-large-readiness: rows={expectedFolderCount}; complete-readiness-scan-ms={readinessClock.Elapsed.TotalMilliseconds:F2}; batched-scan-ms={batchReadinessClock.Elapsed.TotalMilliseconds:F2}; batches={readinessBatchCount}; max-batch-ms={maximumBatchMilliseconds:F2}; auto-resorted-before-observation={autoResorted}; final-order-valid=True; first={observation.Names[0]}; last={observation.Names[^1]}; shell-counts=[{string.Join('|', observation.ShellItemCounts)}]; listview-counts=[{string.Join('|', observation.NativeListViewCounts)}]; export-counts=[{string.Join('|', observation.ExportRowCounts)}]; count-transitions=[{string.Join('|', largeReadinessCountTrace ?? [])}]; paint-messages=[{string.Join('|', initialVirtualSortPaintProbe?.Messages ?? [])}]; selected=[{string.Join('|', largeSortSelectedBefore)}]=>[{string.Join('|', largeSortSelectedAfter)}]; listview-focus={largeSortListViewFocusBefore}=>{largeSortListViewFocusAfter}; shell-focus={largeSortShellFocusBefore}=>{largeSortShellFocusAfter}; foreground-unchanged=True");
                    Console.WriteLine($"shell-provider-product-recursive-compare-timing: comparisons={compareCount}; sampled-every=64; samples={timingSamples}; sampled-size-lookup-ms={sampledLookupMilliseconds:F3}; estimated-size-lookup-ms={estimatedLookupMilliseconds:F3}; maximum-sampled-size-lookup-us={maximumLookupMicroseconds:F2}; direct-path-cache-hits={fastPathHits}; direct-path-cache-misses={fastPathMisses}");
                }
                else ReportSortExperiment("automatic-after-fill", orderAfterAsyncFill, autoResorted, 0, observation, classId);
                Console.WriteLine($"shell-provider-product-async-scheduled-sort-state: sort-columns=[{string.Join('|', CaptureSortColumns(hostType, host))}]; compare-trace=[{string.Join(" || ", compareDiagnosticsAfterScheduledSort)}]");
            }
            else if (sortStrategy == "header-click")
            {
                stage = "native-header-click-sort";
                WaitUntil(() => expectedUpdatedNames.SetEquals(desktopUpdateObserver!.UpdatedItemNames),
                    TimeSpan.FromSeconds(5), "the hidden Shell observer did not receive both completed-folder updates before header sorting");
                var sortHeaderViewWindowField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var header = FindSortHeader((nint)sortHeaderViewWindowField.GetValue(host)!);
                var headerTexts = ReadNativeHeaderTexts(header, 2);
                var recursiveColumn = Array.FindIndex(headerTexts, name =>
                    name.Equals("Recursive Size", StringComparison.OrdinalIgnoreCase) || name == "递归大小");
                if (recursiveColumn < 0)
                    throw new InvalidOperationException($"The product Shell header omitted the recursive-size column: [{string.Join('|', headerTexts)}].");

                ClickHeaderColumn(header, recursiveColumn);
                var firstClick = ObserveProductSort(hostType, host, capture, readRows, readItemCounts,
                    nameColumn, TimeSpan.FromMilliseconds(400));
                ClickHeaderColumn(header, recursiveColumn);
                var secondClick = ObserveProductSort(hostType, host, capture, readRows, readItemCounts,
                    nameColumn, TimeSpan.FromSeconds(2), ["folder-small", "folder-large"]);
                var observation = (
                    Names: secondClick.Names,
                    ShellItemCounts: firstClick.ShellItemCounts.Concat(secondClick.ShellItemCounts).Distinct().Order().ToArray(),
                    NativeListViewCounts: firstClick.NativeListViewCounts.Concat(secondClick.NativeListViewCounts).Distinct().Order().ToArray(),
                    NativeListViewRowSnapshots: firstClick.NativeListViewRowSnapshots.Concat(secondClick.NativeListViewRowSnapshots).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    ExportRowCounts: firstClick.ExportRowCounts.Concat(secondClick.ExportRowCounts).Distinct().Order().ToArray(),
                    TransientReadErrors: firstClick.TransientReadErrors + secondClick.TransientReadErrors);
                ReportSortExperiment(sortStrategy, orderAfterAsyncFill, autoResorted, 0, observation, classId);
                if (!secondClick.Names.SequenceEqual(["folder-small", "folder-large"], StringComparer.OrdinalIgnoreCase))
                    throw new TimeoutException("Native recursive-size header clicks did not restore ascending order.");
                if (observation.ShellItemCounts.Contains(0) || observation.ExportRowCounts.Contains(0))
                    throw new InvalidOperationException("Native recursive-size header sorting temporarily emptied the product Shell view.");
            }
            else throw new ArgumentOutOfRangeException(nameof(sortStrategy), sortStrategy, "Unknown product sort experiment.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"shell-provider-product-async: failed-stage={stage}; exception={exception}");
            return 1;
        }
        finally
        {
            initialVirtualSortPaintProbe?.Dispose();
            itemUpdateObserver?.Dispose();
            filesystemUpdateObserver?.Dispose();
            desktopUpdateObserver?.Dispose();
            notificationSource?.Dispose();
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else if (host is IDisposable disposable)
                disposable.Dispose();
            PumpDispatcher(TimeSpan.FromMilliseconds(150));
            if (namespaceRegistered) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistered) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            if (adapter != 0) NativeLibrary.Free(adapter);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            HostProbeForegroundGuard.EnsureUnchanged(foregroundBefore, GetForegroundWindow(), "product async Shell view probe");
        }
    }

    internal static int Run(bool descending = false, bool ignoreFolderSizeForSort = false,
        bool registerDesktopNamespace = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-HostProvider-" + Guid.NewGuid().ToString("N"));
        var pathA = Path.Combine(root, "default-root");
        var pathB = Path.Combine(root, "session-root");
        var smallFolder = Path.Combine(pathB, "folder-small");
        var largeFolder = Path.Combine(pathB, "folder-large");
        const int expectedRows = 3;
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        nint adapter = 0;
        var classRegistered = false;
        var namespaceRegistered = false;
        HwndSource? source = null;
        HwndHost? host = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(pathA, "default-only"));
            Directory.CreateDirectory(smallFolder);
            Directory.CreateDirectory(largeFolder);
            File.WriteAllText(Path.Combine(pathA, "default-only.txt"), "wrong root");
            File.WriteAllBytes(Path.Combine(pathB, "file-middle.bin"), new byte[12]);
            File.WriteAllBytes(Path.Combine(smallFolder, "payload.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(largeFolder, "payload.bin"), new byte[29]);

            var adapterPath = FindAdapterLibrary();
            adapter = NativeLibrary.Load(adapterPath);
            Check(GetExport<SetClassId>(adapter, "SetSyntheticFolderClassIdForProbe")(ref classId));
            Check(GetExport<SetPath>(adapter, "SetSyntheticSortBackingPath")(pathA));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticCustomSizeColumnForProbe")(1));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticRecursiveSizeScanOnDemandForProbe")(1));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticIgnoreRecursiveFolderSizeSortForProbe")(
                ignoreFolderSizeForSort ? 1 : 0));
            RegisterClass(classKeyPath, adapterPath);
            classRegistered = true;
            if (registerDesktopNamespace)
            {
                RegisterNamespace(namespaceKeyPath);
                namespaceRegistered = true;
            }

            var targetIdentity = $"::{{{classId:D}}}\\edpath:{EncodePath(pathB)}";
            ProbeFilesystemPathRoundTrip(targetIdentity, pathB);
            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var propertyKeyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var sortColumnType = appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!;
            var displayColumnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var basicFormat = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var recursiveSortKey = Activator.CreateInstance(propertyKeyType,
                Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u)!;
            var sortColumns = Array.CreateInstance(sortColumnType, 1);
            var initialDirection = descending ? -1 : 1;
            sortColumns.SetValue(Activator.CreateInstance(sortColumnType, [recursiveSortKey, initialDirection]), 0);
            var displayColumns = Array.CreateInstance(displayColumnType, 2);
            var nameKey = Activator.CreateInstance(propertyKeyType, basicFormat, 10u)!;
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [recursiveSortKey, 180]), 0);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [nameKey, 240]), 1);
            var viewStateConstructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == 6);
            var initialViewState = viewStateConstructor.Invoke([4u, 16, sortColumns, null, true, displayColumns]);
            var restoredTab = RoundTripSessionTab(appAssembly, Path.Combine(root, "ExplorerDirs.ini"),
                targetIdentity, initialViewState, initialDirection);
            targetIdentity = (string)restoredTab.GetType().GetProperty("Path")!.GetValue(restoredTab)!;
            var viewState = restoredTab.GetType().GetProperty("View")!.GetValue(restoredTab)!;
            if (!registerDesktopNamespace)
                Console.WriteLine("shell-provider-registration-gate: desktop-namespace-registration=False; clsid-com-registration=True");
            LaunchSessionRecoveryChild(Path.Combine(root, "ExplorerDirs.ini"), adapterPath, classId, pathA,
                ignoreFolderSizeForSort);
            Console.WriteLine("shell-provider-session-ini: path-preserved=True; recursive-column-and-sort-preserved=True");
            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            host = (HwndHost)Activator.CreateInstance(hostType, targetIdentity, viewState)!;
            var grid = new Grid { Width = 800, Height = 500 };
            grid.Children.Add(host);
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs Shell provider HostProbe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 800, Height = 500
            });
            source.RootVisual = grid;
            grid.Measure(new Size(800, 500));
            grid.Arrange(new Rect(0, 0, 800, 500));
            grid.UpdateLayout();

            var navigatedField = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindowField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var currentPathProperty = hostType.GetProperty("CurrentPath", BindingFlags.Instance | BindingFlags.Public)!;
            WaitUntil(() => Equals(navigatedField.GetValue(host), true) && (nint)viewWindowField.GetValue(host)! != 0,
                TimeSpan.FromSeconds(15), "the product ExplorerBrowserHost did not complete navigation to the registered Shell path");

            var actualPath = (string)currentPathProperty.GetValue(host)!;
            if (!string.Equals(actualPath, targetIdentity, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The product host persisted a different location: expected='{targetIdentity}', actual='{actualPath}'.");
            var locationField = hostType.GetField("_location", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var location = locationField.GetValue(host)!;
            var isFilesystem = (bool)location.GetType().GetProperty("IsFileSystemLocation")!.GetValue(location)!;
            if (isFilesystem) throw new InvalidOperationException("The Shell namespace path was incorrectly classified as a filesystem path.");

            var readCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? counts = null;
            WaitUntil(() =>
            {
                counts = readCounts.Invoke(host, [false, false]);
                return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == expectedRows;
            }, TimeSpan.FromSeconds(15), "the production host did not enumerate the registered Shell folder's mixed rows");
            if (counts is null) throw new InvalidOperationException("The production host could not read the Shell folder's row count.");
            var total = (int)counts.GetType().GetProperty("Total")!.GetValue(counts)!;
            var currentViewState = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(host, null)!;
            var actualSorts = ((IEnumerable)currentViewState.GetType().GetProperty("SortColumns")!.GetValue(currentViewState)!)
                .Cast<object>().Select(sort =>
                {
                    var key = sort.GetType().GetProperty("Key")!.GetValue(sort)!;
                    return $"{key.GetType().GetProperty("FormatId")!.GetValue(key)}:{key.GetType().GetProperty("PropertyId")!.GetValue(key)}/{sort.GetType().GetProperty("Direction")!.GetValue(sort)}";
                }).ToArray();
            string[] directRecursiveValues = [];
            WaitUntil(() =>
            {
                directRecursiveValues = ReadRecursiveValues(hostType, host, expectedRows);
                return directRecursiveValues.Length == expectedRows && directRecursiveValues.All(value => !string.IsNullOrWhiteSpace(value));
            }, TimeSpan.FromSeconds(15), "the product host's IShellFolder2.GetDetailsEx did not return recursive values");
            PumpDispatcher(TimeSpan.FromMilliseconds(300));
            WaitUntil(() =>
            {
                var settledCounts = readCounts.Invoke(host, [false, false]);
                return settledCounts is not null &&
                    (int)settledCounts.GetType().GetProperty("Total")!.GetValue(settledCounts)! == expectedRows;
            }, TimeSpan.FromSeconds(10), "the product Shell view did not settle its row count after initial custom sorting");
            var recursiveDisplay = ReadRecursiveDisplayText(adapter, hostType, host, expectedRows);
            if (!string.Equals(recursiveDisplay.Header, "Recursive Size", StringComparison.Ordinal) ||
                recursiveDisplay.Values.Select(value => TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue)
                    .Order().SequenceEqual([5UL, 12UL, 29UL]) is false)
                throw new InvalidOperationException($"The provider's recursive GetDetailsOf contract is unexpected: header='{recursiveDisplay.Header}', values=[{string.Join('|', recursiveDisplay.Values)}].");
            var capture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var snapshot = capture.Invoke(host, null)!;
            var (columnNames, rows) = ReadExportRows(snapshot);
            var sizeColumnIndex = Array.FindIndex(columnNames, name =>
                name.Contains("Recursive Size", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Size", StringComparison.OrdinalIgnoreCase) || name.Equals("大小", StringComparison.Ordinal) || name.Contains("{b725f130-47ef-101a-a5f1-02608c9eebac}:12", StringComparison.OrdinalIgnoreCase));
            if (sizeColumnIndex < 0) throw new InvalidOperationException($"The product view omitted the restored recursive-size column: [{string.Join('|', columnNames)}].");
            if (rows.Length != expectedRows) throw new InvalidOperationException($"The product export snapshot returned {rows.Length} rows; expected {expectedRows}.");

            var nameColumnIndex = Array.FindIndex(columnNames, name =>
                name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name.Equals("名称", StringComparison.Ordinal));
            if (nameColumnIndex < 0) throw new InvalidOperationException($"The product host did not expose its Name column: [{string.Join('|', columnNames)}].");
            var namesInShellOrder = rows.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var smallRowIndex = Array.FindIndex(rows, row => Path.GetFileName(row[nameColumnIndex])
                .Equals("folder-small", StringComparison.OrdinalIgnoreCase));
            var largeRowIndex = Array.FindIndex(rows, row => Path.GetFileName(row[nameColumnIndex])
                .Equals("folder-large", StringComparison.OrdinalIgnoreCase));
            if (smallRowIndex < 0 || largeRowIndex < 0 ||
                !TryReadBytes(directRecursiveValues[0], out var firstBytes) || !TryReadBytes(directRecursiveValues[1], out var secondBytes) ||
                firstBytes == secondBytes)
                throw new InvalidOperationException($"The product host did not expose distinct recursive values: rows=[{string.Join('|', namesInShellOrder)}], direct-values=[{string.Join('|', directRecursiveValues)}], sort-state=[{string.Join('|', actualSorts)}].");

            var getCompareCount = GetExport<GetCounter>(adapter, "GetSyntheticSortCompareCount");
            var getCompareCountForColumn = GetExport<GetCounterForColumn>(adapter, "GetSyntheticSortCompareCountForColumn");
            var compareCountBeforeSort = getCompareCount();
            var columnCompareCountsBefore = Enumerable.Range(0, 8).Select(index => getCompareCountForColumn((uint)index)).ToArray();
            var ascendingOrder = new[] { "folder-small", "file-middle.bin", "folder-large" };
            var descendingOrder = ascendingOrder.Reverse().ToArray();
            var orderBeforeRefresh = Array.Empty<string>();
            WaitUntil(() =>
            {
                orderBeforeRefresh = ReadExportRows(capture.Invoke(host, null)!).Rows
                    .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
                return MatchesExpectedSortOrder(orderBeforeRefresh, ascendingOrder, descending, ignoreFolderSizeForSort);
            }, TimeSpan.FromSeconds(15), "the product host did not apply the restored custom sort before a full view refresh");
            var providerColumnContract = ReadProviderColumnContract(hostType, host);
            if ((!ignoreFolderSizeForSort && providerColumnContract.CompareResult is not (-1 or 1)) ||
                (ignoreFolderSizeForSort && !descending && providerColumnContract.CompareResult != 0) ||
                providerColumnContract.DataKey.FormatId != new Guid("7E1F84B7-C052-4E51-8A3C-869254A6B910") ||
                providerColumnContract.DataKey.PropertyId != 1)
                throw new InvalidOperationException($"The provider's native recursive column contract was unexpected: key={providerColumnContract.DataKey.FormatId:B}:{providerColumnContract.DataKey.PropertyId}, compare={providerColumnContract.CompareResult}.");
            var refreshCompareCountBefore = getCompareCount();
            // In the registered provider host, restored sort state may precede the first actual row reorder.
            var shellRefreshResult = RefreshCurrentShellView(hostType, host);
            if (shellRefreshResult < 0) Marshal.ThrowExceptionForHR(shellRefreshResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            var orderAfterRefresh = ReadExportRows(capture.Invoke(host, null)!).Rows
                .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var refreshCompareCountAfter = getCompareCount();
            if (!MatchesExpectedSortOrder(orderAfterRefresh, ascendingOrder, descending, ignoreFolderSizeForSort))
                throw new InvalidOperationException($"A full Shell view refresh did not apply the recursive-size order: [{string.Join('|', orderAfterRefresh)}].");
            var ascendingCommandCompareCountBefore = getCompareCount();
            var sortCommandResult = InvokeBackgroundShellVerb(hostType, host, "sortascending");
            if (sortCommandResult < 0) Marshal.ThrowExceptionForHR(sortCommandResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            var commandCompareCount = getCompareCount();
            var orderAfterAscendingCommand = ReadExportRows(capture.Invoke(host, null)!).Rows
                .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var descendingCommandResult = InvokeBackgroundShellVerb(hostType, host, "sortdescending");
            if (descendingCommandResult < 0) Marshal.ThrowExceptionForHR(descendingCommandResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            var orderAfterDescendingCommand = ReadExportRows(capture.Invoke(host, null)!).Rows
                .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var descendingCommandSorts = CaptureSortColumns(hostType, host);
            var descendingCommandCompareCount = getCompareCount();
            var ascendingAgainCommandResult = InvokeBackgroundShellVerb(hostType, host, "sortascending");
            if (ascendingAgainCommandResult < 0) Marshal.ThrowExceptionForHR(ascendingAgainCommandResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            var orderAfterAscendingAgainCommand = ReadExportRows(capture.Invoke(host, null)!).Rows
                .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var ascendingAgainCommandSorts = CaptureSortColumns(hostType, host);
            var ascendingAgainCommandCompareCount = getCompareCount();
            var sortHeaderWindow = FindSortHeader((nint)viewWindowField.GetValue(host)!);
            var logicalHeaderTexts = ReadNativeHeaderTexts(sortHeaderWindow, 2);
            var displayColumnsBeforeHeaderOrder = CaptureDisplayColumns(hostType, host);
            var initialVisualHeaderOrder = ReadNativeHeaderVisualTexts(sortHeaderWindow, logicalHeaderTexts);
            var nativeHeaderOrder = initialVisualHeaderOrder;
            var requestedHeaderOrder = new[] { "Recursive Size", "Name" };
            if (!nativeHeaderOrder.SequenceEqual(requestedHeaderOrder, StringComparer.Ordinal))
            {
                _ = SetNativeHeaderOrder(sortHeaderWindow, [1, 0]);
                PumpDispatcher(TimeSpan.FromMilliseconds(100));
                nativeHeaderOrder = ReadNativeHeaderVisualTexts(sortHeaderWindow, logicalHeaderTexts);
                if (!nativeHeaderOrder.SequenceEqual(requestedHeaderOrder, StringComparer.Ordinal))
                {
                    DragHeaderColumnRight(sortHeaderWindow);
                    PumpDispatcher(TimeSpan.FromMilliseconds(100));
                    nativeHeaderOrder = ReadNativeHeaderVisualTexts(sortHeaderWindow, logicalHeaderTexts);
                }
                (columnNames, rows) = ReadExportRows(capture.Invoke(host, null)!);
                sizeColumnIndex = Array.FindIndex(columnNames, name =>
                    name.Contains("Recursive Size", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Size", StringComparison.OrdinalIgnoreCase) || name.Equals("大小", StringComparison.Ordinal) || name.Contains("{b725f130-47ef-101a-a5f1-02608c9eebac}:12", StringComparison.OrdinalIgnoreCase));
                nameColumnIndex = Array.FindIndex(columnNames, name =>
                    name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name.Equals("名称", StringComparison.Ordinal));
                if (sizeColumnIndex < 0 || nameColumnIndex < 0)
                    throw new InvalidOperationException($"Reordered product export omitted a visible column: [{string.Join('|', columnNames)}].");
            }
            var recursiveHeaderIndex = Array.FindIndex(logicalHeaderTexts, name => name == recursiveDisplay.Header);
            if (recursiveHeaderIndex < 0)
                throw new InvalidOperationException($"The native header omitted the recursive column after reorder: logical=[{string.Join('|', logicalHeaderTexts)}], visual=[{string.Join('|', nativeHeaderOrder)}].");
            var headerCompareCountBefore = getCompareCount();
            var headerColumnCountsBefore = Enumerable.Range(0, 8).Select(index => getCompareCountForColumn((uint)index)).ToArray();
            ClickHeaderColumn(sortHeaderWindow, recursiveHeaderIndex);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            (columnNames, rows) = ReadExportRows(capture.Invoke(host, null)!);
            var headerFirstOrder = rows.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var headerFirstSorts = CaptureSortColumns(hostType, host);
            var headerFirstCompareCount = getCompareCount();
            ClickHeaderColumn(sortHeaderWindow, recursiveHeaderIndex);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            directRecursiveValues = ReadRecursiveValues(hostType, host, expectedRows);
            (columnNames, rows) = ReadExportRows(capture.Invoke(host, null)!);
            namesInShellOrder = rows.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var exportedRecursiveValues = rows.Select(row => row[sizeColumnIndex]).ToArray();
            var exportedValuesByName = rows.ToDictionary(
                row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex],
                row => TryReadBytes(row[sizeColumnIndex], out var bytes) ? bytes : ulong.MaxValue,
                StringComparer.OrdinalIgnoreCase);
            if (exportedValuesByName.Count != expectedRows || exportedValuesByName["folder-small"] != 5 ||
                exportedValuesByName["file-middle.bin"] != 12 || exportedValuesByName["folder-large"] != 29)
                throw new InvalidOperationException($"The product export snapshot did not consume recursive display values in Shell row order: columns=[{string.Join('|', columnNames)}], values=[{string.Join('|', exportedRecursiveValues)}].");
            var mappedShellExport = ReadVisibleShellDetailsSnapshot(adapter, hostType, host, nativeHeaderOrder, total);
            if (!mappedShellExport.Columns.SequenceEqual(["Recursive Size", "Name"], StringComparer.Ordinal) ||
                !mappedShellExport.Rows.Select(row => string.Join('|', row)).SequenceEqual(
                    namesInShellOrder.Select(name => $"{exportedValuesByName[name]}|{name}"), StringComparer.Ordinal))
                throw new InvalidOperationException($"The visible Shell details-to-column mapping produced unexpected export data: columns=[{string.Join('|', mappedShellExport.Columns)}], rows=[{string.Join('|', mappedShellExport.Rows.Select(row => string.Join('|', row)))}], ordinals=[{string.Join('|', mappedShellExport.Ordinals)}].");
            var valuesByName = rows.Select((row, index) =>
                    (Name: namesInShellOrder[index], Bytes: TryReadBytes(directRecursiveValues[index], out var bytes) ? bytes : ulong.MaxValue))
                .ToDictionary(item => item.Name, item => item.Bytes, StringComparer.OrdinalIgnoreCase);
            var recursiveValuesMatchFixture = valuesByName["folder-small"] == 5 &&
                valuesByName["file-middle.bin"] == 12 && valuesByName["folder-large"] == 29;
            if (!recursiveValuesMatchFixture)
                throw new InvalidOperationException($"The product Shell view returned unexpected per-folder values: [{string.Join('|', valuesByName.Select(item => $"{item.Key}={item.Value}"))}].");
            var sortApplied = MatchesExpectedSortOrder(namesInShellOrder, ascendingOrder,
                descending: false, ignoreFolderSizeForSort: ignoreFolderSizeForSort);
            var compareCountAfterSort = getCompareCount();
            var columnCompareDeltas = Enumerable.Range(0, columnCompareCountsBefore.Length)
                .Select(index => getCompareCountForColumn((uint)index) - columnCompareCountsBefore[index]).ToArray();

            var headerCompareDeltas = Enumerable.Range(0, columnCompareCountsBefore.Length)
                .Select(index => getCompareCountForColumn((uint)index) - headerColumnCountsBefore[index]).ToArray();
            Console.WriteLine($"shell-provider-location: session-ini-roundtrip=True; product-host-navigation=True; is-filesystem=False; rows={total}; saved-visible-columns=[{string.Join('|', displayColumnsBeforeHeaderOrder)}]; visible-columns-after-header-action=[{string.Join('|', CaptureDisplayColumns(hostType, host))}]; logical-header-texts=[{string.Join('|', logicalHeaderTexts)}]; initial-visual-header-order=[{string.Join('|', initialVisualHeaderOrder)}]; visual-header-order=[{string.Join('|', nativeHeaderOrder)}]; recursive-header-index={recursiveHeaderIndex}; getdetailsof-headers=[{string.Join('|', mappedShellExport.ShellHeaders)}]; mapped-export-columns=[{string.Join('|', mappedShellExport.Columns)}]; mapped-export-ordinals=[{string.Join('|', mappedShellExport.Ordinals)}]; mapped-export-rows=[{string.Join('|', mappedShellExport.Rows.Select(row => string.Join('|', row)))}]; getdetailsof-header='{recursiveDisplay.Header}'; getdetailsof-values=[{string.Join('|', recursiveDisplay.Values)}]; export-columns=[{string.Join('|', columnNames)}]; export-values=[{string.Join('|', rows.Select(row => row[sizeColumnIndex]))}]; order-before-shell-refresh=[{string.Join('|', orderBeforeRefresh)}]; order-after-shell-refresh=[{string.Join('|', orderAfterRefresh)}]; shell-refresh-hr=0x{shellRefreshResult:X8}; refresh-compare-delta={refreshCompareCountAfter - refreshCompareCountBefore}; displayed-order-after-two-header-clicks=[{string.Join('|', namesInShellOrder)}]; order-after-first-header-click=[{string.Join('|', headerFirstOrder)}]; header-sort-state-after-first-click=[{string.Join('|', headerFirstSorts)}]; order-after-sortascending=[{string.Join('|', orderAfterAscendingCommand)}]; order-after-sortdescending=[{string.Join('|', orderAfterDescendingCommand)}]; sort-state-after-sortdescending=[{string.Join('|', descendingCommandSorts)}]; order-after-next-sortascending=[{string.Join('|', orderAfterAscendingAgainCommand)}]; sort-state-after-next-sortascending=[{string.Join('|', ascendingAgainCommandSorts)}]; direct-recursive-values=[{string.Join('|', directRecursiveValues)}]; fixture-values-correct={recursiveValuesMatchFixture}; provider-map-column-to-scid={providerColumnContract.DataKey.FormatId:B}:{providerColumnContract.DataKey.PropertyId}; direct-provider-compare={providerColumnContract.CompareResult}; restored-sort-state=[{string.Join('|', actualSorts)}]; shell-sort-command-hr=0x{sortCommandResult:X8}; shell-command-compare-delta={commandCompareCount - ascendingCommandCompareCountBefore}; descending-command-hr=0x{descendingCommandResult:X8}; descending-command-compare-delta={descendingCommandCompareCount - commandCompareCount}; ascending-again-command-hr=0x{ascendingAgainCommandResult:X8}; ascending-again-command-compare-delta={ascendingAgainCommandCompareCount - descendingCommandCompareCount}; header-window={sortHeaderWindow}; first-header-compare-delta={headerFirstCompareCount - headerCompareCountBefore}; second-header-compare-delta={compareCountAfterSort - headerFirstCompareCount}; compare-delta-by-column=[{string.Join('|', columnCompareDeltas)}]; header-compare-delta-by-column=[{string.Join('|', headerCompareDeltas)}]; shell-sort-applied={sortApplied}; target=session-root; fallback=default-root; parent-visible={IsWindowVisible(source.Handle)}");
            if (IsWindowVisible(source.Handle)) throw new InvalidOperationException("The hidden Shell provider HostProbe parent became visible.");
            var parentColumnsBeforeNestedNavigation = CaptureDisplayColumns(hostType, host);
            var nestedPath = Path.Combine(pathB, "folder-small");
            var nestedIdentity = $"::{{{classId:D}}}\\edpath:{EncodePath(nestedPath)}";
            ProbeFilesystemPathRoundTrip(nestedIdentity, nestedPath);
            hostType.GetMethod("Navigate")!.Invoke(host, [nestedIdentity]);
            var navigatedState = hostType.GetProperty("CurrentPath")!;
            var hasNavigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var shellViewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? nestedCounts = null;
            try
            {
                WaitUntil(() =>
                {
                    nestedCounts = readCounts.Invoke(host, [false, false]);
                    return string.Equals((string?)navigatedState.GetValue(host), nestedIdentity, StringComparison.OrdinalIgnoreCase) &&
                        Equals(hasNavigated.GetValue(host), true) && (nint)shellViewWindow.GetValue(host)! != 0 &&
                        nestedCounts is not null && (int)nestedCounts.GetType().GetProperty("Total")!.GetValue(nestedCounts)! == 1;
                }, TimeSpan.FromSeconds(15), "the production host did not browse from the registered path root into its physical child folder");
            }
            catch (TimeoutException ex)
            {
                var actualPathAfterTimeout = currentPathProperty.GetValue(host);
                var rowsAfterTimeout = nestedCounts?.GetType().GetProperty("Total")!.GetValue(nestedCounts);
                throw new TimeoutException($"Nested navigation state: expected='{nestedIdentity}', actual='{actualPathAfterTimeout}', hasNavigated={hasNavigated.GetValue(host)}, viewWindow={shellViewWindow.GetValue(host)}, rows={rowsAfterTimeout}.", ex);
            }
            var nestedValues = ReadRecursiveValues(hostType, host, 1);
            if (nestedValues.Length != 1 || !TryReadBytes(nestedValues[0], out var nestedBytes) || nestedBytes != 5)
                throw new InvalidOperationException($"The nested path root returned an unexpected child size: [{string.Join('|', nestedValues)}].");
            if (!(bool)hostType.GetMethod("GoBack")!.Invoke(host, null)!)
                throw new InvalidOperationException("The product host did not accept Back from the nested physical folder.");
            object? restoredCounts = null;
            WaitUntil(() =>
            {
                restoredCounts = readCounts.Invoke(host, [false, false]);
                return string.Equals((string?)navigatedState.GetValue(host), targetIdentity, StringComparison.OrdinalIgnoreCase) && restoredCounts is not null &&
                    (int)restoredCounts.GetType().GetProperty("Total")!.GetValue(restoredCounts)! == expectedRows;
            }, TimeSpan.FromSeconds(15), "Back did not restore the registered parent path root and its mixed rows");
            var restoredValues = ReadRecursiveValues(hostType, host, expectedRows);
            var restoredValuesMatch = restoredValues.Select(value => TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue)
                .Order().SequenceEqual([5UL, 12UL, 29UL]);
            if (!restoredValuesMatch)
                throw new InvalidOperationException($"Back restored the parent path but not its recursive values: [{string.Join('|', restoredValues)}].");
            var parentColumnsAfterBack = CaptureDisplayColumns(hostType, host);
            if (!parentColumnsAfterBack.SequenceEqual(parentColumnsBeforeNestedNavigation, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Back did not restore the parent's visible columns: before=[{string.Join('|', parentColumnsBeforeNestedNavigation)}], after=[{string.Join('|', parentColumnsAfterBack)}].");
            Console.WriteLine($"shell-provider-nested-navigation: child-folder=True; nested-rows={nestedCounts!.GetType().GetProperty("Total")!.GetValue(nestedCounts)}; child-size={nestedBytes}; back-to-parent=True; restored-rows={restoredCounts!.GetType().GetProperty("Total")!.GetValue(restoredCounts)}; restored-values=[{string.Join('|', restoredValues)}]; parent-columns-restored=True; parent-visible={IsWindowVisible(source.Handle)}");
            return RunFilesystemSortRestoreProbe();
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else if (host is IDisposable disposable)
                disposable.Dispose();
            PumpDispatcher(TimeSpan.FromMilliseconds(150));
            if (namespaceRegistered) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistered) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            if (adapter != 0) NativeLibrary.Free(adapter);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static bool MatchesExpectedSortOrder(string[] actual, string[] ascendingOrder,
        bool descending, bool ignoreFolderSizeForSort)
    {
        if (actual.Length != ascendingOrder.Length) return false;
        if (!ignoreFolderSizeForSort)
        {
            var expected = descending ? ascendingOrder.Reverse() : ascendingOrder;
            return actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
        }

        var fileIndex = Array.FindIndex(actual, name => name.Equals("file-middle.bin", StringComparison.OrdinalIgnoreCase));
        var folders = actual.Where(name => name.StartsWith("folder-", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);
        return fileIndex == (descending ? 0 : actual.Length - 1) &&
            folders.SequenceEqual(["folder-large", "folder-small"], StringComparer.OrdinalIgnoreCase);
    }

    private static object RoundTripSessionTab(Assembly appAssembly, string profilePath, string path, object viewState,
        int expectedSortDirection)
    {
        var tabSettingsType = appAssembly.GetType("ExplorerDirs.App.TabSettings", throwOnError: true)!;
        var sessionStateType = appAssembly.GetType("ExplorerDirs.App.SessionState", throwOnError: true)!;
        var sessionStoreType = appAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
        var paneType = typeof(List<>).MakeGenericType(tabSettingsType);
        var panesType = typeof(List<>).MakeGenericType(paneType);
        var panes = (IList)Activator.CreateInstance(panesType)!;
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = (IList)Activator.CreateInstance(paneType)!;
            if (paneIndex == 0)
                tabs.Add(Activator.CreateInstance(tabSettingsType, [path, viewState, null, false, false]));
            panes.Add(tabs);
        }

        var stateConstructor = sessionStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(constructor => constructor.GetParameters().Length == 11);
        var state = stateConstructor.Invoke([4, 4u, panes, 0, new[] { 0, 0, 0, 0 }, 16,
            new[] { false, false, false, false }, null, null, null, null]);
        var store = Activator.CreateInstance(sessionStoreType, [profilePath, profilePath + ".legacy"])!;
        sessionStoreType.GetMethod("Save")!.Invoke(store, [state]);
        var reloaded = Activator.CreateInstance(sessionStoreType, [profilePath, profilePath + ".legacy"])!;
        var loadedState = sessionStoreType.GetMethod("Load")!.Invoke(reloaded, null)
            ?? throw new InvalidOperationException("The production SessionStore could not reload its written INI profile.");
        var loadedPanes = ((IEnumerable)loadedState.GetType().GetProperty("Panes")!.GetValue(loadedState)!).Cast<object>().ToArray();
        var loadedFirstPane = ((IEnumerable)loadedPanes[0]).Cast<object>().ToArray();
        if (loadedFirstPane.Length != 1) throw new InvalidOperationException("The INI round trip did not preserve the probe tab.");
        var loadedTab = loadedFirstPane[0];
        if (!string.Equals((string)loadedTab.GetType().GetProperty("Path")!.GetValue(loadedTab)!, path,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The INI round trip changed the registered Shell path identity.");

        var loadedView = loadedTab.GetType().GetProperty("View")!.GetValue(loadedTab)!;
        var sortColumns = ((IEnumerable)loadedView.GetType().GetProperty("SortColumns")!.GetValue(loadedView)!).Cast<object>().ToArray();
        var displayColumns = ((IEnumerable)loadedView.GetType().GetProperty("DisplayColumns")!.GetValue(loadedView)!).Cast<object>().ToArray();
        if ((uint)loadedView.GetType().GetProperty("Mode")!.GetValue(loadedView)! != 4 ||
            (int)loadedView.GetType().GetProperty("IconSize")!.GetValue(loadedView)! != 16 ||
            sortColumns.Length != 1 || displayColumns.Length != 2)
            throw new InvalidOperationException("The INI round trip changed the custom recursive view shape.");
        var sort = sortColumns[0];
        var sortKey = sort.GetType().GetProperty("Key")!.GetValue(sort)!;
        var sortFormat = (Guid)sortKey.GetType().GetProperty("FormatId")!.GetValue(sortKey)!;
        var sortId = (uint)sortKey.GetType().GetProperty("PropertyId")!.GetValue(sortKey)!;
        var sortDirection = (int)sort.GetType().GetProperty("Direction")!.GetValue(sort)!;
        if (sortFormat != Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC") || sortId != 12 || sortDirection != expectedSortDirection)
            throw new InvalidOperationException("The INI round trip changed the recursive sort key or direction.");
        var restoredDisplayKeys = displayColumns.Select(column =>
        {
            var key = column.GetType().GetProperty("Key")!.GetValue(column)!;
            return ((Guid)key.GetType().GetProperty("FormatId")!.GetValue(key)!,
                (uint)key.GetType().GetProperty("PropertyId")!.GetValue(key)!,
                (int)column.GetType().GetProperty("Width")!.GetValue(column)!);
        }).ToArray();
        if (restoredDisplayKeys[0] != (Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u, 180) ||
            restoredDisplayKeys[1] != (Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10u, 240))
            throw new InvalidOperationException("The INI round trip changed recursive/name column keys or widths.");
        return loadedTab;
    }

    internal static int RunSessionRecoveryChild(string profilePath, string adapterPath, Guid classId,
        string defaultRoot, bool ignoreFolderSizeForSort)
    {
        var foregroundBefore = GetForegroundWindow();
        nint adapter = 0;
        HwndSource? source = null;
        HwndHost? host = null;
        try
        {
            adapter = NativeLibrary.Load(adapterPath);
            Check(GetExport<SetClassId>(adapter, "SetSyntheticFolderClassIdForProbe")(ref classId));
            Check(GetExport<SetPath>(adapter, "SetSyntheticSortBackingPath")(defaultRoot));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticCustomSizeColumnForProbe")(1));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticRecursiveSizeScanOnDemandForProbe")(1));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticIgnoreRecursiveFolderSizeSortForProbe")(
                ignoreFolderSizeForSort ? 1 : 0));

            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var sessionStoreType = appAssembly.GetType("ExplorerDirs.App.SessionStore", throwOnError: true)!;
            var store = Activator.CreateInstance(sessionStoreType, [profilePath, profilePath + ".legacy"])!;
            var session = sessionStoreType.GetMethod("Load")!.Invoke(store, null)
                ?? throw new InvalidOperationException("The child process could not load the parent's INI session.");
            var panes = ((IEnumerable)session.GetType().GetProperty("Panes")!.GetValue(session)!).Cast<object>().ToArray();
            var tabs = ((IEnumerable)panes[0]).Cast<object>().ToArray();
            if (tabs.Length != 1) throw new InvalidOperationException("The child process did not recover the session tab.");
            var tab = tabs[0];
            var path = (string)tab.GetType().GetProperty("Path")!.GetValue(tab)!;
            var viewState = tab.GetType().GetProperty("View")!.GetValue(tab)!;
            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            host = (HwndHost)Activator.CreateInstance(hostType, path, viewState)!;
            var grid = new Grid { Width = 800, Height = 500 };
            grid.Children.Add(host);
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs cross-process session recovery probe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 800, Height = 500
            });
            source.RootVisual = grid;
            grid.Measure(new Size(800, 500));
            grid.Arrange(new Rect(0, 0, 800, 500));
            grid.UpdateLayout();

            var currentPath = hostType.GetProperty("CurrentPath")!;
            var navigated = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindow = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var readCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? counts = null;
            WaitUntil(() =>
            {
                counts = readCounts.Invoke(host, [false, false]);
                return string.Equals((string?)currentPath.GetValue(host), path, StringComparison.OrdinalIgnoreCase) &&
                    Equals(navigated.GetValue(host), true) && (nint)viewWindow.GetValue(host)! != 0 &&
                    counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == 3;
            }, TimeSpan.FromSeconds(20), "the child process did not restore the registered path into the product ExplorerBrowserHost");

            var displayColumns = CaptureDisplayColumns(hostType, host);
            if (displayColumns.Length != 2 || !displayColumns.Any(value => value.Contains("{b725f130-47ef-101a-a5f1-02608c9eebac}:12", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The child process did not restore the recursive display column: [{string.Join('|', displayColumns)}].");
            var sortColumns = CaptureSortColumns(hostType, host);
            if (sortColumns.Length != 1 ||
                !sortColumns[0].Contains("{b725f130-47ef-101a-a5f1-02608c9eebac}:12", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The child process did not restore the recursive sort key: [{string.Join('|', sortColumns)}].");
            var recursiveValues = ReadRecursiveValues(hostType, host, 3);
            if (recursiveValues.Length != 3 || recursiveValues.Any(value => !TryReadBytes(value, out _)) ||
                !recursiveValues.Select(value => TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue)
                    .Order().SequenceEqual([5UL, 12UL, 29UL]))
                throw new InvalidOperationException($"The child process loaded the path but not its recursive size values: [{string.Join('|', recursiveValues)}].");
            var sorts = ((IEnumerable)viewState.GetType().GetProperty("SortColumns")!.GetValue(viewState)!).Cast<object>().ToArray();
            var descending = (int)sorts[0].GetType().GetProperty("Direction")!.GetValue(sorts[0])! < 0;
            var capture = hostType.GetMethod("CaptureFileViewExportSnapshot",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var order = Array.Empty<string>();
            var exportColumnsForNames = Array.Empty<string>();
            WaitUntil(() =>
            {
                var (exportColumns, exportRows) = ReadExportRows(capture.Invoke(host, null)!);
                exportColumnsForNames = exportColumns;
                var nameIndex = Array.FindIndex(exportColumns, name => name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("名称", StringComparison.Ordinal));
                if (nameIndex < 0) return false;
                order = exportRows.Select(row => Path.GetFileName(row[nameIndex]) ?? row[nameIndex]).ToArray();
                return MatchesExpectedSortOrder(order, ["folder-small", "file-middle.bin", "folder-large"],
                    descending, ignoreFolderSizeForSort);
            }, TimeSpan.FromSeconds(15), $"the child process did not apply the saved recursive sort; columns=[{string.Join('|', exportColumnsForNames)}], order=[{string.Join('|', order)}]");
            if (IsWindowVisible(source.Handle) || GetForegroundWindow() != foregroundBefore)
                throw new InvalidOperationException("The cross-process recovery probe changed visibility or foreground ownership.");
            Console.WriteLine($"shell-provider-session-child: path-restored=True; rows={counts!.GetType().GetProperty("Total")!.GetValue(counts)}; recursive-values=[{string.Join('|', recursiveValues)}]; recursive-column=True; sort=[{string.Join('|', order)}]; parent-visible=False; foreground-unchanged=True");
            return 0;
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else if (host is IDisposable disposable)
                disposable.Dispose();
            PumpDispatcher(TimeSpan.FromMilliseconds(150));
            if (adapter != 0) NativeLibrary.Free(adapter);
        }
    }

    private static void LaunchSessionRecoveryChild(string profilePath, string adapterPath, Guid classId,
        string defaultRoot, bool ignoreFolderSizeForSort)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The current process path is unavailable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        startInfo.ArgumentList.Add("--shell-provider-session-child");
        startInfo.ArgumentList.Add(profilePath);
        startInfo.ArgumentList.Add(adapterPath);
        startInfo.ArgumentList.Add(classId.ToString("D"));
        startInfo.ArgumentList.Add(defaultRoot);
        startInfo.ArgumentList.Add(ignoreFolderSizeForSort.ToString());

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the session recovery child process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(45000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The cross-process session recovery probe exceeded 45 seconds.");
        }
        Task.WaitAll(standardOutput, standardError);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The cross-process session recovery probe exited {process.ExitCode}. stdout=[{standardOutput.Result}] stderr=[{standardError.Result}]");
        Console.Write(standardOutput.Result);
    }

    private static int LaunchProviderEnumerationChild(Guid classId, string adapterPath, string fallbackRoot,
        string backingPath)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The current process path is unavailable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        startInfo.ArgumentList.Add("--shell-provider-enumeration-child");
        startInfo.ArgumentList.Add(classId.ToString("D"));
        startInfo.ArgumentList.Add(adapterPath);
        startInfo.ArgumentList.Add(fallbackRoot);
        startInfo.ArgumentList.Add(backingPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the provider enumeration child process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The isolated provider enumeration exceeded 15 seconds.");
        }
        Task.WaitAll(standardOutput, standardError);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The isolated provider enumeration child exited {process.ExitCode}. stdout=[{standardOutput.Result}] stderr=[{standardError.Result}]");
        Console.Write(standardOutput.Result);
        var prefix = "provider-enumeration-child: count=";
        var line = standardOutput.Result.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The provider enumeration child returned no count.");
        var countText = line[prefix.Length..].Split(';', 2)[0];
        return int.Parse(countText, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static int RunFilesystemSortRestoreProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-HostFilesystemSort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstFile = Path.Combine(root, "a-large.bin");
        var secondFile = Path.Combine(root, "z-small.bin");
        File.WriteAllBytes(firstFile, new byte[29]);
        File.WriteAllBytes(secondFile, new byte[5]);
        HwndSource? source = null;
        HwndHost? host = null;
        try
        {
            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var propertyKeyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var sortColumnType = appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!;
            var displayColumnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var sizeKey = Activator.CreateInstance(propertyKeyType,
                Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12u)!;
            var nameKey = Activator.CreateInstance(propertyKeyType,
                Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10u)!;
            var sortColumns = Array.CreateInstance(sortColumnType, 1);
            sortColumns.SetValue(Activator.CreateInstance(sortColumnType, [sizeKey, 1]), 0);
            var displayColumns = Array.CreateInstance(displayColumnType, 2);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [nameKey, 240]), 0);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [sizeKey, 120]), 1);
            var viewStateConstructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == 6);
            var viewState = viewStateConstructor.Invoke([4u, 16, sortColumns, null, true, displayColumns]);
            var hostType = appAssembly.GetType("ExplorerDirs.App.ExplorerBrowserHost", throwOnError: true)!;
            host = (HwndHost)Activator.CreateInstance(hostType, root, viewState)!;
            var grid = new Grid { Width = 800, Height = 500 };
            grid.Children.Add(host);
            source = new HwndSource(new HwndSourceParameters("ExplorerDirs filesystem sort HostProbe")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 800, Height = 500
            });
            source.RootVisual = grid;
            grid.Measure(new Size(800, 500));
            grid.Arrange(new Rect(0, 0, 800, 500));
            grid.UpdateLayout();

            var navigatedField = hostType.GetField("_hasNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var viewWindowField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            WaitUntil(() => Equals(navigatedField.GetValue(host), true) && (nint)viewWindowField.GetValue(host)! != 0,
                TimeSpan.FromSeconds(15), "the product ExplorerBrowserHost did not navigate to the filesystem sort fixture");
            var readCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? counts = null;
            WaitUntil(() =>
            {
                counts = readCounts.Invoke(host, [false, false]);
                return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == 2;
            }, TimeSpan.FromSeconds(15), "the filesystem sort fixture did not enumerate both files");

            var capture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var (columnNames, rowsBeforeRefresh) = ReadExportRows(capture.Invoke(host, null)!);
            var nameColumnIndex = Array.FindIndex(columnNames, name => name.Equals("Name", StringComparison.OrdinalIgnoreCase) || name == "名称");
            var sizeColumnIndex = Array.FindIndex(columnNames, name => name.Equals("Size", StringComparison.OrdinalIgnoreCase) || name == "大小");
            if (nameColumnIndex < 0 || sizeColumnIndex < 0 || rowsBeforeRefresh.Length != 2)
                throw new InvalidOperationException($"The filesystem sort snapshot is incomplete: columns=[{string.Join('|', columnNames)}], rows={rowsBeforeRefresh.Length}.");
            var orderBeforeRefresh = rowsBeforeRefresh.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var sizesBeforeRefresh = rowsBeforeRefresh.Select(row => row[sizeColumnIndex]).ToArray();
            var restoredSorts = CaptureSortColumns(hostType, host);
            var refreshResult = RefreshCurrentShellView(hostType, host);
            if (refreshResult < 0) Marshal.ThrowExceptionForHR(refreshResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            var (_, rowsAfterRefresh) = ReadExportRows(capture.Invoke(host, null)!);
            var orderAfterRefresh = rowsAfterRefresh.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var expectedAscending = new[] { "z-small.bin", "a-large.bin" };
            var physicalAscendingAppliedBeforeRefresh = orderBeforeRefresh.SequenceEqual(expectedAscending, StringComparer.OrdinalIgnoreCase);
            var physicalAscendingAppliedAfterRefresh = orderAfterRefresh.SequenceEqual(expectedAscending, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"filesystem-sort-restore: rows={rowsBeforeRefresh.Length}; restored-sort=[{string.Join('|', restoredSorts)}]; size-values-before-refresh=[{string.Join('|', sizesBeforeRefresh)}]; order-before-refresh=[{string.Join('|', orderBeforeRefresh)}]; order-after-refresh=[{string.Join('|', orderAfterRefresh)}]; refresh-hr=0x{refreshResult:X8}; sort-applied-before-refresh={physicalAscendingAppliedBeforeRefresh}; sort-applied-after-refresh={physicalAscendingAppliedAfterRefresh}; parent-visible={IsWindowVisible(source.Handle)}");
            if (IsWindowVisible(source.Handle)) throw new InvalidOperationException("The hidden filesystem sort HostProbe parent became visible.");
            return physicalAscendingAppliedBeforeRefresh ? 0 : 1;
        }
        finally
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            else if (host is IDisposable disposable)
                disposable.Dispose();
            PumpDispatcher(TimeSpan.FromMilliseconds(150));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void RegisterClass(string path, string adapterPath)
    {
        if (Registry.CurrentUser.OpenSubKey(path) is not null)
            throw new InvalidOperationException("The generated Shell provider CLSID unexpectedly already exists.");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Shell provider CLSID.");
            key.SetValue(null, "ExplorerDirs HostProbe isolated Shell folder", RegistryValueKind.String);
            using var inproc = key.CreateSubKey("InprocServer32", writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary InprocServer32 registration.");
            inproc.SetValue(null, adapterPath, RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            using var shellFolder = key.CreateSubKey("ShellFolder", writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary ShellFolder attributes.");
            shellFolder.SetValue("Attributes", unchecked((int)0xA0000000), RegistryValueKind.DWord);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static void RegisterNamespace(string path)
    {
        if (Registry.CurrentUser.OpenSubKey(path) is not null)
            throw new InvalidOperationException("The generated Desktop namespace key unexpectedly already exists.");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Desktop namespace registration.");
            key.SetValue(null, "ExplorerDirs HostProbe isolated Shell folder", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static void RegisterVolatileClass(string path, string adapterPath)
    {
        if (Registry.CurrentUser.OpenSubKey(path) is not null)
            throw new InvalidOperationException("The generated product Shell CLSID unexpectedly already exists.");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path,
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create the temporary volatile Shell CLSID.");
            key.SetValue(null, "ExplorerDirs product HostProbe isolated Shell folder", RegistryValueKind.String);
            using var inproc = key.CreateSubKey("InprocServer32",
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create the temporary volatile InprocServer32 registration.");
            inproc.SetValue(null, adapterPath, RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            using var shellFolder = key.CreateSubKey("ShellFolder",
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create the temporary volatile ShellFolder attributes.");
            shellFolder.SetValue("Attributes", unchecked((int)0xA0000000), RegistryValueKind.DWord);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static void RegisterVolatileNamespace(string path)
    {
        if (Registry.CurrentUser.OpenSubKey(path) is not null)
            throw new InvalidOperationException("The generated product Desktop namespace key unexpectedly already exists.");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path,
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create the temporary volatile Desktop namespace registration.");
            key.SetValue(null, "ExplorerDirs product HostProbe isolated Shell folder", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static string EncodePath(string path) => string.Concat(path.Select(character =>
        ((ushort)character).ToString("X4", CultureInfo.InvariantCulture)));

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-x64",
                "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Build ExplorerDirs.NativeProbe's static Shell folder adapter first.");
    }

    private static string FindProductAdapterLibrary()
    {
        var forceProductAdapter = string.Equals(
            Environment.GetEnvironmentVariable("EXPLORERDIRS_HOSTPROBE_USE_PRODUCT_ADAPTER"), "1", StringComparison.Ordinal);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var diagnosticPath = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-product-diagnostics-x64",
                "ExplorerDirs.ShellNamespace.Diagnostics.dll");
            if (!forceProductAdapter && File.Exists(diagnosticPath)) return diagnosticPath;
            var path = Path.Combine(directory.FullName, "ExplorerDirs.NativeProbe", "bin", "native-product-x64",
                "ExplorerDirs.ShellNamespace.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Build ExplorerDirs.NativeProbe's product Shell folder adapter first.");
    }

    private static string ReadProductDiagnosticCounter(nint adapter, string exportName) =>
        NativeLibrary.TryGetExport(adapter, exportName, out var address)
            ? Marshal.GetDelegateForFunctionPointer<GetCounter>(address)().ToString(CultureInfo.InvariantCulture)
            : "unavailable";

    private static void ResetProductCompareDiagnostics(nint adapter)
    {
        if (NativeLibrary.TryGetExport(adapter, "ResetProductCompareDiagnostics", out var address))
            Marshal.GetDelegateForFunctionPointer<ResetProductCompareDiagnosticsDelegate>(address)();
    }

    private static string[] ReadProductCompareDiagnostics(nint adapter)
    {
        if (!NativeLibrary.TryGetExport(adapter, "GetProductCompareDiagnosticCount", out var countAddress) ||
            !NativeLibrary.TryGetExport(adapter, "GetProductCompareDiagnostic", out var recordAddress)) return [];
        var count = Marshal.GetDelegateForFunctionPointer<GetCounter>(countAddress)();
        var read = Marshal.GetDelegateForFunctionPointer<GetProductCompareDiagnostic>(recordAddress);
        var records = new string[count];
        for (var index = 0; index < count; index++)
        {
            var buffer = new StringBuilder(1024);
            Check(read(index, buffer, (uint)buffer.Capacity));
            records[index] = buffer.ToString();
        }
        return records;
    }

    private static string ReadProductDiagnosticString(nint adapter, string exportName)
    {
        if (!NativeLibrary.TryGetExport(adapter, exportName, out var address)) return "unavailable";
        var buffer = new StringBuilder(4096);
        var result = Marshal.GetDelegateForFunctionPointer<GetDiagnosticString>(address)(buffer, (uint)buffer.Capacity);
        return result >= 0 ? buffer.ToString() : $"hr=0x{result:X8}";
    }

    private static string[] ReadProductDiagnosticItemIdentities(nint adapter)
        => ReadProductDiagnosticIdentities(adapter,
            "GetProductAsyncItemNotificationIdentityCount", "GetProductAsyncItemNotificationIdentity");

    private static string[] ReadProductDiagnosticIdentities(nint adapter, string countExport, string identityExport)
    {
        if (!NativeLibrary.TryGetExport(adapter, countExport, out var countAddress) ||
            !NativeLibrary.TryGetExport(adapter, identityExport, out var identityAddress))
            return [];
        // A bounded diagnostic log must never silently masquerade as a complete notification trace.
        if (NativeLibrary.TryGetExport(adapter, identityExport + "DroppedCount", out var droppedAddress) &&
            Marshal.GetDelegateForFunctionPointer<GetCounter>(droppedAddress)() != 0)
            throw new InvalidOperationException("Notification identity trace overflowed; captured names are incomplete.");
        var count = Marshal.GetDelegateForFunctionPointer<GetCounter>(countAddress)();
        var getIdentity = Marshal.GetDelegateForFunctionPointer<GetProductAsyncItemNotificationIdentity>(identityAddress);
        var identities = new string[count];
        for (var index = 0; index < count; index++)
        {
            var buffer = new StringBuilder(4096);
            Check(getIdentity(index, buffer, (uint)buffer.Capacity));
            identities[index] = buffer.ToString();
        }
        return identities;
    }

    private static string GetProviderItemName(string identity) =>
        TryDecodeProviderBackingPath(identity, out var backingPath)
            ? Path.GetFileName(backingPath) ?? backingPath
            : identity;

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string failure)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var completed = false;
        timer.Tick += (_, _) =>
        {
            completed = condition();
            if (completed || clock.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!completed) throw new TimeoutException(failure);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static (string[] Names, int[] ShellItemCounts, int[] NativeListViewCounts, string[] NativeListViewRowSnapshots, int[] ExportRowCounts, int TransientReadErrors) ObserveProductSort(
        Type hostType, HwndHost host, MethodInfo capture, MethodInfo readRows, MethodInfo readItemCounts,
        int nameColumn, TimeSpan duration, string[]? expectedNames = null, int sampleIntervalMilliseconds = 20,
        List<string>? orderedCountTrace = null)
    {
        var shellItemCounts = new HashSet<int>();
        var nativeListViewCounts = new HashSet<int>();
        var nativeListViewRowSnapshots = new HashSet<string>(StringComparer.Ordinal);
        var exportRowCounts = new HashSet<int>();
        var transientReadErrors = 0;
        string[] names = [];
        var lastCountTuple = "";
        var viewWindowField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clock = Stopwatch.StartNew();
        do
        {
            try
            {
                var viewWindow = (nint)viewWindowField.GetValue(host)!;
                var listSnapshot = ReadNativeListViewSnapshot(viewWindow);
                nativeListViewCounts.Add(listSnapshot.Count);
                nativeListViewRowSnapshots.Add($"{listSnapshot.Count}:{string.Join('|', listSnapshot.Names)}");
                var shellCount = -1;
                var liveCounts = readItemCounts.Invoke(host, [false, false]);
                if (liveCounts is not null)
                {
                    shellCount = (int)liveCounts.GetType().GetProperty("Total")!.GetValue(liveCounts)!;
                    shellItemCounts.Add(shellCount);
                }
                var snapshot = capture.Invoke(host, null)!;
                var rows = readRows.Invoke(null, [snapshot])!;
                var orderedRows = (string[][])rows.GetType().GetField("Item2")!.GetValue(rows)!;
                names = orderedRows.Select(row => Path.GetFileName(row[nameColumn]) ?? row[nameColumn]).ToArray();
                exportRowCounts.Add(names.Length);
                var countTuple = $"{shellCount}:{listSnapshot.Count}:{names.Length}";
                if (countTuple != lastCountTuple)
                {
                    orderedCountTrace?.Add($"{clock.ElapsedMilliseconds}ms={countTuple}");
                    lastCountTuple = countTuple;
                }
            }
            catch (TargetInvocationException exception) when (IsTransientShellReadError(exception))
            {
                transientReadErrors++;
            }
            PumpDispatcher(TimeSpan.FromMilliseconds(sampleIntervalMilliseconds));
        } while (clock.Elapsed < duration && (expectedNames is null ||
            !names.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase)));
        return (names, shellItemCounts.Order().ToArray(), nativeListViewCounts.Order().ToArray(),
            nativeListViewRowSnapshots.Order(StringComparer.Ordinal).ToArray(), exportRowCounts.Order().ToArray(), transientReadErrors);
    }

    private static (string[] Names, int[] ShellItemCounts, int[] NativeListViewCounts, string[] NativeListViewRowSnapshots, int[] ExportRowCounts, int TransientReadErrors) ObserveProductLargeSortWithCountSampling(
        Type hostType, HwndHost host, MethodInfo capture, MethodInfo readRows, MethodInfo readItemCounts,
        int nameColumn, string[] expectedNames, nint adapter, List<string> countTrace)
    {
        const int expectedCount = 1002;
        var shellCounts = new HashSet<int>();
        var listViewCounts = new HashSet<int>();
        var exportCounts = new HashSet<int>();
        var nativeViewField = hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var elapsed = Stopwatch.StartNew();
        var lastCounts = string.Empty;
        var stableFullCountSamples = 0;
        string[] names = [];
        do
        {
            try
            {
                var liveCounts = readItemCounts.Invoke(host, [false, false]);
                var shellCount = liveCounts is null ? -1 :
                    (int)liveCounts.GetType().GetProperty("Total")!.GetValue(liveCounts)!;
                var listCount = ReadNativeListViewItemCount((nint)nativeViewField.GetValue(host)!);
                shellCounts.Add(shellCount);
                listViewCounts.Add(listCount);
                var compareCount = ReadProductLongCounter(adapter, "GetProductRecursiveCompareCount");
                var enumStarted = ReadProductDiagnosticCounter(adapter, "GetProductSyntheticEnumStarted");
                var enumCompleted = ReadProductDiagnosticCounter(adapter, "GetProductSyntheticEnumCompleted");
                var enumItems = ReadProductLongCounter(adapter, "GetProductSyntheticEnumItemCount");
                var enumTotalMilliseconds = ReadProductLongCounter(adapter, "GetProductSyntheticEnumTotalMilliseconds");
                var enumMaximumMilliseconds = ReadProductLongCounter(adapter, "GetProductSyntheticEnumMaximumMilliseconds");
                var counts = $"{shellCount}/{listCount}";
                var transition = $"{counts}/compare-{FormatProbeCounter(compareCount)}/enum={enumStarted}/{enumCompleted}/items-{FormatProbeCounter(enumItems)}/enum-ms={FormatProbeCounter(enumTotalMilliseconds)}/{FormatProbeCounter(enumMaximumMilliseconds)}";
                if (!string.Equals(counts, lastCounts, StringComparison.Ordinal))
                {
                    countTrace.Add($"{elapsed.ElapsedMilliseconds}ms={transition}");
                    lastCounts = counts;
                }

                if (shellCount == expectedCount && listCount == expectedCount)
                {
                    stableFullCountSamples++;
                    if (stableFullCountSamples >= 5)
                    {
                        // Read full Shell row data only after the cheap count samples settle, so the observer
                        // does not repeatedly enumerate all 1,002 rows while DefView is rebuilding them.
                        var snapshot = capture.Invoke(host, null)!;
                        var export = readRows.Invoke(null, [snapshot])!;
                        var rows = (string[][])export.GetType().GetField("Item2")!.GetValue(export)!;
                        names = rows.Select(row => Path.GetFileName(row[nameColumn]) ?? row[nameColumn]).ToArray();
                        exportCounts.Add(names.Length);
                        if (names.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase)) break;
                        stableFullCountSamples = 0;
                    }
                }
                else stableFullCountSamples = 0;
            }
            catch (TargetInvocationException exception) when (IsTransientShellReadError(exception)) { }
            PumpDispatcher(TimeSpan.FromMilliseconds(20));
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(20));

        return (names, shellCounts.Order().ToArray(), listViewCounts.Order().ToArray(), [],
            exportCounts.Order().ToArray(), 0);
    }

    private static long? ReadProductLongCounter(nint adapter, string exportName) =>
        NativeLibrary.TryGetExport(adapter, exportName, out var address)
            ? Marshal.GetDelegateForFunctionPointer<GetCounter64>(address)()
            : null;

    private static string FormatProbeCounter(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";

    private static (int[] ShellItemCounts, int[] ExportRowCounts, int FinalShellItems, int FinalExportRows) ObserveProductRowCounts(
        Type hostType, HwndHost host, TimeSpan duration)
    {
        var readItemCounts = hostType.GetMethod("ReadItemCounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var capture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var readRows = typeof(ShellProviderLocationProbe).GetMethod("ReadExportRows", BindingFlags.Static | BindingFlags.NonPublic)!;
        var shellItemCounts = new HashSet<int>();
        var exportRowCounts = new HashSet<int>();
        var clock = Stopwatch.StartNew();
        var finalShellItems = -1;
        var finalExportRows = -1;
        do
        {
            var counts = readItemCounts.Invoke(host, [false, false]);
            if (counts is not null)
            {
                finalShellItems = (int)counts.GetType().GetProperty("Total")!.GetValue(counts)!;
                shellItemCounts.Add(finalShellItems);
            }
            try
            {
                var snapshot = capture.Invoke(host, null)!;
                var rows = readRows.Invoke(null, [snapshot])!;
                finalExportRows = ((string[][])rows.GetType().GetField("Item2")!.GetValue(rows)!).Length;
                exportRowCounts.Add(finalExportRows);
            }
            catch (TargetInvocationException exception) when (IsTransientShellReadError(exception)) { }
            PumpDispatcher(TimeSpan.FromMilliseconds(20));
        } while (clock.Elapsed < duration);
        return (shellItemCounts.Order().ToArray(), exportRowCounts.Order().ToArray(), finalShellItems, finalExportRows);
    }

    private static bool IsTransientShellReadError(TargetInvocationException exception) =>
        exception.InnerException?.HResult == unchecked((int)0x80070057);

    private static void ReportSortExperiment(string strategy, string[] initialOrder, bool shellAutoResorted,
        int actionResult, (string[] Names, int[] ShellItemCounts, int[] NativeListViewCounts, string[] NativeListViewRowSnapshots, int[] ExportRowCounts, int TransientReadErrors) observation,
        Guid classId)
    {
        var expectedAscending = new[] { "folder-small", "folder-large" };
        Console.WriteLine($"shell-provider-product-async-sort: strategy={strategy}; product-dll=True; volatile-registration=True; hidden-shell-view=True; recursive-values=[29|5]; initial-order=[{string.Join('|', initialOrder)}]; shell-auto-resorted={shellAutoResorted}; action-hr=0x{actionResult:X8}; final-order=[{string.Join('|', observation.Names)}]; candidate-ordered={observation.Names.SequenceEqual(expectedAscending, StringComparer.OrdinalIgnoreCase)}; shell-item-counts=[{string.Join('|', observation.ShellItemCounts)}]; native-listview-item-counts=[{string.Join('|', observation.NativeListViewCounts)}]; native-listview-row-snapshots=[{string.Join(" || ", observation.NativeListViewRowSnapshots)}]; export-row-counts=[{string.Join('|', observation.ExportRowCounts)}]; transient-empty-shell-view={observation.ShellItemCounts.Contains(0)}; transient-empty-listview={observation.NativeListViewCounts.Contains(0)}; transient-empty-export={observation.ExportRowCounts.Contains(0)}; transient-read-errors={observation.TransientReadErrors}; class={classId:D}; foreground-unchanged=True");
    }

    private static int InvokeProductSetSorting(Type hostType, HwndHost host, Array sortColumns)
    {
        var getView = hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var view = (nint)getView.Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The product host returned no IFolderView2 for the SetSortColumns probe.");
        try
        {
            var targetType = hostType.GetNestedType("NativeViewStateTarget", BindingFlags.NonPublic)!;
            var constructor = targetType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.GetParameters().Length == 3);
            var policyCallback = Delegate.CreateDelegate(constructor.GetParameters()[2].ParameterType, host,
                hostType.GetMethod("UpdateFolderSizeSortPolicy", BindingFlags.Instance | BindingFlags.NonPublic)!);
            var target = constructor.Invoke([view, new Func<bool>(() => true), policyCallback]);
            var setSorting = targetType.GetMethod("SetSorting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            return (int)setSorting.Invoke(target, [sortColumns])!;
        }
        finally { Marshal.Release(view); }
    }

    private static T GetExport<T>(nint module, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));

    private static bool IsWindowVisible(nint window)
    {
        return window != 0 && IsWindowVisibleNative(window);
    }

    private static string[] ReadRecursiveValues(Type hostType, HwndHost host, int count)
    {
        var getView = hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var view = (nint)getView.Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The product host did not expose its current IFolderView2.");
        try
        {
            var shellFolder2Id = new Guid("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
            Check(GetMethod<GetFolder>(view, 5)(view, ref shellFolder2Id, out var folder));
            if (folder == 0) throw new InvalidOperationException("The product Shell view returned no IShellFolder2.");
            try
            {
                var key = new NativePropertyKey(new Guid("7E1F84B7-C052-4E51-8A3C-869254A6B910"), 1);
                var getItem = GetMethod<GetItem>(view, 6);
                var getDetailsEx = GetMethod<GetDetailsEx>(folder, 17);
                var values = new string[count];
                for (var index = 0; index < count; index++)
                {
                    var itemResult = getItem(view, index, out var pidl);
                    if (itemResult == unchecked((int)0x80004005) ||
                        itemResult == unchecked((int)0x80070057) ||
                        itemResult == unchecked((int)0x8000000B))
                    {
                        // Shell item indexes can transiently change while a large async view is filling.
                        values[index] = string.Empty;
                        continue;
                    }
                    Check(itemResult);
                    try
                    {
                        var variant = Marshal.AllocCoTaskMem(IntPtr.Size == 8 ? 24 : 16);
                        try
                        {
                            Marshal.WriteInt16(variant, 0);
                            var result = getDetailsEx(folder, pidl, ref key, variant);
                            if (result == unchecked((int)0x80004005) || result == unchecked((int)0x8000000B))
                            {
                                // The product provider uses E_FAIL to mark an async size row not cached yet.
                                values[index] = string.Empty;
                                continue;
                            }
                            Check(result);
                            values[index] = Marshal.GetObjectForNativeVariant(variant)?.ToString() ?? string.Empty;
                        }
                        finally
                        {
                            _ = PropVariantClear(variant);
                            Marshal.FreeCoTaskMem(variant);
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(pidl); }
                }
                return values;
            }
            finally { Marshal.Release(folder); }
        }
        finally { Marshal.Release(view); }
    }

    private static (NativePropertyKey DataKey, short CompareResult) ReadProviderColumnContract(Type hostType, HwndHost host)
    {
        var viewPointer = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (viewPointer == 0) throw new InvalidOperationException("The product host did not expose its current IFolderView2 for provider comparison.");
        try
        {
            var shellFolder2Id = new Guid("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
            Check(GetMethod<GetFolder>(viewPointer, 5)(viewPointer, ref shellFolder2Id, out var folder));
            if (folder == 0) throw new InvalidOperationException("The product Shell view returned no IShellFolder2 for provider comparison.");
            try
            {
                var mapResult = GetMethod<MapColumnToScid>(folder, 19)(folder, 1, out var dataKey);
                Check(mapResult);
                var getItem = GetMethod<GetItem>(viewPointer, 6);
                Check(getItem(viewPointer, 0, out var left));
                Check(getItem(viewPointer, 1, out var right));
                try
                {
                    var compare = GetMethod<CompareIds>(folder, 7)(folder, 1, left, right);
                    return (dataKey, unchecked((short)(compare & 0xffff)));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(left);
                    Marshal.FreeCoTaskMem(right);
                }
            }
            finally { Marshal.Release(folder); }
        }
        finally { Marshal.Release(viewPointer); }
    }

    private static (string[] Columns, string[][] Rows) ReadExportRows(object snapshot)
    {
        var type = snapshot.GetType();
        var columns = ((IEnumerable)type.GetProperty("Columns")!.GetValue(snapshot)!)
            .Cast<object>().Select(value => value.ToString() ?? string.Empty).ToArray();
        var rows = ((IEnumerable)type.GetProperty("Rows")!.GetValue(snapshot)!)
            .Cast<IEnumerable>().Select(row => row.Cast<object?>().Select(value => value?.ToString() ?? string.Empty).ToArray())
            .ToArray();
        return (columns, rows);
    }

    private static string[] CaptureSortColumns(Type hostType, HwndHost host)
    {
        var state = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        return ((IEnumerable)state.GetType().GetProperty("SortColumns")!.GetValue(state)!)
            .Cast<object>().Select(sort =>
            {
                var key = sort.GetType().GetProperty("Key")!.GetValue(sort)!;
                return $"{key.GetType().GetProperty("FormatId")!.GetValue(key)}:{key.GetType().GetProperty("PropertyId")!.GetValue(key)}/{sort.GetType().GetProperty("Direction")!.GetValue(sort)}";
            }).ToArray();
    }

    private static string[] CaptureDisplayColumns(Type hostType, HwndHost host)
    {
        var state = hostType.GetMethod("CaptureViewState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        return ((IEnumerable)state.GetType().GetProperty("DisplayColumns")!.GetValue(state)!)
            .Cast<object>().Select(column =>
            {
                var key = column.GetType().GetProperty("Key")!.GetValue(column)!;
                return $"{key.GetType().GetProperty("FormatId")!.GetValue(key)}:{key.GetType().GetProperty("PropertyId")!.GetValue(key)}";
            }).ToArray();
    }

    private static string[] ReadNativeHeaderTexts(nint header, int count)
    {
        var textBuffer = Marshal.AllocHGlobal(512);
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeHeaderItem>());
        try
        {
            var names = new string[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt16(textBuffer, 0);
                Marshal.StructureToPtr(new NativeHeaderItem
                {
                    Mask = 2, // HDI_TEXT.
                    Text = textBuffer,
                    TextLength = 256
                }, itemPointer, false);
                if (SendMessageW(header, 0x120B, index, itemPointer) == 0) // HDM_GETITEMW.
                    throw new InvalidOperationException($"Could not read native header item {index}.");
                names[index] = Marshal.PtrToStringUni(textBuffer) ?? string.Empty;
            }
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(itemPointer);
            Marshal.FreeHGlobal(textBuffer);
        }
    }

    private static string[] ReadNativeHeaderVisualTexts(nint header, string[] logicalTexts)
    {
        var pointer = Marshal.AllocHGlobal(sizeof(int) * logicalTexts.Length);
        try
        {
            var listView = GetParent(header);
            if (listView == 0 || SendMessageW(listView, 0x103B, logicalTexts.Length, pointer) == 0) // LVM_GETCOLUMNORDERARRAY.
                throw new InvalidOperationException("Could not read the native header's visual column order.");
            var order = new int[logicalTexts.Length];
            Marshal.Copy(pointer, order, 0, order.Length);
            if (order.Any(index => index < 0 || index >= logicalTexts.Length) || order.Distinct().Count() != order.Length)
                throw new InvalidOperationException($"The native header returned an invalid order array: [{string.Join('|', order)}].");
            return order.Select(index => logicalTexts[index]).ToArray();
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static bool SetNativeHeaderOrder(nint header, int[] order)
    {
        var pointer = Marshal.AllocHGlobal(sizeof(int) * order.Length);
        try
        {
            Marshal.Copy(order, 0, pointer, order.Length);
            var headerResult = SendMessageW(header, 0x1203, order.Length, pointer); // HDM_SETORDERARRAY.
            var listView = GetParent(header);
            var listViewResult = listView == 0 ? 0 : SendMessageW(listView, 0x103A, order.Length, pointer); // LVM_SETCOLUMNORDERARRAY.
            return headerResult != 0 || listViewResult != 0;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void DragHeaderColumnRight(nint header)
    {
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            if (SendMessageW(header, 0x1207, 0, rectanglePointer) == 0) throw new InvalidOperationException("Could not read first header rectangle.");
            var first = Marshal.PtrToStructure<NativeRect>(rectanglePointer);
            if (SendMessageW(header, 0x1207, 1, rectanglePointer) == 0) throw new InvalidOperationException("Could not read second header rectangle.");
            var second = Marshal.PtrToStructure<NativeRect>(rectanglePointer);
            var startX = (first.Left + first.Right) / 2;
            var y = (first.Top + first.Bottom) / 2;
            var endX = Math.Min(second.Right - 1, second.Left + 4);
            var start = unchecked((nint)((y << 16) | (startX & 0xffff)));
            var end = unchecked((nint)((y << 16) | (endX & 0xffff)));
            _ = SendMessageW(header, 0x0200, 0, start);
            _ = SendMessageW(header, 0x0201, 1, start);
            _ = SendMessageW(header, 0x0200, 1, end);
            _ = SendMessageW(header, 0x0200, 1, end);
            _ = SendMessageW(header, 0x0202, 0, end);
        }
        finally { Marshal.FreeHGlobal(rectanglePointer); }
    }

    private static (string Header, string[] Values) ReadRecursiveDisplayText(nint adapter, Type hostType, HwndHost host, int count)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The product host returned no view for recursive display text.");
        try
        {
            var folderId = new Guid("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
            Check(GetMethod<GetFolder>(view, 5)(view, ref folderId, out var folder));
            if (folder == 0) throw new InvalidOperationException("The product view returned no IShellFolder2 for display text.");
            try
            {
                var detailsSize = checked((int)GetExport<GetShellDetailsSizeNative>(adapter, "GetStaticShellDetailsSize")());
                var details = Marshal.AllocCoTaskMem(detailsSize);
                try
                {
                    var values = new string[count];
                    var getDetails = GetMethod<GetDetailsOf>(folder, 18);
                    Marshal.Copy(new byte[detailsSize], 0, details, detailsSize);
                    Check(getDetails(folder, 0, 1, details));
                    var header = ReadShellDetailsText(details);
                    var getItem = GetMethod<GetItem>(view, 6);
                    for (var index = 0; index < count; index++)
                    {
                        Marshal.Copy(new byte[detailsSize], 0, details, detailsSize);
                        Check(getItem(view, index, out var pidl));
                        try
                        {
                            Check(getDetails(folder, pidl, 1, details));
                            values[index] = ReadShellDetailsText(details);
                        }
                        finally { Marshal.FreeCoTaskMem(pidl); }
                    }
                    return (header, values);
                }
                finally { Marshal.FreeCoTaskMem(details); }
            }
            finally { Marshal.Release(folder); }
        }
        finally { Marshal.Release(view); }
    }

    private static (string[] Columns, int[] Ordinals, string[] ShellHeaders, string[][] Rows) ReadVisibleShellDetailsSnapshot(
        nint adapter, Type hostType, HwndHost host, string[] visibleHeaders, int rowCount)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The product host returned no view for visible Shell details export.");
        try
        {
            var folderId = new Guid("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
            Check(GetMethod<GetFolder>(view, 5)(view, ref folderId, out var folder));
            if (folder == 0) throw new InvalidOperationException("The product view returned no IShellFolder2 for visible Shell details export.");
            try
            {
                var detailsSize = checked((int)GetExport<GetShellDetailsSizeNative>(adapter, "GetStaticShellDetailsSize")());
                var details = Marshal.AllocCoTaskMem(detailsSize);
                try
                {
                    var getDetails = GetMethod<GetDetailsOf>(folder, 18);
                    var shellHeaders = new List<string>();
                    for (uint ordinal = 0; ordinal < 16; ordinal++)
                    {
                        Marshal.Copy(new byte[detailsSize], 0, details, detailsSize);
                        var result = getDetails(folder, 0, ordinal, details);
                        if (result < 0) break;
                        shellHeaders.Add(ReadShellDetailsText(details));
                    }
                    if (shellHeaders.Count == 0) throw new InvalidOperationException("The provider exposed no Shell details columns.");
                    var ordinals = new int[visibleHeaders.Length];
                    for (var index = 0; index < visibleHeaders.Length; index++)
                    {
                        var matches = shellHeaders.Select((header, ordinal) => (header, ordinal))
                            .Where(pair => string.Equals(pair.header, visibleHeaders[index], StringComparison.Ordinal))
                            .Select(pair => pair.ordinal).ToArray();
                        if (matches.Length != 1)
                            throw new InvalidOperationException($"Visible header '{visibleHeaders[index]}' maps to {matches.Length} GetDetailsOf columns: [{string.Join('|', shellHeaders)}].");
                        ordinals[index] = matches[0];
                    }

                    var rows = new string[rowCount][];
                    var getItem = GetMethod<GetItem>(view, 6);
                    for (var row = 0; row < rowCount; row++)
                    {
                        Check(getItem(view, row, out var pidl));
                        try
                        {
                            var values = new string[ordinals.Length];
                            for (var column = 0; column < ordinals.Length; column++)
                            {
                                Marshal.Copy(new byte[detailsSize], 0, details, detailsSize);
                                Check(getDetails(folder, pidl, (uint)ordinals[column], details));
                                values[column] = ReadShellDetailsText(details);
                            }
                            rows[row] = values;
                        }
                        finally { Marshal.FreeCoTaskMem(pidl); }
                    }
                    return (visibleHeaders, ordinals, shellHeaders.ToArray(), rows);
                }
                finally { Marshal.FreeCoTaskMem(details); }
            }
            finally { Marshal.Release(folder); }
        }
        finally { Marshal.Release(view); }
    }

    private static string ReadShellDetailsText(nint details)
    {
        var strretOffset = 8;
        if (Marshal.ReadInt32(details, strretOffset) != 0)
            throw new InvalidOperationException("The synthetic Shell provider returned a non-WSTR STRRET.");
        var pointer = Marshal.ReadIntPtr(details, strretOffset + (IntPtr.Size == 8 ? 8 : 4));
        if (pointer == 0) return string.Empty;
        try { return Marshal.PtrToStringUni(pointer) ?? string.Empty; }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static int InvokeBackgroundShellVerb(Type hostType, HwndHost host, string expectedVerb)
    {
        var assembly = hostType.Assembly;
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var browserArguments = new object?[] { shellViewId, null };
        var browserResult = (int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, browserArguments)!;
        Check(browserResult);
        var shellView = browserArguments[1] ?? throw new InvalidOperationException("The product browser returned no IShellView.");
        try
        {
            var shellViewInterface = assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost+IShellView", true)!;
            var contextMenuId = new Guid("000214E4-0000-0000-C000-000000000046");
            var viewArguments = new object?[] { 0u, contextMenuId, null };
            var viewResult = (int)shellViewInterface.GetMethod("GetItemObject")!.Invoke(shellView, viewArguments)!;
            Check(viewResult);
            var contextObject = viewArguments[2] ?? throw new InvalidOperationException("The product Shell view returned no background IContextMenu.");
            try
            {
                var unknown = Marshal.GetIUnknownForObject(contextObject);
                nint context = 0;
                try
                {
                    Check(Marshal.QueryInterface(unknown, in contextMenuId, out context));
                }
                finally { Marshal.Release(unknown); }
                try
                {
                    var menu = CreatePopupMenu();
                    if (menu == 0) throw new InvalidOperationException("Could not create the hidden Shell command menu.");
                    try
                    {
                        var queryResult = GetMethod<QueryContextMenu>(context, 3)(context, menu, 0, 1, 0x7fff, 0x10 | 0x800);
                        Check(queryResult);
                        var commandCount = Math.Min(queryResult & 0xffff, 0x7fff);
                        var buffer = Marshal.AllocHGlobal(512);
                        try
                        {
                            uint? commandOffset = null;
                            for (uint offset = 0; offset < commandCount; offset++)
                            {
                                Marshal.WriteInt16(buffer, 0);
                                if (GetMethod<GetCommandString>(context, 5)(context, offset, 4, 0, buffer, 256) == 0 &&
                                    string.Equals(Marshal.PtrToStringUni(buffer), expectedVerb, StringComparison.OrdinalIgnoreCase))
                                {
                                    commandOffset = offset;
                                    break;
                                }
                            }
                            if (commandOffset is null)
                                throw new InvalidOperationException($"The product Shell view did not expose the '{expectedVerb}' canonical command.");
                            var invoke = new NativeInvokeCommand
                            {
                                Size = (uint)Marshal.SizeOf<NativeInvokeCommand>(),
                                Verb = (nint)commandOffset.Value,
                                Show = 1
                            };
                            return GetMethod<InvokeCommand>(context, 4)(context, ref invoke);
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                    }
                    finally { DestroyMenu(menu); }
                }
                finally { Marshal.Release(context); }
            }
            finally
            {
                if (Marshal.IsComObject(contextObject)) Marshal.ReleaseComObject(contextObject);
            }
        }
        finally
        {
            if (Marshal.IsComObject(shellView)) Marshal.ReleaseComObject(shellView);
        }
    }

    private static int RefreshCurrentShellView(Type hostType, HwndHost host)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var arguments = new object?[] { shellViewId, null };
        Check((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, arguments)!);
        var shellView = arguments[1] ?? throw new InvalidOperationException("The product browser returned no IShellView for refresh.");
        try
        {
            var interfaceType = hostType.Assembly.GetType("ExplorerDirs.App.ExplorerBrowserHost+IShellView", true)!;
            return (int)interfaceType.GetMethod("Refresh")!.Invoke(shellView, null)!;
        }
        finally
        {
            if (Marshal.IsComObject(shellView)) Marshal.ReleaseComObject(shellView);
        }
    }

    private static nint FindSortHeader(nint viewWindow)
    {
        var header = SendMessageW(FindNativeListView(viewWindow), 0x101F, 0, 0); // LVM_GETHEADER.
        if (header == 0) throw new InvalidOperationException("The product ExplorerBrowser list view did not expose its native header.");
        var count = SendMessageW(header, 0x1200, 0, 0); // HDM_GETITEMCOUNT.
        if (count < 2) throw new InvalidOperationException($"The product Shell header exposed only {count} column(s); the recursive-size header is missing.");
        return header;
    }

    private static int ReadNativeListViewItemCount(nint viewWindow) =>
        checked((int)SendMessageW(FindNativeListView(viewWindow), 0x1004, 0, 0)); // LVM_GETITEMCOUNT.

    private static nint FindNativeListView(nint viewWindow)
    {
        nint listView = 0;
        EnumChildWindows(viewWindow, (window, _) =>
        {
            var className = new StringBuilder(128);
            _ = GetClassName(window, className, className.Capacity);
            if (string.Equals(className.ToString(), "SysListView32", StringComparison.Ordinal))
            {
                listView = window;
                return false;
            }
            return true;
        }, 0);
        if (listView == 0) throw new InvalidOperationException("The product ExplorerBrowser view did not expose a SysListView32.");
        return listView;
    }

    private static (int Count, string[] Names) ReadNativeListViewSnapshot(nint viewWindow)
    {
        var listView = FindNativeListView(viewWindow);
        // Pair the native count and text reads so a transient reorder cannot make separate samples appear contradictory.
        var count = checked((int)SendMessageW(listView, 0x1004, 0, 0)); // LVM_GETITEMCOUNT.
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        var textPointer = Marshal.AllocHGlobal(1024);
        try
        {
            var names = new string[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt32(textPointer, 0);
                var item = new NativeListViewItem { Item = index, Text = textPointer, TextCapacity = 512 };
                Marshal.StructureToPtr(item, itemPointer, false);
                _ = SendMessageW(listView, 0x1073, index, itemPointer); // LVM_GETITEMTEXTW.
                names[index] = Marshal.PtrToStringUni(textPointer) ?? string.Empty;
            }
            return (count, names);
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(itemPointer);
        }
    }

    internal static bool IsNativeGroupingEnabled(nint viewWindow) => SendMessageW(FindNativeListView(viewWindow), 0x10AF, 0, 0) != 0;

    internal static string ReadNativeGroupingState(nint viewWindow)
    {
        var list = FindNativeListView(viewWindow);
        var cells = ReadNativeListViewCells(viewWindow, 3);
        return $"enabled={SendMessageW(list, 0x10AF, 0, 0)}; count={SendMessageW(list, 0x1098, 0, 0)}; cells={string.Join("/", cells.Select(row => string.Join("|", row)))}";
    }

    internal static int[] ReadNativeGroupIds(nint viewWindow)
    {
        var list = FindNativeListView(viewWindow);
        var count = checked((int)SendMessageW(list, 0x1004, 0, 0));
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            var groups = new int[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(new NativeListViewItem { Mask = 0x100, Item = index, GroupId = -1 }, pointer, false);
                if (SendMessageW(list, 0x104B, 0, pointer) == 0)
                    throw new InvalidOperationException("Native grouped row identity was not readable.");
                groups[index] = Marshal.PtrToStructure<NativeListViewItem>(pointer).GroupId;
            }
            return groups;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    internal static string[][] ReadNativeListViewCells(nint viewWindow, int subItemCount)
    {
        var listView = FindNativeListView(viewWindow);
        var count = checked((int)SendMessageW(listView, 0x1004, 0, 0)); // LVM_GETITEMCOUNT.
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        var textPointer = Marshal.AllocHGlobal(1024);
        try
        {
            var rows = new string[count][];
            for (var itemIndex = 0; itemIndex < count; itemIndex++)
            {
                var cells = new string[subItemCount];
                for (var subItem = 0; subItem < subItemCount; subItem++)
                {
                    Marshal.WriteInt32(textPointer, 0);
                    var item = new NativeListViewItem { Item = itemIndex, SubItem = subItem, Text = textPointer, TextCapacity = 512 };
                    Marshal.StructureToPtr(item, itemPointer, false);
                    _ = SendMessageW(listView, 0x1073, itemIndex, itemPointer); // LVM_GETITEMTEXTW.
                    cells[subItem] = Marshal.PtrToStringUni(textPointer) ?? string.Empty;
                }
                rows[itemIndex] = cells;
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
            Marshal.FreeHGlobal(itemPointer);
        }
    }

    private static (string[] Selected, string? Focused) ReadNativeListViewSelectionState(nint viewWindow)
    {
        var listView = FindNativeListView(viewWindow);
        var rows = ReadNativeListViewCells(viewWindow, 1);
        var selected = new List<string>();
        string? focused = null;
        for (var index = 0; index < rows.Length; index++)
        {
            const int selectedAndFocused = 0x0001 | 0x0002;
            var state = (int)SendMessageW(listView, 0x102C, index, selectedAndFocused); // LVM_GETITEMSTATE.
            if ((state & 0x0002) != 0) selected.Add(rows[index][0]);
            if ((state & 0x0001) != 0) focused = rows[index][0];
        }
        return (selected.ToArray(), focused);
    }

    private static string? ReadShellFocusedRow(Type hostType, HwndHost host)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The product host returned no IFolderView2 for focused-row readback.");
        try
        {
            var result = GetMethod<GetFocusedFolderViewItem>(view, 10)(view, out var index);
            Check(result);
            var rows = ReadNativeListViewCells((nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!, 1);
            return (uint)index < (uint)rows.Length ? rows[index][0] : null;
        }
        finally { Marshal.Release(view); }
    }

    private static void SelectShellViewItemByPath(Type hostType, HwndHost host, string itemPath)
        => SelectShellViewItemsByPath(hostType, host, [itemPath]);

    private static void SelectShellViewItemsByPath(Type hostType, HwndHost host, string[] itemPaths)
    {
        if (itemPaths.Length == 0) throw new ArgumentException("At least one selected item is required.", nameof(itemPaths));
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new InvalidOperationException("The product host returned no IFolderView2 for selection setup.");
        var primaryItemName = Path.GetFileName(itemPaths[0]);
        try
        {
            var rows = ReadNativeListViewCells((nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!, 1);
            var selectItem = GetMethod<SelectFolderViewItem>(view, 15);
            for (var itemNumber = 0; itemNumber < itemPaths.Length; itemNumber++)
            {
                var itemPath = itemPaths[itemNumber];
                var itemName = Path.GetFileName(itemPath);
                var itemIndex = Array.FindIndex(rows, row => row[0].Equals(itemName, StringComparison.OrdinalIgnoreCase));
                if (itemIndex < 0) throw new InvalidOperationException($"Could not find '{itemPath}' in the native view rows.");
                // The input-recovery probe exposed that 0x1f includes SVSI_EDIT (0x3).
                // Selection setup must select/focus, without accidentally starting rename.
                Check(selectItem(view, itemIndex, itemNumber == 0 ? 0x1du : 0x1u));
            }
        }
        finally { Marshal.Release(view); }
        SetNativeListViewFocus((nint)hostType.GetField("_viewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!, primaryItemName);
    }

    private static void SetNativeListViewFocus(nint viewWindow, string itemName)
    {
        var listView = FindNativeListView(viewWindow);
        var rows = ReadNativeListViewCells(viewWindow, 1);
        var itemIndex = Array.FindIndex(rows, row => row[0].Equals(itemName, StringComparison.OrdinalIgnoreCase));
        if (itemIndex < 0) throw new InvalidOperationException($"Could not find native ListView row '{itemName}' to focus.");
        var itemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeListViewItem>());
        try
        {
            var item = new NativeListViewItem { Mask = 0x0008, State = 0x0001, StateMask = 0x0001 }; // LVIF_STATE / LVIS_FOCUSED.
            Marshal.StructureToPtr(item, itemPointer, false);
            _ = SendMessageW(listView, 0x102B, itemIndex, itemPointer); // LVM_SETITEMSTATE.
        }
        finally { Marshal.FreeHGlobal(itemPointer); }
    }

    private sealed class NativePaintMessageProbe : IDisposable
    {
        private static int _nextSubclassId;
        private readonly nint _window;
        private readonly nuint _subclassId;
        private readonly SubclassProc _callback;
        private GCHandle _self;
        private readonly List<string> _messages = [];

        public NativePaintMessageProbe(nint window)
        {
            _window = window;
            _subclassId = unchecked((nuint)Interlocked.Increment(ref _nextSubclassId));
            _callback = OnMessage;
            _self = GCHandle.Alloc(this);
            if (!SetWindowSubclass(_window, _callback, _subclassId, GCHandle.ToIntPtr(_self)))
            {
                _self.Free();
                throw new InvalidOperationException($"Could not observe native ListView paint messages: {Marshal.GetLastWin32Error()}.");
            }
        }

        public IReadOnlyList<string> Messages => _messages;

        public void Clear() => _messages.Clear();

        private nint OnMessage(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nint referenceData)
        {
            if (message is 0x000F or 0x0014) // WM_PAINT / WM_ERASEBKGND.
            {
                var count = SendMessageW(window, 0x1004, 0, 0); // LVM_GETITEMCOUNT.
                _messages.Add($"0x{message:X}:rows={count}");
            }
            return DefSubclassProc(window, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (_self.IsAllocated)
            {
                _ = RemoveWindowSubclass(_window, _callback, _subclassId);
                _self.Free();
            }
        }
    }

    private static void ClickHeaderColumn(nint header, int columnIndex)
    {
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            if (SendMessageW(header, 0x1207, columnIndex, rectanglePointer) == 0) // HDM_GETITEMRECT.
                throw new InvalidOperationException($"Could not read the product Shell header column {columnIndex} rectangle.");
            var rectangle = Marshal.PtrToStructure<NativeRect>(rectanglePointer);
            var x = (rectangle.Left + rectangle.Right) / 2;
            var y = (rectangle.Top + rectangle.Bottom) / 2;
            var coordinates = unchecked((nint)((y << 16) | (x & 0xffff)));
            _ = SendMessageW(header, 0x0200, 0, coordinates); // WM_MOUSEMOVE.
            _ = SendMessageW(header, 0x0201, 1, coordinates); // WM_LBUTTONDOWN.
            _ = SendMessageW(header, 0x0202, 0, coordinates); // WM_LBUTTONUP.
        }
        finally { Marshal.FreeHGlobal(rectanglePointer); }
    }

    private static T GetMethod<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private static void ProbeFilesystemPathRoundTrip(string parsingName, string expectedBackingPath)
    {
        // Profile-specific provider IDs are useful only if portable exports can avoid persisting them.
        var parseResult = SHParseDisplayName(parsingName, 0, out var pidl, 0, out _);
        if (parseResult < 0 || pidl == 0)
            throw new InvalidOperationException($"Could not parse the probe provider path: 0x{parseResult:X8}.");
        try
        {
            var pathResult = SHGetNameFromIDList(pidl, 0x80058000, out var pathPointer);
            try
            {
                var path = pathResult >= 0 && pathPointer != 0 ? Marshal.PtrToStringUni(pathPointer) : null;
                var isProviderIdentity = path?.StartsWith("::{", StringComparison.Ordinal) == true;
                var hasEncodedPath = TryDecodeProviderBackingPath(path, out var encodedBackingPath);
                var backingPathMatches = hasEncodedPath && string.Equals(encodedBackingPath, expectedBackingPath, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"shell-provider-filesystem-name: hr=0x{pathResult:X8}; returned-provider-identity={isProviderIdentity}; encoded-backing-path-matches={backingPathMatches}");
                if (pathResult < 0 || !isProviderIdentity || !backingPathMatches)
                    throw new InvalidOperationException("The provider identity did not round-trip to its expected backing filesystem path.");
            }
            finally
            {
                if (pathPointer != 0) Marshal.FreeCoTaskMem(pathPointer);
            }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static bool TryDecodeProviderBackingPath(string? identity, out string path)
    {
        path = string.Empty;
        const string marker = "\\edpath:";
        var markerIndex = identity?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (markerIndex < 0) return false;
        var token = identity![(markerIndex + marker.Length)..];
        if (token.Length == 0 || token.Length % 4 != 0) return false;
        var decoded = new StringBuilder(token.Length / 4);
        for (var index = 0; index < token.Length; index += 4)
        {
            if (!ushort.TryParse(token.AsSpan(index, 4), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var character))
                return false;
            decoded.Append((char)character);
        }
        path = decoded.ToString();
        return Path.IsPathFullyQualified(path);
    }

    private static bool TryReadBytes(string value, out ulong bytes)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out bytes);
    }

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ConfigureShellNamespaceRoot(ref Guid classId, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFlag(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long GetCounter64();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ResetProductCompareDiagnosticsDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int GetProductCompareDiagnostic(int index, StringBuilder buffer, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectFolderViewItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFocusedFolderViewItem(nint self, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int GetDiagnosticString([MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetProductViewNotificationsUseSyntheticRoot(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ConfigureProductNotificationDeliveryForProbe(int flush, uint delayMilliseconds);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void SetProductAsyncItemNotificationEnabledForProbe(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int GetProductAsyncItemNotificationIdentity(int index,
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounterForColumn(uint column);
    [StructLayout(LayoutKind.Sequential)] private struct NativePropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsEx(nint self, nint pidl, ref NativePropertyKey key, nint variant);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint pidl, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint GetShellDetailsSizeNative();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MapColumnToScid(nint self, uint column, out NativePropertyKey key);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CompareIds(nint self, nint column, nint left, nint right);
    [StructLayout(LayoutKind.Sequential)] private struct NativeInvokeCommand
    {
        public uint Size, Mask;
        public nint Owner, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextCapacity;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public nint ColumnFormats;
        public nint Group;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeHeaderItem
    {
        public uint Mask;
        public int Width;
        public nint Text, Bitmap;
        public int TextLength, Format;
        public nint Parameter;
        public int Image, Order;
        public uint Type;
        public nint Filter;
        public uint State;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint maxCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeCommand info);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisibleNative(nint window);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern nint GetParent(nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool EnumChildProc(nint window, nint parameter);
    [DllImport("user32.dll", EntryPoint = "EnumChildWindows")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint window, nint rectangle, nint updateRegion, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint subclassId, nint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint subclassId, nint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(nint variant);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
}
