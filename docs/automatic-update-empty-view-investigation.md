## 2026-11-06 Public-symbol interpretation of enumeration worker

REA opened the system DLL but no native analysis provider was available; session was closed without configuration changes. Downloaded Microsoft's public PDBs by each image's RSDS key into src/.state/native-symbols. DbgHelp SymFromAddr returned error 126 even with the installed VS x64 helper; this attempt is not a successful symbol-handler validation. A read-only MSF/DBI/S_PUB32 parser then mapped section-relative public symbol addresses to the DLL's PE section RVAs.

Observed nearest public names (not decompiled bodies): windows.storage.dll+0x27B35C -> _GetEnumerator+0xF0; +0x22DA37 -> CEnumThread::InternalResumeRT+0xC7; +0x22D90D -> CEnumThread::s_EnumThreadProc+0x4D. SHCORE+0x43A8C is nearest SHCreateThreadRef+0x1FC, which is too far to establish an exact function boundary. Evidence: src/.state/enumeration-public-symbols.log, read-enumeration-public-symbols.py and check-symbol-export-rvas.py.

Identity checks: DLL RSDS GUID matches PDB stream-1 GUID for both; RSDS ages are 1 while public PDB stream ages are 2 and 3 respectively, so do not claim strict GUID+age identity validation. The symbol-server lookup keys were FC2C41F4093E147F64921116B8DD38731/Windows.Storage.pdb and D27BFB8722C42B7497C9EAE6B78BEDB21/shcore.pdb. Independent PE-export comparison agrees for 558 storage and 96 SHCORE named addresses; storage AssocKeyFromElement has a different export/public-symbol RVA and remains an explicit exception, not silently accepted.

DLL SHA256: storage 8D6AC2B451AF71E913665C8459935AC862E219458F0962C99F3A3D48AB9C251B; SHCORE C49F6CA8DCC99077703B654578C5F2000ACD126354484678AF86282A5BAF5163. PDB hashes: E13C96C71D93512EBA7157DB3869B1CD841A7F7785645618887A9430EA8D621C / 8B1C4E5FED549CF230F0E995FE6054350BA2C3726EA3D562631C1DBE7299EBCD.

These names support the existing observed non-UI enumeration-thread classification, but do not identify its scheduling origin. The worker stack is cut off at the thread entry: it cannot distinguish a newly scheduled notification reaction from delayed startup work. Preserve all delivery/readiness guards; next compare settled baseline/no-event enumeration timing against the same-root burst before changing product behavior. No product change or publication in this step.

## 2026-11-06 Native enumeration stacks and bounded flush control

Experiment only: trace now includes PID/TID and up to 12 enumeration stacks (20 module/RVA frames each). App session output records its UI thread. The opt-in EXPLORERDIRS_PROJECTED_CHUNKED_FLUSH_CONTROL=1 uses flush boundaries every four queued deliveries and always flushes the tail; default remains one. Pure queue stage 7 first failed with unconditional flush, then passed after boundary implementation; all existing queue lifetime/capacity/retirement checks remain.

Evidence: src/.state/projected-enumeration-stack-same-root32.log and projected-enumeration-stack-timeline.log recorded a creation confirmation timeout with latest Changed, no successor deletion, and transport pending zero; actual creation visibility took about 10.6 seconds. This does not justify widening superseded-creation confirmation or relaxing its timeout.

src/.state/projected-chunk4-same-root32.log and projected-chunk4-timeline.log: two same-root processes (32 created files, no 1,025-row filler) completed creation in 7,657/4,757ms and deletion in 1,629/1,930ms, but the full run failed on extra enumeration 5->6. Extra context-1 enumeration was on TID 42552, not UI TID 45568, with windows.storage.dll RVAs 0x27B35C/0x22DA37/0x22D90D and SHCORE.dll+0x43A8C, matching cold-enumeration stack shape. No corresponding managed refresh request was recorded. Stack symbols remain unresolved; this does not identify the initiating Shell task or prove batching fixes latency.

Queue smoke re-run passed. Ordinary HostProbe rebuild passed with zero warnings/errors; App and HostProbe output exclude the experiment entrypoints. Ordinary native SHA256 remains B61059F45CC281131820D22FB831BCF01D1530B70BC6539ACBC2A8C9313399FF. The experiment stays disabled and unpublished. Next: resolve native frames and verify complete App navigation focus separately from selection helper tests.

# 自动变化期间短暂零行：刷新来源调查

## 2026-11-06 缓冲热回调日志后再次捕获间歇失败

原生通知实验的TraceProjectedDeliveryFields改为进程内有界缓冲：SFVM_FSNOTIFY只格式化/入缓冲，不逐条CreateFile/WriteFile。状态空闲或失败、退休、枚举诊断点批量flush；总上限仍4096条并显式输出trace-truncated，不丢弃业务事件。flush在缓冲互斥锁外拼接/写文件，独立写锁保持批次不交错；日志仍会分配/加锁且flush有IO，不是零观察开销。未flush时进程被强制终止会丢尾部诊断，不将缺少日志当成事件不存在。

projected-buffered-trace-same-root32.log（owned556828d5897c41a6b7ce574871b8a69b）再次失败：pending64的旧Created burst-0确认超时，没有新增根枚举。因此旧创建补偿仍有未覆盖条件，不能宣称修复全部交错。补充超时delivery-pending及路径latest-kind/sequence诊断；只读DescribePathForDiagnostic不影响排队/确认。

projected-buffered-detailed-same-root32.log（prepared143548-355d3f，owned1baaf39d96734f3d9538b6bd52a2cfa5）整体退出1，这次未超时，而是第二进程枚举5→6。实际tick520989000枚举context3的当前shared-root，已不局限context4。对应App刷新请求日志只有第一进程的520960406请求，没有第二进程该时间段RefreshWithPresentation记录；view-event未记录UPDATEDIR。只能说明本次额外枚举没有对应的已观测托管刷新入口/目录通知，尚不能区分原生排序/分组/视图内部调用或其他来源。

该轮两PID已落盘3018/2982条，无truncated标记；前台/输入/清理守卫通过不改变功能失败。代码仍使用显式路径、仅实验编译，无普通产品日志或持久化配置。后续应采集原生枚举调用栈，而非将context4当固定根因。

NativeProbe、普通HostProbe/App最终构建零警告零错误；ledger/dispatch/后台队列无窗口合同通过，普通输出排除实验/结构追踪入口。普通DLL仍B61059F45CC281131820D22FB831BCF01D1530B70BC6539ACBC2A8C9313399FF。此批是诊断记录路径与错误详情，未修复剩余根枚举/吞吐，未发布。

