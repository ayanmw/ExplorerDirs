# 可使用版本

每个版本放在独立子目录中。当前供预览的版本为 `releases\preview-0.3.3\ExplorerDirs.App.exe`。这些是完整发布输出，不依赖 `src` 下的编译结果；请从这里启动日常使用的版本。

当前预览版是依赖 .NET 10 Windows Desktop Runtime 的 Windows 桌面程序。`SHA256.txt` 记录主程序校验值。旧版本保留供回退；不要在程序运行时覆盖其目录。

`preview-0.3.3` 改为原生 ExplorerBrowser 文件视图，加入标签中键/双击关闭及系统菜单前置命令。文件/空白处右键、查看/排序/分组切换和第三方扩展在各用户环境中的行为仍待手工验收；请先保留 `preview-0.2.1` 供回退。`preview-0.3.0` 至 `preview-0.3.2` 是本轮早期构建，不作为当前预览入口。

发布新版本：在项目根目录执行 `powershell.exe -File .\scripts\publish.ps1 -Version preview-X.Y.Z`。脚本拒绝覆盖同名版本。
