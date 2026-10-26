# Command Toolbar Vector Icons Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task. Keep this visual pass isolated from command semantics and commit the finished batch.

**Goal:** Finish theme-aware vector icons for the remaining main toolbar and Shell file command controls.

**Architecture:** Extend `ExplorerChromeIcons` with local geometries for print, folder size, environment refresh, terminal, and the nine existing Shell file commands. Static toolbar commands use icon-only content with explicit automation names; file command toolbar buttons keep their visible text beside the icon. The dynamic file-command menu receives matching `Path` icons. All paths use the current semantic text brush and existing handlers/state gates.

**Tech Stack:** .NET 10 WPF, `Geometry`, `Path`, `ExplorerDirs.ControlTextBrush`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [2026-10-26 theme and icons design](../specs/2026-10-26-theme-and-icons-design.md)

## Global Constraints

- Preserve the `ShellFileCommand` enum, native verbs, background/selection routing, shortcut text, and enabled state.
- Keep file command labels visible; do not make toolbars icon-only.
- Preserve all existing click handlers, tooltips, Automation IDs, and focus stops.
- Use vector paths with semantic theme strokes; do not add bitmap/Q-Dir assets or hardcoded RGB colors.
- Do not change print, scan, environment refresh, terminal launch, or Shell file-operation behavior.

## Review Focus

- Each of the nine `ShellFileCommand` values maps to exactly one valid icon in the existing command order.
- File command menu and button icons stay paired with the same command and visible label.
- Command enabled/disabled state remains determined by the existing Shell query.
- Print, folder-size, environment refresh, and terminal buttons retain their exact handlers and keyboard/tooltips.
- Icons remain dynamic in light, dark, and high-contrast resource modes.

---

### Task 1: Add command geometries and label/icon helpers

**Files:**
- Modify: `src/ExplorerDirs.App/ExplorerChromeIcons.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ExplorerChromeIconSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`

- [ ] Add a failing smoke for the complete geometry set and mapping of every Shell command.
- [ ] Add command icon mappings, label-plus-icon button content, and menu icon creation.
- [ ] Run focused icon smoke and confirm every geometry parses, maps once, and uses semantic strokes.
- [ ] Commit the icon mapping/helper task with integration.

### Task 2: Apply icons to command controls

**Files:**
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ExplorerChromeIconSmoke.cs`

- [ ] Replace print, folder-size, environment refresh, and terminal glyphs with named vector icons.
- [ ] Add matching menu and text-preserving toolbar icons for all `ShellFileCommand` entries.
- [ ] Assert the unchanged event handlers, automation names, visible labels, shortcuts, and shell state paths.
- [ ] Run focused icon, full Release `SettingsSmoke`, and App Release build.
- [ ] Commit the command toolbar integration.

### Task 3: Record verification boundary

**Files:**
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [ ] Run `git diff --check` and verify no Shell command state or invocation logic changed.
- [ ] Record that command outcomes remain native and that visual/DPI/high-contrast checks need GUI acceptance.
- [ ] Commit the record with this implementation batch.
