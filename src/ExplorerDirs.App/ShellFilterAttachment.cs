using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellFilterAttachment
{
    private static readonly Guid SiteId = new("C0A651F5-B48B-11D2-B5ED-006097C686F6");
    private static readonly Guid FilterId = new("9CC22886-DC8E-11D2-B1D0-00C04F8EEB3E");

    internal static void Apply(nint browserUnknown, nint filterUnknown)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(browserUnknown, in SiteId, out var site));
        try
        {
            if (filterUnknown != 0)
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(filterUnknown, in FilterId, out var filter));
                Marshal.Release(filter);
            }
            var write = Marshal.GetDelegateForFunctionPointer<SetFilter>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(site), 3 * nint.Size));
            Marshal.ThrowExceptionForHR(write(site, filterUnknown));
        }
        finally { Marshal.Release(site); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetFilter(nint self, nint filter);
}
