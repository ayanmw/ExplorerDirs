# 2026-10-28 main 主目录隔离候选：当前，已准备未运行

这是当前唯一验收候选。它从主目录的干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-045534-826-74a861`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，28 项待验收。
- 源码提交：`50cb324c9393271785f50b31eb8568f4b6f063d5`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-045534-826-74a861+50cb324c9393271785f50b31eb8568f4b6f063d5`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`86FC24A57915E6FDBEBCB8EBF52B5DE714D428E24ABC0D67FFFDA301007A8BAA`。
- App DLL SHA-256：`65CA7B6D42CB604489DFC4DBF33D473637DE74E249DC0F428BA32C26368A93A1`。

## 准备验证

运行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与清单一致，源码提交仍为 `50cb324`；候选 EXE 当前运行数为 0。没有启动窗口、创建 Shell view、加载 UI 自动化或操作键鼠。新增的 Explorer Shell 重启验收必须在保存工作并明确授权的桌面会话中执行；不会对普通隔离候选暗中重启用户桌面。

## 已替换的前一候选

`20261028-045252-741-88d8d1` 对应源码提交 `89116c8`，当时有 27 项待验收。新候选加入 Explorer Shell 重启安全验收项后取代它；旧副本没有删除，但不再是当前验收版本。
