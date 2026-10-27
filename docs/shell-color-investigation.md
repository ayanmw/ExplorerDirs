# 文件区导入后颜色变化调查

2026-10-20。用户报告：导入后只有文件列表区域变黑，重启后恢复白色。
状态：根因未确认，未实施颜色修复，不能据本记录宣称已复现或解决。

## 已核对的数据路径

- `QdrImporter.Parse` 从配置构造 `TabSettings` / `ViewState`，不导入颜色。
- `QdrViewSettings.TryRead` 通过 Windows 属性存储 API 读取已识别的 Mode、
  IconSize、GroupView、GroupByKey、GroupByDirection、Sort、ColInfo，
  再构造受限的 `ViewState`。不把原始 Q-Dir 视图 blob 传给 ExplorerBrowser。
- `SessionStore` 的视图模型只包含上述受支持的查看、排序、分组、列状态，
  没有背景色或主题字段。
- `ExplorerBrowserHost.ApplyViewState` 恢复模式/图标大小、列、排序和分组。
  当前 App 源码没有 SetWindowTheme、DWM 主题设置或全局深色模式切换调用。

### 2026-10-24 导入应用链源码复核

`MainWindow.ImportQdrAsync` 在确认后为当前会话制作备份，再由 `PaneControl.ImportTabs`
为导入标签创建新的 `ExplorerBrowserHost`，传入解析后的 `ViewState`。Shell 导航完成后，
宿主异步调用 `ApplyViewState`，只恢复查看模式/图标大小、显示列、排序和分组。
`QdrImporter.Parse` 还显式把颜色列为不导入项。沿此应用链未发现把 Q-Dir 颜色写入
ExplorerBrowser、设置 DirectUI 背景或更改 Windows 主题的代码。

因此源代码排除了“导入配置直接把颜色值写进文件视图”的已知路径，但不能解释用户实际看到的黑底。
视图创建/切换顺序、DirectUI 重绘或 Shell/系统主题状态仍需可见颜色证据；隐藏窗口类列表和
`SysListView32` 颜色消息不能测量 DirectUI 最终绘制颜色。没有更改产品颜色或 Shell 主题行为。

上述事实排除了“当前导入器直接应用 Q-Dir 颜色字段”这一路径；不能排除
Shell 内部状态、对话框/扩展加载、视图创建顺序或绘制生命周期的影响。

