# 可使用版本

每个版本放在独立子目录中。当前供预览的版本为 `releases\preview-0.5.2\ExplorerDirs.App.exe`。这些是完整发布输出，不依赖 `src` 下的编译结果；请从这里启动日常使用的版本。

当前预览版是依赖 .NET 10 Windows Desktop Runtime 的 Windows 桌面程序。`SHA256.txt` 记录主程序校验值。旧版本保留供回退；不要在程序运行时覆盖其目录。

`preview-0.5.2` 增加逐窗格的 Tab / Shift+Tab 正反向焦点顺序，包括详细信息列标题，并修复粘贴后虽然窗口焦点回到文件区、方向键却失效的情况。隔离测试目录中已验证四窗格跨窗格反向焦点、复制粘贴后直接使用方向键，以及本机无确认框的 Shift+Delete；有确认框时仍待用户复测。发布版配置在项目根目录 `profile\ExplorerDirs.ini`，与 `src` 调试配置隔离；旧 `session.json` 首次运行时仅迁移布局与路径，旧文件仍保留。原生菜单、查看/排序/分组和第三方扩展仍待手工验收；保留 `preview-0.5.1` 供回退。

发布新版本：在项目根目录执行 `powershell.exe -File .\scripts\publish.ps1 -Version preview-X.Y.Z`。脚本拒绝覆盖同名版本。

未来版本的发布脚本会随 exe 写入 `FolderRoutingCapability.ini`，记录单目录参数能力与主程序 SHA-256，供可选文件夹右键入口核对。现有 `preview-0.5.2` 没有此清单，不能作为该入口的目标；新版本也须先完成真实窗口验收，再显式启用右键入口。发布脚本和入口脚本都不会自动更改 Windows 默认打开动作或 Win+E。
