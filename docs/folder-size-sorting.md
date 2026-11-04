# 文件夹与文件大小排序：接线与验证

## 已确认目标

工具/选项/文件夹集中显示开关、来源和未来的混合排序开关。混合排序默认关闭；关闭时文件夹在前、文件在后分别按大小排序，开启后统一排列。标签独立记忆、复制继承、便携 INI 恢复。当前仅前两类现有设置完成菜单集中；混合开关、会话字段和产品排序接线尚未完成。

## 2026-11-04 原生对照

Microsoft SHCOLSTATE_NOSORTBYFOLDERNESS 明确表示不单独排列文件夹：https://learn.microsoft.com/windows/win32/api/shtypes/ne-shtypes-shcolstate 。它可供大小列策略使用，但文档语义不能替代实际 Shell 行序验证。

第一原型给 SyntheticSortFolder 增加每实例私有 COM 能力，大小列禁用 Shell 独立分类，比较器自行处理分类，并尝试通过 IFolderView2.GetSortColumns 对降序反转作补偿。原型中的默认混合值仅保留旧比较行为，不是用户要求的产品缺省值；产品将来仍必须以 false 应用。

实际 ExplorerBrowser 私有 Desktop 夹具中，设置 IFolderView2.GetFolder 返回对象为独立排序后，直接比较目录和文件确实进入分类分支；但是 Shell 实际排序在别的 SyntheticSortFolder 实例中执行，仍然按大小混排。诊断日志用实例地址确认这一点，例如同一运行中设置/直接比较实例为 2618364072112，实际排序实例为 2618366996080（地址只用于该次进程内对照）。不能把实例字段当成每视图状态，也不能用进程全局开关掩盖同路径标签串用。

证据：src/.state/product-size-mixed-sort-instance-trace.log；隔离后可重复的失败入口为 product-size-mixed-sort-isolated-experiment.log。完整行序断言保留，第一项“关闭混合、升序”仍失败；后续混合/降序未因此记通过。几轮均前台/输入守卫干净，没有控制用户桌面。

## 实验隔离

全部策略接口、分类分支、列标志和实验字段受 EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT 保护。只有显式开关构建该原型：

```powershell
pwsh -NoProfile -File src/ExplorerDirs.NativeProbe/build-product-shell-folder-adapter-diagnostics.ps1 -SizeSortPolicyExperiment
dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --product-size-sort-policy-private-desktop src/ExplorerDirs.NativeProbe/bin/native-product-diagnostics-x64-sort-policy/ExplorerDirs.ShellNamespace.Diagnostics.dll
```

它预期目前会报告真实行序不符，而非验收通过。实验 DLL 输出独立目录，不覆盖产品或普通诊断库。普通比较诊断记录新增有界的实例标识，帮助确认重新绑定。

普通产品 DLL 未启用实验，SHA-256 仍为 D2E02736BDD01843BE9D71CA4C2CFDBC4516CD6443D671837FF4B9E325F953A7，与此前 native-repeat-hash.log 相同；现有排序二进制行为未被这个失败原型替换。

## 本批其他验证和限制

- MainWindow.xaml 编译通过，现有显示大小及三来源在同一文件夹组且入口不重复，点击/INI处理沿用。无菜单可见验收。
- 27 项后台检查整轮通过：src/.state/background-checks/20261104-215214-e66990263b2246e9ab6c825badca9c04/summary.json，记录提交前源码状态。普通构建不运行实验排序分支。
- 原有独立原生显示探针复跑在外部新增后选中状态由 0x3 变为 0x1，丢失选中位、保留焦点位，整项失败；日志 product-size-display-after-policy-isolation.log。该轮守卫干净，必须保留失败。此夹具不是完整产品 WPF 宿主，尚不能认定产品已复现同一故障。它也不能因产品 DLL 哈希相同而被忽略，后续需与完整宿主的选择恢复路径对照。
- 未覆盖 temp.86，未更改用户配置或系统桌面。

## 后续接线

需要确定可在 Shell 重绑定/后台排序实例中恢复、且仍区分同路径不同标签的视图策略上下文，例如由瞬时视图身份/PIDL携带上下文，而非按物理路径建全局设置表。先验证模式和方向传播，再扩展 ViewState/INI/复制/QDR；不得提前发布一个仅保存勾选状态的产品开关。实际原生排序及原有选择恢复的失败保持未完成。

## 后续隔离对照：身份传播与失败路径

实验 DPR2 PIDL 可将独立/混合策略传给 Shell 重绑定后实际执行比较的对象；product-size-sort-context-rebind-trace.log 的 encoded-policy=0 证明传播，尚不证明最终行序正确。新增无窗口 PIDL 检查覆盖两模式、方向位、1024 字符分段边界、多段根、非法位及跨段不一致；sort-policy-safe-pidl.log 通过。所有新格式和导出只在显式排序实验宏中启用，未接入产品标签或配置。

