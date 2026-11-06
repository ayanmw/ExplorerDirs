using System.Runtime.InteropServices;

internal static class ProjectedMetadataGenerationProbe
{
    internal static int Run(string adapterPath)
    {
        var module = NativeLibrary.Load(Path.GetFullPath(adapterPath));
        try
        {
            var smoke = Marshal.GetDelegateForFunctionPointer<GenerationSmoke>(NativeLibrary.GetExport(module, "RunProjectedMetadataGenerationSmoke"));
            var result = smoke(out var stage);
            if (result != 0) throw new InvalidOperationException($"Projected metadata generation failed at stage {stage}: 0x{result:X8}.");
            Console.WriteLine("Projected metadata generation: PASS stale enumeration/acknowledgement; full replacement vs partial merge; root reopen; shared lease; independent roots; no window or file mutation.");
            return 0;
        }
        finally { NativeLibrary.Free(module); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GenerationSmoke(out uint stage);
}
