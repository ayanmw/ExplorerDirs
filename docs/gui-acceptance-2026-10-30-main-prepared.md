# 2026-10-30 主线集中验收候选

此副本仅用于后续集中 GUI 验收，不是发布版本，也没有启动窗口。

- 源提交：`f51a681f9f800f74bfae4dddd5713566b8987fe3`（主线工作区干净）
- 副本：`src/.state/gui-smoke/20261029-204650-234-3f3951/`
- BuildIdentity：`gui-acceptance.20261029-204650-234-3f3951+f51a681f9f800f74bfae4dddd5713566b8987fe3`
- 状态：`prepared-not-run`，32 项待验收
- EXE SHA-256：`5358AEDDCFBF626C028E78390EC928420D9970E79F482B24D2079EFA0DC3886A`
- App DLL SHA-256：`31381F732B9B03C7197342329BAB2C4C682D78C8A7BBD817EEECBA32DDA8DE35`
- 初始布局：四窗格；每窗格含 5 个样本文件和空文件夹，另有重命名、剪贴板、跨窗格拖放、搜索、QDR、Program Starter、10,000 项递归大小等隔离夹具。

准备命令为 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly`。`PrepareOnly` 在加载 UI Automation 和窗口操作代码前返回。本次验证了清单源提交与 EXE/DLL 哈希一致、源工作区标记干净、四窗格夹具数量正确、当前没有 `ExplorerDirs.App` 进程。没有启动窗口、创建 ExplorerBrowser、发送按键或鼠标、操作剪贴板，或修改真实注册表。

32 项仍全部待可见验收；构建和清单核验不代表产品 GUI、Shell 菜单、物理输入或真实 Q-Dir 互操作已通过。
