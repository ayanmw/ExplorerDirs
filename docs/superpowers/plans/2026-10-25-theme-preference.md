# 全局主题偏好实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. 按任务逐项执行，SettingsSmoke 每项先 RED 再 GREEN。

**Goal:** 增加默认跟随 Windows 的 profile 级 Light/Dark 主题偏好，并即时更新 WPF 应用界面。

**Architecture:** `Theme.ini` 独立保存用户选择；单个 App 级控制器解析用户偏好与系统当前配色，将 WPF SystemColors 资源替换为浅色/深色调色板，并把修改同步到所有窗口。系统模式使用 Windows `UISettings` 读取前景色并订阅 `ColorValuesChanged`；ExplorerBrowser 文件视图仍由 Shell 管理。

**Tech Stack:** .NET 10 WPF、Windows SDK WinRT `UISettings`、profile INI、SettingsSmoke。

**Spec:** [全局主题偏好设计](../specs/2026-10-25-theme-preference.md)

## 全局约束

- 主题默认 `System`，偏好与已解析 Light/Dark 分开；系统事件不写 INI。
- 所有本程序窗口共用一个偏好控制器；Q-Dir 导入/导出不改变主题。
- 只应用到 WPF chrome，不给原生 ExplorerBrowser 文件视图强加颜色。
- 保留 `UiPreferences.ini` 紧凑密度及现有地址栏、标签和视图状态语义。
- 不改 `docs/roadmap.md` 或已交付的 `preview-0.6.0-temp.43`。

## Review Focus

- 缺失、非法、未来版本及外部并发修改的 `Theme.ini` 必须采用安全回退/拒写。
- Light/Dark 的 SystemColors 前景与背景必须成对更新，避免菜单文字与背景对比错误。
- `System` 模式颜色变化必须在 UI Dispatcher 上刷新全部窗口；显式主题不随事件覆盖。
- WPF 深色主题不得被误当成 ExplorerBrowser 文件列表变暗或导入黑底缺陷已修复。
- App 退出时解除 WinRT 事件，避免控制器/窗口被长期引用。

## Tasks

### Task 1：偏好模型与 INI 存储

**Files:** 新建 `ThemePreferenceStore.cs`；链接 `ThemePreferenceSmoke.cs`；在 `Program.cs` 调用回归。

- [x] 先写缺省、三种值、损坏备份、未来版本和外部编辑冲突测试并确认 RED。
- [x] 实现版本化 `Theme.ini` 读写及无效值恢复。
- [x] 运行 ThemePreference 专项 smoke。

### Task 2：系统配色读取与主题资源

**Files:** 修改 App TFM 以引用 Windows SDK WinRT API；新建 `ApplicationThemeController.cs`、`ThemePalette.cs`；修改 `App.xaml`、`MainWindow.xaml`、`PaneControl.xaml`、相关代码。

- [x] 先为 System/Light/Dark 解析和调色板键映射写 RED 回归。
- [x] 接入 `UISettings.GetColorValue(UIColorType.Foreground)` 与 `ColorValuesChanged`，失败按设计回退。
- [x] 运行时替换 WPF 系统颜色资源并将显式硬编码边框/状态色改为动态资源；不改 Shell 文件视图。
- [x] App Release 构建；正常多窗口由 App 注入同一控制器。隐藏 HostProbe 保留无控制器路径，避免碰真实主题偏好。

### Task 3：主题菜单与文档

**Files:** 修改 `App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、SettingsSmoke；更新 `docs/shell-parity.md` 和设计记录。

- [x] 在“工具→选项→主题”增加三态选择，写入后立即更新全局资源，保存失败恢复原选项。
- [x] 通过配置隔离检查：QDR 导入/导出不调用主题存储；Theme.ini 单独保存全局偏好。
- [x] 运行完整 Release SettingsSmoke、App、HostProbe、NativeProbe Release 构建及 `git diff --check`。
- [ ] 可见系统切色、菜单、标题栏和文件区呈现只记录为待验收；不制作或替换用户未预览的发布包。
