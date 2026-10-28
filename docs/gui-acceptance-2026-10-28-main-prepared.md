# 2026-10-28 main 主目录隔离候选：当前，已准备未运行

这是当前唯一验收候选。它从主目录当前干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-051842-945-c3b3a7`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，28 项待验收。
- 源码提交：`f3a04e3c28a26729a18e3ede493082c5315f21de`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-051842-945-c3b3a7+f3a04e3c28a26729a18e3ede493082c5315f21de`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`5C8D218EDA2803312D6E7F158A50D78EAD1EDE1E6A8AB3833B1254B713BEADF6`。
- App DLL SHA-256：`BDADA5CBB02A03C66322326BCDE97D053AE8FA7F60E92928DA38DF5AD5CAC567`。

## 准备验证

运行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与清单一致，候选源码提交为 `f3a04e3`；当前候选进程数为 0。没有启动窗口、创建 Shell view、加载 UI Automation 或操作键鼠。本候选包含 Explorer 自动恢复及 fallback 后的桌面 Shell 身份验证；相关菜单验收仍须在保存工作并明确授权的桌面会话中执行。

## 已替换的前一候选

`20261028-051246-344-bcb87e` 对应源码提交 `07942e6`，含 28 项待验收；该版本的 Explorer fallback 仅确认启动进程，当前候选已补上有界 Shell 恢复确认。更早候选 `20261028-050319-528-f5d4c8`（`3af17be`）、`20261028-045919-195-847a27`（`1ddd43a`）、`20261028-045534-826-74a861`（`50cb324`）及 `20261028-045252-741-88d8d1`（`89116c8`）均已被替代，保留为历史副本。
