# 键盘操作清单

本表按 [Microsoft 的 Windows / 文件资源管理器快捷键清单](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows) 核对。代码接入与实际 GUI 验收分开记录。文件视图是 Windows Shell ExplorerBrowser；应用处理自己的快捷键后，将其余原生按键交给 `IShellView.TranslateAccelerator`，不重新实现 Shell 文件复制、删除等操作。

| 按键 | Explorer Dirs 行为 | 当前状态 |
| --- | --- | --- |
| Backspace、Alt+↑ | 打开当前目录的上一级 | 已接入 Shell 导航；Backspace 按用户习惯定义，区别于 Windows Explorer 的“后退” |
| Alt+←、Alt+→ | 当前标签的后退、前进历史 | 已接入 ExplorerBrowser 内置 TravelLog；源码已改为切换标签和布局时保留视图，真实历史保持待复测 |
| Tab、Shift+Tab | 当前窗格按后退、前进、上级、刷新、地址、地址历史下拉、转到、新建标签、关闭标签、可见时的搜索输入/搜索/关闭控件、快速筛选输入/模式/筛选文件夹/预设/保存/删除/应用/关闭控件、标签标题、目录树（若显示）、详细信息列标题（若有）、文件项、状态栏“查看”入口的顺序遍历，再进入下一窗格；Shift+Tab 严格反向。非详细信息视图跳过列标题 | 焦点映射与正反向遍历有无窗口回归；原生列标题/文件项之间及菜单关闭后的真实键盘焦点仍待 GUI 验收 |
| Ctrl+Tab、Ctrl+Shift+Tab、标签标题聚焦时 Ctrl+←/→ | 按当前窗格标题显示顺序循环标签 | 已接入；Q-Dir 12.56 加入 Ctrl+←/→ 切标签，本项目仅在标签标题聚焦时处理，文件视图中的 Ctrl+方向键继续交给 Shell；`Ctrl+PageUp/PageDown` 保留给文件列表的原生上下翻页，Q-Dir FAQ 64 也建议用 `Ctrl+Tab` 切标签；拖动排序后的顺序待 GUI 验收 ([Q-Dir 版本记录](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)、[FAQ 64](https://www.softwareok.com/?faq=64&seite=faq-Q-DIR)) |
| Ctrl+1～9 | 跳到当前窗格按标题显示顺序排列的第 1～9 个标签；不存在对应标签时保持当前选择 | 源码映射与无窗口回归通过；重排和多窗格中的实际选择待 GUI 验收 |
| 标签标题的 ←、→、Home、End | 按标题显示顺序切换标签；文件视图内的方向键仍交给 Shell | 已接入；待 GUI 验收 |
| Alt+D、Ctrl+L | 从面包屑进入完整地址编辑并全选路径 | 已接入；真实焦点待 GUI 验收 |
| F4 | Modern 地址栏第一次打开当前标签路径所在的 Shell 文件夹树，再按一次进入完整路径编辑；Classic 地址栏直接进入编辑 | 源码已接入每窗格原生 Shell 树、当前目录定位和虚拟文件夹解析名路由；Escape、切标签、窗格隐藏及窗口失焦关闭。历史仍由独立下拉按钮打开。可见位置、F4/Escape 焦点与虚拟命名空间操作待 GUI 验收 |
| F5 | 刷新当前文件视图 | 已接入 |
| F9 | 计算或取消当前文件系统目录的递归总大小、文件数与子目录数 | 已接入后台显式扫描；不自动扫描、不跟随重解析点；导航/切标签/隐藏窗格时取消；实际多窗格状态栏体验待 GUI 验收 |
| F11 | 当前最大化 / 恢复窗口；Q-Dir 对应全屏 | 快捷键已有接线，但与 Q-Dir 行为不一致；全屏模式的窗口外观待确认 ([Q-Dir 官方命令表](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)) |
| Alt+F11 | 保存当前进程的窗口会话并重启 Explorer Dirs | 已接入 Q-Dir 同款快捷键；仍使用应用内确认和会话保存，不注册系统级热键 |
| F10 | 激活菜单栏并打开“文件”菜单 | 已接入源码；真实焦点和关闭/再次按键行为待 GUI 验收 |
| Ctrl+T、Ctrl+W、Ctrl+Shift+T、Ctrl+K | 新建、关闭、恢复/打开所选文件夹、复制标签 | Ctrl+Shift+T 在文件视图聚焦且仅选中一个文件系统文件夹时于新标签打开它；否则恢复最近关闭的标签。输入框与目录树聚焦时不读取旧文件选区。Ctrl+W 关闭当前标签；每窗格最后一个标签会保留，不像 Explorer 在最后一个标签时关闭窗口。 |
| Ctrl+Q | 打开快速链接菜单 | 已接入独立便携 `QuickLinks` 目录，支持目录快捷方式在活动窗格打开、程序/文件快捷方式由 Windows Shell 启动；真实菜单焦点与可见行为待 GUI 验收 |
| Shift+Enter | 文件视图中将当前选择的文件夹在新标签页中打开；选择文件、无单一文件夹选择或文本输入框聚焦时交回对应控件 | 原生 `HwndHost` 路径与 WPF 未处理键路由都调用同一文件区焦点门控；SettingsSmoke 覆盖成功、焦点不在文件区及无唯一普通文件系统文件夹选择三种情况。物理按键与可见 GUI 仍待验收 |
| Ctrl+A、Ctrl+C/Ctrl+Insert、Ctrl+X、Ctrl+V/Shift+Insert、Ctrl+Z、Ctrl+Y | 全选、复制、剪切、粘贴、撤销、重做 | Shell 先处理；若未接手，应用使用对应原生 Shell 视图命令兜底。Ctrl+Insert 与 Shift+Insert 是微软清单中的复制/粘贴别名，映射 smoke 通过；真实文件操作与 Undo/Redo 序列仍待 GUI 验收 |
| Ctrl+Shift+A | 在文件视图中反向选择当前 Shell 视图的文件项；文本输入框聚焦时保留给输入控件 | 已接入 Q-Dir 对应操作；选择算法及身份保持有隐藏回归，物理按键待 GUI 验收 |
| Enter、F2、Ctrl+D、Delete、Shift+Delete、Alt+Enter、Shift+F10、Shift+右键 | 打开、重命名、删除到回收站、永久删除、属性、打开原生上下文菜单；Shift+右键在 Windows 11 请求“显示更多选项”菜单 | 方向键在原生重命名 Edit 聚焦时直接送入编辑框；SettingsSmoke 覆盖四方向 KeyDown 与 KeyUp 边界。四窗格隐藏生产宿主验证无修饰方向键保持焦点，Shift+←/→ 在编辑框中选择文字并恢复探针线程键盘状态。其它按键与鼠标消息由原生 Shell 视图处理；实际文件操作、菜单类型、目标和物理键盘焦点仍待 GUI 验收 |
| Ctrl+Shift+N | 在当前窗格用 Shell 背景菜单命令新建文件夹 | 已接入；提供“文件”菜单和工具栏入口以避开其他应用的全局热键；新文件夹进入重命名的时序待 GUI 复测 |
| 方向键、Home、End | 文件项选择与滚动 | 由原生 Shell 视图处理；逐项待 GUI 验收 |
| Ctrl+鼠标滚轮 | 按八种查看方式循环切换 | 本项目自定义行为；隐藏生产宿主的合成滚轮消息完成八种循环并保留选区，物理输入路由、可见效果与焦点待 GUI 验收 |
| Ctrl+Shift+1～8 | 按 Explorer 顺序切换视图：超大图标、大图标、中等图标、小图标、列表、详细信息、平铺、内容 | 已接入；快捷键解析把 1～8 映射到八种 `ViewStyle`，与 Microsoft 示例中的 `2=大图标`、`6=详细信息` 一致。Microsoft 清单列到 1～9；第 9 键不由应用拦截，文件视图聚焦时交给原生 Shell，实际第 9 键行为与视图读回仍待 GUI 验收 |
| Ctrl+数字小键盘 `+` | 自动调整详细信息视图中的列宽以适应内容 | 应用在文件视图焦点下将按键先交给 `IShellView.TranslateAccelerator`；Shell 返回 `S_FALSE` 时再发给原生 `SysListView32`，避免 WPF 截走该键。路由 smoke 通过；实体数字小键盘、ExplorerBrowser 实际列宽变化及可见 GUI 仍待验收 |
| Ctrl+方向键后按空格 | 增加或取消单个项目的选择，不影响其他已选项目 | 快捷键 smoke 确认 Ctrl+↑、Ctrl+↓、Ctrl+空格不被应用拦截；原生 Shell 实际选择行为待 GUI 验收 |
| Ctrl+拖动、Shift+拖动、Alt+拖动文件 | 分别复制、移动、创建快捷方式 | ExplorerBrowser 使用原生 Shell 拖放；跨窗格直接 `IDropTarget` 复制已有隔离探针，物理拖动手势、移动/快捷方式语义待 GUI 验收 |
| F3、Ctrl+E、Ctrl+F | 聚焦当前窗格搜索框；Enter 或窗格“搜索”按钮在当前目录的 Shell 结果视图中搜索，Esc 返回目录 | 源码、菜单和工具栏入口已接入并通过构建及无窗口映射回归；真实结果、焦点和右键菜单待 GUI 验收 |
| Alt+F3 | 以唯一选中的普通文件系统文件夹作为搜索范围；输入框显示文件夹名，提示文字包含完整路径；Esc 返回原目录 | 快捷键与焦点映射、`search-ms:` 文件夹范围往返通过无窗口回归；未选中唯一文件系统文件夹时显示提示且不复用旧范围；真实按键与搜索结果待 GUI 验收 |
| Ctrl+M | 打开当前窗格快速筛选栏；Enter 应用编辑条件，F5 应用并刷新，Esc 清除筛选并关闭 | 已接入；预设保存/载入/删除及焦点顺序待可见 GUI 验收 |
| F6、Shift+F6 | 在每个可见窗格的地址栏、可见搜索输入、可见快速筛选输入、标签标题、可见目录树、详细信息列标题（若有）、文件区和状态栏“查看”入口之间正反向循环；跳过窗格内的小按钮 | 停靠点映射有无窗口回归；按[详情窗格设计](superpowers/specs/2026-10-24-details-pane-design.md)不为原生预览/详情窗格增加独立停靠点。Shell 窗格切换后的真实焦点和菜单退出焦点仍待 GUI 验收 |
| Alt+P、Alt+Shift+P、Ctrl+Shift+E | 切换当前标签的预览窗格、详情窗格；将可见目录树展开并定位到当前目录 | Alt+P/Alt+Shift+P 分别切换互斥的原生 Shell 帧，状态按标签保存；菜单/工具栏显示快捷键。Ctrl+Shift+E 将当前窗格树展开到目录。SettingsSmoke 和隐藏生产宿主回归通过；窗格可见渲染、实际尺寸、焦点及树交互待 GUI 验收 |
| Ctrl+N | 复制当前窗口及其布局/标签，并创建独立会话 | 已接入独立窗口 INI 和 `WindowSessions.ini` 顺序清单；收藏夹与应用偏好仍按 profile 共享，固定标签随窗口会话保存。隐藏配置回归通过；可见快捷键、新窗口并行交互和重启恢复待 GUI 验收 |
| Windows+E | 打开文件资源管理器 | 仍由 Windows 系统处理；本应用未注册全局热键或接管系统 Shell。作为后续可逆系统集成项单独规划，不计为当前已支持快捷键 |

2026-10-24 微软现行 File Explorer 快捷键清单复核：[Alt+Shift+P](https://support.microsoft.com/en-gb/accessibility/windows/keyboard-shortcuts-in-windows) 切换详情窗格；[Num Lock+* / + / -](https://support.microsoft.com/en-au/accessibility/windows/keyboard-shortcuts-in-windows) 是目录树中的展开/折叠命令，应由获得焦点的原生目录树处理，不应映射到文件列表。生产 `NativeShellTree` 原生控件与 WPF `ShellTreeHost.TranslateAcceleratorCore` 隐藏宿主都通过 `+` 展开、`*` 递归展开子目录、`-` 折叠测试；WPF 探针仅在加速器未处理时按消息循环派发，并确认前台窗口未变。两项测试都由探针直接建立隔离树状态，不验证实体 Num Lock 输入、用户 Tab/F6 获得焦点或可见焦点体验，仍需桌面验收。

2026-10-24 快捷键清单补入微软现行 Explorer 表中的 `Ctrl+D` 删除到回收站与 `Shift+F10` 上下文菜单。应用未拦截 `Ctrl+D`；其余未识别的文件视图按键经 `IShellView.TranslateAccelerator` 交给 Explorer Shell，因此没有复制文件删除实现。此为源码路由核对，不证明真实文件删除目标、回收站结果或键盘菜单的可见体验；仍需 GUI 验收。

窗口会话仍由一个进程内的 `App` 管理，文件夹转发器按 profile 路由到当前活动窗口；无参数的重复启动会请求激活现有窗口。无法确认单实例路由时，新进程在打开任何登记的会话文件之前停止，避免同时写同一组会话。每个 `MainWindow` 写入自己的会话 INI，写入冲突保护保持原样。`WindowSessions.ini` 保存启动恢复顺序；关闭一个窗口但仍有其他窗口时，仅移除该窗口的清单项，最后一个窗口关闭时保留清单以供下次启动恢复。

地址栏获得焦点时，Backspace 和 Ctrl+A/C/V/X/Z 等文本编辑按键留给地址栏。Shell 内部正在重命名时，编辑键留给重命名输入框。布局切换后应把焦点放回该布局上次活动的文件视图；从单窗格切回四窗格应回到之前活动的四窗格位置。

地址编辑框内按 Esc 恢复面包屑和文件视图焦点；Tab / Shift+Tab 仍将地址编辑视为一个焦点停靠点。点击面包屑路径段不增加键盘停靠点。

其他程序注册的系统级热键可能先于本程序收到按键；前台应用无法可靠阻止其触发。若 Ctrl+Shift+N 等组合键冲突，请使用本程序的菜单或工具栏入口。隔离 GUI 布局及搜索冒烟脚本不发送键盘或鼠标输入，仍会打开测试窗口，因此只在明确空闲的桌面时段手动启用。文件操作快捷键需人工验收。

Q-Dir 官方快捷键清单还列出两项与 Explorer 相冲突的定义：Q-Dir 使用 `Ctrl+N` 新建文件夹、`Shift+F6` 切换查看方式；本项目按用户已选定的多窗口会话方案及 Explorer 焦点顺序，分别保留 `Ctrl+N` 新窗口、`Ctrl+Shift+N` 新建文件夹和 `Shift+F6` 反向遍历主要区域。Q-Dir 的视图切换由本项目 `Ctrl+鼠标滚轮` 与 `Ctrl+Shift+1…8` 提供。依据：[Q-Dir 官方英文快捷键/菜单清单](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)、[Microsoft Windows / File Explorer 快捷键清单](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows)。

2026-10-25 地址历史下拉采用当前标签已访问目录作为数据范围；导航历史独立于 Shell 前进/后退 TravelLog，最多保留最近 20 个唯一路径，复制标签会复制运行时列表，进程重启后重新从当前目录建立。SettingsSmoke 覆盖去重、最近顺序、容量和副本隔离；下拉菜单的可见呈现、键盘选择和导航结果仍待 GUI 验收。

2026-10-25 Q-Dir Modern 地址栏依据官方 FAQ 116 采用 F4 两阶段交互：首次打开 Shell 文件夹树，再次聚焦路径编辑。地址历史由独立按钮打开。源代码与 smoke 已验证快捷键状态、Shell 位置校验和 Release 构建；树弹出布局、实际焦点、虚拟文件夹选择和 Escape 行为仍待 GUI 验收。

2026-10-25 依据微软当前 File Explorer 快捷键表复核：Ctrl+方向键后空格等原生选择操作继续交给 Shell；Ctrl+数字小键盘 `+` 先询问 `IShellView.TranslateAccelerator`，仅 Shell 返回未处理时才把按键转发到原生 ListView 执行列宽自适应；`Win+E` 仍由系统处理。`Backspace` 返回上级目录是用户指定的 Q-Dir 习惯，与微软表中的后退行为不同。`Ctrl+Shift+N` 保持为应用内快捷键，并提供菜单/工具栏替代入口；本程序不注册全局热键或键盘钩子，无法阻止其他程序注册的全局热键抢先消费按键。真实键盘焦点、原生 Shell 操作和系统级路由仍待 GUI 验收。

`preview-0.5.2` 对文件操作后的 Win32 视图焦点与 Shell 文件项焦点一并恢复；如果 Shell 对话框关闭时没有前台窗口，且本应用仍处于活动状态，则恢复本窗口的前台焦点。隔离测试已验证 Ctrl+C/V 生成副本后方向键立即可用；Shift+Delete 需在真实窗口中复测，且只对测试文件执行。

新源码在多选状态只通过 Shell 的 `IFolderView` 恢复原焦点项，不再把 UI Automation 可视树里的首个选中项当作焦点目标。多选复制、粘贴及删除确认后的方向键与焦点位置仍待真实窗口验收。

## GUI 验收步骤

新增查看方式入口：Ctrl+Shift+1 超大图标、2 大图标、3 中等图标、4 小图标、5 列表、6 详细信息、7 平铺、8 内容。顶部“视图 → 当前标签查看方式”提供相同操作，热键被外部应用占用时使用菜单。Ctrl+1～9 仍选择标签；Ctrl+Shift+9 暂交给 Shell，未声明支持。

快捷键与菜单调用 Shell 的 [SetViewModeAndIconSize](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setviewmodeandiconsize)，切换后读回实时配置；不重新套用缓存排序/分组。微软 [File Explorer 快捷键表](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows) 列出 Ctrl+Shift+数字改变查看样式（例：2 大图标、6 详细信息）。本项目的八种样式、排序/分组保留、方向键焦点、复制标签和 INI 重启恢复均需 GUI 验收；构建与无窗口快捷键回归不能替代这些检查。

1. 每个窗格分别打开多级目录，用 Alt+←、Alt+→ 确认其历史互不影响；再按 Backspace、Alt+↑ 返回父目录。切换标签后原标签的历史仍在。
2. 在四窗格的某个文件项开始按 Shift+Tab，应依次到“名称”列标题、标签标题、关闭标签按钮；按 Tab 应原路返回。继续按 Tab 经过文件项后应到下一窗格“后退”按钮。切单窗格、再切回四窗格，确认焦点回到此前活动的文件区。进入地址栏并编辑文本，确认 Backspace 不触发上一级。重排当前窗格的标签后，用 Ctrl+1、Ctrl+2 跳转并检查按显示顺序选择；按超过标签数的 Ctrl+9 应保持原选择。
3. 在文件区按 Alt+D / Ctrl+L、Ctrl+Tab、F5、F11；再检查文件视图的复制、粘贴、重命名、删除、新建文件夹、属性等 Shell 操作。
4. 从文件区按 F6，确认跳到下一可见窗格地址栏；从地址栏按 Shift+F6，确认回到原文件区。详细信息视图应经过列标题，图标视图应跳过列标题；在导航小按钮上按 F6 应直接跳到本窗格下一个主要区域。全程检查反向顺序和已选文件项是否保留。
5. 在测试目录用 F3、Ctrl+E、Ctrl+F 和顶部“搜索当前目录”入口打开搜索栏，输入唯一测试文件名后按 Enter 或点窗格“搜索”按钮；检查结果、双击、文件/空白处右键、Tab 与 Shift+Tab、返回/前进历史。按 Esc 返回原目录，重启确认会话只恢复目录路径。分别对已索引、未索引及网络目录观察响应。
