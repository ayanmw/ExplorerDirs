using System.Runtime.InteropServices;

// Capability inspection only: never Initialize, Browse, CreateEntry, TravelTo or RemoveEntry.
internal static class ExplorerBrowserHistoryCapabilityProbe
{
    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var clsid = new Guid("71f96385-ddd6-48d3-a0c1-ae06e8b055fb");
        var browserId = new Guid("dfd3b6b5-c10c-4be9-85f6-a66969f402f6");
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, 0, 1, ref browserId, out var browser));
        try
        {
            var viewId = new Guid("000214e3-0000-0000-c000-000000000046");
            var getView = Method<GetCurrentView>(browser, 17);
            var viewResult = getView(browser, ref viewId, out var view);
            if (view != 0) { Marshal.Release(view); throw new InvalidOperationException("Uninitialized browser unexpectedly has a file view."); }
            var providerId = new Guid("6d5140c1-7436-11ce-8034-00aa006009fa");
            var providerResult = Marshal.QueryInterface(browser, in providerId, out var provider);
            var serviceResult = unchecked((int)0x80004002);
            var countResult = unchecked((int)0x80004002);
            uint count = 0;
            var countQueried = false;
            try
            {
                if (providerResult >= 0 && provider != 0)
                {
                    // Windows SDK tlogstg.h defines SID_STravelLogCursor as IID_ITravelLogStg.
                    var travelId = new Guid("7ebfdd80-ad18-11d3-a4c5-00c04f72d6b8");
                    serviceResult = Method<QueryService>(provider, 3)(provider, ref travelId, ref travelId, out var travel);
                    try
                    {
                        if (serviceResult >= 0 && travel != 0)
                        {
                            countQueried = true;
                            countResult = Method<GetCount>(travel, 7)(travel, 0x31, out count); // TLEF_ABSOLUTE
                        }
                    }
                    finally { if (travel != 0) Marshal.Release(travel); }
                }
            }
            finally { if (provider != 0) Marshal.Release(provider); }
            var finalViewResult = getView(browser, ref viewId, out var finalView);
            if (finalView != 0) { Marshal.Release(finalView); throw new InvalidOperationException("History inspection unexpectedly created a file view."); }
            HostProbeForegroundGuard.EnsureUnchanged(foreground, GetForegroundWindow(), "uninitialized ExplorerBrowser history inspection");
            var countDescription = countQueried ? $"0x{countResult:X8}" : "not-called";
            var entries = countQueried && countResult >= 0 ? count.ToString() : "unavailable";
            Console.WriteLine($"ExplorerBrowser history capability: initialized=False; view-created=False; view-hr=0x{viewResult:X8}/0x{finalViewResult:X8}; provider-hr=0x{providerResult:X8}; travel-service-hr=0x{serviceResult:X8}; count-hr={countDescription}; entries={entries}; history-mutated=False; foreground-unchanged=True");
            return 0;
        }
        finally { Marshal.Release(browser); }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCurrentView(nint self, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryService(nint self, ref Guid service, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCount(nint self, uint flags, out uint count);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
