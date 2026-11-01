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
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [nameKey, 240]), 0);
            displayColumns.SetValue(Activator.CreateInstance(displayColumnType, [recursiveSortKey, 180]), 1);
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
            var sortCommandResult = InvokeBackgroundShellVerb(hostType, host, "sortascending");
            if (sortCommandResult < 0) Marshal.ThrowExceptionForHR(sortCommandResult);
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            directRecursiveValues = ReadRecursiveValues(hostType, host, 2);
            (columnNames, rows) = ReadExportRows(capture.Invoke(host, null)!);
            namesInShellOrder = rows.Select(row => Path.GetFileName(row[nameColumnIndex]) ?? row[nameColumnIndex]).ToArray();
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

            Console.WriteLine($"shell-provider-location: product-host-navigation=True; is-filesystem=False; rows={total}; recursive-column=True; export-values=[{string.Join('|', rows.Select(row => row[sizeColumnIndex]))}]; displayed-order=[{string.Join('|', namesInShellOrder)}]; direct-recursive-values=[{string.Join('|', directRecursiveValues)}]; fixture-values-correct={recursiveValuesMatchFixture}; provider-map-column-to-scid={providerColumnContract.DataKey.FormatId:B}:{providerColumnContract.DataKey.PropertyId}; direct-provider-compare={providerColumnContract.CompareResult}; restored-sort-state=[{string.Join('|', actualSorts)}]; shell-sort-command-hr=0x{sortCommandResult:X8}; shell-compare-delta={compareCountAfterSort - compareCountBeforeSort}; compare-delta-by-column=[{string.Join('|', columnCompareDeltas)}]; shell-sort-applied={sortApplied}; target=session-root; fallback=default-root; parent-visible={IsWindowVisible(source.Handle)}");
            if (IsWindowVisible(source.Handle)) throw new InvalidOperationException("The hidden Shell provider HostProbe parent became visible.");
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
            if (namespaceRegistered) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistered) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            if (adapter != 0) NativeLibrary.Free(adapter);
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint name, uint maxCount);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvokeCommand(nint self, ref NativeInvokeCommand info);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisibleNative(nint window);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(nint variant);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
}
