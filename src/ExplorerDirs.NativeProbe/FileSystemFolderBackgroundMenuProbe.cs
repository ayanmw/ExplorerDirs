using System.Runtime.InteropServices;
using System.Text;

// Checks whether an app-owned row view can obtain the standard filesystem
// background menu from IShellFolder without creating or showing an IShellView.
internal static class FileSystemFolderBackgroundMenuProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.BackgroundMenu-{Guid.NewGuid():N}");
        nint absolutePidl = 0;
        nint folder = 0;
        nint contextMenu = 0;
        nint menu = 0;
        try
        {
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(Path.Combine(fixture, "child-folder"));
            File.WriteAllText(Path.Combine(fixture, "sample.txt"), "fixture");

            Check(SHParseDisplayName(fixture, 0, out absolutePidl, 0x20000000, out var attributes));
            if (absolutePidl == 0) throw new InvalidOperationException("Shell returned a null folder PIDL.");
            if ((attributes & 0x20000000) == 0)
                throw new InvalidOperationException("The temporary filesystem fixture did not resolve as a Shell folder.");

            Check(SHBindToObject(0, absolutePidl, 0, in ShellFolderId, out folder));
            if (folder == 0) throw new InvalidOperationException("Shell returned a null filesystem IShellFolder.");

            var createResult = At<CreateViewObject>(folder, 8)(folder, 0, in ContextMenuId, out contextMenu);
            if (createResult < 0 || contextMenu == 0)
            {
                Console.WriteLine($"filesystem-folder-background-menu: create-view-object-hr=0x{createResult:X8}; available=False; shell-view-created=False");
                return 0;
            }

            menu = CreatePopupMenu();
            if (menu == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create an in-memory menu.");

            const uint firstCommand = 1;
            const uint lastCommand = 0x7fff;
            const uint flags = 0x0010 | 0x0800; // CMF_EXPLORE | CMF_EXTENDEDVERBS
            var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0,
                firstCommand, lastCommand, flags);
            if (queryResult < 0)
            {
                Console.WriteLine($"filesystem-folder-background-menu: create-view-object-hr=0x{createResult:X8}; query-context-menu-hr=0x{queryResult:X8}; available=False; shell-view-created=False");
                return 0;
            }

            var labels = ReadMenuLabels(menu);
            var verbs = ReadVerbs(contextMenu, unchecked((uint)queryResult & 0xffff));
            Console.WriteLine($"filesystem-folder-background-menu: create-view-object-hr=0x{createResult:X8}; query-context-menu-hr=0x{queryResult:X8}; available=True; items={GetMenuItemCount(menu)}; labels=[{string.Join('|', labels)}]; verbs=[{string.Join('|', verbs)}]; shown=False; invoked=False; shell-view-created=False");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"filesystem-folder-background-menu: {exception.GetType().Name};hr=0x{exception.HResult:X8};detail={exception.Message}");
            return 1;
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
            if (folder != 0) Marshal.Release(folder);
            if (absolutePidl != 0) ILFree(absolutePidl);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
        }
    }

    private static string[] ReadMenuLabels(nint menu)
    {
        var labels = new List<string>();
        var buffer = new StringBuilder(256);
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            buffer.Clear();
            _ = GetMenuStringW(menu, (uint)index, buffer, buffer.Capacity, 0x0400);
            var label = buffer.ToString().Replace("&", string.Empty, StringComparison.Ordinal);
            if (label.Length > 0) labels.Add(label);
        }
        return labels.ToArray();
    }

    private static string[] ReadVerbs(nint contextMenu, uint commandCount)
    {
        var verbs = new List<string>();
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            for (uint offset = 0; offset < commandCount; offset++)
            {
                Marshal.WriteInt16(buffer, 0);
                if (At<GetCommandString>(contextMenu, 5)(contextMenu, offset, 4, 0, buffer, 256) == 0 &&
                    Marshal.PtrToStringUni(buffer) is { Length: > 0 } verb)
                    verbs.Add(verb);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return verbs.ToArray();
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateViewObject(nint self, nint owner, in Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCommandString(nint self, uint offset, uint type, uint reserved, nint buffer, uint cchMax);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint value);
    [DllImport("shell32.dll")]
    private static extern void ILFree(nint pidl);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(nint menu, uint item, StringBuilder text, int maxCount, uint flags);
}
