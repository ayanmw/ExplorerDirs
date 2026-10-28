# 2026-10-28 main 主目录隔离候选：已准备，未运行

这是当前唯一验收候选。它从主目录的干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-045252-741-88d8d1`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，27 项待验收。
- 源码提交：`89116c8f8832ed63c392b3ffc4ae97c7a4f6f28c`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-045252-741-88d8d1+89116c8f8832ed63c392b3ffc4ae97c7a4f6f28c`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`6F2389A3AA088F3E82EE213BEA62BCE35BE135899318B69592523BBB91E29077`。
- App DLL SHA-256：`B2C5DFE2902A3F19FA4A714A2B47F09F9E83272A2088F6C385734C1A0AAE4A20`。

## 准备验证

运行 `pwsh -NoProfile -File src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与清单一致，源码提交仍为 `89116c8`；候选 EXE 当前运行数为 0。没有启动窗口、创建 Shell view、加载 UI 自动化或操作键鼠。该准备结果不代表 27 项可见验收已通过。