曾尝试在 GetCurFolder 中查询 IFolderView2.GetSortColumns，再序列化当前方向。encoded-root 初始化出现栈溢出（0xC00000FD，product-size-sort-context-direction.log）；未采集原生栈，不能断言完整递归链。已撤去回调中的视图反查和 HWND 属性路径，比较器只读不可变实验方向。后续必须由明确排序请求传入方向，而非在序列化或比较回调中反查视图。

公开 IColumnManager 状态对照：SetColumnInfo 返回 S_OK，但请求状态 0x5 后 GetColumnInfo 仍读到 0x1。保留失败断言，日志 sort-policy-column-state-readback-final.log；该次初始大小、单项补值、F5 和列重排先通过，失败发生在混合策略写回检查。前台/输入守卫干净，四个宿主销毁后存活列表 HWND=0。故不采用仅调用 SetColumnInfo 即宣称已支持的方案。

清理后后台总检 27 项通过：src/.state/background-checks/20261104-225059-b5799d51aa194301ad94d91e2c892ec2/summary.json（源码为 7796e50 加本批未提交修改）。普通产品重新构建 SHA256 为 36730EAA8DB30C3DB89ECFE015845C530D8E336FC32390B3D9C5ADC7D73EC174，不能再沿用前批哈希不变的结论；普通根解析辅助函数也有源码调整，排序策略本身仍受实验宏隔离。未进行可见 UI 验收，未覆盖预览。

## 显式方向输入后的真实行序验证

隔离导出现在接收完整 policy 位（混合位+降序位），不再将方向位作为 BOOL 折叠为混合。探针在创建主视图前传入固定方向，每轮仅断言对应方向；不据此宣称同一视图动态切换已完成。

四种模式的主视图完整行序均匹配：

| 模式 | 实际字节顺序及类别 | 日志 |
| --- | --- | --- |
| 独立升序 | 目录0、目录25600、目录40960、文件2048 | sort-direction-asc.log |
| 独立降序 | 目录40960、目录25600、目录0、文件2048 | sort-direction-desc.log |
| 混合升序 | 目录0、文件2048、目录25600、目录40960 | sort-direction-mixed-asc.log |
| 混合降序 | 目录40960、目录25600、文件2048、目录0 | sort-direction-mixed-desc.log |

日志均在 src/.state；四轮前台与输入守卫干净。独立升/降及混合降整项通过（包含更新、F5、列重排、来源切换、自动通知和清理）；混合升序虽然行序通过，但后续自动变化丢失选中位，整项失败，不能算全绿。失败与先前独立探针现象一致，仍需完整产品宿主对照，不改低断言。

实验 DLL 严格编译、NativeProbe Release 编译及 PIDL 无窗口 smoke 通过。还缺：同一视图动态方向/策略变化、同路径多标签身份隔离、实际产品接线及便携配置；不覆盖 temp.86。

## 自动变化选区对照

增加探针内的 Shell GetSelectionState 与逐行选择状态对照，按名称重新定位目标而不沿用插入前索引。selection-shell-state-1.log 复现：新增前 list=0x3/Shell=0x11，新增后 list=0x1/Shell=0x10，selected=[]，焦点保留但真实选区已丢，不是纯绘制残留；该轮隔离守卫通过，原断言仍失败。

增加仅探针使用的有界消息观察器（128 条/阶段，溢出计数），回调不打印、不反查 Shell，阶段结束再输出；观察列表消息及父窗口 LVN_ITEMCHANGED，销毁前撤 subclass。selection-notification-observer-2.log 整项通过，但新增/删除期间明确先全局取消选择、恢复焦点、再恢复选择；这提供时序证据，不是修复。观察器接入后多轮通过不能消除先前失败，可能有观测时序影响。下一步对照未观察与完整宿主恢复路径，分辨恢复遗漏或异步完成采样边界。未修改产品选择策略，未发布。

## 动态共享上下文原型

实验用 PIDL 携带瞬时上下文 token；同一视图的目录绑定共享一个原子策略值，独立视图分别创建 lease。全局表只按 token 查弱引用，不按路径共享策略；lease 显式释放表项，绑定对象仅持有该上下文。比较与序列化不调用视图 getter。新增接口槽 SetSizeSortPolicy 同时接收混合位及方向位；旧的单独混合方法仅保留实验对照。

真实 ExplorerBrowser 动态对照 dynamic-sort-peer.log：主视图依次独立升/降、混合升/降、再独立升/降，完整行序全匹配。另建同物理路径视图，独立上下文固定为混合；每轮强制 peer 升序后降序以重新比较，最终混合降序不受主视图策略影响。主列表 HWND 始终相同，策略切换期间全目录枚举计数无增加。该轮后续补值/自动通知/选区/分组与清理全部通过，5 个宿主销毁后残留列表 HWND=0，隔离守卫干净。这是新的一次通过证据，不删除先前选区失败。

