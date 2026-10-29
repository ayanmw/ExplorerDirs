# 2026-10-30 主线集中验收候选（最新）

这是后续集中可见验收用的隔离副本，不是发布版本，也没有启动窗口。候选加入了地址栏 F7/F8、“此电脑”按钮 Tab 顺序，以及 Alt+K/Alt+V 文件操作别名验收。

## 固定身份

- 源提交：`bf49df98045fae605e50e89a29eb1c44c085c781`，主线工作区干净。
- 副本：`src/.state/gui-smoke/20261029-221003-686-b08353/`。
- BuildIdentity：`gui-acceptance.20261029-221003-686-b08353+bf49df98045fae605e50e89a29eb1c44c085c781`。
- 状态：`prepared-not-run`，36 项待验收。
- EXE SHA-256：`290FA2AC3E6310B6229C0669F74F04D8A020423C373C016B1B2B1810BB4D53AA`。
- App DLL SHA-256：`C7572E73F143D144A23BD986E04D7B0895C7E8DDA8D7C1E6672F463A89F5B12B`。
- 初始布局为四窗格，使用副本自己的 profile 和隔离文件夹；包含 10,000 个递归大小测试文件及跨窗格复制/移动样本。

## 本次更新与验证范围

验收清单包含 `Ctrl+Shift+C`：文件视图有焦点时复制不带引号的完整路径，并检查地址栏/搜索输入仍可正常输入；并为 `Alt+K`/`Alt+V` 配有单独的复制/剪切源文件，验证粘贴结果与源文件状态。新增 F7 路径层级、F8/“此电脑”按钮共用动态驱动器菜单，以及 Tab/Shift+Tab 地址按钮顺序检查。当前源提交还包含目录 watcher 错误类型诊断；此项需真实触发 watcher 错误才能验证，未列入普通 GUI 步骤，也未在准备阶段触发。另需检查标签栏“使用 Explorer 浏览”实际菜单位置和系统 Explorer 是否打开当前标签目录；定向 smoke 只证明启动目标使用 Windows 目录中的 Explorer 完整路径且参数原样保留。

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 准备副本成功，App Release 构建成功。独立复核确认源提交匹配、`SourceWorkingTreeDirty=false`、36 项待验收及 EXE/DLL 哈希匹配；四个窗格夹具和 Alt+K/Alt+V 测试文件均已生成，ExplorerDirs 进程数为 0。准备流程未启动窗口、创建 ExplorerBrowser、发送输入、改动剪贴板或真实注册表。

候选仅供之后用户明确安排的集中验收；构建与准备结果不代表可见交互、Shell 菜单或真实 Q-Dir 互操作通过。此前候选仍保留在各自 `src/.state/gui-smoke/` 目录，不覆盖旧副本。
