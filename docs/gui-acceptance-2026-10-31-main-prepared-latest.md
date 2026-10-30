# 2026-10-31 主线集中验收候选

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；仅构建并准备隔离副本与夹具，没有启动应用、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`6257656eb107b1b96dcec76c5bd1965327ad361b`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-214436-370-cda2af/`。
- BuildIdentity：`0.6.0-preview.20261030-214436-370-cda2af+6257656eb107b1b96dcec76c5bd1965327ad361b`。
- 状态：`prepared-not-run`，45 项待验收，初始四窗格布局。
- EXE SHA-256：`425A6CFF59B0E25CF18F8EC4F999FED691F5B9E6B5638A66E841DC7DD7FB7502`。
- App DLL SHA-256：`57DA9655A17444EDFD9A9DD79886F51486DB27BB5DB0D6CAF5EF9547D20A4A5A`。
- EXE/DLL 哈希及待验收清单可在副本的 `preparation.json` 核对。副本路径、隔离 profile 和夹具身份均由清单记录。

## 本次验收重点

- 普通文件系统夹具按 F5，核对外部新增文件、空白时长、选区/焦点和滚动位置；确认 Shell 在枚举时如何绘制行，以及键鼠目标是否对应可见行。产品不再用 `SetRedraw(FALSE)` 冻结列表。
- 上一轮 F5 尚在刷新时立即再按一次，确认两轮最终都显示最新外部文件；记录空白间隔，并核对键鼠命中、选区、焦点和滚动位置，不预设旧行会被保留。
- 查看方式切换只观察文件行重排、空白、焦点、选区和滚动；不应让路径、标签、工具栏等窗格 chrome 一同闪动。
- 搜索/虚拟位置按 F5 时观察当前 Shell 原生绘制过程、空白时间和输入命中，不再检查应用保帧区间。
- View → Soft-Refresh 默认关闭。仅在隔离 profile 打开后，对比 F5、外部新增文件、复制/重命名时已有行、新行可见性与排序；关闭后按 F5 验证目录收敛，并检查全局范围、新标签继承和重启持久化。该实验不预设与 Q-Dir 行为等价。
- 其余验收覆盖多窗格焦点/文件操作、标签复制与克隆、主题与原生菜单、列值筛选、Q-Dir `.qdr` 互操作、文件夹入口及其他待验收项目；逐项以 `preparation.json` 的 `PendingAcceptance` 为准。

候选准备成功，哈希与 `preparation.json` 一致。候选尚未运行，因此没有可见 UI 通过结论；后续可在一次集中 UI 验收中使用此副本。旧候选仍保留在 `src/.state/gui-smoke/` 作为历史记录。
