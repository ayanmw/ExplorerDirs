# Shell Location Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist and restore non-filesystem Shell folders confirmed by the local Shell in tabs and `.qdr` state while retaining the existing native ExplorerBrowser implementation.

**Architecture:** Store ordinary filesystem directories as they are today and supported virtual folders as canonical absolute Shell parsing names in `TabSettings.Path`. A focused Shell location codec resolves names to folder PIDLs and distinguishes filesystem folders from namespace folders; navigation, tab UI, tree/capacity/watcher, and Q-Dir text import/export use that distinction.

**Tech Stack:** C# 14, .NET 10 Windows WPF, Windows Shell COM/PInvoke, existing SettingsSmoke and NativeProbe projects.

**Spec:** `docs/superpowers/specs/2026-10-24-shell-location-sessions-design.md`

## Global Constraints

- Each file pane remains a Windows Shell ExplorerBrowser view.
- Persist per-tab path, view, sort, group, columns, filter, and Shell panes in the portable INI.
- Search result sessions continue to save the filesystem directory scope, not the `search-ms:` URI.
- Preserve old INI versions and `.qdr` extension/base-hash protections.
- Do not start ExplorerBrowser-hosted probes or GUI automation during the current no-Computer-Use period.

## Review Focus

- Any absolute Shell parsing name is accepted only after the local Shell resolves it and confirms `SFGAO_FOLDER`; unavailable names must not erase the previously readable session profile.
- A navigation completion from a stale/unknown Shell location must not overwrite the current tab identity.
- ZIP archives must be treated as namespace folders only when Windows Shell reports folder attributes; ordinary files named `.zip` must not be opened as folders.
- Virtual locations must not show capacity from the previous filesystem directory or retain a stale directory-tree root.
- Q-Dir imports with unresolved or non-folder namespace names must warn and skip only the affected tab while preserving other recognized tabs.

### Task 1: Shell location identity codec

**Files:**
- Create: `src/ExplorerDirs.App/ShellLocationIdentity.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`
- Create: `src/ExplorerDirs.SettingsSmoke/ShellLocationIdentitySmoke.cs`

**Interfaces:**
- `ShellLocationIdentity.TryNormalize(string value, out string identity, out bool isFileSystemPath)` accepts absolute filesystem paths or Shell parsing-name candidates and returns a persisted string; native Shell resolution remains the folder-validation gate.
- `ShellLocationIdentity.TryGetFolderPidl(string identity, out nint pidl)` resolves only a confirmed folder; the caller owns and frees a successful PIDL.
- `ShellLocationIdentity.TryGetPersistedIdentity(nint pidl, out string identity, out bool isFileSystemPath)` returns a filesystem path for true filesystem folders or a canonical parsing name for supported Shell folders.

- [x] **Step 1: Write failing pure-policy smoke cases** for ordinary paths, known `shell:` aliases, Shell parsing-name candidates, unsupported protocol names, and ZIP path component boundaries.
- [x] **Step 2: Run** `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release`; confirm the new interface is missing.
- [x] **Step 3: Implement** the codec with stable known-folder aliases plus native Shell parsing-name resolution and `SFGAO_FOLDER` validation. Resolve ZIP candidates only through Shell parsing and folder-attribute confirmation.
- [x] **Step 4: Run the focused smoke and full SettingsSmoke**; verify alias normalization and all pre-existing settings regressions.

### Task 2: Session and Q-Dir text interop

**Files:**
- Modify: `src/ExplorerDirs.App/SessionStore.cs`
- Modify: `src/ExplorerDirs.App/QdrImporter.cs`
- Modify: `src/ExplorerDirs.App/QdrExporter.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/QdrImportSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/QdrExportSmoke.cs`

**Interfaces:**
- Session parsing accepts only filesystem paths or identities accepted by `ShellLocationIdentity`.
- Q-Dir base fields and the application extension retain the same path identity text; no INI version bump is introduced.

- [x] **Step 1: Add failing tests** for session round-trip of supported Shell identities, Q-Dir import/export, and warning/skip behavior for unsupported virtual names.
- [x] **Step 2: Run** `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release`; confirm new expectations fail before implementation.
- [x] **Step 3: Implement** validation and import/export handling while retaining existing filesystem behavior and extension hash checks.
- [x] **Step 4: Run full SettingsSmoke** and verify old INI migrations and Q-Dir import/export round-trips.

### Task 3: ExplorerBrowser navigation and UI state

**Files:**
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Modify: `src/ExplorerDirs.App/ShellTreeHost.cs`
- Modify: `src/ExplorerDirs.App/NativeShellTree.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`
- Add focused policy tests only where logic is independent from a live ExplorerBrowser.

**Interfaces:**
- `ExplorerBrowserHost.Navigate` keeps the current filesystem gate for directories, and sends a resolved Shell folder PIDL through `BrowseToIDList` for supported namespace locations.
- Navigation completion publishes only the canonical identity of the settled current PIDL. Search completion keeps its existing separate runtime path.
- `PaneControl` displays a friendly Shell folder name and icon, passes only filesystem paths to the directory tree, and clears tree synchronization while a namespace location is active.

- [x] **Step 1: Add state tests** for filesystem versus namespace location publication and virtual breadcrumbs; existing generation gates continue to guard stale completions.
- [x] **Step 2: Run** SettingsSmoke with the new expectations.
- [x] **Step 3: Implement** native Shell-name navigation and settled-location publication; preserve history and normal directory routing.
- [x] **Step 4: Run** full SettingsSmoke plus App and NativeProbe Release builds; do not launch ExplorerBrowser probes in this pass.

### Task 4: Roadmap evidence and limits

**Files:**
- Modify: `docs/shell-parity.md`
- Modify: `docs/roadmap.md`
- Modify: `README.md` only if current user-facing behavior needs a usage note.

- [x] Record the supported virtual Shell set, automated evidence boundaries, and still-open visible Shell/ZIP compatibility checks.
- [x] Run `git diff --check` and review task-related files. Other workspace changes predate this slice and remain untouched.

## Verification

- `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release`
- `dotnet build src/ExplorerDirs.App -c Release --no-restore`
- `dotnet build src/ExplorerDirs.NativeProbe -c Release --no-restore`
- `git diff --check`

Do not claim visible virtual navigation, Q-Dir GUI compatibility, or all ZIP providers from pure-policy tests and builds.
