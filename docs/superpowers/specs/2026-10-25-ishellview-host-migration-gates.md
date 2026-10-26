# 独立 IShellView 宿主分阶段迁移门槛

状态：用户已授权继续隔离 `IShellView` 迁移原型；本文件仍不授权替换产品文件视图。它规定逐阶段验证门槛，不改变现有 ExplorerBrowser 产品架构。

## 意图

Explorer Dirs 的首要目标是稳定地承载 Windows Shell 文件管理，并达到 Q-Dir 多窗格日常体验。用户提出导入后文件区变黑和逐项颜色需求后，隔离默认 `IShellView` 原型已证明部分标准视图可绘制逐项颜色，但当前 ExplorerBrowser 使用 DirectUI，没有该原型依赖的 `SysListView32`。颜色是推动原型研究的因素，不足以成为牺牲文件管理核心体验的理由。

本设计规定：如何判断独立 `IShellView` 是否能承载完整产品窗格，以及何时可以考虑迁移。默认继续使用 ExplorerBrowser；任何迁移都必须先通过逐阶段门槛，并与现有产品体验做等价验收。

## 目标

- 保持 Shell 原生文件视图、上下文菜单、文件操作、导航历史、筛选、通知、焦点、标签和多窗格行为。
- 验证独立 `IShellView` 宿主能否覆盖用户日常使用的本地、虚拟、搜索及容器位置。
- 每阶段使用能代表目标能力的隔离夹具、自动回归和后续可见验收；明确记录哪些结论仅来自默认 Shell 文件夹视图。
- 只有所有 P0 和迁移相关 P1 项通过后，才讨论替换产品宿主；未通过时保留 ExplorerBrowser 并暂停迁移。

## 非目标

- 本阶段不修改产品窗格、不生成发布版、不移除 ExplorerBrowser。
- 不把隐藏 HWND、Private Desktop、直接 COM 调用或本程序往返测试当作物理输入、可见 UI 或 Q-Dir 互操作证据。
- 不依赖未公开的 Windows Shell 接口，也不把 Q-Dir 私有配置或程序实现复制到项目。
- 不把逐项颜色扩展到未证明支持的视图模式、Shell provider、选中/悬停/焦点状态。

## 方案比较

1. **保留 ExplorerBrowser，颜色能力继续单独评估（当前推荐默认）。** 继续使用产品已接入的 ShellBrowser 回调、TravelLog、筛选、变更监视、视图状态和 WPF 窗格。优点是保住现有文件管理基线；代价是当前没有可确认的逐行颜色扩展点，颜色功能可能继续缺失。
2. **把所有产品窗格切换到独立 `IShellView` 宿主。** 可直接创建系统默认文件夹视图，并控制创建回调、列表窗口和逐项绘制。优点是最完整地探索颜色及 DefView 能力；代价是必须自行重建大量 ExplorerBrowser 已提供的浏览器框架/生命周期契约，导航、筛选、通知、标签、焦点、虚拟位置、预览与拖放均可能退化。
3. **双宿主混用或按位置/功能选择宿主。** 保留 ExplorerBrowser 作为默认，部分窗格或视图改用独立 `IShellView`。启动风险较低但产生两套菜单、键盘、列、筛选、历史、状态保存和拖放行为；用户难以预测，同一标签跨布局/重启也可能改变语义。

选择建议：采用分阶段能力验证；没有通过门槛前一律保留 ExplorerBrowser。若目标最终要求所有窗格具有一致 Explorer/Q-Dir 行为，双宿主不作为默认产品设计。是否继续走整体替换，须在阶段门槛结果出来后另行审阅。

## 已核实现状

