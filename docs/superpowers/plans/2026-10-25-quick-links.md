# Quick Links Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add portable Q-Dir-style Quick Links, separate from Favorites and workspaces.

**Architecture:** Store ordinary Windows `.lnk` files under the active profile. Use `IShellLinkW`/`IPersistFile` to create and read target paths; delegate non-folder launch to the Shell.

**Tech Stack:** .NET 10 WPF, Windows Shell COM, console SettingsSmoke.

**Spec:** `docs/superpowers/specs/2026-10-25-quick-links-design.md`

## Global Constraints

- Keep ExplorerBrowser as the file view and use Windows Shell for file/program links.
- Keep profile state portable and separate from Favorites.ini and workspace `.qdr` files.
- Do not use Computer Use or launch a GUI during this work.

## Review Focus

- Unicode target names: verify created `.lnk` resolves the original directory.
- Duplicate display names: verify adding twice does not overwrite an existing shortcut.
- Program target: verify it is retained for Shell execution, not navigated as a folder.
- Virtual Shell folder target: verify its PIDL resolves to a confirmed folder identity and remains navigable in the active pane.
- Existing menu behavior: verify Ctrl+Q routes to Quick Links while Favorites remains separate.

---

### Task 1: Shortcut store and smoke coverage

**Files:** create `src/ExplorerDirs.App/QuickLinkStore.cs`; create `src/ExplorerDirs.SettingsSmoke/QuickLinkStoreSmoke.cs`; modify SettingsSmoke project and `Program.cs`.

- [x] Write smoke assertions for empty storage, Unicode folder link round-trip, duplicate-name preservation, program target, and virtual Shell folder PIDL round-trip.
- [x] Run the smoke before implementation and observe a missing-source compile failure.
- [x] Implement `.lnk` load/create/target resolution using Shell Link COM; confirm PIDL folders through the shared Shell identity validator.
- [x] Run full Release SettingsSmoke.

### Task 2: Menu and documentation

**Files:** modify `MainWindow.xaml`, `MainWindow.xaml.cs`, `docs/keyboard-shortcuts.md`, `docs/shell-parity.md`, and `docs/roadmap.md`.

- [x] Add a distinct Quick Links menu and route Ctrl+Q to it.
- [x] Add current folder, open link directory, list links, navigate filesystem/virtual folder links, and Shell-open other targets.
- [x] Build the Release app and update roadmap/parity claims with the GUI acceptance boundary.
