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

- [x] Keep F5 on the existing `IShellView.Refresh()` call; do not recreate ExplorerBrowser or navigate away and back.
- [x] Initial implementation left redraw enabled around `SetViewModeAndIconSize`, because mode changes do not call `IShellView.Refresh()`.
- [x] Remove the mode-change redraw lease after the user reported obvious blanking. The view-mode setter changes the existing Shell view in place; allowing DefView to paint its transition avoids hiding the item list while still not calling `IShellView.Refresh()` or navigating.
- [x] Remove the F5 redraw lease as well. It toggled native redraw immediately around `Refresh()` without visual evidence that this improved painting; the Shell now owns paint timing for both actions.
- [x] Defer full mode-state readback to the background dispatcher after the Shell mode setter returns. Cache the accepted mode/size immediately, coalesce repeated pending readbacks, and reject retired browser/navigation generations so the prior full state is not persisted while the new capture is pending.
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

The initial comparison probes Shell enumeration and message timing only. It does not render pixels, so it cannot confirm whether the old rows remain visible or whether the refresh looks less blank. Sorting, grouping, filtering, selection/focus restoration, and cross-process notification impact were not measured. Product F5 calls `IShellView.Refresh()` directly on the current view; mode changes call `SetViewModeAndIconSize` on that same view and do not call `Refresh`. Current source leaves redraw timing to DefView. The mode setter's full state readback runs later at dispatcher background priority; the accepted mode and normalized icon size are cached first, while browser/navigation generations prevent a delayed read from updating a retired view. Neither hidden-host evidence nor the refresh comparison renders pixels. F5 and mode-switch visuals therefore remain unverified until a visible UI acceptance pass.

Latest hidden-host pass: `baseline=1037 ms`, `updatedir=544 ms`, `ishellview=48 ms`, `ishellview-redraw-suppressed=43 ms`; all routes observed three rows and the parent HWND stayed hidden. The refresh-comparison process reported unchanged foreground. This is not evidence about perceived flicker.

### 2026-10-31: Confirm the product uses Explorer's F5 refresh contract

Microsoft's folder-view guidance states that Windows Explorer calls `IShellView::Refresh` when the user presses F5, and the method contract says the view revalidates its contents and refills from underlying storage. Explorer Dirs already uses this same API on the current view, so routing F5 through another Shell command interface has no documented basis for reducing the blank interval. The next investigation should remain at the native view's redraw/presentation boundary; do not recreate the browser or navigate away and back. This contract still says nothing about retaining the previously painted frame or about input hit testing while redraw is suppressed. [Windows Explorer folder-view guidance](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview#refresh) · [IShellView::Refresh](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-refresh).

### Task 5: Bounded redraw lease experiment and rollback

- [x] Add a bounded asynchronous redraw lease and prepare a clean isolated GUI candidate. The candidate was not launched, so the experiment never established a visual benefit.
- [x] Remove the F5 redraw lease and forced whole-view invalidation after source review showed that the app was freezing painting and then explicitly repainting the full native view. The refresh completion wait remains for selection/focus recovery only.
- [ ] Visually confirm the updated F5 behavior and view-style transitions; check a slow directory, selection/focus, scrolling, and a virtual Shell location.

The latest source build validates compilation only. Neither hidden-host row-count probes nor this plan's source review measures rendered pixels; visual acceptance remains open.

The earlier product-host F5 probe confirmed the four-pane counts, 3-item selection and focused `initial.txt` remained intact. In the latest run, the hidden native view-style probe reported supported modes and preserved selection/sort/group state, then exited with its foreground-boundary guard because the foreground changed; do not treat that run as a clean end-to-end pass. Neither probe renders visible pixels.

### Task 6: Keep the last F5 frame while Shell refills the list

- [x] Reintroduce a bounded `IFolderView2::SetRedraw(FALSE)` lease for F5 only, using the existing native row-count stabilization wait and its 1.5-second fallback.
- [x] Do not force-invalidate the view or all child windows after releasing the lease; Shell's queued invalidations remain responsible for the final paint.
- [x] Leave view-mode changes on the direct native setter path; a synchronous redraw lease there ends before Shell's asynchronous layout repaint and did not solve the visible gap.
- [ ] Build and prepare a current isolated candidate, then visually compare F5 on populated/slow directories and a virtual location; confirm selection, focus, and scroll position.
- [ ] While the previous frame is held, click and keyboard-navigate rows; confirm the visible item and Shell hit target do not diverge. `IFolderView2::SetRedraw` only promises redraw on/off, not input suppression ([Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setredraw)). If a click or key targets an item different from the visible row, remove the redraw lease.

This candidate intentionally holds the last painted F5 frame while the list repopulates. The API contract does not establish that input is disabled while painting is paused; stale visuals could therefore mislead clicks or keyboard input. The 1.5-second threshold is checked by a background-priority DispatcherTimer and is not a hard wall-clock cap while the UI thread is blocked. It may reduce the blank interval, but if Shell does not invalidate its list after redraw is re-enabled it could leave a stale frame. No whole-view reconstruction or pixel snapshot is used. A failed candidate should be removed rather than expanded with broad forced redraw flags.

### Task 7: Capture the product F5 paint timeline in HostProbe

- [x] Make the existing `--live-shell-refresh` HostProbe call `RefreshPreservingLastFrame`, the same product entry used by F5, Ctrl+R and the refresh buttons.
- [x] Include the native `WM_PAINT` / `WM_ERASEBKGND` timeline in the probe result, alongside its existing row-count, selection and focus assertions.
- [x] Build `ExplorerDirs.HostProbe` Release successfully with 0 warnings and 0 errors; commit as `1177988` on `main`.
- [ ] Run the hidden host only after a current idle-desktop handoff and explicit test authorization. Project rules note that even a hidden ExplorerBrowser host can change the system foreground.
- [ ] Use the timeline to distinguish time spent inside Shell paint handling from gaps between paint messages. It cannot establish displayed pixels or stale hit-target correctness; those still require the planned visible acceptance pass.
