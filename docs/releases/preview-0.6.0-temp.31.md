# Explorer Dirs 0.6.0 临时预览 temp.31

## 本版相较 temp.30

- “工具 → 选项 → 地址栏样式”新增 Classic 完整路径与 Modern 面包屑两种显示方式。默认仍为现代面包屑；经典样式持续显示完整路径，点击路径或使用地址栏快捷键可编辑。
- 样式写入独立便携 `AddressBar.ini`，不会混入每窗口会话；隔离预览会复制该偏好。两种样式仍使用原生 Shell 进行路径解析和导航。

## 验证

- Release `SettingsSmoke`：通过，覆盖默认现代样式、经典偏好往返和外部编辑保护。
- App Release 构建：通过，0 警告、0 错误。
- 发布隔离 smoke：通过，确认独立 profile 会复制 `AddressBar.ini` 原文。
- 发布包 EXE/DLL 哈希与 `app/SHA256.txt` 一致。
- 未启动 GUI。菜单可见性、经典/现代实际焦点、地址输入和四窗格排版仍待验收。

## 使用

运行 `app/ExplorerDirs.App.exe`。本预览使用隔离 profile；在“工具 → 选项 → 地址栏样式”切换样式。请保留 `app` 目录结构。

EXE 与 App DLL 校验值以包内 `app/SHA256.txt` 为准。
