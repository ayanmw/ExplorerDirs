namespace ExplorerDirs.App;

internal static class ShellHeaderPresetShortcut
{
    private const nuint MouseControl = 0x0008;

    internal static bool IsControlRightClick(nuint keyFlags, string? windowClass) =>
        (keyFlags & MouseControl) != 0 &&
        string.Equals(windowClass, "SysHeader32", StringComparison.Ordinal);
}
