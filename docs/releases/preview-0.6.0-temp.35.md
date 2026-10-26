# Explorer Dirs 0.6.0 临时预览 temp.35

本版基于当前源码，使用独立 `app/profile/`。相较 temp.34，增加了可选的右键命中诊断，帮助区分鼠标点在列表项目还是列表背景；诊断不改变 Shell 菜单生成方式。

## 右键诊断

仅在启动进程前设置 `EXPLORER_DIRS_FOCUS_TRACE` 后写入诊断日志。日志记录命中屏幕坐标、目标分类和 UI Automation 控件类型祖先链，不记录控件名称、文件名、路径或值。可以在 PowerShell 中从 release 目录启动隔离副本：

```powershell
$env:EXPLORER_DIRS_FOCUS_TRACE = Join-Path $env:TEMP 'explorer-dirs-menu-hit.log'
& .\app\ExplorerDirs.App.exe
```

日志中的命中类型用于定位点击落在 `ListItem`/`DataItem` 还是列表背景。它不证明 Windows 最终创建了哪类标准菜单；菜单仍由原生 Shell 生成。

## 验证与限制

- App 和 SettingsSmoke Release 构建通过，均 0 警告、0 错误；`--shell-menu-target-smoke` 通过；发布隔离 smoke 通过。
- 产品版本：`0.6.0-preview.temp.35+77495f5d72a4739c2bb33a21d021328294f728c0`。
- EXE SHA-256：`A97B5B6F53D78366C0C3F59552C98599885A09720ECDBCCDC34D97F9820AF053`
- DLL SHA-256：`C3A8A992F39FC06F345E14128E5280838DFD0F7852007E9DCB735B8D491DB189`
- 未启动 GUI、ExplorerBrowser、Q-Dir 或 Excel。真实 UI Automation 命中、空白处标准菜单类型及菜单焦点仍待可见验收。
- 依赖 .NET 10 Windows Desktop Runtime。保留 temp.34 供回退。
