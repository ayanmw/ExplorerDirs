# Explorer Dirs 0.6.0 临时预览版 49

## 相较 temp.48

- 隔离预览打包现在也会复制 profile 中的 `Theme.ini`（如果存在），保留跟随系统、浅色或深色主题偏好。temp.48 的 Ctrl+滚轮失败回退和 `ViewPresets.ini` 复制行为继续包含在当前源码版本中。

## 验证

- 发布隔离 smoke 对 `Theme.ini` 的原样复制、源 profile 不变、拒绝覆盖已有版本和普通发布行为通过。
- App Release 构建通过；temp.49 的程序集版本与 EXE/DLL 哈希已在打包后核对。
- 未启动 GUI、ExplorerBrowser、HostProbe 或 Q-Dir；主题在可见 Shell 文件区、物理滚轮路由、焦点/选区和 Q-Dir 互操作仍待桌面验收。

预览使用独立 profile；运行时对该 profile 的修改不会写回日常配置。请从 `app/ExplorerDirs.App.exe` 启动。
