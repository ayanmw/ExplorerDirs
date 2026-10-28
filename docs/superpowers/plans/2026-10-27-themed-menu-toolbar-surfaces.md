# Themed Menu and Toolbar Surfaces Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task. Keep the batch limited to WPF-owned menu and toolbar surfaces.

**Goal:** Make application menus, context menus, and toolbars follow Explorer Dirs' live semantic theme resources.

**Architecture:** Add implicit WPF styles in `App.xaml` for menu, context-menu, toolbar, and toolbar-tray surfaces. Reuse existing dynamic semantic brushes so light/dark changes and high-contrast fallback stay owned by `ApplicationThemeController`; native ExplorerBrowser views remain outside these WPF styles.

**Tech Stack:** .NET 10 WPF, XAML `DynamicResource`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [Theme and icons design](../specs/2026-10-26-theme-and-icons-design.md).

## Constraints and Review Focus

- Use only existing `ExplorerDirs.ControlBackgroundBrush`, `ControlTextBrush`, `BorderBrush`, and `WindowBackgroundBrush` roles.
- Do not replace WPF templates, alter menu commands, focus stops, or keyboard behavior.
- Do not apply WPF colors to ExplorerBrowser, Shell context menus, or native file/folder icons.
- Keep system high-contrast behavior through the existing dynamic semantic resource fallback.
- State and toolbar controls retain existing native command routing.

## Task 1: Theme app-owned menu and toolbar surfaces

**Files:**
- Modify: `src/ExplorerDirs.App/App.xaml`
- Test: `src/ExplorerDirs.SettingsSmoke/PaneThemeMarkupSmoke.cs`
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [x] Add markup smoke checks for semantic dynamic resources on Menu, MenuItem, ContextMenu, ToolBar, and ToolBarTray.
- [x] Confirm the focused smoke fails before adding these styles.
- [x] Add color-only implicit styles without replacing control templates.
- [x] Run focused theme smoke, complete Release `SettingsSmoke`, App Release build, and `git diff --check`.
- [x] Record that theme appearance still needs visible Light/Dark/high-contrast acceptance and Shell-owned views are unchanged.
- [x] Commit the batch.
