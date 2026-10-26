# 0.6.0 临时预览第十版

日期：2026-10-24。目录版本：`preview-0.6.0-temp.10`；程序集版本：`0.6.0-preview.temp.10`。

本包从当前源码构建，程序和独立配置位于 `app/`，不会升级日常 profile，也不依赖 `src/` 下的调试输出。它包含 temp.9 已列出的功能，以及下列后续源码改动。没有启动可见 GUI；源码回归和构建不代表实际键鼠体验通过。

## temp.9 之后的变化

- 状态栏“查看”入口纳入每窗格 Tab/Shift+Tab 和 F6/Shift+F6 焦点顺序。
- 状态栏选中项目大小按视图代际、选择版本和数量缓存；选区变化后重读，稳定选区最多每 5 秒重读一次，降低大选区重复访问 Shell 属性的负担。
- 文件操作后的焦点恢复会确认前台窗口仍属于目标应用窗口；不会从其他应用或另一个 Explorer Dirs 窗口抢回前台。
- 快捷键清单补充 Ctrl+D 与 Shift+F10 的 Explorer Shell 转发说明；没有复制实现 Shell 文件操作。

## 包含的主要功能

- 多窗格、十二种布局、多标签及每窗口独立 INI 会话；Shell 原生文件视图、右键菜单、文件操作、导航历史、查看/排序/分组和列状态。
- Q-Dir `.qdr` 导入及状态导出、每标签筛选和筛选预设、目录树、收藏夹/工作区、当前视图 CSV/TXT/HTML/XLS 导出与打印。
- 状态栏项目数、选中数、可取得的选中大小和磁盘空间，以及逐标签预览/详情窗格开关。

## 验证与限制

- 发布隔离 smoke、Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建通过；包内 EXE/DLL SHA-256 与清单一致。
- 没有启动 App、ExplorerBrowser、HostProbe 或 NativeProbe，也没有使用键盘、鼠标或系统剪贴板。
- 文件区右键目标、键盘焦点、Ctrl+鼠标滚轮、多窗格文件操作、窗口恢复和自动刷新仍需可见 GUI 验收；`.qdr` 与实际 Q-Dir 的互读互写未验证。
- 打印分页、预览/详情实际渲染、网络/离线位置、超大目录响应长尾、颜色过滤、主题个性化和第三方 Shell 扩展故障隔离仍未完成。

从 `app/ExplorerDirs.App.exe` 启动并仅在测试目录中预览。本包需要 .NET 10 Windows Desktop Runtime，不表示与 Q-Dir 完全兼容或已通过 Explorer 行为验收。
