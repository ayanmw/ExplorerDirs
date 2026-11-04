# 文件夹大小主动显示原型与后台验证

2026-11-04。本记录区分单元格显示、真实 Shell 通知、产品接入和可见验收；当前不是 temp.86 完成声明。

## 原因与实现

标准大小列身份统一后，普通文件 F5 丢失大小的问题已由用户确认修复。但真实视图没有为文件夹请求大小属性，后台扫描器因而没有被触发。Q-Dir 静态调查提供了主动处理显示文字的方向，见 qdir-folder-size-investigation.md。

独立实现的原型仅在 `EXPLORERDIRS_SIZE_TEXT_EXPERIMENT` 下编译：

- 在真实 DefView 的 `LVN_GETDISPINFOW` 默认处理后，补充普通目录的大小文字。文件、重解析点及其他列保持原处理。
- 通过 `IColumnManager` 的可见属性键识别标准 `PKEY_Size`，不按列标题语言或固定列位置识别。
- 通过 `IFolderView.Item` 确认当前行的 provider PIDL，复用既有后台数据源和缓存；显示回调不执行同步递归扫描。
- 只写入通知调用者原先提供的有界缓冲区，不覆盖 Shell 默认处理可能替换的内部字符串。
- 跟踪最多 256 个近期请求的行。完成通知通过窗口消息回到视图线程，重新核对行身份，只重绘文字已变化的行；不以每个结果触发全视图刷新或重排。
- 完成消息携带实例 cookie，避免 HWND 被复用后旧消息作用于新实例；销毁时清空原子窗口指针及行记录。

生产构建未启用此开关，也未启用实验通知桥；不把未通过完整通知资格检查的 DLL 发布给用户。

## 后台真实控件证据

`ProductSizeDisplayProbe.cs` 使用产品的 `ProfileShellNamespaceOwner`、真实 provider PIDL 和真实 Windows Shell 控件。提供直接 IShellView 和 ExplorerBrowser 两条入口，窗口仅在从不切换到用户屏幕的私有 Desktop 中存在。读取的是实际列表单元格，不是模拟返回值。

已通过的功能子检查：

| 检查 | 实际结果 |
| --- | --- |
| 初始显示 | 普通文件 2 KB，文件夹 12 KB、25 KB，空文件夹 0 B |
| 四视图共享 profile | 同名目录分别显示 12/31/32/33 KB，修改第一处不影响其余视图 |
| 失效后补值 | 文件夹由 12 KB 更新为 40 KB |
| 选择与焦点 | 补值后原普通文件仍为选中且聚焦 |
| F5 | 文件和文件夹大小保持正确 |
| 列重排 | 大小移到第三列后仍显示正确 |
| 数值排序 | 25/40 KB 升序与降序结果均正确 |
| 关闭与重新开启 | 关闭时目录留空且文件 2 KB 保留；开启后恢复目录数值 |
| ExplorerBrowser 销毁 | 四个容器 Destroy 返回成功，所跟踪的列表 HWND 无存活 |

早期单视图初始/F5 检查曾同时通过前台及输入不变保护。扩展四视图检查期间检测到了桌面输入变化，有的运行也检测到前台切换，因此**整轮隔离检查不记通过**；功能子检查单独记录，不能替代整轮保护结果，也不能据此断言变化由探针造成。

## 仍失败的发布门槛

新增外部文件后，物理 CREATE 通知应让原生列表从 4 行变成 5 行，且显示新文件 3 KB。目前超时。

隔离 trace 已确认：物理事件到达视图的独立订阅，转换后的 provider 通知也已发出。但这些事实不能证明 DefView 已把事件应用到其条目集合。测试保留失败断言，不通过手动 F5 掩盖自动更新失败。

对照设置 `SFVM_QUERYFSNOTIFY` 的递归监视没有改善，已撤回；该接口在当前系统会被调用，但文档列出的支持范围有限，不能将其作为已验证解决办法：
https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-queryfsnotify

另外，实验桥不再使用旧的 32 槽视图强引用表，而旧延期刷新/目录更新定时器仍从该表查找实例。即使当前单元格补值使用独立消息，也必须在正式接入前审查这些定时路径的引用和注销，不能直接打开两个实验宏作为最终产品方案。

## 复现与证据

构建：

```powershell
pwsh -NoProfile -File src/ExplorerDirs.NativeProbe/build-notification-bridge-experiment.ps1 -TraceSizeRequests -DisplayText
```

完整资格检查（目前应在物理新增行检查失败）：

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --product-size-display-browser-private-desktop src/ExplorerDirs.NativeProbe/bin/native-notification-bridge-experiment-x64-trace-display-text/ExplorerDirs.ShellNamespace.dll
```

运行日志保存在忽略目录 `src/.state/product-size-display-browser-qualification.log`。DLL 旁的 `.size-trace.log` 为有界进程日志，记录 `physical-view-subscription`、`physical-view-event`、`provider-view-forwarded` 和单元格补值。

正式构建路径的 25 项后台回归全部通过，报告为 `src/.state/background-checks/20261104-105510-f5c29f17c345466fbd2d8faaeda25497/summary.json`。其中 App、SettingsSmoke、HostProbe、NativeProbe 构建及数据源、Everything 协议、会话/列配置、异步扫描等检查不能替代上面仍失败的真实列表通知检查。

后续顺序：先让真实 DefView 正确消费新增/删除/重命名/属性通知，并验证各窗格隔离与筛选语义；补齐延期任务生命周期，再接入正式应用宿主，最后集中进行菜单、文件操作、重命名、拖放及可见体验验收。不覆盖现有 temp.86 预览。
