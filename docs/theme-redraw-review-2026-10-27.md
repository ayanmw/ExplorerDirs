# 2026-10-27 主题、刷新与重绘审查

## 范围与证据边界

围绕 ApplicationThemeController、SystemThemeMonitor、ApplicationThemeWindowBinding、NativeWindowFrameTheme、NativeShellTheme、MainWindow、PaneControl、ExplorerBrowserHost、ShellTreeHost、NativeShellTree、PaneLayoutHost/PaneLayoutGrid，以及目录变化合并、刷新请求和完成门控进行源码审查。没有逐行审计全部文件或宣称无其他缺陷。重点是消息入口 → 状态变化 → Dispatcher/COM → 原生/WPF 绘制与退休回调。

本批未使用 Computer Use；未覆盖已验收的 temp.76 发布目录。自动测试不能证明实际屏幕闪烁已消失，也没有测量帧耗时或进程内存收益。

## 已确认并修复

1. **P2：无变化的系统通知触发全应用主题重应用。** SystemParameters 的任意属性通知均排入 Apply，重建主题画刷、刷新进程菜单主题并触发所有窗格 RefreshTheme。原逻辑在 20 次属性通知 + 1 次颜色通知下违反“颜色未变不刷新”的回归断言。现将一轮通知合并为一次 Background Dispatcher 任务，读取最新系统状态；解析后颜色及偏好均未改变则不更新资源或发送 ThemeChanged。保留高对比度下的系统资源重新读取。
2. **P2：释放后排队任务仍能重写应用资源。** SystemParameters 原来直接排入 Apply，而 Apply 无 disposed 校验。现在排队任务及 Apply 均检查释放状态。回归验证排队后先释放，不再替换资源或通知界面。
3. **P2：强调色变化时重复替换中性色画刷。** SetThemeResources 原先无条件替换所有画刷，DynamicResource 消费者因此收到无意义的资源变化。现在颜色相同则保留已有 SolidColorBrush；真实强调色和深浅切换仍更新。回归验证强调色改变后中性背景画刷身份不变、重复设置同一强调色不再次通知。

没有将 ThemeChanged 全部吞掉：System 切到颜色相同的固定模式仍通知菜单同步选项，但不替换画刷。相关回归已覆盖。

## 检查后保留的机制

- 目录变更队列合并创建/删除与强制刷新；原生通知先行，计数差异及遗漏通知通过现有兜底恢复。不能用删掉 Refresh 换取表面不闪烁。
- Shell 刷新完成及恢复选区流程对浏览器、目录/导航版本、用户输入版本进行校验，且有异步完成与释放路径。本批未发现需要立即修改的确凿新缺陷，不代表原生调用顺序已穷尽验证。
- 标签切换保留浏览器实例；切换时重建地址/统计展示与目录树同步有实际语义，不做整体删除。
- 原生主题 HWND 戳、延后创建子控件的主题刷新、菜单钩子重入保护、Tooltip 退出同步通知后再应用等已保留。
- 用户已验收的单层贴合灰边、子菜单裁剪补偿、文件区原生菜单内容保持原样。

## 候选风险，尚未证明是用户可见缺陷

1. **菜单重复遍历/描边。** ExplorerBrowserHost 的 WM_MENUSELECT/WM_ENTERIDLE 和线程返回钩子都可能调用 ApplyMenuFrames；返回钩子的 16ms 门控不涵盖宿主入口。源码可确认存在重复入口，但尚未量化一次真实菜单消息涉及的重复次数和耗时。移除一个入口前须覆盖 Shell/扩展菜单生命周期及钩子不可用路径。
2. **树布局重绘。** ShellTreeHost.OnWindowPositionChanged 总是 MoveWindow(..., true)，尚未对相同原生尺寸作跳过。是否导致多余 WM_PAINT/WM_ERASEBKGND，需固定尺寸与拖动尺寸两组消息计数；不能仅凭调用存在就归因闪烁。
3. **主题变化遍历所有已创建标签。** 保证隐藏标签切回颜色一致，但真实颜色变化时仍会遍历它们。这次消除的是无变化的主题广播；延迟隐藏标签应用需要额外的主题版本与显示前保证，不在本批扩大实现。
4. **高对比度与原生菜单外围。** BeginMenu 对高对比度有绕过，而 ApplyMenuFrames/返回钩子的其他路径仍需专项检验，包括运行中切换高对比度后的原区域与画笔。当前未切系统设置，也不声称该模式已完整验收。

## 验证

- `--system-theme`：原逻辑先失败；修复后通过。21 次混合通知只排入一个主题任务、未变色无广播/画刷替换、真实偏好变化、相同颜色下偏好 UI 通知、中性色复用、重复强调色跳过、释放后任务不更新。
- 同一专项仍覆盖 System/Light/Dark 配置恢复及 WPF 菜单正常/高亮/禁用、Tooltip、DataGrid、ContextMenu 和 ToolBar 模板。
- 测试夹具改为取消 App 构造时排入的生产启动任务，避免 Dispatcher 生命周期检查打开实际会话；最终检查应用窗口数为 0、前台不变。开发该检查的首轮因泵入生产启动而失败，已修正夹具，不把该轮计为无窗口验证。
- `--native-theme-colors`：原生颜色、Tooltip 去重、主题往返、钩子生命周期、裁剪区域恢复、树颜色、左右子菜单重叠与不累积位移通过。
- 完整 Release SettingsSmoke 通过；日志在 src/.state/theme-hang/review-settings.log。

## 后续可见验证建议

在独立候选中检查深浅往返、连续复制/切换标签、四窗格创建删除文件、拖动分隔条及原生右键子菜单。对“闪一下”的具体动作进行消息次数/耗时采样，再决定是否减少原生擦除或刷新。不要先用全局禁用重绘、Sleep 或删除通知恢复掩盖问题。
