# Everything 文件夹大小数据源

## 接入边界

temp.86 目标为默认显示、可关闭的文件夹大小列，提供 Auto / Everything / 内置扫描三种来源，并复用到独立空间分析。当前本文件记录数据源阶段，不表示列表或设置已接入。

EverythingSizeClient 使用官方 SDK 文档公开的 IPC QUERY2W 协议，批量查询指定目录直接子项，不打开 Everything 搜索窗口、不更改外部配置。没有导入 Everything 程序，也没有新增 SDK DLL 运行依赖。

官方资料：https://www.voidtools.com/support/everything/sdk/ 及 https://www.voidtools.com/support/everything/sdk/ipc/ 。2026-11-02 下载官方 Everything-SDK.zip 用于接口核对，SHA256 F5716D9513CCE6B462B5170A0A2E7E081E191D9BCAC6774F5DECC061497DF443；SDK C 源和公开头部附 MIT 许可。下载材料仅在 src/.state/everything-sdk 调试目录，不随产品打包。IPC 字段偏移依据该包 ipc/everything_ipc.h。

## 已实现合同

- 查询前检查服务窗口、索引就绪和目录大小索引能力；无运行实例、索引不可用、超时、错误报文分别返回状态。
- 只创建 HWND_MESSAGE 接收窗口；所有工作在后台 STA，最多两条并行查询。发送使用 SendMessageTimeout，回包等待 1.5 秒，等待期间检查取消；排队取消不创建窗口。
- 每页最多 2048 项，返回总数与 offset 以支持后续分页；请求完整路径和逻辑大小，负值保留为未知，不冒充 0。
- 回包检查大小上限、计数、偏移、字段、UTF-16、终止符及路径；查询结果再校验每项确为请求目录的直接子项。

## 当前证据

- 本机 Everything 1.4.1.1032 正在运行。只读查询项目目录：Available，13 行，13 项大小可用，约 430 ms；前台不变，外部配置不变。
- --everything-size-protocol-smoke：中文路径、0 与未知值区分、报文截断/坏偏移/计数/字段、预取消通过，无窗口/IPC。
- --everything-size-live-fixture：新建两个临时目录，分别含 5 B / 37 B 文件，真实 Everything 查询读回两目录正确汇总，约 734 ms；前台不变，夹具清理成功。
- --navigation-chrome-smoke：修正旧检查对电脑菜单构造方式的假设后通过图标/可访问名称与焦点遍历合同。未进行可见 UI 验收。

## 待完成

2026-11-02 native 回调桥已接入 App 的 profile owner：后台 provider worker 先请求 Everything，Auto 缺值返回 native 扫描后备；Everything 专用缺值缓存为不可用。快照独立记录大小/计数是否可用，未知大小以空属性/破折号表示，未知计数不伪造零。来源切换取消旧回调并清空快照、失效在途 reservation；Shell 路径失效同步移除 managed 父目录批次缓存。managed 缓存最多四个第一页（8,192 行）、2 秒复用，超出第一页的目标使用精确路径查询。native 读写锁保证注销返回前已进入的回调退出，delegate 生命周期由 owner 保持。

--folder-size-native-bridge-smoke 使用真实 C++ adapter 与可控数据源，无 Shell view/IPC/注册表：父目录批次共享、子路径失效、未知与零区别、Auto native 后备、Builtin 不联系索引、native 取消、切换来源退休等待中的回调、注销后恢复原生扫描、快照可用状态和旧 reservation 拒绝发布均通过。既有 --synthetic-shell-folder-tree-smoke 通过，包括真实文件操作和缓存失效合同，明确 shell-view-created=False。C++ product/native-probe `/W4 /WX` 与 App/NativeProbe Release build 通过。真实 Everything 5 B/37 B 夹具增加精确路径查询读回验证通过，整轮约 1.8 秒，前台不变。

