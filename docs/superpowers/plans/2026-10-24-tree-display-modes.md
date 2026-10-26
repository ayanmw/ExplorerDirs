# 2026-10-24 Tree display modes

## Scope
Add three app-owned directory tree modes: shared tree for the active pane, a separate tree per pane, and no tree. Persist the selected mode per window in the portable session INI while retaining each pane's independent tree visibility for switching back to per-pane mode. Keep existing `.qdr` numeric `tree_mode` mappings untouched until verified.

## Steps
1. Add a pure session smoke case proving v10 mode round-trip, v9 migration to per-pane mode, legacy pre-v9 handling, and future-version rejection.
2. Bump session format to v10 and preserve the exact pre-upgrade profile backup.
3. Add mode choices to View > Directory Tree and compose a shared native Shell tree beside the pane layout. Route shared-tree selection to the active pane and synchronize it on pane activation/navigation.
4. Update Q-Dir import/export app-extension handling and docs so app mode round-trips without inventing Q-Dir numeric semantics.
5. Run focused settings smoke, Release build, and diff checks. No GUI validation in this pass.

## Acceptance evidence
INI mode persistence and migration smoke pass; app compiles; diff is limited to session, tree UI wiring, related docs, and tests. Visible focus/resize behavior remains pending GUI acceptance.
