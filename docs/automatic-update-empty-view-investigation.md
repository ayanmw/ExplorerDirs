# 自动变化期间短暂零行：刷新来源调查

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
