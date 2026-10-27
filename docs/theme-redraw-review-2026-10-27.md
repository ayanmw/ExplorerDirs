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

## 同批次继续优化与重复验证

菜单宿主的 WM_MENUSELECT/WM_ENTERIDLE 现在走 ApplyMenuFramesFromOwner：已有当前线程返回钩子时跳过宿主的重复处理，钩子仍在原生处理完成后执行原有描边、裁剪及子菜单补偿；钩子未安装时保留宿主兜底。隐藏窗口实际 SendMessage(WM_MENUSELECT) 验证返回钩子确实运行；同时验证宿主跳过、清理后兜底可用、嵌套生命周期及已有菜单几何回归。没有改变已验收的裁剪/边框/位置算法。

树布局候选已做反证：隐藏原生 STATIC 父子窗口下，相同尺寸 MoveWindow 连续 20 次没有产生冗余 WM_WINDOWPOSCHANGED，真实尺寸改变仍正常。因此不保留尺寸缓存或新 helper，不把它作为已证实的闪烁来源；该临时夹具测量日志为 src/.state/theme-hang/opt-tree-red.log。隐藏窗口测量不能证明可见树绝无擦除，仍以实际场景为准。

用户授权同一批次优化经重复验证后覆盖同一预览版本；本次继续更新 temp.76，完整保留配置及旧包。

发布完成：temp.76 构建来源 2b9bcc6；系统主题及原生主题专项各连续两轮通过，完整 SettingsSmoke 通过。旧包完整备份至 releases/.history/preview-0.6.0-temp.76-before-redraw-opt-20261027-111749；替换前校验 active INI，保持原 profile 原位，发布文件与 staging 哈希一致。通过 Computer Use 启动确切 release EXE，截图观察四窗格、深色原生主/子菜单及贴合边界，随后归还桌面；未做完整浅色/所有交互的本轮 GUI 复测，不宣称全部闪烁消失。

## 右键打开时文件区与菜单闪白

用户在已发布 temp.76 中观察右键时文件区及菜单闪白。源码确认 BeginMenu 每次都刷新进程主题策略/菜单缓存并向 Shell owner 发送 WM_THEMECHANGED。隐藏 owner 子类消息计数回归在旧逻辑下失败，移除逐次无条件刷新后连续 10 次菜单开关没有 owner 主题失效消息；显式 SetProcessTheme + Apply 仍能发送原生主题变化。将 RefreshImmersiveColorPolicyState 移至真正应用进程主题的入口，保留主题切换时缓存刷新。

菜单返回钩子原先只在首轮 WM_PAINT/WM_NCPAINT/WM_WINDOWPOSCHANGED 等之后尝试主题；现增加 WM_CREATE 完成阶段，为新菜单窗口在首次显示/绘制前应用主题。原有边框、裁剪及子菜单补偿保持。该时序调整需要真实新建菜单与深浅往返验收，静态截图不能证明短暂白帧已消失。

### 菜单内容与外框分两次出现的进一步证据

- 112450 诊断未观察到创建钩子：只有 WM_ENTERMENULOOP 装钩子不足。将范围提前到 ViewWindowProc 的 WM_CONTEXTMENU / WM_RBUTTONUP 调用 DefSubclassProc 前后并用 finally 平衡后，112702 捕获 HCBT_CREATEWND，visible=False。
- 112854 诊断在创建阶段成功应用主题和挂接菜单子类，之后收到隐藏状态的定位消息。日志也显示区域修改可再入定位，最终代码增加重入保护、SetWindowRgn 使用 redraw=false。
- 用户确认 113054/beb2d0 文件区不再闪白，但边框仍晚出现；后来截图确认主/子菜单各有两套边框。不能把该版记为边框同步验收通过。
- 113324 诊断实际捕获每次弹出先收到 WM_PRINT(0x317) 和 WM_PRINTCLIENT(0x318)，visible=False，flags=0xE。系统先将菜单绘入动画用 DC，而旧代码只向真实窗口 GetWindowDC 描边，所以临时图像仍有旧外围，后续真实窗口才补最终线。
- 最终候选对两个打印消息保存/恢复调用方 DC，在同一可见区域内调用原生绘制，再将同一条细边框画入给定 DC；PRINTCLIENT 转为客户区坐标。没有关闭系统动画、修改全局参数或改动菜单命令。
- 新离屏像素回归先失败后通过：317/318 的深→浅→深图像均有对应 #565656 / #A0A0A0 边框，旧外围像素未被重画，DC 裁剪和画刷颜色恢复。原生专项再次运行通过，系统主题专项及完整 SettingsSmoke 通过。
- 113617/e12b4b 是移除诊断文件写入后的候选（其后只有测试扩展）。打开调用被 Computer Use 物理 ESC 硬停止拦截；不得记为已启动或边框同步通过。现有 temp.76 保持不变，待实际动画验收后按同批规则覆盖。

