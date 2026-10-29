# Q-Dir Tree Split Round-Trip Design

## Intent

Preserve the original `Tree1split` through `Tree4split` values when a `.qdr` file is imported, saved in an Explorer Dirs session, and exported again. The project has no verified unit or mapping for these fields, so it must not apply them to its own tree widths.

## Goals

- Keep valid integer field text verbatim across import, session restart, and Q-Dir export.
- Include preserved fields in the Q-Dir base hash so editing them invalidates the application extension.
- Clear preserved values when the user changes tree mode, per-pane tree visibility, or a tree width.
- Keep old session profiles readable and back them up before upgrading to the next format.

## Non-goals

- Interpret or apply Q-Dir split values as Explorer Dirs DIP widths.
- Claim visible Q-Dir interoperability or tree-width compatibility.
- Preserve malformed or non-integer split fields.

## Approach

Store optional raw split strings in the portable session model. The importer accepts only bounded non-negative integer text, without canonicalizing it. The Q-Dir exporter writes those values only when present. Existing session/versioned-extension validation remains the compatibility boundary.

## Constraints and success criteria

- Existing Q-Dir exports without split metadata remain byte-semantically unchanged in their base fields.
- A source string such as `002500` returns as `002500` after session and Q-Dir round trips.
- Invalid values are not applied and do not enter preserved metadata.
- Tree layout/visibility/width changes invalidate the preserved values before the next save/export.
- Release SettingsSmoke and App Release build pass; no GUI or release package is changed.

## Implementation calls

- Store fields by their Q-Dir native pane number; do not remap them through Explorer Dirs layout order.
- Keep the values opaque and clear all four together when any relevant local tree setting changes.
