# All View Column Headers Implementation Plan

> **For agentic workers:** Execute the steps in this plan sequentially and keep the hidden-host and visible-UI acceptance boundary explicit.

**Goal:** Ask native Explorer Shell views to show their existing column headers in every view mode.

**Architecture:** Set `FVO_CUSTOMPOSITION` on ExplorerBrowser before initialization, then clear only the two folder flags that suppress column headers in each `OnViewCreated` callback. Keep the existing ExplorerBrowser and Shell view implementation.

**Tech Stack:** C#, .NET 10 for Windows, COM interop, WPF `HwndHost`, SettingsSmoke.

**Spec:** `docs/superpowers/specs/2026-10-25-all-view-column-headers-design.md`

## Global Constraints

- Preserve unrelated Shell folder flags.
- Keep native Shell selection, sorting, context menus, and file operations.
- Do not run GUI or ExplorerBrowser host probes during the current no-Computer-Use period.
- Do not publish until hosted and visible acceptance is complete.

## Review Focus

- `FVO_CUSTOMPOSITION` is set before `Initialize` and does not replace unrelated option bits.
- The `OnViewCreated` update clears only the two header suppression bits.
- Shell failure leaves the file browser usable and is recorded as unverified behavior.

---

### Task 1: Share and test Shell folder-view header flags

**Files:**
- Create: `src/ExplorerDirs.App/ShellFolderViewOptions.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/FolderHeaderFlagsSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Modify: `src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj`
- Remove: `src/ExplorerDirs.NativeProbe/FolderHeaderFlags.cs` after both consumers use the shared app source.

- [x] Add failing smoke assertions for setting `FVO_CUSTOMPOSITION` while preserving other bits, and clearing only header suppression bits.
- [x] Run `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release --no-restore` and confirm the new assertion fails because the app helper is not present.
- [x] Implement the shared pure flag transformations and link them into both probe projects.
- [x] Rerun SettingsSmoke and verify it passes.

### Task 2: Apply the native flags at ExplorerBrowser lifecycle points

**Files:**
- Modify: `src/ExplorerDirs.App/ExplorerBrowserHost.cs`
- Modify: `docs/roadmap.md`
- Modify: `docs/shell-parity.md`

- [x] Set `FVO_CUSTOMPOSITION` before `IExplorerBrowser.Initialize`, preserving all existing option bits.
- [x] In `OnViewCreated`, query `IFolderView2`, read folder flags, clear the two header suppression bits, and verify readback without changing unrelated bits.
- [x] Build App, NativeProbe, and HostProbe Release; run full SettingsSmoke and `git diff --check`.
- [x] Record hosted and visible Shell behavior as pending; do not claim acceptance or publish.
