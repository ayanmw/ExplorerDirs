# 命名查看预设实施计划

> **For agentic workers:** 按步骤逐项执行；每项先写会失败的 SettingsSmoke，再实现并验证。

**Goal:** 允许用户在同一 profile 的各窗格/标签间保存、应用和删除具名查看方式与列布局。

**Architecture:** 新增独立 `ViewPresets.ini` 存储，预设只包含查看模式、图标大小、显示列、顺序与宽度。应用时只更改当前标签的这几项，排序/分组沿用当前标签状态。菜单入口放在顶部“视图”菜单。

**Tech Stack:** .NET 10、WPF、Windows Shell `IFolderView2` / `IColumnManager`、现有 SettingsSmoke。

**Spec:** `docs/shell-parity.md` 的“排序、分组、列设置”一行；Q-Dir FAQ 137 已核实命名保存查看方式与列布局。

## 全局约束

- 不把命名预设加入 `ExplorerDirs.ini` 会话快照；配置使用独立 profile INI，遵循 `PortableIniWriter` 的并发修改保护。
- 保持 ExplorerBrowser 文件视图及原生 Shell 命令；应用预设不更改排序/分组。
- 不更改用户尚未验收的 temp.43 发布包，不启动可见 GUI。

## 审查重点

- 列键重复、空 GUID、越界宽度或空列布局必须拒绝保存和应用。
- 未来版本/被并行实例修改的预设文件不能被覆盖。
- 预设按名称替换、删除与序列化往返保持稳定。
- 应用预设后当前标签排序/分组不变，其他标签和窗格不变。
- Native Shell 应用失败时不能报告列布局已成功。

---

### Task 1：预设模型与 profile 存储

**Files:** 创建 `src/ExplorerDirs.App/ViewPresetStore.cs`；链接到 `src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`；测试 `src/ExplorerDirs.SettingsSmoke/ViewPresetStoreSmoke.cs`。

- [x] 先为保存/替换/删除、INI 往返、非法列布局及未来版本保护写失败烟测并观察 RED。
- [x] 实现受校验的具名预设模型与独立 INI 读写。
- [x] 运行专项 smoke 和完整 SettingsSmoke。

### Task 2：原生应用路径与视图菜单

**Files:** 修改 `ExplorerBrowserHost.cs`、`PaneControl.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`；修改 `ViewPresetStoreSmoke.cs` 或新增纯逻辑 smoke。

- [x] 先写失败烟测，验证预设与当前 `ViewState` 合并后保留 SortColumns、GroupKey、GroupAscending。
- [x] 增加当前活动标签的模式/图标大小/列设置应用入口，失败时保持会话保存保护。
- [x] 在顶部“视图”菜单动态列出预设，支持加载、保存为名称、覆盖同名和删除。
- [x] 运行 SettingsSmoke、App Release 构建；不启动 GUI。
- [ ] 取得桌面空闲授权后，验收菜单、Shell 列应用、排序/分组保留和重启体验。

### Task 3：文档与交付边界

- [x] 更新 `docs/column-layout.md` 和 Shell parity 状态，记录配置格式及 GUI 未验收项。
- [x] 暂不更新 `docs/roadmap.md`，等用户验收 temp.43 后再按要求调整。
- [x] 检查预设相关源码与文档 diff、空白；保留尚未预览的 temp.43 包，不生成新发布包。

### Task 4：Q-Dir 列标题 Ctrl+右键预设入口

**Goal:** 在原生 `SysHeader32` 列标题上按住 Ctrl 并右键时，打开当前窗格的具名查看预设菜单；普通右键和其它控件上的 Ctrl+右键保持 Shell 原行为。

**Approach:** `ExplorerBrowserHost` 识别 `WM_RBUTTONDOWN` 的 `MK_CONTROL` 与 `SysHeader32` 类名，并通过一次性 `ShellHeaderPresetMenuGate` 只配对随后的鼠标 `WM_CONTEXTMENU`；键盘菜单或取消点击会清除待处理状态。`MainWindow` 在该坐标弹出 WPF 预设菜单，保存/应用动作复用现有 Store 与当前标签 Shell 视图；已有顶部菜单仍是预设管理入口。普通右键与其它控件上的 Ctrl+右键继续交给 Shell。

**Files:** 修改 `ExplorerBrowserHost.cs`、`PaneControl.xaml.cs`、`MainWindow.xaml.cs`；新增纯逻辑 `ShellHeaderPresetShortcut.cs` 与其 SettingsSmoke。

- [x] 先写策略与一次性消息配对回归：Ctrl+右键只命中列标题；普通右键、键盘菜单、取消点击和已消费消息不能误截获 Shell 菜单。临时 `false` 门控下专项断言按预期失败。
- [x] 运行专项 smoke 确认 RED，再接入消息与弹出菜单。
- [x] 验证完整 SettingsSmoke、App Release 构建和差异检查。
- [ ] 不修改 `docs/roadmap.md`、不发布；可见菜单位置/焦点及 Shell 原生列菜单行为留待 GUI 验收。
