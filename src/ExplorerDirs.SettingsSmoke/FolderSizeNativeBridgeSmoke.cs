using System.IO;
using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class FolderSizeNativeBridgeSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VerifyStore();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Query([MarshalAs(UnmanagedType.LPWStr)] string path,
        nint cancelled, out ulong bytes, out ulong files, out ulong folders, out ulong unreadable, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Invalidate([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InvalidatePath([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetDelay(int milliseconds);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Counter();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VerifyColumns([MarshalAs(UnmanagedType.LPWStr)] string path);

    internal static void Run(string library)
    {
        var module = NativeLibrary.Load(Path.GetFullPath(library));
        var root = Path.Combine(AppContext.BaseDirectory, "native-size-bridge-" + Guid.NewGuid().ToString("N"));
        var cancellationFlag = Marshal.AllocHGlobal(4); Marshal.WriteInt32(cancellationFlag, 0);
        Directory.CreateDirectory(Path.Combine(root, "small")); Directory.CreateDirectory(Path.Combine(root, "large"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "small", "data.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(root, "large", "data.bin"), new byte[9]);
            File.WriteAllBytes(Path.Combine(root, "earlier.bin"), new byte[1]);
            File.WriteAllBytes(Path.Combine(root, "later.bin"), new byte[1]);
            File.SetLastWriteTimeUtc(Path.Combine(root, "earlier.bin"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(Path.Combine(root, "later.bin"), new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var verifyColumns = Marshal.GetDelegateForFunctionPointer<VerifyColumns>(NativeLibrary.GetExport(module, "VerifyBackingColumnMetadataForProbe"));
            Marshal.ThrowExceptionForHR(verifyColumns(root));
            var verify = Marshal.GetDelegateForFunctionPointer<VerifyStore>(NativeLibrary.GetExport(module, "VerifyFolderSizeAvailabilityStoreForProbe"));
            Marshal.ThrowExceptionForHR(verify());
            var query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(module, "QueryConfiguredFolderSizeForProbe"));
            var invalidate = Marshal.GetDelegateForFunctionPointer<Invalidate>(NativeLibrary.GetExport(module, "InvalidateConfiguredFolderSizeForProbe"));
            var invalidatePath = Marshal.GetDelegateForFunctionPointer<InvalidatePath>(NativeLibrary.GetExport(module, "InvalidateFolderSizePath"));
            var calls = 0; var unknown = false; var overflow = false; var exactCalls = 0;
            var status = EverythingSizeStatus.Available;
            TaskCompletionSource<IndexedSizePage>? gate = null;
            CancellationToken batchToken = default;
            using var entered = new ManualResetEventSlim();
            Task<IndexedSizePage> FakeQuery(string parent, uint offset, CancellationToken token)
            {
                Interlocked.Increment(ref calls);
                if (gate is { } pending) { batchToken = token; entered.Set(); return pending.Task; }
                if (status != EverythingSizeStatus.Available) return Task.FromResult(new IndexedSizePage(status, []));
                if (overflow) return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available,
                    [new(Path.Combine(root, "large"), true, 29)], 2049));
                return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available,
                    [new(Path.Combine(root, "small"), true, unknown ? null : 17), new(Path.Combine(root, "large"), true, unknown ? null : 29)], 2));
            }
            Task<IndexedSizePage> FakeExact(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Interlocked.Increment(ref exactCalls);
                return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available,
                    [new(path, true, unknown ? null : 17)], 1));
            }
            using var bridge = new FolderSizeNativeBridge(module, FolderSizeSource.Everything, FakeQuery, FakeExact);
            (int Hr, ulong Bytes, uint Flags) Read(string leaf)
            {
                var hr = query(Path.Combine(root, leaf), cancellationFlag, out var bytes, out _, out _, out _, out var flags);
                return (hr, bytes, flags);
            }
            if (Read("small") != (0, 17UL, 1U) || Read("large") != (0, 29UL, 1U) || calls != 1)
                throw new Exception("Native queries did not share their Everything parent batch or lost count availability.");
            invalidate(Path.Combine(root, "small", "data.bin"));
            if (Read("small").Bytes != 17 || calls != 2) throw new Exception("Descendant changes did not invalidate the shared parent query.");
            Marshal.ThrowExceptionForHR(invalidatePath(root));
            if (Read("small").Bytes != 17 || calls != 3) throw new Exception("Explicit refresh did not invalidate the parent query.");
            overflow = true; bridge.SetSource(FolderSizeSource.Everything);
            var beforeOverflow = calls;
            if (Read("small") != (0, 17UL, 1U) || Read("large") != (0, 29UL, 1U) ||
                exactCalls != 1 || calls != beforeOverflow + 1)
                throw new Exception("Beyond-first-page lookup lost exact size or repeated the shared parent query.");
            unknown = true; bridge.SetSource(FolderSizeSource.Everything);
            if (Read("small") != (0, 0UL, 0U) || exactCalls != 2)
                throw new Exception("Unknown exact lookup became an available zero or silently scanned.");
            overflow = false; unknown = false;
            foreach (var unavailable in new[] { EverythingSizeStatus.NotRunning, EverythingSizeStatus.IndexUnavailable,
                EverythingSizeStatus.Timeout, EverythingSizeStatus.InvalidReply, EverythingSizeStatus.Failed })
            {
                status = unavailable; bridge.SetSource(FolderSizeSource.Everything);
                if (Read("small") != (0, 0UL, 0U)) throw new Exception($"Everything-only scanned on {status}.");
                bridge.SetSource(FolderSizeSource.Auto);
                if (Read("small") != (0, 5UL, 3U)) throw new Exception($"Auto fallback failed on {status}.");
            }
            status = EverythingSizeStatus.Available;
            unknown = true; bridge.SetSource(FolderSizeSource.Everything);
            if (Read("small") != (0, 0UL, 0U)) throw new Exception("Unknown Everything size became an available zero.");
            bridge.SetSource(FolderSizeSource.Auto);
            if (Read("small") != (0, 5UL, 3U)) throw new Exception("Automatic fallback did not use native scanning.");
            bridge.SetSource(FolderSizeSource.BuiltIn); var previousCalls = calls;
            if (Read("large") != (0, 9UL, 3U) || calls != previousCalls)
                throw new Exception("Built-in mode contacted Everything or produced wrong metrics.");
            bridge.SetSource(FolderSizeSource.BuiltIn, enabled: false);
            if (Read("small").Hr != unchecked((int)0x800704C7) || calls != previousCalls)
                throw new Exception("Disabled folder sizes still issued queries or scans.");
            bridge.SetSource(FolderSizeSource.BuiltIn, enabled: true);
            if (Read("small") != (0, 5UL, 3U)) throw new Exception("Re-enabling folder sizes did not resume scanning.");
            var delay = Marshal.GetDelegateForFunctionPointer<SetDelay>(NativeLibrary.GetExport(module, "SetSyntheticAsyncRecursiveSizeEntryDelayForProbe"));
            var entriesVisited = Marshal.GetDelegateForFunctionPointer<Counter>(NativeLibrary.GetExport(module, "GetSyntheticDirectoryMetricsEntryCount"));
            Directory.CreateDirectory(Path.Combine(root, "slow"));
            for (var entryIndex = 0; entryIndex < 80; entryIndex++)
                File.WriteAllBytes(Path.Combine(root, "slow", $"entry-{entryIndex}.bin"), new byte[1]);
            Marshal.ThrowExceptionForHR(delay(5));
            var baseline = entriesVisited();
            var scanning = Task.Run(() => Read("slow"));
            try
            {
                var start = Environment.TickCount64;
                while (entriesVisited() < baseline + 3 && Environment.TickCount64 - start < 2000) Thread.Sleep(2);
                if (entriesVisited() < baseline + 3) throw new Exception("Native scan did not start.");
                bridge.SetSource(FolderSizeSource.BuiltIn, enabled: false);
                bridge.SetSource(FolderSizeSource.BuiltIn, enabled: true);
                if (!scanning.Wait(TimeSpan.FromSeconds(2)) || scanning.Result.Hr != unchecked((int)0x800704C7))
                    throw new Exception("Quick disable/re-enable revived an old native scan.");
            }
            finally
            {
                Marshal.WriteInt32(cancellationFlag, 1);
                scanning.GetAwaiter().GetResult();
                Marshal.ThrowExceptionForHR(delay(0)); Marshal.WriteInt32(cancellationFlag, 0);
            }
            Marshal.WriteInt32(cancellationFlag, 1);
            if (Read("small").Hr != unchecked((int)0x800704C7)) throw new Exception("Native cancellation was ignored.");
            Marshal.WriteInt32(cancellationFlag, 0);
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.SetSource(FolderSizeSource.Everything);
            var waiting = Task.Run(() => Read("small"));
            try
            {
                if (!entered.Wait(TimeSpan.FromSeconds(2))) throw new Exception("Callback did not enter wait.");
                // Retiring one row must not cancel the shared batch needed by another row.
                var sharedCalls = calls;
                Marshal.WriteInt32(cancellationFlag, 1);
                if (!waiting.Wait(TimeSpan.FromSeconds(2)) || waiting.Result.Hr != unchecked((int)0x800704C7) ||
                    !batchToken.CanBeCanceled || batchToken.IsCancellationRequested)
                    throw new Exception("Individual row cancellation retired the shared Everything batch.");
                Marshal.WriteInt32(cancellationFlag, 0);
                gate.TrySetResult(new(EverythingSizeStatus.Available,
                    [new(Path.Combine(root, "large"), true, 29)], 1));
                if (Read("large") != (0, 29UL, 1U) || calls != sharedCalls)
                    throw new Exception("Sibling lookup did not reuse its shared batch.");
                gate = new(TaskCreationOptions.RunContinuationsAsynchronously); entered.Reset();
                bridge.SetSource(FolderSizeSource.Everything);
                waiting = Task.Run(() => Read("small"));
                if (!entered.Wait(TimeSpan.FromSeconds(2))) throw new Exception("Replacement callback did not enter wait.");
                bridge.SetSource(FolderSizeSource.BuiltIn);
                if (!waiting.Wait(TimeSpan.FromSeconds(2)) || waiting.Result.Hr != unchecked((int)0x800704C7))
                    throw new Exception("Source switch did not retire the pending callback.");
                if (!batchToken.CanBeCanceled || !batchToken.IsCancellationRequested)
                    throw new Exception("Source switch left the shared Everything IPC batch running.");
            }
            finally { gate.TrySetResult(new(EverythingSizeStatus.IndexUnavailable, [])); waiting.GetAwaiter().GetResult(); }
            bridge.Dispose();
            if (Read("small") != (0, 5UL, 3U)) throw new Exception("Bridge disposal left a registered callback.");
            Console.WriteLine("Native folder size bridge smoke: PASS (backing date columns/values/sort; batch sharing, invalidation/F5, unknown/zero, native fallback, disable/re-enable, cancellation, source retirement, unregister and cache flags; no Shell view/IPC/registry)");
        }
        finally
        {
            Marshal.FreeHGlobal(cancellationFlag);
            NativeLibrary.Free(module);
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))
                Directory.Delete(root, true);
        }
    }
}
