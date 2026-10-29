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
        Check(KeyboardShortcuts.Resolve(0x31, 3) == ExplorerShortcut.FocusPane1 &&
              KeyboardShortcuts.Resolve(0x34, 3) == ExplorerShortcut.FocusPane4 &&
              KeyboardShortcuts.RequiresFileViewFocus(ExplorerShortcut.FocusPane4) &&
              KeyboardShortcuts.ResolveFocusedPaneIndex(ExplorerShortcut.FocusPane4, visiblePaneCount: 4) == 3 &&
              KeyboardShortcuts.ResolveFocusedPaneIndex(ExplorerShortcut.FocusPane4, visiblePaneCount: 3) is null,
            "Ctrl+Alt+1-4 focus the corresponding Q-Dir pane and yield to text inputs");
        Check(KeyboardShortcuts.Resolve(0x31, 2) == ExplorerShortcut.Tab1 &&
              KeyboardShortcuts.Resolve(0x31, 6) == ExplorerShortcut.View1,
            "pane focus shortcuts preserve existing Ctrl+digit tabs and Ctrl+Shift+digit views");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
