using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.App;
using ExplorerDirs.NativeProbe;

public static partial class SyntheticShellSortProbe
{
    private static readonly List<StreamViewHost> ProductDisplayProcessLifetimeHosts = [];
    internal static int RunProductSizeDisplay(string adapterPath, bool explorerBrowser = false, bool rootControl = false)
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
        nint diagnosticModule = 0;
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
            var classId = Guid.NewGuid();
            owner = ProfileShellNamespaceOwner.Start(classId, fixture, Path.GetFullPath(adapterPath));
            diagnosticModule = NativeLibrary.Load(Path.GetFullPath(adapterPath));
            if (NativeLibrary.TryGetExport(diagnosticModule, "GetProductSyntheticEnumStarted", out var counter))
                enumCounter = Marshal.GetDelegateForFunctionPointer<GetCounter>(counter);
            owner.SetFolderSizeSource(FolderSizeSource.BuiltIn, true);
            var mainIdentity = rootControl ? $"::{{{classId:D}}}" : CreatePathIdentityParsingName(classId, fixture);
            Console.WriteLine($"product-size-display-identity: encoded-root={!rootControl}");
            Check(SHParseDisplayName(mainIdentity, 0, out root, 0, out _));
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
            owner.SetFolderSizeSource(FolderSizeSource.BuiltIn, false);
            sizeEnabled = false;
            Check(At<RefreshView>(view, 8)(view));
            WaitForSizes("disabled-ordinary-file-preserved");
            owner.SetFolderSizeSource(FolderSizeSource.BuiltIn, true);
            sizeEnabled = true;
            Check(At<RefreshView>(view, 8)(view));
            WaitForSizes("reenabled");
            Check(ShellFolderViewOptions.SetNoEnumRefresh(folderView, true));
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
                var count = SendMessageW(listView, LvmGetItemCount, 0, 0).ToInt32();
                var index = Array.IndexOf(ReadColumnTextsForRows(listView, 0, count), "file-2048.bin");
                Check(At<GetDisplayFolderFlags>(folderView, 25)(folderView, out var flags));
                Console.WriteLine($"product-size-display-anchor: stage={stage}; index={index}; flags=0x{flags:X}; state=0x{SendMessageW(listView, LvmGetItemState, index, (nint)(LvisSelected | LvisFocused)).ToInt64():X}");
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
            owner?.Dispose();
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
}
