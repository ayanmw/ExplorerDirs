# Layout proportions implementation plan

> Execute inline with superpowers:executing-plans. User authorized continued background development; no computer-use or interactive windows.

**Goal:** Adjust Q-Dir-style pane sizes without recreating native browsers, and remember each layout's proportions.

**Architecture:** Keep the four existing pane children in PaneLayoutHost. Add dedicated splitter rows/columns and WPF GridSplitters outside their native view bounds. Store per-layout horizontal/vertical star proportions in independent LayoutProportions.ini beside the session profile, using PortableIniWriter conflict protection. Existing profiles start evenly divided.

**Tech stack:** Existing .NET/WPF, native ExplorerBrowser unchanged.

**Spec:** Existing explorer-dirs design, roadmap's fixed-layout gap, and the user's 2026-10-23 background-development authorization.

## Scope and checks

- [x] Add a failing noninteractive STA layout check for missing splitters.
- [x] Implement divider placement for six existing layouts; preserve pane identity, visibility and spans, with minimum pane dimensions and no extra Tab stop.
- [x] Implement per-layout INI read/write, defaults, invalid-data backup, future-version protection and external-edit detection. Test independent layouts and culture-invariant round trips.
- [x] Wire drag completion and top Layout menu reset; write only after an explicit proportion change, not on window resize or periodic session save.
- [x] Verify layout geometry in an unattached WPF tree; no actual mouse/keyboard input or GUI automation. Routed drag events are raised in-process on unattached controls only.
- [x] Fresh build, focused smoke, diff review, and update progress/evidence boundaries.

## Execution evidence — 2026-10-23

- Continued in the existing task checkout to preserve the accumulated unreleased work; no Git commit or release publication.
- RED: original PaneLayoutHost failed the dedicated splitter-track assertion. GREEN: all six geometries and original pane identities passed after implementation.
- Fresh review found canceled dragging retained changed star weights while persistence stayed unchanged. A target .NET 10 unattached-control DragStarted/Delta/Completed regression failed before the fix, then passed after restoring the original axis GridLengths on cancellation. The reviewer checked the correction and closed the finding.
- Minimum-size clamp and subsequent enlargement retain preferred star proportions. Retired splitter callbacks cannot publish to another layout; canceled drag does not save.
- INI coverage: independent layout round trips under fr-FR, missing file without writes, external-edit rejection, very large future-version protection, exact damaged-file backup with partial recovery, and invalid-save rejection.
- Final `dotnet build src/ExplorerDirs.App/ExplorerDirs.App.csproj -c Release --no-restore`: exit 0, 0 warnings, 0 errors.
- Final `dotnet run --project src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj -c Release`: exit 0, `Settings smoke: PASS`.
- Review scope was this feature only; earlier dirty changes and release/profile files were preserved. No native desktop acceptance was attempted.
- Temporary-preview packaging now includes `LayoutProportions.ini` in its explicit isolated-profile copy list. `publish.ps1` syntax and changed-file whitespace checks passed; packaging itself was not run.

## Deferred acceptance

Visible native-window resizing, drag feel, DPI, real focus/history preservation and restart appearance require later user acceptance. QDR import/export still does not exchange divider ratios. Do not claim GUI or QDR parity from this change.
