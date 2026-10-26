# Quick Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-tab Q-Dir-style wildcard quick filter that uses the native ExplorerBrowser Shell view.

**Architecture:** Keep filter settings separate from Shell `ViewState` and persist them on `TabSettings` in session INI v6. Each ExplorerBrowser owns a managed COM `IFolderFilter` attached through its native `IFolderFilterSite`; the callback evaluates immutable patterns and fails open when a Shell name cannot be resolved. Each pane has an independent filter bar separate from the existing Search Folder bar.

**Tech Stack:** C# 10/.NET 10, WPF, Windows ExplorerBrowser/COM, `System.IO.Enumeration.FileSystemName`, existing SettingsSmoke and hidden NativeProbe.

**Spec:** `docs/superpowers/specs/2026-10-24-quick-filter-design.md`

## Global Constraints

- Preserve the Windows ExplorerBrowser view and native Shell operations.
- Session INI format v6 must preserve v1–v5 readability, back up the loaded older snapshot before upgrade, and refuse to overwrite newer formats.
- Filter state belongs to each tab; new tabs start empty and duplicate tabs copy the setting.
- Search Folder filtering is disabled while the virtual search view is active; the tab's filter resumes on return to a normal directory.
- COM calls and filter state changes stay on the browser's owning STA; callbacks never touch WPF controls.
- Hidden probes use isolated fixtures, never show a window, and never send physical input or touch user paths.
- No dependency install, release, commit, or system routing change.

## Review Focus

- **Wildcard boundaries:** empty semicolon parts, whitespace, case, `*`, `?`, and malformed input must not hide unrelated files. Test in `QuickFilterSmoke`.
- **Folder handling:** folders remain visible by default and are filtered only when the flag is enabled. Test parser/predicate and native folder fixture.
- **COM lifetime:** changing or clearing a filter, navigating into/out of a Search Folder, and disposing the browser must not retain stale filters or crash. Test native set/clear/reapply and disposal.
- **Session migration:** v1–v5 profiles read an empty filter and v6 round-trips configured values; invalid values trigger existing recovery and v7 is protected. Test `SessionStoreSmoke`.
- **Pane/tab isolation:** changing one tab's filter must not change another tab or pane; duplicate copies while new tab defaults empty. Test hidden two/four-browser fixture and tab-state smoke.

---

### Task 1: Wildcard filter model

**Files:**

- Create: `src/ExplorerDirs.App/ShellQuickFilter.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ShellQuickFilterSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`, `src/ExplorerDirs.SettingsSmoke/Program.cs`

**Interfaces:**

- Produce `internal sealed record QuickFilterSettings(string Pattern, bool ExcludeMatches, bool FilterFolders)` with `Empty`, `HasPattern`, `Matches(string name)`, and `ShouldShow(string name, bool isFolder)`; parse and retain normalized wildcard entries once per applied setting.
- Consume `FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)` for each parsed semicolon-separated pattern.

- [x] Write tests for semicolon alternatives, trimming/empty segments, case-insensitive matching, include mode, exclude mode, folder pass-through, filtering folders when enabled, and empty input.
- [x] Run `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release --no-restore`; confirmed expected CS0246 missing-model failure.
- [x] Implement the immutable model with patterns parsed once on construction/apply; empty effective patterns normalize to `QuickFilterSettings.Empty`.
- [x] Rerun SettingsSmoke; all wildcard assertions and the complete smoke suite passed.

### Task 2: Native filter adapter

**Files:**