- 产品 `ExplorerBrowserHost` 持有 `IExplorerBrowser`，在创建的 STA 管理视图、导航就绪、Shell TravelLog、快捷键转发、文件操作焦点恢复、视图配置、目录 watcher 与刷新恢复。
- 产品 `ShellBrowserSite` 实现 `ICommDlgBrowser`、`ICommDlgBrowser2`、`ICommDlgBrowser3` 及 Explorer Pane 可见性回调；WPF `PaneControl` 负责多个标签、布局、导航树和视图状态连接。
- 隔离 `StreamViewHost` 的浏览器框架回调存在 no-op，`BrowseObject` 基类返回 `E_NOTIMPL`，没有完整产品的状态栏、控制窗、WPF 窗格、标签、历史、筛选和 watcher 生命周期。
- 隔离 `FilteredHost` 只实现两个临时目录的父级/历史/筛选路径，不能证明任意路径、虚拟 Shell 位置或完整搜索行为。
- 默认 ExplorerBrowser 实际 view 的窗口树为 `CtrlNotifySink`、`DirectUIHWND`、`ScrollBar`，没有原型所需 `SysListView32`；原型的 `LVM_SETVIEW` 和 `NM_CUSTOMDRAW` 不能直接接入现有宿主。
- Private Desktop 已证明默认 `IShellView` 在 Icon、Details、SmallIcon、List、Tile 五种同步模式下可绘制规则色并保留实际选中项默认颜色，包含排序/分组样本；不涵盖 Content、Q-Dir 特殊模式、其它 Shell provider、用户桌面或 ExplorerBrowser DirectUI。

详细差异和逐轮日志见[隔离原型记录](../../ishellview-color-spike.md)；当前用户可见 parity 仍以[Shell 功能矩阵](../../shell-parity.md)为准。

## 分阶段门槛

阶段按顺序推进。发现能力不足时先分析是否为夹具/宿主问题；不得跳过失败阶段去做颜色演示，也不得把一个隔离能力通过当成完整产品迁移许可。

### 阶段 0：契约清单与基线冻结

- 为目标产品窗格列出 ExplorerBrowser 已接线能力：文件/背景菜单、`TranslateAccelerator`、导航历史、虚拟位置、搜索、筛选、预览/详情、视图/排序/分组/列、文件操作、目录更新、选区/焦点恢复、拖放、标签和会话保存。
- 每项标记“生产已接线 / 原型缺失 / 需可见验收”，并指定能证明该项的测试层级。
- 固定当前 App、SettingsSmoke、NativeProbe 的 Release 基线。不得改变桌面、用户剪贴板、Shell Undo 栈或每日 profile。

退出条件：清单覆盖核心文件管理和所有 P0；测试入口的夹具生命周期、前台检查和失败清理明确。当前源码对照已完成第一版，仍需把迁移关口与验收分类固化为可执行表。

阶段 0 的能力/证据矩阵：

