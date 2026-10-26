# 原生导航树与预览窗格接入调查

2026-10-20。这是隔离隐藏宿主的能力实验，不是 App 功能交付或真实窗口验收。
当前 App 继续使用单视图 ExplorerBrowser；没有新增树/预览开关。

## 选中文件后的隐藏预览帧复验（2026-10-24）

新增 NativeProbe 参数 `--pane-capability-selected`：在独立隐藏父窗口中启用
`EBO_SHOWFRAMES` 和 `IExplorerPaneVisibility`，导航到唯一 GUID 夹具后通过
`IShellView::SelectItem(SVSI_SELECT)` 选中文本测试文件；不设键盘焦点、不显示窗口、
不发送物理或合成输入。这样可区分此前“没有选择文件”的实验缺口。

结果：`Shell Preview Extension Host` 子窗口存在且自身带 `WS_VISIBLE`，但祖先可见样式链
为 false，窗口矩形仍为 `0×0`；同一父窗口下 `NamespaceTreeControl` 为 `154×449`。
测试父窗口始终不可见，前台 HWND 未改变，探针退出 0。

这证明选择样本本身仍不能让隐藏预览宿主得到可用尺寸；不证明可见 ExplorerBrowser 的
原生预览无法工作，也不证明预览内容加载成功。真实可见布局、预览处理器生命周期和
切换窗格时的历史/焦点仍需隔离 GUI 验收；在此之前不应把 `EBO_SHOWFRAMES` 接入产品。

## 公开接口与实验方案

- [EBO_SHOWFRAMES](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-explorer_browser_options)
  为 ExplorerBrowser 启用标准窗格。
- [IExplorerPaneVisibility](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerpanevisibility)
  允许 site chain 中的宿主通过 QueryService(SID_ExplorerPaneVisibility) 提供窗格状态。
- [GetPaneState](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerpanevisibility-getpanestate)
  区分导航树、预览、详细信息及命令等窗格。
- [EPS_FORCE](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_explorerpanestate)
  隐含忽略已持久化初始状态，并禁止用户在 Shell 内自行改变该状态。
  实验以 FORCE | DEFAULT_ON/OFF 区分宿主指定的开关，不读取或改写日常配置。

实验 site 仅位于 `src/ExplorerDirs.NativeProbe/PaneProbeSite.cs`：
导航树和预览使用测试开关，已知其他窗格强制关闭，未知 GUID 返回 DONTCARE；
保留现有 ShellBrowserSite 的 ICommDlgBrowser2 服务。
接口 IID 与本机 Windows SDK 10.0.26100.0 头文件核对。
始终设置 EBO_NOBORDER | EBO_NOPERSISTVIEWSTATE，并保持父 HWND 不可见。

## 复现命令

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --pane-capability
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --pane-capability-off
```

每次独立进程、独立临时目录；无鼠标/键盘输入、无强制前台、无 ShowWindow，
不执行文件操作命令。树组件可能自行加载 Shell 命名空间提供方；
不把“父窗口隐藏”当作没有 Shell 组件活动。

## 实际结果

| 步骤 | 导航树状态/回调 | 预览结果 |
| --- | --- | --- |
| 初始 ON | 回调 2 次；NamespaceTreeControl 的祖先可见样式链为真，尺寸 154×449 | 回调 9 次；预览宿主存在，但祖先可见样式链为假，尺寸 0×0 |
| 改为 OFF，再设置相同选项 | 导航回调未增加，仍保留原尺寸及可见样式链 | 回调未增加，未显示内容 |
| IShellView.Refresh | 导航回调仍未增加，树状态不变 | 预览回调增加至 11；仍未显示内容 |
| 清除再恢复 EBO_SHOWFRAMES | 导航回调仍未增加，树状态不变；实际目录 PIDL 未改变 | 仍未显示内容 |
| 导航至子目录 | 导航回调增加至 4；祖先可见样式链变为假，旧子窗口及尺寸仍保留 | 预览回调增加至 20；宿主自身可见样式关闭 |
| 原生后退 | 实际 PIDL 返回测试根目录 | 不据此推断预览支持 |
| 独立进程初始 OFF | 回调 2 次；祖先可见样式链为假，尺寸 0×0 | 回调 9 次，未显示内容 |

所有记录中的“可见样式链”都只检查隐藏测试根窗口以下的 WS_VISIBLE 标志；
**不是桌面实际显示或截图证据**。父窗口最终不可见，前台 HWND 前后相同，
两种探测均退出 0。目录与后退均通过实际 PIDL 比较，而非只检查调用 HRESULT。

## 对接入方案的影响

1. 原生导航树有可用的初始状态控制路径，可以继续评估复用；
   不能仅增加一个即时勾选菜单并期待 SetOptions 或 Refresh 使树同步变化。
2. 动态变化需进一步确认受支持的更新方式。当前实验只证明“下次导航会重新查询”；
   不通过偷偷重建 ExplorerBrowser 来实现开关，因为这会危及每标签历史。
3. 同目录重新导航已通过下述独立探测排除：本机既不刷新导航树状态，又增加
   一条重复目录历史。不得将它作为动态树开关的刷新方式。
4. 预览只证明宿主及回调路径存在。需在获得独占桌面后用隔离文本/图片文件
   验证内容、布局、处理程序生命周期及故障影响；不能把零尺寸宿主当成预览可用。
5. 实际接入还需补 INI 配置范围、隐藏标签状态，以及树/预览带来的 Tab 停靠点；
   原有四窗格文件区焦点与正反遍历验收不能省略。

本实验没有提升 App 功能完成状态，也没有发布新版本。

## 同目录重新导航实验（2026-10-20）

新增显式参数 `--pane-same-location`，只操作隐藏宿主与自动生成的隔离目录：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --pane-same-location
```

