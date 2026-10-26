# Explorer Dirs 0.6.0 临时预览 temp.30

## 本版相较 temp.29

- 地址栏导航现在接受以 `/` 分隔的绝对盘符和 UNC 路径，并规范化重复的 Windows 路径分隔符，例如 `C:/Windows/`、`C:\\Windows\\` 和 `//server//share/folder`。
- 仅转换绝对文件系统路径；Shell 虚拟位置名称、URI、相对输入和 `\\?\` / `\\.\` 设备路径保持原样，目录解析继续交由 Windows Shell。

## 验证

- Release `SettingsSmoke`：通过，覆盖盘符/UNC 分隔符以及虚拟位置、URI、相对路径和设备路径的保留规则。
- App Release 构建：通过，0 警告、0 错误。
- 发布使用独立 profile；EXE 与 App DLL 校验值见包内 `app/SHA256.txt`。
- 未启动 GUI。地址栏焦点、物理输入、实际本地/UNC Shell 导航及四窗格可见行为仍待验收。

## 使用

运行 `app/ExplorerDirs.App.exe`。本预览使用隔离 profile；请保留 `app` 目录结构。

EXE 与 App DLL 校验值以包内 `app/SHA256.txt` 为准。
