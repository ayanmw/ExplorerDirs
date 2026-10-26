using ExplorerDirs.App;

internal static class ShellHeaderPresetShortcutSmoke
{
    internal static void Run()
    {
        Check(ShellHeaderPresetShortcut.IsControlRightClick(0x0008, "SysHeader32"),
            "Ctrl+right-click on a Shell column header is claimed");
        Check(!ShellHeaderPresetShortcut.IsControlRightClick(0x0000, "SysHeader32"),
            "plain right-click on a Shell column header remains native");
        Check(!ShellHeaderPresetShortcut.IsControlRightClick(0x0008, "DirectUIHWND"),
            "Ctrl+right-click outside the header remains native");

        var gate = new ShellHeaderPresetMenuGate();
        var headerPoint = new ShellMenuPoint(140, 260);
        Check(gate.BeginRightClick(0x0008, "SysHeader32"), "header chord arms one context message");
        Check(gate.TryConsume(headerPoint, out var actualPoint) && actualPoint == headerPoint,
            "mouse context coordinates are delivered to the preset menu");
        Check(!gate.TryConsume(headerPoint, out _), "one header click consumes at most one context message");
        Check(!gate.BeginRightClick(0x0000, "SysHeader32") &&
            !gate.TryConsume(headerPoint, out _), "plain header right-click does not consume the Shell menu");
        Check(gate.BeginRightClick(0x0008, "SysHeader32"), "a second header chord can arm after consumption");
        gate.Reset();
        Check(!gate.TryConsume(headerPoint, out _), "cancelled header click cannot capture a later menu");
        Check(gate.BeginRightClick(0x0008, "SysHeader32") &&
            !gate.TryConsume(new ShellMenuPoint(-1, -1), out _) &&
            !gate.TryConsume(headerPoint, out _), "keyboard context menus cancel the pending mouse chord");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException($"Shell header preset shortcut: {label}");
    }
}
