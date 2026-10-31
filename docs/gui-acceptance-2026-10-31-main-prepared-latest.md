# 2026-11-01 主线集中验收候选（当前指针）

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；只构建并准备隔离副本与夹具，没有启动应用窗口、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`9174b869e2a375f49245179b499816c99c14791e`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261031-194856-328-7b2cc3/`。
- BuildIdentity：`0.6.0-preview.20261031-194856-328-7b2cc3+9174b869e2a375f49245179b499816c99c14791e`。
- 状态：`prepared-not-run`，47 项待验收，初始四窗格布局。
- 准备时间：`2026-10-31T19:49:07Z`。
- EXE SHA-256：`5E0F848AE5036643D318850C375310A1E3EB0E894AE71A587E2252FBEA73BF55`。
- App DLL SHA-256：`B32987D237A89272B2654CB6D4145563BCB3C8AF3ECFD32AF77CF51EA3798DA1`。
- 隔离目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261031-194856-328-7b2cc3\`；操作夹具、profile 与待验收清单均位于副本内。

## 集中验收重点

- F5 当前视图刷新、连按刷新、搜索/虚拟位置和查看方式切换：记录空白或重排时长、Shell 是否逐步绘制、鼠标键盘命中目标、选区、焦点和滚动。
- 多窗格键盘焦点、粘贴/删除后方向键、F2 编辑导航键、正反 Tab、Ctrl+滚轮、跨窗格拖放，以及复制标签与克隆视图的选区/历史恢复。
- 在临时 Q-Dir 工作区副本观察 Tab Group、Move/Copy to new Tab Group 与 Use Tab Groups 的作用域、切换和 `.qdr` 保存恢复语义；不得覆盖原配置或工作区。
- 目录树悬停展开、深浅主题、文件区原生菜单边界、标签复制期间绘制、滚动条端点、目录树图标/主题与窗口多屏恢复。
- Q-Dir `.qdr` 导入/导出互操作、颜色元数据保真，并对照观察扩展名 Color Filter / Highlight-Filter 是否为同一行为、关闭入口及 `.qdr` 保存语义；另验程序项导入和文件夹入口预览，保持注册表隔离，不启用真实系统入口。
- 列值筛选箭头、多条件与逐标签持久化，F9 递归大小摘要，以及 Soft-Refresh 默认关闭后的新旧行和排序行为。
- 其余项目以副本 `preparation.json` 的 `PendingAcceptance` 为准。`.qdr` 预览格式不匹配时文件的列表处置仍待 Q-Dir 验收后决定。

该候选尚未运行，没有任何可见 UI 通过结论。集中验收应从此隔离副本启动，不使用 `src` 调试程序或日常 profile。旧副本保留在 `src/.state/gui-smoke/` 供追溯。