## 隐藏宿主探测

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --color-capability
```

使用隔离文件夹及隐藏原生宿主，记录创建真实原生菜单前、菜单释放后、
再导航创建新视图后的子窗口类。只枚举该宿主的后代窗口，没有截图、
显示窗口或输入；不调用文件操作命令，不访问日常 profile。

本机三个阶段均为：

```text
CtrlNotifySink:2,DirectUIHWND:1,ExplorerBrowserControl:1,ScrollBar:2,SHELLDLL_DefView:1
listview-colors=unavailable (no SysListView32; no rendered-color conclusion)
parent-visible=False; foreground-unchanged=True
```

当前视图没有 SysListView32。探测代码仅对真正的 SysListView32 查询
LVM_GETBKCOLOR / LVM_GETTEXTCOLOR / LVM_GETTEXTBKCOLOR；其他窗口类不支持
这些消息，不能把其默认返回值 0 解释为黑色。COLORREF 也不等同于最终绘制截图。
微软的 [ListView 消息说明](https://learn.microsoft.com/en-us/windows/win32/controls/list-view-control-reference)
定义了这些消息适用的控件范围。

此前菜单探测输出过 `dark mode 1`，本轮只读 `QueryFileCommands` 状态探针再次观察到一次。
仓库源码没有对应文本输出；来源及其对进程的影响仍未确定，不能据此归因于某个扩展，
更不能将其当作黑底复现证据。隐藏探测不提供 DirectUI 实际绘制颜色。

## Q-Dir 颜色能力的静态二进制线索（2026-10-24）

本机 `C:\SoftwareOK\Q-Dir\Q-Dir.exe` 的文件版本/产品版本均为 `12.79.0.0`，
SHA-256 为 `9ADAA5994A476F06F0DC9CDF5EAE9CC0986CBFC43055FB9F05341F803FB21FCA`。
只读 ASCII/UTF-16 字符串扫描找到 `color.ini`、`;Q-Dir Colors`、`;Q-Dir Farben`、
`Fav_Colors`、`useColor=%d`、`useTreeColor=%d`、`#BG-A`、`GetBkColor`、
`SetBkColor`、`SetTextColor`、`GetThemeColor` 等常量/导入名称。
对同一文件运行 `objdump -p` 的 PE 导入表还列出 `SetWindowLongPtrW`、`CallWindowProcW`、
`GetDC`、`ReleaseDC`、`FillRect`、`DrawTextW`、`SetBkColor`、`SetTextColor` 等 Win32 API。
`objdump -d -M intel` 的局部反汇编在 `0x401054` 显示：分支比较第三参数是否为 `0x133`，
匹配后对第四参数调用 `SetBkColor(..., 0x0000ff)` 与 `SetTextColor(..., 0x00ffffff)`，随后继续一条回调路径。
Win32 中 `0x0133` 是 [`WM_CTLCOLOREDIT`](https://learn.microsoft.com/en-us/windows/win32/controls/wm-ctlcoloredit)，
该消息用于编辑控件绘制前设置颜色；不过没有符号或调用者证据确认这个函数的参数语义，
因此这里只能记录“数值相同”的线索，不能断言该代码处理编辑框或文件列表。

这些字符串与导入支持“程序存在独立颜色配置及视图/树颜色开关，并包含 Win32 绘制/窗口子类化能力”的静态线索，
与官方 FAQ 描述相符；但 PE 导入表与一段无符号反汇编都不能把这些 API 绑定到文件视图，不能确认哪些规则应用于文件行、
颜色配置的具体语法或 API 调用顺序，也不能把 `SetTextColor` 等导入认定为 ExplorerBrowser `DirectUIHWND` 的着色实现。
REA 已打开该 PE 并确认其为 x64；当前主机没有可用的 PE 深度分析提供方（Ghidra、IDA、Kuna
未配置，Hopper 不支持 Windows），因此没有符号、函数图或调用者证据。未运行 Q-Dir，也未提取或复用其代码。

现阶段仍以官方 FAQ 的外部功能描述作为颜色规则清单；黑底问题需等独占桌面实验取得真实像素证据，
颜色高亮实现则要先证明与原生 ExplorerBrowser 文件项绘制路径相容。静态常量不能替代这两项验证。

## 2026-10-25 本机活动颜色配置与 `.qdr` 收藏分离

只读检查本机 Q-Dir 12.79 的 `Q-Dir.ini`，在 `[Filter2]` 中看到 12 条活动配置，例如隐藏项、扩展名组合、`#DIR`、`#BG` 和 `#BG-A`，其原始值形如 `3=#DIR=1=008800=-1=1=-1=0`。这补充了前述 PE 字符串证据：本机确有持久化颜色规则数据，但目前没有各数字字段名称的权威定义，因此不把其颜色/样式列序解释为已解码格式。

同一安装目录的 `start.qdr` 与一个收藏 `.qdr` 只含 `colorthema=0`，没有规则记录。官方 [FAQ 47](https://www.softwareok.com/?faq=47&seite=faq-Q-DIR) 说明从 Q-Dir 7.21 起，需启用相应选项后颜色设置才会保存到收藏；[FAQ 6](https://www.softwareok.com/?faq=6&seite=faq-Q-DIR) 确认规则可以使用多扩展名组合、`#` 隐藏项和 `#DIR` 文件夹标记。这说明全局 `Q-Dir.ini` 与收藏 `.qdr` 是不同样本来源，不能把当前 `Filter2` 行直接当作 `.qdr` 编码契约。要新增安全导入，仍需取得启用颜色保存的正向 `.qdr` 样本并逐字段验证。

## 2026-10-24 原生 Shell 行着色扩展点复核

查阅 Microsoft 的 [`IExplorerBrowser` 架构说明](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)：浏览器通过当前位置对应的 `IShellFolder` 询问要创建的视图，通常由数据源使用默认 `DefView`；随后由该视图向数据源枚举条目并读取属性与菜单信息。文档也说明，命名空间扩展可以通过 `IShellFolder::CreateViewObject` 提供自定义 `IShellView`。[`IShellView` 接口](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellview)承担创建/销毁视图窗口、项目选择、快捷键以及与外框菜单和工具栏合并等职责。

因此官方文档可确认的自定义绘制路线属于“为 Shell 数据源提供另一种完整视图”，不是在现存 ExplorerBrowser `DefView` 上注册一个逐行背景色回调。Microsoft 的[图标覆盖处理器](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-implement-icon-overlay-handlers)只为条目图标提供叠加图像，也不能表达 Q-Dir 的整行前景/背景颜色。

从这些接口职责推断，自定义 `IShellView` 或包装文件系统的命名空间扩展可能涉及完整的枚举、选择、排序、拖放、原生菜单与文件操作语义；官方页面没有承诺它可透明替换当前文件夹默认视图。当前产品要求保留 ExplorerBrowser 原生文件视图，因此本项目不以更换整个视图或图标覆盖作为轻量着色修复。下一步仍是先用真实窗口实验确定导入后黑底的来源；Q-Dir 规则行着色另作架构决策，须先证明原生交互不会退化，再进入实现。

## 2026-10-25 Microsoft Shell 接口范围补查

复核 Microsoft Learn 的 [Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)、
[IExplorerBrowser](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)、
[IShellFolderView](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-ishellfolderview) 与
[NSTCCUSTOMDRAW](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/ns-shobjidl-nstccustomdraw)。
官方说明确认 ExplorerBrowser 通常询问当前 Shell 文件夹创建 DefView，并可通过 `GetCurrentView` 查询返回对象是否为 `IID_CDefView`。
`IShellFolderView` 自 Windows 7 起不再可用；其文档说明该接口由旧版系统文件夹视图支持，且直接操纵该视图中的 ListView。
公开接口文档没有给出在 ExplorerBrowser 当前 DefView 上为每个文件行指定前景/背景色的回调。

若自建 `IShellView`，文件夹视图对象要创建并维护自己的内容窗口、处理与 Explorer 的通信、合并文件夹命令菜单/工具栏，并管理菜单、拖放等用户交互；官方还提醒有些视图特性无法由自定义视图实现，通常建议继续使用默认系统视图。
`NSTCCUSTOMDRAW` 是 `INameSpaceTreeControlCustomDraw` 的树项目绘制数据结构，作用于目录树，不能证明文件列表行可被自绘。
据此，当前文档中“ExplorerBrowser 默认视图无已确认逐行着色扩展点”的结论得到更强官方依据；这仍是对公开接口文档范围的结论，不等于证明所有未文档化 DefView 消息或第三方扩展均不可行。

下一步仍将两件事分开：先取得可见实验确认导入后暂时黑底的实际像素和触发序列；Q-Dir 按规则逐行着色则需单独评估完整自定义视图的成本与交互兼容，不能以过期的 `IShellFolderView` 或目录树自绘接口作为捷径。没有更改文件视图实现。

## 获得桌面独占权后的最小区分实验

每组使用新的隔离 App 进程和相同测试目录，记录文件区截图、创建顺序、
操作前后及重启后结果；不更改系统主题、不禁用用户扩展：

1. 冷启动 → 新标签，不打开导入对话框：确定基线及同进程新视图行为。
2. 冷启动 → 打开导入对话框 → 取消 → 新标签：区分对话框/扩展加载影响。
3. 冷启动 → 导入只有目录和显式查看方式的隔离 `.qdr`：
   排除完整 ColsAndSort 数据的必要性。
4. 冷启动 → 导入包含已验证视图属性的 `.qdr`：对比排序/分组/列恢复路径。
5. 各组再执行切标签、换目录、调整窗口尺寸与重启：
   区分已存在视图、新建视图和重绘行为。

先确定能够稳定改变颜色的最短步骤，再增加针对该步骤的诊断或修复。
不以强制白底、修改用户系统主题或未验证的私有深色 API 掩盖原因。
主题个性化仍遵循功能 → 组件体验 → 主题的项目顺序；此颜色一致性缺陷继续提前处理。

## 2026-10-24 启动恢复与 Q-Dir 导入共用文件视图构造路径

源码对照确认两种路径最终共用同一 Shell 宿主和视图状态恢复回调：启动时 `MainWindow` 将会话 `TabSettings` 传入 `PaneControl`，由构造函数逐项调用 `AddTab`；导入确认后 `MainWindow.ImportQdrAsync` 调用 `PaneControl.ImportTabs`，后者同样逐项进入 `AddTab`。`AddTab` 均以路径、`ViewState`、快速筛选及预览/详情窗格状态创建 `ExplorerBrowserHost`。两者导航完成后都在 `DispatcherPriority.Loaded` 阶段调用同一个 `ApplyViewState`，恢复查看方式、列、排序和分组。

因此现有源码没有“导入专用文件视图类”或导入专用颜色设置路径；这与前面的 Q-Dir 配置字段检查相符。两条流程仍有时序差别：导入发生在已运行窗口中，会追加多个标签并切换选中标签；启动恢复则在主窗口初始化时一次创建会话标签。仅凭共享构造/恢复代码不能断定 DirectUI 绘制状态相同，也不能解释导入后暂时变黑。

### 2026-10-24 构造时机差异细化（待验证假设）

源码显示，启动时 `MainWindow` 先构造各 `PaneControl`，并把会话标签作为未选中的 `ExplorerBrowserHost` 保存；窗口展示、标签选中后，`PersistentTabHost.Show` 才把选中宿主接入可见视觉树，由 `HwndHost.BuildWindowCore` 创建 ExplorerBrowser。导入则在窗口已运行时调用 `PaneControl.ImportTabs`：新标签先创建为未选中项，随后立即选中导入的标签，`PersistentTabHost.Show` 将新宿主加入已加载的视觉树；四窗格标签追加完成后，导入代码才应用布局变化。两条路径都由宿主初始化后异步导航，并在导航完成后用 `DispatcherPriority.Loaded` 调用相同 `ApplyViewState`。

这提供了一个待检验的具体差异：黑底可能与“向已运行窗口加入并选中新 HwndHost，再重排布局”的绘制生命周期有关；源码没有证据证明这一时序会导致颜色改变。下次可见实验应增加“运行中普通新标签（不导入）”对照：若普通新标签也黑，优先检查新宿主/已有窗口的绘制时序；若只有 `.qdr` 导入后黑，再比较导入的原生查看/列/分组状态。仍需同目录截图像素确认，不能直接强制白底。

下一次独占桌面验收仍应按前述最小实验记录同目录文件区像素、导入前后可见窗格、逐标签创建/应用状态顺序和重启结果；本轮没有修改颜色或主题行为。

### 2026-10-25 后续颜色对照序列补充

为区分 `.qdr` 导入和运行中创建 `ExplorerBrowserHost` 的影响，下一份隔离验收清单明确要求使用同一测试目录与“详细信息”模式，对比初始新视图、运行中普通新标签、取消导入对话框、导入最小 `.qdr`、重启恢复五个阶段；每阶段在同一屏幕区域记录文件列表截图/像素，并标出首次发生颜色变化的操作。仅当对照复现后再定位 Shell 视图时序或状态来源，不先强制白色背景。清单仅更新准备脚本，本轮不启动任何窗口。

### 2026-10-25 隔离最小 `.qdr` 夹具准备

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 现生成仅含 Q-Dir 版本、详细信息模式和一个隔离测试目录的 `qdr-color-baseline.qdr`；不带 `ColsAndSort`、颜色或系统路径。manifest 中记录夹具路径，并将待验收步骤明确为：比较新建视图、导入对话框取消、导入该最小文件及重启后的 DirectUI 文件区颜色。准备副本 `src/.state/gui-smoke/20261024-193803-842-eea458/` 状态为 `prepared-not-run`，四窗格，EXE/DLL 哈希匹配；文件本身的只读 importer 检查识别为一个标签，只有未知布局与颜色不导入两条警告。没有启动应用、GUI 或 Q-Dir，因此尚无新的颜色像素证据。

## 2026-10-25 隔离 IShellView 逐项颜色原型

用户选择先做隔离 `IShellView` 原型。`ExplorerDirs.NativeProbe --ishellview-color-spike` 直接从临时文件夹 Shell 对象创建默认 `IShellView`，不使用 ExplorerBrowser、不改生产代码、不显示窗口、不发送桌面输入。探针在本机看到 `SysListView32` 与 `SysHeader32`；选中夹具条目后，查询 `IFolderView2`、背景和选中项 `IContextMenu` 及 `IID_IDropTarget` 均成功（不执行任何命令或拖放）。当前两项夹具用项目索引指定不同的文字背景色。

ListView 的直接通知父级为 `SHELLDLL_DefView`，而列表子窗口创建时没有 `WS_VISIBLE`。单项夹具探针只在隔离视图里为列表子窗口设置该样式（父级仍隐藏）；5 次运行中 4 次收到整体 `CDDS_PREPAINT`，但没有项目级 `CDDS_ITEMPREPAINT`，包括发送 `WM_PRINTCLIENT` 的样本。两项夹具的合成通知验证了通知处理器的颜色分支和结构偏移；真实隐藏视图可枚举两项并取得原生选择菜单与拖放接口，但仍没有项目级绘制通知。因此目前只是验证到默认视图子控件和原生操作接口存在，**没有验证真实逐项颜色回调或屏幕像素着色**。Windows 官方 ListView 文档描述了在 `CDDS_ITEMPREPAINT` 修改 `NMLVCUSTOMDRAW::clrText` / `clrTextBk` 的机制，详细范围见[隔离原型记录](ishellview-color-spike.md)。实际成立与否需在获准的可见隔离窗口中确认。

尤其不能外推到生产 ExplorerBrowser：生产宿主的子窗口类型可能不同；切换宿主仍有菜单集成、选择、焦点、拖放和 Shell 命令兼容风险。未做架构迁移，也未改变已报告的导入后黑底问题。

## 2026-10-25 System/Light/Dark 与原生文件区的源码边界

源码追踪确认，全局主题偏好由 `ApplicationThemeController` 读取 profile 下的 `Theme.ini`，并替换 WPF `Application.Resources` 中的 `SystemColors` 画刷；`ThemeChanged` 当前只通知 `MainWindow` 更新主题菜单的勾选状态。该事件没有传给 `PaneControl` 或 `ExplorerBrowserHost`，控制器也没有对原生 Shell/DirectUI 文件视图设置背景或文字颜色。因此当前主题选项能够控制 WPF 外壳，不能据此承诺原生文件区跟随 System/Light/Dark。

导入路径仍如前述源码复核：`QdrImporter` 明确把 `useColor`、`useTreeColor`、颜色规则和背景列为不导入项；Q-Dir 原生视图流解析只保留查看、图标大小、分组、排序和列状态。现有 `ThemePreferenceSmoke` 验证 INI 默认、往返、未来格式保护以及纯逻辑颜色解析，没有创建 ExplorerBrowser 或检查 DirectUI 绘制像素。

这缩小了排查范围：全局主题偏好没有被 `.qdr` 导入接线直接修改，也没有从该控制器向文件列表下发颜色；但它不能解释“导入后暂黑、重启变白”，因为仍缺少用户当时所用 `.qdr`、同目录像素前后对照和运行中视图创建时序记录。已准备的最小 `.qdr` 可用于后续隔离可见复现；在复现前不把该现象归因到主题控制器，也不强制设白色背景。

### 2026-10-25 隔离原型再次隐藏复跑

按用户选择再跑一次非可见 `IShellView` 原型。八种查看方式、排序/分组读回、背景与项目菜单动词以及 `IDropTarget` 均可查询；生产命令适配器将复制、删除、重命名、属性和新建文件夹报告为可用，粘贴报告不可用且没有调用命令。绘制计数与先前样本一致：21 次整体预绘制、17 次逐项通知，只有平铺模式出现两条逐项通知；但通知矩形与裁剪区都是空值，目标色像素为 0。合成通知通过不能替代真实绘制证据。

探针退出码为 0，`parent-visible=False`、`foreground-unchanged=True`、`visible-test=False`、`product-untouched=True`。因此可以确认默认视图的原生查看/菜单/拖放接口仍在；无法确认 Q-Dir 式逐项着色能否实际显示。当前约束下不打开可见窗口。正式 ExplorerBrowser 仍保持原状，导入后暂时黑底的根因也仍未定位。

## 2026-10-25 ExplorerBrowser 与独立 `IShellView` 的公开扩展边界

本轮对照 Microsoft Learn 与本机 Windows SDK 10.0.26100.0。`IExplorerBrowser` 提供导航、当前视图查询、文件夹设置/选项和事件订阅；`IExplorerBrowserEvents::OnViewCreated` 在视图显示前通知宿主，可用于调整视图模式或文件夹标志。公开接口没有提供把 ExplorerBrowser 已创建的 `IShellView` 换成应用自定义视图的入口。最后一点是基于文档化接口面的判断，并非对所有未文档化实现的断言。

`SFV_CREATE` 的 `IShellFolderViewCB` 是由调用 `SHCreateShellFolderView` 的代码传入默认视图创建过程；这不等同于 ExplorerBrowser 给现存 DirectUI 文件视图暴露的逐项绘制回调。本机 SDK 的 `SFVM_*` 消息定义覆盖菜单、状态、列、通知、拖放等协作，没有公开逐行前景/背景着色消息。Microsoft 的 [`IExplorerBrowser`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)、[`OnViewCreated`](https://learn.microsoft.com/en-us/windows/desktop/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowserevents-onviewcreated)、[`SFV_CREATE`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-sfv_create) 与 [`SHCreateShellFolderView`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shcreateshellfolderview) 文档描述了这些宿主边界。

若绕开 ExplorerBrowser 自行承载 `IShellView`，项目需提供完整的 `IShellBrowser` 协作宿主。当前 NativeProbe 的 `StreamViewHost` 仅用于隐藏探针：菜单插入/设置/移除、状态栏、modeless、激活和工具栏回调是空实现；控件窗口查询与控制消息未实现；默认 `BrowseObject` 返回 `E_NOTIMPL`；视图状态流只读隔离内存。独立筛选探针虽覆写导航并实现有限历史，也不是生产级宿主。需补齐的 parity 面至少包括导航/历史、命令菜单合并、工具栏/状态栏、Shell 快捷键转发、视图持久化、通知/刷新、焦点、拖放和文件操作，并在产品 UI 中逐项验收。相关官方契约见 [`IShellBrowser`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellbrowser) 与 [`IShellView::CreateViewWindow`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-createviewwindow)。

因此当前决策是：生产继续使用 ExplorerBrowser，颜色规则保持未实现/未决；独立 `IShellView` 仅作为隔离可行性研究，不把已验证的默认 `DefView` ListView 着色原型迁入产品。若后续需评估切换宿主，先完成 parity 清单和独立可运行原型，再比较用户可见行为及维护成本。导入后暂黑问题仍按前述隔离对照方案定位，不能与逐项颜色规则混为同一缺陷。

## 2026-10-28 ExplorerBrowser 现有子类链的逐项绘制候选点

源码复核发现，生产 `ExplorerBrowserHost.AttachViewWindow` 会对子视图窗口、沿父链直到宿主窗口的窗口，以及该视图的所有后代安装同一 `SetWindowSubclass` 回调。`ViewWindowProc` 已处理 `WM_NOTIFY`，当前仅把通知交给原生 tooltip 主题刷新；尚未处理 `NM_CUSTOMDRAW`。

这提供了一个比替换整个 ExplorerBrowser 更窄的待验证接点：若当前 Windows/ExplorerBrowser 的 `SysListView32` 将逐项绘制通知发给已被子类化的父窗口，宿主理论上可以在调用 `DefSubclassProc` 前识别来自该 ListView 的 `NM_CUSTOMDRAW`，应用已解析的 Q-Dir 规则，并让其余通知继续由 Shell 默认处理。当前证据仅是源码中的窗口覆盖范围与消息分支；它**不证明**生产 DefView 会发送 `CDDS_ITEMPREPAINT`，也不证明 DirectUI/图标/平铺/内容视图会采用相同路径。

按当前无可见窗口约束，本轮没有创建 ExplorerBrowser 宿主或合成/注入通知，没有修改产品代码。下一步应在获准的隔离宿主中只观察 `WM_NOTIFY` 的 `hwndFrom`、控件类和通知码，再确认是否出现逐项绘制阶段及回调 DC。若不存在稳定逐项通知，继续沿用隔离 `IShellView` 迁移门槛；若存在，也必须先覆盖八种视图、选择态、排序/分组、导航替换和无规则默认绘制，才评估产品接入。

### 2026-10-28 隐藏 ExplorerBrowser 控件树快照

运行现有 `ExplorerDirs.NativeProbe --color-capability`，在不可见父窗口中导航到仅含隔离文件的目录，并在菜单查询及再次导航后枚举视图子窗口。三阶段控件树一致：`ExplorerBrowserControl`、`SHELLDLL_DefView`、`DirectUIHWND`、两个 `CtrlNotifySink` 和两个 `ScrollBar`；没有 `SysListView32` 或 `SysHeader32`。探针报告 `parent-visible=False`、`foreground-unchanged=True`，未创建产品窗口，也未读取/修改用户文件。

因此，针对当前这套 ExplorerBrowser/Windows 视图，依赖标准 ListView `NM_CUSTOMDRAW` 或 `SysHeader32` 的逐项颜色接点没有对应控件；探针也无法读取 ListView 专属颜色属性。此结果支持暂不向现有 `NativeShellTheme` 添加 ListView 着色代码，并把 Q-Dir 规则着色留在隔离 `IShellView` 架构评审中。控件树快照没有直接记录所有 `WM_NOTIFY`，也不能证明 DirectUI 不存在任何私有/未文档化绘制扩展；它只否定了“当前视图含标准 ListView，可直接挂 NM_CUSTOMDRAW”的假设。
