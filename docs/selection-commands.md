# 选择菜单与选区保持（2026-10-23）

## 源码行为

编辑菜单新增全选、取消选择、反向选择。反向选择还可在文件视图使用 Ctrl+Shift+A，菜单展开时读取当前标签的 Shell 项目数/选中数；
空视图、无选区的取消选择、导航未就绪或原生重命名中禁用对应入口。
保留 Ctrl+A 原有原生和文本编辑路由，不注册新快捷键。

命令从 IFolderView.Items 获取当前视图相对 PIDL，反选通过 IFolderView2.GetSelectionState
判断已选状态，全部准备完成后调用 SelectAndPositionItems。取消选择只读取已选项。
不重新枚举文件系统，也不改变目录、查看方式、排序或分组。Ctrl+A 保持原生 Shell/文本编辑语义；Ctrl+Shift+A 由活动文件视图调用同一反选命令。
菜单“全选”在文件视图可重新取得焦点时，改为调用同一 Shell `IShellView.TranslateAccelerator` Ctrl+A 路径；只在调用线程内临时置 Ctrl 状态，并在 Shell 同步处理后恢复原键盘状态。若 Shell 不处理快捷键或文件视图无法取得焦点，则回退到原有 PIDL 批量选择路径。
选择类快捷键由文件视图处理；主窗口检测到 WPF 文本输入控件聚焦时，会将 Shift+Enter 和 Ctrl+Shift+A 留给输入控件。

准备期间检查原浏览器、导航代际、活动窗格、重命名和选区版本；失效时不写入。
反选采用取消原选中项和选中原未选中项两个批次，批次之间复查视图身份。
两个批次不是事务，第二批取消或失败不会回滚第一批；发生原生错误提示检查当前选区后重试。

原焦点恢复无条件使用 SELECT|FOCUSED，会把反选后未选中的焦点项重新选中。
直接改成 FOCUSED 又会取消该项的选中状态。现通过实际 PIDL 读取 selected bit 后保留它，
仅在原焦点项确实是唯一选中项时才使用原有 UIA 聚焦兜底。

## 原生证据

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --selection-commands
$env:EXPLORER_DIRS_PROBE_SHELL_SELECT_ALL = '1'
dotnet src/ExplorerDirs.HostProbe/bin/Release/net10.0-windows/ExplorerDirs.HostProbe.dll --live-shell-selection-large 10001
```

- 未实现时在首个 All 检查失败。
- 原生批量调用带 SELECT|DESELECTOTHERS 时返回 S_OK，但只有一项选中；等待消息处理后仍相同。
  改为对不同集合分别使用 SELECT 和 DESELECT；两者均使用 NOTAKEFOCUS。
- 三项唯一 fixture：详细信息、列表、图标，各自有/无分组，共六组检查通过。
  核对全选、取消选择、从全选/空选区反选、部分选区的身份补集、两次反选恢复。
- 准备期间失效保持原选区；第一批后视图失效不执行第二批。
- 生产焦点恢复 helper 加 NOTAKEFOCUS 后保持选区，模式/排序/分组不变；空目录三种命令通过。
- 最终退出码 0，`parent-visible=False; foreground-unchanged=True`。所有变化仅涉及测试视图选区；
  没有复制/删除/重命名文件、输入键鼠、修改剪贴板或加载日常 profile。
- 生产 `TrySelectAllViaNativeAccelerator` 隐藏宿主复验：1,000 项约 3 ms；10,001 项约 13–17 ms，目标项排除后抽样确认剩余 10,000 项，删除未选中目标后身份完全保持，fallback=0。探针仅在隐藏 UI 线程调用产品 Shell accelerator helper，临时设置并恢复本线程 Ctrl 状态，不注入桌面键盘。
- 八种生产查看方式矩阵复验：隐藏四窗格宿主在第一个窗格依次切到超大/大/中/小图标、列表、详细信息、平铺、内容；每种方式都调用菜单实际使用的 Shell `TranslateAccelerator` helper，读取到完整 4/4 个相同文件身份，其他三个窗格选区未变，查看模式保持，helper 均返回 handled。命令为 `EXPLORER_DIRS_PROBE_SHELL_SELECT_ALL=1`、`EXPLORER_DIRS_PROBE_SHELL_SELECT_ALL_ALL_MODES=1` 配合 `--live-shell-selection-large 4`。该矩阵是四项隔离夹具、未设置分组/排序，不替代可见菜单与真实焦点 GUI 验收；10,001 项单模式压力仍由上一项结果覆盖。
- 全选性能对照：同一 1,000 项中自定义 `SelectAndPositionItems` 单批约 1,874 ms；拆成 10 个 100 项批次约 1,926 ms。原生 Ctrl+A 路径因此用于菜单“全选”，自定义批量 helper 保留为 Shell 不处理时的回退，以及取消选择/反向选择的实现。
- 隐藏宿主没有运行 WPF 菜单点击或可见窗口；菜单关闭、菜单点击后的焦点、其他窗格以及真实键盘操作仍需 GUI 验收。

分组设置可异步重排索引，测试因此在设置后处理消息，并在重新建立部分选区时重新捕获项目身份，
不假设两次 index 0 代表同一项目。实际调用使用 PIDL，结果验证使用独立 ShellItemArray 的项目身份。

## 待验收

实际 App 菜单退出和确认焦点、方向键、Alt+Tab 后状态、多窗格切换、网络和大目录响应。
隐藏原生选择测试不覆盖 WPF 菜单、UIA 焦点兜底或实际窗口显示，也不是 F2 重命名或自动刷新修复证据。

## 接口依据

- [IFolderView.Items](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-items)
- [IFolderView2.GetSelectionState](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-getselectionstate)
- [IFolderView.SelectAndPositionItems](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems)
- [SVSIF 选择与焦点标志](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif)
- [IShellView.TranslateAccelerator](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-translateaccelerator)
- [SetKeyboardState（仅改变调用线程键盘状态）](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setkeyboardstate)
