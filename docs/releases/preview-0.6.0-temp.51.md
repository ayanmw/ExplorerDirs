# Explorer Dirs 0.6.0 临时预览版 51

这是基于当前源码生成的独立 profile 预览包，重点修正文件区右键目标无法确认时的自定义菜单注入。

## 本版重点

- 当 Shell 列表没有可用的原生项目命中测试时，不再用 UI Automation 的 `ListItem` 行边界猜测点击目标。对于详细列表行尾空白，目标无法确认时仍保留 Windows Shell 原生右键菜单，但不插入目标不明的 Explorer Dirs 命令。
- 包含 temp.50 中已有的 `.qdr` 查看方式兼容修正、自动刷新计数不可读处理、文件操作焦点恢复、命名查看预设、目录树、系统主题偏好及 Ctrl+滚轮等功能。

## 验证范围

- 右键目标专项 smoke、完整 Release SettingsSmoke 通过。
- App Release 构建通过，0 警告、0 错误；发布隔离 smoke 通过。
- 本包未启动 GUI、ExplorerBrowser 或 Q-Dir。真实视图里的项目/空白处命中、菜单内容、焦点行为及 Q-Dir 实际读取仍待桌面验收。
- 程序集产品版本：`0.6.0-preview.temp.51+77495f5d72a4739c2bb33a21d021328294f728c0`
- EXE SHA-256：`B09BED7F936E532278901393A6329CA00E61BE9C9F9DC455D0A4D6973EA01C59`
- DLL SHA-256：`53CA21EA9613C5008BA073BF920DF1F87A807FDCB9321CC0CFCD1BC95BA27674`

请从 `app/ExplorerDirs.App.exe` 启动。预览运行时对本包 `app/profile` 的修改不会写回项目日常 `profile`。
