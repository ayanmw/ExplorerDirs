# 当前目录递归大小实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为活动文件系统目录增加可取消的后台递归大小与项目数计算。

**Architecture:** 独立同步扫描器由 `Task.Run` 调用，使用取消令牌并明确跳过重解析点；`PaneControl` 绑定扫描到发起时的标签与路径，通过独立状态栏文本显示状态/结果。F9 切换开始与取消，导航、切标签、隐藏和卸载使任务失效。

**Tech Stack:** C# / .NET 10 Windows、WPF、ExplorerBrowser、SettingsSmoke。

**Spec:** `docs/superpowers/specs/2026-10-25-recursive-directory-size-design.md`

## Global Constraints

- 文件视图继续使用 Windows Shell ExplorerBrowser。
- 递归扫描仅由 F9 明确启动，不加入自动目录导航扫描。
- 绝不跟随重解析点目录，不在 UI/STA 执行递归 I/O。
- 不持久化扫描结果，不改变 INI、会话或 `.qdr` 格式。
- 本轮不启动 GUI 或操作桌面。

## Review Focus

- 大目录与用户按键取消：token 每处理项目检查，任务完成后确认标签/路径仍一致。
- 权限拒绝与路径消失：返回部分结果并标注跳过数，不中断应用。
- 重解析点：扫描器不遍历链接目标。
- 标签/窗格切换和导航竞态：旧任务不更新新目录状态栏。
- 非文件系统 Shell 位置：不传给文件系统扫描器。

### Task 1: 递归扫描器

**Files:** `src/ExplorerDirs.App/RecursiveDirectoryScanner.cs`、`src/ExplorerDirs.SettingsSmoke/RecursiveDirectoryScannerSmoke.cs`、`src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj`、`src/ExplorerDirs.SettingsSmoke/Program.cs`

- [x] 先写并运行失败测试：空目录、嵌套目录大小和文件/目录数、预先取消；真实 NTFS junction 夹具验证扫描不进入链接目标（临时反事实下钻时 sentinel 计入并按预期失败）。
- [x] 实现迭代式目录栈扫描器，逐条检查取消；遇到访问失败或跳过重解析项时记录未读取计数并继续。
- [x] 运行专项与完整 Release SettingsSmoke。

### Task 2: 活动窗格和状态栏接入

**Files:** `src/ExplorerDirs.App/KeyboardShortcuts.cs`、`src/ExplorerDirs.App/MainWindow.xaml.cs`、`src/ExplorerDirs.App/MainWindow.xaml`、`src/ExplorerDirs.App/PaneControl.xaml`、`src/ExplorerDirs.App/PaneControl.xaml.cs`、`docs/keyboard-shortcuts.md`、`docs/roadmap.md`

- [x] F9 解析为目录大小命令；工具菜单和工具栏同时提供入口。
- [x] 后台启动/再次触发取消；仅当前发起标签和路径仍一致时发布结果。
- [x] 导航、切标签、隐藏/卸载时取消，状态不跨标签残留。
- [x] 验证 Release SettingsSmoke、App Release 构建和 `git diff --check`；不启动 GUI。
