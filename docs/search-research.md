# 当前目录搜索：实现依据与边界

状态：原生搜索位置与防阻塞服务站点已接入源码并通过无窗口测试；搜索结果导航、输入框和快捷键尚未接入。真实窗口验收须等用户留出空闲桌面。

## 已确认的 Shell 能力

- Windows 的 `ISearchFolderItemFactory` 可指定查询条件和搜索范围，并生成表示结果的 Shell item 或 PIDL；结果仍由 Shell 文件视图承载。[接口文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-isearchfolderitemfactory)、[微软示例](https://learn.microsoft.com/en-us/windows/win32/shell/samples-searchfolder)
- Search Folder 能使用索引，也能对指定范围手动枚举；因此不能仅以“该目录未建索引”为由改做自制文件列表。但非索引、网络和离线位置的速度及取消行为必须实际测量。[Windows Search 概览](https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-wds-overview)
- `IExplorerBrowser` 文档警告：搜索文件夹在默认行为下可能于 UI 线程过滤，直到搜索完成才响应。接入前需通过 `SID_SExplorerBrowserFrame` 提供 `ICommDlgBrowser2`，让 `GetViewFlags` 返回 `CDB2GVF_NOINCLUDEITEM`，并验证该服务确实被 Shell 查询。[ExplorerBrowser 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)

## 已完成的无窗口接入

- 当前实现选择 Windows 原生 `search-ms:query=…&crumb=location:…` 作为查询位置，参数分别 URL 编码。微软文档明确列出本地与网络范围的 `location` 用法。[search-ms 参数](https://learn.microsoft.com/en-us/windows/win32/search/getting-started-with-parameter-value-arguments)、[范围参数](https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-wds-qryidx-crumb)
- 本机 Shell 无窗口探测及控制台回归证实：范围查询可解析成虚拟 PIDL；它没有 `SIGDN_FILESYSPATH`，有 `search-ms:` 解析名。`ShellSearchLocation` 负责构造、读取和解析该位置。
- `ShellBrowserSite` 在 ExplorerBrowser 初始化前设置站点，向 `SID_SExplorerBrowserFrame` 提供 `ICommDlgBrowser2` 和 `CDB2GVF_NOINCLUDEITEM`。控制台测试核对 COM vtable 标志、基接口以及真实 ExplorerBrowser 对象的 `GetSite` 往返。Shell 视图实际调用此服务仍待 GUI 验收。

## 当前源码需要改变的边界

- `ExplorerBrowserHost.Navigate` 对输入调用 `Path.GetFullPath` 和目录存在性探测，只接受普通文件系统路径；Search Folder 需要专用的 Shell item / PIDL 导航入口。
- `OnNavigationComplete` 只在 `FileSystemPath(pidl)` 返回路径时更新当前状态。搜索结果是虚拟 Shell 位置，需分别记录搜索范围的文件系统路径与浏览器当前的 Shell 位置；不能把搜索 URI 写入现有路径字段或面包屑。
- 搜索视图仍应保留原生 Shell 的文件操作、图标、右键菜单、排序和分组。先验证搜索文件夹导航、返回原目录及 TravelLog，再开放 F3、Ctrl+E、Ctrl+F 和窗格内搜索框。单独让快捷键聚焦一个没有结果能力的输入框不算完成。

## 验证门槛

1. 控制台或无窗口验证搜索范围/查询条件构造及非文件系统位置识别；不得启动交互窗口。此项已通过本机回归。
2. 空闲桌面时，在隔离测试配置中验证当前目录搜索、返回目录、后退/前进、双击结果、文件/空白处右键以及多窗格焦点。
3. 分别测试已索引目录、未索引本地目录及网络目录，观察 UI 响应和取消；结果与配置恢复策略在实际行为明确后定稿。
