# Folder startup request idempotency Implementation Plan

> **For agentic workers:** Use this plan task-by-task in the current worktree. Keep the UI closed; run only settings and command-line pipe smoke checks.

**Goal:** Make startup retries idempotent without collapsing separate launches of the same folder.

**Architecture:** Add an opt-in request-ID handshake over the existing profile pipe. Keep legacy message parsing for old senders, negotiate/fallback for new senders, and cache a bounded set of completed IDs in the receiver process. Reuse the startup request GUID across the initial 150 ms attempt and the two-second lock-contention retry.

**Tech Stack:** .NET 10, C#, named pipes, existing `ExplorerDirs.SettingsSmoke` harness.

**Spec:** `docs/superpowers/specs/2026-10-28-folder-open-idempotency.md`

## Global Constraints

- Keep the current pipe name and single-owner lock unchanged.
- Preserve v1 activation, directory, and QDR message semantics for old senders.
- Do not deduplicate independent requests by path.
- Do not claim durable exactly-once delivery across receiver crashes.
- Do not launch or control UI windows.

## Review Focus

- A retry arriving while the first callback is still running must wait and then reuse the first result, not invoke the callback twice.
- A distinct request ID for the same path must still invoke the callback.
- Legacy client traffic and new-to-old fallback must not dispatch a request twice.
- Partial handshake/payload timeout must remain bounded and must not populate the dedupe cache.
- Cache eviction must remain bounded without evicting a normal two-second retry before it can be processed.

## Task 1: Add failing router regressions

**Files:**
- Modify: `src/ExplorerDirs.SettingsSmoke/Program.cs`
- Add: `src/ExplorerDirs.SettingsSmoke/FolderOpenRouterProtocolSmoke.cs`
- Modify: `src/ExplorerDirs.App/FolderOpenRouter.cs`

**Interfaces:**
- Tests will call `TryForwardAsync`, `TryForwardQdrAsync`, and `TryActivateAsync` with an optional stable `Guid requestId`.

- [x] Add a delayed callback test that sends the same ID twice and asserts one callback plus the same final result.
- [x] Add a distinct-ID/same-path case and assert two callbacks.
- [x] Add v2 handshake and legacy compatibility cases.
- [x] Run targeted SettingsSmoke and confirm the new behavior fails against the current router (`-3` was rejected with response `0`).

## Task 2: Implement compatible request-ID routing

**Files:**
- Modify: `src/ExplorerDirs.App/FolderOpenRouter.cs`
- Modify: `src/ExplorerDirs.App/App.xaml.cs`

- [x] Add the handshake marker and explicit unsupported-peer fallback.
- [x] Parse both handshake and existing legacy requests in the same server.
- [x] Cache completed request-ID results in a bounded receiver-local FIFO (256 entries).
- [x] Generate one startup GUID and pass it through initial forwarding and retry.
- [x] Run targeted SettingsSmoke and confirm all new regressions pass.

## Task 3: Verify and commit

**Files:**
- Review: `docs/superpowers/specs/2026-10-28-folder-open-idempotency.md`
- Review: `docs/superpowers/plans/2026-10-28-folder-open-idempotency.md`

- [x] Run full Release SettingsSmoke, App Release build, and `git diff --check`.
- [x] Review the complete diff for protocol compatibility, bounded state, and accidental UI/system changes.
- [x] Commit the coherent change and confirm the worktree is clean (`c391736`).
