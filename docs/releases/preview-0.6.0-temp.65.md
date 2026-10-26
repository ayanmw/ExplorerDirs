# Explorer Dirs 0.6.0 临时预览版 65

本版在 temp.64 后更新主题选中色对比、应用自有图标、Q-Dir 颜色规则导入提示，以及目录树的选中文件夹同步。使用独立 profile；不会写回日常配置。该预览方便检查近期累积改动，不代表 Q-Dir 完整兼容或 GUI 验收完成。

## 本版变化

- 浅色/深色选中色按底色校验对比度；Windows/自定义强调色继续驱动活动窗格和应用自有控件。应用自有导航、标签、文件命令与查看方式图标使用主题语义色矢量资源。
- 启用“从文件列表选择并展开”后，选中当前可见文件系统文件夹可在对应逐窗格树或活动共享树中定位展开，不改变文件视图路径或焦点；偏好独立保存在 `NavigationTreeSelection.ini`。
- Q-Dir 颜色规则暂不应用时会在导入预览中明确提示，避免用户误以为颜色设置已经导入。

## 验证与限制

- 本包由当前 `codex/windows-accent-theme` 工作树构建，发布目录为 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.65`；程序和配置位于 `app/`，使用独立 profile。
- 产品版本：`0.6.0-preview.temp.65+59bd93cea788f148b0414f28eee74d03a36153e5`。
- EXE SHA-256：`0A8D60B9A1EABEAA094DD133C2806BD7168F42F79BCCEE4AC5C310573B54B4F0`；DLL SHA-256：`EF852111FEC936BCED7C57EA795C418FE60742E0B8B2F240183ECD0A3EE5E5BC`，两者均与包内 `app/SHA256.txt` 一致。
- Release SettingsSmoke 通过；App Release build 0 警告、0 错误；`publish-smoke.ps1` 通过（fixture payload）。隔离 profile 中的 `ExplorerDirs.ini` 与 `PinnedTabs.ini` 哈希分别与主 profile 一致，未修改来源文件。本机 profile 没有 `Theme.ini`，因此预览首次启动使用应用默认的 System 主题。
- 应用仍使用 Windows Shell ExplorerBrowser 文件视图；本版不表示文件区支持深色或 Q-Dir 逐项颜色规则。
- 未启动预览程序、未操作 GUI 或 Q-Dir。主题即时切换、高对比度、四窗格目录树交互、真实 `.qdr` 导入/导出及 Shell 菜单仍待可见验收。
