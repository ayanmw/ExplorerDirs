# Explorer Dirs 0.6.0 临时预览 temp.32

## 本版相较 temp.31

- 地址栏右侧新增独立的历史下拉按钮，与 F4 打开相同的当前标签最近访问路径列表。
- 路径编辑框继续使用自己的原生右键菜单；历史入口不拦截输入框右键。
- 历史按钮加入每窗格 Tab/Shift+Tab 焦点顺序，位于地址控件之后、转到按钮之前。

## 验证

- Release `SettingsSmoke`：通过；焦点遍历覆盖 64 种动态控件组合及 Tab/Shift+Tab 互逆。
- App Release 构建：通过，0 警告、0 错误。
- 发布隔离 smoke：通过，确认 profile 文件仅作为预览数据复制；未启动可执行文件。
- EXE SHA-256：`90F52275EB60F8F0FB0EF459EA485B2B00B12424E5466429998CC58CF2E04C17`
- DLL SHA-256：`C61F7C467C51FC69C7688A221992EECB3206BA1EB94E4DB6481B6F0D811BAA42`
- 未启动 GUI。按钮在窄四窗格下的排版、菜单鼠标/键盘操作和地址输入框右键菜单仍待可见验收。

## 使用

运行 `app/ExplorerDirs.App.exe`。本预览使用独立 profile；请保留 `app` 目录结构。

EXE 与 App DLL 校验值以包内 `app/SHA256.txt` 为准。