初次双视图探针在 peer 尚未完成异步枚举时读取固定四行，触发 rowCount 前置断言；已改为等待四行且读取期间检查当前数量。未改动真实行序断言。peer 同样启用 NoEnumRefresh，使策略对照不混入默认枚举刷新。

无窗口 smoke 新增：同路径两份 lease 不共享策略，同 token 的重绑定引用能观察更新，释放 lease 后注册项不可再取得；原有路径分段/非法数据测试仍通过。实验使用同进程瞬时 token，目前不具备跨进程解析或持久化能力；产品接入前必须保持稳定的保存路径、对外 Shell 身份及导航生命周期，不能把瞬时 PIDL 写入会话或直接作为外部进程入口。尚未接入产品菜单/INI，未发布。

本批 27 项后台总检整轮通过：src/.state/background-checks/20261104-232757-928cdf524aa34a7d9b96d11f5fbd6a59/summary.json，记录 914f754 加本批源码状态；额外显式排序实验构建和动态视图探针独立于总检。未进行可见 UI 验收。

## 稳定名称、子目录身份与对外数据对象

新增实际 Shell 接口对照，日志 src/.state/dynamic-sort-external-child-nested.log：动态 root 的 SIGDN_DESKTOPABSOLUTEPARSING 与普通稳定 edpath 名称相同（GUID 大小写按 Shell 规范化作不区分比较），SHParseDisplayName 后可往返；绑定 folder-empty 后 IPersistFolder2.GetCurFolder 返回的子目录能取得含稳定父身份的解析名称。GetUIObjectOf(IDataObject) 的 CF_HDROP 精确指向物理 file-2048.bin，不输出排序 token。

新进程仅接收稳定名称与预期物理目录，不预加载 DLL、不设置 profile class/context，通过现有临时 profile 注册元数据完成解析/绑定，并独立重复稳定名称、子目录父身份和 CF_HDROP 检查。子进程不创建窗口，15 秒有界等待且输出异步排空；清理只针对该测试子进程和自己的 COM 资源。

整轮动态六次排序、同路径 peer、后续自动变化与选区、清理通过，前台/输入守卫干净，5 宿主销毁后残留列表 HWND=0。初始测试曾因 GUID 大小写差异误报名称不符，已记录实际文本确认后修正比较，不改产品身份。上述证明稳定文本与物理数据对象可跨进程使用，不证明原始临时 PIDL 可跨进程传递，也没有验证原生属性页/第三方菜单全部格式或真实浏览器前进后退。产品入口必须继续使用稳定路径，临时 PIDL 不可直接持久化。

本批仅增加探针，NativeProbe Release 编译与最终 diff 检查通过；产品源码未改，未覆盖 temp.86。下一步核对应用现有导航/状态采集使用稳定名称的路径，并设计新视图生命周期接线。

## 标签配置接线：INI v15

ViewState 新增默认 false 的 MixedSizeSort init 属性，保留原六参数构造合同；Copy/record with 保留策略，ShellViewStateCapture 从真实 Shell 采集模式/列时继续保留应用自己的策略。原先尝试新增位置构造参数触发旧反射探针失败，最终采用附加属性避免改变构造合同，未跳过原探针。

会话格式升至15，逐标签写 MixedSizeSort；v1～14 默认 false，v15 缺失或非法值不算完整快照，未来格式保护保留，升级前备份。版本升级是为了旧应用看到新会话时拒绝覆盖，不能让旧版悄悄丢弃新策略。QDR 自有扩展沿用 SessionStore，因此保留开关；纯 Q-Dir 基础状态不虚构其策略字段，缺省独立排序。

新 mixed-size-sort-session-smoke 覆盖默认/复制/同路径独立/旧版本/缺失非法/未来格式/实际保存及新 store 重读/备份和采集代际保护；QdrExportSmoke 增加扩展开关及无扩展回退断言。未挂载生产 Pane 的 pane-tab-copy-order 新增新标签默认 false、复制与跨窗格复制继承 true 的实测断言，无 HWND/Shell 视图。日志 mixed-sort-session.log、mixed-sort-tab-copy.log。

后台总检扩至28项，完整通过：src/.state/background-checks/20261104-234720-c02fb62cb7734e0fbb03c35585815e57/summary.json；随后新增 Pane 断言单独重建执行通过。记录的是本批提交前源码，不是已发布载荷。菜单尚未开放，原生实验尚未接入产品宿主，未覆盖 temp.86 或用户配置。

## 原生排序入口与方向同步实验

