using System.Runtime.InteropServices;

internal static class SearchFolderSmoke
{
    internal static void RequireFolder(nint pidl)
    {
        var id = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        Marshal.ThrowExceptionForHR(SHCreateItemFromIDList(pidl, ref id, out var item));
        try
        {
            var read = Marshal.GetDelegateForFunctionPointer<Attributes>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(item), 6 * nint.Size));
            var hr = read(item, 0x20000000, out var attributes);
            if (hr != 0 || (attributes & 0x20000000) == 0)
                throw new Exception("Search navigation must target a native folder, not a protocol launch item.");
        }
        finally { Marshal.Release(item); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Attributes(nint self, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(nint pidl, ref Guid iid, out nint item);
}
