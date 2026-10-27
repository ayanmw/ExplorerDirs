# Explorer Dirs memory sampling

## 2026-10-28：temp.76 空闲短时复采

北京时间 00:24:27–00:25:22，对仍在运行的同一进程 PID 58888 采样 12 点、每 5 秒一次。启动时间仍为 UTC 2026-10-27 13:43:03；EXE SHA-256 `016E5C4FA3145051FEFD694D17F53E4C5BF320550C6236FD8E7639C93BD79F93`，DLL SHA-256 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`，ProductVersion `0.6.0-preview.temp.76+4dcafc22202a380de6954f288b9f62eb78e138fb`。未重启、强制 GC、裁剪工作集、操作窗口或触碰用户数据。

| 指标 | 首末值 | 本段范围 |
| --- | ---: | ---: |
| Working Set / MiB | 230.09 → 230.09 | 230.09–230.09 |
| Private Bytes / MiB | 115.28 → 115.28 | 115.28–115.28 |
| 进程句柄 | 1112 → 1112 | 1112–1112 |
| 线程 | 33 → 33 | 33–33 |
| GDI / USER 对象 | 191 / 113 → 191 / 113 | 均未变化 |
| 进程累计 CPU 时间 | 264.953 → 264.953 秒 | 未变化 |

该窗口内所有采样均完全相同，说明此约一分钟的空闲段未观察到增长；它不是目录浏览/文件操作负载，也不能排除短于五秒的峰值、操作后回落或长时间增长。需要在用户实际使用同一版本时记录阶段和负载，才能判断差异来源。没有采集托管堆或原生分配栈。

原始 JSON `src/.state/memory-samples/live-58888-20261028.json` 不入 Git，SHA-256 `E09290FCE5FE760EAEEBB86BD99070FEBEBE01F442002C07A0D766405C12BDB7`。

同日稍后又对同一 PID/版本被动采样 60.29 秒，13 点、间隔 5 秒。Working Set 全为 230.09 MiB，Private Bytes 全为 115.28 MiB，虚拟字节、句柄（1112）和线程（33）也无变化。原始 CSV `src/.state/memory-samples/ExplorerDirs-memory-20261028-003229.csv` 不入 Git，SHA-256 `8F70B0C2ECBCFB559D8E781EA50E55F3253CED64E8D5AEEF970F053C93EF8264`。这只是相邻短窗口的静止进程级计数；没有记录用户操作、GDI/USER、托管堆或原生分配栈，不据此推断整晚/长期无增长。

### 同一进程的托管计数与采集器后复采

在上述复采后，对相同 PID 58888 使用 `dotnet-counters` 采集 55 秒。托管累计分配量增加 20,775,592 字节（约 19.81 MiB）；发生 1 次 Gen0，Gen1/Gen2 均为 0，GC 暂停累计约 1 ms，GC 提交量报告为 28,872,704 字节（约 27.54 MiB）。各代 `last_collection.heap.size` 合计从 14.49 MiB 到 18.25 MiB；这些是该指标报告的上次回收大小，不能直接解读为存活对象或泄漏增长。进程 Working Set 在 232.00–234.50 MiB 范围内。

`dotnet-counters` 结束后立即再用进程采样器观察同一 PID 60.32 秒：Working Set 稳定在 233.31 MiB、Private Bytes 稳定在 117.55 MiB、句柄 1121、线程 34。与此前未附加运行时计数器时的稳定读数相比，之后读数高约 3.22 MiB Working Set 和 2.27 MiB Private Bytes；变化发生在计数器采集前后，但当时没有记录用户操作，也没有独立对照，不能归因于采集器或应用活动。

两份本地原始 CSV：`managed-live-58888-02.csv` SHA-256 `038F2E7202104FDF14B10685683AE5F8FE44DD04A2014CBF7123749BB5D126C3`；`ExplorerDirs-memory-20261028-003540.csv` SHA-256 `F3F7EF92CD1CC44A34013A4A2B596E6674F9953D1A49D688C62C72BE64A34276`。均位于 `src/.state/memory-samples/`，不入 Git。该被动采样没有用户操作标记，也没有托管/原生分配栈，不能定位分配来源或证明长期趋势。

## 2026-10-27：同一日常进程的后续资源观察

北京时间 22:41:47–22:42:48，以现有 perf-sample 对仍在运行的 PID 58888 补采 13 点、约 60.25 秒。与前段 JSON 比较，ProcessStartUtc 和磁盘 DLL SHA-256 相同（temp.76/4dcafc2）；未重启、强制 GC、裁剪工作集或操作窗口。

| 指标 | 前段末尾（22:10） | 本段开始及结束（22:41–22:42） |
| --- | ---: | ---: |
| Private Bytes / MiB | 132.66 | 133.23 |
| Working Set / MiB | 235.86 | 228.30 |
| 句柄 | 1262 | 1117 |
| GDI 对象 | 191 | 191 |
| USER 对象 | 135 | 115 |

本段全部 13 点的上述五项均相同，线程也均为 34。进程 CPU 累计仅增加约 0.078 秒；不将机器 CPU 百分比四舍五入后的 0 当作完全没有工作。跨段 Private Bytes 增加约 0.57 MiB，Working Set 减少约 7.56 MiB，句柄和 USER 回落。这进一步表明前段对象数上升不是本样本中不可逆的单调积累，当前没有支持立即改架构或强制回收的增长证据。

中间约 31 分钟没有连续采样，操作负载也没有固定记录，不能声称整段内存稳定、已排除泄漏，或把变化归功于后续修复（当前进程没有加载这些新提交）。下一步仍需捕获实际增长操作前后并关联 GC/原生分配证据；本轮不新增常驻诊断代码。

原始产物 `src/.state/memory-samples/resource-live-58888-followup-20261027.json` 保留本地、不入 Git，SHA-256：`74E75431CC2DD644D7F2901D22B6F236981BBE90C046E5C1FFCBF67ECFEE50AB`。采样完成且身份对比通过；不是优化 A/B 或 UI 验收。
## 2026-10-27：托管版本与 GUI 对象计数

`scripts/perf-sample.ps1` JSON schema 升至 4：保留 PID/启动时间、发布目录归属、内存/CPU/句柄/线程指标，补充同目录 `ExplorerDirs.App.dll` 路径、SHA-256、ProductVersion，以及每个样本的 `GdiObjectCount` / `UserObjectCount`。DLL 身份只对 Explorer Dirs 进程记录；其他进程仍需 `-AllowOtherProcess`，不会推断其托管程序集。

`BinaryIdentitySource=on-disk-at-start` 明确版本/哈希来自开始采样时的磁盘文件，不能证明映射模块在启动后未被外部替换。GUI 对象通过只读 [GetGuiResources](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguiresources) 取得：成功且无对象为 0，失败为 null。对象数不能换算成字节，也不能仅凭增长归因于 Shell、WPF 或扩展。

后台 `src/ExplorerDirs.DiagnosticsSmoke/perf.ps1` 覆盖 schema、样本/增量一致性、非应用进程默认拒绝、其他进程不伪造 DLL 身份、无效 GUI 资源句柄返回 null、当前 release DLL 哈希/版本读回。Windows PowerShell 子进程和当前 PowerShell 调用均通过；不启动应用窗口或注入输入。查询仅在外部采样时运行，不增加产品常驻开销，不执行 GC/工作集裁剪或重启。

### 本次只读运行基线

PID 58888，启动于 UTC 13:43:03.531；采样为北京时间 22:09:32–22:10:32（约 60.28 秒，13 个样本）。磁盘 DLL ProductVersion 为 `0.6.0-preview.temp.76+4dcafc22202a380de6954f288b9f62eb78e138fb`，不是最近后台开发提交的新构建。DLL SHA-256 为 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`。

