using ExplorerDirs.App;

internal static class QDirDriveShortcutSmoke
{
    internal static void Run()
    {
        Check(KeyboardShortcuts.Resolve(0x41, 3) == ExplorerShortcut.NavigateDriveA &&
              KeyboardShortcuts.Resolve(0x5a, 3) == ExplorerShortcut.NavigateDriveZ &&
              KeyboardShortcuts.Resolve(0x43, 3) == ExplorerShortcut.NavigateDriveC &&
              KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.NavigateDriveC),
            "Q-Dir Ctrl+Alt+drive-letter resolves the requested drive shortcut");
        Check(KeyboardShortcuts.Resolve(0x43, 0) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x43, 1) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x43, 2) == ExplorerShortcut.None &&
              KeyboardShortcuts.Resolve(0x43, 7) == ExplorerShortcut.None,
            "drive shortcuts require exactly Ctrl+Alt without Shift");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