流程：根目录 → 子目录 → 选中唯一测试文件（不设置键盘焦点）→ 将 site 树偏好改为关闭
→ 使用 flags=0 重新导航到同一子目录 → 后退/前进，逐次比较实际 PIDL。
只使用 [BrowseToIDList 文档列出的导航标志](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowser-browsetoidlist)。
SDK 中存在 `SBSP_WRITENOHISTORY` 不表示此方法支持它；没有用未列出的标志假设历史不会改变。

本机结果：

| 检查 | 观察 |
| --- | --- |
| 树偏好查询 | 同目录重导航前后 nav-callbacks 均为 4；没有重新查询导航树 |
| 树宿主状态 | 仍为启用样式链、154×449，未按新偏好关闭 |
| 文件视图 | HWND 保持相同；查看模式保持；选中数保持 1 |
| 第一次后退 | 仍是子目录 PIDL，并未回到根目录 |
| 第二次后退 | 回到根目录 PIDL |
| 再次前进 | 回到子目录 PIDL |
| 导航回调 | 6 次开始 / 6 次完成 / 0 次失败，pending=False |
| 桌面边界 | parent-visible=False；foreground-unchanged=True |

首次用“后退一次回根目录”的断言运行失败；随后显式核实第一次仍为同一子目录、
第二次才回根目录，确认新增了重复历史，而非其他未知位置或导航失败。
完整观察探测最终退出 0，仅表示实验运行完成，不表示树开关方案成功。
没有验证排序/分组、图标大小、键盘焦点或可见绘制；既然树状态与历史两项已失败，
没有继续把此方案推进到产品。

后续可评估独立承载原生 NamespaceTreeControl，让树的显示/隐藏与文件视图导航分离；
这仍须验证 Shell 事件、目录同步、COM 释放、键盘停靠点和四窗格占用，
不能以这份实验作为该替代方案已可用的证据。

## 独立原生目录树能力（2026-10-21）

新增 `src/ExplorerDirs.NativeProbe/StandaloneTreeProbe.cs`：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --standalone-tree
```

依据微软 [INameSpaceTreeControl](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-inamespacetreecontrol)
及 [INameSpaceTreeControlEvents](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nn-shobjidl-inamespacetreecontrolevents)，
并以本机 SDK 核对接口顺序。使用独立 CLSID_NamespaceTreeControl，
**没有创建 ExplorerBrowser**。父窗口隐藏，根目录为唯一隔离样本目录，
SHCONTF_FOLDERS 限定文件夹；不加载用户桌面根、磁盘清单或网络根。
禁用排序流持久化、标签编辑与拖放；这些只是探测限制，不是产品最终能力。

最后一次实测：

- GetRootItems：1 项，读回路径与隔离根一致。
- SetItemState 选中已展开根下的子目录，GetSelectedItems：1 项，路径匹配。
- 收到 2 次选择通知和 2 次项目加入通知；说明程序主动同步选区也会产生回调，
  产品接入必须防止它被当作用户导航再次执行。
- TreeUnadvise 后选中根目录仍成功，选择回调计数不再增加。
- RemoveAllRoots 成功；释放接口并销毁父窗口后，枚举出的 3 个子 HWND
  及父 HWND 均经 IsWindow 确认为已销毁。此检查不证明进程中所有 COM 内存零泄漏。
- parent-visible=False、foreground-unchanged=True；进程退出 0。

结论：独立承载、根加载、目录选择读取、通知订阅/取消及窗口清理路径可用，
可以继续实现独立树宿主。由于这项实验没有文件视图，不能据此声称已验证
树与文件视图之间的历史、选区或键盘焦点保持。

接入约束：

1. 树显示/隐藏只改变独立宿主布局，不重新导航或重建 ExplorerBrowser。
2. 文件视图目录变化同步树选中项时，不反向触发新导航；异步或排队事件绑定当前窗格/标签身份。
3. 只将可解析的文件系统文件夹送入现有导航链路；虚拟节点不能伪装为文件系统路径。
4. 将树纳入正反 Tab/F6 路线，明确隐藏时的焦点归属；真实键盘操作仍需可见窗口验收。
5. 先验证延迟展开、当前目录定位与关闭释放，再接入独立便携配置；
   不在应用启动时无条件枚举所有驱动器和网络位置。

## 可复用树组件的隐藏验证（2026-10-21）

`src/ExplorerDirs.App/NativeShellTree.cs` 已封装 STA 创建、根替换、
目录选择读取、通知订阅和释放。此阶段先验证组件；随后 UI 接入记录见下一节。
NativeProbe 直接编译并调用这份生产源码：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --tree-host-core
```

