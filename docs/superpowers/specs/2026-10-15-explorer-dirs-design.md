# Explorer Dirs 设计草案

日期：2026-10-15

## 意图与成功标准

用户需要一个开源、可持续修改的 Windows 文件浏览窗口替代品。Q-Dir 式多布局和多标签必须是核心能力；应能在崩溃后恢复工作上下文，且用户/机器环境变量改变后无需重启即可让新启动的命令行获得更新。最终应能逐步接管 Win+E 和文件夹打开，但不接管桌面、任务栏或登录 Shell。

## 路线选择

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
- `SessionStore` 将全局布局、默认新标签查看方式和各标签路径、查看模式、图标大小、排序列及分组写入便携 INI，使用临时文件加覆盖移动避免半写入。发布版各版本共享项目根目录 `profile/ExplorerDirs.ini`；复制单个发布目录时使用其自身 `profile/`；Debug 写入 `src/.state/ExplorerDirs.ini`。首次启动从旧 JSON 单向迁移布局和路径，保留旧文件。加载时校验标签数量与区段完整性；损坏时先备份原文件，再恢复可解析标签并在窗口状态栏提示。无法备份或遇到高版本格式时禁止覆盖原文件；即使重复的 `FormatVersion=1` 跟在较新版本号后面，也保留高版本保护。Shell 视图使用 `EBO_NOPERSISTVIEWSTATE`，不再调用 `SetPropertyBag` 保存自身状态到注册表。
- `CrashLog` 在配置目录下写托管异常 JSON 报告，只记录异常类型、HRESULT 和不含源码路径的调用方法；不序列化异常消息、`Data`、当前文件路径或环境变量。同一异常对象只写一次，日志写入失败不能掩盖原异常。原生 Shell 扩展故障仍需独立的进程隔离或 Windows 故障报告方案。
- 独立只读脚本 `scripts/crash-summary.ps1` 用 `Get-WinEvent` 查询最近的 Windows `Application Error` 1000 事件，可选择 Explorer Dirs 或 Q-Dir。解析 XML 中的应用名、版本、模块名、异常码、程序路径和进程身份；本项目只以路径判断“项目内发布版 / 源码测试版 / 其他副本”，Q-Dir 仅标记为对标程序。报告分别给出事件数与进程 ID 加创建时间去重的进程数；缺少完整身份时不猜测合并。输出不保留路径或进程身份。`abrupt-*.json` 标记只属于本项目且另行计数，不能视为已证实的崩溃；模块名也不能直接证明 Shell 扩展致因。[微软 Get-WinEvent 示例](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-winevent?view=powershell-5.1)、[应用崩溃事件说明](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-crashing-behavior)。
- 当前 ExplorerBrowser 宿主仍与 Shell 扩展同进程，原生崩溃无法仅靠 `CrashLog` 捕获。[可靠性隔离与恢复设计](../../reliability-isolation-design.md)按“真实窗口基线与转储 → 有界整窗恢复原型 → 必要时的窗格进程隔离原型”安排验证；不把 WER 已加载模块清单当作致因证据。当前无转储且 GUI 未验收，未启用自动重启或改变 Shell 宿主架构。
- `scripts/wer-dumps.ps1` 将 WER 采集限制在指定程序的 `LocalDumps\<exe>` 键，默认 `Status`/`Preview` 只读；真正启用需管理员和显式诊断确认。配置 `REG_EXPAND_SZ` 的用户 LocalAppData 目录、`DumpType=1` 小型转储与 `DumpCount=2`；所有权标记同时绑定目录和配置值，外部修改后拒绝删除，停用不清理转储。隔离 HKCU 根路径回归通过；未修改真实 HKLM。此项仅是 Windows 诊断设置，和应用便携 INI 无关。[微软 WER 参数文档](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps)。
- 只读 `scripts/perf-sample.ps1` 指定现有 PID 后调用 `Process.Refresh()` 周期采样 `WorkingSet64`、`PrivateMemorySize64`、累计 CPU 时间、句柄和线程；附可执行文件 SHA-256 便于比较构建，不输出路径或命令行。PID/进程名核对阻止误采其他程序，进程退出时保留已采样片段。CPU 百分比按相邻采样的 CPU 时间差、实际墙钟时间及逻辑处理器数计算。[微软工作集属性说明](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64?view=net-10.0)、[私有内存属性说明](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.privatememorysize64?view=net-10.0)。隔离 GUI 冒烟另用秒表记录测试进程启动至窗口和首次文件项、UIA 按钮调用至上级/后退后的文件项，以及搜索提交至状态和结果项；仅在用户留出的空闲桌面显式启动，输出不含浏览路径的 `performance.json`。这些数据包含 UIA 轮询开销，属于单次探索性测量，不能代替发布版长期曲线。当前仅控制台进程回归通过，真实应用的长期曲线与导航响应仍待测量。
- `RunMarkerTracker` 在同一 `crash-logs/` 目录为每个实例原子创建独立运行标记；正常退出先将其改名为 `finished-*` 再删除，以免删除失败导致下次误报。下次启动读取 `run-*` 中的进程号与启动时间，只有确认对应进程已结束才改名为 `abrupt-*` 并显示未正常退出提示；其他存活实例的标记保持不动，损坏标记原样保留。文件仅含 PID、启动时间与版本，不能确定是崩溃、强制结束还是某个 Shell 扩展造成。真实原生崩溃后的行为待隔离 GUI 验收。
- `FavoritesStore` 在同一配置目录单独保存 `Favorites.ini`，使已有预览版不会在写会话时移除收藏夹。顶部菜单为当前活动窗格添加、移除及打开收藏目录；收藏列表去重、最多 200 项。文件损坏时先备份并恢复可读条目，高版本格式与其他实例的修改不能被旧状态覆盖。同版本收藏夹写入也通过 `PortableIniWriter` 的路径锁串行化。菜单打开目录后仅在本窗口和原窗格仍活动时恢复文件视图焦点；真实窗口交互待验收。
- `UiPreferencesStore` 在同一配置目录单独保存 `UiPreferences.ini`，避免旧预览版保存会话时清除外观选项；沿用 `PortableIniWriter` 的外部修改检测和进程间写入保护。全局紧凑开关位于顶部“视图”菜单，默认标准间距；切换仅改变 WPF 工具栏、窗格边距、导航按钮、标签标题和状态栏的尺寸，不重新创建 ExplorerBrowser，也不增减 Tab 焦点停靠点。配置格式与损坏备份可在控制台验证，四窗格的真实可读性、焦点和滚动仍需 GUI 验收。
- `PinnedTabsStore` 在同一配置目录单独保存 `PinnedTabs.ini`，避免旧预览版重写会话时删除固定标记。每项记录窗格、显示顺序位置和路径；加载时仅匹配这三者一致的标签，旧版改变会话后不把固定状态误套到其他路径。固定标签排在窗格左侧，复制时继承固定状态；中键、双击、Ctrl+W 和“关闭其他标签”跳过固定标签，需先取消固定。拖动仅在固定组或普通组内重排。会话成功保存后才写固定状态；文件损坏先备份，高版本或外部修改拒绝覆盖。真实窗口交互和重启恢复待 GUI 验收。
- `App.OnStartup` 解析一个目录参数；`StartupDirectory` 仅用 `Path.GetFullPath` 规范化路径，不同步探测目录是否可达。`FolderOpenRouter` 以配置绝对路径、用户身份和权限级别派生 v1 命名管道名，使用 `PipeOptions.CurrentUserOnly` 和每配置锁文件限定接收进程；请求有大小与绝对路径校验，接收窗口在第一窗格新增标签后才回执。启动进程收到成功回执即退出；服务不可用或未确认时保留目录参数并独立打开，状态栏提示可能的保存冲突。无参数启动仍可创建另一个窗口，依靠会话写入冲突保护。多个参数、选项样式或非法路径只恢复原会话并给出提示。命名管道的同用户及 Windows 权限级别限制见[微软 PipeOptions 文档](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions)；真实窗口激活、标签创建与离线 UNC 行为待 GUI 验收。此流程不改变 Win+E 或文件关联。
- `scripts/folder-verb.ps1` 作为第一阶段系统入口，仅在每用户 `HKCU\Software\Classes\Directory\shell\ExplorerDirs.Open` 和 `Drive\shell\ExplorerDirs.Open` 建立非默认菜单项，命令格式为 `"<发布版 exe>" "%1"`。`Status`/`Preview` 不写注册表；启用需发布版同目录能力清单中的 exe 哈希匹配、操作者提供该哈希并显式确认已验收；现有 `preview-0.5.2` 因无能力清单不能启用。卸载前逐项验证所有权标记、菜单值和命令，若被外部改写则拒绝删除。隔离 `HKCU\Software\ExplorerDirs\Smoke\<GUID>` 下已验证启用、重复执行、冲突保护和卸载；真实 Shell 菜单与目录参数启动待 GUI 验收。此阶段不更改 `open` 默认动词或 Win+E。[微软的每用户类注册表说明](https://learn.microsoft.com/en-us/windows/win32/sysinfo/hkey-classes-root-key)、[Shell 菜单动词及 `%1` 引号要求](https://learn.microsoft.com/en-us/windows/win32/shell/context)。
- `SessionStore` 读取时保留原 INI 内容；`PortableIniWriter` 以配置绝对路径派生的进程间 Mutex 串行化同版本会话与收藏夹写入，锁内再次读取并比较；内容变动则拒绝覆盖并由窗口状态栏提示重启。临时文件名含随机标识，写入成功后才更新内存中的已见内容。旧发布版未参与此 Mutex，跨版本同时运行的最后时刻竞争仍需通过发布策略和实机验收约束；控制台回归覆盖外部编辑、两个新实例同时从空会话配置写入及收藏夹被持锁时拒写。
- `EnvironmentRefresh` 只读 Windows 用户与机器环境注册表，以 `DoNotExpandEnvironmentNames` 取得 `REG_EXPAND_SZ` 原始值，避免旧进程环境参与展开；再用新合并快照解析引用，机器与用户 PATH 按顺序合并，并从本进程移除运行期间曾观察到、现已删除的持久化变量。`REG_SZ` 保持字面值；进程独有变量仍可作为引用回退。官方行为依据：[RegistryKey.GetValue](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registrykey.getvalue?view=net-10.0)、[GetEnvironmentVariables](https://learn.microsoft.com/en-us/dotnet/api/system.environment.getenvironmentvariables?view=net-10.0)。
- 标签恢复不以 `Directory.Exists` 结果替换保存路径；该 API 在无权限、磁盘故障等情况下也会返回 `false`。[目录存在性说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.exists?view=net-10.0)。导航前的后台预检现在用 `File.GetAttributes` 区分真实目录与普通文件，权限异常则让 Shell 自行处理；UNC 共享根目录遇到属性获取失败时才用 `Directory.Exists` 兜底。[属性 API 与异常说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.getattributes)、[UNC 共享根目录的属性限制](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileattributesw)。代际序号使过期结果失效；Shell 导航回调按目标路径区分本程序请求与用户在文件视图中的导航，因为首次导航回调同步、后续回调可异步。[WPF 后台工作](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)、[ExplorerBrowser 回调顺序](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowserevents)。Shell 路径解析与实际绑定仍在 UI 线程，需要另行测量。
- 每个 `PaneControl` 的标签标题用单行 `ItemsPresenter` 与水平 `ScrollViewer` 承载，选中标题请求滚入可见范围；左右滚动按钮和标题上的鼠标滚轮只改变标签栏位置，按钮不占据现有 Tab / Shift+Tab 焦点顺序。[WPF TabControl 模板说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/tabcontrol)。`TabControl.SelectedContent` 只承载当前内容，可能在切换标签时卸载旧 `HwndHost`；现改为标题与内容分离，`PersistentTabHost` 把已访问 ExplorerBrowser 留在同一可视树，只切换 `Visibility`。主窗口的 `PaneLayoutHost` 也让四个窗格始终附着，仅隐藏当前布局不用的窗格。[WPF HwndHost 源码](https://source.dot.net/presentationframework/System/Windows/Interop/HwndHost.cs.html)显示可见性变化会隐藏/显示托管 HWND，来源变化会触发重建或重新挂接。无窗口回归覆盖可视树对象保留；原生句柄和 TravelLog 待 GUI 验收。
- 标签拖动使用 WPF 的 `DoDragDrop` 和仅在本窗格有效的临时令牌；Drop 按标题左右半区决定插入前后。[WPF 拖放机制](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)。`TabHeaderPanel` 只重排标题的布局坐标，不从 `TabControl.Items` 删除或插入选中标签；`PersistentTabHost` 独立保持 Shell 内容。`PaneControl` 单独维护显示顺序，用于 Ctrl+Tab、标题方向键和会话快照；关闭选中标签时改选显示顺序中的相邻标签。排版与排序边界可无窗口测试，Shell 生命周期及真实拖放仍需 GUI 验收。
- 地址区域默认展示可点击面包屑，编辑时切回原 `TextBox`；Alt+D、Ctrl+L、F4、Tab 与右侧按钮均能进入完整路径输入，Esc 取消。F4 按微软当前 File Explorer 专用快捷键条目处理为选中地址栏；同页的通用快捷键表另列“地址列表”。`BreadcrumbPath` 用 [Path.IsPathFullyQualified](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.ispathfullyqualified) 与 [Path.GetPathRoot](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getpathroot) 拆分驱动器和 UNC 分享根目录，不在展示阶段探测目录存在性；相对或无法解析的路径保留为一个整体段。点击段只导航当前标签。导航成功后更新面包屑，失败时保留当前路径并在状态栏显示原因；只有当前窗格仍活动时才恢复文件视图焦点。路径拆分可在控制台验证，WPF 鼠标与焦点行为待 GUI 验收。
- 当前目录搜索使用 Windows 原生 `search-ms:` 范围查询位置，继续由 Search Folder 承载 Shell 结果视图；URL 参数分别编码，非文件系统 PIDL 不进入普通路径解析。`ExplorerBrowserHost` 在初始化前设置 `ShellBrowserSite`，提供 `ICommDlgBrowser2` 的 `CDB2GVF_NOINCLUDEITEM`；`ShellNavigationState` 分别保存目录路径与运行时搜索位置，导航完成时识别搜索 PIDL。每窗格搜索栏接受 F3/Ctrl+E/Ctrl+F、顶部菜单或工具栏，Enter 或窗格搜索按钮导航结果，Esc 返回目录；可见搜索控件加入正反向 Tab 顺序。会话只保存范围目录，不保存搜索查询。Shell 视图是否取用该站点、结果右键与文件操作、历史和焦点仍待真实窗口验证。依据、风险与验收见 [搜索设计调查](../../search-research.md)。
- `EnvironmentRefresh` 合并 Windows 机器和用户持久化变量（PATH 按机器后用户拼接），收到 `WM_SETTINGCHANGE` 的 `Environment` 通知或手动触发时刷新本进程。直接创建的 `cmd.exe` 继承更新值。已有进程和通过文件关联启动的程序不在此承诺内。
- `TerminalLauncher` 通过后台任务启动系统目录下的 `cmd.exe`，工作目录取点击时的活动窗格路径，不在 UI 线程预先调用 `Directory.Exists`；窗口关闭时取消尚未启动的任务。失败时只在本窗口仍活动时显示对话框，否则留在状态栏，避免延迟错误抢占其他应用。`UseShellExecute=false` 下相对程序名依赖 PATH 的依据见[微软 ProcessStartInfo 说明](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-diagnostics-processstartinfo-useshellexecute)，后台处理阻塞工作的依据见[WPF 线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)。真实网络工作目录、命令行环境继承和关闭窗口竞态仍待 GUI 验收。

## 后续文件操作原则

Shell 视图已提供系统文件操作菜单与键盘行为。先验收复制、移动、删除、重命名、撤销和回收站语义，再决定是否为跨窗格操作单独调用 Windows [IFileOperation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)。其 COM 调用需要 STA。任何破坏性操作须确认目标与结果，失败时不得宣称成功。

## 可靠性与兼容性

- 布局切换不销毁隐藏窗格；目录慢或不可读时由 Shell 视图处理，真实网络盘和无权限目录仍需人工验收。
- 每次路径/标签/布局变化延迟保存会话，另每 3 秒轮询原生视图变化；异常关闭后应恢复最近一次已保存状态。复制标签先抓取原生视图的实时设置。
- INI 格式带版本号，并支持旧 JSON 单向迁移；损坏配置备份与恢复仍待实现。
- 第一阶段只支持普通路径；网络盘、Shell 虚拟目录、长路径和重解析点需要明确验收样例。
- 系统入口接管须提供退出与恢复方法，并在独立里程碑中测试；不修改 `explorer.exe` 或登录 Shell。
- 可使用构建发布到 `releases/<version>/`，每个目录不可覆盖；发布脚本使用唯一暂存目录和拒绝覆盖的同卷目录移动，目标目录若并发出现则失败并保留暂存文件。`src/` 中的二进制只供开发和调试。原生 Shell 菜单及第三方扩展在进程内执行，异常隔离和菜单消息的人工验收仍待完成。

当前多标签补充：Ctrl+1～9 在当前窗格按标题显示顺序选中对应标签；超出标签数时保持原选择。该映射与 Ctrl+Shift+1～9 分开，后者仍交由原生 Shell 视图处理查看样式。标题拖动重排后的数字位置和真实焦点需 GUI 验收。

周期会话保存从每 3 秒查询所有标签的 Shell 视图，调整为只查询当前可见标签；切换标签和布局前捕获即将隐藏的视图，隐藏后从每标签缓存序列化配置。导航完成后的实时视图不可用、Shell 读取查看/排序/分组接口返回失败码、排序列数量无效或列数据不完整时，保留上次完整缓存并停止写入会话 INI，直到该标签再次完整捕获；复制标签也拒绝使用失败捕获的缓存。视图捕获判断从 COM 宿主中拆出，模拟读取源的无窗口回归覆盖失败与恢复；真实 COM 取值仍待 GUI 验收。关闭标签时仍尽力捕获其视图供“恢复关闭的标签”使用。此优化的卡顿改善、视图设置保存精度和原生视图生命周期仍需真实窗口验收。

## 首版验收

1. 单/双/四窗格切换及每窗格标签导航可用，重启后路径和布局恢复。
2. 改变持久化用户环境变量后，应用收到系统通知或手动刷新；新打开的内置命令行看到新值，无需重启应用。
3. 无权限目录、已删除目录和慢目录不导致整个窗口崩溃或长时间无响应。
4. 后续在文件操作完整、恢复路径验证、异常遥测和回归测试就绪后，才评估 Win+E/文件夹关联接管。
