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

These probes measure row enumeration and message timing, not rendered pixels. They are historical comparisons. Following commit `d0e8f5f`, ordinary filesystem F5 sends `SHCNE_UPDATEDIR`; search/virtual locations and paths that cannot use that notification retain `IShellView.Refresh()` on the existing view, with native redraw enabled. This product route was chosen from the stock-view row-retention evidence, despite slower row availability in an older hidden-host comparison; it has not yet been checked for visible ExplorerBrowser flicker or updates to other same-directory Shell windows. The row-count timer is used only to time selection/focus recovery. View-mode changes call `SetViewModeAndIconSize` on the existing view and do not refresh or re-navigate the folder; full state readback is deferred to Dispatcher `ContextIdle`. No hidden-host evidence establishes visible blank duration or displayed pixels. F5 and mode-switch visuals remain open for consolidated UI acceptance.

Latest hidden-host pass: `baseline=1037 ms`, `updatedir=544 ms`, `ishellview=48 ms`, `ishellview-redraw-suppressed=43 ms`; all routes observed three rows and the parent HWND stayed hidden. The refresh-comparison process reported unchanged foreground. This is not evidence about perceived flicker.

### 2026-10-31: Confirm the native fallback matches Explorer's F5 refresh contract

Microsoft's folder-view guidance states that Windows Explorer calls `IShellView::Refresh` for F5. Explorer Dirs uses it as the fallback for search/virtual locations and when it cannot obtain the current filesystem folder PIDL; ordinary filesystem folders use `SHCNE_UPDATEDIR` to preserve the current row list during reconciliation. Neither contract guarantees retention of previously painted pixels or safe input targeting while redraw is suppressed. Do not recreate the browser or navigate away and back for this issue. [Windows Explorer folder-view guidance](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview#refresh) · [IShellView::Refresh](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-refresh).

### Task 5: Initial redraw experiment and rollback (historical)

- [x] Add a bounded asynchronous redraw lease and prepare a clean isolated GUI candidate. The candidate was not launched, so the experiment never established a visual benefit.
- [x] Remove the F5 redraw lease and forced whole-view invalidation after source review showed that the app was freezing painting and then explicitly repainting the full native view. The refresh completion wait remains for selection/focus recovery only.
- [ ] Visually confirm the updated F5 behavior and view-style transitions; check a slow directory, selection/focus, scrolling, and a virtual Shell location.

The latest source build validates compilation only. Neither hidden-host row-count probes nor this plan's source review measures rendered pixels; visual acceptance remains open.

### 2026-11-01 source status

Commit `d0e8f5f` reintroduced the filesystem directory-notification route based on stock-view row retention. This supersedes the later `IShellView.Refresh()` source descriptions in historical sections above. The notification is process-wide to Shell listeners, so verify same-directory external views as part of acceptance; no product UI result is claimed.

The earlier product-host F5 probe confirmed the four-pane counts, 3-item selection and focused `initial.txt` remained intact. In the latest run, the hidden native view-style probe reported supported modes and preserved selection/sort/group state, then exited with its foreground-boundary guard because the foreground changed; do not treat that run as a clean end-to-end pass. Neither probe renders visible pixels.

Task 5 records the first experiment. Its removal was later superseded by Task 6 below; it does not describe current product behavior.

### Task 6: Bounded F5 redraw lease and filesystem directory notification

- [x] Use `SHCNE_UPDATEDIR` for ordinary filesystem F5 refreshes; retain `IShellView::Refresh` for search/virtual locations or when the current folder PIDL cannot be read.
- [x] Use a bounded `IFolderView2::SetRedraw(FALSE)` lease only when explicit F5 cannot use the filesystem `SHCNE_UPDATEDIR` path and must fall back to `IShellView::Refresh()`. A successful directory notification reconciles rows in place, so freezing that view would hold an obsolete frame unnecessarily. Keep the existing row-count stabilization wait and 1.5-second fallback for the direct-refresh path; do not force-invalidate the view or all child windows.
- [x] Leave view-mode changes on the direct native setter path; a synchronous redraw lease there ends before Shell's asynchronous layout repaint and did not solve the visible gap.
- [ ] Build and prepare a current isolated candidate, then visually compare F5 on populated/slow directories and a virtual location; confirm selection, focus, and scroll position.
- [ ] While the previous frame is held, click and keyboard-navigate rows; confirm the visible item and Shell hit target do not diverge. `IFolderView2::SetRedraw` only promises redraw on/off, not input suppression ([Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setredraw)). If a click or key targets an item different from the visible row, remove the redraw lease.

This was an intermediate candidate. It was superseded by the later direct `IShellView.Refresh()` route and removal of the product redraw lease. Its redraw/input risk analysis applies only to that withdrawn experiment; the current product does not hold a stale frame. No whole-view reconstruction or pixel snapshot is used.

