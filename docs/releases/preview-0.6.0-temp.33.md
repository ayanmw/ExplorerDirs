# Explorer Dirs 0.6.0 临时预览 temp.33

## 本版相较 temp.32

- 地址栏历史按钮在紧凑窗格中将按钮和布局占位同步缩窄至 20 DIP，普通模式保持 24 DIP。
- 按钮仍与 F4 打开相同的当前标签最近访问路径列表；路径编辑框的原生右键菜单保持原样。

## 验证

- Release `SettingsSmoke`：通过；焦点遍历覆盖 64 种动态控件组合及 Tab/Shift+Tab 互逆。
- App Release 构建：通过，0 警告、0 错误。
- 发布隔离 smoke：通过；预览包生成时未启动可执行文件。
- EXE SHA-256：`B47375DBC9B0A393C7C974368634C0DA82A148485BC2D9C01BC703EB392AF9C3`
- DLL SHA-256：`AF8A0C8B254B6205EC2AC0150C88CBAE3F2B53D41BAE884A1FF85F7166448AEB`
- 未启动 GUI。按钮在窄四窗格下的排版、菜单鼠标/键盘操作和地址输入框右键菜单仍待可见验收。

## 使用

运行 `app/ExplorerDirs.App.exe`。本预览使用独立 profile；请保留 `app` 目录结构。

EXE 与 App DLL 校验值以包内 `app/SHA256.txt` 为准。
