> **地址输入复核（2026-10-27）：**相对目录、环境变量与 `C:` 根目录规则早已在 AddressInputResolver 实现，与仅处理分隔符的 BreadcrumbPath 分开理解。本次仅修复显式 `\\?\` / `\\.\` 路径在前缀识别前被环境变量展开的问题，保留百分号目录字面量；普通输入继续展开当前进程变量。纯字符串回归与完整 SettingsSmoke 通过，未验证 Shell 对具体设备地址的支持。参考 [Q-Dir FAQ 146](https://www.softwareok.com/?faq=146&seite=faq-Q-DIR)、[Windows 路径命名空间](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)。本项不在此前已固定的 141624 候选中。

> **可见键盘验收补充（2026-10-27）：**用户在 125449/179260 四窗格 Details 夹具中确认一个样本 F2 可进入重命名，方向键保持编辑。只关闭该样本范围，未外推所有模式/窗格或全部 P0；F6 续测前隔离窗口已关闭。见[本轮证据](gui-acceptance-2026-10-27-shell-baseline.md)。

> **文件键错误分支（2026-10-27）：**全选/剪贴板/撤销/重做只在 Shell S_FALSE 后补发，错误/未知结果不再映射额外文件命令或排队恢复；COM 前后复核原输入目标。策略回归先失败后通过，构建通过；真实 accelerator、焦点与重入未运行，不能据此宣称实际文件操作已验收。

> **新原生分组观测（2026-10-27）：**完整捕获确认不同的非空分组/方向时接受新状态，退休旧恢复 timer/request；临时 None 继续保护。实际未挂接宿主的合并检查先失败后通过，不等于原生菜单操作验收；显式 None 的区分仍开放。

> **图标缓存引用（2026-10-27）：**应用静态图标缓存限制为 256 项 LRU，淘汰后可重新加载，已有图像消费者不受清理影响。实际缓存合成压力从旧 1,027 项变为 256 项，并验证 Shell 图标重载像素；不代表进程 MB 收益，普通文件夹共用图标，对既有大占用的归因仍未知。详见[内存记录](memory-sampling.md#2026-10-27应用图标缓存容量检查)。

> **筛选临时分组（2026-10-27）：**为枚举临时清除的分组不再进入保存/复制快照，单独保留键/方向，其他视图字段照常捕获；计时取消和失败不丢意图，旧请求不能替换当前恢复队列。未挂接宿主和模拟捕获检查通过，没有 Shell view；真实枚举、成功恢复、期间手动改分组及重启仍待可见验收。

> **快速筛选失败（2026-10-27）：**原生附件失败不再静默成功；当前请求异常时回滚设置/筛选/分组，过期请求不覆盖新访问。窗格应用/清除处理错误，失败不写历史、不隐藏原筛选。托管 COM site 验证真实接口/HRESULT，未挂接 Pane 的受控通知异常验证回滚和界面边界，两者不等于真实 Shell 失败端到端验收；分组、选区和慢刷新仍待可见验证。

> **完整视图恢复保护（2026-10-27）：**模式、列、排序或分组恢复失败/视图不可用时保留原配置，后台标签立即阻止不可靠快照，不再只保护自定义列；恢复只在同一次访问内成功，过期访问停止写入。未挂接宿主的两项缺陷先失败后通过，四字段失败/重试策略回归通过；真实 Shell 部分恢复与错误重试仍待可见验收。

> **候选验收准备（2026-10-27）：**已固定产品源码 `62274bc` 的隔离候选及 EXE/DLL 哈希，核对夹具与独立 profile，未启动 GUI 或发布；不包含其后的产品修复。准备脚本已修正旧上级目录断言与 F4 清单；P0 文件操作/焦点、导航交接、状态持久化、多屏和 QDR 的下一次验收顺序见[准备记录](gui-acceptance-2026-10-27-prepared.md)，所有可见结果仍为未运行。

> **原生导航键（2026-10-27）：**四方向保护扩展到 Home/End/PageUp/PageDown；文件区仅在 Shell S_FALSE 后补发，原生命名编辑框直接接收 keydown，保持原消息与 Ctrl/Shift 状态。八键及拒绝条件策略回归先失败后通过，未运行实际 Shell 键盘操作；范围选择、翻页和编辑焦点仍待可见验收。

> **当前目录命令（2026-10-27）：**移除 TabState 的可变路径副本，统一派生自 Browser.CurrentPath。未显示实际窗格验证旧地址仍显示时的当前目录、新标签、搜索关闭和地址编辑；四窗格迟到通知与新输入回归通过。未验证真实 Shell 时序、实际命令行启动或可见焦点。

> **标签快照的目录来源（2026-10-27）：**会话、固定标签、两类复制及关闭快照使用 Browser.CurrentPath，避免导航完成通知尚未送达时用旧 UI 路径配上新视图设置。实际未挂接窗格的交接阶段检查先失败后通过，不建立 Shell view；真实快速导航与重启结果仍待可见验收。

> **视图配置捕获代际（2026-10-27）：**读取模式、排序、分组和列期间核对 browser/read generation，COM 重入切换访问时拒绝整份捕获、保留旧完整缓存并停止后续读取。五阶段合成 getter 回调、稳定重试及完整配置回归通过，未创建 Shell view；真实 Shell 时序和可见保存仍待验收。

> **恢复关闭标签的地址记录（2026-10-27）：**关闭快照保留独立地址栏 MRU，恢复时带入新标签，后续复制也相互独立；保留关闭栈与记录各 20 条上限，不保留原生宿主。实际未挂接窗格回归先复现缺失、修正后通过；这不是 Shell 前进/后退历史恢复，真实菜单、下拉及焦点待可见验收。

> **新窗口跟随来源屏幕（2026-10-27）：**Ctrl+N、工作区窗口和无已存位置的附加恢复窗口，改按来源所在显示器工作区错位放置，保留已存位置优先级。负坐标、边界、超大尺寸的坐标检查及不可见普通 HWND 的真实定位检查通过，没有创建 Shell view 或输入键鼠；多屏 WPF 初始化和混合 DPI 仍待可见验收。

> **最小化后的再次激活（2026-10-27）：**无参数再次启动和目录转发共用窗口恢复入口，保留最小化前的最大化状态；F11 保留沉浸呈现及退出状态。实际未显示 MainWindow 的四类状态回归通过，未创建产品 HWND/Shell view；真实单实例消息、窗口位置及双屏效果待可见验收。

> **导航通知与输入完成（2026-10-27）：**目录/搜索/失败通知携带应用请求编号；旧结果仍更新实际标签位置，但不结束新地址提交、清掉新搜索或消费其焦点请求。部分重复目标批次不确认输入，已报告失败的请求不能被迟到成功改写。先复现同路径重试目标被提前清理和新地址提前结束，修正后通过实际队列、同目标失败与四窗格输入检查；真实 Shell 投递次序、ERROR_BUSY 重试及可见焦点仍待验收。

> **Shell 目录请求识别（2026-10-27）：**程序发出的目录请求与重试目标改用 PIDL 的持久化身份登记；Pending/失败回调也识别虚拟目录，避免“此电脑”或别名回调误取消更新的目录探测。五类真实 Shell PIDL 配合未挂接宿主的合成回调先复现旧误判，修正后通过完成/失败清理、迟到回调、重复计数及原生导航取代旧请求检查；没有 ExplorerBrowser/文件视图，真实快速切换与连续地址提交仍待验收。

> **新输入保护（2026-10-27）：**路径提交后继续修改会取消旧提交对地址编辑框的接管；搜索成功/失败回调及重新聚焦保留新的关键词或空草稿。匹配搜索结果可确认输入，显式关闭和切换标签仍重置。四个未显示实际窗格的控件事件与合成结果通知检查通过；没有 HWND/Shell view，连续路径提交的原生回调次序和物理输入仍待验收。

> **导航/搜索完成焦点（2026-10-27）：**两个完成回调改为消费当前导航的焦点请求，并使用已有活动窗格/输入意图/前台保护；原生命名或列标题聚焦时跳过。启动默认文件区意图与搜索提交路径同步衔接。未挂接实际宿主回归先复现旧请求残留，修正后验证过期、重复、pending、拒绝请求以及编辑取消和同步搜索失败；没有创建 Shell view 或操作桌面，真实慢目录/搜索和物理焦点仍待验收。

> **标签切换延迟焦点（2026-10-27）：**选中标签后的 Loaded 回调现在复核窗口当前的文件区焦点意图，避免仅凭标签仍选中/可见就把焦点从其它窗格、地址编辑或标签标题抢回。App/HostProbe Release build、既有焦点遍历与未显示地址编辑检查通过；真实输入竞争尚未复现，鼠标/Ctrl+Tab/标题方向键及跨窗格焦点仍待可见验收。

> **下载系统目录定位（2026-10-27）：**地址位置菜单使用 `shell:Downloads`，不再拼接默认用户目录。只读解析结果与 [SHGetKnownFolderPath](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderpath) 的当前下载目录相同，现有祖先/驱动器菜单 smoke 通过；未修改 Windows 重定向设置，实际迁移及菜单导航待可见验收。

> **现代地址栏点击编辑（2026-10-27）：**空白区域和分隔符左键点击现在进入完整路径编辑并全选；路径按钮继续导航，右键不触发编辑。四个未显示的实际 PaneControl 路由事件回归先复现旧实现失败，修正后通过；没有 HWND、Shell view 或桌面输入。物理鼠标命中与键盘焦点待可见验收，尚未发布。

> **窗口关闭与资源释放（2026-10-27）：**普通隐藏 HWND 验证父 HwndSource 释放后子宿主仍被暂存，显式 Dispose 才销毁。窗口 Closed 现释放全部自有文件视图（包括非活动标签）、目录树、地址弹出树和共享树，停止后台任务/定时器及迟到保存；两个未显示的实际 MainWindow 回归验证关闭一个窗口不影响另一个，会话先保存。未创建真实 Shell 文件视图，实际 Ctrl+N/关闭后的 COM 释放和内存收益仍待验收。生命周期依据：[WPF HwndHost 源码](https://source.dot.net/PresentationFramework/System/Windows/Interop/HwndHost.cs.html)。

> **递归计算所选项目大小（2026-10-27）：**“工具 → 计算所选项目递归大小”汇总当前文件视图选择中的文件和文件夹；父子目录重叠选择只计一次，不跟随重解析点，也会跳过路径祖先中的 junction。选择变化会取消扫描；F9 继续扫描当前目录并可取消当前递归扫描。复用状态栏格式，只有用户显式触发才运行。专项扫描器 smoke、完整 Release SettingsSmoke 与 App build 通过；菜单可见性及大目录交互尚未 GUI 验收。

> **状态栏当前目录大小提示（2026-10-27）：**新增默认关闭的“视图 → 未选中时显示当前目录可取得大小”，仅用 Shell 当前视图直接子项的可用属性汇总；选区存在时优先显示选中大小，不递归目录。完整 Release SettingsSmoke、App/NativeProbe Release build 通过；隐藏 ExplorerBrowser `SVGIO_ALLVIEW` 探针验证 3 个直接条目中两文件大小合计 8 字节、目录未纳入，前台未改变。未做可见 GUI 验收；属性缓存 5 秒，同数目内容变化可能短暂陈旧。详见[路线图](roadmap.md#2026-10-27未选中时显示当前目录可取得大小)。

> **文件视图复选框选择（2026-10-27）：**“视图 → 使用复选框选择项目”通过 Shell `IFolderView2::SetCurrentFolderFlags(FWF_CHECKSELECT)` 切换，默认关闭，便携偏好应用于现有和后续每个标签。Release SettingsSmoke 与 App Release build 通过；尚未运行 ExplorerBrowser/可见 UI，复选框点击与 Shell 选中状态待桌面验收。Ctrl+Shift+A 反选命令已存在；依据 [Q-Dir FAQ 77](https://softwareok.com/?faq=77&seite=faq-Q-DIR)。
> **文件视图整行选择（2026-10-27）：**“视图 → 整行选择”通过原生 `FWF_FULLROWSELECT` flag 作用于现有和新建标签，只修改该 flag；缺少有效 `FullRowSelection.ini` 时不覆盖 Shell 默认，用户显式切换后单独保存。专项 SettingsSmoke 与 App Release build 通过；未做 ExplorerBrowser/GUI 验收，详细信息/列表的实际高亮、命中测试及右键行为仍待确认。详见[路线图](roadmap.md#2026-10-27补齐-q-dir-整行选择选项)和[设计](superpowers/specs/2026-10-27-qdir-full-row-selection-design.md)。

> **多行标签栏（2026-10-27）：**“视图 → 多行标签栏”现可让标签标题按可用宽度换行，隐藏单行滚动箭头并启用纵向滚动；默认关闭，保存在独立便携 `MultilineTabHeaders.ini`。Release SettingsSmoke 覆盖单行滚动范围、多行行列坐标及配置往返，App Release build 通过。尚未运行可见 GUI；极窄窗格、拖动重排、标签标题动态变化和键盘焦点需人工验收。

> **标签标题最大宽度（2026-10-28）：**“视图 → 限制标签标题最大宽度”默认开启，保持原有 180 DIP 上限；关闭后标题可按文件夹全名自然扩展，遵循 Q-Dir 提供的最大宽度开关。偏好单独保存在便携 `TabHeaderMaximumWidth.ini`，发布隔离 profile 白名单已纳入该文件。Release SettingsSmoke 覆盖 INI 往返及 WPF 面板测量上限，完整发布 smoke、App Release build 通过；可见菜单/长标题布局仍待集中 UI 验收。

> **标签关闭按钮（2026-10-28）：**“标签页 → 显示标签关闭按钮”按需在可关闭标签上加入 × 按钮；默认关闭，固定标签和窗格中最后一个标签不显示按钮。保留原有中键/双击关闭行为，偏好独立保存在便携 `TabCloseButton.ini` 并复制到隔离预览 profile。配置/显示资格 smoke、完整 Release SettingsSmoke、App Release build 与发布隔离 smoke 通过；按钮命中、视觉密度、标签移动后状态及不同主题仍待集中 UI 验收。对标依据：[Q-Dir 12.55 功能历史](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)。

> **拖动悬停标签自动切换（2026-10-27）：**“标签页 → 拖动悬停自动切换”提供关闭和 0.2–3 秒六个延时档位，默认 1 秒，保存在独立便携 `TabHoverActivation.ini`。每窗格仅为 Shell 文件/文件夹拖动启动可取消的 dispatcher timer；应用内标签重排保持原行为。偏好、拖放目标 gate 与接线 SettingsSmoke 和 App Release build 通过。鼠标悬停是否持续收到 WPF DragOver、切换后的 Shell drop target 和实体文件拖放仍待 GUI 验收。

> **空白标签栏双击新建标签（2026-10-27）：**在标签标题滚动区域的空白处左键双击，复用 `NewTab()` 创建 Home/默认视图标签；标签标题双击仍走原关闭逻辑。接线 smoke 与 Release build 通过。真实鼠标命中和视觉仍待 GUI 验收。

> **新建标签使用当前文件夹（2026-10-27）：**“标签页”菜单可选择普通新建标签从当前窗格目录开始，默认关闭；便携设置保存在 profile 的 `NewTabLocation.ini`。指定目录新建和复制标签不受影响。Release SettingsSmoke 与 App Release build 通过，菜单与跨重启行为仍待可见验收。来源：[Q-Dir 官方英文功能表](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)。

> **跨窗格移动和复制标签（2026-10-28 复验）：**普通标签移动保留原 ExplorerBrowser/HWND 与 Shell Travel Log；复制创建独立 host。隐藏生产 `PaneControl` 探针现覆盖两个真实 Shell 标签分别导航、切换离开后恢复、通过窗格级 Back/Forward 命令验证当前标签路由，并确认一个标签后退时另一个的路径/Back/Forward 状态不变；现有跨窗格移动探针也已复跑通过，确认移动后仍能按原历史后退，关闭会销毁 host。为恢复旧 quick-filter 探针，本批同步补齐其已过期的 `PaneControl` 构造参数。两项均未显示窗口或改变前台；可见工具栏状态、真实焦点、布局切换及跨重启行为仍待集中 GUI 验收。Q-Dir 官方功能字符串列出“Move to new Tab Group”“Copy to new Tab Group”“Use Tab Groups”，但公开资料未说明其组语义或手势；本实现只对齐跨视图移动/复制需求，不宣称标签组语义相同。[Q-Dir 官方英文功能表](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)。

> **隔离 Shell 菜单命令队列（2026-10-27）：**共享 frame coordinator 现在按 FIFO 保存每条延迟 `WM_COMMAND` 的 pane owner 与命令 ID，防止 STA 处理前新命令覆盖旧槽。隐藏 frame smoke 验证两个 owner/ID 的顺序与身份，NativeProbe Release build 和完整 `--shell-view-color-policy-smoke` 通过；未创建 `IShellView`，真实菜单回调时序仍待验证。产品未改。

> **IShellView 延迟 View 命令拒绝过期 pane（2026-10-27）：**隔离 NativeProbe 的共享菜单 frame 会把 `WM_COMMAND` 延迟到自定义消息执行；切换 pane 后，旧 View 菜单命令之前可能沿旧 owner 发给已不活动的 view。现有执行前再次核对 coordinator owner，过期命令被消费但不派发。新增隐藏 frame 两 pane 回归；NativeProbe Release build 与无 Shell-view 的 `--shell-view-color-policy-smoke` 通过。仅是迁移原型门控，不代表产品 ExplorerBrowser 或真实菜单交互验收。详见[原型记录](ishellview-color-spike.md#2026-10-27拒绝已过期-owner-的延迟-shell-view-命令)。

> **隐藏 frame 合并菜单消息链（2026-10-27）：**`--shell-menu-group-routing-smoke` 现经过真实 coordinator subclass、来源跟踪、延迟消息队列及隐藏 pane HWND；File/View 重复 ID 经 `WM_MENUSELECT` 和按位置的 `WM_MENUCOMMAND` 都按来源组收到原始 `WM_COMMAND`，连续入队并先后更改选择来源时，FIFO 仍保持来源不串线；Object 组同 ID 被拒绝；pane 退出后已排队命令不再送达旧 view。构建 0 警告/错误。未创建 `IShellView`，也未触发系统真实菜单模态跟踪，Shell/OLE 菜单接收窗口及时序、用户可见行为仍待独立验证。详见[原型记录](ishellview-color-spike.md#2026-10-27-隐藏-frame-coordinator-消息链-smoke)。

> **ExplorerBrowser Shell 状态提示（2026-10-26）：**每标签浏览器的 `StatusTextChange` 事件现经原生事件 sink 投递到独立的每窗格提示区域，错误/搜索/F9 状态仍使用原来的状态区域；事件按 COM 连接线程和订阅身份检查，标签切换恢复对应浏览器最近文本。SettingsSmoke 覆盖 native `IDispatch` BSTR、清空和卸订阅后的迟到回调，完整 Release SettingsSmoke 与 App Release build 通过。未启动 ExplorerBrowser/GUI，Shell 实际是否发送提示、鼠标悬停内容与四窗格布局仍待可见验收。

> **Shell 命令状态查询重入刷新（2026-10-26）：**Shell 原生命令查询期间若重入事件改变选区/活动上下文，查询会丢弃过期快照。现有合并刷新门控会保留查询期间到达的刷新请求，避免嵌套 timer 消费后令工具栏停留在旧启用状态；外层查询结束后只补一次刷新。纯逻辑回归和完整 Release SettingsSmoke、App Release build 通过；COM 重入与可见工具栏状态未验收。详见[路线图](roadmap.md#2026-10-26shell-命令状态查询期间保留刷新请求)。

> **Alt+Tab 与 Shell view 重新激活的文件操作焦点恢复（2026-10-27）：**窗口失焦不再销毁仍有效的恢复请求；后台暂停 Shell 焦点读取和 `SetFocus`，期限只累计窗口活动时间。另发现内容 HWND 与 focused item 即使看似正常，`IShellView` 仍可能未重新 UIActivate；每个文件操作恢复请求现只尝试一次 `UIActivate(SVUIA_ACTIVATE_FOCUS)`，已进入重命名、窗口失活或请求过期时跳过，后续轮询不重复。新状态 smoke、完整 Release SettingsSmoke 和 App Release build 通过；没有使用剪贴板或可见 GUI，真实 Ctrl+C/V、Shift+Delete、Alt+Tab 与多窗格方向键时序仍待验收，文件操作焦点 P0 未关闭。详见[路线图](roadmap.md#2026-10-27文件操作后重新激活-shell-视图候选) 与 [Alt+Tab 恢复记录](roadmap.md#2026-10-27alt-tab-时暂停文件操作焦点恢复期限)。

> **按目标 PIDL readiness 节制刷新选区重试（2026-10-25）：**两次 20,000 项隔离 stock `IShellView` 样本中，`ItemCount` 已达到 20,000 后，4 个分散目标 PIDL 仍在随后约 17–20 ms 内才依次可由 `GetItemPosition` 定位。生产 deferred restore 现复用当前 Shell 选区代际内已成功定位的 PIDL，只探测未定位目标；`SelectionVersion` 变化清空缓存。选区完整恢复后只剩焦点重试，现跳过后续 readiness 扫描；该门控有 SettingsSmoke 与 App/HostProbe Release 编译验证，但未取得新的运行时延迟样本。readiness 不可用时保留旧行为，1.5 秒最终回退仍尝试恢复。完整 SettingsSmoke、App/HostProbe/NativeProbe Release 构建和隔离 NativeProbe 通过；没有运行 ExplorerBrowser 产品 HostProbe 或可见 GUI，故多窗格真实刷新/延迟收益未验证，自动刷新 P0 仍开放。详见[路线图](roadmap.md#2026-10-25按目标-pidl-可定位状态节制选区重试)。

> **Shell 文件命令过期请求前置取消（2026-10-27）：**`Invoke` 现于构造原生菜单前检查 pane/导航/选择上下文，并在菜单构造及 canonical verb 启用查询后再次检查，避免已过期请求仍创建或调用 Shell 菜单。SettingsSmoke 先验证了旧实现会为预先过期请求创建菜单，再覆盖预过期、菜单构造重入、命令状态查询和 New Folder 取消。Release SettingsSmoke 与 App Release 构建通过（0 警告/错误）；未调用实际 Shell 命令、未启动 ExplorerBrowser 或可见 GUI，菜单 COM 重入仍待隔离原生验证。详见[路线图](roadmap.md#2026-10-27-shell-文件命令过期请求前置取消)。

> **文件操作后项目焦点恢复（2026-10-25）：**恢复门控现在同时检查原生内容 HWND 与非空目录的 `IFolderView.GetFocusedItem`；Shell 没有 focused index 时会尝试把有效 selection mark 设为焦点，并保留其 selected 位。空目录仍可在内容区保留键盘焦点。纯状态 SettingsSmoke 与 App Release 构建通过；默认 `IShellView` 的选区/焦点矩阵通过，但本机夹具无法构造 selection mark 有效而 focused index 缺失的状态，回退分支尚无原生运行证据。源码还发现文件操作重试门在 HWND/项目索引都看似正常时会跳过 `FocusView()`，而窗口 `Activated` 路径会重调它；这与用户报告的 Alt+Tab 后恢复相符，但尚未验证因果。真实粘贴/删除后的物理方向键行为仍待可见验收。

> **文件操作焦点恢复尊重列标题输入（2026-10-26）：**焦点恢复待处理时，`SysHeader32` 列标题输入现在会取消文件项焦点恢复；文件区输入仍可保留恢复请求，以支持粘贴/删除后恢复方向键操作。新增门控 smoke、完整 Release SettingsSmoke 与 App/HostProbe Release 构建通过。没有运行 ExplorerBrowser 或 GUI；物理排序时焦点与真实文件操作后的键盘行为仍待验收。

> **文件操作焦点恢复尊重重命名编辑框（2026-10-26）：**当前 ExplorerBrowser 内原生 `Edit` 重命名框仍持有焦点时，焦点监视现在视为文件区焦点已恢复，即使 `IFolderView.GetFocusedItem` 暂时读不到项目焦点；此时跳过 focused-item 查询，另一窗格的编辑框不会满足本窗格门控。回归用例、完整 Release SettingsSmoke 和 App Release 构建通过。未执行文件操作/重命名 GUI，真实多布局箭头编辑仍待验收。

> **刷新后瞬态空视图计数恢复（2026-10-25）：**生产宿主两轮复跑重现刷新回调连续读到空视图后，Shell 计数恢复为稳定非零但仍与 watcher 目标相差一项；旧逻辑继续重复刷新且无法按时重建基准。现等非零计数稳定后按 Shell 实际数量重基准，避免重复刷新循环。完整 SettingsSmoke、App/HostProbe Release 构建和隔离生产文件操作探针通过，选区与独立焦点保持；临时预览 [temp.53](../releases/preview-0.6.0-temp.53/) 已打包。此为隐藏本机有限样本，不证明可见刷新时序、慢速/网络目录或用户真实 Ctrl+V；自动刷新 P0 继续开放。

> **焦点故障诊断准备（2026-10-25）：**GUI smoke 现可通过 `-CaptureFocusTrace` 将按键、`IShellView.TranslateAccelerator` 返回值、焦点窗口类和 Shell focused/marked index 记录到隔离运行目录；默认不开启。该跟踪用于下次经授权的真实粘贴/删除复现，不能替代键盘操作验收，也不表示已有焦点根因已确认。

> **目录树模式（2026-10-25）：**应用会话 v10 增加共享单树、逐窗格树和关闭目录树模式；共享树跟随活动窗格并通过原生 Shell 树控件导航。逐窗格树可分别拖动宽度，共享树也可独立拖动；宽度保存在便携 `NavigationTreeWidths.ini`（默认 160/190 DIP，范围 96–600 DIP），并由 Explorer Dirs `.qdr` 扩展 v4 保存五路宽度；旧 v1–v3 扩展仍可读。SettingsSmoke 覆盖会话迁移、宽度配置和扩展回归，App Release 构建通过。未启动 GUI；共享树焦点路由已接入每窗格 Tab/Shift+Tab 与 F6/Shift+F6 停靠点；布局切换后也会恢复该布局的活动窗格并同步共享树路径，纯逻辑回归通过。拖动手感、窄窗格布局、真实原生焦点和多布局可见体验仍待人工验收。`TreeNsplit` 语义仍未解码，与应用自有宽度配置分开处理。

> **大目录刷新选区/焦点恢复（2026-10-25）：**此前 50k 隐藏生产宿主验证旧路径最终保留多选及未选焦点；焦点恢复已移除全视图 PIDL 预扫描，直接设置后再核验。最新“批量恢复优先、读回不匹配再使用旧种子兜底”策略现已通过隔离默认 `IShellView` 的未分组/分组 × 八种视图矩阵，覆盖 direct、额外选区 fallback、部分失效和全部失效快照；读回的选区与焦点符合预期。该探针仅四个临时项目，没有验证 ExplorerBrowser 产品宿主或大目录性能。STA 大目录响应 P0 仍开放；不可把新策略的潜在耗时收益计作已测结果。

> **自动刷新计数暂不可读及重入保护（2026-10-26）：**目录变更协调器在 Shell 条目数暂不可读时等待下一次计数读取，保留 watcher 增量；运行中的主 timer、合并创建/文件夹删除通知及异步刷新完成回调均在 Shell 计数读取后复核 monitor/navigation 版本、视图路径和 watcher queue，拒绝过期读数，并防止旧完成回调清理新监视器状态。重命名和 watcher 故障等强制刷新仍立即执行。纯状态 smoke 覆盖不可读等待、恢复差异处理、强制刷新和过期完成结果拒绝。真实 ExplorerBrowser 可见更新、慢速/网络目录和 STA 长尾未验证，自动刷新 P0 仍开放。

> **大批量稳定计数窗口遇到暂不可读样本（2026-10-25）：**Shell 计数暂不可读时清除“连续稳定计数”观察，防止恢复后同一旧计数被当作连续样本提前刷新；累计等待上限和 watcher 增量保持。新增状态回归、Release SettingsSmoke、App/HostProbe Release 构建通过；没有运行产品宿主、计时或可见 UI，自动刷新 P0 仍开放。

> **瞬态空视图恢复窗口生命周期（2026-10-26）：**Shell 计数重新与 watcher 目标一致时，关闭此前空视图恢复剩余预算，避免旧恢复状态影响下一次独立目录变化。新增状态回归、Release SettingsSmoke、App/HostProbe Release 构建通过；未运行产品宿主或 GUI，自动刷新 P0 仍开放。

> **大批量停滞视图 fallback（2026-10-25）：**至少 32 项的 Shell 计数差异在计数持续变化时仍允许枚举；计数连续四次不变时提前进入现有刷新恢复，计数前进会重置停滞窗口，12 次总等待上限保留。专项状态 smoke 和 App Release 构建通过；没有新的生产宿主或性能样本，不能据此称 P0 已关闭或延迟已改善。详见[路线图](roadmap.md#2026-10-25自动刷新大批量停滞视图提前恢复)。

> **自动刷新同步异常恢复（2026-10-25）：**Shell 刷新在异步完成回调前抛异常时，现在会释放进行中门控、保留强制刷新并按有上限的退避重试；异常诊断不写入路径或消息。Release SettingsSmoke 与 App 构建通过，真实 COM 异常和可见更新仍待验收，自动刷新 P0 未关闭。
> **刷新调用失败处理（2026-10-25）：**`GetCurrentView` / `IShellView.Refresh` 返回失败码或 ExplorerBrowser 宿主不可用时，不再触发成功完成回调；目录 watcher 将保留强制刷新并按退避重试，手动刷新和筛选恢复会报告失败。Release SettingsSmoke 与 App 构建通过；真实 Shell HRESULT 及 GUI 更新仍待验收，自动刷新 P0 未关闭。
# Q-Dir / Explorer 核心操作对照

> **OLE 拖放源按键状态（2026-10-27）：**`IDropSource` 现在等待启动鼠标键先按下再于释放时完成，避免 OLE 循环启动时键尚未按下便立即结束；Release COM vtable smoke 通过。没有运行拖放循环或输入，真实 OLE 拖放仍是 P0 未验收。

> **显式授权 OLE Copy 探针（2026-10-27）：**NativeProbe 提供 `--shell-view-ole-drop-copy --AllowInteractiveDesktopControl`，以临时目录、真实双 `IShellView` 和选中项 `IDataObject` 执行 Copy 后核对内容；NativeProbe Release build 通过，命令未运行。它用当前交互桌面，鼠标若放到目标窗格外可被其它 Shell target 接收，只能在明确授权且可隔离操作的会话中手动运行，不属于产品拖放验收。

> **交互 OLE 探针窗口清理（2026-10-27）：**交互模式现在在退出时销毁可见顶层窗口及 pane 子窗格；Private Desktop 模式继续让 watchdog 回收顶层 HWND。隐藏窗口生命周期 smoke 与 NativeProbe Release build 通过，未运行真实 Shell view 或拖放。详见[隔离原型记录](ishellview-color-spike.md#2026-10-27交互-ole-探针顶层窗口清理)。

> **隔离拖放原型接线（2026-10-27）：**NativeProbe 的 `ShellViewDragSource.RunOleLoop` 已将真实 Shell `IDataObject` 与 `IDropSource` 接到 `ole32!DoDragDrop` 调用入口，NativeProbe Release build 通过；本轮没有执行调用，也没有移动光标或注入输入。真实 OLE 拖放、操作效果、视图收敛和可见交互仍为 P0 未验收，产品继续使用 ExplorerBrowser。详见[原型记录](ishellview-color-spike.md#2026-10-27-ole-dodragdrop-调用入口接线)。

> **虚拟 Shell 标签和快捷链接图标（2026-10-27）：**标签标题与快捷链接现在按 Shell 位置 PIDL 读取虚拟文件夹图标；普通文件夹和文件扩展名图标维持原逻辑。定向 Windows Shell smoke 确认文件夹、“此电脑”和“回收站”图像不同。当前没有 GUI 验收，实际控件绘制仍待检查。详见[路线图](roadmap.md#2026-10-27虚拟-shell-标签和快捷链接使用位置图标)。

> **标签栏显示模式（2026-10-26）：**对照 Q-Dir [FAQ 101](https://www.softwareok.com/?faq=101&seite=faq-Q-DIR) 的“始终显示 / 至少两个标签时显示”选项，视图菜单现在可切换两种模式，并保存到便携 `TabStripVisibility.ini`。默认保持始终显示；按每个窗格的标签数量显示/隐藏标题栏，Shell 视图、标签对象和会话不受影响。Tab/Shift+Tab 与 F6 停靠点同步移除隐藏的标签栏；策略/INI smoke、扩展焦点矩阵、完整 Release SettingsSmoke 和 App Release build 通过。真实菜单操作、切换后键盘焦点和跨重启可见效果尚未桌面验收。

> **Watcher 自然更新后的选区校正（2026-10-26）：**删除未选中项目时，隐藏生产宿主曾观察到 Shell 将原焦点项加入选区而无最终选区回调。现在 watcher 变化已结算且计数自然匹配时，若当前路径/导航/用户输入代际仍匹配，就按缓存快照复核并恢复原生选区与焦点；仅本轮有 watcher 变化时才触发，空闲轮询不触碰焦点。隐藏定向探针、用户清空选区、10,000 项产品压力和 Release SettingsSmoke 通过。真实 ExplorerBrowser 可见交互、其它 Shell provider 与 GUI 验收未做；自动刷新 P0 仍开放。详见[调查记录](live-update-investigation.md#2026-10-26-watcher-自然收敛时的选区漂移)。

> **所有查看方式的列标题（2026-10-25）：**源码已在 ExplorerBrowser 初始化前启用 `FVO_CUSTOMPOSITION`，并在 `OnViewCreated` 清除两个列头隐藏标志，Shell 其余 folder flags 保持不变。纯逻辑 smoke 和 Release 构建通过；由于当前不使用 Computer Use，未运行隐藏 ExplorerBrowser 宿主探针或 GUI。八种模式的列头渲染、排序/排列、选区与原生菜单行为仍待验证；此项尚未作为已验收功能发布。

> **快捷键对照（2026-10-25）：**重新核对 Microsoft File Explorer 清单后，没有发现应由应用新增处理、却仍未覆盖的常用快捷键；标准文件操作和树控件按键保留给 Shell/原生树，应用自有导航、标签、查看和窗格组合由宿主处理。已明确记录一个有意差异：Explorer 在最后一个标签按 Ctrl+W 会关闭窗口，本程序为每个窗格保留至少一个标签，因此最后一个标签不关闭。详见[快捷键清单](keyboard-shortcuts.md)。

> **Q-Dir 命名列预设范围（2026-10-25）：**官方 FAQ 137 确认列头 Ctrl+右键可按名称保存/载入查看方式、列与列宽；FAQ 28 说各 Q-View 独立管理列并考虑文件夹类型，FAQ 14 另述在视图间传递列设置。本程序已提供顶部“视图”菜单的具名预设，并新增列标题 Ctrl+右键快捷入口。公开说明没有明确预设是否带排序/分组或其作用域；本程序预设不覆盖标签自己的排序/分组。详见[路线图](roadmap.md#2026-10-25补录-q-dir-命名列布局预设缺口)。实际菜单位置和列应用仍待可见验收。

> **Q-Dir 现代地址栏目录树对照（2026-10-25）：**SoftwareOK 官方产品页的当前版本历史列出 Q-Dir 12.82（2026-10-24 更新）；FAQ 116 说明 Modern address bar 第一次按 F4 显示地址栏目录树，第二次按 F4 聚焦路径编辑。本项目已接入每窗格独立的原生 Shell 树 Popup：F4 在 Modern 样式下按“树、编辑”两阶段工作，Classic 样式直接进入路径编辑；地址历史保留独立下拉按钮。代码接受可解析并确认是文件夹的绝对 Shell 解析名。Popup 定位、焦点、Escape/失焦关闭及虚拟文件夹可见导航仍待桌面 GUI 验收。[Q-Dir 产品页](https://www.softwareok.com/?seite=Freeware/Q-Dir) · [FAQ 116](https://www.softwareok.com/?faq=116&seite=faq-Q-DIR)

> **焦点遍历更新（2026-10-25）：**每个窗格的状态栏“查看”按钮现作为文件区之后的 Tab 停靠点，并纳入 F6/Shift+F6 的区域循环；信息文本仍为只读内容。每窗格树模式显示的导航栏“树”按钮也已纳入刷新与地址栏之间的 Tab 顺序，离开该模式时不再占停靠点；32 种动态停靠点组合的 Release 回归通过。菜单打开/关闭、原生列标题和四窗格真实焦点仍未 GUI 验收。

> **选区调查状态更正（2026-10-24）：**下方较早的自然删除选区丢失记录已被[基线复核](live-update-investigation.md#2026-10-24-更正自然删除选区丢失是探针基线失配)取代。原探针通过 Shell COM 改选区但未同步产品输入代数/快照；修正后有效隐藏样本保留了未受影响选区与焦点。不要把旧样本当作已确认缺陷或据此增加恢复逻辑。此更正仍不替代可见 ExplorerBrowser/Explorer 对照。自动刷新 P0 因 STA 长尾及慢速/网络、可见体验继续开放。

> **Shell 命令状态查询的上下文刷新与重入保护（2026-10-25）：**编辑菜单和工具栏准备状态时，如果构造原生菜单或读取命令启用状态导致当前窗格/导航/选区失效，现立即释放当前菜单、停止后续菜单查询，并整体禁用这一轮快照；SettingsSmoke 对两个重入点通过。工具栏处于悬停或键盘焦点时，活动窗格选区、导航完成/失败、标签切换会触发 150 ms 合并查询；工具栏空闲时不查询 Shell 扩展菜单。刷新门控 smoke 覆盖非活动窗格、空闲、合并及上下文变化。可见工具栏启用状态和第三方扩展仍待桌面验收。

> **目录元数据自动更新（2026-10-24）：**当前目录 watcher 现订阅文件大小、修改时间、属性和创建时间；现有文件内容/元数据变化以零计数强制刷新请求进入已有合并队列。映射回归、SettingsSmoke 与 App Release 构建通过；未运行 ExplorerBrowser/HostProbe，实际 Windows 事件和可见列刷新待验收，且不递归监听子目录。该改动尚未打入 temp.10。

2026-10-24 自动刷新大批量 watcher 活动门控：Release 隐藏 HostProbe 中增加 32 项以上差异的 750 ms watcher 静默门控，避免将持续创建/删除期间的部分 Shell 枚举计数重设成新基线；单项差、重命名与 watcher 错误仍沿用快速/强制刷新。10k 与一轮 50k 复验最终视图及选择/焦点正确，50k fallback 从旧样本的多次降至本轮 2 次；性能波动和因果限制见[调查记录](live-update-investigation.md#2026-10-24-大批量-watcher-活动期间的部分-shell-基线与延后兜底)。自动刷新 P0、可见窗口和网络盘验收仍开放。

2026-10-24 Q-Dir 状态栏空间概览补齐：[官网](https://www.softwareok.com/?seite=Freeware/Q-Dir)列出分区空间使用概览。本程序已有的后台容量读取现以百分比优先显示当前用户可用比例，并同时保留可用容量；百分比四舍五入到整数，总容量为 0 时不显示比例，工具提示仍保留可用/总容量原值。格式回归和 SettingsSmoke 通过；四窗格可见宽度、挂载点/配额与网络盘展示仍待 GUI 验收。

2026-10-27 Q-Dir 状态栏显示/隐藏：官方 [FAQ 36](https://www.softwareok.com/?faq=36&seite=faq-Q-DIR) 说明可按偏好显示或隐藏文件视图状态栏。本程序“视图 → 状态栏”默认开启，使用独立 `StatusBar.ini`；关闭后隐藏每窗格状态信息、项目计数、磁盘空间及状态栏查看按钮，并从键盘 Tab/F6 次序中移除隐藏停靠点。配置往返、外部编辑保护、未来格式拒写及完整焦点矩阵通过；实际菜单交互、隐藏栏后的视觉空间和真实焦点仍待可见验收。

2026-10-27 状态栏选中大小偏好：Q-Dir 官方英文功能清单列出对象数量与选中对象大小等状态栏显示选项。本程序新增“状态栏显示选中项目大小”，默认开启并用独立 `StatusBarSelectionSize.ini` 保存；关闭时不查询选区大小属性，仍显示项目总数/选中数。此项不是递归大小；递归扫描仍由 F9 显式启动。配置 smoke 和可见菜单/状态栏行为分开验收。

2026-10-24 文件夹删除 RMDIR 隐藏对照：3 轮不手动提前通知均在产品 250 ms 合并后发送 RMDIR、fallback=0；3 轮额外提前发送中有 2 轮在 140/153 ms 更新，另 1 轮由产品后续通知/fallback 收敛。`NONE` 组不是无 RMDIR 控制；现有 coalescing 规则不变，GUI 与大批量行为仍待验收。见[调查记录](live-update-investigation.md#2026-10-24文件夹删除提前与合并-rmdir-对照)。

2026-10-24 Release 隐藏生产 `--live-shell-product-monitor` 通过：单文件操作、跨窗格复制/删除/移动和文件夹创建/删除都到达对应视图；选区与焦点恢复。整轮仍触发 9 次 fallback，文件夹删除 fallback 1 次且没有发 RMDIR；这只证明受控本机 Shell 路径，不等同于可见 UI 或低延迟验收。详见[调查记录](live-update-investigation.md#2026-10-24生产-shell-文件操作与自动更新复验)。

2026-10-24 校正消息泵探针后的第二轮 10k 隐藏压力样本通过：最终视图、磁盘、两项选择和独立焦点正确，STA 取消息最大间隔 71 ms、ContextIdle 最大间隔 219 ms；仍有两次计数 fallback，自动刷新 P0 未关闭。单次隐藏样本不代表可见响应或长期稳定性，见[调查记录](live-update-investigation.md#2026-10-24校正消息泵探针后的第二轮-10000-项复测)。

2026-10-24 创建通知批次修正：同一创建批次在 60 ms 安静期内只保留一次待派发状态，计时器确认最后一次事件已静默后才消费最新路径。Release 隐藏 HostProbe 10k 创建/删除和完整 SettingsSmoke 通过，无 fallback、选区及焦点恢复；但未插桩样本仍出现 9.645 秒 ContextIdle 空档。自动刷新 P0 与 GUI 验收继续开放，不能据此宣称响应时延已解决。详见[路线图](roadmap.md#2026-10-24创建事件通知派发合并与-10000-项复测)。

2026-10-24 删除自然更新波动复测：Release native-only 隐藏宿主 2 个有效轮中，一轮等待 15 秒仍保持旧计数，另一轮于 538 ms 自然收敛；生产 watcher 3/3 轮于 338–362 ms 视图收敛、363–396 ms 批次结算，均通过一次 fallback 且保留选区/焦点。此结果只证明本机隐藏宿主样本，自动刷新 P0 继续开放。详见[调查记录](live-update-investigation.md#2026-10-24-native-only-删除选区恢复稳定复测)。
2026-10-24 10k 产品 watcher 当前复测：创建/删除事件均完整计数，最终恢复 3 项并保留两项选择和独立焦点；创建/删除各有一次计数 fallback，post-operation settle 约 5 秒，最大 Dispatcher 心跳间隔 125 ms。仅限本机隐藏宿主，本地自动刷新可收敛但响应成本仍待优化和 GUI 验收。详见[调查记录](live-update-investigation.md#2026-10-24-10000-项-watcher-当前复测)。

2026-10-24 对标信息刷新（2026-10-25 树模式字段状态已更新）：[Q-Dir 官网](https://www.softwareok.com/?seite=Freeware/Q-Dir)当前版本历史列出 12.82（2026-10-24 更新）；用户本机 12.79 的配置与 GUI 调查仍作为独立本机基线，两者不混用。官网列出的能力还包括预览、每个文件视图独立目录树、颜色/高亮过滤、当前视图导出 CSV/TXT/HTML/XLS 与打印。当前源码已有逐窗格目录树、文本快速筛选和当前视图导出；导出整份可见视图（包括有选中项时）已由用户确认，实际菜单操作、Q-Dir 对照和超大目录性能仍待验收。当前视图打印已接入文件菜单与工具栏，并有按视图方式生成打印布局的隐藏 HostProbe 验证；打印预览、分页质量及 Q-Dir 可见对照仍待验收。颜色高亮仍缺。预览菜单和工具栏切换已接入，并逐标签保存；`.qdr` 的全局 `Vorschau=1/0` 已映射到导入标签。目录树状态逐窗格保存；`Tree1`～`Tree4` 按暂定 0/1 映射读写并仍待可见 Q-Dir 验收，`tree_mode` 已按静态证据映射为 0=关闭、1=共享、2=逐窗格；`TreeNsplit` 仍未接入。Preview Handler 可见渲染与跨窗格显示效果尚未验证，不能据此宣称预览功能完成。官网功能页用于能力清单，不代替本机版本和实际 GUI 互操作证据。

2026-10-25 Q-Dir 递归大小对照：官网明确列出 F9/工具菜单中的大小设置、递归文件与子目录计数、把文件夹大小显示在“大小”列及计数显示在“类型”列，并说明排序时忽略预计算以避开导航性能问题。另一个官方页面说明可在状态栏显示当前目录总大小。本项目继续保留 ExplorerBrowser 原生列与排序，这次先接入按需 F9 当前目录递归总量/文件数/子目录数，后台执行且可取消，不自动扫描、不跟随重解析点；真实 NTFS junction smoke 现验证不会计入链接目标文件。结果在本程序状态栏显示，扫描时按批次报告已检查条目数（不是百分比或预计剩余时间）。它补上当前目录汇总能力，但尚不等同于 Q-Dir 的逐行文件夹大小列、大小单位/百分比设置或排序。可见进度刷新与大目录/网络盘性能仍待 GUI 验证。

2026-10-25 逐行文件夹派生列调查：`IFolderView2::SetViewProperty` 看起来可为当前视图项目缓存系统属性，可能让原生“大小”列显示计算值；微软将该接口标记为弃用，且不支持搜索结果/库视图，尚未在产品视图可见验证。系统 `System.FileCount` 不是列属性，不能直接成为“类型”列中的子项计数。实现边界和下一步验证条件见[专项调查](folder-size-column-investigation.md)。

2026-10-26 原生 Shell 视图架构评审：逐行派生列最值得先验证的是隔离 `IShellFolder2` folder adapter + Windows DefView；该组合仍需独立 `IShellBrowser` 宿主，不是 ExplorerBrowser 列插件。当前产品继续使用 ExplorerBrowser，逐行大小列不进入产品实现。静态列显示/数值排序可作为无递归阶段；只有它通过后才评估原生项目委托和更广的迁移门槛。[架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。

2026-10-24 Q-Dir 颜色高亮技术边界复核：[FAQ 6](https://www.softwareok.com/?faq=6&seite=faq-Q-DIR)明确列出扩展名通配符、多扩展名分号列表、隐藏项 `#`、文件夹 `#DIR`、只读项 `#RO` 和多组颜色规则；[FAQ 47](https://softwareok.com/?faq=47&seite=faq-Q-DIR)还列出背景占位符 `#BG`（视图背景）与 `#BG-A`（活动窗格背景），以及 `-1/0/1` 后续规则可否覆盖当前字段的标志。FAQ 47 说明从 Q-Dir 7.21 起，需先启用相应选项，颜色设置才会随收藏 `.qdr` 保存，因此这些字段并非每份工作区必有。现有可读 Q-Dir.ini 的 `[Filter2]` 有 12 条真实规则，包含 `#Hidden`、扩展名、`#DIR`、`#BG` 和 `#BG-A`；规则字段数值列的完整语义及含颜色规则的 `.qdr` 正向样本仍待确认，当前导入器尚未应用这些规则，不能假称全字段兼容。