已实现 FolderSizeDataSource 的来源选择与分页消费：Everything 专用模式缺值保留未知，Auto 只扫描索引缺值目录，BuiltIn 不联系 Everything；内置扫描全局最多两个 worker，按批次调度而非每目录创建线程。不额外复制 Everything 索引和消费者已有的缓存。FolderSizeOptionsStore 默认 Enabled=True、Source=Auto，保存于独立 INI，支持冲突、未来格式及损坏文件保护。

--folder-size-data-source-smoke 验证来源路由、真实零与缺值区分、自动模式限制扫描范围、Everything 不可用后备、取消、选项重载/冲突/未来格式保护；通过，无 IPC/UI。App Release build 0 warning/error。

来源设置菜单和空间分析已接入，见 space-analysis-temp86.md；普通列表默认大小列/视图开关、关闭后停止请求、F5 主动失效、来源切换后的既有行更新仍未完成。尚未覆盖真实 Everything 未运行/关闭目录索引、查询中服务退出、挂起服务和多页目录；当前测试不能替代这些运行期边界。

2026-11-02 后续后台接线：ConfigureFolderSizeProvider 增加 enabled 状态，关闭时 ScheduleRecursiveSizeSnapshot 不入队，scanner 在条目边界检查开关及来源 generation；快速关闭再开启也会取消旧 generation 的扫描。App 启动读取 Enabled/Source，bridge 和 owner 保持开关，注销恢复默认 native 行为。InvalidateFolderSizePath 导出连接 F5，清理当前路径的子树与祖先快照，并同步失效 managed 批次。来源切换刷新已创建的 provider 视图；不创建隐藏标签宿主。

2026-11-02 普通目录接入前的标准列补全：此前 provider 只暴露三列（扩展模式四列），会丢失现有修改时间等列，因此增加对 backing IShellFolder2 的标准列映射、列状态、显示文本、属性和排序转发。排除已提供的 Name/Size/Type 重复键，按 provider 实例缓存最多 1,024 个 backing ordinal，COM 调用不持有 map mutex；STRRET_OFFSET 在临时 PIDL 释放前转为独立字符串。普通类型文本也读取原生属性，而不统一显示 File。

VerifyBackingColumnMetadataForProbe 在真实文件夹中确认修改/创建/访问时间列存在、标题和值可读；两个已设置不同修改时间的文件通过 provider 日期 CompareIDs 得到正确次序，GetDetailsEx 可读回日期属性。该检查已纳入 native bridge smoke，无 Shell view/注册。既有 folder-tree smoke 通过。标准列排序当前需重新解析 backing 子 PIDL，尚未测量大列表性能；未运行真实 IColumnManager 列恢复或可见 UI，不能宣称透明迁移完成。

2026-11-02 身份策略准备：FolderSizeLocationPolicy 区分用户物理路径与 Shell 实际 provider identity，只对 Shell 已确认的普通目录进行 provider 路由；ZIP stream、虚拟位置及关闭时的普通目录保持原路由，已有 provider history 不因关列而换身份。选择恢复仅允许物理直接子项，并将其绑定到当前 native parent。QdrShellPathCodec 补齐 encoded-root 后的真实子项段解码；此前把整串当十六进制，遇到 nested provider identity 会失败。新合同覆盖中文/目录名 .zip、真正 ZIP stream、嵌套路径、关闭/历史、直接子项及非法相对段，无 Shell/IO。策略尚未连接 ExplorerBrowserHost 的导航与选择流程，不代表普通目录已经接入。

新版 --folder-size-native-bridge-smoke 通过关闭/重开无额外查询、显式刷新失效、在延迟扫描开始后快速关开仍返回取消等合同。App/native C++ 构建通过。视图菜单 checkbox、普通目录的透明 provider 接入仍未完成；provider 身份会影响现有选区 PIDL、历史与监视逻辑，不能只替换 AddTab 的路径就宣布接入完成。后续需统一身份展示及操作路由，并验证 ZIP/虚拟位置、选择和历史不回归。

2026-11-02 默认大小列策略补全：WithSizeColumn 在启用时补充缺失的标准大小列，未捕获列布局时提供名称/修改时间/类型/大小；保留已有列顺序、宽度、模式、排序和分组，不修改输入快照。关闭文件夹汇总时不删除普通文件大小列；重复调用不重复添加，达到列数上限时保留用户布局。folder-size-location-policy-smoke 已覆盖默认和自定义布局、重复启用及关闭后的排序保留并通过；该策略尚待宿主调用，不能记为视图入口完成。

