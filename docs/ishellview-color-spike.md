# 隔离 IShellView 逐项颜色原型

## 当前授权与迁移阶段门槛

用户已授权继续推进隔离 `IShellView` 宿主迁移原型，验证范围包括原生菜单、Shell 文件操作、拖放、目录通知、键盘焦点和每会话 view-state。授权范围仅限 NativeProbe/隔离宿主，不等于批准替换产品 ExplorerBrowser 或发布迁移版本；产品文件视图保持现状，直到原型门槛有完整证据并单独评审。

已有的隐藏 COM smoke、局部文件操作/通知样本和跨进程 view-state 文件测试来自不同路径，不能合并宣称完整迁移通过。尚缺同一宿主内的稳定逐项颜色像素、真实 OLE 拖放闭环、物理键盘焦点，以及由真实 `IShellView` 写入后销毁、重建并恢复会话状态的验证。当前继续不使用 Computer Use；遵循项目桌面规则，只做源码和不创建 Shell view 的验证，直到具备独立 Windows 会话条件。

## 2026-10-26：读取 Q-Dir Filter2 原始规则行

NativeProbe 增加 `QdrFilter2RawReader`，只处理 `[Filter2]` 中的数字键，按键序号排序，并保留模式字符串及其后六个原始字段。这个结构依据本机可读 `Q-Dir.ini` 的 12 行样本；字段含义未确认的值不映射到颜色或样式开关。编号规则若字段数不符或序号重复，解析整体失败且不返回部分规则。

颜色策略 smoke 覆盖样本中的 `#Hidden`、通配规则和 `#BG` 原始行，以及缺字段拒绝。该读取器仅供隔离原型使用，未接入产品 `.qdr` 导入器；不会导入 Q-Dir 颜色、也不证明 `IShellView` 可见绘制或迁移兼容性。产品当前仍对 `[Filter2]` 显示“未导入”警告。

复核 [Q-Dir FAQ 47](https://softwareok.com/?faq=47&seite=faq-Q-DIR) 与 [FAQ 6](https://www.softwareok.com/?faq=6&seite=faq-Q-DIR)：公开说明确认 `#BG`/`#BG-A` 背景规则、`#` 隐藏项、`#DIR` 文件夹、`#RO` 只读项，以及属性值 `-1` 允许后续匹配规则设置，`0` 表示不设置且锁定后续规则，`1` 表示设置并锁定后续规则。公开页面未说明 `[Filter2]` 行中这六个值各自对应的属性列；本机 INI 样本只能证明行形状。因此映射前仍须获得可公开验证的字段顺序证据，不能按样本猜列含义。

## 2026-10-26：为隔离 Shell 宿主插入标准 OLE 菜单组

`StreamViewHost.InsertMenusSB` 的默认宿主路径现创建 File、Edit、View、Favorites、Tools、Help 六个原生弹出菜单，并使用 Shell 标准菜单 ID；`OLEMENUGROUPWIDTHS` 的宿主数量为 `[2,0,3,0,1,0]`。`RemoveMenusSB` 与 `Dispose` 只移除宿主创建的菜单组和子菜单；外层显式回调仍优先接管。菜单标题暂为固定英文原型标签，未连接 WPF 产品菜单或本地化资源。

`--shell-view-color-policy-smoke` 先在隐藏 HWND 上经实际 COM vtable 验证菜单项、ID、分组宽度、移除/释放及重复合并；另在同一 HMENU 保留一个外部 popup，确认宿主卸载不会删除非自身菜单。OLE composite 安装也已用真正的 `IOleInPlaceActiveObject` COM 接口夹具，不再使用空活动对象。没有创建 `IShellView`，Shell 尚未向这些组添加自己的命令项；因此不证明 Shell 菜单项合并、OLE 命令消息触发、活动对象消息循环或可见交互。依据：[Microsoft Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview) 与 [OleSetMenuDescriptor](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-olesetmenudescriptor)。

进一步尝试在隐藏 HWND 上直接合成 `WM_INITMENU`、`WM_INITMENUPOPUP`、`WM_MENUSELECT` 与 `WM_COMMAND`。消息到达 frame，但没有转发到 active-object 窗口；这类直接 `SendMessage` 没有经过系统实际菜单跟踪流程，不能据此判定真实 OLE 菜单路由失败，也不能当作成功证据。移除此无效断言；真实用户菜单选择仍须在不共享桌面的隔离会话验证。

## 2026-10-26：为隔离 Shell 宿主提供 IOleInPlaceFrame

`StreamViewHost` 现在也暴露 `IOleInPlaceFrame` IID。其菜单插入/安装/移除、状态文字、模态状态与 accelerator 方法复用 `IShellBrowser` 已验证的父级路由；`SetActiveObject` 走独立回调，父级接受后 host 保留一个 COM 引用并在替换/Dispose 时释放。边框空间的三个方法保留为 `E_NOTIMPL`，异常回调转成 `E_FAIL`。

NativeProbe 的真实 CCW vtable smoke 覆盖该接口的继承槽布局、回调参数与 HRESULT、异常边界、边框拒绝及 Dispose 后断开。根据[微软 IOleInPlaceFrame 契约](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nn-oleidl-ioleinplaceframe)，这个接口提供容器框架菜单/状态/快捷键入口；当前还没有 Shell `UIActivate` 实际调用、共享 HMENU 生命周期或 `OleSetMenuDescriptor` 证据，因此这不代表主菜单已合并、安装或能正确分派命令。

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
