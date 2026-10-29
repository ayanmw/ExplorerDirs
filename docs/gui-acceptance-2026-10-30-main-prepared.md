# 2026-10-30 主线集中验收候选

此副本仅用于后续集中 GUI 验收，不是发布版本，也没有启动窗口。

- 源提交：`89d5ad8ca1f97a58423dec70f3cdeb6bc5ab2ac0`（主线工作区干净）
- 副本：`src/.state/gui-smoke/20261029-210710-414-36eaf8/`
- BuildIdentity：`gui-acceptance.20261029-210710-414-36eaf8+89d5ad8ca1f97a58423dec70f3cdeb6bc5ab2ac0`
- 状态：`prepared-not-run`，35 项待验收
- EXE SHA-256：`AC07311CFE24CD63E72F8722E615A8C33ECDFC1A827604947555A355E675FA98`
- App DLL SHA-256：`02E52A9A145BC0BAD91366F36D0A672FE9A00A59AAC9A50492D38A912CF6637A`
- 初始布局：四窗格；每窗格含 5 个样本文件和空文件夹，另有重命名、剪贴板、跨窗格拖放、搜索、QDR、Program Starter、10,000 项递归大小等隔离夹具。
- 新增验收：Ctrl+Alt+1…4 窗格聚焦且不干扰现有数字快捷键；快速链接文件夹普通单击与 Ctrl+单击的窗格/窗口语义及文件/程序链接行为。

准备命令为 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`。清单确认源提交与 EXE/DLL SHA-256，源码工作区干净；`ExplorerDirs.App` 当前没有运行进程。没有启动窗口、创建 ExplorerBrowser、发送按键或鼠标、操作剪贴板，或修改真实注册表。

35 项仍全部待可见验收；构建和清单核验不代表产品 GUI、Shell 菜单、物理输入或真实 Q-Dir 互操作已通过。该候选包含“复制所选项目完整路径”及其 Shell 选区路径读取验证提交；实际菜单、剪贴板内容及复制后的键盘焦点仍待验收。
