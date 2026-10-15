namespace ExplorerDirs.App;

internal enum ExplorerShortcut
{
    None, Parent, Back, Forward, NextPane, PreviousPane, Address,
    NewTab, CloseTab, DuplicateTab, ReopenTab, NextTab, PreviousTab,
    Refresh, ToggleMaximize
}

internal static class KeyboardShortcuts
{
    // ModifierKeys values: Alt=1, Control=2, Shift=4. Keep this mapping free of
    // WPF types so the same rules can be checked by the console smoke test.
    public static ExplorerShortcut Resolve(int key, int modifiers) => (key, modifiers) switch
    {
        (0x08, 0) => ExplorerShortcut.Parent,        // Backspace: Q-Dir-style parent
        (0x26, 1) => ExplorerShortcut.Parent,        // Alt+Up
        (0x25, 1) => ExplorerShortcut.Back,          // Alt+Left
        (0x27, 1) => ExplorerShortcut.Forward,       // Alt+Right
        (0x09, 0) => ExplorerShortcut.NextPane,      // Tab
        (0x09, 4) => ExplorerShortcut.PreviousPane,  // Shift+Tab
        (0x44, 1) => ExplorerShortcut.Address,        // Alt+D
        (0x4c, 2) => ExplorerShortcut.Address,       // Ctrl+L
        (0x54, 2) => ExplorerShortcut.NewTab,
        (0x54, 6) => ExplorerShortcut.ReopenTab,
        (0x57, 2) => ExplorerShortcut.CloseTab,
        (0x4b, 2) => ExplorerShortcut.DuplicateTab,
        (0x09, 2) => ExplorerShortcut.NextTab,
        (0x09, 6) => ExplorerShortcut.PreviousTab,
        (0x74, 0) => ExplorerShortcut.Refresh,       // F5
        (0x7a, 0) => ExplorerShortcut.ToggleMaximize,// F11
        _ => ExplorerShortcut.None
    };

    public static int NextPane(int current, int visibleCount, bool reverse)
    {
        if (visibleCount < 1) return 0;
        return (current + (reverse ? visibleCount - 1 : 1)) % visibleCount;
    }
}
