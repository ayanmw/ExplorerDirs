# Explorer 详情窗格 Implementation Plan

> **For agentic workers:** Implement inline in the current project checkout. Preserve unrelated dirty work. Do not launch a visible GUI or publish a release.

**Goal:** Add a per-tab native Explorer Details pane that is mutually exclusive with Preview, persists through session/QDR extension state, and follows File Explorer's Alt+Shift+P shortcut.

**Architecture:** Keep ExplorerBrowser as the file view and use `IExplorerPaneVisibility` for `EP_DetailsPane`. Preserve the existing Preview toggle API while adding a single Shell pane state to the underlying browser/site, with transitions enforcing exclusivity.

**Tech Stack:** .NET 10 Windows WPF, ExplorerBrowser, portable INI session state, SettingsSmoke and HostProbe.

**Spec:** [Explorer 详情窗格设计](../specs/2026-10-24-details-pane-design.md).

## Global Constraints

- Use Shell-provided file views and native details; do not build a list or metadata panel.
- Per-tab state, default off, duplicate/reopen/QDR extension round-trip.
- Session v8 must read v1-v7 and preserve old content in a `.before-v8-*.bak` before first write.
- Preview and Details are mutually exclusive and must preserve the live view HWND, path, count, and selection when toggled.
- No visible GUI use and no release generation in this task.

## Task 1: Session state and pane-state regression

**Files:** `SessionStore.cs`, `ExplorerPaneVisibility.cs`, `PreviewPaneSessionSmoke.cs`, `ExplorerPaneVisibilitySmoke.cs`, `Program.cs`, SettingsSmoke project file.

- [x] Add failing tests for v8 Details round-trip, v7 default-off migration, mutual exclusion validation, native pane state mapping, and Alt+Shift+P mapping.
- [x] Run SettingsSmoke and confirm the new assertions fail for missing details state/shortcut.
- [x] Implement v8 serialization with `.before-v8` backup and safe v1-v7 parsing; reject contradictory Preview+Details state.
- [x] Pass the full SettingsSmoke suite.

## Task 2: ExplorerBrowser runtime and tab lifecycle

**Files:** `ShellBrowserSite.cs`, `ExplorerBrowserHost.cs`, `PaneControl.xaml.cs`, `ExplorerPaneVisibility.cs`.

- [x] Add one explicit Shell pane state to the browser/site and apply it to both native pane IDs.
- [x] Make Preview and Details toggles mutually exclusive while retaining existing view instances.
- [x] Thread Details state through new/duplicate/close/reopen/import/snapshot tab flows; new tabs remain off.
- [x] Add a hidden HostProbe sequence for None→Details→Preview→None and preserve HWND/path/count/selection assertions.
- [x] Run the targeted HostProbe scenario and Release build.

## Task 3: User controls, shortcut, and docs

**Files:** `MainWindow.xaml`, `MainWindow.xaml.cs`, `KeyboardShortcuts.cs`, `keyboard-shortcuts.md`, `shell-parity.md`, `roadmap.md`.

- [x] Add a checkable menu item and toolbar toggle, synchronize them on active-tab changes, and wire Alt+Shift+P.
- [x] Add QDR extension status text without changing the native base hash/format.
- [x] Update parity/roadmap evidence and explicitly retain visible rendering/focus/layout as pending GUI acceptance.
- [x] Run full SettingsSmoke, App and HostProbe Release builds, and `git diff --check`.
