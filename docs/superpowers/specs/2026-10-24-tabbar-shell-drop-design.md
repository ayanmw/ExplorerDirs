# Tab Bar Shell Folder Drop Design

## Goal

Allow users to drag a folder or drive onto empty space in a pane's tab strip to open that location in a new tab, matching a Q-Dir shortcut while preserving native Shell navigation.

## Confirmed Scope

- Accept any absolute Shell location that the current Windows Shell can resolve and confirms as a folder with `SFGAO_FOLDER`. This includes ordinary directories, drives, built-in virtual folders, ZIP folders, and installed third-party namespace folders.
- Convert accepted Shell items to canonical absolute Shell parsing names and pass them through the same location/session path used by `.qdr` import and session restore. Do not persist raw PIDL bytes.
- If a namespace provider later becomes unavailable, only the affected tab can fail to restore; show the existing unavailable-location warning.
- Reject files, URI/protocol targets, search-result locations, unresolved Shell objects, and objects that do not report `SFGAO_FOLDER`.
- Keep existing tab-item drag behavior for reordering application tabs. External Shell drops are accepted only on unused tab-strip space, not on a tab header.
- A successful drop creates an unpinned tab in the receiving pane, selects it, and leaves existing tabs and their state intact.
- Reject a drop containing zero or multiple Shell locations. The Q-Dir reference describes dragging a folder or drive (singular); choosing a first item from a multi-selection would be surprising and lossy.

## Current Implementation Boundary

- `TabHeaderScroll` contains the tab headers but has no Shell drop target for blank space.
- Each `TabItem` accepts only the app's private tokenized drag payload and reorders tabs.
- Existing Shell location identity and navigation paths already resolve canonical names and verify folder attributes. The new drop path should reuse them rather than create a parallel parser or navigation implementation.

## Proposed Flow

1. Register the blank tab-header strip as a drop target while preserving the private tokenized tab-reorder behavior.
2. During drag-over, check only whether the native OLE data object offers Shell item data. Do not resolve namespace items, enumerate children, or create tabs during hover.
3. During `Drop`, handle filesystem `CF_HDROP` as exactly one path and resolve it through existing Shell PIDL parsing. Handle virtual Shell PIDL data (`CFSTR_SHELLIDLIST`) with [`SHCreateShellItemArrayFromDataObject`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shcreateshellitemarrayfromdataobject). Require exactly one item in either format. A headless WPF `DataObject` probe confirmed the array API returns `DV_E_FORMATETC` for a FileDrop-only object, so the two formats must be handled separately.
4. While the drop data object is still valid, require `SFGAO_FOLDER` and capture its canonical desktop-absolute parsing name through the existing Shell location identity path. Do not retain the `IShellItemArray` or source data object after the drop callback: Microsoft documents that the array holds the source reference and IDropTarget data objects are no longer valid after Drop completes.
5. Create and select a new tab using existing default-view settings, then navigate through the existing Shell location route. If resolution or navigation fails, keep prior tabs unchanged and surface the normal location error.

## Acceptance

- An ordinary filesystem directory and drive dropped on blank tab-strip space open in a new selected tab.
- This PC, Recycle Bin, a ZIP folder, and one installed third-party namespace folder are accepted only when the current Shell resolves them as folders.
- A file, URI, malformed PIDL payload, unresolved namespace, and multi-item payload are rejected without changing tabs.
- Drag-over does not resolve Shell namespace providers; the item array is consumed only during Drop and is not retained after the callback.
- Dropping on a tab header does not navigate that tab; existing private tab drag still reorders tabs and respects pinned/unpinned boundaries.
- New tabs receive configured default view settings; existing tabs retain path, selection-independent state, view settings, and order.
- The created tab persists and restores using its canonical Shell parsing name; if its provider is removed before restore, only that tab reports unavailable.
- Headless validation can proceed independently. Visible drag-and-drop acceptance requires a later explicit idle-desktop handoff.

## Out of Scope

- Dropping files into a folder, copy/move operations, drop-on-tab navigation, URLs, search-result PIDLs, and multiple-location batch tab creation.
- A new profile setting or custom drag-and-drop framework.
- Visible GUI testing in this design pass.
