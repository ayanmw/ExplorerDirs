using System.Runtime.InteropServices;

// Inspect the native New submenu without showing a menu or invoking commands.
internal static class BackgroundCommandProbe
{
    internal static void Run(nint shellView)
    {
        var iid = new Guid("000214E4-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(At<GetItems>(shellView, 15)(shellView, 0, ref iid, out var context));
        try
        {
            foreach (var flags in new uint[] { 0x810, 0x10 })
            {
                var menu = CreatePopupMenu();
                if (menu == 0) throw new InvalidOperationException("Probe popup allocation failed.");
                try
                {
                    var hr = At<QueryMenu>(context, 3)(context, menu, 0, 1, 0x7fff, flags);
                    Marshal.ThrowExceptionForHR(hr);
                    var count = hr & 0xffff;
                    Report(context, menu, count, $"flags={flags:X} before");
                    var context2Id = new Guid("000214F4-0000-0000-C000-000000000046");
                    if (Marshal.QueryInterface(context, in context2Id, out var context2) < 0) continue;
                    try
                    {
                        for (var index = 0; index < GetMenuItemCount(menu); index++)
                        {
                            var item = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 0x6 }; // MIIM_ID | MIIM_SUBMENU
                            if (!GetMenuItemInfo(menu, (uint)index, true, ref item)) throw new InvalidOperationException("Could not inspect menu item.");
                            var child = item.Submenu;
                            var id = item.Id;
                            if (child == 0 || id == 0 || id == uint.MaxValue) continue;
                            if (!string.Equals(Verb(context, id - 1), "New", StringComparison.OrdinalIgnoreCase)) continue;
                            var result = At<MenuMessage>(context2, 6)(context2, 0x117, child, index); // WM_INITMENUPOPUP
                            Console.WriteLine($"background-new submenu-init={result:X8}; child-count={GetMenuItemCount(child)}");
                        }
                        Report(context, menu, count, $"flags={flags:X} after");
                    }
                    finally { Marshal.Release(context2); }
                }
                finally { DestroyMenu(menu); }
            }
        }
        finally { Marshal.Release(context); }
    }

    private static void Report(nint context, nint menu, int count, string stage)
    {
        var found = new List<string>();
        for (uint offset = 0; offset < count; offset++)
        {
            var verb = Verb(context, offset);
            if (verb.Equals("New", StringComparison.OrdinalIgnoreCase) || verb.Equals("NewFolder", StringComparison.OrdinalIgnoreCase))
                found.Add($"{verb}:offset={offset}:state={GetMenuState(menu, offset + 1, 0):X8}");
        }
        Console.WriteLine($"background-new {stage}; allocated={count}; matches={string.Join(',', found)}");
    }

    private static string Verb(nint context, uint offset)
    {
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (At<CommandString>(context, 5)(context, offset, 4, 0, buffer, 256) != 0) return string.Empty;
            Marshal.WriteInt16(buffer, 510, 0);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint Submenu, Checked, Unchecked, Data, Text;
        public uint Length;
        public nint Bitmap;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryMenu(nint self, nint menu, uint index, uint first, uint last, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CommandString(nint self, nuint id, uint flags, nint reserved, nint text, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int MenuMessage(nint self, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW")] private static extern bool GetMenuItemInfo(nint menu, uint item, bool byPosition, ref MenuItemInfo info);
}
