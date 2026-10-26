# 竞品调研（初稿 2026-10-15；更新 2026-10-26）

## 证据边界

初次检查时，本机 `C:\SoftwareOK\Q-Dir\Q-Dir.exe` 的文件版本为 12.73.0.0；收到对比截图后复查，已更新为 12.79.0.0（文件时间 2026-10-11）。同目录有 `Q-Dir.ini`、`start.qdr` 和 `Favoriten`。仅读取了文件元数据及 INI 的节名/键名，没有运行故障复现，也没有将其配置或二进制复制进本仓库。INI 可见 `[Q_View_1]`、`[Favoriten]`、`default_tab`、`show_tree`、`StartCrash` 等标识；它们只能说明有相应配置字段，不能推断实现细节或可靠性。

用户报告 Q-Dir 经常崩溃、环境变量变化后需要重启。本机近 30 天只读事件日志后来核对到 Q-Dir 的 16 条崩溃事件，按进程身份去重为 8 个实例；没有复现步骤和转储，不能归因于某个 Shell 扩展，也不能把事件数当崩溃率。见[脱敏证据记录](q-dir-crash-evidence-2026-10-16.md)。环境变量行为仍待直接验证。初次调研时官网最新版本为 12.79（2026-10-11）；截至 2026-10-26，官网已列出 12.82（2026-10-24）。本机对照二进制仍是先前核实的 12.79.0.0，两者不应混作同一测试版本。来源：[Q-Dir 官网](https://www.softwareok.com/?seite=Freeware/Q-Dir) · [官方版本记录](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)。

用户提供的 12.79 对比截图表明：Q-Dir 把文件、编辑、视图等功能放在顶部菜单，布局有独立快捷图标；文件列表的右键菜单最上方提供关闭、复制、恢复、创建标签等命令，下方还呈现 Windows Shell 与第三方扩展命令。本项目现已在 ExplorerBrowser 原生菜单前插入自定义命令；背景命中与扩展兼容性仍待 GUI 验收。持续更新的具体差距见 [核心操作对照](shell-parity.md)。

## 2026-10-26 官方版本与地址栏目录树更新

Q-Dir 官方版本记录现列出 12.82（2026-10-24），更新说明包括“改进在 Explorer 地址栏中使用目录树”，但没有给出更详细的交互或字段变化。[FAQ 116](https://www.softwareok.com/?faq=116&seite=faq-Q-DIR) 说明现代地址栏第一次按 F4 显示每个 Explorer 视图的目录树，再按一次 F4 聚焦路径编辑框。本项目已实现每窗格 Shell 树 Popup 和两阶段 F4 路由；实现证据见[核心操作对照](shell-parity.md#地址栏样式与历史)，Popup 的可见定位、焦点、关闭及虚拟位置仍待 GUI 验收。该功能是现有能力与官方基线的对照，不把 12.82 的笼统更新说明当作已经确认的新接口或行为。[官方版本记录](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)

2026-10-25 复核 Program Starter 时，本机 `C:\SoftwareOK\Q-Dir\Q-Dir.ini` 的 `[Program]` 节只有 `portable_install=1`，当前没有可验证的实际启动项行。Q-Dir FAQ 46 公开说明 Ctrl+S、`Name=Path=Arguments[=Work directory]` 与目录/所选文件/多窗格占位符；Program Starter 专页说明保存于 Q-Dir.ini 并允许相对路径。Explorer Dirs 已接入独立 `ProgramStarter.ini`、Q-Dir `[Program]` 导入预览和显式导入；当前已用公开格式 fixture 验证解析，仍待真实桌面交互验收及带有效启动项的 Q-Dir.ini 正向样本核验。[FAQ 46](https://softwareok.com/?faq=46&seite=faq-Q-DIR) · [Program Starter 专页](https://www.softwareok.com/?seite=Freeware/Q-Dir/Programmstarter_in-Q-Dir)

## 功能对照

| 产品 | 官方可确认能力 | 本项目可借鉴之处 |
| --- | --- | --- |
| [Q-Dir](https://www.softwareok.com/?seite=Freeware/Q-Dir) | 四窗格、每窗格标签页、布局收藏、拖放、过滤/颜色、树、预览、便携 INI、目录大小等。官网称最多四组各 16 个标签页。 | 保留高密度多窗格工作流，优先改善会话恢复和运行时环境刷新。 |
| [Files](https://files.community/) / [源码](https://github.com/files-community/Files) | Windows 开源文件管理器，标签页、双窗格、现代 UI。 | 参考 Windows 集成与贡献流程；避免首版追求完整 Shell 功能。 |
| [Double Commander](https://doublecmd.sourceforge.io/) | 开源跨平台双窗格，标签页、后台操作、搜索、归档与插件。 | 参考批量文件操作及后台任务模型。 |
| [Explorer++](https://explorerplusplus.com/features) / [源码](https://github.com/derceg/explorerplusplus) | 开源 Windows 文件管理器，标签、书签、预览、过滤、拖放。 | 参考 Shell 兼容性、快捷键、便携配置。 |
| [FreeCommander](https://freecommander.com/fchelpxe/en/FreeCommander.html) | 双窗格、标签、过滤、便携；官网描述为 Windows 文件管理器替代品。 | 参考 Windows 常用操作的快捷键和布局习惯。 |

## 取舍结论

1. Q-Dir 的价值集中在四窗格加每窗格独立标签；首版应先让这个核心交互稳定可用。
2. 文件操作、Shell 命名空间和系统入口接管牵涉较多边界，必须在可恢复的浏览器基础上逐步加入。
3. 用户痛点“环境变量热刷新”须定义为可测试行为：收到 `WM_SETTINGCHANGE` 后更新本进程环境，由本程序直接创建的新子进程继承新值；已有进程不会被改变。Windows 的进程环境按父进程复制，见 [Microsoft 用户环境变量说明](https://learn.microsoft.com/en-us/windows/win32/shell/user-environment-variables) 和 [WM_SETTINGCHANGE 文档](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange)。
4. 文件浏览窗口替代与登录 Shell 替代是不同目标。微软的 [Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/) 面向 kiosk 等专用设备；本项目近期范围是前者。

调研仍缺：Q-Dir 崩溃的复现步骤与调用栈/转储、常用快捷键/配置偏好、真实大目录与网络盘性能数据。这些需要后续在用户实际工作流中收集。

## 2026-10-26 Q-Dir 官方快捷键对照复核

本轮重新核对 SoftwareOK 当前公开的 [Q-Dir 英文命令/字符串表](https://www.softwareok.com/?seite=Freeware/Q-Dir/language/English)，页面列出 Ctrl+A、Ctrl+Shift+A、Ctrl+C/X/V、Ctrl+N、Ctrl+Q、Ctrl+S、Ctrl+M、F3、Alt+F3、Backspace、Alt+Left/Right、F5、Shift+F6 和 Alt+L 等快捷键/提示。

- 文件选择、剪贴板、删除、刷新等基础命令由 Explorer Dirs 原生 ExplorerBrowser/Shell 路径处理；Alt+Left/Right、Backspace、Ctrl+Q、Ctrl+S、Ctrl+M、F3 与 Alt+F3 已在应用侧接入。实现清单及待 GUI 项见[快捷键表](keyboard-shortcuts.md)。
- 两个明确保留的交互差异：Q-Dir 的 Ctrl+N 是新建文件夹，Explorer Dirs 用 Ctrl+N 创建独立会话窗口，并以 Ctrl+Shift+N/菜单/工具栏新建文件夹；Q-Dir 的 Shift+F6 切换查看方式，Explorer Dirs 用 F6/Shift+F6 正反向遍历主要焦点区域，另用 Ctrl+Shift+1～8 与 Ctrl+滚轮切换查看方式。这些选择符合已确认的多窗口和 Explorer 键盘焦点方案，不因对照而重映射。
- Alt+L 屏幕放大镜属于辅助工具，不在当前文件管理核心范围；记录为低优先级 Q-Dir 差异，不把它混入 Shell 文件操作或抢占系统级快捷键。

该复核没有发现应立即改映射的核心快捷键缺口，因此没有改快捷键代码。公开字符串/帮助不能证明本机 Q-Dir 12.79 的实际按键运行时行为；Q-Dir 与 Explorer Dirs 的真实键盘比较仍待获准的隔离可见验收。
