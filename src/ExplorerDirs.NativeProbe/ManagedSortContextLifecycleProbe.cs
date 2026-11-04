using System.Runtime.InteropServices;
using ExplorerDirs.App;

public static partial class SyntheticShellSortProbe
{
    internal static int RunManagedSortContextLifecycle(string adapterPath, string legacyAdapterPath, string? productAdapterPath = null)
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
            if (!first!.Contains(firstLocation) || first.Contains(secondLocation) || first.Contains(root))
                throw new InvalidOperationException("Context location ownership check failed.");
            Check(verify(secondLocation, 3));
            first!.Update(true, 1);
            Check(verify(firstLocation, 1));
            Check(verify(secondLocation, 3));
            prepared = first.Prepare(nextRoot);
            Check(verify(prepared, 1));
            if (!first.Contains(prepared)) throw new InvalidOperationException("Prepared next location lost context ownership.");
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
            if (productAdapterPath is not null)
            {
                var productModule = NativeLibrary.Load(Path.GetFullPath(productAdapterPath));
                FolderSizeSortContext? product = null;
                nint productLocation = 0, nextProductLocation = 0;
                try
                {
                    foreach (var name in new[] { "CreateSizeSortPolicyPidlForProbe", "CreateDynamicSizeSortPidlForProbe", "SetDynamicSizeSortPolicyForProbe",
                        "ReleaseDynamicSizeSortLeaseForProbe", "SetSizeSortShellPartitionForProbe", "CreateSizeSortContextRootForProbe",
                        "VerifySizeSortContextValueForProbe", "GetSizeSortColumnClickCountForProbe", "VerifySizeSortPolicyPidlForProbe" })
                        if (NativeLibrary.TryGetExport(productModule, name, out _))
                            throw new InvalidOperationException($"Product adapter exposed a probe-only entry point: {name}.");
                    if (!FolderSizeSortContext.TryCreate(productAdapterPath, root, false, 1, out product, out productLocation))
                        throw new InvalidOperationException("Product adapter did not enable per-tab sort contexts.");
                    if (!product!.Contains(productLocation) || product.Contains(root))
                        throw new InvalidOperationException("Product context did not preserve its location ownership.");
                    product.Update(true, -1);
                    nextProductLocation = product.Prepare(nextRoot);
                    if (!product.Contains(nextProductLocation)) throw new InvalidOperationException("Product context lost cross-directory ownership.");
                    Console.WriteLine("Product sort context ABI: PASS (enabled, probe exports absent, update, navigation identity, release; no window)");
                }
                finally
                {
                    product?.Dispose();
                    if (productLocation != 0) Marshal.FreeCoTaskMem(productLocation);
                    if (nextProductLocation != 0) Marshal.FreeCoTaskMem(nextProductLocation);
                    NativeLibrary.Free(productModule);
                }
            }
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