## 2026-11-06 刷新请求与视图通知分开观测

仅REFRESH_ORIGIN_TRACE构建增加进程级EXPLORERDIRS_REFRESH_REQUESTS_ONLY=1：保留RefreshWithPresentation调用栈及native enter/return，但TraceNativeListStructure直接返回；默认原追踪行为不变，普通产品不含此字段。最初将字段插入方法内导致编译失败，已移至类成员区并重建成功；失败prepare不是已运行产物。

projected-refresh-requests-only.log（prepared140731-494753，owned0062bbc396e0415d83b7f621d28f8f24）两个进程完整同根32文件/同名大小、枚举不增及所有隔离守卫通过。仍可见冷启动分组恢复请求返回S_FALSE，未复现额外根枚举，不能据此消除之前失败记录。

原生通知实验补充SFVM_FSNOTIFY实际view-event，不解析/bind PIDL、不调用视图getter；记录已有合成child名称或当前根路径与context。为覆盖四视图重复事件将显式文件追踪上限增至每进程4096条。projected-view-event-same-root32.log及projected-view-event-timeline.log（owned0b898c024e4041babb4a977419d3b68e）：两个进程完整功能/确认/枚举与全部隔离守卫通过，整体退出0，无零行；新增8713/8210ms、删除3467/3304ms。实际没有view-structure-origin.log，确认请求追踪与结构追踪已分离。两PID分别2888/2920条，未达上限；该通过轮无view-event UPDATEDIR。没有额外枚举的轮次不能反证失败轮由什么触发，文件追踪本身仍有同步IO并可能影响时序。

此批无产品修复，不将多轮通过改写为间歇问题已解决。后台交付/metadata世代合同继续通过；普通App/HostProbe恢复构建零警告零错误并排除实验/请求追踪入口，普通DLL仍B61059F45CC281131820D22FB831BCF01D1530B70BC6539ACBC2A8C9313399FF。未发布。下一步用更少热路径IO的事件记录定位失败时的根枚举调用来源，同时保留旧创建确认和同根门槛，不调整超时。

## 2026-11-06 旧创建确认被后续删除取代，剩余根枚举仍失败

先跑RefreshOriginTrace的projected-size-guard-refresh-origin.log（owned6cd208a477574d51a4f102b05de9ccee），两进程完整同根/32文件/同名大小及隔离通过，枚举5→5、无零行。日志仍有冷启动分组恢复的manual=False/full=True请求，但发生在首批watcher dispatch前且native返回S_FALSE，不能据此归因之前的后续枚举。

新增LastUnconfirmed诊断和原生枚举目录/context/flags记录，且直接记录size-publish字段时不再分配临时wstring载荷。projected-enumeration-same-root32.log（owned2f98ca0febd446afb34ddcae64a43d94）同样两进程通过，新增8008/7557ms、删除2810/2187ms。关闭视图结构追踪后projected-enumeration-no-view-trace.log（owned73a5b85c1c324a44be8e6d57bbca487f）失败，具体为旧Created external-file.txt.burst-0、pending64。列表已经完成新增和删除，但此monitor仍等旧创建行重新出现。不能通过延长超时解决；带追踪通过也不证明无竞态。

实验确认规则修正：Ledger查询仅返回同owner、尚未完成的旧work后、仍为最新的Deleted；Changed或其他owner不构成证据。Created正常行确认失败时，仅后台交付已无待办、物理属性明确FILE/PATH_NOT_FOUND、实际原生行不存在、访问与后续删除在读取前后仍有效，才确认旧创建；不丢弃后续删除，后续仍正常派发/原生ticket确认。未知访问错误继续等待。此规则未处理全部rename/recreate交错，不扩大为所有过期work直接完成。

projected-superseded-created-red.log在尚未实现successor查询时按预期失败，完成后ledger及dispatch无窗口合同通过（包括诊断项清理）。新增实际App宿主受控测试：此前创建已由真实Shell交付，手动保留相应创建记录，再实际删除/发送原生删除并等待真实行消失；实际确认方法拒绝退休访问、拒绝磁盘重建但仍无行的状态，确认不存在，保留并完成后续删除。projected-superseded-created-host-final.log整项及隔离通过，同时原A-B-A/F5/容量/同名类型和关闭检查通过。该子项为受控ingress与直接确认方法调用，不等于自然FSW时序已全面验收。

完整App projected-superseded-created-same-root32.log（owned7bcba5be20d04528bc244ae4a7d0607d）未记录旧创建超时，但仍整体退出1：第二进程枚举5→6，tick518266296实际枚举context4的当前shared-root目录，非子目录枚举。无superseded-creation日志，说明此自然时序没有命中新分支，不能拿该轮证明自动分支已覆盖；已由上述受控实际宿主补验证。剩余枚举触发源仍待定位，不启用实验为产品默认。

最终普通HostProbe/App构建零警告零错误，排除实验/结构追踪入口，普通DLL哈希仍B61059F45CC281131820D22FB831BCF01D1530B70BC6539ACBC2A8C9313399FF。本批为实验协调与诊断，未重新宣称全套39项完成（上一普通修正批次已通过）；未发布。下一步定位context4的根枚举来源并测试更多快速交错。

## 2026-11-06 确认并拦截删除后的过期大小通知

先验证根身份假设：临时仅实验区分context ID、忽略其可变低2位；无窗口旧规则/新规则/同context排序位/对称性/路径合同红绿通过，但完整App projected-context-identity-same-root32.log仍在第二进程出现一个旧目录残留。该无效身份改动、开关及临时探针均已移除，普通根CompareIDs规则未改，避免保留没有修复价值的语义变化。

实验增加显式EXPLORERDIRS_PROJECTED_DELIVERY_TRACE文件路径，最多每进程1024条，记录PID/seq/context/event/name及accepted/enter/returned；载荷携带独立诊断字段。accepted在Enqueue返回后写，worker可能先写enter，不能用打印行顺序代替接受顺序；FLUSH returned也不证明视图已应用。projected-timeline-same-root32.log及projected-delivery-timeline-same-root.log再次复现，第二进程所有MKDIR/UPDATEITEM的worker调用返回早于RMDIR开始，反驳该轮由worker迟发MKDIR导致残留的解释。

补充原有异步大小发布路径后，projected-size-publish-same-root32.log及projected-size-publish-timeline.log复现并记录：PID77192四份RMDIR在tick516063562前返回，随后516063593原大小扫描路径再次发送该目录UPDATEITEM；四列表均出现external-notification-fixture残留。这是通知桥外另一条真实过期发布路径。

