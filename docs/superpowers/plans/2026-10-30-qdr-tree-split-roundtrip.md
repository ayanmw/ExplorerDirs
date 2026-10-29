# Q-Dir Tree Split Round-Trip Implementation Plan

> **For agentic workers:** Use inline execution; preserve the user's no-worktree rule. Work in the existing main checkout.

**Goal:** Preserve safe, opaque Q-Dir tree split values across import, Explorer Dirs session saves, and Q-Dir export without applying unverified widths.

**Architecture:** Extend `SessionState` and its INI profile with optional raw split strings; include them in the Q-Dir base fields and extension hash. Clear them on local tree layout, visibility, or width changes.

**Tech Stack:** C#/.NET WPF, portable INI session store, existing Release SettingsSmoke.

**Spec:** `docs/superpowers/specs/2026-10-30-qdr-tree-split-roundtrip-design.md`

## Global Constraints

- No GUI or release-package changes in this batch.
- Never guess Q-Dir split units or map them to Explorer Dirs DIP widths.
- Preserve existing session version compatibility and backup-on-upgrade behavior.
- Commit the completed batch locally; do not push.

## Review Focus

- Missing/invalid split values must not make an otherwise recoverable old profile unreadable.
- External edits to native split values must invalidate a stale Q-Dir extension.
- Any local tree mode, visibility, or width edit must prevent stale source values from re-exporting.
- Existing Q-Dir extension versions must remain importable.
- Native pane numbers must remain stable across app layout mapping.

---

### Task 1: Session model and portable persistence

**Files:** `SessionStore.cs`, `PaneTreeSessionSmoke.cs`

- [x] Add nullable raw split values to `SessionState` and session format v12.
- [x] Test v12 round-trip, v11 compatibility, malformed raw metadata rejection, and backup before v11-to-v12 upgrade.
- [x] Run the session migration smoke as part of the full Release SettingsSmoke suite.

### Task 2: Q-Dir import/export integration

**Files:** `QdrImporter.cs`, `QdrExporter.cs`, `QdrImportSmoke.cs`, `QdrExportSmoke.cs`

- [x] Test importing split strings, preserving formatting, exporting them, and invalidating the extension when a split base field changes.
- [x] Implement bounded integer-text capture and Q-Dir native-key emission; include split keys in the base hash.
- [x] Preserve existing extension versions and run import/export coverage in the full Release SettingsSmoke suite.

### Task 3: Invalidate on local tree changes and verify

**Files:** `MainWindow.xaml.cs`, relevant settings smoke, `docs/qdr-import.md`, `docs/shell-parity.md`, `docs/roadmap.md`

- [x] Clear preserved values on tree-mode, per-pane visibility, or width changes; carry imported values into normal session snapshots and import backups.
- [x] Run full Release SettingsSmoke, App Release build, and `git diff --check`.
- [x] Review final diff and commit the batch locally; clean working tree verified.