- Create: `src/ExplorerDirs.App/ShellFolderFilter.cs`
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`, add `ShellFolderFilterSmoke.cs`
- Modify: `src/ExplorerDirs.HostProbe/Program.cs`, `src/ExplorerDirs.HostProbe/FolderFilterCapabilityProbe.cs`

**Interfaces:**

- Produce `ShellFolderFilter : IShellFolderFilter` with `ShouldShow(...)` and `GetEnumFlags(...)` in Windows SDK vtable order. `GetEnumFlags` currently requests both folders and non-folders so the filter can classify each entry.
- Produce host methods `SetQuickFilter(QuickFilterSettings settings)` and `ClearQuickFilterForSearch(bool searchActive)`; the native `IFolderFilterSite.SetFilter` is queried on the browser object (IID `C0A651F5-B48B-11D2-B5ED-006097C686F6`), slot 3, and the current `IShellView.Refresh` slot 8 refreshes after changes.
- `ShouldShow` reads the child name with `IShellFolder.GetDisplayNameOf` (`SHGDN_INFOLDER | SHGDN_FORPARSING`) and reads `SFGAO_FOLDER` with `IShellFolder.GetAttributesOf`; if either query is unavailable, it returns `S_OK` (fail open).

- [x] Add a hidden production-host regression for a filter that includes one file, excludes another, keeps folders by default, and filters folders when enabled; check actual Shell item identities/count, not count alone.
- [x] Run the focused native test before implementation; confirmed it fails because product `SetQuickFilter` is not implemented.
- [x] Implement COM interface signatures in SDK order (`ShouldShow`, then `GetEnumFlags`) and wire per-browser `IFolderFilterSite.SetFilter`.
- [x] Resolve the production host integration boundary: `ShellBrowserSite.GetViewFlags` returned `NOINCLUDEITEM`, which bypassed the native filter callback. The site now clears that flag only while a normal-directory quick filter is active.
- [x] Run the hidden production-host fixture; include mode, folder pass-through/filtering, exclude mode, clearing, actual Shell identities/counts, and four-pane independence passed.
- [x] Run the existing four-pane Search Folder navigation probe after the flag change; native history and directory restore passed.
- [x] Add an explicit set-filter → Search Folder → return-to-directory restore assertion; hidden production-host regression passed after enabling the filter flag before target-view creation.
- [x] Run the native fixture twice, including set→replace→clear and Search Folder detach/restore; require original items restored and foreground HWND unchanged. The guarded Release fixture passed twice after fixing selection loss during grouped filter transitions. Both runs confirmed the original rows, four-pane isolation, Search Folder return, parent hidden, foreground HWND unchanged, and no fixture-directory inventory changes. The failure trace showed a transient larger Shell selection callback between group clearing and refresh; `SetQuickFilter` now holds a selection-recovery suppression lease across that transition. Logs: `src/.state/quick-filter-final-repeat-1-20261026.log` and `src/.state/quick-filter-final-repeat-2-20261026.log`. A probe reflection call also now passes the optional search scope explicitly.

### Task 3: Session INI v6

**Files:**

- Modify: `src/ExplorerDirs.App/SessionStore.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`, create `src/ExplorerDirs.SettingsSmoke/QuickFilterSessionSmoke.cs`

**Interfaces:**

- Replace the positional `TabSettings` declaration with an equivalent record constructor `TabSettings(string path, ViewState view, QuickFilterSettings? quickFilter = null)` and a non-null `QuickFilter` property that normalizes null to `QuickFilterSettings.Empty`; existing two-argument call sites remain source-compatible.
- Persist `FilterPattern`, `FilterExcludeMatches`, and `FilterFolders` within each `[PaneN.TabM]` section.

- [x] Add v6 round-trip tests, v1–v5 empty-default tests, malformed boolean recovery tests, and v7 write-protection coverage.
- [x] Run focused SessionStore smoke; confirmed the expected missing `TabSettings.QuickFilter` failure before implementation.
- [x] Update `Render`/`ParseProfile` to format version 6 and validate optional fields; preserve an exact `.before-v6-*` snapshot before upgrading v1–v5.
- [x] Verify an older v5 client assembly cannot overwrite v6 using the retained v5 baseline binary; rejected profile, rejected write, original unchanged.
- [x] Rerun SettingsSmoke; filter round-trip, legacy defaults, malformed boolean, v7 protection, and old-profile migration assertions passed.

### Task 4: Per-pane filter bar and shortcuts

**Files:**

- Modify: `src/ExplorerDirs.App/PaneControl.xaml`, `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/KeyboardShortcuts.cs`, `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs` or add `QuickFilterUiStateSmoke.cs`

**Interfaces:**

- Add a per-pane `QuickFilterBar` with pattern input, Include/Exclude choice, `FilterFolders` checkbox, Apply and Clear/Close buttons.
- Ctrl+M opens the active pane's bar and selects its text; Enter applies; F5 while the filter input is focused applies then refreshes; Escape clears and closes.
- New tabs use `QuickFilterSettings.Empty`; duplicate and close/reopen paths preserve `TabSettings.QuickFilter`.

- [x] Add Ctrl+M shortcut resolution coverage before wiring UI handlers.
- [x] Wire the filter bar separately from Search Folder; persist through existing session snapshots, preserve settings on duplicate/reopen, and default new tabs to empty.
- [x] Route Enter to apply, F5 in the filter input to apply/refresh, Ctrl+M to open/select the per-pane editor, and Escape to clear/close.
- [x] Verify restored/new/duplicate tab filter state and restored/duplicated Shell sort, group, mode, icon size, and columns with the hidden production PaneControl probe; the probe exposed and fixed restored tabs dropping their saved filter.
- [ ] Verify keyboard focus order and physical input with later visible acceptance.

### Task 5: Integration, documentation, and verification

**Files:**

- Modify: `docs/roadmap.md`, `docs/shell-parity.md`, `docs/superpowers/specs/2026-10-15-explorer-dirs-design.md`
- Verify: `src/ExplorerDirs.App`, `src/ExplorerDirs.SettingsSmoke`, `src/ExplorerDirs.HostProbe`

- [x] Extend the hidden Shell probe across four independent ExplorerBrowser views and a Search Folder return; confirm changing one leaves the others unchanged.
- [x] Build App and HostProbe Release (SettingsSmoke compiled and passed under `dotnet run`); rerun full SettingsSmoke, quick-filter/search-navigation probes, the old-reader guard, and `git diff --check`.
- [x] Record hidden-probe evidence and remaining physical-keyboard/visible-GUI/performance acceptance boundaries in roadmap and parity docs.

## Execution Note

Implement in this order with a red/green cycle per task. Do not publish or commit; user preview/release work is a separate explicit request. The plan avoids visible GUI and supports later manual acceptance of physical Ctrl+M/Enter/Escape behavior.
