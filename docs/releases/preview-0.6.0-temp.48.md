# Explorer Dirs 0.6.0 临时预览版 48

## 相较 temp.47

- Ctrl+滚轮累积到完整刻度后，如果应用无法切换查看方式，会把完整累计刻度交还给原生 Shell 子类链，避免切换失败时滚轮输入无响应。成功切换仍由应用消费，普通滚轮行为不变。
- 隔离预览打包现在会复制 profile 中的 `ViewPresets.ini`（如果存在），使命名查看预设能随预览 profile 一起使用。

## 验证

- Release `ExplorerDirs.SettingsSmoke` 通过。
- App Release 构建通过，0 警告、0 错误。
- 发布隔离 smoke 覆盖 `ViewPresets.ini` 原样复制、源 profile 不变、拒绝覆盖已有版本及普通发布行为。
- 未启动 GUI、ExplorerBrowser、HostProbe 或 Q-Dir；真实鼠标路由、焦点/选区、Shell 拒绝切换时的行为和 Q-Dir 互操作仍待验收。

预览使用独立 profile；运行时对该 profile 的修改不会写回日常配置。请从 `app/ExplorerDirs.App.exe` 启动。