普通native修正：发送排队的逐项大小UPDATEITEM前读取其物理路径属性，仅ERROR_FILE_NOT_FOUND/PATH_NOT_FOUND明确确认不存在时释放该待发PIDL并跳过，未知/权限错误仍保留原路径。没有取消物理删除事件或全面静默掉更新，也没有完整刷新兜底。检查与交付之间仍有竞态，不声明原子保证；同名替换的身份变化不在此简单存在性检查覆盖内。

projected-size-guard-same-root32.log及projected-size-guard-timeline.log：两个进程的创建目录→删除→创建文件→删除文件阶段均通过，记录到两份size-skipped-missing，未再在该阶段保留旧目录。但整体仍退出1：第二进程后续确认超时、枚举5→7和3次零行采样，不能宣称后台通知方案或完整同根场景已通过。首进程完整32文件和同名大小场景通过，只作为子项证据。

39项后台检查全部通过，报告src/.state/background-checks/20261106-212332-05d15efe35d849d6b847a788fb8a1014/summary.json；普通native四个真实ExplorerBrowser视图stale-size-normal-browser-final.log整项/隔离守卫通过（12/25KB/0、40KB更新、选区焦点、F5、列重排、数字排序和销毁）。普通DLL因真实修正更新为B61059F45CC281131820D22FB831BCF01D1530B70BC6539ACBC2A8C9313399FF。普通App/HostProbe恢复并排除实验入口；预览未发布。下一步继续定位第二进程的确认超时与枚举触发源。

## 2026-11-06 后台交付接入隔离通知桥，发现同根旧目录残留

私有ABI v5改为57561654-2DB4-42F9-BC73-7A67F5FD8B38，新增slot10 Begin、11 Retire、12 Status。普通构建排除。仅进程EXPLORERDIRS_PROJECTED_BACKGROUND_DELIVERY_CONTROL=1启用；默认仍同步原路径。UI准备绝对PIDL并复制ILGetSize字节，worker仅CoInitializeEx(MTA)/SHChangeNotify(IDLIST|FLUSH)/CoUninitialize，不捕获this/COM接口/HWND。开始新监视器创建独立队列，停止/视图NCDESTROY/有队列的根重初始化/析构退休；后台错误与在途数回传timer，排序就绪和settled日志均包含后台未完成门槛。已接受通知仍允许完成，未证明可以撤回。

`src/.state/projected-background-batch64-size32.log`（prepared125240-8b06af，owned run c96a0020234b46d98f1988b69b46f2b4）：默认Size、不同根四窗格1025填充/32文件、64批次/后台FLUSH，两进程功能/逐名/分组/不增加枚举与全部隔离守卫通过，整体退出0，自动阶段无零行；新增5363/5199ms，删除2559/1851ms。Dispatcher两段记录均0，末尾部分owner callback-max33–36ms，native累计约27–31ms。它避免该轮UI同步flush的大阻塞，但总完成时间未显著优化，不能宣称吞吐达标。

`projected-background-lifecycle.log` 实际App宿主的A-B-A、受控在途排序协调、stale Changed、容量停止、真实F5恢复/同名文件转目录、旧刷新回调、关闭roots/facts=0及隔离守卫通过，整体退出0。受控在途是原有托管待办，未新增实际worker阻塞门槛；roots/facts归零不等于后台worker已全部结束。

同物理目录独立视图失败：`projected-background-same-root32.log` owned run6abfccb547ec4260a204368b6acb99d0，第一进程包含32文件/同名37→83KB场景通过，第二进程file-create四列表从2到4（预期3）后超时，整体退出1。此same-root路由没有1025填充行，不能外推为大目录同根压力通过。补充小夹具失败清单后，`projected-background-same-root32-inventory.log` owned rundb5bb56f304345c18c7ff80bae204ad3，在delete阶段失败，四列表均残留small|large|external-notification-fixture；实际已删除目录未从视图移除。两轮隔离守卫通过不改变功能失败。不能启用此方案。

源码线索：根path-marker CompareIDs仅比较物理路径，忽略各标签编码上下文；独立后台队列可能让跨视图同根通知交错。该交错尚无逐项投递时间线，不能将此认定唯一原因。下一步验证通知域/根身份隔离或同根单生产者协调，保留旧目录残留的红色门槛，不扩大超时或完整枚举掩盖。

最终增加根重初始化退休保护后，队列合同与v5原生1025行基础通知桥复测通过（projected-background-v5-baseline-bridge.log，隔离通过），此基础探针不调用新Begin，因此不等于后台交付通过。普通native重建哈希仍CADC37A135D8F0551767976EA0258F0A3075499BDFF6518C4101E90B903904EF；普通App/HostProbe恢复构建零警告零错误并排除实验入口。未发布。

## 2026-11-06 后台交付队列独立原型

新增仅PROJECTED_NOTIFY_BRIDGE_EXPERIMENT编译的ProjectedDeliveryQueueExperiment.h及无窗口导出RunProjectedDeliveryQueueSmoke。尚未接入NotifyProjectedArrival/Removal/Rename，也没有向Shell发送通知。队列复制事件号/两份字节载荷，单worker按序消费，不捕获Shell接口/HWND/宿主回调。容量包含排队与已接受在途项；溢出明确退休并清空未接受项，保留失败码，不静默淘汰后续继续。交付回调异常同样退休。

退休不等待worker：立即拒绝新入队/清空未接受项，已经取出的单项允许完成。不能撤回已经被worker接受或Shell接收的通知；实际接入仍需视图/导航代际、在途通知与重开同路径的验证。WaitIdleForProbe仅给无窗口测试使用，产品UI禁止调用。worker持有共享队列及独立DLL引用，FreeLibraryWhenCallbackReturns把模块引用释放推迟到回调返回，CloseThreadpoolWork由系统等待回调结束后实际释放工作对象。API语义依据 https://learn.microsoft.com/en-us/windows/win32/api/threadpoolapiset/nf-threadpoolapiset-freelibrarywhencallbackreturns 和 https://learn.microsoft.com/en-us/windows/win32/api/threadpoolapiset/nf-threadpoolapiset-closethreadpoolwork 。尚未做真实DLL卸载竞态测试，不将API接线当成该验收完成。

RED：先新增探针命令，旧原型缺少导出，projected-delivery-queue-red.log失败。GREEN：projected-delivery-queue-final.log退出0，覆盖调用方修改原始两份载荷后副本不变、顺序、阻塞首项时退休立即清空后续、重复退休/拒绝入队、排队+在途容量、异常退休和调用方释放队列后worker仍完成并最终释放。所有载荷为测试字节，未验证真实PIDL格式或跨线程SHChangeNotify。