| Shell 合同 | 当前产品实现 | 独立宿主现状 | 可复用验证 | 迁移前仍需的证据 |
|---|---|---|---|---|
| 浏览器框架、view 创建/销毁 | `ExplorerBrowserHost` + `ShellBrowserSite` | `StreamViewHost` 同时暴露 `IShellBrowser`/`IOleInPlaceFrame`；隐藏 HWND smoke 已验证六个标准宿主菜单组、Shell 菜单 ID、`OLEMENUGROUPWIDTHS` 和宿主组移除，也验证 synthetic composite 使用真正 `IOleInPlaceActiveObject` 指针经 `SetMenuSB`/`OleSetMenuDescriptor` 安装/卸载；菜单与 OLE descriptor 保持调用方所有权；菜单、状态、模态、快捷键与活动对象回调已接到父级路由，边框空间仍为 `E_NOTIMPL`；无 Shell `UIActivate` 实际调用或产品控件接线 | NativeProbe 的 IShellBrowser/IOleWindow/IOleInPlaceFrame vtable、隐藏 HWND 菜单组与 composite 生命周期 smoke；现有默认 view 创建/销毁检查；`ShellViewHostFilterProbe` 的待处理视图切换夹具 | Shell 实际向这些组加入命令、真实 OLE 菜单消息分派、连续切换/失败回滚、窗口附着、真实 WPF/原生控制控件、产品共享菜单/工具栏呈现、COM 所有权和可见多窗格生命周期 |
| 导航、Back/Forward/Parent | ExplorerBrowser 导航事件门控及每标签 TravelLog | `FilteredHost` 只覆盖根目录与一个子目录 | `ShellViewHostFilterProbe` 导航标志、失败目标、历史边界；产品 `NativeNavigationProbe` 记录 ExplorerBrowser 基线 | 任意多层绝对 PIDL、分支、虚拟位置、搜索位置和失败恢复 |
| 菜单与命令路由 | `NativeShellCommandMenu`、`ShellMenuTargetTracker`、Shell canonical verbs | 背景菜单可查询并已实际调用 Details、Refresh、sort/group 升降序读回；`SVGIO_SELECTION` 的文件夹菜单含 `open`、文件菜单含 `copy`，`SVGIO_BACKGROUND` 含 `refresh`；真实列头 `WM_CONTEXTMENU` 在私有 Desktop 捕获后，可初始化查看/排序/分组 submenu；四个标准叶命令及三个子菜单中 9/6/7 个子项与背景 `IContextMenu` 对应标题的 ID 基址差均为 30976，根 canonical verb 为 `view`、`arrange`、`groupby`；Details 视图的 `SysHeader32` 与 `IColumnManager` 可见列数均读回 4 | NativeProbe 背景命令及文件/文件夹项菜单查询、列头 popup hook、三组子菜单 ID 对照、列控件读回、Refresh/sort/group 焦点探针、SettingsSmoke 目标决策 | 根 submenu 项仍返回 `UINT_MAX`，但其初始化后的子项映射现已通过隐藏 Private Desktop 对照；未验证真实鼠标/键盘交互、可见菜单及产品菜单合并；`GetItemObject` 没有列标题范围；隐藏默认 IShellView 已通过稳定采样确认命令后短暂空视图/选区波动，并在 150–200 ms 内稳定；ExplorerBrowser 产品及可见行为仍未验证；正式排序恢复须使用 Shell item identity；产品宿主状态恢复及可见原生菜单仍待验收 |
| 快捷键与输入控件 | `ExplorerBrowserHost.TranslateAcceleratorCore`，Shell 文件命令兜底，重命名编辑器箭头路由 | `StreamViewHost.TranslateAcceleratorSB` 现可选转发到宿主处理器；消费/`S_FALSE` 回退、异常和释放路径有真实 COM vtable smoke；尚未接入应用快捷键，也没有完整消息循环 | `ShellRenameKeyRoutingSmoke`、`FileViewShortcutDispatchSmoke`、NativeProbe vtable smoke、Private Desktop 基础导航 probe | 物理键盘、Shift 范围选择、复制/粘贴、重命名、确认框后的方向键及 Tab 正反遍历；验证应用路由先于 Shell 且文本输入不被截获 |
| 筛选、搜索和 Shell 回调 | `ShellBrowserSite` 的 CDB/CDB2/CDB3；`ShellFolderFilter`、Search Folder 路由 | `FilteredHost` 的 `IncludeObject` 使用临时筛选夹具；隔离颜色宿主通过 CDB2/CDB3 全部继承/新增槽转发到产品回调，CDB3 `IncludeObject` 也通过真实 CCW vtable 覆盖匹配、排除及 `GetFolder`/属性/显示名失败开放 | SettingsSmoke quick-filter/search 与 CDB3 状态；`ShellViewHostFilterProbe` 隐藏导航/筛选集成；`ShellViewColorBrowserHostSmoke` vtable 与故障注入回归 | 完整 DefView 枚举与回调时序、隐藏项/筛选结果读回、搜索往返、多窗格隔离、真实 provider 失败 |
| 视图模式、排序、分组、列 | `ViewStyle`、`ShellColumnManager`、每标签视图状态/预设 | 默认 Shell modes 可切换；ListView 同步属于颜色原型局部手段 | View/QDR SettingsSmoke、NativeProbe view-style probes | 八种模式（含 Content/自定义）的真实 Shell 状态、排序分组/列读回、刷新保留 |
| 会话与 `.qdr` | `SessionStore`、`QdrImporter`、`QdrExporter`、view state stream | NativeProbe 可按 GUID 会话键将原生 stream 原子保存到独立文件，并在新 store/host 实例中读回；仍没有产品会话编排 | QDR/Session SettingsSmoke 与 view-stream NativeProbe | 真实进程重启恢复、产品标签身份接线、导入/导出互操作和配置升级保护 |
| 文件操作及实时变化 | Shell `IFileOperation` 路由、`ShellDirectoryChangeQueue/Batch`、每窗格 watcher/恢复 | 通过独立 watcher 接入做过少量复制/改名/删除与刷新重叠测试 | SettingsSmoke 的纯逻辑/文件夹具；NativeProbe 临时操作；ExplorerBrowser HostProbe 可复用但当前暂停 | 操作前后视图收敛、大目录/burst/溢出、选区身份、失败对话框与跨窗格状态 |
| 焦点、选择与恢复 | `ShellViewSelectionSnapshot`、`ShellFocusRestoreGate`、`FocusTraversal` | 选择快照复用过；隐藏 focus 结果不具可见意义 | SettingsSmoke focus/selection gates；NativeProbe selection identity | 原生项目/列标题焦点、四窗格 Tab/Shift+Tab、粘贴/删除确认后的方向键 |
| 拖放与 Shell provider | 产品 `IDropTarget`/WPF tab drop/导航树 Shell | 隔离 view 暴露 `IDropTarget`，未构成真实 OLE 源端 | NativeProbe data object、HostProbe 目标接口/夹具 | 鼠标真实 OLE Copy/Move/Link、虚拟 PIDL、标签重排、网络与 ZIP 行为 |
| 多窗格/标签及辅助窗格 | `PaneControl`、`PaneLayoutHost`、`SessionStore`、`NativeShellTree`、`ExplorerPaneVisibility` | 无 WPF 窗格或标签 host | SettingsSmoke layout/session/tree gates；HostProbe 隐藏生命周期观察 | 实际布局切换、隐藏 view 保留、树/预览/详情绘制及重启可见体验 |
| 逐项颜色 | 产品 DirectUI 无已确认的逐项颜色扩展点 | 默认 `IShellView` 的五种标准同步模式曾取得 Private Desktop 像素证据 | `ShellViewColorPolicySmoke`、Private Desktop 原型颜色 probe | 更多视图/状态/provider 的像素与选中/悬停/焦点；并须通过前十项合同 |

