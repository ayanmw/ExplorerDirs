# Explorer Dirs memory sampling

Use the read-only sampler to collect a time series while the app is already running:

```powershell
pwsh -NoProfile -File scripts/sample-memory.ps1
```

It samples every 5 seconds for 5 minutes by default and writes CSV under `src/.state/memory-samples/`. Override the interval, duration, or destination with `-IntervalSeconds`, `-DurationSeconds`, and `-OutputPath`.

The script samples every currently running `ExplorerDirs.App` process and records UTC time, PID/start time, working set, private bytes, virtual bytes, handle count, and thread count. It does not start, focus, or interact with the app, and does not read paths, folders, or file names. If no app process exists when sampling starts, it exits with code 2 instead of launching one.

For comparisons, keep the same window/tab count and workload, then compare `private_bytes` and `working_set_bytes` over elapsed time. A rising working set alone can reflect paging/cache behavior; the CSV does not identify which allocation caused growth. Correlate a rising process-level curve with a separate profiler or a controlled feature-by-feature repro before attributing it to a specific Shell view or managed object.

This is a sampling aid, not an automatic background monitor. The current machine had no running `ExplorerDirs.App` process when this was added, so no fresh runtime measurements were recorded.
