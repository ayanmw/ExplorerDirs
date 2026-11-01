using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ExplorerDirs.App;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

// Isolated lifecycle spike: two portable profiles own independent Shell registrations.
internal static class ShellNamespaceOwnerProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid BrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid BrowserInterfaceId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NamespaceOwners-" + Guid.NewGuid().ToString("N"));
        var profileA = Path.Combine(fixture, "profile-a");
        var profileB = Path.Combine(fixture, "profile-b");
        var rootA = Path.Combine(fixture, "root-a");
        var rootB = Path.Combine(fixture, "root-b");
        var ready = Path.Combine(fixture, "child.ready");
        var verify = Path.Combine(fixture, "child.verify");
        var verified = Path.Combine(fixture, "child.verified");
        var verifyAgain = Path.Combine(fixture, "child.verify-again");
        var verifiedAgain = Path.Combine(fixture, "child.verified-again");
        var stop = Path.Combine(fixture, "child.stop");
        Guid classIdA = Guid.Empty;
        Guid classIdB = Guid.Empty;
        var classRegisteredA = false;
        var namespaceRegisteredA = false;
        var classRegisteredB = false;
        var namespaceRegisteredB = false;
        Process? child = null;
        var childOutput = new StringBuilder();
        var childError = new StringBuilder();
        var oleInitialized = false;
        try
        {
            var oleResult = OleInitialize(0);
            if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
            oleInitialized = true;
            Directory.CreateDirectory(profileA);
            Directory.CreateDirectory(profileB);
            Directory.CreateDirectory(rootA);
            Directory.CreateDirectory(rootB);
            File.WriteAllText(Path.Combine(rootA, "profile-a.txt"), "A");
            File.WriteAllText(Path.Combine(rootB, "profile-b.txt"), "B");
            Directory.CreateDirectory(Path.Combine(rootA, "shared-folder"));
            Directory.CreateDirectory(Path.Combine(rootB, "shared-folder"));
            File.WriteAllBytes(Path.Combine(rootA, "shared-folder", "payload.bin"), new byte[13]);
            File.WriteAllBytes(Path.Combine(rootB, "shared-folder", "payload.bin"), new byte[29]);
            classIdA = new ShellNamespaceProfileStore(Path.Combine(profileA, "ShellNamespace.ini")).LoadOrCreate()
                ?? throw new InvalidOperationException("Profile A did not produce a valid persistent provider identity.");
            classIdB = new ShellNamespaceProfileStore(Path.Combine(profileB, "ShellNamespace.ini")).LoadOrCreate()
                ?? throw new InvalidOperationException("Profile B did not produce a valid persistent provider identity.");
            if (classIdA == classIdB) throw new InvalidOperationException("Different portable profiles received the same provider CLSID.");

            RegisterVolatileNamespace(classIdA, FindAdapterLibrary());
            classRegisteredA = namespaceRegisteredA = true;
            using var adapterA = new AdapterModule(classIdA, rootA);
            using var initialBrowserA = BrowserInstance.Open(classIdA, adapterA.Module,
                "profile-a.txt", "profile-b.txt", 13);

            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add(typeof(ShellNamespaceOwnerProbe).Assembly.Location);
            start.ArgumentList.Add("--shell-namespace-owner-profile-child");
            start.ArgumentList.Add(Path.Combine(profileB, "ShellNamespace.ini"));
            start.ArgumentList.Add(rootB);
            start.ArgumentList.Add(ready);
            start.ArgumentList.Add(verify);
            start.ArgumentList.Add(verified);
            start.ArgumentList.Add(verifyAgain);
            start.ArgumentList.Add(verifiedAgain);
            start.ArgumentList.Add(stop);
            child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated profile B registration owner.");
            child.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) childOutput.AppendLine(eventArgs.Data); };
            child.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) childError.AppendLine(eventArgs.Data); };
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            WaitForFile(ready, child, "Profile B did not register its namespace.");
            if (File.ReadAllText(ready) != classIdB.ToString("D"))
                throw new InvalidOperationException("Profile B registered a CLSID different from its portable INI identity.");
            classRegisteredB = namespaceRegisteredB = true;

            initialBrowserA.Verify("profile-a.txt", "profile-b.txt", 13);
            initialBrowserA.Dispose();
            DeleteNamespace(classIdA);
            classRegisteredA = namespaceRegisteredA = false;
            File.WriteAllText(verify, "verify-after-a-close");
            WaitForFile(verified, child, "Profile B view changed after profile A closed.", ready + ".stage");

            var reopenedClassIdA = new ShellNamespaceProfileStore(Path.Combine(profileA, "ShellNamespace.ini")).LoadOrCreate();
            if (reopenedClassIdA != classIdA)
                throw new InvalidOperationException("Profile A changed its stable CLSID after restart.");
            RegisterVolatileNamespace(classIdA, FindAdapterLibrary());
            classRegisteredA = namespaceRegisteredA = true;
            using var reopenedBrowserA = BrowserInstance.Open(classIdA, adapterA.Module,
                "profile-a.txt", "profile-b.txt", 13);
            reopenedBrowserA.Verify("profile-a.txt", "profile-b.txt", 13);
            File.WriteAllText(verifyAgain, "verify-after-a-reopen");
            WaitForFile(verifiedAgain, child, "Profile B view changed after profile A reopened.", ready + ".stage");

            File.WriteAllText(stop, "stop");
            if (!child.WaitForExit(15_000))
            {
                var stage = File.Exists(ready + ".stage") ? File.ReadAllText(ready + ".stage") : "<missing>";
                throw new TimeoutException($"Profile B owner did not stop and remove its namespace registration. Last child stage: {stage}");
            }
            if (child.ExitCode != 0)
                throw new InvalidOperationException($"Profile B owner failed: exit={child.ExitCode}; stdout={childOutput}; stderr={childError}");
            // Volatile registry keys are session-scoped, not process-scoped. Once the owner
            // process exits and releases its Browser, the coordinator can remove its keys.
            DeleteNamespace(classIdB);
            classRegisteredB = namespaceRegisteredB = false;
            reopenedBrowserA.Verify("profile-a.txt", "profile-b.txt", 13);
            Console.WriteLine($"shell-namespace-profile-owners: distinct-profile-clsids=True; profile-B-view-survived-A-close-and-reopen=True; reopening-A-reused-clsid=True; profile-A-view-survived-B-close=True; volatile-registrations-cleaned=True; profile-a={classIdA:D}; profile-b={classIdB:D}");
            return 0;
        }
        finally
        {
            if (child is { HasExited: false })
            {
                try { File.WriteAllText(stop, "stop"); } catch (IOException) { }
                if (!child.WaitForExit(5_000)) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
            }
            child?.Dispose();
            if (classRegisteredA || namespaceRegisteredA) DeleteNamespace(classIdA);
            if (classRegisteredB || namespaceRegisteredB) DeleteNamespace(classIdB);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            if (classIdA != Guid.Empty)
            {
                var classPath = ClassKeyPath(classIdA);
                var namespacePath = NamespaceKeyPath(classIdA);
                using var classKey = Registry.CurrentUser.OpenSubKey(classPath);
                using var namespaceKey = Registry.CurrentUser.OpenSubKey(namespacePath);
                if (classKey is not null || namespaceKey is not null)
                    throw new InvalidOperationException("The profile-owner spike left profile A Shell registration behind.");
            }
            if (classIdB != Guid.Empty &&
                (Registry.CurrentUser.OpenSubKey(ClassKeyPath(classIdB)) is not null ||
                 Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classIdB)) is not null))
                throw new InvalidOperationException("The profile-owner spike left profile B Shell registration behind.");
            if (oleInitialized) OleUninitialize();
        }
    }

    internal static int RunChild(string profileIni, string backingPath, string ready,
        string verify, string verified, string verifyAgain, string verifiedAgain, string stop)
    {
        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        var classId = new ShellNamespaceProfileStore(profileIni).LoadOrCreate()
            ?? throw new InvalidOperationException("Profile B provider identity is invalid.");
        AdapterModule? adapter = null;
        BrowserInstance? firstBrowser = null;
        try
        {
            ReportStage(ready, "profile-store-loaded");
            RegisterVolatileNamespace(classId, FindAdapterLibrary());
            ReportStage(ready, "namespace-registered");
            adapter = new AdapterModule(classId, backingPath);
            firstBrowser = BrowserInstance.Open(classId, adapter.Module,
                "profile-b.txt", "profile-a.txt", 29, stage => ReportStage(ready, stage));
            firstBrowser.Verify("profile-b.txt", "profile-a.txt", 29);
            ReportStage(ready, $"first-browser-open-rows-{firstBrowser.RowCount}");
            File.WriteAllText(ready, classId.ToString("D"));
            WaitForSignal(verify, TimeSpan.FromSeconds(20));
            firstBrowser.Verify("profile-b.txt", "profile-a.txt", 29);
            File.WriteAllText(verified, $"rows={firstBrowser.RowCount};profile-b-only=True;recursive-size=29;profile-a-closed=True");
            ReportStage(ready, "survived-profile-a-close");
            WaitForSignal(verifyAgain, TimeSpan.FromSeconds(20));
            ReportStage(ready, "profile-a-reopened-signal-received");
            firstBrowser.Verify("profile-b.txt", "profile-a.txt", 29);
            File.WriteAllText(verifiedAgain, "profile-b-view-still-valid-after-a-reopens");
            ReportStage(ready, "survived-profile-a-reopen");
            WaitForSignal(stop, TimeSpan.FromSeconds(20));
            ReportStage(ready, "stop-signal-received");
            Console.WriteLine($"shell-namespace-profile-owner-child: browser-survives-profile-a-close-and-reopen=True; rows={firstBrowser.RowCount}; volatile-keys-process-lifetime=True");
            Console.Out.Flush();
            // ExplorerBrowser teardown on this hidden desktop may block in OleUninitialize.
            // The worker owns only volatile registry keys and disposable fixture files, so
            // terminate this isolated process and let Windows release the entire Shell graph.
            Environment.Exit(0);
            return 0;
        }
        finally
        {
            // This probe does not establish normal Shell host teardown. The short-lived owner
            // process is the release boundary; the parent then removes and verifies its keys.
            _ = firstBrowser;
            _ = adapter;
            ReportStage(ready, "owner-process-exit-cleanup");
            OleUninitialize();
        }
    }

    private static void RegisterVolatileNamespace(Guid classId, string adapterPath)
    {
        var classPath = ClassKeyPath(classId);
        var namespacePath = NamespaceKeyPath(classId);
        if (Registry.CurrentUser.OpenSubKey(classPath) is not null || Registry.CurrentUser.OpenSubKey(namespacePath) is not null)
            throw new InvalidOperationException($"Refusing to overwrite an existing Shell registration for profile {classId:D}.");
        try
        {
            using (var clsid = Registry.CurrentUser.CreateSubKey(classPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile))
            {
                clsid.SetValue(null, "ExplorerDirs profile-owned Shell namespace probe", RegistryValueKind.String);
                using var inproc = clsid.CreateSubKey("InprocServer32", RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile);
                inproc.SetValue(null, adapterPath, RegistryValueKind.String);
                inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                using var shellFolder = clsid.CreateSubKey("ShellFolder", RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile);
                shellFolder.SetValue("Attributes", unchecked((int)0xA0000000) | 0x00100000, RegistryValueKind.DWord);
            }
            using var nameSpace = Registry.CurrentUser.CreateSubKey(namespacePath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile);
            nameSpace.SetValue(null, "ExplorerDirs profile-owned Shell namespace probe", RegistryValueKind.String);
        }
        catch
        {
            DeleteNamespace(classId);
            throw;
        }
    }

    private static void DeleteNamespace(Guid classId)
    {
        Registry.CurrentUser.DeleteSubKeyTree(NamespaceKeyPath(classId), throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ClassKeyPath(classId), throwOnMissingSubKey: false);
    }

    private static string ClassKeyPath(Guid classId) => $@"Software\Classes\CLSID\{{{classId:D}}}";
    private static string NamespaceKeyPath(Guid classId) => $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";

    private static void WaitForFile(string path, Process child, string message, string? stagePathOverride = null)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!File.Exists(path) && Environment.TickCount64 < deadline)
        {
            if (child.HasExited) throw new InvalidOperationException($"{message} Child exit={child.ExitCode}; stderr={child.StandardError.ReadToEnd()}");
            Thread.Sleep(10);
        }
        if (!File.Exists(path))
        {
            var stagePath = stagePathOverride ?? path + ".stage";
            var stage = File.Exists(stagePath) ? File.ReadAllText(stagePath) : "<no stage recorded>";
            throw new TimeoutException($"{message} Last child stage: {stage}");
        }
    }

    private static void WaitForSignal(string path, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!File.Exists(path) && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        if (!File.Exists(path)) throw new TimeoutException($"Timed out waiting for owner signal '{Path.GetFileName(path)}'.");
    }

    private static void ReportStage(string readyPath, string stage) =>
        File.AppendAllText(readyPath + ".stage", stage + Environment.NewLine);

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before this probe.");
    }

    private sealed class AdapterModule : IDisposable
    {
        private readonly nint _module;
        internal nint Module => _module;
        public AdapterModule(Guid classId, string backingPath)
        {
            _module = NativeLibrary.Load(FindAdapterLibrary());
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetClassId>(NativeLibrary.GetExport(_module, "SetSyntheticFolderClassIdForProbe"))(ref classId));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetPath>(NativeLibrary.GetExport(_module, "SetSyntheticSortBackingPath"))(backingPath));
        }
        public void Dispose() => NativeLibrary.Free(_module);
    }

    private sealed class BrowserInstance : IDisposable
    {
        private readonly nint _browser;
        private readonly nint _shellView;
        private readonly nint _folderView;
        private readonly nint _viewFolder;
        private readonly nint _module;
        private int _disposed;

        private BrowserInstance(nint browser, nint shellView, nint folderView, nint viewFolder, nint module, int rowCount)
        {
            _browser = browser;
            _shellView = shellView;
            _folderView = folderView;
            _viewFolder = viewFolder;
            _module = module;
            RowCount = rowCount;
        }

        internal int RowCount { get; }

        internal static BrowserInstance Open(Guid classId, nint module,
            string expectedName, string forbiddenName, ulong recursiveBytes, Action<string>? reportStage = null)
        {
            nint browser = 0, parent = 0, pidl = 0, shellView = 0, folderView = 0, viewFolder = 0;
            uint initialized = 0;
            try
            {
                var browserClass = BrowserClassId;
                var browserInterface = BrowserInterfaceId;
                reportStage?.Invoke("browser-cocreate-enter");
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserInterface, out browser));
                reportStage?.Invoke("browser-cocreate-done");
                parent = CreateWindowExW(0, "STATIC", "ExplorerDirs isolated profile Browser probe",
                    0x90000000, 40, 40, 820, 620, 0, 0, 0, 0);
                if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var rect = new ProbeRect { Right = 820, Bottom = 620 };
                var settings = new ProbeFolderSettings { Mode = 4 };
                Marshal.ThrowExceptionForHR(At<BrowserInitialize>(browser, 3)(browser, parent, ref rect, ref settings));
                initialized = 1;
                reportStage?.Invoke("browser-initialize-done");
                // Each profile CLSID is initialized with its own backing root, so browse the
                // profile namespace root and verify that its view exposes only that root's rows.
                var parsingName = $"::{{{classId:D}}}";
                reportStage?.Invoke("browser-parse-enter");
                Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, 0, out pidl, 0, out _));
                reportStage?.Invoke("browser-parse-done");
                var browseHr = At<BrowserBrowseToIdList>(browser, 13)(browser, pidl, 0);
                if (browseHr < 0) Marshal.ThrowExceptionForHR(browseHr);
                reportStage?.Invoke("browser-browse-done");
                Marshal.FreeCoTaskMem(pidl);
                pidl = 0;
                var shellViewId = ShellViewId;
                Marshal.ThrowExceptionForHR(At<BrowserGetCurrentView>(browser, 17)(browser, ref shellViewId, out shellView));
                reportStage?.Invoke("browser-current-view-done");
                var folderView2Id = FolderView2Id;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in folderView2Id, out folderView));
                reportStage?.Invoke("browser-folder-view2-done");
                var expectedRows = 2;
                var deadline = Environment.TickCount64 + 5000;
                var count = 0;
                while (Environment.TickCount64 < deadline)
                {
                    Application.DoEvents();
                    Marshal.ThrowExceptionForHR(At<ViewItemCount>(folderView, 7)(folderView, 2, out count));
                    if (count == expectedRows) break;
                    Thread.Sleep(10);
                }
                reportStage?.Invoke($"browser-row-count-{count}");
                if (count != expectedRows)
                    throw new TimeoutException($"Profile {classId:D} ExplorerBrowser exposed {count} rows; expected {expectedRows}.");
                var shellFolderId = ShellFolderId;
                Marshal.ThrowExceptionForHR(At<ViewGetFolder>(folderView, 5)(folderView, ref shellFolderId, out viewFolder));
                var verify = Marshal.GetDelegateForFunctionPointer<VerifyFolder>(NativeLibrary.GetExport(module, "VerifySyntheticFolderContentsForProbe"));
                Marshal.ThrowExceptionForHR(verify(viewFolder, expectedName, forbiddenName, recursiveBytes));
                return new BrowserInstance(browser, shellView, folderView, viewFolder, module, count);
            }
            catch
            {
                if (viewFolder != 0) Marshal.Release(viewFolder);
                if (folderView != 0) Marshal.Release(folderView);
                if (shellView != 0) Marshal.Release(shellView);
                if (initialized != 0 && browser != 0) _ = At<BrowserDestroy>(browser, 4)(browser);
                if (browser != 0) Marshal.Release(browser);
                if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
                // This parent is isolated on PrivateDesktopShellProbe's private desktop and
                // remains owned by the short-lived probe process until it exits.
                throw;
            }
        }

        internal void Verify(string expectedName, string forbiddenName, ulong recursiveBytes)
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(BrowserInstance));
            Marshal.ThrowExceptionForHR(At<ViewItemCount>(_folderView, 7)(_folderView, 2, out var count));
            if (count != RowCount) throw new InvalidOperationException("The profile ExplorerBrowser row count changed unexpectedly.");
            var verify = Marshal.GetDelegateForFunctionPointer<VerifyFolder>(NativeLibrary.GetExport(_module, "VerifySyntheticFolderContentsForProbe"));
            Marshal.ThrowExceptionForHR(verify(_viewFolder, expectedName, forbiddenName, recursiveBytes));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Marshal.Release(_viewFolder);
            Marshal.Release(_folderView);
            Marshal.Release(_shellView);
            _ = At<BrowserDestroy>(_browser, 4)(_browser);
            Marshal.Release(_browser);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ProbeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ProbeFolderSettings { public uint Mode, Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowserInitialize(nint self, nint parent, ref ProbeRect rect, ref ProbeFolderSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowserBrowseToIdList(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowserGetCurrentView(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int BrowserDestroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ViewGetFolder(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ViewItemCount(nint self, uint scope, out int count);
    private static T At<T>(nint instance, int index) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, index * IntPtr.Size));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int VerifyFolder(nint folder, [MarshalAs(UnmanagedType.LPWStr)] string expectedName, [MarshalAs(UnmanagedType.LPWStr)] string forbiddenName, ulong recursiveBytes);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
}