### 2026-10-31: Return explicit F5 to the native Shell refresh route

Source and prior isolated timing showed ordinary filesystem `SHCNE_UPDATEDIR` (about 544 ms until the new row was observable) versus direct `IShellView.Refresh()` (about 48 ms). The timings measure row availability, not pixels. Windows Explorer's documented F5 contract is also `IShellView::Refresh`. Explicit F5 uses the current view's `Refresh()` for filesystem, search, and virtual locations; it does not broadcast a directory update to other Shell windows. The redraw lease described in this intermediate entry was removed in the subsequent change below.

The full Release SettingsSmoke and App Release build passed with 0 warnings/errors. No ExplorerBrowser or UI was launched. Visible F5 blank-frame duration and click/keyboard behavior during Shell re-enumeration remain unverified; check populated, slow and virtual locations during the next bundled GUI acceptance. The current implementation does not suppress redraw, so the earlier stale-painted-frame risk applies only to the withdrawn lease experiment.

### 2026-10-31: Keep overlapping explicit refreshes under one view redraw hold

Multiple F5 calls can start separate row-stabilization timers for the same view generation. Independent `SetRedraw(FALSE)` calls are not reference counted by Shell, so the first timer to finish could call `SetRedraw(TRUE)` while a later refresh is still repopulating rows. `ShellViewRedrawLeaseCoordinator` now shares the native lease per host navigation generation and releases it only after every overlapping refresh completion/cancellation releases its token. A separate navigation generation has an independent hold, so an obsolete view's cleanup cannot release a newer view's redraw state.

### 2026-10-31: Remove the product F5 redraw hold

The user still observed a clear blank frame during F5 and view-style changes. Source review confirmed view-style changes already use the active Shell view in place; the product F5 path additionally held `IFolderView2.SetRedraw(FALSE)` while rows stabilized. Remove that product lease and coordinator so `IShellView.Refresh()` can paint as Shell repopulates rows. Keep the row-stabilization callback only for safe selection/focus recovery. `ShellViewRedrawLease` remains in NativeProbe solely for its isolated A/B experiment. View-style handling remains on `SetViewModeAndIconSize` with deferred state capture. A source-contract smoke and App Release build verify wiring; visible pixel behavior remains unverified and must be checked in the later GUI batch.

The focused contract first failed to compile because the coordinator was missing, then passed after implementation; full Release SettingsSmoke and App Release build passed. No Shell window was created. This verifies lease lifetime only, not visible pixels, hit testing or the Shell's handling of rapid repeated F5; those remain in the bundled UI acceptance.

### Task 7: Capture the product F5 paint timeline in HostProbe

- [x] Make the existing `--live-shell-refresh` HostProbe call the current `RefreshFromUser` entry used by F5, Ctrl+R and the refresh buttons.
- [x] Include the native `WM_PAINT` / `WM_ERASEBKGND` timeline in the probe result, alongside its existing row-count, selection and focus assertions.
- [x] Build `ExplorerDirs.HostProbe` Release successfully with 0 warnings and 0 errors; commit as `1177988` on `main`.
- [x] Extend the opt-in transition timeline with keyboard, mouse-button and wheel message timestamps, recording only message type and HWND class (no key payload or pointer coordinates); App/HostProbe Release build and full `SettingsSmoke` pass with 0 warnings and 0 errors.
- [ ] Re-run the hidden host only after a current idle-desktop handoff and explicit test authorization. Project rules note that even a hidden ExplorerBrowser host can change the system foreground.
- [ ] Use the timeline to distinguish time spent inside Shell paint handling from gaps between paint messages and identify input delivered during refresh. It cannot establish displayed pixels or which row a click hit; those still require the planned visible acceptance pass.

### Current answer to the refresh question

F5 does not rebuild the whole ExplorerBrowser, pane, tab, or navigation history. Current ordinary filesystem locations send `SHCNE_UPDATEDIR | SHCNF_PATH | SHCNF_FLUSHNOWAIT`; virtual locations and paths that cannot be represented use the current Shell view's `IShellView.Refresh()`. The notification lets DefView reconcile the directory in place but is asynchronous and broadcast to Shell listeners. There is no public ExplorerBrowser/DefView contract that refreshes an arbitrary folder in place while guaranteeing the old rendered rows remain visible. Stable row-count samples are only used to time selection/focus recovery; they do not prove the notification's enumeration or painting has completed.

Changing view mode is narrower: it invokes `IFolderView2.SetViewModeAndIconSize` on the same view. It does not call F5, re-navigate, or recreate the browser; Shell still has to rearrange and repaint items. The current code leaves redraw enabled for refreshes and only briefly suppresses redraw around the synchronous view-mode setter. Further changes to the visible transition need a coordinated pixel/input acceptance pass; a hidden row-count or source test cannot establish that they improved the actual frame.
