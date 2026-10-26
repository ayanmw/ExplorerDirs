# 贡献指南

欢迎提交功能请求、复现步骤和代码。请先阅读 [设计草案](docs/superpowers/specs/2026-10-15-explorer-dirs-design.md) 与 [路线图](docs/roadmap.md)。

提交问题时说明 Windows 版本、程序版本、路径类型（本地盘/UNC/网络盘）、布局、标签数量、复现步骤、预期与实际行为。日志或截图中请遮盖用户名、私人路径、文件名和环境变量值。

GitHub Actions 的 Windows workflow 会在 Windows runner 上运行同一组 Release 构建与 smoke 检查。

代码变更请保持单一目的。提交前运行 Release 构建、配置/逻辑 smoke 和发布隔离检查：

```powershell
dotnet build .\src\ExplorerDirs.App\ExplorerDirs.App.csproj --configuration Release
dotnet run --project .\src\ExplorerDirs.SettingsSmoke\ExplorerDirs.SettingsSmoke.csproj --configuration Release
pwsh -NoProfile -File .\scripts\publish-smoke.ps1
pwsh -NoProfile -File .\scripts\folder-verb-smoke.ps1
```

`SettingsSmoke` 会检查配置、Shell 状态和文件系统边界；两个 PowerShell smoke 只使用 fixture，不会生成真实发布包、改动默认文件关联或启动 GUI。通过这些检查不代表 ExplorerBrowser 的可见行为已验收。涉及文件操作、会话恢复或系统入口的变更应附可执行的失败与恢复验证步骤，并按项目 `AGENTS.md` 的桌面独占要求安排真实 GUI 验收。
