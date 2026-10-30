# 竞品调研（初稿 2026-10-15；更新 2026-10-26）

## 证据边界

初次检查时，本机 `C:\SoftwareOK\Q-Dir\Q-Dir.exe` 的文件版本为 12.73.0.0；收到对比截图后复查，已更新为 12.79.0.0（文件时间 2026-10-11）。同目录有 `Q-Dir.ini`、`start.qdr` 和 `Favoriten`。仅读取了文件元数据及 INI 的节名/键名，没有运行故障复现，也没有将其配置或二进制复制进本仓库。INI 可见 `[Q_View_1]`、`[Favoriten]`、`default_tab`、`show_tree`、`StartCrash` 等标识；它们只能说明有相应配置字段，不能推断实现细节或可靠性。

用户报告 Q-Dir 经常崩溃、环境变量变化后需要重启。本机近 30 天只读事件日志后来核对到 Q-Dir 的 16 条崩溃事件，按进程身份去重为 8 个实例；没有复现步骤和转储，不能归因于某个 Shell 扩展，也不能把事件数当崩溃率。见[脱敏证据记录](q-dir-crash-evidence-2026-10-16.md)。Explorer Dirs 的环境变量热刷新已通过隔离 HWND、真实注册表变量和新 `cmd.exe` 子进程验证；Windows 自身广播送达可见产品窗口仍待验收，详见[路线图记录](roadmap.md#2026-10-28隔离验证环境变量设置变更刷新)。初次调研时官网最新版本为 12.79（2026-10-11）；截至 2026-10-26，官网已列出 12.82（2026-10-24）。本机对照二进制仍是先前核实的 12.79.0.0，两者不应混作同一测试版本。来源：[Q-Dir 官网](https://www.softwareok.com/?seite=Freeware/Q-Dir) · [官方版本记录](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)。

用户提供的 12.79 对比截图表明：Q-Dir 把文件、编辑、视图等功能放在顶部菜单，布局有独立快捷图标；文件列表右键包含 Windows Shell 与第三方扩展命令，标签操作则通过标签栏上下文菜单提供。随后用户明确要求文件区菜单只保留原生 Shell 内容，因此 Explorer Dirs 当前不在文件区菜单前插入自定义命令；标签命令保留在标签栏菜单中。背景点击、第三方扩展和原生菜单主题的可见验收记录见[核心操作对照](shell-parity.md)。

2026-10-29 复核 Q-Dir FAQ 117 的 `.qdr` 示例：`Tree1split`～`Tree4split` 仍只给出数值示例 `2500`，没有说明单位、基准宽度或方向。它确认这些字段存在，但不足以安全推导目录树像素宽度；导入器继续明确跳过，不写入猜测值。[Q-Dir FAQ 117](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)

## 2026-10-26 官方版本与地址栏目录树更新

Q-Dir 官方版本记录现列出 12.82（2026-10-24），更新说明包括“改进在 Explorer 地址栏中使用目录树”，但没有给出更详细的交互或字段变化。[FAQ 116](https://www.softwareok.com/?faq=116&seite=faq-Q-DIR) 说明现代地址栏第一次按 F4 显示每个 Explorer 视图的目录树，再按一次 F4 聚焦路径编辑框。本项目已实现每窗格 Shell 树 Popup 和两阶段 F4 路由；实现证据见[核心操作对照](shell-parity.md#地址栏样式与历史)，Popup 的可见定位、焦点、关闭及虚拟位置仍待 GUI 验收。该功能是现有能力与官方基线的对照，不把 12.82 的笼统更新说明当作已经确认的新接口或行为。[官方版本记录](https://www.softwareok.com/?seite=Freeware/Q-Dir/History)

2026-10-25 复核 Program Starter 时，本机 `C:\SoftwareOK\Q-Dir\Q-Dir.ini` 的 `[Program]` 节只有 `portable_install=1`，当前没有可验证的实际启动项行。Q-Dir FAQ 46 公开说明 Ctrl+S、`Name=Path=Arguments[=Work directory]` 与目录/所选文件/多窗格占位符；Program Starter 专页说明保存于 Q-Dir.ini 并允许相对路径。Explorer Dirs 已接入独立 `ProgramStarter.ini`、Q-Dir `[Program]` 导入预览和显式导入；当前已用公开格式 fixture 验证解析，仍待真实桌面交互验收及带有效启动项的 Q-Dir.ini 正向样本核验。[FAQ 46](https://softwareok.com/?faq=46&seite=faq-Q-DIR) · [Program Starter 专页](https://www.softwareok.com/?seite=Freeware/Q-Dir/Programmstarter_in-Q-Dir)

## 2026-10-27 官方版本基线复核

再次核对 [Q-Dir 官方产品页](https://www.softwareok.com/?seite=Freeware/Q-Dir)：当前仍为 12.82，更新日期 2026-10-24；版本说明只具体提到改进地址栏目录树使用，其余为一般 Quad-Explorer 优化和语言文件更新，没有给出可据此实现的新交互规格。Explorer Dirs 已有 Modern 地址栏 F4 两阶段树/路径编辑、祖先导航及“此电脑”驱动器子项。本轮不据笼统的“优化”文字推测新行为；当前实际差距应通过之后的可见 Q-Dir/Explorer Dirs 对照记录，而不是再扩写未经证实的版本功能。

## 2026-10-28 官方大小信息行为复核

Q-Dir [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 将“Size information”描述为可切换的逐行文件/文件夹大小显示，入口为 Extras 菜单或 F9；文件夹递归大小复用 Size 列，大小文字可带同一目录中文件与文件夹大小的相对百分比。官方说明没有定义百分比的精确分母、隐藏项/失败扫描的处理及舍入策略，因此这些规则仍需可见行为确认。

Explorer Dirs 当前 F9 按此前确认用于显式递归汇总整个目录树到状态栏，和 Q-Dir 的“切换逐行大小信息”不是同一行为；继续保留用户选定的 F9 语义。逐行列仍受 [Shell 视图架构门槛](folder-size-column-investigation.md)限制，本轮没有引入猜测公式或代码实现。

## 2026-10-31 官方 Soft-Refresh 行为补查

Q-Dir 的公开资料实际列出两个不同选项，不能合并解释：语言表 ID 53016 是“Soft-Refresh, not re-enumerate the view (drop, copy, refreshed)”；ID 53033 是“Disable auto sorting when rename or copy items”。FAQ 112 把后一个新名称称为 Soft-Refresh，并描述其在新增项目时避免自动重排；FAQ 139 将该开关放在“Tools → List View”中。由此可确认“不重枚举”和“禁用自动排序”是不同的 Q-Dir 行为入口，但公开资料没有说清二者内部交互、逐操作刷新时机及失败回退。[Q-Dir FAQ 112](https://www.softwareok.com/?faq=112&seite=faq-Q-DIR)、[FAQ 139](https://www.softwareok.com/?faq=139&seite=faq-Q-DIR)、[官方语言表](https://www.softwareok.com/?seite=Freeware%2FQ-Dir%2Flanguage%2FEnglish-UK)

Windows 的 `FWF_AUTOARRANGE` 公开语义是自动排列视图元素；`FWF_NOENUMREFRESH` 的语义是不重新枚举视图。现有证据不足以证明其中任一 flag 可安全复刻 Q-Dir 的完整行为。Explorer Dirs 尚无这两个独立设置；实现前应先分别验证复制/拖放/重命名的排序行为和 F5/外部变化的内容新鲜度，并再决定持久化范围，避免把“不重枚举”误用于普通刷新。

## 2026-10-27 Win+E 入口对照

Q-Dir 帮助页概述支持把 Win+E 设为启动 Q-Dir；更具体的 [FAQ 81](https://www.softwareok.com/?faq=81&seite=faq-Q-DIR) 给出的做法是使用 QuickTextPaste，把 `E+L-Win` 绑定到 `run:%PATH%/q-dir.exe`。因此，公开证据确认的是可通过外部快捷键工具转发 Win+E，并未说明 Q-Dir 自身注册全局热键、替换 Explorer 或改写文件夹关联。此处是基于官方操作步骤的判断，不推断 Q-Dir 的内部实现。

Explorer Dirs 已具备单目录命令行启动和可选、可逆的每用户文件夹/磁盘菜单脚本；这与 Win+E 热键路由是不同入口。鉴于 Win+E 是系统级组合键且可能与其他快捷键工具冲突，后续设计须先明确应用自身的可逆启停与冲突恢复流程，不使用未经验证的全局键盘钩子或注册表键。当前仍未接管 Win+E；系统入口验收继续列在 [路线图 M3](roadmap.md#系统入口接管) 中。

## 2026-10-27 Q-Dir 文件筛选行为对照

复核 Q-Dir 官方 [FAQ 37](https://www.softwareok.com/?faq=37&seite=faq-Q-DIR) 与 [FAQ 38](https://www.softwareok.com/?faq=38&seite=faq-Q-DIR)：Q-Dir 说明每个文件视图分别保存筛选设置；筛选支持多个 `*`/`?` 通配模式、正向或反向匹配、可选择是否筛选文件夹、保存筛选收藏，并可通过列表列标题筛选。FAQ 37 还把其筛选称作 “effective tagging filter”，但没有解释这个术语是否意味着仅高亮、选择标记或额外筛选行为，因此不据此增加新的“标记”模式。

Explorer Dirs 已按标签保存独立过滤规则和筛选收藏，支持分号分隔模式、包含/排除以及可选筛文件夹；这覆盖了 FAQ 38 明确描述的主要规则，并比每视图保存扩展到每标签保存。ExplorerBrowser 使用原生 Shell 视图，故列标题筛选预期沿用系统能力；与快速筛选同时启用时是否只影响当前视图，仍需四窗格可见验收。筛选策略源码见[核心操作对照](shell-parity.md)；本轮没有新增筛选实现或改变路线图优先级。

Q-Dir [FAQ 84](https://www.softwareok.com/?faq=84&seite=faq-Q-DIR) 另说明筛选输入框支持历史自动完成：按向下键展开，选中某条后按 Delete 删除该记录。Explorer Dirs 现将已应用的非空模式记录到独立便携历史 INI，保留最近 50 项，并按此键盘约定接入历史下拉；它与每标签筛选状态和命名筛选收藏分开保存。下拉的实际键盘操作仍待可见 GUI 验收。

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
- Alt+L 屏幕放大镜属于辅助工具，不改变文件管理核心范围；本项目现以“工具 → 屏幕放大镜”和 Alt+L 直接启动 Windows Magnifier 对齐入口，不复制另一套放大器，也不注册全局快捷键。实际启动仍待可见验收。

该复核没有发现应立即改映射的核心快捷键缺口，因此没有改快捷键代码。公开字符串/帮助不能证明本机 Q-Dir 12.79 的实际按键运行时行为；Q-Dir 与 Explorer Dirs 的真实键盘比较仍待获准的隔离可见验收。
