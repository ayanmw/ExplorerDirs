using ExplorerDirs.App;

internal static class QDirHistoryShortcutSmoke
{
    internal static void Run()
    {
        Check(KeyboardShortcuts.Resolve(0x08, 1) == ExplorerShortcut.Back,
            "Q-Dir Alt+Backspace goes back");
        Check(KeyboardShortcuts.Resolve(0x08, 5) == ExplorerShortcut.Forward,
            "Q-Dir Alt+Shift+Backspace goes forward");
        Check(KeyboardShortcuts.Resolve(0x08, 0) == ExplorerShortcut.Parent,
            "plain Backspace still goes to parent");
        Check(KeyboardShortcuts.Resolve(0x08, 3) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x08, 7) == ExplorerShortcut.None,
            "Ctrl-modified Alt+Backspace combinations remain unclaimed");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