2026-10-25 Private Desktop 隔离颜色复验：`IShellView` 的 `SysListView32` 逐项颜色现于 Icon、Details、SmallIcon、List、Tile 五种同步模式中通过真实目标色像素检查，Name 降序及 Size 分组时五种模式也通过；Icon 的选中项目保留 Shell 默认颜色，未选中项目仍按规则着色。DefView 回调的 `CDIS_SELECTED` 在本机被错误地置于所有行，故探针在绘制前快照 ListView 的 `LVIS_SELECTED`，不能直接沿用回调标志。此为私有 Desktop 上的隔离默认 `IShellView` 证据，不证明 ExplorerBrowser DirectUI、Content 模式、其它 Shell provider 或产品宿主可迁移；文件区仍使用 ExplorerBrowser。详见[原型记录](ishellview-color-spike.md#2026-10-25-defview-选择状态标志与同步视图颜色复验)。

2026-10-25 颜色原型组件化：`ShellListSelectionSnapshot` 与 `ShellViewColorPolicy` 从原生探针中拆出；`--shell-view-color-policy-smoke` 无窗口验证 LVIS 选中位、DefView 伪选中回调位、规则色和 fallback。组件抽取后隐藏探针与 Private Desktop 五模式/排序分组/选中像素回归均通过。最小 `StreamViewHost` 仍未实现通用 BrowseObject、控制窗、状态文字和框架菜单；颜色宿主后来已接入完整 CDB/CDB2/CDB3、逐槽 CCW smoke 及 `SID_SShellBrowser` 的 `QueryService` 路由 smoke。产品仍依赖 ExplorerBrowser 导航事件、TravelLog、快速筛选、watcher、选区恢复和 WPF 每标签/窗格生命周期。未接入产品视图；详见[原型与宿主差异](ishellview-color-spike.md#2026-10-25-独立视图宿主与-explorerbrowser-契约差异)。

当前生产文件区继续使用 ExplorerBrowser；`IFolderView2` 提供查看、排序、分组等能力，但不提供逐项颜色方法。隔离默认 `IShellView` 的 `SysListView32` 在部分模式曾采到逐项颜色像素，但后续八种模式屏幕内采样未命中颜色像素，因此现有证据仍不足以确认全模式着色。颜色宿主目前已实现 CDB/CDB2/CDB3，并以真实托管 COM vtable smoke 覆盖回调槽；这不证明 DefView 实际调用顺序、完整筛选语义或产品通知行为。产品仍使用 ExplorerBrowser DirectUI，隔离原型尚未覆盖 ExplorerBrowser 导航、状态、TravelLog、每标签生命周期及完整刷新/焦点/拖放操作；这不支持直接迁移生产宿主。后续若继续原型，应先处理这些通用宿主差距并在独立 Windows 会话完成验证；逐项着色不能按已完成处理，也不等同于已报告的导入后黑底问题。详见[隔离原型记录](ishellview-color-spike.md)。

2026-10-24 右键来源状态保护：右键按下时若系统光标坐标读取失败，现清除上一条命中结果，避免同位置的后续菜单复用旧目标；无坐标状态机回归先失败后通过。UI Automation 实际点击分类、空白区域右键与菜单插入仍待可见 GUI 验收。

2026-10-24 10,000 项隐藏压力补充：曾有一轮最终选择/焦点检查失败；后续一轮逐次记录三次 fallback，每次均捕获原选择与焦点，刷新中短暂读到 0 项后约 253–275 ms 恢复。另两轮无 fallback 的 10k 测试均保留选择/焦点。异常仍未稳定复现，自动刷新 P0 不关闭；未改产品策略、未做可见 GUI 验收。Shell stale 项身份查询在 10,003 项中耗时 12 ms。详见[自动刷新调查](live-update-investigation.md#2026-10-24-十千项压力与恢复边界)。

2026-10-24 单文件迟到兜底调整：Watcher 首次计数检查已等待既有 750 ms 静默期；单项差仍不一致时现在直接刷新，不再叠加 500 ms 等待。Debug 隐藏生产监视器复制样本分别于 870 ms 经 fallback 显示和 543 ms 自然显示；Release 样本于 134 ms 自然显示。完整监视器的选区/焦点与跨窗格操作检查通过。2–31 项仍保留一次额外计数检查，32 项以上继续长枚举窗口。慢速/网络盘与可见 GUI 未验证；自动刷新 P0 保持开放。详见[调查与时序](live-update-investigation.md#2026-10-24-单项计数不一致的兜底延迟调整)。
2026-10-24 选区基线复核更正：上方旧调查中的自然删除清选结论来自隐藏探针状态失配（探针用 Shell COM 改选区，却没有同步产品输入代数和已保存快照）。修正基线后，native-only `IFileOperation`、生产 watcher 和显式目录更新样本均保留未受影响的选区与焦点。旧结果不再列为已确认缺陷，也不据此增加恢复逻辑；详情见[基线失配调查](live-update-investigation.md#2026-10-24-更正自然删除选区丢失是探针基线失配)。可见 Explorer 对照及自动刷新长尾仍待验收。
Ctrl+C/V 源码路由复核发现原恢复只有 ContextIdle 与 250 ms 两次尝试；现增加受浏览器/路径/导航代际、焦点意图和用户输入代数门控的 1.5 秒有界监视与恢复。完整 SettingsSmoke、App/HostProbe Release 构建通过。没有使用剪贴板或真实 Paste，因此用户报告是否已修复仍须隔离可见窗口复测。详见[焦点调查](live-update-investigation.md#2026-10-24-ctrlcv-焦点恢复源码复核)。

2026-10-24 自动刷新跟进：本地固定磁盘目录的文件创建监听现对 60 ms 静默期内的创建事件合并，在 ExplorerBrowser 所属 STA 检查 Shell 计数落后后发出一次精确 `SHCNE_CREATE`；计数兜底保留。12 对 stale-view 控制样本中，精确通知 9/12 于 93–200 ms 显示，无额外通知 0/12 于 250 ms 内显示。合并实现三轮 3,000 项创建/删除压力测试均最终恢复正确计数并保留选区/焦点，一轮在操作结束后由 fallback 补齐；Dispatcher 最大间隔 62–80 ms。追加一轮产品压力运行无 fallback，删除阶段 312 ms 从 861 项收敛至 3 项，焦点/选区保留，Dispatcher 最大间隔 65 ms。另有单文件复制轮仍约 1.91 秒才由 fallback 更新。最新隐藏探针确认文件夹创建/删除由 fallback 恢复；跨 `D:`/`C:` 的 Shell 复制/移动/删除三轮均 watcher 与计数最终一致，首轮无 fallback、后两轮有源/目标窗格 fallback。真实可见窗口、慢速/网络复制仍待验收。未发布。见[通知与压力记录](live-update-investigation.md#2026-10-24-精确-create-通知控制组与批量合并接入)及[文件夹/跨卷补验](live-update-investigation.md#2026-10-24-文件夹与跨卷-shell-操作补验)。
2026-10-24 文件夹精确通知 A/B：新隐藏探针对比 `SHCNE_MKDIR` 与不发送通知，三组都先确认视图仍旧；MKDIR 组 1/3 于 250 ms 内更新、控制组 0/3，两组各两次 fallback，暂无稳定收益，未接入产品代码。文件夹创建/删除继续依赖现有最终收敛路径。详见[MKDIR A/B 记录](live-update-investigation.md#2026-10-24-文件夹-mkdir-通知-ab)。
2026-10-24 文件夹删除精确通知 A/B：`SHCNE_RMDIR` 组 4/5 于 125–141 ms 更新，控制组 0/5 于 250 ms 内更新；RMDIR 仍有一轮走 fallback。由于通用 `FileSystemWatcher.Deleted` 事件不提供目标类型，未把 RMDIR 接入产品路径，避免把文件误报为目录。细节见[调查记录](live-update-investigation.md#2026-10-24-文件夹-mkdir-通知-ab)。
2026-10-24 删除项类型探测：Shell 列表在删除后短暂保留的 PIDL 可通过路径匹配与 `SFGAO_FOLDER` 分辨文件/目录，HostProbe 的文件、空目录样本均识别正确。该查找逐项扫描，尚未测大目录成本，也没有产品接线；目标消失或属性不可读时仍需 fallback。

2026-10-23 自动刷新继续保持 P0：无界面 Shell 文件操作探针新增磁盘结果、独立通知及实际文件身份的分层观察，已有“复制/删除成功但部分视图在 20 秒内仍不一致”的反例；同一对照也能通过，不能据单次结果关闭问题。未改产品刷新逻辑、未做 GUI 验收。见[最新证据](live-update-investigation.md#2026-10-23-无界面-shell-文件操作对照)。

2026-10-22 用户复现：多窗格 F2 重命名时按方向键会退出编辑，Shift 只使后续文件选择变为多选；单窗格暂未观察到。源码发现编辑框分支返回 HwndHost 默认 false，WPF 会继续方向焦点导航。候选修正仅将 WM_KEYDOWN 的四个方向键直接交给当前本窗格的原生 Edit 并报告已处理，避免跨窗格；保持 Ctrl/Shift 文本编辑行为。真实多窗格复测尚未完成，不能据构建判为修复验收通过。

2026-10-24 源码路径复核：`MainWindow.Window_PreviewKeyDown` 在 Shell 重命名 Edit 获焦时跳过应用级快捷键；`ExplorerBrowserHost.FocusView()` 在原生 Edit 仍获焦时不把焦点移回文件项，窗口激活后的恢复路径也调用该保护。由此暂未发现应用快捷键或焦点恢复逻辑必然结束重命名的第二条源码路径。该静态检查不验证 Shell Edit 实际窗口类、真实键盘消息是否经过 `TranslateAcceleratorCore`，也不覆盖四窗格的方向键、Shift/Ctrl 组合；桌面验收仍开放。

2026-10-24 隐藏生产宿主补测：在真实 Shell 视图 HWND 下挂接隐藏原生 Edit 子控件，调用生产 `TranslateAcceleratorCore` 并向该 Edit 定向发送四个方向键。四次均由宿主返回已处理，Edit 焦点与文本保持；没有使用 SendInput、显示窗口或剪贴板。此探针验证“当前焦点确为 Edit 时”的分派分支，但没有让 Shell 启动 F2 内联编辑，也没有触发 WPF 的真实键盘焦点遍历，不能替代原始多窗格复现的可见 GUI 验收。

2026-10-24 F2 光标动作补验：四窗格隐藏探针先通过生产 `TranslateAcceleratorCore` 路由 F2，再确认 Shell 建立重命名 Edit；将光标放到文件名中间后，经同一生产路由发送 Left/Right，文本光标实际移动，四个方向键都保持编辑态与焦点。四个 pane Release 运行通过。探针未模拟物理键盘或可见窗口的 WPF 焦点链，GUI 验收仍开放。

随后用隔离 HostProbe 直接向真实隐藏 `SHELLDLL_DefView` 发送 F2，先选中测试文件。隐藏顶层窗口线程的 `GetFocus()` 为 0，Shell 没有创建 Edit 子窗口；探针按约定以 exit 3 报告“无法建立重命名编辑态”，不是产品测试通过或失败。要让 Shell 创建真实内联编辑器，必须进入可见窗口焦点链；本轮不显示窗口、不模拟全局输入，保留 GUI 验收待办。

搜索生产宿主补验：隐藏四窗格八阶段覆盖查询、历史、返回目录、连续导航、发布事件与缓存配置；修正旧搜索 Pending 取消新目录请求及 ERROR_BUSY 处理。源码集成验证通过，真实窗口与焦点仍待验收，尚未发布。见[记录](search-folder-investigation.md#生产-wpf-宿主集成与快速导航)。

配置补验已使用四窗格独立的模式、尺寸、排序、分组和列宽；返回目录后原生捕获与原快照一致，且捕获失败标记必须为 false。最新隔离 GUI 副本已准备但未运行，详见同一记录。

内嵌搜索新增源码修正及原生验证：拒绝非文件夹协议对象，保留查询身份；两次搜索和原生历史返回结果通过，应用窗口验收待完成。见[记录](search-folder-investigation.md)。

后台自动更新探针与证据边界见[调查记录](live-update-investigation.md)：生产宿主存在间歇失败，最小原生对照可通过。当前没有新的 GUI 验收或修复版发布。

**13:07 补验反例：temp.3 精确发布包右下窗格粘贴后仍不自动更新。自动刷新尚未解决，下面“修正”描述仅指候选代码及局部测试。**

2026-10-21 实际桌面补验：[测试记录](gui-acceptance-2026-10-21.md)。修正 temp.2 原生文件视图不自动更新，发布 temp.3。源码候选粘贴/删除后立即方向键及列排序通过；当前目录搜索修正前后均失败，继续跟进。下方较早的桌面受阻记录保留为历史，不代表本次未测试。

更新：2026-10-20。以用户实际工作流为验收单位。官网说明、源码接入、无窗口回归、真实窗口通过是四种不同证据。[验收记录](gui-acceptance-2026-10-18.md)保留此前观察；2026-10-19 已发布独立配置的临时版 `preview-0.6.0-temp.1`，完整 GUI 验收未完成。此前桌面测试被用户 Esc 中断；2026-10-20 晚已获重新授权至次日 10:00（Asia/Shanghai），但另一个任务正在使用桌面，须先确认独占交接；本轮仅做后台验证。

## 基准

2026-10-21 原生导航状态补查：已实收并验证 `CommandStateChange` 的前进/后退状态，
包括取消、新分支和同路径重复历史。同日已接入 App 每标签订阅、按钮与快捷键动作门控，
生产订阅器的 11 阶段隐藏检查及生命周期回归通过。真实按钮焦点、Tab 和四窗格仍待验收。
见[实验矩阵](native-navigation-probe.md#2026-10-21-命令状态通知)。

- [Q-Dir 官方功能](https://www.softwareok.com/?seite=Freeware/Q-Dir)：多窗格/标签、剪贴板、拖放、查看方式、列设置、树和预览等。沿用其紧凑多窗格操作习惯。
- [微软 Explorer 快捷键](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows) 与 [Explorer 使用说明](https://support.microsoft.com/en-us/windows/experience/fileexplorer/file-explorer-in-windows)：作为文件操作及键盘行为基准。
- [IFolderView2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2)：宿主通过 Shell 接口操作视图；公开接口不等于整套 Explorer 窗口界面。
- 保留用户已指定的差异：Backspace 上级目录；每窗格至少一个标签；布局只放顶部；INI 绿色配置；源码和发布版隔离。

## 差距与交付条件

布局交互补齐（2026-10-23）：源码为八种预设加入专用分隔条，三窗格分隔条避开跨行/列窗格；
每种布局独立记忆分隔比例，存入 `LayoutProportions.ini`，顶部布局菜单可恢复当前布局均分；四宫格上下两排左右比例分别记忆。
仅在拖动结束或主动重置时写入，普通窗口缩放不改写比例；保留四个窗格对象和原有 Tab 顺序。
无窗口几何、取消/过期回调及配置保护检查通过。真实拖动、原生视图实例、历史、焦点、DPI 和重启显示仍待验收，尚未发布。
`.qdr` SP0/SP1/SP2 比例字段已接入；四宫格两排比例分别映射。Q-Dir GUI 的拖动和读回尚未验收，不能仅凭配置往返宣称完整互操作。

2026-10-21 `.qdr` 视图兼容修正：按本机 Q-Dir 12.79 读写链路校验 CODEMODE1 第三字段。
原先仅检查整数格式；现拒绝校验不匹配的视图快照，保留目录及显式模式并提示。
68 个本地字段匹配，错误校验和边界向量回归通过。同日已接入完整列快照的原生视图输出，
Windows 属性直读及无扩展读回通过；Q-Dir GUI 互操作仍未完成。无列快照的标签仍通过扩展保存。
随后通过隐藏原生 IShellView 直接读取导出流，发现并修正 LogicalViewMode 缺失导致的
图标大小恢复错误；16 个预设/分组场景通过，Q-Dir 可见程序与再保存仍未验收。

2026-10-21 默认查看配置：新标签提供八种预设，以模式和图标大小共同保存；
旧的系统默认尺寸及自定义值保持原值。会话 v3 与 `.qdr` 扩展往返、旧格式升级备份、
旧预览版拒绝覆盖 v3 已通过无窗口验证。当前标签不随默认值更改；真实菜单、新建及重启显示待验收。

查看方式的原生补查：实际生产列管理器设置自定义列顺序/宽度后，
八种模式在有/无分组下均准确读回，包括非详细信息模式及返回详细信息。
原生接口切换未丢列设置；Ctrl+滚轮消息路径、真实列拖动及重启仍未验收。

新建文件夹入口补齐上下文保护：Shell 菜单准备后重新检查原请求、活动窗格及导航版本，
失效时不发出创建命令；后续重命名还检查观察器身份、选区版本和焦点意图。
菜单、工具栏与快捷键共用入口。真实菜单准备期间切窗格、创建后切应用/地址栏、
连续新建以及同目录离开后返回，均需隔离 GUI 验收；构建和既有监听检查不能替代这些场景。

导航状态补充：旧回调与同路径重访的排队动作已加版本保护；重复/未知目标批次
核对实际视图位置，无法读取时保留缓存并报告失败。构建、状态回归和隐藏原生
位置读取通过，真实窗口仍待验收，详见[导航发布规则](navigation-publication.md)。
历史服务补查也覆盖当前视图、视图站点及各自顶层浏览器提供方，均返回 E_NOTIMPL；
真实前后退 PIDL 检查通过。随后通过 CommandStateChange 接入按钮可用状态，
不将查询失败解释为没有历史；未收到通知的方向保留未知状态。

2026-10-24 自动更新补验：文件创建继续用 60 ms 合并的 `SHCNE_CREATE`，仅在视图计数落后时发送。删除事件路径按 250 ms 合并；只对最新路径查询 stale Shell item，确认为文件夹时发送 `SHCNE_RMDIR`，文件返回 `NotFolder` 不发送 `SHCNE_DELETE`。fallback 保留：隔离文件夹删除连续 3 轮于 359–373 ms 自然收敛，但完整生产监视器仍出现一次 folder-delete fallback。10k 压力中生产只做一次身份查询，128 文件 burst 的诊断查询一次耗时 3 ms，Dispatcher 最大间隔 82 ms；该轮选区断言失败后来确认是探针基线失配，已由[调查更正](live-update-investigation.md#2026-10-24-更正自然删除选区丢失是探针基线失配)撤回。

| 操作 | 当前源码事实 | 缺口 / 必须通过的验收 | 优先级 |
| --- | --- | --- | --- |
| 显示隐藏项目 | “视图 > 隐藏项目”提供跟随 Windows（默认）、显示、隐藏三态；显式偏好保存在 profile `HiddenItems.ini`，应用于所有窗格/标签。使用原生 `IFolderFilterSite`/`SHCONTF_INCLUDEHIDDEN`，不请求 `SHCONTF_INCLUDESUPERHIDDEN`，也不修改 Windows 用户设置；搜索视图使用空快速筛选规则，避免旧筛选模式泄漏到搜索结果。仅刷新已创建的 ExplorerBrowser view；延迟创建的标签保留新偏好并在 view 初始化时应用。跟随模式响应任意已到达的 `WM_SETTINGCHANGE`，重读 Shell 状态并仅在值变化时刷新已创建视图；Windows 实际发出该通知仍待验证 | 配置/枚举/筛选/延迟 view 和通知路由 smoke、App Release build 与隔离发布 profile smoke 通过。普通隐藏项目实际显示/隐藏、受保护系统项不显示、快速筛选组合、ExplorerBrowser 刷新，以及 Windows 外部切换后是否广播通知仍需 GUI 验收 | P2 进行中 |
| 复制、剪切、粘贴、删除、撤销、重命名、属性 | Shell 处理；Shell 未接手时 Ctrl+A/C/V/X/Z 与 Ctrl+Insert/Shift+Insert 有原生命令兜底；Ctrl+D 未被应用截获并交给 `IShellView.TranslateAccelerator`；两组复制/粘贴快捷键均请求活动视图焦点恢复。隔离四窗格 NativeProbe 经各自 `IShellView.TranslateAccelerator` 合成 Ctrl+A，逐 pane 全选且确认兄弟 pane 选择为零，未动系统剪贴板。另连续 3 次对真实 `IFileOperation::CopyItem` 和 `MoveItem` 启用 `FOF_RENAMEONCOLLISION`：同名目标原文件均保留，复制源保留，移动源移除，两种新副本均写入 Shell 生成的非冲突名称；探针仅用临时文件并禁用确认/错误 UI | 该探针证明的是合成 Ctrl+A 的单次 Shell accelerator 路径与显式重命名冲突策略，不证明完整消息循环/物理键盘，也不证明产品对话框如何呈现/取消冲突；未覆盖剪贴板、删除到回收站/永久删除、Undo/Redo 或用户恢复流程；单选/多选、粘贴后立即方向键及 Insert 组合真实行为待 GUI 验收；不得误改非测试文件 | P0 |
| 网络驱动器映射与断开 | “工具 → 网络驱动器”调用 Windows `WNetConnectionDialog` / `WNetDisconnectDialog` 系统对话框，使用本窗口作为 owner；用户取消不显示错误，普通错误显示系统消息，`ERROR_EXTENDED_ERROR` 通过同线程 `WNetGetLastError` 显示网络提供程序名称、错误码与说明。只有映射/断开成功才向 `CSIDL_DRIVES` / `FOLDERID_ComputerFolder` 发送 `SHCNE_UPDATEDIR` 通知；smoke 覆盖分发、owner HWND、取消/成功通知门控、原生结果码及两种错误信息路径，不建立或断开实际连接 | 菜单可见性、映射/断开后 ExplorerBrowser 是否按通知刷新、凭据交互与真实失败提示待可见窗口验收；断开对话框由 Windows 网络 API 异步显示，窗口生命周期行为尚未实测。对标：[Q-Dir FAQ 125](https://www.softwareok.com/?faq=125&seite=faq-Q-DIR)、[WNetConnectionDialog](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetconnectiondialog)、[WNetDisconnectDialog](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetdisconnectdialog)、[WNetGetLastError](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetlasterrora)、[SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify) | P2 |
| Q-Dir Paste Special（剪贴板内容存为文件） | 编辑菜单加入 Paste Special 子菜单和 Ctrl+Shift+V；仅提供当前剪贴板存在的图片 PNG/BMP/JPG 或文本 UTF-8/UTF-16/ANSI 类型。普通 Ctrl+V 继续走 Shell 原生文件粘贴 | 时间戳文件名使用 `CreateNew` 原子冲突编号、不覆盖；ANSI 按 Windows 当前代码页编码并拒绝有损转换；仅接受当前可写文件系统目录。创建后发送 Shell PIDL 创建通知；浏览器、目录、导航/视图/选区/输入代际都有效时才短暂重试选中新文件，并防止 Shell COM 重入覆盖更新请求。离线图片/文本/命名/请求代际 smoke、快捷键回归、完整 SettingsSmoke 与 App Release build 通过；真实剪贴板提供程序、菜单、实时视图更新/选择/焦点仍待隔离 GUI 验收 | P2 |
| 文件区焦点与键盘遍历 | 已有内容 HWND/文件项恢复、Tab 与 F6 路由；焦点恢复区分文件内容 HWND 与 ExplorerBrowser/Shell 子控件；列标题焦点会触发回到文件内容窗格；焦点恢复等待期间，当前文件内容区收到的输入更新请求代数并继续恢复，其他 Shell 控件或失活窗格输入仍取消请求；窗格导航栏“查看方式”按钮现作为目录树按钮之后、地址控件之前的独立 Tab 停靠点；隐藏目录树不再占用停靠点索引，避免与列标题/文件区焦点编号重合；SettingsSmoke 覆盖正反遍历及焦点门控；隔离默认 `IShellView` 的四窗格 Details/List/Icon 合成 accelerator 探针读回目标项焦点 `0→1` 且兄弟 pane 不变 | 导航映射回归现在覆盖 256 种可选控件组合和每个停靠点的 Tab/Shift+Tab 逆变换。仍需在真实 Shell 中验证列标题转文件区、窗格查看方式按钮的实际键盘遍历、文件操作/确认框后的方向键、四窗格正反 Tab、布局切换与 Alt+Tab；隔离合成 accelerator 不等同物理键盘或应用 message loop；外部窗口焦点不得被抢占 | P0 |
| 文件/文件夹/背景右键 | 文件项和背景菜单由 Shell 原生生成；本程序不向文件区菜单插入自定义命令，也不重做查看/排序/分组/刷新/新建。四窗格隔离探针分别从每个 view 获取背景菜单并执行查看/排序/分组 canonical verb；也逐 pane 从 `SVGIO_SELECTION` 获取文件项 `IContextMenu` 并读到 `copy`，同时确认选区不泄漏到其他 pane。产品 ExplorerBrowser 当前是 DirectUI，没有 SysListView32；若未来重新评估文件区自定义菜单，需先证明命中测试能区分图标/文本与整行空白，并单独重新确定交互范围；本项目当前菜单以原生 Shell 行为为准。 | 已验证隔离 Shell view 的标准背景/文件项菜单来源与命令；产品 ExplorerBrowser 的行尾空白右键、键盘菜单和第三方扩展仍需可见验收；隔离探针没有显示产品 UI 菜单，故不能替代产品可见验收 | P0 |
| 跨窗格与 Explorer 互操作 | Release 隔离 NativeProbe 创建两个 Shell view。目标 `IShellView.GetItemObject(SVGIO_BACKGROUND, IID_IDropTarget)` 返回 `E_NOINTERFACE`；目标文件夹注册的 `IDropTarget` 接收从 `IShellView.GetItemObject(SVGIO_SELECTION)` 取得的真实选中项 `IDataObject` 后，直接调用 `DragEnter/DragOver/Drop` 成功 Copy，验证目标原名/内容并保留源文件。独立 `SHCreateDataObject` 路径也成功 Copy；本探针只允许 Copy，未测试 Move。最近 Private Desktop 复跑：选中项数据对象 Copy、注册文件夹目标 Copy 通过，前台未变化；直接查询 view drop target 不支持。 | 直接 COM 调用不等于真实 OLE `DoDragDrop`，也不验证跨窗格鼠标拖放、Ctrl/Shift/右键效果、源端效果交接、焦点或产品 WPF 宿主；以上仍需集中可见验收，不能标记跨窗格拖放完成 | P0 |
| 每标签导航与历史 | Shell TravelLog；保留隐藏宿主；每实例 CommandStateChange 驱动当前标签按钮，未知保留尝试、pending 门控；[隐藏原生探测](native-navigation-probe.md)验证 11 阶段实际 PIDL 与生产状态 | App 切标签/布局不丢历史；按钮禁用后的焦点与 Tab；连续后退/前进、失效路径/网络路径；历史无需跨重启持久化 | P0 |
| 标签与会话 | 新建、复制、关闭/恢复、固定、排序；Shift+Enter 与 Ctrl+Shift+T 在文件视图单选文件夹时可在新标签打开；INI 保存活动窗格/标签 | 隐藏生产路径已核对恢复/复制的模式、图标尺寸、排序、分组与显示列，以及新标签默认视图；重启恢复活动标签/焦点和可见验收仍待完成。Q-Dir 官方历史还记载在目录树/文件列表中键击中键可在新标签打开目录。目录树通过 Shell 点击回调使用精确 `IShellItem`；文件视图现在用 UI Automation 坐标命中条目、读取其标签名，再在当前文件系统目录下解析并由 Shell 复核文件名、父目录和普通文件夹属性。没有命中条目、搜索/虚拟位置、路径校验失败、导航变化或后续输入时均不打开；新标签继续使用默认视图。SettingsSmoke 与 App Release 构建通过；UI Automation 命中、实际中键输入和可见新标签行为仍待 GUI 验收。见[Q-Dir History](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2FHistory)、[UI Automation ListItem 名称规则](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportlistitemcontroltype)。 | P0 |
| 标签组 | 当前每个窗格维护自己的标签集合；未发现跨标签组的状态模型或命令。Q-Dir 官方菜单字符串明确列出“Tab Group”“Move to new Tab Group”“Copy to new Tab Group”“Use Tab Groups” | 官方 FAQ 101 的公开提问区中，用户直接询问如何显示已添加/移动到新组的标签；该页面只回答标签栏显隐，未说明组的作用域、命名/切换方式、组内标签是否跨窗格，以及组和布局/会话如何保存。这确认不能从菜单字符串可靠推断行为；需在集中 Q-Dir 验收批次观察交互后再定标签/工作区模型与 INI 迁移，确认前不添加推测性菜单或持久化。来源：[Q-Dir 官方功能页](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish)、[Q-Dir FAQ 101](https://softwareok.eu/?faq=101&seite=faq-Q-DIR) | P2 待评估 |
| 查看方式 | 原生菜单、顶部/状态栏菜单和 Ctrl+Shift+1～8 已接入；Ctrl+滚轮在 Shell 文件视图 HWND 与窗格 WPF 控件中共用每个 ExplorerBrowserHost 的代际隔离累加器，整刻度后循环八种方式，成功切换才消费手势；普通滚轮仍由原控件处理。小图标默认尺寸为 16。隐藏视图回归覆盖八种预设、选区身份、排序/分组及滚轮循环，见[调查](view-style-wheel-investigation.md)。 | 真实鼠标输入路由、跨控件半刻度、不同控件上的滚轮焦点与可见绘制；四窗格目标和菜单退出焦点；Ctrl+1～9 不冲突；默认新标签设置不被修改；普通滚轮滚动。Q-Dir“所有查看方式显示列标题”已接入源码，宿主/GUI 行为待验收。默认 `IShellView` 对象的 `SetFolderViewOptions(FVO_CUSTOMPOSITION)` 返回 `E_NOTIMPL`；但未初始化的 `CLSID_ExplorerBrowser` 对象支持读取、设置并恢复该选项。隐藏 ExplorerBrowser 探针已在初始化前读写并恢复 `FVO_CUSTOMPOSITION`，两次 `OnViewCreated` 均成功清除列标题抑制位；八种查看预设在切换后 250–1000 ms 均读到可见 `SysHeader32`，列顺序/宽度、排序、分组及选区身份保持。该探针复用生产 flag helper，但未运行产品 WPF 窗口；物理输入、立即切换时的短暂隐藏、可见绘制与焦点仍待桌面验收 | P1 |
| 鼠标滚轮焦点与查看方式 | 顶部“视图 → 鼠标悬停滚轮”默认关闭；开启后由仅限本 UI 线程的 `WH_GETMESSAGE` 钩子把普通竖向滚轮路由到消息坐标下可见 Shell 文件视图或目录树，目标窗格成为逻辑活动窗格但不抢键盘焦点。钩子只处理 `PM_REMOVE`，Ctrl+滚轮仍按原路径循环八种查看方式；参考 [Q-Dir FAQ 75](https://www.softwareok.com/?faq=75&seite=faq-Q-DIR) | router、带符号坐标、PM_REMOVE 策略和 portable INI 默认/往返 smoke、完整 Release SettingsSmoke、App Release build 通过；尚未进行真实鼠标与多窗格 GUI 验收，菜单可用性、滚轮方向/精度及焦点保持仍待桌面验证 | P1 |
| 菜单栏与工具栏命令覆盖 | 编辑菜单保留九种文件命令，主工具栏仅保留属性和新建文件夹按钮；两者读取 native canonical verb 与启用状态；撤销/重做与粘贴查询当前视图背景命令；工具栏正悬停或聚焦时，活动窗格选区、导航完成/失败和标签切换通过 150 ms 合并查询更新状态；生产 `QueryFileCommands` 空选区/单选/窗格隔离探针及刷新门控 smoke 通过。源码确认每次刷新都会重新取得当前 Shell 视图及 `IContextMenu`，不复用旧菜单对象。Private Desktop 只读校准发现旧 `IContextMenu` 对象报告 Undo/Redo `False/False`，随后两次新取得的背景菜单均为 `True/True`；无文件操作、父窗口隐藏、前台未变。此差异属于菜单对象状态查询时效，不能据此判定 Undo/Redo 实际操作可用或产品缓存错误。Shell 查询重入保护可在当前上下文失效时提前终止整组快照。新建文件夹复用原入口并初始化原生 New 延迟子菜单；刷新当前目录工具栏按钮调用活动窗格现有 Shell 刷新入口，F5 与 Ctrl+R 均映射到同一动作。NewFolder 入口先聚焦当前 Shell 视图并复核请求、导航和用户输入，再执行命令；创建后只等首次恢复刷新一轮，继续尝试选中新目录并重命名；`SelectItem(..., SVSI_EDIT)` 被接受后等待原生编辑框焦点确认，不重复发送。完整离屏入口实验曾改变前台，不记为通过，见[路线图](roadmap.md#2026-10-24新建文件夹刷新后的自动重命名续查) | 真实活动窗格的 `CreateNewFolder()` 焦点、Refresh 后自动重命名、物理用户输入中断和可见 UI 尚未验证；工具栏真实启用状态、实际文件命令、撤销/重做/回收站、刷新目标仍待 GUI 验收。真实 Undo/Redo 调用需在一次性 Windows 用户会话中验证：Shell undo 栈按用户会话共享，不能在当前会话调用以免撤销其他进程的用户操作 | P0 进行中 |
| 排序、分组、列设置 | 原生菜单；INI 已保存模式、图标大小、排序/分组；每标签保存显示列、列宽和顺序；顶部“视图 → 命名查看预设”可保存、应用、覆盖和删除具名模式/列布局；Ctrl+右键原生列标题可直接保存/应用预设。应用保留当前标签排序/分组，并通过 profile `ViewPresets.ini` 独立共享到各窗格 | SettingsSmoke 覆盖字段往返、列顺序、排序/分组合并保留、覆盖/删除、非法列配置、未来格式拒写和并行修改冲突；快捷键策略与一次性消息门控 smoke 覆盖 Ctrl+右键 `SysHeader32` 的消息配对、键盘菜单和取消状态。App Release 构建通过。Q-Dir FAQ 137 只明确查看方式与列配置，不明确排序/分组是否属于预设，本项目按已公开范围实施。实际列头命中、弹出菜单位置/焦点、原生 Shell 菜单保留、真实应用列、复制标签和重启恢复仍待桌面验收 | P1 |
| 全选、取消选择、反向选择 | 编辑菜单接入当前 Shell 视图；反向选择支持 Ctrl+Shift+A；PIDL 快照、原生批量选择、保留焦点项选中状态；[隐藏原生回归](selection-commands.md)通过。2026-10-28 当前源码的隐藏生产 ExplorerBrowserHost 四窗格 × 8 种查看方式共 32 组通过：反选为精确身份补集，二次反选恢复原选择，焦点、兄弟窗格选择及各窗格查看方式均保持；原始输出在 `src/.state/shell-selection-invert-hostprobe-current.log`。 | 可见菜单退出、快捷键焦点、多窗格、网络/大目录响应；不将选区保持等同于键盘焦点验收 | P1 进行中 |
| 地址与位置 | 普通文件系统路径、面包屑、搜索虚拟位置；Q-Dir 明文/编码 PIDL 与应用会话接受任意由当前 Shell 解析且确认为文件夹的绝对名称，不限固定别名或 ZIP。文件系统位置保持路径身份；虚拟位置持久化为 Shell 绝对解析名称；缺失提供程序或非文件夹只影响该标签。已知别名在地址/标签以友好名称显示 | SettingsSmoke 覆盖内置别名、Windows Network Shell 文件夹 PIDL、Q-Dir 文本/编码导入、`shell:::{GUID}` 基础字段导出并在无本程序扩展时重新导入、身份往返、非文件夹拒绝、ZIP 根及内部文件夹分类。未启动 ExplorerBrowser 或 UI；第三方扩展缺失/变化后的可见恢复与 ZIP 在不同 Windows 配置上的行为仍待验收 | P1 进行中 |
| 地址栏样式与历史 | 已提供 Modern 面包屑/Classic 完整路径全局偏好，保存在 `AddressBar.ini`；Modern F4 打开当前路径所在的 Shell 文件夹树，再次 F4 进入路径编辑；Classic F4 直接进入编辑；右侧独立按钮以及后退/前进箭头右键打开当前标签最近访问路径菜单，最多 20 条、运行时维护；右键菜单定位到被点的导航箭头，左键后退/前进命令不变；地址输入支持环境变量、`/` 分隔符、相对路径、`..`，单独盘符（如 `C:`）指向盘符根目录，空输入刷新当前视图；虚拟 Shell 位置保留 Shell 名称，不拼接文件系统基路径；位置下拉按祖先层级展开，“此电脑”子菜单动态列出当前逻辑驱动器，就绪本地固定盘显示卷标，网络盘不查询远程卷标 | 两种样式的可见排版、箭头右键菜单的弹出位置/禁用箭头可用性、地址树 Popup 焦点与关闭、虚拟文件夹导航、历史菜单键盘操作待 GUI 验收；路径输入框继续保留原生右键菜单；驱动器菜单更新时机及断开映射盘的可见行为待 GUI 验收 | P2 |
| 目录树、预览、详情窗格 | Q-Dir 支持每个文件视图的独立目录树和预览；本项目树已接入源码：独立宿主、每窗格切换及 v10 会话持久化，支持共享树/逐窗格树/关闭，活动窗格切换与布局切换均同步路径，逐窗格模式的可见值保留；每个逐窗格树和共享树都有独立拖动宽度，配置写入 `NavigationTreeWidths.ini`。Explorer Dirs `.qdr` 扩展 v4 保存共享树及四窗格宽度；Q-Dir `TreeNsplit` 仍不映射。Ctrl+Shift+E 接入当前窗格树同步以展开/定位当前目录。新增 Q-Dir 风格“从文件列表选择并展开”偏好，默认启用并单独保存在 `NavigationTreeSelection.ini`：当前可见窗格单选一个 Shell 文件夹后，只在对应逐窗格树或活动窗格共享树定位并展开，不改变文件视图目录或焦点。目录树图标通过 `IShellItem` 绝对 PIDL 与 `SHGetFileInfo(SHGFI_PIDL)` 读取系统图像列表索引，并显式绑定同一系统图像列表；生产回调 smoke 验证普通目录 `3/3`、“此电脑”`15/15`、回收站 `32/32`，索引均位于 512 项系统图像列表中。隐藏直接树绘制 A/B 确认普通文件夹图标像素与系统模板一致，但没有覆盖 WPF `HwndHost` 的最终绘制。`.qdr` `tree_mode` 按静态映射读写基础字段（0=关闭、1=共享、2=逐窗格）；`Tree1`～`Tree4` 暂按 0/1 语义映射，尚待可见 Q-Dir 验收。预览与详情可由“视图”菜单、工具栏、Alt+P/Alt+Shift+P 切换，逐标签保存，两个原生 Shell 帧互斥。隐藏宿主序列确认帧状态切换且文件视图 HWND、目录、计数和选区保持。 | WPF `HwndHost` 中目录树普通/展开及虚拟 Shell 节点图标的实际绘制仍待使用当前预览包可见验收；若仍空白，再检查宿主裁剪、尺寸与像素来源。目录树点击/键盘、树宽拖动手感、窄窗格空间分配、共享树尺寸保存与真实焦点、多布局可见及重启恢复、Q-Dir 可见互操作待验收；新联动的真实 Shell 选区触发、目录树展开行为和可见多窗格体验尚未做 GUI 验收。预览/详情实际渲染、窗格尺寸、焦点/交互及可见应用体验待 GUI 验收 | P1 |
| 预览格式过滤 | Q-Dir FAQ 34 将通配符设置列在 General 的预览选项，并说 `*.*` 可查看预览中的所有格式；FAQ 38 则单独说明每个 Explorer view 的文件过滤，可用 `*.exe`、分号通配符并选择是否过滤文件夹。FAQ 34 的通用说明混有日期、大小和关键词过滤列表描述，尚不能确认预览选项是否同时筛选列表。本项目已实现每标签快速文件过滤；不可把它冒充预览资格过滤。本项目使用每标签原生 ExplorerBrowser Preview Pane | `IFolderFilterSite` 会隐藏列表项；`IExplorerPaneVisibility` 只控制整个窗格可见性，不能按所选文件切换。复用系统 Preview Handler 时没有已确认的逐项钩子；自建 Handler 宿主会扩大 COM 生命周期、焦点/快捷键与稳定性范围。FAQ 34 语义需真实 Q-Dir 可见交互确认后再决定边界；不把快速筛选或整窗格开关冒充逐项预览过滤。技术边界见[预览窗格调查](preview-pane-investigation.md#q-dir-预览格式过滤的实现边界2026-10-28-复核) | P2 待评估 |
| 照片打印（Photo-Print） | 已从活动 Shell 选择接入 WIC 解码、坏图逐项报告、1/2/4/6/9 张分页、Fit/Fill/厘米尺寸、WPF 固定页面文档及独立照片打印窗口；打印机和纸张从本机队列能力读取，设置变化重建预览，提交使用当前预览对应的队列与已验证 `PrintTicket`，预览降采样而打印使用原像素。既有“打印当前文件视图”目录清单入口保持独立。Q-Dir FAQ 150 是公开功能基线；本机 12.79 PE 打印 API 导入名不构成该命令的函数级证据 | Release SettingsSmoke 与 App Release 通过；未启动窗口、枚举真实打印机、提交打印任务或确认打印质量，故窗口视觉、可访问性、驱动兼容、纸张能力和输出效果仍待隔离桌面验收 | P2 进行中 |
| 文件视图导出与打印 | Q-Dir 官方 FAQ 将导出入口说明为“文件→导出”，对象是当前目录视图；当前已接入活动窗格“文件→导出当前文件视图”，按可见列顺序和 Shell 项目顺序导出整个视图（用户确认即使存在选区也导出整份视图）：CSV、UTF-8 制表符 TXT、HTML 和 BIFF8 XLS。生产读取使用 `IColumnManager` 列顺序、`PSGetPropertyDescription` 标题、`SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER` 行序、`IShellItem2.GetPropertyStore(GPS_FASTPROPERTIESONLY)` 与 `PSFormatForDisplayAlloc` 单元格显示，不自行扫描目录。文件先写入同目录临时文件，完整写入并刷盘后替换目标；故障注入 smoke 确认失败保留旧目标并清理临时文件。OfficeIMO.Excel BIFF8 writer 经写出再读回；SettingsSmoke 覆盖四种序列化格式、转义及公式样式文件名字符串化。隐藏 NativeProbe 验证未筛选视图的升/降序、分组、图标视图、中文列标题和文件大小格式。生产 HostProbe 在已分组且有选中文件时应用快速筛选，确认恢复两项视图、导出顺序、原分组和选中/焦点项。NativeProbe 发现 Shell 原生接口直接对已分组视图安装筛选时可能返回 `E_INVALIDARG`/`E_NOTFOUND`；生产流程先清除分组、应用筛选并刷新，再恢复分组，绕开该状态。 | 打印命令已接入文件菜单与工具栏，并按查看方式生成缩略图网格、列表、平铺卡片或详细表格；隐藏 HostProbe 5/5 轮真实取得 Shell 图像并核对两种文档的行数；可见菜单操作、打印预览/分页质量、Q-Dir GUI 对照、筛选/分组转换可见操作、虚拟 Shell 位置、超大目录及实际 Excel 打开体验仍待验证。`.qdr` 会话导出不等同于文件列表导出。[Q-Dir FAQ 131：当前视图导出与打印](https://www.softwareok.com/?faq=131&seite=faq-Q-DIR)；[Q-Dir 功能清单](https://www.softwareok.com/?seite=Freeware%2FQ-DIR%2FHelp+file)；[Windows `IFolderView::Items`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-items)、[`_SVGIO`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)、[`IFolderView::Item`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-item)、[`IColumnManager`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icolumnmanager)、[`IShellItem2::GetPropertyStore`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem2-getpropertystore)、[`IPropertyStore::GetValue`](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-ipropertystore-getvalue)、[`PSFormatForDisplayAlloc`](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psformatfordisplayalloc) | P2 |
| 状态反馈与工具 | 总数/选中数；当前标签选区变化后 75 ms 合并刷新；查看菜单、后台容量（可用字节及百分比）及选择集快速属性的可取得大小已接入；大小值支持全局单位、精度和千位分隔配置；F9 递归汇总的文件/子目录数量可在选项中隐藏，默认显示，不完整扫描提示不受影响；现新增显式工具命令汇总所选文件/文件夹递归大小；工具菜单新增“重启 Windows 资源管理器”，确认后定位并重启承载桌面 Shell 的 Explorer 进程，自动恢复判定要求新 PID 的映像仍为 Windows `explorer.exe`，避免把任意新的桌面 Shell PID 误报为成功；状态回归、本地磁盘实读、原生属性和隐藏视图选区检查通过；导航失败按目标结束 pending；容量读取器独立取消时结算为暂不可用，导航/隐藏取消仍清除旧快照 | 可见选择/清空/切标签/导航/搜索、失败恢复、网络盘/挂载点、大选区和窄窗格排版待 GUI 验收；普通状态栏选择大小仍只汇总 Shell 已提供属性，未知不当零；递归选择计算需显式触发，见下行；Explorer 重启菜单的确认提示、桌面恢复及共享进程下文件窗口影响尚未实际操作验收 | P1 进行中 |
| 文件夹大小与子项计数 | F9、工具菜单和工具栏按需汇总当前文件系统目录整棵子树的字节数、文件数和子目录数；“工具 → 计算所选项目递归大小”按需汇总已选文件/文件夹；重复或父子重叠项目只计一次。两种扫描均可由再次 F9 取消，选择扫描在选择变化时取消；导航/切标签/隐藏窗格时取消，跳过重解析点，不自动扫描；结果显示在应用状态栏。Q-Dir 将单个文件夹的递归大小复用到 Size 列，将文件/目录数量（含子目录中的项目）复用到 Type 列，并可设单位、小数、百分比、数量、失焦暂停和是否忽略大小排序；FAQ 82 将大小下划线百分比定义为同目录项目间比例，但没有公开精确分母；见[逐行大小调查](folder-size-column-investigation.md#q-dir-已公开的列行为补充2026-10-25) | 仍无 Q-Dir 式逐行派生值、百分比和排序策略；隔离 DefView 尚未消费 adapter 的逐项属性，未通过列读取/排序门槛，产品未接线。精确百分比公式和配置默认值仍需实测；汇总扫描不计为该逐行功能 | P2 进行中 |
| 文件区颜色与全局主题 | 用户确认导入后仅文件列表黑底、重启白底；Q-Dir 整体主题与文件行颜色规则分属两种能力。当前主题含 System/Light/Dark，强调色可跟随 Windows 或自定义 RGB，WPF 外壳使用语义色资源，高对比度交还 Windows；最近选中色板还保证 WPF 选择背景至少 3:1、文字至少 4.5:1 对比度。ExplorerBrowser 文件内容仍由 Shell 绘制，不由 WPF 调色板控制。Microsoft 的 [IExplorerBrowser 架构](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)说明文件夹数据源通常创建默认 DefView，也可经 `IShellFolder::CreateViewObject` 提供自定义视图；[`IShellView`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellview)承担完整视图生命周期和框架交互。Q-Dir FAQ 47 公开 `#BG`/`#BG-A` 与颜色规则保存选项，但当前 `.qdr` 样本没有规则正向数据，未知 `colorthema` 不猜映射。NativeProbe 隔离原型已把 `#BG-A`/`#BG` 解析结果接入 `SysListView32` 的 `LVM_SETBKCOLOR`，无规则回到 `CLR_NONE`；纯策略 smoke 与 Release 构建通过，但没有运行 Shell view 或采集实际像素，详见[隔离原型记录](ishellview-color-spike.md#2026-10-26接入隔离-shell-视图背景色)。 | 先补系统强调色驱动的 WPF 语义色板和成套自有图标；系统跟随颜色与 `.qdr` 规则导入分开。黑底需同目录真实窗口对照。产品 ExplorerBrowser 没有已确认的逐行着色扩展点；隔离 IShellView 原型不能证明产品可替换。文件规则进入产品前，须先通过 Shell 菜单、选择、排序/分组、键盘、拖放、文件操作及刷新兼容门槛。 | 系统色板/图标代码已实现、待桌面验收；文件列表黑底需复现；Q-Dir 颜色规则待有效 `.qdr` 样本 |
| 搜索与过滤 | Shell Search Folder 已接入，文件系统路径和通过本程序 Shell 身份校验的虚拟文件夹均可作为范围；无效位置会清楚提示，不构造错误的 `search-ms:` 请求。无窗口本机探针覆盖 Desktop、This PC、Recycle Bin 与 This PC GUID，最多枚举首项；完整结果、第三方扩展和可见 ExplorerBrowser 行为仍待验收。快速筛选接入 Shell `IFolderFilterSite`、每标签独立状态、INI v6 迁移和窗格筛选栏。支持分号分隔的 `*`/`?`、包含/排除、可选筛文件夹；Ctrl+M 打开，Enter 应用，F5 在输入框应用并刷新，Esc 清除关闭。默认不过滤目录，对应 Q-Dir 的“忽略目录”；隐藏宿主覆盖筛选规则、四窗格隔离和搜索视图往返恢复；分组筛选恢复现等待本次 Shell `ShouldShow` 枚举完成，超时清筛并重试未筛选视图，隐藏生产 HostProbe 5/5 轮通过。 2026-10-26 隐藏夹具重跑发现分组筛选期间 Shell 在未收到用户文件输入时临时扩大选区；生产代码现跨分组清除与刷新接管边界抑制恢复回调，筛选后保留原选中项与焦点。修复后完整快速筛选/Search Folder 夹具两轮通过，行身份、分组、四窗格隔离、父窗口隐藏及前台不变均核验；焦点顺序与物理筛选栏输入仍待可见验收。启用筛选时只读沿用 Windows Shell“显示所有文件”偏好：开启时请求枚举普通隐藏项，关闭时不请求；不读取或显示受保护系统隐藏项。命名筛选预设可跨窗格保存、载入和删除，独立存放于 `QuickFilterFavorites.ini`，不改变其他标签当前筛选。Q-Dir FAQ 37 的 Alt+F3 所选文件夹搜索已接入：要求文件视图聚焦且恰有一个普通文件系统文件夹选中，搜索栏显示范围文件夹名并在提示中保留完整路径；Esc 返回原标签目录。多选、非文件夹及虚拟所选位置明确提示，不继承旧范围。 | 物理按键、焦点返回、搜索结果、非索引/网络/权限位置、虚拟文件夹和跨重启可见体验仍待验收；不将无窗口路由与 URI 测试等同于 Q-Dir 完整兼容 | P1/P2 进行中 |
| 收藏、快速链接与工作区 | 独立 Favorites.ini 保存目录；profile 下 `QuickLinks/*.lnk` 保存文件夹、程序或文件快捷方式，Ctrl+Q 打开快速链接菜单；可添加当前目录或单个所选文件、文件夹及可解析的 Shell 文件夹；管理入口打开 `.lnk` 目录，后续仍由 Windows Shell 管理快捷方式；Workspaces 下的 `.qdr` 保存整组布局/标签 | 快速链接与 Program Starter 是两项独立功能：Ctrl+Q 仍打开 `.lnk` 快速链接，工具菜单 / Ctrl+S 打开 Program Starter。后者使用独立便携 `ProgramStarter.ini`，支持命令项增删改、Q-Dir `[Program]` 文件选择、导入预览后显式应用、标准占位符、活动布局可见窗格快照，以及普通用户 `ArgumentList` 启动；解析只把引号外 `=` 作为字段边界，引号内 `=` 可保留在程序路径或参数值，唯一尾部分隔符仍按工作目录边界解释。导入读取支持 UTF-8/UTF-16 BOM、严格 UTF-8 和 Windows 当前 ANSI 代码页，避免无 BOM 本地编码的中文字段损坏。写入由共享配置的外部编辑检查保护；新增、编辑、删除或导入只有成功保存后才更新窗口列表，冲突不会留下未保存的界面状态。隔离发布 profile 已加入此配置文件。Store、占位符、解析器、编码读取、启动分派、快捷键保护和命令状态有 SettingsSmoke 覆盖；真实隔离子进程已验证中文、空格、引号、等号和空参数的 argv 边界。新增所选项路径的 Store smoke 通过；菜单焦点、启用状态及导入交互仍待桌面验收；本机 Q-Dir.ini 无有效启动项行，Q-Dir 正向文件互操作未证实。Program Starter 不隐式替换当前标签 | P2 进行中 |
| 导入 Q-Dir 工作区 | `.qdr` 目录/标签/查看方式及全部 12 个已核实原生布局编号（0–11）均映射到本程序布局，按可见位置转换窗格；每标签 `ViewModeN` / `ViewModeN_i` 按 Q-Dir FAQ 117 的 FVM_ICON～FVM_THUMBSTRIP 1–7 导入，8（Windows Content）及越界值回退详细信息并提示；5/7 在本机隐藏 IShellView 降级为大图标/平铺；已识别包装可导入排序/分组/图标大小/列设置；未识别字段名在预览汇总提示（本机样本含 `ColsID`、`ViewModeFlags`），不回显字段值；`tree_mode` 的 0/1/2 映射到关闭/共享/逐窗格树；`Tree1`～`Tree4` 的 0/1 暂按逐窗格树开关导入，并在预览中提示尚未通过 Q-Dir 可见互操作确认；全局 `Vorschau` 应用到本次导入标签；追加前预览与备份 | `TreeNsplit` 宽度字段仍不导入，单位/含义未确认；Q-Dir 颜色规则尚未导入；布局映射有本机 Q-Dir 12.79 静态几何证据；真实选择、追加、重启恢复及 Q-Dir 版本差异待验收 | P1 |
| 导出 `.qdr` 状态与格式兼容 | 基础块写全部 12 个布局对应编号、窗格字段、零起始 Focus 和当前活动标签的 `Vorschau`，保留隐藏窗格；Content（8）因 Q-Dir 公开功能清单未列出而在 `ViewModeN` 和视图流中均回退为详细信息（4），Explorer Dirs 扩展保留准确的应用视图；完整列快照时写原生视图状态流，见[格式与证据](qdr-layout-mapping.md)。三份真实 Q-Dir 样本经过本程序导入→导出→再导入后，扩展会话状态逐字一致，剥离扩展的基础字段也能由本程序导入器恢复目录/标签/选择/视图 | Windows 隐藏 `IShellView` 已读回视图状态流；Q-Dir 实际读取、GUI 追加/恢复、活动窗格焦点与分隔线外观仍未验收；本程序自身往返不等于 Q-Dir 互操作通过 | P1 进行中 |
| 性能与稳定性 | 有运行标记、诊断脚本；Changed 文件事件在 250 ms 静默后按路径合并并发送 Shell `UPDATEITEM`，超 128 条或路径过长回退整视图刷新；watcher 重建后，事件处理与 watcher 替换/退休串行化，过期对象回调会丢弃；Shell 扩展仍进程内运行。内存一轮对照中，释放 44 个闲置视图仅减少约 2 MB；重启后的下降更明显，用户报告启动后约 229 MB 工作集 / 156 MB 提交量 | 隐藏真实 ExplorerBrowserHost 已验证 Changed 元数据快照/大小排序更新及选区/焦点保持（约 1.12 秒）；可见像素重绘、其他 Shell provider、网络/大目录性能、扩展阻塞/崩溃仍待验收；内存增长需按同一 PID 和固定工作负载纵向采样工作集、Private Bytes、句柄、线程及托管堆，现有多 PID 截图不能说明增长来源；不能声称已解决原生扩展隔离 | P0 持续 |

### 2026-10-25：Q-Dir 官方功能清单差异补录

官方帮助页目前列出预览格式过滤、文件夹大小/子项数量、经典/现代地址栏、Quick-Link 管理和屏幕放大镜。对照现有源码：原生预览窗格没有 Q-Dir 对应过滤入口；地址栏现在默认显示现代面包屑，并在“工具→选项”提供 Classic 完整路径与 Modern 面包屑切换，存于独立 `AddressBar.ini`；输入时会将绝对盘符/UNC 路径的 `/` 和重复 `\\` 规范化后交给 Shell，虚拟 Shell 名称、URI、相对输入和设备路径不改写。两种样式的实际焦点和四窗格显示仍待 GUI 验收。Q-Dir FAQ 146 另列地址栏历史菜单与路径分隔符规则。目录收藏与工作区只部分对应 Quick-Link 管理；F9 已补当前目录递归大小与数量，但仍无逐行目录大小列。屏幕放大镜列为低优先级便利功能。FAQ 34 对预览过滤与列表过滤的描述需要可见确认；当前原生 Pane API 也没有按扩展名设置资格的直接入口，见[技术评估](preview-pane-investigation.md#q-Dir-预览格式过滤的实现边界2026-10-25)。

2026-10-26 地址/查看入口对照：窗格工具栏现在有每标签查看方式按钮，复用相同的 Shell 原生视图菜单；地址栏右侧位置下拉列出当前目录到根目录的上级链及常用系统位置，地址历史仍独立。现代样式使用现有“编辑”按钮切换完整路径编辑，经典样式可点击路径编辑。此轮只通过纯状态菜单模型 smoke 与 Release build；图标、弹出菜单观感、虚拟位置解析和多窗格交互仍待可见验收。Q-Dir 官方说明确认其支持现代/经典地址栏切换及通过地址栏导航上级目录；Windows Explorer 文档将面包屑作为可点击祖先导航，并允许输入路径。[Q-Dir 地址栏/上级目录 FAQ](https://www.softwareok.com/?faq=53&seite=faq-Q-DIR) · [Q-Dir 地址栏 FAQ](https://www.softwareok.com/?faq=100&seite=faq-Q-DIR) · [Microsoft Explorer Shell 地址栏说明](https://learn.microsoft.com/en-us/windows/win32/shell/developing-with-windows-explorer)

2026-10-24 自动更新补充：源码复核发现当前目录 watcher 此前只订阅名称变化；现有文件大小、修改时间、属性或创建时间变化现在会以零计数强制刷新请求进入既有合并队列。完整 SettingsSmoke 和 App Release 构建通过，尚未打包；真实文件系统事件和可见列更新待桌面验收。子目录不递归监听。

2026-10-25 快速筛选枚举标志语义更正：Microsoft 将 `IFolderFilter::GetEnumFlags` 的 `pgrfFlags` 定义为 `[out]`，因此必须由过滤器提供枚举类型。该次接口语义更正本身未改变行为；后续已按 Windows `Show All Files` 偏好加入普通隐藏项枚举，详见上方“搜索与过滤”及[路线图](roadmap.md#2026-10-25快速筛选沿用-windows-隐藏文件偏好)。

## 本轮代码与下一轮验收

2026-10-19 桌面实测：隔离构建粘贴后方向键可用；Shift+Delete 确认后却切到另一窗格。焦点日志证明另一窗格的 Static 容器收到 WM_SETFOCUS 后误更新了活动窗格。候选修复只认可文件内容窗口及其子窗口的焦点；修复后 GUI 未复测（用户 Esc 停止）。此修复收入用户要求的临时预览包，不能标记验收通过。

前一轮新增当前标签的查看方式入口。本轮通过 IColumnManager 保存和恢复每标签的显示列、顺序与宽度；使用稳定属性键，不依赖中文列标题。会话升级为 v2，首次保存先备份读取到的 v1 原文；旧版会拒绝覆盖 v2。部分读取保留完整缓存，恢复失败时暂停会话写入，重新导航后重试。搜索视图设置保持运行时状态。细节与回退方式见[列设置设计与验收](column-layout.md)。

下一次明确空闲桌面时优先用隔离测试目录：两个窗格互相复制/移动 → 粘贴后立即方向键 → 多选删除取消/确认 → 正反 Tab → 背景右键 → 切查看方式并重启恢复。失败就修复并重跑相关步骤，不扩展为无关功能。记录确切构建/发布哈希与实际结果。

每次核心交互改动同步本表及路线图；新可用版本仍只发布到不可覆盖的 `releases/<version>/`，经确切二进制真实窗口验证后才标记可用。

## Ctrl滚轮诊断

2026-10-20：宿主内原生窗口的鼠标输入通常直接进入 Win32，不经过 WPF 鼠标事件，因此不能凭 WPF 处理器缺失判定根因。[微软 WPF/Win32 互操作说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation)。

沿用仅为隔离测试进程设置的 `EXPLORER_DIRS_FOCUS_TRACE` 日志路径开关。启用后，Ctrl+WM_MOUSEWHEEL 每经过一个已挂钩的原生窗口就记录刻度、窗口类、是否到达宿主；同一轮分发合并一次后台读取实际模式与图标大小。记录不新增路径、文件名或剪贴板内容，不改变默认消息分发，不主动聚焦或转发滚轮；默认不产生记录。

2026-10-24 已完成代码层自定义循环。隐藏宿主合成消息覆盖已挂钩 view HWND；此前记录仅作实施前 Shell 原生行为证据。真实桌面仍需确认物理消息到达该宿主、目标窗格由鼠标位置决定且按下 Ctrl 时不会产生原生图标缩放；GUI 验收前不宣称完整修复。

2026-10-24 精确文件创建通知再验证：回归探针定位并修复了 60 ms 合并路径丢失单个代表文件路径的问题。现在产品 watcher 会把路径保留到 Shell STA 计时器读取；隐藏生产宿主单文件复制确认 1 次 `SHCNE_CREATE`，完整跨窗格文件操作和焦点/选区检查通过。该轮仍触发 fallback，不能据此判定即时显示改善。详见[修复调查](live-update-investigation.md#2026-10-24-精确-create-通知路径回归修复)。

修复后的再复跑在一次单文件 Shell 复制中约 117 ms 发出 `SHCNE_CREATE`、133 ms 显示文件且无 fallback；后续完整四窗格产品监视器也通过。此前同一路径仍出现 fallback，因此不把单次快速结果外推为普遍保证，计数兜底保留。详见[更新记录](live-update-investigation.md#2026-10-24-精确-create-通知路径回归修复)。

创建通知修复后的 50,000 项本地隐藏宿主压力样本：产品 watcher 创建/删除事件均完整，通知按持续创建期间的静默合并窗口发出 12 次，创建无 fallback；删除由两次 fallback 收敛，最终计数、选区和焦点正确。最大 Dispatcher 间隔 514 ms，不能标记性能验收通过。详见[压力记录](live-update-investigation.md#2026-10-24-创建通知路径修复后的-50000-项复测)。

2026-10-24 Q-Dir 原生视图流复验：16 组隐藏 `IShellView` 恢复检查通过，覆盖八种查看预设、分组、图标尺寸、排序和列布局；实际 Q-Dir 文件读取与可见窗口验收仍开放。NativeProbe 已在退出时清理本次唯一 GUID 夹具。详见[导出验证](qdr-import.md#2026-10-24-原生视图流复验与探针清理)。

2026-10-24 焦点算法回归扩展：不同停靠点数量的四窗格全循环验证 Tab/Shift+Tab 每一步互逆且无重复或遗漏，并验证 F6/Shift+F6 major 区域互逆；Release SettingsSmoke 通过。实际控件 Tab 顺序、详细信息列头停靠及真实键盘焦点仍待 GUI 验收。

2026-10-24 源码续验：四窗格真实隐藏 Shell 重命名 Edit 的方向键路由探针 4/4 通过；左右键移动文字光标、上下左右保持编辑焦点及窗格归属。生产目录树隐藏宿主的展开、取消同步、根替换、越界目录拒绝、STA 生命周期与销毁检查通过。App、NativeProbe、HostProbe Release 构建和 SettingsSmoke 通过。没有物理键盘或可见桌面输入，因此原报告的 F2 可见多窗格复现和目录树实际键盘/鼠标行为仍待验收。
2026-10-24 标签对齐续进：目录树中键新建标签接入 `INameSpaceTreeControlEvents::OnItemClick`。事件携带实际被点的 `IShellItem`，只将文件系统文件夹转换为绝对路径并派发到新标签；中键双击通知被忽略，避免同一次双击开两个标签。SettingsSmoke 与 App Release 构建通过；未操作桌面，目录树原生点击和可见新标签结果仍待 GUI 验收，文件列表中键另行调查。

2026-10-24 文件视图中键目标实现：微软 UI Automation 的 ListItem 契约规定 Name 来自项目文本标签；生产路径在中键按下时异步做坐标命中，要求命中当前 Shell 文件列表条目，再以标签名在捕获的当前文件系统父目录中构造候选路径。Shell 必须重新解析该路径，并确认它仍是同名、同父目录的普通文件系统文件夹；释放后只在视图、导航和输入代际仍一致时开新标签。空白、表头、搜索/虚拟位置和无效路径不动作，不借用残留选区。SettingsSmoke 覆盖子路径与路径逃逸拒绝，App Release 与完整 SettingsSmoke 通过；未使用 GUI，原生 UI Automation 实际命中与物理中键仍待验收。

Ctrl+N 已按 Q-Dir 的 Duplicate Window 语义复制当前四窗格/标签/布局状态，并创建独立窗口会话：[Q-Dir 官方快捷链接说明](https://www.softwareok.com/?faq=4&seite=faq-Q-DIR)也将 Duplicate Window 列为菜单命令。主窗口继续使用 `ExplorerDirs.ini`，其他窗口各自使用独立 INI，由 `WindowSessions.ini` 记录并在启动时恢复。文件菜单和工具栏明确标为“复制当前窗口”；窗口本地固定标签跟随会话文件保存，收藏夹、筛选收藏、应用偏好和布局比例仍在 profile 级共享。隐藏配置回归覆盖互不覆盖、顺序载入、关闭单个窗口、保留最终窗口及外部修改冲突。若清单引用的次窗口 INI 缺失，则回退到主会话、给出提示并禁止覆盖清单，避免生成一个默认空白窗口。真实窗口和跨重启 GUI 操作仍待验收。

2026-10-25 Shift+Enter 目标标签路由续查：Q-Dir 官方快捷键表包含“Selection in new tab”。项目原生 `HwndHost` 路径已有该命令，但 Microsoft 的 WPF/Win32 互操作说明指出未处理的按键还会进入 WPF 路由；窗口级 `ExecuteShortcut` 原先没有对应分支，会把按键标记为已处理而不打开标签。现在两个路径共用文件区焦点和有效单文件夹选择门控；SettingsSmoke 覆盖打开成功、焦点不在文件区及无唯一普通文件系统文件夹选择。实际物理按键、Shell 回退与可见标签行为仍待 GUI 验收。

2026-10-25 Q-Dir 标签栏拖放差异已接入代码：空白标签栏支持单个文件夹/驱动器新建并选中标签；虚拟 PIDL 与普通 `CF_HDROP` 分别处理，均须经 Shell 确认 `SFGAO_FOLDER`。普通文件、多项、URI 拒绝，拖到现有标签仍不导航；标签自身重排继续用原私有令牌。无界面 `CF_HDROP` 文件夹/文件/多项冒烟、路径决策 smoke，以及由 Windows `SHCreateDataObject` 生成的 CIDA 虚拟文件夹数据对象生产解析通过。仍未验证真实虚拟 Shell 拖放来源、标签栏空白区命中、提供程序异常、实际建标签后的导航和跨重启恢复；真实 Q-Dir 行为仍待可见验收。范围及限制见[标签栏 Shell 拖放设计](superpowers/specs/2026-10-24-tabbar-shell-drop-design.md)。

2026-10-25 全局 WPF 主题偏好已接入：profile 级 Theme.ini 保存 System/Light/Dark，默认跟随 Windows；System 模式订阅 UISettings 颜色变化并动态替换 WPF SystemColors，显式模式固定，应用退出解除事件。高对比度时清除自定义资源，交还系统颜色。Shell ExplorerBrowser 文件区仍由 Windows 管理，不把 WPF 深色主题当作文件区着色或导入黑底修复。ThemePreference SettingsSmoke、App/NativeProbe Release 构建通过；HostProbe Release、发布隔离与 folder verb smoke 通过。真实系统切色、菜单样式与窗口/标题栏行为仍待可见验收。

**2026-10-25 隐藏 ExplorerBrowser 列标题运行时复验：**`FVO_CUSTOMPOSITION` 在 Initialize 前读回已启用；两个 `OnViewCreated` 回调均成功读回列标题抑制位清除。隐藏生产 ExplorerBrowser 在有/无分组 × 八种查看方式切换时保持列顺序/宽度、排序/分组及两项选区身份，父窗口隐藏、前台未变。尚未检查 `SysHeader32` 样式或真实可见/点击行为，不等于列标题 GUI 验收通过；详见[路线图记录](roadmap.md#2026-10-25隐藏-explorerbrowser-中列标题-folder-flags-运行时复验)。

**2026-10-25 列头样式补测：**隐藏 ExplorerBrowser 在 16 组视图序列中的 folder flags 保持 `0x40200200` 且两个列头抑制位始终清除；`SysHeader32` 一直存在，但其 `WS_VISIBLE` 随模式/切换历史变化，列表多为隐藏，图标尺寸样本也有不一致。当前不能证明全模式可见，也不应直接强制子窗口样式；先查明原生窗口状态与排序通知时序。见[路线图记录](roadmap.md#2026-10-25列头-hwnd-样式与-folder-flags-对照)。

**列头样式时序复测：**隐藏宿主两轮在 1 s 取样都看到八种模式的 `SysHeader32` 变为 `WS_VISIBLE`；部分模式在 250 ms 仍隐藏。列头 `HDM_GETITEMCOUNT` 全程为 3，folder flags 不变，列配置/选区/排序/分组保持。更支持异步布局时序解释，但仍未验证像素、实际标题文本、鼠标排序或可见 GUI。详见[路线图记录](roadmap.md#2026-10-25列头异步样式与列项数复测)。

2026-10-25 隐藏 ExplorerBrowser 列头消息续验：附加生产 ShellBrowserSite 后，向 SysHeader32 定向发送标准按下/抬起窗口消息，Shell 排序方向发生切换；直接发 WM_NOTIFY 也能切换排序。两条路径的 ICommDlgBrowser3.OnColumnClicked 计数均为 0，表明本机 DefView 自行处理此排序。该测试没有物理输入或可见绘制，列头实际可见、鼠标命中及 GUI 排序仍待验收，见[路线图记录](roadmap.md#2026-10-25-explorerbrowser-列头通知路由与排序复测)。

2026-10-25 Ctrl+数字小键盘加号补齐：文件视图按键先经 `IShellView.TranslateAccelerator`，仅对 `S_FALSE` 将 Ctrl+VK_ADD KeyDown 转交原生 `SysListView32`，由 Shell 自己计算列宽。路由 smoke 与原生 `IShellView` 探针通过；2026-10-28 又在隐藏生产 `ExplorerBrowserHost` 中确认 Name 列从 110 增至 419，其他窗格列宽不变、焦点保留、桌面前台不变。实体数字小键盘与可见 UI 仍待集中验收。详见[路线图](roadmap.md#2026-10-25ctrl数字小键盘加号转交原生列宽自适应)。

2026-10-26 焦点恢复的“文件区是否为空”查询改为显式 `IFolderView.ItemCount(SVGIO_ALLVIEW)`。SettingsSmoke 检查传入范围、空视图和失败计数；App Release 构建通过。隐藏 stock Shell 对 `SVGIO_BACKGROUND` 也返回总数的结果不能代替 API 契约证明；未验证可见多窗格焦点或真实方向键行为，详见[路线图](roadmap.md#2026-10-26焦点恢复使用明确的-shell-视图计数范围)。

2026-10-26 Q-Dir `[Program]` 导入中，多个未引用的参数区段等号现在作为歧义行显示在跳过预览中，避免把可能的工作目录静默拼进参数；明确引用的等号参数仍通过 argv 边界测试。导入 smoke、完整 Release SettingsSmoke、App Release 构建通过。真实 Q-Dir 正向配置样本及窗口交互尚未验收，详见[路线图](roadmap.md#2026-10-26q-dir-程序项导入拒绝含糊的额外分隔符)。

2026-10-27 搜索位置生命周期：浏览器销毁时清空该宿主的搜索 PIDL/解析名称映射，因为对应 Shell TravelLog 同时失效；每标签仍在当前浏览器生命周期内保留搜索 Back/Forward 所需身份。SettingsSmoke 验证销毁后旧身份失效及重新记录；没有启动 ExplorerBrowser，真实搜索返回/前进与 HwndHost 重建后的可见行为仍待验收。详见[路线图](roadmap.md#2026-10-27浏览器销毁时清理-shell-搜索身份映射)。

2026-10-26 Program Starter 的显式相对工作目录按应用程序目录解析；绝对路径、活动文件夹默认值及虚拟位置回退保持原样。参数 smoke 通过。Q-Dir 官方资料未明确第四字段相对路径锚点，因此这是本程序的便携路径规则，不作跨应用一致性声明；完整 SettingsSmoke、Release 构建和实际 Q-Dir 正向样本验收见[路线图](roadmap.md#2026-10-26program-starter-支持相对工作目录)。

2026-10-26 地址与查看入口：窗格工具栏现在提供每标签查看方式按钮，并复用原 Shell 查看方式菜单；地址位置下拉显示当前路径及所有上级、常用系统目录和 Shell 位置，历史菜单继续独立。现代地址栏保留独立编辑按钮，经典完整路径可点击编辑；位置下拉是单独 Tab 停靠点。路径菜单模型与焦点顺序 smoke、Release 构建通过；弹出菜单图标、布局和虚拟位置的可见行为仍待桌面验收。[Q-Dir 地址栏/上级目录 FAQ](https://www.softwareok.com/?faq=53&seite=faq-Q-DIR) · [Microsoft Explorer 地址栏与面包屑](https://learn.microsoft.com/en-us/windows/win32/shell/developing-with-windows-explorer)

2026-10-27 多窗格 Shell 重命名焦点防护：窗口级快捷键和文件命令状态刷新现检查所有窗格的原生重命名编辑框，而不只检查逻辑活动窗格。静态接线 smoke 通过。另据[当前 Release 四窗格 HostProbe](roadmap.md#2026-10-26当前四窗格生产宿主-f2-方向键复验)记录，四个生产 ExplorerBrowser pane 的隐藏宿主均用合成消息验证方向键后原生编辑框保持焦点；这不验证物理 F2 消息进入路径、可见多布局窗口或真实键盘，故 F2/方向键仍是 P0 可见验收项。

2026-10-27 Q-Dir 重启快捷键：官方快捷键清单列出 Alt+F11 重启；本程序“文件 → 重启应用”现在标注并响应 Alt+F11，复用现有确认、所有窗口会话保存和重启逻辑。分派/接线 smoke、完整 Release `SettingsSmoke` 与 App Release 构建通过；没有触发真实重启或 GUI 验收。[Q-Dir 官方快捷键清单](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)

2026-10-27 F11 沉浸式全屏：按用户选择，保留应用菜单、工具栏和状态栏原先的显隐状态；全屏只移除窗口边框并覆盖当前显示器，退出后恢复原窗口状态。无窗口状态 smoke 与 Release build 通过；多显示器、任务栏遮挡和实际 Shell 视图尺寸尚未可见验收。[路线图](roadmap.md#2026-10-27实现-q-dir-f11-全屏切换)

2026-10-27 IShellView 原型 F2 编辑焦点：单个默认 Shell 视图在 F2 重命名编辑期间，Left/Up/Right/Down 四个合成按键均由 `TranslateAccelerator` 接收，编辑框与焦点保持。Private Desktop 临时夹具验证通过；这不覆盖多窗格/WPF 或 ExplorerBrowser 产品，因此现有多窗格重命名缺陷仍为 P0 待可见验收。[隔离原型记录](ishellview-color-spike.md#2026-10-27-f2-编辑状态下四方向键隔离验证)

2026-10-27 IShellView 原型 F2 编辑焦点扩展：Icon、Details、SmallIcon、List、Tile 五种模式下，F2 均进入原生重命名编辑框；Left/Up/Right/Down 合成键后编辑框焦点保持，Private Desktop 完整流程连续两轮通过。真实多窗格焦点争用及 ExplorerBrowser 产品行为仍待可见验收。[隔离原型记录](ishellview-color-spike.md#2026-10-27-五种视图模式下-f2-编辑焦点隔离验证)

2026-10-27 四窗格 IShellView F2 路由：四个 pane 分别启动重命名并将 Left/Up/Right/Down 路由到目标 view 后，编辑焦点保持，兄弟 pane 的 focused item 和 selection 不变，两轮 Private Desktop 完整探针通过。尚未验证 WPF 的错误窗格路由、物理输入或 ExplorerBrowser 产品行为。[隔离原型记录](ishellview-color-spike.md#2026-10-27-四窗格-f2-编辑方向键焦点隔离)

2026-10-27 IShellView 错误目标焦点隔离：Pane 1 原生 F2 编辑时，Pane 2 收到 Pane 1 编辑框的合成 Down 消息并返回 `S_FALSE`，未夺取 Pane 1 编辑焦点；四窗格真实 Shell session-restore consumer 连续两轮通过。该结果不覆盖 WPF `HwndHost` 消息链或物理输入。[隔离原型记录](ishellview-color-spike.md#2026-10-27-错误-pane-的-accelerator-不夺取活动-f2-编辑焦点)

2026-10-27 IShellView 合并 File/Edit 菜单原型：标准合并菜单 File 与 Edit 组中的命令现在按组归属识别，并将原始 Shell 命令 ID 发给活动 view HWND；同步 `GetWindow` 返回后会重新验证 pane owner 和 view，避免 COM 重入切换期间把旧命令发到新视图；Edit > Select All 仍走原生选择接口，View 命令保持原路由。合成 HMENU smoke 通过假 COM `GetWindow` 接口向隐藏 HWND 实际发送并捕获 File/Edit/View 的原始 `WM_COMMAND`，同时验证菜单分组、嵌套路径、Edit 分类、非法组不匹配及派发前重入期间的 view 替换，没有创建 Shell view。真实 Shell File/Edit 命令执行、菜单激活/显示、列标题范围及可见 GUI 交互未验证，不能视为迁移菜单门槛通过。

2026-10-27 列标题菜单契约复核：Microsoft `IShellView::GetItemObject` 使用 `_SVGIO` 范围，官方枚举只有背景、选中、全部和已勾选项目；没有列标题范围。因此列标题菜单不是缺少一个 `GetItemObject` 参数，而是独立表头 `WM_CONTEXTMENU` 的创建、命令来源和执行路由问题。现有 Private Desktop 探针只捕获 popup、初始化查看/排序/分组子菜单并比较其命令 ID 偏移，随后取消菜单；尚未验证实际命令分发、排序列身份或用户可见行为。[Microsoft GetItemObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-getitemobject) · [Microsoft _SVGIO](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)

2026-10-28 列标题菜单命令隔离复核：在 Private Desktop 的临时 `IShellView` Details 视图中，实际捕获并初始化查看/排序/分组子菜单；各子项与背景 `IContextMenu` 项一一映射，命令 ID 基差为 `30976`。将列标题“类型”项映射为背景菜单命令偏移并调用 `IContextMenu::InvokeCommand` 后，`IFolderView2` 读回的主排序键变为 `PKEY_ItemTypeText`（属性 ID 4）。直接把捕获到的 ID 发给 `SHELLDLL_DefView` 的 `WM_COMMAND` 没有改变排序，故不能把裸窗口消息当作有效分发路径。前台窗口保持不变。该结果验证的是隔离宿主的菜单 ID→Shell 命令映射与排序状态，不模拟用户实际选择菜单项，也不验证 ExplorerBrowser 产品 UI 的物理输入、排序列命中或焦点；后者仍待集中可见验收。

2026-10-27 OLE 宿主边框合同复核：隔离 `StreamViewHost` 的 `GetBorder` 返回父客户区；`RequestBorderSpace` / `SetBorderSpace` 接受零边框占用，并对非零工具栏空间返回 `INPLACE_E_NOTOOLSPACE`。`--shell-view-color-policy-smoke` 经真实 COM vtable 覆盖了有效/空参数及返回码，没有创建 `IShellView`。这只验证宿主合同，不证明 Shell 实际激活流程会按该顺序调用，也不代表完整宿主迁移门槛通过。

### 2026-10-27 鼠标空白取消选择（候选，待实机验收）

鼠标选择手势按下至抬起期间不保存中间快照用于恢复，抬起后捕获 Shell 最终状态。已通过策略检查与构建；空白点击取消、再框选连续两项及原生刷新保留选择仍待实机验收。原生菜单白框仍未解决。


### 用户验收补充（2026-10-27）

用户已确认：多文件选择后，点击空白取消选择正常。此验收针对所测候选和该操作；Ctrl/Shift/跨视图与后台更新保持选择不由此自动推定通过。


### 2026-10-27 文件区原生菜单基线

按用户要求暂停文件区自定义菜单注入：新建/复制/固定/关闭/恢复标签及复制路径不再追加到文件区原生菜单；标签栏菜单、主菜单及快捷键入口保留。未提供 CustomCommands 时不启动自定义目标 UIA 探测。仍保留主题兼容处理，不将菜单内容原生等同于无主题处理。构建通过；最新隔离副本准备后待可见验收。


### 2026-10-27 主题验收补充与子菜单贴合候选

用户验收通过：空白点击取消多选、原生菜单深浅往返、目录树主题跟随、094726 的菜单内外异常亮框消失。文件区暂停自定义命令注入。6209e88 已加入可见子菜单边缘补偿，左右展开/不累积位移的隐藏原生测试通过，真实 Shell 父子识别、连续鼠标进入、屏幕边缘与不同 DPI 仍待复测；最新候选 095843 尚未启动，不记为完整菜单体验通过。


### 2026-10-27 子菜单横向位置回归原生

用户明确要求父子菜单横向位置遵循资源管理器原生规则。当前不提供独立间距模式，因此删除 6209e88 的应用层位置补偿及其专用测试，不再枚举 HMENU 父子关系或调用 SetWindowPos 移动子菜单；展开方向、重叠量和屏幕边缘避让交回 Windows Shell。保留单层灰边、主题往返和目录树刷新。未来若明确提供独立间距选项，必须包含默认跟随原生和 0 间隔；本批不新增配置入口。NativeThemeColorProbe 和 Release 构建通过；裁剪后的视觉贴合仍需在新隔离候选中对照验收，不宣称已与 Explorer 像素一致。

### 2026-10-27 用户授权恢复裁剪补偿
用户明确授权：父子菜单按实际相邻裁剪宽度之和靠近，抵消本程序引入的透明距离，保留系统原始重叠量。不增加独立间距配置。保留当前贴合底色的单像素灰边。隐藏 HWND 用例验证向左/右展开的可见重叠量、尺寸/纵坐标不变及重复调用不累积位移；真实 Shell 父子识别、多级展开、边缘避让与不同 DPI 仍待用户验收。本批只准备隔离候选，不启动 GUI。

### 2026-10-27 最新主题验收边界
右键文件区闪白、菜单动画单框贴合、活动窗格尺寸闪烁均已获用户明确确认并落地 temp.76；原生滚动条端部首次白块仍在同步绘制候选验证。完整记录见 [主题重绘审查](theme-redraw-review-2026-10-27.md)，不据这三项关闭全部 Shell P0。

> **四 pane WPF 选中项菜单隔离（2026-10-28）：**隐藏 Private Desktop 中四个原生 `IShellView` 均能为选中项提供含 `copy` 的 `IContextMenu`。初次失败时四个 view 在菜单测试前均残留 2 项选择，修复探针为每轮清空并复核全部 pane 后，完整流程连续 3 轮通过；不等于可见菜单跟踪或鼠标拖放验收。详见[隔离验证记录](ishellview-color-spike.md#2026-10-28四窗格-wpf-选中项菜单隔离复验)。

> **四 pane WPF 新建文件夹命令（2026-10-28）：**隐藏原生背景菜单通过产品 `ShellFileCommands.Invoke(NewFolder)` 路径执行；适配器先初始化延迟“新建”子菜单，再以当前菜单偏移调用 `IContextMenu::InvokeCommand`。三个同目录 view 均观察到新文件夹，独立目录 pane 保持原状，连续 3 次通过。未初始化子菜单的单独偏移探针曾返回 `E_FAIL`，这不是产品适配器的结果。实际 ExplorerBrowser UI 的焦点/重命名体验仍待验收。详见[隔离验证记录](ishellview-color-spike.md#2026-10-28wpf-隔离四窗格的原生新建文件夹命令)。
> **Explorer `/select` 启动定位（2026-10-28）：**启动解析支持 `/select,<对象>`、逗号后空格分隔及 `/select <对象>`；对象父目录与对象路径通过现有 profile 单实例协议路由，重试沿用请求 ID 去重。目标窗格只在相符导航完成后排队原生 Shell 选择，并绑定导航、选区、用户输入代际。旧版接收端不支持选择消息时，协议协商降级为打开父目录，避免启动请求丢失。Release SettingsSmoke 与 App Release 构建通过；未运行 ExplorerBrowser 或可见 GUI，Windows 实际参数拆分、Shell 高亮和焦点仍待集中验收。未知开关保持拒绝，不接管 Win+E/注册表。

### 全局关闭文件视图分组（2026-10-28）

“视图 → 停用所有视图分组”是 profile 级开关，独立于 Shell 右键菜单的单视图分组。开启时对每个现有/新建视图设置 Windows `FWF_NOGROUPING`；标签原分组键和方向仍保留在会话缓存，关闭开关后恢复。偏好存于 `DisableGrouping.ini`。后台 `SettingsSmoke` 与 Release build 通过；尚未操作真实 ExplorerBrowser 菜单，也未验证不同标签分组恢复和跨重启结果。[Q-Dir 说明](https://www.softwareok.com/?page=Windows%2FQ-Dir%2FTips%2F11) · [Windows FOLDERFLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags)

### 标签栏菜单与系统 Explorer（2026-10-28）

标签页右键菜单现在以“使用 Explorer 浏览”开头，将该标签 `Browser.CurrentPath` 传给 `explorer.exe`；空白标签栏右键显示当前标签菜单。原空白双击新建标签及标签项双击关闭规则不变。后台参数/接线 smoke 不启动子进程；真实 Explorer 导航和可见菜单定位待集中 UI 验收。

### Explorer Shell 重启工具安全边界（2026-10-28）

“工具 → 重启 Windows 资源管理器”先验证 `%WINDIR%\explorer.exe` 存在，并确认桌面 Shell 窗口所属进程的映像路径与该文件完全相同；若 Shell 是自定义进程或路径不可确认，则拒绝结束进程。自动恢复分支要求桌面 Shell 的 PID 已变化且新进程映像仍与 Windows `explorer.exe` 相同；自动恢复超时后启动的 fallback 也继续等待最多 10 秒，并用相同身份条件确认 Shell 已恢复，否则报告超时。路径、PID 和有界等待有独立 SettingsSmoke，完整 SettingsSmoke 与 App Release build 通过。该检查不证明实际重启恢复时间；真实桌面、开始菜单、任务栏和共享进程文件窗口影响仍待集中验收。

### Q-Dir 列头过滤差距（2026-10-28）

Q-Dir 官方 FAQ 38 将名称通配符过滤和列表列值过滤作为两种功能分别介绍，并明确每个视图有独立设置。示例图显示列标题的筛选入口打开该列的值清单，用户可勾选值过滤；因此本功能必须按标签分别保存，不能把筛选条件变成 pane 级或全局状态。当前 `QuickFilterSettings.Matches` 只对 Shell 项目名执行通配符匹配；`ShellFolderFilter` 没有按活动列读取项目属性，因此类型、大小、日期等列值的过滤尚未覆盖。Windows Header Control 的 `HDS_FILTERBAR` 会在每个列标题下增加文本输入框，并提供过滤通知；它不是 Q-Dir 示例中的复选值清单，只能作为功能较简单的替代方案。ExplorerBrowser 这条路径没有 `SysHeader32`，因此不能直接依赖 `HDF_SPLITBUTTON`/`HDN_DROPDOWN` 扩展表头；产品现有 UI Automation `Header → SplitButton` 路径可用于定位列头，但实际展开尚未证明。现有 `ShellColumnManager.Capture` 已从 Shell `IColumnManager` 读取可见列和对应 `PROPERTYKEY`，`IShellFolder2::GetDetailsEx` 可按 FMTID/PID 读取项目属性。实现仍应保留 ExplorerBrowser 原生视图和文件区菜单，属性读取失败时保持项目可见，并按标签保存筛选状态。来源：[Q-Dir FAQ 38](https://www.softwareok.com/?faq=38&seite=faq-Q-DIR)（[官方示例图](https://www.softwareok.com/img/faq/Q-DIR/Dateien_filtern_ueber_den_Spaltenkopf_2021-03-03-08-34-51.png)）、[Windows Header Control Filters](https://learn.microsoft.com/en-us/windows/win32/controls/header-controls)、[HDN_DROPDOWN](https://learn.microsoft.com/en-us/windows/win32/controls/hdn-dropdown)、[HDITEMW](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/ns-commctrl-hditemw)、[IShellFolder2::GetDetailsEx](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdetailsex)。

#### 2026-10-28 Private Desktop 探针结果

在不切换用户桌面的 Private Desktop ExplorerBrowser 视图中，UI Automation 能读到一个 `UIViewHeader` 和三个 `UIColumnHeader` SplitButton。把可见列从 Name/Size/Type 重排为 Type/Name/Size 后，`IColumnManager` 返回 property id `4/10/12`，UIA 列头也按“类型/名称/大小”排列，矩形随列宽同步；这为“当前列序 → PROPERTYKEY”的索引映射提供了本机证据。`IFolderFilter::ShouldShow` 对三个 fixture 项都成功 QI 到 `IShellFolder2`，`GetDetailsEx(PKEY_ItemTypeText)` 返回 3 个值，样本为“TXT 文件”“BIN 文件”；清除 filter 后项目数从 3 恢复为 3。UIA 声明支持 `ExpandCollapsePattern`，但调用 Expand 后状态仍为 Collapsed，且没有原生 `#32768` 菜单出现，不能据此认为下拉菜单可接管。探针未验证网络/云/虚拟 Shell provider、鼠标箭头区域、筛选值菜单 UI、标签保存或用户可见交互。下一步仅继续隔离检查列头点击区域与现有 Shell 下拉行为，再评审应用 Popup 的锚定/生命周期；不据此接入产品菜单。

#### 2026-10-28 UIA Invoke 与前台守卫失败

额外尝试对 DirectUI 列头 SplitButton 调用 UIA `InvokePattern.Invoke()`，该轮读到排序状态不变且没有 `#32768` Popup，但 Private Desktop 守卫报告前台 HWND 从 `0x272314` 变为 `0x59024C`，因此整轮以失败处理，不采纳其菜单行为结论。移除 Invoke 后再运行原只读能力探针，守卫仍报告 `0xE0B5E` 变为 `0x405F0` 并失败。现有证据不能把变化归因于 UIA、探针或外部活动；不得降低前台不变门槛。暂停继续运行 ExplorerBrowser 视图探针，待能确认稳定前台状态后再继续；列头真实鼠标箭头命中仍未验证。

#### 2026-10-28 列值筛选后端接线

`QuickFilterSettings` 现在保存以 `PROPERTYKEY` 标识的列值条件。一个属性所选值按不区分大小写的 OR 匹配，多属性条件按 AND 匹配；与文件名通配符同时存在时两者继续按 AND 组合。生产 `ShellFolderFilter` 仅在有列条件时调用 `IShellFolder2::GetDetailsEx`，再用 `InitPropVariantFromVariant` 和 `PSFormatForDisplayAlloc` 得到列显示文本；某属性无法读取时跳过该条件，避免误隐藏项目。会话 INI v11 把这些条件按标签写入 Base64/JSON，旧格式默认为无列条件，旧会话写回前备份；Q-Dir 扩展包含同一会话字符串，因此能往返本程序自定义条件。

这一批没有 UI 来创建、选择或清除列值，也没有在 ExplorerBrowser 生产回调里实测 Shell 属性读取/格式化；不能视为列值筛选功能已可供用户使用。下一步实现可见筛选入口，并在集中 GUI 验收中覆盖重排列、空值、过滤组合、逐标签复制/恢复和属性不可读时的行为。此前列头 DirectUI 入口的前台隔离结果仍保持失败，故本批没有改变原生表头或重复执行该探针。

#### 2026-10-28 列头箭头入口接线

`ExplorerBrowserHost` 在导航完成、Shell view 创建及视图几何改变后，通过 Dispatcher 空闲任务缓存 UIA `SplitButton` 边界，并与当时 `IColumnManager` 可见 PROPERTYKEY 顺序对应。鼠标子类回调不执行 UIA；仅当点击落入列头矩形右侧 20 个屏幕坐标单位的区域并避开最右 3 个单位的列宽调整边缘时，应用才消费按下/抬起并排队打开该列筛选清单。标题文字区仍交给 Shell 执行排序；缓存与视图代次不匹配、列头数量不同或 UIA 不可用时也完全保留 Shell 原生点击行为。列值清单支持同列多值勾选、空白值显示和清除当前列条件；应用条件通过现有活动标签 Browser 设置，保留名称筛选与其它列条件。读取值仅请求目标列并去重排序。

后台纯几何回归覆盖箭头区、标题区、列宽调整边缘、标题外区域及 PROPERTYKEY 关联；App Release build 和完整 Release SettingsSmoke 通过。没有启动 ExplorerBrowser 或 GUI，故当前 20/3 DIP 命中范围与真实 Windows 表头控件布局的重合仍未验证；列清单的深浅主题观感、空值读取、筛选后 Shell 刷新、跨 provider 可用性及多标签复制/重开也待可见验收。此前 Private Desktop 前台守卫失败的探针不重跑。
