# Explorer Dirs 0.6.0 临时预览第十五版

目录版本：`preview-0.6.0-temp.15`。程序位于 `app/`，使用包内独立 `profile/`；源码和调试输出仍在 `src/`，该预览不写入项目日常 profile。

## 相比 temp.14

- 目录 `FileSystemWatcher` 首次创建失败或运行中收到 Error 后，会通过单个待处理重试定时器尝试重建；失败时从 250 ms 起指数退避，最长 30 秒。重建成功后请求一次 Shell 协调刷新，以覆盖监视中断期间的变化；导航或监视器停止后，旧重试会被退休。
- 增加回归，确认监视器退休后迟到的失败回调不能重新安排重试。

## 验证与边界

- Release `SettingsSmoke`：通过；App、HostProbe、NativeProbe Release 构建：通过，均为 0 警告、0 错误；发布隔离 smoke：通过。
- 程序集产品版本：`0.6.0-preview.temp.15+77495f5d72a4739c2bb33a21d021328294f728c0`。
- EXE SHA-256：`00A8759EEBE57337E6C834F1DD8F9F045E34C6A20F1A502804BF80285E3F0A88`；DLL SHA-256：`7EE1F4FC6F085D7654A49925663BE6B246BEB0E88B52D0D13A3507534706B382`。包内清单、文件哈希、程序集产品版本和独立 profile 已核对。
- 未启动本预览、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir。真实可见列表收敛、网络重连、选择/焦点恢复、Q-Dir `.qdr` 互操作及自动刷新长尾仍待验收。此包是功能预览，不代表真实 GUI 验收通过。

从 `app/ExplorerDirs.App.exe` 启动；需要 .NET 10 Windows Desktop Runtime。