新增 --product-size-sort-entrypoints-private-desktop。先前动态原型总在命令前写入目标方向，无法证明用户入口同步；新实验中只先改变混合位，升序用实际 header HWND 的本地鼠标消息，降序调用原生 sortdescending 命令，不预先 SetSortColumns/写入该次目标方向。没有 SendInput 或用户桌面鼠标移动。

实验确认六次列头操作改变了真实排序方向，但 IShellFolderViewCB 的 SFVM_COLUMNCLICK 计数一直为0（sort-entrypoints.log），不能依赖这个旧回调。Microsoft 默认排序回调页面也明确列出系统视图不识别 SFVM_GETSORTDEFAULTS 的限制：https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-getsortdefaults 。不将旧接口存在视为现代 DefView 入口可用。

新 SizeSortHeaderObserver 只在 probe 挂接原生 header，观察 HDM_SETITEMA/W 的 HDI_FORMAT / HDF_SORTUP、HDF_SORTDOWN；只对已知大小列更新原子上下文，不查询视图，不调用重排或重绘。显式原生 setter 仍在实验宏内。sort-arrow-entrypoints.log：六次模式/方向及 peer 隔离完整行序匹配，箭头策略更新计数递增至11；后续自动变化/选区/分组/清理及前台输入守卫整项通过，5宿主无残留列表 HWND。

初轮原生菜单重排时临时空行导致固定四行读取前置断言失败；探针现在等待当前行数恢复到4再读取，并保持3秒截止和完整四行顺序要求，不对空列表记通过。先前无箭头同步的入口轮在后续选区断言失败仍保留。

当前只验证了详细信息模式与固定列身份下的同步；列表/图标模式、列重排后的观察器重新定位、header HWND 重建及导航生命周期仍未覆盖。因此该方案没有接入产品宿主。NativeProbe Release 与显式实验 native 严格构建通过，最终diff检查通过，普通产品无新增排序行为，未发布。

## 列表/图标模式反例

新增 list/icons 两个独立入口，参数 nativeSortMode=3/1；先应用产品同样的 EnableAllViewHeaders，再切换模式。没有点击隐藏列头，升降序均用原生 canonical sortascending/sortdescending，保持其他真实行序/选区/隔离断言。

sort-arrow-list.log 首次失败后，补齐产品列头标志的 sort-arrow-list-product-flags.log 仍失败；sort-arrow-icons.log 同样失败。两种模式的 header 更新次数均0，独立升序通过，独立降序却将文件2048放到所有目录前。实际行序为 file-2048.bin、folder-12288、folder-25600、folder-empty，违反独立分类要求。两轮前台/输入守卫干净。说明方向没有同步，不能把详细信息模式中的成功扩大为所有查看方式可用。

随后 sort-arrow-details-regression.log 完整通过，证明新增模式分支没有破坏已验证的详细信息路径。NativeProbe Release 编译及最终diff检查通过。没有产品代码或发布载荷变更。

后续方案必须覆盖隐藏header时的原生排序状态变化；箭头观察器仅能作为详细信息的一条信号，不能成为产品唯一依据。不得静默禁用列表/图标模式，也不得把独立降序改成文件在前以通过测试。继续评估无需比较回调反查的通用排序命令/状态接线。

## 隐藏列头的命令后校正原型

先做 Shell 自带分类反例（shell-partition-native.log）：显式隔离开关补根 FILESYSTEM/FILESYSANCESTOR 属性、按共享混合位返回列分类标志、关闭自定义类别比较后，独立升序仍混排（0目录/2048文件/25600目录/40960目录）。整项失败、隔离守卫干净；此属性/列控制只在显式实验开关启用，不修改产品属性。

替代对照在原生命令返回后读取实际 GetSortColumns，再写共享策略；只有独立模式且方向与策略不同才执行相反方向/实际方向两次 native verb，确保 Shell 重新比较。混合模式和已同步方向不做额外重排。读取发生在命令外层，不在 CompareIDs/GetCurFolder/原生比较线程中反查。

sort-reconcile-icons.log：图标模式六次切换完整行序、每步已选且聚焦文件保持、同路径 peer 独立、后续自动变化/分组/清理整项通过，隔离守卫干净，策略期间完整枚举断言为0；额外 verb 提交0.354/0.419/0.756 ms，仅为四行夹具提交时间，不代表排序完成时间或大目录性能。sort-reconcile-list-guarded.log 同样全部功能断言通过，但末尾 last-input 变化，进程退出1，整轮隔离不通过；保留失败，不因功能通过改记整项全绿。首轮列表对照也因相同输入守卫失败保留。

