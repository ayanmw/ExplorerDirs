using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

internal static class RecursiveSizeCacheLifecycleProbe
{
    internal static int Run()
    {
        var libraryPath = FindAdapterLibrary();
        var module = NativeLibrary.Load(libraryPath);
        try
        {
            var smoke = Marshal.GetDelegateForFunctionPointer<RunCacheLifecycleSmoke>(
                NativeLibrary.GetExport(module, "RunSyntheticRecursiveSizeCacheUnloadLifecycleSmoke"));
            Marshal.ThrowExceptionForHR(smoke());
            Console.WriteLine("recursive-size-cache-lifecycle: shared-snapshot-released-on-idle-unload=True; next-store-starts-empty=True");
            return 0;
        }
        finally { NativeLibrary.Free(module); }
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RunCacheLifecycleSmoke();
}