有效负向控制：临时删除容量计算中的在途项，构建后projected-delivery-queue-negative-capacity.log在stage4失败；finally恢复源文件，重建及最终全合同通过。原生metadata generation合同继续通过，NativeProbe构建零警告零错误。普通DLL重建哈希仍CADC37A135D8F0551767976EA0258F0A3075499BDFF6518C4101E90B903904EF。没有产品行为、UI或发布变化。

下一步：评审实际绝对PIDL拷贝/格式边界、worker COM初始化、单访问退休和交付失败反馈，再接隔离通知实验；真实Shell更新、吞吐/交互延迟、F5/导航/关闭/同路径重开仍未验证。

## 2026-11-06 批次确认往返与同步flush对照

原始逐owner日志显示每8事件后等待整批行确认，部分派发递增相隔约0.5秒或更多。实验进程级EXPLORERDIRS_PROJECTED_BATCH64_CONTROL=1将上限改为64，默认仍8；原生实验EXPLORERDIRS_PROJECTED_FLUSH_CONTROL=1改为SHCNF_FLUSH，默认仍FLUSHNOWAIT，普通DLL均排除此控制。不改FIFO/逐项确认/队列容量/10秒确认期限，不自动完整枚举兜底。

64+NOWAIT失败：`src/.state/projected-batch64-size32.log`，owned run `src/.state/app-session-process/7e152332bd01461db3fd36470e3b788c`，第一进程32文件新增后四列表仍1027，独立Shell观察器收到各目录0x1000 UPDATEDIR，四monitor随后确认超时，整体退出1。隔离守卫通过不能把功能失败记成通过。官方SHChangeNotifyRegister文档明确多通知可能合为UPDATEDIR；本轮观察符合该机制，但独立观察器不能独自证明每个DefView收到的事件与合并原因。https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister

64+FLUSH：`src/.state/projected-batch64-flush-size32.log`，owned run `src/.state/app-session-process/d5442dedfb9b4806949496846ac24cb6`，两个进程功能/逐名/分组/枚举及全部隔离检查通过，整体退出0，自动阶段无零行。新增4269/3331ms，删除1242/1355ms。但累计native派发墙钟变大，最后owner可达1118–1263ms，单次callback628–796ms，Dispatcher也记录255–332ms等长定时操作。FLUSH等待通知交付的定义见 https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify 。交付不等于列表最终完成，仍保留现有行确认。这不是可接受的UI方案，不将控制改为默认。

结论：直接扩大NOWAIT批次不能安全解决吞吐；同步FLUSH虽通过该场景但把等待转进UI回调。下一步评审不可变PIDL通知载荷与后台交付的分离，必须处理COM线程归属、导航退休、顺序、生命周期及在途确认，不能从线程池直接调用现有STA Shell接口。尚未实现后台交付。

无窗口dispatch及native metadata generation合同通过；普通原生DLL重建哈希仍CADC37A135D8F0551767976EA0258F0A3075499BDFF6518C4101E90B903904EF，普通HostProbe/App重新构建通过并排除实验入口。未发布，两个实验参数默认仍关闭，运行作用域finally恢复环境变量。

## 2026-11-06 定位并替换列标题UIA遍历热点

实验新增共享Dispatcher操作观察器，仅在通知实验编译，监视器引用计数归零时解绑；记录超过50ms操作的方法名/优先级/嵌套深度，最多256条，不记录委托参数。使用运行时私有_method字段仅识别诊断标签，不参与产品功能；字段缺失显示unavailable。操作耗时为含嵌套/原生消息循环的墙钟时间，不等于排他CPU或完整原生消息剖析。

RED实测 `src/.state/projected-dispatcher-operations-name32.log`，owned run `src/.state/app-session-process/b5593042ed8a464b84c08413f5966b71`：两进程功能及隔离守卫通过，发现22条QueueColumnHeaderGeometryRefresh慢操作，55–999.8ms，累计约9999.5ms。其CaptureColumnHeaderArrowTargets从文件列表根做UIA Descendants遍历，与大量行相关的慢操作构成了具体可修改路径，而不是此前猜测通知调用慢。批量新增5605/6407ms、删除1530/3418ms。

普通产品修正：SysListView32改读其原生Header的项目矩形、显示顺序与下拉矩形，结合IColumnManager可见属性键；无有效split-button则不注册命中区域，不改变现有20px命中规则。其他非ListView宿主保留UIA路径。列几何捕获后重新检查browser/generation/pending，避免COM重入导航后将旧几何标成新视图。此缓存重入保护是同一路径审查发现的连带修正，不宣称已经实际复现它。

GREEN名称对照 `src/.state/projected-native-header-name32.log`，owned run `src/.state/app-session-process/0595c72fc4bb4f3db48be8407661027a`：两进程功能、逐名、分组、不增加枚举及全部隔离守卫通过，自动阶段无零行；两段attached→detached均records=0，无超过50ms的Dispatcher操作。新增3061/2957ms、删除2489/1639ms。最终补上split-button要求后，默认Size主排序复测 `src/.state/projected-native-header-size32-final.log`，owned run `src/.state/app-session-process/7e1af691286c4823ac3382c3c23f1824`，同样全部通过且records=0，新增4083/5820ms、删除2108/3656ms。秒级剩余延迟没有解决，不把Name与Size跨场景差异解释为单一因果。

`native-header-geometry-final.log` 实际普通产品宿主Name/Size属性键与有序屏幕矩形、两列各6次排序、选区/焦点、混合排序、已正确排序跳过mutation、销毁及全部隔离守卫通过，整体退出0。仅私有Desktop无激活地显示测试宿主，不操作共享桌面；真实鼠标箭头点击/列重排/DPI仍需集中验收。39项后台检查全部通过，报告 `src/.state/background-checks/20261106-203114-ac678ce1e6894e519bd281e35f6dba63/summary.json`；该整轮早于最后增加的split-button矩形检查，最终普通构建和原生宿主检查已重新通过。普通App/HostProbe输出恢复，排除实验监视器/Dispatcher诊断类型；未发布。

## 2026-11-06 降低跨进程行采样负载

AppSessionProcessProbe新增仅探针进程的EXPLORERDIRS_PROBE_SPARSE_ROW_SAMPLING=1控制，将AwaitRows轮询间隔25ms改为250ms，其他等待、15秒期限、最终条目/名称/分组/枚举/隔离断言保留。默认仍25ms；日志记录实际周期。稀疏采样较易漏掉短暂零行，不能用它替代瞬态覆盖，也不能把最终通过视为视觉无闪。

