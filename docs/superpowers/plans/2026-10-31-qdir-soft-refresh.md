# Q-Dir Soft-Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persisted global Soft-Refresh switch that uses the native Shell `FWF_NOENUMREFRESH` flag.

**Architecture:** Keep ExplorerBrowser and `IFolderView2`; toggle only the native no-enumeration-on-refresh bit. Store one global portable boolean and pass it to current and future pane views.

**Tech Stack:** C#, WPF, Windows Shell COM, portable INI, Release SettingsSmoke.

**Spec:** `docs/superpowers/specs/2026-10-31-qdir-soft-refresh.md`

## Global Constraints

- Default off; preserve the current auto-refresh behavior until the user enables Soft-Refresh.
- Preserve every unrelated `FOLDERFLAGS` bit and verify the requested bit by reading back.
- Keep the native Shell context menu and file-operation implementation unchanged.
- Do not launch GUI or publish a release in this batch.

## Review Focus

- Missing/invalid/future INI data must follow the existing portable preference protection policy.
- Views created after startup or after a toggle must receive the current value.
- Shell failures in one tab must not prevent applying the setting to other views; report failed paths.
- The tooltip must explain that new/changed items can remain absent until a full enumeration.
- Static/hidden verification cannot establish the exact Q-Dir visible behavior; leave it for the prepared GUI acceptance batch.

---

### Task 1: Native flag helper and regression contract

**Files:**
- Modify: `src/ExplorerDirs.App/ShellFolderViewOptions.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ShellFolderViewOptionsSmoke.cs`

- [x] Add a regression for enabling/disabling `FWF_NOENUMREFRESH` while unrelated bits remain intact.
- [x] Run the settings smoke before implementation; it failed because `SetNoEnumRefresh` was missing.
- [x] Add a focused helper that sets the single flag and verifies the readback.
- [x] Run the full Release SettingsSmoke and confirm the contract passes.

### Task 2: Portable preference and global menu routing

**Files:**
- Modify: `src/ExplorerDirs.App/UiPreferencesStore.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/FullRowSelectionRoutingSmoke.cs` or a focused sibling smoke
- Modify: `src/ExplorerDirs.SettingsSmoke/StatusBarVisibilitySmoke.cs` or a focused preference smoke

- [x] Add an isolated INI boolean preference, default false.
- [x] Add the checkable View-menu entry with a clear refresh/re-enumeration tooltip.
- [x] Apply changes to existing views and pass the preference to newly created views/tabs.
- [x] Verify startup default, portable round-trip, menu/model wiring, multi-pane propagation, and new-tab propagation through SettingsSmoke/source-routing contracts.
- [x] Run full Release SettingsSmoke, App Release build, and `git diff --check`.

### Task 3: Roadmap and review

**Files:**
- Modify: `docs/shell-parity.md`
- Modify: `docs/roadmap.md`

- [x] Record the exact native flag and verification boundary; keep visible Q-Dir comparison open.
- [x] Review the full diff and commit the implementation batch (`a17cf1a Add experimental soft refresh option`); subsequent parity wording was reconciled in `f8eb06c` and `cd5dfa0`.
