using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellFolderChildIdentity
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static bool TryResolveChildFolder(string parentIdentity, string? childName, out string childIdentity)
    {
        childIdentity = string.Empty;
        if (!IsSingleChildDisplayName(childName) ||
            !ShellLocationIdentity.TryGetFolderPidl(parentIdentity, out var parentPidl))
            return false;
        var name = childName!;

        try
        {
            // With a null parent folder, SHBindToObject expects an absolute PIDL as its second argument.
            if (SHBindToObject(IntPtr.Zero, parentPidl, IntPtr.Zero, in ShellFolderId, out var folder) < 0 || folder is null)
                return false;
            try
            {
                var parseResult = folder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, name,
                    out _, out var childPidl, out _);
                if (parseResult < 0 || childPidl == IntPtr.Zero)
                {
                    if (childPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(childPidl);
                    return false;
                }

                try
                {
                    var absolutePidl = ILCombine(parentPidl, childPidl);
                    if (absolutePidl == IntPtr.Zero) return false;
                    try
                    {
                        return ShellLocationIdentity.TryGetPersistedIdentity(absolutePidl, out childIdentity, out _);
                    }
                    finally { Marshal.FreeCoTaskMem(absolutePidl); }
                }
                finally { Marshal.FreeCoTaskMem(childPidl); }
            }
            finally { Marshal.ReleaseComObject(folder); }
        }
        catch (COMException)
        {
            childIdentity = string.Empty;
            return false;
        }
        finally { Marshal.FreeCoTaskMem(parentPidl); }
    }

    private static bool IsSingleChildDisplayName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." &&
        !value.Contains('\\') && !value.Contains('/') && !value.Any(char.IsControl);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr owner, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name,
            out uint charactersEaten, out IntPtr relativePidl, out uint attributes);
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(IntPtr parentFolder, IntPtr relativeOrAbsolutePidl, IntPtr bindContext, in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
}
