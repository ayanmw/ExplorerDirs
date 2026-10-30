# 2026-10-31 主线集中验收候选

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；仅构建并准备隔离副本与夹具，没有启动应用窗口、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`6b3efa16bf72372d1b5eec4cca11d2d859c77eee`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-221237-787-06b732/`。
- BuildIdentity：`0.6.0-preview.20261030-221237-787-06b732+6b3efa16bf72372d1b5eec4cca11d2d859c77eee`。
- 状态：`prepared-not-run`，45 项待验收，初始四窗格布局。
- EXE SHA-256：`9B42C749FD21D28FE6FF9E17C7C78465E52F35E0F0AA159F46846EB043D09487`。
- App DLL SHA-256：`A04D73104592239533B5A9EAE5F4DCAD274B87C6C9CA16C4E5415599F2C3629C`。
- 哈希、隔离 profile、手动操作夹具与待验收清单见副本 `preparation.json`；启动身份由唯一 BuildIdentity 区分。

## 集中验收重点

- F5 当前视图刷新、重复刷新、搜索/虚拟位置刷新和查看方式切换：记录文件区空白/重排、旧画面、鼠标键盘命中、选区、焦点及滚动；Shell 原生重绘效果只能由可见验收确认。
- 多窗格键盘焦点、粘贴/删除后方向键、F2 编辑导航键、正反 Tab、Ctrl+滚轮、跨窗格拖放，以及复制标签与克隆视图的选区/历史恢复。
- 深浅主题下文件区原生菜单边界、标签复制期间绘制、滚动条端点、目录树图标与主题、窗口多屏恢复。
- Q-Dir `.qdr` 导入/导出实际互操作、颜色元数据保真、程序项导入、文件夹入口预览；清单要求对注册表保持隔离，不启用真实系统入口。
- 列值筛选箭头和多条件持久化、F9 递归大小摘要、Soft-Refresh 默认关闭后的新旧行与排序行为。
- 其余所有 45 项以 `preparation.json` 的 `PendingAcceptance` 为准；若实际操作与夹具不符，先记录现象和候选 BuildIdentity。

候选准备成功，EXE/DLL 哈希与 `preparation.json` 一致。候选尚未运行，因此没有可见 UI 通过结论；后续集中验收应从这个副本启动，不使用 `src` 调试程序或日常 profile。原候选仍保留在 `src/.state/gui-smoke/` 作为历史记录。