使用同一prepared App `20261106-121746-056-24ab90`、Name主排序、Background优先级、1025填充行和32文件场景，`src/.state/projected-sparse-sampling-name32.log` 两进程功能与全部隔离守卫通过，整体退出0，owned run `src/.state/app-session-process/0816b2c314ef4989b493e935ece764c8`。新增3164/3184ms、删除1708/2307ms；第二进程部分owner最大回调间隔786ms仍存在。相比前一25ms组3879/4065ms新增、4042/1940ms删除，部分较快，但顺序非随机、样本少、存在其他负载波动，不足以归因探针主导，更不是产品性能修复。下一步仍需UI线程操作或堆栈证据。

NativeProbe重新构建零警告零错误；仅改探针，无普通产品行为或发布改变。

## 2026-11-06 同一二进制调度优先级对照

实验构建读取进程级 EXPLORERDIRS_PROJECTED_NORMAL_PRIORITY_CONTROL=1 时使用Normal，否则仍Background；启动日志记录实际priority。仅用于对照，不是普通产品选项。两个变量均由探针调用finally还原，普通构建不包含此入口。采用同一prepared payload `20261106-121746-056-24ab90`、名称排序控制、相同1025填充/32文件场景，先Normal再Background，顺序非随机，样本有限。

`src/.state/projected-priority-normal-name32.log` 新增3391/6410ms、删除1589/1575ms；实际启动日志确认Normal，第二进程部分owner最大待办回调间隔912–916ms。`src/.state/projected-priority-background-name32.log` 新增3879/4065ms、删除4042/1940ms。两组各两个进程功能/逐名/分组/不增加枚举及全部隔离守卫通过，整体退出0，自动阶段无零行采样。未测真实键鼠响应，不证明提高优先级的交互代价可接受。

优先级提高未稳定消除秒级等待，不能据此把普通产品改为Normal，也不能完全排除Dispatcher调度影响。下一步改为采集长间隔期间UI线程操作/消息或堆栈，并对照外部逐行/计数采样负载，避免持续增加不能归因的累计计数。普通HostProbe/App构建通过，恢复后核查无实验入口；预览未发布。

## 2026-11-06 完整回调与日志耗时对照

新增实验 callback-ms/callback-max-ms/logging-ms，完整回调finally计时包含同步重入和日志；日志字段反映此前已完成回调，当前写日志尚未结束，不能视为当前回调的闭合计时。首轮补充计时漏接finally，仅日志计时有效，保留 `projected-callback-name32.log`，不将其中为零的callback字段作为证据；修正后重新准备并运行完整场景。

有效结果 `src/.state/projected-callback-complete-name32.log`，owned run `src/.state/app-session-process/9852c6a0d13943c3a12640ef36ed9e6e`：两进程四窗格1025填充行/32文件增删、逐名、分组、确认及枚举检查均通过；前台/输入/清理守卫通过，整体退出0，自动阶段无零行。批量新增5841/5805ms、删除2884/2035ms。八owner最后settled累计回调115.1–145.3ms，单次最大7.9–13.7ms，日志累计5.9–9.2ms，而待办时回调最大间隔646.6–1084.2ms。完整回调与日志的已测耗时不能解释长间隔；仍需测量Dispatcher操作排队/执行与其他消息工作，不能直接归因Shell或调高优先级。所有值为墙钟时间，重入可重叠，不是CPU独占占比。

普通HostProbe/App再次构建零警告零错误，普通输出排除实验入口；本批未发布。

## 2026-11-06 待办期间定时器间隔实测

实验计时增加 active-gap-max-ms、sampled-pending-ms、active-ticks 和 not-ready，只记录回调开始间隔及待确认采样，不改变调度优先级、20ms周期或超时。

`src/.state/projected-wait-name32.log`：名称排序对照、1025填充文件、32文件批量增删，两个完整App进程全部功能与前台/输入/清理守卫通过，整体退出0。实际新增2991/5678ms、删除1615/1456ms，名称逐项检查通过，自动阶段无零行采样，四owner各自枚举6→6。原始日志位于 `src/.state/app-session-process/83c6899b04b3448daa36a39e263a11b0/app/projected-monitor-experiment.log`。

八owner累计派发7.2–8.9ms、行读取101.3–133.5ms、ack0.1–0.4ms；有待办时最大回调间隔272.1–1144.5ms，待确认采样累计4541.4–7622.0ms，未就绪检查26–50次。间隔明显超过20ms，但包含调度、其他UI/Shell消息和系统调度等因素，尚不能归因单一阻塞函数。累计待确认是采样墙钟区间，不是精确逐事件延迟或CPU耗时，多owner不可相加解释独占占比。结果支持下一步测量Dispatcher排队/其他UI工作，并保留Shell通知应用等待这一条路径；不支持直接提高优先级、改flush标志或放宽超时。

普通HostProbe/App重新构建零警告零错误，并核查两处App程序集均无实验监视器/列表结构追踪入口；未修改或发布temp.86。

## 2026-11-06 名称排序对照与调用计时

复用现有进程级EXPLORERDIRS_PROBE_NAME_SORT_CONTROL对照，只在探针调用范围设为1并finally恢复，使用当前通知实验副本，保留大小列和一份大小分组。`src/.state/projected-name-control32.log` 两进程1025填充文件、32文件增删/名称/分组/确认/枚举通过，自动阶段均无零行；输入守卫失败，整体退出1。批量新增约8623/10606ms，说明高延迟不能只归因启动Size排序恢复。该对照不是关闭文件夹大小功能，不能排除大小提供器其他开销。

实验派发、行确认和ack回调分别累计Stopwatch时间，计入现有dispatch/settled日志，不进入普通构建。`projected-timing-name32.log` 两进程完整功能及隔离守卫均通过，整体退出0，自动阶段无零行；新增2945/6669ms、删除2635/1612ms，存在明显单轮波动。八份owner最终约102/103事件各自累计：派发6.5–10.2ms、行确认96.7–118.4ms、ack0.1–0.5ms。

这是回调内墙钟时间，不是CPU独占时间，可能包含同步重入；不能将多owner相加解释为互斥CPU占比。尚未计时的是Shell应用通知的等待、Dispatcher排队、其他消息处理及探针采样开销。因此下一步应测定时器间隔和未确认等待，而不是先优化派发函数内部或提高超时预算。不能将这轮名称排序零样本外推为Size排序/视觉问题已解决。

本批只增加实验诊断，普通App/HostProbe已恢复并核查无实验/追踪入口，未发布。

## 2026-11-06 进一步定位与修正

