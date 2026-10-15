# 贡献指南

欢迎提交功能请求、复现步骤和代码。请先阅读 [设计草案](docs/superpowers/specs/2026-10-15-explorer-dirs-design.md) 与 [路线图](docs/roadmap.md)。

提交问题时说明 Windows 版本、程序版本、路径类型（本地盘/UNC/网络盘）、布局、标签数量、复现步骤、预期与实际行为。日志或截图中请遮盖用户名、私人路径、文件名和环境变量值。

代码变更请保持单一目的，构建命令为 `dotnet build .\src\ExplorerDirs.App\ExplorerDirs.App.csproj --configuration Release`。涉及文件操作、会话恢复或系统入口的变更应附可执行的失败与恢复验证步骤。
