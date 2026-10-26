# Navigation and Tab Toolbar Icons Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task. Keep the visual work isolated from Shell command behavior and commit the finished batch.

**Goal:** Replace character-only navigation and tab toolbar symbols with consistent theme-aware vector icons.

**Architecture:** Add a small shared WPF icon factory and semantic geometry set for navigation/tab commands. Reuse that factory for existing view mode icons, while keeping the eight view geometries in their dedicated set. Pane and main toolbar controls retain their current click handlers, shortcuts, tooltips, automation IDs, and enabled-state logic; controls whose glyph currently supplies an implicit accessible label receive an explicit `AutomationProperties.Name`.

**Tech Stack:** .NET 10 WPF, `Geometry`, `Path`, `ExplorerDirs.ControlTextBrush`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [2026-10-26 theme and icons design](../specs/2026-10-26-theme-and-icons-design.md)

## Global Constraints

- Preserve Shell navigation, view switching, tab commands, shortcuts, enablement, focus stops, and menu placement.
- Use local vector geometries and the existing semantic theme brush; hardcoded RGB and Q-Dir icon assets are out of scope.
- Keep Shell file/folder icons and ExplorerBrowser rendering unchanged.
- Preserve existing tooltips, automation IDs, and accessible names; add explicit names when replacing glyph-only labels.

## Review Focus

- Back/forward/up/refresh/tree and tab commands remain attached to their original handlers.
- All icon paths are valid, distinct, and fit the 16-unit grid.
- Dynamic strokes resolve through semantic colors, including high-contrast fallback.
- Replacing glyphs does not remove UI Automation names or keyboard focus stops.
- Existing eight view mode menu icons remain intact after using the shared factory.

---

### Task 1: Add a shared theme-aware icon factory and geometry set

**Files:**
- Create: `src/ExplorerDirs.App/ExplorerChromeIcons.cs`
- Modify: `src/ExplorerDirs.App/ViewStyleMenu.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ExplorerChromeIconSmoke.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ViewStyleIconSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`

- [ ] Write geometry/factory smoke coverage and run it to confirm RED before adding the factory.
- [ ] Define Back, Forward, Up, Refresh, Tree, NewTab, NewWindow, DuplicateTab, Close, Search, AddressDropDown, and History paths.
- [ ] Route existing view menu and toolbar paths through the shared factory without changing item click behavior.
- [ ] Run focused navigation icon, view style icon, and pane theme smokes.
- [ ] Commit this task as part of the navigation/tab icon batch.

### Task 2: Apply icons to pane navigation and tab toolbars

**Files:**
- Modify: `src/ExplorerDirs.App/PaneControl.xaml`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ExplorerChromeIconSmoke.cs`

- [ ] Replace pane back/forward/up/refresh/tree and path dropdown/history glyphs with shared vector content.
- [ ] Replace main toolbar new tab/window, duplicate/close tab, refresh, and search glyphs.
- [ ] Preserve each control's click handler, shortcuts, tooltip, automation ID, and focus stop; add names where glyphs were the only label.
- [ ] Run focused smoke and inspect diff for command or tab-order changes.
- [ ] Commit the control integration task.

### Task 3: Verify and record

**Files:**
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [ ] Run full Release `ExplorerDirs.SettingsSmoke` and App Release build.
- [ ] Run `git diff --check` and verify no Shell navigation or file operation code changed.
- [ ] Record that actual toolbar proportions, DPI, hover/disabled states, and high-contrast appearance still need desktop acceptance.
- [ ] Commit the verification record with this implementation batch.
