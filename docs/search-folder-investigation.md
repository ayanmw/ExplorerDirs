# 内嵌搜索修正（2026-10-21）

状态：源码候选已通过原生搜索结果与历史验证，**真实应用窗口验收及发布仍待完成**。

## 已有缺陷与根因证据

temp.2/temp.3 的真实窗口输入搜索后显示“无法打开搜索结果”，并打开独立 Explorer 搜索窗口。
原代码直接 `SHParseDisplayName(search-ms URI, null)`，只验证 PIDL 不为空及字符串能读回，未验证目标是不是文件夹。
本机独立属性检查显示：默认解析的对象 `SFGAO_FOLDER=false`、`SFGAO_BROWSABLE=true`。它是协议浏览目标，不能据 URI/PIDL 非空认定可嵌入。

使用微软文档中的 [STR_PARSE_PREFER_FOLDER_BROWSING](https://learn.microsoft.com/en-us/windows/desktop/shell/str-constants) 绑定上下文后，同一查询得到 `SFGAO_FOLDER=true`、`SFGAO_BROWSABLE=false`。源码现在使用该解析方式，并在导航前检查文件夹属性；若仍返回非文件夹，报错且不调用 BrowseToIDList。

## 查询身份与原生历史

直接切换解析方式还不够：

1. 默认搜索文件夹解析名称只包含目录描述，不区分查询。
2. 两个不同查询的 `ILIsEqual` 在本机返回 true，但 PIDL 字节不同。
3. Shell 又会在导航 pending/complete 和当前视图持久化接口中重写 PIDL；首次仅用原始字节摘要的实现出现“结果正确，查询身份恢复失败”。该失败由原生探针捕获。

现通过 search-ms 的 displayname 参数指定“查询 — 完整目录”，保留用户可读的搜索名称。每个 ExplorerBrowserHost 独立记录自己发出的原生位置：先匹配完整 PIDL 摘要，再匹配本浏览器确实登记的精确名称；名称碰撞对应不同请求时拒绝，不能覆盖旧查询。普通窗口标题、任意外来描述和其他浏览器的映射不作为恢复依据。

导航就绪状态、成功/失败回调以及实际位置复核统一通过该映射恢复原查询 URI。目录路径仍保存真实 scope；查询映射仅在本标签浏览器生命周期中保留，不写 INI。

代码审查另发现 Shell 解析可能泵送 STA 消息：准备中的旧搜索可能在新导航之后恢复执行。现以请求序号和浏览器实例在解析、名称登记及异常返回后复核；目录导航、历史导航、原生导航开始及销毁均使旧准备失效。新搜索立即取消旧目录探测，旧目录解析成功和失败分支均检查探测归属。该重入防护目前经过源码审查，尚未做真实窗口重入注入验收。

## 验证

- Settings smoke 新增独立 IShellItem 属性断言。旧代码首先失败：`Search navigation must target a native folder, not a protocol launch item.`
- 修正后 Settings smoke 通过，包括包含中文及 URI 分隔符的查询、两个不同查询、浏览器间隔离、强制相同原生 caption 的冲突拒绝与旧映射保留。
- `dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --search-folder-navigation` 使用隐藏原生 ExplorerBrowser，验证：
  - 查询 three：仅返回 three.txt，恢复正确查询身份；
  - 查询 five：仅返回 five.txt，恢复正确查询身份；
  - 原生后退：恢复 three.txt 和对应查询；
  - 原生前进：恢复 five.txt 和对应查询。
- 探针没有显示窗口、输入键鼠或设置焦点，并检查父窗口隐藏、前台未变化。
- App Release 构建 0 警告、0 错误。尚未发布，不改变 temp.3 二进制。

### 生产 WPF 宿主集成与快速导航

新增 `dotnet run --project src/ExplorerDirs.HostProbe -c Release -- --search-navigation`：使用产品的四个真实 ExplorerBrowserHost，隐藏 WPF 宿主以禁止搜索后的焦点恢复。所有目录和文件均由探针创建，不读取用户 profile、不输入键鼠、不操作剪贴板。

八阶段依次检查初始目录、两次不同搜索、后退、前进、返回目录、搜索后立即返回目录、搜索后立即提交另一查询。每阶段同时要求真实 Shell 文件身份、产品 SearchQuery/CurrentPath、导航就绪状态及对外发布的 Navigated/SearchNavigated 事件一致。搜索状态下捕获的目录配置必须保持原快照。

快速导航用例先复现两个问题：

1. 搜索后立即 Navigate，四个窗格最终均停在旧搜索。较晚到达的 app 搜索 Pending 回调被当成外来导航，取消了后发目录探测。现记录已发搜索，完成/失败时退休；已发搜索的 Pending 不再取消新请求。
2. 关闭上述路径后，原生 Browse 返回 `ERROR_BUSY (0x800700AA)`。现仅对该错误让出 STA 消息循环，每 50 ms 重试导航受理，最多 5 秒；每次都复查请求是否仍有效，过期即取消，不轮询刷新文件列表。其他错误仍直接报告。

修正后八阶段四窗格集成检查通过。这些快速连续调用不等同于在 Shell 解析内部精确注入 STA 重入；真实窗口、焦点和用户输入验收仍保留。

代码审查另补上异步取消/异常退休的浏览器实例检查：若旧实例销毁后同一宿主创建了新浏览器，旧延迟任务不得减少新实例的导航登记。该销毁重建边界为源码防护，尚未做专门重建注入。

后续补强配置检查：四窗格分别使用两种详细信息配置、96/256 大图标、不同名称/大小排序、有/无类型分组以及不同自定义列宽。初始阶段逐项核对请求值与原生捕获值；搜索期间比较缓存；返回目录及快速返回目录后再次捕获原生值，并要求 `ViewCaptureFailed=false`，拒绝缓存回退充当成功。八阶段全部通过。分组/排序重建期间项目数组可能暂时返回 E_INVALIDARG，探针只在原有 15 秒观察期限内重读完整数组，不能接受部分结果。

2026-10-21 14:01 已准备最新隔离 GUI 副本 `src/.state/gui-smoke/20261021-060111-704-5db17a/`，只运行 `run.ps1 -PrepareOnly`，状态为 prepared-not-run，没有启动窗口。DLL SHA256：`7992BE81A3D3CECDE102320DC52E69DC060811C82E4039B572417BCC537FA417`。这是源码验收副本，不是新的 release。

## 仍需验证

真实 WPF 窗口中的搜索栏、Esc 返回目录、两个查询间后退前进、布局/标签切换以及搜索状态下的保存保护。网络范围、离线设备、复杂 AQS 和持续输入仍需单独覆盖。原生组件通过不代替上述验收。
