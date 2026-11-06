using System.Runtime.InteropServices;

internal static class ProjectedDeliveryQueueProbe
{
    internal static int Run(string adapterPath)
    {
        var module = NativeLibrary.Load(Path.GetFullPath(adapterPath));
        try
        {
            var smoke = Marshal.GetDelegateForFunctionPointer<QueueSmoke>(NativeLibrary.GetExport(module, "RunProjectedDeliveryQueueSmoke"));
            var result = smoke(out var stage);
            if (result != 0) throw new InvalidOperationException($"Projected delivery queue failed at stage {stage}: 0x{result:X8}.");
            Console.WriteLine("Projected delivery queue: PASS copied payloads; ordered background delivery; atomic batch submission and overflow; owner-scoped retirement without stopping peers; bounded flush boundaries and final tail; retirement without waiting for in-flight delivery; bounded queued+in-flight capacity; callback failure; worker-owned lifetime; no Shell notifications/windows/files.");
            return 0;
        }
        finally { NativeLibrary.Free(module); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueueSmoke(out uint stage);
}
