# Explorer Dirs memory sampling

## 2026-10-28：记录四窗格压力阶段及 Shell 宿主释放后的占用

- HostProbe 增加被动阶段检查点，并提供仅供隔离探针显式启用的 `EXPLORER_DIRS_HOSTPROBE_FORCE_GC_AFTER_DISPOSE=1` 完整 GC 对照；正式 App 不读取此变量。四阶段为 4 个 Shell 视图就绪、3,000 项创建完成、删除并静置 5 秒后、释放全部 Shell 宿主后。默认检查点不强制 GC；托管字节字段可能包括尚未回收的垃圾。
- 当前工作树 `4a3f401cbc8cadebf9882af7c96637ad443396a9` 的 Release HostProbe 构建为 0 警告/错误。单轮产品监视模式压力运行退出码 0，四阶段（Private Bytes / Working Set / GC 托管堆估算 / 句柄）分别为：就绪 `75.88 / 125.36 MiB / 1.10 MiB / 984`；创建后 `82.34 / 134.88 MiB / 6.29 MiB / 998`；删除静置后 `89.87 / 143.30 MiB / 12.93 MiB / 1,020`；Shell 宿主 Dispose 后 `78.72 / 131.76 MiB / 13.02 MiB / 895`。Delete 后视图恢复 3 项，选区与焦点保持，3,000 个 watcher 事件完整，无 observer 错误。
- 单独启用完整 GC 后，托管堆估算降到 `0.68 MiB`，最近 GC 堆大小为 `1.16 MiB`，Gen0/1/2 各发生 1 次；Private Bytes 为 `79.67 MiB`，比 GC 前的 Dispose 检查点略高约 `0.93 MiB`，而低于压力峰值约 `10.20 MiB`。因此多数托管堆估算是可回收的分配，但进程 Private Bytes 不会按托管堆差额同步下降；GC 堆保留、运行时、Shell 原生对象等来源仍未进一步区分。不得将强制 GC 对照解释成产品会自动清理或泄漏已排除。
- 构建后完整 Release `SettingsSmoke` 全部通过。无窗口、无桌面输入、未操作 temp.76，也未更新 release。原始阶段输出在忽略目录 `src/.state/memory-samples/hostprobe-stress3000-full-gc-20261028.txt`，SHA-256 `C84BC6DC23F9EA09BE6B7705CF2EF29595AF7D05B87F1E3B4E3594EE573A5D3D`。这仍是隐藏 HostProbe 合成负载，不等同于完整正式 App 的长期工作曲线或分配栈。

## 2026-10-28：四窗格隐藏宿主的 3,000 项变更峰值

- 用当前工作树 `4a3f401cbc8cadebf9882af7c96637ad443396a9` 构建 Release HostProbe，构建 0 警告/错误；加载的 App 程序集版本为 `0.6.0-preview.temp.3+4a3f401cbc8cadebf9882af7c96637ad443396a9`。以隐藏生产 ExplorerBrowser 宿主创建四窗格，向一个窗格写入并删除 3,000 个临时文件，其它窗格保留 3 项样本；外部每约 250 ms 采集进程级内存。全程未显示窗口、注入输入或操作 temp.76；夹具由探针清理。
- 单轮 56 点、约 14.4 秒：Private Bytes 峰值 `94,130,176` B（89.77 MiB），Working Set 峰值 `149,016,576` B（142.11 MiB），最多 1,023 个进程句柄、39 个线程。完整创建/删除最终可见，目录和 Shell 视图回到 3 项，选区与焦点恢复，watcher 事件为 3,000/3,000、无 observer 错误；删除侧触发一次计数兜底刷新。独立重复运行的 HostProbe 退出码为 0。
- 采样第一点发生在进程启动的最初数毫秒，不能作为基线；最后一点仍接近峰值，进程随后退出，因此本轮没有测到宿主销毁后的保留量或回落幅度。HostProbe 省略正式 App 的主窗口与完整应用控件，也没有采集 GC 堆/原生分配栈；这些数字只说明这个合成压力阶段的进程峰值，不等价于用户正式版内存，也不能证明泄漏。
- 原始样本在忽略目录 `src/.state/memory-samples/hostprobe-stress3000-current-20261028-090020.csv`，SHA-256 `FE8DAC0D6751D2683F4A3363EB638D5839ACEE9931AAE31853C3E3C0EBBA16A4`；探针输出 `hostprobe-stress3000-current-20261028-090020.stdout.txt`，SHA-256 `0A9BC21E9C70F19A47077929597CBAB2A940EC19CF1EE3C6EA1859E88AFBC012`。该目录不入 Git。
- 同轮也跑过 `--live-shell-directory-stress-native 3000` 对照，但它没有生产监视器、没有 5 秒操作后静置，而且产品监视模式与 native-only 模式的选区基线不同；峰值差异不能作为监视器成本的 A/B 结论。下一步若要归因，应在同一进程内增加按阶段的托管堆/GC 计数和宿主销毁后样本，或对正式 App 固定负载采集；本轮不据此改内存策略。

