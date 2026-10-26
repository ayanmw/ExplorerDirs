# Themed ListBox Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. Scope is app-owned WPF list controls only.

**Goal:** Theme WPF list surfaces and selected rows with the current semantic palette.

**Architecture:** Add implicit `ListBox` and `ListBoxItem` styles in `App.xaml`. Use control surface/text/border roles and the accent/accent-text pair for selected rows; leave native Shell views untouched.

**Tech Stack:** .NET 10 WPF, XAML `DynamicResource`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [Theme and icons design](../specs/2026-10-26-theme-and-icons-design.md).

## Constraints and Review Focus

- Preserve ListBox selection/focus behavior and default control templates.
- Use the paired accent background/text roles to keep selection readable.
- Do not style ExplorerBrowser content or native file and folder icons.

## Task 1: Theme app-owned WPF list rows

**Files:**
- Modify: `src/ExplorerDirs.App/App.xaml`
- Test: `src/ExplorerDirs.SettingsSmoke/PaneThemeMarkupSmoke.cs`
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [x] Add smoke checks for ListBox surfaces and selected-row accent colors.
- [x] Confirm the focused smoke fails before adding list styles.
- [x] Apply semantic surface, border, text, and selected colors.
- [x] Run focused theme smoke, full Release `SettingsSmoke`, App Release build, and `git diff --check`.
- [x] Record GUI acceptance limits and commit this batch.
