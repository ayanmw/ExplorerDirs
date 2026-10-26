# Themed Text Entry Controls Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. Keep changes to WPF text-entry and selection surfaces.

**Goal:** Make text boxes and combo boxes follow Explorer Dirs' live semantic theme and accent selection colors.

**Architecture:** Add implicit WPF styles for `TextBox`, `ComboBox`, and `ComboBoxItem` in `App.xaml`. Use existing semantic brushes for surfaces, foregrounds, borders, caret, and text selection; keep native ExplorerBrowser views and Shell icons untouched.

**Tech Stack:** .NET 10 WPF, XAML `DynamicResource`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [Theme and icons design](../specs/2026-10-26-theme-and-icons-design.md).

## Constraints and Review Focus

- Preserve input handlers, focus traversal, editable combo-box behavior, and selection models.
- Keep high-contrast behavior routed through the existing semantic palette.
- Do not replace control templates or style native Shell file lists and icons.

## Task 1: Theme text-entry and combo-box surfaces

**Files:**
- Modify: `src/ExplorerDirs.App/App.xaml`
- Test: `src/ExplorerDirs.SettingsSmoke/PaneThemeMarkupSmoke.cs`
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [x] Add smoke assertions for dynamic semantic brush bindings on TextBox, ComboBox, and ComboBoxItem.
- [x] Confirm the focused markup smoke fails before adding these styles.
- [x] Add color-only styles including accent-based caret selection and readable selection text.
- [x] Run focused theme smoke, complete Release `SettingsSmoke`, App Release build, and `git diff --check`.
- [x] Record GUI acceptance limitations and commit this batch.
