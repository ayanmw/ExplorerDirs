# Quick Filter Presets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add portable, reusable named quick-filter presets while keeping the active filter independent per tab.

**Architecture:** Add a dedicated `QuickFilterFavorites.ini` store with the project's backup, version protection and optimistic write checks. The shared store is injected into each pane; its editable preset ComboBox loads a selected preset into the editor, while Filter/Enter remains the only operation that applies it.

**Tech Stack:** C# 10/.NET 10, WPF, existing `PortableIniWriter`, existing SettingsSmoke.

**Spec:** `docs/superpowers/specs/2026-10-24-filter-favorites-design.md`

## Global Constraints

- Preserve the native ExplorerBrowser and per-tab filter behavior.
- Keep preset data separate from directory favorites and session state.
- Do not add dependencies, publish, commit, or operate a visible GUI.
- Protect malformed, newer-version, and externally modified settings files.

## Review Focus

- **Preset identity:** case-insensitive name replacement must preserve unrelated favorites and ordering. Test save/replace/delete.
- **INI values:** wildcard separators and equals signs must survive parsing; newline-injection is rejected or normalized before writing. Test round-trip and malformed input.
- **Profile safety:** missing, damaged, future-version, and concurrent changes must follow existing portable-store behavior. Test backup/write protection.
- **Pane isolation:** selecting/using a preset must change only the active tab; new tabs remain empty. Test store/model state and retain HostProbe coverage of browser isolation.
- **Editor flow:** selecting a preset loads fields without applying; Enter/Filter applies the currently edited fields. Verify mappings and WPF build; visible input remains pending.

---

### Task 1: Preset model and portable store

**Files:**

- Create `src/ExplorerDirs.App/QuickFilterFavoritesStore.cs`
- Create `src/ExplorerDirs.SettingsSmoke/QuickFilterFavoritesSmoke.cs`
- Modify `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj` and `Program.cs`

**Interfaces:**

- `internal sealed record QuickFilterFavorite(string Name, QuickFilterSettings Settings)`.
- `QuickFilterFavoritesStore(string path)` provides `Load(): IReadOnlyList<QuickFilterFavorite>` and `Save(IEnumerable<QuickFilterFavorite>)`.
- Limit the store to 200 unique names, case-insensitive replacement, and format version 1.

- [x] Write smoke assertions for round-trip, duplicate-name replacement, order, malformed-file backup, v2 protection, invalid name, max-count and external-edit rejection.
- [x] Run SettingsSmoke and confirm expected missing-type failure before implementation.
- [x] Implement the store using `PortableIniWriter.WriteIfUnchanged` and existing invalid-file backup conventions.
- [x] Rerun SettingsSmoke and confirm all store cases pass.

### Task 2: Per-pane preset controls

**Files:**

- Modify `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify `src/ExplorerDirs.App/PaneControl.xaml`, `PaneControl.xaml.cs`
- Modify `docs/roadmap.md`, `docs/shell-parity.md`

**Interfaces:**

- MainWindow creates one shared `QuickFilterFavoritesStore` next to the session profile and passes it to all panes.
- PaneControl accepts a selected preset into the editor without applying it; Save replaces by name; Delete only removes a selected preset; Apply/Enter applies current editor fields to the current tab.

- [x] Add editable preset selector, Save and Delete controls without merging with directory Favorites.ini.
- [x] Include each added control in the pane's existing forward/reverse focus-stop order.
- [x] Preserve the Ctrl+M, apply and clear flows; new and other existing tabs remain unchanged when a preset is only loaded in the editor.
- [ ] Verify the preset editor's visible interaction and complete keyboard focus traversal after desktop acceptance is available.

### Task 3: Integration and verification

- [x] Build App and HostProbe Release; run full SettingsSmoke, quick-filter and search-navigation HostProbes, and `git diff --check`.
- [x] Record the completed capability and keep visible keyboard/visual acceptance boundaries in roadmap/parity docs.

## Execution Note

Continue inline in the existing dirty checkout; no commit or release. Do not overwrite unrelated work.
