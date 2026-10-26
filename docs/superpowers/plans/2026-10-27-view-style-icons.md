# View Style Vector Icons Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task. Keep the icon update separate from command behavior and commit each finished batch.

**Goal:** Replace character-only view switch affordances with a consistent, theme-aware vector icon set for Explorer's eight view modes.

**Architecture:** Store eight WPF `Geometry` paths in one code-owned icon set. Use `Path` controls for dynamically populated Shell view menu items and a compact generic view icon plus chevron in both pane view buttons. Path strokes use the existing semantic control-text brush, so Light, Dark, and high-contrast modes share the current theme pipeline. Existing layout diagrams remain vector shapes and keep their current semantic brushes.

**Tech Stack:** .NET 10 WPF, `System.Windows.Media.Geometry`, `Path`, `ExplorerDirs.SettingsSmoke`.

**Spec:** [2026-10-26 theme and icons design](../specs/2026-10-26-theme-and-icons-design.md)

## Global Constraints

- Preserve all view style names, ordering, selected-state logic, keyboard shortcuts, and Shell command dispatch.
- Keep each icon local and vector based; do not copy Q-Dir icon files.
- Keep native Shell file/folder icons and ExplorerBrowser contents unchanged.
- Use semantic theme resources for icon strokes; do not hardcode RGB colors.
- Do not change focus stops or accessibility names when replacing button content.

## Review Focus

- All eight `ViewStyle.All` entries must map to a valid, distinct geometry in the same order.
- Menu item click continues applying the original `ViewStyle` instance.
- Both pane view buttons retain their existing click/context menu behavior and accessible names.
- Dynamic icon strokes resolve in normal and high-contrast palettes.
- Layout diagram geometry and selection feedback remain intact.

---

### Task 1: Define and verify the eight view geometries

**Files:**
- Create: `src/ExplorerDirs.App/ViewStyleIconSet.cs`
- Create: `src/ExplorerDirs.SettingsSmoke/ViewStyleIconSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`

- [ ] Add smoke coverage for count/order, parseable non-empty geometry, and distinct paths.
- [ ] Run the focused icon smoke and confirm it fails before the icon set exists.
- [ ] Add eight 16-unit vector paths aligned with `ViewStyle.All` and a compact toolbar aggregate.
- [ ] Re-run the focused icon smoke and confirm all paths parse and remain distinct.
- [ ] Commit this task as `feat: add vector assets for shell view modes`.

### Task 2: Use vectors in view menus and pane controls

**Files:**
- Modify: `src/ExplorerDirs.App/ViewStyleMenu.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml`
- Test: `src/ExplorerDirs.SettingsSmoke/ViewStyleIconSmoke.cs`

- [ ] Add menu `Path` icons indexed from each `ViewStyle` while preserving `Tag`, check state, and click dispatch.
- [ ] Replace the toolbar/status button character content with the same compact vector view icon and chevron.
- [ ] Assert menu and both buttons use the semantic `ExplorerDirs.ControlTextBrush` and preserve command/accessibility wiring.
- [ ] Run focused icon and existing pane theme markup smokes.
- [ ] Commit this task as `feat: show vector icons for view switching`.

### Task 3: Verify and record

**Files:**
- Modify: `docs/theme-icon-roadmap.md`
- Modify: `docs/roadmap.md`

- [ ] Run full `ExplorerDirs.SettingsSmoke` and App Release build.
- [ ] Run `git diff --check`; confirm no changes to `ViewStyle`, Shell view dispatch, file icons, or focus navigation.
- [ ] Record that visual sizing, DPI and high-contrast presentation still require authorized GUI acceptance.
- [ ] Commit the verification record with the implementation batch.
