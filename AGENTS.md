# Explorer Dirs project rules

## Product boundary

- Build a Windows file browsing window replacement with Q-Dir style multi-pane workflows. Do not replace the desktop, taskbar, login shell, or the `explorer.exe` system file.
- Use Q-Dir and other products as interaction references. Do not copy their binaries, configuration, artwork, or proprietary source into this repository.
- Keep tab, pane, layout, and file-operation behavior independently testable. Preserve existing sessions and user files when adding features.
- Each file pane uses the Windows Shell ExplorerBrowser view. Keep its native item/background hit testing and View/Sort/Group/New/Properties commands; prepend Explorer Dirs commands to the same native context menu. Do not substitute a WPF ListView for the file view without explicit product review.
- A tab header supports middle-click and left-button double-click to close. Keep at least one tab in each pane.
- Reordering tab headers must preserve each hosted ExplorerBrowser instance and its navigation history. Keep displayed order, keyboard tab cycling, and saved tab order aligned.
- Layout switching stays in the top menu and toolbar; do not add layout choices to file-view or tab-header context menus.
- Route normal file-view keyboard shortcuts through the hosted Shell view. Keep file operations in Shell interfaces rather than duplicating copy/paste logic in WPF.
- Keep navigation history independent for each tab by using ExplorerBrowser's travel log. Backspace and Alt+Up open the parent folder; Alt+Left/Right navigate history. Tab and Shift+Tab traverse each visible pane's navigation buttons, address, selected tab header, Details column header when present, and file items in exact reverse order; then wrap to the next/previous pane. Ctrl+Tab cycles tabs in the active pane. Restore file-view focus after layout changes; preserve address-bar editing shortcuts.
- Breadcrumb segments navigate the current tab only. Preserve Alt+D/Ctrl+L, the editable full path, and its existing single Tab focus stop; breadcrumb buttons must not create extra stops or take focus from another pane.
- File operations and Shell confirmation dialogs must return keyboard focus to the active tab's actual file-content HWND. Do not treat the outer Shell view HWND as the file list, and do not steal focus from an address box, toolbar control, another pane, or another application.
- After a Shell file operation, verify both the Win32 content focus and the Shell item focus. If Windows leaves no foreground window while this app is still active, reassert this app's foreground focus; never take foreground focus from another application. Keep existing multi-selection when restoring a focused item.
- Keep standard shortcuts local to the app and provide menu or toolbar routes for actions whose key combinations may be registered globally by other software. Do not install global keyboard hooks or register global hotkeys to compete with other applications.

## Source, development, and releases

- `src/` contains source code and local build/debug output. Never tell the user to run an executable from `src/` as their daily-use release.
- Keep portable INI settings out of the registry. Published versions share `profile/ExplorerDirs.ini` beside the repository's `releases/` directory; debug builds use `src/.state/ExplorerDirs.ini`. A release copied elsewhere uses its own `profile/` next to the executable. Do not merge development and release profiles implicitly.
- Session saves must not overwrite changes made by another instance or an external editor after load. Coordinate current-version writers for the same profile path and leave the on-disk file untouched on conflict.
- Store favorites in a separate `Favorites.ini` beside the session INI so older releases cannot remove them while saving sessions. Preserve external edits, invalid files, and newer formats before writing.
- Persist global layout/default new-tab view and each tab's path, view mode, icon size, sort columns, and grouping independently. Duplicating a tab copies its live settings; a new tab uses the configured default view, initially Details. Preserve legacy JSON during one-way migration.
- Put each usable build in a new immutable `releases/<version>/` directory using `scripts/publish.ps1`. Never publish over a running release or make a release depend on files under `src/`.
- Keep generated release binaries out of Git. Track release instructions and build scripts. Verify the exact release executable launches before calling it usable.
- Do not change Win+E, folder associations, or registry routing until there is a tested, reversible enable/disable flow.

## Maintaining the project

- When the user changes a durable project rule, update this file in the same change. Do not add speculative restrictions as rules.
- When accepted requirements change scope or order, update `docs/roadmap.md` and the design document in the same change; distinguish implemented code from manual acceptance.
- Keep the README's current capabilities and limitations accurate. State clearly which Shell menu behavior has been verified and that third-party extensions execute in the application process.
- Run a fresh build and focused smoke checks before committing or publishing. Record any unverified GUI behavior as pending, not complete.
- When the user is using this computer, do not launch an interactive GUI, move the cursor, or inject keys for testing. GUI automation requires an explicitly available test window; its script must require an opt-in parameter and stop sending input if another application takes focus.
- Preserve unrelated local changes and user session data. Keep commits narrow and reviewable.
