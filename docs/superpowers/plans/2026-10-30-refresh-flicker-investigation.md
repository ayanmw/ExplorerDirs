# F5 Refresh Flicker Investigation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Determine whether directory change notification can refresh the active Shell view with less visible blanking than the current F5 `IShellView.Refresh()` path, without losing Shell behavior.

**Architecture:** Use a disposable, ordinary filesystem fixture and isolated ExplorerBrowser host to compare direct refresh with `SHCNE_UPDATEDIR`. Preserve the existing product path unless the alternative maintains view semantics and reduces blanking.

**Tech Stack:** ExplorerBrowser/IShellView, `SHChangeNotify`, existing NativeProbe/HostProbe harnesses.

**Spec:** `docs/superpowers/specs/2026-10-30-build-identity-and-refresh.md`

## Global Constraints

- Do not replace the native Shell view or freeze rendered pixels.
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

## Evidence and limits

`dotnet ExplorerDirs.NativeProbe.dll --refresh-comparison` ran three times with an invisible parent window and unchanged foreground. Each trial observed 3 rows from an initial 2:

| Route | Row count observed | Elapsed range |
| --- | ---: | ---: |
| No explicit signal | 3 | 1048–1053 ms |
| `SHCNE_UPDATEDIR` | 3 | 567–584 ms |
| `IShellView.Refresh()` | 3 | 94–117 ms |

This probes Shell enumeration and message timing only. It does not render pixels, so it cannot confirm whether the old rows remain visible or whether the refresh looks less blank. Sorting, grouping, filtering, selection/focus restoration, and cross-process notification impact were not measured. No product refresh code changed.