测试入口名只说明可复用的局部验证，不表示相应迁移门槛已通过。ExplorerBrowser HostProbe 目前受项目 no-Computer-Use 规则限制，不在本轮启动。

### 阶段 1：通用宿主生命周期与导航

- 将临时双目录样例扩展为任意合法 filesystem absolute PIDL 的文件夹导航；验证 Enter、双击通知、`SBSP_PARENT`、Back/Forward、无效目标、同路径和导航分支截断。
- 创建/切换/销毁 `IShellView` 时维持正确的前一个 view、活动 view、窗口附着和 COM 所有权；失败导航不能丢失原视图。
- 再覆盖 Shell 绝对解析名与搜索结果位置；只接受 Shell 已确认的文件夹，按位置身份持久化，不把搜索临时 PIDL 写入会话。

退出条件：多层临时树和至少一个虚拟 Shell 夹具在连续往返、失败及重试中均保持当前路径和活动视图正确；旧 view 句柄/引用释放无泄漏；所有失败路径保留可用旧视图。仅默认文件系统夹具通过不满足此门槛。

### 阶段 2：Explorer 浏览器回调与菜单/快捷键

- 实现并验证完整 `ICommDlgBrowser` / `ICommDlgBrowser2` / `ICommDlgBrowser3` 语义，包括 include-object、默认命令、状态变化、列点击和预览/筛选通知。
- 检查背景、普通项目、文件夹、列标题和键盘菜单的原生 Shell 命令；自定义命令只能使用已确认目标，并保持前置/合并菜单合同。
- 覆盖常用 Explorer 快捷键、文本编辑/重命名排除、多选和 Shell 返回 `S_FALSE` 的按键派发；确认焦点窗口、Shell 焦点项与选择项分别正确。

退出条件：文件和背景菜单均保留 Windows Shell 原生命令；快捷键在所有目标视图模式下具备完整消息循环验证，重命名和其他输入控件不被截获。直接调用单个 `TranslateAccelerator` 不足以证明键盘多选。

### 阶段 3：视图状态和每标签行为

- 对齐八种查看方式、排序、多字段排序、分组、列宽/次序、筛选和可见列标题；检查切视图及刷新后选区身份、排序/分组与焦点保持。
- 将每标签保存/复制/默认值、INI 版本冲突和 `.qdr` 交换接到新宿主适配层，不改变当前 profile 或导入源。
- 覆盖树、预览、详情、快速筛选和 Search Folder 的 attach/detach/恢复，不能因新宿主改变标签路径或过滤条件。

退出条件：所有现有 View/Session/QDR smoke 的要求能应用于新宿主，并有实际 Shell 视图读回；旧配置仍可读、新版拒绝被旧版本覆盖。App 模式仅显示缓存值不算通过。

### 阶段 4：文件操作、实时更新和并发状态

