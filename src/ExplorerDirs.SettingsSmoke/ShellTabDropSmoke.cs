using ExplorerDirs.App;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellTabDropSmoke
{
    internal static void Run()
    {
        const uint folder = 0x20000000;
        const uint fileSystem = 0x40000000;
        const string thisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        Require(ShellTabDropTarget.TryAcceptLocation(1, folder, thisPc, out var location) &&
                location == thisPc,
            "one confirmed virtual Shell folder produces its canonical location");
        Require(ShellTabDropTarget.TryAcceptLocation(1, folder | fileSystem, @"C:\Users\Marvin", out location) &&
                location == @"C:\Users\Marvin",
            "one confirmed filesystem folder produces its normalized path");
        Require(!ShellTabDropTarget.TryAcceptLocation(1, fileSystem, @"C:\Users\Marvin\file.txt", out _) &&
                !ShellTabDropTarget.TryAcceptLocation(2, folder, thisPc, out _) &&
                !ShellTabDropTarget.TryAcceptLocation(0, folder, thisPc, out _),
            "files and zero or multiple dropped Shell items are rejected");
        Require(!ShellTabDropTarget.TryAcceptLocation(1, folder, "https://example.invalid", out _) &&
                !ShellTabDropTarget.TryAcceptLocation(1, folder, "not-an-absolute-location", out _),
            "URI and malformed Shell names are rejected");

        var shellData = CreateShellIdListData(thisPc);
        try
        {
            Require(shellData is not null,
                "Shell SHCreateDataObject creates a Shell IDList IDataObject");
            var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
            {
                cfFormat = unchecked((short)RegisterClipboardFormat("Shell IDList Array")),
                dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
            };
            var formatResult = shellData!.QueryGetData(ref format);
            var parsed = ShellTabDropTarget.TryGetShellItemFolder(shellData!, out location);
            Require(formatResult == 0,
                "Shell SHCreateDataObject exposes the Shell IDList Array clipboard format");
            Require(parsed &&
                    location == thisPc,
                "native Shell CIDA data object resolves a virtual folder through the production parser");
        }
        finally
        {
            if (shellData is not null) Marshal.ReleaseComObject(shellData);
        }

        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs-ShellDrop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        var file = Path.Combine(root, "item.txt");
        File.WriteAllText(file, "smoke");
        try
        {
            var oneFolder = CreateFileDrop(child);
            Require(ShellTabDropTarget.HasShellLocationData(oneFolder) &&
                    ShellTabDropTarget.TryGetDroppedFolder(oneFolder, out var dropped) &&
                    string.Equals(Path.GetFullPath(dropped), child, StringComparison.OrdinalIgnoreCase),
                "native Shell data object resolves one filesystem directory");
            Require(!ShellTabDropTarget.TryGetDroppedFolder(CreateFileDrop(file), out _),
                "native Shell data object rejects a filesystem file");
            Require(!ShellTabDropTarget.TryGetDroppedFolder(CreateFileDrop(child, file), out _),
                "native Shell data object rejects multiple locations");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static IDataObject CreateFileDrop(params string[] paths)
    {
        var files = new StringCollection();
        files.AddRange(paths);
        var data = new DataObject();
        data.SetFileDropList(files);
        return data;
    }

    private static System.Runtime.InteropServices.ComTypes.IDataObject? CreateShellIdListData(string location)
    {
        if (!ShellLocationIdentity.TryGetFolderPidl(location, out var pidl)) return null;
        var lastItem = ILFindLastID(pidl);
        var parent = ILClone(pidl);
        var child = ILClone(lastItem);
        var itemArray = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (parent == IntPtr.Zero || child == IntPtr.Zero || !ILRemoveLastID(parent)) return null;
            Marshal.WriteIntPtr(itemArray, child);
            var iid = new Guid("0000010E-0000-0000-C000-000000000046");
            var hr = SHCreateDataObject(parent, 1, itemArray, IntPtr.Zero, ref iid, out var dataObject);
            if (hr < 0 || dataObject == IntPtr.Zero)
            {
                if (dataObject != IntPtr.Zero) Marshal.Release(dataObject);
                return null;
            }
            try { return (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(dataObject); }
            finally { Marshal.Release(dataObject); }
        }
        finally
        {
            Marshal.FreeHGlobal(itemArray);
            if (child != IntPtr.Zero) Marshal.FreeCoTaskMem(child);
            if (parent != IntPtr.Zero) Marshal.FreeCoTaskMem(parent);
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateDataObject(nint folder, uint count, nint childItems,
        nint innerDataObject, ref Guid interfaceId, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("shell32.dll")]
    private static extern nint ILClone(nint pidl);

    [DllImport("shell32.dll")]
    private static extern nint ILFindLastID(nint pidl);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILRemoveLastID(nint pidl);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