2026-11-02 宿主接线第一步：ExplorerBrowserHost 增加显式 profile/enabled 配置，应用发起导航在 Shell 确认普通文件夹属性后转换 provider PIDL；导航登记和回调使用同一物理身份，发布位置另保留 native identity。provider 位置按真实路径恢复目录监视、快速筛选和地址通知；物理选区映射为当前 provider 的直接子 PIDL，搜索完成清除 provider 身份。provider 恢复使用默认大小列策略，未知大小排序仍走既有延迟排序路径。来源刷新同时识别宿主内部 provider 身份。App Release build 零警告错误，location-policy、view-state-restore 和 selection-folder-resolver 定向后台检查通过。普通窗格尚未调用配置入口；Shell 自发导航、历史、真实选择/操作及大目录性能仍待验证，不能发布或视为默认大小列已完成。

2026-11-02 普通窗格入口接线：MainWindow 从 FolderSize.ini 向每个 Pane 配置 Enabled，新增视图菜单的文件夹大小开关，先保存成功再更新 provider 和窗格。AddTab 配置新宿主；已有 provider 关闭时只刷新，不替换宿主；启用既有普通目录时导航到当前逻辑路径。标签遍历使用快照并检查归属，避免 Shell 重入关闭或转移。App 构建和数据源、native bridge、location policy 检查通过，无产品窗口。真实选区保持、历史重复条目、Shell 自发导航、默认列恢复和文件操作尚待验收，不能覆盖发布包。

2026-11-02 导航审查修复：provider 编码根不代表真实磁盘根，原 GoParent 的 Shell BrowseParent 会走内部 namespace 父级。现在仅 provider 使用物理路径上一层，磁盘/UNC 共享根回到此电脑；普通 Shell 和搜索导航保持原行为。Program Starter/克隆选区共用的项目路径采集也将本 profile provider identity 解码为物理路径，避免内部解析名泄漏给外部程序或丢失物理路径筛选项。location-policy 测试覆盖物理上级、嵌套、磁盘根、UNC、其他 profile 和普通路径，并通过；App Release build 零警告错误。无真实 Shell view，原生选择、快捷键和历史可见验收仍待执行。

2026-11-02 开关转换选区保护：对普通视图启用 provider 前采集真实选区，以 browser/read generation/selection version 检查 COM 重入后的快照；失败不强行转换。隐藏标签延迟至激活后转换，避免现有选择恢复定时器因不可见直接取消；关闭开关清除待转换状态。来源刷新遍历也改为快照并检查归属，避免刷新重入导致集合变化。App Release build 与既有 view-state-restore 后台检查通过；本次快照/隐藏标签恢复尚缺真实宿主验收。

官方 BrowseToIDList 文档仅列出部分支持的 SBSP 标志，不包含 WRITENOHISTORY；未据此添加未经验证的无历史标志。原生 Travel Log 的同路径身份转换仍可能产生额外条目，待解决，不能用应用层历史去重替代。资料：https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowser-browsetoidlist

2026-11-02 原生关闭行为修复：新增 no-view 测试先复现关闭后目录 PKEY_Size 仍返回数字（HRESULT E_FAIL），而停止查询测试此前未覆盖该属性层。GetDetailsEx 现在对关闭后的目录返回 VT_EMPTY，GetDetailsOf 大小列返回空文本，不调用扫描；普通文件仍保持可读取的 1 B 大小。新增测试修复后通过。native probe 与 product C++ /W4 /WX 构建通过，native bridge 与目录树原生测试通过（嵌套解析/绑定/存储、菜单/数据对象、多选真实复制移动删除、冲突重命名和缓存失效），均未创建 Shell view。可见视图排序/选择/刷新仍需集中验证。

