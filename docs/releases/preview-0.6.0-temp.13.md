# Explorer Dirs 0.6.0 临时预览第十三版

日期：2026-10-24。目录版本：`preview-0.6.0-temp.13`；程序集版本：`0.6.0-preview.temp.13`。

这是从当前源码制作的可审阅预览包。程序位于 `app/`，使用包内独立 `profile/`；不会覆盖日常 profile，也不依赖 `src/` 下的调试输出。请只在测试目录中检查。

## 当前源码包含

- 原生 ExplorerBrowser 多窗格/多标签浏览、布局切换、文件和文件夹图标、Shell 右键菜单及文件操作。
- `.qdr` 布局、标签和视图状态导入/导出；支持本机 Shell 能解析且确认为文件夹的绝对解析名称。
- 每窗口独立会话、工作区收藏、快速筛选与收藏、目录树，以及每标签查看方式、排序、分组和列状态。
- Ctrl+鼠标滚轮切换查看方式、Ctrl+Y Shell 重做路由、Tab/Shift+Tab 键盘停靠点遍历，以及状态栏查看方式/容量/选中大小工具。
- 当前文件视图导出和打印、预览/详情窗格开关及目录变化监视。

## 构建验证与边界

- 发布隔离 smoke 与全量 Release SettingsSmoke 通过；App、HostProbe、NativeProbe Release 构建均为 0 警告、0 错误。
- 打包后已核对 EXE/DLL SHA-256 清单、DLL 产品版本、隔离 profile、随包说明和发布目录；未运行可执行文件。
- 本轮不启动可见 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir。真实窗口焦点与鼠标路由、Shell 菜单及文件操作表现、导入后颜色、Q-Dir 读写互操作、打印/预览渲染、自动刷新长尾和第三方扩展仍待桌面验收。
- 预览包不表示 Explorer/Q-Dir 完整兼容或所有功能已通过真实窗口验收。

从 `app/ExplorerDirs.App.exe` 启动。需要 .NET 10 Windows Desktop Runtime。
