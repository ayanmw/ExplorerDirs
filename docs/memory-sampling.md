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

## 2026-10-27：拆分进程占用与 .NET GC 堆

同一 temp.74/PID 17608（启动时间与 DLL 哈希再次核对一致），用微软 `dotnet-counters` 10.0.750501 做一分钟 EventPipe 指标采集。实际指标时间为北京时间 11:00:42–11:01:37，12 组快照，累计型指标有 11 个五秒增量；另用现有 `perf-sample.ps1` 并行记录 16 个进程样本，以下仅比较重叠的 12 个样本。

| 指标 | 本次结果 | 解释边界 |
| --- | --- | --- |
| 最近一次 GC 报告的堆大小，含碎片 | 10.92–14.50 MiB | 不是两次 GC 之间的实时堆大小，也不是全部对象的实时存活量 |
| 最近一次 GC 报告的碎片 | 1.05–4.89 MiB | 上一行已包含此部分，不能再加一次 |
| GC 提交量 | 各快照均为 29.95 MiB（31,399,936 字节） | 仅为 GC 使用的提交内存，包含供后续分配使用的余量 |
| 同期进程 Private Bytes | 165.48→165.42 MiB，范围 155.08–166.93 MiB | 与 GC 指标取样并非同一时刻，不能用差值精确归属组件 |
| 同期进程 Working Set | 280.23→281.16 MiB，范围 270.89–282.52 MiB | 包含驻留共享页，不能与提交量混为一谈 |
| 托管分配增量 | 55 秒约 70.42 MiB，平均 1.28 MiB/s | 分配流量，不是泄漏量或保留量 |
| GC 次数与暂停累计 | gen0=2，gen1=2，gen2=0；暂停约 31 ms | 仅覆盖本次采集窗口 |

本样本改变下一步调查方向：GC 对象堆及其提交量明显小于进程总占用，不能把总内存直接归因于 C# 对象堆，也没有依据先通过强制 GC 或堆上限解决大部分占用。GC 以外仍包含运行时/JIT、WPF 原生组件、Shell、第三方扩展及其他缓存等；尚无各自分配栈，不能把剩余部分全部算给 ExplorerBrowser，也不能由此预测 Win32/C++ 重写能节省多少。

用户操作、标签数量和目录工作负载未固定；计数采集本身有开销，结果不是空闲基线、优化 A/B 或长期泄漏结论。没有强制 GC、堆转储、进程暂停/重启或桌面输入。下一步先固定相同操作，结合托管堆曲线与原生分配类别/栈归属再选择优化点；资源释放修复的收益需用包含修复的新构建单独验证。

### 可复现的计数采集方式

本机 SDK 10.0.400 支持 `dnx` 临时运行工具；没有增加全局工具或项目包依赖。执行前重新确认 PID 和启动时间，示例中的 PID 只对应本次记录。`--resume-runtime false` 不恢复原本处于暂停状态的运行时；采样结束会自行断开。

```powershell
dnx dotnet-counters@10.0.750501 --source https://api.nuget.org/v3/index.json -- collect `
  --process-id 17608 --duration 00:00:01:00 --refresh-interval 5 `
  --format csv --output runtime.csv --resume-runtime false --maxTimeSeries 40 `
  --counters 'System.Runtime[dotnet.gc.last_collection.memory.committed_size,dotnet.gc.last_collection.heap.size,dotnet.gc.last_collection.heap.fragmentation.size,dotnet.gc.heap.total_allocated,dotnet.gc.collections,dotnet.gc.pause.time,dotnet.process.memory.working_set,dotnet.process.cpu.time,dotnet.timer.count]'
```

工具将累计 Counter 导出为每个采样间隔的增量，CSV 列名明确带 `/ 5 sec`；不要把它再次差分。GC 的 `last_collection` 指标只有发生相应 GC 后才更新。指标含义依据：[Microsoft .NET runtime metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime)；工具用法：[dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)。

原始产物位于本工作树 `src/.state/memory-samples/managed-live-17608-01/`，不入 Git：

- `runtime.csv` SHA-256：`A48C94CBAE4523D9F221DCF8AF38A3151148D52445ED36C751897D6BE12213E5`。
- `process.json` SHA-256：`61ECB0947FF2EC2BB24C2A9D76EC163A586F7F9C258CB39A85D2A72B440AD447`。
