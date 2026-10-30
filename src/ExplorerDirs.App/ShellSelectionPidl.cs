using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal static class ShellSelectionPidl
{
    // Returns a caller-owned child PIDL only when the item belongs directly to
    // the supplied view folder. Selection restoration requires a child-relative
    // PIDL, including for virtual folders such as This PC.
    internal static bool TryGetDirectChild(string parentIdentity, string itemParsingName, out nint childPidl)
    {
        childPidl = 0;
        if (!ShellLocationIdentity.TryGetFolderPidl(parentIdentity, out var parentPidl)) return false;
        try
        {
            if (SHParseDisplayName(itemParsingName, IntPtr.Zero, out var itemPidl, 0, out _) < 0 || itemPidl == 0)
            {
                if (itemPidl != 0) Marshal.FreeCoTaskMem(itemPidl);
                return false;
            }
            try
            {
                var relative = ILFindChild(parentPidl, itemPidl);
                if (relative == 0 || ILGetSize(relative) != ILGetSize(ILFindLastID(relative))) return false;
                childPidl = ILClone(relative);
                return childPidl != 0;
            }
            finally { Marshal.FreeCoTaskMem(itemPidl); }
        }
        finally { Marshal.FreeCoTaskMem(parentPidl); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out nint pidl,
        uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll")] private static extern nint ILFindChild(nint parent, nint child);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("shell32.dll")] private static extern nint ILClone(nint pidl);
}
