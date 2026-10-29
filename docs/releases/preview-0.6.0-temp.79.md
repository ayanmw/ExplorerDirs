# Explorer Dirs Preview 0.6.0 temp.79

程序位于 `releases/preview-0.6.0-temp.79/app/ExplorerDirs.App.exe`，使用独立 profile；本包从 temp.78 复制便携会话配置，不连接日常 profile。

相较 temp.78，本版加入或更新：

- “视图 → 标签页显示”增加“隐藏标签栏（保留标签）”，仅隐藏标签栏 UI，现有标签与快捷键仍保留。
- 文件视图中键及键盘“在新标签打开”现可打开经当前 Shell provider 确认的虚拟文件夹；文件仍不会误作文件夹打开。
- “工具 → 文件夹入口…”可以只读检查 Directory/Drive 入口、预览本发布版 EXE 与 SHA-256，并显式启用或禁用当前用户文件夹/磁盘右键入口。默认未启用；确认当前版本的菜单行为后再自行决定是否启用。
- 重启 Windows Explorer 的进程身份校验和终止使用同一个进程句柄，避免 PID 复用时结束错误进程。

此前 temp.78 已有的标题栏模式、列值筛选性能与筛选期间选区保护等功能继续保留。

验证：完整 Release `ExplorerDirs.SettingsSmoke` 通过；发布隔离 smoke 通过；Release 包构建成功。包内 EXE SHA-256：`CA6A378D45C04F1DE95E9A79EB72AB5E1F24E137BA28301D6D8842AF2ECCC659`；DLL SHA-256：`66B3AF3A9494DCCA95D653ABB050DEAFB5CF5AE29925E9A0155A3FB0EDC9FE46`，与 `app/SHA256.txt` 一致。版本为 `0.6.0-preview.temp.79+e77f12a8579fcb2d72854c9b045bc12c5b4ed560`。

本包没有启动进行可见验收；列头筛选、Explorer 菜单、虚拟位置中键打开和新文件夹入口真实菜单/启动仍待集中 UI 验收。系统文件夹入口没有启用，未修改真实 Explorer 注册表。隔离 `IShellView` 逐行大小列仍只是研究原型，未接入产品；Q-Dir `.qdr` 真实互操作仍需实际对照。
