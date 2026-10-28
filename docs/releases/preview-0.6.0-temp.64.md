# Explorer Dirs 0.6.0 临时预览版 64

本版在 temp.63 基础上加入应用主题强调色选择和窗口标题栏主题同步，使用独立 profile。主题变更保存在 `Theme.ini`；不会修改 Windows 系统主题，也不会影响 ExplorerBrowser 文件区原生绘制。

## 本版变化

- “工具 → 选项 → 主题 → 强调色”可跟随 Windows 强调色，或输入自定义 `#RRGGBB`；该选择与 System/Light/Dark 模式独立。
- `Theme.ini` 升级到格式 v2，仍可读取 v1；错误颜色会备份，更新冲突和未知未来格式受保护。
- Windows 11 主窗口和照片打印窗将应用强调色同步到 DWM 标题栏/边框；高对比度恢复系统颜色。旧 Windows 保留系统窗口框颜色。

## 验证与限制

- 产品版本：`0.6.0-preview.temp.64+b16b394881d3146c826b5ca1272cf188aa0a41bb`。
- EXE SHA-256：`AB14A78944F6762CCC087E6DE845A3E5CDC06907429AB350B419C64C1A279110`；DLL SHA-256：`7B1D7BC4543A76ACF95FA7D1A4DAB1742EEBB337A6D098EAA71CEDD49C80B61B`，与包内 `app\SHA256.txt` 一致。
- 隔离 profile 含 2 个配置文件，逐文件与主项目 profile 来源的 SHA-256 一致；来源未修改。
- `scripts/publish-smoke.ps1` 通过（fixture payload）；完整 Release `SettingsSmoke` 通过；App Release publish 成功。
- SettingsSmoke 覆盖配置迁移/保护、颜色往返和菜单/对话框接线；App Release build 0 警告、0 错误；`git diff --check` 通过。
- 本次没有启动发布程序或操作 GUI；颜色对话框、系统切色、多窗格刷新、标题栏实际显示和高对比度外观仍待可见验收。
- ExplorerBrowser 文件列表仍由 Windows Shell 绘制；本版不表示文件区已支持深色或 Q-Dir 文件颜色规则，也不代表完整 Q-Dir 兼容。
