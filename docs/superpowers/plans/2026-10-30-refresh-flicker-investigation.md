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

- [ ] Capture current direct-refresh row-count timing, selection/focus recovery, and foreground/input invariants.

### Task 2: Compare directory notification

**Files:**
- Modify only a disposable probe or dedicated probe source if required; do not alter the product F5 route yet.

- [ ] Apply `SHCNE_UPDATEDIR` to an isolated filesystem fixture and record the same measures plus create/delete/rename visibility.
- [ ] Reject the alternative if it misses changes, breaks view state, or refreshes unrelated Shell windows in a way that cannot be scoped.

### Task 3: Decide and report

- [ ] Keep `IShellView.Refresh()` if the notification route lacks complete behavior or visual benefit.
- [ ] If it clearly improves behavior, implement an explicit filesystem-only route with the existing direct refresh fallback for non-filesystem locations.
- [ ] Commit investigation/implementation evidence on `main`; visible UI acceptance remains separate.
