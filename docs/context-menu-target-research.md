# 文件区右键菜单目标识别（历史调查）

## 当前状态

用户决定文件区菜单采用 Windows Shell 原生内容，不恢复此前的标签/路径自定义项。ExplorerBrowserHost 已删除自定义命令菜单注入、目标 UIA 探测及自定义 `WM_COMMAND` 分派；产品文件区的 `WM_CONTEXTMENU` 除顶部列头预设门控外交由原生 Shell 处理。菜单主题和 tooltip 消息处理仍保留。当前没有为文件区自定义菜单读取 Shell 选区的产品路径。

`ShellMenuTargetTracker` 文件目前只保留隔离 NativeProbe 所需的纯 ListView hit-flag 解析与坐标解码；它不再跟踪产品右键菜单。产品 ExplorerBrowser 使用 DirectUI，隔离 IShellView 的原生 ListView 命中结果不能外推到产品。产品菜单的实际行内空白、列尾空白、键盘触发及 Shell 扩展表现仍需集中可见验收。

## 历史问题与实现

文件区自定义菜单曾短暂用于新标签打开和复制路径。旧实现仅查看 Shell 当前选择集，无法区分鼠标点中的项目、背景或键盘菜单；后续尝试以异步 UI Automation 命中目标，但 ExplorerBrowser DirectUI 不提供可验证的逐项命中范围，因此该功能按用户决定停用并删除，不再维护目标识别方案。

旧命中跟踪回归已移除；当前 `--shell-menu-target-smoke` 仅覆盖隔离 NativeProbe 仍在使用的 ListView 标志分类与 Win32 坐标解码，不证明产品 ExplorerBrowser 的菜单行为。

## 2026-10-25 DirectUI 鼠标命中公开 API 边界复查

ExplorerBrowser 的 DirectUI 文件区没有 `SysListView32`，不能走 `LVM_SUBITEMHITTEST`。复核 Microsoft UI Automation 文档后，`AutomationElement.FromPoint` 只保证返回元素的包围矩形包含该点，并明确说明该点未必位于控件可点击区域；`GetClickablePoint` 返回的是控件上一个可点击点，不能反向判断用户给定坐标是否命中条目；`TextPattern.RangeFromPoint` 返回离坐标最近的退化文本范围，也不保证该坐标属于当前文件条目。故这些 API 单独使用都不足以证明 DirectUI 列表中的真实鼠标目标，不能把 `ListItem` 的整行 UIA 矩形当作逐项菜单命中证据。

当时源码采用 fail-closed 行为：原生 ListView 可执行精确 hit test；DirectUI 或其他无精确命中 API 的路径返回 `Unknown`，省略依赖文件目标的自定义命令，保留 Shell 原生上下文菜单。后来用户决定不恢复这些自定义命令，因此该目标判定分支已经移除。

依据：[Microsoft AutomationElement.FromPoint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.frompoint?view=windowsdesktop-10.0)、[GetClickablePoint](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-getclickablepoint)、[TextPattern.RangeFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-rangefrompoint)。

## 集中可见验收

集中验收时检查：未选择时在空白处右键、已有选区后在空白处右键、文件/文件夹条目右键、Shift+F10/菜单键，以及查看/排序/分组/刷新/新建等 Shell 原生菜单。确认空白点击的选择行为、菜单随当前浅/深主题变化和多窗格一致性；不再检查或期望出现标签/路径自定义项。

把“最后一行下方的列表空白”和“可见行右侧未显示文字的尾部区域”分开操作。详细视图可能把整行作为 Shell 项目命中区域，菜单类型由 Windows Shell 的结果确认。

## 2026-10-26 隐藏 ExplorerBrowser UIA 树探测

Release HostProbe 对隐藏的生产 `ExplorerBrowserHost` 视图，在独立线程读取 UI Automation Control View。结果只有一个 `SHELLDLL_DefView` Pane 节点，没有文件列表、文字或图标子节点；探针确认父窗口仍隐藏且前台窗口未变。日志：`src/.state/shell-view-uia-tree-20261026-093116.log`。

因此此隐藏视图样本没有提供逐项 UIA 矩形或命中坐标，不能解决 DirectUI 右键目标识别，也不足以验证可见 ExplorerBrowser 下 UIA 树的差异。保留现有 `Unknown` / fail-closed 规则，不把该隐藏探测当作可见命中或菜单类型证据。
