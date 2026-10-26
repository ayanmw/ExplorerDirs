# Shell Location Sessions Design

## Goal

Allow a tab to navigate to and restore Windows Shell folders that are not ordinary filesystem directories, beginning with This PC, Recycle Bin, Libraries, and ZIP folders. Keep native ExplorerBrowser views and the existing portable INI and `.qdr` text formats.

## Confirmed Current Boundaries

- `TabSettings.Path` currently stores a filesystem path. `ExplorerBrowserHost.Navigate` normalizes it with `Path.GetFullPath` and `DirectoryNavigationGate`.
- Navigation completion currently accepts a filesystem parsing path or an in-memory `search-ms:` location. Search results retain their filesystem scope in the tab session.
- The directory tree, disk capacity, file watcher, breadcrumb builder, and Q-Dir importer/exporter assume filesystem paths in several places.
- Q-Dir `.qdr` fields are plain text. The project can store textual Shell parsing names without changing the session INI version or serializing native PIDL bytes.

## Decisions

- Store one Shell identity string in the existing `TabSettings.Path` field. Ordinary directories remain absolute filesystem paths. Virtual folders store a canonical absolute Shell parsing name returned by `SHGetNameFromIDList(..., SIGDN_DESKTOPABSOLUTEPARSING)`.
- Do not persist PIDL bytes or `search-ms:` query PIDLs. Search results continue to save their filesystem scope.
- Accept Shell locations only when the Shell resolves the name and reports `SFGAO_FOLDER`. Do not launch URI/protocol targets or arbitrary files as folder locations.
- Support any absolute Shell parsing name that the current Windows Shell resolves and confirms with `SFGAO_FOLDER`, including This PC, Recycle Bin, Libraries, ZIP locations, and installed third-party namespace folders. Persist the canonical Shell parsing name; a location whose provider is later unavailable makes only its tab unavailable and must produce a clear warning. URI/protocol targets and search-result PIDLs remain excluded.
- Preserve ExplorerBrowser's native view and travel log. Shell names navigate through `BrowseToIDList`; ordinary filesystem paths retain existing directory probing and watcher behavior.
- Virtual locations have no filesystem disk-capacity result or directory watcher. Directory-tree synchronization remains filesystem-only and clears stale location state while a virtual location is active.
- Search and file-creation commands that require an ordinary filesystem directory remain unavailable in virtual folders unless their existing native Shell command handles the action directly.

## Acceptance

- Stable known virtual locations serialize, parse, duplicate, close/reopen, and survive the existing session INI round-trip without a format-version change.
- `.qdr` import accepts supported Shell parsing names and retains ordinary path import behavior; `.qdr` export emits the stored textual name and retains extension/base-hash validation.
- Shell navigation records a virtual location only after the current ExplorerBrowser reports that folder as the settled view. Failed or unknown navigation cannot replace the last confirmed session location.
- Navigation history remains scoped to each ExplorerBrowser tab. Search state continues to return to its filesystem scope.
- Shell-only locations never inherit stale disk capacity, directory watcher, or directory-tree state.
- SettingsSmoke and Release builds pass. Native view behavior, ZIP provider variation, and visible Windows UI remain separate acceptance items when the desktop is available.

## Out of Scope

- Persistence of raw PIDL bytes, network/cloud namespace compatibility guarantees across machines, and persistence of search-result PIDLs.
- Replacing the Shell view with a managed list control.
- Computer-use or visible GUI testing in this implementation pass.
