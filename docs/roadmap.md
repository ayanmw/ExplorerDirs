# 路线图

## 当前执行入口（2026-10-31 后台复核）

2026-10-31 查看方式切换局部重绘租约：真实视图模式变更现在只在同步 `IFolderView2.SetViewModeAndIconSize` 周围短暂关闭 DefView 重绘，租约在返回或异常时立即释放；F5 刷新仍让 Shell 自行绘制，避免异步枚举期间旧帧与实际命中目标不一致。针对性源码合同先按预期失败，再通过；完整 Release SettingsSmoke 与 App Release 构建通过（0 警告、0 错误）。没有创建 Shell view 或启动 GUI；此项只证明代码接线，实际黑帧/闪烁是否减少仍待集中验收。

2026-10-31 当前主线集中验收副本以干净提交 `eff0a65654b44c0a7d8b1de41d967e2071130891` 准备：四窗格副本位于 `src/.state/gui-smoke/20261031-050655-754-4b72da/`，状态 `prepared-not-run`，47 项待验收。包含 F5/查看方式切换、键盘焦点、标签/克隆、主题、Q-Dir Tab Group、`.qdr`、Soft-Refresh 等项目；构建和副本准备成功，EXE/DLL SHA-256 已与 `preparation.json` 核对。后续主线提交仅更新了调查/路线图文档，未改变该候选对应的产品源码。没有启动应用窗口、创建 ExplorerBrowser 或操作文件。BuildIdentity、哈希和验收边界见[最新集中验收候选](gui-acceptance-2026-10-31-main-prepared-latest.md)。

2026-10-31 派生目录大小列：DefView 的单项通知、慢速列状态、父目录更新、列重放、查看模式切换与 DELETE+CREATE A/B 均未使更新后的派生列保留/重取；其中无效的 `IShellView.Refresh()` 轮因桌面前台/输入变化不采纳。有效轮次保持选区/焦点并通过隔离清理，产品仍使用 ExplorerBrowser，未接入派生列。停止追加同类 DefView 通知补丁，后续评估自有行视图与完整 Explorer 兼容门槛。详情见[派生大小列调查](folder-size-column-investigation.md#2026-10-31wpf-根宿主下的通知列重放策略收敛)。
2026-10-31 Q-Dir 筛选期间多选跳转：快速名称筛选或列值筛选启用时，文件内容区的未修饰 Up/Down 会在多选项目间按 Shell 视图顺序移动焦点，并确保目标可见；不修改其它选中位。仅当当前焦点本身属于多选集合且存在相邻已选项目时接管；单选、首尾边界、搜索、修饰键、非文件内容焦点及过期视图请求都回到/拒绝原生 Shell 路由。方向键门控、相邻序号及当前访问路由有 Release SettingsSmoke；完整 SettingsSmoke 和 App Release build 通过。没有创建 Shell view 或 GUI；实际 Shell 选择回调、像素/焦点、分组/各视图模式及 Q-Dir 可见对照仍待集中验收。

2026-10-31 F5 当前源码复核：浏览器可用且没有待重试导航时，产品刷新直接调用当前 `IShellView.Refresh()`；不广播 `SHCNE_UPDATEDIR`，也不通过 `IFolderView2.SetRedraw(FALSE)` 暂停绘制。刷新后的计数稳定等待只用于安全恢复选区/焦点。查看方式切换仍在同一 Shell 视图调用 `IFolderView2.SetViewModeAndIconSize`，不重建 ExplorerBrowser 或重新导航。源码入口 `ExplorerBrowserHost.Refresh` 与 `ShellRefreshPresentationSmoke` 一致；白屏持续时间、刷新期间输入目标与慢速/虚拟目录仍待集中可见验收。下文“F5 绘制租约收窄”是已撤回的中间方案，不代表当前产品行为。

2026-10-31 Private Desktop 原生视图刷新/查看方式行数采样：300 项 stock `IShellView` 中，List/小图标/Details 三次 `IFolderView2.SetViewModeAndIconSize` 切换期间最小行数均为 300，视图和 `SysListView32` HWND 不变。随后调用 `IShellView.Refresh()`，样本短暂读到 0 行（3 个 5 ms 采样点，跨度约 10 ms），最终读到新增后的 301 行；视图/List HWND 仍不变。对照的 `SHCNE_UPDATEDIR` 通知样本最小行数为 301、没有空行样本，通知后读到 302 行。Private Desktop 隔离检查通过，前台和输入 tick 未变化；探针没有测量像素绘制，因此不能把行数恢复时间当成可见闪烁时长。该结果支持“应用没有重建视图、空行来自 Shell 原生刷新阶段”的判断；通知路径不能限定为本应用视图且此前隐藏生产宿主样本更慢，暂不替换 F5。用户可见白/黑帧仍留待集中 GUI 验收。

2026-10-31 目录树拖入展开生命周期修正：树宿主变为不可见时，取消尚未触发的悬停展开计时和 Shell 项引用，避免隐藏窗格在后台被意外展开、重现时改变树状态。回归先在旧代码上失败，加入隐藏事件清理后通过；定向 SettingsSmoke 和 App Release build 通过。该检查验证生命周期接线，不验证真实 Shell 拖入回调和放置结果；两者仍留待集中 UI 验收。

2026-10-31 逐行文件夹大小快照跨 provider 重建合同：NativeProbe 的扫描快照表现为可共享、引用计数的 store；provider 嵌套绑定复用同一 store，probe 则保留 store、销毁 provider 后用同一 store 构造新 provider。当前合同的初始值和变更后值均由生产扫描器生成：两条 sibling PIDL 的缓存为 10/12 B，重扫后为 7/12 B；新 provider 重新解析 PIDL 后仍读回更新值及正确数值排序。原生 DLL 构建及 `--static-shell-column-folder-smoke` 通过，输出 `provider-recreation=retained`，没有创建 Shell view 窗口。证据只证明隔离 probe 可把扫描结果跨 provider 生命周期保留并复用；产品 owner 注入、Shell class factory 生命周期、生产 `ShellListViewRowModel`/异步扫描发布、DefView 列更新和可见交互仍未验证。ExplorerBrowser 产品不变；逐行大小仍为 P2 隔离原型。详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31扫描快照跨-provider-重建)。

2026-10-31 temp.80 同 PID 被动内存复采：PID `47460` 持续存活，13 点覆盖 60.20 秒，Working Set 固定 `234,713,088 B`、Private Bytes 固定 `121,180,160 B`，句柄/线程/GDI/USER 计数也不变。没有固定文件浏览负载或托管堆/原生分配采样，因此只说明此分钟无进程计数变化，不能排除操作期间或长期增长。完整身份、原始 CSV 哈希与限制见[内存采样记录](memory-sampling.md#2026-10-31-0821-北京时间temp80-同一-pid-一分钟只读采样)。

2026-10-31 逐行文件夹大小绝对身份与同实例重枚举合同：NativeProbe 从真实 Shell PIDL 获取 `SIGDN_DESKTOPABSOLUTEPARSING`，传入合成 `IShellFolder2` 实例级行快照表；provider 再从 backing folder/行 PIDL 解析并校验同一 identity。夹具扫描时 10 B、磁盘后改为 7 B；列仍读回 10 并排在 8 B 文件后。两个兄弟行独立保存 10 B/12 B，只更新一行为 6 B 后，两行值与排序分别正确；同一 provider 再次 `EnumObjects` 生成新 PIDL 后仍命中更新后的值。原生 DLL/NativeProbe Release 构建及 `--static-shell-column-folder-smoke` 通过，不创建窗口。provider 实例重建后的缓存、生产 `ShellListViewRowModel` 生命周期/异步扫描发布及 Shell 通知还未验证；未证明 DefView 列恢复/通知合同、可见交互或产品接线。ExplorerBrowser 产品不变；逐行大小仍为 P2 隔离原型。

2026-10-31 跨窗格移动标签 HostProbe 复验：修复 `PaneTabTransferProbe` 未传递新增 `softRefresh` 可选构造参数的过期调用。当前 Release HostProbe `--pane-tab-transfer` 通过：原 `TabItem`、`TabState`、`ExplorerBrowserHost` 与原生 HWND 保持同一实例，宿主转移到目标窗格后尺寸更新；当前 B 位置可通过 Shell Back 回到移动前 A 位置。完整探针还通过独立复制、克隆及关闭清理。运行使用隐藏且禁止激活的 `HwndSource`，前台未受影响；真实标签拖放、物理鼠标与焦点体验仍待集中可见验收。

2026-10-31 隔离 Shell 重命名映射扩展到绝对解析名：收集 `SHCNE_RENAMEITEM` / `SHCNE_RENAMEFOLDER` 通知时改用 `SIGDN_DESKTOPABSOLUTEPARSING`，匹配行模型的 Shell 身份，而非只支持文件系统路径；目录重命名 row ID 丢失回归仍通过。目录路径变化后旧递归大小/计数清空，后台重扫回写新值；创建/更新/重命名/删除、选择焦点恢复均通过，窗口隐藏且前台未变。NativeProbe Release build 0 警告、0 错误。证据限于隐藏 `SysListView32` 探针；虚拟 Shell、可见 IShellView 和产品 ExplorerBrowser 未覆盖，产品仍未接入此行模型。

2026-10-31 隔离扫描过期失败门控：目录大小协调器此前只将过期取消视为无结果；若旧 worker 忽略取消后抛出 IO/扫描错误，异常仍会泄漏到调用方，可能被误报为当前扫描失败。现在任意已过期 ticket 的 worker 异常都以 `false` 结果结束，当前 ticket 的异常仍正常传播。新增回归在旧逻辑上复现失败，并分别验证过期异常不影响较新成功发布、当前异常继续传播；定向和完整 Release SettingsSmoke、App/NativeProbe Release build 与 `git diff --check` 通过。仅覆盖隔离原型协调器，不意味着产品逐行大小功能已接入。

2026-10-31 Q-Dir 颜色规则纯策略复核：重新核对官方 FAQ 6/47，确认公开语法覆盖分号分隔通配符、隐藏项 `#`、目录 `#DIR`、只读 `#RO`、背景 `#BG`/活动视图背景 `#BG-A`，以及逐属性 `-1/0/1` 规则优先级；这些说明仍未给出 `.qdr` `[Filter2]` 六个数值字段的列顺序。Release NativeProbe `--shell-view-color-policy-smoke` 退出码 0，覆盖原始行保真、公开匹配/优先级、背景与字体策略、选择态保留及隔离宿主 COM 合同；该 smoke 未创建 Shell view，也未显示 UI。未把字段猜测接入 `.qdr` 导入器，产品仍使用 ExplorerBrowser。详情见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-26读取-q-dir-filter2-原始规则行)。

本文件同时保留历史批次，较早段落的“当时未实现”不能作为当前缺口清单。当前优先级如下；后台通过与可见交互通过分开记录。

2026-10-31 主线集中验收候选更新：当时副本基于干净提交 `cd5dfa05dcd291e2a6d9367f5ce458a1740993d0`，四窗格副本位于 `src/.state/gui-smoke/20261031-014610-875-4d3ab6`；清单 `prepared-not-run`、46 项待验收，EXE/DLL SHA-256 分别为 `C9E3B653163A339BB29905FB0BD181D0653A72E6C2E1C05F605D630EB0535B05` / `579B73E312F492824C7000BE1E3BC45E2C0CC6A686FD501D86F5A5B9662B5C8F`。profile 预置 1.5 秒悬停展开延时。准备只构建并写隔离夹具，没有启动应用窗口、创建 ExplorerBrowser、发送输入或触碰正式 profile；不能据此宣称任何可见交互通过。刷新空白、命中目标、选区/焦点/滚动已列入集中验收。身份和清单见[最新集中验收候选](gui-acceptance-2026-10-31-main-prepared-latest.md)。

2026-10-31 当前主线后台回归复核（源码提交 `c4ff10034d0934168d3a3e6a6523b4004cb2650c`；随后仅有候选记录文档提交）：完整 Release `SettingsSmoke` 通过；App 与 NativeProbe Release build 均为 0 警告、0 错误；`--synthetic-shell-folder-tree-smoke` 通过嵌套枚举、递归大小/列值、PIDL、项目菜单/数据对象、多选 Copy/Move/Delete、同名冲突保留及重命名合同，结果明确为 `shell-view-created=False`。这些结果复核已实现后端与隔离 adapter，不证明 DefView/IShellView 可见逐行大小列、排序、真实 OLE 拖放或产品迁移；Explorer Dirs 产品仍使用 ExplorerBrowser，可见验收仍按上述候选集中进行。

2026-10-31 当前主线隔离行模型复测（记录提交 `76baf11`）：`--common-listview-relative-size-contract` 通过异步扫描、派生值/比例重算、数值排序、会话排序键及焦点行保持；`--shell-change-reconciliation` 通过 Shell 创建/更新/文件重命名/目录重命名/删除通知、稳定行身份、目录大小重扫与选区焦点保持。两个探针输出均确认窗口隐藏且前台未变。此证据仅覆盖标准 `SysListView32` 行模型，不创建 ExplorerBrowser/IShellView，也不覆盖产品 Q-Dir 列显示、列头排序交互、原生背景菜单或拖放。详细结果见[逐行大小架构调查](folder-size-column-investigation.md#2026-10-31当前主线重复运行行模型后台合同)。

2026-10-31 自有 ListView 的 Shell 菜单边界复测：`--common-listview-data-contract` 通过行 PIDL 经父 `IShellFolder::GetUIObjectOf` 查询项目菜单，canonical verb 有 `cut/copy/delete/rename/properties`；直接父 `IShellFolder::CreateViewObject` 的背景菜单仍没有查看/排序/分组/刷新。探针没有显示 Popup 或执行命令。后续自有文件视图迁移门槛必须补上 Explorer 视图级背景菜单与命令路由，不能用文件夹菜单替代；产品继续使用 ExplorerBrowser。详情见[Shell 菜单差异调查](folder-size-column-investigation.md#2026-10-31当前主线-shell-项目菜单与背景菜单差异复测)。

2026-10-31 递归目录树扫描取消边界：可见文件夹大小原型在进入递归扫描前会先物化需要返回的路径集合；现在集合构建逐项检查 CancellationToken，取消后不再继续枚举剩余目标。SettingsSmoke 先验证旧行为会在预取消时仍消费路径，再覆盖枚举中途取消；定向扫描 smoke、完整 Release SettingsSmoke、App 与 NativeProbe Release build 均通过（0 警告/错误）。此项仅减少隔离逐行大小原型在刷新/关闭后的无效准备工作，不接入产品 ExplorerBrowser，也未测量大型/网络目标的实际取消延迟。

2026-10-31 隔离逐行大小列复用单位格式：`CommonListViewColorBaseline` 的大小单元格不再固定输出字节数，而调用应用已有 `ByteSizeFormatter`，支持传入自动/固定单位、精度和千位分隔设置；尚未扫描的行保留空白，不显示虚假的零值。接线合同先在旧代码上失败，定向 smoke 覆盖自动 MB、字节千分位及未扫描状态通过。此项仅验证隔离 ListView 原型可复用格式策略；未连入用户配置或产品 ExplorerBrowser，Shell 列和可见 UI 仍未实现。

2026-10-31 Shell 行重枚举缓存校验：逐行大小原型不再把旧目录的递归字节数/计数带到同解析名的新文件、虚拟项或路径变化项；只有仍为同一路径的文件系统文件夹才复用旧扫描值，等待后台新扫描回写。NativeProbe Release build 通过（0 警告、0 错误）；未运行行模型 smoke 或创建 Shell view。此修正仍仅用于隔离原型。

同批追加：扫描回写结果不含当前文件夹时，清除旧递归大小/计数并显示未扫描状态，避免目录暂不可访问或在枚举/扫描间消失后继续显示过时值。NativeProbe Release build 通过；运行时边界未做 smoke 验证。

同批追加：隔离行模型可从显式 Shell 重命名映射保留被改名条目的 row ID，使自有行模型后续能按原身份恢复选择/焦点；路径变化时仍清空旧递归扫描值。更新了隐藏重命名探针断言和日志，NativeProbe Release build 通过，probe 未运行。

2026-10-31 隔离 `IShellView` OLE 拖放入口自管 COM apartment：`RunOleLoop` 之前只检查 STA，没有保证当前 STA 已执行 `OleInitialize`；目标注册调用刚好先初始化 OLE，令拖放源入口依赖隐式调用顺序。现由拖放循环入口初始化并在 `finally` 配平，初始化失败时不进入 `DoDragDrop`。无窗口行为合同覆盖 `S_OK`、`S_FALSE`、初始化失败不执行/不反初始化，以及拖放抛错仍清理；NativeProbe Release build、完整 SettingsSmoke 通过，0 警告/错误，`git diff --check` 通过。没有创建 `IShellView`、显示窗口或启动拖放循环。真实 OLE Copy/Move、取消、视图通知与前台隔离仍未验收；产品继续使用 ExplorerBrowser。

2026-10-31 F5/查看方式空白帧复核：两条操作都没有重建 ExplorerBrowser、窗格、标签或导航历史；F5 直接调用当前 `IShellView.Refresh()`，查看方式调用当前视图的 `IFolderView2.SetViewModeAndIconSize`，后者让 Shell 原地重排项目。移除 F5 期间额外的 `SetRedraw(FALSE)`，让 Shell 在重新枚举行项目时持续绘制；原有行数等待只用于安全恢复选区/焦点，不再冻结画面。查看方式路径保持原生切换并延后读取状态。Release 源码合同 smoke 和 App Release build 通过（0 警告/错误）；未打开 GUI，真实像素空白时长、慢目录和虚拟位置仍待集中验收。隔离计时的 48 ms/544 ms 仅为行数出现时间，不能代表画面效果。详见[刷新闪烁计划](superpowers/plans/2026-10-30-refresh-flicker-investigation.md)。

2026-10-31 集中验收候选已更新至提交 `6257656eb107b1b96dcec76c5bd1965327ad361b` 的四窗格隔离副本 `src/.state/gui-smoke/20261030-214436-370-cda2af`，45 项待可见验收，状态 `prepared-not-run`。它用于之后集中检查刷新、查看方式及其余待验收功能；准备过程未启动窗口，未触碰正式 profile。刷新验收现在确认 F5 原生绘制、不冻结旧帧，并检查重复刷新、搜索/虚拟位置和模式切换时的空白、焦点、选区、滚动与命中结果。身份、校验值和验收重点见[集中验收候选](gui-acceptance-2026-10-31-main-prepared-latest.md)。

2026-10-31 05:10 北京时间内存观察：当前仍运行的 temp.80 PID `47460` 进行 60.226 秒只读复采（13 点）；Working Set `223.4→223.4 MiB`、Private Bytes `115.2→115.1 MiB`，句柄 `1,114→1,109`、线程 `36→34`、GDI `95` 不变。该一分钟内进程级计数没有单调上升；未记录固定操作负载，不能据此排除浏览操作期间或长期运行的增长。身份、哈希和限制见[内存采样记录](memory-sampling.md#2026-10-31-0510-北京时间temp80-当前运行实例一分钟被动复采)。

2026-10-31 05:19 北京时间内存观察：同一 temp.80 PID `47460` 进行第二次 60 秒只读采样（13 点）；Working Set `223.43→223.41 MiB`、Private Bytes `115.11→115.06 MiB`，均轻微回落；另一个独立 12 秒补充样本句柄 `1104–1107`、线程 `33–34`、GDI `95`、USER `104`。两次窗口均没有固定用户负载，只能描述短时静置计数，不能推断操作期间或长期增长，更不能归因来源。身份、哈希和限制见[内存采样记录](memory-sampling.md#2026-10-31-0519-北京时间temp80-同一进程-60-秒只读复采)。

2026-10-31 NativeProbe 目录通知与派生行值联动：隐藏 Shell change probe 收到真实 `SHCNE_UPDATEDIR` 后重新枚举，再由 worker 重算该视图中受影响文件夹的递归大小和内容计数，并将值合并到稳定 Shell row ID；真实隐藏 `SysListView32` 读回 `12 B` 和 `files: 2, folders: 1`。夹具从 1 个文件、0 个子目录、4 B 变为 2 个文件、1 个子目录、12 B，目录行 ID 保持；初始/变更扫描均在线程池运行并回 owner Dispatcher 发布。Shell 行重排/删除后重建列表仍选中并聚焦原稳定行。探针 Release build/运行通过；前台未改变，列表父窗口不可见。此为隔离数据、通知和列表投影合同，未接入产品 ExplorerBrowser/IShellView，也未验证真实 UI 的 F5 无黑屏、局部刷新像素或 Q-Dir 等价行为。

2026-10-31 Q-Dir 目录树拖入悬停展开：`NativeShellTree` 初始化加入 Windows `NSTCS_SPRINGEXPAND`。Release SettingsSmoke 定向样例通过，确认该样式与 `NSTCS_DISABLEDRAGDROP`、`NSTCS_NOEDITLABELS` 同时存在；保持禁用树内重排和标签编辑。微软文档说明 `NSTCS_DISABLEDRAGDROP` 只禁止控件内部拖放，外部项目仍可拖入树控件，因此此次只补上原生悬停展开。当前使用 Windows 默认延时，不提供 Q-Dir 的自定义延时档位；真实文件拖入、目录落点及展开时长仍待集中 UI 验收。[功能矩阵](shell-parity.md#本轮代码与下一轮验收)。

2026-10-31 目录树悬停展开更正：复核 Windows SDK 的 `_NSTCSTYLE` 文档后发现，`NSTCS_SPRINGEXPAND` 表示切换所选节点时自动折叠先前展开的节点，与拖放悬停计时无关；上方旧记录及功能矩阵把它误判成拖入展开。已从 `NativeShellTree` 样式中移除，并将回归改为拒绝该标志。Q-Dir 官方 FAQ 74 和选项清单确认其独立“Auto-Expand Drag over Time”选项，提供 0.2、0.5、1、1.5、2、3 秒；本项目真实拖入悬停展开与延时设置仍未实现。下一步需评估如何在不丢失 Namespace Tree Shell drop target 的情况下拦截拖入并延迟展开，再实现可取消、按控件生命周期清理的定时行为；不修改系统级鼠标设置。源码级验证通过不代表真实拖放已验收。

2026-10-31 目录树拖入悬停展开接入：Windows SDK 提供的 `INameSpaceTreeControlDropHandler` 可由现有 `TreeAdvise` 事件宿主实现；拖入/悬停回调直接给出鼠标下 Shell 项，无需替换 `IDropTarget`。当前实现对文件夹启动 1 秒 STA Dispatcher 计时；悬停换项、离开、放置、树导航或释放会取消，计时到期后使用原 `IShellItem` 引用调用 `SetItemState(EXPANDED)`，避免重新解析导致虚拟 Shell 位置丢失；Drop 回调返回 `E_NOTIMPL` 且不改写效果，表达本回调未接管文件操作。Release SettingsSmoke 覆盖回调项传递、效果不变、离开/放置清理和不接管 Drop；App 与 NativeProbe Release build 通过（均 0 警告/错误）。仍需在真实 Windows Shell 拖入场景确认 `TreeAdvise` 回调、默认文件操作、目标目录和延时视觉行为；目前延时固定 1 秒，Q-Dir 的 0.2/0.5/1/1.5/2/3 秒选项未实现。路线图及[竞品调研](competitive-research.md#2026-10-31-官方功能清单细化)已同步该边界。

2026-10-31 目录树拖入展开延时档位：目录树菜单新增 0.2、0.5、1、1.5、2、3 秒选项，默认维持原来的 1 秒；设置保存到独立 `NavigationTreeDragHoverDelay.ini`，已有共享树/逐窗格树立即更新，正在计时的悬停重新开始计时。配置支持外部修改冲突保护、格式损坏备份及未来版本不覆盖。Release 定向 `--shell-tree-control-style-smoke` 覆盖回调效果不变、延时六档往返、非法档拒绝和未来版本不覆盖；App 与 NativeProbe Release build 均为 0 警告/错误。完整 Release SettingsSmoke 本轮未通过：既有 `EnvironmentChangeMessageSmoke` 的隐藏消息目标前台不变断言失败；保留失败结果，不放宽断言。真实 `DoDragDrop`、可见菜单和默认放置行为仍待集中 UI 验收；专项配置 smoke 不代表 Shell 拖放已通过。

2026-10-31 F5 绘制租约收窄（中间方案，已撤回）：当时曾让普通文件系统目录优先用 `SHCNE_UPDATEDIR`，并在退回 `IShellView.Refresh()` 时短暂调用 `IFolderView2.SetRedraw(FALSE)`。后续比较发现普通目录广播通知的行数可见时间更长；用户仍观察到明显空白后，产品最终回到所有位置直接调用当前 `IShellView.Refresh()`，移除产品绘制租约。此前关于通知成功/失败门控及租约范围的记录是历史试验，不应作为当前行为。详情见[刷新闪烁计划](superpowers/plans/2026-10-30-refresh-flicker-investigation.md)。

2026-10-31 04:12 北京时间内存观察：对仍运行的 temp.80 PID `47460` 做 60 秒只读复采（13 点），Working Set `223.19→223.15 MiB`、Private Bytes `114.84→114.75 MiB`，句柄 `1,112–1,114`、线程 `35–37`、GDI `95`、USER `105–107`；本窗口没有单调增长。未操作应用且没有固定负载，因此此样本只反映短时静置计数，不能排除用户操作期间或长期运行的增长，也不能归因来源；身份、哈希和完整限制见[内存采样记录](memory-sampling.md#2026-10-31-0412-北京时间temp80-当前运行实例一分钟被动复采)。

2026-10-31 后台回归复核：App Release 构建（0 警告/错误）、完整 Release SettingsSmoke 和 `--static-shell-column-folder-smoke` 均通过。原生适配器无窗口合同再次确认列标题、`UInt64` 值及 `2/10/10/100` 数值比较正确；输出明确 `window-created=False`。这不改变 DefView 未读取自定义派生列的结论，也不代表产品列、Shell view 或可见交互通过。

2026-10-31 原生 `IShellView` 菜单排序合同：Private Desktop 隔离探针现验证背景菜单的名称升/降序确实重排三条原生 Shell 行（`aardvark,child,keep.txt` ↔ `keep.txt,child,aardvark`），而不只检查 `GetSortColumns` 状态；同轮查看方式、刷新、分组菜单、列标题菜单和排序后焦点恢复通过，前台及输入状态未变。该合同补足原生菜单命令与行序证据，不代表 ExplorerBrowser 产品接线、可见体验或 Q-Dir 验收。

2026-10-31 原生导航历史 PIDL 生命周期合同：新增 `--ishellview-native-pidl-lifetime`，使用真实 Windows Shell PIDL 驱动隔离 `ShellViewNavigationHistory`，完成 201 个停靠点、400 次后退/前进、前进分支截断、失败导航临时 PIDL 回收及 `SBSP_CREATENOHISTORY` 原位替换。探针跟踪历史对象拥有的每个 PIDL，结束时 `allocated=204/released=204`、无存活分配；Release 构建及运行通过，不创建 Shell view 窗口，也不改变前台。它只证明历史类的 PIDL 所有权，不覆盖实际 `IShellView` 窗口/COM 资源长跑、ExplorerBrowser 产品宿主或可见交互。

2026-10-31 F5/查看方式刷新边界再次核对：F5 对现有 `IShellView` 调用 `Refresh()`，不会重建 ExplorerBrowser、标签、窗格或导航历史；这是 Explorer 的标准 F5 契约。查看方式只对同一 `IFolderView2` 调用 `SetViewModeAndIconSize`，Shell 仍须原地重新布局和绘制项目。当前产品没有暂停原生重绘。普通文件夹的 `SHCNE_UPDATEDIR` 曾在隔离本机样本中保持列表行，但新行可见时间约 544 ms，对比 `IShellView.Refresh()` 约 48 ms；这是行数采样而非像素数据，且系统通知会分发给其他 Shell 接收方，因此没有切回通知路径。公开 Shell API 没有承诺 F5 后保留旧像素帧的接口；白屏时长、慢目录下画面和输入命中仍留给集中可见验收。调查计划的旧阶段描述已更正，见[刷新闪烁调查](superpowers/plans/2026-10-30-refresh-flicker-investigation.md)。

2026-10-31 原生 `IShellView` 生命周期资源采样：Private Desktop 隔离探针预热 5 轮后创建/销毁 200 个完整 stock 视图；每轮四项均枚举成功，`DestroyViewWindow` 后没有残留子 HWND，GDI/USER 计数不增长。两次测量中，第 20 至第 200 轮的进程句柄均增加 180；句柄类型快照显示这 180 个均指向 NativeProbe 自身 PID，最后强制 GC 后仍在。新增句柄值/访问掩码采样确认首末快照间的 180 个句柄访问掩码均为 `0x1410`；样本句柄值不同，不能按单个句柄误认为重复引用。Private Bytes 在两轮末端分别比首样高约 2.7 MiB 和 2.9 MiB，中间有回落，不能据此认定持续线性泄漏。复用单个 `IShellBrowser` 的 60 轮 A/B 仍约每轮增加一个同类句柄；对象级对照只做 200 次 `IShellFolder::CreateViewObject`/Release、不创建窗口时，仅增 12 个混合类型句柄，没有 Process 句柄。追加单轮分阶段探针后，`CreateViewObject` 和取得宿主接口时句柄数不变；`CreateViewWindow` 后总句柄 +95；等待文件枚举完成时总句柄 +187，并首次观察到 4 个访问掩码均为 `0x1410` 的自进程 Process 句柄；`DestroyViewWindow`、消息泵稳定、释放 `IShellView`/宿主接口后这 4 个仍在。该阶段关联不能证明由 Shell view 直接创建，也不能解释重复生命周期中的 +180。总句柄差值还受 Shell/运行时初始化影响。当前尚不能区分 Shell/COM 依赖组件保留与未释放资源；结果不代表 ExplorerBrowser 产品宿主。Process Handle Tracing 隔离试验已启用，但栈帧未返回；枚举完成的第一时间复制原始记录后，仍见 ChangeNotify 线程（`shcore.dll+0x43930`）OPEN，以及探针主线程 CLOSE/OPEN。由于事件只记录数值句柄、槽位可复用，且已知 `OpenProcess` 控制也没有栈帧，线程记录不足以归属最终存活对象。句柄类型扫描已避免 `Process.GetProcessById` 额外开关句柄；Release 构建和两次 Private Desktop 阶段探针通过，前台及输入未变。下一步改用带内核句柄事件/栈的 ETW 对照同宿主无枚举与完整枚举路径；若系统未提供可用栈，再回到基于句柄对象生命周期的差分实验。以上仍是隔离原生探针，不代表 ExplorerBrowser 产品宿主，诊断代码不接入产品。

2026-10-31 ETW 句柄追踪权限检查：本机 WPR `Handle` 配置存在，但普通权限执行 `wpr -start Handle -filemode` 返回 `0x80070005 Access is denied`；记录未启动，没有 ETL，也没有更改 WPR 状态。仅在可用的授权管理环境下再考虑该系统级追踪，不把权限拒绝当成 ETW 能力结论。

2026-10-31 Q-Dir 官方功能差距补查（更正）：FAQ 112 将“Soft-Refresh”称为“Disable auto sorting when rename or copy items”的旧名，FAQ 139 描述的是同一菜单开关；语言表保留两个字符串 ID 不能证明有两个同时存在的选项。Windows `FWF_NOENUMREFRESH` 明确阻止刷新时重新枚举/丢弃当前内容，是本项目现有 Soft-Refresh 开关的合理 Shell 对应，但复制、拖放、重命名时的真实排序与内容时序仍待 Q-Dir/本项目可见对照。默认行为不改，继续保留 F5 新鲜度提示。详见[竞品调研](competitive-research.md#2026-10-31-官方-soft-refresh-行为补查)与[功能对照](shell-parity.md)。

2026-10-31 Soft-Refresh 原生开关实验：新增默认关闭的全局便携选项，只设置 `FWF_NOENUMREFRESH`，并将该设置路由至当前及新建 ExplorerBrowser Shell 视图。Windows 契约明确为刷新时不重枚举/不丢弃当前内容；Q-Dir FAQ 112/139 将同一 Soft-Refresh 选项描述为复制、拖放、刷新或重命名后不自动重排。菜单与功能对照现按此对应关系表述，仍不把源码/flag 合同当作可见等价证明。菜单提示新项目可能暂时不可见，关闭后需按 F5 完整重读。标志位、INI 持久化和源码路由有 Release SettingsSmoke/构建检查；实际 F5、外部变化及复制/拖放/重命名后的内容与排序时序待集中 GUI 验收，默认保持关闭。

2026-10-31 02:07 CST：只读采样仍在运行的 `preview-0.6.0-temp.80` PID `47460` 5 分钟（61 点）。Working Set `220.61–220.70 MiB`，Private Bytes `115.30–115.48 MiB`，首末均轻微回落；GDI 全程 95。负载未固定，不能据此排除操作期间增长或定位来源。完整身份、哈希及边界见[内存采样记录](memory-sampling.md#2026-10-31-0207-北京时间temp80-当前运行实例五分钟只读采样)。

2026-10-31 02:15 CST：同一 PID 的 .NET Runtime counters 6 点采样显示最近一次 GC 报告堆约 `11.41–12.09 MiB`、GC committed `28.07 MiB`，同期进程 Private Bytes 约 `115.93 MiB`；只观察到一次 Gen 0。此数据说明 GC 堆不是进程提交量的全部，不能把余量直接归为 Shell/native 分配；分配速率仍需固定工作负载后再归因。详见[采样记录](memory-sampling.md#2026-10-31-0215-csttemp80-net-runtime-计数附加采样)。

2026-10-31 自有行模型接收有效扫描快照：扫描结果现按 filesystem path 更新原有 Shell 行的递归大小和内容计数；ID、PIDL、图标及顺序保持，虚拟 Shell 行和未包含在本轮扫描中的行不变。隐藏相对大小合同由行模型发布初始及变更后值，保留异步扫描、比例排序、选区/焦点身份；专项 smoke、完整 Release SettingsSmoke 和 App/NativeProbe Release 构建通过（0 警告/错误），前台不变。它仍只是隔离 SysListView32 数据合同，产品没有逐行派生列。纯行模型快照对账覆盖重新枚举后的重排、元数据更新、新增/移除、稳定 ID 与 PIDL 退休集合；NativeProbe 隐藏窗口的真实 Shell 通知合同通过 create/update-directory/rename/delete 通知及重新枚举。行状态映射合同覆盖多选/焦点身份，隐藏 `SysListView32` 投影合同完成新增/删除后列表重建及状态写回、读回；随后真实 stock `IShellView` fresh-view 探针通过完整多选恢复、一个过期选中 PIDL 的幸存焦点恢复，以及焦点本身未选中时保留幸存多选并恢复同一未选中焦点 PIDL。菜单排序实际行序、导航历史 PIDL 所有权合同及隔离 Shell view 生命周期资源采样现已补齐，详见上方记录；视图创建后出现的自进程 Process 句柄增长原因仍未确定。产品仍使用 ExplorerBrowser，且产品宿主/可见焦点与输入未验证。详见[架构调查](folder-size-column-investigation.md#2026-10-31-隐藏-syslistview32-写回重枚举后的选区焦点)与[自动刷新调查](live-update-investigation.md#2026-10-31-混合-readiness-的焦点恢复复核)。

2026-10-31 集中验收候选已刷新：干净主线 `0f83f3d7a98dfe02e57a447cf493c28cda7f81ce` 构建出 `src/.state/gui-smoke/20261030-170859-050-e4c757/`，manifest 为 `prepared-not-run`、41 项待验收、四窗格布局；EXE/DLL SHA-256 为 `50E39E514F87DFEFD9537FC20238DE12F7DBE7F1DF65FEAABE13BDDCCA3C3307` / `500431B5CF12BFA55DEE8720F16CEFDD333F824AA133758D2A31986BD505FE67`。准备脚本未启动应用、创建 ExplorerBrowser 或操作桌面。F5 保帧、绘制空白、选区/焦点/滚动及查看方式切换列入该批验收。候选说明见[2026-10-31 主线集中验收候选](gui-acceptance-2026-10-31-main-prepared-latest.md)。

2026-10-31 逐行大小列隔离原型：`--common-listview-relative-size-contract` 的两次生产目录树扫描现在线程池执行；STA 的 WPF Dispatcher 在等待期间继续泵消息，结果返回 owner thread 后再更新隐藏 ListView。原合同曾确认同步扫描运行在 owner STA，新增断言后先失败；实现后合同通过，扫描 worker/owner 分离，行数值、相对百分比、递归文件/子目录数、排序、项目身份/选区/焦点及前台不变均符合夹具。此结果只验证隔离行模型线程边界，不代表性能提升或产品逐行列；Explorer Dirs 仍使用 ExplorerBrowser。详情见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31-自有-listview-扫描移出-sta)。

2026-10-31 逐行大小扫描请求代际：NativeProbe 隔离层现用 `DirectoryTreeScanCoordinator` 管理扫描 ticket；新扫描取消上一轮，导航/视图退休和 owner dispose 会取消当前 worker，结果仅在 owner 验证 ticket 仍为当前代际后继续更新行。SettingsSmoke 的 worker 级取消/过期门控合同先因缺失协调器编译失败，再通过；相对大小 ListView 隐藏合同也通过，前台不变。该项只验证原型扫描生命周期，不是产品列接线或 UI 性能证据。详情见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31-隔离扫描代际与过期结果门控)。

2026-10-31 逐行大小列异步发布：扫描结果经 owner `SynchronizationContext` 回到原 STA 发布，并在发布时再次拒绝过期 ticket；隐藏 ListView 合同改由真实临时目录 `FileSystemWatcher` 事件触发更新，不再手动重扫。另用可控同步上下文验证了“旧结果已排队、随后新扫描开始、最后派发旧回调”的竞争窗口，旧回调没有发布；协调器 smoke 连续 5 轮通过。App/NativeProbe Release build 和隐藏相对大小数据合同通过，结果包含 `update-applied=True`，前台不变且 HWND 隐藏。该原型没有接入产品 Shell 宿主，也不证明真实 ExplorerBrowser/IShellView 通知、实时列刷新、性能或 GUI 行为。详情见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31隔离扫描的异步发布与文件变化触发)。

2026-10-31 自有 ListView 虚拟 Shell 行合同：NativeProbe Release build 后运行 `--common-listview-virtual-folder-contract`，从虚拟控制面板枚举 9 个非文件系统 Shell 子项，将 PIDL、解析名和系统图标索引映射到隐藏 `SysListView32` 行并逐项读回；退出码 0，前台不变且窗口不可见。该结果只补强自有行模型的虚拟位置身份/显示接入，不覆盖项目操作、菜单、拖放、通知、导航输入、性能/内存或产品集成；Explorer Dirs 仍使用 ExplorerBrowser。详见[逐行大小列架构调查](folder-size-column-investigation.md#2026-10-31虚拟-shell-项目进入自有-listview-行模型)。

2026-10-31 NativeProbe 文件系统与虚拟 Shell 行模型统一：两个隐藏合同现共用 `ShellListViewRow`，显式表达可空文件系统路径/递归大小及 PIDL、解析名、属性、枚举来源和系统图标索引。普通目录合同验证路径、filesystem 属性与递归值；虚拟控制面板合同验证无 filesystem 属性的项目不带路径/递归值。数据、虚拟目录和异步相对大小更新合同通过，前台不变且窗口不可见。统一的是行记录形状，尚未统一枚举器、操作管线、通知或产品视图；Explorer Dirs 继续使用 ExplorerBrowser。详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31文件系统与虚拟-shell-共用行数据模型)。

2026-10-31 NativeProbe 文件系统与虚拟 Shell 枚举入口统一：`ShellListViewEnumerator` 现统一 `IEnumIDList` 遍历、PIDL 组合/保留、身份读取和异常清理；递归大小通过文件系统专属回调提供，虚拟位置仍可无路径/大小。普通、虚拟、异步派生值三项隐藏合同及 Release build 通过，前台未变。仅为隔离架构原型，文件操作/通知/交互与产品视图尚未统一；Explorer Dirs 继续使用 ExplorerBrowser。详情见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31文件系统与虚拟-shell-共用枚举入口)。

2026-10-31 NativeProbe 虚拟 Shell 子文件夹导航：对 Control Panel 的 9 个非文件系统文件夹使用绝对 PIDL 逐个绑定子 `IShellFolder`，并通过共用枚举器读取下一层，共得到 72 个项目。NativeProbe Release build 与扩展后的虚拟合同通过，窗口不可见且前台未变。此证据限定于本机 Control Panel provider；未覆盖所有虚拟 provider 的绑定、项目打开/操作或 Explorer Dirs 产品接线。详情见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31虚拟-shell-文件夹的下一层绑定与枚举)。

2026-10-30 自有 ListView 递归列扫描内存：扫描器使用惰性深度优先枚举器帧，活动枚举器数量随目录深度而非同级子项数增长；子树完成后立即累计并关闭，取消时 finally 释放活动帧。5,000 个同级目录夹具中，完整结果与 4 条指定结果的托管分配分别为 4,052,080 B / 3,361,544 B，减少 690,536 B（17.0%）；强制 GC 后存活托管堆估算分别为 1,755,912 B / 1,288 B；扫描期间采样峰值估算为 1,822,952 B / 69,064 B。最新单轮耗时 741/491 ms，不作为速度结论。以上仅衡量托管堆，不能外推 Private Bytes、工作集或 ExplorerBrowser 的实际节省。递归扫描分配诊断、取消/句柄清理 smoke、完整 Release SettingsSmoke 和 App Release 构建通过；原型仍未接入产品逐行列。详情见[逐行大小列调查](folder-size-column-investigation.md)。

2026-10-30 集中验收候选（前一候选，已被 10-31 刷新）：在干净主线提交 `b6756b0b52f66348fdaffd6d776c52dafaae6626` 上运行 `GuiSmoke -PrepareOnly`，生成 `src/.state/gui-smoke/20261030-155458-008-c5b040/`。`preparation.json` 标记 `prepared-not-run`、41 项待验收、`SourceWorkingTreeDirty=false`；BuildIdentity 为 `0.6.0-preview.20261030-155458-008-c5b040+b6756b0b52f66348fdaffd6d776c52dafaae6626`，EXE/DLL SHA-256 为 `6189533D0DF072EF6D8D7C257FB31B672E39D703E28E1EA784D172A202F76E45` / `333A5AAB195A6D3C0D960E5E69705B9F77750729BB3CE1C722552411998C26EC`，与 manifest 一致。F5 旧帧保持、最终重绘、选区焦点和滚动位置验收已列入清单；准备过程未启动窗口、创建 ExplorerBrowser 或发送输入。完整身份见[候选清单](gui-acceptance-2026-10-30-main-prepared-latest.md)。

2026-10-30 预览发布 `preview-0.6.0-temp.81`：从干净主线提交 `52358ace42d9008022a1f4858568101aab81ec98` 发布，提交时间 `2026-10-30T23:43:30+08:00`，`BuildTags=none`，包内独立 profile。EXE/DLL SHA-256 为 `516E09919543B5EC06D65E1339C8BC5EF97893B09C16066E385F0176FDDE8694` / `29C3EE4719F2444C7F62C7BEE7A262C8161D019056E710D51E86119F7B491532`，与 `app/SHA256.txt` 一致；PowerShell 7 发布隔离 smoke 通过。未启动 GUI；F5/查看方式的可见空白、选区、焦点和滚动位置仍待集中验收。版本说明见[preview-0.6.0-temp.81](releases/preview-0.6.0-temp.81.md)。

2026-10-31 预览发布 `preview-0.6.0-temp.82`：从干净主线提交 `c1694164d5f94afb8bc3d3027025b10381ee76d6` 发布，提交时间 `2026-10-31T00:03:39+08:00`，`BuildTags=none`，包内独立 profile。EXE/DLL SHA-256 为 `716028B545A5354C4E617C95B89B6B00E74836AE11DC3CE48562428639164680` / `683FD107F88430AD648E0EA0EE33BB42BC22E7A963BEDF32F8D8BE09A68433B0`，与 `app/SHA256.txt` 一致；程序集元数据确认源码状态为 clean。未启动 GUI；F5 保帧候选的可见改善、点击/键盘命中一致性和查看方式切换空白仍待集中验收。版本说明见[preview-0.6.0-temp.82](releases/preview-0.6.0-temp.82.md)。

2026-10-31 预览发布 `preview-0.6.0-temp.83`：从干净主线提交 `63bc718313b764762c093c3a41b1b158a959ac6c` 发布，`BuildTags=none`，使用独立 profile。EXE/DLL SHA-256 为 `398F89C764A16A7C56CE3328DE9D1C9F386EAEC0717D78FA1C72EB98F1E379D0` / `1AB654E54E39C72DFF5C41D5146367AF83B697B47E41AAF3773987752BFD4254`，与 `app/SHA256.txt` 一致；程序集元数据确认源码状态为 clean。未启动 GUI；F5 视觉与交互、查看方式切换空白仍待集中验收。版本说明见[preview-0.6.0-temp.83](releases/preview-0.6.0-temp.83.md)。

2026-10-31 预览发布 `preview-0.6.0-temp.84`：从干净主线提交 `fc8d5e4cfd03086e88d37d568dc1835f6ad8cd52` 发布，提交时间 `2026-10-31T00:14:58+08:00`，`BuildTags=none`，使用独立 profile。EXE/DLL SHA-256 为 `393EB05C1317FD504A923E1690AB9883C0A269656BA47449BADD2DB0C16AB80B` / `EAEB6758B41089C287F0D6B319B2E6371E5A010A0F601EDCECDFF77D3EFFF37F`，与 `app/SHA256.txt` 一致；程序集元数据确认源码状态为 clean。未启动 GUI；F5 可见效果和输入命中、查看方式切换后的重绘/焦点仍待集中验收。版本说明见[preview-0.6.0-temp.84](releases/preview-0.6.0-temp.84.md)。

2026-10-31 F5 保帧范围收紧：源码追踪发现 `ExplorerBrowserHost.Refresh` 同时服务用户手动刷新、目录监视通知、筛选/分组及搜索返回；此前保帧租约套在公共刷新执行体，可能把自动更新的旧画面也冻结。现改为仅 F5、工具栏和窗格刷新按钮显式请求保帧，其余内部刷新继续由 Shell 即时绘制。App Release build 通过（0 警告/错误）；未运行 GUI，F5 的视觉改善及冻结期间输入命中仍待集中验收。

2026-10-31 查看方式切换后的焦点恢复减少重复选择：源码追踪发现工具栏/菜单切换模式后若焦点需回到文件视图，`FocusView` 会调用 `RestoreFocusedItem`，该方法即使 Shell 已报告目标项为焦点仍无条件再次 `SelectItem`。现在只有焦点缺失或落在其它项时才重设，保留真实焦点/选区状态并避免重复 Shell 选择写入。App Release build 通过（0 警告/错误）；未运行测试或 GUI，实际重绘减少量与焦点体验待集中验收。

2026-10-30 文件列表刷新重绘（新候选，待可见验收）：F5 仍只调用当前 `IShellView.Refresh()`，没有重建 ExplorerBrowser、窗格或标签；查看方式切换只对当前 `IFolderView2` 调用 `SetViewModeAndIconSize`，不调用 Refresh 或重新导航。F5 在 Shell 重新填充列表期间暂缓原生列表重绘，待行数连续稳定或 Dispatcher 首次检查到 1.5 秒阈值后恢复；主线程被同步 Shell 调用阻塞时，该阈值不是墙钟硬上限。此 API 只控制绘制，不保证禁用输入，旧画面与当前命中项可能短暂错位，因此验收必须检查点击/键盘的目标正确性；不通过则撤回此候选。不会对子控件调用 `RedrawWindow(...ALLCHILDREN)`。查看方式切换继续让 Shell 自行重排，完整状态读取延后执行。黑屏和交互安全均未可见验证；候选与证据见[刷新闪烁计划](superpowers/plans/2026-10-30-refresh-flicker-investigation.md)。

2026-10-31 强调色更新避免重复原生主题刷新：`UISettings.ColorValuesChanged` 中即使只变化 Windows 强调色，应用也会重新应用同一 Win32 Preferred App Mode 并刷新原生菜单主题。现在独立跟踪原生深浅模式和高对比度状态；Accent-only 更新仍替换 WPF 语义画刷、通知窗口更新，但跳过原生 `SetProcessTheme`。重复高对比度通知不再清除/重建相同系统资源。浅/深模式或高对比度改变时继续刷新原生主题。App Release build 与可见主题切换尚待验证；未宣称高对比度/Windows 运行时事件通过。

2026-10-30 列表重绘让路：F5 的 `IShellView.Refresh()` 与查看方式的 `SetViewModeAndIconSize` 都保留在当前 Shell 视图内；两者之后的同步状态读取、行数检查和选区/焦点恢复改为 `DispatcherPriority.ContextIdle`，让排队中的 Shell 绘制消息先有机会处理。App Release build 通过（0 警告/错误），未运行测试或 UI。该调整只减少应用收尾工作抢占首次绘制的机会，不改变 Shell 自身的重枚举/重排，也不证明黑屏时段已缩短；F5 与各查看方式的视觉时长留待集中验收。完整 Shell 视图重建不在这两条调用路径中。

2026-10-30 逐行递归大小列公开排序接口与会话恢复复核：Microsoft 对 `IShellFolder2::GetDefaultColumn` 的合同明确，返回的列索引会作为 `IShellFolder::CompareIDs` 的 `lParam`；自有 namespace 的隐藏 DefView 表头点击曾按递归值升降序，但 ExplorerBrowser 的 `IFolderView2::SetSortColumns` 不能恢复未注册的应用私有列键（隔离样本返回 `TYPE_E_ELEMENTNOTFOUND`）。后续普通 `SysListView32` 原型已将自有排序 `PropertyKeyState` 与方向通过现有 `SessionStore` 的 Render/ParseComplete 往返，并在每次重排后按稳定 Shell row ID 恢复选择和焦点；隐藏合同确认升序、降序、再次升序及目录大小改变后排序状态正确。该结果完成的是“应用自有行模型的排序状态保存”这一模型门槛，不代表 ExplorerBrowser 可恢复该列，也不证明 Q-Dir 式完整 Shell 视图体验。产品继续使用 ExplorerBrowser；隔离迁移仍需补齐自有行视图的查看/排序/分组/刷新菜单语义、文件操作、拖放、通知、导航、键盘、可见绘制、性能/内存和集中交互验收后再作产品决策。[GetDefaultColumn](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdefaultcolumn) · [CompareIDs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids) · [派生大小列调查](folder-size-column-investigation.md#2026-10-30-隔离-listview-自定义排序会话恢复)

2026-10-30 自定义列排序恢复合同补充：Microsoft `IShellFolder2::GetDefaultColumn` 只给文件夹视图一个默认排序列索引；它把该索引传给 `CompareIDs`，没有每标签排序方向或用户状态保存语义。旧 `IShellFolderView::Rearrange` 虽可接受列序号，但 Microsoft 标注其可能变更或不可用，不作产品恢复路径。`IFolderView2::SetSortColumns` 使用 `SORTCOLUMN` 属性键数组；未注册的应用私有列键在隔离原型中返回 `TYPE_E_ELEMENTNOTFOUND`。源码复核更正：`SyntheticShellSortProbe` 仅在 `customSizeColumn=false` 时用 `SetSortColumns(PKEY_Size)` 排序；显示自定义“Recursive Size”列时实际通过合成列头点击测试升/降序。之后在自定义列映射下直接尝试 `SetSortColumns(PKEY_Size)`，原生调用返回 `0x8002802B (TYPE_E_ELEMENTNOTFOUND)`，所以系统大小键不能恢复该自定义列排序；该失败探针在销毁隐藏父窗口时又超时，前台与输入基线未见改变，但本次隔离清理没有完成。一次性探针源码已撤回，未接入产品。该运行的两个临时 fixture 目录及仅属于该运行的 CLSID/Explorer Desktop NameSpace 注册项仍留在用户配置中；自动安全审查拒绝了递归目录和注册项删除，本轮不再尝试绕过。后续应先评估自有宿主能否维护排序状态，不重跑此清理未完成的探针，也不注册机器级属性 schema。[GetDefaultColumn](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdefaultcolumn) · [SetSortColumns](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setsortcolumns) · [Rearrange](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shobjidl_core-ishellfolderview-rearrange)

2026-10-30 查看方式重复应用去重：`SetViewStyle` 现在先读取当前 Shell 模式和图标尺寸；如果与请求完全相同，就跳过重复 `SetViewModeAndIconSize`，仍按原流程更新本地状态缓存并延后捕获完整视图状态。读取失败时保留原 setter 路径。App Release 构建通过（0 警告/错误）。该改动只避免“再次选择当前查看方式”的冗余原生布局写入；真实模式切换的黑屏/重排及 F5 的列表空白未解决，仍需集中 UI 验收分别计时。

2026-10-30 查看方式切换减少重复焦点激活：检查调用链发现 Ctrl+Shift 及 Ctrl+滚轮从 Shell 文件视图触发后仍立即运行 `FocusCurrentView()`，再次执行 `IShellView.UIActivate`、查询/恢复 focused item 和可能的 UIA focus。现在仅在切换前焦点不属于当前 native 文件视图时执行恢复；工具栏/菜单切换仍恢复到文件区。App Release 构建通过（0 警告/错误）；未运行测试或 GUI，因此这只是移除已确认的重复工作，不代表视觉闪烁已解决。待集中验收核对快捷键/滚轮不丢焦点，菜单操作仍回到文件区，并观察列表重排是否减少闪烁。

2026-10-30 刷新后的焦点恢复避免重复聚焦：`ShellViewSelectionSnapshot.Restore` 已先核对当前选区，但仍会对快照焦点项无条件调用 `IShellView.SelectItem(SVSI_FOCUSED)`。现在先用其相对 PIDL 核对 Shell 当前 focused item；只有焦点未保留时才发送恢复命令，原有失败验证与选中项回退不变。App Release 构建通过（0 警告/错误）；未运行测试或 GUI。该改动减少刷新完成后的重复焦点命令，不代表 Shell 初始重枚举黑屏已解决，需集中验收选区/焦点和重绘。

2026-10-30 F5/查看方式验收准备：GuiSmoke 清单新增已填充目录 F5、外部文件创建、行区空白/黑屏、列表框架稳定、查看方式重排及选择/焦点/滚动位置检查。准备时发现脚本把自定义身份传给 `InformationalVersion`，但项目构建目标会依据 `Version + GitCommit` 重设该值，导致产物身份校验失败；改为只为隔离候选覆写合法 SemVer `Version`，并保留真实 Git commit 后，已在干净提交 `da0eff3` 上成功生成 `src/.state/gui-smoke/20261030-135148-367-fa7b4a/`。manifest 为 `prepared-not-run`、`SourceWorkingTreeDirty=false`、41 项待验收；EXE SHA-256=`CF13B170C0F3DFC4733C3C2EBE62C731F189ED9A1A02F34034DBD58F28AD51F5`。准备过程未启动窗口。视觉效果仍须后续集中 GUI 验收。

2026-10-30 逐行派生列接口复核：Microsoft 文档只承诺 `SHCOLSTATE_ONBYDEFAULT` 是 Explorer Details view 的默认展示提示；`IColumnManager.SetColumns` 设置当前视图列序且会清除列状态。隔离探针的通知后 `Apply` 虽返回 `S_OK`，自定义大小列仍不可见且 provider 未收到值请求，故不把 HRESULT 当成恢复成功。当前读数不能解释为单一缺陷；停止重复通用列重放/行重绘尝试。下一步若继续需在新视图记录 `GetDetailsOf`、默认列状态、SCID 映射及 `CM_ENUM_ALL` 列键的完整接口链，否则先保持产品未接线。只读复核发现两个已知测试 CLSID 及 Desktop NameSpace 项仍存在，因此不启动会添加注册项的 Private Desktop 实验。详情见[派生列接口复核](folder-size-column-investigation.md#2026-10-30自定义列可见性合同复核)。

2026-10-30 克隆历史容量调整：用户确认保留每标签最多 100 个导航位置，以控制长期会话的历史/选区快照内存；超出时淘汰最旧位置，克隆只复制当前保留窗口。完整 Release SettingsSmoke 现通过：120 次导航后历史正好保留 `C:\21`～`C:\120`，克隆可独立回退到最旧保留站，源历史仍停在 `C:\120`。这是模型级容量/克隆验证，不创建 ExplorerBrowser，实际连续导航、界面回退体验仍待集中验收。

2026-10-30 逐行大小列隔离复验尝试：运行不注册 CLSID 的 Strict IShellFolder Private Desktop 适配器探针；前台 HWND 未变，但系统输入 tick 改变，守护标记 `isolation-clean=False`。自定义列仍无逐项取值/比较回调并以 exit 1 退出，标准 `PKEY_Size` 排序通过；因隔离条件不完整且未提供新信息，不计为干净复验，不据此调整方向或重跑。未注册 property schema、未接入产品；详见[逐行大小调查](folder-size-column-investigation.md)。

2026-10-30 14:22 UTC：再次对仍运行的正式预览 temp.78 PID `19580` 做同一 PID 60 秒被动采样，未启动、重启或操作窗口。13 个样本的 Working Set 为 `361.69–367.72 MiB`，Private Bytes 为 `178.79–183.09 MiB`；句柄 `1,375–1,456`、线程 `71–91`、GDI `1,205–1,240`、USER `157–179`。操作负载未固定，首末变化不能作为泄漏证据或来源归因；原始 CSV 与限制说明见[内存采样记录](memory-sampling.md#2026-10-30-1422-北京时间temp78-当前运行实例一分钟只读复采)。

2026-10-30 06:06 UTC：从干净主线提交 `290d05a0a455618abcf15dd57a70d21e0d8a7412` 重新准备四窗格集中验收副本 `src/.state/gui-smoke/20261030-060610-762-f18303/`。manifest 标记 `prepared-not-run`、40 项待验收、`SourceWorkingTreeDirty=false`；EXE/DLL 哈希已记录。仅构建并准备隔离副本，没有启动窗口、创建 ExplorerBrowser 或操作桌面。用户已确认克隆视图为独立于复制标签的命令；此副本验收清单覆盖其历史与选区路径。候选详情见[最新验收候选](gui-acceptance-2026-10-30-main-prepared-latest.md)。

2026-10-30 06:08 UTC：对仍在运行的正式预览 temp.78 PID `19580` 做 60 秒被动采样，未启动、重启或操作窗口。13 个样本的 Working Set 为 `361.21–363.20 MiB`，Private Bytes 为 `178.31–180.67 MiB`（首末下降约 `2.36 MiB`）；句柄/线程只有小幅波动。负载未固定，不能据此判断泄漏或增长来源。原始 CSV 与完整身份见[内存采样记录](memory-sampling.md)；后续需配合固定操作阶段和托管堆计数。

2026-10-30：复核 Microsoft `IShellFolder2` 公共列接口定义，并与隔离适配器及有效通知 A/B 对照。当前 provider 的 `GetDetailsOf` 列文本、`MapColumnToSCID` 属性键、`GetDetailsEx` 初始数值和 `CompareIDs` 排序路径已匹配；有效刷新后 DefView 重枚举但不再请求递归大小列，IColumnManager 捕获也不含其属性键。公开接口没有承诺任意派生列在刷新后保留或重读，故不把问题归结为单一映射错误；产品列仍未实现，ExplorerBrowser 保持不变。下一步若继续该 P2 原型，先以单变量方式验证新视图的自定义列恢复合同；若无法成立，保留缺口评审并推进其它可落地差距。细节和 Microsoft 原文链接见[逐行大小调查](folder-size-column-investigation.md)。

2026-10-30 05:59 UTC：在提交 `6e9de3c4548e75b741398decb0cc29517a4dd1a0` 后，从干净主线刷新集中验收副本，路径 `src/.state/gui-smoke/20261030-055901-649-45914e/`，状态 `prepared-not-run`，40 项待可见验收。新增独立克隆视图的两站历史/选区夹具；候选哈希及完整清单见[最新候选记录](gui-acceptance-2026-10-30-main-prepared-latest.md)。准备仅构建并复制隔离副本，未启动 GUI、创建 ExplorerBrowser 或操作桌面。

2026-10-30 逐行大小更新探针校正：复核私有桌面保留日志时发现，`20261030-093242-358ea2af` 虽然进程正常返回，但通知前的整视图刷新与通用 `IColumnManager.Apply` 已移除递归大小列，实际通知步骤被跳过。因此该运行不算通知 A/B。更早两次 WPF 根运行重复确认初始递归大小显示与标题数值排序；下一项是让各通知策略从独立的新视图基线执行，并将列不可见判为前置条件失败。没有启动新视图或改变产品代码；细节见[逐行大小调查](folder-size-column-investigation.md)。

2026-10-30 通知探针控制流：默认策略改为单项 `SHCNE_UPDATEITEM`，`all` 不再可用；每种策略要求独立进程/新视图。探针从初始自定义列直接开始并将缺少列/错误顺序改为失败前置条件，不再先刷新和尝试用通用列管理器重建实验列。NativeProbe Release build 0 警告/错误；没有运行 Shell view，通知是否能更新值/排序仍未知。详情见[逐行大小调查](folder-size-column-investigation.md)。

2026-10-30 克隆历史容量（已被用户后续决定覆盖）：此前曾移除应用历史模型的 100 站截断，并验证 120 次导航回到最早位置；用户随后确认保留 100 位置上限，以限制长会话内存。实现与验证状态见上方最新记录。此前 unlimited 版本的验证结果不代表现行上限实现。

2026-10-30 克隆历史分配优化：克隆视图直接从历史对象复制列表容器，避免先生成 `Snapshot()` 数组再复制一次；逐站不可变选区仍共享。完整 Release SettingsSmoke 与 App Release build 通过；这减少克隆时的临时引用数组分配，没有常驻内存的 MB 实测，也未运行产品窗口。长会话的真实内存占用仍需固定负载采样。

2026-10-30 当前运行内存快照：只读采样正式预览 `temp.78` 的同一 PID 19580 约 1 分钟，Working Set 为 362.0→364.1 MiB、Private Bytes 为 179.7→181.6 MiB，句柄约 1407–1443、线程 77–90。未控制用户操作，不能据此判断泄漏或归因；后续应在固定窗格/标签/目录与操作序列下复采。详情见[内存采样记录](memory-sampling.md#2026-10-30temp78-当前运行实例一分钟快照)。

2026-10-30 集中验收候选更新：用户确认采用独立“克隆视图”命令，保留“复制标签”语义；源码和隐藏 HostProbe 已覆盖该分流、可重放历史、逐站选区及普通/虚拟/搜索位置。将当前干净主线 `14e6aa5cfc2f6fbc9a230211faf3a0270f8fa0fa` 重新执行 `GuiSmoke/run.ps1 -PrepareOnly`，准备 `src/.state/gui-smoke/20261030-052522-882-ebcdd5/`，39 项 `prepared-not-run`。只构建隔离副本和夹具，未启动窗口或创建 ExplorerBrowser。候选身份、SHA-256 与可见验收清单见[最新候选记录](gui-acceptance-2026-10-30-main-prepared-latest.md)；克隆逐站焦点、异步搜索结果和实际菜单操作仍待用户集中验收。

2026-10-30 相对大小比例原型文件变更合同：更新比例列实验，先等待临时目录中目标文件的 `FileSystemWatcher.Changed`，再重扫目录树、更新两个真实 Shell 行对应的数据并重排。10/14、4/14 变为 10/30、20/30 后，顺序从小目录在前改为大目录在前，原选中/项目焦点身份保持；新增通知/排序断言先失败，补齐后 Release NativeProbe 连续两轮通过，窗口不可见且前台未变。此处验证的是文件系统 watcher 与通用 ListView 数据模型，不是 Shell `SHChangeNotify` 或 ExplorerBrowser 的自定义行刷新；列标题输入、真实键盘焦点、菜单/拖放和 Q-Dir 显示精度仍待后续验证。详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-30-相对大小比例排序选区与文件变更合同)。

2026-10-30 克隆搜索选区探针修正与复验：首次 `--pane-tab-transfer` 在 18.8 秒后超时，原因是测试在调用克隆前等待 Shell 选择通知更新源历史；但生产克隆命令会同步捕获实时选区，并更新当前历史站点后再复制快照。探针已改为直接克隆后检查这一产品路径，随后完整 Release HostProbe 退出码 0：搜索 URI、克隆启动选区、每站历史身份和 Back/Forward 均通过，普通目录与“此电脑”用例也通过。搜索结果由探针直接调用 `IShellView.SelectItem`，所以此结果验证克隆时实时快照和 Shell PIDL 身份，不证明真实用户输入事件或可见焦点/异步恢复；这些仍留待集中 UI 验收。没有显示 GUI 窗口。

2026-10-30 Private Desktop 隔离判定修正：上一轮拖放探针曾报告运行中前台短暂变化、输入 tick 变化，但因结束时前台句柄恢复仍退出 0。探针监视现在逐轮采样全局输入 tick，并将任何已观察前台变化、结束前台不一致或输入变化统一判为隔离失败；纯策略 smoke 覆盖稳定通过、瞬时前台切换失败、输入重叠失败及结束前台不同失败。首轮 smoke 在策略缺失时按预期编译失败，补实现后 smoke 通过；NativeProbe Release build 0 警告/错误。没有重跑 Private Desktop/Shell view，因此不对桌面环境做新断言；先前拖放样本依旧不算通过隔离门槛。

2026-10-30 Private Desktop 拖放目标诊断：`--shell-view-drop-target-private-desktop` 退出码 0；真实 Shell `IDataObject` 通过直接 `IDropTarget` 调用完成目标窗格/文件夹的复制和移动场景，并核对源/目标文件状态，跨卷默认复制及 Shift+Move 也通过。该命令没有调用 `DoDragDrop`，且比较的是 `IShellView.GetItemObject`/`IShellFolder.CreateViewObject` 目标，不是自有 `SysListView32`。`IShellView.GetItemObject` 目标的 Copy 候选为 false；探针当前未把该候选列入失败条件。运行监视还观察到前台短暂由 Feishu 切换到 ChatGPT，输入 tick 同期变化，结束时前台句柄恢复原值；因此本轮不是通过前台隔离门槛的完整运行，后续不重跑 Private Desktop 拖放，直到明确解决前台/输入变动来源。真实 OLE 拖动和自有 ListView 接入仍未验证。

2026-10-30 主线后台回归与验收副本刷新：当前 `main` 上 Release SettingsSmoke、App Release build（0 警告/错误）及无 Shell view 的 NativeProbe 文件树 smoke 均通过；后者核对合成 Shell 文件夹的多选 Copy/Move/Delete、冲突改名与 PIDL/绑定合同。随后用 `GuiSmoke/run.ps1 -PrepareOnly` 基于干净提交 `103d15c6c94044834bc47323c07a43993ec91a44` 准备 `src/.state/gui-smoke/20261030-022742-972-3c2dad/`：39 项、`prepared-not-run`；EXE、App DLL、颜色/容量/程序启动器夹具哈希均与 manifest 一致。未启动窗口、创建 ExplorerBrowser 或操作共享桌面。克隆视图仍需可见验收搜索选区、焦点与逐站导航；当前候选身份及完整清单见[最新候选身份](gui-acceptance-2026-10-30-main-prepared-latest.md)。

2026-10-30 克隆视图“此电脑”选区隐藏 HostProbe：从真实 ExplorerBrowser 的 `shell:MyComputerFolder` 视图选择系统盘，克隆并执行应用级 Back/Forward，核对克隆每站保留系统盘绝对解析名；用生产 `ShellSelectionPidl.TryGetDirectChild` 获取相对 PIDL 并在克隆 `IShellView` 选择盘符。Release HostProbe 0 警告/错误，完整 PaneTabTransferProbe 连续两轮通过、前台未变。自动延迟恢复因隐藏宿主焦点门控未执行，其他虚拟 provider 与可见 UI 仍待验收；详见[核心操作对照](shell-parity.md)。

2026-10-30 克隆视图真实 Shell 历史复验：隐藏生产 HostProbe 通过真实 `PaneControl` / ExplorerBrowser 在 A/B 两目录间捕获不同选区，独立克隆后后退/前进并核对各站位置/选区快照；逐站用生产 Shell selection batch 应用于克隆 view，源标签历史和状态保持独立。Release HostProbe 通过、前台未变。宿主无活动 WPF `Window`，焦点保护按设计拦截自动选区恢复；本轮不能证明可见窗口的延迟恢复/焦点，搜索异步与虚拟 provider 未覆盖，留待集中验收。详细见[核心操作对照](shell-parity.md)。

2026-10-30 隔离 ListView 自定义排序会话恢复：普通 `SysListView32` 原型现用应用私有属性 key 表示递归相对大小列排序，并通过 `SessionStore.Render` / `ParseComplete` 做内存往返；排序 target 按保存方向执行升序/降序，重排后按稳定 Shell row ID 还原选中和项目焦点。NativeProbe Release build 0 警告/错误；随后隐藏 `--common-listview-relative-size-contract` 退出码 0，输出确认 session sort key/direction 往返、降序排序、改回升序及选中/焦点身份保持，窗口不可见且前台 HWND 未变。产品 ExplorerBrowser 仍不能用此 app-only key 调用 `IFolderView2::SetSortColumns`，未做产品接线或 UI 变更。

2026-10-30 逐行派生列架构评审：隔离 `IShellView` 初次可提供并排序自定义大小值，但刷新后 DefView 没有重取值；标准 `SysListView32` 隐藏数据合同已证明单元格更新、按新值排序和项目选区/焦点身份保持。若要继续逐行派生列，应先做单窗格隔离自有 ListView 原型，使用真实 Shell 行对象验证菜单、文件操作、拖放、通知、导航及焦点合同；不直接迁移 ExplorerBrowser。内存收益尚无证据，必须在同机同目录集及同操作序列下测量。无窗口 folder-tree smoke 现已核对自定义列的索引、映射 key 和显示/属性值一致；DefView 通知后为何不重取值仍未解释。具体路线和门槛见[逐行大小列调查](folder-size-column-investigation.md#2026-10-30视图架构与派生列可行性复核)。

2026-10-30 Q-Dir 标签组资料边界：官方英文/英国英文字符串表有“Tab Group / Move to new Tab Group / Copy to new Tab Group / Use Tab Groups”菜单标签；检索官方 FAQ 与版本记录仍未找到组作用域、跨窗格关系或保存规则。按项目规则不分析 Q-Dir 二进制；标签组维持 P2 待评估，在集中可见验收确认交互前不猜测菜单和 INI 模型。详见[核心操作对照](shell-parity.md)。

2026-10-30 克隆视图历史选区存储：导航历史中的每站选区只通过只读列表暴露，克隆历史对象复制位置容器并共享不可变选区存储，避免复制文件名引用数组。Release SettingsSmoke 验证共享存储且源视图后续更新不会影响克隆；完整 SettingsSmoke 与 App Release build 通过。没有运行正式 App，且未采集同 PID 前后数据；这减少的是托管引用数组和短期副本，不复制字符串，也没有可报告的 MB 节省。详细边界见[memory sampling 记录](memory-sampling.md)。

2026-10-30 最近收藏标题跨重启保存：Q-Dir 的“最近打开的收藏夹”标题模式原先只记录当前进程内最后显式打开的收藏路径。现将该路径写入当前窗口 session 旁的 `RecentFavorite.ini`，与 profile 全局标题显示模式分离，多个窗口各自恢复且不会互相覆盖；普通导航仍不更新此值。新增 Release SettingsSmoke 覆盖空状态、每窗口隔离、带等号路径往返。没有启动 GUI；重启后窗口标题实际显示仍待集中验收。

2026-10-30 克隆视图历史选区快照竞态：Dispatcher 延迟捕获 Shell 选区时，视图/路径未变化并不足以证明读取期间的选区仍是最新值。现捕获导航、视图与选区代次，并在 COM 读取前后核对当前位置与历史位置；任一代次或位置变化即丢弃旧快照，后续排队捕获可写入新状态。纯代次门控 smoke 先按预期编译失败，再覆盖选区变化、视图替换、历史位置变化及有效快照；完整 Release SettingsSmoke 与 App Release build 通过。未触发真实 COM 重入或启动 UI，重入时序和克隆后逐站选区仍需后续集中验收。

列值筛选菜单状态更新（同日）：列值菜单现分页显示，每页最多 20 项；完整排序值数组仍驻留至菜单关闭，Shell 枚举/排序成本没有消除，分页可见交互待验收。此前“菜单项数量与唯一值线性增长”的记录已被此实现取代。

集中验收清单补充：在 10,000 个不同名称的目录中从 Name 列筛选菜单翻到后续页，勾选值后翻页并返回，确认勾选状态/计数保持，再清除并核对全量回归。该项针对分页交互，不将 SettingsSmoke 的纯分页逻辑覆盖当作可见验收。

集中验收候选已刷新：从干净提交 `db5c8daf54e3106f31023760f92f61d6708ddd74` 执行 `run.ps1 -PrepareOnly`，生成 `src/.state/gui-smoke/20261030-005620-783-418f8b/`，39 项，manifest 为 `prepared-not-run` 且 `SourceWorkingTreeDirty=false`。EXE、App DLL 和颜色夹具的实际 SHA-256 与 manifest 完全匹配；10,000 个唯一名称的列筛选分页及最近收藏标题双窗口恢复均进入验收清单。仅准备隔离副本，未启动窗口或创建 ExplorerBrowser；清单、构建身份与哈希见[最新候选身份](gui-acceptance-2026-10-30-main-prepared-latest.md)。

集中验收候选再次刷新：从干净提交 `0bb2a1e1ae36961cbb3c753df7561e9cbfce3685` 执行 `run.ps1 -PrepareOnly`，生成 `src/.state/gui-smoke/20261030-012049-679-6e864e/`。manifest 为 `prepared-not-run`、`SourceWorkingTreeDirty=false`，39 项待验收；EXE、App DLL 和 Q-Dir 颜色夹具哈希已复读确认。清单保留独立“克隆视图”验收场景（含普通目录、搜索选区、逐站导航、前进分支截断及捕获期间状态变化），不替换“复制标签”现有语义。未启动窗口或创建 ExplorerBrowser；当前身份见[最新候选身份](gui-acceptance-2026-10-30-main-prepared-latest.md)。

2026-10-30 克隆视图快照一致性保护：克隆时 Shell 视图设置和选区分两次读取，COM 重入期间可能导航到另一位置。现捕获目录/搜索 URI、导航与视图代次、选区代次，并验证导航历史仍指向同一位置；任一值变化就拒绝生成不一致的克隆。定向 SettingsSmoke 覆盖保护接线与历史位置匹配；App Release build、完整 SettingsSmoke 通过。没有启动 ExplorerBrowser/GUI，COM 重入时序仍待隔离 HostProbe/集中验收。

2026-10-30 列值筛选菜单临时内存：捕获完成后把唯一列值集合从仍需保留的 Shell 捕获对象中移出；菜单排序直接合并当前已选值并生成最终数组，避免旧 HashSet、LINQ 去重链和菜单项在构建期间共同保留。列表语义（大小写不敏感去重、保留已选但当前不可见的值、区域排序）由 Release SettingsSmoke 覆盖，App Release build 0 警告/错误、完整 SettingsSmoke 退出码 0。没有创建 ExplorerBrowser/GUI，也未测量极大列值清单的实际 MB；菜单视觉项仍与唯一值数量线性增长，保留后续优化空间。

2026-10-30 克隆视图恢复搜索结果选区：克隆历史位置为 Shell 搜索时，按 `SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER` 读取当前结果并匹配保存的绝对解析名；选中前重新取得当前行 PIDL、核对其解析名，再通过 `IShellView.SelectItem(PIDL, …)` 按项目身份选中，避免异步插入结果后旧行号选中另一项目。等待所有快照项出现后再批量恢复；匹配采用流式枚举，全部命中即停止，条目数不变时最多每 5 次轮询重扫。解析名匹配、PIDL 身份校验、流式早停和请求门控 SettingsSmoke 通过；App Release build（0 警告/错误）与完整 SettingsSmoke 通过。没有启动 ExplorerBrowser/GUI，搜索 provider 的实际排序、结果加载时序和选区可见效果仍待集中验收。

2026-10-30 集中验收候选同步：从干净主线 `08ab2a82102ada97fc277b76095963544dc96193` 执行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`，生成 `src/.state/gui-smoke/20261030-004055-897-500aed/`，包含 39 项待验收清单，克隆视图普通目录/搜索结果选区、逐站导航和快照一致性列入可见检查；异步搜索结果行身份复核及列值菜单候选合并/排序由 SettingsSmoke 覆盖。manifest 中 `SourceWorkingTreeDirty=false`；EXE/DLL/颜色夹具哈希与候选身份见[最新候选身份](gui-acceptance-2026-10-30-main-prepared-latest.md)。准备过程未启动窗口、创建 ExplorerBrowser 或发送输入；候选状态为 `prepared-not-run`，等待后续集中验收。

2026-10-30 复制标签选区遇到已删除项目：复核复制标签恢复队列发现，选中项中任一路径在副本导航完成前失效，会使整批选区都不恢复。现跳过无法由 Shell 解析的失效项，仅在同目录内为仍存在的文件排队；所有项均失效时不启动空请求。隐藏生产 ExplorerBrowser HostProbe 先复现失败，再验证保留项被排队、失效项被排除及全失效不创建请求；完整 Release SettingsSmoke、App/HostProbe Release build（0 警告/错误）和 `git diff --check` 通过。没有启动 GUI 或触碰用户文件，实际删除竞态和可见多选仍待集中验收。

2026-10-30 复制标签差距表复核：前几批为克隆视图加入独立于“复制标签”的命令、可重放的逐位置导航历史、分支截断和每站选区；普通目录与虚拟直接子项使用相对 PIDL，搜索结果改用当前 Shell view 的索引恢复。该对照更新记录了实现边界与仍待真实 GUI 验收的 Shell travel log、虚拟 provider 和搜索结果加载时序；克隆过程不迁移原 ExplorerBrowser 宿主对象或其私有 Travel Log。

2026-10-30 复制标签恢复虚拟 Shell 选区：选区快照现在保留 Shell 项目的绝对解析名；克隆视图就绪后，只有能解析为当前 Shell 文件夹直接子 PIDL 的项目才交给现有批量选择与焦点保护流程，已删除、不可解析或非直接子项会跳过。SettingsSmoke 直接验证从“此电脑”解析系统盘的相对 PIDL，并拒绝非子项；完整 Release SettingsSmoke、App/HostProbe Release build（0 警告/错误）通过。没有启动 ExplorerBrowser 或 GUI，因此复制“此电脑”驱动器等虚拟项的实际选择仍待后续集中验收；Shell Back/Forward 历史差距仍开放。

2026-10-30 复制标签保留文件选区：同窗格复制与跨窗格复制现在捕获当前标签中可寻址的文件系统选区，并在副本完成导航后将选区恢复到新 Shell view。Release HostProbe 用临时目录中的两个文件验证源选区、克隆状态传递、生产批量 `IShellView.SelectItem` 调用及副本选区读回；移动标签原对象/HWND/Back 历史和关闭清理回归也通过。Release SettingsSmoke、App/HostProbe Release build 与 `git diff --check` 通过；隐藏窗口未激活且前台不变。虚拟 Shell 选项不纳入此恢复，实际复制标签键鼠和可见焦点仍待集中 GUI 验收。

2026-10-30 ExplorerBrowser 隐藏项目偏好修复：隐藏生产宿主在普通隐藏文件、系统隐藏文件夹具上先复现显式 Hide 仍列出普通隐藏项（Shell 列表计数 3，而预期 2）。根因是 `ShellBrowserSite.GetViewFlags` 在 `QuickFilterActive=false` 时设置 `NOINCLUDEITEM`，使 DefView 绕过已安装的 `IFolderFilter::ShouldShow`；显式 Show/Hide 现都启用 Shell folder-filter 回调，Hide 用 `SFGAO_HIDDEN` 排除普通隐藏项，GetEnumFlags 仍不请求 `SHCONTF_INCLUDESUPERHIDDEN`。隐藏 `PaneControl`/真实 ExplorerBrowser 宿主复验 Show=3、Hide=2、系统隐藏项始终不出现；`SettingsSmoke`、HostProbe 和 App Release build 通过。没有改 Windows 全局设置或显示窗口；外部切换后通知、其他 Shell provider 与可见菜单验收仍开放。

2026-10-30 文件夹入口安装事务回滚复验：此前的 smoke 只直接调用回滚 helper。本轮在注册表值写入边界注入一次异常，让 `Enable-ExplorerDirsFolderVerb` 完整执行创建、失败捕获与回滚；第二个注册项部分写入失败后两个新注册项均恢复为缺失，既有外部改值保护用例仍通过。`publish-smoke.ps1` 也通过，确认隔离预览包会携带此模块并能读取包内程序能力清单。受控异常不等同于真实注册表 ACL/系统写入故障；没有修改日常 `HKCU\Software\Classes`、启动 GUI 或产出真实 release。

2026-10-30 文件夹入口回滚保护：启用用户级文件夹入口时，若写入中途失败，回滚现在可清理由本次创建且只含本次写入值的部分键；若部分键含外部修改则保留并标记冲突。隔离注册表 smoke 直接覆盖完整部分状态的清理与外部改值保留；此前 ACL 故障发生于创建键之前，不能作为安装中途写失败的端到端复现。未修改日常 `HKCU\Software\Classes`，未运行 GUI 或发布包。

2026-10-30 文件夹入口首次写入失败边界：继续检查时发现，已创建但尚无任何值的叶键也可能由第一次注册表写入失败留下。回滚 helper 现在仅对本次跟踪创建的空叶键执行清理；新增回归先按预期失败再通过。该测试直接验证 helper 的空键行为，不模拟实际系统写入故障；未修改日常注册表、未运行 GUI 或发布包。

2026-10-30 Q-Dir 颜色元数据保真：导入 `.qdr` 时把 `useColor`、`useTreeColor`、`colorthema` 与 `[Filter2]` 数字行作为不透明数据保存到便携会话 v13，并在再导出的 `.qdr` 原序写回；颜色尚不接入 ExplorerBrowser 文件视图。规则行纳入扩展 BaseHash，手工修改后旧扩展不会覆盖源值。后台导入/启动/会话/导出回归覆盖中；真实 Q-Dir 打开重存与显示效果未验收。

2026-10-30 Q-Dir 树分隔字段往返保真：有效 `Tree1split`～`Tree4split` 原值现写入会话 v12，并随 `.qdr` 导出/导入原样往返；不猜单位、不映射为 Explorer Dirs DIP 宽度。字段纳入扩展 BaseHash；本程序树模式、逐窗格可见性或树宽度改变后清除原值。完整 Release SettingsSmoke 与 App Release build 通过；未启动 GUI，也未用 Q-Dir 打开重存文件，因此可见互操作仍待集中验收。

2026-10-30 键盘清单同步：源码的窗格焦点顺序和 `FocusTraversalSmoke` 已包含地址层级下拉及“此电脑”下拉，但键盘清单遗漏两项；现已按实际 Tab 顺序补齐文档。仅修正文档，不改变快捷键和焦点行为；真实控件焦点仍待集中 GUI 验收。

2026-10-30 Q-Dir 地址栏快捷键补齐：按官方 Q-Dir 4.51 帮助将 `Alt+S` 接到现有地址编辑/全选入口；`Ctrl+S` 仍保留给 Program Starter。定向地址快捷键 smoke 先按预期失败，接线后通过；完整 Release `SettingsSmoke` 退出码 0，App Release build 0 警告/错误。物理键盘焦点仍待集中 GUI 验收。[快捷键清单](keyboard-shortcuts.md)

2026-10-30 Q-Dir F7 地址层级菜单：按官方帮助将 `F7` 接到当前窗格的地址路径下拉菜单，复用已存在的祖先目录和常用 Shell 位置层级；`Shift+F7` 的新建文件夹绑定保持不变。定向 smoke 先按预期失败，接线后通过；新建文件夹快捷键回归、完整 Release `SettingsSmoke` 退出码 0、App Release build 0 警告/错误。真实菜单焦点与路径选择待集中 GUI 验收。[快捷键清单](keyboard-shortcuts.md)

2026-10-30 Q-Dir F8“此电脑”菜单：新增 `F8` 快捷键，在当前窗格弹出“此电脑”及系统当前逻辑驱动器入口；驱动器目标和图标复用 Shell 地址菜单能力，未修改 F7 路径层级菜单。定向快捷键和菜单数据 smoke 通过；真实菜单外观、焦点和键盘选择待集中 GUI 验收。[快捷键清单](keyboard-shortcuts.md)

2026-10-30 Q-Dir F8 按钮入口：在地址栏旁新增独立“此电脑”按钮，使用 Shell 系统图标并将菜单锚定到该按钮；Tab/Shift+Tab 顺序纳入新按钮。焦点矩阵扩至 2,048 种控件组合，按钮与菜单路由 smoke 和 App Release build 通过；按钮实际绘制、菜单定位及真实键盘焦点仍待集中 GUI 验收。[快捷键清单](keyboard-shortcuts.md)

2026-10-30 集中验收扩展：`src/ExplorerDirs.GuiSmoke/run.ps1` 增加 Alt+K 复制与 Alt+V 剪切专用临时文件，并把 F7 路径层级菜单、F8/“此电脑”按钮及 Tab 顺序加入可见验收步骤。脚本 PowerShell 语法解析通过；曾从含文档改动的工作区准备副本，该副本不作为干净基线，随后重新准备。

2026-10-30 Q-Dir Alt+V / Alt+K 文件操作别名：`Alt+V` 通过编辑菜单现有 Shell `cut` 动词剪切所选项目，供后续粘贴移动；`Alt+K` 通过 `copy` 动词复制所选项目。仅文件视图获得焦点时执行，地址/搜索文本和 Shell 重命名输入不截获；编辑菜单快捷键文字显示两个组合。Shell 命令路由、焦点门控、别名冲突修饰键 smoke 通过；真实键盘、剪贴板及后续粘贴文件操作待集中 GUI 验收。[快捷键清单](keyboard-shortcuts.md)

2026-10-30 Q-Dir “Copy Path”：在“编辑”菜单增加“复制所选项目完整路径”，单选原样复制，多选按行复制；虚拟 Shell 位置或空选区不写入部分/无效路径。定向路径格式 smoke、完整 Release `SettingsSmoke`、App Release build（0 警告/错误）、`ExplorerDirs.HostProbe --selected-paths-clipboard` 真实生产 Shell 单选/多选取值（隐藏宿主、前台不变、未触碰剪贴板）及 `git diff --check` 通过；未启动 GUI。下一次集中验收需检查菜单状态、剪贴板文本、虚拟项目拒绝，以及复制后原文件视图仍可立即用方向键操作。[Shell 功能对照](shell-parity.md)

2026-10-30：修正逐行递归大小隔离 A/B 的基线解释。此前 `IColumnManager.Capture` 快照不包含合成 namespace 的自定义递归列，旧通知轮次因此没有先恢复有效值列，不能单独据此断言各通知策略无法刷新正确列值。新 WPF `HwndSource` Private Desktop 探针显式调用 `SetColumns`/`SetColumnInfo` 重放 Name/Recursive Size/Type；调用成功且 `Capture` 读回自定义键，但 DefView 行内容仍为 `1200/4` 而不是预期 `10/4`，探针检测到无效基线后跳过通知。初始视图仍显示 `10/4` 并可排序。该轮退出码 0、前台与输入时间未变、临时注册/夹具清理完成；退出码只说明探针结束，不代表列恢复通过。产品继续使用 ExplorerBrowser，未接入原型；详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-30显式重放自定义列的刷新基线复核)。下一步需评审能在刷新后重新提供有效派生列值的受支持视图架构；不再运行缺少正确列基线的通知对照。

2026-10-30 目录树图标后台复核：生产 `NativeShellTree` 隐藏宿主读到有效的系统图像列表句柄和普通目录图标索引；默认与显式图像列表的像素、初始化尺寸与最终尺寸 A/B 均一致，前台不变。没有证据支持再改图标选择代码；当前待确认点是 WPF `HwndHost` 的最终可见绘制，保留到集中 GUI 验收。

近期执行顺序：已为“使用系统 Explorer 浏览”和用户级文件夹入口整理[集中验收清单](gui-acceptance-system-entry.md)。下一步继续从仍开放的 Shell parity 缺口中选取无需 GUI 的实现项；依赖真实消息循环、物理键鼠或当前用户 Explorer 扩展菜单的项目留待集中验收。新功能的成功标准与验证范围在对应批次记录，不将探针证据当作产品可见验收。

2026-10-30：最新集中验收候选已从干净主线 `db5c8daf54e3106f31023760f92f61d6708ddd74` 准备于 `src/.state/gui-smoke/20261030-005620-783-418f8b`，状态 `prepared-not-run`，37 项待验收。EXE/DLL SHA-256 为 `C8791A3A19BBB035FD407A3AB8B718F97280AC09E418380A780C0EEDEC48C469` / `B474C648B316463EAD9E58CE08A3F5BD0EBCABD8753B2EC4C73875709B14F55A`。新增 Q-Dir 颜色元数据夹具哈希为 `70D8C32A0F9B0831A4AD060FAA87A982B7E077DD4791A2F375023D6BF9AC8D34`，只读 importer 检查确认四窗格布局可解析且颜色预览保留 4 条规则。未启动窗口，不属于日常 release；详情见[最新候选身份](gui-acceptance-2026-10-30-main-prepared-latest.md)。

2026-10-30 自动刷新诊断：目录 watcher 的 `Error` 事件此前只触发强制协调和重建，未保留错误类型。现将异常类型与 HRESULT 记录到 `LastShellRefreshStatus`，并在 Dispatcher 写入前验证导航、目录、队列、恢复实例和 watcher 仍匹配。随后真实四窗格生产宿主以 10,000 项并发本地创建触发 watcher Error 并恢复通过；错误持久诊断另见本路线图后续条目。自动刷新 P0 与可见体验门槛不变。详见[调查记录](live-update-investigation.md#2026-10-30记录目录-watcher-错误类型)。

2026-10-30 IShellView 隔离迁移复验：当前 Release NativeProbe 的四窗格 WPF consumer 再次通过（退出码 0）。反向选择、Ctrl+A、F2 方向键隔离、Shell 文件操作通知、原生视图菜单及 WPF 根窗同步销毁均通过，前台 HWND 与最后输入时间不变。该结果只加强隔离宿主证据；ExplorerBrowser 产品接线、物理输入、完整 OLE 拖放与可见验收仍开放。详细记录见[隔离原型](ishellview-color-spike.md#2026-10-30当前主线复跑四窗格-wpf-consumer)。

2026-10-30 递归目录大小隔离更新 A/B：WPF 根宿主下有效临时 Shell namespace 可建立视图，初始递归值显示与数值排序正确；单项通知、父目录通知和整视图刷新均未恢复派生列值，整视图刷新还丢失选区/焦点并耗时约 2.64 秒。宿主未超时，临时注册与夹具均清理。该路线不接入产品，后续不再叠加同类 DefView 刷新技巧；详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-30wpf-根宿主下递归大小列更新-ab)。

2026-10-30 Q-Dir 快速链接 Ctrl+单击：按 [Q-Dir 官方说明](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English) 补齐文件夹链接的 Ctrl+单击新窗口行为。新窗口复制来源会话后，在其活动窗格导航到快捷链接目标；普通单击仍在当前活动窗格导航，文件/程序快捷方式在两种修饰键状态下继续交给 Windows Shell 启动。目标须由 Shell 明确确认为文件夹；若新窗口创建失败，保持来源窗口不变并显示状态提示。快速链接目标策略与菜单接线 smoke、完整 Release SettingsSmoke、App Release build 通过；未启动 GUI，物理 Ctrl+单击与可见新窗口体验留待集中验收。

2026-10-30 Q-Dir 窗格焦点快捷键：Q-Dir 官方帮助规定 Ctrl+1…4 聚焦对应 Q-View，但 Explorer Dirs 已用 Ctrl+1…9 切换标签。本项目新增 Ctrl+Alt+1…4 聚焦布局中对应的可见窗格，并把焦点放进文件视图；隐藏窗格请求不改变当前活动窗格，文本输入控件保留按键。路由、键位不冲突和文本焦点策略已有 smoke；可见窗格间的真实焦点切换仍待 GUI 验收。[Q-Dir 4.51 官方帮助](https://www.softwareok.com/?seite=q-dir-hilfe)

2026-10-30：后台快捷键复核时检查到 Alt+F11 虽已有 Q-Dir 同款应用内重启入口，但窗口级预览键路由没有避开文本框焦点，可能在地址/搜索编辑中弹出重启确认。现将该快捷键与文件视图命令一同交还给文本输入控件；焦点策略定向 smoke、完整 Release SettingsSmoke 与 App Release build 均通过（0 警告、0 错误）。Shift+F6 继续保留本项目既有的反向焦点区域导航，不覆盖为 Q-Dir 的查看方式循环，因为路线图已确认该差异以保持 Explorer 键盘导航。

2026-10-30 Q-Dir Copy Path 快捷键：官方功能提示记载 `Ctrl+Shift+C` 复制所选路径且不加引号。当前主线已具备完整路径复制命令，本批将该组合接入统一快捷键解析器，复用现有 Shell 路径读取/剪贴板动作，仅在文件视图聚焦时触发，并在“编辑”菜单显示快捷键。回归先以预期失败确认映射缺失，接线后完整 Release SettingsSmoke 通过；App Release build、剪贴板真实写入和物理键盘交互仍待验证/集中验收。未制作预览包。依据：[Q-Dir 官方提示](https://softwareok.com/?page=Windows%2FTip%2FQ-Dir%2F23)。

2026-10-30 集中验收候选更新：将最新主线（含 `Ctrl+Shift+C`）准备为隔离副本 `src/.state/gui-smoke/20261029-212632-525-ee20a3/`，并在 35 项验收清单中加入文件区快捷键、文本输入焦点边界检查。`PrepareOnly` 成功，清单记录源提交 `41c1a60` 且工作区干净，EXE/DLL 哈希与副本一致；进程数为 0，未启动窗口或更改剪贴板/真实注册表。该副本不是 release，所有可见项仍待集中验收；旧副本仍留在其隔离目录；当前候选身份见[最新候选身份](gui-acceptance-2026-10-30-main-prepared-latest.md)。

2026-10-30 当前主线后台基线：`dotnet run --project src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj -c Release --no-restore` 完整通过，exit code 0；`ExplorerDirs.App` Release build 0 警告/错误。覆盖的是已存在的设置/路由/状态 smoke 与托管 App 编译，不包含真实文件视图 UI、Shell 菜单、物理输入或 ExplorerBrowser 可见行为。本轮未修改产品源码或 release。

2026-10-30 Q-Dir 盘符快捷键：按 Q-Dir 4.51 官方帮助补入文件视图聚焦时的 `Ctrl+Alt+A～Z`，跳转至对应已列出的逻辑盘符根目录；未列出的盘符交回 Shell，不主动探测网络盘就绪状态。映射、严格修饰键及文本输入焦点边界 smoke、完整 Release SettingsSmoke 与 App Release build 通过，0 警告/错误；未运行窗口或物理按键。可见键盘行为和当前安装版 Q-Dir 对照留待集中验收。[快捷键清单](keyboard-shortcuts.md)

2026-10-30 Q-Dir Alt+L 放大镜入口：将“工具 → 屏幕放大镜”和 Alt+L 接到 Windows 自带 `Magnify.exe`，保持 Windows 的镜头/全屏/停靠模式与系统快捷键实现，不另做屏幕采集放大器，不注册全局热键。为保留 Windows 放大镜运行期间的 `Ctrl+Alt+L` 镜头切换，本程序在检测到 Magnify 进程时让出同组合键（若无法读取进程状态也优先让系统处理）；定向 smoke 覆盖 Alt+L 解析、文本输入保护、组合键边界、让行策略及系统程序启动参数。未实际启动放大镜或打开 Explorer Dirs 窗口，真实辅助功能交互待集中验收。[快捷键清单](keyboard-shortcuts.md) · [Q-Dir 功能对照](shell-parity.md#2026-10-30-官方功能清单补核)

2026-10-30：继续排查递归目录大小 Shell 原型的清理停滞。源码显示异常返回路径先释放 `IFolderView2`、再销毁 `IShellView` 子窗口；现调整 teardown 为先 `DestroyViewWindow`，保留 view/browser-site/folder 引用直至子窗口销毁，再释放 COM 引用与 parent。NativeProbe Release、原生 `/W4 /WX`、无窗口 DLL unload contract 与 synthetic folder tree smoke 均通过；未运行 Private Desktop view probe，不能声称 STA 等待已解决。产品/release 未改。后续在桌面边界稳定时，用带外部 watchdog/minidump 的递归大小探针复测清理阶段。

2026-10-30：继续准备递归目录大小更新的 Shell 通知 A/B。NativeProbe 现依次测单项 `SHCNE_UPDATEITEM`、单项加父目录 `SHCNE_UPDATEDIR` 和整视图刷新，并采集列值、排序、选区/焦点是否更新；无窗口合同 smoke 还确认合成值覆盖更新列属性并让 `CompareIDs` 按 1 对 4 字节文件重新排序，清除覆盖后比较关系恢复。NativeProbe Release、原生 `/W4 /WX`、DLL unload contract 和 folder-tree smoke 通过；前台 HWND 仍为 0，未运行 Shell view A/B。产品和 release 未改。下一步在桌面边界稳定时运行 A/B 与 watchdog 清理复测，再根据观测决定是否实施后台大小缓存和逐项更新。

2026-10-30：本轮源码级复核发现递归大小排序探针先卸载合成 Shell COM DLL、撤销临时 namespace 注册，再销毁可能仍含 Shell 子窗口的父 HWND，存在生命周期风险。已调整探针为先销毁父窗口树，再撤销注册，并修正 `DllCanUnloadNow` 计数以包含枚举器和 class factory；无窗口合同 smoke 先失败、补齐后通过。NativeProbe Release 与原生 `/W4 /WX` 构建通过。watchdog 已接入递归大小/WPF 对照命令，支持外部进程 timeout dump，25 秒捕获早于 probe 内部 30 秒超时；timeout-tree 自检已验证可写出 MDMP 并清理进程。真实 Shell 挂起 dump 尚未采集/分析，不能声称卡死已修复。后续在桌面边界允许时复测。产品和 release 均未改。详见[调查记录](folder-size-column-investigation.md#合成-namespace-排序对照与清理停滞)。

2026-10-30 主干后台基线：提交 `9c47a79` 为合成 Shell 排序探针加入逐阶段清理诊断后，完整 Release `ExplorerDirs.SettingsSmoke` 通过，App Release build 0 错误；唯一警告为 NuGet 漏洞源不可达产生的 `NU1900`。本轮未改产品功能、未启动窗口、未更新 release。该结果确认既有后台回归通过，不改变文件操作/焦点、真实 OLE 拖放、自动刷新可见体验及 IShellView 迁移等开放门槛。

2026-10-30：Explorer 重启器改为在同一 SafeProcessHandle 上校验桌面 Shell PID/映像并执行终止与等待，防止校验和 `Kill()` 之间 PID 复用到不同进程；身份门控 smoke、完整 Release `ExplorerDirs.SettingsSmoke` 和 App Release build 均通过。全量 smoke 首轮曾在 `EnvironmentChangeMessageSmoke` 创建 `HwndSource` 时超时，独立定向复跑及随后完整套件均通过，当前视为未复现的瞬时超时，不改测试实现。App 构建 0 错误、1 条 `NU1900`（NuGet 漏洞源不可达）。没有实际重启 Explorer、启动产品 GUI 或更新 release。逐行派生大小调查另依据 Microsoft 文档标记 `IShellFolderView::RefreshObject` 为 legacy/不可依赖接口；详见[调查记录](folder-size-column-investigation.md)。

2026-10-30：继续评估逐行派生大小列的隔离 `IShellView` 路线。注册 schema / property store adapter 的 DefView 实验虽能读回排序键，但排序阶段没有触发 `CompareIDs`、属性读取或 legacy `SFVM_GETDETAILSOF`，行序不变；合成 namespace 对照则按真实夹具文件的大小通过升降序，并可将递归目录大小显示为 10、文件显示为 4，升降序均正确重排且分别触发比较器。递归目录探针在结果断言之后卡于销毁父窗口阶段，等待链指向 COM/ChsIME；首次前台与输入时间未变。随后清除测试窗口焦点的 A/B 仍卡在相同位置，且该次守卫观测到前台/输入时间改变，因此不计作隔离通过。产品仍使用 ExplorerBrowser；后续需桌面边界稳定时再检查父窗口/子窗口销毁调用栈。证据与边界见[逐行大小调查](folder-size-column-investigation.md#2026-10-30-合成-namespace-排序对照与清理停滞)。

2026-10-30：为下一次受控的 Private Desktop 超时增加显式路径 minidump 采集（不设置 `EXPLORER_DIRS_NATIVEPROBE_TIMEOUT_DUMP_PATH` 时不生成 dump）；无 Shell 窗口自检确认 MDMP 头、线程列表与线程信息流可读取。另加 opt-in WPF `HwndSource` 根宿主的递归列排序/清理对照，保留原 `STATIC` 基线。NativeProbe Release 构建 0 警告/错误。尚未运行新对照、重跑卡住视图或取得实际线程栈；这些是诊断准备，不代表已定位或修复生命周期问题。详见[逐行大小调查](folder-size-column-investigation.md#2026-10-30-合成-namespace-排序对照与清理停滞)。

2026-10-30：发布预览 temp.79（`0.6.0-preview.temp.79+e77f12a8579fcb2d72854c9b045bc12c5b4ed560`）。相较 temp.78，包含隐藏标签栏但保留标签状态、虚拟 Shell 文件夹新标签打开、文件夹入口管理 UI 和 Explorer 重启 PID 身份保护。完整 Release SettingsSmoke、发布隔离 smoke、实际 publish 及哈希核对通过；profile 从 temp.78 独立复制。未启动程序、未启用入口、未修改真实 Explorer 注册表。

2026-10-30：复核逐行大小异步更新的受支持接口后，将隔离验证候选从 legacy `IShellFolderView::RefreshObject` 改为 `SHChangeNotify(SHCNE_UPDATEITEM)`。产品现有文件系统通知路径在项目更新后，会于排序/分组依赖变更元数据时补发目录更新；它不证明虚拟 namespace 的行为，但让后续 A/B 范围明确为单项通知、单项加父目录通知和整视图刷新。公开文档没有保证 DefView 会重取扩展列、重排或保留选区；通知还可能合并为目录级事件。尚未运行行为探针；Private Desktop view 探针仍暂停至隔离边界稳定。产品未改，ExplorerBrowser 保持现状。详见[调查记录](folder-size-column-investigation.md#2026-10-30-支持路径候选复核)。

2026-10-31：运行 `SHCOLSTATE_SLOW` 递归大小列 A/B。慢列标志未阻止单项目录更新后 DefView 列索引变为 `-1`，也未引发行值更新、比较或重新枚举；更新阶段只多两次 `GetDetailsOf` 请求。该合成样本不能证明普通 ExplorerBrowser/provider 行为。探针在父窗口销毁阶段超时，watchdog 终止进程；超时前前台 HWND 与输入时间戳不变，且 CLSID/NameSpace 注册、临时目录和进程均已清理。结果不支持将该标志接入产品；产品与 release 未改。详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31shcolstate_slow-defview-更新-ab)。

2026-10-31：用 WPF `HwndSource` 根宿主复跑递归大小列探针，单项更新场景连续三轮均在前台/输入不变且清理完整的条件下返回；列索引仍为 `-1`，未更新列值。追加父目录更新、通知后显式列重放及 Icon→Details 切换均未恢复行值或比较器调用，说明应停止堆叠同类 DefView 刷新手段。完整 `IShellView.Refresh()` 轮观察到行重排，但前台与输入守卫变化，故判为无效样本，不采纳其行为结论；临时注册/夹具已核对清理。WPF 根宿主与探针清理成功有强相关性，但根因和产品兼容性仍未证明；产品与 release 未改。后续转向隔离 namespace/自有视图数据路径及 Shell 合同，不接入产品。详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-31wpf-根宿主清理与更新策略对照)。

2026-10-30：键盘“在新标签打开所选文件夹”（Shift+Enter/Ctrl+Shift+T）改为读取 Shell 绝对解析名，并要求 Shell folder 属性及当前系统 provider 均确认该位置为文件夹；因此支持可解析的虚拟 Shell 文件夹，同时保留搜索位置、非文件夹及不受支持 namespace 的拒绝行为。文件视图中键新标签仍限普通文件系统目录。定向 resolver smoke、完整 Release SettingsSmoke 均通过；App Release build 0 错误，3 条 NU1900（NuGet 漏洞源不可达）。未启动窗口，真实键盘输入与可见新标签行为仍待集中 GUI 验收；未更新 release。

2026-10-30：文件视图中键开新标签现在除普通路径外，也可解析受支持 Shell 父文件夹内的 UI Automation 命中项：通过父 `IShellFolder::ParseDisplayName` 取得相对 PIDL、组合为绝对 PIDL，并由 Shell 再确认目标是文件夹。定向 smoke 已在临时文件夹及“此电脑”系统盘上通过；非法名称、缺失子项、普通文件目标和过期请求均拒绝。完整 Release SettingsSmoke 通过；App Release build 0 错误、2 条 NU1900（NuGet 漏洞源不可达）。没有运行产品 GUI，虚拟 provider 与真实中键命中仍待集中验收。

2026-10-30：“使用 Explorer 浏览”启动器现在以 Windows 目录下 `explorer.exe` 的完整路径启动系统文件管理器，继续把当前文件夹/Shell namespace 路径作为单一参数传递。定向 smoke 先验证失败（目标仍是裸 `explorer.exe`），修改后通过；App Release build 0 警告、0 错误。没有启动 Explorer，真实 Explorer 导航仍待集中 GUI 验收。

2026-10-29：隔离 namespace 生命周期探针改为在两个视图均有待处理刷新时先调用 `IShellView::DestroyViewWindow`，再取消关闭视图的刷新，并确认兄弟视图仍刷新、释放后无迟到回调。探针内部断言均通过，关闭期间未观察到刷新回调；但外层 Private Desktop 守卫检测到前台从 LockApp 变为 ChatGPT 且输入时间戳变化，因此整轮不计为隔离通过。未接入产品，也未重跑窗口探针；需在桌面边界稳定时复验，且该场景仍不覆盖真实 HWND 销毁重入或并发 Shell 通知。源码改动仅调整 NativeProbe 场景顺序。详见[架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。

2026-10-29：Release `--shell-view-color-policy-smoke` exit 0；其中 `ShellViewDragSource.VerifyContract` 覆盖 OLE 源的 Escape 取消、等待观察到初始鼠标键后才允许结束、释放后返回 Drop，以及让 Windows 处理 None/Copy/Move 光标反馈。该命令未创建 Shell view 或文件；仅证明 COM 回调合同，不证明真实 `DoDragDrop`、指针命中、目标协商、Copy/Move 结果或产品行为。

2026-10-29：隔离原生 namespace 原型已修复探针自身的 PIDL 生命周期错误，并验证真实 DefView 可创建、按虚拟列升降序排序；背景/选中菜单可构建。选中 `IDataObject` 的 `CF_HDROP` 与 CIDA 实际取出后均指向夹具中的同一文件；`IFileOperation.CopyItems` 已复制样本。双视图的重命名/删除现改用虚拟视图选区返回的 `IDataObject` 调用 `RenameItems`/`DeleteItems`，两视图均收到通知且未操作项的选择/焦点恢复，两轮通过。临时 HKCU 注册及夹具目录均清理，未接入产品 ExplorerBrowser。隔离 IShellView 另已按 Shell API 验证 8 种 ViewStyle 模式及单选保留（Content 的底层 SysListView32 报告 Tile，尚无视觉等价证据）；下一步仍缺完整 OLE `DoDragDrop` 源/目标循环及视图生命周期边界；在迁移门槛完成前不迁移产品宿主。细节见[原生 Shell 架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。

2026-10-29：在隔离 synthetic DefView 探针中补充多选成功 Copy：选中 `two.bin` 与 `z-ten.bin`，从 Shell selection `IDataObject` 执行 `IFileOperation.CopyItems`，确认目标恰有两项、逐字节匹配源文件且源文件保留；原有多选取消后保持两项选中及焦点的验证仍通过。完整 Private Desktop watchdog exit 0，NativeProbe Release build 0 警告/错误，结束后临时 CLSID/NameSpace 和源/目标夹具均不存在。运行期间外层曾短暂观察到前台从 TortoiseGitProc 变为 TortoiseGitUDiff；首次变化时输入时间戳未变，探针结束时原前台恢复，但变化原因未能归因，因此不将其视为“全程前台未变”的证据。结果仅覆盖隔离 Shell data object 与程序化复制，不等同于真实鼠标 OLE 拖放或产品行为。

2026-10-29：将隔离原型的 `IFileOperation.MoveItems` 加入双视图文件操作序列：删除 `a-large-renamed.bin` 后将 `z-ten.bin` 移到独立目标目录。探针日志确认源文件消失、目标文件存在且字节一致；两个视图都移除源项，并保留未移动 `two.bin` 的选中与焦点。Release build 0 警告/错误。但这轮 watchdog exit 1：运行期间外层前台从 Task Manager 变为 LockApp，输入时间戳改变，因此不计为完整 Private Desktop 通过；探针 finally 和 watchdog 均已清除临时 CLSID/NameSpace、源/目标夹具及进程。需在前台稳定时重跑同一场景后，才能把该项标为完整验证。该程序化 Move 不代表真实鼠标 OLE 拖放。

2026-10-29：完成标签栏隐藏模式的后台实现：保留原有“始终显示 / 多标签时显示”，新增仅隐藏标题条的选项，标签对象、浏览状态与快捷键不受影响。便携配置升至版本 2，并兼容读取旧版本布尔值；未知更高版本拒绝写入。定向与完整 smoke、App Release build 通过。未启动产品 GUI，菜单操作、焦点导航、跨重启展示仍待集中验收。

2026-10-29：重新运行文件夹入口的隔离注册表 smoke。脚本仅在随机 `HKCU\Software\ExplorerDirs.Tests\FolderVerb-*` 临时键及系统临时目录中验证能力清单哈希、安装/重复安装/卸载、预存冲突拒绝和外部编辑保护；退出后检查无残留测试键或临时目录。全部通过，未接触真实 `HKCU\Software\Classes\Directory/Drive` 关联项。该证据修复了此前因沙箱拒绝 HKCU 写入而缺失的隔离测试，不代表真实 Explorer 菜单可见或启动体验已验收。

2026-10-29：文件夹入口命名管道协议 smoke 在沙箱内因 `CurrentUserOnly` 客户端连接限制而跳过；随后在授权的隔离执行中重跑通过。请求 ID 握手不会提前分发，超时重试只分发一次，独立同路径请求可再次分发，`.qdr` 与窗口激活重试幂等，缺少激活目标会拒绝，旧版发送端和新版接收端兼容。本结果只覆盖本地协议及模拟回调，不启动产品窗口、不验证 Explorer 的真实右键菜单或打开目录行为。

2026-10-29：只读检查本机 Q-Dir 安装目录中的 4 个 `.qdr`（`New.nwp.qdr`、`start.qdr`、`start2.qdr`、一个收藏夹工作区）均未包含 `ViewModeFlags`、`ColsID` 或 `FFlags` 键。它们现有的 `ViewMode`、树开关、焦点和比例值不能推出上述字段语义；因此不据这些样本补写导入映射，需在 Q-Dir 中切换对应选项并重新保存配置后再比对，或取得含这些键的真实配置样本。未启动 Q-Dir 或产品 GUI。

2026-10-29：用本机三个 Q-Dir `.qdr` 文件执行真实配置导入→`QdrLaunchSession` 启动投影→本程序导出→再导入：42 标签、44 标签和单标签启动配置均通过，基础字段恢复且扩展会话逐字匹配。首轮发现 `QdrRoundTripProbe` 绕过产品启动投影，直接把未使用的隐藏窗格留空，导致单窗格配置触发 exporter 完整性校验；探针已改用产品相同的 `QdrLaunchSession.Create`，并新增单窗格回归。完整 Release SettingsSmoke 通过。此结果只验证本机配置在本程序内往返，不证明 Q-Dir GUI 读取本程序导出结果。

2026-10-29：只读检查本机 Q-Dir 安装目录 `.qdr` 发现 `start.qdr`/`start2.qdr` 为 `tree_mode=2`，但四个 `TreeN` 均为 0，`TreeNsplit` 仍记录默认 2500。导入器此前只按 `tree_mode=0` 抑制宽度警告，导致所有窗格树关闭时仍提示忽略宽度。新增回归先复现该多余警告，再将提示门控改为：只有存在有效启用的窗格树或树开关不完整时才提示；四个有效树开关全为 0 时不提示。完整 Release SettingsSmoke 通过，未启动 Q-Dir 或产品 GUI；宽度单位仍未知，不导入 `TreeNsplit`。

2026-10-29：内存留存审查发现 `ExplorerBrowserHost` 在 Shell 选择恢复期间，即使 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE` 未启用，也会为每个视图最多保留 128 条诊断字符串。此集合有上限，不是无界泄漏，也没有证据说明它能解释用户观察到的整体内存差异；现已在 tracing 关闭时跳过记录，仅保留显式诊断所需数据。源码回归先按预期失败，再通过；定向 `--shell-selection-restore-gate-smoke` 与完整 Release `ExplorerDirs.SettingsSmoke` 均通过，App Release build 成功（0 错误，2 条 NU1900，因 NuGet 漏洞源不可达）。没有运行产品 UI；当前没有可归因的 ExplorerDirs.App 运行样本，因此不报告内存下降数值，也不将此项视为整体内存增长问题已解决。

2026-10-29：提交 `ba4036f` 新增“最近打开的收藏夹”窗口标题模式后，完整 Release `ExplorerDirs.SettingsSmoke` 输出 `Settings smoke: PASS`；App Release build 为 0 错误、2 条 NU1900（NuGet 漏洞数据源不可达）。未启动产品窗口。当前没有 ExplorerDirs.App 进程，因此本轮无法采集实际运行内存。`folder-verb-smoke.ps1` 的首次本地权限内尝试被沙箱拒绝；随后按脚本作用域获批，在唯一 `HKCU\Software\ExplorerDirs.Tests\FolderVerb-*` 临时键下运行通过，完成幂等安装、冲突保护、外部编辑保护与清理检查，结束后无 FolderVerb 测试键残留，未触碰真实 Explorer 注册项。

2026-10-29：隔离 `IShellView` 已验证 Shell item 入口、视图枚举/标准 Size 排序、菜单、文件操作与通知；新增列仍未被 DefView 请求逐项值。官方接口文档复核确认系统 DefView 没有受支持的 cell-value/render/sort 回调入口，而自定义 `IShellView` 会承担窗口、呈现、菜单、键盘、拖放等完整 view 职责。经用户授权的虚拟 namespace 实验现已证明：Shell parsing name 可解析，`SHBindToParent`/`IShellFolder::BindToObject` 可由注册 class factory 实例化文件夹对象，DefView 会调用 `IShellFolder2::CompareIDs` 并按数值升降序重排；背景菜单包含标准查看/排序/分组/刷新命令。初始实验曾因虚拟 folder 未实现子项 `GetUIObjectOf` 而无法提供选中项对象；后续 `7e4e767` 已加入 filesystem 子项解析与 `IContextMenu`/`IDataObject` 委托，并由后续隔离探针验证实际菜单、`CF_HDROP`/CIDA 提取、Copy/取消、Rename、Delete 和 Move。Move 的文件系统与双视图断言通过，但该轮 watchdog 因前台切至 LockApp 判失败，因此整轮仍待重跑。真实鼠标 OLE `DoDragDrop`、完整焦点/生命周期门槛及产品宿主接线仍未完成；产品继续使用 ExplorerBrowser。证据见[原生 Shell 架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)和[逐行列调查](folder-size-column-investigation.md#2026-10-29-shell-绑定激活-namespace-com-对象并保留排序)。

2026-10-29：提交 `bbc2f6a` 修正 Shell 视图重建期间的暂时选区扩张：当同一视图且无新输入时，若旧快照焦点项原本未选中、却出现在较大的新快照中，继续保留已确认的多选快照；异步枚举补齐其它项目仍可更新。纯门控回归和 HostProbe 确定性回调覆盖通过；四窗格 3,000 项创建/删除压力运行 6 轮均退出码 0，选区/焦点保持。完整 Release SettingsSmoke 通过。仅为隐藏本机 ExplorerBrowser 宿主证据，用户可见文件操作与其它 Shell provider 仍待集中验收。

2026-10-29：列值筛选性能验证扩展至 10,000 项普通本地目录（5,000 TXT + 5,000 BIN）。隐藏生产 ExplorerBrowser HostProbe 连续两轮筛选稳定收敛到 5,000 项分别耗时 183/172 ms，清除并恢复 10,000 项耗时 93/92 ms；四窗格隔离、临时夹具清理、父窗隐藏和前台不变均通过。计时覆盖筛选应用至三次稳定项目数读取，不包括列头菜单值清单枚举，也不代表机械盘、网络/云或第三方 provider。性能结果尚未表明需要继续优化；可见菜单与用户操作仍待集中验收。

2026-10-29：当前沙箱拒绝同进程客户端连接 `PipeOptions.CurrentUserOnly` 命名管道（`UnauthorizedAccessException: Access to the path is denied`）。路由协议与“无激活目标” smoke 现在先做实际连接预检；不可用时明确输出 `SKIP`，完整 SettingsSmoke 其余检查通过。该结果不验证跨进程路由协议本身；真实 Windows 用户会话仍需运行这些 IPC 检查。

2026-10-29：列值筛选定向 COM smoke 暴露 `PropVariantClear` DLL 错误和不存在的 `InitPropVariantFromVariant` 入口；二者曾令 Shell 属性筛选在 fail-open 保护下总是放行。现改为 `Ole32.PropVariantClear` / `Propsys.VariantToPropVariant`，并将多列属性读取合并为每个项目一次 `IShellFolder2` 查询、复用原生缓冲区。计数型 COM smoke 验证接口查询次数、属性失败放行、匹配与不匹配结果；完整 Release SettingsSmoke 通过。App Release build 成功，0 错误，NuGet 漏洞数据源不可达产生 2 条 NU1900 警告。未启动 ExplorerBrowser/GUI，也未更新预览包。

2026-10-29：新增隐藏生产 ExplorerBrowser 列值筛选验证。四窗格 Shell 视图用当前系统实际显示的 `System.ItemTypeText` 文本依次筛选 TXT、BIN，验证条件切换、同列值 OR、跨列条件 AND、名称通配符与列值 AND、冲突条件为空、清除恢复完整视图，以及其它窗格不受影响；父窗保持隐藏、前台 HWND 未变化，临时夹具由探针清理。Release HostProbe 构建成功，探针退出码 0。该证据覆盖本机普通文件夹和当前 Shell 属性 provider，不覆盖列头菜单的可见命中、慢速/虚拟 provider 或用户交互；预览包未更新。

2026-10-29：修复逐标签筛选探针过期的反射构造参数，并补验列值筛选的标签语义。隐藏 `PaneControl`/ExplorerBrowser 实例确认新标签默认无筛选、复制标签继承列值筛选且实际 Shell 项目数分别为 2 和 1；源标签状态和每标签导航历史隔离仍通过，探针父窗隐藏、前台不变。此验证不覆盖重启后的真实会话恢复或可见标签交互；NuGet 漏洞源不可达产生 1 条 NU1900 警告，未更新预览包。

2026-10-29：隐藏生产 ExplorerBrowser 列筛选扩展到 Windows ZIP 压缩文件夹 provider。探针在隔离夹具内创建两个类型不同的归档条目，读取 Shell 视图实际显示类型值；逐值应用 `System.ItemTypeText` 后可分别得到匹配条目，清除条件恢复两项，再返回源目录并删除 ZIP。四窗格隔离、父窗隐藏、前台不变通过。该结果只覆盖本机 ZIP provider，不代表网络、云盘或第三方虚拟 Shell provider；未启动可见 GUI、未更新预览包。

2026-10-28：以干净主分支当前源码重跑三个定向 Release SettingsSmoke：`--shell-tab-drop-smoke`、`--tab-transfer-policy-smoke`、`--qdr-layout-smoke` 均通过。它们覆盖 Shell 文件夹拖到标签的数据解析、跨窗格移动/复制策略与 Q-Dir 布局映射；没有启动产品窗口，不证明真实鼠标拖放、标签焦点/历史或 Q-Dir 可见互操作。

> **IShellView 拖放最新复验（2026-10-28）：**两轮探针的外层前台变化都发生在 Private Desktop worker 的 Shell 操作阶段，但源码确认监视器读取的是调用线程所在的原桌面前台，阶段名只提供时间相关性；探针 worker 内 Private Desktop 的前台保持不变。旧日志没有记录原桌面 HWND 的进程/窗口类或输入时间，无法区分外部用户活动与探针影响。本轮为下一次受控复验加入这些身份及输入时间戳诊断；仍保留原桌面前台变化即失败的保护，不重跑 Shell-view 探针。现有直接 `IDropTarget` Copy 路径返回成功、目标内容正确且源保留；将 `IShellView.GetItemObject` 作为目标仍返回 `E_NOINTERFACE`。未调用 `DoDragDrop`，真实 OLE 拖放仍未验证。详见[本轮拖放复验](ishellview-color-spike.md#2026-10-28-private-desktop-拖放探针前台阶段复验)及[前台监视器桌面区分](ishellview-color-spike.md#2026-10-28-区分-private-desktop-阶段与调用桌面前台变化)。

2026-10-28：系统文件夹入口回滚复核发现，启用失败后的清理只检查本项目标记，可能删除启用期间被外部改动的注册表树。现在仅在注册表项仍完整匹配本项目生成内容时才回滚；外部新增或修改会保留为冲突供用户处理。PowerShell 语法解析通过；隔离注册表 smoke 因当前执行环境拒绝写入 HKCU（`Access is denied`）未能运行，未改真实 Explorer 注册表。

2026-10-28：列值筛选菜单读取完成后的全量去重与排序移至线程池执行；结果返回 UI 前再次检查菜单状态和 Shell 视图代次，丢弃已关闭菜单或已切换标签/目录的旧结果。App Release build 成功，0 错误；NuGet 漏洞源不可达产生 2 条 NU1900 警告。未运行 GUI，列头入口和大量值清单仍待集中验收。

| 顺序 | 剩余工作 | 当前事实与下一步证据 |
| --- | --- | --- |
| 1 | 核心产品集中验收 | 最新副本见[验收候选记录](gui-acceptance-2026-10-31-main-prepared-050655.md)：基于干净主线 `eff0a65654b44c0a7d8b1de41d967e2071130891`，47 项，状态 `prepared-not-run`，路径 `src/.state/gui-smoke/20261031-050655-754-4b72da/`。文件区输入/焦点、独立克隆视图、主题/菜单、Q-Dir `.qdr`、系统 Explorer 入口，以及 F5 原生重绘/查看方式重排等候集中可见验收；逐行派生列仍只在隔离架构原型阶段。 |
| 2 | 产品稳定性与内存增长归因 | 图标缓存有界和可见标签轮询已实现；temp.76 被动区间未见明显增长。四窗格隐藏 HostProbe 3,000 项压力峰值 89.87 MiB Private Bytes / 143.30 MiB Working Set；Shell 宿主 Dispose 后 Private Bytes 回落约 11.15 MiB。隔离完整 GC 将托管堆估算由约 13.02 MiB 降至 0.68 MiB，但 Private Bytes 没有同步下降，不能据此判泄漏或归因原生占用。temp.78/PID 19580 于 2026-10-30 13:35–13:36 被动采样 60 秒：Private Bytes 稳定在 180.5 MiB，Working Set 约 360.93–361.04 MiB，句柄/线程固定；用户负载未知，不说明操作期间增长。下一步仍需对正式 App 固定使用负载采样，再决定是否需要分配栈。详见[内存采样](memory-sampling.md)和[可靠性方案](reliability-isolation-design.md)。 |
| 3 | IShellView 隔离迁移门槛 | 2026-10-28 当前 Release NativeProbe 的 Private Desktop 四窗格 consumer 复验通过：状态流恢复、四 pane 菜单路由、pane 1–3 键盘/Ctrl+A、四 pane F2 焦点隔离、Copy/Rename/Delete 收敛、目录通知及 sibling 隔离通过，前台不变且无遗留进程。用户另在隔离候选 `125449 / 179260` 中手动确认单个 sample 的 F2 重命名与方向键编辑状态保持；这不覆盖产品 ExplorerBrowser。另用真实选中项 `IDataObject` 直接调用已注册目录 `IDropTarget` 的 DragEnter/Over/Drop，目标文件内容与 Copy effect 通过；直接调用已确认 Ctrl+Copy、跨卷无修饰键 Copy/Shift+Move、同卷无修饰键 Move/Shift+Move 的目标端效果和源清理；但仍不是有效 `DoDragDrop` 源端闭环代替。三次使用原生 `STATIC` 顶层 parent 的同步销毁在 Shell view/pane 已回收后卡于 `DestroyWindow`；增加 OLE 初始化与 WPF DispatcherFrame 泵仍复现。将同一菜单生命周期置于 WPF `HwndSource` 根窗（其下为 pane `STATIC`，保持 OLE 和 WPF pump）后连续 3/3 销毁成功并收到 `WM_NCDESTROY`，前台不变。四 pane WPF `HwndSource → Grid → HwndHost → STATIC` 组合随后在 Private Desktop 完整菜单激活/停用与同步清理中连续 3/3 通过，焦点自然回到 `SysListView32`，根窗完整收到 `WM_NCDESTROY`；此前单 pane `SetFocus(root)` 造成的 pane 焦点结果不代表 Shell 菜单停用行为。隔离原型的多窗格宿主清理与 Shell 列表焦点门槛已有强证据，但仍非产品 ExplorerBrowser 的可见输入、拖放或菜单接线验收。真实 OLE 拖放、产品可见焦点键盘及完整产品接线未通过；完成隔离探针不等于批准或完成迁移。逐项颜色能力也仍未决。源码复核发现生产 ExplorerBrowser 已对子视图窗口、父链和后代安装 `WM_NOTIFY` 子类钩子，当前只处理 tooltip；已在 Private Desktop 上对子视图、父链及后代观察 `WM_NOTIFY`：八种查看模式、排序/分组、选中与强制重绘后仍为 DirectUI，通知数及 `NM_CUSTOMDRAW` 均为 0；因此当前系统上没有证据支持复用标准逐项绘制通知，仍不能排除 DirectUI 私有扩展，详见[颜色通知普查](shell-color-investigation.md#2026-10-28-private-desktop-explorer-通知普查)。2026-10-29 Private Desktop 新增 `IPropertyStoreFactory` 控制列：系统注册 schema 的 `System.Document.ByteCount` 经 `BindToObject` 工厂可显示 `2/10/10`，并有 3 次工厂绑定/创建/取值证据；`GetSortColumns` 能读回方向 `1/-1`，但行序在两方向设置后相同，排序消费仍未通过；未注册私有 PKEY 仍没有标题和值。逐项列尚未通过显示加排序完整门槛，仍仅限 NativeProbe；真实 OLE 拖放闭环及产品接线/可见验收仍未完成。包装 adapter 可经根对象创建视图、绑定两层子目录，但绝对 PIDL 仍不能由普通 Shell API 解析。带阶段标记的临时 CLSID/手工 PIDL 样本已通过 Initialize、SHCreateShellFolderView 和宿主构造；Shell 在 IShellView.CreateViewWindow 后重入 CreateViewObject，45 秒未返回，watchdog 已清理进程和 CLSID。准确阻塞原因未明，不能据此判断产品无响应，详见[逐行列调查](folder-size-column-investigation.md#2026-10-29-clsid-pidl-直建视图超时)。见[主题接点调查](shell-color-investigation.md#2026-10-28-explorerbrowser-现有子类链的逐项绘制候选点)、[原型记录](ishellview-color-spike.md)及[逐行列调查](folder-size-column-investigation.md)。 |
| 4 | Explorer 系统入口 | 每用户文件夹/磁盘右键入口采用可撤销方案，Win+E 全局钩子仍暂缓。目录参数、`/select,<对象>`、命名管道转发、注册表启停/冲突保护及“工具 → 文件夹入口…”管理窗口已进入 temp.79；窗口要求能力清单、精确 EXE 哈希和用户确认，默认不启用。发布布局、UI 模型和隔离注册表 smoke 通过；未验证真实菜单显示/启动，未修改真实 `HKCU\Software\Classes`。 |
| 5 | Q-Dir 列值筛选 | Private Desktop 的先前探针确认本地目录 3/3 项可通过 `IShellFolder2::GetDetailsEx` 读取 `System.ItemTypeText`；2026-10-29 隐藏生产 ExplorerBrowser HostProbe 已验证本机 TXT/BIN 类型筛选、同列值 OR、跨列 AND、名称条件 AND、冲突条件为空、切换/清除恢复和窗格隔离；同日又通过生产 ZIP 压缩文件夹 provider 测试，动态读取 Shell 类型值并验证逐值筛选与清除恢复。探针前台保持不变。新增 PaneControl 标签探针确认新标签默认清空列筛选、复制标签保留列条件且 Shell 结果匹配；本批补充 `VT_EMPTY` 空白筛选回归，并由 `SessionStore.Save` 写盘、全新 store 实例读回。隐藏生产 Shell HostProbe 随后从该磁盘快照创建 PaneControl/ExplorerBrowser，验证重载标签仅显示筛选匹配项；连续两次运行均通过，父窗口隐藏、前台不变。本批又以本机真实 `System.Comment` 空属性验证空值条件保留、非空值排除，生产 Shell 探针再连续两次通过。10,000 项本机目录筛选在连续两轮分别于 183/172 ms 达到稳定 5,000 项结果，清除后 93/92 ms 恢复全量；计时覆盖过滤应用至三次稳定读数，不测列头菜单值清单，不外推至网络/云 provider。真实应用重启后的完整会话与可见交互仍待验收。QDR 扩展往返已接入；DirectUI SplitButton 几何缓存、标题右侧独立箭头命中、列值清单与即时应用也已接入。菜单值枚举按每轮 64 项分批，让出 UI Dispatcher；关闭菜单、标签改变或视图代次改变时停止。菜单单列选择上限与 v11 会话限制一致，套用前还会验证累计载荷，超限时保留当前条件。当前剩余集中于可见验收：真实箭头命中、主题下菜单样式、完整应用重启恢复，以及网络/云盘/第三方 provider、慢速目录与大型列表菜单响应。HostProbe 不替代可见菜单验收，预览包未更新。详见[功能对照](shell-parity.md#q-dir-列头过滤差距2026-10-28)。 |
| 6 | Q-Dir 窗口标题栏模式 | 已增加“视图 → 标题栏显示”五种模式及便携 `WindowTitle.ini`。最新增加的“最近打开的收藏夹”只由显式点击普通目录收藏菜单更新，标题显示其路径；普通导航不覆盖；最近收藏路径已按窗口 session 独立保存在 RecentFavorite.ini，跨重启恢复且多窗口互不覆盖。已补充 SettingsSmoke 覆盖路径往返和窗口隔离；菜单/标题更新尚无可见验收。命名 `.qdr` 工作区导入是否也更新最近收藏仍待用户确认，当前不更新。详见[功能对照](shell-parity.md#窗口标题栏内容模式2026-10-29)。 |

2026-10-28：复核 Explorer 重启工具时补上结束进程前的 Shell PID 再确认。读取桌面 Shell PID、打开并验证映像期间若旧进程退出且 PID 被复用为另一 `explorer.exe`，现在会在结束前发现当前 `GetShellWindow()` 已指向不同进程并中止，避免误杀另一个 Explorer 实例。身份回归先按预期因缺少检查失败，再通过完整 Release SettingsSmoke 与 App Release build（0 警告/错误）；没有结束或启动系统 Explorer，真实桌面恢复仍待集中验收。

2026-10-28：列值筛选后端第一批：`QuickFilterSettings` 增加每列属性键与选中显示值；同列值按 OR 匹配，不同列按 AND 合并，Shell 属性读取失败时保持项目可见。`IFolderFilter::ShouldShow` 已接入 `IShellFolder2::GetDetailsEx` 与 Shell 显示格式化路径，名称通配符编辑保留已有列条件。会话 INI 升到 v11，v1–v10 保持可读，旧会话首次写回前备份；Q-Dir 扩展沿用该应用会话承载自定义条件。App Release 构建与完整 Release SettingsSmoke 通过；没有启动 ExplorerBrowser 或可见 UI，因此生产 Shell 属性格式/筛选结果、列头入口和跨 provider 行为仍未验证。下一批完成可见的列值条件入口并接入活动标签。

2026-10-28：列值筛选 UI 第二批：将 DirectUI 表头 SplitButton UIA 边界与 Shell `IColumnManager` 可见列键按顺序映射，在 ContextIdle 缓存几何；鼠标消息路径只做缓存箭头区域命中，标题文字继续由 Shell 处理。当前列值按 Shell 可见显示文本读取，条件菜单支持多选、单列清除，并通过当前 Browser 更新活动标签筛选；已有名称与其它列条件会保留。列值读取只物化目标列，避免为菜单构造整行/所有列快照。几何纯逻辑 smoke、App Release build、完整 Release SettingsSmoke 与 `git diff --check` 通过；没有启动 ExplorerBrowser 或可见 UI，所以箭头命中、菜单展开与真实筛选效果仍须用户集中验收。源码接线不代表 Q-Dir 交互验收通过。

2026-10-28：列值清单读取改为 UI Dispatcher 分批执行，每轮最多读取 64 个项目并让出消息循环；菜单关闭、活动标签切换或视图代次变化后停止并释放 Shell 接口，避免大目录清单的一次性遍历长时间独占 UI。App Release build 通过，0 警告/错误；未运行测试或 GUI，单次 Shell 属性调用的阻塞风险及真实交互响应仍待验收。

2026-10-28：列值筛选边界对齐会话格式：每列选择清单遵守 256 值限制；应用新条件前校验同一组会话字段长度、列数及 128 KiB 总载荷限制，失败时保留原筛选且不等待定时会话保存才报错。App Release build 通过，0 警告/错误；未运行测试或 GUI。

2026-10-28：为集中验收准备最新隔离候选 `src/.state/gui-smoke/20261028-030259-694-16bd5d`。源码 `0621d2bdf0f5360e8da45b3e338dea0408c1b07b`，工作区干净；Release `dotnet publish` 成功，唯一构建标识写入程序集，EXE SHA-256 `D763E219D8899DD1FE9CAD40C1EFD3E467143075FE5A351A17BB40EC144E237B`。准备目录含四个独立窗格样本、QDR/Program Starter 配置和 10,000 项递归扫描夹具。`-PrepareOnly` 在加载 UI Automation 与窗口操作前返回；未启动候选窗口、未覆盖日常发布。该产物仅供后续集中桌面验收，不代表功能验收通过。

2026-10-28：内存采样器现可用 `-ProcessId` 固定单个 ExplorerDirs 实例，并核对进程启动时间以拒绝 PID 复用；CSV 增加 GDI/USER 计数。指定 PID 与默认多实例模式各做了两点只读冒烟，错误进程身份以退出码 2 拒绝且不创建输出。没有记录固定浏览负载，因此内存归因仍待同一 PID 的受控操作采样；说明与命令见[内存采样](memory-sampling.md#当前采样器用法)。

2026-10-28：以当前 `0f23ba1` 源码运行完整 Release `ExplorerDirs.SettingsSmoke`，退出码 0，汇总 `Settings smoke: PASS`。本次没有修改产品代码、启动可见窗口或覆盖发布包。它证明当前无窗口设置/路由/状态回归集通过，不替代 ExplorerBrowser 真实鼠标键盘、主题像素或独立 Q-Dir 互操作验收。

2026-10-28：NativeProbe 的 Private Desktop 列标题菜单复验现已执行“类型”子项的 Shell 命令映射，并从 `IFolderView2` 读回主排序键为 `PKEY_ItemTypeText`；裸 `WM_COMMAND` 发给 `SHELLDLL_DefView` 未改排序，实际验证通过的是映射后的 `IContextMenu::InvokeCommand`。这一项补强了隔离宿主命令映射证据，不覆盖物理菜单选择或产品 ExplorerBrowser UI，仍留在集中验收门槛内。详见[核心操作对照](shell-parity.md#2026-10-28-列标题菜单命令隔离复核)。

2026-10-28：新增 WPF HwndHost 四窗格 `IShellView` 键盘/菜单组合探针，Ctrl+A、F2 四方向编辑焦点、兄弟 pane 焦点隔离、菜单逐窗格激活/停用和同步关闭连续 3/3 通过。此次补齐的是隔离宿主迁移门槛，不代表产品 ExplorerBrowser 可见体验；见[隔离原型记录](ishellview-color-spike.md#2026-10-28四窗格-wpf-宿主键盘与菜单组合矩阵)。

2026-10-28：Private Desktop 对照已让包装 PIDL adapter 进入真实 DefView 消费路径，并确认它只请求列标题、不请求逐项属性或比较；原始 adapter 对照相同，列显示/排序门槛均未通过。此结果关闭“只改子 PIDL 身份即可打通 DefView 派生列”的假设，逐行派生列继续留在产品之外。后续先评审新的属性供应架构；其余 Q-Dir 差距按后台可验证性继续推进，用户可见验收仍集中安排。

2026-10-28：补测 `SFV_CREATE.psfvcb` 的 `IShellFolderViewCB::MessageSFVCB(SFVM_GETDETAILSOF)` 回退：隔离自定义列让 `GetDetailsOf` 返回 `E_NOTIMPL` 后，Private Desktop DefView 的回调请求仍为 0，测试列内容为空；标准列和前台隔离不受影响。该回调只作为旧兼容后备，且微软将 `SFVM_GETDETAILSOF` 列为可能变更/不可用的弃用接口，因此否决其作为列实现路线。下一步暂停同一 `SHCreateShellFolderView` DefView 上的列消费变体，先评审需要完整 namespace 属性供应的新架构；不要把这一结论外推到所有 Shell namespace 方案。详见[逐行大小调查](folder-size-column-investigation.md#2026-10-28-旧-ishellfolderviewcb-逐项列回退探针)。

2026-10-28：现有隐藏 ExplorerBrowser `--color-capability` 探针在菜单查询和二次导航后均观测到 `DirectUIHWND`，没有 `SysListView32` 或 `SysHeader32`；父窗不可见且前台不变。故当前视图没有标准 ListView `NM_CUSTOMDRAW` 可挂接；这不排除 DirectUI 的私有消息机制，也不是 Q-Dir 逐项着色已支持的证据。详见[主题接点调查](shell-color-investigation.md#2026-10-28-隐藏-explorerbrowser-控件树快照)。

## 2026-10-28：Explorer `/select` 启动请求

- 启动解析器支持 `/select,<对象>`、`/select, <对象>` 和 `/select <对象>`；定位到对象所在目录后，通过现有 profile 单实例管道携带目录与目标项。接收端只在对应导航完成后向当前 Shell 视图请求选择，并复用已有带导航代际、选区版本和用户输入版本保护的重试机制。遇到不支持该协议的旧版接收端时，降级转发父目录，避免已有实例锁导致启动请求丢失；旧版无法执行高亮选择。
- Release SettingsSmoke 覆盖解析、路径空格、未知/缺失开关、同请求重试去重、父目录与目标项不匹配拒绝，以及旧版接收端降级；App Release build 通过，0 警告/错误。没有启动 GUI 或 ExplorerBrowser，不证明 Windows 实际命令行拆分、Shell 文件/文件夹高亮和焦点行为；加入集中 UI 验收。
- 仍不处理 `/root`、`/n` 等浏览器窗口开关，不接管 Win+E、不修改文件关联或注册表。启动参数只用于标准文件/文件夹路径；磁盘根目录自身没有可打开的父级，安全地回到原会话并提示。

2026-10-28：同步清理追踪已排除普通 HWND teardown、单纯多进程共用 Private Desktop、Shell view 存在本身及 1–4 个 pane 的数量作为稳定触发条件。最小可复现序列缩到单 pane 真实 Shell view 经 `SVUIA_ACTIVATE_FOCUS` 安装、再经 `SVUIA_DEACTIVATE` 卸载共享菜单后，同步销毁 parent：`WM_DESTROY` 子类回调正常返回，但未收到 `WM_NCDESTROY`。协调器销毁时机和焦点还原均未改变结果；当前证据指向菜单激活/卸载后的 OLE/Shell 生命周期，仍缺挂起线程栈以确认具体系统调用。process-exit 方式继续只作为隔离探针回收，不接入产品。完整矩阵、日志与证据边界见[原型诊断记录](ishellview-color-spike.md)。

本轮基于 2ac23d5 源码重跑完整 Release SettingsSmoke、无 HWND 的 pane-tab-copy-order 和 App Release build，全部通过，构建零警告错误。日志分别为 `src/.state/background-current-settings.log`、`background-current-pane.log`、`background-current-app.log`。未启动窗口、未改变发布版本。上批拖放退休修复属于隔离原型，设备地址字面量修复属于后续产品批次，均不改动已经固定的 UI 候选。

## 2026-10-28：改变隔离列原型的项目 PIDL 身份

- 在 NativeProbe 原生 `IShellFolder2` adapter 增加 opt-in 子 PIDL 包装实验；包装真实 filesystem PIDL，并在 `ParseDisplayName`、`EnumObjects` 及比较、属性、绑定、显示名、UI 对象、重命名入口解包后委托。普通 adapter 路径保持原行为，产品 `ExplorerBrowser` 未改。
- 无窗口包装合同验证通过：PIDL 身份往返、损坏 marker 拒绝、枚举项属性与 `IDataObject` 获取；数据对象的 `CF_HDROP` 和 CIDA 均解析到现存夹具文件的真实 filesystem 路径。既有逐行静态列合同及夹具文件重命名通过；原始 PIDL 合同仍通过。底层文件系统枚举器的 `Clone` 返回 `E_NOTIMPL`，adapter 保留相同结果。C++ `/W4 /WX`、NativeProbe Release 构建及 `git diff --check` 通过。
- 绝对 PIDL 对照发现当前包装身份不能直接跨普通 Shell API 解析：原始文件路径可由 `SHGetNameFromIDList(SIGDN_FILESYSPATH)` 解析，而 `ILCombine(原始父目录 PIDL, 包装 child)` 返回 `E_INVALIDARG`。因此先需要解决自定义根/项目 PIDL 的 namespace 路由，再进入 DefView 窗口验收；当前包装方案不接入产品。
- 这尚未验证 DefView 是否会因子 PIDL 身份改变而请求自定义列值或排序，也未覆盖 Shell 菜单、剪贴板传输、OLE 拖放和导航通知。按用户当前要求，DefView 窗口验证暂存入集中 UI/隔离视图验收批次。

## 2026-10-28：目录树系统图像列表后台集成复核

- 在既有图标索引 smoke 上补充生产 `NativeShellTree` 控件级检查：专用 STA 线程中创建隐藏父 HWND，读取真实 `SysTreeView32` 的 `TVM_GETIMAGELIST`，确认控件使用与 `SHGetFileInfo` 索引解析器相同的系统图像列表；创建和释放前后前台 HWND 不变。
- 定向图标 smoke 与完整 Release `SettingsSmoke` 通过。该结果加强了索引到原生树控件的后台接线证据，但不能证明 WPF `HwndHost` 中最终像素正确；普通目录及虚拟节点的可见图标仍纳入集中 UI 验收，本轮未启动窗口或更新预览包。

## 2026-10-28：temp.76 再次被动内存复采

- 对仍运行的 PID 58888 被动采样约 60 秒、13 点；进程身份与先前 temp.76 记录一致。Working Set、Private Bytes、句柄和线程在本段内仅小幅波动，完整值域、SHA-256 与限制见[内存记录](memory-sampling.md#2026-10-28temp76-再次被动复采)。
- 这是未标注浏览负载的短时观测，没有窗口输入、GC 计数器或分配栈，不关闭内存增长调查，也不据此调整架构或声称已解决泄漏。

## 2026-10-28：隔离验证环境变量设置变更刷新

- 首次后台探针构造 `MainWindow` 以获得消息 HWND，触发了窗口可见性假设失败；该失败属于探针设计，不是产品刷新失败证据。现改为独立不可见的 1×1 `HwndSource`，直接挂载生产 `SystemSettingChangeHook` 和 `EnvironmentRefresh.Reload`；不创建主窗口/Shell 文件视图、不触碰 profile，也不向其他应用广播消息。
- 通过真实 `WM_SETTINGCHANGE` HWND 消息验证当前用户环境变量新增后刷新到本进程、由 `TerminalLauncher` 启动的 `cmd.exe` 可继承；删除注册表值并再次刷新后，本进程和新 `cmd.exe` 均不再包含该变量。探针结束后 HKCU 与进程中对应临时变量数量均为 0。
- HostProbe Release 构建（0 警告/错误）、该隔离探针及完整 Release `SettingsSmoke` 通过。证据覆盖生产消息 hook/service 的进程内刷新与子进程继承；不代表可见窗口提示、系统广播接收或跨进程刷新已做 UI 验收。

## 2026-10-28：修正 3,000 项自动刷新探针的追踪断言

- 首次隐藏生产 HostProbe `--live-shell-directory-stress 3000` 退出码为 1，但日志中的创建/删除收敛、watcher/observer 事件和最终选区/焦点均通过。源码追踪发现探针把追踪变量“未设置”误当成追踪开启；产品只在值明确为 `1` 时记录 direct-select 事件。相同负载显式关闭 trace 后通过，确认失败来自探针门控而不是产品操作断言。
- 将断言条件改为与产品一致的显式 opt-in。修正后默认设置与 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 两轮均退出码 0；两轮 watcher/observer 创建与删除均为 `3,000/3,000`，目标及其它窗格最终为 `3/3`，原有两项选择和未选中的 `initial.txt` 焦点恢复。创建计数短暂滞后后于约 1.1 秒内收敛；删除侧发生一次 fallback 刷新。HostProbe Release 构建 0 警告/错误。
- 运行于未显示的四窗格 ExplorerBrowser 宿主和临时 fixture，没有桌面输入、可见 UI 或发布更新。该结果只证明本地 3,000 项样本，不关闭跨 provider/网络目录/用户实际交互的自动刷新 P0。

## 2026-10-28：Ctrl+滚轮余量不跨导航复用

- 源码追踪发现每个 ExplorerBrowser 宿主持有高精度 Ctrl+滚轮累加余量，但导航开始、完成和宿主重置都会递增 `ShellViewReadiness.Generation`，累加器此前未绑定此代际。用户在同一窗格导航前后的两次半刻度输入，可能被合并为新视图的一次切换。
- 现在累加器按读取代际清空余量；原生滚轮消息在累加前传入当前代际。回归用真实 `ShellViewReadiness.Begin` 夹在两次半刻度之间，确认旧余量丢弃且新视图内仍可累加。旧实现先因缺少代际入口构建失败，完整 Release `SettingsSmoke` 与 App Release build 随后通过，0 警告/错误；日志 `src/.state/view-style-wheel-generation-settings.log`、`view-style-wheel-generation-app.log`。
- 未创建窗口/Shell view、未运行物理滚轮或更新 release；物理高精度输入及 WPF 窗格表面仍归集中 UI 验收。此修改只影响视图样式循环，不改变普通滚轮或标签配置。

## 2026-10-28：WPF 窗格控件按完整滚轮刻度切换视图

- 继续检查窗格的 WPF 预览滚轮路径时发现它直接把单次 `MouseWheelEventArgs.Delta` 交给循环函数；该函数对小于 120 的增量也至少切换一个预设。现改为每标签累积 WPF 表面输入，只在完整刻度时应用视图切换，并按该标签的 Shell 视图代际丢弃导航前余量。
- 新 smoke 覆盖半刻度不调用切换、累计完整一格只调用一次，并静态确认窗格事件接入该路由。接入前因路由方法缺失按预期构建失败；完整 Release `SettingsSmoke` 与 App Release build 通过，0 警告/错误。日志 `src/.state/view-style-wheel-chrome-settings.log`、`view-style-wheel-chrome-app.log`。
- 未启动窗口或模拟物理滚轮；原生 HWND 与 WPF 表面的真实设备路由、滚动焦点和可见切换效果仍待集中 UI 验收。

## 2026-10-28：WPF 与原生 Ctrl+滚轮共用余量

- 复核上项后发现，原生 Shell HWND 与 WPF 窗格表面分别累加高精度滚轮余量；用户把鼠标跨过控件边界时，同一手势的半刻度可能分散在两个累加器而无法触发切换。原生路径回调给 PaneControl 的已完成刻度也不能再经过 WPF 累加，否则会重复累计。
- 现在每个 ExplorerBrowserHost 持有唯一、按视图代际隔离的累加器；原生 HWND 与 WPF 窗格表面都写入它，原生路径完成整刻度后直接应用视图切换。SettingsSmoke 覆盖两路半刻度合并为整刻度，源码接线检查确认 WPF/原生共用入口且原生完成刻度不二次累计。Release SettingsSmoke、App Release build 通过，0 警告/错误；日志 `src/.state/view-style-wheel-shared-settings.log`、`view-style-wheel-shared-app.log`，`git diff --check` 通过。
- 未启动窗口或模拟物理滚轮；真实跨控件滚动、焦点目标、可见绘制仍留待集中 UI 验收。普通滚轮和标签配置未改变。

## 2026-10-28：区分产品崩溃与原生探针故障

- 只读检查最近 7 天 Windows Application 日志：没有 `ExplorerDirs.App.exe` 的 1000/1001/1026 事件；发现一条 `ExplorerDirs.NativeProbe.exe` 在 2026-10-27 23:45:15 的 `0xC0000374` 堆损坏记录。时间对应逐行大小隔离探针，和[调查记录](folder-size-column-investigation.md)中已识别的旧 `SHELLDETAILS` 缓冲区大小/复用错误一致；修正后已有独立进程记录未再出现该崩溃。此轮只读核对，没有重新运行探针或分析转储栈。
- 该有限日志范围不能证明程序从未崩溃，也不构成用户遇到的无响应问题已解决的证据；当前没有新的产品进程崩溃栈可支持“死循环”判断。后续若再次出现无响应，应保留当时版本、进程转储和操作时间线后再归因。

## 2026-10-28：拒绝有歧义的 QDR 位置别名

- 同一窗格/标签序号同时含 `DirN` 明文路径和 `FolderN` 编码 PIDL 时，格式没有已确认的优先级。旧代码会静默选择其中一个位置，可能导入错误目录；现在跳过该冲突标签并在导入预览警告，其它标签继续导入，冲突编号也不会被当前标签复用。
- 两种字段顺序的回归先按预期失败，修正后 QDR 专项及完整 Release SettingsSmoke 通过；App Release 构建 0 警告/错误。只处理临时输入，没有读写用户 `.qdr`、启动窗口或更新 release。真实 Q-Dir 互操作和其余集中 UI 验收仍开放。

## 2026-10-27：多窗口清单的超大未来版本保护

- 复核重启链路时发现 WindowSessions.ini 的 FormatVersion 用 Int32 解析，2147483648 等未知未来版本溢出后被视为损坏格式，备份后仍允许 Create 覆盖清单。改为与其他便携配置一致的 BigInteger 版本解析，保留未知版本的只读保护，不改变窗口数或会话格式。
- 扩展现有未来版本回归，覆盖 2、Int32 上界之外及 42 位数字；旧实现先在写入保护断言失败，修复后完整 Release SettingsSmoke 通过，App Release build 零警告错误。测试仅使用独立临时配置，未启动窗口或修改用户 profile。日志：src/.state/window-registry-version-{red,green,build}.log。
- 后续重复字段复核：原来的短路条件在读到首个版本后跳过后续解析，FormatVersion=1 后追加 2 或超大版本仍可能进入损坏备份/重写路径。现在重复声明仍判为不完整，但保留所有有效声明的最高版本来决定未来格式保护，与 PinnedTabsStore 一致。新增低→高、高→低及超大重复声明回归先失败后通过；完整 SettingsSmoke 与 App Release build 通过。日志 src/.state/window-registry-duplicate-{red,green,build}.log。
- 这是配置读写保护证据，不替代真实多窗口重启/多屏位置恢复验收；固定 141624 UI 候选和 temp.76 不变，本修复进入后续产品批次。
## 2026-10-27：设备地址保留百分号字面量

- 官方 Q-Dir FAQ 146 的相对路径、环境变量、空地址刷新与盘符根目录规则，当前代码早已由 AddressInputResolver 实现（a11a37e）。不能仅从 BreadcrumbPath 的分隔符规范化函数推断这些功能缺失；本次完整调用链核对后没有重复实现。
- 发现解析器先 ExpandEnvironmentVariables 再识别设备前缀，令显式 `\\?\` / `\\.\` 路径中恰好同名于环境变量的百分号目录被改写。现在去掉输入外围引号后先保留显式设备路径，其余普通输入仍用现有展开/相对解析规则。新回归先复现旧行为，再覆盖扩展盘符、扩展 UNC、设备盘符及引号输入，普通变量展开和进程环境更新读回均通过。完整 Release SettingsSmoke、App Release build（零警告错误）通过；日志 src/.state/address-device-literal-{red,green}.log。
- 未创建目录/窗口、未读写注册表或用户环境；测试变量仅存在于测试进程并在 finally 清理。Shell 最终是否支持具体设备地址仍由原校验/导航路径决定，不将纯字符串验证当作长路径端到端验收。固定的 141624/4ec809 UI 候选保持原样，此修复归下一批。

## 2026-10-27：固定集中 UI 验收副本

- 按后台优先约束，整理相对 temp.76 的六批产品修复及外部采样工具，更新焦点/查看命令失效/标签容量/原生菜单与主题回归清单，新增 99 标签隔离 QDR。提交 359d86c 后从干净源码 PrepareOnly；版本/EXE/DLL/夹具哈希核对通过，23 项保持待验收，无目标进程或运行日志。
- 夹具经实际导入器识别 99/0/0/0 标签，完整 SettingsSmoke 通过。未启动窗口、未发布或覆盖日常版本；后续集中测试使用固定身份并逐项记录，详见[下一批 UI 验收](ui-acceptance-next-batch-2026-10-27.md)。

## 2026-10-27：内存观察补齐版本与 GUI 资源维度

- 外部 perf-sample 升为 schema 4，新增磁盘 DLL 哈希/版本和 GDI/USER 对象计数；读取失败为 null，非应用进程不伪造 DLL 身份。性能诊断 smoke 在 PS5 子进程、当前 PowerShell 和既有 release PID 上通过，含无效 GUI 句柄失败路径；未启动应用窗口、不增加产品常驻采样。
- 同一 temp.76/PID 58888 观察约一分钟：Private Bytes 160.46→132.66 MiB，Working Set 233.00→235.86 MiB，GDI 205→191，USER 117→135，进程句柄 1215→1262。负载未固定，不能归因/判泄漏或宣称优化收益，实际版本仍为已发布 4dcafc2。完整边界与原始产物哈希见[内存采样](memory-sampling.md)。

## 2026-10-27：延迟文件焦点请求绑定原标签

- 关闭地址编辑/搜索或清除筛选等路径会排队 ContextIdle 文件焦点恢复。旧回调执行时直接 FocusCurrentView，未绑定发起标签；排队后切换标签可令旧请求授权给新标签。现在捕获发起 TabState，执行时确认仍为当前标签，再按现有窗口/焦点意图门控聚焦该标签的 Browser。
- 真实生产 PaneControl 的未挂窗测试通过 Dispatcher 实际排队：旧实现下切换标签后仍读取新标签焦点许可，回归先失败；修复后不读取该许可，同一标签的有效请求仍进入当前意图检查。所有 Browser HWND=0，前台不变。完整 Release SettingsSmoke、App Release build（零警告错误）、扩展 pane-tab-copy-order 均通过。日志 src/.state/deferred-tab-focus-{red,green,settings}.log。
- 这是后台排队/身份隔离证据，不是实际焦点外观验收；关闭搜索/地址栏后快速切标签的可见体验加入集中清单。未启动窗口或更新发布包。

## 2026-10-27：普通查看切换与 Ctrl+滚轮过期输入保护

- 普通 SetViewStyle 现在与查看预设共用受当前性保护的模式写入步骤：浏览实例/导航代次变化后不报旧错误、不读回新目录状态、不报告旧切换成功。ReadViewStyle 也在取得 COM view 后、调用模式 getter 前检查实例/代次，避免导航重入后继续访问已退休目标。
- 原生 Ctrl+滚轮在调用窗格切换器前固定浏览实例、view HWND 和代次，回调后目标变化或隐藏时不再将累计滚轮量传给 Shell；当前有效目标仅切换失败时，原来的完整刻度/方向回退保持。非活动标签的迟到 CtrlWheelHandler 不允许操作当前兄弟标签。
- 后台假目标回归覆盖模式 setter 失效不报旧错误/成功、初始失效不写、只写模式及小图标默认值规范化；纯逻辑回归覆盖过期正反滚轮均不回退，并保留既有有效失败回退测试；源码接线断言覆盖标签身份保护。完整 Release SettingsSmoke 通过，App/NativeProbe Release build 均零警告错误。日志 src/.state/view-style-retirement-{red,green,final}.log。未创建窗口/Shell view，真实精密滚轮、快速切标签/导航及可见状态留集中验收，未更新 release。

## 2026-10-27：查看预设与列宽写入拒绝过期回调

- 源码审查发现手动 ApplyViewPreset 未像完整恢复一样核对浏览实例/导航代次；原生设置器重入导航后，旧调用仍可能进入列设置、清除新的恢复失败保护或读回新目录。现在预设按既有 IShellViewStateTarget 分阶段写 Mode/Columns，每阶段前后检查当前性，失效后不进入下一阶段、不发布旧错误、不清除新访问的保存保护。其他排序/分组不随预设重写。
- ShellColumnManager.Apply 增加可选当前性检查，QueryInterface 后、SetColumns 后、每列宽度写入前后均拒绝失效操作。原生完整恢复与手动预设均传入各自代次门控；无门控的既有调用保持原行为。真实 SDK 形状 COM 服务器回归先复现 SetColumns 回调失效后仍写宽度，再验证失效时零宽度写入、首列宽度回调失效后不写第二列。
- 假目标预设测试覆盖正常只写模式/列、Mode/Columns 各阶段失效不报旧错误、当前失败停止及初始过期不写；完整 Release SettingsSmoke 通过，App/NativeProbe Release build 均零警告错误。日志：src/.state/view-preset-{retirement-red,retirement-green,columns-behavior-red,columns-green}.log。未创建 Shell view/窗口或更新发布包；物理 Shell 回调顺序、快速导航与预设操作归入集中 UI 验收。Ctrl+滚轮/普通 SetViewStyle 的同类调用链仍待单独审查。

## 2026-10-27：阻止超限标签会话覆盖

- 后台追踪发现会话读取器每窗格只接受 100 标签，而保存器能写出 101 标签；普通新增、复制等路径可令运行态越过该边界。新增回归先复现旧保存覆盖，再为 SessionStore.Save 增加同一上限的写前拒绝，保留原完整文件及写入基线。101 拒绝/原字节保留、修正后继续保存、100 正常读回均通过；完整 Release SettingsSmoke、App Release build（零警告错误）、未建 HWND 的 pane-tab-copy-order 通过。日志在 src/.state/session-write-limit-{red,green,boundary}.log。
- 该提交只修复持久化端的数据保护；后续入口修复见下项。核对完整源码后确认主窗口 QDR 追加原已有四窗格总量检查，此前对该路径缺少保护的判断不准确。未启动窗口、未更新 release。

## 2026-10-27：标签入口统一数量边界

- 普通新建、显式路径/中键开新标签、复制、恢复关闭标签和跨窗格移动/复制均检查同一 SessionStore 上限；AddTab 保留最终检查，ImportTabs 在新增第一项前检查整批。主窗口原有 QDR 四窗格总量预检改用同一常量。被拒绝时保留选择、源标签实例及关闭标签快照，不自动关闭用户标签。
- 真实生产 PaneControl 的未挂窗测试先复现 NewTab 开出第 101 项，修复后验证满额拒绝新增/复制/移动、99+2 整批拒绝且选择不变、99+1 成功、恢复被拒绝不消耗关闭快照、腾出位置后恢复正常；所有宿主 HWND=0。完整 Release SettingsSmoke 通过；旧新标签位置静态接线断言更新到经过容量检查的新调用链，当前目录/指定路径语义保留。App Release build 零警告错误。日志：src/.state/pane-tab-limit-{red,green,batch,settings-final}.log。
- 本批不扩大既有每窗格 100 标签的持久化能力；如以后提高上限，需要同时评审 Session/QDR/资源成本。提示文字、中键、真实拖放及满额 UI 行为归入集中可见验收，未操作桌面或更新发布包。

## 2026-10-27：后台优先与真实 QDR 样本基线

- 用户要求尽量不使用窗口，需可见验收的项目后续集中成批。三份本机真实 QDR 只读后台往返通过，分别 43/44/28 个标签；现有 43/40/24 份原生视图字段全部解析。后两份缺失的各四份视图字段源文件本就没有。输入前后哈希一致，未启动窗口或改产品/release。范围及独立 Q-Dir 互操作门槛见[后台基线](qdr-background-baseline-2026-10-27.md)。

## 2026-10-27：已验收主题基线与当前待办

- `temp.76` 已在指定 releases 目录完成本批主题修复更新并启动验证；用户已确认右键不再使文件区闪白、菜单与边框同步且为单框贴合、窗格切换不再引起双视图闪烁。详见[重绘审查记录](theme-redraw-review-2026-10-27.md)。此前本文件中的“尚未验收”均应结合后续记录阅读，不再代表这三项的当前状态。
- 滚动条端部白块、复制标签与冷启动白底均已由用户确认解决，并以源码 4dcafc2 覆盖发布 temp.76、启动确认四窗格恢复。详见重绘审查记录与发布说明。
- Q-Dir 追赶总目标仍开放：文件操作/方向键与正反 Tab 的完整 P0 可见清单、真实 QDR 互操作及多屏状态仍需逐项取证；IShellView 的逐项颜色/大小列与真实 OLE 拖放、WPF 宿主迁移门槛未闭合，产品仍使用 ExplorerBrowser。新功能批次另行发布；同一主题优化批次经重复验证可继续更新 temp.76。
## 2026-10-27：恢复 Shell P0 验收

- 后续提交 04263d9 为 SysListView32/SysHeader32 增加原生列标题聚焦，避免 UIA SetFocus 拒绝后跳过该停靠点。第一窗格列标题 Right 导航、第二窗格列标题往返和返回后 Down 已取得可见证据；第二窗格同目录 Ctrl+C/V 后 Up 可直接操作文件，小样本副本大小/哈希一致。尚不覆盖高速连续输入、删除确认、跨窗格/慢 IO 或全模式，文件操作焦点 P0 继续开放。详见键盘验收补充。
- 干净源码 1770a00 的 125449/179260 隔离夹具中，用户确认一个样本 F2 可进入编辑且方向键保持编辑；全部窗格/模式矩阵仍开放。F6 续测前窗口已关闭，文件操作/剪贴板未执行。不根据受并发输入影响的自动 F2 观察修改产品。证据和后续顺序见[键盘验收补充](gui-acceptance-2026-10-27-shell-baseline.md)。

## 2026-10-27：修正跟随 Windows 深浅主题

- 用户要求跟随 Windows/固定浅色/固定深色；现有三态入口和 INI 已有，SystemThemeMonitor 对 Foreground 亮度错误取反，导致当前 Windows 深色解析成浅色。修正取反，选项名称明确三态，保留现有事件监听、强调色和高对比度处理。
- 实际 `--system-theme` 先复现监测链路失败，修正后当前 WindowsIsDark=True，实际 controller 的三态及重新加载 Theme.ini 均通过。没有创建窗口/Shell view，前台不变；真实系统主题切换与文件区深色绘制未验收。依据：[Microsoft Windows themes](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes)。打包 temp.76，范围/限制见[版本说明](releases/preview-0.6.0-temp.76.md)。

## 2026-10-27：发布 temp.75 与整体计划

- 用户要求整体进度/近期远期计划及新预览；已整理[进度报告](progress-2026-10-27.md)，明确实现、原型、可见验收和缺失项。按 652d35e 干净源码发布[preview-0.6.0-temp.75](releases/preview-0.6.0-temp.75.md)到指定 releases，新包独立 profile 来自 temp.74，三份配置及原文件哈希核对通过。
- 完整 Release SettingsSmoke、发布隔离 smoke、真实 publish、版本/EXE/DLL 哈希核对通过。没有启动新包或操作桌面，真实启动/GUI 门槛仍开放。产品继续 ExplorerBrowser；原型后续改动不代表已接入。本批不关闭总体 Q-Dir 追赶目标。

## 2026-10-27：跨进程竞争与互斥超时验证

- 为原型状态流 smoke 增加无窗口 race-writer 子命令：每个进程先创建宿主、写入自身内存流并报告 ready，父进程确认同一初始基线均已打开后统一放行保存。连续三轮两进程竞争均只有一个成功，另一个明确报告“打开后状态已改变”，最终字节与成功者一致。
- 另由父进程持有同一路径 mutex，放行子进程保存；子进程按既有 500 ms 有界等待返回锁占用错误，原文件保持不变。释放 mutex 后新进程可正常保存。子进程使用 CreateNoWindow，15 秒 runner 超时递归清理；父进程 join 后才删除夹具。
- 完整 `--shell-view-state-stream-smoke` 与 NativeProbe Release build 通过，构建零警告错误；没有 Shell view、桌面控制或产品变更。这补足此前未验证的协作进程争锁行为，仍不覆盖非协作外部写入者、异常退出/断电或真实 UI 冲突提示。已固定产品候选和日常 release 不变。

## 2026-10-27：刷新固定产品验收候选

- 准备脚本补充筛选分组、隐藏窗格悬停与慢 IO 关闭清单，提交 5a6f3d2 后从干净源码执行 PrepareOnly；新候选身份、哈希、夹具及未运行证据见[本批记录](gui-acceptance-2026-10-27-current-source-prepared.md)。包含旧候选之后 11 批产品修复，仍使用 ExplorerBrowser，不包含 NativeProbe 宿主迁移。
- Publish/脚本解析、版本/哈希/四窗格独立路径/10,000 文件夹具检查通过；没有候选进程、焦点或性能运行记录。全部 21 项仍待验收，不据准备成功关闭 P0。旧候选保留为历史，本批未更新 releases 日常版本。

## 2026-10-27：状态文件按同一句柄有界读取

- ShellViewStateStore 原对路径 FileInfo.Length 检查后另用 ReadAllBytes，路径替换/增长可能令实际读取越过先前检查。现在单个 FileStream（Read + Delete sharing）取得长度、分配不超过 64 KiB、ReadExactly 并检查末尾；路径原子替换不改变已打开句柄。不会把检查后的新大文件直接全部读入。
- 新增真实文件 0/65,536 字节完整读回与 65,537 字节拒绝且保留原文件；完整状态流与颜色策略 smoke 通过，NativeProbe Release build 零警告错误。没有复现替换竞态或测量进程收益，不把输入边界修复当作原有内存增长归因。未创建 Shell view，产品/release 未改。

## 2026-10-27：原型视图状态阻止过期宿主覆盖

- 两个相同 stateId 宿主先后关闭时，旧状态原可无条件覆盖新文件。宿主现于创建时读取一次状态，初始读流使用该独立快照；保存比较创建时的存在性与字节内容。沿用 PortableIniWriter 的按路径 mutex 命名，互斥内重新读取/比较/原子替换，500 ms 未取得锁则失败，不覆盖检测到的外部创建、删除或更新。初始快照与比较基线共用同一数组，不新增第二份 64 KiB 副本。
- 两宿主回归先复现旧关闭覆盖新状态，修正后验证拒绝、打开中快照稳定、新宿主可继续保存，以及外部创建/删除保留。NativeProbe Release build 零警告错误，完整状态流（含跨进程顺序恢复）与颜色策略 smoke 通过。未创建 Shell view，未验证多进程同时抢锁或真实 UI 冲突提示；非协作写入者在检查后的竞争仍不受 mutex 协调。产品/release 未改。

## 2026-10-27：关闭宿主不返回或重新持有活动对象

- 隔离 StreamViewHost Dispose 后仍保留借用 View 字段，QueryActiveShellView 可对旧指针 AddRef；SetActiveObject 也可在关闭后或 handler 内关闭后重新持有对象。现在关闭标记后清空借用 View，查询关闭宿主返回 E_ABORT/空指针，设置活动对象在 handler 前后检查关闭状态，避免新引用进入已退休宿主。
- 使用有效假 COM 对象的 CCW IShellBrowser slot 15 回归先失败后通过；IOleInPlaceFrame slot 8 验证关闭后拒绝，受控 handler 关闭后外层也不持有对象。没有使用失效指针或创建 Shell view。NativeProbe Release build 零警告错误，完整颜色策略、菜单消息链与状态流 smoke 通过；真实 view 生命周期、回调重入与产品迁移仍待验收，release 未改。

## 2026-10-27：关闭原型宿主拒绝重新创建状态流

- StreamViewHost 原没有退休状态，Dispose 后 GetViewStateStream 仍会创建流，写请求还能重新挂入 `_savedStream`。现在在清理前标记关闭，重复 Dispose 短路，关闭后的 read/write/readwrite 统一 E_ABORT、空指针，不增加成功请求计数。
- 实际 CCW IShellBrowser slot 12 回归先复现关闭后 read 模式仍成功，修正后覆盖三种模式与计数，完整状态流 smoke（含跨进程）和颜色策略 smoke 通过，NativeProbe Release build 零警告错误。未创建真实 Shell view；不会强行撤销调用方已持有的 IStream，原生 view 释放顺序仍需单独验收。产品/release 未改。

## 2026-10-27：原生状态快照保留 Shell 写入位置

- 原型 `ReadSavedBytes` 对 Shell 仍持有的同一 IStream Seek(0)/Read，未恢复位置，快照后继续写入会从末尾追加。现在记录原 64 位 seek position，在 finally 恢复；大小/完整性检查仍保留。当前磁盘状态按 session GUID 独立保存且单份限制 64 KiB，没有在此处发现按目录无界缓存；这不代表原型总体内存已评估完毕。
- 实际 SHCreateMemStream 回归先复现中间位置读取快照后再写一字节导致结果不同，修复后得到预期原位覆盖。完整 `--shell-view-state-stream-smoke`（含独立 session 与跨进程最新代次恢复）、`--shell-view-color-policy-smoke` 通过；NativeProbe Release build 零警告错误。没有创建 Shell view，真实 SaveViewState 回调顺序及产品接线仍待验收，产品/release 未改。

## 2026-10-27：菜单重新激活不复活旧命令

- 隔离 frame 旧检查只比较 pane owner；同一 owner 停用后重新安装同一 HMENU 时，旧延迟命令会重新获得执行资格。现在队列保存安装代次，安装/有效 owner 停用时使旧代次失效；解析与延迟执行都核对 owner+代次，失败替换回滚也不恢复旧命令资格。
- 隐藏普通 HWND 的真实 coordinator 消息链先复现重新激活后旧命令送达假 pane，修复后拒绝，之后新命令正常执行；另覆盖无显式停用的同菜单重装。NativeProbe Release build 零警告错误，专用 `--shell-menu-group-routing-smoke` 和完整 `--shell-view-color-policy-smoke` 通过。未创建 Shell view 或运行真实菜单模态循环，产品 ExplorerBrowser 与 release 未改。

## 2026-10-27：隔离导航历史处理回调重入与关闭

- NativeProbe 的 `ShellViewNavigationHistory` 在导航回调内可再次修改同一历史，外层随后仍提交旧结果；现在单次导航持有 busy gate，嵌套请求返回 HRESULT_FROM_WIN32(ERROR_BUSY)，完成/异常后解除。回调中 Dispose 标记关闭，借用历史 PIDL 延迟到回调退出后释放，外层返回 E_ABORT 且不提交，后续导航也拒绝。
- 回归先复现嵌套请求改变历史，修正后验证嵌套拒绝、外层正确提交、随后普通导航正常、Back 回调关闭期间 PIDL 可读、返回后历史已释放及关闭后拒绝。NativeProbe Release build 零警告错误，完整 `--shell-view-color-policy-smoke` 通过；仅合成指针池/回调，不创建 Shell view，不等于真实 COM 重入或产品迁移验收。产品 ExplorerBrowser 和 release 未改。

## 2026-10-27：退休的递归扫描失败不清除新结果

- `ScanRecursiveDirectoryAsync` 失败分支原在后台线程先清空最近结果，再进入 dispatcher 检查当前扫描；旧 worker 的迟到失败可覆盖新结果。清理现在与错误显示一起在 UI 线程进行，先确认 token/owner/path/当前标签及可见性，再修改字段。
- 未挂接实际 PaneControl 回归设置一份现有结果，对已不存在的临时子目录执行无当前所有权的真实扫描并等待其失败；旧实现清空结果而失败，修正后保留结果/路径/owner，`--pane-tab-copy-order` 通过。App/HostProbe Release build 零警告错误；不创建 Shell view，不模拟实际大目录时序。用户 F9、选择变化与晚到网络错误的可见体验仍待验收，本批未发布。

## 2026-10-27：隐藏窗格不执行迟到悬停切换

- 标签拖动悬停 timer 原只检查目标仍在集合中；布局隐藏不保证触发 Unloaded，迟到回调可切换隐藏标签并通知活动窗格。现在 IsVisibleChanged 隐藏时取消，并在 timer 消费目标后复核 IsLoaded/IsVisible，避免保留过期目标。
- 未挂接实际 PaneControl 的 `--pane-tab-copy-order` 新回归先复现隐藏窗格仍选中目标，修复后通过，既有复制顺序、路径、历史和筛选检查继续通过。HostProbe/App Release build 零警告错误，完整 Release SettingsSmoke 通过；未创建 HWND/Shell view、未执行拖放。真实拖动中切换布局、目标 Shell drop target 和焦点行为仍待可见验收，本批未发布。

## 2026-10-27：照片解码与预览尊重窗口生命周期

- 打印所选照片现在仅允许一批加载，菜单状态刷新不会在加载期间重新启用；关闭窗口传递解码取消并立即结束界面等待。解码完成后复核窗口仍可见且活动，失活时只提示重新选择打印，不打开迟到预览；关闭后不再写状态或刷新菜单。
- App/HostProbe Release build 零警告错误，完整 Release SettingsSmoke 通过；未显示 MainWindow 的关闭探针验证加载期间禁用与重复调用短路，未读取真实 Shell 选区、未创建 Shell view。图片加载 smoke 新增两张图片之间取消，验证不返回部分成功批次且首张文件已释放。真实大图/网络文件的解码中断粒度、切换应用及预览仍待可见验收；单张同步解码不保证立即中止，不宣称测得内存收益。本批未发布。

## 2026-10-27：文件视图导出沿用窗口关闭边界

- CSV/TXT/HTML/XLS 导出也接入窗口级后台等待：worker 仅持有目标路径、格式和已捕获快照，关闭后不再更新界面或报告界面错误；已确认原子写入继续，快照仍来自打开保存对话框之前的活动视图。
- `--file-view-export-smoke` 通过四格式既有检查及新增真实临时文件回归：在写入临时文件中途阻塞，取消界面等待后旧目标保持完整，放行后目标成为完整新导出。未显示窗口、未创建 Shell view，无法据此证明保存对话框/最后窗口退出的完整体验。App Release build 零警告错误，本批未发布。

## 2026-10-27：关闭窗口时结束工作区后台等待

- 工作区列表原 worker 直接捕获窗口；慢盘上的枚举、QDR 读取与写入也会一直保留界面 await。现在 worker 仅接收路径/预期文本/导出文本，关闭窗口取消本窗口等待，完成后不再更新已关闭界面；列表重复展开仍共享一次未完成枚举。已确认导出允许继续完成，迟到故障被观察，外部编辑保护和原子保存保持原实现。
- 定向受控任务先复现已取消窗口仍启动操作，修正后通过关闭前取消、正常结果、异常传播及关闭中取消等待/底层任务仍运行。完整 Release SettingsSmoke 通过；App/HostProbe Release 构建零警告错误。顺序运行 `--window-close-lifetime` 通过，验证未显示的两个真实窗口独立取消及既有 host 清理，不创建 Shell view，前台不变断言通过。
- 这是生命周期修复，没有测出进程内存收益，也不声称能中断阻塞 IO；慢盘导入导出的真实对话框、进程退出时导出完成情况仍待可见验收。本批未发布，之前冻结的 GUI 候选不含本批修改。

## 2026-10-27：文件快捷键补发仅接受 Shell S_FALSE

- 原方向/列宽兜底已限制 S_FALSE，但剪贴板/全选/撤销/重做在 accelerator 错误后仍进入命令映射。统一为仅 WM_KEYDOWN + 精确 S_FALSE 可补发，S_OK、错误、未知状态、keyup 和系统键不发额外文件命令，错误路径不新建文件操作焦点恢复请求。
- 输入固定原 browser/view/navigation visit/焦点 HWND，在 GetCurrentView、原生 accelerator 和释放 COM 后复核；已退休输入直接消费，避免把旧键发到新目录。S_OK 文件操作允许正常移动焦点，只在同访问内保留既有恢复路径。契约依据：[Microsoft IShellView::TranslateAccelerator](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-translateaccelerator)。
- 专项 `--shell-file-commands-smoke` 先复现 HRESULT 错误仍映射文件命令，修正后覆盖八组快捷键/别名与各状态、消息类型。App/HostProbe Release build 0 警告/错误。测试是策略与源码接线，不调用真实 IShellView accelerator；COM 重入、窗口输入的物理目标和文件操作后焦点仍待可见验收，本批未发布。
- 完整 Release SettingsSmoke 通过。导航焦点与标签状态探针首次并行时均在最后的前台不变断言失败，来源未确定，不能记为通过；完整配置检查结束后分别顺序复跑均通过。未显示窗口/未创建 Shell view 的断言保留，前台保护不放宽；这不是实际 accelerator 或键盘端到端验证。

## 2026-10-27：新确认的原生分组优先于旧筛选意图

- 保留临时分组的合并此前会无条件覆盖捕获值，包括 Shell 已呈现的另一种非空分组或方向。现在完整捕获读到不同的非空 key/ascending 时接受新状态、取消旧 timer 并递增筛选请求，防止后续旧刷新重新排队覆盖；临时 None 仍用已确认意图保护。记住新意图时直接更新缓存，避免把旧缓存误当作新观测值触发取消。
- 未挂接实际宿主的合并回归先复现新分组被旧意图覆盖，修正后通过不同 key、仅方向变化、timer/request 退休和新意图替换旧缓存。原临时 None/取消/失败快照检查保留。输入为模拟完整捕获，不是物理原生菜单；显式“无分组”与临时 None 的区分、枚举期间菜单操作和最终可见分组仍待验收，本批未发布。

## 2026-10-27：限制应用图标缓存的长期引用

- 源码发现 ShellIconProvider 的进程静态 Dictionary 无容量上限，按虚拟位置/扩展名保留图像。普通文件系统目录共用 `<folder>`，因此不能用该发现解释此前普通四窗格的大部分占用。改为固定 256 项 LRU，命中更新顺序、大小写等价键替换不增加容量；淘汰仅删除缓存引用，现有图像消费者保持其引用，后续缺失仍走 Shell 加载，原 HICON finally 清理不变。
- 实际生产缓存压力先复现 1,024 个合成键后保留 1,027 项，修复后为 256 项；淘汰后的此电脑图标重新加载像素一致。独立策略检查覆盖最近使用、替换、连续追加 4,096 键、缺失和已有冻结图像消费者引用保持，不调用强制 GC 或裁剪工作集。专项图标 smoke 与 App Release build（0 警告/错误）通过；没有 Shell 文件视图/桌面输入。
- 这里只证明有界引用，不测进程 MB 收益，不裁剪原生浏览历史或驻留文件视图；真实长期浏览、内存曲线、DPI 与可见图标仍待新构建验收。分析边界见[内存记录](memory-sampling.md#2026-10-27应用图标缓存容量检查)。本批未发布。

## 2026-10-27：筛选的临时未分组不覆盖标签设置

- 筛选为绕过 Shell 空枚举会暂时清除分组，普通捕获此前可能保存“无分组”，导航取消恢复计时后也会丢失原意图。现在清除前保留已确认的分组键/方向并写入缓存；完整原生捕获仅覆盖这两个临时字段，继续保留实时模式、图标、排序和列。计时取消/恢复失败保留意图，当前分组或完整视图成功恢复后解除，宿主销毁时清理。
- 旧刷新回调在创建队列前即核对请求，不能替换新计时器/缓存；枚举计数 COM 返回后复核 visit 与 timer，分组 setter 前后也复核，避免旧回调写入新访问。完整视图恢复同时核对筛选请求，避免结束旧恢复时清掉新筛选意图。
- 未挂接实际宿主检查先复现缓存不保留待恢复分组、旧请求替换当前意图两项失败，修正后覆盖方向、取消计时、模拟原生未分组快照保留实时模式/大小，以及缺少原生视图导致恢复失败后的实际 Pane 会话快照。没有创建 Shell view 或输入键鼠；实际枚举/切换标签/导航/重启、恢复成功后释放意图、枚举期间手动改分组与可见排序仍待验收。本批未发布，旧验收候选保持原哈希。

## 2026-10-27：快速筛选失败回滚与界面异常处理

- IFolderFilterSite 查询失败原先静默返回，现通过独立原生附件组件报告 QueryInterface/SetFilter 失败并成对释放 COM 引用。当前筛选应用/刷新异常时恢复原设置和原筛选，尝试恢复分组、撤销过期分组计时，回滚期间保持选区恢复抑制；请求或访问已改变则不让旧回滚覆盖新状态。
- 应用/清除入口捕获错误并在当前窗格显示，回填原模式/排除/文件夹选项；失败清除不隐藏编辑区，失败模式不加入成功历史。原状态恢复本身失败时报告 AggregateException 的不完整恢复信息，不吞掉原始错误。未创建原生视图的标签仅保存设置，避免无浏览器 Refresh 异常。
- 原生附件专项先复现缺少筛选接口仍当成功，修正后通过真实托管 COM site 的查询、槽位调用、取消筛选、原生 HRESULT 拒绝及无效过滤器检查；不是 ExplorerBrowser。未挂接 Pane 回归覆盖一次性通知故障、设置/编辑器回滚、历史排除、清除失败、回滚通知再次失败及后续成功；异常来自可控通知订阅者，未验证真实 Shell 失败的整个 UI 链路。没有创建 Shell view 或输入键鼠，真实分组/选区、慢刷新与提供程序拒绝仍待验收；本批未发布。

## 2026-10-27：完整视图恢复失败时保护原设置

- 原先仅列设置恢复失败会阻止捕获；模式/排序/分组失败只是报错，没有自定义列时视图不可用也不受保护。统一为完整视图恢复保护，保留原缓存，所有字段成功且 browser/read generation 未变才解除恢复失败状态；旧访问重入后停止后续写入及错误发布。
- `ShellViewStateRestore` 与既有捕获组件对称，保留原生 setter/COM 槽位及尽量应用其他字段的行为；默认无列/排序覆盖时不额外发送设置。ViewCaptureFailed 立即包含恢复失败，后台标签不必等待下一次捕获才阻止会话覆盖。查看预设从首次写入起也保护部分应用结果。
- 未挂接实际宿主先复现“视图不可用但无自定义列”和“下一次捕获前后台保存仍认为有效”两项失败，修复后通过。专用 `--view-state-restore-smoke` 覆盖四字段 HRESULT 失败、稳定重试、访问失效、无自定义列和参数传递；完整 Release SettingsSmoke 通过，没有创建 Shell view 或输入键鼠。真实提供程序拒绝、部分恢复外观及重新导航后的恢复仍待验收。
- 先前固定的 `62274bc` 隔离候选不包含本批产品修复，后续可见验收前需重建并重新记录哈希；本批未更新候选或 release。

## 2026-10-27：固定当前源码的隔离验收候选

- 修正 GuiSmoke 上级目录检查仍等待旧夹具 `files` 的错误，改为从实际初始目录推导；更新 Modern F4、原生导航键、窗口位置及标签交接的待验收清单。准备清单增加源码提交、dirty 状态与唯一构建标识，并检查 DLL ProductVersion，避免沿用旧默认 temp.3 文字。
- PrepareOnly 成功生成 `20261027-045305-773-a09a78` 候选，独立核对 EXE/DLL 哈希、构建身份、四窗格路径/样本、10,000 文件夹具与上级定位条目；候选未启动、无运行测量/焦点日志。产品源码为 `62274bc`，准备时仅脚本有未提交差异，清单据实保留 dirty=true。具体产物身份、八阶段验收顺序及未运行结果见[验收准备记录](gui-acceptance-2026-10-27-prepared.md)。未创建 release，P0 与真实 GUI 门槛不变。

## 2026-10-27：Home/End 与翻页键保留原生输入路径

- 原兜底只覆盖四方向，Home/End/PageUp/PageDown 被 Shell 拒绝后仍可能进入 WPF 焦点导航。共享导航键判定扩为 VK_PRIOR～VK_DOWN 八键：文件内容仍只在 TranslateAccelerator 返回 S_FALSE 时补发一次；重命名编辑框直接接收同组 keydown，保留原消息及 Ctrl/Shift 状态。不改 Tab/Enter/Escape，也不拦截 Alt 系统键。
- 专项 `--shell-file-commands-smoke` 先在 PageUp 未转发用例失败，修正后验证八键路由、S_OK/错误/keyup/系统键不补发、列头/编辑框/非内容窗口排除和重命名入口。没有创建 Shell view 或注入键鼠；这里只证明策略与接线，实际 Home/End 文本选择、文件列表范围选择、翻页、不同查看方式及物理焦点仍待可见验收。本批未发布。

## 2026-10-27：当前目录操作消除标签路径副本

- 上批修正快照后，继续发现 Pane.CurrentPath 和搜索关闭等操作仍通过单独的 TabState.Path 读取滞后路径。现移除该可变副本，让 Path 直接来自 Browser.CurrentPath；新标签当前目录模式、地址编辑/祖先树、目录扫描、菜单路径与对外当前目录消费者统一使用已确认位置，界面仍按原通知更新。
- 扩展未挂接实际窗格检查：旧实现先在“当前目录命令应使用浏览器已确认目录”失败；修正后覆盖旧地址仍显示时读取当前目录、关闭 pending 搜索不退回旧目录、新建标签沿用当前目录和地址编辑初始值。原会话/固定标签/复制/关闭恢复检查继续通过。地址编辑夹具改为先更新 host location 再投递合成目录通知，与生产顺序一致，保留四窗格新输入/迟到结果检查。没有创建 Shell view 或输入键鼠；真实快速导航与命令行进程工作目录等仍待可见验收，本批未发布。

## 2026-10-27：标签快照使用浏览器已确认的目录

- 原生完成先更新 Browser.CurrentPath，Pane 的 Path 要等 Background 通知才更新；会话/固定标签快照、复制、跨窗格复制和关闭快照此前读取后者，交接期间可能把旧路径与当前视图设置配对。五个入口现统一读取 browser 的目录身份，保留标签 UI 的排队通知方式，搜索仍保存真实目录范围。
- 在既有未挂接窗格回归中，使用未挂接 host 的 Navigate 更新目录但不投递 Pane 通知，明确模拟这一交接阶段；旧代码先在会话路径断言失败。修复后会话与固定标签路径一致、两种复制和关闭恢复均使用新目录，旧 MRU 独立性与固定分区回归继续通过，确认所有宿主 HWND 为零/COM browser 为空。没有创建 Shell view、注入输入或证明原生时序；真实快速导航后复制/关闭/重启仍待可见验收，本批未发布。

## 2026-10-27：视图配置捕获拒绝跨导航数据

- 捕获模式、排序、分组和显示列原先只检查返回码与数据完整性，没有核对 COM getter 期间发生的导航。宿主现于 GetCurrentView 前记录 browser 实例和 Shell read generation，读取组件在每个 getter 后核对；访问变化立即停止后续读取，标记捕获失败并保留最后完整缓存，后续稳定捕获可恢复正常保存。
- 新 ViewCaptureGenerationSmoke 在五类 getter 中用实际 ShellViewReadiness 代际变化模拟导航重入，旧逻辑先复现“过期快照被认为完整”；修复后拒绝整份旧数据、停止继续读取、保留缓存全部字段，并验证稳定重试及读取前已过期时不调用 getter。完整 Release SettingsSmoke、App/HostProbe Release build（0 警告/错误）、未挂接标签复制/恢复与目录回调检查通过。没有创建 Shell view 或操作桌面；真实 COM 重入/慢目录切换、可见排序与配置保存仍待验收，本批未发布。

## 2026-10-27：恢复关闭标签保留地址栏记录

- 源码发现关闭快照只保留 TabSettings，“恢复关闭的标签”重建标签时会丢失原地址栏最近访问记录，而复制标签已保留该记录。关闭快照现同时保存独立的 AddressPathHistory 副本，恢复后交给新标签；不保留已关闭的 Browser/TabState，不增加跨重启持久化，仍最多保留 20 个关闭标签、每份地址记录最多 20 条。
- 在既有 `--pane-tab-copy-order` 未挂接实际窗格检查中，先复现关闭恢复后记录只剩当前地址；修正后通过完整顺序、关闭后旧对象修改隔离、恢复后的复制隔离，以及 Browser HWND 为零/COM browser 为空检查。保留原固定标签分区与复制顺序验证，没有创建 Shell view 或操作桌面。这里只恢复地址栏下拉 MRU，不恢复已销毁 Shell Travel Log；真实关闭/恢复菜单、下拉交互及焦点仍待可见验收。

## 2026-10-27：新窗口跟随来源窗口所在显示器

- Ctrl+N、打开 `.qdr` 工作区和旧会话无位置记录的附加窗口，原先都以主屏 `SystemParameters.WorkArea` 限制坐标，副屏来源可能被拉回主屏。三处改为共享 `CascadeFrom`，原生目标初始化后以来源窗口所在显示器工作区和实际窗口尺寸计算错位位置；已保存的窗口位置优先，不改窗口大小。
- 新 `WindowCascade` 仅使用原生屏幕坐标，不将 WINDOWPLACEMENT 工作区坐标混入 SetWindowPos。偏移按来源 DPI 换算；定位后用实际尺寸再约束一次，以处理同步 DPI 尺寸变化。来源最小化时从同一屏工作区起点安排新窗口，不还原或激活来源。
- 专项配置 smoke 覆盖右/左/上方显示器、负坐标、越界来源、右下边界、超大窗口和缩放偏移；旧的仅上限约束在越界来源用例失败，修复后通过。`--window-close-lifetime` 使用两个不可见普通 HWND 验证真实定位、尺寸保持、工作区限制、来源不动及窗口未显示，前台保持不变；实际 MainWindow 关闭与最小化恢复回归继续通过。未创建 Shell view，未验证真实多屏 WPF 初始化/不同 DPI 的可见呈现；本批未发布。

## 2026-10-27：再次启动与目录转发保留最大化状态

- 源码发现两个单实例入口都将最小化窗口强制设为普通窗口：无参数再次启动，以及向已有窗口转发目录。现在共用 `RestoreFromMinimized`，按最小化前状态恢复普通/最大化；F11 沉浸窗口仍恢复为普通无边框窗口，保留退出 F11 的独立状态，未最小化的窗口不变。
- 扩展 `--window-close-lifetime`，以原先强制 Normal 的逻辑做负向对照，断言“最小化的最大化窗口应恢复最大化”失败；修正后在未显示的实际 MainWindow 检查上述四种状态与 HWND 始终为零，并继续通过窗口关闭、宿主释放及跨窗口所有权检查。完整 Release SettingsSmoke 通过，App/HostProbe Release build 通过；没有激活窗口或创建 Shell view。真实再次启动/目录转发、物理最大化位置与双屏仍待可见验收，本批未发布。

## 2026-10-27：导航完成通知与用户提交关联

- 两项回归先复现旧行为：同一路径再次提交但尚未进入原生 Pending 时，旧完成清除了新请求的重试目标；旧目录通知把新地址提交提前视为完成并退出编辑。目录/搜索/失败通知现携带请求编号，接收时再次核对，标签实际位置与输入完成分别处理。
- 已登记请求在计数之外保留本批次最新提交编号；部分重复目标回调不确认输入，原生重复目标仍走现有实际位置核对。迟到通知可更新当前原生视图的标签位置，但不能结束新地址编辑、清掉新搜索 pending/范围、隐藏其搜索框、覆盖新状态或消费新焦点意图。已报告失败的请求保留错误和重试目标，不能被同目标批次后续成功改写为输入成功。新提交在校验前即使旧准备失效；目录解析失败回调也复核原探测代际。
- `--folder-navigation-callbacks` 通过五类真实目录 PIDL、同路径新请求、真实 Dispatcher 排队通知、同目标提前失败，以及真实搜索 PIDL 的搜索→目录回调交接；`--address-editing` 在四个未显示窗格验证连续地址提交、旧目录/搜索/失败通知、新搜索 pending 和当前结果确认。既有 `--navigation-focus-requests` 通过。测试不创建 ExplorerBrowser/文件视图；搜索接受阶段在控件检查中显式模拟，原生回调顺序也为合成。真实慢目录、连续输入、ERROR_BUSY 重试与物理焦点仍待可见验收，P0 保持开放。

## 2026-10-27：Shell 别名与虚拟目录的导航请求识别

- 发现 `OnNavigationPending` 只用文件系统路径查找程序发出的目录请求，导致“此电脑”等虚拟位置被误认成视图内用户导航，取消后续目录探测。`shell:Downloads`/桌面别名又可能解析成不同的文件系统路径，原输入登记与回调清理不匹配。
- 现在从已解析 PIDL 取得持久化目录身份，在重新确认请求有效后登记并更新重试目标；成功、失败、异常及取消使用同一身份清理。Pending/失败回调同时识别虚拟和文件系统目录，保留重复目标计数。
- 新 `--folder-navigation-callbacks` 使用未挂接的实际宿主与五类真实 Shell PIDL，先复现旧实现误判；修正后验证完成清理、迟到 Pending 保留新探测、旧失败不发布错误、不覆盖新重试目标、重复目标与真正原生导航取代旧请求。App/HostProbe Release build 均 0 警告/错误，既有地址输入/导航焦点检查通过。没有创建 ExplorerBrowser/文件视图或操作桌面；这验证回调记账，异步解析与实际 Shell 回调顺序仍待验收。连续地址提交的 UI 完成身份仍需进一步处理，不能据此声称所有导航竞争已解决。

## 2026-10-27：保护地址和搜索的新输入

- 提交路径后继续修改地址会撤销旧提交对编辑框的接管；旧目录完成仍更新标签位置，但不覆盖新的未提交路径，也不结束编辑。未修改的提交仍在完成后正常退出编辑。
- 搜索框记录新输入，迟到的搜索成功/失败以及重新聚焦不再直接改回旧关键词；显式提交规范化首尾空白，相同查询的结果确认输入。关闭搜索和切换标签仍按对应目录/标签同步，不增加跨标签草稿持久化。
- 既有 `--address-editing` 扩展到四个未显示的实际窗格，分别复现旧地址覆盖和搜索覆盖失败，修正后覆盖成功/失败、清空输入、新提交、关闭搜索及后台标签通知。配套 `--navigation-focus-requests` 与窗口位置配置 smoke 通过；没有创建 HWND/Shell view 或操作桌面。这不验证原生回调次序，连续提交两个地址与慢目录/搜索交叉操作仍待真实窗口验收。

## 2026-10-27：目录与搜索完成时消费并复核焦点请求

- 发现目录/搜索完成回调只按待聚焦标记与可见性直接调用 `FocusView()`，绕过当前窗格、编辑意图和前台窗口检查。两个回调现共用 `RestoreNavigationFocus`：只处理已完成的当前导航，先消费其两个焦点标记，再通过已有 `RestoreContentFocusIfNeeded` 恢复；原生命名框和列标题持有焦点时跳过。旧导航不能清掉新导航请求，重复完成不能再次恢复。
- 启动及布局切换保留文件区焦点意图，Loaded 回调也复核活动状态；用户转入编辑框后取消。提交搜索成功进入 pending 且仍由当前活动窗口的搜索框/按钮持有交互时，立即将操作交给文件区；后续回到编辑框会取消意图。同步搜索启动失败不转移焦点。显式聚焦发生在导航 pending 期间时，保留请求直到目标视图完成，避免只聚焦旧视图便丢失请求。
- 新 `--navigation-focus-requests` 在未挂接的实际宿主先复现旧待聚焦标记残留，修正后覆盖拒绝/允许入口、旧/重复/未完成导航、两类请求独立消费、启动意图、编辑取消及同步搜索失败。没有 HWND、Shell view 或桌面输入；本测试不验证真实 SetFocus/UIActivate、慢目录/搜索框切换、列标题与原生命名的物理焦点。P0 可见验收仍保留。

## 2026-10-27：标签切换的延迟焦点遵守当前输入目标

- 源码发现 `Tabs_SelectionChanged` 的 Loaded 回调只核对标签、IsLoaded 和 IsVisible，未核对窗口当前活动窗格与文件区焦点意图；回调排队期间若用户转到其它窗格、地址栏或标签标题，旧回调仍会调用 `FocusCurrentView()`。
- 该入口现在复用现有 `MayRestoreFileFocus`，在执行时再次确认当前窗格、文件区意图及窗口活动状态。保留原回调的标签滚动和可见性检查，不改变 Tab 停靠顺序。
- App/HostProbe Release 构建 0 警告/错误，既有焦点遍历矩阵和四个未显示窗格的地址编辑回归通过。它们不复现真实焦点竞争；切换标签后立即转到其它窗格、地址栏、标签标题，以及 Ctrl+Tab/鼠标标签选择后的实际焦点仍待可见验收。粘贴/删除焦点 P0 没有因此关闭。

## 2026-10-27：下载入口跟随 Windows 实际位置

- 地址位置菜单此前拼接 `%USERPROFILE%/Downloads`，在 Windows 中迁移下载文件夹后仍会使用默认路径。改为 `shell:Downloads`，继续经过现有 Shell 文件夹校验和导航路径，不创建文件夹或修改用户配置。
- 现有地址菜单专项 smoke 增加独立 `SHGetKnownFolderPath(FOLDERID_Downloads)` 对照，当前菜单目标解析为系统 API 返回的相同文件系统目录；祖先层级及驱动器菜单检查通过。本轮没有改变真实下载目录位置，迁移前后操作及菜单点击仍待可见验收。

## 2026-10-27：现代地址栏点击空白直接编辑

- 对照用户此前反馈，确认现代面包屑只有路径按钮导航和独立“编辑”入口，空白点击未处理。现在为可命中的透明背景接入冒泡左键事件，复用完整路径编辑/全选；路径按钮自行处理点击，继续保留导航，右键不触发编辑。
- `--address-editing` 在四个未显示的实际窗格先复现旧行为失败，修正后覆盖空白、分隔符、路径按钮、右键及经典样式共享编辑入口；窗口/浏览器 HWND 均为零，未创建 Shell view 或注入桌面输入。物理命中、实际键盘焦点和四窗格布局仍待可见验收。

## 2026-10-27：首次取得运行版的 .NET 堆与分配指标

- 对同一 temp.74/PID 17608 用临时 `dotnet-counters` 采集一分钟，并行复用进程采样器。最近 GC 报告的堆大小（含碎片）10.92–14.50 MiB，GC 提交量保持 29.95 MiB；同期 Private Bytes 155.08–166.93 MiB。55 秒托管分配流量约 70.42 MiB，不等于保留或泄漏。原始文件哈希、工具版本和解释见[内存记录](memory-sampling.md)。
- 下一步优先拆分 GC 之外的运行时/WPF/Shell/扩展等占用，再评估架构收益；不能把差值直接算给 Shell，或凭此预测 C++ 重写节省量。没有强制 GC、转储、应用重启或桌面操作；用户工作负载未固定，计数器有开销，不是长期泄漏或修复 A/B 验收。

## 2026-10-27：关闭窗口时显式释放全部原生宿主

- 发现窗口 `Closed` 原先只撤销部分订阅，没有释放各标签及目录树的 HwndHost。普通隐藏 STATIC 子窗口实验确认：HwndSource 释放后，子 HWND 仍存在且 DestroyWindowCore 未调用；显式 Dispose 才执行销毁。不能依赖窗口消失或 GC 清理 Shell 宿主。
- 现保持 Closing 的会话保存，Closed 时释放该窗口所有活动/非活动标签、各窗格目录树、地址弹出树及共享树，同时取消窗格后台任务和定时器；停止延迟保存，并拒绝关闭后的保存调度。其他独立窗口保持运行。
- 新 `--window-close-lifetime` 先复现原实现未释放宿主，修复后通过两个未显示的实际 MainWindow 验证所有权隔离、全部宿主 Dispose、保存先于清理和迟到保存拒绝。既有 `--tab-host-lifetime`、`--pane-tab-copy-order` 通过，App/HostProbe Release build 0 警告/错误。本轮未创建真实 Shell 文件视图，未量化释放内存；实际 Ctrl+N/关闭窗口后的 HWND/COM、进程内存与可见操作仍待验收。

## 2026-10-27：取得 temp.74 的三分钟运行内存样本

- 对已运行 PID 17608 只读采样 37 次并核对进程生命周期、EXE/DLL 哈希。Working Set 为 268.01→272.55 MiB，Private Bytes 为 158.44→158.89 MiB；Private Bytes 范围 153.06–162.65 MiB，句柄 1368→1355、线程 45→44，见[采样记录](memory-sampling.md)。
- 此短窗口数值上下波动，不能据此证明长期无泄漏或某项优化有效；未固定用户操作，没有启动窗口、操作桌面或强制回收。继续关注固定版本/工作负载下的长期增长来源。

## 2026-10-27：修复复制固定标签到空白标签栏时的排序

- 原因：`AddTab` 已将副本加入固定标签区，随后计算目标位置时再次把副本计入固定标签数量；把固定标签复制到包含普通标签的窗格空白处，会把副本插到第一个普通标签之后。
- 现在先从显示顺序中移除刚创建的副本，再计算目标插入位置。实际未挂接窗口的 `PaneControl` 回归先复现旧代码失败，修复后覆盖固定/普通标签的目标前、目标后和空白追加、会话保存顺序、选中索引及独立宿主。所有宿主的 HWND 为零、COM browser 为空，未创建 Shell 视图或操作桌面。
- `--pane-tab-copy-order`、原转移策略 smoke 通过，App/HostProbe Release build 0 警告/错误；物理 Ctrl+拖动、可见标签排列和跨窗格焦点仍待 GUI 验收。

## 2026-10-27：修复跨窗格移动后关闭标签的宿主泄漏

- 对照 WPF HwndHost 源码，确认移出/移入视图树走原 HWND 暂存/重挂路径，不会再次调用 BuildWindowCore。跨窗格移动新增的保留标记因而不会清除，之后关闭标签会提前退出 DestroyWindowCore，跳过原生窗口及浏览器清理。
- `--tab-host-lifetime` 使用普通隐藏 STATIC HWND 验证连续 8 次移出/移入保留同一 HWND，且构建只执行一次、销毁只在 Dispose 执行；再对未创建 COM 浏览器的产品宿主调用真实清理入口，旧代码复现 HWND 未销毁，修复后通过。移除多余标记/手动重挂，保留原标签对象及 WPF 默认生命周期。
- 真实 Shell 跨窗格探针增加“移动后关闭、HWND 销毁及 browser 释放”断言，本轮未运行该探针。当前证据不量化内存收益，也不解释用户早前的全部内存增长；可见拖动、Shell Travel Log 和 COM 清理仍待桌面验收。

## 2026-10-27：补齐隔离预览的偏好配置复制

- 对照运行时配置存储，补齐发布清单遗漏的 11 个 INI：快速筛选历史、滚轮焦点、标签栏显示、多行标签、拖动悬停、新标签目录、状态栏显示/选中大小/目录大小、复选框选择及整行选择。此前这些偏好在隔离预览中会恢复默认值。
- 发布夹具首先复现 `QuickFilterHistory.ini` 缺失；修复后逐字节复制、源配置不变、只复制已登记窗口、未知 INI/无关文件排除等检查全部通过。没有创建真实发布包或启动 GUI。

## 2026-10-27：默认恢复窗口位置、大小与最大化状态

- 根因：此前会话只记录布局/标签，主窗口几何没有持久化；附加窗口还会在启动时重新错开放置。现默认逐窗口保存到 `<session-stem>.WindowPlacement.ini`，普通关闭、周期保存与应用重启共用保存入口，已有记录优先于错位排列。
- 成对使用 Windows GetWindowPlacement/SetWindowPlacement 工作区坐标，保留左侧/上方副屏负坐标；Windows 负责处理完全落到已断开显示器之外的坐标。最小化后仍按进入前的普通/最大化状态恢复，F11 期间保存进入全屏前的位置，不持久化沉浸全屏状态。
- 配置具备外部修改、新版本和损坏备份保护；预览打包复制主窗口及已注册窗口的位置侧车文件。Release 构建、专项配置/原生数据 smoke、发布脚本隔离夹具均通过；未创建任何测试窗口。真实双屏位置、不同 DPI、最大化还原、断屏回退和 F11 重启仍待可见验收。本批未发布新包。

## 2026-10-27：精简主工具栏编辑命令

- 按用户反馈移除主工具栏的剪切、复制、粘贴、删除、重命名、撤销和重做按钮；保留编辑菜单、Shell 快捷键及原生右键菜单入口。属性、新建文件夹和浏览/布局工具保留。
- 调整命令状态刷新，只更新仍存在的工具栏按钮；此调整不改变 Shell 命令分派。

## 2026-10-27：跨窗格移动和复制标签并保留 Shell 历史

- 普通拖动标签到其他窗格会移动原 `TabState` 与 ExplorerBrowser host，保留 Shell Travel Log；Ctrl+拖动会复制当前视图为独立 host。仅允许在相同固定状态的标签分区中放置；移动不能清空源窗格最后一个标签。点击标签标题按左右半区插入，标签栏空白处追加。
- Release SettingsSmoke 覆盖策略及接线；隐藏 HostProbe 使用真实 PaneControl/ExplorerBrowser 验证同一 TabState 和原生 host HWND 被保留、目标宿主尺寸更新、Back 可以穿过移动前历史；Ctrl 复制为新 host。前台未变，未操作可见 GUI。真实鼠标拖动、跨 DPI/显示器和拖动视觉仍待可见验收。

## 2026-10-27：新建标签可使用当前文件夹

- “标签页 → 新建标签时使用当前文件夹”补齐 Q-Dir 的同名可选行为。默认关闭时仍从用户文件夹开始；开启后，工具栏、Ctrl+T 和标签栏空白双击等普通新建标签从当前窗格路径开始。便携配置保存在 `NewTabLocation.ini`。
- 复制标签、指定目录的新标签打开、会话恢复和 Q-Dir 导入仍按原有路径处理。Release SettingsSmoke 与 App Release build 通过；未启动 GUI，菜单切换、跨重启保存和新标签导航仍待可见验收。

## 2026-10-27：补齐空白标签栏双击新建

- Q-Dir 的公开功能清单支持在标签栏空白处左键双击新建标签。Explorer Dirs 现把该手势接到已有 `NewTab()`，沿用 Home 目录与默认视图；点击标签标题仍由现有代码关闭标签。静态 SettingsSmoke 与 Release build 覆盖源码接线，未操作 GUI，因此真实鼠标命中、现有标签关闭和新标签视觉行为仍待可见验收。

## 2026-10-27：加入 Q-Dir 式拖动悬停标签自动切换

- Q-Dir 官方英文菜单表列出 “Auto select drag-over time” 及 0.2、0.5、1、1.5、2、3 秒档位；Explorer Dirs 在“标签页”菜单提供相同档位及关闭选项。默认 1 秒是本项目的体验选择，官网字符串未说明 Q-Dir 的默认值。
- 每个窗格只有在可识别的 Shell 文件/文件夹拖动悬停到另一个标签时才启动计时；应用自己的标签重排不触发。拖出、换目标、放下或窗格卸载会取消；计时到期只切换标签，不执行文件操作。设置写入独立便携 `TabHoverActivation.ini`。
- 配置、目标替换/陈旧 DragLeave、取消和接线 SettingsSmoke 通过，App Release build 0 警告/错误；没有启动 GUI，因此鼠标悬停时序、拖放游标反馈和切换后是否顺利落入文件区仍待可见验收。[Q-Dir 官方功能表](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)。

## 2026-10-27：复核 Q-Dir 当前官方版本与地址栏树基线

- 官方产品页仍列 Q-Dir 12.82（2026-10-24）；最近版本说明提到地址栏目录树改进，但没有详细交互规格。Explorer Dirs 已有 Modern 地址栏 F4 两阶段树/路径编辑和驱动器/祖先定位，本轮未发现仅凭版本说明即可确认的新代码差距。下一步应把重点放在当前版本的可见对照验收与已记录的 Shell P0 缺口，避免将泛化优化说明当作具体需求。依据：[Q-Dir 官方产品页](https://www.softwareok.com/?seite=Freeware/Q-Dir)；调研记录：[竞品调研](competitive-research.md#2026-10-27-官方版本基线复核)。

## 2026-10-27：交互 OLE 拖放探针退出时销毁顶层窗口

- 源码复核发现，显式交互 OLE Copy 探针与 Private Desktop 直接目标探针共用清理路径；交互探针原先只销毁 pane 子窗口，没有销毁可见顶层 `STATIC` HWND，因此退出后可能留下空白窗口。现仅交互模式销毁顶层窗口及其子窗格，Private Desktop 模式继续将顶层窗口交由 watchdog 清理。
- 新增隐藏普通 HWND smoke；修正前因缺少清理入口而按预期构建失败，修正后确认交互顶层已销毁、Private Desktop 顶层仍存在且前台未变化。NativeProbe Release build 0 警告/错误。没有启动真实 Shell view、交互探针或物理输入；实际 OLE 拖放仍未验证。

## 2026-10-27：OLE 拖放源等待启动按键

- `IDropSource::QueryContinueDrag` 现先确认指定的启动鼠标键曾按下，再于释放时返回 `DRAGDROP_S_DROP`；启动 OLE 循环时鼠标键尚未按下则继续等待，Escape 仍取消。原生 COM vtable smoke 在旧实现下按预期失败，修正后 Release NativeProbe build 与 `--shell-view-color-policy-smoke` 通过。
- 这是 IDropSource 状态机证据，不是实际 `DoDragDrop` 或鼠标拖放验收；未创建 Shell view、没有输入或改变前台窗口。交互探针与可接收输入隔离桌面仍待实现/授权验收。

## 2026-10-27：增加显式授权的 Shell OLE 拖放探针入口

- NativeProbe 新增 `--shell-view-ole-drop-copy --AllowInteractiveDesktopControl`：在新建的临时源/目标目录及两个真实 `IShellView` 中选择测试文件，通过 `RunOleLoop` 执行实际 OLE Copy 循环，再核对目标内容与源文件是否保留。没有授权参数或控制台输入被重定向时不会进入交互探针；Escape 取消，默认只允许 Copy。
- NativeProbe Release build 通过（0 警告、0 错误），本轮没有运行该命令或输入鼠标/键盘。命令使用当前交互桌面，必须只在用户明确授权且可隔离操作的测试会话中运行；如果鼠标拖到目标窗格外，Windows 的其它 Shell drop target 仍可能接收这个临时文本文件，因此真实产品拖放门槛仍未通过。

## 2026-10-27：接通隔离 OLE DoDragDrop 调用入口

- `ShellViewDragSource` 现提供隔离原型调用 `ole32!DoDragDrop` 的入口，可接收 COM `IDataObject`、本项目 `IDropSource` 与允许效果并交给系统 OLE 循环。调用要求在已初始化的 STA 中进行；入口不移动光标、不合成键鼠输入，也未从当前 Private Desktop 或用户桌面调用。
- NativeProbe Release build 通过（0 警告、0 错误）。这只证明 COM 参数编组与调用声明可编译，不证明 `DoDragDrop` 实际运行、源/目标效果正确、跨 pane 文件状态收敛或可见交互；真实循环仍需独占且获授权的隔离输入桌面。产品继续使用 ExplorerBrowser，未发布。

## 2026-10-27：补齐 Q-Dir 整行选择选项

- “视图 → 整行选择”使用 `IFolderView2::SetCurrentFolderFlags(FWF_FULLROWSELECT)` 控制 Shell 行选择；只改 `0x00200000` 位，保留其它 folder flags。单独的 `FullRowSelection.ini` 保存显式偏好；文件缺失或无效（已尝试备份）时跟随 Shell 默认，新建标签继承显式设置。
- `SettingsSmoke` 的 flag、便携设置与视图生命周期路由专项通过；SettingsSmoke/App Release 构建均通过（0 警告、0 错误）。完整 SettingsSmoke 后续在既有 Program Starter launcher 清理阶段因工作目录被子进程占用而中断；整行专项 smoke 已独立通过。没有启动 ExplorerBrowser/GUI，故详细信息/列表像素、高亮命中、右键、重启持久化仍待可见验收。设计：[整行选择](superpowers/specs/2026-10-27-qdir-full-row-selection-design.md)。

## 2026-10-27：文件操作后重新激活 Shell 视图候选

- 用户曾反馈复制/粘贴后新文件似乎已获焦点，但方向键不工作，切出再切回应用后恢复。源码对照发现，焦点恢复器在内容 HWND 和项目焦点都报告正常时会跳过 `FocusView()`；而 `FocusView()` 会调用 `IShellView.UIActivate(SVUIA_ACTIVATE_FOCUS)`，窗口重新激活路径也会执行它。这与“项目焦点正确但 Shell 键盘路由未激活”的现象相符，仍是源码假设，尚无用户可见复现证据。
- 每个文件操作恢复请求现会在窗口活动时尝试一次 Shell view activation，即使 HWND/项目焦点看似正常；请求过期、窗口失活或已进入原生命名编辑时不会激活，后续轮询也不重复。回归先按预期因新策略缺失而构建失败；完整 Release `SettingsSmoke` 和 App Release build 通过（0 警告、0 错误）。
- 没有运行 ExplorerBrowser、使用剪贴板或操作可见窗口，因此 Ctrl+C/V、Shift+Delete 确认框、Alt+Tab 和多窗格方向键结果仍待可见复验；文件操作焦点 P0 未关闭。对照说明见 [shell-parity](shell-parity.md)。

## 2026-10-27：延长隔离四窗格恢复探针的专用执行预算

- 追踪此前停点漂移后确认共同原因是 `PrivateDesktopShellProbe.Run()` 固定 25 秒等待：完整 session-restore 由四窗格创建、原生菜单命令、键盘/重命名路由和多轮文件操作组成，测试尚未完成便被内部 deadline 中止。不是某个 pane 固定的 Shell 调用死锁。
- 只将 session-restore 宿主/consumer 的私有桌面等待延长为 110/100 秒，并把 consumer 进程等待设为 100 秒；其他探针仍使用原 25 秒预算。Release NativeProbe build 通过（0 警告、0 错误）；完整 `session-restore-private-desktop` 在 120 秒外层 watchdog 下退出码 0，确认四 pane 的 Details/排序/分组/全选菜单路由、方向键与 Ctrl+A 路由、多 pane F2 编辑焦点隔离、复制/重命名/删除四窗格收敛、目录通知及 foreground 不变。过程日志：`src/.state/native-probe-watchdog/20261027-084330-8968e6b6.stdout.log`。
- 这是隔离 IShellView 原型的测试门槛通过，不是产品迁移或可见 GUI 验收；真实拖放与 WPF 接线仍未验证。watchdog 超时递归清理另见上一条路线图记录。

## 2026-10-27：修复 NativeProbe watchdog 超时后的子进程清理

- 超时 watchdog 过去只终止直接子进程；IShellView 会话恢复探针内部会再启动 consumer 子进程，因此超时可能留下后台 Shell 测试进程。现在通过 `Process.Kill($true)` 递归终止进程树，并扩展 watchdog 自测：构造带孙进程的超时进程，断言父进程和孙进程均退出。修复前回归失败并发现存活孙进程，修复后通过。
- 使用修复后的 watchdog 复跑四窗格 Private Desktop 会话恢复探针，仍在 pane 4 菜单命令阶段后于 60 秒超时；确认本轮没有遗留 NativeProbe/consumer 子进程。该修复解决探针进程泄漏，不解决也不解释 IShellView 恢复停滞。完整证据见[隔离原型记录](ishellview-color-spike.md#2026-10-27-watchdog-超时递归清理回归)。

## 2026-10-27：四窗格 IShellView 恢复探针出现阶段不定的超时

- NativeProbe 的完整四窗格 Private Desktop 恢复回归在 60 秒多轮及一次 120 秒样本中均出现超时，停滞阶段分散于菜单激活/命令、文件操作及消息泵/窗格收敛等待。最新 120 秒样本推进至删除调用前；另一轮删除主 view 已收敛而后续 Pane 2 等待未完成。前台窗口未变。
- 已为探针增加阶段/消息诊断，但没有共同根因结论，不改产品 Shell 行为、不视为迁移通过。下一步需要在真实停滞时采集原生线程栈；证据与日志编号见[隔离 IShellView 记录](ishellview-color-spike.md#2026-10-27-四窗格恢复探针停滞阶段追踪)。

## 2026-10-27：虚拟 Shell 标签和快捷链接使用位置图标

- `ShellIconProvider` 现对可解析的虚拟文件夹身份（例如“此电脑”和“回收站”）通过 PIDL 请求对应 Shell 小图标，并按位置身份缓存；常规文件夹和文件类型图标仍沿用原扩展名/目录缓存。
- 定向 Release smoke 在 Windows Shell 上确认普通目录、“此电脑”和“回收站”均能取得图标，且三者像素不同；完整 Release SettingsSmoke、App Release build（0 警告、0 错误）和 `git diff --check` 通过。没有启动 GUI，标签和快捷链接中的可见绘制仍待验收。

## 2026-10-27：目录树虚拟 Shell 文件夹图标索引

- 对“此电脑”和“回收站”的真实 Shell 解析名调用 `SHGetFileInfo` 文件路径重载会返回无效的通用图标索引；原目录树回调把 `IShellItem` 解析名当路径传入，导致虚拟节点的显式图标缺失。现改为从回调中的 `IShellItem` 取得绝对 PIDL，再以 `SHGFI_PIDL` 读取 Windows 系统图像列表的普通/展开索引。
- 新 smoke 直接调用生产 `NativeTreeEvents.OnGetDefaultIconIndex`，与 Windows 对同一 PIDL 的 `SHGetFileInfo` 结果比较：普通磁盘目录为 `3/3`，“此电脑”为 `15/15”，“回收站”为 `32/32`，均在 512 项系统图像列表范围内。定向 smoke 通过。
- 本轮没有启动 ExplorerBrowser 或可见 GUI；结果证明生产回调为这两类虚拟位置返回正确索引，不证明 WPF `HwndHost` 的实际绘制。普通树与虚拟树的可见验收仍需用当前预览包完成。

## 2026-10-27：递归计算所选文件和文件夹大小

- “工具”新增“计算所选项目递归大小”，仅处理当前文件系统视图的已选文件/文件夹；保留 F9 对当前目录的扫描语义，并可再次按 F9 取消任一进行中的递归扫描。父文件夹与其已选子项只计一次；不跟随重解析点，选择变化时取消扫描。结果继续使用既有状态栏摘要和大小格式设置，不自动扫描或写入配置。
- `RecursiveDirectoryScanner` smoke 覆盖所选文件与目录汇总、重复/嵌套选择去重、直接选中 junction，以及选中 junction 下的普通文件时也不沿祖先重解析点访问目标。路径祖先检查按已检查目录缓存，避免同目录多选重复查询。专项扫描器 smoke、完整 Release SettingsSmoke 和 App Release build（0 警告、0 错误）通过；没有运行可见工具菜单/大目录 GUI 验收。

## 2026-10-27：未选中时显示当前目录可取得大小

- 在“视图”增加“未选中时显示当前目录可取得大小”，默认关闭，配置独立保存在 `StatusBarFolderSize.ini`。开启后仅在没有选中项目时，通过当前 Shell view 的 `SVGIO_ALLVIEW` 汇总直接子项可快速取得的大小；有选区时仍优先显示选中项大小。不会递归统计子目录，也不把 Shell 未提供大小的条目当作零值。
- Release SettingsSmoke 覆盖默认关闭、独立 INI 往返及目录/选区显示优先级；App 和 NativeProbe Release build 均 0 警告、0 错误。隐藏 ExplorerBrowser `--navigation-cancel` 探针读取真实视图的 `SVGIO_ALLVIEW` 与 `SVGIO_SELECTION`：夹具直接子项总数 3，目录本身未计入，两个文件已知大小合计 8 字节；父窗口保持隐藏且前台窗口不变。未启动产品 GUI，因此菜单/状态栏可见效果待验收。属性查询结果缓存 5 秒，同数目条目变动可能在缓存有效期内暂时显示旧汇总；该项是 Shell 可取得值的提示，不保证与磁盘递归总大小相同。

## 2026-10-27：增加状态栏选中项目大小开关

- Q-Dir 官方英文功能清单列出状态栏的对象数量与选中对象大小显示选项。本程序原先固定显示计数并尝试读取可用选中大小；现于“视图”增加“状态栏显示选中项目大小”，默认开启，独立保存在 `StatusBarSelectionSize.ini`，关闭后停止读取选区大小属性，只保留总数和选中数。递归目录扫描仍只由 F9 启动。
- `SettingsSmoke` 覆盖默认值、便携 INI 往返和与状态栏显隐配置分离；完整 Release `SettingsSmoke` 通过，App Release build 通过（0 警告、0 错误），`git diff --check` 通过。本轮未启动 GUI，菜单与状态文本可见效果待验收。官方依据：[Q-Dir 英文功能清单](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)。

## 2026-10-27：复选框设置汇报 Shell 应用失败

- 源码复核发现，现有标签的 Shell 视图拒绝复选框设置时，宿主只触发内部菜单错误事件，没有把失败传回 Pane 的汇总器；全局 INI 已保存后，状态文本可能仍报告所有标签都已应用。现在宿主返回 HRESULT，Pane 将失败与对应路径汇总到状态文本；未创建的视图仍只保存偏好，待 `OnViewCreated` 时应用。
- 完整 Release `SettingsSmoke` 和 App Release build 通过（0 警告、0 错误），`git diff --check` 通过。没有创建 ExplorerBrowser 或 GUI；Shell 拒绝调用的故障注入与可见错误文本仍待隔离验收。

## 2026-10-27：补齐 Q-Dir 文件视图复选框选择

- Q-Dir FAQ 77 说明可在列表视图开启复选框选择，并支持 Ctrl+A 全选、Ctrl+Shift+A 反选。项目原有 Ctrl+Shift+A 反选命令已经由 Shell 选区命令实现；本批补上 View 菜单“使用复选框选择项目”，默认关闭，便携配置写入 `SelectionCheckBoxes.ini`，并应用于所有现有、隐藏及新建标签的 Shell view。
- 复用 `IFolderView2::SetCurrentFolderFlags` 的 `FWF_CHECKSELECT`（0x00040000）位，仅改这一位并读回验证；应用在每次 `OnViewCreated`，切换偏好时也立即应用到已有视图。独立纯状态 smoke 覆盖开关位与无关 flags 保留，便携偏好往返 smoke 通过；完整 Release SettingsSmoke 与 App Release build 通过（0 警告、0 错误）。尚未启动 ExplorerBrowser 或可见 GUI，因此真实勾选、混合项目、复选框点击对 Shell 选中/操作命令的影响待验收。
- 依据：[Q-Dir FAQ 77](https://softwareok.com/?faq=77&seite=faq-Q-DIR)、[FOLDERFLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags)、[IFolderView2::SetCurrentFolderFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags)。

## 2026-10-27：更正 IShellView 选中项拖放探针的文件名断言

- 复跑发现早期探针选中了 `view-target.txt`，却按 `selected-view.txt` 检查目标文件名，因此把成功复制误判为失败。已修正为用独立 `selectedSourcePidl` 选择 `selected-view.txt`，并同时校验目标文件的原名和精确内容；真实 `IShellView::GetItemObject(SVGIO_SELECTION)` 数据对象经目标文件夹 `IDropTarget` 直接调用后成功复制，源文件保留。早期“不落盘”的记录由此撤销。
- 拓展 `IDataObjectAsyncCapability` 阶段记录后，两类对象在 `DragEnter`、`DragOver`、`Drop` 前后均为 `async-mode=False`、`in-operation=False`；此状态没有解释此前误判。
- NativeProbe Release build 0 警告/错误，`drop-target-private-desktop` watchdog 退出码 0，前台 HWND 未变（日志：`src/.state/native-probe-watchdog/20261027-064309-d58b7ee7.stdout.log`）。此探针只直接调用目标回调，不等同于完整 OLE `DoDragDrop`；源端交接、修饰键/移动语义、可见跨窗格拖动及产品宿主仍待验收。记录见[IShellView 拖放调查](ishellview-color-spike.md#2026-10-27-对比-ishellview-选中项数据对象)。

## 2026-10-27：排队隔离 Shell 菜单的延迟命令

- 菜单命令由 frame subclass 延迟到自定义消息执行。源码审查发现 coordinator 原先只有一个待处理槽；多条命令在 STA 再次派发前到达时会覆盖 owner/ID。现在每个成功投递的自定义消息对应 FIFO 队列中的一个 pane owner 与 command ID，逐条取出，释放 coordinator 时清空队列；原有执行前 owner 校验保留。
- NativeProbe Release build 通过（0 警告、0 错误），`git diff --check` 通过；本轮未运行 smoke 或真实 Shell 菜单时序验证。完整 `DoDragDrop` 依赖当前光标和 input desktop，当前不向用户桌面注入输入；直接调用目标 `IDropTarget` 不能替代完整 OLE 循环。迁移授权及产品不迁移的边界不变，详见[原型记录](ishellview-color-spike.md#2026-10-27保留连续到达的延迟-shell-菜单命令)。

## 2026-10-27：保存当前工作区为桌面启动项

- 文件菜单新增“保存当前工作区到桌面…”。它把当前所有窗格、标签及视图状态导出为配置目录 `Workspaces` 下的 `.qdr`，并在用户选择的位置创建 Windows `.lnk`；快捷方式以应用自身为目标，参数指向该状态文件。通过 Shell Link COM 写入并临时文件原子替换，不改写用户桌面或已有 Q-Dir 配置，除非用户主动选择桌面位置。
- 启动 `.qdr` 时将其映射为可启动会话：保留已导入 pane/tab 状态，空 pane 使用通常的 home 标签。已有应用进程收到 `.qdr` 启动参数时，经同 profile named pipe 在新的独立窗口恢复，不覆盖正在运行的窗口会话；请求与目录打开路由分离。
- Release App 和 SettingsSmoke 构建通过（0 警告/错误）；完整 SettingsSmoke 通过，覆盖 Shell Link COM 读回含 Unicode/空格的参数、dotnet 调试宿主参数、Q-Dir pane 映射与空 pane 默认值、IPC 路由分流。没有启动应用或创建用户桌面快捷方式；真实双击启动、Shell 浏览器恢复和可见操作仍待 GUI 验收。

## 2026-10-27：将 drop target 生命周期延伸到 Shell 目录导航

- 源码审查发现 folder `BrowseObject` 创建替代 `IShellView` 的路径没有注册新目录 `IDropTarget`；旧 `StreamViewHost` 也只允许一份注册，无法在切换期间安全地让新旧 view 短暂并存。现按 HWND 保存多份 OLE 注册：成功创建新 view 后先注册新目标，再撤销旧 HWND 目标并退休旧 view；创建/注册失败时撤销新目标并保留原 view 注册。
- 源码审查随后逐一核对 `ShellViewColorSpike` 的所有 `CreateViewWindow` 调用点，确认首次 session restore、四窗格 sibling 创建/重建、folder `BrowseObject` 替换和独立 color spike 初始 view 均建立 drop-target 注册，并在切换/退休时撤销对应 HWND。
- 四个普通隐藏 HWND 的同 host 生命周期 smoke 通过，覆盖选择性撤销、Dispose 清理、重复 HWND 注册由 OLE 拒绝且不破坏原 owner、撤销后重新注册；NativeProbe Release build 0 警告/错误，完整 `--shell-view-color-policy-smoke` 通过。没有运行 Shell view；BrowseObject 的真实导航/拖放仍待隔离宿主验收。

## 2026-10-27：覆盖四窗格 OLE drop target 同 STA 生命周期

- 拖放注册 smoke 扩展到四个独立隐藏普通 HWND，在同一 STA 同时持有四个 OLE drop-target owner；重复注册首个 HWND 必须收到 `DRAGDROP_E_ALREADYREGISTERED`，失败 owner 平衡自己的 `OleInitialize`，原 owner 撤销后 HWND 可重新注册，其余窗格逐个正常撤销。Release NativeProbe build 0 警告/错误，定向 smoke 通过，前台 HWND 未变。
- 该证据验证了四窗格注册 owner 的 OLE 资源生命周期，不验证真实 Shell `IShellFolder` 目标注册或 `DoDragDrop` 文件操作；完整拖放仍是迁移门槛。

## 2026-10-27：隔离 IShellView 文件夹 drop target 注册生命周期

- 四窗格 session-restore 原型现从每个当前目录 `IShellFolder::CreateViewObject(IID_IDropTarget)` 取得目录级目标，并通过 `OleInitialize`、`RegisterDragDrop` 注册到对应 Shell view HWND；导航创建新 view 时也先注册新目标，退休/销毁旧 view 前定向撤销其 HWND 注册。Dispose 会撤销仍由 host 持有的所有目标，并平衡对应的 `OleUninitialize`。依据 Microsoft [`IShellFolder::CreateViewObject`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)、[`IDropTarget`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nn-oleidl-idroptarget)、[`OleInitialize`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleinitialize) 与 [`RegisterDragDrop`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop) 合同。
- 定向普通隐藏 HWND smoke 通过四个同 host 注册、选择性撤销、Dispose 清理、重复注册拒绝及前台不变；NativeProbe Release build 0 警告/错误，`--shell-view-color-policy-smoke` 通过。没有创建 Shell view、发起 `DoDragDrop` 或输入；真实 `BrowseObject` 导航目标是否能注册，以及实际拖放内容/通知仍待隔离宿主运行。

## 2026-10-27：隔离 IShellView 菜单 frame 分离候选

- 按用户授权继续隔离迁移原型。已将每 pane 的 `IShellBrowser.GetWindow` 与 OLE 菜单 frame HWND 分开，并给四个 native view 接入共享菜单协调器；探针新增真实激活后的窗口层级、HRESULT 和顶层菜单断言。
- 隐藏 HWND/OLE vtable smoke 验证两 pane 菜单交接、无效 HMENU 拒绝、旧 owner 延迟移除/释放保护、frame 菜单恢复；四窗格 Private Desktop watchdog 连续三轮验证真实 `UIActivate` 菜单合并和每 pane 的查看/排序/分组命令路由，以及会话恢复、文件操作和目录通知，主桌面 foreground 未变。新增真实 `SVUIA_DEACTIVATE` 检查也通过：最后一个 pane 退出时 Shell 回调卸载 OLE 菜单并恢复 frame 原菜单。NativeProbe Release 构建 0 警告/错误，定向 smoke 均通过。该证据不覆盖可见菜单点击、物理输入、WPF/产品宿主、第三方扩展菜单或 OLE 拖放。产品仍使用 ExplorerBrowser，未发布。
- 真实 OLE `DoDragDrop` 尚未运行。Private Desktop 中 `GetCursorPos` 失败；微软文档要求调用 `SetCursorPos` 时 input desktop 必须是当前桌面，因此不能在这个非活动 Private Desktop 中把目标窗口放到指针下，也不能为此切换到用户共享的 input desktop。继续其它无交互宿主契约与焦点验证；真实拖放留待独占且授权的可接收输入会话。[GetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos) · [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos)

## 2026-10-27：多窗格重命名期间阻止窗口级快捷键和 Shell 状态查询

- 源码复核发现窗口 `PreviewKeyDown` 与文件命令状态刷新都只询问 `ActivePane` 是否持有原生重命名 Edit。现在统一检查所有 pane 的 Shell 子窗口；任一重命名框保持原生键盘所有权时，应用级快捷键不执行，Shell 命令/选区状态也不查询。
- 新门控 smoke 在实现前按预期失败，修正后通过；完整 Release `SettingsSmoke`、App Release 构建（0 警告、0 错误）和 `git diff --check` 均通过。没有启动 ExplorerBrowser 或操作 GUI，因此原始多布局 F2/方向键症状仍待用户可见复验；这项源码防护不能单独证明实按键行为已修复。

## 2026-10-27：扩展 Ctrl+滚轮查看方式切换到窗格工具区

- 文件视图原有 Ctrl+滚轮处理限定于 Shell 子视图 HWND；现在窗格 WPF 表面（导航工具栏、地址区、标签栏等）也将 Ctrl+滚轮传给该窗格当前标签的同一查看方式循环，成功切换才消费事件，普通滚轮仍留给原控件。
- 新增源码接线 smoke，完整 Release `SettingsSmoke` 通过；App Release 构建通过（0 警告、0 错误），`git diff --check` 通过。没有操作 GUI；不同控件上的真实滚轮、四窗格目标、视图刷新后的焦点和普通滚轮行为仍待可见验收。

## 2026-10-27：补齐 Q-Dir Alt+F11 重启快捷键

- Q-Dir 官方快捷键清单将 Alt+F11 定义为重启。本项目现将其接入现有的确认、逐窗口会话保存和重启流程，并在“文件 → 重启应用”旁标注快捷键；普通 F11 切换全屏，不注册系统级热键。
- 快捷键分派/菜单接线 smoke、完整 Release `SettingsSmoke`、App Release 构建和 `git diff --check` 通过。没有触发实际重启；跨多窗口会话恢复仍需可见验收。来源：[Q-Dir 官方快捷键清单](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)。

## 2026-10-27：实现 Q-Dir F11 全屏切换

- Q-Dir 官方命令表将 F11 定义为全屏。按用户确认，Explorer Dirs 的 F11 或“视图 → 全屏”采用沉浸模式：去掉窗口边框、覆盖当前显示器，同时保留进入前的菜单、主工具栏和状态栏显隐状态；再次按 F11 或菜单项恢复进入前的普通/最大化状态和各栏显隐状态。全屏是临时窗口状态，不写入 profile 或会话文件。来源：[Q-Dir 官方命令表](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)。
- Release SettingsSmoke 与 App Release 构建覆盖源码及状态转换；未启动 GUI，因此实际显示器边界、多显示器/DPI、任务栏遮挡与 ExplorerBrowser 视图缩放仍待可见验收。

## 2026-10-27：补齐 Q-Dir 状态栏显示选项

- Q-Dir 官方 FAQ 36 列出文件视图状态栏显示/隐藏选项；Explorer Dirs 现于“视图”菜单提供同类开关，默认显示，保存到 profile `StatusBar.ini`。隐藏时各窗格的状态文本、项目数、磁盘空间和状态栏查看按钮一并隐藏，隐藏栏不再占用 Tab/Shift+Tab 或 F6 停靠点。
- SettingsSmoke 覆盖默认值、便携 INI 往返、外部编辑冲突与未来格式保护；焦点回归把状态栏显示/隐藏加入全部动态组合。实际菜单操作、空间收益和焦点转移仍待可见 GUI 验收。

## 2026-10-27：补齐隔离 OLE 拖放源端回调合同

- NativeProbe 新增标准 `IDropSource` 实现：Escape 取消、发起拖动的鼠标按钮释放时完成，其余状态继续；None/Copy/Move 光标反馈交给 OLE 默认指针。COM vtable smoke 覆盖左键源与右键源。
- 这只补齐 `DoDragDrop` 需要的源端对象合同，没有调用 `DoDragDrop`、没有注入输入或启动 Shell view，也未修改产品 ExplorerBrowser。真实源/目标交接和跨窗格拖动仍待独立可接收输入的桌面验证。

## 2026-10-27：拒绝隔离 OLE 宿主的无效父窗口

- `StreamViewHost.GetWindow` 对零值或已销毁的父 HWND 返回 `E_FAIL`，且输出句柄保持为零；有效父 HWND 仍正常返回。定向 COM vtable smoke 先按预期暴露旧行为，再通过修正验证，并分别覆盖零值与失效句柄；NativeProbe Release 构建 0 警告、0 错误。
- 本 smoke 未创建 `IShellView`，不代表真实 Shell 激活或产品迁移验收。依据：[IOleWindow::GetWindow](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-iolewindow-getwindow)。

## 2026-10-27：隔离 OLE frame 边框空间合同

- NativeProbe 的 `IOleInPlaceFrame` 现接受不占用工具栏边框的空/零空间请求；对非零工具栏空间请求返回标准 `INPLACE_E_NOTOOLSPACE`，对空的 `RequestBorderSpace` 参数返回 `E_INVALIDARG`。定向 `--shell-view-color-policy-smoke` 经真实 CCW vtable 通过。
- 这是隔离迁移原型的接口合同改进，不代表创建过 `IShellView`、Shell 工具栏布局已集成，或产品 ExplorerBrowser 已迁移。
- Property Schema 仍不能作为便携、进程内的自定义列验证路径：[`PSRegisterPropertySchema`](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psregisterpropertyschema) 说明其为机器级注册，涉及 `HKEY_LOCAL_MACHINE` 写权限，不支持按用户 profile 注册；不在日常系统登记测试 schema。后续逐项列验证需在可丢弃 Windows VM 内进行。

## 2026-10-27：保留隔离 view-state 流的 READWRITE 状态

- `StreamViewHost.GetViewStateStream` 之前把 `STGM_READWRITE` 当作纯写入，给请求者空白流，可能破坏该标签既有的 view state。现同时装入当前 session 快照并跟踪同一流的写入，Dispose 后持久化新状态；访问模式 `3` 被拒绝。
- `--shell-view-state-stream-smoke` 覆盖初值读取、更新写入、Dispose 后新 host 跨 session store 读回及非法访问值；通过后仍未创建真实 `IShellView`，真实 Shell 访问序列待验证。依据：[IShellBrowser::GetViewStateStream](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-getviewstatestream)。

## 2026-10-27：修正隔离 OLE 菜单组失败回滚

- 原生菜单组部分插入失败时，按位置正序移除导致索引左移，留下自有菜单项。现在改为逆序回滚，并释放每个已插入 popup。
- `--shell-view-color-policy-smoke` 的 HMENU 夹具覆盖 0–6 组插入前缀，按旧逻辑失败、逆序修正后通过；验证保留外部 popup、销毁全部自有 popup，并保留未插入 popup。构建和完整定向 smoke 通过。未创建 `IShellView`，真实 Shell 激活/可见菜单交互仍待独立桌面验收。

## 2026-10-26：用户授权推进隔离 IShellView 迁移原型

- 用户已明确授权在隔离原型中推进完整宿主迁移验证，验收面包括原生菜单、Shell 文件操作、拖放、目录通知、键盘焦点与每会话视图状态。该授权仅覆盖 NativeProbe/隔离宿主，不包含将产品 ExplorerBrowser 替换为 IShellView，也不包含发布迁移版本。
- 当前已有的证据分别覆盖隐藏 COM vtable/菜单生命周期 smoke、局部 Shell 文件操作与通知样本、独立 IStream 会话文件测试，以及五种标准视图和 Content 视图下的逐项颜色像素采样；这些拼接证据尚未在同一可见宿主中覆盖完整门槛。完整 OLE 拖放循环、物理键盘焦点和真实会话恢复仍未通过。
- 继续按 `docs/ishellview-color-spike.md` 推进原型；用户要求暂不使用 Computer Use，且项目规则禁止在无当前桌面授权时创建可能激活前台的 Shell 宿主，因此本阶段限于源码和无 Shell-view 验证。真实宿主门槛需在独立 Windows 会话完成后评估是否值得迁移产品。

## 2026-10-27：复核隔离 IShellBrowser / OLE frame 回调合同

- 当前 Release `--shell-view-color-policy-smoke` 退出码 0。复核覆盖隔离宿主的 `IShellBrowser` 导航历史与会话流、CDB2/CDB3 查询、菜单生命周期、控件窗口/消息路由、应用加速键回退、状态文本、活动 view、工具栏回调，以及 `IOleInPlaceFrame` 的 COM 路由和异常边界。
- 此 smoke 没有创建 `IShellView`，也不验证真实 Shell view 是否按预期调用这些入口；它只确认当前通用 host 代码及回调合同回归。迁移门槛仍是隔离真实 view 上的 OLE 拖放、可见菜单合并、物理焦点/输入与会话恢复验收；当前继续限于无 Shell-view 的源码工作。
- 后续在同一隔离 smoke 中先确认 `IOleInPlaceFrame.GetBorder` 的输出矩形未填充，再实现为父 HWND 的客户区矩形；客户区逐字段对照及空指针错误回归通过。`RequestBorderSpace` / `SetBorderSpace` 仍显式不支持；未创建 `IShellView`，因此不宣称 Shell 已消费该边界或工具栏布局已验收。细节见[隔离宿主记录](ishellview-color-spike.md#2026-10-27实现隔离宿主-getborder)。

## 2026-10-26：Q-Dir 导入预览列出未导入的颜色规则模式

- `.qdr` 导入预览此前只报告 `[Filter2]` 规则数量，无法让用户判断哪些颜色模式没有导入。现在列出最多六个规则模式并截断过长模式；颜色值和其它未确认字段仍不显示，也不应用规则。
- `QdrImportSmoke` 先验证模式预览并按预期失败，随后通过；扩展回归也确认长模式被截断、最多列六条且颜色值不泄露。完整 Release SettingsSmoke 通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。此改动改善导入预览，不代表 Filter2 字段映射、颜色导入或 `IShellView` 逐项颜色已实现。

## 2026-10-27：复核已安装 Q-Dir 工作区颜色样本

- 只读检查安装目录中的 `start.qdr` 与一个收藏 `.qdr`：两者均为 UTF-16LE 键值流，没有 `[Filter2]` 区段，也没有可确认的逐项颜色规则记录。本轮没有找到可以映射到导入器的正向颜色样本；`Q-Dir.ini` 的 `[Filter2]` 不能直接当作 `.qdr` 序列化格式。
- 导入器继续只预览已识别的规则模式与未导入提示，不应用未知颜色字段。要推进 `.qdr` 颜色兼容，需要一份在 Q-Dir 中启用颜色保存、并含背景或逐项颜色规则的收藏样本，再分别验证 Q-Dir 导出、本程序导入预览和取消/应用后的状态。

## 2026-10-26：为隔离 Shell 宿主接入菜单生命周期回调

- `InsertMenusSB`、`SetMenuSB`、`RemoveMenusSB` 现在可选委托给外层宿主；保留无菜单宿主时原有的 `S_OK` 空菜单行为，委托 HRESULT 原样返回，异常转换为 `E_FAIL`，释放时断开回调。
- 没有注入外层处理器且 parent 为有效 HWND 时，`SetMenuSB` 调用 Win32 `SetMenu` 并用 `OleSetMenuDescriptor` 安装 OLE 分派；`RemoveMenusSB` 卸载分派并在共享菜单仍挂在 parent 上时恢复前一菜单，不销毁调用方 HMENU/HOLEMENU。parent 无效时返回参数错误，parent 为零时保留空宿主 `S_OK` 行为。
- NativeProbe 的 `--shell-view-color-policy-smoke` 通过真实 COM vtable 与隐藏顶层 HWND 验证菜单安装/卸载、descriptor 调用方所有权、参数、HRESULT、异常和释放行为。没有创建 `IShellView` 或显示菜单，未连接 WPF 菜单；产品右键菜单路径未改。

## 2026-10-26：为隔离宿主增加 IOleInPlaceFrame 接口

- `StreamViewHost` 现同时暴露 `IShellBrowser` 与 `IOleInPlaceFrame`。后者的菜单、状态文字、模态状态与快捷键方法复用已验证的宿主回调；`SetActiveObject` 在父级接受后 AddRef 新对象并释放旧对象，Dispose 时释放保留的接口；边框空间协商显式返回 `E_NOTIMPL`。
- NativeProbe 的 `--shell-view-color-policy-smoke` 通过实际 COM vtable 检查接口槽位、父窗口、菜单/状态/快捷键路由、边框拒绝、HRESULT、异常和释放。隐藏 HWND 菜单 fixture 已验证 synthetic HMENU 的 SetMenu/OLE descriptor 安装卸载；仍没有 Shell `UIActivate` 实际回调或真实 Shell 菜单项，因此 Shell 菜单合并与消息派发仍未验收。

## 2026-10-26：为隔离 Shell 宿主接入框架状态回调

- `EnableModelessSB` 与 `SetToolbarItems` 现可选择转发到外层宿主，保留原始 HRESULT；无宿主回调时仍返回原来的 `S_OK`，异常转换为 `E_FAIL`，释放时断开委托。
- NativeProbe 的 `--shell-view-color-policy-smoke` 通过真实 COM vtable 检查模态开关值、工具栏按钮数据/计数/标志、HRESULT、异常与释放行为。没有创建窗口或可见工具栏，也没有接入产品控件；这只是隔离框架路由。

## 2026-10-26：转发隔离宿主的活动视图通知

- `OnViewWindowActive` 现可把活动 Shell view 接口指针和回调转给外层宿主；未配置处理器时维持 `S_OK`，委托 HRESULT 原样返回，异常转为 `E_FAIL`，释放时断开回调。
- NativeProbe 的 `--shell-view-color-policy-smoke` 通过 `IShellBrowser` vtable 槽 16 检查指针/HRESULT、异常与释放后默认行为。未创建 Shell view；不证明窗口焦点、激活顺序或产品窗格激活行为。

## 2026-10-26：补齐隔离宿主的 IOleWindow 帮助回调

- `ContextSensitiveHelp` 可选转发进入/退出帮助模式的状态给外层宿主；无回调时保留原来的 `E_NOTIMPL`，返回值透传，异常转成 `E_FAIL`，释放时断开处理器。
- NativeProbe 的 COM vtable smoke 覆盖 IOleWindow 槽 4 的默认 HRESULT、状态值、失败 HRESULT、异常和释放行为。没有调用真实帮助 UI 或创建 Shell view。

## 2026-10-26：补齐隔离宿主的 Shell 控制窗路由

- `StreamViewHost.GetControlWindow` 和 `SendControlMsg` 现可委托给外层宿主提供的控件窗口映射/消息处理器；未配置控件时分别返回 `E_FAIL` / `E_NOTIMPL`，异常留在 COM 边界内，释放宿主时解除委托。
- NativeProbe 的 `--shell-view-color-policy-smoke` 通过真实 COM vtable 覆盖未知控件、HWND/ID 映射、消息参数与返回值、失败 HRESULT、异常及释放行为。未创建 Shell 视图，产品没有接入 Shell 控件 HWND；真实 Explorer 工具栏/状态栏集成与菜单合并仍待隔离宿主验证。

## 2026-10-26：为隔离 Shell 宿主接入应用快捷键路由

- `StreamViewHost.TranslateAcceleratorSB` 现在可调用注入的宿主处理器：处理器返回已消费时返回 `S_OK`；返回未处理、未配置或抛异常时返回 `S_FALSE`，让 Shell 继续处理。释放宿主时断开处理器与状态回调，避免保留父级委托。
- NativeProbe 的 `--shell-view-color-policy-smoke` 通过真实 COM vtable 覆盖按键 ID/消息指针转发、消费、Shell 回退、异常边界和释放后断开。该回调只是隔离宿主接入点，没有接上 WPF 应用快捷键路由，也没有创建 `IShellView` 或运行完整消息循环；真实快捷键行为和焦点仍待隔离 GUI 验收。

## 2026-10-26：将 ExplorerBrowser Shell 提示接入每窗格状态栏

- 复用每标签浏览器已有的 `DWebBrowserEvents2` 连接点，将 `StatusTextChange` 经线程/订阅身份门控投递到当前窗格。每个窗格新增独立的 Shell 提示区域，不覆盖现有错误、搜索或 F9 扫描消息；切换标签时恢复该标签最近的提示，隐藏窗格时清空显示。
- SettingsSmoke 通过 native `IDispatch` BSTR 事件、托管写入/清空、取消订阅后的迟到事件检查；完整 Release SettingsSmoke 与 App Release build 通过（0 警告/错误）。没有启动 ExplorerBrowser 或 GUI，尚未证明 Windows Shell/provider 会在实际文件选择/悬停中发送该事件，也没有可见布局验收。

## 2026-10-26：接收隔离 Shell Browser 状态文本

- `StreamViewHost.SetStatusTextSB` 不再静默丢弃 Shell 文本：复制 LPCWSTR 到原型 host 状态，空指针清除，并通过 `StatusTextChanged` 通知订阅者。真实 `IShellBrowser::SetStatusTextSB` vtable smoke 覆盖写入/清除与事件通知，通过 `--shell-view-color-policy-smoke`。
- 此项未创建 `IShellView`，也没有 WPF 状态栏；真实 provider 的调用内容、时序和 UI 显示仍待隔离宿主验证。产品状态栏未改。

## 2026-10-26：补齐隔离颜色宿主的 CDB 服务查询路由

- 隔离 `StreamViewHost.QueryService` 在没有外部 `BrowserSite` 时，原先无法以 `SID_SShellBrowser` 返回 host 自己实现的 CDB2/CDB3；直接 `QueryInterface` smoke 没覆盖 Shell 常用服务入口。
- 增加回归先确认服务查询失败，再让独立原型通过请求 IID 查询自身；`--shell-view-color-policy-smoke` 全部通过。此项只验证托管 COM 服务路由，没有创建 `IShellView`，不证明 DefView 实际查询时序或产品迁移条件已满足。
- 下一步仍是补齐通用导航/宿主状态并在独立 Windows 会话验证 Shell 实际调用；产品继续使用 ExplorerBrowser。

## 2026-10-26：将隔离 IShellBrowser 导航路由并入通用宿主

- `StreamViewHost.BrowseObject` 现在可使用注入的 `ShellViewNavigationHistory` 处理真实托管 COM 入口；颜色宿主与筛选宿主共用同一基类路由，历史 PIDL 由基类销毁。无注入历史的基础 host 仍返回 `E_NOTIMPL`。
- `--shell-view-color-policy-smoke` 通过真实 `IShellBrowser::BrowseObject` vtable 验证成功/失败导航、失败保留当前位置与 PIDL 所有权。测试使用模拟 PIDL 和委托，没有创建 `IShellView`。
- 产品导航逻辑和 ExplorerBrowser 未改；实际 Shell provider 导航、view/HWND 交接、焦点/选区恢复仍待独立会话验证。

## 2026-10-26：对齐 Q-Dir 的标签栏显示选项

- Q-Dir FAQ 101 确认可设为始终显示标签栏，或至少有两个标签时显示。2026-10-26 的版本提供这两种模式；2026-10-29 再增加“隐藏标签栏（保留标签）”，三种模式默认保持既有始终显示行为。配置独立保存于便携 `TabStripVisibility.ini`，格式版本 2 兼容读取旧布尔格式，并保护未知更高版本不被覆盖。
- 标签数量变化时按各窗格独立更新标题栏可见性；标签 Shell 宿主与会话不因隐藏标题栏而销毁。Tab/Shift+Tab 与 F6 的焦点停靠图同步跳过隐藏标题栏。
- 定向可见性策略/INI smoke、焦点停靠组合回归、完整 Release SettingsSmoke 和 App Release 构建通过（0 警告/错误）。未操作 GUI；真实菜单/焦点及重启后的可见行为仍待桌面验收。
- 来源：[Q-Dir FAQ 101：标签可见性](https://www.softwareok.com/?faq=101&seite=faq-Q-DIR)。

## 2026-10-26：细化 Q-Dir 逐行文件夹大小目标

- Q-Dir 官方[文件夹大小列说明](https://www.softwareok.com/?seite=Freeware/Q-Dir/File_and_folder_size_Column_with_percentage)确认：文件夹大小复用 `Size` 列，文件/目录数量（含子目录项目）复用 `Type` 列；还提供单位/精度、百分比、失焦暂停及“忽略文件夹大小排序”选项。忽略排序是可选策略，不应写死为唯一行为。
- 明确本项目 F9 当前只是把当前目录递归总量写到状态栏，不能算逐行大小兼容。Q-Dir FAQ 82 已说明大小列下划线百分比相对于同一目录的文件/文件夹项目，而非磁盘容量比例；具体分母和配置默认值仍须通过 Q-Dir 可见行为确认。
- 更新了逐行大小的范围和接受条件。原生隔离 DefView 未读取 adapter 的项目属性，列/排序门槛仍未通过；产品 ExplorerBrowser 未接入。下一步需要安全隔离环境完成视图求值，不在日常桌面激活 Shell 宿主。

## 当前推进顺序与验收边界（2026-10-26）

| 顺序 | 工作项 | 当前状态 | 下一步 |
| --- | --- | --- | --- |
| 1 | ExplorerBrowser / `IShellView` 迁移原型 | 用户已授权隔离原型；产品仍使用 ExplorerBrowser。Private Desktop 四窗格原生容器已验证 Details/List/Icon/Tile 并存、独立视图流往返、同/异目录文件操作与通知、各 pane 的查看/排序/分组菜单命令原 ID 定向到活动 view HWND、共享 top-level OLE 菜单合并与停用恢复、文件项菜单目标隔离、方向键焦点路由、Ctrl+A 选区隔离，以及五种标准视图和 Content 模式下逐项颜色绘制，并覆盖四窗格中 F2 编辑期间的四方向键焦点隔离；两轮完整四窗格迁移 consumer 验证通过，前台保护检查通过 | 原型尚非 WPF/产品集成。剩余的真实 OLE `DoDragDrop` 源端交接、可见菜单点击、物理键盘焦点和可见多窗格体验仍待验收；`GetCursorPos`/`SetCursorPos` 要求当前 input desktop，Private Desktop 本身无法定位用户拖动指针。本阶段继续源码与独立 Private Desktop 验证，不进行用户桌面输入。产品 ExplorerBrowser 不迁移、不发布，直到完整迁移门槛与用户审阅通过 |
| 2 | P0 Shell 日常可靠性 | 文件操作与焦点恢复、空白处右键目标区分、真实拖放、Tab/Shift+Tab、标签历史及 watcher 自动刷新仍有可见验收缺口；隐藏探针和状态回归不能替代真实用户输入 | 在安全桌面以隔离测试目录按[本轮代码与下一轮验收](shell-parity.md#本轮代码与下一轮验收)逐项验收；当前无桌面授权，继续修复有源码依据且可无窗口验证的项目 |
| 3 | 内存与稳定性观察 | 闲置视图释放的实测收益约 2 MB，已移除；只读采样器现输出每个样本相对起点的工作集/Private Bytes 变化量及 Private Bytes 每分钟变化率（JSON schema 3）。10 月 27 日已有 temp.74 运行样本：三分钟内 Private Bytes 158.44→158.89 MiB，范围 153.06–162.65 MiB，不能证明长期无增长；详见 [只读采样记录](memory-sampling.md) | 继续复用 `scripts/perf-sample.ps1`，固定发布二进制、PID 生命周期、窗格/标签数量及目录操作；分别观察 Working Set、Private Bytes、线程、句柄和 CPU，只有持续增长证据后再定位托管堆或 Shell 原生分配 |
| 4 | 已接入组件的可见体验 | 目录树图标已在隐藏直接 NativeShellTree 中与系统图像模板逐像素匹配（193/193）；用户截图所示进程是 temp.58，早于后续图像列表绑定代码，WPF `HwndHost` 可见绘制仍未验收。主题/强调色、地址菜单、查看方式入口、预览/详情窗格、搜索/筛选、导入导出 `.qdr` 等仍有实际控件绘制、焦点、布局及跨重启体验待验收 | 合并到同一隔离预览版验收清单，记录精确版本、进程路径与哈希；优先在之后的预览包复核目录树图标，再按可复现差异修复 |
| 5 | 尚未确定实现边界的 Q-Dir 差异 | 逐项大小实际复用 Size 列，文件/目录数复用 Type 列；支持单位、精度、百分比、失焦暂停和可选排序策略。FAQ 82 将百分比说明为同目录项目间比例，但精确分母和默认设置待实测。隔离合成 DefView 曾显示自定义列 key 的逐项文本，并在标题点击时调用 `CompareIDs` 完成升/降序；变更通知/刷新后的派生列重取仍失败。预览格式过滤没有确认接口，`TreeNsplit` 含义未知 | 生产递归扫描器的多行初值/重扫值现传入隔离 provider 快照，并覆盖 PIDL 重枚举与 provider 重建；无窗口合同通过。产品行模型到原生 provider 的生命周期接线、Shell 变更后的列值重取、菜单/拖放/导航/键盘及完整迁移兼容仍未完成。此前 Private Desktop Shell view 的清理/桌面守卫问题未解决前不再创建新的视图探针；后续优先继续无窗口的数据/生命周期工作，不能据隔离 DefView 初始显示排序结果接入产品 ExplorerBrowser。其他未知字段继续等待真实行为/配置证据 |

### “已实现”与“已验收”的口径

- **已接入**：源码入口、状态处理或配置路径存在；构建通过只证明可编译。
- **无窗口验证**：纯状态、Shell 接口或隔离宿主的测试结果，仅覆盖文档列出的具体场景。
- **可见验收**：在指定发布二进制和隔离测试目录中，通过真实鼠标/键盘操作并确认屏幕结果、Shell 状态及重启行为。
- 任何一层都不自动代表下一层通过；本路线图及 `docs/shell-parity.md` 应继续逐项标明证据边界。

## 2026-10-26：IShellView 隔离迁移原型复跑

- 根因已确认：微软说明 `CreateDesktopW` 创建新 Desktop 时会把调用线程也分配到该 Desktop；原 `PrivateDesktopShellProbe.Run` 在主线程创建它，探针返回后主线程仍在非输入 Desktop，导致主线程 `GetForegroundWindow()` 变为 0。之前外层“前台变化”检查失败是线程桌面归属造成的误报，不能证明用户前台被切换。[CreateDesktopW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createdesktopw)、[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- Desktop 创建现已移至专用 bootstrap 线程；`--private-desktop-owner-smoke` 验证当前线程 Desktop/foreground 前后相同，且新建 Desktop 可关闭。NativeProbe Release build 0 警告、0 错误；owner smoke 通过。尚未复跑 Shell 宿主，因此迁移探针在修复后的隔离边界上仍待确认；完整 filesystem 序列的超时点也未定位。此前 ZIP-only 已完成视图创建和 teardown，约 4 秒，挂点不在该单场景。
- 修复后 ZIP-only watchdog 退出码 0：fixture、ZIP PIDL 解析、`BrowseObject`、一项视图枚举、Shell 菜单/拖放接口查询及 teardown 完成；外层 foreground HWND 前后同为 `0x550EC2`。完整 host-filter 运行到 ZIP 与 teardown 后退出码 1，外层 HWND 从 `0x10EA8` 变为 `0x550EC2`；后续只读 Win32 查询确认两者分别是 ChatGPT (`Chrome_WidgetWin_1`) 和 `dnplayer` / `EvertaleMockPreview` (`LDPlayerMainFrame`)。因此修正了旧桌面归属误报，但完整宿主仍出现实际前台差异；无法判断是探针中的 Shell 激活调用所致，还是期间外部窗口活动。立即停止所有 Shell 宿主/GUI 探针，不尝试恢复前台；需改用独立 Windows 会话/VM 或其他不共享当前交互桌面的隔离环境。

- 用户授权继续做隔离 `IShellView` 迁移验证；生产窗格仍使用 ExplorerBrowser。本轮 NativeProbe Release 构建通过（0 警告、0 错误）。
- `host-filter-private-desktop` 在独立 Private Desktop 上通过临时 filesystem 的筛选、Ctrl+A/Down/Enter 加速器调用、三层导航、上级及历史往返，并通过多个 Shell 位置的局部菜单/接口检查；但运行在 Search Folder 检查之后、ZIP 容器结果输出之前未返回，25 秒内部期限与 45 秒外部 watchdog 均触发。当前无法从日志区分 ZIP Shell provider 卡住还是其导航/清理调用阻塞，故该次全流程验收失败，不能将先前局部通过外推为完整宿主通过。
- `host-menu-commands-private-desktop` 已执行列头菜单映射、Details、刷新、升降序排序/分组和焦点恢复局部检查；这些阶段自身报告前台句柄未变。探针返回后 `PrivateDesktopShellProbe` 的最终全局前台比较却为 false，未采样到变化发生于销毁视图、释放 COM、清理 fixture 或 Private Desktop 退出的哪个边界。按安全约束停止后续 Shell 宿主运行，不发布、不改产品宿主；菜单/命令结论仅保留为局部隔离证据。
- 已为后续受控复跑增加 ZIP fixture/解析/导航各阶段标记，以及视图销毁、COM/PIDL 释放、父窗口处理、fixture 清理前后的前台句柄采样。NativeProbe Release 构建通过（0 警告、0 错误）；由于上次全局前台保护失败，本次只编译，未重跑任何 Shell 宿主。ZIP 的单目标入口现可通过既有 `host-filter-virtual-private-desktop -VirtualScope ZipContainer` watchdog 参数使用。
- 后续按单目标入口运行 ZIP：Shell 绝对 PIDL 解析、`BrowseObject`、视图创建、菜单/拖放接口查询和逐阶段 teardown 均在约 4 秒内完成；探针线程始终运行于非输入 Private Desktop，内部 `GetForegroundWindow` 读数为 0。外层仍因前后台比较返回失败，但旧外层只输出布尔值，因此无法判定是实际前台句柄变化还是观察边界问题。该单场景排除了 ZIP 导航本身必然超时；此前完整序列的超时点仍未定位。
- 已扩充 PrivateDesktopShellProbe 的超时及结束日志，输出外层实际前台 HWND 前后值。迁移原型的焦点/宿主验证暂时需要独立 Windows 会话；在此之前可继续推进与窗口激活无关的 Q-Dir parity 源码工作，但不再启动 NativeProbe Shell view。完整 OLE 拖放、物理键盘消息循环和可见焦点仍未验证。内存问题继续按本路线图既定的固定 PID/固定负载纵向采样计划跟进。

## 2026-10-26：地址栏导航箭头右键打开历史

- 对照 [Q-Dir FAQ 146](https://www.softwareok.com/?faq=146&seite=faq-Q-DIR)，在每个窗格后退/前进箭头上增加右键菜单入口，复用该标签现有的最近路径列表，点击历史项直接导航；菜单锚定在被右键的箭头下方。左键命令和路径输入框原生右键菜单保持原样。
- 定向 `AddressPathHistorySmoke` 先因缺少两个箭头的右键路由而失败，接线后通过；App Release build 0 警告、0 错误。没有运行 GUI，真实按钮右键输入、菜单定位、禁用箭头上的右键和标签隔离仍待可见验收。

## 2026-10-26：补齐 Q-Dir 地址输入规则

- 对照 [Q-Dir FAQ 146](https://www.softwareok.com/?faq=146&seite=faq-Q-DIR)，地址输入现展开环境变量、规范相对路径的 `/` 分隔符、以当前文件系统目录解析相对路径和 `..`；单独盘符 `C:` 导航到 `C:\`，空输入刷新当前视图并恢复地址栏显示。虚拟 Shell 位置不作为文件系统相对路径基准；`C:folder` 这种依赖每盘当前目录的路径不猜测，仍交由现有 Shell 校验。
- 地址导航 smoke 覆盖相对路径、环境变量、空输入、Shell 名称、协议输入和盘符相对写法；定向 smoke 通过，App Release build 0 警告、0 错误。尚未操作 GUI，真实路径输入、失败反馈和虚拟位置上的相对输入行为仍待桌面验收。

## 2026-10-26：内存优化 A/B 结果与持续观察计划

- 用户实测“一次性优化内存”首次提示释放 44 个视图，但物理内存变化约 2 MB，随后重启应用的回落更明显；据此已从源码移除视图释放命令，避免以低收益换取闲置视图重建等待。temp.73 改为提供显式重启应用并恢复当前进程管理的窗口会话。
- 用户观察的启动后样本约为 229 MB 工作集、156 MB 提交量；截图还含多个 ExplorerDirs 进程与不同内存值，当前不能据此判断单实例长期增长曲线或来源。
- 后续在固定进程、布局、标签数和目录工作负载下分时间点采样 Working Set、Private Bytes/提交量、线程、句柄；仓库已有 `scripts/perf-sample.ps1`，可按 PID 连续采样并校验进程启动身份，先复用它，不另加常驻监控。若 Private Bytes 持续增长，再分辨托管堆与 Shell/原生分配；不把多 PID 横向差异当成泄漏证据。
- 2026-10-26 对当时运行的同一 PID（temp.72，SHA-256 `CEF6F03337709A1506167A345439D036224E9F4C11A2A47FBE9B0A2685AA8673`）做 12 次、间隔 5 秒的只读采样：工作集 246–255 MiB、Private Bytes 160–170 MiB，线程数稳定 45，句柄 1243–1245；约 55 秒内 CPU 时间累计增加约 34 秒，说明不能当作静置基线。此短样本未观察到单调内存爬升，但不能排除更长时间或特定操作下的增长；需要在明确固定负载和更长观察窗口下复测。
- 同日稍后复查同一 PID 55668、同一启动时间和 SHA-256（temp.72），再采 6 次、间隔 5 秒：工作集 242–251 MiB、Private Bytes 158–166 MiB、句柄 1224–1226、线程 44–45；约 25 秒 CPU 时间累计增加 11.9 秒。该样本较早的同 PID 读数略低，但期间负载不受控且 CPU 活跃，不能据此判定回落或增长；仍未出现短样本单调增长，需继续固定负载纵向采样。
- 复用采样器时发现，在 worktree 执行会把主仓库 `releases/` 下的应用误标为 `other-copy`，JSON 也没有输出可执行文件路径。`perf-sample.ps1` 现从 Git common dir 定位主仓库，并输出 `ExecutablePath`（SchemaVersion 2）；同一 PID 前测为 `other-copy`/空路径，修正后为 `release`、路径指向 temp.72，SHA-256 不变。采样功能不注入输入、不切换窗口。

## 2026-10-26：继续 Q-Dir 逐行派生列评估

- 按已确认的架构评审范围，继续保持 ExplorerBrowser 产品视图不变。隔离 `IShellFolder2`/DefView 原生 shim 已通过静态列接口与数值比较器合同，但未激活 DefView 不调用逐项属性/排序回调；此前 `UIActivate(SVUIA_ACTIVATE_NOFOCUS)` 曾改变外层前台状态，因此不能在用户桌面重试。
- 新增 Private Desktop 原生 ListView 读取：标准名称/Size subitem 可读，新增派生列 subitem 仍为空，且没有触发 adapter 的逐项目 `GetDetailsOf`、`GetDetailsEx` 或 `CompareIDs`；前台保护通过，升序门槛仍失败。这说明只对未激活 DefView 发送 `LVM_GETITEMTEXTW` 不足以触发派生列，不支持“列表所有文本都未初始化”的简单解释；激活条件、property schema 和 Shell item 属性读取通路仍待区分。
- 适配器四个 `IPropertyStore` 请求入口（`QueryInterface`、`BindToObject`、`BindToStorage`、`GetUIObjectOf`）的计数均为 0；因此不实现基于这些入口的属性存储装饰器。原始 `IShellItem` 属性访问不经过该 shim，这次计数不覆盖该路径；schema 是否造成空列仍待验证，不在日常系统注册。
- 无窗口 `PSGetPropertyDescription` 读回原型自定义 `PROPERTYKEY` 为 `TYPE_E_ELEMENTNOTFOUND (0x8002802B)`，标准 `System.Size` 为 `S_OK`，确认原型自定义键缺少已加载的 property description。此结果与 DefView 空列相符但不证明因果；机器级 schema 不在日常系统注册。
- 同一未激活 DefView 增加已登记的 `System.DateCreated` 对照列后，ListView 可读取其时间文本，而紧邻的未登记派生列仍为空；此前名称和 Size 也正常。此结果强烈支持 schema 是新增列读取的门槛，但排序回调仍为 0，未登记键注册后的行为尚未验证。当前未找到 Windows Sandbox，DISM 状态查询需提升权限；不在日常系统试注册 schema。
- 复核 [PSRegisterPropertySchema 官方文档](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psregisterpropertyschema)：注册 schema 会影响整台机器，调用者需要对 `HKEY_LOCAL_MACHINE` 的写权限，且文档明确不支持按用户 profile 注册。因此不能用当前便携应用进程做无副作用验证；自定义逐项属性要继续验证，应先准备可丢弃的隔离 Windows VM，再测注册后 DefView 是否读取属性并执行排序。
- 逐行大小列仍是有价值但高难度的 Q-Dir 差距：NativeProbe 的合成文件夹现能为目录生成递归字节数并通过无视图合同；DefView 实际显示/排序、Shell 菜单/操作、拖放、通知与每标签状态尚未闭合。当前没有批准迁移 ExplorerBrowser，也不加入产品递归大小列或自绘列表。详见[逐行大小列调查](folder-size-column-investigation.md)。

## 2026-10-26：窗口暂时失焦时保留文件操作焦点恢复

- 源码确认 `MainWindow` 的 `MayRestoreFileFocus` 把窗口激活状态并入“用户仍希望焦点回到文件视图”的条件；`Alt+Tab` 后待处理的 1.5 秒恢复请求会因此立即结束，无法在用户切回后继续观察 Shell 是否又改了焦点。
- 现将“当前窗格仍是目标”与“本窗口此刻可接收焦点”分开：暂时失焦期间只保留原有有界请求，不查询 Shell 文件项焦点，也不调用 `SetFocus`；窗口重新激活且原窗格/标签/路径/用户输入代数仍有效时，原恢复轮询可以继续。切换窗格、标签/路径、用户把焦点移到其他控件或产生新输入仍会取消。
- 新增的门控回归先按预期因 API 不支持 `windowActive` 条件而构建失败；完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。未操作窗口、剪贴板或真实文件；Alt+Tab、真实 Ctrl+C/V/Shift+Delete 后的键盘焦点仍待隔离可见验收，文件操作焦点 P0 保持开放。

## 2026-10-26：复核左侧目录树缺图标反馈

- 本机进程检查发现当前运行实例来自 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.58`；目录树系统图像列表绑定与 Shell 图标索引回调是在后续 temp.69 源码中尝试的。截图标题只显示 Preview 0.6.0，无法仅凭截图确认具体包号，因此不能将截图认定为 temp.69 的可见结果。
- 当前源码的隔离 `--tree-host-core` 探针通过：生产树控件绑定同一系统图像列表，实际选中节点普通/选中图标索引均为 `3`（列表共 512 项）；隐藏父窗口销毁后前台窗口未改变。
- 这排除了当前探针环境中的“图像列表未绑定”或“节点图像索引缺失”，但不验证图标像素是否实际绘制，也不能确认用户截图对应的 temp 编号。缺图标仍是未解决的可见问题；下一步需在用户确认的预览版本上检查实际绘制，或增加隔离像素探针定位树控件绘制层。
- 对照微软 [Windows Classic Samples 的 NamespaceTreeControl 示例](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/Win7Samples/winui/shell/appplatform/namespacetreecontrol/NameSpaceTreeSample.cpp)：示例的 `OnGetDefaultIconIndex` 返回 `E_NOTIMPL`，初始化时也没有替换内部 `SysTreeView32` 图像列表，依赖 NamespaceTreeControl 自己的默认图标路径。当前生产树则同时覆写回调并绑定 `SHGetFileInfo` 返回的系统图像列表；索引有效和句柄相同仍不足以证明自定义路径正确绘制。下一步应在隔离树控件中只切换“默认回退”与“自定义回调/图像列表”，采集行区域实际像素并比较；像素证据出现前不再改生产图标实现。

## 2026-10-26：补充目录树节点图标诊断

- 用户截图显示左侧树节点前的图标栏空白。只读进程核对发现当前运行实例来自 `preview-0.6.0-temp.58`；截图本身没有版本标识，因此不能据此判定 temp.69 的图标绑定是否有效。temp.69 继承了图像索引回调和系统图像列表绑定，但可见绘制仍待在该版本上验收。
- 此前 smoke 只验证索引范围与列表句柄，不能算图标修复完成。
- NativeProbe 的原生目录树检查现会读取实际选中节点的 `TVIF_IMAGE` 与 `TVIF_SELECTEDIMAGE`，核对索引是否落在绑定的系统图像列表范围内，以区分节点索引缺失与图像绘制问题。
- 无窗口 `--tree-icon-callback-smoke` 通过：生产回调为真实临时目录返回 `normal=3/open=3`，系统图像表共 512 项。NativeProbe Release 构建通过（0 警告、0 错误），本轮没有启动树控件或 GUI；节点索引诊断仍待下次桌面授权验收，缺图标根因未确认。

## 2026-10-26：隔离 NamespaceTreeControl 图标绘制 A/B

- 新增 `--tree-icon-render-ab`，在不创建 ExplorerBrowser、不开可见窗口、不发送输入的情况下，以同一个生产 `NativeShellTree` wrapper 比较两种图标路径：NamespaceTreeControl 默认回退（`OnGetDefaultIconIndex=E_NOTIMPL`、不替换系统图像列表）与当前显式系统索引/图像列表。
- 两种路径的根节点 `TVIF_IMAGE/TVIF_SELECTEDIMAGE` 均为 `3/3`；对隐藏控件首行客户区调用 `WM_PRINTCLIENT` 后，80×32 ROI 两边均有 455 个非背景像素，16 像素横带计数完全一致（26/160/78/97/94），SHA-256 前缀均为 `9C0FAB159F83A144`；前台 HWND 未变化。后续将统计范围细化为候选图标带 x=32–55：两边均有 122 个非背景像素，像素哈希前缀均为 `41689DFC10609924`，高频颜色也一致；这进一步说明两条隐藏绘制路径一致，但仍不能单凭颜色直方图把该区域像素认定为文件夹图标。
- 隐藏的直接树宿主不等于 WPF `HwndHost` 产品布局。探针第一次依赖隐藏状态下不可读的 `TVM_GETITEMRECT` 行范围而失败，随后改用固定客户区首行 ROI。本轮 NativeProbe Release build 0 警告、0 错误；前台 HWND 未变化。此前 A/B 批次的 App Release build、完整 Release `SettingsSmoke` 和 diff 检查结果见前一提交。
- 后续只读检查发现当前运行进程是 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.72\app\ExplorerDirs.App.exe`；temp.72 晚于 temp.69 图标路径变更，但没有读取或激活它的窗口，所以仍不构成用户可见绘制证据。当前不能宣布图标缺失已修复，也没有移除产品回退或图像列表绑定；下一步应在确认过的较新预览包中检查 WPF 宿主实际绘制，再按实际结果定位裁剪/尺寸/像素来源。
- 修正 `--tree-icon-render-ab` 模板搜索的候选起点：先前只从 x=24 开始，漏掉缩进树中实际位于 x=19、y=2 的图标，因而错误报告 `1/193`。现在在首行前导区搜索后，默认回退和显式系统图像列表均与同一系统文件夹图标模板匹配 `193/193`；采样图也确认隐藏 `WM_PRINTCLIENT` 画出了节点图标。NativeProbe Release build 0 警告/错误，A/B probe 退出码 0，前台 HWND 未变化。
- 该证据说明隐藏的直接 `NativeShellTree` 中默认和显式图标路径都能正确绘制且结果相同；它没有覆盖 WPF `HwndHost` 的布局、裁剪或可见尺寸，也不能直接证明用户所见缺图标已经修复。未修改生产树图标代码。继续用确认过的新预览做可见验收前，不声称图标缺失已解决。

## 2026-10-26：静态 Shell 大小列适配器合同复验

- 按原生 Shell 视图架构评审的下一步，运行无窗口 `--static-shell-column-folder-smoke`。适配器为 `PKEY_Size` 返回 `UInt64` 与显示文本，并在 `CompareIDs` 中正确处理 2、10、100 和相等值（退出码 0）；输出确认 `window-created=False`。
- 这只验证 `IShellFolder2` 文件夹适配器的列/排序合同，测试值是固定夹具数据；不代表真实递归文件夹大小计算、DefView 显示、拖放或菜单兼容已实现。Private Desktop DefView 探针与产品接入仍未执行。

## 2026-10-26：发布 temp.69 临时预览

- 将 `0f9af33` 打包为 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.69`，采用独立 profile；其源文件来自 temp.68 隔离 profile，复制后逐文件哈希一致。
- Release SettingsSmoke、App Release 构建、文件视图导出故障注入 smoke 与发布隔离 smoke 通过；包内 EXE/DLL 哈希与 `SHA256.txt` 一致，版本为 `0.6.0-preview.temp.69+0f9af33c06fc43397bc01188069821a2f0bdef2e`。
- 未启动发布程序。目录树实际绘制、Tab 焦点、禁用态图标和导出对话框仍待可见验收，详见[temp.69 说明](releases/preview-0.6.0-temp.69.md)。

## 2026-10-26：文件视图导出采用完整文件替换

- 文件视图导出之前直接以 `FileMode.Create` 打开最终目标；若 CSV/TXT/HTML/XLS 写入中途失败，旧导出会先被截断，目标目录还会留下不完整文件。
- 现先在目标同目录生成唯一临时文件，成功写入并刷盘后才替换目标；写入失败会清理临时文件并保留原目标。目录同卷替换也避免跨卷复制产生半成品。
- 定向 smoke 先因导出命令未走原子替换而失败；随后故障注入验证旧文件保留、临时文件清理，以及成功时完整替换。实际磁盘故障、被其它进程占用及用户可见对话框仍待桌面验收。

## 2026-10-26：纳入窗格查看方式按钮的 Tab 顺序

- 每个窗格导航栏中的“查看方式”按钮本身可键盘聚焦，但之前没有分配到自定义 Tab 停靠点；从它按 Tab/Shift+Tab 会把当前焦点判为未知，造成焦点跳回循环边界而不是相邻按钮。
- 焦点映射现在在目录树切换按钮之后、地址控件之前加入窗格查看方式按钮；正反 Tab 继续在当前窗格内逐项循环。F6 的主要区域序列不变。
- 新增前先运行 `--focus-traversal-smoke`，它按预期因导航停靠点数少一项失败；修复后 256 种可选控件组合、正反向逆变换和完整停靠点循环通过。实际物理键盘顺序及多窗格可见行为仍待桌面验收。

## 2026-10-26：矢量图标跟随控件禁用状态

- 矢量 `Path` 图标之前固定使用普通控件文字色，禁用状态可能与父控件呈现不一致。现增加最近父级 WPF `Control.IsEnabled` 门控，禁用时图标描边改用动态 muted 文字色；不改变控件模板、命令启用条件或 Shell 文件图标。
- 图标 smoke 先因缺少禁用态资源规则而失败，修复后通过；App Release 构建 0 警告、0 错误，`git diff --check` 通过。未启动 GUI，实际禁用态对比度及高对比度显示仍待可见验收。

## 2026-10-26：目录树系统图标列表绑定修复

- 用户提供的 Preview 0.6.0 截图仍显示目录树图标区域空白。生产代码已返回 `SHGetFileInfo(SHGFI_SYSICONINDEX)` 的系统图像索引，但没有把该系统图像列表绑定到 `SysTreeView32`；原生树控件已有自己的默认图像列表，二者索引空间不能假定相同。
- `NativeShellTree` 初始化时现在把 `SHGetFileInfo` 返回的共享系统图像列表句柄绑定到原生树控件。Smoke 直接验证真实临时文件夹解析出的普通/展开索引位于系统图像表范围内（本机索引 `3/3`，共 512 项）；NativeProbe 另有隐藏树断言，要求控件读回的图像列表句柄与系统图像列表一致。
- 本批未操作可见 GUI。构建和 smoke 只能证明绑定逻辑接线及索引有效，不能证明当前发布包或虚拟 Shell 节点已在屏幕正确绘制；需下次预览验收。
- 后续将 NativeProbe 的句柄断言延伸到根目录填充及子目录展开后；本次隐藏探针运行中两个阶段仍读回同一系统图像列表，但完整探针最后在前台窗口边界检查失败。它不证明图标已在屏幕正确绘制；不再运行此会改变隐藏焦点的探针，等待独立的可见验收。
- 新增无窗口回调检查：真实临时目录 `IShellItem` 经生产 `OnGetDefaultIconIndex` 返回成功，普通/展开图标索引均为 `3`，位于 512 项系统图像列表内。回调及图像列表句柄链路已有直接证据，缺失图标的可见绘制原因仍未定位。
- 用户截图提交后做只读进程核对，发现当时运行实例的映像路径是 `releases/preview-0.6.0-temp.58/app/ExplorerDirs.App.exe`；导航树图标绑定修复在后续 temp.68。若截图来自该实例，缺图标与旧包版本一致；不关闭用户进程，temp.68 的可见效果仍待用户验收。

主题与应用图标的目标、实施顺序、Q-Dir 兼容边界及验收门槛见[主题与图标路线图](theme-icon-roadmap.md)。该工作排在核心 Shell 功能和组件交互之后；当前系统强调色接线只是基础，不表示主题或文件区着色已完成。

## 2026-10-26：隔离 Shell 静态数值列原型

- NativeProbe 用四个固定 `UInt64` 样本 `2/10/100/10` 验证隔离 `IShellFolder2` shim。无窗口合同覆盖自定义 PROPERTYKEY 列，也覆盖系统标准 `PKEY_Size` 的 `GetDetailsOf` 文本、`GetDetailsEx` 类型/值和 `CompareIDs` 数值顺序；NativeProbe Release build 0 警告、0 错误。
- Private Desktop 的未激活 DefView 检查确认视图持有 shim、列状态可读回，但实际项目顺序不变，且未调用适配器的逐项目列回调；因此未激活视图的排序/显示门槛失败。此结果不代表活动 DefView。不要接入产品 ExplorerBrowser，也不做递归计算。下一步查清列求值所需的激活条件及安全隔离方式；不得在用户桌面调用 `IShellView.UIActivate`。
- 调查补充：实验列使用未登记的临时 PROPERTYKEY；微软 Explorer Data Provider 示例随自定义属性提供 `.propdesc`，其中声明属性类型和标签。`PSRegisterPropertySchema` 文档说明 schema 是机器级、安装期注册。**待验证假设：**DefView 未读取实验属性可能与缺少 property description schema 有关。现有无窗口标准 `PKEY_Size` 合同已通过；未激活 DefView 中的标准列消费仍为 0，schema 是否影响活动视图尚无证据。不得在日常系统注册测试 schema。
- 原生 shim 现对标准 Size 列逐项目文本回调 `GetDetailsOf` 单独计数；此前仅观测 `GetDetailsEx` 与 `CompareIDs`。新计数已在 Private Desktop 未激活视图探针运行，Size 项目计数为 0；由于视图未激活，不能据此排除活动 DefView 使用该回调。
- 接口契约复核显示 `GetDetailsOf`/`GetDetailsEx` 是 Explorer 详情列路径，`IShellItem2::GetPropertyStore` 是另一入口；没有证据时不把 `GetUIObjectOf` 改造成属性存储代理。三类列回调现已观测到未激活视图均未消费，下一步是查找不会触及用户前台的活动视图实验；不接入产品。
- 隔离的未激活 DefView 探针现已实际运行：adapter 身份、排序键/方向读回通过，但 Size 项目 `GetDetailsOf`/`GetDetailsEx` 与 `CompareIDs` 均为 0，真实项目顺序没有变为请求的升序，排序门槛失败。前台窗口保持不变。因为未调用 `UIActivate`，该结果只说明未激活视图的消费行为；之前激活调用曾引起外层前台变化，不在用户桌面复试。

## 2026-10-26：修复原生目录树默认图标回退

- 目录树事件回调未提供自定义索引时，改为返回 `E_NOTIMPL`，让 `NamespaceTreeControl` 解析 Shell 默认图标；生产实现和独立 NativeProbe 保持同一回退语义，回归检查纳入完整 Release `SettingsSmoke`。微软官方 NamespaceTreeControl 示例采用相同的未实现回调语义。
- 定向与完整 Release `SettingsSmoke`、App Release 构建和发布隔离 smoke 通过；已准备 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.66` 独立 profile 预览包，EXE/DLL 哈希与清单一致。未启动 GUI，目录树的实际图标显示及虚拟 Shell 节点仍待可见验收。

## 2026-10-26：目录树图标截图复核

- 用户提供的 `Preview 0.6.0` 截图中，左侧目录树名称前仍有图标栏空白。窗口版本与 temp.66 相符，且该包 EXE SHA-256 `EACFCAD4...A70CDE` 与发布清单匹配；检查其构建提交 `aaa7b6a` 确认回调返回 `E_NOTIMPL`，因此截图复现的是 temp.66 的实际缺陷。
- 复核发现当前系统的 `NamespaceTreeControl` 并未按预期从 `OnGetDefaultIconIndex = E_NOTIMPL` 回退显示图标；该注释及仅检查源码字符串的 smoke 不能证明图标显示。源码现改为对每个 `IShellItem` 读取绝对 Shell 解析名，并用 `SHGetFileInfo(SHGFI_SYSICONINDEX | SHGFI_SMALLICON)` 显式返回 Windows 系统图像列表中的关闭/展开索引，解析失败时才回退 `E_NOTIMPL`。
- 完整 Release `SettingsSmoke` 与 App Release 构建通过；源码实现已在当前分支，但尚未发布在用户截图对应的 temp.66 中。新预览用于验证这版修复；仍需可见验收普通目录和虚拟 Shell 节点图标，命令行 smoke 与打包成功不能代替实际绘制确认。
- 后续将索引解析提取到生产与 smoke 共用的 `ShellTreeIconIndexResolver`；Release `SettingsSmoke` 现在会直接调用 `SHGetFileInfo` 检查真实临时文件夹的普通/展开索引均有效（本机返回 `3/3`），而不是只搜索源码。完整 SettingsSmoke 与 App Release build（0 警告、0 错误）通过；这只证明索引解析，不证明 `NamespaceTreeControl` 绘制，temp.67 仍待可见验收。

## 2026-10-26：目录树跟随文件列表选区

- 对照 Q-Dir [FAQ 66](https://www.softwareok.com/?faq=66&seite=faq-Q-DIR) 的“从列表选择并展开”选项，为本项目增加默认开启、可单独关闭的“视图 → 目录树 → 从文件列表选择并展开”。当前活动标签在单选到经 Shell 确认的文件夹后，使用其绝对 Shell 解析名定位并展开对应逐窗格树；共享树只跟随当前活动窗格。文件视图目录和键盘焦点不变，关闭树/多选/选择普通文件/非活动窗格都不触发；独立便携设置为 `NavigationTreeSelection.ini`。
- 新增 pure-state smoke 覆盖启用/关闭、当前可见窗格、单选文件夹门控以及设置默认/往返。Smoke 先因缺少策略和默认值行为失败，接线后通过；App Release build 通过，0 警告、0 错误。尚未操作 GUI；真实 Shell 选区回调、可见树展开、共享树、多窗格和设置重启恢复仍待验收。

## 2026-10-26：Q-Dir Filter2 规则导入预览

- `.qdr` 文本导入现在识别 `[Filter2]` 区段内的数字索引规则，并在预览中报告未导入的规则数量；不显示原始值，也不猜测颜色/字体列含义。定向回归先按预期失败，后在完整 Release `SettingsSmoke` 中通过；App Release build 0 警告、0 错误。该项不表示颜色已导入或应用，正向 `.qdr` 颜色样本与字段映射仍待确认。

## 2026-10-26：支持自定义应用强调色

- “选项 → 主题 → 强调色”可继续跟随 Windows 强调色，或输入自定义 RGB 色值；浅/深语义色板、选中态、活动窗格和图标按同一颜色派生。Windows 明暗模式仍独立跟随，故自定义色不会阻断系统模式更新。
- `Theme.ini` 升到 v2 并继续读取 v1；错误颜色备份、外部编辑冲突和未来版本保护通过现有配置 smoke。完整 Release `SettingsSmoke`、App Release build（0 警告、0 错误）通过。未启动 GUI，窗口内对比与多窗格即时刷新仍待桌面验收。计划见[自定义强调色计划](superpowers/plans/2026-10-27-custom-accent-color.md)。

## 2026-10-27：WPF 外壳接入语义主题画刷

- 主题控制器现在同时发布窗口、控件、边框、次级文字、强调色和选中态画刷；主窗口、各窗格、布局切换图标/分隔条和照片打印辅助窗改为消费这些动态语义角色。高对比度时移除自定义 `SystemColors` 覆盖，语义角色回落到 Windows 画刷。
- 新增定向语义资源与 XAML 接线 smoke；验证“未接线”时首个语义角色断言按预期失败，接线后定向主题和窗格标记检查通过。完整 Release `SettingsSmoke` 与 App Release 构建通过（0 警告、0 错误）。未运行 GUI；实际系统切色、多窗口刷新和高对比度可见效果仍待验收。实现计划见[语义主题资源计划](superpowers/plans/2026-10-27-theme-semantic-resources.md)。

## 2026-10-27：八种 Shell 查看方式改用本地矢量图标

- 新增 16 单位几何图标集，按 Explorer Ctrl+Shift+1～8 的查看方式顺序绘制。每窗格上方和状态栏的查看按钮共用紧凑矢量查看图标；两个查看菜单为每种查看方式显示对应几何图标，线条使用动态语义文字色。现有布局图形也继续使用语义主题画刷。
- 图标 Smoke 验证八个几何均可解析、边界符合图标网格且不重复，并确认菜单继续传递原 `ViewStyle`、按钮保留无障碍名称。定向图标/主题 smoke、完整 Release `SettingsSmoke`、App Release 构建通过（0 警告、0 错误）。没有操作 GUI；实际尺寸、DPI、弹出菜单观感和高对比度效果仍待验收。实现计划见[查看方式矢量图标计划](superpowers/plans/2026-10-27-view-style-icons.md)。

## 2026-10-27：导航与标签工具栏接入共享矢量图标

- 新增共享主题色图标工厂，现为每窗格后退/前进/上级/刷新/目录树/路径位置/历史按钮，以及主工具栏新建标签/窗口、复制/关闭标签、刷新和搜索提供矢量图标。被替换的字符原来承担可访问名称时补上明确 UI Automation 名称；点击事件、快捷键、提示、焦点停靠与 Shell 调用保持原样。查看方式菜单和按钮也改用共享工厂。
- 新增图标几何、语义笔刷和命令绑定 smoke；完整 Release `SettingsSmoke`、App Release 构建通过（0 警告、0 错误）。未启动 GUI；大小、DPI、悬停/禁用状态和高对比度外观仍待桌面验收。实现计划见[导航与标签矢量图标计划](superpowers/plans/2026-10-27-navigation-tab-icons.md)。

## 2026-10-27：完成应用命令工具栏矢量图标接线

- 打印、递归目录大小、环境刷新和终端按钮改用主题语义色矢量图标；剪切、复制、粘贴、删除、重命名、属性、撤销、重做和新建文件夹的动态菜单/文字工具栏显示配对图标。保留原命令标识、标签、快捷键、点击处理、提示及 Shell 状态门控。
- 定向图标 smoke 覆盖 25 个几何、九个 Shell 文件命令映射、菜单/工具栏入口及原处理器；完整 Release `SettingsSmoke`、App Release 构建（0 警告、0 错误）和 `git diff --check` 通过。未启动 GUI，图标尺寸、DPI、禁用/悬停和高对比度外观仍待可见验收。计划见[命令工具栏矢量图标计划](superpowers/plans/2026-10-27-command-toolbar-icons.md)。

## 2026-10-27：应用菜单与工具栏表面跟随语义主题

- 为 WPF `Menu`、`MenuItem`、`ContextMenu`、`ToolBar` 和 `ToolBarTray` 接入现有动态表面/文字/边框资源，不替换系统控件模板，不改变菜单命令及键盘行为；ExplorerBrowser 和 Shell 原生菜单保持原生绘制。
- 增加五类控件的动态主题接线 smoke；定向主题 smoke、完整 Release `SettingsSmoke`、App Release 构建（0 警告、0 错误）和 `git diff --check` 通过。未操作 GUI，浅/深色实际弹出菜单、高对比度及 hover/selection 视觉仍待可见验收。计划见[菜单与工具栏主题表面计划](superpowers/plans/2026-10-27-themed-menu-toolbar-surfaces.md)。

## 2026-10-27：文本输入与下拉控件接入动态主题

- 文本框和下拉框表面、文字、边框消费语义主题；文本插入光标及选中文字消费动态主题/强调色。未替换模板，不改变输入事件、编辑模式、焦点或选择处理。
- 主题接线 smoke 先按预期因缺少 TextBox 绑定失败，接入后 TextBox、ComboBox 和 ComboBoxItem 检查通过；完整 Release `SettingsSmoke` 与 App Release build（0 警告、0 错误）通过。没有操作 GUI，编辑态、下拉态、禁用态和高对比度颜色仍待验收。计划见[文本输入控件主题计划](superpowers/plans/2026-10-27-themed-text-entry-controls.md)。

## 2026-10-27：按钮与选择控件接入动态主题

- WPF 按钮、切换按钮、复选框和单选框使用动态语义色，不重做系统控件模板，保留现有命令、焦点和状态行为。
- 定向资源 smoke 先按预期因 Button 主题样式缺失失败，接入四类样式后通过。App Release 构建 0 警告、0 错误；完整 Release `SettingsSmoke` 与 `git diff --check` 待本批最终验证。未操作 GUI，鼠标 hover/pressed、禁用/勾选及高对比度外观待可见验收。计划见[按钮控件主题计划](superpowers/plans/2026-10-27-themed-button-controls.md)。

## 2026-10-27：应用自有 WPF 列表使用主题选中色

- 将辅助窗口的 `ListBox` 表面和行绑定到语义颜色，选中行使用当前强调色与成对的可读文字色；不改变列表模板、选区逻辑或 ExplorerBrowser 文件列表。后续对比度修正让 ListBox 选中行与 TextBox 选中文字改用满足对比度门槛的选中色板。
- 定向接线 smoke 先因 ListBox 样式缺失失败，加入资源后通过；完整 Release `SettingsSmoke`、App Release build（0 警告、0 错误）和 `git diff --check` 均通过。未操作 GUI，列表可见效果待验收。计划见[WPF 列表主题计划](superpowers/plans/2026-10-27-themed-listbox.md)。

## 2026-10-27：Windows 11 标题栏同步应用主题

- 主窗口和照片打印辅助窗将应用调色板同步到 DWM 标题栏、标题文字和边框；主题变化即时重新应用，高对比度恢复系统默认。调用公开的 Windows 11 22000+ 颜色属性；旧系统拒绝属性时不影响窗口创建和 WPF 客户区主题。
- 纯 COLORREF/高对比度策略和两个窗口接线 smoke 通过；完整 Release `SettingsSmoke`、App Release build（0 警告、0 错误）和差异检查通过。未启动 GUI，Windows 11/旧系统兼容、激活态标题栏颜色和实时切换效果仍待可见验收。计划见[窗口标题栏主题计划](superpowers/plans/2026-10-27-themed-window-frame.md)。

## 2026-10-26：temp.62 隔离主题预览

- 将照片打印窗口系统色适配和当前标签强调色下划线打包到 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.62`，使用独立 profile。发布隔离 smoke、全量 Release `SettingsSmoke` 和 App Release 构建均通过；EXE/DLL 哈希与包内清单一致。
- 未启动 GUI；此包不代表多窗格主题、深色/高对比度视觉、Shell 文件区着色或 Q-Dir 颜色互操作已验收。变更细节见[第六十二版说明](releases/preview-0.6.0-temp.62.md)。

## 2026-10-26：temp.63 使用主项目 profile 的隔离预览

- 从隔离 worktree 发布时，原脚本只查找 worktree 自己的 `profile/`；该目录缺失会导致新包的独立 profile 为空。`publish.ps1` 现提供 `-ProfileRoot`，默认行为仍从当前仓库 profile 复制；明确传入时校验目录存在，并从指定来源只读复制允许的 INI、登记的窗口会话/固定标签侧车及顶层工作区/快速链接。
- `publish-smoke.ps1` 覆盖外部 profile 来源、字节一致、来源不变及显式缺失路径拒绝。temp.63 在本项目中使用 `D:\projects\explorer-dirs\profile` 生成独立 `app/profile/`，包含照片打印窗口系统色和当前标签强调色。发布细节见[第六十三版说明](releases/preview-0.6.0-temp.63.md)。
- 产品版本 `0.6.0-preview.temp.63+fb48e6c4cf819c8d79ce20ac09f44243edd01172`；EXE SHA-256 `D579B06A362D9E89E721EA4DD13B3EDBE9C5E2244471665330F88C5927105A5E`，DLL SHA-256 `04C833116CD902ACCAFD9D5316C5185624703986601ADA1DA29A636CBAB2C6C2`，均与包内清单一致。发布隔离 smoke、全量 Release `SettingsSmoke`、App Release 构建通过；未启动 GUI，主题可见效果仍待桌面验收。

## 2026-10-26：照片打印窗口跟随 Windows 系统颜色

- 照片打印窗口的背景、正文、预览边框和预览底色改用 WPF 动态系统资源，设置错误文字跟随系统文字色，避免高对比度/深色环境下继续硬编码浅色或固定红色。
- 新增 XAML smoke 断言。`SettingsSmoke` Release 通过，App Release 构建 0 警告/错误；未启动 GUI，故没有可见主题验收。此变更不影响 ExplorerBrowser 原生文件区，也不代表全应用主题完成。

## 2026-10-26：当前标签使用系统强调色提示

- 每个窗格当前选中的标签增加两 DIP 强调色底边，消费动态 `ExplorerDirs.AccentBrush`；保留 WPF 默认 TabItem 模板，避免重做标签的选择和焦点行为。XAML smoke 覆盖动态资源绑定及底边范围。
- 全量 Release `SettingsSmoke` 通过，App Release 构建 0 警告/错误；未启动 GUI，因此四窗格颜色强度、深色/高对比度可读性和标签交互仍待可见验收。未改 Shell 文件视图或用户配置。

## 2026-10-26：temp.61 地址栏驱动器名称预览

- 地址栏位置菜单在“此电脑”下显示当前逻辑驱动器；本地固定盘显示卷标，未命名或不可读取时回退为驱动器类型名称，网络盘不查询远程卷标。
- 地址菜单定向 smoke、完整 Release `SettingsSmoke`、App Release 构建（0 警告/错误）和发布隔离 smoke 通过。预览包位于 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.61`，使用独立 `app/profile/`。产品版本 `0.6.0-preview.temp.61+1fcc8a4d3db9549ee108d719e6634287559ede48`；EXE SHA-256 `A18429AFB3E89595B7D1823E3DF21CB0C4EDF23744CBC072AAC15E9B24DFD84B`，DLL SHA-256 `5F0BD2AC09FA059B85DCBCE3F0153E62C3BD90B4799E7CC9B9F190849190F3EB`，与包内清单一致。未启动程序或操作桌面，实际菜单行为与 Q-Dir 互操作仍待可见验收。

## 2026-10-26：temp.60 隔离预览

- 按用户之前的预览请求发布 temp.60，内容包含地址祖先层级下拉、Windows 强调色基础接线和活动窗格强调色标记。包位于 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.60`，使用独立 `app/profile/`，不依赖 `src/`。产品版本 `0.6.0-preview.temp.60+80d2bbd147ac4d248e5554e509692c0f9c432f3b`；EXE SHA-256 `58BED64B44B2F66A8930A265B9200EC2DB78EE7EB41B5A33322974B2220DE258`，DLL SHA-256 `346D9CF4187750E28416F99E82338FB760D089D9D79DC843C253A867926A4F75`，与包内清单相符。
- 发布隔离 smoke、完整 Release `SettingsSmoke`、App Release 构建通过，构建 0 警告/错误；没有启动发布程序，不能据此视为可见验收完成。

## 2026-10-26：地址栏位置菜单改为可展开的祖先目录树

- 原有地址下拉只平铺当前目录、各级父目录和常用系统位置，未表达祖先之间的层级。现在文件系统路径按根目录到当前目录建父子菜单，悬停/展开可沿目录层级选择，点击任意祖先直接导航；当前目录显示勾选状态。
- 此电脑、桌面、下载、文档、库、网络、控制面板、回收站等常用 Shell 位置仍作为独立项显示，图标继续由 Windows Shell 提供。虚拟位置仍单独作为当前位置且不会拆成文件系统路径。
- `AddressPathMenuSmoke` 覆盖根到当前层级、父子身份、当前路径标记、常用位置分组和回收站虚拟位置；定向与完整 `SettingsSmoke` 通过，App Release 构建 0 警告/错误。可见菜单展开与鼠标行为尚未测试。

## 2026-10-26：地址栏菜单增加“此电脑”驱动器子项

- “此电脑”现在展开当前 Windows 逻辑驱动器，包含固定/可移动盘和映射盘；菜单打开时重新读取盘符，支持本次运行期间连接/断开后的变化。就绪的本地固定盘显示卷标，其他盘按驱动器类型命名；不查询网络盘卷标或连通远程共享，避免菜单构建时等待网络。
- `AddressPathMenuSmoke` 覆盖驱动器父级、逻辑盘符枚举、卷标/类型名称回退、当前路径祖先重复盘符，以及现有常用位置/虚拟位置回归。定向 smoke 通过；完整 Release `SettingsSmoke`、App Release 构建和可见菜单行为待本批验证。

## 2026-10-26：主题强调色接入活动窗格标记

- 活动窗格根边框现在绑定动态 `ExplorerDirs.AccentBrush`，非活动窗格回到系统边框色；初始化恢复和后续活动窗格切换都会统一更新四个窗格的状态。边框加粗一像素用于区分活动窗格，不影响 ExplorerBrowser 内部文件区。
- 新增活动窗格主题接线 smoke，先验证缺少接线时失败，再验证接入后通过。仍未用真实桌面观察色彩强度、不同 DPI、窄窗格边缘和高对比度表现；此项不构成可见 UI 验收。

## 2026-10-26：主题接入 Windows 系统强调色（第一阶段基础接线）

- 新增 `UIColorType.Accent` 读取和 `ColorValuesChanged` 变化处理。系统颜色更新会刷新当前应用控制器的色板；显式浅色/深色模式也保留 Windows 强调色，且不更改主题 INI。读取异常时保持上次有效值；启动时读取失败使用默认蓝色。
- 浅色/深色语义色板现根据 Accent 派生选中背景，并依据黑白文本的相对对比度选择选中文字；应用资源提供 `ExplorerDirs.AccentBrush` / `ExplorerDirs.AccentTextBrush` 入口供后续组件接入。活动标签、焦点环、按钮图标尚未消费这些资源，属于后续阶段。
- 主题色板专用 smoke、完整 `SettingsSmoke`、App Release 构建通过，构建 0 警告/错误。没有启动 GUI；实际系统切色通知、多窗口更新、高对比度及 WPF 可见效果仍待桌面验收。因此本记录只表示 API 和色板接线已完成，不代表 UI 全面跟随强调色。

## 2026-10-26：SHChangeNotifyRegister 间歇失败隔离及路线图结论修正

- 新增 HostProbe 单次注册/注销模式。无 ExplorerBrowser 的单注册进程以 1 秒间隔独立运行 8 次，均成功；相同进程内不等待的 40 次生命周期压力对照只有 11/40 次注册成功，历史矩阵也出现过 40/40 与 36/40。高频循环与单次结果不同，但目前不能将差异归因到某个参数或资源。
- 四个隐藏 ExplorerBrowser 就绪后的成对测试对当前根目录和其下空目录分别注册。两个立即连续注册的初测为 3/4 全成功，其中一轮首个成功、第二个失败。加入 150 ms STA 消息泵间隔后，8 轮中 4 轮两次均成功、2 轮两次均失败；另 2 轮在 Shell 视图就绪时发现前台窗口被外部改变并自动停止，未进入注册判断。该延时没有消除不稳定；路径内容和四个宿主是否为因果因素仍未确定。注册和宿主测试都不做文件操作；记录：`src/.state/registration-once-low-frequency-20261026.log`、`src/.state/registration-after-hosts-pumped-pairs-20261026.log`。
- 由于测试期间出现外部前台窗口切换，停止后续宿主测试。未据此修改产品 watcher、注册标志、重试或等待策略；注册根因仍开放。
- 对当前源码再跑完整 Release `ExplorerDirs.SettingsSmoke`，结果 `Settings smoke: PASS`；App 与 HostProbe Release 构建均 0 警告、0 错误。该回归覆盖托管状态、配置和命令路由，不证明可见 GUI 或 Shell 宿主行为。
- 更新过期的 GUI 验收隔离副本：`src/.state/gui-smoke/20261026-020157-971-9dd349/`。清单状态为 `prepared-not-run`，四窗格布局及独立 profile；EXE/DLL 哈希与本次隔离发布输出一致，递归大小夹具有 10,000 个测试文件。准备脚本在加载 UI Automation 和所有窗口操作之前返回，因此没有启动窗口、发送输入或访问剪贴板。此副本供后续授权的可见验收，不是 release。
- 修正上一条快速筛选标签验证结论：两轮有效自定义视图测试实际覆盖名称排序、Type 分组及 Name/Type/Size 三列宽度；初始标签完整恢复、复制标签保留完整 Shell 视图快照均通过 2/2。原文“未分别验证用户自定义排序、分组和列宽”已过时。仍不替代可见 GUI和跨重启恢复验收。
- 用户于 2026-10-26 再次确认递归目录统计保留 F9 显式触发；导航、刷新、切标签或布局切换不自动扫描。`AGENTS.md` 已加入该长期约束。当前 Release SettingsSmoke（含递归扫描器与 CrashLog）及 App Release 构建重新通过，构建 0 警告/错误；未运行 GUI，F9 实际按键路由和状态栏仍待可见验收。

## 2026-10-26：隐藏宿主标签创建与复制状态复验

- `--live-shell-operations` 两次都在产品宿主创建前因 `SHChangeNotifyRegister` 失败（`last-error=8`），未执行文件操作；第二轮生成的专用夹具已确认清理。改用独立 `--quick-filter-tabs` 隐藏探针绕过该注册边界。
- 修正探针与当前 `TabSettings`、`PaneControl` 构造器签名的差异，并在比较设置前等待筛选后的 Shell 项目计数连续三次稳定；仅等待子视图句柄就绪会早于目录枚举和设置恢复。当前 Release HostProbe 使用有效的 Details 状态（名称升序、Type 分组、Name/Type/Size 三列宽度 271/193/137）连续两轮退出码 0：初始标签恢复全部设置，复制标签保留源筛选及完整 Shell 视图快照，普通新标签与 Shift+Enter 文件夹标签采用无筛选的默认 Details 视图，对文件选择不拦截。父窗口保持隐藏、前台 HWND 未变化，临时夹具自动清理。日志：`src/.state/quick-filter-tabs-custom-views-round1-20261026.log`、`src/.state/quick-filter-tabs-custom-views-round2-20261026.log`。
- 此结果证明本机隐藏 Shell 宿主的配置恢复、新建与复制路径，不替代可见 GUI、物理键盘焦点或重启后会话恢复验收。Release HostProbe 构建（0 警告/错误）和完整 `ExplorerDirs.SettingsSmoke` 通过。注册错误边界仍开放，未操作可见桌面或发布。

## 2026-10-26：筛选切换期间保留分组视图的文件选区

- Release HostProbe 首次复跑发现：在 `keep.txt` 单选且分组稳定后，应用快速筛选会变为 `initial.txt` 与 `keep.txt` 多选，并把项目焦点移到 `initial.txt`。回调时间线显示 `SetGroupBy(null)` 后、刷新接管前，在用户输入代数未变化时 Shell 暂报 2 项选择；应用将该中间状态替换了已确认的单选快照。
- `ExplorerBrowserHost.SetQuickFilter` 现为分组切换持有选区恢复抑制租约，直到同步刷新开始并由刷新自身的抑制范围接手。HostProbe 同时补上最终前台 HWND 未变化断言；并修正探针反射调用 `Search` 时必须显式传入可选 `scopePath=null` 的遗漏。
- 修复后 Release 隐藏生产宿主快速筛选夹具连续两轮退出码 0：包含/排除、文件夹策略、清除、分组/视图行快照、选区和焦点、导出/打印捕获、四窗格隔离及 Search Folder 返回普通目录恢复均通过；父窗口保持隐藏、前台 HWND 未变化。原始日志：`src/.state/quick-filter-final-repeat-1-20261026.log`、`src/.state/quick-filter-final-repeat-2-20261026.log`。Release SettingsSmoke 与 App/HostProbe 构建也通过，均 0 警告/错误；夹具目录前后清单无差异。
- 证据仅覆盖本机隐藏 ExplorerBrowser 宿主和临时夹具，不替代筛选栏物理键盘/可见交互、其他 Shell provider 或大目录验收；未操作可见桌面、未发布。

## 2026-10-26：隐藏 Shell 视图 UIA 树未提供逐项命中目标

- Release HostProbe 在独立线程检查隐藏生产 ExplorerBrowser 的 UI Automation Control View，只返回 `SHELLDLL_DefView` Pane，没有文件名、图标或文件列表子节点；父窗口仍隐藏，前台窗口不变。记录见[右键目标调查](context-menu-target-research.md#2026-10-26-隐藏-explorerbrowser-uia-树探测)及 `src/.state/shell-view-uia-tree-20261026-093116.log`。
- 因此未改变 DirectUI 目标 `Unknown` 时省略依赖具体文件的自定义命令这一 fail-closed 行为。该结果只适用于隐藏宿主，不能代替可见 UIA 树及右键菜单验收。
## 2026-10-26：稳定 Changed 分组探针后复跑 128 路径更新

- 当前 Release HostProbe 的 128 文件 Changed 分组场景首次在真正写文件前失败：设置 `System.Size` 排序与 `System.DateModified` 分组后，探针立即设置选择，Shell 正在异步重建视图，选择读回暂时为空。失败发生在夹具选择准备，不是 watcher/通知断言。
- HostProbe 现等待完整项目快照连续三次（20 ms 间隔）一致后再设选择。修正后的同场景退出码 0：observer 收到一条合并目录更新，128 行大小读回和排序顺序均收敛；选择、焦点保持，整视图 fallback 增量为 0，前台未变。总耗时约 2,220 ms，原始日志：`src/.state/changed-metadata-128-group-recheck-20261026-092740.log`。
- 这是隐藏本机生产 ExplorerBrowserHost 与临时目录证据，证明探针现在等待 Shell 分组布局稳定后再开始文件变更；不证明可见分组标题/绘制、网络 Shell 或四窗格同时突发。生产 Changed 通知策略未改。完整 Release SettingsSmoke、App/HostProbe Release 构建（均 0 警告/错误）及本次文件 `git diff --check` 通过。

## 2026-10-26：Changed 分组 129 路径超限 fallback 复验

- 使用同一稳定分组准备逻辑运行 129 个 Changed 文件场景，超过每批 128 个 distinct path 上限。Release HostProbe 退出码 0：Shell observer 未收到按项通知，生产 fallback 增量恰为 1；129 行大小读回及最终排序顺序均正确，选择和焦点保持，前台未变。原始日志：`src/.state/changed-metadata-129-group-overflow-20261026-092904.log`。
- 该次总耗时约 2,389 ms，其中 `Refresh()` 约 17.1 ms、native refresh 约 15.6 ms；证明的是本机隐藏宿主下的超限分支和最终状态，不代表可见像素、其他 Shell provider 或大批量同时突发响应。

## 2026-10-26：当前选区复核策略在 50,000 项产品宿主复跑

- 连续两轮隐藏 Release HostProbe `--live-shell-directory-stress-large 50000`，关闭 Shell callback trace，不打开可见产品窗口、不发送桌面输入。两轮退出码均为 0；产品 watcher 创建/删除事件均完整 `50,001/50,001`，50,000 项创建、128 项批量删除与最终清空均可见并收敛；原有 `second.txt`、`third.txt` 选择和未选中的 `initial.txt` 焦点最终恢复。第二轮独立 observer 也完整 `50,000/50,000`；第一轮少观察一个创建事件（`49,999/50,000`），但产品 watcher 无缺失。
- 负载间 Dispatcher 间隔差异明显：第一轮 ContextIdle/Input/Normal 最大间隔 1,493/1,061/841 ms，第二轮为 222/214/214 ms；专用线程 heartbeat 最大间隔分别 1,450/214 ms。第一轮 stdout 在会话回传中被截断，没有单独存原始日志；第二轮完整日志为 `src/.state/directory-stress-50000-selection-recheck-20261026-092411.log`。该波动不足以得出稳定响应结论。
- 该样本补上“最新延迟选区复核策略”的 50k 隐藏产品负载覆盖，未证明可见多窗格键鼠体验、其它 Shell provider、网络盘或跨机器稳定性；自动刷新 P0 仍开放，未发布。

## 2026-10-26：验证所有查看方式的原生列标题

- 隐藏 ExplorerBrowser `--folder-header-options-hosted` 四个有效轮均通过（NativeProbe Release build 0 警告/错误；四轮退出码 0）。初始化前 `FVO_CUSTOMPOSITION` 从 `0` 设置并读回为 `0x2`；两个 `OnViewCreated` 均成功更新 folder flags。四轮的 1000 ms 采样均显示全部列头样本可见（共 20 个采样/轮）；列顺序/宽度、排序、分组及选区身份保持，header click 探针也保留 Shell 列/排序状态。
- 两轮复测前的一次调用在视图模式断言前因目录异步枚举尚未达到 3 项而退出；`ViewStyleProbe` 现最多等待 3 秒的 fixture 项数收敛，并在超时时报告 HRESULT 与项目数。失败样本不计入列头结果。该隐藏 ExplorerBrowser 原生探针使用共享生产 flag helper，但不是产品 WPF 窗口测试；立即切换时部分模式仍短暂没有可见 header。物理输入/菜单/焦点和可见绘制尚未验证，没有操作可见桌面或发布。日志：`src/.state/folder-header-options-hosted-20261026-091326.log`、`src/.state/folder-header-options-repeat-1-20261026-091539.log`、`src/.state/folder-header-options-repeat-2-20261026-091614.log`（准备阶段失败）、`src/.state/folder-header-options-settled-1-20261026-091758.log`、`src/.state/folder-header-options-settled-2-20261026-091835.log`。

## 2026-10-26：恢复 Shell 自然更新后延迟丢失的存活选区

- 两轮 10,000 项基线重跑中，一轮通过、一轮计数与焦点通过但最终选区为空。失败时间线表明 watcher 驱动的恢复曾成功，之后 Shell 又在没有新 selection callback 的情况下清空选区。没有放宽原有门槛。
- 增加最多 3 秒的自然更新后选区复核；只在同一窗格/导航、用户输入版本未变且 Shell 当前选区为空时，按原 PIDL 快照恢复。导航、用户输入和目录监视器停止均取消复核。
- 新状态门测试先按预期失败；定向 smoke、完整 Release SettingsSmoke、App/HostProbe Release build（0 警告/错误）通过。修复后 10,000 项隐藏生产 HostProbe 连续三轮退出码 0，计数、存活选区和焦点均通过。日志与因果边界见[自动刷新调查记录](live-update-investigation.md#2026-10-26：监视器安静后复核存活选区)。没有操作可见 GUI、没有发布；网络/虚拟 Shell 与用户可见验收仍待完成。

## 2026-10-26：当前 Release 逐窗格复验 F2 重命名箭头路由

- HostProbe Release 构建通过（0 警告、0 错误）。路线图规定逐个测试 pane 0–3；通用 `--live-shell-rename-editor` 入口当前只选择 pane 0，因此本轮改用四个明确的 `--live-shell-rename-editor-pane-N` 命令顺序执行。
- 四窗格都通过真实生产 ExplorerBrowser Shell 项目进入原生重命名编辑器；Left/Right 移动文本插入点，Shift+Left/Right 选取名称字符，四方向键处理后编辑仍保持焦点，且其他窗格不认领该编辑器焦点。四次探针均退出码 0。
- 这是隐藏宿主的合成 `TranslateAcceleratorCore` 输入证据；没有覆盖可见多布局窗口中的物理 F2 消息路径和用户实按键，因此保留 P0 GUI 验收项；未启动产品窗口或生成 release。

## 2026-10-26：当前 Release 复跑 10,000 项文件变更

- Release HostProbe 构建 0 警告、0 错误；隐藏生产宿主 `--live-shell-directory-stress-large 10000` 退出码 0。Watcher 创建/删除各记录 10,000/10,000 与 10,001/10,001，最终磁盘/视图 3/3，选区和焦点恢复；创建无 fallback，删除有 2 次 fallback。
- 单轮过程中 Shell 计数曾从 9,874 降到 975 后恢复，创建期间最高看到 10,003 项。创建/删除可见时间约 1,031/784 ms，操作完成约 1,816/784 ms；最大 Dispatcher 心跳间隔 139 ms，Shell 选区与焦点读取最大各 15 ms。该隐藏数据不证明可见 UI 流畅或过程计数稳定，自动刷新 P0 继续开放。完整边界见[动态更新调查](live-update-investigation.md#2026-10-26-当前-release-的-10000-项创建删除回归)。
- 回看 fallback 摘要确认两次删除阶段刷新都因同一批次包含创建与删除而 `forced=True`，当时 expected/actual 相等；这是为防止净计数掩盖条目替换的既有规则，不据此认定多余刷新。计数大幅回落发生于异步刷新/重枚举期间，根因尚未分离。

## 2026-10-26：完成隔离 IShellView 逐项颜色的屏幕内采样

- 私有 Windows Desktop 探针在可见临时 `IShellView` 中采样八种查看模式：所有模式行颜色像素均未命中；Icon、Details、SmallIcon、List 没有可用项目绘制回调，Tile/Content 虽有项目绘制通知但没有逐项通知。普通 ListView 对照组收到回调并命中目标像素，确认采样链可工作。Release NativeProbe build 0 警告/错误，watchdog 退出码 0（约 21 秒）。
- 主桌面前台句柄保持不变，探针记录 `product-untouched=True` 并由子进程退出清理。隔离 Shell 菜单、视图状态和临时文件操作读回不等于产品 ExplorerBrowser 或用户可见验收。依据和日志见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-26-私有桌面逐项颜色屏幕内采样)。
- 不再重复相同 DefView `NM_CUSTOMDRAW` 项目回调采样；逐项颜色仍未解决，不据此迁移产品宿主。后续优先推进默认 Shell 菜单和文件操作体验；颜色若继续，应先验证不同技术路径。

## 2026-10-26：验证隔离颜色原型的 GDI 字体恢复

- `ShellViewColorFontSmoke` 新增内存 GDI DC 回归：对逐项字体样式创建并选入临时字体，模拟项目绘制结束后恢复原字体，并确认原字体重新成为当前对象。定向 `--shell-view-color-policy-smoke` 通过。该用例验证真实 GDI 资源操作与恢复顺序，不创建 Shell view，也不证明 DefView 通知、屏幕绘制或产品 ExplorerBrowser 已支持逐项颜色。

## 2026-10-26：验证 CDB3 当前筛选缓冲区参数拒绝

- 隔离颜色宿主的完整 `ICommDlgBrowser3` vtable smoke 现覆盖 `GetCurrentFilter` 的无效缓冲区：空指针与容量 0 都返回 `E_INVALIDARG`，且容量为 0 时保留调用方缓冲内容；有效缓冲区仍以 NUL 终止空筛选文本并返回 `S_OK`。
- `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --shell-view-color-policy-smoke` 通过。这通过实际托管 COM vtable/CCW 边界验证参数合同；没有创建 Shell view，也不证明 DefView 的调用时序。

## 2026-10-26：回调元数据故障不会误过滤文件

- 阶段 2 的隔离 CDB3 smoke 补上三类 Shell provider 故障：`IFolderView::GetFolder`、`IShellFolder::GetAttributesOf`、`GetDisplayNameOf` 返回失败。每种情形均经真实 `ICommDlgBrowser3::IncludeObject` vtable 槽调用，验证回调返回 `S_OK` 并保留项目；`.txt` 匹配仍保留、`.bin` 不匹配仍返回 `S_FALSE`。
- `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --shell-view-color-policy-smoke` 通过。该 smoke 使用故障注入 COM 夹具，不加载真实第三方 provider，也不创建 `IShellView`；只证明错误开放策略穿过 CCW 回调边界。

## 2026-10-26：Q-Dir 程序启动项过滤通过真实 COM 回调槽验证

- 补齐隔离颜色宿主过滤 smoke 的边界：此前 `.txt` 保留与 `.bin` 排除样例直接调用托管 `IncludeObject`；现在经 `ICommDlgBrowser3` 的真实 CCW vtable 槽调用，并传入模拟 `IFolderView` 与子 PIDL。两个结果分别返回 `S_OK`（0）和 `S_FALSE`（1）。
- `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --shell-view-color-policy-smoke` 通过，包括完整 CDB2/CDB3 槽、缺失元数据失败开放和有效筛选项。此证据验证接口/委托边界与策略结果；模拟对象未建立真实 `IShellView`，不能证明 DefView 的实际枚举顺序或可见着色效果。

## 2026-10-26：逐槽验证隔离宿主的 ICommDlgBrowser2/3 COM 回调

- 隔离颜色宿主此前只通过 `QueryInterface` 证明暴露 CDB/CDB2/CDB3，并以托管直接调用检查少数方法。现在先取得专用 CDB2/CDB3 接口指针，再按 vtable 槽调用 CDB3 全部九个回调及 CDB2 六个继承回调，验证 `S_FALSE`/`S_OK`、`GetViewFlags` 输出、`GetCurrentFilter` 空终止输出和 `OnStateChange(CDBOSC_SELCHANGE)` 对选区代际的更新。
- 首次探测误用 `IShellBrowser` 指针索引 CDB3 槽位，返回无效 HRESULT；改为分别 QueryInterface 到 CDB2/CDB3 指针后全部通过。定向 `--shell-view-color-policy-smoke` 通过。未创建 Shell view；此项验证 CCW 接口布局和委托，不证明 DefView 调用时序。

## 2026-10-26：初始 Shell 视图创建失败也走销毁路径

- 同类生命周期复核发现，隔离颜色原型只在初始 `CreateViewWindow` 成功后标记 view 已创建；若初始创建调用失败（包括未返回 HWND），finally 会直接释放 view/folder。现改为记录创建调用是否发生，最终清理统一走 `RetireAfterCreateAttempt`：调用前失败直接释放，调用后尝试 `DestroyViewWindow` 并按结果立即释放或延迟保留。初始创建成功却没有 HWND 也作为错误处理并执行销毁。
- 复用的生命周期 smoke 覆盖未调用创建时直接释放、调用后无 HWND 仍调用 destroy、destroy 失败时保留引用以及父窗口销毁后释放；定向 `--shell-view-color-policy-smoke` 通过。未运行真实 Shell view，因此真实初始创建的部分失败行为没有被触发。

## 2026-10-26：验证 BrowseObject COM 边界的异常 HRESULT 转换

- 阶段 1 的托管 smoke 已证明导航委托抛异常时历史不变、暂存 PIDL 释放；现通过 `IShellBrowser` CCW 实际 vtable 的 `BrowseObject` 槽调用同一宿主，注入带确定 HRESULT 的 `COMException`。边界返回原 HRESULT `0x80070005`，当前位置/index/count 不变，暂存副本释放。
- 定向 `--shell-view-color-policy-smoke` 通过。这验证托管 CCW 对该回调异常的 HRESULT 映射，不创建真实 Shell view，不覆盖 Shell 调用时序或实际导航行为。

## 2026-10-26：导航回调抛异常时清理暂存 PIDL

- 阶段 1 历史状态 smoke 增加导航委托抛异常的失败场景。`ShellViewNavigationHistory.BrowseObject` 在回调前克隆目标 PIDL；回调抛异常时 `finally` 必须释放该暂存副本，且不能提交新位置或改变 index/count。
- 新用例验证异常向调用方传播、暂存 PIDL 释放一次、当前 PIDL/index/count 保持原值。定向 `--shell-view-color-policy-smoke` 通过；本轮没有运行 COM Shell view 或 GUI，故只证明托管历史状态及 PIDL 所有权策略。

## 2026-10-26：创建窗口失败且未返回 HWND 时仍清理 `IShellView`

- 阶段 1 生命周期复核发现，导航失败清理原先以 `nextWindow != 0` 判断是否调用 `DestroyViewWindow`。若 `CreateViewWindow` 已被调用但失败时没有写回 HWND，代码会直接释放新 view/folder；改为记录创建调用是否发生：调用前失败才直接释放，调用后无论是否取得 HWND 都通过引用退休策略尝试 `DestroyViewWindow`，失败时继续保留到父窗口销毁。
- 先加入“创建调用发生但 HWND 仍为 0”的策略断言，验证在新增 API 前按预期编译失败；接入后确认会调用 destroy，并覆盖 destroy 失败时新 view 与 folder 都延迟保留到父窗口销毁。定向颜色策略 smoke、NativeProbe Release 构建（0 警告/错误）、完整 Release SettingsSmoke 与 diff 空白检查通过。未运行真实 Shell view，因此 `CreateViewWindow` 部分失败时 provider 的实际行为仍未验证。

## 2026-10-26：隔离 `IShellView` 导航销毁失败时延迟释放 COM 引用

- 生命周期复核发现：导航已创建并提交新视图后，旧视图 `DestroyViewWindow` 失败仍会释放旧 `IShellView` 和 `IShellFolder`，可能让仍存活的子窗口持有失效的站点引用。新增引用退休队列：成功销毁时立即释放；返回失败或调用抛异常时保留视图与文件夹引用，待父窗口确认销毁后再释放。新视图提交后的旧视图清理错误不再向历史组件报告导航失败，避免历史回滚到已被替换的窗口状态。
- 无窗口策略 smoke 覆盖成功销毁、HRESULT 失败、调用抛异常和父窗口销毁后的延迟释放；定向 `--shell-view-color-policy-smoke`、NativeProbe Release 构建（0 警告/错误）及完整 Release SettingsSmoke 通过。没有调用真实 `IShellView`、创建 Private Desktop 或操作 GUI；因此真实 COM 销毁失败行为、父窗口清理失败和可见导航仍未验证。若父窗口清理失败，保留的裸 COM 引用会故意泄漏至进程退出；OS 进程回收不等同于对这些接口调用 `Release`。产品 ExplorerBrowser 未改。

## 2026-10-26：隔离导航历史遵守 `SBSP_CREATENOHISTORY`

- 官方 `BrowseObject` 契约规定 `SBSP_CREATENOHISTORY`（SDK 值 `0x00100000`）不应向 travel log 新增条目；历史组件此前仍会追加目标。现将成功的该类普通导航建模为替换当前位置，不增加 history count/index；从非尾部位置发起时清除失效的前进分支。此“替换当前停靠点”的内部模型依据“不添加新 entry”推导，Shell 原生 TravelLog 的精确快照规则仍未实测。
- 新 smoke 先通过 root→child→branch、Back 构造前进分支，再对 `SBSP_CREATENOHISTORY` 目标执行导航；修复前断言失败，修复后确认当前位置替换、index 不变、count 不增且 Forward 不再进入旧分支。另验证失败的 no-history 导航保留原位置/前进分支，以及 `SBSP_RELATIVE | SBSP_CREATENOHISTORY` 组合先合并 PIDL、再替换当前停靠点。定向 `--shell-view-color-policy-smoke`、NativeProbe Release build（0 警告/错误）、完整 Release SettingsSmoke 和差异/空白检查均通过。无 Shell 窗口/Private Desktop/GUI。
- 依据：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)。

## 2026-10-26：隔离导航历史验证同位置访问

- 按阶段 1 的同路径门槛，新增内存 PIDL smoke：导航回调仍收到同位置请求，但历史 index/count 保持单项且当前 PIDL 不变，避免重复位置在后退/前进历史里形成伪停靠点。
- 定向 `--shell-view-color-policy-smoke`、NativeProbe Release build（0 警告/错误）、完整 Release SettingsSmoke 和空白检查通过；没有创建 Shell view、Private Desktop 或 GUI。

## 2026-10-26：隔离导航历史支持相对 PIDL

- 契约复核发现 `ShellViewNavigationHistory` 将普通 `BrowseObject` PIDL 直接视为 Desktop-relative absolute PIDL，未处理 `SBSP_RELATIVE`。微软接口说明此标志代表相对当前文件夹的 PIDL；Windows SDK `ShObjIdl_core.h` 定义其值为 `0x1000`。现在历史组件使用 `ILCombine` 将其与当前绝对位置组合，再把绝对目标传给宿主导航回调，并清除 `SBSP_RELATIVE` 类别位。
- 内存 PIDL smoke 先因缺少合并委托/实现而按预期编译失败，接入后验证组合目标与回调标志；颜色宿主另验证当前目录的相对导航、普通/Parent/Back、失败保留当前位置/历史和释放历史 PIDL。定向 `--shell-view-color-policy-smoke` 通过。没有运行 Shell view、Private Desktop 或 GUI，所以仅证明 PIDL 历史策略和回调路由，不证明 Shell provider 的实际导航结果。
- 依据：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject) 与 Windows SDK `ShObjIdl_core.h`。

## 2026-10-26：颜色隔离宿主接入通用导航历史

- `ShellViewColorBrowserHost.BrowseObject` 现在委托给 `ShellViewNavigationHistory`；颜色原型的导航回调按目标 PIDL 创建新的文件夹与 `IShellView`，创建成功后切换当前 view，并释放前一 view/folder。父级、后退/前进和失败保留语义复用既有历史组件，未改产品 ExplorerBrowser。
- 无 Shell 窗口 smoke 验证颜色宿主把普通导航、父级、后退和失败请求路由到历史组件；失败导航保留当前位置/历史，释放宿主会释放其历史 PIDL。`--shell-view-color-policy-smoke`、NativeProbe Release build 与完整 SettingsSmoke 均通过。没有运行 `IShellView`、Private Desktop 或 GUI，因此实际 COM 导航生命周期和窗口替换仍未验证；这只是补齐隔离原型的一项宿主合同。

## 2026-10-26：修复隔离 Shell 导航历史的父级 PIDL 生命周期

- 将隔离 `IShellView` 宿主的父级/后退/前进历史状态抽为 `ShellViewNavigationHistory`，`FilteredHost` 复用该组件。原实现先释放 `SBSP_PARENT` 返回的临时 PIDL，之后仍用该指针比较并复制历史；现在先持有独立副本，Shell 导航成功后再提交历史分支，失败时清理暂存 PIDL 且不改变当前历史。
- 新 smoke 使用释放哨兵 PIDL 覆盖父级导航、后退/前进、分支截断和失败不变；故意恢复“先释放再读取”的顺序时按预期失败，恢复后通过。NativeProbe Release 构建 0 警告/错误，完整 SettingsSmoke 通过。未运行 Shell/GUI/Private Desktop；当时颜色原型宿主尚未接入导航，产品 ExplorerBrowser 未改。

## 2026-10-26：Program Starter 写入冲突时不显示未保存改动

- 根因：程序启动器先修改窗口内的 ObservableCollection，再调用带外部编辑保护的 `ProgramStarterStore.Save`。另一个窗口已写入时，存储层正确拒绝旧快照，但当前窗口仍保留未保存的新增/编辑/删除/导入项，关闭后这些改动消失。
- 现改为先保存候选完整列表，成功后才更新窗口集合；失败时保留原显示列表。定向 smoke 先因缺少提交边界编译失败，接入后用两个 store 实例真实复现陈旧快照冲突，确认外部文件及窗口列表都保留，再确认成功写入后列表与磁盘一致。未启动 GUI；冲突提示和菜单操作仍待可见验收。

## 2026-10-26：细化 `.qdr` 颜色开关导入预览

- 导入器不应用 `useColor` / `useTreeColor`、颜色规则或背景值；预览现在对值为 `1` 的开关逐项提示已启用但未应用，值为 `0` 时不制造待迁移设置的暗示，其他值提示无法识别且不回显原值。规则与背景的序列化格式仍未知，不据此添加颜色导入。
- `QdrImportSmoke` 分别覆盖启用、关闭和无效值；新增断言先失败，再接入提示逻辑。定向及完整 Release `SettingsSmoke`、App Release 构建和 diff 空白检查结果见本轮验证。未启动 GUI、Q-Dir 或 Shell 探针；真实导入预览仍待桌面验收。

## 2026-10-26：Shell 命令状态查询期间保留刷新请求

- Shell 原生菜单查询可能泵送消息。若查询期间选区/活动上下文变化并排入刷新，嵌套 Dispatcher timer 不应先消费请求；否则外层会丢弃过期快照后没有后续刷新，工具栏按钮可能一直保持旧状态。
- `ShellCommandRefreshGate` 现在在查询期间保留 pending 刷新；查询完成后再启动一次合并刷新。复核发现 `SettingsSmoke` 曾有重复命令分支，导致该门控 smoke 从定向入口和全量入口中都未执行；现已修正入口并加入全量套件。故意移除查询中保护后，定向 smoke 按预期失败；恢复后定向及全量 Release SettingsSmoke 均通过。没有启动 Shell/GUI；真实 COM 重入及工具栏可见状态仍待后续验收。

## 2026-10-26：temp.58 隔离预览

- 按此前预览请求，已发布 `releases/preview-0.6.0-temp.58/`，收录网络驱动器成功后通知“此电脑”刷新等当前源码更新；包内只有独立 profile，不写入日常配置。Release `SettingsSmoke`、App Release 构建（0 警告/错误）及发布隔离 smoke 均通过；程序集产品版本 `0.6.0-preview.temp.58+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `66661E45FC3F981E4858EB016447F7C937E1321E0292DD75C82778E06B677A15`，DLL SHA-256 `FE0ECEEAF2F4CB12687D43AFC851333D2F2E757ACFBDE525955C95EFA79D5541`，与包内清单一致。
- 本包不启动 GUI；网络映射/断开后的真实 ExplorerBrowser 刷新、可见焦点/菜单/文件操作、Q-Dir 实际导入导出仍待桌面验收。

## 2026-10-26：映射网络驱动器后通知“此电脑”视图刷新

- `WNetConnectionDialog` / `WNetDisconnectDialog` 返回成功后，应用通过 `SHGetKnownFolderIDList(FOLDERID_ComputerFolder)` 取得“此电脑”PIDL，并发送 `SHCNE_UPDATEDIR` + `SHCNF_IDLIST | SHCNF_FLUSHNOWAIT`。取消及失败不发送通知；不主动刷新无关文件窗格，也不改变会话选择/焦点。
- `NetworkDriveDialogsSmoke` 覆盖成功映射、取消、成功断开和失败四种分派，以及真实 `SHGetKnownFolderIDList` / `SHChangeNotify` 调用边界所用的目标 GUID、事件码、旗标、PIDL 参数和释放路径（通过注入委托验证；未实际发送系统通知）。完整 Release `SettingsSmoke`、App Release build（0 警告/错误）和相关 `git diff --check` 通过。没有实际建立/断开网络连接或启动 GUI，因此 ExplorerBrowser 与 Explorer 的可见刷新行为仍待桌面验收。
- 依据：[SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify)说明 `SHCNE_UPDATEDIR` 可通知 Shell 某个目录内容变化，且 `SHCNF_FLUSHNOWAIT` 开始投递后立即返回；[CSIDL_DRIVES](https://learn.microsoft.com/en-us/windows/win32/shell/csidl) 对应包含映射网络驱动器的“此电脑”虚拟文件夹。

## 2026-10-26：完成隔离颜色原型的字体样式绘制接线

- 颜色规则解析出的粗体、斜体、下划线、删除线现在一同传入隔离 `NM_CUSTOMDRAW` 处理器；自绘行从当前字体继承未配置属性，按行选择临时 GDI 字体，并在项目绘制结束后恢复与释放。
- 字体策略 smoke 覆盖四属性启用、显式关闭与继承；`dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --shell-view-color-policy-smoke` 通过，NativeProbe Release build 0 警告/错误。没有启动 Shell/GUI/Private Desktop 探针，真实绘制生命周期和像素效果仍待后续专门验收；不代表产品 ExplorerBrowser 已支持颜色规则。
- 已确认的 Program Starter 方向已有完整实现和隔离 smoke：独立 profile `ProgramStarter.ini`、Q-Dir `[Program]` 导入预览与显式应用均就绪，用户 Q-Dir.ini 不会被写回。当前缺少带有效启动项的用户 Q-Dir.ini 正向样本，真实导入预览界面仍待桌面验收。

## 2026-10-26：隔离颜色原型应用逐视图背景色

- NativeProbe 的隔离 Shell 列表视图现在按 `#BG-A` / `#BG` 规则应用背景色，并在导航创建新视图后重新设置；无匹配规则时使用 `CLR_NONE`，交由父窗口绘制背景。该实现仅验证原生列表视图背景色接线，不属于产品功能。
- 背景色策略 smoke 覆盖颜色透传、无规则回退和空窗口句柄拒绝；NativeProbe Release build 与 `--shell-view-color-policy-smoke` 通过。没有启动 Shell/GUI/Private Desktop 探针，真实 Shell 视图显示效果仍待单独验收。

## 2026-10-26：颜色探针接入原生项目过滤回调

- 颜色原型宿主现同时暴露 `ICommDlgBrowser`、`ICommDlgBrowser2`、`ICommDlgBrowser3`，并把 `IncludeObject` 路由到现有 `ShellFolderFilter`。这属于隔离原生视图的过滤回调，并非产品使用的 `IFolderFilterSite` 路径；既有记录显示原始 `IShellView` 不支持 `IFolderFilterSite`，故该探针无法照搬 ExplorerBrowser 的安装机制。隔离夹具加了一个应被过滤的 `.bin` 文件；探针断言全部项目都经过回调、列表只保留两条可见 `.txt` 项，从而让颜色索引按过滤后的 Shell 顺序建立。修复 NativeProbe 对 `HiddenItemsPreference` 的漏链接。
- 无窗口 smoke 从 `IShellBrowser` 指针实测 `QueryInterface`，确认三个 `ICommDlgBrowser` 版本可被查询；还确认 `GetViewFlags` 不设置 `NOINCLUDEITEM`，并用假的 `IFolderView`/`IShellFolder` COM 对象通过生产 `IncludeObject` 验证 `notes.txt` 保留、`notes.bin` 过滤和元数据失败开放。颜色策略 smoke、NativeProbe Release build、完整 Release SettingsSmoke、App Release build 与空白检查通过，0 警告、0 错误。没有运行 Shell 宿主、HiddenProbe、Private Desktop 或 GUI；DefView 调用时序、Shell 枚举与颜色绘制仍待独立验收，产品 ExplorerBrowser 未改。

## 2026-10-26：从快速链接菜单添加单个所选项目

- Q-Dir Quick-Link 支持快速启动文件夹、文件和程序；本项目原菜单只能直接添加当前目录，所选文件/文件夹需手动打开 profile 的 `.lnk` 管理目录创建。现增加“添加所选项目”，支持一个已选文件系统文件、文件夹或可解析的 Shell 文件夹。
- 菜单打开时以当前单选状态决定启用；点击时重新读取选择并核对活动窗格，避免把打开菜单时的过期选择加入。Store 拒绝缺失目标；文件走路径快捷方式，文件夹沿用 Shell PIDL 快捷方式。
- 后续源码复核发现菜单构建阶段的 Shell 位置可用性探测可能调用第三方命名空间解析器并抛错；现将该检查限制在局部异常边界内，失败时报告状态并禁用该命令，避免整个快速链接菜单无法打开。纯逻辑 `QuickLinkAvailability` smoke 覆盖成功、正常不可用和注入 `COMException` 三种结果；provider 异常转换用例先因缺少门控 API 编译失败，再接入通过。
- 快速链接目录枚举的 I/O/权限失败也已限制在列表加载边界；错误写入状态栏，菜单其它命令保留，并显示禁用的错误项。新增隔离回归覆盖 `QuickLinks` 同名文件阻塞目录的持久化配置损坏场景；权限拒绝/枚举期间的瞬态 I/O 仍未做异常注入，当前由源码保护与 App 构建验证。
- 隔离 QuickLinkStore smoke 验证普通文件/文件夹、已有虚拟 Shell 文件夹及缺失目标。完整 Release `SettingsSmoke`、App Release build（0 警告/错误）、`publish-smoke.ps1` 和 `git diff --check` 通过。未运行 GUI；真实菜单启用状态、鼠标/键盘操作与重启后显示仍待桌面验收。
- 对标：[Q-Dir Help](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2FHelp+file)列出 Quick-Link 功能；本项目 `.lnk` 仍由 Windows Shell 打开和管理。

## 2026-10-26：跟随 Shell 设置通知刷新隐藏项目视图

- 扩展并重命名为 `SystemSettingChangeHook`：所有 `WM_SETTINGCHANGE` 都排队检查当前 Shell 的普通隐藏项设置；环境变量仍只在通知名为 `Environment` 时刷新。仅当偏好为“跟随 Windows”且读取值发生变化时，才刷新已创建的 ExplorerBrowser 视图；显式显示/隐藏模式不被系统设置覆盖，未创建的标签不调用 Refresh。
- 回归覆盖未知设置名/空参数仍触发 Shell 重读，错误消息不触发，`Environment` 同时保留原环境刷新路径；策略用例验证仅 FollowWindows 且状态变化时刷新。新增断言先因缺少策略 API 编译失败，再接入通过。
- 完整 Release `SettingsSmoke`、App Release 构建（0 警告/错误）、`publish-smoke.ps1` 和相关 `git diff --check` 通过。没有启动 GUI；Windows 在用户修改“显示隐藏项目”后是否广播 `WM_SETTINGCHANGE`、ExplorerBrowser 刷新后是否立即更新仍待桌面验收。本改动处理任意已到达的设置通知，不假定具体通知名称。
- 依据：[WM_SETTINGCHANGE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange)说明收到系统设置变化时应重载应用依赖的设置；[SHELLSTATE](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ns-shobjidl_core-shellstatea)定义普通隐藏项目状态。

## 2026-10-26：接入应用级隐藏项目显示偏好

- 新增“视图 > 隐藏项目”三态：跟随 Windows（默认）、显示、隐藏；应用级偏好保存在 profile `HiddenItems.ini`，隔离发布包按白名单原样携带。所有窗格及现存/新建标签共用该偏好，切换时更新并刷新现存视图，不改系统设置。
- `ShellFolderFilter` 将显式偏好映射为 `SHCONTF_INCLUDEHIDDEN`；保持 `SHCONTF_INCLUDESUPERHIDDEN` 未设置。搜索视图若只因显式可见性而挂过滤器，会传空快速筛选规则，避免旧标签快速筛选影响搜索结果。配置 smoke 覆盖默认/往返、损坏备份、未来格式拒写、外部编辑冲突、三态枚举标志和快速筛选/搜索过滤附着策略。
- 后续源码复核修复延迟创建标签的刷新错误：全局设置仍保存到宿主并在其 Shell view 初始化时应用，但仅对已有 view 调用 Refresh，避免未创建的标签被报告为刷新失败。新增 view-availability 门控回归，先因缺少策略 API 编译失败，再接线通过。
- 定向与完整 Release `SettingsSmoke`、App Release build（0 警告/错误）、`publish-smoke.ps1` 及相关 `git diff --check` 通过。延迟创建 Shell view 的偏好门控在完整 SettingsSmoke 中通过。未启动 GUI；普通隐藏项的真实显示/隐藏、受保护系统项不显示、快速筛选共存与导航/刷新后的 ExplorerBrowser 可见行为仍待 GUI 验收。
- 当时追加核查发现原 `EnvironmentSettingChangeHook` 只路由名称为 `Environment` 的 `WM_SETTINGCHANGE`。Microsoft 文档没有确认 Windows 修改隐藏项目选项时广播的确切名称，因此当时只记录“下次 Shell 枚举读取当前值”，未推断特定通知；后续通用通知重读与刷新逻辑见本文件顶部条目。
- 依据：[IFolderFilter::GetEnumFlags](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderfilter-getenumflags)、[_SHCONTF](https://learn.microsoft.com/windows/win32/api/shobjidl_core/ne-shobjidl_core-_shcontf)。

## 2026-10-26：阻止未处理的文件区箭头键变成 WPF 焦点导航

- `ExplorerBrowserHost.TranslateAcceleratorCore` 在 Shell 返回 `S_FALSE` 后会落入 `HwndHost` 的基类路由；箭头消息可能被 WPF 当成焦点导航。现新增窄门控，仅当 Shell 明确返回 `S_FALSE`、焦点位于文件内容区且不是列标题或编辑框时，才把原始 `WM_KEYDOWN` 发给当前原生焦点控件并结束 WPF 路由。Shell 已处理或返回错误时不介入。
- 新回归先因缺少门控编译失败，接入后完整 Release `SettingsSmoke`、App Release build 与 `git diff --check` 通过，构建 0 警告/错误。未运行 GUI；粘贴/删除后物理方向键选择和各视图的原生响应仍待桌面验收。

## 2026-10-26：颜色原型只绘制规则明确设置的属性

- 源码复核发现原型会把固定灰色背景写到所有未选中行，并会让文字色规则覆盖未设置的背景属性。现在无匹配规则交回 Shell 原生绘制；命中规则时仅覆写已设置的文字/背景属性；选中行仍由 Shell 绘制。新增 text-only 和 unmatched 回归，先以 nullable 颜色状态用例失败，再接入策略和 `NM_CUSTOMDRAW` 处理器。
- `--shell-view-color-policy-smoke` 与 NativeProbe Release build 通过，0 警告/错误。未运行可见像素验证；未改变产品 ExplorerBrowser 或 `.qdr` 导入。

## 2026-10-26：将逐项文字色接入隔离颜色原型

- 原型颜色策略之前已解析文字色，但 `NM_CUSTOMDRAW` 适配器只应用背景色并把文字色固定为默认值。现在将逐项文字色与背景色一起交给隔离绘制处理器，选中行保留 Shell 原生绘制；新增逐行双属性检查，先验证未接接口时失败，再接入。
- `--shell-view-color-policy-smoke` 与 NativeProbe Release build 通过，0 警告/错误。没有运行可见像素探针；这没有验证屏幕绘制、字体属性或产品 ExplorerBrowser，也没有改变 `.qdr` 导入。

## 2026-10-26：避免关闭目录树时对 Q-Dir 默认分隔值误报

- Q-Dir 官方 [FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR) 的示例在 `tree_mode=0` 时仍带有四个 `TreeNsplit=2500` 默认值。导入器现仅在树模式未明确关闭时，对未支持的宽度字段提示警告；明确关闭时不再显示无实际影响的宽度警告。启用或无效模式仍保留警告，不推断分隔值单位或宽度。
- 新增回归覆盖官方关闭模式、启用模式和无效模式：先确认关闭模式用例失败，再调整导入器。完整 Release `SettingsSmoke`、App Release 构建通过，构建 0 警告/错误；没有运行 GUI，也没有改变 `.qdr` 宽度导入行为。

## 2026-10-26：核对 Q-Dir 历史快捷键与本项目焦点导航映射

- Q-Dir 官方旧版帮助页列出 Ctrl+1～4 聚焦四个 Q-View、Shift+F6 切换下一种视图；当前项目把 Ctrl+1～9 用于当前窗格的标签编号选择，把 Shift+F6 用于反向焦点区域遍历，并提供 Tab/Shift+Tab 与 F6/Shift+F6 键盘导航以及 Ctrl+Shift+1～8 视图切换。来源：[Q-Dir 快捷键说明](https://www.softwareok.com/?seite=q-dir-hilfe)。
- 这两组旧版快捷键与本项目已使用的键位冲突，不能仅凭 4.51 帮助页认定当前 12.79/12.82 仍保持相同映射。本轮没有覆盖现有键位；后续对照当前版本实际行为时再决定是否增加兼容映射。该项不改变现有焦点顺序和视图控制。

## 2026-10-26：补齐 Q-Dir `#Hidden` 隐藏项规则别名

- 本机 Q-Dir `Q-Dir.ini` 的 `[Filter2]` 规则样本使用 `#Hidden`，而公开 FAQ 使用 `#` 表示隐藏项。隔离颜色原型的规则匹配器现将两种写法都识别为隐藏项；新增可见/隐藏正反例，先确认 `#Hidden` 用例失败，再实现别名。
- `--shell-view-color-policy-smoke`、完整 Release `SettingsSmoke`、App Release build 与 NativeProbe Release build 通过，构建 0 警告/错误。未修改 Q-Dir `.qdr` 导入器或产品 ExplorerBrowser；本机 INI 数值列语义及 `.qdr` 颜色字段编码仍未确认，也未做 GUI/像素验收。

## 2026-10-26：补充隔离颜色原型的视图背景规则

- Q-Dir 官方 FAQ 47 把 `#BG` 定义为视图背景、`#BG-A` 定义为活动视图背景；NativeProbe 现提供独立匹配入口，活动视图才命中 `#BG-A`，普通 `#BG` 对两类视图生效，且按既有逐属性规则顺序解析。纯 smoke 覆盖这两个占位符、活动/非活动回退、文件名规则隔离及优先级锁定。
- FAQ 117 的公开 `.qdr` 示例包含 `useColor` 与 `useTreeColor`，FAQ 47 说明保存收藏时需显式启用颜色保存；现有项目调查没有取得包含有效颜色规则的正向 `.qdr` 样本。本轮不改导入器、不推断序列化字段，也未启动 GUI/Private Desktop 绘制探针。
- 策略 smoke、完整 Release SettingsSmoke、NativeProbe Release build、App Release build 通过；原型解析不等于真实背景绘制或 `.qdr` 互操作。产品 ExplorerBrowser、用户配置和 release 未改变。

## 2026-10-26：补齐隔离颜色原型的逐属性优先级模型

- Q-Dir 官方 FAQ 47 和旧版帮助页说明颜色规则按顺序执行、首条规则优先；文字、背景、粗体、斜体、下划线、删除线分别使用 `-1/0/+1` 状态。隔离原型现为六类属性保存独立状态，并用同一 evaluator 逐项解析；NativeProbe 像素夹具仅消费背景色。
- 无窗口 smoke 覆盖继续、锁定未设置、锁定已设置、不匹配，以及同一匹配集合内文字/背景/字体属性互相独立；新增 API 测试先因缺失定义按预期编译失败，再接入并通过。策略 smoke、完整 Release SettingsSmoke、NativeProbe Release build、App Release build 均通过，0 警告/错误。
- Q-Dir 官方页面未提供可核实的颜色配置行格式；本轮未解析配置文件，也未运行 GUI/Private Desktop 绘制探针。仅更新隔离 `IShellView` 原型，未改产品 ExplorerBrowser、用户配置、`.qdr` 导入或发布包。后续继续查公开规则格式/导入边界及在授权条件下的用户可见验证。

## 2026-10-26：发布 temp.57 隔离预览包

- 按此前预览请求使用 `scripts/publish.ps1 -Version preview-0.6.0-temp.57 -IsolatedProfile`；Release `SettingsSmoke`、App Release 构建（0 警告/错误）及 `publish-smoke.ps1` 均通过。发布脚本拒绝覆盖已有目录，并将包放入 `releases/preview-0.6.0-temp.57/`，profile 独立于 `src/`。
- 产品版本 `0.6.0-preview.temp.57+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE/DLL SHA-256 与 `app/SHA256.txt` 一致。包内包含第五十七版说明。没有启动新 EXE；先前运行的 temp.2 未操作，文件操作、焦点、右键、布局、主题及 Q-Dir 互操作仍待桌面验收。

## 2026-10-26：细分空白背景与列表行尾的右键验收

- 复核当前 Shell 菜单路径：鼠标命中未知时会省略依赖文件目标的自定义命令，并保留 Shell 原生菜单；菜单构造期间不以残留选区替代命中目标。源码无法决定 DirectUI 列表把可见行尾区域归为文件项还是背景，需观察实际 Shell 标准菜单才能判定用户所称“空白处”。
- GUI smoke 的手动验收现分别要求右键最后一行下方的空白区域与可见行右侧尾部，并启用 focus trace 记录坐标和 UIA 控件类型；研究文档注明不能预先把行尾认定为背景。`--shell-menu-target-smoke` 通过；准备副本 `src/.state/gui-smoke/20261025-212748-589-a0f780/` 状态为 `prepared-not-run`，EXE 哈希一致。
- 没有启动窗口或注入输入。真实 DirectUI 命中范围、Shell 标准菜单类型及自定义命令目标仍待可见验收。

## 2026-10-26：准备含程序启动器导入场景的四窗格验收副本

- `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功发布当前源码到 `src/.state/gui-smoke/20261025-212415-054-82f111/`；manifest 状态为 `prepared-not-run`，EXE SHA-256 与清单相符，四个窗格各有隔离操作夹具，递归汇总夹具有 10,000 个文件。
- 新增隔离 `qdir-program-items.ini` 样本，含一个带参数/工作目录项和一个无参数项，两个程序路径都不存在；样本 SHA-256 已写入 manifest。验收清单现在要求核对启动器持久化、导入预览、取消不写入、显式应用只更新 profile `ProgramStarter.ini`、源文件不变且不启动导入程序。Q-Dir importer smoke 另以完全相同的两条行文本核对名称、路径、参数和工作目录；完整 Release SettingsSmoke 与样本清单/哈希检查通过。
- 只执行构建与夹具准备，没有启动副本或发送输入。只读进程核对发现既有 `releases/preview-0.6.0-temp.2/app/ExplorerDirs.App.exe` 仍在运行；本轮未操作该进程、未改其 profile，也未生成 release。真实文件操作、焦点、菜单、标签/布局、主题及程序启动器可见交互继续等待桌面交接。

## 2026-10-26：复验普通 ListView 与 Shell 颜色绘制基线

- `--common-listview-color-baseline-private-desktop` 在独立 Windows Desktop 的普通 `SysListView32` 上覆盖 Icon、Details、SmallIcon、List、Tile、Content 六种视图；每种都收到逐项绘制回调，标签区像素等于各自行颜色。watchdog 退出码 0，主桌面前台未变。
- `color-private-desktop` 使用 stock `IShellView` 重跑退出码 0。普通名称通配规则在 Icon、Details、SmallIcon、List、Tile 五种原生模式，以及 Name 降序/Size 分组组合中均命中两项身份对应的像素；菜单、选择、复制/重命名/删除 fixture 检查收尾通过。
- 基线日志：`20261026-051237-da85e1e0.stdout.log`（基线）、`20261026-051416-998f4a03.stdout.log`（普通 Shell）；前台守卫均为 true。`#RO` 后续正向复验见下条。

## 2026-10-26：修正隔离 `#RO` 颜色探针顺序并通过像素验证

- 旧只读试验在 Shell view 创建后修改属性并刷新，产生过“回调收到颜色但像素不匹配”以及刷新后无逐项回调的结果，无法据此判断 `#RO` 能力。
- 新增独立 `color-readonly-rule-private-desktop` 诊断路径：临时夹具创建后、Shell view 枚举前设置首项只读；只读项由 `#RO` 匹配，另一项由名称通配匹配；完成颜色读取后立即退出，跳过复制、改名、删除等后续文件操作。Icon、Details、SmallIcon、List、Tile 五种视图及 Name 降序/Size 分组状态下，两个身份对应的像素断言均通过。watchdog 连续两次退出码 0，前台守卫通过；夹具 finally 清理先移除只读属性。
- 这两次受控样本证实 Private Desktop stock `IShellView` 原型中的真实只读属性→`#RO` 规则→像素路径。旧/新测试同时改变属性设置时机与 Refresh 行为，仍不足以归因初次失败的唯一根因；结果也不代表 ExplorerBrowser 产品视图或可见用户 UI 已集成。产品和发布物未改动。
- 日志：`20261026-051538-ec567c71.stdout.log`、`20261026-051639-c1344976.stdout.log`。

## 2026-10-26：收窄隔离 `#RO` 像素结论

- `ShellViewColorRuleMatcher` 的 `FileAttributes` overload 将只读文件系统属性映射到 Q-Dir `#RO` 规则；纯策略 smoke 覆盖通过。普通文件规则的原有 Private Desktop 五种视图与排序/分组像素样本仍有效。
- 初次只读 fixture 像素尝试未通过：颜色映射及自绘回调收到预期 COLORREF，但目标像素未命中；调用 `IShellView.Refresh` 后无逐项绘制通知。当时先移除了不稳定的像素断言，保留为未决诊断。
- 后续在 Shell view 枚举前设置只读属性的专用 Private Desktop 探针已通过真实像素断言，见上方“修正隔离 `#RO` 颜色探针顺序并通过像素验证”。因此初次失败不再代表当前 `#RO` 能力结论；旧/新流程有多个时序差异，唯一根因仍未定。NativeProbe build、颜色策略 smoke 和完整 `ExplorerDirs.SettingsSmoke` 均通过，产品 `ExplorerBrowser` 与发布物未改动。

## 2026-10-26：隔离颜色原型识别 Q-Dir 文件规则标记

- Q-Dir FAQ 6 公布 `*.ext` 通配符、分号组合，以及 `#` 隐藏项和 `#DIR` 文件夹标记；官网英文功能清单另注 `#RO` 为只读项。NativeProbe 的纯逻辑策略现按这些公开规则匹配名称/项目类别；像素探针使用通配规则，再以 Shell PIDL 解析出的当前文件身份映射到绘制索引，不在绘制回调里调用 Shell COM。
- 策略 smoke 的只读标记断言先因接口缺失而编译失败，接入后覆盖不区分大小写、多扩展分号列表、`*`、目录/隐藏/只读标记及空段并通过。NativeProbe Release build 0 警告/错误；常规蓝/绿通配规则的 Private Desktop 颜色像素探针退出码 0，五种标准列表绘制模式、排序/分组后身份着色及选中项原生视觉保留检查通过。该轮没有验证真实只读属性驱动的 `#RO` 着色；相关差异空白检查通过。
- 仅改变 `ExplorerDirs.NativeProbe` 原型，不接入产品 ExplorerBrowser 或用户配置。多条冲突规则优先级、真实 Shell 只读属性到策略的端到端映射和具体颜色配置持久格式尚未完成；虚拟/第三方 Shell provider、Content/自定义布局、悬停/焦点及可见产品 UI 仍未验证。来源：[Q-Dir FAQ 6](https://www.softwareok.com/?faq=6&seite=faq-Q-DIR)、[Q-Dir 英文功能清单](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)。

## 2026-10-26：反向顺序 50,000 项复测暴露高追踪负载长尾

- 按 native-only → product 顺序复测。native-only 轮中磁盘建立 50,000 个文件，但 15 秒创建收敛窗内 Shell 视图持续为 3 项、产品 watcher 为 `0/0`，独立 observer 为 `49,999/50,000`、errors `0/0`，进程退出码 1；它未达到可和 product 比较的视图前提。此前计划的同负载子文件夹周期未执行。HostProbe 现仅在目标 burst 和单文件删除均已在视图收敛时才尝试该周期，未收敛则显式跳过。
- 紧接 product 轮中 observer `50,000/50,000`、产品 watcher `50,001/50,001`，选区/焦点最终读回一致；子文件夹创建/删除分别约 1,467/1,558 ms 自然可见，3,026 ms settled，身份扫描结果 `not-attempted`、folder fallback 0。压力场景整体退出码 1：文件写入约 66.2 秒（超过探针 60 秒 writer gate），批量删除约 276.3 秒；`create-visible/delete-visible` gate 未通过，虽然最终 Shell/磁盘计数为 `3/3`。
- 该 product 轮同时开了 Dispatcher queue 与消息泵/Shell trace，观测到 ContextIdle/Input/Normal 最大间隔约 40.1/241.4/216.4 秒，独立 queue 最大等待约 368.2/241.3/217.6 秒；专用心跳最大间隔 89 ms，Shell `0x4B0` 单次最高约 764 ms，刷新调用最高约 1.86 秒。记录未能把约 215.7 秒消息泵最大间隔定位到单个已采样回调；队列探针也没有全部 drain。此单轮不代表普通使用响应时长或已证明的产品根因。
- HostProbe Release build 与 message-pump-hook smoke 通过，0 警告/错误。反向顺序样本说明 native-only 视图没有收敛时不能构成性能对照；下一步先在不启用 Dispatcher queue-latency probe 的低扰动产品轮中复核同一创建/删除门槛，再判断是否需要更细的 STA 阶段计时。没有改生产刷新策略、没有可见 GUI/输入或发布；自动刷新 P0 仍开放。日志：`%TEMP%\explorer-dirs-p0-50k-native-first-equivalent-20261026.log`、`%TEMP%\explorer-dirs-p0-50k-product-after-native-20261026.log`。

## 2026-10-26：配对追踪 50,000 项产品监视与 native-only 的 STA 间隔

- 使用同一消息泵、Dispatcher queue-latency 和 Shell callback trace 配置顺序运行隐藏 50,000 项创建/删除场景。产品轮退出码 0，observer / 产品 watcher 分别 `50,000/50,000`、`50,001/50,001`，视图/磁盘收敛 `50003/3`，两项选择和焦点恢复；子文件夹删除身份扫描跳过，fallback 后约 934 ms settled。最大 ContextIdle/Input/Normal 间隔为 301/220/220 ms，独立队列最大等待 142/143/153 ms；同一最大空档期间 Background 操作重叠约 73 ms、单项最高约 62 ms，`SHELLDLL_DefView 0x4B0` 调用占约 153 ms。
- 随后 native-only 控制退出码 0，observer `50,000/50,000`、产品 watcher 为 `0/0`，Shell/磁盘收敛 `50003/3`；其最大 ContextIdle/Input/Normal 间隔为 223/223/238 ms，独立队列等待最高 69 ms，Background 操作重叠不到 1 ms、heartbeat 最大间隔 50 ms，消息泵追踪没有记录 `0x4B0` 慢调用。native-only 不验证产品选择恢复，不能比较该项。
- 该顺序配对提示产品轮存在更高的队列等待，但仅一组、执行顺序固定，且产品轮包含额外子文件夹周期；不能归因唯一根因或认定产品刷新策略导致 0x4B0。两轮无可见 GUI/输入；不更改生产调度，自动刷新 P0 仍开放。完整日志在 `%TEMP%\explorer-dirs-p0-50k-aligned-trace-20261026.log` 与 `%TEMP%\explorer-dirs-p0-50k-native-aligned-trace-20261026.log`。

## 2026-10-26：在生产 Shell 视图验证 RMDIR 512 项切换边界

- 扩展隐藏 HostProbe，可在删除子文件夹时把 pane 0 的陈旧视图精确设为 512 或 513 项。512 项样本在删除时仍保留 stale folder，产品发出 1 次 `RMDIR`（`Notified`），随后 fallback 增加 1 并将计数收敛到 511；最初探针因沿用 3 项最终基线而误判失败，修正目标计数后重跑通过。
- 513 项样本的文件夹 watcher 到达后，Shell 在约 157 ms 内先自行移除了项目，早于产品 250 ms 身份查询，因此产品结果为 `not-attempted`、fallback 为 0；该结果验证的是自然 Shell 更新路径，不能声称生产 gate 实际执行了 `SkippedLargeView`。513 的 gate 分支仍由 SettingsSmoke 边界断言覆盖，50,000 项产品场景另有真实 `SkippedLargeView` 证据。
- 再次复跑 512 项时，Shell 也在约 142 ms 自然移除项目，产品结果为 `not-attempted`、fallback 0。探针现接受产品 `Notified` 或 250 ms 前自然收敛这两条有效路径；512 项实际命中 `Notified` 已有通过样本，但不保证每轮 watcher 都能取得 stale PIDL。
- HostProbe Release 构建 0 警告/错误；512 与 513 两个隐藏生产宿主场景均退出码 0，未操作可见 GUI/输入、未发布。此结果确认阈值两侧的当前产品路径可收敛，不覆盖其它 Shell provider 或可见交互。

## 2026-10-26：50,000 项视图的子文件夹删除 fallback 验证

- 在 50,000 个 burst 文件的隐藏生产宿主中额外创建并删除一个空子文件夹；子文件夹创建进入 Shell 视图后再删除。删除 watcher 事件到达，当前视图大于 512 项，身份扫描跳过，计数 fallback 增加 1；约 366 ms 后视图计数回到删除前值，monitor settled，测试断言全部通过。最终四 pane 计数 `50003/3`，选区/焦点恢复。
- 产品 watcher 总事件 `50,001/50,001`（含子文件夹 create/delete）；独立 burst 文件 observer 本轮 create 少一项 `49,999/50,000`、delete `50,000/50,000`、errors `0`，故 observer 侧仍有一次未解释的单项差异，不代表产品 watcher 漏事件。Dispatcher 最大 ContextIdle 空档 1,087 ms；单轮数据不足以归因该空档，自动刷新 P0 仍开放。
- 小视图原生 RMDIR 路径此前仍通过；本轮无可见窗口/输入和 release，GUI 验收仍未完成。

## 2026-10-26：重复复验 50,000 项子文件夹删除与独立 observer

- 同一隐藏生产 HostProbe 场景再次通过，子文件夹删除触发大视图 `SkippedLargeView`，身份扫描计数增量 0，fallback 增量 1；约 942 ms 后视图计数/monitor settled 断言通过，最终四 pane `50003/3`，选区和焦点恢复。完整 Release SettingsSmoke 与 `--message-pump-hook-smoke` 均通过。
- 独立 burst observer 本次 create/delete 都为 `50,000/50,000`、errors `0/0`；产品 watcher 含测试子文件夹为 `50,001/50,001`。上一轮 observer create 少一项未复现，仍作为单次未解释差异保留。ContextIdle 最大空档 433 ms、Input/Normal 215 ms，不能证明单轮前值的因果来源。
- 两轮定向文件夹删除均通过，但只代表本机隐藏 ExplorerBrowser 宿主；不覆盖可见 GUI、实体输入、其它 Shell provider 或跨机器稳定性。自动刷新 P0 和 GUI 验收继续开放，未发布。

## 2026-10-26：限制大视图的删除身份全量扫描

- 分阶段计时证实此前 `QueueDeletedItemNotification` 最高约 262 ms 的耗时来自在线性 Shell 视图扫描中匹配删除路径：隐藏 50,000 项运行的匹配项位于返回项目序号 9,874，随后属性查询判为 `NotFolder`。逐项项目取得和显示名调用累计了主要成本。
- 新增 `CanScanViewForDeletedFolder` 限制：当前视图项数最多 512 时才执行即时 RMDIR 身份扫描；大视图记录 `SkippedLargeView` 并让既有目录计数刷新收敛。SettingsSmoke 边界测试先因缺少 gate 而按预期编译失败，接入后通过。
- 50,000 项隐藏生产 HostProbe 退出码 0：创建/删除 watcher 均 `50,000/50,000`，最终计数 `50003/3`、选区/焦点恢复，fallback 1/3；`directory-delete-identity-scan count=0 last-result=SkippedLargeView`，删除通知同步扫描耗时 0 次，最大 Dispatcher 间隔 354 ms。小视图 `--live-shell-product-monitor-early-rmdir` 仍收到产品原生 RMDIR 通知并最终收敛，fallback 1 次。
- 初轮大目录压力删除的是普通文件；后续已新增 50,000 项视图内子文件夹删除的定向验证，见路线图最新记录及调查详记。隐藏测试不等于可见交互验收；自动刷新 P0 继续开放，未发布。

## 2026-10-26：低频采样且关闭独立队列探针复跑 50,000 项

- 仅启用轻量 `ThreadMessagePumpTraceProbe`，不启用 `DispatcherQueueLatencyProbe`，隐藏生产 HostProbe `--live-shell-directory-stress-large 50000` 退出码 0。observer 与产品 watcher 创建/删除均为 `50,000/50,000`，observer 错误 `0/0`；创建/删除约 28.7/19.1 秒，最终 pane 计数 `50003/3`，选区/焦点恢复，fallback 1/4。
- 最大 ContextIdle 间隔 404 ms，Input/Normal 最大间隔 217/216 ms；heartbeat 最大间隔 223 ms、单次工作 20 ms；Shell selection/focus 查询峰值 9/11 ms。与同一低频采样且开启 queue probe 的 323 ms 单轮结果接近；两者样本量不足以判定探针对队列延迟的因果影响。
- 最大空档所在窗口里，WPF `0xC471` Dispatcher WndProc 与 `QueueDeletedItemNotification` timer tick 各约 188 ms；三次删除身份检查最后结果为 `NotFolder`，删除通知最大单次耗时 187 ms。该记录当时只有整体时长；后续分段计时定位到大视图的线性项目身份扫描，并已由 512 项门控跳过，详见[限制大视图的删除身份全量扫描](#2026-10-26限制大视图的删除身份全量扫描)。
- 该轮验证低频状态轮询下不启用独立队列探针也能完整收敛；未复现早先的长间隔或 observer 漏事件，但不能据此认定此前单轮异常已解释。无可见 UI、用户输入或发布；自动刷新 P0 继续开放。

## 2026-10-26：降低压力探针自身对 Dispatcher 的干扰并复跑 50,000 项

- 对前次 50,000 项 trace 增加 Dispatcher operation callback-kind 累计耗时后，发现 HostProbe 的 20 ms Background heartbeat 累计占用约 16.6 秒；其每 50 ms 同步读取 Shell 选区、焦点和视图计数，明显会干扰被测 UI 线程。此前 3.5 秒与 164 秒的异常长间隔仍保留为样本，但不能单独归因产品。
- 将压力探针 heartbeat 间隔调到 100 ms、Shell 状态采样调到 1 秒后重跑：退出码 0，observer 与产品 watcher 创建/删除均 `50,000/50,000`，创建/删除约 36.2/12.7 秒，刷新 fallback 1/4，四窗格最终计数 `50003/3`，选区/焦点均恢复；最大 Dispatcher 间隔 323 ms、独立 queue probe 最大等待约 349 ms。探针 heartbeat 回调累计耗时降为约 0.7 秒，单次最高 28.5 ms；Shell 状态读取峰值约 15 ms。
- Release HostProbe build 与 message-pump-hook smoke 通过，构建 0 警告/错误。该结果支持低侵入采样下当前高负载场景可收敛，但没有证明所有 50k 运行均稳定，也未解释之前未改采样时 observer 漏事件的单轮异常。自动刷新 P0 继续开放；无可见 GUI、真实输入或发布。

## 2026-10-26：定位 50,000 项队列长尾并验证直接选择优化边界

- 在隐藏生产 HostProbe 上同时启用 Dispatcher 队列延迟与消息泵追踪复跑 50,000 项刷新。observer 与产品 watcher 创建/删除均 `50,000/50,000`，最终计数 `3/3`，保存的两项选择和独立焦点恢复；本轮 ContextIdle 最大空档 3,555 ms，Input/Normal 队列等待峰值 953/800 ms。消息泵和专用线程仍在推进，说明不是 STA 完全停摆。
- 同一空档中最长可关联操作为 Background `ScheduleDeferredRefreshSelectionRestore` timer tick，约 381 ms；其中选区首次批量调用约 135 ms、检查/清理及再次批量调用继续耗时，`GetItemPosition` 全正仍未能一次恢复两项，之后逐项 `SelectItem` 最终在回调内恢复精确集合。该回调解释了长空档的一部分，剩余约 3 秒队列积压尚无单一根因。
- 为避免过早把逐项 `SelectItem` 当成快速路径，隔离生产快照探针尝试在空选区调用并读回；Shell 返回 `S_OK`，但没有实际选中目标，恢复结果为 false。额外旧选区下 replacement fallback 在 16 种分组/查看组合均通过。未证实的直接选择分支已撤销，既有精确集合清理与回退保持。
- Release SettingsSmoke、隐藏 `IShellView` 批量选择矩阵和 App Release build 均通过，0 警告/错误。此处的高负载和隐藏窗口结果不等于可见交互验收；此前 8.5 秒 observer 单事件差异仍未解释，自动刷新 P0 继续开放，未生成 release。

## 2026-10-26：复核最新恢复策略下的 10,000 / 50,000 项自动刷新

- 先以当前源码运行隐藏生产 HostProbe `--live-shell-directory-stress-large 50000`，队列延迟探针关闭。产品 watcher 创建/删除均为 50,000/50,000，最终 Shell/磁盘计数和选区/焦点正确；但独立 observer 删除侧为 49,999/50,000，探针退出码 1，同时 Dispatcher 最大观察间隔约 8,529 ms、独立线程心跳间隔 42 ms。该轮不能算完整通过，也未能解释长间隔。
- 为定位间隔，启用独立 `DispatcherQueueLatencyProbe` 后先跑 10,000 项：退出码 0，observer 与产品 watcher 创建/删除均 10,000/10,000，目标 pane 视图计数在创建后/最终删除后为 `10003/3`，选择及焦点均恢复；Normal/Input/ContextIdle 最大排队延迟分别 10.2/10.8/11.6 ms，Dispatcher 最大间隔 107 ms。
- 同样设置下复跑 50,000 项：退出码 0，observer 与产品 watcher 均 50,000/50,000，目标 pane 视图计数在创建后/最终删除后为 `50003/3`，选择及焦点均恢复；创建/删除用时 36.8/30.2 秒，刷新 fallback 分别 1/4 次。Normal/Input/ContextIdle 最大排队延迟为 134.8/134.9/218.6 ms，最大 Dispatcher 间隔 279 ms，独立线程心跳最大间隔 53 ms。
- 队列追踪轮未复现此前 8.5 秒间隔；首轮 observer 少一个事件和 8.5 秒间隔仍无根因，不能因复跑通过而抹去。此次是单机隐藏生产宿主样本，未操作可见桌面；没有证明网络/离线目录、shell provider 差异或真实交互体验。自动刷新 P0 保持开放，未发布。

## 2026-10-26：复核 UNC 目标路径的 Shell 操作收敛

- 在执行前只读确认专用 fixtures 父目录可经 `\\localhost\D$` 访问；Release HostProbe 设置进程级 `EXPLORER_DIRS_HOSTPROBE_UNC_TARGET=1` 运行 `--live-shell-cross-volume`，退出码 0。D: 本地源与 loopback SMB UNC 目标之间的 Shell 复制、目标清理和移动均成功；源/目标 watcher 事件、文件内容、源文件移除及四窗格最终计数 `2/4/3/3` 均通过。
- 本轮 fallback 计数为 `1/2/0/0`，目标仍不能仅靠原生变化通知保证及时显示，保留计数兜底是必要的。fixture 的 `unc-<guid>` 目录运行后未残留。
- 该 UNC 指向本机 D: 管理共享，底层仍是本地卷；它只验证 UNC 路径/SMB 提供程序的本机处理，不代表远程服务器、真实网络延迟或断线恢复。没有可见窗口或用户数据操作，未修改产品代码/发布包，自动刷新 P0 继续开放。

## 2026-10-26：复核跨固定卷 Shell 复制与移动收敛

- 当前 HostProbe Release 隔离运行 `--live-shell-cross-volume` 退出码 0，源目录在 `D:`、目标在 `C:`。真实 Shell `IFileOperation` 复制后源 watcher 未误报创建、目标创建 watcher 到达、文件内容正确；清理复制夹具后目标目录回到基线；跨卷移动分别观察到源删除和目标创建，文件内容匹配且源路径已消失。
- 最终四窗格 Shell 计数为 `2/4/3/3`，源 pane 删除项、目标 pane 新文件均可见；目标窗格累计触发 2 次计数 fallback，其他窗格 0 次。证明这轮在 fallback 参与下正确收敛，不证明自然 Shell 通知能及时完成，也不覆盖慢速/网络卷、物理拖放、剪贴板或可见 GUI。
- 探针只操作生成的 D:/C: 临时 fixture，父窗口保持隐藏；未执行日常用户文件操作、未修改产品策略、未发布。自动刷新 P0 继续开放。

## 2026-10-26：复跑 3,000 项隐藏生产宿主自动刷新压力

- HostProbe Release 构建通过（0 警告、0 错误）；`--live-shell-directory-stress` 在隐藏四窗格生产 ExplorerBrowser 宿主中创建并删除各 3,000 个隔离文件。产品 watcher 与独立 observer 均收到创建/删除 `3000/3000`，observer 错误为 `0/0`；最终目标/其余窗格计数为 `3/3`，两项既有选择及未选中的 `initial.txt` 焦点在操作后恢复，最终 refresh completion 也恢复正确。
- 本轮创建和删除均经历计数 fallback（共 3 次），说明原生 Shell 更新仍不能独自保证计数及时收敛；fallback 后视图与选择/焦点正确。创建/删除用时约 2.07/2.16 秒；Dispatcher 最大 Normal/Input/ContextIdle 间隔为 78/80/110 ms，心跳最大 77 ms；样本运行约 16.6 秒。
- 这是单轮本机隐藏宿主证据，无逐消息插桩、没有可见窗口或真实输入；不能证明跨机器/网络目录响应或解决此前观察到的秒级 STA 长尾。自动刷新 P0 继续开放，未更改产品代码、未发布。

## 2026-10-26：可靠性配置回归门复核

- 复核 `CrashLog` 的异常字段脱敏、AggregateException 有界收集、临时文件失败清理，以及 `RunMarkerTracker` 的 PID/启动时间身份核对；对应 SettingsSmoke 已覆盖这些行为，本轮源码没有发现可据证据修复的新缺陷。
- `dotnet run --project src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj -c Release` 通过（退出码 0，末尾 `Settings smoke: PASS`），覆盖配置、会话、快捷键、Shell 恢复门、Program Starter、主题偏好和状态设置等纯状态/隔离回归。它不验证真实 UI、Shell 扩展崩溃归因、异常退出时最后一刻会话写入或跨重启实际恢复；自动刷新及可见操作 P0 仍开放。

## 2026-10-26：当前四窗格生产宿主 F2 方向键复验

- 重建 HostProbe Release（0 警告、0 错误），对四个 pane index 分别运行 `--live-shell-rename-editor-pane-0` 至 `-pane-3`。四次都从真实 Shell 项目进入原生重命名框；Left/Right 移动文本光标，Shift+Left/Right 选择名称内字符，四方向键处理后编辑框仍持有焦点，其他窗格不认领焦点；四个探针均退出码 0。
- 这是当前生产 `TranslateAcceleratorCore` 在隐藏四窗格 ExplorerBrowser 宿主中的合成按键消息证据，确认现有箭头转发逻辑逐窗格成立。它没有验证物理 F2 的消息进入路径、可见多布局窗口或用户实际按键，因此不关闭用户报告的 GUI 验收项；未启动产品窗口、未发送真实键盘输入、未生成 release。

## 2026-10-26：复核隔离 `IShellView` 颜色原型的隐藏宿主边界

- 当前 Release NativeProbe 构建通过（0 警告、0 错误）；运行 `--ishellview-color-spike`，不启动 ExplorerBrowser、不显示窗口、不发送输入，夹具位于 NativeProbe 输出目录。
- 本轮在隐藏默认 `IShellView` 中读到八种 Shell 视图模式、背景/项目原生菜单核心命令、`IDropTarget` 与精确 PIDL 选区恢复；Shell 菜单查询约 142–328 ms。合成 `NM_CUSTOMDRAW` 契约通过，但实际预绘制/项目通知的 HDC 区域为空，所有查看方式的目标像素为 0，排序/分组下也没有有效项目绘制回调。复制、重命名、删除的隔离夹具最终计数为 `2→3→3→2`，本轮自动更新与显式刷新路径均收敛。
- 这轮只说明隐藏宿主不产生可用颜色像素，不能否定 10 月 25 日 Private Desktop 上五种标准模式的真实像素通过；两者窗口/绘制条件不同。也不证明产品 ExplorerBrowser 可迁移到 `IShellView`，不测量用户桌面体验。结论仍是：原型在隔离默认 `IShellView` 可行，向产品宿主迁移前须逐项评估导航、筛选、状态、生命周期和逐标签视图契约。详见[颜色原型记录](ishellview-color-spike.md)。

## 2026-10-26：准备最新四窗格交互验收副本

- 运行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 发布最新源码到 `src/.state/gui-smoke/20261025-191415-863-081792/`，生成隔离 profile 与四窗格操作夹具；没有启动该 EXE、发送输入或改动剪贴板。
- 清单状态为 `prepared-not-run`；EXE/DLL 的清单 SHA-256 与文件实算一致，四窗格夹具为 8/5/5/5 个普通文件，单独进度夹具含 10,000 个零字节文件。只读进程核对发现的 ExplorerDirs.App 是既有 `releases/preview-0.6.0-temp.2` 路径；未操作该进程。
- 下次经桌面交接后可验收粘贴/删除后方向键、F2 四方向编辑、F10 菜单、空白处 Shell 右键、跨窗格复制/移动、Tab/Shift+Tab、地址历史/F4、Ctrl+滚轮和状态栏格式。此次没有 GUI 行为证据。

## 2026-10-26：同步 Q-Dir 12.82 地址栏目录树基线

- 复核 SoftwareOK 官方记录：官网现列 12.82（2026-10-24），本机留存的 Q-Dir 二进制仍是此前核实的 12.79。新版说明提到改进 Explorer 地址栏目录树；FAQ 116 给出现代样式 F4 第一次展开树、第二次编辑路径的已知语义。
- 本项目对应能力已实现为每窗格 Shell 树 Popup 与两阶段 F4；这次只校正文档基线并明确新版本说明没有公开更细行为。可见 Popup/焦点与 12.82 实际交互仍待桌面验收，未操作 GUI。
- 顺带修正 `.qdr` 技术记录中仍把 12.81 称为当前官网版本的旧句；12.82 版本记录未增加 `TreeNsplit` 单位/方向说明，故继续不猜测导入宽度。

## 2026-10-26：隔离环境设置消息与 Dispatcher 关闭竞态

- `WM_SETTINGCHANGE("Environment")` 仍只触发本进程注册表快照刷新；但消息在窗口关闭期间到达时，`Dispatcher.BeginInvoke` 可能与关闭并发并从原生窗口回调抛出 `InvalidOperationException`。
- 钩子现在先检查 Dispatcher 关闭状态，再通过窄异常门投递刷新；仅收敛 `InvalidOperationException`，刷新本身仍由原有状态栏错误处理。测试先因调度门缺失而失败，接入后环境消息定向 smoke 通过。
- 完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。没有运行可见 GUI；真实系统广播并发关闭仍待桌面生命周期验收。

## 2026-10-26：验证刷新退避状态的并发推进

- 为监视器恢复处理器失败后的线程池重试补充并发回归：32 个并发失败必须唯一领取前六级退避，其余统一保持 30 秒上限。此测试验证了跨 Dispatcher/线程池共享的失败计数由锁串行化，不重复档位或越界。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 GUI 或 Shell 宿主。

## 2026-10-26：监视器恢复处理器失败后按退避重新排队

- 上一项发现主刷新 Tick 的错误处理器若因计时器状态更新/重启再次失败，虽然二次异常已隔离，计时器仍可能停在已停止状态；直接同步重试也无法处理 Dispatcher 瞬态排队异常。
- 恢复逻辑现在先清除刷新中状态并保留强制刷新标记，再尝试重启计时器。若本地重启失败，则在线程池等待当前退避间隔后重新排入 Dispatcher；执行/排队仍失败时沿同一个线程安全退避状态继续递增。异步重试完成前会在 Dispatcher 上复核导航、路径、batch 和 timer 身份，过期任务不修改新监视器。
- 新增恢复回调二次异常、延迟重试执行和重试调度器异常回归；先因缺少重试 API 失败，接入后通过。完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；真实 timer/Dispatcher 失败与实际刷新恢复仍待可见宿主验收。

## 2026-10-26：隔离目录监视器恢复处理器二次异常

- 主目录刷新 timer 已将 Tick 错误交给 `RecoverRefreshFailure`，但若该恢复处理器在计时器状态更新/重启时再次抛错，原 `TryRunMonitorTick` 会把第二个异常继续抛回 Dispatcher。
- 现在二次恢复异常也会被捕获并记录不含路径的 HRESULT；新增回归先按预期失败，再通过。这个改动只证明 Dispatcher 异常已收敛，不证明计时器恢复失败后监视器仍会自动重启。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI。下一项仍是补齐“恢复处理器自身失败后重新调度监视器”的退避闭环。

## 2026-10-26：通知静默期 timer 异常转为整目录补偿刷新

- 创建/删除/修改 watcher 通知的静默期 DispatcherTimer 会在 Tick 开始时停表，再读取 Shell 计数或发送原生通知；此前这些动作没有异常边界，失败可能成为 Dispatcher 未处理异常，并留下过期 timer 引用。
- 新增通知 timer 恢复门。任一 Tick 失败时清理对应 timer 引用、记录不含文件路径的 HRESULT，并通过同一 watcher 队列排入一次强制目录刷新；若补偿回调自身失败，也不会逃出 Dispatcher。测试先因恢复门缺失而失败，再接线并通过。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；Shell 通知异常及产品宿主中的补偿刷新仍待可见验收。

## 2026-10-26：隔离 FileSystemWatcher 事件回调异常

- watcher 事件门此前只验证事件是否来自当前监视器，回调中的队列投递或通知准备一旦抛错，会从 `FileSystemWatcher` 线程池回调逸出。现在活动 watcher 的事件门捕获回调异常，记录不包含路径的 HRESULT，并用既有退避机制重新安排 watcher 恢复；恢复观察器自身异常也不会逸出。
- 定向回归覆盖回调错误报告、恢复观察器抛错与普通/过期事件路径。新增测试先因缺少异常处理重载而编译失败，接入后 watcher recovery smoke 通过。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；真实 FileSystemWatcher 异常时序和产品宿主恢复仍待验收。

## 2026-10-26：timer 清理抛错前先结算 watcher recovery 状态

- 复核上轮回调保护发现，`RecoverScheduledWatcherAttempt` 先调用 `DispatcherTimer.Stop()`，之后才释放 pending；若停止 timer 抛错，外层虽然会吞住异常保护 Dispatcher，但该 recovery 仍永久 pending。
- 新增 `CompleteFailedAttempt`：先按失败推进退避并清除 pending，再运行受保护的最佳努力清理。宿主用 `finally` 清空当前 timer 引用，即使 `Stop()` 抛错也能继续记录并安排下一次重试。回归先因方法缺失失败，接线后 watcher recovery 定向 smoke 通过。
- watcher recovery 定向 smoke 与完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；真实 Dispatcher timer 释放失败仍待宿主验证。

## 2026-10-26：瞬态 Dispatcher 排队失败后重新安排 watcher 恢复

- 上一轮确认 `BeginInvoke` 同步失败时释放 pending，但仅等待未来 watcher 错误再次触发；如果当前 watcher 只发送一次 Error 事件且 Dispatcher 尚未关闭，恢复会永久停在已发布的失效 watcher 上。
- 现在该失败先结算当前尝试；Dispatcher 仍可用时，由线程池按本次退避间隔延迟重试排队，下一轮继续使用同一恢复策略计算更长退避。Dispatcher 已关闭则不安排 UI 工作。新增回归先因重试调度 API 缺失失败，接入后证明恢复可重新进入 500 ms 退避状态。
- watcher recovery 定向 smoke 和完整 Release `SettingsSmoke` 均通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；真实 Dispatcher transient/shutdown 行为仍待产品宿主验收。

## 2026-10-26：观察并收敛 watcher 的后台释放任务异常

- 复核发现 watcher 重建及导航停止时直接丢弃 `Task.Run(DisposeDirectoryChangeWatcher)`；`EnableRaisingEvents=false` 或最终 `Dispose` 出错会产生未观察任务异常。
- 新增 `ShellDirectoryWatcherStartup.DisposeSafelyAsync`，后台释放任务自行观察并收敛所有释放异常；两处产品生命周期入口改为共用该路径。回归先因 helper 缺失失败，接入后 watcher startup 定向 smoke 通过。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。没有运行 ExplorerBrowser、HostProbe 或 GUI；释放失败的实际 provider 行为仍待产品宿主验收。

## 2026-10-26：watcher 恢复 timer 异常释放 pending 并重试

- 复核发现恢复策略在 `TryBegin` 后将工作排入 Dispatcher，但排队成功后的回调没有异常边界；timer 构造/接线/启动或 Tick 内启动 watcher 抛错时，`_pending` 可能永远保持，后续错误都无法再开始恢复。
- 新增 `ShellDirectoryWatcherRecovery.TryRunCallback` 包住 Dispatcher 回调和 timer Tick。失败后停止并清除仍属当前的 timer，结算 pending，复核监视器身份后记录错误并按下一级退避重试；监视器已过期时只结算状态。`BeginInvoke` 直接失败时释放 pending，等待后续 watcher 错误重新尝试。回归覆盖普通异常、报告器异常与下一级退避。
- watcher 恢复定向 smoke 通过；完整 Release `SettingsSmoke` 在接入期间通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。最后的回归补充后再次通过定向 smoke。未运行 ExplorerBrowser、HostProbe 或 GUI；真实 Dispatcher 失败与 watcher 重建仍待产品宿主验收，自动刷新 P0 继续开放。

## 2026-10-26：收敛 watcher async void 完成回调异常

- watcher 创建/激活任务结束后，`StartDirectoryChangeWatcher` 原先直接从 `async void` 调用请求方完成回调；初次采样定时器创建、恢复调度或排入强制刷新等完成动作抛错时，会成为 Dispatcher 未处理异常，恢复中的 pending 状态也可能滞留。
- 新增 `TryNotifyCompleted` 捕获完成回调及错误观察器自身异常。当前请求回调失败时记录诊断、结算失败尝试并以当前 watcher 身份重新排入恢复；已退休请求不写状态或重试。新增两条 smoke 覆盖回调异常与报告器异常；回归先因缺失接口失败，接入后通过。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；异步宿主中的真实回调异常和恢复 timer 仍待产品验收，自动刷新 P0 继续开放。

## 2026-10-26：watcher 启动回滚清理异常不再穿透 async void

- 复核 `StartDirectoryChangeWatcher` 的 async void 调用链发现，候选 watcher 发布回调若部分修改宿主状态后抛错，原启动 helper 会把异常继续抛给 Dispatcher，且没有撤销或释放候选；激活失败后的 unpublish/dispose 清理异常也可能再次外逸。
- `ShellDirectoryWatcherStartup.StartAsync` 现在把身份核验、发布、激活和最终核验纳入失败收敛；一旦尝试发布就会尝试撤销，撤销失败仍继续异步释放候选。清理失败不再从 async void 启动链逸出，统一以未启动返回供既有恢复重试处理。新增发布半失败回滚与清理异常回归，先复现未处理异常，再接入修复。
- watcher 启动定向 smoke、完整 Release `SettingsSmoke` 均通过；App Release 构建 0 警告、0 错误；`git diff --check` 通过。没有 ExplorerBrowser、HostProbe 或 GUI 验收；真实 FileSystemWatcher/Dispatcher 失败链仍待产品宿主验证，自动刷新 P0 继续开放。

## 2026-10-26：启动采样计数异常不再逃出 Dispatcher

- 自动刷新 P0 复核发现，目录监视器开始阶段的采样 timer 直接调用计数读取；普通 COM 错误会被底层转换为未知值，但封送/释放路径的其他异常仍可能逃出 Dispatcher 回调。
- `ShellDirectoryMonitorStartup.TryReadCount` 现在捕获读取异常并报告；当前请求将异常样本视为未知，重置稳定样本序列并继续采样，过期请求仍由原有请求身份检查停止。新回归先因缺少 API 失败，接入后通过。
- 完整 Release `SettingsSmoke` 退出码 0；App Release 构建 0 警告、0 错误；`git diff --check` 通过。未启动 ExplorerBrowser、HostProbe 或 GUI，因此仍需产品宿主故障路径验收；自动刷新 P0 继续开放。

## 2026-10-26：自动刷新 timer tick 异常进入恢复退避

- 源码复核发现目录变化 timer 每轮开始即停止，但失败恢复只包住 `Refresh` 调用/完成回调；若计数读取、队列结算或自然选区恢复抛出异常，异常会逃出 Dispatcher 且 timer 不再运行。
- 新增 `TryRunMonitorTick`，整段 timer tick 统一捕获并转交当前监视请求的恢复处理：保留强制刷新标记、清除刷新中状态并按既有指数退避重启 timer。过期监视请求仍由恢复身份检查拒绝；回归先因缺少 tick 异常路由 API 而失败，接入后通过。
- 完整 Release `SettingsSmoke` 退出码 0；App Release 构建 0 警告、0 错误；`git diff --check` 通过。没有运行 ExplorerBrowser、HostProbe 或 GUI；只验证异常路由与源码构建，真实 COM/Dispatcher 失败仍待产品宿主验收，自动刷新 P0 继续开放。

## 2026-10-26：为程序启动器落实 200 项容量限制

- 复核发现 `ProgramStarterStore` 有 200 项上限，但窗口仍允许继续添加或导入，超限后只能在保存时显示错误。现在上限由 store 对外提供给同一程序启动器能力；达到上限时禁用添加/导入，导入预览显示剩余名额并阻止超额应用，确认导入时再复核容量。
- 新增命令状态回归覆盖已满、导入超额和刚好可容纳三种情况；回归先因缺少容量接口而编译失败，接入后定向与完整 Release `SettingsSmoke` 均通过。App Release 构建 0 警告、0 错误，`git diff --check` 通过。
- 未启动 GUI；按钮禁用状态、导入预览可见提示和实际焦点仍待桌面验收。

## 2026-10-26：隔离刷新完成回调异常并保持当前请求续接

- 异步 Shell 刷新完成回调会读取视图计数、恢复选区并调用请求方续接；这些步骤若抛出异常，原代码只在 `finally` 释放租约，异常仍会逃出 Dispatcher 回调，且目录监视器续接可能未执行，留下“刷新中”状态。
- 新增 `ShellRefreshCompletion`：只对仍当前的请求报告完成阶段错误；即使完成动作失败，仍尝试调用该请求的续接；续接自身异常也进入同一错误报告路径。状态观察器即使自身抛错，也不会阻止续接。请求退休后不报告旧错误，也不执行旧续接。测试先因实现缺失失败，并再次捕获观察器抛错会阻断续接；修复后覆盖正常、完成异常、续接异常、观察器异常和过期请求分支。
- 完整 Release `SettingsSmoke` 退出码 0，App Release 构建 0 警告、0 错误，`git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；验证不证明真实 COM 异常行为，自动刷新 P0 仍开放。

## 2026-10-26：刷新结果沿用调用前捕获的浏览器与导航身份

- 源码复核发现 `GetCurrentView` 是可能重入导航的 Shell COM 调用，但旧 `Refresh` 在该调用返回后才读取路径和导航版本。若调用期间更换了位置或浏览器实例，旧 view 可能被错误地配上新请求身份；异步完成也会先写刷新状态/读计数，再确认视图仍属于原请求。
- 新增 `ShellRefreshRequest`，在调用前绑定浏览器实例、导航版本和路径；Shell 获取失败、刷新调用前后以及异步完成读计数前后均复核身份。过期结果不再抛出旧视图错误、排入完成回调、恢复或排入旧选区、写共享状态或触发后续操作。回归先因请求身份类型缺失失败，接线后通过；完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。
- 未运行 ExplorerBrowser、HostProbe 或 GUI。回归验证身份状态规则及源码构建，没有在真实 COM 重入时序中复现该竞态；自动刷新 P0 仍开放。

## 2026-10-26：重叠 Shell 刷新各自持有选区恢复抑制租约

- 源码复核发现 `Refresh` 在异步 Shell 刷新完成前用共享布尔值抑制选区恢复；同一窗格的第二次刷新仍未完成时，第一次完成回调会把布尔值提前清零，使后续 Shell 中间选区回调可能覆盖已确认的用户选区快照。
- 新增按导航版本分组、可幂等释放的选区恢复抑制租约。同一导航的异步刷新独立持有租约直到完成或取消；旧导航租约不再抑制新导航的 Shell 回调；同步恢复路径也使用相同状态。重叠刷新与跨导航状态回归均先因 API 不存在失败，接入后通过；完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误。
- 未运行 ExplorerBrowser、HostProbe 或 GUI。此处验证了租约生命周期与源码接线，没有在真实 COM 重入时序中复现竞态；自动刷新 P0 仍开放。

## 2026-10-26：拒绝自动刷新计数读取期间重入后的旧监视器结果

- 源码复核发现监视器启动采样已有读后身份核验，但运行中主 timer、合并创建/文件夹删除通知及异步刷新完成路径缺少完整复核。`ReadDirectoryMonitorItemCount` 可能经 COM 重入导航；过期结果可能结算旧 batch，或让旧完成回调清理新监视器的共享刷新状态。
- 新增 `ShellDirectoryMonitorCountRead.TryRead`，读取返回后复核监视器版本、导航版本、当前视图、已完成路径和 watcher 队列引用。`ShellDirectoryChangeBatch.CompleteRefresh` 现支持计数读取后身份门控；过期结果不会重设旧 batch 基线，宿主回调也会在清理全局刷新状态或重启 timer 前再次确认身份。状态 smoke 先按预期因 API 缺失失败，接入后通过。
- 补充 watcher 生命周期回归：覆盖新 watcher 已发布、启用过程中导航被替换的时序，验证候选会撤销并释放；突变移除启用后的身份复核时，该测试按预期失败。现有 startup 路径已实现此行为，本轮补足其遗漏覆盖。
- 完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；相关文件 `git diff --check` 通过。未运行 ExplorerBrowser/HostProbe 或 GUI，因此这验证了状态门控和源码接线，不代表真实 COM 重入时序已在产品宿主复现；自动刷新 P0 仍开放。

## 2026-10-26：修复 watcher 自然更新后选区漂移

- 隐藏定向产品 HostProbe 首次复现：删除一个未选中项后，目录数自然达到 watcher 预期，但 Shell 把原焦点项加入选区；正确的两项选择变为三项。缓存快照仍有效且没有新的用户输入时，产品现在在自然收敛分支按原生 Shell API 再核验并恢复选区和焦点。
- 修正反射探针传参后，`--live-shell-selection-delete-watcher` 回归通过；用户明确清空选区的 `--live-shell-selection-user-clear` 通过，没有恢复旧选区。`--live-shell-directory-stress-large 10000` 通过，watcher 创建/删除事件各 10,000，四窗格最终计数 3/3。Release `SettingsSmoke` 与 App 构建通过（0 警告、0 错误）。
- 又用 `ShellDirectoryChangeBatch.HasPendingChanges` 限定恢复仅作用于本轮实际 watcher 变化；新增状态 smoke 验证空闲、待处理及结算后的标志，避免计时器空闲轮询反复触碰焦点。门控修正后再次跑定向 watcher 探针、10,000 项压力探针和 Release `SettingsSmoke` 均通过。
- 证据来自隐藏生产 ExplorerBrowser 宿主，没有可见 GUI 验收或 release。更多文件系统 provider、真实多窗格交互与焦点体验仍待检查。详见[实时更新调查](live-update-investigation.md#2026-10-26-watcher-自然收敛时的选区漂移)。

## 2026-10-26：选区恢复策略接入后的 10,000 项产品宿主复验

- 隐藏生产 HostProbe Release 场景 `--live-shell-directory-stress-large 10000` 退出码 0：创建/删除 watcher observer 与产品 watcher 均为 10,000/10,000；变更后恢复了 `second.txt`、`third.txt` 两项选择及未选中焦点 `initial.txt`，四窗格最终计数为 3/3。创建发送 1 次 Shell create 通知，创建/删除路径仍有 fallback；128 项删除批次只做一次 stale 项身份查询并正确识别文件。
- 第一轮最大 ContextIdle 间隔约 1,059 ms，Dispatcher 心跳最大间隔约 869 ms。第二轮启用 250 ms 独立 Dispatcher 队列探针，98 个 Normal/Input/ContextIdle 操作全部完成，最大排队延迟分别约 95/484/842 ms；timer 最大回调间隔约 253 ms，无回调重叠。第三轮再启用同线程消息泵跟踪后，最大 ContextIdle 间隔 101 ms、最大排队延迟 40.9 ms；`0xC471` 中至少 10 ms 的调用有 82 次、累计约 975 ms、单次最大 35.3 ms。额外 hook 与轮间负载可能改变调度，三轮时序差异显著，不能用第三轮宣称性能改善，也未定位前两轮长队列等待的具体消息/COM 阶段。没有显示窗口或发送输入；自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-26-新选区恢复策略下的-10000-项产品宿主复验)。

## 2026-10-26：全正 readiness 下的选区恢复有界重试

- 新的隐藏 stock `IShellView` 恢复探针用四项快照恢复到 20,000 项新视图：即使 readiness 已为 `1111`，前两次恢复仍只选中 1/4，第三次在约 312–315 ms 时完整恢复。隔离宿主未收到 CDB3 状态回调，`SelectionVersion` 保持 0。
- 产品 deferred restore 现在仅在选区仍不完整、间隔至少 100 ms 且所有目标 readiness 均为正时重试；部分/未知 readiness 仍依靠状态变化和 1.5 秒最终回退。完整 Release `SettingsSmoke` 与 App/NativeProbe Release 构建通过。没有运行 ExplorerBrowser 或 GUI，产品 STA 成本、真实焦点和多窗格交互仍待验收；未发布。详见[调查记录](live-update-investigation.md#2026-10-26-readiness-全正后选区仍未完整恢复)。

## 2026-10-26：混合 readiness 下保留幸存选区

- 隐藏 stock `IShellView` 探针用四项真实快照恢复到缺少其中一项的 19,999 项新目录。readiness 稳定为 `1110` 后，首次恢复只得到 1 项；没有全正定时重试，1.55 秒最终回退后读回 3 个仍存在的目标。全正对照仍在约 343 ms 完整恢复 4 项。
- 两场景均调用生产 `ShellViewSelectionSnapshot.Restore` 与 `ShellSelectionRestoreGate`，NativeProbe Release 构建 0 警告/错误，退出码 0，父窗口隐藏且前台不变。隔离宿主仍未收到产品 ExplorerBrowser 的 CDB3 回调；未验证真实产品 timer、STA 响应、焦点或可见行为，自动刷新 P0 保持开放。详见[调查记录](live-update-investigation.md#2026-10-26-混合-readiness-下的幸存选区恢复)。

## 2026-10-26：复跑多目标 PIDL readiness 时序

- 隔离 stock `IShellView` 的 20,000 项枚举探针再次观察到 `ItemCount` 一次跃迁至 20,000，而四个分散目标仍在随后 66.6、73.4、80.0、87.3 ms 才依次可定位；stale PIDL 属性校验为 `E_FAIL`，显示名仍为 `S_OK`。Release NativeProbe 构建与探针均通过，父窗口隐藏、前台句柄未变。
- 该轮仅复核目标 readiness；后续已在隔离同类视图中按 readiness 向量变化调用生产快照恢复，并分别验证全正目标最终完整恢复、单个 stale 目标由最终回退保留幸存项。产品 ExplorerBrowser 与可见 GUI 仍待验收；详细记录见[实时更新调查](live-update-investigation.md#2026-10-26-混合-readiness-下的幸存选区恢复)。

## 2026-10-26：复核多窗格 Tab / Shift+Tab 停靠点接线

- 复查 `PaneFocusStopMap`、控件焦点识别/落点与窗口级 Tab/F6 路由：每窗格顺序包含导航控件、可见搜索/筛选控件、当前标签标题、可见目录树、原生列标题、文件视图及查看按钮；`FocusTraversal.Move` 对每步提供严格反向并在窗格边界循环。没有发现可由源码确认的新不一致，不新增行为代码。
- 现有 `FocusTraversalSmoke` 覆盖 64 种动态控件组合、列标题单独停靠、共享/逐窗格树目标、全循环无遗漏，以及 Tab/Shift+Tab、F6/Shift+F6 互逆。该覆盖是映射与算法证据，不是物理键盘或可见 ExplorerBrowser 焦点证据；控件实际停靠仍待桌面验收。

## 2026-10-26：键盘上下文菜单保留表头和滚动条来源限制

- 菜单目标 tracker 原先只在鼠标右键入口保留 `SysHeader32` / `ScrollBar` 的自定义命令抑制；键盘菜单分支调用无条件开启自定义命令的 `KeyboardMenuRequested()`，因此 Shift+F10 来源限制会被覆盖。新增状态回归先按预期失败，再验证直接键盘上下文消息及预通知路径都保留抑制，同时普通文件视图键盘菜单仍使用 Shell 选区。
- `ShellMenuTargetTracker.KeyboardMenuRequested` 现在接受来源命令策略；`ContextMenu` 键盘分支不再覆盖传入策略，`ExplorerBrowserHost.TranslateAcceleratorCore` 按当前焦点窗口类别设置预通知策略。定向菜单 smoke、完整 Release `SettingsSmoke`、App Release 构建（0 警告/错误）及 `git diff --check` 通过。
- 未运行 ExplorerBrowser 或可见 GUI；Shift+F10 在真实列标题/滚动条上的菜单呈现仍待桌面验收。该修复只限制自定义命令注入，不替换 Shell 原生菜单。

## 2026-10-26：Program Starter 支持相对工作目录

- Q-Dir FAQ 46 明确示例了相对程序路径，但没有说明第四字段工作目录的相对基准。项目设计已建议相对启动路径以应用目录为锚点；原实现却拒绝所有相对工作目录，使导入记录保留但无法启动。
- `ProgramStarterArguments.Build` 现在把显式相对工作目录按应用程序目录规范化；绝对工作目录照旧，未指定时继续使用活动文件夹，虚拟位置继续回退到应用目录。相对工作目录回归先按预期失败，修正后通过；未启动用户配置的程序。
- 参数 smoke、Q-Dir 相对路径导入至启动参数准备的 launcher smoke、完整 Release `SettingsSmoke`、App Release 构建（0 警告/错误）及相关文件 `git diff --check` 均通过；端到端 smoke 不执行夹具程序。未操作 GUI或生成 release。Q-Dir 对第四字段相对路径的具体锚点仍无官方证据，因此这里记录为本项目便携策略，不宣称与 Q-Dir 完全一致。

## 2026-10-26：Q-Dir 程序项导入拒绝含糊的额外分隔符

- 导入器会尊重引号内的等号；但多个未引用的参数分隔符无法判断是参数内容还是参数/工作目录字段错位。旧逻辑把整段静默保留为参数，可能丢掉用户原有工作目录。
- 现在这类行会在预览中标为跳过并说明歧义；明确引用的参数（例如 `"--include=*.log" "--mode=a=b"`）仍保留为独立参数。导入 smoke 先按预期失败，再通过；参数 smoke 验证引用后的 argv 边界。
- 完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误。未导入或启动用户的 Q-Dir 项，实际 Q-Dir 正向样本和窗口交互仍待验收。

## 2026-10-26：调查 Shell 文件夹属性查询能否判定失效 PIDL

- 扩展隐藏 stock `IShellView` 的 20,000 项枚举探针，并加入“创建 view 前已删除”的 PIDL 控制组。两轮中，控制 PIDL 的父 `IShellFolder.GetAttributesOf(SFGAO_VALIDATE | SFGAO_FILESYSTEM)` 都返回 `E_FAIL`；但有效目标首次查询成功时序不一致：一轮在 view count=0 即成功，另一轮要等 view count=20,000 后才成功，与 `GetItemPosition` 时间接近。
- 查询成功只能作为目标仍可由父文件夹解析的正向信号；失败仍无法区分未完成枚举与失效身份，因此不把它接入生产重试终止判断。两轮探针退出码均为 0，父窗口隐藏且前台未变化；无 ExplorerBrowser/可见 GUI 操作。详细记录见[刷新调查](live-update-investigation.md#2026-10-26-ishellfoldergetattributesof-的-readiness-对照)。
- 追加的 `GetDisplayNameOf` 对照中，有效 PIDL 直到 view count 到 20,000 后才成功；预先删除的 stale PIDL 却仍返回 `S_OK` 并可转换出显示名，故该接口也不能证明项目存在。NativeProbe Release 构建通过，0 警告、0 错误；没有生产逻辑变更。

## 2026-10-26：复跑多目标 Shell 选区 readiness 与失效回退矩阵

- 按自动刷新 P0 的后续项，在隔离 stock `IShellView` 中复跑 20,000 项枚举与生产选区快照批量恢复。`ItemCount` 单次跃迁至 20,000 后，四个分散 PIDL 于 61.7/66.2/74.0/83.7 ms 首次可定位；完整选区恢复、旧选区清理 fallback 的 16 种分组/查看方式、单目标失效后的幸存项恢复及全部目标失效后的清空均通过。两项 NativeProbe 均退出码 0，隐藏父窗口保持隐藏且前台句柄未变。
- 这轮没有发现可区分“仍在异步枚举”与“PIDL 已失效”的 Shell 信号；`GetItemPosition` 两种情况下都返回 `E_FAIL`。不据此改变生产批量/重试策略，也不宣称性能改善。产品 ExplorerBrowser、真实多窗格文件操作、可见焦点和 STA 延迟仍待后续授权验收；无 release。

## 2026-10-26：确认 Program Starter 的配置归属与复核门禁

- 用户确认 Program Starter 使用本项目 profile 内独立的 `ProgramStarter.ini`，并支持导入 Q-Dir `[Program]` 启动项；继续保留导入预览与显式应用流程，不修改 Q-Dir.ini。源码已由 `MainWindow` 将 store 指向 profile 下该文件，导入解析器仅生成预览数据。
- 本轮完整 Release `SettingsSmoke` 通过，覆盖独立配置读写/外部修改保护、Q-Dir 两至四字段解析与本地代码页解码、预览不执行导入项，以及隔离子进程 argv 边界；App Release 构建 0 警告、0 错误；`publish-smoke.ps1` 通过且只处理夹具载荷。
- 未打开启动器窗口或导入真实 Q-Dir 正向启动项样本；可见焦点/键盘验收和含实际 `[Program]` 项的 Q-Dir 配置导入仍待后续。没有生成 release。

## 2026-10-26：标签标题聚焦时支持 Q-Dir Ctrl+方向键切标签

- Q-Dir 官方版本记录列出 Ctrl+VK_LEFT/RIGHT 切换文件视图标签；本项目先前只支持 Ctrl+Tab/Shift+Ctrl+Tab。现新增 Ctrl+←/→，但只在标签标题拥有键盘焦点时生效；文件视图内继续由 Shell 接收 Ctrl+方向键，文本输入框保持原生编辑行为。
- 新门控回归先因 API 缺失而编译失败；接入后完整 Release `SettingsSmoke` 通过，App Release 构建 0 警告、0 错误，相关文件 `git diff --check` 通过。没有操作 GUI；标签标题真实焦点态和切换后焦点仍待桌面验收。[Q-Dir 官方版本记录](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)

## 2026-10-26：复核 20,000 项选区恢复 readiness

- 当前 NativeProbe Release 构建 0 警告、0 错误。一次隐藏 stock `IShellView` 样本中，计数从 0 一次跃迁至 20,000；四个分散目标 PIDL 在总数到位后 58.8/63.9/71.3/80.8 ms 才依次可定位。生产选区快照探针同时通过精确两项恢复、16 种分组/查看方式 replacement fallback、幸存 PIDL 与全部失效 PIDL 场景；前台句柄未变。
- 这只复核目标级 readiness 和状态正确性，不测量批量恢复耗时，也不证明产品 deferred timer 的延迟收益。继续保留完整集合校验；修改批量策略前仍需验证旧选区清除和异步目标恢复，生产宿主/可见 GUI 验收未完成。详见[刷新调查](live-update-investigation.md#2026-10-26-选区恢复-readiness-与批量回退复核)。

## 2026-10-26：完整显示 Program Starter 导入字段

- 对照 Q-Dir 的四字段程序项导入流程发现，导入预览及程序项列表没有显示工作目录，用户无法核对完整导入内容。现在两处均展示工作目录；预览格式化使用纯逻辑函数并由导入 smoke 覆盖名称、程序路径、参数与工作目录。
- Q-Dir 导入专项 smoke、完整 Release `SettingsSmoke`、App Release 构建（0 警告、0 错误）通过，相关差异空白检查通过。没有启动 GUI；导入窗口的实际排版与交互仍待桌面验收。

## 2026-10-26：复核隔离 IShellView 的原生命令执行

- 按迁移门槛，仅在一次性 Private Desktop 和临时文件夹中调用默认 Shell 背景菜单的 Details、Refresh、排序升/降序及分组升/降序。视图读回 `mode=4/iconSize=16`、两个夹具项目、Name 排序及 Type 分组的目标方向；前台句柄保持不变，临时 view 与目录均完成清理。
- `NativeProbe` Release 构建 0 警告、0 错误；`native-probe-watchdog.ps1 -Probe host-menu-commands-private-desktop -TimeoutSeconds 45` 退出码 0。该证据属于隔离默认 filesystem `IShellView`，没有创建产品 ExplorerBrowser、触碰用户桌面或证明可见菜单/焦点/拖放；仅补强架构原型门槛，不改变生产 Shell parity 或颜色迁移决策。

## 2026-10-26：兼容 Windows 本地代码页的 Q-Dir 程序项导入

- 启动器原先用默认 UTF-8 读取选定的 Q-Dir `.ini`；不带 BOM 的 ANSI/本地代码页文件中，中文名称或程序路径可能变成替换字符。现在按 UTF-32/UTF-16/UTF-8 BOM 解码；无 BOM 时先严格尝试 UTF-8，失败后回退 Windows `GetACP()` 代码页。仍无法解码的文件会在导入窗口显示错误，不应用损坏文本。
- 新的文件级 smoke 使用本机 ACP 编码中文启动项、UTF-8 BOM 和 UTF-16 BOM 样本，确认名称及可执行路径保留。定向及完整 Release SettingsSmoke 通过；App Release 构建 0 警告、0 错误，`git diff --check` 通过。未修改 Q-Dir 源文件或运行导入项；桌面导入预览仍待可见验收。

## 2026-10-26：拒绝已替换目录 watcher 的迟到事件

- 单独的对象身份比较仍有“比较后、入队前被替换”的竞态，因此 Created、Deleted、Changed、Renamed、Error 回调现在由 watcher 事件门控串行处理；门控也与 watcher 替换/退休同步，过期对象不会进入队列或再次触发恢复。
- 身份门控用例先在旧实现下因缺少门控而失败；并发回归还验证替换会等待正在处理的回调退出，之后拒绝旧 watcher。新增 `--shell-watcher-recovery-smoke` 专项入口并连续通过 6 次，完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误，`git diff --check` 通过。新增真实 `FileSystemWatcher` 临时目录场景，确认活动 watcher 的 Windows 文件事件可进入回调；退役门控关闭后、异步 Dispose 前送达的下一条真实文件事件被拒绝，完整 Release `SettingsSmoke` 与 App Release 构建通过。该场景验证退役窗口的事件围栏，不宣称 .NET 在 Dispose 返回后仍会投递事件；没有启动 ExplorerBrowser/HostProbe 或 GUI，自动刷新 P0 仍开放。

## 2026-10-26：复核 Q-Dir 官方 TreeNsplit 示例边界

- FAQ 117 的公开 `.qdr` 示例处于 `tree_mode=0`、`Tree1`～`Tree4=0`，且四个 `TreeNsplit` 均为 `2500`。这只展示树关闭时的示例值，不能确定树启用时的宽度单位、方向或逐窗格适用关系；导入器继续明确提示并跳过这些字段，不将其猜测映射为本程序 DIP 宽度。
- 本轮仅补充公开证据边界，没有 Q-Dir 二进制分析、桌面操作或代码行为变更。需要带树启用且宽度不同的可控 `.qdr` 样本或可见互操作证据，才能安全实现该映射。[Q-Dir 官方 FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)；详细边界见 [Q-Dir 状态文件说明](qdr-import.md)。

## 2026-10-26：本机 Q-Dir `.qdr` 样本的树宽字段核查

- 扩大本机 `.qdr` 样本清点后，确认可读的安装配置 `start.qdr`、Q-Dir Quick-Link 工作区和用户文档 `default.qdr` 共三份；均为 `tree_mode=2`、`Tree1`～`Tree4=0`，四个 `TreeNsplit=2500`。安装目录 `Q-Dir.ini` 只记录 `tree_mode=2`，未提供树宽值。官方 FAQ 117 的公开样例同样为四个 `2500` 且树均关闭。
- 这些样本都没有树启用且宽度不同的对照；官方页面也只给样例，没有说明 `TreeNsplit` 的单位或换算。因此不改变导入器跳过这些字段的行为，也不据 `tree_mode=2` 推断任一窗格树可见。需要可控的启用态宽度对照或可见互操作证据；本轮只读文件并查询官方帮助页，未启动 Q-Dir 或 GUI。[Q-Dir FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)

## 2026-10-26：照片打印后台门禁复验

- 按照片打印实施计划重新运行完整 Release `ExplorerDirs.SettingsSmoke`，输出末行为 `Settings smoke: PASS`；App Release 构建 0 警告、0 错误；相关文档差异空白检查通过。计划现将源码/文档门禁标记完成。
- 未打开打印预览窗口、枚举或切换真实打印机、提交打印任务或生成 release；打印机驱动兼容、队列/纸张变化后的实际窗口状态、焦点、可访问性和纸张输出质量仍待隔离可见验收。

## 2026-10-26：修正照片打印预览操作栏遮挡

- 静态检查照片打印 XAML 发现预览边框和底部取消/打印操作栏共用根 Grid 的隐式单元格，按钮会叠在预览区域底部。现在预览占用伸展行，操作按钮放入独立自动高度行；侧栏跨越两行，不挤压其设置和错误列表。
- 新增 `PhotoPrintWindowMarkupSmoke`：先按预期在旧结构上失败，再通过行分配、预览/操作区分离和设置控件 accessible name/label 断言。完整 Release SettingsSmoke 与 App Release 构建通过，0 警告/错误。
- 未启动窗口或打印机；实际布局、键盘可达性、屏幕阅读器播报和打印队列交互仍待可见验收。

## 2026-10-26：未知右键命中仍提供目标无关的标签操作

- DirectUI 无法精确确认鼠标右键命中文件项时，旧路径会完全跳过自定义菜单。自定义命令中包含新建/复制/固定/关闭/恢复标签和复制当前目录路径，这些不依赖点击目标；只有“在新标签打开选中目录”“复制选中项路径”等命令需要可信的 Shell 选区。
- 现在 `Unknown` 和背景命中都会调用自定义菜单构建器，但仅 `Item` 与键盘选区可以提供 Shell selection；未知鼠标命中传入 `null`，因此只出现目标无关的标签操作，Explorer 原生菜单仍保留。新增 smoke 覆盖四种目标的选区授权；用例先因策略缺失按预期编译失败，接入后完整 Release SettingsSmoke 通过，App Release 构建 0 警告、0 错误。
- 原生消息来源为 `SysHeader32` 或 `ScrollBar` 时不插入文件视图标签命令，避免 Unknown 目标策略扩展到列标题/滚动条菜单；来源不明时仍保持保守目标策略。
- 未操作 GUI；未知命中下菜单呈现、右键列标题/滚动条时的实际窗口类和上下文观感仍待可见验收。该改动不扩大文件目标推断范围。

## 2026-10-26：Program Starter 真实参数边界子进程验收

- 之前的启动器 smoke 只检查 `ProcessStartInfo.ArgumentList` 和注入的进程分派，未验证 Windows 创建进程后目标实际收到的参数边界。
- SettingsSmoke 现在以自身作为隔离子进程，通过真实 `ProgramStarterLauncher.Start` 传递空格、中文、嵌入引号、等号和空参数；子进程将实际收到的 argv 原子写入临时 JSON，父进程逐项比对。定向 Release smoke 与完整 Release SettingsSmoke 均通过。
- 这验证了安全探针子进程的进程启动和 argv 传递，不覆盖 Program Starter 窗口导入交互或任意第三方程序；未启动用户配置的 Q-Dir 项，也未操作 GUI。

## 2026-10-26：焦点恢复识别仍活动的 Shell 重命名编辑框

- 文件操作后的焦点监视依赖 `IFolderView.GetFocusedItem` 确认焦点是否已落在项目上；Shell 内联重命名编辑框仍有原生焦点时，如果该项查询暂时不可用，恢复门可能误判为焦点丢失并尝试回焦。
- `IsFileContentFocused` 现在把当前 ExplorerBrowser 中仍持有焦点的 Shell `Edit` 控件视为恢复已完成，即使内容窗 HWND 或 focused item 状态尚未同步；编辑框聚焦时跳过 `IFolderView` focused-item 查询。回归用例还确认另一窗格拥有编辑框焦点时不能完成本窗格恢复。首个编辑框状态用例先因门控缺少该状态按预期编译失败；实现后 Release SettingsSmoke 全量通过，App/HostProbe Release 构建 0 警告、0 错误。
- 没有启动 ExplorerBrowser/HostProbe 或可见 GUI；这是纯状态回归和源码接线证据。多布局 F2 后物理方向键、文件操作与重命名交错的真实焦点仍待 GUI 验收。

## 2026-10-26：文件操作焦点恢复避开列标题输入

- 文件操作后焦点恢复待处理期间，原生 `SysHeader32` 列标题点击也满足“内容 HWND 后代”的判断，输入代际更新会让恢复请求继续存活；后续重试可能把用户刚聚焦的列标题焦点拉回文件项，干扰排序。
- 现在保留恢复请求时同时判断输入 HWND 是否为 `SysHeader32`；列标题输入取消文件项焦点恢复，当前视图中的文件区输入仍保留有界恢复。`ShellFocusRestoreGateSmoke` 新增列标题回归，先因缺少该区分按预期编译失败；接线后 Release SettingsSmoke 全量通过，App 与 HostProbe Release 构建均为 0 警告/错误。
- 未运行 ExplorerBrowser/HostProbe 或可见 UI；这是源码路径与状态 smoke 的证据，不能声称真实列标题排序/文件操作后的焦点体验已验收。粘贴/删除后的键盘方向键、物理列标题交互和多布局 GUI 验收仍开放。

## 2026-10-26：固定 watcher 异步启动 smoke 的调用线程

- `DirectoryWatcherStartupSmoke` 曾间歇误报后台激活与调用线程相同；根因是 smoke continuation 在 ThreadPool 上运行，`Task.Run` 后续也可能复用同一 worker 的 ManagedThreadId。产品启动步骤仍在后台执行，但测试用线程 ID 误把顺序复用当作同步执行。
- 现在该断言由专用长运行线程发起，并在该线程同步等待异步启动完成，确保调用线程在后台激活期间保持存活。更新后 Release SettingsSmoke 连续两轮通过；既有 App 与 HostProbe Release 构建均为 0 警告/错误，`git diff --check` 通过。
- 本修复只稳定验证夹具，不改变产品 watcher 调度；真实慢盘/网络 watcher 初始化和可见 UI 仍待验收。

## 2026-10-26：Shell 瞬态空视图恢复预算在计数收敛后关闭

- 双次空视图刷新会启动有界重试预算；当 Shell 后续恢复到 watcher 的准确目标计数时，`Complete()` 已确认状态收敛，但旧实现没有关闭这轮剩余预算，可能把它带入后续独立目录变化。
- 新回归在旧实现上失败，接入后准确计数收敛会关闭空视图恢复窗口；下一次变化重新使用普通停滞等待策略。Release SettingsSmoke 全量通过，App 与 HostProbe Release 构建 0 警告/错误，`git diff --check` 通过。
- 未运行 ExplorerBrowser/HostProbe 或可见 UI；只验证纯状态机和构建，没有实测 Shell 计数收敛时序。自动刷新 P0 仍开放。

## 2026-10-25：Q-Dir 程序路径引号内等号解析

- 复核 `[Program]` 导入后发现，字段分隔符扫描只按第一个 `=` 查找程序路径边界；即使路径整体由引号括住，路径中的 `=` 仍会被误认作字段分隔符并导致该启动项跳过。现在路径和尾部字段只将引号外的 `=` 作为分隔符，保留引号内等号。
- 新增真实解析 smoke：`"C:\\Tools\\Build=Compare.exe"` 必须作为完整程序路径导入；用例在旧代码下按预期失败，修复后 Release SettingsSmoke 全量通过，App Release 构建 0 警告/错误，`git diff --check` 通过。
- Q-Dir 格式对引号转义及参数中单个引号外 `=` 与工作目录边界仍没有可验证的消歧语法；本修复不猜测这些格式，现有规则仍将唯一尾部分隔符解释为工作目录边界。没有启动 GUI 或读取/修改 Q-Dir 配置文件。

## 2026-10-25：焦点恢复阶段跳过已完成选区的 readiness 扫描

- deferred refresh 已确认完整选区恢复后，后续重试只需恢复焦点；原实现仍为每个 100 ms tick 构造选区 readiness 字符串并遍历所有已选 PIDL。现在仅在选区尚未完整恢复且非空时查询 readiness，用户输入/导航代际变化仍会先重置选区完成状态并恢复查询。
- `ShellSelectionRestoreGateSmoke` 覆盖未完成选区继续查询、完成选区跳过扫描和空选区跳过查询。新增用例先因缺少门控而编译失败；接入后全量 Release SettingsSmoke 通过，App 与 HostProbe Release 构建通过（0 警告/错误），`git diff --check` 通过。一次并行运行曾触发既有 watcher startup 时序断言，串行全量复跑通过。
- 未运行 ExplorerBrowser/HostProbe 或可见 UI；本次只验证门控与接线，未测量大选区延迟改善。自动刷新 P0 与焦点可见行为验收保持开放。

## 2026-10-25：Shell 计数不可读会打断大批量稳定窗口

- 大批量计数不匹配的停滞窗口以连续相同的 Shell 计数判定；复核发现一次计数不可读会等待重试，但旧值仍保留在稳定窗口里，下一次相同计数会被错误算作连续样本并可能提前触发刷新。
- 现在不可读样本清除上次计数与连续稳定次数，同时保留累计等待次数及 watcher 增量，避免读取间隙让总重试上限重置。回归 smoke 先在旧代码上按预期失败；修复后 Release SettingsSmoke 全量通过，App 与 HostProbe Release 构建 0 警告/错误，`git diff --check` 通过。
- 仅有纯状态与构建证据；未启动 ExplorerBrowser/HostProbe 或可见 UI，没有计时/性能结论。自动刷新 P0 仍开放。

## 2026-10-25：丢弃跨 Shell 选区代际的 readiness 观察

- 复核 deferred selection restore 时发现，PIDL readiness 查询后没有确认 `SelectionVersion` 仍与查询开始时一致。若同步 Shell COM 查询期间重入 `OnStateChange`/选区回调，当前 tick 可能消费跨代际的状态向量。现在查询前后比较代际；若变化则丢弃该次结果、清空 readiness 缓存、保留重试，并在下一 tick 重新观察，不启动本轮批量恢复。
- `ShellSelectionRestoreGateSmoke` 新增代际稳定/变化两种判定，先在旧源码上按预期编译失败；接入门控后 Release SettingsSmoke 全量通过，App 与 HostProbe Release 构建通过（0 警告/错误），`git diff --check` 通过。
- 仅有状态门控与源码接线证据，未尝试人为触发 COM 重入，也未运行 ExplorerBrowser/NativeProbe 或可见 UI。该保护不改变 100 ms 重试和 1.5 秒最终回退；自动刷新 P0 与生产宿主验收保持开放。

## 2026-10-25：按目标 PIDL 可定位状态节制选区重试

- 新增隐藏 stock `IShellView` NativeProbe，建立 20,000 项文件夹并在初次枚举期间抽样 4 个分散目标 PIDL。两次独立样本的 `IFolderView::ItemCount` 首次读到 20,000 后，4 个目标分别再过 79.0–98.7 ms 与 58.0–75.0 ms 才被 `GetItemPosition` 定位，且各自在约 17–20 ms 内依次可定位；计数到位不代表快照目标全部进入视图。小视图也复现了 `GetItemPosition` 在项目进入视图前返回 `E_FAIL`、进入后返回 `S_OK`。失败仍然无法区分暂未枚举与 stale PIDL。
- 生产 deferred restore 记录各快照选中 PIDL 的定位状态；状态相同且选区未完整时跳过重复整批恢复，只重新查询尚未定位的 PIDL。缓存的正向结果限定在当前 Shell 选区代际内；`SelectionVersion` 变化清空缓存并重查全部目标。状态变化可重试，不可用的 readiness 查询回退旧重试路径；1.5 秒最终回退仍尝试未完成的选区，并保留完整 PIDL 集合核验和独立焦点阶段。可选 Shell trace 记录状态位向量和本轮定位查询数，不含文件名/路径。
- SettingsSmoke 的重试门控用例先按预期编译失败，接入后全量 Release SettingsSmoke 通过；App、NativeProbe、HostProbe Release 构建通过，0 警告/错误。隔离 NativeProbe 的 2 项完整视图 readiness 返回 `11`；缓存第一个正向状态后只调用一次定位查询即可确认第二项，删除一个目标后 fresh 检查仅一个 PIDL 仍可定位，replacement fallback 恢复幸存项，既有分组/查看矩阵退出码 0。20,000 项定位探针退出码 0，父窗口隐藏、前台不变。
- 未运行 ExplorerBrowser HostProbe 或可见 GUI，故生产多窗格大目录中的批次次数/STA 延迟改善尚未验证；没有发布。自动刷新 P0 继续开放。

## 2026-10-25：稳定空目录的 deferred 选区恢复边界

- 新增隐藏产品 HostProbe 场景：先捕获三项选区 PIDL，删除夹具中的全部文件并调用原生 `IShellView.Refresh()`，确认当前 Shell 视图为 0 项后再排入生产 deferred restore。13 次 100 ms 重试均只记录 empty-view defer，没有 selection batch；到 1.5 秒窗口结束执行一次逻辑 final fallback（内部首次批量与 replacement fallback 共调用 2 次 batch API），1,548 ms 后 pending 清除，随后 300 ms 未再次调用。最终视图/选区为 0/0，前台与隐藏窗口边界保持。
- 首轮运行揭示探针在把 snapshot 所有权交给 production timer 后又通过 `using` 双重释放，导致退出期堆损坏；修正为调度成功后由产品持有，只有调度失败才由探针释放。修正后的探针退出码 0。既有 `--live-shell-selection-partial-snapshot` 也退出码 0：3 项完整快照在收到 2 项部分回调后仍保留，并恢复 3 项选择。
- 部分快照回归验证了真实产品回调路径，但不是 50k 异步枚举时多目标逐个出现的完整时序。自动刷新 P0 与大目录 STA 长尾仍开放；无可见 GUI 操作、无 release。

## 2026-10-25：Shell 空视图阶段延后选区重放

- 既有 50k HostProbe 证明刷新后 `ItemCount=0` 是真实暂态；此时仍执行 `SelectAndPositionItems`，而 Shell 重新枚举期间会继续发送选区变更。现在 `RestoreSelectionAfterRefresh` 在有快照选区且 `IFolderView::ItemCount(SVGIO_ALLVIEW)` 明确为 0 时跳过该次同步重放，让 100 ms deferred timer 继续观察。读取失败保持原路径；到 1.5 秒最终回退时仍强制做一次尝试，避免门控无限等待。
- 新增状态 smoke 覆盖空视图延后、非空继续、计数错误走旧路径和 focus-only 不延后；先确认旧实现编译失败，再接入门控后完整 Release SettingsSmoke、App 与 HostProbe Release 构建通过，均 0 警告/错误。
- 新隐藏产品 50k 创建/删除压力运行退出码 0：初次 `view-count=0` 的两次 refresh restore 均为 0 ms，trace 显示 `selection-restore-deferred-empty-view`，没有在空视图阶段调用 batch API；随后 Shell 重新枚举并恢复两项选区及独立焦点，最终文件/视图计数为 3/3。目标场景中 selection batch 后续仍约 48 ms，最大 Dispatcher heartbeat gap 904 ms，因此这只消除了空视图的无效早期调用，未解决其它 Shell STA 长尾或宣称总体耗时改善。
- 未运行可见 GUI、未生成 release。自动刷新 P0 继续开放；下一步复核 0 项稳定空目录的有界最终回退，以及在部分枚举时多目标选区仍能完整恢复。

## 2026-10-25：隔离验证 Shell 视图目标 PIDL 的可见时机

- NativeProbe 的隐藏 stock `IShellView` 创建后立即读取视图计数为 0；对已存在但尚未显示在视图中的 fixture 项调用 `IFolderView::GetItemPosition` 返回 `E_FAIL`。同一项在视图枚举到 4 项后返回 `S_OK`。探针没有创建 ExplorerBrowser、显示窗口或改变前台；Release 构建 0 警告/错误，selection batch spike 退出码 0。
- 补充 stale-PIDL 对照后，刷新并确认删除的目标同样返回 `E_FAIL`；因此该 HRESULT 确实无法区分“异步枚举尚未到达”与“PIDL 已过期/项目已删除”。探针仍未验证 4 种以上多选目标和分组/查看模式的早期结果。因此尚未接入产品恢复门控；选区恢复仍保持批量恢复及完整 PIDL 集合核验。
- 下一步不再将单项 `GetItemPosition` 失败作为等待依据；需寻找能够区分“仍枚举”与“稳定缺失”的信号，或为已观察到消失的目标设置有界失效规则，并先在多项目隐藏视图中验证。只有能减少重复同步恢复、又不让删除目标阻塞其它选区时，才调整产品策略。

## 2026-10-25：目录 watcher 初始化移出导航线程

- `FileSystemWatcher` 的构造、启用和释放现在走后台任务；只有候选 watcher 创建完成后才回到 Dispatcher，并核对监视器代际、导航代际、路径和队列是否仍匹配。快速导航或宿主销毁后，旧候选不会安装到新视图，且其清理不阻塞导航线程。Watcher 恢复仍使用原退避策略，恢复期间可能发生的变化仍触发强制计数协调。
- 新增启动状态 smoke，先观察缺失行为时的预期编译失败，再覆盖慢构造/启用不阻塞调用线程、过期导航候选释放、正常启用及启用失败后的撤销与释放。完整 Release SettingsSmoke 通过；App 和 HostProbe Release 构建均 0 警告、0 错误。
- 隐藏 HostProbe watcher-recovery 通过；产品监视探针的独立 Shell observer 注册曾返回失败（`last-error=0`），同一产品监视模式不依赖该 observer 的重跑通过，覆盖跨窗格复制/移动/删除、文件夹创建/删除、目录计数及选区/焦点恢复。探针父窗口保持隐藏；没有操作桌面或生成 release。
- 这只移除了 UNC/慢路径 watcher 初始化和清理对 Dispatcher 的同步等待，不解决 Shell 通知间歇遗漏或大目录可见延迟。真实远程/离线网络、可见 UI 响应和跨 Windows Shell provider 仍待验收，自动刷新 P0 保持开放。

## 2026-10-25：50,000 项产品与 native-only 诊断对照

- 修正 HostProbe 在反射调用带可选 `IntPtr` 参数的 `NoteNativeUserInput` 时传 `null`，改为显式传 `IntPtr.Zero`；此前压力模式在夹具操作开始前以 `TargetParameterCountException` 退出。Release HostProbe 构建通过，0 警告、0 错误。
- 在相同 50,000 项创建/删除及 128 项批量删除负载下，使用同一消息泵、Dispatcher 队列与 Shell callback 诊断配置完成一组隐藏生产/native-only 对照。产品模式 watcher 创建/删除均 50,000/50,000，最终目录视图和磁盘均为 3，选区/焦点恢复；3 次删除 fallback。native-only 也达到最终 3 项，不触发产品 watcher、fallback 或 Refresh；其 `selection-restored=False` 属该模式不验证产品选择恢复的既定边界。
- 产品模式测得 1,156 ms 最大 Dispatcher heartbeat gap、约 1,415 ms ContextIdle 最大间隔，消息泵最大空档 260 ms，单个被跟踪 WndProc 最大 178 ms；native-only 分别约 237 ms、309 ms、204 ms、52 ms。产品计数读取最大约 0.5 ms、Refresh 最大约 17 ms、目录 timer tick 最大约 17.6 ms；这些调用本身没有解释 1.4 秒 ContextIdle 延迟。该匹配样本提示产品路径与 ContextIdle 队列积压相关，但仍是单组、顺序对照，不能断言因果。
- 产品模式的 Shell 选择/焦点路径 trace 已启用，完整 50k HostProbe 退出码 0。此前将 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE=0` 的轮次缺少 `focused-item-lookup=direct-select` 标记、因此退出码 1，属于诊断开关配置错误，不计为产品行为失败。没有操作可见桌面或生成 release。
- 下一步保持时序不变，增加一组交错重复并对齐 Dispatcher 队列来源与 Shell WndProc 区间；先确认额外积压来自产品通知回调、Shell 枚举还是探针高优先级采样，再评估生产调度调整。自动刷新/大目录响应 P0 继续开放。

## 2026-10-25：50,000 项反向顺序交错复测

- 按 native-only → 产品模式反向顺序复跑同一 50,000 项负载并保留消息泵、Dispatcher queue-latency 与 Shell callback trace。native-only 创建/删除 40,964/43,991 ms，产品模式 41,109/37,771 ms；两轮最终视图/磁盘均收敛到 3，产品 watcher 创建/删除均 50,000/50,000，选择与焦点检查通过。产品轮创建/删除 fallback 为 1/3。
- native-only observer 收到 49,999/50,000 个变化，最终状态与选择/焦点检查通过，但因 observer 数量门槛退出码为 1；不计为完整通过样本。产品轮退出码 0。
- 两轮独立队列探针的 ContextIdle 最大排队延迟分别约 1,246/1,342 ms，Dispatcher 的 ContextIdle 最大间隔约 1,137/1,411 ms；native-only 也出现约 1.25 秒长尾，故原先单组顺序样本中的“产品模式独有高 ContextIdle backlog”不能复现。消息泵最大空档为 117/430 ms，WPF Dispatcher `0xC471` 单次最大处理分别约 96/430 ms；这些是关联测量，不足以把延迟归给 watcher、Shell 枚举或特定产品回调。
- 产品 trace 的 `ScheduleDeferredRefreshSelectionRestore` Background timer 两次执行约 422/430 ms，并与相邻 `0xC471` 空档重叠。其内部可见 Shell 选择 batch API 一次约 57 ms、焦点 `SelectItem` 约 36 ms；现有步骤和仍未归因的 Dispatcher 区间不相等，可能还有 Shell 同步重入/事件处理成本，不能把整个 timer 执行时间当作单个 COM API 耗时。
- 反向顺序复测削弱了产品专属因果假设，但本轮有探针 observer 漏 1 事件且每种模式仍仅一轮；不修改生产刷新/调度策略。下一步转向低扰动、针对 `0xC471` 与 Shell 枚举窗口的分段关联测量，避免继续重复无法区分根因的同类压力轮。可见 UI、真实 Q-Dir 交互和其他 Shell provider 未验证，自动刷新 P0 继续开放。

## 2026-10-25：Shell state-change 单回调耗时追踪

- 为对齐产品刷新/选区恢复 Dispatcher 窗口与 Shell `OnStateChange`，在现有 opt-in `EXPLORER_DIRS_SHELL_CALLBACK_TRACE` 时间线中记录每条 state-change 回调的执行毫秒；正常运行未设置诊断环境变量时不计时、不生成样本。新增 SettingsSmoke 先在旧实现上按预期失败，再验证单回调耗时字段与原聚合计时，完整 Release SettingsSmoke 通过；HostProbe Release 构建 0 警告、0 错误。
- 更新后的隐藏产品 50k 复测退出码 0：watcher 创建/删除 50,000/50,000，最终视图/磁盘 3/3，选择/焦点恢复，fallback 为 1/4。50 次 state-change callback 合计约 307 ms，单次最大约 23.5 ms；`ScheduleDeferredRefreshSelectionRestore` timer 本轮两次执行约 147/168 ms，未重现此前 422/430 ms；ContextIdle 最大队列延迟约 475 ms，最大消息空档约 244 ms。
- 随后给 opt-in callback、恢复步骤、Dispatcher 操作和 WndProc 事件加共同 `Stopwatch` tick 时间戳，再跑一轮产品 50k：创建/删除 watcher 均 50,000/50,000，最终 3/3，选区/焦点恢复，退出码 0。该轮一个 `ScheduleDeferredRefreshSelectionRestore` Dispatcher 操作是 163.7 ms，`0xC471` WndProc 同区间为 163.8 ms（起点相差 29 ticks、终点相差 809 ticks）；此操作内 shell selection batch 约 55.2 ms、两次直接选中约 7.9/50.2 ms、焦点 SelectItem 约 48.6 ms，合计约 161.9 ms。重叠的 9 个 `OnStateChange` 回调合计约 1.82 ms，单个最大约 0.67 ms。因此该样本的 164 ms STA 阻塞由产品选区/焦点恢复的同步 Shell 调用占主，而不是 state-change 回调；尚未解释其它 WndProc/ContextIdle 长尾，且仍只有单轮。
- 下一步用相同 raw tick 证据检查其它恢复回合：减少一轮中对尚未完全枚举 PIDL 的重复同步 `SelectItem` 尝试，同时保持多选与未选中焦点恢复正确；先建立状态 smoke/隐藏宿主回归，再决定是否改调度。未操作 GUI、未生成 release，自动刷新 P0 继续开放。

## 2026-10-25：选区完整性门控与分阶段焦点恢复

- 代码复核发现逐项恢复只要任意一个 `SelectItem` 成功就会将 `SelectionRestored` 置真，可能导致下一次重试跳过仍缺失的其它选中项。现逐项尝试后枚举当前选中 PIDL 集合并要求与快照完整匹配；只有选区完整才允许焦点恢复。空选区/焦点-only 快照仍可直接恢复焦点。
- 将 deferred refresh 恢复拆为两个 Dispatcher timer 阶段：本次选区尚未确认时只恢复选区；完整选区确认后，在下一次 100 ms 重试恢复焦点。用户输入、导航、视图代际和 1.5 秒重试窗口门控保持不变。新增状态 smoke 覆盖未完整选区不得恢复焦点、完整选区后允许恢复焦点、空选区焦点-only 和无焦点快照。
- 隔离 `IShellView` Release NativeProbe 通过：完整两项多选恢复后，selection-only 阶段没有调用 focused `SelectItem`；focus-only 阶段保持两项选择并恢复焦点；既有 direct/replacement/stale-PIDL 场景继续通过。Release SettingsSmoke、NativeProbe 与 HostProbe 构建通过，均 0 警告/错误。
- 隐藏产品 50k 轮退出码 0：watcher 创建/删除均为 50,000/50,000，Shell/磁盘 3/3，选择/焦点最终恢复，fallback 0/2。之前 50k 样本中一个 selection+focus Dispatcher tick 为 163.7 ms；本轮分阶段后 selection tick 为 113.3 ms，下一次 focus `SelectItem` 约 52.3 ms。该轮最大 Dispatcher heartbeat gap 266 ms，ContextIdle queue delay 170 ms；单轮压力数据说明同步工作已拆分但选区阶段仍超过 100 ms，不能宣称所有 STA 长尾已解决。
- 下一步针对首个批量选区 API 约 52.7 ms 且只恢复出一个项目的情况，决定何时继续逐项恢复、何时重试，减少重复批量操作；保留对旧额外选区的清除和完整集合核验。自动刷新 P0 仍开放；未操作可见 GUI、未生成 release。
- 试验了“当前选区为空就跳过批量、直接逐项恢复”的分支，但 stock Shell 隔离 view 中所有项目均已枚举时，direct `SelectItem` 即时读回仍为 0 项，而批量路径可立即恢复完整两项；因此空选区本身不足以证明 view 正在异步枚举，已撤回该优化，没有让 NativeProbe 回归失败或改变产品路径。后续需要可靠的 view-enumeration readiness 信号，才可安全选择 direct-first。

## 2026-10-25：Release 源码集成门禁复核

- 完整 `ExplorerDirs.SettingsSmoke` 通过；`ExplorerDirs.App`、`ExplorerDirs.HostProbe`、`ExplorerDirs.NativeProbe` 的 Release 构建均成功，0 警告、0 错误；`docs/roadmap.md` 与 `docs/qdr-import.md` 的 diff check 通过。
- `pwsh -NoProfile -File scripts/publish-smoke.ps1` 通过：只用 fixture 测试隔离 profile 白名单，确认 `ProgramStarter.ini` 与已登记会话、工作区、快速链接按原字节复制；未登记会话和无关文件不复制、源 profile 不变，且拒绝覆盖已存在版本。脚本没有生成真实 release 或启动可执行文件。
- `pwsh -NoProfile -File scripts/folder-verb-smoke.ps1` 通过；测试范围是隔离的 HKCU 测试键，覆盖安装、幂等、冲突拒绝、外部修改保留和仅移除本项目拥有的值，不注册默认 `Software\Classes` shell verb，也不启动 fixture 程序。
- 此轮只验证构建与无窗口 smoke，没有启动 HostProbe/NativeProbe、Q-Dir 或 GUI。因此它确认当前源码集成与探针可编译，不替代 ExplorerBrowser/IShellView 实际运行、可见操作或 Q-Dir 互操作验收。

## 2026-10-25：补查 Q-Dir 树模式菜单资源

- 对本机 Q-Dir 12.79 PE（SHA-256 `9adaa5994a476f06f0dc9cdf5eae9cc0986cbfc43055fb9f05341f803fb21fca`）只读枚举资源树：`RT_MENU` #128 包含命令 `0x806b` / `0x806c` / `0x806d`，其相邻文本为数字令牌 `54003` / `54001` / `54002`；`RT_STRING` 只含块 #9 与 #3585，没有直接对应这些令牌的字符串块。该结果不能补齐函数交叉引用或 `TreeNsplit` 的像素/比例语义，不改变现有 `tree_mode` 映射，也不改变 `TreeNsplit` 不导入的决定。
- REA PE 资源读取器结果为 observed；当前深度反汇编 provider 不可用（Ghidra 未配置，Hopper 不支持 Windows，IDA/Kuna 未配置）。没有安装或改分析环境，没有启动 Q-Dir/GUI。详见[Q-Dir 导入证据](qdr-import.md#2026-10-25-treensplit-静态读取路径补查)；树宽字段仍需可控启用态/改宽 `.qdr` 样本或可用的函数级分析证据。

## 2026-10-25：自动刷新大批量停滞视图提前恢复

- 生产计数批次对至少 32 项的大差异继续等待 Shell 正在推进的枚举；Shell 项目数连续四次不变时则提前触发现有 `IShellView.Refresh()` fallback，任何计数进展都会重置停滞计数。12 次总重试上限、watcher 活动静默窗口、强制刷新和单项差异策略保持原样。
- 专项 `ShellDirectoryChangeBatchSmoke` 覆盖停滞提前恢复、计数进展重置、持续进展仍受总上限约束、watcher 活动重启等待，以及强制刷新绕过等待；Release 专项 smoke 通过，App Release 构建 0 警告/错误。
- 这是纯状态机和源码接线证据，不是实际大目录耗时改善证明。当前无 Computer Use，本轮没有启动 ExplorerBrowser/HostProbe；STA 长尾、可见更新、网络/慢速目录验收仍开放，自动刷新 P0 不关闭。

## 2026-10-25：Q-Dir Program Starter 参数中的等号兼容

- 官方格式以 `=` 分隔名称、程序路径、参数和可选工作目录；先前导入器把整行分割并拒绝额外分隔符，导致常见参数（如 `--mode=a=b`）整项丢失。现在只把前两个分隔符用于名称和程序路径；参数部分含多个 `=` 时按完整参数文本保留，只有恰好一个剩余分隔符时才按官方四字段格式拆出工作目录。
- 新回归先在旧实现下按预期失败，更新解析后完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告/错误，相关文件 `git diff --check` 通过。Q-Dir 格式没有转义/引用分隔符的语法，参数中恰好一个 `=` 与四字段工作目录形式仍无法无歧义区分，按四字段解释。
- 来源：[Q-Dir FAQ 46](https://www.softwareok.com/?faq=46&seite=faq-Q-DIR)。

## 2026-10-25：Program Starter 参数模板、导入预览与安全启动分派

- 参数构造器现解析 Windows 引号规则，将 `%sel_files%` / `%all_files%` 与窗格 1–4 的列表占位符按独立 argv 参数展开；支持当前/各窗格目录、`%fdir%`、Windows 特殊目录和程序所在盘符。空选择、虚拟项目、未知或不完整 token、嵌入列表 token 会在启动前返回错误。列表参数由调用方直接传 `ProcessStartInfo.ArgumentList`，不拼接命令解释器字符串。
- 新增纯 `QdrProgramImporter.Parse`，只识别 `[Program]` 中 2–4 字段行，支持无参数的 `Name=Path`；程序路径/工作目录支持整字段引号；对缺项、空名称、坏引号返回行号和原因，并忽略已知的 `portable_install` 元数据。解析不接触文件系统、不写回 Q-Dir.ini、不创建进程。额外 `=` 参数保留规则见上方后续修正。[Q-Dir FAQ 46](https://softwareok.com/?faq=46&seite=faq-Q-DIR)
- 新增 `ProgramStarterLauncher`，使用 `ProcessStartInfo.ArgumentList`、`UseShellExecute=false` 和普通用户身份；绝对程序路径、应用目录相对程序路径及 PATH 命令按各自规则解析。准备命令与实际启动委托可分开验证。
- 新增启动器窗口：可增删改、选择启动项、导入 Q-Dir 配置并审阅可导入/跳过行，只有明确按下应用才写本项目 INI；启动时采集当前可见布局的窗格列表和活动选择。工具菜单和 Ctrl+S 共用入口，文本输入焦点仍受快捷键保护。
- 独立 profile 发布白名单现包含 `ProgramStarter.ini`，隔离打包 smoke 会校验它被原样复制。
- 完整 Release SettingsSmoke、App Release 构建（0 警告/错误）和 `git diff --check` 通过。尚无 GUI 验收，也未启动任何用户配置程序；本机 Q-Dir.ini 不含正向启动项，实际 Q-Dir 文件互操作仍待带有效 `[Program]` 样本核验。

## 2026-10-25：Program Starter 独立配置存储

- 新增 `ProgramStarterStore` 与 `ProgramStarterEntry`，将命令项保存在 profile 独立的版本化 `ProgramStarter.ini`；不复用或改写 Quick Links。稳定 GUID、重复名称和 Unicode 字段可往返保存；损坏文件先备份，未知高版本和外部编辑冲突拒绝覆盖。
- 初始存储切片的专项 SettingsSmoke 先观察到缺失存储类型的预期失败，再接入实现。此后的 2026-10-25 后续条目记录了导入、参数、启动分派和 UI 接线；本条只记录最初存储阶段，不代表当前功能状态。

## 2026-10-25：隔离宿主键盘探针暴露前台边界失败

- 完整 `--ishellview-host-filter` 通过隐藏视图筛选、Ctrl+A、方向键、Enter 与子视图创建，但随后报告 `foreground-unchanged=False` 并以边界断言失败退出（码 1）。子视图计数正确，`finally` 清理执行且临时夹具未残留；当前无法把前台变化确定归因于单一焦点或 Shell 调用。
- 官方 `SetFocus` 文档指出它会激活目标窗口或父窗口；探针只在 `SetFocus` 与 `TranslateAccelerator` 之后读取前台，所以还不能断言是哪步改变前台。首次独立 Desktop 清理试验卡在 `DestroyWindow`。
- 新增私有 Desktop 一次性探针运行器及 watchdog 入口后，完整宿主筛选/快捷键/导航链路退出码 0：Ctrl+A、Down、Enter、子目录筛选、父目录、历史前进/后退和边界拒绝均通过，主桌面前台句柄全程未变。Shell view/接口释放及夹具删除通过；顶层窗口由 NativeProbe 进程退出回收。该结果证明独立 Desktop 能隔离这些焦点调用对用户前台的影响，但没有区分导致直接运行前台变化的具体调用；产品 ExplorerBrowser 未变，真实物理按键/可见焦点仍待验收。
- 此次超时试验在构建输出中留下两个唯一 GUID 临时夹具目录（`44962db996be4e7fb7240675b2a0ca19`、`ef45128fa4f3440daecd45232912d2ce`）；终止进程后仍存在。它们只含本探针生成的临时文件，未改用户数据；清理命令受执行策略阻止，未强行绕过。
- 记录：[隔离 IShellView 原型](ishellview-color-spike.md#2026-10-25-隐藏宿主键盘探针前台边界复核)
- 依据：[Microsoft SetFocus](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setfocus)

## 2026-10-25：按用户选择复核隔离 IShellView 颜色原型

- NativeProbe Release 构建成功（0 警告、0 错误），隐藏 watchdog 探针退出码 0；前台句柄未变，父窗口不可见，未创建产品 ExplorerBrowser。八种原生查看模式、背景/项目 Shell 菜单、选区、`IFolderView2` 与 `IDropTarget` 查询仍成功。
- 真实逐项通知本次仍只有平铺模式收到两条，通知矩形为空、裁剪区为 `NULLREGION`、目标色像素为 0；因此颜色实际绘制未证实。接口/菜单可查询也不等同于文件操作执行成功。保持 ExplorerBrowser 产品实现不变；当前无可见桌面操作授权，下一步继续源码核对隔离宿主尚缺的导航、过滤、状态流和操作验收边界。
- 记录：[隔离 IShellView 颜色原型](ishellview-color-spike.md#2026-10-25-按路线图复跑隔离原型)

## 2026-10-25：补录 Q-Dir Photo-Print 差异

- Q-Dir 官方 FAQ 150 将“文件→工具→照片打印”描述为直接打印选中的一张或多张照片，并可设置照片尺寸、每页张数、打印机及预览。当前项目的“打印当前文件视图”生成目录清单，功能不同；合并的 Explorer Shell 右键菜单是否提供 Windows 照片打印动词取决于系统图像处理器，不能等同于应用级照片打印入口。
- 对照表新增独立 P2 差异。后续评估优先查询所选图像的 Shell canonical `Print` 动词及启用状态、参数和对话框归属；若系统处理器提供等价照片对话框则复用，否则再设计实现。此处不新建图像打印引擎，不宣称真实菜单已验收。
- 来源：[Q-Dir FAQ 150](https://www.softwareok.com/?faq=150&seite=faq-Q-DIR)、[Q-Dir 版本历史](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)。

## 2026-10-25：只读核验 Windows Shell 图片 Print 动词

- 修正 NativeProbe 的 COM vtable 读取错误后，Release 构建通过（0 警告、0 错误），隐藏 watchdog 探针退出码 0；临时 PNG 的 `IShellFolder::GetAttributesOf` 与选中项上下文菜单查询成功，规范 `Print` 动词报告 enabled。探针没有显示菜单、调用 Print 或启动打印程序/对话框，前台句柄未变。
- 这证明当前 Windows 图像处理器为该 PNG 暴露了可用的 Shell Print 动词，但不证明它提供 Q-Dir 照片打印所需的尺寸、每页张数、打印机选择和预览流程。仍需在之后获准的隔离可见验收中确认具体处理器行为，再决定能否复用；项目现有目录清单打印仍不等价。

## 2026-10-25：明确照片打印实现边界

- Q-Dir FAQ 150 明确要求所选照片、尺寸、每页数量、打印机和预览。Microsoft Shell 文档将 canonical `Print` 定义为调用文件类型已注册的打印命令，具体命令可与打开命令相同并附加打印参数；因此它不能保证统一的照片设置窗口或预览，不能作为本项目 Photo-Print 的完整实现。
- 对本机 Q-Dir 12.79 PE 的导入名称静态读取发现 `PrintDlgW`、`PageSetupDlg`、`OpenPrinterW`/`GetPrinterW`、GDI 分页打印和 GDI+ 位图绘制 API。该发现支持“Q-Dir 二进制包含原生打印/图像处理能力”，但没有函数级交叉引用，不能证明它们属于 Photo-Print 命令或恢复其 UI/布局；不改变拟议的应用内打印文档方案。
- 已准备应用内 WPF/WIC 照片打印设计与三阶段实现计划，复用 Windows 打印机/纸张选择，活动 Shell 视图提供选区，应用负责受测布局与预览。当前仅为设计/计划，尚未改源码；请先预览设计，确认范围后进入代码实现。
- 来源：[Q-Dir FAQ 150](https://www.softwareok.com/?faq=150&seite=faq-Q-DIR)、[Microsoft Shell Print](https://learn.microsoft.com/en-us/windows/win32/shell/print)、[IContextMenu invocation structure](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ns-shobjidl_core-cminvokecommandinfoex)。

## 2026-10-25：照片打印源码接入

- 已接入活动窗格当前选择、普通文件筛选、WIC 原分辨率解码和有界预览图、坏图逐项反馈、1/2/4/6/9 张分页、Fit/Fill 与自定义厘米尺寸、固定页面文档，以及文件菜单中的“打印所选照片…”入口。
- 照片打印窗口提供本机打印机和该打印机支持纸张的选择。纸张/布局更改会重建预览；只有当前预览有效时打印按钮才会提交，打印路径复用已验证的队列和 `PrintTicket`，不再弹出可改纸张的第二个系统设置对话框。预览使用降采样源，打印文档使用原像素源。
- Release SettingsSmoke 覆盖选择筛选、分页几何、WIC 好图/坏图/非图片混合、句柄释放、文档页数及过期预览禁止提交；App Release 构建通过。尚未打开窗口、枚举真实打印机、创建打印任务或验收实际打印质量；这些仍是隔离桌面验收项，本功能未发布。

## 2026-10-25：补录 Q-Dir Program Starter 差异

- Q-Dir 官方 FAQ 46 与 Program Starter 专页确认：Ctrl+S 打开启动器；字段为 `Name=Path=Arguments=Work directory`，允许相对程序路径；参数可引用当前目录、选中项、全部文件以及各窗格目录/文件。官方示例说明可通过 Ctrl 启动为管理员。该条记录了差异发现时的状态；当前 Explorer Dirs 另有独立 Program Starter，Quick Links 仍只管理 profile 内 `.lnk` 快捷方式。
- 将其列为独立 P2 功能差异，不把现有 Quick Links 宣称为等价能力。用户已选择独立便携 INI，并要求支持导入 Q-Dir 程序项；后续已明确持续沿路线图推进，实现授权已覆盖该切片。[Program Starter 设计草案](superpowers/specs/2026-10-25-program-starter-design.md)记录当前实现和验收边界。公开资料给出 Q-Dir.ini、`[Program]` 启动项字段/行示例；本机 `C:\SoftwareOK\Q-Dir\Q-Dir.ini` 有 `[Program]` 节但仅含 `portable_install=1`，没有实际启动项可作为正向样本。导入使用预览/显式应用，不写源文件或自动执行导入数据；相对程序路径锚定应用目录，多文件通过 argv 独立参数传递；不提供隐式管理员启动。
- 来源：[Q-Dir FAQ 46](https://softwareok.com/?faq=46&seite=faq-Q-DIR)、[Program Starter 专页](https://www.softwareok.com/?seite=Freeware/Q-Dir/Programmstarter_in_Q-Dir)、[Q-Dir 公开号面菜单和能力清单](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)。

## 2026-10-25：补充 Program Starter 官方字段与占位符证据

- Q-Dir FAQ 46 的示例明确了行结构 `Name=Path=Arguments=Work directory`、允许相对程序路径、引号包围带空格的路径参数，并列出 `%curdir%`、`%sel_files%`、`%all_files%`、各窗格变体及 `%windir%`、`%sysdir%`、`%desktop%`、`%appdat%`、`%personal%`、`%drive%`。其后还注明从 6.46 起可用 `%fdir%` 作为所启动文件的父目录。
- Q-Dir 官方 English-UK 字串表把管理入口称为 `Programs`，描述数据保存在 `Q-Dir.ini`，并再次列出相对路径和部分参数占位符；它是 UI 文案/格式说明，不包含可验证的 INI 节名、键名、转义或多记录布局。因此补全草案的 `%fdir%` 范围，但不据字串表推断 Q-Dir.ini 文件导入解析器。
- 依据：[Q-Dir FAQ 46](https://www.softwareok.com/?faq=46&seite=faq-Q-DIR)、[官方 English-UK 字串表](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English-UK)。

## 2026-10-25：补充文件操作后焦点恢复轮询诊断

- 焦点 trace 现在记录每轮恢复尝试的目标状态、视图是否仍匹配、用户输入代数、重试窗口、Shell 内容/项目焦点判定、是否调用恢复及已等待时间。此前“内容/项目焦点判定为已聚焦”时恢复器会静默跳过，难以在用户按方向键失败时区分门控误判与 Shell 加速器问题。
- Release SettingsSmoke 与 App Release 构建通过（0 警告、0 错误）。准备了当前源码隔离 GUI 副本 `src/.state/gui-smoke/20261025-093106-030-a99f0f/`，manifest 为 `prepared-not-run`，包含 trace 指引且 EXE/DLL 哈希匹配；没有启动窗口。此改动只提高诊断能力，未证明或修复用户报告的可见焦点故障。

## 2026-10-25：隔离 IShellView 颜色原型复测

- 通过 `scripts/native-probe-watchdog.ps1 -Probe color -TimeoutSeconds 40` 运行隔离默认 Shell 视图；进程退出码 0，`parent-visible=False`、`foreground-unchanged=True`、`product-untouched=True`。八种原生视图模式及背景/项目核心菜单继续可查询。
- 收到 21 次整体预绘制与 17 次逐项通知，但只有平铺模式出现两条项目通知；矩形为零、裁剪区为 `NULLREGION`、目标色像素为 0。该结果不证明可见窗口无法着色，也没有颜色接入或生产视图迁移依据。屏幕内隔离像素验收仍待后续可用时段，详见[隔离原型记录](ishellview-color-spike.md)。

## 2026-10-25：按用户选择复跑隔离 IShellView 原型

- NativeProbe Release 构建通过（0 警告、0 错误），隐藏 watchdog 运行退出码 0；八种 Shell 查看模式、背景/文件菜单、`IFolderView2`、选区上下文菜单与 `IDropTarget` 查询均成功，前台句柄未变，产品 ExplorerBrowser 未创建。
- 本轮仍只在平铺模式收到两条逐项通知；通知矩形为零、裁剪区为空、八种视图目标色像素为 0。排序/分组后的文件身份映射正确，但没有新增着色证据。按当前不操作可见桌面的约束，颜色仍需之后屏幕内隔离采样验证，不迁移产品视图架构。详见[隔离原型记录](ishellview-color-spike.md)。

## 2026-10-25：大小显示格式与 F9 数量配置预览版

- 大小显示选项新增 F9 文件/子目录数量显示开关，默认开启；关闭后仍保留无法读取/跳过提示。发布隔离脚本及 smoke 加入 `SizeDisplay.ini`，先由回归测试复现遗漏，再修复脚本。
- Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；打包 `releases/preview-0.6.0-temp.55/`，核对产品版本与 EXE/DLL SHA-256。没有启动 GUI；选项菜单、数字格式和 F9 状态栏仍待可见验收。
- 隔离验收清单新增单位/精度、千位分隔、三个大小显示区同步、F9 数量切换和重启持久性步骤；`src/.state/gui-smoke/20261025-092756-953-3bc524/` 状态为 `prepared-not-run`，哈希匹配。本轮没有启动窗口。

## 2026-10-25：核对 ExplorerBrowser 回调与文件项着色边界

- Microsoft 文档将 `IExplorerBrowserEvents` 定义为导航和视图创建通知；`OnViewCreated` 给宿主一个可调整的 `IShellView`。当前产品回调据此设置 Shell 文件视图选项并挂接视图 HWND。现有 `ICommDlgBrowser3` 接线处理视图状态、列点击和筛选，也没有逐项绘制回调；ExplorerBrowser 会按当前 Shell 位置创建相应视图，宿主通知本身不承诺 ListView `NM_CUSTOMDRAW`。
- 因此没有找到可直接接入生产 `ExplorerBrowser` 的公开逐项颜色接口。继续保留原生宿主，不通过子类化假设 DirectUI 内部控件、也不做生产视图迁移。颜色原型仍需屏幕内隔离采样证明真实绘制和 Shell 操作保持；来源见 [IExplorerBrowserEvents](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowserevents)、[IExplorerBrowser](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser) 与[隔离颜色原型](ishellview-color-spike.md)。

## 2026-10-25：复核隔离颜色原型的 Shell 公开回调入口

- 查阅 Microsoft Learn `IShellFolderViewCB` / `SFV_CREATE` 与 Windows SDK 10.0.26100.0 的 `ShlObj_core.h`：`IShellFolderViewCB` 是系统文件夹视图的通知回调，SDK 列出的 `SFVM_*` 消息没有逐项绘制或颜色命令；通用 ListView `NM_CUSTOMDRAW` 可设置项目颜色，但不保证所有 Shell 视图模式稳定提供有效绘制区域。
- 这没有推翻隔离 `IShellView` 中的 ListView 自绘候选，也没有构成屏幕颜色证据；暂不将 `IShellFolderViewCB` 当作受支持的着色接口。生产 ExplorerBrowser 未改。当前仍需一次屏幕内无遮挡隔离采样，确认真实行矩形、颜色像素及原生选择/操作保持，再评估宿主迁移。详见[隔离颜色原型](ishellview-color-spike.md#2026-10-25-ishellfolderviewcb-公开回调范围核对)。

## 2026-10-25：补录 Q-Dir Paste Special 差异

- SoftwareOK 官方 FAQ 145 确认 Q-Dir 可将剪贴板图片另存为 PNG/BMP/JPG，或将文本另存为 UTF-8/UTF-16/ANSI，并提供 Ctrl+Shift+V。源码检索未发现 Explorer Dirs 对应入口；普通 Ctrl+V 已是当前 Shell 的文件粘贴，不能复用来冒充。
- 将 Paste Special 补录为 P2 路线图项，范围限定为明确支持的图片/文本格式、非覆盖命名和当前可写文件系统目录；普通 Ctrl+V 保持 Shell 原生路径。

## 2026-10-25：实现 Q-Dir Paste Special

- 编辑菜单新增 Paste Special 子菜单和 Ctrl+Shift+V。按调用时剪贴板内容展示图片 PNG/BMP/JPG 与文本 UTF-8/UTF-16/ANSI 选项；只在用户显式打开入口/选择格式时读取剪贴板。UTF-8 不写 BOM，UTF-16 写 LE BOM，ANSI 使用 Windows 当前 ANSI 代码页并拒绝不可表示文本。新文件使用本地时间戳名和原子 `CreateNew` 冲突编号，不覆盖已有文件。
- 仅允许当前可写文件系统目录。创建后向 Shell 发送 PIDL 创建通知（覆盖长路径），并在当前标签/目录/导航代际/原选区仍有效且窗格仍可取焦时短暂重试选择新项目；用户继续操作、切换目录或切换视图时取消陈旧选择请求。
- Shell PIDL 解析/通知可能重入导航；现于调用前捕获浏览器、目录、导航/视图/选区/输入代际，并在 Shell 返回后复核，防止旧请求覆盖新窗格状态。选择调用中若发生重入，新建请求不会被旧回调取消，真实用户输入也会阻止后续焦点恢复。
- 新增离线 smoke，验证三种图片编码可解码、文本编码边界、ANSI 有损转换拒绝、同名文件保留，以及浏览器/目录/导航/视图/选区/输入代际失效时拒绝旧选择。专项/完整 SettingsSmoke 通过，App Release 构建 0 警告/错误。没有读取用户剪贴板或启动 GUI；菜单真实显示、剪贴板提供程序互操作、Shell 实时更新与选中/焦点仍待隔离可见验收。

## 2026-10-25：为自然收敛观察增加计数时间线

- HostProbe 的 `--live-shell-directory-stress*` 输出新增 250 ms 计数样本：记录阶段目标数、Shell 视图实际项目数和 watcher 批次的预期/待处理状态；native-only 对照也可直接比较目标与实际计数，用于观察计数短暂停滞后是否自然追上以及 fallback 前后的时序。产品刷新策略未改变。
- App 与 HostProbe Release 构建通过（均为 0 警告、0 错误），全量 Release SettingsSmoke 通过；本轮未运行 Shell 宿主探针或 GUI，因此尚无自然收敛分布数据。之后按现有隔离压力方案采集创建/删除样本，再据数据评估 fallback 等待窗口。

## 2026-10-25：打包临时预览版 temp.54

- 按项目发布规则以 `scripts/publish.ps1 -Version preview-0.6.0-temp.54 -IsolatedProfile -ReleaseNotes docs/releases/preview-0.6.0-temp.54.md` 生成 `releases/preview-0.6.0-temp.54/`。程序集产品版本 `0.6.0-preview.temp.54+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE SHA-256 `CC1C17D1635E5B122886F539B5313BEC84E25CB3AFB8CB66246E13F78DFC8D6D`，DLL SHA-256 `55BFD200DD5912669A1311C0E9F81BDC8D23EA5212A077B7B0AFC0D99C79AB42`，均与包内清单一致，包内独立 profile 存在。
- 发布隔离 smoke 通过；包内说明涵盖 F9 进度节流、F10 菜单快捷键和 F2 诊断，以及未完成的可见验收。没有启动发布版 GUI；不要视为日常使用验收通过。

## 2026-10-25：为 F10 接线准备当前源码验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly -CaptureFocusTrace` 生成 `src/.state/gui-smoke/20261025-081927-799-79f725/`；在 F2、焦点、右键、Ctrl+滚轮、Q-Dir 与颜色步骤外，新增“文件区聚焦时 F10 打开文件菜单，Esc 关闭且不执行命令，再次 F10 可重新打开”的验收步骤。
- 清单包含 13 项待验收、10,000 文件的独立 F9 进度夹具和焦点 trace 路径；状态为 `prepared-not-run`，EXE/DLL 哈希相符。只构建和准备副本，没有启动窗口。

## 2026-10-25：补齐 F10 菜单栏快捷键

- Microsoft 的 Windows / File Explorer 快捷键清单列出 F10 可激活当前应用菜单栏。当前 WPF 菜单此前没有应用级 F10 路由；现将无修饰 F10 映射为打开主菜单栏的“文件”菜单，Alt+F10 不拦截。
- 新快捷键 smoke 先因缺少映射而失败，接线后完整 Release SettingsSmoke 通过；App Release 构建 0 警告/错误，`git diff --check` 通过。菜单真实焦点、按键关闭和可见行为仍待 GUI 验收。
- 参考：[Microsoft：Windows 中的键盘快捷键](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows)。

## 2026-10-25：为 F9 进度提示补充大目录验收夹具

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly -CaptureFocusTrace` 生成 `src/.state/gui-smoke/20261025-081600-006-ca0a4f/`。新增独立 `recursive-size-progress` 夹具，实际核对包含 10,000 个文件；正常四窗格初始目录不受其影响。验收清单新增 F9 进度递增、完成总数、再次触发取消及导航后不回显旧进度的步骤。
- 清单状态为 `prepared-not-run`，EXE 和 App DLL 哈希均与实际文件一致，包含 F2 多窗格方向键和 `focus-trace.log` 验收步骤。本轮未启动窗口或使用 GUI。

## 2026-10-25：准备当前源码隔离桌面验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly -CaptureFocusTrace` 从当前源码生成 `src/.state/gui-smoke/20261025-081458-776-d4a481/`。包含四个独立测试窗格和 11 项待验收步骤，覆盖 F2 四向键/文本编辑、文件操作后的焦点及当前待验收功能。
- 清单状态为 `prepared-not-run`；EXE 与 App DLL SHA-256 均与 `preparation.json` 相符，含 `focus-trace.log` 目标路径。本次仅构建并准备副本，没有启动窗口或操作桌面。

## 2026-10-25：多窗格重命名箭头路由诊断补充

- 当前实现会将焦点位于原生 `Edit` 控件时的四向箭头直接发送给该编辑控件，并消费消息，避免 WPF 把箭头解释为窗格焦点导航；现有 SettingsSmoke 只验证该判定函数，不能证明真实 ExplorerBrowser 多窗格的 HWND 焦点和消息顺序。
- 为现有隔离焦点跟踪补充 `rename-arrow-forward` 记录，包含键值、修饰键、当前宿主/视图/编辑框 HWND 和发送后的焦点 HWND。只有隔离副本显式启用 `EXPLORER_DIRS_FOCUS_TRACE` 时记录；本轮未运行 ExplorerBrowser/GUI，未宣称问题已修复。后续应在四窗格各执行 F2 与四向键，并确认每个箭头都命中对应编辑框、不会转为窗格间选择。

## 2026-10-25：递归目录进度更新节流

- F9 扫描器仍每 256 项产生一次进度样本，状态栏投递增加 250 ms 单调时钟节流，避免超大目录把大量 WPF Dispatcher 操作排队；快速完成的小目录仍由最终汇总直接反馈。
- 节流状态 smoke 覆盖首次立即上报、间隔内抑制、间隔到期恢复；SettingsSmoke 全量通过，App Release 构建 0 警告/错误，相关 `git diff --check` 通过。GUI 状态栏响应尚待验收；没有启动 GUI。

## 2026-10-25：本机 Q-Dir 状态文件只读导入验证

- 使用现有 `--inspect-qdr` 入口只读加载三份本机 Q-Dir 状态文件：44、28、44 个标签均成功解析，布局均为四窗格；识别到 44、24、40 份原生视图快照。预览准确列出未识别字段、暂定树开关解释、未应用的 `TreeNsplit`/`SPT`/`VorschauPos` 及未导入颜色规则等六类限制。
- 新增 `--roundtrip-qdr` 诊断，将导入状态交给本程序导出器后再解析；三份真实样本的 Explorer Dirs 会话快照均逐字匹配，窗格、标签和原生视图快照数量一致。剥离 `ExplorerDirs.*` 扩展后再导入，Q-Dir 基础字段中的目录/标签/选择、视图快照和分隔比例也逐项匹配。完整 Release SettingsSmoke、App/NativeProbe 构建通过（0 警告/错误），`git diff --check` 通过。
- 未改写源配置文件、用户会话或启动 Q-Dir。该验证证明本程序扩展状态的应用内往返，不证明 Q-Dir 可读取导出结果，也不验证 GUI 应用后的真实 Shell 视图；详细记录见[Q-Dir 导入调查](qdr-import.md#2026-10-25-本机-q-dir-状态文件只读导入验证)。

## 2026-10-25：刷新后瞬态空视图的稳定计数恢复

- 两轮隔离生产宿主复跑稳定重现：Shell 刷新回调连续读到 0 项后，列表恢复为 4 项，而批次仍保留合成的 5 项目标；旧逻辑会再次刷新并最终未按预期恢复。新增状态回归先失败，后验证“稳定的非零计数等待短暂安定后重建基准”，避免重复空视图循环。
- 完整 Release SettingsSmoke 通过；App/HostProbe Release 构建 0 警告/错误。修正后的隔离生产宿主通过：计数失配基准恢复、刷新中 watcher 事件、选择和独立焦点保留，以及跨窗格复制/移动/删除、同目录重命名和文件夹创建/删除。
- 复核发现此前探针仅通过 Shell COM 设置全选，却未让生产选区快照与该状态同步；焦点失败因此是无效基线。本次探针显式建立“多选 + 未选焦点项”，并等待产品快照结算，之后 `selection-restored=True`、`focus-restored=True`。这些隐藏操作没有使用系统剪贴板，不等同真实 Ctrl+V 或可见桌面验收。
- 已打包独立 profile 预览 [temp.53](../releases/preview-0.6.0-temp.53/)；打包隔离 smoke、EXE/DLL 哈希和发布说明复核通过。未启动发布程序或 GUI。

## 2026-10-25：准备当前源码的隔离桌面验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly -CaptureFocusTrace` 生成 `src/.state/gui-smoke/20261025-074906-405-38e4f2/`，包含四个独立窗格、23 个隔离测试文件、最小 `.qdr` 颜色诊断基线及待验收步骤。清单状态为 `prepared-not-run`；EXE/DLL SHA-256 与清单一致。本次构建产品版本标记为 `0.6.0-preview.temp.3`，源码修订标识与 `temp.52` 记录一致。
- 未启动 GUI。进程检查发现 `temp.2` 仍在运行，启动时间为 2026-10-23；未对该进程作任何操作。后续只有在桌面空闲并获得明确授权后，才运行本副本的手工验收步骤。

## 2026-10-25：为文件操作后焦点问题准备路由跟踪

- 源码线索显示，文件操作后的重试器会在 HWND 与 Shell 项目索引看似有效时跳过 `FocusView()`；窗口重新激活时则会调用 `FocusView()`，其中包含 `IShellView.UIActivate(SVUIA_ACTIVATE_FOCUS)`。这与“Alt+Tab 后方向键恢复”相符，但因果尚未通过真实窗口复现，暂不改产品焦点行为。
- `src/ExplorerDirs.GuiSmoke/run.ps1` 新增显式 `-CaptureFocusTrace` 开关：隔离测试进程可将已有 `EXPLORER_DIRS_FOCUS_TRACE` 诊断输出写到本次运行目录，记录按键、`TranslateAccelerator` 结果、焦点 HWND 类和 Shell 项目焦点。默认不启用。`-PrepareOnly -CaptureFocusTrace` 已生成 `src/.state/gui-smoke/20261025-072545-496-cb700a/`；清单为 `prepared-not-run`，确认 trace 路径和验收步骤存在，且尚未创建日志或启动窗口。
- 下次获准测试时，按粘贴/删除后箭头步骤重现并比较日志；若重新激活调用与键盘恢复同步出现，再决定是否调整生产重试策略。当前证据只提高可诊断性，不是修复或 GUI 验收通过。

## 2026-10-25：隔离 IShellView 颜色原型隐藏复跑

- 按用户选择复跑 NativeProbe 的隔离逐项颜色原型：Release 运行退出码 0，完成八种查看方式切换与原生菜单查询，逐项颜色处理器收到 17 次通知；只有平铺模式出现两条项目通知。通知矩形仍为空、HDC 裁剪区为 `NULLREGION`，实际行像素/目标色像素均未采到；排序/分组下没有逐项通知。父窗口保持隐藏、前台未变、产品未触碰。
- 本轮遵守继续源码工作、不使用 Computer Use 的约束，没有启动可见窗口。颜色可行性结论仍未定；下一步需要获准的屏幕内无遮挡隔离采样，若像素证据成立，再评估独立 `IShellView` 宿主与产品 ExplorerBrowser 的功能差距。详情见[隔离颜色原型](ishellview-color-spike.md#2026-10-25-隐藏探针复跑)。

## 2026-10-25：文件操作后焦点恢复验证 Shell 项目焦点

- 源码复核发现焦点重试器此前只检查 Win32 焦点 HWND 是否位于内容窗口，列表 HWND 仍持有焦点、但 `IFolderView.GetFocusedItem` 已无有效项目索引时会被误判为恢复完成。现将非空目录中的项目焦点纳入恢复条件；空目录仍允许内容窗口持有键盘焦点。恢复项目时优先使用 Shell focused item，索引无效时回退到有效 selection mark，并保留该项原有选中位。
- 新增纯状态回归覆盖内容 HWND 与 Shell 项目焦点分离、空目录、focused/marked 索引选择及越界回退。测试先因缺少新状态判定失败，调整后完整 Release SettingsSmoke 通过；App Release 构建 0 警告/错误。没有运行 ExplorerBrowser 或 GUI，因此真实粘贴/删除后的方向键体验仍待可见验收。
- 复核发现 NativeProbe 单独链接生产 `ShellSelectionCommands`，未链接现在共用的焦点恢复门，导致该目标无法编译；已补齐链接并构建通过。默认 `IShellView` 选区/焦点矩阵复跑退出码 0，前台未变、父窗口不可见。另尝试仅选择、不设置 `SVSI_FOCUSED` 来制造“有 selection mark、无 focused index”，Shell 仍返回相同 focused index；因此原生探针没有验证该回退分支，不能据此宣称生产故障已解决。
- 再追踪用户报告的 Alt+Tab 后恢复现象：`MainWindow.Activated` 会在活动窗格仍希望文件焦点时调用 `FocusCurrentView()`，而文件操作重试门若已观察到内容 HWND 和 Shell focused index，就跳过这次 `FocusView()`。这提供了一个尚待实测的解释：真实问题可能是 Shell 视图键盘激活/加速路由失效，而不是项目焦点索引缺失。验收步骤现要求失败时 Alt+Tab 离开再返回并复试方向键，以区分两种情况；当前不能据源码推断已修复。
- 已用 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 基于当前源码生成最新隔离副本 `src/.state/gui-smoke/20261025-072102-337-2750ab/`；待验收要求每个窗格粘贴后和 Shift+Delete 确认后不点击鼠标直接按方向键；若失败，再 Alt+Tab 离开/返回复试并记录结果。清单状态及 EXE/DLL 哈希需在验收前复核。

## 2026-10-25：自动刷新 P0 长尾边界复核

- 对照自动刷新源码、状态 smoke 和最新隐藏 50,000 项记录，应用目录计时器、Shell 计数读取、`IShellView.Refresh()` 与队列处理均不能解释约 972 ms 的单次停顿；同一时间窗的延迟定位在 `SHELLDLL_DefView` 私有 `WM_USER` 消息处理。该消息没有公开语义，不能安全拦截或改写；现有 watcher/刷新重试也已有纯状态覆盖。
- 本轮未发现新的可安全源码修复，因此不增加猜测性消息处理。自动刷新 P0 仍开放；继续定位需要获得可见窗口/真实 Shell 运行时测量，或新的可复现应用侧耗时证据。详细测量边界见[自动刷新调查](live-update-investigation.md#2026-10-25-shell-defview-私有消息占用-sta-的长尾)。

## 2026-10-25：右键目标命中失败时不再从 UIA 行范围猜测

- 复核发现，列表窗口缺少 `SysListView32` 命中能力时，探测器会把 UI Automation 的 `ListItem` 祖先当作文件项。详细视图中该边界可能覆盖整行，导致点在行尾空白处时仍向残留选中文件添加自定义命令。现改为命中接口不可用时返回 `Unknown`，菜单注入器因此保留原生 Shell 菜单但不添加目标不明的自定义命令。
- 新回归先因缺少 fail-closed 判定而构建失败，修复后专项右键目标 smoke、完整 Release SettingsSmoke、App Release 构建（0 警告/错误）和 `git diff --check` 通过。没有启动 GUI；生产视图里的实际空白点击与菜单显示仍待可见验收。

## 2026-10-25：为 temp.51 准备最新隔离 GUI 验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 从当前源码生成 `src/.state/gui-smoke/20261025-070858-356-8b6091/`。清单状态为 `prepared-not-run`，共 11 项待验收，四个窗格分别有 8/5/5/5 个隔离文件。
- 将原先笼统的“复制/删除后焦点返回”改成可直接判定的步骤：每个窗格粘贴后立即按方向键，再执行 Shift+Delete 并确认后立即按方向键；两步都必须在不点击的情况下移动同一窗格中的文件焦点/选择。源码追踪未能证明这条时序已修复，现有焦点恢复状态逻辑也不能替代真实按键验收。
- EXE/DLL SHA-256 与清单复核一致；应用进程数为 0。脚本在加载 UI Automation 和窗口操作前结束，本轮未打开窗口或发输入。此隔离副本包含 temp.51 右键目标修正，但不是可见验收通过证据。

## 2026-10-25：覆盖刷新计数读取期间到达的 watcher 事件

- 复核异步刷新完成时的队列排空与 Shell 项目数读取顺序。两者之间若有新 watcher 事件，队列会保留该事件并安排下一轮派发；已有实现没有丢事件，但此前 smoke 未覆盖这一精确交错。本次补充状态回归，验证刷新基线由 4 更新到 5 后，后到事件仍使预期数变为 6。
- 针对性目录批次 smoke、完整 Release SettingsSmoke、App Release 构建（0 警告/错误）和 `git diff --check` 通过。未运行 ExplorerBrowser、HostProbe 或 GUI；实际 Shell 刷新及窗口行为仍待验收。

## 2026-10-25：Shell 刷新负 HRESULT 不再误报成功

- 源码复核发现 `GetCurrentView` 或 `IShellView.Refresh()` 返回负 HRESULT 时，旧路径会调用刷新完成回调；浏览器宿主不可用时也会走同一假完成路径。自动刷新因此可能重置失败状态、丢掉强制重试；手动刷新也没有可靠错误提示。现将负 HRESULT 转为保留原代码的 `COMException`，宿主不可用则抛出失败：自动目录刷新进入已有失败退避，手动刷新显示窗格错误，筛选恢复刷新报告失败而不再运行成功续接。
- 新增回归覆盖成功与失败 HRESULT；先在缺少失败转换时编译失败，修复后全量 Release SettingsSmoke 通过，App Release 构建 0 警告/错误，`git diff --check` 通过。没有运行 ExplorerBrowser/GUI；真实 Shell HRESULT 和异步刷新/重试行为仍待宿主验收，自动刷新 P0 继续开放。详见[刷新调查](live-update-investigation.md#2026-10-25-shell-刷新负-hresult-不再被当作完成)。

## 2026-10-25：修正 Q-Dir 每标签查看方式值 8 的误接收

- 复查 Q-Dir 官方 FAQ 117 的德文更新版后，发现它明确列出标签级 `ViewModeN` / `ViewModeN_i` 为 `FVM_ICON=1` 至 `FVM_THUMBSTRIP=7`，并给出 Q-Dir 字段的 `FVM_LAST=7`。旧导入器错误地接受 8 并按 Windows Shell 的 Content 模式载入；现将 8 与其它越界值一样回退到详细信息并提示。Windows 自身的 `FOLDERVIEWMODE` 仍把 Content 定义为 8；导出 Content 写兼容的详细信息 4，Explorer Dirs 扩展保留本程序的 Content 状态。
- 新回归先以预期错误失败，收紧到 1～7 后通过。全量 SettingsSmoke 与 App Release 构建通过，差异空白检查通过；Q-Dir 实际读取导出文件及可见视图仍未验证。[Q-Dir FAQ 117（德文，2025 更新）](https://softwareok.de/?faq=117&seite=faq-Q-DIR) · [Microsoft FOLDERVIEWMODE](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderviewmode)

## 2026-10-25：全量源码回归复核

- 本轮全量 Release `ExplorerDirs.SettingsSmoke` 通过；App 和 NativeProbe Release 构建均 0 警告/错误。该证据覆盖纯状态、配置往返和隐藏原生探针，不替代 Shell 窗口中的物理输入、菜单、焦点或可见绘制验收。
- 剩余的 P0/P1 主要是需要真实窗口确认的 Shell 交互；命名查看预设的设计与代码已落地，但实际列头命中、菜单/焦点和应用效果仍待可见验收。

## 2026-10-25：Shell 计数不可读时避免刷新循环

- 源码追踪发现目录变化协调器在 Shell 项目数为 `null` 时仍会不断发起全视图刷新；异步刷新完成仍读不到计数时，250 ms 计时器会再次走相同路径。现在普通刷新在计数不可读时等待下一次计数读取，保留待处理的 watcher 增量；Watcher Error/重命名等强制刷新仍绕过等待。
- 新增 `--shell-directory-change-batch-smoke` 纯状态入口。回归先按旧实现失败，再验证不可读计数延后、计数恢复后继续按差异兜底，以及强制刷新语义。未启动 ExplorerBrowser、GUI 或 HostProbe；真实 Shell 延迟、可见更新和慢速/网络目录仍待验收，自动刷新 P0 不关闭。详见[调查记录](live-update-investigation.md#2026-10-25-shell-计数不可读时避免重复全视图刷新)。

## 2026-10-25：补齐十二种布局的 Q-Dir 树开关映射回归

- 对照 `QdrImporter` / `QdrExporter` 的 pane permutation 后确认 `Tree1`～`Tree4` 使用同一布局映射，并与各窗格的标签顺序一起重排。既有十二布局测试只断言目录、标签和活动项，未断言树开关；现扩展全部十二个原生布局用例，分别验证导入重排及导出反向映射。未发现产品代码缺陷。
- 新增纯状态 `--qdr-layout-smoke` 入口，Release smoke 通过，App Release 构建 0 警告/错误，空白检查通过。树字段仍按未通过 Q-Dir 可见互操作确认的暂定 0/1 解释；本轮没有启动 Q-Dir、ExplorerBrowser 或 GUI。

## 2026-10-25：补录 Q-Dir 命名列布局预设缺口（历史状态）

- 对照 Q-Dir 官方 FAQ 137，Q-Dir 可在列头 Ctrl+右键菜单保存和载入有名称的列/查看方式组合，并保留适合不同媒体类型的列集及宽度；现有 Explorer Dirs 只按标签保存当前列设置，没有同类命名预设。该功能归入 Shell/视图功能阶段，暂列 P1。进入实现前需先设计预设包含的状态范围（公开说明至少提及查看方式与列，不足以确认是否含排序/分组）、保存位置与冲突处理，以及如何将自定义动作接入列头菜单而保留 Shell 原生选项；本轮只更新差距记录，未改产品代码。[Q-Dir FAQ 137](https://www.softwareok.com/?faq=137&seite=faq-Q-DIR)
- 继续核对官方 FAQ 28、14 和帮助页后，作用域有了更多证据：Q-Dir 的每个 Q-View 独立管理列，并考虑工作区/系统/普通文件夹类型；FAQ 137 的命名预设明确包括查看方式、列和列宽；FAQ 14 描述把一个视图的列设置传到其他视图。公开文字仍没有精确列出“视图设置”是否包含排序/分组，也未说明命名预设的全局或每窗格存储位置。本项目既有明确需求是每标签独立保存当前状态；新增命名预设可先按用户可见的选择/应用流程设计，不能把 Q-Dir 的每目录历史推断成必需的应用存储规则。[FAQ 28](https://www.softwareok.com/?faq=28&seite=faq-Q-DIR) · [FAQ 14](https://www.softwareok.com/?faq=14&seite=faq-Q-DIR) · [Q-Dir 帮助](https://www.softwareok.com/?seite=Freeware/Q-Dir/Help+file)

### 后续实现状态

命名查看预设现已实现：保存在 profile 级 `ViewPresets.ini`，包含查看模式、图标大小、可见列顺序与宽度；不覆盖每标签自身的排序/分组。顶部“视图”菜单提供保存、应用、覆盖、删除，列标题 Ctrl+右键提供快捷保存/应用入口；实现与 smoke 覆盖见[Shell 对照表](shell-parity.md#排序分组列设置)。SettingsSmoke 已覆盖存储往返、无效/未来格式保护和并行修改冲突。实际列头命中、原生菜单保留、焦点与跨重启可见行为仍待 GUI 验收，因此该 P1 仅代码完成，不作体验验收完成声明。

## 2026-10-25：Q-Dir 导入预览提示未识别字段

- 源码复核发现 importer 会忽略未识别键且不提示。现预览将未知字段名按编号家族折叠后汇总，最多显示 8 个名称，不输出字段值。本机 `start.qdr` 只读解析确认出现 `ColsID`、`ViewModeFlags` 提示，44 个标签、各窗格数量及 44 个视图快照计数保持；两个样本的 `ViewModeFlags` 值均为 `1075838981`（`0x40200005`），公开 FAQ/帮助未找到字段语义。后续结构比对确认两份样本共有 64 个可配对的标签级 `ColsID` / `ColsAndSort`，其中 `start.qdr` 还含 4 组窗格级字段；哈希关联显示两者并非一对一对应，详见本文件后文。暂不猜测或应用这些字段。单测先按旧行为失败，新增提示后通过。全量 Release SettingsSmoke、App Release 构建（0 警告/错误）和差异空白检查通过。没有启动 GUI 或 Q-Dir。

## 2026-10-25：Q-Dir 导出不支持的 Content 查看模式

- `.qdr` 导出审查发现应用 Content 模式若直接写入基础字段，Q-Dir 的公开功能清单未说明能否处理。现 `ViewModeN` 和 `ColsAndSortN` 视图流均回退为详细信息模式 4，Explorer Dirs 扩展仍保存模式 8；本程序再次导入可恢复完整状态。回归先因视图流仍含模式 8 而失败，修正后专项导出/导入 smoke、全量 SettingsSmoke、App Release 构建（0 警告/错误）与差异检查通过。没有启动 Q-Dir 或 GUI，Q-Dir 实际读取仍待验证。

## 2026-10-25：准备当前源码的四窗格核心交互验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 已从当前 Release 源码生成隔离副本 `src/.state/gui-smoke/20261025-014347-865-b9de8b/`，清单状态为 `prepared-not-run`。四个窗格使用独立测试目录，含 8/5/5/5 个临时文件；待验收覆盖文件操作后焦点、四窗格重命名方向键、先选项目再空白处右键、跨窗格复制/移动、Tab 正反遍历、地址历史、Ctrl+滚轮、Q-Dir 导入/导出及黑底颜色变化定位。EXE 与 DLL SHA-256 已与清单核对一致。脚本在启动 UI Automation 或窗口前退出；本轮未启动应用、未操作桌面。该副本用于后续桌面验收，不是通过证据。

## 2026-10-25：核验隐藏颜色绘制通知的 HDC 有效性

- 隔离 NativeProbe 的 `NM_CUSTOMDRAW` 诊断现用 `GetObjectType` 区分通知设备上下文与采样位图：通知 HDC 返回 0（无效 GDI 对象），目标位图 DC 返回 10（有效内存 DC）。隐藏 Shell 的绘制回调没有提供可读的绘图上下文，因此此前目标色像素为零不能用于判断行着色可行性。NativeProbe Release 构建 0 警告/错误，隐藏探针退出码 0；父窗口不可见、前台不变，没有启动生产 ExplorerBrowser。真实行绘制仍需之后的可见隔离采样；产品文件视图未改。详见[隔离颜色原型](ishellview-color-spike.md#2026-10-25-再次复跑隐藏颜色原型)。
- 采样器已在扫描 DIB 像素前调用 `GdiFlush`，确保当前线程已提交 `BitBlt` 等 GDI 绘制。Release NativeProbe 构建和隐藏复跑通过；本轮刷新返回成功，但通知 HDC 仍无效、目标像素仍为零，因此只修正采样同步，不改变颜色路线证据边界。依据：[Microsoft `GdiFlush`](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-gdiflush)。

## 2026-10-25：复跑隔离 IShellView 行颜色原型

- 用户选择继续用隔离默认 `IShellView` 原型核验 Q-Dir 式逐项颜色能否保留 Shell 原生操作。本轮在隐藏、独立 NativeProbe 子进程复跑，退出码 0；父窗口保持不可见、前台句柄未变，生产 ExplorerBrowser 未启动。八种原生查看方式、菜单能力、选择接口和 `IDropTarget` 均可用。颜色处理器收到的绘制通知仍未连到有效像素采样 DC：按模式采样仅平铺视图出现两条逐项通知，矩形为零，目标颜色像素为零。详见[隔离 IShellView 颜色原型](ishellview-color-spike.md#2026-10-25-再次复跑隐藏颜色原型)。下一步保留为之后的可见隔离桌面验收；当前不以此结果改动产品视图架构。
- 复核探针和生产宿主源码后补充架构边界：探针 `StreamViewHost` 的 `IShellBrowser` 菜单/状态回调是空实现、导航返回不支持、快捷键交回 Shell、视图流只在内存保存；它未提供产品的 `ICommDlgBrowser3` 筛选/通知路径及 ExplorerBrowser 生命周期事件。因此可见像素实验即便成功，也只证明隔离视图的着色钩子；生产替换仍需单独迁移导航、筛选、每标签状态/历史、快捷键、预览和 COM 释放行为。App 与 NativeProbe Release 构建通过（0 警告/错误）；没有运行可见模式。

## 2026-10-25：文件操作后从列标题恢复到文件内容焦点

- 源码路径检查发现，Shell 文件操作焦点轮询把“Shell 视图任意子窗口有焦点”当成“文件内容区有焦点”；因此焦点落在原生列标题时不会尝试恢复。另一个 `FocusView()` 路径又因列标题属于内容容器的子窗口而跳过 `SetFocus`。现按实际内容 HWND 判断焦点，明确将列标题排除在“文件内容已聚焦”之外，并在操作后的恢复中把列标题焦点送回内容容器。仍保留重命名 Edit、应用控件焦点、导航/标签变化、用户新输入和其他应用前台的保护。
- `ShellFocusRestoreGateSmoke` 新回归先因缺少焦点分类与重定向策略而构建失败；接线后覆盖文件项、列标题、其它 Shell 子窗口和已聚焦内容四种情况。全量 Release SettingsSmoke 与 App Release 构建通过，0 警告/错误，相关 diff 检查通过。没有启动 ExplorerBrowser 或可见窗口；因此修正了源码路径和状态策略，但真实列标题焦点迁移及 Ctrl+C/V、删除后的物理方向键体验仍待隔离桌面验收。
- 已按独立 profile 打包临时预览 [`preview-0.6.0-temp.42`](../releases/preview-0.6.0-temp.42/)；发布隔离 smoke 通过。没有启动发布 EXE，故此包尚未做真实窗口验收。

## 2026-10-25：多选恢复 fallback 单次清除旧选区

- 单项 `DESELECTOTHERS` 候选曾在未分组八种查看模式通过；新增分组覆盖后发现它和 `GetSelectedItem` 索引清除都会留下其它组的选择，因此撤回该候选。`ClearCurrentSelection` 现枚举当前选区 PIDL，一次调用 `SelectAndPositionItems` 并只传 `SVSI_NOTAKEFOCUS`（`SVSI_DESELECT` 的值为 0）清空整组，再读回选区数量确认为空；不会把 `DESELECTOTHERS` 应用于多 PIDL，也不会误带 `SVSI_FOCUSED`。
- 隐藏 NativeProbe 在分组/未分组 × 八种视图模式下共 16 组测试均通过：每组在对应分组和视图状态下捕获两项选区及焦点 PIDL，选中全部四项后 fallback 精确恢复两项和焦点；直接批量路径也验证焦点恢复；删除一个快照 PIDL 后先恢复唯一幸存项，再删除第二项验证完全失效快照返回未恢复且不留选区。完整 Release SettingsSmoke 与 App Release build 通过，0 警告/错误。未运行 ExplorerBrowser HostProbe 或可见 GUI，也没有证明实际 50k 延迟改善。后续在授权环境比较 replacement fallback 耗时、选区/焦点状态和 Dispatcher 长尾。详见[自动刷新与选区恢复调查](live-update-investigation.md#2026-10-25-分组视图多选恢复-fallback-修正)。

## 2026-10-25：原生视图多选恢复回归与批量标志修正

- 新增隐藏默认 `IShellView` 隔离探针，直接调用生产 `ShellViewSelectionSnapshot`。旧实现能让 `SelectAndPositionItems` 返回成功，却只恢复出一个项目；根因是把 `SVSI_DESELECTOTHERS` 传给多 PIDL 批量调用，该标志逐项应用后会折叠选区。
- 批量调用改为 `SVSI_SELECT | SVSI_NOTAKEFOCUS`，并仍逐项核验完整选区；发现额外残留选区时，fallback 通过 `IFolderView2::GetSelectedItem` 逐项取得当前选中索引并清除，再批量恢复快照，避免将保存 PIDL 映射到视图索引时全目录枚举。测试覆盖两项应保留和两项额外选区应移除。
- Release App 构建、完整 Release SettingsSmoke 和原生探针两次连续运行通过：空选区直接恢复两项；两项额外选区通过 replacement fallback 清除后恢复两项；删除一个保存项目后只留下 1 个幸存项目。父窗口保持隐藏，前台窗口不变。该验证不涉及 ExplorerBrowser、键盘焦点或可见 GUI；已制作独立 profile [`preview-0.6.0-temp.41`](../releases/preview-0.6.0-temp.41/)，可见验收仍待进行。
- 详细记录见[自动刷新与选区恢复调查](live-update-investigation.md#2026-10-25-隔离-ishellview-多选恢复验证与修正)。

## 2026-10-25：temp.39 预览包与选区 PIDL 快速匹配

- 在 `ShellViewSelectionSnapshot` 中加入 `ILIsEqual` 精确 PIDL 匹配；只有二进制 PIDL 不同时才调用 `IShellFolder.CompareIDs(SHCIDS_CANONICALONLY)` 回退。隐藏默认 `IShellView` 探针使用生产快照代码确认选区匹配、恢复成功并跳过批量重选；本样本匹配约 0.6 ms。
- App、NativeProbe、HostProbe Release 构建、完整 Release SettingsSmoke、隐藏原生视图探针与发布隔离 smoke 通过。该结果不证明 ExplorerBrowser 生产宿主或大目录性能收益；隐藏视图焦点未恢复，不作为焦点证据。
- 已发布独立 profile 预览包 [`preview-0.6.0-temp.39`](../releases/preview-0.6.0-temp.39/)，包含 temp.38 的列标题实验；该实验仍待 ExplorerBrowser/GUI 验收。发布说明及哈希见[版本说明](releases/preview-0.6.0-temp.39.md)。

## 2026-10-25：接入所有查看方式的原生列标题

- ExplorerBrowser 初始化前设置 `FVO_CUSTOMPOSITION`；每次 `OnViewCreated` 仅清除 `FWF_NOHEADERINALLVIEWS` 和 `FWF_NOCOLUMNHEADER`，并读回校验，同时保留其它选项/文件夹标志。Shell 不支持时报告一次，继续保留原生文件浏览。
- `FolderHeaderFlagsSmoke` 已改为直接验证生产共享标志逻辑；Release SettingsSmoke 及 App、NativeProbe、HostProbe 构建通过，0 警告/错误。
- 已制作独立 profile 的 `releases/preview-0.6.0-temp.38` 供预览；列头渲染、原生排序/排列和可见焦点尚未验收，不视为正式完成。
- 遵守当前不使用 Computer Use 的约束，没有运行隐藏 ExplorerBrowser 宿主探针或启动 GUI。运行时选项传递、八种查看方式实际列头、排序/排列/选择及菜单行为仍待隔离宿主和可见验收；temp.38 仅为可回退的独立预览。计划见[实施计划](superpowers/plans/2026-10-25-all-view-column-headers.md)。

## 2026-10-25：Q-Dir 全查看方式列标题的原生接口验证

- Q-Dir FAQ 104 确认“所有查看方式显示列标题”是其可切换能力。隔离默认 `IShellView` 查询到 `IFolderViewOptions`，读取选项 `FVO_VISTALAYOUT`（`0x1`）及当前 `FOLDERFLAGS` `0x40200000`；按 Microsoft 要求先尝试启用 `FVO_CUSTOMPOSITION`，Shell 返回 `E_NOTIMPL`，所以没有调用 `SetCurrentFolderFlags` 改列标题位。探针退出码 0、父窗口隐藏、前台不变；这是原生 API 的可用性结果，不是 ExplorerBrowser 行为或列标题可见性的验收。后续需经 ExplorerBrowser 的 `OnViewCreated` 回调应用 folder flags，再在隔离视图验证排序、排列与视图状态；见[Q-Dir FAQ 104](https://www.softwareok.com/?faq=104&seite=faq-Q-DIR)、[Microsoft `OnViewCreated`](https://learn.microsoft.com/en-us/windows/desktop/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowserevents-onviewcreated)及[`SetCurrentFolderFlags`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags)。

## 2026-10-25：未初始化 ExplorerBrowser 接受 `FVO_CUSTOMPOSITION`

- 为区分 Shell view 与浏览器宿主对象的实现，新增不调用 `Initialize`、不提供父窗口/导航目标的 NativeProbe。`CLSID_ExplorerBrowser` 对象查询 `IFolderViewOptions` 成功；`GetFolderViewOptions` 原值为 `0`，开启 `FVO_CUSTOMPOSITION` 后读回为真，随后成功恢复原值。探针退出码 0、前台窗口不变、没有创建窗口。这证明浏览器宿主对象支持设置必要选项，但尚未证明初始化后此选项会传到其 `IShellView`。Microsoft 的 `IExplorerBrowserEvents::OnViewCreated` 文档明确允许在视图显示前修改 folder flags，为应用选项提供了正式接入时机；运行时选项传递、标志读回及排序/排列/选择保持仍未验证。遵守当前“不使用 computer-use”边界，本轮没有创建已初始化 ExplorerBrowser 宿主，也未接入产品。

## 2026-10-25：准备 `OnViewCreated` 列标题隔离探针

- NativeProbe 新增显式 opt-in 的 `--folder-header-options-hosted`：在 `Initialize` 前启用并读回 `FVO_CUSTOMPOSITION`，导航两个隔离临时目录时逐次于 `OnViewCreated` 查询 `IFolderView2`，只清除 `FWF_NOHEADERINALLVIEWS` 与 `FWF_NOCOLUMNHEADER` 两个列标题隐藏位并核对其它 folder flags 未变。前者默认会把标题限制在详细信息模式，后者会在所有模式隐藏标题；清除两位是验证全查看方式列标题的实验操作。随后复用现有八种查看方式状态探针核对排序、分组、列和多选保持。[Microsoft `FOLDERFLAGS`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags)
- NativeProbe Release 编译通过（0 警告/错误）。新增纯状态 `FolderHeaderFlagsSmoke`，先在未实现掩码时按预期失败，再验证两种抑制位被清除且其它 flag 原样保留；完整 Release SettingsSmoke 通过。遵守当前 no-Computer-Use 限制，没有运行 hosted 探针或创建 ExplorerBrowser 窗口；选项能否传递、Shell flag 调用结果、视图渲染以及排序/排列/选区保持都仍待运行时验证。探针不接入生产文件视图。

## 2026-10-25：减少大目录焦点恢复前的全量 Shell 枚举

- 在 `ShellViewSelectionSnapshot.Restore` 中移除焦点 PIDL 设置前的 `ContainsItem` 全目录扫描，改为直接调用 `IShellView.SelectItem`，再用 `IFolderView` 核验焦点是否落到目标项。旧路径在 50k 样本中扫描约 71–78 ms；直接设置后，普通验证约 5 ms，但 Shell 自身忙时一次设置仍约 115 ms。焦点尚未出现在异步枚举结果中时，首次核验仍失败，后续延迟重试成功；未提前把焦点跳到其他选中项。
- HostProbe 的隐藏生产宿主 50k 创建/删除最终通过：watcher 为 50,000/50,000，Shell/磁盘 3/3，原多选和未选中焦点均恢复，最终退出码 0；最大 Dispatcher 心跳间隔 567 ms。探针把创建后的选区/焦点成功判定移到等待基线稳定后，避免把一次早期采样当作最终状态。该轮无可见 GUI，也没有发布。
- 下一步继续拆分 STA 长空档：批量选择 API 与 Shell 的焦点 `SelectItem` 在压力下仍可分别占用约 54–58 ms、约 115 ms；需重复采样定位可安全优化的等待/调用边界，同时保持异步焦点 PIDL 重试和键盘选择状态正确。自动刷新与大目录 STA 响应 P0 仍开放。详见[刷新调查](live-update-investigation.md#2026-10-25-大目录焦点恢复跳过全目录-pidl-扫描)。

## 2026-10-25：补充选区 PIDL 匹配的 COM 计时点

- 为定位前述选区核对的 STA 成本，`ShellViewSelectionSnapshot` 现会在已有原生 Shell 诊断跟踪开启时分别记录 `IFolderView.Items`、`IEnumIDList.Next` 和 `IShellFolder.CompareIDs` 的耗时。跟踪关闭时不读取计时器、不输出逐项记录；恢复决策与行为保持不变。App Release 构建和全量 SettingsSmoke 通过。按当前无 Computer Use 约束，没有启动 ExplorerBrowser HostProbe，因此暂无这些细分调用的实测数据；下一步待桌面空闲且授权交接后复跑相同 50k 隐藏宿主，以计时结果决定是否有可安全优化的调用边界。

## 2026-10-25：选区 PIDL 身份核对增加二进制快速路径

- `ShellViewSelectionSnapshot` 现在先用 Shell `ILIsEqual` 对 PIDL 做二进制相等检查；一致时跳过 `IShellFolder.CompareIDs`，不一致时仍执行原有 `SHCIDS_CANONICALONLY` 比较。Microsoft 文档明确说明二进制不同的 PIDL 仍可能代表同一项目，因此保留 canonical fallback，避免把快速比较误作完整 Shell 身份判断。原生诊断记录新增 `method=pidl-exact|canonical-shell`，供后续相同 50k 压力样本统计命中比例和逐项耗时。[`ILIsEqual` 官方文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ilisequal)
- Release SettingsSmoke 和 App Release 构建通过，0 警告/错误。没有运行 ExplorerBrowser HostProbe，因此尚无快速路径命中率或 STA 时延对照；这是一项不改变匹配语义的候选优化，自动刷新与大目录响应 P0 仍开放。

## 2026-10-25：在隔离 `IShellView` 中验证选区 PIDL 快速路径

- NativeProbe 现链接生产 `ShellViewSelectionSnapshot` 与 `ShellSelectionBatchRestore`，在隐藏默认 `IShellView` 上捕获后立即恢复同一选区。Release 探针退出码 0，trace 确认 `method=pidl-exact`、`selection-current matched=True`、`batch-skipped=True`，选区恢复成功；单项身份比较样本为约 0.5 ms。视图父窗口隐藏且前台窗口不变，没有创建 ExplorerBrowser。
- 探针返回 `focus-restored=False`，因此该隐藏视图样本不作为焦点恢复证据。它验证了生产选择快照的未变化选区快路与跳过批量重放，但不代表 ExplorerBrowser 生产宿主、50k 负载或可见输入性能；后续需在许可的生产 HostProbe 中比较 `method` 分布与总 STA 延迟。

## 2026-10-25：多选恢复优先验证批量 Shell 调用

- 在批量恢复路径前增加纯逻辑决策：直接调用 `IFolderView::SelectAndPositionItems`，只在 API 失败或读回选中 PIDL 与快照不完全一致时，才执行原有 `SelectItem` 播种及批量兜底。Microsoft 文档说明此 API 用于选择当前文件夹视图中可见的项目并返回 HRESULT，但没有说明缺失 PIDL 的具体结果；因此保留兜底，并将 Shell 对缺失/过期 PIDL 的行为列为待实测。
- 跟踪回调现在输出 `selection-batch-path=direct` 或 `selection-batch-path=seeded-fallback`，让下一轮原生测量可直接统计实际分支。新增 SettingsSmoke 三分支回归：批量成功且完全匹配时跳过播种；批量成功但不匹配时走兜底；批量失败时不做匹配检查并走兜底。App/HostProbe Release 构建 0 警告/错误，SettingsSmoke 通过。**本轮没有运行 ExplorerBrowser 宿主压力探针，不能宣称此路径已降低 STA 延迟或保持真实 Shell 选区行为**；待明确桌面授权后，复跑隐藏宿主及可见 ExplorerBrowser 选区/焦点对照。自动刷新 P0 继续开放。

## 2026-10-25：隔离 IShellView 着色隐藏复测

- `ExplorerDirs.NativeProbe --ishellview-color-spike` 隐藏复跑退出码 0，父窗口不可见、前台句柄不变；八种查看方式和原生菜单/拖放接口检查再次通过。真实 `NM_CUSTOMDRAW` 仍只在平铺模式收到两个逐项通知，通知矩形为零、HDC 不指向请求的位图，行区域和位图目标颜色像素均为 0。重复样本没有验证逐项颜色，也没有使用 ExplorerBrowser 或可见桌面；生产视图架构未改。下一步仍需独占桌面授权的可见隔离验证，确认列表视图真实绘制后再决定是否继续原生方案。详见[原型记录](ishellview-color-spike.md#2026-10-25-隐藏探针复测)。

## 2026-10-25：刷新后选区恢复与 STA 队列延迟诊断

- 启用 HostProbe 现有消息泵和 Dispatcher 队列延迟跟踪，复跑 50k 隐藏 Shell 压力。Shell/磁盘最终 3/3，watcher 50,000/50,000，选区/焦点恢复；创建 1 次、删除 2 次 fallback，负数预期未重现。最大 Dispatcher 心跳 588 ms，队列延迟最高约 526 ms。时间线显示 `ScheduleDeferredRefreshSelectionRestore` 在 UI STA 的一次调用约 256 ms，并与 DirectUI/SHELLDLL_DefView 私有消息约 246 ms 重叠；另一个未启用队列跟踪的样本中 DirectUI 私有消息耗时约 754 ms。私有消息含义未知；已确认的恢复方法同步调用 Shell 选择接口，但各 COM 步骤成本尚未分开，因此未改恢复策略。详见[刷新调查](live-update-investigation.md#2026-10-25-sta-延迟与延迟选区恢复重叠)。下一步为恢复链路的 COM 步骤加隔离计时并复测，确认具体瓶颈后再决定优化。

## 2026-10-25：暂时空 Shell 视图不再破坏自动刷新计数基线

- 修复后 50k 复跑发现此前两次空视图刷新会把非空目录逻辑基线重置为 0，后续 watcher 删除产生 `expected=-128/-49,999`；原压力探针虽最终收敛仍以退出码 0 通过。先让 SettingsSmoke 在旧逻辑上复现失败，再改为第二次空视图结果保留正向预期计数，并让后续 watcher 增量继续基于此值；Release SettingsSmoke 转绿。修复后 50k 隐藏生产宿主再次通过：watcher 50,000/50,000，Shell/磁盘 3/3，选区焦点恢复，预期计数非负；删除 fallback 仅在 129 项事件后以 `49,874/50,002` 差值触发。创建阶段无 fallback。Dispatcher 最大心跳间隔仍为 1,139 ms，DirectUIHWND 私有消息 `0x4C8` 单次处理约 754 ms，STA 响应风险仍未解决。无可见 GUI、未发布。详见[自动刷新调查](live-update-investigation.md#2026-10-25-暂时空视图保留逻辑计数并复跑-50000-项)。

## 2026-10-25：隔离 `IShellView` 逐项颜色原型

- 用户选择先验证原生视图路线。NativeProbe 直接创建隐藏的默认 `IShellView`，本机看到 `SysListView32` / `SysHeader32`；选中夹具条目后，`IFolderView2`、背景/选中项 `IContextMenu` 和 `IID_IDropTarget` 查询均成功。只读菜单枚举确认背景菜单提供 `View`、`GroupBy`、`Refresh`、`New`/`NewFolder`、`Properties`、排序动词，选中项菜单提供剪切、复制、粘贴、删除、重命名、属性；复用产品命令适配器检查可用状态，没有显示菜单、调用命令或触碰剪贴板。隔离视图按产品 `ViewStyle.All` 切换八种模式，原生读回模式/尺寸正确，条目与选区保持。合成 `NM_CUSTOMDRAW` 验证处理器契约；隐藏探针在 `WM_PRINTCLIENT` 期间只有平铺模式收到两条项目通知，通知 HDC 不匹配传入位图 DC、矩形为零，离屏图无目标颜色像素，故颜色渲染仍未验证。NativeProbe Release 构建与隐藏运行通过；没有修改生产文件视图。下一步须等桌面可操作时，用明确授权的可见隔离副本确认通知 HDC、行边界和真实像素，再评估其与生产 ExplorerBrowser 的兼容性；可见验证前不切换产品架构。见[原型说明](ishellview-color-spike.md)与[颜色调查](shell-color-investigation.md#2026-10-25-隔离-ishellview-逐项颜色原型)。
- 后续仅增强隔离可见入口：在临时 IShellView 的详细信息视图中清除夹具选区、强制重绘，并从该 ListView 自身 DC 采样两行目标颜色像素；不会截取或保存桌面图像。NativeProbe Release 构建通过；隐藏原型复跑通过且前台未变。可见入口没有运行，颜色仍未验证，生产文件视图未改；桌面测试须确保视图无遮挡并获得明确授权。

- 2026-10-25 按用户选择再次运行隐藏原型：Release `dotnet run` 退出码 0，八种视图的读回、背景与选中项菜单、排序动词、复制/删除/重命名状态及新建文件夹适配器状态均复现；原生视图、选中项与拖放接口仍可查询。此次 Shell 报告 `Paste=False`、`Undo=False`，未读剪贴板或执行命令。绘制统计仍只有平铺模式收到两条 `NM_CUSTOMDRAW` 项目通知，`rc` 为零且 HDC 不属于目标位图，故仍不能证明行颜色。探针父窗口隐藏，前台窗口未变化，生产窗格未创建或修改。可见隔离验收仍是颜色路线进入产品前的门槛。

- 隐藏探针新增逐项绘制通知索引到文件身份的核验：对同一轮绘制快照里的 `dwItemSpec` 调用 `IFolderView::Item`，再将子 PIDL 与夹具文件绝对 PIDL 的末级 ID 比较。Release 实测平铺模式两项分别映射到 `blue-rule-target.txt` 和 `green-rule-target.txt`；其它模式没有项目级通知。探针结果还暴露并排除了模式切换后迟到消息误用前一模式索引的采样竞态。未分组平铺索引身份路径现有隐藏证据；排序/分组变化、真实像素、可见交互和 ExplorerBrowser 兼容性仍未验证。详情见[隔离原型记录](ishellview-color-spike.md#2026-10-25-隐藏平铺通知的文件身份映射)。

## 2026-10-25：10,000 项自动刷新正确基线复测

- 同一 Release HostProbe 连续两轮隐藏生产宿主 `--live-shell-directory-stress-large 10000` 均通过：watcher 创建/删除均为 10,000/10,000，最终 Shell/磁盘项数为 3，选中 `second.txt`/`third.txt` 及未选中的焦点 `initial.txt` 均恢复。每轮在创建计数暂时落后时及删除时各触发一次产品 fallback；创建视图约 1.82–1.91 秒收敛，创建/删除约 3.82/1.77–1.88 秒。第二轮创建 fallback 首次未能恢复暂时缺失的焦点行，延迟恢复在约 236 ms 后完成。该轮 HostProbe Release 构建 0 警告/错误，使用隔离临时文件，无可见窗口；这是隐藏 Shell 与 COM 自动化证据，不代表可见响应或其它 Shell provider。没有修改生产刷新策略。完整运行数据与边界见[调查记录](live-update-investigation.md#2026-10-25-10000-项自动刷新正确基线复测)；自动刷新 P0 继续开放。

## 2026-10-25：50,000 项自动刷新复测

- Release HostProbe 两轮隐藏生产宿主 50,000 项创建/删除压力均通过，watcher 两阶段完整为 50,000/50,000，最终 Shell/磁盘均回到 3 项，选区与未选中焦点恢复。第一轮 fallback 为创建 1 次、删除 3 次；第二轮为创建 1 次、删除 2 次。第二轮时间线显示删除阶段先在 1 个 watcher 事件的计数差、后在 129 个事件的计数差各触发一次刷新，说明事件分批到达和 Shell 计数暂时滞后时会出现重复兜底。创建/删除耗时分别为 20.7/11.1 秒和 34.0/20.5 秒；最大 Dispatcher 心跳间隔 713/916 ms，仍属响应风险。第一轮独立 observer 少收到 1 条创建通知（49,999/50,000），生产 watcher 仍完整。仅覆盖本机本地临时盘与隐藏宿主，不证明可见交互或其他 Shell provider；没有改刷新策略或发布版本。下一步核对 250/500 ms 检查与 `largeBurstActive` 750 ms 窗口，确认是否在延迟到达的删除事件间过早刷新。细节见[自动刷新调查](live-update-investigation.md#2026-10-25-50000-项自动刷新复测)。

## 2026-10-25：活跃大突发期间延后单项计数兜底

- 50k 删除时间线证实单个 watcher 事件后的单项计数差绕过 `largeBurstActive` 延迟，约 4.88 秒后又到达 128 项删除事件。ShellDirectoryChangeBatchSmoke 先按新预期在旧逻辑上失败，再让孤立单项差立即 fallback、活跃突发窗口内等待、突发安静后 fallback。生产逻辑现在只在突发活动时延后单项差；强制刷新不变。SettingsSmoke、App/HostProbe Release 构建通过（0 警告/错误），修复后 50k 隐藏宿主一轮通过，事件完整、最终计数、选区和焦点恢复；删除 fallback 在 129 项差及最终 8,575/3 计数差时发生，不再只因第 1 项事件刷新。最大 Dispatcher 间隔仍为 890 ms，修复后样本仅 1 轮，整体响应风险未解决；无可见 GUI验收、未发布。详见[刷新调查](live-update-investigation.md#2026-10-25-活跃大突发期间延后单项计数兜底)。下一步复跑修复后的同基线并继续定位 STA 长间隔。

## 2026-10-25：Modern 地址栏 F4 两阶段操作

- 按用户确认的 Q-Dir 行为，Modern 地址栏第一次 F4 打开并定位当前 Shell 文件夹树，再按 F4 进入完整路径编辑；Classic 地址栏的 F4 直接进入路径编辑。地址历史继续由右侧独立下拉按钮打开。快捷键状态 smoke、完整 Release SettingsSmoke 与 App Release 构建通过；未操作可见桌面，因此 Popup 位置、真实 Shell 树选择和焦点仍待 GUI 验收。实现范围与限制见[Shell 对照表](shell-parity.md#地址栏样式与历史)及[快捷键表](keyboard-shortcuts.md)。

## 2026-10-25：Changed 多路径按项通知更新生产 Shell 文件视图快照

- 扩展 HostProbe `--live-shell-changed-metadata`，同时改写选中且聚焦的 `initial.txt` 与未选中的 `second.txt`。生产 watcher 将两条 Changed 事件合并为一批，独立 Shell observer 收到两条 `SHCNE_UPDATEITEM`；当前 Shell 行快照分别从 `7 字节|6 字节` 更新为 `128 KB|64.0 KB`。两行在 860.8 ms 内更新，选区/焦点保持，整视图 fallback 增量 0，隐藏父窗口且前台不变；Release HostProbe 构建 0 警告/错误、探针退出码 0。完整 Release SettingsSmoke 通过，包含多路径批次和路径长度边界。此为隐藏宿主当前行快照证据，不证明 DirectUI 可见像素重绘或其他 Shell 提供程序；详见[动态更新调查](live-update-investigation.md#2026-10-25-changed-通知更新生产-shell-文件视图快照)。
- 随后的排序探针发现：两文件大小交换后，当前行快照有时显示新大小，但顺序仍是旧值；ShellBrowserSite 未收到 `CDBOSC_STATECHANGE`，只有选择变化回调。探针在更新后切到名称排序再恢复大小排序可重新排列，但固定延时不适合作为生产触发条件。下方记录了基于活动排序/分组键的目录更新方案及验证结果。
- 已按 Changed 监听的四类元数据修正自动重排：完成逐项 `SHCNE_UPDATEITEM` 后读取当前 Shell 的排序/分组状态；若 `System.Size`、`System.FileAttributes`、`System.DateModified` 或 `System.DateCreated` 参与排序/分组，再补一条对应目录的 `SHCNE_UPDATEDIR`。名称排序和类型分组不触发目录更新。Release SettingsSmoke 策略回归通过；隐藏生产宿主在无额外 flush 注入时发送两条 UPDATEITEM 和一条 UPDATEDIR，大小升序随两文件大小互换正确变序，854 ms 内收敛，选区/焦点保持且没有全视图 fallback。不同 Shell 提供程序、分组视图、大目录成本和可见绘制仍待验证；详见[动态更新调查](live-update-investigation.md#2026-10-25-changed-字段影响排序时补发目录更新)。
- 新增隐藏 HostProbe 的修改时间分组对照开关。Release 生产宿主在 `System.DateModified` 分组及大小排序下收到两条 Changed 路径后，生产通知为两条 `SHCNE_UPDATEITEM` 加一条对应目录的 `SHCNE_UPDATEDIR`；截至本轮共 7 次分组成功样本，最后 5 次连续成功，收敛约 903–965 ms，选区/焦点保持，整视图 fallback 增量为 0，未使用显式 flush。observer 注册曾失败 2 次；新增诊断后另一次 32 路径首轮也在注册时返回 0（last-error=0），但随后 2 文件控制和 32/128 路径重跑成功，故注册失败根因仍未定。独立 Shell 通知 smoke 通过。详见[动态更新调查](live-update-investigation.md#2026-10-25-changed-元数据分组生产宿主对照)及多路径负载记录。
- 已补测 128 条限额边界与 129/160 条超限路径。128 条按项通知路径在约 2.60 秒收敛、fallback 为 0；129 条触发且仅触发 1 次全视图刷新，约 2.37 秒收敛，`IShellView.Refresh()` 75.9 ms；160 条也只刷新 1 次，约 3.14 秒收敛，`IShellView.Refresh()` 153.5 ms。超限场景均保持排序、选区与焦点，前台句柄不变；160 条测试等待产品 fallback 计数后再判断，避免自然 Shell 更新先收敛而造成过早采样。完整阶段计时与证据边界见[动态更新调查](live-update-investigation.md#2026-10-25-changed-多路径超限刷新兜底)。
- 继续压力复测不创建 ExplorerBrowser 的 Shell observer smoke：串行 20 轮中 13 轮通过、1 轮注册返回 0、6 轮注册成功但 5 秒无事件；同 HWND 注册/注销 40/40 成功，新 HWND 立即销毁仅 14/40 注册成功，新 HWND 注销后延迟 100 ms 再销毁为 40/40。复测显示 200 ms 进程间隔并不稳定（10/10 后续重跑为 8/10），1 秒注册准备等待只消除了该批投递超时但仍有 3/10 注册失败；根因仍未定，产品 watcher/通知策略未改。见[动态更新调查](live-update-investigation.md#2026-10-25-changed-元数据分组生产宿主对照)。

## 2026-10-25：逐行递归大小列的 Shell 扩展边界

- 对照微软接口文档复核：`IColumnProvider` 已自 Vista 移除支持；Property Handler 需注册并关联文件类型；可自定义 `IShellView`，但需要自己承载视图行为。结合本机探针已证实的排序忽略缓存、显式刷新清缓存，当前没有找到既保留系统 DefView 又为任意文件夹附加递归大小属性的受支持路径。因此不接入 `SetViewProperty`、不改用自绘列表；保留现有 F9/状态栏递归汇总，将逐行大小列列为后续视图架构设计项。来源与取舍见[逐行大小列调查](folder-size-column-investigation.md#替代实现路线复核)。

## 2026-10-25：显式刷新会清除隐藏视图的逐项大小缓存

- 新增独立 NativeProbe 命令 `--view-property-refresh-capability`：`IShellView::Refresh()` 后原三项仍在，但缓存属性成为 `VT_EMPTY`。按 `System.Size` 降序，注入大值前后顺序均为 `five.txt|three.txt|child`，且缓存值仍可读，说明原生排序没有使用该缓存。目录重命名后隐藏视图在 1 秒消息泵内仍显示旧名称，改名缓存语义未定。Release 构建 0 警告/错误、探针退出码 0，父窗口未显示、前台未改变。此 API 路线目前不满足排序与刷新语义，不接入产品；自然 Shell 更新和可见绘制仍待验证。详见[逐行大小列调查](folder-size-column-investigation.md)。

## 2026-10-25：验证隐藏 ExplorerBrowser 的文件夹大小属性缓存

- NativeProbe 的 `--view-data-capability` 在已有 `System.Size` 列的隐藏详情视图中，对隔离文件夹调用 `IFolderView2::SetViewProperty` 写入 `VT_UI8=987654321`，API 成功且 `GetViewProperty` 读回相同值；立即重新读取完整视图快照后，`IShellItem2` 的底层文件夹大小仍为 unknown，条目集合、排序、列和导出值保持。后续独立探针确认 Shell 大小排序忽略该缓存，显式刷新清空该值。NativeProbe Release 构建 0 警告/错误、探针退出码 0，父 HWND 未显示、前台未改变。此 API 不满足产品所需的排序/刷新行为，未接入产品；可见绘制仍未知，但即使绘制成功也不能单独构成可用实现。详见[逐行大小列调查](folder-size-column-investigation.md)。

## 2026-10-25：扩展 F2 重命名方向键探针覆盖 Shift 文本选择

- HostProbe 的四窗格原生重命名探针现额外按探针线程 Shift 状态测试 Shift+←/→；断言选择范围留在当前 Edit、焦点保持，之后验证线程 256 键状态与测试前完全一致。Release HostProbe 构建 0 警告/错误，四个窗格探针全部通过。此为隐藏生产 Shell 宿主上的合成消息验证，不证明可见窗口的物理按键消息路径或文件重命名提交行为。实现与范围见[键盘清单](keyboard-shortcuts.md)。

## 2026-10-25：复跑四窗格 F2 原生重命名方向键路由

- Release HostProbe 四次逐窗格调用生产 `TranslateAcceleratorCore` 均通过：F2 进入真实 Shell 内联重命名 Edit，Left/Right 移动名称内光标；Left/Up/Right/Down 后仍由原窗格 Edit 持有焦点，其他窗格不认领。HostProbe Release 构建 0 警告/错误，四个探针退出码均为 0。探针仅使用隔离临时文件和未显示的 WPF/Shell 宿主，不使用桌面输入；这不证明可见窗口里的物理 F2 消息进入同一代码路径，仍需可见多窗格验收。

## 2026-10-25：复跑 `.qdr` 原生视图流独立消费者

- 当前 Release `NativeProbe --qdr-native-stream` 通过 16 组（8 种视图模式/图标尺寸 × 分组开关）。Windows 原生 Shell 读取应用输出流后，模式、排序、分组和列信息均读回；子视图未显示，父级前台窗口未改变，退出码 0。此项验证 ExplorerBrowser 使用的 Shell 原生视图流可被当前 Windows 消费，不证明 Q-Dir 读取兼容、真实视图绘制或可见 UI 行为；未启动产品窗口或 Q-Dir。

## 2026-10-25：复核新增 `.qdr` 树分隔值样本

- 在用户文档中找到 Q-Dir 12.79 `default.qdr`，只读取版本及树相关字段。它的 `tree_mode=2`、`Tree1`～`Tree4=0`、四个 `TreeNsplit=2500` 与现有 12.79 样本一致；公开网页补查找到 SoftwareOK FAQ 117 的 7.84 示例，数值也全为 2500。没有来源提供非默认宽度对照，故继续不导入 `TreeNsplit`。Release SettingsSmoke 与 App Release 构建通过；未启动 Q-Dir、GUI 或导入用户配置。样本哈希及证据范围见[Q-Dir 导入记录](qdr-import.md#2026-10-25-treenSplit-静态读取路径补查)。

## 2026-10-25：补充分辨导入后文件区黑底的对照场景

- 颜色调查建议把“运行中普通新标签”加入 `.qdr` 导入对照，以区分运行中创建 ExplorerBrowser 视图与导入数据的影响。GUI smoke 清单现要求同一路径、详细信息视图下，依次比较初始视图、普通新标签、取消导入、导入最小 `.qdr` 和重启恢复，并在同一屏幕区域记录 DirectUI 文件区像素/截图及首次变色步骤。只运行 `-PrepareOnly`，准备副本 `src/.state/gui-smoke/20261024-204216-325-da055f/`；manifest 为 `prepared-not-run`，EXE/DLL 哈希与文件一致。没有启动 GUI、Shell 宿主或 Q-Dir；黑底根因和可见复现仍未确认。详见[颜色调查](shell-color-investigation.md)。

## 2026-10-25：快速筛选沿用 Windows 隐藏文件偏好

- 快速筛选启用时，`IFolderFilter::GetEnumFlags` 原先固定输出 `SHCONTF_FOLDERS | SHCONTF_NONFOLDERS`，因此没有请求 Shell 枚举普通隐藏项；当 Windows 的“显示所有文件”开启时，筛选期间可能与 Explorer 的可见项不一致。现在通过只读 `SHGetSettings(SSF_SHOWALLOBJECTS)` 查询系统偏好，并按需加入 `SHCONTF_INCLUDEHIDDEN`，不修改注册表或便携 INI。API 映射 smoke 覆盖开/关两种状态，生产回调与本机当前设置一致（本机输出 `0xE0`）；完整 Release SettingsSmoke 和 App Release 构建通过。没有运行 ExplorerBrowser 或 GUI，因此筛选视图的实际显示仍待桌面验收；受保护的系统隐藏项不在本次范围。依据：[Microsoft `SHGetSettings`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetsettings)、[`SHELLFLAGSTATE`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-shellflagstate)、[`SHCONTF`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_shcontf)。

## 2026-10-25：更正快速筛选枚举标志的接口语义

- 对照 Microsoft `IFolderFilter::GetEnumFlags` 声明后确认，`pgrfFlags` 是 `[out]`，不是调用者传入的 Shell 默认策略。上一条“保留调用方隐藏项标志”的 smoke 与代码改动基于错误假设，现已撤回；回归改为用非零初值确认方法输出 `SHCONTF_FOLDERS | SHCONTF_NONFOLDERS`（`0x60`）。没有新增用户功能或改变产品过滤策略。
- 同轮 NativeProbe 构建暴露项目文件漏链共享的 `ShellTreeFolderLocation.cs`；补齐引用后 NativeProbe Release 构建通过（0 警告/错误）。完整 Release SettingsSmoke 和 App Release 构建通过；未启动 ExplorerBrowser。接口依据：[Microsoft `IFolderFilter::GetEnumFlags`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderfilter-getenumflags)。

## 2026-10-25：独立验证 Shell 按项更新通知

- HostProbe 新增 `--shell-update-notification-smoke`，仅创建隔离临时目录和隐藏通知接收 HWND，不创建 `ExplorerBrowserHost`。调用生产 `ShellChangedItemNotification` 后，接收器收到 `SHCNE_UPDATEITEM`（`2000`），目标为 `changed.txt`，无锁失败；测试前后前台窗口句柄一致，测试进程退出码 0。完整 Release SettingsSmoke 与 HostProbe Release 构建通过。
- 这证明当前 Windows 将生产格式的通知投递给注册在该隔离目录上的 Shell 接收器；不证明 ExplorerBrowser 文件视图重绘、原生 STA 性能或多窗格选区/焦点行为。未启动应用 GUI，temp.37 预览包未重打。

## 2026-10-25：临时预览 temp.37

- 发布 `releases/preview-0.6.0-temp.37` 独立 profile，包含 Changed 事件 Shell 按项更新。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；产品版本 `0.6.0-preview.temp.37+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `AC2F0720FAD161B308248986079DE13A2D93EEF7FC90B2CA8C2F8455C0292C37`，DLL SHA-256 `C1273E7D86260EF185978278FB1A028805802915169D8BE0C48568334693566D`。没有启动 GUI；ExplorerBrowser 元数据重绘、选区/焦点与性能仍待验收。详见[预览说明](releases/preview-0.6.0-temp.37.md)。

## 2026-10-25：临时预览 temp.36

- 发布 `releases/preview-0.6.0-temp.36` 独立 profile，供验收 Modern 地址栏 F4 两阶段 Shell 文件夹树。Release SettingsSmoke、App Release 构建和发布隔离 smoke 通过；产品版本 `0.6.0-preview.temp.36+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 与包内清单一致。未启动 GUI；地址树焦点、弹出布局、Escape 与虚拟命名空间导航仍待可见验收。详见[temp.36 预览说明](releases/preview-0.6.0-temp.36.md)。

## 2026-10-25：刷新四窗格 GUI 验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 从当前源码构建并准备 `src/.state/gui-smoke/20261024-202315-194-87fac6/`；四个窗格各使用独立临时目录，清单列出 11 项待验收交互，包括每窗格 F2 重命名方向键、多窗格剪贴板焦点、空白处背景菜单和跨窗格拖放。`preparation.json` 状态为 `prepared-not-run`，EXE/DLL SHA-256 与清单一致，所有应用文件均位于该隔离副本。准备分支在加载 UI Automation 和窗口操作之前返回；没有启动 GUI、发键或操作剪贴板。源码/隐藏测试证据不替代这些可见行为验收。

## 2026-10-25：Changed 事件改用 Shell 按项更新

- `FileSystemWatcher.Changed` 现在收集绝对条目路径，在 250 ms 静默期后以 `SHCNE_UPDATEITEM` 通知 Shell；忽略大小写去重，每批上限 128 个。超限或路径不适用于 `SHCNF_PATH` 时仍调用既有全视图 fallback。回归先按旧逻辑失败，再通过；Release SettingsSmoke 全套和 App Release 构建通过（0 警告/错误）。尚未在 ExplorerBrowser 窗口验证元数据重绘、选区/焦点、长路径及性能，不能据源码接线声称用户卡顿已改善。详见[动态更新调查](live-update-investigation.md#2026-10-25-filesystemwatcher-changed-与全视图刷新调用链复核)。

## 2026-10-25：Q-Dir Modern 地址栏 Shell 文件夹树

- Modern 地址栏 F4 首次打开当前目录定位的原生 Shell 文件夹树，再次按 F4 进入路径编辑；Classic 样式直接编辑。地址历史仍由独立下拉按钮打开。树按窗格独立，选择后只导航所属窗格当前标签，并接受 Shell 可解析、确认为文件夹的绝对解析名。
- 地址树 Escape、标签切换、窗格隐藏及窗口失焦会关闭 Popup。地址快捷键 smoke、Shell 位置边界 smoke、完整 Release SettingsSmoke 与 App Release 构建通过（0 警告/错误）。未启动 GUI；Popup 定位、原生树焦点、Escape 行为、虚拟文件夹可见导航及窄窗格体验仍待桌面验收。

## 2026-10-25：显示隐藏项目的原生 Shell 接口边界

- 对照 Microsoft 的 `IFolderView2` / `FOLDERFLAGS` 接口清单，未找到可对单个 ExplorerBrowser 文件视图启用隐藏项目的公开标志；Microsoft 文档将 `SFGAO_HIDDEN` 的可见性关联到系统 Folder Settings，Windows 组策略文档也将 `Hidden` 映射到 HKCU Explorer Advanced 设置。
- 因项目要求设置保持便携 INI 且文件区继续使用原生 ExplorerBrowser，本轮不通过修改注册表来提供应用私有开关，也不以自建列表绕过 Shell。差异已列入 [Shell 功能对照](shell-parity.md)；后续若用户接受系统级联动，再评估菜单入口和设置广播。此为接口边界调研，不代表已实现隐藏项目显示。

## 2026-10-25：Q-Dir Modern 地址栏目录树对照

- SoftwareOK FAQ 116 说明 Modern 地址栏第一次按 F4 显示目录树、再次按 F4 聚焦路径编辑；当前项目是可点击面包屑，F4 则打开历史列表，且另有历史按钮，因此存在明确交互差异。此前产品页提取结果曾显示 12.81；本次直接打开官方产品页后，当前版本历史列为 12.82（2026-10-24 更新），已修正 [Shell 对照清单](shell-parity.md)。本项只依据 FAQ 116 确认交互，不推断版本间功能变化。
## 2026-10-25：Q-Dir `.qdr` 树宽字段样本核对

- Q-Dir 官方 FAQ 117 的 `.qdr` 示例包含 `Tree1split`～`Tree4split`，四项均为 `2500`；本机 Q-Dir 12.79 的 `start.qdr` 与一个快速链接工作区也均保存为 `2500`。现有样本没有不同值，不能据此确认该字段的量纲或它相对于窗格、树区域还是屏幕的分割基准。本程序树宽按 DIP 保存，暂不把该值猜测映射为宽度；继续保留树可见状态导入，并在取得不同宽度的 Q-Dir 样本或可见拖动前后 `.qdr` 对照后再实现。来源：[Q-Dir 官方 `.qdr` 示例](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)。本轮只读检查配置文件，未启动 Q-Dir 或桌面 GUI。

## 2026-10-25：确认 `.qdr` 虚拟 Shell 位置兼容范围

- 用户确认导入所有当前 Windows Shell 可解析、且确认是文件夹的绝对解析名，不限制于内置别名。现有导入器已按 `SHParseDisplayName` 与文件夹属性验证 PIDL，并保留可重新解析的 Shell 绝对名；Shell 扩展缺失时依赖该扩展的位置可能失效。`ShellLocationIdentitySmoke` 覆盖内置别名以外的 CLSID、Q-Dir 编码 PIDL 导入及普通文件 PIDL 拒绝；本轮将兼容边界和失效限制补入[导入说明](qdr-import.md)。该回归不创建 ExplorerBrowser，也不证明第三方扩展的可见 UI 行为。

## 2026-10-25：微软 File Explorer 快捷键复核

- 重新逐项核对微软现行 File Explorer 快捷键表与 `KeyboardShortcuts.Resolve` 及 `ExplorerBrowserHost.TranslateAcceleratorCore`：应用自定义的导航、窗格、标签及查看操作已接线；文件操作、详细信息列宽和树项目选择等保留 Shell 原生处理，没有发现需要再添加的常用宿主快捷键。
- 记录 Ctrl+W 的最后标签差异：Explorer 在只剩一个标签时关闭窗口，本程序按产品规则保留每个窗格的最后一个标签。该差异同步到快捷键及 Shell 对照文档；此轮为源码路由与文档核对，没有发键盘或启动 GUI。

## 2026-10-25：补强新建文件夹重命名观察器隔离回归

- 新增本地临时目录 smoke，直接验证生产 `FolderCreationObservation`：普通文件不进入创建目录快照，唯一新建文件夹按完整路径被捕获，且观察过程中没有 Error。
- SettingsSmoke 新增 `--folder-creation-observation-smoke` 早期入口，使该真实文件系统事件回归可单独运行且在 Shell 探针初始化前退出；本轮 Release 专项构建及单测通过。App Release 构建通过，0 警告、0 错误。没有启动 Shell 宿主或 GUI；这加强了短期观察器边界验证，不替代新建文件夹命令、刷新后选中目标和原生重命名焦点的可见验收。App 源码早于 temp.34 打包且本轮只改 smoke 入口与文档，没有生成重复 release。

## 2026-10-25：修正 SettingsSmoke 纯状态入口隔离

- `--pure-state-smoke` 原本位于几个无条件 Shell smoke 之后，不能保证纯状态边界。现将它前移到探针初始化之前，只运行监视器启动状态和 Shell 名称语法解析测试；Release 构建与该入口通过，确认无需启动 Shell 宿主即可验证这组状态逻辑。

## 2026-10-25：隔离运行右键菜单目标状态回归

- 将既有 `ShellMenuTargetTracker` 断言集中到 `ShellMenuTargetTrackerSmoke`，新增 `--shell-menu-target-smoke` 早期入口；空白处保留选中项、条目命中、异步等待/失败、键盘菜单、过期目标及有符号坐标测试可单独运行，不经过 Shell 探针。另在现有隔离 trace 开关启用时记录命中坐标、分类及 UIA 控件类型祖先链，不记录控件名称或路径。自检还修正空字符串 trace 路径被误判为启用的问题；现在只有非空路径才收集控件链，并由专项 smoke 覆盖 null/空值/有效路径三种情况。App/SettingsSmoke Release 构建与专项运行通过（0 警告/错误）。此状态逻辑测试不验证 ExplorerBrowser 实际 UIA 命中或 Windows 生成的标准背景菜单，详细边界见[目标识别调查](context-menu-target-research.md)。

## 2026-10-25：保护文件视图 CSV/TXT 导出中的公式样式文本

- 修正 CSV 与制表符 TXT 导出：对跳过前导空白后以 `=`, `+`, `-`, `@` 或全角对应符号开头的单元格添加前导 Tab，再按原格式引用字段。这样避免文件名或属性文本被电子表格当成公式执行；代价是电子表格导入后的单元格值会多一个 Tab，XLS 导出继续以字符串单元格保存原值。
- 回归覆盖上述公式前缀、前导空白、全角符号和普通文本；新测试在旧逻辑下按预期失败，修复后完整 Release SettingsSmoke 通过。App Release 构建通过，0 警告/错误；发布隔离 smoke 通过，已打包 `releases/preview-0.6.0-temp.34` 并核对版本和 EXE/DLL 哈希。未启动 GUI 或 Excel，电子表格软件显示与保存重开行为未现场验收。
- 依据：[OWASP CSV Injection](https://community.owasp.org/attacks/CSV_Injection) 指出引号不足以阻止公式求值，并记录 Excel 使用带引号字段前导 Tab 的缓解方式及数据值改变的代价。

## 2026-10-25：地址栏历史下拉入口并发布 temp.33

- 地址栏右侧新增独立历史下拉按钮，与 F4 共用当前标签的最近访问路径菜单；路径编辑框原生右键菜单不变。历史按钮纳入每窗格 Tab/Shift+Tab 顺序，紧接地址控件；紧凑模式下按钮和占位列同步缩窄。
- FocusTraversalSmoke 对 64 种动态停靠点组合验证正反向遍历；完整 Release SettingsSmoke 通过，App Release 构建 0 警告/错误，发布隔离 smoke 通过。生成 `releases/preview-0.6.0-temp.33`。没有启动 GUI；按钮排版、历史菜单交互和地址输入右键行为仍待可见验收。详见[temp.33 预览说明](releases/preview-0.6.0-temp.33.md)。

## 2026-10-25：补充地址历史按钮的隔离 GUI 验收场景

- `src/ExplorerDirs.GuiSmoke/run.ps1` 的待验收清单新增：对比地址历史下拉按钮与 F4 的当前标签路径列表、选择历史后仅导航当前标签、确认按钮进入 Tab/Shift+Tab 顺序，以及地址编辑框右键仍显示文本控件菜单。
- 仅运行 `-PrepareOnly`，准备 `src/.state/gui-smoke/20261024-193543-854-10b24c/`。manifest 为 `prepared-not-run`、四窗格，23 个隔离测试文件；EXE/DLL 与 manifest 哈希一致。没有启动 GUI 或发送输入。可见行为仍待用户安排桌面验收。

## 2026-10-25：复核本机 Q-Dir 44 标签工作区的导入覆盖

- 对 `C:\SoftwareOK\Q-Dir\start.qdr` 运行 SettingsSmoke 的只读 importer 检查：44 个标签映射为四窗格 13/12/10/9，44 个标签均保留视图状态快照与排序列；布局和活动窗格可识别。警告集中在尚未确认的逐窗格树字段、未映射的树宽及未导入的颜色/窗口位置/启动命令。
- 文件 SHA-256 与已登记样本一致。未启动 Q-Dir、ExplorerBrowser 或 GUI，也未改动源配置或导入日常会话。该结果扩大了真实配置样本覆盖证据，不替代真实导入对话框和可见 Shell 行为验收。详见[Q-Dir 导入记录](qdr-import.md)。

## 2026-10-25：准备文件区黑底问题的最小 Q-Dir 导入夹具

- 四窗格验收脚本新增无 `ColsAndSort`、无颜色字段、只指向隔离测试目录的 `qdr-color-baseline.qdr`，待验收序列覆盖新视图基线、取消导入对话框、导入最小配置和重启；用于区分对话框/新宿主时序与视图配置影响。
- `-PrepareOnly` 生成 `src/.state/gui-smoke/20261024-193803-842-eea458/`，manifest 为 `prepared-not-run`，EXE/DLL 哈希匹配；只读 importer 检查识别一个标签，未启动任何 GUI。尚无实际像素证据，根因和修复保持未确认。详见[颜色调查](shell-color-investigation.md#2026-10-25-隔离最小-qdr-夹具准备)。

## 2026-10-25：Q-Dir Classic/Modern 地址栏样式并发布 temp.31

- 新增全局“工具→选项→地址栏样式”：默认 Modern 面包屑，Classic 持续显示完整路径；两者都通过相同的地址输入框和原生 Shell 导航。样式单独写入便携 `AddressBar.ini`，保留外部编辑冲突保护，并加入隔离发布配置复制清单。
- 偏好 smoke 验证默认 Modern、Classic 往返和外部修改拒写；Release SettingsSmoke 全套、App Release 构建（0 警告/错误）及发布隔离 smoke 通过。生成 `releases/preview-0.6.0-temp.31`，包内 EXE/DLL 校验值与清单一致。未启动 GUI；菜单显示、实际焦点与窄四窗格排版待可见验收。详见[temp.31 预览说明](releases/preview-0.6.0-temp.31.md)。

## 2026-10-25：地址栏规范化 Windows 路径分隔符并发布 temp.30

- 对照 [Q-Dir 地址栏 FAQ](https://www.softwareok.com/?faq=146&seite=faq-Q-DIR)，在地址导航入口支持绝对盘符和 UNC 路径中的正斜杠及重复分隔符，例如 `C:/Windows/`、`C:\\Windows\\` 与 `//server//share/folder`。不会规范化相对输入、Shell 命名空间名称、URI 或设备路径；最终目录解析仍交由原生 Shell。
- 新行为的 SettingsSmoke 用例先因缺少实现而失败，接线后 Release SettingsSmoke 全套通过；App Release 构建 0 警告/错误。发布隔离 smoke 通过，`releases/preview-0.6.0-temp.30` 独立 profile 包内 EXE/DLL 哈希与清单一致（EXE `824B4AE2833AC7743B3BE7C68B260CF749CFF885100B984C8979BB367B46ADAB`）。本轮没有启动 ExplorerBrowser 或 GUI，因此输入控件交互及实际 Shell 导航仍待可见验收。经典/现代地址栏外观和自动补全仍是独立差异。详见[temp.30 预览说明](releases/preview-0.6.0-temp.30.md)。

## 2026-10-25：刷新四窗格隔离验收副本（仅准备）

- 运行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 为当前源码 Release 构建并准备 `src/.state/gui-smoke/20261024-192202-739-bf329f/`。manifest 状态为 `prepared-not-run`，初始布局四窗格；四个窗格使用独立测试目录，包含重命名、右键背景菜单、剪贴板、拖放和搜索夹具。EXE SHA-256 为 `CF0021D56FB6937CE0E0457623D160D6ECD76A2192C75BCF265738DA24369286`，DLL SHA-256 为 `271E55FAA7A04761A62F5B200E6F90234BCF474345E84AFD90B509AB7A0797AA`，均与 manifest 相符。
- `PrepareOnly` 在加载 UI Automation 和窗口操作代码之前返回，没有启动测试窗口、发送输入或操作剪贴板。检查时发现另有一个自 2026-10-23 启动的 `preview-0.6.0-temp.2` 进程仍在运行；本次未触碰它。可见四窗格行为和性能数据仍待用户安排桌面验收。

## 2026-10-25：临时预览 temp.29

- 将 F4 每标签最近位置列表和 F9 按需目录递归统计打包为 `releases/preview-0.6.0-temp.29`，使用独立 profile。Release SettingsSmoke、App Release 构建与发布隔离 smoke 通过；无 GUI 验收。详见[发布说明](releases/preview-0.6.0-temp.29.md)。

## 2026-10-25：F9 重解析点跳过的真实目录联接回归

- 扩展递归扫描器 smoke：在隔离目录内创建指向外部测试目录的真实 NTFS junction，断言扫描结果不包含链接目标的 11 字节 sentinel 文件，也不把 junction 当作可下钻子目录。当前本机联接创建与专项测试通过。
- 通过临时反事实改动确认新断言能捕获回归：若目录扫描错误地下钻 junction，结果变为 19 字节、3 个文件、3 个子目录且未读取数为 0，测试按预期失败；恢复产品扫描逻辑后专项通过。仅加强测试，没有改 F9 产品行为，也没有启动 GUI 或 Shell 宿主。

## 2026-10-25：逐行文件夹大小与子项计数列实现边界

- Microsoft `IFolderView2::SetViewProperty` 可为当前视图项目缓存显示属性，但微软标记其从 Windows 7 起弃用，未来可能移除，且搜索结果/库视图不支持。`System.Size` 是可见 `UInt64` 列，`System.FileCount` 则明确不是列；`IColumnManager` 管理现有 Shell 列，不能注册任意派生列。
- 因此 F9 当前目录汇总继续作为已实现能力；逐行大小列只列为可见原生视图隔离验证候选，子项计数列仍需另行评估自定义列/视图宿主，不能用 F9 汇总宣称完成。调查与验证门槛见[逐行文件夹大小调查](folder-size-column-investigation.md)。本轮未启动 GUI 或隐藏 Shell 宿主。

## 2026-10-28：Q-Dir 预览格式过滤实现边界复核

- 复核 FAQ 34 正文及问答区：设置位于 General 预览选项，提示 `*.*` 可预览所有格式；问答直接提到 Preview file extension list，并讨论 PDF/JPG/TIF/ICO。该证据更支持按扩展名限制预览资格，但没有说明列表项是否同时隐藏；正文后半的日期/大小/关键词说明与 FAQ 38 的逐视图文件筛选描述不能互相替代。官方依据：[FAQ 34](https://www.softwareok.com/?faq=34&seite=faq-Q-DIR)、[FAQ 38](https://www.softwareok.com/?faq=38&seite=faq-Q-DIR)。
- 若保留系统 Preview Handler，现有原生窗格接入没有确认可用的逐格式钩子；自建 Handler 宿主需承担 COM/焦点/快捷键生命周期并改变稳定性边界。本轮不误用快速筛选或按选择反复隐藏整窗格；集中 Q-Dir 验收只需确认不匹配文件是否仍显示在列表、预览区域状态及过滤范围，然后再决定是否引入自建宿主。细节见[预览窗格调查](preview-pane-investigation.md#q-Dir-预览格式过滤的实现边界-2026-10-28-复核)。

## 2026-10-25：F9 当前目录递归大小与子项计数

- 对照 Q-Dir 官网 Folder Size 说明（[F9 设置与显示](https://www.softwareok.com/?seite=Freeware/Q-Dir/File_and_folder_size_Column_with_percentage)、[状态栏大小与限制](https://www.softwareok.com/?faq=110&seite=faq-Q-DIR)、[递归计算说明](https://www.softwareok.com/?page=Windows/Tip/Q-Dir/76)），应用新增 F9/工具菜单/工具栏的当前文件系统目录递归计算，结果显示总字节、文件数和子目录数。仅按需运行，后台扫描；再次 F9 取消，导航、切标签、隐藏/卸载时取消；不跟随重解析点，不读取虚拟 Shell 位置，不排序、不持久化。
- 扫描状态栏现在按每 256 个已检查项目报告进度，并在完成时报告尾批项目数；该数是已检查条目，不是百分比或剩余时间。SettingsSmoke 覆盖批次递增和尾批；尚未用可见 GUI 验证状态栏刷新和大目录响应。
- 扫描器专项 smoke 覆盖嵌套目录、字节与计数、空目录、预取消和重解析点跳过；专项通过。完整 SettingsSmoke、App Release 构建和本次 diff 检查均通过。未启动 GUI；状态栏密度/四窗格体验待验收。当前设计先提供当前目录状态栏结果，没有冒充 Explorer 原生“大小/类型”列。

## 2026-10-25：F4 地址栏最近位置列表

- F4 现打开当前标签的最近访问目录菜单；选择目录后只导航该标签。历史在运行时每标签独立维护，最多保留 20 个不同路径，复制标签时复制当前历史；重启后由初始目录重新建立，不写入会话或 INI。
- Release SettingsSmoke 覆盖去重、最近顺序、20 条上限和复制后隔离；完整 SettingsSmoke 通过。App Release 构建及可见菜单、键盘选择、导航焦点尚待验证；本轮未启动 GUI。

## 2026-10-25：刷新本机 Shell 崩溃只读基线

- 使用脱敏 `crash-summary.ps1` 复查滚动 30 天 Windows `Application Error` 1000 事件：Q-Dir 42 条、21 个进程（12.73 七个、12.79 十四个）；Explorer Dirs 一条 0.2.1.0 源码测试事件，发布版事件和未正常退出标记均为零。Q-Dir `0xc0000005` 与 `0xc000041d` 各 21 条；只读状态显示两程序 WER 专用键均缺失。没有获取转储或启用系统设置；新事件没有调用栈，不能据此归因扩展或比较崩溃率。详见[本机 Q-Dir 崩溃证据](q-dir-crash-evidence-2026-10-16.md#2026-10-25-只读复查)。

## 2026-10-25：准备当前源码的四窗格隔离验收副本

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成最新副本 `src/.state/gui-smoke/20261024-185056-046-4547f4/`；`preparation.json` 状态为 `prepared-not-run`，布局为四窗格，每窗格使用独立临时目录，EXE SHA-256 `CF0021D56FB6937CE0E0457623D160D6ECD76A2192C75BCF265738DA24369286`，App DLL SHA-256 `3994BFC3CED24715BE9853E99696344D1027DC1D940F93010FBAB5FF601DEB16`。验收清单新增每窗格 F2 方向键/文本编辑与空白处右键背景菜单场景。脚本分支在 UI Automation 加载及窗口操作之前返回；未启动 GUI、发送输入或使用剪贴板。副本供后续空闲桌面验收，不是发布版；完整范围见其 `preparation.json`。

## 2026-10-25：补充 Q-Dir 官方功能差异清单

- 对照 SoftwareOK 官方帮助与 FAQ，将预览格式过滤、递归文件夹大小/子项计数、地址栏样式和 Quick-Link 管理补入 [Shell 功能差异表](shell-parity.md)。明确快速筛选不等于预览格式过滤，递归大小是需先确定扫描/取消/缓存语义的 Q-Dir 扩展；屏幕放大镜列为低优先级便利项。此项为调研和路线图更新，未开始实现。

## 2026-10-25：临时预览 temp.26

- 发布 `releases/preview-0.6.0-temp.26` 独立 profile 预览包，包含 Shell 文件命令状态查询上下文失效保护和大目录 Shell 计数停滞时提前刷新兜底；当前源码已实现的任意可解析 Shell 文件夹 `.qdr` 导入规则继续保留。Release `SettingsSmoke`、App Release 构建和发布隔离 smoke 通过；程序集产品版本为 `0.6.0-preview.temp.26+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE/DLL SHA-256 与包内清单一致。四窗格验收副本已在 `src/.state/gui-smoke/20261024-183026-607-6607fb/` 准备，`preparation.json` 记录 temp.26 身份和哈希，状态为 `prepared-not-run`。没有启动 GUI；可见文件菜单、自动刷新、虚拟位置导航及 Q-Dir 实际互操作仍待验收。详见[temp.26 预览说明](releases/preview-0.6.0-temp.26.md)。

## 2026-10-25：对标 Q-Dir 的二进制分析许可边界

- SoftwareOK 当前许可页写明不得反编译、逆向工程或反汇编 Q-Dir 可执行文件。项目规则现停止对 Q-Dir 二进制做控制流分析或安装相关分析器；后续继续使用官方公开资料、用户提供的 `.qdr` 样本和正常使用时可观察的产品行为做兼容。
- `TreeNsplit` 的单位与适用范围仍没有可靠证据，保持不导入、不猜测。完整许可来源和边界见[Q-Dir 导入说明](qdr-import.md#目录树字段证据边界2026-10-24)及[官方许可页](https://www.softwareok.com/?seite=Freeware/Q-Dir/Eula)。

## 2026-10-25：活动工具栏文件命令状态跟随 Shell 上下文

- 之前文件命令按钮只在鼠标进入、键盘聚焦、点击命令或展开编辑菜单时查询状态；鼠标停留在工具栏期间通过键盘改选文件或切换标签，按钮会显示旧状态。现在活动窗格在选区变化、导航完成/失败和标签切换时发出上下文变更；只在命令工具栏正被鼠标悬停或键盘聚焦时以 150 ms 合并刷新，窗格切换或离开工具栏后丢弃过期请求。空闲时不查询 Shell 扩展菜单。
- Release SettingsSmoke 的刷新门控覆盖非活动窗格、空闲工具栏、连续事件合并及执行前上下文过期；完整 Release SettingsSmoke 通过，App Release 构建 0 警告/错误。未启动 GUI，可见工具栏启用状态和第三方 Shell 扩展行为仍待桌面验收。

## 2026-10-25：临时预览 temp.25

- 将活动文件命令工具栏状态刷新改动打包到 `releases/preview-0.6.0-temp.25`，采用独立 profile。Release SettingsSmoke、App Release 构建及发布隔离 smoke 通过；产品版本 `0.6.0-preview.temp.25+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 与包内清单一致。未启动 GUI；实际工具栏启用状态和第三方 Shell 扩展仍待桌面验收。

## 2026-10-25：Shell 文件命令状态查询的上下文失效保护

- 文件菜单/工具栏查询选中项与背景命令状态时，若原生菜单准备或 `IsEnabled` 查询触发窗格、导航或选区变化，立即释放当前菜单、停止构造后续菜单，并把整组命令状态标为不可用。避免已过期上下文继续触发另一个 Shell 菜单/扩展的初始化。
- 新回归在旧逻辑下先失败，复现选中项菜单打开期间上下文失效后仍构造背景菜单；修复后验证菜单准备与状态读取两个重入点均会提前停止。Release SettingsSmoke 全量通过；App Release 构建 0 警告/错误。未启动 GUI，真实第三方扩展与可见菜单行为待桌面验收。

## 2026-10-25：大目录 Shell 计数停滞时提前兜底

- 大量文件操作后，Shell 视图计数长时间停在旧值时，不再固定等待完整 12 个 500 ms 检查。连续 4 次观察到同一计数就走现有刷新兜底；计数仍在推进时保留原总上限，新的 watcher 活动会重置等待。小差异、强制刷新、刷新中的选区/焦点恢复策略不变。
- Release SettingsSmoke 新回归在旧逻辑上先失败，修改后全量通过；App/HostProbe Release 构建通过。无插桩隐藏宿主 3,000 项复测两轮及 10,000 项一轮均收敛，watcher 事件完整、最终计数一致、选区与焦点保持。此前同规模旧逻辑样本创建收敛约 5.3 秒，新样本为即时至 1.9 秒，但受 Shell 时序波动影响不作严格性能承诺。未启动 GUI；网络目录、其他 Windows 环境和可见刷新体验仍待验收。

## 2026-10-25：生产 watcher Error 回调恢复验证

- HostProbe 新增隐藏生产宿主模式：向当前 `FileSystemWatcher` 已注册的 Error 委托注入 `InternalBufferOverflowException`，验证产品错误队列、恢复计时器、watcher 实例替换、重建后继续收到创建事件及列表收敛。Release HostProbe 构建 0 警告/错误，实测退出码 0，最终计数 `4/3/3/3` 且 fallback=1。它验证生产回调链但不等于真实 OS 缓冲区溢出，也不验证失败重试、网络恢复或可见 GUI。另一次尝试删除当前打开的被监控目录时阻塞在 Shell 宿主调用并中断，留有一个孤立 HostProbe fixture；清理操作被执行环境策略拒绝。详见[动态更新调查](live-update-investigation.md#2026-10-25-生产-watcher-error-回调与重建路径)。

## 2026-10-25：Shell 位置与原生内存边界复核

- 用户确认虚拟位置应支持所有被当前 Windows Shell 解析并确认为文件夹的绝对解析名称；缺少扩展提供程序时仅对应标签失效。现有 `ShellLocationIdentity` 已按此规则筛选，并持久化可重解析的名称，既有专项 smoke 覆盖别名外位置与 Q-Dir PIDL 往返，因此无需重复改造。
- 继续审查快速筛选 `STRRET`、Shell 命令菜单缓冲区、`PROPVARIANT`/Q-Dir 视图流、目录树通知及 Shell 属性列结构的分配大小、对齐和释放路径，没有发现新的可证实缺陷。Release SettingsSmoke 全套与 App Release 构建通过（0 警告/错误）；未启动 GUI。原生 Shell 交互和可见视图仍按现有验收项待桌面验收。

## 2026-10-25：临时预览 temp.24

- 发布 `releases/preview-0.6.0-temp.24` 隔离预览，包含 64 位快速筛选回调的 `STRRET` 缓冲区尺寸修复。Release `SettingsSmoke`、App/NativeProbe Release 构建和发布隔离 smoke 通过；包内产品版本和 EXE/DLL SHA-256 已核对。未启动 GUI；真实筛选显示行为仍待验收。

## 2026-10-25：修复快速筛选 STRRET 缓冲区边界

- 对照 Microsoft 的结构定义与 .NET 对齐示例确认：64 位 `STRRET` 为 272 字节、32 位为 264 字节。`ShellFolderFilter.TryGetName()` 先前固定分配 264 字节，64 位原生 Shell 写 `STRRET_CSTR` 时可能越过缓冲区尾部。现在按进程位数分配正确大小；SettingsSmoke 回归先失败（64 位期望 272、实际 264），修复后通过。完整 Release SettingsSmoke 和 App/NativeProbe Release 构建通过，App 0 警告/错误；没有运行 ExplorerBrowser 或 GUI，真实 Shell 筛选待验收。

## 2026-10-25：临时预览 temp.23

- 发布 `releases/preview-0.6.0-temp.23` 隔离预览，包含虚拟 Shell 文件夹搜索范围支持。Release `SettingsSmoke`、NativeProbe 虚拟范围探针、App/NativeProbe Release 构建和发布隔离 smoke 通过；包内产品版本和 EXE/DLL SHA-256 已核对。未启动 GUI；完整结果集与可见 Shell 搜索导航仍待验收。

## 2026-10-25：虚拟搜索范围探针失败路径

- 复核 temp.23 后发现，虚拟搜索探针此前即使某个范围解析失败或首项枚举子进程超时，也可能继续返回成功。现在逐项验证搜索文件夹 PIDL 的 `SFGAO_FOLDER`、URI 解析 HRESULT 和枚举结果的 `S_OK/S_FALSE` 一致性；范围异常、子进程错误、超时及缺少 apphost 均使探针以失败状态结束。NativeProbe Release 构建通过；当前机器四个范围及限时首项枚举探针通过，没有改变产品程序或启动 GUI。

## 2026-10-25：Q-Dir 行着色 Shell API 边界复核

- Microsoft Learn 核对确认 ExplorerBrowser 通常创建 DefView；公开接口未记录对现有默认文件视图逐行着色的回调。自定义 `IShellView` 要承担完整内容窗口与 Explorer 交互；旧 `IShellFolderView` 已不再适用于 Windows 7 及之后，且对应 ListView 型系统视图；目录树 `NSTCCUSTOMDRAW` 也不覆盖文件列表。没有改视图架构。黑底根因仍需可见像素实验；规则行着色需要单独评估完整自定义视图及原生交互兼容代价。详见[颜色调查](shell-color-investigation.md#2026-10-25-microsoft-shell-接口范围补查)。

## 2026-10-25：临时预览 temp.22

- 发布 `releases/preview-0.6.0-temp.22` 隔离预览，新增从“工具”菜单打开当前 profile 的崩溃诊断目录。App Release 构建、Release SettingsSmoke 通过；产品版本和 EXE/DLL SHA-256 与包内清单一致。没有启动 GUI；菜单点击和原生崩溃取证仍待验收。

## 2026-10-25：临时预览 temp.21

- 发布 `releases/preview-0.6.0-temp.21` 隔离预览，包含 Q-Dir `tree_mode` 基础字段映射、目录树按钮 Tab 停靠点和键盘 Shell 上下文菜单坐标修正。Release SettingsSmoke、App Release 构建、发布隔离 smoke 与文件夹右键入口隔离 smoke 通过；产品版本和 EXE/DLL SHA-256 与包内清单一致。未启动 GUI 或 Q-Dir，真实键盘/目录树体验和 `.qdr` 互操作待验收。

## 2026-10-25：临时预览 temp.20

- 发布 `releases/preview-0.6.0-temp.20` 隔离预览，包含环境变量刷新回滚失败后的残留变量清理。Release SettingsSmoke、App Release 构建、发布隔离 smoke 通过；产品版本 `0.6.0-preview.temp.20+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 与包内清单一致。没有启动 GUI 或 Q-Dir；实际注册表通知、Shell 交互与 `.qdr` 可见互操作仍待验收。

## 2026-10-25：环境变量刷新回滚失败后的残留清理

- 刷新写入失败时，若某个已应用变量无法回滚，异常现在会携带未恢复的变量名；本进程记住这些名字，下一次刷新即使对应注册表定义已删除，也会尝试清除进程中的残留值。将生产状态合并路径提取为可测试方法后，SettingsSmoke 直接验证它把未恢复名称记入下一轮移除计划；完整 Release SettingsSmoke、App Release 构建通过（0 警告/错误）。尚未启动 GUI；生产注册表变更通知与用户界面流程仍按下方验收项待测。

## 2026-10-25：隔离预览保留目录树宽度配置

- 发布隔离 profile 漏拷 `NavigationTreeWidths.ini`，会让新预览包把逐窗格/共享树宽度重置为默认值。发布 smoke 增加该配置文件的原文复制断言，先按预期失败；`scripts/publish.ps1` 已纳入复制清单，发布隔离 smoke 通过。全量 Release SettingsSmoke、App Release 构建（0 警告/错误）通过后，生成 `releases/preview-0.6.0-temp.19`，产品版本 `0.6.0-preview.temp.19+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL 哈希与清单一致。没有启动预览、ExplorerBrowser 或 Q-Dir；GUI 与实际 Q-Dir 互操作待验收。

## 2026-10-25：Q-Dir 目录树宽度字段资料复核

- 复查 SoftwareOK 当前英文功能页（列出 Q-Dir 12.81）及 FAQ 117，公开 `.qdr` 示例仍只给出 `TreeNsplit=2500`，没有单位、方向或模式说明；本机 12.79 的二进制身份与旧记录一致。REA 自动分析 provider 当前不可用，且未安装分析器或启动 Q-Dir，因此没有新增可安全映射的证据。保留 `TreeNsplit` 不导入；下一步需要真实目录树宽度变化前后的 `.qdr` 样本或可用控制流分析。详见[Q-Dir 导入范围](qdr-import.md#目录树字段证据边界2026-10-24)。

## 2026-10-25：`.qdr` Explorer Dirs 扩展保存目录树宽度

- `.qdr` 导出现在可在 Explorer Dirs 扩展 v4 中保存共享树与四个窗格的五路宽度；有效范围 96–600 DIP。导入预览列出宽度，确认后通过 `NavigationTreeWidths.ini` 的并发保护保存并应用于当前窗口。旧扩展 v1–v3 继续可读且不会重置当前宽度；缺失或无效 v4 宽度会拒绝整个扩展状态并退回可识别的 Q-Dir 基础字段。导入前备份也携带当前宽度。Q-Dir 原生 `TreeNsplit` 仍不映射，避免混淆其未知单位。扩展导入导出回归、完整 Release SettingsSmoke、App Release 构建和 diff 检查通过；没有启动 GUI 或 Q-Dir，真实预览确认/导入、重启及跨 Q-Dir 互操作待验收。

## 2026-10-25：目录树宽度可拖动调整

- 逐窗格目录树和共享目录树现在都有 WPF 拖动分隔条；逐窗格宽度互相独立，共享树宽度单独保存。默认值为 160/190 DIP，允许范围 96–600 DIP；用户完成拖动时写入独立便携 `NavigationTreeWidths.ini`，打开/关闭树、切换模式或改变窗口尺寸不会覆盖宽度。损坏值先备份并保留可读宽度，新格式或外部编辑保护原文件。
- 新增 focused SettingsSmoke 覆盖默认值、五路独立往返、边界、损坏/缺失字段备份与默认回退、并发外部编辑及新版格式保护，通过；App Release 构建通过，0 警告/错误。没有启动 GUI；拖动手感、窄窗格布局和重启后可见恢复仍待桌面验收。该宽度设置独立于尚未解码的 Q-Dir `TreeNsplit`，没有声称其字段互操作。

## 2026-10-25：接通 Q-Dir `.qdr` 树模式映射

- 本机 Q-Dir 12.79 的菜单资源 `Q-Dir.exe`/`RT_MENU 0x80` 将命令 `0x806b/0x806c/0x806d` 指向字符串键 `54003/54001/54002`；官方英文字符串表分别为 “Do not use”/“One 4 all”/“Each has one”。结合代码中 `tree_mode=0/1/2` 选择上述命令，已确定并接入 0=关闭、1=共享树、2=逐窗格树。`.qdr` 导入与导出现在都会读取/写入基础 `tree_mode` 字段，编辑该字段会使旧 Explorer Dirs 扩展校验失效并采用新值。Release `SettingsSmoke`、App Release 构建及 diff 检查通过；没有启动 Q-Dir 或可见 GUI。`Tree1`～`Tree4` 0/1 的实际可见状态语义仍未验收；`TreeNsplit` 宽度仍提示不支持。

### 同日 `TreeNsplit` 静态读取路径补查

- 本机 Q-Dir 12.79 PE 的静态字符串交叉引用显示 `Tree1split`～`Tree4split` 走同一整数配置读取路径，默认值均为 `2500`；值进入四个 pane 状态字段，并参与后续配置序列化。对这些对象偏移的直接寻址扫描只看到读取/序列化路径，未见树控件布局计算的直接引用，但不能排除动态索引/别名访问。证据不含非默认样本，也未追到宽度换算的完整路径，因此不能把 `2500` 解释为比例或直接用于本程序的树宽恢复。导入继续跳过并提示 `TreeNsplit`；详见[Q-Dir 导入说明](qdr-import.md#2026-10-25-treensplit-静态读取路径补查)。没有启动 Q-Dir 或 GUI。

## 2026-10-25：Tab 遍历纳入每窗格目录树按钮

- 源码对照发现每窗格树模式会显示导航栏“树”按钮，但自定义 Tab 停靠点表遗漏该控件，窗口级 Tab 接管使其不能通过 Tab/Shift+Tab 到达。现将可见树按钮纳入刷新按钮与地址栏之间；切换到共享树/关闭树模式时随按钮隐藏而移出停靠点。F6 导航区仍落到地址栏。Release `SettingsSmoke` 对树按钮显隐、搜索/快速筛选、列标题的 32 种停靠点组合通过；后续 Release `SettingsSmoke` 与 App Release 构建通过（0 警告、0 错误），相关 diff 检查通过。没有启动 GUI，真实键盘焦点和原生 Shell 控件互动仍待验收。

## 2026-10-25：键盘触发的 Shell 右键菜单坐标识别

- `WM_CONTEXTMENU` 的键盘触发坐标在 32 位消息字段中是 `(-1,-1)`。回归先用零扩展的 `0x00000000FFFFFFFF` 传入 64 位 `LPARAM`，复现旧判断误走鼠标命中探测、未识别键盘菜单；现以低 32 位哨兵判定，兼容零扩展与符号扩展，继续让键盘菜单按当前 Shell 选择插入自定义项。完整 Release `SettingsSmoke` 与 App Release 构建通过（0 警告、0 错误），相关 diff 检查通过。没有启动 GUI；Shift+F10、菜单键及多窗格可见菜单仍待人工验收。

## 2026-10-25：临时预览 temp.18

- 将标签栏空白处接收单个文件系统文件夹/驱动器并创建新标签的改动打包到 `releases/preview-0.6.0-temp.18`，使用独立 profile。完整 Release `SettingsSmoke`、App Release 构建与发布隔离 smoke 通过；程序集产品版本 `0.6.0-preview.temp.18+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL 哈希与清单一致。未启动 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir；原生 CIDA 虚拟文件夹解析 smoke 通过，真实虚拟 Shell 拖放来源、空白区命中、导航和恢复待验收。详见[第十八版说明](releases/preview-0.6.0-temp.18.md)。

## 2026-10-25：确认 `.qdr` 虚拟位置兼容范围

- 用户确认 `.qdr` 可导入任意能由当前 Windows Shell 解析并确认为文件夹的绝对名称；若命名空间提供程序缺失，只跳过该位置并提示。现有实现已按此规则解析，不再限制于固定别名。本轮新增 Windows Network Shell 文件夹的 PIDL 导入、绝对身份保存、Q-Dir `shell:::{GUID}` 基础字段导出及移除本程序扩展后的再次导入回归；位置/Q-Dir 专项与完整 Release `SettingsSmoke` 通过。未启动 GUI 或 Q-Dir；第三方 Shell 扩展缺失/变化时的可见恢复仍待验收。

## 2026-10-25：修复 `.qdr` 明文文件路径被当作标签

- 导入器原先对明文绝对路径只做格式归一化，没有确认目标是文件夹；编码 PIDL 和虚拟 Shell 名称则已验证 `SFGAO_FOLDER`。现将明文路径同样交给 Windows Shell 解析并确认文件夹，文件或失效目录会按单标签跳过，其他有效标签继续导入。新增回归先复现 `.txt` 文件被接受，再验证混合文件/有效虚拟位置及真实目录编码导入；UTF-16、UTF-8 BOM 加载夹具也改用实际存在的目录。Shell/Q-Dir 专项、完整 Release `SettingsSmoke` 与 App Release 构建通过（0 警告/错误）；没有运行 GUI 或 Q-Dir。

## 2026-10-25：Q-Dir 树字段静态读写线索

- 通过本机 `objdump` 只读检查，在代码中找到对 `tree_mode` 和 `Tree1split`～`Tree4split` 的直接引用：相关 UI 代码将 tree mode 值归一化到 `0–2` 并选择三个 UI 资源 ID；每个 `TreeNsplit` 进入视图配置读取路径，默认参数可见为 `2500`。资源 ID 与菜单文字/共享、逐窗格、关闭模式的对应关系尚未解出，`2500` 的单位、方向和适用视图也未确认，因此暂不导入这些值。REA 无可用 PE 分析 provider；没有启动 Q-Dir、改变配置或安装分析工具。后续需解出资源文字和消费分隔值的调用路径，或取得可控的启用/改宽样本。

### 补充：菜单资源 ID 不能直接当作字符串（2026-10-25）

- 只读解码 `Q-Dir.exe` 的 PE 资源目录后确认，代码中的 `0x806b`～`0x806d` 是被 `tree_mode` 分支选择的 UI/命令资源 ID；RT_STRING 资源只有块 ID `0x9` 与 `0xe01`，并不存在可按 `0x806b` 直接读取的对应字符串块。此前把下一步表述为“从字符串资源直接解出三个菜单标签”不准确，现改为需要追踪这些命令 ID 对应的菜单资源/创建路径，或获取受控配置样本。枚举值及 `TreeNsplit=2500` 的比例语义依旧未确认，不据此修改导入行为。

## 2026-10-25：临时预览 temp.17

- 将 temp.16 后的 watcher 失效恢复/元数据协调、`.qdr` 预览字段及树模式扩展保存、Shell 重命名方向键路由和 Ctrl+Y 原生重做兜底打包到 `releases/preview-0.6.0-temp.17`，使用独立 profile。完整 Release `SettingsSmoke`、Shell 位置/Q-Dir 导入专项、App/HostProbe/NativeProbe Release 构建及发布隔离 smoke 通过。程序版本 `0.6.0-preview.temp.17+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE/DLL SHA-256 与包内清单一致。未启动 GUI、ExplorerBrowser、探针或 Q-Dir；可见交互、自动刷新表现及 Q-Dir 实际 `.qdr` 互操作仍待验收。详见[第十七版说明](releases/preview-0.6.0-temp.17.md)。

## 2026-10-25：标签栏接收 Shell 文件夹拖放（代码已接入，待桌面验收）

- 对照 Q-Dir FAQ 64 补齐差异：空白标签栏接收一个文件夹/驱动器，创建并选中新标签。Shell PIDL 数据通过 `SHCreateShellItemArrayFromDataObject` 解析；普通 `CF_HDROP` 走单一路径解析，两条路径都要求 `SFGAO_FOLDER`，并复用现有会话位置身份。测试发现 Shell 数组 API 不接受仅含 `CF_HDROP` 的 WPF 数据对象，设计稿已据此修订。文件、多项拖放、URI 与未解析项拒绝；私有标签排序拖放路径保留。新增决定逻辑 smoke 和临时目录/文件的原生 `CF_HDROP` 冒烟通过。没有启动 GUI；虚拟 Shell PIDL 拖放、鼠标空白区命中、建标签导航/恢复及 Q-Dir 可见行为仍待桌面验收。计划见[实现计划](superpowers/plans/2026-10-25-tabbar-shell-folder-drop.md)，细节见[设计稿](superpowers/specs/2026-10-24-tabbar-shell-drop-design.md)。

## 2026-10-24：临时预览 temp.16

- 打包共享目录树/逐窗格树模式、布局切换后的活动树同步、任意已确认 Shell 文件夹解析名会话与 `.qdr` 支持，以及未确认 Q-Dir 树字段的导入提示到 `releases/preview-0.6.0-temp.16`，使用独立 profile。Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建和发布隔离 smoke 通过；程序集版本 `0.6.0-preview.temp.16+77495f5d72a4739c2bb33a21d021328294f728c0`；EXE/DLL SHA-256 与包内清单一致。没有运行 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir；可见树/焦点、Shell 虚拟位置导航、自动刷新、跨窗格拖放及 Q-Dir 实际互操作仍待验收。详见[第十六版说明](releases/preview-0.6.0-temp.16.md)。

## 2026-10-24：临时预览 temp.15

- 打包自动 watcher 恢复到 `releases/preview-0.6.0-temp.15`，独立 profile；全量 Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建及发布隔离 smoke 通过。程序版本 `0.6.0-preview.temp.15+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL 哈希与清单一致。没有运行任何 GUI/ExplorerBrowser/探针/Q-Dir；真实自动刷新恢复、网络重连和可见交互仍待验收。详见[第十五版说明](releases/preview-0.6.0-temp.15.md)。

## 2026-10-24：Q-Dir 目录树字段静态复核

- 复核本机 Q-Dir PE：REA 识别为 x64，SHA-256 `9adaa5994a476f06f0dc9cdf5eae9cc0986cbfc43055fb9f05341f803fb21fca`；深度分析 provider 不可用，GNU `objdump -t` 显示无符号表。官网 FAQ 117 只有字段示例值，未定义 `tree_mode` 枚举或 `TreeNsplit` 单位；未运行 Q-Dir，也没有更改二进制。不能从现有静态证据确定映射，导入器继续提示不支持，待受控的可见树/宽度变化 `.qdr` 样本或正式字段文档。详见[Q-Dir 导入范围](qdr-import.md#目录树字段证据边界2026-10-24)。

## 2026-10-24：跨窗格拖放验收夹具准备

- `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 现在在隔离 pane-1 中准备唯一的复制/移动源文件，并在待验收清单中记录 Ctrl 拖动复制与默认拖动移动的结果核对。已生成 `src/.state/gui-smoke/20261024-153044-996-0ffd78`；状态为 `prepared-not-run`，EXE/DLL 哈希与清单一致，夹具内容已核对。没有启动窗口或执行拖放；真实 `DoDragDrop`、源/目标视图和焦点验收仍待桌面授权。

## 2026-10-24：临时预览 temp.14

- 为纳入 `.qdr` 导出快照的目录树模式修正，打包独立 profile 预览版；全量 Release `SettingsSmoke`、App/HostProbe/NativeProbe Release 构建及发布隔离 smoke 均通过。发布后核对包内 EXE/DLL 哈希和产品版本；不启动任何 GUI。真实窗口与 Q-Dir 实际互操作继续待验收，详见[第十四版说明](releases/preview-0.6.0-temp.14.md)。

## 2026-10-24：FileSystemWatcher 缓冲区溢出恢复路径复核

- 微软文档与 .NET 文件提供程序源码确认缓冲区溢出会丢事件，消费者应重新扫描；产品 Error 回调已接入强制 Shell 刷新，16 KiB 缓冲区暂不扩大以避免无证据增加不可分页内存。纯逻辑 Error/强制刷新覆盖已存在；本轮补记证据并保留真实溢出和可见视图收敛待验收，详见[动态更新调查](live-update-investigation.md#2026-10-24-filesystemwatcher-缓冲区溢出恢复路径复核)。

## 2026-10-24：FileSystemWatcher 目录失效恢复

- 临时目录实验复现删除/重建被监视目录后，原 `FileSystemWatcher` 收到 `Win32Exception` 且不再报告同路径新文件。产品监视器现在会在首次创建 watcher 失败或运行中收到 Error 后，通过单一待处理计时器重建；失败时 250 ms 起指数退避、最长 30 s，恢复成功再请求强制协调，导航切换时退休重试。新增回归确认监视器退休后迟到的失败回调不会重新安排重试。恢复策略 smoke、完整 SettingsSmoke、App 与 HostProbe Release 构建通过，均 0 警告/错误。没有运行 ExplorerBrowser，因此真实列表、网络重连和选择焦点恢复仍待验收，自动刷新 P0 保持开放。详见[动态更新调查](live-update-investigation.md#2026-10-24-watcher-失效后的同路径恢复)。

## 2026-10-24：STA Refresh 长耗时的计时边界复核

- 校正既有 trace 解释：计时覆盖 `Refresh(Action?)` 全部同步阶段，含视图获取与选区捕获，不能把 568 ms 全归为原生 `IShellView.Refresh()`；后续选区恢复异步排入 Background Dispatcher。计数读取在已有隐藏样本中很短，分阶段耗时仍未测，自动刷新 P0 继续开放。源码复核确认 HostProbe 已输出同步子阶段及独立的异步完成阶段；各指标逐次记录，但异步完成不可计入同步 STA 时长，也不能把阶段最大值相加。本轮未运行 HostProbe，没有新增压力数据。下一步需用低扰动配置采集可比样本，详见[动态更新调查](live-update-investigation.md#2026-10-24-sta-refresh-长耗时的计时边界复核)。

## 2026-10-24：Refresh 同步阶段诊断字段

- Shell callback trace 开启时，现额外统计视图获取、选区快照与原生 `IShellView.Refresh()` 三个阶段，并加入 HostProbe 已读取的诊断快照；默认关闭时不启动这些阶段计时。App Release 构建和 SettingsSmoke 通过，HostProbe 只构建待确认；未采集新运行数据，也不代表 STA 长尾已改善。详见[动态更新调查](live-update-investigation.md#2026-10-24-sta-refresh-长耗时的计时边界复核)。

## 2026-10-24：Q-Dir 导出保留目录树模式

- 导出入口改为使用完整窗口会话快照，避免手工快照遗漏 `TreeDisplayMode`，导致共享树/关闭树配置在 `.qdr` 扩展往返时回退到逐窗格模式；同时明确导入警告为“无效路径或非可用文件夹”。三种树模式扩展往返和完整 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误。
- 按已确认范围，虚拟 Shell 标签使用可解析的绝对名称并通过 `SFGAO_FOLDER` 检查；系统中缺少对应命名空间扩展时，仅该标签可能无法恢复。无需 GUI 的验证不证明第三方扩展安装变化后的可见恢复行为。

## 2026-10-24：Q-Dir 目录树字段证据复核

- 官方 [FAQ 26](https://www.softwareok.com/?faq=26&seite=faq-Q-DIR) 确认共享单树用于全部窗格，[FAQ 32](https://www.softwareok.com/?faq=32&seite=faq-Q-DIR) 确认可分别启用各视图目录树；FAQ 117 列出 `tree_mode`、`Tree1`～`Tree4` 与 `Tree1split`～`Tree4split`，但没有给数值枚举或比例单位。本程序已实现共享树/逐窗格树/关闭三种会话模式及 `Tree1`～`Tree4` 暂定 0/1 的导入导出，并在导入预览警告该映射未经 Q-Dir 可见互操作确认。官方 FAQ 117 的公开 `.qdr` 示例还含 `tree_mode=0`、全部 `TreeN=0` 和 `TreeNsplit=2500`；英文字符串清单有三个树模式选项名称，但仍未给数值到模式的对应关系。本轮 objdump 找到 `tree_mode` 相关代码将值归一化到 0～2 并选择三个不同 UI 资源 ID，四个 `TreeNsplit` 均进入配置读取路径且传入 2500 默认参数；尚未把资源 ID 还原为菜单语义，也未找到分隔值的消费单位/方向。字段仍未映射。静态分析 provider 当前不可用。
- 两份本机 Q-Dir 12.79 `.qdr` 样本和本机 `Q-Dir.ini` 均是树不可见配置；后者重复记录 `tree_mode=2` 与 `show_tree=0`。官方 FAQ 32 的问答区确认 “One 4 All”“Each has one”“None has one” 三种功能模式，但没有数值对应关系。关闭态样本不能证明枚举值或宽度单位。Q-Dir 二进制字符串确认字段格式串及菜单标签，但本轮 REA PE provider 不可用，普通 `strings`/`objdump` 输出不足以建立读写控制流。未启动 Q-Dir。后续需可控的树启用/共享模式与改宽后的配置样本，再验证导入导出；样本哈希及详细证据见[Q-Dir 导入说明](qdr-import.md#目录树字段证据边界2026-10-24)。

## 2026-10-24：导出 Q-Dir 预览字段

- `.qdr` 导出现在写入 `Vorschau=1/0`，取当前活动窗格的当前标签；本程序扩展仍保存全量逐标签差异。`Vorschau` 纳入基础字段哈希，外部修改后拒绝旧扩展覆盖，旧格式缺少该键时仍按旧字段集合校验。新回归先在旧导出器下因缺少基础字段失败，接入后 Release `SettingsSmoke` 全量通过；App Release 构建 0 警告、0 错误，相关文件 `git diff --check` 通过。
- 该全局字段代表活动视图是为 Q-Dir 基础格式做的映射；本机两份 Q-Dir 样本均为关闭态。Q-Dir 实际打开并显示预览、跨版本读取以及异质标签状态的行为仍待可见互操作验收。

## 2026-10-24：导入 Q-Dir 全局预览开关

- `.qdr` 的 `Vorschau=1/0` 现在映射到本次导入全部标签的原生预览窗格状态；缺失时维持默认关闭，其他值不启用并产生字段警告。新回归先在旧实现下因预览仍关闭而失败，再通过；完整 Release `SettingsSmoke` 通过。
- Q-Dir FAQ 说明预览可用于各文件视图，但本机 Q-Dir 12.79 两份配置样本都为 `Vorschau=0`，尚无启用态可见互操作证据。本项目扩展仍保存逐标签预览差异；Q-Dir 单一基础字段无法表达这些差异。目录树、`TreeN` 和 `TreeNsplit` 仍未导入，不根据关闭态样本推断启用或比例含义。App Release 构建与可见预览渲染待后续验收。

## 2026-10-24：创建/删除净计数抵消时自动刷新

- 修复 watcher 只记录创建/删除净增量导致的漏检：等量删除与创建可能替换条目而不改变项目数；队列在一次派发窗口内、批次在多个 Drain 之间保留两类事件，二者同时出现时强制执行已有 Shell 刷新与选区/焦点恢复。
- 新回归先在旧逻辑上失败；Release `SettingsSmoke` 全套通过，另用真实 Windows `FileSystemWatcher` 在唯一临时目录执行等量删除/创建并经过生产队列与批次，App Release 构建 0 警告、0 错误，相关文件 `git diff --check` 通过。未启动 ExplorerBrowser；可见替换、批量操作成本、网络目录与焦点体验仍待验收，自动刷新 P0 保持开放。详见[调查记录](live-update-investigation.md#2026-10-24-创建删除净计数抵消时强制协调)。

## 2026-10-24：自动刷新/焦点恢复无窗口回归复测

- 复核 10k 刷新焦点记录后确认，原先发现的 focused PIDL 暂不可枚举问题已有生产延后恢复：同一导航与输入代际下每 100 ms 重试，最多 1.5 秒，期间 watcher 不并发触发计数刷新；已有 Release 隐藏宿主 10k 复测、键盘/鼠标合成输入取消集成回归覆盖该路径。本轮未重复改动已存在的恢复逻辑。
- 当前源码完整 Release `SettingsSmoke` 再次输出 `Settings smoke: PASS`；App Release 构建 0 警告、0 错误。回归覆盖刷新与焦点恢复门控、快捷键和会话等无窗口逻辑。此结果不覆盖 ExplorerBrowser 可见交互、真实物理输入、慢速/网络位置或长时间运行，自动刷新 P0 仍开放。

## 2026-10-24：自动刷新双通道静默检测边界

- 追踪生产轮询从 watcher 队列取增量、并入批次、与 Shell 项目数比较的路径：当两种通知都没有变化、watcher 也未报告 Error 时，计数状态机没有独立磁盘枚举信号触发刷新。这是源码边界推断，尚非真实漏报复现。
- 未增加持续目录扫描或改变刷新策略；先保留现有行为与 I/O 成本边界。慢速/网络目录、静默漏报和可见窗口更新仍属于自动刷新 P0，详见[调查记录](live-update-investigation.md#2026-10-24-watcher-与-shell-同时静默时的检测边界)。

## 2026-10-24：原生重命名编辑器保留方向键路由回归

- 将生产 `ExplorerBrowserHost` 中的 F2 编辑器方向键判定提取为纯消息策略并接入 SettingsSmoke：仅 `WM_KEYDOWN` 的左/上/右/下方向键被直接发给当前原生 Edit；KeyUp、系统键和普通按键继续原路由。新断言先因策略缺失编译失败，接入后 focused smoke 与 Release SettingsSmoke 全量通过。
- App Release 构建通过，0 警告、0 错误。该 smoke 验证消息决策与生产接线，不模拟实际多窗格原生编辑控件；物理 F2 后方向键和重命名焦点仍待 GUI 验收。

## 2026-10-24：保留 Shell 的 Ctrl+方向键多选快捷键

- 对照快捷键表核实，应用解析器不会处理 Ctrl+↑、Ctrl+↓ 或 Ctrl+空格；新增 SettingsSmoke 断言，防止后续菜单/快捷键扩展占用原生 Shell 的逐项多选组合。
- Release SettingsSmoke 全量通过。此项仅验证应用不拦截按键；文件视图的实际多选状态仍待可见 GUI 验收。

## 2026-10-24：Q-Dir Shell 绝对名称导入回归

- 按用户确认的兼容范围，补测任意可解析且确认为文件夹的 Shell 绝对名称：Q-Dir FAQ 的 `shell:::{GUID}`“此电脑”语法归一化为本项目 Shell 身份；不存在的命名空间仅跳过对应标签并提示，其他有效目录仍导入。Release SettingsSmoke 全量通过。
- 验证覆盖 Shell 解析和导入结果，不覆盖第三方 Shell 扩展缺失/安装切换后的真实 GUI 恢复行为；已有 `ShellLocationIdentity` 已按 `SFGAO_FOLDER` 确认目标类型。

## 2026-10-24：Q-Dir 行着色的 Shell 扩展边界

- 按 Shell 官方架构文档复核：ExplorerBrowser 通过 `IShellFolder` 选择默认 `DefView`，命名空间扩展可提供完整的自定义 `IShellView`；官方图标覆盖接口仅覆盖图标图像，不是整行颜色。由此确认逐行着色不是当前默认文件视图的轻量配置项；更换视图将承担枚举、选择、菜单和其他 Shell 交互兼容成本。此为官方接口职责与工程影响推断，不是本机运行验证；没有更改产品行为。导入后黑底仍需真实窗口复现，Q-Dir 规则颜色单独进入架构决策。详见[颜色调查](shell-color-investigation.md#2026-10-24-原生-shell-行着色扩展点复核)及[Shell 对照](shell-parity.md)。

## 2026-10-24：临时预览 temp.13

- 将当前源码打包至 `releases/preview-0.6.0-temp.13`，使用独立 profile。发布隔离 smoke、全量 Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建通过（均 0 警告/错误）；DLL 产品版本为 `0.6.0-preview.temp.13+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE/DLL SHA-256 与包内清单一致。没有启动 App、ExplorerBrowser、探针或 Q-Dir。此包供功能预览，不代表可见 GUI、导入后颜色、自动刷新长尾或 Q-Dir 互操作验收完成。详见[第十三版预览说明](releases/preview-0.6.0-temp.13.md)。

## 2026-10-24：Windows 11 右键“显示更多选项”验收项

- 复核微软现行 [File Explorer 快捷键清单](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows) 时发现遗漏了 `Shift+右键`“显示更多选项”。ExplorerBrowser 子类过程对 `WM_RBUTTONDOWN` / `WM_CONTEXTMENU` 只记录菜单目标并继续调用 `DefSubclassProc`，未接管菜单显示；键盘表现已记录为原生 Shell 转发，并将 Windows 11 菜单类型加入待验收步骤。未改运行代码、未启动 GUI；显示的具体菜单和条目目标仍需桌面确认。

## 2026-10-24：Ctrl+Y 原生 Shell 重做兜底

- 文件视图快捷键审计发现 Undo 已有 `Ctrl+Z` Shell 命令兜底，但 Windows 通用文件操作快捷键 `Ctrl+Y` 没有对应 Redo 路由。现先让 `IShellView.TranslateAccelerator` 处理；未接手时发送原生 `FCIDM_SHVIEW_REDO`（`0x701C`），不在 WPF 实现文件操作。地址栏和 Shell 重命名 Edit 仍走各自控件的文本编辑路径。
- 独立 ShellFileCommands smoke 先因缺少 Ctrl+Y 映射失败，加入映射后通过；覆盖 plain Y 与 Ctrl+Shift+Y 不被拦截。App Release 构建与 GUI 操作未在本项中运行；Native Shell 实际 redo 可用状态、Undo→Redo 行为及焦点仍待隔离桌面验收。

## 2026-10-24：Watcher 并发回归的交叠确定性

- 复核发现并发队列烟测虽检查了生产和 Drain 确有重叠，但原先靠 `Thread.Yield()` 争取调度；极端调度下可能在生产者完成后才消费，令“确实交叠”断言依赖时序。现以 producer/consumer 握手固定交叠：一个生产者先提交一批事件并暂停，消费者必须在该生产者仍活动时 Drain 到非空批次，之后生产继续。
- Release `SettingsSmoke` 全套通过，并对同一已构建程序连续运行 5 次均通过；4 个生产者合计 20,000 个变更仍守恒，强制刷新标记保留。此回归只验证并发队列逻辑，不启动 ExplorerBrowser，也不证明实际文件系统通知到 Shell 视图的端到端刷新或长尾性能。

## 2026-10-24：Q-Dir 12.82 对标基线与当前视图导出范围确认

- 复核 Q-Dir 官网后，将对标表中官网版本从 12.81 更新到 12.82（官网标注 2026-10-24 更新）；本机 Q-Dir 12.79 仍单独作为既有静态检查基线，不混为同一版本证据。
- 用户确认“导出当前视图”应输出当前窗格整份可见视图，即使有选中文件也不缩小到选中项。现有 Shell 快照与导出回归按此语义保留；Release SettingsSmoke 全套通过，App Release 构建 0 警告/错误。
- 该验证不覆盖可见导出菜单/保存对话框、Excel 实际打开或 Q-Dir 12.82 读取导出结果；本轮不重建发布包。桌面可见验收清单继续留待用户预览 temp.11 后安排。

## 2026-10-24：Watcher 队列并发交接回归

- SettingsSmoke 新增并发生产者/刷新批次消费用例：4 个生产者共提交 20,000 个目录变化，独立消费者在生产期间并发 Drain；核对总增量守恒、强制刷新标记未丢，且测试确实发生重叠消费。Release SettingsSmoke 全套通过，`git diff --check` 通过。
- 这是纯队列并发检查，不创建 ExplorerBrowser 或可见窗口；它不覆盖启动基线与 Shell 通知竞态、真实目录 watcher 到视图的端到端刷新，也不关闭自动刷新 P0。可见 UI、慢速/网络目录及大目录长尾继续待验收。

## 2026-10-24：临时预览 temp.11

- 将 watcher 启动期变更缓存、慢枚举继续等待，以及当前目录元数据变化刷新一并打包至 `releases/preview-0.6.0-temp.11`。重建发布版本 `0.6.0-preview.temp.11` 后，EXE/DLL 哈希分别与包内文件逐字节一致。Release 隔离 smoke、完整 SettingsSmoke、App/HostProbe/NativeProbe Release 构建均通过，0 警告/错误；独立 profile 复制现存配置文件。未启动 App 或任何 Shell 宿主；实际可见更新、启动期事件与 Shell 的交互和 Q-Dir 互操作仍待验收。详见[第十一版预览说明](releases/preview-0.6.0-temp.11.md)。

## 2026-10-24：监听当前目录文件元数据变化

- 源码核对发现生产目录 `FileSystemWatcher` 原先只订阅文件名和目录名；现有文件写入后改变大小/修改时间，以及属性或创建时间变化，不会进入目录刷新队列。现在增加 `LastWrite`、`Size`、`Attributes`、`CreationTime` 监听，并把 `Changed` 映射为零项目数增量的强制刷新请求，沿现有 watcher/Dispatcher/250 ms 视图刷新队列合并；不递归监控子目录。
- 新增映射 smoke，覆盖监听标志、创建/删除计数、修改/重命名/错误的强制刷新语义；另在唯一临时目录用真实 Windows `FileSystemWatcher` 追加文件内容并确认收到 `Changed`。新增断言先因生产映射文件缺失而构建失败，接入后完整 Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建通过，0 警告/错误。未启动 ExplorerBrowser/HostProbe，真实视图元数据列更新、事件突发和网络目录行为仍待验收。该源码改动已打入 temp.11。

## 2026-10-24：临时预览 temp.10

- 将 temp.9 之后的源码改动打包至 `releases/preview-0.6.0-temp.10`：状态栏“查看”入口进入键盘焦点遍历；选中项目大小读取按视图/选择缓存并节流；Shell 文件操作后的焦点恢复增加前台窗口归属门控。
- 发布隔离 smoke、Release SettingsSmoke 与 App/HostProbe/NativeProbe Release 构建均通过，0 警告/错误；包内 EXE/DLL 哈希已与清单核对，程序集产品版本为 `0.6.0-preview.temp.10`，独立 profile 仅复制现存配置文件。未启动该包或任何 Shell 探针；实际 GUI、Q-Dir 互操作和性能体验仍待验收。详见[第十版预览说明](releases/preview-0.6.0-temp.10.md)。

## 当前状态校正：自然删除时的选区丢失结论已撤回

- 路线图较早记录的“Shell 自然更新计数后会清空未受影响选区”来自探针选区基线失配；后续检查发现测试通过 COM 改选区，却未同步产品输入代数和快照。按生产输入/快照基线重跑后，隐藏 `IFileOperation`、watcher 与显式更新用例保留了未受影响选区和焦点；详见[调查更正](live-update-investigation.md#2026-10-24-更正自然删除选区丢失是探针基线失配)。不再将此项当作已确认缺陷，也不据此增加恢复逻辑。此证据仍不代替可见 ExplorerBrowser/Explorer 对照。
- 自动刷新 P0 仍因 STA 响应长尾、慢速/网络位置和可见文件更新体验未验收而开放；下一步继续处理这些仍有证据支持的缺口。

## 2026-10-24：减少状态栏重复读取大选区大小

- 状态栏项目数每 750 ms 刷新，同时原先每轮都在 Shell STA 重读选择集 `System.Size`；本机 1,000 项快速属性聚合曾测得约 28 ms。现在按视图代际、选择版本和选中数缓存大小，选区/视图变化时下轮立即重读，静态选区最多每 5 秒重读以发现原地大小变化。
- 门控 smoke 覆盖首次读取、稳定命中缓存、选择/视图/数量变化失效、5 秒过期及空选择；Release SettingsSmoke 与 App 构建通过。未启动 ExplorerBrowser，四窗格可见响应和网络/云属性提供方耗时仍待验收。

## 2026-10-24：Shell 文件操作后的焦点恢复前台归属门控

- 源码复核发现，延迟焦点恢复只依赖 WPF `IsActive`，随后可能调用 `FocusView()`，没有确认当前前台窗口属于目标窗格所在窗口。现在仅当前台为空，或前台窗口的 root owner 是目标窗口时才允许继续；前台属于其他应用或另一个 Explorer Dirs 窗口时跳过恢复。
- 新增门控 smoke 覆盖无前台、同窗体拥有的对话框及外部/其他应用窗口三种情况。完整 Release SettingsSmoke 与 App Release 构建通过，0 警告/错误；未启动 GUI，Windows 前台归属调用和真实 Shell 对话框焦点恢复仍待可见验收。

## 2026-10-24：Explorer 快捷键清单补验

- 对照微软现行 File Explorer 快捷键表，发现项目清单未显式列出 `Ctrl+D` 删除到回收站及 `Shift+F10` 打开上下文菜单。`Ctrl+D` 未被应用快捷键解析器拦截，未识别的文件视图按键经 `IShellView.TranslateAccelerator` 交给原生 Shell；没有重复实现删除逻辑。已补充[快捷键表](keyboard-shortcuts.md)和[Shell 对照矩阵](shell-parity.md)。源码只证明按键转发，真实回收站结果、目标菜单和焦点仍需 GUI 验收。

## 2026-10-24：临时预览 temp.9 与原生探针构建补齐

- 当前源码打包为 `releases/preview-0.6.0-temp.9`，使用包内独立 profile；发布隔离 smoke、Release SettingsSmoke、App/HostProbe Release 构建通过。NativeProbe 首次构建发现共享 `NativeShellTree.cs` 新依赖未纳入其显式编译清单，补入 `ShellItemKind.cs` 与 `ShellTreeMiddleClick.cs` 后 NativeProbe Release 构建通过，均为 0 警告/错误。
- 包内 EXE/DLL SHA-256 与清单一致，DLL 产品版本为 `0.6.0-preview.temp.9`。未运行 App、ExplorerBrowser、HostProbe 或 NativeProbe；本包供源码预览，不代表可见 GUI 验收、Q-Dir `.qdr` 互操作或大目录/网络目录验收完成。说明见[第九版预览说明](releases/preview-0.6.0-temp.9.md)。

## 2026-10-24：文件视图中键点击文件夹在新标签打开

- `ExplorerBrowserHost` 在中键按下时异步用 UI Automation 按屏幕坐标命中文件列表项，并取 ListItem/DataItem 标签名；仅普通文件系统目录视图允许此路由。标签名必须是单一路径组件，再与当前目录组合，Shell 重新解析并确认 basename、父目录及普通文件夹属性；中键释放后还要通过视图 HWND、导航版本、输入版本和当前路径门控，过期点击不会开标签。新标签使用现有默认详细列表配置。
- SettingsSmoke 对路径组合和路径逃逸拒绝、Shell 解析后同名同父目录检查，以及导航/输入/路径/视图变化和搜索/关闭状态下丢弃过期请求均通过；新增请求门控断言先因接口缺失失败，接入后通过。App Release 构建 0 警告/错误，完整 SettingsSmoke 与 `git diff --check` 通过。用户要求本轮不操作桌面 GUI，故 UI Automation 对实际 Shell 列表行的命中、文件视图中键行为和可见焦点待后续验收。

## 2026-10-24：HostProbe 断言失败的安全退出

- HostProbe 入口增加统一异常边界：同步探针异常写入 stderr、以退出码 1 结束；现有 fixture/宿主 `finally` 清理仍会先运行，避免断言失败升级为 Windows 崩溃提示。
- `--probe-error-smoke` 在隐藏 WPF 源初始化后、Shell 宿主创建前注入异常；Release 构建通过，探针以预期退出码 1 结束且未遗留进程。该 smoke 未创建 ExplorerBrowser，也未触碰前台输入。

## 2026-10-24：当前文件视图导出范围回归

- 新增隐藏生产宿主回归：夹具视图含 `initial.txt` 和 `unselected.txt`，只选中前者后调用生产快照捕获；结果为两行，选中项与未选中项均包含，且选区未改变。
- Release HostProbe 构建通过；测试退出码 0、前台未变、隐藏父窗口未显示。该证据覆盖导出范围与 Shell 快照捕获，不覆盖可见“文件→导出”菜单、保存对话框或 Excel 实际打开。

## 2026-10-24：刷新后 PATH 传递到命令行子进程

- SettingsSmoke 将唯一临时 PATH 项和测试变量通过生产环境计划应用到当前测试进程，启动隐藏 `cmd.exe` 验证两者均从子进程环境读到；测试退出后恢复原 PATH 与变量，不修改注册表。
- Release SettingsSmoke 全量通过。该检查覆盖进程环境更新到新命令进程的继承，不替代真实 `WM_SETTINGCHANGE` 通知或发布版菜单操作验收。

## 2026-10-24：Ctrl+Z 快捷键映射回归

- SettingsSmoke 新增映射断言：Ctrl+Z 解析为 Shell Undo，普通 Z 与 Ctrl+Shift+Z 不被该命令截获；完整 Release SettingsSmoke 通过。
- 该断言只覆盖快捷键解析器，不验证真实焦点下的 HwndHost/原生 Shell 键盘链；可见 Ctrl+Z 仍待隔离桌面验收。

## 2026-10-24：Ctrl+数字小键盘加号保留给 Shell

- 快捷键表核对微软当前 File Explorer 清单后，将 `Ctrl+数字小键盘加号` 作为详细视图自动调整列宽的原生 Shell 操作；SettingsSmoke 增加断言，确认应用快捷键解析器不会截获该按键。
- Release SettingsSmoke 全量通过。该断言只验证应用路由未占用按键；真实详细视图的列宽行为仍待隔离桌面验收。

## 2026-10-24：原生 Shell 撤销新建文件夹的隔离验证与键盘路由限制

- 临时 Release HostProbe 通过背景 `IContextMenu` 的 `Undo` 撤销了生产 Shell 命令新建的夹具目录，磁盘和 Shell 视图恢复为只剩原始 `initial.txt`。后续重复运行中，有一次撤销完成后触发“前台未变或父窗口仍隐藏”的断言；探针没有分别记录这两个条件，故不能证明运行始终隔离，现已移除该探针入口。
- Ctrl+Z 隐藏路由的一次实验能撤销夹具目录，但复跑也触发相同隔离断言，且异常处理弹出错误窗口，因此不作为通过证据，已移除该探针入口。Ctrl+Z 键盘链需在隔离可见窗口中验证。删除到回收站、确认对话框与人工恢复同样未覆盖，关键文件操作 P0 继续开放。

## 2026-10-24：Undo Shell 动作的隔离性复核

- 用独立临时 HostProbe 再次执行 Shell 新建文件夹后调用 canonical `Undo`。两次观察都确认磁盘文件夹被撤销；一次视图未在 8 秒门槛内收敛，另一次视图回到 `initial.txt`，且其他窗格内容不变。后一次没有启动产品目录 watcher（创建/删除事件 0、fallback 0），说明该样本的视图变化来自 Shell 自身更新，不能证明产品 watcher 路径。
- Shell 操作期间前台短暂变为隐藏的 `OperationStatusWindow`；探针父窗口保持隐藏，进程退出后只读检查确认前台已自然回到 Codex。为避免复跑再次切换前台，已移除该临时探针入口。此项只证明 Shell 背景 Undo 在该次夹具操作成功，不证明应用菜单/物理 Ctrl+Z 键盘链或用户可见焦点行为。

## 2026-10-24：关闭消息与 Shell 回调 trace 的 50,000 项控制复测

- Release 隐藏生产宿主关闭 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE` 与 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE` 后运行 50,000 项创建/删除，退出码 0。产品 watcher 创建/删除均收到 `50,000/50,000`；Shell/磁盘最终均为 3 项，原两项选择和未选中焦点恢复。
- 创建/删除耗时 `41.1/41.6 s`，创建 settle 约 `5.6 s`；创建 fallback 1 次、删除 fallback 2 次，均在对应文件操作已结束后。创建时 Shell 显示 `49,172/50,003` 后由 fallback 恢复；创建/删除刷新期间出现短暂空选，最后恢复。
- trace 关闭时 Dispatcher `ContextIdle/Input/Normal` 最大间隔 `786/577/595 ms`，20 ms 心跳最大间隔 `557 ms`；专用线程心跳最大间隔 `28 ms`，STA 连续忙采样最长 `716 ms`。与上一轮开启诊断 trace 的样本均有数百毫秒间隔，但单样本对照不能归因探针或某个 Shell 操作。此探针仍做周期心跳、视图选择/焦点读取，不是零插桩基准；WPR 原生调用栈采集受本机系统性能策略限制，根因未完全定位，不调整通知/刷新策略。自动刷新 P0 继续开放。

## 2026-10-24：Shell 选择版本变化后的精确集合复核

- 50k 时间线显示 Shell 可在没有用户输入时递增 `SelectionVersion`，且视图有短暂清选后自行恢复的情况。延迟恢复现在先枚举当前已选 PIDL，并与保存快照逐项按 Shell 身份比较；集合完全一致时只恢复焦点，无法读取或发现差异时保留整批选区恢复。
- 新 HostProbe 先在旧行为下复现了无意义的 `SelectAndPositionItems` 调用，再通过；合成 Shell 选择版本通知但保持两项选区不变时跳过批量 API，实际改变 Shell 选择后识别不匹配并重放整批选区。两种情况都保留两项选择及独立焦点；用户输入取消探针、四窗格显式刷新探针及 SettingsSmoke 通过，App/HostProbe Release 构建 0 警告/错误。
- 新逻辑后的 Release 50k 隐藏压力退出码 0，产品 watcher 创建/删除均 `50,000/50,000`，最终 Shell/磁盘为 3 项。创建 fallback 0、删除 fallback 1（在文件操作已完成后）；删除刷新期间选择短暂清空，精确比较走不匹配分支并恢复 `second.txt`/`third.txt` 与独立 `initial.txt` 焦点。ContextIdle/Input/Normal 最大间隔 `658/581/581 ms`，专用线程心跳最大间隔 `37 ms`。这不是性能改善的对照证据；自动刷新 P0、GUI/网络盘验收继续开放。`.qdr` 当前视图导出范围已按用户确认记为整份视图，选中项不缩小范围。

## 2026-10-24：50,000 项 Shell 选择事件时间线与低扰动复核

- Shell 回调 trace 新增最多 128 条 `OnStateChange` 类型/时间记录，状态计数可在逐回调计时关闭时保留。Release 隐藏 50k 压测的 selection version 起点为 7，之后记录 67 个 change=2（`CDBOSC_SELCHANGE`），无 change=3/4；事件时间从 `1.624 s` 分布到 `65.756 s`，横跨创建和删除阶段，期间没有用户输入。
- 该轮退出码 0；消息泵最大间隔 `168.6 ms`，ContextIdle/Input/Normal 最大间隔 `263/183/176 ms`。目录阶段 trace 中显式 `Refresh()` 调用数为 0，说明本轮这些版本变化发生在 Shell 自然更新过程，不是产品 fallback 重绘导致。
- 这说明异步 Shell 更新本身会发出选区变化回调，不能将每次 `SelectionVersion` 变化简单解释为用户新选区或产品恢复。回调时间线尚未与逐条 Shell 项目数绑定，不能说明具体哪些是清选、默认焦点变化或恢复选项；继续保持 selection-version 门控并做逐段关联，不因该轮关闭 P0。详见[调查记录](live-update-investigation.md#2026-10-24-50000-项-shell-选择事件时间线与低扰动复核)。

## 2026-10-24：50,000 项 Shell 选择回调来源复核

- 将 `ShellBrowserSite` 的状态变更计数与逐回调计时解耦：`EXPLORER_DIRS_SHELL_CALLBACK_TRACE=1` 可记录 CDBOSC 类型计数，`EXPLORER_DIRS_SHELL_CALLBACK_TIMING=0` 保持低扰动。Release 隐藏 HostProbe 50k 轮开始/结束计数从 `state=2:7` 到 `state=2:82`，增量 75 个均为 `CDBOSC_SELCHANGE`，没有 3/4 类回调；本轮无压测期间的用户输入。
- 该轮退出码 0，产品 watcher 两阶段完整，最终视图/磁盘回到 3 项，选择和焦点恢复；fallback 为创建 0、删除 1。消息泵最大间隔 `237 ms`，ContextIdle/Input/Normal 最大间隔 `550/546/546 ms`。
- 这确认 Shell 的选区变化通知会在无用户输入的隐藏压力操作中递增版本，但总计数仍混合了异步刷新与产品自身恢复引发的通知，尚未区分两者先后关系。开启逐回调计时的另一轮出现更大的消息间隔，不能据此归因性能变化。下一步关联低扰动状态事件时间与恢复时间线；自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-50000-项-shell-选择回调来源复核)。

## 2026-10-24：延迟焦点恢复期间避免重复选区批次

- 刷新恢复焦点暂未就绪时，原重试每次都会再次执行整批选区恢复。现记录已成功恢复的选区版本；版本稳定时重试只恢复焦点，Shell 选择版本变化后仍恢复整批选区。
- `ShellSelectionRestoreGateSmoke` 新策略断言在旧 API 下按预期编译失败，接入后 SettingsSmoke 全套通过；HostProbe Release 构建 0 警告/错误。隐藏稳定选区/焦点用例确认延迟步骤跳过选区批次 API，并保留两项选择和独立焦点；原用户输入取消探针仍通过。10k 压力轮无 fallback，选择/焦点保持；50k 压力轮退出码 0，最终状态正确。
- 50k 样本在两次刷新间观察到 Shell 选择版本变化，因此仍执行一次 deferred 整批恢复，不能声称该轮已减少调用或改善时延。下一步查明该版本变化来自 Shell 异步重建还是新输入，同时保留“不覆盖用户新选择”的门控；自动刷新 P0 与可见/网络目录验收继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-延迟焦点恢复期间避免重复选区批次)。

## 2026-10-24：50,000 项消息泵 trace 的负载长尾定位

- Release 隐藏生产宿主 50k 创建/删除 trace 轮退出码 0，产品 watcher 两阶段各 `50,000/50,000`，观察器 `49,999/50,000` 且无错误；时间线最终回到 3 项，原两项选择与焦点恢复。创建与删除各有一次 fallback，均发生在操作已完成后。
- 同轮消息泵处理 8,313 条消息，最大 STA 取消息间隔 `407 ms`；ContextIdle/Input/Normal 最大间隔 `874/724/724 ms`，队列延迟最高 `825 ms`，专用线程心跳最大间隔 `26 ms`。Shell `0x4B0/SHELLDLL_DefView` 单次处理最高 `407 ms`；约 `260 ms` 的 HostProbe WndProc 与生产选择恢复 callback 重叠。
- 同轮 Shell 选择恢复 trace 中，选区批次调用、focused PIDL 存在性扫描、Shell 焦点设置/验证之间分别间隔约 `54/78/73 ms`，与约 `260 ms` 的 deferred-restore callback 量级相符。源码显示每次重试会再次执行完整选区恢复；这提示一个可验证的优化点，但尚未证明它是所有消息间隔的唯一原因。
- 该样本未复现秒级空窗，但确认 50k 负载下仍可出现数百毫秒的 Shell/Dispatcher 长尾。trace 只显示时间重叠，不能单独判定因果；自动刷新 P0 与 GUI/网络盘验收继续开放。详细观察见[调查记录](live-update-investigation.md#2026-10-24-50000-项消息泵-trace-负载长尾定位)。

## 2026-10-24：10,000 项自动刷新消息泵 trace 复测

- Release 隐藏生产宿主 10k 创建/删除压力退出码 0，Watcher 产品事件两阶段均为 `10,000/10,000`，最终视图/磁盘为 3 项，选择和独立焦点恢复。创建/删除耗时 `3.395/2.437 s`；两阶段各有一次计数 fallback，分别在操作完成后处理部分或多出一项的 Shell 计数。
- 仅启用消息泵 trace，未开独立高频队列采样：STA 取消息最大间隔 `77.9 ms`，ContextIdle/Input/Normal 最大间隔 `263/111/111 ms`，专用线程心跳 `22 ms`；单条慢 WndProc 最大 `85 ms`，计时器队列延迟最大 `216 ms`。本轮未复现秒级 STA 空窗。
- 刷新期间的采样短暂读到空选区及焦点，随刷新恢复回原两项选择与独立焦点；这不能作为刷新过程完全无闪失的证据。单次 trace 样本也不能解释此前秒级长尾，自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-10000-项自动刷新消息泵-trace-复测)。

## 2026-10-24：第二轮 50,000 项自动刷新门控复测

- 同一 Release 隐藏生产宿主探针再次执行 50,000 项创建/删除，退出码 0，墙钟 `61.9 s`；Watcher 两阶段完整（创建 `50,000/50,000`、删除 `50,000/50,000`），磁盘及 Shell 最终均为 3 项，原有两项选择与独立焦点保留。
- 创建/删除耗时 `21.290/14.447 s`。本轮创建与删除均无计数 fallback；Watcher 累计 `49,998/50,000` 个观察器事件及 `50,000/50,000` 个产品事件，无观察器错误。专用 Dispatcher 心跳最大间隔 `176 ms`；本轮没有启用消息泵/队列高频探针，因此不据此推断 STA 的最大空档。
- 与上一轮同为无负基线、无大批量 fallback；重复样本支持活动批次门控稳定，但耗时波动明显，仍没有解释过往观测到的秒级 STA 长尾。自动刷新 P0 继续开放，GUI 与慢速/网络盘验收未完成。完整探针摘要见[调查记录](live-update-investigation.md#2026-10-24-第二轮-50000-项自动刷新门控复测)。

## 2026-10-24：大批量 watcher 持续活动时延后刷新

- 50k 隐藏宿主的 batch/queue 快照显示，文件事件仍持续到达时，异步 Shell 刷新会暂时返回部分枚举计数。旧逻辑两次不匹配后把该部分计数重设为基线，随后在同一批次剩余 watcher 事件到达时产生负预期计数和多次 fallback。
- 现记录最后一条 watcher 事件时间；计数差异对应至少 32 项且 750 ms 静默期尚未满足时，延后 fallback 并重置原有有限等待计数。单项差和强制刷新（重命名/ watcher 溢出）不受影响。状态 smoke 新增“活动大批次延后、安静后恢复有界等待、单项和强制刷新立即进行”；新增断言在旧 API 下按预期编译失败，接入后通过。App/HostProbe Release 构建 0 警告/错误，完整 SettingsSmoke 通过。
- Release 隐藏 10k 复验退出码 0：watcher 创建/删除 `10,000/10,000`，最终视图/磁盘正确，选择和焦点恢复，创建后 1 次 fallback、完整删除阶段累计 2 次。50k 复验退出码 0：墙钟 `57.4 s`，创建/删除 `17.8/14.9 s`，watcher `50,000/50,000`，最终 3 项、选择和焦点恢复，创建后 1 次 fallback、全轮累计 2 次；fallback 时批次基线仍为 `50,003/50,002`，未见负基线。单文件创建探针于 `126 ms` 可见、fallback=0。
- 对照旧 50k 样本曾在操作持续期间重复 fallback 并出现负预期基线；改后样本支持活动期等待门控有效，但机器负载、Shell 枚举速度仍会波动，单个改后 50k 轮不足以证明长尾已普遍消失。自动刷新 P0 与 GUI/网络盘验收继续开放。完整状态快照和因果边界见[调查记录](live-update-investigation.md#2026-10-24-大批量-watcher-活动期间的部分-shell-基线与延后兜底)。

## 2026-10-24：Shell 回调计时开关隔离与 10,000 项交错复测

- HostProbe 原先用 `EXPLORER_DIRS_SHELL_CALLBACK_TRACE` 同时启用 Shell/Dispatcher 阶段诊断和 `ShellBrowserSite` 每次回调计时，无法单独关闭回调计时。现增加 `EXPLORER_DIRS_SHELL_CALLBACK_TIMING`；HostProbe 默认仍按原行为开启，显式设为 `0` 可关闭逐回调计时而保留其他诊断。
- Release 隐藏生产宿主交错运行计时开/关/开/关各两轮 `10,000` 项创建/删除，四轮退出码 0；watcher 两阶段均完整，最终视图/磁盘为 3 项，选择及独立焦点恢复。开启组创建 `4.34/4.76 s`、删除 `2.81/3.09 s`；关闭组创建 `3.84/3.96 s`、删除 `3.14/3.74 s`。ContextIdle 最大间隔开启组 `270/335 ms`、关闭组 `236/295 ms`，均未复现秒级空窗。
- 开启逐回调计时的 `OnStateChange` 计时总量为 `55.9/82.8 ms`；两组各仅两轮且样本波动明显，不能归因插桩造成性能差异。该对照仍有产品阶段计时、Dispatcher 定时采样及专用线程心跳，不是无观察器基准；自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-shell-回调计时开关隔离与-10000-项交错复测)。

## 2026-10-24：文件夹删除提前与合并 RMDIR 对照

- Release 隐藏宿主交替复跑 6 次：3 次不手动提前通知，3 次在 watcher 观测到删除且 stale Shell 项确认是文件夹后立即手动发送 `SHCNE_RMDIR`。`NONE` 组仍走产品既有 250 ms 合并通知，并非无 RMDIR 控制组。
- `NONE` 组 0/3 在 250 ms 内更新，产品随后各发 1 次 RMDIR、fallback=0；提前 RMDIR 组 2/3 于 140/153 ms 更新且 fallback=0，另 1/3 未在 250 ms 内更新，之后产品批次再通知并触发一次 fallback。六轮最终视图均为 3 项。
- 该样本提示提前信号可能缩短个别文件夹删除的可见时间，但轮数少，且手动提前路径与产品当前路径不同；现有 250 ms coalescing 规则保留。仅隐藏本机宿主，不代表 GUI 或大批量删除；见[调查记录](live-update-investigation.md#2026-10-24文件夹删除提前与合并-rmdir-对照)。

## 2026-10-24：生产 Shell 文件操作与自动更新复验

- Release 隐藏 HostProbe `--live-shell-product-monitor` 退出码 0：同窗格创建、强制重命名、删除，跨窗格复制/删除/移动及文件夹创建/删除均在对应 Shell 视图可见；其他窗格未被误改，选区和独立焦点恢复。
- 整轮触发 9 次计数 fallback，其中 mismatch 2 次、刷新中新增变更 1 次；文件夹删除依然使用 1 次 fallback，未发送原生 RMDIR。最终四窗格都回到 3 项，目标与源目录内容正确。
- 这是本机隐藏生产 Shell 宿主的文件操作证据，不含可见窗口/物理输入，也不是低扰动性能样本。功能 P0 的可复现文件操作路径通过，刷新次数与可见焦点体验仍待优化和桌面验收。

## 2026-10-24：校正消息泵探针后的第二轮 10,000 项复测

- Release 隐藏生产宿主以 `EXPLORER_DIRS_MESSAGE_PUMP_TRACE=1`、关闭独立队列探针运行 10,000 项创建/删除压力，退出码 0；产品 watcher 两阶段均为 `10,000/10,000`，最终 Shell/磁盘均为 3 项，两项选择与未选中焦点恢复。
- 创建/删除耗时 `5.420/3.140 s`，各触发一次计数 fallback；同步 `Refresh()` 分别为 `73.7/63.6 ms`。STA 取消息最大间隔 `71 ms`、ContextIdle 最大间隔 `219 ms`，Dispatcher 线程专用心跳最大间隔 `21 ms`。本轮没有复现秒级空档。
- 这是单次本机隐藏宿主样本，不能证明此前秒级长尾已消失，也不代表可见 UI。fallback 时 Shell 数量短暂落后或多出一项，状态随后自行收敛；未据此更改刷新策略。自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24校正消息泵探针后的第二轮-10000-项复测)。

## 2026-10-24：DispatcherTimer 排队延迟探针校正与 10,000 项复测

- 消息泵 Hook 曾把 DispatcherTimer 的 `OperationPosted` 时间误当作计时器到期时间，因而将 150 ms 的预设间隔也算进排队延迟。WPF 先创建 Inactive operation，再设置 due time 并加入计时器，到期后提升 operation 优先级；smoke 在旧测量下复现 150 ms timer + 400 ms STA 阻塞被报告为 400.8 ms。
- 探针现对 DispatcherTimer 使用其 due time 计算到期后的等待，同时保留投递到启动的原始跨度；普通 Dispatcher 操作仍用 Posted→Started 测量。新增烟测检查无 backlog 的 timer 不报告设定间隔，并检查实际阻塞 timer 的校正延迟与原始跨度减 interval 一致。修正前烟测失败，修正后 `message-pump-hook-smoke` 通过。
- trace-only Release 隐藏 10k 压力轮退出码 0：Watcher 创建/删除均为 10,000/10,000，Shell 和磁盘最终 3/3，选区与焦点保持；创建/删除耗时 6.943/7.793 秒。校正后记录到的慢队列样本（≥100 ms）最高 215 ms（ContextIdle）/167 ms（Background），线程取消息间隔最大 117.5 ms，目录 `Refresh()` 最大 122.3 ms；本轮未复现秒级长尾。
- 这是单次本机隐藏宿主 trace-only 样本，之前开启独立 250 ms 队列采样器的轮次达到 2.397 秒且创建选区失败；不同轮不证明采样器导致差异，均不代表可见 UI。自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-dispatchertimer-排队延迟探针校正与-10000-项复测)。

## 2026-10-24：删除事件 Dispatcher 投递合并与 10,000 项复测

- 延迟超过 5.7 秒的 ContextIdle 队列操作已归因到 `QueueDeletedItemNotification`：原实现为每个 FileSystemWatcher 删除事件分别 `BeginInvoke`。现在 watcher 线程先以线程安全批次记录最新路径，只在批次首次进入待处理状态时投递一次；STA 仍等待完整 250 ms 静默期，再对最新路径做 Shell 项身份/文件夹属性确认，符合条件才发送 `SHCNE_RMDIR`，计数 fallback 保留。
- ShellDeleteNotificationBatch smoke 先按新 API 失败，再通过；覆盖单次 handoff、 burst 合并、静默期重置、最新路径和后续 burst。App、HostProbe Release 构建无警告/错误；完整 SettingsSmoke 通过。
- 无探针 Release 隐藏 10k 压力轮退出码 0：watcher 创建/删除各 10,000/10,000，Shell 视图和磁盘最终 3/3，选区与焦点保持；创建/删除耗时 6.888/9.379 秒，删除 fallback 2 次。详细 trace 未见逐删除 watcher 的 Dispatcher 回调；删除身份扫描 2 次均为 `NotFolder`，不会错发 RMDIR。
- 高扰动队列采样轮中，独立队列探针 ContextIdle 最大排队 2.397 秒，但同时创建阶段选区未恢复，故该轮以 exit 1 结束，不作为完整功能通过；无探针对照完整通过。该修复消除逐删除事件的队列放大，不代表解决其他 Shell/WPF 负载导致的全部 STA 长尾；自动刷新 P0 继续开放。无可见 GUI 验收或发布。详见[调查记录](live-update-investigation.md#2026-10-24-删除事件-dispatcher-投递合并与-10000-项复测)。

## 2026-10-24：创建事件通知派发合并与 10,000 项复测

- 文件创建通知批次现在从第一次 Dispatcher 派发起保持待处理，直到 Shell 通知消费；每次新增事件更新时间戳，60 ms 计时器只在安静期满足后取走最新路径。这样不会为同一未消费批次重复排 Dispatcher 回调，同时保留“最后一次文件事件后等待 60 ms”的原有防抖语义。
- SettingsSmoke 回归在旧状态机上按预期失败，修正时序后验证活动窗口内不再排重复工作、新事件会重置安静期、静默后使用最新路径。中间一次未保留的实现曾在 10k 压力轮发出 115 次 `SHCNE_CREATE` 并触发 4 次 fallback，现已改为带安静期检查的实现。
- 最终 Release 隐藏 HostProbe 10k 轮退出码 0：watcher 创建/删除均为 10,000/10,000，Shell 与磁盘计数最终为 3，选区和焦点保持；本轮无 `SHCNE_CREATE`、无 fallback。Dispatcher `ContextIdle` 最大间隔仍达 9.645 秒，`Normal/Input` 为 4.171/4.437 秒，说明该修正没有解决大目录 STA 长尾；P0 继续开放。
- App/HostProbe Release 构建 0 警告/错误，完整 SettingsSmoke 通过。结果仅限本机隐藏宿主；未做 GUI 验收或发布。详见[调查记录](live-update-investigation.md#2026-10-24-创建事件通知派发合并与-10000-项复测)。

## 2026-10-24：10,000 项自动刷新与 STA 长尾复测

- HostProbe/App Release 构建 0 警告/错误；完整 SettingsSmoke 通过。隐藏生产宿主 10,000 项创建/删除后，watcher 均收到 10,000/10,000 事件，最终视图与磁盘均回到 3 项，原两项选择和独立焦点保持。
- 创建/删除耗时 7.9/14.1 秒；删除阶段发生一次计数 fallback（预期 10,002、实际 10,003），`Refresh()` 同步调用约 754 ms。Dispatcher `Normal/Input/ContextIdle` 最大间隔为 1,472/1,524/3,396 ms，独立线程最大心跳间隔 43 ms；记录到 `DirectUIHWND/0x4C8` 单次 Shell 处理最高 869 ms。
- 本轮确认功能最终收敛，但仍有明显 STA 长尾；单条已观测消息不足以解释最大的 `ContextIdle` 间隔，单样本也不能确定因果或代表可见响应。自动刷新 P0 保持开放，不据此改变产品刷新策略。详见[调查记录](live-update-investigation.md#2026-10-24-10000-项自动刷新与-sta-长尾复测)。

## 2026-10-24：原生 Shell 菜单能力隔离探针

- NativeProbe 原先链接生产 `ShellFileViewExportCapture.cs` 时遗漏打印快照定义，导致探针不能构建。现补齐 `FileViewPrintLayout.cs`、`FileViewPrintDocument.cs`，并在探针项目显式指定 WPF `Image`/`HorizontalAlignment` 别名以解决 WinForms/WPF 隐式 using 冲突。
- Release NativeProbe 构建 0 警告/错误；隐藏菜单能力探针 4.34 秒完成并以 0 退出，覆盖空选区、单选项以及背景 New 子菜单的原生启用状态；父窗口不可见、前台窗口未变化，未调用任何菜单命令。
- 这只证明当前机器上查询原生 Shell 菜单状态的路径可完成，不识别具体第三方扩展，也不排除其他扩展操作会阻塞或崩溃 App；扩展责任和 GUI 菜单体验仍待独立证据。

## 2026-10-24：当前文件视图打印

- 文件菜单和工具栏已加入“打印当前文件视图”。命令在打开打印机对话框前捕获活动标签的 Shell 行序与属性，选区不改变打印范围；关闭对话框不会提交打印任务。
- 打印内容按当前 Shell 查看方式分为缩略图网格、小图标/列表、平铺卡片和详细信息表格。图标类通过 `IShellItemImageFactory` 读取 Shell 缩略图/图标；平铺、详细信息和内容视图保留可见属性及当前行。FlowDocument 使用打印机可用纸张尺寸分页。
- SettingsSmoke 覆盖八种模式到打印布局的映射、详细表格表头/行数、列表行数以及不完整行拒绝；Release 隐藏 HostProbe 5/5 轮通过真实 Shell 图片工厂捕获，逐轮验证筛选视图行序、每项图像、缩略图网格和详细表格行数。没有打开打印机对话框或产生打印任务；不同打印机分页、缩略图目录性能、Q-Dir 可见对照及虚拟 Shell 位置仍待隔离 GUI/打印预览验收。不生成 release。
- 对标依据：[Q-Dir FAQ 131](https://www.softwareok.com/?faq=131&seite=faq-Q-DIR)说明打印当前文件夹视图，并随大图标/缩略图、详细列表、普通列表等查看方式改变纸面输出。

## 2026-10-24：快速筛选分组恢复等待 Shell 枚举

- 隔离 HostProbe 在分组视图应用筛选时捕获到间歇空白：分组清除与过滤器挂接成功，但 `IShellView.Refresh()` 的 Dispatcher 回调先于 Shell 行枚举完成；此刻 `ShouldShow` 回调为 0、项目数为 0，过早恢复分组会让文件列表停留在空白。重复运行确认这是产品回调时序，不是打印捕获造成。
- 现在分组恢复等待本次过滤器至少收到一次项目回调；旧项目数大于 0 时，短暂残留的旧行不能作为“新筛选完成”的信号。快速筛选请求版本号会使过期 Dispatcher 回调失效，避免较早请求在新筛选或导航后恢复旧分组。空目录可立即恢复；如果 5 秒内没有枚举回调，则清除筛选、重新刷新未筛选视图，再恢复分组，避免留下空白列表。超时后也同步清空筛选栏（除非用户仍在编辑文本）。
- SettingsSmoke 对等待、过滤结果为空、旧行残留、空目录及超时回退先 RED 后 GREEN。Release HostProbe 分组快速筛选 5/5 轮通过，每轮筛选行、导出顺序、Type 分组和选中/焦点项一致；打印捕获同轮确认 2/2 个 Shell 图像和两类文档行数。探针异常路径现会关闭其隐藏 Dispatcher，避免失败后残留进程锁住 App DLL。
- 以上仅为本机隐藏 Shell 宿主；没有测试可见筛选框、打印对话框或真实键鼠，也没有生成 release。筛选刷新成本、网络/虚拟 Shell 及桌面验收仍未关闭。

## 2026-10-24：文件操作后的有界焦点重试

- `QueueFocusRestore` 原先只在命令返回后的 ContextIdle 与 250 ms 定时器各尝试一次；Paste 的异步完成没有与焦点恢复同步。现保留立即尝试，并在首个 250 ms 检查后每 100 ms 观察一次，最多持续 1.5 秒；仅在原浏览器/路径/导航代际不变、窗格仍活动且文件区焦点仍被期望、用户输入代数未变化时尝试恢复。
- 若 Shell 视图仍有焦点则只继续监视、不重复调用 SetFocus；焦点已移入 WPF 控件、用户输入、切换视图/路径或窗口失活会取消。SettingsSmoke 新门控用例先因接口缺失失败，接入后通过；App/HostProbe Release 构建 0 警告/错误，完整 SettingsSmoke 通过。
- 没有设置/读取系统剪贴板或显示窗口，因此没有验证真实 Ctrl+C/V 与系统 Paste 的焦点时序。异步 Paste 是否晚于原恢复窗口仍属待证假设；P0 保持开放，需后续在独占隔离桌面记录实际焦点时间线。

## 2026-10-24：Q-Dir 快速文件夹菜单快捷键

- 官方 Q-Dir 英文功能/快捷键页列出 `Ctrl+Q` 打开 Quick-folder menu；本项目已有动态收藏夹菜单，现将该快捷键路由到同一菜单，并在菜单栏显示快捷键提示，没有重复实现收藏列表。
- SettingsSmoke 先在新增映射断言处因 `QuickFolderMenu` 尚不存在而编译失败；接入映射后，App Release 构建（0 警告/错误）和完整 SettingsSmoke 通过。
- 此次未启动窗口或模拟键盘；实际子菜单打开状态、键盘焦点与取消返回文件视图仍待 GUI 验收。[Q-Dir 功能与快捷键](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)。

## 2026-10-24：Ctrl+Shift+T 兼容 Q-Dir 新标签操作

- Q-Dir 将 `Ctrl+Shift+T` 与 `Shift+Enter` 都用于“在新标签打开所选文件夹”；本项目已将 Ctrl+Shift+T 用于恢复关闭标签。现仅当文件视图聚焦、没有 WPF 文本输入焦点且恰好可打开一个文件系统文件夹时执行 Q-Dir 行为；其他上下文保留恢复关闭标签，显式菜单点击始终只恢复。
- SettingsSmoke 新增分派回归，先因实现文件缺失而按预期失败；加入共享动作分派后，验证单个可打开文件夹优先、否则恢复关闭标签。实际 Shell 选区新标签路径已有隐藏生产宿主验证；本轮未显示窗口或模拟输入。
- App Release 构建及完整 SettingsSmoke 通过；真实 Ctrl+Shift+T 焦点上下文、关闭标签恢复和菜单提示仍待 GUI 验收。[Q-Dir 功能与快捷键](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)。

## 2026-10-24：多选快照保护与 50,000 项复测

- 复核一次 50,000 项压力失败后发现：没有新的用户输入时，Shell 若发出比已有选区更少的非空选择回调，`OnShellSelectionChanged` 原先会用该部分快照覆盖完整快照。现按同一视图和输入代数保留较完整快照；用户输入、导航变化或完整/更大选择仍可更新快照。回归 smoke 先失败、接入规则后通过。
- 新增定向隐藏 HostProbe：在真实产品 Shell 回调中建立三项选择快照，程序化缩为两项并手动触发同一个 Shell 选择回调，再清空选择；结果为 `full=3 partial=2 retained-snapshot=3 restored=True`，原三项均恢复。没有注入用户输入或触碰桌面。
- 50,000 项全量诊断复跑中真实捕获到刷新期间的 `selected=1` 回调；原来的两项选择、焦点最终都恢复，未复现快照被部分选择覆盖。watcher 两阶段为 50,000/50,000，最终视图/磁盘为 3/3。
- App/HostProbe Release 构建 0 警告/错误，完整 SettingsSmoke 和四窗格产品 Shell 操作探针通过。新一轮同源 QPC 时间线中，`0x4B0/SHELLDLL_DefView` 的 234 ms 慢调用与约 560–604 ms 的 Normal/Input/ContextIdle 空窗重叠；但最大约 2.02 秒 ContextIdle 空窗没有对应的单条 >50 ms 消息。`OnStateChange` 最大约 40 ms，`0x4C8/DirectUIHWND` 最大约 83 ms；高优先级最大间隔约 730/1,093 ms，仍有未解释部分。自动刷新/大目录 P0 仍开放。
- 详情见[调查记录](live-update-investigation.md#2026-10-24-多选快照保护与-50000-项复测)。证据限于本机隐藏 Shell 宿主和本地盘；不构成可见 UI 验收，也不生成 release。

## 2026-10-24：大目录压力下 Dispatcher STA 空窗定位

- Release 隐藏生产宿主 10,000 项阶段计时复测通过：Watcher 入队最大样本 0.95 ms、Dispatcher 批次消费 0.15 ms、计数检查 0.25 ms、`Refresh()` 同步调用 83 ms、完成回调 0.19 ms；Dispatcher 最大间隔 251 ms。
- 50,000 项产品组存在显著波动：一轮退出码 1，最终虽在约 215 秒回到 3 项，但 `ContextIdle` 最大空档约 149 秒；紧接的逐阶段计时复跑退出码 0，创建/删除 28.9/15.3 秒、计数/选区/焦点恢复，Dispatcher 最大间隔 595 ms。成功轮次通知调用最大 43.69 ms，其他被测 watcher/计数/刷新/回调阶段最大 75.7 ms。
- native-only 对照在 15 秒观察窗内没有显示 burst 文件，Dispatcher 最大间隔 387 ms；它按预期因视图未更新退出码 1。专用线程在异常长尾样本仍保持 87 ms 内。`DispatcherHooks` 已移除；保留失败样本，继续用全阶段计时调查 50k 长尾，不据当前一次严重异常归因或更改产品刷新策略。详见[调查记录](live-update-investigation.md#2026-10-24-watcherrefresh-阶段计时与-50000-项波动复测)。

## 2026-10-24：全视图导出范围确认与多窗格 F2 路由复测

- 用户确认导出整份当前视图，即使已有选中项也不缩小范围。Release HostProbe 的分组快速筛选用例在保留选中项时读回两行完整视图；SettingsSmoke 全套通过。
- 加强四窗格重命名探针：先经生产 `TranslateAcceleratorCore` 路由 F2，再由 Shell 建立原生编辑框；随后四个方向键都保留编辑焦点，左右键确实移动文本光标。四个窗格 Release 探针均通过。
- 测试运行于隐藏宿主，不模拟物理键盘或验证可见窗口的 WPF 焦点链；F2 多窗格的桌面验收仍开放。

## 2026-10-24：删除自然更新的时序波动复测

- Release native-only 隐藏宿主 2 个有效轮中，一轮等待 15 秒仍未更新计数，另一轮于 538 ms 自然收敛；两轮都保留原选择和焦点。另一次复制准备失败、未执行删除，不计入样本。
- 生产 watcher 3/3 轮于 338–362 ms 视图收敛、363–396 ms 批次结算，经一次计数 fallback 后选区和焦点保持。结果只限本机隐藏宿主，不证明网络盘或普遍延迟；自然 Shell 更新存在波动，保留产品 watcher/fallback 路径，自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-native-only-删除选区恢复稳定复测)。

## 2026-10-24：10,000 项产品 watcher 当前复测

- Release 隐藏生产宿主创建并删除 10,000 个文件，产品 watcher 两阶段均收到完整事件计数，最终四窗格和磁盘计数恢复到 3；两项选择及独立焦点均保持。创建/删除各触发一次非强制计数 fallback，删除后的 settle 约 5 秒；最大 Dispatcher 心跳间隔 125 ms。
- 结果只覆盖本机隐藏宿主与本地盘。刷新最终收敛，但 fallback 与多秒 settle 显示仍有响应成本；网络盘、可见 UI 和持续负载继续开放。详情见[调查记录](live-update-investigation.md#2026-10-24-10000-项-watcher-当前复测)。

## 2026-10-24：当前文件视图导出

- “文件 → 导出当前文件视图”现提供 CSV、制表符 TXT、HTML 表格和 BIFF8 `.xls`。按已确认的默认范围，导出活动窗格当前标签的整个 Shell 视图；选区不缩小导出范围。CSV/TXT 使用 UTF-8 BOM 和可逆分隔符/引号转义；HTML 对列名和单元格编码；`.xls` 将名称写为字符串单元格。
- 数据从活动原生 Shell 视图读取：可见列按 `IColumnManager` 顺序，列名来自 Windows 属性描述，行来自 `SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER`，值由 `IShellItem2` 快速属性存储格式化；不自行扫描目录。`.xls` 使用 MIT 许可的 OfficeIMO.Excel BIFF8 writer；依赖漏洞审计无已知漏洞。
- SettingsSmoke 检查四种格式的引号/转义、HTML 编码、公式样式文件名保持文本、`.xls` 写出后再读回及错误行拒绝。隐藏 NativeProbe 生产读取实现验证升/降序、分组和图标视图的顺序、中文列标题、文件大小显示与目录大小空值；父窗口隐藏且前台不变。App、HostProbe、NativeProbe Release 构建和完整 SettingsSmoke 通过。
- 2026-10-24 筛选/分组顺序探针发现：直接在已有分组视图安装 `IFolderFilterSite` 时，Shell 计数可为两项但行数组接口返回 `E_INVALIDARG`/`E_NOTFOUND`。生产快速筛选现会先暂时清除分组，完成筛选刷新后恢复原分组；隐藏 HostProbe 已验证导出行序、分组状态及选中/焦点项均保留。导出仍保留 Shell API 失败时的明确提示，避免错误数据。
- 尚未做可见菜单操作或 Q-Dir GUI 对照；上述状态变化的可见体验、虚拟 Shell 位置、超大目录性能、实际 Excel 打开体验及打印分页仍需验证。未生成 release。

## 2026-10-24：文件视图导出数据读取探针

- Release NativeProbe 在隔离隐藏 ExplorerBrowser 中使用 `SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER` 读取条目，并逐项对照 `IFolderView.Item(index)`：详细信息升序/降序、按类型分组和图标降序均与当前视图顺序一致。官方文档指出单独使用 `SVGIO_ALLVIEW` 不保证可见顺序，可能把选中项排在前面；`IColumnManager` 读回自定义可见列顺序，切换到图标视图后仍可读取。
- 逐个 `IShellItem2` 请求快速属性存储后，`IPropertyStore.GetValue(PKEY_Size)` 对夹具文件读回 3/5 字节，文件夹值为未知；没有自行扫描文件系统。父窗口保持隐藏、前台不变，唯一 GUID 夹具退出时清理。
- 生产 HostProbe `--quick-filter` 通过包含/排除、是否筛文件夹、清空、四窗格隔离及搜索视图往返恢复；新增生产路径用例在已分组且已选中文件时应用筛选，读回当前两行、原“类型”分组、导出顺序和选中/焦点项均通过。NativeProbe 另证明直接按“先分组再安装筛选”调用 Shell 接口会返回上述错误；生产路径通过清除分组、应用筛选并刷新、随后恢复分组来规避。
- 该结果仅来自本机隐藏 Shell 宿主；可见菜单操作、Q-Dir GUI 对照、虚拟 Shell 位置、日期/类型本地化显示、CSV/TXT/HTML/XLS 实际应用及打印分页仍需单独验证。全视图导出范围已由用户明确确认，隐藏 HostProbe 也覆盖了有选中项的分组筛选视图。

## 2026-10-24：逐标签原生预览窗格开关

- “视图”菜单与工具栏加入预览窗格切换，初始状态默认关闭；当前标签切换时同步选中状态。复制标签、关闭后恢复及 Q-Dir 扩展导入/导出保留该状态，新建标签默认关闭。
- 会话 INI 升至 v7，v1～v6 升级前保存 `.before-v7-*.bak`；Q-Dir 扩展 State 包含版本化会话。SettingsSmoke 覆盖往返、旧版默认关闭、错误布尔值拒绝及升级备份。
- HostProbe 通过生产 `SetPreviewPaneVisible` 在同一个 ExplorerBrowser 内反复开关，确认 Shell 选项、视图 HWND、路径、文件计数和选区不变；QDR 导入导出扩展往返、完整 SettingsSmoke、App 与 HostProbe Release 构建均通过。预览宿主在隐藏窗口中无法证明内容实际渲染；不生成 release，可见布局与 Preview Handler 验收待用户空闲桌面。

## 2026-10-24：右键目标坐标读取失败时清空旧命中

- 源码路径复核发现，Shell 视图收到 `WM_RBUTTONDOWN` 后若 `GetCursorPos` 失败，旧命中结果会保留最多 5 秒；之后位置相同的 `WM_CONTEXTMENU` 可能错误复用它。现在每次右键都先替换跟踪状态；坐标不可用时清空旧结果，让菜单消息按自己的坐标重新探测。
- 新增无坐标/旧命中回归，先因状态机接口不接受空坐标而失败，接入后通过。SettingsSmoke、App/HostProbe Release 构建通过（0 警告/错误）；这只覆盖失效状态边界，不证明 UI Automation 对可见空白区的实际命中准确率，需保留 GUI 验收。

## 2026-10-24：Alt+P 切换逐标签预览窗格

- 按 Explorer 常用快捷键清单接入 Alt+P，切换当前活动标签的原生预览帧；“视图”菜单和工具栏同时显示按键。快捷键回归先因映射缺失失败，接入后 Release SettingsSmoke 全套通过，App Release 构建为 0 警告/错误；可见 Preview Handler/焦点验收待完成。

## 2026-10-24：Explorer 详情窗格

- 使用 ExplorerBrowser 原生 `EP_DetailsPane`，通过“视图”菜单、工具栏和 Alt+Shift+P 切换。详情与预览帧互斥；切换保持同一 Shell 文件视图对象。新标签默认关闭，复制/恢复/导入/导出和每标签会话状态保留。
- 会话 INI 升为 v8，v1-v7 读入时详情默认关闭，首次写回备份 `.before-v8-*.bak`；`.qdr` 原生字段不表达该状态，由现有 Explorer Dirs 扩展往返携带。SettingsSmoke 覆盖 v7 预览状态迁移、v8 详情状态、两窗格冲突拒绝、升级备份、快捷键、QDR 扩展往返。
- App/HostProbe Release 构建 0 警告/错误，完整 SettingsSmoke 通过。Release 隐藏生产宿主验证 Preview→Details→Preview→关闭→Details→关闭，帧状态互斥，视图 HWND/路径/计数/选区保持，前台未变。详情元数据真实渲染、尺寸、焦点、多窗格布局及跨重启可见验收仍待空闲桌面；不生成 release。

## 2026-10-24：Ctrl+N 独立窗口会话

- 主会话继续使用 `ExplorerDirs.ini`，每个新增窗口使用独立 INI，并由 `WindowSessions.ini` 保存启动恢复顺序。窗口本地固定标签跟随对应会话；收藏夹、筛选收藏、UI 偏好、目录树偏好和布局比例保持 profile 级共享。
- 关闭时仍有其他窗口则只移除已关闭窗口；最后一个窗口退出时保留恢复清单。新建会话先写入独立文件，只有清单提交成功后才公开；清单冲突会拒绝覆盖并清理未发布的新会话文件。
- 若清单指向的次窗口 INI 已丢失，启动只恢复主会话、提示缺失并保护清单，拒绝以默认目录打开空白窗口或静默重写会话列表。
- SettingsSmoke 覆盖会话文件互不覆盖、顺序恢复、关闭主窗口后用全新清单实例读回次窗口、最后窗口保留、外部编辑冲突及缺失次窗口文件保护。发布脚本隔离 profile 复制同步覆盖已登记的窗口文件。可见 Ctrl+N、并行窗口和真实重启 GUI 验收仍待进行。
- 补齐 profile 单实例启动路由：无参数的第二次启动通过命名管道请求激活现有窗口；目录请求继续由现有窗口打开；无法确认路由时拒绝启动第二个写入者。隔离管道回归通过，真实桌面激活仍待 GUI 验收。

## 2026-10-24：文本输入焦点下保护新建文件夹快捷键

- 窗口级快捷键预处理原先仅让 Backspace 和选择型文件命令避开文本框；`Ctrl+Shift+N` 会继续调用活动窗格的 Shell 新建文件夹命令。现将新建文件夹标记为要求文件视图焦点，地址栏、搜索框等文本控件编辑时将按键留给控件。
- SettingsSmoke 新断言先因快捷键焦点分类遗漏而失败，修复后通过；本次不改变菜单和工具栏的新建入口。真实地址栏/搜索框键盘交互仍待 GUI 验收。

## 2026-10-24：全选原生快捷键八种查看方式矩阵

- 延伸 10,001 项详细信息视图全选证据，Release 隐藏生产宿主逐项切换八种查看方式并调用菜单实际使用的 Shell 加速键 helper。每种模式均选中相同的四项 fixture，查看方式与另外三窗格选区保持不变；总流程包含删除未选中目标后的选区身份保持断言。
- 本矩阵不设置排序/分组，且只使用四项 fixture；不替代分组、多选大目录、可见菜单/焦点与真实键盘 GUI 验收。

## 2026-10-24：新建文件夹刷新后的自动重命名续查

- 生产 `CreateNewFolder()` 离屏实验重现：Shell 命令接受并创建唯一文件夹，但当前焦点仍为 `initial.txt`。旧流程在触发一次恢复刷新后，每轮仍因焦点不匹配而直接返回，最终从未尝试 `IShellView.SelectItem(..., SVSI_EDIT)`。
- 源码已改为首次恢复刷新后等待一次；后续重试不再因焦点仍旧而永久短路，继续尝试选择唯一新目录并进入重命名。离屏试验显示显式聚焦文件视图后可以到达重命名编辑状态，因此正式入口现先调用 `FocusView()`，并在聚焦前后重新验证请求、导航及文件区输入代数，避免焦点调用或原生菜单准备期间已过期的命令继续执行。
- 新增门控回归：过期请求不聚焦、聚焦失败不调用 Shell 命令、聚焦期间导航或用户输入变化则取消、状态仍有效则继续。SettingsSmoke 先因 helper 缺失失败，接入后通过；App/HostProbe Release 构建及不激活前台的 Shell 命令状态/目录监视器探针通过。本次探针中文件夹可见、计数为 2、watcher 创建事件为 1、前台未变，但触发 1 次 fallback（此前轮次为 0），因此不据此宣称自然更新稳定。之前带实际 `FocusView()` 的离屏完整入口变体能到达重命名编辑，但该调用改变了系统前台，故该轮仍不计为通过，也不保留此探针。
- 2026-10-24 当前源码复测：`--file-command-states` 的 watcher 就绪路径成功创建且显示文件夹，创建 watcher 事件为 1、fallback=0、前台未变；`--file-command-states-early` 的 watcher 未就绪路径由短期观察器识别唯一目录并恢复刷新，选区/焦点保持、fallback=0、前台未变。两者只验证隐藏宿主命令状态与创建/刷新，不驱动正式 `CreateNewFolder()` 自动重命名入口。
- 源码复核发现，自动重命名轮询把 `SelectItem(..., SVSI_EDIT)` 的成功返回直接当作完成，没有确认原生 Edit 控件已取得焦点。现将“请求已被 Shell 接受”与“编辑焦点已到达”分开；接受后只等待焦点确认，不重复发送编辑请求。新 smoke 先因门控类型缺失而构建失败，接入后完整 Release SettingsSmoke 及 App Release 构建通过（0 警告/错误）。本轮未启动 Shell 宿主，正式入口中的可见重命名仍待 GUI 验收。
- 真实活动窗格中的焦点恢复、编辑框、物理用户输入中断仍待 GUI 验收；本修正尚未进入 temp.5。

## 2026-10-24：原生预览窗格能力验证（实现前调查）

- 当时预览/详情窗格仍是 Q-Dir 对标缺口。为避免在应用进程内直接执行第三方预览处理器，先采用 ExplorerBrowser 原生帧作为候选；当时仅隔离 HostProbe 的 `--preview-pane` 模式启用。后续已接入逐标签预览和详情开关，见本路线图“Explorer 详情窗格”与“逐标签原生预览窗格开关”；可见渲染仍待验收。
- Shell 成功查询 `IExplorerPaneVisibility`。无强制状态时导航树仍占位，预览区域为 0×0；以 `EPS_FORCE | EPS_INITIALSTATE` 隐藏其它标准帧后，文件视图区宽度为 221/500 px，Preview Extension Host 仍为 0×0。父窗口始终隐藏且前台不变，所以还不能证明处理器实际激活、渲染或四窗格空间可用。
- 同一 ExplorerBrowser 运行中关闭、再开启 `EBO_SHOWFRAMES` 后，`GetOptions` 状态符合请求；文件视图 HWND、当前路径、计数和 `initial.txt` 选区均保持。原生开关不要求销毁当前 Shell 浏览视图，但预览内容是否显示仍待可见验收。
- 当时仅完成隐藏宿主能力验证，产品接线在后续“逐标签原生预览窗格开关”条目记录。详见[预览窗格调查](preview-pane-investigation.md)。

2026-10-24 大选区全选性能定位：隔离 HostProbe 对生产 `ShellSelectionCommands.Apply(All)` 分段计时，1,000 项 PIDL 枚举 <1 ms、批量 `SelectAndPositionItems` 约 1,874 ms；此前 3,000 项总选择约 18.2 秒，10,001 项探针超过 90 秒未完成而中止。另将 1,000 项拆为 10 个 100 项 Shell 批次，选择调用仍需约 1,926 ms，没有获得提速。当前证据把主要成本定位到 Shell 批量选择调用，不是 PIDL 枚举；没有保留临时计时代码，也没有改产品实现。下一步核查“全选”菜单与 Ctrl+A 各自的生产命令路径，并对比 Shell 原生操作；在确认正确路径及多选/焦点语义前不替换实现。隐藏探针尚未验证物理键盘、可见窗口及其他系统版本。

2026-10-24 全选菜单改用 Shell 原生快捷键路径：按上项性能证据，将活动文件视图重新获得焦点后，菜单“全选”经 `IShellView.TranslateAccelerator` 发送 Ctrl+A；Ctrl 状态仅暂存在当前 UI 线程，并在同步 Shell 调用后恢复。若原生快捷键未处理或无法聚焦，保留既有 PIDL 选择回退。先添加 HostProbe 失败用例（缺少产品 helper 时按预期报 `MissingMethodException`），实现后隐藏生产宿主中 1,000 项耗时 3 ms、10,001 项约 13–17 ms；10,000 个剩余项目的选区在删除未选中目标后全部保持，fallback=0。App/HostProbe Release 构建和 SettingsSmoke 通过；可见菜单集成仍需验收，没有操作桌面或发布。

2026-10-24 跨窗格原生 Drop 接收路径隔离验证：进一步发现生产 `IShellView` 可直接查询 `IID_IDropTarget`；用 `SHCreateDataObject` 构造真实 Shell 文件数据对象并直接调用目标 `DragEnter/Drop`（不运行 `DoDragDrop`、不显示窗口、不注入桌面输入）。Release HostProbe 将 pane 0 的隔离文件复制到 pane 1；目标文件内容一致、源文件保留、其余窗格计数不变。初次探针没有等待生产目录监视器稳定，目标列表未自然更新，随后手动调用生产 `Refresh()` 才显示复制项；后续联测已纠正该基线，见下条。该结果验证 Shell drop target 接收及文件复制，不验证真实拖动手势、修饰键/移动语义或 watcher 自动更新。

2026-10-24 跨窗格 Drop 与生产监视器联测：修正探针基线后，先等待四个生产 ExplorerBrowser 目录监视器各自稳定（源窗格 2 项、其他窗格 1 项），再用同一 Shell `IDropTarget` 路径将 `pane-0/drop-source.txt` 复制到 `pane-1`。Release HostProbe 显示源保留、目标视图自动出现副本、目标 watcher fallback refresh 次数为 0，其他窗格计数不变。本测试仍是直接 COM Drop 调用，不覆盖 `DoDragDrop` 鼠标循环、拖动启动/命中测试、移动/右键拖动或用户可见行为。

2026-10-24 跨窗格多选 Drop 与焦点边界补测：同一 Shell `IDataObject` 含两个 pane 0 隔离文件，Release HostProbe 一次 Drop 后两份目标副本内容均匹配、源文件均保留，pane 1 经 Shell 原生通知自动更新（watcher fallback refresh=0），其他窗格计数不变。部分单项/两项 Drop 返回时前台 HWND 暂指向不可见的 Shell“进度”窗口；调用点读数确认释放 `ShellDropProbe.Copy` 中的 COM 接口后该前台窗口仍短暂保留，HostProbe 进程退出后恢复。后续操作轮中前台一直是已有可见的其他应用窗口，说明直接调用路径的前台变化不稳定；这些观察均不能代表真实 `DoDragDrop` 期间或结束后的应用焦点行为。

2026-10-24 跨窗格复制/移动效果隔离探针：Release HostProbe 对目标生产 `IShellView` 的 `IDropTarget` 直接调用三组隔离夹具：仅允许 Copy 时文件被复制；仅允许 Move、分别传入 Shift 和无修饰键状态时文件均移动。源/目标文件内容与存续状态匹配；连续三次操作后两侧 Shell 视图自动收敛，源/目标 watcher fallback 均为 0，另外两窗格不变。限制：同时允许 Copy 与 Move 时，Ctrl 状态下直接 `DragEnter` 返回 3（两个效果位），不能证明真实 OLE 循环的修饰键选择；Move 的直接 `Drop` 虽成功移走源文件且目标内容匹配，返回效果码为 0。真实 `DoDragDrop` 修饰键语义、文件列表鼠标拖动、焦点恢复仍未验证。

2026-10-24 应用文件命令状态与新建文件夹探针：Release 隐藏生产宿主直接查询 `ExplorerBrowserHost.QueryFileCommands()`，无选区时 Cut/Copy/Delete/Rename/Properties 全部禁用，选择隔离 `initial.txt` 后全部启用，相邻空窗格仍全部禁用。另经生产 `ShellFileCommands.Invoke(NewFolder)` 在隔离目录创建一个空文件夹，目录监视器基线已就绪时视图最终可见。
新增“监视器尚未就绪时立即新建”路径后，多次早期运行均在 watcher 建立前执行，生产短期 `FolderCreationObservation` 准确捕获唯一新目录，watcher 创建事件及计数 fallback 都为 0；初次测试曾出现等待 8 秒仍显示 1 项。产品流程现加入一次恢复：仅当短期观察确认唯一新目录、Shell 焦点未匹配且导航仍有效、用户没有继续输入时，调用保留现有选择/焦点的 `Refresh()`；不强制改变焦点，也不在观察失败/歧义或用户继续操作时刷新。SettingsSmoke 门控回归通过；HostProbe 早期恢复路径连续 3 轮均在视图计数和异步恢复回调完成后显示新目录，原有 `initial.txt` 选区及焦点均保持，watcher 创建事件为 0；ready 对照由 watcher 自然更新且没有请求恢复刷新。隐藏命令探针绕过窗口激活门控，尚未覆盖 `CreateNewFolder()` 的可见按钮/快捷键、刷新完成后的自动重命名、真实输入中断和物理焦点；这些仍待隔离交互验收，文件操作 P0 不关闭。运行时偶有来源未明的 `dark mode 1` 输出，不能据此归因。

2026-10-24 四窗格 Shell 文件操作监视复跑：Release `--live-shell-operations` 在四个隔离视图中通过诊断用 `IFileOperation` 复制后 Shell 计数均由 1 到 2，再删除副本后均回到 1；Watcher 创建/删除均为 4、错误为 0，最终各窗格仅保留原始 `initial.txt`。这验证 Shell 自身操作后的四窗格自动更新，不验证应用菜单/快捷键分派、撤销/回收站、冲突对话框或人工恢复；关键文件操作 P0 仍开放。

2026-10-24 原生目录重枚举后的选区保护复验：修正旧探针动态复制前置步骤因 Shell 枚举不稳定而产生的无效基线，改为视图启动前预置未选中删除目标，再删除并发送一次 `SHCNE_UPDATEDIR`。Release 隐藏生产宿主在无 pane 0 watcher 和无计数 fallback 下恢复到 3 项；`second.txt`/`third.txt` 选区及 `initial.txt` 焦点保持。删除前输入代数递增的控制场景也通过。此结果专门验证原生 `UPDATEDIR` 触发的视图重枚举，不代表无通知自然更新、watcher 路径、可见 Explorer 或物理键盘；详见[调查记录](live-update-investigation.md#2026-10-24-选择恢复探针改为稳定的删除基线)。

2026-10-24 F2 多窗格方向键回归复跑：Release `--live-shell-rename-editor-pane-0` 到 `-pane-3` 四个隔离生产 Shell 宿主均通过。每窗格均通过 `SVSI_EDIT` 进入原生重命名编辑器，确认 Left/Right 移动名称内光标，Left/Up/Right/Down 经生产 `TranslateAcceleratorCore` 后仍处于编辑态并保留该窗格 Edit 焦点，其他窗格不认领焦点。此证据覆盖编辑态按键路由，不覆盖可见窗口中物理 F2 的消息进入路径；该项仍待 GUI 验收。

2026-10-24 目录树数字小键盘按键补测：生产 `NativeShellTree` 隐藏宿主建立含两层子目录的隔离根，选中根并将焦点置于实际 `SysTreeView32` 子窗口后，向控件消息队列投递 `VK_ADD`、`VK_MULTIPLY`、`VK_SUBTRACT` 的按下/字符/抬起消息，确认 `+` 展开根、`*` 展开子目录及深层目录、`-` 折叠根。随后在隐藏 WPF `HwndSource` 中挂载生产 `ShellTreeHost`，通过生产 `TranslateAcceleratorCore` 处理相同 `MSG`；仅当加速器未处理时才按消息循环派发到原生 HWND。WPF 宿主探针同样通过，结束后窗口销毁且前台未变。为建立隔离根，该探针直接设置内部树状态，绕过 `IsVisible` 门控下的自动目录同步；不验证实体 Num Lock 输入、用户 Tab/F6 获得焦点或可见窗口体验，仍需桌面验收。详见[键盘操作清单](keyboard-shortcuts.md)。

2026-10-24 跨窗格拖放接口探查：尝试从目标生产 ExplorerBrowser 的 `IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IDropTarget)` 直接取得 Shell 背景 drop target，但当前实现未暴露该接口。此结果不能说明注册在窗口上的原生 OLE 拖放不可用；调用真实 `DoDragDrop` 会进入系统拖动循环，本轮不做桌面输入。没有产品代码改动，跨窗格文件拖放仍须真实 GUI 验收。

2026-10-24 自动刷新 10,000 项后续复测：Release 隐藏生产宿主中 watcher 完整收到 10,000 个创建/删除事件，视图、原有两项选择与独立焦点最终恢复；创建/删除后 Shell 计数 settle 均约 4.6 秒，全轮触发 3 次 fallback，最大 Dispatcher 心跳间隔 139 ms。结果确认可收敛但中间视图有长尾；仅本机隐藏宿主，不据此更改策略，自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-10000-项-watcher-后续复测)。

2026-10-24 快速过滤能力探针：最初的本机隐藏 ExplorerBrowser 探针确认 `IFolderFilterSite` 可用；该能力随后已接入产品并通过生产宿主测试，当前状态见下方快速筛选条目及[Shell 对照](shell-parity.md#搜索与过滤)。仍未覆盖非文件系统命名空间、刷新性能及真实 GUI。

2026-10-23 自动刷新 P0 产品兜底已接入源码：每个已打开的本地固定磁盘目录标签在导航稳定后启用非递归 `FileSystemWatcher`，按 750 ms 静默窗口合并创建/删除；只有当前 Shell 项目数与变更后预期不符才调用保留选区/焦点的现有 `Refresh()`。重命名与 watcher 溢出直接要求刷新，因为项目数无法检测身份变化。发现导航完成时 Shell `ItemCount` 可成功返回但仍暂报 0；现在启动 watcher 前要求计数连续稳定五次（250 ms 间隔，最多 10 秒），避免用未完成枚举作基准。大批量变化采用 6 秒有界等待。又修复了异步 fallback 期间的状态竞态：刷新回调仅在 Shell 计数包含刷新期间的新 watcher 变化时才重设基准；不匹配时保留变化、允许一次刷新重试，第二次仍不匹配则接受当前计数以避免循环。Settings smoke、App/HostProbe Release 构建和常规隐藏生产宿主测试通过；基准为 3 的一轮 3,000 文件压力测试，在创建/删除后都保留了两项选区及独立焦点，且未触发 fallback。其他旧基准轮次已证明可出现不必要 fallback；自然漏更、重试循环边界及真实窗口仍待验证，因此 P0 不关闭，不发布。详见[实现与测试记录](live-update-investigation.md#2026-10-23-产品级本地目录自动刷新兜底)。

2026-10-24 自动刷新跟进：初始 Shell 项目计数需连续稳定五次后才作为 watcher 基准，防止导航完成回调先读到暂态 0。两轮 3,000 项隐藏压力验证均确认初始基准 3、生产 watcher 收到完整创建/删除事件；选区及独立焦点两轮均保留，未触发 fallback。常规隐藏宿主另通过合成计数偏差验证：Shell 仍有 4 项时注入预期 +1，产品计数检查触发恰好两次 fallback，第二次后基准恢复为 4；刷新期间还从另一线程经生产队列注入 -1，完成回调读取并合并该增量后仅需一次 fallback，选区/焦点保持。真实重命名仍触发强制 fallback。自然漏更场景和真实窗口仍待验收，P0 保持开放。

2026-10-24 文件夹与跨卷隐藏验证：产品监视探针使用 Shell `IFileOperation` 新建/删除文件夹，两类 watcher 事件均到达且列表最终可见；文件夹创建没有误走非文件夹 `SHCNE_CREATE`，创建和删除各由一次计数 fallback 补齐。跨固定卷 `D:`→`C:` 的 Shell 复制、删除、移动三轮均最终通过，分别核对源/目标 watcher、文件内容和四窗格计数；第一轮无 fallback，后两轮源/目标窗格分别累计 `1/2` 次 fallback。仅为隔离 fixture 证据，不代表慢速/网络盘或 GUI。文件复制仍有约 1.9 秒 fallback 样本，自动刷新 P0 保持开放。详见[调查记录](live-update-investigation.md#2026-10-24-文件夹与跨卷-shell-操作补验)。

2026-10-24 Release 产品监视器复跑：无父级 Shell 观察器时，单文件复制 watcher 于 99 ms 收到创建、Shell 视图 900 ms 显示，841.6 ms 启动 fallback 后收敛；完整跨窗格 copy/move/delete、同目录 move/delete、文件夹变化和选区/焦点恢复检查通过。四窗格 watcher 创建事件均为 3，8 次 fallback 中含两次计数 mismatch 和一次刷新中并发变化；刷新读回 expected/actual=4 并合并增量 -1。自动刷新仍需可见窗口及慢速/网络盘验证，P0 不关闭。

2026-10-24 自动刷新静默期 A/B 与更新：记录到 `SHCNE_CREATE` 在约 108–138 ms 已发送返回，慢样本仍于约 900 ms 才由 fallback 刷新显示；不是 Dispatcher 尚未发送通知。250 ms watcher 静默期 A/B 三轮首次可见为 357/119/475 ms，正式改为 250 ms 后默认路径三轮为 392/369/132 ms，其中两轮由 fallback 收敛、一轮自然更新；完整文件/文件夹/跨窗格操作及选区/焦点检查通过。Release 构建、Settings smoke 通过。当前证据只覆盖本机隐藏生产宿主，慢速/网络盘及可见窗口仍待验收，P0 保持开放。详见[自动刷新调查](live-update-investigation.md#2026-10-24-缩短本地目录自动刷新静默期)。

2026-10-24 250 ms 静默期下的 3,000 项目录压力复跑：创建 1.63 秒，生产 watcher 创建/删除事件均完整；128 项批量删除的最新路径查询 1 ms，最大 Dispatcher 心跳间隔 113 ms。创建直接收敛，批量删除由一次计数 fallback 完成；两项选区与独立焦点保持，最终磁盘及 Shell 视图均回到 3 项。HostProbe Release 构建与 Settings smoke 通过。结果限本机隐藏宿主，不代表可见 GUI、慢速/网络盘或长期负载，自动刷新 P0 继续开放。详见[压力调查记录](live-update-investigation.md#2026-10-24-250-ms-静默期下的-3000-项目录压力复跑)。

2026-10-24 10,000 项刷新后焦点复查：正式 250 ms 路径 8 轮中 3 轮最终焦点未回到操作前项目，HostProbe 按失败退出；另 5 轮最终焦点正确。新增原生调用跟踪确认一次失败：刷新时 batch selection 成功，但 `ContainsItem` 当时找不到保存的 focused PIDL，因此焦点 `SelectItem` 未执行；同轮成功样本中焦点 PIDL 可找到且 `SelectItem` 返回 `S_OK`。视图计数达到目标仍不足以证明焦点 PIDL 已可查询。自动刷新/大目录焦点 P0 继续开放，下一步把找不到焦点 PIDL 时的延后恢复转成带导航/用户输入门控的回归并修复；不操作可见 GUI、不发布。详见[焦点稳定性复查](live-update-investigation.md#2026-10-24-10000-项刷新后的焦点与选区稳定性复查)。

2026-10-24 超大选区补验：修正夹具顺序为先建档再绑定 Explorer，并让探针模拟产品用户输入结算门控。10,001 项目录导航约 0.7 秒；全选并排除一个未选中目标后删除该目标，10,000 项选区身份全部保持。两次 Shell 自然清选保护样本恢复耗时 86/89 ms；另一次 Shell 自行保留选区、无需恢复，fallback 均为 0。10k 隐藏验证通过，真实键盘操作、可见窗口与其他 Windows 版本仍待验收。

2026-10-24 10,000 项压力补验：一轮曾在最终检查中发现选择/焦点恢复失败；新逐次诊断显示另一次带三次 fallback 的运行中，每次刷新都捕获到原来的两项选择和焦点，刷新期间采样短暂显示 0 项，约 253–275 ms 后恢复。两轮无 fallback 的 10k 样本及最新 3k 样本也保留了选择/焦点。间歇失败仍未与某次快照捕获同步定位；自动刷新与大目录焦点 P0 保持开放，继续重复 fallback 样本，不先改产品策略。Shell 删除项身份查询 12 ms，含 watcher 等待的墙钟 50 ms。见[调查记录](live-update-investigation.md#2026-10-24-十千项压力与恢复边界)。

随后一轮 10k 产品监视发现无 fallback 的自然批量删除也会清空未受影响的两个选中项，焦点保持，目录计数最终正确；创建 fallback 时选择恢复调用 51 ms。计数检查无法捕获选择状态丢失。该样本用外部 File.Delete，不等同于应用内 Shell 文件操作；增加独立的自然收敛选区回归，确认场景语义后再改生产行为。自动刷新 P0 继续开放。见[调查记录](live-update-investigation.md#2026-10-24-10k-自然删除期间的选择丢失)。

native-only 单文件对照在停用产品 watcher、Shell 通知和 fallback 后复现同一现象：删除一个未选中项，ExplorerBrowser 自然将计数从 4 收敛到 3，但清除了另外两项选择，焦点仍在原项。100 文件产品监视对照也在一次外部删除后清选。问题不局限于 10k 负载；下一步核查应用内 `IFileOperation` 语义及 Shell 事件序列，确认后再实现受控的存续选择恢复。可见 Explorer 对照仍待桌面验收。自动刷新/选择体验 P0 开放。详见[单项删除对照](live-update-investigation.md#2026-10-24-10k-自然删除期间的选择丢失)。

隔离 `IFileOperation` 单项删除对照也复现：关闭产品 watcher 后，Shell 删除未选中测试项会自然更新视图、保留焦点、清除其他两项选择；`ICommDlgBrowser3` 收到两次 `CDBOSC_SELCHANGE`。生产自动刷新没有参与。真实 Explorer 是否同样清选尚待可见对照，暂不将其归为产品缺陷或改变 Shell 原生语义。见[Shell 操作对照](live-update-investigation.md#2026-10-24-10k-自然删除期间的选择丢失)。

2026-10-24 复跑确认同一原生删除结果：无产品 watcher、通知或 fallback 时，删未选中项后两项选择清空、焦点保持，`CDBOSC_SELCHANGE` 从 7 增至 9。同步的生产监视器中，单文件复制 191 ms 自然显示，整体四窗格与文件夹检查通过。仍需等可见 Explorer 对照后再决定是否补回存续选择，避免未验证的焦点/选区抢占。

Ctrl+C/V 焦点源码复核：Shell 原生快捷键路径与菜单/工具栏 canonical verb 路径都会在命令返回后立即及 250 ms 后恢复一次；窗口 Activated 另有一次 FocusCurrentView，符合 Alt+Tab 后恢复的现象，但异步 Paste 完成晚于既有恢复只是待验证假设。扩展 `EXPLORER_DIRS_FOCUS_TRACE`，现在也会记录恢复门控跳过原因、前台 HWND、视图可见/就绪状态及 `FocusView()` 后的实际焦点。App Release 构建和 SettingsSmoke 通过。下一步仍需在允许的隔离桌面测试中收集实际 Ctrl+C/V 时间线；当前不触碰剪贴板、不调整恢复重试策略；文件操作焦点 P0 开放。

2026-10-24 历史 trace 复核与四窗格验收准备：仓库中 2026-10-15 的 Ctrl+C/V 日志来自 `Layout=1` 单窗格旧构建，若干粘贴路径同步耗时约 0.8–1.44 秒、随后焦点回到 DirectUI，方向键有改变 focused index 的记录；这些旧证据不能解释当前多布局问题。隔离 `GuiSmoke -PrepareOnly` 现默认准备 `Layout=4`，四个窗格使用独立临时目录，并保留 Pane0 搜索夹具。Release 隔离副本构建及 INI/夹具核对通过；未启动 GUI、输入或剪贴板。多窗格 Ctrl+C/V 与方向键仍待可见验收，不据此改焦点重试时长。详细边界见[焦点调查记录](live-update-investigation.md#2026-10-24-ctrlcv-历史焦点-trace-与多窗格复现范围)。

追加一轮 3,000 项产品监视压力：初始计数稳定为 3，生产 watcher 创建/删除事件均为 3,000，Shell 计数最终 3,003→3；创建时首次读取已到目标，删除后 312 ms 从 861 收敛到 3。该轮未触发 fallback，选区/焦点保留，Dispatcher 最大间隔 65 ms。独立诊断观察器创建计数为 2,998 且无错误，与生产 watcher 分开看待。该轮是无 fallback 的大批量补充证据，不抵消既有迟到样本；P0 继续开放。详见[压力记录](live-update-investigation.md#2026-10-24-文件夹与跨卷-shell-操作补验)。

2026-10-24 文件夹通知候选验证：微软 Shell 事件契约规定文件夹创建用 `SHCNE_MKDIR`。隐藏生产宿主三组交替 A/B 中，Shell 视图确认仍旧时发送 MKDIR 的样本有 1/3 于 250 ms 内更新（123 ms）；无通知组为 0/3。两组均有两轮触发 fallback，不能证明 MKDIR 稳定改善延迟，故保留探针但不接入生产；现有 watcher/fallback 继续保障最终收敛。见[调查记录](live-update-investigation.md#2026-10-24-文件夹-mkdir-通知-ab)。

2026-10-24 删除通知跟进：产品已在 STA 上将 Deleted 路径合并 250 ms，只查询最后一个路径；计数不符且 stale Shell item 的 `SFGAO_FOLDER` 确认是目录时才发送 `SHCNE_RMDIR`。隔离生产宿主连续 3/3 文件夹样本于 watcher 后 359–373 ms 收敛且未触发 fallback；更复杂的全量产品监视轮也发送了 RMDIR，但该次仍由 fallback 补齐，因此 fallback 必须保留。精确文件 `SHCNE_DELETE` A/B 仍无提速，文件返回 `NotFolder`，不发送 RMDIR。10k 压力中产品身份查询 1 次，另一个 128 删除事件批次只查一次代表路径（3 ms），Dispatcher 最大间隔 82 ms；该轮计数/焦点正确，但 Shell 自然更新清空未受影响选择，测试按选区断言失败。详见[产品 RMDIR 与批量查找记录](live-update-investigation.md#2026-10-24-产品-rmdir-接入与批量删除查询)及[文件 DELETE A/B](live-update-investigation.md#2026-10-24-文件删除-delete-通知-ab)。

2026-10-24 生产监视器 Shell 操作补验：隐藏生产宿主探针使用真实 `IFileOperation::CopyItem` 计时 27 次，并将重命名与删除路径改为真实 `IFileOperation::MoveItem` / `DeleteItem`。27 次复制中 18 次自然视图在 549–609 ms 达到 4 项，9 次在首次看到 4 项前触发 fallback，1,897–2,834 ms 后才显示；刷新前记录 `expected=4/actual=3/forced=False`。另有一次完整探针在合成 mismatch 重试后批次基准意外变成 0；增强失败诊断后仍未复现。为下次定位，批次增加最近完成刷新时的预期/实际计数快照；Settings smoke 先红后绿，之后 24 轮完整探针通过。连续三轮同目录 Shell move 均触发强制刷新且新名称可见，DeleteItem 后删除项从视图消失；跨窗格 Shell copy/delete 与 MoveItem/delete 又连续三轮验证源/目标 watcher 和视图计数正确。新三轮中一轮同目录复制仍触发非强制 fallback 后才显示，另外两轮自然显示。隐藏样本不验证 Ctrl+V、系统剪贴板、跨窗格拖放、回收站/撤销或用户可见行为；P0 保持开放。详见[调查记录](live-update-investigation.md#2026-10-24-生产监视器接入-ifileoperation-复制)。

2026-10-24 自动刷新迟到时序诊断：在现有 opt-in 子视图消息计数上增加重置点和相对时间，并记录 fallback 起止；另以不解析 PIDL 路径的父级 Shell 通知观察器做六轮对照。四轮收到父级通知的样本中，事件约 531–546 ms 到达，`0x4AE` 子视图消息紧随其后；一轮父级观察器无事件但视图仍自然更新；另一轮无事件样本中 watcher 77 ms 已收到创建，直到 fallback 于 1,820.6 ms 开始后才出现后续子视图消息并显示。私有消息语义未知且观察器会影响调度；结果只作为时间相关证据，不代表根因已定，不改产品定时策略。详见[调查记录](live-update-investigation.md#2026-10-24-生产监视器接入-ifileoperation-复制)。

2026-10-24 父级 Shell 观察器 A/B：新增同参数的无观察器模式。新入口漏建两项 fixture 的两次初始化失败已修正并排除；修正后观察器开/关各两次，视图可见时间分别 552/536 ms 与 543/593 ms，均无 fallback。样本不足以说明观察器是否影响调度；此前无观察器也出现过约 1.9 秒迟到样本，因此 P0 仍开放，不按这四次快速结果改变策略。详见[调查记录](live-update-investigation.md#2026-10-24-生产监视器接入-ifileoperation-复制)。

2026-10-24 Ctrl+滚轮隐藏消息路由跟进：修正 HostProbe 等待时序后，先前向空 `_viewWindow` 发送消息的“未变化”结果作废。有效的生产 WPF 隐藏宿主测试确认向 `SHELLDLL_DefView` 发送 Ctrl+120 会将 `(mode=4,size=16)` 读为 `(4,18)`；同一消息直接发送给 `ExplorerBrowserControl`、宿主 `Static` 或 WPF 根 HWND 均不变。八种查看方式下 Shell 只改尺寸，不改 mode；其他窗格不变。说明本机 Shell 子视图能处理合成滚轮，不说明真实输入必然路由到该 HWND，也未覆盖可见绘制、焦点和选区。暂不添加重复转发；待桌面交接用 `EXPLORER_DIRS_FOCUS_TRACE` 捕获物理输入路由。详见[查看方式调查](view-style-wheel-investigation.md#隔离消息路由探测)。

2026-10-24 F2 方向键隔离分派补测：在隐藏生产 ExplorerBrowser 视图下创建 Edit 子控件，定向调用 `TranslateAcceleratorCore` 的真实重命名编辑分支，四个方向键全部被标记已处理，焦点和编辑文本保持。未调用 Shell F2 启动内联编辑，未经过 WPF 实际键盘循环；仅验证候选修正的 HWND 分派代码，用户报告的多窗格 GUI 缺陷仍待桌面复测。

尝试在隐藏 `SHELLDLL_DefView` 上发送合成 F2 并自动进入真实 Shell 重命名态时，线程焦点为 0，Shell 没有创建 Edit 子窗口；探针以专用 exit 3 保留此环境边界。未为取得焦点而显示或激活测试窗口。真实 F2、方向键及跨窗格焦点循环仍须空闲桌面验收。

2026-10-24 隐藏 Shell 重命名态补测：探针改用 `IShellView::SelectItem` 的 `SVSI_EDIT`（`0x3`）启动真正的 Shell 内联重命名编辑器；`IsRenameEditorFocused` 确认原生 Edit 子窗口获焦后，四个方向键经生产 `TranslateAcceleratorCore` 分派均保持编辑焦点，完整隐藏 Shell 宿主测试退出码 0。纯 Edit 路由回归和 Settings smoke 通过，Release HostProbe 构建 0 警告/错误。微软文档确认 `SVSI_EDIT` 用于将条目置于重命名态：[SVSIF 枚举](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif)。这绕开了隐藏视图中无法由合成 F2 建立焦点的限制，但没有验证物理 F2 是否进入该路径、WPF 跨窗格真实键盘遍历或可见多窗格行为；桌面验收仍开放。

2026-10-24 四窗格隔离复测：将同一隐藏 HwndSource 中的顺序跨窗格启动改为 pane 0–3 分别独立启动，四次都在对应的真实 Shell 重命名 Edit 中通过左/上/右/下路由，且焦点归属检查未命中其他 pane。一次同进程从 pane 0 编辑态直接切到 pane 1 的试验无法在隐藏宿主建立第二个 Shell 编辑焦点，不能解释为产品多窗格键盘行为；不计入通过。探针新增 pane 选择参数和 `finally` 中 GUID 夹具目录清理，四次独立 Release 复跑均退出 0。该证据仍不覆盖真实 WPF 方向键焦点遍历、物理 F2 或可见窗口，GUI 待办保留。

2026-10-24 F2 编辑行为补强：四窗格隐藏生产 Shell 探针现在在真实重命名编辑器内设置中间光标，分别确认 Left/Right 会移动文本光标，随后验证 Left/Up/Right/Down 全部经 `TranslateAcceleratorCore` 处理且编辑焦点和窗格归属保持。四个 pane 的 Release 探针均通过。该证据仍不模拟物理键盘，也不验证 Shell 通过物理 F2 建立编辑器及可见多窗格行为；GUI 验收保留。

2026-10-24 自动刷新单项迟到改善：隐藏生产监视器再次观察到单文件创建在 2,066 ms 才显示，计数 fallback 于 1,921.5 ms 开始。现对单项计数差在既有 750 ms 静默期后立即 fallback；2–31 项仍多等一个 500 ms 样本，大批量保留 12 次有界等待。回归先红后绿；修改后 Debug 完整 Shell 监视器中，复制样本分别 870 ms 由 fallback 显示、543 ms 自然显示；Release 完整监视器复制样本 134 ms 自然显示。完整监视器的选区/焦点恢复、跨窗格操作、文件夹事件和计数检查均通过，Release 构建和 Settings smoke 也通过。详情见[自动刷新调查](live-update-investigation.md#2026-10-24-单项计数不一致的兜底延迟调整)。仅为隐藏本机宿主证据，慢速/网络盘和可见 GUI 仍待验收，P0 不关闭、不发布。

2026-10-24 临时预览发布：生成 `releases/preview-0.6.0-temp.4/app/ExplorerDirs.App.exe`，产品版本 `0.6.0-preview.temp.4`，独立 `app/profile`，EXE/DLL SHA-256 与清单核对一致。发布隔离 smoke、Settings smoke 和 publish 构建通过；未启动 GUI，因此此包是可供验收的源码预览，不代表真实窗口验收通过。待测项列于[第四版说明](releases/preview-0.6.0-temp.4.md)。同时修复发布脚本在发布目录升级后仍沿用旧程序集版本的问题，发布 smoke 先失败后通过。

2026-10-23 按用户要求继续纯后台开发，不使用 Computer Use。已接入窗格分隔条、每布局独立比例及顶部菜单恢复均分；无窗口几何和配置保护检查、Release 构建通过，尚未发布。F2 多窗格编辑候选修复、自动刷新 P0 和真实 Shell 体验验收继续保持未关闭。

同日继续补齐 Q-Dir 四宫格独立 SP1/SP2：本程序四宫格可分别拖动上下两排左右边界，INI 比例升 v4 并从旧共用比例迁移；带独立下排的扩展升 v3，保留 v1/v2 读取。审查发现并修复越界拖动累积位移；连续三轴上下边界、取消、布局切换迟到事件、配置迁移、旧 v3 读者拒写、QDR 输入输出回归通过。无窗口几何已检查，真实鼠标操作、焦点及 Q-Dir GUI 互操作仍待桌面验收，未发布。

同日刷新调查补充跨进程对照：由隐藏子进程执行 fixture 的 Shell 操作，父进程仍复现视图滞后；随后普通 I/O 对照在独立监听器收到四条 CREATE 的情况下也未更新。因此同进程操作阻塞、监听器缺失通知均不足以解释全部后台反例，下一步检查视图自身订阅/消费与隐藏条件。没有产品刷新补丁，P0 继续开放，见[跨进程证据](live-update-investigation.md#2026-10-23-跨进程操作与通知已到达的反例)。

同日自动刷新调查新增无界面 `IFileOperation` 复制/删除对照：磁盘成功与视图更新分别检查，3 次失败、1 次通过，另有 3 次普通文件 I/O 对照通过；失败时独立 Shell 观察器也只收到部分或缺失的变更通知。已加入时间线和真实文件身份检查，仍未定位根因或修改产品刷新策略，详见[调查记录](live-update-investigation.md#2026-10-23-无界面-shell-文件操作对照)。

继续验证现有显式 `Refresh()`：隔离四窗格发现原生刷新会清除选区并将焦点移到新增文件。生产宿主现用相对 PIDL 快照，在 Dispatcher 后台阶段并通过当前视图/导航保护恢复状态。复制用例保留三项选区和焦点；删除已选焦点文件时过滤失效项并把焦点放到剩余选中项。四窗格复制、删除用例均通过。未接入自动变更通知，P0 仍开放；排序/分组及真实窗口行为仍待验证，见[刷新恢复记录](live-update-investigation.md#单选恢复跟进)。

追加显式刷新状态边界：隐藏生产宿主验证两个选中项与一个未选中焦点项同时存在时，复制并刷新后两项选区与独立焦点身份均保留；同轮复制/删除回归继续通过。该证据仅限本机 fixture/隐藏宿主，不改变自动刷新 P0 或桌面验收状态。

自动刷新排查新增 `CDB2GVF_NOINCLUDEITEM` 隔离矩阵、HWND 显隐与阶段消息差分：置零 flag 无稳定差异；折叠时 WPF 宿主清除 `WS_VISIBLE`，Shell view HWND/样式不变，四轮隐藏父窗口配对中折叠窗格均自动更新。随后新增同 STA message-only HWND 临时观测，并捕获部分失败：四窗格操作时父观察器只收到部分目录事件；单目标 pane 3 在有选区和无选区时都间歇失败，失败时可能无 Shell 事件，也可能父观察器已收到 `UPDATEDIR`/准确 `CREATE` 而视图仍旧，仅见未解释的 `0x4A3`。单选创建有效重跑 10 次中 3 次失败。HostProbe-only 750 ms 文件系统差异检查在创建及删除的失败轮调用现有 Refresh 后，分别保留选区与焦点；正常更新轮不额外刷新。该策略尚未进产品，也未覆盖大目录、慢速/网络复制、重命名及真实窗口。`IShellView::TranslateAccelerator` 后的 Ctrl+V `WM_COMMAND` / `0x701A` 与菜单 `IContextMenu` canonical `paste` 路由已做源码核对，但实际 Paste 需要剪贴板，因此本轮没有执行。自动刷新 P0 继续开放；下一步先评估监视器生命周期、事件合并、导航竞态与大目录成本，再做隐藏宿主验证；不碰用户剪贴板、不作产品刷新补丁。首批未等 attach/复用视图结果已作废。详见[调查记录](live-update-investigation.md#2026-10-23-自动刷新阶段差分间歇失败)。

独立 STA 后续对照：3 次双监听和 1 次关闭独立监听均通过，没有出现两线程投递分歧；尚不能支持“生产 STA 导致通知缺失”的假设。保留可重现入口，自动刷新 P0 不关闭，不继续用重复绿色检查代替根因证据，见[对照结果](live-update-investigation.md#2026-10-23-独立-sta-通知监听对照)。

配置/发布接线补查修正：隔离临时包补齐 `NavigationTree.ini` 与顶层 `Workspaces/*.qdr` 复制，避免已有目录树偏好和工作区收藏在预览副本中消失。真实打包脚本在独立 fixture 仓库中先复现遗漏、修正后通过；保留源文件字节、拒绝同名版本、普通发布不复制 profile 的检查同时通过。构建步骤使用占位文件，未创建真实 release 或进行 GUI 验收。

搜索后续：四个生产 WPF 宿主的隐藏集成验证新增八阶段，覆盖实际发布事件、返回目录、连续请求及目录视图缓存。先复现“旧搜索 Pending 取消新目录探测”和原生 ERROR_BUSY，再修正并通过。未发布，真实窗口验收及自动刷新 P0 仍待完成；详见[搜索调查](search-folder-investigation.md#生产-wpf-宿主集成与快速导航)。

2026-10-21 搜索修正：默认 search-ms 解析返回协议对象而非文件夹；使用文件夹解析上下文并验证属性，加入每浏览器查询身份恢复与碰撞保护。新增回归先红后绿，隐藏原生 two-query/后退/前进四阶段验证通过；未发布，真实 App 窗口搜索/Esc/切标签与保存仍待验收。见[搜索调查](search-folder-investigation.md)。

后续后台调查更新：单一持续消息循环仍出现反例，重复最小原生/Forms 对照也失败，不能据一次最小对照通过排除系统通知或隐藏视图条件。加入独立 Shell 通知观察器，仅用于探针；没有给产品添加定时刷新。见[最新调查](live-update-investigation.md#后续连续循环与通知边界调查)。

2026-10-21 桌面交还后的后台推进：新增原生/生产 WPF 宿主四窗格自动更新探针；真实保存视图配置仍有间歇失败，候选接口不足以修复的结论不变。已修正探针消息循环制造的假象，失败返回非零。下一步按[调查记录](live-update-investigation.md)定位通知时序，不再据单次通过发布修复结论。

**13:07 最新验收纠正：temp.3 精确二进制右下窗格仍复现粘贴成功但列表不更新。自动刷新保持 P0 未解决，ICommDlgBrowser3 仅为候选改动；不得据下方早先局部通过项关闭缺陷。**

2026-10-21 12:50 桌面交接后实测发现 temp.2 文件操作成功但视图不自动更新。补齐 ICommDlgBrowser3 后，粘贴/删除自动更新及紧接方向键、名称排序通过源码候选 GUI 检查；已另发独立配置 temp.3，精确二进制隔离启动通过。搜索在修正前后均失败并可能另开 Explorer，列为下一项缺陷。完整焦点遍历、Ctrl+滚轮和 QDR 互操作仍待验收。见[本次记录](gui-acceptance-2026-10-21.md)。

2026-10-21 用户要求先提供发布包预览，已打包 `preview-0.6.0-temp.2`，独立配置，保留旧版。包含截至本次的源码改动；Settings smoke 与发布构建通过，精确发布二进制 GUI 启动和操作仍待验收，不据提前发布勾选功能验收。见[发布说明](releases/preview-0.6.0-temp.2.md)。

更新至 2026-10-21 源码进度，优先顺序沿用 2026-10-20 用户临时版反馈；勾选仅表示代码已实现并通过相应的非交互检查，不代表完成用户场景验收。

同日 `.qdr` 视图校验补齐：通过 12.79 静态读写路径及 68 个本地保存字段，
确认 CODEMODE1 第三字段是完整数据的有符号 32 位校验值；导入校验失败时
保留目录与显式模式并警告。错误校验回归先红后绿，真实配置只读导入保持既有视图数。
同日已进一步接入原生视图导出：完整列快照的标签写出排序、分组、图标大小和列设置，
未捕获列布局的标签保留扩展状态。Windows 属性直读及去扩展读回通过；
Q-Dir GUI 互操作仍待验收，见[格式依据](qdr-import.md#2026-10-21codemode1-校验值)。

同日原生消费者验证发现输出缺少 LogicalViewMode：256 图标被 Shell 恢复为 48。
补齐后，隐藏 IShellView 直接读取导出流的 16 个场景通过，恢复路径不经过本程序解码器
或视图设置 API。该结果加强视图输出证据；Q-Dir GUI、再保存及 Ctrl+真实滚轮仍待验收。

2026-10-21：新标签默认查看选项补齐八种预设，分别保存模式和图标大小，保持现有标签及自定义默认值。
会话 v3 兼容读取 v1/v2，升级前备份原文；`.qdr` 本程序扩展保留默认尺寸。
配置写读、往返、升级及旧预览版拒绝覆盖 v3 的实测通过。真实菜单、新标签显示和重启仍待 GUI 验收；
未更新发布包，P0 焦点及 Ctrl+滚轮优先级不变。

同日补查：隐藏原生视图使用生产列管理器，验证自定义列顺序/宽度在八种模式、
有/无分组中保持；减少了查看切换丢失列设置这一项未知风险。
该结果不替代 Ctrl+滚轮和真实窗口验收，见[查看方式调查](view-style-wheel-investigation.md)。

同日源码修正：新建文件夹在 Shell 菜单准备后复查原浏览器、导航版本、请求版本和活动窗格；
延迟重命名复查观察器身份、选区版本及文件视图焦点意图。旧计时回调不能取消新操作的监听。
菜单/工具栏/快捷键统一入口；没有改为自行创建文件夹。真实 Shell 重入及焦点场景尚待 GUI 验收。

同日 `.qdr` 活动窗格补齐：原生 `Focus` 采用 0～3，通过布局映射转换；仅应用可见且有导入标签的窗格。
导出写回原生编号并将字段纳入扩展哈希，旧无 Focus 导出仍可读取。各布局所有可见窗格、
隐藏/空目标、无效值、外部修改及旧扩展兼容回归通过；实际窗口键盘焦点待验收。

2026-10-19：用户要求先发布临时版体验变化，交付 `preview-0.6.0-temp.1`（独立配置副本），见[变化与待验收项](releases/preview-0.6.0-temp.1.md)。已恢复桌面测试并验证隔离副本粘贴后方向键；删除确认后串到另一窗格的问题已复现、定位并写入候选修复，复测被用户 Esc 中断。先收集试用反馈再调整后续优先级，不据临时打包勾选 GUI 验收。

## 当前优先顺序（用户 2026-10-20 更新）

2026-10-21 布局交互核查：现有六种布局使用固定比例，尚无拖动分隔条及每布局比例保存。
将这一差距记录在第二阶段组件体验清单，先完成核心文件操作、视图和 `.qdr` 的真实窗口验收。
后续调整比例必须保持现有原生视图实例和每标签历史；本次没有新增分隔条或改变布局配置。

2026-10-21 导航状态调查获得可用入口：ExplorerBrowser 的
`DWebBrowserEvents2::CommandStateChange` 实际发送前进/后退状态。
隐藏探测覆盖初始、前后退、历史中间、取消、新分支及同路径重复记录，
实际 PIDL/状态匹配，未显示窗口或抢焦点。同日已接入每标签订阅、按钮与快捷键动作门控；
生产订阅器通过同一 11 阶段原生检查及连接点生命周期回归。真实按钮焦点、Tab 和四窗格
仍待 GUI 验收，尚未发布，详见[命令状态通知](native-navigation-probe.md#2026-10-21-命令状态通知)。

持续维护 [Q-Dir / Explorer 核心操作对照](shell-parity.md)，以原版 Explorer 的 Shell 行为为最终基准，保留用户已指定的 Q-Dir 多窗格、标签和 Backspace 上级目录习惯。

1. 持续 P0：文件操作后焦点、背景/条目菜单、跨窗格复制移动拖放、每标签历史与会话恢复正确性。用户反馈的文件列表导入后黑底、重启白底作为颜色一致性缺陷跟踪，根因未定；[颜色调查](shell-color-investigation.md)已确认导入数据路径不传颜色，隐藏宿主为 DirectUI，仍需区分导入对话框与视图恢复的真实窗口实验；不留到个性化阶段才处理。
2. 第一阶段：补齐菜单栏、工具栏、导航、文件视图、状态栏功能。Ctrl+滚轮查看方式、常用 Shell 文件命令入口及总数/选中数、容量、属性与递归目录汇总已接入源码并有 smoke/隔离探针覆盖；物理输入路由、菜单可用状态、状态栏布局和真实 Shell 更新仍待可见验收。继续按 [Shell 对照](shell-parity.md)补导航与其它差异；同一命令的菜单、工具栏、快捷键共用 Shell 行为和可用状态。
3. 第二阶段：按原版 Explorer 对齐组件体验，保留 Q-Dir 多窗格习惯。覆盖焦点、正反 Tab、鼠标命中、溢出滚动、菜单勾选、禁用状态、窗口缩放及不同 DPI；正确性问题随发现随修，不等此阶段开始。
4. 第三阶段：主题与应用自有图标。基础“跟随系统 / 浅色 / 深色”已由独立 `Theme.ini`、WPF 动态系统色资源和 `UISettings.ColorValuesChanged` 接入；下一步读取 Windows 强调色，以语义色板驱动活动窗格/标签/焦点/选择状态，随后统一布局、查看方式、导航和命令图标。首期不做自定义色选择器；显式 Light/Dark 仍跟随系统强调色，高对比度优先交给 Windows。Q-Dir 整体主题预设不是已知 `.qdr` 色板格式；逐文件颜色规则（扩展名、隐藏/只读/目录、`#BG`/`#BG-A`）独立评估，不与应用主题混为一项。文件区黑底是独立未定位缺陷，ExplorerBrowser 原生文件区不会被 WPF 调色板强制变色。详见[主题、颜色兼容与图标设计](superpowers/specs/2026-10-26-theme-and-icons-design.md)。
5. 工作区收藏、过滤、树/预览及虚拟位置继续保留在功能清单，按核心功能验收结果拆批；其中 Q-Dir 快速过滤是每视图通配符包含/排除，与 Shell Search Folder 搜索分开，先验证 ExplorerBrowser 原生枚举过滤能力再定实现，详见 [Shell 对照](shell-parity.md)。系统接管仍以后续可逆验收为前提。

### 下一批范围与验收（分项记录实现进度）

| 部件 | 下一步 | 验收边界 |
| --- | --- | --- |
| 文件视图 | Ctrl+滚轮已自定义循环八种查看方式；Shell 子视图的 Ctrl+滚轮由本程序消费，避免同时改变图标尺寸 | 隐藏生产宿主合成消息完成八种模式循环并保持三项选区，其他窗格不变；物理鼠标路由、可见效果、焦点、排序/分组和普通滚轮仍待 GUI 验收 |
| 菜单、工具栏 | 剪切、复制、粘贴、删除、重命名、属性、撤销的统一入口及 Shell 可用状态已接入；刷新当前目录工具栏与 F5/Ctrl+R 共用动作；隐藏四窗格宿主刷新第四窗格时单选和 3 项多选的选区/焦点均保留，见[刷新复验](live-update-investigation.md#2026-10-24-活动窗格显式刷新选区焦点复验) | 可见工具栏/菜单操作、其他窗格目标、禁用状态变化和焦点返回仍待 GUI 验收；地址编辑快捷键不误执行文件命令；布局仍只在顶部 |
| 状态栏 | 总项目数/选中数、查看方式菜单、目录所在卷可用容量及其百分比、选择集可取得大小已接入；直接 Shell 属性探针在本地 1,000 项的读取为约 28 ms；大小聚合按视图/选择缓存，静态选区每 5 秒最多重读一次；F9 后台递归目录汇总按批次显示已检查条目数 | 未知/读取中/失败不显示为零；缓存节流的四窗格可见响应、慢位置、大选区及可见显示仍待验收，过期结果不覆盖新目录；F9 可见进度和大目录响应仍待 GUI 验收 |
| 导航 | 前进/后退原生状态与每标签按钮已接入；继续核查上级、面包屑、完整路径和搜索的可用状态、错误反馈及键盘行为 | 每标签历史独立，切布局/标签不丢历史；禁用按钮后的焦点和 Tab；失败后路径、状态与焦点一致 |
| 颜色一致性 | 复现同一目录在导入、新建、复制、重启恢复时的文件区颜色 | 用户确认只有文件列表区域变黑；源码确认 System/Light/Dark 当前仅更新 WPF `Application.Resources`，没有接入 ExplorerBrowser 文件区；分别检查 Shell 内容与 WPF 外壳，不把尚未导入的 Q-Dir 颜色当作已知根因 |
| `.qdr` 状态导出与兼容 | 十二种布局、活动窗格、完整列快照及四宫格独立比例已接入；继续 Q-Dir 实际 GUI 验收 | Windows 属性直读及无扩展转换测试支持当前输出；Q-Dir 实际保存/读取（含大视图流）、活动窗格焦点与分隔线外观未验收，不能称为全兼容 |

2026-10-24 修复监视器启动覆盖：现在先启用目录 watcher，再等 5 个稳定 Shell 计数；启动期事件留在合并队列，基线建立时交给正式批次并强制刷新。40 次快速采样后降为每秒继续等待，不再放弃 watcher。SettingsSmoke 覆盖稳定样本、无效样本重置、慢枚举降频和最终基线；全量 Release SettingsSmoke、App Release 构建及改动文件空白检查通过。未启动 ExplorerBrowser/HostProbe；启动队列与 Shell 通知的真实交互、慢速/网络位置和可见刷新选区焦点仍待隔离验收。详见[目录变化监视器启动窗口](live-update-investigation.md#2026-10-24-目录变化监视器启动窗口源码复核)及其后续记录。

2026-10-20 补充：`.qdr` 导出属于第一阶段功能补齐。兼容方向以“我们尽量兼容 Q-Dir”为主；自己的个性配置允许通过扩展保存。布局、各窗格标签顺序及当前标签优先，查看/排序/分组/列设置随后按已确认字段补齐。范围与验收见[Q-Dir 状态文件导入与导出](qdr-import.md#状态导出与兼容方向2026-10-20-新增需求)。

详细方案与颜色验收见[设计补充](superpowers/specs/2026-10-15-explorer-dirs-design.md#2026-10-20-功能对齐与主题方案)。

2026-10-24 Q-Dir 颜色过滤核对：官方 FAQ 现已确认规则语法包括扩展名通配符/分号多类型、隐藏项 `#`、目录 `#DIR`、只读 `#RO`，视图/活动窗格背景 `#BG`/`#BG-A`，以及控制后续规则能否覆盖的 `-1/0/1` 标志；FAQ 47 还说明颜色设置可保存进收藏 `.qdr`。当前导入器明确不导入颜色规则和背景值，这是 Q-Dir 互操作差距。ExplorerBrowser 的默认视图未提供已确认的逐行着色入口，暂不以图标覆盖冒称行着色，也不改用自定义 Shell 视图；需要先验证能够保留原生选择、排序、菜单、拖放的实现方案。这个能力差异与“导入后文件区临时黑底”分开跟踪。详细技术边界见[Shell 对照](shell-parity.md#q-dir-颜色高亮技术边界复核)及[Q-Dir 导入范围](qdr-import.md#明确未导入)。

2026-10-20 继续实现：

- [x] 2026-10-21 `.qdr` 基础块补齐现有六种布局，按实际可见位置重排窗格及选中标签，导出反向转换且保留隐藏窗格。依据本机 12.79 静态几何分析，未按菜单文字猜编号；独立原生字段输入、基础字段输出、无扩展及完整扩展往返通过。Q-Dir GUI 互操作仍待验收，见[布局映射](qdr-layout-mapping.md)。
- [x] 2026-10-21 查看方式原生测试发现“小图标”2/-1 返回成功却保持原图标大小；预设、新标签默认和旧默认状态应用改为显式 16。八种预设在有/无分组下保留两个选中项目身份及排序/分组，无新导航；隐藏测试通过。Ctrl+滚轮真实输入路由和 GUI 仍待验收，见[查看方式调查](view-style-wheel-investigation.md)。
- [x] 2026-10-21 独立原生目录树已接入源码：顶层显示开关、各窗格侧栏、NavigationTree.ini、可取消的 Dispatcher 目录同步及 Tab/F6 停靠点。默认关闭，关闭不重建文件视图。隐藏测试覆盖展开/定位、取消、根替换、线程和释放；配置回归通过。真实用户导航、焦点、重启和多窗格布局仍待 GUI 验收，尚未发布，见[原生目录树调查](native-panes-investigation.md)。
- [x] 目录树动态开关的同目录导航方案已排除：隐藏宿主实测既不重新查询树偏好，又增加重复历史；一次后退仍停在同目录，第二次才返回原目录。视图 HWND/模式/选中数保持不能弥补这两项失败。当时转向独立原生树宿主；该方案现已接入源码，进度见上一条，见[实验](native-panes-investigation.md#同目录重新导航实验2026-10-20)。
- [x] 历史服务查询补查当前 Shell 视图、视图站点和顶层浏览器：provider 获取成功，travel 查询均 E_NOTIMPL；同次后退/前进实际位置验证通过。排除了仅查询 ExplorerBrowser 本体造成的入口遗漏；随后改用 CommandStateChange 接入按钮状态，不将服务失败当作空历史，见[入口记录](native-navigation-probe.md#历史服务入口补查2026-10-20)。
- [x] 收藏夹新增“保存当前工作区…”和“追加已保存工作区”，复用 `.qdr` 完整快照及导入预览/备份/追加。当前 profile 旁 `Workspaces` 目录独立存储；菜单后台只列顶层文件名、最多 200 项，点击时重新读取，外部更改不会被列表缓存掩盖。保存到其他目录明确提示不会出现在工作区收藏中。目录隔离、隐藏窗格快照、外部修改、损坏文件和数量边界检查及构建通过；真实菜单、取消/覆盖、追加和重启待 GUI 验收，见[工作区收藏](workspace-favorites.md)。
- [x] 导航回调发布保护：旧回调只清理计数，不更新地址/容量/错误；排队的视图恢复和焦点使用访问版本，避免同路径重访误用旧动作。重复/未知目标批次读取实际 Shell 位置，失败时保留缓存并明确通知，错误排在位置通知之后。状态回归、生产位置读取器的隐藏原生 PIDL 对比、构建与源码复核通过；快速导航及失败提示 GUI 待验收，见[导航发布规则](navigation-publication.md)。
- [x] 选择集大小通过原生 IShellItemArray 的快速属性聚合接入，明确显示“可取得大小”；VT_EMPTY 与有效 0 字节分开，数组数量/选择版本/导航代际改变时拒绝旧统计。真实属性存储验证单文件、多文件、空文件、文件夹和混选；隐藏原生视图实际选区验证 3 项/8 字节，不递归计入文件夹中的 7 字节文件。App 可见多选、大选区、网络缓存与排版待验收，见[选择大小记录](selection-size.md)。
- [x] 状态栏容量由 GetDiskFreeSpaceExW 后台读取当前用户的可用/总容量，保留完整目录路径以支持挂载点语义；可见窗格每 10 秒刷新、全进程最多两个在途查询、排队与等待合计 3 秒超时。取消等待不释放仍在运行的原生调用名额，避免离线路径堆积线程。导航开始/切标签/隐藏立即清空并取消发布旧结果；虚拟视图不显示。过期结果、隐藏、排队超时、容量 64 位/零可用、失败、本地磁盘实读及读取器独立取消后的不可用状态结算回归通过；真实网络盘/挂载点与窗口排版待验收。
- [x] 导航读就绪状态改由 ShellViewReadiness 统一管理：匹配当前目标的失败结束 pending，并使读取快照失效；旧目标的成功/失败不解除新导航。同一目标多次 pending 在匹配回调全部结束前保守等待，销毁后迟到回调不重新开放读取。失败/旧回调/重复目标/重访/未知身份回归先红后绿；隐藏原生宿主验证正常历史回调可结束 pending，快速追加请求实际返回 BUSY。真实失效路径、网络失败后的 App 统计/命令/焦点恢复待验收；无法关联的回调不猜测为当前请求。
- [x] 新增显式启用的原生导航能力探测：隐藏宿主实际 PIDL 验证目录/子目录/后退/前进；本机历史查询服务在四个阶段均返回 E_NOTIMPL，因此未将其误接为按钮禁用依据。随后通过 CommandStateChange 完成源码接入。探测不显示窗口、不注入输入，不代表 App 的四窗格和焦点验收，见[探测记录](native-navigation-probe.md)。
- [x] 每窗格状态栏新增“查看”入口，与顶部当前标签菜单共用八种模式、快捷键提示和实际模式勾选。展开时读取 Shell 当前模式/大小，导航未完成、读取失败或原生重命名中禁用选项；非预设图标大小不猜测勾选相近预设。入口作为每个窗格文件视图之后的 Tab 停靠点，并纳入 F6 区域遍历；真实菜单退出焦点、四窗格目标及窄窗格排版待 GUI 验收。
- [x] `.qdr` 导出保存全部窗格/标签及版本化扩展，恢复完整视图、每标签快速筛选、固定标签、活动窗格与新标签默认方式。基础字段外部修改使旧扩展失效；覆盖前比较目标内容。导入固定标签按对象选择而非偏移索引，导入前追加完整 `.bak.qdr` 备份。六种本程序布局、100 标签边界、损坏扩展、编码、冲突保护无窗口验证通过；真实 UI 与 Q-Dir 打开导出文件未验收，其他 Q-Dir 原生布局仍未完成；原生视图包装已按本文开头范围接入，实际互操作待验收。
- [x] “编辑”菜单与工具栏新增九种常用文件命令，共用 canonical verb 查询和原生菜单启用状态；粘贴、撤销、重做使用当前视图背景，其他命令使用选区。菜单展开或进入工具栏时查询，不用计时器反复构造 Shell 扩展菜单；执行时重新查询并检查导航/选区版本和活动窗格。SettingsSmoke 覆盖 Redo 只查背景菜单、禁用时拒绝执行、启用时调用 native verb，以及窗格/导航上下文过期时取消执行；Release 隐藏 IShellView 在无撤销历史的新夹具中将 Undo/Redo 均报告为 disabled，前台未变。真实撤销后状态变化、实际 Undo/Redo/重命名/确认框和焦点仍待 GUI 验收。
- [x] 状态栏从当前 Shell 视图读取总项目数和选中数，每 750ms 更新可见窗格；隐藏/卸载停止轮询，切标签清空旧统计，导航期间不可用，导航代际变化拒绝旧结果。独立于错误/搜索提示，不枚举目录。空目录、失败 HRESULT、S_FALSE、不一致计数和选择变化的无窗口检查通过；真实选择响应、搜索结果及四窗格刷新仍待 GUI 验收。选中可取得大小、容量与查看入口见本轮追加项。
- [x] 在既有 `EXPLORER_DIRS_FOCUS_TRACE` 开关下增加 Ctrl+滚轮消息层级与处理后模式/图标大小记录；默认关闭，不消费或转发消息。此项仅供定位，不是 Ctrl+滚轮修复。见[差距表的诊断说明](shell-parity.md#ctrl滚轮诊断)。
- [x] Ctrl+滚轮按用户要求自定义为八种查看方式循环：由实际命中并已挂钩的 Shell 视图子窗口处理，消费 Ctrl+滚轮以避免 Shell 原生缩放叠加；活动标签读取当前模式并经既有状态保存路径切换。现将高分辨率滚轮发出的子刻度按每标签累计至 120 后再切换，避免每个小增量都跳一个视图。纯逻辑覆盖双向部分刻度、反向抵消、独立标签及多格增量，应用构建通过；物理鼠标路由、可见效果、焦点、排序/分组及普通滚轮仍待 GUI 验收，未发布。

- [x] 当前标签通过 Ctrl+Shift+1～8 与顶部“视图 → 当前标签查看方式”调用原生 Shell 切换查看方式；不重置排序/分组，不改变新标签默认方式。构建与无窗口回归通过，真实图标大小、选择/焦点及 INI 重启恢复待验收。
- [x] 每标签显示列集合、列宽和列顺序已通过 IColumnManager 接入 INI v2；复制标签复制列快照，新标签使用 Shell 默认列，首次从 v1 保存前备份原文。损坏列数据保留可读标签，失败读取/恢复不覆盖完整快照。配置、迁移、失败路径及无窗口 COM 边界回归通过，真实列拖动、切换标签/目录/查看方式与重启恢复待 GUI 验收，见[列设置设计与验收](column-layout.md)。

## M0：可运行浏览原型

- [x] 建立 Git 仓库与 MIT 开源许可证。
- [x] 单窗格、左右/上下双窗格、两种三窗格、四窗格；独立标签、地址栏和目录列表。
- [x] 顶部菜单和快捷布局图标；布局不占用右键菜单；标签菜单中的新建、复制、关闭其他、恢复关闭；中键与双击标签标题关闭。
- [x] 每个标签承载 Windows ExplorerBrowser 视图，默认详细信息；文件项/背景菜单由 Windows Shell 原生提供查看、排序、分组、新建、属性等命令，产品不向文件区菜单插入自定义项。
- [x] 文件夹和文件图标及文件关联由 Shell 视图提供；目录枚举由 Shell 负责。
- [x] 便携 INI 保存布局、默认新建标签查看方式与每个标签的路径/查看/排序/分组；复制标签复制实时视图设置；旧 JSON 路径和布局单向迁移。
- [x] 会话 INI 另存当前活动窗格和每窗格按显示顺序选中的标签；切换标签/窗格触发延迟保存。旧 INI 缺少字段时仍完整加载并默认首个，损坏索引先备份再恢复可读标签。无窗口配置回归和构建通过，真实关闭重启后选择与焦点待 GUI 验收。
- [x] WPF 宿主将键盘加速消息转给原生 Shell 视图；Ctrl+A/C/V 等操作是否完整工作仍待实际窗口验收。
- [x] 每标签使用 Shell 内置 TravelLog 后退/前进；Backspace、Alt+↑ 上级目录；Alt+←/→ 历史；Tab / Shift+Tab 遍历每窗格导航按钮、地址、标签标题、详细信息列标题和文件项，再循环可见窗格；Ctrl+Tab 循环标签；布局切换恢复上次活动窗格的文件视图焦点。F5 与 Ctrl+R 刷新活动窗格。旧预览版单窗格正反向 GUI 测试通过；持久内容层改动后需复测，四窗格待用户验收。
- [x] 焦点定位到 Shell 内容 HWND 和文件项；剪贴板快捷键后、确认框返回时按活动窗格恢复焦点，并处理 Shell 操作后没有前台窗口的短暂状态。Ctrl+Insert/Shift+Insert 与 Ctrl+C/V 共用 Shell 命令兜底和焦点恢复路径；`preview-0.5.2` 隔离测试验证 Ctrl+C/V 后方向键可用；新组合及 Shift+Delete 待 GUI 复测。
- [x] 多选焦点保护：恢复 Shell 项目焦点后，仅当 Shell 报告恰好一个选中项时才用 UI Automation 聚焦选中元素；多选时不把焦点跳到可视树里第一个选中项。源码构建通过，多选键盘操作仍待真实窗口验收。
- [x] 新建文件夹接入 Shell 背景菜单命令，并提供文件菜单、工具栏入口；Ctrl+Shift+N 与其他程序全局热键冲突时可不用该组合键。新建后的自动重命名和不同目录行为待 GUI 复测。
- [x] 2026-10-23 将新建文件夹纳入统一的八种文件命令状态查询；文件/编辑菜单和工具栏读取原生启用状态，仅初始化 canonical New 延迟子菜单，保留父菜单/子项禁用状态。执行时复查原上下文，继续使用原有观察器与延迟重命名保护。模拟回归及真实隐藏 Shell 状态探测通过；真实创建、自动重命名及四窗格焦点仍待 GUI 验收。
- [x] 新建文件夹的自动重命名不再前后同步枚举当前目录所有子目录：固定本地盘使用短时目录观察并要求唯一候选与 Shell 焦点一致；网络/非固定盘直接执行 Shell 命令，不增加观察器。观察器失败或出现歧义时放弃自动重命名。临时目录回归与构建通过，大目录、网络盘和真实自动重命名待 GUI 验收。
- [x] 接收环境变量变更通知、手动刷新、从当前目录启动命令行。
- [x] 源码调试会话与发布版会话隔离；版本化发布目录和项目规则。发布脚本为每次构建使用唯一暂存路径，并在目标版本目录出现时拒绝最终目录移动，避免把暂存目录嵌入已有版本；发布新版本仍需真实 GUI 启动验证。
- [ ] GUI 手工验收：Ctrl+A/C/V、空白处与条目原生右键、查看/排序/分组切换和重启恢复、复制标签保留配置、中键/双击关闭、多目录、无权限目录、网络盘、环境变量热刷新。右键重点覆盖先选中文件再在空白处右键、文件夹/文件条目、多选、Shift+F10、菜单键及 Windows 11 Shift+右键“显示更多选项”，确认系统原生菜单类型与主题一致。temp.11 的逐项操作顺序与哈希记录见[桌面验收清单](gui-acceptance-temp.11.md)。

## M1：日常文件管理

- [x] Q-Dir FAQ 155 的大小显示格式：工具 → 选项可选自动/字节/KB/MB/GB/TB、0～3 位小数及千位分隔；全局设置保存至独立 `SizeDisplay.ini`，供选中项可取得大小、F9 递归目录汇总和磁盘容量共用格式化。原默认“自动单位、最多两位小数、不启用千位分隔”保持不变。SettingsSmoke 覆盖单位/精度/分隔组合、默认值、INI 往返、外部修改保护和高版本拒写；App Release 构建通过。可见菜单、窄窗格显示和本地化数字格式仍待 GUI 验收。F9 递归汇总的文件/子目录计数可单独开关，默认开启，配置与单位格式共存于 SizeDisplay.ini；设置烟测覆盖隐藏计数与保留不完整扫描提示。未做 GUI 验收。来源：[Q-Dir FAQ 155](https://www.softwareok.com/?faq=155&seite=faq-Q-DIR)。

- [x] Q-Dir 官方 FAQ 125 的网络驱动器连接/断开入口：在“工具 → 网络驱动器”中调用 Windows `WNetConnectionDialog` 与 `WNetDisconnectDialog`，由系统负责盘符、网络资源选择及凭据流程；普通系统错误和 `WNetGetLastError` 的提供程序扩展错误均有对应提示。SettingsSmoke 验证 owner HWND、原生结果码及扩展错误文本路径，没有建立或断开实际连接。菜单/系统对话框可见性、凭据和驱动器刷新待 GUI 验收。依据：[Q-Dir FAQ 125](https://www.softwareok.com/?faq=125&seite=faq-Q-DIR)、[WNetConnectionDialog](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetconnectiondialog)、[WNetDisconnectDialog](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetdisconnectdialog)、[WNetGetLastError](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetlasterrora)。

- [x] 2026-10-23 `.qdr` 原生 SP0/SP1/SP2 比例读写，扩展 v2/v3 保存完整已记忆比例、读取 v1/v2；修改原生字段使旧扩展失效。工作区收藏与导入前备份同步保存；同布局导入重新排列。独立字段预期、非法值、扩展回退及配置回归通过，Q-Dir 可见读取仍待验收，见[比例兼容](qdr-proportions.md)。
- [x] 四宫格支持上下两排独立左右比例，以完整表达原生 SP1/SP2。三个分隔条各自拖动；旧配置继承原共享比例。配置升级 v4，旧 v3 读者拒绝写入。连续拖动越过上下边界并返回的回归、取消、失效事件、resize 与几何检查通过。真实鼠标、焦点和 Q-Dir 互读仍待 GUI 验收。
- [x] 2026-10-23 新增左双右大、上双下大，并补齐三列/三行/四列/四行，共十二种布局支持 `.qdr` 编号/窗格/Focus 转换。条带布局保存三或四项权重，每条分隔线支持取消恢复；三/四行相应提高窗口最小高度，避免底部窗格截断。当前会话 v5、比例 v3 保存前备份旧版，修改前读者拒写实测通过。无窗口几何、十二种映射和配置回归通过；Q-Dir 可见互操作、拖动、焦点及重启显示仍待验收，分隔比例已按后续记录接入，仍有独立四宫格限制，见[布局证据](qdr-layout-mapping.md)。

- [x] 2026-10-23 编辑菜单补齐全选、取消选择、反向选择；基于 Shell 视图 PIDL 和原生批量选择接口，保留准备期间上下文检查及焦点项选中状态。三种模式、有/无分组、空目录、身份补集与取消检查通过；实际菜单/方向键、多窗格和大目录响应待 GUI 验收。反选的两个原生批次不是事务，详见[选择菜单验证](selection-commands.md)。

- [x] `.qdr` 导入源码入口：后台解析、数量/未支持项预览、确认后按窗格追加并备份当前会话。已验证本机 42/28 标签；已核对的视图包装支持排序、分组、图标大小和列设置，其他格式明确回退。详见 [Q-Dir 导入](qdr-import.md)。文件选择、实际追加及视图恢复待 GUI 验收。
- [x] 2026-10-18 历史验收记录：当时隔离副本已观察到真实 Shell 列配置写回，随后发现桌面锁屏并停止输入；该次未完成的操作不再作为当前待办或有效授权。当前预览的验收范围改由 [temp.11 桌面验收清单](gui-acceptance-temp.11.md)记录，实际可见行为仍待用户验收。

- [ ] 验收 Shell 视图提供的新建、重命名、复制、移动、删除到回收站、撤销、多选、剪贴板及文件属性行为；补齐跨窗格拖放。
- [ ] 测量第三方 Shell 扩展（Git、SVN、压缩软件）的阻塞/崩溃影响。2026-10-25 的[脱敏滚动基线](q-dir-crash-evidence-2026-10-16.md#2026-10-25-只读复查)包含 Q-Dir 21 个进程、42 条事件，以及 Explorer Dirs 一条旧源码测试版事件；两程序均无 WER 专用转储配置。旧 WER 报告中的已加载模块仍不能判定扩展责任；当前缺新调用栈、扩展阻塞测量及真实可见复现，隔离与恢复的[设计草案](reliability-isolation-design.md)保持未验证。
- [x] 只读崩溃摘要脚本：查询 Windows `Application Error` 1000 事件，区分项目内发布版、源码测试版和其他副本；仅输出时间、故障模块文件名及异常码，异常退出标记另计。合成事件脱敏/归类回归通过；尚不能据此归因 Shell 扩展或计算长期崩溃率。
- [x] 本机一次源码测试崩溃已形成[脱敏证据记录](crash-evidence-2026-10-16.md)：同步初次导航的旧构建早于当前延后导航的提交，真实新版窗口复测仍待空闲桌面。
- [x] 顶部收藏夹菜单可添加、移除当前目录并在当前窗格导航；独立便携 `Favorites.ini`，含损坏备份、高版本保护、外部修改检测和同版本写入串行化。控制台回归与构建通过，菜单及焦点行为待 GUI 验收。
- [ ] 导航历史与收藏夹的 GUI 验收。代码层工作区收藏已通过 `.qdr` 保存/追加布局、树模式及宽度、逐标签快速筛选、固定标签和查看状态；`QdrExportSmoke` 覆盖这些数据的本程序往返。该回归不证明收藏菜单交互、重启后可见恢复或 Q-Dir 实际读取；当前 Search Folder 查询按规则不作为持久化标签位置保存。
- [x] 原生树/预览能力调查（历史记录）：隐藏 ExplorerBrowser 接收 IExplorerPaneVisibility 回调；初始导航树开关有效，运行中偏好变化需导航才重新查询，SetOptions/Refresh 不即时更新。当时仅确认预览宿主存在，内容未验证；预览/详情随后已接入 App，当前可见渲染和窗格体验状态见本路线图顶部及[shell parity](shell-parity.md)。
- [x] 隐藏原生宿主取消导航已验证真实 OnNavigationFailed、pending 结束、原目录和选区属性可读，以及再次导航/后退恢复。调用返回 S_OK 不代表完成；此项不替代真实目录缺失、权限和网络失败验收，见[导航探测](native-navigation-probe.md#宿主取消与真实失败通知)。
- [x] F4 按微软当前 File Explorer 专用快捷键条目选中完整地址编辑框，与 Alt+D/Ctrl+L 共用入口；控制台映射回归通过，实际焦点待 GUI 验收。
- [x] F6/Shift+F6 在每个可见窗格的地址、标签标题、可用的详细信息列标题和文件区间正反向切换，跳过窗格小按钮；按键映射、单/多窗格顺序和构建通过，真实 Shell 焦点待 GUI 验收。
- [x] Ctrl+1～9 按当前窗格的标题显示顺序选中对应标签，超过标签数不改变选择；无窗口映射回归与构建通过，重排后和多窗格实际跳转待 GUI 验收。
- [x] 当前目录搜索的 Shell 基础：按范围与查询生成 `search-ms:` 虚拟 PIDL；ExplorerBrowser 站点提供 `ICommDlgBrowser2` 的 `CDB2GVF_NOINCLUDEITEM`。无窗口 PIDL、COM vtable、站点往返和构建通过；真实视图是否取用该标志待 GUI 验收。
- [x] F3/Ctrl+E/Ctrl+F 当前目录搜索已接入原生 Search Folder 导航、窗格搜索栏、搜索按钮、菜单和工具栏；真实目录路径与虚拟搜索位置分离，会话只保存目录。Esc/关闭按钮返回目录时会在本窗格仍活动的条件下恢复文件区焦点。构建、URI/PIDL 与状态、快捷键及动态 Tab 顺序无窗口回归通过；结果、菜单、焦点、TravelLog 和非索引/网络目录的真实窗口验收尚未完成，依据见[搜索设计调查](search-research.md)。
- [x] Q-Dir Alt+F3 所选文件夹搜索范围：保留 F3/Ctrl+F 当前目录范围；Alt+F3 仅在文件视图聚焦且恰有一个普通文件系统文件夹选中时打开搜索栏，并把选中目录传给现有 Shell Search Folder。多选、非文件夹与虚拟位置给出提示，不复用旧范围；搜索栏显示所选目录名/完整路径提示，关闭搜索回到原标签目录。快捷键和 URI 回归已接入。
- [ ] Alt+F3 真实键盘、可见搜索结果、焦点和网络/权限目录行为待桌面验收。来源：[Q-Dir FAQ 37](https://www.softwareok.com/?faq=37&seite=faq-Q-DIR)。
- [x] Shell 虚拟位置引入后，按目录搜索先前仍会把 `shell:` 身份传给只接受绝对磁盘路径的 URI 构造器，异常被呈现为内部参数错误。状态回归先因缺少搜索资格状态而失败；现在在搜索流程入口拒绝非文件系统位置，显示明确提示且不创建 `search-ms:` 请求。SettingsSmoke 通过；虚拟文件夹原生搜索仍是后续独立功能，未宣称支持。
- [x] 2026-10-24 Q-Dir 快速筛选接入原生 `IFolderFilterSite`，支持通配符包含/排除及筛选文件夹开关；默认不对文件夹套用筛选，等价于 Q-Dir 的“忽略目录”。每标签状态保存于会话 INI v6，旧 v1～v5 默认空筛选，新增 Ctrl+M 筛选栏、Enter 应用、F5 应用并刷新、Esc 清除关闭。隐藏宿主验证筛选命中、四窗格隔离及搜索结果往返恢复；SessionSmoke 覆盖 v6 往返、旧版默认值、v5 升级备份及旧读者保护。真实 GUI/键盘、非文件系统命名空间与刷新性能仍待验收；不宣称完整 Q-Dir 兼容。
- [x] Q-Dir 式筛选预设收藏：独立 `QuickFilterFavorites.ini` 保存名称、模式、包含/排除和文件夹开关，提供可编辑名称、选择载入、保存覆盖和删除；选择预设只载入编辑栏，点击筛选/Enter 才作用于当前标签。配置回归覆盖往返、同名替换、删除、损坏备份、高版本保护、名称注入、数量上限和外部修改保护；可见菜单/键盘验收待桌面空闲后补。
- [x] 标签标题保持单行；溢出时可用左右箭头或鼠标滚轮横向滚动，切换标签后请求将选中标题滚入可见区。XAML 构建通过，实际滚动与焦点需 GUI 验收。
- [x] 同窗格标签拖动排序：标题只改变排版顺序，Shell 视图对象不从 TabControl 中移除；Ctrl+Tab、标题方向键及会话快照跟随显示顺序。排序与无窗口 WPF 排版回归通过，真实拖动和重启恢复待 GUI 验收。
- [x] 标签切换不再通过 `TabControl.SelectedContent` 交换 ExplorerBrowser：已访问视图留在窗格的持久内容层，只切换可见性；布局切换时四个窗格始终留在主网格中。无窗口可视树回归与构建通过，原生 HWND 和各标签 TravelLog 是否跨切换保持待 GUI 验收。
- [x] 普通文件系统路径的面包屑：驱动器和 UNC 分享根目录作为可点击路径段；原完整地址编辑、Alt+D/Ctrl+L 和 Tab 停靠点保留。路径拆分回归与构建通过，鼠标导航、焦点和长路径滚动待 GUI 验收。
- [x] 顶部视图菜单切换紧凑布局，仅收紧 WPF 间距和控件尺寸；配置独立保存在 `UiPreferences.ini`，含损坏备份、高版本与外部修改保护。构建和配置回归通过，四窗格排版与焦点待 GUI 验收。
- [x] 十二种布局的可拖动分隔条、独立比例和恢复均分入口；`LayoutProportions.ini` v4 保护外部修改、高版本和损坏原文，四宫格额外保存下排比例。无窗口检查通过，真实拖动手感、原生窗口及重启恢复待验收。
- [x] 固定标签：每窗格固定标签排在左侧，复制标签继承固定状态；中键/双击/Ctrl+W 与“关闭其他标签”保留固定标签，取消固定后可关闭。元数据独立保存在 `PinnedTabs.ini`，读取时按窗格、位置及路径匹配，含损坏备份、高版本和外部修改保护。控制台回归与构建通过，真实窗口菜单、拖动、重启恢复待 GUI 验收。
- [ ] 关键文件操作自动化与人工恢复：Release 隐藏生产宿主使用真实 Shell `IFileOperation` 复制/删除隔离测试文件，磁盘身份与四窗格 Shell 计数（1→2→1）通过；一次隐藏实验显示 NewFolder 可由背景 Shell `Undo` 撤销，但重复运行未满足隔离性断言，不能当作稳定验收。SettingsSmoke 现在检查 Ctrl+Z 映射到 Shell Undo，但实际 HwndHost 键盘路由仍需在隔离可见窗口覆盖；删除到回收站、冲突与确认对话框及人工恢复演练也未完成。
- [x] 隔离 GUI 冒烟脚本改为显式授权后才启动，测试程序和配置放在 `src/.state/gui-smoke/`；布局、上级/后退和搜索控件通过 UI Automation 调用，检查唯一测试文件在初始目录及返回目录中出现、搜索结果项和返回目录完成，不发送全局按键、不移动鼠标、不改剪贴板，也不强制抢前台；每次调用前核对前台窗口所属进程。脚本还准备记录启动到窗口、首次文件项、目录往返与搜索结果的探索性耗时。代码构建、语法和防误启动检查通过，真实 GUI 执行与测量待用户留出空闲桌面。
- [ ] 在用户明确留出空闲桌面时运行隔离 GUI 冒烟；再进行文件操作、焦点和右键菜单的人工验收。

## M2：可靠性与性能

- [x] INI 格式版本与旧 JSON 一次性迁移；旧文件保留。
- [x] 损坏 INI 覆盖前保留副本；备份失败则停止覆盖。
- [x] 截断、缺失 `[General]`、TabCount 错误或 SortCount 小于现有排序列数时，先备份配置，再恢复可解析标签与排序；高版本配置禁止旧程序覆盖。控制台回归已通过，真实启动后的提示待 GUI 验收。
- [x] 会话 INI 的高版本保护覆盖重复 `FormatVersion` 的情况：即使后面的重复键写回版本 1，仍识别先前出现的较新版本并停止覆盖。回归先复现错误、修复后通过；真实窗口提示待验收。
- [x] 收藏夹、固定标签与外观 INI 的高版本保护也覆盖超出 32 位整数范围的版本号；即使后面重复写入版本 1，旧程序仍停止覆盖原文件。回归先复现外观配置错误，再覆盖三类配置并通过；真实窗口提示待验收。
- [x] 会话 INI 记住加载时内容；同版本实例写入按配置路径串行化，发现外部编辑或另一个实例的写入后拒绝覆盖。独立临时文件避免同名冲突；控制台双实例竞态回归通过，窗口状态栏提示待 GUI 验收。
- [x] 会话与收藏夹共用便携 INI 安全写入组件；收藏夹持锁拒写回归通过，原有会话双实例竞态与旧配置恢复回归保持通过。
- [x] 托管未处理异常和未观察任务异常写入 `profile/crash-logs`；报告先写入同目录 `.tmp`，成功后才改名为 `.json`，写入失败时尽力清理临时文件；启动时清理超过 1 天的本项目旧临时报告，保留较新的文件以免碰到正在写入的实例。即使进程被强制终止，临时名也不会被误认为正式报告。只记录异常类型、错误码与方法名，不写异常消息、文件路径或环境变量。工具菜单新增“打开崩溃诊断目录”，直接打开当前 profile 下的诊断文件夹；不启用 WER。Release SettingsSmoke 覆盖失败清理、陈旧文件回收、较新临时文件保留、聚合异常并列分支（最多 4 条）及隐私边界，App Release 构建通过；真实崩溃触发及菜单可见行为待 GUI 验收。
- [x] 每实例运行标记：正常退出清理自己的标记；下次启动识别已结束进程的残留标记并归档为 `abrupt-*.json`，提示尝试按保存会话恢复。同名归档已存在时保留旧报告，并用唯一文件名归档新标记，避免后续启动反复提示。报告仅含进程号、启动时间和版本；不判定故障原因。控制台覆盖异常结束模拟、另一存活实例、归档文件名冲突及清理，真实原生崩溃后的恢复提示待 GUI 验收。
- [ ] 原生 Shell 扩展崩溃的诊断与隔离；这类故障可能直接终止进程，托管异常日志不能覆盖。先按[可靠性设计](reliability-isolation-design.md)取得真实窗口基线和可分析的故障证据，再验证整窗恢复及必要时的窗格进程隔离。
- [x] 可选 WER 小型转储配置工具：`scripts/wer-dumps.ps1` 默认只读，针对单个程序提供预览、显式启用及只删除自有键的停用；最多保留两份转储，文件不入 Git。隔离 HKCU 注册表回归通过；真实 HKLM 配置、崩溃转储和调用栈均未执行或验证。
- [x] 恢复标签时保留离线或不可访问的原路径，不再同步探测所有保存路径；导航前目录探测移至后台，过期探测结果不覆盖新请求，刷新可重试。控制台竞态测试通过，GUI 行为待验收。
- [ ] Shell 绑定阶段的大目录、网络盘、重解析点、离线设备卡顿与目录变化通知。后台目录预检已改为区分目录、普通文件/缺失路径和权限不足：权限不足继续交给 Shell，UNC 共享根目录保留 `Directory.Exists` 兜底；控制台分类与竞态回归通过。生产目录 watcher 已允许 UNC 与映射网络盘；本机 SMB 管理共享 `\\localhost\D$` 上复制、删除、移动 Release 隐藏宿主 3/3 轮通过；目录 junction 指向路径的创建/删除 watcher 与 Shell 计数恢复也通过 3/3 轮；10,000 项创建/删除隐藏产品压力连续 3/3 轮最终恢复目录计数、选区和焦点，创建 9.4–10.0 秒、删除 6.9–13.4 秒，fallback 1–5 次且可见波动；另有一轮完整时间线显示创建 12.8 秒、删除 18.0 秒，删除 fallback 后 Shell 继续分批枚举约 19 秒。真实远程/慢速/离线网络、重连、其它重解析点和 UI 卡顿仍未测，Shell 实际权限提示及用户可见刷新需桌面验收。
- [x] 右键菜单前置的“在新标签页中打开”改由 Shell 选中项属性判断普通文件系统文件夹，不在菜单构建时额外同步探测 `Directory.Exists`；控制台属性分类回归和构建通过，真实右键菜单及网络目录响应待 GUI 验收。
- [x] 环境变量删除、`REG_EXPAND_SZ` 引用展开、用户与机器 PATH 组合的回归测试；从 Windows 环境注册表只读原始值，用新快照计算后仅更新本进程。新增 Release 隐藏 `MainWindow` 生产 hook 探针：仅对唯一测试 HWND 定向发送原生 `WM_SETTINGCHANGE`，当前用户环境注册表新增值后进程立即刷新，新开 `cmd.exe` 能读到；删除该注册表值并再次通知后，进程和后续 cmd 都看不到。测试断言父 HWND 始终不可见、不广播其他窗口，并在退出时通过嵌套 `finally` 清理注册表值、进程变量和隐藏 WPF 窗口，即使前一清理步骤失败也会继续执行后续清理。Release 探针、SettingsSmoke 和差异空白检查通过；普通桌面/发布版用户流程、系统广播及发布版 GUI 仍待验收。
- [x] 环境刷新计划在修改进程环境前校验全部名称和值；写入中途失败时尽力恢复本轮已改动的变量，回滚也失败则明确报错。无窗口回归覆盖中途失败与 32,767 字符超长值，真实通知与复杂用户环境仍待验收。
- [x] “在当前目录打开命令行”不再预先同步探测目录，进程启动移到后台；改用绝对系统路径定位 `cmd.exe`，并在窗口关闭时取消未启动的请求。后台、参数和取消的无窗口回归通过，真实慢速目录与环境继承仍待 GUI 验收。
- [ ] 性能指标：启动、目录切换、内存占用、长时间运行和崩溃率。静态检查发现当前便携配置有 9 个标签，旧 3 秒轮询会逐个查询所有标签的 Shell 查看/排序/分组状态；源码已改为隐藏前捕获、轮询仅查询可见标签。导航后实时视图不可用、Shell 读取失败、排序列数无效或列数据不完整时保留上次完整缓存，拒绝用旧缓存覆盖会话 INI，也不从失败捕获复制标签。视图捕获逻辑已拆出无窗口回归，覆盖四类读取失败、无效列数、列数据不完整及恢复成功。隔离 GUI 冒烟脚本已准备以测试进程启动和 UIA 文件项出现时间记录启动及一次上级/后退目录切换，结果保存在该次测试目录的 `performance.json`，不含浏览路径；尚未实际运行，单次数据也不能代表长期性能。优化的真实窗口响应和保存精度仍未验证。已有一次[Q-Dir 只读短时快照](q-dir-performance-snapshot-2026-10-16.md)，不能据此推断本程序性能。
- [x] 只读进程采样脚本按 PID 记录工作集、私有内存、CPU、句柄、线程与可执行文件哈希；每个样本增加相对首样本的内存变化量和私有内存变化速率，便于辨别同一进程是否持续增长。默认拒绝其他进程；隔离 PowerShell 进程 smoke 验证 JSON 字段和 PID 身份门控。Explorer Dirs 的真实长期内存曲线、启动和目录切换耗时仍待独立测量。

## M3：系统入口接管

- [x] 应用内入口管理只允许随包工具脚本齐全的发布目录使用；源码调试目录不显示可操作入口。状态/Preview 通过 JSON 与脚本交互，启用目标必须与当前运行 EXE 完全一致；用户需勾选已验证并在二次确认框检查 EXE 路径和 SHA-256。状态模型/普通与隔离包脚本定位 smoke 通过；可见 UI 与真实入口启用留待集中验收。
- [x] 可选的每用户文件夹/磁盘右键入口已有 `Status`、`Preview`、`Enable`、`Disable` 脚本；命令使用引号传递一个目录参数，不修改默认打开或 Win+E。启用要求新发布版的能力清单、准确 SHA-256 和显式 GUI 验收确认。新增 `scripts/folder-verb-smoke.ps1`，在唯一临时 HKCU 注册表路径验证安装、幂等重复启用、路径/命令引用、现存冲突不产生部分安装、外部替换命令后拒绝卸载并逐字保留，以及正常移除；现在也验证能力清单绑定 EXE 的精确 SHA-256，并拒绝错误哈希、未声明目录参数或含未知字段的清单。隔离注册表 smoke 通过；临时忽略调用方哈希的反事实版本先按预期失败。测试不触碰 `Software\Classes` 且不启动夹具 EXE。实际系统入口尚未启用，菜单显示与目标启动待 GUI 验收。
- [x] 发布脚本现在把入口管理脚本与模块放进 `tools/`；脚本在正常发布包和隔离预览包中默认解析本包的 EXE/能力清单，源码树运行仍需指定发布目标。`publish-smoke.ps1` 覆盖两种包布局和只读 Preview，隔离注册表 smoke 通过；真实菜单与目录启动仍待 GUI 验收。
- 2026-10-28 对 `preview-0.6.0-temp.77` 从包内运行只读 Preview：解析到该包的 `app/ExplorerDirs.App.exe`，目录参数声明为 True，manifest、`SHA256.txt` 与 EXE 哈希一致；`Directory` 与 `Drive` 入口均为 missing。没有启动预览程序或修改真实注册表，可见菜单与实际目录启动仍待集中验收。
- 2026-10-25 对 temp.29 执行只读 `folder-verb.ps1 -Mode Preview`：发布包能力清单和 EXE SHA-256 匹配，命令目标为该版本的 `ExplorerDirs.App.exe "%1"`；`Directory` 与 `Drive` 入口当前均为 missing。此结果仅确认可选入口的发布前置条件，未写系统注册表、未启用入口，也未验证可见菜单或文件夹启动。
- 2026-10-25 对 temp.54 重做只读 Preview：`DirectoryArgumentDeclared=True`，EXE SHA-256 为 `CC1C17D1635E5B122886F539B5313BEC84E25CB3AFB8CB66246E13F78DFC8D6D`，生成的命令为该发布 EXE 加引号目录参数；`Directory` 与 `Drive` 入口仍均为 missing。当前隔离注册表 smoke 再次通过。只验证了发布目标可用及注册/卸载保护，不代表已启用、菜单可见或目录实际启动。
- [x] 程序支持单个目录启动参数：恢复原会话后在第一窗格新建标签，解析本地、UNC 与相对路径时不做同步磁盘探测；无效或多个参数回退原会话并提示。控制台路径回归与构建通过，实际启动及离线目录提示待 GUI 验收。
- [x] 单目录启动请求已接入同配置窗口的命名管道：同用户和权限级别、每配置路径独立，确认第一窗格已接收新标签后启动进程退出；同时请求和接收窗口尚在初始化的控制台回归通过。接收失败时新窗口保留请求并提示可能的会话冲突；真实窗口激活、标签和保存行为待 GUI 验收。
- [x] 无参数的第二次启动通过命名管道激活现有窗口，不会另建窗口并写入同一份多窗口会话；若单实例锁不可用且两秒转发仍未确认，则停止启动新写入者。隔离管道回归验证激活和目录请求互不混淆；真实 Windows 前台激活、桌面启动行为待 GUI 验收。
- [x] 命名管道对已连接但未发送完整请求的客户端设置每段读取超时；超时后释放连接并继续接收下一次文件夹启动请求。客户端只把准确的 `1`/`0` 回执视为已接收/拒绝，未知字节归为不确定，避免把可能已处理的请求误判为明确拒绝。空头、不完整路径负载和异常回执的控制台回归通过，真实窗口启动链路仍待 GUI 验收。
- [x] 窗口接收回调一直等待时，路由器关闭会取消对它的等待并释放配置锁；控制台回归先复现旧路由无法重启，再验证修复。2026-10-28 为目录、Q-Dir 状态和激活请求增加 GUID 请求 ID：同一次启动的 150ms 初次转发与单实例锁重试共用 ID，接收端缓存最近 256 个完成结果，阻止迟到首请求与重试重复执行；不同启动仍可打开同一路径。新接收端继续接受旧请求格式，新发送端只有在旧接收端明确拒绝握手时才回退旧协议。协议隔离回归、完整 Release SettingsSmoke、App Release 构建及 `git diff --check` 通过；缓存仅在接收进程内，接收进程崩溃后的不确定结果不承诺跨进程恰好一次，可见启动窗口行为仍待 UI 验收。
- 2026-10-28 当前执行环境重跑 Release SettingsSmoke 时，命名管道握手 smoke 在 `NamedPipeClientStream.Connect` 收到 `UnauthorizedAccessException`，完整套件随后在同一 FolderOpenRouter 激活路由区域停止。独立同进程 NamedPipeServer/Client 对照在有无 `CurrentUserOnly` 时都返回 `Access denied`，说明当前执行环境本身无法完成本地 named-pipe 连接；这次结果不能用来判定产品路由器回归，也不据此改协议。需要在允许本机管道连接的普通 Windows 会话重跑该 smoke 后，再关闭此验证项。
- [ ] 在独立测试环境验证 Win+E 与文件夹打开路由。
- 2026-10-27 对照官方 [Q-Dir FAQ 81](https://www.softwareok.com/?faq=81&seite=faq-Q-DIR)：公开步骤建议用 QuickTextPaste 将 Win+E 映射到 Q-Dir 命令，未说明 Q-Dir 自身注册全局热键或替换文件夹关联。Explorer Dirs 的单目录启动参数和每用户文件夹菜单不是 Win+E 路由。考虑快捷键冲突，先设计并验证本程序自身可逆的启停/冲突恢复；不假设未文档化的注册表键或全局键盘钩子。
- 2026-10-28 官方 Windows API 复核补充：`RegisterHotKey` 的 Windows 键组合属于系统保留快捷键类别，重复注册通常失败；`WH_KEYBOARD_LL` 虽可拦截并吞掉按键，但依赖全局 hook 链与消息线程，处理超时后 Windows 7 及之后可能静默移除 hook。文件关联 API 处理文件类型/协议默认应用，并没有提供替换 Win+E Explorer 命令的通用接口。因此当前不把全局键盘钩子或默认关联当作安全的默认方案；若后续实现，只能作为用户显式启用、可撤销且具备冲突恢复的独立实验，并单独验证实际路由。[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)、[文件类型和 URI 关联模型](https://learn.microsoft.com/en-us/windows/compatibility/file-type-and-protocol-associations-model)
- [ ] 提供可逆的启用、禁用和故障恢复流程；不得依赖替换系统 `explorer.exe` 文件。
- [ ] 覆盖本地目录、UNC、快捷方式、文件选择对话框和其他程序唤起目录的行为。

## 可复现验收样例

1. 在四窗格各开 3 个标签；切换单窗格再切回四窗格；所有路径仍在；选中非首个窗格中的非首个标签并关闭重开，检查布局、路径、各窗格选中标签与活动窗格恢复。
1. 在任意标签上右键复制标签、关闭其他标签、恢复关闭标签；中键和双击标签标题关闭；顶部菜单与快捷布局图标执行相同布局切换，隐藏窗格的标签保持不变。
1. 从顶部收藏夹菜单保存当前目录，切换到另一窗格后从收藏夹打开，确认只改变当前窗格；移除后重启检查 `profile/Favorites.ini` 与列表。另在一个实例运行期间修改该 INI，确认旧实例不会覆盖外部修改。
1. 同窗格拖动首尾标签互换，确认标题显示顺序、Ctrl+Tab 和标题方向键一致；切换标签检查原标签的目录与后退历史仍在，关闭重开后顺序恢复。中键、双击关闭及横向溢出滚动也需复测；跨窗格拖动不应把标签移走。
1. 在四个窗格各访问两个目录形成后退历史，再切换标签和单/四窗格布局多次；回到每个标签后执行 Alt+←/→，确认历史仍在且 Shell 视图未重新初始化。关闭一个标签后仅该标签的原生视图释放，其他标签保持原历史。
1. 分别拖动左右、上下、四窗格及两种三窗格的分隔条；检查三窗格不会覆盖大窗格，各布局比例独立。缩小/放大窗口后比例保持，切布局及重启后恢复；“恢复当前布局均分”只重置当前布局。检查原生文件区无空白/遮挡、选中项和历史保持、Tab 顺序不增加分隔条；包含高 DPI 和最小尺寸。
1. 在驱动器与 UNC 目录检查面包屑，点击任一段只导航当前标签；长路径尾部可见并能用滚轮横向滚动。用 Alt+D、Ctrl+L、Tab 进入完整地址编辑，Esc 取消；键入不存在的目录后查看错误提示与焦点，再切换窗格确认旧窗格不会夺回焦点。
1. 在四窗格各开多个标签并切换“视图 → 紧凑布局”，确认 Shell 文件视图、选中项和焦点不丢失；窗口缩窄后检查导航按钮、面包屑与标签溢出，重启后紧凑选项保持。再关闭选项确认标准间距恢复。
1. 在同一窗格固定两个标签并复制其中一个，检查固定图标及左侧分组；拖动仅在各自组内重排。对固定标签尝试中键、双击、Ctrl+W 及“关闭其他标签”，确认不会误关。取消固定后关闭，重启检查剩余固定状态；用旧预览版修改会话后再打开新版，只有窗格、位置与路径仍一致的固定状态会恢复。
1. 在文件条目和目录空白处分别右键：由 Windows Shell 提供文件项或背景菜单；空白处可切换查看、排序、分组，且视图实际变化；文件/文件夹图标与属性由 Shell 提供。再先选中一个文件，不取消选中便在空白处右键，确认菜单仍按 Shell 的空白处行为呈现；文件区不应出现标签/路径自定义项。
1. 同一路径开两个标签，分别设为不同的查看、排序、分组；复制其中一个标签，检查新标签继承视图；关闭重开后各标签保持独立。工具 → 选项修改新标签默认查看方式，随后新建标签应使用新默认值。检查 `profile/ExplorerDirs.ini`，并确认旧 `session.json` 未被删除。
1. 两个相同版本实例同时打开同一配置后，在一个实例中改变布局并保存；另一个实例再次改变布局时应报告会话冲突，不能覆盖第一实例的 INI。外部手工编辑配置时也应如此；重启后读取磁盘上的较新配置。
1. 焦点在文件视图时使用 Ctrl+A、Ctrl+C、Ctrl+V；检查选择与复制粘贴结果。焦点在地址栏时 Ctrl+A/C/V 应由地址栏自身处理。
1. 四窗格从任一文件项按 Shift+Tab，焦点先到详细信息列标题、标签标题，然后逆序经过窗格按钮与地址；按 Tab 应逐站原路返回。文件项后下一次 Tab 进入下一窗格首个按钮；第四窗格后回到第一窗格。选中第四窗格后切单窗格再切回四窗格，焦点回到第四窗格文件视图。每个标签单独测试 Alt+←/→ 历史，Backspace / Alt+↑ 上级目录；地址栏中的 Backspace 仍编辑文本。
1. 在测试目录选择文件，按 Ctrl+C、Ctrl+V，确认文件列表仍接受方向键；再选择一个测试文件，按 Shift+Delete、在系统确认框按 Enter，确认文件列表仍可直接用方向键操作。点击地址栏或另一窗格后，确认旧窗格不会抢回焦点。
1. 在四窗格各放一个可丢弃的测试文件，逐窗格用 F2 开始重命名，再按 ←、↑、→、↓；确认编辑框与当前窗格焦点不退出。再试 Shift+←/→ 和 Ctrl+←/→，确认文本选取/光标移动仍由编辑框处理；Esc 取消后只检查文件名，Enter 提交后确认其它窗格选区不变。
1. 在测试目录多选几个不相邻文件，让焦点停在最后一个选中项；切到另一窗格再返回，确认原多选集合保持且方向键从原焦点项继续，而非跳到第一个选中项。再对测试副本执行复制粘贴并复查。
1. 在本地含大量子目录的测试目录，以及网络或可移动目录，分别用文件菜单和工具栏新建文件夹；确认命令没有因预先枚举目录而卡住。本地新目录若 Shell 焦点唯一指向它，应进入重命名；同时出现多个新目录时不得误重命名其他目录。此项仅在空闲桌面用测试目录执行。
2. 保持程序运行，新建用户变量 `EXPLORER_DIRS_SMOKE=updated` 并触发环境变更通知；打开内置命令行执行 `echo %EXPLORER_DIRS_SMOKE%`，应输出 `updated`。验收后删除此测试变量。
3. 浏览不可读或刚被删除的目录，窗格显示错误；其他窗格和标签仍能工作。
4. 从命令行以一个本地目录、UNC 目录及相对目录参数分别启动发布版，检查原会话仍在、第一窗格新增并选中目标标签；传入两个参数或非法路径时检查提示且原会话未被替换。离线 UNC 不应阻塞窗口初始化。此验收不得顺便修改 Win+E 或文件夹关联。
5. 在隔离测试环境启动发布版后强制结束该实例，再重新启动，确认状态栏提示上次未正常结束，保存的布局与标签仍可恢复，`profile/crash-logs/abrupt-*.json` 不含浏览路径。另开两个实例并正常退出其中一个，确认另一个存活实例不被误报；不要把残留标记视为某个 Shell 扩展的故障归因。

2026-10-24 精确 Shell `CREATE` 通知候选评估：基于此前 stale-view 手动通知 3/3 快速显示的结果，临时接入生产 watcher 并验证真实 `IFileOperation` 复制；9 轮中 4 轮约 99–113 ms 显示、1 轮约 1,051 ms、4 轮约 1.87–1.91 秒并触发原有兜底。结果未显示稳定收益，且通知发出后仍存在迟到样本，因此移除候选产品代码，保留原计数兜底与调查记录，不发布。P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-精确-create-通知候选评估)。

2026-10-24 自动刷新通知 A/B 与合并实现更新：确认视图仍旧后，显式文件 `CREATE` 通知在 12 轮中 9 轮于 93–200 ms 显示；无额外通知控制 12 轮均未在 250 ms 内显示（10 轮 540–561 ms 自然更新，2 轮 fallback）。逐文件同步发送曾令 3,000 项压力测试 Dispatcher 停顿 6.5 秒并丢失选区/焦点，现改为 STA 上 60 ms 静默合并，仅在 Shell 计数落后时发一条通知。合并后 3 轮压力测试均最终恢复正确计数并保留选区/焦点，Dispatcher 最大间隔 62–80 ms；其中 1 轮在操作完成后由单次 fallback 补齐，另 2 轮直接到达目标计数。计数确认只发 1 条通知；最新单文件复制轮仍于约 1.91 秒经 fallback 更新，说明通知并非保证即时生效。App/HostProbe 构建及 Settings smoke 通过。慢速/网络与可见窗口仍待验收，P0 不关闭、不发布。详见[自动刷新调查](live-update-investigation.md#2026-10-24-精确-create-通知控制组与批量合并接入)。

2026-10-24 选择保持探针复跑未形成有效样本：Shell 复制项未在 5 秒内自然显示，探针仍继续删除步骤；该轮选择保持且原生选择回调未变化，不作为问题反证。HostProbe Release 构建通过。下一步先修正探针失败时的短路/清理，再用有效自然复制基线复测；在能区分用户新输入与 Shell 自然清选之前，不改产品选区策略。详见[调查记录](live-update-investigation.md#2026-10-24-删除未选中项探针无效轮)。

修正探针失败路径后，Release 有效样本重现原生删除清空未受影响多选：复制视图已先收敛，删除未选中目标后剩余选区为空、焦点保留，Shell `CDBOSC_SELCHANGE` 增 2，fallback 为 0。下一步按“最近有效 PIDL 快照 + 用户输入代际”设计安全恢复门控，先测纯状态逻辑，再用隐藏生产宿主覆盖删除恢复和用户主动清选。详见[调查记录](live-update-investigation.md#2026-10-24-删除未选中项探针无效轮)。

2026-10-24 已实现 Shell 原生更新选区保护：缓存最近非空 PIDL 选区，键鼠/命令输入后先等输入结束再重采样；只有输入代际未变化且视图仍为空时，才恢复仍存在的旧选项。门控 smoke、隐藏自然删除/主动清选对照、显式 Refresh、完整产品监视器及 3,000 项压力测试通过；Dispatcher 最大间隔 80 ms。Release 构建 0 警告/错误。可见 Explorer、真实菜单/鼠标和其他 Windows 版本仍待验收；不发布。详见[调查记录](live-update-investigation.md#2026-10-24-shell-原生更新后的选区保护)。

2026-10-24 进一步复测选区门控：确认输入代际已经递增后立即执行隔离 `IFileOperation` 删除未选中项，原选择/焦点保持，磁盘与计数正确、fallback 为 0。另用 515 项全选和一个未选中删除目标验证大量 PIDL 恢复，515 项均保持；墙钟约 1.28 秒包含固定 500 ms settle，恢复本身耗时未单独测量。两项为隐藏宿主证据，不等同于真实键盘删除/菜单或 GUI 验收。详见[调查记录](live-update-investigation.md#2026-10-24-用户输入后删除与大多选复测)。

2026-10-24 大选区恢复优化：隐藏宿主从 2,051 项快照复现旧恢复路径 11,404 ms 超过 2,000 ms 门槛；改用 Shell 批量 `SelectAndPositionItems`，先确认一个 PIDL 存活并清除多余选择，再批量选择，焦点独立恢复，批量失败保留原身份校验回退。恢复耗时降至 21 ms，2,051 项身份全部保持。刷新三项多选、删除已选项、自然未选项删除、输入后删除、主动清选、Settings smoke 均通过；Release 构建 0 警告/错误。详见[调查记录](live-update-investigation.md#2026-10-24-大选区恢复的批量路径优化)。

2026-10-24 延后恢复焦点 PIDL：刷新后若选区恢复成功但焦点 PIDL 暂不可枚举，应用现在保留原快照并在 Shell 选择回调静默后每 100 ms 重试，最长 1.5 秒；等待期间暂停下一次计数刷新，导航或用户输入会取消。到期仍不可恢复时才聚焦幸存选中项。Release 10,000 项产品监视器在更新后的稳定条件下 3/3 轮通过，最终目录计数、两项选区及独立焦点正确；Settings smoke 和 0 警告/错误构建通过。仅为本机隐藏宿主验证，实际键鼠中断、可见窗口及慢速/网络盘仍待测，自动刷新 P0 保持开放，不发布。详见[延后焦点恢复记录](live-update-investigation.md#2026-10-24-延后重试-shell-焦点-pidl-恢复)。
2026-10-24 延后焦点恢复的真实输入取消回归：新增隐藏生产 Shell 探针，先排入旧选区/焦点快照的延后恢复，再通过原生窗口键盘消息走应用输入处理路径，并把用户选区和焦点改到 `third.txt`。连续 3/3 轮均确认输入代际已稳定、旧恢复任务已取消、选区与焦点仍为 `third.txt`；等待时间超过 1.5 秒恢复上限。HostProbe Release 构建 0 警告/错误，Settings smoke PASS。测试只覆盖本机隐藏 WPF/Shell 宿主与合成原生输入消息，不等同于真实物理键鼠或可见桌面验收；不发布，自动刷新 P0 保持开放。详见[输入取消集成记录](live-update-investigation.md#2026-10-24-延后焦点恢复的输入取消集成回归)。
同日将回归提升为真实刷新路径：先删除当前未选中焦点项，再调用产品 `Refresh()`，等待应用真实进入延迟焦点恢复后发送合成键盘消息并改选 `third.txt`。连续 3/3 轮在 Shell 计数收敛为 2、输入快照收敛及超过恢复窗口后，均保持选区与焦点为 `third.txt`。Settings smoke 与 Release 构建通过。只证明隐藏宿主消息路径，不替代可见 GUI/物理键鼠验收；既有对照探针因 `SHChangeNotifyRegister` 返回 0 未运行到断言，详见[调查记录](live-update-investigation.md#2026-10-24-延后焦点恢复的输入取消集成回归)。
再覆盖鼠标消息：同一产品刷新场景下发送隐藏视图的 `WM_LBUTTONDOWN`/`WM_LBUTTONUP`，并检查输入钩子收到消息；鼠标与键盘各连续 3/3 轮通过，焦点及选区均稳定保留在新用户目标。该测试使用合成 Win32 消息，不代表真实物理点击或可见 GUI 验收；详情见[鼠标输入取消记录](live-update-investigation.md#2026-10-24-延后焦点恢复的鼠标输入取消回归)。
补验延迟恢复取消后的 watcher 衔接：恢复期间让焦点文件删除及 8 个新文件创建进入 watcher，连续 3/3 轮确认 DELETE/CREATE 均收到、Shell 项数收敛到 10、待处理增量归零且监视器基准一致，同时用户焦点/选区未被覆盖。该验证覆盖隐藏宿主内产品 watcher 与 burst 合并队列，不代表网络盘或 GUI 长期运行；继续记录在同一[调查](live-update-investigation.md#2026-10-24-延后焦点恢复的鼠标输入取消回归)。
将 watcher burst 扩展至 10,000 项并连续 3/3 轮通过：删除事件与全部创建事件均收到，计数收敛为 10,002，队列清空；1.5 秒恢复窗口后用户焦点/选区仍保留，fallback 为 1/0/0。该本机隐藏单窗格压力验证补强了高负载目录变化与焦点恢复并发边界，但不关闭自动刷新 P0；可见窗口、网络盘和长期运行仍待验收，详见[记录](live-update-investigation.md#2026-10-24-延后焦点恢复的鼠标输入取消回归)。
2026-10-24 延后焦点恢复后的完整产品监视器复验：Release 隐藏宿主单文件复制 31 ms 完成、watcher 59 ms 收到、视图 120 ms 可见且无 fallback；四窗格创建 watcher 均为 3，跨窗格/同目录文件操作及文件夹创建删除均可见，选区和焦点恢复。结果仅证明本机隐藏宿主，不代替 GUI 或网络盘验收。详见[调查记录](live-update-investigation.md#2026-10-24-延后重试-shell-焦点-pidl-恢复)。

随后扩展为四窗格并发：焦点恢复目标窗格删除一个项目并新增 10,000 项，其余窗格各新增 1,000 项；Release 连续 3/3 轮的 Shell 计数、watcher 批次及 fallback 均按各自目录收敛，目标窗格 `third.txt` 及第二窗格 `second.txt` 的选区/焦点互不干扰。证据仅限隐藏宿主和本地盘，不关闭自动刷新 P0，详见[四窗格压力验证](live-update-investigation.md#2026-10-24-四窗格并发目录变化与焦点恢复压力验证)。

四窗格混合变化继续补验：在 10,000/1,000/1,000/1,000 并发 burst 中，第二窗格混入重命名和删除，第四窗格注入 `FileSystemWatcher.Error` 的 buffer-overflow 错误事件。Release 连续 3/3 轮强制刷新状态、计数、各 watcher 批次与两个窗格焦点/选区均收敛；错误事件为确定性回调注入，不等于真实 OS 缓冲区溢出。详见[混合变化调查](live-update-investigation.md#2026-10-24-四窗格混合变化与-watcher-强制刷新恢复)。

2026-10-24 真实 OS watcher 溢出探测在产品同等 16 KiB 缓冲区下确认：10,000 项产生 2 个真实 Error 回调，50,000 项产生 3 个。创建回调已在线程池侧合并，避免逐项投递 STA；设置 smoke 先失败后通过。发现并修复暂态空 Shell 视图把基线重置为 0 后 timer 停止的问题：接受空视图作为有界重试回退后，后台每 500 ms 复查一次，最多 180 次；真实溢出生产宿主 Release 3/3 轮均恢复 pane 3 `10003` 项及匹配基线、清空增量，焦点/选区保持。常规四窗格 watcher 控制组通过。50,000 项生产宿主写入超过 60 秒门限；此处不宣称通过。只覆盖本机隐藏宿主、本地盘与合成输入；网络盘、可见 UI、物理输入及长期运行仍待验收，自动刷新 P0 未关闭，不发布。详见[真实溢出调查](live-update-investigation.md#2026-10-24-真实-watcher-溢出与创建回调排队)。

2026-10-24 50,000 项隐藏产品宿主压力复测连续 2/2 轮通过：watcher 创建/删除均为 50,000/50,000，Shell 与磁盘计数最终回到 3，焦点/选区保持。创建耗时 22.2–24.0 秒、删除 12.4–16.3 秒；最大 Dispatcher 心跳间隔 343–506 ms，fallback 0–4 次。该证据只覆盖本机隐藏宿主和本地盘，响应间隔波动仍需调查；自动刷新 P0 保持开放，不发布。夹具上限及退出清理由本轮扩展。详见[50,000 项压力记录](live-update-investigation.md#2026-10-24-50000-项产品-watcher-压力复测)。

2026-10-24 50,000 项 Dispatcher 间隔来源 A/B：产品 watcher 组出现 1,473 ms 最大间隔，native-only 对照也出现 1,201 ms；两组探针回调最大工作约 77/79 ms，Shell `ItemCount` 最大均不足 1 ms，native-only 在创建/删除各 15 秒观察后仍未收敛。此证据表明长间隔不只发生在产品 watcher 路径，暂不能精确归因或贸然改变通知/刷新策略。自动刷新/大目录 P0 继续开放。详见[Dispatcher 压力对照](live-update-investigation.md#2026-10-24-50000-项-dispatcher-间隔来源对照)。

2026-10-24 精确创建通知回归修复：隐藏生产探针发现 `ShellCreateNotificationBatch.TakeForDispatcher()` 清空了路径，导致随后 60 ms 计时器只能读到空批次；新回归先在旧实现上失败，修复后通过。单文件 Shell 复制现发 1 次 `SHCNE_CREATE`（watcher 73 ms、发送约 123 ms、Shell expected/actual=4/3），完整四窗格文件操作监视器通过。但该轮仍在约 313 ms 触发 fallback、约 371 ms 完成，不能宣称通知带来速度提升；自动刷新 P0 保持开放。详见[通知路径调查](live-update-investigation.md#2026-10-24-精确-create-通知路径回归修复)。

精确创建通知路径修复后的重复样本：Release 隐藏宿主单文件复制 45 ms 完成，watcher 68 ms 收到，通知约 117 ms 发送，Shell 视图 133 ms 显示且创建未触发 fallback；随后完整跨窗格产品监视器通过。并列前一轮仍 fallback 的结果，通知路径已实际接通但 Shell 时序存在波动，计数兜底继续保留。详见[通知路径调查](live-update-investigation.md#2026-10-24-精确-create-通知路径回归修复)。

2026-10-24 创建通知路径修复后的 50,000 项产品压力复测：watcher 创建/删除均为 50,000/50,000，创建 30.8 秒期间合并发送 12 次 `SHCNE_CREATE`、没有创建 fallback；删除触发 2 次 fallback 后 Shell/磁盘计数回到 3，选区和独立焦点保持。最大 Dispatcher 心跳间隔 514 ms，响应风险仍在，不据单轮宣称性能改善；自动刷新 P0 保持开放。详见[50,000 项修复后复测](live-update-investigation.md#2026-10-24-创建通知路径修复后的-50000-项复测)。

2026-10-24 `.qdr` 原生视图流复验：Release NativeProbe 将导出流直接交给隐藏 `IShellView`，八种查看方式 × 有/无分组 16/16 通过，模式/图标大小/排序/分组/列状态读回一致；Settings smoke 与构建通过。探针增加只清理自身 GUID 夹具的安全退出清理。此结果证明 Windows 原生 Shell 流消费，不代表 Q-Dir 可见互操作验收。详见[导出验证](qdr-import.md#2026-10-24-原生视图流复验与探针清理)。

2026-10-24 焦点遍历纯算法回归扩展：SettingsSmoke 现对四个停靠点数量不同的窗格逐一验证 Tab/Shift+Tab 互逆、完整循环不跳过/重复停靠点，并验证 F6/Shift+F6 major 区域互逆。Release SettingsSmoke 通过。该探针只验证遍历算法，不验证 WPF/原生 Shell 控件的实际 Tab 顺序与焦点保留；多窗格 GUI 验收仍开放。

2026-10-24 实际停靠点探针安全检查：尝试在不可见 HwndSource 初始化四个生产 ExplorerBrowser，只读取焦点停靠点且不调用 Focus/SetFocus、不发键鼠消息；原生 Shell 初始化仍改变了系统前台窗口，探针立即按断言失败并完成清理。已撤销该探针，避免以后运行时再次影响桌面。纯算法回归和源码停靠点映射仍有效；真实 WPF/Shell 控件的 Tab/Shift+Tab 与可见四窗格焦点保持需在有明确桌面授权的验收中验证。

2026-10-24 续验源码状态：App、NativeProbe、HostProbe Release 构建均为 0 警告/错误，SettingsSmoke 通过；四窗格 Shell 重命名编辑器键路由探针 4/4 通过（方向键处理、左右移动文本光标、焦点保留、窗格隔离），生产 NativeShellTree 隐藏宿主探针通过（子目录/深层展开、取消、根替换、越界拒绝、线程约束与释放）。以上为本机隐藏宿主及算法证据，不替代物理键盘、可见多窗格 GUI、Q-Dir 互操作或长期性能验收。

2026-10-24 watcher 删除选区回归连续 4/4 轮通过：隔离子进程删除未选中文件后，生产 watcher 每轮收到 1 个删除事件，未发送 `SHCNE_UPDATEDIR`；fallback 诊断每次均为 `expected=3/actual=4/forced=false`，确认 Shell 保留旧项目计数后按正常 mismatch 兜底。最终计数收敛，另外两个已选文件与独立焦点仍保留。显式 `SHCNE_UPDATEDIR` 及删除前记录用户输入的对照通过，均无 fallback；SettingsSmoke 通过。结果只覆盖本机隐藏宿主，不代表可见窗口交互，自动刷新 P0 继续开放。详见[watcher 删除更新与选区保留](live-update-investigation.md#2026-10-24-watcher-删除更新与选区保留)。

2026-10-24 发布隔离检查发现并修复遗漏：`QuickFilterFavorites.ini` 未随其他便携配置复制到新建独立 profile，导致预览包会失去已保存的筛选预设。发布 smoke 在补充该配置的断言后先失败，脚本加入复制项后通过；现有 release 和 profile 未修改。详见[发布记录](../releases/README.md)。

2026-10-24 native-only 删除选区复测连续 3/3 有效轮通过：预置视图基线后确认 Shell 自然显示副本，再删除未选中副本；watcher 与 `SHCNE_UPDATEDIR` 均关闭，计数自然收敛且 fallback 为 0。Shell selection callback 短暂报告空选择后，现有恢复路径重新选中两项，焦点保留。一次复制未显示的无效准备轮在删除前中止。隐藏生产宿主结果表明当前源码已覆盖此场景，不代表可见 ExplorerBrowser GUI 验收；没有改产品逻辑。详见[native-only 删除选区复测](live-update-investigation.md#2026-10-24-native-only-删除选区恢复稳定复测)。

删除前输入控制补充 selection callback trace：用户输入代数为 `1/0` 时，空选择回调被生产门控跳过；随后 Shell 的显式目录重枚举保留原选择，未使用应用的抑制恢复路径。SettingsSmoke 输入代数门控通过。该项仍需真实键鼠窗口验收。

2026-10-24 原生预览宿主复验：新增隐藏探针在 `EBO_SHOWFRAMES` 下选中隔离文本文件；Preview Host 有 `WS_VISIBLE`，但祖先可见链为假且尺寸 `0×0`；导航树为 `154×449`。父窗口保持隐藏、前台不变。选中文件仍未解决隐藏预览的零尺寸限制，因此不能据此判断可见布局是否可用；尚未将原生窗格接入产品，待可见 GUI 评估后选定原生框架或独立 Preview Handler 路径。详见[原生窗格调查](native-panes-investigation.md#选中文件后的隐藏预览帧复验2026-10-24)。

2026-10-24 资源管理器目录树快捷键：按微软 Explorer 快捷键清单将 Ctrl+Shift+E 映射为当前窗格目录树的定位/展开请求；仅在树可见时消费按键，隐藏时继续交给 Shell。映射回归先失败后通过，Release SettingsSmoke 通过。实际树项目展开、可见焦点保持和四窗格输入仍待 GUI 验收。

2026-10-24 快速筛选续验：按实现计划再次运行 Release 隐藏生产宿主，包含/排除模式、目录过滤、清除、返回普通目录后恢复筛选及其他窗格不变连续通过 2/2；四宿主搜索导航探针 8/8 阶段通过。发现这两种探针退出后未清理各自的 GUID 夹具，现已接入退出清理；修复后两探针连续运行，夹具目录清单前后均为 764 个、无新增也无删除。仅验证本机隐藏 Shell 宿主与隔离夹具，快速筛选栏可见交互、实体键盘及跨重启状态仍待 GUI 验收。

2026-10-24 标签设置复制/新建隐藏复验：`PaneControl` 生产路径探针恢复源标签的图标视图 `(Mode=1, IconSize=48)`、名称降序、类型分组与三列宽度；捕获值与配置一致，复制标签再次读回所有字段一致。普通新标签及 Shift+Enter 打开的选中文件夹标签均使用默认详细信息视图；筛选状态同时确认恢复保留、新建为空、复制继承，选中文件时 Shift+Enter 未被应用消费。Release 构建无警告/错误，隔离窗口未显示、前台句柄未变、测试夹具清理完成。源恢复和复制仅由隐藏本机 Shell 宿主验证；可见 UI、真实键盘焦点与重启后应用状态仍待验收。

2026-10-24 Q-Dir 标签快捷操作补齐：按 Q-Dir 官方功能历史中“Selection in new tab”定义（在新标签打开选中的文件夹），接入 Shift+Enter；无单一文件系统文件夹选择时不消费按键。SettingsSmoke 先因缺少映射失败后通过；隐藏 PaneControl/Shell 生产路径验证选中子文件夹后产生新标签、路径正确且应用新标签默认筛选，同时选中文件时保持未处理。物理按键和可见标签行为仍待 GUI 验收。[Q-Dir 功能历史](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2FHistory)。

2026-10-24 Q-Dir 反向选择快捷键补齐：官方 Q-Dir 快捷键表将 Ctrl+Shift+A 标为“反向选择”；本程序已有原生 PIDL 反选实现，本轮补上文件视图快捷键路由并在菜单显示该键。SettingsSmoke 的缺失映射用例先编译失败，接线后 SettingsSmoke 与 App Release 构建通过（0 警告/错误）；NativeProbe 再次通过详细信息/列表/大图标有无分组共六组反选以及空视图检查。该原生探针没有触发按键到 MainWindow 的路由；物理快捷键、焦点和多窗格仍待 GUI 验收。[Q-Dir 快捷键表](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)。

2026-10-24 选择快捷键文本焦点保护：源码复核发现 Window PreviewKeyDown 也会收到文本框按键，新增保护使 Shift+Enter 与 Ctrl+Shift+A 在 WPF 文本输入控件聚焦时不调用文件视图动作。焦点规则回归先因辅助规则缺失失败，接线后 SettingsSmoke 与 App Release 构建通过（0 警告/错误）；真实文本框/原生 Shell 焦点切换仍待 GUI 验收。

2026-10-24 文件区颜色调查续查：源码追到导入确认后创建 ExplorerBrowserHost，并在导航完成后异步恢复查看方式、列、排序和分组；Q-Dir 颜色不进入 ViewState，当前应用链未发现设置文件区背景或系统主题的调用。该结果排除了已知的直接颜色赋值路径，但不能解释可见黑底；DirectUI 最终绘制仍需桌面实验，未改颜色行为。详见[调查记录](shell-color-investigation.md#2026-10-24-导入应用链源码复核)。

2026-10-24 Ctrl+N 新窗口源码调查（历史记录，当前状态见本文件顶部）：App 每进程只创建一个 `MainWindow`；外部目录转发器按 profile 取得单实例所有权锁；每窗口若直接加载同一 `ExplorerDirs.ini`，`SessionStore` 的 compare-before-write 会拒绝第二个窗口的过期快照。不得放宽写入保护来实现 Ctrl+N。用户已选择“每窗口独立会话文件”。基于当前单进程一窗口与按 profile 路由的结构，候选方案是每个新窗口启动一个新进程并传入唯一会话路径；这样可复用现有窗口生命周期和单会话写入锁。仍需决定新窗口是否在下次启动时自动恢复，并设计收藏/预设共享与每窗口固定标签等 sidecar 的存放范围。

## 2026-10-25：Q-Dir Quick Links

- 新增 profile 级 `QuickLinks` 目录，以标准 Windows `.lnk` 保存快速链接；支持从菜单添加当前文件夹，也可在“管理快速链接”打开目录后手工创建文件夹、程序或文件快捷方式。
- `Ctrl+Q` 改为打开快速链接菜单。点击可解析到文件系统目录的快捷方式时，在活动窗格导航；程序或文件快捷方式交给 Windows Shell 启动。原收藏夹与工作区菜单保持独立。
- `QuickLinkStoreSmoke` 覆盖 Unicode 目标、重复目录名、标准 `.lnk` 创建/读取及程序目标。Release SettingsSmoke 与 App Release build 通过；未启动 GUI，菜单、焦点和图标仍待用户验收。
- 已发布隔离预览 `releases/preview-0.6.0-temp.27` 供后续验收；包内 EXE/DLL SHA-256 与清单由发布脚本生成。该步骤不表示 GUI 已验收。

## 2026-10-25：快速链接支持虚拟 Shell 文件夹

- 复查快速链接路径时发现，Shell Link 可以保存 PIDL，而只读 `GetPath` 不能完整表达“此电脑”等虚拟位置。现在读取 `.lnk` 时优先把 PIDL 交给既有 `ShellLocationIdentity` 校验；确认是文件夹才在活动窗格打开。添加当前虚拟 Shell 文件夹也会保存其 PIDL，程序/普通文件快捷方式仍由 Windows Shell 启动。
- Release SettingsSmoke 覆盖 `shell:MyComputerFolder` 快捷链接创建、回读及再次确认文件夹；App Release build 通过。没有启动 GUI。

当时 Ctrl+N 仍列为未实现的键盘项目；这轮没有通过别名到新标签或复制临时 profile 来伪装新窗口。当时的下一步计划是先确定多窗口会话方案；之后已按用户选择改为同进程多窗口、独立会话 INI 与窗口清单，当前实现记录见本文件顶部。

2026-10-24 自动刷新与用户输入竞态复验：Release 隐藏生产宿主运行 `--live-shell-refresh-deferred-watcher-overflow`，在用户键盘输入已记录、刷新延后恢复已排队时制造真实 watcher buffer overflow（1 次 Error 回调）并并发 10,000 项文件变化。溢出后四窗格计数为 `2/2/3/10003`，待处理增量清空，刷新/计数状态结算完成；主窗格的新选择与独立焦点均为 `third.txt`，第二窗格选择和焦点均为 `second.txt`，延后恢复结束且没有抢回旧焦点。Release HostProbe 构建 0 警告/错误；此结果只覆盖本机隐藏宿主与隔离文件夹，不代表可见输入链、网络盘或长时压力，自动刷新 P0 继续开放。

2026-10-24 原生导航历史 Release 复验：`--navigation-capability` 完成首目录、子目录、后退、前进、快速重访、连续请求、veto 后保留历史、分支导航及相同路径的新历史项检查。可读阶段的生产历史状态均与真实 Shell PIDL 匹配；正常序列 `started=4/completed=4/failed=0/pending=False`，快速重访后仍无 pending。Shell 的 TravelLog 查询继续返回 `E_NOTIMPL`，本探针不据此判断历史为空；本机隐藏原生宿主最终父窗口不可见、前台未变。Release NativeProbe 构建通过（0 警告/错误）；可见 App 按钮启用状态、真实焦点及标签切换后历史保持仍待 GUI 验收。

2026-10-24 Shell 右键菜单状态复验：Release `--menu-capability` 在隐藏原生 Shell 视图中确认空选择下 Cut/Copy/Delete/Rename/Properties 均禁用，单选后启用；NewFolder 在两种 `QueryContextMenu` 标志下均通过初始化原生 New 子菜单后从 unavailable 变为 enabled。背景命令探针使用 `SVGIO_BACKGROUND`，没有显示菜单或调用命令；SettingsSmoke 继续通过背景命中忽略残留选区、匹配右键点复用探测及不确定命中失败关闭等用例。此证据不验证真实鼠标命中率、混合菜单可见顺序或可见 Shell 扩展行为，GUI 验收保留。

2026-10-24 `.qdr` 导出原生消费复验：Release `--qdr-native-stream` 将 8 种查看方式各自在有/无分组状态下通过已注册的 `SID_STopLevelBrowser` / `IShellBrowser.GetViewStateStream` 交给隐藏 Shell 消费，共 16 组全部读回请求的模式与图标尺寸，排序、分组和列快照均存在；序列化流为 717 字节，无子窗口显示、前台未变。此探针验证 Windows Shell 原生属性流消费，不是 Q-Dir 应用读取或保存验收；实际 Q-Dir 互操作仍待桌面测试。

2026-10-24 大目录 UI STA 诊断继续：Release 隐藏生产宿主 10,000/50,000 项压力用例均通过，watcher 事件、最终 Shell/磁盘计数、选区与焦点检查通过。Dispatcher 最大间隔分别最高 3.89 秒与 798 ms，而同进程专用线程最大间隔为 46/22 ms；采样的 `OnStateChange` 回调最长 3.36/1.53 ms。现有样本将停顿指向 ExplorerBrowser 所在 UI STA 路径，但还不能归因到某个 Shell API；继续做低开销分段诊断，不贸然调整刷新策略。结果与边界见[大目录调查](live-update-investigation.md#2026-10-24-dispatcher-sta-空窗的回调窗口消息计时)。

2026-10-24 慢消息时间线阈值降至 10 ms 后，Release 隐藏 50,000 项创建/删除压力轮通过：watcher 两阶段均完整，最终计数、选区和焦点正确；Dispatcher 最大间隔 543/543/603 ms，Shell DefView 慢消息约 228/233 ms，与部分空窗重叠但不足以解释全段。自动刷新 P0 仍开放；下一步收集能将 STA 空窗与具体排队或原生调用关联的证据，不猜测私有消息语义、不改刷新策略。完整数据与限制见[调查记录](live-update-investigation.md#2026-10-24-慢消息阈值降至-10-ms-的-50000-项复测)。

2026-10-24 延续 50,000 项 UI STA 关联计时：隐藏生产宿主创建/删除及选择、焦点恢复检查通过；Normal/Input/ContextIdle 最大间隔 630/621/1,191 ms。DefView 单条慢消息最高 266 ms，与部分约 0.6 秒空窗重叠；最大 ContextIdle 空窗仍有未归因时间。Watcher/Dispatcher 队列、计数读取、刷新完成回调均不足以解释全部空窗。没有修改刷新或通知策略；自动刷新 P0 继续开放。下一步需要更强的消息泵关联或原生调用栈证据。详见[调查记录](live-update-investigation.md#2026-10-24-50000-项-ui-sta-关联计时复测)。

2026-10-24 HostProbe 新增显式启用的线程级消息取出时间线：合成消息 smoke 通过，3,000 项观察 11,168 条取出、最大间隔 57 ms；50,000 项观察 130,772 条、最大间隔 273 ms。两段约 0.6 秒 Dispatcher 空窗中，各有约 231–246 ms 与 DefView 慢消息/相邻消息取出间隔重叠，仍余约 0.3 秒未解释。WPR CPU trace 被系统策略拒绝；不改生产刷新行为，P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-补验线程级消息取出时间线)。

2026-10-24 补上 WPF message-only Dispatcher 窗口计时：合成 smoke 验证 `DispatcherProcessQueue` 的 `0xC471` 真实命中 `ProcessQueue()`；一次 50,000 项隐藏测量记录该 WPF 窗口过程累计 23.66 秒、单次最大 157.9 ms，并与同长消息空档关联，仍不足以解释最大 254 ms ContextIdle。该轮文件计数收敛，但开始批量删除前的选区已额外包含 `initial.txt`，选区断言使进程退出码 1；异步刷新中的加入时点待定位，不记功能通过。探针仅在显式环境变量下启用；生产刷新策略未改。详见[调查记录](live-update-investigation.md#2026-10-24-补充wpf-dispatcher-消息窗口处理计时)。

2026-10-24 修正 50,000 项压力探针的选区基线：原探针直接用 Shell COM 改选区，绕过产品 native-input hook，曾留下 3 项旧快照而视图已是 2 项。现在同步产品输入代际并断言快照匹配后再开始负载。Release 隐藏压力轮通过（退出码 0）：创建/删除 watcher 各 50,000，最终计数 3，选区 second/third 与焦点 initial 保持。新 trace 中 WPF `0xC471` 单次最长 902 ms，与 900 ms 消息空窗相邻；Refresh 最大 844 ms；ContextIdle 最大间隔 2.714 秒仍未完全解释。仅修测试基线与 opt-in 诊断日志，不改产品策略；P0 继续开放。详见[调查](live-update-investigation.md#2026-10-24-50000-项诊断复测与选区基线修正)。

2026-10-24 收窄线程消息探针，移除对所有 Shell 子窗口的 WndProc 子类化，只保留 Dispatcher message-only HWND 及线程级消息取出观察。Release 50,000 项隐藏压力轮通过：创建/删除 28.3/29.9 秒，watcher 各 50,000，最终视图/磁盘计数 3，选择与焦点保持；Dispatcher 最大间隔约 445–449 ms，`0xC471` 最大处理 414.7 ms，消息取出最大间隔 415 ms，Refresh 最大 85.1 ms。广泛子类移除后仍有数百毫秒 STA 空窗，但探针仍逐条计时 Dispatcher WndProc，不能称为无观察器基准。产品刷新策略未改，自动刷新 P0 保持开放。详见[调查](live-update-investigation.md#2026-10-24-收窄-dispatcher-消息探针后的-50000-项复测)。

紧接着关闭线程消息泵探针复测 50,000 项，隐藏用例通过、watcher 双向完整、最终计数与选择焦点检查通过；已有 HostProbe/Shell 阶段计时仍启用。Dispatcher 最大间隔为 Normal/Input/ContextIdle `1.872/1.873/2.196 秒`，DirectUI 单次计时最高 945 ms。与前一轮差异显示单轮波动大，不代表开关探针对性能的因果效果，也不是无插桩基准。自动刷新 P0 继续开放；先做交错重复与行为验收，不再扩大逐消息探针范围。详见[调查](live-update-investigation.md#2026-10-24-关闭线程消息泵探针的-50000-项对照)。

更正自然删除选区丢失的前置证据：原 `--live-shell-selection-delete-native` 探针用 Shell COM 设置了当前两项选择，却仍缓存先前的四项快照；新增基线断言先以 `view=2,saved=4` 失败。探针现调用生产 `NoteNativeUserInput()` 并等快照与实际两项匹配后再删除。修正后的 native-only `IFileOperation` 删除副本、显式 `SHCNE_UPDATEDIR`、watcher 删除和 after-input 控制均有有效通过样本，未再复现“删除后两项选择丢失”；一次 after-input 计数延迟 15 秒的样本独立复跑于 81 ms 收敛，反映更新时序波动。该处没有产品逻辑改动；先前选择丢失结论被撤回，自动刷新 P0 仍开放。详见[基线失配调查](live-update-investigation.md#2026-10-24-更正自然删除选区丢失是探针基线失配)。

2026-10-24 数量变化队列合并复测：保持一个 Dispatcher handoff 到当前 250/500 ms 计时窗口结束，并在每次 Shell 计数检查时合并增量。Release SettingsSmoke、App/HostProbe 构建通过；instrumented 10,000 项产品压力复跑通过，watcher 双向均 `10,000/10,000`、最终 Shell/磁盘为 3、选择/焦点恢复。最新轮 20,000 次数量变化只产生 36 次 Dispatcher handoff，ContextIdle 最大间隔 269 ms；一次首次诊断轮在选择/焦点基线等待失败，随后复跑通过，失败分支现输出恢复时间线以便后续定位。同一队列改动后的无回调插桩压力轮仍测得 ContextIdle 最大 4.436 秒、Input/Normal 最大 1.644/1.374 秒；改善派发合并率不能证明秒级 STA 长尾已解决。自动刷新 P0 继续开放。下一步定位该无插桩长空档与具体队列/同步 Shell 调用的关系。详见[队列合并调查](live-update-investigation.md#2026-10-24-数量变化队列的-dispatcher-handoff-合并)。

2026-10-24 Shell 虚拟位置会话与 Q-Dir 接线：`TabSettings.Path` 保留普通文件系统路径或明确支持的 Shell 文本身份，不改变 INI v8。此电脑、回收站、库、桌面通过 Shell 文件夹属性检查；Q-Dir 文本/PIDL 导入与导出共用此身份规则。ZIP 路径仅作为 Shell 候选传给 Windows 并验证文件夹属性，普通目录名含 `.zip` 不足以认定归档路径。后续 SettingsSmoke 用真实临时 ZIP 验证了本机 Shell 对归档根目录和内部目录的 PIDL 解析与虚拟身份分类，专门目录名含 `.zip` 仍保留文件系统身份。会话、QDR、别名解析、导航状态和友好面包屑已接入源码。Release SettingsSmoke 与 App 构建通过；App 0 警告/错误。未启动 ExplorerBrowser 或 GUI，因此可见导航、重启恢复和 ZIP 命名空间实际体验仍未验收。设计与实现范围见[Shell Location Sessions 设计](superpowers/specs/2026-10-24-shell-location-sessions-design.md)。

2026-10-24 临时预览第六版已打包到 `releases/preview-0.6.0-temp.6`，独立配置，程序集信息版本与 EXE/DLL SHA-256 已核对；发布隔离 smoke 通过。包包含当前视图全量导出、按查看方式打印和 Shell 虚拟位置接线。没有启动 GUI，且 Q-Dir 实际互操作、打印预览、自动刷新边界仍待验收；详见[第六版预览说明](releases/preview-0.6.0-temp.6.md)。

2026-10-24 Dispatcher Timer 细分诊断：HostProbe 现记录 WPF `DispatcherOperation` 回调、优先级和 `DispatcherTimer` 的实际 Tick 订阅目标，并与消息取出空档共享 QPC 时间线。10k 压力中曾捕获 `ExplorerBrowserHost` 目录变化 Tick 持续约 3.16 秒；另一轮启用产品阶段计时的压力显示最大 Tick 129.7 ms、同步 `Refresh()` 最大 129.0 ms，计数查询及队列 Drain 均低于 0.2 ms，最终计数、选择和焦点正确。后一轮未复现数秒 Tick，当前只能认定 Refresh 是该轮耗时主要项，尚不足以解释长尾总体差异；产品刷新策略未改，自动刷新 P0 继续开放。下一步以相同计时配置重复压力样本。详见[Dispatcher Timer 调查](live-update-investigation.md#2026-10-24-dispatcher-timer-回调的细分关联)。

2026-10-24 Dispatcher 区间关联复测：相同双 trace 配置下两轮 10k 创建/删除压力通过，计数、选择和焦点恢复。最长 ContextIdle 计时器间隔分别 2.98/2.75 秒；区间内分别完成 868/48 个 ContextIdle 操作，故撤回“低优先级队列持续饥饿”的推断。Background 操作在两窗内分别重叠 1.40/1.16 秒。另有同步 `Refresh()` 占目录变化 Tick 的 633.3/634.0 ms，确认可造成明显 STA 阻塞，但仍不足以解释最长计时器延迟。自动刷新 P0 继续开放；下一步核对周期计时器 Tick 是否延迟或合并，以及诊断计时边界，不调刷新策略。详见[优先级负载调查](live-update-investigation.md#2026-10-24-dispatcher-优先级负载复测)。

2026-10-24 区分 DispatcherTimer 间隔与实际队列延迟：WPF 文档确认 `DispatcherTimer` 不保证准时，`ContextIdle` 不对应系统空闲状态；源码显示 Tick 完成后重新排入一个后续操作。HostProbe 新增显式开关的独立 ThreadPool 定时投递探针及 smoke；10k 配置两轮中 Normal/Input/ContextIdle 最大投递延迟为 `90.5/99.8/229.7 ms` 与 `139.6/152.9/595.0 ms`。第二轮在 15 秒后目录视图仍为 29 项、磁盘为 3，退出码 1；关闭新探针的同配置对照通过并收敛到 3 项。该差异尚不能归因于探针，说明负载结果波动且测量有扰动可能。保留失败证据、探针默认关闭；下一步降低插桩负担并交错复测，不改产品刷新逻辑，P0 继续开放。详见[Dispatcher 队列延迟调查](live-update-investigation.md#2026-10-24-wpf-定时器间隔与-dispatcher-队列延迟区分)。

同一独立队列探针降为 250 ms 采样后再跑 10k 压力：116 批均投递并完成，Normal/Input/ContextIdle 最大排队延迟 `36.1/44.0/136.6 ms`；原周期 DispatcherTimer 最大间隔 `119/120/220 ms`。文件变化、最终 Shell/磁盘计数 3、选择与焦点均通过。该轮没有复现秒级间隔；不据单轮推断采样频率的因果作用。保留 opt-in 低频探针和失败轮，不改变刷新策略；P0 继续开放。详见[Dispatcher 队列延迟调查](live-update-investigation.md#2026-10-24-wpf-定时器间隔与-dispatcher-队列延迟区分)。

2026-10-24 状态栏键盘入口：每窗格“查看”按钮加入文件视图之后的 Tab/Shift+Tab 停靠点，并纳入 F6/Shift+F6 区域循环；状态信息文本仍不单独占键盘焦点。SettingsSmoke 的焦点索引、详细/非详细视图停靠点、搜索栏跨窗格正反向遍历通过；App Release 构建 0 警告/错误。未启动 GUI，菜单弹出/关闭后的真实键盘焦点、原生列标题停靠及四窗格可见行为待验收。

2026-10-24 动态键盘停靠点映射：将导航控件后的搜索输入/按钮、快速筛选控件、选中标签、目录树、详细列标题、文件视图和状态工具统一由纯逻辑 `PaneFocusStopMap` 计算；`PaneControl` 的 Tab 与 F6 路由共用该映射。SettingsSmoke 对搜索/筛选/树/列头的 16 种组合检查停靠点连续唯一、Tab 全环及 F6 可见区域顺序；全量 Release SettingsSmoke、App Release 构建通过（0 警告/错误）。未启动 GUI；实际 WPF/原生列头控件焦点、禁用筛选收藏按钮的跳过体验和多窗格输入仍需可见验收。


2026-10-24 `.qdr` 样本只读检查输出：`SettingsSmoke --inspect-qdr` 现在同时显示应用布局编号、活动窗格和每条导入提醒，便于辨认真实配置落入哪个已核实映射并区分预期不导入字段。对本机 Q-Dir 12.79 `start.qdr` 重跑：44 个标签、窗格 13/12/10/9、44 个视图快照、布局 4、活动窗格 1；唯一提醒为树/预览/颜色/窗口位置/启动命令不导入。完整 SettingsSmoke 通过。此为只读解析，不代表 GUI 导入或 Q-Dir 导出互操作。

### 2026-10-24 目录监视器基线回调代际保护

源码复核发现启动期计数读取会进入 Shell COM；该调用可能泵消息并重入导航，导致旧目录回调在 COM 返回后继续接受稳定样本、覆盖新目录的监视批次和计时器。现将监视代际、导航代际、就绪状态、已完成路径和队列身份绑定为一次请求，并在 Shell 计数读取前后都验证请求仍有效；旧回调只能清理自己的监视器。SettingsSmoke 覆盖监视代际、独立导航代际（包括新视图已就绪）、路径和队列变化；全量 SettingsSmoke 通过，App Release 构建 0 警告/错误。没有运行 ExplorerBrowser/HostProbe，因此尚未通过真实 Shell COM 重入或可见界面验证。详见[动态更新调查](live-update-investigation.md#2026-10-24-监视器启动稳定样本的重入保护)。

### 2026-10-24 `.qdr` 任意可解析 Shell 文件夹位置

按用户确认，将 `.qdr` 虚拟位置支持范围从内置别名和 ZIP 扩展为所有能被当前 Shell 解析且 `SFGAO_FOLDER` 确认的绝对解析名称。缺少扩展提供程序时，只让对应导入标签无法恢复；不接受协议 URI、非文件夹或搜索结果 PIDL。`ShellLocationIdentity` 现保留 `::` 绝对 Shell 名称，并在 PIDL/文本位置解析和文件夹属性检查后持久化；纯状态烟测覆盖名称形状、协议 URI 拒绝及编码 PIDL resolver 返回虚拟 Shell 身份；另运行不创建 ExplorerBrowser 的 Shell 位置/Q-Dir 专项烟测，验证本机绝对命名空间文件夹识别、稳定身份归一化、真实虚拟文件夹 PIDL 导入以及非文件 PIDL 跳过后保留有效标签。导出专项另验证任意 `::{GUID}` 解析名写入 Q-Dir 当前标签基础字段，并可从带/不带 Explorer Dirs 扩展的文件读回。未运行 ExplorerBrowser 或 `.qdr` GUI 导入，具体扩展提供程序和可见导航体验仍待验收。规则同步到 `AGENTS.md`、QDR 导入说明和 Shell 会话设计。

2026-10-24 任意 Shell 解析名称标题修正：发现非内置 Shell 绝对名称在标签/面包屑处回退成 `Path.GetFileName`，会显示原始 `::{GUID}` 语法。现对可解析且确认是文件夹的 Shell 解析名调用 `SHGetNameFromIDList(SIGDN_NORMALDISPLAY)`，让标题沿用 Windows 本机 Shell 的友好名称；已知中文别名与普通文件系统路径显示不变。新增 Shell 专项回归先复现旧行为再通过；Shell 位置/Q-Dir smoke、纯状态 smoke 和 App Release 构建通过（0 警告/0 错误）。没有启动 ExplorerBrowser、GUI 或 Q-Dir，第三方命名空间提供程序的显示效果仍需在安装该提供程序的环境验收。

2026-10-24 Q-Dir 明文虚拟路径格式对照：官方 FAQ 117 明确给出 `shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}` 的 `.qdr` 示例。Shell 位置专项 smoke 现验证这一确切语法能解析为文件夹、归一化到与编码 PIDL 相同的身份、显示友好名称，并经 `DirN` 明文字段导入。Release 专项 smoke 通过；没有启动 Q-Dir，仍不宣称 Q-Dir 对本程序导出的任意 Shell 位置互操作通过。

### 2026-10-24 逐窗格目录树与 Q-Dir `TreeN` 状态

目录树由单一全局显示值改为每窗格状态，增加窗格内切换入口；顶部“目录树”仍一次切换所有窗格，部分窗格打开时点击会统一打开。会话 INI 升为 v9，v1-v8 沿用 `NavigationTree.ini` 默认状态，首次升级写回前保留 `.before-v9-*.bak` 原文。`.qdr` 的 `Tree1`～`Tree4` 暂按 0/1 并映射到已知布局的 Q-Dir 窗格编号。静态对标发现共享树/逐窗格树/关闭三模式；共享树模式仍未实现，`tree_mode` 与 `TreeNsplit` 未接线且数值语义未确认。Session、Q-Dir import/export 和旧版本升级 smoke 通过，App Release 构建 0 警告/错误；未操作 GUI，窗格按钮体验、重启恢复及 Q-Dir 实际互操作待验收。

2026-10-24 Q-Dir 状态栏空间概览补齐：对照官网列出的分区空间概览，本程序状态栏现以占总容量的整数百分比优先显示，并同时保留当前用户可用字节；四舍五入到最近整数，容量未知或总量为 0 时不会伪造比例，原有精确可用/总量工具提示保留。SettingsSmoke 新断言先在旧实现上按预期失败，再通过；完整 SettingsSmoke 通过。尚未启动 GUI，四窗格窄宽度、挂载点/配额、网络盘显示及实际标签布局仍待验收。此项不包含递归文件夹大小计算。

2026-10-24 临时预览 temp.12：打包到 `releases/preview-0.6.0-temp.12`，独立 profile；程序集产品版本 `0.6.0-preview.temp.12+77495f5d72a4739c2bb33a21d021328294f728c0`。发布隔离 smoke、全量 SettingsSmoke 和 App Release 构建通过，EXE/DLL SHA-256 与包内清单一致。没有启动 App、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir；完整待测边界见[第十二版预览说明](releases/preview-0.6.0-temp.12.md)。

## 2026-10-24：目录树显示模式

- 应用增加共享目录树、每窗格各自目录树、关闭目录树三种模式。共享树通过原生 Shell tree host 显示在所有文件窗格左侧，目录选择导航活动窗格，活动窗格切换与导航时同步路径；逐窗格可见状态保留，切换模式不丢失偏好。菜单选择和每窗口 v10 INI 会话保存；v9 恢复逐窗格模式，v1～v8 沿用旧的 `NavigationTree.ini` 默认值并在升级前备份。
- `.qdr` 应用扩展会往返树模式；Q-Dir `tree_mode` 数值映射继续留空，避免根据关闭态样本猜测。Release SettingsSmoke 和 App 构建通过；共享树已接入每窗格 Tab/Shift+Tab 与 F6/Shift+F6 焦点停靠点，纯逻辑焦点回归通过；共享树尺寸、真实原生焦点及切换多布局的可见行为未启动 GUI 验收。

## 2026-10-24：布局切换后的共享树同步

- 源码路径复核发现 BuildLayout() 会恢复布局记忆的活动窗格，但此前没有同步共享目录树。现将活动窗格选择规则提取为可回归纯逻辑：优先该布局记忆的可见窗格，其次保留当前仍可见窗格，最后回退窗格 1；布局应用后同步共享树路径。新回归先因缺少选择器而失败，接线后 Release SettingsSmoke 与 App Release 构建通过。GUI 树展开位置、焦点保持及实际重排仍未验证。

## 2026-10-24：共享树对 Shell 绝对解析名的路由修正

- 复核共享树同步时发现它用 shell: 前缀猜测虚拟位置；用户已确认的任意 Shell 绝对解析名（例如 ::{GUID}）会被误送到只接受文件系统路径的树定位逻辑。现在路由依据活动 ExplorerBrowser 的 IsFileSystemLocation 状态，虚拟 Shell 文件夹统一切到虚拟树根。针对文件系统路径、::{GUID} 和 shell: 别名的新 smoke 先因目标策略缺失失败，接线后通过；全量 Release SettingsSmoke、App Release 构建及相关 git diff --check 通过。未启动 ExplorerBrowser，真实命名空间树的可见定位仍待 GUI 验收。

## 2026-10-24：Q-Dir 未确认目录树字段的导入提示

- 官方 FAQ 117 给出了 `tree_mode` 与 `Tree1split`～`Tree4split` 示例值，但没有定义枚举或比例单位；FAQ 32/26 分别说明逐窗格树与共享树能力。导入器现在遇到这些字段时在导入预览中明确提示不会应用，避免用户误以为树模式/宽度已恢复；本程序自己的树模式仍由 `ExplorerDirs.State` 扩展恢复。新增 Q-Dir 字段预览回归先失败，补上警告后 Release SettingsSmoke 与 App 构建通过。此项不猜 Q-Dir 数值语义，待受控配置样本后再做映射。

## 2026-10-24：明确提示暂定的 Q-Dir Tree1～Tree4 映射

- 当前导入器会把完整且有效的 Tree1～Tree4 0/1 按逐窗格开关应用，但这一语义仍是基于字段名与 Q-Dir 的逐窗格树功能说明作出的假设，缺少可见互操作证据。导入预览现显示该限制。回归先失败，再通过；Q-Dir 导入专项 smoke 与 App Release 构建通过。字段映射仍待受控启用/关闭 .qdr 样本或可见验收确认。

## 2026-10-25：虚拟 Shell 位置的搜索范围扩展

- 本机无窗口探针确认 `ISearchFolderItemFactory.SetScope` 接受 Desktop、This PC、Recycle Bin 与 `shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}`；`search-ms:` 在这四个范围均解析为文件夹。超时保护子进程对搜索结果只请求首项：Desktop 与 Recycle Bin 返回空枚举，This PC 别名及 GUID 形式各返回一项。该证据支持启用 Shell 身份范围，不能证明完整结果集、第三方扩展或可见 UI。应用现允许已识别别名与通过本程序 Shell 文件夹身份规则的 `::{GUID}` 作为搜索范围，搜索完成后仍保留虚拟位置标记；快速筛选仍是独立功能。SettingsSmoke 新回归先按预期失败，再通过；NativeProbe 与 App Release 构建均为 0 警告/错误。搜索结果枚举只取首项，真实 ExplorerBrowser 导航及提供程序失败提示仍待验收。详见[微软搜索范围说明](https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-wds-qryidx-crumb)与[`ISearchFolderItemFactory`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-isearchfolderitemfactory)。

## 2026-10-25：准备右键空白处问题的隔离 GUI 验收副本

- 运行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`，在 `src/.state/gui-smoke/20261024-200608-771-732ea2/` 生成独立应用、profile、四窗格测试目录及 `.qdr` 颜色基线。准备清单状态为 `prepared-not-run`；23 个测试文件、四个窗格目录及配置文件存在，EXE/DLL SHA-256 与清单一致。`-PrepareOnly` 在加载 UI Automation 与任何窗口操作前返回，因此没有启动应用或验证原生右键菜单。后续桌面空闲且用户明确交接后，可按清单验收“选中项目后右键空白处”的 Shell 背景菜单，并记录自定义路径命令目标。

## 2026-10-25：批量选区恢复调用标志复核

- 隔离的隐藏 `IShellView` 原型尝试重放多选恢复路径。Shell 批量接口返回成功，但随后读回仍含有恢复前的其他项目；源码复核发现 `SelectAndPositionItems` 调用未指定 `SVSI_DESELECTOTHERS`，现已补上该标志，确保批量恢复替换旧选区。微软文档确认该接口应用 `_SVSIF` 标志，且 `SVSI_DESELECTOTHERS` 用于取消其它项目选择；文档未承诺隐藏或失效 PIDL 的行为。[`IFolderView::SelectAndPositionItems`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems) 与 [`_SVSIF`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif)。
- 该原型的隐藏且未激活视图在批量 API 返回成功时仍出现选择读回不匹配，焦点恢复亦无法验证；因此停止把它用作生产行为验收，不据此推断可见 ExplorerBrowser 的选区或焦点表现。SettingsSmoke 全量通过，App Release 构建 0 警告/错误。更改后的 Shell 行为仍待用户明确桌面授权后的 ExplorerBrowser 隔离验收。
- 接着源码追踪发现，播种兜底也曾只依据 HRESULT 报告成功；现在它会在批量调用后核对选区，校验失败则回到逐项恢复幸存 PIDL 的既有路径。纯逻辑回归先因验证入口缺失而失败，新增 `TryRestoreSeededFallback` 后通过；本项仍未由真实 Shell 宿主验证。

## 2026-10-25：Q-Dir 颜色配置的样本边界

- Q-Dir 官方 FAQ 6/47 已公开文件类型过滤语法、目录/隐藏项标记、背景占位符；FAQ 47 说明需启用保存选项后，颜色设置才会随收藏保存。FAQ 117 示例列出 `useColor=0` / `useTreeColor=0`。官方资料未展示规则/背景记录在 `.qdr` 中的字段和序列化格式。只读复核本机 `start.qdr` 与收藏文件仍只看到 `colorthema=0`，没有颜色规则或背景值。导入器继续不应用规则、背景或两个开关，并将开关名加入导入预览提示；新增 smoke 覆盖含 `useColor=1` / `useTreeColor=1` 时仍明确提示不导入。后续需要启用颜色保存且含正向颜色设置的 Q-Dir 收藏样本，才能核实实际字段/编码并映射到每窗格/每标签状态。见[QDR 导入范围](qdr-import.md#明确未导入)。

## 2026-10-25：隔离颜色原型像素采样前置条件校正

- 复核 `--ishellview-color-spike-visible` 时发现取消夹具选中项曾误传 `SVSI_FOCUSED`（`0x10`），而非 `SVSI_DESELECT`（0）。现改为只传 `SVSI_NOTAKEFOCUS`，并在采集像素前断言 Shell 选中数为零。隐藏原型复跑与 NativeProbe Release 构建通过，仍只观察到平铺模式的逐项通知、且离屏像素未命中目标颜色。按当前不操作可见 GUI 的约束，没有运行可见采样分支；颜色是否实际绘出、焦点是否保持仍待之后获准的可见隔离验收。详见[隔离 IShellView 颜色原型](ishellview-color-spike.md#2026-10-25-可见像素探针的选择标志校正)。

## 2026-10-25：排序与分组后的 Shell 项目身份映射

- 隐藏 `IShellView` 临时夹具用不同文件大小验证名称倒序、大小升序和大小分组；`IFolderView2::GetSortColumns`/`GetGroupBy` 读回设置，按当前 `IFolderView::Item` 索引读取的 PIDL 一一对应到两个文件，顺序符合名称/大小排序。对应状态的离屏打印没有产生逐项 `NM_CUSTOMDRAW`，所以这证明可在状态变化后重新查询索引身份，不证明绘制通知/着色在排序或分组下有效。Release NativeProbe 构建和隐藏运行通过，父窗口不可见、前台未变，生产文件视图未创建。

## 2026-10-25：提示尚未应用的 Q-Dir 位置字段

- 核对官方 FAQ 117 示例发现 `SPT=2000` 和 `VorschauPos=7500` 还没有在导入预览中说明。现有解析器继续不应用这两个字段，并分别提示 `SPT` 含义未确认、`VorschauPos` 预览位置尚未支持；未猜测其单位或映射。官方示例型回归先失败，补充提示后通过；随后本机 Q-Dir 12.79 启动配置与收藏配置各自 `--inspect-qdr` 退出码 0，导入数量/视图快照保持不变，未支持警告从 3 项增为 5 项。完整 Release SettingsSmoke、App Release 构建及文档 diff 检查通过；没有启动 GUI 或 Q-Dir。详见[Q-Dir 导入范围](qdr-import.md#明确未导入)。
- 再核对 FAQ 原文确认 `VorschauPos` 只有 Preview 注释，示例 `Vorschau=0`，没有单位或方向说明。本机启动与收藏配置的 `VorschauPos` 也同为 7500 且预览关闭，无法形成差异对照；继续保留未导入提示，不按字段名推断比例。[Q-Dir 官方 FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)

## 2026-10-25：可见颜色探针覆盖全部查看方式

- 扩展隔离 `IShellView` 可见验收入口：获准运行时会清空临时选区、逐一切换产品的八种原生查看方式，采集 ListView 窗口像素并尝试把收到的逐项通知索引映射到夹具 PIDL。Release 构建和隐藏分支回归通过；按当前要求未运行可见分支，因此尚无真实可见颜色或焦点结论。详见[隔离 IShellView 颜色原型](ishellview-color-spike.md)。
- 再次隐藏复核通过：八种模式和原生菜单能力保持；本次背景/文件/文件夹菜单准备约 209/96/207 ms。绘制通知 HDC 仍无效、项目矩形为空、目标像素为零；没有因此修改生产窗格。隐藏焦点读数不作为 UI 证据；颜色接入决策仍等可见隔离采样。

## 2026-10-25：第三方 Shell 菜单扩展查询耗时基线

- NativeProbe 对隔离临时目录查询 Shell 背景、选中文件和选中文件夹菜单。十四次背景/文件查询耗时范围分别为 233.88–366.59 ms、95.04–175.91 ms；六次文件夹菜单耗时 192.35–323.37 ms，后五次返回 98 个规范动词并包含打开、删除、重命名、属性。查询均成功，父窗口隐藏、前台未变；探针只记录动词数量，不保存扩展清单。该指标包含系统及已注册扩展的共同开销，不能归因某扩展或代表界面端到端响应。`scripts/native-probe-watchdog.ps1` 在独立进程运行探针，默认 30 秒超时并保留日志；正常探针与 `-TestTimeoutPath` 睡眠子进程超时自测均通过。后者验证监督器能终止超时进程，不代表 Shell 扩展挂起时的全部清理行为；该探针也不证明产品已有故障隔离。详见[可靠性与隔离设计](reliability-isolation-design.md#2026-10-25本机原生菜单查询初始耗时)。

## 2026-10-25：详细视图 Ctrl+数字小键盘加号

- 新增 `--auto-column-width` 隐藏原生 `IShellView` 探针，直接创建两个长名称文件，设置详细视图并用 `IColumnManager` 读取列宽。原生 Shell 会把小于 110 像素的列宽钳制为 110；基线校正后，`IShellView.TranslateAccelerator` 对 Ctrl+VK_ADD 返回未处理，随后投递到实际 `SysListView32` 的按键使 Name 列从 110 调到 367 像素，Type/Size 列保持 110。探针退出码 0，隐藏父窗口保持不可见、前台窗口不变，临时夹具已清理。此结果证明当前 Windows Shell ListView 具备该原生行为，不证明实体数字小键盘物理输入、ExplorerBrowser/WPF 的实际消息循环及可见 GUI，仍待桌面验收。Q-Dir 官方 FAQ 也将 Ctrl+NumPad+ 列为与 Explorer 相同的列宽自适应快捷键：[FAQ 73](https://www.softwareok.com/?faq=73&seite=faq-Q-DIR)。

## 2026-10-25：Q-Dir 每标签查看模式范围与 Shell 回退

- 本轮更正（后被更新资料再次更正）：当时检查到的英文 FAQ 117 页面没有说明单标签 `ViewModeN` 字段，也没有列 FVM 范围；本机 Q-Dir 12.79 配置样本仅观察到 3/4，因此当时把 1–8 映射判为推断并按 Microsoft Shell 枚举接受 8。随后查到官方德文 FAQ 117（2025 更新）明确给出 Q-Dir 标签视图 1–7，故当前导入行为已按本文开头记录修正：Q-Dir 值 8 回退详细信息，Content 仅作为本程序扩展状态保留；Q-Dir 实际读取其他模式仍待可见互操作确认。
- 隔离默认 `IShellView` 探针对 5（Thumbnail）和 7（Thumbstrip）分别调用 `IFolderView2.SetViewModeAndIconSize` 与 `IFolderView.SetCurrentViewMode`。两 API 都返回 `S_OK`，但读回都为 2（小图标）。导入器因此将 5 映射为大图标（1/96 DIP）、7 映射为平铺（6），并在预览说明替代原因。导出应用 Content（8）时，因 Q-Dir 官方功能清单没有列出 Content，`ViewModeN` 和原生视图流都写详细信息（4），本程序扩展保留 8。`SettingsSmoke` 与 App Release 构建通过；未显示窗口或改生产文件视图。ExplorerBrowser 实际显示及 Q-Dir 读取导出文件仍未验证。参考：[Q-Dir FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)、[Q-Dir 官方功能清单](https://softwareok.com/?seite=freeware%2Fq-dir%2Fhelp+file) 与 [Microsoft FOLDERVIEWMODE](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderviewmode)。

## 2026-10-25：`ColsID` 与标签视图快照的关系复核

- 临时诊断直接调用现有 `QdrViewSettings.TryRead` 解析两份本机样本的 64 个标签级 `ColsAndSort`，并用哈希关联 `ColsID` 与解析出的列/排序快照，不输出字段值、路径或快照内容。64 项全部解析成功；3 组不同 `ColsID` 对应 4 组视图快照，其中存在双向的一对多关系。`start.qdr` 另外包含 4 组无标签后缀的窗格级字段。该样本相关性不支持把 `ColsID` 当作 `ColsAndSort` 的可替代编码；其正式语义仍未由公开资料定义。导入器继续保留未识别字段预览提示，不新增猜测性映射。[Q-Dir 导入范围](qdr-import.md)

## 2026-10-25：隔离 `IShellView` 颜色原型复跑

- 按用户选择继续隔离原生 `IShellView` 原型，不迁移生产 ExplorerBrowser。Release NativeProbe 隐藏运行确认八种视图模式、文件视图选择/拖放接口，以及背景和项目 Shell 菜单仍可用；逐项颜色通知虽可收到，但行矩形为空且无法读取目标色像素。该结果验证原型的 Shell 能力保留范围，不证明用户可见着色。屏幕内颜色采样仍待可用的桌面验收时段，详见[隔离 IShellView 逐项颜色原型](ishellview-color-spike.md)。

## 2026-10-25：分组快速筛选视图导出回退实证

- 隔离默认 `IShellView` 的 `--ishellview-grouped-filter-export` Release 探针退出码 0。分组与快速筛选同时启用时，Shell `GetItems` 路径和逐行 `IFolderView::Item` 回退路径均返回当前可见的 `child|keep.txt`，顺序一致；临时父窗口不可见且前台句柄未改变。它验证默认 `IShellView` 下的捕获逻辑，不替代 ExplorerBrowser 生产 HostProbe、导出菜单或可见 UI 验收。

## 2026-10-25：文件操作快捷键不拦截回归

- `ShellFileCommandsSmoke` 新增断言：Ctrl+A 与 Ctrl+X 映射到原生 Shell 文件视图命令，Shift+Delete 不被应用级快捷键表截获。完整 Release SettingsSmoke 通过。此处仅验证快捷键解析/分派边界，不证明 Shell 实际永久删除、确认框、撤销恢复或删除后焦点；这些仍待可见桌面验收。

### 完整 smoke 的环境消息时序复核

同一轮完整 SettingsSmoke 首次通过；两次后续复跑在现有 `EnvironmentChangeMessageSmoke` 的 5 秒 STA 线程等待处超时。单独运行该环境消息 smoke 通过（一次实测 1.89 秒），单独运行 `--shell-file-commands-smoke` 也通过。当前证据指向完整套件的时序波动；本轮没有放宽原 5 秒门槛，也未将后续失败归因于快捷键回归。

### 环境消息 smoke 阶段诊断

后续复核在环境消息 smoke 中加入阶段记录，超时会报告 STA 线程停留阶段；测试线程改为后台线程，避免超时报错后继续阻止进程退出，原 5 秒失败门槛不变。阶段诊断后，环境消息单项、文件命令单项及完整 Release SettingsSmoke 均通过（完整套件约 15 秒）。本轮未重现超时，故具体停滞根因仍未确认。

## 2026-10-25：OLE `IDropTarget.Drop` 效果返回边界

- 复核 [Microsoft `IDropTarget::Drop` 文档](https://learn.microsoft.com/en-us/windows/desktop/api/oleidl/nf-oleidl-idroptarget-drop)：OLE `DoDragDrop` 在用户完成拖放时调用 `Drop`，并将 `pdwEffect` 返回给源程序；文档明确建议不要直接调用 `Drop`。因此隐藏 HostProbe 直接调用时观察到“源文件已移动、目标正确、效果码为 0”，只能作为直接接口探针的副作用记录，不能据此判定真实拖放反馈错误或修改生产效果映射。跨窗格拖放仍需 OLE `DoDragDrop`/实际用户拖动验收。

## 2026-10-25：隐藏 ExplorerBrowser 中列标题 folder flags 运行时复验

运行 `dotnet run --project src/ExplorerDirs.NativeProbe -c Release --no-restore -- --folder-header-options-hosted`，退出码 0。ExplorerBrowser 在 Initialize 前的 `FVO_CUSTOMPOSITION` 从 `0x00000000` 读回 `0x00000002`；两个不同目录的 `OnViewCreated` 均成功清除 `FWF_NOCOLUMNHEADER` / `FWF_NOHEADERINALLVIEWS`，原始标志 `0x40200200` 本来就未设置这两位。其后在有/无分组下切换八种查看方式，列顺序/宽度、排序/分组和两项选区身份均保持；父窗口始终隐藏，前台未变。

这补上了“初始化前设置选项是否传到已创建 Shell 视图”的隐藏 ExplorerBrowser 运行时证据。它没有读取各模式的 `SysHeader32` 可见样式，也未显示窗口，因此不能证明所有模式中列标题实际可见或可交互；该项仍需用户可见桌面验收。没有修改产品实现或发布包。

### 2026-10-25 列头 HWND 样式与 folder flags 对照

将隐藏 ExplorerBrowser 列头探针扩展为每次原生模式切换后读取当前 `IShellView` 的 `IFolderView2::GetCurrentFolderFlags`，并枚举 `SysHeader32` 自身 `WS_VISIBLE` 样式；探针 Release 构建 0 警告/错误，运行退出码 0，父窗口隐藏且前台未变。

16 组（有/无分组 × 八种模式序列）中 folder flags 始终读回 `0x40200200`，`FWF_NOCOLUMNHEADER` 与 `FWF_NOHEADERINALLVIEWS` 两位都保持清除。`SysHeader32` 始终存在，但自身可见样式会随视图切换变化：详细信息与平铺可见，列表通常隐藏；小图标/内容及图标模式不同尺寸在重复序列中出现过不一致读值。由于读数依赖切换历史，不能据此形成逐模式稳定结论，也不能把 `WS_VISIBLE` 样式直接等同于可见像素或可用交互。

这说明目前不是 folder flags 被 Shell 改回；单靠这两个标志也没有保证每个模式的列头 HWND 保持可见。没有在生产宿主强制设置窗口样式，因为这可能改变 Shell 原生布局/命中行为；下一步需在隐藏原生视图里先查清样式变化的时序和列头通知/排序行为，再决定是否有可维护接入点。当前源码的“所有查看方式显示列标题”不应宣称已完成，仍需可见验收。

### 2026-10-25 列头异步样式与列项数复测

将取样点扩为模式切换后立即、50 ms、250 ms、1 s；在 1 s 取样时再次执行完整的“有/无分组 × 八种模式”矩阵。第二轮与前一轮一致：多种模式的 `SysHeader32` 自身样式在立即/50/250 ms 仍为隐藏，到 1 s 变为 `WS_VISIBLE`；详细信息、平铺及部分图标尺寸更早可见。所有取样的 `IFolderView2::GetCurrentFolderFlags` 都是 `0x40200200`，header 子窗口的 `HDM_GETITEMCOUNT` 始终为 3，三列顺序/宽度快照仍通过，选区/排序/分组也保持。

这使“模式不支持列头”的假设不再符合本机隐藏宿主数据；更符合证据的解释是 ExplorerBrowser/Shell 视图异步完成列头布局，早期的 250 ms 样本读到了中间状态。证据仍只包含 HWND 样式和列项数，没有读取真实绘制像素、标签文本、鼠标命中/排序动作，也没有可见 ExplorerBrowser 验收；不能据此宣称用户看到或可操作的列头已通过。没有修改产品逻辑。

### 2026-10-25 ExplorerBrowser 列头通知路由与排序复测

在隐藏 ExplorerBrowser 中附加产品 `ShellBrowserSite`，启用现有回调计时，并针对首列分别测试两种窗口消息。直接向 `SysListView32` 发送 `WM_NOTIFY/HDN_ITEMCLICKW` 会使 Name 排序从降序变为升序，`ICommDlgBrowser3.OnColumnClicked` 计数为 0；随后使用 `HDM_GETITEMRECT` 得到首列坐标，将 `WM_LBUTTONDOWN/WM_LBUTTONUP` 定向发送给 `SysHeader32`，排序再次翻转，`OnColumnClicked` 仍为 0。视图矩阵中的列数为 3，列顺序/宽度、选择身份及排序/分组保持检查通过；父窗口隐藏、前台未变。

这说明本机 ExplorerBrowser 的 DefView 能在隔离窗口消息下自行处理列头点击并更新排序，无需依赖 `ShellBrowserSite.OnColumnClicked`。此探针没有物理鼠标或可见窗口，不能验证用户实际命中、列头像素/文字是否可见、真实点击是否同时经过相同路径；不据此宣称 GUI 验收通过，也未改生产排序逻辑。

## 2026-10-25：Ctrl+数字小键盘加号转交原生列宽自适应

- Q-Dir/Explorer 快捷键清单与隔离原生视图探针已确认 Ctrl+VK_ADD 可自动调整详细视图列宽；源码之前没有按键转发，新增的快捷键回归先按预期失败。
- 现仅当文件视图收到 Ctrl+VK_ADD 且 `IShellView.TranslateAccelerator` 返回 `S_FALSE` 时，将原始 KeyDown 定向发送给当前视图的 `SysListView32`；Shell 已处理、返回错误、其它修饰键/数字键以及 KeyUp 均不走该路径。快捷键不重实现列宽算法，仍由原生 Shell 执行。
- 新增 pure routing smoke 覆盖未处理结果、键消息、VK_ADD 和修饰键边界；Release SettingsSmoke、App Release 构建均通过，0 警告/错误。未启动 ExplorerBrowser 或可见 GUI，因此实体数字小键盘在产品 HwndHost 中的列宽变化仍待后续授权验收。

## 2026-10-25：复验隔离 `IShellView` 颜色原型

- 按用户选择复跑隐藏原型：NativeProbe Release 构建 0 警告/错误，watchdog 退出码 0，前台未变；八种原生查看模式、Shell 背景/项目菜单、`IFolderView2` 和 `IDropTarget` 查询通过。逐项 `NM_CUSTOMDRAW` 仍只有平铺模式收到两条通知，通知矩形为零、裁剪区为空，八种模式目标像素均为 0。`IFolderView::GetItemPosition` 返回的独立布局位置非空，说明空矩形限于回调几何采样，不能描述为 Shell 项目完全没有位置。
- 没有生产视图迁移；用户要求本阶段不操作电脑，因此未执行屏幕内采样。行着色维持未决，后续若有可用桌面验收条件，再对隔离视图验证真实回调矩形/颜色像素以及选择、菜单和文件操作行为。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-用户选择后的隐藏原型复验)。

## 2026-10-25：私有 Desktop 颜色采样器校正

- NativeProbe 的 ITEMPOSTPAINT 采样曾被空 `NMCUSTOMDRAW.rc` 覆盖；现于绘制前缓存 `LVM_GETITEMRECT` 的行矩形，并保留有效回调矩形优先。私有 Desktop 连续运行两次，Content 模式均在 `LVIR_LABEL` 中心读到两种预期颜色。可见宿主逐模式计数为：六种图标/列表/详细模式 PREPAINT 与 ITEMPREPAINT 均为 0，平铺为 6/0，Content 为 6/2；整窗 BitBlt 像素扫描仍为 0。各模式的列表 HWND 都可见且同位置，只有详细模式少 `WS_HSCROLL`；隐藏样本在平铺收到两条项目通知但裁剪区为空，表明布局与绘制生命周期都会影响通知。需继续比较通用 ListView 通知和 Shell 的重绘路径。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-标签区域颜色回调复验)。

## 2026-10-25：普通 ListView 全模式自绘基线

- 私有 Desktop 新建普通 `SysListView32`，切换 Icon、Details、SmallIcon、List、Tile、Content 六种模式。每种都收到 1 次 PREPAINT、2 次 ITEMPREPAINT/ITEMPOSTPAINT，字段回读与标签区域像素均命中两种目标色；Release NativeProbe 构建与 watchdog 运行通过，主桌面前台不变。结合上一项 Shell `DefView` 矩阵，普通控件通知机制本身可用，但不能将此结果外推到 Shell 文件视图或产品 ExplorerBrowser。接下来评估保留 Shell 操作能力的着色挂接方案。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-普通-listview-全模式颜色基线)。

## 2026-10-25：Shell 模式后同步 ListView 控件模式

- 先调用 Shell `IFolderView2.SetViewModeAndIconSize`，再对其 `SysListView32` 发送官方 `LVM_SETVIEW`（Icon、Details、SmallIcon、List、Tile）。原始五模式日志并未断言 Shell 与 ListView 模式一致；复查发现 Icon 实际为 Shell `2/16`、ListView `0`。后续显式指定图像尺寸并新增模式相等断言后，未排序/分组时五种视图均匹配，逐项自绘通知与目标颜色像素命中。该隔离证据不含命令执行或产品 ExplorerBrowser 的导航、列、焦点/键盘、拖放、排序/分组着色验收；尚未接入生产。[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-shell-模式后同步-listview-控件模式)；官方接口：[LVM_SETVIEW](https://learn.microsoft.com/en-us/windows/32/Controls/lvm-setview)、[ListView NM_CUSTOMDRAW](https://learn.microsoft.com/en-us/windows/win32/controls/nm-customdraw-list-view)。

## 2026-10-25：Shell 与 ListView 匹配模式时的颜色回调

- 该轮原始日志没有校验两个模式是否相等；复查发现 Icon 实际读回 Shell `2/16`、ListView `0`，因此原结果只证明像素着色，不能证明模式匹配。采用显式图像尺寸及严格断言后，未排序/分组的五种模式均读回一致且像素命中；开启 Name 降序、Size 分组后状态和菜单/选区保留，但目标颜色像素为 `0/0`。两组均只在隔离原型运行，产品 ExplorerBrowser、文件操作、真实输入和排序/分组着色仍未验收。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-shell-模式后同步-listview-控件模式)。

## 2026-10-25：复核 Tab/Shift+Tab 焦点序列

- 对照 `PaneFocusStopMap` 与 `PaneControl` 路由，当前索引覆盖导航控件、地址栏、标签、可见目录树、详细信息列标题、文件内容及状态工具；`FocusTraversal.Move` 在停靠点索引层面是严格正反向遍历，并跨可见窗格循环。现有 `FocusTraversalSmoke` 对每个停靠点验证 Tab 后 Shift+Tab、Shift+Tab 后 Tab 均回到原点，并验证动态停靠点组合及列标题位置。
- 当前 Release `SettingsSmoke` 全量运行通过，未发现纯逻辑序列缺陷。这个证据不覆盖 WPF/原生 HWND 实际取得焦点的行为，因此此前报告的“Shift+Tab 回到列名而非原选中文件”仍需可见桌面实测；本轮未启动 GUI，也未改焦点生产代码。

## 2026-10-25：匹配布局后的排序与分组状态复验

- 隔离 `IShellView` 中预设 Name 降序与 Size 升序分组，再逐项同步切换 Icon、Details、SmallIcon、List、Tile。五种模式的 `IFolderView2` 排序/分组读回一致；项目数、背景菜单接口、选择状态与原生选中项核心菜单动词检查通过，结束时恢复原状态。Release NativeProbe 构建 0 警告/错误，私有 Desktop watchdog 退出码 0，主桌面前台未变。
- 此轮逐项通知存在但目标色像素采样 0/0，不能宣称排序/分组状态下颜色绘制成功。仍是隔离 Shell 接口证据，没有文件操作、用户输入、ExplorerBrowser、焦点/键盘、拖放或重启持久化验证；未改生产代码或 release。日志：`src/.state/native-probe-watchdog/20261025-182634-4f29cb01.stdout.log`。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-匹配布局后的排序与分组状态复验)。

## 2026-10-25：同步布局后的筛选、键盘与导航复验

- 隔离 `ICommDlgBrowser3` 筛选宿主中，显式指定图像尺寸后，Icon、Details、SmallIcon、List、Tile 五组 Shell/ListView 模式均读回匹配，两个符合筛选条件的条目保持可见。随后在 Details 模式程序化验证 Ctrl+A、Down、Enter、打开子目录、无效目标拒绝、上级、前进/后退和历史边界，全部通过；watchdog 退出码 0，主桌面前台未变。
- 这纠正了先前仅凭 HRESULT/ListView 模式推断 Shell 模式一致的不足。`SetViewModeAndIconSize(..., -1)` 在该宿主的小图标步骤读回 Icon/48；现在明确指定各视图图像尺寸并断言 Shell 和 ListView 两侧状态。探针记录见[原生导航探针](native-navigation-probe.md#2026-10-25-匹配布局与筛选导航复验)。这仍是临时 `IShellView`/模拟浏览器宿主，不是产品 ExplorerBrowser 或物理键盘的可见验收；未修改生产代码或发布包。

## 2026-10-25：修正匹配视图模式和颜色结论

- 复查 `182400` 私有 Desktop 日志发现 Icon 的 Shell 读回为 `2/16`，ListView 为 `0`；该次不能证明 Shell/ListView 模式一致。NativeProbe 现明确指定每种模式的图像尺寸并新增等值断言。未排序/分组的五种模式在 `183131` 运行中全部读回匹配，且两种目标颜色像素均命中。
- 开启 Name 降序与 Size 分组后，五种布局下排序、分组、选区与核心 Shell 菜单仍保留；目标色像素此时为 `0/0`，颜色在排序/分组下仍未验证。Release 构建和两个私有 Desktop watchdog 探针均通过；没有产品视图迁移或 release 修改。详见[隔离颜色原型](ishellview-color-spike.md#2026-10-25-shell-模式后同步-listview-控件模式)。

## 2026-10-25：ExplorerBrowser DirectUI 宿主不具备原型 ListView

- 在隐藏 ExplorerBrowser 上附着产品 `ShellBrowserSite` 执行视图同步能力探针。当前视图子控件为 `CtrlNotifySink`、`DirectUIHWND`、`ScrollBar`，没有 `SysListView32`；因此原型的 `LVM_SETVIEW`/ListView 着色路径不能在产品宿主上使用。探针将其作为能力缺失报告后继续验证，八种 Shell 模式、排序/分组、列宽/顺序与多选状态均通过，退出码 0、前台未变。无同步的 ExplorerBrowser Shell 模式基线也通过。
- 该结果否定了“把隔离默认 `IShellView` 的 ListView 绘制方案直接接入 ExplorerBrowser”这条路径；并未否定 ExplorerBrowser 的 Shell 文件操作能力。下一步评估 DirectUI 的公开扩展点，以及独立 `IShellView` 宿主为达到产品 parity 必须补齐的导航、筛选、历史、菜单、通知和焦点行为。没有改生产宿主或生成 release。详见[原生导航探针](native-navigation-probe.md#2026-10-25-explorerbrowser-宿主与-syslistview-原型差异)。

## 2026-10-25：Shell 颜色原型的宿主边界与下一步

- 对照 Microsoft Learn、本机 SDK 10.0.26100.0 以及现有 NativeProbe：ExplorerBrowser 可配置/查询当前 Shell 视图并在 `OnViewCreated` 调整视图设置；文档化接口面没有提供将其现存视图替换为自定义 `IShellView` 的入口。SDK 的 `SFVM_*` 列表没有逐行颜色绘制消息。该结论限定于公开接口，不推断未文档化实现。
- 独立 `IShellView` 隐藏原型只验证默认 Shell 视图一部分能力。`StreamViewHost` 的菜单/状态/工具栏回调是空实现，控件消息未实现，默认导航不支持，视图流在内存中；因此不能作为生产宿主或 parity 证明。若后续评估迁移，需先补齐导航历史、菜单与工具栏、状态、Shell 快捷键、持久化、通知刷新、焦点、拖放和文件操作，并做可见验收。
- 当前维持生产 ExplorerBrowser，颜色功能暂缓实现；导入后黑底继续按独立像素对照方案定位。此轮只更新调查结论，没有修改生产代码或发布包。详见[Shell 颜色调查](shell-color-investigation.md#2026-10-25-explorerbrowser-与独立-ishellview-的公开扩展边界)。

## 2026-10-25：DirectUI 右键目标识别的公开 UIA 边界

- 路线图 P0 右键功能已在隔离 `IShellView` 的 `SysListView32` 上用 `LVM_SUBITEMHITTEST` 验证图标命中与列尾空白；产品 ExplorerBrowser 使用 DirectUI，不提供该 ListView 消息入口。进一步核对 Microsoft UIA 文档：`FromPoint` 不保证坐标位于元素可点击部分，`GetClickablePoint` 只返回一个可点击示例坐标，`TextPattern.RangeFromPoint` 返回最近文本范围，均不能单独证明任意右键坐标属于某个文件条目。
- 保持目标不明时省略依赖文件目标的自定义菜单项并保留 Shell 原生菜单，不放宽到整行 UIA `ListItem` 边界。下一步需在获准的桌面验收中检查 DirectUI 是否公开精确图标/文字子项范围，并验证行内空白、列尾空白及列表背景；若没有可验证的精确范围，该 P0 的自定义项覆盖面受限但文件目标安全性继续优先。
- 依据：[Microsoft `AutomationElement.FromPoint`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.frompoint?view=windowsdesktop-10.0)、[`GetClickablePoint`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-getclickablepoint)、[`TextPattern.RangeFromPoint`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-rangefrompoint)。

## 2026-10-25：Shell 更新 observer 的注册失败诊断补全

- 重新构建 Release HostProbe 后运行 3 次不创建 ExplorerBrowser 的 Shell 更新 observer：2 次成功投递 `SHCNE_UPDATEITEM`，1 次注册失败；新诊断保留 `sources=0x9003`、`events=0x7FFFFFFF`、`message=0x8021`、`entries=1`、`last-error=0`，不会打印临时路径或 PIDL/HWND 地址。此前脚本把注册失败参数全部折叠成单一计数，无法比较失败调用配置。
- 更新重复脚本输出后再次复跑得到同样的 2/3 成功与 1/3 注册失败；投递阶段没有失败。该证据只描述独立 observer，不代表生产 ExplorerBrowser 或产品文件变更 watcher；未调整产品通知/刷新策略，自动刷新 P0 与 STA 长尾仍开放。
- 验证：HostProbe Release 构建 0 警告、0 错误；脚本退出码 1 如实保留了间歇注册失败；后续需要继续查注册调用时序/生命周期，并在有明确授权时再运行生产宿主压力测试。

## 2026-10-25：修正排序/分组颜色原型像素断言

- `color-private-desktop` 复跑发现，颜色断言固定把蓝/绿文件对应到索引 0/1，而 Name 降序会改变 Shell 当前索引顺序。探针原本每轮都正确按当前 PIDL 身份重建颜色表，错误在像素验证仍使用固定顺序，导致排序/分组颜色被误报为 `0/0`。
- 现按当前索引颜色检查行矩形。名称降序与大小升序分组时，Icon、Details、SmallIcon、List、Tile 五种同步布局均收到两条逐项绘制通知并命中预期颜色；无排序/分组的五种布局也通过。Release NativeProbe 构建 0 警告/错误，私有 Desktop watchdog 退出码 0。该证据仅属隔离默认 `IShellView` 本机原型；ExplorerBrowser DirectUI、产品宿主 parity、选中/悬停状态和真实文件操作均未验证，产品代码及发布目录未改。
- 最终带失败断言的运行日志：`src/.state/native-probe-watchdog/20261025-184949-861101f3.stdout.log`。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-修正排序分组颜色像素断言)。

## 2026-10-25：产品纯逻辑回归与 Release 构建基线

- 从当前工作树运行完整 Release `ExplorerDirs.SettingsSmoke`，全部回归通过；随后 `ExplorerDirs.App` Release 构建通过，0 警告、0 错误。覆盖本机现有 Shell 状态门控、选择/焦点恢复、刷新重试、文件夹创建/命名、快捷键分发、Q-Dir 导入导出相关状态及配置持久化 smoke。
- 这次全量检查没有创建或操作 ExplorerBrowser 产品窗口，不能替代右键 DirectUI 命中、物理键盘/鼠标、真实剪贴板、跨窗格 OLE 拖放或 GUI 可见体验验收。没有生成发布包。

## 2026-10-25：Shell 隔离探针 Release 构建复核

- 完成全量 SettingsSmoke 和 App 构建后，继续构建 `ExplorerDirs.NativeProbe` 与 `ExplorerDirs.HostProbe` Release；两个项目均通过，0 警告、0 错误。至此本工作树的 App、SettingsSmoke、NativeProbe、HostProbe 四个项目均已完成 Release 构建检查。
- 本次只编译探针，没有运行 ExplorerBrowser/HostProbe 或用户桌面操作；因此不补充 Shell runtime、物理输入或 UI 验收结论，也未生成发布包。

## 2026-10-25：修正 `.qdr` 的 Unicode 标记重复判断

- 导入器字段表按不区分大小写去重，但 `UNICODE` 特例曾用大小写敏感比较。新增 `UNICODE=1` / `unicode=0` 输入回归后，全量 SettingsSmoke 按预期失败并输出重复字段警告；修正为大小写无关比较后，全量 Release SettingsSmoke 通过，App Release 构建 0 警告、0 错误。
- 仅修正元数据标记的重复诊断；普通字段重复仍保留“最后值生效并警告”的既有行为。没有运行 GUI 或生成发布包。

## 2026-10-25：隔离 `IShellView` 原生操作入口复验

- NativeProbe Release 构建 0 警告、0 错误，隐藏 watchdog 运行退出码 0；父窗口隐藏、前台未变、产品 ExplorerBrowser 未创建。背景的查看/分组/刷新/新建/属性/排序命令启用，选中项复制/删除/重命名/属性命令启用，`IFolderView2`、上下文菜单和 `IDropTarget` 查询成功；空剪贴板时粘贴不可用。
- 隔离临时目录中以 `IFileOperation` 实际复制、重命名、删除，并按路径 PIDL 和 `IFolderView` 项目数验证视图：三次完整 Release 探针的文件操作均成功，计数最终为 `2→3→3→2`；两次三类操作均自动更新，一次复制/重命名/删除均需显式 `IShellView.Refresh` 后视图才恢复。此三次小样本显示隔离隐藏宿主的自动更新存在间歇性，不能推断产品 ExplorerBrowser 故障率。NativeProbe Release 构建 0 警告、0 错误；探针退出码 0，前台未变，产品 ExplorerBrowser 未创建。未验证剪贴板粘贴、操作后选区/焦点、产品宿主或逐项颜色；产品代码及 release 未改。详见[隔离颜色原型记录](ishellview-color-spike.md#2026-10-25-路线图继续后的原生操作能力复验)。

## 2026-10-25：按用户选择继续隔离 `IShellView` 原型

- 隐藏 NativeProbe Release 构建及 watchdog 均通过；八种原生查看模式、背景/项目菜单、`IFolderView2` 和 `IDropTarget` 查询继续通过。原生 `IFileOperation` 复制、重命名、删除成功，视图项目计数按 `2→3→3→2` 恢复。
- 本次三类变更均未自动更新隐藏视图，显式 `IShellView.Refresh` 后恢复。加上此前三次样本（两次自动更新、一次需 Refresh），说明独立隐藏宿主的 Shell 变更观察行为仍有波动；不能外推 ExplorerBrowser 故障率。颜色探针仍无有效隐藏逐项颜色像素证据。
- 路线决策：继续在隔离原型中补测宿主行为，不接入产品文件视图。迁移前仍需证明可靠变更通知、键盘/焦点与选择、菜单路由、导航历史、拖放和可见颜色；本轮未改生产代码或 release。日志：`src/.state/native-probe-watchdog/20261025-190643-c3f7f7ac.stdout.log`。详见[隔离原型复验](ishellview-color-spike.md#2026-10-25-用户选择隔离原型后的复验)。

## 2026-10-25：隔离视图变更通知 A/B

- 隐藏 NativeProbe 在自然更新等待 5 秒仍未收敛后，补发标准 `SHCNE_CREATE`、`SHCNE_RENAMEITEM`、`SHCNE_DELETE` 路径通知，再等待 5 秒；三次运行的复制、重命名、删除均未由该通知收敛，但显式 `IShellView.Refresh` 全部恢复正确视图计数。Release 构建通过，NativeProbe 无错误退出。
- 该证据仅表明隐藏 `StreamViewHost` 原型不具备可靠的自动恢复体验。宿主未接入产品的 watcher/批次协调，结果不代表这些通知在可见独立视图或产品 ExplorerBrowser 中失效。下步在原型中复用/验证产品的监视与刷新协调，再评估焦点、选择及原生操作；不迁移产品视图、不生成 release。详见[通知 A/B 记录](ishellview-color-spike.md#2026-10-25-隐藏原型的变更通知-ab)。

## 2026-10-25：隔离宿主验证产品 watcher 与计数协调

- NativeProbe 链接产品 watcher delta queue、目录 change batch 与监视器选项；在隔离临时目录启用 `FileSystemWatcher`，仍只从 STA 消息泵读取 Shell 项目数、刷新并验证 PIDL 路径。补上首轮视图枚举等待，避免异步 DefView 初始化中的暂时 0 项导致测试误报。
- Release 构建 0 警告/错误，隐藏 watchdog 退出码 0。复制、重命名、删除的自然视图更新与 watcher 协调均完成，计数 `2→3→3→2`。当时刷新计数在操作结束后读取，无法归因到单项操作；“复制和重命名各两次”的旧记录已撤回。仍只证明隔离 NativeProbe 对产品纯逻辑组件的接线；未验证生产 ExplorerBrowser、watcher 溢出恢复或 GUI。未修改生产实现和 release。日志：`20261025-192311-c101b1fc.stdout.log`。详见[隔离宿主协调复验](ishellview-color-spike.md#2026-10-25-隔离宿主接入产品目录协调器)。

## 2026-10-25：对齐隔离探针与产品刷新顺序

- 事件诊断确认 `ShellDirectoryChangeBatch.Complete(actualCount)` 会在产品刷新启动前消费当前 `forceRefresh`。旧隔离 coordinator 没有这一步，因而在重命名刷新回调后保留 force 并重复 Refresh；这是探针失真，不是产品已确认的重复刷新缺陷。现探针顺序与 `ExplorerBrowserHost` 对齐，刷新期间的新 watcher 事件继续经 `CompleteRefresh(queue, ...)` 合并。
- 修正各项 Refresh 计数在最终汇总前读取，避免累计值错归到复制/重命名。最新隐藏运行中，复制、重命名、删除各调用一次 Refresh 后按预期收敛，NativeProbe Release 构建及 watchdog 通过；未改产品实现、发布包或可见 GUI。日志：`20261025-192556-8969ec29.stdout.log`。详见[原型刷新顺序复验](ishellview-color-spike.md#2026-10-25-按产品顺序消费刷新请求)。

## 2026-10-25：验证刷新重叠事件与原生选择快照恢复

- 隔离 `IShellView` 在 Refresh 期间接收新增文件后，批次最终校验合并 watcher `+1`，项目数 `3→4`，本轮无需额外 Refresh。Shell 重枚举未自动保留先前选择；复用生产 `ShellViewSelectionSnapshot` 后按项目身份恢复成功。隐藏窗口焦点结果为 false，仍不能代表可见键盘焦点。
- 首次恢复尝试未到回调，经查为隔离 coordinator 只在 500 ms 轮询期内观测到稳定才触发；正式批次对象可能在其后的 `CompleteRefresh` 最终计数检查中才确认成功。调整探针在成功 CompleteRefresh 后调用 settle 回调；NativeProbe Release 构建 0 警告/错误，隐藏 watchdog 通过。未改生产文件视图实现、发布包或 GUI。日志：`20261025-193300-a5dd5e63.stdout.log`。详见[隔离原型刷新重叠与选择快照](ishellview-color-spike.md#2026-10-25-隐藏原型刷新重叠事件与选择快照)。

## 2026-10-25：测量产品 Shell 刷新 fallback 前的自然收敛窗口

- 按自动刷新 P0 的后续调查项，HostProbe 新增可配置、仅探针使用的自然观察窗口；隐藏生产 ExplorerBrowser 中暂停该窗格 fallback 计时器并抑制本次应用 `SHCNE_CREATE`，记录 Shell 自然显示单文件复制的延迟，再恢复产品计时器验证漏更新仍会 fallback。没有改生产刷新策略。
- 复制：Release HostProbe 构建 0 警告/错误，六次 250/600/1200 ms 样本全部完成显示及清理。250/600 ms 窗口内未自然显示，但约 810 ms 后自然出现且无 fallback；1.2 秒窗口 4 次中 3 次在 745–748 ms 自然显示，1 次未收敛，恢复监视后触发一次 fallback 并最终可见。删除：一个 600 ms 和两个 1.2 秒样本在观察期内未自然移除，恢复监视后 fallback；另外两个 1.2 秒样本分别在约 453/400 ms 自然移除。复制观察期的应用 `SHCNE_CREATE`、所有操作的观察期 fallback 均为 0。样本量小且仅为单项本地文件操作，不能据此更改生产 fallback 时限；多项 burst 与重命名仍需测量。详见[自然收敛窗口调查](live-update-investigation.md#2026-10-25-单文件创建的自然收敛窗口探针)。
- 继续扩展 HostProbe 支持 2–5 项创建 burst 与重命名。Burst 2/600 ms 在窗口后约 1028 ms 自然收敛且无 fallback；5/600 ms 触发一次 fallback、约 1503 ms 可见；2/1200 ms 约 749 ms 收敛，5/1200 ms 两次约 1000/1002 ms 收敛。重命名在 600 ms 一次、1200 ms 三次完整轮中均未在窗口内自然更新；watcher 的 forceRefresh 被记录，恢复监视后各执行一次 fallback，改名、刷新完成与夹具清理通过。探针等待创建基线和异步刷新完成后才开始改名，排除了一次与前一刷新重叠的无效轮次；另等改名刷新结束后再清理。HostProbe Release 0 警告/错误；未改生产刷新时限。样本仍小且 burst 使用临时本机直写文件，不代表 Shell `IFileOperation` 批操作或大目录部分枚举；下一步用多项 `IFileOperation` 及较大分批变化验证。详见[自然收敛窗口调查](live-update-investigation.md#2026-10-25-多项创建-burst-与重命名观察)。
- 随后增加一次 `IFileOperation` 中排队多个 Copy/Delete 的批量助手，burst 上限 32，并支持每 8 项分阶段提交。单批 5/16/32 项在 1200 ms 内均未完整显示，之后约 1355/1378/1440 ms 收敛且无 fallback；600 ms 下 16 项一次触发 fallback、约 1531 ms 可见，32 项约 1426 ms 可见且无 fallback。32 项分阶段两轮的前 8/16/24 项阶段点仍显示基线 3 项、burst 可见数 0；最后一批后，1200 ms 样本在 watcher 后约 1031 ms 收敛，600 ms 样本约 2346 ms 总耗时收敛，均无需 fallback。watcher 数量和 Shell 批量删除清理通过；HostProbe Release 构建 0 警告/错误，无产品逻辑调整。仅限本机隐藏宿主和 32 项本地文件，不代表 50k 部分枚举或远程目录。下一步按交错顺序复测已有 50k 产品 watcher 与 native-only HostProbe，关联分段项目身份、fallback 和消息泵延迟；自动刷新 P0 仍开放。详见[多项 IFileOperation 与分阶段 burst](live-update-investigation.md#2026-10-25-多项-ifileoperation-与分阶段-burst)。

## 2026-10-25：用户选择后复跑隐藏 IShellView 颜色原型

- NativeProbe Release 构建 0 警告/错误，隐藏 watchdog 退出码 0。八种 Shell 视图、核心上下文菜单、Shell 接口查询、复制/重命名/删除和 watcher 刷新重叠检查通过；夹具计数 `2→3→3→2`，选择身份恢复成功。逐项颜色证据仍局限于隐藏宿主：21 次预绘制、17 次项目通知，但逐模式仅平铺 2 次；行矩形/裁剪为空，所有模式目标像素为 0。未操作可见 GUI，未创建产品 ExplorerBrowser，也未迁移生产视图。下一步仍需在之后获准的隔离桌面验收中判断真实屏幕绘制；当前不据隐藏结果决定颜色路线。详见[隔离 IShellView 颜色原型](ishellview-color-spike.md#2026-10-25-用户选择后的隐藏-ishellview-原型复跑)。日志：`src/.state/native-probe-watchdog/20261025-194945-acc069d4.stdout.log`。

## 2026-10-25：菜单 Undo/Redo 的安全验证边界

- 核对微软 `IFileOperation::SetOperationFlags` 文档：`FOFX_ADDUNDORECORD` 会将操作加入 undo 栈；Vista 及以后 undo 范围是当前用户会话，Explorer.exe 协调跨进程操作。先前 `False/False→True/True` 的对比已撤回：它把 Shell 操作前缓存的旧 `IContextMenu` 与操作后新取得的对象相比，不能证明 undo 栈改变。
- 新增不做文件操作的只读探针后，同一隐藏 `IShellView` 中旧菜单对象报告 `False/False`，随后两次重新取得的背景菜单都报告 `True/True`；父窗口隐藏、前台未变。由此确认原对比存在菜单对象时效混淆。原生 IFileOperation 使用的 `0x00800414` 不含 `FOF_ALLOWUNDO`/`FOFX_ADDUNDORECORD`，但实际 Undo/Redo 结果仍未验证；正式对照已改为操作前后都重新取得背景菜单，当前仅完成构建，未重跑会执行文件操作的样本。

## 2026-10-26：工具栏 Undo/Redo 命令状态只读复验

- 产品源码核对 `ExplorerBrowserHost.QueryFileCommands()` → `ShellFileCommands.Query()` → `OpenFileCommandMenu()`：每次查询都会重新取得当前 Shell 视图，并以 `GetItemObject` 创建新的 `IContextMenu`，没有跨刷新保留旧菜单对象。因此旧对象 `False/False` 与新对象 `True/True` 的差别不能归因于产品工具栏缓存或已发生文件操作。
- 为避免在用户桌面建立 Shell 视图，增加 Private Desktop 只读命令状态入口与 watchdog 项。NativeProbe Release 构建 0 警告/错误；`command-state-readonly-private-desktop` 退出码 0：旧对象 `False/False`，两次新取背景菜单均 `True/True`，没有文件操作，父窗口隐藏、前台未变。复跑日志：`src/.state/native-probe-watchdog/20261026-005206-575555af.stdout.log`。
- 结论仅限菜单对象的状态查询差异；不证明 Undo/Redo 操作可用性，也没有执行命令。要确认真实撤销栈变化，仍须在一次性 Windows 用户会话中对隔离测试文件做有界操作，再以每次新取得的菜单状态对照；本轮不触碰共享用户会话的撤销栈。
- 若验证实际撤销/重做磁盘结果，应在一次性 Windows 用户会话中执行；不得只用私有 Desktop 或另一个进程代替用户会话隔离。日志：只读校准复验 `src/.state/native-probe-watchdog/20261025-195815-b60b9fbd.stdout.log`，旧对象/新对象对比 `src/.state/native-probe-watchdog/20261025-195451-af59a9d2.stdout.log`。
- 依据：[Microsoft IFileOperation::SetOperationFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)。

## 2026-10-25：隔离颜色原型保留 Shell 选中状态

- 按路线图继续补齐 `IShellView` 颜色原型：先新增合成 `NM_CUSTOMDRAW` 回归，要求 `CDIS_SELECTED` 项目返回 `CDRF_DODEFAULT` 并保留原文字/背景色字段。未修复时隐藏探针按预期失败；修正后 Release 隐藏探针退出码 0，合成契约、八种视图、Shell 菜单、隔离文件操作和 watcher 刷新检查通过。父窗口隐藏、前台不变、产品视图未启动。
- 原型现在只为未选中项目写入规则颜色，选中项目交给 DefView 默认绘制。该测试只证明探针的回调分支符合预期，未验证真实可见的选中高亮、焦点框、悬停或排序/分组下逐项颜色；不据此迁移生产 ExplorerBrowser，也未生成 release。详见[选中项颜色保留契约](ishellview-color-spike.md#2026-10-25-选中项颜色保留契约)。

## 2026-10-25：Private Desktop 确认隔离 IShellView 逐项颜色可绘制

- Private Desktop 回调曾把所有行的 `uItemState` 标成 `CDIS_SELECTED`（0x201），但 Shell 选区数和 ListView `LVIS_SELECTED` 都为零。探针改为在绘制前读取 ListView 实际选中索引；否则会错误地把所有项目都交给默认绘制。
- Release NativeProbe 构建 0 警告/错误，Private Desktop watchdog 退出码 0，主桌面前台不变。默认 `IShellView` 中 Icon、Details、SmallIcon、List、Tile 五种同步模式的未分组行颜色像素通过；Name 降序与 Size 分组下五种模式也命中两种目标色。Icon 模式实际选中第一项后，该行没有规则色像素、未选中行仍有规则色像素；其余四种模式的选中态也通过像素断言。八种原生模式、Shell 文件命令、刷新重叠与选择快照检查通过。
- 这是隔离默认 `IShellView` 在私有 Desktop 的真实绘制证据，确认该宿主具备五种标准模式的逐项着色路径并能保留选择视觉；不能外推到产品 ExplorerBrowser 的 DirectUI、Content/自定义 Q-Dir 模式、悬停/焦点、其它 Shell provider 或用户桌面。产品文件区不迁移，release 未改。下一步先把颜色注入与选区快照做成独立、可测试的原型组件，随后评估独立宿主缺少的 ExplorerBrowser 导航/筛选/状态/生命周期契约，再决定是否值得继续迁移研究。记录：[IShellView 颜色复验](ishellview-color-spike.md#2026-10-25-defview-选择状态标志与同步视图颜色复验)。日志：`src/.state/native-probe-watchdog/20261025-200549-dde38c4f.stdout.log`。

## 2026-10-25：颜色决策组件化及独立 Shell 宿主迁移缺口核对

- 将真实 ListView 选中索引快照与颜色决策抽入 NativeProbe 独立组件，并增加 `--shell-view-color-policy-smoke`。纯 smoke 使用 `LVIS_SELECTED` 状态矩阵确认选中项不覆盖系统绘制、普通行采用规则色、DefView `CDIS_SELECTED` 单独出现时不误抑制着色，未知项保留既有灰色回退。Release 构建与策略 smoke 通过（0 警告/错误，退出码 0）。
- 组件抽取后，隐藏颜色探针与 Private Desktop 颜色像素探针再跑均退出码 0；后者继续通过五种模式、排序/分组颜色和 Icon 选中项保留断言。没有启动 ExplorerBrowser 产品 GUI 或 HostProbe。
- 源码对照确认 `StreamViewHost` 的 BrowseObject、控制窗、状态文字及框架菜单回调是未实现或空实现；颜色基础宿主没有 CDB3。单独 `FilteredHost` 只覆盖两个临时目录的导航/历史/筛选。产品侧则依赖 ExplorerBrowser navigation events、`ShellBrowserSite` 的 CDB3/PaneVisibility、每标签 Shell TravelLog、筛选与 watcher/Refresh、选区焦点恢复、视图状态和 WPF 窗格生命周期。具体逐项差异和依据见[宿主契约对照](ishellview-color-spike.md#2026-10-25-独立视图宿主与-explorerbrowser-契约差异)。
- 颜色注入仍未接入产品；上述差异使其不是可直接替换的视图组件。下一步先给独立宿主形成分阶段设计/迁移门槛，优先保证 Explorer 功能不退化，再决定是否扩充原型；自动刷新 P0 的产品 HostProbe 压力样本仍受当前无 GUI/ExplorerBrowser 探针限制，暂不运行。

## 2026-10-25：隔离 `TranslateAccelerator` 不能单独证明 Shift 多选

- 为补查原生视图的 Shift 范围选择，在 Private Desktop 两项临时夹具中直接设置探针线程 Shift 状态后调用 `IShellView.TranslateAccelerator(WM_KEYDOWN, VK_DOWN)`。Shell 返回 `S_OK`、焦点从第 0 项移动到第 1 项，但选中数仍为 1/2；调用前 `GetKeyState(VK_SHIFT)=0xFF80`。向隐藏 ListView 直接派发 Shift KeyDown 后结果相同。
- 该调用没有复现完整键盘消息队列/控件派发，不能据此认定原生 Shift 多选或产品快捷键失效。撤销了错误假设驱动的探针断言；此项仍待真实输入或完整消息循环隔离验证。NativeProbe Release 构建通过，但这次带试验断言的 watchdog 以退出码 1 结束，日志：`src/.state/native-probe-watchdog/20261025-201502-7628bbcc.stdout.log`。未修改产品代码，未操作主桌面，未运行 ExplorerBrowser 宿主。
- 方法边界详见[隔离原型记录](ishellview-color-spike.md#2026-10-25-shift-范围选择的合成加速器边界)。

## 2026-10-25：整理独立 IShellView 宿主迁移门槛草案

- 按此前路线图先形成[独立宿主分阶段迁移门槛](superpowers/specs/2026-10-25-ishellview-host-migration-gates.md)：比较保留 ExplorerBrowser、整体替换和双宿主混用，推荐在任何产品迁移前依次通过通用导航生命周期、Explorer 浏览器回调/快捷键、每标签视图状态、文件操作/实时刷新、真实拖放与可见验收，再验证颜色。
- 该文档是待审阅架构草案，不是迁移批准。阶段 0 已列出浏览器生命周期、导航、菜单、快捷键、筛选、视图、会话、文件操作/更新、焦点、拖放、多窗格和颜色等合同，并对应当前产品代码、隔离实现、可复用 smoke/probe 与缺少的可见证据。产品继续使用 ExplorerBrowser；本轮未改变产品代码或发布物。后续阶段 1 的通用导航扩展应在设计审阅后开始。

## 2026-10-25：隔离 IShellView 原型支持任意深度文件夹历史

- 阶段 1 的隔离 NativeProbe 先增加 `root → child → grandchild` 回归，原 `FilteredHost` 对合法孙目录返回 `E_NOTIMPL`；扩展后通过当前 PIDL 解析 `SBSP_PARENT`、按当前 PIDL 组合选中子项，并在临时夹具中验证孙目录、父级、Back/Forward、历史索引与每个新 view 的 Shell 项目数。
- NativeProbe Release 构建 0 警告/错误，Private Desktop watchdog 退出码 0；前台未变化，夹具退出后清理。此项仅改善和证明隔离默认 `IShellView` 的多层 filesystem 导航探针，不等于产品 ExplorerBrowser、虚拟位置、Search Folder、Tab/四窗格或真实焦点验收；生产代码与 release 未改。详见[隔离原型记录](ishellview-color-spike.md#2026-10-25-隔离宿主任意深度文件系统导航)。
- 已补历史分支截断、Shell 桌面父级边界和失败导航保留回归：卷根可继续进入 Shell 父级，桌面根无父项；非文件夹目标失败不会替换旧 view/PIDL/历史。每种虚拟位置已改为独立 Private Desktop 进程，Desktop、This PC、Recycle Bin、Libraries、Network Places、ZIP view 和 Search Folder view 均逐个创建成功，背景菜单/QueryContextMenu 与 IDropTarget 查询成功。核心菜单状态采样记录了当前 Shell provider 的 enabled/unknown；Search Folder 本轮无搜索结果，ZIP 的 GetCommandString 仍跳过。真实 DoDragDrop 需要活动指针位于目标窗口，当前非活动 Private Desktop 不适合注入用户鼠标状态；后续需隔离且可接收输入的测试会话，仅对临时 filesystem fixture 验证，不调用虚拟位置的 Drop。产品迁移仍需草案审阅及完整阶段门槛。

## 2026-10-25：隔离宿导航、虚拟位置与筛选全路径复跑

- NativeProbe Release 构建 0 警告/错误；Private Desktop 全路径 watchdog 退出码 0。临时 filesystem fixture 的过滤、分组后视图导出、Icon/Details/SmallIcon/List/Tile 同步、合成 Ctrl+A 加速器、子项进入、非法文件目标失败保留、父级/Back/Forward、孙目录导航和历史分支截断均通过；Shell 桌面根与卷根父级边界也符合预期。
- Desktop、This PC、Recycle Bin、Libraries、Network Places、Search Folder 与 ZIP provider 均创建了隔离 `IShellView`；各自的内存背景菜单 `QueryContextMenu` 与 `IID_IDropTarget` 查询成功。当前运行的 Search Folder 结果数为 1（此前独立运行有 0），因此只确认 view 可创建，不能承诺搜索结果收敛稳定。菜单命令只采样，没有调用；ZIP 的命令名仍跳过。
- 父窗口未显示，前台窗口未变，临时 fixture 正常清理。证据日志：`src/.state/native-probe-watchdog/20261025-204901-c2495f2b.stdout.log`。Ctrl+A 是探针内合成消息，不是物理按键或产品焦点验收；`IDropTarget` QI 不等于 OLE `DoDragDrop`。实际拖放仍需独立可接收输入的测试桌面，当前未注入输入。

## 2026-10-25：隔离 Shell 背景菜单命令执行

- NativeProbe 新增 Private Desktop 菜单命令探针，仅操作唯一临时 filesystem fixture。先从背景 `IContextMenu` 读取 canonical verb，再用 `IContextMenu::InvokeCommand` 执行 `viewlogicaldetails` 与 `refresh`；视图模式由 Icon 改为 Details，`IFolderView2` 读回 `mode=4/iconSize=16`，Refresh 经 STA 消息泵等待后恢复两个可见项目。
- 首轮测试错误假设临时 Shell view 初始非 Details；实测默认即为 Details，修正为探针显式设为 Icon 后重跑。随后即时读取 Refresh 结果曾短暂为 0 项；按 Shell 异步重枚举使用 3 秒有界消息泵等待后，菜单命令回归通过。该现象记录为刷新异步性，不更改产品刷新策略。
- Release NativeProbe 构建 0 警告/错误；watchdog 连续 3 次退出码 0（日志 `20261025-205241-50b8e01f.stdout.log`、`20261025-205324-1f6cb164.stdout.log`、`20261025-205327-017699aa.stdout.log`）。Private Desktop 父窗口未显示、前台未变、每次临时夹具均清理。证明的是隐藏隔离 filesystem `IShellView` 对两个无破坏性背景菜单命令的调用及状态读回，不证明可见菜单、项目菜单/扩展命令、产品 ExplorerBrowser 或真实输入行为。

## 2026-10-25：隔离排序与分组菜单命令复测

- 扩展同一 Private Desktop 临时夹具：先设置 Name 升序并调用原生 `sortdescending` / `sortascending`，再设置 Type 分组并调用 `groupdescending` / `groupascending`；`IFolderView2` 对列 key、方向、分组 key 和方向的读回均符合预期。Release NativeProbe 构建 0 警告/错误，三个 watchdog 运行中两次完整退出码 0，一次在所有命令与夹具清理完成后的外层前台对照返回 `foreground-unchanged=False`；该次无法区分桌面切换来自同步用户活动还是窗口/COM 清理。
- 因前台边界出现一次不确定结果，本轮停止继续启动原生 Shell view 探针；没有发送输入、显示父窗口或修改产品代码/发布物。日志：通过 `20261025-205506-20909c52.stdout.log`、`20261025-205511-085cac55.stdout.log`；前台对照失败 `20261025-205514-7fa1b1f1.stdout.log`。后续先继续纯源码与 smoke 工作；需要新一轮 Private Desktop 重测时，应先确保桌面归属清楚，并将前台快照细化到每条菜单命令和清理边界。

## 2026-10-25：Shell 通知 observer 生命周期探针夹具修正

- 早期重复探针只给新建临时文件发送 `SHCNE_UPDATEITEM`，没有先发送并确认 `SHCNE_CREATE`。因此早期通知投递成功/失败数字无效；`SHChangeNotifyRegister` 的直接返回结果仍可记录，但不能用旧数据支持进程周转或固定 settle 延时结论。
- 修正后，单一 observer 连续处理 12 个临时项目的 CREATE→UPDATE 为 12/12；同进程同 HWND 和同进程每轮新 HWND 的 0/300 ms 交错组都各 12/12。分开顺序组波动较大，包含注册失败、CREATE 漏投和 UPDATE 漏投；原始单次 smoke 也曾在 CREATE 阶段超时。当前只能确认 native-only observer 测量具有时段波动，不能据此断定 ExplorerBrowser 产品 watcher 丢失变化或为产品设置 settle 延时。
- HostProbe Release 构建 0 警告/错误，所有完整记录轮次的前台守卫通过，没有创建 ExplorerBrowser 或改产品通知/刷新策略。详细修正、有效数据与未决边界见[自动刷新调查](live-update-investigation.md#补充已知-shell-项目上的-create--updateitem-生命周期对照)。

## 2026-10-26：焦点恢复使用明确的 Shell 视图计数范围

- `ReadFolderItemFocusState` 原先以 `IFolderView.ItemCount(0)` 判断文件区是否为空。SDK 将 `0` 定义为 `SVGIO_BACKGROUND`、`2` 定义为 `SVGIO_ALLVIEW`；隐藏 stock `IShellView` 探针中两者当前都返回 4，但这不足以把背景范围当作“当前视图总项数”的契约。
- 将总项数读取收敛到 `ShellItemCounts.ReadAllViewTotal`，焦点判断明确传入 `SVGIO_ALLVIEW`；计数失败时保持未知状态，空视图仍不报告文件项焦点。SettingsSmoke 覆盖传入范围、空视图和失败计数；完整 SettingsSmoke 与 App Release 构建通过，0 警告/错误。
- 这是 API 范围语义修正，不声称已复现或单独修复多窗格方向键失焦问题。未操作可见 GUI、未运行 ExplorerBrowser HostProbe、未生成 release；真实键盘焦点和多窗格行为仍待后续桌面验收。

## 2026-10-26：继续自动刷新 P0 压力调查

- 50,000 项无追踪 product 轮创建/删除约 29.9/20.3 秒，Shell 可见及 watcher/observer 事件计数完整，最终四窗格计数、选区和焦点读回通过；此前高追踪轮的约 117 秒空档在无追踪轮没有复现，但目前仅有单轮，不能作因果结论。
- HostProbe 的 `direct-select` 要求是 trace-only 证据，现仅在 Shell callback trace 启用时参与退出判定，实际 selection/focus 行为门槛仍保留。Release HostProbe build 通过，0 警告/错误。
- 修正后首个 10,000 项无追踪轮因批量删除后的选区断言失败（`True/False`）返回 1；同配置重复运行 `True/True`、退出码 0，50,000 项轮也曾通过同一断言。登记为间歇性选区恢复问题，继续追踪 snapshot 与 refresh restore 时序，不放宽行为门槛。完整记录见[自动刷新调查](live-update-investigation.md)。
- 本轮没有操作可见 GUI、没有发布；自动刷新 P0 未完成。

## 2026-10-26：减少 Shell 瞬时空选区覆盖恢复快照

- 在输入代次未改变时，Shell 的空选区 callback 不再立即移走已确认快照；异步恢复操作使用副本。用户发生新输入时仍清除旧快照。新增 SettingsSmoke 先红后绿，HostProbe/App Release build 0 警告、0 错误。
- 修复后两轮 10,000 项 product 压力场景的选区/焦点读回均通过，watcher 与 observer 事件完整；进程仍因独立的 128 文件 `batchDeleteVisible` 门未在 10 秒内收敛而返回 1。之后 bulk-delete fallback 收敛到 3 项。继续调查该批次刷新时序并增加选区恢复重复样本，当前还不判定 P0 完成。
- 没有可见 GUI 操作或发布；完整数据见[自动刷新调查](live-update-investigation.md)。

## 2026-10-26：自动刷新保留批量删除目标

- 定位 128 项删除未收敛原因：watcher 已累计 `-128` 并得出正确目标数，但瞬时空视图恢复分支在稳定旧计数后重设基线，丢弃了删除目标。现在首次稳定旧计数会保留目标并安排一次有界重试；二次失败仍回到原兜底，避免无限刷新。
- 删除文件夹身份扫描增加目标计数约束：仅当实时与逻辑目标视图都在 1–512 项内才扫描，避免 Shell 瞬时 0 项使大目录进入全视图扫描。新增状态机和边界 smoke 通过。
- SettingsSmoke 全量通过，App/HostProbe Release build 0 警告/错误。隐藏 product 压测 10k、50k 均退出码 0，批量删除目标可见、选区/焦点和事件数通过；50k 创建/删除约 31.8/20.5 秒，最终四窗格 `50003/3`。详细日志与限制见[自动刷新调查](live-update-investigation.md)。
- 该组隐藏测试门通过，但仍需其它 Shell provider 与可见交互验收；没有 GUI 操作或发布，自动刷新及全项目路线图保持开放。

## 2026-10-26：拆分自动刷新压力测试的 trace 负载

- 10k 与 50k product 场景分别运行 message-pump-only、Shell callback-only 和双 trace。两种单 trace 在 50k 的最大 Dispatcher 间隔为 432/255 ms；双 trace 为 1,304 ms，消息泵最大空档 217 ms，所有产品收敛/选区/焦点及事件门通过。旧轮约 117 秒长尾未复现，当前证据不足以定根因。
- 诊断 FileSystemWatcher 在一个 10k trace 样本少记 1 个 create 且无 error，产品 watcher 仍是 `10,001/10,001`；这项独立 observer 不作为产品丢事件证据。
- 修正 HostProbe 文件夹删除 gate，允许身份查询数为 0 且视图已自然 settled 的路径，并输出 identity-result；双 trace 10k/50k 复测退出码 0。App/HostProbe Release build 0 警告/错误。
- 证据与各轮日志见[自动刷新调查](live-update-investigation.md)。所有数据来自隐藏本机 ExplorerBrowser，未覆盖可见输入或其它 Shell provider；自动刷新 P0 与路线图继续开放，无 release。
- 同配置第二轮 50k 双 trace 也通过：ContextIdle/Input/Normal 最大间隔 336/336/198 ms、消息泵空档 169 ms。最大间隔窗口内一个 `SHELLDLL_DefView 0x4B0` 调用约 138 ms，Dispatcher operation overlap 9.3 ms，只解释部分空档。旧 117 秒长尾两轮均未重现，作为历史单次异常保留；不据此调整生产策略。

## 2026-10-26：隔离 IShellView 宿主复用产品浏览器回调

- `FilteredHost` 的 `OnStateChange`、`Notify`、默认菜单文字、视图标志、列点击、当前筛选及 view 创建前回调现在委托给产品 `ShellBrowserSite`；`IncludeObject` 继续使用隔离夹具的筛选器，导航/默认命令继续走探针宿主。
- NativeProbe Release build、完整 SettingsSmoke 和隐藏 `--ishellview-host-filter` 集成探针通过，探针退出码 0；覆盖筛选、导航/历史、合成 Ctrl+A/方向键、五种视图模式及多个虚拟 Shell provider。未启动可见 GUI或发布；这仍是隔离探针，不能证明产品宿主替换、真实键盘输入或可见菜单行为。
- 新增运行时回调证据：隔离 `IShellView` 的 Ctrl+A 同时选中两项，并令 `ShellBrowserSite.SelectionVersion` 递增（最新轮从 5 到 8）；因此不只验证宿主能编译，也验证真实 Shell 选择变化经过委托到达产品回调实现。该证据仍限于隐藏 fixture 和合成加速器。
- 新增普通文件夹菜单目标检查：先在 fixture 中只选中 `child` 文件夹，再通过 `IShellView.GetItemObject(SVGIO_SELECTION)` 查询 Shell 原生 `IContextMenu`，确认 canonical `open` 动词存在后清除选择；查询未调用命令、没有弹出菜单。隐藏探针退出码 0，父窗口保持隐藏、前台不变。
- 扩展目标隔离检查：`SVGIO_SELECTION` 的普通文件菜单含 canonical `copy`，而 `SVGIO_BACKGROUND` 的背景菜单含 `refresh`；和文件夹 `open` 断言一起通过。三类目标都只查询菜单，不调用命令，原生分界一致。
- 菜单命令焦点探针在较早轮次观察到原生 `Refresh` 丢失 focused item、排序清除选区；最新复跑则 Refresh、排序及分组均保留了列表和项目焦点/单项选区。因样本结果不一致，尚未确认稳定触发条件；恢复路径原型仍按 PIDL/隔离夹具唯一名尝试恢复，但不作为产品 `NativeShellCommandMenu` 或 ExplorerBrowser 集成完成证据。后续需固定消息泵和状态前置条件复测。
- 核对 Windows SDK 的 `SVGIO` 定义后确认，`IShellView::GetItemObject` 只有背景、选择、全部项目、已勾选项范围，没有列标题专属菜单对象；`IColumnManager` 公开列信息/顺序/可见性，不返回菜单。隐藏 Private Desktop 探针读回 Details 中 `SysHeader32` 及 4 个 Shell 可见列，数量一致；项目也已把 `SysHeader32` 排除在自定义文件/背景命令注入之外。列控件存在与列状态可读已证实，实际列头右键菜单未证实。参考 [SVGIO SDK 定义](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)、[IColumnManager 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icolumnmanager)。
- Private Desktop 探针向实际 `SysHeader32` 发送 `WM_CONTEXTMENU`，用线程级 `WH_CALLWNDPROCRET` 捕获 Shell 初始化后的 popup 并立即调用 `EndMenu`，未等待用户输入；最新轮截获本机本地化标题，与单独查询的背景 `IContextMenu` 有 16 个标题完全相同，包含“新建文件夹、查看、排序方式、分组依据、刷新”等。列头菜单中若干 submenu 根项 ID 为 `UINT_MAX`，且同标题不证明命令目标相同；因此目前确认的是 Shell 菜单内容重叠，不是完整的 canonical verb 路由同一性。菜单已取消，未完成可见检查或真实鼠标命中验收。参考 [WM_CONTEXTMENU 文档](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-contextmenu)。
- 对照探针进一步尝试把实际 popup 菜单 ID 与 `SVGIO_BACKGROUND` 的 `IContextMenu::GetCommandString` 映射比较：根菜单中的“查看/排序方式/分组依据”属于 submenu 项，`GetMenuItemID` 返回 `UINT_MAX`；直接查询对象按 `firstId=1` 分配的 canonical `view` ID 为 24，与 popup 根项无法直接对应。删除了这个错误的等价断言，改为比较两条路径的本地化菜单标题交集（本轮 16 项），仍把 canonical command target 标记为未确认。
- Program Starter 的独立 `ProgramStarter.ini` 与 Q-Dir `[Program]` 导入预览/显式应用已符合本轮确认的配置方向；SettingsSmoke 覆盖 UTF-8/UTF-16/活动代码页输入、歧义行跳过、预览不执行以及配置往返。本次未改动或读取写入用户的 Q-Dir 配置文件。

## 2026-10-26：映射列标题菜单的标准命令与三个子菜单

- 隔离 Private Desktop 的 `SysHeader32` 收到 `WM_CONTEXTMENU` 后，线程级钩子捕获 Shell 菜单并在关闭前向同一 owner 发送 `WM_INITMENUPOPUP` 初始化子菜单。刷新、粘贴、撤消、重做四个叶命令，以及“查看、排序方式、分组依据”内分别 9、6、7 个可见子项，均与 `SVGIO_BACKGROUND` `IContextMenu::QueryContextMenu` 的同标题项保持 `actualId - queriedId = 30976`。根子菜单的 canonical verb 为 `view`、`arrange`、`groupby`；五次隐藏执行都观察到相同映射，最新一轮增加了逐项一对一断言并通过，前台不变。探针按 canonical verb 找到已本地化的子菜单标题，未依赖固定中文 caption。
- 这确认本机当前默认文件夹 Shell view 的列标题菜单与背景 `IContextMenu` 在上述命令项及命令 ID 基址上的对应关系，仍不证明菜单可见交互、用户鼠标命中或产品菜单合并完成。此前仅标题交集的检查不再是当前证据终点。菜单 popup 被取消，未启动产品窗口或发布；探针：`dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --ishellview-menu-command-private-desktop`。Shell 扩展会改变其它菜单项和编号，验证断言只要求这三组标准子菜单共享本轮查得的同一基址。

## 2026-10-26：稳定采样隔离 Shell 排序/分组后的焦点

- 焦点探针先前在项目数恢复且 PIDL 可查询时就停止等待。新采样连续三轮观察到排序/分组会短暂呈现空视图或焦点/选区不匹配，随后于 150–200 ms 内连续三次（间隔 50 ms）稳定到目标焦点和单项选择。Refresh 的三轮复测均在 +0/+50 ms 看到 0 项，分别于 +100/+100/+150 ms 恢复到 2 项、目标焦点和单选状态。探针现要求连续三次稳定后才判断原生命令结果或调用恢复兜底；最新三轮完整执行均退出码 0，没有进入焦点恢复兜底。
- 这解释了旧探针的部分焦点结果差异，并避免恢复代码介入 Shell 尚未完成的视图重建。结论限于隐藏默认 filesystem `IShellView`，不证明 ExplorerBrowser 产品窗口或可见交互；生产菜单/焦点实现没有据此修改。完整时间线见[隔离原型记录](ishellview-color-spike.md#2026-10-26-原生命令后的焦点采样稳定性)。

## 2026-10-26：复核 Program Starter 配置决定

- 用户确认采用 profile 独立 `ProgramStarter.ini`，并支持导入 Q-Dir `[Program]` 程序项。源码已实现该方向：导入先预览、明确应用后才写入本项目配置，不写回 Q-Dir.ini；参数通过隔离进程验证边界。
- 本轮复跑完整 Release SettingsSmoke（`Settings smoke: PASS`）及 App Release build（0 警告、0 错误）。未启动 GUI 或用户配置程序，未读取/改写用户 Q-Dir.ini，也未发布。
- 后续仍需桌面验收启动器菜单、焦点、编辑及导入预览交互；真实 Q-Dir 文件互操作样本由隔离 fixture 覆盖格式，不等于用户配置导入验收。

## 2026-10-26：隔离导航明确拒绝新浏览器请求

- 复核 Windows SDK `ShObjIdl_core.h` 中 `SBSP_NEWBROWSER=0x0002`、`SBSP_SAMEBROWSER=0x0001` 后发现，单视图隔离宿主会把 `NEWBROWSER` 请求继续交给普通导航回调，造成请求“新浏览器”却替换当前视图的语义降级。
- `ShellViewNavigationHistory` 现对 `SBSP_NEWBROWSER` 返回 `E_NOTIMPL`，在任何 PIDL 暂存或导航前拒绝；`SBSP_SAMEBROWSER` 仍保留并交给当前视图回调。先加入回归时按预期失败，修复后定向 `--shell-view-color-policy-smoke` 通过。
- 完整 Release SettingsSmoke 通过；App Release 构建 0 警告、0 错误；相关 `git diff --check` 通过。只运行无 Shell view 的状态/COM smoke，未创建 `IShellView`、ExplorerBrowser 或 GUI；产品文件视图与发布物未改。
- 隔离宿主尚无创建第二个浏览器窗口的生命周期合同，因此明确拒绝比静默在当前视图打开更符合 Shell 请求。新浏览器、多窗口路由能力仍未实现，不能据此宣称阶段 1/2 或产品 Shell parity 完成。

## 2026-10-26：隔离导航拒绝 SDK 明确不支持的浏览模式

- 继续按 Microsoft `IShellBrowser::BrowseObject` 合同核对标志，确认 `SBSP_HELPMODE` 与 `SBSP_KEEPSAMETEMPLATE` 被文档明确标记为不支持；此前隔离单视图宿主会把它们降级成普通目录导航。现在与 `SBSP_NEWBROWSER` 一样，在 PIDL 暂存和导航回调前返回 `E_NOTIMPL`。
- 新增三类标志回归，断言请求被拒绝、导航委托调用数为 0、当前位置和历史保持不变；`SBSP_SAMEBROWSER` 仍转发并成功推进历史。三例先因 HELP/TEMPLATE 请求仍被普通导航处理而失败，拒绝策略后定向 smoke 通过。
- `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --shell-view-color-policy-smoke` 通过；完整 Release SettingsSmoke 通过；App Release 构建 0 警告、0 错误。该验证没有创建 Shell view、ExplorerBrowser 或 GUI，产品文件视图没有改动。
- 合同来源：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)。隔离宿主仍不创建新浏览器或探索模式窗口；这些功能不因显式拒绝策略而视为实现。

## 2026-10-26：隔离导航处理目录树模式请求

- Microsoft `BrowseObject` 文档指出，`SBSP_EXPLOREMODE` 要求新浏览窗口带目录树；如果当前浏览器模式不匹配会打开新窗口。隔离宿主没有导航树或新浏览器生命周期，原先会在当前无树视图里继续导航。现在没有 `SBSP_SAMEBROWSER` 时拒绝此请求并保持当前位置/历史；明确 `SAMEBROWSER` 时按 SDK 规则忽略模式位、继续当前视图导航。
- 新回归先因 ExploreMode 被错误当作普通导航而失败；接入模式门控后，拒绝与 SAMEBROWSER 覆盖两条路径均通过。`OPENMODE` 表示无树模式，与当前隔离宿主匹配，仍保持既有转发。
- 定向 `--shell-view-color-policy-smoke`、完整 Release SettingsSmoke、App Release build（0 警告/错误）通过。仅运行无 Shell view 的托管 smoke；未创建 ExplorerBrowser/IShellView/GUI，产品视图未改。
- 依据：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)。

## 2026-10-26：避免 Q-Dir 程序参数导入时误识别工作目录

- 源码复核发现，Q-Dir `[Program]` 行以 `=` 分隔字段；参数中若出现未引用等号，如 `--option=value`，单靠字段分隔符无法判断它是参数内容还是第四字段。旧解析会把 `value` 静默保存成工作目录，改变命令含义。
- 新增回归先失败，再让导入器仅在第四字段具有明确路径形态、Q-Dir 占位符或引号时接受；无法辨别的行显示“无法唯一识别”原因并跳过。覆盖 `%curdir%`、带引号工作目录、未加引号相对路径工作目录、带引号等号参数和歧义参数。
- 定向 Q-Dir Program importer smoke 与完整 Release SettingsSmoke 均通过；App Release build 0 警告、0 错误，`git diff --check` 通过。此修正只处理配置解析，不运行用户命令、不读写 Q-Dir.ini；GUI 导入预览和真实含启动项的 Q-Dir 文件仍待桌面验收。

## 2026-10-26：查看方式与地址位置下拉

- 将现有每标签查看方式菜单增加到窗格导航工具栏，继续复用 Shell 查看方式切换和状态栏入口。现代地址栏保留独立“编辑”按钮，经典地址栏继续可点击编辑；地址右侧新增位置下拉，列出当前路径及所有上级，再列桌面、用户目录、下载、文档、此电脑、库、网络、控制面板和回收站。地址历史仍使用独立按钮。
- 新增纯状态回归覆盖当前目录到盘符根目录的顺序、常用位置分区和虚拟 Shell 位置；路径位置按钮加入 Tab 顺序，保持地址编辑、位置下拉、历史下拉各自可键盘访问。`--address-path-menu-smoke` 与 `--focus-traversal-smoke` 通过，App Release 构建 0 警告/错误，相关 `git diff --check` 通过。首次完整 SettingsSmoke 在既有 Environment message smoke 的“隐藏消息目标不得改变前台窗口”断言失败；该 smoke 定向重跑及随后完整 Release SettingsSmoke 均通过，首次失败未能复现。未启动 GUI；菜单布局、系统图标及实际 Shell 位置导航待可见验收。
- 按用户要求打包 `releases/preview-0.6.0-temp.59`，隔离 profile；发布隔离 smoke 通过。产品版本 `0.6.0-preview.temp.59+77495f5d72a4739c2bb33a21d021328294f728c0`，EXE SHA-256 `C561B3B50F509760F823B0C71789570905CEB0E91414310DD8D9EC459770A7A7`，DLL SHA-256 `BFA91F50B668D370C9D62786EA6350F62989941FC25ECC450F450B9432EF0486`，与包内校验清单一致。没有启动发布程序；仍是待可见验收预览，不代表 GUI 验收或项目路线图完成。

## 2026-10-26：复验 128 项删除状态机修正

- 当前 Release HostProbe 连续两轮 `--live-shell-directory-stress-large 10000` 均退出码 0；128 项批量删除目标均确认可见，大视图文件夹身份扫描按门控跳过，最终四窗格计数 `10003/3`，选区及独立焦点恢复。刷新次数与耗时仍有波动；记录作为重复隐藏宿主证据，不据此关闭自动刷新 P0。详见[自动刷新调查](live-update-investigation.md#2026-10-26-再验-128-项批量删除与大视图身份扫描门控)。

## 2026-10-26：强化主题选中态对比度

- 主题调色板原先仅将强调色以固定比例混入控件背景，红色系统色等输入可产生与底色对比不足的选中背景。新增回归先按预期失败，再让浅/深主题的选中背景至少达到 3:1；调整仅向黑或白作满足门槛所需的最小混合，保持源强调色方向。选中文字继续使用黑/白中对比更高的一方，并验证至少 4.5:1。
- 覆盖红色、纯黑、纯白、灰色强调色与浅/深两主题的数值回归；完整 Release SettingsSmoke 通过，App Release build 0 警告、0 错误。未启动 GUI，无法据数值断言实际选中色块观感、原生 Shell 选择或高对比度可见效果。

## 2026-10-26：Shift+方向键隔离探针边界

- 为隔离 `IShellView` `TranslateAccelerator` 探针临时添加 Shift+Down 选区尝试。第一次 Private Desktop 运行中，Ctrl+A 正常、方向键焦点移动正常，单次 Shift+Down 返回 `S_OK` 但没有扩大选区；调整为“一个已选且聚焦的锚点”后，第二次运行反而在 Shift+Down 前 Ctrl+A 未选中任何项目。两轮父窗口均隐藏、前台未变，探针进程退出后清理 Private Desktop。
- 第三次运行先单独确认一项已选锚点，再调用带线程 Shift 键态的 `TranslateAccelerator`。Ctrl+A 与普通 Down 本轮通过；Shift+Down 返回 `S_OK` 并把焦点从第 0 项移到第 1 项，但选中数保持 `1/2`。这说明“仅有已选锚点”仍不足以令直接调用扩展范围；探针仍没有走真实键盘输入及 ExplorerBrowser/WPF 的消息泵派发。
- 第四次试验将 Shift/Down 按下与释放排入 Private Desktop 的原生列表窗口队列，按“`TranslateAccelerator`，若返回 `S_FALSE` 再 Translate/Dispatch”的顺序处理。Shift 返回 `S_FALSE`、Down 返回 `S_OK`，最终焦点在第 1 项而选区仍为 `1/2`。虽然探针内部父窗口隐藏且内部前台检查未变，Private Desktop 外层退出检查报告 `foreground-unchanged=False`，因此该轮隔离边界不合格；更早的一次全量宿主运行也在 45 秒 watchdog 超时后被终止。临时队列探针已撤回，没有据此改产品代码。
- 这些结果仍无法区分异步键态、Shell 完整输入处理和当前桌面变化，不是产品缺陷结论。不要继续注入键盘消息或把单次/队列合成结果当成用户可见行为；需待有明确桌面交接时验证真实多布局重命名与方向键。此限制不妨碍继续其它源码路线。
- 日志：`src/.state/native-probe-watchdog/20261026-171141-bec0d947.stdout.log`、`src/.state/native-probe-watchdog/20261026-171158-7fb9d5c1.stdout.log`、`src/.state/native-probe-watchdog/20261026-171519-680f9dd8.stdout.log`、`src/.state/native-probe-watchdog/20261026-172051-3f01b637.stdout.log`、`src/.state/native-probe-watchdog/20261026-172214-9e030845.stdout.log`。这些样本只用于界定探针，不计为 Shell parity 通过或产品失败。

## 2026-10-26：发布临时预览 temp.65

- 按先前预览发布要求，将当前 worktree 打包到 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.65`，应用放在 `app/` 并使用独立 profile。产品版本 `0.6.0-preview.temp.65+59bd93cea788f148b0414f28eee74d03a36153e5`；EXE SHA-256 `0A8D60B9A1EABEAA094DD133C2806BD7168F42F79BCCEE4AC5C310573B54B4F0`，DLL SHA-256 `EF852111FEC936BCED7C57EA795C418FE60742E0B8B2F240183ECD0A3EE5E5BC`，均匹配包内清单。
- Release SettingsSmoke 通过，App Release 构建 0 警告/错误，发布 profile fixture smoke 通过。隔离 profile 的两个配置文件与来源哈希一致；来源未修改。未启动应用或操作 GUI；主题外观、目录树同步、Shell 文件区和 Q-Dir 互操作仍待可见验收。详见[预览说明](releases/preview-0.6.0-temp.65.md)。

## 2026-10-26：补齐程序启动器窗口主题接线

- 源码盘点发现，程序启动器、启动项编辑及 Q-Dir 程序项导入预览是代码创建的独立 WPF 窗口；原先没有绑定窗口语义背景/文字，也没有随主题切换更新 Windows 标题栏。
- 新增共享窗口主题绑定器，并将应用主题控制器传给三个窗口。它们使用动态窗口背景/文字资源，监听主题变化刷新 DWM 标题栏颜色，并在关闭时解除监听；命令、文件视图和导入行为不变。PaneThemeMarkupSmoke 新增回归，先确认缺失时失败，再确认三窗接线与绑定器生命周期。
- 定向 PaneThemeMarkupSmoke 与完整 Release SettingsSmoke 通过；App Release 构建 0 警告、0 错误。未启动 GUI，实际主题切换和高对比度外观仍待可见验收；ExplorerBrowser 文件区黑底问题及 Q-Dir 文件颜色规则兼容仍未解决。
- 后续源码盘点又发现“保存查看预设”代码弹窗同样缺少主题接线；现已复用共享绑定器加入动态背景/文字与标题栏更新，smoke 先失败后通过。完整 SettingsSmoke 和 App Release 构建已复验；未启动 GUI，仍待可见验收。

## 2026-10-26：逐行文件夹大小列的 Shell 架构评审

- 用户选择先做原生 `IShellView` 架构评审。对照逐行大小调查与隔离宿主门槛后，建议原型尝试 `IShellFolder2` folder adapter + Windows DefView：这是比自绘列表更有希望保留原生菜单/项目语义的路线，但仍需独立浏览器宿主，不能直接插入 ExplorerBrowser，也没有产品迁移批准。
- 明确淘汰 ExplorerBrowser 的 `SetViewProperty`（数值不参与本机 Shell 大小排序、刷新会清除且接口已弃用）、旧 `IColumnProvider`，以及为单列重写完整 `IShellView` UI。迁移门槛涵盖导航/历史、文件菜单/操作、键盘焦点、每标签视图状态、实时通知、拖放、Shell provider 和可见验收。
- 下一步仅建议隔离验证静态列与数值排序，再评估原生文件项委托；不扫描递归文件夹、不改产品视图。当前 AGENTS.md 禁止以递归扫描填补属性缺失，逐行大小列实现前必须重新审阅此策略。

## 2026-10-26：复验隔离 IShellView 原型的文件操作与通知

- 按用户授权复跑隔离 Private Desktop `--ishellview-color-spike-private-desktop`。当前样本中，原生查看方式循环、Shell 背景/文件菜单及命令状态可读；临时 fixture 的复制、重命名、删除均在文件系统和 Shell 视图中收敛，目录 watcher 与刷新重叠期间的新增项也最终同步。选择、焦点和刷新后恢复快照通过，探针报告前台句柄未变化。
- 逐项颜色本轮没有通过像素验收：同步切换布局的自定义绘制采样未命中规则色像素，排序/分组状态也没有有效项目绘制像素。因此保留“特定隔离样本已出现可着色像素”这一历史证据，但本轮不能将逐项着色列为稳定能力，更不能推导为产品体验已具备。
- 完整 Release SettingsSmoke 与 App Release build 通过（0 警告、0 错误）。该探针仍是独立原生宿主，不等同于 ExplorerBrowser 产品宿主；没有验证完整 OLE `DoDragDrop` 输入循环、产品每标签会话持久化、键盘物理焦点或可见产品 UI。迁移门槛继续开放，产品文件视图没有改变。

## 2026-10-26：隔离宿主同会话恢复 Shell view-state stream

- `StreamViewHost.GetViewStateStream` 现将最近一次 Shell 写入的数据用于同一 host 后续的读取；初次读取仍返回调用方传入的初始状态流。新增 `--shell-view-state-stream-smoke`，通过 Windows 内存 IStream 的实际 Write/Read 验证同 host 往返；用例先因返回固定初始字节失败，修复后通过。
- 构造器现在复制初始字节：两个 host 即使接到同一个输入数组，也会固定各自的初始回退状态。扩展的 COM IStream smoke 通过外部修改原数组、再更新一个 host 的 stream，验证另一 host 仍读到自己的原始初始状态；新增断言在修复前失败、修复后通过。
- Release `SettingsSmoke`、NativeProbe/App Release build、`git diff --check` 通过，构建 0 警告/错误。没有创建 Shell view；这只是隔离宿主进程内的临时内存行为，未实现标签间隔离、INI/磁盘保存、跨重启恢复、产品接线或 Q-Dir 互操作。
- 此原型改进不关闭迁移阶段门槛。下一步仍需在允许启动原生 Shell view 的独立 Windows 会话中验证新旧 view 切换的真实 stream 生命周期；产品 `ExplorerBrowser` 保持不变。

## 2026-10-26：隔离原型按会话键保存原生 view stream

- `ShellViewStateStore` 现在把 Shell 的不透明 view-state stream 按稳定 GUID 会话键保存为分离文件；写入使用唯一临时文件并 flush，再替换目标文件。`StreamViewHost.Dispose` 在释放 Shell 写入流前提交快照，后续新建 store/host 会优先恢复该会话的字节；没有记录时仍使用导入的初始快照。此存储仅供 NativeProbe，未连接应用 profile、INI、窗口 session registry 或 Q-Dir 导入/导出。
- 扩展 `--shell-view-state-stream-smoke` 覆盖两个会话各自写入、重建 store/host 后逐字节读回及未保存会话回退。未创建 Shell view，不能证明真实 Shell 销毁时的写入时序，也未运行独立进程重启验证。
- 此结果把原型推进到跨 host 对象重建的本地磁盘持久化，但不关闭迁移阶段门槛；后续仍须先在独立 Windows 会话验证真实 view create/destroy 顺序，再设计与产品每标签 session identity 的适配。

## 2026-10-26：跨进程复验隔离 view-state 文件存储

- `--shell-view-state-stream-smoke` 现在启动三个隔离 NativeProbe 子进程：两个依次用同一 GUID 写入不同代的真实 COM IStream 状态并退出；第三个以新 store/host 读回最新一代并逐字节比较。复验退出码 0。
- 此证据验证探针文件存储跨进程可读，不代表进程崩溃、真实 `IShellView.DestroyViewWindow` 调用顺序或产品 session 重启恢复。没有创建 Shell view、GUI 或修改产品 profile；迁移门槛保持开放。

## 2026-10-26：隔离宿主插入标准 OLE 菜单组

- `StreamViewHost.InsertMenusSB` 的默认有效父窗口路径现在插入 File、Edit、View、Favorites、Tools、Help 六个原生弹出组，设置 Shell 标准菜单 ID 和 `OLEMENUGROUPWIDTHS=[2,0,3,0,1,0]`；`RemoveMenusSB`/`Dispose` 仅清理该 host 创建的组，显式外层回调优先。
- `--shell-view-color-policy-smoke` 的 COM vtable 隐藏 HWND 检查覆盖标签、ID、分组计数、卸载、再次合并、卸载时保留同一 HMENU 上的外部 popup，以及以真实 `IOleInPlaceActiveObject` 接口安装/卸载 OLE composite。NativeProbe Release `dotnet run` 通过；尚未创建 `IShellView`，所以 Shell 命令填充、实际 OLE 菜单消息分派和可见菜单均未验证。ExplorerBrowser 产品 UI 和发布物未修改。
- 直接向隐藏 frame 合成 WM_INITMENU/WM_INITMENUPOPUP/WM_MENUSELECT/WM_COMMAND 未把 WM_COMMAND 转发到 active-object 窗口；此路径绕过系统菜单跟踪，不能作为真实分派的失败或成功证据，已记入原型边界。

## 2026-10-26：复跑隔离迁移原型的导航与菜单命令

- NativeProbe Release 构建通过（0 警告、0 错误）。`host-menu-commands-private-desktop` watchdog 退出码 0：原生查看、刷新、Name 升/降序、Type 分组升/降序均由 `IFolderView2` 读回；刷新/重排时焦点或条目会短暂丢失，约 50–250 ms 后稳定恢复。日志：`src/.state/native-probe-watchdog/20261026-234804-68f63cb0.stdout.log`。
- `host-filter-private-desktop` watchdog 退出码 0：临时 filesystem view 的筛选/分组导出、五种同步视图、Ctrl+A、Down、Enter、文件目标拒绝、父级/后退/前进与历史分支截断均通过；Desktop、此电脑、回收站、库、网络位置、搜索文件夹和 ZIP fixture 均成功创建 Shell view。日志：`src/.state/native-probe-watchdog/20261026-234810-1051c20f.stdout.log`。
- `color-private-desktop` watchdog 退出码 0：临时 fixture 的复制、重命名、删除后项目数从 `2→3→3→2` 收敛；刷新期间创建文件后，后续刷新将数量恢复到 4，并保持选择/隐藏焦点快照。逐项目标色在本次八种查看方式采样中仍为 `0`，不能把历史个别命中视为稳定着色能力。日志：`src/.state/native-probe-watchdog/20261026-234913-94950277.stdout.log`。
- 三次 Private Desktop 运行的外层 foreground HWND 均保持为 `0x81852`。这只证明这些隔离宿主/合成加速器场景在本次会话没有改变主桌面前台；不等同真实鼠标拖放、物理键盘、可见焦点或产品 Pane/标签容器验收。`IDropTarget` 接口可查询仍不代表 OLE `DoDragDrop` 已通过。

## 2026-10-27：真实 IShellView 会话状态流写回

- `ShellViewColorBrowserHost` 现可接收 `ShellViewStateStore` 与稳定 session GUID，交给通用 `StreamViewHost`；策略 smoke 先因派生 host 不暴露该能力而失败，新增回归后通过，覆盖已保存流读取、Shell 写流、host Dispose 落盘及新 host 同会话读回。
- `color-private-desktop` watchdog 退出码 0；实际 Shell view 生命周期调用 4 次写流回调，销毁后落盘 772 字节，新建隔离 host 按同一 GUID 读回的流与落盘内容逐字节一致。主桌面前台 HWND 未变。日志：`src/.state/native-probe-watchdog/20261027-000036-81c04f29.stdout.log`。
- 额外尝试用单独的视图模式切换、销毁/重新导航触发写入，并让第二个真实 view 消费该状态；这些简化路径没有产生写流，故不能证明 Shell 会恢复模式或列。完整探针的写回只在更完整的 view 绘制/刷新生命周期中出现，触发条件尚未隔离；其中一轮失败记录：`src/.state/native-probe-watchdog/20261026-235844-733e4d24.stdout.log`。主桌面前台句柄仍未改变。
- 当前证据只证明真实 Shell 写流到临时 session store、重建 host 后再次提供相同流；不证明跨应用进程重启或产品标签 session identity。产品 ExplorerBrowser、INI 配置和发布物未接入或修改。

## 2026-10-27：真实 IShellView 激活菜单合并回调

- 新增 `--ishellview-menu-activation-private-desktop`，在真实默认 filesystem `IShellView` 创建后调用 `UIActivate(SVUIA_ACTIVATE_FOCUS)`，记录 `IShellBrowser` 的 `InsertMenusSB`、`SetMenuSB` 和 `RemoveMenusSB` 次数，并检查宿主窗口是否安装共享菜单。Private Desktop watchdog 连续三轮退出码 0；每轮均观察到 `insert=7 / set=13 / remove=6`、`shared-menu-present=True`，主桌面 foreground HWND 保持 `0x81852`。
- NativeProbe Release build 0 警告、0 错误；原 `color-private-desktop` 状态流往返探针单独复跑通过，仍读回 772 字节。完整记录见 [`ishellview-color-spike.md`](ishellview-color-spike.md#2026-10-27-真实-ishellview-激活菜单合并回调)。
- 这只证明真实 Shell 激活进入隔离宿主菜单生命周期并安装共享菜单；实际可见菜单中的自定义/Shell 项顺序、绘制、快捷键、启用状态和点击行为仍未验证。产品 ExplorerBrowser、INI 和 release 未修改。

## 2026-10-27：真实 IShellView 四窗格模式隔离、状态往返与文件操作

- 修正前一次状态恢复原型的刺激方式：`IFolderView::SetCurrentViewMode` 返回成功，但当前 DefView 读回仍是模式 1；改用本项目已验证的 `IFolderView2::SetViewModeAndIconSize` 后，保存前确认了期望查看模式。
- 原生隔离容器在一个 top-level HWND 下创建 2×2 child pane HWND，分别托管真实 `IShellView`；探针断言四个 pane 是同一父窗口的不同子窗口，且每个 view window 都属于自己的 pane。首窗格恢复 Details、Name 降序、Type 升序分组和三列顺序/宽度，消费进程收到 Shell 4 次读流。第二窗格使用独立 session GUID 切换为 List 并写入 772 字节，销毁后按同 GUID 重建，收到 4 次读流并恢复 List；第三、第四窗格分别读回 Icon 和 Tile。四个 view 同时存活时模式为 Details/List/Icon/Tile (`4/3/1/6`)。首窗格对共享目录执行 Shell 复制、重命名、删除后，四个 view 的项目数逐项收敛，四种模式及首窗格排序、分组和列布局保持。Private Desktop watchdog 连续三轮退出码 0，主桌面前台 HWND 均未变；策略 smoke 通过，NativeProbe Release build 0 警告、0 错误。
- 这证明四个真实 Shell view 能在同一原生父容器中以不同模式并存，第二窗格能按 session GUID 保存/销毁/恢复，同目录的 Shell 文件操作通知能令四个 view 同步更新。四个 pane 浏览同一目录，因此尚未验证跨目录通知隔离；这也不等同 WPF `PaneControl`、ExplorerDirs.App 正式标签 session-store 或生产四窗格协调，未验证真实物理焦点、可见菜单、拖放或键盘行为。消费进程退出时由 OS 回收隔离 Shell STA，生产探针负责清理本轮 fixture/state 目录。完整记录见 [`ishellview-color-spike.md`](ishellview-color-spike.md#2026-10-27-真实-ishellview-四窗格模式隔离状态往返与文件操作)。

## 2026-10-27：四窗格 Shell 菜单回调按激活 pane 路由

- 四窗格恢复探针逐个调用四个真实 `IShellView::UIActivate(SVUIA_ACTIVATE_FOCUS)`；三轮 Private Desktop watchdog 均退出码 0。每次目标 pane 宿主都收到 `InsertMenusSB` 与 `SetMenuSB`，离开 pane 时收到 `RemoveMenusSB`（每次增量分别为 `1/2/1`）；前台窗口句柄保持不变。四种模式与复制/重命名/删除后的四窗格通知收敛也在同轮继续通过。
- 这证明 Shell 菜单生命周期回调能按当前被激活的原生 pane 进入各自宿主。没有展示或点击菜单，也没有验证菜单项实际合并、命令状态/分派、WPF 焦点、物理键盘、跨目录通知或拖放；不能据此判定原型已达到产品迁移门槛。产品 ExplorerBrowser、配置和 release 未改。

## 2026-10-27：四窗格跨目录通知隔离

- 四窗格恢复探针把前三个 Shell view 留在主目录、第四个 view 放入兄弟临时目录。主目录的复制/重命名/删除由三个 view 收敛；随后在独立目录用 `IFileOperation` 新建副本，仅第四个 view 从 1 项更新到 2 项，前三个 view 计数不变且不含该项。连续三轮 Private Desktop watchdog 退出码 0，前台窗口句柄均保持不变。
- 这验证同一隔离原生容器内不同路径的 Shell 文件通知互不串扰。验证触发来自本进程 `IFileOperation`，不代表外部程序 watcher、产品 WPF、拖放或物理输入验收；产品 ExplorerBrowser、配置与 release 未修改。详见 [IShellView 原型记录](ishellview-color-spike.md#2026-10-27-四窗格跨目录-shell-通知隔离)。

## 2026-10-27：隔离拖放尝试与验证边界

- 直接调用目标 `IDropTarget` 的 `DragEnter/DragOver/Drop` 返回 COPY effect，但没有生成目标文件；因此这不是拖放成功证据。真正 OLE `DoDragDrop` 前置定位失败：Private Desktop 的 `GetCursorPos` 返回失败。未对当前桌面设置光标或注入鼠标输入，临时失败实现已撤回。
- Shell 拖放仍是迁移原型的未完成验收项。需要隔离桌面具备自身可用光标，或用户明确授权的隔离 GUI 输入会话后，再验证实际文件内容、双方视图通知和操作效果。详细失败记录与日志见 [IShellView 原型](ishellview-color-spike.md#2026-10-27-隔离-shell-拖放探针未通过产品行为门槛)。


## 2026-10-27：双 pane Shell 导航历史隔离 smoke

- 两个独立 `ShellViewColorBrowserHost` 经各自的 `IShellBrowser::BrowseObject` COM 入口交错导航、后退、建立分支与失败跳转；每个步骤断言双方的当前 PIDL 和 history index。NativeProbe Release build 0 警告/错误，`--shell-view-color-policy-smoke` 通过。
- 这是合成 PIDL 的宿主历史状态 smoke，不创建实际 Shell view；真实 Shell back/forward、可见按钮状态和 Pane 集成仍未验证，产品代码与 release 未改。详见 [IShellView 原型记录](ishellview-color-spike.md#2026-10-27-双-pane-ishellbrowser-导航历史隔离-smoke)。

## 2026-10-27：四窗格标准 Shell 菜单命令路由

- 经各 pane 自己的背景 `IContextMenu` 调用 canonical `viewlogicaldetails`，四个 pane 都能分别从 List 切换到 Details，其他 pane 模式保持；还原各 pane 后，四窗格复制/重命名/删除和跨目录通知通过。诊断确定 Shell 在 Details 菜单命令后按当前 view 宽度重排列宽，但列身份、顺序、排序和分组不变。验证保留初次会话恢复的精确列宽断言，菜单往返后的文件操作断言改为检查列身份/顺序。Private Desktop watchdog 退出码 0，主桌面前台句柄未变。
- 未展示或点击菜单，真实视觉合并、物理键盘与 WPF 活动 pane 集成仍未验证；产品 ExplorerBrowser、配置和 release 未改。详细记录见 [IShellView 原型](ishellview-color-spike.md#2026-10-27-四窗格标准-shell-查看命令定向到目标-pane)。

## 2026-10-27：四窗格 Shell 排序与分组命令路由

- 四个真实 Shell view 各自通过背景 `IContextMenu` 执行 Details、Name 升序/降序及 Type 分组升序/降序。每次命令后都读取四个 pane 的排序键/方向与分组键/方向，确认只有调用命令的 pane 发生预期变化；每轮后恢复模式及 pane 1 的 Name 降序基线。
- Release NativeProbe 构建 0 警告/错误，Private Desktop watchdog 连续三轮退出码 0；四窗格复制/重命名/删除和跨目录通知均通过，主桌面前台句柄不变。
- 这是隐藏隔离默认 filesystem view 的 canonical verb/状态读回，不证明菜单可见合并、列标题右键、物理输入或 WPF 活动 pane 接线。产品 ExplorerBrowser、配置与 release 未改。详细记录见 [IShellView 原型](ishellview-color-spike.md#2026-10-27-四窗格-shell-排序与分组菜单命令隔离)。

## 2026-10-27：四窗格文件项 Shell 菜单目标隔离

- 每个真实 `IShellView` 单独选中一个项目，通过自身 `SVGIO_SELECTION` 获取 `IContextMenu` 并枚举到 canonical `copy`；逐步读取四个 view 的选择数量，确认另外三个 pane 不继承活动 pane 选区，测试后清除全部选区。
- Private Desktop watchdog 连续三轮退出码 0，四窗格状态、Shell 文件操作与通知检查通过，主桌面前台未变。探针没有执行文件命令、修改剪贴板或显示菜单。
- 可见右键菜单合并/命令调用、第三方扩展、产品 ExplorerBrowser 及 WPF 目标命中仍待验证。详见 [IShellView 原型](ishellview-color-spike.md#2026-10-27-四窗格文件项菜单目标隔离)。

## 2026-10-27：四窗格 IShellView 方向键焦点路由

- 在真实隔离 Shell view 的 Details、List、Icon 三种模式中，分别通过目标 view 的 `TranslateAccelerator` 将第一项焦点移动到下一项；网格视图使用 Right，Details/List 使用 Down。四个 view 的焦点读回确认兄弟 pane 不变；单项第四 pane 不参与移动断言。
- Private Desktop 完整 session-restore probe 连续三轮退出码 0，菜单路由、选择项菜单、文件操作和通知仍通过，前台句柄未变。
- 此为合成消息直调 Shell accelerator，不等同物理键盘、完整 message loop、重命名编辑或产品 WPF 焦点验收。详见 [IShellView 原型](ishellview-color-spike.md#2026-10-27-四窗格-ishellview-方向键焦点路由)。

## 2026-10-27：四窗格 Ctrl+A 选择路由

- 在三个包含多项的真实 Shell pane 上逐一通过各自 `IShellView::TranslateAccelerator` 派发 Ctrl+A。每次仅目标 view 的两项全选，其他 pane 选择数为零；探针随后清空选择，并恢复线程键盘 modifier 状态。
- Private Desktop 完整四窗格探针连续三轮退出码 0；方向键、各 pane Shell 菜单、文件操作和目录通知检查同步通过，主桌面前台不变；未操作系统剪贴板。
- 这是合成按键与 modifier 状态的单次 Shell accelerator 路径，不证明物理 Ctrl+A、完整消息循环或 ExplorerBrowser 产品焦点接线。详见 [IShellView 原型](ishellview-color-spike.md#2026-10-27-四窗格-ctrl-a-选择路由)。

## 2026-10-27：补齐 Q-Dir 普通鼠标滚轮焦点选项

- 顶部“视图”菜单新增默认关闭的“鼠标悬停滚轮”，保存在独立 `MouseWheelFocus.ini`。开启后，普通垂直滚轮消息定向到指针所在的可见 Shell 文件视图或目录树；对逐窗格目标更新逻辑活动窗格，不抢键盘焦点；Ctrl+滚轮继续使用原有查看方式循环路径。
- 完整 Release `SettingsSmoke`、定向 `--mouse-wheel-focus-smoke`、App Release 构建通过；本批已提交 `3ad1101`。这证明配置、消息决策和代码接线可构建，不代表真实鼠标、焦点、四窗格菜单行为通过；这些仍需可见桌面验收。对标依据：[Q-Dir FAQ 75](https://www.softwareok.com/?faq=75&seite=faq-Q-DIR)；复核 WPF 官方源码发现 `ComponentDispatcher` 目前只支持键盘消息，因此已改用仅绑定本 UI 线程的 `WH_GETMESSAGE` 钩子；只处理 `PM_REMOVE` 的竖向滚轮，并从消息自带的有符号屏幕坐标确定目标，不用全局钩子或合成第二条滚轮消息。实现依据：[WPF ComponentDispatcher 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Interop/ComponentDispatcher.cs)、[Win32 SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-setwindowshookexw)、[WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)。真实鼠标、滚动方向/精度及多窗格焦点仍待桌面验收。

## 2026-10-27：Shell 文件命令过期请求前置取消

- `ShellFileCommands.Invoke` 现在会在打开原生菜单前检查请求上下文；Shell 菜单构造或 `IsEnabled` 查询可能重入改变窗格、导航或选区，因此两处之后也会再次检查，取消整条过期命令。
- 回归用例先在旧实现上失败，证明预先过期的请求仍会创建菜单；修正后覆盖预过期不打开菜单、菜单构造期间过期、命令状态查询期间过期，以及 New Folder 背景菜单路径。
- Release `SettingsSmoke` 和 App Release 构建通过，0 警告/错误。仅完成状态逻辑与构建验证，没有调用真实 Shell 命令、启动 ExplorerBrowser 或可见 GUI；实际 COM 扩展重入行为仍待隔离验证。

## 2026-10-27：逐行自定义列刷新负向控制

- 私有桌面静态列探针在成功应用列配置后调用 `IShellView::Refresh`，返回 `S_OK`；刷新后自定义列仍为空、实际升序仍为 `100,10,10,2`，DefView 未调用 adapter 的项目取值/比较回调。刷新不是当前缺失列行为的修复方式。
- 官方 `PSRegisterPropertySchema` / `PSUnregisterPropertySchema` 契约要求机器级 schema 和 `HKLM` 写权限，注册属于安装流程、注销属于卸载流程；不在日常开发机执行临时 schema 注册。逐行列仍是失败中的隔离实验，不接入产品。
- 探针 Release build 通过（0 警告、0 错误）；私有桌面前台保护通过。详见[逐行文件夹大小列架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。

## 2026-10-27：隔离 IShellView 通过 canonical verb 新建文件夹

- 列表焦点已确认后，使用不带 `CMIC_MASK_FLAG_NO_UI` 的 ANSI canonical verb 创建成功；Shell 视图从 2 项变为 3 项，但选中数为 0、focused index 仍在原 `child` 项，且没有 `Edit` 重命名窗口。不同菜单构造样本的命令 offset 为 113/115；各自按 offset 调用都失败，canonical verb 成功。Private Desktop 前台句柄未变，探针夹具清理完成。NativeProbe Release build 0 警告/错误，单命令 watchdog 退出码 0。
- 已证明隔离默认 filesystem `IShellView` 可通过 canonical verb 执行创建并让视图收敛；程序化调用没有复现菜单后的自动选中和内联重命名。offset 会随菜单构造变化，且逐次重新查询后调用仍失败，具体原因未知。真实菜单弹出/点击、ExplorerBrowser 产品接线与完整多窗格行为仍未验证，不据单一命令推广全局调用规则。未修改产品代码、用户目录或 release。详见[IShellView 隔离原型记录](ishellview-color-spike.md#2026-10-27-背景菜单新建文件夹-canonical-verb-原型通过)。

## 2026-10-27：布局工具栏选中态使用系统强调色

- 当前布局按钮保留对比度检查过的选择背景，并将边框切换为动态 `ExplorerDirs.AccentBrush`；非选中布局仍使用中性边框。切换布局时按当前主题资源刷新，不新增持久化配置或改变布局行为。
- `--pane-theme-markup-smoke` 对新语义资源接线先失败、后通过；完整 SettingsSmoke 与 App Release build 通过后提交。实际窗口颜色、高对比度和四窗格窄宽度仍待可见验收。

## 2026-10-27：统一标签与辅助窗格工具图标

- 每个窗格的新建/关闭标签按钮改用共享矢量图标；主工具栏预览/详情窗格开关增加图标并保留文字，按钮无障碍名称、原有命令处理器和焦点顺序不变。新增预览与详情图形进入同一 16-unit 图标网格，并使用动态主题语义画刷。
- `--explorer-chrome-icon-smoke` 的图标路径与接线断言先失败、接入后通过；完整 SettingsSmoke、App Release build 和差异检查通过。实际缩放/对比度观感仍待可见验收。

## 2026-10-27：主题色标记已启用的辅助视图

- 目录树、预览和详情三个 ToggleButton 的启用态改为使用语义选择背景、对比前景与动态系统强调色边框，关闭态保持中性色。未改变各按钮命令、状态保存和焦点顺序。
- `--pane-theme-markup-smoke` 新增启用态资源断言，先按预期失败，加入 ToggleButton 样式触发器后通过。完整 SettingsSmoke、App Release build 及差异检查通过；可见主题切换、高对比度和深色下检查仍待桌面验收。

## 2026-10-27：键盘焦点框跟随主题强调色

- TextBox、ComboBox、Button 和 ToggleButton 共用 Accent 色虚线焦点框；系统/自定义强调色变化通过动态资源反映，高对比度沿用系统 Highlight 资源。Shell 原生文件视图焦点绘制不受影响。
- `--pane-theme-markup-smoke` 新增焦点样式及四类控件接线检查，旧实现下失败，接入后通过。真实键盘导航、放大缩放和高对比度外观仍待可见验收。

## 2026-10-27：浏览器销毁时清理 Shell 搜索身份映射

- 源码审查发现，每标签 `ShellSearchHistory` 在两张映射表中保留查询 URI，原有清理只随宿主对象回收；`DisposeBrowser()` 销毁对应 ExplorerBrowser 和其 TravelLog，却没有清掉这些映射。现在在该生命周期边界清空映射，之后重建的浏览器从新历史开始。
- Search Folder SettingsSmoke 在旧实现下因缺少清理入口按预期失败；修正后覆盖旧搜索身份不可再解析及新浏览器重新登记搜索。完整 Release SettingsSmoke 和 App Release build 通过（0 警告、0 错误）。这修复的是确定的托管引用生命周期，不证明它是用户观测到的主要内存增长来源；实际同 PID 内存仍需持续采样。
- 未启动 ExplorerBrowser 或 GUI；真实搜索 Back/Forward 和 WPF HwndHost 重建后的可见行为仍待验收。

## 2026-10-27：IShellView 原生重命名与方向键隔离验证

- Private Desktop 探针在 canonical `newfolder` 创建后显式选中新目录并聚焦列表；经 `IShellView::TranslateAccelerator` 发送 F2 后，原生内联编辑框成功打开并取得焦点。编辑期间再路由 Left/Right，编辑框保持存在且继续持有焦点。
- NativeProbe Release 构建 0 警告/错误；`host-newfolder-private-desktop` watchdog 退出码 0，隔离前台 HWND 未变，临时夹具清理完成。验证范围仅为 Shell 原生 accelerator 和左右键，不含产品多窗格/WPF 消息接线、上下键或物理键盘。
- 这是后续重命名焦点问题的实现线索，不是产品缺陷修复，也不证明 IShellView 宿主迁移已具备产品级兼容性。详见 [IShellView 隔离原型记录](ishellview-color-spike.md#2026-10-27-背景菜单新建文件夹-canonical-verb-原型通过)。

## 2026-10-27：读取 IShellView 激活后的原生菜单组

- 在实际 Shell `UIActivate` 后读取共享 `HMENU`，探针确认 Shell 已向 File/Edit/View/Help 子菜单填充标准命令。当前中文系统样本读取到 4/10/18/1 个项目，包括剪切、复制、粘贴、全选、查看方式、排序、分组与列选择；通过条件只要求这些组非空，避免依赖系统语言或固定项目数。
- Release NativeProbe 构建 0 警告/错误；Private Desktop `menu-activation-private-desktop` watchdog 退出码 0，前台 HWND 未变。
- 这是共享菜单结构和 Shell 命令填充的证据；尚未显示/点击菜单、插入应用自定义菜单项、验证命令分派或 WPF 活动 pane。详见 [IShellView 原型记录](ishellview-color-spike.md#2026-10-27-激活后的-shell-原生菜单组内容读取)。

## 2026-10-27：Shell 主菜单命令目标窗口探针

- 从原生 View 菜单定位 Details 项并使用当前实际 ID；在 Small Icons 基线上，将 `WM_COMMAND` 发给宿主父窗口不改变模式，发给对应 `IShellView` window 后目标 view 切到 Details。探针对两条路由分别复位基线并断言结果。
- NativeProbe Release build 0 警告/错误；Private Desktop `menu-activation-private-desktop` watchdog 退出码 0，前台窗口未变。
- 这指向后续宿主必须把 Shell 命令路由到当前活动 view，但仍是合成消息，不是菜单实际点击或 WPF 消息路由验证。详见 [IShellView 原型记录](ishellview-color-spike.md#2026-10-27-激活后的-shell-原生菜单组内容读取)。

## 2026-10-27：四窗格共享菜单 frame 探针未通过

- 隔离尝试把每个 Shell view 的子窗格 parent 与菜单 top-level frame 分离：Pane 1 可读共享菜单，激活 Pane 2 后 `GetMenu` 返回的句柄无有效菜单项（`GetMenuItemCount=-1`，host callback counts `5/9/4`）。NativeProbe build 通过，完整 session-restore watchdog 因菜单探针失败退出码 1。
- 失败原型代码已撤回，既有四窗格 consumer 保持原样；不据此判断 Shell 菜单本身失败。下一步先查清跨 host 菜单安装/卸载顺序及 HMENU 生命周期，再实现共享 frame 协调并复测。
- 详见 [IShellView 原型记录](ishellview-color-spike.md#2026-10-27-四窗格共享菜单宿主尝试未通过)。

## 2026-10-27：确认四窗格 SetMenuSB 失败是 child HWND 菜单错误

- 在完整四窗格 session-restore 探针中记录每个 host 的菜单回调句柄与 HRESULT。四个 pane 的 `InsertMenusSB`/`SetMenuSB` 回调都到达，但 `SetMenuSB` 均返回 `0x8007059C`，frame 参数是 pane child HWND，安装后 `GetMenu` 为 0。其余已有多窗格会话、菜单 context command、键盘路由、文件操作和通知检查通过。
- 结合 Microsoft `SetMenu` 契约，这个 HRESULT 对应 `ERROR_CHILD_WINDOW_MENU`：子窗口不能拥有菜单。原有“menu callback counts > 0”并未证明菜单成功安装；此前 frame split 试验缺少跨 host 协调。
- 下一步拆分 Shell view pane parent 与顶层 OLE frame window，并实现共享菜单/descriptor 所有权与 active view 生命周期。详细证据和官方合同链接见 [IShellView 原型记录](ishellview-color-spike.md#2026-10-27-四窗格菜单-setmenu-子窗格错误根因确认)。

## 2026-10-27：F2 编辑状态下四方向键隔离验证

- 将原生重命名探针从 Left/Right 扩展到 Left/Up/Right/Down。`host-newfolder-private-desktop` watchdog 通过：Shell 创建临时文件夹，选中并聚焦后启动 F2 编辑，再将四个方向键交给编辑框；四键均返回 `S_OK`，编辑窗口和焦点保持，前台 HWND 未变化。NativeProbe Release build 0 警告、0 错误。
- 这只覆盖合成消息下的单个原生 filesystem `IShellView`；List/Icon 等模式、真实键盘、多窗格、WPF `HwndHost` 与 ExplorerBrowser 产品行为仍待验收。它为迁移原型补充焦点兼容证据，不代表原产品问题已修复。详细日志和边界见[隔离原型记录](ishellview-color-spike.md#2026-10-27-f2-编辑状态下四方向键隔离验证)。

## 2026-10-27：五种视图模式下 F2 编辑焦点隔离验证

- 把 F2 与四方向键检查纳入五种 Shell/ListView 同步模式循环。初次测试发现 Icon 模式因宿主尚未 UIActivate，F2 虽返回 `S_OK`，却未切入编辑框；为探针按原生宿主生命周期显示隔离父窗（`SW_SHOWNOACTIVATE`）并激活 view 后，完整 Private Desktop 流程连续两轮通过，Shell edit 与焦点在四方向键期间均保持，退出时恢复隐藏父窗，foreground HWND 未变化。
- 测试日志：`20261027-034456-3eb61774.stdout.log`、`20261027-034526-eca8b327.stdout.log`。这是单 view、合成键消息的原型证据，尚未覆盖真实键盘、多窗格互相抢焦点、WPF `HwndHost` 或 ExplorerBrowser 产品宿主；不表示用户报告的产品 F2 缺陷已修复。详情见[隔离原型记录](ishellview-color-spike.md#2026-10-27-五种视图模式下-f2-编辑焦点隔离验证)。

## 2026-10-27：四窗格 F2 编辑方向键焦点隔离

- 四窗格 session-restore consumer 现在逐个激活 pane、打开 F2 原生重命名，并将四方向键送到各自编辑框。每次按键后四个 view 的 focused item 与 selection 均逐一核对；两轮完整 Private Desktop watchdog 通过，四 pane 都报告 `arrows=4/focus-isolated=True`，前台窗口未变。
- 该结果证明定向 `IShellView::TranslateAccelerator` 下 pane 状态隔离；不覆盖 WPF 焦点链、错误 pane 路由、真实输入或 ExplorerBrowser 产品宿主，故多窗格重命名 P0 仍未验收。[隔离原型记录](ishellview-color-spike.md#2026-10-27-四窗格-f2-编辑方向键焦点隔离)

## 2026-10-27：错误 pane 的 accelerator 不夺取活动 F2 编辑焦点

- 四窗格隔离 consumer 在 Pane 1 重命名时，把 Down 键消息故意交给 Pane 2 的 `IShellView::TranslateAccelerator`。Pane 2 返回 `S_FALSE`，Pane 1 编辑框焦点保持；随后四 pane 各自的四方向键路由与兄弟 focused item/selection 隔离仍通过。完整 Private Desktop watchdog 两轮退出码 0，前台未变。
- 这只验证直接合成 Shell accelerator 的错误目标路径，不模拟 WPF `HwndHost` 消息链或真实物理键；产品多窗格键盘 P0 仍待可见验收。详见[隔离原型记录](ishellview-color-spike.md#2026-10-27-错误-pane-的-accelerator-不夺取活动-f2-编辑焦点)。

## 2026-10-27：四窗格 Shell 菜单合并应用菜单组

- 隔离 `StreamViewHost` 在 `SetMenuSB` 安装前向 Shell composite HMENU 添加自有 `Explorer Dirs` 菜单组，并在活动 pane 切换/停用时经共享菜单协调器清理自有 popup；同线程 frame subclass 接收 probe 命令的 `WM_COMMAND` 并路由到当前 coordinator owner。该处理遵循 Shell 先构造菜单、宿主再修改 composite menu 的接口顺序。
- NativeProbe Release build 0 警告、0 错误；无 Shell view 的 `--shell-view-color-policy-smoke` 通过；完整 `session-restore-private-desktop` 退出码 0。四个真实 filesystem `IShellView` 逐个激活时当前菜单均含应用组，合成 frame `WM_COMMAND` 每次只递增活动 pane，所有 pane 停用后命令被忽略；前一 owner 菜单清理、最终恢复原 frame 菜单均通过。view-state、Shell 菜单命令、文件操作/通知与焦点隔离检查继续通过，主桌面 foreground HWND 不变。最终日志：`src/.state/native-probe-watchdog/20261027-040514-e132943a.stdout.log`。
- 仅为 Private Desktop 隔离宿主证据；没有显示/点击菜单、验证快捷键/OLE 菜单消息循环或接入 WPF 产品宿主。probe 命令没有产品功能语义。ExplorerBrowser 产品、配置与 release 未改。详细证据见[原型记录](ishellview-color-spike.md#2026-10-27-四窗格-shell-菜单合并应用菜单组)。

## 2026-10-27：Shell 原生菜单命令路由仍待补齐

- 尝试将 frame 收到的 Shell Details 命令 `0x704B` 转发到 coordinator 当前记录的活动视图；命令到达 frame subclass，但目标 pane 2 的视图模式没有变化，owner 仍指向 pane 1。进一步按焦点 HWND 选择 pane 也未使命令生效（Private Desktop watchdog 退出码 1，日志 `20261027-041021-6ff9cece.stdout.log`、`20261027-041155-4116eada.stdout.log`）。两条未经验证的转发分支均已撤回，已通过的应用自定义命令路由不受影响。
- 微软文件夹视图文档说明 Shell 菜单的相关 `WM_COMMAND` 由 Explorer 宿主处理，不会直接传给 folder view 对象；因此问题不只是焦点映射，当前直接转发给 `IShellView` 的方法本身不成立。下一步先确认自建宿主执行标准 Shell 命令的公开契约，再设计最小 pane dispatch probe。此项通过前，不宣称 Shell 原生菜单命令路由或产品 `IShellView` 迁移完成。
- 详见[IShellView 原型记录](ishellview-color-spike.md#2026-10-27-shell-原生菜单命令-frame-转发尚未通过)。

## 2026-10-27：撤回 frame 转发后的原生命令基线复核

- 恢复到上一已提交实现后，NativeProbe Release build 0 警告/错误，完整四窗格 `session-restore-private-desktop` watchdog 退出码 0（`20261027-041344-088dec96.stdout.log`）。各 pane 经背景 `IContextMenu` canonical verb 分别完成 Details、排序、分组；四窗格会话/菜单生命周期、自定义命令 active-pane 路由、文件操作/通知和 F2 方向键焦点隔离仍通过，前台 HWND 未变。
- 这些原生命令走的是 context-menu `InvokeCommand`，不证明 frame 合成 View 菜单的 `WM_COMMAND` 已可用。后续先研究 Explorer 宿主处理 `FCIDM_SHVIEWFIRST..LAST` 的标准命令路径，再实现 frame dispatch 原型；Shell 菜单命令通过前不进入产品宿主迁移。
- 详见[IShellView 原型记录](ishellview-color-spike.md#2026-10-27-撤回-frame-转发实验后的四窗格原生命令基线复核)。

## 2026-10-27：比较 Shell View 菜单与 context-menu 命令 ID

- 在真实 composite View 菜单中读到 Details ID `0x704B`；同一 pane 的 `IContextMenu` canonical verb `viewlogicaldetails` 在 `idCmdFirst=0x7000` 时分配 ID `0x701B`。ID 不相同，不能把 View 菜单 ID 直接重解释成 context-menu verb ID。
- 完整四窗格 watchdog 仍退出码 0（`20261027-041457-17b0712f.stdout.log`）；该检查只读比较 ID，不执行 frame View 菜单命令。下一步查 Windows Explorer 对标准 `FCIDM_SHVIEWFIRST..LAST` 命令的宿主处理/映射，再做最小 dispatch 实验。
- 详情见[隔离原型记录](ishellview-color-spike.md#2026-10-27-撤回-frame-转发实验后的四窗格原生命令基线复核)。

## 2026-10-27：四窗格 Shell View 菜单命令映射与执行

- 两次完整 Private Desktop 四 pane 回归通过，日志为 `20261027-042900-e2e86451.stdout.log` 和 `20261027-042934-db8d1d60.stdout.log`；此前两轮分别在 120 秒时限超时，阶段输出将停滞范围缩小到独立恢复 consumer 附近，但未复现原因。现在 producer/consumer 均输出阶段标记，父进程实时转发 consumer 输出。通过运行中，Shell View composite menu 的 Details、排序升/降序、分组升/降序均映射到当前 pane `IContextMenu` canonical verb，每次只改变活动 pane；完整会话恢复、文件操作/通知、F2 焦点隔离检查通过，主桌面前台窗口不变。
- 结论：标准菜单命令的隔离 frame 分派路线在连续两次完整运行中通过；早先 consumer 阶段超时仍是未解释的间歇风险。继续补齐真实菜单跟踪/OLE accelerator、实际拖放、颜色绘制和 WPF 宿主集成门槛。未接入产品 ExplorerBrowser 替换，也未发布迁移版本。详情见[IShellView 原型记录](ishellview-color-spike.md#2026-10-27-四窗格-shell-view-菜单命令映射与执行)。

## 2026-10-27：厘清 Shell view 与 OLE frame accelerator

- 隔离试验中，Win32 `TranslateAccelerator` 能匹配合成按键与 accelerator 表，但 `OleTranslateAccelerator` 返回 `S_FALSE`，未到达 frame handler。该探针没有完整 OLE `GetWindowContext` 生命周期，不能判断 `IOleInPlaceFrame::TranslateAccelerator` 本身有问题；临时代码已撤回。
- 下一步走文件浏览器真实调用链：从有焦点的 `IShellView::TranslateAccelerator` 检查 Shell 是否委托 `IShellBrowser::TranslateAcceleratorSB`，记录返回值、消息和 pane 路由。ExplorerBrowser 产品和 release 均未改。详细依据见[隔离原型记录](ishellview-color-spike.md#2026-10-27区分-ole-frame-与-shell-view-accelerator-路径)。

## 2026-10-27：验证 filesystem Shell view 的 browser accelerator 委托

- 完整 Private Desktop session-restore consumer 对有焦点的真实 filesystem `IShellView` 合成 F4、F6、F24 `WM_KEYDOWN`。返回值依次为 `S_FALSE`、`S_OK`、`S_FALSE`，browser `TranslateAcceleratorSB` handler 三次均未调用；每次输入前恢复同一文件列表焦点。完整四窗格 session-restore watchdog 退出码 0，前台窗口不变。日志：`src/.state/native-probe-watchdog/20261027-043550-7cf2b1d4.stdout.log`。
- 结论仅适用于此 filesystem view 与这三个合成按键；不代表 namespace extension、真实输入或 WPF 宿主。该验证范围和后续产品键盘路由见下面的路线修正；产品 ExplorerBrowser 与 release 未改。详见[隔离原型记录](ishellview-color-spike.md#2026-10-27探测-filesystem-view-到-browser-accelerator-的委托)。

## 2026-10-27：修正 Shell view accelerator 的验证路线

- 对照微软 `IOleInPlaceSite::GetWindowContext` 与 `OleTranslateAccelerator` 契约后，确认 OLE frame accelerator 是 in-place object activation 提供的另一条路线。当前原型由 `IShellFolder::CreateViewObject` 直接创建 filesystem `IShellView`，没有 OLE in-place object 生命周期；给它补造 `GetWindowContext` 不会验证文件视图实际按键行为。因此移除“完成 OLE accelerator 生命周期”作为 Shell view 迁移门槛。
- 保留真实 filesystem view `IShellView::TranslateAccelerator` 的 F4/F6/F24 委托结果；接下来的 keyboard gate 是产品 `ExplorerBrowserHost.TranslateAcceleratorCore` 中的实际焦点、活动 pane、rename editor 与 Shell 视图分发，需用隔离产品 HostProbe 验证。真实物理键盘和可见 WPF 行为仍需后续桌面验收；不据此声称产品迁移完成。
- 参考：[IOleInPlaceSite::GetWindowContext](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-ioleinplacesite-getwindowcontext)、[OleTranslateAccelerator](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oletranslateaccelerator)、[IShellView::TranslateAccelerator](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-translateaccelerator)。产品 ExplorerBrowser 与 release 未改。

## 2026-10-27：为后续内存增长调查准备只读采样器

- 现有记录没有本轮可比较的进程样本；当前机器也没有运行中的 Explorer Dirs 实例，因此不启动应用制造基线。新增 `scripts/sample-memory.ps1`，默认每 5 秒记录 5 分钟内所有已运行 ExplorerDirs.App 进程的 working set、private bytes、virtual bytes、句柄数和线程数；应用退出后自动结束，不读取目录/文件路径，不启动或操作窗口。
- 仅做脚本解析检查和“没有运行实例”退出路径验证；未取得运行时内存曲线。使用方法和证据限制见[内存采样说明](memory-sampling.md)。这为未来在相同窗格/标签负载下比较内存变化提供 CSV，不把单一 working-set 数值当作泄漏诊断，也不改变应用内存策略。

## 2026-10-27：隔离 Shell view 逐项颜色像素覆盖五种样式

- NativeProbe Release 构建 0 警告/错误；`color-private-desktop` watchdog 退出码 0，日志 `src/.state/native-probe-watchdog/20261027-043915-cea60716.stdout.log`。通过的 `direct-listview-mode-capture` 在真实 filesystem `IShellView` 的 `SysListView32` 上覆盖 Icon、Details、SmallIcon、List、Tile 五种样式；两条按文件身份配置不同颜色的样本，在未排序、排序及分组后都取得两行非零颜色像素，排序/分组后身份映射正确。
- 前置 `visible-row-pixels` 路径回调为零，未通过；只有后续专用采样路径算作证据。Private Desktop 前台句柄未变。颜色绘制因此具备隔离技术可行性证据，仍不代表产品着色已接入或用户桌面外观验收通过；真实 OLE 拖放、物理键盘焦点及 WPF 宿主集成仍在迁移门槛中。详见[IShellView 原型记录](ishellview-color-spike.md#2026-10-27五种-listview-样式的逐项颜色像素隔离复核)。

## 2026-10-27：Alt+Tab 时暂停文件操作焦点恢复期限

- 源码追踪发现，Shell 文件操作的焦点恢复请求虽会在窗口失活期间保留并暂停 `SetFocus`，但原有 1.5 秒绝对期限仍持续计时；用户离开应用超过该期限后返回，请求可能先过期。现在失活轮询会更新计时检查点，期限只在窗口活动期间消耗；窗格/视图/导航/用户输入门控与激活后的 1.5 秒上限不变。
- `ShellFocusRestoreGateSmoke` 对“失活时间超过期限”“持续失活轮询”“重新激活后恢复有界计时”新增回归；先因缺少计时检查点 API 按预期失败，再通过。完整 Release SettingsSmoke 通过，App Release build 0 警告/错误。未操作剪贴板、启动 ExplorerBrowser 或使用物理键盘；真实 Alt+Tab、Ctrl+C/V 和 Shift+Delete 后的方向键体验仍待可见验收，文件操作焦点 P0 保持开放。

## 2026-10-27：隔离 IShellView Edit → Select All pane 路由

- 纠正早前探针误判：`IFolderView::SelectItem` 的 `SVSI_SELECT=0x1`，取消选择为 `0`；曾将 `0x1` 错当清选标志，旧“误选 pane 2”结果无效。当前 Shell 菜单实测将 Edit → 全选分配为 `0x7023`。NativeProbe frame coordinator 依据当前 Edit 菜单项识别该命令，再调用共享 `ShellSelectionCommands.Apply(All)` 到当前 pane 的 `IFolderView2`。
- NativeProbe Release build 0 警告/错误；无 stale-owner 防护前两轮完整 `session-restore-private-desktop` watchdog 退出码 0。加入 pending owner 当前性检查后，四次回归均通过 `menu-activation-complete` 阶段（包含每 pane Edit → Select All 的选择数与 sibling 隔离断言），但之后分别在复制/重命名/删除阶段超时。新路由在隔离四窗格中已验证；完整回归的后续文件操作间歇性超时尚未查明，不能报告为整轮通过。
- 该命令分派仍是 NativeProbe 原型，依赖当前中/英文 Shell 菜单项目文本；owner 变化竞态分支、其他本地化、真实菜单跟踪、物理键盘及 WPF 产品宿主尚未验证。完整迁移门槛仍开放，详见[隔离 IShellView 原型记录](ishellview-color-spike.md#2026-10-27隔离-edit--select-all-pane-路由)。
- 为定位 `file-delete-performed` 后的间歇超时，NativeProbe 现逐步记录主视图计数/路径匹配、Refresh、四 pane 计数和视图设置检查的进入/退出边界；删除主视图等待还记录消息泵、Shell 计数和 PIDL 查找阶段。只做 Release 编译与差异检查，没有启动 Shell 宿主探针；停滞根因仍未知，待有当前授权的隔离运行再读取这些标记。

## 2026-10-27：恢复 consumer 增加逐阶段诊断并复核四窗格基线

- 为恢复 consumer 增加 view/pane 创建、菜单验证和复制/重命名/删除阶段标记。前次三轮通过、一轮文件操作阶段超时；随后新路由批次又有两轮完整回归通过。阶段标记帮助把超时定位到不同阶段，但间歇原因尚未查明。真实拖放、可见菜单交互和 WPF 产品宿主集成仍是迁移门槛。详见[IShellView 原型记录](ishellview-color-spike.md#2026-10-27为恢复-consumer-增加逐阶段诊断)。

## 2026-10-27：四窗格原生 Shell view 的 drop target 生命周期复核

- 在每个真实 filesystem `IShellView::CreateViewWindow` 返回后，从对应文件夹取得 `IID_IDropTarget` 并将其注册到新 view HWND；导航替换 view 时先注册新 HWND、再撤销旧 HWND；失败回滚只撤销新 HWND，释放 view 前撤销其 drop target。重新构建 NativeProbe Release（0 警告、0 错误）后，完整 `session-restore-private-desktop` watchdog 退出码 0：四 pane 恢复、菜单/排序/分组命令、复制/重命名/删除、跨 view 目录通知与焦点隔离均通过，`foreground-unchanged=True`。日志：`src/.state/native-probe-watchdog/20261027-053426-3ca5dac1.stdout.log`。
- 独立 `--shell-view-drop-target-registration-smoke` 通过：单 host 注册四个 HWND、第二 host 重复注册正确失败且不抢走原 owner、选择性撤销后 HWND 可重新注册、Dispose 撤销剩余注册；前台窗口未变化。
- 证据确认真实 Shell view 暴露的 `IDropTarget` 已在四窗格 host 生命周期中完成注册/替换/撤销，不能等同于实际 OLE 拖放。未执行 `DoDragDrop`，未验证拖放效果、鼠标源端、第三方目标、ExplorerBrowser/WPF 产品接线；这些仍是迁移门槛。详见[隔离原型记录](ishellview-color-spike.md#2026-10-27四窗格原生-shell-view-的-drop-target-生命周期复核)。

## 2026-10-27：隔离 IShellView 文件夹目标接收 Shell 数据对象

- `IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IDropTarget)` 在本机 filesystem view 返回 `E_NOINTERFACE`；经 `IShellFolder::CreateViewObject` 取得并注册到 view HWND 的目录目标则接受 `SHCreateDataObject` 构造的临时 Shell 文件对象。`DragEnter/DragOver/Drop` 均成功，Copy effect 为 1，目标内容匹配、源文件保留。NativeProbe Release 构建 0 警告/错误，Private Desktop watchdog 退出码 0，`--shell-view-color-policy-smoke` 通过，foreground 未变。
- 前两次 watchdog 超时根因为 Private Desktop top-level parent `DestroyWindow` 清理阻塞；遵从现有一次性探针进程清理规则后退出正常。两次超时留下的两个 `%TEMP%\ExplorerDirs-NativeProbe\<guid>` 测试夹具无法通过当前工具删除，详情见[探针记录](ishellview-color-spike.md#2026-10-27-ishellview-文件夹-drop-target-的-shell-数据对象接收)。
- 这是文件夹目标接收 Shell `IDataObject` 的隔离证据，源对象通过 `SHCreateDataObject` 构造；没有验证从真实选择创建的数据对象、`DoDragDrop`、鼠标命中/修饰键/移动语义或用户可见跨窗格拖动，故拖放 P0 仍开放。

## 2026-10-27：对比 IShellView 选中项数据对象

- 修正探针对 `ILFindChild` 借用 PIDL 的错误释放，并在枚举前等待源 view 稳定到 3 个测试项目；Private Desktop watchdog 退出码 0，foreground 未变。
- 后续更正发现此阶段把 `view-target.txt` 作为源选择，却检查了 `selected-view.txt` 目标路径；当时观测到的 `copied=False` 只是目标名断言错误，不代表 Shell 未复制。该失败结论已撤销，见路线图顶部 2026-10-27 更正条目与[原型记录](ishellview-color-spike.md#2026-10-27-对比-ishellview-选中项数据对象)。
- 真实选中项复制已在更正后的探针通过；本隔离测试仍只直接调用目标 `IDropTarget`，并非真实 OLE `DoDragDrop`。源端循环、移动/修饰键、鼠标命中、可见交互和产品接线未验证。

## 2026-10-27：登记 Q-Dir 标签组差距

- 对照 Q-Dir 官方英文功能页，发现其标签菜单列有 “Tab Group”“Move to new Tab Group”“Copy to new Tab Group”“Use Tab Groups”。当前 Explorer Dirs 会话模型按窗格保存标签集合，没有已知的标签组实体或组切换命令。
- 公开页面只给出菜单文案，无法确认组作用域、命名/切换交互、是否跨窗格，以及与布局/会话的持久化关系。将此列为 P2 差距；先通过可见 Q-Dir 行为确认语义，再决定标签/工作区模型和 INI 迁移方案，不先写猜测性配置。来源：[Q-Dir 官方英文功能页](https://www.softwareok.com/?seite=Freeware%2FQ-DIR%2Flanguage%2FEnglish)。

## 2026-10-27：拒绝已过期 owner 的延迟 Shell View 命令

- 源码追踪发现，隔离 frame subclass 接收 Shell View `WM_COMMAND` 后会投递自定义消息再执行；Edit → Select All 分支传入 `IsCurrentOwner` 检查，但 View 菜单分支没有。若 pane 在两条消息之间切换，延迟命令会读取当前 frame 菜单的命令 ID，却向旧 owner 的 `IShellView` window 发送命令。
- 为 View 菜单命令增加执行前 owner 检查，并让 frame dispatcher 传入共享 coordinator 的当前 owner 状态。先加入回归场景，旧接口下 build 按预期失败；实现后 NativeProbe Release build 通过（0 警告、0 错误），`--shell-view-color-policy-smoke` 通过，包括隐藏 frame 上两 pane 同命令 ID、切换 owner 后拒绝旧 pane 命令。
- 验证未创建 `IShellView`，仅运行隐藏普通 HWND/OLE 菜单协调器；真实 Shell 菜单、pane 切换的物理时序和 WPF/产品宿主仍未验证。详见[IShellView 原型记录](ishellview-color-spike.md#2026-10-27拒绝已过期-owner-的延迟-shell-view-命令)。

## 2026-10-27：合并菜单命令 ID 只接受唯一组归属

- frame 原先按 View、File、Edit 固定次序查找延迟 `WM_COMMAND` 的 ID；消息没有来源组信息，因此跨组 ID 重复时会存在按优先级误判的风险。NativeProbe 现在检查全部六个 OLE 组，只有唯一出现在 File/Edit/View 之一才分派；重复、Object/Window/Help 组或未识别 ID 不发给 Shell view。
- 合成菜单 smoke 覆盖 File/Edit/View 唯一 ID、与其它组冲突时拒绝、实际到达隐藏假 HWND，以及 view 重入切换后拒绝旧命令；先补场景并确认旧代码无法编译，再实现。专用 `--shell-menu-group-routing-smoke` 通过，未创建 `IShellView`、未启动 ExplorerBrowser。真实 Shell composite 是否存在重复 ID 尚无观察证据；此改动只关闭歧义风险，不代表真实菜单命令已验证。详见[隔离原型记录](ishellview-color-spike.md#2026-10-27-合并菜单命令-id-必须唯一归属)。

## 2026-10-27：补充列头 popup owner 诊断

- `WM_INITMENUPOPUP` 发给菜单 owner window；原列头探针拿到这个 HWND 后只用它初始化子菜单，未记录其身份。现在 NativeProbe 在输出中增加 popup owner HWND 和最多八层父窗口类名链，后续运行时可确认弹出菜单由 header、view 还是宿主 frame 持有。
- NativeProbe Release build 通过，0 警告/错误；没有运行 `IShellView` 菜单探针，故实际 owner 与点击后命令接收窗口都仍未知。该诊断为后续隔离取证准备，不是菜单路由行为验证。详见[原型记录](ishellview-color-spike.md#2026-10-27-为列头-popup-记录消息-owner-窗口链)；消息契约：[WM_INITMENUPOPUP](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-initmenupopup)。

## 2026-10-27：用 WM_MENUSELECT 来源路由重复 ID 菜单项

- 之前无来源信息时，为避免误派发会拒绝跨组重复 ID；现把 `WM_MENUSELECT` 的子菜单来源保存在路由状态和延迟命令队列中。File/Edit/View 重复 ID 按选中的实际菜单组路由；Object/Window/Help 命令不交给 Shell view；无来源时仍要求唯一 ID。增加了 `WM_MENUCOMMAND`（按位置菜单）路径。
- NativeProbe Release build、专用 smoke 通过；新增隐藏普通 Win32 frame/pane 的完整 synthetic subclass 消息链回归，验证 `WM_MENUSELECT` → `WM_COMMAND` → coordinator `PostMessage` → pane `WM_COMMAND`，以及 `WM_MENUCOMMAND` 的 menu+index 路径。同一合法 ID `0x7123` 的 File/View 项按来源命中 group 0/group 2，Object 组同 ID 不派发；连续两个命令在来源切换后仍按 FIFO 保留各自菜单组，pane 停用后排队的旧命令不再到达旧 view。它不是系统菜单跟踪，不创建 Shell view；真实 Shell/OLE 菜单时序和产品行为仍未验证。详见[原型记录](ishellview-color-spike.md#2026-10-27-隐藏-frame-coordinator-消息链-smoke)。

## 2026-10-27：隐藏 frame 的合并菜单消息链验证

- 菜单来源测试现在经过实际 `ShellMenuFrameCoordinator` 窗口 subclass、命令 FIFO、延迟 `PostMessage` 与隐藏 pane HWND；File/View 重复 ID 经 `WM_MENUSELECT` 和 `WM_MENUCOMMAND` 均收到原始 `WM_COMMAND`，快速连续入队时来源也不会串线；Object 组命令不派发，pane 停用后旧命令不会送至已退休 view。NativeProbe Release build 0 警告/错误，`--shell-menu-group-routing-smoke` 通过。
- 边界：输入是手动合成的 Win32 消息，不是系统弹出菜单的模态选择；未创建 `IShellView`/ExplorerBrowser，也未测试实际 Shell/OLE 命令行为。真实 Shell 菜单消息收件窗口与先后顺序、可见体验仍待隔离原生宿主验证。详见[原型记录](ishellview-color-spike.md#2026-10-27-隐藏-frame-coordinator-消息链-smoke)。
## 2026-10-27：多行标签栏选项

- Q-Dir 公布的标签栏能力包含多行标签显示。本程序新增“视图 → 多行标签栏”，按可用宽度排列标签行，关闭横向箭头并提供纵向滚动；默认仍为原先单行滚动，开关保存于独立便携 `MultilineTabHeaders.ini`。标签头仍由现有 `TabHeaderPanel` 排序，不重建浏览器或导航历史。
- Release SettingsSmoke 覆盖默认关闭及开关往返、单行滚动范围与多行标签坐标；App Release build 通过。未运行可见 GUI；窄窗格、动态标签、拖放重排、标签数量增加后的滚动和焦点行为待验收。此项代码已实现，交互验收未完成。

## 2026-10-27：补齐快速筛选历史自动完成

- 对齐 Q-Dir FAQ 84：向下键展开已使用的筛选模式，Delete 移除下拉列表中选中的历史项。Explorer Dirs 以独立便携 INI 保存最近 50 个已应用模式，不与每标签筛选状态或命名筛选收藏混合；历史不可读、格式较新或被其他实例更改时保护原文件。
- Store 与按键策略由 Release SettingsSmoke 覆盖通过，App Release 构建 0 警告、0 错误；实际 WPF 下拉、实体键盘和多窗格交互待可见 GUI 验收。[Q-Dir FAQ 84](https://www.softwareok.com/?faq=84&seite=faq-Q-DIR)。


### 2026-10-27 文件区原生菜单基线

按用户要求暂停文件区自定义菜单注入：新建/复制/固定/关闭/恢复标签及复制路径不再追加到文件区原生菜单；标签栏菜单、主菜单及快捷键入口保留。未提供 CustomCommands 时不启动自定义目标 UIA 探测。仍保留主题兼容处理，不将菜单内容原生等同于无主题处理。构建通过；最新隔离副本准备后待可见验收。


### 2026-10-27 主题验收补充与子菜单贴合候选

用户验收通过：空白点击取消多选、原生菜单深浅往返、目录树主题跟随、094726 的菜单内外异常亮框消失。文件区暂停自定义命令注入。6209e88 已加入可见子菜单边缘补偿，左右展开/不累积位移的隐藏原生测试通过，真实 Shell 父子识别、连续鼠标进入、屏幕边缘与不同 DPI 仍待复测；最新候选 095843 尚未启动，不记为完整菜单体验通过。


### 2026-10-27 子菜单横向位置回归原生

用户明确要求父子菜单横向位置遵循资源管理器原生规则。当前不提供独立间距模式，因此删除 6209e88 的应用层位置补偿及其专用测试，不再枚举 HMENU 父子关系或调用 SetWindowPos 移动子菜单；展开方向、重叠量和屏幕边缘避让交回 Windows Shell。保留单层灰边、主题往返和目录树刷新。未来若明确提供独立间距选项，必须包含默认跟随原生和 0 间隔；本批不新增配置入口。NativeThemeColorProbe 和 Release 构建通过；裁剪后的视觉贴合仍需在新隔离候选中对照验收，不宣称已与 Explorer 像素一致。

### 2026-10-27 用户授权恢复裁剪补偿
用户明确授权：父子菜单按实际相邻裁剪宽度之和靠近，抵消本程序引入的透明距离，保留系统原始重叠量。不增加独立间距配置。保留当前贴合底色的单像素灰边。隐藏 HWND 用例验证向左/右展开的可见重叠量、尺寸/纵坐标不变及重复调用不累积位移；真实 Shell 父子识别、多级展开、边缘避让与不同 DPI 仍待用户验收。本批只准备隔离候选，不启动 GUI。

## 2026-10-28：修正标准 Shell 排序对照的导航标志

- 查明 `--view-data-capability` 误报多一个夹具项目：它把绝对 PIDL 与 `0x4000`（SDK 定义为 `SBSP_NAVIGATEBACK`）传给 `BrowseObject`，该标志忽略 PIDL，测试仍停留在父目录。改为 flags=0，并在读取视图前等待 `IPersistFolder2::GetCurFolder` 与目标 PIDL 一致。
- 分组后应用筛选会替换当前 `IFolderView2`。等待器不再沿用旧 COM 指针；它重取当前视图并连续三次验证预期 PIDL 行序。NativeProbe Release 构建 0 警告/错误；完整 `--view-data-capability` 三个独立进程均 exitCode 0，分组/筛选/导出组合通过。`--view-property-refresh-capability` 也 exitCode 0：普通 Shell 的 `PKEY_Size` 降序为 `five.txt|three.txt|child`，设置视图缓存不改变排序，刷新清空缓存。隐藏父窗口未显示，前台未改变。详见[逐行大小列调查](folder-size-column-investigation.md#2026-10-27类身份对照与探针缓冲区修正)。

## 2026-10-28：校准原生列排序实验

- 隔离 adapter 夹具现使用 2、10、10、100 字节文件。严格 adapter 的真实 `PKEY_Size` 升降序均按物理大小正确重排；旧式 `IShellFolderView::Rearrange` 传入自定义列索引 37 或显示位置 2 均返回 `S_OK`，但行序不变且没有调用 adapter 的 `CompareIDs`。现代自定义 PKEY 升降序仍未重排；标准大小属性升降序正常。Private Desktop 前台未变；探针以 exitCode 1 明确保留自定义排序失败，不把测试缺陷记成通过。
- 这把问题范围进一步收窄到 adapter 的自定义列属性消费/比较路由。未注册机器级 schema，未接入产品，也未改变 ExplorerBrowser。详细证据和下一步边界见[逐行大小列调查](folder-size-column-investigation.md#2026-10-28区分实际-size-排序与旧式列索引排序入口)。
- 微软 `PSRegisterPropertySchema` 文档要求安装阶段进行机器级注册并写入 HKLM，不能按用户注册；因此它与当前便携运行边界冲突，不能作为应用启动时的补救。若要继续验证，需要先解决属性 schema 是否必要、以及是否接受显式安装组件这两个问题；期间优先继续不依赖机器注册的 Q-Dir 差距项。[官方注册说明](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psregisterpropertyschema)

## 2026-10-28：更正文件区右键菜单当前状态

- Review 发现 `docs/shell-parity.md` 的现状表仍称文件区原生菜单前会插入自定义命令，并将 DirectUI 命中探测描述为当前菜单行为；这与用户已确认“不恢复此前自定义右键菜单”、`CustomCommands` 未接线的产品状态不符。现将现状改为 Shell 原生菜单，不注入自定义项；命中识别探针和未来重新评估条件只保留为历史/限制信息。
- 仅更新文档，没有启动窗口。后台验证将检查当前文档表述、`CustomCommands` 是否仍无产品赋值，以及 diff；不据此宣称完成可见 UI 验收。

## 2026-10-28：补采 temp.76 同一进程的运行时内存计数

- 对仍运行的 PID 58888 被动采集 55 秒 .NET runtime counters：累计托管分配约 19.81 MiB、1 次 Gen0、无 Gen1/Gen2，GC 提交量约 27.54 MiB、暂停约 1 ms；这些数值不能等同于存活堆或泄漏量。采集器退出后同 PID 再采样 60 秒，Working Set 与 Private Bytes 均稳定，但较前一段空闲基线高约 3.22/2.27 MiB；由于没有固定用户操作对照，暂不归因给采集器或应用活动。
- 全部采样只读，没有打开/操作 GUI。原始 CSV 留在忽略的 `src/.state/memory-samples/`，详见[内存调查记录](memory-sampling.md)。下一步需在记录明确操作序列后对照托管与进程指标，再考虑分配栈分析。

## 2026-10-28：移除闲置的文件区自定义菜单注入代码

- 用户已决定文件区右键菜单只使用 Windows Shell 原生内容，不恢复标签/路径自定义项。清除 ExplorerBrowserHost 中未接线的命令注入、目标 UIA 探测、临时菜单命令分派及其状态；保留 Shell 菜单主题/tooltip 处理、列头预设菜单门控和中键文件夹命中逻辑。删除不再使用的 `ShellMenuCommand`，保留隔离 NativeProbe 仍需的 ListView 命中解析纯函数及其坐标/命中 smoke。
- 后台验证：定向 `--shell-menu-target-smoke`、完整 SettingsSmoke 和 App Release 构建通过；未启动窗口。产品文件区空白/条目/键盘菜单的可见原生行为、浅深主题及多窗格一致性仍列入集中 UI 验收，不以编译或 smoke 代替。

## 2026-10-28：修复四窗格刷新探针的名称读取误报

- `HostProbe --live-updates` 曾在创建收敛为 `2,2,2,2` 后因 `hosts.Select(Names)` 将 pane 索引传成 `Names(maximumItems)` 而失败。改为显式单参数 lambda 后，真实产品 `ExplorerBrowserHost` 隐藏宿主连续 4 轮创建 `1→2`、删除 `2→1` 四窗格收敛均通过；创建约 0.87–1.03 秒，删除约 0.45–0.59 秒。完整 Release SettingsSmoke 通过。
- 本轮没有显示产品窗口或输入用户桌面。另一个更小的 NativeProbe/WPF 隐藏视图对照仍出现计数不收敛，暂按独立隐藏 DefView/消息泵差异调查；不据此改产品刷新代码，也不关闭可见 UI 验收。

## 2026-10-28：更正跨窗格拖放探针证据范围

- 后台复核发现 `docs/shell-parity.md` 曾记录当前探针覆盖 Copy|Move 和三种修饰键，但 `ShellViewDropTargetProbe.TryTransfer` 实际只允许 Copy；复跑日志也显示目标 `IShellView` 查询 `IID_IDropTarget` 返回 `E_NOINTERFACE`。使用真实 `SVGIO_SELECTION` `IDataObject` 调用已注册文件夹目标的 `DragEnter/DragOver/Drop` 可复制并保留源文件，另一路 `SHCreateDataObject` Copy 也通过。探针未运行 OLE `DoDragDrop`，不覆盖 Move 或产品跨窗格行为。
- 已修正文档表格，保留鼠标拖放、修饰键效果、源端效果交接和产品宿主为待集中 UI 验收。复跑在 Private Desktop 中完成，前台句柄不变；未启动产品 GUI。

## 2026-10-28：补充 WPF 隐藏视图刷新矩阵

- 为 NativeProbe 增加 `--live-updates-view-flags-wpf`，让已有的三种 Shell 视图标志对照可使用 WPF Dispatcher pump。后台矩阵退出码 0；三种场景四窗格创建与删除计数都收敛。此前该最小探针出现过一次间歇不收敛，后续重复通过仍不能证明产品可见 UI 或确定根因；不改产品刷新策略。细节见[自动刷新调查](live-update-investigation.md#2026-10-28-hostprobe-刷新序列误报修正)。
- 本批无可见窗口操作；可见多窗格刷新、启动和复制标签的 UI 验收留待集中进行。

## 2026-10-28：复验真实 ExplorerBrowserHost 四窗格刷新

- 当前 HostProbe `--live-updates` 隐藏序列连续两轮退出码 0：四窗格创建均从 `1→2`，删除从 `2→1`；创建耗时 1.024/1.028 秒，删除 0.582/0.623 秒，产品 watcher 创建/删除通知均为四个。第二轮日志哈希及证据边界见[自动刷新调查](live-update-investigation.md#2026-10-28-补验当前真实-explorerbrowserhost-四窗格序列)。
- 此结果加固本机隐藏生产宿主的正常本地目录路径；网络/扩展提供程序、长时运行以及可见焦点与重绘仍未验收，自动刷新 P0 保持开放。本批没有打开产品窗口或操作桌面。

## 2026-10-28：清理已销毁 Shell 子窗口的诊断缓存

- 源码审查发现右键诊断会按 HWND 缓存 Shell 子窗口类名；视图拆卸和 `WM_NCDESTROY` 原先没有同步移除映射，反复导航重建窗口时会在仍存活的宿主对象中留住旧句柄与字符串。本批在两种退休路径清理对应缓存。
- App Release build（0 警告/错误）和完整 Release `SettingsSmoke` 通过，未启动窗口或 ExplorerBrowser。此项只界定并消除一个可确认的有界诊断缓存生命周期问题；没有运行长时操作采样，不能声称它造成了用户观察到的显著内存增长或测得 MB 级收益。

## 2026-10-28：回收未提交的搜索位置映射

- 长会话源码追踪发现，搜索 URI/PIDL 身份在 `BrowseToIDList` 前登记，以便同步 Shell 导航回调能映射身份；若该阶段遇到 COM 重入而请求过期，或 Shell 持续返回 `ERROR_BUSY` 直到请求被新搜索取代，未进入 TravelLog 的映射仍会留到浏览器销毁。成功提交的历史映射仍必须保留，不能通过任意容量上限牺牲 Back/Forward。
- 现在只对被取代、未提交、失败或抛错的搜索请求按 PIDL 与 URI 精确移除两种别名；已成功提交的搜索映射保持原样。新增原生 PIDL smoke 在旧行为下先失败，修复后验证目标映射被移除、其他查询不受影响、释放的 Shell 名称别名可由后续搜索复用；完整 Release `SettingsSmoke` 与 App Release build 通过（0 警告/错误）。
- 一次 SettingsSmoke 与 App build 并行运行时，旧的 Program Starter 临时夹具清理报告目录被占用；串行重跑完整 SettingsSmoke 通过。未创建产品窗口或 ExplorerBrowser。该修复消除的是已确认的失败请求身份累积，尚未量化它对真实工作负载 Private Bytes 的影响，也不能替代操作期间的长期采样。

## 2026-10-28：补充 Q-Dir 标签组的公开语义证据

- 查阅 Q-Dir 官方 FAQ 101 后确认，其用户提问区有人询问如何显示已移入新组的标签；该 FAQ 实际只回答标签栏的显隐，并未解释标签组。官方英文字符串页仍只提供 Tab Group、Move、Copy、Use 等菜单名称。
- 这项证据支持继续将标签组列为 P2 待评估：语义需要在集中 Q-Dir UI 验收时实测，不能据同名浏览器标签组推断分组、显示或持久化方式。更新了[功能对照](shell-parity.md#标签组)；本轮没有启动 Q-Dir 或改产品行为。

## 2026-10-28：复验 Changed 元数据无额外 Flush 的排序更新

- Release HostProbe 的反射调用未传 `ShellColumnManager.Apply` 新增的可选上下文参数，先修复 HostProbe-only 调用并通过 Release build（0 警告/错误）。没有改产品逻辑。
- 设置 `EXPLORER_DIRS_HOSTPROBE_SKIP_FLUSH=1` 后运行真实生产 `ExplorerBrowserHost` 隐藏夹具：两文件大小更新，大小排序随之自然重排，选区/焦点保留，约 1,122 ms 收敛，整视图 fallback 为 0；父 HWND 隐藏且前台未变。详情及完整日志见[自动刷新调查](live-update-investigation.md#2026-10-28-复验不注入显式-flush-的-changed-元数据排序)。此证据不覆盖可见像素刷新、其他 Shell provider 或网络/云目录，自动刷新 P0 仍开放。

## 2026-10-28：temp.76 同一 PID 内存后续采样

- 对仍在运行的 temp.76/PID 58888 做 60 秒只读采样，13 个点的 Working Set、Private Bytes、虚拟内存、句柄和线程数在窗口内完全不变；与前一段 60 秒窗口接近，Private Bytes 高 8 KiB。未启动、重启或操作应用。
- 这只说明短时静置窗口稳定；没有固定标签/窗格和操作负载，不能据此排除运行期间增长或归因组件。原始 CSV 与哈希见[内存调查](memory-sampling.md#2026-10-28-csttemp76-后续-60-秒同-pid-只读采样)。

## 2026-10-28：temp.76 同一 PID 的 .NET 运行时计数

- 对同一运行进程做 30 秒只读 `dotnet-counters` 采样：进程 Working Set 约 244.61–244.65 MB，Private Bytes 约 117.55 MiB；GC committed 约 27.54 MiB，最近一次 GC 报告的堆大小合计约 17.94 MiB，托管分配流量约 9.63 MiB/25 秒，窗口内没有 GC。未注入用户操作、没有强制 GC。
- 这说明短时进程 Private Bytes 大于 GC 最近提交值，但不能将差额归因于特定组件；分配流量不等于保留量。后续需固定窗格/标签操作并结合原生分配类别采样。原始数据与哈希见[内存调查](memory-sampling.md#2026-10-28-csttemp76-同一-pid-的短时-net-运行时计数)。

## 2026-10-28：显式 OLE 初始化未改变 IShellView 菜单后销毁挂起

- 为隔离 NativeProbe 的 STA 在 Shell 窗口创建前显式执行 `OleInitialize` 增加 opt-in 对照，输入法保持启用；连续两次菜单激活/停用后的同步销毁都在顶层 parent `DestroyWindow` 挂起，50 秒 watchdog 后终止。WCT 均重复看到 COM activation → `ChsIME` 等待链，`cycle=False`，主桌面前台未变。
- 因而“仅缺少 OLE 初始化”不是充分解释。该等待链不是线程栈，且只发生在 Private Desktop 的隔离 `IShellView` 探针，不据此推断当前 ExplorerBrowser 产品故障。下一步单独评估 WPF Dispatcher/消息泵差异；`ImmDisableIME` 仍仅为诊断对照，不能用作产品修复。详细记录见[隔离原型诊断](ishellview-color-spike.md#2026-10-28显式-ole-初始化未改变菜单后销毁挂起)。

## 2026-10-28：标签标题最大宽度开关

- 对照 Q-Dir [FAQ 65](https://www.softwareok.com/?faq=65&seite=faq-Q-DIR)，将原本固定的 180 DIP 标签标题测量上限改为“视图 → 限制标签标题最大宽度”选项；默认开启以保留现有布局，关闭后允许标题按完整目录名自然测量。状态保存到独立便携 `TabHeaderMaximumWidth.ini`，隔离预览配置白名单也已加入该文件。
- 专项 smoke 覆盖偏好往返和 WPF 面板的实际 Measure 上限；完整 Release SettingsSmoke、App Release 构建与发布脚本隔离配置 smoke 均通过，构建 0 警告/错误，差异检查通过。未打开产品窗口；菜单可见状态、超长标题截断/自然扩展以及多行组合效果仍待集中 UI 验收。

## 2026-10-28：标签栏关闭按钮

- 对照 Q-Dir 12.55 的可选标签关闭按钮，为“标签页”菜单增加“显示标签关闭按钮”；默认关闭以保留现有紧凑标签栏。仅当窗格有多个标签且该标签未固定时显示 ×，最后一个标签仍保留，固定标签继续使用既有关闭限制。按钮直接关闭对应标签，不改变中键/双击快捷关闭。
- 设置存入独立便携 `TabCloseButton.ini` 并加入隔离预览复制清单。Release SettingsSmoke 覆盖配置往返与按钮显示资格；App Release build、完整 SettingsSmoke、发布脚本隔离 smoke 通过（构建 0 警告/错误）。没有打开窗口；按钮点击目标、移动标签后的呈现、浅/深主题视觉和可用空间仍待集中 UI 验收。

## 2026-10-28：减少 `.qdr` 导入的行拆分分配

- 源码审查发现，8 MB 输入上限之外，`string.Split('\n')` 还会同时保留行数组和逐行字符串。换行密集的 1,000,000 字符输入在修复前单次解析分配 16,385,488 字节。现在改用 `ReadOnlySpan<char>` 顺序扫描并只为实际字段分配字符串；同一 SettingsSmoke 复测分配 680 字节。完整 Release SettingsSmoke 通过，App Release 构建 0 警告/错误，`git diff --check` 通过。
- 本轮没有启动产品窗口、ExplorerBrowser 或 Q-Dir。此结果只量化合成输入上的托管临时分配，不代表峰值工作集或真实用户导入收益；字段语义和 8 MB 文件限制未变。详细记录见[Q-Dir 导入说明](qdr-import.md#2026-10-28-qdr-行扫描临时分配)。

## 2026-10-28：四窗格 WPF Shell 文件操作与通知收敛

- NativeProbe 在 Private Desktop 的四个 WPF 托管 `IShellView` 上，通过真实 `IFileOperation` 依次复制、重命名、删除；每一步都核对磁盘结果及四个视图的条目数和路径。连续 3 次通过；随后四 pane 菜单激活/停用和同步销毁也均通过，主桌面 foreground HWND 未变化。探针已加入 watchdog 入口，Release 构建 0 警告/错误。
- 这证明原型在同一目录下的 Shell 更新可传播到四个独立 view，尚未覆盖不同目录的隔离、真实 OLE 拖动、产品菜单/视图集成或可见 UI。产品仍使用当前 ExplorerBrowser；IShellView 仍处于隔离迁移评审，不据此启动产品迁移。详见[隔离 IShellView 原型](ishellview-color-spike.md#2026-10-28四窗格-wpf-shell-文件操作与通知收敛)。

## 2026-10-28：四窗格 WPF 宿主跨目录通知隔离

- NativeProbe 在 Private Desktop 中以三个同目录 pane 加一个独立目录 pane 运行四窗格 WPF `HwndHost → IShellView`；真实 Shell 复制、重命名、删除均使前三个视图更新，隔离 pane 内容/计数保持不变。连续 3 轮通过，并通过键盘路由、菜单激活/停用与同步销毁门槛；前台 HWND 不变，Release NativeProbe 构建 0 警告/错误。
- 该结果补上 WPF 宿主与不同目录通知隔离的组合证据，但不覆盖真实 OLE 鼠标拖动、产品可见 UI、第三方扩展菜单和颜色列行为；IShellView 仍在隔离评审阶段，产品未切换宿主。详见[隔离 IShellView 原型](ishellview-color-spike.md#2026-10-28四窗格-wpf-宿主跨目录通知隔离)。

## 2026-10-28：四窗格 WPF Shell 跨卷移动

- 在四窗格 WPF `IShellView` Private Desktop 探针中，使用源卷和临时目标卷执行真实 `IFileOperation::MoveItem`。断言源文件删除、目标文件创建及内容一致；源卷三 pane 计数 `3→2`，目标 pane 计数 `2→3` 并保留原内容。连续 3 轮通过，前台未变，临时夹具清理为 0 残留；Release NativeProbe 构建 0 警告/错误。
- 此项按路线图补齐跨卷 Shell 移动的双侧收敛证据，但不是 `DoDragDrop` 鼠标拖放，也不代表 ExplorerBrowser 产品集成或可见验收通过。详见[隔离 IShellView 原型](ishellview-color-spike.md#2026-10-28四窗格-wpf-shell-跨卷移动)。

## 2026-10-28：temp.76 进程内存被动复采

- 对仍运行的 temp.76/PID 58888 做 60 秒只读采样：Working Set `229.14–229.16 MiB`、Private Bytes `151.87–152.11 MiB`；句柄 `1144–1160`、线程 `40–45`。前约 40 秒计数稳定，随后小幅上升并回落；未操作应用，也没有记录窗格/标签负载。
- 这段静置数据不能归因计数波动或排除操作期间增长。原始 CSV、哈希和二进制身份见[内存采样记录](memory-sampling.md#2026-10-28-csttemp76-后续-60-秒被动复采)；后续仍需固定浏览操作负载并同步采进程与运行时指标。

## 2026-10-28：四窗格 WPF 选中项菜单隔离复验

- 为四 pane WPF 原型增加隐藏 `IContextMenu` 检查，每个视图都能为单项选择提供含 `copy` 的原生菜单；原生 Details/排序/分组映射、键盘路由和菜单 owner 清理同批回归通过。首轮发现菜单检查前四个 Shell view 均已有 2 项选择，系前序文件操作后的残留状态，旧断言把它误认为选择隔离失败；现于每项检查前后清空并核对所有 pane。
- Release NativeProbe build 0 警告/错误，修正后完整 Private Desktop 流程连续 3 次退出码 0，前台 HWND 未改变。仅是隐藏 COM/命令查询证据；菜单可见呈现、真实 OLE `DoDragDrop` 及产品 GUI 留待集中验收，产品仍未切换到 `IShellView`。

## 2026-10-28：WPF 隔离四窗格的新建文件夹菜单命令

- 在四 pane 隐藏 WPF `IShellView` 测试中，直接调用产品 `ShellFileCommands.Invoke(NewFolder)` / `NativeShellCommandMenu` 路径；该路径先初始化延迟“新建”子菜单，再通过当前菜单偏移调用 `IContextMenu::InvokeCommand`，返回 `S_OK`。此前未初始化子菜单的裸偏移调用返回 `E_FAIL`，canonical verb 字符串调用也能成功；本次改用产品适配器验证其真实路径。
- 新目录在同目录三个 pane 中出现，独立 Pane 4 保持不变。Release NativeProbe build 0 警告/错误，完整流程连续 3 轮退出码 0，前台 HWND 未变且 fixture 清理完成。产品 ExplorerBrowser 的真实菜单输入、重命名体验仍待集中 UI 验收。详情见[隔离原型记录](ishellview-color-spike.md#2026-10-28wpf-隔离四窗格的原生新建文件夹命令)。

## 2026-10-28：生产 ExplorerBrowser Ctrl+数字小键盘加号集成验证

- 新增 HostProbe 的隐藏验证入口，通过生产四窗格 `ExplorerBrowserHost.TranslateAcceleratorCore` 发送 Ctrl+VK_ADD，并用生产 `ShellColumnManager` 固定/读取列宽。Details 模式的 Name 列从 110 增至 419；另外三个窗格的列设置保持不变，焦点保留在当前原生文件列表。
- HostProbe Release build 0 警告/错误，隔离运行退出码 0；父窗口始终隐藏、前台 HWND 未变化，探针线程键盘状态恢复。没有打开产品窗口或发送实体键盘输入；本机实际数字小键盘与可见 UI 留待集中验收。

## 2026-10-28：四窗格 WPF `IShellView` 反向选择矩阵

- 在 Private Desktop 扩展四 pane 键盘 consumer：4 个窗格 × 8 种 Shell 查看方式共 32 组，验证反向选择的项目身份补集、连续反选恢复原选择、焦点保持及兄弟窗格选区隔离。连续 3 次通过；同批 Ctrl+A、F2 方向键、跨窗格输入隔离、Shell 文件操作通知、菜单生命周期及销毁清理均通过，前台 HWND 未变。NativeProbe Release build 0 警告/错误。
- 仅为隔离 `IShellView` 原型及生产选择命令的后台证据；未验证产品 `ExplorerBrowser`、物理键盘、可见 UI 或颜色兼容，产品仍未迁移。详见[隔离 IShellView 原型](ishellview-color-spike.md#2026-10-28四窗格-wpf-shell-反向选择矩阵)。

## 2026-10-28：生产 ExplorerBrowser 反向选择矩阵

- HostProbe 新增隐藏 `--shell-selection-invert-modes`，通过四个生产 `ExplorerBrowserHost` 和生产 `ShellSelectionCommands` 检查四窗格 × 八种查看方式：身份补集、再次反选恢复、原焦点保持、兄弟窗格选区与各窗格模式隔离。Release HostProbe build 0 警告/错误；连续 2 次完整矩阵通过，父窗口隐藏且前台 HWND 不变。
- 首轮探针未同步产品输入代际，导致旧选区恢复回调覆盖探针准备的状态；补入 `NoteNativeUserInput` 后问题消失。该证据覆盖隐藏 ExplorerBrowser 的命令行为，不覆盖物理快捷键、可见菜单/焦点交互、网络或大目录。详见[选择命令验证](selection-commands.md#生产-explorerbrowser-反向选择矩阵2026-10-28)。

## 2026-10-28：生产 ExplorerBrowser Ctrl+滚轮路由复验

- 使用隐藏 HostProbe 对生产 `ExplorerBrowserHost` 的真实子视图 HWND 合成 Ctrl+滚轮消息，按八个查看方式循环完整一周。两轮均从 Details 出发并回到 Details，三项选中身份保持，另外三个窗格的视图方式不变；父窗口保持隐藏，探针不读取桌面光标、不发送物理输入。
- 这是消息路由与 Shell 状态读回证据，不代表真实鼠标消息一定命中该 HWND，也不覆盖高精度实体滚轮、WPF 工具栏区域焦点、可见绘制或普通滚轮滚动；这些留待集中 UI 验收。没有修改产品逻辑。

## 2026-10-28：全局停用文件视图分组

- 对齐 Q-Dir 官方“完全停用所有文件列表分组”与当前 Shell 视图右键菜单的单视图分组操作；新增 View 菜单开关和独立便携 `DisableGrouping.ini`。启用后将 `FWF_NOGROUPING` 应用到现有及新建 Shell 视图，停用前缓存每标签分组键/方向，停用期间不把原生无分组状态覆盖进会话，关闭时恢复标签值。
- 过滤器分组延迟恢复队列服从全局覆盖。`SettingsSmoke` 对 flag 保留、profile/窗格/新标签接线及缓存状态策略通过；完整 Release SettingsSmoke 通过，App Release build 0 警告、0 错误。没有打开 GUI 或真实 ExplorerBrowser 窗口；分组菜单可见禁用状态、各标签切换及重启恢复仍待集中验收。依据：[Q-Dir 完全停用分组说明](https://www.softwareok.com/?page=Windows%2FQ-Dir%2FTips%2F11)、[Windows FOLDERFLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags)。

## 2026-10-28：标签菜单外部 Explorer 浏览与空白处右键

- 标签页右键菜单首项新增“使用 Explorer 浏览”，将该标签已确认的 Shell 当前路径作为单一参数交给 Windows `explorer.exe`，避免用 Explorer Dirs 打开第二个标签/窗格。标签栏空白处右键复用当前选中标签的上下文菜单；原有空白处左键双击新建标签及标签项双击关闭逻辑保持不变。
- `SettingsSmoke` 验证磁盘路径（含空格）和 Shell 虚拟位置作为单个进程参数，菜单首项/右键空白区路由及原有双击规则；进程未实际启动。Release SettingsSmoke 与 App Release 构建结果见本批记录。未打开 GUI，真实 Explorer 窗口复用/路径显示和空白区命中位置仍待集中验收。

## 2026-10-28：跨窗格 IShellView 拖放 Move 验证前置检查

- 按“跨窗格与 Explorer 互操作”P0 门槛，先重跑既有隐藏 Copy 基线，再扩展 Move 对照。NativeProbe Release 构建 0 警告/错误；隐藏目标文件夹收到来自真实 `SVGIO_SELECTION` 数据对象的 Copy 后，目标内容正确、源文件保留。该运行不是完整 OLE `DoDragDrop`，不证明鼠标拖放闭环。
- 退出码为 1：Private Desktop 外层监督记录前台 HWND 从 `0x10EA8` 变为 `0x7514D4`，尽管内层文件夹目标探针报告其局部前台未变。该桌面隔离条件未通过，因此本轮停止后续 Shell/Private Desktop 探针，没有尝试恢复前台，也没有运行 Move。外层前台差异的来源待查；修复或明确归因前，不以这次运行支持桌面无干扰结论。

## 2026-10-28：工具菜单加入 Windows Explorer Shell 重启入口

- 工具菜单新增“重启 Windows 资源管理器”。用户确认后，通过桌面 Shell 窗口定位其所属 explorer.exe，后台结束该 Shell 进程并启动 Windows Explorer；先等待系统可能进行的自动 Shell 恢复，避免重复启动。未枚举并结束所有 Explorer 进程。
- App Release 构建 0 警告/错误；没有实际重启 Explorer 或进行 UI 验收。Shell 自动恢复、桌面/开始菜单/任务栏恢复时间、Explorer 文件窗口与 Shell 是否共享进程仍需可见验收。

## 2026-10-28：从主分支准备新的集中验收候选

- 发现旧集中验收文档仍指向已删除的 worktree。使用当前干净 main 提交 `89116c8` 重新运行 GUI smoke `PrepareOnly`，生成 `src/.state/gui-smoke/20261028-045252-741-88d8d1`；清单为 `prepared-not-run`，EXE/DLL 哈希复核一致，候选进程数为 0，未启动窗口。
- 当时将新副本记录为验收基线；原 worktree 候选保留为历史记录并明确标记不可用。随后加入 Explorer Shell 重启验收项并准备更新候选，详见下条。

## 2026-10-28：将 Explorer Shell 重启纳入集中验收

- GUI smoke 清单新增一项：先验证取消确认不触碰 Shell；实际重启只在保存工作并获明确授权的桌面会话中执行，记录桌面/任务栏/开始菜单恢复及 Explorer 文件窗口是否共用 Shell 进程。
- 从干净 main 提交 `50cb324` 重新 `PrepareOnly`，候选 `src/.state/gui-smoke/20261028-045534-826-74a861` 的 EXE/DLL 哈希复核一致，状态 `prepared-not-run`，进程数为 0；28 项可见验收仍未执行。当前基线见[候选身份](gui-acceptance-2026-10-28-main-prepared.md)。

## 2026-10-28：限制 Explorer 重启目标进程

- 源码复核发现重启工具此前仅凭 `GetShellWindow()` 的 PID 结束进程，未检查进程映像；自定义桌面 Shell 可能被误结束。现在先确认 Windows 目录下的 `explorer.exe` 存在，再要求 Shell 窗口所属进程映像规范化后与该文件路径大小写不敏感地完全相同，否则不结束任何进程并返回明确错误。
- 路径身份 SettingsSmoke 先因缺少检查而按预期失败，接入后通过；完整 Release SettingsSmoke 通过，App Release build 0 警告/错误。未启动、结束任何 Explorer/Shell 进程，也未进行 UI 验收；取消确认与真实桌面恢复仍待集中验收。

## 2026-10-28：确认 Explorer Shell 自动恢复的进程身份

- 源码复核发现重启等待阶段把“桌面 Shell PID 变化”当作 Windows Explorer 已恢复；另一个进程短暂接管 Shell 窗口时可能误报成功。现在等待阶段还会读取新 PID 的映像并要求匹配 `%WINDIR%\explorer.exe`；进程在读取 HWND 与打开进程句柄之间退出时继续等待。
- 身份策略回归先因缺少替代进程判定失败，再通过“新 PID 且映像匹配”“旧 PID”“非 Explorer 映像”“原 PID 缺失”四种情况。完整 Release SettingsSmoke 与 App Release build（0 警告/错误）通过；未结束或启动任何系统进程。Windows 实际 Shell 自动恢复与 fallback 启动仍需集中可见验收。

## 2026-10-28：验证 Explorer 重启的 fallback 恢复

- 补齐手动启动路径：Windows 自动恢复等待超时后启动 `explorer.exe`，还会继续等待最多 10 秒，只有检测到不同于旧 PID、且映像确认为 `%WINDIR%\explorer.exe` 的桌面 Shell 才报告成功；仅仅启动进程不再算恢复完成。
- Release SettingsSmoke 覆盖旧 Shell 持续存在时不得误报、有效新 Shell 出现后立即成功及有界轮询耗尽；完整 Release SettingsSmoke、App Release build（0 警告/错误）和 `git diff --check` 通过。没有操作实际 Explorer 进程或打开 UI；自动恢复与 fallback 的桌面、开始菜单和任务栏体验仍待集中可见验收。

## 2026-10-28：补记 Q-Dir 列头过滤差距

- 官方 FAQ 38 将按列过滤单独列在通配符快速过滤之后；当前 `QuickFilterSettings` 只匹配 Shell 项目名，没有按活动列读取属性值。将其记录为待评估的 P2 差距。公开文字未明确列标题触发方式、多列组合规则或筛选状态呈现；先取得更具体的交互证据，再决定是否及如何实现，不改动现有原生列标题菜单。详见[核心操作对照](shell-parity.md#q-dir-列头过滤差距2026-10-28)。

## 2026-10-28：补充列头筛选交互证据

- FAQ 38 页面确认每个 Explorer view 有独立设置，并提供“通过列表列标题过滤”的独立示例；示例图显示列标题上的筛选入口会打开该列的复选值清单。上方旧记录中“未公开具体手势”的判断已过期。实现建议为每标签保存列属性值筛选，与现有名称筛选合并；多个列条件按 AND，属性无法读取时保留项目。先做隔离表头消息/入口与 `IShellFolder2` 属性读取验证，再决定产品接线，不替换 ExplorerBrowser。来源：[Q-Dir FAQ 38](https://www.softwareok.com/?faq=38&seite=faq-Q-DIR)。

## 2026-10-29：IShellView 已注册属性列排序诊断

- Private Desktop 原生 Header 输入 A/B 显示：标准 `PKEY_Size` 列能在升降序间实际重排，已注册 ByteCount 列只切换方向状态、行序不变。无窗口 `IPropertyDescription` 确认两者均有排序描述。证据指向新增属性列的 DefView 比较/排序值消费路径；列值可显示不代表排序可用。NativeProbe Release build 与无窗口合同 smoke 通过；真实视图探针保留未满足私有列门槛的 exit code 1。
- 下一步只在 NativeProbe 中继续隔离 DefView 的排序消费路径；不得修改产品 ExplorerBrowser 或发布包。只有自定义属性列升序、降序都实际按数值重排，并完成 Shell 操作、焦点、拖放和通知等现有迁移门槛后，才评估产品架构迁移。
- ByteCount 和标准 Size 的属性描述均为 `PDDT_NUMBER`，`GetSortByAlias` 均返回 `S_FALSE` 且无主排序别名；已排除显示类型及主排序别名是本次差异的明显解释。继续检查 `IShellFolder2` 列索引到属性键的映射及 DefView 的比较调用路径。
- 列映射计数对照已完成：DefView 初始化会调用 `MapColumnToSCID` 列举扩展列，但排序阶段不调用该映射或 adapter `CompareIDs`；`GetDefaultColumn` 在该绑定视图生命周期中也没有被调用。适配器的直接映射合同本身有效。后续聚焦可支持的 DefView 排序委托/通知接口，仍不把状态读回当作已重排。
- `SFVM_GETSORTDEFAULTS` 回调收到一次，但按 SDK 参数写入 ByteCount 列并返回 `S_OK` 后，视图仍无排序状态、无重排、无 `CompareIDs`；其官方文档明确说系统文件夹视图对象不识别此消息。当前合成点击路径也未触发 `SFVM_COLUMNCLICK`。不再将这两个旧 callback 消息视作排序修复入口；后续验证要转向实际 ListView/Shell 排序委托边界及其完整操作兼容成本。
- 数值排序结论复核：先以真实 `IFileOperation` 复制验证 2→3 项通知，再用名称顺序值为 `10/2/10` 的夹具排除名称顺序偶合。标准 `PKEY_Size` 能正常升降序；系统 ByteCount 列升降序两次都保持同一名称顺序和值 `10/2/10`，方向读回虽切换 `+1/-1`，实际未重排。后续调查改查 DefView/SysListView 排序委托边界；不把实验架构接入产品。
- Private Desktop `SysListView32` 只读 subclass 观察：标准 Size 行序可以改变，但同一排序时段没有发给列表 HWND 的 `LVM_SORTITEMS/LVM_SORTITEMSEX` 或项目插入/删除/设置消息；ByteCount 也没有排序消息或新的属性读取。此证据支持“DefView 在内部项目顺序层处理排序”的推断，限定于已观察 HWND 和 LVM 消息，不断言 Shell 内部实现。下一步追 `SetSortColumns`/Header 通知到 DefView 比较与项目序列的调用边界。
- 对标准 `PKEY_Size` 做了 `IPropertyStoreFactory` 覆写 A/B：逐项读取确认返回 `a-ten.bin=100`、`two.bin=200`、`z-ten.bin=10`，但原生 Size 升序及 Header 升降序仍按磁盘实际字节大小排列。该 DefView 路径忽略子项属性工厂的 Size 覆写；不再投入“只替换属性存储”的方案。NativeProbe Release 构建通过；完整 Private Desktop 运行仍以 exit code 1 停在原有私有测试列为空值门槛，前台/输入守卫未变化。产品与 release 未改。详见 `folder-size-column-investigation.md` 的 2026-10-29 记录。
- 尝试在隔离 adapter 的 `GetAttributesOf` 屏蔽 `SFGAO_FILESYSTEM|SFGAO_FILESYSANCESTOR`，但创建该 Shell view 时此 adapter 的 `GetAttributesOf` 调用数为 0，故没有实际改变视图属性，不能据此判断自定义 namespace 排序。无效探针接线已移除；下一步先查找 DefView 取得文件夹/属性身份的真实入口，随后再考虑独立的非文件系统 Shell namespace 对照。
- 查阅微软官方 Explorer Data Provider sample 后，确认其自有 PIDL 数据由 `IShellFolder2::CompareIDs` 按大小比较；这为独立虚拟 namespace 提供了技术先例，但不是当前文件系统 PIDL 包装 adapter 的行为证据。已收束属性覆写/别名/列映射方向，下一验证应是独立非文件系统 PIDL 样本中的 `CompareIDs` 调用计数与 DefView 升降序行序。由于 Private Desktop 前台守卫仍有未解释的用户桌面 HWND 变化，本轮不创建新的 Shell view，先保留此隔离约束；产品继续使用 ExplorerBrowser。
- 独立合成 folder 的 Private Desktop 探针重跑后，`IFolderView2::GetFolder` 的 `IUnknown` 身份确为自有 `IShellFolder2`，但 DefView 项目数为 0，且没有调用该对象的 `EnumObjects`；物理目录 PIDL 显示实际目录，空 PIDL 指向 Desktop。前台与输入守卫均通过。这个未注册对象实验没有验证 `CompareIDs`；若要继续，需先设计可清理的虚拟 Shell namespace 注册与绑定方案，本轮不写注册表、不改产品/release。细节见 `folder-size-column-investigation.md`。
- 最小 COM class factory 和可回收的专用 `HKCU\Software\Classes\CLSID` 探针构建通过；watchdog 和 probe 都会清理此键，实测退出后键不存在。但仅注册 CLSID 后，绝对路径 `::{GUID}` 仍返回 `E_INVALIDARG`，没有触发 DefView 枚举。微软文档要求将 Shell namespace extension CLSID 挂到命名空间位置；再加 Desktop\NameSpace 项可能影响 Explorer 的桌面/导航窗格缓存，等待用户确认后再执行。未改产品/release，前台及输入守卫未变。详见 `folder-size-column-investigation.md`。

2026-10-28：Private Desktop 前台守卫现每 25 ms 记录首次 HWND 变化及最近探针阶段；drop target 路径在源/目标视图创建、Copy 阶段和清理前更新标签。NativeProbe Release build 0 警告/错误；无 Shell-view `SW_SHOWNOACTIVATE` 对照退出码 0、前台未变，验证监视没有破坏该路径。未重跑 Shell-view 探针，故尚未定位实际前台变化来源；继续保留守卫，不在未确认隔离条件时重复创建 Shell view。详见[阶段监视记录](ishellview-color-spike.md#2026-10-28-private-desktop-前台变化阶段监视)。

2026-10-28：列值筛选查表优化已提交（a73e467）。每列候选值达到 8 个时预建大小写不敏感 HashSet，避免 Shell 项目筛选对最多 256 个值逐项线性比较；较小列表继续线性查找以避免额外集合分配。新增边界 smoke 覆盖第 256 个匹配值和未选择值；完整 Release SettingsSmoke、App Release build（0 警告/错误）和 `git diff --check` 通过。真实 Shell 属性读取与可见筛选效果仍待集中验收。

## 2026-10-29：收敛 DefView 自定义列的支持边界

- 根据微软 `CreateViewObject` 与 namespace extension 文档复核，系统 DefView 可选择由文件夹创建独立 `IShellView`，但没有文档化的扩展回调能给现有 DefView 增加逐项绘制/排序值；自定义 view 则要自行负责子窗口与呈现、快捷菜单、交互及拖放等，微软也建议在功能要求允许时优先系统 view。
- 这不是对所有 Shell 内部路径的绝对否定，而是针对已观测的 DefView 不请求属性值的当前样本，排除继续堆叠属性存储覆写、列映射或 legacy callback 的无依据路线。隔离迁移下一步收敛为有效 namespace 父级中的虚拟 PIDL/`IShellFolder2::CompareIDs` 实验。
- 该 Private Desktop view 实验需要临时 Desktop\NameSpace 挂载；用户尚未明确授权，未写注册表、未创建 view、未改产品或 release。待授权后验证解析、列读取、数值排序、Shell 文件操作与清理。依据：[CreateViewObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)、[Namespace extension overview](https://learn.microsoft.com/en-us/windows/win32/shell/nse-works)、[Implementing a folder view](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)。

## 2026-10-29：独立虚拟 Shell 视图通知与文件操作原型

- 用户授权后，在 Private Desktop 临时注册的 Shell namespace 中，以合成非文件系统 PIDL 和 backing 临时目录运行两个独立 DefView。原生 `CompareIDs` 的 Size 升降序、空白/选中 `IContextMenu`、选中项 `IDataObject` 的 `CF_HDROP`/CIDA、真实 `IFileOperation::CopyItems` 均通过。
- 通过 `SFVM_GETNOTIFY` 订阅 backing folder 后，真实 `IFileOperation` 重命名和删除均到达 `SFVM_FSNOTIFY`。在回调中直接同步 `IShellView::Refresh` 会导致 `0xC000041D`；改为 STA 消息队列定时器延后刷新后，重命名和删除均能自动反映到 DefView，显式刷新后的磁盘枚举也正确。双视图验证先复现只有 view 1 更新；改为每个 view 独立维护待刷新状态和 STA 定时器后，两个 view 都能收到更新，并分别恢复未变化文件 `two.bin` 的选择与焦点；行尚未完成枚举时会在 2.5 秒内有界重试恢复。
- NativeProbe Release build、原生 `/W4 /WX` build 和 `git diff --check` 通过；watchdog 退出后专用 HKCU namespace/CLSID、源夹具和复制夹具均清理，前台窗口未变化。实验没有打开用户可见窗口。
- 隐藏双视图 Private Desktop 操作、构建和前台守卫通过，临时注册与夹具退出后已清理。最新又将两个延迟刷新同时排入队列，在释放 view hold 后泵送 STA 400 ms；两轮均确认两个待处理 timer 被取消且释放后刷新回调数为 0。此项验证探针的显式 hold-release 清理顺序，不等同于 Shell 在 view HWND 销毁消息中途重入或并发通知的竞态测试。证据仍未覆盖多选、被重命名/删除项本身的选区语义、真正 OLE 拖放、键盘交互、第三方菜单扩展、失败/撤销路径、性能或可见体验。用户授权继续隔离架构原型；产品仍使用 ExplorerBrowser，产品迁移尚未获批。后续按迁移门槛逐项补证，最终集中做 UI 验收。详见[IShellView 架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。

补充验证（2026-10-29）：立即在 `IShellView::Refresh` 后恢复时，DefView 行数暂时为 0，无法定位选中项；增加延迟恢复后，在未变化的单个文件 `two.bin` 上 selected/focused 状态恢复为 `0x3`。双视图实验发现单一全局待刷新槽会漏掉第二个视图；改为每视图独立 pending 与 STA 消息队列定时器，并在未完成枚举时最多重试 2.5 秒后，两轮 Private Desktop 运行中两个视图的重命名/删除自动刷新、单选与焦点恢复均通过。随后以虚拟 view 的选区 `IDataObject` 调用 `IFileOperation::RenameItems` 和 `DeleteItems`，两个 view 均收到 Shell 通知并保留未操作项的选择/焦点；最终实现连续两轮通过。再对两个 view 同时排入刷新并调用探针 hold-release，随后泵送 STA 400 ms；两轮均确认待处理 timer 被取消、回调计数不再增加。此证据覆盖显式释放顺序，不覆盖 view HWND 销毁过程中的重入/并发竞态。多选和被操作项目本身的选择语义仍属迁移门槛。

2026-10-29 IShellView 生命周期后续验证：在双视图均有 pending refresh 时先销毁两个 IShellView 窗口，并以 GetWindow/IsWindow 证实 HWND 均失效；两轮均观察到两个延迟回调仍在 HWND 销毁后运行，包含 Refresh 与选区恢复，随后显式释放 view hold 后无新回调。说明 owner 必须先取消每 view 的待处理 timer 并撤销回调持有，再调用 DestroyViewWindow。这不是 use-after-free 证据（timer 持有 COM 引用），也未覆盖 DestroyViewWindow 调用内部重入、跨线程竞争或产品宿主。真实 OLE DoDragDrop 循环仍是 IShellView 迁移门槛的重要缺项。探针 Release build 0 警告/错误；两轮 Private Desktop watchdog exit 0、前台未变化、HKCU 探针注册及临时目录清理。

## 2026-10-29：隔离 Shell 视图通知合并与逐视图取消

- 隔离 NativeProbe 发现：view 1 收到文件系统通知后进入延迟选区恢复期间，紧接着到达的第二条 `SFVM_FSNOTIFY` 被旧逻辑合并丢弃，导致列表可能停留在旧内容。探针现在在当前刷新周期结束时安排一次 follow-up refresh；同一刷新周期内多条通知仍合并为一次后续刷新。
- 新增逐 `IShellView` 的取消入口，并验证先取消 view 1 的待处理 timer、再销毁 view 1；view 1 不再回调，view 2 的 HWND 仍存活且它自己的待处理刷新正常执行。释放剩余 view hold 后没有额外回调。
- 两轮 Private Desktop watchdog 均退出码 0；`late-update.bin` 在第二条通知后可见、follow-up 回调数为 1、兄弟视图仍存活、释放后回调数为 0、前台 HWND 未变化。临时 HKCU CLSID/NameSpace、TEMP 夹具和 NativeProbe 进程均已清理。
- 本次只更改隔离原型，产品 ExplorerBrowser 未迁移。该证据不覆盖产品宿主、真正 OLE 拖放、`DestroyViewWindow` 内部重入、跨线程并发 Shell 通知或可见 UI。后续架构门槛继续覆盖生命周期、焦点/导航和真实拖放，最终集中做 UI 验收。
2026-10-29 刷新通知 burst 合并复验：在 view 1 的选区恢复窗口中连续注入三条 `SFVM_FSNOTIFY`，两轮 Private Desktop watchdog 均只触发 1 次 follow-up refresh，新增 `late-update.bin` 最终可见；view 1 取消/销毁后 view 2 单独刷新，释放后回调数为 0。两轮退出码 0、前台 HWND 未变；HKCU 探针注册、TEMP 夹具和探针进程均已清理。此结果只验证隔离原型同一刷新周期内的 burst 合并，不外推到产品宿主、跨线程通知或真实 OLE 拖放。
2026-10-29 IShellView 文件操作取消门槛：从 synthetic view 的 `SVGIO_SELECTION` 取得实际 Shell `IDataObject`，经 `IFileOperation::Advise` 连接进度 sink，并连续排入两次 `CopyItems`。`PreCopyItem` 第一次调用返回 `E_ABORT` 后，两轮均仅回调一次、`PerformOperations` 返回 `E_ABORT`、取消目标目录为空；`GetAnyOperationsAborted` 读数为 0，仍作为独立状态记录。两轮 Private Desktop watchdog exit 0、前台不变，临时注册/夹具/进程均已清理。仅是隔离默认 DefView 与本地临时文件证据，不代表产品对话框或真实用户取消验收。依据：[PreCopyItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-precopyitem)。

2026-10-29 IShellView 拖放目标按键效果补充：隔离 Private Desktop `IDropTarget` 直接调用通过 Ctrl+Copy、同卷无修饰键默认 Move、同卷 Shift Move、C:→D: 跨卷无修饰键默认 Copy、跨卷 Shift Move 五种情景；核对目标内容、源文件状态及 `Performed/Logical Performed DropEffect`。优化移动中 `Drop` 返回 effect=0，但两个完成效果都报告 Move=2。五种情景连续两轮 watchdog exit 0，前台/输入守卫与 C:/D: fixture 清理通过；日志 `20261029-235600-bac41385.stdout.log` 与 `20261029-235608-c64ecae4.stdout.log`。NativeProbe Release build 0 警告/错误。真实 `DoDragDrop` 源端循环、鼠标命中、产品接线/可见验收仍未完成；IShellView 迁移门槛继续开放。

2026-10-29 多选 Copy 取消后视图状态：扩展同一隔离 NativeProbe，让 DefView 选中 `two.bin` 与 `z-ten.bin`，把 selection `IDataObject` 交给两次排队的 `IFileOperation::CopyItems`，并在 `PreCopyItem` 返回 `E_ABORT`。两轮完整 Private Desktop watchdog 均确认取消后仍选中 2 项，`two.bin` 保持 focused，目标目录为空，前台守卫和清理通过；Release build 0 警告/错误。之后一次附加 CIDA 计数实验读到 2 个子项，但整轮后来因前台 HWND 改变并在后续第二视图焦点断言失败而中止，因此不作为整轮通过记录，也不保留该额外断言。该验证仅覆盖隔离 DefView 的程序化 Copy 取消与多选/焦点保留，不代表产品 ExplorerBrowser 或真实用户取消行为。

## 2026-10-29：合成 Shell 文件夹树合同（无视图）

- NativeProbe 的临时合成 `IShellFolder2` 现在能枚举实际夹具中的目录与文件，遵循 folder/non-folder/hidden flags，并将子目录绑定为带正确根 PIDL 的下一层适配器；覆盖根/子层 `ParseDisplayName`、`GetCurFolder`、`BindToStorage(IStream)` 和真实隔离目录重命名。底层 filesystem `ParseDisplayName` 成功时不填写 `eaten`，适配层现按成功映射的名称长度返回该值。
- 原生适配器 `/W4 /WX` 与 NativeProbe Release 构建通过，0 警告/错误；树级 smoke、既有普通 PIDL 合同及包装 PIDL 合同通过。smoke 创建并清理临时 fixture，未创建 `IShellView`、窗口或触发可见 UI。
- 这补足 namespace 树和子文件夹存储合同，不证明 DefView/自绘列表的导航、焦点、菜单、拖放、通知或产品集成；产品仍使用 ExplorerBrowser。后续需在独立 Windows 会话里运行真实视图/输入门槛，再评审是否继续迁移。

## 2026-10-29：合成 Shell 文件夹树的多级路径合同

- 将合成 `IShellFolder2` 的 `ParseDisplayName` 从单项扩展到分隔路径：逐段委托 filesystem Shell，生成包含每层合成 `SHITEMID` 的相对 PIDL，并返回完整 consumed 字符数。`BindToObject` 递归支持多层文件夹 PIDL；`BindToStorage` 可从根直接打开深层文件流。
- NativeProbe 临时树 smoke 验证 `child\nested.txt` 的双段 PIDL、`child\nested-folder` 的组合绑定与深层枚举、从根打开 `nested.txt` 并读回内容；既有 enumeration、hidden、单层绑定/流、`GetCurFolder` 和重命名合同仍通过。原生 `/W4 /WX`、NativeProbe Release 构建及普通/包装 PIDL smoke 均通过，0 警告/错误。
- 测试仅用真实临时文件夹和 COM `IShellFolder2` 方法，未创建 `IShellView`、窗口或用户桌面输入。它推进的是 namespace PIDL 层级合同；Shell 的真实导航/DefView、焦点、菜单、拖放、通知及产品接线尚未验证，产品仍使用 ExplorerBrowser。

## 2026-10-29：合成 Shell 项目的原生菜单与数据对象

- 树级 smoke 现在从合成文件夹取得普通文件的原生 `IContextMenu` 并查询默认命令，同时取得原生 `IDataObject` 的 `CF_HDROP`，核对其中仍是 backing filesystem 的真实夹具路径。菜单只在内存 HMENU 中构造，不显示、不调用命令；没有创建 Shell view 或窗口。
- 新增菜单与数据对象合同后，树级 smoke、普通 PIDL 合同和包装 PIDL 合同通过；NativeProbe Release 与原生适配器 `/W4 /WX` 构建通过，0 警告/错误。该项验证 adapter 未切断选中项的系统菜单和文件传递数据；不代表菜单可见交互、OLE 拖放闭环或产品 ExplorerBrowser 集成。

## 2026-10-29：合成项多选文件数据对象

- 扩展临时树夹具至两个可见文件；从适配器 `GetUIObjectOf(IDataObject)` 为两项同时取得 Shell 数据对象，并验证 `CF_HDROP` 中两条完整路径都指向被选条目（不依赖返回顺序）。单项原生菜单合同和此前多级路径/绑定/流合同仍通过。
- 原生 `/W4 /WX`、NativeProbe Release 和树级、普通 PIDL、包装 PIDL smoke 通过，0 警告/错误。仅证明多选 `IDataObject`/`CF_HDROP` 转发；不证明真实选择输入、粘贴/拖放闭环或产品视图行为。

## 2026-10-29：合成多选数据对象驱动 IFileOperation 复制

- 将合成文件夹返回的双项 `IDataObject` 直接交给 `IFileOperation::CopyItems`，目标为 fixture 内的真实临时目录；随后调用 `PerformOperations` 并检查 `GetAnyOperationsAborted`。目标中的两个文件内容均与源一致，源文件仍保留。
- 使用 `FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI`，未设置 owner window 或进度对话框。原生 `/W4 /WX`、NativeProbe Release、树级 smoke、普通/包装 PIDL smoke 通过，0 警告/错误；临时树在退出时清理。
- 这是无 Shell view 下真实本地文件系统的 Shell 批量复制证据，提升了阶段 4 的操作合同覆盖；不验证产品 ExplorerBrowser、可见进度/冲突确认、剪贴板循环或 OLE 鼠标拖放。

## 2026-10-29：合成多选数据对象驱动 IFileOperation 移动

- 扩展树级临时 smoke：对原生 Shell 文件夹返回的双项 `IDataObject` 调用 `IFileOperation::MoveItems`，再执行 `PerformOperations` 并读取中止状态。两个源文件均消失，目标目录中内容逐项一致；源夹具在 smoke 退出时统一清理。
- 操作继续使用 `FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI`，不显示进度/确认/错误对话框。原生 `/W4 /WX`、NativeProbe Release、树级 smoke 及普通/包装 PIDL smoke 通过，0 警告/错误。只证明无视图下本地 Shell 多选 MoveItems，不证明用户剪切/粘贴、跨卷、多 provider 或产品视图更新。

## 2026-10-29：合成多选复制的同名冲突处理

- 在临时目标目录预置内容为 `keep-existing` 的 `root.txt`，用合成文件夹提供的双项 `IDataObject` 调用 `IFileOperation::CopyItems`，启用 `FOF_RENAMEONCOLLISION` 与静默/无确认/无错误 UI flags。
- 验证原目标内容完全保留、第二个非冲突文件复制成功，并且目录出现另一个 `root*` 文件；检查中止状态。NativeProbe Release 与原生 `/W4 /WX` 构建、树级和既有 PIDL smoke 通过。只证明本地 filesystem 的冲突重命名策略，不覆盖产品 UI 冲突对话框或 Shell provider。

## 2026-10-29：合成多选数据对象驱动 DeleteItems

- 复制完成后重新绑定合成 `copy-destination` 文件夹并枚举两份生成文件，从该文件夹取得多选 `IDataObject`，交给 `IFileOperation::DeleteItems`；执行后检查 `GetAnyOperationsAborted` 并确认两个副本路径消失。
- 删除范围限定在本轮临时 fixture 副本，保留根目录源文件供后续 `MoveItems` 检查。该验证补充无视图的本地批量删除合同，不代表产品撤销/回收站语义、可见交互或 ExplorerBrowser/IShellView 的视图更新。

## 2026-10-29：准备真实 OLE Copy/Move 验收入口

- 隔离的可见 `IShellView` 拖放原型现提供明确分开的 OLE Copy 与 Shift+Move 入口，同时允许 Copy/Move，并要求 Copy 按住 Ctrl、Move 按住 Shift。若 `CFSTR_LOGICALPERFORMEDDROPEFFECT` 可读则必须与请求效果一致；最终验收还核对目标内容及源项保留/移除状态，不把 `DoDragDrop` 的 `pdwEffect` 单独当作优化移动的最终结果。用户取消时会以非通过状态退出并记录 `acceptance=not-run`，避免把取消误报为验收通过。
- NativeProbe Release build 通过（0 警告、0 错误）；无可见 Shell view 的清理 smoke 通过，确认交互窗口清理与 Private Desktop 窗口保留路径，并确认前台未变化。`git diff --check` 通过。
- 本批次没有启动交互窗口，也没有实际调用 `DoDragDrop`；因此真实鼠标 Copy/Shift+Move、Shell 目标接受效果及源项变化仍未验证，IShellView 迁移门槛保持开放。产品仍使用 ExplorerBrowser。

## 2026-10-29：隔离 Shell 拖放目标 Copy/Move 效果复核

- 扩展 Private Desktop `IDropTarget` 探针，在真实 Shell 选中项数据对象上以 Ctrl 状态核对 Copy；另用独立临时 Shell 数据对象以 Shift 状态核对同卷 Move。Copy 返回 effect=1、目标内容正确、源仍存在；Move 将文件真实移到目标，源项消失，目标内容正确。移动时 `IDropTarget::Drop` 返回 effect=0，但 `Performed DropEffect` 与 `Logical Performed DropEffect` 均返回 Move=2，证明不能要求优化移动的 Drop 返回值必须为 2。
- 首轮因断言错误把 effect=0 判失败；根据文件状态及两个 Shell 数据对象完成格式定位后修正。第二轮 watchdog exit 0、前台句柄与输入时间戳未变化，临时夹具已清除。NativeProbe Release build 0 警告/错误。
- 该验证覆盖真实 Shell 目标对 Ctrl/Shift 状态的直接 COM 回调和同卷移动结果，不包含完整 `DoDragDrop` 源端循环、物理鼠标输入、无修饰键默认行为或产品 WPF 接线；IShellView 迁移门槛仍开放。

## 2026-10-30：无输入 OLE DoDragDrop 自动放置试验未通过

- 为在不发送鼠标/键盘输入的情况下验证 OLE 源端闭环，临时测试源在首次 `QueryContinueDrag` 时返回 `DRAGDROP_S_DROP`。NativeProbe Release 构建成功；Private Desktop 探针卡在 `drop-target: query-and-transfer-start`，12 秒内部 watchdog 返回 124，未记录 `DoDragDrop` 返回或目标 `Drop` /文件结果。
- 守卫同时记录用户桌面前台从 LockApp HWND 变为 0，输入时间戳也变化，原因未知。探针进程退出，唯一 GUID 临时夹具已逐项检查并清理；没有主动切换或恢复前台。自动放置策略不再重试，临时探针代码已撤销。日志：`src/.state/native-probe-watchdog/20261030-000138-46fb9cf9.stdout.log`。
- 该尝试不是产品缺陷复现，也不能证明 Shell 已到达目标 `Drop`。真实鼠标拖放及 `DoDragDrop` 端到端迁移门槛仍未通过；Explorer Dirs 产品继续使用 ExplorerBrowser。后续若重新验证，需安排用户可见且明确授权的交互验收，由用户控制拖动与取消。
- 事后静态对照发现试验源没有等待发起鼠标键按下，而现有隔离 `IDropSource` 先确认按下、再等待释放；微软说明 OLE 会在输入状态变化时调用 `QueryContinueDrag`。这可能解释无输入试验一直未推进，但缺少回调计数和线程栈，仍是待证假设。随后重跑既有 `--shell-view-drop-target-registration-smoke` 通过（四 HWND、重复注册拒绝、选择性撤销、Dispose 清理及前台不变），仅确认注册生命周期合同，不补足真实 OLE 拖放证据。

## 2026-10-30：隔离 OLE drop target 重复撤销修复

- 在注册生命周期 smoke 中复现：宿主持有两个 HWND 注册时，若第二个注册已被撤销，再 Dispose 会收到 `DRAGDROP_E_NOTREGISTERED (0x80040100)` 并中断其它清理。新增幂等处理：把此 HRESULT 当作目标已撤销，平衡对应 `OleInitialize`；其它 HRESULT 仍失败。
- 回归先按预期失败，修复后 NativeProbe Release build 0 警告/错误，扩展 smoke 通过：预撤销的目标清理成功，兄弟 HWND 可重新注册；原四 HWND、多 host 冲突、选择性撤销、整体 Dispose 和前台不变检查均保持通过。该结果不验证 Shell view 上的实际拖放或产品行为。

## 2026-10-30：递归目录大小的原生视图消费验证

- NativeProbe 的合成 namespace 已能生成递归字节数并通过 `CompareIDs` 排序，但 DefView 仍不显示目录行的 Size 文本。额外的 Private Desktop 属性屏蔽 A/B 确认 `GetAttributesOf` 确实清除了 `SFGAO_FILESYSTEM` 和 `SFGAO_FILESYSANCESTOR`（17 次查询中 13 次包含相关标记），目录 `GetDetailsOf`、`GetDetailsEx` 和 property store 请求仍为 0。
- 按 Size 排序结果仍正确（4 B 文件在 10 B 递归目录之前，反向排序相反），说明这份合成样本的比较排序合同有效；但仅屏蔽 filesystem 标记不足以让 DefView 消费目录属性。结论只适用于当前包装真实 filesystem PIDL 的隔离 adapter，不代表全自有 PIDL namespace。
- 这是架构探针证据，未接入产品、未改 release。产品继续使用 ExplorerBrowser，F9 递归扫描仍是显式汇总路径；若未来要逐行派生列，后续研究需转向完整虚拟 namespace 的属性供应与 Shell 操作兼容门槛，而不再重复尝试此标记屏蔽变体。

## 2026-10-30：IShellView WPF 根宿主清理对照
- Private Desktop 合成递归大小视图改用 WPF `HwndSource` 作为父根后完整返回（watchdog exit code 0）；目录值 10、文件值 4 正常显示，升序/降序各调用一次 `CompareIDs` 并按值重排。
- `HwndSource.Dispose` 销毁父根成功，前台 HWND 和最后输入时间戳保持不变。`DllCanUnloadNow` 在仍有 COM 引用时返回 `S_FALSE`，模块留待探针进程退出回收。
- 结果与此前原生 `STATIC` 根销毁卡住的观察形成对照，提示继续聚焦宿主根窗口销毁路径；尚未同轮复跑 `STATIC` 基线或取得卡住调用栈，根因未证实。产品仍使用 ExplorerBrowser，本次只更新隔离架构证据，不代表产品迁移门槛已通过。详见[逐行大小调查](folder-size-column-investigation.md#2026-10-30wpf-根宿主清理对照)。
- 随后同 watchdog 重跑 `STATIC` 基线，仍停在父窗口销毁并捕获 561,128-byte minidump；watchdog 清理临时注册与夹具成功。但前台从锁屏 HWND 变为 `0` 且最后输入时间戳变化，故此轮隔离条件失效，不作为 A/B 通过或根因证据；转储头与目录确认有 14 个 stream（含 14 个线程及线程信息流），线程栈未解析。
- 只读解析主线程上下文后，TID 24940 当前 RIP 对应同版本 `win32u!NtUserMsgWaitForMultipleObjectsEx+0x14`；栈内地址还落在 COM `CoWaitForMultipleHandles` 与 CLR 模块区域，其它线程多位于 ntdll 等待入口。模式与 STA/COM 消息等待相符，不能据此断言死锁或具体根因；仍需符号化调用栈。
- 系统 DbgHelp 的导出符号复核确认消息等待及 `CoWaitForMultipleHandles` 候选，未取得 combase/coreclr 私有 PDB；最近公开导出名不能替代逐帧符号化栈，根因仍未确认。
- 从公共符号服务器取得匹配的 ntdll、win32u、coreclr PDB 后，`StackWalk64` 恢复主线程前四帧：`NtUserMsgWaitForMultipleObjectsEx` → combase ordinal 88 内部帧 → `CoWaitForMultipleHandles` → CLR 模块。CLR 之后 unwind 数据不足而失效；combase PDB 下载不可用（503/超时），故具体 COM 调用者及根因仍未知。

- 后续两次重复 WPF 根宿主递归大小自定义列 probe：一轮有效通过，显示 10/4 并由 DefView 标题升降序排序；另一轮相同显示/排序断言通过，但前台 HWND 与输入时间戳变化，按隔离守卫判定无效。两轮探针进程、临时注册与 fixture 均已清理。该结果加强 WPF 根视图列消费的可重复性证据，但不证明产品迁移或桌面隔离稳定；暂停新的 Private Desktop view probe，产品仍使用 ExplorerBrowser。
- 静态审查 NativeProbe 递归列实现发现，枚举时同步递归计算每个目录大小，每次刷新都会重算，当前没有缓存、取消或异步行更新。微型夹具不提供大型目录性能证据；产品化前需先有后台可取消计算/缓存设计并按树规模量测。
- 更新路径方面，原型 `SFVM_FSNOTIFY` 当前触发整视图 `IShellView::Refresh`。legacy `IShellFolderView::RefreshObject` 不可作为产品实现依赖；下一项受支持接口验证候选为 `SHChangeNotify(SHCNE_UPDATEITEM)`，并需比较产品已有策略所用的父目录补充通知。虚拟 namespace 是否重新取列值/更新排序仍未知，也不能先假定已解决异步列值更新。

- provider-owned attributes 对照显示：DefView 的 `GetAttributesOf` 可由合成 PIDL 直接提供 folder/stream 属性，但标准 `PKEY_Size` 列仍不请求目录值。后续未注册自定义列 key 的隔离实验则能显示目录派生值，并经标题点击调用 `CompareIDs` 排序；`SetSortColumns` 对未知 key 仍返回 `TYPE_E_ELEMENTNOTFOUND`。该路径仍只在 NativeProbe 验证，未接入产品。

- 自定义列 key 探针发现：未注册 `PROPERTYKEY` 经 `GetDetailsOf` 可在 DefView 中同时显示 10 B 目录和 4 B 文件；`SetSortColumns` 因未知 schema 返回 `TYPE_E_ELEMENTNOTFOUND`，但原生列标题点击仍会排序并调用 provider `CompareIDs`。这是 NativeProbe 级可行性证据，完整 Private Desktop 运行因前台/输入守卫变化及清理超时不记为通过。先源码定位清理等待与守卫变化，再决定后续独立会话复测；本轮不动产品及 release。
- 旧 watchdog 仅记录通用 `probe-entered`，Wait-chain 只能显示 COM 等待，无法定位最后执行的清理调用。NativeProbe 现为 `ReleaseSyntheticSortViewHold`、每个 view/COM release、`DestroyViewWindow`、host dispose、DLL unload、注册与 fixture 清理设置独立阶段标记；后台 Release build 和树级 smoke 通过。该诊断代码未由新的 Private Desktop view 运行验证。
- WPF 根宿主的单项加父目录通知同样在约 1 秒内只枚举 2 行一次，保留焦点，但没有大小列 provider 重读；整视图刷新在约 2.6 秒内枚举 4 行两次、丢失焦点/选区，也没有重读。两者 watchdog 均 exit 0 且桌面守卫通过。下一步验证对 Shell ListView 单行发送 `LVM_UPDATE`/重绘是否能触发其 provider 重新读取，而不重建整视图；只作隔离可行性检查，不作为产品集成决定。详见[逐行大小调查](folder-size-column-investigation.md)。
- 单行 `LVM_UPDATE` 返回成功但不触发 provider 读取；`LVM_REDRAWITEMS + UpdateWindow` 也不重读列值，另触发一次两行枚举/比较，焦点仍保留。WPF 隔离探针完整通过且已清理。直接操作 DefView 内部 ListView 不是当前的更新方案。

2026-10-30 WPF 根宿主补测“删除后重建”通知：对同一个绝对子 PIDL 依次发送 `SHCNE_DELETE` 与 `SHCNE_CREATE`，约 1.36 秒内只发生一次两行枚举和一次大小比较，焦点/选区保持，但通知后没有 `GetDetailsOf`/`GetDetailsEx` 调用，大小文本仍未恢复。watchdog exit code 0，前台 HWND/输入 tick 不变，临时 CLSID/NameSpace 注册与夹具已清理；日志 `src/.state/native-probe-watchdog/20261030-034248-535f69ba.stdout.log`。该策略未解决列值刷新，不能作为后续路径。

API 边界复核：本机 Windows SDK `IFolderView2` 提供整视图 redraw 开关，但没有受支持的逐行属性值失效/重取入口；`IShellFolderView::RefreshObject` 已由 Microsoft 标记为 Windows 7 起不再可用的 legacy 接口。结合上述通知与 ListView 消息矩阵，当前 DefView 路径未找到受支持的逐项派生列更新机制。下一步不继续尝试内部 ListView 消息或重复通知；若需要实时派生列，应另行评审自有视图/数据模型及维护成本，并先通过 Shell 操作、导航、焦点、拖放、通知等兼容门槛。产品仍使用 ExplorerBrowser。

## 2026-10-30：自动刷新 watcher 溢出恢复复验

Release HostProbe 的真实 ExplorerBrowser 隐藏宿主场景以 10,000 个并发文件创建触发一次 `FileSystemWatcher.Error`，恢复强制刷新后四窗格计数、队列结算及受检窗格的选区/焦点均通过，退出码 0；无可见窗口或生产逻辑变更。此次补充本机目录溢出恢复证据，不能关闭自动刷新 P0，网络/虚拟 Shell 与可见交互仍待验证。详见[目录变更调查](live-update-investigation.md#2026-10-30真实-watcher-溢出后的生产宿主恢复)。

## 2026-10-30：保留恢复后的 watcher 错误诊断

溢出恢复成功后，通用刷新状态会被覆盖为 `refreshed; rows=0`，错误根因无法回查。现增加独立的 `LastDirectoryChangeWatcherError`，保留最近一次当前目录 watcher 错误类型/HRESULT，并在新目录启动监视时重置。真实 10,000 项本地溢出场景的 RED/GREEN HostProbe 与 Release build 通过；未启动可见窗口、不改变刷新策略。详情见[目录变更调查](live-update-investigation.md#2026-10-30保留-watcher-错误诊断至恢复完成)。

## 2026-10-30：Q-Dir 颜色元数据真实夹具往返断言

新增验收夹具 `qdr-color-metadata.qdr` 的后台往返复核确认：导入后保留 `useColor=1`、`useTreeColor=0`、`colorthema=0` 及 `[Filter2]` 四条有序规则；扩展会话往返与移除 Explorer Dirs 扩展后的 Q-Dir 基础字段往返均通过，夹具 SHA-256 保持 `70D8C32A0F9B0831A4AD060FAA87A982B7E077DD4791A2F375023D6BF9AC8D34`。复核发现 `--roundtrip-qdr` 原先虽检查基础布局/标签/视图，但没有比较颜色元数据；现补上基础字段颜色开关、主题和有序 Filter2 行断言。针对该夹具往返、完整 Release SettingsSmoke 和 App Release build 均通过，0 警告/错误。仅证明文件状态兼容，颜色规则未在 ExplorerBrowser 应用；Q-Dir 实际打开/重存及可见 UI 仍待集中验收。

## 2026-10-30：Q-Dir 克隆视图的标签差距复核

依据 [Q-Dir FAQ 16](https://www.softwareok.com/?faq=16&seite=faq-Q-DIR)，Q-Dir 克隆视图包含当前路径、查看设置、选中对象及浏览历史。源码核对确认 Explorer Dirs 跨窗格移动标签会保留原 Browser 和 Shell Travel Log；复制标签则新建 Browser，不能迁移原生 Travel Log。原差距和可行性判断为本轮实现前的基线；应用级可重放历史现已接入独立“克隆视图”动作，当前边界见下节。完整 Q-Dir 克隆仍需各历史停靠点的选区状态和可见验收。

补充可行性依据：项目既有 [`native-navigation-probe.md`](native-navigation-probe.md#问题与结论) 记录了 ExplorerBrowser 直接查询 `ITravelLogStg` 返回 `E_NOINTERFACE`，通过 `IServiceProvider` 查询 TravelLog 服务返回 `E_NOTIMPL`；公开 ExplorerBrowser 接口没有宿主间复制 TravelLog 的入口。忠实克隆历史因此不能靠共享/复制现有 COM 对象完成，需要应用级导航序列及每个历史停靠点的选区模型，并验证虚拟目录、搜索位置、被取消/失败导航和分支截断。这个范围超出安全的复制标签小修，需在明确用户交互语义后再设计。

## 2026-10-30：独立“克隆视图”命令与应用级导航历史

- 为满足“克隆视图”与“复制标签”语义分开，标签右键菜单新增独立克隆命令；复制标签和跨窗格复制仍保持原行为。克隆会带上最多 100 个最近位置的轻量历史快照及当前选区；原标签继续走自身 Shell Back/Forward，克隆标签通过应用历史重放普通 Shell 位置与 `search-ms:` 搜索位置。成功的后退/前进移动游标，新位置会截断前进分支；启动中的克隆等首个位置恢复完成后才启用历史按钮。
- SettingsSmoke 先以缺失历史模型得到预期编译失败，再覆盖游标移动、前进分支截断、失败导航取消、独立快照、搜索位置往返、上限及克隆/复制命令分流；完整 smoke 与 App Release build 通过。没有运行 ExplorerBrowser HostProbe 或 GUI。
- 第一批实现只恢复克隆时所在位置的选区；逐历史位置选区已在下一节补齐。搜索结果的当前选区恢复仍受现有 Shell direct-child 选择接口限制。真实 Shell Back/Forward TravelLog 与应用模型同步、失败/取消导航和虚拟 provider 行为待集中 GUI 验收；本轮未生成预览包。

## 2026-10-30：克隆视图的逐历史位置选区

- 每个历史停靠点现在维护最近一次稳定 Shell 选区的解析名快照；选区回调后在 Dispatcher 安全时段读取，并在 COM 读取后复核视图代次和位置，避免用过期视图覆盖新状态。后退/前进成功后，普通 Shell 文件夹位置会通过现有批量 PIDL 选择恢复目标停靠点的选区。创建克隆时同步当前选择快照，避免只复制上次导航留下的旧状态。历史位置上限仍为 100；原“复制标签”行为不变。
- SettingsSmoke 覆盖逐站快照、后退/前进、前进分支截断、独立克隆快照、搜索位置选择身份和历史上限。新增隐藏 PaneTabTransferProbe：真实 ExplorerBrowser 搜索结果选中后调用生产“克隆视图”，确认克隆 URI、历史停靠点和启动选区解析名一致；克隆结果视图枚举后，通过对应行 PIDL 进行原生复选，并检查搜索 Back/Forward 保留同一选区身份。隐藏父窗口不满足自动恢复所需的窗口激活和焦点门控，故未把手动复选冒充自动异步恢复验收。完整 UI 与真实前台焦点路径待集中验收。
- `--search-navigation` HostProbe 原先通过反射少传了 `Search` 的可选 `scopePath` 参数，第一阶段后即抛 `TargetParameterCountException`；显式传入每窗格临时范围后，四个隐藏生产 Shell 宿主的双查询、后退/前进、父目录返回及连续请求替换全部通过。

## 2026-10-30：`.qdr` 区段边界修正

- 导入器只把 INI 根级字段作为布局/标签输入；`[Filter2]` 仍单独读取颜色规则，其他区段内容不再误解析为根级 `DirN`、`ViewModeN` 等键。此前非 Filter2 区段中的同名字段可能污染导入标签或触发错误警告。
- 后续补齐不透明往返：未识别根字段和非 Filter2 区段保存在便携会话 v14 并在 QDR 导出时带回；应用会生成的受支持字段以当前状态为准。BaseHash 覆盖所有根字段及保留区段，外部修改会让旧 Explorer Dirs 扩展失效。单次保留上限 1 MiB，超限则不保留。
- Release SettingsSmoke 覆盖未知根字段/区段的导出、导入、会话持久化及外部编辑失效；完整 SettingsSmoke 与 App Release build 通过，0 警告/错误。未启动 GUI 或 Q-Dir；Q-Dir 对未知字段的实际读取及跨版本行为仍待互操作验收。

## 2026-10-30：列值筛选菜单分页

列值筛选仍枚举并保留当前视图的完整去重值数组，但 WPF 菜单改为每页最多 20 个可勾选项，并提供上一页/下一页与范围提示。跨页维持已选集合、当前选择计数、清除操作和每列 256 值限制；已选但当前视图不存在的旧值也仍保留在数组中供翻页访问。新增分页纯逻辑 smoke 覆盖总页数、尾页内容及越界页为空。App Release build 0 警告/错误，完整 SettingsSmoke 退出码 0。没有启动 GUI 或 ExplorerBrowser；分页菜单实际操作体验、超长菜单性能与主题样式仍待后续集中验收。完整值数组仍驻留到菜单关闭，本次只限制创建的菜单控件数，没有声称固定总内存上限。

## 2026-10-30：标准 Win32 ListView 派生值更新与排序合同

- NativeProbe 的普通非虚拟 `SysListView32` 基线现在有名称和派生大小两列；隐藏数据合同先把第二行大小从 `4 B` 改为 `16 B`，再同步更新其 `lParam`，调用 `LVM_SORTITEMSEX` 后确认行序按更新后的数值排序。命令 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --common-listview-data-contract` 通过，输出 `updated-cell=16 B`、`sorted=large-folder:10,small-folder:16`、`visible=False`、`foreground-unchanged=True`。
- 首次可见 Private Desktop 基线因误带 `LVS_OWNERDATA` 虚拟列表样式而使用了不匹配的存储列表 API，第二列更新失败；已修正样式。该轮守卫也发现前台/输入状态改变，所以没有将其作为有效可见结果；修正后只运行隐藏合同，没有重开可见窗口。
- 后续隐藏合同把已选中并聚焦的 `small-folder` 从 `4` 改为 `2`，强制排序跨过 `large-folder`；排序后唯一 selected+focused 状态仍在 `small-folder`。NativeProbe Release 构建和隐藏数据合同通过，窗口不可见且前台不变。该结果只覆盖普通存储式 `SysListView32`，不能外推到 Shell DefView 或产品。
- 结论仅限于自有标准 ListView 可由应用更新派生值并按其排序。Shell 文件枚举、图标、菜单与命令、焦点、拖放、文件系统通知、性能/内存和产品集成均未验证；不据此决定迁移 ExplorerBrowser。详见[派生大小列架构调查](folder-size-column-investigation.md)。

### 本批次追加：真实 Shell 项目身份映射

- 隐藏合同用真实临时文件夹构造 Shell 绝对 PIDL，并验证 Shell 显示名及文件系统解析名可往返到原路径；普通 `SysListView32` 的行 `lParam` 保存稳定 row ID，映射到 PIDL、路径和派生大小。更新大小后按新值重排，选中/焦点仍落在相同 row ID，PIDL 未替换。Release build 与隐藏合同通过，窗口不可见、前台未变，夹具清理完成。
- 这证明可将真实文件系统 Shell 身份映射进自有 ListView 数据模型；行数据仍由探针显式创建，未接 Shell 枚举、图标/菜单/操作、拖放、通知、导航、键盘、虚拟位置、性能/内存或产品。迁移门槛继续开放，产品仍使用 ExplorerBrowser。详见[派生大小列架构调查](folder-size-column-investigation.md)。

### 本批次追加：Shell 枚举驱动行模型

- `--common-listview-data-contract` 现在绑定真实临时父目录并调用 `IShellFolder::EnumObjects` / `IEnumIDList::Next`，将返回的相对 PIDL 组合成每行的绝对 PIDL；ListView 名称和 row ID 映射来自 Shell 枚举，而不是按路径手工创建。新断言先按预期红灯 `shell-enumeration-backed=False`，接线后 Release build 与隐藏合同通过；派生大小重排后选中/焦点仍跟随相同 row ID，PIDL 身份稳定，窗口不可见、前台未变，临时资源清理。
- 该步只证明本地临时文件夹的 Shell 枚举可驱动普通 `SysListView32` 行；系统图标、Shell 列生产、菜单/操作、拖放、通知、导航、键盘、虚拟位置、性能/内存和产品集成仍未验证。未创建 `IShellView`，产品仍使用 ExplorerBrowser。详见[派生大小列架构调查](folder-size-column-investigation.md)。

### 本批次追加：Shell 系统图标索引

- 通过 `SHGetFileInfo` 为枚举行查询 Shell 共享小图标列表和图标索引；隐藏 ListView 挂载共享 image list 并写入每行 `LVIF_IMAGE`，再读回索引与 row ID 映射。排序后映射仍稳定；新增图标索引断言先失败，再通过；NativeProbe Release build 和完整隐藏数据合同通过，窗口不可见、前台未变。
- 此结果不含可见像素/主题/DPI/overlay 验收，也未验证原生菜单、命令、文件操作、拖放、通知、导航、键盘、性能/内存或产品集成。Explorer Dirs 产品仍使用 ExplorerBrowser。详见[派生大小列架构调查](folder-size-column-investigation.md)。

### 本批次追加：项目菜单与视图菜单的接口边界

- 使用真实枚举项 PIDL 查询 `IShellFolder::GetUIObjectOf(IID_IContextMenu)` 项目菜单，并用 `IShellFolder::CreateViewObject(IID_IContextMenu)` 查询背景菜单。两者都能取到菜单命令，未显示或执行；递归遍历完整 HMENU（含 TortoiseGit/SVN 子菜单），前台句柄保持不变。当前机器的 folder 背景菜单树含扩展项、新建及属性，没有查看/排序/分组/刷新。
- 对照产品源码，完整文件区菜单来自当前 `IShellView.GetItemObject`，不是 `IShellFolder` 的背景菜单对象。探针合同只确认两种 folder context menu 可查询，不把它标为完整 Explorer 菜单通过。自有 ListView 如要继续，仍须证明视图级菜单命令作用于自有状态；否则保留 ExplorerBrowser 才能保留当前原生视图菜单路径。详见[派生大小列架构调查](folder-size-column-investigation.md)。
- 双选区扩展的局部操作断言通过：隐藏列表的两项 Shell `IDataObject` 经 `IFileOperation.CopyItems` 复制后均与源载荷逐字节相同，后续临时副本 Move/Rename/Delete 也通过。整轮前台守卫失败（`0x861542→0x530EDC`）；运行后前台为 TortoiseGit 提交对话框，无法归因于探针或同时的共享桌面活动，故不算完整合同通过，也未重跑。Release 构建通过；产品未改。具体范围见[派生大小列调查](folder-size-column-investigation.md)。

### 本批次追加：Shell 驱动的 ListView 行与文件操作合同

- `--common-listview-data-contract` 在不可见的普通 Win32 ListView 中使用真实 `IShellFolder::EnumObjects` 行；图标来自 Shell 共享系统图像列表。更新派生大小并排序后，稳定行 ID、PIDL 身份以及 `small-folder` 的选中/焦点保持一致。
- 同一隐藏合同用所选行的 Shell `IDataObject` 和 `IFileOperation` 完成复制、移动、重命名、删除，并核对临时夹具结果；菜单对象可查询，但 folder 背景菜单不含查看/排序/分组/刷新。完整 Explorer 视图菜单所需的 `IShellView.GetItemObject` 尚未接入。
- 结论：自有 ListView 已验证接入 Shell 枚举、系统图标和常用文件操作的可行性；迁移的核心体验门槛仍包括完整视图菜单、拖放、导航、键盘、通知和虚拟位置。该合同不可外推为 Explorer 等价或产品可迁移结论，产品仍使用 ExplorerBrowser。详见[派生大小列架构调查](folder-size-column-investigation.md)。

### 本批次追加：递归扫描结果进入 Shell 行模型

- `--common-listview-data-contract` 不再手工注入目录大小：先对临时 `large-folder` / `small-folder` 调用产品 `RecursiveDirectoryScanner`，把实际扫描字节数交给 Shell 枚举行；随后将小目录的载荷从 4 字节改成 2 字节，重新扫描并更新同一 Shell row ID，按新值排序，验证选中和焦点仍随 `small-folder` 保持。首轮以未链接扫描器的预期编译错误确认探针缺少生产扫描路径，接入后 NativeProbe Release、数据/文件操作合同、完整 Release SettingsSmoke 与 App Release build 通过。
- 这是递归大小计算到自有行模型的数据合同，不是产品列实现或可见 ListView 绘制；未验证扫描异步/UI 生命周期、实际 Shell DefView 列、拖放/菜单/通知/键盘或可见排序。产品仍使用 ExplorerBrowser。

### 本批次追加：单次树扫描生成各目录汇总

- `RecursiveDirectoryScanner.ScanDirectoryTree` 一次遍历普通目录树并汇总每一级目录的递归大小/计数。SettingsSmoke 核对根及嵌套目录结果、与原扫描结果一致，并确认真实 junction 不跟随。NativeProbe 用一次扫描填充两个 Shell 行；改动小目录内容后重扫一次，同步得到更新行值和根汇总。
- 该方式避免逐行对嵌套目录重复遍历，但当前探针仍直接同步调用扫描器；尚无后台取消、UI 生命周期、列消费或产品接线证据。该次更新后的完整 ListView 合同因运行中前台 HWND 变化而未通过整体守卫，不作整体通过声明；具体通过的字段及限制见调查记录。产品继续保留 F9 显式扫描，仍使用 ExplorerBrowser。详见[逐行大小列调查](folder-size-column-investigation.md)。

### 本批次追加：Shell 子目录与导航历史合同

- 新增 `--common-listview-navigation-contract`，在隐藏 NativeProbe 中用临时目录树调用 Shell 文件夹枚举，核对四个位置的直接子项和父子 PIDL 关系，并复用产品 `ShellTabNavigationHistory` 验证后退、前进以及分支导航清空前向历史。首轮发现既有枚举辅助函数按整份夹具计数，逐目录调用失败；改为传入当前目录直接子项后通过。NativeProbe Release、导航合同、既有数据/文件操作合同、完整 Release SettingsSmoke 与 App Release build 均通过，前台句柄保持不变。
- 这只验证 Shell PIDL/行数据和产品历史状态模型的组合边界；没有创建 `IShellView`、ExplorerBrowser 或实际 ListView 窗口，也没有真实按键、焦点、导航提交/取消或产品 UI 接线证据。迁移门槛保持开放，产品继续使用 ExplorerBrowser。

## 2026-10-30：Q-Dir 同目录相对大小比例隔离计算

- Q-Dir 官方 FAQ 82 说明大小列下划线百分比是当前目录中项目的相对大小；显示精度和舍入规则未公开。隔离 helper 以同级项目所在目录的递归总可访问字节数作分母，返回 `decimal` 百分比，未规定显示精度或格式舍入，零分母为空值。
- SettingsSmoke 覆盖 10/14、4/14，及小目录变化后 10/12、2/12 与零分母；完整 SettingsSmoke、NativeProbe Release build、App Release build 均通过。NativeProbe 仅完成列接线并构建，未运行完整 Shell/窗口合同；产品 UI、配置、可见格式与排序仍未接入，产品继续使用 ExplorerBrowser 和 F9 显式扫描策略。细节见[逐行大小列调查](folder-size-column-investigation.md#2026-10-30-同目录递归大小比例隔离原型)。来源：[Q-Dir FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR)。


## 2026-10-30：独立验证同目录递归大小比例列更新

- 为比例列加独立 `--common-listview-relative-size-contract` 入口，仅执行真实 Shell 枚举、递归扫描及隐藏普通 ListView 行更新，跳过菜单和文件操作。10/14、4/14 更新为 10/12、2/12 后，同级比例均重算正确；稳定行 ID、不可见窗口及 foreground 守卫通过。NativeProbe Release build 与专用合同退出码 0。产品界面和 Q-Dir 精确格式仍未接入；详见[逐行大小调查](folder-size-column-investigation.md#2026-10-30-比例列独立隐藏合同)。


同日延伸验证：在同一隐藏合同中加入递归文件/文件夹计数列，先观察到空值导致断言失败，再接入扫描结果；小目录变为 3 个文件和 1 个子目录后，该行计数更新，兄弟行计数保持不变，百分比列同时正确，foreground 守卫通过。当前只证明隔离行数据刷新。

## 2026-10-30：IShellFolder2 扩展元数据消费边界

- 隔离原生 folder tree 现在为目录行计算递归文件/子目录数，并通过 `GetDetailsOf`/`GetDetailsEx`、`GetDefaultColumnState` 和列键映射提供相对大小比例。`/W4 /WX` 编译及无窗口 `--synthetic-shell-folder-tree-smoke` 通过，既有多级 PIDL、菜单和文件操作合同也通过。
- 单次隐藏 DefView 探针没有显示目录计数，比例值只有文件行显示、目录行为空，排序点击未按比例重排；探针在 Shell view 窗口销毁时超过 watchdog，运行期间共享桌面输入/前台发生变化，故不算完整通过。按用户“尽量不使用窗口”的偏好，后续先静态追查属性存储和列描述接入，不继续反复启动视图窗口。
- 该负向结果说明直接 `IShellFolder2` 元数据合同不能推断 DefView 会显示/排序扩展字段。产品仍不接入，ExplorerBrowser 保持现状；详情见[派生大小列调查](folder-size-column-investigation.md#2026-10-30-ishellfolder2-递归-type-与相对比例列原型)。本轮 NativeProbe 异常清理遗留的精确临时注册项/夹具受执行策略限制未能移除；没有触碰先前 `*-stale-*` 项，需在后续可用的清理路径处理后再把环境视为干净。

## 2026-10-30：通知后显式重设 IShellView 派生列探针

- NativeProbe 增加 opt-in `single-item-reapply-column` 策略：发送单项 `SHCNE_UPDATEITEM` 后，通过 `IColumnManager` 显式重设 Name 与递归大小两列，并记录 HRESULT、列和值、排序、选区及 provider 读取计数。每轮现使用 GUID CLSID 和夹具，避免覆盖既有注册/文件。
- Release build 与无窗口 smoke 通过。正确 Private Desktop 入口测得重设 `S_OK`，但视图未重新读出列值；该轮输入 tick 变化，并在 `STATIC` 根窗口销毁时超时，故只记为无效诊断线索。唯一本轮 CLSID/夹具遗留及清理命令被策略拒绝的情况已记入调查记录。后续需先处理该遗留边界，再选 WPF 根宿主和稳定桌面条件复验；产品继续使用 ExplorerBrowser。详见[派生大小列调查](folder-size-column-investigation.md)。

## 2026-10-30：减少 F5 与查看方式切换的列表重绘空窗

- F5 仍使用 `IShellView::Refresh`，这是 Explorer 对 F5 的标准 Shell 操作；查看方式切换只调用 `IFolderView2::SetViewModeAndIconSize`，此前并未重新导航或完整刷新。
- 文件列表现在在原生刷新期间暂缓重绘，并在本轮异步刷新完成回调清理时恢复；查看方式切换只在同步设置视图模式期间暂缓重绘。若 Shell 不支持 `IFolderView2::SetRedraw`，操作按原路径继续。
- ExplorerDirs.App Release 构建通过（0 警告/错误），本次可见 UI 闪烁尚未验收；实际空窗、刷新耗时及慢速/虚拟 Shell 位置的行为留待集中 UI 验收。没有重建 ExplorerBrowser，也未改导航/刷新语义。

### 后续修正

- 用户仍观察到 F5 列表黑屏。源码复核发现刷新期间关闭 `IFolderView2` 重绘，并在异步完成后对当前 view HWND 连同所有子控件执行强制失效，会额外制造整片列表的重绘窗口。现移除这两步；`IShellView.Refresh()` 继续由 Shell 原生处理，完成等待仅用于之后恢复选区和焦点。
- 查看方式切换继续只设置当前 `IFolderView2` 的模式和图标尺寸，没有重导航、重建浏览器或调用 F5。原有延后状态读取保留，以免阻塞 Shell 自己的重排和首次绘制。
- App Release 构建通过（0 警告、0 错误）。后台探针不采集屏幕像素，本次视觉是否改善仍需集中 UI 验收；不会据此声称黑屏已消失。

### 反馈后的第二轮调整

- 用户反馈 F5 与切换查看方式仍会出现明显列表黑屏。检查发现查看方式的重绘抑制包住 `SetViewModeAndIconSize`，现移除这层抑制，让 Shell 控件自行完成模式重排。
- F5 的 `IShellView::Refresh` 仍只刷新当前 Shell 文件视图内容。重绘恢复不再仅凭 WPF `ContextIdle` 判定，而是等待原生行数连续三次相同（50 ms 采样），最多等待 1.5 秒；恢复绘制后只使当前 Shell 视图 HWND 失效一次，补上重绘请求。不会重新创建 ExplorerBrowser 或导航到同一路径。
- App Release 构建通过（0 警告/错误）。当前未打开可见 UI；黑屏持续时间、慢速/虚拟 Shell 位置的体验需后续集中验收，本次不宣称视觉问题已解决。

## 2026-10-31：F5 与查看方式切换调用链复核

- 当前 F5 用户入口为 `PaneControl.RefreshCurrent → ExplorerBrowserHost.RefreshPreservingLastFrame`。普通文件系统目录通过当前绝对 PIDL 发 `SHCNE_UPDATEDIR`；搜索/虚拟位置或无法取得目录 PIDL 时回退 `IShellView.Refresh`。仅显式用户刷新持有短时 `IFolderView2.SetRedraw` 租约，行数连续稳定后释放，最多等待 1.5 秒；目录 watcher、筛选等内部刷新不使用该租约。两条路径都作用于当前 Shell 视图，不重建 ExplorerBrowser、标签、导航历史或窗格。
- 查看方式切换只调用当前视图的 `IFolderView2.SetViewModeAndIconSize`，随后在 Dispatcher `ContextIdle` 读取其余配置，不重枚举目录。Shell 仍需重排/重绘列表项目，因此这不是只改一个模式标志就能避免列表重绘的操作。
- 源码复核发现先前“已移除 F5 SetRedraw”的记录已被后续仅针对手动 F5 的有限租约取代，已同步修正 shell-parity 说明。没有可见像素证据证明当前租约消除了黑屏；本轮不添加新的重绘拦截或整窗格失效。后续集中 UI 验收分别记录两种操作的空白区间、选择/焦点、滚动位置和慢速目录表现，再决定是否保留租约或改用更窄的呈现路径。

## 2026-10-31：加入按需的原生绘制时序诊断

- 现有 Shell callback trace 开启时，F5 与实际查看方式变更分别记录 2 秒内子窗口 `WM_PAINT` / `WM_ERASEBKGND` 的相对时间、窗口类名、Shell 处理耗时和返回值，最多 256 条。未开启 trace 时不采集这组消息；记录逻辑不改变 Shell 绘制或输入。
- App Release build 通过，0 警告/错误；没有启动窗口或运行测试。该诊断仅能判断消息时序，不能看到像素、测量肉眼空白时长或证明鼠标命中一致；需在集中验收时结合画面记录读取，之后再决定绘制策略。

2026-10-31 F5 绘制时间线接入实际手动刷新入口：Release HostProbe 的 `--live-shell-refresh` 现在调用产品的 `RefreshPreservingLastFrame`，并在原有行数、选区、焦点结果中输出该视图的 `WM_PAINT` / `WM_ERASEBKGND` 时间线。HostProbe/App Release build 通过（0 警告、0 错误），提交 `1177988`。本轮没有运行 ExplorerBrowser 宿主：项目规则要求隐藏宿主实验前确认桌面空闲和当前授权，且其可能改变系统前台。该探针可以补充 Shell 绘制消息顺序与处理耗时，不能读取像素或验证冻结画面期间的鼠标命中；F5 黑屏改善仍待隐藏诊断和集中可见验收分别确认。

2026-10-31 保帧诊断补记交互输入：开启 Shell callback trace 时，F5/查看方式转换时间线现也记录窗口类及鼠标按键、键盘、滚轮消息类型，不记录键值、文本或鼠标坐标；仍受 2 秒和 256 条上限约束，普通运行无采样。HostProbe/App Release build 与完整 Release `SettingsSmoke` 通过（0 警告、0 错误），本轮未启动宿主。此记录可指出保帧期间是否有输入抵达，但不能确认用户看到的行与 Shell 实际命中行是否一致，需在后续集中可见验收核对。

## 2026-10-31：预览格式过滤等待 Q-Dir 可见确认

- 用户决定先等集中 Q-Dir 验收后再决定：扩展名不匹配的文件是否留在文件列表。Q-Dir FAQ 34 同时提到预览扩展名列表与“过滤后减少文件视图项目”，单靠公开页不能确认实际语义；FAQ 38 的每视图文件过滤是另一项功能。
- 在验收确认前不新增预览资格过滤设置，不复用会隐藏列表项目的 `IFolderFilterSite`，也不按选择显隐整个原生预览窗格。详情见[预览窗格调查](preview-pane-investigation.md#q-dir-预览格式过滤的实现边界2026-10-28-复核)。

## 2026-10-31：NativeProbe 共享 Shell 行身份读取

- 文件系统与虚拟 Shell 行探针共享显示名、绝对解析名、属性、PIDL 图标索引读取；文件系统路径仅为真实 filesystem 项目提供。
- NativeProbe Release build、虚拟 Control Panel（9 个虚拟子项）、文件系统数据/操作及导航合同通过，前台窗口未变。仅为隔离探针代码整理，未接入产品。详见[派生大小列调查](folder-size-column-investigation.md)。

## 2026-10-31：虚拟 Shell 行项目菜单与数据对象接口

- 扩展隐藏合同：9 个 Control Panel 项目均可取到 `IContextMenu`、`IDataObject`，并成功读取 `Shell IDList Array` CIDA，将单项 PIDL 往返到原 Shell parsing name；9 个虚拟文件夹绑定成功，共枚举 72 个下一层项目。
- Control Panel 根 `IShellFolder::CreateViewObject(IContextMenu)` 可查询，但 `QueryContextMenu` 返回 0 项。这与视图级菜单是不同接口；不能拿文件夹背景菜单替代 `IShellView` 的查看、排序、刷新命令。
- NativeProbe Release build 与合同通过，窗口隐藏且前台未变化。未显示菜单、执行命令或验证真实拖放/剪贴板。产品未改变，仍使用 ExplorerBrowser。详见[派生大小列调查](folder-size-column-investigation.md)。

## 2026-10-31：Control Panel 的真实虚拟 IShellView

- Private Desktop 单目标探针创建 Control Panel 的真实 `IShellView` 成功。view 级背景 `IContextMenu` 可查询，内存菜单有 8 个顶层项，含查看、排序、分组和刷新；未显示/执行菜单。`IID_IDropTarget` 查询返回 `S_OK`。
- 该 view 的 `IFolderView2::ItemCount(SVGIO_ALLVIEW)` 返回 `E_FAIL`，没有 `SysListView32` 子控件。说明虚拟 provider 的真实 Shell 视图和菜单可用，但不能据此假设每种 Shell 位置都支持统一 ListView 行接管。前台/输入未变化，watchdog 与清理通过。产品未改动；其余菜单状态、命令、拖放和可见体验仍待验证。详见[派生大小列调查](folder-size-column-investigation.md)。

## 2026-10-31：F5 空白区间的源码边界复核

- 显式 F5 的 `IFolderView2.SetRedraw(false)` 只暂停原生列表重绘；普通文件系统目录先通过 `SHCNE_UPDATEDIR` 更新，搜索/虚拟位置或 PIDL 读取失败则调用 `IShellView.Refresh()`。租约释放条件使用 `ItemCount` 连续三次稳定或 1.5 秒超时。行数稳定不能证明行内容已绘制，目录刷新前后条数相同时也无法由此推断刷新阶段。因而这套租约只能尽量延后暴露中间帧，不能保证保留 Explorer 式旧画面。
- 查看方式切换没有调用 `IShellView.Refresh`、重新导航或重建 ExplorerBrowser；它只调用 `SetViewModeAndIconSize`，Shell 对当前行重新排布。Shell 的原生重排本身仍可能短暂重绘列表。
- Q-Dir 官方 FAQ 将 Soft-Refresh 定义为复制/拖放/重命名等导致项目变化时禁止自动重排列表，并非“不重新枚举”的 F5 替代方案；因此它不能解释或直接修复当前的刷新空白。[Q-Dir FAQ 112](https://www.softwareok.com/?faq=112&seite=faq-Q-DIR)
- 当前尚无可见像素验收或新的 ExplorerBrowser 宿主运行数据。已有按需 paint/input 时间线能补足消息顺序，但不包含屏幕像素；下一次集中验收应分别核对 F5 与视图切换的空白区间及选择/滚动状态，再决定是否改变重绘租约。此处不声称黑屏已解决。

2026-10-31 隐藏 ExplorerBrowser HostProbe 重跑 F5：产品刷新入口执行后四窗格计数仍为 `4/4/4/4`，活动窗格三项选区及原焦点文件均恢复，HostProbe 前台守卫未报错；刷新过渡日志没有采到 paint 样本。该隐藏宿主不提供真实屏幕像素，不能判断肉眼黑屏/闪动是否消失；不据此改变重绘租约或宣称可见问题已修复。仍需将 F5 与查看方式切换纳入集中可见验收。

## 2026-10-31：F5 改走 Shell 目录更新通知

- Private Desktop stock `IShellView` 探针使用 300 个文件的临时目录，连续观察 List/Small Icons/Details 切换 150 ms：每种模式始终保留 300 行，`IShellView` 与 `SysListView32` HWND 稳定。对照 `IShellView.Refresh()` 后，同一列表行数短暂到 0；1 秒采样中看到 3 个空列表样本、首末间隔 11 ms，随后恢复到 301 行。采样间隔约 5 ms，**不是像素空白的真实持续时间**。
- 对同一 view 发送指向当前目录绝对 PIDL 的 `SHCNE_UPDATEDIR | SHCNF_IDLIST | SHCNF_FLUSHNOWAIT`，行数没有降到 0，view/list HWND 不变，新增项目到达 302 行。再设置 `FWF_NOENUMREFRESH` 作为自动更新对照，写入项目后计数仍为 302；显式 PIDL 目录通知后到 303，最小行数仍为 302。该结果证明 Shell 目录通知路径在隔离夹具中能保留列表行并观察新增，不证明每个 Shell provider 都采用同样更新策略。
- 用户触发 F5 对普通文件系统位置现在优先通过 `ShellCurrentLocation` 读取当前绝对 PIDL 并发送目录更新通知；搜索、虚拟位置或当前 PIDL 读取失败继续使用原生 `IShellView.Refresh()` 回退。原有选区/焦点恢复流程保留。App Release build、完整 Release SettingsSmoke 和 NativeProbe Release build 通过；Private Desktop 探针前台及输入 tick 未变。
- 此改动没有在 ExplorerBrowser 产品宿主或可见 UI 中验收。当前只证明了直接 stock `IShellView` 的非空行/HWND 行为；没有像素数据，不能宣称用户看到的黑屏已解决。查看方式切换虽未清空行，Shell 仍可能在布局重排中重绘；后续独立调查其绘制时间线，并将 F5 的可见验收加入集中批次。

### 当前源码路径更正（2026-10-31）

- 上述“F5 改走目录更新通知”已被后续提交 `b93a9e7` 撤回：当前显式 F5 对当前 `IShellView` 调用 `Refresh()`，而不是发送广播 `SHCNE_UPDATEDIR`。原因是隐藏 ExplorerBrowser 宿主中目录通知比 `IShellView.Refresh()` 慢，且没有证明能改善可见画面；广播通知也不能限定在当前 Explorer Dirs 视图。
- 当前 F5 没有重建 ExplorerBrowser、标签或导航历史；选区/焦点恢复仅在刷新后异步结算。查看方式切换仍只对现有 `IFolderView2` 调用 `SetViewModeAndIconSize`，随后延迟读取视图状态。
- 这两条路径都只作用于当前文件视图，但 Shell 自己仍会清空并重绘列表行，或在视图模式变化时重新排版。先前短暂暂停原生重绘的尝试已撤回；没有像素证据表明它能让画面更平滑。当前黑屏/闪动仍需可见验收，未经该验收不宣称解决。

### 刷新诊断补充：跟踪 DefView 与 DirectUI 消息

- 产品文件视图是 Shell `SHELLDLL_DefView`/`DirectUIHWND` 路径；普通 `SysListView32` 的 LVM 行消息不能作为产品证据。诊断现对实际 view 类记录 `WM_SHOWWINDOW`、`WM_SIZE`、`WM_WINDOWPOSCHANGED`、`WM_NCDESTROY` 以及 DefView/DirectUI 私有消息处理耗时，并记录所有挂钩窗口的 `WM_SETREDRAW`；绘制与输入时间线仍保留。每次转换最多 256 条、两秒窗口，不记录文件名/文本，默认关闭。
- App Release build 与完整 Release SettingsSmoke 通过；没有创建窗口。该时间线能定位 Shell 是否隐藏/调整/重建 view 及私有消息耗时，但不直接报告行数变化或屏幕像素；当前行数仍由 F5 的 `ItemCount` 采样观察。用户可见闪烁待集中验收。

## 2026-10-31：刷新集中 GUI 验收候选

- 由当前干净主线 `7366323386e6f0d12d3c23c34b9fa355b8c022a3` 执行 `run.ps1 -PrepareOnly`，准备 `src/.state/gui-smoke/20261030-170136-670-23c890/`。manifest 为 `prepared-not-run`，初始四窗格、41 项待验收；EXE 与 App DLL 的 SHA-256 均独立复核匹配 manifest。
- 候选包含 F5 刷新及查看方式切换的可见验收场景，未启动应用或创建 ExplorerBrowser，不是 `releases/` 日常预览包。当前最新身份见[候选索引](gui-acceptance-2026-10-31-main-prepared-latest.md)。

### 候选更新：刷新到最新主线

- 主线提交 `0f83f3d7a98dfe02e57a447cf493c28cda7f81ce` 重新执行 `run.ps1 -PrepareOnly`，得到 `src/.state/gui-smoke/20261030-170859-050-e4c757/`。manifest 为 `prepared-not-run`，四窗格、41 项待验收；EXE 与 App DLL 哈希独立复核匹配。
- 未启动应用或 ExplorerBrowser；候选仍是集中 UI 验收副本，不是 release。最新身份已更新到[候选索引](gui-acceptance-2026-10-31-main-prepared-latest.md)。

## 2026-10-31：隔离 IShellView 刷新后的幸存焦点

- 真实隐藏 stock `IShellView` 中，`SelectItem` 对未激活 view 返回 `S_OK` 仍不足以读回 focused item；探针改为 `UIActivate(SVUIA_ACTIVATE_NOFOCUS)` 后，4 项选区和焦点 PIDL 均恢复，父窗口隐藏且前台 HWND 不变。
- 修复快照恢复中的状态耦合：某个旧选中 PIDL 已删除时，仍独立尝试恢复存在的焦点 PIDL。混合 readiness `1110` 读回 3 个幸存选中项和原焦点 PIDL，明确报告 `selection=False/focus=True`。
- 补充 `110` readiness 的未选中焦点场景：缺少一个已选 PIDL 后，2 个幸存项仍被选中，原未选中焦点 PIDL 精确恢复且继续未选中。
- 结果仅限隐藏 `IShellView` 与生产快照恢复函数；未验证产品 ExplorerBrowser 刷新、用户可见焦点/键盘/滚动或整套 Shell 操作。产品架构仍为 ExplorerBrowser，IShellView 迁移继续保留兼容门槛。细节见[自动刷新调查](live-update-investigation.md#2026-10-31-混合-readiness-的焦点恢复复核)。

## 2026-10-31：刷新集中验收候选更新到当前主线

- 在干净主线提交 `ccd3b3c23f75f9b0b5e588f20a8c603c192f3b6f` 上执行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`，得到 `src/.state/gui-smoke/20261030-200808-091-e70fbb/`。候选为 `prepared-not-run`、初始四窗格、41 项待验收；BuildIdentity 为 `0.6.0-preview.20261030-200808-091-e70fbb+ccd3b3c23f75f9b0b5e588f20a8c603c192f3b6f`。
- EXE SHA-256 `5C306940EBCEFFB35C922300E87EEC3039C4D588F5E14C614B88C71BF5F730F9`、App DLL SHA-256 `62E0B8024DA2414D9B7E721837DEDF3D5E566006B25DD7D3FD1DA07BA9FD48B2` 均与 manifest 内哈希经独立计算一致。没有启动应用、创建 ExplorerBrowser、发送输入或触碰正式 profile；此候选不是发布包，也没有可见通过结论。
- 该候选准备时 F5 仍按普通文件夹发 `SHCNE_UPDATEDIR`、搜索/虚拟位置回退 `IShellView.Refresh()`。这项记录由 2026-10-31 后续提交 `b93a9e7` 的直接 `IShellView.Refresh()` 路径调整取代；最新候选、源码事实和验收说明见本文顶部及[集中验收说明](gui-acceptance-2026-10-31-main-prepared-latest.md)。

## 2026-10-31：递归大小隔离 Shell 列合同复核

- 无窗口 NativeProbe 使用同一真实临时目录分别核对生产递归扫描器与合成 `IShellFolder2` 原型：扫描结果为 10 B、2 files、1 folder；Shell 原型的 `PKEY_Size`、大小文本和数值排序合同通过，嵌套枚举/文件操作合同也通过。
- 普通文件系统适配器对目录递归 `PKEY_Size` 请求返回 `E_FAIL`，此路径不可用于提供派生列；单次结果不外推到所有 Shell provider。更新后的隔离合同由生产扫描器为两条兄弟目录生成 10/12 B 初值、磁盘变化后重扫为 7/12 B，再交给原生缓存验证属性文本、数值排序、PIDL 重枚举及 provider 重建；Release NativeProbe build 与 `--static-shell-column-folder-smoke` 通过，`window-created=False`。这只验证 scanner → 隔离 provider 的数据消费，不含产品异步扫描发布、Shell 通知或可见列；ExplorerBrowser 产品未改。完整范围见[派生大小列调查](folder-size-column-investigation.md#2026-10-31生产扫描结果与-shell-递归大小列合同对照)。

## 2026-10-31：隔离递归大小行缓存验证真实文件身份

- 行模型此前只按 Shell 绝对解析名、文件夹标志和文件系统路径复用递归扫描值；同路径目录替换无法与原对象区分。现在扫描值带有卷序列号和 Windows 128 位文件 ID；重枚举仅在同路径且文件 ID 相同时保留旧值，身份不同或无法读取时清除旧值，等待重扫。打开目录时不跟随 reparse point。
- `--shell-listview-row-model-smoke` 覆盖同路径移动/重建后文件 ID 变化、旧扫描值清除，并检查同身份行数据仍可复用；定向 smoke 通过，NativeProbe Release build 为 0 警告/错误。该身份只接入隔离行模型，不是产品 ExplorerBrowser 功能；当前未测大量可见目录下的身份读取成本、Shell view 列消费或用户可见行为。详情见[递归大小列调查](folder-size-column-investigation.md#2026-10-31同路径文件夹替换时拒绝旧扫描快照)。

## 2026-10-31：扫描完成时拒绝已替换目录的迟到值

- 真实文件系统夹具先证明旧逻辑会把同路径新目录的扫描汇总回写到旧行身份；又覆盖原目录移走、原路径无法读取的情况。现在发布前重新读取文件 ID，只有已知旧身份与当前身份一致才发布；身份不同或读取失败时丢弃结果、清空缓存，并在有新身份时记录该身份、等待下一轮扫描。两个迟到结果回归均在旧逻辑上失败、修复后通过；完整 Release SettingsSmoke 与 NativeProbe Release build 通过，0 警告/错误。
- 仅补强隔离 NativeProbe 行模型的路径键扫描竞态，没有接入产品 ExplorerBrowser/IShellView；后台结果仍由路径索引，且回写检查后至更新之间的文件系统竞争无法通过该检查消除。详情见[调查记录](folder-size-column-investigation.md#2026-10-31扫描完成时拒绝已替换目录的迟到值)。

## 2026-10-31：隔离行模型拒绝身份缺失时的路径扫描结果

当行枚举阶段无法取得目录文件 ID 时，按路径键控的扫描结果不能在稍后仅凭新读取到的 ID 被认作同一目录。真实临时目录回归先在旧实现上复现把过期 `666 B` 写入同路径替换目录；修正为仅当快照身份与发布时身份均可读且相同才接受。身份缺失、变化或不可读时清除大小/摘要，并记下当前可读身份等待下一次扫描。定向 smoke、完整 Release SettingsSmoke、App/NativeProbe Release build 均通过，0 警告/错误。仅改进 NativeProbe 隔离行模型，不接入产品；路径键扫描与身份读取之间仍有竞争窗口。详情见[递归大小列调查](folder-size-column-investigation.md#2026-10-31行身份缺失时拒绝路径扫描结果)。

## 2026-10-31：递归扫描结果的路径 ABA 身份缺口

复核确认当前扫描汇总按路径返回，结果本身没有记录扫描访问的目录对象身份。真实临时目录路径切换后再恢复原目录时，合成的替换目录结果仍会被发布端接受，因为行快照与发布时身份相同；这是接口信息不足的合成复现，不是对真实递归扫描时序的实测结论。该缺口尚未修复。后续隔离原型需将扫描结果绑定到实际打开的目录对象，或增加可证明不会遗漏变更的代际门控；扫描前后重复按路径读 ID 不足以覆盖 ABA。详见[扫描身份边界](folder-size-column-investigation.md#2026-10-31路径身份采样不能识别扫描期间的-aba-替换)。

## 2026-10-31：隔离扫描结果绑定实际目录对象

`HandleBoundRecursiveDirectoryScanner` 在 NativeProbe 通过已打开目录句柄枚举，并把结果连同根目录文件 ID 返回；子目录打开后须与父目录条目的 ID 一致才递归。真实临时目录 smoke 覆盖扫描期间根目录和子目录在同一路径被替换：旧根结果不会发布到替换目录，替换后的子目录也不会被旧父快照误计。700 个长 Unicode 文件名确认超过 64 KB 的多页枚举准确汇总，普通嵌套目录汇总也通过。定向行模型 smoke 通过。

这一改进收窄了 NativeProbe 内部递归大小扫描的路径 ABA 风险，不接入产品 ExplorerBrowser 或 F9 扫描。完整 Release SettingsSmoke、App/NativeProbe Release build（0 警告/错误）及 `git diff --check` 均通过；目录句柄无法解析路径时，无法继续解析的后代会计为不可读。

### 2026-10-31：句柄扫描结果进入无窗口原生列合同

`--static-shell-column-folder-smoke` 现在将句柄绑定扫描器与既有产品递归扫描器对照，确认同一夹具的 10/12 B 初值和 7/12 B 更新值相同，再把结果交给 NativeProbe 原生 provider 验证列文本、数值排序、PIDL 重枚举和 provider 重建；退出码 0，`window-created=False`。这扩展的是隔离 scanner → provider 合同，不接入产品 F9 或 ExplorerBrowser，也没有 DefView/可见列验收。

### 2026-10-31：DefView 更新派生列的受干扰复核

WPF Private Desktop 的单项通知探针初始从 `GetDetailsOf` 读出目录 `10`/文件 `4`；通知后 DefView 重新枚举，却丢失自定义列且没有再次请求行值。前台 HWND 未变，但系统输入 tick 变化，故该轮 exit 1，只作为诊断线索；本轮临时注册和夹具已清理。此前干净探针与 API 核对已指出 DefView 缺少受支持的逐项派生值失效入口，不再增加通知或 ListView 消息变体。该功能仍未接入产品；后续需转向评审自有视图模型及其完整 Shell 兼容门槛。

### 2026-10-31：IShellView 派生列恢复原型的新证据

Private Desktop 探针显示：目录更新后重设列和切换查看方式都不能让 DefView 刷新派生列；销毁并重建同一 `IShellView` 的窗口后列和值恢复。接入现有 `ShellViewSelectionSnapshot` 并以 no-focus 激活后，单选及两行多选场景均可恢复选区和焦点（各重复两轮）。302 行夹具恢复顶部滚动锚点到 y=0，并逐项保持列顺序和宽度；排序列和方向重建前后相同。显式把焦点交给新 `SysListView32` 后，模拟方向下键可移动选中项和 focused item。重建后背景 `IContextMenu` 可构造 7 项内存菜单但未显示/执行；新视图的选中项 `IDataObject` 可由 `IFileOperation.CopyItems` 成功复制测试文件。单独的重建目标窗口探针又确认新 HWND 上注册的 filesystem folder `IDropTarget` 可以接收选中项 `IDataObject` 并正确复制；这不是 OLE `DoDragDrop` 源端闭环。第一次宽拖放探针因系统 `OperationStatusWindow` 抢占前台而被守卫判失败；保留该失败，并仅在不运行跨卷操作的单复制复测中通过。通知路径现已在隔离 adapter 中确认：`SFVM_FSNOTIFY` 返回 `S_OK` 会接管消息，而返回 `E_NOTIMPL` 后 DefView 默认处理可在重建 view 后重新枚举并显示新文件。该路径第一次探针前台守卫通过，第二次功能断言通过但 WXWork→LDPlayer 前台变化使整轮守卫失败，故目前只有一次完整隔离通过。修正仅在 NativeProbe 原型；未迁入 ExplorerBrowser。Shell 自动迁移 HWND 焦点、菜单可见交互/命令、导航路由、其他文件操作及真实产品通知仍待验证，原型不能进产品。详见[派生大小列调查](folder-size-column-investigation.md#2026-10-31隔离复查视图状态流与-defview-窗口重建)。

### 2026-10-31：filesystem `IShellFolder` 背景菜单能力边界

无窗口 probe 在真实临时文件夹上通过 `IShellFolder::CreateViewObject(IContextMenu)` 取得并构造 21 项内存菜单，包含第三方扩展、“新建”和“属性”，未包含“查看、排序、分组、刷新”；没有创建 Shell view 或显示菜单。此接口不能替代 Explorer 视图级背景菜单，自有行列表仍缺受支持的原生 View 菜单/命令来源。详见[逐行大小调查](folder-size-column-investigation.md#2026-10-31普通文件夹的-shell-背景菜单来源)。

### 2026-10-31：递归目录扫描 A/B 微基准

本机暖缓存小型夹具对照产品路径扫描与 NativeProbe 句柄绑定扫描：350 个分支、700 个目录、2450 个文件、483700 B；两者结果一致。路径扫描 289.63 ms / 1.35 MB 托管分配，句柄扫描 68.97 ms / 2.45 MB 托管分配，后者本样本约快 4.2 倍但多分配约 82%。样本固定顺序且仅取 3 次平均，不代表网络共享、峰值/保留内存、长期 F9 行为或用户体验；足以支持继续跨夹具隔离验证，不足以替换产品 F9 扫描器。详见[递归扫描调查](folder-size-column-investigation.md#2026-10-31路径扫描器与句柄绑定扫描器微基准)。

### 2026-10-31：多行句柄扫描与长路径边界

为匹配 Q-Dir 每行目录大小场景，NativeProbe 新增一次打开扫描根、一次遍历并返回多个请求目录摘要和文件身份的隔离方法。对照产品单遍扫描与句柄单遍/逐目录扫描，C: NTFS 的宽、深、多文件三类临时夹具结果全部一致；减少父句柄路径重查后，句柄单遍托管分配比初版下降约 25%–33%，两轮内相对产品扫描的时延约快 4.1 倍、2.4 倍、31–45 倍；同级可见兄弟目录的逐目录扫描耗时接近。测试先暴露并修复了超过 MAX_PATH 后句柄子目录打开失败、root junction 未停止及扫描中目录移动后的路径重解析问题；长路径、移动目录、junction、递归扫描定向 smoke 通过。完整 SettingsSmoke 仍被既有 Soft-Refresh 文案断言失败（断言查找“刷新时不重新枚举”，当前菜单写作“不重枚举”），该断言未在本批次改动。句柄方案仍未接产品；SMB/UNC/provider 兼容、运行内存、ExplorerBrowser/IShellView 可见列和完整 Shell 交互均未验收。详见[递归大小扫描调查](folder-size-column-investigation.md#2026-10-31多行递归大小的单遍句柄扫描)。

### 2026-10-31：BCL 文件系统条目枚举候选

临时 harness 以 `FileSystemEnumerable<T>` 直接消费 `FileSystemEntry` 的名称、属性、长度；三类 NTFS 夹具与路径/句柄扫描摘要一致。两轮多文件夹具中 BCL 用时 11–19 ms、托管分配约 1.10 MB；当前路径扫描为 593–700 ms / 1.23 MB，句柄单遍为 16–24 ms / 0.49 MB。宽目录仅采一轮、主机负载使绝对时延大幅波动，因此先接入现有递归目录树扫描，而不替换 F9 选择扫描。接入后 `120 × 25` 文件夹具的托管分配为 261,232 B，旧实现基线为 1,225,152 B，减少约 78.7%；隐藏/系统项、超过 MAX_PATH、进度、取消、junction 和原有递归摘要定向 smoke 通过。条目元数据是枚举快照，并发路径替换、UNC/provider 兼容及可见 Shell 列行为仍待验证。详见[递归大小扫描调查](folder-size-column-investigation.md#2026-10-31-net-低层文件系统枚举器对照)及其后“BCL 条目元数据接入产品递归目录树扫描”记录。

### 2026-10-31：重建视图的 rename/delete 通知边界

继续补测隔离合成 `IShellView` 的通知契约：物理 backing 目录 `SHCNE_UPDATEDIR` 可让新项目重枚举并显示；对子项物理 PIDL 发送 `SHCNE_RENAMEITEM` 或 `SHCNE_DELETE` 都没有更新现有合成行。之后补发父目录 `SHCNE_UPDATEDIR`，两种情况均各重枚举一次并分别收敛到唯一新名称/删除后的行数。完整 Private Desktop watchdog exit 0，前台与输入不变、UUID 隔离资源清理完成；NativeProbe Release build 0 警告/错误。日志：`src/.state/native-probe-watchdog/20261031-120852-6807f5c2.stdout.log`。这只界定自有 PIDL 合成行与物理 child PIDL 通知之间的映射缺口，不代表 ExplorerBrowser 产品或真实 Shell 文件操作的通知序列。未修改产品代码；实现接入前仍需来源明确的 Shell 变更通知、合成 PIDL 行映射与完整操作/焦点/拖放验证。详见[派生大小列调查](folder-size-column-investigation.md#2026-10-31重建后的合成视图对子项变更通知)。

同日以重建后的 view 选中临时项目并实际执行 `IFileOperation::RenameItems` 与 `DeleteItems`，观察到 `SFVM_FSNOTIFY` 的事件值分别为 rename `0x1`、delete `0x4`；两者在物理 backing 文件已更改后均未重枚举，列表还保留旧行。探针显式补发父目录更新后各重枚举一次并恢复正确行数；完整 watchdog exit 0、前台/输入不变、临时注册与夹具清理完成。该证据取代上一段“尚未验证真实 Shell 文件操作通知序列”的限制，但仍是合成 PIDL adapter/DefView 实验，不代表 ExplorerBrowser 产品；没有产品接线或迁移。下一步应评估在隔离 adapter 中把物理子项变更映射成父级失效的最小方案，并复核是否造成重复枚举/通知循环。日志：`src/.state/native-probe-watchdog/20261031-121600-7acc9070.stdout.log`。详见[通知调查 follow-up](folder-size-column-investigation.md#2026-10-31重建视图真实-ifileoperation-通知复验更新前述手动通知结果)。

## 2026-10-31：隔离 IShellView rename/delete 通知转发

NativeProbe 的合成 PIDL adapter 在显式开启的实验开关下，把真实 `IFileOperation` 引发的 `SHCNE_RENAMEITEM` / `SHCNE_DELETE` 延迟 1 ms 转为 backing 目录 `SHCNE_UPDATEDIR`，同时由 DefView 默认处理原事件。连续三轮中 rename/delete 各只触发一次队列、一次目录通知和一次 provider 枚举；更新后没有再次排队。RED 轮先复现无转发时的 stale row。产品 ExplorerBrowser 未改；尚缺 create/update/move、多事件合并及完整 Shell 交互门槛。证据和下一步见[隔离派生大小列通知调查](folder-size-column-investigation.md#2026-10-31隔离-adapter-把-renamedelete-转成父目录失效)。

## 2026-10-31：隔离 IShellView 通知覆盖扩至 create/move

经 RED 复现后，NativeProbe 的显式通知转换开关也处理 `SHCNE_CREATE`。真实 `IFileOperation::MoveItems` 将条目移出源 view 时观察到 `SHCNE_DELETE`，父目录失效后源行正确消失。create、move-out、rename、delete 各自一轮只引起一次重枚举；完整 Private Desktop 场景连续三轮通过，前台/输入守卫与 UUID 清理均通过。该逻辑仍仅在合成 adapter 探针中，未进入 ExplorerBrowser。多事件合并、移动目标 view、剪切粘贴、OLE 拖放及其他 namespace 仍是迁移门槛；证据见[通知调查](folder-size-column-investigation.md#2026-10-31隔离-adapter-的-createmove-通知复核)。

## 2026-10-31：隔离 IShellView 三项 create burst 合并

三次连续 `SHCNE_CREATE` 连同 DefView 随后的 `SHCNE_UPDATEITEM` 在本机 Private Desktop 单 STA 场景中合并为一次父目录 `SHCNE_UPDATEDIR`，provider 仅枚举一次且三行均可见；连续三轮通过，前台/输入/临时资源守卫通过。证据边界仅为三项连续事件，不外推成并发、网络目录或多视图 burst 性能保证。逻辑仍只在 NativeProbe 合成 adapter 中，产品未改。后续检查通知数组和 timer 关闭生命周期，并继续 Shell 路由与完整拖放门槛；见[通知调查](folder-size-column-investigation.md#2026-10-31三项-create-burst-的通知合并)。

## 2026-10-31：隔离 IShellView 父目录更新计时器取消

NativeProbe 新增按 view 同步排队/取消 delayed backing-directory update 的测试入口。先排队再立即取消，并继续泵 STA 消息 400 ms，连续三轮分别确认已排队 1 次、已发送 0 次、provider 枚举 0 次；前台与输入守卫通过。首轮用外部 `SHChangeNotify` 立即读队列时回调尚未到达，该刺激时序不作为通过或失败证据。计时器生命周期验证仍只在隔离 adapter，没有 ExplorerBrowser 产品改动；窗口 owner 在销毁前的真实调用集成还未接线。日志 `20261031-123706-16b139a2.stdout.log`、`20261031-123730-3aff15f5.stdout.log`、`20261031-123741-8079b46c.stdout.log`。详见[隔离通知调查](folder-size-column-investigation.md#2026-10-31延迟父目录更新计时器的取消生命周期)。

随后将验证放入 view-window teardown：RED 轮确认不取消时，view HWND 销毁后仍会发送一次迟到的 `SHCNE_UPDATEDIR`；加入销毁前取消后连续三轮均未发送，且前台/输入/清理守卫通过。由此补齐了隔离 host 的 owner 顺序要求，但没有跨线程竞态证据或产品接线。日志 `20261031-124311-d534ae63.stdout.log`、`20261031-124323-615655c0.stdout.log`、`20261031-124335-9fbfaed6.stdout.log`。详见[通知调查](folder-size-column-investigation.md#2026-10-31延迟父目录更新计时器的取消生命周期)。

### 2026-10-31：DefView 派生列更新策略停止扩展

WPF Private Desktop 宿主上的单项通知、追加父目录更新、重放 `IColumnManager` 列、切换 Icon→Details，以及 DELETE+CREATE 均未恢复自定义派生列索引或触发取值/排序/重枚举。最新 DELETE+CREATE 轮前台/输入守卫与清理通过，列索引仍为 `-1`；`IShellView::Refresh` 样本因前台/输入变化不采纳。停止继续堆叠 DefView 通知变体。逐行目录大小仍是隔离原型，产品仍使用 ExplorerBrowser。下一阶段应评审自有行模型/绘制所需的 Explorer 兼容合同，重点为视图级菜单、排序/分组、键盘焦点、导航、Shell 文件操作、变更通知及 OLE 拖放；通过完整门槛前不迁移产品。详情见[派生大小列调查](folder-size-column-investigation.md#2026-10-31wpf-根宿主下的通知列重放策略收敛)。
