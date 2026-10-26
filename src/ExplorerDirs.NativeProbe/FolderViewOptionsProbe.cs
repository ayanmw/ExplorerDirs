using System.Runtime.InteropServices;

internal static class FolderViewOptionsProbe
{
    private static readonly Guid ExplorerBrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid ExplorerBrowserId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid FolderViewOptionsId = new("3CC974D2-B302-4D36-AD3E-06D93F695D3F");
    private const uint InprocServer = 1;
    private const uint CustomPosition = 0x2;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        nint browser = 0;
        nint options = 0;
        try
        {
            var classId = ExplorerBrowserClassId;
            var interfaceId = ExplorerBrowserId;
            var createResult = CoCreateInstance(ref classId, 0, InprocServer, ref interfaceId, out browser);
            if (createResult < 0) Marshal.ThrowExceptionForHR(createResult);

            var optionsId = FolderViewOptionsId;
            var queryResult = Marshal.QueryInterface(browser, in optionsId, out options);
            if (queryResult < 0 || options == 0)
            {
                Console.WriteLine($"folder-view-options-unhosted: query=0x{queryResult:X8}; foreground-unchanged={GetForegroundWindow() == foreground}; window-created=False");
                return 0;
            }

            var getOptions = Method<GetFolderViewOptions>(options, 4);
            var setOptions = Method<SetFolderViewOptions>(options, 3);
            var getResult = getOptions(options, out var originalOptions);
            if (getResult < 0) Marshal.ThrowExceptionForHR(getResult);

            var setResult = setOptions(options, CustomPosition, originalOptions | CustomPosition);
            var readResult = getOptions(options, out var readBackOptions);
            var readBackMatches = readResult >= 0 && (readBackOptions & CustomPosition) != 0;
            var restoreResult = setOptions(options, CustomPosition, originalOptions & CustomPosition);
            if (restoreResult < 0) Marshal.ThrowExceptionForHR(restoreResult);
            var verifyRestoreResult = getOptions(options, out var restoredOptions);
            var restored = verifyRestoreResult >= 0 &&
                (restoredOptions & CustomPosition) == (originalOptions & CustomPosition);

            Console.WriteLine($"folder-view-options-unhosted: query=0x{queryResult:X8}; original=0x{originalOptions:X8}; " +
                $"set-custom-position=0x{setResult:X8}; read=0x{readResult:X8}; readback={readBackMatches}; " +
                $"restored={restored}; foreground-unchanged={GetForegroundWindow() == foreground}; window-created=False");
            return restored ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"folder-view-options-unhosted failure: {ex.Message}; foreground-unchanged={GetForegroundWindow() == foreground}; window-created=False");
            return 1;
        }
        finally
        {
            if (options != 0) Marshal.Release(options);
            if (browser != 0) Marshal.Release(browser);
        }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFolderViewOptions(nint self, out uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFolderViewOptions(nint self, uint mask, uint flags);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context,
        ref Guid iid, out nint instance);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
