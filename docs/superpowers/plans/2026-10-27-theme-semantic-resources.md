# WPF Semantic Theme Resources Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task. Each task is a small independently verifiable batch and must be committed.

**Goal:** Give Explorer Dirs owned WPF chrome stable semantic color resources so its controls consistently follow the selected light/dark palette and Windows accent color.

**Architecture:** Keep `Theme.ini`, `SystemThemeMonitor`, and `ThemePalette` as the source of preferences and colors. `ApplicationThemeController` publishes semantic WPF brushes alongside the existing SystemColors overrides; WPF-owned XAML consumes semantic roles through `DynamicResource`. In high contrast, remove app-owned overrides so Windows retains control. ExplorerBrowser and Shell-owned file views remain unchanged.

**Tech Stack:** .NET 10 WPF, XAML DynamicResource, `ExplorerDirs.SettingsSmoke`.

**Spec:** [2026-10-26 theme and icons design](../specs/2026-10-26-theme-and-icons-design.md)

## Global Constraints

- Preserve profile `Theme.ini` v1 and `System/Light/Dark` behavior.
- Keep Windows accent color live in System, Light, and Dark modes.
- High contrast clears custom SystemColors overrides and maps semantic roles back to Windows system brushes.
- Do not claim or implement ExplorerBrowser file-row/background coloring in this work.
- Do not alter commands, focus stops, tab templates, Shell menus, or native file icons.

## Review Focus

- A system accent update must update all semantic accent brushes without rewriting `Theme.ini`.
- Light and Dark surfaces must retain readable normal, muted, and accent foregrounds.
- High contrast must remove every application-owned semantic override.
- The four-pane active border and selected-tab accent must continue using the same accent role.
- Native Shell views and Shell file/folder icons must remain outside the WPF semantic palette.

---

### Task 1: Publish semantic brush roles from the theme controller

**Files:**
- Modify: `src/ExplorerDirs.App/ApplicationThemeController.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ThemePreferenceSmoke.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/Program.cs`

**Interfaces:**
- Publish application resources named `ExplorerDirs.WindowBackgroundBrush`, `ExplorerDirs.WindowTextBrush`, `ExplorerDirs.ControlBackgroundBrush`, `ExplorerDirs.ControlTextBrush`, `ExplorerDirs.BorderBrush`, `ExplorerDirs.MutedTextBrush`, `ExplorerDirs.AccentBrush`, `ExplorerDirs.AccentTextBrush`, `ExplorerDirs.SelectionBackgroundBrush`, and `ExplorerDirs.SelectionTextBrush`.
- Values come from the current `ThemeColors`; no second palette or persistence format is introduced.

- [x] Add smoke assertions that each role is published from the selected palette and accent, and that the high-contrast path maps semantic roles back to Windows system brushes.
- [x] Run the focused theme smoke and confirm it fails because semantic resources are not yet published.
- [x] Add the semantic resource set/remove behavior to `ApplicationThemeController` while preserving current SystemColors behavior.
- [x] Run the focused theme smoke and confirm it passes for Light and Dark accent palettes.
- [x] Commit this task as part of the semantic WPF theme resource batch.

### Task 2: Move app-owned WPF chrome to semantic roles

**Files:**
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml`
- Modify: `src/ExplorerDirs.App/PhotoPrintWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/PaneThemeMarkupSmoke.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ThemePreferenceSmoke.cs`

**Interfaces:**
- WPF-owned windows, pane borders, muted status text, splitters, address surfaces, and auxiliary window borders use the semantic resource names from Task 1.
- SystemColors remain available as the fallback path when semantic resources are removed for high contrast.

- [x] Extend markup smoke checks to cover the WPF-owned color bindings and assert the active-pane border still uses `ExplorerDirs.AccentBrush`.
- [x] Use the Task 1 red result for missing semantic roles; the first full run then exposed a stale photo-print markup assertion, which was updated to the new roles before the green rerun.
- [x] Replace only the app-owned color bindings identified by the XAML/source inventory; retain transparent overlays and the print document's white paper surface.
- [x] Run focused theme and markup smokes and confirm both pass.
- [x] Update the existing photo-print markup expectation to the semantic resources after the full suite exposed that stale assertion.
- [x] Commit this task as part of the semantic WPF theme resource batch.

### Task 3: Verify release integration and record acceptance boundary

**Files:**
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [x] Run full `ExplorerDirs.SettingsSmoke` and Release-build `ExplorerDirs.App`.
- [x] Inspect `git diff --check` and confirm no Shell view, command routing, or unrelated file changes.
- [x] Record that code/configuration checks pass but live Windows theme changes, multi-window rendering, and high-contrast visuals still require authorized desktop acceptance.
- [x] Commit the verification record with the implementation batch.

## Manual Acceptance After Desktop Handoff

- Compare Light and Dark with multiple Windows accent colors across one- and four-pane layouts.
- Switch Windows theme/accent while the app is open and confirm all app windows update.
- Toggle Windows high contrast and confirm system-provided colors remain legible.
- Confirm Shell file lists and native file/folder icons remain unchanged.