新原型没有 header 观察器，不提前提供目标方向，列表/图标均走原生菜单命令。它目前是明确调用包装层的验证，未证明所有用户菜单分发都能被产品宿主覆盖；产品接线仍需消息/命令分发边界、重入导航代际与关闭生命周期保护。额外排序是否有可见闪烁及大目录开销未验证，不据小夹具毫秒值宣布体验达标。NativeProbe Release、显式 native 严格构建及diff检查通过。未更改产品宿主或发布。

## 托管上下文生命周期适配层

FolderSizeSortContext 已加入产品源码，提供候选ABI能力检测、创建、同上下文准备下一目录、原子策略更新和确定性释放。实例额外持有 native module 引用，在释放 native lease 后才释放 DLL；Prepare 返回独立的 caller-owned PIDL，不释放输入。同步锁保护 Update/Prepare/Dispose，重复 Dispose 安全，释放后拒绝继续调用。旧DLL缺任一候选导出时返回未支持，不持有句柄。更新不依赖本地策略缓存，避免同上下文的原生绑定改变策略后缓存失真。

候选 CreateFolderSizeSortContext/PrepareFolderSizeSortLocation/SetFolderSizeSortPolicy/ReleaseFolderSizeSortContext 目前只由显式隔离构建导出。NativeProbe 链接同一产品源码，并用适配层替换原来的手工 lease 创建/清理；动态视图本轮全部功能断言通过，但前台/输入守卫变化，整轮退出1（managed-sort-context-dynamic.log），不记全绿。

新 managed-sort-context-lifecycle-smoke 不创建浏览器/窗口、不注册namespace、不访问伪目录夹具：验证同路径两实例独立、跨目录准备仍共享策略、旧PIDL观察更新、非法方向不污染状态、重复释放、释放后调用拒绝、兄弟上下文继续可用及旧DLL能力缺失回退。managed-sort-context-lifecycle.log 通过；路径只是无IO编码输入，DLL加载本身仍有正常文件访问。

后台总检加入隔离ABI严格构建与上述纯检查，共30项整轮通过：src/.state/background-checks/20261105-002725-7000d4f00a98429b9078a3254bc6ecb0/summary.json。此时源码为 f227743 加本批修改。常规产品DLL仍不启用候选ABI；适配层尚未绑定实际宿主导航、菜单或关闭流程，不声称产品生命周期已经覆盖。未发布。

## 产品宿主导航与关闭接线（候选ABI仍隔离）

App/ProfileShellNamespaceOwner 提供上下文工厂，按实际 DLL 完整导出集缓存能力；Pane 创建 tab 时绑定工厂。ExplorerBrowserHost 在发起 provider Browse 前准备本标签 PIDL，并在 Shell getter 后重新核对导航请求；同一上下文复用于下一物理目录，未带本标签上下文的 provider 访问重新经稳定路径进入。新增 Contains/IsFolderSizeSortLocationForContext 按上下文身份判定，不按物理路径全局共享。INI/地址继续用既有稳定物理路径。

应用自有的 NativeViewStateTarget.SetSorting 在调用 Shell 前同步原子策略，并前后核对当前代际；关闭或重建 browser 时，先销毁 browser/TravelLog，再释放上下文。常规产品DLL尚无候选导出，工厂返回未支持，菜单不开放；尚未接实际用户原生菜单命令后的校正。

纯生命周期测试新增旧普通PIDL、同/异上下文及跨目录PIDL的 Contains 检查，host-context-ownership-pure.log通过。候选四产品 HwndHost 探针 product-sort-context-host.log 证实四上下文独立、跨目录复用、后退/前进保持上下文与稳定路径，关闭后四上下文全部 retired=True，隔离守卫干净。但后续 pane3 保存排序失败：接口为Size升序、实际37/11降序，故整轮失败，不能记产品接线完成。首次误用了旧 net10.0-windows 输出目录导致类型加载失败，已改用实际目标 net10.0-windows10.0.19041.0；未替换旧输出或发布文件。

探针新增生命周期断言时发现：RootVisual=null仅会停放HwndHost；原探针source分支没有显式Dispose各host。现source销毁后逐一Dispose宿主，再断言上下文释放，避免以进程退出冒充产品关闭证据。此修正只影响探针，不改生产窗口关闭路径。

普通后台30项整轮通过：src/.state/background-checks/20261105-004318-297c58096f9d4d7180dced442edb5593/summary.json（本批源码状态）。最后对HostProbe反射构造新增第三个排序回调的接线，HostProbe/App Release重建通过；未据此重跑并关闭候选保存排序失败。候选ABI native严格构建与最终diff检查通过。未覆盖temp.86。

## 2026-11-05 初始排序恢复：确定性修复与对照