## 2026-10-28：temp.76 两分钟被动复采

- 北京时间 08:00:03–08:01:58（UTC 2026-10-28 00:00:03–00:01:58），对仍运行的 PID `58888` 每 5 秒采样，共 24 点；进程启动时间保持 UTC `2026-10-27T13:43:03.531Z`。未启动、重启、聚焦或操作应用。
- 实例为 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.76\app\ExplorerDirs.App.exe`，ProductVersion `0.6.0-preview.temp.76+4dcafc22202a380de6954f288b9f62eb78e138fb`。EXE SHA-256 `016E5C4FA3145051FEFD694D17F53E4C5BF320550C6236FD8E7639C93BD79F93`，DLL SHA-256 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`。
- Working Set 全程 `244,584,448` 字节（233.25 MiB）；Private Bytes 范围 `121,765,888–121,806,848` 字节，首末均为 `121,806,848`。句柄 `1122`、线程 `34`、GDI `191`、USER `113` 均未变化；累计 CPU 增加 `0.079` 秒。
- 原始 JSON 位于忽略目录 `src/.state/memory-samples/temp76-20261028-passive-58888.json`，SHA-256 `9CD2434B92A0CA459A1792DEEAD1C699FCADC51AFAC31CF908D4334CD6C74A4D`。此被动窗口没有固定或记录用户操作、窗格和标签负载；只能说明该区间未观察到明显进程级增长，不能代表文件浏览负载、排除短时峰值/增长后回落或定位分配来源。

## 2026-10-28：temp.76 新一轮被动采样

