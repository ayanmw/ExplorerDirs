# 隔离 IShellView 逐项颜色原型

## 2026-10-31 Shell Undo/Redo 原生命令实验（停止）

先前只读命令状态探针在只读分支提前返回时仍落入 session stream 写回断言，修正为该分支不要求写入/回读；`command-state-readonly-private-desktop` 随后退出码 0。新尝试通过 `IFileOperation::CopyItem` 建立临时副本，再从隐藏 `IShellView` 背景 `IContextMenu` 调用 canonical `undo` / `redo`：一轮中 Undo 使临时副本消失，但 Redo 返回 `S_OK` 后副本没有恢复；另一次运行在复制后未观察到 Undo 可用。现有证据说明该命令状态和调用不构成可靠闭环，不能证明产品 Ctrl+Z/Ctrl+Y 路由可迁移。

该操作探针已移除，不再重复执行。Microsoft 文档明确指出 Vista 及以后 `FOF_ALLOWUNDO` 撤销范围是整个用户 session，状态由 `Explorer.exe` 协调；Private Desktop 并不隔离这份状态，因此此实验存在影响同一登录 session 撤销历史的可能。两轮临时 fixture 已由 watchdog 检查清理，但无法恢复或核验全局 Shell undo history。后续需在用户可控、明确授权的可见验收流程中评估；产品仍使用 ExplorerBrowser。[FOF_ALLOWUNDO 文档](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shfileopstructa)

## 2026-10-31：UPDATEITEM 派生值重算与目录重命名事件合同

扩展隐藏 Shell change reconciliation probe：真实 `SHCNE_UPDATEITEM` 通知一个已枚举的子目录后，重新枚举父视图并在 worker 重扫递归大小，隐藏 `SysListView32` 从 4 B 更新到 7 B；稳定行 ID、其它行及当前选区/焦点保持。随后真实 `SHCNE_RENAMEFOLDER` 通知验证同一目录改名时保留已验证身份对应的 12 B 摘要，刷新投影不出现暂时空值，worker 重扫后值仍正确。

测试先复现 probe 常量错误：它把 `SHCNE_RENAMEFOLDER` 写成与 `SHCNE_UPDATEITEM` 相同的 `0x00002000`，且监听掩码缺少目录重命名的 `0x00020000`；按 Windows SDK 分离两个事件并加入监听位后，定向 `--shell-change-reconciliation` 通过。该结果补齐隔离自有行模型的事件与派生值合同；没有创建 `IShellView` 或可见窗口，不证明 DefView 自定义列刷新，也未接入产品 ExplorerBrowser。`SettingsSmoke` 与 NativeProbe Release build 通过，构建 0 警告/错误。

## 2026-10-31：子项 UPDATEITEM 纳入延迟父目录更新路由

源码检查确认 adapter 仅在显式探针开关打开时把 CREATE、RENAMEITEM、DELETE 转为 backing 父目录更新；已有行的属性更新会被 DefView 默认处理，但自有派生值缓存需要父目录重枚举才能重新取值。现在将 `SHCNE_UPDATEITEM` 加入相同分类器，并用无窗口 NativeProbe smoke 确认四种子项事件被接收、`SHCNE_UPDATEDIR` 与未知事件被排除。新增测试在旧 native adapter 上先因缺少分类器导出而失败，改动后通过；NativeProbe Release build 及既有 wrapped-folder contract smoke 通过。

这只证明事件路由的分类条件接入已有延迟/去重队列；本轮没有创建 Shell view 或运行 Private Desktop，也未实测真实更新事件到达、队列去重、派生缓存刷新、排序重排、第三方 provider 或用户可见列。该路由仍只在 NativeProbe 合成 adapter，产品继续使用 ExplorerBrowser。

## 2026-10-31：OLE 拖放循环自行初始化 STA 上的 OLE

源码复核发现 `ShellViewDragSource.RunOleLoop` 只检查当前线程是 STA，却没有保证 OLE 已在该线程初始化。现有交互探针由目标窗格 `RegisterDragDrop` 顺带调用 `OleInitialize`，因此掩盖了拖放入口对初始化顺序的依赖。现在循环入口执行 `OleInitialize`，失败即停止；`DoDragDrop` 返回或抛错都经 `finally` 调用 `OleUninitialize`，正确平衡 `S_OK` 与 `S_FALSE`。NativeProbe 行为合同验证 S_OK/S_FALSE 均执行并配平、初始化失败不进入操作且不反初始化、操作抛错仍执行一次清理；Release NativeProbe build 与完整 Release SettingsSmoke 均通过，0 警告/错误。没有创建 Shell view、可见窗口或运行交互拖放。该改动只完善 OLE apartment 生命周期，不证明真实 Copy/Move、取消、跨窗格命中、通知收敛、前台隔离或产品宿主可迁移；这些仍待单独可见验收。

## 2026-10-30：当前主线复跑四窗格 WPF consumer

使用当前 Release NativeProbe 重跑 `scripts/native-probe-watchdog.ps1 -Probe private-desktop-four-pane-wpf-host-keyboard-consumer -TimeoutSeconds 40`，退出码 0。四个 pane 的 Ctrl+A、八种视图反向选择矩阵、F2 四方向键与错误 pane 路由隔离、真实 `IFileOperation` 复制/重命名/删除后的四窗格通知收敛、原生 Details/排序/分组选单和同步清理均通过；WPF 根窗收到 `WM_NCDESTROY`。Private Desktop 前后台 HWND 与最后输入时间保持不变，探针进程退出且无遗留 NativeProbe 进程。日志：`src/.state/native-probe-watchdog/20261030-051503-3379c5f6.stdout.log`；stderr 为空。此复跑确认隔离 consumer 组合仍可工作，不验证产品 ExplorerBrowser、物理输入、可见 OLE 拖放或迁移完整性。

## 2026-10-29：Shell item 绑定视图的原生上下文菜单

在自定义 `IShellFolder` 适配器产生的嵌套文件夹 `IShellItem` 上，通过 `BHID_SFViewObject` 建立实际 Shell view，再分别查询 `GetItemObject(SVGIO_BACKGROUND, IID_IContextMenu)` 与 `GetItemObject(SVGIO_SELECTION, IID_IContextMenu)`。背景菜单的 `QueryContextMenu` 返回 194 条命令、31 个菜单项，并枚举到 `refresh`、`newfolder`、`viewlogicaldetails`；选中一项后，选中项菜单返回 135 条命令、27 个菜单项，并枚举到 `copy`、`delete`、`rename`、`properties`。随后从该视图选择取出原生 `IDataObject`，交给 `IFileOperation::CopyItems` 分别复制到兄弟目录和当前视图目录；两个副本内容检查通过，源文件保留，当前视图通过 Shell 通知从 2 项更新为 3 项，同目录副本名由系统本地化为 `ten - 副本.bin`。菜单只查询和枚举，未显示或调用菜单命令；没有执行真实鼠标 OLE 拖放。

Release NativeProbe 构建 0 警告/错误。隔离 Private Desktop 运行中，菜单、复制/通知检查均通过；并通过原生背景菜单动词实际切换 Icon→Details（view mode 读回 4）及 Refresh（计数仍为 3）。Shell view 枚举、Size 排序和列布局读回通过，桌面前台 HWND 与最后输入时间未变化，夹具及私有桌面完成清理。探针最终仍以退出码 1 停在既有自定义列门槛：三条自定义列单元格为空，DefView 未请求逐项 `GetDetailsOf`（总计/标题/条目 `4/4/0`）、`GetDetailsEx`、`CompareIDs` 或 `IPropertyStore`。因此操作对象、刷新通知和原生视图命令也获得正向证据，但逐项自定义列仍未通过；这些结果不证明菜单可见呈现、产品宿主接线、鼠标 OLE 拖放或颜色绘制。未启动产品 GUI。

下一步集中调查 DefView 为何只请求自定义列标题而不请求逐项属性值；这是本原型当前的硬阻塞。之后再补足选中项重命名/删除与导航、真实 OLE 拖放门槛，最后才评估产品迁移。

## 2026-10-28：Private Desktop 拖放探针前台阶段复验

首轮使用 `scripts/native-probe-watchdog.ps1 -Probe drop-target-private-desktop -TimeoutSeconds 35` 运行当时的 Release NativeProbe。源与目标 `IShellView` 均创建；选中视图项提供的 `IDataObject` 可被目标目录及另一个注册 HWND 的 `IDropTarget` 直接 Copy，目标文件内容正确、Copy effect 为 `0x1`、源文件保留；但 `IShellView.GetItemObject` 作为 drop target 的查询返回 `E_NOINTERFACE`。结果仍是直接 COM 回调，不包含真实 `DoDragDrop`。

外层前台守卫首次记录到 HWND 从 `0x2632026` 变为 `0x10C1456`，阶段为 `drop-target: query-and-copy-start`，距探针开始约 687 ms；watchdog 退出码 1。该阶段覆盖多个数据对象查询与拖放回调，不能定位单个 COM 调用，也不能排除外部桌面活动。运行后夹具目录不存在、无残留 `ExplorerDirs.NativeProbe.exe`。日志：`src/.state/native-probe-watchdog/20261028-140641-227d80f2.stdout.log` 与 `.stderr.log`。

随后在源码中为 `DragEnter`、`DragOver`、`Drop` 分别加入调用前后阶段标记，再运行一次相同探针。第二轮前台首次变化记录在 `drop-target: IShellFolder.CreateViewObject (registered HWND target): drop-start`，约 1094 ms；这将变化关联到该注册目标的 `Drop` 调用边界。由于监视器每 25 ms 采样且无法排除外部活动，这不是因果证明。两个 Copy 路径仍返回成功，数据及文件检查通过，但前台守卫失败，watchdog 退出码 1；日志 `src/.state/native-probe-watchdog/20261028-140920-574c5db6.stdout.log`、`.stderr.log`。没有遗留 NativeProbe 进程，本轮临时夹具已由 watchdog 清理。

细粒度标记只提供阶段相关性，不会自行阻止或归因前台切换。两轮失败都不作为迁移门槛通过，也不代表真实用户拖放失败；在找到不影响外层前台的验证环境前，不再重跑同类 Shell-view 探针。

## 2026-10-28：区分 Private Desktop 阶段与调用桌面前台变化

静态追踪确认外层 `Run` 在调用线程（即创建 Private Desktop 的原桌面）采集前台 HWND，轮询也始终由该线程执行；Shell 探针则在另一个已 `SetThreadDesktop` 的 STA 上运行。阶段字符串通过共享字段读取，只能标出探针当时走到哪里。拖放探针自身还在 Private Desktop worker 内比较其桌面前台，并报告 `foreground-unchanged=True`。因此旧日志中“外层前台首次变化恰逢 registered target 的 Drop 阶段”是时间相关性，不能证明该 Drop 改变了用户桌面的前台，也不能排除用户/其它程序在同一时刻切换窗口。

为让下一次受控复验能区分这两种情况，外层守卫现在会同时记录它所监视的线程/桌面、前台 HWND 的 PID/进程名/窗口类，以及开始、变化和返回时的 `GetLastInputInfo` 时间戳；不记录窗口标题或输入内容。原前台变化仍令探针失败，保持保护用户桌面的 fail-closed 行为。本轮只做源码审查和 NativeProbe Release 构建，没有重跑 Shell-view 探针；新日志尚未产生，Drop 的因果关系仍未确定。

## 2026-10-28：四窗格 WPF Shell 反向选择矩阵

扩展 `--private-desktop-four-pane-wpf-host-keyboard-consumer`：四个 WPF 托管 `IShellView`、两项临时文件，在 4 个窗格 × 8 种 Shell 查看方式（大/中/小图标、超大图标、列表、详细信息、平铺、内容）逐项运行生产 `ShellSelectionCommands` 的反向选择。每项检查原选中项身份的补集、再次反选恢复、焦点索引保持及兄弟窗格无选区副作用。32 组全部通过；Ctrl+A、F2 四方向键编辑保持、跨窗格输入隔离、复制/重命名/删除通知和菜单生命周期也随组合探针通过。连续 3 次 Private Desktop 运行退出码 0，前台 HWND 不变、隔离窗口及 Shell 视图均完成清理；NativeProbe Release 构建 0 警告/错误。日志：`src/.state/native-probe-watchdog/20261028-092506-b2dc8aaf.stdout.log`、`20261028-092546-d9e05ad9.stdout.log`、`20261028-092605-1a282d6e.stdout.log`。

测试窗格共用同一临时目录，Shell 会把视图模式变更传播给同目录的其他 Shell view；因此探针在每次模式设置完成后才记录模式基线，并检查反选本身不改变兄弟窗格模式。初版断言错误地把这种 Shell 模式同步归因于反选，已修正测试基线。该结果仅证明隔离 `IShellView` 原型和生产共享选择命令在这些模式下的命令行为；没有验证产品 `ExplorerBrowser`、物理键盘/可见选择体验、系统交互桌面或颜色列兼容，产品宿主仍未迁移。

## 2026-10-28：四窗格 WPF Shell 跨卷移动

扩展 `--private-desktop-four-pane-wpf-host-directory-isolation`：源目录位于工作区卷，第四 pane 的隔离目标目录位于系统临时目录卷；通过 `IFileOperation::MoveItem` 将一个测试文件跨卷移动并改名。断言源文件已消失、目标文件存在且内容一致；源卷三个视图都从 `3` 项收敛回 `2` 项，目标视图从 `2` 项增至 `3` 项并保留原有两项。连续 3 次通过，包含此前的通知隔离、键盘路由、菜单生命周期及同步清理；前台 HWND 不变，NativeProbe Release 构建 0 警告/错误。日志：`src/.state/native-probe-watchdog/20261028-082615-e4dce83b.stdout.log`、`20261028-082630-b832f1d2.stdout.log`、`20261028-082638-13f79c29.stdout.log`。三轮结束后临时目录残留数为 0。

这是 Private Desktop 中真实 Shell 文件操作与视图收敛证据，不是鼠标拖放：没有调用 `DoDragDrop`，也没有验证产品视图、可见确认 UI 或其他 Shell provider。

## 2026-10-28：四窗格 WPF 宿主跨目录通知隔离

新增 `--private-desktop-four-pane-wpf-host-directory-isolation`，在前三个 WPF `HwndHost` pane 打开同一临时目录、第四个 pane 打开独立目录的条件下运行 Ctrl+A、F2 方向键和真实 `IFileOperation` 复制/重命名/删除。前三个 pane 都收敛到操作后的正确条目数/路径；第四 pane 始终保留自己的两项内容，且不显示根目录新建的副本或重命名项。连续 3 次通过，四 pane 菜单激活/停用、同步 view/pane/根窗销毁也通过，foreground HWND 未变。最终断言版日志：`src/.state/native-probe-watchdog/20261028-082221-bbdb77f2.stdout.log`、`20261028-082228-08e490d7.stdout.log`、`20261028-082234-e487a32f.stdout.log`。Release NativeProbe 构建 0 警告/错误。

这补上了先前 WPF 宿主同目录通知探针没有覆盖的隔离边界；旧的静态 pane 会话恢复探针已有独立目录检查，但两类结果不能彼此替代。当前仍未验证真实鼠标 `DoDragDrop`、用户可见产品菜单/选择交互、第三方 Shell 扩展或颜色列兼容，产品仍未迁移到 `IShellView`。

## 2026-10-28：四窗格 WPF Shell 文件操作与通知收敛

扩展 `--private-desktop-four-pane-wpf-host-keyboard-consumer`：四个 WPF 托管 `IShellView` 指向同一临时目录，通过真实 `IFileOperation` 依次复制、重命名、删除，并核对文件内容/路径以及四个 pane 的条目数和目标路径。三次连续运行均通过；复制后四 pane 首轮轮询即收敛，重命名与删除的操作 pane 最迟在第 5 次消息泵轮询收敛，其余 pane 首轮收敛。每轮随后逐 pane 激活/撤销 Shell 菜单，销毁四个 view、pane 和 WPF 根窗口；前台 HWND 始终不变。日志：`src/.state/native-probe-watchdog/20261028-081712-d0542690.stdout.log`、`20261028-081718-1d22d0a4.stdout.log`、`20261028-081725-74be3e96.stdout.log`。Release NativeProbe 构建 0 警告/错误。

同时将该组合入口接入 watchdog，后续可以保留独立 stdout/stderr 证据并设置超时。该探针验证的是同一目录的真实 Shell 文件操作和四个独立视图对 Shell 通知的收敛；它不能证明不同目录之间的通知隔离、可见鼠标/OLE 拖放、产品窗格接线、Shell 扩展上下文菜单或颜色列兼容。当前仍是迁移门槛原型，不等于产品集成，也没有打开产品窗口。

## 2026-10-28：四窗格 WPF 宿主键盘与菜单组合矩阵

新增 `--private-desktop-four-pane-wpf-host-keyboard-consumer`，在 Private Desktop 中通过 `HwndSource → WPF Grid → 四个 HwndHost → STATIC pane → IShellView` 运行组合验收。四个 view 都指向同一临时两文件目录：逐窗格合成 Ctrl+A 均选中 `2/2` 项、兄弟 pane 选区保持为空并清理选区；逐窗格 F2 均进入原生编辑框，四方向键期间编辑框和焦点保持，Escape 取消重命名；另验证从 pane 1 编辑时向 pane 2 投递方向键不打断 pane 1 编辑。测试每个 pane 后显式 `SVUIA_DEACTIVATE`，随后确认 frame 菜单恢复并逐个激活/停用四个 Shell 菜单，最终同步销毁 Shell view、pane 和 WPF 根窗。

菜单与键盘合并运行时，第一版探针在键盘矩阵后菜单激活失败。复查发现键盘辅助例程逐 pane 调用 `SVUIA_ACTIVATE_FOCUS`，却未在每个 pane 测试结束时执行 `SVUIA_DEACTIVATE`，让测试把多个视图留在活动态。加入与单活动 pane 生命周期相符的停用步骤后，键盘、基菜单恢复、四 pane 菜单及同步关闭连续 `3/3` 通过；每轮 foreground HWND 未变、无遗留 NativeProbe 进程。NativeProbe Release 构建零警告/错误。日志位于忽略目录 `src/.state/native-probe-watchdog/20261028-wpf-keyboard-menu-{1,2,3}.log`。

这是隔离 WPF/Shell 宿主组合证据，不是产品 ExplorerBrowser 或可见用户输入验收；真实鼠标 OLE 拖放、产品窗格切换/菜单接线及颜色集成仍未完成。产品源文件与预览 release 未改。

## 2026-10-28：四窗格同步清理停滞位置诊断

新增 `session-restore-sync-cleanup-private-desktop` 诊断入口，让四窗格 consumer 在真实 Shell 操作完成后显式清理，并逐阶段输出进度。三轮中，`DestroyViewWindow`、view/folder/browser-site COM 引用释放、菜单协调器释放及 pane 子窗口销毁均返回；随后卡在 consumer 的顶层 parent `DestroyWindow`。第二、三轮还逐一销毁 pane 2–4 的 Shell view 与 pane 1 子窗口，结果相同。consumer 被其 100 秒期限终止，诊断轮次退出码 1；主桌面 foreground HWND 未变，无遗留 NativeProbe 子进程。日志：`20261028-045213-6af1c87c.stdout.log`、`20261028-045600-3606f1d1.stdout.log`、`20261028-045802-31f0f211.stdout.log`。

因此当前证据把停滞点缩小到顶层 parent 窗口销毁调用期间，尚未确定它是 Shell/窗口消息重入、线程等待还是其他根因；不能把它归结为 `Marshal.Release` 卡住。随后原有 process-exit 清理路径完整复验通过，日志 `20261028-050007-1af1a06f.stdout.log`，退出码 0、前台不变。该诊断仅作用于隔离 NativeProbe；产品宿主未改，同步清理门槛仍未通过。

追加消息追踪后，销毁前 `EnumChildWindows` 读到 0 个子窗口；parent 子类观察到 `WM_DESTROY` 进入并在 `DefSubclassProc` 后返回，但在 70 秒内没有收到 `WM_NCDESTROY`，`DestroyWindow` 也未返回。日志：`20261028-050243-9d16f024.stdout.log`。一个无子窗口、未创建 Shell view、未安装 subclass 的 `STATIC` 对照窗在 consumer 前置阶段也未从 `DestroyWindow` 返回；该轮 55 秒超时，与 producer 共用隔离 Desktop，不能据此排除同 Desktop 的 Shell 状态影响，日志：`20261028-050633-effcf141.stdout.log`。该对照代码已撤回，以免遮住正式 consumer 的 parent teardown 观测。根因仍未确定。

为拆分 Private Desktop 本身及多进程共用因素，新增两个无 Shell 对照：独立新 Desktop 中单进程创建/销毁 childless `STATIC`，以及同一新 Desktop 中父、子两个进程分别创建/销毁普通 `STATIC`。两者均退出码 0、`DestroyWindow=True`、HWND 随后无效，主桌面前台 HWND 不变；日志分别为 `20261028-051206-525873e7.stdout.log` 与 `20261028-051211-6f0dc803.stdout.log`。因此普通窗口销毁和跨进程共用隔离 Desktop 本身可通过；早先对照卡住时 producer 正持有另一个 Shell view，当前差异更集中在共享该 Shell 状态的 Desktop，但仍不足以判定是 Shell view、隐藏消息/窗口还是其间接影响。同步清理根因和产品接入门槛仍未通过。

后续最小化对照中，真实 `IShellView` 活动时销毁另一个无关普通 `STATIC` 也通过（`20261028-051446-befd5744.stdout.log`）；一至四个普通 Shell pane 依次执行 `DestroyViewWindow`、释放接口、销毁 pane 和 parent 同样通过（单 pane `20261028-051544-bd6f51c4.stdout.log`，2/3/4 pane 分别 `20261028-051702-7327ae7a.stdout.log`、`20261028-051708-328c2391.stdout.log`、`20261028-051715-b43f629f.stdout.log`）。但仅对单个 pane 调用 `ActivateView(SVUIA_ACTIVATE_FOCUS)` 安装共享 Shell 菜单、再 `ActivateView(SVUIA_DEACTIVATE)` 卸载菜单，完整释放 Shell view 与 pane 后销毁 parent 即可重现：`WM_DESTROY` 进入并从子类返回，随后 watchdog 超时，未见 `WM_NCDESTROY`。该卡顿在协调器于 parent 销毁前释放、延后到 parent 销毁后释放，以及销毁前将焦点显式移回 parent 时都仍存在（`20261028-052155-03d19fa3.stdout.log`、`20261028-052337-04a4fa62.stdout.log`、`20261028-052441-64abb555.stdout.log`）。日志确认基础菜单已恢复、协调器 owner 已清空、active object 为空、RemoveMenus 回调成功。因此菜单激活/卸载后的 OLE/Shell 生命周期是当前最小可复现触发序列，尚无线程栈证据确认卡住的具体系统调用；原四窗格完整 consumer 仍不接入产品。

## 2026-10-28：隔离原型真实 F2 方向键确认

用户在隔离候选窗口 `125449 / 179260` 中选取 sample 文件，手动按 F2 后确认进入重命名，使用方向键时编辑状态保持。该次只覆盖一个样本及当前查看方式；未覆盖四方向键逐项、Shift/Ctrl 组合、跨窗格竞争焦点、ExplorerBrowser 产品宿主或不同视图模式，因此仅记为隔离原型的可见交互证据，不关闭产品焦点验收。

## 2026-10-28：Private Desktop drop-target 数据对象复验

当前 Release NativeProbe 的 `drop-target-private-desktop` 通过真实 Shell `IDataObject` 直接调用目标 `IDropTarget::DragEnter/DragOver/Drop`。选中视图项的数据对象成功复制到另一 `IShellView` 的目标目录及由目标 `IShellFolder::CreateViewObject` 创建并注册的 HWND；目标文件内容正确、源文件保留，Copy effect 被接受。把源 `IShellView.GetItemObject` 当作 drop target 查询返回 `E_NOINTERFACE`，因此源数据对象与目录 drop target 的角色已分别确认。

watchdog 退出码 0、stderr 为空、前台 HWND 不变，探针退出后无残留。日志 `src/.state/native-probe-watchdog/20261028-015722-7a54f99f.stdout.log` 的 SHA-256 为 `EBBC62316BE8F80FB84B85C9C1633ABC090AE699011CDDAD4B7C386824EF9EA6`。这直接调用 COM drop callbacks，没有执行 `DoDragDrop`、鼠标命中测试或物理拖动；不视作真实 OLE 拖放或产品接线通过。

对直接调用序列尝试追加 Shift/Move 断言时，Drop 创建了目标副本但返回 `DROPEFFECT_NONE`，源文件仍存在。微软文档说明 `DragEnter`/`DragOver`/`Drop` 由 `DoDragDrop` 驱动，不能直接调用来代表完整 OLE 操作；因此该结果只说明当前 direct-callback 探针无法验证 Move，不表示真实 Shell 拖动不能移动文件。临时失败断言已撤回，原 Copy 探针重建后复验通过：日志 `src/.state/native-probe-watchdog/20261028-020200-80fea4fb.stdout.log` SHA-256 `9C4DF26603542BE3B674039139F13C03B2F4ED53E6C372ACA2988E1B0F06A0D8`。失败样本日志 `src/.state/native-probe-watchdog/20261028-020120-da410929.stdout.log` SHA-256 `3725D5070D986A5DD3FC1DE2A02499D2FCDF8A5E06F72BED9056F9B1E8D3BE90`。[IDropTarget::DragEnter](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-idroptarget-dragenter)

## 2026-10-28：直接 Drop 探针核验最终 effect

复核发现 direct-callback Copy 探针此前只核对目标文件内容，尽管记录了 `IDropTarget::Drop` 输出的 `pdwEffect`，却未把最终 effect 纳入成功条件。现在每个 Copy 转移必须同时满足目标内容一致和 Drop 返回的 effect 为 `DROPEFFECT_COPY`。Release NativeProbe 构建 0 警告/错误；Private Desktop 探针退出码 0，选中项数据对象与已注册目录目标均返回 effect `0x1`、内容一致且源文件保留，主桌面前台未变。日志 `src/.state/native-probe-watchdog/20261028-062221-3b07c034.stdout.log`。其他未支持的 `IShellView.GetItemObject` drop-target 查询仍为预期失败。

该修正只防止隔离探针把“文件出现但 Drop 未接受 Copy”误报为成功；仍直接调用 Drop 回调，不覆盖 `DoDragDrop`、鼠标命中、Move 源清理或产品宿主拖放。

## 2026-10-28：四窗格会话恢复探针复验

使用当前 Release 构建运行 `scripts/native-probe-watchdog.ps1 -Probe session-restore-private-desktop -TimeoutSeconds 120`，退出码 0，stderr 为空。隔离 consumer 确认状态流恢复到四个 pane；四个 pane 的 Shell 菜单路由通过，键盘命令与 Ctrl+A 覆盖 pane 1–3，F2 编辑及方向键焦点隔离覆盖四个 pane；Copy、Rename、Delete 后四窗格计数收敛，目录通知与 sibling-pane 隔离通过。前台 HWND 前后相同，NativeProbe/consumer 子进程退出后无残留。

原始日志 `src/.state/native-probe-watchdog/20261028-015505-730c1f9b.stdout.log` 的 SHA-256 为 `CF58E5A5628C3413F1A6CDF4B24F1DA76E1A521E43D8A93A6610EB6670F68D8F`。这是 Private Desktop 上的 IShellView 原型证据；不代表已接入 Explorer Dirs 产品或可见 UI 已验收。鼠标 OLE 拖放、实际 WPF 焦点链、系统主题/DPI 与第三方扩展兼容仍未通过。

## 2026-10-27：交互 OLE 探针顶层窗口清理

源码复核发现，可见交互 OLE Copy 探针退出时未销毁它创建的顶层 STATIC 窗口；Private Desktop watchdog 清理约定不能用于用户桌面入口。现在交互路径销毁顶层窗口及 pane 子窗格，Private Desktop 路径保留由 watchdog 回收的行为。`--shell-view-drop-target-cleanup-smoke` 使用隐藏普通 HWND 验证两种清理策略，前台不变；NativeProbe Release build 通过。本轮未运行可见 OLE 探针或 Shell 拖放，闭环仍未验收。

## 2026-10-27 IDropSource 等待启动鼠标键

`QueryContinueDrag` 现在区分“启动 OLE 循环时尚未按下启动键”和“启动键已按下后释放”：前者继续等待，后者完成拖放；Escape 仍返回取消。`--shell-view-color-policy-smoke` 通过 COM vtable 覆盖左键与右键顺序。没有启动 `DoDragDrop`、创建 `IShellView` 或进行物理输入。真实 source/target 交接、效果协商和视图更新尚未验证。

## 2026-10-27 显式交互 OLE Copy 探针入口

NativeProbe 的 `--shell-view-ole-drop-copy --AllowInteractiveDesktopControl` 会创建新临时 fixture 和可见双 `IShellView`，从源 view 获取所选项目 `IDataObject`，再调用 `RunOleLoop`，最后验证目标内容和源文件保留。命令还要求附着的控制台输入，并在等待开始确认后才进入 OLE 循环；默认仍只允许 Copy。Release build 通过，但没有运行命令、创建 Shell view 或进行输入。该命令使用当前交互桌面，须由用户在明确授权且有隔离条件的测试会话中启动；拖出目标窗格可能由其它系统 drop target 接受，所以不可在普通桌面上做无人看护的自动化。它尚未证明真实产品宿主拖放或多窗格 UI 行为。

## 2026-10-27 OLE `DoDragDrop` 调用入口接线

`ShellViewDragSource.RunOleLoop` 现将传入的 Shell COM `IDataObject` 与 `IDropSource` 交给 `ole32!DoDragDrop`，并原样返回 HRESULT 与最终 effect。调用方必须在已初始化的 STA 运行。该方法不设置光标、不注入输入，也没有在本轮执行；构建只验证了 P/Invoke 和 COM 接口编组的类型正确性。真实 OLE source/target 交接、Copy/Move/Link 效果、视图通知及物理输入仍未验证。

## 2026-10-27：OLE 拖放源端对象合同

NativeProbe 增加了独立 `IDropSource` COM 对象，按启动拖动时的鼠标按钮判断继续或完成，Escape 返回取消，`GiveFeedback` 请求 OLE 使用系统默认光标。COM vtable smoke 覆盖左键/右键启动、按钮仍按下/已释放、Escape，以及 None/Copy/Move 效果。

依据 Microsoft [`IDropSource`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nn-oleidl-idropsource)、[`QueryContinueDrag`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-idropsource-querycontinuedrag) 与 [`GiveFeedback`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-idropsource-givefeedback) 合同。当前尚未连接 `DoDragDrop` 源数据对象，也未运行拖放消息循环；这不证明跨窗格拖放已完成或有效。

## 2026-10-27：隔离四窗格文件夹 drop target 注册生命周期

`StreamViewHost` 现按 view HWND 管理多份 STA 目标注册，通过 `OleInitialize` 和 `RegisterDragDrop` 将 Shell drop target 绑定到对应窗口，并在 view 销毁前由同一 STA 调用 `RevokeDragDrop` 与平衡 `OleUninitialize`。四窗格 session-restore consumer 的初始 view、同级 pane 创建/重建以及 `BrowseObject` 新建目标 view 时，均从对应 `IShellFolder::CreateViewObject(IID_IDropTarget)` 获取目录级目标；导航先注册新窗口，再撤销旧窗口的注册并退休旧 view。新 view 创建/注册失败时会尝试只清理新 HWND，保留原 view 的目标注册。独立 color spike 的初始 view 和其 BrowseObject 替换路径也已接线。宿主仅在注册期间依赖 OLE 对目标 COM 指针的引用。

`--shell-view-drop-target-registration-smoke` 在同一个 host/STA 的四个隐藏普通 HWND 上验证多份 owner、选择性撤销、Dispose 撤销其余目标、重复 HWND 注册被 OLE 拒绝且不破坏原 owner，以及撤销后重新注册。源码审查确认本 spike 所有 `CreateViewWindow` 创建点均有目标注册入口。NativeProbe Release build 0 警告/错误，原有 `--shell-view-color-policy-smoke` 通过。此 smoke 不创建 Shell view；新目录导航的真实目标注册仅完成源码接线/编译，真实 `IShellFolder::CreateViewObject` 返回值、Windows 是否已为该 view HWND 注册目标、跨 pane/项目命中、完整 `DoDragDrop` 源/目标交接、物理输入及文件内容/通知都未验证。不得把它报告为拖放完成。

依据 Microsoft [`IShellFolder::CreateViewObject`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)、[`OleInitialize`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleinitialize)、[`RegisterDragDrop`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop)、[`RevokeDragDrop`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-revokedragdrop) 合同。

## 2026-10-27：四窗格 OLE drop target owner 生命周期 smoke

扩展 `--shell-view-drop-target-registration-smoke`，同一 `StreamViewHost`/STA 同时拥有四个隐藏普通 HWND 的 drop-target 注册；另一个 host 对首个 HWND 重复注册，验证收到 `DRAGDROP_E_ALREADYREGISTERED` 后原 owner 仍可撤销，失败路径也平衡自己的 OLE 初始化。host 对其中一个窗口定向撤销后，该窗口可再次注册；Dispose 再撤销其余目标。Release build 0 警告/错误，smoke 通过且前台 HWND 不变。

这验证单一宿主可在同一 STA 并存管理四个 HWND 注册、选择性撤销及集中释放，重复注册失败不会取代既有 owner；仍使用普通 STATIC HWND 与假 `IDropTarget`，不证明 Shell 文件夹目标是否能注册在真实 view HWND，也未调用 `DoDragDrop`。

### 2026-10-30：drop target 重复撤销的幂等清理

先扩展注册生命周期 smoke：在两份测试 HWND 注册后，直接撤销第二个 HWND，再 Dispose 宿主。原实现按预期复现失败，`RevokeDragDrop` 返回 `DRAGDROP_E_NOTREGISTERED (0x80040100)`，在释放其对应 `OleInitialize` 计数前抛异常，且中断了兄弟窗口注册的清理。修复将“目标已不注册”视为撤销已完成，仍对 STA 线程校验并平衡 `OleUninitialize`；其它 HRESULT 继续报错。微软文档确认该结果码表示指定 HWND 没有已注册的 drop target，`RevokeDragDrop` 同时会释放目标接口引用。

修复后 NativeProbe Release build 0 警告/错误，扩展 smoke 通过；确认已外部撤销的 HWND 可被宿主正常释放，兄弟 HWND 也可再次注册，既有四 pane/重复注册/选择性撤销/整体 Dispose 回归保持通过，前台不变。此处证明的是注册释放的幂等与 OLE 初始化计数平衡；不覆盖真实 `IShellView` HWND、`DoDragDrop`、文件传输或产品接线。

## 2026-10-27：拒绝无效的 OLE 宿主窗口句柄

`StreamViewHost.GetWindow` 现在在父 HWND 为零或已失效时将输出清零并返回 `E_FAIL`，有效 HWND 才返回 `S_OK`。之前实现会对无窗口宿主返回 `S_OK` 和零句柄，违背 `IOleWindow::GetWindow` 的失败语义。

COM vtable smoke 先复现该反例并按预期失败；修正后增加父句柄已销毁的回归。`dotnet build src/ExplorerDirs.NativeProbe -c Release --no-restore`（0 警告、0 错误）及 `--shell-view-color-policy-smoke` 通过。该 smoke 只创建隐藏普通 HWND，没有创建 `IShellView`，不证明真实 Shell 激活行为。依据：[IOleWindow::GetWindow](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-iolewindow-getwindow)。

## 当前授权与迁移阶段门槛

用户已授权继续推进隔离 `IShellView` 宿主迁移原型，验证范围包括原生菜单、Shell 文件操作、拖放、目录通知、键盘焦点和每会话 view-state。授权范围仅限 NativeProbe/隔离宿主，不等于批准替换产品 ExplorerBrowser 或发布迁移版本；产品文件视图保持现状，直到原型门槛有完整证据并单独评审。

已有隐藏 COM smoke、局部文件操作/通知样本和会话状态测试来自不同验证层，不能合并宣称完整迁移通过。完整 Private Desktop session-restore 探针现已覆盖真实 `IShellView` 的写入、销毁、重建和恢复；颜色像素已有五种 ListView 样式及排序/分组后的隔离证据，但仍缺真实用户桌面的可见验收、真实 OLE 拖放闭环、物理键盘焦点，以及 WPF 产品宿主集成。当前继续不使用 Computer Use；不将隔离宿主证据当作产品 UI 验收。

### 2026-10-27：保留连续到达的延迟 Shell 菜单命令

