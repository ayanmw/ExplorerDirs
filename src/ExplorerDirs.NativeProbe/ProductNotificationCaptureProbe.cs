using System.Runtime.InteropServices;

// Diagnostic-only, no HWND/ExplorerBrowser and no user desktop input.
internal static class ProductNotificationCaptureProbe
{
    internal static int Run(string adapterPath)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
        nint module = 0;
        try
        {
            module = NativeLibrary.Load(Path.GetFullPath(adapterPath));
            var smoke = Marshal.GetDelegateForFunctionPointer<CaptureSmoke>(
                NativeLibrary.GetExport(module, "RunProductNotificationIdentityCaptureSmoke"));
            Marshal.ThrowExceptionForHR(smoke());
            Console.WriteLine("Product notification capture: PASS (owned PIDL after source free, deferred exact names, first/last, bounds, short buffer, 32768 retained / 1 dropped; no window).");
            return 0;
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            CoUninitialize();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CaptureSmoke();
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
