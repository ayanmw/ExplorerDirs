# Explorer Dirs memory sampling

Use the read-only sampler to collect a time series while the app is already running:

```powershell
pwsh -NoProfile -File scripts/sample-memory.ps1
```

It samples every 5 seconds for 5 minutes by default and writes CSV under `src/.state/memory-samples/`. Override the interval, duration, or destination with `-IntervalSeconds`, `-DurationSeconds`, and `-OutputPath`.

The script samples every currently running `ExplorerDirs.App` process and records UTC time, PID/start time, working set, private bytes, virtual bytes, handle count, and thread count. It does not start, focus, or interact with the app, and does not read paths, folders, or file names. If no app process exists when sampling starts, it exits with code 2 instead of launching one.

For comparisons, keep the same window/tab count and workload, then compare `private_bytes` and `working_set_bytes` over elapsed time. A rising working set alone can reflect paging/cache behavior; the CSV does not identify which allocation caused growth. Correlate a rising process-level curve with a separate profiler or a controlled feature-by-feature repro before attributing it to a specific Shell view or managed object.

This is a sampling aid, not an automatic background monitor. No running app was available when the script was first added; the observations below record later live samples separately.

## 2026-10-27：temp.74 运行进程的短时只读观察

- 对象：已运行的 `preview-0.6.0-temp.74`，PID `17608`；进程启动于北京时间 `10:05:37`。采样没有启动/重启应用，没有调用 GC、工作集裁剪、Shell 操作或桌面输入。
- 时间：北京时间 `10:41:55` 至 `10:44:55`，37 个样本，约每 5 秒一次；PID 与启动时间在所有样本中一致。
- 精确二进制：EXE SHA-256 `37BC65EE5234C5F1754A56BBE85FB3D104AB8C9E4AF6C4033B04274208A98701`；应用 DLL SHA-256 `8E696F29B81547A1DE98AC21101B6CB1767A477FACB1F39369AAAFA1433837D0`。

| 指标 | 开始 | 结束 | 最低 | 最高 | 起止变化 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Working Set（MiB） | 268.01 | 272.55 | 264.97 | 275.89 | +4.54 |
| Private Bytes（MiB） | 158.44 | 158.89 | 153.06 | 162.65 | +0.45 |
| 句柄数 | 1368 | 1355 | 1345 | 1370 | -13 |
| 线程数 | 45 | 44 | 41 | 45 | -1 |

Private Bytes 在样本间上下波动，本窗口内没有出现单调增长；这不是长时间无泄漏证明。采样没有固定或记录用户的窗格/标签数量和操作，不能作优化前后 A/B 对比，也不能识别托管堆、Shell、扩展或图像缓存各自占用。后台同时有开发构建，Working Set 变化也不能独立归因于应用分配。下一步仍需固定二进制及操作序列的长时曲线，再按资源类别定位。

原始 CSV 留在本工作树的 [`src/.state/memory-samples/ExplorerDirs-memory-20261027-104155.csv`](../src/.state/memory-samples/ExplorerDirs-memory-20261027-104155.csv)，按开发测量产物规则不入 Git。该记录是当时运行的 temp.74，不是当前开发分支的新构建。