新增PrepareOnly的RefreshOriginTrace构建开关，保留刷新请求/原生进入返回及列表删除/清空/计数/排序消息；清空消息附受限托管栈，普通构建排除。`src/.state/projected-zero-trace.log` 的32文件场景在批量删除失败；原生枚举仍8→8，事件曾确认，但实际行仍残留。refresh-origin.log在tick509369000记录ScheduleInitialVirtualSort.RecoverAsync发起manual=False/full=True/soft=True的请求，原生Refresh返回S_FALSE。该轮未复现零行，不据此宣称此栈是此前所有零行的唯一原因。

源码核实两处扰动来源：未分组恢复不先验证当前行序便重设排序；实验Watcher没有更新既有_directoryChangeLastEventAt，异步值/分组读取后也未全面拒绝内容变化。因此普通恢复路径增加一致性检查；单键Size且已正确排序时不操作，保留未知/错误排序和多键/其他列的既有恢复；实验回调更新同一时间戳。

实际HostProbe已排序测试最初使用了文件夹大小尚未就绪的错误夹具（空大小、folder-large在前），该失败不算正确的已排序红灯。修正夹具为原生5|17|29、混排升序后，移除no-op分支的负对照 `ordered-recovery-negative-ready.log` 确定失败，恢复分支后 `ordered-recovery-normal-final.log` 通过行序/选区/列标题点击/全局混排/关闭验证。`ordered-recovery-grouped-final.log` 的已有分组no-op回归通过，两轮功能断言通过、共享输入守卫失败。

`projected-zero-fixed-trace.log` 两独立完整App进程的1025填充文件/32项增删及名称、枚举/确认/分组/会话通过，自动阶段未采样零行，未记录那条延后启动刷新请求；共享前台/输入守卫失败，整体退出1。批量新增仍慢，第二进程约11690ms，不宣称性能目标已达成，记录诊断开销边界。可见闪烁及所有零行原因仍未关闭。

39项后台检查整轮通过：`src/.state/background-checks/20261106-193706-489ccecf514c475385d287a12f8c124b/summary.json`。最后只收窄no-op到单键Size并重新构建，未重跑整轮。普通输出不含诊断/通知实验入口，原生普通DLL哈希未变，未发布。

2026-11-06，基于00da1e3f。本批定位并补诊断，不声明解决空白或删除问题。

## 当前实测

用既有 EXPLORERDIRS_REFRESH_ORIGIN_TRACE 编译隔离App，并以普通产品宏加 NotificationTrace 编译原生适配器；没有修改日用profile或releases。App副本与准备载荷DLL SHA-256相同（4B5C50FA3EFE83BC2454B7465CA831F67AD28C49316FA0847B22EA12C87D9318）。

- 准备载荷：src/.state/gui-smoke/20261106-060312-447-053377。
- 完整进程日志：src/.state/refresh-origin-native-trace.log。
- 原始日志目录：src/.state/app-session-process/1f6590aa542449f19d9492ba777dfbee/app。
- 关联分析：src/.state/refresh-origin-analysis.json。

| 观察范围 | 原生EnumObjects调用 | 应用刷新请求 |
| --- | ---: | ---: |
| 第一进程 before-create 至 settled-file-burst-delete（490164187–490175437） | 0 | 0 |
| 第二进程 before-create 至 settled-file-burst-create（490184796–490198843） | 0 | 1 |

第一进程自动变化阶段已出现短暂零行，但没有应用刷新请求或文件系统重新枚举。不能继续把所有空列表都归因于完整枚举。零行采样本身也不等同可见白闪：仍需区分列表结构变动、重绘暂停和真正呈现。

第二进程490191718记录一条manual=False/full=True/soft=True请求，调用栈来自ScheduleInitialVirtualSort；此时探针已进入文件创建阶段。说明保存大小分组的首次恢复可能与可操作后的自动变化重叠。原日志写在ShellRefreshPolicy.Invoke之前，因此它只能证明请求，不能证明策略准备成功或真正进入IShellView.Refresh。这也是本批补充调用入口/返回记录的原因。

第一进程功能流程完成，第二进程三文件批量删除残留并超时；期间共享前台从锁屏变为WXWork且输入时间改变，隔离守卫未通过。只保留定位证据，不作为性能对比、干净隔离成功或普通构建稳定性结论。诊断同步日志IO也会扰动时序。

## 新增诊断与验证

仅EXPLORERDIRS_REFRESH_ORIGIN_TRACE构建记录SysListView32收到的WM_SETREDRAW、LVM_DELETEALLITEMS、LVM_SETITEMCOUNT、LVM_SORTITEMS/EX；不在消息回调内反查Shell状态。正数逐项计数省略，最多512条，写入失败停用。记录的是收到的命令，不能据此推断已转发或绘制；sort-hold字段用于关联应用呈现保护。

刷新来源日志另记录native-enter/native-return，区分请求、策略准备和实际调用。正常构建编译移除两类日志主体，未增加产品配置入口。

普通App/HostProbe、诊断App和SettingsSmoke构建成功；导航焦点请求与软刷新路由定向检查通过。src/.state/list-structure-build-check.json确认普通DLL不含两类日志字符串、显式诊断DLL包含。新增结构消息日志尚未执行窗口验收，不将编译或字符串检查算作运行通过。普通构建已恢复。

下一步：在集中隔离窗口测试中关联结构消息、刷新入口/返回、原生通知及零行时间；另检查是否可对“实际行组已物化”的启动恢复省去完整刷新。现有分组首次物化曾有失败反例，未验证前不得直接删除该分支。真实拖放、导航默认选择及temp.86其它门槛继续保留。


## 2026-11-06 结构消息与名称排序对照补证

当前产品基线89490e66；增加的仅为AppSessionProcessProbe采样，不更改产品刷新、排序或成功判定。

- `src/.state/list-structure-full-app.log`：四窗格1025 filler，批量删除失败。结构日志仅观察到WM_SETREDRAW，未观察到所监听的清空/计数/排序消息；记录到一次启动分组修复真正进入Refresh，返回S_FALSE（不是S_OK）。不能据此称为成功完整枚举。
- `src/.state/list-structure-render-state.log`：采样增加跨进程读取起止tick、IsWindowVisible和SysSetRedraw前后值。仍有短暂零行及批量删除失败；零行样本可见标志保持true，未取得SysSetRedraw非零证据。属性为零也可能是属性不存在，不能认定绘制已启用，更不能把零行直接等同可见黑屏。
- `src/.state/list-structure-name-sort-control.log`：仅探针配置名称主排序，保留四窗格/分组/大目录；外部删除阶段仍超时。本轮foreground-unchanged=True、input-change-observed=False、isolation-clean=True。这是受控失败反例：不能把删除问题限定为Size主排序，也不能用名称排序绕过功能目标。

