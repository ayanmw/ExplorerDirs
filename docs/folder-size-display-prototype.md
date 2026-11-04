# 文件夹大小主动显示实现与后台验证

2026-11-04。本记录区分单元格显示、真实 Shell 通知、产品接入和可见验收；当前不是 temp.86 完成声明。

## 原因与实现

标准大小列身份统一后，普通文件 F5 丢失大小的问题已由用户确认修复。但真实视图没有为文件夹请求大小属性，后台扫描器因而没有被触发。Q-Dir 静态调查提供了主动处理显示文字的方向，见 qdir-folder-size-investigation.md。

独立实现通过 `EXPLORERDIRS_FOLDER_SIZE_DISPLAY` 编译，配合 `EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS`：

- 在真实 DefView 的 `LVN_GETDISPINFOW` 默认处理后，补充普通目录的大小文字。文件、重解析点及其他列保持原处理。
- 通过 `IColumnManager` 的可见属性键识别标准 `PKEY_Size`，不按列标题语言或固定列位置识别。
- 通过 `IFolderView.Item` 确认当前行的 provider PIDL，复用既有后台数据源和缓存；显示回调不执行同步递归扫描。
- 只写入通知调用者原先提供的有界缓冲区，不覆盖 Shell 默认处理可能替换的内部字符串。
- 跟踪最多 256 个近期请求的行。完成通知通过窗口消息回到视图线程，重新核对行身份，只重绘文字已变化的行；不以每个结果触发全视图刷新或重排。
- 完成消息携带实例 cookie，避免 HWND 被复用后旧消息作用于新实例；销毁时清空原子窗口指针及行记录。

2026-11-04 后续通知及标准列排序修正已接入产品与诊断构建；旧实验宏改为上述功能宏。尚未覆盖 releases，仍需集中可见验收，不声明 temp.86 全部目标完成。

## 后台真实控件证据

`ProductSizeDisplayProbe.cs` 使用产品的 `ProfileShellNamespaceOwner`、真实 provider PIDL 和真实 Windows Shell 控件。提供直接 IShellView 和 ExplorerBrowser 两条入口，窗口仅在从不切换到用户屏幕的私有 Desktop 中存在。读取的是实际列表单元格，不是模拟返回值。

已通过的功能子检查：

| 检查 | 实际结果 |
| --- | --- |
| 初始显示 | 普通文件 2 KB，文件夹 12 KB、25 KB，空文件夹 0 B |
| 四视图共享 profile | 同名目录分别显示 12/31/32/33 KB，修改第一处不影响其余视图 |
| 失效后补值 | 文件夹由 12 KB 更新为 40 KB |
| 选择与焦点 | 补值后原普通文件仍为选中且聚焦 |
| F5 | 文件和文件夹大小保持正确 |
| 列重排 | 大小移到第三列后仍显示正确 |
| 数值排序 | 25/40 KB 升序与降序结果均正确 |
| 关闭与重新开启 | 关闭时目录留空且文件 2 KB 保留；开启后恢复目录数值 |
| ExplorerBrowser 销毁 | 四个容器 Destroy 返回成功，所跟踪的列表 HWND 无存活 |
| 自动软刷新 | 实际新增、重命名、删除和目录更新正常，普通文件选区及焦点保留 |
| 完整枚举增量 | 诊断构建自动更新阶段为 0 |

早期扩展四视图运行有桌面输入/前台变化，不记整轮通过。后续 `product-size-display-enum-qualified.log` 中，诊断构建的全部功能、0 次自动完整枚举、4 个容器清理及前台/输入不变保护共同通过，exit 0。不得把这次结果追溯应用到旧的失败运行。

## 通知断点修正

此前新增外部文件后，物理 CREATE 通知未让列表从 4 行变成 5 行。单变量对照显示：直接浏览 profile 根可更新，带目录编码的根不更新；`ILIsParent` 的直接/递归祖先检查均通过，排除了基本 PIDL 祖先关系失效。

让 DefView 订阅完整的当前目录 PIDL，而非整个 profile CLSID 祖先后，真实列表的新增、重命名、删除通过。旧消息接收器证明的是 profile 级事件交付，不能替代当前目录 DefView 的条目应用。通知桥测试也改为检查真实的当前目录范围。

独立消息窗口直接订阅 encoded 目录仍未收到信号（递归和非递归对照均如此），不能将它与实际 DefView 等同。六事件测试保留原先 profile 级广域接收器作为传输对照，同时单独断言 callback 返回当前目录；真实条目应用由上述 DefView 探针证明。`product-directory-notification-six-events.log` 中六种转发、重复绑定与销毁整项 exit 0。此调整没有以广域观察器取代原生列表断言，也没有声明解决独立接收器的目录订阅差异。

对照设置 `SFVM_QUERYFSNOTIFY` 的递归监视没有改善，已撤回；该接口在当前系统会被调用，但文档列出的支持范围有限，不能将其作为已验证解决办法：
https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-queryfsnotify

生命周期审查：产品的视图引用为借用，HWND 只拥有一份 callback 引用，销毁时取消扫描并移除窗口订阅；不再占用旧的 32 槽强引用表。旧延期完整刷新仅从 probe 专用入口调用；旧子项转目录通知的调度分支现在仅在 NATIVEPROBE 编译，产品不进入依赖旧表的定时路径。产品 DLL 不导出这些 probe 控制入口。

## 实际应用宿主与保存排序