- 北京时间 07:13:45–07:14:45（UTC 2026-10-27 23:13:45–23:14:45），对 PID `58888` 按 5 秒间隔采样 13 点。进程启动时间 `2026-10-27T13:43:03.531Z`，仍是 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.76\app\ExplorerDirs.App.exe`，ProductVersion `0.6.0-preview.temp.76+4dcafc22202a380de6954f288b9f62eb78e138fb`。未启动、重启、聚焦或操作应用。
- Working Set 全程 `244,580,352` 字节（233.25 MiB），Private Bytes 全程 `121,810,944` 字节（116.17 MiB），句柄 `1121`、线程 `34`、GDI `191`、USER `114` 均未变化；累计 CPU 增加约 `0.016` 秒。
- 原始 JSON 位于忽略目录 `src/.state/memory-samples/background-20261028-next.json`，SHA-256 `87D9EE404310306F99D260DB8A619B964C6DB8E874A94887CF3467257016D888`。这是一个无操作标记的一分钟空闲样本，只能说明本区间没有观察到进程级增长；不能代表浏览/文件操作负载、长时趋势或分配来源。

## 2026-10-28：temp.76 五分钟被动采样

- 北京时间 06:23:50–06:28:51（UTC 2026-10-27 22:23:50–22:28:51），对仍运行的 PID `58888` 采样 61 点、间隔 5 秒；进程启动时间全程为 UTC `2026-10-27T13:43:03.531Z`。采样器未启动、重启、聚焦或操作应用，也未附加运行时计数器。
- 实例仍为 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.76\app\ExplorerDirs.App.exe`，ProductVersion `0.6.0-preview.temp.76+4dcafc22202a380de6954f288b9f62eb78e138fb`。EXE SHA-256 `016E5C4FA3145051FEFD694D17F53E4C5BF320550C6236FD8E7639C93BD79F93`，DLL SHA-256 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`。
- Working Set 全程 `244,535,296` 字节（233.25 MiB），Private Bytes 全程 `123,269,120` 字节（117.55 MiB），句柄 `1120`、线程 `34` 全程不变。
- 原始 CSV 留在忽略目录 `src/.state/memory-samples/passive-goal-20261028.csv`，SHA-256 `1F422BA8D0582739D69EDD60521FDF8E4B206F1BAC9DCBC7C14D06CEDEFEEE38`。脚本没有记录操作、窗格或标签负载；稳定的进程级计数不能排除操作期间的短时峰值、增长后回落或更长周期变化，也不能定位分配来源。

## 2026-10-28：temp.76 再次被动复采

- 北京时间 03:27:13–03:28:13（UTC 2026-10-27 19:27:13–19:28:13），对仍在运行的 PID `58888` 采样 13 点、间隔约 5 秒；进程启动时间保持 UTC `2026-10-27T13:43:03.531Z`。没有启动/重启或操作窗口，没有强制 GC、裁剪工作集或附加运行时计数器。
- 实例仍来自 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.76\app\ExplorerDirs.App.exe`。EXE SHA-256 `016E5C4FA3145051FEFD694D17F53E4C5BF320550C6236FD8E7639C93BD79F93`，DLL SHA-256 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`。
- Working Set 范围 `244,543,488–244,563,968` 字节；Private Bytes `123,273,216–123,314,176` 字节；虚拟字节 `2,342,109,507,584–2,342,112,653,312` 字节；句柄 `1120–1123`，线程 `34–35`。首末变化均很小，没有观察到这一分钟内单调上升。
- 原始 CSV 留在忽略目录 `src/.state/memory-samples/ExplorerDirs-memory-20261028-032713.csv`，SHA-256 `C9B722D92CFF27C177567AAC062D1287A0FCCFB2BA4B7684A3612085ED89BD4B`。该样本没有记录窗格/标签或用户操作，不能代表实际浏览负载、长期稳定或定位分配来源。

## 2026-10-28：temp.76 同一进程被动复采

- 北京时间 01:47:34–01:48:34（UTC 2026-10-27 17:47:34–17:48:34），对 PID `58888` 采样 13 点、间隔约 5 秒。进程启动时间、EXE/DLL SHA-256 与前次 temp.76 记录一致；本次仍是 `0.6.0-preview.temp.76+4dcafc22202a380de6954f288b9f62eb78e138fb`，没有重启、触碰窗口、强制 GC 或裁剪工作集。
- Working Set 全程 `244,477,952` B（233.25 MiB），Private Bytes 全程 `123,269,120` B（117.55 MiB），句柄 `1120`、线程 `34`、GDI `191` 全程稳定；USER 对象范围 `114–115`。CPU 累计增加 `0.016` 秒。
- 这再次支持该约一分钟空闲区间没有明显进程级增长；此前样本负载同样未固定，因此不能推出实际浏览操作期间稳定、排除长期增长，或将占用归因到托管堆/Shell/WPF/扩展。该采样没有采集运行时计数器或原生分配栈。
- 原始 JSON 位于忽略目录 [`ExplorerDirs-memory-58888-20261027-1747Z.json`](../src/.state/memory-samples/ExplorerDirs-memory-58888-20261027-1747Z.json)，SHA-256 `7BC0CD32B66674D1F0095AC46C12EE73872C6EC1CF722BAA7C680FBFEC5B1D1F`。

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

## 2026-10-29 UTC：temp.76 同一进程只读复采

- 对象仍为 `preview-0.6.0-temp.76` PID `58888`，启动时间 `2026-10-27T13:43:03Z`。EXE SHA-256 `016E5C4FA3145051FEFD694D17F53E4C5BF320550C6236FD8E7639C93BD79F93`；应用 DLL SHA-256 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`。
- UTC `2026-10-28T16:50:16Z` 至 `16:51:16Z`，13 个样本、约每 5 秒一次，覆盖 60.296 秒。Working Set 全程 `244,547,584` B（233.31 MiB），Private Bytes `123,265,024` B（117.55 MiB），虚拟内存 `2,342,109,573,120` B，句柄 `1121`，线程 `34`；五项计数在本窗口的首末值及最小/最大值相同。
- 这是无交互的只读进程采样；没有记录窗格/标签数量或用户操作，也没有采集 CPU、GC 或原生分配栈。它表明该窗口内这些计数稳定，不构成长时间无泄漏证明，也不能用于组件归因或优化前后 A/B。
- CSV 保留在忽略目录 `src/.state/memory-samples/ExplorerDirs-memory-20261028-background.csv`，SHA-256 `E7D809DD7A290B673C788AEA79B86A9C39414966DE822D34A3D777DECF8A03BB`。后续仍需在记录固定操作序列的情况下观察同一 PID 的曲线，再按 Private Bytes/GDI/USER/句柄变化选择 profiler 路径。

