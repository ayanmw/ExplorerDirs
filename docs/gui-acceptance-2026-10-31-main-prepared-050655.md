# 2026-10-31 主线集中验收候选（05:06）

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；只发布到隔离 `.state` 副本并准备测试夹具，没有启动应用窗口、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`eff0a65654b44c0a7d8b1de41d967e2071130891`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261031-050655-754-4b72da/`。
- BuildIdentity：`0.6.0-preview.20261031-050655-754-4b72da+eff0a65654b44c0a7d8b1de41d967e2071130891`。
- 状态：`prepared-not-run`，47 项待验收；初始四窗格布局。
- EXE SHA-256：`E9F92D0BF592C965F43663443A70661084349199E12BEF6F43D5FDE850367EE1`。
- App DLL SHA-256：`E00B02934666EF3D94765DE4ACA7CD31B8F045133B55AD9F31BBB98B2DEDF631`。
- 夹具与完整清单见副本 `preparation.json`；启动身份由唯一 BuildIdentity 区分。

## 验收范围

文件区焦点与输入、独立克隆视图、布局/标签、主题及原生菜单、Q-Dir `.qdr` 导入导出、系统 Explorer 入口、目录树、筛选、打印/导出、递归大小摘要、窗口恢复、F5 刷新和查看方式切换等，均以 `preparation.json` 中的 `PendingAcceptance` 为准。

`.qdr` 预览格式不匹配时文件列表处置仍等待 Q-Dir 验收后决定。逐行派生大小列仍只在隔离架构原型阶段，不属于产品已交付功能。该候选没有任何可见 UI 通过结论。
