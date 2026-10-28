# Explorer Dirs Preview 0.6.0 temp.77

本版本是隔离预览包，使用 `app/profile/`，不覆盖日常配置。

- 列值筛选在每列选中值达到 8 个时使用大小写不敏感查找集，降低大筛选列表逐项目匹配的开销；少于 8 个仍使用列表比较，筛选值上限保持 256。
- 发布包内提供 `tools/folder-verb.ps1` 和 `FolderVerb.psm1`。在预览根目录运行 `pwsh -NoProfile -File .\tools\folder-verb.ps1 -Mode Preview`，可查看目标 EXE、SHA-256、能力声明与当前系统入口状态。此包没有自动安装文件夹/磁盘右键菜单，也没有接管默认打开方式或 Win+E。
- 启用入口前，需先在 GUI 验收此确切预览版的目录参数和 Shell 菜单行为，再将 Preview 显示的准确 SHA-256 传给启用命令并明确确认。停用只移除此项目创建且仍未被外部修改的入口。

通过 GUI 验收后，使用 Preview 返回的哈希手动执行 `pwsh -NoProfile -File .\tools\folder-verb.ps1 -Mode Enable -ExpectedSha256 <SHA256> -ConfirmVerifiedRelease`；移除入口使用 `pwsh -NoProfile -File .\tools\folder-verb.ps1 -Mode Disable`。启用步骤尚未对真实注册表执行。

后台验证：完整 Release SettingsSmoke、App Release build、隔离发布打包 smoke、文件夹入口隔离注册表 smoke 通过。没有启动此预览包或做可见 GUI 验收；实际菜单显示和从 Explorer 菜单启动仍待验收。
