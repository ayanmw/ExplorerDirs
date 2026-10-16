namespace ExplorerDirs.App;

internal static class ShellMenuCommand
{
    // WM_COMMAND uses the high word and lParam to distinguish menu selections
    // from accelerator and control notifications (Win32 WM_COMMAND contract).
    public static bool TryGetMenuId(nint wParam, nint lParam, out ushort id)
    {
        var value = wParam.ToInt64();
        id = unchecked((ushort)value);
        return lParam == IntPtr.Zero && ((value >> 16) & 0xffff) == 0 && id != 0;
    }
}
