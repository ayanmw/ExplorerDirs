using Microsoft.Win32;
using System.Runtime.InteropServices;

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

            Console.WriteLine("registered multi-root PIDL smoke: PASS (natural root activation, absolute parsing-name PIDL round-trip, simultaneous root/child bindings A=13 B=29, no Shell view window)");
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

    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint value);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint name);
}