- 复用现有 Shell `IFileOperation` 路径；测试复制、剪切、粘贴、重命名、新建、删除/回收站、属性，以及失败、冲突与取消。
- 把产品 watcher queue/batch/recovery 接到每个新 host；检查 watcher 溢出、刷新重叠事件、部分枚举、选区恢复和 fallback。
- 做小目录和大目录交错压力测试；记录 STA 调用时长、枚举进度、文件操作后读回、fallback 次数和焦点/选区恢复。网络及 provider 边界须单列。

退出条件：不以磁盘上操作成功代替视图同步；所有变化在规定收敛条件内出现在当前 Shell 视图，刷新后视图身份和焦点策略正确，watcher 异常能恢复且不阻塞其他窗格。

### 阶段 5：拖放、其它 Shell provider 与可见验收

- 覆盖 `IDropTarget`、真实 OLE 源端效果、跨窗格复制/移动/快捷方式、标签重排、树/列表拖放，以及虚拟 PIDL/容器/网络位置。
- 在专用隔离桌面逐项验收 UI 命中、鼠标、Tab/Shift+Tab、Alt+Tab、菜单关闭、标签重排、布局切换和多窗口，不自动碰用户桌面。
- 与 ExplorerBrowser 现有实现按同一验收清单对照，完整记录不可恢复的功能差异。

退出条件：真实文件和 Shell provider 互操作、物理键鼠以及多窗格切换通过；任何 GUI 失败修复并重复同一场景。隐藏宿主、直接 `Drop` 接口调用和 UIA 元素存在均不能代替真实 OLE/UI 行为。

### 阶段 6：颜色能力验证和迁移决策

- 仅在阶段 1–5 达标后，验证逐项颜色在排序/分组、选中/未选中、悬停、键盘焦点、滚动、刷新和八种视图下的真实绘制。
- 若颜色路径要求替换 Shell 的默认绘制/控件，先验证 Shell 原生命令和 provider 能力仍保留；不可通过同步或缓存一份普通 ListView 来模拟完整 Explorer 视图。
- 比较迁移前后 SettingsSmoke、NativeProbe 和可见验收结果，并由用户预览差异摘要，再决定是否允许产品迁移及发布。

退出条件：颜色覆盖范围明确且可复现，同时阶段 0–5 通过；否则记录不支持的视图和状态，保留 ExplorerBrowser。任何成功仅适用于已测 Windows 版本、provider、模式和状态组合，不概括为全局兼容。

## 回退与发布规则

- 在用户同意整体迁移前，产品默认宿主保持 ExplorerBrowser；不把两个宿主动态混用作为隐藏 fallback。
- 原型失败时撤销或隔离试验代码，不碰 profile、标签文件、Quick Links、收藏夹或 `releases/`。
- 即使全部隐藏测试通过，也先准备差异清单、可见验收步骤和独立预览包；用户预览前不替换发布版本。

## 当前下一步

阶段 0 的可执行能力/证据矩阵已列出；阶段 1 filesystem 原型已验证三层绝对 PIDL 导航、Shell 父级边界、历史往返/分支截断、失败目标保留旧 view。七类虚拟位置现各自通过独立 Private Desktop view 创建/菜单查询探针；核心动词状态按 provider 记录，Search Folder 结果数不稳定，ZIP 命令名查询跳过。默认 filesystem 背景菜单的 Details、Refresh、排序与分组升降序命令均有实际 `InvokeCommand` 和状态读回；扩展复测一次在清理后前台对照失败，原因未定，当前先停用新的 Shell view 运行。手动直调 `IDropTarget::Drop` 不作为拖放行为结论。完整 `DoDragDrop` 会按当前鼠标指针定位目标窗口，需要活动输入桌面；当前 Private Desktop 不活动，不能安全地将输入注入用户桌面。后续在隔离且可接收输入的测试会话，仅对临时 filesystem fixture 验证 OLE source/target；不调用虚拟位置的 Drop，也不启动产品 ExplorerBrowser。此原型授权不代表产品文件视图迁移批准；迁移决策仍待用户审阅本草案。Shift 范围选择的合成 `TranslateAccelerator` 结果不作为失败门槛；完整消息循环或真实输入证据尚缺。

