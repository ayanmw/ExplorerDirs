# Explorer Dirs 0.6.0 临时预览第十七版

目录版本：`preview-0.6.0-temp.17`。程序位于 `app/`，使用包内独立 `profile/`；源码和调试输出仍在 `src/`，本预览不写入项目日常 profile。

## 相比 temp.16

- 目录 watcher 在监控目标失效后会按退避计划重建；文件内容/元数据变化、等量创建删除和 watcher 错误进入既有合并协调流程。状态恢复后的可见刷新、慢速/网络目录长尾仍待真实窗口验收。
- `.qdr` 导出纳入应用内目录树模式扩展状态；Q-Dir `Vorschau=1/0` 导入/导出已接入，按 Q-Dir 单值语义写活动视图，并在导入时应用到本次追加的标签。
- Shell 重命名编辑器中的方向键路由和 Ctrl+Y 原生 Shell 重做兜底已补齐；Ctrl+方向键及 Ctrl+空格继续交给 Shell 处理。
- `Tree1`～`Tree4` 仍是附带警告的暂定 0/1 映射；`tree_mode` 数值和 `TreeNsplit` 比例未猜测。任意可解析并确认为文件夹的 Shell 绝对名称继续适用于会话与 `.qdr` 位置。

## 验证与边界

- 完整 Release `SettingsSmoke` 与 Q-Dir/Shell 位置导入专项：通过；App、HostProbe、NativeProbe Release 构建：通过，0 警告、0 错误；发布隔离 smoke：通过。
- 程序集产品版本：`0.6.0-preview.temp.17+77495f5d72a4739c2bb33a21d021328294f728c0`。
- EXE SHA-256：`B8CDC27E1CD0452557257AE61FE5CE66B742577C122C390E7D4D648E8CC4780D`；DLL SHA-256：`1633B157B1D6B7D94A6CEAA7A238B9B4E32E8D78C16554C80F38AA024082130D`。两者均与包内 `app/SHA256.txt` 一致；`FolderRoutingCapability.ini` 中的 EXE 哈希也一致。
- 包内 `app/profile/` 只复制了当前存在的 `ExplorerDirs.ini` 与 `PinnedTabs.ini`，没有连接到项目日常 profile。
- 本包尚未启动 GUI、ExplorerBrowser、HostProbe、NativeProbe 或 Q-Dir。物理键鼠、真实 Shell 菜单/文件操作、自动刷新可见表现、Shell 虚拟位置可见导航及 Q-Dir 实际读写 `.qdr` 仍待验收。此包仅供功能预览，不代表用户界面或 Q-Dir 互操作已验收通过。

从 `app/ExplorerDirs.App.exe` 启动；需要 .NET 10 Windows Desktop Runtime。该包配置独立，不会升级日常 profile。
