# Explorer 详情窗格设计

## 目标

补齐 Explorer Dirs 当前缺少的 Windows 文件详情窗格，沿用 ExplorerBrowser 提供的原生元数据面板。文件列表和文件操作仍由 Windows Shell 提供。

## 范围

- 在“视图”菜单和工具栏中加入详情窗格开关，当前标签状态同步显示。
- `Alt+Shift+P` 切换详情窗格；`Alt+P` 继续切换预览窗格。
- 详情与预览互斥。打开其中一个会关闭另一个；关闭当前窗格后不自动恢复此前窗格。
- 每个标签独立保存窗格状态；新标签默认关闭，复制、关闭后恢复、Q-Dir 扩展导入/导出保留状态。
- 会话 INI 升为 v8，并在旧版本首次写回前备份原文。v1-v7 均可读取，旧版本详情窗格默认为关闭。
- `.qdr` 原生字段不表达详情窗格；仅由现有 Explorer Dirs 版本化扩展携带该设置。

## 实现方案

**选择：ExplorerBrowser 原生 Details frame。** `IExplorerPaneVisibility` 已在 ShellBrowserSite 中实现，且 ExplorerBrowser 已启用标准 Shell frame；使用 `EP_DetailsPane` 可保持 Shell 元数据、文件关联和系统主题行为。

自绘详情面板会重复实现属性读取、选区同步和元数据格式化；单独创建预览宿主也会绕过已有 ExplorerBrowser 结构，因此不采用。

## 行为细节

- Shell 帧状态用一个窗格模式表达，避免 Details 与 Preview 同时开启。
- 菜单和工具栏以当前活动窗格/标签为准；标签切换时刷新勾选状态。
- 点击或快捷键切换窗格后保留当前 Shell 文件视图对象、路径、选区和焦点恢复意图。
- 不新增 Tab 停靠点；复用“视图”菜单和现有工具栏区域。
- 不改变主题、全局默认查看方式、目录树或 Q-Dir 原生字段。

## 验收

- SettingsSmoke 覆盖 v8 往返、v7 默认关闭、升级备份、非法同时开启两窗格拒绝、预览/详情互斥和菜单快捷键映射。
- HostProbe 隐藏生产 Shell 宿主切换 None→Details→Preview→None，检查帧状态以及文件视图 HWND、路径、计数和选区保持。
- App Release 构建和 SettingsSmoke 必须通过。可见元数据渲染、尺寸、焦点、多窗格布局和重启恢复仍需用户空闲桌面的 GUI 验收；不得把隐藏宿主结果描述为可见完成。
