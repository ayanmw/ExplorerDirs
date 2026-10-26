using System.Runtime.InteropServices;

// Read-only feasibility probe for the Windows Shell photo Print verb.
// It creates one temporary PNG and queries IContextMenu without displaying or invoking it.
internal static class PhotoPrintVerbProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/n/0AAAAASUVORK5CYII=");

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "ExplorerDirs.PhotoPrintProbe");
        var fixture = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
        var imagePath = Path.Combine(fixture, "photo.png");
        nint folderPidl = 0, imagePidl = 0, parentFolder = 0, contextMenu = 0, menu = 0, childArray = 0, text = 0;
        var created = false;
        try
        {
            Directory.CreateDirectory(fixture);
            created = true;
            File.WriteAllBytes(imagePath, OnePixelPng);
            Stage("fixture-created");

            Check(SHParseDisplayName(fixture, 0, out folderPidl, 0, out _));
            Stage("parent-pidl-parsed");
            Check(SHBindToObject(0, folderPidl, 0, in ShellFolderId, out parentFolder));
            Stage("parent-shell-folder-bound");
            Check(SHParseDisplayName(imagePath, 0, out imagePidl, 0, out _));
            Stage("image-pidl-parsed");
            var childPidl = ILFindLastID(imagePidl);
            if (childPidl == 0) throw new Exception("Could not resolve the temporary image child PIDL.");
            Stage("relative-child-pidl-found");
            childArray = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(childArray, childPidl);
            // Ask only for the one attribute this diagnostic needs; requesting every
            // SFGAO bit can make namespace providers evaluate unsupported attributes.
            uint attributes = 0x20000000; // SFGAO_FOLDER
            Check(At<GetAttributesOf>(parentFolder, 9)(parentFolder, 1, childArray, ref attributes));
            Stage($"parent-child-attributes=0x{attributes:X8}");
            Check(At<GetUiObjectOf>(parentFolder, 10)(parentFolder, 0, 1, childArray,
                in ContextMenuId, 0, out contextMenu));
            Stage("image-context-menu-acquired");

            menu = CreatePopupMenu();
            if (menu == 0) throw new Exception("Could not create a non-visible native menu handle.");
            Stage("menu-handle-created");
            var queryResult = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0, 1, 0x7fff, 0x0800);
            Stage("menu-queried");
            Check(queryResult);
            var commandCount = Math.Min(queryResult & 0xffff, 0x7fff);
            text = Marshal.AllocHGlobal(512);
            var found = false;
            var enabled = false;
            var getCommandString = At<GetCommandString>(contextMenu, 5);
            for (uint offset = 0; offset < commandCount; offset++)
            {
                Marshal.WriteInt16(text, 0);
                if (getCommandString(contextMenu, offset, 4, 0, text, 256) != 0) continue; // GCS_VERBW
                var verb = Marshal.PtrToStringUni(text);
                if (!string.Equals(verb, "Print", StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                var state = GetMenuState(menu, offset + 1, 0); // MF_BYCOMMAND
                enabled = state != uint.MaxValue && (state & 3) == 0; // MF_GRAYED | MF_DISABLED
                break;
            }

            Console.WriteLine($"photo-print-verb: context-menu=0x{queryResult:X8}; Print={(found ? (enabled ? "enabled" : "disabled") : "missing")}; invoked=False; menu-displayed=False");
            Console.WriteLine($"photo-print-verb: fixture-type=PNG; parent-window=none; foreground-unchanged={GetForegroundWindow() == foreground}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("photo-print-verb failure: " + ex.GetType().Name);
            return 1;
        }
        finally
        {
            if (text != 0) Marshal.FreeHGlobal(text);
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
            if (parentFolder != 0) Marshal.Release(parentFolder);
            if (imagePidl != 0) Marshal.FreeCoTaskMem(imagePidl);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (childArray != 0) Marshal.FreeHGlobal(childArray);
            if (created)
            {
                File.Delete(imagePath);
                Directory.Delete(fixture);
                if (Directory.Exists(fixtureRoot) && !Directory.EnumerateFileSystemEntries(fixtureRoot).Any())
                    Directory.Delete(fixtureRoot);
            }
        }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);
    private static void Stage(string name) { Console.WriteLine("photo-print-verb-stage=" + name); Console.Out.Flush(); }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetUiObjectOf(nint self, nint owner, uint count, nint pidls, in Guid iid, nint reserved, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributesOf(nint self, uint count, nint pidls, ref uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetCommandString(nint self, uint command, uint type, nint reserved, nint buffer, uint maxLength);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint result);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern nint ILFindLastID(nint pidl);
    [DllImport("user32.dll", PreserveSig = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", PreserveSig = true)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", PreserveSig = true)] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll", PreserveSig = true)] private static extern nint GetForegroundWindow();
}
