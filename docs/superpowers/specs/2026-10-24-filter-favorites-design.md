# Quick Filter Presets Design

## Goal

Let users save reusable Q-Dir-style filter settings and apply them in any pane while retaining each tab's independent active filter.

## Confirmed Q-Dir behavior

The official Q-Dir FAQ says filters are independent per Explorer view and filter settings can be saved as favorites. Its “ignore directories” behavior matches the product's current default: when folder filtering is off, folder names bypass the pattern. Source: https://www.softwareok.com/?faq=38&seite=faq-Q-DIR

## Design decisions

- Keep saved filter presets global and separate from directory Favorites.ini. Active filter settings remain per tab in ExplorerDirs.ini.
- Store each preset's name, normalized wildcard pattern, include/exclude mode, and folder-filter toggle in a dedicated `QuickFilterFavorites.ini` file beside the profile.
- Use a name editable ComboBox in each pane's quick-filter bar. Choosing a saved entry loads its values into the editor but does not apply it until the user presses Filter/Enter. Saving uses the typed name and current editor fields; saving an existing name replaces that preset case-insensitively. Delete applies only to an explicitly selected preset.
- Preserve the existing portable-INI safety rules: bounded entry count, malformed-file backup/recovery, high-version write protection, and external-edit conflict detection.
- New tabs remain empty; applying a preset changes only the active tab. Saving/deleting presets updates the shared store, not other tabs' active filter state.

## Out of scope

- Import/export of filter presets through `.qdr` (Q-Dir's `.qdr` support is not established here).
- Preset folders, sorting, or cross-device synchronization.

## Acceptance

- Presets round-trip names and all filter fields; duplicate names replace one entry without changing order of other entries.
- Invalid names, malformed values, too many entries, future format versions, and external edits cannot corrupt an existing preset file.
- Selecting a preset changes only the editor until applied; applying affects the current tab and retains existing per-tab/session semantics.
- No visible GUI or physical input is used by automated tests; visual/keyboard acceptance remains a later desktop task.
