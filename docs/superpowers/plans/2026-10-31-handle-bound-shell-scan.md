# Handle-Bound Shell Row Scan Plan

> **For agentic workers:** Use this plan sequentially; this task is restricted to the isolated NativeProbe and SettingsSmoke harnesses.

**Goal:** Ensure an isolated recursive row scan reports the file identity of the directory object it actually enumerated, even if its path is renamed or replaced during the scan.

**Architecture:** Add a NativeProbe scanner that opens filesystem directory handles with delete sharing, enumerates each directory through `GetFileInformationByHandleEx(FileIdExtdDirectoryInfo)`, and verifies child identities before opening them. Return identity-tagged summaries and have the row model accept a value only when the scanned object, row snapshot, and current path all match. Keep the product scanner and ExplorerBrowser unchanged.

**Tech Stack:** C#/.NET 10, Win32 directory handles, `GetFileInformationByHandleEx`, Release `SettingsSmoke`, NativeProbe.

## Global Constraints

- Do not open any GUI or ExplorerBrowser window.
- Do not lock out rename/delete: directory handles include `FILE_SHARE_DELETE`.
- Reparse points remain non-recursive and are reported as unreadable, matching the existing prototype policy.
- A path whose identity cannot be verified must not receive a derived size.
- Do not connect this scanner to product ExplorerBrowser or the product's F9 scanner.

## Review Focus

- Root renamed/replaced after opening: result stays bound to the original handle and row publication rejects it for the replacement path.
- Child replaced after parent enumeration: child ID mismatch is skipped, never attributed to the new object.
- Reparse points and missing IDs: no traversal and no trusted result.
- Cancellation and native buffer bounds: handles and buffers are released on every exit.

### Task 1: Add the regression

**Files:**
- Modify: `src/ExplorerDirs.SettingsSmoke/ShellListViewRowModelSmoke.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`

- [x] Add a real temporary-directory test that replaces the root after the scanner has opened it; assert the summary uses the original handle identity and row application rejects it for the replacement path.
- [x] Add a real child replacement test after parent enumeration; confirm the opened child identity mismatch is skipped.
- [x] The first focused run occurred after the initial scanner scaffold was present; it exposed compile-time marshalling overload and namespace errors. Both regressions pass after those errors were fixed.

### Task 2: Implement and wire the isolated scanner

**Files:**
- Create: `src/ExplorerDirs.NativeProbe/HandleBoundRecursiveDirectoryScanner.cs`
- Modify: `src/ExplorerDirs.NativeProbe/ShellListViewRowModel.cs`
- Modify: `src/ExplorerDirs.NativeProbe/CommonListViewColorBaseline.cs`
- Modify: `src/ExplorerDirs.NativeProbe/ShellChangeReconciliationProbe.cs`
- Modify: `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`
- Modify: `src/ExplorerDirs.SettingsSmoke/ShellListViewRowModelSmoke.cs`

- [x] Enumerate entries from opened directory handles, match opened child IDs to the parent enumeration IDs, and return summaries tagged with the root handle identity.
- [x] Change the isolated row model to require scanned, captured-row, and current-path identities to match.
- [x] Update isolated probe call sites and smoke fixtures to pass identity-tagged summaries.
- [x] Run the focused smoke and confirm it passes.

### Task 3: Verify and record

- [x] Run the complete Release `SettingsSmoke` suite.
- [x] Build App and NativeProbe Release with 0 warnings/errors.
- [x] Run `git diff --check`, review the final diff, update the folder-size investigation and roadmap with limits, and commit the batch.
