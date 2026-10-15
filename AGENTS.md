# Explorer Dirs project rules

## Product boundary

- Build a Windows file browsing window replacement with Q-Dir style multi-pane workflows. Do not replace the desktop, taskbar, login shell, or the `explorer.exe` system file.
- Use Q-Dir and other products as interaction references. Do not copy their binaries, configuration, artwork, or proprietary source into this repository.
- Keep tab, pane, layout, and file-operation behavior independently testable. Preserve existing sessions and user files when adding features.

## Source, development, and releases

- `src/` contains source code and local build/debug output. Never tell the user to run an executable from `src/` as their daily-use release.
- Development builds use `%LOCALAPPDATA%\ExplorerDirs.Dev`; published Release builds use `%LOCALAPPDATA%\ExplorerDirs`. Do not merge these profiles implicitly.
- Put each usable build in a new immutable `releases/<version>/` directory using `scripts/publish.ps1`. Never publish over a running release or make a release depend on files under `src/`.
- Keep generated release binaries out of Git. Track release instructions and build scripts. Verify the exact release executable launches before calling it usable.
- Do not change Win+E, folder associations, or registry routing until there is a tested, reversible enable/disable flow.

## Maintaining the project

- When the user changes a durable project rule, update this file in the same change. Do not add speculative restrictions as rules.
- When accepted requirements change scope or order, update `docs/roadmap.md` and the design document in the same change; distinguish implemented code from manual acceptance.
- Keep the README's current capabilities and limitations accurate. State clearly when a custom context menu lacks Windows Shell extension verbs.
- Run a fresh build and focused smoke checks before committing or publishing. Record any unverified GUI behavior as pending, not complete.
- Preserve unrelated local changes and user session data. Keep commits narrow and reviewable.
