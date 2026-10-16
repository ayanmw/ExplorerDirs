# 本机 Q-Dir 崩溃事件基线（2026-10-16）

只读查询本机最近 30 天 Windows `Application Error` 1000 事件，筛选 `AppName=Q-Dir.exe`。命令：

```powershell
powershell.exe -NoProfile -File .\scripts\crash-summary.ps1 -AppName Q-Dir.exe -Days 30 -AsJson
```

| 指标 | 观察 |
| --- | --- |
| 时间范围 | 2026-09-16 至 2026-10-16（查询时的滚动 30 天窗口） |
| Windows 崩溃事件 | 16 条 |
| 不同进程实例 | 8 个；按事件中的进程 ID 与创建时间组合去重 |
| 身份字段缺失 | 0 条；本次去重覆盖全部事件 |
| 文件版本 | 12.73.0.0：7 个进程；12.79.0.0：1 个进程 |
| 同一进程的事件 | 每个进程各有 `0xc0000005` 和 `0xc000041d` 两条，故障模块名均为 `Q-Dir.exe` |

[`0xc0000005` 是访问冲突](https://learn.microsoft.com/en-us/shows/Inside/C0000005)；[`0xc000041d` 表示用户回调函数内发生致命异常](https://learn.microsoft.com/en-us/troubleshoot/microsoftteams/teams-administration/resolve-teams-client-crash-issues)。同一进程的两条事件不能算作两次独立崩溃。这组事件证明这些 Q-Dir 进程发生了崩溃，**不能仅凭故障模块名判断 Q-Dir 本身还是某个 Shell 扩展先造成损坏**，也不能从事件数计算崩溃率。仍缺复现操作、调用栈/转储、运行时长与扩展对照实验。

同窗口中，Explorer Dirs 有 1 条 `src/` 源码测试版事件、项目 `releases/` 发布版 0 条；源码测试事件另见[本项目崩溃证据](crash-evidence-2026-10-16.md)。这两个产品的使用时长和测试条件不同，数字不能直接比较稳定性。脚本输出不含完整程序路径、浏览目录、进程 ID 或创建时间；后两项只用于内存中去重。