2026-10-26 受用户授权复跑迁移探针：Release 构建成功（0 警告/错误）。Private Desktop 导航/筛选探针重新通过临时 filesystem 的绝对 PIDL 深层导航、父级、历史、加速器单次派发及局部 Shell 菜单检查，但在 Search Folder 检查之后、ZIP 容器输出之前未完成；内部 25 秒等待及外部 45 秒 watchdog 触发。现有日志不能确定挂点是在 ZIP provider 导航、视图销毁还是随后资源清理。Private Desktop 菜单命令探针完成列头菜单映射、视图/刷新/排序/分组和局部焦点恢复后，探针内多个阶段仍报告前台未变，但外层在调用返回后检测到全局前台句柄不同。由于没有逐个 teardown 阶段采样，本次不能确定变化来源。按安全边界暂停后续 Shell 宿主运行；此结果不作完整迁移通过证据。

后续 ZIP-only 复跑约 4 秒完成 Shell PIDL 解析、`BrowseObject`、视图创建、菜单/拖放接口查询和 teardown；此前完整序列的挂点仍未确定。前台保护失败的根因是 `PrivateDesktopShellProbe.Run` 在主线程调用 `CreateDesktopW`：Microsoft 契约说明创建 Desktop 时将其分配给调用线程，因此返回后主线程继续处于非输入 Desktop，`GetForegroundWindow` 为 0，并不表示用户桌面真的换了前台。[CreateDesktopW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createdesktopw)、[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)。现在已将创建移到专用 bootstrap 线程；`--private-desktop-owner-smoke` 验证主线程的 Desktop 与前台句柄保持不变，且创建的 Desktop 可关闭。Release build 与 owner smoke 通过；尚未在修复后复跑 Shell 宿主。

修复后 ZIP-only watchdog 退出码 0，前台 HWND 前后同为 `0x550EC2`。完整 host-filter 现在能跑到结束，但外层前台 HWND 从 `0x10EA8` 变成 `0x550EC2` 并失败；只读窗口查询将其分别识别为 ChatGPT 主窗口和 `dnplayer` 的 `EvertaleMockPreview`。由于无法判断是 Shell 激活调用还是测试期间外部活动导致，停止继续运行 Shell 宿主或尝试恢复窗口。根因修复解决了主线程意外留在新 Desktop 的问题，但未达成完整 foreground 隔离门槛。

用户随后明确授权继续推进隔离迁移原型。该授权允许 NativeProbe/临时夹具范围内的原型开发和验证，不代表产品 `ExplorerBrowserHost` 可以替换，也不代表可发布迁移版本。由于完整 Private Desktop 探针曾观察到前台句柄变化且原因未能归因，本机共享交互桌面上暂不启动任何新的 `IShellView` 宿主或 GUI 探针；需先准备独立 Windows 会话、VM 或其他不共享输入桌面的环境。期间可继续进行不启动 Shell view 的源码契约审查、纯逻辑 smoke 和构建验证。

2026-10-26：NativeProbe 的 `StreamViewHost.GetViewStateStream` 现将最近一次 Shell 写入的内存流作为同宿主后续读请求来源；首次读取使用构造时复制的初始字节。两个 host 共用调用方数组的回归证明外部变更不会污染已建 host，也验证一方更新不影响另一方。无 Shell view 的 COM IStream smoke 对旧值回读和共享数组污染先按预期失败，修复后通过。随后新增仅供 NativeProbe 的 `ShellViewStateStore`，以 GUID 会话键单独保存不透明 Shell stream，使用临时文件写入、flush 后原子替换；写入在 host 释放时提交，缺少状态时回退到导入初始字节。smoke 通过两个独立键、重新创建 store/host、无记录回退及 64 KiB 限制验证；三个独立 NativeProbe 进程依次写入两代状态、退出并验证新进程读到最新一代。没有创建 `IShellView`，不证明真实 Shell 何时请求写入、关闭期间错误恢复或产品 session/profile 接线。SettingsSmoke、NativeProbe/App Release build 与 `git diff --check` 通过。

下一步必须换用独立 Windows 会话/VM 或其他不共享当前交互桌面的安全环境，再继续完整 filesystem/virtual 序列、OLE 拖放和真实物理键盘/可见焦点验证。当前会继续推进不启动原生 Shell view 的 Q-Dir parity 源码工作；用户已授权隔离原型，但未授权替换生产 ExplorerBrowser 或生成 release。
