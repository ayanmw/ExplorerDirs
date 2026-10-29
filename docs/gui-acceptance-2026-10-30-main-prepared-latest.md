# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。

## 固定身份

- 源提交：`b9e90fdcd57232dbbe16fdf91aff06f4ab50c680`，主线工作区干净。
- 副本：`src/.state/gui-smoke/20261029-213324-889-62177d/`。
- BuildIdentity：`gui-acceptance.20261029-213324-889-62177d+b9e90fdcd57232dbbe16fdf91aff06f4ab50c680`。
- 状态：`prepared-not-run`，35 项待验收。
- EXE SHA-256：`12A3B9C548E8DE261EBBCCA33E242CDC666A9D96C9FCE9F8CA50E88F1CFB6F2D`。
- App DLL SHA-256：`328FFDF1F9D505FC9A1CBCF5200C1550386DBA6728810B53E4E9A104DD38AD94`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单包含 `Ctrl+Shift+C`：文件视图有焦点时复制不带引号的完整路径，并检查地址栏/搜索输入仍可正常输入。当前源提交还包含目录 watcher 错误类型诊断；此项需真实触发 watcher 错误才能验证，未列入普通 GUI 步骤，也未在准备阶段触发。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 准备副本成功。独立复核确认源提交匹配、`SourceWorkingTreeDirty=false`、35 项待验收及 EXE/DLL 哈希匹配；四个窗格夹具已生成，ExplorerDirs 进程数为 0。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后用户明确安排的集中验收；构建与准备结果不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。此前候选仍保留在各自 `src/.state/gui-smoke/` 目录，不覆盖旧副本。
