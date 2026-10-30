# 2026-10-31 主线集中验收候选

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；仅构建并准备隔离副本与夹具，没有启动应用、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`b93a9e7076bbd1d25d35f1a1c7a0490ce18b44d6`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-212651-032-233d73/`。
- BuildIdentity：`0.6.0-preview.20261030-212651-032-233d73+b93a9e7076bbd1d25d35f1a1c7a0490ce18b44d6`。
- 状态：`prepared-not-run`，44 项待验收，初始四窗格布局。
- EXE SHA-256：`8A46A7747C65661A1D5836F0A6DE07175183D6E67BFFB90AF1F04A8DEE507D1A`。
- App DLL SHA-256：`EC02E51C2E4BA2474260601BC7342D4E344B46D97DED42A91CF9BEE9206081FC`。
- EXE/DLL 哈希及待验收清单可在副本的 `preparation.json` 核对。副本路径、隔离 profile 和夹具身份均由清单记录。

## 本次验收重点

- 普通文件系统夹具按 F5，核对外部新增文件、空白时长、选区/焦点和滚动位置；搜索/虚拟 Shell 位置同样通过当前 `IShellView.Refresh()` 刷新，并确认暂缓绘制期间鼠标/键盘目标与可见行一致。
- 查看方式切换只观察文件行重排、空白、焦点、选区和滚动；不应让路径、标签、工具栏等窗格 chrome 一同闪动。
- View → Soft-Refresh 默认关闭。仅在隔离 profile 打开后，对比 F5、外部新增文件、复制/重命名时已有行、新行可见性与排序；关闭后按 F5 验证目录收敛，并检查全局范围、新标签继承和重启持久化。该实验不预设与 Q-Dir 行为等价。
- 其余验收覆盖多窗格焦点/文件操作、标签复制与克隆、主题与原生菜单、列值筛选、Q-Dir `.qdr` 互操作、文件夹入口及其他待验收项目；逐项以 `preparation.json` 的 `PendingAcceptance` 为准。

候选准备成功，哈希与 `preparation.json` 一致。候选尚未运行，因此没有可见 UI 通过结论；后续可在一次集中 UI 验收中使用此副本。旧候选仍保留在 `src/.state/gui-smoke/` 作为历史记录。
