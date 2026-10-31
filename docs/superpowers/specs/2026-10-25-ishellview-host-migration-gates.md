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
| 浏览器框架、view 创建/销毁 | `ExplorerBrowserHost` + `ShellBrowserSite` | `StreamViewHost` 同时暴露 `IShellBrowser`/`IOleInPlaceFrame`；隐藏 HWND smoke 已验证六个标准宿主菜单组、Shell 菜单 ID、`OLEMENUGROUPWIDTHS` 和宿主组移除，也验证 synthetic composite 使用真正 `IOleInPlaceActiveObject` 指针经 `SetMenuSB`/`OleSetMenuDescriptor` 安装/卸载；菜单与 OLE descriptor 保持调用方所有权；菜单、状态、模态、快捷键与活动对象回调已接到父级路由；`GetBorder` 返回父客户区，边框协商允许零占用并以 `INPLACE_E_NOTOOLSPACE` 拒绝非零占用，均由 COM vtable smoke 覆盖；无 Shell `UIActivate` 实际调用或产品控件接线 | NativeProbe 的 IShellBrowser/IOleWindow/IOleInPlaceFrame vtable、隐藏 HWND 菜单组与 composite 生命周期 smoke；现有默认 view 创建/销毁检查；`ShellViewHostFilterProbe` 的待处理视图切换夹具 | Shell 实际向这些组加入命令、真实 OLE 菜单消息分派、连续切换/失败回滚、窗口附着、真实 WPF/原生控制控件、产品共享菜单/工具栏呈现、COM 所有权和可见多窗格生命周期 |
| 导航、Back/Forward/Parent | ExplorerBrowser 导航事件门控及每标签 TravelLog | `FilteredHost` 只覆盖根目录与一个子目录；`--common-listview-navigation-contract` 通过 Shell 枚举四个临时目录并核对 parent PIDL，且复用产品 `ShellTabNavigationHistory` 验证 Back/Forward/分支；不创建 Shell view，也不接入 ListView 控件导航 | `ShellViewHostFilterProbe` 导航标志、失败目标、历史边界；产品 `NativeNavigationProbe`；`--common-listview-navigation-contract`；SettingsSmoke 历史状态回归 | 任意多层绝对 PIDL、虚拟位置、搜索位置和失败恢复的真实 Shell view 行为；ListView 控件的可见输入/焦点导航 |
| 菜单与命令路由 | `NativeShellCommandMenu`、`ShellMenuTargetTracker`、Shell canonical verbs | 背景 `IContextMenu` 的 Details、Refresh、sort/group 升降序命令在隔离 view 中有 `InvokeCommand`/状态读回；`SVGIO_SELECTION` 文件夹菜单含 `open`、文件菜单含 `copy`，`SVGIO_BACKGROUND` 含 `refresh`。真实 composite View 菜单 ID 与背景菜单 canonical verb ID 不相同。NativeProbe synthetic coordinator 将 File/Edit/View 原始 `WM_COMMAND` 送至隐藏假 HWND；其 `WM_MENUSELECT` 选择状态现能按来源子菜单区分重复 ID，命令队列保留来源，owner/view 重入门控仍在；这些 synthetic smoke 不创建 Shell view，也不证明真实 Shell 命令执行 | NativeProbe 背景命令及文件/文件夹项菜单查询、列头 popup hook、菜单 ID 对照、四 pane context-menu verb 探针、File/Edit/View synthetic `WM_COMMAND` 和 `WM_MENUSELECT` 来源 smoke、SettingsSmoke 目标决策 | frame 对真实 composite View/File/Edit 命令的受支持执行路径仍未确定；真实 `WM_MENUSELECT`/`WM_MENUCOMMAND`/`WM_COMMAND` 时序与目标 HWND 未验证；列头 popup 探针只捕获并取消菜单，owner 链虽已加入诊断但未运行，命令接收归属及排序列身份仍未确定；真实鼠标/键盘菜单交互、产品菜单合并与 ExplorerBrowser 产品行为均未验收 |
| 快捷键与输入控件 | `ExplorerBrowserHost.TranslateAcceleratorCore`，Shell 文件命令兜底，重命名编辑器箭头路由 | `StreamViewHost.TranslateAcceleratorSB` 现可选转发到宿主处理器；消费/`S_FALSE` 回退、异常和释放路径有真实 COM vtable smoke；尚未接入应用快捷键，也没有完整消息循环 | `ShellRenameKeyRoutingSmoke`、`FileViewShortcutDispatchSmoke`、NativeProbe vtable smoke、Private Desktop 基础导航 probe | 物理键盘、Shift 范围选择、复制/粘贴、重命名、确认框后的方向键及 Tab 正反遍历；验证应用路由先于 Shell 且文本输入不被截获 |
| 筛选、搜索和 Shell 回调 | `ShellBrowserSite` 的 CDB/CDB2/CDB3；`ShellFolderFilter`、Search Folder 路由 | `FilteredHost` 的 `IncludeObject` 使用临时筛选夹具；隔离颜色宿主通过 CDB2/CDB3 全部继承/新增槽转发到产品回调，CDB3 `IncludeObject` 也通过真实 CCW vtable 覆盖匹配、排除及 `GetFolder`/属性/显示名失败开放 | SettingsSmoke quick-filter/search 与 CDB3 状态；`ShellViewHostFilterProbe` 隐藏导航/筛选集成；`ShellViewColorBrowserHostSmoke` vtable 与故障注入回归 | 完整 DefView 枚举与回调时序、隐藏项/筛选结果读回、搜索往返、多窗格隔离、真实 provider 失败 |
| 视图模式、排序、分组、列 | `ViewStyle`、`ShellColumnManager`、每标签视图状态/预设 | 默认 Shell modes 可切换；ListView 同步属于颜色原型局部手段 | View/QDR SettingsSmoke、NativeProbe view-style probes | 八种模式（含 Content/自定义）的真实 Shell 状态、排序分组/列读回、刷新保留 |
| 会话与 `.qdr` | `SessionStore`、`QdrImporter`、`QdrExporter`、view state stream | NativeProbe 可按 GUID 会话键将原生 stream 原子保存到独立文件，并在新 store/host 实例中读回；仍没有产品会话编排 | QDR/Session SettingsSmoke 与 view-stream NativeProbe | 真实进程重启恢复、产品标签身份接线、导入/导出互操作和配置升级保护 |
| 文件操作及实时变化 | Shell `IFileOperation` 路由、`ShellDirectoryChangeQueue/Batch`、每窗格 watcher/恢复 | NativeProbe 自有行模型已通过真实多项 copy/move、rename/delete/attributes 通知的行身份、选区/焦点协调；WPF 根宿主的 synthetic `IShellView` adapter 现覆盖真实 `IFileOperation` 新建、文件/文件夹移入、复制、改名、删除，并将子项通知合并为一次父级更新/枚举 | SettingsSmoke 纯逻辑与文件夹具；NativeProbe `--shell-change-reconciliation`；`single-item-recreate-shell-window-scroll` Private Desktop 探针 | 新宿主尚未接入产品 watcher queue/batch/recovery；剪贴板粘贴、冲突/失败 UI、Undo/Redo、双 synthetic view 同步、网络/虚拟 provider、真实 OLE 拖放与可见选区焦点仍未覆盖 |
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

