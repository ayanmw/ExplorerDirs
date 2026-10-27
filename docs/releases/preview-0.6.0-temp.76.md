# Explorer Dirs Preview 0.6.0 temp.76：同目录主题修复候选

入口：`D:\projects\explorer-dirs\releases\preview-0.6.0-temp.76\app\ExplorerDirs.App.exe`。

选项位置：**工具 → 选项 → 主题 → 跟随 Windows / 固定浅色 / 固定深色**。缺少 Theme.ini 时默认跟随 Windows；已有显式固定偏好继续保留，用户可在菜单切回跟随。设置保存在本版独立 `app\profile\Theme.ini`，不用注册表保存应用偏好。强调色保留原有系统/自定义选项，本批不增加个性化规则。

temp.75 已有三态菜单与保存，但系统检测把浅色前景文字误判为浅色主题；本批去掉多余反转，按 Windows 官方语义判为深色。运行中继续使用 UISettings.ColorValuesChanged 通知更新系统颜色，固定模式不受系统深浅切换影响，高对比度保留原有系统色处理。

专项 `--system-theme` 先在当前 Windows 深色设置下复现检测不符，修正后验证真实 monitor、实际 ApplicationThemeController 三态解析和 INI 重建恢复，未创建窗口/Shell view，前台不变。系统真实切换时的通知、可见绘制仍待验收。完整配置检查与发布构建结果记录在仓库路线图。

首次 temp.76 的检测修复不足以构成深色模式，用户截图显示菜单与文件区白块，因此同目录重发本修复候选：WPF 菜单/按钮/标签/工具栏/提示框使用完整语义色模板；保留 ExplorerBrowser，通过 SDK IVisualProperties 为原生文件视图设置文字及背景，列标题使用原生 custom-draw 文字色。Windows 10 19041+ 的系统控件/菜单还使用了版本限定的 uxtheme 兼容入口（含非公开 ordinal），并不构成所有 Windows/第三方扩展的公开兼容承诺。

已取得：隔离真实四窗格文件主体、列标题及三级主题菜单的截图证据；WPF 菜单正常/高亮/禁用及提示框的实际离屏像素、三态 INI 重建、原生颜色 COM slot/错误检查、完整配置检查通过。**最新右键外框、Shell 文件提示、工具栏溢出和运行中三态切换还需用户确认；不宣称已达到 Windows 11 资源管理器完整一致性。**

本次按用户明确授权覆盖原 temp.76 目录，保留当前 profile，旧包移入 releases/.history 后再替换。确切构建版本和 EXE/DLL 哈希见包内 app/SHA256.txt 与 BUILD-INFO.json。当前候选仍需上述人工检查，工具的 ESC 停止信号不解释为用户取消开发，但受到工具前台拦截时不能补写未取得的 GUI 结果。
