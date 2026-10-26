# 查看方式与 Ctrl+滚轮调查

2026-10-21。用户报告 Ctrl+鼠标滚轮不能切换文件视图。
**滚轮输入问题仍未完成 GUI 复现/修复**；本轮在查看方式能力测试中发现并修正“小图标”参数问题。

同日再次核查源码，仍未发现消费 Ctrl+滚轮的处理；不能据此排除真实输入路由问题，
未新增转发或模式切换兜底。桌面被另一任务使用期间，改做 `.qdr` 原生状态流恢复测试：
发现并补齐输出的 LogicalViewMode，修复 256 图标恢复成 48 的数据缺项。
该变化属于状态导出，**不是滚轮修复**；完整证据见 [Q-Dir 导入导出记录](qdr-import.md#同日原生-shell-直接恢复与-logicalviewmode-修正)。

## 输入链路检查

- App 的 ExplorerBrowserHost 在 WM_MOUSEWHEEL + MK_CONTROL 时只做可选诊断，
  然后调用 DefSubclassProc；没有专门消费或重新发送滚轮消息。
- [微软 WM_MOUSEWHEEL 说明](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)
  指明 DefWindowProc 会向父窗口传播，禁止把未经验证的主动转发叠加到这条链路上。
- 查看了 [WPF HwndHost 上游源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Interop/HwndHost.cs)，
  未找到显式 WM_MOUSEWHEEL 分支。这不是本机完整 WPF 输入路由的运行证据，
  也不能排除 Shell、焦点目标或其他 WPF 组件的影响。

待桌面独占后，用已有 EXPLORER_DIRS_FOCUS_TRACE 记录真实滚轮经过的子窗口类、
Ctrl 标志、delta 和异步读回的模式/尺寸。分别检查文件项区域、背景、不同窗格、
地址编辑与原生重命名状态。先确认实际路径和 Shell 是否已改变查看方式，
再决定是否需要宿主补充处理；禁止在两个层级各改变一次模式。

## 隐藏原生能力测试

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --view-style-capability
```

使用已有隔离 fixture 和隐藏 ExplorerBrowser，直接调用
[IFolderView2.SetViewModeAndIconSize](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setviewmodeandiconsize)
并读回状态。没有发送滚轮/键鼠输入、设置焦点或启动可见窗口。

分别建立名称降序 + 无分组、名称降序 + 类型升序分组两套基线。
确认基线被 Shell 接受后选择两个项目；模式切换后按 Shell 文件系统身份比较选区，
不以“数量仍是 2”替代同一项目检查。分组设置本身可能清除选区，
因此选择基线建立在分组设置之后；这项测试只验证查看方式切换。

### 先失败、后修复

| 请求 | 本机读回 | 结果 |
| --- | --- | --- |
| 原“小图标”：mode=2, size=-1 | mode=1, size=48 | S_OK 但仍是中等图标 |
| 候选：mode=1, size=16 | mode=2, size=16 | Shell 会归一为小图标 |
| 修正“小图标”：mode=2, size=16 | mode=2, size=16 | 接受预期状态 |

前两项在有/无分组两种情况下均观察到。不是所有 Shell 提供方或 Windows 版本的普遍结论。
源码修复：

- 顶部/状态栏查看菜单及 Ctrl+Shift+4 共用的“小图标”预设改为 2/16。
- 默认新标签方式为小图标时，ViewState.Default 指定 16。
- 旧会话或导入状态的 mode=2、size=-1 在传给 Shell 时指定 16。
  不修改输入配置文件，不覆盖显式尺寸，其他模式仍使用原尺寸。

### 修正后的结果

八种预设、旧默认小图标恢复、返回详细信息，在两种分组基线下全部读回预期模式/尺寸；
两个选中项目身份、排序列/方向、分组键/方向保持。
实际位置 PIDL 未改变，导航回调仍是首次打开目录的 1 次开始/1 次完成/0 失败。
parent-visible=False，测试前后 foreground HWND 相同，退出 0。

这验证的是原生接口和预设参数，不是 Ctrl+滚轮输入、可见绘制、
键盘焦点、真实四窗格和重启恢复。修复尚未发布；这些项目继续保留 GUI 验收要求。

### 隔离消息路由探测

2026-10-23 首版 `--ctrl-wheel-routing` 在 Shell 浏览器刚可读时就取得 `_viewWindow`；
该时刻 HWND 仍为零，因此当时“模式未变化/退出 1”的结果无效，不能用来推断视图不处理消息。

2026-10-24 修正探针：先等待四个生产宿主的 Shell view HWND 附着，再向第一个窗格
`SHELLDLL_DefView` 发送合成 `WM_MOUSEWHEEL`（Ctrl + 120）。稳定详细视图基线 `(4, 16)`
读回 `(4, 18)`；再把相同消息直接发给其 `ExplorerBrowserControl` 父窗口，读回仍是
`(4, 16)`。其他三个窗格均保持 `(4, 16)`。`--ctrl-wheel-routing` 现读到有效 view HWND，
实际 Shell 子视图响应，退出 0；`--ctrl-wheel-window-chain` 输出并测试了子视图到 WPF 根
HWND 的父链。消息分别直接发给 `ExplorerBrowserControl`、宿主 `Static`、WPF 根 HWND 均不改
查看状态；只有直接发给 `SHELLDLL_DefView` 的消息改变读回尺寸。

同一隔离探针将八种生产查看预设分别设为基线，再发送正/负一个滚轮单位。八种情况下
读回的 `mode` 都保持不变；尺寸随方向改变，例如大图标 `96→107/86`、详细视图
`16→18/16`、内容 `32→36/29`。详细视图从 16 连续上滚 8 次为
`16,18,20,22,24,27,30,33,37`，仍是 mode 4；从最小尺寸 16 下滚保持 16。
因此当前 Shell 原生行为证据指向“调整当前查看方式的尺寸”，不是在详细信息/列表/图标等
方式间循环。若产品要把 Ctrl+滚轮定义为切换模式，那是另一个交互规则，不能称作已复用
Explorer 的原生行为。

这证明本机当前 Shell 的子视图能响应该合成消息，且消息直接发到父链不等价；它不证明
物理 Ctrl+滚轮一定路由到该子视图。探针没有鼠标坐标命中、真实 Windows 输入路由，
也没有模拟用户桌面输入；未检查可见图标重排效果、真实焦点和选区表现。因此暂不加宿主转发
或应用级模式循环，以免与原生尺寸调整叠加。真实输入需在后续桌面交接中，
通过 `EXPLORER_DIRS_FOCUS_TRACE` 记录实际命中 HWND、Ctrl 标志、delta 和处理后的模式，
再决定是否需要唯一的补充处理层。此结果不是滚轮修复或 GUI 验收。

命令：

```powershell
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --ctrl-wheel-routing
dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --ctrl-wheel-window-chain
```

`--ctrl-wheel-routing` 在 view HWND 有效且 Shell 改变状态时退出码为 0；无变化时为 1。
链路探针也使用临时 fixture，
不启动 MainWindow、不显示窗口、不设置焦点、不改剪贴板，也不操作用户路径。

## 2026-10-24：自定义切换方式实现

按用户明确要求，将 Ctrl+滚轮定义为八种查看方式循环。这是应用自定义行为：Windows Explorer 文档描述 Ctrl+滚轮调整图标大小，Shell 的本机探针也观察到只改尺寸、不改 mode。为避免两种动作叠加，`ExplorerBrowserHost` 在当前 Shell 视图子窗口处理并消费带 Ctrl 的滚轮消息；普通滚轮继续交由 Shell。正向滚轮沿 `ViewStyle.All` 向前一项循环，负向向后一项循环，多格消息按刻度累计；小于 120 的高分辨率增量按标签累计，只有凑足整格才切换。模式与预设不完全匹配时，图标模式选择最近尺寸，未知模式以“详细信息”作为起点。

`ViewStyleWheelSmoke` 检查双向切换、首尾循环、非预设尺寸、未知模式、多格及高分辨率部分刻度累计/反向抵消；`--ctrl-wheel-routing` 通过生产 HwndHost 的实际 subclass 路径对一个隐藏窗格连续发送八次正向滚轮，读回经过 List、小图标、中/大/超大图标、内容、平铺并回到 Details。三项选中路径每一步都保持，其他窗格模式未变。设置方式复用现有 Shell `SetViewStyle` 和标签快照读取流程，不重新应用排序/分组。

这只证明隐藏宿主的合成消息路径和状态切换；鼠标物理路由、可见效果、选区/焦点保持与普通滚轮滚动仍待桌面 GUI 验收。未发布新版本。

## 2026-10-25：查看方式切换失败时不丢弃 Ctrl+滚轮

源码路径复核发现 `CycleViewStyle` 在请求切换后无条件返回成功，`ExplorerBrowserHost` 又无条件消费完整刻度。因此当 Shell 视图尚未就绪或拒绝切换时，用户既看不到查看方式变化，也无法让原生 Shell 收到该滚轮动作。现在 `PaneControl` 将实际应用结果返回给消息路由：不足一格仍由本程序累计；成功切换时消费；失败时把累计后的完整刻度（含正/反方向和多格距离）重组后交给 Shell 原生子类链，避免此前已消费的部分刻度导致原生端拿不到完整滚动量。

`ViewStyleWheelSmoke` 增加四项路由决策回归。新增断言先因路由决策缺失而按预期编译失败；接线后全量 Release SettingsSmoke 通过，App Release 构建 0 警告、0 错误，`git diff --check` 通过。没有启动 ExplorerBrowser/HostProbe 或可见 GUI；物理输入路由、Shell 失败时的图标尺寸回退和焦点/选区仍需之后验收。

### 自定义列顺序和宽度补查

同一隐藏探测程序现直接编译使用生产 `ShellColumnManager`、`ColumnLayout` 及其状态类型，
通过真实 `IColumnManager` 设置名称、大小、类型三列，宽度分别为 271、137、193。
先立即读回确认设置已生效，再在上述有/无分组的每个查看模式下读回，按属性键、
顺序和宽度逐一比较；包括平铺、内容及最终返回详细信息，均保持完全一致。
这同时检查了非详细信息模式下自动保存能读到什么，避免只验证“返回详细信息后看起来相同”。

命令仍为 `--view-style-capability`，退出 0；选区身份、排序、分组及实际目录保持，
导航计数未增加，父窗口隐藏，测试前后前台 HWND 相同。本轮没有修改生产列处理逻辑。
证据只覆盖本机隔离文件系统目录和显式 API 切换，不覆盖真实滚轮、窗口列拖动、
虚拟目录提供方、跨 DPI 显示或完整应用重启。