阶段 0 的能力/证据矩阵已覆盖产品合同，并把产品接线、原型证据和可见验收分开。阶段 1 已有三层绝对 PIDL 导航、父级边界、历史分支及失败目标保留旧 view；七类虚拟位置各有 Private Desktop view 创建/菜单查询探针，Search Folder 结果数不稳定、ZIP 命令名查询跳过。默认 filesystem 背景菜单的 Details、Refresh、排序和分组命令已有实际 `InvokeCommand`/状态读回。

阶段 4 的隔离证据已延伸到真实 `IFileOperation` 新建文件夹、文件/文件夹移入、文件复制、改名和删除；当前 synthetic adapter 对相应子项通知只排队/发送一次父目录更新，并只重新枚举一次。`--shell-change-reconciliation` 还覆盖独立行模型的多项跨窗格 copy/move、rename/delete/attributes 与稳定行身份、选择/焦点保持。它们仍不等于新宿主已接入产品 watcher 或双 `IShellView` 协调；剪贴板、冲突/撤销 UI、网络及虚拟 provider 仍是缺口。

Private Desktop 宿主 teardown 和前台守卫问题已在 WPF 根宿主路径解决；最近的完整 synthetic filesystem view 操作探针均通过前台/输入/清理守卫。真实 `DoDragDrop` 仍需要 OLE 的指针/按键输入，不能用隐藏窗口或直接调用 `IDropTarget` 代替；不在共享桌面注入鼠标输入。下一步先推进阶段 4 的双 synthetic view 目标同步与产品 watcher 语义差异审查，再将 OLE 拖放、物理键盘及可见行为留给集中验收。此授权仍仅限 NativeProbe/临时夹具，不授权替换生产 ExplorerBrowser 或生成迁移 release。

