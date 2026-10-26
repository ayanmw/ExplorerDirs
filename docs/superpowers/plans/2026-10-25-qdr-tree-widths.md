# Q-Dir Export of Explorer Dirs Tree Widths Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve adjustable shared and per-pane navigation-tree widths in app-exported `.qdr` workspaces.

**Architecture:** Store five widths in the versioned Explorer Dirs extension, bumping it to v4 while retaining v1-v3 import. Import applies valid widths through `NavigationTreeWidthsStore` only after confirmation and uses its external-edit conflict guard.

**Tech Stack:** .NET 10 WPF, C#, SettingsSmoke.

**Spec:** [Q-Dir 导出的本程序目录树宽度设计](../specs/2026-10-25-qdr-tree-widths-design.md)

## Global Constraints

- Keep Q-Dir native `TreeNsplit` unsupported until its units and layout meaning are confirmed.
- Persist only widths from 96–600 DIP.
- Preserve v1-v3 extension readability and base-hash semantics.
- Do not launch GUI, ExplorerBrowser probes, or Q-Dir.
- Preserve the existing dirty worktree and do not commit or publish.

## Review Focus

- Missing, non-finite, or out-of-range v4 widths must reject the extension without applying partial widths.
- v1-v3 extensions must not reset current profile widths.
- External edits to `NavigationTreeWidths.ini` must not be overwritten during import.
- Import cancellation must not update the width store.

---

### Task 1: Extension v4 and smoke coverage

**Files:** Modify `QdrExporter.cs`, `QdrImporter.cs`, and `QdrExportSmoke.cs`.

**Interfaces:** `QdrExporter.Render(..., NavigationTreeWidths? treeWidths = null)` writes v4. `QdrImportResult.NavigationTreeWidths` is non-null only for a valid v4 extension.

- [x] Add smoke cases for five-width v4 round-trip, legacy v1-v3 imports, every width below and above the allowed range, missing v4 fallback, and width-independent base hash.
- [x] Run focused SettingsSmoke and verify the new cases fail before implementation.
- [x] Implement v4 payload validation and preserve legacy extension handling.
- [x] Run focused Q-Dir export/import smoke and full Release SettingsSmoke.

### Task 2: Import and export integration

**Files:** Modify `MainWindow.xaml.cs`, `PaneControl.xaml.cs`, `README.md`, `docs/qdr-import.md`, `docs/roadmap.md`, and `docs/shell-parity.md`.

**Interfaces:** Export and pre-import backup pass the current `NavigationTreeWidths`. After the user confirms import, save incoming widths through the conflict-protected store, update the current window's cached per-pane widths, then reapply tree mode.

- [x] Add import-preview text and pass the current widths into export and backup.
- [x] Apply imported widths after confirmation and before reapplying tree visibility/mode.
- [x] Update the roadmap and parity record, including GUI acceptance limits.
- [x] Run full Release SettingsSmoke, App Release build, and `git diff --check`.