2026-11-02 标准大小属性补全：新增测试复现首个异步快照前 GetDetailsEx(PKEY_Size) 返回 PIDL 数字占位值，现 async 缓存未命中与已知不可用均返回 VT_EMPTY，真实缓存零仍返回 VT_UI8=0。另一新增测试复现仅读取标准 PKEY_Size 不触发按需查询（此前只有自定义 key 触发），现两个属性入口均走既有按需调度，产品仍为异步；无视图同步夹具确认读回 5 B 并产生快照。两项测试均先失败再修复通过；native bridge、folder-tree、product/probe /W4 /WX build 通过。未运行可见宿主，标准属性读回正确不替代 Shell 实际补值通知与排序验收。

2026-11-02 Shell 自发导航接线：优先读取本 profile 的 DesktopAbsoluteParsing，不能让 FILESYSPATH 将 provider 原生身份抹掉；就绪状态按原生身份计数，显示/会话仍用物理路径。OnNavigationPending 对普通物理目录取消未接受的访问，再在退出回调后排队 Navigate；已是 provider、ZIP stream 和虚拟位置不转换。排队工作检查 browser、请求版本、导航/配置代次，快速切换或开关不能复活旧转换。未接受的原生完成/失败回调先检查 outstanding，不退休替代请求。路径策略定向测试现同时执行完整 ShellViewReadiness 状态测试，新增同物理路径不同 native identity 的失败隔离与重复回调检查，通过；App/SettingsSmoke 构建和 view-state-restore 检查通过。此为源代码接线和状态机证据，未运行真实窗口；取消后的 Travel Log、Shell 实际解析名及回调顺序、快速连点、磁盘入口仍待集中验收，未解决/未宣称原生历史额外条目问题。

取消依据：https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowserevents-onnavigationpending ，官方明确返回失败 HRESULT 会取消导航。

2026-11-02 真实 provider PIDL 的无视图验证：新增 HostProbe --folder-navigation-provider-callbacks <product-dll>，随机临时 profile/CLSID，经生产 owner 创建可清理的 volatile 注册；真实 SHParseDisplayName 解析根及两层中文目录，调用生产宿主 Pending/Complete，确认物理 CurrentPath、native parent 与请求退休，并断言没有 HWND/ExplorerBrowser/file view、前台不变、注册删除和夹具清理。测试连续复现并修复三处原生缺陷：edpath 根编码误吞后续子路径；子项 FORPARSING 返回叶名而非绝对解析名；Initialize/属性解析忽略编码根后的子 PIDL，导致深层目录绑定祖先。现在分别分段解析并保留合成 PIDL、生成绝对解析名、绑定完整物理路径。两层中文测试修复后通过。既有 unhosted 回调增加未接受/重复 terminal 回调不退休新请求检查，五类目录及搜索交接通过；native bridge、folder-tree、product/probe 严格构建通过。此次使用真实产品 DLL/临时注册，但没有创建视图；不能替代 Travel Log、输入焦点、Shell 显示及通知排序验收。

2026-11-02 provider 选区 PIDL 验证扩展：真实产品 DLL 的 root/中文子目录/第二层各放同名 selected.txt，使用生产 FolderSizeLocationPolicy 映射物理路径，再调用 ShellSelectionPidl.TryGetDirectChild，全部得到有效直接子 PIDL；实际 SHGetNameFromIDList 的文件绝对解析名均可解回对应物理路径。跨父目录同名文件与非直接后代均拒绝。夹具和临时注册清理、前台不变断言通过。源码同时补充 provider 绑定后的选区请求有效性检查，避免新增可重入 Shell 调用将旧 clone 列表写回新导航；大小开关转换在采集选区后重查开关和待转换状态。App Release build 零警告错误，provider/unhosted 导航回调测试通过。以上证明绑定身份和回调逻辑，不证明 IShellView 的实际选择/焦点恢复。

