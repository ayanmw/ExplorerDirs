using System.Runtime.InteropServices;
using ExplorerDirs.App;

// Confirms that current Shell view ordering, visible column keys, and cached
// per-item properties can be read without showing a window or enumerating paths.
internal static class ViewDataProbe
{
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderFilterSiteId = new("C0A651F5-B48B-11D2-B5ED-006097C686F6");
    private static readonly Guid ShellItem2Id = new("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid PropertyFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly PropertyKey NameKey = new(PropertyFormatId, 10);
    private static readonly PropertyKey SizeKey = new(PropertyFormatId, 12);
    private static readonly PropertyKey TypeKey = new(PropertyFormatId, 4);

    internal static void RunRefreshCapability(nint browser, string fixture)
    {
        var viewId = FolderView2Id;
        Check(At<GetCurrentView>(browser, 17)(browser, ref viewId, out var view));
        try
        {
            Check(At<SetMode>(view, 35)(view, 4, 48)); // Details view.
            var noGroup = default(PropertyKey);
            Check(At<SetGroup>(view, 17)(view, ref noGroup, 0));
            var ascending = new SortColumn { Key = NameKey, Direction = 1 };
            Check(At<SetSortColumns>(view, 27)(view, [ascending], 1));
            var columns = new[]
            {
                new DisplayColumnState(new(PropertyFormatId, NameKey.PropertyId), 271),
                new DisplayColumnState(new(PropertyFormatId, TypeKey.PropertyId), 193),
                new DisplayColumnState(new(PropertyFormatId, SizeKey.PropertyId), 137)
            };
            Check(ShellColumnManager.Apply(view, columns));
            Pump();
            var descendingBySize = new SortColumn { Key = SizeKey, Direction = -1 };
            Check(At<SetSortColumns>(view, 27)(view, [descendingBySize], 1));
            Pump();
            var sizeOrderWithoutCache = ReadIndexedNames(view);
            Console.WriteLine($"view-data: size-sort without injected cache={string.Join("|", sizeOrderWithoutCache)}");
            Check(At<SetSortColumns>(view, 27)(view, [ascending], 1));
            Pump();
            VerifySnapshot(view, ["child", "five.txt", "three.txt"], "refresh-cache-before-injection");
            ProbeViewPropertyCache(view);
            Check(At<SetSortColumns>(view, 27)(view, [descendingBySize], 1));
            Pump();
            var sizeOrderWithCache = ReadIndexedNames(view);
            var sortedChildIndex = sizeOrderWithCache.ToList().FindIndex(name => name.Equals("child", StringComparison.OrdinalIgnoreCase));
            if (sortedChildIndex < 0) throw new Exception("The child folder disappeared while sorting by cached System.Size.");
            Check(At<GetItemPidl>(view, 6)(view, sortedChildIndex, out var sortedChildPidl));
            try
            {
                var sizeKey = SizeKey;
                Check(At<GetViewProperty>(view, 20)(view, sortedChildPidl, ref sizeKey, out var sortedCachedValue));
                try
                {
                    Console.WriteLine($"view-data: size-sort with injected cache={string.Join("|", sizeOrderWithCache)}; cached-value={sortedCachedValue.Type}:{sortedCachedValue.UInt64}; order-changed={ !sizeOrderWithoutCache.SequenceEqual(sizeOrderWithCache, StringComparer.OrdinalIgnoreCase)}");
                }
                finally { PropVariantClear(ref sortedCachedValue); }
            }
            finally { Marshal.FreeCoTaskMem(sortedChildPidl); }

            var shellViewId = ShellViewId;
            Check(Marshal.QueryInterface(view, in shellViewId, out var shellView));
            try
            {
                Check(At<RefreshShellView>(shellView, 8)(shellView));
                Pump();
                var refreshedViewId = FolderView2Id;
                Check(Marshal.QueryInterface(shellView, in refreshedViewId, out var refreshedView));
                try
                {
                    var refreshedNames = ReadIndexedNames(refreshedView);
                    if (!refreshedNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
                            .SetEquals(["child", "five.txt", "three.txt"]))
                        throw new Exception($"The isolated fixture contents changed after IShellView.Refresh(): {string.Join("|", refreshedNames)}.");
                    var refreshedChildIndex = refreshedNames.ToList().FindIndex(name => name.Equals("child", StringComparison.OrdinalIgnoreCase));
                    Check(At<GetItemPidl>(refreshedView, 6)(refreshedView, refreshedChildIndex, out var refreshedChildPidl));
                    try
                    {
                        var refreshedKey = SizeKey;
                        Check(At<GetViewProperty>(refreshedView, 20)(refreshedView, refreshedChildPidl, ref refreshedKey, out var refreshedValue));
                        try
                        {
                            Console.WriteLine($"view-data: after-refresh System.Size cache type={refreshedValue.Type}; value={refreshedValue.UInt64}; retained={refreshedValue.Type == 21 && refreshedValue.UInt64 == 987654321UL}");
                        }
                        finally { PropVariantClear(ref refreshedValue); }

                    var injectedAgain = new Variant { Type = 21, UInt64 = 987654321UL };
                    var sizeKey = SizeKey;
                    Check(At<SetViewProperty>(refreshedView, 19)(refreshedView, refreshedChildPidl, ref sizeKey, ref injectedAgain));
                    Directory.Move(Path.Combine(fixture, "child"), Path.Combine(fixture, "child-renamed"));
                    PumpFor(TimeSpan.FromSeconds(1));
                    var renamedNames = ReadIndexedNames(refreshedView);
                    Console.WriteLine($"view-data: after-rename names={string.Join("|", renamedNames)}");
                    var renamedIndex = renamedNames.ToList().FindIndex(name => name.Equals("child-renamed", StringComparison.OrdinalIgnoreCase));
                    if (renamedIndex >= 0)
                    {
                        Check(At<GetItemPidl>(refreshedView, 6)(refreshedView, renamedIndex, out var renamedPidl));
                        try
                        {
                            var renamedKey = SizeKey;
                            Check(At<GetViewProperty>(refreshedView, 20)(refreshedView, renamedPidl, ref renamedKey, out var renamedValue));
                            try { Console.WriteLine($"view-data: renamed-item System.Size cache type={renamedValue.Type}; value={renamedValue.UInt64}"); }
                            finally { PropVariantClear(ref renamedValue); }
                        }
                        finally { Marshal.FreeCoTaskMem(renamedPidl); }
                    }
                    else Console.WriteLine("view-data: rename not visible without an explicit refresh");
                    }
                    finally { Marshal.FreeCoTaskMem(refreshedChildPidl); }
                }
                finally { Marshal.Release(refreshedView); }
            }
            finally { Marshal.Release(shellView); }
        }
        finally { Marshal.Release(view); }
    }

    internal static void Run(nint browser, string parentFolder)
    {
        var fixture = Path.Combine(parentFolder, ".view-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "child"));
        File.WriteAllBytes(Path.Combine(fixture, "three.txt"), new byte[3]);
        File.WriteAllBytes(Path.Combine(fixture, "five.txt"), new byte[5]);
        File.WriteAllBytes(Path.Combine(fixture, "child", "nested.txt"), new byte[7]);
        try
        {
            Navigate(browser, fixture);
            var folderView2Id = FolderView2Id;
            Check(At<GetCurrentView>(browser, 17)(browser, ref folderView2Id, out var view));
            try
            {
                var initialCount = WaitForItemCount(view, 3, TimeSpan.FromSeconds(3));
                if (initialCount != 3) throw new Exception($"View-data fixture navigation did not settle; expected 3 entries, found {initialCount}.");
                var noGroup = default(PropertyKey);
                Check(At<SetGroup>(view, 17)(view, ref noGroup, 0));
                var ascending = new SortColumn { Key = NameKey, Direction = 1 };
                Check(At<SetSortColumns>(view, 27)(view, [ascending], 1));
                var columns = new[]
                {
                    new DisplayColumnState(new(PropertyFormatId, NameKey.PropertyId), 271),
                    new DisplayColumnState(new(PropertyFormatId, TypeKey.PropertyId), 193),
                    new DisplayColumnState(new(PropertyFormatId, SizeKey.PropertyId), 137)
                };
                Check(ShellColumnManager.Apply(view, columns));
                Pump();

                VerifySnapshot(view, ["child", "five.txt", "three.txt"], "details-ascending");
                VerifyExportSnapshot(view, ["child", "five.txt", "three.txt"], "details-ascending");
                ProbeViewPropertyCache(view);

                var descending = ascending with { Direction = -1 };
                Check(At<SetSortColumns>(view, 27)(view, [descending], 1));
                Pump();
                VerifySnapshot(view, ["three.txt", "five.txt", "child"], "details-descending");
                VerifyExportSnapshot(view, ["three.txt", "five.txt", "child"], "details-descending");

                var itemTypeKey = TypeKey;
                Check(At<SetSortColumns>(view, 27)(view, [ascending], 1));
                Check(At<SetGroup>(view, 17)(view, ref itemTypeKey, 1));
                Pump();
                VerifyItemSet(view, ["child", "three.txt", "five.txt"], "details-grouped-by-type");
                VerifyExportSnapshot(view, ["child", "five.txt", "three.txt"], "details-grouped-by-type");
                Check(At<SetGroup>(view, 17)(view, ref noGroup, 0));
                Pump();

                Check(At<SetSortColumns>(view, 27)(view, [descending], 1));
                Check(At<SetMode>(view, 35)(view, 1, 256)); // Extra-large icons.
                Pump();
                VerifySnapshot(view, ["three.txt", "five.txt", "child"], "icons-descending");
                VerifyExportSnapshot(view, ["three.txt", "five.txt", "child"], "icons-descending");
                VerifyFilteredGroupedExport(browser);
                Console.WriteLine("view-data: order follows current Shell sort; visible columns and per-item size properties read in Details and Icons");
            }
            finally { Marshal.Release(view); }
        }
        finally
        {
            Navigate(browser, parentFolder);
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static void VerifyFilteredGroupedExport(nint browser)
    {
        var siteId = FolderFilterSiteId;
        nint filterSite = 0, shellView = 0, currentView = 0, configuredView = 0;
        var filter = new ShellFolderFilter(new QuickFilterSettings("*.txt", false, filterFolders: true));
        var filterUnknown = Marshal.GetIUnknownForObject(filter);
        var noGroup = default(PropertyKey);
        var installed = false;
        try
        {
            var currentViewId = FolderView2Id;
            Check(At<GetCurrentView>(browser, 17)(browser, ref currentViewId, out configuredView));
            Check(At<SetMode>(configuredView, 35)(configuredView, 4, 48)); // Details view supports the filter/group scenario.
            Pump();
            var ascending = new SortColumn { Key = NameKey, Direction = 1 };
            Check(At<SetSortColumns>(configuredView, 27)(configuredView, [ascending], 1));
            Check(At<SetGroup>(configuredView, 17)(configuredView, ref noGroup, 0));
            Pump();
            Check(Marshal.QueryInterface(browser, in siteId, out filterSite));
            var shellViewId = ShellViewId;
            Check(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
            Check(At<SetFolderFilter>(filterSite, 3)(filterSite, filterUnknown));
            installed = true;
            Check(At<RefreshShellView>(shellView, 8)(shellView));
            currentViewId = FolderView2Id;
            Marshal.Release(configuredView);
            configuredView = 0;
            Check(At<GetCurrentView>(browser, 17)(browser, ref currentViewId, out currentView));
            var count = WaitForItemCount(currentView, 2, TimeSpan.FromSeconds(3));
            if (count != 2) throw new Exception($"Filtered view expected 2 items, found {count}.");

            var expected = new[] { "five.txt", "three.txt" };
            ProbeFilteredItemArrays(shellView, currentView);
            var ungroupedSnapshot = ShellFileViewExportCapture.Capture(currentView, quickFilterActive: true);
            var ungroupedNames = ungroupedSnapshot.Rows.Select(row => row[0]).ToArray();
            if (ungroupedSnapshot.Rows.Count != 2 || !ungroupedNames.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Filtered ungrouped export rows differ from the filter result or Shell sort order.");
            Console.WriteLine($"view-export: stage=details-filtered; count={count}; order={string.Join("|", ungroupedNames)}; export=PASS");

            var itemTypeKey = TypeKey;
            Check(At<SetGroup>(currentView, 17)(currentView, ref itemTypeKey, 1));
            Pump();
            Marshal.Release(currentView);
            currentView = 0;
            currentViewId = FolderView2Id;
            Check(At<GetCurrentView>(browser, 17)(browser, ref currentViewId, out currentView));
            count = WaitForItemCount(currentView, 2, TimeSpan.FromSeconds(3));
            if (count != 2) throw new Exception($"Filtered grouped view expected 2 items, found {count}.");
            ProbeFilteredItemArrays(shellView, currentView);
            var filteredGroupedSnapshot = ShellFileViewExportCapture.Capture(currentView, quickFilterActive: true);
            var exportedNames = filteredGroupedSnapshot.Rows.Select(row => row[0]).ToArray();
            if (filteredGroupedSnapshot.Columns.Count != 3 || filteredGroupedSnapshot.Rows.Count != 2 ||
                !exportedNames.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Filtered grouped export rows differ from the current filter result or Shell sort order.");
            Console.WriteLine($"view-export: stage=details-grouped-filtered; count={count}; order={string.Join("|", exportedNames)}; folders-excluded=True; export=PASS");

            Check(At<SetFolderFilter>(filterSite, 3)(filterSite, 0));
            installed = false;
            Check(At<RefreshShellView>(shellView, 8)(shellView));
            _ = WaitForItemCount(currentView, 3, TimeSpan.FromSeconds(3));
            Marshal.Release(currentView);
            currentView = 0;
            currentViewId = FolderView2Id;
            Check(At<GetCurrentView>(browser, 17)(browser, ref currentViewId, out configuredView));
            Check(At<SetGroup>(configuredView, 17)(configuredView, ref noGroup, 0));
            var groupedKey = TypeKey;
            Check(At<SetGroup>(configuredView, 17)(configuredView, ref groupedKey, 1));
            Pump();
            Check(At<SetFolderFilter>(filterSite, 3)(filterSite, filterUnknown));
            installed = true;
            Check(At<RefreshShellView>(shellView, 8)(shellView));
            Marshal.Release(configuredView);
            configuredView = 0;
            currentViewId = FolderView2Id;
            Check(At<GetCurrentView>(browser, 17)(browser, ref currentViewId, out currentView));
            count = WaitForItemCount(currentView, 2, TimeSpan.FromSeconds(3));
            if (count != 2) throw new Exception($"Grouped then filtered view expected 2 items, found {count}.");
            ProbeFilteredItemArrays(shellView, currentView);
            var groupedThenFilteredSnapshot = ShellFileViewExportCapture.Capture(currentView, quickFilterActive: true);
            var groupedThenFilteredNames = groupedThenFilteredSnapshot.Rows.Select(row => row[0]).ToArray();
            if (groupedThenFilteredSnapshot.Rows.Count != 2 || !groupedThenFilteredNames.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Grouped then filtered export rows differ from the Shell result.");
            Console.WriteLine($"view-export: stage=details-grouped-then-filtered; count={count}; order={string.Join("|", groupedThenFilteredNames)}; export=PASS");
            Check(At<SetGroup>(currentView, 17)(currentView, ref noGroup, 0));
            groupedKey = TypeKey;
            Check(At<SetGroup>(currentView, 17)(currentView, ref groupedKey, 1));
            Pump();
            var recovered = ShellFileViewExportCapture.Capture(currentView, quickFilterActive: true);
            var recoveredNames = recovered.Rows.Select(row => row[0]).ToArray();
            if (recovered.Rows.Count != 2 || !recoveredNames.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new Exception("Clearing then restoring the active group produced an invalid filtered snapshot.");
            Console.WriteLine($"view-export: stage=group-reset-after-filter; count={count}; order={string.Join("|", recoveredNames)}; export=PASS");
        }
        finally
        {
            if (installed)
            {
                _ = At<SetFolderFilter>(filterSite, 3)(filterSite, 0);
                _ = At<RefreshShellView>(shellView, 8)(shellView);
                if (currentView != 0) _ = WaitForItemCount(currentView, 3, TimeSpan.FromSeconds(3));
            }
            if (currentView != 0) Marshal.Release(currentView);
            if (configuredView != 0) Marshal.Release(configuredView);
            if (installed && GetCurrentViewPointer(browser, FolderView2Id) is var viewAfterFilter && viewAfterFilter != 0)
            {
                noGroup = default(PropertyKey);
                _ = At<SetGroup>(viewAfterFilter, 17)(viewAfterFilter, ref noGroup, 0);
                Marshal.Release(viewAfterFilter);
            }
            Marshal.Release(filterUnknown);
            if (shellView != 0) Marshal.Release(shellView);
            if (filterSite != 0) Marshal.Release(filterSite);
            GC.KeepAlive(filter);
        }
    }

    private static nint GetCurrentViewPointer(nint browser, Guid id)
    {
        var iid = id;
        return At<GetCurrentView>(browser, 17)(browser, ref iid, out var view) < 0 ? 0 : view;
    }

    private static void ProbeFilteredItemArrays(nint shellView, nint view)
    {
        foreach (var flags in new uint[] { 2, 0x80000002 })
        {
            var iid = ShellItemArrayId;
            var result = At<GetItems>(view, 8)(view, flags, ref iid, out var array);
            if (result < 0)
            {
                Console.WriteLine($"view-export: filtered-items flags=0x{flags:X8}; hr=0x{result:X8}");
                continue;
            }
            try
            {
                Check(At<GetCount>(array, 7)(array, out var count));
                var names = new List<string>();
                for (uint index = 0; index < count; index++)
                {
                    Check(At<GetItemAt>(array, 8)(array, index, out var item));
                    try
                    {
                        Check(At<GetDisplayName>(item, 5)(item, 0, out var name));
                        try { names.Add(Marshal.PtrToStringUni(name) ?? string.Empty); }
                        finally { Marshal.FreeCoTaskMem(name); }
                    }
                    finally { Marshal.Release(item); }
                }
                Console.WriteLine($"view-export: filtered-items flags=0x{flags:X8}; items={string.Join("|", names)}");
            }
            finally { Marshal.Release(array); }
        }

        foreach (var flags in new uint[] { 2, 0x80000002 })
        {
            var iid = ShellItemArrayId;
            var result = At<GetItemObject>(shellView, 15)(shellView, flags, ref iid, out var array);
            if (result < 0)
            {
                Console.WriteLine($"view-export: filtered-shell-items flags=0x{flags:X8}; hr=0x{result:X8}");
                continue;
            }
            try
            {
                Check(At<GetCount>(array, 7)(array, out var count));
                var names = new List<string>();
                for (uint index = 0; index < count; index++)
                {
                    Check(At<GetItemAt>(array, 8)(array, index, out var item));
                    try
                    {
                        Check(At<GetDisplayName>(item, 5)(item, 0, out var name));
                        try { names.Add(Marshal.PtrToStringUni(name) ?? string.Empty); }
                        finally { Marshal.FreeCoTaskMem(name); }
                    }
                    finally { Marshal.Release(item); }
                }
                Console.WriteLine($"view-export: filtered-shell-items flags=0x{flags:X8}; items={string.Join("|", names)}");
            }
            finally { Marshal.Release(array); }
        }
    }

    private static int WaitForItemCount(nint view, int expected, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        int count;
        do
        {
            Application.DoEvents();
            count = At<GetItemCount>(view, 7)(view, 2, out var current) < 0 ? -1 : current;
            if (count == expected) return count;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < until);
        return count;
    }

    private static void VerifyExportSnapshot(nint view, string[] expectedNames, string stage)
    {
        var snapshot = ShellFileViewExportCapture.Capture(view);
        if (snapshot.Columns.Count != 3 || snapshot.Rows.Count != expectedNames.Length ||
            !snapshot.Rows.Select(row => row[0]).SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase))
            throw new Exception($"{stage}: production export snapshot did not match visible Shell columns or order.");
        var nameColumn = snapshot.Columns[0];
        if (string.IsNullOrWhiteSpace(nameColumn) ||
            !snapshot.Rows.Single(row => row[0] == "three.txt")[2].Contains('3') ||
            !snapshot.Rows.Single(row => row[0] == "five.txt")[2].Contains('5') ||
            snapshot.Rows.Single(row => row[0] == "child")[2].Length != 0)
            throw new Exception($"{stage}: production export snapshot did not format visible names and sizes correctly.");
        Console.WriteLine($"view-export: stage={stage}; columns={string.Join("|", snapshot.Columns)}; order={string.Join("|", snapshot.Rows.Select(row => row[0]))}; values=PASS");
    }

    private static void VerifySnapshot(nint view, string[] expectedNames, string stage)
    {
        var names = ReadSnapshot(view, expectedNames.Length, stage);
        if (!names.SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase))
            throw new Exception($"{stage}: SVGIO_ALLVIEW order did not match current Shell sort order.");
    }

    private static void VerifyItemSet(nint view, string[] expectedItems, string stage)
    {
        var names = ReadSnapshot(view, expectedItems.Length, stage);
        if (!names.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedItems))
            throw new Exception($"{stage}: Shell item set did not match the isolated fixture.");
    }

    private static IReadOnlyList<string> ReadSnapshot(nint view, int expectedCount, string stage)
    {
        Check(ShellColumnManager.Capture(view, out var columns));
        if (columns is null || !columns.Select(column => column.Key.PropertyId)
                .SequenceEqual(new[] { NameKey.PropertyId, TypeKey.PropertyId, SizeKey.PropertyId }))
            throw new Exception($"{stage}: visible columns were not returned in their configured order.");

        var itemsId = ShellItemArrayId;
        Check(At<GetItems>(view, 8)(view, 0x80000002, ref itemsId, out var array)); // SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER.
        try
        {
            Check(At<GetCount>(array, 7)(array, out var count));
            if (count != expectedCount)
                throw new Exception($"{stage}: unexpected Shell item count {count}; indexed={string.Join("|", ReadIndexedNames(view))}.");
            var names = new List<string>((int)count);
            var sizes = new Dictionary<string, ulong?>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; index < count; index++)
            {
                Check(At<GetItemAt>(array, 8)(array, index, out var item));
                try
                {
                    Check(At<GetDisplayName>(item, 5)(item, 0, out var displayName));
                    try { names.Add(Marshal.PtrToStringUni(displayName) ?? string.Empty); }
                    finally { Marshal.FreeCoTaskMem(displayName); }
                    sizes[names[^1]] = ReadSize(item);
                }
                finally { Marshal.Release(item); }
            }

            var indexedNames = ReadIndexedNames(view);
            Console.WriteLine($"view-data: stage={stage}; allview={string.Join("|", names)}; view-index={string.Join("|", indexedNames)}; sizes={string.Join("|", sizes.Select(pair => $"{pair.Key}:{pair.Value?.ToString() ?? "unknown"}"))}");
            if (!names.SequenceEqual(indexedNames, StringComparer.OrdinalIgnoreCase))
                throw new Exception($"{stage}: SVGIO_ALLVIEW differs from IFolderView.Item(index) order.");
            if (names.Contains("five.txt", StringComparer.OrdinalIgnoreCase) && sizes.GetValueOrDefault("five.txt") != 5 ||
                names.Contains("three.txt", StringComparer.OrdinalIgnoreCase) && sizes.GetValueOrDefault("three.txt") != 3 ||
                names.Contains("child", StringComparer.OrdinalIgnoreCase) && sizes.GetValueOrDefault("child") is not null)
                throw new Exception($"{stage}: Shell item properties did not match the isolated fixture.");
            return names;
        }
        finally { Marshal.Release(array); }
    }

    private static IReadOnlyList<string> ReadIndexedNames(nint view)
    {
        var shellFolderId = new Guid("000214E6-0000-0000-C000-000000000046");
        Check(At<GetFolder>(view, 5)(view, ref shellFolderId, out var folder));
        try
        {
            Check(At<GetItemCount>(view, 7)(view, 2, out var count));
            var names = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                Check(At<GetItemPidl>(view, 6)(view, index, out var pidl));
                var strret = Marshal.AllocHGlobal(264);
                try
                {
                    Marshal.Copy(new byte[264], 0, strret, 264);
                    Check(At<GetDisplayNameOf>(folder, 11)(folder, pidl, 0, strret));
                    var text = new System.Text.StringBuilder(32768);
                    Check(StrRetToBuf(strret, pidl, text, (uint)text.Capacity));
                    names.Add(text.ToString());
                }
                finally
                {
                    Marshal.FreeHGlobal(strret);
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            return names;
        }
        finally { Marshal.Release(folder); }
    }

    private static ulong? ReadSize(nint shellItem)
    {
        var iid = ShellItem2Id;
        var result = Marshal.QueryInterface(shellItem, in iid, out var item2);
        Check(result);
        nint store = 0;
        var value = new Variant();
        try
        {
            var storeId = PropertyStoreId;
            Check(At<GetPropertyStore>(item2, 8)(item2, 8, ref storeId, out store)); // GPS_FASTPROPERTIESONLY.
            var sizeKey = SizeKey;
            Check(At<GetValue>(store, 5)(store, ref sizeKey, out value));
            return value.Type == 21 ? value.UInt64 : null;
        }
        finally
        {
            PropVariantClear(ref value);
            if (store != 0) Marshal.Release(store);
            Marshal.Release(item2);
        }
    }

    private static void ProbeViewPropertyCache(nint view)
    {
        var orderedNames = ReadIndexedNames(view);
        var childIndex = orderedNames.ToList().FindIndex(name => name.Equals("child", StringComparison.OrdinalIgnoreCase));
        if (childIndex < 0) throw new Exception("The isolated folder fixture was not present in the native view.");
        Check(At<GetItemPidl>(view, 6)(view, childIndex, out var childPidl));
        try
        {
            var expectedSize = 987654321UL;
            var sizeKey = SizeKey;
            var injected = new Variant { Type = 21, UInt64 = expectedSize }; // VT_UI8 / System.Size.
            Check(At<SetViewProperty>(view, 19)(view, childPidl, ref sizeKey, ref injected));
            Check(At<GetViewProperty>(view, 20)(view, childPidl, ref sizeKey, out var cached));
            if (cached.Type != 21 || cached.UInt64 != expectedSize)
                throw new Exception($"IFolderView2 property cache returned type={cached.Type}, value={cached.UInt64}.");
            VerifyItemSet(view, ["child", "five.txt", "three.txt"], "details-after-view-property-cache");
            Console.WriteLine($"view-data: SetViewProperty System.Size cache roundtrip=True; value={cached.UInt64}; underlying-item-unchanged=verified");
        }
        finally { Marshal.FreeCoTaskMem(childPidl); }
    }

    private static void Pump()
    {
        PumpFor(TimeSpan.FromMilliseconds(250));
    }

    private static void PumpFor(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
    }

    private static void Navigate(nint browser, string path)
    {
        Check(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try { Check(At<Browse>(browser, 13)(browser, pidl, 0x4000)); }
        finally { Marshal.FreeCoTaskMem(pidl); }
        Pump();
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        if (hr > 1) throw new COMException("Shell view data probe returned an unexpected positive status.", hr);
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SortColumn { public PropertyKey Key; public int Direction; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public ulong UInt64;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentView(nint self, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetGroup(nint self, ref PropertyKey key, int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetSortColumns(nint self, [In] SortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(nint self, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderFilter(nint self, nint filter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefreshShellView(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplayName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPropertyStore(nint self, uint flags, ref Guid iid, out nint store);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetValue(nint self, ref PropertyKey key, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemPidl(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetViewProperty(nint self, nint pidl, ref PropertyKey key, ref Variant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewProperty(nint self, nint pidl, ref PropertyKey key, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplayNameOf(nint self, nint pidl, uint flags, nint strret);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shlwapi.dll", EntryPoint = "StrRetToBufW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrRetToBuf(nint strret, nint pidl, System.Text.StringBuilder buffer, uint capacity);
}
