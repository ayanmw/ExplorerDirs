# 临时预览 temp.36

## 本版内容

- Modern 地址栏按 F4 第一次打开当前路径所在的原生 Shell 文件夹树，再按一次进入路径编辑；Classic 样式直接进入路径编辑。
- 地址历史由独立下拉按钮打开，不再与 F4 共用。
- 每个窗格拥有独立的地址树 Popup。选择文件夹只导航该窗格当前标签；支持由当前 Windows Shell 解析并确认为文件夹的虚拟位置解析名。
- Escape、标签切换、窗格隐藏/卸载及主窗口失焦会关闭地址树。

## 运行位置

启动 `releases/preview-0.6.0-temp.36/app/ExplorerDirs.App.exe`。本预览使用独立 `app/profile/`，不会覆盖 `src` 调试目录或日常 profile。

## 已验证

- 地址树 F4 状态 smoke、Shell 位置边界 smoke、完整 Release SettingsSmoke 通过。
- App Release 构建通过，0 警告、0 错误。
- 发布隔离 smoke 通过；包内 `SHA256.txt` 可核对 EXE/DLL。

## 待桌面验收

- Popup 的实际摆放和窄窗格尺寸。
- F4 打开后原生目录树是否获得焦点，第二次 F4 是否正确聚焦地址编辑。
- Escape、窗口失焦、切换标签及隐藏窗格时的关闭与焦点行为。
- 桌面、此电脑、回收站、库、ZIP 和第三方 Shell 命名空间的可见定位/导航表现。

本轮没有启动应用、ExplorerBrowser 或 Q-Dir；构建与 smoke 不代表上述可见交互已通过。
