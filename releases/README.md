# 可使用版本

每个版本放在独立子目录中。当前供预览的版本为 `releases\preview-0.5.0\ExplorerDirs.App.exe`。这些是完整发布输出，不依赖 `src` 下的编译结果；请从这里启动日常使用的版本。

当前预览版是依赖 .NET 10 Windows Desktop Runtime 的 Windows 桌面程序。`SHA256.txt` 记录主程序校验值。旧版本保留供回退；不要在程序运行时覆盖其目录。

`preview-0.5.0` 加入每标签的 Shell 后退/前进历史、父目录和历史快捷键、窗格间 Tab 焦点切换，以及切换布局后恢复该布局活动文件视图的焦点。发布版配置在项目根目录 `profile\ExplorerDirs.ini`，与 `src` 调试配置隔离；旧 `session.json` 首次运行时仅迁移布局与路径，旧文件仍保留。原生焦点、导航历史、文件/空白处右键、Ctrl+A/C/V、查看/排序/分组切换和第三方扩展在各用户环境中的行为仍待手工验收；保留 `preview-0.4.0` 供回退。

发布新版本：在项目根目录执行 `powershell.exe -File .\scripts\publish.ps1 -Version preview-X.Y.Z`。脚本拒绝覆盖同名版本。
