using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class NativeShellCommandSmoke
{
    internal static void Run()
    {
        var server = new MenuTestServer();
        var unknown = Marshal.GetIUnknownForObject(server);
        try
        {
            var context = Marshal.GetUniqueObjectForIUnknown(unknown);
            using var menu = new NativeShellCommandMenu(context, (nint)42);
            Check(menu.IsEnabled("copy") && menu.IsEnabled("rename") && !menu.IsEnabled("delete"),
                "canonical verbs ignore translated captions and respect MF_DISABLED");
            Check(menu.Invoke("copy") == 0 && server.LastOffset == 1 && server.Owner == 42,
                "invocation uses offset, not absolute menu ID");
            Check(menu.Invoke("rename") == 0 && server.LastOffset == 0,
                "offset zero is a valid MAKEINTRESOURCE command");
            Check(server.Flags == 0x810, "rename and optimize-for-invoke flags are passed");
            try { menu.Invoke("delete"); throw new Exception("disabled command accepted"); }
            catch (InvalidOperationException) { }
            menu.Dispose();
            Check(!menu.IsEnabled("copy"), "disposed menu cannot invoke stale context");
        }
        finally { Marshal.Release(unknown); GC.KeepAlive(server); }
        foreach (var (parentEnabled, enabled) in new[] { (true, true), (true, false), (false, true) })
        {
            var lazy = new MenuTestServer { LazyNewMenu = true, NewMenuEnabled = parentEnabled, NewFolderEnabled = enabled };
            var lazyUnknown = Marshal.GetIUnknownForObject(lazy);
            try
            {
                using var menu = new NativeShellCommandMenu(Marshal.GetUniqueObjectForIUnknown(lazyUnknown), 42);
                Check(lazy.Initializations == (parentEnabled ? 1 : 0) && menu.IsEnabled("NewFolder") == (parentEnabled && enabled),
                    "lazy New submenu retains parent and child disabled states");
                Check(lazy.LastOffset == -1, "availability query never invokes NewFolder");
                if (parentEnabled && enabled)
                    Check(menu.Invoke("NewFolder") == 0 && lazy.LastOffset == 1,
                        "new folder uses its initialized native command offset");
            }
            finally { Marshal.Release(lazyUnknown); GC.KeepAlive(lazy); }
        }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Native Shell command: {label}");
    }
}

[ComVisible(true), Guid("000214E4-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMenuTest
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint firstId, uint lastId, uint flags);
    [PreserveSig] int InvokeCommand(ref TestMenuInvoke info);
    [PreserveSig] int GetCommandString(nuint command, uint flags, nint reserved, nint text, uint count);
}

[ComVisible(true), Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMenuTest2
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint firstId, uint lastId, uint flags);
    [PreserveSig] int InvokeCommand(ref TestMenuInvoke info);
    [PreserveSig] int GetCommandString(nuint command, uint flags, nint reserved, nint text, uint count);
    [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
}

[StructLayout(LayoutKind.Sequential)]
public struct TestMenuInvoke
{
    public uint Size, Mask;
    public nint Owner, Verb, Parameters, Directory;
    public int Show;
    public uint HotKey;
    public nint Icon;
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class MenuTestServer : IMenuTest, IMenuTest2
{
    public uint Flags;
    public nint LastOffset = -1, Owner;
    public bool LazyNewMenu, NewFolderEnabled;
    public bool NewMenuEnabled = true;
    public int Initializations;
    private nint newMenu;
    private uint newFolderId;
    public int QueryContextMenu(nint menu, uint index, uint firstId, uint lastId, uint flags)
    {
        Flags = flags;
        if (LazyNewMenu)
        {
            newMenu = CreatePopupMenu();
            newFolderId = firstId + 1;
            AppendMenu(menu, NewMenuEnabled ? 0x10u : 0x13u, (nuint)newMenu, "任意子菜单名称");
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 2, Id = firstId };
            if (!SetMenuItemInfo(menu, 0, true, ref info)) throw new InvalidOperationException("Fake submenu ID failed.");
            return 2;
        }
        AppendMenu(menu, 0, firstId, "任意标题一");
        AppendMenu(menu, 0, firstId + 1, "任意标题二");
        AppendMenu(menu, 3, firstId + 2, "任意标题三");
        return 3;
    }
    public int InvokeCommand(ref TestMenuInvoke info)
    {
        if (info.Size != Marshal.SizeOf<TestMenuInvoke>() || info.Show != 1)
            return unchecked((int)0x80070057);
        LastOffset = info.Verb;
        Owner = info.Owner;
        return 0;
    }
    public int GetCommandString(nuint command, uint flags, nint reserved, nint text, uint count)
    {
        if (flags != 4 || count < 16 || command > 2) return unchecked((int)0x80070057);
        var verb = LazyNewMenu ? command == 0 ? "New" : "NewFolder" : command == 0 ? "rename" : command == 1 ? "copy" : "delete";
        Marshal.Copy((verb + '\0').ToCharArray(), 0, text, verb.Length + 1);
        return 0;
    }

    public int HandleMenuMsg(uint message, nint wParam, nint lParam)
    {
        if (!LazyNewMenu || message != 0x117 || wParam != newMenu || lParam != 0 || Initializations != 0)
            throw new InvalidOperationException("Unexpected submenu initialization.");
        Initializations++;
        AppendMenu(newMenu, NewFolderEnabled ? 0u : 3u, newFolderId, "任意新建标题");
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint Submenu, Checked, Unchecked, Data, Text;
        public uint Length;
        public nint Bitmap;
    }
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "SetMenuItemInfoW")] private static extern bool SetMenuItemInfo(nint menu, uint item, bool position, ref MenuItemInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
}
