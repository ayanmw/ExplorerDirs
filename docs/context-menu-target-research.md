# 自定义右键命令的目标识别

## 已确认的源码事实

`ExplorerBrowserHost` 承载 Shell 的原生文件视图，并在 `WM_INITMENUPOPUP` 把自定义命令插入原生菜单前端。`InjectCommands()` 此时直接调用 `GetSelectedItem()`；后者通过 `IFolderView.Items(SVGIO_SELECTION)` 读取选择集，并不知道菜单是由条目、空白处还是键盘触发。`PaneControl` 据此添加“在新标签页中打开”或“复制完整路径”。因此在空白处右键但选择集未清除时，前置命令可能使用错误的目标。该结论来自源码；真实窗口中的 Shell 选择变化仍待验收。

Shell 自己的背景与选择集分别有 `SVGIO_BACKGROUND` 和 `SVGIO_SELECTION`；继续保留它的原生菜单，不自行重做查看、排序、分组、新建等项。[SVGIO 枚举](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)

## 目标判定约束

- 鼠标菜单：按实际右键命中位置判定条目或背景，不能只凭已有选择集。只有已确认命中单个文件系统文件夹时，才提供“在新标签页中打开”；只有已确认命中单个条目时，才把“复制完整路径”指向该条目。背景菜单的路径命令应指向当前文件夹。
- 键盘菜单：`WM_CONTEXTMENU` 的坐标为 `(-1,-1)`，应按 Shell 当前选择集解释，而不能沿用上一次鼠标坐标。[WM_CONTEXTMENU](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-contextmenu)
- 未取得可靠命中证据时，前置命令不得把选中项当作鼠标目标。不得在 Shell 菜单消息处理期间同步查询本进程的 UI Automation 树；微软要求访问本程序 UI 元素的 UI Automation 客户端调用放在独立线程。[AutomationElement.FromPoint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.frompoint?view=windowsdesktop-10.0)

## 验收

在隔离测试配置中依次检查：空白处无选择、选中单个文件后空白处右键、选中单个文件夹后空白处右键、文件夹条目右键、普通文件条目右键、多选后右键、Shift+F10/菜单键。每次分别核对 Shell 原生项和项目前置命令的目标，执行路径复制时核对剪贴板内容。UI 自动化冒烟脚本当前不注入鼠标/键盘；这些菜单命中场景需要空闲桌面上的人工验收。
