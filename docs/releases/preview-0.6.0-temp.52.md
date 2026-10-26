# Explorer Dirs 0.6.0 临时预览版 52

这是基于当前源码生成的独立 profile 预览包，重点改进文件操作后的 Shell 项目焦点恢复。

## 本版重点

- 焦点恢复现在区分“文件内容 HWND 有焦点”和“Shell 文件项确实有键盘焦点”。非空目录缺少有效 `IFolderView` focused item 时，恢复重试不会提前结束。
- Shell 没有有效 focused item 但有有效 selection mark 时，将焦点恢复到该项，并保留原有选中状态。
- 包含 temp.51 中已有的右键目标 fail-closed 修正，以及 `.qdr` 兼容、自动刷新、命名查看预设、目录树、系统主题偏好和 Ctrl+滚轮等功能。

## 验证范围

- 完整 Release SettingsSmoke 通过，包含文件内容窗口焦点、Shell 项目焦点、空目录、focused/marked 项目索引的回归。
- App Release 构建通过，0 警告、0 错误。
- 本包未启动 GUI、ExplorerBrowser 或 Q-Dir。真实粘贴/删除后的方向键操作和焦点体验仍待桌面验收。
- 程序集产品版本：`0.6.0-preview.temp.52+77495f5d72a4739c2bb33a21d021328294f728c0`
- EXE SHA-256：`99ACF5E951174C8B9F6D12B17F6BCADD37CB5388717D5E9DC0D3158703218469`
- DLL SHA-256：`6CFF2597C7E58F9C2EAEB4FFD8007F1DCDFD369B08AB5E20C24517156F6F8677`

请从 `app/ExplorerDirs.App.exe` 启动。预览运行时对本包 `app/profile` 的修改不会写回项目日常 `profile`。
