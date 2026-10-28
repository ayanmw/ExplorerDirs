# Preview 0.6.0 temp.68

本预览包含目录树图标显示修复：创建原生目录树时，将 `SHGetFileInfo(SHGFI_SYSICONINDEX)` 使用的 Windows 系统图像列表绑定到 `SysTreeView32`。

## 已完成的检查

- ExplorerDirs.App Release 构建通过，0 警告、0 错误。
- ExplorerDirs.NativeProbe Release 构建通过，0 警告、0 错误。
- 目录树图标索引 smoke 通过：普通/展开索引 `3/3`，系统图像表 512 项。
- 预览包使用独立 profile，不会改写日常使用配置。

## 尚待验收

- 本预览没有启动可见 GUI；请检查普通文件夹、驱动器和虚拟 Shell 位置的图标绘制。
- 图标索引 smoke 和构建不能替代可见窗口验收。
