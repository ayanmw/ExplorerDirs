# Explorer Dirs 设计草案

日期：2026-10-15

## 2026-10-27 整份视图恢复的保存保护

`_viewRestoreFailed` 取代仅针对列的标记，进入自动恢复前即置位；视图不可用、任一 setter 失败或 COM 异常均保留缓存保护。`ShellViewStateRestore` 按模式、可选显示列、可选排序、分组顺序调用原生目标，任一步 HRESULT 失败可继续尝试其他字段，但整份恢复不会标记成功；默认空列/排序仍沿用 Shell，不增设覆盖。

恢复前固定 view state/browser/read generation，每次调用前后复核，过期则停止后续写入与报错，不覆盖新访问的恢复结果。宿主只在当前恢复全部成功时解除标记；后续导航重试。ViewCaptureFailed 同时反映捕获与恢复失败，使未再次捕获的后台标签也立即阻止会话覆盖。预设应用在第一次写入前保护缓存，完整模式/列成功后再允许捕获。回归分别覆盖真实未挂接宿主的保存门控和模拟原生目标的 HRESULT/重入逻辑，真实 Shell setters 的拒绝与恢复外观仍需可见验收。

## 2026-10-27 原生导航键边界

ShellNavigationKeyRouting 统一识别 PageUp/PageDown/End/Home/四方向（VK 0x21～0x28）。原生命名编辑框在宿主快捷键和 IShellView accelerator 前直接接收这些 WM_KEYDOWN，沿用原消息及系统 Ctrl/Shift 状态；普通文件区先交 Shell，只有 S_FALSE 且当前为文件内容、非列标题/编辑框时才补发。S_OK、错误、WM_KEYUP 和 WM_SYSKEYDOWN 不进入补发。保持 Tab、确认、取消及其它编辑键原有分派；策略 smoke 不等同真实键盘、原生选区或多窗格物理焦点验收。

## 2026-10-27 数据快照与排队标签呈现分离

Browser 的 ShellNavigationState 是已确认目录来源。Pane.TabState.Path 曾是 Navigated 后更新的独立缓存，与浏览器在 Dispatcher Background 通知前可能不同；现在该属性直接派生自 Browser.CurrentPath，删除构造与通知中的重复赋值。当前目录命令、扫描、地址栏、新标签和搜索关闭从同一来源读取；界面元素仍可以保留旧呈现直到通知到达，不把界面文字作为操作目标。

SnapshotTabs、SnapshotPins、DuplicateTab、CopyTabFrom、CloseTab 的持久化/新标签路径也明确使用 Browser.CurrentPath；前两者使用同一来源，使固定元数据不因路径滞后而在恢复时失配。查看状态仍走既有捕获和失败拒绝逻辑，搜索位置仍使用真实范围目录，不保存 search URI。不为消除这个时差而同步执行界面通知或改变焦点时序。合成目录通知夹具先更新浏览器位置再通知 Pane，遵循生产的 commit-before-notify 顺序。

## 2026-10-27 原子视图快照的访问范围

ExplorerBrowserHost 在取得 IFolderView2 之前记录 browser 实例及 `_viewReadiness.Generation`，通过有效性回调交给既有 ShellViewStateCapture。捕获入口及各原生 getter 返回后检查，访问代际变化或 browser 替换即返回不完整结果；宿主只在 complete 时替换 `_viewState`，失败时沿用缓存并通过既有 ViewCaptureFailed 阻止不可靠保存/复制。保持 OnNavigationPending 对离开视图的捕获时机，不把 Pending 本身当作所有捕获失败；要求的是一次捕获内访问不变。无窗口回归仅模拟 getter 重入与代际变化，不证明真实原生回调顺序。

## 2026-10-27 关闭标签的轻量地址快照

窗格关闭栈由 TabSettings 扩为设置与 AddressPathHistory 的组合快照，关闭前复制地址 MRU，恢复时传入 AddTab，与复制标签的行为一致。快照不引用 TabState/ExplorerBrowserHost，关闭时仍立即 Dispose 原生宿主；不为恢复历史而驻留 Shell 视图。关闭栈最多 20 项，单份地址记录最多 20 项，仅在进程内存在；Shell Travel Log 的前进/后退历史不在这份快照中。未挂接真实 PaneControl 检查快照和副本的可变状态隔离，可见交互另行验收。

## 2026-10-27 新窗口的显示器归属

三类新窗口入口复用 `CascadeFrom`，在目标 SourceInitialized 后一次执行并撤销订阅；有已存窗口位置时不登记错位操作。`WindowCascade.Place` 以 MonitorFromWindow/GetMonitorInfo 取得来源屏幕工作区，GetWindowRect 与 SetWindowPos 保持屏幕像素坐标。32 DIP 错位量按来源 DPI 换算，约束左上和右下边界；窗口大于工作区时优先保留标题栏入口，不缩小窗口。定位后根据实际尺寸再约束一次，使用 NOACTIVATE/NOSIZE/NOZORDER；来源最小化时改从同屏工作区起点错位，避免使用 iconic 伪坐标，不还原来源。

此路径与持久化的 WINDOWPLACEMENT 工作区坐标互不转换。依据：[Microsoft WINDOWPLACEMENT 坐标约定](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)。普通不可见 HWND 验证原生定位，纯坐标用例覆盖模拟多屏；真实 WPF 跨 DPI 初始化、窗口显示后的坐标和显示器拔插仍需可见验收。

## 2026-10-27 已有窗口的最小化恢复

无参数单实例激活与目录转发统一调用 `MainWindow.RestoreFromMinimized`，再由各入口保留原有激活行为。仅最小化时修改 WindowState；普通/最大化按已记录的 `_lastNonMinimizedMaximized` 恢复。F11 使用普通无边框窗口，存在 `_fullScreenRestoreState` 时恢复 Normal，并保留退出时的原呈现记录。不新增配置。未显示实际窗口回归覆盖状态决策；实际激活、双屏坐标与 F11 呈现另行可见验收。

## 2026-10-27 原生位置与输入完成分离

`ShellNavigationNotice` 同时传递位置/查询/错误文本与应用提交编号。原生视图可能先完成旧目录，而新输入还在后台检查；仍发布已经稳定的实际位置以保持标签会话正确，但只有当前且未报告失败的提交可以结束编辑、重置搜索 UI、清理重试目标和恢复焦点。编号在通知排队时保留，接收和延迟聚焦时再核对；请求失败也只消费自己的意图。

Shell 回调没有单次请求令牌。相同目标仍以计数批次处理，只有最后一个登记退役时给出本批次最新输入编号，部分批次保持无可确认编号；原有 readiness 对重复目标读取实际位置的机制保留。这不推断回调先后关系，也不把合成回调检查视为原生异步导航验收。

## 2026-10-27 Shell 目录请求身份

目录可用性探测和 PIDL 解析完成后，以 PIDL 对应的持久化身份登记应用发出的导航，并使重试目标使用相同身份；原输入别名可能与 Shell 回调路径不同，虚拟目录也可能没有文件系统路径。身份读取可能重入，登记前重新检查探测代际和浏览器可用性。Pending/失败沿统一身份查找并消费计数，成功与异常也清理同一键；仅未登记的原生导航可以取代正在准备的应用请求。无窗口回调回归不覆盖 Shell 的真实投递次序或 UI 完成通知的请求归属。

## 2026-10-27 地址与搜索输入保护

地址提交后继续输入即撤销旧提交结束编辑的权限，完成通知仍更新实际标签路径。搜索框独立记录是否存在新输入，避免仅按键盘焦点决定是否覆盖；成功/失败通知只有在没有新输入或结果与当前文本相同时才能同步，重新聚焦保留草稿。显式提交先去除首尾空白，关闭搜索和切换标签重置编辑状态。不新增持久化或导航请求协议；未显示控件的合成通知回归不代表原生回调次序已验收。

## 2026-10-27 导航完成的焦点所有权