新增默认关闭、按宿主最多128条的恢复轨迹，记录阶段/退出/输入版本/代际，不在比较回调中反查Shell。隔离探针明确观察原生临时降序后立即调用产品CaptureViewState。旧版 transient-sort-capture-before.log 确定失败：缓存被写成临时降序；修正版在同阶段保留升序并报告未完成。临时保护不污染原有失败位，避免隐藏标签在恢复结束后仍被临时失败永久阻断；getter序列还核对临时状态版本，拒绝穿越临时阶段的重入采集。

每个恢复任务拥有序号。新任务/销毁会退休旧任务；旧任务只能清理自己的临时标志，不能解除新任务的重绘保护。抑制重绘前后核对当前任务，过期候选lease自行释放。列可见性Shell getter后再次核对代际，避免迟到读启动旧任务。

第二条对照发现空owner的后台排序命令会在本机四窗格中返回成功却留下错误行序。保持捕获修复不变，只将owner切回0的 sort-null-owner-control.log 再次在pane3失败（接口升序、实际37/11降序），守卫干净；自动finally恢复源码。DefView子窗口owner的四窗格虽通过，但截止分支有0/未知值及行序失败（sort-recovery-deadline.log），保留失败。最终使用本窗格宿主_hostWindow，普通可见菜单的顶层owner逻辑不改。

最终窗格宿主owner对照：sort-pane-owner-candidate.log 四上下文、临时捕获、导航历史、真实行序、大小、选区/焦点、释放和守卫整项通过；sort-pane-owner-deadline.log 观察真实临时反向，再只将readiness时钟推进到31秒，保存升序/5与29字节行序及选区全部恢复，整项通过。早先修正版直接DefView owner的成功轮 sort-recovery-candidate-final.log 和普通DLL轮 sort-recovery-regular-host.log也保留，但不替代最终owner证据。

大目录最终宿主owner回归：sort-recovery-large-no-observer.log / sort-recovery-large-observer.log 均已退出0、完成清理并通过隔离守卫，四窗格各1025目录，4,100条目独立数值/升降序/选区/焦点全部正确；观察器开启时记录4100个通知身份且没有丢弃。第一轮清理未结束即误启动第二轮，两轮清理/扫描有重叠；因此保留功能证据，不将耗时当作独立性能基准。观察超时期间均跟踪原会话，没有因短观察超时重启进程。

最终后台30项整轮通过：src/.state/background-checks/20261105-011951-d744e826334047a9913d0c88c82575b1/summary.json（48d8411加本批源码）。最后只补旧异常分支的同一序号重绘lease保护，App/HostProbe重建检查另行记录。本批没有覆盖预览；混合排序原生菜单校正产品接线及可见验收仍待完成，不因初始排序回归通过宣布temp.86全部完成。

边界：本批的确定性打断控制是导航/状态采集，不代表已覆盖用户键鼠输入在临时反向阶段的所有取消语义；该交互仍保留在集中验收及后续命令协调检查中。


### 2026-11-05 输入补偿的行就绪反例（未修复）

新增私有桌面实际四宿主输入边界探针，不发送硬件输入。第一次记录反向排序返回时原生行已为0（sort-input-boundary.log），因此不能把随后选择失败全部归因于补偿。进一步要求反向排序已有非空原生行后才触发输入，保持补偿后立即选择的断言：sort-input-ready.log 确认 before=2、after=0，立即选择 folder-large 失败。两轮前台/输入守卫均干净，HostProbe Release 构建0警告0错误。

结论：同步恢复保存方向虽能恢复方向标志，但会再次进入原生异步空行阶段，不能直接让原手势继续执行。此输入补偿仍为未提交候选，不开放混合排序菜单、不覆盖temp.86。后续修复必须同时证明实际行序、原手势意图、后续用户排序不被旧任务覆盖；不能用增加探针等待代替手势正确性。保留非空前置条件及立即选择失败断言作为回归。


### 2026-11-05 消除临时方向的两项负对照

1. 将首次反向命令临时改为保存方向，保留原四宿主实际大小/行序门槛：same-direction-host.log 首窗格初值29/5，未收敛到升序，达到90秒私有探针截止；前台与last-input未变。不能以相同方向命令返回成功替代实际重排。
2. 保留反向命令，但在同一次Dispatcher回调中立即执行保存方向：immediate-sort-host.log 同样未越过首窗格排序门槛并达到截止。此结果不支持删去两命令之间的原生异步完成等待。

两轮都是实验失败，不是完整隔离清理通过；探针进程均已终止，源码恢复实验前版本并重新构建。未把实验参数留在产品代码。输入边界探针作为显式诊断入口保留，未加入应为绿色的统一后台门槛；它在当前实现上预期复现失败。下一步需要处理原生异步排序期间的交互合同，不能用同步双命令或同向命令绕过。


### 2026-11-05 原生选择动作先于排序恢复

