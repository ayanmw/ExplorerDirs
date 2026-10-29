# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。候选基于主线提交 `213ec06`，包含 Q-Dir `useColor`、`useTreeColor`、`colorthema` 与 `[Filter2]` 规则行的不透明会话保真及再导出。

## 固定身份

- 源提交：`213ec06744ef8b207550716ea51c5f93fbcee82a`，准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261029-224435-255-898951/`。
- BuildIdentity：`gui-acceptance.20261029-224435-255-898951+213ec06744ef8b207550716ea51c5f93fbcee82a`。
- 状态：`prepared-not-run`，36 项待验收。
- EXE SHA-256：`D17AB4D33354865A422FA3719F02E451EB5B2EA7A0678EA5F8DF800A33488691`。
- App DLL SHA-256：`D33BF5EE8385BD275E8E076303889ED7FD286566D7F457A76BDDF1B8C768EB4F`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单覆盖 `Ctrl+Shift+C`、`Alt+K`/`Alt+V`、F7 路径层级、F8/“此电脑”按钮和地址栏 Tab 顺序，以及标签栏“使用 Explorer 浏览”。Q-Dir 树分隔值仅作不透明字段往返，不代表 DIP 树宽度。窗口与文件视图的菜单、焦点、主题、文件操作、内存和 Explorer 入口仍需真实界面验收。

本候选也包含 Q-Dir 颜色字段的便携会话保真改动；颜色规则在 ExplorerBrowser 中仍未应用。当前 `qdr-color-baseline.qdr` 只是用于主题交互检查的基础工作区夹具，并不验证含颜色规则的 Q-Dir 实际互操作。当前批次的格式往返另有后台 Release SettingsSmoke 覆盖。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功，App Release 发布成功。独立核对确认 manifest 源提交匹配、`SourceWorkingTreeDirty=false`、36 项待验收及 EXE/DLL 哈希匹配；四个窗格夹具和 Alt+K/Alt+V 文件均已生成，ExplorerDirs 进程数为 0。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后集中可见验收；准备成功不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。旧副本仍保留在各自 `src/.state/gui-smoke/` 目录。