复用 `ProfileOwnerMultiRootShellHostProbe` 在私有 Desktop 创建四个真实产品 WPF `ExplorerBrowserHost`。修正探针未调用 `ConfigureFolderSizeLocations` 的遗漏，和 PaneControl 一样传入 profile CLSID，并断言 chrome 的逻辑物理目录。

该测试先在第一窗格等待保存排序超时。源码显示：旧恢复代码仅对 GUID 标题的未解析列运行，而标准 `PKEY_Size` 有正常标题。因此 provider 的异步目录值到齐后没有触发初始排序恢复。现在仅对 provider 的标准大小列允许命名列进入既有分批就绪检查；普通文件系统视图保持原条件。

修正后四宿主功能子检查通过：实际显示 5/29 B、31/7 B、11/37 B、41/13 B（各自升/降序），列配置、独立选区和焦点均正确。该轮检测到用户输入/前台变化，**整体隔离保护未通过**，不能与上面 exit 0 的原生四视图探针混淆。日志 `src/.state/product-four-pane-host-saved-sort-fix.log`。前两轮超时记录及一份 minidump 留在 src/.state，后续定位为保存排序等待，不据此声称产品死锁。

早期选区检查仅向 ListView 写选中标志，原生 CREATE 后该不完整状态被清理；改为通过 Shell SelectItem 建立并等待选区后检查通过，未为此给产品新增强制恢复代码。

## 复现与证据

构建：

```powershell
pwsh -NoProfile -File src/ExplorerDirs.NativeProbe/build-notification-bridge-experiment.ps1 -TraceSizeRequests -DisplayText
```

真实四视图资格检查：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --product-size-display-browser-private-desktop src/ExplorerDirs.NativeProbe/bin/native-notification-bridge-experiment-x64-trace-display-text/ExplorerDirs.ShellNamespace.dll
```

旧失败日志 `src/.state/product-size-display-browser-qualification.log` 保留。新通过日志为 `src/.state/product-size-display-enum-qualified.log`。DLL 旁的 `.size-trace.log` 为有界进程日志，记录订阅、物理事件、provider 转发、枚举和单元格补值。

整合当前目录订阅、产品构建及标准列保存排序恢复后，25 项后台回归全部通过，最终报告为 `src/.state/background-checks/20261104-114924-d486be03beaf46148855d0f34fc90b57/summary.json`。其中 App、SettingsSmoke、HostProbe、NativeProbe 构建及数据源、Everything 协议、会话/列配置、异步扫描等检查与真实列表探针分别记录，不能替代可见验收。

## 后续批次：分组、筛选、真实 Everything 与 4,100 目录

2026-11-04 延续验证，本批没有增加产品功能入口或修改 Everything 设置。

- `product-four-pane-host-group-filter.log`：实际四个 WPF 产品宿主整项 exit 0，前台/输入保护通过。在第一个窗格按标准大小列分组，应用包含目录的名称筛选后，唯一可见目录仍显示 5 B；清除筛选后，5/29 B、排序及大小分组恢复。随后四窗格独立选择与焦点检查通过。
- `product-size-display-grouping-qualified.log`：原生四视图启用/取消大小分组后单元格保持正确，自动更新完整枚举增量仍为 0，整项隔离保护通过。
- `product-four-pane-host-1025-integrated.log`：复现原先四根各 1,025 目录、8,192 缓存容量、通知身份观察开启的负载，4,100 个值及四窗格各自保存的升降序、选区、焦点全部通过，整项隔离保护通过。缓存 4,100 条、载荷 1,508,800 B、0 淘汰；这只是摘要载荷，不代表应用总内存。
- 大目录就绪仍有明显等待：各窗格值就绪等待约 7.8/8.2/11.1/0.9 秒，之后排序等待约 1.3/1.3/29.3/9.3 秒。探针按窗格顺序观察、宿主同时工作，不能把这组数字视为四个独立冷启动耗时或直接归因扫描。正确性通过，性能尚未达标；下一步比较诊断观察开销与产品构建、采集排序/值查询成本。
- 诊断记录修正：`GetSyntheticOnDemandSizeScanCount` 等仅由 NATIVEPROBE 宏递增，产品诊断 DLL 的零值不代表没有磁盘扫描；探针不再将其打印为实测 0，而标记为不可用。
- `everything-live-empty-fixture.log`：真实 Everything 只读查询返回空目录 0 B、两个目录 5/37 B，约 867 ms，前台保持不变。没有打开搜索窗口或改索引配置。
- `product-size-display-everything-indexed.log`：Everything 专用来源经真实 native bridge 和四个原生视图，显示、40 KB 更新、F5、排序、来源开关、分组、自动更新与 0 次额外完整枚举子检查通过。该轮检测到用户输入变化，整项保护不记通过。

Everything 新增夹具的索引有延迟：第一次未等待索引就绪的原生检查中，非空目录有值而新空目录显示“—”；随后直接 SDK 协议查询确认可以得到真实 0。专用来源的集成检查现在明确等待夹具索引就绪，再验证消费结果和显式失效后的补值。它不证明尚未索引的目录会自动从未知变为已知，也不以后台扫描伪造专用来源结果；应用已有 F5 清理大小缓存接线，可见恢复行为仍留集中验收。

新增大目录入口：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --product-four-pane-host-large-private-desktop src/ExplorerDirs.HostProbe/bin/Release/net10.0-windows10.0.19041.0/ExplorerDirs.HostProbe.dll
```

后续重点为大目录延迟及未索引/不可用场景，再集中进行菜单、文件操作、重命名、拖放和可见体验验收；不覆盖现有 temp.86 预览。
