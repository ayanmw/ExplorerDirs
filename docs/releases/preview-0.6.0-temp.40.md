# 临时预览 temp.40

## 本版内容

- 修正原生 Shell 视图多选恢复：多 PIDL 批量恢复不再带 `SVSI_DESELECTOTHERS`，避免 Shell 返回成功但实际只保留最后一项。
- 增加旧选区不匹配时的 seeded fallback：先用当前视图索引选中一个保存项并清除其它选区，再用批量接口补齐快照。
- 增加隐藏默认 `IShellView` 探针，检查空选区恢复、多余旧选区清理以及已删除 PIDL 的幸存项恢复。两次连续运行通过，父窗口隐藏、前台窗口未变。
- 使用独立 profile；不会覆盖 temp.39，也不会写入日常 profile。

## 已验证

- App 与 NativeProbe Release 构建通过，0 警告、0 错误。
- 完整 Release SettingsSmoke 通过。
- 隔离 `IShellView` 探针连续两次通过：两项直接恢复、额外选区 fallback 恢复两项、删除一个快照项目后读取到一个幸存选区。

## 待预览/验收

- 未启动 ExplorerBrowser 生产宿主或可见 GUI；焦点恢复、四窗格和 50,000 项场景仍待单独验收。
- 这是隔离功能预览，不是 GUI 验收完成的正式版本。
