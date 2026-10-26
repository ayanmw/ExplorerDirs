namespace ExplorerDirs.NativeProbe;

internal sealed class MergedShellMenuSelection
{
    private const uint MenuPopupFlag = 0x0010;
    private const uint MenuClosedFlags = 0xFFFF;
    private uint _commandId;
    private nint _commandMenu;
    private nint _openSubmenu;

    internal void Observe(nuint wParam, nint menu, nint compositeMenu)
    {
        var item = (uint)(wParam & 0xFFFF);
        var flags = (uint)((wParam >> 16) & 0xFFFF);
        if (flags == MenuClosedFlags && menu == 0)
            return;

        if ((flags & MenuPopupFlag) != 0)
        {
            _openSubmenu = GetSubMenu(menu, unchecked((int)item));
            _commandId = 0;
            _commandMenu = 0;
            return;
        }

        var sourceMenu = menu;
        if (ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(compositeMenu, item, sourceMenu) is null &&
            _openSubmenu != 0 &&
            ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(compositeMenu, item, _openSubmenu) is not null)
            sourceMenu = _openSubmenu;

        if (ShellViewColorSpike.ResolveMergedShellMenuCommandGroup(compositeMenu, item, sourceMenu) is null)
        {
            _commandId = 0;
            _commandMenu = 0;
            return;
        }

        _commandId = item;
        _commandMenu = sourceMenu;
    }

    internal bool TryGetSource(uint commandId, out nint sourceMenu)
    {
        sourceMenu = _commandMenu;
        return commandId != 0 && commandId == _commandId && sourceMenu != 0;
    }

    internal void Reset()
    {
        _commandId = 0;
        _commandMenu = 0;
        _openSubmenu = 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetSubMenu(nint menu, int position);
}
