# Program Starter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a portable Q-Dir-style Program Starter with its own INI, safe Q-Dir `[Program]` import preview, current-pane placeholders, and ordinary-user process launch.

**Architecture:** Keep Quick Links unchanged. Add a versioned configuration store and pure parsing/argument-expansion components; resolve Shell selections from the active pane only when launching. The WPF UI edits and previews records, while a separate launcher builds `ProcessStartInfo` with `ArgumentList` and never invokes a command shell.

**Tech Stack:** .NET 10, WPF, current portable INI/store patterns, `ProcessStartInfo`, existing `SettingsSmoke` harness.

**Spec:** `docs/superpowers/specs/2026-10-25-program-starter-design.md`

## Global Constraints

- Store entries in profile `ProgramStarter.ini`; keep Quick Links and their `.lnk` files unchanged.
- Import only recognized `[Program]` records; preview imported and skipped rows before applying; never write the source Q-Dir file or execute imported entries during import.
- Launch as the ordinary user and do not use `cmd.exe`, PowerShell, or another command interpreter.
- Resolve `%sel_files%` and `%all_files%` from a live snapshot of the active pane at launch time; do not enumerate hidden panes or rescan in the background.
- Do not register global shortcuts, alter PATH, file associations, or elevation behavior.
- Treat malformed, future-version, externally modified, and ambiguous inputs without overwriting user data.

## Review Focus

- Q-Dir rows containing extra `=` characters or quoted fields: reject ambiguous rows with a preview reason; add parser tests.
- Unknown or incomplete placeholders: reject the launch before starting a process; add expansion tests.
- Multiple file paths containing spaces, quotes, Unicode, or trailing backslashes: preserve each path as its own argv element; add argument-builder tests.
- Missing/disconnected panes or virtual locations without file paths: report the unsupported expansion before launching; add provider tests.
- External edits or a newer INI schema: refuse stale writes and preserve original bytes; add store conflict/version tests.

## File Map

- Create `src/ExplorerDirs.App/ProgramStarterStore.cs` for versioned `ProgramStarter.ini` load/save, backup, and external-edit protection.
- Create `src/ExplorerDirs.App/ProgramStarterEntry.cs` for the stored record shape and stable ID.
- Create `src/ExplorerDirs.App/ProgramStarterArguments.cs` for placeholder parsing/expansion and argv construction.
- Create `src/ExplorerDirs.App/QdrProgramImporter.cs` for non-executing `[Program]` row classification and import preview data.
- Create `src/ExplorerDirs.App/ProgramStarterLauncher.cs` for safe `ProcessStartInfo` creation and process start dispatch.
- Modify `src/ExplorerDirs.App/KeyboardShortcuts.cs`, `src/ExplorerDirs.App/MainWindow.xaml`, and `src/ExplorerDirs.App/MainWindow.xaml.cs` for Ctrl+S, Tools menu, and the launcher window.
- Create `src/ExplorerDirs.App/ProgramStarterWindow.xaml` and `.xaml.cs` for add/edit/delete, import preview, and explicit launch.
- Modify `src/ExplorerDirs.SettingsSmoke/Program.cs` and add focused `ProgramStarter*Smoke.cs` files for store, parser, launcher preparation, and menu shortcut behavior.
- Modify `docs/shell-parity.md`, `docs/roadmap.md`, and a release note only after implementation and verification.

## Task 1: Portable Program Starter Store

**Interfaces:** `ProgramStarterEntry(Guid Id, string Name, string ExecutablePath, string Arguments, string WorkingDirectory)`; `ProgramStarterStore.Load()` returns entries plus a warning; `Save(entries)` refuses a changed source snapshot and refuses unknown future versions.

- [x] Add smoke cases for missing INI, Unicode round-trip, duplicate names with stable IDs, malformed file backup, unsupported schema version, and external-edit conflict.
- [x] Run the focused store smoke and confirm expected failures.
- [x] Implement the store following existing profile INI conventions; use an atomic write and preserve the prior file before schema migration or corrupt-file recovery.
- [x] Run focused and full `SettingsSmoke`; build the App Release configuration.

## Task 2: Placeholder Parsing and Argument Construction

**Interfaces:** `ProgramStarterArguments.Build(entry, ProgramStarterContext)` returns either a `ProgramStarterCommand` (`FileName`, `Arguments`, `WorkingDirectory`) or a user-readable validation error. `ProgramStarterContext` supplies active-pane directory, selected file paths by pane, all visible file paths by pane, and environment special folders.