本轮先观察失败再修正：

- 空树 `RemoveAllRoots` 返回 E_INVALIDARG，因此只在有根时移除。
- [IShellItem.GetAttributes](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem-getattributes)
  对请求属性的有效子集返回 S_FALSE；普通文件夹没有 STREAM 属性，
  应接受非负 HRESULT 后检查实际标志，不能误判为调用失败。
- 对折叠节点尚未加载的子目录，直接 EnsureItemVisible 失败。改为先用 Shell
  身份比较确认目标在根内，再从根逐级展开；不枚举整个目录树。
- 展开会延迟加载，并产生晚于 COM 返回的选择通知。同步作用域必须覆盖宿主
  的有界重试过程，不能只屏蔽单次调用栈。组件本身不等待、不泵消息；
  隐藏测试用消息循环和最多 6 次重试检查三层后代。未来 UI 必须使用可取消、
  绑定当前窗格/标签身份的 Dispatcher 重试，并在用户交互时终止旧同步。

最后运行退出 0：子目录及三层折叠后代读回正确，延迟导航请求数为 0；
替换根成功，普通文件、相对路径与根外目录被拒绝，拒绝根外目录不改变选区；
跨线程访问及 MTA 创建被拒绝；重复 Dispose 安全，释放后访问被拒绝；
父窗口和树窗口销毁，测试前后前台 HWND 相同。主程序 Release 构建
0 警告、0 错误。

该测试不证明可见树的用户点击、UI Automation 导航、焦点遍历、
网络/虚拟根、多窗格占用或便携配置行为；
禁止把隐藏验证计作 GUI 验收或已发布功能。

## 窗格接入与通知时序修正（2026-10-21）

源码已增加“视图 → 目录树”，全局默认关闭。各窗格独立 ShellTreeHost
在启用时创建，显示当前盘符/共享根，侧栏初始宽度 160。开关使用当前 profile 旁
NavigationTree.ini；UiPreferences.ini 的旧格式不变。设置读取保留损坏备份、
高版本保护与外部修改冲突检查，独立配置回归通过。树停靠点位于标签标题之后、
文件列头之前；隐藏时停止同步，树持有焦点时返回文件视图。
文件浏览器对象不因树开关重建或重导航。

同步按 100ms Dispatcher tick 重试，最多 40 次，选区稳定两个 tick 后结束抑制。
代际/tree 实例/目标目录在 COM 调用后校验，原生逐层展开间也检查取消；
换标签、隐藏、销毁或用户输入取消旧同步。计时器不重入正在运行的同步，
错误或超时通过状态栏提示。这限制重试次数，**不表示能中断阻塞中的 Shell/网络 COM 调用**。

复核与原生实测驱动了两次修正：

1. 删除“收到任意输入后，下一条选区通知算用户导航”的长期布尔标记。
   普通展开点击或修饰键不能为迟到的程序化通知授权。
2. 最初试图在 TVN_SELCHANGED 处理栈内识别 COM 选择回调，隐藏实测失败：
   4 次原生选区通知、0 次栈内 COM 选择回调。修改后验证 4 次通知处理结束
   都能读出目录，且程序化通知来源均不是鼠标/键盘。

当前宿主只处理 NMTREEVIEW.action 明确为鼠标/键盘的选择通知，
在原生默认处理返回后读取 Shell 选区，发布前后核对 generation、tree 实例、
NewItem.hItem 与 TVGN_CARET、SelectionRevision 及可见性。
异步 COM SelectionChanged 不再承担导航命令来源。
[OnKeyboardInput 契约](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nf-shobjidl-inamespacetreecontrolevents-onkeyboardinput)
要求 S_OK 才让树继续处理按键，源码已修正返回值；
点击回调保留原生处理。

追加的隐藏测试确认：取消定位/根替换保留原选区；原生树的 SysTreeView32
直接子 HWND 存在，可作为焦点目标。最新 App Release 构建 0 警告/0 错误，
Settings smoke 和 tree-host-core 均通过；只读复核已关闭发现的同步/旧上下文发布问题。

**仍待 GUI 验收，尚未发布：**鼠标和方向键实际产生的通知与导航、Tab/Shift+Tab、
F6/Shift+F6、展开时切标签或隐藏、多窗格尺寸与资源占用、重启恢复、慢网络位置。
UI Automation 选择是否有可确认的用户来源也未验证，不能由程序化通知测试推导。
