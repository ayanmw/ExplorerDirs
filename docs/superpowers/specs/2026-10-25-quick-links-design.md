# Quick Links Design

## Intent

Add the Q-Dir-style Quick Links feature as a separate concept from folder favorites and `.qdr` workspaces. `Ctrl+Q` opens the Quick Links menu. A folder shortcut navigates the active pane; a program or file shortcut opens through the Windows Shell.

## Constraints and non-goals

- Store standard `.lnk` files in `<profile>/QuickLinks` so the feature remains portable and users can manage shortcuts with Explorer.
- Keep Favorites.ini and saved workspaces independent.
- Do not run shortcut targets by parsing or invoking their command lines; use Shell execution for non-folder shortcuts.
- No GUI acceptance or release packaging in this change.

## Approaches

- **Chosen:** Standard `.lnk` files plus the Shell Link COM API. This supports Explorer interoperability and user-managed folder/program shortcuts without adding a package.
- INI entries for folder paths only: simpler but cannot represent programs and does not match Q-Dir's link-management behavior.
- A custom shortcut database: adds a new format and management surface while duplicating `.lnk` capabilities.

## Behavior

The menu can add the current folder, including a confirmed virtual Shell folder, and open the profile QuickLinks directory for manual management. It lists top-level `.lnk` files. Resolved filesystem or virtual Shell folders navigate in the active pane; other links are opened by Windows Shell. Existing Favorites and workspace behavior is unchanged.
