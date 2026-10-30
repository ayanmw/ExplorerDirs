# 2026-10-31 主线集中验收候选

此候选供后续集中可见验收使用，不是 `releases/` 日常预览版。由 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 生成；仅构建并准备隔离副本与夹具，没有启动应用、创建 ExplorerBrowser、发送输入或触碰正式 profile。

## 固定身份

- 源提交：`7366323386e6f0d12d3c23c34b9fa355b8c022a3`；准备时主线工作区干净。
- 副本：`src/.state/gui-smoke/20261030-170136-670-23c890/`。
- BuildIdentity：`0.6.0-preview.20261030-170136-670-23c890+7366323386e6f0d12d3c23c34b9fa355b8c022a3`。
- 状态：`prepared-not-run`，41 项待验收，初始四窗格布局。
- EXE SHA-256：`64AC1E61FD697A639ECBE0D1D52BB220E9A099E11CE282A9CD44DF7D9940B2C9`。
- App DLL SHA-256：`F63681679326DC26182C141366BDAB3D3B09171DB976F9A90AF592CD593A4B6C`。
- manifest 内哈希与副本文件独立计算结果一致；完整夹具、隔离 profile 与验收身份见副本内 `preparation.json`。

## 本次验收重点

- 在已填充目录按 F5，观察旧行、空白时长、新文件到达、选区/焦点恢复和滚动位置；刷新期间验证鼠标及键盘目标与屏幕显示一致。慢目录和虚拟 Shell 位置单独记录。
- 切换详细信息、列表、图标、平铺及内容查看方式，观察仅文件列表重排时的空白、焦点、选区和滚动位置。
- 其余验收覆盖多窗格焦点/文件操作、标签复制与克隆、主题与原生菜单、列值筛选、Q-Dir `.qdr` 互操作、文件夹入口及其他待验收项目；逐项以 `preparation.json` 的 `PendingAcceptance` 为准。

候选准备成功且哈希复核通过。未运行候选，因此没有可见 UI 通过结论。
