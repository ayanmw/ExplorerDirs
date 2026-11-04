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
