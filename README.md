# Explorer Dirs

面向 Windows 的开源多窗格文件管理器。目标是替代日常使用的文件浏览窗口：以 Q-Dir 式布局和标签页为起点，优先解决崩溃后恢复、环境变量热刷新和可持续迭代。

> 当前为可运行原型，不具备完整文件管理功能，也未接管 Win+E 或文件夹默认打开方式。不要把它设置为系统默认文件管理器。

## 已有功能

- 单窗格、左右双窗格、四窗格布局；切换布局时保留各窗格标签页。
- 每窗格多标签；地址栏、上级目录、刷新、双击打开文件或目录。
- 后台枚举目录，切换目录时取消旧请求，避免慢目录长时间锁住界面。
- 定时原子保存布局和标签页路径到 `%LOCALAPPDATA%\ExplorerDirs\session.json`，下次启动恢复。
- 收到 Windows 环境设置变更消息时刷新当前进程的用户和机器环境变量，也可手动刷新；从当前目录启动的 `cmd.exe` 继承刷新后的环境。

## 运行

需要 Windows 10/11 和 .NET 10 SDK。

```powershell
dotnet run --project .\src\ExplorerDirs.App\ExplorerDirs.App.csproj
```

构建：

```powershell
dotnet build .\src\ExplorerDirs.App\ExplorerDirs.App.csproj --configuration Release
```

## 当前边界

当前仅浏览普通文件系统目录。复制、移动、删除、重命名、剪贴板、拖放、右键菜单、回收站、ZIP、网络位置与 Shell 虚拟目录尚未实现。双击文件使用 Windows 文件关联打开；这条路径不保证继承本程序刷新后的环境变量。环境刷新承诺针对本程序直接创建的子进程，现阶段可用“在当前目录打开命令行”验证。

故障报告请附 Windows 版本、复现步骤、目录类型和当时布局/标签数量；不要附个人目录内容或完整环境变量值。

## 文档

- [竞品调研](docs/competitive-research.md)
- [架构与设计边界](docs/superpowers/specs/2026-10-15-explorer-dirs-design.md)
- [路线图与验收](docs/roadmap.md)
- [贡献指南](CONTRIBUTING.md)

本项目使用 [MIT 许可证](LICENSE)。
