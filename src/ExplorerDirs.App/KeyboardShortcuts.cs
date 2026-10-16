namespace ExplorerDirs.App;

internal enum ExplorerShortcut
{
    None, Parent, Back, Forward, NextFocusStop, PreviousFocusStop,
    NextFocusRegion, PreviousFocusRegion, Address,
    NewTab, CloseTab, DuplicateTab, ReopenTab, NextTab, PreviousTab,
    Tab1, Tab2, Tab3, Tab4, Tab5, Tab6, Tab7, Tab8, Tab9, NewFolder,
    Refresh, Search, ToggleMaximize,
    View1, View2, View3, View4, View5, View6, View7, View8
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
        (0x09, 0) => ExplorerShortcut.NextFocusStop,      // Tab
        (0x09, 4) => ExplorerShortcut.PreviousFocusStop,  // Shift+Tab
        (0x75, 0) => ExplorerShortcut.NextFocusRegion,    // F6
        (0x75, 4) => ExplorerShortcut.PreviousFocusRegion, // Shift+F6
        (0x44, 1) => ExplorerShortcut.Address,        // Alt+D
        (0x4c, 2) => ExplorerShortcut.Address,       // Ctrl+L
        (0x73, 0) => ExplorerShortcut.Address,       // F4: select the address field.
        (0x54, 2) => ExplorerShortcut.NewTab,
        (0x54, 6) => ExplorerShortcut.ReopenTab,
        (0x57, 2) => ExplorerShortcut.CloseTab,
        (0x4b, 2) => ExplorerShortcut.DuplicateTab,
        (0x4e, 6) => ExplorerShortcut.NewFolder,     // Ctrl+Shift+N
        (0x09, 2) => ExplorerShortcut.NextTab,
        (0x09, 6) => ExplorerShortcut.PreviousTab,
        (>= 0x31 and <= 0x39, 2) => ExplorerShortcut.Tab1 + (key - 0x31),
        (>= 0x31 and <= 0x38, 6) => ExplorerShortcut.View1 + (key - 0x31),
        (0x74, 0) => ExplorerShortcut.Refresh,       // F5
        (0x72, 0) => ExplorerShortcut.Search,        // F3
        (0x45, 2) => ExplorerShortcut.Search,        // Ctrl+E
        (0x46, 2) => ExplorerShortcut.Search,        // Ctrl+F
        (0x7a, 0) => ExplorerShortcut.ToggleMaximize,// F11
        _ => ExplorerShortcut.None
    };

    public static int NextPane(int current, int visibleCount, bool reverse)
    {
        if (visibleCount < 1) return 0;
        return (current + (reverse ? visibleCount - 1 : 1)) % visibleCount;
    }
}
