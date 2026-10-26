# 自定义右键命令的目标识别

## 已确认的源码事实

`ExplorerBrowserHost` 承载 Shell 的原生文件视图，并在 `WM_INITMENUPOPUP` 把自定义命令插入原生菜单前端。修复前，`InjectCommands()` 直接调用 `GetSelectedItem()`；后者通过 `IFolderView.Items(SVGIO_SELECTION)` 读取选择集，并不知道菜单是由条目、空白处还是键盘触发。`PaneControl` 据此添加“在新标签页中打开”或“复制完整路径”。因此在空白处右键但选择集未清除时，前置命令可能使用错误的目标。该结论来自源码；真实窗口中的 Shell 选择变化仍待验收。

Shell 自己的背景与选择集分别有 `SVGIO_BACKGROUND` 和 `SVGIO_SELECTION`；继续保留它的原生菜单，不自行重做查看、排序、分组、新建等项。[SVGIO 枚举](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)

## 目标判定约束

- 鼠标菜单：按实际右键命中位置判定条目或背景，不能只凭已有选择集。只有已确认命中单个文件系统文件夹时，才提供“在新标签页中打开”；只有已确认命中单个条目时，才把“复制完整路径”指向该条目。背景菜单的路径命令应指向当前文件夹。
- 键盘菜单：`WM_CONTEXTMENU` 的坐标为 `(-1,-1)`，应按 Shell 当前选择集解释，而不能沿用上一次鼠标坐标。[WM_CONTEXTMENU](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-contextmenu)
- 未取得可靠命中证据时，前置命令不得把选中项当作鼠标目标。不得在 Shell 菜单消息处理期间同步查询本进程的 UI Automation 树；微软要求访问本程序 UI 元素的 UI Automation 客户端调用放在独立线程。[AutomationElement.FromPoint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.frompoint?view=windowsdesktop-10.0)

## 当前实现与限制

`ShellMenuTargetTracker` 在鼠标右键按下时保存屏幕坐标并启动异步 UI Automation 命中探测；`WM_CONTEXTMENU` 的坐标可更新目标，键盘坐标 `(-1,-1)` 清除旧鼠标结果。Shell 文件区收到菜单键或 Shift+F10 时也预先标记键盘来源，避免 `WM_CONTEXTMENU` 未到达当前子类窗口时沿用鼠标目标；若按键未形成菜单，该来源在 5 秒后失效。探测仅在元素属于当前 Shell 视图、位于列表内，并可区分条目或背景时返回结果。`WM_INITMENUPOPUP` 不等待 UI Automation；探测尚未完成、失败或过期时省略自定义命令，不阻塞原生 Shell 菜单。关闭菜单、切换到其他鼠标按钮时清除目标。

为下一轮定位真实空白命中与列表行命中差异，隔离进程设置 `EXPLORER_DIRS_FOCUS_TRACE` 后，每次异步命中会记录屏幕坐标、最终目标类别及 UIA 控件类型祖先链。日志不读取或写入 UIA `Name`、文件名、路径或值；未启用该诊断路径时不构造这条记录。记录只说明 UIA 报告的元素链，不证明 Shell 标准菜单实际类型。

自定义目标识别与 Shell 选择菜单类型是两条路径：本程序的 `InjectCommands()` 只在 Shell 已创建的菜单前端插入自定义命令，不负责生成“查看/排序/分组/刷新/新建”等标准背景菜单，也不把 Shell 文件项菜单转换为背景菜单。因此空白处右键若仍出现文件项标准菜单，现有目标探测修复不足以解释或修复该现象。需在可见验收中记录右键屏幕坐标、命中的 UI Automation 元素及 Shell 原生菜单项，确认点击位置是否落在列表行的可命中区域；取得这项证据前，不替换或重建原生 Shell 菜单。

无窗口回归覆盖背景处保留选中项、条目命中、异步未完成/失败、键盘菜单、未形成菜单的按键目标过期、坐标变化、过期鼠标目标与负坐标解码，以及诊断行只包含签名坐标、目标和控件类型。现可用 `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release -- --shell-menu-target-smoke` 在 Shell 探针初始化前单独运行这些目标状态测试；完整 SettingsSmoke 仍复用同一组断言。它不证明实际 ExplorerBrowser 的 UI Automation 树结构、探测时序、命中率或 Shell 生成的标准菜单类型；这些仍需真实窗口验收。

## 2026-10-25 DirectUI 鼠标命中公开 API 边界复查

ExplorerBrowser 的 DirectUI 文件区没有 `SysListView32`，不能走 `LVM_SUBITEMHITTEST`。复核 Microsoft UI Automation 文档后，`AutomationElement.FromPoint` 只保证返回元素的包围矩形包含该点，并明确说明该点未必位于控件可点击区域；`GetClickablePoint` 返回的是控件上一个可点击点，不能反向判断用户给定坐标是否命中条目；`TextPattern.RangeFromPoint` 返回离坐标最近的退化文本范围，也不保证该坐标属于当前文件条目。故这些 API 单独使用都不足以证明 DirectUI 列表中的真实鼠标目标，不能把 `ListItem` 的整行 UIA 矩形当作逐项菜单命中证据。

源码继续采用 fail-closed 行为：原生 ListView 可执行精确 hit test；DirectUI 或其他无精确命中 API 的路径返回 `Unknown`，省略依赖文件目标的自定义命令，保留 Shell 原生上下文菜单。若后续通过可见验收发现 UIA 子项确实提供可验证的图标/文字范围，可再用该范围做精确命中；需同时验证右键行内空白、列尾空白、空列表背景、多选和高 DPI。当前没有证据支持放宽到整行命中。

依据：[Microsoft AutomationElement.FromPoint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.frompoint?view=windowsdesktop-10.0)、[GetClickablePoint](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-getclickablepoint)、[TextPattern.RangeFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-rangefrompoint)。

## 验收

在隔离测试配置中依次检查：空白处无选择、选中单个文件后空白处右键、选中单个文件夹后空白处右键、文件夹条目右键、普通文件条目右键、多选后右键、Shift+F10/菜单键。每次分别核对 Shell 原生项和项目前置命令的目标，执行路径复制时核对剪贴板内容。UI 自动化冒烟脚本当前不注入鼠标/键盘；这些菜单命中场景需要空闲桌面上的人工验收。

桌面记录需把“最后一行下方的列表空白”和“可见行右侧未显示文字的尾部区域”分开操作。详细视图可能把整行作为 Shell 项目命中区域；只有菜单结果和目标路由记录都支持时，才能称某个位置为空白背景。启用隔离 `-CaptureFocusTrace` 时，`focus-trace.log` 只记录屏幕坐标、目标类别和 UIA 控件类型，不写项目名称或路径。

## 2026-10-26 隐藏 ExplorerBrowser UIA 树探测

Release HostProbe 对隐藏的生产 `ExplorerBrowserHost` 视图，在独立线程读取 UI Automation Control View。结果只有一个 `SHELLDLL_DefView` Pane 节点，没有文件列表、文字或图标子节点；探针确认父窗口仍隐藏且前台窗口未变。日志：`src/.state/shell-view-uia-tree-20261026-093116.log`。

因此此隐藏视图样本没有提供逐项 UIA 矩形或命中坐标，不能解决 DirectUI 右键目标识别，也不足以验证可见 ExplorerBrowser 下 UIA 树的差异。保留现有 `Unknown` / fail-closed 规则，不把该隐藏探测当作可见命中或菜单类型证据。
