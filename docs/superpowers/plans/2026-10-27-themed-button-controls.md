# Themed Button Controls Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. Keep this batch to WPF button and selection-control colors.

**Goal:** Make application buttons, toggles, checkboxes, and radio buttons follow the current semantic theme.

**Architecture:** Add implicit color-only WPF styles in `App.xaml` that bind existing semantic brushes. Leave native control templates, command/event routing, focus stops, and Shell views unchanged so the operating system still supplies its interaction states.

**Tech Stack:** .NET 10 WPF, XAML `DynamicResource`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [Theme and icons design](../specs/2026-10-26-theme-and-icons-design.md).

## Constraints and Review Focus

- Preserve click/checked state, focus navigation, accessibility labels, and disabled behavior.
- Use current window/control/border semantic brushes; do not introduce another color configuration.
- Do not replace WPF templates or recolor ExplorerBrowser, native Shell menus, or file/folder icons.
- Keep high-contrast fallback delegated to `ApplicationThemeController`.

## Task 1: Theme WPF button and selection controls

**Files:**
- Modify: `src/ExplorerDirs.App/App.xaml`
- Test: `src/ExplorerDirs.SettingsSmoke/PaneThemeMarkupSmoke.cs`
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [x] Add semantic-brush smoke assertions for Button, ToggleButton, CheckBox, and RadioButton.
- [x] Confirm the focused smoke fails before styles are added.
- [x] Add only color setters; preserve default WPF templates and state handling.
- [x] Run focused theme smoke, full Release `SettingsSmoke`, App Release build, and `git diff --check`.
- [x] Record that visual state and high-contrast behavior still need GUI acceptance; commit the batch.
