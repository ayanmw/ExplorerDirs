# 临时预览 temp.38

## 本版内容

- 当前源码在 ExplorerBrowser 初始化前启用 `FVO_CUSTOMPOSITION`，并在每个新 Shell 视图中尝试清除“仅详细信息显示列头”和“所有查看方式隐藏列头”两个标志。
- 标志逻辑只改动这两个列头位；Shell 读回失败时保留原生文件浏览并报告一次。
- 本包使用独立 profile，启动和设置写入不会改动日常 profile。

## 已验证

- Release App、NativeProbe、HostProbe 构建通过，0 警告、0 错误。
- 完整 Release SettingsSmoke 和发布隔离 smoke 通过。
- 列头标志掩码 smoke 覆盖相关位移除及其它 folder flags 保留。
- 程序集产品版本：`0.6.0-preview.temp.38+77495f5d72a4739c2bb33a21d021328294f728c0`。
- EXE SHA-256：`C3E355E7621D5EA480C10C7AD3D961905E3095BEA080DBB027F34EE13E5D7CE9`。
- DLL SHA-256：`0824CA669A92EC8A5B41EE2DD369E4639952B0EBFBC5ECC76F55DBDBCA967FE5`。

## 待预览/验收

- 本轮未运行 ExplorerBrowser 宿主探针或可见 GUI。各查看方式的列头是否出现，排序、排列、列宽/顺序、选区、焦点和原生右键菜单是否保持，都需要实际预览。
- Windows 的 `IFolderView2::SetCurrentFolderFlags` 要求配合 `FVO_CUSTOMPOSITION`；启用后的可见排列副作用尚未验证。参见 [Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags)。
- 这不是已完成 GUI 验收的正式版本；出现不符合预期的显示或排列时先退出此版本并反馈现象。
