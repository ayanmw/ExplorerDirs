# Explorer Dirs 0.6.0 临时预览版 58

本版是隔离试用包，汇总 temp.57 之后的源码更新。它未完成可见 GUI 或 Q-Dir 互操作验收，不代表这些行为已在真实窗口确认。

## 本版重点

- 映射或断开网络驱动器成功后，向 Windows Shell 通知“此电脑”目录更新，供映射驱动器视图刷新；取消和失败不发送通知。
- 保留 temp.57 的多窗格焦点/快捷键、标签与会话、Q-Dir `.qdr` 和 Program Starter 导入、目录监视器恢复等功能更新。
- 逐项颜色仍只在隔离 `IShellView` 原型中研究，没有接入产品 ExplorerBrowser 文件区。

## 验证范围

- Release `SettingsSmoke` 全量通过。
- App Release 构建通过，0 警告、0 错误。
- `scripts/publish-smoke.ps1` 通过；该检查只使用虚构的 profile/程序文件，不创建真实 release 或启动程序。
- 发布后核对包内 EXE/DLL SHA-256、程序集产品版本、独立 profile 和本说明文件。
- 未启动 GUI、未建立/断开真实网络驱动器、未在 Q-Dir 中打开导出文件。网络驱动器可见刷新、文件操作、右键目标、F2/Tab 焦点、布局拖动及 `.qdr` 可见互操作仍待桌面验收。

请从 `app/ExplorerDirs.App.exe` 启动。此预览使用 `app/profile/` 下的独立配置，不写入日常 profile。不要把 `src/` 中的调试输出当作日常版本。
