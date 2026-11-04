# Q-Dir 12.79 folder-size rendering investigation

2026-11-04. The user authorized local binary analysis after the namespace-attribute candidate also failed to display folder sizes. This is a focused static investigation, not recovery of Q-Dir source or a claim that a product fix is finished.

## Artifact and evidence boundary

- Input: `C:\SoftwareOK\Q-Dir\Q-Dir.exe`, version `12.79.0.0`, 1,949,288 bytes.
- SHA-256: `9adaa5994a476f06f0dc9cdf5eae9cc0986cbfc43055fb9f05341f803fb21fca`.
- REA recognized a native x64 PE. GNU objdump reported PE32+, GUI subsystem, image base `0x400000`, entry `0x50d800`, and no CLR runtime directory.
- REA header Evidence: `ev_3c2428ca8d1138d5a74ba666437de2eab5e33cca82104133ee0f274e9defc710`.
- The connected REA session has no configured Ghidra engine; Rizin execution returned an unavailable-provider error. GNU objdump works. The following function findings come from ordinary local GNU objdump inspection, not REA decompilation Evidence. No engine was installed, target launched, debugger attached, original file changed, or source recovered.
- Raw local inspection files stay under ignored `src/.state/qdir-size-analysis`. Product sources receive no Q-Dir disassembly or recovered implementation.

## Observations

### ExplorerBrowser exists in Q-Dir

At `0x405cfc`, the program prepares the CLSID at `0x5278c8` and calls imported `CoCreateInstance` at `0x405d0c`. The GUID bytes identify `71f96385-ddd6-48d3-a0c1-ae06e8b055fb`, matching `CLSID_ExplorerBrowser` in the installed Windows SDK `ShObjIdl.h`. This proves an ExplorerBrowser creation path exists, not that every layout or OS version uses it.

Strings include `ExplorerBrowserControl`, `SHELLDLL_DefView`, `DirectUIHWND` and `SysListView32`. Strings alone do not establish runtime control selection. There is no basis here for claiming a specific source language, framework, or exclusively legacy view implementation.

### The application intercepts list display-text notifications

At `0x4907ed` / `0x4907f6`, a notification handler recognizes the ANSI and Unicode `LVN_GETDISPINFO` codes (-150 / -177). The path checks `LVIF_TEXT`, examines the item/subitem and computes replacement text. It calls a helper at `0x48110c` from several branches.

That helper copies UTF-16 text into the notification's `pszText` buffer using its `cchTextMax`, then clears the `LVIF_TEXT` bit. Those field offsets agree with x64 `NMLVDISPINFO` / `LVITEMW`. This is direct evidence of application-supplied display text instead of only waiting for a Shell property query.

The notification path also uses a helper at `0x4bf440` which sends `LVM_GETCOLUMNW` (`0x105f`) with `LVCF_TEXT`, then compares the header text to application strings. This maps displayed subitems to application roles. We should use confirmed column/property identity rather than copy header-text matching, which is sensitive to localization and renaming.

### Display values are read from an application cache

The path through `0x490c8b` calls a helper at `0x4d76cc`. Within it, `0x4d7805` performs a string-keyed tree lookup through `0x4d6970`; `0x4d7815` reads a 64-bit cached field, and `0x4d7822` calls a numeric display formatter at `0x4d63ac`. The result flows back through the notification text writer.

The formatter contains numeric conversion/format-option branches and a ratio calculation. The combination supports a size/relative-size presentation interpretation. Exact option names and all cache lifetime/invalidation policies remain unproven.

### A recursive filesystem scan exists

The procedure starting at `0x4d82f4` calls imported `FindFirstFileW` at `0x4d8487`, recursively calls itself at `0x4d862e`, accumulates results, handles a reparse-point flag branch and advances with `FindNextFileW` at `0x4d8ba9`. A file branch combines high/low size words into a 64-bit value at `0x4d89e0` onward and writes a cached record.

This establishes native recursive enumeration and application cache population paths. It does not establish their thread scheduling, full cache invalidation strategy, default trigger, or whether Q-Dir uses another provider for some locations.

## Implication for Explorer Dirs

Our real-view logs now prove ordinary files request standard PKEY_Size, while directory rows do not enter our size getter or schedule a scan. Enabling an asynchronous backend cannot help a callback that is never requested. The corrected standard column identity fixed disappearing file values, but did not make folder values requested. The namespace-attribute experiment also failed visibly and recorded zero scan requests; do not ship it as a fix.

The useful design lesson is to separate calculation from presentation:

1. Reuse the existing bounded asynchronous size cache and Everything/native source selection.
2. Start or request folder calculations from confirmed visible directory identities, not exclusively from a Shell size getter.
3. Explicitly provide folder-size cell text through a supported-by-the-actual-control display path; keep unknown distinct from zero and never scan synchronously in a paint/notification callback.
4. On completion invalidate the affected row/cell only, without full view enumeration, focus movement or sorting on every result.
5. Keep size sorting tied to numeric cached values independently of the displayed text.

Before implementation, determine whether the actual hosted view exposes `SysListView32` and forwards `LVN_GETDISPINFO`. The binary also contains modern-control paths, so it would be wrong to infer that its legacy handler works on DirectUI. If our hosted view is DirectUI, validate a display mechanism in the already authorized isolated IShellView prototype; do not replace product Shell behavior with an ordinary list just to make one column appear.

## Remaining proof obligations

- Current Q-Dir runtime control selection and whether this exact handler supplies its visible folder-size text.
- Our real hosted control class and delivery of display-text notifications.
- Initial asynchronous fill, updates after external changes and F5, numeric sorting/grouping, per-pane isolation and unknown/zero behavior.
- Native context menus, copy/paste, rename, keyboard focus, drag/drop, notifications, portable session restoration and memory lifecycle after any display-host change.

No visible fix or release completion is claimed by this investigation.
