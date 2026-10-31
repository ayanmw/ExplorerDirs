using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Owns the IContextMenu reference returned by IShellView.GetItemObject.
// No popup is shown: native verbs/state are reused by our menu and toolbar.
internal sealed class NativeShellCommandMenu : IShellCommandMenu
{
    private object? _ownerObject;
    private nint _context;
    private readonly nint _ownerWindow;
    private nint _menu;
    private readonly Dictionary<string, uint> _enabled = new(StringComparer.OrdinalIgnoreCase);

    internal NativeShellCommandMenu(object context, nint ownerWindow)
    {
        _ownerObject = context;
        _ownerWindow = ownerWindow;
        try
        {
            var unknown = Marshal.GetIUnknownForObject(context);
            try
            {
                var iid = new Guid("000214E4-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out _context));
            }
            finally { Marshal.Release(unknown); }
            _menu = CreatePopupMenu();
            if (_menu == 0) throw new InvalidOperationException("无法创建 Shell 命令菜单。");
            // CMF_CANRENAME | CMF_OPTIMIZEFORINVOKE. Canonical verbs only;
            // no popup is shown and no localized caption is parsed.
            var hr = Method<QueryMenu>(3)(_context, _menu, 0, 1, 0x7fff, 0x10 | 0x800);
            Marshal.ThrowExceptionForHR(hr);
            var count = Math.Min(hr & 0xffff, 0x7fff);
            var buffer = Marshal.AllocHGlobal(512);
            try
            {
                var verbs = ShellFileCommands.All.Select(info => info.Verb).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var getVerb = Method<CommandString>(5);
                var newFolderAllowed = InitializeNewSubmenu(count, buffer, getVerb);
                for (uint offset = 0; offset < count; offset++)
                {
                    var verb = ReadVerb(offset, buffer, getVerb);
                    if (verb is null || !verbs.Contains(verb)) continue;
                    if (!newFolderAllowed && verb.Equals("NewFolder", StringComparison.OrdinalIgnoreCase)) continue;
                    var state = GetMenuState(_menu, offset + 1, 0); // MF_BYCOMMAND
                    if (state != uint.MaxValue && (state & 3) == 0) // MF_GRAYED | MF_DISABLED
                        _enabled.TryAdd(verb, offset);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { Dispose(); throw; }
    }

    private bool InitializeNewSubmenu(int count, nint buffer, CommandString getVerb)
    {
        for (var index = 0; index < GetMenuItemCount(_menu); index++)
        {
            var item = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 7 }; // STATE | ID | SUBMENU
            if (!GetMenuItemInfo(_menu, (uint)index, true, ref item) || item.Submenu == 0 || item.Id == 0 || item.Id > count)
                continue;
            if (!string.Equals(ReadVerb(item.Id - 1, buffer, getVerb), "New", StringComparison.OrdinalIgnoreCase)) continue;
            if ((item.State & 3) != 0) return false;
            // NewFolder has a canonical verb before its menu item exists. Shell
            // materializes this one submenu on WM_INITMENUPOPUP; querying its
            // state earlier incorrectly disables New Folder in writable folders.
            var iid3 = new Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
            if (Marshal.QueryInterface(_context, in iid3, out var context3) >= 0)
            {
                try
                {
                    var handle = Marshal.GetDelegateForFunctionPointer<MenuMessage2>(
                        Marshal.ReadIntPtr(Marshal.ReadIntPtr(context3), 7 * IntPtr.Size));
                    var result = handle(context3, 0x117, item.Submenu, index, out _); // WM_INITMENUPOPUP
                    // S_FALSE is a successful HRESULT but says the message was not handled.
                    if (result == 0) return true;
                    if (result is not (1 or unchecked((int)0x80004001))) return false; // S_FALSE/E_NOTIMPL: try the legacy interface.
                }
                finally { Marshal.Release(context3); }
            }

            var iid2 = new Guid("000214F4-0000-0000-C000-000000000046");
            if (Marshal.QueryInterface(_context, in iid2, out var context2) < 0) return false;
            try
            {
                var handle = Marshal.GetDelegateForFunctionPointer<MenuMessage>(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(context2), 6 * IntPtr.Size));
                return handle(context2, 0x117, item.Submenu, index) == 0;
            }
            finally { Marshal.Release(context2); }
        }
        return true; // A direct NewFolder item, if present, still uses its native state.
    }

    private string? ReadVerb(uint offset, nint buffer, CommandString getVerb)
    {
        Marshal.WriteInt16(buffer, 0);
        if (getVerb(_context, offset, 4, 0, buffer, 256) != 0) return null; // GCS_VERBW
        // Bound the read even if a handler omitted its terminator.
        Marshal.WriteInt16(buffer, 510, 0);
        return Marshal.PtrToStringUni(buffer);
    }

    public bool IsEnabled(string verb) => _ownerObject is not null && _enabled.ContainsKey(verb);

    public int Invoke(string verb)
    {
        if (_ownerObject is null || !_enabled.TryGetValue(verb, out var offset))
            throw new InvalidOperationException("当前 Shell 命令不可用。");
        var command = new InvokeInfo
        {
            Size = (uint)Marshal.SizeOf<InvokeInfo>(),
            Owner = _ownerWindow,
            Verb = (nint)offset, // MAKEINTRESOURCE: offset within this exact native menu.
            Show = 1
        };
        return Method<InvokeMenu>(4)(_context, ref command);
    }

    public void Dispose()
    {
        if (_menu != 0) { DestroyMenu(_menu); _menu = 0; }
        if (_context != 0) { Marshal.Release(_context); _context = 0; }
        if (_ownerObject is { } owner)
        {
            _ownerObject = null;
            if (Marshal.IsComObject(owner)) Marshal.ReleaseComObject(owner);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public uint Size, Mask;
        public nint Owner, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint Submenu, Checked, Unchecked, Data, Text;
        public uint Length;
        public nint Bitmap;
    }

    private T Method<T>(int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_context), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InvokeMenu(nint self, ref InvokeInfo command);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CommandString(nint self, nuint commandId, uint flags, nint reserved, nint name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MenuMessage(nint self, uint message, nint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MenuMessage2(nint self, uint message, nint wParam, nint lParam, out nint result);

    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW")]
    private static extern bool GetMenuItemInfo(nint menu, uint item, bool byPosition, ref MenuItemInfo info);
}