前两轮受共享输入活动影响，不用于干净性能对比；名称对照的失败无需再用桌面活动解释。三轮均未修改日用profile、未覆盖release。新增计数为诊断性累计样本数，不是闪烁次数或持续时间；跨进程轮询存在采样扰动。

下一步聚焦原生通知投影/列表增删的一致性，保持自动软更新不降级为完整刷新；首次分组恢复优化已单独落地，但没有关闭此问题。可见呈现与真实拖放仍待集中验收。


## 独立文件系统观察对照

`src/.state/filesystem-observer-name-control.log`再次以名称主排序运行，独立FileSystemWatcher对四个测试根各观察一次，不发送通知、不改变截止时间。四根均在494118062记录external-notification-fixture的delete，列表仍各1028行（期望1027）。原生通知日志末尾494117968，未收到该删除对应事件；日志总量未达到4096条上限。原始目录：`src/.state/app-session-process/8e58897a749c40258f8ba5a734328480/app`。

此轮外部前台从msedge变为Feishu且输入改变，隔离守卫未通过，不作稳定性/时延结论。它提供的是“独立OS观察已见删除，而适配器接收日志未见”的分层证据，不证明Windows全局通知未产生，也不证明注册已失效。后续需检查Shell订阅生命周期、通知合并及接收链；不能只修改删除PIDL比较或继续增加补发次数。观察器仅存在于测试进程，限定external-*测试名称、最多512条事件输出，退出释放四个观察器，无产品/配置变化。


## 原始投递及订阅生命周期对照

新增仅NotificationTrace构建的注册/注销、锁定前消息投递及锁定失败记录；此前过滤器遗漏既有注册记录，已补齐，不改变产品事件处理。日志`src/.state/notification-subscription-lifecycle.log`，原始目录`src/.state/app-session-process/7415a28c1b7c4832b6c809e4b5846fdd/app`，适配器SHA256为6D016D89776C5652254A8B5B6957B74311DCFC2A196724FF3D74DE939DEA0FD0。

四次注册成功（1/3/5/7），删除前仍收到原始消息并成功解锁；日志未见提前注销或解锁失败。独立OS观察四根494247968删除，之后没有新的physical-delivery记录，四窗格仍1028行、删除超时。日志仅50行，未达上限。此证据排查的是本次失败，不外推成所有删除故障同因；也不能据未记录注销证明系统内注册必然一直有效。共享输入/前台观察守卫未通过。

Microsoft SHChangeNotifyRegister文档说明通知可合并为UPDATEDIR：https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister 。尚未证明此轮被合并，下一步独立Shell订阅需区分通知服务与本宿主接收链。

正常、NotificationTrace、Diagnostics三种原生构建成功；普通DLL不含新增诊断字符串。无窗口notification-capture合同通过（使用Diagnostics DLL）。最初误将此诊断用例指向普通DLL，因预期不导出诊断入口而EntryPointNotFound，已改为正确Diagnostics构建重跑，不记最初命令成功。可见闪烁及完整App删除门槛仍未关闭，release未改。


## 独立Shell订阅对照（观察器自验后）

增加测试进程自有Shell观察器，在相同私有Desktop/STA监视四个物理根，不转发事件；轮询时泵观察器消息，不改子进程列表或判定。复用观察器时发现旧工具遗漏RMDIR且Unlock的DllImport未指定带下划线原生入口，现已修正；旧无事件结果不能作为有效独立Shell对照。

新增`--shell-observer-contract-private-desktop`仅在专属测试目录显式发mkdir/rmdir夹具通知，验证真实接收及解锁；该合同通过，foreground/input均不变，isolation-clean=True。此夹具通知不用于完整App恢复。

随后`src/.state/independent-shell-name-control-verified-observer.log`中，四根OS删除494576437，独立Shell订阅494577468收到四次RMDIR；子进程adapter日志没有对应physical-delivery/physical-view-event，四列表仍1028行。原始目录`src/.state/app-session-process/b78623c51ced4a09aa27cbe107f4a898/app`。这使“系统没有产生对应Shell通知”不再能解释该轮结果，应继续比较订阅的HWND/根身份/生命周期。子进程注册均成功，未记录提前注销/锁失败。整轮共享输入活动守卫不通过，不记端到端稳定性成功；未修改产品语义或release。


## 2026-11-06 接收窗口、事件掩码及物理路径补发反例

三项相互独立的对照均未达到目标，未进入产品：

- 独立HWND_MESSAGE接收物理事件并转发给既有视图回调：`src/.state/dedicated-notification-window-control.log`，adapter SHA256 0115F106E00E43DF381686725EA641212BC5B9AF8252C46978FE022828B0CBF2。独立订阅收到RMDIR，应用无对应接收，目录删除后四窗格仍1028行。否决用更换接收窗口直接修复的候选。
- 物理订阅缩小到与独立观察器相同的文件事件掩码：`src/.state/narrow-notification-mask-control.log`，SHA256 011FFFCD328D1D02BBB8CD04B0B7658C16FD704987D6E480FADC43F2C4AB3552。同样删除残留，不将掩码修改用于产品。
- 删除后显式发物理PATHW删除通知（探针控制，不是产品恢复）：前两轮`physical-delete-notification-control.log`及`control2.log`在操作前分组夹具检查失败，未实际运行补发，不能算该方案反例。第三轮`src/.state/physical-delete-notification-control3.log`确实执行了四次RMDIR补发，独立Shell收到但产品仍残留。物理PATHW补发也不足以解决。

三轮有效候选均发生共享输入变化，非干净性能/稳定性验收。候选源码已撤回，保留在src/.state下同名patch及physical-delete-notification-control.patch作实验记录；正常源代码及构建入口恢复，不留产品开关。只保留探针就绪修正：观察器初始化解析PIDL可能泵启动回调，在任何文件操作前重新等待四窗格、预期行数及恰好一个分组窗格，不降低原断言或用固定延时替代。下步验证实际注册根PIDL与显示目录是否一致，并检查同一适配器实例初始化/绑定后的订阅关系。


## 注册根目录身份与重新初始化

`src/.state/subscription-root-identity-control.log`针对既有名称排序大目录夹具对照；诊断读取实际_backingRoot的FILESYSPATH，只输出叶名、与_backingPath是否完全一致、PIDL长度。四根pane-0-tab-1、pane-2-tab-0、pane-1-tab-0、pane-3-tab-0均S_OK且匹配标志1；对应注册均成功。没有已挂载/有注册的对象重新Initialize记录。独立Shell仍收到删除，子进程未记录删除投递，目录残留超时。此轮输入守卫失败，不作性能结论。

