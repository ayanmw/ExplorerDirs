# 本机 Explorer Dirs 崩溃证据（2026-10-16）

本记录只保留定位所需的事件摘要，不复制事件中的程序路径、模块路径或其他私人数据。依据是 Windows Application 日志的 `Application Error` 1000 事件及同一时刻的 `.NET Runtime` 1026 事件。[微软的应用崩溃事件说明](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-crashing-behavior)将事件 1000 用作实际崩溃证据；单独的故障模块名不等于根因。

| 项目 | 观察 |
| --- | --- |
| 时间 | 2026-10-15 11:27:43（本机时间，UTC+8） |
| 程序类别 | `src/` 下的源码测试构建；不是项目 `releases/` 下的发布版 |
| Windows 故障模块 | `KERNELBASE.dll` |
| 异常码 | `0xe0434352` |
| 同时刻 .NET 异常 | `System.InvalidOperationException`：调度程序处理已暂停，但仍在处理消息 |
| 堆栈关键链 | `ExplorerBrowserHost.BuildWindowCore` → `Navigate` → `IExplorerBrowser.BrowseToIDList` → WPF `Dispatcher.WndProcHook` |

现有源码的 `BuildWindowCore` 已通过 `Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ...)` 延后首次 `Navigate`。包含此写法的提交 `1400ff0` 时间为 2026-10-15 11:37:36（UTC+8），晚于上述崩溃。此顺序支持“旧构建的同步导航与异常相关”的排查方向，**不能证明新构建已消除该异常**；还需要在用户留出的空闲桌面上用隔离配置复测。

截至 2026-10-16 的本机近 30 天只读查询归出 1 条源码测试构建的 `Application Error` 1000 事件，项目内发布版为 0 条。事件日志的时间窗口、运行次数和实际使用时长均不足以计算崩溃率；这些数字也不能证明第三方 Shell 扩展是否稳定。后续出现新事件时，先用 `scripts/crash-summary.ps1` 归类，再对同一时刻的运行时事件和具体构建版本作脱敏核对。