- [x] Add failing tests for every documented scalar/list placeholder, pane suffixes 1–4, `%fdir%`, empty selection, unsupported virtual item, unknown token, malformed `%...%`, and paths containing Unicode/spaces/quotes/trailing slashes.
- [x] Verify an imported relative working directory resolves against the application directory while absolute paths and pane defaults retain their behavior.
- [x] Run the focused argument smoke and confirm the tests fail for missing behavior.
- [x] Implement exact-token list expansion into separate argv items; expand scalar placeholders within an argument without re-parsing the completed string.
- [x] Reject list placeholders embedded in a larger argument token and reject unknown placeholders with a clear error.
- [x] Run focused and full `SettingsSmoke`; build App Release.

## Task 3: Q-Dir `[Program]` Import Preview

**Interfaces:** `QdrProgramImporter.Parse(text)` returns `QdrProgramImportPreview` with importable entries and skipped rows carrying line number and reason. It performs no writes and creates no processes. Accept documented `Name=Path`, `Name=Path=Arguments`, and `Name=Path=Arguments=Work directory` rows.

- [x] Add fixtures for documented two-, three-, and four-field rows, optional work directory/arguments, Unicode, quoted spaces, blank/comment lines, missing fields, malformed rows, and extra separators.
- [x] Run the parser smoke and confirm expected failures.
- [x] Implement conservative parsing of only documented two- to four-field rows; skip ambiguous records rather than guessing field escaping.
- [x] Verify source bytes remain unchanged and every row is accounted for as importable or skipped with a reason.
- [x] Run focused and full `SettingsSmoke`; build App Release.

## Task 4: Launch Dispatch

**Interfaces:** `ProgramStarterLauncher.Prepare(command)` builds a `ProcessStartInfo` with `UseShellExecute=false`, `FileName`, `WorkingDirectory`, and `ArgumentList`; `Start(command)` invokes an injectable process-start delegate used by smoke tests.

- [x] Add tests for executable and working-directory validation, argument boundaries, ordinary-user startup flags, and start failure reporting.
- [x] Run launcher smoke and confirm expected failures.
- [x] Implement dispatcher without shell command strings, hidden-window tricks, or elevation.
- [x] Verify argument and working-directory handoff through an injected process-start delegate; never start a user-configured app.
- [x] Run full `SettingsSmoke` and App Release build.

## Task 5: Program Starter Window and Keyboard/Menu Entry

**Interfaces:** Ctrl+S resolves to `ExplorerShortcut.ProgramStarter`; the Tools menu opens a modeless/owned Program Starter window. The window receives the active pane's context provider and store, and only constructs a live selection snapshot when the user invokes Launch.

- [x] Add shortcut routing smoke, including text-entry focus protection; Ctrl+S remains local and does not register a global shortcut.
- [x] Add a testable command-state mapping for empty list, selection, invalid item, import preview, and launch.
- [x] Implement add/edit/delete and import-preview-cancel/apply flows. Import apply merges accepted records into the independent INI and does not modify Q-Dir.ini.
- [x] Show the supported placeholders in the editor and show validation errors before starting a process.
- [x] Run full `SettingsSmoke` and App Release build. Visible focus, keyboard operation, Q-Dir positive import fixture, and safe helper-process execution remain GUI acceptance items.

## Task 6: Documentation and Release Isolation

- [x] Update parity/roadmap docs to distinguish the implemented Program Starter from `.lnk` Quick Links and report Q-Dir import evidence separately from app round-trip evidence.
- [x] Extend isolated-profile packaging smoke to include `ProgramStarter.ini` without copying or executing other profile content.
- [x] Run `pwsh -NoProfile -File scripts/publish-smoke.ps1`, full `SettingsSmoke`, App Release build, and `git diff --check`.
- [ ] Do not publish a release unless explicitly requested; do not claim Q-Dir GUI compatibility until a real positive `[Program]` sample has been imported and reviewed.

## Execution Gate

The user asked to continue implementation along the roadmap and selected the independent INI plus Q-Dir import scope. Implementation is authorized within this plan. The repository's `[Program]` section has no positive launch-item sample, so ambiguous Q-Dir rows will be skipped and visible as such; do not execute imported items during preview or import.
