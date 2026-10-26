# Explorer Dirs 0.6.0 临时预览版 57

本版汇总 temp.56 之后的 Shell 导航、焦点/键盘、标签、Q-Dir 配置与目录监视器恢复更新。它用于预览，不代表可见 GUI 或 Q-Dir 互操作验收完成。

## 本版重点

- 加强多窗格文件区的焦点恢复与 Tab/Shift+Tab 停靠点遍历；覆盖 F2 重命名编辑框方向键、文件操作后焦点恢复和标题/列头输入边界。增加 F10 文件菜单入口与相关快捷键路由。
- 继续使用 ExplorerBrowser 原生文件视图、Shell 文件操作和原生右键菜单。鼠标命中不确定时不把残留选区作为目标；右键空白与可见行尾的 Shell 分类仍需桌面验收。
- 扩展 Q-Dir `.qdr` 工作区和原生视图状态兼容；加入独立 `ProgramStarter.ini` 与 Q-Dir `[Program]` 导入预览/显式应用。隔离验收样本中的程序路径不存在，避免误启动。
- 加强目录 watcher、自动刷新、刷新回调和错误恢复路径；高负载与异常时序已有 SettingsSmoke 和隐藏宿主测试覆盖，但这些结果不替代用户可见的响应、焦点与稳定性验收。
- 颜色功能仍是隔离 `IShellView` 原型，没有接入 ExplorerBrowser 产品文件视图或本版用户设置。

## 验证范围

- Release `SettingsSmoke` 全量通过。
- App Release 构建通过，0 警告、0 错误。
- `scripts/publish-smoke.ps1` 通过；该检查只使用虚构的 profile/程序文件，不创建真实 release 或启动程序。
- 发布后会核对包内 EXE/DLL SHA-256、产品版本、独立 profile 和本说明文件。
- 本版没有启动 GUI 或 Q-Dir。真实文件复制/删除/撤销、右键空白菜单、F2/Tab 焦点、预览/详情窗格、布局拖动、`.qdr` 可见互操作、程序启动器导入界面仍待桌面验收。

请从 `app/ExplorerDirs.App.exe` 启动。此预览使用 `app/profile/` 下的独立配置，不写入日常 profile。不要把 `src/` 中的调试输出当作日常版本。
