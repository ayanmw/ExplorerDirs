# Shell refresh stall recovery design

## Intent

Large filesystem bursts can leave the native Shell item count short of the watcher-derived expected count. The current batch waits up to twelve 500 ms checks after the burst becomes quiet, even when the observed Shell count has stopped changing for the whole interval. Shorten that stalled-view delay while preserving time for an actively progressing Shell enumeration.

## Decision

Keep the existing 12-check overall bound. For mismatches of 32 or more items, count consecutive rechecks with the same Shell item count; after four unchanged rechecks, proceed to the existing `IShellView.Refresh()` fallback. Any count progress resets the stall counter. Recent watcher activity still resets the bounded wait, and forced refreshes and one-item mismatches keep their current behavior.

Selection restoration retains the captured relative PIDLs and exact-set validation, with a separate bounded retry gate. A successful `IFolderView::GetItemPosition` for a saved PIDL confirms that target is present in the refreshed view; a failed lookup remains ambiguous between pending enumeration and a stale PIDL. Cache positive target readiness only within the current Shell `SelectionVersion`; query unresolved targets while enumeration proceeds, and reset the cache whenever that version changes. While the exact saved selection is incomplete, retry the native selection batch only when the per-PIDL readiness vector changes or `SelectionVersion` changes. If the readiness query is unavailable, retain the previous retry behavior. Keep the existing 1.5-second final attempt so stale or persistently unavailable targets cannot leave restoration pending indefinitely.

## Constraints and non-goals

- Do not move Shell COM calls off the UI thread or replace the native Shell view.
- Preserve selection/focus snapshot identity and exact-set validation, watcher event semantics, and single-item latency policy. Retry cadence may use positive per-PIDL readiness evidence; failed lookups must not be treated as proof that a PIDL is stale.
- Keep the 12-check upper bound so an enumeration that continues to progress cannot defer fallback forever.

## Verification

SettingsSmoke must prove an unchanged large mismatch reaches fallback after four stable rechecks, progress resets the stall counter, the original 12-check bound still applies, and recent watcher activity restarts the wait. It must also prove stable selection readiness skips repeated batches, positive PIDL readiness is not queried again within one selection generation, unresolved targets are queried, a Shell-selection generation change resets the cache, and the bounded final attempt remains enabled. Then run the complete Release SettingsSmoke and App/HostProbe Release builds. Isolated `IShellView` NativeProbe checks target readiness and stale-PIDL fallback; a hidden production HostProbe stress sample checks count convergence and selection/focus preservation when separately authorized. None replaces visible GUI acceptance.
