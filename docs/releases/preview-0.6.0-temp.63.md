# Explorer Dirs 0.6.0 临时预览版 63

本版与 temp.62 包含相同的主题改动，同时修正从隔离 worktree 发布时选择 profile 来源的问题。发布时从主项目 `D:\projects\explorer-dirs\profile` 只读复制当前允许的设置到 `app\profile\`；不会写回日常 profile。

## 本版变化

- 照片打印窗口背景、文字、预览底色和边框使用 Windows 动态系统颜色。
- 当前每窗格标签显示两 DIP 的系统强调色底边。
- 临时预览使用独立 profile；本包复制主项目当前会话和固定标签配置，不与 `src` 调试状态共享。

## 验证与限制

- 产品版本：`0.6.0-preview.temp.63+fb48e6c4cf819c8d79ce20ac09f44243edd01172`。
- EXE SHA-256：`D579B06A362D9E89E721EA4DD13B3EDBE9C5E2244471665330F88C5927105A5E`；DLL SHA-256：`04C833116CD902ACCAFD9D5316C5185624703986601ADA1DA29A636CBAB2C6C2`，与包内 `app\SHA256.txt` 一致。
- 全量 Release `SettingsSmoke` 通过；App Release 构建 0 警告、0 错误；发布隔离 smoke 通过。
- 隔离 profile 包含 `ExplorerDirs.ini` 和 `PinnedTabs.ini`，各文件与发布时指定的主项目 profile 源文件 SHA-256 一致；源文件未修改。
- 未启动程序或操作桌面；标签主题、打印窗口、深色/高对比度和 Shell 文件区的可见表现仍待 GUI 验收。
- ExplorerBrowser 文件列表仍由 Windows Shell 绘制；本版不表示文件区已支持深色或 Q-Dir 文件颜色规则，也不代表完整 Q-Dir 兼容。
