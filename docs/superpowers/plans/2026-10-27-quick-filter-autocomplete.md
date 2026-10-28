# Quick Filter Autocomplete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Q-Dir-style quick-filter history autocomplete to the existing per-tab Shell filter editor.

**Architecture:** Keep applied filter history in a bounded, profile-level portable INI. The editable filter ComboBox opens the history with Down, applies on Enter, and removes the highlighted history entry with Delete while the dropdown is open. Existing favorites and per-tab filter settings remain separate.

**Tech Stack:** C#, WPF, portable INI writer, SettingsSmoke.

**Spec:** [Q-Dir filter research](../../competitive-research.md#2026-10-27-q-dir-文件筛选行为对照) and official [Q-Dir FAQ 84](https://www.softwareok.com/?faq=84&seite=faq-Q-DIR).

**Global Constraints:** Do not change Shell filter matching, per-tab persistence, or named filter favorites. Protect newer or externally edited INI content. Do not launch GUI or ExplorerBrowser probes in this no-Computer-Use period.

## Tasks

### Task 1: History store and regression coverage

**Files:**
- Create `src/ExplorerDirs.App/QuickFilterHistoryStore.cs`
- Create `src/ExplorerDirs.SettingsSmoke/QuickFilterHistoryStoreSmoke.cs`
- Modify `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Modify `src/ExplorerDirs.SettingsSmoke/Program.cs`

- [x] Verify the new smoke fails before the store exists.
- [x] Implement an MRU string history with duplicate promotion, bounded capacity, deletion, protected future formats, and external-edit conflict checks.
- [x] Run the full Release SettingsSmoke, including focused history coverage.

### Task 2: Filter editor integration

**Files:**
- Modify `src/ExplorerDirs.App/PaneControl.xaml`
- Modify `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Modify `docs/keyboard-shortcuts.md`

- [x] Inject one profile-level history store into each pane.
- [x] Replace the plain filter TextBox with an editable history ComboBox while preserving Enter/Escape behavior.
- [x] Record only applied nonempty patterns; Delete removes the selected history entry only while its dropdown is open.
- [x] Build the App Release configuration and run SettingsSmoke.

### Task 3: Review and commit

- [x] Review the focused diff and run `git diff --check`.
- [x] Commit the feature and its checks in one local commit (`2414d87`).
