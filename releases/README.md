# 可使用版本

每个版本放在独立子目录中。当前供预览的版本为 `releases\preview-0.5.1\ExplorerDirs.App.exe`。这些是完整发布输出，不依赖 `src` 下的编译结果；请从这里启动日常使用的版本。

当前预览版是依赖 .NET 10 Windows Desktop Runtime 的 Windows 桌面程序。`SHA256.txt` 记录主程序校验值。旧版本保留供回退；不要在程序运行时覆盖其目录。

`preview-0.5.1` 修复 Shell 文件操作后焦点落到视图外层窗口的问题：恢复时聚焦实际文件内容控件，并处理剪贴板操作和删除确认框返回。发布版配置在项目根目录 `profile\ExplorerDirs.ini`，与 `src` 调试配置隔离；旧 `session.json` 首次运行时仅迁移布局与路径，旧文件仍保留。Ctrl+C/V、Shift+Delete 后焦点需用户复测；原生菜单、查看/排序/分组和第三方扩展仍待手工验收；保留 `preview-0.5.0` 供回退。

发布新版本：在项目根目录执行 `powershell.exe -File .\scripts\publish.ps1 -Version preview-X.Y.Z`。脚本拒绝覆盖同名版本。
