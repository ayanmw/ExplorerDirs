# 2026-10-29 17:05 main 主目录隔离候选：已准备未运行

这是当前最新的隔离 GUI 验收候选，基于干净主线 `17b6471`。独立 profile 和验收夹具保存在 `src/.state`；它不是日常使用 release，也没有自动启动。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261029-170543-378-b78cd8`。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，32 项待验收。
- 源码提交：`17b6471f5ea52820df453803747afcca291ddece`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261029-170543-378-b78cd8+17b6471f5ea52820df453803747afcca291ddece`。
- 初始布局：四窗格，编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`627390353EA63D6505DE043EB1DB9A2B47B652871F446AB5B5FD476ADA6D9F8F`。
- App DLL SHA-256：`03C69E9DE2840BD7828D0AEF85E42BD87AD560869F9B94BB16C4B171928976F0`。

## 准备结果与边界

`src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。构建出现两条 `NU1900` 警告，因为 NuGet 漏洞数据服务不可达；发布成功。复核了清单中的源提交、干净状态、BuildIdentity、EXE/DLL 哈希及 32 项待验收数。候选进程数为 0；PrepareOnly 在加载 UI Automation 及任何窗口操作前退出，没有创建 ExplorerBrowser、操作键鼠、剪贴板或真实注册表。

本候选覆盖标签栏 Explorer 菜单与空白区右键、列值筛选、Shell 文件夹中键开标签以及文件夹入口只读预览等待验收项。准备完成不代表这些可见行为已通过。真实 Shell 菜单、系统文件夹入口启用/停用仍需集中桌面验收和用户后续显式决定。