初次诊断同时记录了大量未挂载解析/绑定临时对象初始化（1245条），会扰动时序；总日志1293条未达到4096上限。现只在_notificationWindow或注册ID非零时记录重初始化，保留实际订阅根检查；不能由该调整声称产品性能提升。根身份对照排除了该轮“订阅路径与显示路径不一致”的解释，没有证明通知注册在系统内部始终有效。后续应关注同进程物理订阅与原生DefView投影订阅的相互影响，以及消息在App线程中的投递/分派；继续保留失败门槛，不新增自动完整刷新。


## UI线程消息分派对照

首先试用WH_GETMESSAGE，仅观察BackingFolderChange注册消息：`src/.state/notification-queue-dispatch-control.log`。钩子成功但连六条实际到达回调的通知也未观察到，所以取队列钩子不覆盖该传递路径，不能以零记录证明未到App。

替换为仅诊断编译的WH_CALLWNDPROC线程钩子（不修改MSG，不消费事件，始终CallNextHookEx；首个订阅安装、最后注销卸载）。`src/.state/notification-sent-dispatch-control.log`原始目录`src/.state/app-session-process/93f482ce07414f27a5cd563c4e51c03c/app`，adapter SHA256 C7F3F7A99920C0D6A5CC4CA49CE8828237D0CFF3CB0E08CB61E558FC347621CA。四条分派记录与四条physical-delivery、physical-view-event匹配；独立观察器随后收到RMDIR时，两处均无对应删除，目录残留超时。故该轮没有“已到分派却被应用子类吞掉”的证据。不能据此证明所有消息投递场景均正确。

共享输入变化，隔离守卫未通过，仍非性能/稳定性成功。钩子没有进入普通产品构建。安装成功时不解读未定义的GetLastError旧值；仅失败时记录错误码。下步侧重同进程DefView投影订阅与物理订阅的关系，而非无依据改写WPF键盘/主题分派。


## 延后注册与软刷新拆分对照

一次性PostMessage延后重建物理订阅的候选中，四窗格均记录queued及applied（原注册1/4/7/10替换3/6/9/12），仍缺少删除投递且目录残留。日志`src/.state/deferred-subscription-order-control.log`。候选已撤回，源码保存`src/.state/deferred-notification-registration.patch`；不增加产品延迟注册或重试。

新增大目录/原生DLL对照命令可组合`--no-soft-refresh`。`src/.state/native-subscription-no-soft-control.log`两次独立进程业务流程通过，确实记录RMDIR；但同时启用了原有完整刷新恢复，第二进程累计184次含零行的轮询采样（不是闪烁次数），不能当作自然逐项通知或体验成功，也不是采用关闭软刷新的理由。输入守卫不通过。

为拆分原生标志与managed恢复，将SetNoEnumRefresh(IntPtr,bool)只在隔离构建强制传false；其余_softRefresh=True不变。准备载荷`src/.state/gui-smoke/20261106-074256-620-d0f2bc`；`src/.state/noenum-flag-only-control.log`目录删除仍失败，原生RMDIR记录0。已反射读取两个载荷方法IL：基线02200000080003284F0000062A（ldarg.1），对照02200000080016284F0000062A（ldc.i4.0），确认构建确实应用控制。首次反射用错类型命名空间、随后未区分重载，均未取得证据；更正为全局类型且IntPtr重载后成功。不能将故障直接归因于FWF_NOENUMREFRESH。

隔离宏和产品源码已撤回，补丁保存在`src/.state/noenum-flag-control.patch`，releases/profile不变。普通App重新构建恢复。后续应比较managed完整恢复具体触发了什么通知，而非关闭软刷新或删除原生标志当成修复。


## 跨进程实际路径核对

此前仅比较订阅PIDL与适配器自身_backingPath，不能证明它等于测试实际修改的目录。补充诊断对实际FILESYSPATH与观察器输入路径按相同大写字符单元计算64位FNV指纹（测试路径均ASCII），只输出叶名与指纹，不输出完整路径。`src/.state/cross-process-root-path-control.log`四根指纹全部匹配：pane-0-tab-1=11491140687868505830，pane-1-tab-0=5821811756782557410，pane-2-tab-0=18049280870254145511，pane-3-tab-0=13487777824083529520。这是本轮跨进程路径一致的补证，未发现会话副本仍浏览旧根；指纹比较不替代通用Windows路径身份合同。

该轮仍目录删除失败，独立Shell收到RMDIR，产品无对应记录；共享输入变化，隔离守卫不通过。日志未达上限。需继续检查恢复链，不能宣称已修复。

另核查旧IShellFolderView.RemoveObject方案：微软说明IShellFolderView从Windows 7起不再供使用，应使用IFolderView/IFolderView2（https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-ishellfolderview），不采用旧接口作为本项目支持基线。没有因这一探索加入直接操纵原生列表或全视图刷新。


## 恢复父目录元数据通知的隔离反例

完整恢复成功日志中，首次RMDIR在495988328出现，早于App首次Refresh入口495988390，不能把所有成功通知简单说成由完整Refresh调用产生。源码差异还包括Changed批处理在非soft时按排序/分组需要发送父目录UPDATEDIR。

隔离控制仅移除该通知分支的!_softRefresh条件，保留soft=True、原生NoEnumRefresh及禁止自动完整恢复。准备载荷`src/.state/gui-smoke/20261106-075508-645-28bb93`，日志`src/.state/changed-directory-notify-control.log`。分组窗格新增目录后仍1027行，其他三窗格1028，15秒create超时；尚未进入delete，不能称为本候选删除反例或删除成功。App刷新请求日志为空，native确实收到至少一次UPDATEDIR。共享输入变化，隔离守卫失败。候选源码已撤回，补丁`src/.state/changed-directory-notify-control.patch`，正常App重新构建；不恢复这一产品分支。

后续评审：物理FS事件已经可靠到达managed观察器，而投影命名空间不能仅依赖物理Shell订阅保证逐项到达。需要研究由当前投影目录直接发布对应项目的Shell命名空间变化；保持SHChangeNotify等受支持接口，准确保存项目类型/身份并验证同名重建、导航退休、批量增删及分组/选区。不能用旧IShellFolderView.RemoveObject、直接ListView删除、扩大同步扫描上限或自动整页刷新替代目标。评审/原型不是产品修复完成。
