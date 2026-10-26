# 临时预览 temp.41

## 本版内容

- 延续 temp.40 的 Shell 多选恢复修正：批量选择保留每个传入 PIDL；在实际读回完整匹配前不报告恢复成功。
- 优化额外旧选区的 replacement fallback：用 `IFolderView2::GetSelectedItem` 取当前选中索引，通过 `IFolderView::SelectItem` 清除当前选项后再批量恢复快照，避免在失败路径中按保存 PIDL 遍历整个目录寻找索引。
- 隐藏默认 `IShellView` 探针覆盖空选区直接恢复、清除两项额外选区、以及目标项删除后的幸存项恢复。连续两次运行通过。
- 使用独立 profile；不会覆盖 temp.40，也不会写入日常 profile。

## 已验证

- App 与 NativeProbe Release 构建通过，0 警告、0 错误。
- 完整 Release SettingsSmoke 通过。
- 原生探针连续两次通过，父窗口隐藏、前台窗口未改变。
- 发布隔离 smoke 通过；包内 SHA-256 清单与 EXE/DLL 文件一致。

## 待预览/验收

- 未启动 ExplorerBrowser 生产宿主或可见 GUI；焦点恢复、四窗格和 50,000 项场景仍待单独验收。
- 这是隔离功能预览，不是 GUI 验收完成的正式版本。
