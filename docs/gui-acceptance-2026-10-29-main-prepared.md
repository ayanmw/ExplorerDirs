# 2026-10-29 main 主目录隔离候选：当前，已准备未运行

这是当前唯一验收候选。它从主目录干净 `main` 提交构建，独立 profile 与验收夹具都在 `src/.state`；不是日常使用 release。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-160859-753-9b039a`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，29 项待验收。
- 源码提交：`bae8ab9beea3e9a2c79ca0d2337639a13dd7d5e5`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-160859-753-9b039a+bae8ab9beea3e9a2c79ca0d2337639a13dd7d5e5`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`A3774E2090F395CDF508F8A60375258D7D4C784EDF25188212F11DAB921B037C`。
- App DLL SHA-256：`D9F8F804E5384EDB25333965D900ED09B1D516C589B07993423569CE5BDE3A1F`。

## 准备验证

运行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE/DLL SHA-256 均与 `preparation.json` 一致；候选源码为干净提交 `bae8ab9`，含 29 项待验收。脚本在加载 UI Automation 及窗口操作之前退出，没有启动窗口、创建 Shell view 或操作键鼠。候选新增标题栏模式验收：切换四种模式，核对活动窗格上下文、唯一选区条件及隔离 profile 重启后的设置保存。NuGet 漏洞数据源不可访问，出现 `NU1900` 警告；还原与发布仍成功。

该候选供后续集中验收使用，不会自动启动或替换 `releases` 下的日常预览版本。
