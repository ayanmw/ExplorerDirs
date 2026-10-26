# Explorer Dirs 0.6.0 临时预览 temp.27

## 本版新增

- Q-Dir 风格快速链接：`Ctrl+Q` 打开独立菜单；可添加当前目录，或打开 profile 的 `QuickLinks` 目录手工管理 `.lnk` 文件夹、程序和文件快捷方式。
- 文件系统目录链接在活动窗格中导航；程序/文件快捷方式交由 Windows Shell 打开。收藏夹和 `.qdr` 工作区仍保持独立。
- 隔离预览 profile 复制 Quick Links 中第一层 `.lnk` 文件，并排除无关文件和嵌套目录。

## 验证

- `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release --no-restore`：通过，包含快捷方式 Unicode 路径、重复名称、盘符根目录和程序目标。
- `dotnet build src/ExplorerDirs.App -c Release --no-restore`：通过，0 警告、0 错误。
- `pwsh -NoProfile -File scripts/publish-smoke.ps1`：通过；只使用 fixture，不生成其他真实发行包。
- 未启动 GUI。菜单外观、焦点、快捷方式图标、Shell 程序启动结果以及 Q-Dir 实际互操作均待可见验收。

> temp.28 在此基础上补充虚拟 Shell 文件夹 PIDL 快捷方式支持；请优先预览更新后的 temp.28。

## 使用

运行 `app/ExplorerDirs.App.exe`。本预览使用隔离 profile，不写入源码目录的共享 profile。快速链接位于该 profile 的 `QuickLinks` 子目录；初次使用时从 `Ctrl+Q` 菜单添加目录，或选择“管理快速链接…”并在打开的目录中创建 `.lnk` 文件。

SHA-256 以发行包内 `app/SHA256.txt` 为准。
