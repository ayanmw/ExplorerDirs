# Private Desktop Probe Isolation Gate Plan

> **For agentic workers:** Inline execution in the primary checkout; do not create a worktree.

**Goal:** Ensure a private-desktop Shell probe cannot exit successfully after any observed foreground or user-input change.

**Architecture:** Extract the final isolation decision into a pure policy and test all clean/contaminated outcomes without creating windows. The Private Desktop monitor records transient foreground changes and input-tick changes, then uses that policy for its exit status.

**Tech Stack:** C# / .NET 10 Windows, ExplorerDirs.NativeProbe.

**Spec:** Existing constraints in `AGENTS.md` and `docs/superpowers/specs/2026-10-25-ishellview-host-migration-gates.md`; this fixes probe evidence quality only and does not change product architecture.

## Global Constraints

- Do not use or alter the interactive desktop in this change.
- A changed foreground during a probe invalidates the run even if the original foreground returns before completion.
- A changed global last-input tick invalidates the run because user input overlapped the probe.
- Keep product ExplorerBrowser and all release files unchanged.
- Commit the completed batch locally; do not push.

## Review Focus

- Transient foreground change followed by restoration must fail the gate.
- Input tick change with stable foreground must fail the gate.
- Stable foreground and input must continue to pass.
- Probe output must distinguish transient change from final foreground equality.

---

### Task 1: Pure isolation decision and probe integration

**Files:**
- Create: `src/ExplorerDirs.NativeProbe/PrivateDesktopIsolationPolicy.cs`
- Create: `src/ExplorerDirs.NativeProbe/PrivateDesktopIsolationPolicySmoke.cs`
- Modify: `src/ExplorerDirs.NativeProbe/PrivateDesktopShellProbe.cs`
- Modify: `src/ExplorerDirs.NativeProbe/Program.cs`

- [x] Add and run a smoke covering clean, transient foreground change, and input change outcomes; first run must fail because the policy is not implemented.
- [x] Implement the pure result policy and have the monitor sample input during the probe and at completion.
- [x] Run the pure smoke and NativeProbe Release build without starting a Private Desktop or Shell view.
- [x] Review diff and commit locally.
