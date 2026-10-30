# Build identity and About menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Embed deterministic source/build identity in Explorer Dirs binaries, expose it in About → Version Information, and include the short commit ID in the application title.

**Architecture:** Resolve Git commit, commit time, and source cleanliness at build/publish time; emit them and normalized build properties as assembly metadata. Runtime UI reads only that metadata, so no sidecar file or profile state is required.

**Tech Stack:** .NET 10 WPF, MSBuild assembly metadata, PowerShell publish script, existing SettingsSmoke console checks.

**Spec:** `docs/superpowers/specs/2026-10-30-build-identity-and-refresh.md`

## Global Constraints

- No build wall-clock timestamp is embedded.
- Keep the existing `ExplorerBrowser` file view and all title display preferences.
- Metadata must be inside the binary and present in release output.
- No release publication or worktree creation in this batch.

## Review Focus

- Clean and dirty Git trees must be distinguished; release identity must refer to the source actually compiled.
- Missing Git metadata must produce an explicit unknown/dirty status, not a fabricated commit.
- Debug and Release builds must display their real configuration and effective framework/runtime/publish flags.
- Adding the short hash must not remove the selected folder/path from titles configured to show it.
- The About window must use live theme resources in both light and dark mode.

---

### Task 1: Build metadata generation

**Files:**
- Modify: `src/ExplorerDirs.App/ExplorerDirs.App.csproj`
- Modify: `scripts/publish.ps1`
- Test: `src/ExplorerDirs.SettingsSmoke/BuildIdentitySmoke.cs`
- Test: `scripts/publish-smoke.ps1`

**Interfaces:**
- Produces assembly metadata keys `GitCommit`, `GitCommitTimeUtc`, `SourceTreeState`, and individual effective build parameter keys.
- `AssemblyInformationalVersion` includes the source commit; no build timestamp is added.

- [x] Verify exact emitted values for both dirty (pre-commit) and clean (post-commit) source trees using focused SettingsSmoke checks.
- [x] Pass commit ID, `%cI` committer time, source state, and normalized effective build properties through the publish script.
- [x] Publish twice with identical inputs and compare every payload file hash; report any nondeterministic output rather than suppressing it.

### Task 2: About and title presentation

**Files:**
- Create: `src/ExplorerDirs.App/BuildIdentity.cs`
- Create: `src/ExplorerDirs.App/VersionInfoWindow.xaml`
- Create: `src/ExplorerDirs.App/VersionInfoWindow.xaml.cs`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml`
- Modify: `src/ExplorerDirs.App/MainWindow.xaml.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/BuildIdentitySmoke.cs`

**Interfaces:**
- `BuildIdentity.Read(Assembly)` returns a display model derived from assembly attributes.
- `BuildIdentity.ShortCommit` returns the stable short-hash suffix displayed in the title.

- [x] Check menu wiring, metadata fields, and title-mode composition in focused SettingsSmoke checks.
- [x] Add a themed top-level About menu with a Version Information command.
- [x] Add a modal, owner-centered version window using dynamic theme resources.
- [x] Append the short hash to the application title without replacing folder/path title presentation.

### Task 3: Verify and commit

- [x] Build App and SettingsSmoke Release; run the focused metadata/source wiring checks after commit.
- [x] Inspect the final diff and commit this feature batch on `main` (`8e5dde0`).
