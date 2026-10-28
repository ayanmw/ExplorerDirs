# 2026-10-27 worktree 隔离候选：历史记录，已移除

本文件记录的二进制来自已移除的 `windows-accent-theme` worktree，旧候选目录不再作为当前测试版本。请改用[2026-10-28 main 主目录候选](gui-acceptance-2026-10-28-main-prepared.md)；旧版本号、哈希与准备结果仅作历史证据。

这是开发验收副本，不是日常使用 release。替代 [62274bc 旧候选](gui-acceptance-2026-10-27-prepared.md)作为下一次产品验收基线；没有启动本候选、创建 Shell view、加载 UIA 或操作键鼠。

## 固定身份

- 目录：`D:\projects\explorer-dirs-worktrees\windows-accent-theme\src\.state\gui-smoke\20261027-063307-598-59479f`。
- 清单：目录中的 `preparation.json`；状态 `prepared-not-run`，21 项待验收。
- SourceCommit：`5a6f3d2ddefe6b2600845122d69de146054962e4`；SourceWorkingTreeDirty：`false`。
- BuildIdentity / DLL ProductVersion：`gui-acceptance.20261027-063307-598-59479f+5a6f3d2ddefe6b2600845122d69de146054962e4`。
- EXE SHA-256：`1D21823C4619170C9513905EE3F977252625361E43F1F6CB64AEAA9A95DD466D`。
- DLL SHA-256：`17EA9E64B6388722799955E671C72ABC2CF064E5C169F00EAC6C20A833C476EF`。
- 准备脚本 SHA-256：`BE78F4C1CEF65ED05807FB49431177777B04DB3CB374E037ED48F71D5BCA3C74`。

执行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly -CaptureFocusTrace` 成功。该分支在 UIA 与 Start-Process 之前返回。随后独立核对版本及两份文件哈希均匹配；四个 INI 路径均指向本候选夹具，每窗格有五个 sample 文件及空文件夹，独立递归夹具有 10,000 个文件。进程查询没有该 EXE 实例，`focus-trace.log` 与 `performance.json` 均不存在，符合未运行状态。

## 与旧候选相比

按 `git log 62274bc..5a6f3d2 -- src/ExplorerDirs.App` 核对，共包含 11 批产品修复：

- 完整视图恢复失败时保护旧配置；快速筛选附件失败回滚、临时分组保存保护、新原生分组优先。
- 应用图标缓存限制为 256 项；这不是进程内存收益证明。
- 文件键只在 Shell 明确未处理时补发。
- 工作区/QDR 和文件视图导出关闭后结束 UI 等待；照片打印防重复与失活预览保护。
- 隐藏窗格不执行迟到悬停切换；退休递归扫描失败不清除新结果。

NativeProbe 的独立历史、菜单代次、流游标、关闭与保存冲突修复虽在相同源码提交范围内，未编入产品宿主替换；本候选仍使用 ExplorerBrowser，不是原生视图迁移版本。

## 下一次验收

仍须用户重新授权桌面并确认其他任务让出。沿用旧记录的 P0 优先顺序：四窗格文件操作与方向键、F2 编辑、正反焦点遍历和背景菜单；再验快速导航、独立视图状态、生命周期、多屏、QDR 和固定负载内存。

本候选清单另外明确：筛选中改分组与显式无分组、拖动悬停后隐藏窗格、慢 IO 中关闭单个窗口。后者只用可丢弃文件与可控慢目标；确认的导出允许在进程仍存活时完成，不把最后窗口退出视为等待写入承诺。照片打印还需使用可丢弃图片验证加载期间重复调用、切换应用及关闭；不发送真实打印任务。

全部可见结果仍为未运行。若源码或二进制改变，重新准备并记录哈希；不要测试途中覆盖本候选。日常 release 仍未更新，不应将此 `src/` 副本用于日常文件管理。
