using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

public static partial class SyntheticShellSortProbe
{
    private static readonly List<StreamViewHost> ProductDisplayProcessLifetimeHosts = [];
    internal static int RunProductSizeDisplay(string adapterPath, bool explorerBrowser = false, bool rootControl = false,
        FolderSizeSource source = FolderSizeSource.BuiltIn, bool sortPolicyExperiment = false, bool? encodedSortPolicy = null, bool columnStateSortPolicy = false, int encodedSortDirection = 1, bool dynamicSortPolicy = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProductSizeDisplay-" + Guid.NewGuid().ToString("N"));
        nint root = 0, folder = 0, view = 0, parent = 0, browserPointer = 0, folderView = 0;
        var smallPrefix = "12";
        var sizeEnabled = true;
        var sizeSubItem = 1;
        var siblingFixtures = new List<string>();
        var siblingLists = new List<(nint List, string Prefix)>();
        var displayBrowsers = new List<nint>();
        var displayBrowserViews = new List<nint>();
        nint diagnosticModule = 0, sortContextLease = 0, peerSortLease = 0, peerFolderView = 0, peerList = 0;
        GetCounter? enumCounter = null;
        ProfileShellNamespaceOwner? owner = null;
        StreamViewHost? host = null;
        try
        {
            foreach (var bytes in new[] { 12 * 1024, 25 * 1024 })
            {
                var directory = Path.Combine(fixture, "folder-" + bytes);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "data.bin"), new byte[bytes]);
            }
            File.WriteAllBytes(Path.Combine(fixture, "file-2048.bin"), new byte[2048]);
            Directory.CreateDirectory(Path.Combine(fixture, "folder-empty"));
            if (source == FolderSizeSource.Everything)
                WaitForIndex(fixture, new Dictionary<string, ulong>
                    { ["folder-12288"] = 12 * 1024, ["folder-25600"] = 25 * 1024, ["folder-empty"] = 0 });
            var classId = Guid.NewGuid();
            owner = ProfileShellNamespaceOwner.Start(classId, fixture, Path.GetFullPath(adapterPath));
            diagnosticModule = NativeLibrary.Load(Path.GetFullPath(adapterPath));
            if (NativeLibrary.TryGetExport(diagnosticModule, "GetProductSyntheticEnumStarted", out var counter))
                enumCounter = Marshal.GetDelegateForFunctionPointer<GetCounter>(counter);
            owner.SetFolderSizeSource(source, true);
            Console.WriteLine($"product-size-display-source: {source}");
            var mainIdentity = rootControl ? $"::{{{classId:D}}}" : CreatePathIdentityParsingName(classId, fixture);
            Console.WriteLine($"product-size-display-identity: encoded-root={!rootControl}");
            Check(SHParseDisplayName(mainIdentity, 0, out root, 0, out _));
            if (dynamicSortPolicy)
            {
                var prepare = Marshal.GetDelegateForFunctionPointer<CreateDynamicSizePolicy>(
                    NativeLibrary.GetExport(diagnosticModule, "CreateDynamicSizeSortPidlForProbe"));
                Check(prepare(root, 0, out var prepared, out sortContextLease));
                Marshal.FreeCoTaskMem(root); root = prepared;
            }
            if (encodedSortPolicy is { } initialPolicy)
            {
                var prepare = Marshal.GetDelegateForFunctionPointer<CreateEncodedSizePolicy>(
                    NativeLibrary.GetExport(diagnosticModule, "CreateSizeSortPolicyPidlForProbe"));
                Check(prepare(root, (initialPolicy ? 1 : 0) | (encodedSortDirection < 0 ? 2 : 0), out var prepared));
                Marshal.FreeCoTaskMem(root); root = prepared;
                Console.WriteLine($"product-size-sort-policy-root: encoded=True; mixed={initialPolicy}");
            }
            Check(BindProductDisplayFolder(0, root, 0, in ShellFolder2Id, out folder));
            var viewId = new Guid("000214E3-0000-0000-C000-000000000046");
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs product size display on private Desktop",
                0x90000000, 20, 20, 900, 600, 0, 0, 0, 0);
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var settings = new StreamProbeSettings { Mode = 4 };
            var bounds = new StreamProbeRect { Right = 880, Bottom = 560 };
            if (explorerBrowser)
            {
                view = OpenDisplayBrowser(root);
            }
            else
            {
                Check(At<CreateProductDisplayView>(folder, 8)(folder, 0, in viewId, out view));
                host = new StreamViewHost(parent, []) { View = view, BrowserSite = new ShellBrowserSite() };
                ProductDisplayProcessLifetimeHosts.Add(host);
                browserPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
                Check(At<CreateViewWindow>(view, 9)(view, 0, ref settings, browserPointer, ref bounds, out _));
                Check(At<ActivateView>(view, 7)(view, 2));
            }
            ShowWindow(parent, 4); // Never switches the private Desktop onto the user's display.
            var listView = FindListView(view);
            WaitForSizes("initial");
            for (var index = 1; index <= 3; index++)
            {
                var sibling = fixture + "-pane-" + index;
                siblingFixtures.Add(sibling);
                var childPath = Path.Combine(sibling, "folder-12288");
                Directory.CreateDirectory(childPath);
                File.WriteAllBytes(Path.Combine(childPath, "data.bin"), new byte[(30 + index) * 1024]);
                if (source == FolderSizeSource.Everything)
                    WaitForIndex(sibling, new Dictionary<string, ulong> { ["folder-12288"] = (ulong)(30 + index) * 1024 });
                Check(SHParseDisplayName(CreatePathIdentityParsingName(classId, sibling), 0, out var siblingRoot, 0, out _));
                Check(BindProductDisplayFolder(0, siblingRoot, 0, in ShellFolder2Id, out var siblingFolder));
                nint siblingView;
                if (explorerBrowser)
                {
                    siblingView = OpenDisplayBrowser(siblingRoot);
                }
                else
                {
                    Check(At<CreateProductDisplayView>(siblingFolder, 8)(siblingFolder, 0, in viewId, out siblingView));
                    var siblingHost = new StreamViewHost(parent, []) { View = siblingView, BrowserSite = new ShellBrowserSite() };
                    ProductDisplayProcessLifetimeHosts.Add(siblingHost);
                    var siblingBrowser = Marshal.GetComInterfaceForObject(siblingHost, typeof(IStreamProbeBrowser));
                    Check(At<CreateViewWindow>(siblingView, 9)(siblingView, 0, ref settings, siblingBrowser, ref bounds, out _));
                }
                siblingLists.Add((FindListView(siblingView), (30 + index).ToString()));
            }
            CheckSiblingSizes();
            var fileIndex = Array.IndexOf(ReadColumnTextsForRows(listView, 0, 4), "file-2048.bin");
            SetListViewState(listView, fileIndex, LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            File.WriteAllBytes(Path.Combine(fixture, "folder-12288", "data.bin"), new byte[40 * 1024]);
            if (source == FolderSizeSource.Everything)
                WaitForIndex(fixture, new Dictionary<string, ulong> { ["folder-12288"] = 40 * 1024 });
            owner.InvalidateFolderSizes(fixture);
            smallPrefix = "40";
            Check(SHParseDisplayName(mainIdentity + "\\folder-12288", 0,
                out var changed, 0, out _));
            try { SHChangeNotify(ShcneUpdateitem, 0x2000, changed, 0); }
            finally { Marshal.FreeCoTaskMem(changed); }
            WaitForSizes("invalidated-item-update");
            CheckSiblingSizes();
            var fileState = unchecked((uint)SendMessageW(listView, LvmGetItemState, fileIndex, (nint)(LvisSelected | LvisFocused)));
            if ((fileState & (LvisSelected | LvisFocused)) != (LvisSelected | LvisFocused))
                throw new InvalidOperationException("Local folder-size update moved the selected/focused file.");
            Check(At<RefreshView>(view, 8)(view));
            WaitForSizes("F5");
            Check(Marshal.QueryInterface(view, in FolderView2Id, out folderView));
            var target = new NativeShellViewStateTarget(folderView);
            Check(target.SetColumns([
                new(new PropertyKeyState(SizeFormatId, 10), 260),
                new(new PropertyKeyState(SizeFormatId, 4), 140),
                new(new PropertyKeyState(SizeFormatId, 12), 140)]));
            sizeSubItem = 2;
            WaitForSizes("reordered-columns");
            CheckSiblingSizes();
            Check(target.SetSorting([new(new PropertyKeyState(SizeFormatId, 12), 1)]));
            Pump(150);
            var sortedNames = ReadColumnTextsForRows(listView, 0, 4);
            if (Array.IndexOf(sortedNames, "folder-25600") >= Array.IndexOf(sortedNames, "folder-12288"))
                throw new InvalidOperationException("The displayed folder totals did not retain numeric ascending sorting.");
            Check(target.SetSorting([new(new PropertyKeyState(SizeFormatId, 12), -1)]));
            Pump(150);
            sortedNames = ReadColumnTextsForRows(listView, 0, 4);
            if (Array.IndexOf(sortedNames, "folder-12288") >= Array.IndexOf(sortedNames, "folder-25600"))
                throw new InvalidOperationException("The displayed folder totals did not retain numeric descending sorting.");
            if (dynamicSortPolicy)
            {
                Check(SHParseDisplayName(mainIdentity, 0, out var peerRoot, 0, out _));
                var prepare = Marshal.GetDelegateForFunctionPointer<CreateDynamicSizePolicy>(
                    NativeLibrary.GetExport(diagnosticModule, "CreateDynamicSizeSortPidlForProbe"));
                nint preparedPeer = 0;
                try { Check(prepare(peerRoot, 3, out preparedPeer, out peerSortLease)); }
                finally { Marshal.FreeCoTaskMem(peerRoot); }
                try
                {
                    var peerView = OpenDisplayBrowser(preparedPeer);
                    peerList = FindListView(peerView);
                    var peerReady = Stopwatch.StartNew();
                    while (SendMessageW(peerList, LvmGetItemCount, 0, 0).ToInt32() != 4 && peerReady.Elapsed < TimeSpan.FromSeconds(5)) Pump(20);
                    if (SendMessageW(peerList, LvmGetItemCount, 0, 0).ToInt32() != 4)
                        throw new TimeoutException("Same-path peer did not enumerate its four rows.");
                    Check(Marshal.QueryInterface(peerView, in FolderView2Id, out peerFolderView));
                    Check(ShellFolderViewOptions.SetNoEnumRefresh(peerFolderView, true));
                }
                finally { Marshal.FreeCoTaskMem(preparedPeer); }
            }
            if (sortPolicyExperiment)
            {
                var sortPolicyId = new Guid("37A49964-E858-4A39-A411-D552E136921B");
                nint policyFolder = 0, sortPolicy = 0;
                try
                {
                    Check(At<GetSizePolicyFolder>(folderView, 5)(folderView, in ShellFolder2Id, out policyFolder));
                    if (!columnStateSortPolicy) Check(Marshal.QueryInterface(policyFolder, in sortPolicyId, out sortPolicy));
                    var enumBeforePolicies = enumCounter?.Invoke();
                    var originalList = listView;
                    foreach (var mixed in encodedSortPolicy is { } encoded ? new[] { encoded } : columnStateSortPolicy ? new[] { true, false, true } : new[] { false, true, false })
                    foreach (var direction in encodedSortPolicy.HasValue ? new[] { encodedSortDirection } : new[] { 1, -1 })
                    {
                        if (columnStateSortPolicy) Check(SetSizeSortColumnStateForProbe(folderView, mixed));
                        else if (dynamicSortPolicy) Check(At<SetSizePolicy>(sortPolicy, 4)(sortPolicy, (mixed ? 1 : 0) | (direction < 0 ? 2 : 0)));
                        else Check(At<SetSizePolicy>(sortPolicy, 3)(sortPolicy, mixed ? 1 : 0));
                        var beforeNames = ReadColumnTextsForRows(listView, 0, 4);
                        Check(At<GetSizePolicyItem>(folderView, 6)(folderView, Array.IndexOf(beforeNames, "folder-empty"), out var emptyItem));
                        Check(At<GetSizePolicyItem>(folderView, 6)(folderView, Array.IndexOf(beforeNames, "file-2048.bin"), out var fileItem));
                        try
                        {
                            uint folderAttributes = 0x20000000, fileAttributes = 0x20000000;
                            Check(At<GetSizePolicyAttributes>(policyFolder, 9)(policyFolder, 1, [emptyItem], ref folderAttributes));
                            Check(At<GetSizePolicyAttributes>(policyFolder, 9)(policyFolder, 1, [fileItem], ref fileAttributes));
                            Console.WriteLine($"product-size-sort-folder-attributes: folder=0x{folderAttributes:X}; file=0x{fileAttributes:X}");
                            var direct = At<CompareSizePolicyItems>(policyFolder, 7)(policyFolder, 1, emptyItem, fileItem);
                            Console.WriteLine($"product-size-sort-policy-direct: mixed={mixed}; requested-direction={direction}; empty-vs-file={(short)direct}");
                        }
                        finally { Marshal.FreeCoTaskMem(emptyItem); Marshal.FreeCoTaskMem(fileItem); }
                        GetSizePolicyCount? comparisonCount = NativeLibrary.TryGetExport(diagnosticModule, "GetProductRecursiveCompareCount", out var compareAddress)
                            ? Marshal.GetDelegateForFunctionPointer<GetSizePolicyCount>(compareAddress) : null;
                        var beforeComparisons = comparisonCount?.Invoke() ?? -1;
                        Check(target.SetSorting([new(new PropertyKeyState(SizeFormatId, 12), direction)]));
                        var contextId = new Guid("000214E4-0000-0000-C000-000000000046");
                        Check(At<GetSizePolicyMenu>(view, 15)(view, 0, in contextId, out var contextPointer));
                        object context;
                        try { context = Marshal.GetObjectForIUnknown(contextPointer); }
                        finally { Marshal.Release(contextPointer); }
                        using (var command = new NativeShellCommandMenu(context, parent))
                        {
                            var result = command.InvokeCanonicalVerb(direction > 0 ? "sortascending" : "sortdescending");
                            if (result is null) throw new InvalidOperationException("Native size-sort direction verb is unavailable.");
                            Check(result.Value);
                        }
                        var expected = mixed
                            ? direction > 0 ? new[] { "folder-empty", "file-2048.bin", "folder-25600", "folder-12288" }
                                : new[] { "folder-12288", "folder-25600", "file-2048.bin", "folder-empty" }
                            : direction > 0 ? new[] { "folder-empty", "folder-25600", "folder-12288", "file-2048.bin" }
                                : new[] { "folder-12288", "folder-25600", "folder-empty", "file-2048.bin" };
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        do { Pump(20); sortedNames = ReadColumnTextsForRows(listView, 0, 4); }
                        while (!sortedNames.SequenceEqual(expected) && clock.Elapsed < TimeSpan.FromSeconds(3));
                        Console.WriteLine($"product-size-sort-policy: mixed={mixed}; direction={direction}; compare-delta={(comparisonCount?.Invoke() ?? -1) - beforeComparisons}; names=[{string.Join('|', sortedNames)}]");
                        if (!sortedNames.SequenceEqual(expected))
                        {
                            if (NativeLibrary.TryGetExport(diagnosticModule, "GetProductCompareDiagnosticCount", out var countAddress) &&
                                NativeLibrary.TryGetExport(diagnosticModule, "GetProductCompareDiagnostic", out var diagnosticAddress))
                            {
                                var count = Marshal.GetDelegateForFunctionPointer<GetCounter>(countAddress)();
                                var read = Marshal.GetDelegateForFunctionPointer<GetSizePolicyDiagnostic>(diagnosticAddress);
                                for (var index = Math.Max(0, count - 16); index < count; index++)
                                {
                                    var text = new StringBuilder(4096); Check(read(index, text, (uint)text.Capacity));
                                    Console.WriteLine("product-size-sort-policy-compare: " + text);
                                }
                            }
                            throw new InvalidOperationException("Folder/file size order disagrees with the selected mixed/separate policy.");
                        }
                        if (dynamicSortPolicy)
                        {
                            // Force the peer to compare again: merely retaining old painted order
                            // would not prove that its independently bound policy stayed isolated.
                            var peerTarget = new NativeShellViewStateTarget(peerFolderView);
                            Check(peerTarget.SetSorting([new(new PropertyKeyState(SizeFormatId, 12), 1)]));
                            Pump(50);
                            Check(peerTarget.SetSorting([new(new PropertyKeyState(SizeFormatId, 12), -1)]));
                            var peerExpected = new[] { "folder-12288", "folder-25600", "file-2048.bin", "folder-empty" };
                            var peerClock = Stopwatch.StartNew();
                            while (!ReadPeerNames().SequenceEqual(peerExpected) && peerClock.Elapsed < TimeSpan.FromSeconds(3)) Pump(20);
                            if (!ReadPeerNames().SequenceEqual(peerExpected))
                                throw new InvalidOperationException("Same-path peer inherited the main view's sorting policy.");
                            if (FindListView(view) != originalList)
                                throw new InvalidOperationException("Policy change recreated the file view.");
                            Console.WriteLine($"product-size-sort-peer: main-mixed={mixed}; main-direction={direction}; peer=mixed-desc; independent=True; same-list=True");
                        }
                    }
                    if (dynamicSortPolicy && enumBeforePolicies is { } initialEnums && enumCounter!() != initialEnums)
                        throw new InvalidOperationException("Changing sort policy re-enumerated the directory.");
                }
                finally
                {
                    if (sortPolicy != 0) Marshal.Release(sortPolicy);
                    if (policyFolder != 0) Marshal.Release(policyFolder);
                }
            }
            owner.SetFolderSizeSource(source, false);
            sizeEnabled = false;
            Check(At<RefreshView>(view, 8)(view));
            WaitForSizes("disabled-ordinary-file-preserved");
            owner.SetFolderSizeSource(source, true);
            sizeEnabled = true;
            Check(At<RefreshView>(view, 8)(view));
            WaitForSizes("reenabled");
            Check(ShellFolderViewOptions.SetNoEnumRefresh(folderView, true));
            using var selectionMessages = new SelectionMessageObserver(listView);
            var anchor = Array.IndexOf(ReadColumnTextsForRows(listView, 0, 4), "file-2048.bin");
            // Establish selection through the Shell contract, not just list paint state.
            Check(At<SelectItem>(folderView, 15)(folderView, anchor, 0x1u | 0x4u | 0x10u));
            Pump(150);
            TraceAnchor("before-create");
            var enumerationsBefore = enumCounter?.Invoke();
            Console.WriteLine("product-size-display-cells: PASS (four panes, zero, update, F5, columns, numeric ascending/descending, source off/on); automatic row qualification follows.");
            var addedFile = Path.Combine(fixture, "automatic-file.bin");
            File.WriteAllBytes(addedFile, new byte[3072]);
            Check(SHParseDisplayName(addedFile, 0, out var physicalAdded, 0, out _));
            try { SHChangeNotify(ShcneCreate, 0x2000, physicalAdded, 0); }
            finally { Marshal.FreeCoTaskMem(physicalAdded); }
            WaitForRowCount(5);
            TraceAnchor("after-create");
            var addedNames = ReadColumnTextsForRows(listView, 0, 5);
            var addedIndex = Array.IndexOf(addedNames, "automatic-file.bin");
            if (addedIndex < 0 || ReadColumnTextsForRows(listView, sizeSubItem, 5)[addedIndex] != "3 KB")
                throw new InvalidOperationException("Physical create notification did not append the ordinary file and size.");
            var renamedFile = Path.Combine(fixture, "automatic-renamed.bin");
            Check(SHParseDisplayName(addedFile, 0, out var oldPhysical, 0, out _));
            File.Move(addedFile, renamedFile);
            Check(SHParseDisplayName(renamedFile, 0, out var physicalRenamed, 0, out _));
            try { SHChangeNotify(ShcneRenameItem, 0x2000, oldPhysical, physicalRenamed); }
            finally { Marshal.FreeCoTaskMem(oldPhysical); }
            WaitForName("automatic-renamed.bin");
            TraceAnchor("after-rename");
            CheckSiblingSizes();
            File.Delete(renamedFile);
            try { SHChangeNotify(ShcneDelete, 0x2000, physicalRenamed, 0); }
            finally { Marshal.FreeCoTaskMem(physicalRenamed); }
            WaitForRowCount(4);
            TraceAnchor("after-delete");
            Check(SHParseDisplayName(fixture, 0, out var physicalRoot, 0, out _));
            try { SHChangeNotify(ShcneUpdatedir, 0x2000, physicalRoot, 0); }
            finally { Marshal.FreeCoTaskMem(physicalRoot); }
            WaitForRowCount(4);
            WaitForSizes("physical-notifications");
            TraceAnchor("after-directory-update");
            anchor = Array.IndexOf(ReadColumnTextsForRows(listView, 0, 4), "file-2048.bin");
            var anchorState = unchecked((uint)SendMessageW(listView, LvmGetItemState, anchor, (nint)(LvisSelected | LvisFocused)));
            if ((anchorState & (LvisSelected | LvisFocused)) != (LvisSelected | LvisFocused))
                throw new InvalidOperationException("Automatic create/rename/delete lost the unaffected selected/focused file.");
            if (enumerationsBefore is { } before)
            {
                var delta = enumCounter!() - before;
                Console.WriteLine($"product-size-display-automatic-enumerations: delta={delta}");
                if (delta != 0) throw new InvalidOperationException("Soft notifications re-enumerated a complete directory.");
            }
            Check(target.SetGrouping(new PropertyKeyState(SizeFormatId, 12), true));
            Pump(150);
            WaitForSizes("grouped-size");
            Check(target.SetGrouping(null, true));
            Pump(150);
            WaitForSizes("ungrouped-size");
            Console.WriteLine($"Product size display: PASS (ExplorerBrowser={explorerBrowser}; four real Shell views with one profile, independent same-name folders, 12/25 KB and zero, invalidated 40 KB update, selected/focused file preserved, F5, column reorder and numeric sorting; private Desktop).");
            return 0;

            nint OpenDisplayBrowser(nint location)
            {
                var classId = new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
                var iid = new Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
                Check(CoCreateInstance(ref classId, 0, 1, ref iid, out var browser));
                displayBrowsers.Add(browser);
                ShellBrowserSite.SetSite(Marshal.GetObjectForIUnknown(browser), new ShellBrowserSite());
                Check(ShellFolderViewOptions.EnableCustomPosition(browser));
                Check(At<InitializeDisplayBrowser>(browser, 3)(browser, parent, ref bounds, ref settings));
                Check(At<OptionsDisplayBrowser>(browser, 11)(browser, 0xC0));
                Check(At<BrowseDisplayBrowser>(browser, 13)(browser, location, 0));
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Pump(40);
                    if (At<GetDisplayBrowserView>(browser, 17)(browser, in viewId, out var current) >= 0 && current != 0)
                    {
                        // GetCurrentView can publish before its native child exists.
                        try { _ = FindListView(current); displayBrowserViews.Add(current); return current; }
                        catch (InvalidOperationException) { Marshal.Release(current); }
                    }
                }
                throw new TimeoutException("ExplorerBrowser did not create a Shell view.");
            }

            string[] ReadPeerNames() => SendMessageW(peerList, LvmGetItemCount, 0, 0).ToInt32() == 4
                ? ReadColumnTextsForRows(peerList, 0, 4) : [];

            void WaitForIndex(string parentPath, IReadOnlyDictionary<string, ulong> expected)
            {
                // Everything indexes filesystem changes asynchronously. This case
                // verifies native consumption of indexed values, not index latency.
                // Unindexed/unknown results must remain distinct from a measured zero.
                var timer = Stopwatch.StartNew();
                IndexedSizePage? page = null;
                while (timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    page = EverythingSizeClient.QueryChildrenAsync(parentPath, 0, CancellationToken.None).GetAwaiter().GetResult();
                    if (page.Status != EverythingSizeStatus.Available)
                        throw new InvalidOperationException($"Everything source prerequisite failed: {page.Status}.");
                    if (expected.All(pair => page.Items.Any(item => item.IsDirectory &&
                        Path.GetFileName(item.Path) == pair.Key && item.Bytes == pair.Value)))
                    {
                        Console.WriteLine($"product-size-display-index-ready: expected-folders={expected.Count}; elapsed-ms={timer.ElapsedMilliseconds}");
                        return;
                    }
                    Pump(50);
                }
                throw new TimeoutException($"Everything did not index the fixture values in time; rows={page?.Items.Count}.");
            }

            void CheckSiblingSizes()
            {
                foreach (var sibling in siblingLists)
                {
                    var clock = Stopwatch.StartNew();
                    string value = "";
                    while (clock.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        Pump(40);
                        if (SendMessageW(sibling.List, LvmGetItemCount, 0, 0).ToInt32() != 1) continue;
                        value = ReadColumnTextsForRows(sibling.List, 1, 1)[0];
                        if (value.StartsWith(sibling.Prefix, StringComparison.Ordinal) && value.Contains("KB")) break;
                    }
                    if (!value.StartsWith(sibling.Prefix, StringComparison.Ordinal) || !value.Contains("KB"))
                        throw new InvalidOperationException($"Independent same-name folder expected {sibling.Prefix} KB, actual {value}.");
                }
            }

            void WaitForRowCount(int expectedCount)
            {
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Pump(40);
                    if (SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32() == expectedCount) return;
                }
                throw new TimeoutException($"Physical notification expected {expectedCount} rows.");
            }

            void WaitForName(string expectedName)
            {
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Pump(40);
                    if (ReadColumnTextsForRows(listView, 0, 5).Contains(expectedName)) return;
                }
                throw new TimeoutException("Physical rename notification did not rename the existing row.");
            }

            void TraceAnchor(string stage)
            {
                selectionMessages.Drain(stage);
                var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
                var index = Array.IndexOf(ReadColumnTextsForRows(listView, 0, count), "file-2048.bin");
                Check(At<GetDisplayFolderFlags>(folderView, 25)(folderView, out var flags));
                // Compare Shell identity state with list paint state before changing recovery code.
                // External insertion can reorder rows, so always resolve the anchor by name.
                Check(At<GetSizePolicyItem>(folderView, 6)(folderView, index, out var identity));
                uint shellState;
                try { Check(At<GetDisplaySelectionState>(folderView, 33)(folderView, identity, out shellState)); }
                finally { Marshal.FreeCoTaskMem(identity); }
                var selected = new List<string>();
                var names = ReadColumnTextsForRows(listView, 0, count);
                for (var row = 0; row < count; row++)
                    if ((SendMessageW(listView, LvmGetItemState, row, (nint)LvisSelected).ToInt64() & LvisSelected) != 0)
                        selected.Add(names[row]);
                Console.WriteLine($"product-size-display-anchor: stage={stage}; index={index}; flags=0x{flags:X}; state=0x{SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)).ToInt64():X}; shell-state=0x{shellState:X}; selected=[{string.Join('|', selected)}]");
            }

            void WaitForSizes(string stage)
            {
                var timer = Stopwatch.StartNew();
                string[] names = [], sizes = [];
                while (timer.Elapsed < TimeSpan.FromSeconds(8))
                {
                    Pump(40);
                    var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
                    if (count != 4) continue;
                    names = ReadColumnTextsForRows(listView, 0, count);
                    sizes = ReadColumnTextsForRows(listView, sizeSubItem, count);
                    var expected = new Dictionary<string, string>
                    { ["folder-12288"] = smallPrefix, ["folder-25600"] = "25", ["file-2048.bin"] = "2", ["folder-empty"] = "0" };
                    if (names.Select((name, index) => !sizeEnabled && name.StartsWith("folder-", StringComparison.Ordinal)
                        ? string.IsNullOrWhiteSpace(sizes[index])
                        : expected.TryGetValue(name, out var prefix) &&
                        sizes[index].StartsWith(prefix, StringComparison.Ordinal) &&
                        sizes[index].Contains(name == "folder-empty" ? "B" : "KB", StringComparison.OrdinalIgnoreCase)).All(value => value))
                    {
                        Console.WriteLine($"product-size-display-{stage}: {string.Join('|', names.Zip(sizes, (name, size) => name + '=' + size))}");
                        return;
                    }
                }
                throw new TimeoutException($"Actual Shell size cells failed at {stage}: {string.Join('|', names.Zip(sizes, (name, size) => name + '=' + size))}");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("product-size-display failure: " + error.Message);
            throw;
        }
        finally
        {
            if (folderView != 0) Marshal.Release(folderView);
            foreach (var current in displayBrowserViews) Marshal.Release(current);
            foreach (var browser in displayBrowsers)
            {
                Check(At<DestroyDisplayBrowser>(browser, 4)(browser));
                Marshal.Release(browser);
            }
            if (displayBrowsers.Count != 0)
                Console.WriteLine($"product-size-display-lifecycle: destroyed={displayBrowsers.Count}; surviving-list-HWNDs={siblingLists.Count(item => IsWindow(item.List))}");
            // The private-desktop harness owns this process. DestroyViewWindow can
            // synchronously activate the system IME during STA teardown; leave these
            // private HWND/COM references to process exit, as the existing harness does.
            // Registration and fixture ownership are still cleaned explicitly.
            if (peerFolderView != 0) Marshal.Release(peerFolderView);
            owner?.Dispose();
            if (peerSortLease != 0)
                Marshal.GetDelegateForFunctionPointer<ReleaseDynamicSizePolicy>(
                    NativeLibrary.GetExport(diagnosticModule, "ReleaseDynamicSizeSortLeaseForProbe"))(peerSortLease);
            if (sortContextLease != 0)
                Marshal.GetDelegateForFunctionPointer<ReleaseDynamicSizePolicy>(
                    NativeLibrary.GetExport(diagnosticModule, "ReleaseDynamicSizeSortLeaseForProbe"))(sortContextLease);
            if (diagnosticModule != 0) NativeLibrary.Free(diagnosticModule);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
            foreach (var sibling in siblingFixtures)
                if (Directory.Exists(sibling)) Directory.Delete(sibling, true);
            GC.KeepAlive(ProductDisplayProcessLifetimeHosts);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "SHBindToObject")]
    private static extern int BindProductDisplayFolder(nint parent, nint pidl, nint bind, in Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateProductDisplayView(nint self, nint owner, in Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InitializeDisplayBrowser(nint self, nint parent, ref StreamProbeRect bounds, ref StreamProbeSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int OptionsDisplayBrowser(nint self, uint options);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowseDisplayBrowser(nint self, nint location, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDisplayBrowserView(nint self, in Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyDisplayBrowser(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDisplayFolderFlags(nint self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSizePolicyFolder(nint self, in Guid iid, out nint folder);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetSizePolicy(nint self, int mixed);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSizePolicyMenu(nint self, uint flags, in Guid iid, out nint context);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDisplaySelectionState(nint self, nint item, out uint state);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSizePolicyItem(nint self, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CompareSizePolicyItems(nint self, nint column, nint left, nint right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long GetSizePolicyCount();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSizePolicyAttributes(nint self, uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] items, ref uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateDynamicSizePolicy(nint source, uint policy, out nint output, out nint lease);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ReleaseDynamicSizePolicy(nint lease);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateEncodedSizePolicy(nint source, int mixed, out nint output);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetSizePolicyDiagnostic(int index, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, uint capacity);



    private static int SetSizeSortColumnStateForProbe(IntPtr folderView, bool mixed, Func<bool>? isCurrent = null)
    {
        bool Current() => isCurrent?.Invoke() ?? true;
        const int aborted = unchecked((int)0x80004004);
        const uint stateMask = 0x10, noFolderSeparation = 0x4;
        if (!Current()) return aborted;
        var sizeColumnManagerId = new Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453");
        const int InvalidData = unchecked((int)0x8007000D);
        var result = Marshal.QueryInterface(folderView, in sizeColumnManagerId, out var manager);
        if (result < 0) return result;
        try
        {
            if (!Current()) return aborted;
            var key = new ShellColumnManager.NativeKey { FormatId = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), PropertyId = 12 };
            var info = new ShellColumnManager.ColumnInfo { Size = (uint)Marshal.SizeOf<ShellColumnManager.ColumnInfo>(), Mask = stateMask, Name = string.Empty };
            result = At<SizeSortColumnInfo>(manager, 4)(manager, ref key, ref info);
            if (!Current()) return aborted;
            if (result != 0) return result < 0 ? result : InvalidData;
            if ((info.Mask & stateMask) == 0) return InvalidData;
            var state = mixed ? info.State | noFolderSeparation : info.State & ~noFolderSeparation;
            if (state == info.State) return 0;
            // Change only the folder separation bit; visibility and width belong to the saved column layout.
            info.Mask = stateMask; info.State = state;
            result = At<SizeSortColumnInfo>(manager, 3)(manager, ref key, ref info);
            if (!Current()) return aborted;
            if (result < 0) return result;
            var readback = new ShellColumnManager.ColumnInfo { Size = (uint)Marshal.SizeOf<ShellColumnManager.ColumnInfo>(), Mask = stateMask, Name = string.Empty };
            result = At<SizeSortColumnInfo>(manager, 4)(manager, ref key, ref readback);
            if (!Current()) return aborted;
            Console.WriteLine($"size-sort-column-state: requested=0x{state:X}; readback=0x{readback.State:X}; hr=0x{result:X8}");
            return result < 0 ? result : readback.State == state ? 0 : InvalidData;
        }
        finally { Marshal.Release(manager); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SizeSortColumnInfo(nint self, ref ShellColumnManager.NativeKey key, ref ShellColumnManager.ColumnInfo info);

    internal static int RunSizeSortContextPidlSmoke(string adapterPath)
    {
        var module = NativeLibrary.Load(Path.GetFullPath(adapterPath));
        try
        {
            var verify = Marshal.GetDelegateForFunctionPointer<VerifySizeSortContext>(
                NativeLibrary.GetExport(module, "VerifySizeSortPolicyPidlForProbe"));
            Check(verify());
            Console.WriteLine("Size-sort context PIDL: PASS (both modes, 1024-character boundary and multi-segment roots, invalid/mismatched policy rejected; no window or registration).");
            return 0;
        }
        finally { NativeLibrary.Free(module); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VerifySizeSortContext();

}
