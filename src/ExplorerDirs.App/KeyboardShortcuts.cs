namespace ExplorerDirs.App;

internal enum ExplorerShortcut
{
    None, Parent, Back, Forward, NextFocusStop, PreviousFocusStop,
    NextFocusRegion, PreviousFocusRegion, Address, AddressTree, AddressLocations, AddressComputer,
    NewTab, NewWindow, QuickFolderMenu, ProgramStarter, CloseTab, DuplicateTab, ReopenTab, NextTab, PreviousTab, OpenSelectionInNewTab,
    Tab1, Tab2, Tab3, Tab4, Tab5, Tab6, Tab7, Tab8, Tab9, FocusPane1, FocusPane2, FocusPane3, FocusPane4, NewFolder, InvertSelection,
    Refresh, Search, QuickFilter, ExpandTree, TogglePreviewPane, ToggleDetailsPane, ToggleFullScreen,
    RestartApplication, MoveSelectedItems, CopySelectedItems,
    CalculateDirectorySize,
    ScreenMagnifier,
    View1, View2, View3, View4, View5, View6, View7, View8, SearchSelectedFolder, ActivateMenu,
    PasteSpecial, CopySelectedPaths,
    AutoFitColumns,
    NavigateDriveA, NavigateDriveB, NavigateDriveC, NavigateDriveD, NavigateDriveE,
    NavigateDriveF, NavigateDriveG, NavigateDriveH, NavigateDriveI, NavigateDriveJ,
    NavigateDriveK, NavigateDriveL, NavigateDriveM, NavigateDriveN, NavigateDriveO,
    NavigateDriveP, NavigateDriveQ, NavigateDriveR, NavigateDriveS, NavigateDriveT,
    NavigateDriveU, NavigateDriveV, NavigateDriveW, NavigateDriveX, NavigateDriveY,
    NavigateDriveZ
}

internal static class KeyboardShortcuts
{
    // ModifierKeys values: Alt=1, Control=2, Shift=4. Keep this mapping free of
    // WPF types so the same rules can be checked by the console smoke test.
    public static ExplorerShortcut Resolve(int key, int modifiers) => (key, modifiers) switch
    {
        (0x08, 0) => ExplorerShortcut.Parent,        // Backspace: Q-Dir-style parent
        (0x08, 1) => ExplorerShortcut.Back,          // Q-Dir: Alt+Backspace.
        (0x08, 5) => ExplorerShortcut.Forward,       // Q-Dir: Alt+Shift+Backspace.
        (0x26, 1) => ExplorerShortcut.Parent,        // Alt+Up
        (0x25, 1) => ExplorerShortcut.Back,          // Alt+Left
        (0x27, 1) => ExplorerShortcut.Forward,       // Alt+Right
        (0x79, 0) => ExplorerShortcut.ActivateMenu,  // F10: activate the menu bar.
        (0x09, 0) => ExplorerShortcut.NextFocusStop,      // Tab
        (0x09, 4) => ExplorerShortcut.PreviousFocusStop,  // Shift+Tab
        (0x75, 0) => ExplorerShortcut.NextFocusRegion,    // F6
        (0x75, 4) => ExplorerShortcut.PreviousFocusRegion, // Shift+F6
        (0x44, 1) => ExplorerShortcut.Address,        // Alt+D
        (0x53, 1) => ExplorerShortcut.Address,        // Q-Dir: Alt+S focuses the address bar.
        (0x56, 1) => ExplorerShortcut.MoveSelectedItems, // Q-Dir: Alt+V cuts selected items for a later paste.
        (0x4b, 1) => ExplorerShortcut.CopySelectedItems, // Q-Dir: Alt+K copies selected items for a later paste.
        (0x76, 0) => ExplorerShortcut.AddressLocations, // Q-Dir: F7 opens the address path hierarchy.
        (0x77, 0) => ExplorerShortcut.AddressComputer, // Q-Dir: F8 opens the This PC menu.
        (0x4c, 1) => ExplorerShortcut.ScreenMagnifier, // Q-Dir: Alt+L.
        (0x4c, 2) => ExplorerShortcut.Address,       // Ctrl+L
        (0x73, 0) => ExplorerShortcut.AddressTree, // F4: Q-Dir Modern address tree / path-edit cycle.
        (0x54, 2) => ExplorerShortcut.NewTab,
        (0x4e, 2) => ExplorerShortcut.NewWindow,
        (0x51, 2) => ExplorerShortcut.QuickFolderMenu, // Q-Dir: open the quick-folder menu.
        (0x53, 2) => ExplorerShortcut.ProgramStarter, // Q-Dir: open Program Starter.
        (0x54, 6) => ExplorerShortcut.ReopenTab,
        (0x57, 2) => ExplorerShortcut.CloseTab,
        (0x4b, 2) => ExplorerShortcut.DuplicateTab,
        (0x0d, 4) => ExplorerShortcut.OpenSelectionInNewTab, // Q-Dir: open selected folder in a new tab.
        (>= 0x31 and <= 0x34, 3) => ExplorerShortcut.FocusPane1 + (key - 0x31), // Q-Dir: Ctrl+1-4 focuses a Q-View; Alt avoids the tab shortcut conflict.
        (0x6b, 2) => ExplorerShortcut.AutoFitColumns, // Ctrl+NumPad+: let the native Details view size columns.
        (0x4e, 6) => ExplorerShortcut.NewFolder,     // Ctrl+Shift+N
        (0x76, 4) => ExplorerShortcut.NewFolder,     // Q-Dir: Shift+F7.
        (0x74, 4) => ExplorerShortcut.NewFolder,     // Q-Dir: Shift+F5 opens New > New Folder.
        (>= 0x41 and <= 0x5a, 3) => ExplorerShortcut.NavigateDriveA + (key - 0x41), // Q-Dir: Ctrl+Alt+drive letter.
        (0x56, 6) => ExplorerShortcut.PasteSpecial,  // Q-Dir: save clipboard content as a file.
        (0x43, 6) => ExplorerShortcut.CopySelectedPaths, // Q-Dir: copy selected paths without quotes.
        (0x41, 6) => ExplorerShortcut.InvertSelection, // Q-Dir: Ctrl+Shift+A.
        (0x45, 6) => ExplorerShortcut.ExpandTree,    // Ctrl+Shift+E
        (0x50, 1) => ExplorerShortcut.TogglePreviewPane, // Alt+P
        (0x50, 5) => ExplorerShortcut.ToggleDetailsPane, // Alt+Shift+P
        (0x09, 2) => ExplorerShortcut.NextTab,
        (0x09, 6) => ExplorerShortcut.PreviousTab,
        (>= 0x31 and <= 0x39, 2) => ExplorerShortcut.Tab1 + (key - 0x31),
        (>= 0x31 and <= 0x38, 6) => ExplorerShortcut.View1 + (key - 0x31),
        (0x74, 0) => ExplorerShortcut.Refresh,       // F5
        (0x78, 0) => ExplorerShortcut.CalculateDirectorySize, // F9
        (0x52, 2) => ExplorerShortcut.Refresh,       // Ctrl+R
        (0x72, 0) => ExplorerShortcut.Search,        // F3
        (0x72, 1) => ExplorerShortcut.SearchSelectedFolder, // Q-Dir: Alt+F3 searches the selected folder.
        (0x45, 2) => ExplorerShortcut.Search,        // Ctrl+E
        (0x46, 2) => ExplorerShortcut.Search,        // Ctrl+F
        (0x4d, 2) => ExplorerShortcut.QuickFilter,    // Ctrl+M: Q-Dir-style quick filter
        (0x7a, 0) => ExplorerShortcut.ToggleFullScreen, // Q-Dir: F11 toggles full screen.
        (0x7a, 1) => ExplorerShortcut.RestartApplication, // Q-Dir: Alt+F11.
        _ => ExplorerShortcut.None
    };

