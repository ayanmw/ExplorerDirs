# Tab Hover Activation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Match Q-Dir's delayed tab activation while dragging by selecting a tab after a configurable hover delay.

**Architecture:** Add a profile-level portable INI preference with Q-Dir's documented delay choices (disabled, 200, 500, 1000, 1500, 2000, or 3000 ms). Each pane uses one dispatcher timer and only starts it for an external Shell file/folder drag over a tab; app-owned tab reordering keeps its current behavior. The default is 1000 ms, an Explorer Dirs choice because Q-Dir's public string list does not establish its default.

**Tech Stack:** C#, WPF `DispatcherTimer`, `PortableIniWriter`, Release `SettingsSmoke`.

**Spec:** Q-Dir official command list documents “Auto select drag-over time” and the delay options. Existing product tab drag behavior is in `PaneControl.Tab_DragOver` and `Tab_Drop`.

## Global Constraints

- Preserve app-owned pinned/ordinary tab reorder behavior.
- Do not change drop targets or file operation semantics; a hover only selects the tab.
- Store preference in a separate portable INI and preserve newer or externally edited files.
- Do not run visible GUI or ExplorerBrowser probes in this batch.

## Review Focus

- App-owned tab reorder must never start hover activation; cover in policy smoke.
- A non-Shell payload must not start hover activation; cover in policy smoke.
- Delayed DragLeave from an earlier tab must not cancel a newer hover target; cover in gate smoke.
- Disabled preference must not create or run a timer; cover in policy smoke and menu/store smoke.
- Closing or dropping while a timer is pending must stop activation; cover gate cancellation and static pane wiring smoke.

---

### Task 1: Preference and hover gate

**Files:** `TabHoverActivationPreference.cs`, `TabHoverActivationGate.cs`, linked SettingsSmoke files.

- [x] Add failing store and deterministic gate smoke cases for allowed delays, portable round-trip, future-format protection, external-edit refusal, payload/reorder gating, target replacement, stale leave, consume, and cancel.
- [x] Run the focused smoke and confirm it fails for missing implementation.
- [x] Implement the preference store and gate with the exact delay values above.
- [x] Run focused and full Release SettingsSmoke.

### Task 2: Pane and menu integration

**Files:** `MainWindow.xaml`, `MainWindow.xaml.cs`, `PaneControl.xaml.cs`, SettingsSmoke markup checks.

- [x] Add the “标签页 → 拖动悬停自动切换” choices, persist selection, and apply changes to all panes.
- [x] Start one per-pane timer only for eligible Shell drag-over; cancel on leave, drop, target removal, preference change, or pane shutdown.
- [x] Select the still-present hovered tab when the timer elapses without consuming the drag/drop operation.
- [x] Build the App Release configuration and rerun SettingsSmoke.

### Task 3: Documentation and commit

- [x] Update `docs/roadmap.md` and `docs/shell-parity.md` with behavior and GUI acceptance limits.
- [ ] Run `git diff --check`, inspect the focused diff, and commit the batch locally.
