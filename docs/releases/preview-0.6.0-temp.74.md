# Explorer Dirs Preview 0.6.0 temp.74

发布位置：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.74\app\ExplorerDirs.App.exe`

本包基于提交 `e74ad26`，相较 temp.73 包含以下应用改动：

- 地址栏左右导航按钮可打开地址历史；地址输入遵循 Q-Dir 的路径解析/提交规则。
- 标签栏增加“始终显示”与“至少两个标签时显示”两种模式；隐藏标签栏时从 Tab/Shift+Tab 焦点顺序中移除其停靠点。
- 每个窗格显示当前 Shell 状态提示，按标签保留提示状态。
- `.qdr` 导入预览展示可识别的 Q-Dir 颜色规则模式，并明确这些颜色规则仍未导入或应用。

产品版本：`0.6.0-preview.temp.74+e74ad26a950edaf291730abb30dc3a00978dc769`

SHA-256：

- `ExplorerDirs.App.exe`：`37BC65EE5234C5F1754A56BBE85FB3D104AB8C9E4AF6C4033B04274208A98701`
- `ExplorerDirs.App.dll`：`8E696F29B81547A1DE98AC21101B6CB1767A477FACB1F39369AAAFA1433837D0`

验证：完整 Release `SettingsSmoke` 通过；App Release build 0 警告、0 错误；发布包 EXE/DLL 哈希与包内清单一致；独立 profile 含 `ExplorerDirs.ini` 和 `PinnedTabs.ini` 两个文件。预览程序未启动，也未修改默认文件夹打开方式、Win+E 路由或系统注册表。真实菜单、焦点、Shell 状态提示、Q-Dir 互操作及原生文件视图颜色仍需可见验收。
