# Column Value Filter Entry Implementation Plan

> **For agentic workers:** This plan is implemented inline on `main`; the user requested no worktrees.

**Goal:** Let a user open a column's value checklist from its header arrow, create or clear that tab's column-value filter, and retain native title-area sorting behavior.

**Architecture:** Cache DirectUI column-header geometry outside native mouse dispatch and map each visible header to the matching Shell property key. Clicking only the cached arrow hit zone opens an app-themed checklist; Shell property values are read from the current view, and applying a selection reuses the existing per-tab `QuickFilterSettings`/`IFolderFilter` path.

**Tech Stack:** C# 10, WPF, UI Automation, ExplorerBrowser COM, SettingsSmoke.

**Spec:** `docs/shell-parity.md` — Q-Dir column value filter gap (2026-10-28); `docs/roadmap.md` current priority 5.

## Global Constraints

- Preserve ExplorerBrowser as the product file view and native Shell operation/menu path.
- Do not call UI Automation synchronously from the native mouse-message hook.
- Do not claim visible support until real header-arrow, checklist, and live Shell filtering behavior pass visible acceptance.
- Keep column filter values tab-local and preserve existing name-pattern criteria.

## Review Focus

- Header geometry absent, stale, reordered, resized, or mismatched with Shell columns must leave native header input untouched.
- Clicking the text portion of a header must preserve native sort.
- Empty selection clears only the clicked column's criterion.
- Menu values must use Shell display formatting and preserve selected values that are currently filtered out.
- Applying or clearing a column criterion must retain the name-pattern filter and other columns' criteria.

---

### Task 1: Header-arrow hit routing

**Files:** Create `src/ExplorerDirs.App/ShellColumnHeaderArrowHitTest.cs`; modify `ExplorerBrowserHost.cs`; add the linked file and smoke case to SettingsSmoke.

- [x] Test arrow-zone inclusion, text-zone exclusion, resize-edge exclusion, and column identity mapping.
- [x] Cache current visible header SplitButton bounds and Shell property keys outside WndProc.
- [x] Consume only a hit within a valid cached arrow zone; queue the menu request after the native message returns.

### Task 2: Column value checklist

**Files:** Modify `ShellFileViewExportCapture.cs`, `ExplorerBrowserHost.cs`, `PaneControl.xaml.cs`, and `MainWindow.xaml.cs`.

- [x] Capture distinct Shell-formatted values for the clicked visible property, retaining current selections.
- [x] Show a theme-aware checkable list and an action to clear the clicked column criterion.
- [x] Apply criteria to the owning current tab while preserving name and other-column filters.

### Task 3: Verification and commit

- [x] Run App Release build, full Release SettingsSmoke, and `git diff --check`.
- [x] Review the final diff and commit the feature as one batch; defer visible acceptance to the user's next UI review.

### Task 4: Keep value enumeration responsive

**Files:** Modify `ShellFileViewExportCapture.cs`, `ExplorerBrowserHost.cs`, `PaneControl.xaml.cs`, and `MainWindow.xaml.cs`.

- [x] Read at most 64 current-view items per Dispatcher turn, retaining Shell's existing visible-item and filtered-view paths.
- [x] Stop and release the retained Shell interfaces when the menu closes, the active tab changes, or the view generation changes.
- [x] Build the App Release configuration with zero warnings and errors; defer real large-directory and slow-provider response measurements to visible acceptance.