参考：[WM_PRINT](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-print) 与 [WM_PRINTCLIENT](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-printclient)，后者文档明确涉及 AnimateWindow 的绘制要求。

### 单框贴合最终验收
113617 的动画同步获确认，但仍有双框间隙。修复嵌套 PRINTCLIENT 重复装饰，并补回归证明内部不再出现第二条内缩线；真实截图 114802 仍有外层合成框，因此没有记为全部解决。最终在动画取图前和定位完成后重置合成边框抑制缓存，避免创建/显示重置默认状态后仍仅凭颜色戳跳过。114915/4abaf6 用户明确确认：同步、单框、无间隙；随后再次确认右键文件区闪白和菜单双框均已解决。原生专项通过；现继续处理用户新增的窗格切换闪烁，尚未覆盖 temp.76。

### 活动窗格切换引起两个内容区重排
用户确认右键文件区刷新及菜单双框已解决后，报告窗格切换时两个窗格同时闪烁。SetActivePaneVisual 原先将边框从 1 像素改为 2 像素，导致旧/新窗格内容区域都变化，ExplorerBrowserHost.OnRenderSizeChanged 随后调用 Shell SetRect。修复为 inactive 边框1+Padding1、active 边框2+Padding0，总内缩始终2；缓存活动状态，重复激活不重设动态画刷引用。原边框视觉与Shell刷新恢复流程保留。
未挂接生产 PaneControl 的布局回归在旧代码先失败，修复后连续10次激活/取消均保持 RootBorder.Child.RenderSize，相同状态保留资源引用；未创建 HWND/Shell view，前台不变。115332/0fd4d2 隔离候选已启动，真实窗格切换闪烁效果待用户反馈。原生主题、系统主题、未挂接标签流程均通过。

最终用户验收：115332/0fd4d2 的窗格切换闪烁已解决，允许本版本落地；本轮右键文件区刷新、菜单动画单框贴合与窗格切换均获用户确认，按既有同批次规则更新 temp.76。

最终发布：temp.76 源码提交 da6b3c3；2026-10-27 完成原目录覆盖，旧包完整备份于 releases/.history/preview-0.6.0-temp.76-before-final-redraw-20261027-120303。5个 active INI 保留原位并在发布前校验一致，旧包全文件及 staging/目标发布文件哈希通过。精确 release EXE 经 Computer Use 启动并观察四窗格会话恢复；已归还桌面。

## 原生滚动条端部首次绘制

用户报告端部白块经鼠标悬停消失。120621/8d7c87 实际四窗格截图复现；控件诊断只有 SysListView32/列标题，没有独立 ScrollBar 子 HWND，也没有 WM_CTLCOLORSCROLLBAR 消息。120843/2b012e 在延后主题检查中对可见 SysListView32 发送一次 WM_NCPAINT 后截图白块消失，但用户重启观察到从白到深的过渡，未通过首次显示验收。

当前候选把修复同步接入原生可见 WM_WINDOWPOSCHANGED / WM_NCPAINT 返回前；以主题、水平/垂直滚动条标志及窗口尺寸去重，同状态不重复发送，真正应用主题后清除旧印记。保留延后枚举作为懒创建的补漏；隐藏标签显示时排入一次合并主题检查。仅重画非客户区，不调用 IShellView.Refresh，不擦除客户区，不改变大小或焦点。

隐藏原生消息计数回归通过：一次 frame paint，客户区 paint/erase 为0，20次相同状态检查不增加绘制；浅色切换与返回深色各增加一次frame paint。该测试使用普通隐藏 HWND，不证明实际 SysListView32 的首次可见时序。121215/118a22 已启动供用户验证，启动白块过渡是否消失仍待反馈；temp.76 暂未覆盖本项。诊断 File.AppendAllText 已移除。

用户随后拒绝 121215/118a22：列表填充后再次绘制出白块，且不再自动消失。主题/尺寸/滚动条位缓存不足以表示实际绘制失效。现将“重复主题扫描”与“实际 WM_PAINT/WM_NCPAINT”分开：前者按状态去重，后者在 native DefSubclassProc 返回前同步补画非客户区，不按旧扫描印记跳过。独立 HWND 级正在绘制标记在 finally 恢复，补画产生的再次 NC 通知不递归。

新增真实隐藏消息计数回归：缓存相同但新 paint 失效仍会增加一次 frame paint；frame 回调内部再次请求强制补画会被重入门控拒绝；没有客户区 paint/erase。原生专项、系统主题专项通过。121630/31e686 打开被 Computer Use 物理 ESC 硬停止拦截，因此没有新的可见验收证据；不继续桌面输入、不覆盖 temp.76。后续将使用提交后的最终准备候选，只有用户验证首次显示和列表填充后均无白块才关闭此项。
