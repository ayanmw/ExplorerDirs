# 2026-10-28 main 主目录隔离候选：当前，已准备未运行

这是当前唯一验收候选。它从主目录当前干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-050319-528-f5d4c8`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，28 项待验收。
- 源码提交：`3af17be29ab2003d526b0300dc6800d691c842a4`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-050319-528-f5d4c8+3af17be29ab2003d526b0300dc6800d691c842a4`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`CAC2AAD0AEDBFEA408B0EA9AF64D2D7BAA76035E1D5813D6BFE28CE8037DB799`。
- App DLL SHA-256：`1CC5ABA888070DEE3B61B607A1C70033D66252ADCBD5962F5EA5083CCA363B03`。

## 准备验证

运行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与清单一致，候选源码提交为 `3af17be`；当前候选进程数为 0。没有启动窗口、创建 Shell view、加载 UI 自动化或操作键鼠。新增的 Explorer Shell 重启验收必须在保存工作并明确授权的桌面会话中执行；不会对普通隔离候选暗中重启用户桌面。

## 已替换的前一候选

`20261028-045919-195-847a27` 对应源码提交 `1ddd43a`，含 28 项待验收。当前候选从包含 Explorer 重启目标映像校验的最新主分支提交重新构建，替代该副本；旧副本没有删除，但不再是当前验收版本。更早的 `20261028-045534-826-74a861`（`50cb324`）及 `20261028-045252-741-88d8d1`（`89116c8`）也已被替代。
