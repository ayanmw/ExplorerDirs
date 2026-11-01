using System.Diagnostics;
using System.Runtime.InteropServices;
using ExplorerDirs.App;
using Microsoft.Win32;

namespace ExplorerDirs.NativeProbe;

// Verifies natural Shell activation and child binding without creating a view/window.
internal static class RegisteredMultiRootPidlProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-RegisteredRoot-" + Guid.NewGuid().ToString("N"));
        var firstPath = Path.Combine(fixture, "first");
        var secondPath = Path.Combine(fixture, "second");
        var classId = Guid.NewGuid();
        var classKeyPath = $@"Software\Classes\CLSID\{{{classId:D}}}";
        var namespaceKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{{classId:D}}}";
        nint module = 0, rootPidl = 0, rootFolder = 0, childPidl = 0, childFolder = 0;
        nint absoluteChildPidl = 0, reparsedAbsolutePidl = 0, parsingName = 0;
        var oleInitialized = false;
        var classRegistered = false;
        var namespaceRegistered = false;
        try
        {
            Directory.CreateDirectory(firstPath);
            Directory.CreateDirectory(secondPath);
            File.WriteAllText(Path.Combine(firstPath, "first-only.txt"), "first");
            Directory.CreateDirectory(Path.Combine(firstPath, "shared-folder"));
            File.WriteAllBytes(Path.Combine(firstPath, "shared-folder", "payload.bin"), new byte[13]);
            File.WriteAllText(Path.Combine(secondPath, "second-only.txt"), "second");
            Directory.CreateDirectory(Path.Combine(secondPath, "shared-folder"));
            File.WriteAllBytes(Path.Combine(secondPath, "shared-folder", "payload.bin"), new byte[29]);

            Check(OleInitialize(0));
            oleInitialized = true;
            module = NativeLibrary.Load(FindAdapterLibrary());
            Check(GetExport<SetClassId>(module, "SetSyntheticFolderClassIdForProbe")(ref classId));
            Check(GetExport<SetPath>(module, "SetSyntheticSortBackingPath")(firstPath));
            Check(GetExport<SetFlag>(module, "SetSyntheticCustomSizeColumnForProbe")(1));
            Check(GetExport<SetFlag>(module, "SetSyntheticRecursiveSizeScanOnDemandForProbe")(1));
            RegisterClass(classKeyPath, FindAdapterLibrary());
            classRegistered = true;
            RegisterNamespace(namespaceKeyPath);
            namespaceRegistered = true;

            Check(SHParseDisplayName($"::{{{classId:D}}}", 0, out rootPidl, 0, out _));
            Check(SHBindToObject(0, rootPidl, 0, in ShellFolderId, out rootFolder));
            if (rootFolder == 0) throw new InvalidOperationException("The registered namespace root did not bind to IShellFolder.");
            var verifyFolder = GetExport<VerifyFolder>(module, "VerifySyntheticFolderContentsForProbe");
            Check(verifyFolder(rootFolder, "first-only.txt", "second-only.txt", 13));

            var parse = GetVTableMethod<ParseDisplayName>(rootFolder, 3);
            Check(parse(rootFolder, 0, 0, secondPath, 0, out childPidl, 0));
            if (childPidl == 0) throw new InvalidOperationException("The activated root did not parse the second physical root path.");
            absoluteChildPidl = ILCombine(rootPidl, childPidl);
            if (absoluteChildPidl == 0) throw new InvalidOperationException("Could not combine namespace root and path PIDL.");
            Check(SHGetNameFromIDList(absoluteChildPidl, 0x80028000, out parsingName));
            if (parsingName == 0) throw new InvalidOperationException("Shell did not produce an absolute parsing name for the path PIDL.");
            Check(SHParseDisplayName(Marshal.PtrToStringUni(parsingName)!, 0, out reparsedAbsolutePidl, 0, out _));
            var expectedPidlSize = ILGetSize(absoluteChildPidl);
            var actualPidlSize = ILGetSize(reparsedAbsolutePidl);
            if (expectedPidlSize != actualPidlSize ||
                !PidlBytesEqual(absoluteChildPidl, reparsedAbsolutePidl, checked((int)expectedPidlSize)))
                throw new InvalidOperationException($"Shell absolute parsing-name round-trip changed the path PIDL: name='{Marshal.PtrToStringUni(parsingName)}'; expected={expectedPidlSize} bytes; actual={actualPidlSize} bytes.");
            Check(SHBindToObject(rootFolder, childPidl, 0, in ShellFolderId, out childFolder));
            Check(verifyFolder(childFolder, "second-only.txt", "first-only.txt", 29));

            var absoluteParsingName = Marshal.PtrToStringUni(parsingName)!;
            var fallbackTab = new TabSettings(firstPath, ViewState.Default());
            var appSession = new SessionState(4, 4,
                [[new TabSettings(absoluteParsingName, ViewState.Default())], [fallbackTab], [fallbackTab], [fallbackTab]],
                0, [0, 0, 0, 0]);
            var restoredSession = SessionStore.ParseComplete(SessionStore.Render(appSession))
                ?? throw new InvalidOperationException("The app session INI parser rejected the Shell path identity.");
            var restoredPath = restoredSession.Panes[0][0].Path;
            if (!string.Equals(restoredPath, absoluteParsingName, StringComparison.Ordinal))
                throw new InvalidOperationException("The app session INI round-trip changed the absolute Shell parsing name.");
            if (!ShellLocationIdentity.TryGetFolderPidl(restoredPath, out var restoredPidl))
            {
                var diagnostic = DescribeShellItemAttributes(absoluteChildPidl);
                throw new InvalidOperationException($"The app Shell identity helper could not resolve the restored custom folder; diagnostic={diagnostic}.");
            }
            try
            {
                if (!ShellLocationIdentity.TryGetPersistedIdentity(restoredPidl, out var persistedIdentity, out var isFilesystem) ||
                    isFilesystem || !string.Equals(persistedIdentity, absoluteParsingName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The app Shell identity helper did not preserve the custom parsing name as a virtual Shell folder.");
            }
            finally { Marshal.FreeCoTaskMem(restoredPidl); }
            var childExitCode = RunCrossProcessPathIdentityCheck(classId, absoluteParsingName, firstPath, secondPath);
            if (childExitCode != 0)
                throw new InvalidOperationException($"A fresh process could not resolve the saved namespace path identity; exit code={childExitCode}.");

            Console.WriteLine("registered multi-root PIDL smoke: PASS (natural root activation, absolute parsing-name PIDL round-trip, app SessionStore and ShellLocationIdentity round-trip, fresh-process path/content recovery, simultaneous root/child bindings A=13 B=29, no Shell view window)");
            return 0;
        }
        finally
        {
            if (childFolder != 0) Marshal.Release(childFolder);
            if (rootFolder != 0) Marshal.Release(rootFolder);
            if (parsingName != 0) Marshal.FreeCoTaskMem(parsingName);
            if (reparsedAbsolutePidl != 0) Marshal.FreeCoTaskMem(reparsedAbsolutePidl);
            if (absoluteChildPidl != 0) Marshal.FreeCoTaskMem(absoluteChildPidl);
            if (childPidl != 0) Marshal.FreeCoTaskMem(childPidl);
            if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
            if (namespaceRegistered) Registry.CurrentUser.DeleteSubKeyTree(namespaceKeyPath, throwOnMissingSubKey: false);
            if (classRegistered) Registry.CurrentUser.DeleteSubKeyTree(classKeyPath, throwOnMissingSubKey: false);
            if (module != 0) NativeLibrary.Free(module);
            if (oleInitialized) OleUninitialize();
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
        }
    }

    internal static int RunChild(string classIdText, string parsingName, string fallbackPath, string expectedPath)
    {
        nint module = 0, pidl = 0, folder = 0;
        var oleInitialized = false;
        try
        {
            var classId = Guid.Parse(classIdText);
            Check(OleInitialize(0));
            oleInitialized = true;
            module = NativeLibrary.Load(FindAdapterLibrary());
            Check(GetExport<SetClassId>(module, "SetSyntheticFolderClassIdForProbe")(ref classId));
            // A different fallback makes this process prove that the saved parsing name carries its own root identity.
            Check(GetExport<SetPath>(module, "SetSyntheticSortBackingPath")(fallbackPath));
            Check(GetExport<SetFlag>(module, "SetSyntheticCustomSizeColumnForProbe")(1));
            Check(GetExport<SetFlag>(module, "SetSyntheticRecursiveSizeScanOnDemandForProbe")(1));
            Check(SHParseDisplayName(parsingName, 0, out pidl, 0, out _));
            Check(SHBindToObject(0, pidl, 0, in ShellFolderId, out folder));
            Check(GetExport<VerifyFolder>(module, "VerifySyntheticFolderContentsForProbe")(
                folder, "second-only.txt", "first-only.txt", 29));
            Check(SHGetNameFromIDList(pidl, 0x80028000, out var actualParsingName));
            try
            {
                if (!string.Equals(Marshal.PtrToStringUni(actualParsingName), parsingName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The fresh process resolved a different absolute namespace parsing name.");
            }
            finally { Marshal.FreeCoTaskMem(actualParsingName); }
            Console.WriteLine($"registered path PIDL child: PASS (path='{expectedPath}'; pidl-and-content-restored=True)");
            return 0;
        }
        finally
        {
            if (folder != 0) Marshal.Release(folder);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (module != 0) NativeLibrary.Free(module);
            if (oleInitialized) OleUninitialize();
        }
    }

    private static int RunCrossProcessPathIdentityCheck(Guid classId, string parsingName, string fallbackPath, string expectedPath)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(RegisteredMultiRootPidlProbe).Assembly.Location);
        start.ArgumentList.Add("--registered-path-pidl-child");
        start.ArgumentList.Add(classId.ToString("D"));
        start.ArgumentList.Add(parsingName);
        start.ArgumentList.Add(fallbackPath);
        start.ArgumentList.Add(expectedPath);
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the fresh-process path identity probe.");
        if (!child.WaitForExit(15_000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new TimeoutException("The fresh-process path identity probe did not exit within 15 seconds.");
        }
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output)) Console.Write(output);
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.Write(error);
        return child.ExitCode;
    }

    private static void RegisterClass(string path, string modulePath)
    {
        using var classes = Registry.CurrentUser.OpenSubKey("Software\\Classes", writable: true)
            ?? throw new InvalidOperationException("HKCU\\Software\\Classes is unavailable.");
        using var existing = classes.OpenSubKey(path["Software\\Classes\\".Length..]);
        if (existing is not null) throw new InvalidOperationException("The generated CLSID unexpectedly already exists.");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
                ?? throw new InvalidOperationException("Could not register temporary CLSID.");
            key.SetValue(null, "ExplorerDirs isolated registered multi-root probe", RegistryValueKind.String);
            using var inproc = key.CreateSubKey("InprocServer32", writable: true)
                ?? throw new InvalidOperationException("Could not register InprocServer32.");
            inproc.SetValue(null, modulePath, RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            using var shellFolder = key.CreateSubKey("ShellFolder", writable: true)
                ?? throw new InvalidOperationException("Could not register ShellFolder attributes.");
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
        using var existing = Registry.CurrentUser.OpenSubKey(path);
        if (existing is not null) throw new InvalidOperationException("The generated namespace key unexpectedly already exists.");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
                ?? throw new InvalidOperationException("Could not register temporary Desktop namespace entry.");
            key.SetValue(null, "ExplorerDirs isolated registered multi-root probe", RegistryValueKind.String);
        }
        catch
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            throw;
        }
    }

    private static T GetExport<T>(nint module, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));

    private static string DescribeShellItemAttributes(nint pidl)
    {
        var shellItemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        var createResult = SHCreateItemFromIDList(pidl, in shellItemId, out var shellItem);
        if (createResult < 0 || shellItem == 0) return $"SHCreateItemFromIDList=0x{createResult:X8}";
        try
        {
            var getAttributes = Marshal.GetDelegateForFunctionPointer<GetAttributes>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(shellItem), 6 * IntPtr.Size));
            var folderResult = getAttributes(shellItem, 0x20000000, out var folderAttributes);
            var filesystemResult = getAttributes(shellItem, 0x40000000, out var filesystemAttributes);
            var combinedResult = getAttributes(shellItem, 0x20000000 | 0x40000000, out var combinedAttributes);
            return $"SHCreateItemFromIDList=0x{createResult:X8}; folder=0x{folderResult:X8}/0x{folderAttributes:X8}; filesystem=0x{filesystemResult:X8}/0x{filesystemAttributes:X8}; combined=0x{combinedResult:X8}/0x{combinedAttributes:X8}";
        }
        finally { Marshal.Release(shellItem); }
    }

    private static bool PidlBytesEqual(nint first, nint second, int length)
    {
        var firstBytes = new byte[length];
        var secondBytes = new byte[length];
        Marshal.Copy(first, firstBytes, 0, length);
        Marshal.Copy(second, secondBytes, 0, length);
        return firstBytes.AsSpan().SequenceEqual(secondBytes);
    }

    private static T GetVTableMethod<T>(nint instance, int index) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, index * IntPtr.Size));
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

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ParseDisplayName(nint self, nint owner, nint bindContext,
        [MarshalAs(UnmanagedType.LPWStr)] string name, nint eaten, out nint item, nint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetClassId(ref Guid classId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFlag(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int VerifyFolder(nint folder, [MarshalAs(UnmanagedType.LPWStr)] string expectedName,
        [MarshalAs(UnmanagedType.LPWStr)] string forbiddenName, ulong recursiveBytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAttributes(nint self, uint mask, out uint attributes);

    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint value);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateItemFromIDList(nint pidl, in Guid iid, out nint value);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
}
