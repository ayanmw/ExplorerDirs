using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerDirs.App;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

// Checks whether BrowseToObject keeps a wrapped filesystem folder as the active view folder.
// The host runs only on PrivateDesktopShellProbe's separate Windows Desktop.
internal static class ExplorerBrowserWrapperProbe
{
    private static readonly Guid BrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid BrowserInterfaceId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid PersistFolder2Id = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
    private const uint SigdnFileSystemPath = 0x80058000;

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-BrowserWrapper-" + Guid.NewGuid().ToString("N"));
        nint browser = 0, parent = 0, folderPidl = 0, fileSystemFolder = 0, adapter = 0;
        nint shellView = 0, folderView = 0, viewFolder = 0, adapterIdentity = 0, viewIdentity = 0, persist = 0, currentPidl = 0;
        nint nativeLibrary = 0;
        uint initialized = 0;
        try
        {
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: create-fixture");
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "sample.txt"), "wrapper-probe");
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            var shellFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(SHBindToObject(0, folderPidl, 0, ref shellFolderId, out fileSystemFolder));

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            var createAdapter = Marshal.GetDelegateForFunctionPointer<CreateAdapter>(
                NativeLibrary.GetExport(nativeLibrary, "CreateStrictShellFolderAdapter"));
            Marshal.ThrowExceptionForHR(createAdapter(fileSystemFolder, out adapter));

            var browserClass = BrowserClassId;
            var browserId = BrowserInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out browser));
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated wrapper browser probe",
                0x90000000, 20, 20, 820, 620, 0, 0, 0, 0); // Visible only on the isolated probe Desktop.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var bounds = new ProbeRect { Right = 820, Bottom = 620 };
            var settings = new ProbeFolderSettings { Mode = 4 };
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: browse-to-object");
            Marshal.ThrowExceptionForHR(At<Initialize>(browser, 3)(browser, parent, ref bounds, ref settings));
            initialized = 1;
            var browseHr = At<BrowseToObject>(browser, 14)(browser, adapter, 0);
            if (browseHr < 0) Marshal.ThrowExceptionForHR(browseHr);
            Application.DoEvents();

            PrivateDesktopShellProbe.ReportStage("wrapper-probe: inspect-view-identity");
            var shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
            var folderView2Id = FolderView2Id;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
            var viewFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref viewFolderId, out viewFolder));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(adapter, in UnknownId, out adapterIdentity));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(viewFolder, in UnknownId, out viewIdentity));
            var adapterRetained = adapterIdentity == viewIdentity;

            var persistId = PersistFolder2Id;
            string currentPath = "unavailable";
            if (Marshal.QueryInterface(viewFolder, in persistId, out persist) >= 0 && persist != 0 &&
                At<GetCurFolder>(persist, 5)(persist, out currentPidl) >= 0 && currentPidl != 0 &&
                SHGetNameFromIDList(currentPidl, SigdnFileSystemPath, out var pathPointer) >= 0)
            {
                currentPath = Marshal.PtrToStringUni(pathPointer) ?? "unavailable";
                Marshal.FreeCoTaskMem(pathPointer);
            }

            var currentPathMatchesFixture = string.Equals(currentPath, fixture, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"explorerbrowser-wrapper-object: browse-to-object=0x{browseHr:X8}; adapter-folder-retained={adapterRetained}; current-path-matches-fixture={currentPathMatchesFixture}; current-path={currentPath}; fixture-path={fixture}; host-window-created=True; visible-to-user=False");
            if (adapterRetained || !currentPathMatchesFixture)
                throw new InvalidOperationException("BrowseToObject behavior differed from the expected filesystem rebind diagnostic.");
            return 0;
        }
        finally
        {
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-current-pidl");
            if (currentPidl != 0) Marshal.FreeCoTaskMem(currentPidl);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-persist");
            if (persist != 0) Marshal.Release(persist);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-view-identity");
            if (viewIdentity != 0) Marshal.Release(viewIdentity);
            if (adapterIdentity != 0) Marshal.Release(adapterIdentity);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-view-folder");
            if (viewFolder != 0) Marshal.Release(viewFolder);
            if (folderView != 0) Marshal.Release(folderView);
            if (shellView != 0) Marshal.Release(shellView);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: browser-destroy");
            if (initialized != 0 && browser != 0) _ = At<Destroy>(browser, 4)(browser);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: browser-release");
            if (browser != 0) Marshal.Release(browser);
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: parent-destroy");
            // ExplorerBrowser parent destruction can block after the browser is destroyed;
            // the isolated probe process owns this private-Desktop HWND until process exit.
            Console.WriteLine($"explorerbrowser-wrapper-object: parent-destroy-deferred={parent != 0 && IsWindow(parent)}");
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: release-adapter-and-folder");
            if (adapter != 0) Marshal.Release(adapter);
            if (fileSystemFolder != 0) Marshal.Release(fileSystemFolder);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (nativeLibrary != 0) NativeLibrary.Free(nativeLibrary);
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            PrivateDesktopShellProbe.ReportStage("wrapper-probe: cleanup-complete");
        }
    }

    internal static int RunNamespace()
    {
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-BrowserNamespace-" + classId.ToString("N"));
        nint browser = 0, parent = 0, rootPidl = 0, shellView = 0, folderView = 0, viewFolder = 0, persist = 0, currentPidl = 0;
        nint nativeLibrary = 0;
        uint initialized = 0;
        var classRegistrationCreated = false;
        var namespaceRegistrationCreated = false;
        try
        {
            PrivateDesktopShellProbe.ReportStage("namespace-probe: prepare-fixture-and-registration");
            Directory.CreateDirectory(Path.Combine(fixture, "child"));
            Directory.CreateDirectory(Path.Combine(fixture, "child", "nested"));
            File.WriteAllBytes(Path.Combine(fixture, "root.txt"), [1, 2, 3, 4]);
            File.WriteAllText(Path.Combine(fixture, "rename-source.txt"), "namespace rename contract");
            File.WriteAllBytes(Path.Combine(fixture, "child", "nested", "deep.bin"), Enumerable.Repeat((byte)7, 10).ToArray());

            nativeLibrary = NativeLibrary.Load(FindAdapterLibrary());
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetFolderClassId>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticFolderClassIdForProbe"))(ref classId));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetBackingPath>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticSortBackingPath"))(fixture));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                NativeLibrary.GetExport(nativeLibrary, "SetSyntheticCustomSizeColumnForProbe"))(1));
            RegisterSyntheticFolder(classKeyPath);
            classRegistrationCreated = true;
            RegisterSyntheticNamespace(namespaceKeyPath);
            namespaceRegistrationCreated = true;
            Marshal.ThrowExceptionForHR(SHParseDisplayName($"::{{{classId:D}}}", 0, out rootPidl, 0, out _));
            if (rootPidl == 0) throw new InvalidOperationException("The temporary namespace did not resolve to an absolute root PIDL.");

            var browserClass = BrowserClassId;
            var browserId = BrowserInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out browser));
            parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated registered namespace browser probe",
                0x90000000, 20, 20, 820, 620, 0, 0, 0, 0); // Visible only on the isolated probe Desktop.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var bounds = new ProbeRect { Right = 820, Bottom = 620 };
            var settings = new ProbeFolderSettings { Mode = 4 };
            PrivateDesktopShellProbe.ReportStage("namespace-probe: browse-to-root-pidl");
            Marshal.ThrowExceptionForHR(At<Initialize>(browser, 3)(browser, parent, ref bounds, ref settings));
            initialized = 1;
            var browseHr = At<BrowseToIdList>(browser, 13)(browser, rootPidl, 0);
            Marshal.ThrowExceptionForHR(browseHr);
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();

            PrivateDesktopShellProbe.ReportStage("namespace-probe: inspect-active-view");
            var shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
            var folderView2Id = FolderView2Id;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
            var viewFolderId = ShellFolder2Id;
            Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref viewFolderId, out viewFolder));

            var persistId = PersistFolder2Id;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(viewFolder, in persistId, out persist));
            Marshal.ThrowExceptionForHR(At<GetClassId>(persist, 3)(persist, out var activeClassId));
            Marshal.ThrowExceptionForHR(At<GetCurFolder>(persist, 5)(persist, out currentPidl));
            var classIdMatches = activeClassId == classId;
            var rootPidlMatches = PidlBytesEqual(rootPidl, currentPidl);
            var recursiveColumn = ReadHeader(viewFolder, 1);
            var recursiveColumnPresent = string.Equals(recursiveColumn, "Recursive Size", StringComparison.Ordinal);
            Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var itemCount)); // SVGIO_ALLVIEW
            var sizeKey = new PropertyKeyState(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12);
            var captureHr = ShellColumnManager.Capture(folderView, out var initialVisibleColumns);
            if (captureHr != 0 || initialVisibleColumns is null)
                throw new InvalidOperationException($"Could not capture ExplorerBrowser Shell columns: {captureHr}.");
            var requestedColumns = initialVisibleColumns.Any(column => column.Key == sizeKey)
                ? initialVisibleColumns
                : initialVisibleColumns.Append(new DisplayColumnState(sizeKey, 160)).ToArray();
            var applyHr = ShellColumnManager.Apply(folderView, requestedColumns);
            var verifyCaptureHr = ShellColumnManager.Capture(folderView, out var visibleColumnsAfterApply);
            if (applyHr != 0 || verifyCaptureHr != 0 || visibleColumnsAfterApply is null)
                throw new InvalidOperationException($"Could not apply/read ExplorerBrowser Shell columns: apply={applyHr}; capture={verifyCaptureHr}.");
            var recursiveSizeVisible = visibleColumnsAfterApply.Any(column => column.Key == sizeKey);
            var shellViewWindow = At<GetWindow>(shellView, 3)(shellView, out var viewWindow) >= 0 ? viewWindow : 0;
            var listView = FindChildByClass(shellViewWindow, "SysListView32");
            var header = listView == 0 ? 0 : SendMessageW(listView, 0x101F, 0, 0); // LVM_GETHEADER
            var headerTitles = header == 0 ? [] : ReadNativeHeaderTitles(header);
            var viewChildClasses = ReadChildClasses(shellViewWindow);
            if (!classIdMatches || !rootPidlMatches || !recursiveColumnPresent || !recursiveSizeVisible || itemCount != 3)
                throw new InvalidOperationException("ExplorerBrowser did not preserve the registered custom namespace view contract.");
            Console.WriteLine($"explorerbrowser-registered-namespace: browse-to-idlist=0x{browseHr:X8}; class-id-matches={classIdMatches}; root-pidl-matches={rootPidlMatches}; recursive-column-metadata={recursiveColumn}; recursive-size-visible-in-shell={recursiveSizeVisible}; visible-shell-column-count={visibleColumnsAfterApply.Count}; native-header=[{string.Join('|', headerTitles)}]; view-child-classes=[{string.Join('|', viewChildClasses)}]; row-count={itemCount}; fixture={fixture}; visible-to-user=False");

            nint backgroundMenu = 0, backgroundPopup = 0;
            var backgroundMenuHr = CreateViewObject(viewFolder, ContextMenuId, out backgroundMenu);
            var backgroundMenuQueryHr = unchecked((int)0x80004005);
            var backgroundMenuItemCount = -1;
            try
            {
                if (backgroundMenuHr >= 0 && backgroundMenu != 0)
                {
                    backgroundPopup = CreatePopupMenu();
                    if (backgroundPopup == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    backgroundMenuQueryHr = At<QueryContextMenu>(backgroundMenu, 3)(backgroundMenu, backgroundPopup, 0, 1, 0x7FFF, 0);
                    if (backgroundMenuQueryHr >= 0) backgroundMenuItemCount = GetMenuItemCount(backgroundPopup);
                }
            }
            finally
            {
                if (backgroundPopup != 0) DestroyMenu(backgroundPopup);
                if (backgroundMenu != 0) Marshal.Release(backgroundMenu);
            }
            Console.WriteLine($"explorerbrowser-registered-namespace: background-context-menu-hr=0x{backgroundMenuHr:X8}; query-hr=0x{backgroundMenuQueryHr:X8}; menu-items={backgroundMenuItemCount}");
            if (backgroundMenuHr < 0 || backgroundMenu == 0 || backgroundMenuQueryHr < 0 || backgroundMenuItemCount <= 0)
                throw new InvalidOperationException("The registered namespace did not expose native background-folder commands.");

            PrivateDesktopShellProbe.ReportStage("namespace-probe: native-item-menu-and-rename");
            Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "rename-source.txt", out var renameSource));
            nint contextMenu = 0, popup = 0;
            int shellMenuItemCount;
            try
            {
                Marshal.ThrowExceptionForHR(GetUiObjectOf(viewFolder, renameSource, ContextMenuId, out contextMenu));
                popup = CreatePopupMenu();
                if (popup == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Marshal.ThrowExceptionForHR(At<QueryContextMenu>(contextMenu, 3)(contextMenu, popup, 0, 1, 0x7FFF, 0));
                shellMenuItemCount = GetMenuItemCount(popup);
                if (shellMenuItemCount <= 0) throw new InvalidOperationException("The active namespace did not provide native file commands.");
            }
            finally
            {
                if (popup != 0) DestroyMenu(popup);
                if (contextMenu != 0) Marshal.Release(contextMenu);
                Marshal.FreeCoTaskMem(renameSource);
            }

            Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "rename-source.txt", out var renameSourceAgain));
            nint renamedItem = 0;
            try
            {
                Marshal.ThrowExceptionForHR(SetNameOf(viewFolder, renameSourceAgain, "rename-complete.txt", out renamedItem));
                if (File.Exists(Path.Combine(fixture, "rename-source.txt")) ||
                    !File.Exists(Path.Combine(fixture, "rename-complete.txt")))
                    throw new InvalidOperationException("The registered namespace rename did not reach the backing filesystem.");
            }
            finally
            {
                if (renamedItem != 0) Marshal.FreeCoTaskMem(renamedItem);
                Marshal.FreeCoTaskMem(renameSourceAgain);
            }
            Console.WriteLine($"explorerbrowser-registered-namespace: native-item-menu-items={shellMenuItemCount}; backing-filesystem-rename=True");

            PrivateDesktopShellProbe.ReportStage("namespace-probe: child-and-parent-navigation");
            Marshal.ThrowExceptionForHR(ParseDisplayName(viewFolder, "child", out var childItem));
            var expectedChildRoot = ILCombine(rootPidl, childItem);
            Marshal.FreeCoTaskMem(childItem);
            if (expectedChildRoot == 0) throw new InvalidOperationException("Could not compose a child namespace PIDL.");
            try
            {
                ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                Marshal.ThrowExceptionForHR(At<BrowseToIdList>(browser, 13)(browser, expectedChildRoot, 0));
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();
                ReadActiveFolder(browser, ref shellView, ref folderView, ref viewFolder, ref persist);
                Marshal.ThrowExceptionForHR(At<GetClassId>(persist, 3)(persist, out var childClassId));
                Marshal.ThrowExceptionForHR(At<GetCurFolder>(persist, 5)(persist, out currentPidl));
                var childIdentityMatches = childClassId == classId && PidlBytesEqual(expectedChildRoot, currentPidl);
                Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var childRowCount));
                if (!childIdentityMatches || childRowCount != 1)
                    throw new InvalidOperationException("Child navigation did not preserve the namespace PIDL or enumerate its backing folder.");

                ReleaseActiveFolder(ref shellView, ref folderView, ref viewFolder, ref persist, ref currentPidl);
                Marshal.ThrowExceptionForHR(At<BrowseToIdList>(browser, 13)(browser, rootPidl, 0));
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();
                ReadActiveFolder(browser, ref shellView, ref folderView, ref viewFolder, ref persist);
                Marshal.ThrowExceptionForHR(At<GetClassId>(persist, 3)(persist, out var returnedClassId));
                Marshal.ThrowExceptionForHR(At<GetCurFolder>(persist, 5)(persist, out currentPidl));
                var rootReturnMatches = returnedClassId == classId && PidlBytesEqual(rootPidl, currentPidl);
                Marshal.ThrowExceptionForHR(At<GetItemCount>(folderView, 7)(folderView, 2, out var rootRowCountAfterRename));
                if (!rootReturnMatches || rootRowCountAfterRename != 3)
                    throw new InvalidOperationException("Parent navigation did not return to the registered namespace root.");
                Console.WriteLine($"explorerbrowser-registered-namespace: child-pidl-matches={childIdentityMatches}; child-row-count={childRowCount}; return-to-root={rootReturnMatches}; root-row-count-after-rename={rootRowCountAfterRename}");
            }
            finally { Marshal.FreeCoTaskMem(expectedChildRoot); }
            return 0;
        }
        finally
        {
            PrivateDesktopShellProbe.ReportStage("namespace-probe: destroy-browser");
            if (currentPidl != 0) Marshal.FreeCoTaskMem(currentPidl);
            if (persist != 0) Marshal.Release(persist);
            if (viewFolder != 0) Marshal.Release(viewFolder);
            if (folderView != 0) Marshal.Release(folderView);
            if (shellView != 0) Marshal.Release(shellView);
            if (initialized != 0 && browser != 0) _ = At<Destroy>(browser, 4)(browser);
            if (browser != 0) Marshal.Release(browser);
            // ExplorerBrowser parent destruction can block; this HWND belongs to the private Desktop.
            if (parent != 0 && IsWindow(parent))
                Console.WriteLine("explorerbrowser-registered-namespace: parent-destroy-deferred-to-private-desktop-process-exit=True");
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            PrivateDesktopShellProbe.ReportStage("namespace-probe: unregister-temporary-namespace");
            if (namespaceRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistrationCreated) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            var registrationsRemoved = Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is null &&
                Registry.CurrentUser.OpenSubKey(classKeyPath) is null;
            Console.WriteLine($"explorerbrowser-registered-namespace: temporary-registrations-removed={registrationsRemoved}");
            if (nativeLibrary != 0)
            {
                var canUnload = Marshal.GetDelegateForFunctionPointer<CanUnload>(NativeLibrary.GetExport(nativeLibrary, "DllCanUnloadNow"))();
                if (canUnload == 0) NativeLibrary.Free(nativeLibrary);
                else Console.WriteLine($"explorerbrowser-registered-namespace: native-library-retained-until-process-exit=0x{canUnload:X8}");
            }
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RegisterSyntheticFolder(string classKeyPath)
    {
        using var classes = Registry.CurrentUser.OpenSubKey("Software\\Classes", writable: true)
            ?? throw new InvalidOperationException("HKCU\\Software\\Classes is unavailable.");
        using var existing = classes.OpenSubKey(classKeyPath["Software\\Classes\\".Length..]);
        if (existing is not null) throw new InvalidOperationException("The random probe CLSID already exists; refusing to overwrite it.");
        try
        {
            using var classKey = Registry.CurrentUser.CreateSubKey(classKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Shell-folder CLSID key.");
            classKey.SetValue(null, "ExplorerDirs isolated ExplorerBrowser namespace probe", RegistryValueKind.String);
            using var inproc = classKey.CreateSubKey("InprocServer32", writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary InprocServer32 key.");
            inproc.SetValue(null, FindAdapterLibrary(), RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            using var shellFolder = classKey.CreateSubKey("ShellFolder", writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary ShellFolder key.");
            shellFolder.SetValue("Attributes", unchecked((int)0xA0000000), RegistryValueKind.DWord);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static void RegisterSyntheticNamespace(string namespaceKeyPath)
    {
        if (Registry.CurrentUser.OpenSubKey(namespaceKeyPath) is not null)
            throw new InvalidOperationException("The random probe namespace key already exists; refusing to overwrite it.");
        try
        {
            using var namespaceKey = Registry.CurrentUser.CreateSubKey(namespaceKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not create the temporary Desktop\\NameSpace key.");
            namespaceKey.SetValue(null, "ExplorerDirs isolated ExplorerBrowser namespace probe", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static bool PidlBytesEqual(nint left, nint right)
    {
        if (left == 0 || right == 0) return false;
        var leftSize = ILGetSize(left);
        var rightSize = ILGetSize(right);
        if (leftSize == 0 || leftSize != rightSize) return false;
        var leftBytes = new byte[leftSize];
        var rightBytes = new byte[rightSize];
        Marshal.Copy(left, leftBytes, 0, leftBytes.Length);
        Marshal.Copy(right, rightBytes, 0, rightBytes.Length);
        return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }

    private static int ParseDisplayName(nint folder, string name, out nint item)
    {
        uint eaten = 0, attributes = 0;
        return At<ParseDisplayNameNative>(folder, 3)(folder, 0, 0, name, out eaten, out item, ref attributes);
    }

    private static int CreateViewObject(nint folder, Guid iid, out nint value)
        => At<CreateViewObjectNative>(folder, 8)(folder, 0, ref iid, out value);

    private static int GetUiObjectOf(nint folder, nint item, Guid iid, out nint value)
    {
        var items = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(items, item);
            uint reserved = 0;
            return At<GetUiObjectOfNative>(folder, 10)(folder, 0, 1, items, ref iid, ref reserved, out value);
        }
        finally { Marshal.FreeHGlobal(items); }
    }

    private static void ReadActiveFolder(nint browser, ref nint shellView, ref nint folderView, ref nint viewFolder, ref nint persist)
    {
        var shellViewId = ShellViewId;
        Marshal.ThrowExceptionForHR(At<GetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
        var folderView2Id = FolderView2Id;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
        var viewFolderId = ShellFolder2Id;
        Marshal.ThrowExceptionForHR(At<GetFolder>(folderView, 5)(folderView, ref viewFolderId, out viewFolder));
        var persistId = PersistFolder2Id;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(viewFolder, in persistId, out persist));
    }

    private static void ReleaseActiveFolder(ref nint shellView, ref nint folderView, ref nint viewFolder,
        ref nint persist, ref nint currentPidl)
    {
        if (currentPidl != 0) { Marshal.FreeCoTaskMem(currentPidl); currentPidl = 0; }
        if (persist != 0) { Marshal.Release(persist); persist = 0; }
        if (viewFolder != 0) { Marshal.Release(viewFolder); viewFolder = 0; }
        if (folderView != 0) { Marshal.Release(folderView); folderView = 0; }
        if (shellView != 0) { Marshal.Release(shellView); shellView = 0; }
    }

    private static int SetNameOf(nint folder, nint item, string name, out nint renamed)
        => At<SetNameOfNative>(folder, 12)(folder, 0, item, name, 0, out renamed);

    private static string ReadHeader(nint folder, uint column)
    {
        var details = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeShellDetails>());
        try
        {
            Marshal.StructureToPtr(default(NativeShellDetails), details, false);
            Marshal.ThrowExceptionForHR(At<GetDetailsOf>(folder, 18)(folder, 0, column, details));
            var value = Marshal.PtrToStructure<NativeShellDetails>(details);
            if (value.Text.Type != 0 || value.Text.Pointer == 0) return "<non-WSTR>";
            var text = Marshal.PtrToStringUni(value.Text.Pointer) ?? "";
            Marshal.FreeCoTaskMem(value.Text.Pointer);
            return text;
        }
        finally { Marshal.FreeCoTaskMem(details); }
    }

    private static nint FindChildByClass(nint parent, string className)
    {
        if (parent == 0) return 0;
        nint found = 0;
        _ = EnumChildWindows(parent, (child, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            if (GetClassNameW(child, name, name.Capacity) > 0 && name.ToString() == className)
            {
                found = child;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static string[] ReadNativeHeaderTitles(nint header)
    {
        var count = checked((int)SendMessageW(header, 0x1200, 0, 0)); // HDM_GETITEMCOUNT
        if (count < 0 || count > 64) throw new InvalidOperationException($"Invalid native header item count: {count}.");
        var text = Marshal.AllocHGlobal(512);
        try
        {
            var titles = new string[count];
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt16(text, 0);
                var item = new NativeHeaderItem { Mask = 2, Text = text, TextCapacity = 256 };
                if (SendMessageW(header, 0x120B, index, ref item) == 0) // HDM_GETITEMW
                    throw new InvalidOperationException($"Could not read native header item {index}.");
                titles[index] = Marshal.PtrToStringUni(text) ?? string.Empty;
            }
            return titles;
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    private static string[] ReadChildClasses(nint parent)
    {
        if (parent == 0) return [];
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        _ = EnumChildWindows(parent, (child, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            if (GetClassNameW(child, name, name.Capacity) > 0) classes.Add(name.ToString());
            return true;
        }, 0);
        return [.. classes];
    }

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before this probe.");
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential)] private struct ProbeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ProbeFolderSettings { public uint Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeStrRet { public uint Type; public nint Pointer; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeShellDetails { public int Format, Width; public NativeStrRet Text; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeHeaderItem
    {
        public uint Mask;
        public int Width;
        public nint Text;
        public nint Bitmap;
        public int TextCapacity;
        public int Format;
        public nint Param;
        public int Image;
        public int Order;
        public uint Type;
        public nint Filter;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateAdapter(nint folder, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref ProbeRect rect, ref ProbeFolderSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowseToObject(nint self, nint value, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentView(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurFolder(nint self, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetClassId(nint self, out Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowseToIdList(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int ParseDisplayNameNative(nint self, nint owner, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out nint item, ref uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUiObjectOfNative(nint self, nint owner, uint count, nint items, ref Guid iid, ref uint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObjectNative(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetNameOfNative(nint self, nint owner, nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint renamed);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint first, uint last, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetBackingPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetProbeFlag(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CanUnload();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, ref Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool EnumChildProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder className, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, int message, nint wParam, ref NativeHeaderItem item);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);
}