| 指标 | 开始 | 结束 | 最低 | 最高 |
|---|---:|---:|---:|---:|
| Working Set / MiB | 233.00 | 235.86 | 233.00 | 235.86 |
| Private Bytes / MiB | 160.46 | 132.66 | 132.59 | 160.46 |
| 进程句柄 | 1215 | 1262 | 1215 | 1266 |
| 线程 | 38 | 49 | 38 | 49 |
| GDI 对象 | 205 | 191 | 191 | 205 |
| USER 对象 | 117 | 135 | 117 | 138 |

本窗口没有呈现 Private Bytes 或 GDI 单调上涨；USER 对象与进程句柄增加，但用户操作、菜单/窗格/标签数量没有固定记录，不能据此判为泄漏或归因。Working Set 与 Private Bytes 方向不同，不把两者混为“内存一直涨”。没有采集托管 GC 或原生分配栈，不能解释本次提交量下降的具体来源；也不是任何修复的前后 A/B。

原始 JSON 留在工作树 `src/.state/memory-samples/resource-live-58888-20261027.json`，不入 Git，SHA-256 为 `4CA0E6A1422481F90B2CC4433FA3B254905C07966735DF5E0BA4FD3CA0D5890E`。后续固定同版本与同操作序列，优先观察 Private Bytes、GDI/USER 与句柄在操作后是否回落，再选择需要分配栈的类别。

## 2026-10-27：应用图标缓存容量检查

源码定位到一处确定的长期引用：ShellIconProvider 的静态 Dictionary 在虚拟 Shell 解析名称或新扩展名不断出现时没有上限，缓存不会随关闭标签清理。当前生产调用主要是标签标题与地址位置菜单；普通文件系统目录共用一张文件夹图标，因此不能把此前普通目录工作负载的数百 MiB 占用归因于这张缓存。

已将该应用缓存换为 256 项 LRU，不裁剪 Shell Travel Log、搜索身份或文件视图。已有控件继续持有 ImageSource，淘汰仅移除缓存引用；需要时重新加载，Native HICON 仍在复制成冻结 ImageSource 后立即 DestroyIcon。它约束的是一个可重新获取的数据缓存，容量上限是条目数，不是进程内存的 MiB 上限。

专项 `--shell-icon-provider-smoke` 在实际生产缓存中插入 1,024 个合成键，旧实现保留 1,027 项，修复后保留 256 项；没有遍历 1,024 个真实 Shell 目录。被淘汰的“此电脑”图标经 Shell 重新加载后像素一致。独立缓存实例继续插入 4,096 个键，验证 LRU、大小写等价替换、缺失不插入、冻结图像消费者引用保持。测试图像和键是合成工作负载，没有强制 GC、工作集裁剪、Shell 文件视图或用户桌面输入。

此结果只关闭一条无界引用路径，不是用户进程的前后 A/B，也未测量释放了多少托管/原生内存。常规本地目录使用下收益预计有限；实际驻留/提交量是否变化仍需同一新构建、固定目录与操作序列的长期采样，继续按下文的进程/GC/原生分配边界定位。

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
