# Navigation Tree Width Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users resize per-pane and shared navigation trees and retain those widths in portable profile settings.

**Architecture:** Add a small versioned `NavigationTreeWidths.ini` store for one shared width and four pane widths. Place a WPF GridSplitter between each tree and its file view, and save only after a drag completes.

**Tech Stack:** .NET 10 WPF, C#, SettingsSmoke.

**Spec:** [可调整目录树宽度设计](../specs/2026-10-25-navigation-tree-width-design.md)

## Global Constraints

- Default widths: per-pane 160 DIP, shared tree 190 DIP.
- Valid persisted widths: 96–600 DIP.
- Q-Dir `TreeNsplit` remains unsupported until its semantics are verified.
- Do not launch a GUI or ExplorerBrowser-hosted probe.
- Preserve the existing dirty worktree; do not commit or publish.

## Review Focus

- Damaged or duplicate INI fields must not silently overwrite user data — test malformed-file backup and read-only protection.
- A newer format must be left intact — test future-version protection.
- Width changes from opening/hiding/resizing should not trigger persistence — keep saving on GridSplitter drag completion only.
- Each pane and shared tree must restore its own width — test store round-trip with distinct values.
- Restored widths must not exceed the supported range — reject invalid persisted values and preserve defaults for missing entries.

---

### Task 1: Width settings store

**Files:** Create `src/ExplorerDirs.App/NavigationTreeWidthsStore.cs`; modify SettingsSmoke project links and runner; create `src/ExplorerDirs.SettingsSmoke/NavigationTreeWidthsSmoke.cs`.

**Interfaces:** `NavigationTreeWidths` carries `Shared` and `Panes` (four widths). `NavigationTreeWidthsStore.Load()` returns defaults or validated state; `Save(NavigationTreeWidths)` writes through `PortableIniWriter.WriteIfUnchanged`.

- [x] Write smoke coverage for defaults, five-width round-trip, invalid width fallback/backup, and future-format non-overwrite.
- [x] Run `dotnet run --project src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj -c Release -- --tree-width-smoke`; verify it fails because the store is missing.
- [x] Implement the versioned store with defaults 190/160×4 and range 96–600 DIP.
- [x] Run the focused smoke and verify all cases pass.

### Task 2: Resizable pane and shared trees

**Files:** Modify `src/ExplorerDirs.App/PaneControl.xaml`, `PaneControl.xaml.cs`, `MainWindow.xaml`, and `MainWindow.xaml.cs`.

**Interfaces:** `PaneControl.TreeWidth` gets/sets the current pane tree column width; `TreeWidthChanged` fires only on GridSplitter `DragCompleted`. `MainWindow` loads store values once and saves an updated immutable width snapshot after each completed drag.

- [x] Add XAML splitters and bounded tree columns; place file views in the next grid column.
- [x] Apply loaded per-pane/shared widths before enabling tree mode; hiding a tree must retain its width.
- [x] Persist only completed user drags; validate values through the store before writing.
- [x] Run focused tree-width smoke, full Release SettingsSmoke, App Release build, and `git diff --check`.
- [x] Update `docs/roadmap.md` and `docs/shell-parity.md` with implemented behavior and GUI acceptance limits.
