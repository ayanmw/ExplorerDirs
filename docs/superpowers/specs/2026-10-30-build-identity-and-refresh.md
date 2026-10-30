# Build identity and refresh behavior

## Intent

Make every Explorer Dirs binary self-identifying and make same-source, same-environment, same-parameter builds reproducible. Also investigate the file-list blanking during F5 without changing Shell refresh semantics on assumption alone.

## Build identity

- Use the product version, full Git commit ID, Git committer timestamp in UTC, and a clean/dirty source marker.
- Do not stamp wall-clock build time.
- Embed behavior-affecting build properties in the binary: configuration, target framework, runtime identifier, self-contained mode, compile constants, and publish feature switches.
- Show the long version/build identity in a themed “关于 → 版本信息” window. Add the short commit ID to the normal application title while retaining existing title display preferences.
- Release artifacts must receive identity from the source tree being published. Debug builds must identify whether the source tree is dirty.

## Refresh behavior

- Keep direct `IShellView.Refresh()` as the F5 baseline.
- Compare it with `SHCNE_UPDATEDIR` on an isolated ordinary filesystem Shell view, measuring list availability/row count, refresh completion, sorting/grouping/filtering, selection/focus recovery, and notification effects.
- Do not switch product refresh paths unless the comparison shows reduced blanking and correct Shell behavior. Virtual/non-filesystem locations retain the existing direct refresh path.
- Do not freeze or cache rendered view pixels as a substitute for Shell refresh.

## Constraints

- Continue using the existing ExplorerBrowser Shell view.
- Do not open or control a user-facing application window during background work.
- Do not publish a release in this batch.
- Commit completed implementation batches on `main`; do not create a worktree.

## Open verification boundary

Binary metadata and byte-for-byte repeatability can be checked by two clean publishes with the same identity inputs. The refresh comparison can prove hidden Shell state and timing; only visible acceptance can confirm perceived flicker is improved.
