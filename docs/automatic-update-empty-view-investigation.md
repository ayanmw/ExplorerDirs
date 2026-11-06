# 自动变化期间短暂零行：刷新来源调查

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
