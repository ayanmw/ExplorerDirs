# 0.6.0 临时预览第八版

日期：2026-10-24。目录版本：`preview-0.6.0-temp.8`；程序集版本：`0.6.0-preview.temp.8`。

此包从当前源码构建，配置位于 `app/profile/`，与日常 profile 和 `src` 调试配置隔离。它主要预览目录变化 watcher 启动时序修正。未启动应用 GUI，也未完成 Shell/Q-Dir 可见互操作验收。

## 相对 temp.7 的主要变化

- **目录变更监视器提前启动：**导航到支持的文件系统目录后，先启用 `FileSystemWatcher`，再等待 Shell 项目数稳定。基线建立期间发生的创建、删除、重命名和监视器溢出会留在合并队列中；稳定后交给正式批次处理，并触发一次刷新，避免因事件数已被 Shell 反映而漏掉变化。
- **慢目录继续建立监视：**前 40 次检查每 250 ms 进行；之后改为每秒检查，直到获得 5 个连续相同的有效项目数。慢枚举期间 watcher 保持活动，不再因采样次数耗尽而放弃该目录的后续监视。
- SettingsSmoke 覆盖稳定计数、无效样本重置、40 次后的降频等待及慢枚举最终稳定。此验证没有创建 ExplorerBrowser，也没有测试真实 watcher 与 Shell 通知同时到达的时序。

## 隔离与启动

运行 `app/ExplorerDirs.App.exe`，需要 .NET 10 Windows Desktop Runtime。配置位于包内 `app/profile/`；不会写回项目根目录日常 profile。请从本目录启动，不要运行 `src` 下的调试构建，也不要在应用运行时覆盖包目录。`app/SHA256.txt` 记录 EXE 与 DLL 的 SHA-256。

## 验证边界

- Release SettingsSmoke 全量通过；App Release 构建 0 警告、0 错误；发布隔离 smoke 与包内哈希校验通过。
- 未启动 GUI、ExplorerBrowser 或 HostProbe。需重点检查慢速目录开始枚举时发生创建/删除，Shell 列表是否最终更新，选区和焦点是否保留，以及导航/切换标签期间旧 watcher 是否正确停止。
- 大目录性能、网络/离线位置、可见菜单和文件操作验收仍未完成；第三方 Shell 扩展仍在应用进程内运行。

请仅在隔离包中预览，并在测试目录中操作文件。若列表没有更新，请记录目录类型、操作时机、项目数变化及选区/焦点表现。