2026-11-02 分页/取消/不可用边界：4,097 项三页的受控查询验证 offset=0/2048/4096、最后一页、真实零与未知值；Auto 仅扫描一个缺值目录，Everything 专用不扫描。新增测试先复现查询返回时已取消仍发布整页进度，FolderSizeDataSource 增加返回后及逐项取消检查后通过。真实 native bridge 加入受控第一页溢出→精确查询、批次共享及精确结果未知检查，并逐一验证 NotRunning/IndexUnavailable/Timeout/InvalidReply/Failed 在专用模式保留未知、Auto 才执行内置后备，全部通过。上述错误状态为注入，不代表实际关闭/挂起外部 Everything；未改变其配置或进程。本机只读 IPC 夹具再次通过，两个目录 5 B/37 B 与精确路径读取正确，耗时 1,751 ms，前台不变、夹具已清理。协议/空间分析回归、App Release build 均通过，无可见文件视图。

2026-11-02 重要更正：标准 System.Size/PKEY_Size 是 {B725F130-47EF-101A-A5F1-02608C9EEBAC}:12（SDK propkey.h 与微软文档一致），此前新增 FolderSizeLocationPolicy 和旧实验/宿主探针误用了 {9E5E05AC-1936-4A75-94F7-4704B8B01923}:1。原生 adapter 的 kSizeKey 和 MapColumnToSCID 原本正确；现产品默认列、开发者入口、profile/session/排序宿主探针和跨进程/qdr夹具统一使用标准键。此前以旧键配置的可见排序实验不能证明标准大小列排序，须重新验收；不能将用户历史超时仅归因于缓存。真实 product provider 的 root/两层中文同名文件使用不同修改时间，读取父 IShellFolder2.GetDetailsEx 与原生文件系统非空属性逐项一致，并断言大小等于 FileInfo.Length；文件夹解析名往返 ILIsEqual 通过，证明绑定层级未变化。路径策略、跨进程列配置、隔离 qdr 导入导出测试通过，HostProbe Release 构建通过。qdr-only 分支移到广域检查前，避免该命令先跑无关环境/界面检查。旧实验会话中错误键的兼容处理仍待评估；真实视图排序仍未验收。官方属性定义：https://learn.microsoft.com/en-us/windows/win32/properties/props-system-size

2026-11-02 旧实验大小键兼容：MigrateLegacySizeKey 仅用于本 profile provider 被 Shell 确认后的视图缓存，将旧实验键转换为标准 System.Size；保留模式、图标大小、列宽、排序方向和分组方向。已有标准大小列时优先保留标准列及其宽度，避免新旧并存形成重复列；排序保留首个位置和方向，重复转换幂等。普通 Shell 视图与其他自定义属性不转换，输入快照不被修改。逻辑测试覆盖旧列/混合列/关闭汇总/普通视图隔离与幂等；真实产品 provider 的 root/两层中文 PIDL 回调以旧键种子视图启动，确认完成导航时 cached GroupKey 已转换，再进入正常恢复流程。测试与 App/HostProbe 构建通过，无可见视图；实际原生列/排序/分组恢复仍待集中验收，不直接改写用户现有配置文件。

2026-11-02 来源切换提交边界审查：原生队列为两 worker、最多 2,048 排队任务（数量上限不等于进程 MB 上限）。发现查询返回后到 store.Set 之间仍可切换 provider，旧结果可能在清缓存后回填。现逐行任务与批次条目捕获 provider generation；查询前/回调后/内置递归及缓存提交均检查同一代次，SetForProviderGeneration 与 Configure+Clear 使用同一 SRW 锁序列化，旧代次提交返回取消而不覆盖当前快照。native store 测试确认当前代次可写、错误代次被拒绝并保留原值；native bridge 全部回归通过。逐行 8 项异步扫描约 98 ms，实验 view-batch 500 项约 756 ms，均 peak workers=2、首读不等扫描、ACL 部分结果可用、取消及缓存复读通过，shell-view-created=False。product/probe 严格构建通过。未切换产品默认调度到 batch，未声称进程内存下降或真实视图更快。F5 通知失效对默认逐行任务的在途提交保护仍需继续核查。

