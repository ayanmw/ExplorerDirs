# 隐藏项目显示偏好 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a portable, application-wide preference to follow Windows hidden-item settings or explicitly show/hide ordinary hidden items in ExplorerBrowser views.

**Architecture:** Persist a three-state preference in `HiddenItems.ini`. Pass the resolved policy through `MainWindow` and each `PaneControl` to every `ExplorerBrowserHost`; combine it with the existing `ShellFolderFilter` used by quick filtering. Refresh all existing views when the global preference changes.

**Tech Stack:** C# 14, WPF, Windows Shell `IFolderFilterSite`, portable INI, `ExplorerDirs.SettingsSmoke`.

**Spec:** `docs/superpowers/specs/2026-10-26-hidden-items-design.md`

## Global Constraints

- Use native ExplorerBrowser enumeration; do not replace the Shell file view.
- Default to following Windows and never set `SHCONTF_INCLUDESUPERHIDDEN`.
- Store user preference beside the profile INI and preserve unknown/newer or externally edited data.
- Do not write Windows user settings or launch GUI during source-only verification.

## Review Focus

- Unsupported or damaged INI must not overwrite the original without a successful backup: test malformed and future-version files.
- Explicit show must include ordinary hidden items but exclude protected system items: test exact `SHCONTF` flags.
- Explicit hide must omit ordinary hidden items: test exact `SHCONTF` flags.
- Following Windows must use the live Shell setting in the filter: test both system states and explicit override precedence.
- Changing preference must update every existing tab without losing quick-filter behavior: test the pure pane-policy fan-out gate; visible Shell refresh remains a GUI acceptance item.

---

### Task 1: Preference state and Shell enumeration policy

**Files:**
- Create: `src/ExplorerDirs.App/HiddenItemsPreference.cs`
- Modify: `src/ExplorerDirs.App/ShellFolderFilter.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/HiddenItemsPreferenceSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`, `src/ExplorerDirs.SettingsSmoke/Program.cs`

**Interfaces:** `HiddenItemsPreference` has `FollowWindows`, `Show`, and `Hide`; `HiddenItemsPreferenceStore.Load()` defaults to `FollowWindows`; `ShellFolderFilter.GetEnumerationFlags(HiddenItemsPreference preference, bool windowsShowsHidden)` resolves only `SHCONTF_INCLUDEHIDDEN` and never `SHCONTF_INCLUDESUPERHIDDEN`.

- [x] **Step 1:** Add failing tests for default, round-trip, damaged/newer/external-edit protection and exact enumeration flags.
- [x] **Step 2:** Run the focused smoke and verify it fails for missing preference policy.
- [x] **Step 3:** Implement the preference enum/store and enumeration resolver.
- [x] **Step 4:** Run the focused smoke and verify it passes.

### Task 2: Apply app preference to all views and menu

**Files:**
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `PaneControl.xaml.cs`, `ExplorerBrowserHost.cs`, `scripts/publish-smoke.ps1`, `docs/shell-parity.md`, `docs/roadmap.md`
- Test: add focused policy routing assertions in `HiddenItemsPreferenceSmoke.cs` where possible.

**Interfaces:** `ExplorerBrowserHost.SetHiddenItemsPreference(HiddenItemsPreference)` updates filter attachment for the current view; `PaneControl.SetHiddenItemsPreference(HiddenItemsPreference)` fans the choice to all existing tab hosts; `MainWindow` saves and applies the selected choice to all four panes.

- [x] **Step 1:** Add failing state-routing tests proving explicit preference causes filter attachment, FollowWindows without quick filter leaves the native default untouched, search does not reuse quick-filter patterns, and only initialized Shell views are refreshed.
- [x] **Step 2:** Run the focused smoke and verify expected missing-policy compile failures.
- [x] **Step 3:** Add the versioned profile store, three radio menu items, and all-pane/all-tab policy propagation. Search retains an empty quick-filter rule set while hidden-item filtering remains active; deferred hosts adopt the preference without attempting refresh before a view exists.
- [x] **Step 4:** Add `HiddenItems.ini` to the isolated profile whitelist and its packaging smoke fixture.
- [x] **Step 5:** Focused and full Release `SettingsSmoke`, App Release build (0 warnings/errors), `publish-smoke.ps1`, and relevant `git diff --check` passed.
- [x] **Step 6:** Update the parity table and roadmap with verification and visible acceptance boundaries.

### Task 3: Product visible acceptance

- [ ] In an authorized isolated GUI session, verify show/hide/follow behavior on ordinary hidden entries, protected system entries, active quick filters, navigation, refresh and multiple tabs. Do not claim this task complete from source tests alone.
