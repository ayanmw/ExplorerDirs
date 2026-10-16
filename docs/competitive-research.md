# 竞品调研（2026-10-15）

## 证据边界

初次检查时，本机 `C:\SoftwareOK\Q-Dir\Q-Dir.exe` 的文件版本为 12.73.0.0；收到对比截图后复查，已更新为 12.79.0.0（文件时间 2026-10-11）。同目录有 `Q-Dir.ini`、`start.qdr` 和 `Favoriten`。仅读取了文件元数据及 INI 的节名/键名，没有运行故障复现，也没有将其配置或二进制复制进本仓库。INI 可见 `[Q_View_1]`、`[Favoriten]`、`default_tab`、`show_tree`、`StartCrash` 等标识；它们只能说明有相应配置字段，不能推断实现细节或可靠性。

用户报告 Q-Dir 经常崩溃、环境变量变化后需要重启。本机近 30 天只读事件日志后来核对到 Q-Dir 的 16 条崩溃事件，按进程身份去重为 8 个实例；没有复现步骤和转储，不能归因于某个 Shell 扩展，也不能把事件数当崩溃率。见[脱敏证据记录](q-dir-crash-evidence-2026-10-16.md)。环境变量行为仍待直接验证。官网在初次调研时列出的最新版本为 12.79（2026-10-11）。来源：[Q-Dir 官网](https://www.softwareok.com/?seite=Freeware/Q-Dir)。

用户提供的 12.79 对比截图表明：Q-Dir 把文件、编辑、视图等功能放在顶部菜单，布局有独立快捷图标；文件列表的右键菜单最上方提供关闭、复制、恢复、创建标签等命令，下方还呈现 Windows Shell 与第三方扩展命令。本项目当前实现菜单与标签命令，Shell 扩展组合仍在路线图中。

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
