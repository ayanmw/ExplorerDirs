# 镜像三窗格布局 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 增加左双右大、上双下大，并兼容对应 Q-Dir 布局、标签和活动窗格。

**Architecture:** 在 LayoutPreset 末尾追加稳定编号 7/8，现有 Grid/分隔条/快捷图标按预设生成。
Q-Dir ViewMode 10/2 分别映射 7/8，原生窗格 1、2、3 为可见窗格，4 保留为隐藏窗格。
会话升级 v4（读取 v1～v4）、布局比例升级 v2（读取 v1/v2），保存前保留旧版本原文。
旧读者必须拒绝覆盖高版本，不默默将未知布局降为四窗格。

**Tech Stack:** C# / WPF / INI / QDR。

**Spec:** docs/qdr-layout-mapping.md 的本机 Q-Dir 12.79 布局分支；用户持续对齐 Q-Dir 的要求。

## 验证与交付

- [x] 独立原生输入和输出字段测试先复现缺少 ViewMode 10/2，再补齐预设/映射。
- [x] 会话/比例迁移、重复保存、旧格式读入、新格式拒写保护；用修改前 DLL 的设置类型验证旧读者。
- [x] 无窗口 WPF 几何验证八种布局和新增两种实际左右/上下位置；隐藏窗格对象、比例与 Tab 停靠点保持。
- [x] 构建、Settings smoke、独立审查，更新规则与路线图。

只读核对现有静态证据，不运行/修改 Q-Dir，不复制其实现代码或资源。可见布局拖动、焦点、
Q-Dir 打开导出的实际互操作仍需 GUI 验收；不发布或修改日常 profile。
