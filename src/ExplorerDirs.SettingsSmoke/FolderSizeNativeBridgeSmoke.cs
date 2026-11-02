using System.IO;
using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class FolderSizeNativeBridgeSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int VerifyStore();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Query([MarshalAs(UnmanagedType.LPWStr)] string path,
        nint cancelled, out ulong bytes, out ulong files, out ulong folders, out ulong unreadable, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Invalidate([MarshalAs(UnmanagedType.LPWStr)] string path);

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
            var verify = Marshal.GetDelegateForFunctionPointer<VerifyStore>(NativeLibrary.GetExport(module, "VerifyFolderSizeAvailabilityStoreForProbe"));
            Marshal.ThrowExceptionForHR(verify());
            var query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(module, "QueryConfiguredFolderSizeForProbe"));
            var invalidate = Marshal.GetDelegateForFunctionPointer<Invalidate>(NativeLibrary.GetExport(module, "InvalidateConfiguredFolderSizeForProbe"));
            var calls = 0; var unknown = false;
            TaskCompletionSource<IndexedSizePage>? gate = null;
            using var entered = new ManualResetEventSlim();
            Task<IndexedSizePage> FakeQuery(string parent, uint offset, CancellationToken token)
            {
                Interlocked.Increment(ref calls);
                if (gate is { } pending) { entered.Set(); return pending.Task; }
                return Task.FromResult(new IndexedSizePage(EverythingSizeStatus.Available,
                    [new(Path.Combine(root, "small"), true, unknown ? null : 17), new(Path.Combine(root, "large"), true, unknown ? null : 29)], 2));
            }
            using var bridge = new FolderSizeNativeBridge(module, FolderSizeSource.Everything, FakeQuery);
            (int Hr, ulong Bytes, uint Flags) Read(string leaf)
            {
                var hr = query(Path.Combine(root, leaf), cancellationFlag, out var bytes, out _, out _, out _, out var flags);
                return (hr, bytes, flags);
            }
            if (Read("small") != (0, 17UL, 1U) || Read("large") != (0, 29UL, 1U) || calls != 1)
                throw new Exception("Native queries did not share their Everything parent batch or lost count availability.");
            invalidate(Path.Combine(root, "small", "data.bin"));
            if (Read("small").Bytes != 17 || calls != 2) throw new Exception("Descendant changes did not invalidate the shared parent query.");
            unknown = true; bridge.SetSource(FolderSizeSource.Everything);
            if (Read("small") != (0, 0UL, 0U)) throw new Exception("Unknown Everything size became an available zero.");
            bridge.SetSource(FolderSizeSource.Auto);
            if (Read("small") != (0, 5UL, 3U)) throw new Exception("Automatic fallback did not use native scanning.");
            bridge.SetSource(FolderSizeSource.BuiltIn); var previousCalls = calls;
            if (Read("large") != (0, 9UL, 3U) || calls != previousCalls)
                throw new Exception("Built-in mode contacted Everything or produced wrong metrics.");
            Marshal.WriteInt32(cancellationFlag, 1);
            if (Read("small").Hr != unchecked((int)0x800704C7)) throw new Exception("Native cancellation was ignored.");
            Marshal.WriteInt32(cancellationFlag, 0);
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.SetSource(FolderSizeSource.Everything);
            var waiting = Task.Run(() => Read("small"));
            try
            {
                if (!entered.Wait(TimeSpan.FromSeconds(2))) throw new Exception("Callback did not enter wait.");
                bridge.SetSource(FolderSizeSource.BuiltIn);
                if (!waiting.Wait(TimeSpan.FromSeconds(2)) || waiting.Result.Hr != unchecked((int)0x800704C7))
                    throw new Exception("Source switch did not retire the pending callback.");
            }
            finally { gate.TrySetResult(new(EverythingSizeStatus.IndexUnavailable, [])); waiting.GetAwaiter().GetResult(); }
            bridge.Dispose();
            if (Read("small") != (0, 5UL, 3U)) throw new Exception("Bridge disposal left a registered callback.");
            Console.WriteLine("Native folder size bridge smoke: PASS (batch sharing, invalidation, unknown/zero, native fallback, cancellation, source retirement, unregister and cache flags; no Shell view/IPC/registry)");
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