输入不再同步发送方向命令，而先更新恢复任务的输入版本、停止其计时器，并排队在原生动作之后捕获最新选区。再恢复保存方向并用该快照恢复选择；连续输入仅接受仍有效的最新导航/输入上下文。过期排队任务释放快照与重绘保护。列头及显式WM_COMMAND不走选择保留路径，防止把用户改排序误当成要恢复的选择；这些命令在恢复中途的完整合同仍需单独验证。

sort-post-input-host.log、sort-input-consecutive-no-edit.log、sort-input-review-host.log 均整项通过：补偿前后原生行数2/2、立即选择成功、连续两次操作后保留最后选区、后续用户降序不被旧任务覆盖；并继续通过四宿主实际数值/行序、导航、分组筛选、焦点、关闭四上下文及前台/输入隔离守卫。不是物理鼠标/键盘验收。

同一最新测试在HEAD旧实现上的sort-input-red-control.log稳定失败：输入版本0/2导致任务取消，保存升序未恢复；前台/输入守卫干净。因此此回归区分旧实现与修正，不靠测试额外等待掩盖输入前清空。此前同步恢复断言已替换为原手势立即可执行和异步最终正确两项实际合同。

重命名/鼠标按住时计时器暂停重排。此保护暴露测试辅助函数0x1f包含SVSI_EDIT(0x3)，意外启动重命名；已按微软SVSIF定义改为0x1d（SELECT|DESELECTOTHERS|ENSUREVISIBLE|FOCUSED），保留附近注释。见 https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif 。连续选择最初因真实编辑框保护暂停而超时（sort-input-consecutive-trace.log），不记作排序通过。实际F2编辑/拖选仍需专门验收，不能仅据分支存在关闭目标。

普通产品的混合策略激活与菜单方向校正、恢复期间用户原生排序/重命名/导航的剩余边界、整批temp.86集中可见验收均保持未完成；不发布中间版本。

最终审查源码的30项后台整轮通过：src/.state/background-checks/20261105-020009-23edead34bee427b94ebdeea12a381ef/summary.json。此前一轮20261105-015737亦通过，但早于列头/WM_COMMAND区分和过期回调清理补充，最终证据以上述020009为准。未覆盖发布。


### 2026-11-05 显式排序取消与原生编辑框补充

增加两项显式私有桌面入口：--product-sort-command-host-private-desktop、--product-sort-rename-host-private-desktop，参数同输入探针（HostProbe.dll、实验adapter.dll）。均复用实际四宿主及其后续完整检查，不创建可见用户桌面窗口，不发送全局键盘输入。

命令场景在临时降序、真实行已就绪时模拟原生命令输入边界并调用Shell canonical sortascending。sort-command-cancellation-final.log整项通过：原任务出现取消记录且没有继续发送saved-verb，当前升序和后续降序保持正确；并通过四窗格行序/数值、分组筛选、导航、选区焦点、关闭上下文及隔离守卫。只覆盖此canonical命令序列，不等价于真实菜单ID路由或用户选择其他列。

重命名场景显式使用SVSI_EDIT在folder-large打开真实Edit，泵消息350ms确认编辑仍聚焦、排序仍为临时降序（未在编辑中重排），向私有桌面编辑控件发送Escape取消后，恢复原排序与最新选区。sort-rename-host-final.log整项通过且隔离守卫干净。首轮因反射遗漏Public标志失败（sort-rename-host.log），已修正探针，不将该轮记成功。该证据覆盖原生编辑生命周期，不替代物理F2/方向键、长时间编辑及拖选验收。

普通连续选择回归sort-boundaries-selection-control.log再次整项通过。HostProbe/NativeProbe Release构建均0警告0错误，diff检查通过。本批只扩展诊断入口/测试，没有修改产品实现或重跑宣称整轮30项总检。混合策略正式激活、原生菜单/列头方向同步、剩余交互合同及temp.86集中验收继续推进，不覆盖发布。


### 2026-11-05 原生命令后的产品宿主策略校正（候选ABI）

新增显式 --product-user-sort-host-private-desktop（HostProbe.dll、实验adapter.dll）。它只创建一个真实ExplorerBrowserHost，以隔离分类命令合同，保留跨目录/历史/上下文关闭检查；原四宿主输入/编辑/分组测试不删减。夹具最终为5 B、29 B两个目录与17 B文件。

重要调查修正：初版复用分组/筛选后续状态，虽然辅助TrySetGroup(null)返回成功，但再次Refresh恢复了大小分组，view-state实际GroupKey仍为PKEY_Size。此时全列表不应按未分组数值断言；user-sort-policy-value.log / user-sort-compare-trace.log证明上下文匹配、共享policy校验S_OK且比较回调5/17/29正确，不能据该夹具宣布混合比较器失败。独立分类场景现跳过这段分组夹具，仍由普通四宿主场景覆盖分组。辅助分组清除与后续状态恢复的边界另待核查。

