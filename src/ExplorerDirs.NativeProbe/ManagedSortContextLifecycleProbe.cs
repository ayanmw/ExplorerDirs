using System.Runtime.InteropServices;
using ExplorerDirs.App;

public static partial class SyntheticShellSortProbe
{
    internal static int RunManagedSortContextLifecycle(string adapterPath, string legacyAdapterPath)
    {
        adapterPath = Path.GetFullPath(adapterPath);
        var module = NativeLibrary.Load(adapterPath);
        nint root = 0, nextRoot = 0, firstLocation = 0, secondLocation = 0, prepared = 0;
        FolderSizeSortContext? first = null, second = null;
        try
        {
            var createRoot = Marshal.GetDelegateForFunctionPointer<CreateSortLifecycleRoot>(
                NativeLibrary.GetExport(module, "CreateSizeSortContextRootForProbe"));
            var verify = Marshal.GetDelegateForFunctionPointer<VerifySortLifecycleValue>(
                NativeLibrary.GetExport(module, "VerifySizeSortContextValueForProbe"));
            Check(createRoot(@"C:\context-fixture-not-accessed", out root));
            Check(createRoot(@"D:\next-context-fixture-not-accessed", out nextRoot));
            if (FolderSizeSortContext.TryCreate(Path.GetFullPath(legacyAdapterPath), root, false, 1,
                    out var unsupported, out var unsupportedLocation) || unsupported is not null || unsupportedLocation != 0)
                throw new InvalidOperationException("Legacy adapter capability fallback failed.");
            if (!FolderSizeSortContext.TryCreate(adapterPath, root, false, 1, out first, out firstLocation) ||
                !FolderSizeSortContext.TryCreate(adapterPath, root, true, -1, out second, out secondLocation))
                throw new InvalidOperationException("Expected context capability was unavailable.");
            Check(verify(firstLocation, 0));
            Check(verify(secondLocation, 3));
            first!.Update(true, 1);
            Check(verify(firstLocation, 1));
            Check(verify(secondLocation, 3));
            prepared = first.Prepare(nextRoot);
            Check(verify(prepared, 1));
            first.Update(false, -1);
            Check(verify(firstLocation, 2));
            Check(verify(prepared, 2));
            Check(verify(secondLocation, 3));
            try { first.Update(false, 0); throw new InvalidOperationException("Invalid direction accepted."); }
            catch (ArgumentOutOfRangeException) { }
            Check(verify(firstLocation, 2));
            first.Dispose();
            first.Dispose();
            try { first.Prepare(root); throw new InvalidOperationException("Disposed context prepared a location."); }
            catch (ObjectDisposedException) { }
            try { first.Update(false, 1); throw new InvalidOperationException("Disposed context accepted a policy."); }
            catch (ObjectDisposedException) { }
            if (verify(firstLocation, 2) >= 0) throw new InvalidOperationException("Retired context remained registered.");
            second!.Update(false, 1);
            Check(verify(secondLocation, 0));
            Console.WriteLine("Managed sort context lifecycle: PASS (same-path isolation, cross-directory reuse, existing identities, invalid direction, legacy capabilities, idempotent disposal, post-disposal rejection, sibling survives; no fixture IO/registry/window)");
            return 0;
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
            foreach (var item in new[] { root, nextRoot, firstLocation, secondLocation, prepared })
                if (item != 0) Marshal.FreeCoTaskMem(item);
            NativeLibrary.Free(module);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateSortLifecycleRoot([MarshalAs(UnmanagedType.LPWStr)] string path, out nint root);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VerifySortLifecycleValue(nint root, uint expected);
}