## 2026-10-28 CST：temp.76 后续 60 秒同 PID 只读采样

- 再次采样同一 `preview-0.6.0-temp.76` PID `58888`，进程启动时间 `2026-10-27T13:43:03Z`。EXE SHA-256 `016E5C4FA3145051FEFD694D17F53E4C5BF320550C6236FD8E7639C93BD79F93`；应用 DLL SHA-256 `6015270C1A770FF8B8591709AB1E8BD53AA32A670BC6EAEC000468AADDBD8BF8`。
- UTC `2026-10-27T20:33:28Z` 至 `20:34:28Z`，13 个样本、覆盖 60.457 秒。Working Set 全程 `244,539,392` B（233.30 MiB），Private Bytes `123,273,216` B（117.55 MiB），虚拟内存 `2,342,109,507,584` B，句柄 `1120`，线程 `34`；每项首末、最小和最大值相同，与前一段 60 秒采样基本一致。
- 采样全程只读且未操作桌面，但没有记录窗格/标签数量和用户负载，也没有 GC 或原生分配栈。因此只支持两个短时静置窗口内计数稳定，不排除长时间增长，不能归因或作为改版 A/B。
- 原始 CSV 留在忽略目录 `src/.state/memory-samples/ExplorerDirs-memory-followup-20261028.csv`，SHA-256 `47B59CB1D5F97CDDF52B972C27D0F7F40F8D48B2505EECC3EF669E1CFE77089F`。后续要判断运行期间增长，仍需在固定操作序列下采样同一进程并结合分配类别；本次没有启动、重启或操作应用。

## 2026-10-28 CST：temp.76 同一 PID 的短时 .NET 运行时计数

- 对同一 PID `58888`、同一 temp.76 EXE/DLL 哈希执行 30 秒 `dotnet-counters` 采样（本机时间 `04:36:03` 至 `04:36:28`，6 个快照；累计计数提供 5 个五秒增量）。没有暂停、重启、强制 GC 或输入桌面；进程 Working Set 在该窗口约 `244.61–244.65` MB，另一次进程采样 Private Bytes 为 `123,273,216` B（117.55 MiB）。
- GC 最近一次报告的各代堆大小合计约 `17.94 MiB`，GC committed 为 `27.54 MiB`；托管分配增量合计约 `9.63 MiB/25 秒`，约 `0.38 MiB/s`。窗口中 Gen0/1/2 GC 次数与 GC pause 增量均为 0。`last_collection.heap.size` 不是实时存活堆，累计分配也不是保留量或泄漏量。
- 这进一步表明观察到的进程 Private Bytes 明显高于 GC committed，但不能将差值归给 Shell、WPF、运行时或第三方扩展；采样窗口没有固定操作负载，也没有原生分配栈。它只提供一次短时闲置运行时快照，不构成长期增长结论或新旧版本 A/B。
- 原始 CSV 留在忽略目录 `src/.state/memory-samples/runtime-followup-58888-20261028.csv`，SHA-256 `C657D480B88CD7735727EC1212814BCB8433CE238E1717A2E16D9A9ACE9EA76D`。指标解释见上文列出的微软 .NET runtime metrics 文档。
