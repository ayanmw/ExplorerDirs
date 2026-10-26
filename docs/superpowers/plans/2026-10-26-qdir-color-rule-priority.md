# Q-Dir color rule priority implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Model Q-Dir's documented per-property color-filter continuation states in the isolated `IShellView` prototype.

**Architecture:** Keep the prototype independent of the product UI and ExplorerBrowser. Each ordered rule carries separate tri-state values for text/background color and bold/italic/underline/strikeout. A generic pure evaluator resolves one property at a time; `Continue` lets lower-priority matching rules set it, while either locked state stops evaluation. Preserve the existing name matcher and selected-item Shell drawing behavior.

**Tech Stack:** C# / .NET 10, existing `ExplorerDirs.NativeProbe` policy smoke.

**Spec:** `docs/ishellview-color-spike.md`; Q-Dir FAQ 47 publicly documents `-1` as allowing the next filter to set/change a value and `0`/`1` as preventing later filters from changing that value.

## Global Constraints

- Do not change production ExplorerBrowser or application settings.
- Do not run GUI or Private Desktop probes in this slice.
- Keep selected rows on native Shell drawing.
- Treat `0` as locked unset and `1` as locked set; later rules can only fill/update a `Continue` state.

## Review Focus

- A matching non-continuable rule must block a later match.
- A matching continuable rule must allow the later matching rule to decide.
- Non-matching rules must not affect the accumulated state.
- A final continuable state must remain unset.
- Matching rules can resolve different properties independently.
- Style properties use the same tri-state order as colors without changing current drawing behavior.
- `#BG` and `#BG-A` are treated as view-background rules, not file-name patterns.
- Active-view rules only match the active view; ordinary background rules match either view.

### Task 1: Per-property ordered rule-state evaluator

**Files:** `src/ExplorerDirs.NativeProbe/ShellViewColorPolicy.cs`, `src/ExplorerDirs.NativeProbe/ShellViewColorPolicySmoke.cs`, `docs/ishellview-color-spike.md`.

- [x] Add failing pure-smoke cases for continuation, locked unset, locked set, and non-matching rules.
- [x] Run the color-policy smoke and confirm the expected missing API/behavior failure.
- [x] Implement the background-property evaluator without connecting it to a config format or product view.
- [x] Add failing pure-smoke cases proving independent text and font-style resolution on the same item.
- [x] Run the policy smoke and confirm the new per-property rule model is missing.
- [x] Implement one generic evaluator over the documented text/background/font-style property slots; keep native drawing limited to existing background colors.
- [x] Run the focused smoke, full SettingsSmoke, NativeProbe Release build, and App Release build.
- [x] Record the public priority, per-property semantics, and remaining file-format/UI integration gaps in the spike note.
- [x] Add failing smoke cases for `#BG`/`#BG-A`, activity state and priority.
- [x] Run the focused smoke and confirm view-background matching is missing.
- [x] Implement pure view-background resolution using the same ordered property evaluator.
- [x] Run focused smoke, full SettingsSmoke, NativeProbe Release build, and App Release build; update roadmap/spike findings.
- [x] Add failing cases for the `#Hidden` alias observed in the local Q-Dir `[Filter2]` sample, implement it as the documented hidden-item marker's alias, and rerun focused/full release verification without touching `.qdr` import.
