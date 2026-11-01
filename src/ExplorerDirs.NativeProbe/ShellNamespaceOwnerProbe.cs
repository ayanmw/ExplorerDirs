using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerDirs.App;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

// Isolated lifecycle spike: two portable profiles own independent Shell registrations.
internal static class ShellNamespaceOwnerProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NamespaceOwners-" + Guid.NewGuid().ToString("N"));
        var profileA = Path.Combine(fixture, "profile-a");
        var profileB = Path.Combine(fixture, "profile-b");
        var rootA = Path.Combine(fixture, "root-a");
        var rootB = Path.Combine(fixture, "root-b");
        var childId = Guid.NewGuid();
        var ready = Path.Combine(fixture, "child.ready");
        var verify = Path.Combine(fixture, "child.verify");
        var verified = Path.Combine(fixture, "child.verified");
        var stop = Path.Combine(fixture, "child.stop");
        Guid classIdA = Guid.Empty;
        var classRegisteredA = false;
        var namespaceRegisteredA = false;
        Process? child = null;
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
            var classIdB = new ShellNamespaceProfileStore(Path.Combine(profileB, "ShellNamespace.ini")).LoadOrCreate()
                ?? throw new InvalidOperationException("Profile B did not produce a valid persistent provider identity.");
            if (classIdA == classIdB) throw new InvalidOperationException("Different portable profiles received the same provider CLSID.");

            RegisterVolatileNamespace(classIdA, FindAdapterLibrary());
            classRegisteredA = namespaceRegisteredA = true;
            using var adapterA = new AdapterModule(classIdA, rootA);
            VerifyLocation(classIdA, rootA, adapterA.Module, "profile-a.txt", "profile-b.txt", 13);

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
            start.ArgumentList.Add(stop);
            child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated profile B registration owner.");
            WaitForFile(ready, child, "Profile B did not register and resolve its namespace.");
            if (File.ReadAllText(ready) != classIdB.ToString("D"))
                throw new InvalidOperationException("Profile B registered a CLSID different from its portable INI identity.");

            VerifyLocation(classIdA, rootA, adapterA.Module, "profile-a.txt", "profile-b.txt", 13);
            DeleteNamespace(classIdA);
            classRegisteredA = namespaceRegisteredA = false;
            File.WriteAllText(verify, "verify");
            WaitForFile(verified, child, "Profile B could not resolve its namespace after profile A closed.");
            if (File.ReadAllText(verified) != "profile-b.txt")
                throw new InvalidOperationException("Profile B resolved a location outside its own physical root.");

            var reopenedClassIdA = new ShellNamespaceProfileStore(Path.Combine(profileA, "ShellNamespace.ini")).LoadOrCreate();
            if (reopenedClassIdA != classIdA)
                throw new InvalidOperationException("Profile A changed its stable CLSID after restart.");
            RegisterVolatileNamespace(classIdA, FindAdapterLibrary());
            classRegisteredA = namespaceRegisteredA = true;
            VerifyLocation(classIdA, rootA, adapterA.Module, "profile-a.txt", "profile-b.txt", 13);

            File.WriteAllText(stop, "stop");
            if (!child.WaitForExit(15_000))
                throw new TimeoutException("Profile B owner did not stop and remove its namespace registration.");
            var childOutput = child.StandardOutput.ReadToEnd();
            var childError = child.StandardError.ReadToEnd();
            if (child.ExitCode != 0)
                throw new InvalidOperationException($"Profile B owner failed: exit={child.ExitCode}; stdout={childOutput}; stderr={childError}");
            VerifyLocation(classIdA, rootA, adapterA.Module, "profile-a.txt", "profile-b.txt", 13);

            Console.WriteLine($"shell-namespace-profile-owners: distinct-profile-clsids=True; simultaneous-profile-b-resolution=True; closing-A-preserved-B=True; reopening-A-reused-clsid=True; closing-B-preserved-A=True; volatile-registrations-cleaned=True; profile-a={classIdA:D}; profile-b={classIdB:D}");
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
            if (oleInitialized) OleUninitialize();
        }
    }

    internal static int RunChild(string profileIni, string backingPath, string ready, string verify, string verified, string stop)
    {
        var oleResult = OleInitialize(0);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        var classId = new ShellNamespaceProfileStore(profileIni).LoadOrCreate()
            ?? throw new InvalidOperationException("Profile B provider identity is invalid.");
        var registered = false;
        try
        {
            RegisterVolatileNamespace(classId, FindAdapterLibrary());
            registered = true;
            using var adapter = new AdapterModule(classId, backingPath);
            VerifyLocation(classId, backingPath, adapter.Module, "profile-b.txt", "profile-a.txt", 29);
            File.WriteAllText(ready, classId.ToString("D"));
            WaitForSignal(verify, TimeSpan.FromSeconds(20));
            VerifyLocation(classId, backingPath, adapter.Module, "profile-b.txt", "profile-a.txt", 29);
            var resolved = backingPath;
            File.WriteAllText(verified, Path.GetFileName(Path.Combine(resolved, "profile-b.txt")));
            WaitForSignal(stop, TimeSpan.FromSeconds(20));
            Console.WriteLine("shell-namespace-profile-owner-child: profile-b-reparsed-after-profile-a-removal=True");
            return 0;
        }
        finally
        {
            if (registered)
            {
                DeleteNamespace(classId);
                if (Registry.CurrentUser.OpenSubKey(ClassKeyPath(classId)) is not null ||
                    Registry.CurrentUser.OpenSubKey(NamespaceKeyPath(classId)) is not null)
                    throw new InvalidOperationException("Profile B could not remove its volatile Shell namespace.");
            }
            OleUninitialize();
        }
    }

    private static string VerifyLocation(Guid classId, string expectedPath, nint module,
        string expectedName, string forbiddenName, ulong recursiveBytes)
    {
        var parsingName = $"::{{{classId:D}}}\\{EncodePath(expectedPath)}";
        Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, 0, out var pidl, 0, out _));
        try
        {
            var shellFolder = ShellFolderId;
            Marshal.ThrowExceptionForHR(SHBindToObject(0, pidl, 0, in shellFolder, out var folder));
            if (folder == 0) throw new InvalidOperationException("The profile-specific Shell path did not bind to a folder.");
            try
            {
                var verify = Marshal.GetDelegateForFunctionPointer<VerifyFolder>(NativeLibrary.GetExport(module, "VerifySyntheticFolderContentsForProbe"));
                Marshal.ThrowExceptionForHR(verify(folder, expectedName, forbiddenName, recursiveBytes));
            }
            finally { Marshal.Release(folder); }
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, SigdnDesktopAbsoluteParsing, out var name));
            try
            {
                var actual = Marshal.PtrToStringUni(name) ?? "";
                if (!actual.Contains(classId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                    !actual.EndsWith(EncodePath(expectedPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Profile namespace resolved an unexpected parsing name: '{actual}'.");
                return actual;
            }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static string EncodePath(string path)
    {
        var result = new StringBuilder("edpath:", checked(7 + path.Length * 4));
        foreach (var character in path)
            result.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        return result.ToString();
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

    private static void WaitForFile(string path, Process child, string message)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!File.Exists(path) && Environment.TickCount64 < deadline)
        {
            if (child.HasExited) throw new InvalidOperationException($"{message} Child exit={child.ExitCode}; stderr={child.StandardError.ReadToEnd()}");
            Thread.Sleep(10);
        }
        if (!File.Exists(path)) throw new TimeoutException(message);
    }

    private static void WaitForSignal(string path, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!File.Exists(path) && Environment.TickCount64 < deadline) Thread.Sleep(10);
        if (!File.Exists(path)) throw new TimeoutException($"Timed out waiting for owner signal '{Path.GetFileName(path)}'.");
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int VerifyFolder(nint folder, [MarshalAs(UnmanagedType.LPWStr)] string expectedName, [MarshalAs(UnmanagedType.LPWStr)] string forbiddenName, ulong recursiveBytes);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint value);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
}
