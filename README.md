# Explorer Dirs

面向 Windows 的开源多窗格文件管理器。目标是替代日常使用的文件浏览窗口：以 Q-Dir 式布局和标签页为起点，优先解决崩溃后恢复、环境变量热刷新和可持续迭代。

> 当前为可运行预览版，不具备完整文件管理功能，也未接管 Win+E 或文件夹默认打开方式。不要把它设置为系统默认文件管理器。

## 已有功能

- 顶部菜单和图形快捷按钮切换单窗格、左右/上下双窗格、两种三窗格和四窗格；切换布局时保留各窗格标签页。
- 每窗格多标签；标签右键菜单支持新建、复制、关闭其他和恢复关闭标签。中键或双击标签标题可关闭标签；每个窗格至少保留一个标签。
- 每个标签内嵌 Windows Shell ExplorerBrowser 文件视图，初始为详细信息。文件和目录空白处按系统视图命中，支持原生查看、排序、分组、新建、属性、文件关联、图标及第三方 Shell 扩展菜单；本程序的标签、路径和布局命令排在同一个右键菜单前面。
- 地址栏、上级目录、刷新、双击打开文件或目录；Shell 视图自行完成目录枚举和显示。
- 定时原子保存布局和标签页路径到 `%LOCALAPPDATA%\ExplorerDirs\session.json`，下次启动恢复。
- 收到 Windows 环境设置变更消息时刷新当前进程的用户和机器环境变量，也可手动刷新；从当前目录启动的 `cmd.exe` 继承刷新后的环境。

## 预览发布版

在 Windows 10/11 且安装 .NET 10 Windows Desktop Runtime 的环境中，从 [`releases`](releases/README.md) 下最新版本目录启动 `ExplorerDirs.App.exe`。发布目录与源码编译输出相互独立；当前版本位置会写在该目录的说明中。

## 开发调试

需要 .NET 10 SDK。以下命令只用于源码开发，调试版会话写到 `%LOCALAPPDATA%\ExplorerDirs.Dev`：

```powershell
dotnet run --project .\src\ExplorerDirs.App\ExplorerDirs.App.csproj
```

构建：

```powershell
dotnet build .\src\ExplorerDirs.App\ExplorerDirs.App.csproj --configuration Release
```

## 当前边界

当前应用的地址栏与会话仅支持普通文件系统目录。文件操作、剪贴板、视图切换和第三方菜单由嵌入的 Windows Shell 视图提供；跨窗格拖放、撤销、回收站和网络位置仍需针对真实目录人工验收。第三方 Shell 扩展在本程序进程内运行，尚无扩展崩溃隔离。文件关联启动的程序不保证继承本程序刷新后的环境变量；热刷新承诺针对本程序直接创建的子进程，可用“在当前目录打开命令行”验证。系统右键菜单与标签快捷关闭仍待用户在不同目录和窗口布局下预览确认。

故障报告请附 Windows 版本、复现步骤、目录类型和当时布局/标签数量；不要附个人目录内容或完整环境变量值。

## 文档

- [竞品调研](docs/competitive-research.md)
- [Q-Dir 截图对比与当前差异](docs/ui-comparison-2026-10-15.md)
- [架构与设计边界](docs/superpowers/specs/2026-10-15-explorer-dirs-design.md)
- [路线图与验收](docs/roadmap.md)
- [贡献指南](CONTRIBUTING.md)

本项目使用 [MIT 许可证](LICENSE)。
