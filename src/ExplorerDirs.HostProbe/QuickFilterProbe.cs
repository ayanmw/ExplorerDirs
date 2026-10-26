using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

// Exercises the production host only in its hidden, isolated fixture window.
internal static class QuickFilterProbe
{
    internal static int Run(Type hostType, IReadOnlyList<HwndHost> hosts, IReadOnlyList<string> folders,
        Func<HwndHost, int> count, Func<Type, HwndHost, string, uint, string[]> viewItemPaths)
    {
        var filterType = hostType.Assembly.GetType("ExplorerDirs.App.QuickFilterSettings", throwOnError: false)
            ?? throw new Exception("Product QuickFilterSettings is not available.");
        var setFilter = hostType.GetMethod("SetQuickFilter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Exception("Production ExplorerBrowserHost.SetQuickFilter is not implemented.");
        var initialOtherPanes = hosts.Skip(1).Select((host, index) => Names(viewItemPaths(hostType, host, folders[index + 1], 2))).ToArray();

        Apply("*.txt", exclude: false, filterFolders: false);
        VerifyPane0(["folder.tmp", "initial.txt", "keep.txt"], "include files; leave folders untouched");
        VerifyOtherPanes();

        Apply("*.txt", exclude: false, filterFolders: true);
        VerifyPane0(["initial.txt", "keep.txt"], "include mode filters nonmatching folders when enabled");
        VerifyOtherPanes();

        Apply("*.tmp", exclude: true, filterFolders: true);
        VerifyPane0(["initial.txt", "keep.txt"], "exclude mode hides matching files and folders");
        VerifyOtherPanes();

        Apply(string.Empty, exclude: false, filterFolders: false);
        VerifyPane0(["drop.tmp", "folder.tmp", "initial.txt", "keep.txt"], "clearing restores all Shell items");
        VerifyOtherPanes();

        SelectShellItem(hostType, hosts[0], folders[0], "keep.txt");
        Console.WriteLine($"quick-filter: selected-before-group={DescribeSelection(hostType, hosts[0], folders[0], viewItemPaths)}");
        SetGroupByType(hostType, hosts[0]);
        var groupedNames = Names(["drop.tmp", "folder.tmp", "initial.txt", "keep.txt"])
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!WaitForStable(() => count(hosts[0]) == 4 &&
                GroupSummary(hostType, hosts[0]).Contains("/4,asc=", StringComparison.Ordinal) &&
                Names(viewItemPaths(hostType, hosts[0], folders[0], 2))
                    .Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(groupedNames, StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250)))
            throw new Exception("The grouped Shell fixture did not settle before the filter scenario.");
        Console.WriteLine($"quick-filter: selected-after-group-before-filter={DescribeSelection(hostType, hosts[0], folders[0], viewItemPaths)}");
        Apply("*.txt", exclude: false, filterFolders: true);
        VerifyPane0(["initial.txt", "keep.txt"], "applying a filter to a grouped view preserves the group and row snapshot");
        VerifyOtherPanes();
        VerifyGroupedExport(hostType, hosts[0], folders[0], viewItemPaths);
        VerifySelectionAndFocus(hostType, hosts[0], folders[0], "keep.txt", viewItemPaths);

        Apply(string.Empty, exclude: false, filterFolders: false);
        SetNoGroup(hostType, hosts[0]);

        Apply("*.txt", exclude: false, filterFolders: false);
        // MethodInfo.Invoke does not apply optional-parameter defaults; pass the production null scope explicitly.
        hostType.GetMethod("Search")!.Invoke(hosts[0], ["keep.txt", null]);
        var searchQuery = hostType.GetProperty("SearchQuery", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Exception("Production search state is unavailable.");
        if (!WaitFor(() => searchQuery.GetValue(hosts[0]) is string, TimeSpan.FromSeconds(10)))
            throw new Exception("Search Folder navigation did not complete with the quick filter enabled.");
        hostType.GetMethod("Navigate")!.Invoke(hosts[0], [folders[0]]);
        VerifyPane0(["folder.tmp", "initial.txt", "keep.txt"], "filter resumes after returning from Search Folder");
        VerifyOtherPanes();
        Apply(string.Empty, exclude: false, filterFolders: false);
        Console.WriteLine("quick-filter: include/exclude/folders/clear PASS; ordered-view-snapshots=True; independent-panes=True");
        return 0;

        void Apply(string pattern, bool exclude, bool filterFolders)
        {
            var settings = Activator.CreateInstance(filterType, pattern, exclude, filterFolders)
                ?? throw new Exception("Could not create QuickFilterSettings.");
            setFilter.Invoke(hosts[0], [settings]);
        }

        void VerifyPane0(string[] expected, string stage)
        {
            var expectedSorted = Names(expected).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var filter = hostType.GetField("_quickFilter", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hosts[0]);
            var callbackCount = (int?)filter?.GetType().GetProperty("ShouldShowCalls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(filter);
            if (!WaitFor(() => count(hosts[0]) == expected.Length &&
                    Names(viewItemPaths(hostType, hosts[0], folders[0], 2))
                        .Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(expectedSorted, StringComparer.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(5)))
                throw new Exception($"{stage}: expected [{string.Join(',', expectedSorted)}], actual-count={count(hosts[0])}, actual=[{string.Join(',', Names(viewItemPaths(hostType, hosts[0], folders[0], 2)))}], filter-callbacks={callbackCount}, group={GroupSummary(hostType, hosts[0])}, site-filter={FilterSiteActive(hostType, hosts[0])}, quick-filter-status={ReadDiagnostic(hostType, hosts[0], "LastQuickFilterStatus")}, shell-refresh-status={ReadDiagnostic(hostType, hosts[0], "LastShellRefreshStatus")}, refresh-stages={ReadDiagnostic(hostType, hosts[0], "DirectoryChangeStageTimingSnapshot")}, native-messages={ReadDiagnostic(hostType, hosts[0], "NativeUserMessageSnapshot")}, selection-callbacks={ReadDiagnostic(hostType, hosts[0], "ShellSelectionCallbackTimelineSnapshot")}.");
            VerifyNativeViewOrder(hostType, hosts[0], stage);
        }

        void VerifyOtherPanes()
        {
            for (var i = 1; i < hosts.Count; i++)
            {
                var current = Names(viewItemPaths(hostType, hosts[i], folders[i], 2));
                if (!initialOtherPanes[i - 1].SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"Filter changed pane {i}: expected [{string.Join(',', initialOtherPanes[i - 1])}], actual [{string.Join(',', current)}].");
            }
        }
    }

    private static string ReadDiagnostic(Type hostType, HwndHost host, string propertyName) =>
        hostType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host)?.ToString() ?? "unavailable";

    private static string GroupSummary(Type hostType, HwndHost host)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        if (view == 0) return "no-view";
        try
        {
            var hr = At<GetGroupBy>(view, 18)(view, out var key, out var ascending);
            return $"hr=0x{hr:X8},key={key.FormatId:B}/{key.PropertyId},asc={ascending}";
        }
        finally { Marshal.Release(view); }
    }

    private static bool? FilterSiteActive(Type hostType, HwndHost host)
    {
        var site = hostType.GetField("_browserSite", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
        return site?.GetType().GetProperty("QuickFilterActive", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(site) as bool?;
    }

    private static void SetGroupByType(Type hostType, HwndHost host) => SetGroup(hostType, host,
        new NativePropertyKey { FormatId = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), PropertyId = 4 });

    private static void SetNoGroup(Type hostType, HwndHost host) => SetGroup(hostType, host, default);

    private static void SetGroup(Type hostType, HwndHost host, NativePropertyKey key)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new Exception("Could not get the native view to change grouping.");
        try { Marshal.ThrowExceptionForHR(At<SetGroupBy>(view, 17)(view, ref key, 1)); }
        finally { Marshal.Release(view); }
    }

    private static void VerifyGroupedExport(Type hostType, HwndHost host, string folder,
        Func<Type, HwndHost, string, uint, string[]> viewItemPaths)
    {
        if (!WaitFor(() => GroupSummary(hostType, host).Contains("/4,asc=", StringComparison.Ordinal), TimeSpan.FromSeconds(5)))
            throw new Exception($"The active Type grouping was not restored after filtered enumeration: group={GroupSummary(hostType, host)}; quick-filter-status={ReadDiagnostic(hostType, host, "LastQuickFilterStatus")}; shell-refresh-status={ReadDiagnostic(hostType, host, "LastShellRefreshStatus")}.");
        var method = hostType.GetMethod("CaptureFileViewExportSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Production file-view export capture is unavailable.");
        var snapshot = method.Invoke(host, null) ?? throw new Exception("Production export capture returned no snapshot.");
        var rows = ((System.Collections.IEnumerable)snapshot.GetType().GetProperty("Rows")!.GetValue(snapshot)!)
            .Cast<System.Collections.IEnumerable>()
            .Select(row => (string)row.Cast<object>().First())
            .ToArray();
        var currentNames = viewItemPaths(hostType, host, folder, 2).Select(Path.GetFileName).ToArray();
        if (!rows.SequenceEqual(currentNames, StringComparer.OrdinalIgnoreCase))
            throw new Exception($"Grouped export order differed from the Shell view: export=[{string.Join(',', rows)}], Shell=[{string.Join(',', currentNames)}].");

        VerifyPrintCapture(hostType, host, expectedRows: rows.Length);

        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new Exception("Grouped native view was unavailable after filter application.");
        try
        {
            Marshal.ThrowExceptionForHR(At<GetGroupBy>(view, 18)(view, out var group, out _));
            if (group.FormatId != new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC") || group.PropertyId != 4)
                throw new Exception($"Applying the filter changed the active Type grouping: group={group.FormatId:B}/{group.PropertyId}; quick-filter-status={ReadDiagnostic(hostType, host, "LastQuickFilterStatus")}; shell-refresh-status={ReadDiagnostic(hostType, host, "LastShellRefreshStatus")}.");
        }
        finally { Marshal.Release(view); }
        Console.WriteLine($"quick-filter: grouped export snapshot and grouping preserved; rows={string.Join('|', rows)}");
    }

    private static void VerifyPrintCapture(Type hostType, HwndHost host, int expectedRows)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new Exception("The native view was unavailable for print capture.");
        try
        {
            var captureType = hostType.Assembly.GetType("ExplorerDirs.App.ShellFileViewExportCapture", throwOnError: true)!;
            var capture = captureType.GetMethod("CaptureForPrint", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [view, true, 1u, 48]) ?? throw new Exception("Shell print capture returned no snapshot.");
            var rows = (System.Collections.IEnumerable)capture.GetType().GetProperty("Rows")!.GetValue(capture)!;
            var rowCount = rows.Cast<object>().Count();
            var images = (System.Collections.IEnumerable)capture.GetType().GetProperty("Images")!.GetValue(capture)!;
            var capturedImages = images.Cast<object?>().Count(image => image is not null);
            if (rowCount != expectedRows || images.Cast<object?>().Count() != expectedRows || capturedImages == 0)
                throw new Exception($"Unexpected print capture: rows={rowCount}, images={images.Cast<object?>().Count()}, nonempty-images={capturedImages}, expected={expectedRows}.");

            var printDocumentType = hostType.Assembly.GetType("ExplorerDirs.App.FileViewPrintDocument", throwOnError: true)!;
            var create = printDocumentType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [capture, 720d, 960d]) ?? throw new Exception("Thumbnail print document was not created.");
            var blocks = (System.Collections.IEnumerable)create.GetType().GetProperty("Blocks")!.GetValue(create)!;
            var table = blocks.Cast<object>().Single();
            var rowGroups = (System.Collections.IEnumerable)table.GetType().GetProperty("RowGroups")!.GetValue(table)!;
            var tableRows = (System.Collections.IEnumerable)rowGroups.Cast<object>().Single()
                .GetType().GetProperty("Rows")!.GetValue(rowGroups.Cast<object>().Single())!;
            if (tableRows.Cast<object>().Count() != (expectedRows + 2) / 3)
                throw new Exception("Thumbnail print layout did not preserve all current rows.");

            var details = captureType.GetMethod("CaptureForPrint", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [view, true, 4u, 16]) ?? throw new Exception("Details print capture returned no snapshot.");
            var detailsDocument = printDocumentType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [details, 720d, 960d]) ?? throw new Exception("Details print document was not created.");
            var detailsBlocks = (System.Collections.IEnumerable)detailsDocument.GetType().GetProperty("Blocks")!.GetValue(detailsDocument)!;
            var detailsTable = detailsBlocks.Cast<object>().Single();
            var detailsGroups = (System.Collections.IEnumerable)detailsTable.GetType().GetProperty("RowGroups")!.GetValue(detailsTable)!;
            var detailsGroup = detailsGroups.Cast<object>().Single();
            var detailsRows = (System.Collections.IEnumerable)detailsGroup.GetType().GetProperty("Rows")!.GetValue(detailsGroup)!;
            if (detailsRows.Cast<object>().Count() != expectedRows + 1)
                throw new Exception("Details print layout did not preserve the column header and all current rows.");
            Console.WriteLine($"quick-filter: print capture PASS; rows={rowCount}; shell-images={capturedImages}; thumbnail-groups={(expectedRows + 2) / 3}; details-rows={expectedRows + 1}");
        }
        finally { Marshal.Release(view); }
    }

    private static void VerifySelectionAndFocus(Type hostType, HwndHost host, string folder, string expectedName,
        Func<Type, HwndHost, string, uint, string[]> viewItemPaths)
    {
        var expectedPath = Path.Combine(folder, expectedName);
        var selected = viewItemPaths(hostType, host, folder, 1);
        var focused = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [folder]);
        if (!selected.SequenceEqual([expectedPath], StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(focused, expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Applying a grouped filter changed selection/focus: selected=[{string.Join('|', selected)}], focused={focused ?? "<none>"}; filter-status={ReadDiagnostic(hostType, host, "LastQuickFilterStatus")}; selection-timeline={ReadDiagnostic(hostType, host, "ShellSelectionCallbackTimelineSnapshot")}; restore-timeline={ReadDiagnostic(hostType, host, "ShellSelectionRestoreTimelineSnapshot")}.");
        Console.WriteLine("quick-filter: grouped filter preserved selected item and focused item");
    }

    private static string DescribeSelection(Type hostType, HwndHost host, string folder,
        Func<Type, HwndHost, string, uint, string[]> viewItemPaths)
    {
        var selected = viewItemPaths(hostType, host, folder, 1);
        var focused = (string?)hostType.GetMethod("GetFocusedItemPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [folder]);
        return $"selected=[{string.Join('|', selected.Select(Path.GetFileName))}],focused={Path.GetFileName(focused)}";
    }

    private static void SelectShellItem(Type hostType, HwndHost host, string folder, string name)
    {
        var browser = hostType.GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var browserInterface = hostType.GetNestedType("IExplorerBrowser", BindingFlags.NonPublic)!;
        var shellViewId = new Guid("000214E3-0000-0000-C000-000000000046");
        var viewObject = new object?[] { shellViewId, null };
        Marshal.ThrowExceptionForHR((int)browserInterface.GetMethod("GetCurrentView")!.Invoke(browser, viewObject)!);
        var shellView = viewObject[1]!;
        var selectItem = hostType.GetNestedType("IShellView", BindingFlags.NonPublic)!.GetMethod("SelectItem")!;
        Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder, name), 0, out var absolutePidl, 0, out _));
        try
        {
            var relativePidl = ILFindLastID(absolutePidl);
            Marshal.ThrowExceptionForHR((int)selectItem.Invoke(shellView, [relativePidl, 0x1u | 0x4u | 0x10u])!);
        }
        finally
        {
            Marshal.FreeCoTaskMem(absolutePidl);
            Marshal.ReleaseComObject(shellView);
        }
    }

    private static string[] Names(IEnumerable<string> paths) =>
        paths.Select(path => Path.GetFileName(path)!).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static void VerifyNativeViewOrder(Type hostType, HwndHost host, string stage)
    {
        var view = (nint)hostType.GetMethod("GetFolderView2Pointer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, null)!;
        if (view == 0) throw new Exception($"{stage}: native view was unavailable for ordered snapshot verification.");
        nint array = 0, folder = 0;
        try
        {
            var arrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            Marshal.ThrowExceptionForHR(At<GetItems>(view, 8)(view, 0x80000002, ref arrayId, out array));
            Marshal.ThrowExceptionForHR(At<GetCount>(array, 7)(array, out var arrayCount));
            Marshal.ThrowExceptionForHR(At<GetItemCount>(view, 7)(view, 2, out var viewCount));
            if (arrayCount != viewCount)
                throw new Exception($"{stage}: ordered snapshot count {arrayCount} differs from current view count {viewCount}.");

            var folderId = new Guid("000214E6-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(At<GetFolder>(view, 5)(view, ref folderId, out folder));
            for (uint index = 0; index < arrayCount; index++)
            {
                Marshal.ThrowExceptionForHR(At<GetItemAt>(array, 8)(array, index, out var item));
                string arrayName;
                try
                {
                    Marshal.ThrowExceptionForHR(At<GetDisplayName>(item, 5)(item, 0, out var displayName));
                    try { arrayName = Marshal.PtrToStringUni(displayName) ?? string.Empty; }
                    finally { Marshal.FreeCoTaskMem(displayName); }
                }
                finally { Marshal.Release(item); }

                Marshal.ThrowExceptionForHR(At<GetItemPidl>(view, 6)(view, (int)index, out var pidl));
                var strret = Marshal.AllocHGlobal(264);
                try
                {
                    Marshal.Copy(new byte[264], 0, strret, 264);
                    Marshal.ThrowExceptionForHR(At<GetDisplayNameOf>(folder, 11)(folder, pidl, 0, strret));
                    var indexedName = new StringBuilder(32768);
                    Marshal.ThrowExceptionForHR(StrRetToBuf(strret, pidl, indexedName, (uint)indexedName.Capacity));
                    if (!string.Equals(arrayName, indexedName.ToString(), StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"{stage}: ordered Shell snapshot item {index} '{arrayName}' differs from IFolderView.Item(index) '{indexedName}'.");
                }
                finally
                {
                    Marshal.FreeHGlobal(strret);
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        finally
        {
            if (folder != 0) Marshal.Release(folder);
            if (array != 0) Marshal.Release(array);
            Marshal.Release(view);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplayName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemPidl(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplayNameOf(nint self, nint pidl, uint flags, nint strret);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroupBy(nint self, ref NativePropertyKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetGroupBy(nint self, out NativePropertyKey key, out int ascending);
    [StructLayout(LayoutKind.Sequential)] private struct NativePropertyKey { public Guid FormatId; public uint PropertyId; }
    [DllImport("shlwapi.dll", EntryPoint = "StrRetToBufW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrRetToBuf(nint strret, nint pidl, StringBuilder buffer, uint capacity);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        do
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            if (condition()) return true;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < end);
        return condition();
    }

    private static bool WaitForStable(Func<bool> condition, TimeSpan timeout, TimeSpan stableDuration)
    {
        var end = DateTime.UtcNow + timeout;
        DateTime? stableSince = null;
        do
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            if (condition())
            {
                stableSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - stableSince >= stableDuration) return true;
            }
            else
            {
                stableSince = null;
            }
            Thread.Sleep(10);
        } while (DateTime.UtcNow < end);
        return false;
    }
}
