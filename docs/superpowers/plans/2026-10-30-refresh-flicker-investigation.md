# F5 Refresh Flicker Investigation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Determine whether directory change notification can refresh the active Shell view with less visible blanking than the current F5 `IShellView.Refresh()` path, without losing Shell behavior.

**Architecture:** Use a disposable, ordinary filesystem fixture and isolated ExplorerBrowser host to compare direct refresh with `SHCNE_UPDATEDIR`. Preserve the existing product path unless the alternative maintains view semantics and reduces blanking.

**Tech Stack:** ExplorerBrowser/IShellView, `SHChangeNotify`, existing NativeProbe/HostProbe harnesses.

**Spec:** `docs/superpowers/specs/2026-10-30-build-identity-and-refresh.md`

## Global Constraints

- Do not replace the native Shell view or cache/freeze rendered pixels as a substitute for Shell updates. A temporary native redraw suppression may be used while the existing view updates, provided Shell remains responsible for the final paint.
- Do not broadcast unrelated refreshes from the product unless the effect is understood and accepted.
- Keep the current path for virtual/non-filesystem Shell locations.
- Do not use the user's visible desktop for experiments.

## Review Focus

- External create/delete/rename changes and metadata updates must appear.
- Sorting, grouping, quick filters, and hidden-item settings must remain correct.
- Existing selection and focused item should recover only when the user has not changed input.
- Network/virtual Shell locations must retain direct refresh behavior.
- Global `SHChangeNotify` effects on other views must be observed before product adoption.

---

### Task 1: Record current refresh behavior

**Files:**
- Inspect: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Inspect: `src/ExplorerDirs.App/ShellChangedItemNotificationBatch.cs`
- Test: Existing isolated HostProbe/NativeProbe path, or create a narrowly scoped probe if none can measure the needed state.

- [x] Capture direct-refresh row-count timing in a hidden ExplorerBrowser host and verify the foreground remains unchanged. Existing product code separately guards selection/focus recovery; the hidden probe does not measure it.

### Task 2: Compare directory notification

**Files:**
- Modify only a disposable probe or dedicated probe source if required; do not alter the product F5 route yet.

- [x] Compare no-signal, `SHCNE_UPDATEDIR`, and `IShellView.Refresh()` on isolated filesystem fixtures across three trials; all routes eventually observed the new row.
- [x] Keep the alternative out of product F5: the hidden host measured `IShellView.Refresh()` at 94–117 ms versus 567–584 ms for `SHCNE_UPDATEDIR`, and did not establish a visual benefit. The broadcast notification is also not scoped to this one Explorer Dirs view.

### Task 3: Decide and report

- [x] Keep `IShellView.Refresh()` because the notification route showed no row-availability or visual benefit.
- [x] Do not add a filesystem-only product route; retain the existing direct Shell refresh behavior.
- [x] Commit the isolated probe and investigation evidence on `main` (`bc2d357`); visible UI acceptance remains separate.

### Task 4: Test Shell-owned redraw suppression

**Files:**
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Add: `src/ExplorerDirs.App/ShellViewRedrawLease.cs`
- Probe: `src/ExplorerDirs.NativeProbe/RefreshComparisonProbe.cs`
- Smoke: `src/ExplorerDirs.SettingsSmoke/ShellViewRedrawLeaseSmoke.cs`

- [x] Keep F5 on `IShellView.Refresh()`, but restore `IFolderView2` redraw as soon as the synchronous call returns. The previous lease lasted through asynchronous enumeration and selection recovery, potentially leaving an emptied native list unable to paint while it repopulated.
- [x] Initial implementation left redraw enabled around `SetViewModeAndIconSize`, because mode changes do not call `IShellView.Refresh()`.
- [x] After the user still observed visible mode-switch flicker, restore a redraw lease only around the synchronous mode/size setter. Release it before reading back the resulting view state; this is a visual coalescing candidate, not evidence that flicker is fixed.
- [x] Add lease-scope smoke coverage proving redraw is restored immediately after the synchronous Shell call.
- [x] Run the hidden production-host `--live-shell-refresh` probe: all four panes stayed at the expected row counts; the refreshed pane preserved 3 selected items and the focused item.
- [x] Run native `--view-style-capability`; the latest run preserved selection/sort/group state across the modes but tripped its foreground-boundary guard, so it is not a clean end-to-end pass.
- [ ] Confirm visible F5 and view-style transitions no longer show a black/blank list; hidden probes cannot establish visual flicker.

## Evidence and limits

`dotnet ExplorerDirs.NativeProbe.dll --refresh-comparison` ran three times with an invisible parent window and unchanged foreground. Each trial observed 3 rows from an initial 2:

| Route | Row count observed | Elapsed range |
| --- | ---: | ---: |
| No explicit signal | 3 | 1048–1053 ms |
| `SHCNE_UPDATEDIR` | 3 | 567–584 ms |
| `IShellView.Refresh()` | 3 | 94–117 ms |

The initial comparison probes Shell enumeration and message timing only. It does not render pixels, so it cannot confirm whether the old rows remain visible or whether the refresh looks less blank. Sorting, grouping, filtering, selection/focus restoration, and cross-process notification impact were not measured. The product restores `IFolderView2::SetRedraw` immediately after the synchronous `IShellView.Refresh()` call; async enumeration and selection recovery are allowed to paint. The latest product candidate also suppresses redraw only for the synchronous `SetViewModeAndIconSize` call. Existing hidden-host evidence predates that mode-change lease, and neither it nor the refresh comparison renders pixels. F5 and mode-switch visuals therefore remain unverified until a visible UI acceptance pass.

Latest hidden-host pass: `baseline=1037 ms`, `updatedir=544 ms`, `ishellview=48 ms`, `ishellview-redraw-suppressed=43 ms`; all routes observed three rows and the parent HWND stayed hidden. The refresh-comparison process reported unchanged foreground. This is not evidence about perceived flicker.

The earlier product-host F5 probe confirmed the four-pane counts, 3-item selection and focused `initial.txt` remained intact. In the latest run, the hidden native view-style probe reported supported modes and preserved selection/sort/group state, then exited with its foreground-boundary guard because the foreground changed; do not treat that run as a clean end-to-end pass. Neither probe renders visible pixels.
