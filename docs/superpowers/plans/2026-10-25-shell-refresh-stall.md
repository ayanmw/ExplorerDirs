# Shell refresh stall recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Trigger the existing large-burst Shell refresh fallback sooner when the observed item count stops progressing.

**Architecture:** Track the last observed Shell count and consecutive unchanged rechecks in `ShellDirectoryChangeBatch`. Preserve the existing 12-check total cap and reset stall tracking whenever a new large burst is active or the batch is rebased.

**Tech Stack:** C# / .NET 10 Windows, Release SettingsSmoke, hidden HostProbe.

**Spec:** [Shell refresh stall recovery design](../specs/2026-10-25-shell-refresh-stall-design.md).

## Global Constraints

- Keep ExplorerBrowser and the native Shell file view.
- Preserve refresh selection and focus recovery behavior.
- Do not use desktop input or show a GUI.

## Review Focus

- Stable large mismatch should refresh after four unchanged rechecks.
- Count progress should restart the stall window.
- Repeated progress must still reach fallback by the existing 12-check bound.
- Recent watcher activity must restart the wait.

### Task 1: Stall-aware large mismatch wait

**Files:** `src/ExplorerDirs.App/ShellDirectoryChangeBatch.cs`, `src/ExplorerDirs.SettingsSmoke/ShellDirectoryChangeBatchSmoke.cs`.

- [x] Add a failing smoke proving a stable large count mismatch proceeds after four unchanged rechecks.
- [x] Run the focused smoke and confirm it fails because current logic waits until the total bound.
- [x] Implement stall counting while retaining the total bound.
- [x] Run full Release SettingsSmoke.
- [x] Build App and HostProbe Release; run hidden 3,000-item product-monitor stress twice and 10,000-item stress once; verify convergence, selection, and focus.

### Task 2: Record evidence

**Files:** `docs/live-update-investigation.md`, `docs/roadmap.md`.

- [x] Record measured trace and no-trace samples, the bounded scope of the change, and remaining visible acceptance.
- [x] Run `git diff --check` on the task files.
