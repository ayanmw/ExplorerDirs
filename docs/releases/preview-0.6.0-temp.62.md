# Explorer Dirs 0.6.0 临时预览版 62

此包在 temp.61 基础上补充两处应用界面主题接线：照片打印窗口使用 Windows 动态系统表面/文字/边框颜色；每个窗格当前选中的标签显示系统强调色底边。ExplorerBrowser 文件列表仍由 Windows Shell 绘制，本版不宣称支持文件区深色或 Q-Dir 文件颜色规则。

## 本版变化

- 照片打印窗口背景、文字、预览底色与边框跟随 WPF 动态系统颜色；错误文字不再硬编码红色。
- 每个窗格的当前标签使用两 DIP 强调色下划线；保留默认 TabItem 模板、原生文件视图和标签数据/行为。
- 包位于 `D:\projects\explorer-dirs\releases\preview-0.6.0-temp.62`，入口为 `app\ExplorerDirs.App.exe`，配置位于独立的 `app\profile\`。

## 验证与限制

- 产品版本和 EXE/DLL SHA-256 以包内 `app\SHA256.txt` 与随包构建产物为准。
- 全量 Release `SettingsSmoke`、Pane Theme XAML smoke、App Release 构建和发布隔离 smoke 通过。
- 未启动程序窗口，未操作桌面；当前标签在四窗格的视觉效果、深色/高对比度下的可读性、打印窗口外观及 Q-Dir `.qdr` 颜色互操作仍待可见验收。
- 此包用于源码预览，不代表全应用主题或项目路线图已完成。
