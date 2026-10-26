# Explorer Dirs 0.6.0 临时预览版 55

本版补上全局大小显示格式设置和 F9 递归汇总数量开关，并修正独立预览 profile 未携带 `SizeDisplay.ini` 的打包遗漏。该包用于预览，不代表 GUI 验收通过。

## 本版重点

- “工具 → 选项 → 大小显示格式”可选自动、字节、KB、MB、GB、TB，设置 0～3 位小数和千位分隔；配置写入 profile 下的 `SizeDisplay.ini`。
- 同一选项中可切换 F9 递归汇总是否显示文件数和子目录数，默认显示；关闭数量不会隐藏无法读取/跳过项目提示。
- `scripts/publish.ps1 -IsolatedProfile` 现在会复制 `SizeDisplay.ini`，避免预览包忽略已保存的大小格式设置。

## 验证范围

- Release SettingsSmoke 通过，包含格式单位/精度/分隔、F9 数量开关和 INI 持久化回归。
- 发布隔离 smoke 首先按预期失败并指出缺失的 `SizeDisplay.ini`，修复发布配置后通过；确认源 profile 未被修改、无关文件不被复制、已有发布目录不会覆盖。
- App Release 构建通过，0 警告、0 错误。包内 EXE 与 DLL 哈希记录在 `SHA256.txt`。
- 没有启动 GUI、ExplorerBrowser 或 Q-Dir。菜单布局、本地化数字格式、F9 状态栏更新和 Q-Dir 行为仍待可见桌面验收。

请从 `app/ExplorerDirs.App.exe` 启动。此预览使用独立的 `app/profile/`，运行时修改不会写回项目日常 `profile/`。
