# Tab Bar Shell Folder Drop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dragging one Shell folder onto blank tab-strip space opens it in a new selected tab.

**Architecture:** WPF keeps its existing private tab reorder path. The blank strip checks native Shell data formats during drag-over, then resolves exactly one item through `SHCreateShellItemArrayFromDataObject` during Drop and stores the confirmed canonical parsing name through the existing tab navigation path.

**Tech Stack:** .NET 10 WPF, Windows Shell COM, existing SettingsSmoke executable.

**Spec:** `docs/superpowers/specs/2026-10-24-tabbar-shell-drop-design.md`

## Global Constraints

- Accept only one Shell item confirmed as a folder by `SFGAO_FOLDER`.
- Keep existing tab reorder behavior and never resolve providers during drag-over.
- Release all COM objects inside the Drop callback; persist only the canonical location string.
- Do not launch GUI or ExplorerBrowser probes during this no-Computer-Use period.

## Review Focus

- A file dropped on blank tab space is ignored: decision smoke rejects missing `SFGAO_FOLDER`.
- A multi-item drag does not create a tab: decision smoke requires exactly one item.
- An invalid parsing name is ignored: decision smoke rejects URI and malformed names.
- A virtual Shell folder is accepted by its parsing name: smoke covers a `::{GUID}` identity.
- Private reorder tokens are not interpreted as external Shell drops: existing token path remains first.

---

### Task 1: Validate Shell drop decision and wire blank strip

**Files:**
- Create: `src/ExplorerDirs.App/ShellTabDropTarget.cs`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml`
- Modify: `src/ExplorerDirs.App/PaneControl.xaml.cs`
- Test: `src/ExplorerDirs.SettingsSmoke/ShellTabDropSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`

**Interfaces:**
- Produces: `ShellTabDropTarget.TryAcceptLocation(uint itemCount, uint attributes, string parsingName, out string location)` and a WPF `TryGetDroppedFolder(IDataObject data, out string location)` resolver.
- Consumes: `PaneControl.OpenPathInNewTab(string)` and `ShellLocationIdentity.TryNormalize`.

- [x] Write a smoke check for folder-only, exactly-one, canonical Shell name acceptance and file/URI/multi-item rejection.
- [x] Run the focused smoke and confirm the missing production decision causes failure.
- [x] Implement the Shell resolver and blank-strip handlers; keep the private tab reorder token behavior unchanged.
- [x] Run focused smoke, full SettingsSmoke, Release build and `git diff --check`.
- [x] Update roadmap and Shell parity notes with implementation evidence and pending visible acceptance.

**Verification result:** Focused Shell tab drop smoke passes, including a real `CF_HDROP` `DataObject` against the native Shell resolver. Full Release SettingsSmoke, App Release build (0 warnings/errors), publish isolation smoke and `git diff --check` pass. Virtual `CFSTR_SHELLIDLIST` drag data and interactive WPF target hit testing remain for GUI acceptance.
