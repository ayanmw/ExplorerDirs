# Explorer Dirs `preview-0.6.0-temp.24`

此版本基于当前源码生成，使用独立 profile；保留 temp.23，不覆盖日常 profile。

## 相较 temp.23 的变化

- 修复快速筛选调用 `IShellFolder::GetDisplayNameOf` 时 `STRRET` 接收缓冲区过小的问题。64 位原生结构需要 272 字节，旧缓冲区只有 264 字节；现按进程位数分配正确的结构大小。

## 验证与边界

- 新增结构大小回归，先复现 64 位预期 272、实际 264 的差异，再确认修复后通过。
- Release `SettingsSmoke`、App/NativeProbe Release 构建、发布隔离 smoke 通过；App 构建 0 警告、0 错误。
- 本包使用 `app/profile/` 独立配置目录。未启动本预览或 ExplorerBrowser；真实筛选显示行为仍待 GUI 验收。
