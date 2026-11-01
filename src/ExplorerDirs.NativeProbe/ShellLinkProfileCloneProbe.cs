using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerDirs.App;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

// Measures whether a Shell Link PIDL created under one profile CLSID can bind after only the cloned CLSID remains.
internal static class ShellLinkProfileCloneProbe
{
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ShellLinkClone-" + Guid.NewGuid().ToString("N"));
        var linkPath = Path.Combine(fixture, "source-profile.lnk");
        var classA = Guid.NewGuid();
        var classB = Guid.NewGuid();
        var classAPath = ClassPath(classA);
        var namespaceAPath = NamespacePath(classA);
        var classBPath = ClassPath(classB);
        var namespaceBPath = NamespacePath(classB);
        nint module = 0, pidlA = 0, pidlB = 0;
        var classARegistered = false;
        var namespaceARegistered = false;
        var classBRegistered = false;
        var namespaceBRegistered = false;
        var oleInitialized = false;
        try
        {
            Directory.CreateDirectory(fixture);
            File.WriteAllBytes(Path.Combine(fixture, "payload.bin"), new byte[29]);
            Check(OleInitialize(0));
            oleInitialized = true;
            var adapterPath = FindAdapterLibrary();
            module = NativeLibrary.Load(adapterPath);
            var configure = GetExport<ConfigureRoot>(module, "ConfigureSyntheticNamespaceRootForProbe");
            Check(configure(ref classA, fixture));
            Check(configure(ref classB, fixture));
            RegisterClass(classAPath, adapterPath); classARegistered = true;
            RegisterNamespace(namespaceAPath); namespaceARegistered = true;
            RegisterClass(classBPath, adapterPath); classBRegistered = true;
            RegisterNamespace(namespaceBPath); namespaceBRegistered = true;

            Check(SHParseDisplayName(ParsingName(classA, fixture), 0, out pidlA, 0, out _));
            Check(SHParseDisplayName(ParsingName(classB, fixture), 0, out pidlB, 0, out _));
            var pidlsEqual = PidlBytesEqual(pidlA, pidlB);
            SaveLink(linkPath, pidlA);
            var sourceLinkMatches = ReadLinkPidl(linkPath, out var savedBeforeSwitch) &&
                PidlBytesEqual(pidlA, savedBeforeSwitch);
            if (savedBeforeSwitch != 0) Marshal.FreeCoTaskMem(savedBeforeSwitch);

            Marshal.FreeCoTaskMem(pidlA); pidlA = 0;
            Marshal.FreeCoTaskMem(pidlB); pidlB = 0;
            Registry.CurrentUser.DeleteSubKeyTree(namespaceAPath, throwOnMissingSubKey: false); namespaceARegistered = false;
            Registry.CurrentUser.DeleteSubKeyTree(classAPath, throwOnMissingSubKey: false); classARegistered = false;

            var child = RunChild(classB, fixture, linkPath);
            Console.WriteLine($"shell-link profile CLSID clone: source-link-pidl-roundtrip={sourceLinkMatches}; same-path-provider-pidls-equal={pidlsEqual}; cloned-provider-only-link-pidl-read={child.LinkPidlRead}; cloned-provider-only-folder-bind={child.FolderBound}; cloned-provider-only-link-content={child.ContentVerified}; link-parsing-name={child.ParsingName}; shell-link-path={child.ShellLinkPath}");
            if (!sourceLinkMatches)
                throw new InvalidOperationException("The controlled source profile link did not preserve its source PIDL.");
            return 0;
        }
        finally
        {
            if (pidlB != 0) Marshal.FreeCoTaskMem(pidlB);
            if (pidlA != 0) Marshal.FreeCoTaskMem(pidlA);
            if (namespaceARegistered) Registry.CurrentUser.DeleteSubKeyTree(namespaceAPath, throwOnMissingSubKey: false);
            if (classARegistered) Registry.CurrentUser.DeleteSubKeyTree(classAPath, throwOnMissingSubKey: false);
            if (namespaceBRegistered) Registry.CurrentUser.DeleteSubKeyTree(namespaceBPath, throwOnMissingSubKey: false);
            if (classBRegistered) Registry.CurrentUser.DeleteSubKeyTree(classBPath, throwOnMissingSubKey: false);
            using var remainingAClass = Registry.CurrentUser.OpenSubKey(classAPath);
            using var remainingANamespace = Registry.CurrentUser.OpenSubKey(namespaceAPath);
            using var remainingBClass = Registry.CurrentUser.OpenSubKey(classBPath);
            using var remainingBNamespace = Registry.CurrentUser.OpenSubKey(namespaceBPath);
            var registrationsRemoved = remainingAClass is null && remainingANamespace is null &&
                remainingBClass is null && remainingBNamespace is null;
            Console.WriteLine($"shell-link profile CLSID clone: temporary-registrations-removed={registrationsRemoved}");
            if (module != 0) NativeLibrary.Free(module);
            if (oleInitialized) OleUninitialize();
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
            if (!registrationsRemoved) throw new InvalidOperationException("Temporary volatile provider registrations remained after the Shell Link clone probe.");
        }
    }

    internal static int RunChild(string classIdText, string fixture, string linkPath)
    {
        var classId = Guid.Parse(classIdText);
        nint module = 0, linkPidl = 0, parsedPidl = 0, folder = 0;
        object? linkObject = null;
        var oleInitialized = false;
        var linkPidlRead = false;
        var folderBound = false;
        var contentVerified = false;
        var parsingName = "<unavailable>";
        var shellLinkPath = "<unavailable>";
        try
        {
            Check(OleInitialize(0));
            oleInitialized = true;
            module = NativeLibrary.Load(FindAdapterLibrary());
            Check(GetExport<ConfigureRoot>(module, "ConfigureSyntheticNamespaceRootForProbe")(ref classId, fixture));
            linkPidl = LoadLinkPidl(linkPath, out linkObject);
            var pathBuffer = new StringBuilder(32768);
            var pathResult = ((IShellLinkW)linkObject!).GetPath(pathBuffer, pathBuffer.Capacity, 0, 0);
            shellLinkPath = pathResult >= 0 ? pathBuffer.ToString() : $"HRESULT=0x{pathResult:X8}";
            linkPidlRead = linkPidl != 0;
            if (linkPidlRead && SHGetNameFromIDList(linkPidl, 0x80028000, out var name) >= 0 && name != 0)
            {
                try { parsingName = Marshal.PtrToStringUni(name) ?? "<empty>"; }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            if (SHParseDisplayName(ParsingName(classId, fixture), 0, out parsedPidl, 0, out _) >= 0 && parsedPidl != 0 &&
                SHBindToObject(0, parsedPidl, 0, in ShellFolderId, out folder) >= 0 && folder != 0)
                folderBound = true;
            if (linkPidlRead && SHBindToObject(0, linkPidl, 0, in ShellFolderId, out var linkedFolder) >= 0 && linkedFolder != 0)
            {
                try
                {
                    var verify = GetExport<VerifyFolder>(module, "VerifySyntheticFolderContentsForProbe");
                    contentVerified = verify(linkedFolder, "payload.bin", "missing.txt", 29) >= 0;
                }
                finally { Marshal.Release(linkedFolder); }
            }
            Console.WriteLine($"shell-link profile CLSID child: link-pidl-read={linkPidlRead}; folder-bound={folderBound}; link-content-verified={contentVerified}; link-parsing-name={parsingName}; shell-link-path={shellLinkPath}");
            return 0;
        }
        finally
        {
            if (folder != 0) Marshal.Release(folder);
            if (parsedPidl != 0) Marshal.FreeCoTaskMem(parsedPidl);
            if (linkPidl != 0) Marshal.FreeCoTaskMem(linkPidl);
            if (linkObject is not null && Marshal.IsComObject(linkObject)) Marshal.FinalReleaseComObject(linkObject);
            if (module != 0) NativeLibrary.Free(module);
            if (oleInitialized) OleUninitialize();
        }
    }

    private static (bool LinkPidlRead, bool FolderBound, bool ContentVerified, string ParsingName, string ShellLinkPath) RunChild(
        Guid classId, string fixture, string linkPath)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(ShellLinkProfileCloneProbe).Assembly.Location);
        start.ArgumentList.Add("--shell-link-profile-clone-child");
        start.ArgumentList.Add(classId.ToString("D"));
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add(linkPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Shell Link clone child.");
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("The Shell Link clone child did not exit within 20 seconds.");
        }
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output)) Console.Write(output);
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.Write(error);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Shell Link clone child failed with exit code {process.ExitCode}.");
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(value => value.StartsWith("shell-link profile CLSID child:", StringComparison.Ordinal))
            ?? throw new InvalidDataException("The Shell Link clone child emitted no result line.");
        bool Value(string key) => line.Contains($"{key}=True", StringComparison.OrdinalIgnoreCase);
        var name = line.Split("link-parsing-name=", 2, StringSplitOptions.None).Last();
        var pathParts = name.Split("; shell-link-path=", 2, StringSplitOptions.None);
        return (Value("link-pidl-read"), Value("folder-bound"), Value("link-content-verified"), pathParts[0], pathParts[^1]);
    }

    private static bool ReadLinkPidl(string path, out nint pidl)
    {
        object? instance = null;
        try
        {
            pidl = LoadLinkPidl(path, out instance);
            return pidl != 0;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }

    private static nint LoadLinkPidl(string path, out object? instance)
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)!;
        instance = Activator.CreateInstance(type)!;
        var link = (IShellLinkW)instance;
        Marshal.ThrowExceptionForHR(((IPersistFile)link).Load(path, 0));
        Marshal.ThrowExceptionForHR(link.GetIDList(out var pidl));
        return pidl;
    }

    private static void SaveLink(string path, nint pidl)
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)!;
        var link = (IShellLinkW)Activator.CreateInstance(type)!;
        try
        {
            Marshal.ThrowExceptionForHR(link.SetIDList(pidl));
            Marshal.ThrowExceptionForHR(((IPersistFile)link).Save(path, true));
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    private static bool PidlBytesEqual(nint first, nint second)
    {
        if (first == 0 || second == 0) return false;
        var firstLength = ILGetSize(first); var secondLength = ILGetSize(second);
        if (firstLength == 0 || firstLength != secondLength) return false;
        var a = new byte[checked((int)firstLength)]; var b = new byte[checked((int)secondLength)];
        Marshal.Copy(first, a, 0, a.Length); Marshal.Copy(second, b, 0, b.Length);
        return a.AsSpan().SequenceEqual(b);
    }

    private static void RegisterClass(string path, string modulePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create the temporary volatile CLSID key.");
            key.SetValue(null, "Explorer Dirs isolated Shell Link clone probe", RegistryValueKind.String);
            using var inproc = key.CreateSubKey("InprocServer32", RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create temporary InprocServer32 key.");
            inproc.SetValue(null, modulePath, RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            using var shellFolder = key.CreateSubKey("ShellFolder", RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create temporary ShellFolder key.");
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
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile)
                ?? throw new InvalidOperationException("Could not create temporary Desktop NameSpace key.");
            key.SetValue(null, "Explorer Dirs isolated Shell Link clone probe", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static string ClassPath(Guid value) => $@"Software\Classes\CLSID\{{{value:D}}}";
    private static string NamespacePath(Guid value) => $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{value:D}}}";
    private static string ParsingName(Guid value, string path)
    {
        var token = new StringBuilder("edpath:", checked(7 + path.Length * 4));
        foreach (var character in path) token.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        return $"::{{{value:D}}}\\{token}";
    }

    private static string FindAdapterLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "bin", "native-x64", "ExplorerDirs.StaticShellFolderAdapter.dll");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Run build-static-shell-folder-adapter.ps1 before this probe.");
    }

    private static T GetExport<T>(nint module, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ConfigureRoot(ref Guid classId, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int VerifyFolder(nint folder, [MarshalAs(UnmanagedType.LPWStr)] string expectedName,
        [MarshalAs(UnmanagedType.LPWStr)] string forbiddenName, ulong bytes);
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, nint findData, uint flags);
        [PreserveSig] int GetIDList(out nint pidl);
        [PreserveSig] int SetIDList(nint pidl);
    }
    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    }

    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parent, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetNameFromIDList(nint pidl, uint sigdn, out nint name);
}
