using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Binary-only contract for the prototype's private child-PIDL identity envelope.
// It intentionally creates no Shell view or window.
internal static class WrappedPidlContractSmoke
{
    internal static int Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        nint source = 0, wrapped = 0, restored = 0, corrupted = 0, library = 0;
        try
        {
            Directory.CreateDirectory(path);
            var itemPath = Path.Combine(path, "pidl-roundtrip.txt");
            File.WriteAllText(itemPath, "contract fixture");
            Check(SHParseDisplayName(itemPath, 0, out source, 0, out _));
            library = NativeLibrary.Load(FindAdapterLibrary());
            var wrap = Marshal.GetDelegateForFunctionPointer<TransformPidl>(
                NativeLibrary.GetExport(library, "WrapStaticShellChildPidl"));
            var unwrap = Marshal.GetDelegateForFunctionPointer<TransformPidl>(
                NativeLibrary.GetExport(library, "UnwrapStaticShellChildPidl"));

            Check(wrap(source, out wrapped));
            Check(unwrap(wrapped, out restored));
            if (!ILIsEqual(source, restored)) throw new InvalidOperationException("Wrapped PIDL did not round-trip to the same Shell identity.");

            var wrappedSize = ILGetSize(wrapped);
            corrupted = Marshal.AllocCoTaskMem(checked((int)wrappedSize));
            var bytes = new byte[wrappedSize];
            Marshal.Copy(wrapped, bytes, 0, bytes.Length);
            bytes[2] ^= 0x5A; // Corrupt the private envelope marker; reject instead of forwarding it as a filesystem PIDL.
            Marshal.Copy(bytes, 0, corrupted, bytes.Length);
            var invalidHr = unwrap(corrupted, out var invalidOutput);
            if (invalidOutput != 0) ILFree(invalidOutput);
            if (invalidHr >= 0) throw new InvalidOperationException("Malformed wrapper marker was accepted.");

            Console.WriteLine($"wrapped child PIDL contract: round-trip=PASS; malformed-marker=REJECTED; bytes={wrappedSize}; window-created=False");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("wrapped child PIDL contract failure: " + ex);
            return 1;
        }
        finally
        {
            if (corrupted != 0) Marshal.FreeCoTaskMem(corrupted);
            if (restored != 0) ILFree(restored);
            if (wrapped != 0) ILFree(wrapped);
            if (source != 0) ILFree(source);
            if (library != 0) NativeLibrary.Free(library);
            try { Directory.Delete(path, recursive: true); } catch { }
        }
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

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TransformPidl(nint source, out nint result);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint left, nint right);
    [DllImport("shell32.dll")] private static extern void ILFree(nint pidl);
}
