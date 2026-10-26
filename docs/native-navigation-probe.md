# 原生导航能力探测

2026-10-20。此记录验证原生 ExplorerBrowser 组件；不是 Explorer Dirs 发布 EXE 的 GUI 验收。

## 2026-10-21 命令状态通知

**最新结论：历史数量服务不可用，但本机 ExplorerBrowser 支持
`DWebBrowserEvents2::CommandStateChange` 连接点，已经实收并验证前后退状态。**
下面旧记录中的“继续核查其他通知”已有这项答案；同日生产接入见下一节，尚未发布。

### 同日生产接入：源码已实现，GUI 待验收

`ShellHistoryEvents` 负责每浏览器连接点订阅和两个独立三态缓存。
App 订阅在首次导航之前建立；按钮与 Alt+左/右的动作入口共用可用状态。
pending 或位置未确认时暂不允许历史导航，期间收到的原生状态仍保留。
接口不支持或尚无某方向通知时保持未知，允许沿用原导航尝试。
导航完成/失败及命令通知排队刷新时检查订阅实例身份；Pane 只更新当前标签按钮，
切标签立即读取对应实例。隐藏标签不销毁订阅或历史。

生产 `ShellBrowserCommandSink` 按 SDK 声明完整 41 个公开事件。
除命令状态外的事件均为空处理，不修改 ref 参数。退订前断开宿主回调，
退订失败记录 HRESULT 并继续释放；迟到通知不能更新已销毁实例。
只有 Advise 返回 S_OK 才认为订阅成功，失败或其他返回不保留连接点。

验证：

- Settings smoke 先复现未建立订阅，再通过真实 COM 模拟连接点验证订阅、
  方向独立/未知回退、pending 门控、实例隔离、失败 Advise/Unadvise、
  幂等释放、Advise/Unadvise 中同步回调及迟到通知。
- 实际 IDispatch::Invoke 调用 DISPID 105 验证 long / VARIANT_BOOL；
  调用 DISPID 263 验证未使用 WindowClosing 事件返回成功且不改写原 Cancel。
- 同一隐藏 ExplorerBrowser 同时订阅实验 sink 和生产订阅器，下表各阶段
  对生产 Back/Forward 及动作门控再做独立断言，11 阶段均匹配；
  实际 PIDL 全部符合预期，窗口隐藏，前台未变。

这些结果验证生产订阅器和后台处理，不证明可见 App 按钮效果。
尤其按钮变禁用后的焦点、Tab 跳过禁用按钮、四窗格/隐藏标签切换、
Shell 双击导航、搜索/虚拟位置和真实离线/权限失败仍待 GUI 验收。

`--navigation-capability` 增加公开 COM 接口检查和实验 sink `NavigationCommandProbe`。
ExplorerBrowser 直接 QI `ITravelLogStg`、`IWebBrowser2` 均为 `80004002`；
QI `IConnectionPointContainer` 和 `FindConnectionPoint(DWebBrowserEvents2)` 均为 `00000000`。
当前 Shell 视图和它的 site 不支持连接点容器，由它们查询到的 top-browser 则支持。
这不代表 ExplorerBrowser 是 WebBrowser 控件；原历史 QueryService 仍返回 E_NOTIMPL。

ABI 核对本机 SDK `10.0.26100.0` 的 `OCIdl.h`、`ExDisp.h`、`ExDispId.h`：

- `IConnectionPointContainer` IID `B196B284-BAB4-101A-B69C-00AA00341D07`，FindConnectionPoint 为 vtable 4。
- `DWebBrowserEvents2` IID `34A715A0-6587-11D0-924A-0020AFC7AC4D`，DISPID 105；
  命令 1 为前进、2 为后退，参数是 long / VARIANT_BOOL。
- 连接点 Advise / Unadvise 为 vtable 5 / 6。首次导航前订阅，销毁浏览器前退订；
  退订失败也通过 finally 继续释放浏览器和隐藏父窗口，再传播错误。

