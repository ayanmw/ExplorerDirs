# Independent Window Sessions Implementation Plan

> **For agentic workers:** Implemented natively in this continuing task; steps use checkbox syntax for tracking.

**Goal:** Add Ctrl+N windows whose pane, tab, view, selection metadata, and pinned tabs persist independently and restore together on the next launch.

**Architecture:** Keep the existing `ExplorerDirs.ini` as the first window's backward-compatible session file. Store additional window sessions as distinct INI files in the same profile directory and list them in a conflict-protected window-session registry. Keep favorites, quick-filter favorites, UI preferences, navigation-tree preference, and layout proportions profile-wide; pin state follows its window session. A graceful close of one window while other windows remain removes only that session from the registry; the final window's session list remains available for next-launch recovery.

**Tech Stack:** .NET 10 Windows WPF, portable INI stores, `PortableIniWriter`, console SettingsSmoke.

**Spec:** User decision in this task: “每窗口独立会话文件（推荐）”; project constraints in `AGENTS.md` on portable INI, conflict protection, and shared profile settings.

## Global Constraints

- Published versions share `profile/ExplorerDirs.ini` beside the repository's `releases/` directory; debug builds use `src/.state/ExplorerDirs.ini`.
- Session saves must not overwrite changes made by another instance or an external editor after load.
- Favorites and preference stores remain portable and shared at profile scope.
- Do not launch or automate a visible GUI in this task.

## Review Focus

- A profile without a registry upgrades to one `ExplorerDirs.ini` window without losing its existing state.
- Two open window sessions save independently even if their snapshots diverge.
- Closing one window while another remains removes only the closed session; last-window close preserves the restart roster.
- Invalid, missing, or newer-version registry data must not discard the readable primary session or be overwritten unsafely.
- Window-local pinned tabs must not leak between sessions; shared favorites and preferences must remain at profile scope.

---

### Task 1: Window session registry and persistence tests

**Files:**
- Create: `src/ExplorerDirs.App/WindowSessionRegistry.cs`
- Create: `src/ExplorerDirs.SettingsSmoke/WindowSessionRegistrySmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`

**Interfaces:** `WindowSessionRegistry(profilePath)` exposes ordered session paths, creates an additional initialized session from a complete `SessionState`, and removes a non-final session. `SessionStore` remains the authoritative parser/writer for each window's session data.

- [x] Add failing smoke coverage for primary-session migration, session creation, reload order, remove-one/preserve-final semantics, and conflict rejection.
- [x] Run the focused smoke and observe the expected missing-type failure.
- [x] Implement registry with validated generated file names, conflict-protected INI writes, and distinct window session files.
- [x] Run focused smoke and verify it passes.

### Task 2: MainWindow window-local state and shared profile settings

**Files:**
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`

**Interfaces:** `MainWindow` accepts an explicit session path and a callback for opening a sibling window. It exposes an immutable session snapshot for cloning. Pinned tabs use a sidecar derived from that window's session filename; all other preference stores continue to use the profile directory.

- [x] Add a `Ctrl+N` shortcut mapping and menu entry with an accelerator label.
- [x] Add a toolbar button to create a window without relying on a global hotkey.
- [x] Create a new session file and registry entry before showing a cloned window; remove the entry if construction fails.
- [x] Build the app and run SettingsSmoke.

### Task 3: Startup restore and close semantics

**Files:**
- Modify: `src/ExplorerDirs.App/App.xaml.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify: `docs/keyboard-shortcuts.md`
- Modify: `docs/shell-parity.md`
- Modify: `docs/roadmap.md`

- [x] Load all registered window sessions, falling back to the legacy primary file when no roster exists.
- [x] Route folder-open IPC to the active available window while restoring the registered windows.
- [x] Remove a closed session only when another window remains; preserve the final roster for restart recovery.
- [x] Run full app build, SettingsSmoke, and isolated two-session persistence regression.
- [x] Run `git diff --check`; report hidden tests and the remaining visible Ctrl+N/restart GUI checks separately.