目录与搜索完成共享一次性的导航焦点恢复入口。仅当前且已完成的导航可以消费待聚焦标记；消费发生在原生恢复前，避免旧意图跨导航保留。恢复复用现有窗格、窗口活动与前台所有权检查，并尊重当前原生重命名/列标题焦点。启动先建立文件区焦点意图，后续编辑焦点可取消；搜索提交在仍拥有交互时先进入文件区，结果回调不得独立夺回输入。未挂接对象回归只证明请求消费和接线；真实异步导航及键盘操作仍需可见验收。

## 2026-10-27 现代地址栏直接编辑

点击现代面包屑的空白区域或分隔符进入完整路径编辑并全选；路径节点继续导航。使用透明背景参与命中，监听冒泡左键事件而非预览事件，避免抢走子按钮的操作。复用现有地址编辑入口、提交/取消及 Tab 停靠点，不新增设置。未显示实际控件的路由回归覆盖四窗格与经典样式；真实鼠标命中和键盘焦点另行验收。

## 2026-10-27 默认窗口位置持久化

每个窗口在自己的会话旁保存 `<session-stem>.WindowPlacement.ini`，默认启用，不增加设置开关。记录普通窗口四边坐标和最大化状态；使用原生 `WINDOWPLACEMENT` 工作区坐标成对读取/恢复，保留负坐标，不混用 WPF DIP 或 `SetWindowPos` 的屏幕坐标。位置恢复在 `SourceInitialized` 时执行；多窗口启动只对缺少有效位置记录的窗口使用旧的错位排列。

周期保存、关闭和重启写入位置。最小化时保留最后非最小化状态；进入 F11 前捕获原生位置，全屏期间仍保存该快照。新旧会话格式不变，位置为独立版本化 INI，并沿用外部编辑冲突保护、未知高版本拒写和损坏文件备份。隔离预览复制已注册窗口的位置文件。

使用 [GetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement) 与 [SetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement)；后者由系统调整完全离屏位置。无窗口 smoke 验证配置、负坐标、状态归一化和 ABI，不能替代真实双屏、DPI、拔屏和 F11/最大化重启验收。

## 2026-10-21 内嵌搜索位置修正

search-ms 作为请求元数据，使用文件夹优先绑定上下文解析并验证 Shell 文件夹属性，避免导航协议启动项。每浏览器保留已发出的查询/目录位置映射，跨原生 PIDL 规范化恢复查询；精确原生名称碰撞拒绝。搜索 URI 不写入目录会话。源码与原生组件结果已验证，真实窗口未验收，详见 [搜索记录](../../search-folder-investigation.md)。

## 2026-10-21 Q-Dir 视图包装校验

已通过本机 12.79 读写链路确认 CODEMODE1 第三字段：对完整解码数据按有符号字节
计算乘加值，零字节跳过乘法，结果为有符号 32 位溢出值。源码在加载 Windows 属性存储
前验证该值；失败沿用整份视图回退、目录与显式模式保留、导入预览警告。
不因校验正确而跳过结构或类型检查，也不把此非加密校验用于来源信任判断。