2026-11-02 默认逐行失效保护：新增 gated per-row 测试先复现扫描没有共享 store reservation。逐行 worker 现于扫描前获取同路径 reservation，跨视图同路径只扫描一次，退出/取消/失败均释放；缓存命中与被通知作废的 S_FALSE 不计完成、不发更新，也不误报失败。新增阶段暂停扫描、移除缓存并作废在途 reservation、放行，确认旧结果未回填且完成计数不增加，通过。入队捕获不再移走调用方 parsingName，失败时可删除正确 pending key；该拒绝路径为源码修复，尚未强制 quota 拒绝测试。失败的扫描夹具也等待已排队任务退出再释放 gate，避免测试本身提前关闭句柄。逐行 8 项、实验 batch 8 项、native bridge 与 product/probe 严格构建通过，无 Shell view。F5 可见延迟/闪烁/选区与通知后的自动重试仍需集中验收，不能据存储合同宣称界面性能改善。

2026-11-02 原生历史接口只读调查：SDK tlogstg.h 定义 ITravelLogStg、SID_STravelLogCursor=IID_ITravelLogStg（7EBFDD80-AD18-11D3-A4C5-00C04F72D6B8）及枚举/删除 API。新增显式命令 HostProbe --explorer-browser-history-capability，仅 CoCreateInstance 未初始化 ExplorerBrowser，绝不 Initialize/Browse/TravelTo/CreateEntry/RemoveEntry。实际 IServiceProvider QI=S_OK，QueryService(ITravelLogStg)=E_NOTIMPL；查询前后 GetCurrentView=E_FAIL 且指针为空，未调用 GetCount、前台未变。HostProbe 构建零警告错误。结论仅是未初始化对象不提供该服务，不能推断初始化后的实际窗口也不支持，也未证明内部身份转换是否产生重复历史。没有把此可选 COM 能力调查加入后台固定通过门槛，没有接入猜测性的历史删改或替换既有 Travel Log。后续仍需真实 Shell 历史证据。参考：https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser ，接口要求 Initialize 后才准备导航，并要求释放其生成的窗口资源。

2026-11-02 共享 IPC 生命周期修复：新增 native bridge 测试先复现切换来源仅取消原生等待者、实际父目录查询 token 为 None，底层 IPC 仍占查询名额。ParentPage 现使用来源 generation token，并在缓存锁内检查当前请求取消；单行取消不取消共享查询，来源切换/关闭才取消整个旧代次。测试同时验证单行取消返回 ERROR_CANCELLED、另一行复用同一批次且不重复查询、切换后底层 batch token 已取消，修复后通过。App Release build 零警告错误。此为受控 native/managed 回调证据，不代表真实 Everything 超时性能或可见 F5 延迟已验收；未启动窗口、未覆盖 temp.86。
2026-11-02 新建后焦点/重命名 PIDL 修复：真实 product provider 的无视图测试先复现 GetFocusedItemPath 将 provider 子 PIDL 与物理父 PIDL 拼接，无法返回实际文件路径。焦点解析现使用当前 native parent 再解码物理路径；新建后的 SelectItem 也改用该 native parent 的直接子 PIDL，避免拿物理 PIDL 末项选择 provider 行。root/两层中文目录各验证同名文件和新建文件夹的真实直接子 PIDL 及焦点路径，修复后通过；provider 注册和夹具清理、前台不变、无 HWND/browser/view 断言通过。普通导航回调测试通过。实际新建命令、编辑框出现与键盘输入仍需集中 UI 验证，不能据 PIDL 合同宣称完整重命名体验已验收；未覆盖发布。
2026-11-02 文件操作物理路径边界：新增测试先复现生产 FileSystemPath 对真实 provider 文件不能返回物理路径，这会妨碍粘贴后选区映射；现该助手按本 profile 解析名解码，再为普通 Shell 项保持原 FILESYSPATH 回退。单项 SelectedItem 和选中目录新标签入口也将本 provider 解析名解为物理路径，保留其他 namespace 的原语义。root/两层中文目录的真实文件路径测试及普通文件系统对照通过，目录策略检查通过；App 随 HostProbe Release 构建成功，未创建 Shell view、前台不变、临时注册/夹具清理。实际粘贴选择、预览及新标签交互仍需集中可见验证。未更新发布包。