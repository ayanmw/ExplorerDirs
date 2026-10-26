# Explorer Dirs 0.6.0 临时预览第十六版

目录版本：`preview-0.6.0-temp.16`。程序位于 `app/`，使用包内独立 `profile/`；源码和调试输出仍在 `src/`，该预览不写入项目日常 profile。

## 相比 temp.15

- 目录树现在支持共享树、逐窗格树和关闭三种模式；共享树会跟随活动窗格，并在切换布局后同步到恢复的活动目录。布局切换的活动窗格选择规则有纯逻辑回归覆盖。
- 会话与 `.qdr` 支持任意被当前 Shell 解析并确认是文件夹的绝对解析名称。共享树路由依据 Shell 文件系统位置状态；扩展缺失时对应标签可能无法恢复。
- 对 Q-Dir 尚未确认含义的 `tree_mode` 和 `TreeNsplit`，导入预览会明确提示未应用；`Tree1`～`Tree4` 的 0/1 映射仍是暂定解释。

## 验证与边界

- Release `SettingsSmoke`：通过；App、HostProbe、NativeProbe Release 构建：通过，均为 0 警告、0 错误；发布隔离 smoke：通过。
- 程序集产品版本：`0.6.0-preview.temp.16+77495f5d72a4739c2bb33a21d021328294f728c0`。
- EXE SHA-256：`4FFCF1D914ACE0E154120966D16F5FEEABC60B4A81EEEEF076D03078B6ED7563`；DLL SHA-256：`FA3F5DDB456B2D7FE50192AA6DEACE125DB92D6EAA9969FA815D1CBD3BAF0AAA`。包内清单、文件哈希、程序集产品版本和独立 profile 已核对。
- 未启动本预览、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir。目录树实际显示/焦点、Shell 虚拟位置可见导航、自动刷新恢复、网络重连、跨窗格拖放及 Q-Dir 实际 `.qdr` 互操作仍待验收。此包是功能预览，不代表真实 GUI 验收通过。

从 `app/ExplorerDirs.App.exe` 启动；需要 .NET 10 Windows Desktop Runtime。