错误校验测试先红后绿；固定向量、既有属性流检查与两份真实配置只读导入通过，
原生 68 个视图字段匹配。同日已接入下述原生包装输出；Q-Dir 可见窗口读取尚未验收。
该结论不能单独证明双向兼容，见[格式依据](../../qdr-import.md#2026-10-21codemode1-校验值)。

### 原生视图导出

完整列快照通过 Windows INamedPropertyStore/IPersistSerializedPropStorage 写出 Mode、
LogicalViewMode、IconSize、GroupView/GroupByKey/GroupByDirection、Sort、ColInfo；封装已确认头部与校验。
字段使用与目录一致的窗格/标签编号，并参与基础字段哈希。列快照未知时省略原生视图字段，
继续通过扩展保留已知状态；无效状态或序列化失败则在写文件前中止。
不通过伪造默认列或截断流来适配未验证边界。

直接 Windows 属性读取、独立校验、去扩展读回、八模式和最大列数测试验证转换。
Q-Dir 某保存路径的 4096 字节上限与解码器行为分开记录：大视图的首次读取及再次保存
都需要 GUI 验收。确认框同步说明六种布局、原生视图输出条件和剩余互操作限制。

进一步以隐藏 IShellView 直接消费导出流，发现没有 LogicalViewMode 时，IconSize=256
会恢复为 48；补齐后八种预设 × 有/无分组 16 场景恢复正确。
测试通过路径不经过本程序解码器或视图 setter，使用标准顶层浏览器服务取得状态流。
验证使用显式图标尺寸；不外推到旧模式 5/7、-1 默认尺寸或 Q-Dir 实际界面。

## 2026-10-21 原生历史按钮状态：源码已接入，GUI 待验收

隐藏 ExplorerBrowser 实测支持 `DWebBrowserEvents2.CommandStateChange`：
CSC 1/2 分别报告前进/后退，首次目录、前后退、取消、新分支及同路径重复历史均与实际 PIDL 一致。
这为按钮状态提供原生来源，无需另建路径历史；原先 ITravelLogStg 服务查询仍不支持。
详见[能力验证](../../native-navigation-probe.md#2026-10-21-命令状态通知)。

生产实现范围仍是当前标签的前进/后退可用状态，不扩展为历史列表或替代 Shell TravelLog。
每实例缓存两个独立三态值（未知/启用/禁用），pending 期间继续接收通知但不允许执行导航，
完成/失败后再根据当前 readiness 发布。不能把命令状态事件当作一次导航完成回调。
异步刷新检查实例存活、当前标签和 readiness，不捕获旧窗格作为最终发布目标。
订阅不支持时保留原导航能力；销毁前退订，失败仍继续释放，旧回调不更新新浏览器。

生产使用独立 `ShellBrowserCommandSink`，声明 SDK 的完整 41 个公开事件；
非命令状态通知为空实现，不修改取消或输出参数。`ShellHistoryEvents` 仅把 S_OK
视为订阅成功；失败释放连接点，退订前断开回调，退订失败仍继续释放。
主宿主把通知排入 Dispatcher 并检查原订阅身份，窗格只刷新当前标签；
按钮与快捷键共用历史导航门控，切标签重新读取对应状态。
真实 COM 模拟退订/失败清理、迟到回调、实例隔离、pending 状态回归通过；
IDispatch 调用验证命令参数及 WindowClosing 不改变取消参数。
生产订阅器的隐藏原生 11 阶段导航状态与实际 PIDL 匹配。
可见窗口的禁用按钮焦点、Tab 顺序、双击导航、搜索/虚拟位置与真实失败仍待验收。

## 2026-10-21 Q-Dir 基础布局映射

2026-10-23 后续补齐三列/三行/四列/四行，应用 ID 9/10/11/12 分别映射原生 7/8/5/6。
当前共十二种，条带布局按每条边界生成分隔轨道，独立保存三或四项正有限权重；
保持窗格对象、隐藏标签和原生历史。三行/四行最小窗口高度为 525/630，保留旧布局的工具栏预算。
会话当前 v5，比例当前 v4，升级前保留旧原文，旧读者拒写新格式。
后续已接入 `.qdr` SP0/SP1/SP2 比例交换与扩展 v2/v3，旧 v1/v2 可读；导出、工作区收藏、导入前备份包含全部已记忆布局比例。
导入合并通过配置冲突保护后应用，同布局也重新排列。四宫格现用独立上/下排左右分隔比例。
条带交换名义份额，不承诺跨应用像素一致。可见布局、焦点及 Q-Dir 读取待验收，详见[比例方案](../../qdr-proportions.md)。以下保留此前八种/六种的演进记录。

2026-10-23 扩展为八种：追加应用编号 7（左双右大）与 8（上双下大），分别映射原生
10/2，两者原生窗格按 1、2、3、4 转换，前三个可见。沿用现有两行两列 Grid 分隔轨，
不重建窗格/浏览器；比例、选中标签与活动窗格保持。会话格式升级 v4，读取 v1～v4，
首次从 v1/v2/v3 保存前备份原文；比例格式 v2 同样保护旧 v1。旧读者必须拒绝覆盖新格式。
无窗口布局和配置检查通过；实际 GUI 与 Q-Dir 互操作待验收。以下为最初六种映射说明。

当前十二种应用布局通过 `QdrLayoutMap` 统一映射 Q-Dir 12.79 的全部原生编号 0～11；
逐项几何与编号见[布局映射证据](../../qdr-layout-mapping.md)。上下双窗格使用原生窗格 1/3，
左大右双和上大下双使用 1/3/4。导入时连同整组标签、选中索引转换窗格排列，
把隐藏窗格保留在尾部；导出基础字段时反向转换。应用扩展保持自身 pane/pin 身份，不作二次排列。

对应关系基于本机 12.79 的配置读取、命令分派、分隔矩形和 pane 绑定静态链路，
不以静态菜单文本或菜单显示顺序猜测。未知版本或未知编号仍提示未支持；本机 12.79
编号 0～11 均有映射。已记忆分隔比例及完整列快照的原生视图状态流已接入导出；
Q-Dir 实际读取、可见分隔比例/焦点及不完整列快照标签的互操作仍待验收。
原生 Focus 已按零起始窗格编号转换：只应用已知布局中可见且有导入标签的目标，
缺失/-1 不指定，非法或不可映射值提示。预览显示目标，布局设置后再应用活动窗格。
导出写回原生编号并纳入基础字段哈希；旧无 Focus 扩展仍可读取，有效扩展保持自己的应用编号。
全部可见窗格、无效/隐藏/空目标及旧扩展兼容的无窗口回归通过，实际键盘焦点仍待 GUI 验收。
独立基础字段输入、字段输出、空隐藏窗格、外部编辑后的扩展失效以及有/无扩展往返已回归；
真实 Q-Dir 保存与读取、窗口位置和原生交互仍待 GUI 验收。

## 2026-10-23 原生选择菜单

2026-10-23 选择菜单补充：编辑菜单增加全选、取消选择和反向选择，基于当前原生视图的
PIDL 快照和批量选择接口，不扫描磁盘。菜单读取和写入前验证活动标签/导航/选区，
反选分两个批次并在中间检查视图身份，明确不提供事务回滚。焦点修复保留原生 selected bit；
不能通过无条件重新选中焦点项破坏取消选择或反选。证据与 GUI 边界见[选择菜单](../../selection-commands.md)。

## 2026-10-21 新建文件夹上下文保护

2026-10-23 补充：NewFolder 纳入统一文件命令状态与调用路径，文件/编辑菜单和工具栏读取
原生可用状态。只按 canonical New 标识初始化延迟子菜单，再读取 NewFolder 状态；
父菜单或子项禁用时不启用。菜单准备后仍复查原请求，保留观察器和自动重命名流程。
隐藏 Shell 状态探测和模拟禁用回归通过，真实创建及焦点验收未完成。

保留 Shell 背景 `NewFolder` 命令与固定本地盘的短期目录监听。入口捕获浏览器、
导航版本和新建请求版本；COM 菜单准备返回后，在调用命令前确认原活动窗格、可见性、
位置可信状态及文件视图焦点意图仍成立。菜单、工具栏和 Ctrl+Shift+N 共用入口，
明确转入文件命令意图但不在执行前强行改变选区或聚焦文件项。

延迟重命名绑定原导航版本和观察器。读取焦点项及获取 Shell 视图后再次检查上下文、
选区版本和是否已进入原生重命名；用户切到其他窗格、地址栏、目录树或应用时放弃。
计时回调从 COM 返回后复查观察器身份，旧回调不能取消嵌套触发的新建操作。
本修正来自源码路径检查；真实创建、Shell 重入、连续新建与焦点恢复仍须 GUI 验收。

## 2026-10-21 默认新标签查看配置

默认菜单与当前视图共用八种 `ViewStyle` 预设，但分别维护状态。
默认值存储 `DefaultViewMode` 和 `DefaultIconSize`；更改只影响之后新建标签，
复制标签仍取当前标签的完整实时快照。未匹配预设的旧值或自定义值显示独立标记，保留原值。
小图标的系统默认尺寸在应用时规范为 16；其他显式自定义尺寸不作近似替换。

会话升级到 v3：v1/v2 默认尺寸补为 -1，v3 要求有效尺寸（-1 或 1～1024）。
首次从旧格式保存前，将加载原文保存在唯一 `.before-v3-*.bak`，再执行既有外部修改保护写入。
正常保存、导出、导入前备份和本程序 `.qdr` 扩展恢复均传递默认尺寸。高于 v3 的文件禁止覆盖。

八预设的实际文件写读、新标签模型、`.qdr` 往返、自定义尺寸、升级原文备份及损坏值检查通过。
用旧 `preview-0.6.0-temp.1` DLL 的 SessionStore 对隔离文件反射验证：
读取 v3 被拒绝，随后保存也被拒绝，文件未改变；没有启动其窗口或访问日常 profile。
真实默认菜单、新标签视图与重启显示尚未验收。旧版回退需使用升级备份副本，不自动降级 v3。

## 意图与成功标准

### 2026-10-23 窗格比例

现有十二种布局使用独立分隔行/列，WPF GridSplitter 放在原生窗格边界之外。
三窗格布局的横/竖分隔条只覆盖被分割区域；四个窗格始终保持原对象和父容器。
四宫格使用专用容器内的三个 Thumb 分隔条，允许上下两排分别设置左右比例，窗格不嵌套或重挂载。
分隔条不加入 Tab 顺序，内容轨道设置最小尺寸，顶部布局菜单提供恢复当前布局均分。

每个布局分别保存分隔比例，独立 `LayoutProportions.ini` 放在会话配置旁，
使用既有原子写入和外部修改检查。缺失配置使用均分；损坏文件先备份并保留有效布局；
未来版本停止写入。只保存拖动完成或主动重置，普通窗口缩放及周期会话保存不改写比例。
写入失败时布局在本次会话生效并在状态栏报错。初版不交换 `.qdr` 比例；后续已按上文接入，导入比例采用先保存再应用的语义。

无窗口测量/排列、配置回归及构建通过；原生 HWND 的实际缩放、焦点/历史、DPI、
鼠标拖动手感和重启外观需单独用户验收。2026-10-23 用户明确要求不使用 Computer Use。

用户需要一个开源、可持续修改的 Windows 文件浏览窗口替代品。Q-Dir 式多布局和多标签必须是核心能力；应能在崩溃后恢复工作上下文，且用户/机器环境变量改变后无需重启即可让新启动的命令行获得更新。最终应能逐步接管 Win+E 和文件夹打开，但不接管桌面、任务栏或登录 Shell。

## 路线选择

2026-10-16 用户明确继续对齐 Q-Dir 核心操作，最终以原版 Explorer 体验为准。执行优先级与验收差距见 [核心操作对照](../../shell-parity.md) 和 [路线图](../../roadmap.md)。复用 Shell 操作接口，宿主负责多窗格、标签、消息/焦点路由与便携状态；不能以控件接入替代操作验收。当前标签查看快捷键 Ctrl+Shift+1～8 与顶部菜单共用 `ViewStyle`，通过 `IFolderView2::SetViewModeAndIconSize` 只修改模式与图标大小，随后读回真实设置，沿用现有每标签保存路径；搜索结果的查看状态仍只在运行时保存。Ctrl+1～9 继续选标签，Ctrl+Shift+9 暂不额外接管。

| 方案 | 优点 | 代价 | 结论 |
| --- | --- | --- | --- |
| 基于现有开源管理器二次开发 | 已有成熟文件操作和 Shell 兼容性 | 需接受原项目架构、许可和升级成本；四窗格与环境刷新未必容易嵌入 | 保留为后续重新评估选项 |
| 自建 .NET 10/WPF Windows 客户端 | 本机 SDK 与运行时已具备；四窗格和标签模型可以小步实现；Windows 消息/进程环境容易接入 | 需逐步补全 Shell 行为和文件操作 | **当前采用** |
| WinUI 3 新项目 | 现代 Windows 外观与生态 | 引入额外工作负载和部署复杂度，初期不能直接改善核心痛点 | 暂不采用 |

## 模块与数据流

- `MainWindow` 从同一组布局定义生成顶部菜单和快捷图标，管理活动窗格、环境刷新入口和会话保存调度。布局定义目前包括单窗格、双窗格（横/竖）、三窗格（两式）和四窗格。
- `PaneControl` 管理一个窗格内的标签、地址栏和导航；各窗格互不共享标签状态。标签操作通过顶部菜单、快捷按钮、右键菜单及键盘快捷键进入，中键或双击标签标题可关闭。
- `ExplorerBrowserHost` 为每个标签承载 Windows Shell 视图，新标签默认详细信息，可在工具 → 选项修改。Shell 视图负责目录枚举、文件/空白处命中、图标、文件关联和查看/排序/分组/新建/属性等原生菜单。宿主在原生菜单最前面插入标签和路径命令；布局只在顶部菜单与图标中。WPF 键盘消息先通过 `IShellView::TranslateAccelerator` 转发，未处理的 Ctrl+A/C/V/X/Z 映射到 Shell 视图原生命令；实际文件快捷键仍需 GUI 验收。右键扩展仍在本进程内运行。
- 项目前置菜单的“在新标签页中打开”取单个 Shell 选中项的 `SFGAO_FOLDER`、`SFGAO_FILESYSTEM` 和 `SFGAO_STREAM` 属性；只对普通文件系统文件夹显示，不在菜单构建时另外同步执行 `Directory.Exists`。属性常量与 `IShellItem::GetAttributes` 的用法参照 [微软 SFGAO 文档](https://learn.microsoft.com/en-us/windows/win32/shell/sfgao) 和 [GetAttributes 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem-getattributes)。属性分类可做无窗口回归，真实 Shell 扩展菜单和慢速目录仍需 GUI 验收。
- 原生文件视图聚焦时，宿主先识别应用级导航/标签/焦点遍历按键，再把其他键交给 Shell。每标签复用 ExplorerBrowser 的 TravelLog，`BrowseToIDList` 使用 `SBSP_NAVIGATEBACK` / `SBSP_NAVIGATEFORWARD` / `SBSP_PARENT`。Backspace 与 Alt+↑ 走父目录；Alt+←/→ 走历史。Tab / Shift+Tab 由 `FocusTraversal` 按正反向顺序经过每窗格导航按钮、地址、选中标签标题、详细信息列标题和文件项，再跨到下一窗格。F6 / Shift+F6 复用同一焦点目标，只跳地址、标签标题、可用的列标题及文件区，并跨可见窗格循环；这是依据[微软主要区域键盘导航说明](https://support.microsoft.com/en-us/accessibility/windows/use-a-screen-reader-to-explore-and-navigate-file-explorer-in-windows)在当前界面结构中的对应设计，目录树和详情侧栏尚不存在。列标题由 Shell UI Automation 元素聚焦；非详细信息视图跳过该停靠点。`MainWindow` 按布局记忆活动窗格，并在布局切换后恢复视图焦点。快捷键范围和人工验收见 `docs/keyboard-shortcuts.md`。
- Shell 文件视图外层 `SHELLDLL_DefView` 不一定接收文件键盘输入。焦点恢复先定位其 `DirectUIHWND`（兼容传统 `SysListView32`），设置 Win32 焦点后通知 `IShellView::UIActivate`，并在 Shell 尚未恢复文件项焦点时以 `IFolderView::SelectItem` 重新聚焦现有选中项而不清除其他选择。剪贴板快捷键处理后延迟检查活动视图焦点；Shell 确认对话框关闭、主窗口重新激活时再恢复。如果 Shell 操作结束后前台窗口为空且 WPF 仍认为本窗口活动，可重新声明本窗口为前台；其他应用已有前台焦点时绝不抢焦点。地址栏、工具栏和其他窗格的显式焦点优先，避免异步恢复抢焦点。
- `ShellIconProvider` 仅为项目自己的标签标题取文件夹图标并释放本机图标句柄；文件视图内的图标由 Shell 绘制。
- 新建文件夹通过当前 `IShellView` 的背景 `IContextMenu` 执行 Shell 的 `NewFolder` 命令；文件菜单和工具栏也可触发该操作。Ctrl+Shift+N 只作为本程序局部快捷键，不能覆盖其他程序注册的全局热键；不安装全局键盘钩子。隔离 GUI 布局冒烟脚本须显式授权、使用独立测试配置和 UI Automation 按钮调用，不注入系统按键、不移动鼠标、不改剪贴板、不强制窗口置前；每次调用前核对前台进程，焦点转移立即停止交互。实际执行须等用户留出空闲桌面。
- 自动重命名不再在 UI 线程先后执行 `Directory.EnumerateDirectories`。固定本地盘在调用 Shell 命令前短时启动 `FileSystemWatcher`，只记录直接子目录的创建通知；最多等待约 1.4 秒，只有唯一候选与 Shell 当前焦点路径一致才请求 `IShellView::SelectItem` 进入编辑。监听失败、多个候选、焦点变化或导航离开时停止，避免误改已有目录；网络和非固定盘只执行 Shell 命令。观察器可通过本地临时目录做无窗口测试，Shell 焦点和自动重命名仍需 GUI 验收。[微软 Created 事件说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.created?view=net-10.0)指出事件可能重复；[Error 事件说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.error?view=net-10.0)说明缓冲区或远程连接可能失效。
- `SessionStore` 将全局布局、活动窗格、默认新标签查看方式和各窗格按显示顺序选中的标签，以及各标签路径、查看模式、图标大小、排序列及分组写入便携 INI，使用临时文件加覆盖移动避免半写入。切换标签或活动窗格触发延迟保存；旧 INI 缺少活动位置字段时默认首个且不视为损坏。发布版各版本共享项目根目录 `profile/ExplorerDirs.ini`；复制单个发布目录时使用其自身 `profile/`；Debug 写入 `src/.state/ExplorerDirs.ini`。首次启动从旧 JSON 单向迁移布局和路径，保留旧文件。加载时校验标签数量与区段完整性；损坏时先备份原文件，再恢复可解析标签并在窗口状态栏提示。无法备份或遇到高版本格式时禁止覆盖原文件；即使重复的 `FormatVersion=1` 跟在较新版本号后面，也保留高版本保护。Shell 视图使用 `EBO_NOPERSISTVIEWSTATE`，不再调用 `SetPropertyBag` 保存自身状态到注册表。
- `CrashLog` 在配置目录下写托管异常 JSON 报告，只记录异常类型、HRESULT 和不含源码路径的调用方法；不序列化异常消息、`Data`、当前文件路径或环境变量。同一异常对象只写一次，日志写入失败不能掩盖原异常。原生 Shell 扩展故障仍需独立的进程隔离或 Windows 故障报告方案。
- 独立只读脚本 `scripts/crash-summary.ps1` 用 `Get-WinEvent` 查询最近的 Windows `Application Error` 1000 事件，可选择 Explorer Dirs 或 Q-Dir。解析 XML 中的应用名、版本、模块名、异常码、程序路径和进程身份；本项目只以路径判断“项目内发布版 / 源码测试版 / 其他副本”，Q-Dir 仅标记为对标程序。报告分别给出事件数与进程 ID 加创建时间去重的进程数；缺少完整身份时不猜测合并。输出不保留路径或进程身份。`abrupt-*.json` 标记只属于本项目且另行计数，不能视为已证实的崩溃；模块名也不能直接证明 Shell 扩展致因。[微软 Get-WinEvent 示例](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-winevent?view=powershell-5.1)、[应用崩溃事件说明](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-crashing-behavior)。
- 当前 ExplorerBrowser 宿主仍与 Shell 扩展同进程，原生崩溃无法仅靠 `CrashLog` 捕获。[可靠性隔离与恢复设计](../../reliability-isolation-design.md)按“真实窗口基线与转储 → 有界整窗恢复原型 → 必要时的窗格进程隔离原型”安排验证；不把 WER 已加载模块清单当作致因证据。当前无转储且 GUI 未验收，未启用自动重启或改变 Shell 宿主架构。
- `scripts/wer-dumps.ps1` 将 WER 采集限制在指定程序的 `LocalDumps\<exe>` 键，默认 `Status`/`Preview` 只读；真正启用需管理员和显式诊断确认。配置 `REG_EXPAND_SZ` 的用户 LocalAppData 目录、`DumpType=1` 小型转储与 `DumpCount=2`；所有权标记同时绑定目录和配置值，外部修改后拒绝删除，停用不清理转储。隔离 HKCU 根路径回归通过；未修改真实 HKLM。此项仅是 Windows 诊断设置，和应用便携 INI 无关。[微软 WER 参数文档](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps)。
- 只读 `scripts/perf-sample.ps1` 指定现有 PID 后调用 `Process.Refresh()` 周期采样 `WorkingSet64`、`PrivateMemorySize64`、累计 CPU 时间、句柄和线程；附可执行文件 SHA-256 便于比较构建，不输出路径或命令行。PID/进程名核对阻止误采其他程序，进程退出时保留已采样片段。CPU 百分比按相邻采样的 CPU 时间差、实际墙钟时间及逻辑处理器数计算。[微软工作集属性说明](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64?view=net-10.0)、[私有内存属性说明](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.privatememorysize64?view=net-10.0)。隔离 GUI 冒烟另用秒表记录测试进程启动至窗口和首次文件项、UIA 按钮调用至上级/后退后的文件项，以及搜索提交至状态和结果项；仅在用户留出的空闲桌面显式启动，输出不含浏览路径的 `performance.json`。这些数据包含 UIA 轮询开销，属于单次探索性测量，不能代替发布版长期曲线。当前仅控制台进程回归通过，真实应用的长期曲线与导航响应仍待测量。
- `RunMarkerTracker` 在同一 `crash-logs/` 目录为每个实例原子创建独立运行标记；正常退出先将其改名为 `finished-*` 再删除，以免删除失败导致下次误报。下次启动读取 `run-*` 中的进程号与启动时间，只有确认对应进程已结束才改名为 `abrupt-*` 并显示未正常退出提示；若目标归档名已存在，则保留旧报告并使用带随机后缀的新名字，避免同一标记在后续启动反复提示。其他存活实例的标记保持不动，损坏标记原样保留。文件仅含 PID、启动时间与版本，不能确定是崩溃、强制结束还是某个 Shell 扩展造成。真实原生崩溃后的行为待隔离 GUI 验收。
- `FavoritesStore` 在同一配置目录单独保存 `Favorites.ini`，使已有预览版不会在写会话时移除收藏夹。顶部菜单为当前活动窗格添加、移除及打开收藏目录；收藏列表去重、最多 200 项。文件损坏时先备份并恢复可读条目，高版本格式与其他实例的修改不能被旧状态覆盖。同版本收藏夹写入也通过 `PortableIniWriter` 的路径锁串行化。菜单打开目录后仅在本窗口和原窗格仍活动时恢复文件视图焦点；真实窗口交互待验收。
- `UiPreferencesStore` 在同一配置目录单独保存 `UiPreferences.ini`，避免旧预览版保存会话时清除外观选项；沿用 `PortableIniWriter` 的外部修改检测和进程间写入保护。全局紧凑开关位于顶部“视图”菜单，默认标准间距；切换仅改变 WPF 工具栏、窗格边距、导航按钮、标签标题和状态栏的尺寸，不重新创建 ExplorerBrowser，也不增减 Tab 焦点停靠点。配置格式与损坏备份可在控制台验证，四窗格的真实可读性、焦点和滚动仍需 GUI 验收。
- `PinnedTabsStore` 在同一配置目录单独保存 `PinnedTabs.ini`，避免旧预览版重写会话时删除固定标记。每项记录窗格、显示顺序位置和路径；加载时仅匹配这三者一致的标签，旧版改变会话后不把固定状态误套到其他路径。固定标签排在窗格左侧，复制时继承固定状态；中键、双击、Ctrl+W 和“关闭其他标签”跳过固定标签，需先取消固定。拖动仅在固定组或普通组内重排。会话成功保存后才写固定状态；文件损坏先备份，高版本或外部修改拒绝覆盖。真实窗口交互和重启恢复待 GUI 验收。
- `App.OnStartup` 解析一个目录参数；`StartupDirectory` 仅用 `Path.GetFullPath` 规范化路径，不同步探测目录是否可达。`FolderOpenRouter` 以配置绝对路径、用户身份和权限级别派生 v1 命名管道名，使用 `PipeOptions.CurrentUserOnly` 和每配置锁文件限定接收进程；请求有大小与绝对路径校验，接收窗口在第一窗格新增标签后才回执。客户端只将准确的 `1`/`0` 回执认作成功/拒绝，未知字节及传输中断保持“不确定”，避免误判为明确拒绝；启动进程收到成功回执即退出，服务不可用或未确认时保留目录参数并独立打开，状态栏提示可能的保存冲突。路由器关闭会取消接收回调的未完成等待并释放配置锁；仍在运行但窗口始终不就绪时，单实例管道可能排队，不能把客户端超时当成安全重试许可。无参数启动仍可创建另一个窗口，依靠会话写入冲突保护。多个参数、选项样式或非法路径只恢复原会话并给出提示。命名管道的同用户及 Windows 权限级别限制见[微软 PipeOptions 文档](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions)；真实窗口激活、标签创建与离线 UNC 行为待 GUI 验收。此流程不改变 Win+E 或文件关联。
- `scripts/folder-verb.ps1` 作为第一阶段系统入口，仅在每用户 `HKCU\Software\Classes\Directory\shell\ExplorerDirs.Open` 和 `Drive\shell\ExplorerDirs.Open` 建立非默认菜单项，命令格式为 `"<发布版 exe>" "%1"`。`Status`/`Preview` 不写注册表；启用需发布版同目录能力清单中的 exe 哈希匹配、操作者提供该哈希并显式确认已验收；现有 `preview-0.5.2` 因无能力清单不能启用。卸载前逐项验证所有权标记、菜单值和命令，若被外部改写则拒绝删除。隔离 `HKCU\Software\ExplorerDirs\Smoke\<GUID>` 下已验证启用、重复执行、冲突保护和卸载；真实 Shell 菜单与目录参数启动待 GUI 验收。此阶段不更改 `open` 默认动词或 Win+E。[微软的每用户类注册表说明](https://learn.microsoft.com/en-us/windows/win32/sysinfo/hkey-classes-root-key)、[Shell 菜单动词及 `%1` 引号要求](https://learn.microsoft.com/en-us/windows/win32/shell/context)。
- `SessionStore` 读取时保留原 INI 内容；`PortableIniWriter` 以配置绝对路径派生的进程间 Mutex 串行化同版本会话与收藏夹写入，锁内再次读取并比较；内容变动则拒绝覆盖并由窗口状态栏提示重启。临时文件名含随机标识，写入成功后才更新内存中的已见内容。旧发布版未参与此 Mutex，跨版本同时运行的最后时刻竞争仍需通过发布策略和实机验收约束；控制台回归覆盖外部编辑、两个新实例同时从空会话配置写入及收藏夹被持锁时拒写。
- `EnvironmentRefresh` 只读 Windows 用户与机器环境注册表，以 `DoNotExpandEnvironmentNames` 取得 `REG_EXPAND_SZ` 原始值，避免旧进程环境参与展开；再用新合并快照解析引用，机器与用户 PATH 按顺序合并，并从本进程移除运行期间曾观察到、现已删除的持久化变量。`REG_SZ` 保持字面值；进程独有变量仍可作为引用回退。应用刷新计划与读取注册表的步骤分离，修改进程环境前先校验所有名称和值；任一步写入失败时恢复本轮已完成的变更，若恢复也失败则明确报错。合成计划回归在独立测试进程里验证删除旧变量、设置新变量及无窗口 `cmd.exe` 继承，另用注入失败验证回滚和超长值拒绝；测试后恢复原进程变量。真实通知仍待 GUI 验收。官方行为依据：[RegistryKey.GetValue](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registrykey.getvalue?view=net-10.0)、[GetEnvironmentVariables](https://learn.microsoft.com/en-us/dotnet/api/system.environment.getenvironmentvariables?view=net-10.0)、[SetEnvironmentVariable 的长度限制](https://learn.microsoft.com/dotnet/api/system.environment.setenvironmentvariable)。
- 标签恢复不以 `Directory.Exists` 结果替换保存路径；该 API 在无权限、磁盘故障等情况下也会返回 `false`。[目录存在性说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.exists?view=net-10.0)。导航前的后台预检现在用 `File.GetAttributes` 区分真实目录与普通文件，权限异常则让 Shell 自行处理；UNC 共享根目录遇到属性获取失败时才用 `Directory.Exists` 兜底。[属性 API 与异常说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.getattributes)、[UNC 共享根目录的属性限制](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileattributesw)。代际序号使过期结果失效；Shell 导航回调按目标路径区分本程序请求与用户在文件视图中的导航，因为首次导航回调同步、后续回调可异步。[WPF 后台工作](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)、[ExplorerBrowser 回调顺序](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowserevents)。Shell 路径解析与实际绑定仍在 UI 线程，需要另行测量。
- 每个 `PaneControl` 的标签标题用单行 `ItemsPresenter` 与水平 `ScrollViewer` 承载，选中标题请求滚入可见范围；左右滚动按钮和标题上的鼠标滚轮只改变标签栏位置，按钮不占据现有 Tab / Shift+Tab 焦点顺序。[WPF TabControl 模板说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/tabcontrol)。`TabControl.SelectedContent` 只承载当前内容，可能在切换标签时卸载旧 `HwndHost`；现改为标题与内容分离，`PersistentTabHost` 把已访问 ExplorerBrowser 留在同一可视树，只切换 `Visibility`。主窗口的 `PaneLayoutHost` 也让四个窗格始终附着，仅隐藏当前布局不用的窗格。[WPF HwndHost 源码](https://source.dot.net/presentationframework/System/Windows/Interop/HwndHost.cs.html)显示可见性变化会隐藏/显示托管 HWND，来源变化会触发重建或重新挂接。无窗口回归覆盖可视树对象保留；原生句柄和 TravelLog 待 GUI 验收。
- 标签拖动使用 WPF 的 `DoDragDrop` 和仅在本窗格有效的临时令牌；Drop 按标题左右半区决定插入前后。[WPF 拖放机制](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)。`TabHeaderPanel` 只重排标题的布局坐标，不从 `TabControl.Items` 删除或插入选中标签；`PersistentTabHost` 独立保持 Shell 内容。`PaneControl` 单独维护显示顺序，用于 Ctrl+Tab、标题方向键和会话快照；关闭选中标签时改选显示顺序中的相邻标签。排版与排序边界可无窗口测试，Shell 生命周期及真实拖放仍需 GUI 验收。
- 地址区域默认展示可点击的现代面包屑；用户可从“工具 → 选项 → 地址栏样式”切换为持续显示完整路径的经典样式，选择保存在独立 `AddressBar.ini`。编辑时切换到完整路径 `TextBox`；Alt+D、Ctrl+L、Tab 与右侧“编辑”按钮进入路径输入，Esc 取消；地址栏右侧独立历史下拉按钮与 F4 打开当前标签最近访问路径菜单，不覆盖输入框的原生右键菜单。`BreadcrumbPath` 用 [Path.IsPathFullyQualified](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.ispathfullyqualified) 与 [Path.GetPathRoot](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getpathroot) 拆分驱动器和 UNC 分享根目录，不在展示阶段探测目录存在性；相对或无法解析的路径保留为一个整体段。点击段只导航当前标签。导航成功后更新当前样式显示，失败时保留当前路径并在状态栏显示原因；只有当前窗格仍活动时才恢复文件视图焦点。地址历史按钮加入每窗格 Tab/Shift+Tab 顺序；焦点映射回归和构建验证通过，实际鼠标、键盘菜单交互及四窗格排版待 GUI 验收。
- 当前目录搜索使用 Windows 原生 `search-ms:` 范围查询位置，继续由 Search Folder 承载 Shell 结果视图；URL 参数分别编码，非文件系统 PIDL 不进入普通路径解析。`ExplorerBrowserHost` 在初始化前设置 `ShellBrowserSite`，提供 `ICommDlgBrowser2` 的 `CDB2GVF_NOINCLUDEITEM`；`ShellNavigationState` 分别保存目录路径与运行时搜索位置，导航完成时识别搜索 PIDL。每窗格搜索栏接受 F3/Ctrl+E/Ctrl+F、顶部菜单或工具栏，Enter 或窗格搜索按钮导航结果，Esc 返回目录；可见搜索控件加入正反向 Tab 顺序。会话只保存范围目录，不保存搜索查询。Shell 视图是否取用该站点、结果右键与文件操作、历史和焦点仍待真实窗口验证。依据、风险与验收见 [搜索设计调查](../../search-research.md)。
- `EnvironmentRefresh` 合并 Windows 机器和用户持久化变量（PATH 按机器后用户拼接），收到 `WM_SETTINGCHANGE` 的 `Environment` 通知或手动触发时刷新本进程。直接创建的 `cmd.exe` 继承更新值。已有进程和通过文件关联启动的程序不在此承诺内。
- `TerminalLauncher` 通过后台任务启动系统目录下的 `cmd.exe`，工作目录取点击时的活动窗格路径，不在 UI 线程预先调用 `Directory.Exists`；窗口关闭时取消尚未启动的任务。失败时只在本窗口仍活动时显示对话框，否则留在状态栏，避免延迟错误抢占其他应用。`UseShellExecute=false` 下相对程序名依赖 PATH 的依据见[微软 ProcessStartInfo 说明](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute)，后台处理阻塞工作的依据见[WPF 线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)。真实网络工作目录、命令行环境继承和关闭窗口竞态仍待 GUI 验收。

## 后续文件操作原则

Shell 视图已提供系统文件操作菜单与键盘行为。先验收复制、移动、删除、重命名、撤销和回收站语义，再决定是否为跨窗格操作单独调用 Windows [IFileOperation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)。其 COM 调用需要 STA。任何破坏性操作须确认目标与结果，失败时不得宣称成功。

## 可靠性与兼容性

- 2026-10-19 用户要求先交付临时预览：使用 `releases/preview-0.6.0-temp.1/app/` 的嵌套便携目录，复制日常 INI 作为起点并独立保存，避免 v2 升级影响 0.5.2。临时包注明完整 GUI 验收未完成、删除后焦点修复待复测；不降低后续可用发布版的验收要求。试用反馈后再调整路线图，详见[临时版说明](../../releases/preview-0.6.0-temp.1.md)。

- 2026-10-18 用户新增 `.qdr` 导入：按窗格追加目录和标签、支持已确认的四窗格布局与查看方式；先展示数量和缺失项，再备份当前完整会话并应用。已核对的 CODEMODE1 包装通过 Windows 属性存储接口读取排序、分组、图标大小和列设置；任一字段失败整份视图扩展回退。保留现有标签和来源文件，不执行配置命令。未知布局与视图结构明确报告未支持；[导入范围](../../qdr-import.md)。用户同时开放完整 GUI 测试，当前因 Windows 锁屏待解锁，不能据无窗口通过宣称已验收。

- 2026-10-18 列设置扩展：`ViewState.DisplayColumns` 保存按可见顺序排列的属性键与宽度，`IColumnManager` 负责读取和恢复。无列接口的视图保留旧缓存，失败或不完整读取阻止当前会话保存；恢复失败同样阻止用部分结果覆盖缓存，重新导航重试。INI v2 可读 v1，首次保存前备份旧原文；旧版不能直接降级读取 v2，回退必须显式选用备份。详细限制与验收见 [列设置](../../column-layout.md)。

- 布局切换不销毁隐藏窗格；目录慢或不可读时由 Shell 视图处理，真实网络盘和无权限目录仍需人工验收。
- 每次路径/标签/布局变化延迟保存会话，另每 3 秒轮询原生视图变化；异常关闭后应恢复最近一次已保存状态。复制标签先抓取原生视图的实时设置。
- INI 格式带版本号，支持旧 JSON 单向迁移；损坏配置先备份再恢复可解析标签，实际窗口提示仍待验收。
- 第一阶段只支持普通路径；网络盘、Shell 虚拟目录、长路径和重解析点需要明确验收样例。
- 系统入口接管须提供退出与恢复方法，并在独立里程碑中测试；不修改 `explorer.exe` 或登录 Shell。
- 可使用构建发布到 `releases/<version>/`，每个目录不可覆盖；发布脚本使用唯一暂存目录和拒绝覆盖的同卷目录移动，目标目录若并发出现则失败并保留暂存文件。`src/` 中的二进制只供开发和调试。原生 Shell 菜单及第三方扩展在进程内执行，异常隔离和菜单消息的人工验收仍待完成。

当前多标签补充：Ctrl+1～9 在当前窗格按标题显示顺序选中对应标签；超出标签数时保持原选择。该映射与 Ctrl+Shift+1～9 分开，后者仍交由原生 Shell 视图处理查看样式。标题拖动重排后的数字位置和真实焦点需 GUI 验收。

周期会话保存从每 3 秒查询所有标签的 Shell 视图，调整为只查询当前可见标签；切换标签和布局前捕获即将隐藏的视图，隐藏后从每标签缓存序列化配置。导航完成后的实时视图不可用、Shell 读取查看/排序/分组接口返回失败码、排序列数量无效或列数据不完整时，保留上次完整缓存并停止写入会话 INI，直到该标签再次完整捕获；复制标签也拒绝使用失败捕获的缓存。视图捕获判断从 COM 宿主中拆出，模拟读取源的无窗口回归覆盖失败与恢复；真实 COM 取值仍待 GUI 验收。关闭标签时仍尽力捕获其视图供“恢复关闭的标签”使用。此优化的卡顿改善、视图设置保存精度和原生视图生命周期仍需真实窗口验收。

## 2026-10-20 功能对齐与主题方案

用户认可临时版基本功能，要求顺序为：菜单、工具栏、导航、文件视图与状态栏功能 → 组件操作体验 → 主题和图标个性化。以下为待实现设计；不改变既有文件操作与发布验收门槛。

### 文件视图与命令入口

- Ctrl+滚轮复用当前标签的原生查看方式与图标大小接口。先观察 Explorer 对各查看模式的真实过渡及本宿主消息路由，不直接把八种菜单模式当作循环列表；如果 Shell 已处理，不再二次执行。考虑不足一个滚轮刻度的连续输入，普通滚轮仍由原生视图滚动。
- 查看切换保留选中项、键盘焦点、排序、分组及历史；结果进入现有每标签快照，复制标签继承，新标签默认方式不变。多窗格验收包括鼠标悬停窗格与键盘活动窗格不同的情况，避免操作错误视图。
- 菜单、工具栏和快捷键绑定同一 Shell 命令及状态。补齐常用文件命令入口时，覆盖无选择、多选、地址编辑、不可写位置、确认取消及命令失败；不另建复制/删除实现。
- 状态栏先展示总项目数、选择数量与可取得的文件大小，并提供查看方式快捷入口。磁盘可用空间仅在适用位置查询；不为了合计大小遍历目录。慢查询应异步、可丢弃过期结果，不能把未知数值当成零。

首批统计已接入：通过 [IFolderView.ItemCount](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-itemcount) 的 ALLVIEW/SELECTION 读取现有视图集合。可见窗格在所属 STA 每 750ms 查询两次计数，不发起后台目录枚举；隐藏/卸载停止定时器。S_FALSE、错误码、负数或选择数大于总数返回未知；导航期间不读，COM 消息重入造成导航代际或当前标签改变时不展示旧快照。统计文本与原状态提示分开，避免刷新覆盖错误。此路径仍有 Shell COM 调用耗时风险，网络/大型视图需实测；没有据无窗口回归承诺界面响应时间。选择集可取得大小和容量查询见下方追加实现。

菜单/工具栏首批已接入剪切、复制、粘贴、删除、重命名、属性、撤销。通过 IShellView.GetItemObject 取得选区或背景 IContextMenu，以 `CMF_CANRENAME | CMF_OPTIMIZEFORINVOKE` 查询，用 [GetCommandString](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-getcommandstring) 的语言无关 verb 识别并检查原生菜单禁用位；InvokeCommand 使用同一菜单的命令偏移。选区属性命令在未选中项目时禁用，粘贴/撤销指向背景。菜单展开或进入工具栏更新状态；点击重新构造上下文，不复用过期菜单对象。执行前检查目录代际、Shell 报告的选择版本和当前可见/活动窗格，准备期间变化则取消。地址编辑快捷键路由保持原样；正在原生重命名时不查询命令菜单。此路径仍会运行进程内 Shell 菜单处理器，不能声称隔离第三方扩展或已经验证性能。模拟 COM 菜单覆盖多语言标题、禁用项、命令偏移（含 0）与释放，真实文件操作、各位置暴露的 canonical verbs 和菜单退出焦点仍待验收。

### 文件区颜色缺陷与主题边界

用户确认：导入 `.qdr` 后**只有文件列表区域变黑**，重启后白底。当前导入器没有颜色字段，`UiPreferencesStore` 只保存紧凑密度；因此没有证据表明应用导入了 Q-Dir 主题，也不能仅据现象断言是 Windows 系统主题导致。

先在隔离配置下比较同一路径的现有标签、新建标签、导入标签、复制标签和重启恢复；记录实际系统应用配色、视图创建/恢复顺序以及 Shell 内容区和 WPF 外壳的颜色。修复以可重复的差异证据为依据，不用每次导入后强制刷白掩盖问题。

全局主题方案：

1. 选项提供“跟随系统（默认）/浅色/深色”。INI 保存用户选择，运行时再解析系统配色；不能把 System 自动保存成当时的 Light 或 Dark。应用不写系统主题注册表。
2. 建议扩展外观 INI 为有版本迁移的格式，保留紧凑选项，升级前备份，沿用外部编辑及高版本保护。具体格式在实现时确认；会话每标签查看/排序/分组不与全局主题混存。
3. 系统配色变化、新建/导入/恢复视图使用同一策略；导入不得隐式修改全局主题。高对比度模式优先保证可读性，系统强调色与浅深色偏好分别处理。
4. 分别验证 WPF 菜单/工具栏和原生 ExplorerBrowser 内容区。WPF 资源主题或深色标题栏并不能保证 Shell 文件区同步变色。先做公开接口能力验证；若某系统版本不能统一，明确支持范围和回退，不提供显示成功却只有部分区域变化的选项。
5. 基础颜色一致性作为缺陷提前处理；完整主题选项按功能与组件对齐后的阶段交付，自定义配色与按钮图标套装最后实施。

验收矩阵：三种主题选择 × 新建/复制/导入/重启恢复；再覆盖运行中系统浅深色改变、隐藏窗格重新显示、高对比度和只读/损坏配置。颜色一致之外还必须保持文字、选中项、禁用项可辨认，且不销毁已有视图历史或抢走焦点。

### Q-Dir 状态导出与扩展

同日追加需求：在功能补齐阶段增加 `.qdr` 状态导出，并持续完善布局和标签导入兼容。优先让本程序正确读取 Q-Dir；导出采用已验证的原格式字段，并允许独立标识、版本化的本程序扩展。Q-Dir 无需支持本程序个性配置。工作区快照包含隐藏窗格，保留标签顺序与当前标签；现有导入预览、备份、追加语义保持。未知布局值与私有视图包装不猜测写出；Q-Dir 输入兼容、本程序往返和 Q-Dir 读取导出分别验收。详细范围见 [Q-Dir 状态文件导入与导出](../../qdr-import.md#状态导出与兼容方向2026-10-20-新增需求)。

当日已接入导出入口和 UTF-16 明文基础块；根据官方德文 FAQ 117 的显式标注，当时基础字段仅接入四窗格 0 和单窗格 11；2026-10-21 已扩展至本文开头列出的六种布局，依据为独立几何核查而非菜单顺序。本程序扩展 v1 保存完整会话 INI 与固定标签，使用基础字段 SHA-256 绑定防止外部编辑后恢复陈旧快照；未知/损坏扩展整份回退并提示。完整快照校验后才写出，覆盖前复查目标内容；导入前同时保留 INI 与完整 `.bak.qdr` 备份。固定标签插入后按对象身份选中导入标签；扩展中的全局项仅含新标签默认查看方式，预览明确告知，主题不受影响。无窗口六种布局/视图/固定标签/冲突回归通过；实际窗口与 Q-Dir 读取未验收，未对应应用预设的原生布局仍为缺口；原生视图输出已按本文开头方案接入，Q-Dir 实际互操作继续待验收。

## 首版验收

1. 单/双/四窗格切换及每窗格标签导航可用，重启后路径和布局恢复。
2. 改变持久化用户环境变量后，应用收到系统通知或手动刷新；新打开的内置命令行看到新值，无需重启应用。
3. 无权限目录、已删除目录和慢目录不导致整个窗口崩溃或长时间无响应。
4. 后续在文件操作完整、恢复路径验证、异常遥测和回归测试就绪后，才评估 Win+E/文件夹关联接管。

状态栏查看入口已接入：各窗格提供八种查看方式，与顶部菜单共用定义和勾选逻辑。菜单展开时读取当前 IFolderView2 模式及图标大小；读取失败、导航中或原生重命名中禁用选项，非预设图标大小不伪装为相近预设。切换仍走现有 Shell 接口，不改新标签默认方式；按钮不增加 Tab 停靠点，键盘使用既有顶部菜单及快捷键。COM 返回后检查当前标签身份，避免对已切走的标签恢复焦点。多窗格目标、菜单退出焦点、原生右键更改后的勾选与窄窗格排版仍待真实窗口验收。

2026-10-21 原生测试修正小图标默认尺寸：本机 mode=2/size=-1 返回成功但保持旧图标模式；
预设及新建默认状态使用 2/16，旧保存状态在应用时仅对 2/-1 转为 2/16，不覆盖显式尺寸。
八种预设在名称降序、类型升序分组/无分组两套基线下保留选区身份及排序/分组，
未新增导航。Ctrl+滚轮输入、可见绘制和实际配置重启仍待验收，见[查看方式调查](../../view-style-wheel-investigation.md)。

状态栏容量已接入：[GetDiskFreeSpaceExW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw) 查询完整当前目录，并补充 UNC 所需的末尾反斜杠，不提前降为盘符根目录；因此保留符号链接/挂载点的卷定位语义。显示当前用户可用空间，提示中显示该用户总容量，64 位字节值不截断。真实磁盘配额和挂载点仍待实机验收。

查询在后台执行，全进程最多两个在途原生调用；每个可见窗格十秒刷新一次，等待名额与结果共用三秒时限。超时只取消等待，原生调用结束前仍占名额；若两个调用都长时间阻塞，其他窗格容量会暂不可用，文件浏览不等待容量结果。导航开始、换标签、隐藏/卸载均取消当前发布；代际检查及实际当前目录检查拒绝旧结果。搜索与其他没有文件系统目录的虚拟视图隐藏容量。不新增文件枚举、选中项大小计算或目录递归。无窗口回归覆盖竞态、节流、取消/超时、并发保留、64 位容量及本地磁盘 API；真实网络位置和窗口显示仍需 GUI 验收。

导航读就绪状态修正：此前 OnNavigationPending 设置 pending，而 OnNavigationFailed 只报告错误，导致统计、容量、查看菜单和文件命令可能一直不可用。现由 ShellViewReadiness 接收开始/成功/失败/销毁事件，以 Shell parsing name 关联目标并递增读取代际。当前目标失败与成功都可结束 pending；旧目标的回调只结清自身，不开放新的导航。同一目标重复请求保留未结束数量，全部匹配回调完成前不猜测先后；无法匹配的回调也不释放已知当前请求。该状态只控制读取就绪，不替代 Shell TravelLog、目录模型或历史。

状态回归先复现失败后未解除 pending，再覆盖旧成功/失败、同目标重复、A-B-A 重访、未知身份和销毁后回调。隐藏原生宿主的四次正常导航回调可正常收敛；快速追加的两次请求实际被 Shell 以 ERROR_BUSY 拒绝，不能据此声称验收了原生重叠失败。App 的真实失效路径/网络失败、错误提示与焦点恢复仍待桌面验证，见 [原生导航探测](../../native-navigation-probe.md)。

选择集可取得大小已实现：IFolderView.Items(SVGIO_SELECTION) 取得当前 IShellItemArray，以 GPS_FASTPROPERTIESONLY 读取 System.Size 的原生聚合值。S_OK + VT_UI8 才展示，VT_EMPTY 等缺失值不当成零；混选文件夹时明确只是可取得大小，不是文件夹内容总计。读取前后核对数组数量、选择版本与导航代际，不为缺失属性追加磁盘读取兜底。此查询在当前 Shell STA 完成，仍需实测大型选区及第三方提供方延迟。真实 Shell 属性和隐藏文件视图的生产读取检查通过，App 可见 UI/焦点待验收，见 [选择大小记录](../../selection-size.md)。


## 2026-10-20 工作区收藏

复用现有 .qdr 完整状态导出与预览、备份、追加导入，在当前便携配置旁 Workspaces 目录保存命名快照。收藏夹增加保存与追加入口；后台只枚举顶层文件名，最多 200 项，重开菜单共享在途查询。点击时重新读取并验证文件，保存到其他目录明确作为普通导出。Favorites.ini 仍只保存单目录收藏。无窗口完整快照、列表边界及外部修改检查通过；真实菜单、对话框和 Shell 视图恢复待验收。详见[工作区收藏](../../workspace-favorites.md)。

## 2026-10-21 独立目录树组件

使用独立 NamespaceTreeControl，与现有 ExplorerBrowser 的生命周期和导航分离。
NativeShellTree 已封装 STA/COM、根替换、Shell 身份祖先确认、延迟展开后的选区读取及释放；
ShellTreeHost 已连接 PaneControl，顶部视图菜单提供全局开关，默认关闭，
NavigationTree.ini 独立保存。根为当前盘符/共享根，侧栏初始宽度 160；
Tab/F6 在标签标题后加入树停靠点。程序同步使用跨延迟通知的作用域，
Dispatcher 有界重试且检查代际；换标签、隐藏、销毁和用户输入取消旧同步，
不把一次 COM 返回当作同步结束。用户导航以 TVN_SELCHANGED 的鼠标/键盘来源为依据，
处理结束后核对目标树项、代际和选区版本再发布；异步 COM 选择通知本身不授权导航。
关闭树只改变独立宿主布局，不重新导航文件视图。隐藏组件和配置回归已通过，
真实用户导航、焦点、重启和多窗格排版待验收，见[原生树调查](../../native-panes-investigation.md)。
