# 2026-10-28 main 主目录隔离候选：当前，已准备未运行

这是当前唯一验收候选。它从主目录当前干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-045919-195-847a27`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，28 项待验收。
- 源码提交：`1ddd43a6aa759d6f9de185cf47b608b994e70c03`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-045919-195-847a27+1ddd43a6aa759d6f9de185cf47b608b994e70c03`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`56C6B7293784F30289AEF0AA1595103BA44DF46F38E5BE9A226E0B5B8927EB25`。
- App DLL SHA-256：`FD65FB4588A58436F1331F07FDD2C996D7AAD1A7C95BEBE17880C3FBE2E4820E`。

## 准备验证

运行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与清单一致，候选源码提交为 `1ddd43a`；当前候选进程数为 0。没有启动窗口、创建 Shell view、加载 UI 自动化或操作键鼠。新增的 Explorer Shell 重启验收必须在保存工作并明确授权的桌面会话中执行；不会对普通隔离候选暗中重启用户桌面。

## 已替换的前一候选

`20261028-045534-826-74a861` 对应源码提交 `50cb324`，含 28 项待验收。当前候选从最新主分支提交重新构建，替代该副本；旧副本没有删除，但不再是当前验收版本。更早的 `20261028-045252-741-88d8d1` 对应 `89116c8`，也已被替代。
