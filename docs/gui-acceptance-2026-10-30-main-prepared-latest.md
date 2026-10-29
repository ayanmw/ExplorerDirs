# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。

## 固定身份

- 源提交：`41c1a6085cb3e5575313672879d3c0e87e9a1b59`，主线工作区干净。
- 副本：`src/.state/gui-smoke/20261029-212632-525-ee20a3/`。
- BuildIdentity：`gui-acceptance.20261029-212632-525-ee20a3+41c1a6085cb3e5575313672879d3c0e87e9a1b59`。
- 状态：`prepared-not-run`，35 项待验收。
- EXE SHA-256：`E9E8DAA470394FD39EE7FA916EE8EF69D413E823E4C69A0649B0B34CE6B48DA2`。
- App DLL SHA-256：`75BA67EEC9344B19D6F507D7E1B1637C8C051547A26C581D745CAC222A69F506`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单新增 `Ctrl+Shift+C`：文件视图有焦点时复制不带引号的完整路径，并检查地址栏/搜索输入仍可正常输入。其余清单继续覆盖文件区原生菜单、列值筛选、标签菜单、导航、会话、主题及系统入口。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 发布副本成功。独立复核确认源提交匹配、`SourceWorkingTreeDirty=false`、35 项待验收及 EXE/DLL 哈希匹配；没有运行 Explorer Dirs 进程。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后用户明确安排的集中验收；构建与准备结果不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。此前候选仍保留在各自 `src/.state/gui-smoke/` 目录，不覆盖旧副本。
