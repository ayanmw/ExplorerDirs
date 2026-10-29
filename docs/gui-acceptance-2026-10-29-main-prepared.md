# 2026-10-29 main 主目录隔离候选：已被新候选取代

此历史候选已被 [2026-10-29 17:05 主线候选](gui-acceptance-2026-10-29-main-prepared-latest.md)取代。保留本页身份与记录作为历史，不再用于验收。

## 固定身份

- 目录：`D:\projects\explorer-dirs\src\.state\gui-smoke\20261028-171800-392-9d9985`（UTC 时间戳；本地日期为 2026-10-29）。
- 清单：候选目录中的 `preparation.json`；状态 `prepared-not-run`，29 项待验收。
- 源码提交：`fe48df98ef63c0d5845ca7bd64cf1ccbf76ca911`；`SourceWorkingTreeDirty=false`。
- BuildIdentity：`gui-acceptance.20261028-171800-392-9d9985+fe48df98ef63c0d5845ca7bd64cf1ccbf76ca911`。
- 布局：四窗格，初始布局编号 4；profile 位于候选副本 `app\profile\ExplorerDirs.ini`。
- EXE SHA-256：`7A1927F49C89D465555C0CAD40FB0D560E2B774736F929D5D2EEA28863BF2B2C`。
- App DLL SHA-256：`7C341A598B5F355566D0DD1B4FC0E72735FC9CB7AC38DB77340A0E7341A2B7D0`。
- `qdir-program-items.ini` SHA-256：`ED433EA6803E47D30CC85B117A839C2049BDA0C3F3D6A390A457641EDE4D99BC`；99 标签容量 `.qdr` SHA-256：`36B51EE070F97171E6B620F38951A43786D5B2E5E59A45CCEA1DF18FB665E533`。

## 准备验证

运行 `src/ExplorerDirs.GuiSmoke/run.ps1 -PrepareOnly` 成功。独立复核 EXE、App DLL、Program Starter 导入夹具及标签容量 `.qdr` 的 SHA-256 均与 `preparation.json` 或生成内容一致；候选源码是当前干净 `main` 提交 `fe48df9`，清单含 29 项待验收。脚本在加载 UI Automation 及窗口操作之前退出，没有启动窗口、创建 Shell view 或操作键鼠。该清单包括已新增的标题栏模式检查，以及本轮之后主线已实现但尚未人工验收的 Shell 选区恢复、文件夹入口和其它既有项目；它不是 `releases` 下的日常预览版。NuGet 漏洞数据源不可访问，出现 `NU1900` 警告；还原与发布仍成功。

该候选供后续集中验收使用，不会自动启动或替换 `releases` 下的日常预览版本。