静态复核共享 frame subclass 时发现，菜单 `WM_COMMAND` 被延迟到自定义消息执行，但 coordinator 原先只保存一个待处理命令。若在 STA 消费第一个自定义消息前又收到同类命令，第二条会覆盖第一条；已投递的两个自定义消息随后都可能读取第二条命令，造成首命令丢失或命令执行次数不匹配。NativeProbe 现为每条成功投递的自定义消息按 FIFO 入队对应的 pane owner 与 command ID，逐条出队，并在 coordinator dispose 时清空队列；owner 仍在执行前复核。新增隐藏 frame smoke 以两种 pane owner 和不同 command ID 验证 FIFO 身份保留及队列排空；NativeProbe Release build 与 `--shell-view-color-policy-smoke` 均通过（0 警告、0 错误）。该 smoke 不创建 `IShellView`，没有验证真实 Shell 菜单回调在 STA 中连续到达的时序。

完整 `DoDragDrop` 仍不能在当前 Private Desktop 条件下安全验证：微软文档说明 OLE 循环按当前光标下窗口寻找目标并跟踪鼠标/键状态；`GetCursorPos` 和 `SetCursorPos` 要求调用时 input desktop 为当前桌面。为了避免向用户桌面注入鼠标输入，本轮没有尝试驱动真实拖动；直接调用 `IDropTarget` 的通过结果仍不等同于 OLE 源/目标闭环。参见 [DoDragDrop](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-dodragdrop)、[GetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos) 与 [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos)。

### 2026-10-27：五种 ListView 样式的逐项颜色像素隔离复核

`color-private-desktop` watchdog 本轮退出码 0，NativeProbe Release build 为 0 警告/错误，日志为 `src/.state/native-probe-watchdog/20261027-043915-cea60716.stdout.log`。原型为两条文件身份配置不同前景/背景颜色，在 Icon、Details、SmallIcon、List、Tile 五种原生 ListView 样式中读取目标行像素；排序与 Size 分组后再按当前 Shell item 身份映射颜色，并对两行分别断言非零像素。该路径报告五种样式 `matched-color-unsorted` 与 `matched-shell-and-listview-sorted-grouped` 均通过。

日志前面的 `visible-row-pixels` 检查没有收到逐项通知，结果是 0；不能把它算作成功证据。通过的像素证据来自后续专用 `direct-listview-mode-capture`，对实际 `SysListView32` 的 `NM_CUSTOMDRAW` 采样。整个窗口属于独立 Private Desktop，主桌面 foreground HWND 未变；本结果验证隔离宿主的绘制技术可行性，不代表产品已接入，也不替代用户桌面的可见验收、主题/缩放检查或真实 Shell 扩展兼容检查。

## 2026-10-27：四窗格 Shell View 菜单命令映射与执行

隔离宿主的顶层 frame subclass 现在将 Shell composite View 菜单的 `WM_COMMAND` 映射到当前 pane 背景 `IContextMenu` 的 canonical verb。Shell 菜单项 ID 与 context-menu ID 并不一致，因此实现读取活动 View 菜单项的层级标题路径，再与 `IContextMenu::QueryContextMenu` 生成的命令路径匹配；按层级后缀消歧排序和分组菜单中重复的升序/降序项。执行通过 `IContextMenu::InvokeCommand`，并在原始 `WM_COMMAND` 返回后 `PostMessage` 到 frame，避免在 Shell 菜单回调中同步重入。

完整回归有过两次 120 秒 watchdog 超时（`20261027-042623-ddebc4b7.stdout.log`、`20261027-042802-29e50ff5.stdout.log`），前台窗口均未变化。第二次增加阶段标记后确认生产 view 已保存并销毁，停在独立恢复 consumer 调用附近；当时 consumer 输出只在退出后回传，无法定位其内部阶段。随后保留了生产/consumer 阶段标记，并改为实时转发 consumer stdout/stderr。两次连续完整运行通过（`20261027-042900-e2e86451.stdout.log`、`20261027-042934-db8d1d60.stdout.log`）：四个真实 filesystem `IShellView` pane 均验证 Details、排序升序/降序和分组升序/降序；每项只改变活动 pane，兄弟 pane 的模式、排序和分组保持不变。文件复制/重命名/删除、目录通知、F2 焦点隔离、view-state 重建恢复和 foreground HWND 不变等既有检查也通过。consumer 早期停滞的原因仍未复现或确认；阶段输出保留，以便下次超时时定位。

此验证通过 frame 收到的合成 `WM_COMMAND` 调用分派器，未实际显示或点击 Shell 菜单，也未覆盖真实鼠标菜单跟踪、快捷键/OLE accelerator、物理键盘或 WPF `HwndHost` 集成。路径匹配依赖本机 Shell 菜单文本；跨 Windows 版本、本地化语言、扩展菜单项仍需验证。该变化只在 NativeProbe 隔离宿主中，不是产品迁移完成的证据。

### 2026-10-27：隔离 Edit → Select All pane 路由

此前一次临时探针把 `IFolderView::SelectItem` 的标志 `0x1` 当成取消选择使用；实际 `SVSI_SELECT=0x1`，而 `SVSI_DESELECT=0`。因此旧记录中“pane 2 被误选”的结论被错误清选步骤污染，不能作为路由证据。恢复正确标志后，当前系统合成菜单实际将 Edit → 全选分配为 `0x7023`；原型 frame coordinator 收到命令后以当前 Edit 菜单项文字确认该命令，再调用项目现有 `ShellSelectionCommands.Apply(All)` 作用于当前 `IShellView` 的 `IFolderView2`，最后通过 `RestoreFocusedItem(..., takeFocus:false)` 保持原生项目焦点。

NativeProbe Release 构建 0 警告、0 错误；无失效 owner 防护前，完整四 pane `session-restore-private-desktop` watchdog 连续两轮退出码 0：`20261027-051232-3a389403.stdout.log`、`20261027-051309-69db2ee8.stdout.log`。日志读回 pane1/2/3 各自 `2/2` 项被选中、pane4 `1/1`，每次只目标 pane 有选区，清理后所有 pane 均为空选区；原有 view-state、菜单、文件操作/目录通知、Ctrl+A、F2 焦点检查也通过，foreground HWND 未变。随后加入 pending command owner 仍为当前 pane 的检查；四次完整回归均走过 `menu-activation-complete`（Edit 全选断言在此前执行），因此新保护下的四 pane Edit 全选也通过：`20261027-051440-1b4c16df.stdout.log`、`20261027-051510-5c90b97d.stdout.log`、`20261027-051539-7154f511.stdout.log`、`20261027-051631-ff469050.stdout.log`。但这些回归之后分别在复制/重命名/删除阶段超时，不能算整套 session-restore 回归通过；卡点位置会变化，尚未找到原因。

该实现只在 NativeProbe 隔离宿主，不接入产品 ExplorerBrowser。它通过合成 frame `WM_COMMAND` 与当前中文/英文菜单项目名识别全选，尚未覆盖其他本地化 Shell 菜单、真实菜单跟踪/鼠标点击、物理键盘或 WPF 产品焦点链；不能据此宣称产品迁移已完成。活跃 pane 的 owner 校验有四 pane 合成命令覆盖；切换 pane/视图后旧 pending 命令被丢弃的竞争分支、完整操作回归稳定性仍未验证。

### 2026-10-27：为恢复 consumer 增加逐阶段诊断

在第二个完整超时后，恢复 consumer 停滞位置不稳定：一轮在四 pane 菜单命令后未进入文件操作完成标记；加入复制、重命名、删除的开始/完成/读回标记后，两轮完整 Private Desktop session-restore watchdog 退出码 0（`20261027-050416-d29a6157.stdout.log`、`20261027-050448-d6f3baa5.stdout.log`）。更早的 `20261027-050301-4d766a58.stdout.log` 也完整通过。三轮成功均覆盖四 pane 菜单、Shell 复制/重命名/删除、视图状态、焦点与目录通知，主桌面 foreground HWND 未变；间隔仍有一轮在文件操作阶段超时（`20261027-050333-52f9c29c.stdout.log`），故不能视为已修复的稳定性问题。

诊断输出现在分别标记 view 创建/恢复、pane 创建、菜单路由和每项文件操作，帮助定位偶发 consumer 停滞。全选菜单的隔离 pane 路由已按上一节验证；超时原因仍未确认。隐藏原型通过也不代表产品 ExplorerBrowser 或可见用户桌面验收。

当前可见停滞点集中在 `file-delete-performed` 之后、`file-delete-verified` 之前，但旧阶段日志无法区分主视图计数/路径匹配、Refresh、四 pane 计数或恢复列/排序/分组检查。NativeProbe 现会分别记录这些边界，并在删除主视图等待中记录 `Pump`、`ItemCount`、PIDL 路径检查和每窗格计数等待入口，供下次获准的隔离运行定位具体阻塞调用。本轮仅构建验证，没有启动 ExplorerBrowser/IShellView 宿主探针；当前无根因结论。

## 2026-10-27：Content 视图逐项颜色绘制像素验证

在同一 NativeProbe 文件夹中切换到 `FVM_CONTENT`（Shell mode 8），确认原生 `SysListView32` 为 Content 样式且包含两个夹具项目；随后对每个项目读取其标签区域像素，并与按 Shell 项目身份匹配的规则背景色比较。首次采样可能发生在逐项绘制回调前，因此最多重试 10 次、每次间隔 100 ms。`color-private-desktop` watchdog 退出码 0，日志 `src/.state/native-probe-watchdog/20261027-044600-26c2494a.stdout.log` 报告 `shell=8/32;list-view=4;items=2;callbacks=2;attempts=2`，两项实际与预期颜色分别为 `0xF0E0D0`、`0xD0F0E0`。完整 NativeProbe Release 构建 0 警告/错误；该轮 session state 写回 772 字节，重建 host 恢复成功，Private Desktop 前台 HWND 不变。

前两轮未通过的采样分别暴露了颜色值通道顺序解释错误，以及 Content 初次绘制尚未收到逐项回调；修正比较为原始 COLORREF 后，带有限重试的第三轮稳定命中。此前五种标准 ListView 样式的逐项颜色采样也已通过。该证据证明隔离 `IShellView`/DefView 在六种已测视图模式中能按项目绘制规则颜色，但不代表颜色功能已接入 Explorer Dirs 产品、Q-Dir `.qdr` 颜色已导入，也没有覆盖主题变化、DPI、多语言 Shell 扩展或可见 UI 验收。

## 2026-10-27：四窗格原生 View 菜单命令 ID 直接分派

将合并 View 菜单中已匹配到的 `FCIDM_SHVIEWFIRST..LAST` 命令原样发送给当前活动 `IShellView::GetWindow` 返回的 view HWND。此前 NativeProbe 用菜单文字路径匹配背景 `IContextMenu` canonical verb；微软 [`Implementing a Folder View`](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview) 说明 folder-specific 菜单命令在该 ID 区间内，并由 Explorer 宿主传给 folder view 窗口过程处理。单 pane 的 Details 命令实验也确认父 frame 收到同一 `WM_COMMAND` 不改变视图，而 view HWND 收到后切至 Details。

Release NativeProbe 构建 0 警告/错误；`session-restore-private-desktop` watchdog 连续两次退出码 0：`20261027-044947-295ada75.stdout.log` 与 `20261027-045014-d7a36a44.stdout.log`。每次都在四 pane 分别将合成菜单里的 Details、排序升/降序、分组升/降序命令送达各自 view HWND；读回结果表明只有当前 pane 状态变化。四窗格菜单安装/停用恢复、视图状态恢复、复制/重命名/删除与 watcher 检查、F2 四方向键焦点隔离继续通过，前台 HWND 未变。

该结果补齐隔离 NativeProbe 中的标准 View 菜单命令目标路由，并避免用本地化菜单文字推断 context-menu verb。命令清单仍通过当前 composite 菜单 ID 查找；实际菜单显示/点击、键盘菜单导航、标准 File/Edit 菜单命令、WPF `HwndHost` 和 ExplorerBrowser 产品均未验证。产品宿主不变更、不发布。

## 2026-10-27：区分 OLE frame 与 Shell view accelerator 路径

验证 `OleTranslateAccelerator` 时，探针创建的 accelerator 表可被 Win32 `TranslateAccelerator` 匹配，但调用 OLE API 返回 `S_FALSE` 且没有到达 `IOleInPlaceFrame::TranslateAccelerator` handler。该单次实验未满足完整 OLE in-place activation / `GetWindowContext` 生命周期，不能作为 frame accelerator 实现失败的结论；临时测试代码已撤回，不影响完整会话恢复探针。

微软文档分别说明 [`IShellView::TranslateAccelerator`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-translateaccelerator) 是 Explorer 调用以分发文件视图快捷键，[`IShellBrowser::TranslateAcceleratorSB`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-translateacceleratorsb) 处理浏览器 frame accelerator，而 [`OleTranslateAccelerator`](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oletranslateaccelerator) 属于对象服务器请求容器翻译的 OLE 路由。`OleTranslateAccelerator` 使用由 `IOleInPlaceSite::GetWindowContext` 提供的 frame 信息和 accelerator table；它用于 OLE in-place object activation。本文原型直接通过 `IShellFolder::CreateViewObject` 创建 `IShellView`，没有把文件视图作为 OLE in-place object 激活，因此人为补上 `GetWindowContext` 再测 `OleTranslateAccelerator` 不会验证当前 Shell view 宿主的按键路径。文件视图按键应继续沿 `IShellView::TranslateAccelerator` 验证，并区分 view 自己消费的按键与容器按键。

### 2026-10-27：探测 filesystem view 到 browser accelerator 的委托

在完整 Private Desktop session-restore consumer 内，对有焦点的真实 filesystem `IShellView` 直接提交独立的 F4、F6、F24 `WM_KEYDOWN`，每次前都重新聚焦同一原生文件列表。`IShellView::TranslateAccelerator` 分别返回 F4=`S_FALSE`、F6=`S_OK`、F24=`S_FALSE`；三次均未调用 `IShellBrowser::TranslateAcceleratorSB` 回调。探针结束后恢复原焦点，前台 HWND 未变；完整四窗格 session-restore watchdog 退出码 0，日志：`src/.state/native-probe-watchdog/20261027-043550-7cf2b1d4.stdout.log`。

这说明 Windows 当前 filesystem view 对这几个合成按键没有委托 browser accelerator handler，其中 F6 由 view 自己消费。它不证明自定义 namespace extension 对 frame accelerator 的一般委托规则，也不覆盖真实物理按键、消息循环 accelerator table 或 WPF `HwndHost`。后续重点应放在产品 `ExplorerBrowserHost.TranslateAcceleratorCore` 到实际 focused Shell view 的消息、焦点与 pane 路由；`OleTranslateAccelerator` 不作为 filesystem `IShellView` 迁移门槛。参考微软 [`IOleInPlaceSite::GetWindowContext`](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-ioleinplacesite-getwindowcontext) 对 OLE object activation 的契约。

## 2026-10-26：读取 Q-Dir Filter2 原始规则行

NativeProbe 增加 `QdrFilter2RawReader`，只处理 `[Filter2]` 中的数字键，按键序号排序，并保留模式字符串及其后六个原始字段。这个结构依据本机可读 `Q-Dir.ini` 的 12 行样本；字段含义未确认的值不映射到颜色或样式开关。编号规则若字段数不符或序号重复，解析整体失败且不返回部分规则。

颜色策略 smoke 覆盖样本中的 `#Hidden`、通配规则和 `#BG` 原始行，以及缺字段拒绝。该读取器仅供隔离原型使用，未接入产品 `.qdr` 导入器；不会导入 Q-Dir 颜色、也不证明 `IShellView` 可见绘制或迁移兼容性。产品当前仍对 `[Filter2]` 显示“未导入”警告。

复核 [Q-Dir FAQ 47](https://softwareok.com/?faq=47&seite=faq-Q-DIR) 与 [FAQ 6](https://www.softwareok.com/?faq=6&seite=faq-Q-DIR)：公开说明确认 `#BG`/`#BG-A` 背景规则、`#` 隐藏项、`#DIR` 文件夹、`#RO` 只读项，以及属性值 `-1` 允许后续匹配规则设置，`0` 表示不设置且锁定后续规则，`1` 表示设置并锁定后续规则。公开页面未说明 `[Filter2]` 行中这六个值各自对应的属性列；本机 INI 样本只能证明行形状。因此映射前仍须获得可公开验证的字段顺序证据，不能按样本猜列含义。

## 2026-10-26：为隔离 Shell 宿主插入标准 OLE 菜单组

`StreamViewHost.InsertMenusSB` 的默认宿主路径现创建 File、Edit、View、Favorites、Tools、Help 六个原生弹出菜单，并使用 Shell 标准菜单 ID；`OLEMENUGROUPWIDTHS` 的宿主数量为 `[2,0,3,0,1,0]`。`RemoveMenusSB` 与 `Dispose` 只移除宿主创建的菜单组和子菜单；外层显式回调仍优先接管。菜单标题暂为固定英文原型标签，未连接 WPF 产品菜单或本地化资源。

`--shell-view-color-policy-smoke` 先在隐藏 HWND 上经实际 COM vtable 验证菜单项、ID、分组宽度、移除/释放及重复合并；另在同一 HMENU 保留一个外部 popup，确认宿主卸载不会删除非自身菜单。OLE composite 安装也已用真正的 `IOleInPlaceActiveObject` COM 接口夹具，不再使用空活动对象。没有创建 `IShellView`，Shell 尚未向这些组添加自己的命令项；因此不证明 Shell 菜单项合并、OLE 命令消息触发、活动对象消息循环或可见交互。依据：[Microsoft Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview) 与 [OleSetMenuDescriptor](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-olesetmenudescriptor)。

进一步尝试在隐藏 HWND 上直接合成 `WM_INITMENU`、`WM_INITMENUPOPUP`、`WM_MENUSELECT` 与 `WM_COMMAND`。消息到达 frame，但没有转发到 active-object 窗口；这类直接 `SendMessage` 没有经过系统实际菜单跟踪流程，不能据此判定真实 OLE 菜单路由失败，也不能当作成功证据。移除此无效断言；真实用户菜单选择仍须在不共享桌面的隔离会话验证。

## 2026-10-26：为隔离 Shell 宿主提供 IOleInPlaceFrame

`StreamViewHost` 现在也暴露 `IOleInPlaceFrame` IID。其菜单插入/安装/移除、状态文字、模态状态与 accelerator 方法复用 `IShellBrowser` 已验证的父级路由；`SetActiveObject` 走独立回调，父级接受后 host 保留一个 COM 引用并在替换/Dispose 时释放。`GetBorder` 报告父 HWND 客户区；`RequestBorderSpace` 与 `SetBorderSpace` 仍为 `E_NOTIMPL`，异常回调转成 `E_FAIL`。

NativeProbe 的真实 CCW vtable smoke 覆盖该接口的继承槽布局、回调参数与 HRESULT、异常边界、边框拒绝及 Dispose 后断开。根据[微软 IOleInPlaceFrame 契约](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nn-oleidl-ioleinplaceframe)，这个接口提供容器框架菜单/状态/快捷键入口；当前还没有 Shell `UIActivate` 实际调用、共享 HMENU 生命周期或 `OleSetMenuDescriptor` 证据，因此这不代表主菜单已合并、安装或能正确分派命令。

## 2026-10-27：实现隔离宿主 GetBorder

`StreamViewHost.GetBorder` 现在验证输出指针和父 HWND，再通过 `GetClientRect` 返回容器可用客户区；空指针返回 `E_INVALIDARG`，无效父窗口返回 `E_FAIL`，Win32 读取失败转换为对应 HRESULT。`RequestBorderSpace` / `SetBorderSpace` 仍返回 `E_NOTIMPL`，因为原型尚未提供可分配的独立 Shell 工具栏边框空间。

`--shell-view-color-policy-smoke` 先以隐藏普通 HWND 的 COM 回调断言复现 `GetBorder` 不写结果的失败，再核对返回矩形与独立 `GetClientRect` 结果一致、空指针被拒绝、其余 OLE frame 路由和异常/Dispose 回归通过。该验证没有创建或激活 `IShellView`，不证明真实 Shell 对边框协商的调用时序或产品菜单/工具栏布局。

## 2026-10-27：补齐隔离宿主的 OLE 边框空间拒绝合同

复核 `IOleInPlaceUIWindow` 合同后，`RequestBorderSpace` 对空指针返回 `E_INVALIDARG`；对四边均为零的请求返回 `S_OK`，对需要工具栏空间的请求返回 `INPLACE_E_NOTOOLSPACE`。`SetBorderSpace` 接受空指针或零宽度，不声称分配原型不支持的非零工具栏空间，并返回同一明确的无工具空间 HRESULT。

COM vtable smoke 覆盖上述空指针、零值和非零请求；NativeProbe Release 定向 `--shell-view-color-policy-smoke` 通过。依据：[RequestBorderSpace](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-ioleinplaceuiwindow-requestborderspace)、[SetBorderSpace](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-ioleinplaceuiwindow-setborderspace)。没有创建 `IShellView`，因此真实 Shell 是否请求边框空间及其菜单/工具栏表现仍未验证。

## 2026-10-27：保留隔离 Shell view-state 流的读写模式

`IShellBrowser::GetViewStateStream` 现在按 `grfMode & STGM_ACCESS` 区分 READ、WRITE 和 READWRITE。READWRITE 使用已有 Shell 快照/初始导入状态作为流内容，同时保留可写流供后续保存；不合法访问值 `3` 返回 `E_INVALIDARG` 且不返回流。微软文档指出三种访问模式均有效，虽然 Shell 通常在创建视图时用 READ、保存时用 WRITE。

`--shell-view-state-stream-smoke` 先因 READWRITE 返回空流而失败；修正后验证读取原状态、覆写并 Dispose 持久化到独立 session，再由新 host 读回；无效访问模式也有拒绝断言。定向 smoke 通过。没有创建真实 `IShellView`，因此尚未确认 Windows Shell 是否会在该原型路径请求 READWRITE；依据：[IShellBrowser::GetViewStateStream](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-getviewstatestream)。

## 2026-10-27：修正部分 OLE 菜单组插入失败的回滚

源码审查发现 `InsertNativeMenuGroups` 的失败清理按正序移除菜单位置。每次删除会让后续项左移，可能遗留自有 popup 并错删紧邻的菜单项。现在通过共同的回滚函数按逆序撤销已插入组，释放相应 popup 句柄。

新增原生 HMENU 回归夹具，对 0 到 6 个已插入组的全部失败前缀逐一验证；每种情况下都只移除已插入的自有 popup，保留外部菜单项，且未插入的 popup 仍有效。夹具先按旧正序行为失败，逆序修正后通过。`--shell-view-color-policy-smoke` 全部通过。此测试不创建 `IShellView`、不显示菜单，也不验证 Shell 可见菜单合并或产品菜单接线。

## 2026-10-26：补齐 IOleWindow 帮助回调路由

`StreamViewHost.ContextSensitiveHelp` 可选转给外层宿主，传递进入/退出帮助状态并保留其 HRESULT；没有处理器时继续返回 `E_NOTIMPL`。异常会转为 `E_FAIL`，释放时清除回调。COM vtable smoke 验证槽 4 的默认/转发/异常/释放行为；未启动实际帮助 UI 或 Shell view。

## 2026-10-26：转发活动 Shell 视图通知

`StreamViewHost.OnViewWindowActive` 现在可选委托给外层宿主，原样传递活动 view 指针并返回父级 HRESULT。无回调时仍返回原先的 `S_OK`；异常转换为 `E_FAIL`，释放 host 时解除委托。

`--shell-view-color-policy-smoke` 经实际 `IShellBrowser` CCW vtable 槽 16 验证参数、HRESULT、异常隔离和释放后的默认行为。没有创建 Shell view 或窗口，因此焦点、激活时序以及多窗格/标签激活尚未验证。

## 2026-10-26：接入 Shell 模态与工具栏状态回调

`StreamViewHost.EnableModelessSB` 和 `SetToolbarItems` 现在可选调用外层宿主回调：前者传递 Shell 请求的启用/禁用状态，后者传递原生按钮结构指针、数量和标志；回调 HRESULT 原样返回。未配置回调仍返回原有的 `S_OK`，异常转换成 `E_FAIL`，释放时清除委托。

`--shell-view-color-policy-smoke` 通过实际 `IShellBrowser` CCW vtable 验证参数、返回值、异常边界和释放后的默认行为。没有创建窗口或可见工具栏，也没有接入产品框架控件；这不证明工具栏合并、按钮绘制或模态交互已经实现。

## 2026-10-26：为 IShellBrowser 接入外层菜单生命周期路由

`StreamViewHost.InsertMenusSB`、`SetMenuSB` 和 `RemoveMenusSB` 可选调用外层宿主处理器；处理器收到原始 HMENU、`HOLEMENU`、活动窗口 HWND 和菜单组宽度指针，返回 HRESULT 原样保留，异常转成 `E_FAIL`。没有外层 SetMenu/RemoveMenus 处理器且 parent HWND 有效时，host 自己调用 Win32 `SetMenu` 并以 `OleSetMenuDescriptor` 接入 OLE 菜单分派；卸载时解除分派，并仅在该共享菜单仍挂在 parent 时恢复之前的菜单。parent 为零时维持空宿主 `S_OK` 行为。

`--shell-view-color-policy-smoke` 通过真实 `IShellBrowser` CCW vtable 和未显示的顶层 HWND 验证原生菜单安装/卸载，以及 `OleCreateMenuDescriptor` 创建、`OleSetMenuDescriptor` 安装/解除和调用方销毁顺序。没有创建 `IShellView`，也没有 Shell 菜单项进入 composite 或转换 WPF 菜单；因此不证明 Shell 实际合并、命令派发或可见交互。HMENU 与 HOLEMENU 仍由调用方拥有。产品右键菜单实现不受影响。

## 2026-10-26：为 Shell 控制窗提供外层宿主路由

`StreamViewHost.GetControlWindow` 和 `SendControlMsg` 现在可选委托给外层宿主：控件映射只返回显式提供的 HWND；未提供的窗口返回 `E_FAIL`，没有消息处理器时返回 `E_NOTIMPL`。成功的消息处理保留原始 HRESULT 和返回值；委托异常转换为 `E_FAIL`，不跨越 COM 边界。释放时清理两个委托。

`--shell-view-color-policy-smoke` 通过真实 `IShellBrowser` CCW vtable 覆盖未支持控件、有效/缺失 HWND、消息参数/结果、失败 HRESULT、异常和释放后的断开。没有创建 `IShellView`，也没有映射产品 WPF/ExplorerBrowser 控件；这不证明 Shell 实际查询控件的种类或调用时序，也不代表菜单合并已实现。

## 2026-10-26：为 IShellBrowser 提供父级快捷键路由

`StreamViewHost.TranslateAcceleratorSB` 不再无条件返回 `S_FALSE`。隔离宿主可选提供 `AcceleratorHandler`：返回已处理时向 Shell 返回 `S_OK`；无处理器、未消费或处理器异常时返回 `S_FALSE`，保持 Shell 原生处理。宿主释放时清除处理器引用。

`--shell-view-color-policy-smoke` 通过实际 `IShellBrowser` CCW vtable 验证消息指针和命令 ID 到达处理器、消费/回退 HRESULT、处理器异常不越过 COM 边界以及释放后不再调用处理器。没有创建 `IShellView` 或完整消息循环，也没有接入产品的 WPF 快捷键路由；物理键盘、多选、文本编辑排除和焦点行为仍未验证。

## 2026-10-26：接收 IShellBrowser 状态文本

`StreamViewHost.SetStatusTextSB` 原先返回 `S_OK` 但丢弃传入文本。现将 LPCWSTR 复制到 host 的 `StatusText` 状态；空指针表示清除，并通过 `StatusTextChanged` 通知隔离宿主订阅者。通知异常会被限制在 native callback 边界内，不涉及原生传入缓冲区的所有权。

`--shell-view-color-policy-smoke` 通过真实 `IShellBrowser::SetStatusTextSB` CCW vtable 槽写入文本再传空指针清除，验证 host 状态和订阅事件中的两次更新。没有创建 `IShellView` 或 WPF 状态栏；尚未验证 DefView/provider 实际调用内容、调用时序或文本显示布局。

## 2026-10-26：通过 SID_SShellBrowser 暴露 CDB2/CDB3 服务

颜色宿主先前只在直接 `QueryInterface` 时能取得自身的 `ICommDlgBrowser2/3`，而 `StreamViewHost.QueryService` 在未配置独立 `BrowserSite` 时对 `SID_SShellBrowser` 返回 `E_NOINTERFACE`。现为该独立原型增加服务查询回退：若没有外部 site，则按请求 IID 对 host 自身执行 `QueryInterface`；已有 `BrowserSite` 时仍优先委托给它。

Smoke 先通过实际 `IServiceProvider` CCW vtable 槽调用 `QueryService`，并按 `SID_SShellBrowser` 请求 CDB2/CDB3；修复前两次查询均未取得接口，修复后均成功。完整 `--shell-view-color-policy-smoke` 通过，包括既有 CDB2/CDB3 全槽回调、筛选及 provider 失败开放用例。该验证没有创建 `IShellView`，仅证明托管服务路由/COM 身份可用，不证明 DefView 实际通过此路径查询或调用的时序，也不证明产品 ExplorerBrowser 已可迁移。

## 2026-10-26：将 BrowseObject 导航路由下沉到通用隔离宿主

`StreamViewHost.BrowseObject` 原先始终返回 `E_NOTIMPL`，颜色宿主和筛选宿主各自转发到 `ShellViewNavigationHistory`。现将可选的导航历史和其 PIDL 生命周期归到通用 host；没有配置历史时仍返回 `E_NOTIMPL`，配置后由基类统一处理绝对/相对目标、父级、前进/后退及 `SBSP_*` 策略。颜色宿主通过构造参数接入，筛选宿主将已有历史状态交给基类，并保留其目标计数与 fixture 专属回调。

新增回归先因缺少 `StreamViewHost` 的 `navigation` 参数而构建失败；接线后通过真实 `IShellBrowser::BrowseObject` CCW vtable 槽验证成功导航、失败 HRESULT 保留当前位置/历史，以及宿主释放历史 PIDL。完整 `--shell-view-color-policy-smoke` 通过。该 smoke 用模拟 PIDL 和委托，不创建 `IShellView`；颜色探针实际重建 view 的导航回调仍是另一层，真实 provider 导航、旧/新 HWND 交接、焦点与选择恢复没有因此验收。

## 2026-10-26：让同一探针宿主读回 Shell 写入的视图流

`StreamViewHost.GetViewStateStream` 之前每次读取都返回固定的初始字节；即使 Shell 已写入新视图状态，后续在同一宿主生命周期内创建 view 仍读不到它。现在首次读取使用传入的初始流，之后若存在 Shell 写入则从保存的内存 `IStream` 读取最近字节。构造时复制输入字节，避免两个标签/调用方共用同一数组后，外部修改污染每个 host 的初始回退状态。

新增 `--shell-view-state-stream-smoke`，不创建 `IShellView`，直接通过真实 Windows `SHCreateMemStream`/COM `IStream` 接口写入字节，再请求读取流并逐字节比较；还以两个 host 共用一个初始数组后修改调用方数组的方式，验证每个 host 保留独立初始快照，且其中一个 host 的 stream 更新不会覆盖另一个 host。两项回归分别先因读回旧初始值、初始数组被外部修改而失败，修复后通过。NativeProbe/App Release build 与完整 SettingsSmoke 通过，0 警告/错误。此结果只证明同一个 `StreamViewHost` 实例内存往返及实例隔离；没有进程重启持久化或 Q-Dir 互操作证据。

同一探针随后加入 `ShellViewStateStore`：以稳定 GUID 会话键将 opaque native stream 写入独立二进制文件，先写唯一临时文件并 flush，再原子替换目标；`StreamViewHost.Dispose` 在释放最后的写入流前提交保存。重新创建 store 和 host 后可读取原 stream，未保存的 GUID 仍使用构造时的初始字节。NativeProbe smoke 覆盖双会话隔离与恢复；尽管后续已增加独立进程读写复验，真实 `IShellView` 的保存/销毁时序及产品标签会话持久化仍未验证。

跨进程复验由三个 NativeProbe 子进程组成：两个先后运行的写入进程针对同一 GUID 各写一代状态并退出，最后一个新进程重新创建 store/host，逐字节读回第二代最新内容。该 smoke 证明独立进程可恢复并更新原型文件状态，仍未启动 `IShellView`，因此不验证 Shell 真实销毁时的写流提交时序或应用会话恢复。

## 2026-10-26：内存 DC 字体资源生命周期

字体策略 smoke 现在额外创建内存 GDI DC，调用真实 `TrySelect` 将逐项样式字体选入，再执行与项目绘制后相同的 `Restore`，确认原字体重新成为当前对象后才销毁 DC。定向 `--shell-view-color-policy-smoke` 通过。该结果覆盖 GDI 句柄选择/恢复，不创建 Shell view，也不证明真实 DefView 通知或可见像素绘制。

## 2026-10-26：`GetCurrentFilter` 无效输出参数回归

完整 CDB3 vtable smoke 现在通过 `GetCurrentFilter` 槽测试空缓冲区和零容量，二者都应返回 `E_INVALIDARG`；零容量调用后原缓冲区哨兵保持不变。有效缓冲区仍返回空、NUL 终止的筛选字符串。定向策略 smoke 通过。这验证托管 COM 接口的输出参数处理，不创建 DefView 或证明 Shell 实际调用顺序。

## 2026-10-26：IncludeObject 的 provider 失败开放回归

过滤回调现有策略要求 Shell 元数据读取失败时保留文件。隔离 smoke 通过 `ICommDlgBrowser3` 的 `IncludeObject` vtable 槽，分别注入 `IFolderView::GetFolder`、`IShellFolder::GetAttributesOf`、`GetDisplayNameOf` 的失败 HRESULT；三种情况均返回 `S_OK`。有效元数据仍分别验证 `.txt` 返回 `S_OK`、`.bin` 返回 `S_FALSE`。定向策略 smoke 通过。此处是故障注入的 COM 回调测试，不代表真实 provider 错误表现或 DefView 枚举。

## 2026-10-26：导入过滤项经 CDB3 vtable 验证

`.txt` 应保留、`.bin` 应排除的 smoke 现在取得 `ICommDlgBrowser3` COM 接口指针，并从 `IncludeObject` 槽调用；模拟 `IFolderView` 及子 PIDL 经 CCW 进入与 native 调用方相同的委托边界。两项分别得到 `S_OK` 和 `S_FALSE`，定向策略 smoke 通过。此验证没有创建 `IShellView`，仍不证明 DefView 会按预期时序调用过滤器，也不证明可见结果或着色。

## 2026-10-26：逐槽验证 ICommDlgBrowser2/3 回调 ABI

颜色宿主的 CDB smoke 现取得专用 `ICommDlgBrowser3` 与 `ICommDlgBrowser2` 接口指针，分别按 vtable 调用 CDB3 全部九个回调和 CDB2 六个继承回调。断言验证 HRESULT、selection version 更新以及视图标志/空终止筛选输出。首次错误地用 `IShellBrowser` 指针索引 CDB3 vtable，返回无效结果；改为先对对应 IID 执行 QueryInterface 后通过。定向 smoke 通过。这是实际托管 COM vtable/CCW 验证，没有创建 DefView，也不证明 Shell 调用顺序。

## 2026-10-26：初始 CreateViewWindow 失败时执行匹配销毁

初始根 view 原先只有 `CreateViewWindow` 成功后才会进入 `DestroyViewWindow` 清理路径。现在在调用前记录尝试状态；即使调用失败且没有返回 HWND，finally 仍调用 `RetireAfterCreateAttempt`，避免直接释放可能部分初始化的 view/folder。调用前失败仍直接释放；成功返回却没有 HWND 则报告创建错误并销毁。

定向生命周期 smoke 覆盖通用创建尝试策略及 destroy 失败后的延迟释放，`--shell-view-color-policy-smoke` 通过。没有运行原生 view，因此没有人为触发 Shell 初始创建的部分失败。

## 2026-10-26：BrowseObject COM 边界转换导航异常

在上面的托管导航失败回归基础上，宿主现通过 `IStreamProbeBrowser` CCW 的实际 `IShellBrowser::BrowseObject` vtable 槽调用。导航委托抛出带 `0x80070005` 的 `COMException` 后，原生调用方收到同一 HRESULT；历史位置/index/count 不变，暂存目标副本已释放。定向 smoke 通过。该调用走真实托管 COM 可调用包装器，但未创建 `IShellView`，也不证明 Shell provider 导航成功/失败时的回调序列。

## 2026-10-26：导航回调抛异常时释放暂存 PIDL

`ShellViewNavigationHistory.BrowseObject` 在调用 view 创建/切换委托前保留目标 PIDL 副本。新增失败回归让委托抛出异常，并验证副本由 `finally` 释放、当前 PIDL 与 history index/count 不变，异常仍返回调用方处理。定向颜色策略/导航 smoke 通过。这是托管状态与分配所有权测试，不模拟 COM callable wrapper 的 HRESULT 转换，也没有调用 Shell view。

## 2026-10-26：CreateViewWindow 失败且没有 HWND 时仍执行销毁

导航失败清理此前只有 `CreateViewWindow` 返回了非零 HWND 才调用 `DestroyViewWindow`。创建函数可能在失败时没有写出 HWND；如果已进入创建调用，就应让 view 自己执行匹配的销毁路径。现在显式跟踪该调用是否发生：创建调用前失败时直接释放新接口，调用后失败时总是走 `ShellViewReferenceRetirement`，由 destroy HRESULT 决定立即释放或延迟到父窗口销毁。

隔离策略 smoke 覆盖创建调用前失败的直接释放，以及调用后即使没有 HWND 也调用 `DestroyViewWindow`；另验证这类 destroy 失败时 view/folder 都进入延迟释放队列。该项先以缺少 helper API 编译失败，接入后定向 smoke 通过。NativeProbe Release build 与完整 Release SettingsSmoke 通过。没有运行 Shell view，所以未模拟真实 provider 在部分创建失败时留下子窗口的行为。

## 2026-10-26：销毁失败时延迟释放视图和文件夹引用

导航创建新窗口并切换当前 view 后，旧 view 的 `DestroyViewWindow` 如果失败，旧实现仍立即释放 `IShellView` 与 `IShellFolder`，存在窗口继续回调已释放站点的风险。现在由 `ShellViewReferenceRetirement` 统一处理：销毁成功则立即释放；HRESULT 失败或调用抛异常则保留两项引用，直到父窗口确认销毁后释放。若父窗口按探针策略保留且仍有失败退休的 view，宿主 COM 指针也保持 AddRef；由于原生 COM 引用不会在线程退出时自动 `Release`，这条罕见失败路径会故意保留引用至进程退出。

策略 smoke 覆盖成功销毁、HRESULT 失败、抛异常及父窗口销毁后的引用释放；定向 smoke、NativeProbe Release 构建（0 警告/错误）和完整 SettingsSmoke 通过。它没有调用真实 `IShellView`，未观察 Shell 实际销毁失败或父窗口销毁失败。父窗口销毁失败时保留的 COM 引用是直到进程退出的故意泄漏，不声称线程退出会自动回收它们。产品仍使用 ExplorerBrowser，本策略尚未迁移到产品窗格。

## 2026-10-26：同位置导航不重复记录历史

阶段 1 的历史序列要求覆盖同位置访问。新增无窗口 PIDL smoke 使当前绝对位置与请求目标相同，验证历史仍只有一个 stop、index/PIDL 不变，同时宿主导航回调仍被调用。定向颜色策略/导航 smoke、NativeProbe Release build（0 警告/错误）及完整 Release SettingsSmoke 通过；本用例不创建 Shell view，未验证同路径 Shell 导航是否刷新视图或原生 TravelLog 行为。

## 2026-10-26：遵守 `SBSP_CREATENOHISTORY`

官方 `IShellBrowser::BrowseObject` 文档定义 `SBSP_CREATENOHISTORY`（SDK 值 `0x00100000`）为不向 travel log 新增导航 entry。共享历史此前仍会追加此类目标。现在成功时用目标 PIDL 替换当前停靠点并保持 index/count；若当前位置原有前进历史，则新位置使该分支失效并被清理。把当前 entry 替换视为“不增加 entry”的状态机模型是根据文档语义作出的推断，尚未与 Shell 原生 TravelLog 快照对照。

无窗口 smoke 构造 root→child→branch 后退到 child 的状态，先验证存在旧的 forward entry，再导航至 no-history 目标。修复前失败，修复后验证目标更新、index/count 不变且前进被拒绝；失败的 no-history 导航则保留原位置与 forward branch。额外组合 `SBSP_RELATIVE | SBSP_CREATENOHISTORY`，验证相对目标先归一化并保留 no-history 语义。定向颜色策略/导航 smoke、NativeProbe Release build（0 警告/错误）及完整 Release SettingsSmoke 通过。未创建 Shell view 或 GUI，产品 ExplorerBrowser 未改。

## 2026-10-26：通用导航历史接受相对 PIDL

契约对照发现 `ShellViewNavigationHistory` 原先把普通导航参数一律视为绝对 PIDL。`IShellBrowser::BrowseObject` 的 `SBSP_RELATIVE`（Windows SDK 值 `0x1000`）明确表示 PIDL 相对于当前文件夹；现由共享历史组件使用 `ILCombine(currentAbsolutePidl, relativePidl)` 生成自有绝对 PIDL，再调用宿主导航回调，并移除已归一化的类别标志。颜色宿主和筛选宿主都把 Shell 的 `ILCombine` 接入该组件。

内存 PIDL smoke 覆盖相对路径组合、回调中类别位清除和历史提交；颜色宿主 smoke 也覆盖其入口的相对导航、失败时历史保留及释放时清理 PIDL。定向 smoke 与 NativeProbe Release build 通过。此处未创建 Shell view，不能证明真实 Shell provider 传入相对 PIDL 时的导航效果；产品 ExplorerBrowser 不变。依据：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)。