真正的未分组旧版反例为user-sort-ungrouped-final.log：原生菜单降序未保持文件夹在前，整项失败且隔离守卫干净。前面两轮并发对照在进入新增断言前已有第三窗格初始排序失败（user-sort-red-native.log、user-sort-list-red.log），不能算新增分类失败证据；也说明初始排序稳定性尚不可关闭。

产品宿主现于DefSubclassProc命令/上下文菜单返回及列头左键释放后合并排队读取当前单一PKEY_Size排序。只有同一browser/视图代际/上下文/provider仍有效时才同步方向；分类模式方向变化复用已有有界恢复、选区/输入保护，混合/分组不做额外校正。跳过自己发送的排序命令，方向不变不重排；暂态期间收到的用户校正延后到暂态结束。视图拆除中止排队与延后请求。不在CompareIDs/GetCurFolder中读Shell状态，不改变右键菜单内容。

user-sort-final-direct.log整项通过：原生命令分类降序29/5/17；再直接更新本标签测试策略并走一次原生命令，混合升序5/17/29、混合降序29/17/5、分类升序5/29/17、分类降序29/5/17全部实际行序正确。没有保留测试中诊断性的额外反向命令。测试修改策略仍是反射到候选上下文，不代表选项菜单已交付。活动IPersistFolder2根已验证属于本标签上下文，测试PIDL克隆与诊断module都有确定释放。

user-sort-queue-four-pane-regression.log四宿主连续输入/选区/实际大小/方向/分组筛选/关闭以及隔离守卫通过（最后仅补provider范围与延后请求清理）。最终30项后台整轮通过：src/.state/background-checks/20261105-023212-5fa9707cb566433b8a4ca20d225c53d8/summary.json。列表模式合同不是图标/详细信息列头、其他排序列或可见重绘性能验收；这些、初始并发排序反例、混合开关入口与正式ABI激活仍待完成，temp.86不发布。

最终源码另通过user-sort-final-four-pane.log四宿主整轮（含最终provider范围/延后清理），隔离守卫干净；不消除之前两轮初始排序失败事实。


### 2026-11-05 恢复结束前核实实际行序

initial-sort-stability-1.log重现原问题且隔离守卫通过：第三窗格初值37/11，恢复轨迹为opposite返回1415ms、saved返回1843ms、finish1968ms；最终标准PKEY_Size/+1但行仍37/11，75秒探针失败。由此确认命令提交成功/方向标志正确/行数恢复均不能证明实际排序已完成；这不是等待大小值超时，也不是前台干扰。

新增ShellSizeSortOrder以活动IFolderView2顺序读取IShellFolder2标准PKEY_Size原始VT_UI8及SFGAO_FOLDER，验证数值升降序和文件夹分类边界，不解析格式化文本。返回true=已符合、false=实际反例、null=数量变化/值未就绪/代际退休。所有PIDL、folder与view引用确定释放；每8行让出Dispatcher，避免大目录校验连续占用UI线程。

初始恢复及命令后复用恢复在结束前进行该校验。未确认则等待；两次错误行序样本触发原生方向重试，仍受原30秒预算限制，保留最新选区输入处理；异步检查期间防止计时器重入，导航退休立即终止。重试分支的受控故障注入尚未完成，不能仅以正向测试关闭偶发问题。

阶段证据：同步首版initial-sort-verified-1/2/3.log顺序三轮四宿主通过，但未触发retry标记，不声称覆盖重试。异步版本size-order-async-input.log实际四宿主选择、大小、分组/筛选、历史、释放及守卫通过；size-order-async-large.log 4×1025目录实际升降序、数值、选区焦点、清理与隔离守卫整项通过，进程已退出0后才启动下一轮；warm-size-read约1.97～2.19ms/窗格，仅为该探针读值指标，不是新增校验总耗时或UI性能验收。

最终源码30项后台总检通过：src/.state/background-checks/20261105-025054-8fd7e4e52fea4f0d8f4ff49482350d7b/summary.json。size-order-final-policy.log确认正确/错误方向、数量变化/退休合同及含文件夹/文件的混合/分类相反策略区分均通过，隔离守卫干净。先前size-order-async-policy.log因探针直接改native策略却未同步宿主已提交方向，触发额外异步校正导致读值尚未就绪；现改用产品UpdateFolderSizeSortPolicy路径，不修改产品以迎合该夹具。

边界：最终只有资源释放和回调包装整理晚于大目录轮，其后总检及策略夹具已重新执行；未重做最终源码的大目录性能比较。图标/详细信息原生入口、受控重试、长编辑/拖选及集中可见验收、选项入口和正常DLL激活仍未完成。不覆盖temp.86。
