# Explorer Dirs 0.6.0 临时预览版 56

本版在 temp.55 的源码基础上，接入详细视图的原生列宽自适应快捷键。此包用于预览；没有启动 GUI，不能视为可见交互验收通过。

## 本版重点

- 详细视图获得焦点时，按 `Ctrl+数字小键盘+` 触发 Windows Shell 原生列宽自适应。仅在 `IShellView.TranslateAccelerator` 返回 `S_FALSE`、按键为 Ctrl+VK_ADD 且处于 KeyDown 时，才把按键交给当前 `SysListView32`；Shell 已处理、其它修饰键、普通数字键与 KeyUp 不会重复触发。
- 包含 temp.55 已有的大小显示单位/精度/千位分隔设置及 F9 递归汇总数量选项。
- 使用 `-IsolatedProfile` 发布，程序位于 `app/`，独立 profile 位于 `app/profile/`；预览运行不会写回 `src/` 调试配置。

## 验证范围

- Release `SettingsSmoke` 全量通过，覆盖 Ctrl+VK_ADD 路由边界、其它按键/修饰键不转发及相关配置行为。
- App Release 构建与发布隔离 smoke 通过；EXE/DLL SHA-256 记录于包内 `SHA256.txt`。
- 没有启动发布 EXE、ExplorerBrowser 或 Q-Dir。数字小键盘实际输入、列宽可见变化、焦点/选区以及多窗格行为仍待桌面验收。

请从 `app/ExplorerDirs.App.exe` 启动。此预览带有独立 profile。
