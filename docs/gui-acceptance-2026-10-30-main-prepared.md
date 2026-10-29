# 2026-10-30 主线集中验收候选

此副本仅用于后续集中 GUI 验收，不是发布版本，也没有启动窗口。

- 源提交：`c7dd4195150fd81f705003450bcaae8f6f947038`（主线工作区干净）
- 副本：`src/.state/gui-smoke/20261029-205458-371-110f60/`
- BuildIdentity：`gui-acceptance.20261029-205458-371-110f60+c7dd4195150fd81f705003450bcaae8f6f947038`
- 状态：`prepared-not-run`，34 项待验收
- EXE SHA-256：`F02C8A69D17F9A91594BF730277C4CCAEF49D43668A61CF647B54304598DAACE`
- App DLL SHA-256：`13382D42CD83CE94095EFF00E5259E9741399B5287E0165CDDAF1C149A8FB15D`
- 初始布局：四窗格；每窗格含 5 个样本文件和空文件夹，另有重命名、剪贴板、跨窗格拖放、搜索、QDR、Program Starter、10,000 项递归大小等隔离夹具。
- 新增验收：Ctrl+Alt+1…4 窗格聚焦且不干扰现有数字快捷键；快速链接文件夹普通单击与 Ctrl+单击的窗格/窗口语义及文件/程序链接行为。

准备命令为 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`。清单确认源提交与 EXE/DLL SHA-256，源码工作区干净；`ExplorerDirs.App` 当前没有运行进程。没有启动窗口、创建 ExplorerBrowser、发送按键或鼠标、操作剪贴板，或修改真实注册表。

34 项仍全部待可见验收；构建和清单核验不代表产品 GUI、Shell 菜单、物理输入或真实 Q-Dir 互操作已通过。
