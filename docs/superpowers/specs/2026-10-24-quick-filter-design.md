# Quick Filter Design

## Intent

Add Q-Dir-style quick filtering to each Explorer Dirs tab while retaining the Windows Shell view. The feature must remain separate from Search Folder navigation and must not replace the native file view.

## Goals

- Each tab owns an independent quick-filter pattern, include/exclude mode, and “filter folders” choice.
- A semicolon-separated list of `*`/`?` wildcard patterns matches Shell item names using case-insensitive Windows-style matching.
- Include mode shows matching files; exclude mode hides matching files. Folders pass through unchanged by default. When “filter folders” is enabled, folder names use the same matching rule.
- Ordinary hidden items follow the current Windows Shell “Show All Files” preference while filtering is active.
- Ctrl+M opens the active pane's filter bar and selects its current text. Enter applies; F5 applies and refreshes. Escape clears the tab's filter and closes the bar.
- New tabs start without a filter. Duplicating a tab copies its filter settings. Closing/reopening a tab retains its settings through the existing session flow.
- Store filter settings in portable session INI format v6. Versions 1–5 remain readable with empty filters; saving an older version backs it up before upgrading. Older clients must refuse to overwrite v6.
- Use the native `IFolderFilterSite.SetFilter` on each ExplorerBrowser instance. Install/replace/clear filters on that browser's STA thread, refresh the Shell view after a change, and release COM references when the browser is disposed.
- Search Folder navigation remains separate. Temporarily remove the quick filter while a Search Folder is active; restore the saved tab filter when returning to a normal directory.
- If a Shell item name cannot be read, keep the item visible. Filtering must fail open for unsupported Shell namespace items.

## Non-goals

- Search content, metadata/column filters, and recursive filtering. Q-Dir private filter fields are not decoded or written; the versioned Explorer Dirs extension preserves this application's per-tab filters through its session snapshot, without requiring Q-Dir to recognize them.
- Filtering inside Search Folder results.
- Showing protected operating-system hidden items; the filter only follows the Shell's ordinary hidden-item setting.
- Replacing `ExplorerBrowserHost` with a WPF list or custom file enumeration.
- Changing file operations, sorting, grouping, or directory navigation semantics.

## Approaches

1. **Native `IFolderFilterSite` (chosen for this implementation):** the hidden ExplorerBrowser probe has confirmed `SetFilter` calls the filter and `ShouldShow` changes the Shell view's item count. This keeps the native view, icons, context menus, keyboard behavior, and view modes. It needs careful COM lifetime and UI-thread handling.
2. **Shell Search Folder:** it already exists in the app, but it conflates quick filtering with search navigation, changes the current Shell location/history, and can incur index or enumeration behavior. It is a poorer match for Q-Dir's per-view filter.
3. **Custom list view:** rejected because it would lose the Explorer Shell's native menus, operations, and interaction parity.

The native route is an implementation decision based on the probe and Q-Dir parity goal; it is not represented as a user choice.

## Constraints and design details

- The Shell view calls `IFolderFilter.ShouldShow` before displaying items. The callback uses immutable filter state and does not access WPF controls.
- Parse patterns once when applying. Split on semicolons, trim each part, discard empty parts, and compare with `FileSystemName.MatchesSimpleExpression(..., ignoreCase: true)`. Empty effective pattern clears the filter.
- For a file, include mode returns `S_OK` on any match; exclude mode returns `S_FALSE` on any match. For a folder with “filter folders” off, return `S_OK` regardless of pattern.
- `IFolderFilter::GetEnumFlags` receives `pgrfFlags` as an output. Return `SHCONTF_FOLDERS | SHCONTF_NONFOLDERS`, adding `SHCONTF_INCLUDEHIDDEN` when `SHGetSettings(SSF_SHOWALLOBJECTS)` reports that ordinary hidden items are enabled. This mirrors the global Shell preference without changing it.
- The UI lives in a distinct per-pane quick-filter bar; the existing search bar remains independent. The bar includes pattern text, Include/Exclude selection, a folder checkbox, Apply, and Clear/Close controls.
- Add `QuickFilterSettings` as a field on `TabSettings`; do not mix it into `ViewState`, because filtering is not a Shell view-mode/sort/group property.
- Session INI v6 writes filter keys only when a filter is configured. Missing keys in older/current files yield the empty default. Invalid mode or folder flag makes the profile incomplete and follows the existing backup/recovery path.
- Keep native callback/filter implementation in focused files. No third-party dependency is needed.

## Verification

- Unit-style smoke tests cover wildcard lists, include/exclude, case-insensitivity, folder pass-through, empty input, and malformed/empty patterns.
- Session tests cover v6 round-trip, v1–v5 compatibility, invalid fields, and high-version write protection.
- The hidden native probe applies a managed `IFolderFilter` to an isolated ExplorerBrowser fixture, checks file/folder inclusion and exclusion by actual Shell item count/identity, clears the filter, and checks restoration. It never displays a window or touches user files.
- Build App, NativeProbe, and SettingsSmoke; run focused filter and session tests plus the complete SettingsSmoke suite.
- Physical Ctrl+M/Enter/Escape routing, visible view updates, multi-pane keyboard focus, and long/remote folder performance remain GUI acceptance items.
