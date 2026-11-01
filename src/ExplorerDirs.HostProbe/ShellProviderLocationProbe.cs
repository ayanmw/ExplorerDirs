using Microsoft.Win32;
using System.Collections;
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
    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-HostProvider-" + Guid.NewGuid().ToString("N"));
        var pathA = Path.Combine(root, "default-root");
        var pathB = Path.Combine(root, "session-root");
        var smallFolder = Path.Combine(pathB, "folder-small");
        var largeFolder = Path.Combine(pathB, "folder-large");
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
            File.WriteAllBytes(Path.Combine(smallFolder, "payload.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(largeFolder, "payload.bin"), new byte[29]);

            var adapterPath = FindAdapterLibrary();
            adapter = NativeLibrary.Load(adapterPath);
            Check(GetExport<SetClassId>(adapter, "SetSyntheticFolderClassIdForProbe")(ref classId));
            Check(GetExport<SetPath>(adapter, "SetSyntheticSortBackingPath")(pathA));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticCustomSizeColumnForProbe")(1));
            Check(GetExport<SetFlag>(adapter, "SetSyntheticRecursiveSizeScanOnDemandForProbe")(1));
            RegisterClass(classKeyPath, adapterPath);
            classRegistered = true;
            RegisterNamespace(namespaceKeyPath);
            namespaceRegistered = true;

            var targetIdentity = $"::{{{classId:D}}}\\edpath:{EncodePath(pathB)}";
            var appAssembly = Assembly.Load("ExplorerDirs.App");
            var viewStateType = appAssembly.GetType("ExplorerDirs.App.ViewState", throwOnError: true)!;
            var propertyKeyType = appAssembly.GetType("ExplorerDirs.App.PropertyKeyState", throwOnError: true)!;
            var sortColumnType = appAssembly.GetType("ExplorerDirs.App.SortColumnState", throwOnError: true)!;
            var displayColumnType = appAssembly.GetType("ExplorerDirs.App.DisplayColumnState", throwOnError: true)!;
            var basicFormat = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
            var recursiveSortKey = Activator.CreateInstance(propertyKeyType,
                Guid.Parse("9E5E05AC-1936-4A75-94F7-4704B8B01923"), 1u)!;
            var sortColumns = Array.CreateInstance(sortColumnType, 1);
            sortColumns.SetValue(Activator.CreateInstance(sortColumnType, [recursiveSortKey, 1]), 0);
            var displayColumns = Array.CreateInstance(displayColumnType, 2);
            var nameKey = Activator.CreateInstance(propertyKeyType, basicFormat, 10u)!;
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [recursiveSortKey, 180]), 0);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [nameKey, 240]), 1);
            var viewStateConstructor = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == 6);
            var viewState = viewStateConstructor.Invoke([4u, 16, sortColumns, null, true, displayColumns]);
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
                return counts is not null && (int)counts.GetType().GetProperty("Total")!.GetValue(counts)! == 2;
            }, TimeSpan.FromSeconds(15), "the production host did not enumerate the registered Shell folder's two rows");
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
                directRecursiveValues = ReadRecursiveValues(hostType, host, 2);
                return directRecursiveValues.Length == 2 && directRecursiveValues.All(value => !string.IsNullOrWhiteSpace(value));
            }, TimeSpan.FromSeconds(15), "the product host's IShellFolder2.GetDetailsEx did not return recursive values");
            var recursiveDisplay = ReadRecursiveDisplayText(adapter, hostType, host, 2);
            if (!string.Equals(recursiveDisplay.Header, "Recursive Size", StringComparison.Ordinal) ||
                recursiveDisplay.Values.Select(value => TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue)
                    .Order().SequenceEqual([5UL, 29UL]) is false)
                throw new InvalidOperationException($"The provider's recursive GetDetailsOf contract is unexpected: header='{recursiveDisplay.Header}', values=[{string.Join('|', recursiveDisplay.Values)}].");
            var providerColumnContract = ReadProviderColumnContract(hostType, host);
            if (providerColumnContract.CompareResult != 1 ||
                providerColumnContract.DataKey.FormatId != new Guid("7E1F84B7-C052-4E51-8A3C-869254A6B910") ||
                providerColumnContract.DataKey.PropertyId != 1)
                throw new InvalidOperationException($"The provider's native recursive column contract was unexpected: key={providerColumnContract.DataKey.FormatId:B}:{providerColumnContract.DataKey.PropertyId}, compare={providerColumnContract.CompareResult}.");

            var capture = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var snapshot = capture.Invoke(host, null)!;
            var (columnNames, rows) = ReadExportRows(snapshot);
            var sizeColumnIndex = Array.FindIndex(columnNames, name =>
                name.Contains("Recursive Size", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("9e5e05ac-1936-4a75-94f7-4704b8b01923", StringComparison.OrdinalIgnoreCase));
            if (sizeColumnIndex < 0) throw new InvalidOperationException($"The product view omitted the restored recursive-size column: [{string.Join('|', columnNames)}].");
            if (rows.Length != 2) throw new InvalidOperationException($"The product export snapshot returned {rows.Length} rows; expected 2.");

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
            var orderBeforeRefresh = ReadExportRows(capture.Invoke(host, null)!).Rows
                .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var refreshCompareCountBefore = getCompareCount();
            // In the registered provider host, restored sort state may precede the first actual row reorder.
            var shellRefreshResult = RefreshCurrentShellView(hostType, host);
            if (shellRefreshResult < 0) Marshal.ThrowExceptionForHR(shellRefreshResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            var orderAfterRefresh = ReadExportRows(capture.Invoke(host, null)!).Rows
                .Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var refreshCompareCountAfter = getCompareCount();
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
                    name.Contains("9e5e05ac-1936-4a75-94f7-4704b8b01923", StringComparison.OrdinalIgnoreCase));
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
            directRecursiveValues = ReadRecursiveValues(hostType, host, 2);
            (columnNames, rows) = ReadExportRows(capture.Invoke(host, null)!);
            namesInShellOrder = rows.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
            var exportedRecursiveValues = rows.Select(row => row[sizeColumnIndex]).ToArray();
            if (exportedRecursiveValues.Select(value => TryReadBytes(value, out var bytes) ? bytes : ulong.MaxValue)
                    .SequenceEqual([5UL, 29UL]) is false)
                throw new InvalidOperationException($"The product export snapshot did not consume recursive display values in Shell row order: columns=[{string.Join('|', columnNames)}], values=[{string.Join('|', exportedRecursiveValues)}].");
            var mappedShellExport = ReadVisibleShellDetailsSnapshot(adapter, hostType, host, nativeHeaderOrder, total);
            if (!mappedShellExport.Columns.SequenceEqual(["Recursive Size", "Name"], StringComparer.Ordinal) ||
                !mappedShellExport.Rows.Select(row => string.Join('|', row)).SequenceEqual(["5|folder-small", "29|folder-large"], StringComparer.Ordinal))
                throw new InvalidOperationException($"The visible Shell details-to-column mapping produced unexpected export data: columns=[{string.Join('|', mappedShellExport.Columns)}], rows=[{string.Join('|', mappedShellExport.Rows.Select(row => string.Join('|', row)))}], ordinals=[{string.Join('|', mappedShellExport.Ordinals)}].");
            var valuesByName = rows.Select((row, index) =>
                    (Name: namesInShellOrder[index], Bytes: TryReadBytes(directRecursiveValues[index], out var bytes) ? bytes : ulong.MaxValue))
                .ToDictionary(item => item.Name, item => item.Bytes, StringComparer.OrdinalIgnoreCase);
            var recursiveValuesMatchFixture = valuesByName["folder-small"] == 5 && valuesByName["folder-large"] == 29;
            if (!recursiveValuesMatchFixture)
                throw new InvalidOperationException($"The product Shell view returned unexpected per-folder values: [{string.Join('|', valuesByName.Select(item => $"{item.Key}={item.Value}"))}].");
            var sortApplied = valuesByName["folder-small"] < valuesByName["folder-large"] &&
                namesInShellOrder.SequenceEqual(["folder-small", "folder-large"], StringComparer.OrdinalIgnoreCase);
            var compareCountAfterSort = getCompareCount();
            var columnCompareDeltas = Enumerable.Range(0, columnCompareCountsBefore.Length)
                .Select(index => getCompareCountForColumn((uint)index) - columnCompareCountsBefore[index]).ToArray();

            var headerCompareDeltas = Enumerable.Range(0, columnCompareCountsBefore.Length)
                .Select(index => getCompareCountForColumn((uint)index) - headerColumnCountsBefore[index]).ToArray();
            Console.WriteLine($"shell-provider-location: product-host-navigation=True; is-filesystem=False; rows={total}; saved-visible-columns=[{string.Join('|', displayColumnsBeforeHeaderOrder)}]; visible-columns-after-header-action=[{string.Join('|', CaptureDisplayColumns(hostType, host))}]; logical-header-texts=[{string.Join('|', logicalHeaderTexts)}]; initial-visual-header-order=[{string.Join('|', initialVisualHeaderOrder)}]; visual-header-order=[{string.Join('|', nativeHeaderOrder)}]; recursive-header-index={recursiveHeaderIndex}; getdetailsof-headers=[{string.Join('|', mappedShellExport.ShellHeaders)}]; mapped-export-columns=[{string.Join('|', mappedShellExport.Columns)}]; mapped-export-ordinals=[{string.Join('|', mappedShellExport.Ordinals)}]; mapped-export-rows=[{string.Join('|', mappedShellExport.Rows.Select(row => string.Join('|', row)))}]; getdetailsof-header='{recursiveDisplay.Header}'; getdetailsof-values=[{string.Join('|', recursiveDisplay.Values)}]; export-columns=[{string.Join('|', columnNames)}]; export-values=[{string.Join('|', rows.Select(row => row[sizeColumnIndex]))}]; order-before-shell-refresh=[{string.Join('|', orderBeforeRefresh)}]; order-after-shell-refresh=[{string.Join('|', orderAfterRefresh)}]; shell-refresh-hr=0x{shellRefreshResult:X8}; refresh-compare-delta={refreshCompareCountAfter - refreshCompareCountBefore}; displayed-order-after-two-header-clicks=[{string.Join('|', namesInShellOrder)}]; order-after-first-header-click=[{string.Join('|', headerFirstOrder)}]; header-sort-state-after-first-click=[{string.Join('|', headerFirstSorts)}]; order-after-sortascending=[{string.Join('|', orderAfterAscendingCommand)}]; order-after-sortdescending=[{string.Join('|', orderAfterDescendingCommand)}]; sort-state-after-sortdescending=[{string.Join('|', descendingCommandSorts)}]; order-after-next-sortascending=[{string.Join('|', orderAfterAscendingAgainCommand)}]; sort-state-after-next-sortascending=[{string.Join('|', ascendingAgainCommandSorts)}]; direct-recursive-values=[{string.Join('|', directRecursiveValues)}]; fixture-values-correct={recursiveValuesMatchFixture}; provider-map-column-to-scid={providerColumnContract.DataKey.FormatId:B}:{providerColumnContract.DataKey.PropertyId}; direct-provider-compare={providerColumnContract.CompareResult}; restored-sort-state=[{string.Join('|', actualSorts)}]; shell-sort-command-hr=0x{sortCommandResult:X8}; shell-command-compare-delta={commandCompareCount - ascendingCommandCompareCountBefore}; descending-command-hr=0x{descendingCommandResult:X8}; descending-command-compare-delta={descendingCommandCompareCount - commandCompareCount}; ascending-again-command-hr=0x{ascendingAgainCommandResult:X8}; ascending-again-command-compare-delta={ascendingAgainCommandCompareCount - descendingCommandCompareCount}; header-window={sortHeaderWindow}; first-header-compare-delta={headerFirstCompareCount - headerCompareCountBefore}; second-header-compare-delta={compareCountAfterSort - headerFirstCompareCount}; compare-delta-by-column=[{string.Join('|', columnCompareDeltas)}]; header-compare-delta-by-column=[{string.Join('|', headerCompareDeltas)}]; shell-sort-applied={sortApplied}; target=session-root; fallback=default-root; parent-visible={IsWindowVisible(source.Handle)}");
            if (IsWindowVisible(source.Handle)) throw new InvalidOperationException("The hidden Shell provider HostProbe parent became visible.");
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
                    Check(getItem(view, index, out var pidl));
                    try
                    {
                        var variant = Marshal.AllocCoTaskMem(IntPtr.Size == 8 ? 24 : 16);
                        try
                        {
                            Marshal.WriteInt16(variant, 0);
                            Check(getDetailsEx(folder, pidl, ref key, variant));
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
        nint header = 0;
        EnumChildWindows(viewWindow, (window, _) =>
        {
            var className = new StringBuilder(128);
            _ = GetClassName(window, className, className.Capacity);
            if (string.Equals(className.ToString(), "SysListView32", StringComparison.Ordinal))
            {
                header = SendMessageW(window, 0x101F, 0, 0); // LVM_GETHEADER.
                return header == 0;
            }
            return true;
        }, 0);
        if (header == 0) throw new InvalidOperationException("The product ExplorerBrowser view did not expose a SysListView32 header.");
        var count = SendMessageW(header, 0x1200, 0, 0); // HDM_GETITEMCOUNT.
        if (count < 2) throw new InvalidOperationException($"The product Shell header exposed only {count} column(s); the recursive-size header is missing.");
        return header;
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFlag(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCounter();
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
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern nint GetParent(nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool EnumChildProc(nint window, nint parameter);
    [DllImport("user32.dll", EntryPoint = "EnumChildWindows")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(nint variant);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
}
