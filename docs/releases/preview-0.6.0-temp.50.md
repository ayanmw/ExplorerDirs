# Explorer Dirs 0.6.0 临时预览版 50

这是基于当前源码生成的独立 profile 预览包，方便检查 temp.49 之后的 Q-Dir 配置兼容和 Shell 交互修正。

## 本版重点

- 修正 `.qdr` 导入对每标签查看方式值 8 的处理，按 Q-Dir 公开范围回退到详细信息，并保留 Explorer Dirs 自身 Content 查看状态。
- `.qdr` 导出遇到 Q-Dir 未公开支持的 Content 模式时写入兼容的详细信息值；本程序扩展仍可恢复自身模式。
- 加强目录变化计数暂不可读时的刷新协调，避免重复全视图刷新循环。
- 包含当前源码已有的 Shell 文件操作焦点恢复、每标签查看状态、命名查看预设、可选 Shell 目录树、系统主题偏好和 Ctrl+滚轮处理。

## 验证范围

- Release SettingsSmoke 全量回归通过。
- App Release 构建通过，0 警告、0 错误。
- 发布隔离 smoke 通过；profile 与日常配置分离。
- 本包未启动 GUI、ExplorerBrowser 或 Q-Dir。真实菜单、鼠标/键盘焦点、文件区可见主题和 Q-Dir 实际读取仍待桌面验收。
- 程序集产品版本：`0.6.0-preview.temp.50+77495f5d72a4739c2bb33a21d021328294f728c0`
- EXE SHA-256：`29CF860E0149350494C6BEC11158DF0947378FE2E114B40C68548357A7CB475D`
- DLL SHA-256：`DDB649CBEF8CEC468184357ABD9280FDF58402B758DD59A0452A3993B375E9DC`

请从 `app/ExplorerDirs.App.exe` 启动。预览运行时对本包 `app/profile` 的修改不会写回项目日常 `profile`。
