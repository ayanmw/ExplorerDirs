# Q-Dir Export of Explorer Dirs Tree Widths

## Goal

Preserve the app's five adjustable navigation-tree widths when exporting and reimporting an Explorer Dirs `.qdr` workspace.

## Decisions

- Keep Q-Dir's native `TreeNsplit` fields unsupported because their units and layout meaning remain unknown.
- Add the app's `NavigationTreeWidths` as an optional, versioned Explorer Dirs extension payload. Bump the extension format to v4; continue reading v1-v3 unchanged.
- A v4 extension must contain all five valid widths (shared tree and four panes, 96–600 DIP). A missing or invalid width invalidates the extension snapshot and falls back to recognized Q-Dir base fields.
- Export writes all five current profile widths. Import preview identifies their presence. After confirmation, import saves the widths through the existing conflict-protected `NavigationTreeWidthsStore`, then applies them to the current window.
- Pre-import `.qdr` backup includes the current widths. No new Q-Dir base fields are introduced, and extension base-hash behavior remains unchanged.

## Acceptance

- Five distinct widths round-trip through extension v4.
- v1-v3 exports remain readable and produce no imported width override.
- Invalid/missing v4 width data rejects the extension and preserves the Q-Dir base import fallback.
- App Release build and full Release SettingsSmoke pass. GUI application and Q-Dir interoperability remain separate acceptance.
