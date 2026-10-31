# 2026-10-31 主线集中验收候选

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；仅构建并准备隔离副本与夹具，没有启动应用窗口、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`5b3dbb583a18bfd465b074762352f8c2821f3a3c`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261031-030146-548-62ca97/`。
- BuildIdentity：`0.6.0-preview.20261031-030146-548-62ca97+5b3dbb583a18bfd465b074762352f8c2821f3a3c`。
- 状态：`prepared-not-run`，47 项待验收，初始四窗格布局。
- EXE SHA-256：`8952C631FCA9A071A8EAF66D71F624D8F551FC9BAE3432F71AB8DFD22C54BB02`。
- App DLL SHA-256：`FAC4A260C72661E12E82FCA076F9B29C1768151CF375519A10148CA29182B7F3`。
- profile 预置 1.5 秒目录树悬停展开延时。
- 哈希、隔离 profile、手动操作夹具和待验收清单见副本 `preparation.json`；启动身份由唯一 BuildIdentity 区分。

## 集中验收重点

- F5 当前视图刷新、连按刷新、搜索/虚拟位置和查看方式切换：记录空白或重排时长、Shell 是否逐步绘制、鼠标键盘命中目标、选区、焦点和滚动；不冻结重绘时以可见结果验收。
- 多窗格键盘焦点、粘贴/删除后方向键、F2 编辑导航键、正反 Tab、Ctrl+滚轮、跨窗格拖放，以及复制标签与克隆视图的选区/历史恢复。
- 在临时 Q-Dir 工作区副本观察 Tab Group、Move/Copy to new Tab Group 与 Use Tab Groups 的作用域、切换和 `.qdr` 保存恢复语义；不得覆盖原配置或工作区。
- 目录树悬停展开的六个延时档、文件夹命中与放置目标；改档后重启确认便携设置保存，仅用隔离夹具。
- 深浅主题下文件区原生菜单边界、标签复制期间绘制、滚动条端点、目录树图标和主题、窗口多屏恢复。
- Q-Dir `.qdr` 导入/导出互操作、颜色元数据保真、程序项导入和文件夹入口预览；保持注册表隔离，不启用真实系统入口。
- 列值筛选箭头、多条件与逐标签持久化，F9 递归大小摘要，以及 Soft-Refresh 默认关闭后的新旧行和排序行为。
- 其余项目以 `preparation.json` 的 `PendingAcceptance` 为准。发现不匹配时先记录 BuildIdentity、操作和结果，再决定路线变更；`.qdr` 预览格式不匹配时文件的列表处置待 Q-Dir 验收后决定。

候选由当前主线 `-PrepareOnly` 重建，准备成功，EXE/DLL 哈希与 `preparation.json` 一致。清单明确包含 F5 当前视图刷新、连续 F5、搜索/虚拟位置、Soft-Refresh 对照和 Q-Dir Tab Group 语义观察；候选尚未运行，没有可见 UI 通过结论。后续集中验收应从此隔离副本启动，不使用 `src` 调试程序或日常 profile。旧副本仍保留在 `src/.state/gui-smoke/` 作为历史记录。
