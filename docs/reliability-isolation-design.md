# Shell 扩展崩溃：隔离与恢复设计草案

状态：证据和方案已整理；可预览、可撤销的 WER 转储配置工具已通过隔离注册表测试，但尚未在系统 WER 键启用。自动重启和多进程窗格也未启用。实际故障验收必须在用户留出的空闲桌面与隔离配置中进行。

## 目标与现状

用户需要 Q-Dir 式多窗格、原生文件视图和系统右键菜单，同时希望单个扩展故障不让日常文件管理反复中断。当前每个标签的 ExplorerBrowser 都在 `ExplorerDirs.App.exe` 进程内；[微软说明 Shell 扩展处理器是加载到 Shell 宿主进程中的 COM DLL](https://learn.microsoft.com/en-us/windows/win32/shell/shell-exts)。因此托管异常日志不能捕获所有原生崩溃，现有运行标记和会话保存只能在重新启动后帮助恢复。

[本机 Q-Dir 事件基线](q-dir-crash-evidence-2026-10-16.md)最初记录 8 个不同进程；2026-10-25 只读滚动复查扩展为 21 个进程、42 条事件。可读 WER 报告列出了 TortoiseGit/TortoiseSVN 模块，却没有可分析的转储；两程序当前均无 WER 专用转储配置。已加载模块、故障模块名或事件数量都不足以判定扩展责任。仍需取得调用栈和可复现操作，再决定是否需要把 Shell 视图移到进程边界之外。

## 方案比较

| 方案 | 能解决什么 | 代价与限制 |
| --- | --- | --- |
| 保持现有单进程，补充诊断与会话恢复 | 保留原生 Shell 菜单、焦点和窗格行为；确定失败阶段，缩短人工恢复时间 | 原生扩展仍能终止整个窗口 |
| 独立监督进程启动完整应用窗口 | 应用进程崩溃后，监督进程可在受控次数内重新打开已保存会话 | 不能阻止本次崩溃；自动重启和抢焦点必须由用户选择并在 GUI 中验收 |
| 每窗格独立 Shell 工作进程 | 理论上将某个窗格的扩展崩溃限制在其工作进程 | 跨进程子窗口、键盘焦点、原生菜单、拖放及 DPI 都要重新验证；[跨进程 `SetParent` 可能重置子进程 DPI 模式](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) |
| 自制文件视图 | 可完全控制进程与渲染 | 不能直接满足当前已确定的原生 Explorer 视图和右键菜单要求，不作为当前路线 |

## 推荐顺序

1. **建立真实故障基线。** 在空闲桌面用隔离配置验收当前版本的菜单、焦点、拖放和会话恢复；记录精确构建号、操作、目录类型、事件时间及是否存在同进程的成对事件。`crash-summary.ps1` 继续只输出脱敏摘要。
2. **按需收集调用栈。** `scripts/wer-dumps.ps1` 已提供 `Status`、`Preview`、`Enable`、`Disable`，只针对 `ExplorerDirs.App.exe` 或 `Q-Dir.exe` 的 WER 专用键。默认配置为最多两份小型转储；启用需要管理员 PowerShell 与 `-ConfirmDiagnosticCollection`，停用只移除仍符合本工具所有权标记的键，不删除转储。隔离 HKCU 测试验证了重复启用、外部修改保护和冲突拒绝；真实 HKLM 配置未执行。微软的[本地转储文档](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps)说明系统键需要管理员权限；转储可能包含浏览路径和文件内容，不入 Git、不默认上传。仅在用户主动选择的短期排查时段启用，完成后撤销。
3. **优先验证整窗恢复。** 若新构建仍发生原生崩溃，先做独立监督进程原型：区分正常退出与异常终止，只在用户开启恢复选项时重启；短时间内连续失败要停止重启并展示安全恢复入口。会话仍使用现有便携 INI 和冲突保护，不覆盖用户编辑。
4. **隔离仅作为后续原型。** 如果证据显示整窗恢复不足，再在独立测试环境试验每窗格进程。必须证明文件/空白处原生右键、跨窗格拖放、Tab/Shift+Tab、列标题、布局切换、不同 DPI 显示器和标签历史均可用，才决定是否替换现有宿主。

## 验收边界

- 正常退出不触发重启；异常退出不会无限循环，也不会覆盖另一个实例或外部编辑过的 INI。
- 自动恢复不能在用户使用其他窗口时擅自抢前台；是否自动打开窗口必须成为显式选项。
- 原生文件视图与系统右键菜单仍是产品约束；隔离原型只有在保留这些交互后才算可替代。
- 事件摘要和项目日志不写浏览路径、文件名或环境变量；转储另行保护，需单独同意采集与清理。

## 2026-10-25：PID 复用下的异常退出标记

SettingsSmoke 现使用当前烟测进程的 PID 构造一条启动时间不同的旧标记，并确认 `RunMarkerTracker` 将其识别为已结束进程、归档为 abrupt-exit 记录；测试专属目录每次先清理，避免旧归档满足本轮断言。完整 Release SettingsSmoke 通过。该回归只验证 PID 与启动时间共同识别进程实例，不证明能捕获原生 Shell 崩溃、区分崩溃与强制终止，或证明会话在异常退出时总是最新。

## 2026-10-25：本机原生菜单查询初始耗时

NativeProbe 在独立进程中创建仅含临时文件的隐藏 `IShellView`，分别查询背景、选中文件和选中文件夹的 `IContextMenu`，只记录 `QueryContextMenu` 耗时与文件夹菜单动词数量，不显示菜单、不执行动词、不访问用户目录。十四次运行中的背景查询耗时为 233.88–366.59 ms，单项文件菜单为 95.04–175.91 ms；六次文件夹菜单耗时为 192.35–323.37 ms，后五次均返回 98 个规范动词。文件夹菜单还通过 `Open`、`Delete`、`Rename`、`Properties` 的标准动词检查。每次原生菜单能力检查均通过，父窗口保持隐藏，前台未改变。

这些是本机当前 Shell 扩展集合的少量基线样本，耗时包含 Windows 与已注册菜单处理器的共同工作；不能归因到某个扩展、预测慢速/故障条件或代表 Explorer Dirs 界面总延迟。新增的 [`native-probe-watchdog.ps1`](../scripts/native-probe-watchdog.ps1) 将 NativeProbe 放在独立进程中，默认 30 秒结束期限，超时终止探针并保留标准输出/错误日志。正常 NativeProbe 退出样本通过；`-TestTimeoutPath` 使用独立睡眠子进程触发 5 秒期限，验证超时后子进程退出并保留日志。该自测验证监督器的计时/终止代码，不代表 NativeProbe 内 Shell 扩展挂起后的清理行为。菜单动词数量不能表明扩展责任或耗时占比；独立 NativeProbe 进程退出码 0 也不证明应用进程具有崩溃隔离。后续应在隔离测试时段覆盖不同菜单目标；若要归因扩展或收集崩溃调用栈，仍需用户单独选择诊断数据收集与适当的可见验收。

2026-10-25 追加五轮同配置隐藏探针样本：背景菜单查询中位数 209.56 ms（194.16–254.51 ms），选中文件菜单 98.08 ms（92.02–100.89 ms），选中文件夹菜单 172.84 ms（119.71–214.21 ms）；五个子进程均正常退出，日志都记录 `foreground-unchanged=True` 和 `product-untouched=True`。这是同机 `IShellView` 隔离宿主的 `IContextMenu::QueryContextMenu` 局部耗时，不等于产品点击菜单总延迟，也不能从混合处理器链路中归因任一第三方扩展；没有发现可据此声称为挂起或崩溃的样本。

只读预览示例：

```powershell
powershell.exe -NoProfile -File .\scripts\wer-dumps.ps1 -Mode Status -AppName Q-Dir.exe
powershell.exe -NoProfile -File .\scripts\wer-dumps.ps1 -Mode Preview -AppName Q-Dir.exe
```

`Status=missing` 只表示该程序没有本工具配置的专用键，不能据此断言系统没有全局 WER 配置。实际启用会写 `HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\<AppName>`；此系统诊断配置不属于项目的便携 INI。转储默认放在执行工具的用户 LocalAppData `ExplorerDirs\WerDumps\<AppName>`；若以另一个管理员账户提权，需显式指定目标程序可写的绝对目录。原文件不会自动复制或上传。
