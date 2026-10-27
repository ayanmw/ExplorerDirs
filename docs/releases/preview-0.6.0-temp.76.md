# Explorer Dirs Preview 0.6.0 temp.76：Windows 深浅主题

入口：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.76\app\ExplorerDirs.App.exe`。

选项位置：**工具 → 选项 → 主题 → 跟随 Windows / 固定浅色 / 固定深色**。缺少 Theme.ini 时默认跟随 Windows；已有显式固定偏好继续保留，用户可在菜单切回跟随。设置保存在本版独立 `app\profile\Theme.ini`，不用注册表保存应用偏好。强调色保留原有系统/自定义选项，本批不增加个性化规则。

temp.75 已有三态菜单与保存，但系统检测把浅色前景文字误判为浅色主题；本批去掉多余反转，按 Windows 官方语义判为深色。运行中继续使用 UISettings.ColorValuesChanged 通知更新系统颜色，固定模式不受系统深浅切换影响，高对比度保留原有系统色处理。

专项 `--system-theme` 先在当前 Windows 深色设置下复现检测不符，修正后验证真实 monitor、实际 ApplicationThemeController 三态解析和 INI 重建恢复，未创建窗口/Shell view，前台不变。系统真实切换时的通知、可见绘制仍待验收。完整配置检查与发布构建结果记录在仓库路线图。

范围：应用自身 WPF 界面、已接入主题的弹窗和标题栏。仍使用 ExplorerBrowser；本批没有引入未公开 Shell 深色接口，不能保证原生文件列表、目录树、系统对话框和第三方菜单全部同步深色，也不代表逐项颜色原型已接入。

本包使用从 temp.75 复制的独立 profile，不改旧版本配置，不自动启动程序。

发布核对：版本 `0.6.0-preview.temp.76+1d4fbadb559ad22220ec68df42e60323d3ea44ab`；EXE SHA-256 `DA173D6E7EAA0C6485535198BA5E9A8CC1D2BAC8EA4C9B8F471FA8D586EF0ACA`；DLL SHA-256 `CFB6B30C0FF8BBB241C684E692886D4CEB64D2F654B21ED2FF3F66A6A974ECBF`，与包内 SHA256.txt 一致。完整 Release SettingsSmoke 与 publish 通过；profile 包含会话、位置、固定标签和界面偏好四份 INI，没有 Theme.ini 时默认跟随 Windows。程序未由本任务启动。
