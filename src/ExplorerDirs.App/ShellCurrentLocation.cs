using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellCurrentLocation
{
    // Returns a caller-owned absolute PIDL, or zero. The input IUnknown is borrowed.
    // Read the actual folder from the current view, not the last notification's target.
    internal static nint ReadPidl(nint unknown)
    {
        nint browser = 0, view = 0, folder = 0, pidl = 0;
        try
        {
            var browserId = new Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
            var viewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
            var folderId = new Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
            if (unknown == 0 || Marshal.QueryInterface(unknown, in browserId, out browser) != 0 ||
                Method<GetInterface>(browser, 17)(browser, ref viewId, out view) != 0 || view == 0 ||
                Method<GetInterface>(view, 5)(view, ref folderId, out folder) != 0 || folder == 0 ||
                Method<GetFolderPidl>(folder, 5)(folder, out pidl) != 0 || pidl == 0)
                return 0;
            var result = pidl;
            pidl = 0;
            return result;
        }
        finally
        {
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (folder != 0) Marshal.Release(folder);
            if (view != 0) Marshal.Release(view);
            if (browser != 0) Marshal.Release(browser);
        }
    }
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInterface(nint self, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFolderPidl(nint self, out nint pidl);
}
