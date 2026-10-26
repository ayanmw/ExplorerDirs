# Modern Address Bar Shell Folder Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (recommended) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-pane native Shell folder tree popup to the Modern address bar with Q-Dir's two-stage F4 behavior.

**Architecture:** Reuse `NativeShellTree`/`ShellTreeHost` inside a pane-owned WPF `Popup` rooted at Desktop. Return canonical validated Shell folder identities from native selection, and route shortcut state through the active pane while preserving the separate address-history button.

**Tech Stack:** .NET 10 WPF, Windows Shell COM, SettingsSmoke.

**Spec:** [Modern 地址栏 Shell 目录树](../specs/2026-10-25-addressbar-folder-tree.md).

## Global Constraints

- Keep ExplorerBrowser as the file view and native Shell operations on the creating STA.
- Never navigate a different tab after a delayed popup or selection callback; bind callbacks to the current pane and tab state.
- Accept only absolute Shell names resolved and confirmed as folders by `ShellLocationIdentity`; reject URIs, search locations and non-folders.
- Do not launch GUI, ExplorerBrowser probes, or Q-Dir during implementation. Visible acceptance remains pending.
- Preserve unrelated dirty worktree changes; do not commit or publish.

## Review Focus

- F4 in Modern style follows tree → path edit; Classic F4 goes directly to path edit.
- F4 must not open address history; the existing history button still does.
- Virtual folder parsing names survive tree selection; invalid/URI/non-folder targets never navigate.
- Escape, tab change, pane unload and window deactivation close the popup without stealing focus from a newer target.
- Native HwndHost popup builds correctly, but build/smoke evidence is not visible GUI acceptance.

### Task 1: Shortcut state and identity decisions

**Files:** `src/ExplorerDirs.App/KeyboardShortcuts.cs`, new `src/ExplorerDirs.App/AddressTreeShortcutState.cs`, `src/ExplorerDirs.SettingsSmoke/Program.cs`, new `src/ExplorerDirs.SettingsSmoke/AddressTreeShortcutSmoke.cs`.

- [x] Add failing pure-state tests for Modern first/second F4, Classic F4, style changes while open, and explicit history-button separation.
- [x] Implement a small transition helper that returns `OpenTree` or `FocusAddressEdit`; Classic always chooses `FocusAddressEdit`, without storing state across panes.
- [x] Run the focused smoke and the full Release SettingsSmoke.

### Task 2: Native Shell folder selection

**Files:** `src/ExplorerDirs.App/NativeShellTree.cs`, `src/ExplorerDirs.App/ShellTreeHost.cs`, `src/ExplorerDirs.SettingsSmoke/ShellLocationIdentitySmoke.cs`.

- [x] Add failing tests for canonical file-system and `::{GUID}` folder selection names plus URI/search/non-folder rejection at the location decision boundary.
- [x] Add a generic selected-folder parsing-name accessor and a cancellable `SelectLocation` path that compares/expands Shell ancestors, retaining the existing file-system-only accessor for current tree behavior.
- [x] Ensure all navigation callbacks receive a canonical `ShellLocationIdentity` and retain generation/current-state checks.
- [x] Run focused Shell location smoke and full Release SettingsSmoke; do not instantiate ExplorerBrowser.

### Task 3: Per-pane address tree popup

**Files:** `src/ExplorerDirs.App/PaneControl.xaml`, `src/ExplorerDirs.App/PaneControl.xaml.cs`, and focused SettingsSmoke if a pure popup lifetime decision is extracted.

- [x] Add a pane-owned `Popup` under `AddressHost` containing `ShellTreeHost` rooted at Desktop; keep history button and its click handler unchanged.
- [x] Wire F4 through the active pane to open/focus tree or enter path edit according to style and current popup state.
- [x] Route folder selection through the current tab browser; close the popup on selection, Escape, tab change, pane unload and window deactivation, with stale-callback checks.
- [x] Preserve address edit text and focus restoration rules; selection from the popup navigates only the owning pane's current tab.

### Task 4: Verification and documentation

**Files:** `docs/roadmap.md`, `docs/shell-parity.md`, `docs/keyboard-shortcuts.md`, and this plan.

- [x] Run focused address-tree smoke, full Release SettingsSmoke, App Release build, and task-file `git diff --check`.
- [x] Record source/build/test evidence and explicitly leave placement, native selection, real F4/Escape focus, and virtual namespace popup acceptance pending GUI.
