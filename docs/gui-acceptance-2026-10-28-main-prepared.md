# 2026-10-28 main 主目录隔离候选：当前，已准备未运行

这是当前唯一验收候选。它从主目录当前干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-051246-344-bcb87e`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，28 项待验收。
- 源码提交：`07942e66e3d443ee1010359987fc0803e18f7a7b`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-051246-344-bcb87e+07942e66e3d443ee1010359987fc0803e18f7a7b`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`62262A6E078E8AD24B6B9E7A4FB6A7E08B111FDD33D7AD48EA1797EC0C33673E`。
- App DLL SHA-256：`5C39BD34C71116D110EFC5016F6B2BC1C5C75ABE74CEDDE58F91A9999317087F`。

## 准备验证

运行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与清单一致，候选源码提交为 `07942e6`；当前候选进程数为 0。没有启动窗口、创建 Shell view、加载 UI 自动化或操作键鼠。本版包含 Explorer Shell 自动恢复进程映像校验；对应菜单验收仍必须在保存工作并明确授权的桌面会话中执行，不会对普通隔离候选暗中重启用户桌面。

## 已替换的前一候选

`20261028-045919-195-847a27` 对应源码提交 `1ddd43a`，含 28 项待验收。当前候选从包含 Explorer 重启目标映像校验的最新主分支提交重新构建，替代该副本；旧副本没有删除，但不再是当前验收版本。更早的 `20261028-045534-826-74a861`（`50cb324`）及 `20261028-045252-741-88d8d1`（`89116c8`）也已被替代。
