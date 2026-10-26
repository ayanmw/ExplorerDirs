# Explorer Dirs `preview-0.6.0-temp.25`

此版本基于当前源码生成，使用独立 profile；保留 temp.24，不覆盖日常 profile。

## 相较 temp.24 的变化

- 修复文件命令工具栏状态在指针停留于工具栏时未随文件选择或标签变化更新的问题。活动窗格的选区变化、导航完成/失败和标签切换会在工具栏悬停或键盘聚焦时触发 150 ms 合并查询；工具栏空闲时不查询 Shell 命令扩展。

## 验证与边界

- 完整 Release `SettingsSmoke` 通过，包含刷新门控对非活动窗格、空闲工具栏、连续事件合并和执行前上下文过期的覆盖。
- App Release 构建 0 警告、0 错误；发布隔离 smoke 通过；包内 EXE/DLL SHA-256 与清单一致。
- 本包使用 `app/profile/` 独立配置目录。未启动本预览或 ExplorerBrowser；可见工具栏启用状态及第三方 Shell 扩展仍待 GUI 验收。