## 2026-10-26：把粗体、斜体、下划线和删除线接入隔离绘制适配器

逐项规则解析出的四种字体属性现在传入 `NM_CUSTOMDRAW` 适配器。适配器读取当前字体描述，在行绘制期间创建并选入对应 GDI 字体，并在项目绘制回调后恢复原字体、释放临时句柄；未配置的属性继承当前字体，选中行仍交由 Shell 绘制。纯策略 smoke 覆盖全部样式、显式关闭和未配置继承，NativeProbe Release build 与定向 smoke 通过，0 警告/错误。

没有运行 `IShellView`、Private Desktop 或可见绘制探针，因此 HDC/字体生命周期在真实 DefView 通知中的表现、像素效果、其他视图模式和产品 ExplorerBrowser 均未验证。该改动只在隔离原型中，规则导入和产品配置没有改变。

## 2026-10-26：颜色探针接入原生项目过滤回调

颜色探针现使用 `ShellViewColorBrowserHost`，同一 COM 宿主暴露 `ICommDlgBrowser`、`ICommDlgBrowser2`、`ICommDlgBrowser3`，并在 `IncludeObject` 中调用现有 `ShellFolderFilter`。这是隔离原生视图的过滤方式，不等同于产品：ExplorerBrowser 产品路径在浏览器对象的 `IFolderFilterSite` 上安装过滤器；当前探针的原始 `IShellView` 对 `IFolderFilterSite` 返回 `E_NOINTERFACE`（既有本机记录），因此不能在该探针上复用同一个安装点。CDB2 `GetViewFlags` 在过滤启用时保持 `NOINCLUDEITEM` 清除，使隔离视图可以调用 `IncludeObject`。隔离夹具增加一个 `.bin` 排除项；运行探针时会要求过滤回调覆盖全部三项、可见项目保持为两项，颜色索引只从 Shell 当前可见条目建立。为让 NativeProbe 单独编译，项目链接了共享的 `HiddenItemsPreference` 定义。

无窗口 smoke 从 `IShellBrowser` 接口指针开始，实际调用 `QueryInterface` 验证宿主同时暴露三个 COM 回调接口；并验证过滤启用时不设置 `NOINCLUDEITEM`、Shell item 元数据不可用时回调失败开放并保留项目。新增假的 `IFolderView` / `IShellFolder` COM 夹具直接经过生产 `IncludeObject` 与 `ShellFolderFilter`：`notes.txt` 的返回值为 `S_OK`，`notes.bin` 为 `S_FALSE`。这验证了托管回调与规则筛选之间的连接，不模拟 DefView 枚举、Shell 过滤安装时序或颜色绘制。颜色策略 smoke 与 NativeProbe Release build 通过，0 警告/错误；完整 Release SettingsSmoke 和 App Release build 也通过。按本轮约束没有运行原生视图探针；真实 `IncludeObject` 回调、过滤结果、颜色索引及 Shell 过滤顺序仍未验证；产品 ExplorerBrowser 和 `.qdr` 导入未改变。

## 2026-10-26：未匹配规则和未设置属性保留 Shell 原生颜色

原型此前给每个未选中行都写入固定默认灰色背景，且只匹配文字色的规则也会覆盖背景色。现在策略仅在规则明确设置文字色或背景色时请求自绘；无匹配规则返回原生绘制，单属性规则只写入该属性，选中项继续保留 Shell 原生选择颜色。颜色策略 smoke 覆盖文字色单属性与未匹配项两个反例，先因 nullable 颜色状态不支持而失败，接入后通过。

`--shell-view-color-policy-smoke` 与 NativeProbe Release build 通过，0 警告/错误。这是策略与通知处理路径验证，未运行可见像素探针，尚未验证屏幕上未匹配项目确实保留系统主题颜色，也未接入产品 ExplorerBrowser。

## 2026-10-26：将逐项文字色接入隔离 NM_CUSTOMDRAW

颜色策略现将已解析的每项文字色和背景色一并送入隔离 `NM_CUSTOMDRAW` 处理器；选中行仍交给 Shell 原生选择绘制。之前策略 evaluator 已支持文字色，但绘制适配器只使用背景色并固定默认文字色。策略 smoke 新增两条未选中行的独立文字色/背景色检查，并保留 DefView `CDIS_SELECTED` 伪选中位与真实 `LVIS_SELECTED` 选择检查；先以缺少的接口触发编译失败，再接入。

`--shell-view-color-policy-smoke` 与 NativeProbe Release build 通过，0 警告/错误。本轮没有运行可见像素探针；实际屏幕上的文字颜色、字体样式、列视图表现及任何产品 ExplorerBrowser 行为仍未验证。规则字段格式和 `.qdr` 颜色导入也未改变。

## 2026-10-26：区分文件颜色规则与视图背景规则

