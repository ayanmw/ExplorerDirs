using ExplorerDirs.App;

internal static class QDirNewFolderShortcutSmoke
{
    internal static void Run()
    {
        Check(KeyboardShortcuts.Resolve(0x76, 4) == ExplorerShortcut.NewFolder &&
              KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.NewFolder),
            "Q-Dir Shift+F7 creates a folder through the active file view");
        Check(KeyboardShortcuts.Resolve(0x74, 4) == ExplorerShortcut.NewFolder &&
              KeyboardShortcuts.Resolve(0x74, 0) == ExplorerShortcut.Refresh,
            "Q-Dir Shift+F5 creates a folder while F5 still refreshes");
        Check(KeyboardShortcuts.Resolve(0x76, 0) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x76, 2) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x76, 6) == ExplorerShortcut.None,
            "F7 variants other than Shift+F7 remain unclaimed");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