公开文档：[命令状态事件](https://learn.microsoft.com/en-us/previous-versions/cc136546(v=vs.85))、
[连接点查询与释放责任](https://learn.microsoft.com/en-us/windows/win32/api/ocidl/nf-ocidl-iconnectionpointcontainer-findconnectionpoint)。
实验仅实现所需的 dispatch 成员，不将它直接视作已经完成的生产订阅器。

### 实测矩阵

每次状态断言前独立读取 Shell 视图的实际 PIDL，与隔离目录比较。
下表数值来自原生事件，没有从自建路径列表推算。

| 阶段 | 后退 | 前进 |
| --- | --- | --- |
| 第一个目录 | False | False |
| 进入子目录 | True | False |
| 后退到第一个目录 | False | True |
| 再前进到子目录 | True | False |
| 快速请求中唯一被接受导航完成 | True | False |
| 回到历史中间 | True | True |
| 宿主取消前往新分支 | True | True |
| 成功进入新分支 | True | False |
| 再次导航到同一分支路径 | True | False |
| 后退一次，实际路径仍是分支 | True | True |
| 再后退，实际路径回到子目录 | True | True |

取消阶段准确收到一次失败通知，pending 结束，命令状态通知数保持 12；
全部阶段结束共收到 20 次命令状态通知。快速提交中另两次请求返回 BUSY，
没有被当作成功历史。测试退出 0，`parent-visible=False; foreground-unchanged=True`；
本版将桌面边界变更设为失败条件。不显示窗口、不输入键鼠或调用文件命令。

修正清理路径后再次完整运行退出 0；该次探测 DLL SHA-256：
`D33201A0461B18CC4F3254B0D48D64A5EA0969BD1407408F82F75D3FDC7CE942`。
本地完整日志在忽略目录 `src/.state/navigation-command-probe/20261021-command-state.txt`。
独立复核确认 ABI 和能力结论，并已关闭“退订异常跳过其余释放”的问题。

### 生产接入约束与剩余验收

每个 ExplorerBrowser 实例独立订阅、保存状态，销毁前退订并忽略迟到回调；
仅在所属 Dispatcher 中刷新当前标签按钮，不在 COM 回调中导航、修改选区或抢焦点。
导航 pending 时禁用相应动作；接口不支持或尚无通知时保留“未知”，
不能把未知当作没有历史。标签隐藏不销毁订阅或清空原生历史。

尚需验证 Shell 用户双击导航、搜索/虚拟位置、真实离线或权限失败、
可见 App 按钮、Tab 跳过禁用项和四窗格。此轮是原生能力验证，
生产生命周期与异常处理和 App 接入进度见上节；GUI 验收仍待完成。

## 问题与结论

路线图要求前进/后退/上级入口显示正确可用状态。现有程序以
`BrowseToIDList(SBSP_NAVIGATEBACK / SBSP_NAVIGATEFORWARD)` 导航，
不另建应用历史来替代 Shell TravelLog。

[微软 ExplorerBrowser 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)
说明原生组件维护历史，但未提供直接的 CanGoBack/CanGoForward 属性。
[`ITravelLogStg`](https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa768470(v=vs.85))
存在 GetCount；SDK `tlogstg.h` 定义 SID_STravelLogCursor 与其 IID 相同。
这不足以证明 ExplorerBrowser 实现了该查询服务。

本机探测结果：

- 环境报告版本 `10.0.26200.0`，首轮探测 DLL SHA-256：
  `A070530E3DD55D2318B355C89F23C483DF0967F35D90B0D5F6C4C9F56721F702`。
- ExplorerBrowser 在创建、Initialize 完成、初次目录导航、子目录导航四个时点均可查询 IServiceProvider；
  但 QueryService(SID_STravelLogCursor, IID_ITravelLogStg) 均为 `0x80004001`（E_NOTIMPL）。
- 进入隔离目录、进入子目录、后退和前进均通过当前视图实际 PIDL 比较。
  使用 IFolderView.GetFolder → IPersistFolder2.GetCurFolder，随后 ILIsEqual；
  不把 BrowseToIDList 的成功返回码单独当作导航完成。
- 宿主不调用 ShowWindow、SetFocus 或任何鼠标/键盘注入，结束前 `IsWindowVisible(parent)=False`；
  本次前台窗口对比未改变。

**决定：不把该历史服务接入按钮可用状态。** 继续核查其他受支持的状态通知；
不能在服务查询失败时把“未知”当成没有历史，从而禁用正常后退/前进。
不使用已弃用 IBrowserService 的内部层次接口作为替代。
该失败结果仅限本机此宿主和本次系统环境，不推断所有系统版本的服务行为。

## 复现

源码位于 `src/ExplorerDirs.NativeProbe`；与日常 App 和配置独立。

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --navigation-capability
```

显式参数才运行原生组件。无参数仅打印用法。隔离测试文件与目录在该项目输出目录的
`fixtures/<unique>/` 中；不读取日常 profile、不执行文件操作命令、不显示窗口。
探测会创建一个不可见的原生父 HWND，并泵送所属线程消息。
初始化后使用与 App 一致的 `EBO_NOBORDER | EBO_NOPERSISTVIEWSTATE`。
单次执行可能受系统 Shell 组件影响；本次正常退出码为 0。

```text
created: provider=00000000; travel=80004001
initialized: provider=00000000; travel=80004001
browse=00000000
first-directory: actual-folder-match=True
first-directory: provider=00000000; travel=80004001
browse=00000000
child-directory: actual-folder-match=True
child-directory: provider=00000000; travel=80004001
back: actual-folder-match=True
forward: actual-folder-match=True
parent-visible=False; foreground-unchanged=True
```

当前探测每次导航后泵送消息两秒，再核验实际位置；超出此时间仍未完成会失败，
因此失败需结合实际位置/HRESULT 调查，不能一概认定导航功能损坏。

### 2026-10-25 当前源码复跑

Release 执行 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --navigation-capability`，退出码 0。生产 `ShellHistoryEvents` 订阅在隐藏 ExplorerBrowser 上持续与实验 sink 匹配：初始目录 back/forward 均 false，进入子目录后 back=true，后退后 forward=true，前进后 back=true；快速重访、取消分支、新分支、同路径新历史项及连续同路径后退的命令状态也均匹配实际 Shell 位置。全部 4 个普通导航完成、0 失败；两次快速请求返回 `ERROR_BUSY`，没有被误报为成功。探针结束时父窗口不可见且前台句柄不变。

该复跑重新验证的是生产历史通知适配器与隐藏 ExplorerBrowser 的 Shell 旅行日志状态，不是应用界面中按钮禁用状态、键盘快捷键路由或切标签后的可见焦点；这些仍待桌面验收。

## 仍需真实窗口验收

- Explorer Dirs 四窗格的当前标签、导航按钮及快捷键。
- 切换标签/布局后每标签历史保持，搜索结果的返回目录语义。
- 无可后退/前进历史时的按钮状态与错误反馈。
- 导航失败后的地址、状态提示和实际键盘焦点。
- 本机探测没有验证上级目录、菜单点击、焦点、网络位置或发布版行为。

## 导航状态管理追加验证

同日追加：探测通过 Advise 接收真正的 IExplorerBrowserEvents，复用 App 的
`ShellViewReadiness` 与相同的 PIDL parsing-name 读取代码。
此版 DLL SHA-256：
`95D7A049FCCC9B04FC4439AE7AE7117ABC52CD653FF94563321C8A1B65CA6CBE`。

```text
normal-history: pending=False; started=4; completed=4; failed=0
browse=00000000
browse=800700AA
browse=800700AA
rapid-revisit: actual-folder-match=True
rapid-revisit: pending=False; started=5; completed=5; failed=0
parent-visible=False; foreground-unchanged=True
```

正常历史的四次开始/完成回调均可解除 pending。随后不泵送消息连续提交三次导航，
只有第一次被接受，另外两次返回 `HRESULT_FROM_WIN32(ERROR_BUSY)`；所以不能把它
写成“三个重叠导航均完成”的证据。最终实际目录正确，状态可读取。
此阶段尚无真实失败回调；同日后续的受控取消已验证真实失败通知，见下节。
目录不存在、网络失败及交错失败回调仍待 App 实际窗口复测。

## 宿主取消与真实失败通知

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --navigation-cancel
```

在隐藏宿主已进入隔离目录后，下一次前往子目录的 OnNavigationPending 返回 E_ABORT。
这是[微软文档规定的宿主取消路径](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowserevents-onnavigationpending)，
没有访问失效网络位置、移动/删除文件或显示错误对话框。

```text
browse=00000000
cancel-retains-directory: actual-folder-match=True
cancel-callbacks: pending=False; started=2; completed=1; failed=1; vetoed=1
hidden-view: selected=3; known-size=8; folder-content-excluded=True
after-cancel-navigation: actual-folder-match=True
after-cancel-navigation: pending=False; started=3; completed=2; failed=1
after-cancel-back: actual-folder-match=True
parent-visible=False; foreground-unchanged=True
```

调用返回 S_OK，而导航随后取消并发送真实 OnNavigationFailed：
再次说明不能把 BrowseToIDList 的返回值当作导航完成。
复用的 ShellViewReadiness 能结束 pending，旧视图实际目录不变，仍能读取
3 项选区的 8 字节快速属性；再次导航到子目录及后退成功。
探测要求准确收到一次 veto/失败通知，最终退出 0。
此结果仅证明宿主取消的恢复路径，不覆盖目录不存在、权限拒绝、设备离线或网络超时，
也不验证可见 App 的地址提示、菜单可用状态和真实键盘焦点。

后续复测已把生产 `ShellCurrentLocation.ReadPidl` 加入每次实际位置核验，
与原有独立 GetCurFolder 读取及预期 PIDL 一致，取消/恢复/后退仍通过。
相关宿主过期回调和重复批次处理见[导航发布规则](navigation-publication.md)。

## 原生文件命令可用状态探测

同日增加 `--menu-capability`。隔离隐藏宿主使用真实 IShellView.GetItemObject，
复用 App 的 NativeShellCommandMenu / ShellFileCommands；仅读取 canonical verb 和
enabled state，不调用 InvokeCommand，不执行复制、删除等文件操作。

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --menu-capability
```

本机结果：空选区时 Cut / Copy / Delete / Rename / Properties 全部为 False；
选择一个隔离项目后这五项全部为 True。Paste 和 Undo 在本次系统状态下均为 True，
它们依赖剪贴板及 Shell 操作历史，测试不固定要求此值，也不读取或输出剪贴板内容。
`parent-visible=False; foreground-unchanged=True`，退出码 0。

此证据验证真实 Shell 菜单中的命令发现和状态读取，未验证 App 菜单点击、
实际文件操作、确认对话框及焦点恢复。查询过程中导航、选择或标签变化时，
App 会丢弃整组状态；调用前不再通过 FocusView 改变选区，接受命令后才排队恢复焦点。
对应的失效状态控制台回归已通过。

### 2026-10-23 新建文件夹的延迟菜单状态

同一探测现已覆盖 NewFolder。在可写隔离目录中，空选区和单选均返回 True。
诊断发现 canonical `NewFolder` 在子菜单初始化前已经存在，但 GetMenuState 返回
FFFFFFFF；经 IContextMenu2.HandleMenuMsg(WM_INITMENUPOPUP) 初始化 canonical `New`
子菜单后返回 00000000。0x810 与 0x10 查询标志均表现如此，产品保留原 0x810。

产品仅初始化这个子菜单，不解析本地化标题，不显示菜单。父菜单或子项禁用时保持禁用；
模拟 COM 回归覆盖两种禁用状态、查询不执行命令、正确命令偏移和失效请求取消。
文件菜单、编辑菜单、工具栏及快捷键共用新建入口，保留原来的短期目录观察和重命名保护。
本次真实隐藏探测仍为 `parent-visible=False; foreground-unchanged=True`，退出码 0；
Paste=False、Undo=True 仅为此次环境状态。没有调用真实 NewFolder，实际创建、自动重命名、
四窗格目标和操作后的焦点仍待可见窗口验收。

另有 `--color-capability` 诊断模式，记录菜单前后及再次导航的自有子窗口类，
仅对 SysListView32 读取其支持的颜色消息。本机为 DirectUI，不能从该模式断言
绘制颜色；限制和下一步实验见[文件区颜色调查](shell-color-investigation.md)。

`--pane-capability` / `--pane-capability-off` 使用实验 site 与 EBO_SHOWFRAMES
调查原生树和预览窗格；不会改变 App 当前宿主选项。结果与即时切换限制见
[原生窗格调查](native-panes-investigation.md)。

## 历史服务入口补查（2026-10-20）

原先只检查 ExplorerBrowser 自身的 IServiceProvider。为排除取错服务宿主，
本次扩展同一隐藏探测，在首目录、子目录、后退完成、前进完成四个阶段检查：

1. 当前 IShellView 查询 IServiceProvider。
2. 当前 IShellView 查询 IObjectWithSite，再 GetSite(IServiceProvider)。
3. 上述两个提供方分别 QueryService(SID_STopLevelBrowser, IServiceProvider)。
4. 每个提供方 QueryService(SID_STravelLogCursor, ITravelLogStg)；仅成功时才读取前后条目数。

接口与常量核对本机 Windows SDK `10.0.26100.0`：
`tlogstg.h` 明确定义 SID_STravelLogCursor 为 IID_ITravelLogStg，
其 GetCount 位于 IUnknown 后第 5 个方法（零起 vtable 7）；
ShlGuid.h 定义 SID_STopLevelBrowser 为 `4C96BE40-915C-11CF-99D3-00AA004AE837`。
未尝试未知服务 GUID 或私有接口。

实测所有已创建视图的 provider、site 和 top-browser 查询均成功；
所有这些入口的 travel 查询仍为 `80004001`（E_NOTIMPL）。
创建/初始化后、尚无目录时 GetCurrentView 为 `80004005`，未解引用不可用视图。
同次测试的首目录、子目录、后退及前进实际 PIDL 全部匹配，正常四次导航
`started=4 / completed=4 / failed=0 / pending=False`；最终
`parent-visible=False; foreground-unchanged=True`，进程退出 0。

结论仅限本机嵌入 ExplorerBrowser：这些公开服务入口无法给出历史数量，
不代表其历史导航失效，也不代表独立 explorer.exe 的服务行为相同。
App 前后退按钮的可用状态仍未完成。后续若采用宿主维护的状态，必须覆盖
Shell 自发导航、同路径重访、取消/失败、隐藏标签和重叠请求，不能把路径相等
直接当作同一条历史记录，不能把服务查询失败当作历史数量为零。

## 2026-10-25 匹配布局与筛选导航复验

扩展 `ShellViewHostFilterProbe`：在隔离的 `IShellView`/`ICommDlgBrowser3` 文件筛选宿主中，依次同步 Icon(1/48)、Details(4/16)、SmallIcon(2/16)、List(3/16)、Tile(6/48) 与对应 `SysListView32` 模式；每步均读取 Shell 模式/尺寸、ListView 模式和可见项目名，五种组合全部匹配，且仅显示 `child`、`keep.txt`。随后回到 Details 执行既有键盘/导航链：Ctrl+A 选择两项，Down 将焦点由第 0 行移至第 1 行，Enter 打开子目录；无效文件夹目标保持子视图不变，上级、前进、后退及历史首尾边界均通过。私有 Desktop watchdog 退出码 0，主桌面前台未变。

测试提示 `SetViewModeAndIconSize(SmallIcon, -1)` 在此宿主读回 Icon/48。Microsoft 文档说明 `-1` 使用该视图模式的默认图像尺寸；为何此宿主最终返回 Icon 模式尚未解释，因此探针改用明确尺寸并验证实际读回，不能只依据 HRESULT 或 `LVM_GETVIEW` 宣称两种模式同步。此结果限于临时过滤宿主和程序化 Shell 加速器调用，不覆盖产品 ExplorerBrowser、物理按键/可见焦点或文件操作。Release 构建 0 警告/错误；日志：`src/.state/native-probe-watchdog/20261025-183034-3ee2f132.stdout.log`。接口说明：[Microsoft SetViewModeAndIconSize](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setviewmodeandiconsize)。

## 2026-10-25 ExplorerBrowser 宿主与 SysListView 原型差异

新增 `--view-style-synchronized-hosted`，在隐藏的 ExplorerBrowser 中附着产品 `ShellBrowserSite`，尝试查找当前 `IShellView` 窗口中的 `SysListView32`，以判断隔离原型的 `LVM_SETVIEW` 同步能否移植。实际子控件类为 `CtrlNotifySink`、`DirectUIHWND`、`ScrollBar`，没有 `SysListView32`；因此该宿主没有可向其发送 ListView 模式消息的窗口。探针随后继续执行原 ExplorerBrowser Shell 模式矩阵，八种 FVM 状态以及排序、分组、列顺序/宽度、多选身份检查通过；父窗口不可见，前台未变，退出码 0。

另行运行未开启同步的 `--view-style-capability` 基线亦通过。结论：隔离默认 `IShellView` 原型中的 `SysListView32` 不能直接代表当前 ExplorerBrowser 的 DirectUI 文件视图；`LVM_SETVIEW` 颜色路径目前不适用于产品宿主，不能据此接线或替换 ExplorerBrowser。下一步应调查 DirectUI 视图的公开扩展点，或评估采用独立 `IShellView` 宿主所需补齐的 ShellBrowser/历史/过滤/通知能力，再决定产品架构。Release NativeProbe 构建 0 警告/错误。记录命令：`--view-style-capability` 与 `--view-style-synchronized-hosted`。
