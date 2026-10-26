# 0.6.0 临时预览第十一版

日期：2026-10-24。目录版本：`preview-0.6.0-temp.11`；程序集版本：`0.6.0-preview.temp.11`。

本包从当前源码构建，程序和独立配置位于 `app/`，不会升级日常 profile，也不依赖 `src/` 下的调试输出。它包含 temp.10 已列出的功能、watcher 启动期变更缓存与慢枚举降频等待，以及当前目录现有文件元数据变化刷新。没有启动可见 GUI；源码回归和 Windows 文件监视器 smoke 不代表 ExplorerBrowser 可见行为已验收。

## temp.10 之后的变化

- 当前目录 watcher 增加文件大小、修改时间、属性和创建时间监视。现有文件被写入或修改元数据时，发出项目数不变的刷新请求，交给现有队列合并后刷新活动 Shell 视图。
- 导航后先启用目录 watcher，再以连续 5 次稳定 Shell 项目数建立基线；建立期间的事件由合并队列保留并交给刷新流程。快速采样 40 次仍未稳定后改为每秒继续检查，不再放弃启动 watcher。
- SettingsSmoke 验证通知映射语义，并在唯一临时目录使用 Windows `FileSystemWatcher` 追加文件内容，确认实际收到 `Changed` 事件。
- 仍不递归监控子目录。此前已完成的状态栏焦点停靠、选中大小缓存节流和焦点恢复前台归属门控也包含在包内。

## 包含的主要功能

- 多窗格、十二种布局、多标签及每窗口独立 INI 会话；Shell 原生文件视图、右键菜单、文件操作、导航历史、查看/排序/分组和列状态。
- Q-Dir `.qdr` 导入及状态导出、每标签筛选和筛选预设、目录树、收藏夹/工作区、当前视图 CSV/TXT/HTML/XLS 导出与打印。
- 状态栏项目数、选中数、可取得的选中大小和磁盘空间，以及逐标签预览/详情窗格开关。

## 验证与限制

- 发布隔离 smoke、Release SettingsSmoke、App/HostProbe/NativeProbe Release 构建通过；包内 EXE/DLL SHA-256 与清单一致。
- 没有启动 App、ExplorerBrowser、HostProbe 或 NativeProbe，也没有使用键盘、鼠标或系统剪贴板。
- 元数据刷新尚未验证 ExplorerBrowser 可见列、连续写入、网络目录和实际响应时间；右键目标、键盘焦点、多窗格文件操作、窗口恢复、打印/预览渲染及 Q-Dir `.qdr` 互读互写仍待验收。
- 超大目录响应长尾、颜色过滤、主题个性化和第三方 Shell 扩展故障隔离仍未完成。

从 `app/ExplorerDirs.App.exe` 启动并仅在测试目录中预览。本包需要 .NET 10 Windows Desktop Runtime，不表示与 Q-Dir 完全兼容或已通过 Explorer 行为验收。

桌面验收步骤见[验收清单](../gui-acceptance-temp.11.md)。
