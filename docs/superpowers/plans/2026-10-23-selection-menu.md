# 原生选择菜单 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 补齐编辑菜单的全选、取消选择、反向选择，延续 Shell 文件视图操作语义。

**Architecture:** 从 IFolderView 的视图枚举取得相对 PIDL，以 IFolderView2.GetSelectionState 判断原选区，使用 SelectAndPositionItems 分别批量选择/取消选择。取消选择仅枚举选中项。原生试验发现批量 SELECT|DESELECTOTHERS 只保留最后一项，空 PIDL 配合 NOTAKEFOCUS 的清空调用返回 E_INVALIDARG，因此不采用这两条路径。不枚举磁盘、不注入按键、不依赖本地化菜单文字或未公开的命令编号。

**Tech Stack:** C# / WPF / Windows Shell COM。

**Spec:** 用户要求补齐菜单功能并复用 Explorer 原生文件视图；项目 AGENTS.md 的原生视图、选择保护及无 Computer Use 约束。

## 边界

- 只改变当前活动标签的选择，不更改路径、排序、分组或查看方式。
- 准备期间目录、标签、选择发生变化则取消；读取失败时不先清空原选区。
- 恢复焦点不得重新选中已取消选择的文件；重命名时不启用选择菜单。
- 保留已有 Ctrl+A 文本编辑/原生快捷键路由；本轮增加菜单入口，不注册全局快捷键。
- 使用唯一 fixture 的隐藏原生视图验证，不显示窗口、不运行真实文件命令；GUI 验收另列。

## 步骤

- [x] 增加隐藏原生选择回归，确认未实现时失败；覆盖全选、清空、两次反选、失效准备、模式/排序/分组不变。
- [x] 实现 ShellSelectionCommands，完成原生回归。
- [x] 接入编辑菜单、活动标签与保留选区的焦点恢复，完成构建及相关 smoke。
- [x] 独立源码审查，更新差距清单和验收边界。

真实菜单、焦点与方向键、大目录响应、多窗格切换仍未 GUI 验收。反选由两个原生批次组成，
并非事务：第一批后切换视图会取消第二批；原视图可能保留已完成的部分选择变化，不尝试向旧视图回滚。
