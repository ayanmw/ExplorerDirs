# Q-Dir Full-Row Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add a portable View-menu preference that enables or disables Windows Shell full-row selection across existing and newly created file views.

**Architecture:** Reuse the existing ExplorerBrowser and `IFolderView2` flag access in `ShellFolderViewOptions`. Preserve the Shell default when `FullRowSelection.ini` is absent; when the user explicitly chooses a value, update only `FWF_FULLROWSELECT` and preserve all other folder flags. Store the preference with the established protected portable-boolean format.

**Tech Stack:** C#, WPF, Windows Shell `IFolderView2`, portable INI, SettingsSmoke.

**Spec:** `docs/superpowers/specs/2026-10-27-qdir-full-row-selection-design.md`

## Global Constraints

- Keep ExplorerBrowser and native Shell file operations; do not replace the file view.
- Missing preference means follow the Shell default and must not call `SetCurrentFolderFlags` at startup.
- Change only `FWF_FULLROWSELECT` (`0x00200000`) and preserve unrelated flags.
- Apply explicit state to every existing and newly created tab view.
- Protect invalid settings with a backup and future settings versions from overwrite.

## Review Focus

- Missing INI leaves current Shell flags untouched; test the absent-file state.
- Explicit enable/disable preserves unrelated Shell flags; test both transitions and unrelated bits.
- A view created after toggling receives the explicit preference; cover constructor and `OnViewCreated` wiring.
- Invalid INI returns to follow-Shell mode after backup; verify the existing portable preference behavior.
- A Shell application failure is reported in the status bar; verify the failure aggregation path.

### Task 1: Portable preference and native folder flag

**Files:**
- Modify: `src/ExplorerDirs.App/UiPreferencesStore.cs`
- Modify: `src/ExplorerDirs.App/ShellFolderViewOptions.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/FolderHeaderFlagsSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/StatusBarVisibilitySmoke.cs`

- [x] Add failing smoke cases for `FWF_FULLROWSELECT` enable/disable, unrelated flag preservation, and portable preference default/round-trip/protection.
- [x] Run the focused SettingsSmoke build and confirm the expected missing API/type failures.
- [x] Implement the flag transform and an explicit-value signal without changing existing boolean preference load semantics.
- [x] Run focused full-row selection smoke checks; full SettingsSmoke reached the unrelated Program Starter launcher cleanup and stopped because its working directory remained in use by a child process.

### Task 2: Apply preference to all ExplorerBrowser views

**Files:**
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`

- [x] Add source/contract smoke coverage for menu wiring, explicit-state propagation, and `OnViewCreated` application.
- [x] Implement the View-menu command, per-pane propagation, per-host initialization, Shell HRESULT reporting, and future-view application.
- [x] Run focused full-row selection smoke and Release App build (0 warnings/errors); full-suite limitation is recorded above.

### Task 3: Record parity and verification boundary

**Files:**
- Modify: `docs/roadmap.md`
- Modify: `docs/shell-parity.md`
- Modify: this plan

- [x] Record code/build/smoke evidence and explicitly leave pixels, selection, context-menu hit testing, and restart behavior for visible acceptance.
- [x] Review diff, run `git diff --check`, and commit this batch.
