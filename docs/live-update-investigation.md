# 文件视图自动更新调查（2026-10-21）

## 2026-10-26 当前 Release 的 10,000 项创建/删除回归

运行 `dotnet build src/ExplorerDirs.HostProbe/ExplorerDirs.HostProbe.csproj -c Release` 后，Release `--live-shell-directory-stress-large 10000` 隐藏生产宿主探针退出码 0。创建后 1,031 ms 视图可见，创建操作约 1,816 ms；删除后 784 ms 视图最终可见并收敛。观察器与生产 watcher 分别记录创建/删除 10,000/10,000 和 10,001/10,001，无 observer 错误，Shell 创建通知 1 次；创建阶段无 fallback，删除阶段发生 2 次 fallback，其中一次在删除操作完成时触发。

最终目录计数与磁盘一致为 3/3，原先两项选择和独立焦点项均恢复。过程计数并不平滑：时间线曾从 9,874 项降至 975、再恢复到 3；创建期间也短暂显示 10,003 项。最大 Dispatcher 心跳间隔 139 ms、该探针测得最大 Shell 选区读取 15 ms、焦点读取 15 ms。此为单轮本机隐藏生产宿主样本；最终一致不等于过程可见体验正确，计数波动也未定位其具体 Shell 异步枚举/刷新成因。没有启动可见产品窗口或生成 release，自动刷新大目录 P0 仍开放。

对照 fallback 诊断字段，删除阶段的两次 fallback 均为 `forced=True`，各自的 expected/actual 计数相等；对应批次都同时收到了创建和删除事件（累计 `created-events=10000/10001`、`deleted-events=1/2`）。产品队列明确在同一批次包含创建与删除时强制 Refresh，因为单看净计数无法发现等量替换的条目身份变化；`ShellDirectoryChangeQueueSmoke` / `ShellDirectoryChangeBatchSmoke` 覆盖此规则。因此这两次 fallback 与计数不匹配无关，不能据此判定为多余刷新。计数大幅回落发生在异步刷新/重枚举时间线内，尚未判明是 `Refresh()` 触发的 DefView 重建还是期间另一批 Shell 枚举；后续需将刷新调用起止、Shell 数量序列和异步完成回调按同一单调时钟对齐。

## 2026-10-26 定时计数读取后的监视器身份复核

源码复核发现启动采样在 `ReadDirectoryMonitorItemCount` 后通过 `ShellDirectoryMonitorRequest.IsCurrent` 拒绝旧读取，但运行中 Dispatcher timer 和合并创建/文件夹删除通知直接把读取值交给刷新/通知逻辑。计数读取进入 Shell COM；若该调用重入导航，旧目录的结果可能被应用到当前 batch 或用于向 Shell 发送过期通知。

现有 monitor request 现在也用于运行中 timer、合并创建/文件夹删除通知和异步刷新完成回调：完成 Shell 计数读取后重新核验 monitor/navigation 版本、view readiness、已完成路径和 watcher queue 引用。过期读取会结束当前 tick、取消这次 Shell 通知，或阻止旧 batch 结算；异步刷新回调在清理共享状态前也再核验一次，避免旧回调把新监视器标记成空闲。`ShellDirectoryChangeBatch` 的新增 smoke 在 count delegate 内改变请求状态，确认旧读数不结算 batch 基线。

此处是由源码调用边界推导出的竞态防护，纯状态 smoke 不会触发 Windows COM 重入，也未运行产品 ExplorerBrowser/HostProbe 或 GUI。真实重入时序、当前窗格更新和焦点保持仍需后续产品宿主/桌面验收；自动刷新 P0 保持开放。

## 2026-10-26 watcher 自然收敛时的选区漂移

10,000 项产品 HostProbe 出现过间歇性失败：创建后的刷新能恢复 `second.txt`、`third.txt` 和未选中焦点 `initial.txt`，但后续删除一个未选中 burst 文件后，Shell 视图会把 `initial.txt` 加入选区；选区回调没有报告这次最终状态变化。这个删除只让目录计数自然收敛到 watcher 预期值，因此 `ShellDirectoryChangeBatch.Complete` 不要求整视图刷新，原路径也就没有再次核对或恢复选区。

新增隐藏产品 HostProbe 复现并确认：watcher 观察到一次删除，目录项数正确，但最终选区由两项变为三项，焦点仍正确。定向 `--live-shell-selection-delete-watcher` 探针最初被自身的反射调用错误阻断；显式传入可选 HWND 参数后，修正后的探针按预期先失败。产品在计数已自然收敛分支增加一轮恢复：只有缓存快照仍属于当前路径、导航代际和用户输入代际时才复用它，Shell 仍通过原生 view API 完成精确读回。用户输入变化后不使用旧快照。复核计时器后又加上 `HasPendingChanges` 条件：只在本轮确有 watcher 变化时恢复，空闲轮询不触碰 Shell 焦点；若停滞窗口在本轮内重基准，条件取自其重基准前的批次状态。

修复后定向 watcher 删除探针退出码 0，两项选择与焦点均保持；显式用户清空选区的隐藏探针退出码 0，确认不会复活已清空的选择。`ShellDirectoryChangeBatchSmoke` 覆盖空闲、待处理变化及结算后状态，完整 Release `SettingsSmoke` 通过。再跑 `--live-shell-directory-stress-large 10000` 退出码 0：创建/删除 watcher 事件各 10,000 次，四窗格最终计数 3/3，创建及后续删除的选区/焦点检查通过；刷新前后有界恢复仍保持工作。App Release build 为 0 警告、0 错误。所有验证均为隐藏生产宿主；没有操作可见桌面，也没有生成 release。计数与事件压力通过不能替代其它文件系统 provider、多窗格可见焦点和 GUI 验收。

状态：**未解决**。精确 temp.3 发布包的桌面反例见 [GUI 验收](gui-acceptance-2026-10-21.md)。本页是后续后台调查，不替代真实窗口验收。

## 2026-10-24 当前目录现有文件的元数据变化

源码核对发现，生产 `FileSystemWatcher` 之前只订阅 `FileName | DirectoryName`，仅覆盖项目增删/重命名，没有订阅现有文件的 `LastWrite`、`Size`、`Attributes` 或 `CreationTime`。因此已存在项目写入后，文件大小、修改时间或属性等列可能一直保留旧值，项目总数也无法触发计数差异兜底。

现增加这些 `NotifyFilters`，将 `Changed` 事件映射为 `delta=0, forceRefresh=true`，使用既有队列和 Shell `Refresh()`/选区恢复路径。创建/删除的计数增量保持不变；子目录不递归监听。无窗口映射回归先在生产映射类型缺失时编译失败，接入后 Release SettingsSmoke 与 App Release 构建通过，0 警告/错误。

尚未运行 ExplorerBrowser/HostProbe，也未生成预览包。真实 Windows 上同一文件多次写入产生的事件频率、可见元数据列更新、网络目录表现和 GUI 响应仍需验收；不得将此源码修复等同于此前“文件视图未自动更新”问题整体解决。

## 2026-10-23 跨进程操作与通知已到达的反例

新增 `HostProbe --live-shell-child-process`，将同样的 `IFileOperation` 复制/删除放入
隐藏子进程，父进程继续运行四个生产宿主和两个独立 STA 通知监听器。
子进程只接受自身输出目录下 `fixtures/<GUID>`，检查重解析点和既有生成文件内容；
父进程通过重定向管道接收磁盘完成确认，不同步阻塞 Dispatcher 等待操作。
复制和删除分别使用子进程，操作后均保持 STA 消息循环，直到整轮观察结束才退出，
避免立即退出生产者引入另一种通知条件。操作确认限时 15 秒，异常或提前退出不计为通过。
这些进程不显示窗口、不使用剪贴板、不运行应用启动流程，也不操作用户目录。

| 对照 | 创建阶段（20 秒） | 删除阶段（20 秒） | 通知与磁盘 |
| --- | --- | --- | --- |
| 子进程 Shell 操作 | 四个视图均只有 `initial.txt`，计数 1,1,1,1 | 前三视图为空，第四仍有 `initial.txt`，计数 0,0,0,1 | 磁盘复制/删除均正确；FileSystemWatcher 创建/删除各 4；主 STA 未见 CREATE，仅一条 DELETE，另一 STA 未见对应通知 |
| 随后普通文件 I/O | 四个视图均只有 `initial.txt` | 四个视图仍只有 `initial.txt` | FileSystemWatcher 创建/删除各 4；主 STA 收到四条对应 CREATE 和三条 DELETE，视图仍未一致 |

两轮均按预期返回失败码 1，stderr 为空，父窗口保持隐藏，所有子进程正常结束。
第一轮的创建、删除确认耗时分别为 330/255 ms；这是**进程启动、操作、管道确认和父线程观察**
的合计，不能当作 `IFileOperation` 自身耗时或与先前同进程纯调用时间直接比较。
原始日志分别为：

- `src/.state/live-shell-child-process-e3c25aa016e44b01bd964f2d20def492.log`
- `src/.state/live-io-process-control-aeb2fb0511d943bd99f8e9be23a55269.log`

结论：后台反例不要求文件操作与视图在同一进程；普通 I/O 也可在独立监听器收到创建通知后
仍不更新。因此不能将全部反例都归因于主 STA 执行文件操作或独立监听器未收到通知。
监听器和每个视图各有订阅，本轮仍未证明具体视图收到了相同通知，也未区分隐藏视图条件
与桌面缺陷。下一步检查视图订阅/消费边界；不添加轮询、强制刷新或跨进程产品方案。

诊断自身首次运行曾因 `Console.In.ReadLineAsync()` 同步阻塞 STA 而未发 readiness，
父进程 15 秒超时并清理子进程；这次失败不纳入 Shell 通知结论。
改为在线程池等待管道输入、STA 保持泵消息后得到上述有效结果。
另用仓库根目录作为非法子进程参数，确认在操作前拒绝并返回 2；未残留探针进程。
HostProbe Release 构建 0 警告/0 错误，独立代码审查完成。本轮只有诊断工具与文档变更，
没有产品修复、可见 GUI 验收或新 release。

## 2026-10-23 独立 STA 通知监听对照

针对“生产 STA 同时执行文件操作/读取视图，是否单独阻碍通知”的假设，新增
`--live-shell-independent-sta`：在同一进程内创建第二个 STA、独立 Dispatcher 和永不显示的
HwndSource。它只注册同一 fixture 根目录的 Shell 通知，不执行文件操作、视图查询或刷新。
主线程原观察器继续保留；两者和操作/视图日志共用同一 Stopwatch。注册完成后才允许修改文件。
后台摘要在短锁内读取，PIDL 名称解析在锁外；结束时请求 Dispatcher 退出并等待线程结束，
在所属线程退订和销毁源。初始化/运行失败不能被当作“没有通知”。
两份摘要分别读取，内部各自一致，但不是跨线程同一瞬间的原子快照。

| 同一轮对照 | 创建/删除视图 | 主 STA 通知 | 独立 STA 通知 | 退出 |
| --- | --- | --- | --- | --- |
| 默认视图 A | 2,2,2,2 → 1,1,1,1 | CREATE/DELETE 各 4 | CREATE/DELETE 各 4 | 0 |
| 默认视图 B | 2,2,2,2 → 1,1,1,1 | CREATE/DELETE 各 4 | CREATE/DELETE 各 4 | 0 |
| 关闭独立 STA 的对照 | 2,2,2,2 → 1,1,1,1 | CREATE/DELETE 各 4 | 未启用 | 0 |
| 已保存视图配置 | 2,2,2,2 → 1,1,1,1 | CREATE/DELETE 各 4 | CREATE/DELETE 各 4 | 0 |

独立监听的三个运行中，两边对应通知时间相同或相差约 1 ms，均无通知锁失败；
关闭独立监听也通过。因此，本轮没有出现“主线程缺少通知而独立 STA 完整收到”的反例，
**尚不能支持或排除该假设，更不能把独立 STA 作为产品修复**。先前失败证据仍有效。
这些有限运行也不支持计算故障率或宣称通知已稳定；不再仅为获取绿色结果反复重跑。

四份日志（均位于 `src/.state/`）依次是：

- `live-shell-independent-sta-a1189786af734c93af9028fd117ad3fc.log`
- `live-shell-independent-sta-55e73161b18d4c68bce20b3f251496bb.log`
- `live-shell-control-dd7a895c33e94e84ad3cd8768299f168.log`
- `live-shell-independent-sta-5342fc574ba54b9d881c6850c96f3424.log`

构建通过，0 警告、0 错误。所有线程退出和隐藏窗口检查随每次运行通过；没有可见窗口、
输入、剪贴板或用户文件操作，没有产品刷新代码变更。保留对照入口供下一次可复现失败时使用。
独立代码审查核对了启动同步、锁边界、线程清理及证据解释，未发现阻断问题。

## 2026-10-23 无界面 Shell 文件操作对照

遵守用户“不使用 computer-use”的要求，本轮仅使用永不显示的 HwndSource 和生产 ExplorerBrowserHost。
新增 `--live-shell-operations`，通过 `IFileOperation` 在本次唯一 fixture 的四个直接子目录内将
`initial.txt` 复制为 `created.txt`，随后永久删除自建的 `initial.txt`。不使用剪贴板、回收站或用户目录，
不请求撤销记录；关闭原生进度/确认/错误 UI、提权提示和复制钩子，遇错提前失败。
调用前检查 fixture 路径、普通文件属性与原始内容；调用后同时检查 HRESULT、aborted 和实际磁盘内容。
这不是应用 Ctrl+C/V 命令入口，也不覆盖第三方复制钩子、确认框或焦点。

接口槽位核对 [Windows SDK 声明](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/ShObjIdl_core.h)，
标志依据 [SetOperationFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)。
所有操作只在探针内，未替换产品 Shell 命令，也未添加强制刷新。

| 本轮顺序 | 生产宿主配置及变更方式 | 创建后四窗格计数 | 删除后四窗格计数 | 结果 |
| --- | --- | --- | --- | --- |
| 1 | 默认视图，普通文件 I/O | 2,2,2,2（1028 ms） | 1,1,1,1（524 ms） | 通过 |
| 2 | 只读已有隔离配置，普通文件 I/O | 2,2,2,2（1029 ms） | 1,1,1,1（538 ms） | 通过 |
| 3 | 默认视图，Shell 操作 | 1,1,1,1（20 s 超时） | 1,1,0,1（20 s 超时） | 失败，磁盘复制/删除均成功 |
| 4 | 默认视图，普通文件 I/O 对照 | 2,2,2,2（1043 ms） | 1,1,1,1（519 ms） | 通过 |
| 5 | 默认视图，Shell 操作 | 2,2,2,2（523 ms） | 2,2,2,2（20 s 超时） | 失败，删除后仍有旧项 |
| 6 | 只读已有隔离配置，Shell 操作 | 1,1,2,2（20 s 超时） | 1,1,2,2（20 s 超时） | 失败，文件身份也不符合磁盘 |
| 7 | 默认视图，Shell 操作，共享时钟日志 | 2,2,2,2（522 ms） | 1,1,1,1（554 ms） | 通过，不抵消此前反例 |

每次普通文件 I/O 与 Shell 操作观察均使用新建 fixture；表中时间是操作返回后的观察时间，
Shell 操作自身耗时另记（本机 23～84 ms），不是性能保证或统计性故障率。
失败轮次中 FileSystemWatcher 收到 4 个创建、4 个删除且无错误，而独立 Shell 观察器收到的
CREATE/DELETE 只有部分或缺失；共享内存通知锁没有失败。观察器与原生视图的订阅不同，
不能按某一个 pane 的通知逐条等同于该视图的投递，也不能把通知合并等同于丢失。
本轮证据说明需要继续检查通知投递与消费边界，尚未证明具体责任组件。

探针增加有限长度的 Shell 通知时间线及视图计数变化日志，最终版本使用同一 Stopwatch。
审查同时核对了本机 Windows SDK 的 COM ABI 与无 UI 标志，未发现阻断问题；
时间戳建议已落实为进入消息回调时取值，操作起止也使用共享时钟（第 7 次之后补齐日志字段）。
每阶段同时验证实际文件名，避免只看计数把旧文件误判为新结果。20 秒失败返回非零；
它表示观察期限内未一致，不表示永远不会更新。

本机原始日志保留于 `src/.state/live-shell-operations-2c2c52de5969467e80180b1049440457.log`
（第 6 次，通知与视图时钟各自起算）及 `src/.state/live-shell-operations-4f56c4948c1e403ab724076ead93f83c.log`
（第 7 次，共享时钟）。日志只包含生成的相对 fixture 名称；不跟踪生成文件。
最终 HostProbe Release 构建为 0 警告、0 错误。**没有修复结论，没有新的发布或 GUI 验收。**

独立 STA 与跨进程对照的后续结果见上节。
可见窗口对照仍待用户授权，不通过定时刷新隐藏根因。

## 后续连续循环与通知边界调查

生产宿主测试已把初始化、等待、创建、删除放在同一个 DispatcherFrame 内，用阶段状态控制；不再在文件操作之间退出消息循环。

- 连续循环 + 保存配置：创建 `2,2,1,1`、删除 `2,1,1,1`，退出 1。
- 连续循环 + 移除自定义窗口子类钩子：先通过，重跑创建 `2,2,1,1`、删除 `2,2,1,1`，退出 1。
- 隔离源码副本不订阅导航回调：创建 `2,1,1,1`、删除 `2,0,0,1`，退出 1。
- 隔离探针直接使用原生 IExplorerBrowser::GetCurrentView 读取计数，避开生产 Getter 的视图 RCW：创建 `2,2,1,1`、删除 `1,1,1,1`，创建已失败，退出仍为 1。
- **重新运行最小原生对照也出现失败**：WPF 消息循环下无站点创建 `2,2,1,1`、删除 `2,2,1,0`；Forms 循环下无站点创建 `2,1,2,2`、删除 `1,1,2,2`。两种对照的生产站点分支也失败。此前单次通过不能用于排除这一层。

因此，生产宿主、保存配置或某个消息循环并不是当前证据下的必要条件；不能直接据隐藏探针给生产代码增加刷新补丁。还需区分系统通知条件、隐藏视图行为与可见窗口缺陷。

新增 `ChangeNotificationObserver`，只在探针自己的隐藏窗口上注册对唯一 fixture 根目录的 Shell 变更观察。它不刷新视图，不更改系统配置；退出时退订。输出仅包含事件类型计数和共享内存锁失败数，不记录浏览路径。注册使用 [SHChangeNotifyRegister](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister) 的中断/Shell/递归/新投递标志，按照 SDK 规则锁定、解锁通知数据。Windows 可以合并通知，因此不能把通知条数当成文件操作数。

首次观察器运行：四窗格创建阶段都保持 1，观察器累计收到三次 `0x2000`（UPDATEITEM），未观察到 CREATE/DELETE。此证据不足以证明通知永远丢失。另一个隔离试验显式对自建文件发送 SHChangeNotify 并 flush：创建四窗格均变为 2，但删除仍为 `1,2,1,2`；没有将显式发送通知加入产品代码。

删除阶段如果创建已失败，现在会继续观察完整期限，而不会因旧计数刚好为 1 立即结束。两探针重新构建均为 0 警告、0 错误。这些结果属于失败调查，不是验收通过。

## 已确认的边界

- 粘贴/删除可以已在磁盘成功，视图与项目数量仍未更新；F5 后可见。不能把此现象全部归为键盘焦点。
- `ICommDlgBrowser3` 接口补齐不是充分修复条件。源码注释与 ABI 测试说明已移除这种暗示。
- 原生最小四窗格对照中，无站点与生产站点都曾通过创建/删除自动更新，因此不能认定站点本身必然阻止通知。
- 当前背景实验没有显示窗口、发送输入、修改剪贴板或操作用户目录。只读解析指定 profile 的 ViewState，测试路径始终替换成自建唯一 fixture。

## 可运行的探针

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --live-updates
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --live-updates-wpf
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-updates
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-updates --profile <只读配置路径>
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-shell-operations
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-shell-operations --profile <只读配置路径>
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-shell-independent-sta

dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-shell-child-process
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --live-shell-refresh-unselected-focus
```

`NativeProbe` 比较无站点/生产站点四个原生视图；`HostProbe` 引用真实 App 程序集，在不显示的 HwndSource 中承载四个生产 ExplorerBrowserHost。后者不启动 MainWindow，不加载 App 启动流程，也没有菜单操作或 UIA 焦点验收。

创建前预期每窗格 1 项，创建后 2 项，删除自建 initial.txt 后 1 项。创建失败后，删除阶段恰好读到 1 不能算通过。两个探针都在观察失败时返回非零；测试生成数据保留在各自 bin 下 fixtures 中。

## 本轮观察

| 对照 | 观察 |
| --- | --- |
| 原生最小视图 + Forms 消息循环 | 无站点/有站点均 2,2,2,2 → 1,1,1,1 |
| 原生最小视图 + 持续 WPF 消息循环 | 无站点/有站点均通过，创建约 0.2～1 秒、删除约 0.5 秒，仅作本机观察 |
| 生产宿主 + 默认视图 | 一次完整通过 |
| 生产宿主 + temp.3 测试保存的列/排序/图标配置 | 可失败，新增阶段有过 1,1,1,1；并不证明配置是根因 |
| 同上，初始化后再等待 1.5 秒 | 一次完整通过，随后相同命令重跑删除阶段出现 1,2,1,2，退出码 1 |
| 隔离诊断副本显式 OleInitialize | 新增 2,2,1,2，删除 1,2,0,2，退出码 1；不能消除失败 |

## 被排除的测试假象与未成立结论

初版探针在循环里频繁 BeginInvoke 退出、重进 DispatcherFrame，并 Thread.Sleep。该方式可以让最小原生视图也出现部分窗格滞后。后来改为单阶段持续 DispatcherFrame + 定时条件观察，该最小对照通过。因此早期“移除子类钩子/跳过 ApplyViewState/移除历史事件订阅仍失败”的实验受消息循环混杂因素影响，不能据此排除相应生产路径。

保留持续循环后，生产宿主仍有间歇失败，证据尚不足以确定与桌面缺陷相同的根因。5 秒观察超时也不能证明永远不会更新。没有添加轮询刷新、FileSystemWatcher 或 F5 替代方案。

## 2026-10-23 显式刷新恢复对照

新增 `ExplorerDirs.HostProbe --live-shell-refresh`：先确认生产四窗格各读取到一个 fixture 项，
再通过诊断用 `IFileOperation` 向四个目录复制文件。复制后、调用生产宿主 `Refresh()` 前，
四个视图计数仍为 `1,1,1,1`；调用后在观察期限内读到 `2,2,2,2`，退出码 0。
父窗口始终隐藏，操作仅涉及本轮生成的 fixture。

这证明当前宿主的 `IShellView.Refresh()` 可以在本机隔离四窗格对照中恢复已落盘的目录内容，
也说明自动通知失败与显式刷新成功可以同时发生。

### 单选恢复跟进

将复制和显式刷新拆成每次只刷新一个目标窗格的四轮隔离测试。初测只有单选时，刷新会清掉
目标项选区；刷新返回后立即按身份恢复成功。随后把 fixture 扩成每窗格三项，选中三项并记录
焦点项目，再分别刷新每个窗格。生产 `Refresh()` 最初在刷新后读到 `3/0` 个选中项、焦点落到
新增文件，回归检查退出 1。实现生产修正后，四个窗格各自都在 `3/3` 项选择、原焦点身份保持的
情况下把目标视图从 3 项更新到 4 项，其他窗格计数不变；四个回归用例退出 0。

生产修正由 `ShellViewSelectionSnapshot` 克隆并保留相对 PIDL，在 Dispatcher 后台阶段尝试恢复，
并通过当前父 Shell 文件夹的 `CompareIDs` 检查项目仍位于视图。它使用 Shell 项目比较，而不是
按列表索引或 PIDL 原始字节判断，避免排序变化把选择指向另一项。只有导航版本和当前路径仍匹配时才执行恢复；通过
`SVSI_NOTAKEFOCUS` 避免把操作系统键盘焦点移入宿主。随后新增删除已选文件用例：测试先复制，
再删除焦点项 `initial.txt`；刷新后四个窗格的各自用例均保留其余两项选区，并将焦点放到仍选中的
`second.txt`，项目数与文件身份也匹配。复制和删除用例均在四窗格分别运行通过。

该结果仍只覆盖本机文件夹、三项多选、单个焦点项和复制/删除文件操作；未覆盖刷新期间重命名/
导航、排序分组读回、无选区时的焦点规则或可见窗口。它改善显式刷新行为，但没有接入自动变更
通知，P0 仍开放。

### 未选中焦点项跟进

新增隐藏生产宿主探针 `--live-shell-refresh-unselected-focus`：刷新前保持 `second.txt`、
`third.txt` 选中，同时让未选中的 `initial.txt` 获得项目焦点；随后通过 Shell 复制文件并调用
生产 `Refresh()`。单轮验证结果为选区 `2/2` 身份一致、焦点仍为 `initial.txt`、列表由 3 项更新为
4 项，退出码 0。该探针确认快照同时恢复“焦点项是否选中”的区别；没有更改生产代码，也不涉及
可见窗口。多窗格及排序/分组下的该特殊焦点状态尚未覆盖。

### `CDB2GVF_NOINCLUDEITEM` 对照

微软文档将 `CDB2GVF_NOINCLUDEITEM`（`0x10`）定义为“不调用 `IncludeObject`”；
ExplorerBrowser 文档也建议搜索视图返回此 flag，以避免在 UI 线程同步过滤项目。
见 [GetViewFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icommdlgbrowser2-getviewflags)
和 [IExplorerBrowser](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)。

为隔离这个怀疑点，NativeProbe 新增 `--live-updates-view-flags`，用相同的隐藏原生四视图和
普通文件 I/O 对照“无 site、生产 site、相同 site 但 GetViewFlags=0”。置零变体实测每个视图
调用 `GetViewFlags` 3 次，确认变体已生效。第一轮三组均未完整更新；随后两轮三组都完成四视图
创建/删除更新。组间没有稳定差异，结果反而确认隐藏原生探针本身存在间歇性更新行为；当前证据
不支持将 NOINCLUDEITEM 作为根因。没有改生产 flag 或加入强制刷新。

该矩阵没有确认真实用户窗口是否同样间歇，也不证明上述 flag 在所有 Shell folder 上无影响；
结论限于本机标准文件夹、隐藏 native ExplorerBrowser 及本探针的回调实现。

### HWND 显隐与窗格折叠对照

新增 `HostProbe --live-shell-hwnd-tree`，等四个生产 Shell 视图都完成导航并挂接后再采样窗口树。
四个视图均为 `SHELLDLL_DefView`，其子树含 `DirectUIHWND` 与滚动条。折叠 pane 0 时，
WPF 宿主 `Static` 的 style 从 `0x50000000` 变为 `0x40000000`（WS_VISIBLE 位清除），
恢复后回到原值；Shell `SHELLDLL_DefView`、`DirectUIHWND` 的 HWND 和 `WS_VISIBLE` 位保持不变。
因为整个探针父窗口隐藏，所有 HWND 的 `IsWindowVisible` 都为 false；这个值只说明祖先链隐藏，
不能拿来代表单个 Shell 视图的可见状态。

随后 `--live-shell-visibility-updates-pane-0..3` 每次使用新 fixture，只把一对同排窗格设为
折叠/未折叠并分别创建一个文件。四次分别轮换 0/1/2/3 为折叠窗格，四次都在折叠期间读到
该对窗格 `2/2` 项，未操作的两窗格仍为 `1/1`；恢复折叠窗格后计数不变。也就是说，在这四轮
隐藏父窗口对照中，WPF 窗格折叠本身没有阻止 Shell 自动更新。

首个 HWND 快照发生在 Shell view attach 前，已作废；首个两阶段更新探针还复用了视图状态，
被未更新计数污染，也不纳入这四轮结果。修正后的树探针等待所有视图 attach；更新探针每个
进程只测一对窗格。以上仍是隐藏父窗口、本机普通目录和普通文件 I/O，不能排除真实可见窗口
行为差异或之前观察到的 Shell 间歇失败；未更改生产代码。

继续枚举同一 STA 的普通顶层及 `HWND_MESSAGE` 窗口，折叠和恢复前后集合稳定。消息专用窗口
包含若干 `WorkerW`、一个 `UIAInvokeHelperWndClass`、一个 WPF `HwndWrapper` 和
`OleMainThreadWndClass`；目前没有证据将某个 `WorkerW` 对应到具体 ExplorerBrowser，
也不能把这个枚举等同于 Shell 内部注册表。所有可见状态检查仍受隐藏根窗口祖先影响。

为对照成功操作阶段，HostProbe 现在会分别采集复制和删除前后的私有消息计数差分及新增参数样本，
只在诊断环境变量启用时记录。最近的完整成功轮中，普通文件 I/O 复制时四个视图各增加
`0x4AE`（wParam 为临时 PIDL 指针、lParam `2`）、`0x4C8`、`0x4CF`；删除时四个视图各增加
`0x4A3`、`0x4AE`（同样类型的指针、lParam `3`）、`0x4C8`、`0x4CC`、`0x4CF`。
Host `IFileOperation` 对照和子进程 `IFileOperation` 对照的成功轮也观察到四个视图计数、文件身份
和磁盘操作一致；消息 ID 仍是未解释的私有消息，不能据此认定通知语义。

此前记录过未更新超时轮，但它发生在新增阶段差分之前，不能据当前新成功样本反推失败轮里是否
缺少这些消息。成功时父观察器、四个子视图消息和列表身份可同时变化；产品 `ICommDlgBrowser2.Notify`
仍为 0。后续已增加同 STA 的 message-only HWND 只读观测，并捕获到带同阶段差分的间歇失败，详见下文。

## 下一步

1. 连续 DispatcherFrame、独立 STA 和跨进程生产者对照已完成。2026-10-23 新增生产 `ShellBrowserSite` 回调及 `IShellView` 子类 HWND 的 WM_USER 以上消息计数，仅在探针进程设置 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 时启用。一次成功轮四窗格约 0.5 秒更新；之后两个 Shell 复制/删除轮都超时 20 秒。超时之一 FileSystemWatcher 收到 4 CREATE/4 DELETE，父 HwndSource 的 Shell 观察器收到 4 次 0x2000（UPDATEDIR），四视图的 `ICommDlgBrowser::Notify` 均为 0，项目计数保持 1；另一次观察器收到 5 个事件（3 个目录级 UPDATEDIR 和 2 个文件路径事件），覆盖三个不同窗格路径，而四视图仍未更新。超时轮各 IShellView HWND 收到相同种类的自定义消息（0x4A1、0x4A3、0x4A4、0x4A5、0x4AD、0x4AE、0x4AF、0x4B0、0x4C8、0x4CC/0x4CF、0xC2D7），计数仅小幅不同；尚未将这些私有消息 ID 与具体 Shell 通知语义映射，因此不据此判定其已处理文件变更。成功轮收到相同集合，也不能用消息出现来证明项目刷新。所有轮次 `OnStateChange` 主要为 2，`Notify` 为 0；实际项目数是独立 Shell view getter 读取。
2. 另一轮超时中，四个文件目录的文件系统操作均完成，但父 Shell 观察器只见 CREATE 前的 3 个目录更新、DELETE 后 1 个通用/未知事件；视图 HWND 消息类型/数量仍近似一致。这与此前父观察器在其他轮收到全四次更新的案例形成反例：父窗口收到什么和视图各自更新不是一对一关系。
3. 一次紧接的探针启动遇到 `SHChangeNotifyRegister` 返回 0，未开始任何文件操作；下一次全新 fixture 注册成功。保留注册失败作为探针边界，不将它记为视图更新失败。回调与 HWND 消息计数只在 opt-in 探针进程写内存，默认产品运行不收集这些字典、不写路径或日志。原始最后一轮摘要位于 `src/.state/live-shell-callback-probe-20261023-b.log`，仅含生成的 fixture 相对标识。
4. 已比较四个生产 Shell 视图 HWND 的父子层级、样式，以及 WPF 折叠期间的显示状态；父窗口隐藏的更新对照里四窗格都能在折叠状态下自动更新。因此单独的 pane collapse 不是这些失败轮的充分解释。HostProbe 现仅在相关隐藏诊断模式下对本 STA 的 message-only HWND 安装临时 subclass，消息经 `DefSubclassProc` 原样转发，退出时移除；对 `WorkerW` 保存少量参数样本。成功时四个 `WorkerW` 各收到 `0x4A0`/`0x4B5`，失败时有的只收到 `0x4B5`，其他类还见 `0x8000` 等；这些窗口目前均未映射到具体 ExplorerBrowser 或通知语义。
5. 只有找到充分解释并通过生产宿主回归的修正，才再安排精确发布包的真实复制、删除及焦点验收。桌面授权已经结束，须再次有明确交接才能操作 GUI。

## 2026-10-23 自动刷新阶段差分间歇失败

新增对照沿用四个隐藏生产 `ExplorerBrowserHost` 和唯一生成目录。每个窗格预置三项，在 pane 0 建立
三项选区及焦点后，由原生 `IFileOperation` 将 `initial.txt` 复制为 `created.txt`；磁盘事件由
`FileSystemWatcher` 计数，父窗口通过 `SHChangeNotifyRegister` 只观察生成根目录，四个 Shell view HWND
和同 STA message-only HWND 分别做阶段计数。成功标准是 3 秒内四窗格各从 3 项变为 4 项；探针随后
显式刷新 pane 0，并独立确认项目身份、选区和焦点。

同一模式复跑呈现间歇结果：有效成功轮约 0.55–0.60 秒后四窗格变为 `4,4,4,4`，父观察器收到四个
`0x2000`（UPDATEDIR）及一个通用/未知事件；四个 view HWND 均有新增消息，其中可见 `0x4AE`、
`0x4C8`、`0x4CF`。一次带完整阶段差分的部分失败轮 3 秒后为 `3,3,4,3`：文件系统已报告四次
CREATE，父观察器只有部分目录事件（未见 pane 0/3 更新），四个 view HWND 的差分也不相同：pane 2
出现多条私有消息、pane 1/3 没有差分，pane 0 仅有一条 `0x4A3`。显式刷新 pane 0 后为
`4,3,4,3`。此前另有两轮在新增阶段差分之前分别停在 `3,3,3,3` 和 `3,3,3,4`；这两轮只支持
自动更新失败或局部失败，不能补充消息收件证据。显式刷新用例中三项选区与 `initial.txt` 焦点均保持；
这只证明刷新恢复行为，不证明自动刷新根因已解决。

message-only 观测在同 STA 的 `WorkerW` 窗口看到 `0x4A0`/`0x4B5` 组合或 `0x8000`；成功和失败
状态并非一一对应。另有两次批量选择准备失败和一次 `SHChangeNotifyRegister` 返回 0，均在文件操作
前退出并排除于有效轮次；原样重跑后选择建立成功。窗口归属和自定义消息语义均未映射，不得据此推断
通知已经进入或被某个 ExplorerBrowser 消费。当前证据支持自动更新存在间歇性，且至少一个完整失败轮
显示文件系统操作完成、父观察器只收到部分目录事件、视图更新也部分发生；尚未指出是系统投递、订阅
边界还是隐藏条件造成。

### 多选与操作范围矩阵

为拆分两个变量，HostProbe 增加以下隐藏模式：

| 模式 | 操作前选区 | IFileOperation 目标 | 观测窗格 | 有效样本结果 |
|---|---|---|---|---|
| `--live-shell-refresh` | pane 0 选中三项 | 四个窗格 | 四个窗格 | 有成功，也捕获到 `3,3,4,3` 部分失败 |
| `--live-shell-refresh-unselected` | 无选区 | 四个窗格 | 四个窗格 | 当前有效轮次为 `4,4,4,4` |
| `--live-shell-refresh-one-pane` / `--live-shell-refresh-one-pane-3` | pane 0 / pane 3 各选中三项 | 仅目标窗格 | 四个窗格 | pane 0 四个、pane 3 一个有效轮次均只更新目标窗格 |
| `--live-shell-refresh-one-pane-unselected` / `--live-shell-refresh-one-pane-unselected-3` | pane 0 / pane 3 无选区 | 仅目标窗格 | 四个窗格 | pane 0 四个、pane 3 一个有效轮次均只更新目标窗格 |
| `--live-shell-refresh-one-pane-3-single` | pane 3 仅选中 `initial.txt` | 仅 pane 3 | 四个窗格 | 有成功也有失败；单选不能排除该问题 |

单窗格操作在 pane 0 与 pane 3 都只让目标视图更新；pane 0 有选区和无选区的当前有效轮次均通过。
pane 3 无选区用例有效轮次 6 次均在约 0.52–0.54 秒更新；但有选区用例出现间歇失败：仅选中
`initial.txt` 时也捕获过失败，所以问题不能归结为多选。一次带两个独立 STA Shell 观察器的 pane 3
失败轮中，文件系统已确认 `created.txt` 创建；主 STA 与独立 STA 均未收到根目录事件，pane 3 只增加
一条未解释的 `0x4A3`，仍有 3 项。显式刷新后文件才出现在视图。更早的三项选区失败也有同样的“磁盘
已改、视图未更新”结果。该轮排除了“只有主 STA 消息循环漏收”这一解释，但还不能区分 Shell 未产生
通知、共同过滤条件或视图缓存状态。全窗格操作无选区的有效轮次成功；有选区时既有成功也有部分失败。

后续单目标重跑说明以上计数只是当时样本：pane 3 无选区同一观察器模式后来又有 2/3 有效轮未自动更新；
本轮新跑 5 个有效无选区样本和 5 个有效三项选区样本均自动更新，另各有注册失败样本在操作前排除。
因此失败仍是间歇性的，不能用某个最新连续通过批次覆盖之前的反例。另一个已记录失败中，主观察器实际
收到 `pane-3\created.txt` 的 `SHCNE_CREATE`，但视图仍保持 3 项，仅出现 `0x4A3`；所以也存在
“CREATE 已到父观察器、视图却未枚举新增项”的失败形态。

新增 `--live-shell-refresh-one-pane-unselected-3-filesystem-control` 作为诊断控制：使用 `File.Copy`
创建隔离测试文件，并在文件已由系统自动枚举后，对唯一 fixture 目录以 `SHCNE_UPDATEDIR` +
`SHCNF_PATH | SHCNF_FLUSH` 再发一次通知。父观察器收到 `0x1000`（UPDATEDIR）并跟随收到 `0x2000`
（UPDATEITEM），pane 3 保持 4 项。它验证了路径参数形式的诊断通知能被投递，但视图当时已经更新，
不能说明该通知能否挽救“磁盘已有文件、视图仍旧”的失败。一次 PIDL 参数形式的尝试没有观察到投递；
不把该无效对照用于产品结论。后续单选 pane 3 重跑中，10 次里有 3 个有效失败样本：其中一轮的父观察器
看到 `UPDATEDIR` 和后续针对 `created.txt` 的 `CREATE`，两者都未使视图从 3 项变为 4 项；另一轮目录通知
本身进入父观察器但视图仍旧；还有一轮未观察到 Shell 事件。两次显式刷新均恢复列表。`ICommDlgBrowser3`
的 `Notify` 在这些成功/失败轮次都没有回调，不能把它当作 Shell 文件变更事件通道。

新增 HostProbe-only 的 750 ms 延迟对照模式 `--live-shell-refresh-one-pane-3-single-fallback`、
`--live-shell-refresh-one-pane-3-multiselect-fallback` 和 `--live-shell-delete-selected-pane-3-fallback`：
在 fixture 的 `FileSystemWatcher` 收到目标目录创建/删除后，
检查原生视图项数与目录项数；只有不一致时才调用现有 `ExplorerBrowserHost.Refresh()`。单选创建样本中捕获
一次需要兜底的情况：视图仍 3 项、刷新后为 4 项，选区和焦点身份保留；成功自动更新轮没有额外刷新。
删除样本 7 个有效轮中 2 次需要兜底，删除后剩余选区和焦点均保留；其余轮原生视图已是 2 项，没有额外
刷新。多选创建测试的 4 个有效样本均自动更新，尚未触发需要兜底的分支。该实验表明“延迟检查差异后调用现有刷新”
是可行的候选恢复机制，但样本量仍小；尚未覆盖大目录、慢速/网络文件复制、重命名、虚拟 Shell 位置或真实
窗口，也没有把监视器接入产品代码。

`SHChangeNotifyRegister` 注册失败、批量选择准备失败发生在文件操作前，均作为无效轮次记录；高频重复
启动时能连续遇到注册失败，后续重复需拉开时间，不能把它们当作刷新失败或忽略其对样本数的影响。

这些测试直接使用 `IFileOperation`，没有设置或读取用户系统剪贴板，也没有调用生产的 Paste 命令。源码路径
已核对：键盘先把消息交给 `IShellView::TranslateAccelerator`；Shell 文档要求视图未处理时返回 `S_FALSE`，
由宿主继续按普通 accelerator/menu 路径处理。项目对未处理的 Ctrl+V 向当前 `SHELLDLL_DefView` 发送
`WM_COMMAND`，命令值为 `0x701A`。菜单和工具栏另取当前视图的后台 `IContextMenu`，用 canonical `paste`
命令映射出的菜单偏移调用 `IContextMenu::InvokeCommand`。两条路径最终都会访问系统剪贴板，因此未在本轮
执行它们；只凭直接 `IFileOperation` 结果不能断言实际 Ctrl+V 已修复。

下一步把“延迟检查只在视图与本地目录不一致时刷新”的机制作为产品候选，先补齐监视器生命周期、事件合并、
导航竞态及大目录成本的设计，再用隐藏生产宿主验证创建/删除/重命名及焦点状态。保持 Native Shell 通知为
主要路径，并确认候选只修复遗漏，不在正常更新轮重复刷新；之后再追踪可安全隔离的 Paste 数据对象测试。
不碰用户剪贴板、不操作 GUI、不发布。

参考：[IShellView::TranslateAccelerator](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-translateaccelerator)、
[IContextMenu 接口实现与 InvokeCommand 参数](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-implement-the-icontextmenu-interface)。

### 2026-10-23 产品级本地目录自动刷新兜底

先为 `ShellDirectoryChangeBatch` 增加状态冒烟，再写实现并观察到预期失败；绿色用例覆盖原生视图已更新时不额外刷新、创建/删除漏更时要求兜底、同一批创建和删除抵消、重命名与 watcher 错误强制刷新，以及兜底后基准数推进。随后将该状态逻辑接到生产 `ExplorerBrowserHost`：目录导航稳定且标签视图状态恢复后启动监视器；目录变化到达后以 750 ms Dispatcher 定时器合并；计数相符则不刷新，不相符才进入等待判断，最终调用现有带选区/焦点快照恢复的 `Refresh()`。重命名和 watcher 错误直接要求刷新。新导航会使旧回调失效并释放 watcher；浏览器销毁也会释放资源。监视器针对当前已打开的本地固定磁盘目录，非递归；网络盘、UNC 和搜索结果不启动监视器。

检查通过：`dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release`；
`dotnet build src/ExplorerDirs.App -c Release --no-restore`；
`dotnet build src/ExplorerDirs.HostProbe -c Release --no-restore`。并行构建 App 与其 WPF smoke 一度争用 `obj` 下生成的 XAML 源文件；串行重跑后均通过，属构建并行冲突而非源码错误。

新增隐藏生产宿主模式 `--live-shell-product-monitor`，实际用隔离 fixture 文件测试创建、重命名及删除；另在真实创建后合成 `Renamed` 信号，以确定地进入生产 debounce/fallback 分支。三次有效运行均通过：fallback 计数为 1、3、1（计数较高的那轮也覆盖了真实重命名和删除事件），刷新后选区和焦点均保留，重命名及删除均在视图可见。该强制分支测试证明接线和刷新状态恢复；不能代替自然发生的“Shell 漏更”产品复现。前一模式 `--live-shell-refresh-one-pane-unselected` 也通过，四窗格只目标窗格更新。测试使用后台隐藏 HwndSource，不显示窗口、不调用 GUI、不使用剪贴板。


### 2026-10-23 大批量目录变化的等待窗口复核

首轮 3,000 文件压力用例最终可见计数正确，但删除阶段一次 fallback 读取到 1,380 项；这说明“计数连续 500 ms 不变”不能证明 Shell 枚举已经完成。将状态逻辑改为：少量变化累计最多等待 3 次检查（初次不匹配后再等两个 500 ms 间隔），大批量净变化（绝对 delta 至少 32）最多等待 12 次检查（初次不匹配后最多再等 11 个间隔）；无论计数是否变化，窗口到期后仍触发 fallback。重命名与 watcher 错误绕过等待。新增的 smoke 覆盖短暂稳定、持续变化和最终超时，先失败后通过。

验证：Settings smoke 通过；App 与 HostProbe Release 构建均为 0 警告/错误。更新后的隐藏产品压力轮多次结果不同：一轮创建 1,639 ms、删除 2,623 ms，创建和删除各触发一次 fallback，删除触发时探针报告操作仍运行；另一轮两次 fallback 后最终收敛；新增时间线诊断的一轮创建 5,182 ms、删除 7,482 ms，创建阶段未 fallback，删除阶段一次 fallback（`expected=0, actual=395`，操作已完成）。最后一轮 1.2 秒观察点已分别正确达到 3,003 和 3；各轮最终磁盘/视图数均为 3。对照 watcher 约收到 2,998–2,999/3,000 个创建和 3,000 个删除通知，没有报告溢出。native-only 单轮没有 fallback，1.2 秒观察点计数为 2,969 和 1,154，随后自然达到目标。单轮时长受环境调度影响，仅作边界证据，不用于宣称性能优劣。`expected=0` 说明刷新完成时重设的基准数仍可能处在 Shell 异步枚举过程的中间状态；时间线复核应继续覆盖“刷新后重设基准、后续事件累计”这一段。

这轮证明了最终收敛和大批量延迟期间的 UI Dispatcher 心跳仍在推进（最大观测间隔 39–585 ms），没有证明 fallback 时机合理，也没有测试选区/焦点在 3,000 项刷新时保留。下一步应改进压力探针记录每次刷新开始/结束、Shell 计数时间序列与操作完成关系，特别是 fallback 后基准重设是否过早；再判断长操作采用何种恢复策略。确认前不关闭 P0、不发布。继续不使用 GUI、不碰用户剪贴板。

### 2026-10-23 保留 fallback 刷新期间的新目录变化

源码跟踪确认一处确定的状态竞态：`Refresh()` 返回后才异步读取 Shell 项目数；其间 FileSystemWatcher 仍能把后续变化累加到当前批次，但回调原先无条件 `Rebase(actualCount)`，会同时清除这些尚未核对的事件。如果读取时 Shell 视图还处于枚举中，旧事件估算和新计数基准都会丢失。现在用 `CompleteRefresh(actualCount)` 对照刷新前的目标数及刷新期间累计的事件：完全匹配时才清空并重设基准；不匹配时保留批次并重新检查，最多再做一次 fallback，仍不匹配则接受当前计数以限制重复刷新。大批量计数差也采用延长等待。对应状态回归覆盖刷新期间删除、计数不符、rename 强制状态和两次刷新后的终止；测试先因缺少新状态转换而失败，再通过。

验证：Settings smoke 通过；App/HostProbe Release 构建 0 警告/错误；隐藏生产宿主创建/强制 fallback/重命名/删除通过，fallback 后选区和焦点保留。一次最新 3,000 文件产品监视压力轮创建 1,658 ms、删除 1,201 ms，1.2 秒观察点 Shell 计数分别为 3,003 和 3，最终磁盘/视图为 3；该轮未触发 fallback，Dispatcher 心跳最大间隔 62 ms。先前同代码核心修复、时间线探针修正前后的压力轮仍出现 1–2 次 fallback，计数也有短暂滞后，所以这轮只证明一个无 fallback 的自然通知场景通过，不能证明漏更兜底和重试路径全部稳定。当前探针记录 fallback 发生时的摘要，但逐次 Shell 计数/刷新开始结束时间线仍不足；继续补足该证据并验证高频下的选区/焦点后，才能评估是否关闭 P0。仍未操作 GUI、剪贴板或发布。

随后将 3,000 项压力探针扩展为：开始前选中 `second.txt`、`third.txt`，将焦点独立设在未选中的 `initial.txt`；在创建 3,000 项后和删除后分别读取 PIDL 选区及焦点项。最新一轮创建/删除耗时 3,822/3,909 ms，两个阶段 `selection-restored=True` 且 `focus-restored=True`，最终磁盘/视图回到 3 项；删除阶段触发一次 fallback，时间线捕获 `expected=0, actual=3, operation-complete=True`，无 watcher 溢出，Dispatcher 最大心跳间隔 265 ms。此结果把高频选区和焦点恢复从未知推进为一轮隐藏压力通过，但 `expected=0` 与实际计数 3 相同，表明 watcher 估算在大批次中会造成不必要的 fallback。还需要重复压力和自然漏更样本，不能据此认定刷新判断已正确。

### 2026-10-24 等待 Shell 初始项目计数稳定

生产 watcher 的创建/删除事件计数在一次压力轮中均为 3,000/3,000，但首轮 Shell 基准仍为 0，导致在快速创建再删除后得到 `expected=0, actual=3` 并触发两次 fallback。源码追踪显示 `IFolderView::ItemCount` 在导航完成后可以返回成功 HRESULT 和 0，但 Shell 原目录项目仍在加载；因此单纯重试 `null` 不会捕捉这个有效但暂态的结果。

现在 watcher 启动前每 250 ms 读取一次 Shell 计数，要求连续五次相同（约 1 秒稳定观察），最多观察 40 次；超时则不建立错误基准，也不启用 watcher。导航版本和路径在每次检查时复核，离开目录会停止启动计时器。隐藏 HostProbe 现在等到基准可用后才开始受测文件操作，并输出起始计数、等待次数以及产品 watcher 与独立观察 watcher 各自的事件数。

验证：Settings smoke 通过；App/HostProbe Release 构建 0 警告/错误。常规隐藏产品监视测试等到三项初始基准后通过（创建、合成重命名 fallback、重命名及删除均可见，选区/焦点恢复）。随后两轮 3,000 文件压力测试都确认 `monitor-start-count=3, monitor-start-samples=5`；生产 watcher 每轮收到 3,000 创建和 3,000 删除，两轮创建/删除最终均可见，两项选区和独立焦点在两个阶段均保留，均未触发 fallback，Dispatcher 最大心跳间隔为 65/72 ms。Shell 计数相对磁盘操作的可见延迟为 123–1,112 ms。此前零基准造成的两次 fallback和创建后的焦点丢失没有在正确稳定基准下重现。自然漏更 fallback 重试尚需在正确初始基准下验证；也未进行真实 GUI 验收。

在正确的三项基准下，常规隐藏宿主又增加了确定性计数不匹配场景：先真实创建到四项并等批次稳定，再人为给预期计数加一但不改目录，让生产 Dispatcher 检查按计数差进入 Refresh。该差值持续存在时，记录到恰好两次 fallback（首次加一次有界重试），第二次后批次基准为四；多选与独立焦点保持。之后真实重命名产生的 `Renamed` 信号另触发 fallback，重命名项及删除项均可见。输出：`fallback-refreshes=3 mismatch-fallbacks=2 mismatch-recovered-count=4 selection-restored=True focus-restored=True rename-visible=True rename-fallback=True delete-visible=True`。这是产品计时器和生产宿主在确定性 mismatch 下的证据，不等同于自然 Shell 漏更；自然遗漏仍需单独复现。该 probe 只改隔离 fixture 和进程内状态，无 GUI/剪贴板操作。

随后在另一个刷新周期测试 in-flight 队列竞态：注入 mismatch 后，后台 probe 线程轮询到生产 `_directoryRefreshInProgress`，立即经生产 `QueueDirectoryChange` 排入 `delta=-1`，竞争 `Refresh()` 的 Background 完成回调与 watcher 的 ContextIdle 分发。新增的批次完成入口先 Drain 队列，再读取 Shell 计数；探针检查本次刷新确实排空 `-1`，预期仍为四项，并在 2 秒额外观察期内只发生一次 fallback。最终输出 `in-flight-fallbacks=1 in-flight-drained-delta=-1`，选区/焦点及真实重命名/删除仍通过。该事件从探针调用生产队列入口合成，验证的是 Dispatcher 优先级与队列合并顺序；它不是文件系统自然通知或自然 Shell 漏更的复现。

### 2026-10-24 生产监视器接入 IFileOperation 复制

先前 `--live-shell-product-monitor` 用普通 `File.Copy` 验证生产 `FileSystemWatcher`；既有 `IFileOperation` 探针则使用测试专属 fallback，不能说明生产 watcher 是否接到实际 Shell 复制事件。现在同一隐藏四窗格生产宿主改用诊断 helper 的真实 `IFileOperation::CopyItem`，仅操作唯一 fixture 中的 `initial.txt`，复制为 `product-created.txt`，不访问剪贴板。

最初八轮均为 `copy=IFileOperation watcher-created-events=1`，其中五轮创建后未触发 fallback、三轮触发 1 次。为定位偶发情况，探针增加读取生产计时器最近一次刷新时 `expected/actual/forced` 的诊断字段；两次触发记录为 `expected=4 actual=3 forced=False`。此前探针只在 `WaitForExpectedCounts` 观察到 4 项后，才读取 fallback 计数；因此不能据此断言是自然视图先到 4、随后又回到 3。之后的采样组三轮每 100 ms 读取一次直接视图与生产计数，均为 `4/4` 且无 fallback；但该采样改变了 Dispatcher/COM 时序。

为减少轮询干扰，随后把 ShellBrowserSite 通知快照拆成创建前、首次观察到 4 项时、静默期结束后三个点。该低扰动时序连续八轮中，四轮在首次观察到 4 项时已发生 fallback，且刷新记录为 `expected=4 actual=3`；另外四轮到达 4 项时没有 fallback。前一组很可能是刷新后才观察到 4 项，而后一组符合自然 Shell 视图更新。快照只记录累计的浏览器状态回调数，且刷新本身也会产生回调，所以不将回调计数增量解释为自然通知时序。当前证据更支持“Shell 视图自然更新有时及时、有时迟到，FileSystemWatcher 计数兜底在迟到样本中先刷新恢复”，而非已证明的 `4→3` 回退；隐藏宿主仍不能证明可见窗口中的频率或用户可见漏项。

这轮同时暴露出 HostProbe 的一个竞态断言过窄：它要求 `-1` 增量必须由 `Refresh()` 的完成回调直接 Drain，但 Dispatcher 的 `ContextIdle` 队列可能先 Drain 并合并到同一批次。一次运行中功能状态已恢复到预期 4 项且只发生一次 fallback，只有 `LastRefreshDrainedDelta` 为 0，导致探针失败。现在断言最终预期计数和 fallback 次数这两个外部行为不变量，Drain 所在入口只作为诊断值保留。修改后连续八轮都通过；其中一轮最后一次完成回调 Drain 为 0，其余为 -1，均恢复预期 4 项。该修正只调整 HostProbe 诊断断言，不改变产品刷新逻辑。

随后给复制操作、watcher 首次收到事件、视图首次达到 4 项加上单调时钟时间戳，不在等待中主动读取 Shell 计数。扩展至 27 次复制观察：每轮 IFileOperation 耗时 52–248 ms，watcher 事件在 73–327 ms 内到达。18 次没有 fallback，视图在 549–609 ms 首次达到 4 项；另外 9 次在首次看到 4 项前触发 fallback，视图于 1,897–2,834 ms 才达到 4 项，刷新前均记录 `expected=4 actual=3 forced=False`。单轮示例 `(IFileOperation 耗时, watcher 事件时间, 视图首次显示时间)` 为 `(52,81,549)`、`(117,142,2127)` 毫秒。该分布在本机隐藏宿主中呈现为自然快速更新和 fallback 后较迟可见两组；不说明 fallback 是否造成等待，因未触发 fallback 的对应反事实无法同时观察。当前数据支持有界等待会处理迟到样本，但仍需可见窗口验证用户感知，不能仅凭这些数据调整 750/500 ms 定时策略。

其中一轮复制阶段自然在 565 ms 显示 4 项、未触发 fallback，但随后合成计数 mismatch 的完整探针报错：重试后批次 `ExpectedCountAfterChanges` 意外变成 0，而预期应为 4。失败时没有保留刷新回调的实际计数，因此目前不能断言是产品暂态空视图还是探针/调度竞态。失败诊断已补充当前 Shell 计数、批次值、最近一次刷新预期/实际计数及 fallback 次数；之后 16 轮完整探针均通过，没有复现。该偶发结果未被当作已修复，也未据此改产品逻辑；继续保留为待复现的 P0 风险。

为让下次复现能定位来源，给 `ShellDirectoryChangeBatch` 增加仅供内部诊断的 `LastRefreshExpectedCount` / `LastRefreshObservedCount`，并在失败信息输出这两个字段和该次完成回调 Drain 的 delta。Settings smoke 先因诊断属性缺失而编译失败，属性及记录代码加入后通过。更新后又连续 24 轮完整隐藏探针通过；最新三轮记录的刷新回调计数均为 `expected=4 observed=4`，包括复制本身触发 fallback 的样本。该记录不改变刷新决策；那次批次变 0 仍未复现，原因未定。

同时将重命名验证从普通 `File.Move` 改为诊断 helper 执行的 Shell `IFileOperation::MoveItem`，再由 `PerformOperations` 执行；删除也从 `File.Delete` 改为 Shell `IFileOperation::DeleteItem`。helper 仅接受 fixture 内的文件名，验证移动源消失/目标内容正确及删除目标消失。三轮完整验证中，生产 watcher 每轮观察到强制重命名 fallback（`DirectoryChangeLastForcedRefresh=True`）和一个 Deleted 事件；新名称在 Shell 视图中可见，删除后视图均回到三项。DeleteItem 使用当前静默诊断标志，不启用回收站/撤销。此项覆盖同目录 Shell move/rename/delete 的生产监视链，不覆盖剪贴板移动、跨窗格拖放、撤销/回收站行为或可见 GUI。

随后新增跨窗格目录的 `IFileOperation::CopyItem` 对照：只接受四窗格隔离根目录下的两个不同 fixture 文件夹和文件名，验证源文件内容保留、目标副本内容正确，随后用 `DeleteItem` 清理目标项。连续三轮完整隐藏生产探针通过：目标窗格 watcher 收到创建/删除事件且视图计数按 3→4→3 更新；源目录的创建 watcher 计数不变，其他两个窗格计数不变。操作直接使用 Shell API，不读取或修改系统剪贴板，因此不等同于应用 Ctrl+V/Paste，也不覆盖拖放。

本轮继续验证跨窗格移动：在源窗格用 Shell copy 建立专用 fixture，再通过 `IFileOperation::MoveItem` 移到目标窗格，最后用 `DeleteItem` 清理。连续三轮中源 watcher 都收到 Deleted、目标 watcher 都收到 Created；源/目标视图按 `5/3→4/4→4/3` 收敛，另外两窗格保持 3 项。每轮跨窗格 move 和清理均通过。三轮开始时同目录复制的自然视图更新时间分别为 568、1,887、543 ms；中间一轮在 Shell 视图首次显示前触发一次非强制 fallback（`expected=4 actual=3`），另外两轮未触发。这个迟到样本与此前复制观察一致，继续说明 watcher 兜底仍在起作用，不能据连续成功宣称自然通知稳定。

为关联迟到样本，诊断开关下新增可重置的 Shell 子视图 HWND 消息样本时间戳，并记录生产 fallback 的开始/完成时间；时间戳只在 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 时采集。六轮另外挂接不解析 PIDL 路径的父级 `SHChangeNotify` 观察器：四轮父级观察器收到原始事件 `0x40000` 和 `0x2`（约 531–546 ms），随后子视图约 533–548 ms 收到 `0x4AE`，视图约 538–569 ms 可见；另有一轮父级观察器没有事件但视图仍于 581 ms 自然更新。第六轮复制操作 49 ms、生产 watcher 77 ms 收到创建，父级观察器无事件且无 lock failure，子视图只在约 59 ms 记录到 `0x4A3`；生产 fallback 于 1,820.6 ms 开始，约 1,887.8 ms 出现下一组子视图消息并显示文件，fallback 于 1,900.2 ms 完成。上述 `0x4A3/0x4AE` 等私有消息语义未映射；样本提示父级通知缺失和视图迟到可能相关，但“父级观察器无事件”也出现在一次自然更新轮，因此不足以定位根因。额外挂接观察器可能改变调度，六轮不用于估算迟到率，也不据此调整产品定时策略。

随后增加 `--live-shell-product-monitor-no-parent-observer`，用同一隐藏生产宿主/复制/视图消息/fallback 计时对比是否挂接父级观察器。该入口最初漏建每个 fixture 的 `second.txt`/`third.txt`，因此两次初始化失败均不计样本；修正入口后，两组有效观察器开启/关闭数据分别为 `552/536 ms` 和 `543/593 ms`，均未触发 fallback。样本量小，当前看不出观察器造成稳定方向的时序变化；此前未挂观察器时也捕获过约 1.9 秒迟到轮，不能以本轮四个快速样本推翻。后续只在需要时保留此 A/B 开关，不扩增绿色样本作为“已解决”的依据。

HostProbe Release 构建 0 警告/错误；扩展计时样本中 26/27 轮的完整检查通过；另有一轮上述合成 mismatch 恢复断言未通过。后续在增加刷新回调计数记录后，24 轮未复现；新增同目录 move/delete 和跨窗格 copy/delete 后各连续三轮完整检查通过。下一步继续在更宽的 Shell 操作场景中验证刷新基准，并保留批次变 0 的失败诊断；不把隐藏宿主结果当成可见窗口验收。P0 继续开放。

### 2026-10-24 精确 CREATE 通知控制组与批量合并接入

早期生产通知候选样本不足且结果不稳定，曾暂时移除。为判断通知是否真的能加速，而非恰好撞上自然更新，再运行同一隐藏四窗格生产宿主、同一 `IFileOperation::CopyItem`、确认通知前视图仍为 3 项的交替控制组：显式 `SHCNE_CREATE | SHCNF_PATH | SHCNF_FLUSH` 12 轮中，9 轮在操作开始后 93–200 ms 显示第 4 项，3 轮仍于 1,855–1,893 ms 由 fallback 补齐；不额外发送通知的 12 轮中，0 轮在 250 ms 内显示，10 轮于 540–561 ms 自然显示，另 2 轮于 1,842–1,860 ms fallback 后显示。另有 3 轮目录级 `SHCNE_UPDATEDIR` 交替样本结果混合，不能作为更快路径。以上均为本机隐藏宿主样本，不代表所有磁盘、Windows 版本或可见 GUI。

该控制组支持在文件创建后补发精确 Shell 通知，但逐个文件同步 `SHCNF_FLUSH` 不适合批量场景：临时逐事件接入后，3,000 个创建耗时约 6.5 秒，Dispatcher 最大心跳间隔也达到 6.5 秒；fallback 在创建仍进行时介入，并导致创建后的选区及焦点丢失。移除此逐项发送后改为 STA 上 60 ms 静默合并，取批次最后一个文件路径；计数检查确认 Shell 视图仍落后时，才发送一次通知。这样单文件复制隐藏探针在约 133 ms 显示，发出 1 次通知且未触发 fallback；完整生产监视探针中的重命名、删除、跨窗格复制/移动、计数恢复及选区/焦点检查也通过。

合并后的 3,000 文件创建/删除压力探针连续三轮通过：创建约 1.42–1.86 秒、删除约 0.76–2.85 秒；三轮最终视图均到达 3,003 项并恢复到 3 项，创建/删除后选区和焦点均保留，Dispatcher 最大心跳间隔为 62–80 ms。前两轮在首次读取时已到目标计数且未触发 fallback；第三轮首次仍为 3 项，在操作完成后约 5.1 秒由一次 fallback 补齐。后两轮的计数确认均只发出 1 次 Shell CREATE 通知，生产 watcher 收到 3,000/3,000 事件。路径策略排除文件夹、相对路径和 `MAX_PATH` 之外的路径，Settings smoke 覆盖这些边界及批次只保留一个最新路径。App/HostProbe Release 构建 0 警告/错误。

当前正式实现是“延迟合并的精确文件通知 + 计数兜底”：受控 stale 样本显示通知可在多数轮次提早更新，批量测试确认不会逐项同步 flush；有个别轮次 Shell 仍未对齐，既有 fallback 仍负责恢复。剩余验证包括慢速/网络复制、跨卷操作、文件夹新建及真实可见窗口；不发布新包，自动刷新 P0 仍开放。

### 2026-10-24 文件夹与跨卷 Shell 操作补验

最新 `--live-shell-product-monitor-no-parent-observer` 重跑覆盖同目录复制、移动、删除及 `IFileOperation::NewItem` 建文件夹/`DeleteItem` 删空文件夹。文件夹创建和删除都收到生产 `FileSystemWatcher` 事件且最终在视图可见；创建没有误发非文件夹 `SHCNE_CREATE`。两个文件夹操作各触发一次计数 fallback，说明文件夹变化依靠现有计数恢复，不被精确文件通知覆盖。该轮还确认跨窗格复制/移动/删除、焦点与选区恢复；常规 Shell move 与删除可见。

另以隐藏四窗格宿主验证跨固定卷 `D:\`→`C:\` 的 `IFileOperation::CopyItem`、目标删除和 `MoveItem`，共三轮。三轮复制后源 watcher 均未增加、目标 watcher 均收到创建；删除后目标视图回到基准；移动后源 watcher 均收到删除、目标 watcher 均收到创建，文件内容及源/目标存在性检查一致，最终四窗格计数均为 `2/4/3/3`。第一轮 fallback 为 `0/0/0/0`，后两轮为 `1/2/0/0`，说明操作最终可见但不同窗格仍会依赖各自计数恢复。所有项都在隔离生成目录中操作并在宿主释放后清理，不使用剪贴板或可见 GUI。

验证命令：`dotnet build src/ExplorerDirs.HostProbe -c Release --no-restore`（0 警告/错误）、`dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release --no-restore`（PASS）、`dotnet run --project src/ExplorerDirs.HostProbe -c Release --no-build -- --live-shell-product-monitor-no-parent-observer`，以及三次 `dotnet run --project src/ExplorerDirs.HostProbe -c Release --no-build -- --live-shell-cross-volume`。完整产品轮中单文件复制仍约 1.9 秒由 fallback 更新，文件夹创建/删除也依赖 fallback；三轮跨卷操作最终均可见，但后两轮观察到源/目标窗格的 fallback。自然 Shell 更新延迟、重复压力及慢速/网络卷仍需验证，P0 不关闭；没有可见窗口验收或发布。

随后把目标 pane 改为本机 SMB 管理共享 `\\localhost\D$` 下的唯一 UNC fixture，执行同一组 Shell 复制、目标删除和移动。首次探针先按预期失败：Shell 能绑定，但生产 monitor 对 UNC 返回不创建 watcher。将路径判定扩展为允许 UNC 与 `DRIVE_REMOTE` 映射盘后，Release 探针 3/3 通过：UNC pane watcher 收到复制/删除/移动对应事件，目录计数依次为 4、3、4，最终四窗格为 `2/4/3/3`；两窗格 fallback 为 `1/2/0/0`、`1/1/0/0`、`1/1/0/0`，文件内容与源/目标存在性也匹配。测试使用当前机器的本地 SMB 回环管理共享，不代表远程服务器延迟、凭据变化、断线重连或离线盘；watcher 创建仍发生在 Shell STA，慢速路径的启动卡顿风险待测。没有打开可见窗口或发布。

同一探针再将 pane 1 设为本机 NTFS 目录 junction，Shell 浏览别名而外部文件 I/O 修改 junction 指向的目标目录。Release 生产宿主连续 3/3 轮确认目标 watcher 收到创建和删除，Shell 视图依次为 4 项、3 项并回到 `3/3/3/3`；pane 1 fallback 分别为 1、1、2。fixture 创建失败路径也加入了 junction 安全拆除和根目录清理。该结果证明本机目录 junction 下的变化可由当前 watcher/计数恢复观察，不覆盖目录符号链接、卷挂载点、循环链接、远程重解析点或跨卷 junction。

追加一轮完整 `--live-shell-directory-stress`：初始基准为 3（5 次稳定样本），创建 3,000 项用时 3,390 ms，操作完成时视图已达 3,003；删除用时 2,503 ms，首次读取为 861 项，随后 312 ms 收敛到 3。最终磁盘和视图均为 3，创建/删除全程没有 fallback；两阶段选区和独立焦点都保留，Dispatcher 最大心跳间隔 65 ms。生产 watcher 收到 3,000/3,000 创建/删除，独立诊断观察器收到 2,998/3,000 创建、无错误；这两个监听通道计数分别记录，不把诊断观察器的差值当作产品 watcher 遗漏。该轮补充一条大批量无 fallback 的自然更新证据，但单轮不能抵消此前 fallback 反例，也不证明所有负载下时机正确。

### 2026-10-24 文件夹 MKDIR 通知 A/B

Microsoft 的 `SHChangeNotify` 约定区分文件与文件夹事件：文件夹创建用 `SHCNE_MKDIR`，普通文件创建用 `SHCNE_CREATE`；`SHCNE_CREATE` 不应用于文件夹（[Microsoft API 文档](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)）。新增隐藏生产宿主对照模式 `--live-shell-product-monitor-early-mkdir` 和 `--live-shell-product-monitor-early-mkdir-none`，在 Shell 视图仍为 3 项时用 `IFileOperation::NewItem` 创建隔离文件夹，再分别发送 MKDIR 或不发送通知。

三组交替结果均确认通知前视图仍旧、Watcher 已收到文件夹创建，且最终视图和清理结果正确。无通知组 0/3 在 250 ms 内更新，fallback 次数为 `1/1/0`；MKDIR 组 1/3 在 250 ms 内更新（该轮 123 ms），另两轮未能在窗口内更新且最终由现有路径恢复，fallback 次数为 `0/1/1`。因此 MKDIR 的事件类型符合 API 约定，但这一小组样本没有显示稳定减少 fallback；不接入生产通知路径，文件夹刷新继续由现有 watcher/计数 fallback 保证最终收敛。没有操作可见 GUI 或剪贴板。之后若调整，需要扩大随机交替样本并同时记录父级 Shell 通知与视图到达时序，避免仅凭单个快样本改生产行为。

随后针对删除侧使用 `--live-shell-product-monitor-early-rmdir` / `--live-shell-product-monitor-early-rmdir-none`，先让隔离文件夹进入 Shell 视图，再通过 `IFileOperation::DeleteItem` 删除，并在确认列表仍旧后发送或不发送 `SHCNE_RMDIR`。五组交替样本中，RMDIR 4/5 在 125–141 ms 更新，1/5 未在 250 ms 内更新并由一次 fallback 补齐；无通知组 0/5 在 250 ms 内更新，五轮均最终正确，未触发 fallback。结果支持 RMDIR 是一个可加速文件夹删除的 Shell 信号，但不是可靠保证。

初次 A/B 后暂不将 RMDIR 接入通用 watcher 删除路径：.NET `FileSystemEventArgs` 提供 `FullPath`、`Name` 和 `ChangeType`，不包含已删除目标的文件/目录属性；删除后 `Directory.Exists` 也无法识别类型。若用 RMDIR 替代文件的 DELETE 会违反 Shell 事件语义，发两种通知则可能产生重复/错误刷新。随后 stale Shell identity probe 找到可用判定，产品实现见下方“产品 RMDIR 接入与批量删除查询”。微软定义见 [SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify) 与 [.NET FileSystemEventArgs](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemeventargs?view=net-10.0)。

后续隐藏探针在删除通知后、视图计数变化前枚举当前 `IFolderView.Items(SVGIO_ALLVIEW)`，按 `IShellItem.GetDisplayName(SIGDN_FILESYSPATH)` 匹配删除路径并读取 `SFGAO_FOLDER`。真实删除的隔离文件仍在 stale Shell view 中返回 `file`，空文件夹返回 `folder`；两例均在发送人工通知前读取，文件系统对象已不存在。这证明可以从 Shell 现存项身份而非磁盘路径识别类型。随后产品按最新事件路径合并后查询一次；10k 视图一项查询为 33 ms，128 个删除事件的代表路径查询为 3 ms。属性缺失、路径不匹配或视图已自然追平时不发送，继续依靠 count fallback。

### 2026-10-24 产品 RMDIR 接入与批量删除查询

产品 watcher 把 `Deleted` 路径合并 250 ms，最多查询最新路径一次。只有预期计数与实际视图计数不同，且该路径仍在 `IFolderView2` 中并经 `SFGAO_FOLDER` 确认为目录时，才发送一次 `SHCNE_RMDIR | SHCNF_PATH | SHCNF_FLUSH`。普通文件、相对/超长路径、Shell 项/属性不可读或视图已追平时均不发通知；count fallback 保持不变。Settings smoke 覆盖 batch 的 latest-path、drain 及路径长度/类型边界。

隐藏生产宿主 `--live-shell-product-monitor-early-rmdir-none` 连续 3 轮确认删除时列表仍有 4 项、watcher 收到事件且 stale 项为 folder；产品结果均为 `Notified`，视图于 watcher 后 359–373 ms 收敛，fallback 为 0。另有一次同模式发出 RMDIR 但仍由 fallback 收敛；完整 `--live-shell-product-monitor-no-parent-observer` 中，产品文件夹删除发出 1 次 RMDIR、folder-delete fallback 为 1，跨窗格复制/移动/删除、重命名及其余计数检查通过。RMDIR 是尽力加速信号，不能替代 fallback。

Release 版 HostProbe/Settings smoke 构建通过（0 警告/错误、smoke PASS）。Release 隔离 RMDIR 复跑有一轮未满足 stale-before-notify（视图先自然变为 3 项，作无效样本）；新 fixture 复跑后确认 stale folder、产品 `Notified` 1 次，视图在 watcher 后 454 ms 收敛且 fallback 为 0。

文件删除控制返回 `NotFolder`，RMDIR 通知数为 0。独立精确 `SHCNE_DELETE` A/B 仍无提速，普通文件继续依赖自然 Shell 更新或 fallback。

`--live-shell-directory-stress-large` 的 10,000 项轮次中，产品 stale-item 查询 1 次、结果 `NotFolder`。另外的批量查询对 128 个删除事件只查最后一个 watcher 路径一次，识别为文件用时 3 ms；产品 watcher 收到 10,000/10,000 创建与删除事件，删除最终回到 3 项，Dispatcher 最大间隔 82 ms。该轮按 `selection-restored=False` 退出码 1：Shell 自然删除清除了两项未受影响选择，焦点仍保留。没有可见 GUI 验收。

### 2026-10-24 文件删除 DELETE 通知 A/B

新增隐藏生产宿主探针 `--live-shell-product-monitor-early-delete` / `--live-shell-product-monitor-early-delete-none`：先在 pane 0 建立并确认测试文件可见，再删除该文件，等生产 watcher 收到 `Deleted` 后确认 Shell 仍显示旧计数、并通过 stale PIDL 查询确认为文件；随后发送 `SHCNE_DELETE` 或不发送，比较产品 750 ms 检查前的自然视图更新。6 轮交替样本均满足 stale-before-notify，watcher 在 26–48 ms 收到事件；DELETE 组和控制组都为 0/3 在 700 ms 内更新，且每轮均由一次既有 fallback 补齐，最终四窗格计数正确。发送精确文件 DELETE 没有显示可测的提速，不接入生产路径；保留计数 fallback。

同轮重跑 `--live-shell-directory-stress-large`：创建 10,000 个文件耗时 5,463 ms，删除耗时 2,539 ms；在 10,003 项视图中识别唯一陈旧删除文件，`ShellViewItemKind` 调用 33 ms，含 watcher 等待的整段耗时 76 ms。bulk delete 后磁盘与视图都回到 3 项，生产 watcher 收到 10,000/10,000 创建与 10,000/10,000 删除事件，未触发 fallback；未受影响的两项选择仍被 Shell 自然更新清空，焦点保持。HostProbe 因 `selection-restored=False` 按预期以 exit 1 结束，此轮不算通过。它测量的是 burst 前单项身份查询，不是“批量事件只查最后一个路径”的完整试验；批量路径限流尚未实测，也未接入产品。

### 2026-10-24 十千项压力与恢复边界

新增隐藏模式 `--live-shell-directory-stress-large`，将同一隔离目录创建/删除压力从 3,000 项提高至 10,000 项，并在 Shell 视图仍含目标项时测量已删除文件的类型查询。该轮 HostProbe Release 构建通过，Settings smoke PASS。实际创建耗时 6,232 ms，删除耗时 4,043 ms；最终磁盘、Shell 视图都回到 3 项，生产 watcher 收到创建 10,000/10,000、删除 9,999/10,000 事件，唯一陈旧项查询在 10,003 项视图中识别为文件，函数调用耗时 12 ms（包含 watcher 等待的墙钟耗时 37 ms）。

首轮还暴露一个间歇的选择恢复失败：创建 fallback 后探针读到原选择消失、焦点落在 `burst-0000.txt`，刷新快照属性最终为 0；但当时没有逐次记录每次刷新各自的快照，无法确认异常属于创建还是删除阶段，也无法判定选择在调用 `Refresh` 前是否已丢失。该轮最终计数正确，但因选择/焦点不符而失败。

此前探针只等待列表计数一致，可能早于异步刷新完成；现已改为同时等待刷新不在进行且目标计数与 Shell 计数一致。AGENTS.md 固化此断言顺序，避免将异步回调前的中间状态误报成最终结果。仍未进行可见 GUI 验收、发布或跨 Windows 环境验证。

为定位丢失时点，探针增加约 250 ms 一次的选择/焦点采样，以及每次 fallback 单独的刷新前快照记录。后续一轮 10,000 项再次出现三次 fallback（创建一次、删除两次）：三次刷新均捕获原来的 2 项选择和焦点；选择采样在每次刷新期间短暂读到 0 项，约 253–275 ms 后恢复。该轮创建耗时 5,464 ms、删除耗时 3,532 ms，最终视图/磁盘均为 3 项；stale 文件类型识别调用 12 ms、含 watcher 等待的墙钟 50 ms，Dispatcher 最大心跳间隔 314 ms。另两轮 10,000 项无 fallback，创建/删除后选择焦点均保留；最新 3,000 项复跑也通过，最大心跳间隔 79 ms。此前仍有一轮 10,000 项最终检查失败、快照报告为 0。证据说明 Shell Refresh 期间确有短暂失选，但多数轮能恢复；间歇失败尚未复现与逐次快照同步关联，不能归因为 Refresh 本身或关闭风险。下一步保留这组逐次记录，继续重复大目录 fallback 样本，并区分刷新过程内的暂态与回调后稳定结果。未据此改产品刷新策略。

探针同时修正了首次批量删除事件的观察基线：之前把此前单文件身份测试产生的删除通知计入基线，可能导致 `first-delete-observed` 假阴性；现改为在批量删除开始前采集观察器计数。本轮该断言为 true。`first-delete-observed` 属诊断观察器证据，不替代产品 watcher 的事件计数。

### 2026-10-24 10k 自然删除期间的选择丢失

再一轮带逐次捕获/恢复计时的 10,000 项运行中，创建阶段触发一次 fallback（刷新前计数 `9664`、预期 `10003`），原选择与焦点均被捕获，刷新恢复调用耗时 51 ms，创建后选择/焦点一致。随后批量外部删除 burst 项时，Shell 视图自然从 10,003 项收敛到 3 项，没有新的 fallback；但首个删除到达约 42 ms 后原来的 `second.txt`、`third.txt` 选择已消失，焦点仍是 `initial.txt`，删除结束后选择仍为空。最终目录计数正确，探针选择恢复断言为 false。

逐次采样看到创建 fallback 时短暂清选并恢复，而删除阶段此轮无刷新却清除了选择。这表明“计数最终相同”不足以证明选择状态保持；产品计数兜底无法检测没有计数差异的选区变化。当前操作是隔离目录中的外部 `File.Delete`，不能直接代表应用内 `IFileOperation` 删除体验，但确认了本机 ExplorerBrowser 对大量外部移除的选择行为。下一步以独立受控场景区分小批量/大批量、自然 Shell 通知和产品 fallback，并确认未受影响的原有选中项是否应保留；在明确语义前不增加无条件自动恢复，以免覆盖用户新选择。此次 Dispatcher 最大心跳间隔 183 ms；仅为单轮含诊断采样结果，不能作为常态响应指标。

随后缩为单文件 native-only 对照：使用同一隔离 ExplorerBrowser 宿主，停止产品目录监视器，不发应用 Shell 通知，也不调用 `Refresh()`。先由 `File.WriteAllText` 创建一个文件，选择仍为原来的两项；直接删除该未选中项后，Shell 视图短暂仍显示 4 项，PIDL 类型查询识别出被删文件，随后自然回到 3 项。自然计数收敛后，原来的两项选择已清空，焦点仍为 `initial.txt`。独立文件系统观察器记录到删除事件，产品 watcher 与 fallback 均为 0。该受控结果确认单个外部文件删除也能在隐藏 ExplorerBrowser 中清除未受影响选择；它尚未证明可见 Windows Explorer 主窗口或应用内 Shell `IFileOperation` 具有相同行为。

新增 `--live-shell-selection-delete` 直接以同一隐藏 ExplorerBrowser 宿主对照应用所依赖的 `IFileOperation`：产品 watcher 已停止，不发自定义通知、不调用 `Refresh()`。复制 `initial.txt` 到未选中的测试名后，原两项选择和独立焦点仍保留；再由 `IFileOperation::DeleteItem` 删除该未选中项，视图从 4 项回到 3 项后原选择变空、焦点仍保留，磁盘状态正确、fallback 为 0。宿主 `ICommDlgBrowser3::OnStateChange` 计数在删除阶段的 `CDBOSC_SELCHANGE`（state 2）增加 2 次。该结果把选择变化定位到 Shell 原生删除更新阶段，未见产品 watcher/fallback 参与；尚需在可见原版 Explorer 对照，确认是否属于标准 Shell 语义。

2026-10-24 再运行同一 `--live-shell-selection-delete`，结果重复：删除未选中项后 `second.txt`、`third.txt` 选择清空，`initial.txt` 焦点保持，`fallback-count=0`，删除阶段 `CDBOSC_SELCHANGE` 从 7 增至 9。紧接着完整 `--live-shell-product-monitor-no-parent-observer` 监视器中，单文件复制在 191 ms 自然显示、未触发复制 fallback；后续产品刷新恢复选区/焦点，跨窗格、文件夹事件及计数检查均通过。本次对照仍不能说明可见 Windows Explorer 是否一致，因此不在自然删除回调里无条件恢复旧选择。

### 2026-10-24 Ctrl+C/V 焦点恢复源码复核

键盘路径先调用 `IShellView::TranslateAccelerator`。Shell 返回已处理时，Ctrl+A/C/V/X/Z 与 Shift+Delete 会进入 `QueueFocusRestoreAfterFileKey`；若未处理，宿主对 `_viewWindow` 发送相应 `WM_COMMAND`（Copy `0x7019`、Paste `0x701A` 等），随后同样排队恢复。命令/工具栏路径通过当前视图的 canonical `IContextMenu` verb 执行，`InvokeCommand` 返回后排队恢复。两类路径现在会在 `ContextIdle` 立即尝试恢复，并进入最多 1.5 秒的有界观察期：首个 250 ms 检查后每 100 ms 重试，仅在同一视图、活动窗格和用户输入代数仍有效时执行；该策略仍未与 Shell 文件操作真正完成的通知关联。

`Alt+Tab` 后的窗口 `Activated` 回调在 `_fileViewFocusDesired` 为真时再次调用 `FocusCurrentView()`，这与用户报告的“切回两次后方向键恢复”相符。可能原因是 Ctrl+V 的异步 Shell 操作在两次既有恢复尝试之后才改变项目/焦点；但目前没有可见 Ctrl+V 的消息与焦点时间线，不能确认此因果，也不能确认 Shell 回调一定晚于 250 ms。隐藏 `IFileOperation` 复制不访问剪贴板，且不会复现此焦点症状，因此不作为 Ctrl+V 通过证据。

现有 `EXPLORER_DIRS_FOCUS_TRACE` 已记录按键进 `TranslateAcceleratorCore`、Shell HRESULT、fallback `WM_COMMAND`、`restore-check` 及 `FocusView` 前后 HWND/类名/项目状态。2026-10-24 扩展恢复诊断：每次尝试会记录 `MayRestoreFocus`、WPF 窗口激活、视图可见/就绪和前台 HWND；被门控跳过时记录原因，通过门控时记录列标题焦点、焦点资格及 `FocusView()` 后的实际焦点 HWND。这样未来时间线可区分“没有请求恢复”“恢复被门控”和“SetFocus 未到达内容控件”。App Release 构建与 SettingsSmoke 通过；这只是诊断接线，没有验证异步 Paste 完成时序，也没有改变恢复频率或触碰剪贴板。下一步在用户有空桌面并明确可测时，对隔离副本的一次 Ctrl+C/V 收集这条时间线。P0 焦点问题继续开放。

同一 100 文件产品监视对照中，创建 fallback 捕获并恢复了两项选择；之后删除一个未选中项，自然计数回到 102 项时选择已清空，焦点仍保留，未触发新的 deletion fallback。小批量与大批量样本均显示该问题并非仅由 10k 负载触发。下一步优先比较应用内 Shell 删除、外部单文件删除、外部批量删除的原生事件序列，并在可见验收时与 Explorer 对照；若确认是宿主中的 Shell 自然更新边界，再设计只恢复仍存在且未被用户新选择覆盖的条目。

### 2026-10-24 活动窗格显式刷新选区/焦点复验

为核实工具栏与 F5/Ctrl+R 共用的刷新动作对多窗格当前项的影响，分别运行隐藏生产 ExplorerBrowser 宿主：`--live-shell-refresh-one-pane-3-single` 与 `--live-shell-refresh-one-pane-3-multiselect-fallback`。两次都只对第四窗格执行真实 `IShellView::Refresh` 路径，其他三个窗格计数不变。单选场景刷新前后均选中 1 项，焦点均为 `initial.txt`；多选场景刷新前后均选中 3 项，焦点同样保留。第二场景的 750 ms 检查发现 Shell 已自然更新到预期计数，fallback 刷新实际次数为 0，因此该轮不是 fallback 行为证据。

两次隐藏探针均在隔离目录运行，无可见窗口输入、剪贴板访问或用户文件操作；支持“活动第四窗格显式刷新会保留当前选区与焦点”的生产宿主证据，但不证明可见工具栏点击、菜单焦点返回、Ctrl+V 完成后焦点或不同 Windows 环境行为。自动刷新大目录与真实用户输入 P0 仍开放。

### 2026-10-24 单项计数不一致的兜底延迟调整

`--live-shell-product-monitor-no-parent-observer` 再次复现单文件复制迟到：第一次样本复制用时 154 ms、watcher 在 184 ms 观察到创建，Shell 视图在 2,066 ms 才显示，生产计数 fallback 于 1,921.5 ms 开始。源代码在既有 750 ms watcher 静默期后，对小变更最多再做两次 500 ms 计数延迟检查。

将小变更延迟规则改为：净变化或计数差为 1 时，首次 750 ms 检查仍不一致便立即刷新；2–31 项保留一次额外 500 ms 检查；32 项以上保留原 12 次有界枚举检查。对应 Settings smoke 先新增单项立即兜底断言，确认其在旧规则下失败，再实现该策略；并保留 2 项延迟、3,000 项长枚举和刷新重试用例。

改动后的完整隐藏生产监视器样本中，单文件复制一轮在 fallback 开始 812.3 ms、视图 870 ms 可见；另一轮 Shell 自然更新于 543 ms 完成、没有 fallback。随后 Release 配置生产宿主自然更新样本为 134 ms，完整监视器 4 个 fallback、选区/焦点恢复及跨窗格 copy/move/delete、重命名和文件夹事件检查全部通过。两轮 Debug 完整监视器也通过：最终四窗格计数正确，`selection-restored=True`、`focus-restored=True`，跨窗格复制/移动/删除、重命名强制刷新、文件夹创建/删除与 watcher 检查通过。文件夹操作是否需要 fallback 仍随轮次变化。App/HostProbe Release 构建、Release Settings smoke 通过；此结果仅覆盖本机隐藏宿主，不证明可见 GUI、慢速/网络盘行为或所有小批量 Shell 枚举时序，自动刷新 P0 仍开放，未发布。

### 2026-10-24 删除未选中项探针无效轮

再次运行 `--live-shell-selection-delete` 时，复制后的 Shell 视图在 5 秒内未显示新项（`copy-visible=False`），但磁盘步骤已继续，最终报告 `delete-visible=True`。此轮选择保持，且 `CDBOSC_SELCHANGE` 计数没有变化；因此它没有进入此前确认的“Shell 原生删除更新清空选择”状态，是无效样本，不能作为修复或反证。Release HostProbe 重新构建为 0 警告/错误。既有两次有效运行仍是当前根因证据；下一步应先让探针在复制未自然显示时停止并清理 fixture，再取得有效复制基线后验证安全的选区恢复门控。当前没有产品行为改动，也没有可见桌面操作。

修正探针让复制未可见时立即以无效样本退出后，Release 复测形成有效对照：复制项在 Shell 视图可见后再由 `IFileOperation::DeleteItem` 删除；未选中的删除目标移除后，两项未受影响选区被 Shell 清空，`initial.txt` 项焦点保留，`CDBOSC_SELCHANGE` 从 7 增至 9，产品 fallback 为 0。该轮复现了既有问题。下一步实现“最近一次有效选择 + 用户输入代际”门控，仅在无后续用户输入且旧 PIDL 仍存在时恢复；先用纯逻辑回归验证门控，再跑同一隐藏生产宿主探针覆盖恢复和用户主动清选控制。可见 Explorer 对照仍缺，发布前仍需用户验收。

### 2026-10-24 Shell 原生更新后的选区保护

在 `ShellBrowserSite` 的选择变化回调中维护最近一次非空 PIDL 快照；嵌入 Shell 视图收到键盘、鼠标按下或命令消息时，递增输入代际并清除旧快照，待 Dispatcher 处理完本轮输入后再读取最终选择。自然更新清空选择时，仅当快照非空、输入代际未变、视图/导航仍匹配，且当前选择仍为空时，异步恢复仍存在的 PIDL。导航和显式 Refresh 会清除或暂停这类恢复，避免复用旧目录快照及与既有 Refresh 恢复竞争。

Settings smoke 对门控三个分支通过。隐藏生产宿主 Release 复现中，先确认复制项自然显示，再由 `IFileOperation` 删除未选中项：两项存活选择与独立焦点均恢复、fallback 为 0，连续有效复跑均通过。合成键盘输入后主动清空选区的控制场景保持为空。显式多选 Refresh、完整产品目录监视器及 3,000 项创建/删除压力场景也通过；压力场景 Dispatcher 最大间隔 80 ms，删除阶段选择和焦点保持。Release App/HostProbe 构建 0 警告/错误。

尚未覆盖可见 Windows Explorer 对照、真实鼠标清选/上下文菜单/不同输入设备、用户通过键盘启动删除操作时的选区语义、超大多选集性能和其他 Windows 版本。该实现只证明本机隐藏生产宿主场景，不发布；这些边界进入空闲桌面 GUI 验收清单。

### 2026-10-24 选择恢复探针改为稳定的删除基线

旧 `--live-shell-selection-delete` 先通过 `IFileOperation::CopyItem` 动态建立待删项目；本轮连续两次出现磁盘已复制但 Shell 视图在 15 秒内未增加项目，因此探针按无效基线跳过删除。为避免把准备阶段的自然枚举波动与选择恢复混在一起，现于创建 ExplorerBrowser 前预置未选中的 `selection-delete-target.txt`，确认四项视图基线后只删除该目标，再显式发送 `SHCNE_UPDATEDIR` 令 Shell 视图重新枚举。产品目录 watcher 在 pane 0 中停止，且该恢复路径不触发计数 fallback。

Release 两种隐藏场景均通过：删除未选中目标后，`second.txt`/`third.txt` 仍被选中，独立焦点 `initial.txt` 保持，磁盘目标已删除、Shell 计数回到 3、fallback 为 0。第二种场景在删除前经宿主 HWND 合成 Delete 消息并确认输入代数递增，之后同样保持选择与焦点。两轮原生 `CDBOSC_SELCHANGE` 通知从 9 增至 11；输入控制轮的 site 诊断未返回状态数字，但输入代数确认消息已到达。此证据验证显式 `UPDATEDIR` 后的原生视图重枚举及选择保护，不等同于此前观察到的无显式通知自然 watcher 路径，也不代表可见 Explorer 或真实键盘输入。

### 2026-10-24 用户输入后删除与大多选复测

继续验证上一节的输入代际门控：在复制项已自然出现在视图后，向隐藏 Shell 宿主发送合成 Delete 键消息（探针确认产品输入代际已递增），紧接着通过隔离 fixture 的 `IFileOperation` 删除未选中项。复制/删除视图均收敛、磁盘结果正确，原两项选择与独立焦点保留，fallback 为 0。此项验证“新输入已被记录后发生自然 Shell 删除”的恢复，不等价于向可见 DefView 发送真实 Delete 并确认系统对话框路径。

大选区场景额外创建 512 个文件，加原有 3 项后全选 515 项；再创建未选中目标并由 `IFileOperation` 删除。Shell 计数回到 515，全部选中项身份保持，fallback 为 0。操作到稳定读回墙钟约 1.28 秒，其中包含探针固定的 500 ms settle；未单独量出同步恢复耗时，不能据此保证大选区 UI 响应时间。该轮证明 515 项隐藏宿主恢复正确；更大多选规模、真实按键/上下文菜单和可见窗口仍待覆盖。

### 2026-10-24 大选区恢复的批量路径优化

将大选区探针扩大为 2,051 项并加入 2,000 ms 恢复调用门槛。旧版逐 PIDL 重复枚举/比较在有效隐藏宿主样本中耗时 11,404 ms，超出门槛；这把此前“选择正确但恢复可能阻塞”的风险转化为可复现性能失败。

恢复逻辑现先通过 Shell `SelectItem` 找到一个仍存在的快照项并清除当前额外选择，再使用 `IFolderView::SelectAndPositionItems` 一次传入 PIDL 数组，随后独立恢复项目焦点。批量接口失败时保留旧的逐项身份校验路径，因此快照包含已删除项目时仍有正确性回退。批量调用期间暂停逐项 `CDBOSC_SELCHANGE` 快照采集，完成后只采样一次，避免每次选择通知都重新枚举完整多选。

Release 隐藏宿主最终 2,051 项全部恢复，批量恢复调用 21 ms、探针墙钟 1,080 ms（包含固定 500 ms 稳定等待）、fallback 为 0，性能门槛通过。后续回归复跑再次通过：2,051 项前后计数一致、选区身份完全一致、删除可见、恢复 21 ms、fallback 为 0。显式三项多选刷新、删除一个已选项后保留剩余项与焦点、自然删除未选中项后恢复、用户输入后的删除、主动清选保持为空均通过；Settings smoke 与 Release App/HostProbe 构建通过，构建 0 警告/错误。个别 2,051 项夹具轮未在等待窗内自然收敛，探针按失败基线短路；成功轮已完整满足计数/选择断言。超大规模选区和可见 GUI 仍待验收。

随后尝试把压力夹具扩大到 10,000 项，首轮在视图已打开后逐项建档，ExplorerBrowser 几分钟仍未收敛；中止后把夹具调整为先生成文件、再创建 Shell 视图。第二轮发现导航计数断言仍写成 3，修正后 10,001 项（含一个未选中删除目标）在约 0.7 秒完成导航。冷调用选择 helper 会在每个 `CDBOSC_SELCHANGE` 中同步重新枚举累积选区，导致探针运行数分钟；实际产品会在用户输入未结算时跳过这类中间采样。探针现模拟输入代际并等待最终选区采样后再删除，避免把绕过产品输入门控的 O(n²) 路径当成真实 Ctrl+A 性能。

有效 Release 隐藏宿主样本：全选后排除预置删除目标，10,000 个选中项均被采样；由 `IFileOperation` 删除未选中目标后，10,000 个选中项身份全部保持，目录计数正确。两次 Shell 自然清选并触发保护的样本，恢复调用分别为 86/89 ms；其中一次墙钟 695 ms（含固定 500 ms settle），fallback 为 0，`CDBOSC_SELCHANGE` 前后各 10,004 次。另一次删除后 Shell 自己保留选区、没有发出新选择变化回调，探针记录 `native-restore-ms=-1`、`restore-not-needed=True`，墙钟 562 ms、fallback 为 0；该情况按正确行为通过，但不计入恢复耗时样本。探针正常退出会清理专属随机目录；早期被中止的轮次可能遗留在构建输出夹具目录。此结果把 10k 隐藏宿主验证从待测变为通过，但不代替可见窗口、真实键盘全选/删除及其他 Windows 版本验收。

### 2026-10-24 自动刷新产品监视器 Release 复跑

Release 隐藏生产宿主完整监视器通过。单文件复制 79 ms 完成，watcher 99 ms 收到创建；Shell 首次显示新项为 900 ms，计数 fallback 在 841.6 ms 开始、904.8 ms 完成，最终计数正确。没有父级 Shell 观察器介入；此轮仍显示自然更新延迟的个体差异，fallback 正常兜底。

同一轮 watcher 四窗格均收到 3 次创建；总 fallback 8 次（其中合成计数 mismatch 2 次、刷新期间增量 1 次），最后一次刷新读到 expected=4/actual=4 并合并 drained delta=-1。跨窗格复制/删除/移动、同目录移动/删除、文件夹创建/删除均可见；文件夹创建无误发文件 CREATE，删除使用一次 RMDIR 并由一次 fallback 完成。最后选区和焦点均恢复。该轮只证明本机 Release 隐藏宿主行为，不证明可见 GUI或其他文件系统。

### 2026-10-24 缩短本地目录自动刷新静默期

在 Release 产品监视器中增加文件 `SHCNE_CREATE` 的调用起止时间。一次正常样本于 108–112 ms 发送并返回，视图 122 ms 自然显示；慢样本于 138 ms 发送并返回，但视图直到 fallback 开始后约 90 ms 才显示（总计 915 ms）。另一次 900 ms 样本同样在 fallback 开始前已发出通知。结合消息时间线，证据排除了“通知排在 750 ms fallback 之后才被 Dispatcher 发出”这一假设；尚不能证明 Shell 为什么延迟处理已送达的通知。

随后用同一生产宿主做计时器 A/B：把 watcher 静默期临时设为 250 ms，三轮视图首次显示分别为 357、119、475 ms；两轮慢样本分别由 250 ms fallback 后收敛，一轮 Shell 自然更新且没有 fallback。三轮完整跨窗格/同目录操作、文件夹变化、选区和焦点检查均通过。将正式计时器的初始及复用间隔从 750 ms 改为 250 ms 后，以默认生产路径复跑三轮：视图分别于 392、369、132 ms 显示；前两轮 fallback 于约 301/294 ms 开始，第三轮自然更新无 fallback。完整跨窗格复制/移动/删除、同目录操作、文件夹变化及选区/焦点均通过。Release App/HostProbe 构建与 Settings smoke 通过。

该变更只缩短 watcher 静默期；大批量计数不匹配仍走既有 500 ms/有界枚举等待，fallback、Shell 通知和刷新保选区逻辑均保留。此处为三轮本机隐藏宿主样本，慢速/网络盘和可见窗口尚未覆盖，自动刷新 P0 保持开放。

### 2026-10-24 250 ms 静默期下的 3,000 项目录压力复跑

使用 `--live-shell-directory-stress-large 3000` 在隔离目录中运行 Release 产品监视器。创建用时 1,634 ms，生产 watcher 收到 3,000/3,000 个创建事件；Shell 创建阶段直接收敛，未触发 fallback。额外删除单项后，128 项批量删除的最新路径身份查询耗时 1 ms，查询时 Shell 视图仍含该文件，随后批量删除可见。批量删除期间最大 Dispatcher 心跳间隔为 113 ms。

剩余批量删除最终由一次计数 fallback 收敛（刷新开始时 expected=3,002、actual=3,003；删除任务已完成），创建/删除后的两项选区和独立焦点都保留，最终磁盘及视图计数均回到 3。外部 watcher 对创建少报 1 项（2,999/3,000），产品 watcher 则完整收到创建与删除事件；两者没有错误。HostProbe Release 构建 0 警告/错误，Settings smoke 通过。

此轮支持 250 ms 合并窗口下的 3,000 项创建/删除与 Shell 身份查询在本机隐藏宿主可运行，且选区/焦点正确；它没有证明所有 burst 均不触发 fallback，也没有覆盖可见 GUI、慢速/网络盘或持续高负载下的长期 Dispatcher 响应。自动刷新 P0 仍开放。

### 2026-10-24 10,000 项刷新后的焦点与选区稳定性复查

在正式 250 ms watcher 静默期下连续运行 Release `--live-shell-directory-stress-large 10000`。创建耗时约 3.4–4.8 秒、删除约 1.3–4.5 秒；产品 watcher 每轮均报告创建 10,000/10,000。8 轮增强采样中有 3 轮在最终检查时焦点不同于操作前的 `initial.txt`，探针以 exit 1 结束；另 5 轮最终计数、两项选择及焦点均正确。失败轮最终磁盘和视图计数仍回到 3，选区也恢复，失败项是焦点身份。

50 ms 时间线显示，fallback 后 Shell 视图计数有时已达到 10,002/10,003，选区仍会短暂变空；可观察到的空档约 60–100 ms，多数样本随后恢复。一次焦点失败轮中，刷新回调时 view count=10,002，刷新代码记录选择与焦点恢复均成功且耗时 14 ms，但阶段性检查仍看到焦点为新增的 `burst-0000.txt`；后续目录删除阶段最终回到 `initial.txt`。Shell 选择回调显示，Refresh 抑制期间先发出空/部分选择状态，抑制解除附近又出现空、单选、双选回调；对应采样中焦点短暂转到新增的 `burst-0000.txt`，约 70–110 ms 后回到 `initial.txt`。这些变化发生在无用户输入、导航未挂起的隐藏宿主中。

新增的原生调用级 trace 在一轮最终焦点失败样本中确认：第二次 Refresh 回调的 batch selection seed 和 `SelectAndPositionItems` 均返回成功，但随后 `ContainsItem` 无法在当前 Shell 枚举中找到快照里的 focused PIDL，因此没有执行焦点 PIDL 的 `SelectItem`；结果为 `selection=True, focus=False`。当时焦点快照仍有效，探针后续看到焦点落到 `second.txt`。同轮另一次恢复中，焦点 PIDL 可找到，`SelectItem` 返回 `S_OK`，立即验证也通过。证据指向 Refresh 回调发生时 Shell 视图枚举尚未提供焦点项目；项数看似达到目标并不足以证明 PIDL 已可查询。它解释了至少一次直接恢复失败，但尚不能独自解释所有后续焦点漂移轮次。

为定位问题，HostProbe 现在以 50 ms 采样选区、焦点及视图计数，并输出每次刷新完成、最多 64 条 Shell 选择回调和最多 128 条快照恢复原生调用样本；产品只增加恢复结果诊断值，没有改变恢复时序。最新 Release HostProbe 构建 0 警告/错误；这轮 HostProbe 因焦点检查失败而按预期 exit 1。下一步是在 Shell 枚举能找到焦点 PIDL 后重试恢复，并在重试期间尊重导航变化及用户输入代际；先把此失败场景转为有明确恢复状态断言的回归，再实现策略。10k 焦点稳定仍未通过，自动刷新/大目录焦点 P0 保持开放；没有运行可见 GUI，也未发布。

### 2026-10-24 延后重试 Shell 焦点 PIDL 恢复

Settings smoke 先增加了“延后恢复只在同一视图、输入代际未变且仍在有界窗口内继续”的门控断言；旧代码因缺少该门控按预期失败，再实现后通过。产品刷新后的首次选择恢复若发现保存的 focused PIDL 暂不可枚举，会先恢复选区但不把焦点抢到任一选中项，并保留 PIDL 快照。随后每 100 ms 检查 Shell 选择回调版本；回调稳定后重试，最多 1.5 秒。成功后释放快照并恢复 watcher 检查；超时才使用原有“聚焦幸存选中项”回退。导航或路径变化、键盘/鼠标输入会取消该任务，watcher 在恢复等待期间不并发触发另一次计数刷新。

首个 10k 运行曾因探针仍按旧时序采样“创建阶段焦点”而 exit 1；同一输出显示延后调用在约 250 ms 后成功找回 `initial.txt`，删除阶段最终焦点也正确。探针已改为把“无计数刷新进行中、无延后恢复等待、计数符合预期”作为创建阶段稳定条件。更新后的 Release 产品监视器 3/3 轮通过：每轮产品 watcher 创建事件 10,000/10,000，最终磁盘及视图计数为 3，两项选区与 `initial.txt` 独立焦点均保持。至少两轮直接观察到 focused PIDL 首次不可见、延后重试时重新可见并通过 `SelectItem` 验证；其它轮次即时恢复成功。最大 Dispatcher 心跳间隔 105–126 ms。App/HostProbe Release 构建 0 警告/错误，Settings smoke PASS。

当前证据支持本机 Release 隐藏宿主 10k 单窗格压力中的稳定恢复，并有导航/输入取消的纯逻辑门控回归。实际可见 UI、真实键鼠在重试窗口内打断操作、慢速/网络盘以及其它 Windows 版本仍未覆盖；因此自动刷新 P0 不关闭，不发布。

同一改动后的完整 Release 产品监视器复验 1 轮通过：单文件复制 31 ms 完成、watcher 59 ms 收到、Shell 视图 120 ms 可见且无 fallback；四窗格 watcher 均收到 3 次创建，生产刷新 4 次（含两次计数 mismatch 和一次刷新期间增量）。跨窗格复制/删除/移动、同目录移动/删除、文件夹创建/删除均可见，文件夹创建未误发文件 CREATE，最后选区和焦点均恢复。该轮仅为本机隐藏宿主，不能替代 GUI 或网络盘验收。

### 2026-10-24 延后焦点恢复的输入取消集成回归

新增 `--live-shell-deferred-restore-user-input` 隐藏生产宿主模式，直接构造一个持有旧选区与独立焦点的快照并排入产品的延后恢复计时器；随后向 Shell 视图窗口发送合成键盘输入消息，让应用的原生输入处理路径取消旧任务，再将选区和焦点改到 `third.txt`。探针等待输入快照收敛及恢复任务清空，再额外等待 1.6 秒（超过完整 1.5 秒恢复窗口）后读取真实 Shell 选区和焦点。

Release 构建 0 警告/错误；集成探针连续 3/3 轮通过，每轮均为 `scheduled=True`、`settled=True`、`old-restore-pending=False`、选中 `third.txt`、焦点 `third.txt`；Settings smoke PASS。首轮确认失败原因在探针辅助函数：虽然选择成功，但未显式发送 `SVSI_FOCUSED`，Shell 仍将旧行报告为焦点。修正夹具后通过，未改动产品恢复策略。此证据来自无可见窗口的本机 WPF/Shell 宿主和合成消息，不证明真实物理键鼠或桌面窗口行为；可见 GUI、慢速/网络盘仍待验收，自动刷新 P0 继续开放，不发布。

随后增加 `--live-shell-refresh-deferred-user-input`，不再反射调用延迟调度方法：先在真实 Shell 视图中选择 `second.txt`、`third.txt` 并将焦点放在未选中的 `initial.txt`，删除焦点文件后调用产品 `Refresh()`。探针等待 `_directorySelectionRestorePending` 实际变为 true，再发送原生键盘消息并把焦点与选区改到 `third.txt`；最终 Shell 视图计数稳定为 2，输入代际收敛，延迟恢复任务不再挂起，超过 1.5 秒后焦点/选区仍是 `third.txt`。Release 构建 0 警告/错误，真实产品刷新路径探针连续 3/3 通过；Settings smoke PASS。

为这条路径尝试运行的既有 `--live-shell-refresh-one-pane-unselected-3` 对照探针在创建测试观察器时失败，`SHChangeNotifyRegister` 返回 0，因此未到达其刷新断言；新增的针对性探针不依赖该全局 Shell 通知观察器。初次真实刷新尝试因删除发生在 `Refresh()` 之后，Shell 仍能枚举旧焦点而没有进入延迟恢复；调整为先删磁盘文件、再刷新后稳定触发。重建文件会造成计数观察竞态，因此最终场景保留删除状态并断言 Shell 计数收敛为 2。以上属于本机隐藏 Shell/WPF 宿主与合成键盘消息，不是物理键鼠或可见窗口验收。

### 2026-10-24 延后焦点恢复的鼠标输入取消回归

在上述真实刷新场景中增加 `--live-shell-refresh-deferred-mouse`。它等待产品真实进入延后恢复后，向隐藏原生 Shell 视图窗口发送 `WM_LBUTTONDOWN`/`WM_LBUTTONUP`（客户区原点），并断言该消息确实经过产品输入代际钩子；随后把选区和焦点设置为 `third.txt`。Release 下鼠标消息模式 3/3、键盘消息模式 3/3 通过；每轮均确认 deferred reached、输入代际更新、刷新后 Shell 计数为 2、输入状态收敛，延迟窗口结束后焦点与选区仍是 `third.txt`。Release 构建 0 警告/错误，Settings smoke PASS。

鼠标消息是发送给隐藏控件的合成 Win32 消息，不代表物理点击、坐标命中或桌面可见行为。此前既有刷新对照探针因 `SHChangeNotifyRegister` 返回 0 未运行到刷新断言；针对性输入回归绕过该观察器依赖。可见 GUI、物理键鼠及慢速/网络盘仍待验收。

后续把产品目录 watcher 保持开启，在延迟焦点恢复期间让已删除的 `initial.txt` 与随后创建的 8 个 `extra-*.txt` 都经过 watcher。测试要求确认 DELETE 与 8 个 CREATE 均到达、Shell 视图计数从 2 收敛到 10、目录变化批次待处理增量归零且预期计数重新基准为 10。鼠标消息 + 活跃 watcher 场景连续 3/3 轮通过；每轮最终选区/焦点仍为 `third.txt`。这验证了恢复任务被用户输入取消后，暂停期间进入 watcher 的删除/创建 burst 仍由产品队列合并并结算，没有因取消恢复而遗失。本轮未修改生产刷新代码，仍是隐藏宿主/合成鼠标消息证据。

随后将同一场景扩至 10,000 个新文件，覆盖完整产品 watcher burst 合并及计数恢复路径。Release 连续 3/3 轮确认原焦点文件 DELETE 到达、10,000 个 CREATE 均到达，Shell 视图从初始 3 项经删除收敛到 10,002 项；watcher 批次待处理增量归零、最终预期计数为 10,002，用户输入代际收敛，1.5 秒延迟恢复窗口结束后仍聚焦并仅选中 `third.txt`。三轮 fallback 次数为 1、0、0，说明 Shell 自然枚举与兜底刷新路径均能收敛。首次 3,000 项尝试因探针仅等 3 秒便报告 settle=false；计数/焦点当时正确，改为符合本功能有界刷新周期的 15 秒观察后 3,000 项也通过 3/3。此验证是单窗格本机隐藏宿主、合成鼠标消息和本地文件系统，不覆盖物理输入、可见 UI、网络盘或长时多窗格持续负载。

### 2026-10-24 四窗格并发目录变化与焦点恢复压力验证

将上述场景扩展到四个生产 ExplorerBrowser 宿主并发写入各自目录：目标窗格删除未选中的焦点文件并新增 10,000 项，其余三个窗格各新增 1,000 项。目标窗格在实际延迟恢复期间接收合成鼠标输入并改选/聚焦 `third.txt`；第二窗格保留 `second.txt` 的单项选区与焦点作为跨窗格隔离检查。

Release 连续 3/3 轮通过：四个 watcher 均收到目标创建事件；Shell 视图计数最终为 `10002/1003/1003/1003`；每个目录变化批次待处理增量归零且预期计数匹配。目标 pane 的延迟恢复与输入代际均结束，pane 0 的 `third.txt` 与 pane 1 的 `second.txt` 焦点/选区都保持。fallback 向量为 `1/1/1/1`、`1/0/0/0`、`1/1/1/1`，覆盖自然 Shell 更新和按窗格兜底刷新混合结果。Release 构建 0 警告/错误，Settings smoke PASS，键盘取消控制组再次通过。

该结果证明本机隐藏宿主中并发本地 watcher 与各窗格计数在本测试 burst 后能收敛，并提供两个窗格的焦点/选区隔离证据；不覆盖物理输入、可见 GUI、网络盘、跨卷并发或长时持续负载。自动刷新 P0 仍开放，不发布。

### 2026-10-24 四窗格混合变化与 watcher 强制刷新恢复

在四窗格并发 burst 场景中加入混合文件变化：pane 1 将 `third.txt` 重命名为 `renamed-third.txt`、删除 `initial.txt` 并新增 1,000 项；pane 3 通过反射调用真实 `FileSystemWatcher.OnError(ErrorEventArgs(InternalBufferOverflowException))`，触发产品已注册的 Error 事件处理器，其余 pane 继续执行原 burst。该错误回调是确定性的 overflow-handler 注入，不是由操作系统真实缓冲区溢出产生。

Release 连续 3/3 轮通过：计数收敛为 `10002/1002/1003/1003`，四个 watcher 批次清空且预期值分别匹配；pane 1 的 rename 和 pane 3 的 Error 均报告 forced refresh，目标用户 pane 与第二窗格的选区/焦点保持独立。各轮 fallback 向量为 `1/1/1/1`、`1/1/1/1`、`0/1/0/1`。Release HostProbe 构建 0 警告/错误、Settings smoke PASS，键盘取消控制组通过。

该测试验证产品 `FileSystemWatcher.Error` 事件接线与强制刷新处理，也覆盖真实文件重命名/删除混入多窗格大 burst；它不证明操作系统能否在真实高压时序触发缓冲区溢出。仍待真实溢出压力、物理键鼠、可见 UI、网络盘与长期运行验收；自动刷新 P0 保持开放，不发布。

### 2026-10-24 真实 watcher 溢出与创建回调排队

新增独立临时目录探针，保持产品相同的 16 KiB `FileSystemWatcher` 缓冲区。10,000 项并行创建时实际收到 176 个 `Created` 与 2 个 OS `Error` 回调；50,000 项收到 137 个 `Created` 与 3 个 `Error` 回调，确认能在本机文件系统稳定制造真实溢出。夹具只写入系统临时目录下的唯一子目录并在探针退出时清理。

隐藏生产宿主以 pane 3 真实并行创建 10,000 项，附加只计数的 Error 观察器；首次复现 2 个实际 watcher Error、最终 Shell 视图计数 `10003`，并保持 pane 0 的 `third.txt` 和 pane 1 的 `second.txt` 焦点/选区。该压力揭示两项问题：创建回调原先每个文件都向 STA Dispatcher 投递工作；Shell 计数基线在临时空视图期间被重设为 `0`，Shell 最终虽显示 `10003`，基线仍未收敛。创建路径现已改成线程池侧先记录最新路径并通过待派发标记合并 Dispatcher 回调；新增 smoke 先失败后通过，常规四窗格 watcher 控制场景通过。

继续追踪发现 timer 在某次计数与已重基准值相同、无需刷新后会停止；若该值是空视图的暂态 `0`，Shell 随后完成枚举时不会再有 watcher 事件触发校准。现在当非空预期计数经过有界刷新后被空视图替代，批次会安排 180 次、每 500 ms 一次的后台计数复查；Shell 计数重新出现后正常计数差路径刷新并重基准，达到上限则停止。Settings smoke 新增“接受空视图并继续有限复查”测试，先失败后通过。修复后真实 OS 溢出整合探针连续 3/3 轮通过，每轮 1 次 Error，pane 3 最终视图及基线均为 `10003`、待处理增量归零；四窗格 final counts 为 `2/2/3/10003`，pane 0 的 `third.txt` 和 pane 1 的 `second.txt` 焦点/选区均保持。各轮 pane 3 fallback 为 2、2、9，覆盖不同 Shell 通知/枚举时序。50,000 项生产宿主尝试超过 60 秒写入门限，未作为通过样本。

本组证据为本机隐藏 Shell/WPF 宿主、并行本地磁盘 I/O 与合成输入；不覆盖可见 UI、物理键鼠、网络盘或长期运行。自动刷新 P0 继续开放，不发布。

### 2026-10-24 10,000 项产品 watcher 压力复测

使用 Release 隐藏生产宿主连续运行 3 轮，每轮在单目录创建并删除 10,000 个文件，记录产品 watcher、Shell 视图计数、原有选区/焦点和 Dispatcher 心跳。三轮均以退出码 0 完成，文件视图最终从基线 3 项收敛回 3 项；选中的 `second.txt`、`third.txt` 与独立焦点 `initial.txt` 在创建和删除后均恢复。产品 watcher 每轮分别观察到 10,000/10,000 个创建/删除事件，未报告 observer 错误。创建耗时为 9.4–10.0 秒；删除耗时为 6.9–13.4 秒。Shell 异步枚举期间有短暂空视图或中间计数，刷新 fallback 每轮为 1–5 次；三轮最大 Dispatcher 心跳间隔为 106–202 ms。第二轮删除明显较慢且 fallback 较多，说明在该本机负载下可恢复，但延迟/刷新次数仍有调度波动。

这只是本机单窗格隐藏宿主、磁盘压力与程序化 Shell 输入，不是可见窗口响应、远程盘、长时内存曲线或崩溃率结论；不据此宣称性能达标。自动刷新 P0 继续开放，不发布。

追加一轮保留完整 Shell 计数时间线的复测：创建耗时 12.8 秒，操作结束时视图为 9,699 项，之后约 0.7 秒自然收敛到 10,003，创建阶段没有 fallback；删除耗时 18.0 秒，产品在 `expected=9874, actual=10002` 时执行 1 次 fallback，随后 Shell 计数仍分批下降，约 19 秒后回到 3 项。期间选区/焦点保持，最大 Dispatcher 心跳间隔 394 ms。此样本显示 fallback 不会阻断后续原生枚举，但 Shell 展示大量删除的长尾主要由 Shell 自身枚举时序主导；不据此盲目延长 fallback 等待窗口，以免普通单项变化也变慢。

### 2026-10-24 50,000 项产品 watcher 压力复测

将 `--live-shell-directory-stress-large` 的夹具上限从 10,000 扩至 50,000，并把该模式纳入退出清理，避免重复压力轮次遗留夹具。Release 隐藏生产宿主连续两轮创建并删除 50,000 个文件，均退出码 0；产品 watcher 两轮都完整收到 50,000/50,000 创建与删除事件，Shell 视图最终由 `50003` 回到 `3`，磁盘计数也为 3。两轮选区与独立焦点在创建和删除后均通过检查。

第一轮创建/删除耗时 22.2/12.4 秒，settle 分别 4.6/4.3 秒，触发 4 次 fallback，最大 Dispatcher 心跳间隔 506 ms；第二轮创建/删除耗时 24.0/16.3 秒，settle 1.4/0 秒，没有 fallback，最大 Dispatcher 心跳间隔 343 ms。批量删除期间 128 项代表路径 Shell 身份查询分别为 17/18 ms，查询时仍可见，最终批次均正确收敛。独立 50,000 项 watcher 探针在 15.7 秒创建后收到 142 个 `Created` 与 3 个真实 OS `Error` 回调，与整合生产宿主 watcher 全量计数分开记录。

两轮证明本机本地盘的隐藏产品宿主可完成 50,000 项 burst 并恢复最终计数、焦点和选区，不证明用户可感知的窗口响应达标：Dispatcher 观察到 343–506 ms 最大心跳间隔，创建/删除耗时也存在波动。仅完成两轮，未覆盖可见 UI、慢速/网络盘、长时间持续负载或其他 Windows 版本；自动刷新 P0 保持开放，不发布。

### 2026-10-24 50,000 项 Dispatcher 间隔来源对照

为区分 Shell 查询本身与回调间停顿，压力探针增加了 Dispatcher 回调总工作时间及 Shell `ItemCount`、选区、焦点读取的独立耗时。一次产品 watcher 50,000 项复测中，创建/删除用时 22.8/34.4 秒，刷新 fallback 1 次并最终恢复计数、选区和焦点；最大 Dispatcher 间隔为 1,473 ms。探针回调自身最大工作 77 ms；`ItemCount` 最大不足 1 ms，选区/焦点读取最大 38/43 ms。

随后用相同 50,000 项夹具运行 native-only 对照：产品目录 watcher 已停止，不发生产品 fallback。创建/删除各自等待 15 秒后，Shell 视图计数仍分别停在 49,316/1,997，两个阶段均未在有界观察内收敛；最大 Dispatcher 间隔 1,201 ms，探针回调自身最大工作 79 ms，`ItemCount` 最大不足 1 ms，选区/焦点读取最大 42/41 ms。选区与独立焦点保持。该对照仍有同一 ExplorerBrowser、Shell 自身异步枚举、测试监视计时器及本地磁盘 50,000 项并发压力，不能精确归因到单一 Windows 组件。

产品 watcher 与 native-only 两组都出现秒级 Dispatcher 间隔，而被测同步 Shell 查询耗时远低于该间隔；native-only 也未及时枚举完项目。因此当前证据不支持把停顿归因于 watcher 逐事件派发或某次 `ItemCount` 调用，也没有足够证据安全调整产品计时器/通知策略。压力探针的分段计时保留供后续复测。自动刷新/大目录 P0 继续开放；本地隐藏宿主之外的可见 UI、其他盘型和持续负载仍待验证。

### 2026-10-24 多优先级 Dispatcher 与线程调度对照

为区分低优先级 Dispatcher 延迟与整个进程线程都未获调度，压力探针增加 `Normal`、`Input`、`ContextIdle` 三个轻量 Dispatcher 心跳，并以独立专用线程每 20 ms 记录一次调度间隔。Release 隐藏生产宿主单独运行 50,000 项产品 watcher burst：创建/删除耗时 33.6/19.5 秒，watcher 创建/删除事件各 50,000/50,000，最终视图与磁盘均回到 3 项，原选区和焦点最终恢复。

本轮 `Normal`/`Input` 最大心跳间隔均为 559 ms，`ContextIdle` 为 628 ms，既有 `Background` 心跳为 554 ms；独立线程最大间隔为 41 ms。探针自身一次心跳最多工作 58 ms，Shell 数量读取不足 1 ms，选择/焦点读取最大 28/29 ms。样本说明 Dispatcher 所在的 Shell STA 线程确有数百毫秒未处理高优先级心跳的空窗，而同进程独立线程调度较平稳；它没有区分原生消息处理、COM/ExplorerBrowser 回调或连续 Dispatcher 工作，也不是可见用户窗口响应测试。

之后试验的 `DispatcherHooks` 会给每个发布操作加时间戳并跨线程同步，改变了小规模样本的调度结果，已从探针移除，不纳入性能结论。SettingsSmoke、最终 HostProbe Release 构建通过。下一步应在不挂全局 Dispatcher Hook 的前提下，对 ExplorerBrowser 站点回调和窗口子类消息按类型累计耗时，继续用单独运行的同规模夹具定位 UI STA 空窗；在定位前不更改产品刷新节流或 Shell 通知策略。自动刷新/大目录 P0 继续开放。

### 2026-10-24 精确 CREATE 通知路径回归修复

生产监视器复跑曾发现单文件 `IFileOperation::CopyItem` 后产品 watcher 收到创建事件、Shell 计数仍落后，但 `SHCNE_CREATE` 发送数为 0，随后靠计数 fallback 收敛。新增原因诊断后复现为 `empty-path-batch`。根因是 `ShellCreateNotificationBatch.TakeForDispatcher()` 在 STA 交接时消费并清空了代表路径，而 60 ms 合并计时器随后才调用 `TakeLatest()`；因此单事件批次必然丢失路径，多事件批次也只会偶然使用交接后新到达的路径。

先在 `ShellCreateNotificationBatchSmoke` 添加“Dispatcher 交接后延时消费者仍能取得路径”的断言，旧实现按预期失败；再修改交接仅结束派发标记、保留最新路径给 60 ms 计时器消费。Settings smoke 全通过，HostProbe/App Release 构建 0 警告/错误。父级 Shell 事件观察器本轮一次注册失败，改用不依赖父级观察器的同一生产监视器变体复验成功：单文件复制 watcher 73 ms 收到，产品发送 1 次 `SHCNE_CREATE`，结果为 `expected=4;actual=3`，发送时间约 122.8 ms；完整四窗格 copy/delete/move、文件夹变化、计数校验和选区/焦点检查通过。

这一轮仍在 312.9 ms 启动计数 fallback，并于 371.4 ms 完成；首次视图显示约 365 ms。因此它证明通知从丢路径恢复到正确发送，但不证明本轮因此更快，fallback 仍是实际收敛保障。自动刷新 P0 保持开放，可见窗口、慢速/网络盘及更大 burst 仍待验收。

同一 Release 隐藏宿主随后再复跑一次：`IFileOperation::CopyItem` 用时 45 ms，产品 watcher 于 68 ms 收到事件，`SHCNE_CREATE` 于约 117–120 ms 发送，Shell 视图于 133 ms 首次显示新项目，本次创建没有 fallback。之后完整四窗格文件操作/焦点选区监视器退出码 0；计数 mismatch 与刷新中并发增量测试使用 4 次 fallback 并恢复基线，跨窗格及同目录文件操作、文件夹创建/删除均可见且焦点/选区正确，文件夹删除发送 1 次 RMDIR。与前一轮仍于 313 ms fallback 的样本并列看，创建通知路径已工作且有一次快速自然显示，但运行时序仍有差异，不据单轮保证所有目录即时刷新。

### 2026-10-24 创建通知路径修复后的 50,000 项复测

在 `ShellCreateNotificationBatch` 路径修复后重新运行 Release `--live-shell-directory-stress-large 50000`。产品 watcher 完整收到 50,000/50,000 创建与删除事件，创建耗时 30.8 秒、删除 20.7 秒；Shell 项目最终由 `50003` 回到 `3`，磁盘也回到 3。两项选中项与独立焦点在创建和删除后均保持。持续创建跨越许多 60 ms 合并窗口，本轮发出 12 次精确 `SHCNE_CREATE`，创建阶段无计数 fallback；批量删除阶段发生 2 次 fallback 后收敛。128 文件代表路径查询一次耗时 16 ms，最大 Dispatcher 心跳间隔 514 ms；探针回调最大工作 40 ms，`ItemCount` 最大不足 1 ms，选区/焦点查询最大 22 ms。

该轮验证修复后的合并路径可以承受 50,000 项产品 watcher burst，并保留最终计数、选区与焦点；12 次通知是持续 30.8 秒创建过程跨多个静默窗口的结果，不应误读为每次 burst 一条或全轮仅一条。514 ms Dispatcher 间隔仍属响应风险，创建/删除耗时也有运行间波动；与之前样本不能直接据单轮宣称性能改善。自动刷新/大目录 P0 继续开放；仅覆盖本机隐藏宿主与本地盘，不覆盖可见 UI、慢速/网络盘或长时持续负载。
### 2026-10-24 10,000 项 watcher 后续复测

再次运行 Release `--live-shell-directory-stress-large 10000`。生产 watcher 收到全部 10,000 个创建和删除事件，文件视图最终从 3 项恢复到 3 项；`second.txt`、`third.txt` 选区和 `initial.txt` 独立焦点在创建及删除后均恢复。创建/删除操作分别耗时 5.25/5.29 秒，Shell 计数 settle 分别约 4.61/4.57 秒；全轮触发 3 次 fallback（创建 1 次、删除 2 次）。删除 fallback 启动时 Shell 仍显示 248 项，之后原生枚举继续收敛。最大 Dispatcher 心跳间隔 139 ms，单次心跳工作 48 ms；Watcher 观察器收到 9,998/10,000 创建事件、无错误，产品 watcher 的完整计数独立确认为 10,000/10,000。

该样本再次确认最终计数、焦点和选区可恢复，但中间视图可能长时间落后，刷新次数和 Dispatcher 间隔仍有波动。它是本机单窗格隐藏生产宿主与本地盘压力证据，不代表可见响应、远程/慢速盘或长期负载；自动刷新 P0 仍开放，未据此更改产品定时策略。
### 2026-10-24 新建文件夹发生在目录 watcher 基线建立前

扩展 Release 隐藏生产宿主 `--file-command-states`：菜单状态验证之后，通过生产 `ShellFileCommands.Invoke(NewFolder)` 操作每轮新建的隔离目录，并同时启动生产 `FolderCreationObservation`。新加的 `--file-command-states-early` 不等待目录 watcher 建立基线，在 Shell 视图与导航已就绪后立即执行命令。

三个早期运行均记录到 `watcher-ready-before-invoke=false`、初始基线为 null、`FolderCreationObservation` 捕获唯一的“新建文件夹”，而目录 watcher 创建事件和 fallback 计数都为 0。两轮在最长 8 秒内由 Shell 自然显示；另一次未自然显示，显式调用现有 `Refresh()` 后显示。此前一次同路径早期运行也在 8 秒后保持四窗格计数 `1/1/1/1`，磁盘已有目录，之后同一命令在 watcher ready 条件下可经 watcher 及计数 fallback 显示。

这确认 watcher 建立前存在漏掉创建事件的时间窗，Shell 自然更新也有间歇差异；一次显式刷新能恢复该隐藏宿主样本。产品现使用 `ShellNewFolderRefreshGate` 将恢复限制为唯一、无歧义且观察成功的文件夹，目标焦点尚未匹配、导航仍有效、并且用户输入代数未变化时最多刷新一次；门控 smoke 覆盖重复刷新、已聚焦、无/多/失败观察、导航变化和新输入。HostProbe 早期模式另用真实观察快照及生产门控调用现有 `Refresh()`，连续三轮都在计数收敛且异步恢复回调结束后显示目录，原有 `initial.txt` 选区和焦点均保持；ready 模式不触发门控，watcher 创建事件为 1 且目录可见。该恢复调用不强制选中目录。由于隐藏父窗口未激活，这些结果不覆盖真实产品窗口激活入口、物理用户按键中断、刷新后的新目录焦点或真实自动重命名；不据此宣称可见体验已修复。

### 2026-10-24 watcher 删除更新与选区保留

Release HostProbe 在绑定 Shell 视图前预置 `selection-delete-target.txt`，建立四项基线后由隔离子进程通过 `IFileOperation` 删除未选中的目标文件。watcher 模式不调用 `SHCNE_UPDATEDIR`，等待生产 `FileSystemWatcher` 删除事件、生产批次 settle 和 Shell 计数从 `4/3/3/3` 收敛到 `3/3/3/3`。

连续 4/4 轮均输出：`watcher-deleted=1`、`updatedir-notification=False`、`delete-visible=True`、`disk-correct=True`、`selected-before/after=second.txt,third.txt`、`focus-before/after=initial.txt`、`selection-preserved=True`、`focus-preserved=True`，`fallback-delta=1`。追加的 fallback 触发计数显示 `expected=3, actual=4, forced=False`：Shell 视图保留了已删除项目的旧计数，生产计数比较因此按既有规则启动刷新；不是 watcher 强制刷新。发送精确 `SHCNE_DELETE` 的既有 A/B 结果为通知组和控制组都 0/3 在 700 ms 内更新，所以文件删除继续使用计数 fallback。该样本证明 watcher 能观察删除，最终视图由一次计数 fallback 收敛，并保留未删除项目的选区/焦点；它不是无 fallback 的 Shell 自然更新证据。

同轮显式 `SHCNE_UPDATEDIR` 对照及“删除前记录一次用户输入”对照均通过，二者 fallback 均为 0；SettingsSmoke 通过。父窗口保持隐藏。证据限于本机隔离目录和隐藏生产宿主，不等同于可见窗口中的实际删除交互，也不关闭自动刷新 P0。

### 2026-10-24 native-only 删除选区恢复稳定复测

新增 `--live-shell-selection-delete-native`，使用创建 Shell 视图前预置的稳定四项基线；生产 watcher 停止，不发送 `SHCNE_UPDATEDIR`。先通过隔离 `IFileOperation::CopyItem` 将视图增加到 5 项并确认可见，再删除未选中的副本，等待原生 Shell 视图自然回到 4 项。若复制阶段未显示新项，探针会在删除前拒绝该轮，避免把无效基线记作结果。

连续 3 个有效样本均满足计数自然变化、磁盘正确、`fallback-delta=0`，原来的 `second.txt`/`third.txt` 选区与 `initial.txt` 焦点均保留；Shell selection callback timeline 记录了瞬时空选择，随后现有抑制恢复路径重新选中两项。`CDBOSC_SELCHANGE` 由 12 增至 18。另一次复制未进入视图的准备失败在删除前退出，不计入有效样本。

这表明当前 Release 隐藏生产宿主的原生删除选区恢复代码已覆盖该稳定场景，本轮没有改产品逻辑。仍不代表真实可见 ExplorerBrowser 窗口、鼠标/键盘删除体验或不同 Windows 环境已验收；需保留 GUI 验收项。

同轮重跑“删除前记录用户输入”的控制场景并打开 selection callback trace：输入版本为 `1/0`（native/settled），空选区回调发生在该版本差异期间；生产 `OnShellSelectionChanged` 的代数门控会忽略这类回调，之后 `SHCNE_UPDATEDIR` 重枚举时 Shell 自己恢复了两项选择，未走 `suppressed=true` 的应用恢复路径。`SettingsSmoke` 中“新输入阻止旧快照恢复”和延迟恢复取消用例通过。该控制验证的是输入门控与显式重枚举，不替代真实用户输入的可见 GUI 验收。

#### 补充：native-only 删除自然收敛时序复测（2026-10-24）

Release `--live-shell-selection-delete-native` 一轮有效失败后立即重复一次：失败轮磁盘删除成功，但 15 秒内 Shell 视图仍保持旧计数，通知快照不变（`state=2:12`），探针未通过；紧接样本的 selection callback timeline 在宿主启动后 `t≈1885ms` 显示自然更新，回调从 12 增至 18。此前连续 3/3 通过样本仍有效，但自然收敛并不稳定。生产 watcher 对照通过一次非强制计数 fallback 收敛到 3 项，保留 `second.txt`/`third.txt` 选区和 `initial.txt` 焦点；本探针没有输出相对于删除操作的完成时长，不能从宿主启动时间推算删除延迟。当前只改变证据判断，不改变产品策略；自动刷新 P0 继续开放，真实可见窗口与其他 Windows 环境仍待测。

为获得以删除调用为零点的计时，HostProbe 现额外输出操作返回、Shell 计数收敛和 watcher 批次结算时间。本次交替批次中 native-only 有 2 个有效轮：一轮删除命令 25 ms 返回，等待 15,058 ms 仍未收敛；另一轮命令 68 ms 返回、538 ms 视图收敛。另 1 次复制未进入视图的准备失败，删除未执行且不计入样本。生产 watcher 有 3/3 个有效轮通过，操作返回/视图收敛/批次结算分别为 `32/338/370 ms`、`30/362/396 ms`、`29/338/363 ms`，每轮 fallback=1。有效轮中的选区和焦点均保持。结果支持该本地隔离路径存在自然 Shell 更新波动，而当前 watcher/fallback 路径能在这三轮收敛；样本仅限本机隐藏宿主，不证明普遍延迟或网络盘行为。计时字段只增加诊断输出，不改变产品逻辑。

### 2026-10-24 多选快照保护与 50,000 项复测

此前一次 50,000 项运行在最终计数回到 3 且焦点恢复的情况下，选区只恢复了 `second.txt`；轨迹记录了删除期间选区从两项短暂变成一项，随后清空。源码复核发现 `OnShellSelectionChanged` 对任何非空选择都会直接替换 `_lastNativeSelection`，即使视图、导航和用户输入代数未变。这样一个中间子集可能丢掉尚未删除的另一项。

现有 `ShellSelectionRestoreGateSmoke` 增加了策略断言：同一视图且没有新输入时，较小的非空选择不得覆盖已有快照；相同或更大的选择可以更新，用户输入变化或视图变化也不继承旧快照。先加断言后运行时按预期失败，接入门控后通过。产品回调在此情况下释放中间快照并保留原来的 PIDL 身份，后续刷新恢复仍由原有 `ContainsItem` 检查过滤已经不存在的条目。

接入后 Release HostProbe/App 构建 0 警告/错误，完整 SettingsSmoke、四窗格产品 Shell 操作探针通过。新的 50,000 项压力轮 watcher 创建/删除均为 `50000/50000`；最终视图及磁盘均为 3 项，`second.txt`/`third.txt` 选择和 `initial.txt` 焦点保持。创建/删除各约 22.4/16.9 秒；创建阶段无 fallback，删除发生一次计数 fallback。Dispatcher `Normal`/`Input` 最大间隔约 497 ms、`ContextIdle` 482 ms；独立线程最大间隔 22 ms。此次轨迹中选区在两阶段基本保持两项，Refresh 时清空后由显式快照恢复，没有再次出现部分选择回调。因此本轮是候选保护后的成功回归，不是对偶发失败序列的直接复现，仍需加入能稳定产生部分选择回调的定向 HostProbe。

同轮将创建/删除 `SHChangeNotify` 从 `SHCNF_FLUSH` 改为 `SHCNF_FLUSHNOWAIT`，避免在 ExplorerBrowser STA 等所有 Shell 订阅方处理完通知；这是依照 [SHChangeNotify 官方说明](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify) 的候选响应性调整。隐藏生产监视器与 50,000 项轮均完成，但 Dispatcher 间隔仍接近 0.5 秒，且没有可靠 A/B 性能对照；所以它尚未证明能修复 STA 长空窗，也不能据单轮断言提速。全证据只覆盖本机隐藏宿主/本地盘，未验证可见桌面、网络盘或长时负载。

#### 补验：产品选择回调中的部分快照保护

新增 Release 隐藏 HostProbe 模式直接走 `ExplorerBrowserHost.OnShellSelectionChanged`：先选中 3 个夹具条目并捕获完整快照，再用原生 Shell 接口程序化取消一个选中项（用户输入代数不变），调用生产回调后读取 `_lastNativeSelection`，之后清空视图选择并再次调用回调，等待生产恢复路径。结果为 `full=3 partial=2 retained-snapshot=3 restored=True`，最终 `initial.txt/second.txt/third.txt` 三项均被恢复。该模式 0 退出，不显示父窗口、未操作真实桌面；这证明保护策略已接入真实产品回调/恢复调用链。它是受控回调调用，不证明 Shell 自发部分选择事件在所有负载下的具体时序。后续继续调查大目录 Dispatcher STA 空窗；该运行应使用 `--live-shell-directory-stress-large` 启用阶段计时，普通 stress 模式只提供较基础的计时和状态输出。

#### 补验：全量回调与窗口类计时

因先前 callback 每 8 次、Shell 子类消息每 32 次抽样可能漏过罕见慢调用，opt-in 诊断现改为全量聚合 `ICommDlgBrowser3` 回调及所有已子类化视图窗口的 `DefSubclassProc` 耗时，并按消息 ID 与原生窗口类区分；非诊断运行不启用该计时。Release 10,000 项样本创建/删除事件均 10,000/10,000，视图与磁盘回到 3，选区/焦点保持。`OnStateChange` 32 次最大 8.45 ms；`0x4C8/DirectUIHWND` 最大 37.37 ms，`0x4B0/SHELLDLL_DefView` 最大 22.20 ms；`ContextIdle` 最大间隔 244 ms，Input/Normal 均 118 ms。刷新同步调用最大 94.69 ms，完成回调最大 0.20 ms。

同版本 Release 50,000 项样本创建/删除耗时 28.3/33.5 秒，watcher 完整收到两阶段 50,000/50,000，最终视图与磁盘为 3 项，`second.txt`/`third.txt` 与 `initial.txt` 焦点保持。此次 Shell selection callback timeline 直接包含刷新周围 `selected=1`，快照保护后恢复到两项；另一个定向回调探针得到三项完整恢复。全量 `OnStateChange` 95 次最大 46.31 ms；`0x4C8/DirectUIHWND` 75 次最大 624.41 ms，`0x4B0/SHELLDLL_DefView` 20 次最大 271.14 ms。刷新同步调用最大 182.41 ms、刷新完成回调最大 0.15 ms、删除通知最大 44.18 ms；计数读取最大 0.19 ms、Dispatcher 批次消费最大 2.80 ms、Watcher 入队抽样最大 0.95 ms。Dispatcher `Normal/Input/ContextIdle` 最大间隔为 893/1,808/2,045 ms，专用线程最大间隔 38 ms，Dispatcher STA CPU 连续忙碌样本最长 535 ms。

与前一轮每 32 次抽样相比，按窗口类全量聚合发现 DirectUI 与 DefView 内部消息存在数百毫秒慢调用，但它们没有单独覆盖 1.8–2.0 秒最大 Dispatcher 间隔，且各耗时与总间隔并非严格配对。当前证据确认嵌入 Shell 的内部窗口处理是大目录响应风险的一部分，但没有指出具体私有消息语义或可安全绕开的 API。全证据仍限于本机本地盘隐藏宿主；不更改刷新/通知策略、不发布。下一步应使用能捕获原生调用栈的管理员性能追踪，或设计按消息阶段关联的低开销追踪；本机 WPR CPU 采集曾因启用系统性能分析策略权限不足而未开始。

#### 补验：共享计时起点与慢消息时间线

为核对慢 Shell 消息与 Dispatcher 空窗的先后，HostProbe 把两条时间线改用同一 QPC 起点，并在诊断模式记录每个超过 50 ms 的 `DefSubclassProc` 调用时间、窗口类、消息号及参数；同时记录超过 100 ms 的 Normal/Input/ContextIdle Dispatcher 间隔。Release 50,000 项复测退出码 0，创建/删除各约 29.7/26.5 秒，watcher 完整收到 50,000/50,000 事件，最终视图与磁盘回到 3 项，原选区和焦点保持。

此轮 `0x4B0/SHELLDLL_DefView` 于共同时间点约 36,435 ms 完成 234 ms 下游处理，同一时间附近 Normal/Input 间隔约 560 ms、ContextIdle 约 604 ms；这支持该 Shell 消息处理消耗了其中一部分 UI STA 时间。`0x4C8/DirectUIHWND` 最大 83 ms。`OnStateChange` 74 次全量计时最大 39.7 ms，`Refresh()` 最大 115.8 ms，完成回调 0.16 ms，计数读取最大 0.16 ms，Dispatcher 批次消费最大 0.15 ms。高优先级最大间隔为 Normal 730 ms、Input 1,093 ms，ContextIdle 最大 2,020 ms；在最大 ContextIdle 空窗附近没有同样长的单条子类消息。线程独立心跳最大间隔 39 ms，Dispatcher STA 连续忙碌采样最长约 517 ms。

因此已测到的一个 DefView 慢消息与一组中等 Dispatcher 空窗相重合，但其耗时不能解释全部高优先级空窗；低优先级 ContextIdle 还可能受排队工作饥饿影响。`0x4B0`/`0x4C8` 是 Shell/DirectUI 内部消息，参数含私有状态或指针，当前不据编号猜测其语义，也不拦截或修改消息。普通用户态 CLI 环境无 WPR/cdb 等原生调用栈工具，WPR 曾因系统性能策略权限未能启动；下一步在保持隐藏测试的前提下，优先观察低于 50 ms 的消息簇与队列工作关联，或取得可用的原生调用栈采集权限后再做根因定位。

### 2026-10-24 10,000 项 watcher 当前复测

Release `--live-shell-directory-stress-large 10000` 单轮退出码 0。产品 watcher 创建和删除事件均为 `10000/10000`，观察器分别为 `9999/10000`（无 error），最终磁盘及四窗格计数均回到 3；原有两项选择和独立焦点在创建、删除后保持。创建/删除操作分别为 6.99/3.20 秒，随后删除阶段的 post-operation settle 为 5.01 秒；创建与删除各触发一次 fallback，分别因 Shell 视图当时为 7389/10003 项而与预期 10003/10002 不符。创建后 Shell 最终先达到 10003 项，删除最终回到 3 项。最大 Dispatcher 心跳间隔 125 ms，单次心跳工作 43 ms，Shell 项目数读取最大不足 1 ms，选区/焦点读取最大 20/22 ms。该样本显示本机隐藏宿主在 10k burst 下可恢复并保留选择/焦点，但两次 fallback 和多秒 settle 仍有时序成本；不代表可见 UI、网络盘或长期负载。

### 2026-10-24 大选区全选耗时定位

此前 10,001 项探针在全选阶段超过 90 秒没有完成，进程已停止；该轮不能作为功能失败或通过样本。将同一隔离夹具缩小后，1,000 项生产 `ShellSelectionCommands.Apply(All)` 可以完成，完整调用约 1,879 ms；其中 PIDL 枚举不足 1 ms，单次 `IFolderView2::SelectAndPositionItems` 批量提交约 1,874 ms。此前 3,000 项完整全选约 18.2 秒，显示成本随选择规模快速增长。

以上分段计时只用于这次定位，已从生产源码移除；没有改选择实现。当前只能确认瓶颈在 Shell 批量选择调用一侧，尚未确认是该接口的规模成本还是菜单命令选择了低效路径。下一步要分开验证菜单“全选”和 Ctrl+A 的命令分派及选择语义，再评估是否可由当前 Shell 视图的原生键盘/菜单命令承担全选。Ctrl+A 的物理输入、多窗格焦点、真实窗口响应和其他 Windows 版本仍未覆盖。

进一步把 1,000 个 PIDL 以每批 100 个分十次提交到同一 `SelectAndPositionItems` 接口，总调用仍约 1,926 ms，和一次性提交约 1,874 ms 接近；拆批本身没有改善总耗时。该诊断分支已从 HostProbe 移除，因此当前产品和探针源码均无该临时代码。

### 2026-10-24 全选菜单复用 Shell Ctrl+A

将同一大选区探针切换到产品 `ExplorerBrowserHost.TrySelectAllViaNativeAccelerator`：探针先在隐藏 Shell 宿主的 UI 线程聚焦当前视图，再直接调用产品 helper；helper 临时把 Ctrl 置为当前调用线程按下状态，构造 Ctrl+A `MSG` 交给当前 `IShellView.TranslateAccelerator`，并在同步返回时恢复原键盘状态。微软文档说明 `SetKeyboardState` 只改调用线程的键盘输入状态，不影响其他线程；这不是 `SendInput` 或桌面键盘注入。

先运行尚无该产品方法的探针，确认按预期以 `MissingMethodException` 失败；接入菜单“全选”路径后，1,000 项样本 Shell 返回已处理且选中 1,000 项，快捷键翻译约 3 ms；10,001 项样本约 13–17 ms，排除未选中的删除目标后保留 10,000 项。删除目标后选区身份完全一致，fallback=0。菜单若无法聚焦当前 Shell 视图或 Shell 返回未处理，仍回退既有 PIDL 批量选择。

另向隐藏视图 HWND 直接发送 `WM_COMMAND/FCIDM_SHVIEW_SELECTALL` 的探针没有产生选择，因此没有采用该路径。当前证据验证产品 helper 与 Shell 选择行为，不验证可见菜单点击后的真实焦点、菜单关闭、多窗格或物理键盘；这些需要可见 UI 验收。

### 2026-10-24 Dispatcher STA 空窗的回调/窗口消息计时

为继续区分 ExplorerBrowser 回调、窗口子类前置处理与下游 Shell 消息处理，HostProbe 在压力阶段开始前重置计时，并额外记录 `ICommDlgBrowser3` 回调及 `ViewWindowProc` 抽样耗时；仅压力探针开启环境变量时启用，不改变产品刷新行为。单独运行 Release 10,000 项用例，退出码 0：创建/删除耗时 21.2/4.6 秒，最终计数及选区/焦点恢复检查通过；`Normal`/`Input`/`ContextIdle` 最大 Dispatcher 间隔为 1,199/1,665/3,889 ms，专用线程最大间隔 46 ms。抽样到的 `OnStateChange` 回调最大 3.36 ms，单条子类消息下游耗时 0.02 ms。

随后独立运行 50,000 项用例，退出码 0：创建/删除耗时 34.9/19.2 秒，watcher 事件均为 50,000/50,000，Shell 与磁盘最终回到 3 项，选区与焦点最终恢复。三个高优先级 Dispatcher 间隔最大值为 `Normal=698`、`Input=710`、`ContextIdle=798 ms`，专用线程最大间隔 22 ms；采样到的 `OnStateChange` 最大 1.53 ms，唯一一条子类消息样本低于 0.01 ms。短回调与独立线程调度平稳再次说明空窗发生在 ExplorerBrowser 所在 UI STA 的同步/原生处理路径附近，但稀疏的回调及消息样本不足以指出具体 Shell API 或内部工作；10k/50k 的 Dispatcher 间隔波动也不单调。下一步应提高关键回调/消息的低开销分段可观测性，避免据此直接改产品计时器或通知策略。

两轮只覆盖本机本地盘、隐藏生产宿主和程序化文件变化；不代表可见窗口响应、网络盘、长期负载或其他 Windows 版本。自动刷新/大目录 P0 继续开放，不发布。

### 2026-10-24 watcher/Refresh 阶段计时与 50,000 项波动复测

在上一轮探针基础上继续给生产路径增加稀疏阶段计时：Watcher 线程入队和 Dispatcher 批次消费每 64 次抽样；低频 Shell 计数检查、`Refresh()` 同步调用、刷新完成回调及原生创建/删除通知逐次计时。10,000 项复测退出码 0，创建/删除 watcher 事件完整，Shell/磁盘最终回到 3 项，选区/焦点保持；`Normal/Input/ContextIdle` 最大间隔为 115/115/251 ms，专用线程 22 ms。Watcher 入队样本最大 0.95 ms，Dispatcher 批次消费最大 0.15 ms；计数检查最大 0.25 ms，`Refresh()` 同步调用 83 ms，完成回调 0.19 ms；本轮两次创建通知最大 3.47 ms，两次删除通知最大 4.39 ms。另一轮相同 10k 配置的刷新同步调用 69 ms、完成回调 0.20 ms，Dispatcher 最大间隔 243 ms。此轮首次记录了通知调用耗时，但样本不能外推到 50k。

50,000 项先出现一轮严重长尾：创建 41.3 秒、删除 156.7 秒；测试在约 215 秒总观察时间内才看到最终 3 项，因此 `delete-visible=False`、退出码 1。该轮 `ContextIdle` 最大空档约 149 秒，`Normal/Input` 约 87 秒，而专用线程最大间隔 87 ms。Watcher 入队抽样最大 1.77 ms、Dispatcher 批次消费最大 3.38 ms、计数检查最大 0.15 ms；5 次 `Refresh()` 中抽样到的一次同步调用 89.7 ms，完成回调样本 0.16 ms。因为这轮尚未加入逐次原生通知计时，不能把长尾归给通知调用。

同规模 native-only 对照停止产品 watcher 和通知路径：创建/删除后 15 秒观察窗口内 Shell 视图仍为基线 3 项，磁盘已回到 3；因此按预期对照条件退出码 1。`ContextIdle` 最大间隔 387 ms、`Normal/Input` 96 ms，专用线程 23 ms。随后用逐次低频阶段计时复跑 50k 产品组，退出码 0，创建/删除耗时 28.9/15.3 秒，最终视图与磁盘回到 3；`Normal/Input/ContextIdle` 最大间隔 565/564/595 ms，专用线程 26 ms。Watcher 入队最大样本 0.92 ms，Dispatcher 批次消费最大 0.17 ms，计数检查最大 0.41 ms；4 次 `Refresh()` 为 75.7 ms 最大，4 次刷新完成回调为 0.15 ms 最大；本轮一次创建通知 5.52 ms，4 次删除通知最大 43.69 ms。`OnStateChange` 抽样最大 31.7 ms，抽样到的 Shell 窗口消息最大 0.46 ms。

这组结果确认 50k 产品压力的 UI STA 长空档高度波动：一次测试有约 149 秒空窗，紧接的同规模产品复跑低于 0.6 秒；native-only 没有展示 burst 文件，也没有同等级长空窗。队列、计数、Refresh 同步调用、刷新回调和通知的本轮样本均远小于异常空档，但 149 秒样本没有逐次通知数据，不能据此认定 Shell 内部枚举为唯一原因。下一步保留该失败样本，使用全阶段计时重复产品/原生对照并收集异常长尾；在长尾能重复定位之前，不改产品刷新策略，也不宣称 50k 响应达标。测试只覆盖本机隐藏宿主和本地盘，不覆盖可见窗口、网络盘或长期负载。

### 2026-10-24 慢消息阈值降至 10 ms 的 50,000 项复测

在 HostProbe 中将 opt-in 慢消息时间线阈值从 50 ms 降到 10 ms，并通过文件日志保留完整压力轮输出。Release 隐藏生产宿主以退出码 0 完成：创建 50,000 项用时 25.7 秒，删除用时 13.8 秒；产品 watcher 创建与删除各收到 `50,000/50,000`，观察器收到 `49,999/50,000` 且无错误。最终 Shell 视图和磁盘均回到 3 项，`second.txt`/`third.txt` 选区与 `initial.txt` 焦点保持。创建阶段无 fallback；删除阶段触发 3 次计数 fallback，最后一次使视图收敛到 3 项。专用线程最大心跳间隔 21 ms，Dispatcher `Normal/Input/ContextIdle` 最大间隔为 `543/543/603 ms`，连续忙碌采样最长 615 ms。

新增的慢消息时间线记录到多次 `0x4C8/DirectUIHWND` 约 21–61 ms 下游处理，并记录到 `0x4B0/SHELLDLL_DefView` 两次约 228/233 ms 处理；对应时间点附近 Dispatcher 的 `Normal/Input` 间隔约 543 ms，`ContextIdle` 间隔约 548/603 ms。消息处理和 STA 空窗存在时间重叠，但单条已记录消息小于对应间隔，且时间线没有证明其余时间由何种工作占用。全量 `OnStateChange` 55 次最大 32.6 ms；`Refresh()` 3 次最大 140.1 ms，完成回调最大 0.19 ms；Watcher 入队抽样最大 12.1 ms、Dispatcher 队列消费最大 0.14 ms、计数读取最大 0.16 ms、创建通知最大 1.54 ms、删除通知最大 44.4 ms。没有据此改变产品刷新行为或推断私有消息参数语义。

此轮中 Shell 自身刷新多次把选区暂时清零或降为一项；生产回调的完整快照保护与后续 Refresh/延迟恢复最终还原两项选择和原焦点。该压力样本验证了整轮末态与原有保护路径，没有稳定复现“部分快照覆盖完整快照”的故障序列。后续保持 P0 开放，优先为异常时间段增加可关联的 STA 队列/原生调用栈证据；本机 WPR 曾因性能分析策略权限不足而无法开始。结果仅覆盖本机、本地盘、隐藏宿主和程序化文件变化，不代表可见窗口响应、网络盘或其他 Windows 版本。

### 2026-10-24 Ctrl+C/V 命令后的焦点恢复窗口扩展

既有源码路由调查确认：键盘和菜单命令在 Shell 命令返回后，仅排入一次 `ContextIdle` 焦点恢复和一次 250 ms 定时恢复；窗口重新激活会额外恢复。由于这没有关联到 Paste 的异步完成，且隐藏 `IFileOperation` 不能触发系统 Paste，当前无法证明用户报告的焦点丢失是否由恢复过早造成。

现改为立即恢复后保留最多 1.5 秒的观察期，首个 250 ms 检查后每 100 ms 检查。每次只在同一浏览器实例、路径和导航代际仍有效，窗格仍为活动窗格、窗口活动且文件区焦点意图未取消、原生用户输入代数未变化时执行；Shell 视图本身已经有焦点时只等待，不重复 `SetFocus`。切换路径/标签、地址栏/工具栏获得焦点、用户继续输入、窗口失活或超时会取消。

新增 `ShellFocusRestoreGateSmoke` 先因策略接口缺失编译失败；接入门控后验证可继续监视，并覆盖控件焦点、导航/标签变化、新输入、超时停止和避免重复聚焦。App 与 HostProbe Release 构建 0 警告/错误，SettingsSmoke 全套通过。没有读写系统剪贴板、调用真实 Paste、显示窗口或注入桌面输入；因此这证明实现接线与状态门控，不证明 Ctrl+C/V 体验已经修复。异步 Paste 是否晚于原 250 ms 恢复窗口仍是推断，P0 保持开放，后续需在隔离可见窗口复测并查看焦点 trace。

### 2026-10-24 50,000 项 UI STA 关联计时复测

Release 隐藏生产宿主以 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 运行 `--live-shell-directory-stress-large 50000`，退出码 0。Watcher 创建/删除均收到 50,000/50,000 事件，Shell 和磁盘计数最终回到 3；两项选择及独立焦点均保持。创建/删除分别耗时 30.5/51.0 秒，删除后另等待约 5.0 秒。该负载的 Dispatcher `Normal/Input/ContextIdle` 最大间隔为 630/621/1,191 ms，同进程专用线程最大间隔 31 ms；STA CPU 平均约 36%，最长连续忙样本 814 ms。

同轮 `ICommDlgBrowser3::OnStateChange` 122 次，累计约 1,568 ms、单次最大 50.1 ms。`DefSubclassProc` 下游耗时按 HWND 类累计：`DirectUIHWND` 最大 209.2 ms、约 3,099.7 ms 累计；`SHELLDLL_DefView` 最大 265.8 ms、约 511.1 ms 累计。两条 DefView 慢消息分别结束于约 37,172/38,358 ms，与约 621/630 ms 的 Normal/Input 空窗重叠；这再次证明嵌入 Shell 消息处理会占用 STA 时间。最长 ContextIdle 间隔结束于约 65,469 ms，邻近已记录的 DirectUI 处理合计不足以覆盖整个间隔，仍有未归因时间。刷新同步调用 3 次、最大 151.7 ms；Watcher 入队、Dispatcher 批次消费最大分别为 0.98/1.20 ms；计数读取最大 0.21 ms；创建/删除通知最大 0.77/29.0 ms。上述已计时生产阶段不足以解释所有长空窗。

结论仍是定位未完成：Shell 内部消息是可量化的部分成本，但本次 trace 没有覆盖 UI STA 上所有消息泵工作或 Shell 内部调用栈；不解读私有消息参数，不改变 watcher、Refresh 或通知策略。系统已安装 WPR 且当时无其他 trace，但 `wpr -start CPU -filemode` 被系统性能分析策略拒绝（`0xc5585011`），未启动采集；当前也没有 WPA、xperf、cdb 或 ProcDump 可用。调查仅限本机、本地盘、隐藏生产宿主和程序化文件变化，不代表可见 UI、网络盘或长期运行。下一步应在不挂全局 Dispatcher hook 的前提下取得可关联的 STA 消息泵/原生调用栈证据；自动刷新 P0 保持开放。

#### 补验：线程级消息取出时间线

因系统拒绝 WPR CPU 采集，在 HostProbe 增加仅显式启用的线程级 `WH_GETMESSAGE` 观察器（`EXPLORER_DIRS_MESSAGE_PUMP_TRACE=1`）。它只挂当前隐藏宿主的 STA 线程，记录 `PM_REMOVE` 取出的消息数、相邻取出间隔及超过 100 ms 时的下一条消息 HWND/编号；不拦截、不修改消息，不安装桌面全局钩子。合成线程消息 smoke 通过，Release HostProbe 构建 0 警告/错误。

3,000 项低负载对照中观察 11,168 条消息，最大取出间隔 57.2 ms，而 Dispatcher `ContextIdle` 最大间隔为 161 ms；没有大于 100 ms 的消息取出空档。50,000 项运行中观察 130,772 条消息，最大取出间隔 272.9 ms；Dispatcher `Normal/Input/ContextIdle` 最大间隔为 600/600/628 ms。两处时间线相关性较强：约 32,110–32,356 ms 的 `0x4B0/SHELLDLL_DefView` 下游处理为 245.7 ms，消息取出间隔为 245.8 ms，Dispatcher 间隔约 600–628 ms；约 33,394–33,626 ms 的同类消息处理为 231.0 ms，消息取出间隔 231.1 ms，Dispatcher 间隔约 542–564 ms。每处仍有约 0.3 秒 Dispatcher 延迟未由该单次窗口处理解释。

50,000 项运行退出码 0，watcher 创建/删除事件均为 50,000/50,000，最终目录/视图状态和选择焦点检查通过。消息取出时间线把两段约 0.6 秒 Dispatcher 延迟中的约 0.23–0.25 秒与 DefView 窗口处理关联起来；其余时间仍未归因。`WH_GETMESSAGE` 只标记消息被取出，不给出消息处理耗时或 Shell 内部调用栈；与现有 DefSubclassProc 时间线的配对是时间相关证据，不是私有消息语义证明。诊断仅在 HostProbe 显式环境变量下启用，没有生产逻辑变更；自动刷新 P0 保持开放。


#### 2026-10-24 补充：WPF Dispatcher 消息窗口处理计时

此前线程级 `WH_GETMESSAGE` 时间线识别出 `0xC471`，但初版窗口枚举没有覆盖 WPF 的 message-only HWND，因而只能看到消息取出，无法计时其处理函数。HostProbe 的 opt-in 观察器现通过反射定位当前 `Dispatcher` 的内部 message-only 窗口并加上 Win32 子类计时；应用产品代码不使用该私有字段。合成 smoke 向该窗口同步发送注册消息 `DispatcherProcessQueue`，确认窗口过程计时命中，Release 构建与 smoke 均通过。

WPF 官方源码确认 `Dispatcher` 将 `DispatcherProcessQueue` 注册为内部消息，并在对应窗口过程分支调用 `ProcessQueue()`（[Dispatcher.cs](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/Dispatcher.cs#L20-L23)、[WndProcHook/ProcessQueue](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/Dispatcher.cs#L1843-L1878)）。本机 WindowsDesktop.App 10.0.11 反射得到的消息值也是 `0xC471`。因此这一消息现在可按真实 WPF 队列处理路径理解，不再只是未知 HWND 消息。

一次 opt-in 的 50,000 项 Release 隐藏宿主测量中，消息取出数 156,706、最大取出间隔 157.9 ms；Dispatcher 窗口收到 `0xC471` 154,850 次，累计下游窗口处理时间 23,659.5 ms、单次最大 157.9 ms。该最长取出空档与一条 `0xC471` 处理时长相同；同轮 `Normal/Input/ContextIdle` 最大心跳间隔为 200/200/254 ms，专用线程最大间隔 24 ms。该关联解释了部分 STA 空窗，仍无法解释其余间隔，也只有单轮 instrumented 50k 样本，子类计时本身会增加观测开销。

该压力轮创建/删除事件完整、最终视图与磁盘回到 3 项；但原选区应为 `second.txt`、`third.txt`，探针在开始批量删除前已采到额外的 `initial.txt`，因此选区断言未通过、进程退出码为 1。日志尚不能判定该项是在哪个异步刷新恢复阶段加入，不能把问题归因于批量删除。当前只取消息处理时长作为诊断证据；未改产品刷新/通知逻辑，也未在可见桌面操作。后续需独立复现删除选区差异，并在多轮轻量/压力 A/B 下确认 WPF 队列消息耗时的稳定性；自动刷新 P0 仍开放。

### 2026-10-24 50,000 项诊断复测与选区基线修正

前一轮 50,000 项压力样本虽已收敛计数，但退出码 1，原因是删除前探针读到额外的 `initial.txt` 选中项。继续追查后发现压力探针通过 Shell COM 直接执行 `Select All`，再把 `initial.txt` 变为仅聚焦项；这绕过了产品的 native-input hook，导致产品缓存仍保留修改前的 3 项快照，而探针已经把视图变为 2 项。一次带快照诊断的复测直接观察到该不一致（当前 Shell 2 项、保存快照 3 项）。这说明此前失败与探针没有建立用户输入代际基线有关，不能作为产品真实输入路径的选区回归结论。

压力探针现于设好“选中 second/third、焦点 initial”后调用产品输入状态捕获，并等待保存快照的数量与实际 2 项选区一致；若基线无法对齐则拒绝开始负载。修正后 Release 隐藏生产宿主 50,000 项复测退出码 0：创建/删除事件为 `50,000/50,000`，最终 Shell/磁盘计数 `3/3`；刷新前、创建后、批量删除前后选区均为 `second.txt`、`third.txt`，焦点为未选中的 `initial.txt`，选择和焦点断言通过。创建/删除用时 22.8/23.2 秒。

同一轮消息泵观察到 151,033 条取出、最大间隔 900.1 ms；WPF `DispatcherProcessQueue`（`0xC471`）窗口过程 149,254 次、累计 21.50 秒、单次最大 902.0 ms。`0xC471` 的 902 ms 慢调用与 900 ms 消息取出空窗相邻；同轮 `Normal/Input/ContextIdle` 最大间隔 906/962/2,714 ms，专用线程 40 ms。产品 `Refresh()` 同步调用 3 次、总计 1,011 ms、单次最大 844 ms；完成回调最大 0.23 ms。DefView `0x4B0` 单次最大 214 ms，DirectUI `0x4C8` 最大 133 ms。该轮证明 WPF Dispatcher 队列处理、Shell 窗口处理及同步 Refresh 都会占据 STA 时间；ContextIdle 空窗仍显著大于单条最长消息/Refresh，单轮计时也受探针开销影响，不能归因所有停顿或推广为用户响应表现。

没有改产品选区恢复、watcher 或 Refresh 策略；新增修改限于压力测试基线和仅 opt-in 的快照决策日志。自动刷新 P0 保持开放。下一步重复同一正确基线的压力轮，并进一步把慢 `Refresh()` 的起止时间与 Dispatcher 消息、优先级心跳做共享 QPC 时间关联；仍只使用隐藏宿主。

### 2026-10-24 收窄 Dispatcher 消息探针后的 50,000 项复测

复核上一轮探针后发现，它在初始化时枚举并对子窗口逐个安装 WndProc 子类，还对每条消息做字典累计；这会扰动 ExplorerBrowser 的 `DirectUIHWND`/`SHELLDLL_DefView` 消息路径，使这些被测对象自身的长耗时证据不可靠。现移除线程内其他 HWND 的枚举与子类，只对子类化 WPF Dispatcher message-only HWND；合成烟测仍显式添加一个独立 `HwndSource`，验证普通 HWND 与 Dispatcher 消息两条路径。快照只累计 10 ms 以上的 Dispatcher 窗口处理，减少低耗时消息的托管聚合。线程级 `WH_GETMESSAGE` 仍记录当前 STA 的消息取出间隔；这不是零开销采样，Dispatcher 每条 WndProc 仍有开始/结束时间戳，因此数据仍标为 instrumented 样本。

Release HostProbe 构建通过（0 警告/错误），`--message-pump-hook-smoke` 通过，完整 SettingsSmoke 通过。50,000 项隐藏生产宿主复测退出码 0：创建/删除分别 28.3/29.9 秒；产品 watcher 两阶段均收到 50,000 项，最终视图与磁盘为 3 项；删除前选择基线为 `second.txt`、`third.txt`，焦点为 `initial.txt`，最终选择/焦点恢复检查通过。Dispatcher `Normal/Input/ContextIdle` 最大间隔为 446/445/449 ms，专用线程最大间隔 31 ms；`WH_GETMESSAGE` 观察 159,174 条消息，最大取出间隔 415.0 ms。Dispatcher HWND 中 `0xC471` 慢调用（>=10 ms）987 次、累计 16.38 秒、单次最大 414.7 ms；其最大值与约 415 ms 的消息取出空档相邻。产品 `Refresh()` 两次最大 85.1 ms；现有 Shell 消息探针记录 DefView 单次最高 234.5 ms。此次移除广泛子类后仍观察到数百毫秒 STA 空窗，说明先前 902 ms 长尾并非只由对子窗口安装额外观察器造成；但 Dispatcher 子类化及逐消息计时仍有开销，不能将该样本当作无探针基准。

该轮进一步支持 `DispatcherProcessQueue`/Shell 同步工作会占据 STA 的结论，并确认正确选区基线下压力场景可以收敛；剩余空窗仍不能完全归因于单个 Shell 调用。没有改产品 watcher、通知、Refresh 或选区策略，也没有操作可见 GUI。下一步保持自动刷新 P0 开放，先用低负载/同负载重复样本确认趋势，再考虑系统级采样策略是否允许取得原生调用栈证据；若无法取得，则把观察器限制和可重复的 STA 长尾作为当前证据边界。

### 2026-10-24 关闭线程消息泵探针的 50,000 项对照

随后在同样的 Release 隐藏生产宿主中关闭 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE`，再次运行 50,000 项创建/删除。此设置不安装 `WH_GETMESSAGE` 钩子，也不对子类化 WPF Dispatcher message-only HWND；但 HostProbe 和产品原有的 Shell 窗口阶段计时仍在工作，故这是“无线程消息泵探针”对照，不是无观察器/纯产品基准。

用例退出码 0，产品 watcher 创建、删除均为 `50,000/50,000`，最后 Shell 与磁盘计数均回到 3；程序化选择/焦点恢复断言通过。该轮 Dispatcher `Normal/Input/ContextIdle` 最大间隔达到 `1,872/1,873/2,196 ms`，专用线程最大间隔 39 ms。已有 Shell 消息计时观察到 `DirectUIHWND/0x4C8` 单次最高 944.7 ms、`SHELLDLL_DefView/0x4B0` 最高 339.9 ms，时间线上与长空窗相邻。与前一轮启用窄 Dispatcher 探针时约 445–449 ms 最大优先级间隔相比，这一轮更长；两个单样本差异大且轮间 Shell 工作时序不同，不能证明探针带来性能变化或给出未观测基线。

这一轮显示禁用线程消息泵观察器后，数百毫秒至秒级 STA 长尾仍可出现，现有 Shell 消息阶段计时也能看到长处理；但由于应用侧计时仍在，且 WPR CPU 采样先前被系统策略拒绝，仍没有无插桩调用栈证据。产品刷新策略不变，自动刷新 P0 继续开放。后续以多轮交错 A/B 评估波动；若无系统级原生采样权限，优先停止增加逐消息观察器，转向可复现的刷新/选择行为验收和明确标注诊断边界。

### 2026-10-24 更正：自然删除选区丢失是探针基线失配

路线图先前记录的 native-only 单文件删除“清空另外两项选择”，由 `--live-shell-selection-delete-native` 的一轮失败引出。复跑时 selection callback timeline 显示删除前当前视图已是 `selected=2`，产品缓存 `_lastNativeSelection` 却仍为 4；探针通过 Shell COM 设置 Select All、聚焦和取消选择，没有像真实鼠标/键盘选择那样递增产品输入代数并结算新快照。删除后的生产恢复于是正确按它持有的旧四项快照恢复，形成测试状态污染，而不是有效的两项选区回归。

先给探针增加删除前不变量断言，在旧设置下按预期失败：`view=2, saved=4`。再按压力探针已采用的做法，调用生产 `NoteNativeUserInput()` 并等待输入代数结算、保存快照数与当前选区一致。修正后 native-only 真实 `IFileOperation` 删除副本连续两次保留 `second.txt`/`third.txt` 与 `initial.txt` 焦点，fallback=0；显式 `SHCNE_UPDATEDIR`、生产 watcher 删除和“删除前输入”控制各有有效通过样本，选择/焦点均保持。一次 after-input 运行计数 15 秒未收敛，随后的单独复跑 81 ms 收敛，说明自然/通知更新仍有时序波动；该失败不再作为选区丢失证据。

本轮只修正隐藏探针的输入基线和前置断言，没有改产品恢复逻辑。该结果仅验证本机隐藏生产宿主以及受控 `IFileOperation` 路径，仍不代替可见 ExplorerBrowser 和 Explorer 对照。应将前述基线失配的 native-only 选区丢失结论视为被本更正取代；自动刷新 P0 继续开放，下一步针对仍有效的自然收敛延迟、非 GUI 可复现文件操作和压力响应继续推进。

### 2026-10-24 10,000 项自动刷新与 STA 长尾复测

Release HostProbe/App 构建通过（0 警告/错误），完整 SettingsSmoke 通过。隐藏生产宿主运行 `--live-shell-directory-stress-large`，创建/删除 10,000 个文件后退出码 0；产品 watcher 两阶段均收到 `10,000/10,000` 事件，最终 Shell 四窗格计数与磁盘均为 3。`second.txt`/`third.txt` 两项选择及未选中的 `initial.txt` 焦点在创建、删除前后保持。外层观察器分别收到 `9,998/10,000` 创建事件与无错误，产品 watcher 的完整计数独立确认。

创建/删除耗时分别为 7,904/14,101 ms。创建阶段无 fallback；删除阶段一次计数 fallback 在 Shell 实际 10,003 项、预期 10,002 项时启动，最终收敛至 3。删除后的探针 settle 为约 5,001 ms。产品 `Refresh()` 同步调用约 754 ms、完成回调约 0.20 ms；刷新起点约 10,600 ms，期间出现 Normal 队列约 758 ms 空档。Dispatcher `Normal/Input/ContextIdle` 最大间隔分别为 1,472/1,524/3,396 ms，独立线程最大心跳间隔 43 ms，STA 最长连续忙样本约 204 ms。

窗口消息计时中 `DirectUIHWND/0x4C8` 单次 Shell 处理最高约 869 ms，另一条约 810 ms；`SHELLDLL_DefView/0x4B0` 最高约 41 ms。消息计时未覆盖 3,396 ms 的最大 ContextIdle 空档，故只能确认本轮存在长消息和明显队列空档，不能把最大空档归因于某条已测消息。该结果为单轮本机隐藏宿主样本，不代表可见 UI 或其他机器；没有改产品刷新策略。后续应以轻量化、可共享时钟的刷新阶段观测继续定位，并优先核查多秒队列空档期间是否持续处理 WPF/原生消息；自动刷新 P0 保持开放。

### 2026-10-24 创建事件通知派发合并与 10,000 项复测

#### 低插桩对照

前一条逐消息诊断轮还记录到 20,000 次 watcher 数量变化调用和 12,369 次 `ShellDirectoryChangeQueue` Dispatcher handoff；这是独立于创建路径代表项批次的另一条队列，计数也带有插桩，不能当作无插桩基准。它提示下一步需单独观察数量变化队列的合并率和 STA 消费量。

同一隐藏生产宿主以 `--live-shell-directory-stress 10000` 再跑一轮，未设置 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE`，且 `message-pump-trace=disabled`；Shell 窗口/回调逐消息计时均为空。watcher 创建/删除为 `10,000/10,000`，最终 Shell 与磁盘计数均为 3，两项选中项和独立焦点恢复，fallback=0，原生 `SHCNE_CREATE`=2。创建/删除用时 10.75/18.65 秒。尽管没有上述逐消息插桩，Dispatcher `ContextIdle` 最大间隔仍为 7.122 秒，`Normal/Input` 为 3.260/3.275 秒，独立线程最大心跳间隔 72 ms。这说明秒级 STA 长尾不只出现在高插桩轮次；样本不同，不能从间隔大小推断插桩的性能影响。

#### 创建通知队列的派发状态修正

源码追踪发现，每个创建事件先更新 `ShellCreateNotificationBatch`；Dispatcher 执行首个回调时，`TakeForDispatcher()` 就清除 `_dispatchPending`，但原生通知仍要等 60 ms 静默计时器消费。如果 Shell 在此期间仍枚举大量项目，后续创建事件可再次安排 Dispatcher 回调，尽管批次只需要最新路径。

第一次尝试只保留待派发标志、却没有让计时器检查最近一次事件时间；10k 隐藏压力轮因此每隔约 60 ms 发通知，输出 `native-create-notifications=115`、fallback=4。该方案已被丢弃。最终实现保留一次待派发状态到批次实际消费，并记录最近事件时间；计时器触发时若距最新创建尚未安静 60 ms，就重新等待，满足后才取出最新路径。SettingsSmoke 新断言先在旧状态机上因缺少安静期 API 按预期失败；接线后覆盖“窗口内不重复排 Dispatcher”“后续事件重置静默期”“安静后取最新路径”。

修正后的 Release HostProbe/App 构建为 0 警告/错误，完整 SettingsSmoke 通过。另一次 10k 隐藏宿主运行退出码 0：watcher 创建/删除均 `10,000/10,000`，最终 Shell 与磁盘计数为 3，两项选择及焦点保持；原生 `SHCNE_CREATE`=0、fallback=0。创建/删除为 18.87/8.30 秒。该轮 `ContextIdle` 最大间隔 9.645 秒，`Input/Normal` 为 4.437/4.171 秒，独立线程最大心跳间隔 49 ms；Shell 窗口计时关闭，应用内部队列计时也未启用。因此状态机修正保持了压力操作的收敛与选择焦点，但没有证据证明它缩短了长尾，自动刷新 P0 继续开放。下一步应继续追查无产品 `Refresh()` 时仍出现的 STA 长空档，并在可行时测量实际 Dispatcher 回调数量/处理成本；结果不等同于 GUI 响应验收。

### 2026-10-24 数量变化队列的 Dispatcher handoff 合并

在先前 instrumented 10k 样本中，20,000 个 watcher 创建/删除数量变化曾产生 12,369 次 `ShellDirectoryChangeQueue` Dispatcher handoff。队列现保持一个 handoff 活跃到 250/500 ms 计时窗口结束：Dispatcher 回调先取走累计增量但不清除待派发状态；计时器每次计数检查继续合并窗口期间的新增量，直到计时/刷新路径明确释放队列。这样避免 Shell 长时间枚举期间每批 watcher 回调反复派发低优先级工作。

Release SettingsSmoke 覆盖活跃窗口内合并、强制刷新位保留、完整增量以及计时窗口结束后重新派发，通过；App/HostProbe 构建 0 警告/错误。一次 instrumented `--live-shell-directory-stress-large` 在删除前的选择/焦点基线等待 12 秒后失败；探针此前没有在异常分支打印刷新回调时间线，因此这次结果不能单独证明队列回归。加上失败诊断后两轮 10k 隐藏压力均通过：创建/删除 watcher 各 `10,000/10,000`，最终 Shell/磁盘均 3 项，`second.txt`/`third.txt` 与 `initial.txt` 焦点恢复；最新一轮基线等待通过，fallback 3 次。该轮计时记录 20,000 次数量变化只触发 36 次 Dispatcher handoff，Normal/Input/ContextIdle 最大间隔为 127/154/269 ms，独立阶段队列计时最高约 0.19 ms。另一轮的短暂空视图随后完成枚举并恢复选择/焦点；这也说明计数到达不应替代刷新完成及恢复回调检查。

在同一队列改动后的无 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE` 10k 轮中，watcher 双向各 `10,000/10,000`、最终计数/选择/焦点正确，fallback=3；`ContextIdle` 最大空档仍为 4.436 秒，`Input/Normal` 为 1.644/1.374 秒，独立线程最大间隔 91 ms。可见 handoff 合并后计时轮更顺畅，但不同轮负载与 Shell 枚举时序差异很大，不能据此推断派发合并解决 STA 长尾。

以上 handoff 数和优先级空窗来自本机隐藏宿主样本，不能外推为可见 UI 流畅度。第一次 instrumented 失败目前无法在复跑中重现；诊断仅增加探针失败时的状态输出，没有改变产品恢复策略。自动刷新 P0 保持开放；下一步围绕无插桩样本的 4.436 秒 ContextIdle 空档，找出与具体队列/同步 Shell 调用相邻的时间线，继续保留失败路径和选区/焦点校验；不据这些样本调整 Shell 刷新策略，也不代表 Q-Dir/Explorer 可见对照验收。

### 2026-10-24 Dispatcher Timer 回调的细分关联

为判定取消息空档期间的 WPF 队列实际执行内容，HostProbe 的 opt-in `WH_GETMESSAGE` 诊断现在同时记录消息间隔的起止 QPC、慢窗口过程的起止时间，以及 `Dispatcher.Hooks` 观测到的慢操作优先级、回调和参数状态。`DispatcherTimer` 状态额外解析其间隔与 `Tick` 订阅目标；反射仅限 HostProbe 诊断，不进入生产路径。合成烟测先因缺少 Dispatcher 操作时长而失败，加入起止时间及 `DispatcherOperation` 状态解析后通过。

一轮 10k 产品压力把约 1.77 秒消息取出空档与 `DispatcherProcessQueue` 中约 1.89 秒的处理区间重叠起来；随后另一次 10k 样本将耗时回调归属到 `ExplorerBrowserHost.StartDirectoryChangeMonitorCore` 创建的 250 ms `DispatcherTimer`。其中一个回调约 3.16 秒，且线程取消息空档约 3.06 秒。此轮未启用产品阶段计时，不能说明它在 Tick 内具体停在哪里。

再一轮同时开启消息泵与产品阶段计时：10,000 创建/删除 watcher 事件均完整，最终 Shell/磁盘为 3 项，选择及独立焦点恢复，退出码 0。该轮 `directory-timer-tick` 最大 129.7 ms，`Refresh()` 同步调用最大 129.0 ms；计数读取最大 0.180 ms、`ShellDirectoryChangeQueue.Drain()` 最大 0.019 ms，计时器回调最长样本与一次 Refresh 区间重合。`ContextIdle/Input/Normal` 最大间隔为 261/186/156 ms。此结果支持 Refresh 是该轮慢 Tick 的主要同步阶段，但先前约 3.16 秒 Tick 尚未在同配置阶段计时下复现；压力轮差异明显，不能把单轮相关性推广成根因结论。

本轮只新增 opt-in 诊断和合成烟测，没有改自动刷新策略。后续以相同 trace 配置重复压力轮，确认数秒 Tick 是否稳定地由 Refresh 占据，并保留 Shell 选择/焦点与计数验证；在差异缩小前，自动刷新 P0 继续开放。

### 2026-10-24 删除事件 Dispatcher 投递合并与 10,000 项复测

在 `QueueDeletedItemNotification` 的回调类型诊断中，10k 删除 burst 时每个 watcher 事件各自调用 `Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ...)`；慢队列样本等待约 5.7 秒，和 ContextIdle 计时器空档同时出现。根因是 Shell 身份查询虽只处理最终代表路径，但查询前仍把整批删除事件逐个压入 WPF 队列。

修改删除批次为线程安全的单次 handoff：每个 watcher 事件只更新最新路径与最近事件时间；只有从“无待投递批次”转为待处理时才返回需要 `BeginInvoke`，之后事件复用这次 STA handoff。STA 上现有 250 ms ContextIdle timer 持续等到静默期，若期间又有删除则重启等待；静默后取最新路径，并保留既有实际 Shell 项 `SFGAO_FOLDER` 检查、精确路径限制、`SHCNE_RMDIR` 条件及计数 fallback。监视器停止/重启时以捕获的批次对象做身份校验，旧 watcher 回调不会把新监视器批次占住。

先修改 `ShellDeleteNotificationBatchSmoke`，在旧实现下按预期因缺少 `TryRecordForDispatch`/`TakeForDispatcher`/`TryTakeLatestAfterQuietPeriod` 而构建失败。新实现通过：空状态无路径、首事件只排一次、后续路径不增加 handoff、静默期内新事件重置 250 ms 等待、最终消费最新路径并允许下一 burst 开始。完整 Release SettingsSmoke 通过；App 与 HostProbe Release 分别顺序构建通过，0 警告/错误。

详细 trace 的高扰动 10k 轮中，产品 watcher 收到创建/删除各 `10,000/10,000`，删除阶段没有逐事件 `QueueDeletedItemNotification` Dispatcher callback；独立采样器的 ContextIdle 最大排队等待为 `2,397 ms`，但该轮创建阶段选区未恢复而以 exit 1 结束，因此不记为完整功能通过。无队列采样器的同一 Release 隐藏压力轮退出码 0：创建/删除耗时 `6,888/9,379 ms`，两阶段 watcher 完整；Shell 与磁盘最终 `3/3`，原两项选择和独立焦点恢复。全轮计数 fallback 六次，其中创建四次、删除两次；删除最新路径身份扫描两次都识别为文件（`NotFolder`），没有向文件发送 `SHCNE_RMDIR`。产品目录计时仅 37 次总 dispatcher-queue handoff（该计数属于目录变化合并队列，不是删除 handoff 计数），最大 refresh `95.3 ms`，删除通知调用两次合计约 `6.5 ms`。

本修正确认消除了逐项删除回调对 Dispatcher 队列的放大，但无探针对照是一次本机隐藏宿主成功样本；高负载时选区及队列等待仍会波动，不能据一轮推断所有秒级 STA 长尾都已消失。可见 UI 和 Explorer/Q-Dir 对照尚未测试；自动刷新 P0 继续开放。

### 2026-10-24 Dispatcher 优先级负载复测

再次使用相同双 trace 配置运行 10k 产品压力，Watcher 创建/删除均收到 `10,000/10,000`，视图/磁盘最终 `3/3`，选择和独立焦点保持。Dispatcher 的 `ContextIdle/Input/Normal` 最大间隔为 `1,858/1,242/1,232 ms`，取消息最大间隔为 `892.6 ms`。按优先级的 `DispatcherOperation` 统计为：ContextIdle `10,507` 次、累计 `107.9 ms`、最大 `5.9 ms`；Background `1,159` 次、累计 `9,313.4 ms`、最大 `568.5 ms`；Input `437` 次、累计 `10.7 ms`；Normal `438` 次、累计 `30.4 ms`。

此轮最长 Background 操作再次是 `ExplorerBrowserHost` 的目录变化 Tick，耗时 `568.5 ms`；其中 `Refresh()` 同步调用耗时 `568.2 ms`，计数查询最大 `0.192 ms`，队列 Drain 最大 `0.017 ms`。与前一相同配置轮的 Tick/Refresh `129.7/129.0 ms` 一致地表明，Refresh 是目录变化 Tick 慢路径中的主要同步工作；早先约 3.16 秒的单轮 Tick 在详细计时配置下尚未复现，不能解释为稳定上界。

大量 Background 时间与较长 ContextIdle 计时器间隔同时出现，但优先级聚合只报告累计时间，尚未证明哪批具体操作造成计时延迟。下一步已将 ContextIdle 最大间隔的起止时刻与 Dispatcher 操作区间对应；不要仅凭总量调整队列优先级。当前仅是本机隐藏宿主的 instrumented 证据，自动刷新 P0 继续开放。

#### 补验：最长 ContextIdle 计时器间隔内的实际操作

HostProbe 现为每个已完成的 Dispatcher 操作保留有界 QPC 起止区间，并计算它与最长 ContextIdle 计时器间隔的重叠；合成 smoke 先因缺少重叠接口按预期编译失败，接入后 `message-pump-hook-smoke` 通过。10k 双 trace 压力复测退出码 0，watcher 创建/删除均 `10,000/10,000`，最终视图/磁盘 `3/3`，选择与焦点恢复。

本轮最长 ContextIdle 计时器回调间隔为 `2,980 ms`，时间窗 `33,680.2..36,660.6 ms`。同一窗内完成了 `868` 个 ContextIdle 操作，累计区间重叠 `22.5 ms`、单次最大 `2.5 ms`；另有 `61` 个 Background 操作，累计区间重叠 `1,402.1 ms`、单次最大 `155.5 ms`，以及少量 Input/Normal 操作。因 ContextIdle 操作确实在该窗内继续完成，这个计时器最大间隔不能当作 ContextIdle 队列完全饥饿的证据。累计重叠是包含操作时长，不是独占 CPU 时间，也不能单独解释剩余间隔。

窗口内最长的 Background 操作对应目录变化 `DispatcherTimer` Tick，持续 `155.5 ms`；全轮另有一个 Tick/同步 `Refresh()` 达 `634.0/633.3 ms`，计数检查最大 `0.170 ms`、队列 Drain 最大 `0.017 ms`。这确认同步 Refresh 可以造成明显 STA 阻塞，但本轮最长计时器间隔远长于任一被记录 Tick。线程级最大取消息间隔为 `618.7 ms`，专用心跳最大间隔 `37 ms`。因此目前证据是若干可见的 STA 慢回调与 WPF 计时器延迟并存，整个计时器延迟仍未归因；此前“像是高优先级工作持续压住低优先级队列”的推断应撤回。

随后在同一双 trace 配置下再跑一轮 10k 压力：最长 ContextIdle 计时器间隔为 `2,750 ms`，其中只有 `48` 个 ContextIdle 操作区间（累计重叠 `15.2 ms`），同时有 `54` 个 Background 操作（累计重叠 `1,158.8 ms`，单次最大 `53.3 ms`）。该轮测量期间 ContextIdle/Input/Normal 的周期探针总 Tick 数分别为 `365/482/483`；watcher 双向 `10,000/10,000`，最终视图/磁盘 `3/3`，选择与焦点通过。计数说明周期探针可以持续工作，但某一 ContextIdle 探针回调仍出现长间隔；窗口内存在其他 ContextIdle 操作，因此这仍不是队列完全饥饿的证据。Tick 总数也不能判断具体错过了多少次触发，因为 DispatcherTimer 可能延后回调而非逐个补发。

下一步直接核对计时器样本是否遗漏/合并 Tick，以及探针计时器与 Hook 的启动、停止和时间戳边界，再决定是否需要更低扰动的 STA 事件测量。不要据此改刷新策略；自动刷新 P0 保持开放。

### 2026-10-24 WPF 定时器间隔与 Dispatcher 队列延迟区分

核对 WPF 官方文档和源码后，修正前述测量解释：`DispatcherTimer` 不保证严格按 Interval 执行，触发取决于 Dispatcher 队列中的工作；`DispatcherPriority.ContextIdle` 也没有对应的系统“空闲状态”。当前的 50 ms 优先级 `DispatcherTimer` 统计只测得各探针回调之间的间隔，不能直接当成任意 Dispatcher 操作的排队时延。实现中每次 `FireTick()` 先调用 Tick，再通过 `Restart()` 安排下一次单个操作，因此不会按延误时长补发一串积压 Tick。[WPF DispatcherTimer 文档](https://learn.microsoft.com/dotnet/api/system.windows.threading.dispatchertimer)、[WPF 源码 FireTick/Restart](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/DispatcherTimer.cs#L1228-L1324)、[DispatcherPriority 文档](https://learn.microsoft.com/dotnet/api/system.windows.threading.dispatcherpriority)。

为直接量队列等待，HostProbe 增加 opt-in `DispatcherQueueLatencyProbe`：独立 `System.Threading.Timer` 每 50 ms 向 STA 的 Normal/Input/ContextIdle 分别 `BeginInvoke`，记录投递至执行的时延和 ThreadPool 回调间隔；只有设置 `EXPLORER_DIRS_DISPATCHER_QUEUE_LATENCY_TRACE=1` 才加入压力用例。新烟测先因缺少类型按预期构建失败，接入后构建与 `--dispatcher-queue-latency-smoke` 通过，覆盖三个优先级都实际执行并在停止定时源后排空。

相同双 trace 压力配置下，一轮 opt-in 测量投递 750 批、全部排空，独立 ThreadPool 回调最大间隔 55.6 ms；Normal/Input/ContextIdle 最大排队时延 `90.5/99.8/229.7 ms`，同轮原有 DispatcherTimer 最大间隔 332 ms，10k 文件创建/删除、选择焦点和最终计数均通过。重复轮投递 957 批并排空，ThreadPool 最大回调间隔 54.6 ms，排队时延 `139.6/152.9/595.0 ms`，DispatcherTimer 最大间隔 638 ms；但该轮 Shell 视图在删除阶段 15 秒观察后仍有 29 项，而磁盘为 3 项，探针进程退出码 1。匹配双 trace 但关闭独立队列探针的随后一轮通过，最终视图/磁盘 `3/3`，DispatcherTimer 最大间隔 308 ms。失败轮未证明由探针造成；它提示两轮负载时序仍有波动，也提醒独立探针可能改变 Dispatcher 负载。新探针保持显式 opt-in，不将该轮记为功能通过，也不改生产刷新逻辑。

当前较强结论是 50 ms 周期 `DispatcherTimer` 的秒级最大间隔不稳定复现；独立排队探针在最近两轮记录到最大 230/595 ms 的排队时延，但不覆盖整个压力操作，也不能证明此前秒级间隔一定只是定时器语义。下一步保留失败样本，降低独立探针频率/负载后再交错比较开关，观察是否能复现秒级队列延迟及目录计数不收敛；自动刷新 P0 继续开放。

将独立探针降为每 `250 ms` 一批后再跑同配置 10k 压力：共投递并排空 `116` 批，ThreadPool 回调最大间隔 `251.8 ms`；Normal/Input/ContextIdle 最大排队时延 `36.1/44.0/136.6 ms`。同轮优先级 DispatcherTimer 最大间隔 `119/120/220 ms`，同步 Refresh 最大 `89.9 ms`；创建/删除 watcher 各 `10,000/10,000`，Shell 与磁盘最后均 3 项，选区和焦点恢复，退出码 0。该轮没有复现秒级队列等待或视图计数失败；与前述 50 ms 两轮相比负载时序也不同，不能将差异归因于采样频率，但低频探针足以取得有用队列时延样本且扰动更低。后续以 250 ms opt-in 探针为主，保持无探针对照交错复测，不改产品刷新策略。

### 2026-10-24 DispatcherTimer 排队延迟探针校正与 10,000 项复测

复核 trace-only 10k 记录时，发现消息泵 Hook 报告许多约 500 ms Background DispatcherTimer“队列延迟”，但同轮取消息最大间隔只有约 113 ms。根因在采样边界：WPF `DispatcherTimer.Restart()` 先创建 Inactive `DispatcherOperation`，随后设置 due time 并注册 Win32 timer；到期才把已有操作提升到目标优先级。`DispatcherHooks.OperationPosted` 因而早于计时器实际到期。原 Hook 使用 OperationPosted→Started 全跨度，将预设 interval 计入了排队延迟。参见 [WPF DispatcherTimer.Restart/Promote/FireTick](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/DispatcherTimer.cs#L1228-L1324)。

先扩展 `message-pump-hook-smoke`：150 ms DispatcherTimer 遇到 400 ms 的 STA 阻塞，旧实现报告 `400.8 ms`；无阻塞的 250 ms timer 也被错误列为慢队列操作。改为从 DispatcherTimer 的 `_dueTimeInTicks` 计算到期后的超时，并另存 `post-to-start-ms` 原始跨度；非计时器操作保持原 Posted→Started 算法。烟测还要求闲置 timer 不生成慢队列记录，并核对被阻塞 timer 的实测超时与 `post-to-start - interval` 在 50 ms 内一致。校正前回归以这些 flags 失败，修正后 `message-pump-hook-smoke` 通过；独立 `--dispatcher-queue-latency-smoke` 与完整 SettingsSmoke 也通过。

使用仅消息泵 trace、关闭独立队列采样器的 Release 隐藏 10k 轮复测退出码 0：创建/删除耗时 `6,943/7,793 ms`，Watcher 创建/删除各 `10,000/10,000`，最终 Shell/磁盘 `3/3`，两项选区和独立焦点保持。校正后记录到的慢队列样本（≥100 ms）最高 ContextIdle `215 ms`、Background `167 ms`；50 ms ContextIdle timer 回调间隔最大 `264 ms`、取消息最大间隔 `117.5 ms`；目录 `Refresh()` 同步调用最高 `122.3 ms`。本轮没有复现秒级队列等待。

与此前启用独立 250 ms 队列采样的轮次（ContextIdle `2,397 ms`，创建阶段选区失败）不构成严格 A/B：Shell 枚举和输入时序不同，不能判定探针造成延迟，也不能说明长尾已解决。相反，本轮修正显示先前消息泵 Hook 对 DispatcherTimer 的长延迟数值包含 interval，不能再作为真实队列等待引用；独立 `System.Threading.Timer` 通过 BeginInvoke 所测延迟仍是单独的队列探针证据。自动刷新 P0 继续开放，保留同配置复测与可见 UI 验收。
### 2026-10-24 校正消息泵探针后的第二轮 10,000 项复测

在上次校正 `DispatcherTimer` 起点后，再次以 Release 隐藏生产宿主运行 `--live-shell-directory-stress-large 10000`，仅启用 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE=1`，独立 `System.Threading.Timer` 队列探针关闭。进程退出码 0；产品 watcher 创建/删除均收到 `10,000/10,000`，最终磁盘与 Shell 视图均为 3 项，`second.txt`/`third.txt` 选择及未选中 `initial.txt` 焦点均恢复。

创建/删除分别耗时 `5,420/3,140 ms`，Shell settle 均在操作结束时已完成；目录刷新同步调用各两次，最大 `73.7/63.6 ms`，选择与焦点恢复分别约 `49/47 ms`。STA 消息取出最大间隔 `71.0 ms`，Dispatcher `Normal/Input/ContextIdle` 最大间隔 `110/115/219 ms`，专用线程最大间隔 `21 ms`。当前样本未复现秒级长空档。

计数 fallback 创建、删除各一次：创建时预期 `10,003`、Shell 计数读到 `9,454`，随后 Shell 自行达到 `10,003`；删除时预期 `10,002`、读到 `10,003`，之后 Shell 视图逐步回到 3。两次恢复都保留选择与焦点。日志能确认时序和最终状态，但不能证明若不刷新会何时自然收敛，也不能据此判定 fallback 多余或正确。该轮与旧样本 Shell 时序不同，不构成严格 A/B；不据一轮通过调整 250/500 ms 窗口或刷新规则。自动刷新 P0 继续开放，真实可见窗口、其他驱动器和长时间运行仍未验收。

### 2026-10-24 生产 Shell 文件操作与自动更新复验

Release 隐藏生产 HostProbe `--live-shell-product-monitor` 退出码 0。该轮通过生产 watcher 与 Shell 视图，依次执行单文件创建、重命名和删除，跨窗格复制/删除/移动，以及文件夹创建/删除；每个目标窗格显示相应结果，其他窗格未被误改。最初多选和未选中的焦点文件在 mismatch/in-flight Refresh 后均恢复，最后四个视图都回到 3 项。

整轮产品监视记录 9 次 fallback，其中 mismatch `2` 次、刷新进行中合并的新变化 `1` 次；单文件重命名使用强制刷新。文件夹创建没有计数 fallback，也没有误发文件 `SHCNE_CREATE`；文件夹删除发生 1 次 fallback，产品没有发送 `SHCNE_RMDIR`。结果可作为当前受控 Shell 操作链正确性的证据，但这是多步组合样本，不能把 9 次 fallback 全归因于单一操作；也没有逐项记录每次 fallback 的实际必要性或操作时延。

本轮没有显示窗口、模拟键鼠、改系统配置或使用用户文件。它不证明可见菜单/对话框焦点，也不是低扰动性能样本；自动刷新 P0 仍开放。下一步应在不使用高频持续采样的前提下，逐项记录单文件及跨窗格操作的 fallback 起因、计数和 settle 时间，再决定是否有证据调整产品阈值。

### 2026-10-24 文件夹删除提前与合并 RMDIR 对照

为拆开“产品合并后发送 RMDIR”和“watcher 到达后立即发 RMDIR”的时间差，Release 隐藏 HostProbe 交替运行 `--live-shell-product-monitor-early-rmdir-none` 与 `--live-shell-product-monitor-early-rmdir` 各三次。每轮都先让隔离文件夹进入 Shell 视图，再由 `IFileOperation::DeleteItem` 删除；确认 watcher 事件到达、4 项旧行仍在且 Shell stale item 的 `SFGAO_FOLDER` 属性有效后，`RMDIR` 组才发送一次 `SHCNE_RMDIR | SHCNF_PATH | SHCNF_FLUSH`。

需要准确解释 `NONE`：它只表示探针不额外手动提前发送通知，产品自己的 250 ms 静默合并批次仍活动，且三轮都自动发送了 1 次产品 RMDIR；因此这不是没有 RMDIR 的控制组。该组 0/3 在删除开始后 250 ms 内显示为 3 项，最终均收敛，fallback 均为 0。手动提前通知组有 2/3 分别在 140/153 ms 内显示且产品延迟批次没有再发送 RMDIR；第 3 轮未在 250 ms 内显示，随后产品通知 1 次并经 1 次 fallback 收敛。所有轮次退出码均为 0，最终计数均为 `3,3,3,3`。

结果提示直接提前发送正确类型的通知可能加快部分单文件夹删除，但仅三组样本，且人工发送是在探针确认旧项仍存在后立即执行，不能据此改写产品规则，也不代表批量目录删除。项目现行规则仍要求删除路径先合并 250 ms、再按 Shell stale item 类型确认发送 RMDIR；以后若要改变该策略，应先完成更多交替轮次和可见验收。没有显示窗口、操作用户文件或改变产品代码。

### 2026-10-24 Shell 回调计时开关隔离与 10,000 项交错复测

复核隐藏压力探针配置时发现，`EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 同时启用 `ExplorerBrowserHost` 的阶段诊断和 `ShellBrowserSite` 每次回调计时。回调计时在每个回调进入/退出时读取 Stopwatch 并执行原子计数，无法独立评估这层测量是否改变压力结果。现将 `ShellBrowserSite` 的细粒度计时分离为 `EXPLORER_DIRS_SHELL_CALLBACK_TIMING`；受支持的 HostProbe 模式仍在该变量未显式设置时默认打开，设为 `0` 可只关闭此层计时并保留既有主阶段跟踪。Release HostProbe 构建 0 警告/错误，完整 SettingsSmoke 通过；运行时开关通过输出中 callback samples 非零/全零确认。

随后交错执行 `callback timing=1,0,1,0` 四轮隐藏生产 `--live-shell-directory-stress-large 10000`，没有启用高频线程消息泵或独立队列采样器。四轮均退出码 0；产品 watcher 创建/删除均为 `10,000/10,000`，最终磁盘和 Shell 视图均为 3 项，`second.txt`/`third.txt` 多选及未选中 `initial.txt` 焦点恢复。开启组创建 `4,342/4,758 ms`、删除 `2,810/3,093 ms`；关闭组创建 `3,842/3,963 ms`、删除 `3,143/3,742 ms`。开启组 ContextIdle 最大探针间隔 `270/335 ms`，关闭组 `236/295 ms`；Input/Normal 间隔开启组 `148/147` 与 `175/175 ms`，关闭组 `114/121` 与 `189/189 ms`；专用线程心跳开启组 `21/22 ms`、关闭组 `27/25 ms`。

开启组的 `OnStateChange` 计时总量分别 `82.8/55.9 ms`，其它主要 Shell 回调类型在该用例没有被调用；两组仅各两轮，创建/删除时间的差异方向不一致，不能证明插桩令 STA 变慢或变快，也不能排除小幅影响。测量仍包含产品阶段计时、Dispatcher 周期计时和心跳探针，不是无观察器基线。此轮没有复现秒级 STA 长尾，但此前长尾波动仍未解释；不改产品自动刷新规则，P0 继续开放。

### 2026-10-24 大批量 watcher 活动期间的部分 Shell 基线与延后兜底

先把 50k fallback 时间线扩展为只读 `batch` 基线/增量、`ShellDirectoryChangeQueue` 待处理增量及累计 watcher 事件数。Release 隐藏产品宿主中，创建期间的 `FileSystemWatcher` 事件仍在持续到达，但异步 Shell `Refresh()` 会在分批枚举中暂报部分或回退的项目数。旧状态机在连续两次刷新不匹配后执行 `Rebase(observed)`，把当时部分 Shell 计数采纳为基线；例如创建事件累计已达 `27,170` 时，记录到 batch baseline `9,181` 且 queue 另有 `190` 个增量。继续到达的 watcher 事件随即在错误基线上累计，负预期基线和重复 fallback 因而出现。这是对本机隐藏样本和本地路径的机制证据，不说明所有延迟都来自同一原因。

最小改动是在大差异（至少 32 项）期间检查最近 watcher 事件时间：最后事件后不足 `750 ms` 时继续等待并重置有限 settle 检查数，让同一文件操作先静下来；安静后继续已有最多 12 次的枚举等待。单项差异仍直接进入兜底，重命名与 watcher 溢出 `ForceRefresh` 仍立即进行。烟测先因三参数行为缺失而编译失败，再于实现后通过；覆盖活动大批次重新开始等待、安静后恢复有界等待、单项差不延迟和强制刷新绕过门控。App/HostProbe Release 构建无警告/错误，完整 SettingsSmoke 通过。

改后 Release 10k 隐藏轮退出码 0：watcher 创建/删除 `10,000/10,000`，最终 Shell/磁盘一致，选区和焦点恢复；创建后 1 次 fallback，完整删除阶段累计 2 次。创建耗时 `4,342 ms`、删除 `2,219 ms`，Shell/Dispatcher 的 ContextIdle 最大采样间隔 `186 ms`，专用线程心跳 `21 ms`。单文件创建隐藏探针 43 ms 完成磁盘操作、74 ms watcher 到达、126 ms 首次可见，fallback=0。

改后 50k 隐藏轮退出码 0，墙钟 `57,362 ms`，创建/删除 `17,790/14,895 ms`，创建后 settle `5,435 ms`；watcher 创建/删除 `50,000/50,000`，最终 Shell/磁盘各 3 项，选择与焦点恢复。总 fallback 2 次；唯一创建 fallback 发生在写入结束后，记录的 baseline 与 expected 均为 `50,003`；另一条单项删除差异也保留为 `50,002`，没有出现负基线。ContextIdle/Input/Normal 最大间隔 `927/754/754 ms`，专用线程心跳最大 `23 ms`；仍观察到 Shell `DirectUIHWND/DefView` 单条慢处理，故本轮不能关闭所有 STA 长尾问题。

旧样本最高记录 15 次 fallback，并出现负预期基线；改后仅一轮 50k 且本机负载/Shell 枚举速度存在明显方差，不能把差值全部归因于这项门控，也不能证明其它操作系统版本或网络目录相同。当前证据支持修复了“持续大批量事件中把部分 Shell 计数当基线”的状态机风险；没有可见 GUI、慢速/网络盘或长期并发验收，自动刷新 P0 继续开放。

### 2026-10-24 第二轮 50,000 项自动刷新门控复测

为确认上节的大批量活动门控可重复，在相同 Release 隐藏生产宿主模式下再次执行 `--live-shell-directory-stress-large 50000`，保留 Shell/Dispatcher 主阶段 trace，同时关闭逐回调计时、消息泵 trace 和独立队列延迟采样器。进程退出码 0，墙钟 `61,866 ms`；创建/删除分别 `21,290/14,447 ms`。创建 settle `3,132 ms`，随后检查窗口再运行 `5,010 ms`。

Watcher 创建与删除均为 `50,000/50,000`，产品事件也为 `50,000/50,000`，观察器事件为 `49,998/50,000`，错误 `0/0`。最终磁盘、Shell 四窗格计数均回到 `3`；两项选择、未选中的焦点文件在创建和删除后均恢复。创建及删除各自 fallback 均为 0；时间线里没有 fallback 样本或负基线。专用 Dispatcher 心跳最大间隔 `176 ms`，Shell 数量/选区/焦点单次读取最大分别 `0/34/23 ms`。

与前一轮 50k 相比，本轮没有创建阶段 fallback（前一轮有一次在 Shell 全量计数稳定后发生），进一步支持“活动大批量 watcher 期间先等待安静期”避免部分枚举重设基线的效果；不能把 fallback 数量或耗时差异单独归因为该门控，Shell 枚举与主机负载仍有波动。本轮关闭了消息泵和队列高频 trace，所以 `176 ms` 是专用心跳观测值，不是 STA 取消息空档或 ContextIdle 间隔；它也不能解释此前 10k 压力样本中观测到的秒级 STA 长尾。自动刷新 P0、长尾原因、可见 GUI 与网络/慢速目录验收继续开放。

### 2026-10-24 10,000 项自动刷新消息泵 trace 复测

为复查已有秒级 STA 空窗证据，用 Release 隐藏生产宿主运行 `--live-shell-directory-stress-large 10000`，启用 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE=1` 与 Shell 阶段诊断，关闭逐回调计时及独立 Dispatcher 队列延迟探针。进程退出码 0。创建/删除分别耗时 `3,395/2,437 ms`；Watcher 产品事件均为 `10,000/10,000`，最终 Shell/磁盘回到 3 项，原两项选择与独立焦点恢复。

本轮创建 fallback 在操作完成后观察到 `4,927` 项（期望 `10,003`），删除阶段在第一次删除事件后读到 `10,002` 项（期望 `10,002`）却仍经一次 fallback 完成；两次刷新均重新恢复了原选择和焦点。采样时间线在刷新过程中短暂出现空选择和空焦点记录，之后恢复；因此只证明采样结束时恢复，不证明刷新期间视觉状态连续。

消息泵共处理 `3,603` 条消息，STA 取消息最大间隔 `77.9 ms`；ContextIdle/Input/Normal 最大间隔 `263/111/111 ms`。专用线程心跳最大间隔 `22 ms`。WPF 定时器校正后的最大队列延迟 `216 ms`，Background dispatcher callback 最大耗时 `84.9 ms`；慢 WndProc 最大 `85 ms`，Directory timer tick 最大 `84.9 ms`，其中同步 Shell `Refresh()` 最大 `84.4 ms`。本轮没有重现此前观测到的多秒空窗。

该 trace-only 单轮不是长期稳定性结论；没有证明高频独立采样器与长尾之间的因果关系，也未覆盖可见窗口、网络/慢速目录或不同系统负载。自动刷新 P0 与秒级长尾解释继续开放，不因本轮指标收敛而关闭。

### 2026-10-24 50,000 项 Shell 选择事件时间线与低扰动复核

为把每次选择版本递增与恢复步骤放在同一时间轴，`ShellBrowserSite` 现在在 `SHELL_CALLBACK_TRACE=1` 下保留最多 128 个 `OnStateChange` 类型/时间点；该事件计数独立于 `SHELL_CALLBACK_TIMING`，避免为低扰动 trace 打开每回调计时。HostProbe 在压力开始时重置这条事件时间线，并输出 Shell 状态计数和 `SelectionVersion` 起点。App/HostProbe Release 构建 0 警告/错误。

关闭逐回调计时，仅开启消息泵与 Shell trace，执行 Release 隐藏生产宿主 50k 创建/删除探针，退出码 0。开始 SelectionVersion 为 7；压测过程记录 67 个 `change=2` 回调，结束计数 `state=2:67`，没有 change 3/4。事件从 `1,624 ms` 起分布到 `65,756 ms`，跨过创建和删除操作；探针在此期间没有注入键鼠输入。目录阶段 trace 记录产品显式 `Refresh()` 调用 0 次，这组 `SelectionVersion` 增量来自 Shell 自然更新期间的 `CDBOSC_SELCHANGE`，不是 watcher fallback 刷新。

同轮消息泵最大取消息间隔 `168.6 ms`；ContextIdle/Input/Normal 最大间隔 `263/183/176 ms`。这比之前逐回调计时开启样本的 `1,250.6 ms` 空窗小，但两轮负载与调度不同，不能将差异因果归于计时开关。计数与时间线证明 Shell 异步更新可在无用户输入、无显式产品刷新时递增选择版本，因此该版本门控不能简单解释成“用户改了选择”。它尚未把每个回调与对应 Shell item count、selection count 和焦点变化一一绑定；需继续区分 Shell 清选、默认焦点变更与应用发起的恢复。产品代码仍保留版本变化后完整恢复的门控，自动刷新 P0 继续开放。

### 2026-10-24 50,000 项 Shell 选择回调来源复核

为区分 deferred restore 前的 Shell 选择版本变化与用户输入，把 `ShellBrowserSite` 的 `CDBOSC_*` 状态计数从逐回调计时开关独立出来：`EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 记录状态类型，`EXPLORER_DIRS_SHELL_CALLBACK_TIMING=0` 不启动每回调 Stopwatch。Release HostProbe/App 构建 0 警告/错误。

低扰动 50k Release 隐藏宿主 trace 轮退出码 0。压测阶段前 callback 快照为 `state=2:7`，结束为 `state=2:82`，增加 75 个状态回调；没有出现 change 3（rename）或 change 4（state change）。`ShellBrowserSite.OnStateChange` 中 change 2 对应 `CDBOSC_SELCHANGE`，并递增 `SelectionVersion`。探针在创建/删除阶段没有生成键鼠输入，只有开始前建立一次选择基线，因此该组版本增量来自 Shell 选区回调，而非探针用户输入。

Watcher 创建和删除均为 `50,000/50,000`，最终磁盘/视图各 3 项，选择和独立焦点恢复；创建 fallback 0、删除 fallback 1。消息泵最大间隔 `237 ms`；ContextIdle/Input/Normal 最大间隔 `550/546/546 ms`。创建/删除更新中途曾观察到选择清空与逐步恢复，说明这些回调计数混合了 Shell 刷新期间的选择变化和产品发起的恢复操作；单纯计数不能证明哪一个回调触发了 deferred timer 的版本门控。

对照的逐回调计时开启轮记录到 85 个状态回调、累计计时约 `782 ms`、单次最大 `62.7 ms`，同轮最大消息泵空窗 `1,250.6 ms`；低扰动关闭计时轮记录 75 个回调及 `237 ms` 消息间隔。两轮负载/调度不同，不能据此将空窗归因于插桩或回调本身。下一步需在低扰动时间线上关联每个状态回调、`SelectionVersion`、deferred timer 和选区数量变化，再决定安全减少完整恢复的条件。

### 2026-10-24 延迟焦点恢复期间避免重复选区批次

50k trace 中 deferred restore 的一个回调包含选区 `SelectAndPositionItems`、focused PIDL 存在性扫描和 `SelectItem`/焦点验证。为避免选区已恢复且 Shell 选择版本稳定时重复重放批次，现让 deferred restore 保存“选区已恢复”状态：选择版本不变时只尝试恢复焦点；Shell 选择版本变化时清除该状态，下一次稳定检查再完整恢复选区与焦点。焦点 PIDL 仍暂缺时继续等待，不提前改焦点；窗口/路径、用户输入和重试期限门控不变。

先扩展 `ShellSelectionRestoreGateSmoke`，在实现缺失时按预期编译失败，再添加策略并运行全套 SettingsSmoke，通过。App/HostProbe Release 构建 0 警告/错误。新隐藏 HostProbe 稳定选区用例在选区已恢复、版本未变时看到 deferred 时间线没有 `selection-batch-api`，最终保留 `second.txt`/`third.txt` 选择和 `initial.txt` 焦点；原 deferred 用户输入取消用例仍通过。

Release 10k 压力轮退出码 0、无 fallback、最终计数正确且选择/焦点恢复；本轮没有触发 deferred 重试。改后 50k trace 退出码 0，Watcher 产品事件完整，创建阶段无 fallback、删除阶段一次，最终回到 3 项且选择/焦点恢复；消息泵最大间隔 `275.9 ms`，ContextIdle/Input/Normal 最大间隔 `1,417/557/572 ms`。这轮 Shell 选择版本在初次刷新后发生变化，故 deferred 路径仍执行一批完整选区恢复；与前一轮相比没有证明高负载回调调用次数下降。稳定版本路径的隐藏用例证明了跳过机制可用，但尚未证明它降低自然 50k 样本的总体延迟。

下一步追踪 `SelectionVersion` 变化对应的 `CDBOSC_*` 原因与是否伴随新用户输入，判断 Shell 异步清选/部分选择回报何时必须触发整批恢复；不能单凭“用户输入代际未变化”跳过，以免覆盖 Shell 自然变化后的正确选区状态。自动刷新 P0 继续开放。

### 2026-10-24 Shell 选择版本变化后的精确集合复核

50k 低扰动时间线确认 `SelectionVersion` 也会因 Shell 自然更新而改变；此前“版本变化就整批重放”的策略虽安全，但当 Shell 最终自行恢复了原选择时会做重复工作。现在延迟恢复比较当前文件视图的选中 PIDL 集合与捕获快照：完全一致则跳过 `SelectAndPositionItems`，继续恢复独立焦点；枚举失败或任何项目不同则执行原整批恢复。比较只遍历已选项，且按枚举顺序逐项比对；顺序不同会安全地走完整恢复路径，不会误判为一致。

Release HostProbe 覆盖稳定与变化两条分支。稳定探针创建两项选择和一个未选中焦点，排入延迟恢复后人工发出 Shell `CDBOSC_SELCHANGE` 版本通知但不改选择集合。旧行为先复现为 `selection-batch-api count=2` 且测试失败；实现后 trace 为 `selection-current matched=True`，无批量 API 调用，最终两项选择与未选中焦点均保持。差异探针在排队后将当前选择改成 `initial.txt` 单项并触发版本通知；trace 为 `selection-current matched=False`，随后 `selection-batch-api count=2,succeeded=True`，回到 `second.txt`/`third.txt` 选择及 `initial.txt` 独立焦点。两探针退出码均为 0，延迟恢复用户输入取消用例通过。Release `--live-shell-refresh` 和 `--live-shell-refresh-one-pane` 显式刷新用例通过；SettingsSmoke 全套通过，App/HostProbe Release 构建 0 警告/错误。

新逻辑后的 50k Release 隐藏压力退出码 0，产品 watcher 创建/删除为 `50,000/50,000`，观察器为 `49,999/50,000` 且错误 `0/0`。创建阶段没有计数 fallback；删除阶段在操作结束后有 1 次 fallback，最终 Shell 与磁盘计数为 3。创建期间两项选择与独立焦点持续保留；删除刷新中视图短暂清空选择，deferred 时间线先检测不匹配并执行批次 API，再恢复原两项选择与焦点。ContextIdle/Input/Normal 最大间隔 `658/581/581 ms`，专用线程最大间隔 `37 ms`。此轮开启了消息泵与 Shell 诊断，且没有等配置的前版本性能对照，不能把时间变化归因于集合比对。

本轮已覆盖精确相等跳过、集合不同时完整恢复、常规显式刷新、用户输入取消和 50k 自然压力最终状态。逐次选择短暂变化、可见 ExplorerBrowser 界面、网络盘/慢速目录仍未验收；长尾最高仍有数百毫秒，自动刷新 P0 继续开放。

### 2026-10-24 50,000 项消息泵 trace 负载长尾定位

为观察大目录负载下的长尾，在 Release 隐藏生产宿主运行 `--live-shell-directory-stress-large 50000`，开启 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE=1` 和 Shell 主阶段诊断，关闭逐回调计时及独立高频队列采样。进程退出码 0；产品 watcher 创建/删除事件均为 `50,000/50,000`，观察器分别 `49,999/50,000`，错误 `0/0`。时间线最终回到 3 项、原两项选择和独立焦点；创建与删除各触发一次非强制计数 fallback，均在相应磁盘操作结束后发生。创建 fallback 读到 `49,835`（预期 `50,003`），删除 fallback 读到 `50,003`（预期 `50,002`）；批次基线保持为当前预期值，没有负基线。

消息泵处理 `8,313` 条消息，最大 STA 取消息间隔 `407.2 ms`，其中一条下一消息为 `0x4CC/DirectUIHWND`。同一时段的 `0x4B0/SHELLDLL_DefView` Shell 处理最高 `407.15 ms`；另有一条 Shell DefView 消息约 `259.6 ms`。ContextIdle/Input/Normal 最大间隔分别 `874/724/724 ms`，校正后 ContextIdle 队列延迟最高 `825 ms`；Background Dispatcher callback 最大 `259.4 ms`，专用线程心跳最大间隔 `26 ms`。

时间线还记录了约 `259.6 ms` 的 HostProbe WndProc，其执行重叠于生产 `ScheduleDeferredRefreshSelectionRestore` callback；这只能证明时间重叠，不能断定消息泵空隙全部由该 callback 导致。该轮没有秒级空窗，但确认 50k 负载会出现数百毫秒 Shell 与 Dispatcher 延迟。自动刷新 P0、长尾因果、可见 UI 及慢速/网络目录仍未验收。

### 2026-10-24 关闭消息与 Shell 回调 trace 的 50,000 项控制复测

为确认前一轮开启 `WH_GETMESSAGE`/Shell 消息 trace 时观测到的长间隔是否仅由诊断钩子造成，运行同一 Release 产品隐藏宿主 50k 压力，显式设置 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE=0`、`EXPLORER_DIRS_SHELL_CALLBACK_TRACE=0`、`EXPLORER_DIRS_SHELL_CALLBACK_TIMING=0`。HostProbe 仍保留其常规 20 ms 心跳、Shell 计数读取及每 50 ms 的选区/焦点检查，因此这不是完全无插桩性能基准。

用例退出码 0。创建/删除耗时 `41,067/41,641 ms`，生产 watcher 两阶段均为 `50,000/50,000`，观察器为 `49,999/50,000`、错误 `0/0`；Shell/磁盘最终均为 3 项。创建阶段在操作完成后看到 `actual=49,172/expected=50,003` 并触发一次 fallback，创建 settle 约 `5,558 ms`；删除阶段两次 fallback，最终收敛。刷新期间选择曾短暂变空，末态 `second.txt`/`third.txt` 和独立 `initial.txt` 焦点均保留。

关闭两个消息 trace 后，ContextIdle/Input/Normal 最大间隔仍为 `786/577/595 ms`，常规心跳最大间隔 `557 ms`，独立线程心跳最大间隔 `28 ms`；Dispatcher STA 平均 CPU 样本约 `30.1%`，最长连续忙样本 `716 ms`。与最近开启消息 trace 的 50k 样本相比，量级相近，但样本差异不能证明 trace 没有任何性能影响，也不能据单轮归因于 Shell 内部枚举。它只表明关闭这些 trace 后，数百毫秒 STA 长间隔仍然存在；逐次 Shell/WPF 消息调用栈尚不可见。WPR CPU 采集先前被本机系统性能分析策略拒绝，当前不调整通知、刷新等待或 Dispatcher 优先级。下一步保留这些行为和时序边界；获得系统级原生调用栈证据后再定位长间隔，否则优先继续可复现的用户可见更新及时性验证。自动刷新 P0 继续开放。

进一步对照同轮的 `shell-selection-restores` 时间戳与源码：deferred restore 的 `selection-seed` 到 `selection-batch-api` 约 `54 ms`；之后检查旧 focused PIDL 是否仍在视图中约 `78 ms`；再执行焦点选择并验证约 `73 ms`。这三个间隔合计约 `204 ms`，与同轮约 `259 ms` 的 deferred-restore Dispatcher callback 在量级上相符。源码路径确实在每次 `RestoreSelectionAfterRefresh` 中重放整批选区、扫描视图查找 focused PIDL，再执行 Shell 焦点选择。该关联让“重试期间重复 Shell 恢复调用”成为可检验的性能假设，但计时段包含 COM/Shell 处理且有重入消息，不能简单相加当作严格调用耗时；目前也没有证明仅此路径造成全部 407 ms 消息间隔。

下一步应在保留“异步枚举暂缺旧焦点时不跳焦点”和“新 Shell 选择变化后允许重新恢复”的行为前提下，验证是否可在选择版本稳定时跳过重复的整批选区恢复；先覆盖连续重试与选择版本变化的逻辑回归，再用相同 50k trace 对照 fallback、选区/焦点最终状态和 callback 时间。当前没有改此恢复逻辑。

### 2026-10-24 目录变化监视器启动窗口源码复核

源码读取发现：每次导航后，生产视图先每 250 ms 读取一次 Shell 项目数；必须连续得到 5 个相同的有效计数，才创建并启用该路径的 `FileSystemWatcher`。计数仍不稳定时最多尝试 40 次（约 10 秒），之后启动计时器停止且不再创建 watcher。故从导航完成到 watcher 启用之间，或计数持续不稳定超过上限时，产品 watcher 没有接收文件系统变更；Shell 原生通知仍是主要更新路径，因此这是监视器兜底的覆盖缺口，不等于已复现用户可见的陈旧列表。

下一步应在可安全验证的隔离路径中设计“监视器先启动、计数后稳定”的基线交接，特别区分稳定期内 Shell 已反映的 watcher 变更与仍需 fallback 的变更；在此之前不把初始部分计数直接用作基线，以免大目录异步枚举触发错误刷新或丢失变更。该风险尚无可见桌面复现，本轮只做源码追踪，未启动 ExplorerBrowser、HostProbe 或 GUI。

### 2026-10-24 启动期目录变更现在先由 watcher 缓存

生产路径现先创建并启用 `FileSystemWatcher`，再以 Shell 项目数建立稳定基线。稳定期事件保留在合并队列中，创建/删除/重命名/监视器溢出都会标记强制刷新；基线建立时将队列原子交给正式批次，随后用现有 `Refresh()` 选择/焦点恢复路径收敛。启动事件通知 Shell 的同时仍留有批次 fallback，避免“Shell 已反映事件”与“Shell 仍显示旧数”的竞态被基线计算掩盖。

纯逻辑采样器仍要求 5 个连续相同有效计数；40 次 250 ms 快速检查后改为每秒检查，持续枚举时不再停止 watcher 启动流程。SettingsSmoke 新用例覆盖稳定计数、无效计数重置、原 40 次上限后的降频等待及最终建立基线；Release SettingsSmoke 全量通过，App Release 构建为 0 警告/错误，改动文件 `git diff --check` 通过。

本轮没有创建 ExplorerBrowser、HostProbe 或可见窗口，因此只验证采样状态机和编译/配置回归；事件到达启动队列、Shell 通知和 watcher 同时工作、慢速/网络目录，以及刷新后的可见选区/焦点仍待隔离运行验收。

### 2026-10-24 Ctrl+C/V 历史焦点 trace 与多窗格复现范围

复核仓库已有的 `src/.state/focus-probe/focus-events*.log` 后，发现这些样例使用的隔离 profile 为 `Layout=1`（单窗格），且 trace 日期为 2026-10-15，早于当前焦点恢复代码。若干 Ctrl+V 路径中，Shell `TranslateAccelerator` 从约 0.8 秒到 1.44 秒后才返回；期间 `DirectUIHWND` 收到失焦，随后恢复到该视图。恢复后的 Down/Up 消息有 Shell `S_OK` 样本并改变 focused index。快速 Ctrl+V 样本也有连续 Down/Up 处理记录。

这些旧日志支持“Shell 粘贴处理可能同步阻塞约一秒并改变原生焦点”的观察，但不能说明当前代码行为，也不能覆盖用户报告的多布局现象；单窗格日志中的方向键成功不是多窗格修复证据。日志没有捕获当前四窗格下 Ctrl+V 返回后所聚焦的 UI Automation 项目及随后的物理方向键，因此不据此调整 1.5 秒恢复窗口。

为让下次隔离验收直接覆盖该差异，`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 已改为在独立四窗格布局中为四个窗格加载各自的临时目录；第一窗格同时包含搜索夹具。2026-10-24 的新准备副本 `src/.state/gui-smoke/20261024-114523-911-c787db` 的 INI 确认 `Layout=4`、四个窗格各一个标签，夹具目录各有五个样例文件和一个空文件夹；准备过程仅发布隔离副本，没有启动窗口、输入或改剪贴板。当前 EXE SHA-256 为 `CF0021D56FB6937CE0E0457623D160D6ECD76A2192C75BCF265738DA24369286`。真实四窗格 Ctrl+C/V 与方向键复现仍待桌面验收。

### 2026-10-24 监视器启动稳定样本的重入保护

复核 `StartDirectoryChangeMonitor` 发现，启动采样 Tick 在读取 Shell 项目数之前确认了监视器与导航状态，但 `ReadDirectoryMonitorItemCount()` 通过 `IFolderView` 执行 Shell COM，可能泵消息并重入导航。旧回调返回后若不重新确认，就可能把旧目录的第五个稳定计数交给 `StartDirectoryChangeMonitorCore`，替换新目录的正式批次/计时器。

修复将监视代际、导航代际、当前就绪状态、已完成磁盘路径和队列引用捕获在 `ShellDirectoryMonitorRequest` 中；计数读取前后各检查一次。过期回调停止自己的启动计时器，仅当队列仍属于它时才清理监视器，不会停止新目录的监视器。SettingsSmoke 覆盖独立代际、已就绪新视图、路径及队列变化；全量 Release SettingsSmoke 与 App Release 构建通过。未启动 ExplorerBrowser 或 HostProbe，所以尚未观察真实 COM 重入调度与可见文件视图行为。

### 2026-10-24 目录监视计时器提前返回路径复核

检查 `CreateDirectoryChangeTimer` 后，确认刷新中或延迟选区恢复中会停止当前 tick 并返回。两条路径都有恢复点：watcher 自己发起的 `Refresh()` 在完成回调中重新启动目录计时器；延迟恢复结束或取消时，`CancelDeferredRefreshSelectionRestore(restartDirectoryMonitor: true)` 重新启动计时器。导航换代则由 `StopDirectoryChangeMonitor` 清理旧计时器。因此没有证据表明此提前返回会令当前监视器永久停摆，本轮不改该路径。

Release `SettingsSmoke` 全套通过，涵盖目录队列并发、监视器启动采样、刷新/焦点恢复门控和 Shell 位置导入等无窗口回归。该测试没有创建 ExplorerBrowser，也不覆盖实际计时器交错、文件系统事件到可见视图的收敛或 STA 长尾；自动刷新 P0 继续开放。

### 2026-10-24 watcher 与 Shell 同时静默时的检测边界

源码复核确认，常规目录轮询先从 `ShellDirectoryChangeQueue` 取出 watcher 的净项目数变化与 `ForceRefresh`，再将它们并入 `ShellDirectoryChangeBatch`，与 Shell 当前项目数比较。无队列事件时增量保持为 0；若 Shell 视图也仍返回基准计数，则 `ShouldDeferRefresh` 不要求刷新，而 `Complete` 认为批次已匹配并继续轮询。因此此路径依赖 watcher 或 Shell 至少有一个通道报告变化；若两者都没有报告，且 watcher 未发出 Error，当前计数协调器本身没有独立磁盘枚举可发现这次变化。

这是依据当前状态机得到的检测边界，不是已复现的文件系统故障，也不说明真实 Shell/FSW 对同一操作一定会同时漏报。既有 `FileSystemWatcher.Error` 会请求强制刷新；大目录、网络位置和可见更新仍需分开验收。没有据此增加定时目录扫描或改变刷新策略，以免在未测量 I/O 成本前增加持续枚举。

### 2026-10-24 创建/删除净计数抵消时强制协调

继续核对生产事件合并时发现，队列与批次只保留创建/删除的净增量。例如先删除一个条目再创建另一个条目，`+1/-1` 抵消后预期项目数仍等于基线；若 Shell 也没有更新，常规计数比较会认为视图已收敛。这与重命名和 watcher 错误已有的强制刷新处理不同，是源码可达的身份变更漏检路径。

新增回归先在旧逻辑上失败，确认净增量为零时 `Complete()` 返回“不刷新”。现在队列在一个计时器派发窗口内保留是否出现创建与删除，跨队列 Drain 的批次也各自保留两种事件；两类事件均出现时设置 `ForceRefresh`，即使净增量为零也走现有 Shell 刷新/选区焦点恢复路径。单一类型的创建或删除仍沿用计数协调，不增加持续磁盘枚举。

Release SettingsSmoke 全套通过，覆盖同队列窗口分多次 Drain、跨批次合并、生产顺序的队列到批次接线、相同基线计数下触发刷新及单一创建/删除不强制刷新的回归。另在唯一临时目录用真实 Windows `FileSystemWatcher` 删除一个文件、创建另一个文件，将两条真实事件送入生产队列与计数批次，确认实际计数相同仍要求刷新；测试退出时清理夹具目录。App Release 构建 0 警告、0 错误，改动文件 `git diff --check` 通过。未启动 ExplorerBrowser，Shell 漏报时实际可见条目替换、同时成批操作的刷新成本、网络目录和焦点体验仍待验证；这关闭一个计数净额检测缺口，不关闭自动刷新 P0。

### 2026-10-24 FileSystemWatcher 缓冲区溢出恢复路径复核

微软文档说明，`FileSystemWatcher` 内部缓冲区溢出会丢失文件系统事件，并建议用 `Error` 事件处理；.NET 文件提供程序源码进一步将 `InternalBufferOverflowException` 描述为 watcher 仍在运行但丢弃了事件，因此消费者必须重新扫描。生产 `ExplorerBrowserHost` 已订阅 watcher `Error`，映射为零计数增量的强制刷新；`ShellDirectoryChangeBatch` 让强制刷新绕过大批量等待，并走现有 Shell `Refresh()` 与计数协调。相关纯逻辑 smoke 已覆盖 Error 映射及强制刷新。

当前 watcher 缓冲区为每个监视目录 16 KiB。微软文档指出该缓冲区占用不可分页内存，建议在足够容纳事件的前提下尽量保持较小；因此本轮没有在缺乏实际溢出观测时扩大缓冲区。文档与源码只证明错误处理接线和逻辑策略，不证明本机真实溢出后可见视图收敛；大目录/网络位置及持续运行仍需后续隔离验收。

### 2026-10-24 watcher 失效后的同路径恢复

用独立临时目录在本机 Windows 复现 watcher 生命周期：监视目录被删除时，同一个 `FileSystemWatcher` 发出 `Error`（异常类型 `Win32Exception`）；随后重建相同目录并创建文件，该 watcher 没有收到 `Created`。该结果说明当前 `Error` 后仅强制刷新不足以恢复失效句柄；测试使用独立 C# 控制台夹具，无 UI，也未启动 ExplorerBrowser。

生产目录监视器现为每个活动目录持有恢复策略。首次创建 watcher 失败或运行中收到 Error 时，都会通过 Dispatcher 安排重建；Error 另沿原队列请求 Shell 刷新。首次等待 250 ms，重建失败后按 500 ms、1 s 等指数退避，最大 30 s，多个故障共用一个待处理尝试。监视器停止或导航换代会退休旧恢复策略。重建成功后再入队一次强制协调，以覆盖 watcher 脱离期间的变化。SettingsSmoke 覆盖重试合并、退避上限、成功后退避重置、停止后拒绝新重试，以及监视器退休后迟到的失败回调不能重新安排重试。完整 SettingsSmoke、App 与 HostProbe Release 构建通过，0 警告/错误。

尚未在生产 ExplorerBrowserHost 中运行此恢复路径；真实网络断开/重连、权限恢复、可见列表收敛、选择焦点恢复及第三方 Shell 行为仍待隔离验收。缓冲区溢出会复用同一恢复流程；本项不宣称已解决全部自动刷新 P0。

### 2026-10-24 STA Refresh 长耗时的计时边界复核

复核 `ExplorerBrowserHost` 的计时接线后，确认已有 `_directoryRefreshCallTiming` 包围的是整个 `Refresh(Action?)` 包装方法，而非单独的 `IShellView.Refresh()`：其同步区间还包括获取当前视图、捕获选区、调用 Shell 刷新及排入恢复回调；刷新后的选区恢复由后续 Background Dispatcher 操作执行。`ReadDirectoryMonitorItemCount()` 在目录 Tick 中单独计时，已有隐藏样本最大值约 `0.192 ms`，但刷新前的视图获取与选区捕获尚未分项测量。

因此既有 `568.2 ms` 等数据证明产品同步刷新慢路径会占用 STA，不足以把全部时长归因于 `IShellView.Refresh()` 本身。下一步应在隔离性能探针中分别记录视图获取、选区快照、原生 Refresh 调用三个同步阶段，再以低扰动对照决定可优化点；线程迁移或跳过原生刷新会改变 Shell 语义，本轮没有做此类改动。该结论来自源码计时边界复核，不是新的运行测量。

为下轮测量准备，`DirectoryChangeStageTimingSnapshot` 新增 `refresh-get-view`、`refresh-selection-capture`、`native-refresh` 三项，分别覆盖获取当前 Shell 视图、获取 `IFolderView` 并捕获选区、调用 `IShellView.Refresh()`。计时仅在 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 时启动，复用 HostProbe 已输出的阶段快照；关闭诊断时不启动这些 Stopwatch。App Release 构建 0 警告/错误，完整 Release SettingsSmoke 通过。没有运行 HostProbe，因此新字段是否会在真实刷新样本中产生有效分解值仍未验证；这只是准备诊断，不是性能优化结果。

本轮只做源码到输出的口径复核：HostProbe 通过反射读取并打印 `DirectoryChangeStageTimingSnapshot`。`refresh-call` 截止到同步 `Refresh(Action?)` 返回，包含其内部 COM/快照/入队开销；`refresh-get-view`、`refresh-selection-capture`、`native-refresh` 是其内部阶段；`refresh-completion` 则在稍后的 Background Dispatcher 回调中单独计时，包含完成时的状态检查、计数重读与选区恢复安排。每个阶段配置为逐次记录，因此可比较同一轮的次数、累计耗时和最大值；不要把各阶段的最大值相加，也不要把异步完成时间当作同步 STA 调用时长。本轮未运行 HostProbe，没有新增压力数据或性能结论。

### 2026-10-25 大目录 Shell 计数停滞时提前兜底

先以 Release 隐藏生产宿主运行 3,000 项创建/删除压力并启用 Shell 阶段计时。4 次 `Refresh()` 共耗时 244.1 ms，其中 `IShellView.Refresh()` 共 242.4 ms、最大 68.2 ms；获取视图最大 0.006 ms，选区捕获最大 0.395 ms。创建/删除各触发两次 fallback；创建后的视图在 fallback 前仍停在 3/3,003，随后刷新完成时到达 3,003。关闭计时再跑相同规模的旧逻辑对照，创建计数收敛为 5,257 ms，且 Shell 计数在 t=1,496 ms 到 t=7,347 ms 之间停留在 4 项。由此定位到大差异分支的固定等待：Shell 计数没有推进时仍可消耗最多 12 个 500 ms 检查。

现在大差异分支继续保留 12 次总上限；每次计数推进会重置停滞计数，连续 4 次读到相同计数便走已有刷新兜底。强制刷新、小差异和 watcher 仍在活动的处理保持原样。新 smoke 先在旧实现上因稳定计数仍被延迟而失败；修复后完整 Release SettingsSmoke 通过。无插桩隐藏生产宿主复测 3,000 项两次，创建收敛分别为即时和 1,215 ms；10,000 项一次，创建收敛 1,900 ms，创建/删除 watcher 均收到完整事件，最终四窗格及磁盘计数一致，选区和独立焦点均保持。由于 Shell 时序有波动，旧样本与新样本不是严格交错 A/B；这些本机隐藏宿主结果不证明可见 UI、网络盘或其他 Windows 环境的时延。没有运行 GUI，自动刷新 P0 仍开放。

### 2026-10-25 生产 watcher Error 回调与重建路径

先尝试在 ExplorerBrowser 正打开的目录上制造真实监视器失效；目录删除操作被 Shell 宿主同步阻塞，停止了该隔离进程，没有触碰用户目录。随后改用受控注入：从生产 `FileSystemWatcher` 的真实 `Error` 委托调用 `InternalBufferOverflowException`，使 `ExplorerBrowserHost` 按原回调路径入队强制协调并启动恢复。Release HostProbe 隐藏生产宿主退出码 0；确认原 watcher 被替换、新 watcher 已启用，随后新 watcher 观察到创建事件，文件视图收敛至 `4/3/3/3`，最终触发 1 次刷新。该结果验证 Error 委托至生产重建链路和重建后的继续监听，不是 Windows 内核实际缓冲区溢出的复现，也不覆盖真实可见窗口或网络盘恢复。一次已中断的实验留下了一个 HostProbe `bin/Release/.../fixtures` 下的孤立临时目录；删除尝试被执行环境策略拒绝，因此未进一步清理。

### 2026-10-25 FileSystemWatcher Changed 与全视图刷新调用链复核

源码显示，`FileSystemWatcher` 监视 `LastWrite`、`Size`、`Attributes`、`CreationTime` 等变化；此前任何 `Changed` 都会强制整视图同步刷新。现改为：有效的绝对路径进入逐路径、忽略大小写去重批次，静默 250 ms 后通过微软公开的 `SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path)` 通知 Shell 更新条目。单批最多保留 128 个不同路径；批次溢出或路径不符合 `SHCNF_PATH` 的 `MAX_PATH` 限制时，保留原全视图刷新兜底。创建/删除/重命名、Watcher Error 和计数不一致路径不变。

先新增隔离回归，确认旧 `ForChanged()` 会在计数不变时要求全视图刷新，断言按预期失败后才改生产接线。Release SettingsSmoke 验证 Changed 不再要求整视图 fallback、同路径去重、多个路径完整保留、250 ms 静默期和超过 128 条时回退；完整 Release SettingsSmoke 及 App Release 构建通过（0 警告/错误）。这些检查验证队列策略和源码接线的构建，不证明当前 ExplorerBrowser 视图会因 `SHCNE_UPDATEITEM` 正确重绘，也没有新的 STA 性能数据。微软文档还说明 Shell 可将同目录大量 `SHCNE_UPDATEITEM` 合并为一个 `SHCNE_UPDATEDIR`；因此验收需检查目标条目或目录内容最终可见更新，不应要求接收端逐条收到原始事件。下一步须在获准的隔离窗口中检查文件大小/修改时间可见更新、选区/焦点保持、路径长度边界和刷新耗时；如果通知未能更新当前视图，再恢复或调整兜底，不能据接口定义宣称用户体验已经改善。

来源：[Microsoft `SHChangeNotify` / `SHCNE_UPDATEITEM` / `SHCNF_PATH`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify) 与 [`SHChangeNotifyRegister`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister)。本轮未启动 ExplorerBrowser 或 GUI。

### 2026-10-25 Changed 通知更新生产 Shell 文件视图快照

新增 `HostProbe --live-shell-changed-metadata`，通过真实生产 `ExplorerBrowserHost` 和隔离文件夹测试 Changed 更新链路。探针切换到详情视图并固定名称/修改日期/类型/大小列，选择且聚焦 `initial.txt`；同时把 `initial.txt` 从 7 字节写成 128 KiB，并把未选中的 `second.txt` 从 6 字节写成 64 KiB，两者修改时间均前移。生产 watcher 将两个 Changed 事件合并为一批，独立 Shell observer 收到 `SHCNE_UPDATEITEM (0x2000)` 的两条路径；从当前 `IFolderView2` 行快照读回，两项大小分别变为 `128 KB` 与 `64.0 KB`。通知在约 828.7 ms 发出，两行在约 860.8 ms 内更新；选区与焦点路径保持不变，整视图 fallback 计数增量为 0，隐藏父窗口未显示、前台句柄未变。HostProbe Release 构建 0 警告/错误，探针退出码 0。随后完整 Release SettingsSmoke 通过，覆盖多路径批次、去重/静默合并及 `MAX_PATH` 路径策略边界。

这验证了本机隐藏生产宿主中的 FileSystemWatcher Changed → 生产多路径按项 Shell 通知 → 当前 Shell 行快照更新链路；它不是可见像素/DirectUI 重绘验证，也不证明四窗格可见交互、其他 Shell 提供程序或多路径突发性能。下一步仍需在可见隔离窗口核对实际大小/修改日期单元格、排序、焦点与长路径边界。

### 2026-10-25 Changed 元数据更新后的原生排序复核

扩展同一隐藏生产宿主探针，按 `System.Size` 升序排列 7 字节的 `initial.txt` 与 6 字节的 `second.txt`，随后将两者分别改写为 64 KiB 与 128 KiB。两条 Changed 路径都收到 `SHCNE_UPDATEITEM`，当前 Shell 行快照正确读出 `64.0 KB|128 KB`，但两次探针的排序顺序都仍为旧值 `second.txt|initial.txt`；ShellBrowserSite 只收到 CDBOSC 选择变化，未收到 CDBOSC_STATECHANGE。连续两次运行中，先切换到名称排序再恢复大小排序可令行顺序变为 `initial.txt|second.txt`，总耗时约 0.94–1.03 秒，选区和焦点仍保留，fallback 增量为 0。

因此当前按项通知链证明了属性更新，却没有证明或实现活动排序重排。显式同值重设或清空后恢复排序没有触发重排，必须先切到另一排序键再恢复，说明恢复策略需要保留完整复合排序/分组并避免不必要的视图抖动。尚未接入产品：固定延时无法证明慢速 Shell 提供程序已完成属性更新，当前 ShellBrowserSite 回调也没有可用的状态完成信号。下一步应继续在隐藏宿主验证异步排序切换时序与当前多列排序/分组的保留；若无法建立可靠时序，再评估按项更新后是否需要受限的计数/刷新兜底。两轮结果仅限本机隐藏 ExplorerBrowser，不证明可见排序指示器、像素绘制或其他 Shell 提供程序。

### 2026-10-25 Changed 字段影响排序时补发目录更新

随后对比 `SHCNF_FLUSHNOWAIT` 与后台线程 `SHCNF_FLUSH`。后者约 27–62 ms 返回，但至少一次返回时视图行仍保留旧大小和旧排序；ShellBrowserSite 也没有给出对应的状态完成通知。因此不能把 SHChangeNotify 的同步投递标志当作当前 ExplorerBrowser 行缓存已更新或已重排的屏障。

基于前一轮 `SHCNE_UPDATEDIR` 对照能在元数据已更新后触发重排，生产 Changed 批次现先发每条 `SHCNE_UPDATEITEM`，再读取当前视图状态；如果任一排序键或分组键是 `System.Size`（Basic 属性 12）、`System.FileAttributes`（13）、`System.DateModified`（14）或 `System.DateCreated`（15），就对 Changed 路径所在目录补发一条 `SHCNE_UPDATEDIR`。状态读取失败时保守地补发目录通知。其他排序/分组不发送目录通知，继续只用按项更新。一个批次按不同父目录去重后各发一次。这样不靠固定等待猜测元数据何时到达，也不为名称排序和类型分组触发额外目录枚举。

SettingsSmoke 新增策略回归：大小排序、复合排序中的修改时间、属性排序及创建时间分组要求目录更新；名称排序与类型分组不要求；属性格式 GUID 不匹配时不得误判。完整 Release SettingsSmoke 通过，App/HostProbe Release 构建 0 警告/错误。隐藏生产宿主未额外注入 flush 时，生产通知序列为两条 `SHCNE_UPDATEITEM` 加一条 `SHCNE_UPDATEDIR`；两文件大小从 `7|6` 字节交换为 `64|128 KiB`，行顺序在约 854 ms 变为 `initial.txt|second.txt`，选区/焦点保留、计数 fallback 增量为 0。名称排序对照仅收到两条 UPDATEITEM，不发 UPDATEDIR，顺序保持名称升序。

该修正验证的是本机普通文件夹的大小排序和“无需目录更新”的名称排序对照。尚未验证元数据分组的原生行为、不同 Shell 提供程序、云/网络目录、大批量 Changed 期间目录更新的 STA/枚举成本、可见排序指示器及像素重绘；本轮未运行 GUI，也未发布版本。下一步应在隐藏宿主增加分组与多路径负载/响应测量，再等待可见桌面验收。官方接口说明 `SHCNF_FLUSH` 等到通知送达受影响组件，`SHCNF_FLUSHNOWAIT` 则在投递开始后返回；这不等同于应用视图完成重排。[SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)。用到的属性键分别为 [System.Size](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-size)、[System.FileAttributes](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-fileattributes)、[System.DateModified](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-datemodified) 和 [System.DateCreated](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-datecreated)。

### 2026-10-25 Changed 元数据分组生产宿主对照

HostProbe `--live-shell-changed-metadata` 新增 `EXPLORER_DIRS_HOSTPROBE_CHANGED_GROUP=modified` 隐藏测试开关：在真实生产 `ExplorerBrowserHost` 上先设置 `System.DateModified` 分组，再通过生产 watcher 修改选中/聚焦的 `initial.txt` 和未选中的 `second.txt`。两次 Release 样本中，生产 observer 均收到两条 `SHCNE_UPDATEITEM` 与一条目标目录 `SHCNE_UPDATEDIR`；大小及顺序分别在约 903/907 ms 内收敛，选区/焦点保持，`DirectoryChangeFallbackRefreshCount` 增量为 0，父 HWND 隐藏、前台句柄未变，且未注入 `SHCNF_FLUSH`。这支持“活动元数据分组键会触发目录更新”的本机普通文件夹路径证据，但没有读取/断言 Shell 分组标题或 DirectUI 像素，因此不等同于可见分组体验已验收。

早期两次重跑曾在 `ChangeNotificationObserver` 注册阶段以 `Shell change registration failed` 退出，发生在文件改写和生产通知断言之前。随后给失败诊断增加 HWND、根路径、PIDL 地址、注册/事件标志及 `SetLastError` 捕获值；新构建后分组探针连续 5 轮通过（两条 UPDATEITEM、一条目标目录 UPDATEDIR、无整视图 fallback）。之后一次 32 路径首轮也在注册阶段返回 0，记录到 `hwnd=0x10061E84`、本机临时目录 PIDL、`sources=0x9003`、`events=0x7FFFFFFF`、`entries=1`、`last-error=0`；随后同构建的 2 文件控制、32/128 文件负载均成功注册和运行。另一个不创建 ExplorerBrowser 的 Shell 更新通知 smoke 通过。当前可确认失败不是 watcher、写文件或 Shell 行快照断言阶段；注册返回 0 的根因仍未知。Microsoft 文档仅说明 0 表示内存不足或参数无效，不据 last-error=0 判定具体原因。[SHChangeNotifyRegister](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister)。HostProbe Release 构建通过，0 警告/错误。没有运行可见 GUI或发布版本。

2026-10-25 后续对同一个不创建 ExplorerBrowser 的 `--shell-update-notification-smoke` 串行运行 20 次：13 次退出码为 0；7 次失败，其中 1 次 `SHChangeNotifyRegister` 返回 0（参数及 `last-error=0` 与早期样本一致），另 6 次注册后 5 秒内没有收到任何观察消息（锁失败为 0、事件计数为空）。因此“注册失败”和“注册后通知未送达”是两种不同失效阶段，不能合并归因于注册参数；20 次循环会频繁创建/删除独立临时目录，本身也可能影响 Shell 通知时序。当前结果证明该 observer smoke 不稳定，不证明 ExplorerBrowser 生产 watcher 丢失文件变化；仍需用只做注册/注销的生命周期探针与单次低频通知控制继续隔离，保持不改产品通知策略。

注册/注销生命周期对照已补：同一个 HWND 重复注册/注销 40 次全部成功；每轮新建 HWND 并在注销后立即销毁的 40 次中仅 14 次注册成功；每轮新 HWND 注销后先保留接收窗口 100 ms 再销毁，则注册与注销均 40/40 成功，前台句柄不变且未创建 ExplorerBrowser。探针因此将可复现的高失败率缩小到“快速新 HWND 生命周期”路径，但还不能解释跨进程 smoke 的所有失败。one-shot smoke 在注销后加入 100 ms 窗口存活期后，另 10 轮为 7 次通过、1 次注册失败、2 次投递超时；样本不支持宣称已修复，仍需分别验证进程退出后的 Shell 清理与通知投递。探针记录注销返回值且注销幂等；没有改变产品的 FileSystemWatcher 或 SHChangeNotify 策略。

随后增加 200 ms 进程间隔的重复脚本。初次 10/10 通过未能稳定复现：再次按同样间隔跑 10 次，8 次通过、2 次投递超时；把注册后的准备等待从 150 ms 加到 1000 ms 后再跑 10 次，7 次通过、3 次注册失败、0 次投递超时。相反，把 200 ms 放在 HWND 销毁之后但仍在进程退出之前，10 次只通过 6 次。当前只可推断准备等待影响通知投递超时，而注册失败有独立的不稳定因素；没有足够证据把进程间隔判为根因。新增 `scripts/shell-notification-smoke-repeat.ps1` 可控制运行次数、准备等待和进程间隔，后续继续隔离 API 注册返回 0 与 Shell 消息未送达两条路径。

### 2026-10-25 Changed 多路径负载收敛与 Shell 合并

扩展隐藏 HostProbe `--live-shell-changed-metadata`：通过 `EXPLORER_DIRS_HOSTPROBE_CHANGED_BURST_COUNT` 创建并一次改写 32 或 128 个临时文件，生产视图设置为 `System.Size` 升序并按 `System.DateModified` 分组。两组都通过：全部行的大小快照改变且顺序符合新元数据，32 项约 1,521 ms 收敛，128 项约 2,439 ms 收敛；选区和焦点保持，`DirectoryChangeFallbackRefreshCount` 增量为 0，父 HWND 隐藏、前台未变。Shell observer 每组只收到 1 条 `SHCNE_UPDATEDIR`（目标显示为 root-or-other），没有逐项 UPDATEITEM；这是 Shell 对同目录通知的合并结果，不能据此判断生产发送端少发了按项通知。

128 项场景的生产阶段计时为 watcher queue 512 次调用/8 个采样/最大 0.144 ms，Dispatcher handoff 1 次，directory timer tick 1 次/0.010 ms，queue drain 1 次，count check 1 次/0.009 ms；refresh-call 与 native-refresh 均为 0。32 项场景 128 次 watcher queue 调用、1 次 handoff、1 次计数检查，同样没有整视图刷新。测试规模与批次硬上限一致；未测试超过 128 个 distinct path 时的全视图兜底成本、慢/网络 Shell provider、四窗格同时变更或可见绘制。结果是本机单个普通临时目录的一次负载样本，不足以给出普遍时延承诺。

单独运行 Release HostProbe 构建和同一轮 2 文件控制均通过；SettingsSmoke 已在上一轮完成且该处只新增了 HostProbe 诊断/负载逻辑。本轮没有运行可见 GUI，也没有发布版本。后续超限实测见下节。

#### 2026-10-26 128 路径分组复测与探针准备竞态

当前 HostProbe 首次复跑 128 文件、`System.Size` 升序、`System.DateModified` 分组时，在文件改写前的 `SelectOnlyItem` 断言失败，实际选择集为空。失败发生在调用 `SetSort`/`SetGroup` 后立即读回选择的准备阶段，尚未修改夹具文件；因此它不构成 watcher 或 Shell 通知回归失败。探针现等待完整行快照连续三次（20 ms 轮询）一致后再设置选择，若 5 秒仍未稳定则明确以准备阶段错误退出。

修正后以 `EXPLORER_DIRS_HOSTPROBE_CHANGED_BURST_COUNT=128`、`EXPLORER_DIRS_HOSTPROBE_CHANGED_GROUP=modified`、`EXPLORER_DIRS_HOSTPROBE_SKIP_FLUSH=1` 重跑 Release HostProbe，退出码 0。Shell observer 收到一条 `SHCNE_UPDATEDIR`（root-or-other；Shell 可合并同目录事件），128 行大小均更新，最终顺序为 `initial.txt`、`second.txt`、`changed-002.txt` 至 `changed-127.txt`；选区与焦点保持，生产整视图 fallback 增量 0。总耗时 2,220.2 ms；阶段计时为 watcher queue 512 次/8 个采样/最大 0.153 ms、Dispatcher handoff 1 次、directory timer tick 1 次/0.027 ms、count check 1 次/0.019 ms，未调用 `Refresh()`。原始日志：`src/.state/changed-metadata-128-group-recheck-20261026-092740.log`。

这是隐藏本机普通文件夹与生产 ExplorerBrowserHost 的数据/API 读回，不是可见分组标题或像素验收，也不覆盖网络/云 Shell、四窗格并发 Changed 或慢速提供程序。此次只修正探针准备等待，没有改产品 Changed 通知路径。

#### 2026-10-26 129 路径分组超限 fallback

以相同的大小升序、修改时间分组夹具，将 Changed burst 增至 129 个路径并跳过测试额外 flush。Release HostProbe 退出码 0：超过 128 路径上限后 observer 没有收到按项更新事件，生产 fallback 计数恰增 1；129 行大小全部更新，行顺序与新大小一致，选择/焦点保持且前台未变。总耗时 2,389.0 ms；单次 `Refresh()` 17.088 ms、native refresh 15.582 ms，fallback 完成后 Shell 行快照已收敛。原始日志：`src/.state/changed-metadata-129-group-overflow-20261026-092904.log`。

这验证的是本机隐藏 ExplorerBrowserHost 的超限分支；不表示能从 Shell observer 逐项看到全部自然文件系统事件，也不证明可见分组标题、网络 provider 或多窗格并发更新。

### 2026-10-25 Changed 多路径超限刷新兜底

将 HostProbe Changed 批次测试上限扩到 160，并使超限场景等待生产 `DirectoryChangeFallbackRefreshCount` 增量后再采集行快照，避免自然 Shell 文件系统更新先到达而过早读取 fallback 计数。当前样本：128 条走按项通知和一条 Shell 接收端目录更新，整视图 fallback 为 0，约 2,601 ms 收敛；129 条超出 128 distinct-path 限额后 `fallbackDelta=1`，全部 129 行及顺序约 2,370 ms 收敛，`IShellView.Refresh()` 用时 75.895 ms；160 条同样仅触发 1 次 fallback，全部 160 行及顺序约 3,141 ms 收敛，刷新用时 153.540 ms。两次超限样本均保留选区、焦点和前台句柄，父 HWND 未显示；160 条记录 644 次 watcher queue 调用、1 次 Dispatcher handoff/tick/count check。各次 Shell 接收端的目录通知可能由文件系统自然更新合并产生，不能将其归因到产品 `Refresh()`。

一次较早的 160 条探针读取了行快照后立即取 fallback 计数，误报 `fallbackDelta=0`；修正探针为先等待计数后，同样本确认 fallback 执行且验收通过。另一次因 foreground 基线在视图准备前采集而报告前台变化；将基线移到写入前后复测，前台保持不变。该收窄只修正测试观察窗口，不改变产品。HostProbe Release 构建 0 警告/错误；完整 SettingsSmoke 本轮将重跑。样本只覆盖本机普通临时目录和隐藏 ExplorerBrowserHost，不能代表大于 160 项、其他 Shell provider、可见 DirectUI 绘制或网络目录表现。没有运行 GUI或发布版本。

### 2026-10-25 10,000 项自动刷新正确基线复测

在同一 Release HostProbe 构建上连续运行两轮隐藏生产宿主 `--live-shell-directory-stress-large 10000`。两轮 watcher 均完成 10,000/10,000 创建和删除事件，Shell 与磁盘最终均为 3 项；创建期间 Shell 计数只增长到 4,609–5,017 项时，产品各触发一次计数 fallback，随后视图达到 10,003 项。删除开始后 Shell 暂读到 10,003 项，产品再触发一次 fallback，最终回到 3 项。两轮完整操作均通过，`second.txt`/`third.txt` 选区及未选中的 `initial.txt` 焦点最终恢复，隔离宿主退出码均为 0。创建到视图收敛分别约 1,910/1,815 ms，创建/删除分别耗时 3.82/1.77 秒和 3.82/1.88 秒；每轮共 2 次 fallback。第二轮创建刷新首次回调时焦点条目暂不可用，延迟恢复路径约 236 ms 后重新聚焦 `initial.txt`，说明该路径对短暂不完整的 Shell 行集仍有作用。

这是两轮本机普通本地临时目录、隐藏 ExplorerBrowserHost 的重复证据；测试通过 COM/文件系统自动化驱动，不是用户键鼠或可见 UI 验收。它支持当前刷新 fallback 与选区/焦点恢复链路在 10k 创建/删除负载下收敛，但不能说明列表可见刷新延迟、交互响应、其他存储设备或所有 Shell provider 已达标。没有更改生产策略或发布版本；自动刷新 P0 继续开放。

### 2026-10-25 50,000 项自动刷新复测

使用同一 Release HostProbe 隐藏生产宿主运行 `--live-shell-directory-stress-large 50000`，两轮均退出码为 0。生产 watcher 创建和删除事件均为 50,000/50,000，最终磁盘与 Shell 视图计数回到 3，视图项数从 `50,003` 收敛到 `3`；创建/删除后的 `second.txt`、`third.txt` 选区和未选中 `initial.txt` 焦点均通过。

第一轮创建/删除耗时 20.726/11.099 秒；创建后视图再用约 1.608 秒收敛。创建阶段 1 次 fallback，删除阶段 3 次（`fallback-after-delete=4` 是从创建开始的累计数）；最大 Dispatcher 心跳间隔 713 ms。外部独立 observer 收到 49,999/50,000 个创建事件，但生产 watcher 完整计数仍为 50,000/50,000。

第二轮创建/删除耗时 34.037/20.523 秒；创建阶段 1 次 fallback，删除阶段 2 次（累计 `fallback-after-delete=3`）。时间线显示创建时 Shell 计数 `40,685/50,003`；删除后先在仅观察到 1 个删除事件时以 `50,003/50,002` 计数差触发 fallback，随后在 129 个删除事件累计时以 `50,002/49,874` 再触发一次。最后最大 Dispatcher 心跳间隔 916 ms。该轮把额外刷新触发定位到计数暂时滞后与 watcher 事件分批到达的窗口，但没有证明减少这些刷新会改善可见响应或保持所有 Shell 时序正确。

两轮仍只覆盖本机本地临时盘和隐藏 ExplorerBrowserHost；Dispatcher 心跳长间隔提示 STA 响应风险，但不是可见窗口帧率或用户输入延迟测量。不能据此证明可见交互、网络/慢速存储、长期运行或其他 Shell provider 达标。下一步核对 250/500 ms 计时与 `largeBurstActive` 的 750 ms 窗口，判断在删除事件延迟分批到达时，单项差异是否先触发了可避免的整视图刷新；先用状态/烟测或重复隔离测量建立证据，再决定是否调整窗口，不能单凭两轮输出放宽所有单项删除的刷新时延。未改生产策略、未操作 GUI、未发布版本；自动刷新 P0 保持开放。

### 2026-10-25 活跃大突发期间延后单项计数兜底

前一轮第二个 50,000 项样本在删除期间先只收到 1 个 watcher 事件，`expected=50,002`、Shell `actual=50,003`，随后约 4.88 秒才观察到另外 128 个删除事件。源码确认单项差异分支先于 `largeBurstActive` 判断，导致活跃 750 ms 窗口无法延后这次 Refresh。SettingsSmoke 先修改为要求“孤立单项差异仍立即兜底、活跃突发时延后、突发安静后立即兜底”；旧实现按预期在活跃突发断言失败。

现在 `ShellDirectoryChangeBatch.ShouldDeferRefresh` 仅在 `largeBurstActive` 时延后单项差异；普通单文件变化与强制刷新保持原策略。完整 Release SettingsSmoke 通过，App 与 HostProbe Release 构建均为 0 警告/错误。修复后单轮 50,000 项隐藏生产宿主运行退出码 0：创建/删除 watcher 各 50,000/50,000，最终视图/磁盘均为 3，选区和未选中焦点恢复；创建阶段 1 次 fallback，删除阶段 2 次。fallback 时间线显示删除阶段改为在 129 项事件累计、Shell `50002/49874` 计数差时及全批事件后、Shell `8575/3` 计数差时刷新；不再在仅 1 个 watcher 事件时提前刷新。创建/删除耗时 26.351/13.790 秒，视图计数各在约 1.140/1.444 秒额外收敛；最大 Dispatcher 心跳间隔仍为 890 ms。

该结果证明目标分支的时序变化及单轮最终收敛，不证明总体 fallback 次数或用户可感知延迟普遍改善；当前仅一个修复后 50k 样本。下一步需复跑同基线确认此行为稳定，并继续定位 890 ms STA 间隔。未运行可见 GUI、未改其它刷新策略、未发布版本；自动刷新 P0 仍开放。

### 2026-10-25 暂时空视图保留逻辑计数并复跑 50,000 项

修复后重复运行同一 `--live-shell-directory-stress-large 50000` 基线，首轮完整退出码为 0，但中间 `fallback-timeline` 出现 `expected=-128` 和 `expected=-49,999`。回溯状态机发现：两次 Refresh 后 Shell 暂时返回 0 项时，`CompleteRefresh` 将非空目录基线重置为 0；随后 watcher 删除增量继续相减，使逻辑预期为负数。原探针只验最终磁盘/视图/选区/焦点，所以这条不变量被漏检。

SettingsSmoke 新增“非空目录在刷新期间暂时呈空”后仍保留逻辑计数、之后 10,000 项删除增量不能产生负数、并在有限重查窗口内等待的断言。旧实现按预期在第一条基线断言失败；修复后通过：重复空视图保留正向逻辑基线，并在 180 次重查窗口内让 Shell 恢复枚举，避免 watcher 后续删除从 0 开始累计。

修复后重新构建并运行 50,000 项隐藏生产宿主，退出码 0：生产 watcher 创建/删除为 50,000/50,000，最终 Shell/磁盘为 3/3，选区与焦点恢复。该样本创建阶段无 fallback，删除阶段 1 次 fallback；时间线为 129 个删除事件后 `expected=49,874`、Shell `actual=50,002`，预期始终非负，不再出现仅 1 个事件后的早期刷新。创建/删除耗时 26.681/24.315 秒，创建视图约 2.149 秒收敛，外部 observer 创建通知为 49,999/50,000（生产 watcher 完整）。

Dispatcher 最大心跳间隔仍为 1,139 ms；采样到 DirectUIHWND 私有消息 `0x4C8` 单次 Shell 处理约 753.6 ms，另有 `SHELLDLL_DefView` 私有消息约 241 ms。Host/探针没有把这些私有消息映射到公开语义，因此不能据此安全地拦截或改写；目前证据指向 Shell 自身在大目录压力下占用 STA 的时间，而不是计数查询或宿主回调（本轮计数查询最大约 0.128 ms、单次 `IShellView.Refresh` 约 90.8 ms）。这只是一个本机隐藏宿主样本，不代表可见交互延迟。

本轮 Release SettingsSmoke 与 HostProbe 集成压力通过；没有可见 GUI、没有更改 Shell 架构、没有发布版本。下一步继续追踪 STA 长间隔的操作重叠，并重复修复后的大批删除样本，确认 1,139 ms 停顿与 DirectUI 消息的关联是否稳定；自动刷新 P0 继续开放。

### 2026-10-25 STA 延迟与延迟选区恢复重叠

为定位上一轮约 1,139 ms Dispatcher 心跳空档，打开 HostProbe 已有的消息泵与 Dispatcher 队列延迟跟踪，再运行相同 50,000 项隐藏生产宿主。此轮仍通过：生产 watcher 50,000/50,000，最终 Shell/磁盘 3/3，选区和焦点恢复；创建 1 次 fallback，删除 2 次。删除 fallback 分别对应孤立的 1 项删除和之后累计 129 项的删除批次，第二次差值为 `49,874/50,002`，没有出现负预期。最大 Dispatcher 心跳间隔为 588 ms；队列跟踪最大延迟约 526 ms。

时间线将 647 ms 的 ContextIdle 空档定位在 37.541–38.188 秒：其中 `ScheduleDeferredRefreshSelectionRestore` 一次 Background 回调占 256 ms；同一时段有 `DirectUIHWND` 私有消息 `0x4CC` 约 246 ms，之后 `SHELLDLL_DefView` 私有消息 `0x4B0` 约 246 ms。另一次此前无队列跟踪的样本记录 `DirectUIHWND` 私有消息 `0x4C8` 约 754 ms，并与约 1,139 ms Dispatcher 空档重叠。私有消息没有公开语义映射，不能据此拦截或重写。

`ScheduleDeferredRefreshSelectionRestore` 在 UI STA 上调用 `ShellViewSelectionSnapshot.Restore`；该恢复路径会同步枚举当前选择、提交选区，再查询焦点 PIDL 并调用 Shell 选择接口。当前证据证明这些恢复操作与 STA 长空档重叠，但没有单步耗时，尚不能区分 `CurrentSelectionMatches`、`SelectAndPositionItems`、焦点枚举或 Shell 内部重绘各自的成本。刷新阶段统计最大约 127 ms，计数读取最大约 0.071 ms；因此本轮没有更改刷新或选择恢复策略。下一步为这些 COM 步骤加隔离耗时诊断并在同负载下复测，确认瓶颈后再用回归测试验证优化；不以异步猜测或忽略焦点恢复来换取表面响应。

后续已在 `CurrentSelectionMatches` 增加诊断跟踪点：分别计量 `IFolderView.Items` 获取枚举器、每次 `IEnumIDList.Next` 和每次 `IShellFolder.CompareIDs` PIDL 身份比较。调用只在现有原生跟踪启用时读取时间戳和输出逐项记录，未更改匹配策略；App Release 构建和全量 SettingsSmoke 通过。因当前不操作桌面，本轮未启动 ExplorerBrowser HostProbe，尚无新 COM 计时样本；需在桌面授权后复跑同负载，再决定后续优化。

本轮是单机隐藏宿主与诊断跟踪证据，不能代表可见输入延迟，也不能证明其它 Shell provider。没有操作 GUI 或发布版本；自动刷新与大目录 STA 响应 P0 仍开放。

### 2026-10-25 大目录焦点恢复跳过全目录 PIDL 扫描

在 `ShellViewSelectionSnapshot.Restore` 对选择恢复、焦点查找、焦点设置和核验分别计时后，50,000 项隐藏生产宿主样本显示：恢复未选中焦点 `initial.txt` 前，`ContainsItem` 扫描当前 Shell 视图耗时约 71–78 ms；接下来的 `IShellView.SelectItem` 设置焦点约 57–85 ms，`IsFocused` 核验约 5 ms。为了避免在 STA 上先扫描完整视图，焦点路径现在直接请求 `SelectItem`，随后仍以 Shell 的焦点状态核验成功与否；若异步枚举尚未呈现目标 PIDL，继续沿用延迟重试与原有 fallback 约束。

按 TDD 先给 HostProbe 增加“直设焦点”跟踪断言；旧实现的 50,000 项运行缺少该标记而退出码 1。实现后最终完整隐藏压力轮退出码 0：生产 watcher 创建/删除 50,000/50,000，Shell/磁盘最终 3/3，选区与未选焦点恢复，`shell-focus-restore-direct-select=True`。此轮初次刷新仍因 Shell 列表未呈现焦点项而核验失败，延迟恢复后成功；在 Shell 忙时 `SelectItem` 单次约 115 ms，焦点核验约 5.5 ms。恢复多选的 seed 与批量 API 分别约 67 ms 和 54 ms，另一条原生捕获约 28 ms 和 58 ms。最大 Dispatcher 心跳间隔为 567 ms。压力探针的创建阶段 one-shot 选区样本偶尔早于延迟恢复，故最终创建成功判定改为使用等待“刷新与延迟恢复均结束、选区和焦点回到基线”的状态；这保留了等待期间超时失败路径。

当前证据表明，跳过全目录查找消除了额外的全视图扫描，但 Shell 选择调用本身仍可能阻塞 STA。因此这不是大目录响应问题的完整修复，也不证明其他目录提供程序或可见窗口行为。SettingsSmoke、可见 GUI、跨启动行为与发布版仍需单独验证；本轮没有使用 computer-use、操作可见 GUI 或发布版本。下一步继续把性能调查限制在批量选择与 Shell 选择调用本身，不削弱延迟焦点恢复正确性。

### 2026-10-25 选区 PIDL 比较二进制快速路径

性能诊断指出选区快照回读会对每个 PIDL 调用 `IShellFolder.CompareIDs(SHCIDS_CANONICALONLY)`。现在 `IsSameItem` 先调用 `ILIsEqual`：精确二进制相等时直接成功；不相等时仍调用 `CompareIDs`，保留 Shell 对不同编码但同一项目 PIDL 的 canonical 身份判断。微软文档明确指出这两种比较的语义不同，故未删除 canonical fallback。`CurrentSelectionMatches` 的 trace 增加 `method=pidl-exact` 或 `method=canonical-shell`，后续 HostProbe 可直接核对高频未变化选区是否走快路。Release SettingsSmoke 与 App 构建通过；尚未运行隐藏 ExplorerBrowser，也没有性能前后样本，不能声称 STA 延迟已降低。

随后将生产 `ShellViewSelectionSnapshot` 和 `ShellSelectionBatchRestore` 链接进 NativeProbe，在隐藏默认 `IShellView` 上捕获并恢复同一条选中项。Release 探针退出码 0，单项 trace 为 `method=pidl-exact`、`selection-current matched=True`、`selection-restored=True`，且没有 `selection-batch-api` 记录；身份比较阶段约 0.5 ms，说明该样本跳过了逐项 `CompareIDs` 和批量选区重放。隐藏视图没有确认焦点（`focus-restored=False`），故不将此结果计为焦点恢复通过或生产 ExplorerBrowser 性能改善。父窗口隐藏、前台句柄不变，未创建 ExplorerBrowser；50k HostProbe 对照仍待后续授权。

### 2026-10-25 多选恢复改为批量 API 首选的待验策略

上节将选择恢复种子 `IShellView::SelectItem` 观察到约 67 ms，而 `IFolderView::SelectAndPositionItems` 约 54 ms。候选改动是先批量提交所保存的所有选择 PIDL，再枚举读回集合；只有批量 API 失败或集合与快照不完全一致时，才使用原有逐项 `SelectItem` 播种路径，然后再做批量恢复。这样在成功且读回匹配时可省去一次种子 Shell 调用；这是基于前一轮耗时的预期，尚无改后 Shell 宿主数据。

增加纯逻辑 `ShellSelectionBatchRestore` 策略并接入 `ShellViewSelectionSnapshot`。SettingsSmoke 覆盖批量成功/匹配、批量成功/不匹配、批量失败三种调用顺序；SettingsSmoke 通过，App 与 HostProbe Release 构建均为 0 警告/错误。微软文档仅确认批量接口作用于“当前视图中可见”的项目并返回 HRESULT，没有承诺不可见/过期 PIDL 的行为，所以失败与读回不匹配都保留原身份验证/种子兜底。[IFolderView::SelectAndPositionItems 官方文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems)

策略回调另记录 `selection-batch-path=direct` 或 `selection-batch-path=seeded-fallback`，供后续隐藏宿主压力样本直接统计成功直通与兜底次数；诊断回调为空时不构造/写入该消息。该标记只说明分支选择，不是性能或 Shell 状态证据。

由于项目规则要求无 computer-use 阶段不启动可能改变前台窗口的 ExplorerBrowser 宿主，本轮未运行 HostProbe。纯逻辑回归和 Release 构建不能证明真实 Shell 会在异常 PIDL 下返回何种 HRESULT、直接批量调用是否在目标机器更快、或文件删除时仍正确恢复剩余选择。下一步须在用户明确桌面授权后，以缺失 PIDL、完整多选和单选三类隔离用例验证；当前保持兜底，不把潜在耗时收益计入已验证结果。

### 2026-10-25 隔离 IShellView 多选恢复验证与修正

NativeProbe 新增 `--ishellview-selection-batch-spike`，创建隐藏默认 `IShellView` 和四项临时文件，不创建 ExplorerBrowser、不激活窗口、不发送用户输入。先验证空选区可直接恢复两项；随后加入两个不属于快照的额外选区，验证读回检测不匹配后，replacement fallback 能清除额外选择并精确恢复两项；最后删除一个快照项目并刷新，确认 fallback 只恢复一个幸存项目。探针两次连续退出码 0，父窗口始终隐藏且前台句柄未变。

新增探针首次运行发现生产逻辑把 `SVSI_DESELECTOTHERS` 与多 PIDL 一起传给 `IFolderView::SelectAndPositionItems`：HRESULT 成功，但视图只保留一个项目，因此完整读回不匹配。批量接口现使用 `SVSI_SELECT | SVSI_NOTAKEFOCUS`。替代方案测试发现，`IFolderView2::GetSelectedItem` 可返回当前选中项索引，随后 `IFolderView::SelectItem(index, SVSI_DESELECT | SVSI_NOTAKEFOCUS)` 可移除该项；重复从索引 0 查询直到 `S_FALSE`，可清除多个当前选择，无需为快照 PIDL 全视图扫描。额外选区回归验证了清除两个旧项后精确恢复两项。

该验证证明本机默认 `IShellView` 的多选恢复和 extra-selection fallback，不证明 ExplorerBrowser 生产宿主、焦点恢复、Shell 异步枚举时序或可见 GUI。删除项目后的 `SelectionRestored=True` 表示存在一个幸存选区项，不代表原有两项全部仍存在；不可用 PIDL 的完整性仍由逐项读回和当前恢复状态处理。50k HostProbe 对照和真实窗口验收保持待验证。

### 2026-10-25 多选 replacement fallback 单次清空旧选区

此前 `ClearCurrentSelection` 每次通过 `IFolderView2::GetSelectedItem` 取一个索引，再调用 `IFolderView::SelectItem(..., SVSI_DESELECT)`，循环清空所有残留选区；这让旧选区数量直接决定 STA 上的同步 Shell 调用次数。为避免多选较大时执行 N 次清除调用，先在隐藏默认 `IShellView` 中验证单项清空语义：`IShellView::SelectItem(childPIDL, SVSI_SELECT | SVSI_DESELECTOTHERS)` 虽返回成功，却未清除额外选择；改用 `IFolderView::SelectItem(selectedIndex, SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_NOTAKEFOCUS)` 后，在未分组视图中多个旧选区缩为一项，再补回快照第二项能恢复两项。未分组八种查看模式曾通过。

### 2026-10-25 分组视图多选恢复 fallback 修正

继续加入按 `System.ItemTypeText` 分组的隐藏默认 `IShellView` 用例后，上述单项优化未通过：分组下 `DESELECTOTHERS` 留下了不属于快照的 `d-extra.bin`。逐项 `GetSelectedItem` 方案也漏掉残留项：四项选择中枚举只返回三个索引，清除后仍有一个分组项目处于选中状态。失败读回显示 `a-selected.txt|b-removed.txt|d-extra.bin`，所以不能将索引扫描完成或 seed API 成功当作选区已清空。

NativeProbe 进一步验证，对当前选区 PIDL 数组调用 `IFolderView::SelectAndPositionItems` 并只传 `SVSI_NOTAKEFOCUS`（`SVSI_DESELECT` 的值为 0）返回成功，可清除这四项，包括索引清除漏掉的分组项目。生产 `ClearCurrentSelection` 已改为枚举当前选区 PIDL、一次批量取消选择，并通过 `IFolderView::ItemCount(SVGIO_SELECTION)` 确认剩余为零；这里刻意不用 `SVSI_DESELECTOTHERS`，该标志此前已证明在批量选择中逐项生效会破坏多选语义。

最终 NativeProbe 在未分组/按类型分组 × 八种查看模式共 16 组测试中，分别于当前状态捕获两项选区及焦点 PIDL，选中全部四项后调用 production fallback，均精确恢复两项和焦点；直接批量恢复也通过焦点断言；删除一个快照 PIDL 后只恢复幸存项。Release SettingsSmoke 和 App Release build 通过，0 警告/错误，父窗口保持隐藏且前台句柄不变。此证据来自默认 `IShellView`，不等同于 ExplorerBrowser 或 50k 性能验证。后续在允许运行生产 HostProbe 时，比较 fallback 清除耗时、选区/焦点最终状态与大目录 Dispatcher 长尾，并补可见焦点验证。

### 2026-10-25 批量优先策略复跑

最新 `ShellSelectionBatchRestore` 策略接入后，重建 NativeProbe Release 并复跑 `--ishellview-selection-batch-spike`。探针退出码 0：无额外选区时走 `direct` 并恢复两项及焦点；额外选择出现时走 replacement fallback，仍恢复两项和焦点；删除快照中的一项后只恢复一个幸存项目；全部快照项目失效时报告未恢复且不留选区。上述 fallback 覆盖未分组/分组 × 八种查看方式。父窗口不可见、前台句柄未改变。

这确认批量优先加读回校验策略可在默认 `IShellView` 的夹具中工作；四项目样本不代表大目录性能，也不证明 ExplorerBrowser 的产品回调、并发变化时序或物理焦点行为。新策略仍需生产 HostProbe 压力验证及之后的桌面验收。

### 2026-10-25 Shell 计数不可读时避免重复全视图刷新

源码追踪到一个无界失败循环：目录变化计时器读取 Shell 项目数为 `null` 时，`ShouldDeferRefresh` 原先会立即放行，随后 `Complete(null)` 请求整视图刷新；异步完成再次读到 `null` 时，`CompleteRefresh` 无法累计可比较的计数，仍会重启 250 ms 计时器，下一轮再次触发同样刷新。新增隔离的 `--shell-directory-change-batch-smoke` 先按旧行为失败，证明不可读计数原先不会等待。

现在非强制刷新在计数暂不可读时延后本轮协调，保留 watcher 增量并以既有 500 ms 计时器重读；计数恢复后按现有匹配/偏差策略继续。Watcher Error、重命名等强制刷新仍可越过此门控。该回归是纯状态测试，没有启动 ExplorerBrowser 或 GUI；异步计数暂不可读的真实来源、用户可见刷新及时性、慢速/网络位置与 STA 长尾仍需分别验收，自动刷新 P0 保持开放。

### 2026-10-25 大目录刷新保留用户选区快照

连续两次 10,000 项隐藏生产宿主压力先复现了选区问题：探针经产品输入路径记录了 `second.txt` / `third.txt` 两项和独立焦点 `initial.txt`，Shell 在增量枚举期间却临时把焦点项也加入选区；刷新路径随后从这个临时 Shell 状态取样，导致恢复目标偏离用户确认的选区。第一轮修正刷新快照来源后，创建刷新恢复到两项，但后续外部批量删除仍让 Shell 清空选区；原因是刷新工作完成后，已确认快照被释放，无法应对后续原生选区变化。

现在同一视图且没有新用户输入时，刷新优先克隆已确认的用户快照，临时 Shell 变化不会改写它；刷新专用副本用于延迟恢复，原快照继续可供后续 Shell 选区变化恢复。延迟恢复期间忽略 Shell 的中间选择回调。SettingsSmoke 先在旧逻辑上按预期失败，随后新增门控回归通过；App、HostProbe Release 构建 0 警告/错误。相同 10,000 项压力连续两轮通过：产品 watcher 创建/删除事件均完整 10,000/10,000，最终 Shell 与磁盘均回到 3 项，两项原选区和独立焦点在创建、删除后均恢复。独立观察器各少收到一次创建通知，不影响产品 watcher 结果。

两轮 Dispatcher 最大间隔为 207/191 ms，独立队列最大排队延迟为 4.9/8.0 ms；大目录期间仍观测到 `SHELLDLL_DefView` 私有消息单次约 26/33 ms，以及 90% 左右 CPU 单采样。私有消息语义未确认，不能据此拦截或改写；本结果只覆盖本机隐藏宿主，不代表可见输入延迟、其他 Shell provider 或网络目录，也不关闭自动刷新 P0。没有启动可见 GUI、没有修改预览发布包。

### 2026-10-25 异步选择快照增长丢失与 50,000 项复测

新一轮 `--live-shell-directory-stress-large 50000` 未通过：创建阶段两项选中内容最终只保留 `second.txt`，未选中的焦点 `initial.txt` 恢复成功。跟踪显示，刷新期间 Shell 先报告一项选中，随后才报告完整两项；`OnShellSelectionChanged` 的旧门控只要已有非空快照就无条件保留它，因此把中间的一项快照锁成了最终状态。失败轮中两次 Refresh 都遇到计数暂时为空，最终选择仍不完整。

`ShellSelectionRestoreGate.ShouldKeepExistingSnapshot` 现在同时比较旧、新快照计数：同视图、无新用户输入时保留较大快照；Shell 后续报告更多选中项时，允许快照增长。新增 SettingsSmoke 先在旧门控下按预期失败，再在修正后通过；完整 Release SettingsSmoke 通过，App/HostProbe Release 构建 0 警告、0 错误。

修复后再次运行相同 50,000 项隐藏生产宿主，退出码 0：生产 watcher 创建/删除为 50,000/50,000，最终 Shell 视图回到 3 项；选择时间线在目录删除收敛前持续显示两项 `second.txt`/`third.txt`，独立焦点为 `initial.txt`。创建阶段 fallback=0，删除阶段 fallback=2，最终两项选区和焦点断言通过。Dispatcher 最大心跳间隔约 658 ms，最长连续忙碌约 823 ms；因此选择正确性回归已通过，本轮仍观测到大目录 STA 长尾，P0 不关闭。

本轮为隐藏 ExplorerBrowser HostProbe，仅操作临时文件，没有桌面输入或发布。单轮成功不能证明其它 Shell provider、慢速/网络目录或可见窗口行为；后续仍需重复大目录样本与桌面验收。

### 2026-10-25 选区恢复逐项 Shell 查询造成 STA 长尾

对上述 50,000 项压力继续使用完整日志测量选区恢复链。第一轮最大 Dispatcher ContextIdle 空档约 665 ms，第二轮约 707 ms；两轮最大 Background 延迟恢复回调分别约 428/437 ms。第二轮增加逐项 `IShellView.SelectItem` 计时后，单项调用约 4–55 ms，焦点设置约 24–41 ms，但相邻恢复步骤之间仍有未分解间隔。

再为 `ContainsItem` 增加计时后，第三轮显示恢复两项选区时，对当前大视图逐项枚举 PIDL 的耗时约 144 ms 和 140 ms；随后的两次 `SelectItem` 分别约 12 ms 和 25 ms，焦点设置约 24 ms。该轮生产 watcher 创建/删除均为 50,000/50,000，最终两项选择和焦点正确，退出码 0；ContextIdle 最大间隔约 722 ms，HostProbe 心跳最大间隔约 715 ms。由此确认逐项全视图成员查询是恢复路径的可优化成本之一，但并不能解释全部空档，Shell 自身 STA 消息仍有额外耗时。

本轮只增加诊断计时，没有改变恢复策略。已有 NativeProbe 覆盖快照 PIDL 被删除后只恢复幸存项，但还需在 ExplorerBrowser 隐藏 HostProbe 下验证直接调用 `SelectItem` 对缺失 PIDL 的返回和选区副作用；确认后再考虑移除 `ContainsItem` 预扫描。Release HostProbe 构建 0 警告、0 错误；三轮均为隐藏宿主，不代表可见 UI、其他 Shell provider 或网络目录表现。

### 2026-10-25 选区恢复改用直接 Shell 选择并核验

复用上述 NativeProbe 的陈旧 PIDL 情形增加回归断言，要求 fallback 对每个保存的 PIDL 直接调用 Shell 并检查读回；旧的 `ContainsItem` 实现按预期失败。探针随后发现 Shell 对已经删除的 PIDL 仍可能返回 `S_OK`，即使最终没有选中任何项目，因此 HRESULT 不能作为“此快照项仍存在或已经选中”的判据。

生产恢复现在直接对保存的 PIDL 调用 `IShellView::SelectItem`，再只枚举当前选中集合，核对目标 PIDL 是否真的被选中；这避免为每个候选项扫描完整 50k 视图，也避免把陈旧 PIDL 的 `S_OK` 误计为成功。选中集合核验失败时仍尝试下一个快照项；焦点 fallback 同样直接尝试并通过 Shell 当前焦点核验。NativeProbe 验证普通多选、额外选区替换、单个陈旧 PIDL只恢复幸存项、全部陈旧 PIDL保持空选区，并覆盖分组/未分组下八种查看模式，退出码 0；完整 Release SettingsSmoke 通过，HostProbe Release 构建 0 警告、0 错误。

修复后 50,000 项隐藏生产宿主退出码 0：产品 watcher 创建/删除均为 50,000/50,000，最终视图/磁盘为 3/3，两项原选区与 `initial.txt` 焦点在创建和删除后保持。延迟恢复回调的最大耗时从本轮之前观测到的约 437 ms 降至约 173 ms。整轮仍测得 ContextIdle 最大间隔约 1,076 ms、HostProbe 心跳最大间隔约 953 ms，因此这项修复减少了选区恢复成本，但没有消除所有 STA 长尾；剩余空档需继续关联到 Shell 原生消息与其它回调。外部独立观察器创建通知为 49,999/50,000，产品 watcher 计数完整。没有使用桌面输入或发布版本。

### 2026-10-25 Shell DefView 私有消息占用 STA 的长尾

为拆分修复后的剩余长尾，再运行一次带消息泵、Dispatcher 队列和原生视图消息时间线的 50,000 项隐藏生产宿主。运行通过，生产 watcher 创建/删除均为 50,000/50,000，Shell 目录最终收敛，原两项选择和独立焦点保持。此轮创建/删除耗时约 34.9/38.9 秒；ContextIdle 最大 Tick 间隔约 2,052 ms，HostProbe 心跳最大间隔约 1,657 ms。

消息泵最大消息取出空档约 978 ms（38,580.8–39,558.5 ms），同一时段 `SHELLDLL_DefView` 的 `0x4B0` WndProc 持续约 972 ms（38,586.9–39,559.2 ms），并覆盖了数次约 12–106 ms 的 `0x4E` 视图消息处理。相对地，本轮应用目录 Tick 最大约 15.5 ms、`IShellView.Refresh()` 最大约 15.1 ms、队列 Drain 最大约 0.07 ms、Shell 计数读取最大约 0.1 ms；没有发现应用计数或刷新回调能解释这次近 1 秒连续占用。

Microsoft 将 `WM_USER` 到 `0x7FFF` 定义为窗口类私有消息范围；`0x4B0` 落在该范围，公开文档没有给 `SHELLDLL_DefView` 的此消息定义语义。[WM_USER 消息范围](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-user) 因而本结果只能把停顿定位到本机 Shell DefView 的同步消息处理，不能推断消息具体含义，也不能据此安全拦截或改写。最大 ContextIdle 间隔长于单条 Shell 消息，且同窗还有约 273 ms 消息空档，剩余时间尚未分解；自动刷新/大目录响应 P0 继续开放。

同轮首次尝试停止产品目录监视器的 native-only 模式时，探针仍强制要求产品恢复选区基线，因 Shell 在增量枚举期间把焦点项加入选区而保护性退出（码 1）。随后修正 HostProbe，让 native-only 模式只跳过产品选区/焦点断言，继续验证 Shell 目录数量和测试目录清理；产品模式的选区恢复断言保持不变。调整后的 50,000 项 native-only 隐藏压力退出码 0，生产目录计时器、队列、Refresh 及 watcher 阶段均为 0 次；Shell/磁盘最终均为 3 项。此轮 ContextIdle 最大间隔约 275 ms，消息泵最大消息空档约 196 ms，没有超过 200 ms 的 `SHELLDLL_DefView` `0x4B0` 调用。与产品监视模式中观测到的约 972 ms `0x4B0` 长调用相比，这支持进一步检查产品监视/显式刷新与 Shell 长处理之间的关系，但单轮、非交错样本不能证明产品 Refresh 是其原因。下一步按相同 50,000 项工作负载重复交错 native-only 与产品监视模式，直接对齐 Refresh 调用、Shell `0x4B0` 区间和消息泵空档；在此之前不更改刷新策略或窗口线程模型。

### 2026-10-25 产品监视与 native-only 50,000 项交错对照

在同一台机器、同一隐藏生产 ExplorerBrowser 夹具和相同 50,000 项创建/删除脚本下，再依次运行产品监视模式与 native-only 模式，开启相同消息泵、原生消息和 Dispatcher 队列跟踪。产品模式再次通过：watcher 创建/删除均为 50,000/50,000，视图/磁盘最终 3/3，原两项选区与焦点恢复。共调用三次 `IShellView.Refresh()`，耗时 `22.2/394.1/391.6 ms`；之后约 `524.6/548.7/568.1 ms`，分别出现 `SHELLDLL_DefView` `0x4B0` 慢消息 `432.0/767.4/266.5 ms`。该轮最大 ContextIdle 间隔约 1,702 ms、最大消息泵空档约 768 ms；产品目录 Tick 和原生 Refresh 最大约 394 ms。

紧随其后的 native-only 轮也退出码 0：Shell 视图创建/删除后为 `50,003→3` 项，磁盘最终 3 项；产品目录 watcher、目录 Tick、显式 Refresh 阶段均为 0 次。消息泵最大空档约 214 ms，ContextIdle 最大间隔约 579 ms；原生消息慢时间线没有 `0x4B0` 慢消息（该时间线记录大于约 10 ms 的处理）。native-only 模式不承诺产品选区恢复，因此本轮 `selection-restored=False`，但目录收敛及清理断言通过。

本组之前的 native-only 轮没有执行产品模式专有的 128 项批量删除段，因此只作为初步对照。为消除这项差异，HostProbe 的 50k native-only 模式现在也执行同样的 128 项批量删除、路径身份查询和最终批量清理；产品模式断言保持不变。

匹配该段后的新 native-only 50k 轮退出码 0：128 个删除事件完整观察到，滞后项目身份查询仍返回文件、批量删除后视图计数收敛，最终 Shell/磁盘均为 3 项。产品 watcher/计时/显式 Refresh 阶段为 0 次；ContextIdle 最大间隔约 435 ms、消息泵最大空档约 230 ms，原生消息慢时间线未记录 `0x4B0` 大于约 10 ms 的处理。该轮不要求产品选区恢复，故 `selection-restored=False` 是预期边界。

与同样含 128 项批删的产品模式配对后，产品轮三次 fallback 时的实际/预期项目数分别为 `47,339/50,003`、`50,003/50,002`、`50,002/49,874`，随后各调用一次 `IShellView.Refresh()`；每次 Refresh 返回后约 `0.525/0.549/0.568` 秒，分别记录到约 `432/767/267 ms` 的 `0x4B0` Shell 消息。产品轮最大 ContextIdle 间隔约 1,702 ms、消息泵最大空档约 768 ms。匹配负载下仍得到相同方向的差异，说明高延迟与产品的计数 fallback/显式 Refresh 路径高度相关，但单次顺序配对仍不是严格因果证明。

创建 fallback 的样本尤其显示：Refresh 前 Shell 计数为 `47,339/50,003`，native-only 对照在相同 1.2 秒创建后等待窗口内已达到 `50,003`；产品模式在 fallback 后重建并收敛，随后仍发生长 `0x4B0` 处理。下一步应增加仅用于探针的自然收敛观察窗口，测量产品计数落后时不立刻 Refresh 是否会自行追上，以及删改期间多长等待仍能兜住真正漏通知；先取得这组分布再评估是否调整产品 fallback 期限。不得仅按固定延迟压掉必要刷新。自动刷新 P0 继续开放。

### 2026-10-25 单文件创建的自然收敛窗口探针

按上述下一步，在 HostProbe 中增加 `EXPLORER_DIRS_HOSTPROBE_NATURAL_OBSERVATION_MS`（100–5000 ms），只用于隐藏测试：开始真实 `IFileOperation` 单文件复制前暂停该窗格产品 fallback 计时器，并占用其应用创建通知定时器槽以抑制产品发出的 `SHCNE_CREATE`；不发送外部 Shell 通知。观察窗口内以 25 ms 间隔读取 Shell 视图计数；窗口结束立即恢复 250 ms 产品监视计时器，并验证最终显示及测试文件清理。观察期间 fallback 和应用创建通知计数均须为 0，否则该轮不作为自然收敛证据。

Release HostProbe 构建通过（0 警告、0 错误），六次隐藏生产 ExplorerBrowser 样本全部退出码 0，初始计数均为 3，文件系统 watcher 都观察到新增文件，最终创建项可见且删除清理收敛：

| 自然观察窗口 | 观察窗口内首次可见 | 恢复监视后结果 |
| --- | ---: | --- |
| 250 ms（1 次） | 未出现 | 约 811 ms 总耗时后自然出现，未调用 fallback |
| 600 ms（1 次） | 未出现 | 约 810 ms 总耗时后自然出现，未调用 fallback |
| 1200 ms（4 次） | 3 次在 745–748 ms 出现；1 次窗口内未出现 | 前 3 次无需 fallback；剩余 1 次恢复监视后触发一次 fallback，随后项目可见并清理成功 |

这批创建样本说明单文件复制在隐藏 ExplorerBrowser 中有约 0.75–0.81 秒的延迟自然更新路径，但也存在 1.2 秒仍未收敛、需要恢复 fallback 的轮次。随后将同一窗口机制用于临时文件删除：一个 600 ms 样本和两个 1.2 秒样本在观察期内未自然移除，恢复监视后各触发一次 fallback；另两个 1.2 秒样本分别在约 453 ms、400 ms 自然移除且没有 fallback。文件删除没有发送产品文件夹 `RMDIR` 通知，`NotFolder` 结果符合目标为普通文件的约束。所有样本最后都达到正确计数并清理完成，观察期 fallback 均为 0。

创建与删除样本均为小样本、隐藏 ExplorerBrowser、本机临时目录，不支持调整生产 fallback 期限；不代表大目录、重命名、其他 Shell provider 或可见 UI。探针没有修改生产监视策略。以下继续在同一自然观察机制中补测 burst 与重命名。

### 2026-10-25 多项创建 burst 与重命名观察

HostProbe 增加 `EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_COUNT`（2–5）和 `EXPLORER_DIRS_HOSTPROBE_NATURAL_OPERATION=rename` 两种诊断模式，均要求先设置自然观察窗口。Burst 在本机临时目录快速创建 2–5 个文件；观察期暂停窗格 fallback 并抑制应用 `SHCNE_CREATE`，以全部目标名称和准确项目数共同判断 Shell 是否自然收敛。重命名模式先建立并等待创建基线、应用创建通知及其异步刷新完全结束，再通过 `File.Move` 改名；观察期暂缓产品强制刷新，记录产品批次是否收到 `forceRefresh`，之后恢复计时并验证新名称、刷新完成和夹具清理。上述操作都只运行于隐藏生产 ExplorerBrowser。

Release HostProbe 构建通过（0 警告、0 错误）。Burst 样本：2 项/600 ms 窗口未在期内全部显示，恢复监视后约 1028 ms 总耗时完整显示且未触发 fallback；5 项/600 ms 窗口未在期内显示，恢复后触发一次 fallback、约 1503 ms 显示；2 项/1200 ms 窗口在约 749 ms 全部显示；5 项/1200 ms 两次分别约 1000、1002 ms 全部显示。四次观察窗口内的 fallback 和应用创建通知均为 0，watcher Created 数量准确为 2 或 5；后续删除清理均通过。

重命名样本：一个 600 ms 与三个 1200 ms 的完整通过轮次，观察期均未自然替换旧名称；产品 watcher 都设置强制刷新，观察期 fallback 为 0，恢复监视后各执行一次 fallback，新名称可见且刷新及清理完成。一次早期轮次在创建基线刷新仍进行时就开始改名，出现自然更新与强制标记消费竞争；修正探针等待创建刷新完成后再取基线，不把该轮计入样本。另有一次改名成功但清理与刷新回调重叠的轮次，现改为先等待刷新完成再删夹具；修正后的重复轮次通过。

现有证据表明，这台机器上的小批量新增有约 0.75–1.0 秒的自然收敛路径，但 5 项/600 ms 样本仍需 fallback；本次稳定基线下的重命名则由产品强制刷新完成。样本量及操作类型仍有限，不能推导大目录部分枚举阈值或调大生产 timer。下一步改用多项 `IFileOperation` 和更大的分批创建/删除样本，继续记录各批 Shell 项目身份收敛、watcher 事件和 fallback 时序；自动刷新 P0 仍开放。

### 2026-10-25 多项 IFileOperation 与分阶段 burst

为覆盖真实 Shell 操作队列，`ShellFixtureOperations` 新增单次 `IFileOperation` 中排队多个 Copy/Delete 的测试助手；HostProbe burst 可选 `EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_KIND=ifileoperation`，计数上限 32。创建和清理均通过 Shell 批操作，观察期仍暂停产品 fallback 并抑制本次应用 `SHCNE_CREATE`；watcher 事件数、所有文件名/视图计数及最终清理都需匹配。支持 `EXPLORER_DIRS_HOSTPROBE_NATURAL_BURST_STAGE_SIZE` 将同一 burst 分为多次 `IFileOperation`，每个中间阶段间隔 250 ms 并采样 Shell 当前可见数量与项目身份。

Release HostProbe 构建 0 警告/错误。单批 5/16/32 项 IFileOperation，在 1200 ms 观察窗口内均未显示全部项目，之后分别约 1355/1378/1440 ms 完整可见，观察窗口内 fallback 和应用 Shell 创建通知均为 0；所有 watcher 创建数准确，批量删除清理成功。600 ms 窗口中，16 项样本在恢复计时后调用一次 fallback、约 1531 ms 可见；32 项样本约 1426 ms 自然可见、没有 fallback。

另以 32 项、每阶段 8 项跑两轮。前三个阶段的样本点（第 8/16/24 项创建后约 350/655/982 ms）Shell 视图仍为基线 3 项、可见 burst 条目数为 0。最后一批后，1200 ms 窗口样本在 watcher 事件后约 1031 ms 才完整出现；600 ms 窗口结束时仍未显示，随后约 2346 ms 总耗时收敛。两轮均没有调用 fallback，32 个创建事件全部收到，Shell 批量删除后清理通过。

这组结果只覆盖本机隐藏 ExplorerBrowser、同一文件夹最多 32 个本地文件和当前 Shell provider；样本仍不代表 50k 部分枚举或跨卷/网络位置。分阶段计数保持在 3 项直到 burst 停止后再更新，提示该场景的自然视图更新发生在批次静默后的重新枚举阶段，但不能由此推断 DefView 通知内部机制。下一步把大规模 HostProbe 的生产目录 watcher 与 native-only 对照按交错轮次复测，并关联分段项目身份、fallback 和消息泵延迟；产品 fallback 期限保持不变。

### 2026-10-25 Shell 通知探针注册与投递阶段分离复测

为区分 `SHChangeNotifyRegister` 返回 0 与注册成功后未收到消息，对不创建 ExplorerBrowser 的 native-only HWND 探针分开重复了注册生命周期和单次通知投递。生命周期矩阵中共享 HWND 反复注册/注销为 40/40；新建 HWND 后立即销毁也为 40/40；新建 HWND 等待 100 ms 再注册为 36/40，另 4 次注册失败且 `last-error=0`。失败分布与早先“立即销毁失败较多、等待后全通过”的观察相反，因此 HWND 销毁/重建时序不能作为确定根因。所有轮次前台窗口均未变化。

单次投递在 150 ms setup delay 下的六轮有三轮未收到任何 observer 消息，三轮各收到一条 `2000/changed.txt`；未收到消息的轮次均为注册成功、`lock-failures=0`、事件目标为空。setup delay 增至 1000 ms 后六轮中五轮成功、一轮在等待期内未收到通知；这提示较长准备窗口可能改善命中率，但样本不足以确认因果或用延时作为修复。新诊断输出脚本的短回归运行 3/3 通过。

探针中 `SHChangeNotification_Lock` 失败计数为 0 且没有目标/时间线，说明这类超时发生在消息抵达 observer hook 之前，不是收到消息后 PIDL 锁定或目标解析失败。当前注册参数为单个递归 PIDL、文档规定的递归中断/新投递标志组合；Microsoft 文档说明返回 0 表示无效参数或内存不足，但现有 `last-error=0` 和间歇性成功尚不足以判断具体原因。参考 [SHChangeNotifyRegister](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister) 与 [SHChangeNotifyEntry](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ns-shobjidl_core-shchangenotifyentry)。

HostProbe Release 构建通过，0 警告、0 错误；重复脚本能打印脱敏的投递诊断摘要。本轮仅更改测试脚本与调查记录，没有改产品通知标志、重试、延时或 watcher 策略。该 native-only observer 的不稳定不能证明 ExplorerBrowser 生产 watcher 丢失变化，也尚未定位根因；后续应减少外部 Shell 通知负载并继续隔离注册 0 的条件，再决定是否需要生产代码调整。

#### 补充：注册事件掩码对照（2026-10-25）

为验证全事件掩码是否导致 `SHChangeNotifyRegister` 间歇返回 0，HostProbe 的 observer 增加仅供探针使用的十六进制事件掩码覆盖项；默认仍为原 `0x7FFFFFFF`。在正确的 `net10.0-windows10.0.19041.0` Release 输出上，以相同的每次新 HWND、注册后等待 100 ms、共 40 次注册/注销对照：全事件掩码首轮只有 14/40 注册成功；反向再测时全事件掩码 40/40，`SHCNE_UPDATEITEM` 单事件掩码两轮均为 40/40。旧输出目录中先前运行的可执行文件没有新掩码诊断字段，已识别为旧构建并排除，不计入样本。

随后按每种掩码各运行两次真实 `SHCNE_UPDATEITEM` observer smoke：全事件掩码 2/2 收到通知；单事件掩码 1/2 收到，另一次注册成功但等待期内未收到消息。该端到端小样本不支持把事件范围收窄作为注册或投递修复；全掩码低成功率样本没有复现，注册阶段仍呈现时间性波动。HostProbe Release 构建 0 警告、0 错误；所有窗口均为隐藏 HwndSource，前台句柄未变，未创建 ExplorerBrowser。

事件掩码环境覆盖仅存在于 HostProbe 诊断程序集，不进入产品通知实现；当前没有据此缩窄生产事件或调整其行为。由于生命周期矩阵的全掩码结果从 14/40 回到 40/40，而单事件端到端样本还少收到一次通知，现证据只能排除“简单地换成 UPDATEITEM 掩码即可稳定修复”，不能确定 `SHChangeNotifyRegister` 返回 0 的原因。后续若继续调查，应先对齐注册调用是否等待 STA 消息泵及注册/注销清理是否完成，避免将 HWND 高频周转负载误当作产品使用场景。

#### 补充：注册生命周期等待期间泵消息的对照（2026-10-25）

在相同“新建 HwndSource → 注册 → 注销 → 等待 100 ms → 销毁”的每进程 40 次矩阵上，为诊断程序集增加 `EXPLORER_DIRS_HOSTPROBE_NOTIFICATION_PUMP_DURING_SETTLE=1` 选项；开启时等待期间通过 WPF `DispatcherFrame` 泵消息，关闭时仍为原 `Thread.Sleep`。交错执行 8 轮后，普通睡眠共 290/320 次注册并注销成功，消息泵模式 320/320；普通睡眠唯一失败轮为 10/40，另外 7 轮均为 40/40。所有进程前台窗口未变化，也没有创建 ExplorerBrowser。

这批样本显示消息泵模式下未观察到失败，但异常只出现在一轮普通睡眠样本，且普通模式其余轮次均通过；不能确认消息泵能消除 Shell 的间歇注册失败，也不能把这个人为高频销毁窗口压力等同产品生命周期。该环境开关只留在 HostProbe，不用于生产；生产仍不因这组结果改变注册/注销或通知策略。下一步若要确认因果，应在更低干扰条件下增大独立重复样本，并单独记录注册失败与未投递事件，不再把窗口创建和销毁频率作为用户场景结论。

### 2026-10-25 隐藏原生视图多选恢复回归复跑

使用 Release NativeProbe 运行 `--ishellview-selection-batch-spike`，退出码 0。直接批量恢复保留两项选择和独立焦点；replacement fallback 清除额外旧选区后准确恢复两项；单个失效 PIDL 只恢复幸存项，完全失效快照不留选区。上述 fallback 在未分组/分组 × 八种查看方式共 16 组均通过。父窗口保持隐藏、前台句柄未变；探针未创建 ExplorerBrowser、未显示 GUI、未发送键鼠输入，临时夹具在进程结束时清理。

这次复跑确认的是默认原生 `IShellView` 下选择身份与焦点恢复正确性，不验证生产 ExplorerBrowser 宿主、50k STA 延迟、物理键鼠或其他 Shell provider，也没有消除 `SHELLDLL_DefView` 私有同步消息导致的长尾。性能 P0 仍开放；未经有区分力的生产宿主测量，不改通知/刷新时序。

### 2026-10-25 同步刷新异常后的监视恢复

源码追踪发现，目录变化计时器在调用 `Refresh` 前置 `_directoryRefreshInProgress=true`，原先只有异步完成回调会清除此状态；`IShellView.Refresh()` 抛异常时，`Refresh` 会清理选择恢复状态后重新抛出，导致计时器跳过后续所有 watcher 批次。现同步/异步启动与完成路径都会捕获刷新异常：清除卡住的进行中状态，保留强制刷新请求，并按 500 ms 起步、最高 30 秒的退避重试；成功完成后重置退避。诊断状态只记录异常类型和 HRESULT，不记录路径或异常消息。

回归先按预期失败，再覆盖退避递增、上限、成功后重置；完整 Release SettingsSmoke 与 App Release 构建通过，`git diff --check` 通过。本轮未启动 ExplorerBrowser/GUI；真实 COM 异常、异步 Shell 完成时序和重试后可见更新仍待生产宿主/桌面验收，自动刷新 P0 与 STA 长尾未关闭。

### 2026-10-25 手动刷新异常隔离

继续追踪发现，工具栏、菜单和 F5 最终会调用 `PaneControl.RefreshCurrent` → `ExplorerBrowserHost.Refresh()`；同步 Shell 异常此前可能越过命令入口进入 WPF Dispatcher。现在公开刷新入口捕获同步异常，在该窗格状态记录异常类型/HRESULT，并显示可重试提示，避免异常继续冒泡到应用消息循环。捕获逻辑由无窗口回归覆盖成功与异常两条路径；完整 Release SettingsSmoke、App Release 构建（0 警告/错误）及 `git diff --check` 通过。

这只隔离调用返回前同步抛出的异常，不代表 Shell 刷新成功，也不取代目录监视器的重试恢复；异步通知、真实 COM 失败和窗口存活行为仍未通过 ExplorerBrowser/GUI 验证，自动刷新 P0 继续开放。

### 2026-10-25 Shell 刷新负 HRESULT 不再被当作完成

继续追踪刷新结果发现，`GetCurrentView` 或 `IShellView.Refresh()` 返回负 HRESULT 时，旧路径会调用 `afterRefresh` 并返回；浏览器对象不可用时也会调用同一成功续接。自动目录监视器随后可能把失败当作成功完成，重置重试退避并清除强制刷新请求。现将两处负 HRESULT 转成保留原 HRESULT 的 `COMException`，宿主不可用抛出失败，不再运行成功续接：自动刷新进入同步异常恢复，手动刷新由公共入口显示错误，筛选恢复刷新捕获并报告失败。

新增回归验证成功 HRESULT 不生成失败、`E_FAIL` 产生相同 HRESULT 的可重试异常；测试先因方法缺失编译失败，接入后完整 Release SettingsSmoke 退出码 0，App Release 构建 0 警告/错误，`git diff --check` 通过。宿主不可用分支复用已覆盖的同步异常入口，没有单独构造 COM 窗口回归。未运行 ExplorerBrowser/GUI，未实测 Windows Shell 返回失败 HRESULT 的异步行为；自动刷新 P0、STA 长尾及可见更新仍待后续验证。

### 2026-10-25 Shell 通知重复探针保留注册失败参数

为继续区分注册失败与注册成功后的通知漏投，Release HostProbe 构建通过（0 警告、0 错误），在 1 秒 setup delay、300 ms 进程间隔下运行 3 次非 ExplorerBrowser observer 探针：2 次成功收到 `SHCNE_UPDATEITEM`，1 次在 `SHChangeNotifyRegister` 阶段失败。重复脚本此前只报告 `failure=registration`，现从异常诊断中保留注册 sources、event mask、message、entry 数和 `last-error`，不输出临时目录、PIDL 或 HWND 地址。

失败样本为 `sources=0x9003`、`events=0x7FFFFFFF`、`message=0x8021`、`entries=1`、`last-error=0`；三轮没有投递阶段失败。此结果再次表明 observer 注册具有间歇性，但 `last-error=0` 仍不足以定位 Windows Shell 的原因，也不能外推为生产 ExplorerBrowser 的 `FileSystemWatcher` 或 `SHChangeNotify` 行为。未改产品通知或刷新策略；Shell 注册根因与 P0 长尾继续开放。

#### 补充：低频与高频进程周转配对复测

早期重复脚本只在创建临时文件后发送 `SHCNE_UPDATEITEM`，没有先用 `SHCNE_CREATE` 将新项目通知给 Shell。因此这些运行中的通知成功/失败计数不是有效的“已知 Shell 项目更新投递”测量，不能用来比较进程周转率。早期日志中 `SHChangeNotifyRegister` 返回 0 的次数仍是该 API 注册结果的直接观测，但其频率受后续探针运行顺序/系统状态影响，不能据此推导产品 watcher 故障。

#### 补充：单进程同 HWND 重注册与投递窗口

早期版本同样对每轮刚创建的唯一文件只发送 `SHCNE_UPDATEITEM`，没有对应的 `SHCNE_CREATE`。其 `32/36` 与 `36/36` 数字只说明夹具在两种 settle 配置下返回情况不同，不是有效的项目更新投递对照；此前关于 300 ms settle 的因果线索撤回，不应用于产品决策。

#### 补充：已知 Shell 项目上的 CREATE / UPDATEITEM 生命周期对照

为修正夹具，HostProbe 现在先对临时文件发 `SHCNE_CREATE` 并确认 observer 收到该项目，再修改文件并调用生产 `ShellChangedItemNotification.NotifyChangedItem`，等待同一项目的 `SHCNE_UPDATEITEM`。它支持单一 observer 多项、同进程同 HWND 重注册和同进程每轮新建 HWND 三种模式，并记录 CREATE、UPDATE、注册、注销失败以及前台变化；不创建 ExplorerBrowser。

Release HostProbe 构建通过。单一 observer 连续 12 个临时项目的 CREATE→UPDATE 为 12/12。复用同一 HWND 的两组交错试验各 24 轮，`0/300 ms` settle 两组均各 12/12；没有注册、投递或注销失败。每轮新建/销毁 HWND 的 24 轮交错组同样两组各 12/12。分开顺序执行时结果曾波动：新 HWND + 0 ms settle 有一轮 2/12，100 ms settle 有一轮 0/12，300 ms settle 有两轮 12/12；随后 0/300 ms 交错组通过，故不能据这些顺序组认定 300 ms 是有效阈值。另有一轮原始单次 smoke 在 CREATE 阶段超时；有效项目夹具下的跨进程重复也曾分别得到 10/12（300 ms 进程间隔）及 6/12（1,200 ms 间隔），其中同时包含注册失败、CREATE 漏投与 UPDATE 漏投，未呈现随更长间隔改善的趋势。所有有效记录轮次前台守卫通过。

当前结论是 native-only `SHChangeNotifyRegister` observer 的注册/事件投递本身在本机呈现时段相关波动；有效正控制证明部分配置可稳定收到 CREATE 和 UPDATE，但失败样本不足以定位是注册生命周期、Shell 服务调度还是其他环境因素。它不使用 ExplorerBrowser 产品宿主，也不证明生产 watcher 或视图更新丢失文件。未更改产品通知或刷新策略；暂停用重复同类 observer 运行拟合固定 settle 延时，后续需在生产宿主受控隔离测试中建立等价证据，当前仍受无 GUI 验收约束。

### 2026-10-25 将生产目录 watcher 的启动和释放移出 Dispatcher

源码检查确认，允许 UNC 与映射网络目录后，生产导航回调仍同步执行 `new FileSystemWatcher(path)`、`EnableRaisingEvents=true`；导航切换和窗格销毁也同步停用/释放 watcher。慢速或离线网络路径因此可能让 Shell Dispatcher 等待 watcher 初始化或清理。该线程等待风险由调用位置和线程归属核实，但尚未在真实慢速/离线网络上测量，不把它认定为此前自动刷新遗漏的原因。

现改为后台构造 watcher；回到原 Dispatcher 后，只有监视器代际、导航代际、路径、视图和队列仍匹配时才登记候选，然后在后台启用。候选过期或启用失败会撤销登记并在后台释放；正常导航等待 watcher 初始化期间不会启动目录计数采样；错误恢复沿用原退避策略，成功后仍强制协调脱离期间可能发生的目录变化。停止监视器先失效队列与代际，再后台释放活动 watcher。

`DirectoryWatcherStartupSmoke` 先对缺少启动实现的旧源码观察到预期编译失败，接入后验证创建/启用/释放线程边界、慢构造不阻塞调用方、导航过期候选不发布并释放、有效 watcher 启用，以及启用异常后的撤销与清理。完整 Release SettingsSmoke 退出码 0；App 与 HostProbe Release 构建 0 警告/错误。隐藏 HostProbe watcher-recovery 通过；产品监视模式首次因独立 Shell observer 注册返回 0 而中止，改用不依赖该 observer 的模式后退出码 0，覆盖跨窗格复制/移动/删除、文件夹创建/删除、计数与选区/焦点恢复。该模式创建隐藏 HWND，不显示窗口、不调用 GUI。

仍未验证真实远程/离线网络初始化时延、UI 可见响应、跨 Windows 版本或其它 Shell provider。此改动仅消除 watcher 创建/启用/释放在 Dispatcher 上的同步工作；Shell 通知偶发遗漏、大目录 STA 长尾和自动刷新 P0 保持开放。

### 2026-10-25 50,000 项产品监视与 native-only 同负载队列跟踪

HostProbe 压力模式先暴露一个测试调用错误：反射调用带可选 `IntPtr` 参数的 `NoteNativeUserInput` 时传入 `null`，运行于夹具操作前抛出 `TargetParameterCountException`。修正为传入 `IntPtr.Zero` 并重建后，执行 50,000 项创建/删除和 128 项批量删除对照；产品/native-only 两轮使用相同消息泵、Dispatcher queue-latency、Shell callback trace 设置。产品轮也开启 `focused-item-lookup=direct-select` 路径追踪；之前关闭 Shell callback trace 的轮次虽然最终计数/选区/焦点通过，却因该追踪标记未生成而退出码 1，不纳入本次有效比较。

产品轮退出码 0：创建/删除分别 38,018/24,438 ms，生产 watcher 两阶段均为 50,000/50,000，视图/磁盘 3/3，选区/焦点恢复，删除侧 3 次 fallback。最大 Dispatcher heartbeat gap 为 1,156 ms、ContextIdle gap 为 1,415 ms；消息泵最大取出空档为 260.4 ms，单个被跟踪 WndProc 最大为 177.7 ms。产品目录阶段计时中 watcher queue 抽样最大 1.5 ms、Dispatcher handoff 抽样最大 0.463 ms、timer tick 最大 17.594 ms、计数检查最大 0.509 ms、`IShellView.Refresh()` 最大 15.821 ms，三次删除通知合计 257.5 ms、单次最大 176.8 ms。

紧接的 native-only 对照退出码 0：创建/删除分别 35,892/25,707 ms，视图/磁盘最终 3/3；产品 watcher、fallback、Refresh 均为 0。该模式不验证产品的选区恢复，故 `selection-restored=False` 为预期；焦点恢复为 true。最大 Dispatcher heartbeat gap 237 ms、ContextIdle gap 309 ms、消息泵最大空档 204.4 ms、单个被跟踪 WndProc 最大 51.5 ms。两轮都是本机隐藏 ExplorerBrowser，负载和采样配置一致，但只有一组顺序结果；它支持“产品路径中 ContextIdle backlog 较高”的相关性观察，不能证明产品刷新或通知是唯一原因。

关键差异是 ContextIdle 队列延迟约 1.4 秒，而消息泵空档和单个窗口过程都低于 0.3 秒；产品计数读取、刷新和 timer tick 也远不足以解释该差值。后续应在交错重复中将慢队列项映射到具体 Dispatcher callback 和 Shell 枚举区间，并单独评估探针自身 Background/Input/Normal 采样负载。当前不调整产品刷新时间、线程模型或通知策略；可见 UI、其它 Shell provider 与真实网络位置仍未验证，自动刷新 P0 保持开放。

### 2026-10-25 反向顺序交错复测

为减小前一组“产品先、native-only 后”的顺序偏差，以 native-only → 产品模式反向顺序各执行一轮相同 50,000 项创建/删除及 128 项批量删除负载。两轮使用相同的消息泵、Dispatcher 队列延迟及 Shell callback 追踪设置。

native-only 创建/删除用时 40,964/43,991 ms，最终 Shell/磁盘为 3/3，选区与焦点检查通过；observer 收到 49,999/50,000 个事件，因此进程因该门槛退出码 1，不能算完整通过。产品轮退出码 0，创建/删除用时 41,109/37,771 ms，生产 watcher 为 50,000/50,000，最终 Shell/磁盘为 3/3，选区/焦点恢复，创建/删除 fallback 为 1/3。

两轮独立队列探针的 ContextIdle 最大排队延迟分别为约 1,246/1,342 ms，Dispatcher ContextIdle 最大间隔约 1,137/1,411 ms。native-only 也出现同量级长尾，原先单组顺序对照所提示的产品独有 ContextIdle backlog 未能复现。消息泵最大空档约 117/430 ms；仅跟踪 Dispatcher message-only HWND 时，WPF `0xC471` 的单次慢处理最高约 96/430 ms。产品轮能看到 `QueueCreatedItemNotification` DispatcherTimer 与目录变更回调排队，但队列延迟是等待时间，不能表明这些 callback 是造成 STA 忙碌的根因；`0xC471` 的消息计时也不暴露其内部 Shell 工作。

进一步读取产品轮的完整 callback 时间线后，发现 `ScheduleDeferredRefreshSelectionRestore` 的 Background timer 两次实际执行约 422/430 ms；其中一次与 `0xC471` 的 422 ms 消息空档重合。其内部细项记录到 Shell selection batch API 约 57 ms、焦点 `SelectItem` 约 36 ms，远小于整个 Dispatcher 操作区间。该 timer 操作时间包括可能发生的同步 Shell 回调/消息泵重入，不能解释为单个 COM 调用时长。该调用路径是下一步值得拆分的候选，但两次观察尚不能说明它造成大目录所有 ContextIdle 长尾；native-only 同样有约 1.25 秒 ContextIdle 延迟。

因此当前证据只支持：两种模式在该负载与探针设置下都可能出现约 1 秒以上的 ContextIdle 延迟；这组反序结果不支持把该长尾单独归因于产品 watcher 或刷新策略。由于 native-only observer 漏 1 个事件，且每种模式仍只有一轮，不能据此比较稳定分布。暂不改生产调度；后续若继续，应优先设计低扰动的 `0xC471` 与 Shell 枚举分段关联测量，而不是继续重复同类整段压力轮。日志保存在 `%TEMP%\explorer-dirs-50k-native-interleaved-20261025.log` 与 `%TEMP%\explorer-dirs-50k-product-interleaved-20261025.log`。没有操作可见桌面，也没有生成 release。

### 2026-10-25 Shell `OnStateChange` 单次耗时关联

前一轮的产品 Dispatcher 时间线出现 `ScheduleDeferredRefreshSelectionRestore` Background timer 422/430 ms 的执行区间，但 state-change 聚合计时只能显示总量和最大值，不能对齐具体回调。现在在已有 opt-in state-change trace 中为每个保留样本增加 `elapsed-ms`；关闭 trace 时不调用计时器、不分配样本。SettingsSmoke 的新断言先在原实现上失败，再在改动后通过；完整 Release SettingsSmoke 退出码 0，HostProbe Release 构建 0 警告/错误。

新隐藏产品 50k 轮退出码 0，创建/删除 watcher 各 50,000/50,000，最终视图/磁盘 3/3，选区/焦点恢复，fallback 为 1/4。采到 50 条 `OnStateChange`，合计 306.855 ms，最大单次 23.529 ms；同轮 `ScheduleDeferredRefreshSelectionRestore` 两次 Dispatcher timer 执行约 146.5/167.6 ms，未出现前次的 422/430 ms。独立 ContextIdle 队列最大延迟约 475.4 ms，消息泵最大空档 243.9 ms，最大 Dispatcher 心跳间隔约 433 ms。

此数据说明前次数百毫秒 Dispatcher timer 运行区间不能归因于单个 Shell state-change 回调；它也表明 50k 隐藏宿主的队列和 STA 延迟在轮次间显著变化。它未揭示 `0xC471` 内部做了什么，也未解释 native-only 的相近 ContextIdle 长尾。下一步需要把当前已有的 callback、timer 和窗口消息时间点放到同一时钟轴做区间关联，避免单独看最大值。日志：`%TEMP%\explorer-dirs-50k-product-selection-callback-timing-20261025.log`。未操作可见窗口或生成发布物。

随后为 opt-in Shell callback、选择恢复步骤、Dispatcher operation 和慢 WndProc 增加同源 `Stopwatch` ticks，保证能精确比较跨探针区间。新一轮产品 50k 退出码 0：watcher 创建/删除均为 50,000/50,000，最终视图/磁盘 3/3，选择/焦点恢复，fallback 为 0/1。

这一轮中，`ScheduleDeferredRefreshSelectionRestore` 的一个 Background Dispatcher operation 为 163.7 ms（start/end ticks：22919027266218/22919028903312），对应 `0xC471` WndProc 为 163.8 ms（22919027266189/22919028904121）；起点只差 29 ticks，终点差 809 ticks。operation 内记录到 selection batch API 55.2 ms、两次直接选中 7.9/50.2 ms、focused-item `SelectItem` 48.6 ms，总计约 161.9 ms；同区间 9 个 `OnStateChange` 回调合计约 1.82 ms，单次最大 0.67 ms。此样本能把这段约 164 ms UI STA 占用定位到同步 Shell 选择/焦点恢复调用，而非 Shell state-change 回调；但仍是单次压力轮中的一个窗口，不能外推为全部 Shell 延迟根因。

下一步检查恢复算法何时对尚未可枚举的 PIDL 重复调用同步 `SelectItem`，评估可逆的分段/重试调整；必须保持选中项集合、未选中焦点以及用户输入门控的现有语义，并用状态 smoke 与隐藏宿主验证后再决定是否更改产品策略。其余 `0xC471` 处理和两模式均出现的 ContextIdle 长尾仍未解释。日志：`%TEMP%\explorer-dirs-50k-product-common-ticks-20261025.log`。未操作可见窗口或生成发布物。

### 2026-10-25 选区完整性门控和焦点分阶段恢复

源码复核发现 direct selection loop 把“至少一个保存项成功选中”当成整组选区已恢复。deferred timer 随后会标记 `selectionRestored=true`，之后只重试焦点，可能跳过未成功的其它 PIDL。现在 direct 项逐项调用后会用当前选中 PIDL 集合与快照逐项核对，只有完整匹配才报告 selection-restored。焦点-only 快照的空选区视为已满足选择阶段。

deferred refresh 现在在选区尚未完整时不调用 focused-item `SelectItem`。完整选区确认后，沿原 100 ms timer 再做焦点恢复；无焦点快照当次完成。现有导航/用户输入门控、Shell SelectionVersion 检查和 1.5 秒截止未变。SettingsSmoke 状态 gate、新增 NativeProbe selection-only→focus-only 两阶段测试均通过；已有默认直接恢复、替换选区、stale PIDL 和 16 种分组/查看方式 replacement fallback 仍通过。

更新后的隐藏产品 50k HostProbe 退出码 0：生产 watcher 创建/删除 50,000/50,000，最终 Shell/磁盘 3/3，选区/焦点均恢复，fallback 0/2。前序同类 trace 中某一组合选区+焦点 tick 为 163.7 ms；新样本 selection-only tick 113.3 ms，后续 focus-only `SelectItem` 52.3 ms。单次 STA 处理峰值因此下降约 50 ms，但 selection 阶段自身仍约 113 ms，且样本数不足以证明统计改善。ContextIdle queue max 170.1 ms、Dispatcher heartbeat max gap 266 ms；其它 STA 长尾仍可能来自别的 Shell 操作。

本轮未改成逐选中项分发到多个 timer，因为当前实测仍需要根据首轮 batch 只恢复部分 PIDL 后直接补齐所有项；下一步先判断何时可跳过重复 batch 并以 direct item 列表完成恢复，保持清除旧额外选择和 exact-set 验证。无可见 GUI、未发布。新日志：`%TEMP%\explorer-dirs-50k-staged-selection-20261025.log`。

随后验证“空选区直接选择以跳过首个慢 batch”的候选。修改前先让 NativeProbe 要求此路径；旧实现按预期因走 `selection-batch-path=direct` 而失败。加入空选区分支后，隔离 stock `IShellView` 反例显示 direct `SelectItem` 返回成功但即时精确读回仍是 0 项；同一全枚举 fixture 的 batch path 可直接恢复两项。说明仅凭当前选中数为 0 无法区分“刷新中尚未枚举”与“视图已就绪”；该候选分支已撤回，保留此前完整验证通过的 staged 版本。NativeProbe 再次 Release 构建 0 警告/错误，selection batch spike 退出码 0，前台不变、父窗口隐藏。

当前不跳过批量恢复。若要缩短约 113 ms 的 selection-only tick，下一步先找对目标 PIDL 的 readiness 信号（无需遍历整个目录），或者评估已验证状态下的逐项恢复时序；必须确保旧额外选择仍被清除、最终 selection PIDL 集合精确匹配。没有改变用户可见刷新策略、没有操作可见桌面，也没有生成 release。


### 2026-10-25 隐藏视图探测目标 PIDL 定位信号

Microsoft 的 `IFolderView::GetItemPosition` 文档只承诺返回项目在视图中的坐标，未将其描述为目录枚举完成状态。本机隐藏 stock `IShellView` 探针补充记录了时序：创建 view 后当前项目数为 0，同一个已存在 fixture 文件的子 PIDL 查询返回 `E_FAIL`；待 Shell 项目数达到 4 后，同一查询返回 `S_OK`。NativeProbe Release 构建 0 警告/错误，selection batch spike 退出码 0，父窗口隐藏且前台句柄不变。

随后复用一个已删除项目的 child PIDL，在 Shell 计数确认刷新完成后查询同一方法，也返回 `E_FAIL`；该结果与尚未枚举时完全相同。因此 `GetItemPosition` 只能证明成功时当前可定位，失败不能作为重试/等待判断。本次只增加隔离探针日志和路线图记录，没有更改产品选择恢复策略。后续寻找能区分“仍枚举”与“稳定缺失”的信号，或验证有界失效规则，并确认多目标情况下删除项不会阻塞其余选区恢复。
### 2026-10-25 空视图期间延后选区重放

此前的 50k 产品 trace 显示刷新完成回调在 `ItemCount=0` 时立即重放保存的多选，Shell 随后重枚举到 50,003 项。新增 `ShellSelectionRestoreGate.ShouldDeferSelectionRestore`，只在快照包含选中 PIDL 且原生 `IFolderView.ItemCount(SVGIO_ALLVIEW)` 成功返回 0 时延后这一同步恢复；计数读取失败、focus-only 快照不改变原行为。`RestoreSelectionAfterRefresh` 在 Shell COM 读取后再次检查浏览器/路径/导航代际，防止重入导航后使用过期结果。deferred timer 继续使用既有 100 ms 间隔与 1.5 秒上限；上限后的最后一次回退明确绕过空视图门控。

状态 smoke 先因缺少新门控方法按预期构建失败，接入后完整 Release SettingsSmoke 通过；App 与 HostProbe Release 构建均为 0 警告/错误。隐藏产品 `--live-shell-directory-stress-large 50000` 退出码 0：50,000 个文件创建/删除 watcher 事件均完整，最终 Shell/磁盘 3/3，多选 `second.txt`/`third.txt` 与独立焦点 `initial.txt` 均恢复。创建和删除刷新后的首次回调均记录 `selection-restore-deferred-empty-view count=0`，restore callback 为 0 ms；随后 view 枚举到文件后完成恢复。延后的 batch API 在本样本约 47.9/48.8 ms；最大 Dispatcher heartbeat gap 904 ms，故此优化只移除了计数明确为 0 时的无效早期批量恢复，不代表其它 STA 长尾已解决。

本轮未操作可见桌面或生成 release。仍需验证最终保持空目录时的 1.5 秒有界回退、部分枚举多目标快照，以及可见体验；自动刷新 P0 保持开放。
### 2026-10-25 稳定空目录的 deferred 恢复边界

新增 `--live-shell-empty-directory-deferred-restore` 隐藏产品 HostProbe：等待 pane 0 的三项视图加载并选中三项，保存生产 `ShellViewSelectionSnapshot`；删除夹具三文件，通过当前 `IShellView::Refresh` 使 Shell 视图成为稳定空视图，再调用生产 `ScheduleDeferredRefreshSelectionRestore`。结果退出码 0：13 次 empty-view defer、deferred-refresh 阶段 0 次批量 API；到期后的单次逻辑 fallback 内部先尝试直接批量 API、再走 replacement fallback，因此该最终阶段记录 2 次 batch API。1,548 ms 后 pending 清除，之后 300 ms fallback batch 数没有增加；Shell 计数及选区均为 0，父窗口隐藏。

探针首次暴露自身的 snapshot 所有权错误：调度成功后产品 timer 接管并释放 snapshot，而探针的 `using` 又在退出时二次释放，造成 native heap corruption。已改为调度成功转移所有权，只有调度失败才由探针释放；重建后重复运行退出码 0。另单独运行现有 `--live-shell-selection-partial-snapshot` 退出码 0，完整 3 项快照在 Shell 报告 2 项部分选区后仍保留并恢复 3 项。该测试覆盖实际产品回调和恢复链，但不代表 50k 异步枚举中多个目标 PIDL 分批到达的完整时序。

没有操作可见桌面或生成 release。稳定空目录的有界终止已验证；自动刷新 P0、大目录其他 STA 长尾和可见体验仍开放。

### 2026-10-25 目标 PIDL readiness 重试门控

为检查“项数已到位但目标项尚未进入 view”的可能性，新增隔离 stock `IShellView` NativeProbe：预建 20,000 个普通文件，打开空父窗口中的 Shell 视图，并在初次枚举期间轮询 4 个分散 child PIDL 的 `IFolderView::GetItemPosition`。两次样本首次读到总项数 20,000 后，目标分别在 79.0/84.3/91.4/98.7 ms 与 58.0/62.1/67.9/75.0 ms 被首次定位；故 `ItemCount` 满值仍不足以确认每个快照 PIDL 已可操作。小视图中也观察到定位前 `E_FAIL`、定位后 `S_OK`。`E_FAIL` 对 stale PIDL 仍相同，不能作为目标已永久缺失的判据。探针仅创建隐藏 stock `IShellView`，无 ExplorerBrowser、无输入，结束时前台未变；两次 Release 运行退出码均 0。

据此，`ShellViewSelectionSnapshot.ReadTargetReadiness` 为每个捕获的选中 PIDL 生成定位状态向量。deferred timer 第一次观察照常尝试；后续仅重新查询尚未定位的目标，当前选择代际内已定位 PIDL 复用正向结果。Shell `SelectionVersion` 改变时清空该向量缓存并重查所有目标；状态变化后才重做批次，或到达既有 1.5 秒最终回退时强制尝试。未确认完整选区期间不触发焦点恢复；最终 fallback 仍尝试剩余选区。readiness 查询失败会走旧的重试路径；新增 trace 记录位向量、查询数与决策，不输出路径/文件名。

`ShellSelectionRestoreGateSmoke` 的新策略断言先因方法缺失而按预期编译失败，加入门控后 Release SettingsSmoke 全量通过；App、NativeProbe、HostProbe Release build 均通过，0 警告/错误。隔离 selection batch spike 在已枚举两项 view 中确认 readiness=`11`；提供缓存 `10` 时仅执行一次未定位目标查询并返回 `11`。删除一个选中目标后 fresh 检查仅一个 PIDL 保持可定位，replacement fallback 恢复幸存项；直接恢复和分组/查看矩阵退出码 0。20k 多目标定位探针两次退出码 0。没有启动生产 ExplorerBrowser HostProbe 或可见 UI，因此尚不能断言多窗格产品中批次数或 STA 延迟下降；需在之后授权窗口用真实生产宿主复核。无 release。

2026-10-25 选区代际重入保护：readiness COM 查询前后再次读取 `ShellBrowserSite.SelectionVersion`；若期间发生 Shell selection/state-change 回调，则丢弃该 observation 和缓存，标记下一 tick 重新尝试，避免把跨代际向量用于当前批量恢复。新增稳定/变化状态门控 smoke，旧实现先按预期编译失败；完整 Release SettingsSmoke、App 与 HostProbe Release 构建通过，0 警告/错误。此轮没有构造真实 COM 重入、运行 ExplorerBrowser/NativeProbe 或 GUI，因此这是源码防护与状态逻辑证据，不是实机竞态复现；现有 100 ms cadence 和 1.5 秒上限不变。

### 2026-10-26 选区恢复 readiness 与批量回退复核

当前版本 Release `NativeProbe` 构建通过（0 警告、0 错误）。单次隐藏 stock `IShellView` 20,000 项样本中，`ItemCount` 只发生一次计数跃迁并直接到达 20,000；四个分散目标 PIDL 随后于 58.8、63.9、71.3、80.8 ms 首次可定位。这与此前两次样本一致说明总数收敛与各目标 PIDL 可操作之间存在间隔，但单轮不能用来推断跨机器分布或用户可见延迟。

2026-10-26 后续复跑：相同隔离枚举探针再次观察到 `ItemCount` 单次跃迁至 20,000，四个目标首次可定位时间为 61.7、66.2、74.0、83.7 ms；父窗口仍隐藏、前台句柄未变。配套 selection batch spike 再次通过直接恢复、额外选区清理 fallback 的 16 种分组/查看方式、单个 PIDL 失效时保留一个幸存目标，以及全部失效时清空选区。`GetItemPosition` 对未枚举和删除后的 stale PIDL 都返回 `E_FAIL`；这轮没有新的 Shell 信号可区分两者，也未测量恢复耗时。

同轮生产 `ShellViewSelectionSnapshot` 批量恢复探针通过：精确两项多选和焦点可恢复；清空旧选区的 replacement fallback 在 16 种分组/查看方式组合下通过；删除一项后仅恢复幸存项，全部快照 PIDL 失效时不遗留选择。父窗口保持隐藏、前台句柄不变；没有创建 ExplorerBrowser 或发送输入。

本样本没有计时批量选择 API，也没有制造生产 deferred timer 中的逐项可见窗口，因此不证明重复批量恢复耗时或新策略收益。当前行为继续依赖目标级 readiness 与完整选区核验；任何缩减批次的改动仍需在上述旧额外选区、失效 PIDL、分组/查看方式矩阵下证明集合精确一致，再另行取得生产宿主和可见 GUI 证据。

### 2026-10-26 `IShellFolder.GetAttributesOf` 的 readiness 对照

为寻找 stale PIDL 与尚未出现在 view 中的有效 PIDL 之间的独立信号，扩展隔离枚举探针：预先创建 20,000 个文件、四个分散有效目标，以及一个在创建 Shell view 前已删除的 stale 控制项；枚举期间同时查询目标的 `IFolderView.GetItemPosition` 和父 `IShellFolder.GetAttributesOf(SFGAO_VALIDATE | SFGAO_FILESYSTEM)`。

两轮结果不一致。第一轮四个有效 PIDL 在 view count 仍为 0 时，父文件夹属性查询已成功（约 59.8–61.1 ms），而 `GetItemPosition` 到 view count 为 20,000 后约 83.0–104.9 ms 才成功；stale PIDL 的属性查询始终为 `E_FAIL`。第二轮四个有效目标的属性查询直到 view count 达到 20,000 才成功，约 69.4–90.6 ms，且与 `GetItemPosition` 首次成功时刻接近；stale 控制仍为 `E_FAIL`。两轮父窗口均保持隐藏、前台句柄不变，进程退出码均为 0。

随后加入 `IShellFolder.GetDisplayNameOf(SHGDN_INFOLDER | SHGDN_FORPARSING)` 对照。第三轮中四个有效 PIDL 都要等到 view count=20,000 后才成功；但预先删除的 stale 控制仍返回 `S_OK`，`StrRetToBufW` 也能成功转换其显示名。因此显示名查询不能用来确认该项目仍存在。

因此属性查询成功是项目仍可由父 Shell 文件夹解析的正向证据，但属性查询失败也可能出现在有效、尚未完成枚举的项目上，不能作为失效判据；显示名查询甚至可能为 stale PIDL 成功。样本显示两者都不能替代目标可定位信号或安全地缩短重试窗口；生产恢复策略保持不变。探针是隐藏 stock `IShellView`，未创建 ExplorerBrowser、未触碰用户桌面，也未测量用户可见延迟。

### 2026-10-26 readiness 全正后选区仍未完整恢复

新增隔离枚举恢复探针：源目录只含四个被选中的项目，另一个独立目标目录含 20,000 项；从源视图捕获真实 `ShellViewSelectionSnapshot`，在目标目录新建 stock `IShellView`，目标视图从空计数开始枚举。多轮中，目标计数从 0 到 20,000 后首次 readiness 向量为 `1111`，但首次生产 snapshot restore 只选中 1/4 项、返回未完成；隔离宿主的 `ShellBrowserSite` 未收到 CDB3 状态回调（SelectionVersion 保持 0），所以此前只靠 readiness/版本变化/1.5 秒最终回退的纯策略会把选择保持到最终回退。对所有目标均 positive 的状态每隔至少 100 ms 再试后，第二次仍为 1/4，第三次约 312–315 ms 才达到精确 4/4；该实验的文件夹项目身份匹配且真实读回计数为 4。该现象说明 `GetItemPosition` 全正并不足以证明 `SelectAndPositionItems` 已能完整应用目标集合。

产品 deferred restore 现在在“上次批量恢复仍不完整、至少 100 ms 已过、readiness 向量非空且全部为 `1`”时允许再次尝试；存在 `0`/未知 readiness 时不按时间重试，保留目标状态变化和 1.5 秒最终回退，避免失效项触发反复的 replacement 清选。新增纯门控 smoke 先因缺少 API 按预期编译失败，再通过；完整 Release SettingsSmoke、App 与 NativeProbe Release 构建均通过（0 警告/错误），隔离恢复探针退出码 0、父窗口隐藏、前台不变。

该探针创建的是默认 `IShellView`，不是产品 ExplorerBrowser；`ShellBrowserSite` 在它的宿主中没有收到 CDB3 回调，故不能代表产品中 `SelectionVersion` 的具体时序，也没有测量产品 STA 成本、真实文件操作或可见焦点。快速重试的收益/多窗格响应以及“部分 readiness + 失效项”定时回退组合仍待生产宿主/GUI 验收；未生成 release。

2026-10-26 再次复跑 `--ishellview-selection-enumeration`：Release NativeProbe 构建 0 警告/错误；20,000 项的 `ItemCount` 仍只发生一次跃迁，四个分散目标 PIDL 首次可定位时间为 66.6、73.4、80.0、87.3 ms，均发生在计数到达 20,000 之后。stale PIDL 的属性校验仍为 `E_FAIL`，显示名解析仍为 `S_OK`；隔离父窗口保持隐藏且前台句柄未变。此轮复核了目标可定位顺序，但没有调用生产 `ShellViewSelectionSnapshot.Restore`，不能说明实际恢复在目标逐项 ready 时的选区结果。

该轮之后已补上全正 readiness 和混合 readiness 两种 fresh-view 恢复场景：真实生产快照恢复会先遇到不完整选择，再按状态门控有限重试或等待最终回退；新结果见下节。此隐藏 stock `IShellView` 仍不能替代产品 ExplorerBrowser HostProbe 或可见 GUI。

### 2026-10-26 混合 readiness 下的幸存选区恢复

扩展 `--ishellview-selection-enumeration-restore`，新增 `--ishellview-selection-enumeration-restore-stale` 场景：源视图捕获四项真实 Shell 选区快照；目标目录包含 20,000 项中的 19,999 项，特意缺少最后一个快照目标。目标 fresh view 的 readiness 收敛为 `1110`，目标项目数为 19,999。

混合场景在约 89.1 ms 首次调用生产 `ShellViewSelectionSnapshot.Restore`，readback 只有 1 项且整组恢复正确地报告未完成。稳定 `1110` 后没有因 100 ms 间隔重复清选/替换；约 1,554.1 ms 的有界最终回退后，真实 Shell 读回 3 个幸存项目。进程退出码 0。作为对照，同一探针全正 `1111` 样本在约 76.8、222.4、343.1 ms 尝试三次，第三次读回完整 4 项。

两种场景都使用隐藏默认 `IShellView`、隐藏父窗口和真实生产快照恢复函数；前台句柄保持不变。探针宿主没有收到 CDB3 状态回调（`SelectionVersion=0`），因此此证据验证的是恢复函数和重试 gate 在这两种 PIDL 集合上的状态语义，不等同产品 ExplorerBrowser 的 deferred Dispatcher timer；也未测出产品 STA 成本或真实焦点。未启动可见 GUI、未发送输入、未发布。下一步改用隐藏产品 HostProbe 复验刷新和选区/焦点。

### 2026-10-26 最新恢复策略下的 10,000 / 50,000 项产品宿主复核

使用当前源码的隐藏生产 HostProbe `--live-shell-directory-stress-large`，先运行 10,000 项并开启 `EXPLORER_DIRS_DISPATCHER_QUEUE_LATENCY_TRACE=1`。退出码 0；产品 watcher 和独立 observer 创建/删除均 `10,000/10,000`，目标 pane 视图计数在创建后/最终删除后为 `10003/3`，创建/删除两轮均恢复 `second.txt`、`third.txt` 两项选择及未选中的 `initial.txt` 焦点。产品 watcher 各触发一次创建 fallback、两次删除 fallback；Normal/Input/ContextIdle 最大排队延迟为 10.2/10.8/11.6 ms，最大 Dispatcher 观察间隔 107 ms，专用线程心跳最大间隔 24 ms。

随后以同样设置运行 50,000 项。退出码 0；observer 与产品 watcher 创建/删除均 `50,000/50,000`，目标 pane 视图计数在创建后/最终删除后为 `50003/3`，两项选择与独立焦点均恢复。创建/删除用时约 36.8/30.2 秒，分别触发 1/4 次计数 fallback；Normal/Input/ContextIdle 最大排队延迟为 134.8/134.9/218.6 ms，最大 Dispatcher 观察间隔 279 ms，专用线程心跳最大间隔 53 ms。队列探针 337 次 timer callbacks 均完成，无 post failure 或重叠。

此前一轮相同 50,000 项场景但未开队列追踪时，产品 watcher 完整收到事件而独立 observer 少一个删除事件，退出码 1；Dispatcher 最大观察间隔约 8,529 ms、独立线程心跳最大间隔 42 ms。之后的 10,000 与 50,000 项队列追踪轮都未复现该长间隔，因此只能记录为未解释的单轮波动。队列追踪可能改变调度，三轮不能用于推断优化前后的响应改善；fallback 仍参与最终收敛。全部测试为隐藏本机生产宿主，不含可见 UI、真实远程网络、其它 Shell provider 或桌面输入，自动刷新 P0 继续开放。

### 2026-10-26 新选区恢复策略下的 10,000 项产品宿主复验

使用隐藏生产 HostProbe Release 场景 `--live-shell-directory-stress-large 10000` 复验当前 App 源码。四个 ExplorerBrowser 视图均在未设置 `WS_VISIBLE` 的 `HwndSource` 内创建；探针不调用 `ShowWindow` 或 `Activate`。该场景创建并删除 10,000 个临时文件，同时运行产品 watcher、Shell 目录计数协调及选择/焦点恢复。

运行退出码 0。隔离 observer 与产品 watcher 分别观察到创建/删除各 10,000/10,000 个事件；创建路径发送 1 次合并 `SHCNE_CREATE`，仍发生 1 次创建 fallback；删除路径仍发生 fallback。创建、删除 settled 后都保留 `second.txt`/`third.txt` 两项选择与未选中的 `initial.txt` 焦点，四窗格最终 Shell/磁盘计数为 3/3。128 项删除批次记录到恰好一次 stale 项类型查询，确认是文件且批次视图最终收敛。

第一轮最大 ContextIdle 间隔约 1,059 ms、Dispatcher 心跳最大间隔约 869 ms；四窗格 watcher 与选择/焦点最终状态正确，但 STA/Dispatcher 仍有明显长尾。

启用 250 ms `DispatcherQueueLatencyProbe` 后复跑第二轮，同样退出码 0、完整 watcher 事件和选择/焦点最终状态通过。探针共安排 98 次 timer callback；Normal、Input、ContextIdle 各排入并完成 98 个操作，post failure 均为 0，timer callback 最大间隔 252.6 ms、回调无重叠。最大排队等待分别为 Normal 95.1 ms、Input 484.0 ms、ContextIdle 841.5 ms。该轮说明至少一部分长间隔可直接观察为 Dispatcher operation 的排队延迟；但这组采样没有把排队窗口与具体 Shell `WndProc` 或同步 COM 调用逐段对齐，因此不能归因单一消息或恢复步骤，也不证明提高优先级就会改善体验。

第三轮同时启用 `DispatcherQueueLatencyProbe` 与 `ThreadMessagePumpTraceProbe`，同样通过且选区/焦点恢复。该轮最大 ContextIdle timer 间隔约 101 ms，队列等待峰值为 Normal 9.6 ms、Input 17.7 ms、ContextIdle 40.9 ms；消息泵观测到 4,222 条消息，`0xC471/SHELLDLL_DefView` 达到至少 10 ms 的调用有 82 次、累计 975.1 ms、单次最多 35.3 ms，消息泵最大取消息间隔为 37.6 ms。最大 ContextIdle 空档内共看到 4 个 ContextIdle operation（重叠 35.3 ms）及 5 个 Background operation（重叠 7.4 ms），没有单次长达 0.8 秒的消息或回调。

第三轮与前两轮差异很大：额外 hook 可能改变调度，测试负载与 Shell 枚举自然时序也存在波动。因此当前证据确认 Dispatcher 队列延迟曾达到 841.5 ms，但尚未把该等待定位到具体 WndProc/COM 步骤；诊断运行中未再观察到该长尾，不能据此称响应变快。三轮均为隐藏 WPF/ExplorerBrowser 宿主，没有用户输入、可见窗口或真实桌面绘制；本机单一负载也不能代表其它 Shell provider。新选区重试对长尾的影响仍不确定，自动刷新 P0 继续开放。

### 2026-10-26 50,000 项 Dispatcher 长尾与选择回调对齐

启用 `EXPLORER_DIRS_DISPATCHER_QUEUE_LATENCY_TRACE=1`、`EXPLORER_DIRS_MESSAGE_PUMP_TRACE=1` 后再次运行隐藏生产 HostProbe `--live-shell-directory-stress-large 50000`，退出码 0。独立 observer 与产品 watcher 创建/删除均为 `50,000/50,000`；创建和删除用时约 31.0/18.6 秒，目标 pane 最终计数 3，选择/焦点读回正确，fallback 各为 1/2。

本轮 ContextIdle 最大 Dispatcher 空档约 3,555 ms，Input/Normal 最大队列等待 953/800 ms；独立队列探针 267 次 timer callbacks 均完成，未重叠或 post failure。专用线程 heartbeat 最大间隔 40 ms，消息泵最大间隔 623 ms。此证据排除了“STA 与消息泵完全停止”这一解释，但不表示 UI 仍可及时响应。

对齐最大 ContextIdle 空档后发现，空档中累计 Background operation 重叠约 1,039.5 ms，最大单次约 381.1 ms；最长操作为 `ScheduleDeferredRefreshSelectionRestore` Background timer tick。该回调内 deferred readiness 为 `11`，首轮批量 restore 约 135.5 ms 只读回 1/2 项；Replacement fallback 清理约 40.8 ms、第二次批量调用约 14.3 ms仍为 1/2；随后逐项 `SelectItem` 约 67.7 与 86.0 ms，最终恢复 2/2，整个 tick 约 381 ms。全正 readiness 并不能保证批量调用一次完成，这与既有 fresh-view 隐藏 `IShellView` 观察一致。单个回调只覆盖最大空档的一部分，不能解释其余约 2.5 秒队列等待。

另对“空选区时跳过批量 replacement、直接逐项 `SelectItem`”做了失败验证：隔离快照探针中，空选择时 `SelectItem` 返回 `S_OK`，但紧接着的 Shell 读回仍为空；因此撤销该实验分支和关联 gate 测试，避免错误宣称快路径可靠。保留现有 replacement fallback；该回退仍通过 16 种分组/查看方式矩阵、额外选项清理及 stale PIDL 的既有探针。

完整 Release SettingsSmoke、NativeProbe selection batch spike 及 App Release build 均退出码 0；App build 0 警告/错误。追踪日志写入临时目录 `explorer-dirs-current-50k-pump-queue.log`。所有窗口均为隐藏探针，无可见 GUI、桌面输入或 release；高负载长尾的剩余来源、物理按键体验和可见体验仍待验证。

### 2026-10-26 低侵入探针复跑 50,000 项

为确认测量工具是否在制造 Dispatcher 压力，给 `ThreadMessagePumpTraceProbe` 增加按 Dispatcher 回调方法分类的累计完成数、耗时和单次最大值。首轮新增分类揭示 HostProbe 的 20 ms Background heartbeat 回调累计耗时约 16.6 秒；源码核对发现它每 50 ms 同步读取 Shell 选区、焦点及计数。该诊断采样足以明显干扰重目录场景，故将压力探针 heartbeat 调为 100 ms、三项 Shell 状态轮询调为 1 秒；每次状态读取的最大耗时继续记录。

低频版本 HostProbe Release 构建 0 警告/错误，`--message-pump-hook-smoke` 通过。隐藏生产宿主 `--live-shell-directory-stress-large 50000` 退出码 0：observer / 产品 watcher 创建和删除各 `50,000/50,000`；创建/删除用时 36.204/12.709 秒，目标 pane 计数 `50003/3`，刷新 fallback 1/4，创建 settled 1,567 ms；两项选择与独立焦点均恢复。Normal/Input/ContextIdle 最大排队等待约 154/154/349 ms，最大 Dispatcher 观察间隔 323 ms，dispatcher heartbeat 最大间隔 293 ms。诊断 heartbeat 回调累计耗时 701 ms、单次最大 28.5 ms；Selection/Focus/Count 读取峰值分别 15/13 ms。

这轮支持“探针采样频率是此前长间隔的重要混杂因素”，但前轮 3.5 秒及 164 秒空档不能因此从历史证据中删除，也没有解释前轮 observer 缺一个事件的单次失败。当前样本为隐藏本机生产宿主；不证明跨机器稳定性、其它 Shell provider、桌面输入或可见交互。未改生产刷新算法、未启动可见 GUI、未发布；自动刷新 P0 继续开放。

### 2026-10-26 低频采样下关闭独立队列探针复跑 50,000 项

为确认低频状态轮询的效果不依赖独立 `DispatcherQueueLatencyProbe`，保持 `ThreadMessagePumpTraceProbe` 开启并关闭 queue probe，再运行隐藏生产 HostProbe `--live-shell-directory-stress-large 50000`，退出码 0。observer 与产品 watcher 创建/删除均 `50,000/50,000`，observer errors `0/0`；创建/删除用时 28.705/19.108 秒，目标 pane 最终计数 `50003/3`，选区和独立焦点恢复，刷新 fallback 1/4。

最大 ContextIdle 间隔 404 ms，Input/Normal 最大间隔 217/216 ms；dispatcher heartbeat 最大间隔 223 ms、最大工作 20 ms；Shell selection/focus 读取峰值 9/11 ms。该样本与 queue probe 开启的低频轮（ContextIdle 323 ms、queue 最大等待 349 ms）相近，但每种配置只有单轮，不足以判断 queue probe 的因果影响或自然运行分布。

最大空档所在的 31,752–32,157 ms 窗口中，`0xC471` Dispatcher WndProc 与 `QueueDeletedItemNotification` 的 timer tick 各运行约 188 ms；同轮三次 delete-identity scan 最后结果为 `NotFolder`，删除通知总计最大单次 187 ms。当时的追踪只统计整体调用耗时；分段取证及后续限制大视图扫描见下方[2026-10-26 限制大视图删除身份扫描](#2026-10-26-限制大视图删除身份扫描)。该轮仍只能说明时间相关性，不能把约 188 ms 的通知调用认定为 watcher 漏报或全部 404 ms 队列间隔的根因。

本轮没有复现早期长间隔或 observer 漏一个事件；历史异常仍未解释，不能据本轮认定问题已解决。场景为隐藏本机生产宿主，无可见 UI、用户输入或其它 Shell provider；未更改生产刷新算法、未发布。自动刷新 P0 继续开放。

### 2026-10-26 限制大视图删除身份扫描

先为 `ShellDeletedItemNotification.NotifyDeletedFolder` 加入仅在 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 开启的 COM 分段计时，并在隐藏生产 HostProbe 50,000 项场景下复测。计时显示该方法通过 `IFolderView.GetItems(SVGIO_ALLVIEW)` 后按序 `ItemAt`、`GetDisplayNameOf`，直到项目序号 9,874 才匹配删除路径；属性查询返回 `S_FALSE`（result=`NotFolder`）。原来 `delete-notification` 单次最高耗时约 187–262 ms，线性枚举整个视图是主要成本，而不是属性查询本身。分段计时不记录路径或文件名。

设置状态测试 `CanScanViewForDeletedFolder` 的 512、513、unknown 三种边界；先运行看到缺少 API 的预期编译失败，再加入 gate 并接入 `QueueDeletedItemNotification`。视图项数大于 512 或未知时跳过同步身份扫描，记录 `SkippedLargeView`，保留现有目录计数 watcher/fallback；最多 512 项仍可尝试原生 RMDIR 快速通知。Release SettingsSmoke 通过，HostProbe/App Release build 0 警告/错误。

改动后隐藏生产 HostProbe `--live-shell-directory-stress-large 50000` 退出码 0：observer/产品 watcher 创建与删除均 `50,000/50,000`、errors `0/0`；创建/删除 27.735/20.100 秒，最终 pane 计数 `50003/3`，选区/焦点正确，fallback 1/3；身份扫描 `count=0,last-result=SkippedLargeView`，delete notification stage `calls=0`，最大 Dispatcher 间隔 354 ms。对小视图运行 `--live-shell-product-monitor-early-rmdir`，自动通知结果仍为 `Notified`，RMDIR 数 1，fallback 后四窗格均收敛到 3。

初轮大目录压力轮实际删除的是普通文件；随后新增的 50,000 项子文件夹删除定向 E2E 已补上，结果见下一节。该优化避免已测到的全视图扫描堵塞 STA；隐藏样本不证明可见 UI/Q-Dir 交互，未生成 release，自动刷新 P0 仍开放。

### 2026-10-26 50,000 项视图中的子文件夹删除收敛

扩展隐藏生产 HostProbe `--live-shell-directory-stress-large 50000`：在 burst 文件创建/settled 及单文件删除后、批量删除前，直接在监控目录创建一个空子文件夹，等待该项进入四窗格 Shell 视图，然后直接删除目录。测试记录 product watcher create/delete 事件、视图数量、scan counter、refresh fallback 和 monitor settled 状态。

Release HostProbe 构建 0 警告/错误，运行退出码 0。创建/删除子文件夹 watcher 事件均观察到；删除时 Shell 视图大于 512 项，记录 `SkippedLargeView`，身份扫描计数不增加；目录计数 fallback 增加 1，约 366 ms 后视图回到删除前计数且 monitor settled。之后 50,000 文件 bulk delete 也通过；四 pane 最终计数 `50003/3`，两项选择和独立焦点恢复。小视图 `--live-shell-product-monitor-early-rmdir` 仍在早前独立样本中证明原生 RMDIR 路径可用。

本轮 product watcher 事件总数为 create/delete `50,001/50,001`（50,000 个 burst 文件加测试子文件夹）；独立 burst observer 报告 create `49,999/50,000`、delete `50,000/50,000`、errors 0，仍有一个普通文件创建事件未被独立 observer 捕获，产品 watcher 未丢事件。Dispatcher 最大 ContextIdle 间隔 1,087 ms、Input/Normal 602/457 ms；最大空档未被归因，单轮结果不证明是新文件夹周期造成。该隐藏 GUI 无输入、非可见用户验收，无 release；自动刷新 P0 继续开放。

随后同参数再次复跑，退出码 0；独立 burst observer create/delete 均为 `50,000/50,000`、errors `0/0`，产品 watcher 含测试子文件夹为 `50,001/50,001`。上一轮少一个普通文件创建事件未复现，仍作为单轮异常保留，不将其归因于产品 watcher。此次子文件夹删除的计数 fallback 增加 1、身份扫描增量为 0、`SkippedLargeView` 断言成立，约 942 ms 后视图和 monitor settled；最终四 pane `50003/3`，选区/焦点恢复。ContextIdle/Input/Normal 最大间隔为 433/215/215 ms。重复结果支持目标路径可收敛，但仍是本机隐藏宿主的有限样本，不证明可见体验、其它 Shell provider 或跨机稳定性。

### 2026-10-26 RMDIR 扫描门槛的 512 / 513 项产品视图对照

扩展隐藏 HostProbe `--live-shell-product-monitor-rmdir-boundary 512|513`，在 pane 0 预先填充文件，并在删除前创建一个子文件夹，使待删项仍处于 Shell 视图时计数分别为 512、513；其余 pane 保持 3 项。Release HostProbe 构建 0 警告/错误。

512 项样本中 watcher 于约 124 ms 到达，删除后 Shell 仍保留 stale folder；产品查询一次并记录 `Notified`。视图在前 250 ms 未收敛，随后 count fallback 增加 1，最终 pane 0 回到 511 项，探针退出码 0。该结果确认上限本身仍允许小视图快速 RMDIR 路径，但它没有消除 fallback。再次复跑时 watcher 约 108 ms 到达，而 Shell 在约 142 ms 已自然移除该项目；产品没有开始身份扫描，fallback 0，probe 也退出码 0。

513 项样本中 watcher 约 126–141 ms 到达，但 Shell 在约 157–171 ms 已自行移除文件夹，早于产品身份扫描的 250 ms 延迟；结果为 `not-attempted`、RMDIR 0 次、fallback 0 次，最终计数 512，探针退出码 0。这是 Shell 自然更新先收敛，因此不构成生产 gate 执行并跳过扫描的证据；513 分支仍由 SettingsSmoke 纯状态边界测试覆盖，大视图实际跳过另由 50,000 项产品宿主场景验证。实际时序显示 512/513 两侧都可能自然收敛而跳过身份查询，故该 HostProbe 断言允许自然收敛路径或确实触发的 gate 路径。所有样本均为隐藏本机 ExplorerBrowser，无可见 UI、用户输入、发布或跨 provider 证据。

### 2026-10-26 配对追踪 50,000 项产品监视与 native-only

在同一 HostProbe Release 和 trace 环境中顺序运行 `--live-shell-directory-stress-large 50000` 与 `--live-shell-directory-stress-native 50000`。两轮 observer 创建/删除均为 `50,000/50,000`；产品轮 watcher 为 `50,001/50,001`（含定向子文件夹），native-only 产品 watcher 为 `0/0`。两轮 Shell/磁盘最终均为 `3/3`。产品轮 selection/focus restore 均通过；native-only 按模式不执行产品选择恢复，不以 `False/False` 作为失败证据。

产品轮创建/删除约 30.644/14.989 秒，最大 ContextIdle/Input/Normal 间隔 301/220/220 ms；独立队列中 ContextIdle 最大等待 152.5 ms、Input 142.7 ms、Normal 142.4 ms。该最大 ContextIdle 窗口为 42,358.3–42,659.8 ms，窗口中记录到约 73.4 ms Background operation 重叠（最大单项 61.7 ms），以及 `SHELLDLL_DefView` `0x4B0` 调用 42,497.0–42,649.5 ms、约 152.5 ms；二者解释该窗口的重要部分，但不能说明是唯一来源。heartbeat 最大间隔 30 ms，消息泵仍在推进。此轮定向子文件夹删除 identity scan 0 次、`SkippedLargeView`，fallback 增加 1，约 934 ms settled。

紧接的 native-only 控制创建/删除约 32.323/19.455 秒，最大 ContextIdle/Input/Normal 间隔 223/223/238 ms，队列延迟最高约 68.8 ms；ContextIdle 最大窗口内 Background operation 重叠仅约 0.1 ms，heartbeat 最大间隔 50 ms。慢消息 trace 未记录 `0x4B0` 调用，所见最慢 DefView 消息约 17.5 ms。消息泵与 Dispatcher 仍出现约 200 ms 级间隔，说明这类间隔并非只在产品 watcher 模式中出现；产品轮的较高队列等待值得继续复核。

这是固定先产品后 native-only 的单轮配对，产品轮另有子文件夹删除周期，采样工具也会影响调度；不构成因果对照或稳定分布，不能仅凭它更改 Dispatcher 优先级、刷新计时或线程模型。两轮均为隐藏本机 ExplorerBrowser，没有可见 UI/用户输入或其它 Shell provider。完整日志：`%TEMP%\explorer-dirs-p0-50k-aligned-trace-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-native-aligned-trace-20261026.log`。

### 2026-10-26 反向顺序大目录轮出现高追踪负载长尾

按 native-only → product 反向顺序运行 50,000 项压力场景。native-only 轮创建文件后 15 秒窗口内 pane 视图始终为 3 项，磁盘达到 50,003 项，产品 watcher / fallback / native create notification 均为 0；独立 observer 收到 `49,999/50,000` 个创建、`50,000/50,000` 个删除，无 error。随后测试继续删除磁盘文件，最终 Shell/磁盘计数 `3/3`，但 `create-visible=False`，进程退出码 1。该轮没达到 burst 已加载至 Shell view 的前提，所以无法作为与 product 同负载的性能对照；额外文件夹周期在本轮被探针记录为不可见。HostProbe 现仅当 50k burst 和前置单文件删除都已确认视图收敛时才执行该周期；否则显式输出跳过，避免等待不相关的 15 秒。

随后 product 轮中 observer 与产品 watcher 创建/删除各完整收敛：`50,000/50,000` 与 `50,001/50,001`，errors `0/0`。folder create/delete 自然进入 Shell 视图，分别约 1,467/1,558 ms，完整周期 3,026 ms；删除身份 scan 0 次、`not-attempted`、folder fallback delta 0。压力场景整体仍退出码 1：写入 50,000 文件耗时约 66.2 秒，超过探针 60 秒 writer completion gate；批量删除约 276.3 秒，创建/删除可见 gate 未通过，尽管最终磁盘和 Shell 计数都为 3 项，选区/焦点最终读回一致。

product 轮启用了 `DispatcherQueueLatencyProbe`、message-pump trace 和 Shell callback trace。ContextIdle/Input/Normal 最大 dispatcher 间隔约 40.1/241.4/216.4 秒；独立队列最大等待约 368.2/241.3/217.6 秒，队列 probe `drained=False`；单独专用线程 heartbeat 最大间隔 89 ms。消息泵汇总的最大空档约 215.7 秒，Shell `0x4B0` 最大单次约 763.8 ms，Shell state callback 最大约 63.3 ms，最大刷新调用约 1.86 秒；这些单项不能解释该消息泵空档，根因仍未知。此轮 CPU/调度和探针追踪负载均异于此前轮次，不能把上述墙钟当成一般用户响应或直接归因为产品 watcher。

两个日志：`%TEMP%\explorer-dirs-p0-50k-native-first-equivalent-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-product-after-native-20261026.log`。没有可见 GUI、用户输入或发布。后续先关闭独立 Dispatcher queue probe，用低扰动产品压力轮确认高延迟和收敛 gate，再按慢点补齐 STA 阶段证据；当前不改刷新策略。

### 2026-10-26：无追踪压力轮与追踪门槛核对

完整关闭 Dispatcher queue、message-pump、Shell callback trace 后，50,000 项 product 轮创建/删除耗时 29.923/20.338 秒；Shell 创建/删除可见、product watcher `50,001/50,001`、独立 observer `50,000/50,000`、errors `0/0`、最终四窗格 `50003/3`、选区与焦点恢复均通过。随后删除测试子文件夹时跳过大视图同步身份扫描（`SkippedLargeView`，scan count 0），由一次 fallback 收敛。该轮日志仍有多秒级间隔，不过此前启用 message-pump 与 Shell trace 的轮次出现约 117 秒空档；目前只能判断追踪负载可能显著影响结果，单轮不能证明因果或稳定性能分布。日志：`%TEMP%\explorer-dirs-p0-50k-product-untraced-20261026.log`。

核对无追踪诊断门时发现 HostProbe 仍无条件要求 selection restore trace 中出现 `focused-item-lookup=direct-select`，即使 Shell callback trace 明确关闭。该标记只用于诊断，不是产品行为断言；现在仅在 trace 启用时检查，实际选区/焦点读回、创建删除可见和 watcher 收敛门仍保留。Release HostProbe build 0 警告/错误。

修正后的 10,000 项无追踪复测仍以退出码 1 结束，但原因是实际选区断言 `selection-restored=True/False`：批量删除后选区为空；焦点保持，Shell 创建/删除可见、事件数、计数收敛均通过。此前 50,000 项无追踪轮同一选区断言为 `True/True`，因此这是间歇性产品行为或探针状态波动，尚未归因，不能通过放宽门槛消除。该次 10,000 项日志为 `%TEMP%\explorer-dirs-p0-10k-untraced-gate-20261026.log`。自动刷新 P0 继续开放；后续应针对批量删除后的 selection snapshot / refresh restore 时序继续取证，再决定是否修产品代码。

同一配置立即重复一轮，10,000 项创建/删除可见、selection/focus 均 `True/True`、product watcher `10,001/10,001`、observer `10,000/10,000`，退出码 0（日志 `%TEMP%\explorer-dirs-p0-10k-untraced-repeat-20261026.log`）。这确认失败不是每次稳定复现；根因仍未知，继续保留 selection 行为门槛。

### 2026-10-26：空选区回调期间保留已确认选择快照

失败与成功的 10k 日志都显示 Shell 刷新会发出 `empty-restore`，随后很快出现从缓存计数 0 替换为 1 项的回调。原逻辑在排队恢复前立即从 `_lastNativeSelection` 移走并交给异步回调释放完整快照；在输入代次未变化时，后续部分选择事件因此可能从空状态开始。现改为只要视图输入代次仍一致，就保留已确认快照，并把副本交给异步恢复；真实用户输入仍会通过输入代次变化/`NoteNativeUserInput` 清除旧快照。SettingsSmoke 新增相同输入代次保留、输入变化不保留两例；先确认缺少策略时编译失败，再通过。App/HostProbe Release build 0 警告/错误。

修复后两轮 `--live-shell-directory-stress-large 10000` 的选区与焦点均为 `True/True`，创建/删除可见且 watcher/observer 事件数完整；但两轮都因另一项既有 `batchDeleteVisible` 门返回 1。日志中的 128 文件删除后，10 秒内 pane 未达到该批次目标数（`batch-visible=False`）；稍后的批量清空 fallback 又将 pane 收敛到 3 项。该问题与 selection/focus 断言分开记录，尚未归因。两轮日志：`%TEMP%\explorer-dirs-p0-10k-retain-empty-snapshot-20261026.log`、`%TEMP%\explorer-dirs-p0-10k-retain-empty-snapshot-repeat-20261026.log`。目前样本支持保留快照的方向但数量有限；还需重复和对比 callback/refresh 阶段后，才判断间歇选区问题是否解决。

### 2026-10-26：保留 watcher 目标并重试瞬时空视图后的刷新

失败轮的 128 项删除阶段已收到 `pending=-128`、期望计数 `9874`，但当前 Shell 仍报告 `10002`。状态机的瞬时空视图恢复分支等待计数稳定后把 `10002` 重新设为基线，清除了这批删除目标，10 秒 gate 因此未收敛。现在首次出现“稳定非空但仍旧计数不符”时，保留原目标并安排一次有界刷新重试；若第二轮仍未收敛，回到既有稳定计数兜底，避免无界刷新。状态机 smoke 更新后先因新期望失败，再通过。

该额外刷新曾让 `batch-visible=True`，并暴露删除子文件夹扫描可能在 Shell 计数短暂为 0 时误判为小视图。扫描现在要求当前读数和 watcher 目标数都在 1–512 之间；未知、零项和逻辑大视图都跳过。SettingsSmoke 覆盖边界与“实际为 0、逻辑目标为 10,003”的情况。

集成验证：SettingsSmoke 全量通过；App/HostProbe Release build 0 警告、0 错误。无追踪 product 场景 10,000 和 50,000 项均退出码 0；128 项删除门、创建/删除可见、选区/焦点恢复及 watcher/observer 事件完整。两轮大视图子文件夹删除均显示 `identity-scan-skipped=True`、scan count 0。50k 创建/删除 31.775/20.462 秒，最终四窗格 `50003/3`、磁盘 3。日志：`%TEMP%\explorer-dirs-p0-10k-targeted-fixes-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-targeted-fixes-20261026.log`。

这些是隐藏本机 ExplorerBrowser 压测，无可见桌面、真实用户输入、其它 Shell provider 或 release 验收。自动刷新 P0 的该组大目录/删除恢复门通过；不能据此宣告整体 P0 或产品路线图完成。

### 2026-10-26：分离 50,000 项长间隔的 trace 配置影响

采用相同 product stress，分别关闭、单开或组合开启 Shell callback trace / timing 与 message-pump trace；独立 Dispatcher queue probe 全程关闭。10,000 项下 message-pump-only 的 Dispatcher/消息泵最大间隔为 144/64.7 ms；Shell callback-only 为 126 ms；两者组合为 150/64.4 ms。产品 refresh、选区/焦点和批次删除均通过。message-pump-only 样本的独立诊断 `FileSystemWatcher` 创建事件为 `9,999/10,000` 且 error 0，但产品 watcher `10,001/10,001`；其余同组样本 observer 完整。这个独立测试 watcher 计数不作为产品通知失败证据。

50,000 项下 Shell callback-only Dispatcher 最大间隔 432 ms，message-pump-only Dispatcher/消息泵最大间隔 255/192.2 ms；各自 watcher/observer 及视图门通过。双 trace 50k 轮创建/删除约 35.920/22.943 秒，Dispatcher 最大 ContextIdle/Input/Normal 间隔 1,304/757/619 ms，消息泵最大间隔 217.3 ms、专用心跳 38 ms；产品和 observer 事件完整，选区/焦点正确，退出码 0。此前双 trace 低扰动 50k 轮出现约 117 秒 Dispatcher/消息泵空档，本轮没有复现。当前结果说明 trace 组合在 50k 有秒级以内的调度影响，但不能解释旧长尾，也不能凭单轮推断稳定分布或因果。

双 trace 10k 首次退出码 1 是 HostProbe 对文件夹删除路径的过严判定：Shell 已自然移除条目并使计数收敛，身份查询数为 0；探针只接受显式 `SkippedLargeView` 且要求额外 fallback。现在 product 门接受“视图监控已 settled 且身份查询数为 0”，同时将 `identity-result` 输出出来；Native-only 保持其无 fallback 断言。复测双 trace 10k 退出码 0，日志 `explorer-dirs-p0-10k-combined-trace-gate-20261026.log`。这只调整探针成功语义，不改变产品刷新策略。

全部本轮日志：`%TEMP%\explorer-dirs-p0-10k-message-pump-only-20261026.log`、`%TEMP%\explorer-dirs-p0-10k-shell-callback-only-20261026.log`、`%TEMP%\explorer-dirs-p0-10k-combined-trace-20261026.log`、`%TEMP%\explorer-dirs-p0-10k-combined-trace-gate-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-shell-callback-only-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-message-pump-only-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-combined-trace-20261026.log`。无可见桌面和用户输入，未发布；自动刷新 P0 仍开放。

同配置第二轮 50k 双 trace 也退出码 0：创建/删除 34.973/24.733 秒，Dispatcher 最大 ContextIdle/Input/Normal 间隔 336/336/198 ms，消息泵最大空档 169.2 ms、心跳 52 ms，所有文件、选区和焦点门均通过。最大 ContextIdle 窗口为 38,310.8–38,646.9 ms，其中 `SHELLDLL_DefView 0x4B0` 从 38,499.2–38,637.4 ms 执行 138.2 ms；同窗记录的 Dispatcher operation overlap 为 9.3 ms。该 Shell 调用能解释约 138 ms，不能解释窗口剩余间隔。旧约 117 秒长尾在两次新双 trace 50k 运行中都没有重现，当前将其保留为历史单次异常并转向路线图其它未完成验收，不据此更改生产线程/刷新策略。重复轮日志 `%TEMP%\explorer-dirs-p0-50k-combined-trace-repeat-20261026.log`。


### 2026-10-26：10,000 项计数与刷新调用同钟对齐

为复核短暂计数变化，HostProbe 记录 `IExplorerBrowser.Refresh()` 调用、fallback 回调和计数读取的 `Stopwatch` ticks。Release HostProbe 构建通过；隐藏生产宿主 `--live-shell-directory-stress-large 10000` 退出码 0，日志为 `src/.state/directory-stress-10000-ticks-20261026-090203.log`。

创建阶段 count sample 在 9.437 s 为 6,566 项；fallback 1 的 `Refresh()` 调用为 10.250–10.266 s，回调于 10.326 s 完成，10.542 s 的独立计数采样为 10,003。删除阶段 fallback 2 的 `Refresh()` 为 11.186–11.196 s，11.200 s 完成回调读到 0 项，但 11.635 s 的独立采样已回到 10,002。稍后 watcher 目标降至 3：15.872 s 仍读到 9,874 项，22.106 s 读到 874 项，23.110 s 到 3 项。fallback 3 的 `Refresh()` 为 14.171–14.200 s，14.308 s 回调读到 10,002 项。

同钟证据显示，至少一次零计数发生在 `Refresh()` 完成回调内，随后独立采样已恢复；批量删除后的长尾表现为 Shell view 逐步减少到目标值。它支持视图重建/枚举期间计数暂态的解释，但不能判断可见窗口是否空白、是否只是回调读取过早，或是否有更深层 Shell provider 时序问题。未改生产刷新策略；该样本为单轮隐藏 ExplorerBrowser，不含可见 GUI/Q-Dir 或发布验收，自动刷新 P0 仍需后续可见验证。

### 2026-10-26：监视器安静后复核存活选区

使用加入同钟时间戳的 10,000 项场景复跑两次：第一轮退出码 0，第二轮退出码 1。失败轮创建/删除视图计数最终收敛，焦点保持 `initial.txt`，但原有 `second.txt,third.txt` 选区最终为空。时间线显示刷新恢复曾成功、选区也至少两次重建；约 18.2 秒采样时选区再次为空，而记录到的最后一次 Shell selection callback / watcher 驱动恢复发生在约 16.0 秒。该轮未观察到清空后的新选择回调，因此原有“收到空选择回调时补选”的恢复路径不能覆盖这次延迟清空。失败日志：`src/.state/directory-stress-10000-repeat-2-20261026-090504.log`；对照成功轮：`src/.state/directory-stress-10000-repeat-1-20261026-090435.log`。

先新增状态门 smoke 并确认它因缺少自然更新复核策略而失败；随后产品在自然目录更新后启动最多 3 秒、每 250 ms 一次的选区检查。仅当保存的选择非空、当前 Shell 选择数为 0、窗格仍在同一导航位置且用户输入版本未变时，才调用既有 PIDL 身份恢复；已有非空选择不被反复覆盖，用户输入、导航或监视器停止会取消检查。完整 Release `SettingsSmoke`、App/HostProbe Release build（均 0 警告/错误）通过。修复后相同 10,000 项隐藏生产宿主连续三轮退出码 0，三轮视图计数、删除可见、`second.txt,third.txt` 选区与 `initial.txt` 焦点均恢复；其中两轮的日志观察到延迟验证路径实际执行，第三轮 Shell 自然保留了选区。

本修复针对隐藏本机 filesystem ExplorerBrowser 中的延迟空选区；未完成可见多窗格/键盘验收，也未验证网络、虚拟 Shell 或第三方 provider。它不关闭自动刷新 P0，也不代表产品整体验收或新 release。

### 2026-10-26：最新选区复核策略的 50,000 项复跑

为把“自然更新后最多 3 秒的选区复核”从 10,000 项扩到 50,000 项，顺序运行两轮隐藏 Release HostProbe `--live-shell-directory-stress-large 50000`，设置 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=0`。两轮退出码均为 0；产品 watcher 创建与删除事件均为 `50,001/50,001`，创建后、128 项批量删除后及最终清空均可见并到达预期计数；最终保留 `second.txt`、`third.txt` 两项选择及 `initial.txt` 独立焦点。第二轮独立 observer 创建/删除也为 `50,000/50,000`；第一轮 observer 创建少一项 `49,999/50,000`、错误数为 0，但产品 watcher 完整。两轮均发生删除阶段 fallback，数量分别为 2 与 4；这证明最终收敛，不证明每次 watcher 变更都无需 fallback。

第二轮完整原始 stdout 保存在 `src/.state/directory-stress-50000-selection-recheck-20261026-092411.log`。第一轮的工具回传 stdout 被截断，没有另存原始日志；可用证据限于当次返回的退出码和摘要/部分 timeline。第一轮 Dispatcher ContextIdle/Input/Normal 最大间隔为 1,493/1,061/841 ms、专用线程 heartbeat 最大间隔 1,450 ms；第二轮分别为 222/214/214 ms 与 214 ms。间隔波动显著，不能据此声称 50k 响应稳定或归因单一原因。第二轮在若干刷新时刻出现 `empty-restore`，随后 selection timeline 恢复到两项，说明最终态通过并不意味着过程中完全没有短暂选区变化。

两轮是隐藏本机 ExplorerBrowser 产品宿主与临时文件夹，不创建可见产品窗口、不注入桌面鼠标/键盘；不能替代可见多窗格验收，也不覆盖网络/虚拟 Shell、第三方 provider 或其它机器。此结果补上最新恢复逻辑的大目录覆盖，自动刷新 P0 仍开放。

### 2026-10-26：注册失败与 ExplorerBrowser 宿主数量的隔离

新增 HostProbe `--shell-change-registration-once`，只创建隐藏 HWND、注册并注销一次。无 ExplorerBrowser 的此模式按 1 秒间隔独立运行 8 次，8/8 成功，前台窗口未变化。相同进程快速重复 40 次的历史生命周期对照只有 11/40 次注册成功（11 次成功注销），而既往相同 native-only 测试也有过 40/40、36/40；因此快速循环是一个与单次低频路径不同的条件，但现有数据不能证明是它导致注册失败。

另一个隐藏 HostProbe 在四个生产 ExplorerBrowser 视图就绪后，先对包含四个窗格目录的根路径注册/注销，再对根目录下新建的空目录注册/注销。立即连续注册的初测中 3/4 轮完全成功；一轮第一笔成功、第二笔失败。两笔之间增加 150 ms WPF STA 消息泵等待后，8 轮中 4 轮两次都成功、2 轮两次都失败；另外 2 轮在宿主就绪阶段发现前台窗口被外部改变，按保护逻辑停止，未进入注册测试。等待不能稳定修复问题，路径内容也没有表现出稳定关联。原始日志：`src/.state/registration-once-low-frequency-20261026.log`、`src/.state/registration-after-hosts-paired-targets-20261026.log`、`src/.state/registration-after-hosts-pumped-pairs-20261026.log`、`src/.state/registration-after-hosts-low-frequency-20261026.log`、`src/.state/registration-after-hosts-empty-root-20261026.log`、`src/.state/registration-after-hosts-repeat-20261026.log`、`src/.state/shell-change-registration-stress-20261026.log`。

测试只在隐藏窗口上观察注册/注销，没有执行文件操作。由于后续轮次检测到外部前台窗口切换，停止了更多 ExplorerBrowser 宿主探测。HostProbe Release 构建通过，0 警告、0 错误；没有改产品注册参数、重试、延时或 watcher 策略。`last-error=0/8` 都不足以定位无效参数或资源条件；根因仍开放。

### 2026-10-26 再验 128 项批量删除与大视图身份扫描门控

在上节状态机修正及身份扫描门控之后，当前 Release HostProbe 连续两次运行 `--live-shell-directory-stress-large 10000`。两轮均退出码 0；独立 observer 对 burst 文件创建/删除均为 `10,000/10,000`，产品 watcher 含额外测试子文件夹事件为 `10,001/10,001`。128 项批量删除目标 `burst-0127.txt` 在 Shell 视图仍有 10,002 项时被确认是文件，单次查询分别耗时 4 ms；两轮 `batch-visible=True`。大视图目录删除扫描计数均为 0、结果 `SkippedLargeView`，由计数协调器完成收敛。

两轮最终目标窗格计数均为 3，四窗格计数为 `10003/3`；`second.txt`、`third.txt` 选区和未选中的 `initial.txt` 焦点在创建、单项删除及批量删除后保持。批量删除收敛分别使用 2 次与 1 次 fallback；Dispatcher 最大间隔也有波动（ContextIdle 108/128 ms，Input 83/85 ms，Normal 77/85 ms）。这加强了 128 项删除状态机修复在本机隐藏生产宿主上的重复证据，但不证明可见交互、慢速/网络位置、其它 Shell provider 或跨机器稳定性；自动刷新 P0 仍开放，无 release。

### 2026-10-28 HostProbe 刷新序列误报修正

本轮 `HostProbe --live-updates` 第一次运行中，真实产品 `ExplorerBrowserHost` 的四窗格创建已读到 `2,2,2,2`，随后探针在取项目名时抛出“2 items exceeds the probe limit 0”。根因是 `hosts.Select(Names)` 绑定到了 LINQ 的带索引 `Select` 重载，pane 序号被当作 `Names` 的 `maximumItems` 参数；不是 Shell 列表项目上限或产品刷新失败。创建/删除两处均改为显式单参数 lambda。

修正后同一隐藏生产宿主序列连续 4 次退出码 0，每轮四窗格均从 `1,1,1,1` 收敛至创建后的 `2,2,2,2`，删除夹具文件后回到 `1,1,1,1`；创建收敛约 0.87–1.03 秒，删除约 0.45–0.59 秒。完整 Release SettingsSmoke 也通过。没有显示 MainWindow 或发送用户输入；这是隐藏 HwndSource 中的真实 ExplorerBrowserHost 与本地临时目录证据，不是可见刷新体验验收。

另一次 `NativeProbe --live-updates-wpf` 最小四窗格对照仍观察到未收敛计数（无站点控制通过，生产站点创建 `1,2,2,2`、删除 `0,1,1,1`）；这条不同的最小宿主路径不能覆盖或推翻 HostProbe 结果，也不能据此给产品刷新逻辑加补丁。它继续作为独立调查项，需先隔离隐藏 DefView/消息泵差异。