2026-10-26 受用户授权复跑迁移探针：Release 构建成功（0 警告/错误）。Private Desktop 导航/筛选探针重新通过临时 filesystem 的绝对 PIDL 深层导航、父级、历史、加速器单次派发及局部 Shell 菜单检查，但在 Search Folder 检查之后、ZIP 容器输出之前未完成；内部 25 秒等待及外部 45 秒 watchdog 触发。现有日志不能确定挂点是在 ZIP provider 导航、视图销毁还是随后资源清理。Private Desktop 菜单命令探针完成列头菜单映射、视图/刷新/排序/分组和局部焦点恢复后，探针内多个阶段仍报告前台未变，但外层在调用返回后检测到全局前台句柄不同。由于没有逐个 teardown 阶段采样，本次不能确定变化来源。按安全边界暂停后续 Shell 宿主运行；此结果不作完整迁移通过证据。

后续 ZIP-only 复跑约 4 秒完成 Shell PIDL 解析、`BrowseObject`、视图创建、菜单/拖放接口查询和 teardown；此前完整序列的挂点仍未确定。前台保护失败的根因是 `PrivateDesktopShellProbe.Run` 在主线程调用 `CreateDesktopW`：Microsoft 契约说明创建 Desktop 时将其分配给调用线程，因此返回后主线程继续处于非输入 Desktop，`GetForegroundWindow` 为 0，并不表示用户桌面真的换了前台。[CreateDesktopW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createdesktopw)、[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)。现在已将创建移到专用 bootstrap 线程；`--private-desktop-owner-smoke` 验证主线程的 Desktop 与前台句柄保持不变，且创建的 Desktop 可关闭。Release build 与 owner smoke 通过；尚未在修复后复跑 Shell 宿主。

修复后 ZIP-only watchdog 退出码 0，前台 HWND 前后同为 `0x550EC2`。完整 host-filter 现在能跑到结束，但外层前台 HWND 从 `0x10EA8` 变成 `0x550EC2` 并失败；只读窗口查询将其分别识别为 ChatGPT 主窗口和 `dnplayer` 的 `EvertaleMockPreview`。由于无法判断是 Shell 激活调用还是测试期间外部活动导致，停止继续运行 Shell 宿主或尝试恢复窗口。根因修复解决了主线程意外留在新 Desktop 的问题，但未达成完整 foreground 隔离门槛。

用户随后明确授权继续推进隔离迁移原型。该授权允许 NativeProbe/临时夹具范围内的原型开发和验证，不代表产品 `ExplorerBrowserHost` 可以替换，也不代表可发布迁移版本。旧的前台偏差已定位为 Private Desktop 创建线程归属问题，WPF 根宿主上的完整 filesystem view 探针现有通过守卫的运行路径；该修复不代表任意 Shell/GUI 探针都天然无副作用，每轮仍须检查 watchdog 前台、输入及清理输出。真实 OLE 鼠标拖放仍须等待用户安排的可见验收会话。

2026-10-26：NativeProbe 的 `StreamViewHost.GetViewStateStream` 现将最近一次 Shell 写入的内存流作为同宿主后续读请求来源；首次读取使用构造时复制的初始字节。两个 host 共用调用方数组的回归证明外部变更不会污染已建 host，也验证一方更新不影响另一方。无 Shell view 的 COM IStream smoke 对旧值回读和共享数组污染先按预期失败，修复后通过。随后新增仅供 NativeProbe 的 `ShellViewStateStore`，以 GUID 会话键单独保存不透明 Shell stream，使用临时文件写入、flush 后原子替换；写入在 host 释放时提交，缺少状态时回退到导入初始字节。smoke 通过两个独立键、重新创建 store/host、无记录回退及 64 KiB 限制验证；三个独立 NativeProbe 进程依次写入两代状态、退出并验证新进程读到最新一代。没有创建 `IShellView`，不证明真实 Shell 何时请求写入、关闭期间错误恢复或产品 session/profile 接线。SettingsSmoke、NativeProbe/App Release build 与 `git diff --check` 通过。

下一步必须换用独立 Windows 会话/VM 或其他不共享当前交互桌面的安全环境，再继续完整 filesystem/virtual 序列、OLE 拖放和真实物理键盘/可见焦点验证。当前会继续推进不启动原生 Shell view 的 Q-Dir parity 源码工作；用户已授权隔离原型，但未授权替换生产 ExplorerBrowser 或生成 release。
