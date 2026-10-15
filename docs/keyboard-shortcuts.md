# 键盘操作清单

本表按 [Microsoft 的 Windows / 文件资源管理器快捷键清单](https://support.microsoft.com/en-au/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec) 核对。代码接入与实际 GUI 验收分开记录。文件视图是 Windows Shell ExplorerBrowser，不重新实现文件复制、删除等操作。

| 按键 | Explorer Dirs 行为 | 当前状态 |
| --- | --- | --- |
| Backspace、Alt+↑ | 打开当前目录的上一级 | 已接入 Shell 导航；Backspace 按用户习惯定义，区别于 Windows Explorer 的“后退” |
| Alt+←、Alt+→ | 当前标签的后退、前进历史 | 已接入 ExplorerBrowser 内置 TravelLog；历史仅在当前运行的标签内 |
| Tab、Shift+Tab | 当前窗格按后退、前进、上级、刷新、地址、转到、新建标签、关闭标签、标签标题、详细信息列标题、文件项的顺序遍历，再进入下一窗格；Shift+Tab 严格反向。非详细信息视图跳过列标题 | 已接入；单窗格隔离 GUI 测试通过，四窗格待用户验收 |
| Ctrl+Tab、Ctrl+Shift+Tab | 在当前窗格的标签间循环 | 已接入 |
| Alt+D、Ctrl+L | 聚焦并全选当前窗格地址栏 | 已接入 |
| F5 | 刷新当前文件视图 | 已接入 |
| F11 | 最大化 / 恢复窗口 | 已接入 |
| Ctrl+T、Ctrl+W、Ctrl+Shift+T、Ctrl+K | 新建、关闭、恢复、复制标签 | 已接入；原生文件视图聚焦时也路由到应用 |
| Ctrl+A、Ctrl+C、Ctrl+X、Ctrl+V、Ctrl+Z | 全选、复制、剪切、粘贴、撤销 | 已有 Shell 加速键转发及 Shell 命令兜底；真实文件操作待 GUI 验收 |
| Enter、F2、Delete、Shift+Delete、Alt+Enter、Ctrl+Shift+N | 打开、重命名、删除、永久删除、属性、新建文件夹 | 由原生 Shell 视图处理；逐项待 GUI 验收 |
| 方向键、Home、End、Ctrl+鼠标滚轮 | 选择、滚动、调整图标 | 由原生 Shell 视图处理；逐项待 GUI 验收 |
| Ctrl+E、Ctrl+F、Alt+P、Ctrl+Shift+E | 搜索、预览窗格、目录树 | 对应界面尚未实现，列入后续里程碑 |
| Ctrl+N | 新窗口 | 当前版本未实现；需先确定多窗口与共用 INI 的会话合并规则 |

地址栏获得焦点时，Backspace 和 Ctrl+A/C/V/X/Z 等文本编辑按键留给地址栏。Shell 内部正在重命名时，编辑键留给重命名输入框。布局切换后应把焦点放回该布局上次活动的文件视图；从单窗格切回四窗格应回到之前活动的四窗格位置。

`preview-0.5.2` 对文件操作后的 Win32 视图焦点与 Shell 文件项焦点一并恢复；如果 Shell 对话框关闭时没有前台窗口，且本应用仍处于活动状态，则恢复本窗口的前台焦点。隔离测试已验证 Ctrl+C/V 生成副本后方向键立即可用；Shift+Delete 需在真实窗口中复测，且只对测试文件执行。

## GUI 验收步骤

1. 每个窗格分别打开多级目录，用 Alt+←、Alt+→ 确认其历史互不影响；再按 Backspace、Alt+↑ 返回父目录。切换标签后原标签的历史仍在。
2. 在四窗格的某个文件项开始按 Shift+Tab，应依次到“名称”列标题、标签标题、关闭标签按钮；按 Tab 应原路返回。继续按 Tab 经过文件项后应到下一窗格“后退”按钮。切单窗格、再切回四窗格，确认焦点回到此前活动的文件区。进入地址栏并编辑文本，确认 Backspace 不触发上一级。
3. 在文件区按 Alt+D / Ctrl+L、Ctrl+Tab、F5、F11；再检查文件视图的复制、粘贴、重命名、删除、新建文件夹、属性等 Shell 操作。
