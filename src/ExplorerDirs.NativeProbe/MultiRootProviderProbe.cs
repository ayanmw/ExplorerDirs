using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Exercises independent namespace root instances without creating an ExplorerBrowser or view window.
internal static class MultiRootProviderProbe
{
    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-MultiRootProvider-" + Guid.NewGuid().ToString("N"));
        var firstPath = Path.Combine(root, "first");
        var secondPath = Path.Combine(root, "second");
        var firstClassId = Guid.NewGuid();
        var secondClassId = Guid.NewGuid();
        var initialized = false;
        var module = nint.Zero;
        try
        {
            Directory.CreateDirectory(firstPath);
            Directory.CreateDirectory(secondPath);
            File.WriteAllText(Path.Combine(firstPath, "first-root-only.txt"), "first");
            File.WriteAllText(Path.Combine(secondPath, "second-root-only.txt"), "second");
            Directory.CreateDirectory(Path.Combine(firstPath, "shared-folder"));
            Directory.CreateDirectory(Path.Combine(secondPath, "shared-folder"));
            File.WriteAllBytes(Path.Combine(firstPath, "shared-folder", "payload.bin"), new byte[13]);
            File.WriteAllBytes(Path.Combine(secondPath, "shared-folder", "payload.bin"), new byte[29]);

            var initializeResult = OleInitialize(0);
            if (initializeResult < 0) Marshal.ThrowExceptionForHR(initializeResult);
            initialized = true;
            module = NativeLibrary.Load(FindAdapterLibrary());
            var configureRoot = Marshal.GetDelegateForFunctionPointer<ConfigureRoot>(
                NativeLibrary.GetExport(module, "ConfigureSyntheticNamespaceRootForProbe"));
            Marshal.ThrowExceptionForHR(configureRoot(ref firstClassId, firstPath));
            Marshal.ThrowExceptionForHR(configureRoot(ref secondClassId, secondPath));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                NativeLibrary.GetExport(module, "SetSyntheticCustomSizeColumnForProbe"))(1));
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<SetProbeFlag>(
                NativeLibrary.GetExport(module, "SetSyntheticRecursiveSizeScanOnDemandForProbe"))(1));

            var runSmoke = Marshal.GetDelegateForFunctionPointer<RunMultiRootSmoke>(
                NativeLibrary.GetExport(module, "RunSyntheticMultiRootProviderSmoke"));
            Marshal.ThrowExceptionForHR(runSmoke(
                ref firstClassId, "first-root-only.txt", "second-root-only.txt", 13,
                ref secondClassId, "second-root-only.txt", "first-root-only.txt", 29));
            var runSingleClassPidlSmoke = Marshal.GetDelegateForFunctionPointer<RunSingleClassPidlSmoke>(
                NativeLibrary.GetExport(module, "RunSyntheticSingleClassMultiRootPidlSmoke"));
            Marshal.ThrowExceptionForHR(runSingleClassPidlSmoke(
                firstPath, "first-root-only.txt", "second-root-only.txt", 13,
                secondPath, "second-root-only.txt", "first-root-only.txt", 29));
            Console.WriteLine("synthetic multi-root provider smoke: PASS (distinct CLSID roots and one CLSID with path-bearing PIDLs; recursive sizes 13/29 B; no Shell view window)");
            return 0;
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            if (initialized) OleUninitialize();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ConfigureRoot(ref Guid classId, [MarshalAs(UnmanagedType.LPWStr)] string backingPath);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RunMultiRootSmoke(
        ref Guid firstClassId,
        [MarshalAs(UnmanagedType.LPWStr)] string firstExpectedName,
        [MarshalAs(UnmanagedType.LPWStr)] string firstForbiddenName,
        ulong firstRecursiveBytes,
        ref Guid secondClassId,
        [MarshalAs(UnmanagedType.LPWStr)] string secondExpectedName,
        [MarshalAs(UnmanagedType.LPWStr)] string secondForbiddenName,
        ulong secondRecursiveBytes);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RunSingleClassPidlSmoke(
        [MarshalAs(UnmanagedType.LPWStr)] string firstPath,
        [MarshalAs(UnmanagedType.LPWStr)] string firstExpectedName,
        [MarshalAs(UnmanagedType.LPWStr)] string firstForbiddenName,
        ulong firstRecursiveBytes,
        [MarshalAs(UnmanagedType.LPWStr)] string secondPath,
        [MarshalAs(UnmanagedType.LPWStr)] string secondExpectedName,
        [MarshalAs(UnmanagedType.LPWStr)] string secondForbiddenName,
        ulong secondRecursiveBytes);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetProbeFlag(int enabled);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void OleUninitialize();
}