Q-Dir 官方 [FAQ 47](https://www.softwareok.com/?faq=47&seite=faq-Q-DIR) 将 `#BG` 定义为视图背景，将 `#BG-A` 定义为活动视图背景。NativeProbe 现把它们交给独立的视图背景匹配/求值入口，不当作文件名通配规则；`#BG-A` 只命中活动视图，`#BG` 可为活动或非活动视图供色，并沿用同一逐属性优先级逻辑。无窗口策略 smoke 覆盖两个标记、活动状态、全局回退和高优先级锁定。

此记录后来已由下方“接入隔离 Shell 视图背景色”推进：解析出的颜色会应用到原生 `SysListView32`，未命中时传 `CLR_NONE` 让父窗口绘制系统默认背景。该实现仍仅在隔离 `IShellView` 原型中，真实背景像素、主题变化、`.qdr` 规则导入以及产品 ExplorerBrowser 均未验证。官方 [FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR) 示例公开 `useColor` / `useTreeColor` 开关；FAQ 47 说明需在保存收藏前启用颜色保存。官方页面仍未给出规则序列化字段，因此不推断 `.qdr` 导入格式。

## 2026-10-26：建模 Q-Dir 颜色规则的逐属性优先级

Q-Dir 官方 [FAQ 47](https://www.softwareok.com/?faq=47&seite=faq-Q-DIR) 和[官方旧版帮助页](https://www.softwareok.com/?seite=q-dir-hilfe)说明颜色表按顺序执行、最上方优先；文字色、背景色、粗体、斜体、下划线、删除线各自具有 `-1/0/+1` 状态。`-1` 将该属性交给较低优先级规则，`0` 锁定未设置，`+1` 锁定已设置。NativeProbe 现为六类属性保存独立三态值，并用同一个纯 evaluator 按规则顺序解析；隔离自绘消费文字色、逐项背景色和字体样式，`#BG`/`#BG-A` 视图背景也已连接到原生 ListView 背景消息。官方资料没有给出可核实的 Q-Dir 颜色规则 INI 行格式，因此不推断解析/导入行为。

无窗口策略 smoke 覆盖 continuation、两种锁定、不匹配/最终 continuation，以及同一项目上背景与文字颜色、字体样式的独立优先级。隔离像素夹具仍通过该 evaluator 为两条背景色规则解析颜色身份。策略 smoke、完整 Release SettingsSmoke、NativeProbe Release build、App Release build 均通过，0 警告/错误。本轮未运行 GUI/Private Desktop 像素探针；未接入产品 ExplorerBrowser、用户设置或 `.qdr` 导入。Q-Dir 配置存储格式、规则导入及用户可见效果仍未验证。

## 2026-10-26：接入隔离 Shell 视图背景色

`#BG-A` / `#BG` 解析结果现通过 `LVM_SETBKCOLOR` 发送到隔离视图的 `SysListView32`；创建导航后的新视图时重新应用。无规则时发送 `CLR_NONE`，让 ListView 将背景擦除交给父窗口，保留 Shell 默认系统色。颜色仍只存在于 NativeProbe 夹具，不进入产品设置或 ExplorerBrowser。

新增纯逻辑 smoke 覆盖已解析 COLORREF 原样转发、无规则默认回退及无效 HWND 拒绝。NativeProbe Release build 0 警告/错误，`--shell-view-color-policy-smoke` 通过。未创建 Shell view，因此真实 `LVM_SETBKCOLOR` 返回值、活动/非活动 pane 像素、各查看模式与 OS 主题变化尚未验证；`.qdr` 颜色规则序列化仍未知。

## 2026-10-26 复验普通颜色路径与控件基线

再次运行 `common-listview-color-baseline-private-desktop`，普通 `SysListView32` 在六种标准样式中均读取到两种预期颜色像素，watchdog 退出码 0，主桌面前台不变。随后运行 `color-private-desktop`，stock `IShellView` 的普通名称通配颜色在五种 Shell 模式与 Name 降序/Size 分组模式均通过逐项像素断言，菜单、选择与临时文件复制/重命名/删除检查完成，watchdog 退出码 0。日志分别为 `20261026-051237-da85e1e0.stdout.log` 与 `20261026-051416-998f4a03.stdout.log`。

这两轮确认普通规则的绘制和像素读取在本机可复现；它们没有运行 `#RO` 规则。只读规则的后续受控复验见下方“按 Q-Dir 官方通配规则匹配颜色目标”，通过 Private Desktop 像素断言，但不构成产品 ExplorerBrowser 集成或用户可见验收。

## 2026-10-26：按 Q-Dir 官方通配规则匹配颜色目标

Q-Dir [FAQ 6](https://www.softwareok.com/?faq=6&seite=faq-Q-DIR) 明确给出 `*.mp*` 通配示例、以分号连接多个扩展名，以及 `#` 隐藏项和 `#DIR` 文件夹标记。官方[英文功能清单](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)另将 `#RO` 标为只读项。隔离 NativeProbe 的 `ShellViewColorRuleMatcher` 按这些公开形式匹配显示名及目录/隐藏/只读类别；普通扩展模式采用不区分大小写匹配，且不把文件规则套到目录上。原型绘制前先按当前 Shell PIDL 顺序建立身份映射，再在纯绘制策略中查颜色，避免于 `NM_CUSTOMDRAW` 回调调用 Shell COM。

验证：`--shell-view-color-policy-smoke` 覆盖大小写、通配符、多扩展分号列表、空分段、目录、隐藏和只读标记；普通文件规则的 `color-private-desktop` 在五种列表模式及排序/分组后通过。旧只读试验在 view 创建后更改属性并调用 `Refresh`，虽有一次回调收到预期颜色但像素未命中，刷新后也没有逐项绘制通知；该次结果无法定位根因。

随后增加专用 `color-readonly-rule-private-desktop`：在创建 Shell view 前设置第一项只读，按 `#RO` 规则着色；第二项使用普通名称规则。Release NativeProbe 构建 0 警告/错误，watchdog 连续两次退出码 0。五种标准视图以及 Name 降序/Size 分组后的两项身份像素均通过；日志 `20261026-051538-ec567c71.stdout.log`、`20261026-051639-c1344976.stdout.log`。这验证 Private Desktop 默认 `IShellView` 原型中的文件属性读取、规则匹配、颜色映射与像素绘制链。由于旧/新测试顺序同时改变了属性设置时机和 Refresh 行为，不能把旧失败归因于唯一因素。它不是 ExplorerBrowser 产品集成，也不覆盖可见用户 UI；规则冲突优先级、设置持久化和其它布局/provider 仍未完成。

## 2026-10-25 修正排序/分组颜色像素断言

复跑 `color-private-desktop` 后发现，原像素断言固定假设项目索引 0/1 分别对应蓝/绿夹具；名称降序会反转 Shell 当前项目顺序。颜色回调映射其实每轮都按当前 PIDL 顺序重建，因此固定断言在排序/分组场景把正确颜色误报为 `0/0`。

探针现在按当前 Shell 索引的颜色映射统计每个 ListView 项目矩形中的像素。Release NativeProbe 构建 0 警告、0 错误；`scripts/native-probe-watchdog.ps1 -Probe color-private-desktop -TimeoutSeconds 45` 退出码 0。名称降序、大小升序分组时，Icon、Details、SmallIcon、List、Tile 均收到两条逐项绘制通知并在两行分别命中预期颜色；无排序/分组的五种同步模式也通过。该结果证明隔离默认 `IShellView` 的颜色可见性和索引映射在此夹具、私有 Desktop、本机五种布局下成立，不能外推到 ExplorerBrowser DirectUI、产品导航/焦点/拖放，也没有验证选中/悬停颜色与真实用户文件操作。

探针现会在排序/分组像素没有逐项通知或任一颜色未命中时直接失败，避免仅输出日志造成假通过。最终 Release 构建与私有 Desktop watchdog 复跑均通过。日志：`src/.state/native-probe-watchdog/20261025-184949-861101f3.stdout.log`。产品源码和发布目录未修改。

## 2026-10-25 隐藏宿主键盘探针前台边界复核

完整 `--ishellview-host-filter` 在隐藏夹具中通过筛选、Ctrl+A、方向键、Enter 及子视图创建后，报告 `foreground-unchanged=False`，随后因前台边界断言退出码 1。失败发生在子视图计数已为 1 之后；该轮不能作为前台隔离通过的证据。探针的 `finally` 清理路径执行，临时夹具未残留。当前没有证据能将前台变化归因到单一 `SetFocus` 或 Shell 调用，因此先不改探针行为，也不重跑会执行焦点操作的分支。

仅运行其前缀、在任何键盘焦点操作之前退出的 `--ishellview-grouped-filter-export` 通过：筛选回调 3 次、夹具视图含 `child` 与 `keep.txt`、分组视图导出得到相同两项；父窗口不可见、前台句柄不变、退出码 0。由此确认该无输入子路径可用；键盘加速器、焦点及导航链路的本轮结果仍受前台边界失败限制，须先安全定位焦点测试导致的状态变化。

源码顺序显示，完整探针只在 `IShellView::UIActivate(SVUIA_ACTIVATE_FOCUS)` 后立即检查前台；之后调用 `SetFocus(listView)`、`TranslateAccelerator(Ctrl+A)`，到选择结果输出时才再次读取前台。因此现有日志无法区分这几步。Microsoft 的 [`SetFocus` 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setfocus)说明它会激活接收焦点的窗口或其父窗口，这让 `SetFocus` 成为与现象一致的候选原因，但尚非单步实测结论。尝试把最小焦点检查放在新建的非输入 Desktop 上时，窗口清理未能在预期时间内完成；测试进程已终止，方案未通过，也未改仓库代码。当前不在用户桌面重跑这个焦点分支。

随后新增 `--ishellview-host-filter-private-desktop`，由 watchdog 启动一次性 NativeProbe 进程，在从不切换到输入桌面的独立 Windows Desktop 上运行完整宿主测试。Release 构建 0 警告/错误，`scripts/native-probe-watchdog.ps1 -Probe host-filter-private-desktop -TimeoutSeconds 40` 退出码 0：Ctrl+A 选中全部两项后清空选区，Down 将焦点索引从 0 移到 1，Enter 经 `OnDefaultCommand`/`BrowseObject` 创建子视图，筛选结果、文件目标拒绝、父目录、后退/前进及边界状态均正确；期间主桌面前台句柄保持不变。Shell view 和 COM 接口释放、测试夹具删除通过。私有 Desktop 的顶层窗口不能在此桌面显式 `DestroyWindow`；探针将其留给工作线程/一次性进程退出清理，因此 watchdog 进程边界是该模式的必要部分。此结果验证隔离宿主的合成键盘/导航路由，不替代 ExplorerBrowser 产品窗格的物理按键或可见焦点验收。

## 2026-10-25 按路线图复跑隔离原型

NativeProbe Release 构建成功（0 警告、0 错误）。通过 `scripts/native-probe-watchdog.ps1 -Probe color -TimeoutSeconds 40` 运行隐藏探针，进程退出码 0；父窗口不可见、前台句柄未变，产品 ExplorerBrowser 未创建。默认视图切换八种查看方式成功，背景/项目 Shell 菜单动词、`IFolderView2`、选区上下文菜单与 `IDropTarget` 查询均成功。

颜色样本仍未建立真实可见着色：21 次整体预绘制、17 次逐项通知中，只有平铺模式出现两条项目通知；矩形为零、裁剪区为 `NULLREGION`、目标颜色像素为 0。此次也没有执行菜单动词或文件操作，所以仅能说明相关原生接口和菜单仍可查询，不能据此宣称原生文件操作完整保留。`focus-restored=False` 来自隐藏原型，不能当作用户可见焦点结论。产品架构未改；屏幕内隔离像素采样及实际操作仍待可操作的桌面验收时段。

## 2026-10-25 watchdog 隔离复测

运行 `scripts/native-probe-watchdog.ps1 -Probe color -TimeoutSeconds 40`，NativeProbe 退出码 0。默认 `IShellView` 再次切换八种模式，并查询到背景 View/GroupBy/Refresh/New/Properties 与排序动词，以及项目 Cut/Copy/Paste/Delete/Rename/Properties 菜单；选区、`IFolderView2` 和 `IDropTarget` 查询成功。父窗口不可见、前台句柄未改变，产品 ExplorerBrowser 未创建。

该次收到 21 次整体预绘制与 17 次逐项通知；按每种模式采样时仍只有平铺模式收到两条项目通知。其项目矩形为零，`GetClipBox` 返回空区域，目标色像素计数为零。列表控件报告默认白背景、黑文字，但这是隐藏隔离视图的控件颜色值，不是 DirectUI 或可见产品视图的像素证据。`focus-restored=False` 也不作为用户可见焦点结论。屏幕内无遮挡颜色像素验收仍未运行；生产宿主未更改。

## 2026-10-25 按路线图复跑原型

按用户选择继续用隔离默认 `IShellView` 验证颜色钩子与原生操作兼容性。`dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --ishellview-color-spike` 退出码 0，报告 `native-view-and-context-menu-retained=True`：八种视图模式可切换；背景菜单包含查看、分组、刷新、新建、属性与排序命令；项目菜单包含剪切、复制、粘贴、删除、重命名、属性；选区和 `IDropTarget` 可查询。

颜色方面，隐藏回调在详细信息和平铺等模式被观察到，但只有平铺模式收到两条逐项通知；通知矩形均为 `0,0,0,0`，裁剪区为空，回调读出的目标像素仍为 `0x000000`。`synthetic-color-contract=True` 只验证探针能改写合成的自绘结构，不是 Shell 实际着色的证据。此次没有创建可见窗口或触碰生产 ExplorerBrowser；所以结论限于探测到的原生能力仍在，颜色是否可见仍待获准的屏幕内隔离采样。

本次复跑前，NativeProbe 被 `ViewDataProbe` 内两次同名局部变量挡住编译；将第二处改为 `groupedThenFilteredNames` 后探针正常构建和运行。该修正仅消除同方法局部变量重名。

## 2026-10-25 隐藏探针复跑

从当前工作树执行 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --ishellview-color-spike`，退出码 0。探针再次创建独立临时夹具和默认 `IShellView`，读到 `SysListView32`/`SysHeader32`，完成八种视图模式切换、原生背景/选中项菜单查询；目标色通知处理器收到 21 次整体预绘制和 17 次逐项通知，但只在平铺模式收到两条逐项通知。对应 `NMLVCUSTOMDRAW.rc` 仍为零矩形，回调 HDC 类型为 3，但裁剪框为 `NULLREGION`；所有行像素和位图目标色像素计数均为 0。排序/分组读回后项目身份匹配成功，但该离屏打印路径没有逐项颜色回调。最终报告 `parent-visible=False`、`foreground-unchanged=True`、`visible-test=False`、`product-untouched=True`。

这次复跑确认隐藏探针和隔离边界工作正常，没有新增颜色可行性证据。当前用户授权的是继续源码工作，未授权打开可见测试窗口；因此不运行 visible 入口，也不将颜色处理器接入生产 ExplorerBrowser。要判断逐项颜色是否实际绘制，下一步仍需一次获准的屏幕内、无遮挡隔离采样；即便像素通过，生产宿主兼容性仍需另行评估。

## 最新测量修正（2026-10-25）

此前记录把 `CDDS_ITEMPREPAINT` 回调结束后再调用 `GetObjectType` 得到的 HDC 类型 0，解释为 Shell 没有提供有效 HDC。该检查发生在通知返回之后，超出了 `NMCUSTOMDRAW.hdc` 的有效期，结论不成立；旧记录中“绘图上下文无效”的说法应视为已被本节取代。

NativeProbe 现于回调期间检查 HDC，并请求 `CDRF_NOTIFYPOSTPAINT`，尝试在逐项后绘制回调中读取像素。屏幕外 `WS_VISIBLE` 运行退出码 0，前台窗口未改变，产品窗格未创建；Release 构建 0 警告、0 错误。实际回调当时 `GetObjectType(hdc)` 为 3（`OBJ_DC`，是有效 DC），但 Shell 提供的逐项矩形仍为空；为避免对无效坐标调用 `GetPixel`，探针将像素记为 `0xFFFFFFFF`（未采样哨兵）。八种模式的逐模式采样仍没有建立稳定的“目标行像素具有请求颜色”证据。

追加记录每条回调的发送 HWND、阶段、原始 `rc` 与 HDC 裁剪框后，屏幕外运行收到的回调均来自目标 `SysListView32`，HDC 类型仍为 3，但 `rc` 和 `GetClipBox` 均为 `0,0,0,0`（裁剪类型 `NULLREGION`）。这说明 `WS_VISIBLE` 加屏幕外定位仍没有形成可绘制区域；空矩形来自当前离屏采样环境，不能归因于 `IShellView` 不支持行几何或颜色。

因此这次证据修正了隐藏探针对 HDC 生命周期的误判，也定位了屏幕外采样的边界，但没有证明颜色可行或不可行。剩余关键问题是在有效绘制区域内取得逐项颜色像素；仅靠当前离屏场景不能回答。此前回调结束后测得的 HDC 类型值也不能替代回调内测量。

## 目的与范围

验证能否在不替换原生 Shell 文件视图的情况下，对每项设置 Q-Dir 风格的文字和背景颜色。原型仅在 `ExplorerDirs.NativeProbe` 中创建临时目录、隐藏父窗口和默认 `IShellView`；不创建 ExplorerBrowser、不操作生产窗格、不注入到其他进程，也不接触用户目录。

运行方式：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --ishellview-color-spike
```

在另有明确桌面授权时，可用 `--ishellview-color-spike-visible --AllowInteractiveDesktopControl` 打开仅含临时夹具的原生视图 5 秒，窗口使用不激活样式，并核对前台句柄不变。可见入口会清除夹具选区，依次切换八种查看方式并请求重绘；每种方式都会从隔离 ListView 的窗口 DC 复制像素到临时内存位图，统计两条测试行是否实际出现各自目标颜色，并将收到的绘制索引映射到夹具 PIDL。不会截取桌面或保存图像。窗口被其它内容遮挡时，像素采样可能受裁剪，必须确保测试视图区域无遮挡。此入口只编译、不运行；没有桌面授权时继续使用隐藏模式。

## 当前机器的探针结果（2026-10-25）

- 默认 `IShellView` 的窗口树包含 `SysListView32` 与 `SysHeader32`。
- `IFolderView2` 查询成功，背景 `IContextMenu` 查询成功；默认 Shell 查看和背景操作接口仍在。
- 通过 `IFolderView::SelectItem` 选中唯一临时条目后，选中数为 1，`IShellView::GetItemObject(SVGIO_SELECTION, IID_IContextMenu)` 查询成功；对 `IShellView` 的 `IID_IDropTarget` 查询也成功。这里只证明接口存在，不调用菜单命令或执行拖放。
- 隔离探针现在会在内存菜单上调用背景/选中项的 `IContextMenu::QueryContextMenu` 并只读枚举规范动词，不显示菜单、不调用命令。当前系统背景菜单提供 `View`、`GroupBy`、`Refresh`、`New`/`NewFolder`、`Properties`，并提供 `arrange`、`sortascending`、`sortdescending` 排序动词；选中项菜单提供 `Cut`、`Copy`、`Paste`、`Delete`、`Rename`、`Properties`。这证明默认视图能提供这些原生菜单入口，不证明菜单视觉合并、启用状态正确、命令执行或文件操作结果。
- 隔离探针还复用产品链接进来的 `ShellFileCommands.Query` 和 `NativeShellCommandMenu` 检查原生启用状态：当前背景查看、分组、刷新、新建、属性及排序动词都启用；选中项的复制、删除、重命名、属性启用；生产适配器报告新建文件夹启用。该次运行中 Shell 将粘贴和撤销报告为不可用，探针未读取或更改剪贴板，也未调用任何动词。这个结果比接口查询更接近产品命令状态路径，但仍不证明 UI 按钮/菜单显示状态、实际命令执行或文件操作完成。
- 探针使用产品 `ViewStyle.All` 和产品同款 `IFolderView2::SetViewModeAndIconSize` 路径，依次切换超大图标、大图标、中等图标、小图标、列表、详细信息、平铺、内容八种模式；每次都读回预期模式/图标尺寸，两个临时条目和一个选中项均保留。这里验证的是原生模式切换和状态保持，不代表产品已改用 `IShellView`。
- 父窗口保持隐藏，探针前后前台窗口一致，临时夹具位于探针输出目录并在退出时清理。
- 颜色处理器原型已按 Windows ListView 的 `NM_CUSTOMDRAW` 协议实现：整体预绘制时申请逐项通知，在项目预绘制阶段按项目索引写入不同的 `clrTextBk`，并设置 `clrText`。
- 隐藏探针通过直接向已挂钩通知父级发送合成 `WM_NOTIFY/NM_CUSTOMDRAW`，验证了 `CDDS_PREPAINT → CDRF_NOTIFYITEMDRAW`，以及索引 0/1 各返回不同背景色和 `CDRF_NEWFONT`。这是处理器与结构布局的单元级验证，不代表真实绘制已采用这些颜色。
- 当前隐藏运行中，真实平铺模式两条 `NM_CUSTOMDRAW` 项目通知的 `dwItemSpec` 可通过 `IFolderView::Item` 取回，并与两个已知文件的 Shell 子 PIDL 精确匹配。探针只用同一轮绘制快照里的索引做映射，避免后续模式消息串入。另以不同大小的临时文件分别设置名称倒序、大小升序、大小分组；`IFolderView2::GetSortColumns` 和 `GetGroupBy` 读回所设状态，随后逐项读取当前 `IFolderView::Item` PIDL。名称倒序读回绿色文件在蓝色文件前，大小升序读回蓝色文件在绿色文件前，分组状态下两个当前索引也都能精确映射回各自文件。由此验证的是“每次按当前索引重新查询 PIDL”在这些状态下可用，不是通知索引永久稳定；排序/分组后的离屏打印没有产生逐项 `NM_CUSTOMDRAW` 回调，因此没有验证这些状态下着色通知或真实颜色像素。
- 临时目录中的两项文件都已由 Shell 枚举到。ListView 的通知直接父窗口是 `SHELLDLL_DefView`，创建时它没有 `WS_VISIBLE`。探针仅在临时视图中给列表子窗口设置 `WS_VISIBLE`（祖先仍隐藏），并分别清零计数后按八种模式请求 `WM_PRINTCLIENT`。`LVM_GETITEMRECT` 能读到有效项目矩形（详细信息模式为两条 24 像素行）；在打印调用返回后立即快照通知后，只有平铺模式在该请求期间收到两条项目通知，二者 `NMLVCUSTOMDRAW.rc` 为零，HDC 也不等于传入的位图 DC。32 位 DIB 的项目区域及整图均未发现目标背景色像素。由于祖先窗口仍隐藏且通知没有指向该离屏 DC，当前打印请求不能作为颜色输出证据；这既不验证也不否定窗口可见时的 `NM_CUSTOMDRAW` 着色路线。

微软文档说明，ListView 可通过 `NM_CUSTOMDRAW` 在 `CDDS_ITEMPREPAINT` 设置项目颜色；详细列表还可请求 `CDDS_ITEMPREPAINT | CDDS_SUBITEM` 对单元格分别着色：[NM_CUSTOMDRAW](https://learn.microsoft.com/en-us/windows/win32/controls/nm-customdraw)、[ListView NMLVCUSTOMDRAW](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/ns-commctrl-nmlvcustomdraw)。因此，原生 DefView 的颜色路线有可行性，尚不能称作已完成原型验收。

## 对产品路线的含义

生产文件窗格目前基于 ExplorerBrowser；它在现有探针中呈现的子窗口和本次直接创建的默认 `IShellView` 不同，不能据本次结果直接套用 `SysListView32` 消息或切换产品架构。这个实验表明，直接托管 Shell 默认视图可能同时保留 `IFolderView2` 和背景菜单，并存在利用 ListView 自绘通知着色的候选接入点。

在改动生产窗格前，还需要一个获准的可见隔离测试确认：通知确实到达宿主、目标文件行和各子项索引稳定可映射、选择/焦点/排序/分组/滚动/原生菜单及文件操作没有回归。若公开系统设置、主题或 Shell 扩展导致默认视图切换成非 ListView 子窗口，颜色实现必须降级而不能破坏浏览功能。

### 从隔离宿主迁移到产品宿主的差距（2026-10-25）

源码对照后确认，`StreamViewHost` 只提供 `IShellBrowser` 的最小探针实现：菜单/状态栏回调为空操作，`TranslateAcceleratorSB` 让 Shell 继续处理，`BrowseObject` 不支持，视图状态流只读/写探针内存；它没有产品所用的 `ICommDlgBrowser3` 过滤、通知及列点击回调，也没有 ExplorerBrowser 的导航完成事件。产品当前由 ExplorerBrowser 创建视图，再通过 `OnViewCreated` 附加窗口、恢复状态并维护导航/历史、筛选和每标签生命周期。因而即使可见隔离视图确认颜色像素可行，也不能直接把这个探针宿主替换进生产；必须先补齐等价 `IShellBrowser`/浏览器服务契约，并逐项迁移产品的导航、状态流、筛选、预览、快捷键及释放路径。此项属于独立宿主架构工作，不能与颜色钩子验证合并估算。

### 源码接口差异复核（2026-10-25）

逐项核对后，隔离 `StreamViewHost` 实现了 `IShellBrowser` 的方法布局；`BrowseObject` 在没有导航委托时返回 `E_NOTIMPL`，`GetControlWindow`/`SendControlMsg` 与 `TranslateAcceleratorSB` 可委托给外层，但尚无产品控制窗映射或完整消息循环。`SetStatusTextSB` 复制/清除状态并通知订阅者。菜单合并、工具栏回调仍未接管产品 UI；`GetViewStateStream` 使用隔离内存/状态文件而非产品会话编排。它同时提供 `IShellServiceProvider`，仅为视图状态流查询探针接口或按 IID 暴露自己的 CDB 回调。

本轮在直接创建的默认 `IShellView` 上只读查询 `IFolderFilterSite`，结果为 `E_NOINTERFACE`（`0x80004002`）。这与 ExplorerBrowser 对象上的筛选挂接点不同，说明现有 `IFolderFilterSite.SetFilter` 不能直接用于此独立视图；此结果不排除通过宿主 `ICommDlgBrowser3.IncludeObject` 实现普通项目过滤，也没有测试普通隐藏项枚举标志能否由独立宿主控制。

## 2026-10-25 独立宿主过滤回调探针

新增隐藏入口 `--ishellview-host-filter`，使用继承共享 `StreamViewHost` 的专用宿主实现 `ICommDlgBrowser`、`ICommDlgBrowser2` 和 `ICommDlgBrowser3`，共享宿主及生产 `ShellBrowserSite` 均未改写。初版只实现 v3 时，`IncludeObject` 回调数为 0、3 项全显示；补齐 v1/v2/v3 接口集后，Release NativeProbe 实际收到 3 次 `IncludeObject`，并复用生产 `ShellFolderFilter` 的名称匹配逻辑，最终从临时 `keep.txt`、`filter.bin` 和 `child` 中显示 `keep.txt` 与 `child`。父窗口不可见，前台未变化，探针退出码 0。

这证明独立 `IShellView` 可由宿主 `ICommDlgBrowser3.IncludeObject` 承担普通项目筛选，也证明了默认保留文件夹的行为可接入。它没有调用 ExplorerBrowser 上的 `IFolderFilterSite`，不证明隐藏文件枚举能由此控制，也不覆盖生产导航/历史/焦点集成、可见视图或多窗格性能。后续宿主评估应保留两条接口职责的区别：ExplorerBrowser `IFolderFilterSite.SetFilter` 和独立视图 `ICommDlgBrowser3.IncludeObject`。

### 文件夹“打开”触发方式检查

另一次仅针对同一临时子文件夹的探针尝试，从选中项原生菜单查询 `open` 规范动词并调用。Shell 返回成功，但隔离宿主的 `IShellBrowser.BrowseObject` 计数仍为 0；因此这不构成导航成功证据，且该动词路径可能交由外部 Shell 处理。为避免在无桌面授权阶段继续触发有副作用的打开动作，已从探针移除这段调用及其临时代码。实际导航路由仍待使用受控宿主消息或之后获准的可见隔离验收验证。

随后改为向隔离视图的 `SysListView32` 直接发送 `WM_KEYDOWN/WM_KEYUP VK_RETURN`，不调用菜单动词。宿主 `OnDefaultCommand` 从焦点项取得相对 PIDL，与临时根 PIDL 组合后交给 `BrowseObject`；该回调绑定目标文件夹、创建新的 `IShellView`，再由探针切换到新视图并销毁旧视图。Release NativeProbe 实际读到 `default-command-calls=1`、`browseobject-calls=1`、`browse-target-is-child=True`；新子目录视图经过筛选后显示唯一的 `inside.txt`。探针再将根 PIDL 交给 `BrowseObject`，重建根视图并读回两个可见条目；普通文件目标被拒绝时，当前子视图及其唯一条目保持不变。父窗口不可见、前台未变，探针退出码 0。由此验证了隔离宿主中的一级往返导航、新视图枚举和非文件夹目标拒绝；尚未覆盖 Windows Shell 历史栈/后退标志、创建新视图期间的故障恢复、产品标签状态/焦点集成或可见键盘体验。

产品侧则由 `ExplorerBrowserHost` 初始化、设置选项并订阅 `IExplorerBrowserEvents`。导航 pending 会停监视器、结束旧请求并捕获当前标签视图状态；`OnViewCreated` 应用列标题选项并附加 Shell 窗口；导航完成再校验导航代数并发布位置。附加的 `ShellBrowserSite` 实现 `ICommDlgBrowser3`，承担 Shell 状态变化/选区通知、筛选开关与当前筛选文本等回调。

这确认了颜色探针只证明“默认 Shell 视图可提供原生菜单、视图与拖放接口，以及候选绘制通知”，没有覆盖产品导航/筛选/视图状态合同。若以后要继续验证宿主迁移，应先建立新的隔离宿主契约探针，逐项测试导航往返、过滤、Shell 状态流、命令加速键及窗口销毁；只有这些行为通过后，颜色收益才足以进入架构评估。该静态复核没有改生产代码，也没有改变“可见颜色未证实”的结论。

## 当前状态

**Spike Release 编译及隐藏运行通过；已验证八种 Shell 视图模式、原生菜单能力和合成 `NM_CUSTOMDRAW` 处理器契约。隐藏打印请求未建立“Shell 绘制通知 → 传入位图 DC”的有效路径，且离屏图像没有目标颜色像素，因此逐项着色仍未验证；可见行为与生产 ExplorerBrowser 兼容性也未验证。探针的最小 `IShellBrowser` 宿主与生产 ExplorerBrowser 回调/生命周期尚有明显差距；未修改生产文件视图。**

## 2026-10-25 隐藏平铺通知的文件身份映射

为确认绘制通知的 `dwItemSpec` 能否用于查询真实 Shell 项目，隐藏探针现对同一轮 `WM_PRINTCLIENT` 快照中的逐项索引调用 `IFolderView::Item`，并把返回的子 PIDL 与两个已知夹具文件的绝对 PIDL 子项比较。Release NativeProbe 实际观察到平铺模式索引映射为 `blue-rule-target.txt` 与 `green-rule-target.txt`；其它七种模式未收到逐项通知。探针也捕获并排除了不同视图模式间迟到通知导致的旧索引误配。父窗口不可见、前台未变，生产文件视图未创建。此证据只覆盖本机当前未分组、平铺模式，不证明排序/分组后索引持久性、真实颜色像素、可见焦点或 ExplorerBrowser 兼容性。

可见入口现会直接读取隔离 ListView 窗口 DC，按八种查看方式逐项统计颜色像素，并将实际绘制回调中的项目索引映射到临时夹具 PIDL；测试前会清除选区以免选择高亮遮住颜色。当前只完成 Release 编译及隐藏分支回归，尚未运行可见入口；这段代码仍不提供逐项着色可行性的运行证据。

## 2026-10-25 可见像素探针的选择标志校正

源码复核发现，可见像素路径原先把 `0x10` 注释为 `SVSI_DESELECT`；该值实际为 `SVSI_FOCUSED`，会令选择高亮遮挡颜色采样，并可能改动焦点。现改为仅传 `SVSI_NOTAKEFOCUS`（取消选择对应的 `SVSI_DESELECT` 值为 0），并在采样前要求 Shell 选中数确实为零。隐藏探针与 Release 构建通过，但二者不会进入可见采样分支；颜色像素以及可见焦点影响仍未验证。

## 2026-10-25 隐藏探针复测

复跑 `--ishellview-color-spike` 退出码为 0，前台句柄未变且父窗口不可见。八种模式切换、两项枚举及选中项保持、背景/选中项 Shell 菜单和 `IDropTarget` 查询均再次成功。探针读取到的临时默认视图背景/文本/文本背景为白/黑/白；它没有导入 `.qdr`，所以不能解释应用内的导入后黑底。自绘统计再次为整体预绘制 5 次、逐项绘制 2 次；逐项通知仅发生在平铺模式，两个条目的矩形均为零，通知 HDC 与 `WM_PRINTCLIENT` 传入的位图 DC 不同，目标行和整图中目标颜色像素均为 0。可见 IShellView 测试仍未运行。

这次重复确认隐藏打印路径仍不能证明项目行颜色；它也不否定可见窗口中 ListView 的逐项通知。生产 ExplorerBrowser 没有被创建或修改。

## 2026-10-25 再次复跑隐藏颜色原型

通过 `scripts/native-probe-watchdog.ps1 -Probe color -TimeoutSeconds 40` 在独立隐藏进程复跑，退出码 0，父窗口不可见且前台句柄未变。默认视图仍提供两项夹具、八种原生查看方式、背景/选中项/文件夹菜单动词和 `IDropTarget`；原生菜单查询约 111–262 ms。自绘观察合计收到 21 次整体预绘制和 17 次逐项通知，但这些通知分布于强制绘制和状态检查，不能视为八种模式都收到回调；按各模式采样时仍只有平铺模式收到两条逐项通知。其项目矩形均为零，通知 HDC 不等于采样位图 DC，目标颜色像素仍为零。排序/分组后的 PIDL 映射检查通过，但没有对应绘制回调。

本次仅复核隐藏实验，未显示任何窗口、未操作 ExplorerBrowser、未修改生产视图。逐项颜色仍须等之后获准且桌面空闲时运行已有的可见隔离采样入口；当前不能据隐藏结果决定是否替换产品视图架构。

同轮探针还用 `GetObjectType` 检查了通知 HDC 与目标位图 DC：逐项通知的 HDC 类型返回 0（无效 GDI 对象），采样位图 DC 返回类型 10（有效内存 DC）。这把隐藏采样失败进一步定位为 Shell 在隐藏绘制路径中没有提供有效绘图上下文；不是目标色扫描器只漏看了行区域。真实窗口的绘制通知和像素仍须可见隔离采样确认。

像素读取前现调用 `GdiFlush` 刷新本线程的 GDI 批次，避免可见采样在 `BitBlt` 尚未提交时读取 DIB。隐藏复测中刷新成功，通知 HDC 仍为无效类型 0、目标 DIB DC 仍为类型 10；因此此改动只修正采样同步，不改变隐藏 Shell 未提供有效绘制上下文的结论。微软 [`GdiFlush`](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-gdiflush) 文档说明它刷新调用线程的当前批次。

## 2026-10-25 隔离探针再次复核

Release 构建后再次运行 `--ishellview-color-spike`，退出码 0，父窗口仍隐藏、前台句柄未变，生产文件视图未创建。八种模式、背景及文件/文件夹菜单能力和排序动词再次通过；本次 Shell 菜单准备耗时约为背景 209 ms、文件选择 96 ms、文件夹选择 207 ms。这只是本机单次观测，不构成性能基准或端到端菜单响应结论。

按视图模式采样时仍只有平铺模式收到两条逐项通知并映射到两项夹具 PIDL；通知 HDC 类型为 0、项目矩形为空、目标颜色像素为 0。隐藏采样报告 `focus-restored=False`，不能据此断言可见焦点行为。当前证据仍未建立“Shell 实际绘制 → 有效通知 HDC → 目标行颜色像素”的链路；下一道有效关卡是获准后的可见隔离采样。

## 2026-10-25 隔离原型复跑

再次以 Release 构建并通过 `scripts/native-probe-watchdog.ps1 -Probe color -TimeoutSeconds 40` 在独立隐藏进程运行。构建 0 警告、0 错误；探针退出码 0，父窗口不可见、前台句柄未变，且报告 `product-untouched=True`。本轮仍读回八种 Shell 视图模式，背景菜单包含查看、分组、刷新、新建、属性及排序动词，选中项菜单包含复制、删除、重命名和属性；对应生产命令适配器将复制、删除、重命名、属性、新建文件夹报告为可用。粘贴和撤销/重做在本次 Shell 状态查询中不可用，探针未调用这些命令。

颜色链路没有新证据：平铺收到两条项目通知且能映射至两个夹具文件，其余七种视图没有项目通知；通知 HDC 的 GDI 类型仍为 0，采样位图 DC 类型为 10，项目通知矩形为空，逐行及整图目标颜色像素均为 0。因而本轮确认的是隐藏探针可重复运行及原生 Shell 功能接口仍在，既不能判定可见 `NM_CUSTOMDRAW` 是否能着色，也不能支持将生产 ExplorerBrowser 替换为 `IShellView`。

## 2026-10-25 屏幕外可见窗口采样

为避免使用用户桌面输入，同时检查 Shell 是否只在 `WS_VISIBLE` 时提供真实绘制，新增 `--ishellview-color-spike-offscreen` 隔离入口。窗口按虚拟桌面原点放置到桌面边界之外，使用 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，探针不发送键盘或鼠标输入，并在结束前检查前台句柄未变。Release 构建 0 警告、0 错误；`scripts/native-probe-watchdog.ps1 -Probe color-offscreen -TimeoutSeconds 40` 退出码 0，父窗口可见但位于屏幕外，前台保持不变。

可见窗口创建后，按视图项目索引调用 `IFolderView::SelectItem(index, SVSI_DESELECT)` 清除夹具选区；此前通过 `IFolderView2` 的 PIDL 重载清除时返回 `E_BOUNDS`，所以这次采样尚未覆盖非空选区情况下的行为。八种模式切换完成；只有“内容”模式收到两条真实项目通知，全部模式均未采到目标颜色像素（每项 0/0），因此屏幕外 `WS_VISIBLE` 仍不能形成逐项颜色的有效证据。该结果不能替代屏幕内、无遮挡窗口的可见采样，也不能据此断言屏幕内绘制行为。

结论仍是颜色功能未验证；这轮确认的只是隔离窗口创建、原生模式/菜单查询、视图切换和前台不变。没有创建 ExplorerBrowser 或改动产品视图，也没有运行 Q-Dir 或真实桌面 GUI。

## 2026-10-25 屏幕外 `PrintWindow` 绘制对照

在现有屏幕外可见入口中增加 `PrintWindow(PW_CLIENTONLY)` 对照，以便区分“ListView 未在屏幕外桌面产生有效像素”和“Shell 没有通过自绘通知提供逐项颜色”的情况。Release NativeProbe 构建 0 警告、0 错误；由 watchdog 独立进程运行退出码 0，隔离父窗口位于虚拟桌面外，前台未变化，报告 `product-untouched=True`。原有隐藏模式复跑也退出码 0。

屏幕外窗口的 `PrintWindow` 对 8 种查看模式均返回成功，但每种模式的打印调用均没有逐项 `NM_CUSTOMDRAW` 通知，目标行颜色像素为 `0/0`。直接读取列表窗口 DC 的视图切换采样中，只有“内容”模式出现两条逐项通知；两项颜色像素仍为 `0/0`。因此 `PrintWindow=True` 只说明窗口 API 接受了捕获请求，不证明 Shell 已输出完整内容，也没有形成有效颜色回调到目标像素的证据。隐藏窗口仍只有平铺模式出现的无效通知 HDC；产品 ExplorerBrowser 未创建或修改。

本轮没有取得能继续判定 `NM_CUSTOMDRAW` 兼容性的屏幕像素证据；下一道有效关卡仍是桌面空闲且获准时的屏幕内无遮挡隔离采样。路线图和产品架构未改。

## 2026-10-25 用户选择后的隔离原型复跑

按用户确认先验证独立 `IShellView` 原型，本次仅运行 `dotnet run ... -- --ishellview-color-spike` 隐藏入口；进程退出码 0，父窗口不可见、前台句柄未变，报告 `product-untouched=True`。没有创建生产 `ExplorerBrowserHost`，也没有显示 GUI 或发送输入。

Shell 默认视图再次读回八种查看方式，并保留背景/文件/文件夹菜单能力和 `IDropTarget`。菜单查询本次约为背景 551 ms、选中项 169 ms、文件夹 331 ms；这是单次探针开销，不是用户交互性能指标。逐项自绘消息总计 17 次，但按单个查看方式采样时仍只有平铺收到两条可映射到夹具文件的回调；通知 HDC 的 GDI 类型为 0，项目矩形为空，行与位图的目标色像素均为 0。`focus-restored=False` 属于隐藏探针状态，不能推断可见焦点体验。

因此本次确认原型的 Shell 浏览接口仍在，但没有证明真实绘制接受逐项颜色。不会据此替换生产 ExplorerBrowser。下一步颜色结论需要桌面空闲时获准的屏幕内无遮挡隔离采样；在此之前该颜色路线保持“候选、未验收”。

## 2026-10-25 用户选择后的屏幕外复测

用户确认先继续隔离 `IShellView` 原型后，使用已有 `--ishellview-color-spike-offscreen` 分支复跑；探针进程退出码为 0，父窗口位于桌面外，前台句柄未变，`product-untouched=True`。本轮选择清理与焦点恢复报告均为 True；八种查看模式均可切换并保留选中项，背景/选中项菜单接口、常用菜单动词、`IFolderView2` 与 `IDropTarget` 仍可查询。菜单/颜色检查不调用文件命令或拖放。

本轮屏幕外可见视图在“内容”模式收到两条可映射至夹具文件的逐项通知；其它七种模式没有逐项通知。所有模式目标色像素均为 0/0；`PrintWindow` 返回成功但未收到逐项通知，不能据此认定实际视图内容已绘入捕获图像。不同运行中收到逐项通知的视图模式不一致（此前在“平铺”模式观察到两条），说明回调时机依赖视图/绘制状态；目前没有“逐项通知稳定到达所有模式且对应行像素采用请求颜色”的证据。

这次加强了“原生浏览模式、选区和菜单查询接口仍可用”的隔离证据，但没有改变着色结论。它仍不证明屏幕内未遮挡时的自绘效果，也不验证生产 ExplorerBrowser 的子控件、逐项绘制兼容性或交互体验；颜色实现与宿主迁移均保持未决，路线图未变。

## 2026-10-25 用户选择后的隐藏入口复测

再次运行 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --ishellview-color-spike`，退出码 0。父窗口不可见、前台句柄未变，报告 `product-untouched=True`；本次没有运行屏幕外或屏幕内可见入口。

八种模式均读回预期视图状态；背景原生菜单包含查看、分组、刷新、新建、属性及排序命令，选中项菜单包含复制、粘贴、删除、重命名和属性命令。当前 Shell 将粘贴及撤销/重做报告为不可用，探针没有读取剪贴板或调用命令。自绘仍未建立真实颜色证据：按模式采样时仅“平铺”出现两条项目通知，并映射到两个临时文件；该轮的 HDC 类型值是在通知回调结束后读取，不能用于判断 HDC 有效性。后来在回调期间读取为 `OBJ_DC`，但项目矩形/裁剪区为空，像素不可采样。隐藏探针报告 `focus-restored=False`，不能据此推断可见焦点行为。

结果与前次隐藏复测一致：它支持继续保留原生 `IShellView` 作为颜色候选原型，但不足以决定生产窗格迁移或声称逐项颜色可用。由于本阶段明确不使用桌面交互，下一次有区分力的颜色验证仍需另行进行可见隔离采样；生产 `ExplorerBrowserHost`、路线图和发布包均未修改。

## 2026-10-25 再次运行屏幕外可见入口

以当前 Release NativeProbe 构建运行 `scripts/native-probe-watchdog.ps1 -Probe color-offscreen -TimeoutSeconds 40`，构建 0 警告、0 错误，探针退出码 0。隔离 Shell 父窗体使用 `WS_VISIBLE` 并放置在虚拟桌面以外，运行前后前台句柄不变；没有创建生产 ExplorerBrowser、没有桌面输入，临时夹具在退出时清理。八种查看方式均成功读回，选中项保持；背景/文件选择菜单、生产命令状态适配器及 `IDropTarget` 查询也通过。

本轮按模式采样均未收到逐项绘制通知，行像素与位图像素都是 `0/0`；`PrintWindow` 对八种模式均返回成功，但各模式没有逐项通知且目标像素仍为 `0/0`。整轮自绘观察到 21 次整体预绘制、4 次逐项消息，但这些异步通知没有落在逐模式采样窗口内，不能映射为稳定行颜色。因而这次结果仍只证明隔离宿主的原生浏览/菜单接口可用，不能判断屏幕内实际绘制的颜色支持。

这与此前屏幕外运行中偶尔在平铺或内容模式收到两条通知的结果一致地显示：离屏通知时序不稳定，`PrintWindow` 也没有补上有效绘制证据。继续重复同一离屏路径预期不会解决关键未知；确认行着色仍需要受授权的屏幕内、无遮挡 `IShellView` 隔离采样。产品视图、路线图和发布包均未改。

## 2026-10-25 隐藏 `IShellView` 宿主导航标志验证

扩展 `--ishellview-host-filter` 隔离夹具，覆盖 `IShellBrowser::BrowseObject` 的 `SBSP_PARENT`、`SBSP_NAVIGATEBACK` 和 `SBSP_NAVIGATEFORWARD` 标志。微软接口文档定义这三种标志分别要求返回父文件夹、沿旅行日志后退和前进，并忽略传入 PIDL；夹具宿主使用 root → child → root 的受控历史序列响应这些调用（[BrowseObject 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)）。Release NativeProbe 构建 0 警告、0 错误，隐藏运行退出码 0：Enter 进入子目录，父目录标志回到根目录，后退恢复子视图，前进恢复根视图；历史头部后退、尾部前进和对文件 PIDL 的失败导航均保留活动视图、当前 PIDL 与历史索引。父窗口不可见，前台句柄未变化，临时夹具已清理。

这验证的是自建测试宿主对 Shell 导航标志的处理及其视图替换路径，不是 Windows ExplorerBrowser 内置 TravelLog 的真实集成，也不代表生产应用已接线；当前父目录解析仅覆盖探针中的 root/child 两级夹具。生产导航、真实历史菜单/快捷键、视图生命周期及颜色能力仍未验证，未改产品源码、路线图或发布包。

## 2026-10-25 隐藏 `IShellView.TranslateAccelerator` Ctrl+A 验证

在同一隔离 root 视图中，通过 `IShellView::TranslateAccelerator` 直接提交 `WM_KEYDOWN/VK_A`，并只在探针线程临时设置 Ctrl 高位，调用后恢复完整 256 键状态。首次未激活/未聚焦的调用返回 `S_FALSE` 且未选择项目；加入 `UIActivate(SVUIA_ACTIVATE_FOCUS)` 仍不足以建立隐藏窗口焦点。再对隐藏 `SysListView32` 调用 `SetFocus` 后，`TranslateAccelerator` 返回 `S_OK` 并选中全部 2 个可见夹具项；随后原生逐项取消选择，读回为空。Release NativeProbe 构建与 `--ishellview-host-filter` 运行通过，父窗口不可见、前台句柄未变化、线程键盘状态已恢复。

此结果证明当前 Windows 默认 `IShellView` 在拥有线程焦点时能处理 Ctrl+A 加速键；它不证明生产 ExplorerBrowser 下的 HwndHost `TranslateAcceleratorCore` 路由、真实键盘输入、焦点切换或文件操作快捷键，也不覆盖剪贴板命令。无界面测试里必须建立正确的视图焦点，单看 `S_FALSE` 不能得出 Shell 不支持 Ctrl+A。生产实现、路线图与发布包均未修改。

同一已聚焦隔离列表继续通过 `TranslateAccelerator` 发送 `WM_KEYDOWN/VK_DOWN`。`IFolderView2::GetFocusedItem` 从第 0 行移动到第 1 行；之后探针清空选区并读回 0 项。随后把已选中的子文件夹 Enter 也改为经 `IShellView.TranslateAccelerator` 发送，而非直接给 ListView 合成消息；Shell 返回 `S_OK` 并调用 `ICommDlgBrowser3.OnDefaultCommand`，继而通过 `BrowseObject` 创建子视图。Release 运行退出码为 0，桌面边界保持不变。源码对照显示这与产品 `ExplorerBrowserHost.TranslateAcceleratorCore` 的关键顺序一致：仅在焦点属于文件视图时调用 Shell 加速器，`S_OK` 即消费该键。此证据不覆盖 ExplorerBrowser 生产宿主、真实物理输入、应用快捷键截获、多窗格焦点、Shift 多选或重命名编辑期间的路由；目前验证限于默认 Shell 视图在探针线程已聚焦列表时处理 Ctrl+A、Down、Enter。

## 2026-10-25 隔离快捷键兜底路径边界复核

为核对产品 `TranslateAcceleratorCore` 在 Shell 返回未处理时的命令兜底，本轮曾尝试清除隐藏列表焦点后向视图 HWND 发送 `WM_COMMAND/FCIDM_SHVIEW_SELECTALL`；命令没有改变选区。进一步检查生产代码发现，调用 Shell 加速器前先要求键盘焦点属于 `_viewWindow` 或其子窗口，因此“无焦点时 Shell 拒绝、随后进入产品命令兜底”不是可达状态。该失败探针不应视为生产兜底缺陷；无焦点实验已从 `--ishellview-host-filter` 移除。聚焦视图中的 Ctrl+A、Down、Enter 隔离测试及完整隐藏原型复跑通过。

颜色能力本轮仍无新正向证据：平铺可映射两项 PIDL，但屏幕外回调的 HDC 虽为有效 `OBJ_DC`，其矩形与裁剪区均为空，目标颜色像素无法采样；其他视图没有稳定逐项着色回调。结果不能证明屏幕内绘制支持或不支持逐项颜色。没有更改生产实现、路线图或发布包。

## 2026-10-25 颜色规则身份映射探针改进（未运行窗口）

隔离探针之前直接用 `NM_CUSTOMDRAW.Item` 行号选择蓝/绿颜色；排序或分组改变行顺序时，颜色会跟随行号而不是文件身份。现改为在每次采样绘制前，经 `IFolderView::Item` 读取当前项目 PIDL 顺序并匹配两个临时夹具文件，再构造行号到目标色的表；`NM_CUSTOMDRAW` 回调只查表，不在绘制回调中重入 Shell COM。合成通知契约覆盖当前顺序和排序/分组后的顺序映射。

Release NativeProbe 编译通过（0 警告、0 错误），`git diff --check` 通过。本次没有运行隐藏、屏幕外或可见窗口探针，因此只验证了源码接线与编译；原生通知时序、真实像素绘制和屏幕内采样结论均未改变。生产 ExplorerBrowser、路线图和发布包未修改。

## 2026-10-25 `IShellView` 颜色接口边界复核

复核探针与生产宿主源码：隔离探针直接通过 `IShellFolder::CreateViewObject(IID_IShellView)` 创建系统默认文件夹视图，以 `IShellBrowser` 宿主承接它，并从实际 `IShellView` 读取 `IFolderView2`、上下文菜单及拖放接口。生产窗格仍由 `IExplorerBrowser` 创建和管理视图，只通过 `GetCurrentView(IID_IShellView)` 访问 Shell 加速键、刷新和菜单；没有生产迁移。

微软文档确认，`NM_CUSTOMDRAW` 是 ListView 向其父窗口发送的通用控件通知，父窗口可在项目绘制阶段设置前景色和背景色；文档没有为 `IShellView` 定义逐项颜色接口，也没有承诺默认 Shell 文件夹视图的所有布局都会以可拦截、稳定的 ListView 绘制通知呈现（[ListView 自绘指南](https://learn.microsoft.com/en-us/windows/win32/controls/using-custom-draw)、[NM_CUSTOMDRAW 文档](https://learn.microsoft.com/en-us/windows/win32/controls/nm-customdraw-list-view)、[IShellView 文件夹视图说明](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)）。

与这条接口边界一致，现有隐藏及屏幕外样本可以观察到默认视图的原生命令/布局接口，但逐项绘制回调会缺失或给出不可采样的行区域；仅构造合成 `NM_CUSTOMDRAW` 请求成功，不等于 Windows Shell 在真实绘制周期中接受颜色。当前判断是：`IShellView` 适合作为保留 Shell 操作能力的原型宿主，但逐项着色尚无可用证据，无法据此决定生产视图迁移。下一步需要一次屏幕内、无遮挡的隔离采样，记录真实 `NM_CUSTOMDRAW` 阶段、项目矩形、回调期 HDC 和目标像素；若仍无有效回调，则停止重复隐藏/屏幕外实验，保留 ExplorerBrowser 并将颜色能力单独列为未决。

本次为源码与官方接口文档复核，没有运行探针或窗口，没有改生产代码、路线图或发布包。

## 2026-10-25 路线图继续时的隐藏与屏幕外复测

按用户选择继续验证隔离默认 `IShellView` 原型。本次先运行隐藏入口 `--ishellview-color-spike`，再运行屏幕外入口 `--ishellview-color-spike-offscreen`；两次命令均退出码 0，`foreground-unchanged=True`，未触碰生产 ExplorerBrowser。

两种模式都确认八种 Shell 查看方式可切换；原生背景菜单提供查看、分组、刷新、新建、属性和排序，项目菜单提供复制、删除、重命名、属性；`IFolderView2`、选中项 `IContextMenu` 和 `IDropTarget` 查询成功。隐藏模式逐项通知只在平铺模式出现两次，矩形为空且裁剪区为 NULL；屏幕外 `WS_VISIBLE` 模式在内容视图收到两条逐项通知，但同样是零矩形/NULL 裁剪区，详细信息及列表模式没有项目通知。逐行目标色像素计数在所有模式均为 0，屏幕外打印窗口也没有目标色像素；合成颜色通知仍只验证处理器本身。

这次有更强的负面证据：仅将窗口标成可见并移至虚拟桌面之外，仍不能形成可供逐项着色的有效绘制区域；但由于窗口不在屏幕内，结果仍不能证明屏幕内真实绘制必定失败。也没有取得可用于产品架构决策的颜色成功证据。原生操作保留结果仅限接口查询与菜单动词检查，不代表真实键鼠菜单、文件操作、焦点或选择行为都通过。

结论：保留 `ExplorerBrowser` 作为产品文件视图；不把 `IShellView` 颜色 spike 迁入生产。当前无依据继续重复隐藏/屏幕外样本。行颜色仍未解决；若后续需要定论，必须另行安排屏幕内无遮挡隔离验收，并先确认通知回调有有效项目几何及目标像素。导入后黑底是另一问题，不由本原型解释。

## 2026-10-25 原生列点击通知补充探针

在同一个隐藏临时 `IShellView` 夹具完成视图矩阵后，探针定位到 `SysHeader32` 的父窗口为 `SysListView32`，列数为 3；向该父窗口发送 `WM_NOTIFY/HDN_ITEMCLICKW`（首列）后，`IFolderView2::GetSortColumns` 从 Name 降序读回为 Name 升序。通知返回值为 0，排序状态确实改变。探针只合成了通知，没有发送鼠标输入；它验证了 Shell DefView 接收该通知路径并更新排序，不证明用户实际点击的坐标命中、绘制反馈或 ExplorerBrowser 产品宿主的事件转发。

同轮重新运行 `--ishellview-color-spike`：默认 `IShellView` 菜单、文件操作接口和八种查看模式仍通过既有检查；颜色探针仍只在平铺模式观察到两条逐项通知，通知矩形和裁剪区域为零，隐藏采样未得到目标色像素。颜色可见性仍未证实，未接入产品。

## 2026-10-25 用户选择后的隔离原型复跑

按用户选择再次运行已存在的隔离入口 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release --no-build -- --ishellview-color-spike`，退出码 0。该入口使用临时夹具和隐藏父窗口，没有创建生产 `ExplorerBrowser`，报告 `product-untouched=True`、`parent-visible=False`、`foreground-unchanged=True`。

本轮八种原生视图模式均能读回预期状态，背景菜单包含查看、分组、刷新、新建、属性和排序命令；文件菜单包含剪切、复制、粘贴、删除、重命名和属性，`IFolderView2`、选择菜单及 `IDropTarget` 查询成功。逐项自绘通知本轮仍只在平铺模式出现两次；通知矩形为 `0,0,0,0`、裁剪区为 `NULLREGION`，八种模式均未读到目标颜色像素。排序/分组后的文件身份映射保持正确，但不改变绘制证据。焦点恢复读回为 false，因此不把本次结果作为焦点行为证据。

结论仍是：隔离 `IShellView` 原型保留了可查询的 Shell 浏览、菜单和拖放接口，但没有证明逐项颜色在屏幕上可绘制，也没有证明生产 `ExplorerBrowser` 能由此替换。遵守当前无 Computer Use 边界，本轮未启动可见窗口；产品宿主迁移和颜色功能均不进入实现。

## 2026-10-25 `IShellFolderViewCB` 公开回调范围核对

为判断是否存在比拦截 ListView `NM_CUSTOMDRAW` 更直接的默认 Shell 视图着色入口，核对 Microsoft Learn 的 [`IShellFolderViewCB`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-ishellfolderviewcb) 与 [`SFV_CREATE`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-sfv_create) 文档，并检查 Windows SDK `10.0.26100.0` 的 `ShlObj_core.h`。文档将该回调描述为系统文件夹视图对象与文件夹提供者之间的通知/视图修改通道；SDK 列出的 `SFVM_*` 消息包括窗口创建、列点击、默认视图模式、详细信息、状态栏及拖放等，没有逐项绘制或颜色消息。

这不排除非公开行为，也不改变既有 `NM_CUSTOMDRAW` 实验，但目前没有依据把 `IShellFolderViewCB` 当作受支持的文件行着色接口。任何逐项着色候选仍须证明真实绘制回调、有效行矩形/裁剪区、像素结果，以及原生选区和操作行为；此检查只收窄了公开接口证据，不是屏幕验收，也没有产品代码变更。

## 2026-10-25 用户选择后的隐藏原型复验

按用户选择继续使用隔离默认 `IShellView` 原型。Release NativeProbe 构建通过（0 警告、0 错误），经隐藏 watchdog 运行退出码 0；父窗口不可见、前台句柄未变、未创建生产 ExplorerBrowser，临时夹具由探针清理。八种 Shell 查看模式均可切换并读回；背景原生菜单仍提供查看、分组、刷新、新建、属性和排序，文件菜单保留剪切、复制、粘贴、删除、重命名和属性；`IFolderView2`、选中项上下文菜单和 `IDropTarget` 查询成功。

本次 `IFolderView::GetItemPosition` 对可映射项目读到非空布局位置，但自绘通知的 `NMCUSTOMDRAW.rc` 仍为 `0,0,0,0`，裁剪区为空；逐项通知只在平铺模式出现两次，八种模式的目标颜色像素均为 0。排序/分组后按 PIDL 身份重建颜色映射仍正确，但未改善绘制回调几何或像素证据。`focus-restored=False` 是隐藏宿主结果，不作为真实焦点行为证据。

结论不变：原型证明默认 `IShellView` 查询到的 Shell 浏览、菜单和拖放接口仍存在；它没有证明逐项着色可见，也不足以支持迁移生产 ExplorerBrowser。遵守当前不操作电脑的要求，本次未运行可见采样；颜色决策继续保持未决，待屏幕内、无遮挡的隔离采样取得有效逐项矩形、绘制像素，并检查原生选择与操作后再定。

## 2026-10-25 路线图继续时复跑

NativeProbe Release 构建通过（0 警告、0 错误），`scripts/native-probe-watchdog.ps1 -Probe color -TimeoutSeconds 40` 退出码 0。父窗口不可见、前台句柄未变、产品 ExplorerBrowser 未创建。八种视图状态均读回预期值，背景和项目核心 Shell 菜单、`IFolderView2`、`IDropTarget` 查询成功；文件身份到颜色规则的排序/分组映射仍正确。

本次只在平铺模式收到两条逐项通知。通知矩形为 `0,0,0,0`、裁剪区为 `NULLREGION`，八种模式的行像素与位图目标色计数均为 `0/0`。这次重复结果没有新增长色可行证据；真实屏幕内绘制、可见焦点和操作后选择保持仍未验证。遵守当前不使用 Computer Use 的边界，未运行可见采样，未改生产视图实现。

同轮继续修复照片打印 Shell 动词探针时，确认并修正了该新探针自己的 COM vtable 读取错误：COM 接口对象首字段才是 vtable 指针。修复只作用于 `PhotoPrintVerbProbe`，与颜色原型无关；颜色探针 Release 编译和隐藏复跑均通过。

## 2026-10-25 选中项颜色保留契约

隔离颜色处理器之前会对每个 `CDDS_ITEMPREPAINT` 项目写入自定义文字与背景色，没有检查 `NMCUSTOMDRAW.uItemState`。因此即使 Shell 已将行选中，原型仍可能覆盖 Explorer 的选中颜色。先扩展合成 `NM_CUSTOMDRAW` 契约：模拟 `CDIS_SELECTED` 项目并预置颜色字段，要求回调返回 `CDRF_DODEFAULT` 且不改写字段。旧实现按预期失败；随后原型改为对选中项走 DefView 默认绘制，仅给未选中项着色。隐藏 NativeProbe Release 运行通过，`synthetic-color-contract=True`，八种视图及复制/重命名/删除与 watcher 刷新检查通过，前台未变、父窗口隐藏。

此回归只验证自有回调分支尊重选中标志，不证明 Shell 实际可见的选中高亮、焦点框、悬停或排序/分组下着色；屏幕像素证据仍需隔离可见验收。未改产品视图或发布包。本次直接运行隐藏探针，进程退出码为 0；未生成 watchdog 日志。

## 2026-10-25 当前源码隐藏复跑

再次执行 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --ishellview-color-spike`，退出码 0。隔离视图保留八种原生查看模式、排序/分组状态读回、背景及项目 Shell 菜单动词和 `IDropTarget`；生产命令适配器识别复制、删除、重命名、属性和新建文件夹可用。该次 Shell 报告粘贴不可用，探针未读写剪贴板，也未调用任何菜单动词。

绘制结果仍不支持颜色结论：整体预绘制 21 次、逐项通知 17 次，但逐模式采样只有“平铺”收到两条逐项通知，回调行矩形和 DC 裁剪区均为空，目标色像素计数为 0。合成通知仍通过颜色处理器契约验证；这只证明处理器能处理模拟结构。父窗口保持隐藏、前台句柄不变、`visible-test=False`、`product-untouched=True`。没有获得真实可见行的着色证据，也没有证伪该路线。

## 2026-10-25 私有 Desktop 颜色采样与行矩形修正

使用独立 Windows Desktop 运行 `color-private-desktop`，探针父窗口在该私有桌面可见，主桌面前台句柄保持不变；Release 构建 0 警告、0 错误，watchdog 退出码 0。回调期间收到 `OBJ_DC`，裁剪盒为 `0,24,640,403`；但 Shell 提供的 `NMCUSTOMDRAW.rc` 仍为空。此前采样器把这个空矩形覆盖到有效行位置，导致 ITEMPOSTPAINT 采样点失效。本轮修正为绘制前用 `LVM_GETITEMRECT` 缓存行位置，并仅在 Shell 的回调矩形非空时覆盖。

修正后可稳定把两个行索引映射回临时文件名；该次仍用行首采样，读到白色，尚未采中标签绘制区域。因私有 Desktop 不是用户当前桌面，且当时没有正确取样标签矩形，不能据此判断 Shell 着色可行性。产品 ExplorerBrowser 未创建，生产代码与发布目录未改。日志：`src/.state/native-probe-watchdog/20261025-181017-ef085057.stdout.log`。

## 2026-10-25 标签区域颜色回调复验

将预绘制后的取样点改为 ListView `LVIR_LABEL` 矩形中心，并将写入 `NMLVCUSTOMDRAW.clrText/clrTextBk` 的回读值一同记录。私有 Desktop 连续复跑两次均成功，最新日志 `src/.state/native-probe-watchdog/20261025-181216-06d7488a.stdout.log`：Content 模式收到两条项目回调；回调 HDC 的标签矩形像素分别读到 `0xF0E0D0` 与 `0xD0F0E0`，与设置的两种颜色一致，回调结构回读到文字色 `0x202020` 和对应背景色。由此得到隔离 Shell `IShellView` 在 Content 模式能够逐项绘制颜色的正向证据。

同一构建下记录每种模式的通知阶段：超大/大/中/小图标、列表、详细信息均为 PREPAINT 0 / ITEMPREPAINT 0；平铺为 6/0；Content 为 6/2。平铺虽然收到整体 PREPAINT，却未收到项目通知。另一次隐藏复验中，平铺模式收到了 2 条项目通知，但其 HDC 裁剪区为空；日志 `src/.state/native-probe-watchdog/20261025-181500-c64b8fc7.stdout.log`。私有 Desktop 复验的完整记录为 `src/.state/native-probe-watchdog/20261025-181517-b8648407.stdout.log`。

每种模式下 `SysListView32` 都报告 `IsWindowVisible=True`，屏幕矩形为 `108,131,748,551`；详细信息模式的样式值比其他模式少 `WS_HSCROLL`，其余窗口样式相同。因此，未收到通知不是由 ListView 隐藏或移出宿主造成；更可能是 DefView 在不同逻辑布局中选择了不同的项目绘制路径，或调用的具体项目重绘机制没有请求自绘通知。这里的“更可能”是根据通知计数和可见窗口状态作出的推断，尚未区分这两种原因。

证据边界：该过程运行在私有 Desktop，并使用临时原生 `IShellView`，不等同于用户桌面的可见验收或生产 `ExplorerBrowser`；窗体整块 BitBlt 像素扫描也仍为 `0/0`。目前只能确认 Content 模式的 ITEMPOSTPAINT 标签区域取样成功，不能宣称所有布局、选择/悬停状态及生产窗格均支持逐项颜色。平铺在可见与隐藏宿主间通知结果不同，提示绘制生命周期/可见性会影响回调。普通 ListView 基线已在下一节完成；后续应评估保留 Shell 操作能力时可用的着色挂接路径。

## 2026-10-25 DefView 选择状态标志与同步视图颜色复验

为验证选中项仍保留原生高亮，探针在原 `CDIS_SELECTED` 分支上首次运行时失败：Private Desktop 回调把两个项目的 `uItemState` 都报告为 `0x201`，但 `IFolderView::ItemCount(SVGIO_SELECTION)` 和 `LVM_GETSELECTEDCOUNT` 均为 0，两个 `LVM_GETITEMSTATE(..., LVIS_SELECTED)` 也都为 0。不能直接把此 DefView 回调位当成真实选中状态。

探针现于绘制前快照 `SysListView32` 的 `LVIS_SELECTED` 索引，仅让真实选中项目走 `CDRF_DODEFAULT`。先清空选区检查未选中像素，再单独选中 Icon 模式第一个项目；五种匹配视图（Icon、Details、SmallIcon、List、Tile）的选中态检查均通过，目标选中行没有规则色像素，未选中行仍命中规则色。未分组时五种模式均命中两种颜色；Name 降序与 Size 分组时五种同步模式也命中两种颜色。自定义 DefView 视图切换和 Content 仍有不同回调行为，本结论不扩展至全部八种布局。

NativeProbe Release 构建 0 警告/错误；Private Desktop watchdog 退出码 0，前台句柄未变。运行同时通过隐藏原生菜单、复制/重命名/删除、刷新重叠及选择快照检查。此证据首次确认默认 `IShellView` 在同步其 `IFolderView2` 与 `SysListView32` 模式后，可在私有 Desktop 中真实画出未选中逐项颜色并保留选中项默认视觉；不证明产品 ExplorerBrowser DirectUI、悬停、焦点框、其它 Shell provider 或用户桌面表现可直接迁移。隐藏模式复跑仍通过，但不可见窗口本身没有像素证据。日志：`src/.state/native-probe-watchdog/20261025-200549-dde38c4f.stdout.log`；选择状态诊断的前序失败日志：`20261025-200430-305b1154.stdout.log`。

## 2026-10-25 颜色选择快照/绘制策略组件化

将原型中的 ListView 实际选中索引快照和颜色规则决策抽到 `ShellViewColorPolicy.cs`。`ShellListSelectionSnapshot` 在绘制前通过 `LVM_GETITEMSTATE/LVIS_SELECTED` 构建不可变索引快照；`ShellViewColorPolicy` 对选中项请求 DefView 默认绘制，对未选中项返回配置色或原型灰色回退。`--shell-view-color-policy-smoke` 不创建窗口、不访问 Shell 或文件，覆盖真实 `LVIS_SELECTED` 位与曾误导我们的 `CDIS_SELECTED` 回调位不同的输入、规则色、选中态和未知索引回退。

Release 构建 0 警告/错误，纯策略 smoke 退出码 0；组件抽取后隐藏 `--ishellview-color-spike` 与 Private Desktop `color-private-desktop` 均再次退出码 0，后者五种同步模式和排序/分组像素断言保持通过。日志：隐藏 `src/.state/native-probe-watchdog/20261025-200822-a76690bb.stdout.log`；Private Desktop `src/.state/native-probe-watchdog/20261025-200837-80c18051.stdout.log`。组件仍属于试验探针，没有接入生产 ExplorerBrowser。

## 2026-10-25 独立视图宿主与 ExplorerBrowser 契约差异

源码对照限定在本仓库实现：颜色/状态探针共享 `QdrNativeViewProbe.cs` 的最小 `StreamViewHost`；产品窗格由 `ExplorerBrowserHost` 持有 ExplorerBrowser 并使用 `ShellBrowserSite` 提供回调。两者的明确差异如下：

| 能力 | 隔离原型 | 当前产品实现 | 迁移缺口 |
|---|---|---|---|
| Shell 浏览器框架 | `GetWindow`、`QueryActiveShellView`、view-state stream、服务查询；状态文本事件、控制窗/消息与加速器现有可选宿主委托；菜单合并/工具栏仍未实现 | ExplorerBrowser 创建/管理 ShellBrowser 与活动 view；`OnViewCreated` 抓取实际 view HWND | 状态文本产品提示已接入；隔离宿主仍需映射真实控制窗、合并菜单、验证宿主消息循环与活动 view 生命周期 |
| 导航和历史 | `StreamViewHost` 可注入共享 `ShellViewNavigationHistory` 并统一路由绝对/相对 PIDL、父级及历史标志；颜色探针回调会按目标绑定文件夹并创建/替换 Shell view，筛选探针仍限临时 fixture | ExplorerBrowser navigation events、取消/过期门控、虚拟位置/搜索路由及每标签 Shell TravelLog | 基类 COM 路由与纯状态历史已有 smoke；实际 Shell 导航的 provider 失败、view/HWND 交接、焦点/选区恢复和并发门控仍须独立验证 |
| 筛选 | 颜色宿主现实现并以 smoke 验证 CDB/CDB2/CDB3 全部回调槽及 `SID_SShellBrowser` 的 QueryService 路由；CDB3 的 `IncludeObject` 覆盖匹配、排除及 provider 元数据失败开放。FilteredHost 仍仅覆盖临时目录导航/历史 | `ShellBrowserSite` 实现 CDB/CDB2/CDB3；产品按标签接入 `IFolderFilterSite`，处理隐藏项偏好和搜索状态 | CDB 托管接口与服务查询合同已有 smoke；仍需确认 DefView 实际查询/回调顺序，并将过滤规则、原生回调线程语义和大目录枚举时序整合进通用宿主，验证全目录/搜索行为 |
| 选择与状态 | 颜色探针手工创建选择；多选恢复使用生产快照类，但没有产品窗口/标签协调器 | `ExplorerBrowserHost` 维护用户选择快照、焦点、命令可用状态、视图模式/排序/分组/列配置及延迟恢复 | 连接真实用户输入代际、标签持久化、焦点遍历、刷新恢复与错误路径 |
| 文件更新 | 当前颜色探针临时接入产品 watcher/batch coordinator 做隔离操作验证 | 每个产品文件系统标签拥有目录 watcher、合并队列、Shell STA Refresh 与 fallback | 从临时 fixture 移植到每个新宿主实例，验证 burst、异常、溢出和跨路径切换 |
| Pane/界面集成 | 独立 parent hwnd，无 WPF PaneControl、标签、四窗格或应用主题 | WPF 每窗格/每标签持有 ExplorerBrowser，负责视图附着/隐藏、原生 Pane、地址/搜索/标签和布局状态 | 完整容器、创建/销毁、重排、隐藏保留、每标签配置、状态栏和系统主题集成 |

`ShellViewColorBrowserHost` 现已把 CDB/CDB2/CDB3 回调接入颜色宿主，并通过真实托管 CCW vtable smoke；`ShellViewHostFilterProbe.FilteredHost` 另证明 `BrowseObject` 与 CDB3 过滤可在窄隔离用例内实现。两者都不是通用导航框架，也未补足产品窗格生命周期。当前证据支持“颜色绘制技术在默认 IShellView 中部分模式可行”，不支持把产品视图直接替换为该原型。颜色继续留在隔离研究阶段，后续若要进入产品方案，必须先针对上述导航/状态/刷新/容器缺口完成独立设计和分阶段等价验证；CDB3 的接口槽本身已有 smoke，DefView 实际调用顺序、全目录/搜索过滤及大目录时序仍未验证。

## 2026-10-25 Shift 范围选择的合成加速器边界

在私有 Desktop 的两项临时文件夹具中，探针先选中第 0 项、在线程键盘状态设置 Shift，再直接调用 `IShellView.TranslateAccelerator(WM_KEYDOWN, VK_DOWN)`。Shell 返回 `S_OK` 并把焦点项从 0 移到 1，但选中项数仍为 1/2；`GetKeyState(VK_SHIFT)` 在调用前确认为 `0xFF80`。另向隐藏 ListView 直接发送 Shift KeyDown 后结果相同。该探针调用没有走完整 Windows 键盘消息队列和真实控件派发，因此不能据此断言 `IShellView` 的 Shift 范围选择失效，也不构成产品快捷键缺陷证据。已移除把此假设当作通过条件的探针断言；Shift 多选仍需真实输入或能复现完整消息循环的隔离验证。

## 2026-10-25 普通 ListView 全模式颜色基线

新增 `--common-listview-color-baseline-private-desktop`，在独立 Windows Desktop 创建普通 `SysListView32`，通过 `LVM_SETVIEW` 覆盖 Icon、Details、SmallIcon、List、Tile、Content 六种常用模式；父窗口仅由隔离探针创建，不使用 Shell 文件夹视图。每种模式均稳定收到 1 次整体 PREPAINT、2 次 ITEMPREPAINT 和 2 次 ITEMPOSTPAINT；回调字段读回预期文字/背景色，标签区域像素也分别为 `0xF0E0D0` 与 `0xD0F0E0`。Release 构建 0 警告、0 错误，私有 Desktop watchdog 退出码 0，主桌面前台不变；日志为 `src/.state/native-probe-watchdog/20261025-182045-357e1bb0.stdout.log`。

这证明当前机器上的 Common Controls ListView 在六种基线视图中都支持该自绘/像素路径，也验证了颜色字段及标签矩形取样方式。它不能证明 Explorer `DefView` 会暴露同样通知，也不涉及 Shell 菜单、文件操作、选择、拖放和 ExplorerBrowser。与上一节 Shell 探针逐模式计数对照，通知差异属于 Shell `DefView` 实际行为，不是通用 ListView 自绘回调缺失。下一步要评估在保留 Shell 操作能力前提下能否可靠挂接颜色绘制；不得把普通 ListView 基线直接当作可替换产品方案。

## 2026-10-25 Shell 模式后同步 ListView 控件模式

微软文档确认，`LVM_SETVIEW` 可设置标准 ListView 的 Icon、Details、SmallIcon、List、Tile 模式，需 Common Controls v6 manifest；`NM_CUSTOMDRAW` 的逐项通知由 PREPAINT 阶段返回 `CDRF_NOTIFYITEMDRAW` 请求。[LVM_SETVIEW](https://learn.microsoft.com/en-us/windows/win32/Controls/lvm-setview)、[ListView NM_CUSTOMDRAW](https://learn.microsoft.com/en-us/windows/win32/controls/nm-customdraw-list-view)

在隔离 Shell 原型中，先通过 `IFolderView2.SetViewModeAndIconSize` 设置 Shell 模式，再向其 `SysListView32` 发送 `LVM_SETVIEW`。原始 `182400` 日志并未断言 Shell 与 ListView 模式相等，实际 Icon 读回为 Shell `2/16`、ListView `0`；因此那一轮不能作为“五种模式全部匹配”的证据。后续改为给每种 Shell 模式指定明确图标尺寸并断言两个控件读回后，未排序/分组的 Icon、Details、SmallIcon、List、Tile 五种模式均匹配，逐项 `NM_CUSTOMDRAW` 及两个目标颜色像素也全部命中。新增排序/分组循环后，五种模式仍保留 Name 降序与 Size 分组以及 Shell 菜单/选区能力，但该状态下目标像素为 0/0，不能宣称已着色。恢复原 Content/32 状态。最新日志：`src/.state/native-probe-watchdog/20261025-183131-6d453c21.stdout.log`；先前日志：`src/.state/native-probe-watchdog/20261025-182400-8c12ab39.stdout.log`。

同一测试也单独验证了只调用 `LVM_SETVIEW`、Shell 仍报告 Content/32 的覆盖路径；它同样能着色，但 Shell 元数据与 ListView 外观不一致，因此不作为产品候选。匹配模式实验仍只是隔离 `IShellView`：没有执行文件操作，真实 `ExplorerBrowser` 的导航、列排序、焦点/键盘加速、拖放、主题切换和重启持久化也未验证。后续已在筛选宿主中补测布局循环后的 Ctrl+A、Down、Enter、上级及前进/后退，详见[原生导航探针](native-navigation-probe.md#2026-10-25-匹配布局与筛选导航复验)。生产接线仍待真实宿主差异评估；当前没有生产源码或 release 变更。

## 2026-10-25 匹配布局后的排序与分组状态复验

扩展 `VerifyDirectListViewModes`：先在临时 Shell 视图设置 Name 降序、Size 升序分组，再逐项切换并同步 Shell 与 `SysListView32` 的 Icon、Details、SmallIcon、List、Tile 模式。每个模式下 `IFolderView2` 均读回相同排序/分组；项目数为 2，背景菜单接口成功，选中/取消选择与选中项 Delete/Rename/Properties 动词查询通过。探针最后恢复排序、分组和原 Content/32 模式。Release 构建 0 警告/错误，私有 Desktop watchdog 退出码 0，前台未变。

本轮匹配模式采样仍有逐项通知，但目标色像素计数是 0/0；因此排序/分组后颜色绘制并未复验成功。排序/分组的状态保留只是在隔离 `IShellView` 中的接口读回，不验证真实用户输入、文件操作、ExplorerBrowser 宿主、焦点/键盘、拖放或重启持久化，也不支持迁移生产视图的决定。日志：`src/.state/native-probe-watchdog/20261025-182634-4f29cb01.stdout.log`。

## 2026-10-25 ExplorerBrowser DirectUI 宿主对照

用隐藏 ExplorerBrowser 并附着产品 `ShellBrowserSite` 运行 opt-in `--view-style-synchronized-hosted`。当前 `IShellView` 子窗口类为 `CtrlNotifySink`、`DirectUIHWND`、`ScrollBar`，没有 `SysListView32`，所以无法在产品宿主上执行原型所依赖的 `LVM_SETVIEW`。探针将该点作为能力缺失报告后继续运行 Shell 原生模式矩阵；八种模式以及排序、分组、列顺序/宽度和多选保持检查通过，退出码 0，前台未变。未同步的原生 ExplorerBrowser 基线 `--view-style-capability` 也通过。

这说明默认 `IShellView` 原型中的 SysListView32 着色/布局同步机制不能直接接入当前 ExplorerBrowser。产品视图架构保持不变；逐项着色仍未解决。后续需要调查 DirectUI 的公开扩展机制，或审视独立 IShellView 宿主必须自行承担的过滤、导航历史、菜单与通知行为，再判断是否有可接受的迁移路径。

## 2026-10-25 路线图继续后的原生操作能力复验

按用户选择继续复验隔离 `IShellView`。Release NativeProbe 构建 0 警告、0 错误；隐藏 watchdog 退出码 0，`parent-visible=False`、`foreground-unchanged=True`、`product-untouched=True`。八种 Shell 视图模式切换成功；背景菜单的查看、分组、刷新、新建、属性、排序均启用，选择菜单的复制、删除、重命名、属性均启用；`IFolderView2`、选择上下文菜单、`IDropTarget` 查询成功。空剪贴板下粘贴状态为不可用，符合当前测试条件。

本次在无 UI 隔离夹具中用 `IFileOperation` 依次复制、重命名、删除临时文件，并按路径 PIDL 与 `IFolderView` 项目数检查视图状态。三次完整 Release 探针均正确完成磁盘操作，视图计数最终按 `2→3→3→2` 收敛；其中两次在三种操作后均自动更新，另一次三种变更都需显式 `IShellView.Refresh` 才恢复。测试文件及夹具随退出清理。这证明隔离默认 `IShellView` 保留了这些 Shell 操作入口，也暴露其隐藏宿主的自动视图通知存在间歇性；样本只有三次，不能据此量化生产 ExplorerBrowser 的故障率。测试不覆盖剪贴板粘贴、操作后选区/焦点、产品宿主或颜色行为。隐藏绘制仍无有效回调像素，本轮没有新的逐项颜色可行性证据；屏幕内逐项着色仍需获准桌面验收。生产 ExplorerBrowser 和 release 未改。

## 2026-10-25 用户选择隔离原型后的复验

Release NativeProbe 构建通过（0 警告、0 错误），隐藏 watchdog 退出码 0。八种 Shell 视图模式、背景及项目菜单、`IFolderView2`、选中菜单和 `IDropTarget` 仍可查询；临时夹具中的原生 `IFileOperation` 复制、重命名、删除均成功，项目数按 `2→3→3→2` 变化。与此前三次样本中两次自动更新不同，本次三种操作后的视图都未自动更新，逐项显式 `IShellView.Refresh` 后才读回正确项目数，进一步确认通知行为有波动。父窗口保持隐藏、前台句柄未变，未创建产品 `ExplorerBrowser`。

颜色部分没有新增可行性证据：隐藏绘制的逐项回调矩形和裁剪区为空，目标颜色像素为 0；排序/分组状态下没有项目绘制回调。本次也没有验证剪贴板粘贴、操作后焦点/选择、可见桌面输入或生产宿主。结论是继续保留隔离原型用于逐项补齐宿主行为，但不迁移生产文件视图；变更通知可靠性、焦点与选择恢复、菜单消息路由、导航历史、拖放及可见颜色仍是迁移前门槛。日志：`src/.state/native-probe-watchdog/20261025-190643-c3f7f7ac.stdout.log`。

## 2026-10-25 隐藏原型的变更通知 A/B

为区分显式 Shell 变更通知与 `IShellView.Refresh`，探针在自然更新等待 5 秒仍未收敛后，分别为临时文件复制、重命名、删除补发标准 `SHCNE_CREATE`、`SHCNE_RENAMEITEM`、`SHCNE_DELETE`（路径格式、无等待刷新），再等候最多 5 秒；只有仍未收敛才调用 `IShellView.Refresh`。Release NativeProbe 构建 0 警告/错误。连续三次隐藏运行的三类自然更新均为 false，标准通知后仍未收敛，显式 Refresh 后均成功恢复 `2→3→3→2`。日志分别为 `20261025-190920-2dbec5a2.stdout.log`、`20261025-191013-3d1acadb.stdout.log` 和 `20261025-191138-a0e99035.stdout.log`。

这说明本探针的隐藏独立宿主不能依赖自然通知或单次标准通知立即恢复；它不是 Shell 通知 API 普遍失效的证据，也不能外推到可见的独立宿主或产品 ExplorerBrowser。`StreamViewHost` 不实现产品 watcher、批处理与刷新协调，所以该结果将宿主变更监视列为独立原型必补能力。没有改产品通知策略、生产文件视图或 release；本轮仍不验证可见逐项颜色、物理输入、剪贴板及焦点/选择恢复。

## 2026-10-25 隔离宿主接入产品目录协调器

NativeProbe 现链接产品 `ShellDirectoryChangeQueue`、`ShellDirectoryChangeBatch` 与 `ShellDirectoryChangeMonitorOptions`，在临时目录启用 `FileSystemWatcher`。watcher 事件仍只更新线程安全队列；Shell 计数读取、`IShellView.Refresh` 和恢复检查都在探针 STA 的消息泵中执行。原型等待 watcher 安静期，比较预期与实际项目数，并以项目 PIDL 路径核对复制、重命名、删除结果。初始化还会等待 DefView 的首轮两项目枚举完成，避免把异步加载中的 0 项误报成夹具损坏。

Release 构建 0 警告、0 错误，隐藏 watchdog 退出码 0。该轮三项操作的自然更新和 watcher 协调均完成，最终计数为 `2→3→3→2`。当时探针在所有操作结束后才读取全局刷新计数，因此日志中的复制/重命名“各两次”不能归属到单项操作；该归因现已撤回。具体日志：`src/.state/native-probe-watchdog/20261025-192311-c101b1fc.stdout.log`。本轮未修改生产代码或 release。

## 2026-10-25 按产品顺序消费刷新请求

事件跟踪显示，复制产生 `Created(+1)` 和两条 `Changed(0)`；重命名产生 `Renamed(0, force=true)`；删除产生 `Deleted(-1)`。前一版隔离 coordinator 在调用 `IShellView.Refresh` 前漏掉产品宿主的 `ShellDirectoryChangeBatch.Complete(actualCount)`。因此重命名的 force 标记没有在启动对应刷新时消费，回调仍认为批次未完成，误触发第二次刷新。产品 `ExplorerBrowserHost` 在刷新前确实调用 `Complete(actualCount)`，所以此前的重复刷新是探针与产品顺序不一致造成的，不是已确认的产品缺陷。

现探针按产品顺序先完成当前批次再调用刷新；刷新期间到达的队列事件仍由 `CompleteRefresh(queue, readActualCount)` 合并。另修正每项刷新计数的采样时机，避免在全部操作结束后读取累计值并错误归因。Release NativeProbe 构建 0 警告、0 错误；隐藏 watchdog 退出码 0。该轮自然更新均未及时完成，watcher/计数协调对复制、重命名、删除各调用一次 Refresh，路径与视图计数均收敛为 `2→3→3→2`。日志：`src/.state/native-probe-watchdog/20261025-192556-8969ec29.stdout.log`。本轮只改隔离探针与记录，没有改产品实现或 release。

## 2026-10-25 隐藏原型刷新重叠事件与选择快照

刷新重叠探针现在在 `IShellView.Refresh` 期间新增文件，并用产品 `ShellDirectoryChangeBatch.CompleteRefresh` 验证 watcher 的 `+1` 增量并入刷新结果。最新运行中，刷新前选中项数为 1；Refresh 期间新增项后，最终读数由 3 收敛到 4，批次记录 `drained-delta=1`，无需额外重试刷新。Shell 重枚举后原选中项未自动保留，生产 `ShellViewSelectionSnapshot` 的身份恢复随后成功（`selection-restored=True`）；隐藏焦点为 false，不能据此判断可见窗口中的键盘焦点。

首次选择恢复尝试失败是探针回调时序问题：它只在短轮询窗口观察到稳定时调用恢复，但产品批次的最终一致性检查可能稍后才确认计数。现将回调放到 `CompleteRefresh` 成功之后，避免漏掉成功收敛。Release NativeProbe 构建 0 警告、0 错误，隐藏 watchdog 退出码 0。日志：`src/.state/native-probe-watchdog/20261025-193300-a5dd5e63.stdout.log`。父窗口保持隐藏，未启动生产 ExplorerBrowser；不构成可见输入、真实焦点、Q-Dir 颜色或产品刷新验收。

## 2026-10-25 用户选择后的隐藏 IShellView 原型复跑

按用户选择复跑隔离默认 `IShellView` 颜色探针。Release NativeProbe 构建通过（0 警告、0 错误），watchdog 退出码 0；输出确认 `parent-visible=False`、`visible-test=False`、`product-untouched=True`、`foreground-unchanged=True`。八种 Shell 查看方式、背景/项目核心菜单、`IFolderView2`、`IDropTarget` 查询通过；临时目录复制、重命名、删除及刷新重叠期间新增文件的 watcher 协调成功，最终项目数为 `2→3→3→2`，选区身份恢复成功。隐藏焦点仍为 false，不作为真实焦点证据。

本次 `NM_CUSTOMDRAW` 共观察到 21 次整体预绘制、17 次项目通知，但只有平铺模式出现 2 次通知；通知行矩形为零、裁剪区域为空，所有查看方式的目标颜色像素计数均为 0。排序/分组状态下没有项目回调。结果仍不能判断屏幕内无遮挡绘制是否能着色；不迁移正式 ExplorerBrowser。日志：`src/.state/native-probe-watchdog/20261025-194945-acc069d4.stdout.log`。本次未打开窗口或使用 Computer Use。

## 2026-10-25 隔离宿主任意深度文件系统导航

按路线图阶段 1，将原先只接受根目录/一个已知子目录的 `FilteredHost.BrowseObject` 扩展为接受导航回调实际能够创建的任意绝对文件夹 PIDL；`SBSP_PARENT` 由当前绝对 PIDL 克隆后移除最后一个 ID 计算，不再硬编码回到夹具根目录。`OnDefaultCommand` 也改用当前目录 PIDL 与选中项相对 PIDL 组合，避免进入第二层后仍错误地从根目录解析。

新增临时 `root → child → grandchild` 用例：先直接导航到孙目录，再执行 Parent、Back、Forward，分别检查 `CurrentPidl`、历史索引、每个 Shell view 项目数及创建/切换。修改前用例按预期失败：合法的孙目录目标收到 `E_NOTIMPL`；泛化导航与 PIDL 上级解析后，NativeProbe Release 构建 0 警告/错误，Private Desktop watchdog 退出码 0，输出 `arbitrary-depth-navigation=True; parent=True; back-forward=True; history-index=5`，前台句柄保持不变，夹具清理完成。随后补入历史分支回归：从孙目录回退后改去兄弟目录，旧前进目标不可再访问；NativeProbe Release 构建 0 警告/错误，Private Desktop watchdog 退出码 0，输出 `history-branch-truncated=True; history-index=5`。最新日志：`src/.state/native-probe-watchdog/20261025-202422-3d61a02b.stdout.log`。

证据仅证明此临时 filesystem `IShellView` host 的多层绝对 PIDL 路由和 view-history 替换，不覆盖虚拟 Shell 位置、Search Folder、失败创建时并发导航、ExplorerBrowser 产品宿主、Tab/四窗格、焦点/物理键鼠或 Q-Dir 可见体验。未修改产品宿主和发布物。

## 2026-10-25 隔离宿主导航边界与失败保留

NativeProbe 将父级 PIDL 计算提取为 `TryGetParentPidl` 并验证 Shell 命名空间边界：系统卷根仍能解析到其 Shell 父级，而桌面根（空绝对 PIDL）没有父项。不要把 filesystem drive root 当作全局导航根；Explorer 可以从卷根继续向“此电脑”等 Shell 父级导航。已有非文件夹目标失败场景同时确认 `BrowseObject` 返回失败后，旧 child view、当前 PIDL、项目数与历史索引仍保持不变。

Release NativeProbe 构建 0 警告/错误；Private Desktop watchdog 退出码 0，输出 `shell-parent-boundary=True; drive-root-parent=available; desktop-root-parent=none`、`nonfolder-target-rejected=True; active-child-view-preserved=True; history-index=1`、`history-branch-truncated=True`。最新日志：`src/.state/native-probe-watchdog/20261025-202556-36cda510.stdout.log`。这证明的是隐藏隔离 filesystem 宿主对 Shell PIDL 上级和失败导航的处理；没有验证 ExplorerBrowser 产品宿主或可见交互。

## 2026-10-25 隔离宿主虚拟 Shell 与 Search Folder view

扩展同一隐藏 `FilteredHost` 探针，使用 Shell 绝对 PIDL 经 `BrowseObject → SHBindToObject → IShellFolder.CreateViewObject → IShellView.CreateViewWindow` 建立虚拟 view。`shell:Desktop`、`shell:MyComputerFolder`、`shell:RecycleBinFolder` 均成功切换并在 3 秒枚举窗口内读到项目；以临时夹具为 scope 的 `search-ms:` Search Folder 也成功创建并枚举。当前机器本轮计数分别为 18、3、15 和 1，只是环境观测值，不作为回归固定数量。既有 `--search-virtual-scopes` 另外验证 Desktop、This PC 与 Recycle Bin search scope 可解析；其枚举子进程每个 URI 只取首项或空结果。

NativeProbe Release 构建 0 警告/错误；Private Desktop watchdog 退出码 0，前台句柄未变化。后续加入 Libraries、Network Places 和临时 ZIP 夹具，隔离 host 成功创建并枚举 Desktop、This PC、Recycle Bin、Libraries、Network Places、ZIP 容器及 Search Folder view；该次项目数为 18、3、15、6、4、1、1，属当前机器观测值。各 view 的背景 `IContextMenu` 获取与内存 `QueryContextMenu` 均返回成功，`IShellView` 上的 `IID_IDropTarget` 查询也返回 `S_OK`。Desktop、This PC、Recycle Bin、Libraries、Network Places 和 Search Folder 的规范菜单动词已读取；ZIP provider 在 `GetCommandString` 上导致一次 watchdog 超时，所以当前只查询 ZIP 的 view、`QueryContextMenu` 和 `IDropTarget`，跳过逐项读取命令名。最新完整日志：`src/.state/native-probe-watchdog/20261025-203408-994bfe23.stdout.log`。超时进程被 watchdog 终止，遗留一个临时 ZIP 文件于 ignored build fixtures 目录；清理命令被工具策略拒绝，文件路径留在当前会话记录中。探针没有显示/调用菜单，也没有调用 `IDropTarget::DragEnter/Drop` 或执行真实 OLE 拖放；这只证明隔离默认 `IShellView` 能绑定、建立 view 并暴露这些接口，未证明产品的历史恢复、每标签状态、菜单命令语义与启用状态、拖放效果、搜索结果更新或虚拟 namespace 的完整互操作。

### 拖放调用边界与重复 provider 超时

尝试从临时 fixture 读取 Shell `IDataObject`，直接向隐藏 child view 的 `IDropTarget` 调用 `DragEnter/Drop`。即使传入空白区屏幕坐标与 `MK_LBUTTON|MK_CONTROL`，`DragEnter` 的 Copy 接受状态在 `Drop` 后仍被清为 `DROPEFFECT_NONE`，fixture 目标没有新文件。因为没有运行完整 `DoDragDrop` OLE 源端/目标端消息循环，这个结果不证明真实拖放失效；相应的一次性直调探针入口已撤下，避免把不完整调用路径留作可依赖的验收项。后续应实现独立 `IDropSource` 并在 Private Desktop 上通过 `DoDragDrop` 验证，或继续仅报告能力接口已查询。

一次再次运行完整虚拟位置序列时，Desktop、This PC、Recycle Bin、Libraries 及菜单动词读取成功，随后在 Network Places 阶段 watchdog 超时（exit 124）；此前同一序列已有一次完整通过。Shell provider 的枚举/菜单回调存在运行时间波动，因此下一轮应按 provider 拆分进程和超时，不能因一次超时将其判为不支持，也不能以另一次通过推断稳定性。watchdog 强制终止的进程未执行 finally，ignored fixtures 下又留下该轮临时 ZIP；清理请求被工具策略拒绝。该次日志：`src/.state/native-probe-watchdog/20261025-204154-c8b39ba8.stdout.log`。

### 2026-10-25 单目标虚拟位置与菜单状态

为避免 provider 间相互拖累，NativeProbe 增加 `host-filter-virtual-private-desktop -VirtualScope <Desktop|ThisPC|RecycleBin|Libraries|NetworkPlaces|SearchFolder|ZipContainer>` 单目标路径。每次只建立一个 temp fixture，并将 ZIP 延迟到 ZipContainer 单测创建。各目标单独 watchdog 运行均退出码 0：Network Places `204357-8f697e77`、ZIP `204405-e70787cf`、Search Folder `204411-ec15b08a`，This PC `204431-7aa3c79d`、Recycle Bin `204435-6f28d84f`、Libraries `204440-b5d08352`、Desktop `204450-e466f199`；补读菜单状态后的 Network Places 和 Search Folder 复跑分别为 `204502-0634251b`、`204508-3aa1bfe9`。每个进程均记录 `foreground-unchanged=True` 并完成 fixture 清理。

新增只读核心菜单状态采样：This PC 的 Refresh、GroupBy、View、Properties、AddNetworkPlace 和 Recycle Bin 的 Refresh、GroupBy、View、Empty 均报告 enabled；Network Places 的 Refresh、GroupBy、View enabled；Search Folder 的 Refresh、GroupBy、View、SaveSearch enabled，Properties 状态 unknown。Libraries/Desktop 的 NewFolder 因菜单位置状态查询返回 unknown，不据此断言命令禁用。命令没有被调用，状态只对应当前 Shell provider 返回的内存菜单。

Search Folder 可建立 view、查询 `SaveSearch` 菜单，但本次和单目标复跑的枚举数均为 0；先前全序列的某次运行见到 1 项。当前夹具在 Windows Search 索引中的结果与异步收敛不稳定，因此不能宣称结果枚举或刷新兼容。ZIP 只读取菜单接口和 QueryContextMenu，仍跳过其可能阻塞的 `GetCommandString`。这些隔离样本降低了多 provider 序列超时的不确定性，但不替代完整拖放验收或产品 GUI。

### 2026-10-25 全路径隔离宿主复跑

Release NativeProbe 构建 0 警告、0 错误；Private Desktop 全路径 watchdog 退出码 0。临时 filesystem fixture 的过滤与分组导出、五种同步视图、合成 Ctrl+A、Shell 默认进入子文件夹、拒绝文件目标时保留当前 view、Parent/Back/Forward、孙目录导航、历史分支截断均通过。Desktop、This PC、Recycle Bin、Libraries、Network Places、Search Folder 和 ZIP provider 都成功建立 view，背景菜单内存构建与 `IID_IDropTarget` 查询成功；父窗口未显示、前台句柄不变、夹具清理完成。日志：`src/.state/native-probe-watchdog/20261025-204901-c2495f2b.stdout.log`。

本次 Search Folder 枚举到 1 项，与此前单目标运行得到 0 项不同，结果依赖当前索引/收敛状态；这里只确认 provider 能创建 view。菜单命令没有执行，ZIP 的 `GetCommandString` 仍跳过。Ctrl+A 是合成加速器输入，不证明物理键盘或用户焦点；查询到 `IDropTarget` 不证明真实 OLE 拖放。

### 2026-10-25 原生背景菜单命令调用

新增独立 `--ishellview-menu-command-private-desktop` 探针，在唯一临时 filesystem view 上重新读取背景 `IContextMenu`，解析并验证 `viewlogicaldetails` 与 `refresh` canonical verbs 后经 `InvokeCommand` 调用。先显式设为 Icon，再调用 Details；`IFolderView2` 读回 Details `mode=4/iconSize=16`。调用 Refresh 后使用 STA 消息泵等待 Shell 异步重枚举，最后读回两个预期项目。Release 构建 0 警告/错误，随后 watchdog 连续三次退出码 0，Private Desktop 前台句柄未变，每轮 fixture 均清理；日志分别为 `20261025-205241-50b8e01f.stdout.log`、`20261025-205324-1f6cb164.stdout.log` 与 `20261025-205327-017699aa.stdout.log`。

首次失败来自测试假设：该环境新建 view 默认已经是 Details；修正夹具显式切换 Icon 后，菜单命令按预期改变模式。Refresh 的即时计数一度为 0，消息泵等待后恢复，不能用同步计数断言其立即完成。本结果只证明隐藏默认 filesystem `IShellView` 对这两个背景命令执行后能读回状态，不涵盖可见菜单、文件/扩展菜单、ExplorerBrowser 产品 view 或用户焦点。

### 2026-10-25 排序与分组背景菜单命令

在同一临时 filesystem view 中，探针先通过 `IFolderView2` 设置 Name 升序与 Type 升序分组，再使用 Shell 背景 `IContextMenu::InvokeCommand` 调用 `sortdescending`、`sortascending`、`groupdescending`、`groupascending`。调用后 `GetSortColumns` / `GetGroupBy` 都读回预期属性 key 和升降序。Release 构建 0 警告/错误；三次 Private Desktop watchdog 中两次退出码 0（`20261025-205506-20909c52.stdout.log`、`20261025-205511-085cac55.stdout.log`），另一次所有菜单状态读回与 fixture 清理完成后，外层前台对照为 false（`20261025-205514-7fa1b1f1.stdout.log`）。

失败只发生在探针方法返回之后的总前台检查；日志无法区分同一时刻的用户前台切换与隔离窗口/COM 清理影响。当前将排序/分组命令的状态读回记为已观测成功，但该扩展运行的隔离边界重复性尚未确认；本轮停止继续创建 Shell view，不将其表述为稳定通过或用户可见验收。

### 2026-10-26 Private Desktop 菜单命令复验

`dotnet build src/ExplorerDirs.NativeProbe -c Release` 成功，0 警告/错误。通过 `scripts/native-probe-watchdog.ps1 -Probe host-menu-commands-private-desktop -TimeoutSeconds 45` 再次运行隔离菜单命令探针，退出码 0：Details 菜单 verb 将视图设为 `mode=4/iconSize=16`，Refresh 后两个临时项目均恢复，Name 排序升/降序及 Type 分组升/降序的命令结果均由 `IFolderView2` 读回确认；宿主不可见于主桌面，外层前台句柄保持不变，view/COM/fixture 清理步骤完成。日志：`src/.state/native-probe-watchdog/20261026-004929-72c83fc3.stdout.log`。

本次补强了隔离默认 filesystem `IShellView` 对这些原生命令的执行与状态读回证据；不证明产品 ExplorerBrowser、可见菜单、物理焦点、Shell 扩展菜单、文件命令或真实 OLE 拖放行为，也不授权迁移产品宿主。

### 2026-10-26 原生命令后的焦点采样稳定性

原菜单命令探针在目标项目数与 PIDL 可查询时就结束等待，但 Shell 排序/分组会在该窗口内继续重建视图。改为记录命令后的即时状态，并要求列表 HWND、目标 PIDL、单项选择和预期项目数连续三次（每次间隔 50 ms）一致后，才判定原生焦点/选区状态或执行探针恢复。

NativeProbe Release build 0 警告/错误。连续 3 次 Private Desktop 运行中，四种排序/分组命令都出现短暂的空视图或焦点/选区不匹配：sortdescending、sortascending、groupascending 首个 0 ms 快照中项目数为 0；groupdescending 的首个快照为 2 项但选区为 0，随后 +50 ms 暂时变为 0 项。它们随后在 +150–200 ms 内达到连续三次稳定的 2 项、目标焦点、单选状态。Refresh 的三轮复测均在 +0 与 +50 ms 看到 0 项；分别于 +100、+100、+150 ms 起恢复到 2 项、目标焦点和单选状态。稳定采样版本的三次完整探针运行均退出码 0，且没有调用探针的 `SelectItem` 兜底恢复。它解释了旧探针为何会在视图重建期间过早采样并可能干扰状态，不能据此证明产品 ExplorerBrowser 的焦点行为或用户可见体验。菜单/命令状态与前台/隐藏窗口检查均通过。

真实 OLE 拖放仍需在鼠标指针位于临时目标 view 上时运行 `DoDragDrop`；OLE 会查找当前指针下的窗口，并持续跟踪光标与按键状态（[Microsoft DoDragDrop 文档](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-dodragdrop)）。当前 Private Desktop 是非活动桌面，不能安全地向用户活动桌面注入鼠标状态；因此本轮不启动完整拖动循环。已有的 `IDropTarget` QI 仍只证明接口可用，之前的手动 `DragEnter/Drop` 直调也不代表真实拖放效果。后续真实 OLE 验收需要隔离但可接收输入的活动桌面/测试会话，完成后仍只操作临时 fixture。

### 2026-10-26 抽取隔离宿主的导航历史状态

源码审查发现 `FilteredHost.BrowseObject(SBSP_PARENT)` 会先释放临时父 PIDL，随后又用该指针比较并复制当前历史项，构成释放后读取。父级路径改为在导航前创建自有副本，并只在 Shell 导航成功后提交；失败路径释放暂存 PIDL而不改历史。`ShellViewNavigationHistory` 现作为隔离宿主间可复用组件，覆盖父级、后退/前进、历史分支和导航失败。释放哨兵 smoke 在故意恢复旧释放顺序时失败，修复后通过；NativeProbe Release build 与完整 SettingsSmoke 通过。此处记录的是历史状态抽取完成时的进度；后续颜色原型也接入该组件并有无窗口路由 smoke，但实际 Shell 导航仍未运行，不能据此宣称颜色宿主达到完整框架导航能力。产品文件视图未修改。

### 2026-10-26 颜色原型接入导航历史

`ShellViewColorBrowserHost.BrowseObject` 现在委托给共享 `ShellViewNavigationHistory`。颜色原型的回调按目标绝对 PIDL 绑定 Shell 文件夹、创建新的 `IShellView`，并在创建成功后替换旧 view/window 与 folder；导航失败会释放暂存对象并保留旧 view。宿主释放时也释放其持有的历史 PIDL。

无 Shell 窗口 smoke 通过真实托管宿主入口检查普通 BrowseObject、父级及后退路由与历史索引/PIDL 更新。`--shell-view-color-policy-smoke` 通过。该 smoke 使用模拟 PIDL 和导航回调，不创建 Shell view；NativeProbe Release build 和完整 SettingsSmoke 是本轮后续验证项。没有运行 `IShellView`、Private Desktop 或可见 GUI，因此新旧 Shell HWND 交接、真实 provider 导航失败以及焦点/选择恢复仍未知；产品 ExplorerBrowser 没有改动。

### 2026-10-26 隔离导航的新浏览器标志

Windows SDK 定义 `SBSP_NEWBROWSER` 为在新浏览器中导航，`SBSP_SAMEBROWSER` 为当前浏览器；源码复核发现单视图隔离宿主会忽略前者并继续在当前 view 导航。现让共享 `ShellViewNavigationHistory` 在复制 PIDL 或调用 view 切换回调前，对 `SBSP_NEWBROWSER` 明确返回 `E_NOTIMPL`，避免把用户请求改成不同操作；`SBSP_SAMEBROWSER` 则保留并正常转发。新浏览器请求保持当前位置与历史不变的 smoke 先按预期失败，加入拒绝策略后通过；同浏览器标志转发也通过。该策略只适用于不管理第二个浏览器窗口的隔离宿主。定向 `--shell-view-color-policy-smoke`、完整 Release SettingsSmoke、App Release build（0 警告/错误）及相关差异空白检查通过；未创建 Shell view 或 GUI，产品 ExplorerBrowser 未改。

### 2026-10-26 BrowseObject 明确不支持的浏览标志

Microsoft 的 `IShellBrowser::BrowseObject` 合同将 `SBSP_HELPMODE` 与 Vista 起的 `SBSP_KEEPSAMETEMPLATE` 明确列为“不支持，不要使用”；之前单视图原型会忽略这两个标志并替换当前视图。现在 `ShellViewNavigationHistory` 和已拒绝的 `SBSP_NEWBROWSER` 一样，在处理 PIDL 前返回 `E_NOTIMPL`。回归验证三种请求均不调用导航回调、不改变当前绝对 PIDL/index/count；`SBSP_SAMEBROWSER` 正向路由保持正常。三个 unsupported-flag 用例先按预期失败，接入拒绝掩码后，定向 `--shell-view-color-policy-smoke`、完整 Release SettingsSmoke、App Release build（0 警告/错误）通过。无 Shell view、ExplorerBrowser 或 GUI；产品宿主未改。来源：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)。

### 2026-10-26 SBSP_EXPLOREMODE 与单视图宿主

Microsoft 的 `BrowseObject` 文档说明 `SBSP_EXPLOREMODE` 表示新浏览窗口应显示目录树；当前浏览器若不匹配则会创建新窗口，而 `SBSP_SAMEBROWSER` 会忽略 OPEN/EXPLORE 模式位。独立宿主没有目录树或新窗口 host，因此现对 ExploreMode 且未强制 SameBrowser 的请求返回 `E_NOTIMPL`，不把它降级成无树的当前视图导航；SameBrowser + ExploreMode 则清除模式位后继续当前 view。无树 host 对应的 OpenMode 继续转发。新增拒绝/忽略回归先按预期失败，修复后定向 smoke 通过；完整 SettingsSmoke 和 App Release build（0 警告/错误）通过。该门控只作用隔离宿主，无 Shell view/GUI；产品 ExplorerBrowser 未改。来源：[Microsoft IShellBrowser::BrowseObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-browseobject)。

### 2026-10-26 私有桌面逐项颜色屏幕内采样

`dotnet build src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release` 通过（0 警告、0 错误）；`scripts/native-probe-watchdog.ps1 -Probe color-private-desktop -TimeoutSeconds 45` 退出码 0，运行约 21 秒。探针在独立 Windows Desktop 创建可见的临时 `IShellView`；主桌面前台句柄未变，日志记录 `product-untouched=True`，临时宿主与夹具由子进程退出清理。

本轮屏幕内像素检查中，八种 Shell 查看方式的项目行目标颜色像素均未命中（`0/0`）；Icon、Details、SmallIcon、List 没有收到可用的 `CDDS_ITEMPREPAINT`，Tile/Content 收到项目绘制通知，但也没有项目级回调，因而没有逐项着色。对照用的普通 ListView 在同一探针会话中各模式均产生项目回调并命中像素，证明采样链路可工作；不能把该对照结果外推为默认 Shell `IShellView` 支持着色。背景/选中项 Shell 菜单、视图模式读回和临时文件复制/重命名/删除刷新也在本轮通过，但均属于隔离默认 filesystem view，不是产品 ExplorerBrowser 或可见用户操作验收。

这次屏幕内负结果结束了对相同 DefView `NM_CUSTOMDRAW` 项目回调路径的重复采样；逐项颜色仍是未决能力，不据此迁移产品宿主。后续优先推进可复用的原生 Shell 菜单和文件操作行为；若要继续颜色，应先提出新的、不同于该 DefView 回调路径的技术假设并单独验证。

### 2026-10-27 真实 Shell view-state stream 按会话键写回

`ShellViewColorBrowserHost` 现在可把 `ShellViewStateStore` 与稳定 session GUID 传给通用 `StreamViewHost`。策略 smoke 新增真实派生宿主的 store/read/write/dispose/recreate 往返，先因构造器不暴露 store 而失败，接线后通过。

`color-private-desktop` 在临时 fixture 中创建真实默认 filesystem `IShellView`。`DestroyViewWindow` 期间宿主收到 4 次 stream 写回；`Dispose` 后同 GUID 状态文件存在，大小 772 字节。随后新建隔离宿主使用相同 GUID 调用 `GetViewStateStream(READ)`，逐字节匹配实际 Shell 写出的流。该次 NativeProbe Release build 0 警告/错误，Private Desktop watchdog 退出码 0；主桌面 foreground HWND 从 `0x81852` 保持不变。复验日志：`src/.state/native-probe-watchdog/20261027-000036-81c04f29.stdout.log`。

证据仅覆盖 Shell 销毁回调到临时 session store、再由新 host 提供相同字节。单独切换模式、停用/销毁 view 或同路径重导航均未触发写回；写入只在完整探针后段出现，目前尚未隔离出最小触发条件。让第二个真实 `IShellView` 消费恢复流并读回模式/列的尝试因此没有通过，失败记录：`src/.state/native-probe-watchdog/20261026-235844-733e4d24.stdout.log`。没有跨应用进程重启或连接产品标签会话 ID。状态目录和 fixture 都由探针创建并清理，产品 INI、ExplorerBrowser 与 release 未修改。

### 2026-10-27 真实 IShellView 激活菜单合并回调

NativeProbe 新增 `--ishellview-menu-activation-private-desktop`，在唯一临时 filesystem fixture 上创建实际默认 `IShellView`，随后调用 `UIActivate(SVUIA_ACTIVATE_FOCUS)`。`StreamViewHost` 记录 `IShellBrowser::InsertMenusSB`、`SetMenuSB` 与 `RemoveMenusSB` 实际入口次数；探针并检查顶层宿主 `GetMenu` 是否返回共享菜单句柄。Release 构建 0 警告、0 错误，定向 `--shell-view-color-policy-smoke` 通过。Private Desktop watchdog 连续三轮退出码 0，每轮稳定读到 `insert=7 / set=13 / remove=6`、`shared-menu-present=True`、`active-view=True`；主桌面 foreground HWND 始终为 `0x81852`。原 `color-private-desktop` 另行复跑退出码 0，状态流仍写入 772 字节并由重建 host 读回。

首次探针运行在菜单结果后被颜色主探针的状态流断言判为失败，因为专用菜单模式没有执行颜色主探针后续刷新/绘制阶段。现把这条模式与状态流验收条件分开；状态流门槛由独立的颜色探针复验，不降低原门槛。

这证明真实默认 Shell 视图激活会进入当前隔离 `IShellBrowser` 的菜单生命周期并安装共享菜单。它没有显示/点击菜单，也没有验证 Shell 菜单项与应用自定义命令合并后的排序、绘制、快捷键、启用状态或命令分派；未覆盖 ExplorerBrowser 产品宿主、Pane/WPF 容器或标签切换。实际可见菜单验收仍需之后获准的隔离输入会话，产品宿主、INI 和 release 未修改。日志：`src/.state/native-probe-watchdog/20261027-000439-ec670e37.stdout.log`、`20261027-000445-8a085849.stdout.log`、`20261027-000446-98774ea8.stdout.log`；独立状态流复验：`20261027-000447-38b83a8d.stdout.log`。

### 2026-10-27 真实 IShellView 四窗格模式隔离、状态往返与文件操作

`--ishellview-session-restore-private-desktop` 生产者在临时文件夹 view 中设置 Details、Name 降序、Type 升序分组及 Name/Size/Type 三列顺序与宽度，调用 `IShellView::SaveViewState` 并将 session GUID 对应的流落盘。之后启动一个全新的 NativeProbe 消费进程，传入 fixture 路径、状态目录和相同 GUID；消费进程确认状态文件在启动前已存在，Shell 对新 host 请求 4 次读取 748 字节流，并从新建真实 view 读回 mode `4`、Name 降序、Type 升序分组及三列的相同顺序和宽度。消费者创建 2×2 原生子窗格布局，四个 pane HWND 都是同一 top-level 的不同子窗口，四个真实 `IShellView` window 分别是所属 pane 的子窗口。第一 pane 为 Details/mode `4`；第二 pane 用独立 session GUID 切换到 List/mode `3`，单独 `SaveViewState` 并写入 772 字节，按现有 `ShellViewReferenceRetirement` 路径销毁后以同 GUID 重建，Shell 对新 host 请求 4 次读流并恢复 List，第一 pane 仍是 Details。第三、第四 pane 分别切到 Icon/mode `1` 与 Tile/mode `6`；四个 view 同时存活时读回 `4,3,1,6`。第一 pane 对四个 view 共享的目录执行 Shell 复制、重命名、删除后，四个 view 的项目数都逐项收敛到预期值，四种模式以及第一 pane 的排序、分组和列布局仍保持。

NativeProbe Release build 0 警告、0 错误，定向 `--shell-view-color-policy-smoke` 通过；四窗格版本 Private Desktop watchdog 连续三轮退出码 0，每轮生产者和消费者 PID 不同，四个 view 读回 Details/List/Icon/Tile (`4/3/1/6`)，第二 session 重建 read=4/write=1；Shell 复制、重命名、删除后四个 pane 项目数都收敛，主桌面 foreground HWND 均保持 `0x81852`。日志：`20261027-003105-fc14fff6.stdout.log`、`20261027-003119-043d18b7.stdout.log`、`20261027-003129-2cc68611.stdout.log`。

早期失败并非 Shell 忽略会话流：旧刺激调用 `IFolderView::SetCurrentViewMode` 虽返回成功，但保存前模式仍是 `1`；改用已验证的 `IFolderView2::SetViewModeAndIconSize` 后，保存前和新进程新 view 都正确读回。消费者在四 pane 状态与文件操作检查后通过自身进程退出回收隔离 Shell STA，生产者再删除临时 fixture 和状态目录；同步释放该 STA 内的 Shell view 曾使消费者卡在清理阶段，所以不把未完成的同步清理当作恢复失败。该证据证明四个真实 Shell view 可在同一原生容器中以不同模式并存，第二 pane 能按 session GUID 独立保存/销毁/恢复，同目录 Shell 文件通知会使四个 view 的项目数都收敛。四个 pane 浏览同一路径，因此没有验证不同目录之间的通知隔离；它也不是 WPF `PaneControl` 或 ExplorerDirs.App 正式会话接线，不证明可见拖放、物理焦点/键盘或菜单体验。产品 ExplorerBrowser、INI 与 release 均未接入或修改。

### 2026-10-27 四窗格菜单激活回调路由

四窗格 session-restore consumer 在四个模式和各自 host 已建立后，依序对每个真实 `IShellView` 调用 `UIActivate(SVUIA_ACTIVATE_FOCUS)`。每轮在目标 pane 的 `ShellViewColorBrowserHost` 上分别观察到 `InsertMenusSB +1`、`SetMenuSB +2`；Shell 离开该 pane 时 `RemoveMenusSB +1`。Private Desktop watchdog 连续三轮通过，三轮都维持主桌面 foreground HWND `0x81852`。日志：`20261027-003403-1ceb1c1b.stdout.log`、`20261027-003416-58e4ebf2.stdout.log`、`20261027-003426-f3a24d83.stdout.log`。

该结果只验证 `UIActivate` 的 Shell 回调进入对应原生 pane host；没有打开或点击菜单，不能证明菜单视觉合并、WPF 命令分派、键盘焦点或真实输入体验。四个 view 仍位于同一隔离进程且浏览相同 fixture，未验证生产宿主、不同路径通知、拖放，也未修改产品 ExplorerBrowser、INI 或 release。

### 2026-10-27 四窗格跨目录 Shell 通知隔离

四窗格 session-restore consumer 将前三个 `IShellView` 留在主 fixture，第四个 view 放到兄弟隔离 fixture；两组都保持自己的视图模式和 session host。主目录的 Shell 复制、重命名、删除只要求前三个 pane 收敛；随后在另一目录执行 Shell 复制，第四个 pane 收到新项目且计数 `1→2`，前三个 pane 保持原计数并且没有出现该跨目录文件。Private Desktop watchdog 连续三轮退出码 0，主桌面 foreground HWND 每轮未变：`20261027-003657-e9c78ab7.stdout.log`、`20261027-003711-0d8fef2c.stdout.log`、`20261027-003721-a191bc98.stdout.log`。夹具位于 NativeProbe `fixtures` 根目录并由外层探针按根路径边界清理。

本结果证明同一隔离原生容器里的 Shell 文件通知按不同目录更新各自 view，未依赖 F5 刷新。它仍是 `IFileOperation` 触发的合成操作，不覆盖真实外部进程 watcher、产品 PaneControl/WPF、可见菜单/物理输入、拖放或 ExplorerBrowser 产品接线；产品配置与 release 未改。

### 2026-10-27 隔离 Shell 拖放探针未通过产品行为门槛

从真实源 view 选中 child PIDL 后取得 `IDataObject`，并从目标文件夹的 `IShellFolder::CreateViewObject(IID_IDropTarget)` 取得 Shell drop target。手动调用 `DragEnter`、`DragOver`、`Drop` 虽均返回成功且保留 COPY effect，目标夹具始终未生成文件；这条直接回调路径不构成真实拖放验收。进一步改为 OLE `DoDragDrop` 后，Private Desktop 中 `GetCursorPos` 返回失败，无法在不移动/注入用户桌面输入的情况下把隔离目标放到当前光标下，因而没有开始 OLE 拖放循环。相关失败日志：`20261027-003901-65e574d6.stdout.log`、`20261027-003958-08c0f48b.stdout.log`、`20261027-004029-d5c2fb1a.stdout.log`、`20261027-004054-b9fd9a72.stdout.log`、`20261027-004126-daa97a66.stdout.log`、`20261027-004146-fd004e7e.stdout.log`、`20261027-004213-3866e1a3.stdout.log`、`20261027-004239-60802bf2.stdout.log`、`20261027-004305-030e6607.stdout.log`、`20261027-004459-b33c6755.stdout.log`。

失败的临时拖放代码已撤回；四窗格跨目录通知隔离的已提交探针不受影响。本记录不判定 Windows Shell 拖放本身失败，也不代表产品拖放已验证。后续需要让真实 OLE 拖放循环在可定位指针的独立测试桌面内运行，或在用户明确授权的隔离 GUI 会话中进行；不调用 `SetCursorPos` 修改当前用户指针。官方合同说明：文件夹级 drop target 通过 `IShellFolder::CreateViewObject` 请求，而条目级 `GetUIObjectOf(IID_IDropTarget)` 的 `cidl` 需为 1。
### 2026-10-27 双 pane IShellBrowser 导航历史隔离 smoke

`ShellViewColorBrowserHostSmoke` 现通过两个独立 host 暴露出的 `IShellBrowser::BrowseObject` COM vtable 交错执行导航：各自进入不同 PIDL、第一 pane 后退、第二 pane 建立新历史分支，以及第一 pane 的失败目标。每一步都同时断言两个 history 的当前位置和索引；跨 pane 操作没有改变另一方历史，失败导航保留双方状态。NativeProbe Release 构建 0 警告/错误，`--shell-view-color-policy-smoke` 全部通过。

此项使用 `ShellViewNavigationHistorySmoke.PidlPool` 的合成 PIDL 与成功/失败回调，不创建真实 Shell view，也不证明后退/前进按钮状态、travel log 或可见 Pane 导航。实际 `IShellView` 宿主历史接线仍需窗口级验证；产品 ExplorerBrowser 代码、配置和 release 未修改。

### 2026-10-27 四窗格标准 Shell 查看命令定向到目标 pane

四窗格 consumer 为每个 Shell view 取得原生背景 `IContextMenu`，按 canonical verb `viewlogicaldetails` 调用 `IContextMenu::InvokeCommand`，验证 pane 2、3、4 各自从 List/Icon/Tile 切到 Details、其它 pane 仍保持既定模式，再恢复原模式。连续三轮 Private Desktop watchdog 退出码 0；四窗格状态、复制/重命名/删除、跨目录通知仍通过，foreground HWND 未变。日志：`20261027-004952-5e89970c.stdout.log`、`20261027-005008-7067c712.stdout.log`、`20261027-005020-58682019.stdout.log`。NativeProbe Release build 0 警告/错误，策略 smoke 与 diff check 通过。

首 pane 也参加往返时，诊断采样确认：切到 List 后三列宽仍为 `280/120/160`；调用 Shell 的 Details 菜单 verb 后，Shell 按当前约 380 px 的 view 宽度把列宽调整为 `158/110/110`。排序、分组、模式和列身份/顺序均未改变。原失败是后续文件操作断言错误地要求宽度仍与初次恢复完全相同，不是复制改变了排序/分组或列结构。

探针现在对四个 pane 都运行 List→Shell Details 菜单命令→恢复原模式；文件操作阶段继续检查 Details、排序、分组和列身份/顺序。精确列宽仍在独立进程首次恢复状态时严格检查。Release build 0 警告/错误；2026-10-27 00:53:05 Private Desktop watchdog 退出码 0，四 pane 命令路由、复制/重命名/删除和跨目录通知全部通过，主桌面 foreground HWND `0x81852` 未变。日志：`src/.state/native-probe-watchdog/20261027-005305-a64c57ff.stdout.log`。

测试仍不显示菜单，也不证明真实菜单外观、物理键盘或 WPF 活动 pane 集成；产品 ExplorerBrowser、配置与 release 未改。

### 2026-10-27 四窗格 Shell 排序与分组菜单命令隔离

在同一四窗格 consumer 中，先为每个真实 view 建立 Name 升序、Type 升序分组的可读回基线。然后逐个 pane 通过其自身背景 `IContextMenu` 调用 `viewlogicaldetails`、`sortdescending`、`sortascending`、`groupdescending`、`groupascending`；每次调用后读取四个 `IFolderView2` 状态，验证只有目标 pane 的排序/分组方向改变，其他三个 pane 保持升序。恢复四种查看模式和 pane 1 原有 Name 降序状态后，再执行已有 Shell 复制/重命名/删除及跨目录通知检查。

NativeProbe Release 构建 0 警告、0 错误；加入 Details 菜单后排序/分组状态不变断言后，Private Desktop watchdog 连续三轮退出码 0；三轮均通过四个 pane 的菜单命令路由、文件操作和通知检查，主桌面 foreground HWND 保持 `0x81852`。日志：`20261027-005805-3fc08177.stdout.log`、`20261027-005826-4d2e0fc6.stdout.log`、`20261027-005843-098f9488.stdout.log`。

该证据证明隔离默认 filesystem `IShellView` 的标准背景菜单命令可按各 pane 的原生 `IContextMenu` 定向，并读回独立排序/分组状态；菜单未实际显示或点击，因此不证明 UI 合并、列标题右键、物理输入、扩展菜单或 WPF 活动 pane 接线。产品 ExplorerBrowser、配置与 release 未修改。

### 2026-10-27 四窗格文件项菜单目标隔离

在四窗格 session-restore consumer 中逐个 `IFolderView2::SelectItem` 选中一项，并经对应 `IShellView::GetItemObject(SVGIO_SELECTION, IID_IContextMenu)` 查询文件项菜单。每一步都读取四个 view 的 `SVGIO_SELECTION` 数量，断言只有目标窗格有一项选中；原生菜单必须枚举出 canonical `copy` 动词。随后清空四个 view 的选区。探针不调用菜单命令、不访问系统剪贴板，也不显示菜单。

Release NativeProbe 构建 0 警告/错误；Private Desktop watchdog 连续三轮退出码 0，四个 pane 都读到自己的 `copy` 菜单且兄弟 pane 选区为零，四窗格文件操作/通知及前台 HWND `0x81852` 检查通过。日志：`20261027-010130-21d014fe.stdout.log`、`20261027-010152-b16fbd87.stdout.log`、`20261027-010209-b0ee888b.stdout.log`。

这验证了隔离原生 view 能从各自选择项提供 Shell 文件菜单对象并保持选区隔离；未验证可见右键菜单、命令点击、第三方扩展菜单、产品 ExplorerBrowser 或 WPF 目标命中。

### 2026-10-27 四窗格 IShellView 方向键焦点路由

四窗格 consumer 分别在 Details、List、Icon 模式中，将焦点设到第一项并通过对应 `IShellView::TranslateAccelerator` 派发导航键：Details/List 使用 Down，Icon 网格使用 Right。三种模式的目标 pane 焦点均从索引 0 移至 1；每次操作前后读取四个 `IFolderView2::GetFocusedItem`，兄弟 pane 的焦点索引保持不变。第四窗格只有一个项目，未纳入移动断言。结束前清空测试选择。

NativeProbe Release 构建 0 警告/错误；完整四窗格 Private Desktop watchdog 连续三轮退出码 0，前台 HWND 保持 `0x81852`。日志：`20261027-010520-482e0ca0.stdout.log`、`20261027-010542-f36a9d5b.stdout.log`、`20261027-010559-e5ef25fc.stdout.log`。

这证明默认 filesystem `IShellView` 在三个标准模式下能经各自 accelerator 入口移动 pane 内 Shell 焦点，并保持兄弟 view 状态。消息由隔离探针合成并直接交给 Shell，不是物理键盘/完整应用消息循环；不证明 WPF `HwndHost` 焦点、Tab/Shift+Tab、重命名编辑器、剪贴板操作或用户可见行为。产品 ExplorerBrowser、配置与 release 未修改。

### 2026-10-27 四窗格 Ctrl+A 选择路由

在同一四窗格 consumer 中逐 pane 激活各自 `IShellView` 与列表焦点，以隔离探针线程的键盘状态设置 Ctrl 按下，然后经对应 `IShellView::TranslateAccelerator` 派发 Ctrl+A。pane 1–3 分别读回自身全部两项被选中，另外三个 pane 的选中项数量保持为零；每步清除选择，并在 finally 路径恢复原线程键盘状态。没有读取或写入系统剪贴板。

NativeProbe Release 构建 0 警告/错误；完整 Private Desktop 四窗格 watchdog 连续三轮退出码 0，记录 `ctrl-a=pane1:2,pane2:2,pane3:2`，前台 HWND 保持 `0x81852`。日志：`20261027-010720-60883814.stdout.log`、`20261027-010744-e5a5e1ee.stdout.log`、`20261027-010803-f770a4fc.stdout.log`。

该证据是直接合成 Ctrl+A 消息与线程 modifier 状态的 Shell accelerator 路径，不是物理键盘或完整产品消息循环验收；第四 pane 只有一个 fixture 项且未纳入全选断言。产品 ExplorerBrowser、WPF、剪贴板与 release 未修改。

### 2026-10-27 背景菜单“新建文件夹” canonical verb 原型通过

`--ishellview-menu-command-private-desktop` 已能从背景 `IContextMenu` 读到 `newfolder` canonical verb。单命令探针在 Private Desktop 中显示并以 `UIActivate(SVUIA_ACTIVATE_FOCUS)` 激活默认 `IShellView`，再将原生列表设为焦点后比较调用形式：不同的 `QueryContextMenu` 样本中，`GetCommandString` 找到的命令偏移先后为 `113` 和 `115`；以各自 `MAKEINTRESOURCE(offset)` 调用 `IContextMenu::InvokeCommand` 均返回 `E_FAIL (0x80004005)`。以无 `CMIC_MASK_FLAG_NO_UI` 的 ANSI canonical verb `newfolder` 调用返回 `S_OK`，实际创建“新建文件夹”，Shell 项目数由 2 增至 3。创建后选中数仍为 0，focused index 仍指向原 `child` 项，没有 `Edit` 重命名窗口，焦点仍在 `SysListView32`。没有调用 `GetControlWindow` 或状态文本回调；Private Desktop 外前台 HWND 未变，夹具随后清理。NativeProbe Release build 0 警告、0 错误，定向 watchdog 退出码 0。

后续扩展同一 Private Desktop 探针：创建后明确通过 `IFolderView::SelectItem` 选中新目录，设置列表焦点，并将合成 F2 交给 `IShellView::TranslateAccelerator`。F2 返回 `S_OK`，原生内联 `Edit` 出现且取得焦点；随后将合成 Left/Right 交回 `IShellView::TranslateAccelerator`，两者均返回 `S_OK`，编辑窗口保持存在并持续持有焦点。这提供了一个可行的原生路径，可供后续解决多窗格重命名时方向键被邻窗格截获的问题。它只验证了编辑中的左右方向键，尚未覆盖上下键、真实物理键、完整 WPF/HwndHost 消息循环或产品多窗格路由，因此不能据此宣称用户报告的产品缺陷已修复。

该结果证明默认文件系统 `IShellView` 可通过 canonical verb 完成新建文件夹并由视图枚举新项；程序化创建自身没有自动选中/重命名，但显式选中后原生 F2 和编辑器左右键路由有效。偏移在重新构造菜单时发生变化，因此不能跨菜单缓存；即使逐次重新查询后，offset 调用仍失败，具体原因未明。仍未验证真实弹出菜单点击、ExplorerBrowser 产品或完整四窗格，也不把一次具体命令推广为所有 Shell 命令都应使用字符串。隔离探针新增 `--ishellview-newfolder-private-desktop` 与 watchdog 入口，并要求列表预先持有焦点、创建结果同时出现在磁盘和 Shell 列表才算通过；产品 ExplorerBrowser、用户目录和 release 均未改动。

### 2026-10-27 激活后的 Shell 原生菜单组内容读取

扩展 `--ishellview-menu-activation-private-desktop`：在真实默认 filesystem `IShellView` 完成 `UIActivate(SVUIA_ACTIVATE_FOCUS)` 后读取宿主共享 `HMENU` 的根级菜单组及其子项。Private Desktop watchdog 退出码 0，NativeProbe Release build 0 警告/错误，主桌面 foreground HWND 保持 `0x81852`。探针确认 File/Edit/View/Help 四个子菜单已包含 Shell 项目；当前中文 Windows 样本分别读到 4/10/18/1 项，Edit 含剪切、复制、粘贴、全选及快捷键，View 含八种查看方式及排列/排序/分组/列选择命令。探针以菜单组非空作为通过条件，不依赖本地化标题与具体子项数量。

这补足了此前只有 `InsertMenusSB`/`SetMenuSB` 回调次数及共享句柄的证据：激活后的共享菜单对象中确实有 Shell 填充的标准命令。仍未追踪显示菜单、真实点击、应用自定义菜单插入位置、命令分派、键盘快捷键优先级或 WPF 活动 pane；因此不能证明应用当前菜单体验已合并，也不验证 ExplorerBrowser 产品菜单。产品代码、配置、release 与用户桌面未改动。

继续验证 View→Details 菜单命令分派：探针从激活后的 View 子菜单按本地化标题定位 Details 项并读取其运行时命令 ID（本机样本 `0x704B`），将 view 设为 Small Icons 作为基线。对宿主父 HWND 发送 `WM_COMMAND` 后模式仍为 `2`；重置基线后，将相同消息发送到目标 `IShellView` window，模式变为 Details `4`。断言覆盖父窗口无变化及目标 view 的模式变化，Release build 0 警告/错误，Private Desktop `menu-activation-private-desktop` watchdog 退出码 0，前台句柄保持 `0x81852`。

这说明原型中 Shell 菜单项自身的命令 ID 可由目标 view window 处理，也提示应用宿主需要把活动 view 的 Shell 命令送到正确窗口。实验使用合成 `WM_COMMAND`，没有真实跟踪菜单或鼠标选项；父窗口是探针的 `STATIC` host，不代表 WPF window 的默认/自定义窗口过程。仍需在真实 OLE 菜单循环和多 pane 活动目标下验证，不能直接当作产品路由实现已经确定。

### 2026-10-27 四窗格共享菜单宿主尝试未通过

为测试上节推导的多 pane 命令目标，在隔离 consumer 中尝试把 pane 的 Shell view parent（子窗格）与菜单 frame（顶层容器）分开，并按 pane 依序激活、向目标 view window 发送 Details 命令。NativeProbe Release build 通过，但 session-restore consumer 在 Pane 2 菜单读取处失败：Pane 1 可读到顶层共享菜单；切换到 Pane 2 后 `GetMenu` 返回的非零句柄没有可读菜单项（`GetMenuItemCount=-1`），当时 Pane 2 host 回调计数为 insert/set/remove `5/9/4`。失败日志：`src/.state/native-probe-watchdog/20261027-032413-14966fbe.stdout.log`。

这不是 Windows 菜单系统或 `IShellView` 的负面结论，因为失败发生在 NativeProbe 自建的多 host 菜单 frame 管理。该 frame split 实验代码已撤回，原先四窗格 session restore consumer 和产品 ExplorerBrowser 未改。当前证据表明每个 view 的菜单不能只靠各自 host 独立 Set/Remove 同一个顶层菜单；下一步需隔离真实回调顺序、句柄有效期及跨 host 所有权，再设计协调器并复跑四窗格激活/卸载和命令目标验证。未验证真实菜单点击或 WPF 产品窗口。

### 2026-10-27 四窗格菜单 SetMenu 子窗格错误根因确认

给每个 `StreamViewHost` 增加只读回调轨迹后，既有四窗格 Private Desktop session-restore probe 仍完整通过（退出码 0；文件状态恢复、菜单命令、copy 选择菜单、方向键、Ctrl+A、复制/重命名/删除及目录通知均通过）。但 `VerifyFourPaneMenuActivation` 的日志显示，四个 pane 的 `SetMenuSB` 虽都被调用，返回值全部为 `0x8007059C`，调用中传入的 frame HWND 是各自 pane 子窗口，随后 `GetMenu(frame)` 为 0。日志：`src/.state/native-probe-watchdog/20261027-032609-83148717.stdout.log`。

`0x8007059C` 是 `HRESULT_FROM_WIN32(ERROR_CHILD_WINDOW_MENU)`；`SetMenu` 文档规定菜单不能分配给 child window，因此原型当前把 menu 设在 pane 上必然失败。`OleSetMenuDescriptor` 也要求将 dispatch 安装到 container frame window；`IShellBrowser::SetMenuSB` 的用途是安装合成菜单，Shell 文档还要求 view deactivation 时通过 `RemoveMenusSB` 恢复菜单。依据：[SetMenu](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setmenu)、[系统错误码 1300–1699](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1300-1699-)、[OleSetMenuDescriptor](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-olesetmenudescriptor)、[IShellBrowser::SetMenuSB](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-setmenusb)、[Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)。

因此之前只检查 `InsertMenusSB`/`SetMenuSB` 回调次数的四窗格“菜单激活”结论只能证明回调到达各个 host，不能证明任何 pane 的合成菜单被安装。下一步必须将每 view 的 `IShellBrowser::GetWindow`（pane parent）与 top-level `IOleInPlaceFrame::GetWindow`/菜单 frame 分开，并由一个共享 frame owner 串行管理当前 menu、OLE descriptor、活动 view 和移除恢复。尚未验证该协调器、真实菜单跟踪或 WPF 产品接线；产品代码和 release 未改。

### 2026-10-27 OLE 菜单 frame 与 Shell pane HWND 分离（源码候选）

上一轮真实 `UIActivate` 菜单跟踪发现四个 pane 都回调 `SetMenuSB`，但 HRESULT 是 `0x8007059C`（`ERROR_CHILD_WINDOW_MENU`）。根因是 `IShellBrowser.GetWindow` 和 `IOleInPlaceFrame.GetWindow` 共用 child pane HWND；Win32 `SetMenu` 不允许把窗口菜单挂到 child window。微软文档也要求 OLE 菜单分派安装在容器 frame HWND 上：[SetMenu](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setmenu)、[OleSetMenuDescriptor](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-olesetmenudescriptor)。

NativeProbe 现在分开保留浏览器 pane parent 与 OLE top-level menu frame，四个 `IShellView` 共用 `ShellMenuFrameCoordinator`。协调器负责当前 owner 的 OLE descriptor 和菜单切换；迟到的非 owner `RemoveMenusSB` 不应卸载活动 pane 的菜单，切换安装失败会尝试恢复上一 owner。四窗格探针现会断言 Browser `GetWindow` 仍返回 pane，OLE frame `GetWindow` 返回共同顶层 HWND，并要求 `SetMenuSB` 成功且 frame 上确实有菜单。

当前仅 `dotnet build src/ExplorerDirs.NativeProbe -c Release --no-restore` 通过（0 警告、0 错误）；没有启动 Shell view 或隔离桌面，因此 menu coordinator 的真实 COM/OLE 生命周期、连续 pane 激活、旧 owner 延迟移除和 frame 菜单恢复尚未验证。产品 ExplorerBrowser、INI 与 release 未改；本候选代码未接入产品。

随后为协调器增加非 Shell hidden-window 回归：两个 child pane 的菜单依次接管同一 top-level frame；无效 HMENU 被拒绝且不影响当前 owner；旧 pane 对旧菜单的迟到 `RemoveMenusSB` 及释放旧 pane 都不会拆除新菜单；当前 owner 移除后恢复 frame 原菜单；同一 host 的 Browser `GetWindow` 与 OLE frame `GetWindow` 分别返回 child 与 top-level HWND。`dotnet build src/ExplorerDirs.NativeProbe -c Release --no-restore` 0 警告、0 错误，`dotnet run --project src/ExplorerDirs.NativeProbe -c Release --no-build -- --shell-view-color-policy-smoke` 退出码 0。它只验证 Win32/OLE coordinator 状态机和 COM vtable，不创建 `IShellView`、不显示菜单、不运行 Shell 激活，所以仍需前述真实四窗格隔离验证。

### 2026-10-27 四窗格菜单共享 frame 真实回调验证

第一次运行新菜单断言时 Pane 2 失败：诊断轨迹显示它仍将子窗格 HWND 当作 frame，并再次收到 `0x8007059C`。追查到原因是 session-restore consumer 在销毁并重建 Pane 2 host 时漏传 `menuFrameWindow` 和共享 coordinator；不是协调器的菜单交接失败。修正重建路径后，Release 构建 0 警告/错误，`session-restore-private-desktop` watchdog 连续三轮退出码 0，日志：`20261027-033501-8d0242bc.stdout.log`、`20261027-033524-cf73daba.stdout.log`、`20261027-033542-fdd57431.stdout.log`；另一次首次失败诊断为 `20261027-033410-53e04d8b.stdout.log`。

三轮中四个 Pane 的 `SetMenuSB` 均返回 `S_OK`，共享 top-level frame 均安装对应 pane 的合成菜单；pane 逐个激活时的旧菜单移除不会清掉新 owner 的菜单。四窗格 Details、排序、分组命令各自作用于目标 pane，view-state 多进程恢复、复制/重命名/删除与同/异目录 Shell 通知检查均通过。probe 明确报告 `foreground-unchanged=True`。这是真实默认 filesystem `IShellView` 在 Private Desktop 的原生激活/菜单合并证据；仍未显示或点击菜单，也未验证 WPF/产品宿主、物理键盘输入、扩展菜单和 OLE 拖放，因此不能据此迁移或发布产品宿主。

### 2026-10-27 四窗格最后一个 view 停用后恢复 frame 菜单

在四窗格菜单循环末尾对最后一个 view 调用 `UIActivate(SVUIA_DEACTIVATE)`，新增断言要求 Shell 增加 `RemoveMenusSB` 回调、顶层 frame 菜单恢复到 coordinator 创建时的原始菜单。NativeProbe Release 构建 0 警告/错误；`session-restore-private-desktop` watchdog 连续三轮退出码 0，日志 `20261027-033747-45b38d47.stdout.log`、`20261027-033826-666e2fb3.stdout.log`、`20261027-033846-ea5e22c8.stdout.log`。每轮轨迹显示最后 pane 的 `SetMenuSB(menu=0)` 与 `RemoveMenusSB` 均返回 `S_OK`，`GetMenu(frame)` 从活动菜单变回 0；前台 HWND 未变化。完整四窗格会话、命令、文件操作及目录通知样本也通过。

该检查补齐了原型的活动菜单卸载路径，但仍不显示或点击菜单，也不验证 WPF/ExplorerBrowser 产品宿主。Shell 拖放仍未通过 `DoDragDrop`：微软 [GetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos) 与 [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos) 文档要求调用线程位于 input desktop；Private Desktop 不是活动 input desktop，不能用它来定位拖动光标。没有切换用户桌面或注入输入；真实拖放仍需独占且获授权的可接收输入会话。

### 2026-10-27 F2 编辑状态下四方向键隔离验证

将原有的 Left/Right 检查扩展为 Left/Up/Right/Down。Private Desktop `host-newfolder-private-desktop` watchdog 通过：先用 Shell canonical `newfolder` 在临时目录创建项目，选中并聚焦它，向 `IShellView::TranslateAccelerator` 发送 F2，再把四个方向键分别发给原生 `Edit` 编辑框。四次返回 `S_OK`，编辑框持续存在且持续持有焦点；日志 `src/.state/native-probe-watchdog/20261027-034108-b117b401.stdout.log` 显示 `foreground-unchanged=True`。NativeProbe Release build 为 0 警告、0 错误。

这只证明单个原生 filesystem `IShellView` 的合成消息路径；没有覆盖 List/Icon 等视图方式、真实物理键盘、多窗格邻窗焦点、WPF `HwndHost` 或产品 ExplorerBrowser。该证据可用于后续宿主迁移比较，不能宣称用户报告的多窗格 F2 缺陷已修复。

### 2026-10-27 五种视图模式下 F2 编辑焦点隔离验证

将重命名焦点检查放入 Shell 与 `SysListView32` 同步模式循环，覆盖 Icon、Details、SmallIcon、List、Tile。初次尝试在 Icon 模式未进入编辑态；诊断显示 F2 返回 `S_OK`、选中项仍为 1，但焦点落在 `SHELLDLL_DefView`。根因是该通用筛选探针此前没有激活 Shell view。按原生宿主使用条件，在 Private Desktop 上以 `SW_SHOWNOACTIVATE` 显示父窗并调用 `UIActivate(SVUIA_ACTIVATE_FOCUS)` 后复测；结束时恢复隐藏父窗。

完整 `host-filter-private-desktop` watchdog 连续两轮退出码 0：`20261027-034456-3eb61774.stdout.log`、`20261027-034526-eca8b327.stdout.log`。五种模式各自的 F2 都进入 `Edit` 并获得焦点，Left/Up/Right/Down 均为 `S_OK` 且编辑框/焦点保持；Ctrl+A、Down 导航、多层 history 和本轮其它 Shell view 检查也通过。前台 HWND 保持不变。

该结果只验证 Private Desktop 内合成 accelerator 消息；没有验证物理键盘、相邻多窗格焦点争用、用户可见多窗格操作或 ExplorerBrowser 产品宿主。本次没有改产品视图或 release。

### 2026-10-27 四窗格 F2 编辑方向键焦点隔离

在四窗格 session-restore consumer 的完整流程中，对每个真实 filesystem `IShellView` 分别激活 view、聚焦其列表、选中一个项目并通过 `TranslateAccelerator` 启动 F2。向当前编辑框连续发送 Left/Up/Right/Down 后，编辑 HWND 与键盘焦点保持；逐键读取四个 view 的 focused item 和 selection，兄弟 pane 均未变化。结束时用 Escape 取消重命名并清除测试选择。

`session-restore-private-desktop` watchdog 连续两轮退出码 0：`20261027-034716-d5121c08.stdout.log` 与 `20261027-034738-631c462c.stdout.log`。日志报告 `pane1` 到 `pane4` 均 `arrows=4/focus-isolated=True`，四窗格菜单激活/卸载恢复、视图状态恢复、Shell 文件操作、同/异目录通知仍通过，前台 HWND 未变化。

这是 Private Desktop 内显式定向至目标 view 的合成 accelerator 检查。它验证四个 view 的原生焦点/选区互不串扰，但不模拟 WPF `HwndHost` 的 Tab/Shift+Tab 或错误 pane 路由，也未覆盖真实键盘、鼠标、可见窗口或 ExplorerBrowser 正式产品。因此不能视为用户报告的多窗格重命名问题已修复，产品代码和 release 未改。

### 2026-10-27 错误 pane 的 accelerator 不夺取活动 F2 编辑焦点

四窗格 consumer 新增反向路由试验：Pane 1 的原生 `Edit` 正持有 F2 重命名焦点时，将 `WM_KEYDOWN Down`（`hwnd` 指向 Pane 1 的编辑框）故意交给 Pane 2 的 `IShellView::TranslateAccelerator`。目标错误 pane 返回 `S_FALSE`；Pane 1 编辑框保持有效并继续持有焦点，随后对四个 pane 分别执行正确路由的 Left/Up/Right/Down，兄弟 view 的 focused item/selection 均保持不变。

`session-restore-private-desktop` 完整 watchdog 连续两轮退出码 0：`20261027-035210-efee867c.stdout.log`、`20261027-035232-37d2a85f.stdout.log`。两轮都报告 `foreign-route=pane2:00000001/pane1-edit-retained=True` 和四 pane `arrows=4/focus-isolated=True`，前台 HWND 未变化。

这是直接合成调用 Shell accelerator 的错误目标实验，不模拟 WPF `HwndHost` 事件链，也不能证明真实物理消息一定到达正确 host。它说明原生 DefView 在该样本中对错误 view 返回未处理并保留原编辑焦点；产品多窗格焦点问题仍需产品消息循环与真实输入验收。产品 ExplorerBrowser、配置和 release 未改。

### 2026-10-27 四窗格 Shell 菜单合并应用菜单组

根据微软 `Implementing a Folder View` 的宿主顺序，在 `SetMenuSB` 收到 Shell 已构造的 composite HMENU 后，给隔离 host 添加一个应用自有的 `Explorer Dirs` 顶层菜单组，含启用的 `Probe active-pane command` 项。菜单协调器切换活动 pane 或停用最后一个 view 时先解除 OLE 菜单 descriptor，再清理前一 owner 自己创建的 popup；Shell 回调传来的 HMENU 与宿主先前持有的 root 不完全一致时，按自有 popup 句柄清理，避免误删 Shell 菜单项。顶层 frame 用同线程 `SetWindowSubclass` 接收 `WM_COMMAND`，并将该 probe 命令交给当前 coordinator owner。

NativeProbe Release build 0 警告/错误；无 Shell view 的 `--shell-view-color-policy-smoke` 通过；完整 `session-restore-private-desktop` watchdog 退出码 0，日志 `src/.state/native-probe-watchdog/20261027-040514-e132943a.stdout.log`。四个真实 filesystem `IShellView` 逐一激活后，frame 上当前 composite 均包含应用菜单组；向 frame 发送该命令的 `WM_COMMAND` 后，只有当前 pane 的计数递增，四个 pane 各为 1；最后停用所有 pane 后再发送则被忽略。每次切换会清理前一 host 自有 popup，并在最终停用后恢复原 frame 菜单。四 pane view-state 恢复、Shell Details/排序/分组命令、选中项 copy 菜单、Ctrl+A、F2 四方向键与选区隔离、文件复制/重命名/删除、目录通知检查均继续通过；foreground HWND 未变化。

这证明 Private Desktop 隐藏原生宿主内应用菜单组与 Shell 菜单共存，且合成 `WM_COMMAND` 只分派给当前活动 pane；不等于真实点击菜单项，因为探针直接向 frame 发送消息。probe 命令没有产品功能语义；没有显示菜单、运行真实 OLE 菜单消息循环、验证快捷键、扩展菜单或 WPF 产品接线。ExplorerBrowser 产品、配置与 release 未改。依据：[Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)、[IShellBrowser::SetMenuSB](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-setmenusb)、[SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass)。

### 2026-10-27 Shell 原生菜单命令 frame 转发尚未通过

在共享 frame subclass 中尝试把 Shell 菜单 ID `0x704B`（Details）转发给 coordinator 当前记录的 `hwndActiveObject`。Private Desktop 四 pane 探针能收到 `WM_COMMAND`，但目标 pane 2 仍为 Small Icons；回调轨迹显示菜单 coordinator 的 owner 和 active HWND 仍是 pane 1。随后尝试按焦点 HWND 映射 pane，目标模式仍未改变（日志 `20261027-041155-4116eada.stdout.log`）。因此不能把“菜单项 ID 可读”当作“Shell 命令已正确执行”。

微软的 [Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview) 明确说明，Shell view 菜单的相关 `WM_COMMAND` 由 Explorer 宿主处理，不会传给 folder view 对象；因此把 frame 消息直接发给 `IShellView` 是错误的架构假设。`IShellBrowser::OnViewWindowActive` 用于通知宿主 view 获取焦点，`SetMenuSB` 安装 composite menu，但这两项文档本身没有定义我们这个自建宿主怎样执行 Details 命令。参考：[IShellBrowser::SetMenuSB](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-setmenusb)、[IShellBrowser::OnViewWindowActive](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-onviewwindowactive)。

失败的 Shell 命令转发分支已撤回；上一节已通过的应用自定义 probe 命令 active-pane 路由保持。下一步先从 Windows Explorer/Shell 实现和公开接口确定宿主必须承担的标准命令执行路径，再设计最小 pane dispatch probe；在此之前不将 Shell 菜单分派能力列为已完成，也不迁移产品宿主。

### 2026-10-27 撤回 frame 转发实验后的四窗格原生命令基线复核

在恢复到已提交的应用菜单 probe 后，重新运行 Release build 与完整 `session-restore-private-desktop` watchdog。构建 0 警告/错误，watchdog 退出码 0，日志 `20261027-041344-088dec96.stdout.log`。四个 filesystem `IShellView` 通过 Shell 背景 `IContextMenu::InvokeCommand` 的 canonical verbs 分别执行 Details、排序和分组；会话恢复、菜单 merge 生命周期、自定义 app command active-pane 路由、复制/重命名/删除、目录通知及四 pane F2 方向键焦点隔离也全部通过，foreground HWND 未变。

这轮确认撤回未损坏已通过的原生能力。但 `menu-commands=pane1:details/sort/group,...` 走的是逐 pane 背景 context menu canonical verb，不是 frame 上合成 View 菜单的 `WM_COMMAND`；两者必须分开记录。后续需查明 Explorer 宿主如何处理 standard `FCIDM_SHVIEWFIRST..LAST` 命令，并据此为自建 frame 建立受支持的执行路径。

随后把两种 Details 命令 ID 做只读对比：真实 composite View 菜单项 ID 为 `0x704B`；查询同一 pane 的背景 `IContextMenu` 时以 `0x7000` 为 `idCmdFirst`，canonical verb `viewlogicaldetails` 对应 `0x701B`。二者不相同，因此不能把 View 菜单 ID 直接当作 context-menu verb ID。包含对比的四窗格 watchdog 再次退出码 0，日志 `20261027-041457-17b0712f.stdout.log`，所有既有 pane 操作/会话检查仍通过。该差异不证明 Explorer 具体如何执行 `0x704B`；仍需找到宿主命令表或标准分发实现。

### 2026-10-27 四窗格原生 Shell view 的 drop target 生命周期复核

NativeProbe Release 构建通过（0 警告、0 错误）。运行 `scripts/native-probe-watchdog.ps1 -Probe session-restore-private-desktop -TimeoutSeconds 120`，watchdog 退出码 0，日志 `src/.state/native-probe-watchdog/20261027-053426-3ca5dac1.stdout.log`。四个真实 filesystem `IShellView` 的创建、激活、视图状态恢复、菜单切换、复制/重命名/删除、同/跨目录更新通知和方向键焦点隔离均通过，结束时 `foreground-unchanged=True`。本轮产品外的宿主原型在每个 `CreateViewWindow` 后取得该 view 文件夹的 `IID_IDropTarget` 并注册新 HWND；导航时先注册新窗口，再撤销旧窗口；view 销毁前撤销注册。四窗格完整探针顺利完成，证明该注册/替换/撤销路径没有破坏当前 host 生命周期。

另运行 `ExplorerDirs.NativeProbe.dll --shell-view-drop-target-registration-smoke`，通过单 host 四 HWND 注册、重复注册冲突不改变原 owner、选择性撤销与重新注册、Dispose 撤销其余注册，且前台窗口保持不变。

边界：以上没有调用 `DoDragDrop`，也没有让用户文件经鼠标跨 pane 拖动。它确认目标接口已绑定到真实 view HWND，不证明 Drop effect、移动/复制语义、外部拖放源/目标、可见交互或 ExplorerBrowser/WPF 产品接线。真实拖放验收仍需要可接收 OLE 输入的隔离桌面，并保持与用户日常桌面分离。

### 2026-10-27 IShellView 文件夹 drop target 的 Shell 数据对象接收

新增 Private Desktop 临时夹具探针，对比 `IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IDropTarget)` 与从目标文件夹 `IShellFolder::CreateViewObject(IID_IDropTarget)` 取得并注册到该 view HWND 的目标。当前 filesystem `IShellView::GetItemObject` 返回 `E_NOINTERFACE (0x80004002)`；文件夹目标的 `DragEnter`、`DragOver`、`Drop` 均返回 `S_OK`，Copy effect 为 1，使用 `SHCreateDataObject` 构造的 Shell 文件数据对象将临时文件复制到目标目录，源文件内容与存续状态都保持。NativeProbe Release build 0 警告/错误，`drop-target-private-desktop` watchdog 退出码 0，`--shell-view-color-policy-smoke` 通过，foreground 未变化。

前两次探针运行在文件操作完成后卡于 Private Desktop 顶层 parent 的 `DestroyWindow`，watchdog 以超时结束；诊断确认 drop 与 Shell view 的撤销/销毁均已完成。按现有 Private Desktop 约束改为由一次性进程退出回收 top-level parent 后，探针退出码 0。两次被 watchdog 结束的探针各遗留一个仅含测试文件的临时 fixture，路径为 `%TEMP%\ExplorerDirs-NativeProbe\81c4ff72648f49fba34e19442d1aa5d4` 与 `%TEMP%\ExplorerDirs-NativeProbe\a9ef35a4f4244e62bb1150f2ad1cbdf9`；当前工具拒绝删除这些用户 temp 目录，故未声称已清理。

这项结果比单纯注册 smoke 多验证了文件夹目标确实能消费 Shell `IDataObject` 并执行复制；但数据对象由 `SHCreateDataObject` 构造，不是从真实源 view 当前选择取得。它不证明真实 OLE `DoDragDrop`、鼠标命中/修饰键源端交接、移动语义、第三方目标或可见多窗格行为；`DoDragDrop` 仍待可接收输入的独立桌面验收。

### 2026-10-27 对比 IShellView 选中项数据对象

> **更正（2026-10-27）：**早期探针把选择数据对象的目标文件名错设为 `selected-view.txt`，但当时实际选中的是 `sourceItemPidl` 对应的 `view-target.txt`。Drop 创建原文件名后，探针只检查错误的目标路径，因而把成功复制误报为失败。此前本节及路线图中的“真实选中项对象不复制”结论无效，以下更新结果为准。

扩展 Private Desktop 探针创建源 `IShellView`，等待临时目录条目完成枚举，选择一个文件并调用 `IShellView::GetItemObject(SVGIO_SELECTION, IID_IDataObject)`。该调用成功；同一目标文件夹的 `IDropTarget` 对此数据对象的 `DragEnter`、`DragOver`、`Drop` 均返回 `S_OK` 且 Copy effect=1，但目标目录没有新增文件。作为对照，单独用 `SHCreateDataObject` 从同一源目录构造的数据对象仍能复制并保留源文件。`IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IDropTarget)` 仍返回 `E_NOINTERFACE`。

两个对象的 `IDataObject::EnumFormatEtc(DATADIR_GET)` 结果相同，均含 7 种公开格式：`Shell IDList Array`、`CF_HDROP`、`FileName`、`FileContents`、`FileNameW`、`FileGroupDescriptorW`、`ZoneIdentifier`，且 aspect、index 和 tymed 属性一致。当前证据排除了“格式列表缺少常用 Shell 文件格式”这一简单解释，但没有比较延迟渲染的数据内容、`IDataObjectAsyncCapability` 或源端 `IDropSource`/OLE drag-loop 生命周期。

继续读取 `CF_HDROP`：两个对象都返回同一个 `source\view-target.txt` 完整路径。两者均可查询到 `IDataObjectAsyncCapability`，`GetAsyncMode=False` 且 `InOperation=False`。因此当前比较过的公开格式列表、文件路径和异步接口初始状态一致；尚未比较 Shell IDList Array 的 PIDL 内容。直接 `IDropTarget.Drop` 成功码却没有复制文件，仍不能说明完整 `DoDragDrop` 会失败，因为探针没有 OLE drag source/loop。

随后解码两个对象的 `Shell IDList Array` CIDA：两者的父 PIDL 都解析为同一临时 `source` 目录，唯一子 PIDL 都解析为同一 `view-target.txt`。因此当前系统上可读的格式列表、CF_HDROP 路径、CIDA 路径及 async capability 初始状态都相同；剩余差异可能在 COM 对象身份/内部状态或完整调用协议，现有证据不能再缩窄。微软 OLE 文档将 `DoDragDrop` 描述为调用 `IDropSource` 与 `IDropTarget` 的完整循环；直接分别调用目标的 `DragEnter/DragOver/Drop` 只验证目标方法响应，不等价于该循环。依据：[OLE drag-and-drop](https://learn.microsoft.com/en-us/windows/win32/com/drag-and-drop)、[Shell data object](https://learn.microsoft.com/en-us/windows/win32/shell/dataobject)。

首次运行发现源 `sourceChildPidl` 是 `ILFindChild` 指向绝对 PIDL 内部的借用指针，不应释放；错误释放导致 heap corruption。移除释放并加入最多 3 秒的 Shell 枚举等待后，探针稳定运行。当前日志还会列出目标目录最终文件名，防止仅依赖 Drop 的成功码误判操作已完成。

NativeProbe Release build 0 警告/错误；Private Desktop `drop-target-private-desktop` watchdog 退出码 0，`foreground-unchanged=True`。仍未调用真实 `DoDragDrop`，也没有验证鼠标源端、移动语义、第三方目标、可见交互或产品宿主接线。下一步在禁止合成用户输入的边界内评估 OLE drag-loop 验证方式，并继续迁移所需的其余宿主能力；本探针不会被视为拖放迁移完成。

#### 2026-10-27 更正后的当前会话复跑

现将 view 选择明确改为 `selectedSourcePidl`，目标断言同时核对原文件名和精确 fixture 内容。`scripts/native-probe-watchdog.ps1 -Probe drop-target-private-desktop -TimeoutSeconds 45` 退出码 0：目标文件夹 `IDropTarget` 直接消费真实 `IShellView::GetItemObject(SVGIO_SELECTION)` 数据对象后，`selected-view.txt` 已创建且内容匹配，源文件保留；`SHCreateDataObject` 路径也通过。前台 HWND 未变。新日志：`src/.state/native-probe-watchdog/20261027-064205-255387b2.stdout.log`。

这次把异步状态读取扩展到 `DragEnter`、`DragOver` 和 `Drop` 前后；两个对象在所有阶段均保持 `async-mode=False`、`in-operation=False`，状态没有变化，也无需用异步操作状态解释前述假失败。此前针对 `FileContents`、HGLOBAL 格式、Drop 键状态及协商格式的观测仍是数据，只是不能再支撑“选中项对象不复制”这一结论。

本证据只验证直接调用目标接口的复制和目标内容，不等于完整 OLE `DoDragDrop` 源端/目标交接；鼠标命中、Ctrl/Shift/右键拖动、移动语义、可见交互和产品宿主仍待独占且获授权的桌面验收。依据：[OLE drag-and-drop](https://learn.microsoft.com/en-us/windows/win32/com/drag-and-drop)。

### 2026-10-27 拒绝已过期 owner 的延迟 Shell View 命令

frame subclass 收到合成菜单 `WM_COMMAND` 后会投递自定义消息，再由当前 coordinator owner 执行；View 菜单分支之前没有复核 owner，Edit → Select All 已有该检查。两条消息之间切换 pane 时，旧命令可能以新 frame 菜单匹配出命令 ID，却向旧 owner 的 view HWND 发送。本轮在 View 命令执行前加入 owner 检查，并把 coordinator 活动状态传入 dispatcher。NativeProbe Release 构建 0 警告/错误；无 Shell-view 的 `--shell-view-color-policy-smoke` 通过，新增隐藏 frame 两 pane 回归确认 stale owner 的已识别命令被消费但不派发。没有创建 `IShellView`、显示菜单或启动产品 ExplorerBrowser；实际 Shell 菜单时序及可见行为仍待隔离桌面验收。

### 2026-10-27 无窗口原型基线复核

重新执行 `dotnet build src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release --no-restore`，0 警告、0 错误；随后运行 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release --no-build -- --shell-view-color-policy-smoke`，退出码 0。该 smoke 覆盖颜色策略、隔离宿主 COM 契约、导航历史、共享菜单 owner 变更及 `IDropSource` 回调约定；不创建 `IShellView`，不执行 `DoDragDrop`，也不验证物理输入、实际菜单点击或 WPF 产品集成。此结果只确认纯逻辑/宿主契约基线当前可编译且通过，拖放迁移门槛仍未关闭。

### 2026-10-27 合并菜单命令 ID 必须唯一归属

源码审查发现，frame 收到延迟 `WM_COMMAND` 后原先按 View、File、Edit 的固定优先级查找相同命令 ID。`WM_COMMAND` 本身只携带低 16 位命令 ID，不携带来源菜单组；若 composite HMENU 在多个组复用同一 ID，固定优先级可能把命令解释成另一组的命令。旧合成菜单 smoke 已构造跨组重复 ID，但没有验证最终 frame 路由。

现在 NativeProbe 先在全部六个 OLE 菜单组中解析命令归属；只有命令仅出现在 File、Edit、View 中的一个组时才执行对应分支，Object、Window、Help 组的 ID 不交给 Shell view。多组重复或找不到的 ID 都不作 Shell 命令派发。Smoke 覆盖 File/Edit/View 的唯一命令、与其它组冲突的 ID 拒绝、原始 ID 到达隐藏假 Shell HWND，以及旧 view 重入切换保护。先补回归并观察到缺失 resolver 的编译失败，再实现；`dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --shell-menu-group-routing-smoke` 退出码 0。

本轮不创建 `IShellView`，不启动 ExplorerBrowser，不显示菜单。它验证的是应用自有 frame 的 synthetic HMENU 路由策略；真实 Shell composite menu 的 ID 分配是否出现重复仍未知，所以不能声称已观察到用户可见误派发，也不能替代真实 Shell 菜单验证。

### 2026-10-27 为列头 popup 记录消息 owner 窗口链

微软的 `WM_INITMENUPOPUP` 契约说明消息会发给下拉菜单的 owner window；现有列头探针读取到该 HWND，但只把它用于初始化子菜单，没有保存或输出。现为探针记录首次 popup 的 owner HWND 与最多八层父窗口的 HWND/窗口类名，便于后续辨认实际收件者属于 header、view 还是宿主 frame。

本轮只改 NativeProbe 诊断输出并进行 Release 编译；没有运行需要创建 `IShellView` 的列头菜单探针，因此 owner 链仍无运行时结论。该日志用于确定真实 popup 的菜单 owner，不等同于用户选中菜单项后 `WM_COMMAND` 的最终接收 HWND；命令分派仍待单独证据。[WM_INITMENUPOPUP](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-initmenupopup)

### 2026-10-27 使用 WM_MENUSELECT 来源区分同 ID 菜单项

前一批为避免误派发，缺少来源信息且跨组 ID 重复时会拒绝命令；这保证了安全，却会让合法的重复 ID 项不可用。微软 `WM_MENUSELECT` 消息提供当前选中项 ID/弹出菜单索引及菜单句柄；现在 NativeProbe 保存该选择路径对应的子菜单来源，并在延迟命令队列中保留来源句柄。路由器先依来源菜单确定 OLE 组：File/Edit/View 按各自组执行，Object/Window/Help 不传给 Shell view；没有有效来源时仍只接受唯一归属的 ID。对设置 `MNS_NOTIFYBYPOS` 的路径也加入 `WM_MENUCOMMAND` 的 menu+index 识别。

合成菜单 smoke 覆盖嵌套 `WM_MENUSELECT` 参数序列、File 与 View 同 ID 分派、Object 同 ID 拒绝、显式 Reset 清除来源状态、来源句柄随 FIFO 队列保存，以及真实隐藏假 HWND 收到原始命令 ID；NativeProbe Release build 与专用 smoke 通过。测试没有进入真实菜单模态循环，也没有创建 `IShellView`，所以实际 Shell/OLE 是否把菜单消息送到当前 frame、菜单退出与 `WM_COMMAND` 的时序仍待隔离宿主验证。依据：[WM_MENUSELECT](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-menuselect)、[WM_COMMAND](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-command)、[WM_MENUCOMMAND](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-menucommand)。

### 2026-10-27 隐藏 frame coordinator 消息链 smoke

扩展专用 smoke：用普通隐藏 Win32 frame 与 pane、六组 synthetic HMENU、真实 OLE menu descriptor 和 `ShellMenuFrameCoordinator` subclass；发送合成 `WM_ENTERMENULOOP`、嵌套 `WM_MENUSELECT`、`WM_COMMAND`，再从消息队列分派 coordinator 实际投递的延迟消息。File 与 View 复用合法范围命令 ID `0x7123`，分别由来源菜单路由到隐藏假 pane HWND，并分别记录 group 0/group 2。此验证覆盖 subclass 收件、来源状态、排队、PostMessage 及最终 HWND 送达，未模拟系统菜单跟踪，也未创建 Shell view。

随后把同一 coordinator smoke 扩展到按位置通知的 `WM_MENUCOMMAND`：File 与 View 的重复 ID `0x7123` 分别按各自 `HMENU` 派发，Object 组的同 ID 则不投递命令。再连续提交 File 与 View 两个 `WM_COMMAND`，在消息泵派发前改变当前选中来源；FIFO 两次延迟派发仍分别保留 File、View 的来源组。追加“命令排队后停用 pane，再处理延迟消息”场景；已退休菜单的命令不会到达旧 view HWND。NativeProbe Release build 0 警告/错误，`--shell-menu-group-routing-smoke` 通过。它补足 synthetic 消息链的集成证据；真实 OLE/DefView 模态菜单实际消息次序、命令 ID、接收 HWND 与用户可见行为仍未验证，迁移门槛不关闭。

### 2026-10-27 四窗格恢复探针停滞阶段追踪

为区分全套恢复探针中偶发超时的具体边界，增加菜单选择、四 pane 菜单激活/命令、文件操作和各 pane 计数等待日志；计数等待可选记录 WinForms message filter 看到的消息号、HWND 和窗口类。NativeProbe Release 构建通过（0 警告、0 错误），Private Desktop watchdog 多轮退出码 124。停滞点并不固定：样本曾停在菜单激活前后、pane 菜单命令、复制/重命名/删除，以及删除后的消息泵/其它窗格计数等待。`20261027-083314-807898ed` 的删除主视图到第 5 次轮询后匹配，Pane 1 收敛后转到 Pane 2 等待；`20261027-083437-2c3cd84f` 使用 120 秒预算，推进至 `file-delete-start` 后仍超时，尚无 `file-delete-performed`。Private Desktop 前台窗口保持不变。

这些分布式停滞日志不能确定共同根因，也不能证明是固定 Shell 命令、文件操作或 `Pump` 消息派发失败；目前没有加入规避逻辑或延长产品超时。后续需用可取得原生线程栈的手段捕获一次真实停滞，之后才能定位 COM 调用、DefView 消息处理或测试夹具生命周期。完整四窗格 Shell 宿主迁移仍未过可靠性门槛，产品 ExplorerBrowser 未改。
### 2026-10-27 watchdog 超时递归清理回归

此前 `scripts/native-probe-watchdog.ps1` 超时只调用 `Process.Kill()`，而会话恢复 probe 会启动独立 consumer 子进程。该实现只保证直接子进程退出，不保证 consumer 的后代进程退出。新增 `-TestTimeoutPath` 回归夹具：由 watchdog 测试父进程启动 30 秒 pwsh 孙进程，记录孙进程 PID，再令父进程在 5 秒时限超时。

修复前测试以失败退出，并观察到存活孙进程 PID `32872`；测试清理随后回收该进程。改用 `Process.Kill($true)` 后，回归输出 `selftest=timeout-tree-passed; seconds=5; parent-exited=True; grandchild-exited=True`。这确认了 watchdog 会递归结束被测进程树。

之后用新 watchdog 复跑 `session-restore-private-desktop`，该探针仍超时，最后阶段记录到 pane 4 的 sort/group 菜单命令检查；日志为 `src/.state/native-probe-watchdog/20261027-083750-52d4c612.stdout.log`。watchdog 返回超时后，进程快照中没有 NativeProbe 或会话恢复 consumer 遗留。由此只确认超时清理有效；IShellView 四窗格恢复停滞仍未定位，也不能推断为本次遗留子进程问题所致。

### 2026-10-27 定位四窗格 session-restore 的变化停点

为区分 Shell 调用挂起与测试预算不足，在菜单读取、Select All 路由、Shell 动词调用、状态断言和文件操作边界增加阶段日志。多轮的最后阶段会变化：有的停在菜单检查，有的已完成四 pane 菜单路由并进入复制/重命名。检查 Private Desktop runner 后发现，真正的内层执行线程有固定 25 秒 deadline；整个 session-restore 仍在执行时，该 runner 就向 watchdog 报告 timeout。此前“阶段不定的 Shell 停滞”结论应由此修正为测试宿主提前超时；没有证据表明存在某个固定 pane/动词死锁。

现只为 `session-restore` 和它派生的 consumer 将 Private Desktop runner deadline 调为 110 秒与 100 秒，并把父进程等待 consumer 的时间调为 100 秒；其余 probe 保持 25 秒。NativeProbe Release build 0 warning/error。`scripts/native-probe-watchdog.ps1 -Probe session-restore-private-desktop -TimeoutSeconds 120` 退出码 0，Private Desktop foreground HWND 前后相同。成功日志 `20261027-084330-8968e6b6.stdout.log` 的 consumer 总结确认：状态流恢复、四 pane 菜单项逐窗格路由、方向键/Ctrl+A/F2 路由、copy/rename/delete 四 pane 收敛、目录通知及 sibling pane 隔离均通过。

本次通过只覆盖隔离 NativeProbe 中的 IShellView 宿主，不表示已接入 Explorer Dirs 产品；未运行可见 UI，也没有验证 OLE `DoDragDrop` 的鼠标源端、WPF 焦点链或用户实际拖放。

### 2026-10-28 捕获菜单清理挂起的 WCT 等待链

为给 `single-pane-menu-sync-cleanup` 的稳定挂起补充线程级证据，NativeProbe 在其 Private Desktop watchdog 超时点调用 Windows Wait Chain Traversal，并注册 OLE32 的 COM 状态回调。SDK `wct.h` 校正了节点枚举：`WctThreadType=8`；查询显示 `cycle=False`，探针线程 58152 处于线程等待，目标为同进程线程 58168；目标线程也在等待，后续节点为 COM → `svchost` PID 1832、COM activation → `ChsIME` PID 49244。当前快照不能证明循环死锁；WCT 只报告调用时的等待链，也不能单凭进程名确定具体 COM 类或组件责任。[Microsoft WCT sample](https://learn.microsoft.com/en-us/windows/win32/debug/using-wct)、[GetThreadWaitChain](https://learn.microsoft.com/en-us/windows/win32/api/wct/nf-wct-getthreadwaitchain)

本次差分结果：未激活菜单的单窗格 Shell view 清理正常完成，父窗口收到 `WM_DESTROY` 和 `WM_NCDESTROY`；单独的菜单激活探针也完成，但它把父窗口清理由进程退出负责，因此没有验证同步 DestroyWindow。相反，焦点激活后显式停用、跳过显式停用，以及 no-focus 激活后停用，这三种随后同步销毁父窗口的变体都可复现卡在 `WM_DESTROY` 返回之后；no-focus 变体重复时也有一次正常完成，说明有间歇性。各挂起样本的 WCT 链一致落到同进程等待线程、COM、`svchost` 和 `ChsIME`，且 `cycle=False`。这排除了“必须显式调用 DEACTIVATE”及“必须获得焦点”这两个单一条件，但尚未区分菜单激活留下的 Shell/OLE 状态与 Private Desktop 上的系统输入法 COM 行为。

NativeProbe Release 构建通过（0 警告、0 错误）；全程不打开产品 GUI，Private Desktop 前台 HWND 保持不变。产品 ExplorerBrowser、release 和主题代码未改。当前只能说同步顶层窗口销毁在部分菜单激活历史之后不稳定，不能宣称 Explorer Dirs 产品发生死循环，也不能把根因归给输入法/RPCSS。下一步是检查窗口的 IME context、子窗口和 OLE menu coordinator 在 DestroyWindow 前后的状态，并设计不切换用户桌面的差分条件；在有可归属原生栈或稳定差分前，不做产品规避改动。

追加差分：跳过显式 `SVUIA_DEACTIVATE`，让菜单仍处于 active 状态并由 coordinator 在 pane/view 释放后清理，父窗口仍在 `WM_DESTROY` 返回后挂起，WCT 链同样到 `ChsIME`。`SVUIA_ACTIVATE_NOFOCUS` 加显式停用的三轮结果为两次挂起、一次完成；完成轮的断言已修正为“未安装共享菜单是预期”，不把它误报成菜单安装失败。因此焦点激活、显式停用都不是必要单一条件；仍未验证的是菜单激活历史与 Private Desktop COM/IME 环境的组合效应。新加入的参数化隔离 probe 保留作后续对照入口。

窗口状态检查补充：普通清理基线与菜单挂起样本在父窗口销毁前都为 `Static` 顶层窗、零子窗、IME context 存在；菜单样本的 Shell view/pane 已销毁、coordinator 无 owner、active object 为 0、窗口菜单句柄为 0。为检验窗口 IME context 是否是直接触发条件，另一个隔离变体在父窗口销毁前调用 `ImmAssociateContext(hwnd, NULL)` 并确认当前 context 为空；挂起与同一 COM→`svchost`/`ChsIME` 链仍复现。此差分削弱了“父窗口当前关联 HIMC 直接导致挂起”的解释，但不能排除线程级输入法/COM 服务交互或 Private Desktop 环境因素。日志：`20261028-054952-9aed3674.stdout.log`、`20261028-054958-29b528b9.stdout.log`、`20261028-055117-d1b709b7.stdout.log`。

随后在 `DestroyViewWindow` 前枚举 pane 子窗：`SHELLDLL_DefView`、`SysListView32`、`SysHeader32` 均有 IME context；挂起样本与不挂起的 no-menu 基线结构一致。测试隔离地从 pane 与这些 Shell 子窗逐一解除 HIMC 后，父窗口销毁仍挂起，WCT 仍指向同一 COM activation 链。另一变体在探针 STA 线程创建任何 Shell 窗口前调用 `ImmDisableIME(threadId)`，连续 3 次菜单激活与完整窗口清理全部通过；窗口 HIMC 仍存在。当时的结果一度提示线程级 IME/COM 交互，但后续 WPF HwndSource 根窗对照在 IME 启用时 3/3 正常销毁，削弱了该解释；现阶段不能把 ChsIME 或线程级 IME/COM 认定为根因。尝试用系统 WPR CPU profile 采集原生切换栈时，因缺少 `SeSystemProfilePrivilege` 失败（`0xc5585011`）；确认 WPR 未开始记录。日志：`20261028-055513-466f35ab.stdout.log`、`20261028-055539-4215b539.stdout.log`、`20261028-055433-e74b8cc7.stdout.log`、`20261028-055443-56672aa0.stdout.log`、`20261028-055450-a748daf7.stdout.log`、`20261028-055650-24a14d94.stdout.log`。

`ImmDisableIME` 只用于私有探针 STA，不作为产品修复：在应用共用 UI STA 上禁用输入法会破坏中文输入，且逐窗 detach HIMC 已证明不足。当前 IShellView 迁移仍需解决/规避这一线程级生命周期交互；由于缺少系统采样权限，下一步优先检查 WPF/UI STA 与私有 STA 的 COM/IME 初始化差异，并评估是否能让 Shell host 在自身可回收的 STA 上运行，而不是降低 Shell 功能或把全局禁用输入法带入产品。

### 2026-10-28：显式 OLE 初始化未改变菜单后销毁挂起

为区分“隔离 STA 没有 OLE 初始化”与“WPF STA/消息环境差异”，新增仅供 NativeProbe 使用的 opt-in `private-desktop-single-pane-menu-ole-init-sync-cleanup`。它在隔离 worker STA、创建 Shell 窗口之前调用 `OleInitialize`，保持输入法启用，并在探针正常返回时配对 `OleUninitialize`；没有修改产品 ExplorerBrowser 或共享 WPF UI 线程。

Release NativeProbe 构建 0 警告/错误。该用例连续两次在父窗口 `WM_DESTROY` 回调正常返回后卡于 `DestroyWindow`，均由 50 秒 watchdog 结束；两次 WCT 快照都显示同进程线程等待 → COM → `svchost` COM activation → `ChsIME`，`cycle=False`，前台 HWND 未变化。日志为 `20261028-060248-029d195c.stdout.log` 和 `20261028-060400-db16b703.stdout.log`。因此“仅缺少 OleInitialize”不能解释这组复现；这仍是等待链快照，不是调用栈，也不能证明产品 ExplorerBrowser 有同一问题。下一步应单独检查 WPF Dispatcher/消息泵差异，再决定是否值得继续专用 STA 架构验证；不得把 `ImmDisableIME` 带入产品。

### 2026-10-28：WPF HwndSource 根窗下同步清理通过

在前一轮“显式 OLE + WPF DispatcherFrame”仍挂起的基础上，只将顶层窗口从原生 `STATIC` 改为 WPF `HwndSource`；其下仍创建同样的 pane `STATIC`、真实 `IShellView` 和共享菜单 coordinator，输入法保持启用。新路线连续 3/3 完成 `DestroyViewWindow`、pane `DestroyWindow` 与根 `HwndSource.Dispose`；根窗均经过 `WM_DESTROY` 和 `WM_NCDESTROY`，`IsWindow` 变为 false，前台 HWND 不变。日志：`20261028-061036-7b7cb8f2.stdout.log`、`20261028-061047-2181c798.stdout.log`、`20261028-061049-8c00b176.stdout.log`。

这是一项强差分：显式 OLE 与 WPF DispatcherFrame 单独及组合用于原生 `STATIC` 根窗时仍挂起，而 WPF 根窗组合 3/3 成功；此前将现象归因为线程级 IME/COM 交互过强，现已修正为尚无根因结论。给根 `HwndSource` 加上 WPF `Grid` 根视觉后，直接 `SetFocus(root)` 会把焦点置于 pane `STATIC`，但这属于探针额外注入的焦点变化，不能代表 Shell 菜单停用后的自然焦点状态。

### 2026-10-28：四 pane WPF HwndHost 菜单与同步清理

为匹配产品的 `HwndSource → WPF Grid → HwndHost → STATIC pane → Shell view` 层级，隔离探针现通过 WPF `Grid` 托管四个真实 `HwndHost`，每个 host 创建自己的 `STATIC` pane 并承载一个原生 `IShellView`。在输入法启用、显式 `OleInitialize` 和 WPF DispatcherFrame 泵下，依次对四 pane 激活/停用 Shell 菜单，再按序销毁 view、pane HwndHost 与根 HwndSource；连续 3/3 完整通过。每轮菜单插入/移除与 coordinator owner 释放均通过；菜单停用后 `GetFocus` 均落在当前 `SysListView32`，根窗销毁收到 `WM_DESTROY`/`WM_NCDESTROY`，`IsWindow=False` 且主桌面前台不变。日志：`20261028-061743-bc406769.stdout.log`、`20261028-061800-5928a72b.stdout.log`、`20261028-061804-3bb96028.stdout.log`。

这同时解释了前一轮 `focus-restored-to-parent=False`：显式 `SetFocus(root)` 把已恢复到 Shell 列表的焦点改到 pane host；取消该人为干预后，WPF HwndHost 路径保留了 Shell 列表焦点。此证据验证的是隔离 WPF/Shell 组合，不是产品 ExplorerBrowser 的可见输入体验；没有验证真实鼠标 OLE 拖放、四 pane 用户输入、复制/移动文件、Shell 扩展异常或生产宿主替换。原型以外的产品代码未改。
### 2026-10-27 导航历史拒绝嵌套请求并延迟关闭释放

`ShellViewNavigationHistory` 的 callback 是可重入边界；原组件没有 busy/disposed 状态，合成回调中的第二次 BrowseObject 会提交另一目标，外层仍按旧请求更新历史。本批用 ERROR_BUSY HRESULT 拒绝尚未完成的同实例嵌套请求，finally 解除 gate，不建立无界延迟队列。回调中关闭会返回 E_ABORT 并阻止提交；历史 PIDL 直到 callback 退出后才释放，避免 Back/Forward 借用目标被提前释放。重复 Dispose 不重建历史，关闭后新导航拒绝。

合成 PIDL 池回归先失败后通过，并检查下一次普通请求能继续、关闭期间 Back 目标可读及返回后清空。NativeProbe Release build 0 warning/error，完整 `--shell-view-color-policy-smoke` 通过。本次没有创建 Shell view 或运行 Private Desktop；真实 Shell 扩展重入、宿主关闭与历史/视图一致性仍需隔离原生验收，产品 ExplorerBrowser 不变。
### 2026-10-27 延迟菜单命令绑定安装代次

隐藏 frame coordinator 的新增合成消息回归先复现：命令排队后 RemoveMenusSB、再 SetMenuSB 同一个 host/HMENU，owner 再次相等，旧命令被送到假 pane HWND。现在 queue 保存 menu generation，SetMenu 的有效替换尝试及当前 owner Deactivate 递增代次，解析和最终执行均检查 owner+generation。重新安装失败而恢复旧菜单也不恢复旧队列执行资格；无效输入菜单在替换开始前拒绝，保持既有有效 owner。

回归验证重激活拒绝旧命令、新命令正常，以及没有独立 RemoveMenusSB 的同菜单重装拒绝旧命令。NativeProbe Release 构建零警告错误，专用菜单消息链与完整颜色策略 smoke 通过。仅使用普通隐藏 HWND、合成 HMENU/OLE descriptor、假 pane 接收命令；没有真实 IShellView、系统菜单跟踪或产品迁移验收。
### 2026-10-27 读取状态快照不改变原生写流位置

`ReadSavedBytes` 原直接移动保存的 SHCreateMemStream 游标到 0 并读取，留下末尾位置。新增真实 IStream 回归：写入 1/2/3，将游标移到 1，读取快照，再写 9；旧行为得到追加内容，修正后为 1/9/3。现在以 64 位位置记录游标，并在 finally 恢复，避免读取快照干扰 Shell 后续写入。

NativeProbe Release build 零警告错误，完整状态流 smoke（包括 session 隔离、读写更新、跨进程重开）及颜色策略 smoke 通过；没有创建 Shell view。当前状态存储是按 session GUID 保存的单流，保存与加载限制 64 KiB；没有在此处新增缓存或改变产品内存策略。真实 Shell SaveViewState/导航交接与 WPF 产品集成仍需单独验收。
### 2026-10-27 宿主关闭后拒绝新状态流

StreamViewHost 的 Dispose 原只释放当前资源而没有退休标记，迟到 GetViewStateStream 仍可创建 native stream，write 模式还会重新持有 `_savedStream`。现清理前标记关闭，重复 Dispose 短路，新的 read/write/readwrite 请求均返回 E_ABORT 与空输出指针，计数不增加。

真实 CCW 的 IShellBrowser slot 12 回归先观察关闭后 read 请求仍成功，修正后覆盖三种模式；完整状态流（含跨进程）与颜色策略 smoke、NativeProbe Release build 通过，构建零警告错误。本次无 Shell view，不据此确认原生 SaveViewState/DestroyViewWindow 的清理顺序；调用方已持有的 IStream 仍按原有 COM 引用计数有效，不被强制撤销。产品 ExplorerBrowser 与 release 未改。
### 2026-10-27 关闭后的活动视图和对象接口

状态流关闭保护之后，继续核对 IShellBrowser QueryActiveShellView 与 IOleInPlaceFrame SetActiveObject：前者原在 Dispose 后仍 AddRef 借用 View，后者可重新持有 COM 对象，甚至 handler 已关闭宿主后外层仍继续持有。现退休时清空借用 View，关闭查询返回 E_ABORT/空输出，设置活动对象在 handler 前后检查退休。

有效假 COM 对象配合 CCW slot 15/slot 8 回归先复现旧查询仍成功，修正后验证关闭拒绝及 handler 关闭后不持有。借用 View 清空不代表释放调用者自己的引用。NativeProbe Release build 零警告错误，完整颜色策略、菜单消息链、状态流 smoke 通过；无真实 Shell view，原生生命周期顺序及 WPF 产品迁移仍需单独验收。
### 2026-10-27 视图状态按打开快照检查保存冲突

新增两宿主同 stateId 回归先复现：二者均打开旧状态，先关闭者保存后，后关闭者会覆盖它。现在宿主创建时只读取一次持久化状态，读流使用这份初始快照，保存则比较同一份基线（缺失以 null 区分空数组）。已有 `_bytes` 与 expected baseline 共享引用，不额外复制已保存的 64 KiB 状态。未读到持久化状态时仍克隆调用方 fallback。

存储沿用应用 PortableIniWriter 的 mutex 路径命名，500 ms 有界等待，持锁核对当前存在性/内容后保留原有临时文件写入/flush/原子替换。回归覆盖旧宿主保存拒绝、活动快照不被另一宿主保存替换、重新打开后可保存、外部创建及删除保持。NativeProbe Release build 零警告错误，完整状态流与颜色策略 smoke 通过；跨进程证据仍是顺序读写，未证明同时竞争锁的行为。非协作外部写入者不受该 mutex 控制；真实 Shell view/冲突 UI/产品接线未验证，产品和 release 不变。
### 2026-10-27 多进程争用状态保存的实测

此前状态冲突检查仅验证同进程宿主与跨进程顺序读写。本批增加独立 race-writer 子命令，创建宿主后通过 ready/go 文件屏障确保两个进程都持有同一初始快照，再并发保存。三轮均一成功、一明确内容冲突；磁盘字节属于成功者。另由父进程持有实际同名路径 mutex，确认子进程已读基线后放行，子进程在 500 ms 写锁等待后失败，原文件不变；释放锁后新子进程正常保存。

完整状态流 smoke 和 NativeProbe Release build 通过，构建零警告错误。进程不创建窗口，runner 15 秒上限且超时递归 Kill，父进程清理前 join 两个 runner。此证据补足协作进程争锁及超时，不覆盖非协作写入者的检查后竞争、异常退出或磁盘故障；无真实 Shell view/产品宿主/GUI 验收，已固定产品候选不变。

### 2026-10-27：关闭后拒绝迟到的拖放注册（后台验证）

- `StreamViewHost.RegisterDropTarget` 现在先检查宿主退休状态。此前 Dispose 已完成撤销且重复 Dispose 短路，但注册入口仍能继续构造 OLE 注册，存在迟到调用新增资源后无人撤销的生命周期缺口。
- 无窗口回归用已 Dispose 的宿主和零句柄验证：修复前进入 HWND 参数检查并抛 ArgumentException；修复后先抛 ObjectDisposedException。未创建 HWND、Shell view 或执行 RegisterDragDrop，不声称已复现真实 OLE 泄漏。
- 完整 `--shell-view-state-stream-smoke` 通过，包含原有 COM stream、跨进程持久化、并发写保护与锁超时检查。日志：`src/.state/drop-retirement-red.log`、`src/.state/drop-retirement-green.log`。
- 仅隔离原型改变；真实导航/关闭重入与拖放交互仍待集中 UI 验收，不归因为产品内存增长或无响应的根因。固定 UI 候选和 temp.76 发布目录未更新。

### 2026-10-27：自定义列尚无数值时的原生契约

无窗口 RunFolderContract 扩展两个真实临时文件，二者不在静态数值表。旧适配层 GetDetailsEx 返回 E_FAIL，红测明确记录 Pending custom GetDetailsEx failed: 0x80004005。修复仅针对自定义列：未知值返回 S_OK/VT_EMPTY，显示文本为空；排序时空值排在已知值前，两个空值沿用底层名称比较，不用零字节伪装未完成结果。用于诊断的标准 PKEY_Size 替换控制保持原行为。

真实 native IShellFolder2 COM 调用验证空值类型、空白文本、空↔已知比较反对称、空值自比较、两个空值与原生名称排序一致，以及原有 2/10/100 和同值比较。另用 SHCIDS_CANONICALONLY 确认 ten.bin 与 ten-copy.bin 虽有相同自定义数值仍是不同原生身份；[微软 CompareIDs 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids)区分列排序规则与 canonical identity 比较，修复未改写后一条委托路径。

C++ /W4 /WX 构建及 --static-shell-column-folder-smoke、--static-shell-column-contract-smoke 通过。原生结果 window-created=False；本次未运行创建视图的 Run/Private Desktop 分支，未接入产品。日志 src/.state/static-missing-{red,green,native-build}.log。静态空值基础不能代替异步目录扫描/完成后重新排序、通知、可见焦点与迁移验收；递归目录大小与颜色 P0 仍未完成。

### 2026-10-28：四窗格 WPF 原生选中项菜单隔离复验

新增背景验证覆盖四个 WPF 托管 `IShellView` 的已选文件 `IContextMenu`：每个 pane 均取得原生菜单并确认包含标准 `copy` verb；同一批也通过每窗格的 Details、排序、分组命令映射与键盘路由。第一次失败时，在选中项菜单阶段前读到四个视图各自已有 2 项选中；这是前序真实 Shell 文件操作留下的状态，不能当成 pane 间选择泄漏。探针现于每个菜单用例前清空所有 pane 的选择，并在退出时清空、核对所有 pane；重新构建后连续 3 轮完整 Private Desktop 流程通过，前台 HWND 未变。

Release NativeProbe 构建 0 警告/错误。该证据只覆盖隐藏私有桌面上的原生 COM 菜单对象和命令查询，不显示菜单、不运行真实菜单跟踪，也不验证产品 UI 或真实鼠标拖放；这些仍留待集中可见验收。

### 2026-10-28：WPF 隔离四窗格的原生新建文件夹命令

在四 pane `IShellView` 的隐藏 Private Desktop 流程末尾，改为调用产品 `ShellFileCommands.Invoke(NewFolder)` 与 `NativeShellCommandMenu`。生产适配器先经 `IContextMenu2::HandleMenuMsg(WM_INITMENUPOPUP)` 初始化延迟的“新建”子菜单，再用当前菜单对象内的命令偏移调用；返回 `S_OK`。此前未初始化子菜单的独立调用偏移返回 `E_FAIL`，直接使用 canonical verb 字符串虽可执行，但不代表产品入口的实际调用路径。

创建出的临时目录在同目录三个 pane 中均可见且计数增加，独立目录 Pane 4 的计数与内容保持不变；若 Shell 打开内联重命名，则由隔离 view 接收 Escape 后结束。当前产品适配器路径连续 3 次通过，前台 HWND 未变，fixture 清理完成。该验证覆盖隐藏原生适配器与通知传播，不等于产品 ExplorerBrowser 的可见菜单/焦点/自动重命名验收，也不覆盖用户真实输入。日志：`20261028-084306-df033e88.stdout.log`、`20261028-084356-2bf5a2ad.stdout.log`、`20261028-084412-57d9c5b7.stdout.log`、`20261028-084429-303fb9d9.stdout.log`。

### 2026-10-28：复验 `IShellView` drop target 时 Private Desktop 前台守卫失败

从当前 `main` 重新运行 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --shell-view-drop-target-private-desktop`。探针在隔离桌面创建真实文件系统 `IShellView`，并通过真实 Shell 选中项 `IDataObject` 直接调用目标 `IDropTarget`：选中项数据对象目标和 `IShellFolder::CreateViewObject` 注册目标均返回 Copy effect，目标临时文件内容正确、源文件保留；`IShellView::GetItemObject` 的 drop-target 查询返回 `E_NOINTERFACE`。这些回调结果仅作诊断，不视为本轮通过，因为外层 watchdog 检测到用户桌面前台 HWND 从 `0x1E71D78` 变为 `0x14712D4`，整轮退出码为 1，`foreground-unchanged=False`。无法归因前台变化来源，不恢复前台、不弱化守卫，也不继续运行会创建 Shell view 的 Private Desktop 探针。本轮没有调用 `DoDragDrop` 或注入鼠标；临时夹具目录已确认清理。真实 OLE 拖放与该隔离条件仍未通过。

同日静态复核探针创建路径时发现一个待验证风险：`ShellViewDropTargetProbe` 将顶层 `STATIC` parent 以 `WS_POPUP | WS_VISIBLE` 创建在已切换到 Private Desktop 的线程上，未使用 `SW_SHOWNOACTIVATE`；微软[`CreateWindow` 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowa)说明带 `WS_VISIBLE` 的创建路径会发送激活和显示所需消息，[`SW_SHOWNOACTIVATE`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow) 则明确显示而不激活。这个窗口路径可能影响隔离性，但现有失败运行的逐阶段 stdout 未保留，不能将其认定为前台变化根因。随后运行无 Shell 视图的 `private-desktop-window-destroy-control`，退出码 0 且外层前台保持不变；但内部 `IsWindowVisible=False`，所以该窗口销毁对照没有覆盖实际可见/激活路径，也不能验证或排除此假设。恢复 Shell-view 探针前，先增加不创建 Shell view 的精确窗口样式对照，分别验证隐藏创建、WS_VISIBLE 创建与显式非激活显示，确认外层 foreground 守卫稳定，再重新评估拖放探针；不移除守卫。

### 2026-10-28 Private Desktop 窗口可见性与前台隔离对照

为区分创建方式与销毁阻塞，增加 `private-desktop-window-visibility-control`，仅使用普通 `STATIC` 窗口，不创建 Shell view：

- 隐藏 `WS_POPUP` 创建后立即销毁成功；窗口不可见，私有桌面活动/前台句柄均为 0。
- `WS_POPUP | WS_VISIBLE` 创建成功且 `IsWindowVisible=True`；该窗口成为私有桌面的活动窗。随后的 `DestroyWindow` 在 10 秒内未返回，外层 watchdog 终止进程；用户桌面前台 HWND 前后相同。WCT 等待链包括 COM activation 与 `ChsIME`，但当前证据不能证明它是根因。
- 隐藏创建后调用 `ShowWindow(SW_SHOWNOACTIVATE)`，窗口可见，私有桌面的活动/前台 HWND 仍为 0，`DestroyWindow` 成功，外层前台守卫通过。

据此，Private Desktop drop probe 的顶层 parent 改为隐藏创建后非激活显示，交互式 OLE 模式仍保留可见创建路径。一次受守卫重跑中，选中项 IDataObject 和注册的文件夹 drop target 均通过直接 Copy，源文件保持；`IShellView.GetItemObject` 查询仍为 `E_NOINTERFACE`，对应 view target Copy 失败。外层前台 HWND 再次从 `0x2632026` 变化到 `0x4F1D0C`，故整轮退出码 1，不算通过。探针夹具已清理，进程退出；没有调用真实 `DoDragDrop` 或输入用户桌面。

非激活显示控制能保持外层前台，但真实 Shell view 探针仍触发守卫失败。由于未能在前台变化发生时采集到精确阶段，不能断言是 Shell view 创建导致，也不能排除外部窗口活动。暂停继续创建 Shell view 的探针；不要恢复前台或移除守卫。日志：`20261028-135833-538ef870.stdout.log`（首次可见创建销毁阻塞）、`20261028-135906-d534142a.stdout.log`（带阶段标记的复验）、`20261028-135944-59c2aa2a.stdout.log`（非激活显示通过）、`20261028-140025-209e28ae.stdout.log`（drop probe 前台守卫失败）。

### 2026-10-28 Private Desktop 前台变化阶段监视

前台守卫现于主线程等待 Private Desktop worker 时每 25 ms 采样一次用户桌面的 `GetForegroundWindow()`。首次发现 HWND 不同，会记录 elapsed time 和当前最近阶段；当前拖放探针在源视图创建、目标视图创建、Copy 查询及清理前更新阶段标记。该监视只记录，不恢复前台、不取消调用，也不放宽最终前台守卫。

NativeProbe Release build 0 警告/错误；随后仅重跑无 Shell-view 的 `SW_SHOWNOACTIVATE` STATIC 窗口控制，退出码 0，stage-at-return 已输出，`foreground-change-observed=False`，前后台 HWND 同为 `0x9901DE`。没有重跑 Shell-view 探针，因此尚无证据证明该监视能将先前前台变化定位到具体阶段。下一次 Shell-view 复验仍需先确认隔离桌面和前台守卫条件；若守卫变化，保留采样日志并判失败，不恢复前台。日志：`20261028-140547-3e07b6e4.stdout.log`。

### 2026-10-29 合成文件夹树合同（无视图）

隔离适配器由单层文件枚举扩为临时 filesystem-backed 文件夹树：根与子层都覆盖 folder/non-folder/hidden 过滤、`ParseDisplayName`、嵌套 `BindToObject` 与根 PIDL 往返，另验证 `BindToStorage(IStream)` 读取及 `SetNameOf` 实际重命名。初次合同失败定位到底层 filesystem `ParseDisplayName` 成功但不写 `eaten`；包装层在合成单项 PIDL 映射成功后填充消费字符数。

原生 `/W4 /WX`、NativeProbe Release 构建与 tree/普通 PIDL/wrapped PIDL smoke 均通过，未创建 Shell view 或窗口。该证据只证明 namespace adapter 的文件夹合同，不包含 DefView、交互或产品兼容；原型仍未接入 Explorer Dirs。

### 2026-10-29 多级 Shell parsing 与 storage/bind 合同

合成 `IShellFolder2` 现在逐段解析相对路径并构造多组件 synthetic PIDL；`BindToObject` 可从根递归绑定到多级子文件夹，`BindToStorage` 可沿多组件 PIDL 绑定深层文件流。合同测试覆盖 `child\nested.txt` 的两段 PIDL、`child\nested-folder` 的组合绑定及深层枚举，以及从根打开深层 `IStream` 并读回内容。单层枚举、隐藏项过滤、嵌套 `GetCurFolder`、单层流与重命名回归保持通过。

NativeProbe Release、原生 `/W4 /WX` 和普通/包装 PIDL smoke 通过。未创建 `IShellView` 或窗口；这些证据仅验证合成 namespace 的方法合同，不代表真实 DefView 导航或产品兼容。

### 2026-10-29 合成项的 Shell 菜单与文件数据对象

无视图的临时树 smoke 现在调用合成文件夹的 `GetUIObjectOf`：查询真实文件的默认 `IContextMenu` 命令，并从 `IDataObject` 读取 `CF_HDROP` 后核对 backing filesystem 路径。查询只建立 HMENU，不展示、不执行；未创建 Shell view 或窗口。树、multi-component PIDL、普通/wrapped PIDL 合同与 Release 原生/托管构建均通过。此证据只证明接口转发和数据对象格式，不证明可见菜单、OLE `DoDragDrop` 或产品接线。

### 2026-10-29 多选 `IDataObject` 文件传递

临时文件树现在包含两个可见文件。合成文件夹一次对两项调用 `GetUIObjectOf(IDataObject)`，再用 `CF_HDROP` 读取路径；合同要求两项路径均存在，不依赖枚举顺序。单项菜单、单/多组件 PIDL、深层绑定与流读取保持通过。它只证明 Shell 数据对象可以携带多选文件路径，不覆盖真实用户选择、剪贴板粘贴或 OLE 拖放；产品仍未接入该适配器。

### 2026-10-29 多选 `IDataObject` 到 IFileOperation 复制

合成树 smoke 将适配器为两个选中项返回的 `IDataObject` 交给 Windows `IFileOperation::CopyItems`，目标为临时 backing tree 内新建目录；执行 `PerformOperations`，检查 `GetAnyOperationsAborted`，并逐项核对目标文件内容及源文件仍存在。操作使用静默/无确认/无错误 UI flags；没有创建 Shell view、窗口或进度 UI。

NativeProbe Release 与原生 `/W4 /WX` 构建、树级/普通 PIDL/包装 PIDL smoke 均通过。该项证明 native Shell 多项数据对象能驱动后台本地复制，不代表产品接线、交互确认 UI、虚拟 provider、网络复制或 OLE 拖放已通过。

### 2026-10-29 多选 `IDataObject` 到 IFileOperation 移动

树级 smoke 对同一合成文件夹的双项 `IDataObject` 执行 `IFileOperation::MoveItems` 到临时目标目录，检查 `PerformOperations`、`GetAnyOperationsAborted`、源文件消失和目标内容一致。使用静默/无确认/无错误 UI flags，无 Shell view 或窗口。此证据覆盖本地同卷后台移动，不涵盖跨卷移动、剪切/粘贴 UI、Shell provider 互操作或产品 view notification。

### 2026-10-29 多选 CopyItems 同名冲突

临时目标预置 `root.txt` 且内容为 `keep-existing`；合成文件夹的双项 `IDataObject` 通过 `CopyItems` 复制，并启用 `FOF_RENAMEONCOLLISION` 及静默 flags。合同确认旧目标字节不变、无冲突文件成功复制、冲突文件以另一个名称落地，并检查中止状态。没有 UI 或 Shell view；这不代表产品冲突确认对话框已验证。

### 2026-10-29 合成多选 IDataObject 驱动 DeleteItems

复制合同成功后，树级 smoke 再从合成 `copy-destination` 文件夹重新枚举两份副本，通过 `GetUIObjectOf` 生成多选 `IDataObject`，调用 `IFileOperation::DeleteItems`、`PerformOperations` 与 `GetAnyOperationsAborted`，最后确认两个目标路径均已消失。删除仅针对本轮 smoke 创建的 fixture 副本；源文件和冲突处理目录不参与此删除。没有创建 Shell view 或窗口，也不覆盖产品视图刷新、撤销回收站行为或交互确认。

### 2026-10-29 目标端 Copy/Move 键效果与跨卷默认行为

Private Desktop `drop-target-private-desktop` 探针现对 `IShellFolder::CreateViewObject` 返回并注册到目标窗格 HWND 的真实 Shell `IDropTarget`，直接调用 DragEnter/DragOver/Drop，并让两个效果都可用。Ctrl 状态（keyState `0x9`）下，所选项 `IDataObject` 返回 Copy=1，目标文件内容正确且源保留。Shift 状态（keyState `0x5`）下，同卷及 C:→D: 跨卷临时文件都被移至目标：目标内容正确、源路径消失。无修饰键（keyState `0x1`）在同卷默认执行 Move；C: 源到 D: 目标的跨卷对照则默认执行 Copy，目标内容正确且源保留。两种同卷优化移动和跨卷 Shift Move 中 `IDropTarget::Drop` 返回 effect=0；数据对象的 `Performed DropEffect` 与 `Logical Performed DropEffect` 均读回 Move=2。五种情景连续两次 Private Desktop watchdog exit 0，前台 HWND 和输入时间戳保持不变，C: 与 D: 临时夹具均清理完成。跨卷情景仅在探针二进制目录与系统临时目录位于不同卷时执行；不满足时日志会标为不可用。

NativeProbe Release build 0 警告/错误。该证据只验证目标端 Shell 回调、修饰键状态与本地临时文件结果；没有进入真实 `DoDragDrop` 循环，也未验证鼠标输入、源端结束协议、跨窗格可见交互或产品接线。不能据此关闭拖放迁移门槛。

### 2026-10-30 无输入 OLE DoDragDrop 自动放置尝试（未通过）

为避免注入鼠标/键盘，临时探针曾尝试让 `IDropSource::QueryContinueDrag` 首次调用即返回 `DRAGDROP_S_DROP`，并在 Private Desktop 内调用真实 OLE `DoDragDrop`。NativeProbe Release 构建成功，但调用停在 `drop-target: query-and-transfer-start`，内部 12 秒 watchdog 返回 124；没有 `DoDragDrop` 返回记录，也没有证据确认目标 `Drop` 回调或文件传输结果。外层守卫同时观察到前台句柄从 LockApp 变为 0、输入时间戳改变，原因未知。探针进程已退出，唯一 GUID 临时夹具已逐项核对并清理；未创建跨卷夹具，也未主动恢复或抢占前台。

该自动化策略不能作为隔离安全的闭环验证方式，临时试验代码已移除，不再重跑。真实 `DoDragDrop` 源端循环及鼠标命中仍未验证；产品继续使用 ExplorerBrowser。后续若要验证，需要采用用户可见且明确授权的交互验收窗口，并由用户控制开始或取消拖动；不得把本次卡住解释为产品缺陷或 Shell 已执行目标 Drop。日志：`src/.state/native-probe-watchdog/20261030-000138-46fb9cf9.stdout.log`。

事后静态对照发现，临时测试源未等待发起鼠标键先进入按下状态；现有隔离源则先观察到发起键按下，再等释放后返回 Drop。微软文档说明 `QueryContinueDrag` 由 OLE 拖放循环在输入状态变化时调用，因此“没有真实按键状态变化，回调未被触发”是一个与卡点相符的假设，但日志没有回调计数或线程栈，无法确认它就是原因。当前可执行的可靠路径仍是已有可见交互探针，由用户在隔离窗口完成拖动；暂不再运行自动释放试验。

### 2026-10-31 `CCM_SETNOTIFYWINDOW` 逐项着色通知路由验证（未通过）

为检验“DefView 原通知路径屏蔽了 `NM_CUSTOMDRAW`”这一新假设，临时 Private Desktop 探针把 `SysListView32` 的通知目标改为隔离 sink，并同步将收到的 `WM_NOTIFY` 转发回原 DefView。微软 `WM_NOTIFY` 文档要求消息发往控件的父窗口；Windows SDK 虽定义 `CCM_SETNOTIFYWINDOW`，但没有提供旧目标返回值或 Shell DefView 组合下的兼容保证，因此探针不把其返回值当作合同。

Release 构建通过（0 警告、0 错误）。Private Desktop 运行期间 sink 收到并同步转发 16 条通知，但图标视图依旧没有 `NM_CUSTOMDRAW`（逐项回调 0，目标行颜色像素 0）；原有逐项颜色门槛失败。该结果说明改路由没有打开新的逐行绘制回调，且这次提前退出没有覆盖转发后所有 Shell 行为，不能据此声称通知转发完整或适合产品使用。临时路由入口和代码已移除；产品 ExplorerBrowser 与正式 `IShellView` 宿主均未修改。相关 API 说明：[WM_NOTIFY](https://learn.microsoft.com/windows/win32/controls/wm-notify)、本机 SDK `CommCtrl.h` 中的 `CCM_SETNOTIFYWINDOW` 定义。

### 2026-11-01 原生 Details 菜单切换后的逐项颜色同步

对真实合并 View 菜单的 Details 命令 `0x704B` 做隔离 A/B：命令后 `IFolderView2` 与 ListView 均报告 Details，但立即同步 `LVM_SETVIEW` 后的下一次像素采样仍没有 `NM_CUSTOMDRAW`。把同步动作延后到原生视图已完成一轮绘制之后，再读取 ListView 当前 `LV_VIEW_*` 并以相同值调用 `LVM_SETVIEW`，后续绘制恢复 2/2 个逐项回调，两条合成颜色规则的目标像素均通过。原型只对 `IShellView` 菜单分派中实际改变模式的命令执行该同步，并记录列表模式和结果；该路由随后在 Details 菜单场景验证 `verified=True`。完整 Private Desktop watchdog exit 0、foreground/input 未变化、隔离清理通过；NativeProbe Release 构建 0 警告/错误。此处的 100 ms 消息泵仅用于隔离原型顺序验证，尚未证明适合产品 UI 消息调度；只测 Details 单项命令，其他模式、排序/分组切换、provider 和 ExplorerBrowser 产品行为仍待验证。日志：`src/.state/native-probe-watchdog/20261101-004841-5e6b6505.stdout.log`。
