# Explorer Dirs 0.6.0 临时预览 temp.28

## 本版新增

- 基于 temp.27 的 Q-Dir 风格 Quick Links 增加虚拟 Shell 文件夹快捷方式支持。
- 快捷方式读取优先解析其 Shell PIDL，并复用应用的 Shell 身份校验；只有确认是文件夹的位置才在活动窗格导航。因此“此电脑”等虚拟位置可以留在当前多窗格工作流中。
- 程序和普通文件快捷方式仍交给 Windows Shell 启动；收藏夹与 `.qdr` 工作区保持独立。

## 验证

- `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release --no-restore`：通过；覆盖 Unicode 文件夹、重复链接名、盘符根目录、程序目标和 `shell:MyComputerFolder` PIDL 往返。
- `dotnet build src/ExplorerDirs.App -c Release --no-restore`：通过，0 警告、0 错误。
- `pwsh -NoProfile -File scripts/publish-smoke.ps1`：通过，验证独立 profile 只复制第一层 `.lnk` 文件。
- 未启动 GUI；虚拟文件夹在 ExplorerBrowser 中的实际导航、菜单外观/焦点/图标和程序启动仍待可见验收。

## 使用

运行 `app/ExplorerDirs.App.exe`。本预览使用隔离 profile。按 `Ctrl+Q` 打开快速链接菜单，可添加当前文件系统或 Shell 文件夹；选择“管理快速链接…”可用 Explorer 管理 `.lnk` 文件。

SHA-256 以发行包内 `app/SHA256.txt` 为准。
