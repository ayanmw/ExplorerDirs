using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

// Checks whether BrowseToObject keeps a wrapped filesystem folder as the active view folder.
// The host runs only on PrivateDesktopShellProbe's separate Windows Desktop.
internal static class ExplorerBrowserWrapperProbe
{
    private static readonly Guid BrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid BrowserInterfaceId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateAdapter(nint folder, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref ProbeRect rect, ref ProbeFolderSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowseToObject(nint self, nint value, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentView(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolder(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurFolder(nint self, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, ref Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
}
