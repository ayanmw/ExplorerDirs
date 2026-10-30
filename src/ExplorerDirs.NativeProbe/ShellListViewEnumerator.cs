using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Shared ownership and identity path for filesystem and virtual Shell rows.
internal sealed record ShellListViewEnumeration(List<ShellListViewRow> Rows, nint SystemImageList);

internal static class ShellListViewEnumerator
{
    private const uint ShcontfFolders = 0x00000020;
    private const uint ShcontfNonFolders = 0x00000040;

    internal static ShellListViewEnumeration Enumerate(nint folderPidl, nint shellFolder,
        Func<ShellListViewRowIdentity, ulong?>? recursiveBytes = null)
    {
        Marshal.ThrowExceptionForHR(At<EnumObjects>(shellFolder, 4)(shellFolder, 0,
            ShcontfFolders | ShcontfNonFolders, out var enumerator));
        var rows = new List<ShellListViewRow>();
        nint systemImageList = 0;
        try
        {
            while (true)
            {
                var hr = At<EnumNext>(enumerator, 3)(enumerator, 1, out var relativePidl, out var fetched);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (fetched == 0) break;
                if (relativePidl == 0) throw new InvalidOperationException("Shell enumeration returned a null child PIDL.");

                nint absolutePidl = 0;
                nint retainedAbsolutePidl = 0;
                nint retainedRelativePidl = 0;
                try
                {
                    absolutePidl = ILCombine(folderPidl, relativePidl);
                    if (absolutePidl == 0) throw new InvalidOperationException("Could not combine the Shell parent and enumerated child PIDLs.");
                    var identity = ShellListViewRowIdentity.Capture(shellFolder, relativePidl, absolutePidl,
                        ref systemImageList);
                    var recursiveSize = recursiveBytes?.Invoke(identity);

                    retainedAbsolutePidl = ILClone(absolutePidl);
                    retainedRelativePidl = ILClone(relativePidl);
                    if (retainedAbsolutePidl == 0 || retainedRelativePidl == 0)
                        throw new InvalidOperationException("Could not retain Shell row PIDLs.");

                    rows.Add(new ShellListViewRow(rows.Count + 1, retainedAbsolutePidl, retainedRelativePidl,
                        identity.DisplayName, identity.ParsingName, identity.FileSystemPath, identity.Attributes,
                        recursiveSize, FromShellEnumeration: true, SystemImageIndex: identity.SystemImageIndex));
                    retainedAbsolutePidl = 0;
                    retainedRelativePidl = 0;
                }
                finally
                {
                    if (absolutePidl != 0) ILFree(absolutePidl);
                    if (retainedAbsolutePidl != 0) ILFree(retainedAbsolutePidl);
                    if (retainedRelativePidl != 0) ILFree(retainedRelativePidl);
                    ILFree(relativePidl);
                }
            }
            return new ShellListViewEnumeration(rows, systemImageList);
        }
        catch
        {
            FreeRows(rows);
            throw;
        }
        finally { Marshal.Release(enumerator); }
    }

    internal static void FreeRows(IEnumerable<ShellListViewRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.AbsolutePidl != 0) ILFree(row.AbsolutePidl);
            if (row.RelativePidl != 0) ILFree(row.RelativePidl);
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumObjects(nint self, nint owner, uint flags, out nint enumerator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumNext(nint self, uint count, out nint pidl, out uint fetched);

    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILClone(nint pidl);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern void ILFree(nint pidl);
}