    public static ExplorerShortcut ResolveTabHeaderArrow(int key, int modifiers, bool tabHeaderFocused)
    {
        if (!tabHeaderFocused || modifiers != 2) return ExplorerShortcut.None;
        return key switch
        {
            0x25 => ExplorerShortcut.PreviousTab, // Q-Dir: Ctrl+Left switches tabs.
            0x27 => ExplorerShortcut.NextTab,     // Q-Dir: Ctrl+Right switches tabs.
            _ => ExplorerShortcut.None
        };
    }

    public static bool RequiresFileViewFocus(ExplorerShortcut shortcut) =>
        shortcut is ExplorerShortcut.OpenSelectionInNewTab or ExplorerShortcut.InvertSelection or
            ExplorerShortcut.SearchSelectedFolder or
            ExplorerShortcut.PasteSpecial or
            ExplorerShortcut.CopySelectedPaths or
            ExplorerShortcut.MoveSelectedItems or
            ExplorerShortcut.CopySelectedItems or
            ExplorerShortcut.ProgramStarter or
            ExplorerShortcut.ScreenMagnifier or
            ExplorerShortcut.AutoFitColumns or
            (>= ExplorerShortcut.FocusPane1 and <= ExplorerShortcut.FocusPane4) or
            ExplorerShortcut.NewFolder or
            (>= ExplorerShortcut.NavigateDriveA and <= ExplorerShortcut.NavigateDriveZ);

    public static bool ShouldYieldToTextInput(ExplorerShortcut shortcut) =>
        RequiresFileViewFocus(shortcut) || shortcut == ExplorerShortcut.RestartApplication;

    public static bool ShouldYieldDriveLToMagnifier(ExplorerShortcut shortcut, bool magnifierRunning) =>
        shortcut == ExplorerShortcut.NavigateDriveL && magnifierRunning;

    public static int? ResolveFocusedPaneIndex(ExplorerShortcut shortcut, int visiblePaneCount)
    {
        if (shortcut is < ExplorerShortcut.FocusPane1 or > ExplorerShortcut.FocusPane4) return null;
        var index = (int)shortcut - (int)ExplorerShortcut.FocusPane1;
        return index < visiblePaneCount ? index : null;
    }

    public static int NextPane(int current, int visibleCount, bool reverse)
    {
        if (visibleCount < 1) return 0;
        return (current + (reverse ? visibleCount - 1 : 1)) % visibleCount;
    }
}
