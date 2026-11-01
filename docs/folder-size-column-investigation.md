# 逐行文件夹大小与子项计数列调查

## 2026-11-01：递归列 Q-Dir 基础字段闭环

在前一轮验证 Q-Dir 基础 `ColsAndSort` 导入与应用扩展导入后，增加一次基础字段往返：先导出包含递归列的三列状态，剥离 `ExplorerDirs.*` 扩展导入，再用导入结果重新导出，第二次仍剥离扩展并导入。排序/显示键 `{9e5e…}:1`、递归数据/分组键 `{7e1f…}:1`、`Type → Name → Recursive Size` 顺序、各列宽度和排序/分组方向均保持一致。定向 `--qdr-location-roundtrip-only` SettingsSmoke 通过。

这加强的是项目自己的 Q-Dir 基础字段读写合同；Q-Dir 实际 GUI 读取/显示递归列、产品 provider 映射与可见排序仍未验证。产品继续使用 ExplorerBrowser，未改产品代码或 release。

## 2026-11-01：列重排后的可见标题与 Shell 排序键映射

扩展已注册 synthetic namespace 的 `IShellView` 探针，对 `IColumnManager.Apply` 应用两种三列排列，再从 native Header 读取 `HDM_GETORDERARRAY`，按视觉位置逐列点击并回读 `IFolderView2::GetSortColumns`。两种排列下，“Recursive Size”标题点击均返回 Shell sort/display key `{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1`；“Name”和“Type”也分别返回其自身的 Shell 排序键。排序方向随标题点击变化，探针结束前恢复了原列与原视图状态。

此项修正先前探针对“列标题索引”的假设：Header 的 item index 与屏幕显示顺序是不同索引域；必须用 `HDM_GETORDERARRAY` 映射视觉列位置，不能直接拿 `IColumnManager` 快照索引模拟用户点击。NativeProbe Release 构建通过（0 警告/错误），注册 namespace Private Desktop 探针返回并通过前台、输入、隔离清理守卫。日志：`src/.state/native-probe-watchdog/20261101-233440-382b5b77.stdout.log`。这仍是合成 `IShellView` 原型证据，未接入产品 ExplorerBrowser，也未验证用户可见列重排、产品排序状态捕获或 Q-Dir 导入后的列键映射。

源码复核显示，产品的 `ColumnLayout.IsValid` 不限制已知列白名单：它验证非空格式 GUID、宽度范围和列键唯一性；`SessionStore` 保存/恢复 PROPERTYKEY 与宽度，`ShellViewStateCapture/Restore` 也保留 Shell 当前给出的列/排序键。随后扩充的跨进程 INI smoke 将三列按非默认 `Type(160) → Name(240) → Recursive Size(176)` 排列保存；子进程精确读回列序、宽度、Shell sort key `{9e5e…}:1`、递归数据/分组 key `{7e1f…}:1`、排序方向和路径。定向命令先因旧的两列断言而失败，改为逐列验证后通过。因此通用会话层不是递归列的首要阻断点。当前缺口仍是产品 ExplorerBrowser 所浏览的物理文件夹没有递归大小值 provider；这些通用快照代码只会保存 Shell 实际暴露的列，不能创造列或逐行数据。此判断来自源码路径审阅及配置层 smoke，没有改动产品实现，也不把“未知键可持久化”误当成列值消费通过。

## 2026-11-01：Shell 多项文件通知合并失效

将多项 `IFileOperation.NewItem`/`DeleteItem` 排入单个 Shell 操作，在 `size-10` 中分别创建三个文件（1/2/3 B）和删除这三个文件。探针先等到三个不同路径的 `SHCNE_CREATE`/`SHCNE_DELETE` 都抵达，再将整批通知送入一次缓存失效处理：两组都只失效一个递归根；两视图各请求一次并共享单次重扫，结果由 20 B 到 26 B 再回到 20 B。每批通知没有造成三次重复扫描；行 ID、排序、选择和焦点保持。

Release NativeProbe build 0 警告/错误；三轮 Private Desktop watchdog exit 0。日志：`20261101-203234-7a75f2cb`、`20261101-203244-cd986f58`、`20261101-203251-8752c7fb`。这验证探针将已到达的通知批次合并后失效，不代表生产 watcher 的时间窗/去抖策略、Shell 通知丢失补偿、通知持续风暴或产品接线。

## 2026-11-01：同路径目录替换时拒绝旧扫描结果

双 `IShellView` 递归列原型先缓存 `nested` 子目录 4 B；`HandleBoundRecursiveDirectoryScanner` 完成旧目录枚举后，在仍持有旧根目录句柄时暂停返回。`IFileOperation.DeleteItem` 删除该目录后，旧子目录缓存代次递增，父级重新扫描为 20 B。接着在相同路径用 `IFileOperation.NewItem` 创建新目录并写入 6 B 文件，系统自然发送 `SHCNE_MKDIR`；新子目录文件 ID 与旧目录不同，两个视图共享新路径 6 B 缓存，父级共享扫描为 26 B。最后释放旧扫描：它带着旧身份和 4 B 摘要返回，但旧代次不匹配，发布被拒绝，新目录的 6 B 与父级 26 B 缓存都保持不变。

Release NativeProbe build 0 警告/错误；三轮 Private Desktop watchdog exit 0，删除/重建事件、缓存代次、身份差异、双视图共享和选区/焦点断言均通过。日志：`20261101-203102-c6fcdcff`、`20261101-203113-cd6c727a`、`20261101-203120-937c6e3a`。新增的扫描后暂停回调仅供 NativeProbe 竞态控制；缓存发布代次仍是探针模型，产品递归列未接入，也未覆盖通知丢失或网络存储。

## 2026-11-01：自然目录创建与删除失效

在双视图夹具的 `size-10-renamed` 内，通过 `IFileOperation.NewItem` 新建子目录并加入 4 B 文件；探针自然收到 `SHCNE_MKDIR (0x8)`，使已缓存的递归父目录摘要失效，两视图共享新摘要 24 B。随后用 `IFileOperation.DeleteItem` 删除该非空子目录。实际系统事件为两条 `SHCNE_DELETE (0x4)`（其中一条的路径是被删目录本身），没有发出 `SHCNE_RMDIR`；失效逻辑据目录路径退休递归父项缓存，两视图共享重扫回到 20 B。为覆盖 Shell/驱动可能发送的目录专用事件，失效归类同时识别 `SHCNE_MKDIR/RMDIR`。

Release NativeProbe build 0 警告/错误；三轮 Private Desktop watchdog exit 0，行 ID、排序、选区/焦点、前台和隔离清理守卫通过。运行日志：`20261101-202828-2e0b0384`、`20261101-202837-5540c51d`、`20261101-202843-91570258`。这只验证本地临时夹具和探针缓存模型，不覆盖同路径替换竞态、通知丢失/合并、网络路径或产品接线。

## 2026-11-01：Shell 文件创建、删除、改名的自然通知

将双视图递归列探针中的测试文件创建、删除和改名改由 `IFileOperation.NewItem/DeleteItem/RenameItem` 执行，移除这些操作后的手动 `SHChangeNotify`。`SHChangeNotifyRegister` 连续三轮自然收到 `SHCNE_CREATE (0x2)`、`SHCNE_DELETE (0x4)`、`SHCNE_RENAMEITEM (0x1)`，其中改名还观察到父目录 `SHCNE_UPDATEDIR (0x2000)`。每次都只失效 `size-10` 的递归摘要，两视图共享一次重扫，结果依次为 23/20/20 B，行 ID、排序、选区/焦点不变。Release NativeProbe build 0 警告/错误，Private Desktop watchdog 三轮 exit 0。

目录内容直接写入导致的 `UPDATEITEM/UPDATEDIR` 仍由探针显式通知；普通文件写入不是 `IFileOperation` 操作，本轮没有证明其自然 Shell 通知。探针事件只在临时本地夹具复验，未测网络路径、通知合并/丢失、用户级文件操作或产品 watcher 接线。日志：`20261101-202619-4e8f14d0`、`20261101-202624-664af1c5`、`20261101-202629-0815dd14`。

## 2026-11-01：目录改名期间旧路径在途扫描退休

在双真实 `IShellView` 递归列 NativeProbe 中，让 `HandleBoundRecursiveDirectoryScanner` 打开 `size-10` 的真实目录句柄后暂停；随后由 `IFileOperation.RenameItem` 执行真实夹具目录改名，不再从探针手动调用 `SHChangeNotify`。注册在父目录的 `SHChangeNotifyRegister` 窗口收到自然 `SHCNE_RENAMEFOLDER` 事件后，改名通知使旧路径缓存代次失效，两个 peer view 在新路径只共享一次新扫描。释放旧 worker 后，它仍通过已打开句柄读回 20 B，但旧路径已不存在且代次不匹配，因此该结果不能发布或重建旧缓存；新路径缓存仍保持两 view 共用同一个结果。

Release NativeProbe build 0 警告/错误；自然 Shell 通知版本的 Private Desktop watchdog 连续三轮 exit 0。三轮均读回稳定 row ID=3、排序与选区/焦点，前台、输入和夹具清理守卫通过。此处“代次”仅为探针模拟的缓存发布票据；产品逐行列仍未接入，不能据此声称产品的多视图生命周期已解决，也未覆盖网络/慢盘句柄行为或通知风暴/丢失时序。日志：`20261101-202418-ef5463eb`、`20261101-202423-9670e5d2`、`20261101-202428-0dc01979`。

## 2026-10-31：过期重命名映射不转移行身份

Shell 变更回调和目录重枚举之间存在时间差：旧目录改名后可能被删除，目标路径在重枚举前又出现另一个目录。`ShellListViewRowModel.Reconcile` 现只在 rename 映射两侧都是文件系统目录、且两侧身份都可读但不相同时拒绝继承旧行 ID；目标行取得新 ID，旧大小仍清空。身份一致的改名继续保留 ID/大小；普通同路径刷新维持现有路径行身份策略。

SettingsSmoke 用真实临时目录执行改名、删除、同路径重建，先按预期观察到旧实现把旧 ID 交给新目录，再验证修复后目标分配新 ID、文件 ID正确、大小为空且旧 PIDL 进入退休集合。定向行模型 smoke、完整 Release SettingsSmoke 及 NativeProbe Release build 通过，0 警告/错误；未创建 Shell view 或 UI。此合同只覆盖行模型处理逻辑，真实 Shell 通知时序仍待迁移验收。

## 2026-10-31：同一目录改名时沿用递归大小快照

Shell 重枚举可通过旧/新解析名映射识别被改名的行。`ShellListViewRowModel.Reconcile` 现在在前后两行都是文件系统目录且卷序列号/文件 ID 一致时沿用旧递归大小摘要，即使路径已经变化；若类型或身份不一致仍丢弃旧摘要。这样同一目录对象改名时不会先显示空值，而同路径被替换成不同目录的已有保护保持有效。

SettingsSmoke 对两个真实临时目录执行改名，并反序重枚举，验证各自行 ID、目标路径、文件 ID 和摘要没有串线，同时旧 PIDL 都进入退休集合；定向 `--shell-listview-row-model-smoke` 与完整 Release SettingsSmoke 通过，NativeProbe Release build 0 警告/错误。此合同只针对隔离行模型，未创建 Shell view 或接入产品；真实 Shell rename 通知映射、异步扫描竞争及可见排序仍待迁移门槛验收。

## 2026-10-31：包装子 PIDL 的父绑定身份复核

普通包装-folder smoke 现把两种不同使用方式分别断言：普通 backing 绝对 PIDL 可解析为预期文件；将包装的相对 child PIDL 原样与 backing 文件夹 PIDL 组合后，`SHGetNameFromIDList(SIGDN_FILESYSPATH)` 按预期返回 `E_INVALIDARG`，因为包装 envelope 只由其父 adapter 解释。随后通过 `SHCreateItemWithParent(adapter, wrappedChild)` 取得的 Shell item 必须返回正确的文件系统路径和绝对 parsing name，`IParentAndItem` 还需保留 adapter 的 `IUnknown` 与同一 child PIDL。完整 wrapped-folder smoke 通过，窗口未创建。

因此输出字段现在明确写作 `backing=resolved; wrapped-child-as-absolute=expected-reject; parent-bound=resolved`，不再把父对象缺失时的拒绝显示成含义不明的 `unresolved`。Shell `IDataObject` 仍会将文件系统路径序列化为普通文件系统 item；这不保留 adapter 的 COM/PIDL 身份，属于不同合同。

## 2026-10-31：包装子 PIDL 的枚举器克隆合同

NativeProbe 的 `WrappedEnumIdList` 现在由共享、惰性缓存的子项 PIDL 状态支持；每个包装枚举器保留独立游标，因此 `Clone`、`Reset` 和 `Skip` 不依赖底层文件系统枚举器实现 `Clone`。合同从非零游标克隆后逐项比较 PIDL 身份，并检查 `Reset`、克隆后 `Skip` 与 `Next(count)` 的批量及末尾部分读取语义。另对底层已交出首个 PIDL、包装缓存随后报 `E_OUTOFMEMORY` 的时序做故障注入：首次调用报告错误但不返回项目，重试仍取得与基线相同的首项；缓存改为直接接管 inner 返回的 PIDL，避免复制分配。夹具同时包含文件和子目录，Shell data object/CIDA 检查都接受两类真实文件系统项目。

验证：原生 Release DLL 与 NativeProbe Release 构建通过；`--static-shell-column-wrapped-folder-smoke` 的正常枚举与一次性缓存失败重试、`--static-shell-column-parent-item-smoke` 和 `--wrapped-pidl-contract-smoke` 均退出码 0，输出包含 `enum-clone=supported`、失败后首项身份保留、相同 parent/child PIDL 身份以及 `window-created=False`。父项绑定合同还断言 `SHCreateItemWithParent` 同时返回预期 `SIGDN_FILESYSPATH` 和 `SIGDN_DESKTOPABSOLUTEPARSING`。转为 `IDataObject` 后再建 Shell item array，普通文件路径仍相同，但 adapter 的 parent `IUnknown` 和包装 child PIDL 身份不再相同；这说明文件系统目标保留，不能据此声称 adapter 身份能穿过 Shell data object 往返。这些均为无窗口隔离合同。

边界：`SHCreateItemWithParent` 使用包装父项和子 PIDL 可取得预期项目及绝对名称；但把包装子 PIDL 直接与普通文件系统根 PIDL 做原生绝对组合，仍报告 `0x80070057` 未解析。Shell 从底层文件系统 `IDataObject` 构建 item array 会保留普通文件系统路径，但不会保留 adapter 的 COM/PIDL 身份。这些结果彼此不矛盾：前者是显式 parent-bound item，后者是 Shell 标准序列化路径。未注册 synthetic namespace root；合同仅创建并释放一个未激活的 `IShellView` COM 对象，没有创建/激活视图窗口，也没有修改产品 ExplorerBrowser；迁移资格与完整 Shell 兼容性仍未验证。

补充文件操作合同：从包装 adapter 的单项文件子 PIDL 取得 `IDataObject`，真实 `IFileOperation::CopyItems`、`MoveItems`、`RenameItems`、`DeleteItems` 均在 NativeProbe 自有临时夹具中通过，并核对源/目标路径及字节；随后通过 `GetUIObjectOf` 一次取得两项文件的多选 `IDataObject`，确认 `CF_HDROP` 与 CIDA 都含有匹配的两条绝对路径，再执行两项复制、移动、删除并逐项核对文件系统结果。多选 `IContextMenu` 在内存菜单中查询到 103 个命令、25 项菜单，含 `copy` 和 `delete` 动词；菜单没有显示或执行。所有操作调用前后前台 HWND 均保持不变。操作静默执行且禁用 copy hooks，没有创建/激活 Shell 视图窗口。由此确认这些数据对象可承载普通文件单选与多选复制、移动、改名和删除，且包装层能构造原生多选菜单对象；但不证明菜单可见行为/命令路由、undo/redo、通知、OLE 拖放源端、虚拟项目身份往返或 ExplorerBrowser 产品体验。

## 2026-10-31：扫描快照跨 provider 重建

将递归大小表从 `SyntheticSortFolder` 单实例字段提取为可引用共享的 `SyntheticRecursiveSizeSnapshotStore`。同一 provider 创建的嵌套 folder provider 共享该 store；NativeProbe 则保留 store 所有权、释放旧 provider，再构造新 provider 并复用 store。新 provider 对重新解析出来的 sibling PIDL 继续按绝对 Shell parsing name 读取各自的 6 B/12 B 快照，列文本、`VT_UI8` 属性和相对 8 B 文件的数值排序均通过。

验证：原生 Release DLL 构建成功；`dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --static-shell-column-folder-smoke` 退出码 0，输出含 `re-enumerated-pidl-retained=passed`、`provider-recreation=retained` 和 `window-created=False`。probe 显式把共享 store 传给新 provider，证明的是 store 生命周期合同，不证明 Shell class factory 会自动让新对象取得同一 store。

边界：扫描结果如何由产品 owner 持有并注入重建后的 provider 尚未接线；静态 class factory 创建独立 provider 时仍会产生独立 store。该 probe 不建 Shell view 窗口，不验证 DefView 的列刷新、可见排序、菜单/焦点/拖放、通知或 Q-Dir 互操作。产品仍用 ExplorerBrowser，逐行大小列仍为 P2 原型。

## 2026-11-01：通过 class factory 创建共享快照 provider

递归大小身份缓存合同现在让旧 provider 退出后，由 `SyntheticFolderClassFactory::CreateInstance` 创建新 provider，再读取快照。为避免新 provider 从 backing 目录的真实大小碰巧得到相同值，探针在重建前把缓存设为与磁盘递归总量不同的哨兵值。默认独立 factory 的红测因读不到快照而按预期失败；factory 显式持有并传递同一 snapshot store 后，列文本、`VT_UI8` 值与数值排序通过。相同路径目录替换后，新文件 ID 仍拒绝旧快照。

Native adapter `/W4 /WX` 与 NativeProbe Release 构建通过；`--static-shell-column-folder-smoke` 退出码 0，输出 `factory-created-provider-shared-cache=passed`、`same-path-folder-replacement=stale-snapshot-rejected`、`window-created=False`。该结果证明可将快照存储沿 class factory 创建路径显式注入新 provider；`DllGetClassObject` 仍创建独立 store，Shell 的自然 COM 激活、factory 之间共享、活动 view 更新和产品扫描器接线尚未验证。产品仍使用 ExplorerBrowser，不能据此声称逐行大小列可用。

### 2026-11-01：独立 `DllGetClassObject` factory 共享快照并按可卸载周期释放

将同一缓存合同提升为两次独立 `DllGetClassObject` 调用：第一个 factory 创建 provider 并写入快照，旧 provider 和 factory 释放后，第二个独立 factory 创建 provider 并读回与磁盘递归大小不同的哨兵值；相同路径替换仍因文件 ID 变化拒绝旧快照。无共享 store 的红测因列文本读不到快照而失败，加入 DLL 级受锁 store 后通过。`DllCanUnloadNow` 现在在无活动合成 COM 对象、服务器锁或进行中的 class-object 调用时释放 DLL 持有的 store 并返回 `S_OK`；NativeProbe 在释放 fixture COM 对象后显式检查此合同，随后才 `NativeLibrary.Free`。

验证：原生 adapter `/W4 /WX` 构建、NativeProbe Release 构建、`--static-shell-column-folder-smoke` 与 `--synthetic-dll-unload-contract-smoke` 均通过；前者输出 `cross-dllgetclassobject-shared-cache=passed`、`window-created=False`、`DllCanUnloadNow=S_OK; shared-store-released=True`。没有创建 Shell view/window，也没有前台或 UI 验收。

边界：探针直接调用 DLL 导出的 `DllGetClassObject`，没有经由 COM loader/Shell namespace 执行自然激活；没有验证真实 Shell 是否在 view 重建过程中创建新 provider、活动 DefView 的更新、进程重启缓存或产品扫描器接线。store 在 DLL 可卸载前按绝对解析名累计快照，目前没有容量上限/逐目录淘汰策略，因此这不是产品缓存设计或内存安全结论。ExplorerBrowser 产品未变更，逐行大小列仍未实现。

### 2026-11-01：已确认 Shell 自然激活入口，跨 provider 快照仍未验证

复跑现有 `synthetic-recursive-directory-size-private-desktop` 时，NativeProbe 为唯一临时 CLSID 写入用户级 `InprocServer32` 和 Desktop `NameSpace` 项，再用 `SHParseDisplayName`/`SHBindToParent` 创建合成文件夹及 view。输出 `registered-class-factory-creates=1`，初始 Details 列读取到递归大小 `10`，升/降序比较也通过；这确认 Shell 经过 COM 注册调用了 adapter 的 class factory，自然激活入口不再是未知项。

该轮后续更新通知没有促使活动 DefView 重读/更新列值，且探针最终卡在 `cleanup-destroy-parent-window-1`。watchdog 的等待链为线程等待 COM、`svchost` 的 COM activation 与 `ChsIME`，没有观察到等待环；进程结束后 watchdog 确认移除了本轮临时注册和夹具。故 teardown 未通过，也不能将卡顿归因于本轮 store。此轮没有调用 `DllCanUnloadNow` 的自然 Shell 版本合同。

证据边界：自然激活仅创建并观察到一个 Shell provider（计数为 1）；没有在 Shell 创建的两个不同 provider 之间写入并读回同一哨兵快照，因此 DLL 级 store 对自然激活的跨 provider 复用仍待验证。下一步应增加无窗口、独立 provider 的 Shell 激活合同，并单独核对 COM 引用归零与 unload；不要重复依赖会卡在父窗口 teardown 的完整 view 探针。

### 2026-11-01：无 Shell view 窗口的双 provider 快照与卸载合同

新增 `--synthetic-shell-factory-snapshot-cache-private-desktop`：经临时注册的 Shell namespace PIDL 两次分别 `BindToObject`，并要求 class factory 创建计数增加至少 2、两对象的 `IUnknown` identity 不同。第一 provider 将 `child` 的真实文件系统身份对应快照写为 997 B 哨兵，第二 provider 用 `GetDetailsOf` 读回文本 `997`；若两次 Shell 激活各有独立 store，该断言会失败。此命令不创建 `IShellView` 或 Shell view 窗口，避开完整 view 探针此前卡住的父窗口 teardown。

连续两次运行均通过（class factory create delta=2，身份不同，第二 provider 读回哨兵）；COM 引用释放后 `DllCanUnloadNow=S_OK`，原生 DLL 已卸载，Private Desktop 前台/输入未变，watchdog 报告 `isolation-clean=True`。Native adapter `/W4 /WX` 和 NativeProbe Release 构建（0 警告/错误）通过。该合同证明临时注册下 Shell 创建的独立 provider 共享同一 DLL 快照 store，且本场景无 COM 引用时可以卸载。

边界：没有创建活动 DefView，未验证显示/排序、活动 view 更新、生产扫描器注入、容量/淘汰、长时间内存占用或 ExplorerBrowser 产品接线。完整 Shell view 探针的 teardown 阻塞仍是单独问题，不因这个无 view 合同而视为修复。

### 2026-11-01：生产递归扫描结果跨 Shell provider 复用

扩展上述无 Shell view 合同：通过项目生产 `RecursiveDirectoryScanner.ScanDirectoryTreeForPaths` 扫描夹具目录，得到 child=10 B、2 files、1 nested directory；扫描完成后把磁盘内容改为 7 B，再将扫描所得 10 B 发布到第一个 Shell 激活的 provider。另一次 class factory 激活得到不同 COM identity 的第二 provider，并从其 `GetDetailsOf` 读回 `10`。因此第二实例读到的是扫描快照，而不是扫描后已变为 7 B 的磁盘回退值。两次 watchdog 运行均观察到 factory create delta=2、读回成功、`DllCanUnloadNow=S_OK`、DLL freed 和 `isolation-clean=True`。

NativeProbe Release 构建 0 警告/错误，未创建 `IShellView` 窗口。该结果将生产扫描器的字节汇总接到自然 Shell 激活的共享快照 provider；扫描结果按路径送入，文件系统身份由 adapter 从 Shell 行重新解析并校验。本轮不测试生产异步调度/取消/重扫发布、活动 DefView 更新、缓存限额/长期内存、ExplorerBrowser 产品接线或可见列。

### 2026-11-01：活动 DefView 更新路径对照（生产扫描器输入）

将活动 view 的实时快照发布路径接到生产 `RecursiveDirectoryScanner.ScanDirectoryTreeForPaths`：先修改隔离夹具，再由扫描器得到 child=1 B，并将扫描结果发布到 Shell provider。对照两种活动 view 更新方式：

- 仅发送 `SHCNE_UPDATEITEM + SHCNE_UPDATEDIR` 后，2.5 秒观察窗口内没有触发 `EnumObjects`；自定义大小列没有被重新读取，值和排序均未更新。
- 调用整 view `IShellView.Refresh()` 后，Shell 触发一次枚举并重读两行；child 大小显示为 1 B，数值排序更新。隐藏探针从 Refresh 到值/顺序读回约 266 ms。
- 整 view 刷新会清除选区和焦点；紧接着使用 `ShellViewSelectionSnapshot` 恢复，选区、焦点均成功读回。

验证运行于隔离 Private Desktop 的 WPF 根宿主；宿主正常返回，前台/输入未变，watchdog 隔离清理通过。Shell 在宿主存活期间仍持有 DLL COM 引用（`DllCanUnloadNow=S_FALSE`），隔离进程退出后回收。

证据边界：266 ms 是隐藏探针的状态轮询时间，不是像素或感知延迟测量；不能据此判断用户可见的黑屏/闪烁。该探针使用合成 Shell provider，没有改动产品 ExplorerBrowser，也没有证明产品扫描器、焦点恢复及刷新策略已接线或完成可见 UI 验收。当前结论仅是：目录变更通知未更新该自定义列，而整 view Refresh 可更新值与排序，且必须另行恢复选区/焦点。

## 2026-10-31：扫描快照按绝对 Shell 身份注入原生列

NativeProbe 新增无窗口合同，将生产 `RecursiveDirectoryScanner` 的扫描值与 Shell 返回的 `SIGDN_DESKTOPABSOLUTEPARSING` 名称一起传入合成 `IShellFolder2`。provider 根据 backing folder 和行 PIDL 再解析一次绝对 parsing name，并要求与传入 identity 匹配后才写入实例级逐行快照表。夹具扫描时目标目录为 10 B，随后磁盘内容变为 7 B；注入 10 B 后列文本为 `10`、属性为 `VT_UI8(10)`，且 `CompareIDs` 将它排在 8 B 根文件之后。两个兄弟目录分别存入 10 B 与 12 B，只更新第一行到 6 B；同一 PIDL 的文本、属性及排序随之更新，第二行仍保持 12 B 和独立排序。随后在同一 provider 实例中重新执行 `EnumObjects`，新生成的两个行 PIDL 仍通过 absolute parsing name 命中各自最新快照。

验证：`build-static-shell-folder-adapter.ps1` 原生 Release DLL 构建成功，NativeProbe Release 构建 0 警告/错误；`dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release --no-build -- --static-shell-column-folder-smoke` 退出码 0，输出包含 `absolute-parsing-name-cache=independent`、`same-pidl-update=6B/text/property/sort-passed` 与 `re-enumerated-pidl-retained=passed`。探针没有创建 Shell view 窗口。

边界：key 已改为绝对 parsing name；重枚举合同只在同一 NativeProbe 合成 provider 实例内通过。provider 实例重建后快照是否保留仍未验证。每次取值都会重新调用 backing Shell 解析行 identity，因此这里不提供生产性能结论。没有把 `ShellListViewRowModel` 的扫描结果/生命周期直接接入 provider，也没有跨 provider 实例缓存、扫描调度/异步回写或 `SHChangeNotify`。因此这只验证 identity 对齐与同实例重枚举后的快照映射/更新，不是产品列接线，也没有验证 DefView 是否重取/保留列值、菜单/焦点/拖放或 Q-Dir 可见显示。Explorer Dirs 产品仍使用 ExplorerBrowser；逐行大小列仍是未实现的 P2 产品功能。

## 2026-10-31：隐藏 SysListView32 写回重枚举后的选区/焦点

NativeProbe 新增 `--shell-listview-selection-restore`：用真实 `SHParseDisplayName`/`IShellFolder` 枚举普通目录项目，将行投影到不可见的 Windows `SysListView32`，设置两个选中项和一个 focused item。夹具新增 `delta` 并以新的行顺序重建列表，再从控件读回选区/焦点；随后删除已选中的 `beta`，重新枚举/对账/重建，再读回控件状态。

合同通过：新增项未选中，`beta`/`gamma` 的选中状态和 `gamma` 焦点在新顺序下保留；删除 `beta` 后，剩下的 `gamma` 仍选中并聚焦，焦点没有落到其它行。父窗口不可见且前台 HWND 未变。此合同真实写入并读取了 Win32 ListView 状态，但行顺序是由夹具排序后投影的；没有创建 Shell DefView、调用 `IShellView` 或证明 Shell 自身重排/命令的选区行为。

仍需把相同状态恢复方式放到真实隔离 `IShellView` 测试中，验证原生菜单、排序、键盘、通知、PIDL 长跑和滚动语义。产品 ExplorerBrowser 保持不变。

## 2026-10-31：变更后的选区与焦点按行身份映射

隔离 `ShellListViewSelectionSnapshot` 保存选中行 ID 集合和 focused row ID，并在新枚举顺序中解析为每行的选中/焦点状态。NativeProbe 的 Shell 通知夹具现跨 create、update-directory、重命名后的逆序行快照及 delete，对未变化的 `stable.txt` 选中/焦点身份做映射；行消失时不把焦点转移给另一行。SettingsSmoke 覆盖新增、删除、重排、多选、焦点消失及旧快照隔离；隐藏 Shell 通知合同连续通过 3 轮，前台不变。

该结果仍停留在行身份与状态映射层，没有把状态写回原生 `SysListView32`，也没有在 `IShellView` 中读回/恢复实际选区、焦点、滚动或 Shell 排序。后续要用隐藏原生列表验证状态回写，再评估真实 `IShellView` 的通知/重排门槛；产品 ExplorerBrowser 不变。

## 2026-10-31：隐藏窗口 Shell 变更通知驱动对账

NativeProbe 新增 `--shell-change-reconciliation`：用不可见的 message-only HWND 注册 `SHChangeNotifyRegister`（Shell/interrupt 来源及 `SHCNRF_NewDelivery`），并通过 `SHChangeNotification_Lock/Unlock` 读取事件。夹具在注册后依次新建文件、更新目录、重命名和删除文件，各操作调用 `SHChangeNotify`；收到通知后再重新枚举并运行行模型对账。

本机合同通过：create、update-directory、rename、delete 通知均到达；未变更项目在新增/更新/重命名后保持稳定 ID；重命名项目获得新 ID；删除项目的旧 ID/PIDL 被退休；最终只剩稳定夹具行。消息窗口始终不可见，前台窗口未改变。执行覆盖事件发布与订阅、Shell 再枚举和模型 PIDL 所有权交接；没有进行 Private Bytes/句柄泄漏剖析，也没有验证交互式 Shell 操作触发的复杂事件合并/洪泛。

此探针只运行于 NativeProbe，不接入正式视图；仍需验证真实视图选区/焦点在模型重排后的恢复、排序状态及 PIDL 长跑生命周期。Explorer Dirs 产品继续使用 ExplorerBrowser。

## 2026-10-31：重新枚举后的行身份对账

隔离 `ShellListViewRowModel.Reconcile` 现在可把一次新的 Shell 枚举结果与旧快照对账：按 Shell 绝对解析名匹配身份，保留匹配行的稳定 ID 与递归扫描摘要，同时采用新枚举的显示名、PIDL、图标索引和顺序；新增行分配递增 ID。旧快照中的 PIDL 都作为 `RetiredRows` 返回，由调用者在新快照接管后释放。

SettingsSmoke 先因 `Reconcile` 尚不存在而按预期编译失败；实现后定向扫描 Smoke 通过，覆盖元数据更新、重排、添加、删除、PIDL 退休集合和摘要保留。

此处只证明纯行模型的快照对账合同；没有订阅或接收 `SHChangeNotify`，也没有接入真实 Shell view、列表选区/焦点恢复或产品 UI。下一步在隐藏窗口上验证真实 Shell 变更通知到达后触发枚举/对账，并检查 PIDL 清理及行身份；产品 ExplorerBrowser 不变。

## 2026-10-31：扫描结果进入稳定 Shell 行模型

隔离 `ShellListViewRowModel` 现把递归扫描结果按规范化文件系统路径映射回现有 Shell 行，并为行保存大小、文件/目录/不可读计数快照。更新返回新的行集合，但保留行顺序、稳定 ID、绝对/相对 PIDL 和系统图标索引；没有扫描结果的行与没有文件系统路径的虚拟 Shell 行保持原值。定向 SettingsSmoke 先因模型不存在而构建失败，增加实现后通过，并覆盖旧快照不被修改。

`CommonListViewColorBaseline` 的隐藏相对大小合同现在从该行模型读取初始及更新后的大小/计数，再发布到 `SysListView32`。后台扫描、owner-thread 发布、FileSystemWatcher 触发、比例重算、升降序和选区/焦点身份的既有合同继续通过。完整 Release SettingsSmoke、NativeProbe 和 App Release 构建均通过，均为 0 警告/错误；隐藏列表不可见，前台 HWND 不变。

该项把扫描 ticket 的有效结果与行数据发布连起来，但仍仅覆盖两个临时文件夹的隔离 ListView；触发源仍是原型自己的 FileSystemWatcher，不是 `SHChangeNotify` 或 Shell provider 通知。产品 ExplorerBrowser、Shell 菜单/拖放/虚拟项目行操作、可见绘制和 Q-Dir 互操作均未改变或验证。下一门槛是在隔离原型中用 Shell 通知更新、新增和移除行，同时维持 PIDL 生命周期、排序、选区/焦点，再重新审视真实 Shell 操作覆盖；不据此批准产品迁移。

## 2026-10-31：虚拟 Shell 行项目接口探测

扩展隐藏的 `--common-listview-virtual-folder-contract`：从 Control Panel 虚拟根枚举出的 9 个项目，逐项通过父 `IShellFolder::GetUIObjectOf` 请求 `IContextMenu` 与 `IDataObject`。本机结果为 `virtual-item-context-menus=9`、`virtual-item-data-objects=9`；9/9 个项目的 `IDataObject.GetData("Shell IDList Array")` 均返回单项 CIDA，且父子 PIDL 组合后的 Shell parsing name 与原行身份一致。9 个虚拟文件夹均可绑定，合计枚举 72 个下一层项目。

同一虚拟根的 `IShellFolder::CreateViewObject(IContextMenu)` 查询成功，但 `QueryContextMenu` 返回 0 个菜单项。它不能提供该位置的 Explorer 视图级查看/排序/刷新菜单；视图级菜单仍须由 `IShellView` 提供。探针只查询并销毁内存菜单，不显示菜单或调用命令。

NativeProbe Release build 0 警告/错误，虚拟文件夹合同通过；窗口保持隐藏且前台未变化。结果证明这些 Control Panel 项目提供了项目级菜单与可往返的 CIDA 数据对象；不证明原生菜单可见、命令可执行或真实拖放/剪贴板体验正确，也不代表所有 Shell namespace 均提供这些接口。产品仍使用 ExplorerBrowser。

### 2026-10-31：Control Panel 虚拟位置的真实 IShellView

将 Control Panel 加入既有 Private Desktop 单目标探针后，`BrowseObject → SHBindToObject → CreateViewObject(IID_IShellView) → CreateViewWindow` 成功。该 view 的 `GetItemObject(SVGIO_BACKGROUND, IID_IContextMenu)` 和内存 `QueryContextMenu` 成功，读出 8 个顶层菜单项：查看、排序方式、分组依据、刷新、粘贴、撤消删除等；没有显示菜单或调用命令。`IID_IDropTarget` 查询返回 `S_OK`。

此 provider 的 `IFolderView2::ItemCount(SVGIO_ALLVIEW)` 返回 `E_FAIL`，其 view HWND 下也没有 `SysListView32` 子控件。因此“能够创建真实 Shell view 并取得原生菜单”不等于“可由统一的 ListView 行模型接管并保留所有 Explorer 能力”。菜单内容是本机此位置的观测，未验证可见菜单、启用状态、命令行为、焦点、拖放或其它 Windows 版本/provider。Private Desktop watchdog 退出码 0，前台句柄和输入时间戳未变化，临时探针资源清理完成；产品仍使用 ExplorerBrowser。

## 2026-10-31：隔离扫描的异步发布与文件变化触发

目录树扫描协调器新增 `RunLatestAsync`：扫描在后台线程运行，完成后通过 owner `SynchronizationContext` 发布；发布前再次核对 ticket，避免旧扫描结果覆盖新访问或更新。隐藏 ListView 合同将递归数据扫描迁出 STA，并通过真实临时目录中的 `FileSystemWatcher` 修改文件来触发下一轮扫描和结果发布，不再由测试代码手动重扫。合同断言事件已观察、结果已应用、扫描线程与 owner 分离、更新在原 owner 线程发布，同时检查递归文件/子目录数、相对大小和升降序、行身份、焦点/选区及前台窗口不变。`DirectoryTreeScanCoordinatorSmoke` 的最新结果优先发布、旧结果不发布合同、递归扫描 smoke、NativeProbe/App Release build 与隐藏 `--common-listview-relative-size-contract` 均通过；探针输出 `file-change-observed=True`、`update-applied=True`、`visible=False`、`foreground-unchanged=True`。

这是 NativeProbe 内部的异步数据流验证，不是产品实时列或 Shell 通知集成：更新触发来自原型自己的 `FileSystemWatcher`，没有接入 ExplorerBrowser/IShellView 产品宿主，也没有验证真实文件视图刷新、跨窗格冲突、网络路径延迟、性能/内存收益或 GUI 表现。产品仍使用 ExplorerBrowser，逐行大小列仍未接入。

同批又补充了一个可控的队列时序回归：扫描已完成并将发布回调排入 owner context 后，先开始新一代扫描，再实际派发旧回调。旧请求返回未发布、回调未触碰模型且新 ticket 保持有效；递归扫描协调器 smoke 连续 5 轮通过。该项验证代际门控的窄竞态，不代表真实繁忙 UI Dispatcher 下的延迟上限。

## 2026-10-30：目录树扫描器托管内存 A/B

为量化隔离 ListView 逐行派生值扫描的内存开销，增加 `--recursive-directory-scan-allocation-probe`。探针创建 5,000 个空子目录，预热完整/指定路径两条扫描路径，然后测量 `GC.GetAllocatedBytesForCurrentThread`、强制 GC 后的存活堆估算及扫描期间分批强制 GC 采样的最大存活堆。扫描器同时改为惰性深度优先枚举器帧：父目录枚举暂停在当前子目录，子树完成后即累计并关闭；取消时 finally 释放全部活动帧，不再预先把同级所有子目录节点压入待处理栈。待处理枚举器/打开句柄数量因此随当前目录深度增长，而非随一个目录的全部子项数增长。完整扫描返回 5,001 项；指定路径扫描仅返回根目录和 3 个子目录共 4 项。最终托管分配为 4,052,080 B / 3,361,544 B，差 690,536 B（17.0%）；强制 GC 后存活堆估算为 1,755,912 B / 1,288 B；遍历中采样的峰值估算为 1,822,952 B / 69,064 B。最新复验单轮耗时为 741 / 491 ms，仍不作扫描速度结论。

`dotnet run --project src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj -c Release -- --recursive-directory-scan-allocation-probe` 可重跑该诊断。采样值只覆盖托管堆，不包括 Windows Shell、文件系统缓存、COM/原生堆，也不是 Private Bytes/Working Set；夹具主要是空目录且探针不创建 ExplorerBrowser，因此不能外推到实际目录或产品内存节省。递归扫描 smoke 另外核对扫描中取消可及时退出，且取消后 fixture 可立即删除（枚举句柄已释放）。App Release build 为 0 警告/错误；递归扫描 smoke、完整 Release SettingsSmoke 和隐藏 `--common-listview-relative-size-contract` 均通过，前台未变化。产品仍未接入递归逐行列。

## 2026-10-30：自定义列可见性合同复核

Microsoft 文档将 `IShellFolder2::GetDefaultColumnState` 的 `SHCOLSTATE_ONBYDEFAULT` 定义为 Windows Explorer Details view 默认应显示该列；它没有说明 Shell 通知后必须继续显示，也没有承诺 ExplorerBrowser 或 DefView 会把未注册属性键作为持久可见列。[GetDefaultColumnState](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdefaultcolumnstate) `GetDetailsOf(nullptr, iColumn)` 提供文件夹列名及同一列索引的数据，但它本身不保存用户当前列集合。[GetDetailsOf](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelldetails-getdetailsof)

`IColumnManager::SetColumns` 的合同是设置该视图要显示的 PROPERTYKEY 顺序，并会清除全部列状态，需随后逐列调用 `SetColumnInfo`。[SetColumns](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icolumnmanager-setcolumns) 本项目隔离探针观察到：新建视图初始可按 `GetDetailsOf` 显示并排序 provider 自定义递归大小列；通知后 DefView 不再读取其值，当前 `IColumnManager.Capture` 集合也不含自定义 key。通知后仅将 Name 与自定义 key 传给 `ShellColumnManager.Apply` 返回 `S_OK`，但读取到的大小列索引仍为 `-1`，provider 对该列的 `GetDetailsOf` / `GetDetailsEx` 请求数仍为 0。因此 `S_OK` 不能作为自定义列已恢复的证据；现有数据尚不足以把原因定为某个单一 Shell 回调或属性类型问题。

据此停止重复尝试通用 `IColumnManager.Capture/Apply` 和标准行重绘消息。若继续该方向，下一项实验必须记录视图初始化前后 `GetDetailsOf(NULL, iColumn)`、`GetDefaultColumnState`、`MapColumnToSCID` 与 `IColumnManager.GetColumns(CM_ENUM_ALL)` 的调用/返回值，并从无通知的新视图检查列键来源；没有可证明受支持的可见列持久化机制之前，不接入产品。2026-10-30 只读环境检查仍发现两个已知测试 CLSID `{4864fed2-6004-4e3c-9e07-d3f10f560a2c}` 与 `{a17d2a61-4c0b-4b22-8e36-35d92cbc1170}` 同时存在于 HKCU CLSID 和 Desktop `NameSpace`；此前清理操作遭执行策略拒绝，本轮没有重试删除，也不新增注册型探针。产品仍使用 ExplorerBrowser。

## 2026-10-30：视图架构与派生列可行性复核

本次把隔离 `IShellView` 结果与标准 Win32 `SysListView32` 控件合同合并评估，目的仅是判断下一步架构，不改变产品当前的 ExplorerBrowser。

| 路线 | 派生列更新/排序 | Shell 体验保留 | 本项目成本与证据 |
| --- | --- | --- | --- |
| ExplorerBrowser / 系统 DefView | 可使用文件夹已有列；当前没有受支持的任意派生列接入及逐项重取路径 | 最强，继续由 Windows Shell 提供查看、菜单、导航和文件操作 | 改动最小；现有逐行大小探针在刷新后读回错误/缺失的值，不能满足该列 |
| 自建 Shell namespace + `IShellView` / DefView | 初次可由 `IShellFolder2` 提供值并通过 `CompareIDs` 排序；显式恢复列和通知后，当前 DefView 没有重新请求派生值 | 可复用 DefView 的原生菜单与不少 Shell 命令，但 namespace/PIDL、通知、拖放等仍需适配 | 已有隔离文件夹适配器和操作合同；有效刷新基线仍失败，不能作为产品替代视图 |
| 自有 `SysListView32` 数据模型 | 已验证 `IShellFolder::EnumObjects` 子 PIDL、row ID 和系统图标索引可映射；派生值更新、排序及选中/焦点身份保持通过 | `IShellFolder` 可提供项目菜单及文件夹背景菜单，但不含当前 Shell view 的查看/排序/分组/刷新命令；还需接入文件操作、拖放、通知、导航及键盘路由 | 隐藏探针已挂载 Shell 共享小图标列表并取得两类 folder context menu；尚无完整 Explorer 视图菜单、可见绘制、虚拟位置、性能/内存或完整输入验收，产品化工作量高 |

**结论：**逐行派生值的技术控制权要求应用持有行数据模型；继续调整 DefView 通知不是有证据的路线。隔离原型已从真实 Shell 文件夹枚举子 PIDL 并放入普通 `SysListView32` 行模型；下一阶段仍需验证可见详细视图/图标、视图级原生菜单、复制/移动/删除、重命名、拖放、外部变更刷新、焦点/选区及目录导航。成功门槛应同时包括真实文件 Shell 行能更新派生值并保持排序/焦点，以及 Shell 操作合同不退化；在此之前不迁移产品，不以合成控件的内存或绘制结果推断实际收益。

### 2026-10-30：Microsoft 列接口契约复核

`IShellFolder2::GetDetailsOf` 以当前文件夹自己的零起始列号返回标题或单项显示文本；传入空 PIDL 时取标题，文件系统文件夹前四列有标准约定，其它列集合和索引仍由文件夹决定。`MapColumnToSCID` 只负责把列号映射到属性集 ID/属性 ID，`GetDetailsEx` 则允许按该属性键取值。参考 [GetDetailsOf](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdetailsof)、[MapColumnToSCID](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-mapcolumntoscid) 和 [IShellFolder2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellfolder2)。

当前隔离适配器已让列标题、列号、`MapColumnToSCID` 与初始 `GetDetailsEx` 数值保持一致，列头排序时 `CompareIDs` 也读取递归值。有效 `SHChangeNotify` 运行中 DefView 会重枚举，却没有再次请求该自定义列，IColumnManager 捕获列表也不含该属性键。Microsoft 的这些接口页面没有承诺 DefView 会在刷新后保留任意自定义列或重读派生值。因此尚不能把问题归因于单纯的属性键映射错误，也不能据此推断所有 Shell namespace 都不支持自定义列；现阶段缺的是可验证的视图列状态/刷新模型。产品继续使用 ExplorerBrowser，后续若继续实验，需先围绕新建视图恢复列状态做单变量验证，再决定是否停止 DefView 路线并转向完整自有行模型。

**资源判断：**现有证据不足以声称自有 ListView 能降低内存。ExplorerBrowser 仍会在应用中运行，若只替换控件但继续并行保留旧 Shell hosts，短期反而可能增加常驻对象。内存必须在同机、同目录集、同查看模式、同窗格数与相同操作序列下测量，并分别比较启动稳定态和长时间刷新后的 private bytes/working set；测量之前不把迁移收益列为理由。

## 2026-10-30：Shell 项目身份与自有 ListView 行模型合同

隐藏数据合同使用真实临时文件夹，通过 `SHParseDisplayName` 获取绝对 PIDL，并以 Shell 显示名和 `SIGDN_FILESYSPATH` 解析名核对路径往返。每条 ListView 行以稳定 row ID 关联 Shell PIDL、解析名和递归大小；更新派生值后只改变行数据，不替换 PIDL/行 ID，再按数值排序。探针确认选中且聚焦的 `small-folder` 排至首行后，selected/focused 状态仍跟随 row ID=2，两个 Shell PIDL 身份保持不变。命令 `dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release --no-build -- --common-listview-data-contract` 通过；窗口不可见，前台窗口未变，临时夹具已清理。

此结果把验证推进到“真实文件系统 Shell 枚举项目可成为自有 ListView 行模型”，但只覆盖两个临时文件夹；没有创建 Shell view 或验证虚拟位置、图标、菜单/命令、文件操作、拖放、通知、导航、键盘、全视图模式及内存/性能。产品仍使用 ExplorerBrowser，迁移门槛不变。

## 2026-10-30：Shell 枚举驱动 ListView 行合同

前一条只把单独解析的 PIDL 映射到手工创建的 ListView 行。本轮改为解析并绑定真实临时父文件夹，通过 `IShellFolder::EnumObjects(SHCONTF_FOLDERS | SHCONTF_NONFOLDERS)` 和 `IEnumIDList::Next` 取得子项相对 PIDL，再与父 PIDL 组合为每行持有的绝对 PIDL；显示名、路径和 row ID 均从这批枚举项构造。夹具仅包含两个文件夹，名称及预置递归字节数用于验证行排序更新。

`--common-listview-data-contract` 的新断言先以 `shell-enumeration-backed=False` 失败；接入枚举后，NativeProbe Release 构建 0 警告/错误，隐藏合同通过，输出 `shell-enumeration-backed=True`、`shell-pidl-identities-preserved=True`、`selected-focused-after-sort=small-folder`、`visible=False`、`foreground-unchanged=True`。枚举异常路径会释放已构造的行 PIDL 和 enumerator，测试结束清理 parent PIDL、文件夹接口及夹具。

该结果证明 Shell 的真实文件夹枚举输出可驱动自有 ListView 的行身份；它没有验证系统图标绘制、列元数据生产、Shell 菜单/命令、文件操作、拖放、变化通知、导航、键盘、性能/内存或产品行为。未创建 `IShellView`，Explorer Dirs 产品仍使用 ExplorerBrowser。

## 2026-10-31：虚拟 Shell 项目进入自有 ListView 行模型

新增 `--common-listview-virtual-folder-contract`，从控制面板的绝对 Shell 解析名绑定父 `IShellFolder`，枚举其 9 个虚拟子项并读取每项的 normal display name、desktop-absolute parsing name、`SFGAO_FOLDER`/`SFGAO_FILESYSTEM` 属性及 `SHGFI_PIDL` 系统图标索引。每项以绝对/相对 PIDL 保持身份，并写入隐藏标准 `SysListView32`；逐行读回名称、解析名、row ID 与图标索引。Release NativeProbe build 0 警告/错误，合同通过：`root-is-virtual=True`、`children=9`、`filesystem-children=0`、`pidl-row-identity=True`、`hidden=True`、`foreground-unchanged=True`。诊断只输出计数，不输出控制面板项目名称或解析路径。

普通文件系统合同证明了扫描值和 `IFileOperation`，该合同补充了纯虚拟 Shell 命名空间的行身份、名称及图标索引映射；在 2026-10-31 统一行模型前，两类合同使用不同记录。仍没有验证虚拟项目操作/拖放、菜单命令、属性列、键盘焦点、可见绘制或真实 ExplorerBrowser；没有改产品视图，继续使用 ExplorerBrowser。此前在“此电脑”下只观察到 3 个文件系统驱动器项目，不能作为虚拟行支持证据；控制面板夹具才提供了 9 个 `SFGAO_FILESYSTEM` 未设置的子项。

## 2026-10-31：文件系统与虚拟 Shell 共用行数据模型

新增 NativeProbe 内部 `ShellListViewRow`，让普通文件系统和虚拟 Shell 两个合同使用相同的行字段：稳定 row ID、绝对/相对 PIDL、显示名、绝对解析名、可空文件系统路径、Shell 属性、可空递归字节数、系统图标索引及枚举来源。文件系统合同确认真实枚举行有路径、`SFGAO_FILESYSTEM` 和递归大小；控制面板虚拟合同逐项核对属性与路径是否一致，并要求递归大小为空。两种合同分别读回 PIDL/row ID、解析名和系统图标映射。

NativeProbe Release build 为 0 警告/错误；`--common-listview-data-contract`、`--common-listview-virtual-folder-contract` 和 `--common-listview-relative-size-contract` 均通过，窗口不可见且前台未变。相对大小合同仍通过异步更新、排序和选区/焦点断言。该步把两类数据形状合并到同一原型模型，但没有统一两种枚举、属性读取或操作管线；没有验证系统图标实际绘制、虚拟项目操作/拖放/菜单/键盘、产品 Shell 通知或 GUI。Explorer Dirs 产品仍使用 ExplorerBrowser。

## 2026-10-31：文件系统与虚拟 Shell 共用枚举入口

新增 `ShellListViewEnumerator`，统一 `IEnumIDList` 遍历、父子 PIDL 组合、共享行身份读取、绝对/相对 PIDL 保留及失败清理。两类调用方共用该入口；普通文件系统合同通过回调关联夹具递归大小并在枚举后排序，虚拟控制面板合同保留 Shell 的原始枚举顺序且不提供大小值。

NativeProbe Release build 为 0 警告/错误；普通文件系统数据/操作合同、9 项虚拟控制面板合同和异步相对大小更新合同通过，前台未变。该步统一了隔离原型的枚举/行创建入口，但未统一后续文件操作、通知、绘制、交互，也未接入产品 ExplorerBrowser；PIDL 所有权通过合同正常结束路径验证，异常注入下的逐分支故障测试及可见 GUI 未覆盖。

## 2026-10-31：虚拟 Shell 文件夹的下一层绑定与枚举

扩展 `--common-listview-virtual-folder-contract`：对 Control Panel 枚举出的每个非文件系统项目，依据 Shell 属性确认其为文件夹后，用其绝对 PIDL 绑定子 `IShellFolder`，再调用共用 `ShellListViewEnumerator` 读取下一层行，最后释放子行 PIDL 与 folder 接口。当前机器的 9 个虚拟项目全部可绑定，共枚举出 72 个下一层项目。

NativeProbe Release build 0 警告/错误，扩展后的虚拟合同通过；9/9 子文件夹绑定、行身份、共享模型、隐藏窗口及前台不变均满足。它证明这组 Control Panel 虚拟文件夹可沿 PIDL 继续导航并复用同一枚举入口；不代表所有 Shell provider 均可绑定，也没有验证逐项打开、项目操作、菜单、拖放、通知或产品交互。Explorer Dirs 产品仍使用 ExplorerBrowser。

## 2026-10-30：Shell 系统图标索引接入 ListView 合同

在 Shell 枚举行创建期间，对每个实际解析路径调用 `SHGetFileInfo(SHGFI_SYSICONINDEX | SHGFI_SMALLICON)`，记录共享系统图像列表句柄和行图标索引。创建隐藏 `SysListView32` 后通过 `LVM_SETIMAGELIST` 挂载 Shell 返回的共享小图标列表；插入每行时设置 `LVIF_IMAGE`，再以 `LVM_GETITEM` 验证图标索引与 row ID 对应，并在派生值排序后再次核对映射。

新图标合同先因 `shell-icons-available=False` 失败；接线后 Release build 与 `--common-listview-data-contract` 通过，输出 `system-image-list-attached=True`、`row-icons-bound=True`、`sorted-row-icons-bound=True`。测试没有可见窗口或像素采样，因此只证明索引查询、共享列表挂载和控件数据读回，不证明图标实际绘制外观、DPI/主题效果、Shell overlay 或产品体验。Explorer Dirs 产品未改。

## 2026-10-30：Shell 项目菜单与视图菜单边界

用枚举得到的子 PIDL 经 `IShellFolder::GetUIObjectOf(IID_IContextMenu)` 查询项目菜单，并经父 `IShellFolder::CreateViewObject(IID_IContextMenu)` 查询文件夹背景菜单；两者都返回菜单命令，查询期间没有显示菜单、执行命令或改变前台窗口。探针递归读取全部 `HMENU` 子菜单，确认确实遍历了 TortoiseGit/SVN 子菜单；当前机器的 folder 背景菜单根项包含扩展项、新建、属性，完整菜单树仍没有查看、排序、分组、刷新。菜单命令数分别为 154 和 115，数量包含已安装的扩展，不能按数量推断 Windows 自带命令数。

源码对照确认，产品从 `ExplorerBrowser.GetCurrentView` 取得当前 `IShellView`，再通过 `IShellView::GetItemObject(SVGIO_BACKGROUND/SVGIO_SELECTION, IID_IContextMenu)` 获取以当前视图为上下文的菜单对象；这与直接从 `IShellFolder` 查询的对象不同。故本探针只证明项目/文件夹 context menu 可按 PIDL 查询，不证明自有 ListView 能复用完整 Explorer 文件区菜单。若继续实验自有视图，必须单独证明视图级菜单命令能正确作用于它自己的查看、排序、分组状态；否则保留 ExplorerBrowser 是维持该项体验的已知路径。未修改产品或 GUI。

多选扩展本轮已取得局部行为结果：隐藏 ListView 以两个已选行对应的相对 PIDL 请求父 `IShellFolder::GetUIObjectOf(IID_IDataObject)`，`IFileOperation::CopyItems` 两项目标载荷都与来源逐字节相同；其中一个副本后续 Move/Rename/Delete 也均未中止且内容/路径断言通过。整条 NativeProbe 运行退出码为 1，因为窗口前台守卫观察到 HWND 从 `0x861542` 变为 `0x530EDC`；运行后只读查询将当前前台识别为 TortoiseGit 提交对话框，但无法判断切换是否由探针或同时发生的桌面活动引起。因此本轮不计为完整隐藏合同通过，也不把前台变化归因于实现。Release 构建为 0 警告/错误；未重跑该可能与共享桌面冲突的探针。此局部结果只验证普通隐藏 ListView 组装的双选 Shell 数据对象复制，不证明真实 `IShellView` 选区、系统剪贴板、Shell 通知后视图刷新、OLE 拖放或完整菜单操作合同。

## 2026-10-30：显式重放自定义列的刷新基线复核

此前更新 A/B 用 `IColumnManager.Capture` 的返回值重放刷新前列集合；该集合从未包含合成 namespace 的递归大小键，因此通知后的“无列值”不能单独证明通知本身没有刷新能力。本轮改用固定的 Name / Recursive Size / Type 三列，经 `SetColumns` 与 `SetColumnInfo` 显式重放自定义键，并在通知前检查基线。

初始视图仍显示 `child=10`、`small.bin=4`，且原生列标题排序基线通过。`IShellView::Refresh` 后，显式 `ShellColumnManager.Apply` 返回成功，随后 `IColumnManager.Capture` 也返回成功并读回递归大小属性键；但 DefView 的实际行文本没有恢复为 `10/4`，列 1 读到 `1200/4`，无法在正确值基线上继续通知 A/B。探针现在检测到这一状态后会明确输出 `notification-skipped=True`，不再把缺少有效列值的后续结果误当作通知比较。

WPF `HwndSource` Private Desktop 探针退出码 0，桌面前台 HWND 和最后输入时间未变，临时 CLSID、Desktop namespace 注册及夹具清理完成。退出码只表示探针完整结束；“显式列恢复”这一行为断言失败，所以本轮没有通过该恢复路线，也没有验证通知能否刷新一个正确重建的自定义列。早期以标准列快照为恢复基线的通知结果需按无有效列值基线解读。产品仍使用 ExplorerBrowser，未接入该原型。日志：`src/.state/native-probe-watchdog/20261030-093242-358ea2af.stdout.log`。

## 2026-10-30：WPF 根宿主下递归大小列更新 A/B

在隔离 Private Desktop 中通过受保护的临时 Desktop `NameSpace` 注册解析到真实文件系统夹具的 namespace；自有 adapter 使用 `IShellFolder2::CompareIDs` 提供数值比较，并由 WPF `HwndSource` 承载 `IShellView`。视图初次建立时，递归目录 `child` 显示大小 `10`，文件 `small.bin` 显示大小 `4`；升序和降序都按数值正确重排。前台 HWND 与最后输入时间未变。

改变目录内容后依次比较 `SHCNE_UPDATEITEM`、单项加父目录 `SHCNE_UPDATEDIR`、`IShellView::Refresh` 三种更新。三种策略的 Shell 列快照都不含递归大小属性列，刷新期间没有新的逐项属性读取；均无法读回递归列值。前两种策略在该夹具下保留选区/焦点，但整视图刷新后选区/焦点状态不再匹配。整视图刷新结算约 2.64 秒，慢于通知策略且未恢复派生列。WPF 根宿主下本次没有复现 `CreateViewWindow` 或父窗口销毁超时，Private Desktop watchdog 正常退出；adapter DLL 因仍有 Shell 引用而保留至一次性探针进程退出。watchdog 确认临时 CLSID、Desktop namespace 注册、夹具目录和子进程均已清理，没有生成超时 dump。

本轮证明 WPF 根宿主使这个合成 namespace 视图能够完成排序与 A/B 生命周期，但没有解决 DefView 在更新后丢弃自定义列及其逐项值的问题；整视图刷新还带来约 2.64 秒延迟与选区/焦点损失。因此不进入 ExplorerBrowser 产品集成，也不继续叠加这三种通知技巧。日志：`src/.state/native-probe-watchdog/20261030-051635-7c532fdf.stdout.log`，stderr 为空。该结果只适用于本机隔离 adapter / DefView，不代表 ExplorerBrowser 产品菜单或视图行为。

## 结论（2026-10-25）

当前文件列表是 ExplorerBrowser 提供的原生 Shell 视图，列通过 `IColumnManager` 读取和应用；该接口只能选择当前 Shell 文件夹已经支持的属性列，不能向现有视图注册任意新列。`IColumnProvider` 已是 Vista 前的旧方案，不能作为当前 Windows 的扩展路线。

微软仍实现 `IFolderView2::SetViewProperty`，可为视图内的单个 PIDL 缓存属性，属性只显示在视图中，不写回文件。但该 API 自 Windows 7 起已弃用，文档明确表示未来可能移除，且搜索结果和库视图不支持。理论上可尝试给文件系统文件夹注入系统 `System.Size` 值，以复用现有“大小”列；这不等于可保证 ExplorerBrowser 接受该值，也不适用于所有 Shell 位置。

`System.Size` 的属性架构将其标为可见列，类型为 `UInt64`。`System.FileCount` 虽是 `UInt64`，但被系统架构标记为不可见列，因此不能据此得到可添加的“子项数量”列。由 `SetViewProperty` 缓存任意自定义属性、再让原生视图显示新列，目前没有受支持的接口证据。

## 项目当前能力

- F9/工具栏/工具菜单：按需递归计算当前文件系统目录的字节、文件数与子目录数，在状态栏汇总；后台执行且可取消。
- 选区大小：汇总 Shell 可提供大小的已选文件，不递归目录。
- 原生列：保留 ExplorerBrowser 支持的标准列，并逐标签保存列集合和宽度。
- 缺少：每行的文件夹递归大小列、每行子项计数列，以及这些派生值的排序/百分比显示。

## 实现边界与验证要求

1. 不把 `SetViewProperty` 作为已支持功能或默认依赖。隐藏探针已显示它无法满足当前大小排序与显式刷新语义；可见桌面验证只回答 DirectUI 是否绘制缓存值，不能弥补这两个缺口。
2. 不为这条 API 路线继续实现异步大小计算或产品级缓存，因为 Shell 大小排序忽略注入值，显式刷新会清空缓存。只有找到能同步满足显示、排序、刷新和 Shell STA 响应要求的受支持机制后，再进入实现评估。
3. 子项计数列不能从 `System.FileCount` 直接推出。若坚持逐行显示，需另行评估自定义视图宿主/列提供者；这会触及当前保留原生 Explorer 交互的核心约束，进入设计前应先有可见原型和兼容性证据。
4. 本轮没有启动可见 GUI；隐藏 ExplorerBrowser 的缓存往返 spike 不等于产品可见列验证，也不证明用户可见的 ExplorerBrowser 行为。

## 替代实现路线复核

- 继续使用现有 ExplorerBrowser / Shell DefView：没有找到受支持的接口可向系统原生视图注册任意的递归文件夹大小列。`IColumnProvider` 虽然曾用于增加 Details 列，但微软明确说明 Vista 起已移除支持；传统 column handler 还要求注册 COM DLL 与 Shell handler。
- 通过 Windows Property System 的 property handler 提供值：property handler 需与文件扩展名关联并注册。它适用于特定文件类型的属性读写，不是本程序可便携地为任意现有文件夹附加派生属性的入口；对所有目录类型注册也会引入系统范围 Shell 扩展部署与稳定性风险。
- 编写自己的 Shell namespace / `IShellView`：接口允许 namespace extension 自行实现视图，但这会放弃现有系统 DefView，必须重新满足文件管理器依赖的列、菜单、快捷键、选择、拖放和通知行为，范围远超增加一列。
- 本程序自行绘制文件列表：最容易计算并排序递归大小，但会切断用户已经要求保留的 Explorer 原生菜单、Shell 命令和键盘交互，当前不符合项目主架构。

因此，现阶段保留 F9/状态栏递归目录汇总；逐行递归大小列作为架构探索项，不再尝试把弃用缓存接口包装成完整功能。若后续确实要逐行显示，需要先设计一个能保持原生 Shell 交互的整体视图架构，确认其菜单、操作和性能范围后再进入实现计划。依据：[Microsoft `IColumnProvider`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj/nn-shlobj-icolumnprovider)、[注册和分发 Property Handler](https://learn.microsoft.com/en-us/windows/win32/properties/prophand-reg-dist)、[Shell 接口清单](https://learn.microsoft.com/en-us/windows/win32/shell/interfaces)、[Shell 扩展稳定性说明](https://learn.microsoft.com/en-us/windows/win32/shell/shell-exts)。

### 2026-10-25 隐藏 ExplorerBrowser 属性缓存 spike

`NativeProbe --view-data-capability` 在隔离普通文件夹的隐藏 ExplorerBrowser 详细视图中，将当前视图已有的 `System.Size` 列设为可见，然后对 `child` 的视图子 PIDL 调用 `IFolderView2::SetViewProperty` 写入 `VT_UI8=987654321`。Shell 返回成功，`GetViewProperty` 读回相同类型和值；紧接着重新读取完整视图快照时，`IShellItem2` 的快速属性存储仍报告目录大小 unknown，且条目集合、排序、列和导出值保持原样。父窗口未显示且前台句柄未变。

这确认当前 Windows 隐藏 ExplorerBrowser 的逐项属性缓存可往返，并且本次没有写入底层项目；它不证明 DirectUI 文件视图会把该值绘制到“大小”列或适用于所有 Shell 位置。后续排序探针已发现 Shell 原生大小排序忽略该缓存值，显式刷新也会清除它。API 仍是微软标记的弃用接口，因此该路线目前不满足完整文件视图排序/刷新行为，不能据此接入产品。

### 2026-10-25 显式刷新会清除视图属性缓存

新增独立命令 `NativeProbe --view-property-refresh-capability`，在新建的隐藏 ExplorerBrowser 和独立测试目录中先写入并读回同一 `System.Size=987654321` 值，随后调用 `IShellView::Refresh()`。刷新后重新枚举确认仍是原来的 `child`、`five.txt`、`three.txt` 三项，再读 `child` 的 `System.Size` 得到 `VT_EMPTY`。另在刷新前记录大小降序为 `five.txt|three.txt|child`；写入 `987654321` 并从名称排序切回大小降序后，顺序仍相同，而 `GetViewProperty` 仍读回注入值，说明本机 Shell 排序没有采用此缓存值。探针随后在同一隔离目录执行 `Directory.Move(child, child-renamed)`，但隐藏视图在 1 秒消息泵后仍只枚举旧名称，因此不能据此判断改名后的缓存行为；自然 Shell 更新和可见列绘制仍未验证。

## 资料

- [Microsoft: IFolderView2::SetViewProperty](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setviewproperty)
- [Microsoft: System.Size](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-size)
- [Microsoft: System.FileCount](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-filecount)
- [Microsoft: IFolderView2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2)
- [Microsoft: IColumnManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icolumnmanager)
- [Microsoft: IColumnProvider](https://learn.microsoft.com/en-us/windows/win32/api/shlobj/nn-shobj-icolumnprovider)

## Q-Dir 已公开的列行为补充（2026-10-25）

复查 Q-Dir 官方 [FAQ 63](https://www.softwareok.com/?faq=63&seite=faq-Q-DIR) 和[功能说明](https://www.softwareok.com/?seite=Freeware/Q-Dir/File_and_folder_size_Column_with_percentage) 后确认，逐行信息不止一个递归大小字段：文件大小继续显示在 Size 列，文件夹总大小复用 Size 列；文件/目录数量（包括子目录中的项目）复用 Type 列。官方选项还包括单位/小数位/千位分隔、百分比、目录/文件数量、无焦点时暂停计算，以及是否忽略文件夹大小参与排序。Q-Dir 明确指出，可选择忽略文件夹大小排序以避免导航性能问题；因此排序忽略是可选策略，不是所有模式下的固定行为。

### 2026-10-28 FAQ 82 控制入口与百分比说明

重新打开 Q-Dir 官方 [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 核对当前文字：逐行 Size 信息可以关闭/开启，入口写作 Extras → Size information 或 F9；文件及文件夹大小的下划线百分比被说明为相对于同一目录中文件和文件夹总量。据此推断分母范围是当前目录的文件/文件夹总体，而非磁盘容量；文件/目录是否确实共用一个分母仍应以可见互操作确认。该页未定义隐藏项、失败扫描项目如何参与及舍入方式；递归文件夹在父目录总量中的统计和具体显示仍需可见互操作核实。

Explorer Dirs 的 F9 已由用户选定为显式递归汇总命令，输出整棵目录子树的状态栏统计；保留该行为，不将其静默改为 Q-Dir 的逐行信息开关。该记录确认两者当前命令语义不同；逐行功能仍须先通过新的 Shell 视图架构与性能门槛，不改变 ExplorerBrowser 产品路径。

本项目现有 F9 只汇总当前目录整棵子树的总字节数、文件数和子目录数到状态栏，**不等同于 Q-Dir 的逐行列和 F9 数据大小信息**，也不提供每个可见子目录的大小/数量、百分比或排序设置。Q-Dir 官方 [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 已明确百分比相对同一目录中的文件与文件夹总量，而不是磁盘容量比例；隐藏、失败或未完成项目的处理，以及递归子文件夹如何计入当前目录的可见总量仍待可见互操作确认。下一次可见互操作还需确认 Size/Type 两列显示、计算未完成标记、精确格式、排序切换，以及“无焦点时暂停”如何作用于四窗格；默认排序设置也仍未知。

因此，后续原型验收需要同时覆盖逐项值与交互策略：Size 列复用、Type 列复用、单位/精度、百分比、数量、未完成/失败状态、排序忽略开关、失焦暂停和多个窗格并发。目录扫描必须按需、可取消，不能在导航时对全部子目录递归扫描；任何缺值不能伪装成 0。当前隔离 adapter 尚未读取逐项属性，排序门槛未通过，不能开始产品接线。

本项目目前的 F9/状态栏扫描只汇总当前目录的总字节数、文件数和子目录数，尚未提供逐行数据。此前 `SetViewProperty(System.Size)` spike 证明该视图缓存不会参与本机 Shell 大小排序且刷新会清除，不能满足可排序列。公开资料补充了 Q-Dir 的目标行为，但没有改变 ExplorerBrowser 对任意新列缺少受支持扩展接口的结论；也没有新增视图实现方案或启动 Shell 探针。

### 2026-10-27 百分比语义补充

Q-Dir [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 将 Size 列中文件夹大小的下划线百分比说明为同一目录内文件和文件夹大小总体的相对比例。据此推断这是目录内容占比，不是磁盘空间使用率，不能复用状态栏的“已用容量百分比”。FAQ 未说明隐藏项、失败扫描、递归未完成及舍入如何处理；这些细节仍需对照 Q-Dir 可见行为。该资料只更新需求语义，不改变目前 ExplorerBrowser/DefView 的新列架构门槛。

### 2026-10-26 原生 Shell 视图架构评审

用户选择先做原生 `IShellView` 架构评审。评审推荐隔离验证 `IShellFolder2` folder adapter + Windows DefView，而不是自绘列表或重写完整 `IShellView`；这仍需独立 `IShellBrowser` 宿主，不可作为 ExplorerBrowser 的列插件。评审范围、比较、门槛及递归计算策略冲突见[架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。未修改产品代码、未运行 Shell 宿主或 GUI。

### 2026-10-26 无窗口文件夹适配器合同复验

运行 `ExplorerDirs.NativeProbe.exe --static-shell-column-folder-smoke`，在不创建窗口的条件下直接查询隔离 `IShellFolder2` 适配器。`PKEY_Size` 可读为 `UInt64`，标准大小列的格式化文本和 `CompareIDs` 均按 2、10、100 的数值顺序处理，并将相等值比较为 0；退出码为 0，探针输出 `window-created=False`。样本值是适配器合同夹具，不是真实递归目录计算结果。

这提高了 `IShellFolder2` + Windows DefView 路线的可行性证据，但没有创建 DefView、没有验证文件系统文件夹视图的列显示/排序，也没有覆盖拖放、右键菜单、Shell 操作和刷新。下一步仍需独立宿主兼容门槛验证；在本项目当前不进行 GUI/桌面操作的约束下，不运行该宿主探针，也不接入 ExplorerBrowser 产品代码。

## 2026-10-27：排序对照实验不再遇首个失败就退出

源码复核发现 StaticShellFolderAdapterProbe.Run 顺序执行 custom 升/降序，再执行标准 PKEY_Size 升/降序；首个数值顺序断言失败会直接抛出，后面标准属性对照没有执行。因此既有首个 custom 排序失败不能推出标准 Size 的排序实验也失败。之前显示读取后的回调计数仍是该阶段证据，但不能代替后续被跳过的四组排序请求。

探针现分别执行四组检查并逐项报告，每个错误保留在 AggregateException 中，任意一组失败仍令整个实验失败；没有降低升/降序预期。无窗口自检注入首次异常、随后三个错误结果，确认四组都执行并最终报告四个失败；全正确的矩阵也通过。该自检随真实无窗口 folder-contract smoke 通过，日志 src/.state/static-sort-matrix.log，原生属性/空值合同亦通过。修正最终成功日志的 parent-visible 标志，与该实验实际 ShowWindow/边界检查一致。

尚未运行创建视图的矩阵，未加入动态值缓存、递归扫描、IPropertyStore 装饰器或产品迁移。当前关键缺口仍是 DefView 是否在安全隔离环境中实际读取和排序适配列；应在后续集中实验分别取得 custom 与标准 Size 的四组结果，而不是继续扩展未被消费的数据通道。
## 2026-10-27：独立 Desktop 四组排序实测（门槛未通过）

通过新增 watchdog 模式 `static-shell-column-adapter-private-desktop` 在独立 Windows Desktop 执行；未调用 SwitchDesktop、SetForegroundWindow 或键鼠输入。私有 Desktop 内允许窗口显示以满足 DefView 条件，不等于无 HWND。超时外层 35 秒/内部 25 秒；两次均正常返回失败 exitCode=1，非挂死或超时。前后前台均 0x14A111A。

首轮 20261027-232708-9067d0e3 完整执行四组：custom/Size 升序失败，两个降序表面通过。但初始名称顺序 hundred/ten/ten-copy/two 恰好对应 100/10/10/2，降序通过不能证明发生重排。改用 a-ten-copy/hundred/ten/two，使初始数值为 10/100/10/2，与任何预期方向均不同；仅为此夹具增加等值文件名别名，不改数值断言。修正排序结束的 visible 边界检查，与既有 ShowWindow 保持一致。

最终记录：`src/.state/native-probe-watchdog/20261027-232840-a9f01e28.stdout.log`，SHA-256 `52C42DB67877FD1CC2A95F45B197D3A0AEE6A865DE0B12D005A2C9424E5F7E50`。DLL SHA-256 `87BEC6A41112A6093E5B9AAA744F790E98877D5D056B9AE835A9A14F0A44272F`。

| 检查 | 请求/读回 | 实际项目数值顺序 | 结果 |
| --- | --- | --- | --- |
| custom 升序 | S_OK，方向 +1 | 10,100,10,2 | 失败 |
| custom 降序 | S_OK，方向 -1 | 10,100,10,2 | 失败 |
| PKEY_Size 升序 | S_OK，方向 +1 | 10,100,10,2 | 失败 |
| PKEY_Size 降序 | S_OK，方向 -1 | 10,100,10,2 | 失败 |

列表实际读出的 Size 文本仍均为 1 KB，自定义子项文本为空，名称与日期列可读；view-uses-adapter=1。每组排序前、排序完成且探针尚未自行读取数值时，CompareIDs/GetDetailsEx/GetDetailsOf/IPropertyStore 各计数无增量。每组完成后四次直接 GetDetailsEx 读回由探针自身造成，现已用明确的阶段标签区分，不能误称 DefView 开始消费属性。

结论：静态 folder contract 仍通过，但 DefView 消费/排序门槛未通过。标准 PKEY_Size 对照也失败，使“仅缺自定义 schema”不足以解释结果；不能因此直接注册 schema、添加 IPropertyStore 装饰器或投入动态扫描缓存。仍需不同且有依据的视图身份/属性读取路径假设；当前证据不证明所有自定义 namespace 都不可行。产品架构、发布版与固定候选均未改变。

C++ /W4 /WX、NativeProbe Release build 零警告错误，最终无窗口 folder-contract smoke 继续通过。独立 Desktop 实验是后台原生组件负向证据，不是用户可见文件管理体验验收。
## 2026-10-27：接口转发与 COM 身份缺陷

新增最多 64 个不同 IID 的原生 QueryInterface 转发诊断（无持有额外 COM 引用、无动态增长；只在隔离 DLL 中），保留原转发语义。独立 Desktop 记录 `src/.state/native-probe-watchdog/20261027-233209-eff81f30.stdout.log`：DefView 请求并成功取得转发的 IParentAndItem（B3A4B685...）和 IPersistFolder2（1AC3D9F0...），另有若干内部 IID；四组排序仍失败，正常退出 1，前台前后不变。未注册任何属性 schema。

IID 名称经本机 Windows SDK 10.0.26100.0 ShObjIdl_core.h 核对；[IParentAndItem 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iparentanditem)说明该接口能直接提供父 ShellFolder 与子 PIDL。由此形成“转发的身份/父项接口可能让视图重新回到底层文件系统”的待验证假设，而不是已证明的读取调用链。

新增独立无窗口入口 `--static-shell-column-identity-smoke`，从同一个 adapter 及其成功返回的 IParentAndItem 分别 QueryInterface(IUnknown)，比较真实指针并逐个 Release。结果 `same-IUnknown=False`，以失败退出；日志 src/.state/static-identity-red.log。根据[微软 QueryInterface 实现规则](https://learn.microsoft.com/en-us/windows/win32/com/rules-for-implementing-queryinterface)，同一对象的各接口必须有相同 IUnknown 身份。因此当前“任意未知接口直接返回 inner 指针”的包装方式至少存在 COM 身份合同缺陷；原数值 folder-contract 通过不能覆盖该门槛。

本批只加诊断和独立失败门槛，未伪造修复、未降低要求。下一步应在隔离策略中只暴露正确包装的接口或对不支持接口返回 E_NOINTERFACE，再验证视图创建、原生操作与列消费是否仍可保留。若必须实现自己的 namespace 身份，应按架构门槛评审，不能把简单隐藏接口造成的功能丢失当作迁移成功。未证明该缺陷是所有排序失败的唯一原因；产品 ExplorerBrowser 不使用此 shim。
## 2026-10-27：严格接口策略与持久化包装对照

新增独立 CreateStrictShellFolderAdapter 工厂；旧工厂保留为身份缺陷的负向对照，不改产品。严格对象只暴露自己实现的接口，不直接返回 inner 的任意 QI 指针。新增 --static-shell-column-identity-strict-smoke 与 watchdog 的 static-shell-column-adapter-strict-private-desktop 模式。

第一阶段严格拒绝未知接口，直接数值合同/基础身份可通过，但独立 Desktop 的 SHCreateShellFolderView/CreateViewObject 返回 E_NOINTERFACE，无法进入文件视图（20261027-233706-08c1579f）。这不是迁移成功，也不能用“不支持接口”掩盖必需能力缺失。

第二阶段在严格对象上实际实现 IPersistFolder2（包括 IPersist/IPersistFolder 的 QI），各接口的 IUnknown 都返回同一适配层身份；GetClassID/Initialize/GetCurFolder 的功能调用委托给底层并逐次释放引用。原生文件夹操作仍通过已有 IShellFolder2 方法委托。直接检查同时验证文件夹与 persistence 接口身份一致、不再暴露未实现的 IParentAndItem；完整数值合同继续通过，exitCode=0，日志 src/.state/static-strict-final-identity.log。

最终独立 Desktop 实测（20261027-233824-e77f264e）恢复视图创建，view-uses-adapter=1，名称/原始 Size/日期可读，但自定义列仍空、四组排序仍为 10/100/10/2，最终失败退出 1，前台前后不变。证明 COM 身份缺陷确实可以隔离修正，但修正它并保留最小 persistence 路径仍不足以修复列消费。不能把该缺陷称为排序问题唯一原因，也不能把“严格接口直接测试通过”当作视图门槛通过。

C++ /W4 /WX 和 NativeProbe 构建通过，原有无窗口 folder-contract 与新的严格身份检查均通过。严格模式仅为隔离 A/B，未证明快捷键、菜单、拖放和通知等整体功能保留；不能替换产品宿主。下一步仍需调查文件系统解析身份/属性缓存如何影响 DefView 消费，不继续无依据扩展动态扫描缓存。候选和发布版不变。

## 2026-10-27：类身份对照与探针缓冲区修正

前一轮新增的原生 folder-contract 探针在输出 PASS 后以 `0xC0000374`（heap corruption）退出，因此该次结果无效。复查发现探针将 `SHELLDETAILS` 固定按 272 字节分配；x64 SDK 下 `STRRET` 的对齐使完整结构大于该值。另外，标准 `PKEY_Size` 对照还复用了已经释放的 details 缓冲区。现在由同一 C++ 编译单元导出 `sizeof(SHELLDETAILS)`，托管探针按实际大小分配；标准 Size 检查使用单独分配、释放的缓冲区。

修正后，`--static-shell-column-classid-smoke` 在 3 个独立进程中均以 exitCode=0 完成，严格身份基线也以 exitCode=0 完成，未再出现堆损坏。随后在独立 Desktop 上比较仅改变 `IPersist::GetClassID` 返回值的原型：DefView 确实使用 adapter，名称、系统 Size、日期及自定义列的属性回调均可读取；但四种自定义列/`PKEY_Size` 升降序仍不改变列表顺序。排序前后直接读取 DefView 内 `SysListView32` 的可见文件名，均为原始名称序；`CompareIDs` 计数为 0。显式调用 `IShellView::Refresh` 也未改变结果（返回 `S_FALSE`）。故类身份不是目前排序失败的修复点；隔离原型仍未达到逐行列的消费/排序门槛，不能接入产品。该私有 Desktop 探针退出码为 1，前台窗口句柄保持不变。

此结果修正了此前被损坏探针污染的判断边界：无窗口合同现可重复通过，但实际 DefView 消费与排序仍失败。产品代码、发布候选及用户配置均未改动。

为建立普通文件系统 ExplorerBrowser 对照，追查发现隐藏 `--view-data-capability` 的导航 helper 将绝对 PIDL 与 `0x4000` 一起传入；Windows 定义该值为 `SBSP_NAVIGATEBACK`（忽略 PIDL），所以 Shell 实际仍留在父夹具目录，误把新建的 `.view-data-*` 子目录计为第四项。改用绝对 PIDL 标志 0，并等待当前 `IPersistFolder2::GetCurFolder` 与目标 PIDL 相等后，夹具导航和详情排序名称升降序均恢复正确。

分组/筛选组合期间 Shell 会替换当前 `IFolderView2`；持有旧指针读取时得到空项或 `E_FAIL`，单次 `ItemCount` 命中也可能只是过渡值。探针现每次从 `IExplorerBrowser::GetCurrentView` 取新接口，并要求三次连续读取都得到预期 PIDL 行序，才继续检查导出。`--view-data-capability` 随后在 3 个独立隐藏 ExplorerBrowser 进程中全部以 exitCode=0 完成，含普通名称排序、筛选、分组、分组后启用筛选及导出行序；父 HWND 未显示，前台未变。

单独的 `--view-property-refresh-capability` 以 exitCode=0 完成，系统 `PKEY_Size` 以实际 5/3 字节文件得到 `five.txt|three.txt|child` 的降序；注入 `IFolderView2::SetViewProperty` 后仍为相同顺序，刷新后缓存清空。修正后普通 Shell 的标准排序控制已通过；与之对照，自定义 adapter 的 DefView 可见行序仍保持原名序，排序失败不能归因于初始夹具导航错位。

## 2026-10-28：区分实际 Size 排序与旧式列索引排序入口

此前 adapter 的物理夹具四个文件长度完全相同，不能作为实际 `PKEY_Size` 重排对照。本轮将隔离文件固定为 2、10、10、100 字节，并以字母序 `a-ten-copy.bin|hundred.bin|ten.bin|two.bin` 作为初始顺序。严格 adapter 的 Private Desktop 记录 `src/.state/native-probe-watchdog/20261028-002005-80159188.stdout.log`（SHA-256 `A6CB26626009DD5F4E4A3BF38183CC7D98FB94D1317606138CCD5B8A90F506DD`）：`PKEY_Size` 升序得到 `two|a-ten-copy|ten|hundred`，降序得到 `hundred|a-ten-copy|ten|two`；两项均通过。由此确认此前 Size 对照失效是夹具问题，不是 Shell 标准排序失效。

同一实验用 `IShellFolderView::Rearrange` 分别传入自定义列索引 37 和其当前显示位置 2，两次均返回 `S_OK`，但可见顺序不变；`CompareIDs` 计数也均无增加。随后 `IFolderView2::SetSortColumns` 对自定义 PKEY 升降序同样保持名称顺序，读回的排序键/方向正确但未重排；标准 `PKEY_Size` 升降序仍通过。自定义列的 `GetDetailsOf` 子项为空，排序调用未触发 `GetDetailsEx`、`IPropertyStore` 或 `CompareIDs`。探针 exitCode=1 是自定义排序门槛失败，Private Desktop 前台句柄未变。

微软文档将 `Rearrange` 标为弃用接口，并说明系统文件视图会以其低 16 位指定排序列；本轮实际试验表明调用成功本身不代表 adapter 获得了比较回调。[`IShellFolderView::Rearrange`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ishellfolderview-rearrange)。因此当前可确认：普通文件夹的标准大小排序可用；此 adapter 的自定义属性显示/排序尚未打通；两条排序入口都未调用 adapter 比较函数。自定义属性 schema 是否是原因仍只是推测，本轮没有注册 schema，也没有证明所有自定义 namespace 都不可行。产品仍使用 ExplorerBrowser，未接入原型。

后续评估 schema 方案时还要考虑部署边界：微软 [`PSRegisterPropertySchema`](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psregisterpropertyschema) 文档要求安装时注册机器本地 `.propdesc`，schema 为机器级、不能按用户注册，且 API 需要包含 `HKLM` 写权限的权限上下文。这与项目当前便携配置、不改系统注册表的原则冲突；不能把 schema 注册作为普通启动行为。若未来证明确实是唯一技术路线，需先单独审视安装/卸载和系统影响策略，再决定是否做可选安装能力。

### 2026-10-28：系统已知属性 schema 无窗口核验

为形成后续 DefView 对照，本轮只扩展无窗口文件夹合同探针：通过 `PSGetPropertyDescription` 查询 `System.Document.ByteCount`（`{D5CDD502-2E9C-101B-9397-08002B2CF9AE},4`）。Windows SDK 10.0.26100.0 的 `propkey.h` 将其声明为 `VT_I4`；当前机器解析结果为 `S_OK`，`IPropertyDescription::GetPropertyType` 返回 `VT_I4`，`GetColumnState` 返回 `SHCOLSTATE_TYPE_INT`。同一探针中的私有测试属性仍返回 `TYPE_E_ELEMENTNOTFOUND`，而 `System.Size`、`System.DateCreated` 与 `System.Document.ByteCount` 均能解析。

隔离 adapter 现在另提供一个仅供实验的 `System.Document.ByteCount` 列，使用与 schema 一致的 `VT_I4` 值；无窗口合同已验证该列状态、键映射、显示文本、类型和数值比较。该结果证明适配器可以按系统 schema 契约提供一列，但仍不证明 DefView 会消费该值。`VT_I4` 也不适合真实文件夹总大小。没有注册 schema、创建窗口或改动产品；NativeProbe Release 构建、原生 adapter 构建及 `--static-shell-column-folder-smoke` 通过，输出 `window-created=False`。后续隔离 DefView 对照只能把它用作“已知 schema”诊断控制，并与产品 64 位大小语义严格区分。

### 2026-10-28：已知 schema 列的 DefView 消费检查

将 `System.Document.ByteCount` 以其系统描述的 `VT_I4` 类型加入严格 `IShellFolder2` adapter 的 Details 列后，在独立 Private Desktop 上激活 DefView 检查。运行记录为 `src/.state/native-probe-watchdog/20261028-014424-registered-schema-column.stdout.log`（SHA-256 `8E51A3CE311D5AED4BCDBEC5C3024FBC62D151D99DF8A28D72B3B6FD77C16A8B`）。`UIActivate(SVUIA_ACTIVATE_NOFOCUS)` 成功，DefView 确认使用 adapter，用户桌面的前台窗口句柄前后不变。

DefView 的 `System.Document.ByteCount` 子项文本仍为空；从创建视图前重置的计数器观察，显示列及排序前后，该属性对应的 `GetDetailsOf`、`GetDetailsEx` 和 `CompareIDs` 计数均为 0。未注册测试列只收到一次列标题 `GetDetailsOf`，没有逐项读取。标准 `PKEY_Size` 升降序都通过；`System.Document.ByteCount` 两个方向仍保持初始名称顺序，严格探针以 exit code 1 报告未达自定义列排序门槛。可确认：即使系统 schema 已知、属性类型和列状态匹配，当前严格 filesystem adapter 路径仍未让 DefView 消费该自定义列。不能据此排除其他 Shell namespace/宿主设计，也不构成产品迁移依据；产品仍使用 ExplorerBrowser。

### 2026-10-28 激活视图仍不触发逐项属性回调

为排除“DefView 尚未激活所以不读取列值”的解释，NativeProbe 增加 `--static-shell-column-adapter-activated-strict-private-desktop` 对照。它在独立、未切换的 Windows Desktop 上创建同一严格接口适配器和文件夹夹具，显示父窗口后调用 `IShellView::UIActivate(SVUIA_ACTIVATE_NOFOCUS)`，再执行同一列显示与四向排序矩阵；没有把输入焦点交给视图，也未操作用户桌面。

最终运行记录为 `src/.state/native-probe-watchdog/20261028-013132-ca617853.stdout.log`（SHA-256 `F6EDBAF5663419115E6DB3E29756ADCED0AF75A7B2EC21D69926A4C4E3204952`），NativeProbe DLL SHA-256 `15D68CD4647CFC01BC6F79D650F22F805B39954D442BBE9520CD2FFAD914558E`。NativeProbe Release build 为 0 警告、0 错误。`UIActivate` 返回 `S_OK`，view 仍使用严格适配器，私有 Desktop 边界与主桌面前台句柄检查通过。标准 `PKEY_Size` 升/降序按真实 2/10/10/100 字节重排通过；自定义列升/降序仍保持名称顺序并使整个探针以预期的门槛失败退出。视图显示的自定义列各项文本为空；排序前后适配器 `GetDetailsEx(custom/size)`、子项 `GetDetailsOf`、`CompareIDs` 计数都没有由 DefView 增加，只有探针自身的少量显式检查调用。

这排除了本探针采用的“激活后 DefView 会开始消费该适配器逐项列值”路径，但不证明任意自定义 Shell namespace 或自定义 PIDL/属性存储路线不可行。下一步需改变项目身份/属性提供路径来形成新的可证伪假设；不再仅重复激活同一 adapter，不注册系统级 property schema、不增加递归扫描缓存，也不接入产品 ExplorerBrowser。该隔离结果不是用户桌面 UI 验收。

### 2026-10-28 包装子 PIDL 的后台合同原型

按“改变项目身份”假设新增仅供 NativeProbe 的私有子 PIDL 包装层：枚举器和 `ParseDisplayName` 返回含原始 PIDL 副本的私有单项 PIDL；`IShellFolder2` 的比较、属性、显示名、绑定、UI 对象和重命名入口会先还原原始项目身份，再委托文件系统 Shell folder。包装格式只在进程内实验使用，不写入 profile 或 `.qdr`。

无窗口合同验证通过：真实文件 PIDL 往返后 `ILIsEqual` 保持身份；错误 marker 被拒绝；包装枚举项通过 `GetAttributesOf`；`GetUIObjectOf(IDataObject)` 返回的数据对象可读取 `CF_HDROP` 与 `Shell IDList Array`（CIDA），两者都解析为现存夹具文件的真实 filesystem 路径；包装解析得到的项目维持既有列文本、`VT_UI8`、数值排序合同；`SetNameOf` 将夹具文件重命名并返回可还原的新 PIDL。底层文件系统枚举器的 `Clone` 返回 `E_NOTIMPL`，包装器原样保留，不伪造成功。原始 PIDL adapter 合同也继续通过，NativeProbe C++ `/W4 /WX` 与 Release 构建为 0 警告/错误，`git diff --check` 通过。

随后对同一包装项执行绝对 PIDL 路径解析：原始文件的绝对 PIDL 由 `SHGetNameFromIDList(SIGDN_FILESYSPATH)` 正确解析；`ILCombine(原始父目录 PIDL, 包装子 PIDL)` 则返回 `E_INVALIDARG`。这说明当前包装 child 身份不能直接用于普通 Shell API 解析绝对路径。适配器内部的列/重命名/DataObject 合同通过，不能据此推断该包装项可在 Shell 组件之间自由传递。

这只证明私有 PIDL 包装的编码/解码及被测 `IShellFolder2` 委托入口；绝对 PIDL 路径解析失败说明还缺少能解释自定义根/项目身份的 Shell namespace 路由。DefView 自定义列消费仍未验证，也未覆盖打开、菜单、属性对话框、剪贴板传输、OLE 拖放、导航和通知。下一步先解决绝对身份/namespace 路由问题，再在集中批次评估 DefView 列消费；当前包装方案不能直接进入产品。

### 2026-10-28：包装文件夹的嵌套绑定合同

无窗口复核发现包装 adapter 的 `BindToObject` 还会把子文件夹直接返回为普通 filesystem folder；因此即使根级 DefView 将来能使用包装身份，浏览进子目录也会离开实验 adapter。新增回归合同后，旧实现先因子目录缺少 adapter 专属 `Registered schema control` 列而失败。修复后，包装模式下对子文件夹的 `BindToObject(IShellFolder/IShellFolder2)` 会建立另一个包装 adapter；探针确认子 folder 保留该列元数据，且继续可解析其文件并取得正常 Shell 数据对象。未包装模式和其他非文件夹接口仍沿用原委托路径。

NativeProbe 原生 `/W4 /WX` 与 Release 构建、包装 folder smoke、原始 folder smoke 和 `git diff --check` 全部通过；未创建窗口、未触碰产品代码。进一步调用包装 folder 的 `CreateViewObject(IID_IShellView)` 后，不调用 `CreateViewWindow`，`IFolderView2::GetFolder` 仍报告该视图持有包装 adapter，且 `window-created=False`。这确认可在不建 HWND 的情况下构造 DefView COM 对象并核对根 folder 身份，但不测试列表读取或绘制。新增的第二层相对绑定也通过：根→子目录→孙目录每一级都取得包装 adapter，末端文件仍可经 Shell 数据对象返回。该结果只证明直接 folder 对象持有者可沿两层相对目录继续委托，不修复包装 PIDL 的普通绝对路径解析失败。

微软文档将绝对 PIDL 定义为从 Desktop namespace root 开始的完整身份；自定义 namespace 扩展需要在 Shell namespace 中声明根位置，文档列出的入口包括 CLSID junction。当前实验没有注册扩展，也只把 adapter 直接交给调用者，因此尚无从 Desktop 解析私有包装 PIDL 的路径。参考[PIDL namespace 说明](https://learn.microsoft.com/en-us/windows/win32/shell/namespace-intro)、[IShellFolder 接口范围](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellfolder)及[namespace junction](https://learn.microsoft.com/en-us/windows/win32/shell/nse-junction)。DefView 的列表读取、导航、上下文菜单、拖放、通知与自定义列消费仍待后续隔离宿主/集中 UI 验收。

### 2026-10-28：包装 PIDL 的 DefView 列消费对照

为确认“DefView 是否因为项目 PIDL 身份不同而开始读取 adapter 属性”，新增独立 Private Desktop 包装 adapter 命令，并与原始 PIDL adapter 使用相同夹具各跑一轮。两轮 DefView 都报告 `view-uses-adapter=1`，但自定义列子项仍为空；读完列表后计数均为 `GetDetailsOf(custom header/item)=1/0`、`GetDetailsEx=0`、`CompareIDs=0`、`IPropertyStore=0`。自定义列升序和 `System.Document.ByteCount` 升序都因行序未改变而失败。包装项直连 adapter 的显示/比较合同仍通过，所以差异发生在 DefView 消费路径，而不是包装 PIDL 解码入口。

完整日志位于 `src/.state/native-probe-watchdog/20261028-wrapped-pidl-defview.log`（SHA-256 `41BCDC57A2C424603529F180772CDF1CEE36A78D1F3A7F3CCA6617B9FED60C1D`）和原始基线 `20261028-static-adapter-defview.log`（SHA-256 `188246C3C72A486D8C13ECDBE7EFB4EE2874F0347304845EFF99B7D229B45C59`）。两次探针都按未通过列显示/排序门槛返回 exit code 1；前台 HWND 均保持不变，Private Desktop 在子进程结束时清理。结果说明仅改变 child PIDL 身份不足以让 DefView 逐项读取或排序这些列；包装 adapter 及列路继续仅作隔离研究，不接入产品。进一步推进需要新的、可证伪的属性提供架构假设，并评审完整 Shell 行为成本，不能把当前失败改成软性通过。

### 2026-10-28：旧 `IShellFolderViewCB` 逐项列回退探针

核对 Windows SDK 与 Microsoft Learn 后，增加一个只供 NativeProbe 的 Private Desktop A/B 命令：`SFV_CREATE.psfvcb` 安装 `IShellFolderViewCB`；仅在此模式下，让测试自定义列的 `IShellFolder2::GetDetailsOf` 返回 `E_NOTIMPL`，由 `MessageSFVCB` 为 `SFVM_GETDETAILSOF` 提供测试标题与文本。其他模式及产品代码不变。官方接口说明该回调只在 `IShellFolder2::GetDetailsOf` 失败且无 `IShellDetails::GetDetailsOf` 时作为后备；该消息另列于微软弃用 API 清单。

Private Desktop 实测中，DefView 仍能创建并显示标准 Name/Size/Date 列，但未调用 `SFVM_GETDETAILSOF`（请求/标题/项目均为 0），测试自定义列各项文本为空；前台 HWND 前后相同，探针退出并清理其 Private Desktop。完整日志 `src/.state/native-probe-watchdog/20261028-legacy-details-callback-private-desktop.log` 的 SHA-256 为 `4D5BFDFA9B061E9116CDE122DF87E0F9C6F492A448A1605A254861BB2466F640`。NativeProbe Release 构建 0 警告/错误，命令退出码 0 表示隔离观察完整完成，并不表示该列路线通过。结论：当前 `SHCreateShellFolderView` 视图没有采用此回调补读该自定义列；加上回调的弃用状态，不将其作为产品列实现路线。该结果不能推广为所有自定义 Shell namespace 架构都不可行。

参考：[SFV_CREATE](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-sfv_create)、[LPFNVIEWCALLBACK 的 SFVM_GETDETAILSOF 说明](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nc-shlobj_core-lpfnviewcallback)、[弃用 Shell API](https://learn.microsoft.com/en-us/windows/win32/shell/deprecated-api)。

### 2026-10-29：`IShellItem` 父级身份适配探针

针对包装 child PIDL 无法经 `ILCombine` 变成可解析的文件系统绝对 PIDL，新增无窗口命令 `--static-shell-column-parent-item-smoke`。探针使用 `SHCreateItemWithParent(parentFolder=adapter, childPidl=wrapped)` 创建 `IShellItem`，再从 `IParentAndItem::GetParentAndItem` 取回父 `IShellFolder` 与 child PIDL，直接比较父对象 `IUnknown` 身份和 `ILIsEqual` 结果；同时读取 Shell 文件系统显示路径。IID 与方法签名核对本机 Windows SDK 10.0.26100.0 `ShObjIdl_core.h`。

结果通过：`display-path` 正确指向夹具 `two.bin`，`same-parent-IUnknown=True`，`same-child-PIDL=True`，窗口未创建。既有包装文件夹无窗口合同、嵌套绑定、Shell 数据对象及列/排序合同同次运行通过；绝对 PIDL 对照仍明确显示 `ILCombine(folderPidl, wrappedChild)` 得到的 PIDL 无法按文件系统路径解析。NativeProbe Release 构建 0 警告、0 错误。

这证明 Shell item 路径可在对象级保留自定义父文件夹和相对 child PIDL；它没有恢复包装 PIDL 的 Desktop namespace 绝对身份，也没有证明 DefView 列消费、标准上下文菜单、拖放、剪贴板、导航、通知或完整 `IShellView` 宿主可用。故不改变“不能接入产品”的结论。

继续验证 Shell 选择项桥接：由上述 `IShellItem` 调用 `SHCreateShellItemArrayFromShellItem`，`IShellItemArray::GetItemAt` 返回的 item 仍具有相同父 `IUnknown` 和 child PIDL。最初将此数组绑定 `BHID_DataObject` 也返回成功，但核对 [`IShellItemArray::BindToHandler`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemarray-bindtohandler) 文档后发现，该 handler 仅应用于 flat data object 或由 `SHCreateShellItemArrayFromDataObject` 创建的数组；因此不把前一调用当作受支持合同。

现按受支持形式验证数据对象往返：从包装 adapter 取得 `IDataObject`，用 `SHCreateShellItemArrayFromDataObject` 构造数组，再调用 `BHID_DataObject` 取得返回的数据对象。往返的 `CF_HDROP` 与 CIDA 都可解析到现存夹具文件，但 `IParentAndItem` 显示父 `IUnknown` 和 child PIDL 均已变为普通文件系统身份（两次独立运行都为 False）。探针、包装 folder/column 合同均以 exit code 0 结束，窗口未创建。

随后用包装子文件夹构造 `IShellItem`：`BHID_SFObject` 返回的 `IShellFolder2` 保有实验列元数据；`BHID_SFViewObject` 返回 `IShellView`，经 `IFolderView2::GetFolder` 取回的 folder 与绑定得到的 folder 有相同 `IUnknown`。两轮完整无窗口探针都重复通过。日志为 `src/.state/native-probe-watchdog/20261029-shell-item-parent-probe-final-1.log` 和 `...-final-2.log`。

结论：对象级 `IShellItem`、由它直接创建的数组和 `BHID_SFViewObject` 可保留包装身份；经受支持的标准数据对象往返则丢失身份。后一条可维持现存文件格式的剪贴板/拖放路径，但不能充当完整自定义 namespace 桥接，也不能让 Shell 绝对 PIDL 解析包装节点。当前的新正向证据使隔离 `IShellView` 窗口原型值得继续，但尚未实际创建 view window、浏览/枚举列表、验证列消费、Shell 菜单、复制/移动拖放、导航、通知和刷新；也不构成产品迁移许可。下一步应在隔离宿主中从 `BHID_SFViewObject` 获得的视图创建隐藏 view window，逐项验证枚举、详情、排序和标准文件操作，再决定完整迁移是否可行。

### 2026-10-29：Shell item 绑定视图的 Private Desktop 窗口门槛

新增 `--static-shell-item-bound-view-private-desktop`，通过 `IShellItem::BindToHandler(BHID_SFViewObject)` 取得子文件夹 `IShellView`，在独立 Private Desktop 中调用 `CreateViewWindow`。两轮均创建实际 DefView `SysListView32`、读到两个子项，并通过标准 `PKEY_Size` 升序排序（`two.bin|ten.bin`）；从 view folder 取得的适配器仍暴露实验列元数据，且基于它取得的 Shell 文件 `IDataObject` 通过 `CF_HDROP` 和 CIDA 现存路径合同。Private Desktop 监视器两轮均记录前台句柄与输入时间戳不变，并由测试进程负责桌面清理。

自定义列仍未通过：列设置 Capture 读回成功，view folder 暴露 40 列，但自定义显示子项仍为空。两轮回调计数相同：`GetDetailsOf` 共 4 次且均为列头、逐项为 0；自定义 `GetDetailsEx`、`CompareIDs` 和 `IPropertyStore` 均为 0。两个文件的 `IDataObject` 格式合同（`CF_HDROP` 与 CIDA）也有效，但没有执行复制/移动操作。故该架构已证明可从 `IShellItem` 绑定到真实 Shell view、枚举和按标准大小排序，也能提供常规文件数据对象格式；仍未提供产品所需的逐项自定义列值，且没有验证原生菜单、真实复制/移动、OLE 拖放、导航历史与目录通知。由于自定义列是本轮硬门槛，两轮探针均在该项以 exit code 1 结束；这不是窗口创建失败。视图的 `GetFolder` 与单独 `BHID_SFObject` 返回对象虽 `IUnknown` 指针不同，但前者仍含实验列元数据，不能把指针不相等单独判为视图退回文件系统。

Private Desktop 记录：`src/.state/native-probe-watchdog/20261029-shell-item-bound-view.log`（末轮）；探针源码与命令保留，后续可复跑。产品代码、ExplorerBrowser 和 release 未改变。

参考：[IShellItem::BindToHandler](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem-bindtohandler)、[IShellItemArray::BindToHandler](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemarray-bindtohandler)、[SHCreateItemWithParent](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shcreateitemwithparent)、[SHCreateShellItemArrayFromShellItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shcreateshellitemarrayfromshellitem)、[IParentAndItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iparentanditem)。

### 2026-10-29：`IPropertyStoreFactory` 列值提供路径

为区分“DefView 不读取 `GetDetailsEx`”与“DefView 不支持额外列”，在 `StaticShellFolderAdapter` 的 Private Desktop 专用绑定视图探针中增加一个最小 `IPropertyStoreFactory`。微软文档说明该工厂通常经 `IShellFolder::BindToObject` 或 `IShellItem::BindToHandler` 获取，且是实现 Shell 文件夹数据源时提供属性存储的受支持方式；工厂只暴露已有 schema 的 `System.Document.ByteCount`，测试值由对应文件实际长度转为 `VT_I4`。测试夹具长度为 `2/10/10` 字节。该实现仅编入 NativeProbe 原生 DLL，不触及产品代码、系统注册表或发布包。

Private Desktop 实测：已注册 `System.Document.ByteCount` 列显示为 `2|10|10`，标题正确；DefView 通过新工厂路径发生 `BindToObject/创建 store/GetValue = 3/3/3`，而同一轮 `GetDetailsEx` 子项、`GetDetailsOf` 子项和 `CompareIDs` 仍为 0。显式从 `IShellItem2::GetPropertyStore` 和 folder `BindToObject(IPropertyStoreFactory)` 读取也分别得到 10。对照中，未登记的私有测试 PKEY 仍没有列标题或值，且显示列成功不等于排序成功：将已注册列设为排序键后，视图仍保持初始名称顺序 `ten - 副本.bin|ten.bin|two.bin`；标准 `PKEY_Size` 恢复后则按 `two.bin|ten - 副本.bin|ten.bin` 排序。前台 HWND 与输入时间戳不变，Private Desktop 由探针清理。

本轮说明：DefView 可以显示通过 `IPropertyStoreFactory` 提供且具有系统 schema 的额外属性列；此前的空白不只是 `GetDetailsEx` 回调未被消费。探针仅在绑定视图命令的生命周期内启用该 factory，其他 NativeProbe 路径仍委托原 Shell folder。尚未解释系统属性的排序路径，也未解决自定义私有 PKEY 的 schema/标题；因此“自定义逐行列显示及排序”硬门槛仍未通过，不能接入产品。最终输出：`src/.state/native-probe-watchdog/20261029-023213-192edcbb.stdout.log`，SHA-256 `DDA42C1CFBCC1296AE3F33F88482E0E56DAEA3F04E833B735759B17381DAA146`；探针按尚未满足私有 PKEY 门槛返回 exit code 1，已注册 schema 值显示、factory 调用计数、基础 Shell 操作检查均通过。

参考：[IPropertyStoreFactory](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nn-propsys-ipropertystorefactory)、[IShellFolder::BindToObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-bindtoobject)、[GETPROPERTYSTOREFLAGS](https://learn.microsoft.com/en-us/windows/win32/api/propsys/ne-propsys-getpropertystoreflags)。

### 2026-10-29：已注册属性列排序方向读回

在上述隔离探针中补充排序前后行序及 `IFolderView2::GetSortColumns` 读回。`System.Document.ByteCount` 列初始视图顺序为 `two.bin|ten - 副本.bin|ten.bin`，值为 `2|10|10`；设置方向 `1` 后，排序状态读回 `System.Document.ByteCount:1`，行序变为 `ten - 副本.bin|ten.bin|two.bin`。随后明确设置 SDK `SORT_DESCENDING=-1` 并刷新，视图读回方向已变成 `-1`，但行序和值仍为 `ten - 副本.bin|ten.bin|two.bin` / `10|10|2`。两次排序后 `CompareIDs`、逐项 `GetDetailsEx/GetDetailsOf` 仍无调用；属性工厂显示阶段仍是 3 次绑定/创建/取值。微软文档将 `SetSortColumns` 定义为设置并排序视图的操作，本机 SDK 也声明 `SORT_DESCENDING=-1`、`SORT_ASCENDING=1`；因此现有结果表明排序状态已读回，但 DefView 对该已注册属性列的重排/方向消费仍不符合预期。不能仅凭 `S_OK` 或状态读回宣称排序可用。

本轮探针仍因未登记私有 PKEY 列没有值而以 exit code 1 结束；系统 schema 列显示与排序状态读回均有证据，实际排序门槛仍失败。日志 `src/.state/native-probe-watchdog/20261029-023517-d275bf4b.stdout.log`，SHA-256 `3F75F29366BABD94D055135A958575C5EBDA0FC4939546843705BF419E99834B`。

参考：[IFolderView2::SetSortColumns](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setsortcolumns)、[SORTCOLUMN](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ns-shobjidl_core-sortcolumn)。

### 2026-10-29：标题点击方向与属性排序元数据

补充通过 Private Desktop 原生 `SysHeader32` 鼠标消息点击已注册属性列标题的对照。两次点击后 `GetSortColumns` 分别读回方向 `+1`、`-1`，因此原先合成 `LVN_COLUMNCLICK` 不翻转方向的问题不能代表真实 Header 点击；但两次行序和值仍为 `ten - 副本.bin|ten.bin|two.bin` / `10|10|2`，没有随方向重排。`CompareIDs`、逐项 `GetDetailsEx/GetDetailsOf` 仍为 0，属性工厂仍为三次绑定/创建/取值。记录见 `src/.state/native-probe-watchdog/20261029-023820-85f298c6.stdout.log`；该运行因未注册私有 PKEY 列仍无值而按预期 exit code 1，Private Desktop 前台未变化。

再用无窗口 folder-contract smoke 查询 `IPropertyDescription`：`System.Document.ByteCount` 类型为 `VT_I4`，`GetSortDescription` 返回 `PDSD_SMALLEST_BIGGEST`（`0x3`），`GetTypeFlags` 为 `0x8000001A`，列状态为 `0x2`；标准 `PKEY_Size` 的类型为 `VT_UI8`，排序描述同为 `0x3`。该探针新构建 Release 成功（0 警告、0 错误），运行 exit code 0、未创建窗口。该排序描述只决定 UI 使用的排序文案（最小到最大），不是 DefView 已按属性值比较/重排的证据；不能把属性类型或 schema 描述视作排序已可用。下一步继续定位 DefView 的列排序比较接口/值消费路径；在升降序均实际按数值重排前，逐行大小列仍不满足迁移门槛。

### 2026-10-29：标准 Size 与属性列的 Header 输入 A/B

在相同 Private Desktop 视图中，对标准 `PKEY_Size` 的 Header 列 1 发送与属性列相同的 `WM_MOUSEMOVE/WM_LBUTTONDOWN/WM_LBUTTONUP` 消息。两次点击后标准列行序分别为升序 `two.bin|ten - 副本.bin|ten.bin`、降序 `ten - 副本.bin|ten.bin|two.bin`，读回方向为 `-1`、`+1`，说明此输入模拟确实能触发 DefView 标准列重排。对已注册 `System.Document.ByteCount` 列 2 重复点击，方向状态为 `+1`、`-1`，行序仍是 `ten - 副本.bin|ten.bin|two.bin`。这将差异收窄到新增属性列的 DefView 比较/排序值消费路径，而不是 Header 点击模拟整体失效。

最新日志：`src/.state/native-probe-watchdog/20261029-024146-7f855e87.stdout.log`。标准 Size 对照通过；整条隔离视图探针仍因未注册私有 PKEY 自定义列无值而 exit code 1，Private Desktop 前台未变化。产品和发布目录未改动。

### 2026-10-29：属性排序别名排查

按 Windows `IPropertyDescriptionAliasInfo` 合同读取 ByteCount 与 `PKEY_Size` 的主排序别名。两者查询都返回 `S_FALSE` 且没有别名对象；因此该次 A/B 中 ByteCount 失败而 Size 成功，不能归因于 ByteCount 缺少 sort-by alias（两边都没有）。无窗口 NativeProbe Release build 0 警告、0 错误，folder-contract smoke exit code 0、未创建窗口。下一步应继续查 `IShellFolder2` 列索引和 `IFolderView2` 属性键排序之间的映射/调用路径，特别是 DefView 为什么能对 Size 进入行重排，却没有向 adapter 发出 ByteCount 列比较。

同一无窗口探针读取 `GetDisplayType`，ByteCount 与 Size 都返回 `PDDT_NUMBER`（`1`）；连同已读出的升序文案描述 `PDSD_SMALLEST_BIGGEST`，当前属性描述的数字显示/排序文案元数据一致，尚不能解释两列排序行为差异。

参考：[IPropertyDescriptionAliasInfo](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nn-propsys-ipropertydescriptionaliasinfo)、[IShellFolder::CompareIDs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids)、[IShellFolder2::GetDefaultColumn](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdefaultcolumn)。

### 2026-10-29：未注册合成 Shell 文件夹的 DefView 边界

新增 NativeProbe 合成文件夹，提供三个自有 PIDL 项目及标准 `PKEY_Size` 元数据；其 `CompareIDs` 按 `2/10/100` 数值比较，并记录 `EnumObjects`、项目枚举与比较器调用。Private Desktop 隔离运行中，`IFolderView2::GetFolder(IID_IShellFolder2)` 返回的根对象身份与探针对象一致，但实际视图项目数为 0，`EnumObjects=0`、枚举项数为 0，故尚未进入升降序和 `CompareIDs` 验证。此前将空 PIDL 作为根时，视图反而显示 Desktop 的 62 项；用自定义未知 CLSID PIDL 时探针挂起，已放弃此无效路径。最新运行 `src/.state/native-probe-watchdog/20261029-033247-644ad21e.stdout.log` 退出码 1，Private Desktop 前台 HWND 与输入时间戳均未变化。

结论仅限于这个“未注册对象 + 物理文件夹绝对 PIDL”的构造：对象身份能从 `GetFolder` 读回，但不足以让 DefView 从对象枚举项目；`GetCurFolder` 的物理 PIDL 会被 Shell 当作文件系统位置，空 PIDL 会落到 Desktop。不能据此否定自有 PIDL namespace 中 `CompareIDs` 的标准大小排序，也不能把排序成功作为本轮结果。继续验证需要一个能够由绝对 PIDL 解析/绑定回合成 folder 的虚拟 namespace；本轮没有创建 Shell namespace/CLSID 注册表项，也没有修改产品或 release。下一阶段应先评估带完整清理与失败恢复的进程级临时注册方案，再继续比较器探针。`build-static-shell-folder-adapter.ps1` 和 NativeProbe Release 均构建成功（0 警告、0 错误）。

后续只为诊断绝对 PIDL 绑定，加入了 COM `IClassFactory` 并在探针运行期间临时写入专用 `HKCU\Software\Classes\CLSID` 项；构建成功，注册表写入获准的隔离进程执行完成，probe/watchdog 退出后检查该 CLSID 已清除。仅此 CLSID 注册仍不足以解析 `::{GUID}`：`SHParseDisplayName` 返回 `E_INVALIDARG`，视图创建/枚举及大小排序均未发生。没有挂接 Desktop/This PC `NameSpace`，因此没有让此文件夹进入 Explorer 命名空间。微软的命名空间扩展文档要求把扩展 CLSID 放在一个命名空间位置；下一步是否临时增加 Desktop\NameSpace 项待用户明确确认。日志 `src/.state/native-probe-watchdog/20261029-033704-fb20dbd7.stdout.log`；前台窗口和输入时间戳保持不变。

### 2026-10-29：列映射与默认排序合同调用计数

在 NativeProbe 适配器中增加 `MapColumnToSCID`、`GetDefaultColumn` 调用计数，并暂时启用一个只供实验的默认排序覆写（返回 ByteCount 列序号）。Private Desktop 创建绑定视图时，DefView 调用 `MapColumnToSCID` 40 次枚举列，但 `GetDefaultColumn` 为 0；初始读回排序仍为标准 Name 列，`CompareIDs` 为 0。仅在列枚举阶段看到 MapColumn 调用，随后标准 Size Header 排序以及 ByteCount `SetSortColumns`/Header 排序阶段两项计数均为 0。默认排序覆写因宿主完全不调用该方法而没有生效。

静态 folder-contract 已单独直接调用适配器映射合同，注册属性列索引正确映射到 `System.Document.ByteCount`；因此并非适配器无法映射，而是当前绑定 DefView 在排序路径未请求该映射，也未回调列比较器。最新输出 `src/.state/native-probe-watchdog/20261029-024818-b1854f9d.stdout.log`；Private Desktop 前台未变化。探针仍以 exit code 1 报告私有测试列无值。此结果将问题收敛到该 Shell view 的排序宿主行为；继续确认是否有可支持的排序委托/通知接口可接入，不能直接将默认列覆写或更改 `CompareIDs` 参数当成修复。

### 2026-10-29：`IShellFolderViewCB` 排序回调能力检查

本机 SDK 定义 `SFVM_GETSORTDEFAULTS`（53）和 `SFVM_COLUMNCLICK`（24）。隔离 view 创建时收到一次 `SFVM_GETSORTDEFAULTS`；仅用于诊断的回调按文档从 `wParam` 写入升序方向、向 `lParam` 写入 ByteCount 列索引并返回 `S_OK`。但创建后的 `IFolderView2::GetSortColumns` 返回无排序列，初始行序没有变，适配器 `CompareIDs` 计数仍为 0。文档明确说明系统文件夹视图对象不识别 `SFVM_GETSORTDEFAULTS`，因此排除它作为此 DefView 的排序控制入口。通过现有隔离 Header/`LVN_COLUMNCLICK` 消息路径时，`SFVM_COLUMNCLICK` 回调计数为 0；这只代表当前合成消息路径，不外推为真实物理鼠标路径。

完整记录：`src/.state/native-probe-watchdog/20261029-025123-08a037e1.stdout.log`。NativeProbe Release 构建 0 警告、0 错误；Private Desktop 前台未变；探针仍因私有测试列无值以 exit code 1 结束。参考：[IShellFolderViewCB::MessageSFVCB](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ishellfolderviewcb-messagesfvcb)、[SFVM_GETSORTDEFAULTS](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-getsortdefaults)。

### 2026-10-29：DefView 列点击通知对照

在 Private Desktop 绑定视图的已注册 schema 列上，向 `SysListView32` 的父窗口发送 `WM_NOTIFY/LVN_COLUMNCLICK`（子项 2），用于对照程序化 `SetSortColumns`。第一次通知后顺序从 `2|10|10` 变为 `10|10|2`，与按可见字符串比较的次序一致；第二次同样的合成通知没有反转顺序。两次过程中 `CompareIDs`、逐项 `GetDetailsEx/GetDetailsOf` 仍为 0，工厂统计保持原有 3 次显示读取。仅能确认 DefView 对第一次列点击通知改变了顺序；不能据此确认实际鼠标标题点击、内部比较算法或方向切换，因为合成通知没有经历真实的 Header/ListView 输入状态。

完整输出：`src/.state/native-probe-watchdog/20261029-023639-86984b53.stdout.log`，SHA-256 `B91C5B93ED3AFE0F7654DDFBD0DA70C38AC617E8D9ADF8AA218B231878667D49`。下一步应检查 Header 通知与实际点击状态如何更新 `SORTCOLUMN`，并以有数值差异的排序确认排序类型；不把当前文本序迹象作为产品决策。

### 2026-10-29：`System.Document.ByteCount` 升降序数值对照复核

复核前述绑定视图中的 ByteCount 排序结论时发现，先前的叙述没有把“行名顺序”和“属性值顺序”作为独立成功条件。现在探针明确记录每行的 `System.Document.ByteCount` 文本，并分别对排序方向 `+1/-1` 检查数值序列以及两次排序之间的行序变化。探针先验证 2→3 项的真实 Shell 文件复制通知，再将三项夹具重命名为 `a-ten.bin=10`、`two.bin=2`、`z-ten.bin=10`，显式刷新后名称顺序为 `a-ten.bin|two.bin|z-ten.bin`、属性值为 `10|2|10`。这个排列既不是数值升序 `2|10|10`，也不是数值降序 `10|10|2`，可排除按名称顺序碰巧等于任一数值方向。

标准 `PKEY_Size` 标题点击仍能按大小实际切换：升序 `two.bin|a-ten.bin|z-ten.bin`，降序 `a-ten.bin|z-ten.bin|two.bin`。ByteCount 设为升序后得到 `a-ten.bin|two.bin|z-ten.bin` / `10|2|10`，不满足预期 `2|10|10`；设为降序后行序与升序完全相同，仍为 `10|2|10`，不满足预期 `10|10|2`。方向状态分别读回 `+1/-1`，但方向变化没有重排行。故这条 DefView 路径对已注册 ByteCount 列的数值排序仍不工作；此前只看行名或使用 `2/10/10` 值序列的判断不够区分，应以本次非单调夹具复核为准。`CompareIDs` 及逐项属性读取回调仍为 0，显示值由 `IPropertyStoreFactory` 提供。日志：`src/.state/native-probe-watchdog/20261029-025919-177dca49.stdout.log`。Release 构建 0 警告/错误；探针以 exit code 1 结束，原因仍是未注册私有测试 PKEY 列为空值，不是夹具或 Shell 操作前置项失败；Private Desktop 前台句柄与输入时间戳未变。

这把已注册系统属性列的限制确定到“方向状态可读回，但 DefView 保持了名称顺序，没有按属性值排序”，并排除了名称顺序与数字顺序偶然重合的解释。后续不继续堆叠列元数据或旧 callback 试探；下一步检查 DefView/SysListView 排序委托边界及其与 `IShellFolder2::CompareIDs` 的关系，评估是否需要自绘/自管列表及由此增加的 Shell 操作兼容成本。产品仍留在 ExplorerBrowser，未发布任何改动。

### 2026-10-29：CLSID PIDL 直建视图超时

为避开尚未授权的 `Desktop\NameSpace` 注册，NativeProbe 改为临时注册唯一 HKCU CLSID、手工构造 CLSID 绝对 PIDL，并直接从隔离 adapter 创建 `IShellView`。原生 adapter 和 NativeProbe Release 构建均成功；带阶段日志的 Private Desktop watchdog 在 45 秒后终止探针。日志证明 `Initialize`、`SHCreateShellFolderView`、导出函数、`StreamViewHost` 构造和 Browser 接口取得都已返回；随后调用 `IShellView.CreateViewWindow`，Shell 多次重入 `CreateViewObject`，但 `CreateViewWindow` 在 45 秒内没有返回。watchdog 收尾后 NativeProbe 子进程与临时 CLSID 键均不存在，前台基线为 LockApp 且未变化。日志：`src/.state/native-probe-watchdog/20261029-035150-4c85193a.stdout.log`。

这把卡点缩小到 `CreateViewWindow` 对当前 CLSID 绝对 PIDL 的处理，但仍不能确认内部阻塞原因；缺少真实的 namespace 父级是待验证假设，不能当作已证实根因。单独 CLSID 注册不是完整 Shell namespace 挂载点，这种 PIDL 也未通过 Shell 正常解析；重复运行同一形态不会提供可靠排序证据。恢复实验需要有效且隔离的 namespace 注册位置。该超时不是产品卡死证据，也没有接入产品或更新发布版。

此轮的超时自清理还暴露 watchdog 兼容问题：Windows PowerShell 5 不支持 `Process.Kill(bool)`，且原进程树自检在停止子进程后没有等待退出并重新读取进程状态。脚本现改用受支持的 `Stop-Process`，兼容 UTF-8 无 BOM 写入，并在有界等待后复查子进程；PowerShell 7 与 Windows PowerShell 5.1 的 watchdog 进程树自检均通过。

### 2026-10-29：DefView 与自定义文件视图的支持边界复核

复核微软 `IShellFolder::CreateViewObject` 与 Shell namespace extension 文档后，确认 `SHCreateShellFolderView` 返回的是系统 DefView；接口文档允许文件夹返回系统视图或自行实现的 `IShellView`，并要求每次请求都创建独立 view object。微软建议优先使用系统视图以保留自定义视图未必具备的功能。自定义 view 则必须自行创建并管理子窗口、内容呈现、交互、快捷菜单和拖放等职责；这不是向现有 DefView 注册一项列属性的扩展点。

因此，当前探针确认 DefView 不消费新增列逐项值后，没有受支持的“给现有 DefView 补充 cell value/render/sort callback”可直接接入。完整自定义 `IShellView` 或自管列表是另一种架构，需要重新实现并逐项证明当前原生文件操作、菜单、键盘、拖放、通知、焦点与多窗格体验；这会扩大迁移面，不能作为逐行颜色/大小列的局部修补。此判断不证明任意第三方实现都绝对无法扩展 DefView，也不推断未公开的 DirectUI 内部行为。

下一条有技术根据的实验仍是由自有 namespace 使用稳定的虚拟 PIDL 和 `IShellFolder2::CompareIDs`，而不是继续改写被 DefView 忽略的属性存储/legacy callback。该实验需要一个有效的 namespace 父位置；当前唯一已试的手工 CLSID PIDL 在 `CreateViewWindow` 超时，且没有注册 Desktop\NameSpace。临时挂载位置须先获用户明确授权。获批后应在隔离副本验证正常 Shell 解析、列逐项读取、数值升降序、原生文件操作与关闭清理；在这些门槛通过前产品继续使用 ExplorerBrowser。

参考：[IShellFolder::CreateViewObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)、[Understanding Shell Namespace Extensions](https://learn.microsoft.com/en-us/windows/win32/shell/nse-works)、[Implementing a Folder View](https://learn.microsoft.com/en-us/windows/win32/lwef/nse-folderview)。

### 2026-10-29：DefView 排序时的 ListView 消息观察

为判断标准 Size 和 ByteCount 的差异是否来自 ListView 本身，探针在同一个 Private Desktop `SysListView32` 上安装短生命周期的只读 subclass：仅记录发往该 HWND 的 `LVM_FIRST..LVM_LAST` 范围消息，并立即交给原窗口过程。分别采集标准 Size `SetSortColumns`/两次合成 Header 鼠标消息路径，以及 ByteCount 两个方向；采样区间避开探针随后读取行文本的 LVM 查询。该观察只覆盖发给此 ListView HWND 的消息，不覆盖 DefView 父窗口、其他 HWND 或 Shell 内部 COM/模型调用。

标准 Size 升降序行序仍正确改变，适配器 `CompareIDs` 仍为 0；但其排序区间没有看到 `LVM_SORTITEMS`（`0x1030`）、`LVM_SORTITEMSEX`（`0x1032`）或 LVM 插入/删除/设置项目消息。ByteCount 两个方向也没有这些操作消息、没有新的属性工厂读取，行序继续为 `a-ten.bin|two.bin|z-ten.bin`，值为 `10|2|10`。观测到的其他 LVM 流量主要是 Header/项目查询，不能解释为排序命令。NativeProbe Release 构建 0 警告/错误；Private Desktop 运行退出码 1，仍停在末尾未注册私有测试列为空值的既有门槛；前台 HWND 和输入时间戳不变。日志：`src/.state/native-probe-watchdog/20261029-030358-8441a1db.stdout.log`。

这说明在所测本机 DefView 路径中，排序并非由它向 `SysListView32` 发送通用 ListView 排序/重排消息完成；更可能由 DefView 自己先改变 Shell 项目顺序，再由列表读取该顺序。这是根据限定 HWND 消息范围作出的推断，不是对 Shell 内部实现的证明。后续应追踪 `IFolderView2::SetSortColumns`、Header 通知到 DefView 项目序列/属性读取的边界，并优先验证能否通过受支持的 Shell folder 比较合同让 ByteCount 进入同一排序路径。产品和发布包仍未改变。

### 2026-10-29：临时 Desktop namespace 解析与数值排序通过

用户明确授权后，NativeProbe 在唯一保留的测试 CLSID `{A17D2A61-4C0B-4B22-8E36-35D92CBC1170}` 下，临时创建当前用户 `Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace` 项，并从 `SHParseDisplayName("::{GUID}")` 获取 Shell 解析出的绝对 PIDL；不再手工拼接一个未挂载的 CLSID PIDL。探针随后创建该 namespace 的标准 DefView，在隔离 Private Desktop 中读回三项静态样本，并分别执行 `PKEY_Size` 数值升序与降序。

结果：Shell parsing name 解析成功；`CreateViewWindow` 及时返回（此前相同未挂载 PIDL 路径 45 秒超时）；初始名称序为 `a-large.bin|two.bin|z-ten.bin`，升序为 `two.bin|z-ten.bin|a-large.bin`，降序为 `a-large.bin|z-ten.bin|two.bin`；`CompareIDs` 的 Size 比较器在升、降序阶段均各被调用 3 次。探针 exit code 0，前台 HWND 与输入时间戳保持不变。正常结束后由独立检查确认临时 CLSID 与 Desktop `NameSpace` 两个键均不存在。

本结果证明：具备有效 Shell namespace 父级后，独立虚拟 `IShellFolder2`/DefView 原型能够通过 `CompareIDs` 提供可消费的静态数值排序；它也定位了前一轮挂起与 namespace PIDL 未通过 Shell 正常解析相伴。它**不**证明完整系统资源管理器导航入口已实现，也未验证实际文件操作、第三方扩展、可见交互、多窗格及动态递归目录大小值；产品仍使用 ExplorerBrowser，未改 release。首次默认沙箱运行因注册表写权限拒绝而在创建任何键前退出；授权的限时隔离执行随后通过，完整日志为 `src/.state/native-probe-watchdog/20261029-081402-b9b3cce0.stdout.log`。

### 2026-10-29：Shell 绑定激活 namespace COM 对象并保留排序

随后将探针从“Shell 解析 PIDL、再由导出函数直接 `new` 适配器”推进到真正绑定：`SHBindToParent` 取得 Desktop 父文件夹，`IShellFolder::BindToObject` 按解析出的相对 PIDL 绑定目标；原生 DLL 的 `DllGetClassObject`/注册 class factory 负责创建文件夹对象，再从该对象调用 `CreateViewObject`。探针要求 class factory 实际创建计数大于 0，防止绕开 Shell 注册激活路径时误通过。

结果：`SHBindToParent`、`BindToObject`、`CreateViewObject` 均返回；注册 class factory 创建计数为 1；`IFolderView2::GetFolder` 与根文件夹 COM identity 一致；初始、升序、降序行序及 `CompareIDs` 计数继续符合前一节的预期。Release 原生 DLL `/W4 /WX` 与 NativeProbe 构建通过，隔离探针 exit code 0；前台 HWND 和输入时间戳未变，结束后两个临时注册键均不存在。完整日志：`src/.state/native-probe-watchdog/20261029-081657-b53c6798.stdout.log`。

同一探针随后只查询背景菜单、不显示或调用任何命令：`IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IContextMenu)` 成功，`IContextMenu::QueryContextMenu` 返回 7 个顶层菜单项、20 个 canonical verbs，包括 `undo`、`paste`、`refresh`、`groupby`、`sortascending`/`sortdescending`、多种 `viewlogical*` 和 `viewcolsettings`。该菜单对象证明 DefView 可提供背景菜单命令合同，不证明屏幕显示、命令状态或命令执行效果。日志：`src/.state/native-probe-watchdog/20261029-082138-0341f57e.stdout.log`。

再选中第一个合成项目，并用 `IFolderView2::ItemCount(SVGIO_SELECTION)` 确认选择数为 1 后查询项目接口：`GetItemObject(SVGIO_SELECTION, IID_IContextMenu)` 返回 `E_NOINTERFACE`，`GetItemObject(SVGIO_SELECTION, IID_IDataObject)` 返回 `E_FAIL`。因此缺口不是“选择没有成功”；当前合成 folder 的 `GetUIObjectOf` 明确尚未实现，不能提供选中项的 Shell 扩展菜单或标准剪贴板数据对象。完整日志：`src/.state/native-probe-watchdog/20261029-082350-9a619d15.stdout.log`。本测试没有执行复制、剪切、删除或重命名。

此结果证明的是 namespace 的正常 COM 绑定、DefView 数值比较接线和背景菜单对象的可查询性。合成文件夹仍故意不实现子项 `GetUIObjectOf`、重命名/绑定及数据对象，因此没有验证选中项目菜单、复制移动、拖放或真实文件操作；不能把它当作可用的资源管理器替代视图或产品迁移验收。

#### 后续进展（2026-10-29，取代上述初始缺口状态）

提交 `7e4e767` 为 synthetic folder 增加真实 filesystem backing folder，并在 `GetAttributesOf` 与 `GetUIObjectOf` 中将合成子项名称解析为 backing folder 的 child PIDL 后委托 Shell 对象请求。之后的隔离探针验证选中项 `IContextMenu` 与 `IDataObject`，取出 `CF_HDROP` 和 CIDA；多选 `IFileOperation.CopyItems`、取消后保留选择/焦点、`RenameItems`、`DeleteItems` 均已通过相应探针。`MoveItems` 的源/目标及两个视图更新断言通过，但该轮 watchdog 因前台切至 LockApp 返回失败，不记为整轮通过。以上仍是 Private Desktop 上的程序化 Shell 证据，不覆盖真实 OLE `DoDragDrop`、ExplorerBrowser 产品接线或可见用户交互。

### 2026-10-29：标准 Size 的属性存储覆写对照

NativeProbe 的诊断 `IPropertyStoreFactory` 现在同时对三个夹具返回明确的 `PKEY_Size` 覆盖值：`a-ten.bin=100`、`two.bin=200`、`z-ten.bin=10`；逐项直接读取均确认返回 `S_OK` 和目标值。随后设置标准 `PKEY_Size` 升序并刷新，DefView 行序仍是 `two.bin|a-ten.bin|z-ten.bin`，对应真实文件字节大小 `2|10|10`，而非覆盖值所要求的 `z-ten.bin|a-ten.bin|two.bin`。真实 Header 输入产生的两种方向行序也与真实文件大小对应。由此确认该视图的标准 Size 排序没有消费此子项 `IPropertyStoreFactory` 返回的 `PKEY_Size` 值；光装饰标准属性存储无法让递归目录大小参与 Explorer 原生大小排序。

本轮 `StaticShellFolderAdapter.cpp` 原生构建成功，NativeProbe Release 构建 0 警告/错误；Private Desktop 探针前台句柄与输入时间戳未变。完整探针继续走过排序对照，但最终仍以 exit code 1 停在原有“未注册私有测试 PKEY 列值为空”门槛；该状态不推翻此次 Size 对照。完整输出：`src/.state/native-probe-watchdog/20261029-030918-8e9002cc.stdout.log`。没有改产品 ExplorerBrowser 或 release。后续应停止重复尝试覆写 `PKEY_Size`，转向评估 DefView 排序身份/属性来源与自定义 namespace 的真实代价；只有存在受支持的数值排序合同才继续该路径。

### 2026-10-29：文件系统身份属性屏蔽探针未触发

为验证 DefView 是否因 `SFGAO_FILESYSTEM` / `SFGAO_FILESYSANCESTOR` 走了文件系统专用排序路径，曾在隔离 adapter 的 `GetAttributesOf` 临时加入按开关屏蔽这两个标记，并在绑定子文件夹及创建 view 前启用。该 Private Desktop 运行的屏蔽计数仍为 0：Shell 在该阶段没有通过此 adapter 的 `GetAttributesOf` 查询项目属性，因此实际视图并未进入“标记被屏蔽”的条件。原生属性工厂数值与上一轮一致，标准 Size 仍按磁盘大小排序。故这次不构成身份屏蔽 A/B 结果，不能推断移除标记后的 Shell 行为；无效开关接线已移除。输出：`src/.state/native-probe-watchdog/20261029-031308-c29b7d62.stdout.log`，前台 HWND 与输入时间戳未变化，探针仍以既有私有属性列空值门槛 exit code 1 结束。下一步应先定位 Shell view 取得文件夹/属性身份的实际接口边界，再决定是否值得做隔离的非文件系统命名空间样本。

### 2026-10-29：自有 Shell namespace 是排序验证的下一条技术边界

微软官方 Windows Classic Samples 的 Explorer Data Provider 以自有 PIDL 保存项目数据，并在 `IShellFolder2::CompareIDs` 按列比较其中的数值大小；这与当前把真实文件系统 PIDL 包装后继续委托给底层文件夹的 adapter 不同。该示例为“真正的虚拟 namespace 可以提供自己的比较合同”提供了实现先例，但没有证明本项目的 DefView 会消费标准 `PKEY_Size`，也不证明 Shell 文件操作、拖放、第三方扩展和会话体验可无损迁移。

因此此前的 property-store 覆写、属性别名、列映射及文件系统属性屏蔽试验到此收束；若要继续验证递归目录大小排序，应使用独立的非文件系统 PIDL/`IShellFolder2` 样本，并直接计数 `CompareIDs` 调用与升降序行序。产品仍保持 ExplorerBrowser，不接入这个架构原型。

本轮没有运行新的 Shell-view 探针。项目的 Private Desktop 前台守卫曾记录到用户桌面前台 HWND 变化，现行规则要求先确认隔离问题，再继续创建新的 Shell view；故自有 namespace 原型的 DefView 排序行为仍未实测。参考：[Microsoft Explorer Data Provider sample](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/Win7Samples/winui/shell/shellextensibility/explorerdataprovider)、[IShellFolder::CompareIDs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids)。

## 2026-10-30：合成文件夹递归大小元数据合同

- NativeProbe 合成 namespace 现为普通目录项计算递归文件字节数：包含该目录及其子目录中的文件，不计目录自身；遇到 reparse-point 子目录不继续遍历。目录项的 `GetDetailsOf(Size)` 同样返回数值文本。
- 无窗口树级 smoke 使用两层夹具，`child\nested.txt` 为 6 字节、`child\nested-folder\deep.txt` 为 4 字节；合成目录大小、Shell 行文本及 `GetDetailsEx(PKEY_Size)` 的 `VT_UI8` 均断言为 10。并与 4 字节的 `root.txt` 执行 `CompareIDs` 双向比较，确认目录大于文件、反向比较小于文件。先前递归大小断言按预期失败，改动后原生 `/W4 /WX` 构建和 `--synthetic-shell-folder-tree-smoke` 通过，现有 PIDL、绑定、存储、文件操作合同继续通过。
- 这只是 NativeProbe 的递归属性生产合同：没有创建 DefView/`IShellView`，因此不证明 Details 视图会显示该目录值、按它升降序排序，或主题/刷新行为正确。枚举时同步遍历整棵子树也不是产品策略；产品仍保留用户选定的 F9 显式扫描方式，不在导航或刷新时递归扫描。需在独立视图原型中验证列消费与排序，并评估性能后才能考虑产品接线。

## 2026-10-30：递归目录大小的 DefView 消费 A/B

在上述合成夹具上新增 Private Desktop DefView 检查：目录 `child` 含 6 B 直接文件和 4 B 嵌套文件，另有 4 B 文件 `small.bin`。未屏蔽文件系统标记时，视图显示行序 `child|small.bin`，大小文本为 `|1 KB`；排序升序/降序分别得到 `small.bin|child` 与 `child|small.bin`，`CompareIDs` 每个方向调用一次。诊断计数表明 DefView 未向目录请求 `GetDetailsOf`、`GetDetailsEx(PKEY_Size)` 或子项 `IPropertyStore`，唯一的 `GetDetailsEx` 请求来自文件。

随后执行真正的属性屏蔽 A/B：通过 `GetAttributesOf` 清除 `SFGAO_FILESYSTEM | SFGAO_FILESYSANCESTOR`。本轮 17 次属性查询中有 13 次包含这两个标记，确认屏蔽路径确实被触发；但目录 Size 请求仍为 0，显示依旧为空，文件请求仍为 1。升降序仍由 `CompareIDs` 得到正确的 `small.bin|child` / `child|small.bin` 次序。由此可排除“DefView 仅因这两个文件系统属性标记而跳过目录属性”的解释；这不排除其他 namespace 身份或接口合同的影响。

探针进程退出码 1 是预期的显示断言失败（未获得目录 Size 文本），不是 watchdog 超时或崩溃。Private Desktop 前台仍是原 LockApp HWND，输入时间戳未变化；结束后临时 CLSID、Desktop `NameSpace` 项、夹具和探针进程均已确认清理。NativeProbe Release build 0 警告/错误。该结果进一步否定“在包装 filesystem PIDL 的 DefView adapter 上屏蔽标记即可显示逐行大小”的路线；它仍不评估全自有、非 filesystem PIDL namespace 的完整属性消费与文件操作兼容性。产品和 release 未改。

随后追加更强对照：不再委托 backing folder 的 `GetAttributesOf`，而直接从合成 PIDL 元数据对文件返回 `SFGAO_STREAM`、对目录返回 `SFGAO_FOLDER | SFGAO_HASSUBFOLDER`。DefView 的 17 次属性查询均收到 provider-owned 属性，filesystem 标记为 0；但目录 Size 查询仍为 0，大小文本仍是 `|1 KB`，文件 Size 查询为 1，升降序与前轮一致。该结果说明被测 DefView 在此标准 `PKEY_Size` 列中仍跳过目录行，即使目录属性完全由虚拟 provider 提供；它尚不能区分 DefView 对标准大小列的目录语义与当前 Shell folder 属性合同的其它限制。probe 同样因显示断言返回 1，Private Desktop 前台/输入守卫及注册表、夹具清理通过，原生 `/W4 /WX` 和 NativeProbe Release 构建通过。接下来应测试独立 provider-owned 自定义列键是否会请求目录值；若仍不消费，就停止在标准列上反复变体，评估完整虚拟列/视图实现成本。

## 2026-10-30：provider-owned 自定义列显示与标题排序

把合成 folder 第 2 列映射到未注册的专用 `PROPERTYKEY`，列值通过 `IShellFolder2::GetDetailsOf` 返回。DefView 对两行都调用该方法，目录/文件文本分别为 `10` 和 `4`；标准和自定义 `GetDetailsEx` 请求均为 0。直接调用 `IFolderView2::SetSortColumns` 配此未知 key 返回 `TYPE_E_ELEMENTNOTFOUND`，说明未知 key 没有可用的属性描述排序路径。

随后对隔离 `SysHeader32` 的自定义列发送按下/释放鼠标消息（只作用于 Private Desktop 的 view HWND）。DefView 经原生标题排序，行序变为升序 `small.bin|child`、再变为降序 `child|small.bin`，provider `CompareIDs` 每次各调用一次。这是目前最有希望的原生路径：未注册的专用列 key 可以显示目录派生值，并可由 DefView 标题排序路由到 `CompareIDs`；它仍是 NativeProbe 的合成 Shell folder，不代表 Explorer Dirs 产品接线或完整文件操作/拖放/通知兼容。

同轮以已注册 `System.Document.ByteCount` 作对照时，DefView 改用 `GetDetailsEx`，只请求文件值，目录仍为空；对该属性的标题排序也没有通过。该 32 位语义不适用于产品的递归字节数，只作为 schema 行为对照。

虽然自定义列探针已输出正确文本、两种行序及 `CompareIDs` 计数，整轮未通过：Private Desktop watchdog 约 5.9 秒后观察到桌面前台句柄与最后输入时间戳变化，随后 worker 未按时返回，exit code 124。不能据此判断前台变化由测试导致，也不能把它视为产品卡死。临时 CLSID/NameSpace 注册与进程已清理；watchdog 中断 `finally` 后留下的两个明确命名 TEMP 夹具目录仍待清理。后续应先从源码梳理 header-click 后的 view/COM 清理等待点和守卫变化来源；在此之前不再运行新的 Private Desktop view probe。Release 构建与原生 `/W4 /WX` 构建通过，产品和 release 未改。

源码复核发现该次运行时 watchdog 的阶段标记始终停留在通用 `probe-entered`，无法区分是 `ReleaseSyntheticSortViewHold`、`DestroyViewWindow`、COM 释放、host Dispose、DLL unload、注册清理还是 fixture 删除等待。Wait-chain 只显示 worker 正在等待并涉及 COM activation/`ChsIME`，不足以定位某个被调用函数，也不能归因于 header-click。现在 NativeProbe 在每个清理调用前分别报告阶段；该诊断改动通过 Release 构建及无窗口树级 smoke，尚未重跑 Private Desktop。下一次得到有效阶段证据前，不调整释放顺序。

## 2026-10-30：合成 namespace 排序对照与清理停滞

继续以独立 `TEMP/TMP` 目录运行既有 Private Desktop 探针，避免触碰默认临时路径中已有的旧夹具。普通文件大小探针成功退出：初始名称顺序为 `a-large.bin|two.bin|z-ten.bin`，按实际 Shell `PKEY_Size` 数值比较后升序为 `two.bin|z-ten.bin|a-large.bin`、降序为 `a-large.bin|z-ten.bin|two.bin`；`CompareIDs` 在两个方向均有调用，Shell 选区、菜单、程序化 Copy、Rename、Delete、Move 与通知链也通过探针断言。Private Desktop 前台及最后输入时间均未变。日志：`src/.state/native-column-sort-e15695b821f34975a2a9848508e6cc5f/synthetic-shell-sort.log`。

同目录递归目录大小视图探针在结果断言后未能完成清理：日志先确认目录 `child` 显示 10、文件 `small.bin` 显示 4，升序为 `small.bin|child`、降序为 `child|small.bin`，比较器两个方向各调用一次；随后卡在 `cleanup-destroy-parent-window-1`。Wait-chain 显示 worker 等待 COM，链上有 `ChsIME` COM activation；前台与输入时间未变。故结果支持“自有虚拟 namespace 可提供并排序递归大小”，但这一轮不是完整生命周期通过，也未确认 ChsIME 是根因。保留了本轮隔离目录与日志以便继续检查，没有修改用户临时目录、产品 Shell 视图或发布版。

后续 A/B 在父窗口销毁前记录并清除属于测试窗口的焦点/鼠标捕获：捕获窗口为 0，焦点曾属于该测试视图且已清除，但 `DestroyWindow(parent)` 仍在同阶段阻塞；COM/ChsIME 等待链未变。因此“仅因测试视图保留键盘焦点或鼠标捕获”不足以解释停滞。该轮前台守卫在阻塞约 15 秒时检测到前台从 LockApp 变为无窗口句柄，且最后输入 tick 改变；整轮不计为 Private Desktop 隔离通过，不能判断该变化由探针还是外部输入造成。A/B 开关只在 `EXPLORER_DIRS_NATIVEPROBE_CLEAR_FOCUS_BEFORE_PARENT_DESTROY=1` 时对测试父窗口清除输入状态，不影响产品代码。NativeProbe Release 构建 0 警告/错误；目前停止新的 Private Desktop 重试，后续需在桌面边界稳定时再查父窗口 `WM_DESTROY`/子窗口清理调用栈。

为避免后续超时只剩 Wait Chain，PrivateDesktopShellProbe 现支持可选的进程 minidump：只有设置绝对路径环境变量 `EXPLORER_DIRS_NATIVEPROBE_TIMEOUT_DUMP_PATH` 才会在超时时调用 `MiniDumpWriteDump`，使用 `CreateNew` 防止覆盖旧证据；默认不生成转储。无 Shell 窗口的自检生成转储并经 `MiniDumpReadDumpStream` 核对 `MDMP` 头、线程列表及线程信息流。NativeProbe Release 构建 0 警告/错误。自检转储不是卡死现场；新的 Private Desktop 复验尚未在此开关下运行，因此仍没有父窗口销毁等待的线程栈证据。

另加入 opt-in `--synthetic-recursive-directory-size-wpf-host-private-desktop` 对照：保持递归大小数据、视图排序及其它断言不变，只将探针父窗口从原生 `STATIC` 换为 WPF `HwndSource` 根，并通过 `HwndSource.Dispose` 回收。依据是既有独立菜单生命周期探针在 WPF 根窗可正常完成，而原生 `STATIC` 根窗会卡在 `DestroyWindow`；这是对照假设，不是根因结论。保留原 `--synthetic-recursive-directory-size-private-desktop` 作为基线。NativeProbe Release 构建 0 警告/错误；受当前后台优先边界约束，此新视图分支尚未运行，不能报告清理修复。

源码级清理链复核发现：旧顺序在释放 `IShellView` / host 后，先卸载实现合成 Shell folder 的 Native DLL，再移除临时 CLSID 与 Desktop\NameSpace 注册，最后才销毁父 HWND。此前卡住的位置正是最后这一步。现有证据没有证明 DLL 卸载或提前撤销注册是阻塞根因；但父窗口仍含有 Shell 创建的子窗口时，先撤销这些依赖属于不安全的清理顺序，可能使 Shell 销毁/回调链依赖已卸载代码或不可解析的 namespace。NativeProbe 清理顺序现已调整为先销毁两个父窗口，再移除注册、释放 PIDL；随后只有 `DllCanUnloadNow` 返回 `S_OK` 才调用 `NativeLibrary.Free`，否则让操作系统在 probe 进程退出时回收映像。原 unload 判断只跟踪合成 folder；无窗口卸载合同 smoke 首轮在枚举器存活时按预期失败，补充跟踪 `SyntheticChildEnumerator` 和 `SyntheticFolderClassFactory` 后通过（两类对象存活时为 `S_FALSE`，释放后为 `S_OK`）。NativeProbe Release 及原生 `/W4 /WX` 构建均通过；尚未运行 Private Desktop 视图 probe，故没有证据表明卡死已修复；WPF 对照也仍未运行。下一步是在桌面边界允许时运行带超时 dump 的受控复测，若仍卡住再从保留的模块映像中读线程栈。本轮未启动新的 Private Desktop 视图 probe。

另外复核 timeout dump 的采集进程：watchdog 现在可通过绝对路径参数 `-TimeoutDumpPath` 在超时时由外部进程写入可选 minidump，并在尝试采集后再终止 probe。一般 probe 启用该选项时外层超时额外留 10 秒；递归目录大小视图 probe 则会在 25 秒先由 watchdog 捕获，早于其 30 秒内部超时。watchdog 现已接入递归大小基线和 WPF 根宿主两个命令。直接运行 probe 时原环境变量 `EXPLORER_DIRS_NATIVEPROBE_TIMEOUT_DUMP_PATH` 仍控制其进程内转储。无窗口 PowerShell timeout-tree 自检验证外部采集写出带 `MDMP` 签名的文件（最近一次 872,986 bytes），且父子进程按预期退出。该自检只证明外部写转储与超时清理，不证明卡死现场的 dump 内容可供完整栈回溯；尚未运行任何视图 probe。

与这条正向对照并行的 strict adapter 诊断仍为负：虽然 `SetSortColumns` 返回成功并可读回排序 key/direction，DefView 排序时没有调用 adapter 的 `CompareIDs`、属性读取或 legacy `SFVM_GETDETAILSOF`，列表行序不变。由此当前可行路线是自有 PIDL namespace，不是把真实 filesystem folder 包一层后期待默认 DefView 使用其派生比较器。下一步只针对清理阶段做源码/线程等待诊断；未定位前不再扩展实时扫描缓存或产品接线。产品仍使用 ExplorerBrowser，用户要求保留的 Shell 菜单、剪贴板、文件操作、拖放及刷新兼容门槛全部仍需通过。

2026-10-30：首次在 Private Desktop 运行 WPF 根对照，watchdog 退出码 0。`HwndSource` 根下的合成 `IShellView` 显示目录递归大小 10、文件大小 4，升序为 `small.bin|child`，降序为 `child|small.bin`，`CompareIDs` 两方向各调用一次。父窗口通过 `HwndSource.Dispose` 销毁，探针完整返回；用户桌面前台 HWND 与最后输入时间戳均未变化。检查时 `DllCanUnloadNow` 返回 `S_FALSE`，所以 DLL 保留至探针进程退出，符合避免卸载仍被 COM 对象引用模块的策略。

这与此前原生 `STATIC` 根的 `DestroyWindow(parent)` 阶段卡住形成对照，支持将父窗口/宿主销毁路径作为挂起原因的主要调查方向；但尚未在同一轮重跑 `STATIC` 基线，也没有 WPF 根下的卡顿转储或 WM_DESTROY 调用栈，因此不能断言已找到根因。该结果仅证明隔离合成 namespace 的显示、排序和 WPF 根清理，不代表 ExplorerBrowser 产品已经迁移或通过 Shell 菜单、焦点、拖放、操作、通知等迁移门槛。

随后用同一 watchdog 与夹具重跑原生 `STATIC` 基线，停在 `cleanup-destroy-parent-window-1`，外层 watchdog 捕获 561,128-byte `MDMP` 并终止探针；watchdog 检查确认保留的临时 CLSID/NameSpace 注册和新夹具均已清理。但前台监视在该阶段 5.7 秒时记录用户桌面从锁屏 HWND 变为 `0`，最后输入时间戳也变化；因此此基线的桌面隔离条件不成立，不能与前述 WPF 根成功组成有效 A/B 结论。转储头与目录确认包含 14 个 stream，其中线程列表有 14 个线程，并有线程信息流；尚未解析卡住线程栈。WPF 根仍是一个可重复的正向线索，STATIC 销毁挂起仍未定位。

对转储线程列表、线程上下文、模块表与栈内地址做了只读解析：主线程 TID 24940 的 RIP 为 `win32u.dll+0xAF04`，与本机同版本 `NtUserMsgWaitForMultipleObjectsEx+0x14` 对应；其栈地址还落在 `combase.dll` 的 `CoWaitForMultipleHandles` 导出范围附近及 `coreclr.dll`。其它线程的 RIP 多落在 `ntdll` 等待/worker-factory 入口。该地址分布与 STA/COM 等待并泵送消息相符，暂不支持“主线程 CPU 忙循环”的说法；但这不是符号化栈回溯，栈扫描会混入普通数据指针，也未建立 COM 等待环或定位具体调用对象。需要取得可用的 DbgHelp/WinDbg 符号化栈后再判断销毁为何无法返回。

后续用系统 DbgHelp 对主线程 RIP 和已扫描出的候选地址查询符号。匹配的系统导出确认主线程为 `NtUserMsgWaitForMultipleObjectsEx+0x14`，栈候选中包含 `CoWaitForMultipleHandles+0xBB` / `+0xAE3`，其他线程为 `ZwWaitForWorkViaWorkerFactory`、`ZwWaitForMultipleObjects`、`NtWaitForSingleObject` 等等待入口。DbgHelp 未取得 combase/coreclr 的私有 PDB；`Ordinal88` 与 `coreclr_set_error_writer` 等只是最近的公开导出名，不能视为候选地址的真实函数名。该阶段尚未得到逐帧调用栈；随后补取公共符号并做有限栈恢复，见下文。

进一步从 Microsoft 公共符号服务器取得与转储模块 GUID 匹配的 `ntdll.pdb`、`win32u.pdb`、`coreclr.pdb` 到 `%TEMP%\ExplorerDirs-symbols`，并用 `StackWalk64` 读取转储中的线程上下文、线程栈和本地模块 unwind 数据。主线程 TID 24940 可稳定恢复四帧：`NtUserMsgWaitForMultipleObjectsEx+0x14` → 两个 `combase.dll` ordinal 88 内部地址 → `CoWaitForMultipleHandles+0xBB` → CLR 模块地址 `0x7FFA7F3CF448`。越过 CLR 后 unwind 给出无效地址，未获 `combase.pdb`（符号服务器返回 503，后续请求超时）；因此当前能确认的是销毁阶段主线程在 STA/COM 消息等待路径，不知道等待的 COM 调用者/对象，也未证明循环或死锁。不要把 DbgHelp 为 CLR 地址返回的最近公开导出偏移当作真实函数名。

随后在相同 WPF 根宿主下连续复验两次。第一轮再次显示目录递归大小 10、文件大小 4，标题排序升序为 `small.bin|child`、降序为 `child|small.bin`，`CompareIDs` 每个方向各调用一次；watchdog exit code 0，前台 HWND 和最后输入时间戳不变。第二轮的列文本与两种排序断言也通过，但 watchdog 发现前台 HWND 从 LockApp 变为 0 且最后输入时间戳变化，故 exit code 1；此轮不计为隔离成功，也不能归因于 Shell 原型。两轮结束后均无 NativeProbe 进程，临时 CLSID/NameSpace 注册和两个夹具目录均不存在。可记录一次新的有效重复结果，但桌面边界仍不稳定，暂停新的 Private Desktop view 探针。产品仍未迁移，菜单、键盘、拖放和完整生命周期兼容门槛保持开放。

随后对递归大小 provider 做静态性能审查：`EnumerateBackingChildren` 会在构造子项枚举结果时，对每个普通目录同步调用 `GetSyntheticDirectorySize`；该函数会递归遍历其全部后代。每次重新枚举都会重新扫描，没有共享结果缓存、取消或增量通知。当前两层小夹具只验证值与排序，未测大目录耗时，也不能假定 Shell 总在后台线程调用。该实现仅属于 NativeProbe，不能直接移入产品；后续若继续产品级逐行大小，应先设计可取消的后台计算、结果缓存和 Shell 行更新机制，并在固定树规模下量测首次显示、刷新和重复访问成本。

局部结果更新仍未解决：当前合成 folder 对 `SFVM_FSNOTIFY` 排队后调用 `IShellView::Refresh()`，属于整视图刷新并伴随选区/焦点恢复；这不是理想的逐行大小完成通知。`IShellFolderView::RefreshObject` 虽接受子 PIDL 并说明为重绘指定项目，但 Microsoft 将整个 `IShellFolderView` 标为 Windows 7 起不再可用，并提示该方法可能改变或不可用；它不应再列为产品候选。下一项受支持接口假设改为：provider 在更新项目缓存后发送 `SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_IDLIST, absoluteItemPidl, nullptr)`，让 Shell 按项目身份接收变更。Microsoft 文档说明 `SHCNE_UPDATEITEM` 用于现存 Shell 项目属性或内容变化，但没有保证它会重新读取 `IShellFolder2::GetDetailsOf`、更新自定义列排序键或保持当前选区；大量项目通知还可能合并为 `SHCNE_UPDATEDIR`。

产品的文件系统监视路径已有一个相关策略：`ExplorerBrowserHost.QueueChangedItemNotification` 先发送每个变更项的更新通知；若当前视图的排序或分组依赖会变化的元数据，再对受影响目录发送更新通知。该路径针对真实文件系统项，不证明虚拟 namespace 的 PIDL 通知行为，但说明“仅发单项更新”不足以覆盖排序更新的风险。待 Private Desktop 边界稳定后，原型应分别比较单项通知、单项加父目录通知和整视图刷新对列值重取、排序、焦点/选区的影响；通知入口与 `SFVM_FSNOTIFY` 的关系见 [`SFVM_GETNOTIFY`](https://learn.microsoft.com/en-us/windows/win32/shell/sfvm-getnotify)、[`SHChangeNotify`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify) 和 [`IShellFolderView`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-ishellfolderview)。

2026-10-30 API 适用性复核：Microsoft 将 `IShellFolderView` 标为 Windows 7 起不再可用，建议使用 `IFolderView`/`IFolderView2`；其 `RefreshObject` 说明也标注方法可能改变或不可用。当前 Windows 10 SDK `shobjidl_core.h` 声明 `IFolderView2` 的项目、选择、排序、视图属性及重绘控制接口，但没有逐项 `RefreshObject`。因此即使后续隔离测试在当前 DefView 上能取得旧接口并证明逐项重绘，它也只能解释 legacy view 行为，不能作为 ExplorerBrowser 产品实现依赖。产品决策仍需满足的要求是单项大小更新、活动排序正确、当前焦点/选区稳定；候选实现需另找受支持的 Shell 路径，或者由自有 `IShellView` 宿主承担行刷新。

## 2026-10-30：IShellFolder2 递归 Type 与相对比例列原型

原生隔离文件夹适配器现可为目录行计算递归文件数/子目录数，并提供同级相对大小比例的字符串、`VT_R8` 属性值和列键映射。无视图合同断言覆盖 `GetDefaultColumnState`、`MapColumnToSCID`、`GetDetailsOf`、`GetDetailsEx`，以及现有多级路径解析、Shell 菜单、数据对象、复制/移动/重命名/删除合同。首次重建后新增列断言按预期失败；接入后原生适配器 `/W4 /WX` 构建和 `--synthetic-shell-folder-tree-smoke` 通过。

另尝试了一次隔离 `IShellView`/DefView 实际列消费。DefView 没有显示目录的递归计数；比例列仅文件行出现数值，目录行为空，点击该列没有按计算比例重排。该运行在 `DestroyViewWindow` 清理阶段超出 30 秒 watchdog，且监视到运行期间桌面前台和最后输入发生变化，因此不计为通过，也无法将变化归因于探针。为遵守“尽量不使用窗口”的后续偏好，不继续启动新的视图窗口探针。

该结果证明的是隔离 `IShellFolder2` 直接元数据合同，不证明 DefView 会消费这些扩展字段。要让实际视图显示/排序，仍需进一步研究 Shell 属性存储/列描述和生命周期接口；产品没有接入这些列，当前仍使用 ExplorerBrowser。探针异常退出遗留的本次临时测试注册项和 `ExplorerDirs-SyntheticShellSort` 夹具尚未清理：执行环境策略拒绝了精确路径的递归删除命令；此前存在的 `*-stale-*` 夹具未触碰。不得把本轮状态用于产品验收或发布。

2026-10-30 支持路径候选复核：将下一项隔离验证从 legacy `IShellFolderView::RefreshObject` 改为 Shell 变更通知 `SHCNE_UPDATEITEM`。Microsoft 将其定义为既有 Shell 项目的变更事件，并说明接收端通知可合并；公开合同没有说明 DefView 会因此重取扩展列、重新排序或保留选区。源码审查还发现产品已有文件系统项更新通知，并在排序/分组依赖变更元数据时补发受影响目录通知；这只是虚拟 namespace 通知实验的设计参考，不证明其结果相同。故下一次应比较单项、单项加父目录及整视图刷新，当前没有产品实现或虚拟 view 行为结论。

2026-10-30 清理链源码复核：递归大小分支在断言后直接返回，进入 `finally` 时先释放 `IFolderView2`，再调用 `IShellView::DestroyViewWindow`；这让视图子窗口销毁发生在其 view/browser-site COM 引用部分释放之后。现已把两个 `DestroyViewWindow` 调用移到 `IFolderView2`、view、文件夹和 browser-site 释放之前，并在子窗口回收后再释放合成对象引用、宿主与顶层 parent。该顺序符合“先销毁 Shell view window，再释放宿主依赖”的生命周期要求，但仍只是原型代码的修正假设，不证明此前 COM STA 等待因此消失。NativeProbe Release 编译、原生适配器 `/W4 /WX` 编译、无窗口 DLL unload contract 和 synthetic folder tree smoke 均通过；本轮没有运行 Private Desktop view，也没有卡死现场复测。下一步仍需在桌面边界稳定时运行带外部 watchdog/minidump 的递归大小对照，并检查是否越过 `cleanup-destroy-view-window-1` 阶段。产品和 release 均未改。

2026-10-30：A/B 输出增加通知到视图状态收敛的耗时，以及 `EnumObjects` 调用/行数、逐项大小列读取、自定义属性读取和大小比较次数，便于区分重枚举与局部刷新。注意这些计数覆盖探针通过 ListView 读取结果时产生的属性请求；它们用于同一探针内的相对比较，不代表纯 Shell 刷新开销。Release 构建、无窗口 folder-tree smoke、DLL unload contract 和 `git diff --check` 通过；本轮仍未启动真实 Shell view A/B。

2026-10-30：新增隐藏 `--common-listview-navigation-contract`，Shell 实际枚举临时根/子/孙目录和分支目录，验证直接子项及 parent PIDL；Back/Forward/分支截断复用产品 `ShellTabNavigationHistory`。NativeProbe Release、该合同、原有 Shell 行/文件操作合同、完整 Release SettingsSmoke 和 App Release build 通过，前台不变。此探针没有创建可见或隐藏 ListView/Shell view 控件；它不证明控件导航、键盘焦点、Shell TravelLog、虚拟位置或产品接线。

2026-10-30：修正上条 A/B 计数的观察者干扰：探针读取 ListView 文本时，用仅作用于当前 STA 线程的标记屏蔽同步回调产生的枚举、大小属性和比较计数；消息泵期间仍正常累计 Shell 更新工作。无窗口 folder-tree smoke 直接调用 `GetDetailsOf`、`GetDetailsEx`、`CompareIDs`、`EnumObjects` 和枚举器 `Next`，确认观察标记开启时这些查询不会污染计数；输出 `metric-observer-exclusion-passed`。原生适配器 `/W4 /WX`、NativeProbe Release、folder-tree smoke、DLL unload contract 均通过。该合同证明当前线程的屏蔽逻辑，不代表异线程 Shell 调用行为；真实通知 A/B 尚未运行。

2026-10-30：首次完成真实私有桌面 `IShellView` 通知 A/B；三种策略分三个全新进程、各自从递归大小 `10`/文件大小 `4` 的干净视图开始，避免前一通知改变后一策略的状态。`SHCNE_UPDATEITEM` 与 `SHCNE_UPDATEITEM + SHCNE_UPDATEDIR` 各产生 1 次 `EnumObjects`（2 行）和 1 次大小比较；两者排序都变为 `small.bin|child`，选区/焦点保留，但大小列不可见，`GetDetailsOf`/自定义 `GetDetailsEx` 读取均为 0。`IColumnManager` 的刷新后列清单仅有 Name、类型类列和 Type，不含 `System.Size` 或递归大小属性。整视图 `Refresh` 产生 2 次枚举（4 行）、2 次比较，同样没有大小列或属性读取；排序正确，但选区/焦点在 2.5 秒等待后仍未恢复。独立日志：`src/.state/native-probe-watchdog/20261030-032905-8e6c6f4f.stdout.log`、`20261030-032911-11d351dd.stdout.log`、`20261030-032921-04ed2549.stdout.log`。三轮 Private Desktop 前台句柄和输入 tick 均未变，watchdog 均退出码 0；临时 CLSID/NameSpace 注册、夹具目录和 NativeProbe 进程均清理。该结果说明当前自有合成 `IShellView`/DefView 通知路径会重新枚举并失去大小列，不能显示为成功的逐项列值刷新；不外推为 ExplorerBrowser 产品行为。

随后在 WPF `HwndSource` 根宿主中复验单项通知及列状态：通知前 `IShellFolder2` 已向 DefView 提供递归大小列（初始 `GetDetailsOf` 的项值请求为 2，列表显示 `10`/`4`）；但 `IColumnManager.Capture` 在通知前捕获的三项 key 是 Name、`{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1` 和 ItemType，本来就没有递归大小 key `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`。通知后 key 清单未变化，大小列文本不可见，大小 `GetDetailsOf`/`GetDetailsEx` 计数均为 0，说明 DefView 没有再次向 provider 请求该列值；焦点/选区仍保留。对通知前捕获的 IColumnManager keys 调 `ShellColumnManager.Apply` 返回 `S_OK`，读回 keys 仍相同。由于该捕获集合从未包含自定义大小列，这不是“恢复自定义列失败”的有效实验，不能据此判断列是否能通过 `IShellFolder2` 列合同重建。WPF 根宿主探针完整返回、watchdog exit code 0，前台与输入 tick 不变，临时注册和夹具均清理；日志为 `src/.state/native-probe-watchdog/20261030-033732-11a98464.stdout.log`。

此前两次相同列重放诊断使用原生 `STATIC` 根宿主，均在 `cleanup-destroy-parent-window-1` 卡住；内部 wait-chain 指向 Shell/COM activation 与 `ChsIME`，清除探针焦点仍未改变结果。桌面前台 HWND 与最后输入 tick 不变，外层 watchdog 清理了临时 CLSID/NameSpace 注册和夹具。该清理挂起未证实由 `Apply` 引起；WPF 根宿主的成功回合说明可用该宿主继续做有限诊断。下一步聚焦 `IShellFolder2::GetDetailsOf` 列索引/key 和 DefView 收到 `SHCNE_UPDATEITEM` 后为何不再次读取列值；不再把 `IColumnManager` 捕获列表当成自定义列状态来源。产品与 release 未改。

API 静态核对（Microsoft `IColumnManager` 文档，2026-10-30）：`CM_ENUM_VISIBLE` 的数值是 `2`，与本项目 `ShellColumnManager.VisibleColumns` 一致；`SetColumns` 设置视图列集合，并清除所有列状态，随后应调用 `SetColumnInfo`。项目 `Apply` 已按该顺序设置列并恢复捕获到的宽度。但此处 `IColumnManager.Capture` 的捕获集合并不包含经 `IShellFolder2::MapColumnToSCID` 映射的递归大小列 key，所以 `Apply` 的成功只能说明标准 IColumnManager 集合被重放，不能说明自定义列被恢复。参考：[IColumnManager::SetColumns](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icolumnmanager-setcolumns)、[IColumnManager::SetColumnInfo](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icolumnmanager-setcolumninfo)、[CM_ENUM_FLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-cm_enum_flags)。

2026-10-30 WPF 根宿主补齐其余两种通知策略：`SHCNE_UPDATEITEM + SHCNE_UPDATEDIR` 在 969 ms 内收敛，`EnumObjects=1`、枚举 2 行、大小比较 1 次，焦点/选区保留；整视图 `IShellView::Refresh` 约 2578 ms 收敛，`EnumObjects=2`、枚举 4 行、大小比较 2 次，焦点/选区未保留。两者通知后 `GetDetailsOf`/自定义 `GetDetailsEx` 读取均为 0，大小子项文本未恢复；IColumnManager 集合也未变。两轮 watchdog exit code 0，前台 HWND 与输入 tick 未变，临时注册/夹具已清理。日志：`src/.state/native-probe-watchdog/20261030-033900-ffb8c95b.stdout.log`、`src/.state/native-probe-watchdog/20261030-033905-ed4f73ba.stdout.log`。这与 WPF 根宿主的单项通知结果一致：加父目录通知不会让 DefView 重取派生列；整视图刷新有额外枚举成本并丢焦点，仍不刷新该列。

2026-10-30 WPF 根宿主 ListView 行更新消息试验：通知后向排序后的 `child` 项发送 `LVM_UPDATE`，控件返回成功，但没有枚举、大小属性读取、比较或显示值更新；随后发送 `LVM_REDRAWITEMS` 并调用 `UpdateWindow`，同样没有读取 `GetDetailsOf`/`GetDetailsEx` 或更新值，仅额外触发 1 次 `EnumObjects`、2 行枚举和 1 次比较，焦点/选区保持。watchdog exit code 0，桌面前台/输入 tick 不变，临时注册和夹具清理完成；日志为 `src/.state/native-probe-watchdog/20261030-034038-e8088730.stdout.log`。因此直接向内部 ListView 发送标准行更新/重绘消息也没有促使 Shell 重取自定义列；该 HWND 消息实验不构成受支持的 ExplorerBrowser 集成方案。Microsoft 消息说明：[LVM_UPDATE](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-update)、[LVM_REDRAWITEMS](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-redrawitems)。

2026-10-30 WPF 根宿主通知探针前置条件复核：旧日志 `20261030-093145-dc7b1ff9` 和 `20261030-093223-508bfa2b` 在新建视图上读到递归大小 `10`/文件大小 `4`，自定义大小列标题升序/降序分别得到 `small.bin|child` 与 `child|small.bin`，`CompareIDs` 两方向均被调用，watchdog 均正常返回且前台/输入未变化。这证明该隔离视图的初始显示和标题排序合同可重复；不代表产品接线。

随后 `20261030-093242-358ea2af` 将大小通知策略设为 `all`。日志确认初始列可显示并排序，但每种通知前的 `RunSizeUpdateStrategy` 都先调用整视图 `Refresh`，再用通用 `IColumnManager.Capture/Apply` 恢复列；恢复后 `IColumnManager` 集合里没有自定义递归大小 key，`GetDetailsOf` 也未再返回大小文本，代码因 `baseline-size-column-visible=False` 跳过了实际通知并打印 `notification-skipped=True`。探针随后仍以正常返回结束，故其 exit 0 **不构成**任何通知策略的 A/B 结果。该行日志对应源码在 [SyntheticShellSortProbe.cs](../src/ExplorerDirs.NativeProbe/SyntheticShellSortProbe.cs) 的 `ProbeSizeUpdateNotifications` / `RunSizeUpdateStrategy`。

下一步应修正隔离探针的基线管理：每种通知策略都从全新视图的初始自定义列直接开始，禁止先整视图刷新或用不包含该列的 `IColumnManager` 集合重建；若自定义列基线不可见，探针必须报告前置条件失败，而不能作为通知完成结果返回。之后再比较单项通知、单项加父目录和整视图刷新。产品尚未新增逐行派生列，ExplorerBrowser/菜单/焦点/拖放迁移门槛仍未满足。

2026-10-30 探针前置条件控制流修正：通知实验现在默认只运行 `single-item`；`all` 被拒绝，要求每种策略各自启动全新 probe 进程。策略从当前新建且已完成两方向标题排序的视图直接读取 `10`/`4` 基线，不再先调用 `IShellView::Refresh` 或 `IColumnManager::Apply`；若初始大小列/顺序缺失，抛出前置条件错误，不会再打印跳过后以成功退出。通知后仍只读采集 `IColumnManager` 列集合与 Shell 行状态。NativeProbe Release build 成功（0 警告/错误），未运行该 Private Desktop 视图命令；因此尚无新的通知行为结论，待之后在稳定隔离条件下逐策略运行。

2026-10-30 标准 Win32 ListView 派生值合同：扩展隔离 NativeProbe 的普通 `SysListView32` 基线，验证可更新第二列显示值和对应的 `lParam`，并通过 `LVM_SORTITEMSEX` 按更新后的派生数值重排；隐藏数据合同结果为 `initial=large-folder:10,small-folder:4`、`updated-cell=16 B`、排序后 `large-folder:10,small-folder:16`，窗口不可见且前台未变化。首轮可见基线无效：控件带有 `LVS_OWNERDATA`，代码却使用普通存储列表 API，第二列更新失败；已移除该样式，并保留这个失败原因以避免误读。修正后的隐藏合同通过，但没有重跑可见基线，因为首次 Private Desktop 运行同时观察到外部前台/输入状态变化。该结果只证明自有标准 ListView 的数据更新与排序可以接收应用派生值；不证明 Shell 枚举、图标、原生菜单、快捷键、拖放、通知、资源占用或产品集成。详情见 2026-10-30 roadmap 记录。

2026-10-30 标准 Win32 ListView 排序选区合同：在上面的隐藏数据合同中，先选中并聚焦 `small-folder`，把其派生值从 `4` 改为 `2` 后排序，确认行序从 `large-folder,small-folder` 变为 `small-folder,large-folder`，且唯一的 selected+focused 状态仍属于 `small-folder`。NativeProbe Release 构建 0 警告/错误；`--common-listview-data-contract` 输出 `selected-focused-after-sort=small-folder`、`selected-focused-count=1`、`visible=False`、`foreground-unchanged=True`。这仅说明标准存储式 Win32 ListView 重排后按项目身份保留选择/焦点；不证明 Shell DefView、ExplorerBrowser 或本产品的排序行为。

2026-10-30 无窗口列索引/属性键合同：合成 `IShellFolder2` 的 folder-tree smoke 现在分别核对 `GetDetailsOf` 的列索引 1、`MapColumnToSCID(1)` 和对应的 `GetDetailsEx` 值。默认模式映射到 `PKEY_Size` 且展示/属性值均为 10；启用实验自定义列后映射到递归大小专用 key，列文本及 `VT_UI8` 属性都随覆盖值变为 1。NativeProbe Release build 0 警告/错误，原生适配器 `/W4 /WX` build 成功，`--synthetic-shell-folder-tree-smoke` exit code 0、无 Shell view。此项确认 provider 自身的列索引/key/值合同一致；没有解释 DefView 在通知后不再请求该列，也不能证明实际 DefView 排序或 ExplorerBrowser 产品支持。

2026-10-30 Shell 行模型合同扩展：`dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --common-listview-data-contract` 退出码 0。不可见的普通 `SysListView32` 通过 `IShellFolder::EnumObjects`/`IEnumIDList` 枚举真实临时目录，行绑定绝对 PIDL、稳定 row ID 和 Shell 共享系统图标；更新派生大小并排序后，行身份、选区和焦点仍对应 `small-folder`。进一步使用所选行的 Shell `IDataObject` 与 `IFileOperation` 完成复制、移动、重命名、删除；夹具核对通过，源项目按预期保留。项目菜单和 folder 背景菜单可以查询（分别 154 / 115 个命令），但背景菜单来自 `IShellFolder::CreateViewObject`，缺少查看、排序、分组和刷新；产品完整视图菜单来自 `IShellView.GetItemObject`，本次并未接入。合同证明了“Shell 枚举身份 + 系统图标 + 常见文件操作”可接入普通 ListView 的一个可行切片，不证明 Explorer 等价菜单、拖放、导航、键盘、Shell 通知、虚拟位置、性能/内存或产品体验。迁移仍是隔离原型，Explorer Dirs 产品仍使用 ExplorerBrowser。

2026-10-30 产品扫描器到自有行模型：NativeProbe 现链接生产 `RecursiveDirectoryScanner`，通过临时真实目录扫描得到 10/4 字节递归值后再绑定到 Shell 枚举行；把 4 字节文件改为 2 字节后，实际重扫得到 2 字节并更新同一行，然后以数值排序。输出 `recursive-scan-backed=True`、`updated-scanned-bytes=2`、`selected-focused-after-sort=small-folder`、`shell-pidl-identities-preserved=True`、`foreground-unchanged=True`。NativeProbe Release、既有 Shell 行/文件操作合同、完整 SettingsSmoke 与 App Release build 通过。该结果证明扫描器返回值可进入原型数据模型，不证明产品逐行显示、后台扫描调度、Shell view 或菜单/拖放/通知/键盘集成。

2026-10-30 单次树扫描生成各目录递归汇总：生产扫描器新增 `ScanDirectoryTree`，一次深度遍历返回树中每个可访问普通目录自己的递归字节数、文件数、目录数和不可读数；根目录值与原 `Scan` 一致，junction 仍计为不可读且不进入结果。NativeProbe 用一次树扫描为同级的 `large-folder`/`small-folder` 两行提供 10/4 字节，再将小目录文件改为 2 字节后重新扫描一次，同时核对父目录汇总从 14 变为 12。该结果降低“每个可见文件夹各自重扫子树”带来的重复遍历，但探针仍显式同步调用，尚未实现后台取消/生命周期接线、缓存、列显示或产品集成；产品仍使用 ExplorerBrowser 和 F9 显式扫描策略。

同批更新后的 `--common-listview-data-contract` 确实输出了单次目录汇总、2 B 行更新、数值排序、稳定选区/焦点及复制/移动/重命名/删除操作断言通过；但完整合同最终因前台 HWND 从 `0x5C0AB6` 变为 `0x206CC` 而返回 1（菜单阶段前台已变化）。变化原因无法归因于探针或共享桌面活动，因此这次运行不记作完整合同通过，也未重跑。单次树扫描自身已有完整 SettingsSmoke 覆盖；NativeProbe Release build 和导航合同通过，App Release build 通过。该前台守卫失败不影响上述扫描字段，但整体数据/文件操作合同仍待后续稳定环境复验。

2026-10-30 同目录递归大小比例隔离原型：Q-Dir 官方 [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 将大小列下划线百分比说明为当前目录中项目大小的相对比例；没有公开显示精度或舍入规则。本原型以父目录单次树扫描的总可访问字节数为分母，计算 `100 × 行递归字节数 / 当前目录总字节数`，使用 `decimal` 计算，未规定显示精度或格式舍入；总量为 0 时返回空值。SettingsSmoke 覆盖 10/14、4/14、更新同级项目后 10/12、2/12 和零分母。NativeProbe 已将值写入隔离普通 `SysListView32` 的相对比例列，并在夹具变更后更新同级所有行；本次只构建探针，未运行完整 Shell/窗口合同（该合同前次因前台 HWND 变化触发整体守卫失败）。没有产品 UI、INI 配置、列排序或可见格式接线；Explorer Dirs 仍使用 ExplorerBrowser 与 F9 显式递归扫描。

2026-10-30 相对大小比例排序、选区与文件变更合同：扩展 `--common-listview-relative-size-contract`，以真实 Shell 枚举得到的两个目录行和系统图像索引，在隐藏 `SysListView32` 中验证初始 10/14、4/14 排序为小目录在前；先要求监听目标文件的 `FileSystemWatcher.Changed`，收到变更后再把小目录内容从 4 B 更新到 20 B，单次树扫描得到父目录 30 B，比例重算为 10/30、20/30，重新排序后大目录在前。两次重排后相同 Shell 行身份仍为选中/项目焦点项；两轮均收到变更事件、列表不可见且前台 HWND 未变。新增通知/排序断言先按预期失败，接入目标文件监听、按行身份更新及重排后通过。Release NativeProbe 定向合同连续两轮通过。此排序调用是探针中的程序化通用列表排序，且因同级项目共用分母，与递归字节排序等价；这里的 `FileSystemWatcher` 不代表 `SHChangeNotify`/DefView 对虚拟 Shell 行的更新合同，也不验证用户点击列标题、键盘焦点 HWND、Shell 菜单/拖放、ExplorerBrowser 产品列或 Q-Dir 精确显示格式。产品视图未改。

2026-10-30 比例列独立隐藏合同：新增 `--common-listview-relative-size-contract`，复用真实 `IShellFolder::EnumObjects` 行和生产目录树扫描器，在普通隐藏 `SysListView32` 中读回比例列。初始 10/14、4/14 分别读回 `71.428571428571428571428571429%`、`28.571428571428571428571428571%`；将小目录文件从 4 B 改为 2 B 并重扫后，同级行均重算为 `83.33333333333333333333333333%`、`16.666666666666666666666666667%`。合同退出码 0，稳定 row ID 保持，窗口不可见且前台句柄不变；路径不查询菜单、不执行文件操作，因此避开此前整体合同的前台变化位置。Release build 和该专用合同通过。它仍只验证隔离 ListView 数据更新，不代表 ExplorerBrowser 产品逐行列或可见精度/舍入已实现。

2026-10-30 隔离 ListView 自定义排序会话恢复：为继续评估产品列迁移前的数据契约，原型将递归相对大小使用的 app-only `PropertyKeyState` 和升降序写入现有 `SessionStore` 格式，并从 `Render` / `ParseComplete` 读回；排序通过自有 `SysListView32` comparator 执行，不调用 Shell 的 `SetSortColumns`。每次重排前按稳定 Shell row ID 记录 selected/focused 状态，排序后重新定位相同行并还原状态。NativeProbe Release build 0 警告/错误；随后运行隐藏 `--common-listview-relative-size-contract` 退出码 0，初始升序为 `small-folder,large-folder`，保存方向切换为降序后为 `large-folder,small-folder`，再切回升序；选中和焦点在两次重排及目录大小改变后仍跟随同一 Shell row ID，session key/direction 读回正确。窗口不可见、前台 HWND 未变。此 key 仅适用于自有视图的应用状态；产品 ExplorerBrowser 依旧无法用它恢复原生 Shell 排序，未接入产品。

2026-10-30 同目录子项目计数隔离合同补充：在比例合同的隐藏行模型中增加 `Contents` 数据列，初始扫描读回两行各 `1 file, 0 folders`；小目录内容变化为 3 个文件、1 个子目录（新增文件为零字节，保持大小比例夹具的 12 B 总量）后，重扫使该行读回 `3 files, 1 folder`，兄弟行仍为 `1 file, 0 folders`。合同先因计数列未填充按预期失败，接线后 `--common-listview-relative-size-contract` 通过，前台句柄未变。这里验证的是递归计数驱动隔离行更新，不是 Q-Dir 原生显示格式或产品列实现。

2026-10-30 静态 Shell 适配器 Private Desktop 复验尝试：使用不注册 CLSID 的 `--static-shell-column-adapter-activated-strict-private-desktop`，临时夹具位于 NativeProbe 输出目录并由探针 `finally` 清理；前台 HWND 未变化，但守护检测到运行期间系统输入 tick 改变，最终 `isolation-clean=False`。探针因自定义列升/降序仍显示名称序、`GetDetailsEx`/逐项 `GetDetailsOf`/比较回调为 0 而以 exit 1 退出；标准 `PKEY_Size` 升/降序仍通过。由于隔离条件不完整且该行为与既有结果一致，本次不计作干净复验，也不改变架构结论；不重跑以避免与用户当前桌面输入重叠。未注册属性 schema、未改产品视图。

2026-10-30 单项通知后显式重设派生列的探针新增 opt-in `single-item-reapply-column` 策略：通知后只把 `PKEY_ItemNameDisplay` 和递归大小属性键重新应用到同一 `IColumnManager`，再读取行值、顺序、选区及 provider 计数。为避免固定 CLSID/夹具被旧隔离运行占用，探针改为每轮生成 CLSID 和 GUID 夹具，并在启动时记录标识；原有固定/旧注册不会被复用或覆盖。NativeProbe Release build（0 警告/错误）及无窗口 `--synthetic-shell-folder-tree-smoke`（exit 0，`shell-view-created=False`）通过。

首次运行误用了不执行通知策略的 `--synthetic-shell-size-sort-private-desktop`；该结果未包含目标测量。随后正确入口 `--synthetic-recursive-directory-size-private-desktop` 实际观察到初始自定义列显示 `10/4` 且可排序；`IColumnManager.Apply` 返回 `S_OK`，捕获结果仍含递归大小 key，但通知后 DefView 行值读取为不可见（列索引 `-1`），provider 的大小读取/属性读取计数均为 0，选区/焦点保持。此轮最后输入 tick 发生变化，且在销毁 `STATIC` 根窗口时超时并停在 `cleanup-destroy-parent-window-1`；因此上述输出只作诊断线索，不能算有效 A/B 通过或最终否定结论。watchdog 进程已退出，但未到清理段；本轮唯一 CLSID `{4864fed2-6004-4e3c-9e07-d3f10f560a2c}`、NameSpace 项及 GUID 夹具 `ExplorerDirs-SyntheticShellSort-8b0ab5f22b194579b2e5fa2f5ae00a71` / `ExplorerDirs-SyntheticShellSort-Copy-8b0ab5f22b194579b2e5fa2f5ae00a71` 仍待清理。尝试清理时执行策略拒绝了删除操作，未改用其他途径；旧固定 CLSID `{a17d2a61-4c0b-4b22-8e36-35d92cbc1170}` 和 `*-stale-*` 目录均未触碰。下一轮只考虑 WPF 根宿主 Private Desktop 分支，并需先解决此轮残留清理授权/路径，或使用不产生永久注册的替代激活路径。产品仍未改，ExplorerBrowser 保持现状。

2026-10-31 共享 Shell 行身份辅助：文件系统行和虚拟 Shell 行探针现复用 `ShellListViewRowIdentity` 获取显示名、绝对解析名、Shell 属性、PIDL 系统图标索引，以及仅对 filesystem 项目读取的文件系统路径。递归大小夹具继续只接受真实路径；虚拟项目不会伪造路径。NativeProbe Release 构建 0 警告/错误；虚拟 Control Panel 行合同（9 个虚拟子项）、文件系统数据/文件操作合同和导航合同均通过，前台窗口未变化。此项只消除探针重复代码并验证两类 Shell 身份读取，不增加产品支持或改变 ExplorerBrowser 架构。

2026-10-31 自有 ListView 扫描移出 STA：相对大小隔离合同的初始和变更后 `ScanDirectoryTreeForPaths` 现在在 `Task.Run` worker 执行；STA 使用 WPF `DispatcherFrame` 等待并继续泵消息，扫描完成后由原 owner thread 更新隐藏 ListView。新增线程断言先在旧实现上按预期失败（两次扫描均运行于 owner thread），接入后 `--common-listview-relative-size-contract` 通过：worker 与 owner 不同，返回 owner 后行值/百分比、文件/子目录计数、升降序、选区/焦点均正确，控件不可见且前台未变。该项验证线程边界与原型数据链，不证明扫描耗时、UI 响应收益、Shell DefView 通知、产品逐行列或 ExplorerBrowser 迁移。

## 2026-10-31：隔离扫描代际与过期结果门控

NativeProbe 增加 `DirectoryTreeScanCoordinator`：每轮扫描拥有递增 generation 和独立取消令牌；新请求先使旧 ticket 失效并取消其 worker，导航/视图退休或扫描 owner 释放时也会取消当前请求。隐藏 ListView 的初始与变更后扫描共用同一协调器；只有任务完成且 ticket 仍是当前代际时才把结果交回 owner thread 更新行。SettingsSmoke 新增可控异步 worker 回归，核对新请求确实取消运行中的旧任务、旧 ticket 不能再发布、取消后新请求取得较新代际以及 owner 销毁能终止当前任务。定向扫描 smoke 和 `--common-listview-relative-size-contract` 通过；完整 Release SettingsSmoke、构建及 diff 检查结果以本提交验证记录为准。

这是隔离原型的取消与过期结果生命周期合同，不接入产品 `ExplorerBrowser`、用户配置、watcher 或递归大小列；不证明取消时限、真实 UI 响应、Shell provider 的异步通知或产品刷新排序一致性。后续需要把请求代际与行数据更新/目录变化重算联成完整自有视图模型，再进入已授权的隔离 Shell 门槛，不代表产品迁移获批。

## 2026-10-31：Shell 目录通知后重算派生行值

### 2026-10-31：过期扫描失败不再污染当前结果

`DirectoryTreeScanCoordinator.RunLatestAsync` 此前只把已过期 worker 的 `OperationCanceledException` 转成“无结果”。如果 worker 忽略取消并在新扫描开始后抛出 IO 错误，旧异常仍会传到调用方；旧请求的错误处理可能清理或报告当前一代的状态。回归先确认过期 worker 故意等待、被新请求取代后再抛错时旧实现会失败；现在只抑制 ticket 已过期时的异常，当前 ticket 的异常继续传播。定向 smoke 同时确认最新结果成功发布、旧结果不发布、当前失败可由 owner 处理。

此修正确保代际边界覆盖成功和失败两条路径，不改变取消扫描的底层文件系统 API 延迟；它仍是隔离行原型，不接入产品 ExplorerBrowser。

`--shell-change-reconciliation` 隐藏探针现把真实 Shell 目录变更通知与前述异步扫描协调器接通：先接收 `SHCNE_UPDATEDIR`，再由 Shell 重新枚举目录；对受影响的 `size-folder` 通过 worker 递归重算，并以稳定 row ID 将新派生值合入新枚举行。夹具初始有 1 个文件、0 个子目录、4 B；通知后加入一个 2 B 文件和一个子目录，更新读回 2 个文件、1 个子目录、12 B，目录 row ID 保持；真实隐藏 `SysListView32` 读回 `12 B` 与 `files: 2, folders: 1`。每次整表重建后按稳定 ID 恢复原选中/焦点行，变更、重排、删除路径均读回通过。扫描 worker 与 owner thread 不同，结果在 owner Dispatcher 发布。NativeProbe Release 构建及此隐藏合同通过，探针窗口不可见且前台未变；完整 Release SettingsSmoke 和 App Release 构建未因这次 NativeProbe-only 修改重跑。

这只证明隔离行模型和隐藏标准 ListView 的数据投影链与线程边界。探针不创建文件视图窗口，不接入产品 ExplorerBrowser 或产品刷新路径，也不证明扫描带来的交互收益、Q-Dir 原生列行为、可见行绘制/列排序，或产品 F5/查看方式切换不黑屏。下一步应验证自有 Shell 视图实际消费该行模型及文件操作/导航/菜单兼容性，再评估产品架构门槛。

### 2026-10-31：可见目标清单物化阶段响应取消

`ScanDirectoryTreeForPaths` 在递归遍历前需要将调用方的可见目录路径转成完整路径集合。此前取消只在 `ScanDirectoryTreeCore` 开始后才检查，所以已取消任务仍会消费完整目标枚举。现在在目标集合物化前及每个目标路径转换前检查 token；回归先确认预取消仍会枚举输入而失败，再覆盖枚举期间触发取消后不继续消费后续项。定向递归扫描 smoke 与完整 Release SettingsSmoke 通过；App/NativeProbe Release build 均为 0 警告、0 错误。

这仅让扫描取消覆盖“目标集合准备”和后续递归工作之间的完整生命周期；不能中断正在执行的单次同步文件系统 API（如枚举器 `MoveNext`、属性/长度读取），也不证明大目录或网络路径的取消时延。代码仍仅由隔离 NativeProbe 行模型使用，产品没有接入逐行目录大小列。

### 2026-10-31：隔离行大小值复用应用格式设置

普通 `SysListView32` 逐行大小原型此前在初始绑定与变更回写时都硬编码 `N B`，没有复用应用已有的自动/固定单位、精度和千位分隔格式器。现在抽出 `ShellListViewRowPresentation.FormatRecursiveSize`，原型两处写入都使用 `ByteSizeFormatter`；尚未计算的 `RecursiveBytes=null` 行显示为空白。定向 SettingsSmoke 先因 ListView 接线缺少共享格式器而失败，完成接线后覆盖 `1.5 MB`、`1,572,864 B` 和未扫描空白值并通过。

该原型接收显式 `SizeDisplayOptions`；当前 NativeProbe 仅使用格式器默认值，没有读取用户 profile，也未将大小列接入产品 ExplorerBrowser。它验证了 Q-Dir 单位/精度行为可由现有设置模型驱动，不验证 Shell DefView 列、排序或可见效果。

### 2026-10-31：Shell 重枚举时防止目录大小残留到替换文件

源码复核发现行模型按 Shell 解析名保留稳定 row ID 时，也无条件复制上一快照的递归大小。若同一路径的目录在两次枚举间被替换为文件，文件行会继承旧目录的大小和计数。现在只有前后两行都仍是带文件系统路径的 Shell 文件夹、且路径相同时才复用递归扫描结果；类型改变、虚拟项或路径改变时使用新枚举的扫描状态。NativeProbe Release build 通过（0 警告、0 错误）；本轮未运行 UI、Shell view 或行模型 smoke，因此该构建只证明源码接线和编译。

该修正只保护隔离逐行大小原型的重枚举数据，不接入产品 ExplorerBrowser，也不改变产品刷新路径。

同一回写路径还发现：完整扫描未返回某个仍在 Shell 行集中的文件夹时，旧逻辑会保留该行先前的大小/计数。各调用点传入的是本轮要求扫描的可见文件系统目录集合；因此缺少结果表示本轮没有有效值。现在清除该文件夹的递归大小与摘要，保持空白而不显示旧数据。NativeProbe Release build 通过（0 警告、0 错误）；未运行行模型 smoke 或 Shell view。

Shell 行模型的解析名在重命名时变化，普通重枚举会分配新 row ID，使自有视图无法按稳定身份恢复选区/焦点。现在仅当 `SHCNE_RENAMEITEM` 路径提供明确的新解析名→旧解析名映射时，才将新行关联到原 row ID；路径已变化，因此旧递归扫描值不会沿用。现有隐藏 `ShellChangeReconciliationProbe` 已接入该映射并改为报告 ID 保留；NativeProbe Release build 通过（0 警告、0 错误），本轮未运行该 probe，Shell 通知到可见行及实际焦点恢复仍未验证。

### 2026-10-31：真实 Shell 文件和目录重命名身份映射

重命名通知窗口在 `SHChangeNotification_Lock` 有效期内读取 `SHCNE_RENAMEITEM` 与 `SHCNE_RENAMEFOLDER` 的旧/新 PIDL，并用 `SIGDN_DESKTOPABSOLUTEPARSING` 提取与行模型相同的 Shell 绝对解析名，避免映射仅适用于文件系统项目。探针先复现目录重命名后 row ID 丢失；接入目录事件后通过。隐藏行模型合同现确认文件/目录重命名均保留稳定 row ID；目录路径变化时旧递归大小和计数立即清空，异步重扫后回写新的 12 B、2 files、1 folder，稳定选择/焦点保持。Release NativeProbe 构建及 `--shell-change-reconciliation` 通过，窗口隐藏且前台 HWND 未变。`SHCNE_RENAMEFOLDER` 当前以文件系统目录路径验证；虚拟 Shell 项、可见 IShellView 和产品 ExplorerBrowser 未覆盖，产品仍未接入此行模型。

### 2026-10-31：当前主线重复运行行模型后台合同

在 `76baf11` 后重新运行两个 NativeProbe Release 合同。`--common-listview-relative-size-contract` 再次通过：夹具初始大小 `10/4 B` 和比例 `71.43%/28.57%` 正确；更新后 `10/20 B` 和 `33.33%/66.67%` 正确，按新比例重新排序；目录子项摘要更新，扫描 worker 与 owner STA 分离，结果回到 owner 发布，选中/焦点身份保持。输出为 `passed=True`、`visible=False`、`foreground-unchanged=True`。

`--shell-change-reconciliation` 再次通过真实 Shell create/update/file-rename/folder-rename/delete 通知：行 ID 保持/退休符合预期，目录重命名后旧递归缓存被清除并重扫，大小与内容计数更新为 `12 B`、2 files、1 folder；每次扫描在 worker 执行并由 owner 发布，列表重排及删除后的选中/焦点仍保持。输出为 `foreground-unchanged=True`、`hidden-window=True`。

重复结果确认隔离数据模型和标准 `SysListView32` 投影在当前主线上可复现；它没有创建 ExplorerBrowser 或 `IShellView`，没有测试列标题输入、原生背景菜单、拖放和用户键盘，因此仍不构成产品文件视图迁移或 Q-Dir 逐行列验收。

### 2026-10-31：当前主线 Shell 项目菜单与背景菜单差异复测

在同一隐藏 `--common-listview-data-contract` 中重新通过行 PIDL 向父 `IShellFolder::GetUIObjectOf(IID_IContextMenu)` 查询项目菜单，并向父 `IShellFolder::CreateViewObject(IID_IContextMenu)` 查询背景菜单。项目菜单取得 154 个菜单项/扩展命令，canonical verb 包含 `delete`、`rename`、`properties`、`cut`、`copy`、`paste`、`copyaspath`；背景菜单包含 115 项/扩展命令，但它的菜单树不含查看、排序、分组或刷新。两次查询都没有显示菜单或执行菜单命令，前台 HWND 保持不变。

这确认自有 `SysListView32` 可按当前行 PIDL 取得 Windows Shell 项目菜单对象，但不能通过父文件夹菜单得到与产品 ExplorerBrowser 相同的文件区背景菜单。该差异与 Explorer Dirs 的原始要求一致：文件区右键行为需要保留 Explorer 视图上下文；若继续自有视图原型，必须先证明背景查看/排序/分组/刷新命令有可靠的当前视图路由，不能把普通 `IShellFolder` 菜单称为 Explorer 等价菜单。此次仅查询菜单对象和 canonical verb，没有验证可见 Popup、物理命中、命令执行、第三方扩展或产品迁移。

### 2026-10-31：生产扫描结果与 Shell 递归大小列合同对照

NativeProbe 的无窗口 `--static-shell-column-folder-smoke` 现在为真实临时目录构造 6 B 直接文件和 4 B 嵌套文件。生产 `RecursiveDirectoryScanner` 汇总为 10 B、2 files、1 folder；同一夹具交给隔离合成 `IShellFolder2` 合同，验证 `PKEY_Size` 的 `VT_UI8`、详细信息文本、数值排序，以及嵌套枚举/文件操作合同。完整合同退出码 0，`window-created=False`。

本轮把扫描结果接入了隔离原生 provider 的快照输入：两条兄弟目录的初值 10 B/12 B、磁盘变化后的重扫值 7 B/12 B 均由生产 `RecursiveDirectoryScanner` 生成，再由 NativeProbe 的原生列缓存验证行身份、属性文本、数值排序、PIDL 重枚举及 provider 重建后的读取。输出明确记录 `scanner-bytes=10/12`、`rescanned-bytes=7/12` 和 `provider-recreation=retained`；合同退出码 0，`window-created=False`。这证明扫描值可被隔离 provider 消费，不证明产品视图的异步扫描发布或 Shell 通知接线。

尝试让普通文件系统适配器为目录返回递归 `PKEY_Size` 得到 `E_FAIL`，说明不能依赖该适配器路径提供此派生列；也不应将这个单次 HRESULT 泛化为所有 Shell provider 的行为。产品未改，列显示、后台扫描生命周期和排序仍未接入产品；IShellView/自有视图迁移仍需菜单、拖放、通知、焦点、虚拟位置及可见行为评估。

### 2026-10-31：同路径文件夹替换时拒绝旧扫描快照

隔离 `ShellListViewRowModel.Reconcile` 之前只按绝对解析名、文件夹属性和路径复用递归值；删除后在同一路径重建的另一个文件夹可能继承旧大小。现在已扫描行携带卷序列号与 128 位文件 ID；重枚举时只有同路径、同文件系统身份才保留扫描值。目录 ID 读取使用 `FILE_FLAG_OPEN_REPARSE_POINT`，标识 junction 本身而不跟随目标；无法取得新身份时清除旧值，等后续扫描再发布。定向 `--shell-listview-row-model-smoke` 覆盖真实目录移动后同路径重建、文件 ID 变化及旧大小清除，并通过；NativeProbe Release build 0 警告、0 错误。此身份目前只用于隔离行模型，未接入产品 ExplorerBrowser，也未测量数千目录下的句柄打开成本。

### 2026-10-31：扫描完成时拒绝已替换目录的迟到值

补充复核发现另一条同路径竞态：扫描结果按路径回写时，如果扫描发起后目录被替换，旧行仍可能接收已替换路径的汇总。真实文件系统回归先分别在旧逻辑上复现“同路径新目录”及“原路径已消失”两种失败；行模型现于发布扫描值前重新读取当前文件 ID，只有行快照和当前身份均可读取且相同才发布结果，否则丢弃该次结果、清空大小并用可读取的新身份更新行，等待新一轮扫描。定向 row-model smoke 和完整 Release SettingsSmoke 通过，NativeProbe Release build 0 警告、0 错误。该校验只检查发布时可见的身份；后台扫描结果仍按路径键控，且该模型未接入产品 ExplorerBrowser/IShellView。

### 2026-10-31：行身份缺失时拒绝路径扫描结果

补充一条扫描发布边界：若 Shell 行枚举时无法读取目录文件 ID，之后即使同路径当前能读取到新的 ID，也无法证明按路径返回的旧扫描结果对应这个新目录。回归使用真实临时目录，在旧目录移走、同路径创建新目录后，将行快照身份设为不可用；旧实现错误接受 `666 B`。现在只有行快照身份和发布时身份都可读且完全相同，才接受扫描值；否则清除大小/摘要并记下当前可读身份，等待后续扫描。定向 `--shell-listview-row-model-smoke` 先在旧逻辑上失败，修正后通过；完整 Release SettingsSmoke、App/NativeProbe Release build 通过，均 0 警告/错误。

这仍只保护 NativeProbe 隔离行模型，生产 ExplorerBrowser/IShellView 未接入逐行大小。后台扫描输入仍按路径键控；身份采样到结果发布之间仍存在文件系统竞争窗口。

### 2026-10-31：路径身份采样不能识别扫描期间的 ABA 替换

进一步复核发现，发布时再次读取路径身份仍无法证明扫描实际枚举的是哪个目录对象：若路径在扫描期间短暂指向替换目录、随后又恢复为原目录，行快照与发布时的文件 ID 会再次相同，而 `RecursiveDirectoryScanResult` 只有汇总值，未携带扫描所访问对象的身份。真实临时目录重命名/恢复配合合成的路径结果先复现当前模型接受该结果；这证明了路径结果接口的信息不足，但没有运行或声称真实递归扫描必然跨越该短暂替换。

因此不添加第二次路径 ID 检查来冒充修复。要封闭这类窗口，隔离原型下一步必须让扫描结果与扫描实际打开的目录对象绑定，或以可靠的变更代际拒绝扫描结果；仅在扫描前/后按路径采样仍不能识别 ABA。此项尚未修复，产品仍未接入该行模型。

## 2026-10-31：隔离递归扫描绑定到实际目录句柄

NativeProbe 新增句柄绑定扫描器：目录以 `FILE_SHARE_DELETE` 打开并由 `GetFileInformationByHandleEx(FileIdExtdDirectoryInfo)` 枚举；每个子目录只在其打开句柄的卷序列号和 128 位文件 ID 与父目录返回的条目 ID 相同时才递归。扫描摘要携带根目录句柄身份，隔离行模型只在扫描对象、Shell 行快照及当前路径身份一致时写回。真实临时目录回归覆盖扫描器打开根后同路径替换，以及父目录枚举子项后该子项同路径替换；前者仍只汇总旧对象的 6 B，发布端拒绝替换路径；后者跳过身份不匹配的新对象并计入 unreadable。另以 700 个长 Unicode 文件名覆盖超过 64 KB 的目录枚举分页，核对字节数、文件数、目录数和 unreadable 计数；普通嵌套目录也确认 8 B/2 files/1 folder。定向 `--shell-listview-row-model-smoke` 通过。

此原型仍只用于 NativeProbe/SettingsSmoke，未接入 ExplorerBrowser、产品 F9 扫描器或用户界面。目录被重命名后，句柄仍能继续枚举原对象；若无法解析当前目录名，子路径回退会受路径可达性限制，相关项会被安全地略过而不是归给不同文件 ID。定向行模型 smoke、完整 Release SettingsSmoke、App/NativeProbe Release 构建（均 0 警告/错误）及 `git diff --check` 均通过；未启动 UI、隐藏 Shell view 或 ExplorerBrowser。

### 2026-10-31：句柄扫描结果进入无窗口原生列合同

无窗口 `--static-shell-column-folder-smoke` 现同时运行句柄绑定扫描器与既有产品递归扫描器，在相同真实夹具上逐目录比较汇总；初值 10/12 B、变更后 7/12 B 两轮结果一致，并继续把这些扫描值交给原生 provider 合同，验证 `UInt64`、属性文本、数值排序、同 PIDL 更新、重枚举及 provider 重建。合同退出码 0，输出 `window-created=False`。这证明更安全的隔离扫描结果能接入既有 provider 数据合同；不代表产品 F9 改用该扫描器，也不证明 ExplorerBrowser/DefView 可见列刷新。

### 2026-10-31：WPF Private Desktop 复核 DefView 派生列更新

重跑 `--synthetic-recursive-directory-size-wpf-host-private-desktop` 的单项目录通知路径。新建视图初始阶段，DefView 通过 `GetDetailsOf` 读取两个行值并显示目录 `10`、文件 `4`；标准/自定义 `GetDetailsEx` 计数均为 0。修改 backing 目录为 1 B 后发布 provider 快照并发送单项目录通知，DefView 重新枚举 2 行、按文件大小调用一次比较，但自定义列消失（读取索引 `-1`），之后没有 `GetDetailsOf`、属性读取或大小比较回调；选区/焦点仍保留。`IColumnManager::Capture` 仅含系统列，不能说明自定义列被恢复。

本轮 Private Desktop 前台 HWND 未变，但系统输入 tick 在运行期间变化，probe 按守卫返回 exit 1，因此只记录为受干扰的诊断，不作为干净通过或最终 Shell 行为结论。探针生成的 GUID 夹具、HKCU CLSID/NameSpace 项均已确认清理。现有干净探针与源码/API 复核未找到受支持的逐行派生值失效入口；不继续追加通知或内部 ListView 消息变体。逐行派生列需留在 ExplorerBrowser 能力边界之外，或评审自有视图模型的总体 Shell 兼容与维护成本，尚未接入产品。

### 2026-10-31：隔离复查视图状态流与 DefView 窗口重建

在前台窗口和系统输入 tick 均未变化的两次 WPF Private Desktop 运行中，给 `GetViewStateStream` 加入调用计数、mode、初始长度/hash 记录。单项更新前后分别有一次写流和读流；但单项通知后，DefView 仍重新枚举两行并丢失派生列。随后显式 `IColumnManager::SetColumns` 让自定义键重新进入列集合，却没有重建当前 ListView header，也没有触发属性文本请求。再切换 Details → Icons → Details 同样没有恢复列值。

一个隔离对照在同一 `IShellView`/文件夹对象上销毁并重建 `IShellView` 窗口后，列标题和值再次出现（`value-column-index=1`、目录/文件值 `1`/`4`），连续两轮结果一致；该路径重建期间没有额外读写视图状态流，说明恢复来自 view-window 生命周期重新初始化，而不是新序列化状态。首次对照没有恢复选区/焦点，稳定过程约 2.8 秒。随后在销毁前用现有 `ShellViewSelectionSnapshot` 保存一项选中及焦点 PIDL，重建后先 `UIActivate(SVUIA_ACTIVATE_NOFOCUS)` 再恢复；连续两轮均恢复选中和焦点，列和值仍在，稳定过程约 344 ms。后一个数字包含本探针恢复与轮询时间，不是可见用户刷新时长或性能保证。

该实验把问题收敛到“DefView 的窗口重建能重新绑定派生列 provider”。另将原来两行均设为选中的多选状态进行两次复测，快照均捕获并恢复 2 项选中和焦点，列和值也恢复。新增 302 行夹具，在第 250 行处设置顶部锚点；窗口重建自然回到第 0 行。等行值、选区和焦点恢复稳定后，以锚点文件身份再次定位，锚点项目矩形回到视口 y=0；两轮均成功。`IColumnManager::Capture` 的列顺序和宽度也逐项完全一致：名称 264、派生大小 132、类型 132。

排序条件已通过补充探针：重建前后 `IFolderView2.GetSortColumns` 都返回同一递归大小列及方向 `{B725F130-47EF-101A-A5F1-02608C9EEBAC}:10/1`。随后将焦点显式交给重建后的 `SysListView32`，模拟 `VK_DOWN` 后选中项和 focused item 都移动到下一行。两项均在 Private Desktop 中通过，前台 HWND 与输入 tick 未变。

在同一重建场景中，`IShellView.GetItemObject(SVGIO_BACKGROUND)` 仍返回 `IContextMenu`，内存 `HMENU` 的 `QueryContextMenu` 成功并生成 7 项；没有显示 popup 或执行命令。重建后的 `SVGIO_SELECTION` `IDataObject` 也通过 `IFileOperation.CopyItems` 把 `scroll-250.dat` 复制到本次运行独立临时目标，操作未取消、源文件保留且目标长度匹配。

该结果证明宿主可以把焦点重新交给新的文件列表并继续使用方向键，Shell 背景菜单对象可重建，选中项数据对象可用于原生复制；它不证明 Shell 会自动迁移旧 HWND 焦点，也不证明菜单可见交互、菜单命令路由、剪切/删除/重命名、拖放或通知都正常。仍需覆盖标签/导航命令路由及这些剩余合同。ExplorerBrowser 产品未改；这些 Private Desktop 探针没有可见 UI 验收。

后续专用 drop-target probe 在同一普通 filesystem `IShellView` 上先销毁再重建 view window，然后用新 HWND 注册的 `IShellFolder::CreateViewObject(IID_IDropTarget)` 接收重建后 source view 的选中项 `IDataObject`。`DragEnter/DragOver/Drop` 均返回 `S_OK`，Copy effect 与 source/target 文件状态均正确，前台/输入不变。`IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IDropTarget)` 返回 `E_NOINTERFACE`，所以本次证明的是宿主在新 HWND 重新注册 filesystem folder drop target 后可处理复制；它不是完整 OLE `DoDragDrop` 源端闭环，也不覆盖跨卷操作。第一次包含跨卷复制/移动的宽探针因 `IFileOperation` 的系统 `OperationStatusWindow` 抢到共享前台而被隔离守卫判失败；测试保持失败证据，没有弱化守卫。收窄为单个同卷复制后通过。具体记录见路线图。

重建后的目录通知探针另发现一项未通过合同：在 backing 文件夹写入新文件并发送 `SHCNE_UPDATEDIR` 后，view host 收到两次 `SFVM_FSNOTIFY` 并执行延迟刷新，但 3 秒后仍为 302 行；再显式调用 `IShellView.Refresh`，新项目仍未显示，provider `EnumObjects` 计数也未变化。用 `SHCNE_CREATE` 的首轮同样没有可见项目。首次尝试通过合成 folder `ParseDisplayName` 构造通知 PIDL 时 45 秒 watchdog 超时，阶段日志停在 Shell `Initialize`/backing parse；改用物理 backing 路径 PIDL 后不再卡住，前台/输入守卫通过，但刷新合同仍失败。由此只能确认通知回调到达，不能确认重建后的 DefView 会重枚举新项目；需继续检查通知到刷新/缓存失效的接口边界。watchdog 中止的一轮未执行 probe `finally`，留下该次 UUID 夹具和对应 HKCU 测试注册，清理动作被自动审批拦截，未尝试绕过。

随后用普通 filesystem `IShellView` 做对照：`SHCNE_UPDATEDIR` 将 301 行更新到 302 行，过程中列表未变为空，view/list HWND 均保持不变。合成 provider 的差异在 `SFVM_FSNOTIFY` 回调：原实现返回 `S_OK` 并排队 `IShellView::Refresh`，虽然回调到达且刷新函数返回，`EnumObjects` 仍为 0 次。Microsoft 对 `IShellFolderViewCB::MessageSFVCB` 的约定是 `S_OK` 表示回调已经处理消息，`E_NOTIMPL` 表示由系统 folder view 执行默认处理（[MessageSFVCB return values](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolderviewcb-messagesfvcb)）。将隔离 adapter 的 `SFVM_FSNOTIFY` 改为返回 `E_NOTIMPL` 后，滚动锚点/重建场景的通知测试重复一次通过：新文件可见、重新枚举 1 次、host 延迟刷新计数 0，探针 exit code 0，前台与输入守卫未变化。第二次功能断言仍通过，但探针运行期间前台从 WXWork 切到 LDPlayer，整轮按守卫失败保留，不能计作第二次隔离通过。该结论只适用于当前隔离 adapter/DefView 原型；尚未迁入产品 ExplorerBrowser，也没有验证颜色列、排序列或多 pane 通知。

修复探针 watchdog 的资源归属：每轮由 watchdog 生成 UUID，并通过环境变量把同一 UUID 交给 NativeProbe，作为该轮 CLSID 与夹具目录名；watchdog 只删除该 UUID 下、带有本项目归属标记的注册项和路径。它不会清理此前固定/旧 UUID 的遗留测试项；本次复核仍能看到 `{A17D2A61-4C0B-4B22-8E36-35D92CBC1170}` 与 `{67ba5919-8352-4a9b-83a4-9859d280f4fe}` 两组带项目标记的 HKCU 项，以及 `C:\Users\marvin\AppData\Local\Temp\ExplorerDirs-SyntheticShellSort`、`...-Copy` 和 `ExplorerDirs-SyntheticShellSort-8b8d4c6985804e7aaff40d0443230f25`、`...-Copy-8b8d4c6985804e7aaff40d0443230f25` 夹具目录。它们未被本批次触碰，需后续单独审视，不能因新 watchdog 运行通过而视为已清理。

### 2026-10-31：普通文件夹的 Shell 背景菜单来源

新增无窗口 `--filesystem-folder-background-menu` 探针，在带一个文件和一个子目录的临时 filesystem 位置上绑定真实 `IShellFolder`，直接调用 `CreateViewObject(IContextMenu)` 并将菜单构造到内存 `HMENU`。本机返回 `S_OK`，`QueryContextMenu` 也成功，顶层有 21 个菜单项；标签包含第三方扩展、“新建”和“属性”，没有视图级“查看、排序方式、分组依据、刷新”。没有创建 `IShellView`、窗口或可见 popup，也没有执行命令。

这说明普通文件夹父对象提供的 `IContextMenu` 不能作为自有行列表的 Explorer 背景菜单替代品；虚拟 Control Panel 的相同接口曾返回 0 项。若自有行视图要保留 Windows 原生视图命令，必须继续通过真实 Shell view 的视图级菜单路径或找到受支持的同等宿主接口；不能仅凭 `CreateViewObject` 成功宣称菜单兼容。该单机 filesystem 夹具没有证明所有 namespace/provider 的菜单内容。

### 2026-10-31：路径扫描器与句柄绑定扫描器微基准

为评估句柄绑定扫描能否改善扫描时延，使用临时本机文件夹在同一 .NET 进程中对照产品 `RecursiveDirectoryScanner` 与 NativeProbe 的 `HandleBoundRecursiveDirectoryScanner`。夹具为 350 个分支、700 个目录、2450 个文件、483700 B；每种扫描各运行 5 次，前 2 次不计，取后 3 次平均。两种扫描对字节数、文件/目录数和 unreadable 计数给出相同结果。暖缓存样本中，路径扫描平均 289.63 ms、托管分配约 1,349,224 B；句柄扫描平均 68.97 ms、托管分配约 2,449,645 B（约 4.2 倍速度、约 82% 更多托管分配）。

这是单机、浅层、暖缓存夹具的微基准，不是稳定性能承诺：执行顺序固定，样本少，没有测峰值/保留内存、Private Bytes、真实 F9 取消体验、UNC/网络文件系统或第三方 namespace provider。更快的结果值得进入下一轮跨夹具验证，但更高的托管分配和逐目录打开句柄也需要观察；当前没有证据证明它降低产品运行内存或适合取代 F9 路径。句柄扫描器仍留在 NativeProbe，产品 ExplorerBrowser/F9 行为未改变。临时基准程序位于忽略目录 `src/.state/scanner-ab-probe`，未加入版本控制。

### 2026-10-31：多行递归大小的单遍句柄扫描

上一节只比较一个根目录汇总，不能代表 Q-Dir 的“同一列表中多个文件夹各自显示递归大小”。NativeProbe 现在另有 `ScanDirectoryTree`，打开一个扫描根后只枚举一次，并在每个目录句柄关闭前保存请求行的递归摘要与该目录文件 ID；SettingsSmoke 用真实临时目录把根、子目录和兄弟目录结果逐项对照产品 `ScanDirectoryTreeForPaths`，数量、字节数、文件/目录数及身份均匹配。

本机 C: NTFS 夹具对照三种路径：产品单遍路径扫描、句柄单遍扫描、现有句柄扫描器对每个请求目录独立递归。每轮 7 次、前 2 次不计，顺序轮换；完整矩阵重复两轮。宽目录为 350 个可见兄弟目录、每支 4 层、3150 个文件；深链为 24 层并请求第一层子目录；多文件目录为 120 个可见兄弟目录、每个 25 个文件。全部三种路径在三个夹具的请求行摘要一致。初版句柄树扫描每遇到一个目录都查询一次父句柄路径；源码审查后改为优先使用已打开帧的路径，仅当子项打开失败或文件 ID 不符时才重新解析父句柄路径。更新后的两轮时延范围如下：

| 夹具 | 产品路径单遍 | 句柄单遍 | 句柄逐目录独立扫描 |
| --- | ---: | ---: | ---: |
| 宽目录，350 行 | 633–679 ms | 153–166 ms | 177–194 ms |
| 深链，1 行 | 13–15 ms | 5.5–6.2 ms | 5.1–5.4 ms |
| 多文件目录，120 行 | 676–1069 ms | 22–24 ms | 19–23 ms |

每轮托管分配在同一夹具内重复稳定：宽目录约 2.17 MB / 3.79 MB / 3.72 MB；深链约 0.052 MB / 0.082 MB / 0.080 MB；多文件目录约 1.23 MB / 0.487 MB / 0.460 MB，顺序对应上表三种路径。相较初版，路径缓存将句柄单遍分配降低约 25%–33%；宽/深夹具仍比产品路径版分配更多，多文件目录则更低。更新后句柄单遍在本机三个 NTFS 夹具上分别约快 4.1 倍、2.4 倍、31–45 倍；同级可见兄弟目录的逐目录独立扫描与句柄单遍耗时接近，因为子树互不重叠，不能笼统宣称单遍会显著减少工作量。绝对时延跨轮波动明显，样本仍是暖缓存、短时、单机托管分配测量，没有包含峰值/保留进程内存、Shell、UNC、第三方 provider 或真实 UI。

深链夹具还先暴露了旧句柄原型的 MAX_PATH 缺口：超过约 260 字符后，句柄打开子目录失败，导致 unreadable 增加、结果少算。现在 `CreateFileW` 对本地盘和 UNC 文件系统路径使用扩展路径前缀，句柄内部保留该前缀，结果字典再归一化为普通解析路径。新增超过 MAX_PATH 的 28 层临时目录回归在旧逻辑上失败，修正后通过。另一回归在扫描回调中移动打开中的父目录，确认扫描会在旧路径失效/身份不符后重新解析句柄路径，继续绑定到原目录对象。定向 `--shell-listview-row-model-smoke` 与递归扫描 smoke 均通过；递归 smoke 还确认列表中的 junction 结果计为 unreadable、不会跟随目标，junction 自身作为扫描根时同样不下钻。

结论：句柄数据通道对本机 NTFS 的逐行汇总有性能潜力，长路径与 junction 边界已在隔离扫描器覆盖；但访问拒绝、UNC 与第三方 Shell provider 仍未实测。Microsoft 文档把 SMB 3.0/TFO/Scale-out、CsvFS 与 ReFS 列入 `GetFileInformationByHandleEx` 的支持技术范围，实际 `FileIdExtdDirectoryInfo` 行为仍需在对应环境验证：[GetFileInformationByHandleEx](https://learn.microsoft.com/en-us/windows/desktop/api/winbase/nf-winbase-getfileinformationbyhandleex)、[FILE_ID_EXTD_DIR_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_extd_dir_info)。NativeProbe 原型没有接入 ExplorerBrowser、IShellView 产品视图或 F9；列展示、排序、焦点、菜单、拖放及逐项后台刷新仍未通过架构验收。

### 2026-10-31：.NET 低层文件系统枚举器对照

在忽略目录的临时 A/B harness 中增加 `FileSystemEnumerable<T>` 原型：枚举转换器只保留 `FileName`、`Attributes`、`Length`，递归扫描直接消费条目长度，不再为每个文件另调 `File.GetAttributes` 与 `FileInfo.Length`。三类本机 NTFS 夹具都与产品路径扫描及句柄扫描的请求行字节数、文件/目录/unreadable 摘要一致。Microsoft API 文档说明 `FileSystemEntry` 提供 attributes 与 length，适用于当前 .NET 10 项目：[FileSystemEntry](https://learn.microsoft.com/en-us/dotnet/api/system.io.enumeration.filesystementry?view=net-10.0)、[FileSystemEntry.Length](https://learn.microsoft.com/en-us/dotnet/api/system.io.enumeration.filesystementry.length?view=net-10.0)。

在高负载/缓存不稳定的一轮宽目录数据中，产品路径 / 句柄 / BCL 的时延分别为 1438 / 236 / 192 ms，托管分配为 2.17 / 3.79 / 1.97 MB。深链一轮分别为 108 / 67 / 62 ms、分配为 0.052 / 0.082 / 0.037 MB。多文件目录单独复测两轮分别为：产品路径 593–700 ms、句柄单遍 16–24 ms、BCL 11–19 ms；分配为 1.23 / 0.49 / 1.10 MB。BCL 原型在这些样本中比当前路径扫描少分配约 9%–28%，多文件目录用时远低于当前路径扫描，宽目录用时也略低于句柄原型；但机器绝对耗时在各轮之间波动很大，宽目录仅一轮，这些不是可发布性能保证。

这提供了一条不依赖 `FileIdExtdDirectoryInfo` 的候选路径，可能减少对特定 NTFS 扩展枚举接口的依赖；网络/provider 兼容尚未实测。原型还未覆盖完整生产合同：取消/进度回调、重解析点、超过 MAX_PATH、访问拒绝、目录被并发替换及文件在枚举后变化都需与现有扫描器逐项验证；BCL 条目长度是枚举时快照，不能替代句柄文件 ID 的竞态保护。临时原型没有进入 App 或 NativeProbe 产品源码，尚未改变 F9 与 ExplorerBrowser 行为。

### 2026-10-31：BCL 条目元数据接入产品递归目录树扫描

`RecursiveDirectoryScanner.ScanDirectoryTreeCore` 已改用 `FileSystemEnumerable<T>` 读取条目名、属性和长度，移除每个条目额外调用 `File.GetAttributes`、每个文件额外构造 `FileInfo` 取长度的路径。普通 `Scan()` 与 F9 使用的 `ScanSelection()` 未变。`RecursiveDirectoryScannerSmoke` 增加隐藏/系统条目、超过 MAX_PATH 的深层目录、目录树进度以及 120 个目录 × 每目录 25 个文件的分配预算回归；最终实现实测分配 261,232 B，低于预算 1,175,000 B；同一夹具旧路径实现曾测得 1,225,152 B，减少约 78.7%。该分配差异来自同进程托管分配测量，不代表进程工作集或 Private Bytes 会同比下降。

BCL 返回的长度和属性是枚举条目快照；文件在枚举后被删除或改变时，本次汇总仍可能使用已枚举的旧元数据。这不提供原子文件系统快照，也没有句柄扫描器的文件 ID 竞态保护。真实目录并发替换、UNC/网络文件系统、第三方 provider 和可见 Shell 列行为未在本批次验收；若要求严格对象身份，仍需使用隔离句柄扫描方案并完成其兼容性评审。此次只接入递归目录树扫描，不能据此声称 Q-Dir 逐行大小列已进入产品视图。

### 2026-10-31：重建视图真实 IFileOperation 通知复验（更新前述手动通知结果）

在重建后的 view 中选中临时 backing 文件并实际调用 `IFileOperation::RenameItems` / `DeleteItems`。`SFVM_FSNOTIFY` 原始 `lParam` 分别观察到 `0x1`（本机 SDK `SHCNE_RENAMEITEM`）与 `0x4`（`SHCNE_DELETE`）；两次操作都成功改变 backing 文件，但 DefView 没有重枚举，旧行仍在。探针补发父目录 `SHCNE_UPDATEDIR` 后，每次重枚举 1 次，重命名归一为唯一新行（303 行），删除归一到 302 行。另观察到 `0x40000`，本机 SDK 定义为 `SHCNE_FREESPACE`；`wParam` 只记录地址，没有解引用。

完整探针 exit code 0，前台/输入守卫不变、隔离资源清理完成；原生 DLL `/W4 /WX` 和 NativeProbe Release build 均 0 警告/错误。此结果补足前文：现在已验证真实 IFileOperation 产生的物理子项事件，但 backing 目录更新仍由探针显式补发；尚未实现 adapter 事件翻译，也未改变产品/迁移结论。日志：`src/.state/native-probe-watchdog/20261031-121600-7acc9070.stdout.log`。

### 2026-10-31：重建后的合成视图对子项变更通知

在“重建 Shell 窗口并保留滚动锚点”的 Private Desktop 场景中，先创建物理 backing 文件并向目录发送 `SHCNE_UPDATEDIR`：新文件显示，provider 重枚举 1 次，host 延迟刷新计数为 0。随后将文件改名并对物理旧/新绝对子 PIDL 发送 `SHCNE_RENAMEITEM`：3 秒内没有替换旧行，列表短暂达到 305 行，provider 重枚举 0 次。对删除后的物理子项 PIDL 发送 `SHCNE_DELETE` 也没有移除旧行，重枚举 0 次。紧接着向合成视图 backing 目录 PIDL 补发 `SHCNE_UPDATEDIR` 后，重命名收敛为唯一的新名称（303 行，枚举 1 次），删除收敛为 302 行（枚举 1 次）。

探针 exit code 0，`foreground-unchanged=True`、`input-change-observed=False`、`isolation-clean=True`；NativeProbe Release 构建 0 警告/错误，`git diff --check` 通过。证据支持的结论仅限当前合成 provider：物理子项 PIDL 的 rename/delete 通知没有对应到已有合成行；父目录更新触发重枚举并归一结果。没有证明真实文件操作后 Shell 是否会自动发出所需父目录通知，也不能外推到普通 ExplorerBrowser。该探针不构成产品功能或 IShellView 迁移准入证据。日志：`src/.state/native-probe-watchdog/20261031-120852-6807f5c2.stdout.log`。此前错误使用默认通知策略的一轮卡在销毁父窗口的 COM 清理阶段，未到新断言，不能算通知结果；其前台/输入未变且本轮 UUID 资源已清理，日志 `src/.state/native-probe-watchdog/20261031-120738-35165b47.stderr.log`。

### 2026-10-31：隔离 adapter 把 rename/delete 转成父目录失效

先将重建视图后的真实 `IFileOperation::RenameItems` / `DeleteItems` 断言改为“不再手动补发目录更新”。RED 轮再次复现 rename 后旧行仍在、provider 枚举增量为 0，watchdog exit 1，前台 HWND 和输入 tick 未变，隔离资源清理完成。随后仅在 NativeProbe 合成 adapter 的显式开关下，对 `SFVM_FSNOTIFY` 的 `SHCNE_RENAMEITEM` 与 `SHCNE_DELETE` 排队 1 ms timer；timer 返回到 Shell 回调后向 backing 目录发送一次 `SHCNE_UPDATEDIR`。仍将原子事件交给 DefView 默认处理；`UPDATEDIR` 不属于翻译事件，因此不会自我触发循环。`wParam` 仍仅记录、不解引用。

同一 Private Desktop 场景连续四轮均通过：rename 与 delete 分别各自只排队 1 次、发送 1 次，provider 每次重枚举恰好 1 次；行数由 302→303→302，名称更新/删除正确。每轮 watchdog exit 0、前台 HWND 与输入 tick 不变、UUID 注册和夹具清理完成。日志：`20261031-122237-3de21bf7.stdout.log`、`20261031-122255-52ee060f.stdout.log`、`20261031-122302-61e77667.stdout.log`、`20261031-122516-4da9373f.stdout.log`（均在 `src/.state/native-probe-watchdog/`）。最终版本 Native adapter `/W4 /WX` 与 NativeProbe Release build 均为 0 警告/错误；DLL unload contract 与无窗口 `--static-shell-column-folder-smoke` 通过。

这只证明在本合成 provider 中，真实文件操作通知可以经延迟父目录失效更新重建后的 DefView，且本覆盖的 rename/delete 不循环、不重复枚举。没有覆盖 create/update/move、多次并发事件合并、剪切/撤销、菜单命令、导航、拖放源端或其他 namespace/provider；产品 ExplorerBrowser 未改，IShellView 架构门槛仍未通过。后续优先在 Private Desktop 补测 create/move 与成组通知合并，再继续 Shell 命令路由、导航和完整拖放闭环；结果只作为迁移门槛证据，不得直接接入产品。

### 2026-10-31：隔离 adapter 的 create/move 通知复核

先在未开启事件转换时，对真实物理新文件发送 `SHCNE_CREATE`。RED 轮确认 `SFVM_FSNOTIFY` 收到 `lParam=0x2`，但 provider 枚举增量为 0、新行不可见；watchdog exit 1，前台/输入守卫未变、夹具和临时注册清理完成。之后把 `SHCNE_CREATE` 纳入同一显式实验开关，并继续用真实 `IFileOperation::MoveItems` 将该行从合成 view backing 根目录移入其 `child` 子目录。Move 的源 view 回调为 `SHCNE_DELETE` (`0x4`)；转换后的父目录更新让源行消失，目标文件内容保留。

包含 create、move-out、rename、delete 的完整 Private Desktop 场景连续三轮通过。四种情况各自只排队并发送 1 次父目录 `SHCNE_UPDATEDIR`，各触发 1 次 provider 重枚举；创建显示、移动/删除行移除、重命名唯一新行可见。每轮 watchdog exit 0、foreground/input 不变、隔离注册与临时夹具清理完成。日志：`20261031-122840-d195bf2e.stdout.log`、`20261031-122913-493391c2.stdout.log`、`20261031-122924-189dbab9.stdout.log`（`src/.state/native-probe-watchdog/`）。Native adapter `/W4 /WX` 和 NativeProbe Release build 无警告/错误；DLL unload contract、无窗口 `--static-shell-column-folder-smoke` 通过。

范围仍是 NativeProbe 的合成 filesystem-backed provider，实验开关默认关闭。这里只验证单个源 view 对 create、move-out、rename、delete 的行同步；未验证移动目标 view、跨卷/第三方 provider、多事件 burst 合并、撤销、跨窗格剪切粘贴或 OLE 拖放闭环。产品 ExplorerBrowser 和 release 未改，IShellView 迁移门槛仍未通过。下一步评估多个变更事件的合并/顺序，并继续命令路由、导航、菜单及真实拖放源端门槛。

### 2026-10-31：三项 create burst 的通知合并

在真实 Private Desktop `IShellView` 中，先写入三个 backing 文件，再连续发送三次物理子项 `SHCNE_CREATE`，中间不泵消息。DefView 随后还向 view callback 发出若干 `SHCNE_UPDATEITEM`，但合成 adapter 的 1 ms 队列将该 burst 合并为一次父目录 `SHCNE_UPDATEDIR`；provider 只重枚举一次，三个新行均出现。完整场景连续三轮通过，队列/发送/枚举计数均为 `1/1/1`，Private Desktop 前台、输入 tick 和隔离清理守卫通过；日志：`20261031-123047-758558ac.stdout.log`、`20261031-123115-f2d20944.stdout.log`、`20261031-123128-6528e808.stdout.log`。

这是同一 STA 上连续发出的三个 CREATE 事件，不代表并发 provider 事件、慢网络目录、不同窗格或任意长 burst。当前逻辑仍只在 NativeProbe 合成 adapter 中且默认关闭。没有产品接线，也没有用户可见的刷新/闪烁结论。下一步继续核对父目录更新通知数组/事件字段和 timer 生命周期在关闭 view 时的取消路径，然后进入完整命令路由与拖放源端验证。

### 2026-10-31：延迟父目录更新计时器的取消生命周期

给隔离 adapter 加入仅供 NativeProbe 使用的按 view 排队/取消接口。同步排队一次 backing-directory update 后，在泵 STA 消息前立即取消，再泵 400 ms；三次连续 Private Desktop watchdog 均观察到队列 `1`、发送 `0`、provider 枚举 `0`，取消返回 `S_OK`。这证明当前 STA 计时器在 owner 关闭 view 前显式取消时不会迟到发送更新，且计时器持有的 folder 引用会由取消路径释放。完整日志：`20261031-123706-16b139a2.stdout.log`、`20261031-123730-3aff15f5.stdout.log`、`20261031-123741-8079b46c.stdout.log`；前台与输入守卫均通过。

首轮取消尝试在立即读取队列计数前调用外部 `SHChangeNotify`，Shell 回调尚未同步到达，故队列为 0；该轮没有触及取消断言，不作为生命周期失败或通过证据。当前三轮使用同步探针接口精确验证已排队计时器的取消，真实 IFileOperation 的 create/move/rename/delete 映射仍由前述独立场景覆盖。adapter 与探针都仅在 NativeProbe；产品没有新增计时器或 Shell 视图行为。下一步检查 view hold 释放与窗口销毁的 owner 调用顺序，再继续完整命令/菜单/导航/OLE 拖放迁移门槛。

随后将 timer 验证移到真正的 view-window teardown：RED 轮先排队、不取消、销毁窗口并泵消息，稳定复现一次迟到的 `SHCNE_UPDATEDIR`（`directory-updates-after-destroy=1`）。加入销毁前按 view 取消后，连续三轮均为 `0`，Private Desktop 前台/输入/清理守卫通过；日志：`20261031-124311-d534ae63.stdout.log`、`20261031-124323-615655c0.stdout.log`、`20261031-124335-9fbfaed6.stdout.log`。这证明隔离 host 的关闭顺序必须先撤销 backing-directory timer，再调用 `DestroyViewWindow`；它不验证跨线程竞态，也不提供产品 host 的接线证据。

### 2026-10-31：慢速派生列状态的 DefView A/B 探针准备

为验证 `IShellFolder2::GetDefaultColumnState` 上的 `SHCOLSTATE_SLOW` 是否影响 DefView 的派生目录大小列更新，NativeProbe 增加了一个仅供 Private Desktop 使用的 A/B 入口。基线入口（未设置 slow flag）在初始视图中由 `GetDetailsOf` 读取目录/文件值 `10/4`，并按自定义大小列完成升序/降序；同一运行的后续单项目录更新后，自定义列从 DefView 列集合中消失，列索引变为 `-1`，未再请求行值。基线日志为 `src/.state/native-probe-watchdog/20261031-125438-a4c0a827.stdout.log`。

该基线运行随后在销毁父窗口阶段超过 watchdog 时限，且期间系统输入 tick 变化；watchdog 失败关闭并清理了本次 UUID 夹具和注册项。因此初始显示/排序只是阶段性观察，整轮不是通过，不能据此下结论说更新丢列根因已确认。`SHCOLSTATE_SLOW` 变体已接线并通过 NativeProbe Release 构建，但本轮没有运行该变体；在获得隔离桌面及显式运行授权前，不启动同类 Private Desktop view 探针。产品 ExplorerBrowser 未改，派生列也未进入产品。

### 2026-10-31：`SHCOLSTATE_SLOW` DefView 更新 A/B

运行已准备的 slow-column 变体后，初始两行递归列仍显示 `10/4`，自定义列升序与降序均能经 `CompareIDs` 排序。单项目录更新后，读回的列索引仍为 `-1`，没有列值更新、比较器调用或重新枚举；因此 `SHCOLSTATE_SLOW` 没有解决更新后的列丢失。与未设置标志的基线相比，slow 变体在更新阶段多触发两次 `GetDetailsOf` 取值请求，但 DefView 没有保留可更新的列索引。两个样本都保持布局及选区/焦点；这只是合成 provider 的单次 A/B，不能外推到普通 ExplorerBrowser 或其他 provider。

该运行同样卡在 `cleanup-destroy-parent-window-1`，45 秒 watchdog 将探针进程终止；超时前 Private Desktop 前台 HWND 和输入时间戳保持不变，等待链包含 COM activation、`svchost` 与 `ChsIME`。脚本后验确认探针进程、唯一 CLSID/NameSpace 注册项和两处临时夹具均已清理。此次只采纳超时前完成的列更新观测，不将清理超时归因于列或产品故障。日志：`src/.state/native-probe-watchdog/20261031-130931-e15f8442.stdout.log`、`.stderr.log`。这项证据不支持把 `SHCOLSTATE_SLOW` 接入产品；逐行派生列仍需要自有视图/namespace 架构评审与完整 Shell 兼容门槛。

### 2026-10-31：WPF 根宿主清理与更新策略对照

同一递归大小列探针改由 WPF `HwndSource` 作顶层 parent，默认单项 `SHCNE_UPDATEITEM` 连续三轮均正常返回：前台 HWND/输入时间未变、隔离清理通过；三轮更新后都仍读到列索引 `-1`、无行值更新/比较/重枚举，选区与焦点保持。与先前 native `STATIC` 顶层 parent 多轮停在父窗口销毁阶段的结果相比，这三轮表明顶层宿主类型与探针清理结果高度相关；尚不能据此断言 WPF 本身是根因，也不代表产品 ExplorerBrowser 或完整 IShellView 迁移已经通过。日志：`20261031-131149-8fbe9a12.stdout.log`、`20261031-131208-6ca906c2.stdout.log`、`20261031-131215-871e617b.stdout.log`。

在该 WPF 根宿主下，`SHCNE_UPDATEITEM` 后追加父目录 `SHCNE_UPDATEDIR` 的一轮也通过前台/输入及清理守卫，但没有恢复列值或触发行枚举/排序；日志：`20261031-131251-10b7f216.stdout.log`。另外两种“通知后重新应用自定义列”与“再切换 Icon→Details”变体中，`IColumnManager.Apply` 返回成功且列集合重新包含递归大小键，但 DefView 仍给出 `value-column-index=-1`，没有重新取值、比较或枚举行；二者均保持选区/焦点且通过隔离守卫。日志：`20261031-131427-c183ea37.stdout.log`、`20261031-131442-a7585e80.stdout.log`。这些结果不支持继续堆叠同类通知、列重放或视图模式切换补丁。

`IShellView.Refresh()` 对照在超时前观察到两行重新枚举和一次比较/行序变化，但同时前台从 `msrdc` 变为 Explorer、输入时间戳变化，守卫将该轮标为无效，且焦点/选区未保持；这不构成刷新策略有效的证据，也不重跑该场景。唯一 CLSID/NameSpace 注册与临时夹具事后均确认不存在，探针进程已退出。日志：`20261031-131258-4a9de183.stdout.log`。WPF 根宿主下六轮被守卫接受的探针均正常清理；前台/输入变化轮次仍不采纳。现有证据把后续研究重点转向合成 namespace/自有视图的数据与绘制路径，并继续验证 Shell 命令、导航、菜单、文件操作和真实 OLE 拖放合同；不接入产品 ExplorerBrowser。任何带前台/输入变化的轮次均不用于行为结论。

### 2026-10-31：WPF 根宿主下的通知/列重放策略收敛

继续在 WPF `HwndSource` Private Desktop 宿主对照 DefView 单项更新策略。新增 `SHCNE_DELETE` 后紧接同一绝对子 PIDL 的 `SHCNE_CREATE` 变体：初始目录/文件大小列 `10/4` 与排序基线成立；通知后自定义列索引为 `-1`，大小列没有更新、排序比较或行枚举。选区/焦点保持，前台 HWND 与系统输入 tick 不变，watchdog 正常退出且 UUID 注册项与夹具清理完成。日志：`src/.state/native-probe-watchdog/20261031-131600-fa387cce.stdout.log`。

结合此前 WPF 根宿主的 `UPDATEITEM`、`UPDATEITEM+UPDATEDIR`、列重放、Icon→Details 模式切换结果，当前受守卫接受的策略矩阵没有找到能让 DefView 保留并重新取值的办法。`IShellView::Refresh` 样本因前台/输入守卫变化而无效，不据此下结论，也不重复该场景。继续堆叠 DefView 通知、重放列或切换模式已没有证据价值；下一步应在隔离自有行模型中评估视图数据/绘制，同时列出并验证视图级菜单、排序/分组、键盘焦点、导航、Shell 文件操作、通知和 OLE 拖放兼容门槛。产品仍使用 ExplorerBrowser，未接入派生大小列，也未进行架构迁移。

### 2026-10-31：包装多选原生菜单扩展接口

在包装子 PIDL 的两项目录多选场景中，适配器的 `GetUIObjectOf(IContextMenu)` 返回对象成功 `QueryInterface(IContextMenu3)`；内存 `QueryContextMenu` 返回 103 个命令 ID、25 个菜单项，并包含 `copy` 与 `delete` canonical verb。探针只查询并构造内存菜单，没有显示菜单、转发窗口消息或执行命令；前台不变。Release NativeProbe 构建 0 警告/错误，`--static-shell-column-wrapped-folder-smoke` 完整通过。

随后对菜单中 5 个子菜单直接调用 `IContextMenu3::HandleMenuMsg2(WM_INITMENUPOPUP)`：4 个返回 `S_OK`，1 个返回 `E_NOTIMPL`，没有其它失败；三轮两项选择（初始、复制后、移动后）结果一致。菜单仍未显示，前台未变。

这补充证明当前 Windows 本机文件 Shell 菜单对象在多选包装 PIDL 后可查询 `IContextMenu3`，并能处理 4/5 个内存子菜单的初始化消息。第 5 个的 `E_NOTIMPL` 上层如何处理、真实窗口消息转发、`WM_DRAWITEM`、`WM_MEASUREITEM`、菜单边界/视觉尚未验证；第三方扩展与多 provider 也未覆盖。产品仍使用 ExplorerBrowser，未做宿主迁移。

### 2026-11-01：peer 文件系统菜单初始化 HRESULT 对照

在隔离 `IShellView` peer 行选中单项与双项后，分别为完整菜单及 `CMF_DEFAULTONLY` 菜单进入原生 popup 消息循环并立即取消；另枚举完整菜单的子菜单，直接记录 `IContextMenu3::HandleMenuMsg2(WM_INITMENUPOPUP)`。单项完整菜单有 9 个子菜单，回调 HRESULT 分布为 5 个 `S_OK`、2 个 `E_NOTIMPL`、1 个 `S_FALSE`、1 个 `E_FAIL`；双项菜单有 6 个，分别为 4 个 `S_OK`、2 个 `E_NOTIMPL`。但根 popup 的 `WM_INITMENUPOPUP` 在单选、多选和 `CMF_DEFAULTONLY` 下均返回 `E_FAIL`；该 flag 仍构造 22 个顶层项，未形成无扩展的干净对照。所有 popup 均取消且未调用命令，前台和输入守卫未变化，Private Desktop 清理通过。NativeProbe Release 构建 0 警告/错误。日志：`src/.state/native-probe-watchdog/20261101-205609-2298ae89.stdout.log`。

这表明失败不是所有子菜单回调的一致结果，但尚不能判定根 popup `E_FAIL` 是由 Windows Shell、某个第三方扩展或消息参数/路由引起；直接初始化子菜单也不等于实际打开子菜单时的窗口消息处理。产品仍使用 ExplorerBrowser，此探针未改产品菜单路由，也没有可见菜单验收。

### 2026-10-31：初始 Shell 文件夹行缺少扫描身份，通知到达时先废弃旧 ticket

当前隐藏行模型合同复跑在初次读取 `RecursiveSummary` 时抛空引用。定位到原因是 `ShellListViewEnumerator` 构造目录行时没有填 `FileSystemIdentity`，而 `ApplyRecursiveScanResults` 按既定安全合同要求行快照、句柄扫描结果及发布时路径三者身份相同；旧扫描结果因此被清空。现在仅对有文件系统路径且带 `SFGAO_FOLDER` 的行读取卷序列号及 128 位文件 ID，并随行快照保存。隐藏相对大小合同先在该改动前复现失败，修正后初始与更新值、比例排序和焦点/选区均通过。

另一处时序缺口位于隔离相对大小原型的 `FileSystemWatcher.Changed`：旧扫描完成结果若已排入 owner Dispatcher，watcher 后续排入的重扫动作可能排在它之后。现在首个变更回调先从 watcher 线程调用 `DirectoryTreeScanCoordinator.InvalidateCurrent`，再通知 owner 排入新扫描。SettingsSmoke 覆盖已排队结果的取消与拒绝发布；`--common-listview-relative-size-contract` 和 `--shell-change-reconciliation` 均通过，前台未变。

逐目录文件 ID 读取增加了行枚举开销，本轮没有测量大量目录下的成本。以上仍是 NativeProbe/隔离行模型工作，不接入 ExplorerBrowser/产品 Shell view；对可见列、第三方 namespace 或产品迁移不作结论。

### 2026-10-31：同路径替换目录拒绝旧 provider 快照

NativeProbe 的递归大小快照此前只按 Shell 绝对解析名匹配。即使新目录与旧目录占用相同路径，旧快照仍会命中。现在 `SyntheticChild` PIDL 保存目录的卷序列号和 128 位文件 ID；快照仍按解析路径限为一项，但读取时还要求 PIDL 身份匹配。`FILE_ID_INFO` 不可用或打开目录身份失败时不使用递归快照，回退当前行枚举大小。这样避免为每次排序比较重复打开目录句柄，也避免删除目录后仅按文件 ID 留下不断累积的快照。

回归在临时 fixture 中先给 `child` 行缓存 7 B 快照，再把目录移到旁路名称、在原路径新建含 17 B 文件的新目录；旧实现把 7 B 读给新目录，修正后列文本读回 17 B。Native adapter `/W4 /WX` 构建、无窗口 `--static-shell-column-folder-smoke` 和 Release `SettingsSmoke` 通过；全量 smoke 覆盖初始扫描、更新、重新枚举、provider 重建及新对象拒绝旧缓存。探针没有创建 Shell view 或可见窗口。该方案只校正隔离 provider 的缓存身份，不是产品行模型/ExplorerBrowser 接线证据；不支持 `FILE_ID_INFO` 的文件系统无法复用该递归快照，网络/provider 与产品 UI 仍待另外验证。

### 2026-10-31：句柄扫描中的 ABA 路径往返

此前“路径短暂指向替代目录后再恢复”的未闭合记录针对只有路径/汇总的旧结果形状。当前 `HandleBoundRecursiveDirectoryScanner` 已返回它实际打开的根目录文件 ID；补充的 Release 行模型回归验证：扫描打开 6 B 原目录后，将它改名，在原路径建立 10 B 替代目录；当扫描进入原目录的子目录前，再把替代目录移开并将原目录移回原路径。扫描仍从原根句柄读取 6 B，结果携带原对象身份；发布时 Shell 行身份与当前路径身份均再次匹配原对象，因此正确接受 6 B。`--shell-listview-row-model-smoke` 与完整 Release `SettingsSmoke` 通过。

这关闭的是 NativeProbe 句柄绑定扫描对该根目录 ABA 时序的数据归属问题；不将结论扩大到旧的路径扫描器、未绑定身份的汇总结果或所有文件系统/provider。隔离行模型和扫描器仍未接入产品 ExplorerBrowser/F9，产品逐行列、异步 UI 接线及 Shell 视图兼容门槛仍开放。

## 2026-10-31：自有行模型跨窗格移动协调

`ShellChangeReconciliationProbe` 新增两个独立文件夹 pane 与两份隐藏 `SysListView32` 投影。探针从源 `IShellFolder` 一次取得两个选中项的 `IDataObject`，调用 `IFileOperation.MoveItems` 移到目标 Shell 文件夹；没有手动发送 `SHChangeNotify`。递归 Shell watcher 收到两组 DELETE/CREATE 移动事件后，探针分别重新枚举两侧并独立执行 `ShellListViewRowModel.Reconcile`。结果确认源 pane 退休两条移出行、目标 pane 新增两行；两侧未移动的锚点都保留稳定行 ID、选中状态和焦点。Release NativeProbe build 0 警告/错误，`--shell-change-reconciliation` 通过，前台句柄不变且两组列表/宿主窗口均隐藏。

这覆盖了两项多选 `IDataObject` 的 `IFileOperation.MoveItems` 结果、Shell 通知及两份自有行快照对跨窗格移动的协调，不覆盖实际进度/Undo UI、真实 `DoDragDrop`、产品 ExplorerBrowser 或用户可见输入；文件移动仅作用于隔离 fixture。行 ID 在各 pane 内独立，目标 pane 可以合法复用源 pane 的数值 ID。产品视图迁移仍未完成。

### 2026-10-31：自有行模型跨窗格复制协调

在同一隔离合同中，先完成上述多项跨窗格移动，再从接收窗格以真实 Shell `IDataObject` 调用 `IFileOperation.CopyItems` 回复制到原窗格。`SHCNE_CREATE`/目录更新通知后，原窗格重新枚举并新增两行；已复制窗格的两行身份未变，两个窗格原有锚点行的 ID、选择和焦点均保持。完整 NativeProbe Release build 与 `--shell-change-reconciliation` 连续三轮退出码 0，隐藏窗口/前台守卫均通过。

该结果仅验证文件系统 Shell 通知与两份 NativeProbe 自有行快照的复制/移动协调；没有验证真实 OLE 拖放、剪贴板粘贴、进度/撤销界面、ExplorerBrowser 产品宿主或可见用户交互。产品文件视图仍使用 ExplorerBrowser，迁移门槛未关闭。

### 2026-10-31：自有行模型跨窗格删除协调

在上述跨窗格移动与复制后，从接收 pane 的真实 `IShellFolder` 获取两条复制行的 `IDataObject`，调用 `IFileOperation.DeleteItems` 删除临时目标文件。探针没有手动发送 `SHChangeNotify`；递归 Shell watcher 收到两条 `SHCNE_DELETE`，重新枚举目标 pane 后执行行快照 reconcile。测试先在“只重新枚举、尚未更新行快照”的 RED 门失败；加入 reconcile 后，目标 pane 从 3 行收敛为锚点 1 行，两条复制行身份退休，锚点行 ID、选择和焦点保留；源 pane 的原文件和行身份未受影响。定向 NativeProbe Release 运行连续三轮通过，父/list 窗口隐藏且前台 HWND 未变化。

这补上的是本地文件系统、同 STA、隐藏自有行投影下的真实 Shell 多选删除通知闭环。未覆盖回收站语义、undo/redo、可见确认/进度 UI、剪贴板、OLE 拖放、其它 namespace/provider 或 ExplorerBrowser 产品接线；不能据此认定文件区自有视图已可替换产品 Shell view。

### 2026-10-31：自有行模型跨窗格重命名协调

在跨窗格复制后，对接收 pane 中一条复制行取真实 Shell `IDataObject`，调用 `IFileOperation.RenameItems` 将其改名。没有手动发送通知；递归 watcher 依次观察 `SHCNE_RENAMEITEM` 与 `SHCNE_UPDATEITEM`。重新枚举后通过 Shell 提供的新旧绝对 parsing name 映射调用 `ShellListViewRowModel.Reconcile`，重命名行保留原稳定 ID，目标锚点 ID、选择和焦点保持；之后继续对两条副本执行 `DeleteItems`，整体 move/copy/rename/delete 场景 Release NativeProbe 连续三轮通过，隐藏窗及前台守卫通过。

这验证单项重命名的文件系统通知及自有行身份连续性，不覆盖冲突重名、多选同名规则、undo/redo、可见 Shell 菜单、真实键盘/重命名编辑器、OLE 拖放、虚拟 provider 或 ExplorerBrowser 产品接线。`RetiredRows` 是旧快照 PIDL 的释放集合，不能直接解释成行身份已退役；身份连续性应比较 reconcile 后的稳定 ID。

### 2026-10-31：隔离属性变更通知分类

NativeProbe 的 synthetic folder adapter 此前只把 CREATE、RENAMEITEM、DELETE 和 UPDATEITEM 转成父目录失效通知；`SHCNE_ATTRIBUTES` 未覆盖。该事件表示 Shell 项目或文件夹属性发生变化，可能影响自有行模型的展示规则，例如只读属性颜色。先在 `--synthetic-child-notification-routing-smoke` 加入 ATTRIBUTES 断言，旧 adapter 按预期失败；随后纳入相同的延迟父目录更新路径。CREATE/RENAMEITEM/DELETE/UPDATEITEM/ATTRIBUTES 分类均通过，UPDATEDIR 和未知事件仍拒绝转换；原生 adapter `/W4 /WX` 与 NativeProbe Release build 通过，0 警告/错误。

本轮没有创建 Shell view，因此只证明事件分类合同与编译，不证明 Windows 实际发出该事件后 DefView 是否重枚举、属性是否更新或颜色是否刷新。该逻辑仅属于隔离 NativeProbe adapter，不接入 ExplorerBrowser 产品。[Microsoft SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify) 明确将 `SHCNE_ATTRIBUTES` 定义为项目或文件夹属性变化。

#### 后续 Private Desktop 验证尝试（未通过）

随后尝试将 `SHCNE_ATTRIBUTES` 从分类推进到真实隔离 Shell view：临时文件由普通项改为 Hidden，发送属性通知，观察合成 view 是否重新枚举并移除该行。旧分类（不含 ATTRIBUTES）和恢复后的分类各运行一次；两次都在 `cleanup-destroy-parent-window-1` 阶段由 watchdog 超时终止，等待链包含 COM/IME 线程，外层前台基线为 `LockApp` 且未变化。由于 watchdog 在父窗口销毁阶段终止进程，现有输出不足以确认属性行断言结果；不能视为通过，也不能据此归因产品 Shell 行为。该临时场景已撤回。无窗口分类合同及 wrapped-folder 合同随后通过，前台未变。探针异常退出留下运行 ID `a8d40fb43d464bd5a749ca388cd0b96d`、`5b210403d73d4f8585c05c3d4087ce2f` 对应的临时 fixture 与 HKCU 注册项；精确清理命令被自动策略拒绝，未尝试绕过。后续需先在允许的运行条件下解决 Private Desktop teardown，再重复视图级验证。

#### 2026-10-31：Shell 属性通知到隔离行模型的闭环

无 `IShellView` 的 `--shell-change-reconciliation` 现监听 `SHCNE_ATTRIBUTES`。真实临时文件从普通属性切为只读后，通知到达，重新枚举取得 `SFGAO_READONLY`（行属性 `0x40440177`）；恢复原属性后第二次通知也到达，标志清除。两轮 reconcile 均保留属性行 ID、稳定锚点的选中/焦点及隐藏 `SysListView32` 投影；完整跨窗格移动/复制/改名/删除合同继续通过，前台未变化。只验证 Windows Shell 通知、`IShellFolder` 重枚举及 NativeProbe 行模型，不验证 synthetic adapter 到 DefView 的消息转发、可见列着色或 ExplorerBrowser 产品接线。

#### 2026-10-31：合成 IShellView 接收属性通知

此前静态父窗口宿主在 teardown 阶段超时；本轮使用已能稳定销毁的 WPF `HwndSource` 根宿主和 Private Desktop 重跑隔离 view。先临时移除 adapter 对 `SHCNE_ATTRIBUTES` 的分类：事件专属命中数保持 0，后续 Shell 单独投递的 `SHCNE_UPDATEITEM` 不能冒充属性事件。恢复分类后，隐藏属性变化的 `SHCNE_ATTRIBUTES` 命中数为 1，adapter 延迟父目录更新队列/发送各增加 1；隐藏项仍留在行中，DefView 的 `EnumObjects` 增量为 0。view 包含隐藏项且 DefView 可直接更新项目，因此本轮只证明属性事件通过 adapter 转发，不证明重枚举行或隐藏项过滤行为。

完整 `single-item-recreate-shell-window-scroll` Private Desktop 探针 exit 0：此前已有的 move、rename、delete、取消待处理计时器检查继续通过；前台 HWND 与输入 tick 不变，UUID 注册和临时夹具清理通过。Native adapter `/W4 /WX`、NativeProbe Release build 及 `--synthetic-child-notification-routing-smoke` 通过，0 警告/错误。日志：`src/.state/native-probe-watchdog/20261031-234029-1a879d22.stdout.log`。这没有修改产品 ExplorerBrowser，也没有证明只读属性文字/颜色更新、DefView 视图重枚举或可见 UI 体验。先前的 Private Desktop teardown 超时限制已由 WPF 根宿主路线解决，静态父窗口路径仍不作为此类探针的首选。

### 2026-11-01：隔离 adapter 覆盖文件夹生命周期通知

按 Windows `SHChangeNotify` 的事件定义，文件夹新建、删除和重命名分别使用 `SHCNE_MKDIR`、`SHCNE_RMDIR`、`SHCNE_RENAMEFOLDER`，而 `SHCNE_CREATE`、`SHCNE_DELETE`、`SHCNE_RENAMEITEM` 对应非文件夹项目。隔离 synthetic adapter 的父目录更新分类此前只包含后者，因此创建/删除/重命名文件夹可能让自有行投影保留旧项目。现在分类补入三种文件夹事件，并忽略可选的 `SHCNE_INTERRUPT` 修饰位后再判断基础事件；更新仍延迟到父目录，`SHCNE_UPDATEDIR` 仍排除以避免递归通知。

Native adapter 通过 `/W4 /WX` 编译。按当前后台优先边界，本批未运行 smoke 或 Shell view 探针，因此只确认头文件常量和代码可编译；具体文件夹操作通知是否通过 synthetic adapter、是否只触发一次父目录重枚举，仍待后续隔离运行确认。产品 ExplorerBrowser 未改，也没有可见 GUI 验收。

### 2026-11-01：synthetic IShellView 文件夹生命周期通知闭环

在前一批仅完成事件分类和编译后，本批补齐了静态分类合同与真实隔离 view 操作。无 Shell view 的 `--synthetic-child-notification-routing-smoke` 确认 `SHCNE_MKDIR`、`SHCNE_RMDIR`、`SHCNE_RENAMEFOLDER` 均转为父目录更新，带 `SHCNE_INTERRUPT` 的 MKDIR 也能识别，`SHCNE_UPDATEDIR` 和卷空闲空间事件不被递归翻译。WPF 根 Private Desktop 的 `single-item-recreate-shell-window-scroll` 原型中，真实 `IFileOperation::NewItem`、`RenameItems`、`DeleteItems` 依次创建、改名和删除临时文件夹；Shell 分别投递 MKDIR/RENAMEFOLDER/RMDIR，适配器每种操作只排队一次并发送一次父目录更新，DefView 每次只重新枚举一次，行列表准确新增/改名/移除。完整探针退出码 0，Private Desktop 前台 HWND 与输入时间戳不变，isolated cleanup 通过。

该结果证明此 synthetic adapter 的文件夹生命周期通知在当前 Windows 文件系统 provider 和 WPF 根 Shell view 中可闭环。它不覆盖其它 provider、真实产品 ExplorerBrowser、可见 UI、Undo/Redo 或 OLE 拖放；没有产品迁移或 release 变更。
## 2026-11-01 隔离 IShellView 接收移入及复制文件通知

在已覆盖合成 view 文件夹新建、改名、删除后，补充从旁侧临时目录向当前 backing folder 移入整个文件夹的真实 `IFileOperation::MoveItem`。源夹具含 `child.txt`，移动后检查源路径消失、目标子文件内容一致且目标 view 出现目录行。NativeProbe 输出确认 Windows 对该目标报告 `SHCNE_MKDIR (0x8)`；synthetic adapter 延迟父目录更新各排队/发送一次，provider 只重新枚举一次。Private Desktop watchdog exit 0，前台 HWND、输入 tick 不变，UUID 注册项与临时夹具清理通过。

这个场景证明的是隔离 filesystem provider 中的文件夹移入通知路径；不覆盖真实 OLE `DoDragDrop`、ExplorerBrowser 产品、其它 Shell provider、Undo/Redo 或可见文件列表。产品仍使用 ExplorerBrowser；移入目标的用户交互和跨窗格拖放仍待集中 GUI 验收。

同一 probe 随后从旁侧 fixture 调用真实 `IFileOperation::CopyItem`，将 `notify-copied-in.txt` 复制到当前 backing folder。Shell 报告 `SHCNE_CREATE (0x2)`；一次父目录更新后视图新增文件一行，源文件保留且目标内容匹配。连续 probe 的 `enum-calls=1`、`translated-queued=1`、`translated-sent=1`。这只补充目标 view 文件复制通知合同；不验证剪贴板粘贴、进度/冲突对话框、OLE 拖放或产品 ExplorerBrowser。

再以真实 `IFileOperation::MoveItem` 将旁侧临时文件移入同一目标 view。Shell 同样发出 `SHCNE_CREATE (0x2)`；源文件消失、目标内容匹配且目标行出现，队列、通知及 provider 枚举各一次。探针连续执行后 Private Desktop 前台/输入守卫和隔离清理均通过。上述 copy/move 都是单项文件系统操作，不代表多选剪贴板、真实拖动或第三方 Shell namespace。

### 2026-11-01：DefView 派生列刷新探针补充表头诊断

此前 `IColumnManager::Apply` 返回成功且列键读回存在，但 `WaitForSizeState` 只能报告派生值子项是否出现，无法判断自定义列是否仍处于实际 `SysHeader32` 表头。隔离 `SyntheticShellSortProbe` 现于通知/重建策略前后记录原生表头列名，与 `IColumnManager` 列键及逐项值读取计数并列输出，用于区分列未激活与已显示列值未重新读取。NativeProbe Release build 和 `git diff --check` 通过；未创建 Shell view、未运行 probe。需要在获准的隔离运行中取得表头/值计数后，才能决定下一步应查列激活还是 DefView 派生值缓存；不涉及产品 ExplorerBrowser。

### 2026-11-01：Shell view 窗口重建与列重放对照

使用同一 WPF 根 Private Desktop synthetic filesystem view 的两种独立新进程策略复验。`single-item-recreate-shell-window` 连续两轮均在单项 `SHCNE_UPDATEITEM` 后销毁并重建同一个 `IShellView` 的窗口；无需显式重放 `IColumnManager` 列时，表头保持 `Name | Recursive Size | Type`，更新后的目录值读回为 `1`，排序成为 `small.bin | child`，选区和焦点恢复，Shell 只枚举一轮。两轮均 exit 0，前台、输入时间戳和隔离清理守卫通过。

反向对照中，收到通知后调用 `IColumnManager::SetColumns` 重放 `Name` 与递归大小键，再重建窗口，表头变为 `Name | 空白列`，派生值未显示；在该路径前先显式调用 `IShellView::SaveViewState` 得到 `S_OK` 并使状态流写入一次，结果仍为 `Name | 空白列`，读取次数为 0。两次对照均保留选区/焦点；保存状态的样本也保留更新后的排序。此结果说明当前 synthetic provider 中，DefView 自行重建能恢复原生列和值，而手工列键重放覆盖了正确的列布局；不能推断所有 Shell provider 或 ExplorerBrowser 均有相同行为。

探针只验证孤立 `IShellView`/DefView，不接入产品、不改变用户目录或发布包。创建视图时用 `IShellBrowser::GetViewStateStream` 读回、在 `IShellView::SaveViewState` 时写出是 Windows 文档描述的常规生命周期；本次实测表明单独写出后仍不能替代 DefView 自身的列恢复。参考：[IShellView::CreateViewWindow](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-createviewwindow)、[IShellBrowser::GetViewStateStream](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-getviewstatestream)、[IColumnManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icolumnmanager)。

### 2026-11-01：显式重新应用列后的 view-window 重建复验

为对照上面的“仅重建窗口”，本轮使用 `single-item-recreate-shell-window` 策略，在通知后先显式重新应用列，再销毁并重建同一个隔离 `IShellView` 的 view window。初始目录/文件值为 `10`/`4`；重建后 DefView 重新枚举行（2 行）、读取派生值（`[4|1]`）、执行一次大小比较，并按升序显示 `small.bin|child`。`IColumnManager.Apply` 返回 `S_OK`，选区和焦点保持。WPF Private Desktop watchdog exit 0，前台 HWND、输入 tick 未变，隔离注册、夹具和 NativeProbe 进程均清理完成；日志：`src/.state/native-probe-watchdog/20261101-042722-7542daff.stdout.log`。作为对照，默认 `single-item` 策略未触发重枚举或派生值读取；native `STATIC` 根宿主样本在清理时超时，不采纳其行为结论。

本轮原型结果与前述“无需显式重放即可恢复列”的窗口重建样本不同，表明列恢复行为依赖具体的列状态路径，尚不能收敛为通用步骤。另有探针诊断字段按 adapter 声明 GUID `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1` 精确比较时报告列不存在；而且原始日志显示，通知、显式列重放和窗口重建之前的 `IColumnManager.GetColumns` 已返回 GUID `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1`；适配器源码中的 `MapColumnToSCID(1)` 明确返回 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`。因此此差异早于本轮重放，不能归因于重放或窗口重建。可见表头、值、排序与 provider 读取计数均证明该轮派生列已被消费，但是哪一层将列身份转换/替换仍未查明；只认可行值和排序观测，不认可精确 GUID 相等检查。

此证据仍限于当前合成文件系统 provider 下的隔离 `IShellView`/DefView；没有验证产品 ExplorerBrowser、真实用户可见刷新、Shell 菜单/文件操作/拖放完整兼容，也没有批准产品迁移或发布。下一步查清 provider 列 GUID 映射，并把窗口重建与列恢复放回完整通知/导航生命周期门槛中评估。产品仍使用 ExplorerBrowser。

### 2026-11-01：列 GUID 差异的时序范围收窄

日志的 `phase=before` 列快照已包含 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1`，发生在通知、`IColumnManager.Apply` 和 view-window 重建之前；探针 adapter 的 `MapColumnToSCID(1)` 则返回 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`。因此差异不是本轮显式重放或重建创建的。当前证据确认 Shell 列管理器呈现的 key 与 provider 映射 key 不同，但尚未定位转换发生于 Shell 的哪一层，也没有证据把任一 key 判为错误。下一步应在同一 view 的列发现和 `SetColumns` 前后分别记录 `MapColumnToSCID`、表头列身份与 `IColumnManager` 读回值，建立逐阶段映射；不通过猜测 GUID 修改产品或原型列实现。
### 2026-11-01：重放 Shell 捕获列集并跟踪 provider 映射调用

新增 `single-item-capture-reapply-recreate-window` 对照，在 `SHCNE_UPDATEITEM` 后将更新前 `IColumnManager.Capture` 返回的完整三列及原宽度原样 `Apply`，再重建同一 view window。Private Desktop WPF root watchdog exit 0、`isolation-clean=True`；前台仍是同一 `LockApp` HWND，last-input tick 未变。重建后重新枚举 2 行、大小列读回 `[4|1]`、按大小升序为 `small.bin|child`，焦点/选区保持，`Apply` 返回 `S_OK`。日志：`src/.state/native-probe-watchdog/20261101-043355-5428d2ba.stdout.log`。

新增的 provider `MapColumnToSCID` 调用计数表明，通知、捕获列及 `SetColumns` 重放阶段均没有调用该接口；view-window 重建阶段共调用 8 次，对索引 0、1、2、3 各两次。索引 1 的 adapter 源码分支映射至 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`；重建前后的 `IColumnManager` 列快照仍为 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1`。因此 Shell 确实在重建时咨询 provider 映射，但捕获列 key 与 provider 映射 key 仍保持不同；当前仅计数了回调，没有记录 Shell 内部的转换步骤，不能据此断言哪一个 key 是权威身份。

该对照说明以 `IColumnManager` 实际捕获值重放，在本合成 provider 中可恢复派生列消费/排序；此前使用 adapter 声明 key 重放的成功结果也仍成立。探针日志现将精确 GUID 比较明确标成 `provider-recursive-size-key-exact-match=False`，不再把它写成列不存在。结果限于隔离 `IShellView`，没有改产品 ExplorerBrowser 或发布包；迁移门槛仍开放。

### 2026-11-01：记录 provider 的列映射返回值

为区分 Shell 是否在窗口重建时继续询问 provider、以及回调实际返回了什么，NativeProbe 新增有界线程安全映射轨迹，记录列号、完整 SHCOLUMNID 和 HRESULT。静态合同先因新导出尚不存在而失败，接线后通过；Private Desktop WPF 根宿主复跑 exit 0，隔离清理通过。窗口重建时列 0–3 各映射两次；列 1 两次均返回 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`，名称与类型列分别返回标准键，列 3 返回 `E_INVALIDARG`。同一轮 `IColumnManager.GetColumns` 前后仍读到 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1`。这确认 provider 在重建中持续返回其声明键，但没有解释 Shell 为何捕获到不同键；不能据此判定任一 GUID 错误。

诊断同时发现 `ResetSyntheticSortCounters()` 会重置 provider 语义选项。排序探针在分阶段重置计数器后，窗口重建因此可能走到不同映射分支。现已将计数重置与语义选项分开，并加入静态回归合同，避免诊断辅助函数改变 provider 行为。Native adapter `/W4 /WX`、NativeProbe Release 构建及无窗口静态合同通过；WPF Private Desktop 日志：`src/.state/native-probe-watchdog/20261101-045224-c4d69d7e.stdout.log`。仅为隔离原型探针和文档修改，产品仍使用 ExplorerBrowser，未做 UI 验收或发布。
### 2026-11-01：列 GUID 分歧定位到首次 view-window 创建阶段

把列元数据回调也纳入有界轨迹后，Private Desktop WPF 根宿主完整复跑成功（watchdog exit 0，前台 HWND/输入 tick 不变，`isolation-clean=True`）。首次 `IShellView::CreateViewWindow` 中，Shell 读取了 `Name`、`Recursive Size`、`Type` 三个标题；列状态分别为字符串 `0x11`、整数 `0x12`、字符串 `0x11`，并调用 `MapColumnToSCID` 三次，依次得到 `PKEY_ItemNameDisplay`、原型自定义 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`、`PKEY_ItemTypeText`。随后同一 view 的 `IColumnManager.Capture` 仍读到 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1` 作为第二列键。

在重建前清空轨迹后，重建阶段再次调用列映射 8 次（0–3 各两次），但没有再调用 `GetDetailsOf(NULL, i)` 或 `GetDefaultColumnState`。因此已知列标题/状态和 provider 的列键在首次创建时均正确送达，键分歧在首次创建后即可观察；重建不是元数据读取来源。内部身份转换仍未定位，不能判定 Shell 缓存、列管理器转换或其它状态来源，也不据此修改列 GUID。日志：`src/.state/native-probe-watchdog/20261101-045626-ce26270d.stdout.log`。构建通过；仅隔离原型，不涉及产品 ExplorerBrowser、UI 验收或发布。
### 2026-11-01：首次创建返回时列管理器已暴露不同键

在 `IShellView::CreateViewWindow` 刚返回时立即读取 `IColumnManager`，不等首轮 700 ms settle，已得到三列及宽度；`GetColumnInfo(CM_MASK_NAME)` 将两个标准键命名为 `Name`、`Type`，并将 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1` 命名为 `Recursive Size`。700 ms 后键和名称不变。对应首次 provider 回调仍将列 1 映射到 `{7E1F84B7-C052-4E51-8A3C-869254A6B910}:1`。因此不同键在窗口创建返回时已形成，但列管理器身份仍保留正确标题；它不是“自定义列未出现”的证据。此时 view-state stream 记录 5 次读取、0 次写入，只能说明该阶段没有写入，不能单独证明 Shell 内部采用了何种默认列状态。

该结果进一步将调查目标从“重建是否覆盖列键”收窄为“列管理器列身份是否为同一 view/相同 provider 下稳定且可跨新 view 重用”。日志：`src/.state/native-probe-watchdog/20261101-045810-68c8bb9a.stdout.log`；Windows SDK `CM_MASK_NAME=0x8` 已核对。Release NativeProbe build 0 警告/错误，Private Desktop WPF watchdog exit 0，前台/输入/清理守卫通过。后续应在第二个新建 view 中读回列键/名称，并单独验证将捕获键应用到新 view 后的列值与排序；不能从同 view 重建结果外推跨 view 持久性。
### 2026-11-01：捕获列布局跨同 provider 的新 view 可复用

在已完成单项通知/同 view 窗口重建路径后，使用相同 synthetic filesystem provider 与 backing folder 创建第二个独立 `IShellView`。第二 view 初始 `IColumnManager` 三列键与来源 view 相同，`GetColumnInfo(CM_MASK_NAME)` 仍读到 `Name / Recursive Size / Type`；provider 映射仍将列 1 返回另一 GUID。把来源 view 捕获的完整列键与宽度应用到第二 view 后，`IColumnManager.Capture` 完全匹配，真实表头保持三个预期名称，行值读回 `1 / 4`，列头升序为 `child|small.bin`、降序为 `small.bin|child`。watchdog exit 0，前台和输入守卫未变，`isolation-clean=True`。

因此 `{9E5E05AC-1936-4A75-94F7-4704B8B01923}:1` 与 provider `MapColumnToSCID` 键不同，不妨碍当前同进程、同 provider 下跨新 view 应用列布局、读取值和排序。它是该隔离场景中可复用的列管理器身份。该结论不覆盖其他 namespace/provider、进程重启/view-state 持久化、ExplorerBrowser、Q-Dir `.qdr` 互操作或用户可见行为；不能据此认定产品迁移门槛已通过。日志：`src/.state/native-probe-watchdog/20261101-045954-dbdfa2c1.stdout.log`。后续可转向另一个未通过的 Shell 兼容门槛，不必再重复同一 provider 内的 GUID 映射采样。

### 2026-11-01：扫描结果发布到活动 view 缓存后的刷新路径

新增 NativeProbe 专用 `single-item-live-cache-publish` 场景：在不替换目录身份的情况下更新目录内容，把新递归字节数写入活动 `IShellView` provider 的身份键快照缓存，然后分别尝试单项 `SHCNE_UPDATEITEM`、单项加父目录 `SHCNE_UPDATEDIR`、完整 `IShellView::Refresh`。缓存发布使用 provider 实际持有的目录身份与绝对 Shell parsing name；没有用测试全局大小覆盖值。三种方式均由 WPF `HwndSource` Private Desktop 宿主承载。

单项更新和“单项+父目录”没有重枚举（`enum-calls=0`），也未重新读取列值，大小列索引读回 `-1`；行与原选中焦点仍在。完整 `IShellView::Refresh` 重枚举一次（2 行），读到 `4 / 1` 并按新值重排，大小列布局保留。刷新后原选中项/焦点短暂丢失；用现有 `ShellViewSelectionSnapshot` 恢复后，选择与焦点读回均成功。单次隔离探针从发送刷新到稳定读回约 2.7 秒，包含 2.5 秒轮询窗口；这不是产品刷新延迟或可见闪烁测量。

NativeProbe Release 构建 0 警告/错误；三种 WPF Private Desktop 探针均退出 0，前台 HWND、输入 tick 未变，`isolation-clean=True`。日志：`20261101-053922-05b29af5.stdout.log`、`20261101-053927-622e547d.stdout.log`、`20261101-053933-afce6bca.stdout.log`。这证明在当前 synthetic provider 中，扫描结果要可靠呈现在活动 DefView 派生列需完整刷新并显式恢复选择/焦点；仍未验证可见闪烁、实际 ExplorerBrowser 产品、其它 Shell provider，也未接产品扫描器或用户配置。

## 2026-11-01：跨卷文件操作后的双视图通知对账

扩展 `--shell-change-reconciliation`，将源夹具放在 D: checkout 下的唯一 `.state/native-probe` 目录、目标夹具放在 C: 临时目录，并分别为两个窗格目录注册 Shell change 通知。跨卷 `IFileOperation.MoveItems` 后观察到源 `Deleted` 和目标 `Created`；通过通知后的 Shell 重枚举与行模型对账，源/目标计数由 3/1 收敛为 1/3，两个目标文件字节均与原内容匹配。源/目标未移动锚点行 ID、选区及焦点保留，移动源行退休且目标行新增。后续跨窗格 Copy/Rename/Delete 的既有对账序列也继续通过。

NativeProbe Release 构建 0 警告/错误；`--shell-change-reconciliation` 退出码 0，隐藏窗口与前台不变。该证据补齐普通 filesystem 跨卷移动后的双视图通知和行数收敛，不代表 ExplorerBrowser 产品接线、可见 Shell 操作 UI、真实 OLE 拖放、ZIP 跨卷移动或其它 provider 已验证。夹具运行后清理。

### 2026-11-01：ListView 单行更新不能刷新派生目录大小

为寻找比完整 `IShellView::Refresh` 更局部的派生列更新方式，增加隔离 `single-item-lvm-update-live-cache-publish` 对照：先用生产递归扫描器重新统计 `child` 为 `1 B` 并发布到当前 provider 身份缓存，再向对应 DefView `SysListView32` 行发送 `LVM_UPDATE`。WPF `HwndSource` Private Desktop watchdog 退出码 0，前台 HWND 与最后输入 tick 未变，`isolation-clean=True`。`LVM_UPDATE` 返回 0；2.5 秒观察内 `EnumObjects=0`、大小属性读取和比较均为 0、视图仍按旧值排序，递归大小列不可读，选区/焦点仍保留。返回值 0 本身不作成功/失败依据，结论来自视图行、排序和 provider 调用计数。

同一源代码、同一隔离宿主的 `full-view-refresh-live-cache-publish` 对照重新枚举 2 行，列值从 `[10|4]` 更新为 `[4|1]`，排序改为 `small.bin|child`。刷新后的首次读回中选择/焦点暂时缺失；随后现有 `ShellViewSelectionSnapshot` 恢复并读回 `refresh-restore-selection=True`、`refresh-restore-focus=True`。先前记录中把首次读回状态误写成最终状态，现更正：完整刷新加快照恢复在该隔离样本中保留了选择和焦点。Private Desktop 前台/输入/清理守卫与 NativeProbe Release build 通过。此结果说明当前合成 DefView 需要完整刷新才能消费已发布的派生大小值，针对 ListView 单行的重绘消息不足。它不是产品 ExplorerBrowser 的刷新表现或可见闪烁测量；下一步应在隔离原型中检查绘制抑制是否能减少过渡帧，并继续验证其它 provider，不把实验接入产品或据此批准宿主迁移。

### 2026-11-01：完整刷新期间暂缓 ListView 绘制

在上述完整刷新策略上增加 `full-view-refresh-redraw-live-cache-publish` 对照：发送 ListView `WM_SETREDRAW(FALSE)`，执行 `IShellView.Refresh()`，等待新大小及排序读回，再恢复绘制并按 Microsoft 文档使用 `RedrawWindow(RDW_ERASE | RDW_FRAME | RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW)`。本机 Private Desktop WPF probe exit 0；刷新后列值 `[4|1]`、排序 `small.bin|child`，快照恢复选择/焦点读回成功。单项绘制属性 `SysSetRedraw` 没有出现在 HWND 属性表中，因此不能由该属性确认控件是否实际暂停绘制；目前也没有采集刷新过程像素。该实验只证明消息调用顺序与刷新/状态恢复可共存，不证明白/黑过渡帧已消失或用户可见体验改善，需另做隔离像素/可见验收。[Microsoft WM_SETREDRAW 文档](https://learn.microsoft.com/windows/win32/gdi/wm-setredraw)

同日追加只读 `GetDC`/`GetPixel` 采样，尝试在禁用绘制前、刷新稳定后以及恢复并强制重绘后三个阶段读取 ListView 客户区像素。两次 Private Desktop 运行中采样均混有 `GetPixel` 的无效哨兵 `0xFFFFFFFF`；复验一轮分别为 253 个样点中的 92、253、92 个有效值。因为同一 HWND 的可读样点数随阶段变化，且宿主不在当前交互桌面，这组数据不能比较帧内容，也不能证明绘制暂停或闪烁消失。探针现在将有效/无效样点数分开记录，遇到无效值时不报告像素相等结论。Release NativeProbe build 0 警告/错误，Private Desktop watchdog exit 0，前台、输入与隔离清理守卫通过；日志：`20261101-070303-2da107d2.stdout.log`。要验证用户实际看到的过渡帧，仍需后续集中可见 UI 验收；本实验没有触碰产品刷新代码。

随后在相同 synthetic WPF Private Desktop view 的独立进程中对子类观测 `SysListView32` 的 `WM_PAINT`。未发送 `WM_SETREDRAW` 的完整刷新收到 2 次；发送 `WM_SETREDRAW(FALSE)` 后刷新仍收到 2 次，恢复绘制并强制 `RedrawWindow` 时另收到 1 次。两次 watchdog 均 exit 0，前台、输入和隔离清理守卫通过；日志分别为 `20261101-070535-51cd2645.stdout.log`、`20261101-070542-7e52a22a.stdout.log`。这仅证明该两轮中窗口过程仍收到相同数量级的绘制消息，不证明消息对应的客户区内容已画出或用户能看到它；样本各一轮，像素又无法在非活动桌面可靠读取。因此没有足够证据将 `WM_SETREDRAW`/强制重绘用于产品 F5 或查看方式切换。

### 2026-11-01：无窗口递归大小合同扩展到 4 GiB 以上

将自有行模型/原生 provider 的无窗口投影 fixture 改为稀疏文件：同级目录初始递归大小为 `5,368,709,130` 与 `5,368,709,129` 字节，更新后变为 `5,368,709,127` 与 `5,368,709,129` 字节。生产 `RecursiveDirectoryScanner` 与句柄绑定扫描器逐项汇总一致；native `IShellFolder2` provider 通过 `VT_UI8`、十进制文本读取、数值比较和扫描值更新后的排序翻转合同。根文件也使用稀疏长度，令排序断言仍覆盖更新后跨过同一参考值的情况，而不实际分配数 GiB 磁盘空间。`--static-shell-column-folder-smoke` Release 运行通过并明确输出 `window-created=False`；SettingsSmoke 增加大于 4 GiB 的递归大小显示合同，全量通过。这里证明的是当前隔离数据/adapter 64 位值链路，不是 IShellView/ExplorerBrowser 可见列、实际 UI 列排序或产品接线。

### 2026-11-01：同路径身份变化时清除过期递归大小快照

同一 Shell parsing name 被新的目录对象复用时，原 provider 已通过卷序列号与文件 ID 拒绝旧扫描值，但仍把不再匹配的 map 项保留到 DLL store 释放。为避免反复替换同一路径留下失效条目，`TryGet` 在确认身份不匹配时于锁内删除该项，再按原有 Shell 大小回退。静态合同新增缓存项数断言：在原始目录快照和另一目录快照共 2 项的场景中，同路径替换后读取新目录值 17 B、拒绝旧值，并确认缓存降为 1 项。该合同在旧 adapter 上按预期失败（`stale-snapshot-retained`），修复后通过；原生 adapter `/W4 /WX` 构建通过，NativeProbe 的 `--static-shell-column-folder-smoke` exit 0、输出 `replacement-cache-invalidation-and-pruning-passed`，且 `window-created=False`。

这只回收“同一个 parsing name 被新身份取代”的条目。纯目录重命名会产生新 parsing name，旧键在没有显式失效通知/所有者生命周期前仍可能保留；缓存没有全局容量上限。该实验属于 NativeProbe 隔离原型，未修改产品 Shell view、ExplorerBrowser 或发布包，也未测量进程级内存收益。

### 2026-11-01：隔离 `SetNameOf` 迁移重命名目录的递归大小快照

在合成 filesystem provider 的真实目录重命名路径上，`SetNameOf` 同时取得重命名前的 Shell parsing name、成功后的新子 PIDL 与文件身份。若旧快照身份仍匹配，store 在同一锁区内先写入新 parsing name，再删除旧键；值随同一目录身份迁移，不需要重新扫描。静态 NativeProbe fixture 先发布 `1,234 B`，真实调用 `SetNameOf(child → renamed-child)` 后读回新键仍为 `1,234 B`、旧键 miss、快照数仍为 1。该回归在旧 adapter 上按预期输出 `folder-rename-cache-migration-failed`，修复后输出 `folder-rename-and-cache-migration-passed`；原生 adapter `/W4 /WX` 与 `--static-shell-column-folder-smoke` 通过，探针明确 `window-created=False`。

这仅覆盖该原型自身接收到的 `SetNameOf` 文件夹重命名。Shell 删除/移出通知的子树失效后来已补入隔离回调探针；外部进程真实通知时序、父目录移动后的完整事件序列，以及产品 ExplorerBrowser/可见派生列仍未验证。缓存仍无整体容量上限，不能据此声称解决了长期内存增长。

### 2026-11-01：Shell 删除/移出通知清理递归大小子树快照

缓存新增按 parsing name 子树失效：精确清除目录本身及其后代，并以分隔符边界阻止 `child` 误命中 `child-sibling`。合成 `IShellFolderViewCB::MessageSFVCB(SFVM_FSNOTIFY)` 收到 `SHCNE_DELETE`、`SHCNE_RMDIR` 或 `SHCNE_RENAMEFOLDER` 时，从通知中的第一个绝对 PIDL 取得 parsing name 并清理旧子树；带 `SHCNE_INTERRUPT` 的事件按基础事件处理。新合同覆盖子树与同前缀兄弟区分，另经 `SHCNE_RMDIR` 回调验证缓存项归零，回调仍返回 `E_NOTIMPL` 让 DefView 继续默认更新。

原生 adapter `/W4 /WX` 构建、NativeProbe Release 构建和 `--static-shell-column-folder-smoke` 均通过，输出 `recursive-size-subtree-invalidation-passed` 与 `recursive-size-shell-delete-invalidation-passed`，且 `window-created=False`。仅为隔离原型的 callback/cache 路径证据；没有验证 ExplorerBrowser 产品接线、外部应用真实通知的时序、父目录移动事件覆盖、全局缓存容量或用户可见列体验。该缓存仍无容量上限。

### 2026-11-01：内容变更通知使父目录递归大小快照失效

继续追踪缓存正确性时发现，删除 `folder\file.txt` 只清理文件路径下方的项不会使 `folder` 行上缓存的递归总量失效。缓存现按变更路径同时清除匹配项与所有路径祖先；目录移除/重命名还清除旧/新目录子树。合成 `SFVM_FSNOTIFY` 覆盖 create/delete/mkdir/rmdir/item rename/folder rename/update item/update dir，rename 两端 PIDL 分别处理；递归总量变化通知继续返回 `E_NOTIMPL` 交给 DefView 默认更新。

回归先在旧逻辑上复现：对嵌套文件路径发送 `SHCNE_DELETE` 后，父目录快照仍在，合同失败；修复后 `recursive-size-ancestor-invalidation-passed`、`recursive-size-subtree-invalidation-passed` 和 callback 路径 `recursive-size-parent-summary-invalidation-passed` 均通过，且兄弟路径不误删。Native adapter `/W4 /WX`、NativeProbe Release 和 `--static-shell-column-folder-smoke` 通过，`window-created=False`。仍未验证注册到真实 view 的外部变更通知时序、ExplorerBrowser 产品接线、用户可见排序刷新或缓存容量上限。

### 2026-11-01：活动隔离 Shell view 收到递归大小变更通知

此前直接调用 provider 回调只能证明缓存失效逻辑，不能证明 Shell 会把事件投递给 view。NativeProbe 现通过活动的 synthetic `IShellView` 与 `WPF HwndSource` 隐藏宿主验证完整投递：先为 `child` 行发布哨兵快照，再将绝对 PIDL 的 `SHCNE_UPDATEITEM` 与 `SHCNE_UPDATEDIR` 分别交给 Shell 通知系统；第二项前在 `child` 中创建临时文件。两事件到达活动 view 后，provider 中 `child` 快照均变为 miss。既有目录更新检查同时看到根列表一轮重枚举、303 行可见，view 没有安排自有延迟刷新。Private Desktop probe exit 0，前台 HWND/最后输入 tick 未变，`isolation-clean=True`。

该实验确认本机 Windows Shell 的显式 `SHChangeNotify` 经活动 view 注册路径可触发递归大小缓存失效；没有验证不显式发送 Shell 通知的任意第三方写入、跨进程事件生成时序、ExplorerBrowser 产品接线、可见列刷新或长期缓存容量。

### 2026-11-01：递归大小原型缓存设定 LRU 上限

此前 NativeProbe 的递归大小快照会一直保留到身份失效或 store 释放，访问过的路径数量增加时没有数量上限。原型 store 现在最多保留 256 个快照；命中会更新最近访问顺序，超限插入时淘汰最久未使用项。静态合同先在旧实现上按预期失败，再验证触碰最旧项后插入新项会淘汰第二旧项，快照数量保持 256，最新项和值均可读。淘汰只丢弃缓存结果，后续 Shell 读取可回退原生大小或由扫描重新填充。

验证：Native adapter `/W4 /WX`、NativeProbe Release build 0 警告/错误，`--static-shell-column-folder-smoke` 退出码 0 并输出 `recursive-size-cache-lru-bound-passed`；此探针 `window-created=False`。该上限仅适用于隔离 NativeProbe 原型，未接入产品、未测进程内存变化，也不证明逐行大小列可见或 ExplorerBrowser 兼容性。

### 2026-11-01：应用会话跨进程保留递归大小列键

新增 `--column-layout-process-roundtrip` SettingsSmoke：父进程将 prototype Recursive Size 的自定义 property key、列宽、排序方向、分组方向和标签路径序列化到临时 INI，启动独立 `dotnet` 子进程调用产品 `SessionStore.ParseComplete` 读取，再核对字段完全一致。定向场景与完整 SettingsSmoke 均通过，SettingsSmoke Release build 0 警告/错误。

该合同只证明应用 INI 对未知/自定义 property key 的跨进程保存和解析稳定；没有创建 Shell view，也没有证明 `IColumnManager`/DefView 能在另一进程或另一 Shell provider 中恢复、显示或排序该列。产品 ExplorerBrowser 未改，逐行大小仍未接入。

### 2026-11-01：多行递归扫描的托管分配基准复跑

运行 `dotnet run --project src/ExplorerDirs.SettingsSmoke -c Release -- --recursive-directory-scan-allocation-probe`。5,000 个空子目录夹具中，完整树扫描保留 5,001 条结果，请求路径扫描只保留 4 条结果；本次测得托管分配分别为 3,892,000 / 3,201,448 bytes（减少 690,552 bytes，17.7%），强制 GC 采样的近似峰值存活堆为 1,821,992 / 68,664 bytes（差约 1.75 MB），测得耗时 267 / 196 ms；该计时路径每 256 项强制采样一次完整 GC，不能当作实际扫描耗时对比。120 个目录、每目录 25 个小文件的第二夹具，请求 120 条结果分配 261,232 bytes，低于 1,175,000-byte 回归预算。完整扫描器保留正确的逐目录递归摘要；指定结果路径版本只保留视图需要的摘要。

随后运行 `dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --static-shell-column-folder-smoke`，退出码 0；逐行 64-bit 数值列合同、缓存 256 项 LRU、目录身份替换失效和数值排序通过，输出明确 `window-created=False`。这些数据是当前开发机的托管分配/近似堆和隔离 provider 合同；不代表真实磁盘耗时、产品进程内存收益、ExplorerBrowser 接线或可见 UI。已存在的 `ScanDirectoryTreeForPaths` 已避免逐行重复遍历，本轮没有另造扫描器。下一步应等集中 Q-Dir/产品 UI 验收提供格式与刷新反馈，再决定是否接入产品视图。

### 2026-11-01：区分递归大小列与相对百分比探针列

复核 NativeProbe 类型实验后确认，递归总大小是列 1：`GetDefaultColumnState` 返回 `SHCOLSTATE_TYPE_INT`，`GetDetailsEx` 返回 `VT_UI8` 字节值，`GetDetailsOf` 输出十进制字节文本，并由 `CompareIDs` 数值排序。待核查的 `VT_R8` 是隔离合成视图额外的相对百分比列 3，不是递归总大小列。将这两列统称为“递归大小列类型不一致”会扩大问题范围。

恢复原型列状态后，`build-static-shell-folder-adapter.ps1` 与 `dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --static-shell-column-folder-smoke` 均通过；该静态合同明确 `window-created=False`。此前对列 3 的类型尝试没有形成足以判定其 Shell 显示/排序行为的隔离 A/B 证据；不得据此修改列 1 或宣称百分比列已修正。后续在同一隔离 `IShellView` fixture 中只变更列 3 状态，并记录列标题、行显示、排序及 `IColumnManager` 读回；结果见下一节。产品 ExplorerBrowser 和可见用户体验仍需单独验收。

### 2026-11-01：相对百分比列状态位的隐藏 `IShellView` A/B

NativeProbe 增加三种状态模式，仅对隔离合成 view 的列 3（Relative %）设置 `SHCOLSTATE_TYPE_INT`、`SHCOLSTATE_TYPE_STR` 或 `SHCOLSTATE_DEFAULT`；列 1 递归总大小保持 `TYPE_INT`。三种模式均在 WPF `HwndSource` Private Desktop 宿主独立运行并正常清理。状态回读分别为 `0x12`、`0x11`、`0x10`；三轮均保留 `Name / Recursive Size / Type / Relative %` 表头、同一 `IColumnManager` 四列键及宽度，行文本均为 `child=71.428571%`、`small.bin=28.571429%`，列头升序为 `small.bin|child`、降序为 `child|small.bin`。前台 HWND 与最后输入 tick 均不变。

这说明在该 Windows 合成 `IShellView`/DefView 场景下，列 3 的三个类型状态位没有改变可观察的显示、排序或列管理器布局；它没有证明其它 provider、ExplorerBrowser 产品宿主或用户配置文件中的列状态也相同。当前没有理由为追求该探针的差异而改产品元数据。

第一次整数对照使用旧的 `STATIC` parent 路径：列内容和排序读回正确，但 worker 在销毁 parent 时超时，wait-chain 含 COM activation / `ChsIME`，前台和输入守卫未变化。该轮不纳入清理成功的 A/B 集；watchdog 终止进程后仍留有仅属于该轮随机 CLSID `{f2be1c4d-259f-42e7-9389-343c0d1123f0}` 的 HKCU Shell 注册，以及 `%TEMP%\ExplorerDirs-SyntheticShellSort-f2be1c4d259f42e79389343c0d1123f0` 和 `%TEMP%\ExplorerDirs-SyntheticShellSort-Copy-f2be1c4d259f42e79389343c0d1123f0` 夹具。递归清理被本机执行策略拒绝，没有换用其它删除路径。随后相同整数模式改用 WPF Private Desktop parent 复验成功并清理。该超时没有证据表明是列实验或产品卡死。

### 2026-11-01：自有 ListView 递归列的经典查看模式往返合同

将隐藏 `--common-listview-relative-size-contract` 扩展为视图样式往返：递归扫描并异步发布新值、按相对大小排序后，逐一把标准 `SysListView32` 的 `LVS_TYPEMASK` 切换到 Icon、Details、SmallIcon、List，再回到 Details。每一步都核对两行仍在、稳定 Shell row ID 不变、small-folder 的选中/焦点仍跟随该 row ID；返回 Details 后又核对其递归大小与相对百分比文本和切换前一致。

探针开始时尝试 `LVM_SETVIEW`，但该 NativeProbe 控件对消息返回 FALSE，`LVM_GETVIEW` 也一直读作默认 Icon；为避免该 API 失败造成假阳性，本合同改用四种经典视图对应的 `LVS_TYPEMASK` 样式位，并逐步回读样式、行身份和状态。Tile 未纳入。NativeProbe Release build 0 警告/错误；隐藏相对大小合同输出 `Icon/Details/SmallIcon/List=True`、`details-restored=True`、`passed=True`，窗口不可见且前台未变。

这只验证标准 ListView 控件保存行数据、子项文本和状态的合同，不采样客户区像素，不能证明这些模式实际绘制正确或满足 Q-Dir 的布局；也不覆盖 Shell 原生菜单、文件操作、拖放、通知或产品 ExplorerBrowser。它补齐自有行模型的一项模式切换门槛，但不改变“自有行视图需完整兼容验证后才评估迁移”的结论。

发现相邻 common-listview-color-baseline-private-desktop 探针也用 LVM_SETVIEW 后，顺手改为切换并读回四种经典样式；本轮输出四种模式的 custom-draw 回调与像素断言均为真，但 Private Desktop watchdog 在探针完整返回/清理前超时。超时时 foreground HWND 与 last-input tick 未变，wait-chain 经 COM activation / ChsIME 且 cycle=False。因此这里只记录模式设置与断言日志，不把该颜色探针判为完整通过；Tile/Content 模式及退出时序仍需独立处理。

### 2026-11-01：递归大小列的原生背景排序命令

扩展 WPF Private Desktop 下的 synthetic filesystem `IShellView` 探针：初始 Details view 显示递归大小 `child=10`、`small.bin=4`；先经递归大小列标题建立 Shell 原生 sort key，再从该 view 的 `GetItemObject(SVGIO_BACKGROUND)` 获取背景 `IContextMenu`，查询 canonical `sortascending` / `sortdescending` 后以 `InvokeCommand` 执行。两条命令 HRESULT 均为 `S_OK`；`IFolderView2.GetSortColumns` 读回相同键 `{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1`，方向分别 `+1/-1`，项目顺序分别 `small.bin|child` 与 `child|small.bin`。NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台 HWND、输入 tick 与隔离清理守卫通过。

这补齐的是 synthetic view 将原生背景排序命令路由到其自有递归大小列的程序化合同；未显示 popup 或模拟用户选菜单，未覆盖普通 ExplorerBrowser、其它 provider、交互命中/焦点，也没有解决更新通知后 DefView 不重新读取派生列的问题。产品仍为 ExplorerBrowser，派生列仍是隔离原型。

### 2026-11-01：对照 Q-Dir 逐行大小显示与排序选项

复核 Q-Dir 官方[逐行文件夹大小说明](https://www.softwareok.com/?seite=freeware/q-dir/File_and_folder_size_Column_with_percentage)及[大小显示 FAQ](https://www.softwareok.com/?faq=155&seite=faq-Q-DIR)：Q-Dir 将递归大小复用“大小”列，将文件/文件夹数量复用“类型”列，并列出 Bytes/KB/MB/自动单位、千位分隔、0–3 位或自动小数、百分比、文件/文件夹计数、失焦暂停扫描和忽略文件夹大小排序等独立选项。官方说明还指出忽略递归大小排序是为了避免导航性能问题。

截至本节记录时，synthetic provider 原型只显示十进制原始字节文本，另有实验性 Relative % 列；它没有格式偏好、计数列替换、失焦暂停或排序策略选项。原生背景菜单排序命令已证明缓存值可参与升/降序，但这只验证可行性，不代表 Q-Dir 默认策略一致。特别是排序必须只读取已完成快照、不能在 Shell 比较回调中启动目录扫描；若未计算或过期，应使用明确的回退/占位策略，避免排序同步遍历目录树。

后续原型应拆成两个可独立核验的合同：一是已完成递归扫描值的列显示格式（单位、舍入/小数、分组符、百分比与目录计数的列落点）；二是递归值排序策略（启用/忽略、缓存未命中时的比较结果，以及排序期间扫描调用数必须为零）。默认值和便携 INI/UI 选项属于产品设计，当前不由此调查决定。以上结论来自官方公开说明与本仓库原型源码，不代表对 Q-Dir 当前默认值的运行时实测。

### 2026-11-01：隔离原生列自动单位显示格式

在 NativeProbe 的 synthetic `IShellFolder2::GetDetailsOf` 列路径增加隔离开关，允许把大小文本显示为自动单位；数据属性仍由 `GetDetailsEx` 以 `VT_UI8` 返回原始字节值，`CompareIDs` 仍按整数快照比较。无窗口原生合同用 1,536 B 样本验证文本为 `1.5 KB` 且属性仍为 1,536；WPF Private Desktop synthetic `IShellView` 则读到 `child=10 B`、`small.bin=4 B`，列头升/降序与背景菜单升/降序仍按数值正确排序。关闭开关时既有原始 `10`/`4` 文本合同通过。Release NativeProbe build 0 警告/错误；隔离 view probe exit 0，前台/输入/清理守卫通过。

这是显示字符串与 Shell 属性/排序值分离的可行性证明，不是产品 ExplorerBrowser 集成、可见 UI 或 Q-Dir 格式等价验收。千位分隔、多位/自动小数、本地化格式、文件/文件夹计数列、百分比、失焦暂停和忽略大小排序仍未实现；格式开关不保存配置，用户界面与产品默认值未定。

### 2026-11-01：隔离原生大小列的单位、精度与分组格式

继续扩展 NativeProbe 显示路径的隔离格式参数：自动或固定 B/KB/MB/GB/TB/PB/EB 单位、0–3 位小数、去掉无意义的末尾零、按当前 Windows 区域格式使用小数点和千位分隔。静态 provider 合同以 1,234,567 B 验证固定 KB 的 0/1/3 位结果 `1206 KB`、`1205.6 KB`、`1205.632 KB`，固定 MB 为 `1.177 MB`，字节分组按系统分隔符读回；同时确认 `GetDetailsEx` 仍返回原始 `VT_UI8=1234567`。此前自动单位 WPF Private Desktop view 继续显示 `10 B` / `4 B`，列标题与原生背景菜单升降序仍按原始数值比较。Native adapter `/W4 /WX`、NativeProbe Release build、`--synthetic-shell-folder-tree-smoke` 与格式化列 Private Desktop probe 均通过；前台、输入与隔离清理守卫通过。

本轮格式选项只由 NativeProbe 静态合同设置，自动格式仍是唯一接入隐藏 Shell view 的模式；固定单位/精度/分组没有经真实用户可见视图绘制。它们未接入 ExplorerBrowser 产品、没有 INI/UI 配置，也没有决定 Q-Dir 对应选项的产品默认值。列计数、百分比及失焦暂停/忽略大小排序仍未实现。

### 2026-11-01：固定单位格式通过隐藏 `IShellView` 列文本读取

继续将单位/精度参数从 NativeProbe 传入 WPF Private Desktop synthetic `IShellView`。独立隐藏 view 采用固定 KB、最多 3 位小数，在 `GetDetailsOf` 列文本中读到 `child=0.01 KB`、`small.bin=0.004 KB`；读取同一份原始整数快照后，列头及背景菜单升序仍为 `small.bin|child`、降序为 `child|small.bin`，比较回调各运行一次。NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台、输入与隔离清理守卫通过。

这补足固定单位/精度在隐藏 DefView 读取路径的证据，但不采集像素、不显示窗口，也没有核验千位格式在用户可见控件中的绘制。仍未接入 ExplorerBrowser 产品或 INI/UI 配置；Q-Dir 实际显示习惯和偏好默认值需由用户验收决定。

### 2026-11-01：千位分隔在隐藏 `IShellView` 列文本中的验证

Private Desktop 隐藏 view 探针将 `child` 的递归大小快照覆盖为 1,234,567 B，设置固定 Bytes 与系统千位分隔后，列文本读回 `1,234,567 B`，同一视图的原始 4 B 文件仍读作 `4 B`。升序保持 `small.bin|child`，降序保持 `child|small.bin`；比较回调各一次。探针先在未接入大小覆盖的夹具上因预期文本不匹配而失败，加入 NativeProbe-only 覆盖后通过。NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台、输入与隔离清理守卫通过。

该结果证明系统分组符进入隐藏 DefView 的列文本读取路径，不证明可见绘制或 ExplorerBrowser 产品行为；不同区域格式仍需在对应系统配置上验证。覆盖仅是测试夹具，不改变产品数据源或设置。

### 2026-11-01：大小排序缓存未命中时不在 Shell 比较回调内递归扫描

在 synthetic provider 中加入 NativeProbe 专用计数器，区分递归大小缓存未命中与 `GetSyntheticDirectoryMetrics` 扫描调用，并在 Shell `CompareIDs` 调用栈期间记录扫描。固定 KB 与分组字节两种隐藏 view 排序都实际触发 1 次目录大小缓存未命中，比较期间递归扫描计数为 0；`SizeForItem` 使用行枚举阶段携带的大小回退值，列头和背景菜单升降序仍正确。普通 synthetic folder-tree 合同通过；Native adapter `/W4 /WX`、NativeProbe Release 构建无警告/错误，Private Desktop 前台、输入和隔离清理守卫通过。

此证据限定于该 synthetic `IShellFolder2`：它证明 Shell 比较回调没有递归遍历，但不证明枚举阶段扫描成本可接受，也不解决快照过期后在下一次枚举前显示旧值的问题。真实 Shell provider、ExplorerBrowser 产品、后台扫描策略和用户可见排序刷新仍需分别评估。

### 2026-11-01：目录递归总量在 `EnumObjects` 返回前同步计算

在静态 synthetic folder-tree 合同中单独清零并采样 `GetSyntheticDirectoryMetrics` 的调用数与访问项数。枚举包含 `child` 与其 `nested-folder` 的两层夹具时，`EnumObjects` 返回前发生 2 次递归指标调用、访问 3 个后代条目；数值即用于目录行，之后才返回 Shell enumerator。源码路径是 `SyntheticSortFolder::EnumObjects` → `EnumerateBackingChildren` → 每个文件夹行的 `GetSyntheticDirectoryMetrics`，并在该函数内递归下降。Native adapter `/W4 /WX`、NativeProbe Release 与 `--synthetic-shell-folder-tree-smoke` 通过，合同校验了该夹具的计数。

这确定了当前隔离 provider 的成本入口：列值缓存未命中不会在比较回调里扫描，但首次列举文件夹时会先同步遍历其后代。两层夹具只有 3 个条目，不能据此估算大型目录耗时、磁盘访问或产品 ExplorerBrowser 行为；后续需用可扩展负载测量不同宽度/深度，并验证列关闭时能否完全跳过扫描，再设计按需/后台计算策略。

### 2026-11-01：可扩展目录夹具的递归扫描 A/B

增加无窗口 NativeProbe 负载探针，分别枚举单个宽目录（一个目录下 1,000 个文件）、100 个小目录（每个 10 个文件）及 32 层目录链。探针只切换 NativeProbe 专用“需要递归大小”开关，记录 `EnumObjects` 与完整取出行的耗时、直系行数、递归指标调用数和访问条目数。关闭开关时三个场景的递归指标调用/后代访问均为 0；开启时分别为 `1/1000`、`100/1000`、`32/62`。这些对照说明总条目数之外，目录数量和递归深度也会影响成本。

这次记录的单次合成夹具耗时为：宽目录关闭/开启 `0.855/2.283 ms`，100 个小目录 `21.433/53.282 ms`，深链 `0.809/10.995 ms`。耗时受 Windows 文件缓存、文件系统、杀毒软件和本机负载影响；其中关闭扫描后仍有直系目录行枚举与身份读取成本，不能将差值当作产品收益或真实磁盘性能承诺。开关仅属于 NativeProbe，没有接入 Shell 的列可见性判断、ExplorerBrowser、后台队列、INI 或 UI；下一步应验证列请求时机能否触发按需计算，并保证刷新/通知和排序期间不会把扫描工作压回 `CompareIDs`。

### 2026-11-01：递归大小在列值请求时按需扫描

增加单独的 NativeProbe 合同：开启按需模式时，`EnumObjects` 返回文件夹行但递归指标调用/访问条目均为 0；第一次请求该行的 `GetDetailsOf` 后才扫描夹具子树（2 次递归调用、3 个条目）并缓存 10 B。随后通过 `GetDetailsEx` 读取同一自定义递归大小键得到原始 `VT_UI8=10`，重复文本读取沿用缓存；`CompareIDs` 不增加扫描计数。Native adapter `/W4 /WX`、NativeProbe Release、专用按需 smoke 与原有 `--synthetic-shell-folder-tree-smoke` 均通过。

该原型证明隔离 provider 能把同步扫描从行枚举推迟到 Shell 请求列值时，并复用现有有界快照；当前调用仍在 `GetDetailsOf` 的调用线程同步完成，不是后台扫描，也没有确认实际 ExplorerBrowser 是否会为产品列发出对应请求。大量可见文件夹可能仍把工作压在 Shell 视图线程；下一步应将请求阶段与视图实际消费路径对照，并测量多行首次值请求的阻塞成本，再评估是否能安全地异步填充并局部更新。

### 2026-11-01：隐藏 DefView 的多行按需请求范围与扫描成本

将按需模式接入现有 Private Desktop WPF `IShellView` 探针，使用 100 个目录（每个 10 个文件）、一个两层子目录和一个普通文件，共 102 行。`CreateViewWindow` 稳定后、探针主动读取整列前，DefView 已请求 22 行大小文本并触发 22 次扫描（23 次指标函数调用、213 个后代条目）；随后探针显式读取全部 102 行文本，大小列请求数升至 102、扫描数升至 101（102 次指标调用、1,003 个后代条目）。累计扫描器耗时约 15.7 ms。目录值/文件值仍分别显示 10/4；`CompareIDs` 阶段扫描数为 0。NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台、最后输入和隔离清理守卫通过。

这说明隐藏 DefView 初始化会请求一部分大小行值，而额外行值请求会按行触发同步计算；全 102 行的 101 次扫描是探针主动读取整列造成，不能解释为 DefView 冷启动必然请求全部目录。约 15.7 ms 只来自本机合成夹具和当前缓存状态，不是磁盘/网络目录性能承诺。仍未验证产品 ExplorerBrowser 的列请求、可见 UI 感受或异步回填；同步工作可能在 Shell 请求线程执行。下一步应评估后台扫描完成后仅更新对应行值的原型，并保持排序、选区、焦点和通知行为，不据隐藏探针直接迁移产品架构。

### 2026-11-01：后台扫描、STA 发布与 DefView 完整刷新闭环

在既有 `full-view-refresh-live-cache-publish` 隔离策略上增加 `full-view-refresh-background-scan-live-cache-publish`：先于 view STA 修改夹具目录，再用项目的 `RecursiveDirectoryScanner` 和 `DirectoryTreeScanCoordinator` 在 worker 扫描；扫描结果经 WPF `DispatcherSynchronizationContext` 回到 view 所属线程后写入当前 provider 快照，最后调用 `IShellView::Refresh()`。三次独立 Private Desktop WPF view 运行都确认 owner/worker/publish 线程为 `5/7/5`，新大小值 `1 B` 被显示，排序为 `small.bin|child`，选区与焦点保持，DefView 重枚举 2 行并执行一次大小比较；刷新读回分别为 140、172、157 ms。既有 `--common-listview-relative-size-contract` 亦通过，确认自有 ListView 的后台扫描、owner 线程发布、排序及选择/焦点合同仍通过。

此结果将真实扫描工作从 Shell STA 移走，并证明结果可在 STA 安全发布后被当前合成 DefView 消费；派生值仍需完整 view refresh 才更新，因此尚未实现单行更新。140–172 ms 是两行夹具的刷新到稳定读回时间，不是纯扫描延迟，也不能外推大目录耗时。未采集像素，不能判断刷新白闪；未验证真实 ExplorerBrowser、可见 UI、多窗格负载或网络路径。后台扫描策略只存在于 NativeProbe，产品仍未接入逐行递归大小列。

### 2026-11-01：500 个目录时按需列请求范围与成本

把 Private Desktop 隐藏 view 负载从 100 扩大到 500 个同级目录（每目录 10 个小文件，共 502 行），通过 `EXPLORER_DIRS_NATIVEPROBE_ONDEMAND_FOLDER_COUNT` 控制夹具目录数（1–500，默认 100）。两轮 500 目录运行均在探针主动读取全列前收到 22 个大小行请求，扫描 22 个目录、访问 213 项；随后探针主动读完全部行，累计 502 次大小行请求、501 次目录扫描、访问 5,003 项。两轮的初始阶段扫描耗时为 9.303/12.297 ms，整列读取后累计为 71.085/81.577 ms；Private Desktop 前台、输入和清理守卫均通过。默认 100 目录模式随后复跑通过；Release build 0 警告/错误。

这表明在该固定隐藏窗口尺寸下，DefView 初始大小请求数没有随总目录数从 100 增至 500 而增加；因此按需模式把初始同步扫描量限制在当前请求行附近，而完整列读取仍随目录总数线性增长。22 行是本机合成 view 的实测值，不是所有屏幕尺寸、分组/排序状态或 Shell provider 的固定可视行数。扫描耗时受缓存与本机负载影响，也不代表实际用户可见卡顿。该轮最初未加入后台刷新链路；后续 500 行刷新闭环见下一节。所有结果仍未验证产品 ExplorerBrowser、网络目录、多窗格并发或可见 UI。

### 2026-11-01：500 个目录的异步扫描与完整刷新闭环

将 Private Desktop WPF 合成 view 的后台扫描/STA 发布/`IShellView::Refresh()` 链路扩展到 500 个同级目录（每个 10 个文件，另含 `child` 与 `small.bin`，共 502 行）。初始阶段 DefView 请求 22 行、同步扫描 22 个目录并访问 213 项；探针读取全部大小文本后累计到 501 次扫描、5,003 项。后台更新目标 `child` 后，刷新重新枚举 502 行，大小值从 10 更新为 1，选区/焦点保持；稳定读回约 1,000 ms。刷新区间累计发生 500 次递归扫描并访问 5,000 项，且大小文本经缓存读出、没有额外 size 属性调用。NativeProbe Release build 0 警告/错误，Private Desktop 前台、输入与清理守卫通过。

这验证的是 500 行合成 Shell view 的完整重新枚举与状态恢复，不是单行刷新或可见 UI。该轮刷新测试没有先建立并验证递归大小排序键，因此不据此声称 500 行排序顺序在更新后已验证；100 行后台刷新用例覆盖了已排序两项目录的重新排序。500 次扫描主要是 256 项 LRU 上限下，全列主动读取后有缓存淘汰，刷新访问全列又重新计算的成本，不能代表正常初始视口会扫描 500 个目录。当前只在 NativeProbe 原型，未接 ExplorerBrowser 产品；未测像素闪烁、实际磁盘/网络目录或多窗格并发。下一步优先验证缓存容量/可见值请求与排序组合，再决定是否做单行更新以及是否值得迁移产品视图架构。

### 2026-11-01：500 个目录的递归大小缓存容量 A/B

为隔离 NativeProbe 加入仅在 provider 首次创建前可设置的快照上限，默认仍为 256；`EXPLORER_DIRS_NATIVEPROBE_RECURSIVE_SIZE_CACHE_CAPACITY` 可在探针中选 1–4096。相同 500 目录、502 行刷新夹具对比 256、501、512 项：初始阶段每轮都是 22 行请求/扫描、访问 213 项；探针主动读取全列后均为 501 次目录扫描、5,003 项。容量 256 时刷新触发 500 次额外扫描/访问 5,000 项，稳定读回 938 ms；容量 501 与 512 时刷新扫描均为 0，稳定读回分别为 672/625 ms。各轮更新值、502 行、选中项/焦点和 Private Desktop 前台、输入、清理守卫均通过。

对该合成负载，容量达到 501 个目录快照后可避免刷新时因 LRU 淘汰而重扫；容量 512 留少量余量。约 0.3 秒的刷新时间差不是固定性能承诺，仍包含 Shell 重新枚举；缓存增量内存没有单独采样，因此暂不能把更高容量作为产品默认值。该开关与自定义容量只属于 NativeProbe，不改变产品或现有 256 项默认 LRU 合同；NativeProbe Release build、原生 `/W4 /WX` adapter build 与 `--static-shell-column-folder-smoke` 通过，后者确认默认 256 项 LRU 合同且 `window-created=False`。没有建立大小排序键，也没有验证 500 行排序重排、可见刷新像素、真实磁盘/网络目录或多窗格。下一步应在 500 行负载中单独验证已排序状态更新和 DefView 单行更新能力，再结合缓存实际字节占用决定容量策略。

### 2026-11-01：缓存容量的内存测量边界

在 500 目录、全列主动读取后，NativeProbe 直接统计缓存条目及条目载荷：256 个快照包含 77,312 字节，501 个包含 151,292 字节，差 73,980 字节（约 72.2 KiB）。该值按每个 map 项的 `std::wstring` 对象、快照结构和路径 UTF-16 字符数计算；不含红黑树节点链接、分配器管理开销与分配余量，所以是可核验的载荷下限，不是完整堆占用。

同时采样同一探针进程的 Private Bytes/Working Set。每种容量三轮中，Private Bytes 的“整列读取前后差值”分别跨越约 -7.66 MiB 到 +0.08 MiB（256）及 -7.71 MiB 到 +0.14 MiB（501），两组高度重叠；前后绝对值还受探针运行时回收影响。因此进程级采样无法分辨约 72 KiB 的缓存载荷差，也不能据此推断实际常驻内存增加量。容量 501 消除该夹具刷新期的 500 次重扫有证据，但真实缓存总成本尚未量清，不调整产品默认容量。NativeProbe Release build、原生 `/W4 /WX` adapter build 与三轮 `--synthetic-recursive-directory-size-large-background-refresh-wpf-host-private-desktop`/容量探针通过；前台、输入、清理守卫均通过。

### 2026-11-01：500 行递归大小排序刷新重排

加入 NativeProbe 专用 `large-view-sort-refresh` 策略。为了证明更新确实改变相对次序，夹具将 `child` 初始大小设为 30 B、`folder-000` 设为 20 B；列头建立升序后，刷新前 `folder-000` 位于 `child` 前。后台扫描发布 `child=1 B` 快照并完整刷新后，Shell 再比较 1,451 次，502 行保持，`child` 移到 `folder-000` 前，刷新过程未触发递归目录重扫；选区和焦点经现有快照恢复成功，稳定读回约 1,172 ms。Private Desktop 探针退出码 0，前台、输入与隔离清理守卫通过，NativeProbe Release build 0 警告/错误。

这次同时纠正此前的排序判据：Shell 将文件夹和文件按其自身规则分组，不能用目录 `child` 与普通文件 `small.bin` 的相对位置断定递归大小排序是否生效。Shell 的 `IFolderView2` 还报告映射后的排序属性键，而非 provider 显示列的私有键；探针因此结合实际列头操作、比较器调用、升序方向和两个目录的相对位置确认行为。约 1.17 秒包含 502 行重枚举和状态恢复，不是纯排序耗时；这是隐藏合成 `IShellView` 原型证据，不代表可见 UI 刷新观感或 ExplorerBrowser 产品已接入。下一步仍需独立评估只更新单行的 Shell 支持路径，并确认它能否在排序下触发正确重排。

### 2026-11-01：`SFVM_UPDATEOBJECT` 单行刷新探针

为 100 个同级目录（102 行视图）建立递归大小升序，初始 `child=30 B` 排在 `folder-000=20 B` 之后；生产扫描器将活动 provider 快照更新为 `child=1 B` 后，分别调用 `SHShellFolderView_Message(SFVM_UPDATEOBJECT)` 并传入相对 PIDL、绝对 PIDL 对。两种调用都返回 `0`，但等待 2.5 秒后大小仍是 `30`、排序位置未变；行数从 102 变为 103，多出一个空名称行。没有触发重新枚举或比较回调，选择/焦点保持，列与排序设置未变。NativeProbe Release 构建无警告/错误，两个隐藏 Private Desktop 探针退出码为 0，前台、输入和隔离清理守卫通过。

在此宿主上，返回 `0` 不代表派生属性已刷新；相对和绝对 PIDL 两条形式都没有达到更新目的，还引入额外空行。因此不把 `SFVM_UPDATEOBJECT` 接入产品。Microsoft 文档描述了传入旧/新 PIDL 对的默认视图更新消息，但同时提示该函数可能变化或不可用，见 [SHShellFolderView_Message 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shshellfolderview_message)。当前只有完整 `IShellView::Refresh()` 被验证能重取递归大小值并在排序下重排；其限制仍是全视图重枚举和未验证用户可见闪烁，见 [IShellView::Refresh 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-refresh)。以上证据仅来自合成隐藏 Shell view，不证明所有系统版本或第三方 namespace 的行为，也未接入 ExplorerBrowser 产品。

### 2026-11-01：`IShellFolderView` 单项更新与排序重排探针

在同一 100 目录、102 行隐藏 DefView 合成负载中，先建立递归大小升序（`child=30 B`、`folder-000=20 B`），后台发布 `child=1 B` 后分别调用 `IShellFolderView::RefreshObject` 和 `UpdateObject`。`RefreshObject` 返回 `S_OK` 并给出项目索引，但值仍是 30，且列表多出一个空名称行；`UpdateObject` 返回 `S_OK`，值更新为 1，但也多出空名称行且没有比较回调或排序重排。之后测试 `Rearrange(0)`：可移除空行，却把 Shell 排序状态改回 Name；重设原大小排序后值虽更新，`CompareIDs` 仍未再次调用。再反向切换排序方向，`child=1` 仍留在 `folder-000=20` 前面，直接证明行序没有按新的降序重新计算。两种重排路径均保留了探针选区和焦点；结果分别为 102 行无空行，但排序不正确。调用均返回成功，因此 HRESULT 不能单独证明派生列刷新与排序完成。

Microsoft API 页面说明 `RefreshObject` 是重绘指定项目，`UpdateObject` 是以新项目替换旧项目；本探针复现了其调用，但派生递归列值依赖 provider 缓存，结果仍不满足“值更新且排序位置正确”的门槛。另需考虑 `IShellFolderView` 自 Windows 7 起已不再供应用使用，Microsoft 建议使用 `IFolderView2` / `IFolderView`；即使当前 Shell 对象仍可 QueryInterface，也不应把该旧接口作为产品依赖。参考 [RefreshObject](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ishellfolderview-refreshobject)、[UpdateObject](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-ishellfolderview-updateobject) 和 [IShellFolderView 接口状态](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-ishellfolderview)。

结论：该单行 API 路线关闭，不接入 ExplorerBrowser。当前有证据的递归大小值更新和正确重排仍需完整 `IShellView::Refresh()`，代价是整视图重枚举；隐藏 Private Desktop 合成 view 未测可见刷新闪烁、真实 filesystem 多窗格体验或产品接线。

### 2026-11-01：为按需递归大小列补齐隔离 watchdog 入口

复核发现上述按需列值探针已有 NativeProbe 命令，但 `scripts/native-probe-watchdog.ps1` 没有对应映射；直接用统一 watchdog 调用时会在启动前以“不支持的 probe”退出。将该场景加入 synthetic Shell 的唯一 CLSID/夹具生命周期、CLI 映射和超时 dump 保护，再从 Release 构建产物经 watchdog 重跑。100 目录、102 行的 WPF Private Desktop view 初始请求 22 行并同步扫描 22 个目录、访问 213 个后代条目；主动读取全部行后累计 101 次扫描、1,003 个条目，比较阶段扫描为 0。该轮扫描计时为 8.635 ms / 24.333 ms，随机器缓存和负载变化，不作为性能承诺；缓存载荷统计由 22 项增至 101 项、30,492 字节，不含分配器开销。探针退出码 0，前台 HWND 与 last-input tick 未变化，临时注册和夹具清理通过。日志：`src/.state/native-probe-watchdog/20261101-114438-21280a4f.stdout.log`。

本次只修复隔离测试入口并复验现有原型，没有改产品宿主或发布版本。结果再次表明“按需”是列值被 Shell 请求时同步递归扫描，不等同于后台扫描；刷新原型虽已验证后台扫描后通过完整 view refresh 更新和重排，但单行派生值刷新仍未找到可靠方案。没有测可见 UI 阻塞/闪烁、真实磁盘或网络路径、多窗格并发、ExplorerBrowser 列请求及产品接线。

### 2026-11-01：按需递归大小值的后台扫描与 Shell 回读

在隔离 provider 中增加异步按需模式：首次 `GetDetailsOf` 对未缓存的目录列值立即返回空字符串，并把扫描放到 worker；扫描前后校验目录文件身份，成功后写入有界快照，再发送 `SHCNE_UPDATEDIR`。静态 smoke 用门闩证明回调在 worker 开始前已返回空值、worker 发布 10 B、重复读取命中缓存。WPF Private Desktop `IShellView` 探针进一步证实实际 Shell 通知闭环：初始请求后 worker 完成两项目录扫描，活动 view 收到 `SFVM_FSNOTIFY`，随后再次请求列值并读到 `child=10`；列请求计数从 3 增至 6。原同步按需对照仍通过，100 个目录、102 行显示值为目录 10 / 文件 4，排序比较阶段递归扫描为 0。Native adapter `/W4 /WX`、NativeProbe Release（0 警告/错误）、异步无窗口 smoke、异步 Private Desktop watchdog 和同步按需 Private Desktop watchdog 均通过；隔离前台、输入和临时 Shell 注册清理守卫通过。

这是 NativeProbe 的异步行为原型，不是产品功能。当前样例仅验证一个 10 B 目录在隐藏合成 DefView 中从空值回填；worker 采用每目录一个 detached thread，尚未做有界线程池、取消/关闭生命周期、并发多窗格压力或失败后重试策略评审，不能直接移植为产品实现。`SHCNE_UPDATEDIR` 会让 Shell 重新请求并可能重枚举目录；本探针没有量化可见闪烁、排序状态下的异步回填重排或真实目录 I/O。下一步先评审受控后台队列与关闭/失效生命周期，并在隔离 view 中测量通知造成的重枚举范围；产品 ExplorerBrowser 仍未接线。

### 2026-11-01：异步扫描改为有界队列并验证 view 释放取消

将上述 detached worker 改为隔离 adapter 内的固定 2 worker 队列，待处理任务上限为 256；超过上限时队列返回配额错误，列值回调仍以空值继续，后续 Shell 读取可以重试。扫描任务在排队前对 provider AddRef，结束后释放；DLL 静态队列在卸载时停止接收任务、排空并 join worker。创建 Shell view 时开启该 provider 的扫描通知；显式释放 view hold 时标记取消并禁止后续 `SHCNE_UPDATEDIR`。Private Desktop 探针在异步模式 teardown 中先释放 view hold 再销毁 Shell 子窗口；无窗口 smoke 另外在队列任务被门闩阻塞时显式释放 hold。worker 在等待门闩唤醒后和发布快照前都检查取消状态，避免关闭中的工作继续扫描或通知。

RED 阶段的 8 项并发合同先失败于缺失峰值计数导出；加入队列后，view 释放合同又复现了一个竞态：已经等待的 worker 被唤醒后仍会扫描。增加唤醒后的取消检查后，16 项静态夹具中前 8 项全部完成，峰值并发稳定为 2；后 8 项在模拟释放 view 后被取消，指标扫描调用为 0。无窗口 async smoke 连续 3 次通过。隐藏 WPF Private Desktop Shell 回读通过（`child=10`、列请求 3→6、扫描 2 次），同步按需 100 目录回归通过；adapter `/W4 /WX`、NativeProbe Release 0 警告/错误，前台/输入与隔离清理检查通过。

这仍是 NativeProbe 原型，不能说明产品宿主会在相同阶段释放 view hold：队列满载 256 的行为尚未做压力验证；固定线程在 adapter 加载期间保持空闲，尚未评估其常驻内存；失败任务目前会清除 pending 状态但没有自动重试策略。通知仍可能导致目录重枚举，尚未验证排序中的回填重排、通知风暴合并、真实路径耗时、可见刷新效果或 ExplorerBrowser 产品接线。下一步量测队列饱和和多窗格共享负载，并确认有限队列下可见目录最终能重试填值，再评估线程的空闲回收与通知合并。

### 2026-11-01：异步按需扫描队列溢出重试与 Shell 更新合并

将隔离异步 Shell view 压力探针扩展到主动读取全部目录列值，并统计成功扫描、实际发送的 `SHCNE_UPDATEDIR`、Shell 重枚举次数与峰值 worker。队列仍为 2 个 worker、最多 256 个排队任务；容量为 512 的 300 目录夹具越过队列容量后最终全部读到正确值：301 次扫描（300 个目录与既有嵌套目录）、4 次合并通知、1 次重新枚举，worker 峰值 2。典型 100 目录/256 快照上限对照也通过：101 次扫描、2 次通知、1 次重枚举。两轮 Private Desktop watchdog 退出码 0，前台 HWND 与最后输入 tick 不变，唯一隔离 Shell 注册和夹具清理通过；原生 adapter `/W4 /WX`、NativeProbe Release build（0 警告/错误）及无窗口静态合同通过。

另测 300 目录配 256 快照上限并在 60 秒内反复读取整列：没有收敛，累计 14,749 次扫描、145 次通知与 48 次重枚举。200ms 通知合并相较此前 50ms 试验的 551 次通知降低了刷新频率，但不能修复快照上限小于整列目录数造成的 LRU 淘汰/重扫。这个整列主动读取负载也超出初始可视行集合；结果用于暴露缓存容量边界，不代表用户初始进入目录会扫描全部 300 个目录。增加缓存容量可使该夹具收敛，但缓存实际堆开销及产品容量仍未决定。

Shell 通知现由有界队列合并：首个待发送更新后等待最多 200ms，再发送一次目录更新；合并任务与扫描任务共用两个 worker，因此该延迟是刷新风暴和扫描并发之间的原型取舍，尚未评估多窗格竞争、慢磁盘、长生命周期或可见排序回填。仍不接入 ExplorerBrowser 产品；实际用户可见闪烁、磁盘/网络路径表现、全列排序稳定性及容量的内存成本待后续独立门槛验证。日志：`src/.state/native-probe-watchdog/20261101-121229-e348ce5c.stdout.log`、`src/.state/native-probe-watchdog/20261101-121238-aefb2c50.stdout.log`；256/300 的边界复现日志：`src/.state/native-probe-watchdog/20261101-121021-e8f4c9af.stdout.log`。

### 2026-11-01：异步递归大小回填触发 Shell 排序重排

将异步按需递归大小列置于 500 个同级目录的实际隐藏 DefView 排序场景：目录 `folder-400` 与 `folder-401` 在初始可视请求范围之外，初始两者仍按名称顺序（400 在 401 前）；`folder-400` 的递归大小为 20 B，`folder-401` 为 10 B。探针先点击自定义大小列头建立升序并选中/聚焦 `folder-400`，再请求整列、让后台扫描回填。连续两轮均完成 501 个目录快照，Shell 重新枚举 1 次，分别发送 5/6 次合并更新通知；排序正确变为 `folder-401` 在 `folder-400` 前、`folder-400` 在 30 B 的 `child` 前，原选中和焦点仍落在 `folder-400`。峰值 worker 均为 2，Private Desktop watchdog 均 exit 0，前台/输入与隔离清理守卫通过；100 目录/256 缓存异步基准、NativeProbe Release build 及静态无窗口合同也通过。

500 目录轮的快照载荷统计为 151,292 字节（不含 map 节点/分配器开销）；进程 Private Bytes 与 Working Set 同时受 Shell/WPF/托管运行时影响，不能把本轮增量当作产品缓存实占。该排序结果证明本机隐藏合成 `IShellView` 可在通知驱动的整视图更新中用已回填值重新排序并保持选中/焦点身份；没有采样可见像素，不能证明无闪烁。仍是 NativeProbe，不是 ExplorerBrowser 产品接线；多窗格共享队列竞争、慢磁盘/网络、不同排序/分组及用户可见体验待验证。日志：`src/.state/native-probe-watchdog/20261101-121924-6b1aa3c1.stdout.log`、`src/.state/native-probe-watchdog/20261101-121947-50643c10.stdout.log`、常规回归 `src/.state/native-probe-watchdog/20261101-122014-b2b03f5f.stdout.log`。

### 2026-11-01：双活动 Shell view 共享异步递归大小队列

在现有隐藏 WPF `IShellView` 探针中新增双 view 模式：两个 view 同时绑定同一 synthetic 根目录，各自选中/聚焦不同目录，再分别读取 502 行递归大小列。500 个同级目录、512 条缓存容量下四轮复测均通过：两 view 都读回正确值，共扫描 501 个唯一目录（child 与 500 个同级目录），重复扫描 0，峰值 worker 2，发送 5–6 次合并 `SHCNE_UPDATEDIR`，两 view 聚合 `EnumObjects` 增量 2–3，原选中/焦点分别保留。典型单 view 100 目录/256 缓存对照通过（101 扫描、2 通知、枚举增量 1）。两 view 每次 watchdog exit 0；前台 HWND/最后输入 tick 不变，隔离注册和 fixture 清理通过；NativeProbe Release build 0 警告/错误，无窗口静态 Shell 合同通过。

结果说明在该同进程 synthetic provider 中，两 view 的 provider 对象复用同一个有界快照 store；重叠读取没有重复递归扫描。通知会送达共享根下的多个 view；四次复测记录 5–6 次合并通知与 2–3 次聚合枚举增量，均高于单 view 对照的 2 次通知与 1 次枚举。该探针是两个同进程隐藏 view 的合成 filesystem 证据，不能外推到四窗格产品、多窗口独立进程、网络路径、第三方 namespace、真实用户可见闪烁或 ExplorerBrowser。下一步把同一压力扩展到四个活动 view，并观察每个 view 的状态恢复和枚举量；仍不因此接入产品列。

日志：`src/.state/native-probe-watchdog/20261101-122213-7b0225b8.stdout.log`、`src/.state/native-probe-watchdog/20261101-122240-d97f55f2.stdout.log`、`src/.state/native-probe-watchdog/20261101-122434-05b9957c.stdout.log`、`src/.state/native-probe-watchdog/20261101-122525-965e7b93.stdout.log`；单 view 对照：`src/.state/native-probe-watchdog/20261101-122308-e547e558.stdout.log`。

### 2026-11-01：四活动 Shell view 共享异步递归大小队列

将共享根目录模式扩展到四个同时活动的隐藏 WPF `IShellView`，每个 view 选中/聚焦不同的同级目录。500 个同级目录、512 条缓存容量下两轮均回填四份 502 行列值，只执行 501 次唯一目录扫描（重复 0），峰值 worker 2，每轮 5 次合并 `SHCNE_UPDATEDIR`，聚合 `EnumObjects` 增量 4；四份选中/焦点状态均保留。100 目录/256 缓存的小规模对照为 101 次扫描、2 次通知、4 次枚举增量。旧的双视图环境变量入口以 500 目录复测通过：501 次扫描、6 次通知、3 次聚合枚举，两个 view 状态均保留。所有 watchdog exit 0，前台 HWND/最后输入 tick 不变，Private Desktop 及 fixture 清理通过；Release build 0 警告/错误，静态 Shell 合同 smoke 通过。

四视图结果说明同进程 synthetic provider 的有界快照 store 能在四个共享根 view 之间避免重复扫描；五次合并变更通知使四个活动 view 各产生一次聚合重枚举。当前仍未覆盖四个不同/部分重叠根目录、真实磁盘和网络慢 I/O、取消/关闭竞争、ExplorerBrowser 产品接线或可见刷新。下一步验证四个不同根目录共享 worker 时的队列饱和、取消与逐 view 回填，再决定产品列集成门槛。

日志：`src/.state/native-probe-watchdog/20261101-122747-98b51977.stdout.log`、`src/.state/native-probe-watchdog/20261101-122850-1e74e1e1.stdout.log`；100 目录对照：`src/.state/native-probe-watchdog/20261101-122817-16b85b79.stdout.log`；双视图兼容复测：`src/.state/native-probe-watchdog/20261101-122830-385bfe76.stdout.log`。

### 2026-11-01：四个不同根目录的异步递归大小回填

将四 view 隐藏 WPF `IShellView` 探针扩展为每个 view 绑定独立的 `Pane1`–`Pane4` 根目录，并记录队列拒绝计数。100 个目录/根、512 条缓存容量下，四个 view 完成 404 次唯一目录扫描，重复 0，拒绝 0，发送 9 次合并通知、聚合 `EnumObjects` 增量 4；四份 102 行值均正确，选区/焦点全部保留。500 个目录/根、2,048 条缓存容量下，四 view 完成 2,004 次唯一扫描，重复 0，拒绝 0，发送 27 次合并通知、聚合枚举增量 5；四份 502 行均读回正确值并保留各自状态。500 根场景创建约 20,000 个夹具文件。四个共享同根的 100 目录回归也通过：扫描 101 次、重复 0、拒绝 0、通知 2 次、枚举增量 4。Release NativeProbe 构建 0 警告/错误；各次 Private Desktop watchdog exit 0，前台、最后输入和清理守卫通过。

首次把 `WaitForNames` 的稳定窗口设为 2.5 秒时，50–100 目录的不同根场景曾间歇超时并读到空列表；回看失败轮次的底层 `EnumObjects` 已成功枚举 102 项，但列表尚未稳定。将探针等待上限扩到 10 秒后，50、100、500 目录场景均通过。因此该现象属于探针等待门槛不足，不能归因于目录枚举失败或通知根路径错配。

需要区分：500 目录四根场景总共提交 2,004 个独立扫描请求，但实际队列拒绝数为 0；这没有验证不同根目录下的队列饱和/拒绝后重试。既有单视图压力合同覆盖过 256 项队列拒绝与重试，不同根并发饱和仍待显式压测。本探针依旧只证明同进程 synthetic provider 和隐藏 DefView，不代表 ExplorerBrowser 产品、多进程、慢网络路径或可见刷新观感。日志：`src/.state/native-probe-watchdog/20261101-125102-354991af.stdout.log`、`src/.state/native-probe-watchdog/20261101-125131-94eec217.stdout.log`、`src/.state/native-probe-watchdog/20261101-125251-8ca33e73.stdout.log`。

### 2026-11-01：四个不同根目录强制队列饱和

增加仅供 NativeProbe 使用的队列闸门：`EXPLORER_DIRS_NATIVEPROBE_ASYNC_FORCE_QUEUE_SATURATION=1` 会在四个不同根目录的 view 启动前暂停两个 worker，让调用方确定性填满 256 项队列；正常 probe 默认不启用。80 个目录/根、512 条缓存容量时，共需 324 个目录扫描任务；闸门释放前有 66 个入队请求被拒，实际完成 258 个（256 个排队任务与两个正在等待的 worker）。Shell 发送 4 次合并更新并使四个 view 各重枚举一次，但列值未完全恢复：主 view、第二 view和第三 view为 82/82，第四 view 为 16/82；扫描保持 258/324，峰值 worker 为 2。等待 Shell 行数稳定后结果仍相同，说明此原型在队列拒绝后没有可靠的逐行重试保证。

本结论只适用于闸门强制制造的背压，不代表普通负载必现，也不证明 ExplorerBrowser 产品存在相同队列行为。两次无闸门复测曾因探针在 view 异步重枚举时立即要求精确行数而读到暂时的 0 行；`ReadSharedViewColumns` 改为等待 `child` 行和预期行数后，无闸门 80 目录/根对照通过：324 次唯一扫描、拒绝 0、四 view 各 82/82、通知 7 次、聚合重枚举 4 次，选区和焦点均保留。另在队列排空后对四个 ListView 的全部行发送 `LVM_UPDATE`，再对四个 `IShellView` 调用 `Refresh`；扫描仍为 258/324、拒绝仍为 66，虽额外触发聚合枚举也没有恢复空白列值。因此只靠常规刷新无法让当前原型重建被丢弃的排队请求。接下来要在 provider 侧保留可取消的待处理请求，或把多个行值合并成有界批量扫描；不能依赖 Shell 再次请求。

NativeProbe Release build 成功，0 警告/错误；原生 adapter `/W4 /WX` 构建成功。无闸门通过日志：`src/.state/native-probe-watchdog/20261101-130917-3b6cb733.stdout.log`；强制饱和及刷新恢复失败日志：`src/.state/native-probe-watchdog/20261101-131142-3eb10c9e.stdout.log`；先前暂时行数采样失败日志：`src/.state/native-probe-watchdog/20261101-130709-027b57c3.stdout.log`、`src/.state/native-probe-watchdog/20261101-130736-70fce9ed.stdout.log`。

### 2026-11-01：不同根目录下按视图合并队列的原型评估

先前两轮报告的重复扫描是在 256 项默认缓存下执行的；四根共有 324 个快照，工作负载超过缓存容量，不能据此归因通知或 provider 路径。将缓存明确固定为 512 后，无批处理基线恢复为 324 次唯一扫描、0 拒绝、四 view 均 82/82，并保留所有选区/焦点；日志：`src/.state/native-probe-watchdog/20261101-132257-f002b288.stdout.log`。

随后在 NativeProbe 中实现按需行请求收集：Shell 对某个未缓存目录请求列值时，provider 将该目录加入本视图的小批次，同一 view 只排入一个 worker task；工作线程在短暂收集窗口后逐行扫描，按目录身份核验并写回共享快照，结束后合并发出一次 `SHCNE_UPDATEDIR`。它不枚举/扫描未被 Shell 请求的同级目录。四个不同根目录、每根 80 个普通目录及一个嵌套目录，缓存 512 时，在队列门闩释放前恰好排入 4 个 view task；所有 324 个唯一扫描完成、拒绝 0、重复 0、峰值 worker 2、通知 4、聚合重枚举 4；四 view 的 82/82 列值正确。一次相同参数的运行报告第四 view 的焦点未保留（`20261101-133304-2a875366.stdout.log`）；加入失败状态诊断后，最新源码连续两轮均保留四 view 的选区/焦点：`20261101-133349-610344cd.stdout.log`、`20261101-133449-9a54509f.stdout.log`。更早同策略轮次：`20261101-133104-70074f0d.stdout.log`。焦点结果仍有过一次不稳定，暂不视为产品验收完成。

对照原逐行队列：固定同样的 512 缓存和 324 个请求，门闩释放前拒绝 66 项，之后只扫描 258 项；`LVM_UPDATE` 与 `IShellView.Refresh` 没有补回缺失的 66 个值，日志：`src/.state/native-probe-watchdog/20261101-133136-05dbf78f.stdout.log`。该结果说明按需行批次在本机合成四 view 压力场景能减少队列项并消除本次拒绝，但它仍只是 NativeProbe 的 `IShellView` 原型，不是 ExplorerBrowser 产品行为，也未证明普通低负载时的延迟、取消/关闭竞争、慢磁盘/网络或可见排序体验。后续应验证只请求少量行和 view 关闭取消，再评审产品接线。原生 adapter `/W4 /WX` 与 NativeProbe Release 均构建成功，0 警告/错误。

### 2026-11-01：按视图批次的同根四视图共享缓存

放开 batch probe 的同根四视图组合后，使用相同 80 目录夹具和 512 缓存跑两轮。四个共享同一 backing root 的 view 都读回 82/82 行，累计只扫描 81 个唯一目录（child、80 个同级目录），重复扫描 0、队列拒绝 0、峰值 worker 1、通知 2、聚合 `EnumObjects` 增量 4；四份选区和焦点保留。日志：`src/.state/native-probe-watchdog/20261101-133655-890a7c79.stdout.log`、`src/.state/native-probe-watchdog/20261101-133713-ac00c843.stdout.log`。这是同进程 synthetic provider 的隐藏 `IShellView` 证据；更大同根负载与取消竞争尚未验证。

### 2026-11-01：同根多视图扫描合并与关闭取消

先用 500 个同级目录和四个共享根目录的 view 观察 Shell 自动请求范围。探针只额外读取主 view 的前三行，但 Shell 已对多个 view 请求了 1,525 个按单 view 去重的列项；旧实现执行 977 次扫描，已超过同根 501 个唯一目录，确认了跨 `SyntheticSortFolder` 实例的并发重复扫描。扫描缓存只在任务结束后写入，而待处理集合原先属于每个 folder 实例，因此不同 view 会同时扫描相同路径。首次观测日志：`src/.state/native-probe-watchdog/20261101-async-partial-before-dedupe.stdout.log`。这也说明“逐行按需”受 Shell 的实际请求粒度限制：在此 500 行详情视图中，Shell 自己请求了大部分行，三次手动行读取不能被当成仅扫描三行的证据。

在 NativeProbe 的共享快照存储内增加按解析路径的扫描占位：缓存未命中时只有一个 view 扫描该路径，其它 view 等待该快照写入或宿主取消后再尝试。修正后，同一个 500 目录、四同根 view 场景扫描恰为 501 次，重复扫描 0、队列拒绝 0、四 view 全部 502/502、通知 2、峰值 worker 1，四份选区与焦点保留；日志：`src/.state/native-probe-watchdog/20261101-async-same-root-500-after-dedupe.stdout.log`。在相同 Shell 自动请求负载下复跑部分行观察，仍请求 1,525 个按 view 去重条目，但扫描保持 501 次；日志：`src/.state/native-probe-watchdog/20261101-async-partial-after-dedupe.stdout.log`。四个不同根目录、每根 80 项的回归场景仍为 324/324 唯一扫描、拒绝 0、峰值 worker 2、四 view 均 82/82 且选区/焦点保留；日志：`src/.state/native-probe-watchdog/20261101-async-distinct-root-after-dedupe.stdout.log`。

### 2026-11-01：四视图队列与缓存压力闭环

先改造强制队列压力合同，让四个不同根 view 在两个 worker 暂停期间，必须把所有请求排入有界队列且不得拒绝；原 256 项队列在 500 目录/根负载下按预期 RED，2,004 个扫描需求中拒绝 1,746 个。随后将 NativeProbe 队列容量提高到 2,048，刚好覆盖探针允许的四 view ×（500 个同级目录 + child）最大 2,004 个扫描请求，仍保留固定容量。原队列溢出时为了补请求而执行的手动 `LVM_UPDATE`/全 view `IShellView.Refresh` 已移除：新合同验证自然通知回填，不再人为触发额外重枚举。

默认递归大小缓存容量也从 256 提高到 2,048。四个不同根、每根 500 个目录、2,048 缓存的 Private Desktop 运行通过：2,004 次唯一扫描、重复 0、队列拒绝 0、两个 worker、四次通知、四次聚合 `EnumObjects`，四份 502/502 大小值正确，四视图选区/焦点均保留。缓存记录载荷与路径字符统计为 661,280 字节；它不含 `std::map` 节点和分配器开销。该次进程 Private Bytes 为 95,588,352 字节，但没有同进程基线差值，不能把它归因于缓存。容量 256 的 80 目录/根对照仍读到全部值，但扫描升至 915/324、通知 12 次，确认 LRU 淘汰会在 Shell 后续读取中引起重复扫描；缓存 2,048 的同一场景收敛为唯一扫描、每个 view 一次通知。

Release NativeProbe 与原生 adapter `/W4 /WX` 构建通过（0 警告/错误）；两次强制压力运行的队列/缓存/值合同均完成，前台和最后输入守卫不变，Private Desktop/夹具清理通过。最新一次将通知断言收紧为“恰好每 view 一次”后，扫描、缓存、通知和列值仍达标，但第四 view 的选区/焦点丢失，watchdog 以失败退出；之前两轮上限压力运行保留了四份状态，表明高负载选区保持仍有间歇性，尚不能算稳定合同。日志：`src/.state/native-probe-watchdog/20261101-155130-d9d3f545.stdout.log`（RED，旧队列拒绝）、`src/.state/native-probe-watchdog/20261101-155458-f3733d60.stdout.log`（首轮 GREEN，缓存 4,096）、`src/.state/native-probe-watchdog/20261101-155617-d1a715fa.stdout.log`（缓存 256 的重复扫描对照）、`src/.state/native-probe-watchdog/20261101-155756-2eee9d60.stdout.log`（GREEN，默认缓存 2,048）、`src/.state/native-probe-watchdog/20261101-160121-0f8362f1.stdout.log`（队列/缓存/通知通过，第四 view 状态间歇失败）。这仍是 NativeProbe 合成 provider 与隐藏 DefView 证据，不代表 ExplorerBrowser 产品接线、真实磁盘/网络延迟、可见刷新体验或真实应用的内存增量；产品缓存容量需在产品接线前以真实负载和内存差值再评审。

关闭竞争有两种场景。闸门关闭期间排入四个 view 批次后退休全部 view，再放行 worker；四批均结束，扫描数 0、Shell 通知数 0，日志：`src/.state/native-probe-watchdog/20261101-async-same-root-cancel-after-dedupe.stdout.log`。再单独关闭扫描任务所属 view，保持另外三个同根 view 存活并等待；四批均结束，只扫描 81 个唯一目录、发出一次合并通知，存活 view 均读到正确列值并各自重新枚举（`EnumObjects` 增量 3），日志：`src/.state/native-probe-watchdog/20261101-async-single-view-retire-same-root.stdout.log`。首次单 view 探针只等待固定 350ms 就采样，曾在通知已经发出但 Shell 尚未完成重枚举时误判；改为按列值和枚举增量等待后复测通过。

Native adapter `/W4 /WX` 与 NativeProbe Release 均构建成功，0 警告/错误。以上仍是隐藏 synthetic `IShellView` 原型，不是 ExplorerBrowser 产品接线或可见 UI 验收；慢磁盘/网络延迟、队列背压及可见交互体验仍待验证。

### 2026-11-01：单视图批次与列头排序

将 NativeProbe 的批次收集模式开放给单个 Shell view，使用 500 个同级目录（加一个嵌套目录和普通文件）、缓存 512，与同参数逐项异步队列对照。逐项模式完成 501 次扫描、队列拒绝 0、峰值 worker 2、合并通知 6 次，列值全部就绪；扫描器累计用时 253.688 ms。单视图批次也完成 501 次扫描、拒绝 0，峰值 worker 1，通知 1 次，列值全部就绪；扫描器累计用时 166.628 ms。两者首次 Shell 自然请求均为 22 行，后续探针读取整列后达到 501 个目录快照。日志分别为 `src/.state/native-probe-watchdog/20261101-async-single-view-500-per-row.stdout.log` 和 `src/.state/native-probe-watchdog/20261101-async-single-view-500-batch.stdout.log`。该合成场景中批次减少通知并缩短扫描时间；它不是磁盘 I/O 上的用户等待时间，也不能外推到慢盘或网络目录。

再用 `large-view-sort-refresh` 场景检查 500 项列头排序：扫描仍为 501 次、队列拒绝 0，批次完成后离屏的 folder-401 排在 folder-400 前，folder-400 排在大小为 30 的 child 前，选中/焦点状态保留；比较阶段没有触发目录指标扫描。日志：`src/.state/native-probe-watchdog/20261101-async-single-view-500-sort.stdout.log`。NativeProbe Release build 通过，0 警告/错误。此结果仍来自隐藏 synthetic Shell view；ExplorerBrowser 产品没有递归大小列接线，也未做可见列表体验验收。

为后续慢存储压力对照，在 NativeProbe 原生 adapter 中加入默认关闭的扫描延迟注入，环境变量 `EXPLORER_DIRS_NATIVEPROBE_ASYNC_SCAN_DELAY_MS` 可设置 0–1000 ms，并被计入单项扫描耗时；产品源码没有该开关。NativeProbe Release 与原生 adapter 构建通过，`git diff --check` 通过。当前未执行 `IShellView` 压力探针：仓库规则要求先有明确的空闲桌面授权，隐藏 Shell 宿主也可能影响前台。故尚无慢扫描下逐项队列与按 view 批次的完成率、队列拒绝或消息泵实测数据。

### 2026-11-01：无窗口慢扫描队列验证

将相同延迟开关接入不创建 Shell view 的 `--synthetic-async-on-demand-recursive-size-column-smoke`，用 8 个目录任务核对后台逐项队列。0 ms 基线累计扫描 9.472 ms（平均 1.184 ms）；50 ms 注入累计 413.776 ms（平均 51.722 ms）；200 ms 注入累计 1,616.081 ms（平均 202.010 ms）。三轮均使用 2 个 worker 完成 8/8 个快照，首次列值回调保持立即返回空值、之后读到 10 B，重复读取命中缓存；关闭 provider 后剩余 8 个排队请求均被取消，fixture 无 Shell view 创建。日志分别为 `src/.state/native-probe-watchdog/20261101-async-latency-smoke-0ms.stdout.log`、`...-50ms.stdout.log`、`...-200ms.stdout.log`。

这只验证队列任务层面对人为延迟的处理与取消，不包含 `IShellView` 里的 DefView 行请求、菜单/排序消息泵、队列背压或 view 批次策略；不能据此判定慢磁盘/网络上的可见体验。下一步仍需在授权隔离桌面上对 500 项单 view 的逐行与按 view 批次做延迟对照，比较队列拒绝、值最终就绪、通知数、选区/焦点及消息泵响应，再决定是否继续产品架构评审。

### 2026-11-01：无窗口逐项与 view-batch worker 对照

把同一无窗口 async smoke 扩展为逐项/按 provider 批次两种调度，每轮向一个 provider 连续请求 8 个目录大小，验证回调立即返回空值、8 个扫描均发布、重复读取命中缓存，以及 provider 释放时未启动的任务被取消。逐项模式排 8 个扫描任务并用两个 worker；批次模式将 8 项合并为一个 task、用一个 worker，整批可取消。两者在 0/50/200 ms 注入下均完成 8 项，无窗口、无拒绝。

| 单项扫描注入 | 逐项模式累计扫描 / smoke 墙钟 | view-batch 累计扫描 / smoke 墙钟 |
| --- | ---: | ---: |
| 0 ms | 8.693 ms / 212.860 ms | 5.091 ms / 346.392 ms |
| 50 ms | 415.991 ms / 424.117 ms | 410.680 ms / 733.627 ms |
| 200 ms | 1,613.059 ms / 1,025.273 ms | 1,612.369 ms / 1,936.719 ms |

墙钟包含合成 fixture、排队门闩、等待 8 项完成和取消分支，不是纯扫描时间。累计扫描时间相近；view-batch 的单 task 串行处理让 50/200 ms 样本的总等待分别约为逐项模式的 1.73/1.89 倍，0 ms 时也多约 134 ms，显示当前 25/50 ms 合并等待和单 worker 串行的成本。逐项模式的两个 worker 则能并行延迟任务。另一方面，该 smoke 只有 8 项且没有制造队列压力，不能反驳四个 80 项 view 的隔离 Shell 探针中“324 个逐项任务产生 66 次拒绝，而四个 view-batch task 零拒绝”的结果。

因此当前证据不支持把单 worker 全 view 串行批次直接作为最终策略。后续原型应评估按 view 合并入口、再以有界小块利用共享两 worker 队列的调度，并在真实隐藏 `IShellView` 中对照队列拒绝、值最终就绪和状态保持。此处只测了无窗口 provider task；未验证 DefView 消息泵、视图关闭时的活动扫描、磁盘/网络 I/O 特征、可见体验或产品接线。日志：`src/.state/native-probe-watchdog/20261101-async-no-view-per-row-0ms.stdout.log`、`...-50ms.stdout.log`、`...-200ms.stdout.log` 与对应 `view-batch` 日志。

### 2026-11-01：无窗口 500 项 view-batch 回读与缓存上限

扩展 `--synthetic-async-on-demand-recursive-size-column-smoke`，可在 view-batch 模式请求 500 个目录大小项，并对全数验证回读、对另 500 项验证 provider 释放后的取消；同时打印请求数和扫描计时。首轮探针按 500 项读取后值为空，定位到 smoke 直接构造的 `SyntheticSortFolder` 没有应用 `SetSyntheticRecursiveSizeCacheCapacityForProbe` 配置，仍沿用 256 项默认缓存。修正构造路径后，两轮 500 项缓存容量 smoke 通过：500/500 扫描及值回读成功，第二批取消且未启动扫描，view-batch 峰值 worker 1；扫描累计 324.623/328.490 ms，完整 smoke 墙钟 1,932.757/2,247.827 ms（含夹具、门闩、全值校验与取消）。同轮 8 项 view-batch 与逐项模式分别通过，峰值 worker 为 1 与 2。

该结果证明一个 provider 的 500 项按需批次不会因 256 个队列槽位而拒绝请求，但全批串行工作约 328 ms；墙钟不等于用户等待时间或实际磁盘性能。它没有解决 50/200 ms 注入下 view-batch 比逐项双 worker 更慢的已知结果，也没有验证慢磁盘、Private Desktop DefView、多窗格、排序回填或可见体验。因此尚无证据将单 worker batch 作为最终调度策略；有限并行 chunk 仍需单独修正并完成缓存回读合同后才可继续。没有产品接线或 UI 窗口。

### 2026-11-01：按视图合并后使用两个 worker 并行扫描

为解决批次调度保留队列容量却把所有扫描串行压在一个 worker 的问题，仍保持“一个 view 一个协调 task”，每次取出的请求至多再切成两个半批：其中一半交给共享队列的第二 worker，另一半由协调 worker 扫描。两部分共享完成计数，确保 view 关闭取消、快照发布和最终 Shell 更新通知仍按整批收敛；额外队列项最多一个，不按行扩展。

无窗口 8 项批次 smoke 在 10 ms/目录延迟下注入后峰值 worker 为 2，8/8 个目录值正确回读，重复读取命中缓存，关闭 provider 后第二批任务被取消；扫描累计 100.944 ms，探针墙钟 437.284 ms。NativeProbe C# 同时断言批次和逐项模式都达到两个 worker；原生日志中的固定峰值描述已同步改为 2。500 项、2 ms 延迟的一轮也完成 500/500 扫描与缓存回读，扫描累计 1,858.328 ms、探针墙钟 2,826.218 ms；该大批次运行通过双 worker 峰值断言。静态 Shell folder contract、原生 adapter `/W4 /WX` 和 NativeProbe Release build 均通过，构建 0 警告/错误。

这证明无窗口 Synthetic provider 批次可利用现有两 worker，同时每 view 仍只需一个外层队列 task；没有验证并行扫描在真实慢盘/网络上的吞吐、四个不同根目录同时批次时的队列拒绝重试，也未复跑隐藏 DefView 的排序/焦点状态或测量可见刷新。仅 NativeProbe 原型，没有 ExplorerBrowser 产品接线；后续仍先在隔离隐藏 Shell view 验证多 view 背压及取消竞态，再决定是否进入产品架构评审。

### 2026-11-01：双 worker view-batch 的隐藏 Shell view 回读与排序

将 `EXPLORER_DIRS_NATIVEPROBE_ASYNC_BATCH_BY_VIEW=1` 接入现有 Private Desktop/WPF 隐藏 `IShellView` 场景。80 个目录、默认 256 缓存下，完整回读 82 行，81 次唯一扫描、队列拒绝 0、峰值 worker 2、通知 1、重枚举 1；所有列值就绪，隔离桌面清理与前台/输入守卫通过。该轮中途计数曾失败，定位为计数器分别读取时两个 worker 可能已有正在运行的扫描；只对异步中途快照放宽“指标计数必须与已完成扫描数精确相等”的断言，最终全值读取阶段仍保留精确计数关系。

500 个目录按递归大小排序时，默认 256 项 LRU 无法容纳 501 个唯一目录，实际重复扫描 7,224 次且未收敛；显式设为 512 后通过：501 次扫描、零队列拒绝、峰值 worker 2、通知 2、整视图重枚举 1。完整列表含 502 行，回填后 `folder-401` 排在 `folder-400` 前，`folder-400` 排在 30 B 的 `child` 前，选中/焦点保持。缓存 501 项载荷为 151,292 B；此轮进程 Private Bytes 变化为 -348,160 B，无法据此分离 Shell/运行时波动或推算缓存净内存。

这验证了该调度在当前 Windows 隐藏 DefView 下的排序重排及状态保持，但仅限合成 filesystem provider、单 view 和隔离桌面；没有测可见帧、真实慢磁盘/网络、多根四 view 背压与拒绝重试，也没有接入 ExplorerBrowser 产品。后续补测双 worker 批次在四个不同根目录的队列竞争、关闭/取消竞态，再评估产品迁移门槛。

### 2026-11-01：双 worker view-batch 的四根并发与关闭取消

四个不同根目录各有 80 个目录、512 项缓存时，四个隐藏 view 通过双 worker 按视图批次回填：324 个唯一目录扫描、重复 0、队列拒绝 0、峰值 worker 2、通知 4、聚合重枚举 4；四个 view 的 82/82 列值正确，选中/焦点分别保留。因为批处理将负载归并成四个 view task，此用例没有制造 256 项队列饱和，不能用来宣称溢出重试已覆盖。

四个同根 view 在队列门闩后全部关闭，4 个批次均完成取消结算，扫描 0、关闭后通知 0。另一轮只退役其中一个 view，剩余三 view 仍回填全部值：81 次唯一扫描、拒绝 0、通知 1、聚合重枚举 3。所有 Private Desktop watchdog exit 0，前台与最后输入未变，隔离桌面和夹具清理通过。

这补足同进程合成 provider 的四根并发、全关取消及单 view 退役接管证据；仍未覆盖队列被其它产品任务占满、慢网络/磁盘、多个 Explorer Dirs 进程的共享策略、真实 ExplorerBrowser 或可见刷新。递归大小列依旧只在 NativeProbe，不接入产品。

### 2026-11-01：ExplorerBrowser 产品接线架构评审

源码核对确认，产品每个窗格由 WPF `ExplorerBrowserHost : HwndHost` 创建 `IExplorerBrowser`，在导航完成后取得 Shell `IShellView`，并用 `IColumnManager` 保存/恢复 Windows 文件夹本身公开的列。这里没有受支持的接口可向当前 DefView 增加一列由应用按行提供、可参与 Shell 排序的值；隔离 `IShellView` 原型通过 `IShellFolder2` 暴露自定义 `PROPERTYKEY` 才能让 DefView 消费该列。

原型 provider 已能用普通文件系统作 backing folder，并转发一部分枚举、项目身份与 `IFileOperation` 请求；但这仍不能作为在现有 ExplorerBrowser 中替换单个回调的插件。要让产品打开物理路径时得到派生列，导航必须先解析到包装 namespace 的 PIDL，再由包装父/子 folder 维持相同身份链。当前产品会话、地址栏、历史记录、文件夹树、Shell 通知、跨窗格拖放/剪贴板、菜单命令路由和虚拟位置都以现有 Shell 路径及 ExplorerBrowser 生命周期为基础；接入会触及整个窗格的导航和对象身份边界。

已有隔离证据覆盖：Details 列值和数值排序、异步两 worker 批次、排序回填重排、同根去重、多根并发、队列背压重试、view 关闭取消，以及合成文件夹上的部分 Shell 文件操作与对象菜单接口。未覆盖：完整 OLE `DoDragDrop` 源端、可见原生菜单命令路由、完整剪切/撤销重做行为、真实多进程缓存/通知、普通真实文件夹上的完整 namespace 回环、ExplorerBrowser 产品宿主，以及用户可见刷新闪烁。隐藏 view 的焦点/选区测试也出现过一次不稳定，不能当作已关闭门槛。

结论：递归大小列的异步 provider 原型已足以证明“Shell 自定义文件夹可以提供可排序的派生列”，不再需要重复扩展同类隐藏 DefView 压力。它尚不足以证明可安全接入产品；应保持 NativeProbe 隔离，下一阶段把验证集中到最关键的迁移切片：让 ExplorerBrowser 宿主导航到一个包装的真实本地文件夹，验证返回路径/面包屑/父级导航、标准文件与背景菜单执行、单项重命名及 Shell 通知后列刷新，再确认退出与重开无残留注册。只有该切片在可见 UI 中通过，才评估四窗格和会话接线；若这些基础 Shell 行为需要由应用重做，则将迁移成本与收益重新评估，不把列原型并入当前产品。

本评审是基于当前源码与已有隔离探针的路线判断，不是 ExplorerBrowser 原型运行或用户可见验收。本轮没有改产品宿主、发布包或用户级 Shell 注册；该列仍未实现于 Explorer Dirs。

### 2026-11-01：真实文件系统 PIDL 的无窗口导航历史合同

NativeProbe 新增 `--shell-view-real-pidl-navigation-smoke`，使用临时真实目录经 `SHParseDisplayName` 生成 Shell PIDL，在不创建 `IShellView` 窗口的条件下验证绝对目录导航、相对子项合并、父级解析、Back/Forward 历史身份，以及文件 PIDL 被导航回调拒绝时历史保持不变。另经 `StreamViewHost` 的 COM `IShellBrowser::BrowseObject` vtable 回调验证绝对和相对真实目录 PIDL 的路由。

命令退出码 0，7 项断言通过，输出 `shell-view-created=False`。这将导航历史验证从纯合成 PIDL 推进到真实 Windows filesystem PIDL 和 COM 回调边界，但没有创建或导航真实 `IShellView`/ExplorerBrowser，也未验证 Shell 菜单、父级 UI、通知、拖放或列回填。ExplorerBrowser 包装目录的最小宿主切片仍是下一道迁移门槛；产品 ExplorerBrowser 与发布包均未更改。

### 2026-11-01：递归大小扫描期间 Shell 通知的失效竞态

复核递归快照缓存后发现：`SFVM_FSNOTIFY` 可在目录扫描进行时使路径及祖先快照失效，但旧 worker 完成后仍可能把通知前的递归大小重新写回缓存。新增无窗口合同先在旧逻辑上复现失败，再验证通知会标记匹配的 in-flight 路径；旧扫描提交返回 `S_FALSE` 且不进入缓存，释放 reservation 后新扫描可以成功写入新值。`RemoveSubtree` 也会失效对应子树中正在运行的扫描。

异步批次现在只把 `S_OK` 作为已发布结果：被通知丢弃的 `S_FALSE` 不会计为成功，也不会触发多余的 view 更新。原生 adapter `/W4 /WX` 构建、NativeProbe Release 构建、`--static-shell-column-folder-smoke` 与 `--synthetic-async-on-demand-recursive-size-column-smoke` 均通过；静态输出含 `recursive-size-inflight-invalidation-passed`，两命令均为无 Shell view 窗口合同。未启动 GUI。

边界：这是隔离 NativeProbe 的缓存与异步提交正确性修复，不证明真实 `IShellView` 通知时序或 ExplorerBrowser 产品接线。递归大小列仍是 P2 原型；ExplorerBrowser 包装真实目录的宿主切片仍是接入门槛。

### 2026-11-01：按需递归大小快照同时保存文件与文件夹数量

对照 Q-Dir 的“类型列显示递归文件/文件夹数量”选项时发现 NativeProbe 异步按需路径有缺口：枚举阶段刻意不扫描后代，`SyntheticRecursiveSizeSnapshotStore` 之前只缓存字节数，Type 列因此可能显示 `0 files, 0 folders`，而且读取 Type 列不会启动扫描。扩展同一快照保存递归 bytes/files/folders；启用扩展元数据时，Type 列首次读取会复用大小列的同一有界异步任务，未完成先返回空文本，扫描后返回递归计数。没有额外启动第二次扫描。

无窗口 NativeProbe 回归先因 pending count 返回 `0 files, 0 folders` 按预期失败；修复后逐项队列和按 view 批次均读到 `2 files, 1 folder`，仍为两 worker、重复读取命中缓存、关闭后取消剩余任务。Native adapter `/W4 /WX`、NativeProbe Release、两种异步队列 smoke 与静态 folder contract 通过。快照每项增加两个 `ULONGLONG` 计数字段；这里只证明原型快照行为。

边界：未接入 ExplorerBrowser 产品，不证明可见 Type 列绘制、Q-Dir 本地化/格式偏好或可见刷新效果；这仍是隔离 P2 原型。包装真实本地目录的 ExplorerBrowser 宿主切片仍未过迁移门槛。

### 2026-11-01：递归文件夹大小忽略排序合同

Q-Dir 官方 FAQ 提供“排序时忽略文件夹大小”的选项，并说明递归大小参与排序可能影响导航性能；官方说明没有规定忽略后文件夹与文件之间的精确排序次序。本原型据此采用可验证的局部假设：启用时，递归文件夹在 Size 比较中按 0 参与比较，文件仍使用真实字节数；列显示值和递归快照不受影响。

`--static-shell-column-folder-smoke` 先以红测确认此前忽略开关不起作用；实现后通过无窗口对照：夹具中的 10 B 文件夹默认排在 4 B 文件之后，启用忽略后该文件夹按 0 B 排到文件之前。两次比较都没有增加目录指标调用或遍历条目数，证明排序比较不会触发递归扫描。原生 adapter `/W4 /WX` 构建、NativeProbe Release 构建（0 警告/错误）、静态 folder contract 及按需异步计数 smoke 均通过；Shell view 窗口未创建。

边界：这是 NativeProbe 内部排序合同，不是产品设置，也未验证 Q-Dir 忽略模式的精确文件/文件夹相对次序或可见 Shell 排序体验；需后续与 Q-Dir 实际行为对照。ExplorerBrowser 产品、用户配置与 release 均未修改。

### 2026-11-01：ExplorerBrowser 直接浏览 adapter 对象的身份保留检查

为缩小包装真实目录迁移门槛，新增隔离 NativeProbe：把真实临时文件夹绑定为 filesystem `IShellFolder2`，由 `CreateStrictShellFolderAdapter` 包装后直接传给 `IExplorerBrowser.BrowseToObject`，再从 Browser 当前 `IShellView` 的 `IFolderView2.GetFolder` 读回 folder COM identity 与路径。`BrowseToObject` 返回 `S_OK`，当前路径仍是目标物理目录，但活动 view 的 folder identity 与传入 adapter 不同；因此 ExplorerBrowser 会沿 adapter 暴露的 PIDL 重新绑定为普通 filesystem folder，直接传对象不足以保留自定义列 provider。

NativeProbe Release 构建 0 警告/错误，隔离 Private Desktop 探针退出码 0，前台与 last-input tick 均未变化。首次尝试暴露父 STATIC 窗口同步销毁会在 Shell/COM 路径挂起；按既有 Private Desktop 生命周期处理改为让隔离进程退出回收父 HWND 后复跑通过。窗口只在单独 Desktop 创建，对用户不可见。

该结果只排除“直接 BrowseToObject 传 adapter”这一捷径，不验证注册 namespace PIDL、包装子 PIDL 的真实物理操作转发或递归大小列在 ExplorerBrowser 中的显示/排序；后续接线原型必须让 Shell 通过可解析的包装 namespace PIDL 重新进入 adapter。产品及 release 未修改。

### 2026-11-01：ExplorerBrowser 激活注册 namespace 并启用递归列

在上一轮身份检查之后，新增随机 CLSID 的临时 HKCU Shell-folder/桌面 NameSpace 注册，将合成自定义 folder 的绝对 namespace PIDL 交给 ExplorerBrowser `BrowseToIDList`。Browser 当前 view 的 `IPersistFolder2.GetClassID` 返回随机 CLSID，`GetCurFolder` 的 PIDL 与导航根逐字节相同；`IFolderView2.GetFolder` 提供 2 行夹具。provider 的 Size 列标题为 `Recursive Size`；用产品 `ShellColumnManager.Apply` 将标准 `PKEY_Size` 列设为可见后，`Capture` 读回 4 列并确认包含该 key。

Shell view 子控件为 `CtrlNotifySink|DirectUIHWND|ScrollBar`，没有经典 `SysListView32/SysHeader32`，所以本探针没有读到像素或原生 header caption；证据只证明 namespace 身份、行枚举和 Shell/IColumnManager 的可见列状态。NativeProbe Release 构建 0 警告/错误，Private Desktop watchdog exit 0，前台/输入守卫通过；随机 CLSID 和 Desktop NameSpace 两处临时注册确认已删除。`DllCanUnloadNow` 返回 `S_FALSE`，native DLL 留到隔离进程退出回收，不能据此宣称 COM 生命周期无泄漏。

边界：folder provider 仍是以真实临时目录作 backing 的合成 namespace；没有包装真实目录 PIDL，没有验证物理子项 PIDL 身份、菜单/操作/拖放/通知或用户可见列绘制。ExplorerBrowser 产品和 release 未修改。下一步应把 namespace child identity 与真实文件系统 PIDL/操作转发连接起来，并保持注册完全临时。

### 2026-11-01：注册 namespace 的菜单、重命名与层级导航切片

扩展同一隔离 ExplorerBrowser 探针：活动根 folder 可从自身 `IShellFolder2` 取得文件原生 `IContextMenu`，`QueryContextMenu` 构造 33 项菜单；调用该 folder 的 `SetNameOf` 后，重命名结果出现在真实临时 backing 目录。根的 `child` 相对 PIDL 与根 PIDL 合并后导航成功；活动子 view 的 `IPersistFolder2.GetClassID` 仍为临时 namespace CLSID，`GetCurFolder` 与合成绝对子 PIDL 字节一致，子目录枚举到 1 项；再导航回根后 CLSID/PIDL 恢复且行数为 3。

NativeProbe Release 构建 0 警告/错误；Private Desktop 探针通过，前台 HWND/last-input tick 未变化，临时注册与夹具清理完成。导航后读取前等待 250 ms 并泵送消息；第一次未等待的检查读到了旧根 view，按异步 Shell 导航时序修正后通过。

该切片证明注册 namespace 中可经现有 Shell folder 对象取得菜单、转发重命名，并往返一层子目录的 namespace 身份；菜单没有展示或执行命令，未验证背景菜单、文件操作菜单命令路由、递归列重新计算/通知、地址栏显示、拖放、可见绘制或产品宿主。底层仍用合成 child PIDL 映射临时文件系统 backing，而非包装真实 child PIDL；产品和 release 未变。下一步验证外部文件变更通知是否使 Browser 行数和递归列值重新收敛，再评估真实 child PIDL 与当前合成映射的取舍。

### 2026-11-01：注册 namespace 背景菜单转发

活动 Browser 的合成 folder 原先只接受 `IShellView` 的 `CreateViewObject` 请求；查询背景 `IContextMenu` 返回 `E_NOINTERFACE`，探针按预期失败并确认临时注册清理。现在仅将背景 `IID_IContextMenu` 请求转发给真实 backing filesystem folder，视图创建路径保持原样。复验背景 `CreateViewObject`/`QueryContextMenu` 返回成功并构造 21 项原生菜单；文件项菜单仍为 33 项，隔离 backing 文件重命名与一层子目录 PIDL 往返仍通过。原生 adapter `/W4 /WX` 与 NativeProbe Release 构建通过，Private Desktop 前台/输入守卫通过，临时注册及夹具已清理。

该结果证明的是背景菜单对象及命令项能创建，不是菜单实际显示/点击路由或命令执行；也未验证 `New`、刷新、排序、分组命令在 ExplorerBrowser 中的实际效果。产品宿主和 release 未修改。下一步验证外部 Shell 变更通知能否更新根列表及递归大小缓存。

### 2026-11-01：ExplorerBrowser 子目录通知刷新递归大小原型

Private Desktop 探针在已注册 namespace 的 `child` 目录下新增 7 B 文件，并针对该新文件发出物理文件系统 `SHCNE_CREATE`。Shell 将通知送到 `SFVM_FSNOTIFY`；adapter 转发父目录 `SHCNE_UPDATEDIR`，Shell 的枚举计数从 2 增至 3，provider 新解析的 `child` 行大小从 10 B 变为 17 B。起初原有视图行仍读到旧值。修正后，原有视图行的 PIDL 大小字段仍为 10，但行值通过 `GetDetailsOf` 已读到 17 B。

根因是重新枚举只把新递归大小放进新 PIDL；DefView 会保留同一文件夹原行 PIDL，因此行查询回退到旧值。现在，仅在启用隔离通知转发时，对同步递归枚举得到的目录快照也写入该 provider 共享的 identity-validated snapshot store。旧 PIDL 查询时按绝对 parsing name 与 file identity 命中同一快照，能拿到新值；异步按需模式仍由现有扫描任务写入此 store。Private Desktop 探针同时确认根目录外部新增行、子目录内新建文件后的递归大小读值，以及进入子目录/返回根；前台和输入守卫未变化，临时注册已清理。现有静态 folder contract 与 async on-demand smoke 也通过。

边界：这是 NativeProbe 注册 namespace 的逻辑视图读取证据，不是屏幕绘制或产品接线验收；snapshot 写入失败仍不应阻断目录枚举。ExplorerBrowser 产品及 release 未修改。

### 2026-11-01：注册 namespace 原生文件删除命令执行

在同一临时注册 namespace 探针中，先经包装文件项取得原生 `IContextMenu` 并确认其 canonical `delete` 动词，再调用 `IContextMenu::InvokeCommand`，传入该命令 offset 和 `CMIC_MASK_FLAG_NO_UI | CMIC_MASK_SHIFT_DOWN`。随机临时夹具中的 `context-delete.txt` 被 backing 文件系统实际删除；无确认窗口出现。随后已有根列表新增项通知、子目录创建通知与递归大小回填、目录进入/返回仍通过，前台/输入守卫和临时注册清理通过。

这证明文件项 context menu 的一个破坏性命令能穿过包装 namespace 到达真实 backing 文件系统；它不等同于菜单展示或鼠标命中验证，也未证明其他命令（打开、复制、粘贴、属性、新建、撤销/重做）及背景菜单命令路由。只操作每轮新建的临时文件，ExplorerBrowser 产品与 release 未改。

### 2026-11-01：包装 namespace IDataObject 的 Shell 复制与移动

在注册 namespace 的 ExplorerBrowser 探针中，对包装 provider 的 `GetUIObjectOf` 请求 `IDataObject`，分别为临时 `copy-source.txt` 与 `move-source.txt` 创建数据对象；将该对象直接传给 Windows `IFileOperation::CopyItems`/`MoveItems`，并调用 `PerformOperations` 与 `GetAnyOperationsAborted`。两项均报告未取消；目标文件内容逐字节/文本读回一致，复制源保留，移动源消失。`IFileOperation` 使用 STA 和 `FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI`，目标目录位于随机临时夹具，未使用剪贴板或显示 UI。

这证明包装 namespace 的文件项 `IDataObject` 可供标准 Shell 文件操作接口完成复制与移动；不验证 ExplorerBrowser 菜单中的复制/粘贴命令、拖放、冲突处理、多选或真实文件夹 PIDL/对象身份。Private Desktop 前台/输入守卫与临时注册清理通过，产品及 release 未修改。

### 2026-11-01：包装 namespace 原生文件夹拖放目标

基线探针确认合成 namespace 的 `CreateViewObject(IID_IDropTarget)` 返回 `E_NOINTERFACE`，同一 backing filesystem folder 则返回 `S_OK`。因此文件项可提供 `IDataObject` 并不意味着目录已具备拖放目标。`SyntheticSortFolder::CreateViewObject` 现将 `IID_IDropTarget` 与背景 `IContextMenu` 一样转发给 backing folder，`IShellView` 仍由包装层自己创建。

更新后的 Private Desktop 探针通过包装根文件夹绑定子目录，取得其原生 `IDropTarget`；将包装根中的 `copy-source.txt` 数据对象依次交给 `DragEnter`、`Drop`，复制到子目录后逐字节比较源/目标，确认源保留、目标创建。另以 MOVE effect 直接调用相同回调时，`DragEnter` 接受 MOVE，但 `Drop` 返回无 effect；这个直接回调省略了 `DoDragDrop` 的 source-side 完成过程，因此仅记录现象，不据此判断用户实际拖放时的移动行为。Release NativeProbe 构建和原生 adapter `/W4 /WX` 构建通过；探针退出码 0，临时注册/文件夹清理、前台与输入守卫通过。

边界：这是隔离临时 namespace 中对 Shell COM 拖放接口的直接调用，不是 `DoDragDrop` 鼠标交互；没有验证跨窗格/跨进程 OLE 拖放、完整移动效果、拖动反馈、取消/拒绝效果、Shell 通知及产品接线。ExplorerBrowser 产品及 release 未修改。后续应在隔离架构评审中验证真实 OLE 循环与通知，再评估接入递归列原型。

### 2026-11-01：注册 namespace 接收外部进程 Shell 变更通知

将原先由宿主进程直接写文件并调用 `SHChangeNotify` 的检查改为独立 NativeProbe 子进程：子进程创建测试文件，再以绝对 PIDL 分别发送 `SHCNE_CREATE` 和父目录 `SHCNE_UPDATEDIR`；父进程中的私有 Desktop ExplorerBrowser 保持打开并等待更新。根目录新增文件最终出现在视图中，行数为 4。对子目录新增文件，包装通知转发队列增加 1，provider 枚举次数从 2 到 3，已存在的子目录行 `GetDetailsOf` 从 10 B 更新为 17 B；行 PIDL 中保存的旧值仍为 10 B，验证了行值读取依赖共享快照而不是重建旧 PIDL。

NativeProbe Release 构建通过，子进程均退出码 0；Private Desktop 探针退出码 0，临时 namespace 与夹具清理、前台及输入守卫通过。此验证覆盖独立进程发起的 Shell 广播，但没有覆盖真实 Explorer/第三方程序的通知种类、通知风暴、删除/重命名/移动事件、多窗格或产品接线，也不是用户可见刷新验收。

### 2026-11-01：共享递归大小缓存的空闲卸载生命周期

新增无窗口原生合同，先在共享 `SyntheticRecursiveSizeSnapshotStore` 写入一条带 file identity 的快照，确认缓存计数为 1；随后调用 `DllCanUnloadNow`，验证模块空闲时释放 DLL 持有的共享 store；再次获取 store 后确认从空缓存开始，再次空闲检查成功。这个检查覆盖 store 的显式 DLL 卸载清理分支，不要求真正从进程地址空间卸载 DLL，因 NativeProbe 仍持有本地模块句柄。该命令先因导出缺失按预期失败，添加合同后连续三轮通过。

原生 adapter `/W4 /WX`、NativeProbe Release（0 警告/错误）、`--static-shell-column-folder-smoke` 与 `--synthetic-async-on-demand-recursive-size-column-smoke` 均通过。它证明快照不会因 DLL 的共享引用跨过成功的 `DllCanUnloadNow` 空闲门槛；不证明仍被 Shell 持有的 provider/cache 会提前释放，也不取代注册 namespace Private Desktop 运行期间的 store 容量或多窗格压力测量。产品与 release 未改。

### 2026-11-01：四视图初始枚举与焦点建立时序

在原 500 目录/四个不同根压力场景中，偶发第四 view 选区/焦点丢失。新增 transition trace 后复现：丢失发生在扫描排空阶段，已完成 431 次扫描、Shell 更新通知数仍为 0，但 `EnumObjects` 总增量已多 1 次。由此定位到探针在 `WaitForNames` 首次读齐行后立即设置焦点；DefView 仍可能稍后完成一次初始枚举并清掉该状态，问题与递归大小更新通知无关（这是基于事件顺序的根因判断）。

探针现在在所有 view 初始行数齐备后，等待 `EnumObjects` 计数连续安静 1 秒，再设置独立选区/焦点；同时保留仅诊断时输出状态转移的记录。调整后，200 目录/四视图、500 目录/四视图最小内容夹具，以及 500 目录/四视图原始夹具均通过。原始夹具结果：2,004 次唯一扫描、队列拒绝 0、重复扫描 0、通知 4、聚合枚举增量 4、四份 502/502 值正确，四份选区与焦点全保留；前台 HWND 与最后输入不变，watchdog exit 0。日志：`src/.state/native-probe-watchdog/20261101-160829-7a1f4dd3.stdout.log`（焦点丢失转移的定位样本）、`src/.state/native-probe-watchdog/20261101-160722-67ff309f.stdout.log`（200 目录回归）、`src/.state/native-probe-watchdog/20261101-161025-b53e63ed.stdout.log`（500 目录最小夹具）、`src/.state/native-probe-watchdog/20261101-161107-788ad7f9.stdout.log`（500 目录原始夹具通过）。

这是 NativeProbe 隐藏 DefView 的初始化时序修正与压力合同，不是产品 ExplorerBrowser 接线，也不证明可见应用在切换窗格时的焦点行为。先前一次大夹具诊断运行在 view 建立/清理阶段超时，未进入焦点断言，视为无结论；之后原始夹具运行正常完成。

### 2026-11-01：四视图高负载关闭与扫描接管

用 500 个目录/视图的最小内容夹具复核批处理取消。四个不同根 view 在工作闸门关闭时各排入一个批次，退休全部 view 后再释放 worker：4 个批次均结束，扫描 0、Shell 更新通知 0。四个同根 view 的单 view 退休场景则在释放后由存活 view 接管同一批请求：只扫描 501 个唯一目录（无重复），4 批结束，通知 1 次，存活 view 值完整且聚合枚举增量 3。

两次 Private Desktop watchdog 均 exit 0，前台与最后输入守卫不变，测试夹具及隔离窗口清理通过。结果只覆盖 NativeProbe 隐藏 DefView、按 view 批次与这两种受控关闭时序；不代表产品 ExplorerBrowser、逐项模式、慢磁盘/网络取消延迟或可见窗口交互。日志：`src/.state/native-probe-watchdog/20261101-161401-54f597cb.stdout.log`（四个不同根全部关闭）、`src/.state/native-probe-watchdog/20261101-161429-cc2804f3.stdout.log`（单 view 退休、同根接管）。

最初的源码复核发现：批次只在进入目录指标扫描前、以及递归扫描返回后检查 view 取消标记，递归遍历本身没有取消检查。因此当时关闭 view 会等当前目录扫描完成，再抑制结果发布。后续条目边界取消检查的实现与验证见下节。

在途退休探针使用四个独立根、每根 500 个目录、750 ms 延迟；连续两轮都等到两项扫描开始后销毁四个 view。两项扫描均实际完成，但成功发布数为 0、Shell 通知为 0、取消后没有启动额外扫描，4 个批次全部结束。Private Desktop 前台/输入守卫、夹具及隔离清理通过。日志：`src/.state/native-probe-watchdog/20261101-162044-a01ade83.stdout.log`、`src/.state/native-probe-watchdog/20261101-162117-8b8c7753.stdout.log` 与最终加强断言后的 `src/.state/native-probe-watchdog/20261101-162228-d9ef6759.stdout.log`。NativeProbe Release 0 警告/错误；无窗口异步按需列、Shell 文件夹合同及缓存失效 smoke 通过。这证明取消会阻止在途结果发布并停止后续扫描，但不证明递归中的单目录任务可中断；若实际关闭延迟不可接受，再评估递归遍历中的取消点。

### 2026-11-01：递归大小扫描在枚举途中响应 view 关闭

把 view 取消标记传入递归目录指标扫描器，并在打开目录、处理每个条目前及 NativeProbe 延迟注入后检查。父目录向下递归时沿用同一取消标记；异步逐项和按 view 批处理 worker 都传入各自 view 的标记，普通同步合同使用默认空标记。为确保不是在扫描开始前取消，Private Desktop 探针让四个独立根各有 20 个大目录，每个含 200 个文件，以 20 ms/条目延迟等待至少 32 条目后退休 view。

RED 基线仅在递归函数内部无取消检查时失败：关闭前累计 32 项，关闭后升至 400 项。加入检查后连续三轮均维持 32 项不增，2 个已开始扫描没有发布值或发 Shell 通知，4 个 view 批次全部结束；Private Desktop 前台、最后输入和隔离清理守卫通过。证据日志：RED `src/.state/native-probe-watchdog/20261101-162832-390999ae.stdout.log`；GREEN `src/.state/native-probe-watchdog/20261101-163006-e0757d4b.stdout.log`、`src/.state/native-probe-watchdog/20261101-163059-deb89a14.stdout.log`、`src/.state/native-probe-watchdog/20261101-163141-d27bc352.stdout.log`。NativeProbe Release 与原生 adapter `/W4 /WX` 构建通过；无窗口异步按需列和 Shell 文件夹/缓存失效 smoke 通过。

该改进仅在隔离 NativeProbe 适配器中验证，尚未接入产品 ExplorerBrowser；注入延迟模拟慢目录条目处理，不是网络盘真实时延测量，也不代表用户可见列表刷新体验。原生文件系统枚举仍只能在条目边界观察取消；单次 `FindFirstFileW`/`FindNextFileW` 调用和当前条目注入延迟不会被强制中断。

### 2026-11-01：按需递归大小列的队列容量超限对照

为验证逐项任务在有界队列被真实压满后的恢复行为，临时将 NativeProbe 的四个不同根视图各扩为 520 个目录，并使用每目录一个文件的最小夹具；递归大小缓存设为 4,096，避免把缓存淘汰混入队列结果。诊断执行暂时放宽“初始拒绝立即失败”的断言，以便观察 worker 排空后的收敛。逐项模式在 2,048 项队列中于释放 worker 前拒绝 34 项。排空后探针重复读取列值仍只扫描 2,050/2,084 个目录；一份视图为 488/522 行，留下 34 行空值，另三份视图完整。隔离桌面前台、最后输入及资源清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-163719-cb55351b.stdout.log`、`20261101-163719-cb55351b.stderr.log`。

相同夹具切换为按 view 合并批次后，2,084 个目录全部唯一扫描并回填，队列拒绝 0、重复扫描 0、Shell 更新通知 4、四视图 `EnumObjects` 增量各 1，四份选择/焦点均保留；缓存快照 2,084、估算载荷 687,680 B，进程 Private Bytes 为 95,899,648 B（没有同进程基线差值，不能将该进程值归因于缓存）。日志：`src/.state/native-probe-watchdog/20261101-163837-a5b3382e.stdout.log`、`20261101-163837-a5b3382e.stderr.log`。两次 Private Desktop watchdog 均确认前台 HWND 与 last-input tick 不变，隔离清理通过。

结论：逐项模式在超过 2,048 个排队任务时会丢失被拒绝的列请求；Shell 后续重读并未保证所有视图重新请求这些行。view-batch 模式以每个 view 一个外层协调任务及至多一个子批次完成同一压力夹具，没有触及队列上限，因此目前更适合作为递归列原型的默认调度方向。逐项失败负载超出既有 500 目录上限，不代表常规 4×500 负载会拒绝；但并发其它任务或更大目录仍可能达到此边界。该结果仅在 NativeProbe 合成 provider/隐藏 DefView 中成立，未接入产品 ExplorerBrowser，也没有可见 UI、真实磁盘/网络或慢 I/O 证据。若保留逐项调度，仍须增加明确的有界拒绝重试机制；不能把重读列值当作已验证的恢复路径。

### 2026-11-01：高行数批次排队期间关闭全部 view

在四个不同根、每根 520 个目录（每目录一个文件）的隐藏 Shell view 中启用 view-batch，并让队列 worker 在扫描闸门处等待。四个 view 均排入批次后先销毁全部 view，再放行 worker；最终 4 个批次均结束，递归扫描数为 0、Shell 更新通知数为 0。Private Desktop watchdog exit 0，前台 HWND 与 last-input tick 不变，隔离桌面及临时注册/夹具清理通过。日志：`src/.state/native-probe-watchdog/20261101-164218-85ad1cc0.stdout.log`、`20261101-164218-85ad1cc0.stderr.log`。

此结果将“已排队批次取消”从此前每 view 500 项扩展到 520 项，并确认取消发生在目录指标扫描开始之前；不覆盖已进入单次文件系统调用时的中断能力，也不是产品 ExplorerBrowser 或可见 UI 验收。

### 2026-11-01：隐藏 Shell 递归列原型默认使用 view-batch

基于队列超限对照，将 NativeProbe 隐藏 Shell view 的异步按需扫描默认策略改为按 view 合并批次；设置 `EXPLORER_DIRS_NATIVEPROBE_ASYNC_BATCH_BY_VIEW=0` 可显式回到逐项模式，`=1` 仍可显式选批次。强制队列饱和实验要求显式选择逐项模式，避免误把批次下没有队列溢出当成逐项容量合同。

默认环境下运行四个不同根、每根 500 个目录、缓存容量 2,048 的隐藏 Shell view：2,004 个目录唯一扫描完成，队列拒绝 0、重复扫描 0、每 view 一次通知，四份列值均为 502/502，选择/焦点全部保留。Private Desktop watchdog exit 0，前台/last-input 与隔离清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-164532-858ca37e.stdout.log`、`20261101-164532-858ca37e.stderr.log`。

逐项 A/B 明确设置 `EXPLORER_DIRS_NATIVEPROBE_ASYNC_BATCH_BY_VIEW=0`，四根各 520 个目录、缓存 4,096 时，在释放 worker 前拒绝 34 项并按预期停在容量门槛。队列排空后的缺值影响已由前节的延续诊断确认。日志：`src/.state/native-probe-watchdog/20261101-164612-22d0d270.stdout.log`、`20261101-164612-22d0d270.stderr.log`。两种结果均为隐藏合成 `IShellView` 原型证据；产品 ExplorerBrowser 与发布包未修改。

### 2026-11-01：异步批次期间的真实 Shell 目录通知验证边界

尝试在四个隐藏 DefView 的批次 worker 已预留扫描后，对正在变化的嵌套文件发送 `SHChangeNotify`，并要求证明旧扫描被丢弃、同一目录随后重扫。文件项 `SHCNE_UPDATEITEM` 到达 `IShellView` 时记录为 `SFVM_FSNOTIFY` 的 `SHCNE_UPDATEDIR`，但目前探针无法将回调携带的 PIDL 与确切的在途递归缓存键配对：最终值虽读到 38 B，扫描次数仍等于未变更基线，因此不能证明失效竞态已覆盖。

直接对合成子目录 PIDL 发 `SHCNE_UPDATEDIR` 会改变 DefView 行集合；将 `UPDATEDIR` 一概扩大为子树缓存失效则复现反馈风暴（8 个目录、四视图场景扫描 224 次，基线 36 次，23 次通知，值未收敛）。该扩大规则已撤销，现有缓存失效实现未因此改变。下一步应在 NativeProbe 中增加仅测试用的精确缓存键失效入口，锁定一个已知在途扫描并验证旧提交被拒绝、释放 reservation 后重试；再单独验证真实 Shell 通知 PIDL 到缓存键的映射，避免把 DefView 行集合变化误判为缓存行为。

以上组合实验均未通过成功合同，不能算作产品功能或异步通知竞态的完整验证。产品 ExplorerBrowser、release 与 NativeProbe 默认 view-batch 策略均未因该实验更改。

### 2026-11-01：精确缓存键失效与在途结果拒绝验证

NativeProbe 新增只供隔离试验使用的精确 reservation 检查/失效入口：入口在共享缓存锁内确认目标键仍有扫描 reservation，并原子设置失效标记；不对祖先、后代或整棵目录子树做清除。Private Desktop 四个不同根视图中，闸门释放后等待 `folder-000` 扫描开始，修改夹具文件使目标目录从 1 B 变为 38 B，再失效该确切键。旧 worker 的提交被缓存拒绝，基线 20 个扫描仅 19 个成功发布；其余批次完成后显式 `RefreshView`，Shell 重新请求并扫描目标，最终 38 B 进入共享缓存，成功发布数回到 20。最终总扫描 21（比基线多一次重试），通知 5，峰值 worker 2，四视图列值齐备，选区/焦点在探针重扫后重新设置时均通过。Private Desktop 前台/输入守卫与临时 namespace/夹具清理通过。

RED 阶段因预期缺少原生 probe 导出失败；接入后曾发现 `RefreshView` 会暂时清空 ListView，探针现等待行数恢复才检查。刷新会丢掉首视图原选择，因此最终检查是在重扫后重新设置选择/焦点；此结果不证明刷新本身保留 selection。日志：`src/.state/native-probe-watchdog/20261101-171503-08c9db85.stdout.log`。原生 adapter `/W4 /WX` 和 NativeProbe Release（0 警告/错误）构建通过，Private Desktop watchdog exit 0。

边界：此精确入口由探针直接指定缓存键，不证明 Windows Shell 通知可从其 PIDL/事件可靠解析成同一个键；也不覆盖真实文件系统 provider、ExplorerBrowser 产品接线、可见刷新体验或菜单/文件操作。递归大小列仍仅为 NativeProbe 原型，不进入产品或 release。下一步根据通知的变化类型和 PIDL 精确追踪路径映射；若通知仍无法给出稳定 key，则评估以目录身份与 generation token 代替路径键的失效合同，再继续高负载 view-batch 回归。

### 2026-11-01：真实 Shell 通知命中在途递归扫描

NativeProbe 的 `SFVM_FSNOTIFY` 现在可报告通知 PIDL 解析名称、文件系统路径，以及本次通知实际标记为过期的在途 reservation 数。测试夹具由独立子进程写入 `Pane1\folder-000\item-00.bin` 并发出 Shell 文件变更通知；Shell 将文件项事件交给视图时表现为 `SHCNE_UPDATEDIR`，回调 PIDL 解析为该子目录的绝对物理路径，和递归大小缓存键完全一致。原子失效计数确认该通知命中了 1 个正在扫描的 reservation；旧扫描结果未发布，批次结束后显式刷新重扫，目标值读回 38 B。

四个不同根的隐藏 Shell view 场景完成：扫描 21 次（基线 20 次，多一次目标重试）、拒绝 0、峰值 worker 2、四视图列值齐备，隔离刷新后选择与焦点均重新设置通过。无窗口 folder contract、异步队列 smoke、原生 adapter `/W4 /WX` 和 NativeProbe Release 构建通过；Private Desktop 前台/输入守卫与临时资源清理通过。日志：`src/.state/native-probe-watchdog/shell-notification-invalidation-final.log`。此前精确键直调场景仍保留，作为通知路径的对照。

边界：这证明当前合成 namespace 的文件系统路径缓存键可由一次真实跨进程 Shell 通知精确命中；不证明所有通知类型、网络/虚拟 Shell 项、通知风暴、真实产品 ExplorerBrowser 接线或可见刷新期间的选区行为。探针需要在验证后显式 `RefreshView` 才触发重扫，并会重新设置选区/焦点；因此不是无闪刷新或产品体验证据。递归大小列仍仅为 NativeProbe 原型，不进入产品或 release。后续应继续跑四视图高行数 view-batch 回归，并单独评估目录身份变化、移动/重命名及多种 Shell 通知与缓存键的覆盖关系。

### 2026-11-01：文件夹重命名通知双 PIDL 失效合同

扩展无窗口 `--static-shell-column-folder-smoke`：构造真实旧/新文件夹绝对 PIDL 和各自的在途扫描 reservation，直接调用隔离 folder provider 的 `IShellFolderViewCB::MessageSFVCB(SFVM_FSNOTIFY, ..., SHCNE_RENAMEFOLDER)`。回调分别解析两个 PIDL 路径，并各将一个 reservation 标为失效；对旧、新路径提交的扫描值均返回 `S_FALSE`。日志确认两次 `invalidated-reservations=1`，新增合同 `recursive-size-rename-pair-inflight-invalidation-passed`。原生 adapter `/W4 /WX`、NativeProbe Release build 与无窗口完整 folder smoke 均通过，fixture 目录已清理。

边界：这是对真实 Shell folder callback 处理双 PIDL 与缓存 reservation 的无窗口合同，不是外部进程实际移动目录后的通知时序，不验证 view 枚举/选区/排序、网络或虚拟 namespace，也未接产品 ExplorerBrowser。现有代码通过该路径测试，本轮没有改变通知失效算法或默认策略。下一步可在明确桌面空闲授权后用 Private Desktop 验证外部进程真实目录移动/重命名通知；在此之前继续保持 NativeProbe 隔离。

### 2026-11-01：文件重命名通知双 PIDL 失效合同

无窗口 `--static-shell-column-folder-smoke` 再覆盖 `SHCNE_RENAMEITEM`：为旧文件所在根目录和新文件所在子目录分别建立在途摘要扫描 reservation，构造两个真实文件系统绝对 PIDL 并直接调用隔离 folder provider 的 `IShellFolderViewCB::MessageSFVCB`。回调解析旧、新路径并分别使对应父目录扫描过期；两个旧提交都返回 `S_FALSE`。日志出现 `recursive-size-file-rename-pair-inflight-invalidation-passed`。原生 adapter `/W4 /WX`、NativeProbe Release build 和完整无窗口 folder contract 通过，输出 `window-created=False`。

边界：这是合成 callback 中的文件重命名双 PIDL/父目录缓存合同；没有执行实际文件移动、广播 `SHChangeNotify` 或创建 Shell view，不能代表外部通知时序、视图重枚举/选区/排序、产品 ExplorerBrowser 或可见体验。递归大小列仍未接产品。

### 2026-11-01：注册 namespace 的背景 New Folder 命令调用对照

隔离 ExplorerBrowser 注册 namespace 探针已能取得背景 `IContextMenu` 并成功查询 21 个菜单项，但 QueryContextMenu 成功不代表命令可执行。新增隐藏 Private Desktop 对照，分别对真实 filesystem `IShellFolder` 和包装 namespace folder 查询规范 `newfolder` 动词并调用 `IContextMenu::InvokeCommand`；两条路径均返回 `E_FAIL`，没有创建夹具目录。相同探针中的包装文件项 Delete 动词仍成功执行，且重命名、复制、通知、导航等既有断言通过。

这次负向结果不能归因于 wrapper：未显示/路由菜单的直接 `InvokeCommand` 对照同样失败，说明当前无交互调用不足以验证背景新建命令的完整宿主路由。后续需经实际 Shell view 菜单消息路由做独立验证；在此之前不将背景菜单命令执行计为已通过迁移门槛。Private Desktop 前台/输入守卫和临时注册清理通过；产品与 release 未修改。

### 2026-11-01：使用产品菜单适配器执行 namespace 背景 New Folder

回看发现，上述直接 `IContextMenu::InvokeCommand` 对照没有走项目生产路径：`NativeShellCommandMenu` 会初始化 “New” 子菜单（`WM_INITMENUPOPUP`）后才判定/执行 `NewFolder` 动词。现改为在注册 namespace 的 `IShellView` 上通过 `GetItemObject(SVGIO_BACKGROUND)` 取得菜单，并调用产品 `ShellFileCommands.Invoke`/`NativeShellCommandMenu`。命令在真实 backing 目录中创建了一个“新建文件夹”，活动 Shell view 行数从 6 增至 7；探针检测并取消可选的行内重命名编辑，最终由夹具整体清理。执行前先等待 Shell 行数和磁盘项目数一致，避免将前一轮文件操作尚未收敛的刷新误当作新建命令结果。

NativeProbe Release 构建 0 警告/错误；`--explorerbrowser-registered-namespace-private-desktop` 退出码 0，原有菜单查询、文件项删除/重命名、复制/移动、通知和层级导航合同通过。Private Desktop 前台/输入守卫、临时 CLSID/NameSpace 清理通过。它证明生产命令适配器能通过隔离 ExplorerBrowser namespace 的 Shell view 创建目录；不等同于可见菜单弹出/点击、快捷键、提示与进度 UI 或产品窗格接线验收。此前 no-UI 直接调用结果仅是调用方式负向对照，不作为 wrapper 命令能力失败证据。

### 2026-11-01：包装 PIDL DefView 列消费复验

在当前 NativeProbe Release 二进制上重跑 `--static-shell-column-wrapped-adapter-private-desktop`，确认隔离 `IShellView` 使用包装 adapter（`view-uses-adapter=1`），但列表中自定义列各行仍为空；读完列表后计数为 `GetDetailsOf(custom header/item)=1/0`、`GetDetailsEx=0`、`CompareIDs=0`。自定义列和 `System.Document.ByteCount` 的升降序均没有按 2/10/100 数值重排，四项排序合同全部失败，进程退出码 1。标准列标题仍显示，前台 HWND、last-input 与 Private Desktop 清理守卫通过。输出日志在临时目录 `explorer-dirs-wrapped-column.stdout.log`，SHA-256 `DD17FB313BC9607ACA9645192929C3019DB5CD48D0BABE57977D5C545271CBAA`。

这是对 2026-10-28 包装 PIDL 结果在当前源码上的复验，不是新的成功路径：单纯改变项目 PIDL 身份仍不足以让 DefView 请求逐项属性或比较键。当前默认 DefView adapter 方案不满足“逐行显示且可排序”的门槛；不应重复相同包装方式或接入产品。若继续追逐此功能，下一条有意义的原型应改为可控制行值读取/排序的数据视图，并先用隔离视图比较其 Shell 菜单、文件操作、焦点、拖放、通知和刷新成本；在这些行为门槛有结果前，递归列继续留在 NativeProbe，产品保留 F9/状态栏扫描。

### 2026-11-01：将真实 Shell 枚举行接入可控列表导航原型

`--common-listview-navigation-contract` 之前只分别枚举每个测试目录并验证纯位置历史。本轮将这些 Shell 行实际填入隐藏原生 `SysListView32`，用行 `LPARAM` 指向保留的 Shell row identity：从根列表选中 `child` 后打开其目录、读回子列表、后退/前进，再从根列表选中 `branch` 并确认前进历史被清除。每个导航位置的项目名均与真实 `IShellFolder` 枚举一致，Shell parent PIDL 与预期根目录匹配，探针结束时前台未变化。运行初版曾暴露探针自身重复释放重映射行的 PIDL；修正所有权登记后，进程正常退出。

NativeProbe Release 构建（0 警告/错误）、`--common-listview-navigation-contract`、`--common-listview-data-contract` 和 `--common-listview-relative-size-contract` 均退出码 0。现有数据合同同时复验了递归列排序、系统图标、多选 Shell data object、Shell item/background menu 对象查询及 Copy/Move/Rename/Delete。所有 ListView 都未显示，未建立实际 `IShellView`，也未验证鼠标/键盘输入、菜单显示与命令路由或可见重绘。下一步对照该自控列表与默认 `IShellView` 的 view/background 命令来源，验证 View/Sort/Group/Refresh/New 能否在列表身份和行顺序上形成一致结果；这仍是 NativeProbe 架构原型，不接入产品 ExplorerBrowser 或 release。

### 2026-11-01：核对默认 Shell view 菜单命令作为自控列表的同步来源

窄化复跑 `--ishellview-menu-command-private-desktop`，避免跨虚拟位置的长序列影响普通文件夹命令判断。通过 `IShellView::GetItemObject(SVGIO_BACKGROUND)` 取得 view-owned `IContextMenu` 后，使用菜单 canonical verb 依次执行 `viewlogicaldetails`、`refresh`、`sortdescending`、`sortascending`、`groupdescending`、`groupascending`；Shell 的当前模式、排序方向、分组方向及文件顺序均按预期读回。Private Desktop watchdog exit 0，前台 HWND 与 last-input tick 未变，隔离 fixture 清理通过。

该命令仍只控制它所属的默认 `IShellView`。刷新时 Shell 行模型从 3 行短暂归零再恢复；降序排序在本轮样本中使选择/焦点短暂丢失，需使用生产 `ShellViewSelectionSnapshot` 最终恢复。因此不能把 view-owned 菜单对象直接挂给另一个 `SysListView32` 并假设它会同步；后续桥接原型必须在每项命令后显式把 view mode、排序/分组状态和稳定 Shell item 身份投影到自控列表，并检查刷新空窗与焦点恢复成本。此前全量 host-filter 虚拟位置序列已在 ZIP 阶段超过 watchdog 时限，退出码 1；该次不是全量通过证据，也没有残留探针进程。

### 2026-11-01：受控 ListView 的 Shell 状态投影切片

在同一个隔离 Private Desktop 夹具旁新增独立隐藏 `SysListView32`：初始从 Shell 行模型投影 3 个条目；执行原生 Details 命令后将其样式切为 Report 且行不变；执行刷新时，Shell 视图自身在 0/50/100 ms 采样为空、约 150 ms 恢复 3 行，而并行 ListView 在等待期间始终保有旧 3 行；待 Shell 行模型稳定后再投影新顺序。Shell 升降序菜单命令后，独立列表批量重建为与 Shell 完全相同的行顺序。探针 exit 0，NativeProbe Release 构建 0 警告/错误；Private Desktop 前台 HWND、last-input tick 与隔离清理守卫通过。

这证明受控列表可以将 Shell 原生命令作为状态来源，并把旧行保留到刷新行模型就绪后再同步；不证明画面无闪、稳定 PIDL 的跨刷新更新、图标/列/选择完整投影或分组视觉行为。分组命令仍仅验证 Shell `IFolderView2` 状态读回，没有投影到 SysListView 分组。该夹具只是 3 行隐藏样本，产品 ExplorerBrowser、可见 UI、真实大目录及交互验收均未涉及。下一步应验证稳定 Shell item 身份和元数据如何在原生列表中增量更新，再单独完成列表分组与多列投影门槛；尚不足以批准架构迁移或接入产品。

### 2026-11-01：Shell PIDL 稳定身份、系统图标与原生分组桥接

并行受控 SysListView32 的行现在使用 Shell ILIsEqual PIDL 身份映射稳定行 ID；刷新、升降序及分组命令后，3 个 fixture 项仍映射到原有 ID。通过 ShellListViewEnumerator 绑定共享系统小图标列表及每行图标索引。执行 Shell 按类型分组后，将当前 view 顺序投影到启用 Common Controls 6 的原生 ListView group rows，读回分组视图开启且组数为 2。此验证须运行 NativeProbe apphost EXE，因为 dotnet <dll> 使用 dotnet 宿主的 activation manifest，仍加载不支持该 ListView 分组 API 的控件版本。

本夹具上 SHGetFileInfo 没提供可用类型标题，因此探针标题采用扩展名/文件夹回退（TXT file、File folder）；没有证明 Explorer 本地化 System.ItemTypeText 标题映射、视觉布局、折叠/展开、跨分组排序或用户交互。所有 view 都隐藏；未接产品 ExplorerBrowser 或 release。下一步应取得 Shell 的正式类型文本并扩充多列映射，再对行增删/重命名通知做稳定 PIDL reconciliation。

### 2026-11-01：原生列表增加第二列并保持 PIDL 行映射

隔离 peer `SysListView32` 从仅有 Name 扩展为 Name + Type 两列。Type 子项取自同一 Shell 行的类型元数据；在 Details、刷新、升降序、按类型分组投影之后，逐行读回 Type 值，并与稳定 PIDL 行身份对应的元数据逐项比较。`--ishellview-menu-command-private-desktop` 通过，输出 `peer-type-column=True`；NativeProbe Release 构建 0 警告/错误，Private Desktop 前台、last-input 与清理守卫通过。

该切片只证明原生 ListView 的附加文本列能与当前 Shell 行顺序、PIDL 身份、图标和分组共同工作。Type 仍使用当前 `SHGetFileInfo` 值和已有扩展名/文件夹回退；不是 Shell 正式本地化类型文本，也没有直接复制 Explorer 的日期、大小等全部 Details 列。未创建可见窗口、未测用户交互或重绘，也未接产品/release。下一步应从 Shell 的列接口投影真实标准 Details 列，再验证行增删/重命名通知下的子项和 ID 对齐。

### 2026-11-01：通过 IShellFolder2 投影标准详情字段

peer 列表现在从真实 filesystem `IShellFolder2::GetDetailsOf` 读取名称、大小、项目类型和修改日期四列的标题与每行值，并逐单元格读回比对；列值随 Shell 行顺序重投影，PIDL 行 ID、系统图标和分组合同继续通过。Private Desktop `--ishellview-menu-command-private-desktop` 退出码 0，输出 `peer-shell-details-columns=[名称|大小|项目类型|修改日期]`；NativeProbe Release 构建 0 警告/错误，前台、last-input 和隔离清理守卫通过。

列标题来自 Shell 文件夹的前四个 `GetDetailsOf` 索引，不是当前 DefView 用户列顺序/宽度/隐藏状态的完整镜像；这里只读文本，不验证点击列头后由 peer 自身排序。大小仍是 Shell 默认直接属性，尚未投影逐行递归大小。样本仅为三个本地 fixture 条目，view 隐藏，未测可见 UI、增删通知、拖放或产品 ExplorerBrowser。后续继续验证 PIDL 行增删/重命名 reconciliation，再单独接入按需递归值与数值排序。

### 2026-11-01：Shell 创建与删除通知同步 peer 详情列

在同一隔离 Private Desktop `IShellView` 菜单合同中，对真实 filesystem fixture 新建一个文本文件并广播 `SHCNE_CREATE`，等待 Shell 行数稳定后，用 Shell PIDL 顺序重投影 peer 四列；原三行 ID 全部保留，新行获得唯一 ID。随后删除该文件并广播 `SHCNE_DELETE`，再次投影后新行 ID 退役、原行身份和详情值仍对齐。NativeProbe Release 构建 0 警告/错误，`--ishellview-menu-command-private-desktop` exit 0，前台/last-input 与隔离清理守卫通过。

该验证覆盖创建/删除通知及标准详情列的重投影；不覆盖重命名 PIDL identity remap、跨目录移动、通知合并/风暴、通知延迟/丢失、选择焦点恢复、多窗格或可见更新。创建与删除由探针显式调用 `SHChangeNotify`，不是由其它进程自然触发；仍属隐藏 NativeProbe，不代表产品接线或视觉体验。下一步验证重命名通知下旧/新 PIDL 的同一行身份映射与详情字段更新，再推进按需递归值排序。

### 2026-11-01：Shell 重命名通知保留 peer 行身份

在同一隔离 filesystem `IShellView` fixture 中，将 `keep.txt` 改名为 `keep-renamed.txt`，通过专用 `ChangeWindow` 收取 `SHCNE_RENAMEITEM` 并解析通知携带的旧/新绝对 Shell parsing name。peer reconcile 用“新 parsing name → 旧 parsing name”映射旧 row identity；重投影后原 row ID 仍为 3，名称详情已更新为新文件名，其余两行 ID 也保留。逐列读回、创建/删除通知合同、NativeProbe Release 构建通过；`--ishellview-menu-command-private-desktop` exit 0，前台/last-input 与隔离清理守卫通过。

该验证证明本地单文件改名通知映射与 peer 行/详情更新。它没有覆盖文件夹改名或跨目录移动、重名冲突、连续通知合并、选区/焦点恢复、第三方 Shell provider、可见 UI 或产品 ExplorerBrowser；重命名项是固定 fixture 文件，实验没有触碰用户文件。接下来才适合在 peer 的 Details 模型中加入显式按需递归值，并单独验证数值排序与扫描期间更新策略。

### 2026-11-01：真实 IShellView 行的按需递归值 peer 列

新增独立 Private Desktop 探针 `--ishellview-recursive-size-projection-private-desktop`，避免把递归大小夹具混入菜单/分组合同。它从真实隐藏 filesystem `IShellView` 捕获 5 行 Shell PIDL、路径、标准 Details 字段与行 ID，在独立 peer `SysListView32` 增加“Recursive size (on demand)”列；显式触发后台扫描期间，peer 原行和 ID 保持不变。worker 线程 ID 与 STA/owner 不同；结果 2/10/100 B 写入对应行后，peer 原生 `LVM_SORTITEMS` 按 `ulong` 数值排序为 `size-2, child, size-10, size-100, keep.txt`，10 B 同值行由稳定 ID 决定次序。选中并聚焦的 `size-10` 行（ID=3）在排序后仍保持；NativeProbe apphost、Private Desktop 前台/last-input 与夹具清理守卫通过。

初次运行同时发现空的隐藏 Report ListView 尚未 materialize header；`RemovePeerColumns` 现将无 header 视为尚无列可删。Common Controls 6 分组依赖 NativeProbe apphost manifest；watchdog 对 peer 菜单和递归投影两场景改为启动 apphost，避免以 `dotnet.exe` 清单运行导致分组 API 不可用。

边界：递归值是探针显式请求后计算，不接入产品 F9/导航/刷新；列和排序只存在于 peer ListView，未改变或重绘 Shell 自身 DefView，也没有完成 Shell 通知后的重新扫描、可见像素/闪烁、真实慢盘/网络、用户多窗格压力或 ExplorerBrowser 产品接线验收。产品继续使用 ExplorerBrowser，不能据此宣称逐行递归大小列已交付。下一步在 peer 行变更通知闭环上验证递归缓存失效及值更新，同时继续通过菜单、分组和刷新回归门槛。

### 2026-11-01：递归大小对 Shell 更新通知的按需失效原型

隔离 `--ishellview-recursive-size-projection-private-desktop` 在初始后台扫描后修改 `size-10\payload.bin`，向 Shell 发布 `SHCNE_UPDATEITEM` 与 `SHCNE_UPDATEDIR`，确认监听器收到了通知；随后仅移除 `size-10` 的缓存并在 worker 线程重扫，得到 20 B。第一次重枚举发现 DefView 保留了更新前的目录 PIDL，而 `IShellFolder` 已返回相同文件系统路径、不同元数据 PIDL；按 PIDL 字节匹配会误报行缺失。原型现允许文件系统路径配对，并以卷序列号和文件 ID保留同一目录行身份，避免仅凭复用路径继承状态。更新后的数值排序、选中/焦点行和前台窗口均保持；Private Desktop 前台、输入、夹具清理守卫通过，NativeProbe Release build 0 警告/错误。

这只证明隐藏 `IShellView` 旁的独立 peer `SysListView32` 可以接收通知、局部失效和 worker 重扫。没有接入 ExplorerBrowser、产品窗格或可见 UI，也没有覆盖真实慢盘/网络项目、Shell 通知合并、通知丢失与批量目录变更；不能据此宣称产品逐行列已实现。接下来需验证扫描期间新通知到达时的重扫/过期结果保护，再评估迁移门槛。

### 2026-11-01：递归大小通知突发的异步去重切片

在上一节真实文件系统 Shell 更新通知场景上，快速改写 `size-10\payload.bin` 三次并发布多条 `SHCNE_UPDATEITEM`/`SHCNE_UPDATEDIR`。两次独立 Private Desktop 运行均收到 6 个更新事件；探针在 150 ms 安静窗口后按受影响目录去重为 1 项，仅启动一次 worker 扫描，最终读回 20 B。扫描被刻意延迟期间，peer ListView 行 ID 与选中/焦点保持；重枚举后原目录行身份、数值排序和焦点仍保持。NativeProbe Release build 0 警告/错误，两个 watchdog 运行均 exit 0，前台、last-input 和 fixture 清理守卫通过。

这只验证隔离探针内的 burst 收集、路径去重、worker 扫描及 peer 行保留；去重集合和安静计时尚未接入产品通知管线，通知由探针显式 `SHChangeNotify` 触发。未覆盖 watcher 溢出、取消/关闭竞争、慢盘/网络、多个窗格共享容量或可见刷新体验。下一步核对扫描期间新通知到达时的重扫/过期结果保护，再决定是否推进产品架构迁移评审。

### 2026-11-01：递归大小扫描中的新通知使旧结果失效

延续上一节 burst 场景，探针在 worker 已打开目标目录后暂停扫描；此时将 payload 从 20 B 改为 30 B 并发送新 Shell 更新通知。更新到达时递增本地扫描代次，释放 worker 后即使该轮读取到 30 B，也按已过期代次丢弃，不写入 peer/cache；随后启动的 worker 才发布最终 30 B。两次 Private Desktop 运行均通过，peer 行 ID、选择焦点、前台窗口保持，NativeProbe Release build 0 警告/错误，watchdog exit 0，隔离清理通过。

本轮只验证探针内的代次失效语义；被丢弃的 worker 因在通知之后才读取目录，数据本身也可能已是最新，因此它不是“扫描结果必然陈旧”的证明。产品没有逐行递归列，也没有接入此代次机制。剩余门槛包括与实际行模型/取消关闭生命周期接线、真实多窗格并发和可见刷新验收。

### 2026-11-01：被取代扫描取消不清除新缓存

在扫描代次切片后，探针再次将 worker 暂停于目录句柄打开点，先建立 30 B 的较新缓存，再递增代次、取消旧扫描并释放 worker。捕获到 `OperationCanceledException` 后，30 B 缓存、peer 行 ID 和选中/焦点仍保留。两次 Private Desktop 运行均通过，NativeProbe Release build 0 警告/错误，watchdog exit 0，前台/输入/临时目录清理守卫通过。

这是行模型外的探针局部取消合同；没有实际关闭 `IShellView` 或 Pane，没有验证应用 UI 线程错误报告、共享 worker 生命周期或多窗格并发。产品接线仍需用真实 scan identity/generation 验证，不能以此切片代替关闭生命周期验收。

### 2026-11-01：Browser 关闭时退役原生递归列 worker

注册 namespace 的 ExplorerBrowser 隔离探针会在验证尾部创建一个全新 `lifecycle-scan` 目录，并在目标出现前打开第二个 Browser，避免启动枚举预热其快照。两个 Browser 都通过包装 Shell folder 的 `GetDetailsOf` 请求同一路径；provider pending-set 分别确认两个 view 都订阅了未完成请求。探针随后销毁第一个 `IExplorerBrowser`，对其 view 调用 NativeProbe 宿主退役入口 `RetireSyntheticSortViewHoldForProbe`，再打开 worker 队列门闩。结果是两个订阅各自退出，一个取消、另一个成功遍历一次并写入目标目录快照；存活 view 随后读到 `3 B`，且该目录的 identity-validated snapshot 可由存活 view 查询到。

本次 Private Desktop watchdog 成功退出，NativeProbe Release build 0 警告/错误。全局快照项数从 3 增至 5，因它包含目标外 Shell 元数据/视图缓存变化，不能作为目标路径的断言；探针改为检查特定目标快照和存活 view 的读回值。运行期间前台与 last-input 未变化，隔离桌面清理守卫通过。

这仍是合成 namespace + ExplorerBrowser 隔离实验。它由探针在 `Destroy` 后显式调用 view-retire 入口；不证明 Browser 自动触发产品取消，也不验证产品 Pane/UI dispatcher 或可见重绘。

路线调整依据：通用 `StaticShellFolderAdapter` 已有真实文件系统 child PIDL 的包裹/解包和 Shell folder 方法转发；然而[包装 PIDL DefView 复验](#2026-11-01-包装-pidl-defview-列消费复验)中，DefView 只请求了自定义列标题，没有请求逐行值或排序比较。因此只把本隔离 namespace 的合成 child PIDL 替换为真实 PIDL，不能跨过列消费门槛。下一阶段优先在受控行视图中把已有真实 Shell 行身份、递归列、Shell 菜单/文件操作、通知刷新、焦点和拖放组合成一条集成合同；每个宿主迁移门槛仍需独立验证。产品和 release 未修改。

### 2026-11-01：Shell 通知刷新保持多选与独立焦点锚点

扩展 `--shell-change-reconciliation` 隐藏集成合同：初始 ListView 同时选择 `stable.txt` 与 `attribute.txt`，只将 `stable.txt` 设为 focused item。Shell `UPDATEITEM` 后后台扫描并回到 owner thread 发布，递归列从 `4 B` 更新至 `7 B`；之后的属性切换/恢复、create/update、名称重排、删除和递归目录改名/重扫，都重新投影行并恢复相同两项选择及 `stable.txt` 焦点。断言同时检查行模型 snapshot 和真实 `SysListView32` 的选中 ID 集合恰好为这两项，避免只检查单一焦点行而漏掉多选丢失。

同一命令继续通过双窗格 `IFileOperation` Copy/Move/Rename/Delete 与跨卷 Move 对账；所有窗口隐藏，前台未变化。NativeProbe Release build 0 警告/错误，`--shell-change-reconciliation` exit 0。该合同把多选保持接入真实 Shell 通知、递归值回填和行重排，但仍是隐藏自有 ListView 行模型，不是产品视图、真实鼠标输入、菜单或 OLE `DoDragDrop` 证据；ExplorerBrowser 产品和 release 未修改。

### 2026-11-01：真实 Shell 行的递归列逐项按需读取

扩展 `--ishellview-recursive-size-projection-private-desktop`：递归值先全部为空，首次请求只扫描选中的 `size-10` 目录；后台任务期间原 Shell 行名称与稳定 ID 不变，扫描结果只填充该目录单元格，其余文件系统目录仍显示未计算状态。探针随后显式请求剩余目录，确认各自结果映射到原 PIDL 行，再按 `ulong` 数值排序；原选中行身份仍保留。NativeProbe Release build 0 警告/错误，Private Desktop 探针 exit 0，前台、last-input 和夹具清理守卫通过。

这验证的是自控 peer ListView 的逐项按需扫描路径，不是 viewport 可见性检测、自动后台触发策略或 ExplorerBrowser 产品列；探针通过显式行请求模拟“需要该值”，并在同一轮内请求其余目录。Shell 通知突发/失效、取消和过期结果合同仍由前述独立探针覆盖。未接产品、未使用用户目录、未做可见 UI/性能基准或慢盘网络验证；产品和 release 未改变。

### 2026-11-01：递归列请求按当前视口筛选

扩展 `--ishellview-recursive-size-projection-private-desktop`：peer ListView 限为每页一行，将 `size-10` 行滚入视口后，使用原生 `LVM_GETTOPINDEX`/`LVM_GETCOUNTPERPAGE` 得到当前行区间，并只扫描区间内的目录。三次 Release apphost Private Desktop 运行均只请求当前可见的一个目录，其它目录的递归值仍为空；扫描期间原行、稳定 ID、选区与焦点保持，随后既有 Shell 通知失效、后台重扫和数值排序合同继续通过。前台、last-input 与临时夹具清理守卫通过。

这只验证“视口范围可读，并可作为按需请求过滤条件”；探针显式将选中目录滚入视口并调用该策略，没有把滚动/重排消息自动接成调度，也未验证真实可见绘制、用户快速滚动时的取消/节流、慢盘/网络并发或产品 ExplorerBrowser。产品及 release 未改变。

### 2026-11-01：视口请求去抖与过期请求取消

在上述视口切片加入 120 ms 的待调度窗口：先将 `size-10` 行设为可见并排队请求，再在窗口内把 peer ListView 滚到 `size-100`，第二个视口请求取消前一个；只有最终视口内的目录开始 worker 扫描，开始扫描次数为 1。三次 Release apphost Private Desktop 运行均通过，扫描期间 peer 行/ID 不变，非最终视口目录仍为空，后续通知失效、重扫、数值排序及选区焦点合同继续通过；前台、last-input 与 fixture 清理守卫通过。

本轮只验证 NativeProbe 的视口请求调度策略与取消令牌，不是实际 `WM_VSCROLL`、鼠标滚轮、滚动条或键盘滚动消息的事件接线；视口变化仍由探针显式模拟。未测试慢盘/网络中的已开始扫描取消、并发上限、可见绘制、产品 UI 或 ExplorerBrowser 接线。

### 2026-11-01：原生列表滚动消息驱动递归值请求

给 peer `SysListView32` 安装窗口子类，观察 `LVM_ENSUREVISIBLE`、`WM_VSCROLL`、`WM_MOUSEWHEEL` 与键盘消息处理后的 `LVM_GETTOPINDEX`/每页行数变化；只有视口实际变化才重新排队可见目录请求。隔离探针先将 `size-10` 滚入视口，再发送 `WM_VSCROLL/SB_LINEDOWN`；隐藏 ListView 的首行确实从该目录移到 `size-100`，观察器取消待执行的旧请求，最终仅启动一项 `size-100` 扫描。Release apphost Private Desktop 连续三次通过，后续通知失效、重扫、行 ID、选区焦点、前台和隔离清理合同均通过。

同一隐藏控件收到合成 `WM_MOUSEWHEEL` 后视口未变化；这说明隐藏控件不能用来证明滚轮生效。当前证据验证了原生 `WM_VSCROLL` 到视口观察器、去抖与请求取消的链路，不代表物理鼠标/滚轮输入、滚动条拖动、可见 ListView 或 ExplorerBrowser 产品接线。慢目录中已启动扫描的取消与并发上限也未验证。

在以上消息切片继续把旧请求停在 worker 已打开目标目录的回调点，再发送 `WM_VSCROLL` 切换到 `size-100`。观察器取消正在运行的 `size-10` scan；释放旧 worker 后它响应取消，不返回可发布结果，新视口请求成功完成。三次 Release apphost Private Desktop 运行均记录 `scans-started=2`、`scans-completed=1`、`obsolete-inflight-scan-canceled=True`，旧目录仍为空，仅最终可见目录值发布；通知失效后的新缓存、PIDL 行身份、多选/焦点及前台/清理合同继续通过。

这证明 NativeProbe scanner 合作响应取消并丢弃过期结果，不代表真实慢盘或网络 I/O 总能快速中止；没有测 worker 并发上限、物理滚轮/鼠标输入、产品 UI 或 ExplorerBrowser 接线。

### 2026-11-01：自控 Shell 行复用产品所选项命令适配器

在同一隐藏真实 `IShellView` 行投影中保留每行相对 PIDL；从选中 `size-10` 行的相对 PIDL 调用真实父 `IShellFolder.GetUIObjectOf(IContextMenu)`，再将 COM 菜单交给产品 `NativeShellCommandMenu`。产品适配器识别并确认 `cut/copy/delete/rename/properties` 均启用；未显示 popup、未调用动词，也未接触系统剪贴板。此前已通过的按需递归扫描、排序、通知失效和焦点合同继续通过。NativeProbe Release build 0 警告/错误，Private Desktop 探针 exit 0，前台/last-input 与隔离清理守卫通过。

此切片证明自控列表可按真实所选 PIDL取得 Shell 原生命令状态，并复用产品命令适配器；不证明菜单可见外观、owner-draw/第三方扩展消息、实际命令执行、剪贴板、撤销重做、OLE 拖放或产品窗格接线。相对 PIDL 只在当前 peer 行快照生命周期内持有，随行快照替换释放；产品和 release 未改变。

同一命令矩阵现扩展到真实 peer ListView 单选与双选：所选项菜单从一个或两个相对 PIDL 构建，`ShellFileCommands.Query` 同时读取所选项和 view-owned 背景菜单。单选时 Copy/Delete/Rename/Properties 与 Cut 可用；双选时 Copy/Delete/Cut 仍可用；两种情况下背景 New Folder 均可用。Release build 与 Private Desktop 探针重复通过，菜单未显示且未执行命令。该矩阵只核对命令可用状态，不验证剪贴板/删除等动词的多选执行效果。

随后让 peer `SysListView32` 自己作为菜单 owner，使用所选项真实 `IContextMenu` 运行 `TrackPopupMenu(TPM_RETURNCMD)`，收到一次 `WM_INITMENUPOPUP` 后由探针结束菜单，不选择或执行命令。单选菜单 `QueryContextMenu` 报告 239 个命令/46 个顶层项，双选为 162 个命令/41 个顶层项；两轮均进入 popup loop 并由 peer owner 子类继续调用默认窗口过程。两个 Shell 接口都可查询到：`IContextMenu3::HandleMenuMsg2(WM_INITMENUPOPUP)` 返回 `E_FAIL`，由于不是 `S_FALSE`/`E_NOTIMPL`，本次适配器策略没有再试 `IContextMenu2`（未调用），而是把消息继续交给 owner 窗口链。`CMF_DEFAULTONLY` 请求同样得到 `E_FAIL`，但菜单仍有 22 个顶层项，因此这不是干净的 extension-free 对照，不能据此将 HRESULT 归因于 Shell 或第三方命令。官方接口合同将 `S_OK` 定义为成功、其他 HRESULT 为错误，因此不能把这次 `E_FAIL` 当作扩展“未处理”的成功信号；菜单被跟踪并关闭只证明顶层 popup 生命周期可运行，不证明 owner-drawn/懒加载子菜单或第三方扩展渲染正确。NativeProbe Release build 0 警告/错误，Private Desktop probe exit 0，前台、last-input、临时注册及夹具清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-205410-aae5dd09.stdout.log`、`.stderr.log`。详见 [IContextMenu3::HandleMenuMsg2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu3-handlemenumsg2)。

### 2026-11-01：peer 多选 Shell IDataObject 复制集成

在真实隐藏 `IShellView` 行投影中，保持 `size-10` 与 `size-2` 两行的多选状态，从父 `IShellFolder.GetUIObjectOf(IDataObject)` 取得对应两项的真实 Shell 数据对象，并调用 `IFileOperation.CopyItems` 复制到探针唯一临时目录。复制后逐项比较目录载荷、确认源仍存在；peer 行 ID、两项选择及独立焦点锚点保持，系统剪贴板未触碰，操作未中止。NativeProbe Release build 0 警告/错误，`--ishellview-recursive-size-projection-private-desktop` exit 0；Private Desktop 前台、输入与临时夹具清理守卫通过。

该切片把真实所选 PIDL、`IDataObject` 与 Shell 文件操作接在同一条隐藏 peer 流程中；没有显示或执行原生上下文菜单动词，也未验证粘贴、撤销/重做、OLE 拖放、冲突提示/错误 UI、第三方扩展、可见刷新、产品 ExplorerBrowser 接线或用户数据。仍属于 NativeProbe 原型，未接入产品与 release。

### 2026-11-01：peer 多选 Shell 拖放目标回调

沿用前一切片由真实 peer 选中行取得的多项 `IDataObject`，将它交给独立临时目标文件夹的 Shell `IDropTarget`，直接调用 `DragEnter` 与 `Drop`，以 `DROPEFFECT_COPY` 完成两个目录复制。两项目标 payload 均与源逐字节相同，源目录保留；前述 `IFileOperation.CopyItems`、源 peer 行 ID/多选/焦点断言继续通过。NativeProbe Release build 0 警告/错误，Private Desktop 探针 exit 0，前台、last-input 与清理守卫通过；没有使用系统剪贴板或输入。

这是 Shell 目标的直接回调测试，没有运行 OLE `DoDragDrop`，也没有模拟鼠标拖动、拖动源完成回调、屏幕命中测试、滚动/自动激活、Move effect、可见光标/提示或 ExplorerBrowser 产品接线。此前 `DoDragDrop` 在无输入环境无法及时返回，因此不能把本结果写作真实拖放验收；产品与 release 未修改。

### 2026-11-01：Shell 拖放后的目标目录原生枚举

在上述直接 `IDropTarget.Drop` 完成多选目录复制后，探针对目标文件夹调用 `IShellFolder.EnumObjects`，读取返回的子 PIDL 并以完整文件系统 parsing name 解析。Shell 枚举结果精确为 `size-10` 与 `size-2`，与目标目录磁盘内容及源选中集合一致。重复 Private Desktop 探针 exit 0，NativeProbe Release build 0 警告/错误，前台、last-input 与清理守卫通过。

这增加了“目标 `IDropTarget` 接受 → 目标 Shell folder 重新枚举”的后台一致性证据；没有创建目标 `IShellView` 或 peer ListView，也没有行 ID/焦点恢复、延迟通知、真实拖动时序、Move effect、可见刷新或产品接线证据。不能视为目标窗格的完整刷新验收。

### 2026-11-01：Shell Drop 后目标 IShellView 行同步

扩展独立 `ShellViewDropTargetProbe`，在临时目标目录创建哨兵文件，并等待真实目标 `IShellView` 将它枚举为行，再删除并等待空行集合；只有完成这次首次枚举后才执行选中项 Copy。此屏障也用于“先销毁再重建目标 view HWND”的对照，避免把首次枚举尚未完成误判成 Drop 刷新失败。

普通目标 view 和先销毁/重建 HWND 两种路径均通过 Private Desktop 探针。重建后初始行集合为 0；两次后续复跑中，Drop 产生的文件均在 800 ms 内自然进入目标 view 行集合，逐路径与磁盘一致；无需显式 `IShellView.Refresh` 或 `SHChangeNotify` 回退。重建路径投影后的 3 个目标行 PIDL 身份唯一，显式 Refresh 后 PIDL 身份保持；源端两项选择和焦点锚点保留。普通路径仍通过单选/多选复制、同卷移动和跨卷 Copy/Shift-Move 矩阵。NativeProbe build 0 警告/错误，Private Desktop 前台、last-input、清理守卫通过。

该探针直接调用 `IDropTarget.DragEnter/Drop`，没有运行 OLE `DoDragDrop` 或模拟鼠标，也没有验证屏幕命中、拖放提示、Move 后目标/源 view 同步、可见 Shell/产品 ExplorerBrowser 或释放版行为。哨兵只证明探针开始拖放前目标 view 曾完成过一次枚举；不能据此认定真实窗口重建时产品会自动复用相同初始化时序。

### 2026-11-01：四视图 600 目录队列压力复验

使用 Private Desktop 合成 Shell view，以四个不同根目录、每视图 600 个同级目录复验 2,048 项队列的边界。逐行队列模式在两个 worker 暂停时拒绝 354 个请求，并在探针的“不得拒绝”断言处退出；前台与 last-input 未变化。切换到现有 view-batch 模式后，闸门前拒绝数为 0，四个视图最后均读回 602/602 个值，队列拒绝 0，峰值 worker 2，通知及 view 枚举增量各 12。

该高负载探针在清理含 2,400 个目录与约 24,000 个载荷文件的夹具时到达 watchdog deadline，故整体进程退出不是通过；回填指标是在清理前记录的。累计扫描数为 6,054，显著高于 4 × 602 个请求，当前尚不能把 view-batch 结论概括为无重复扫描。本轮没有改产品或调度代码，也没有可见 UI/ExplorerBrowser 产品接线证据。后续先拆解多余扫描来自通知回填还是 Shell 重复请求，再优化探针夹具清理耗时；不要仅因列值最终齐全就扩大队列容量。

### 2026-11-01：共享缓存容量与重复扫描对照

为了分辨上述重复扫描是否与队列模式有关，在相同 4 个不同根视图、每视图 100 个目录负载下分别运行共享缓存容量 2,048 与 128 的 view-batch 探针。容量 2,048 时扫描恰为 404 个唯一值、重复 0，Shell 通知 4 次，四个视图均完整回填，Private Desktop 探针 exit 0。容量 128 时最终值仍完整，但两次运行观察到 1,404–3,498 次扫描、14–38 次 Shell 通知/视图重枚举；NativeProbe 均因扫描数超出 404 唯一扫描基线而 exit 1，Private Desktop 前台、输入和清理守卫通过。

新增的 NativeProbe 计数直接记录共享缓存 lookup hit/miss 和容量驱逐。容量 128 的一次复跑记录 1,242 hits、5,664 misses、1,276 capacity evictions、1,404 scans、14 notifications；容量 2,048 的 100 目录/视图同负载记录 1,212 hits、1,664 misses、0 evictions、404 scans、4 notifications。hit/miss 是 `TryGet` 与 `TryReserveScan` 调用次数，不是唯一行数。该小型对照强烈表明容量不足会引发驱逐、Shell 重查和扫描/通知放大；扫描数会随运行时序变化。

### 2026-11-02：最小夹具验证缓存容量阈值

用相同的四个不同根视图、每视图 512 目录的最小夹具（每个目录 1 个载荷文件）比较 2,048 与 4,096 容量。容量 2,048 时期望 2,052 次唯一扫描，两次实测为 2,219–2,246 次，发生 171–198 次驱逐、8–9 次通知/枚举增量；所有列值最终齐全，但探针因 167–194 次重复扫描 exit 1，Private Desktop 清理守卫通过。容量 4,096 时扫描 2,052 次、驱逐 0、通知/枚举增量各 4，四视图行值、选区和焦点保持，探针 exit 0，夹具正常清理。

同一配对样例记录的缓存 payload 估算值分别为 675,810 B（2,048 个快照）与 677,120 B（2,052 个快照），多保留 4 条快照增加约 1,310 B。该数值包含键/值结构估算，不包括 map 节点、分配器开销和其它进程内存，不能直接当作 Private Bytes 增量。仍未将每次驱逐与具体 Shell 请求、通知逐项关联。产品继续使用 ExplorerBrowser，递归列未接入产品。

随后把 NativeProbe 的默认递归缓存容量从 2,048 调到 4,096，并用四个不同根目录、每视图 600 个目录的最小夹具（每目录一个载荷文件）验证默认值。探针扫描 2,404 个唯一目录，重复扫描 0、容量驱逐 0、队列拒绝 0；四个视图各 602/602 列值就绪，通知与枚举增量各 4，选中/焦点保持，Private Desktop 前台/输入/清理守卫通过，退出码 0。结果缓存 payload 估算为 793,280 B；探针进程记录的 Private Bytes 为 98,312,192 B，但这包含整个 NativeProbe/Shell 运行时，不能归因于递归缓存。这里的默认值仅属于 NativeProbe 验证配置，不是产品缓存容量，也不构成将产品容量改为 4,096 的依据。

为核对 4,096 容量附近的四视图工作集，将 NativeProbe view-batch 最小夹具上限扩至每视图 1,000 个目录；四个不同根共 4,004 个唯一目录快照，4096 容量下驱逐、重复扫描及队列拒绝均为 0。每个 Shell view 的 1,002/1,002 列值就绪，通知/枚举增量各 4，峰值 worker 2，选中/焦点保持；Private Desktop 前台、输入及清理守卫通过，watchdog exit 0。payload 估算为 1,321,280 B，进程 Private Bytes 为 101,068,800 B；后者涵盖整个探针进程，不能当作缓存成本或与上一轮之间的缓存内存差。结果说明 4,096 能覆盖这组约 4,000 行的四视图负载，但只剩 92 个快照余量；更多活动标签/视图、真实产品缓存复用和实际内存仍需单独评估。产品及 release 未修改。

### 2026-11-01：双真实 IShellView 共用递归大小扫描与通知更新

在同一隔离文件夹创建两个真实 `IShellView`，各自捕获 Shell PIDL 行并投影到独立 peer `SysListView32`。两视图同时请求相同四个目录（含基础夹具子目录）时，测试用 `ConcurrentDictionary<string, Lazy<Task<...>>>` 按规范化路径合并在途任务；两侧均读到同一结果对象，实际调用产品 `RecursiveDirectoryScanner` 恰好启动四次扫描。两边分别按数值排序后，行 ID、选中和焦点状态保持。

修改 `size-10\payload.bin` 并发送 Shell 更新通知后，两视图均完成行重枚举，PIDL 行身份保持；删除该目录缓存条目后，两视图并发请求只启动一次新扫描，均得到 20 B，并再次排序、保留选中焦点。NativeProbe Release 构建 0 警告/错误，Private Desktop watchdog exit 0；前台窗口、last-input 与隐藏父窗口守卫通过。调试时发现 `CapturePeerShellRows` 会转移匹配行的绝对 PIDL 并自行释放旧相对 PIDL，调用者不能再整体释放旧快照；修正了探针所有权处理。

这只验证两个真实 Shell view 与探针自建共享缓存/peer list 的扫描和通知合同；产品目前仍使用 ExplorerBrowser，产品 Pane 订阅生命周期、取消/退役、真正逐行列消费、滚动触发和可见交互均未接入或验证。探针未改变产品或 release。

### 2026-11-01：多视图递归快照按 Shell 更新目录范围失效

在双真实 `IShellView` 共享缓存切片上继续接入 `SHChangeNotifyRegister` 事件到缓存失效的探针逻辑。对 `size-10\payload.bin` 写入 20 B 并发送 `UPDATEITEM`/`UPDATEDIR` 后，本机这次事件突发还包含同级其它目录的 `UPDATEITEM`。若把事件路径逐个直接映射为失效，四个已缓存目录都会被重扫；将 `UPDATEDIR` 当作范围边界、仅合并其目录内的项目更新后，三次 Private Desktop 运行均只失效 `size-10`，两视图共享一次新扫描，扫描总数从 4 增至 5，读回 20 B，PIDL 行 ID、排序、选区和焦点保持。

这只是探针针对该事件形态验证的失效策略；其它通知类型（新增/删除/改名）、重叠目录更新、通知丢失/溢出、网络路径和产品 watcher/batch 接线尚未覆盖。产品仍使用 ExplorerBrowser，未改产品或 release。

### 2026-11-01：递归列缓存对新增、删除与改名通知的失效

扩展双真实 `IShellView` 共用缓存探针，对已缓存的 `size-10` 目录依次在隔离夹具内创建 3 B 文件、删除该文件、再将原 20 B 载荷文件改名。每步都发送相应 Shell `CREATE`、`DELETE` 或双路径 `RENAMEITEM`，并发送父目录 `UPDATEDIR`；探针从事件 PIDL 解析路径，只移除包含这些变更路径的递归根缓存。三轮重复运行均通过：每一步只失效 `size-10`、两视图共享一次重扫，字节结果依次为 23、20、20；行 ID、数值顺序、选中与焦点保持。

此次还补了按当前 ListView 稳定行 ID 回填列值并逐行核对显示文本的断言。原因是大小排序会改变控件行序，旧快照的枚举索引不能再直接作为 ListView 行索引。

限制：这些是隔离夹具上的显式 Shell 通知，不涵盖文件系统自然通知的完整时序、重叠通知、丢失/溢出、目录重命名后子树缓存重键、网络路径及产品 watcher/batch。产品仍使用 ExplorerBrowser；本轮未改产品或 release。

### 2026-11-01：双视图中的递归目录改名与缓存键退役

在已经覆盖文件 create/delete/rename 的双 `IShellView` 原型中，再把可见行 `size-10` 实际改名为 `size-10-renamed`，并显式发送双路径 `SHCNE_RENAMEFOLDER`。两份真实 Shell view 均收敛到新名称；基于真实 filesystem identity 对账后，目录行的稳定 ID=3、选中和焦点保持。失效逻辑只退役旧路径缓存；两视图随后对新路径共用一次生产递归扫描，读回 20 B。Peer 列表在已按大小排序的情况下依稳定 row ID 更新名称，数值顺序及逐行列文本检查通过。包含此前文件 update/create/delete/rename 的完整探针连续三轮 exit 0；NativeProbe Release 构建 0 警告/错误，Private Desktop 前台、输入和夹具清理守卫通过。

该测试通过 `Directory.Move` 改变真实夹具，再由探针显式发出 `SHCNE_RENAMEFOLDER`；它不证明所有文件系统 watcher 与 Shell 通知自然时序，也未覆盖重叠子树缓存、重命名竞争、通知丢失/溢出或产品生命周期。仍然只是 NativeProbe；产品继续使用 ExplorerBrowser，未生成 release。

### 2026-11-01：ExplorerBrowser 注册 namespace 的递归列排序入口

扩展已注册临时 Shell namespace 的 ExplorerBrowser 探针：夹具包含递归大小为 2 B 与 10 B 的两个文件夹，以及 4 B、25 B 文件。`IShellFolder2.MapColumnToSCID` 能返回递归列的自定义 property key，但 `IFolderView2.SetSortColumns` 对该 key 返回 `TYPE_E_ELEMENTNOTFOUND (0x8002802B)`；改用标准 `PKEY_Size` 虽然接受，Shell 行序为 `child|child-small|root.txt|rename-source.txt`，递归大小 `CompareIDs` 调用为 0，无法按文件夹递归大小排序。

同一活动 `IShellView` 上查询 `IShellFolderView` 并调用 `Rearrange(1)` 后，provider 的比较器收到 5 次比较，真实 Shell 行序变为 `child-small|root.txt|child|rename-source.txt`，与 2、4、10、25 B 数值顺序一致。Release NativeProbe 构建 0 警告/错误，注册 namespace Private Desktop 全流程 exit 0；临时注册清理、前台及 last-input 守卫通过。

该切片证明 ExplorerBrowser 承载的注册 namespace 可以用旧式列索引触发自定义递归数值排序；没有验证用户鼠标点击表头如何到达该排序入口、排序指示器/升降序状态、真实物理目录的 child PIDL 身份，也没有可见 UI 验收。普通 ExplorerBrowser 文件系统路径与产品仍未接入该 provider；NativeProbe 夹具是临时 backing 目录。下一步应在隔离 `IShellView` 宿主里验证表头排序事件和排序状态恢复，再决定如何映射到产品窗格导航与标签会话。

### 2026-11-01：表头点击递归排序及宿主清理对照

使用已有的 WPF `HwndSource` 根宿主分支，在 Private Desktop 重新运行注册 namespace 探针。该探针通过真实 `SysHeader32` 列标题鼠标事件验证升降序：显示值为目录 `child=10 B`、文件 `small.bin=4 B`，升序成为 `small.bin|child`，降序恢复 `child|small.bin`；比较回调各 1 次，排序期间递归指标扫描为 0。独立背景列菜单的升序/降序状态也分别读回 `1`、`-1`，行序正确。

与此前原生 `STATIC` 宿主在 `DestroyWindow(parent)` 阶段超时的运行对照，本次 WPF 根宿主完整退出，watchdog exit 0，Private Desktop 前台 HWND 与 last-input tick 均未变化；watchdog 确认隔离进程、临时 CLSID/NameSpace 注册和两个夹具目录均已清理。`DllCanUnloadNow` 返回 `S_FALSE`，NativeProbe 将原生模块留到隔离进程退出时回收。该对照说明 WPF 宿主分支可完成原型生命周期，但尚未解析 STATIC 宿主挂起的根因，也不能据此认定 WPF 是唯一可用的产品宿主。

日志：`src/.state/native-probe-watchdog/20261101-204535-42696297.stdout.log`、`.stderr.log`。该结论仅覆盖隔离 `IShellView`/ExplorerBrowser 注册 namespace 原型；产品仍使用 ExplorerBrowser，逐行递归大小列未接入产品，未验证产品菜单、操作、拖放、通知、焦点或可见 UI。

### 2026-11-01：按需递归大小的同目录百分比更新

在隐藏 `IShellView` 的 peer `SysListView32` 原型增加相对百分比列。百分比分母取当前目录直接文件大小与直接子文件夹递归大小之和；在任一同级目录大小仍未完成时，整列保持空白，避免用部分分母给出错误比例。首轮按视口只扫描一个文件夹，其他行仍待计算；所有同级目录值完成后才回填比例。夹具中 `size-10` 为 `10/122 = 8.19672131147540983606557377%`；自然 Shell 更新令该目录从 10 B 变为 30 B 后，分母随之从 122 B 变为 142 B，比例重算为 `21.12676056338028169014084507%`。

NativeProbe Release 构建 0 警告/错误；Private Desktop `--ishellview-recursive-size-projection-private-desktop` exit 0，前台、输入和隔离清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-210628-5db8401e.stdout.log`。这验证的是隐藏 peer 列的数据合同，不是 Q-Dir 的显示舍入/下划线样式，也未接 ExplorerBrowser 产品或可见 UI。

该切片暴露了一个真实取舍：只显示可见行递归大小时可保持按需扫描，但准确的同目录百分比要等全部同级文件夹的递归大小已知后才能计算；本原型在此之前留空。产品接线前还需比较“同级逐目录请求”与“单次遍历当前目录并累计全部行值”的扫描成本，并由可见验收决定空白阶段和比例格式；没有把本轮策略提升为产品默认值。

### 2026-11-01：同级百分比分母的两种扫描策略对照

新增 `--recursive-directory-sibling-percentage-strategy-probe`，在 64 个同级文件夹、每个含一个嵌套目录及 16 个文件、根目录另含一个文件的暖缓存临时夹具上，对比同一个生产 `RecursiveDirectoryScanner` 的两种用法：单次 `ScanDirectoryTreeForPaths(root, root + all sibling folders)`；以及重用已枚举的子文件夹路径和根目录文件大小、逐个调用 `ScanDirectoryTreeForPaths(folder, [folder])`。两种策略的根目录总字节数及每个同级文件夹摘要完全一致。

连续两次各做 1 次预热及 5 次计时，单次树扫描的中位数分别为 25 ms、48 ms，逐文件夹扫描为 47 ms、68 ms；单次树扫描托管分配均为 152,104 B，逐文件夹扫描均为 171,064 B。树扫描记录访问 1,217 项；逐文件夹扫描访问 1,152 项，因为它复用已知的根行，省去 65 个根项重新枚举。两轮方向一致但暖缓存耗时有波动；这里只能说明此本地夹具中一次遍历较快且分配较少，不能外推到首次冷读、机械盘、网络位置、访问拒绝或产品内存。Release SettingsSmoke 构建 0 警告/错误，定向探针两次 exit 0。产品视图未修改。

当前证据支持优先采用单次目录树遍历生成所有同级递归快照，再显示精确百分比；仍需验证扫描中的逐行增量发布/取消对交互响应的影响，以及冷/慢存储行为，之后才能决定产品接线。测试命令：`dotnet run --project src/ExplorerDirs.SettingsSmoke/ExplorerDirs.SettingsSmoke.csproj -c Release --no-build -- --recursive-directory-sibling-percentage-strategy-probe`。

### 2026-11-01：目录树扫描逐行回报与取消原型

给 NativeProbe 复用的句柄绑定目录树扫描器增加可选 `completedDirectory` 回调。每个被请求目录的句柄扫描完成后，扫描器保存其身份/大小摘要、归并到父目录并释放该目录句柄，再从扫描线程回报结果；根目录最后回报。不可递归进入的 reparse 目录也会回报明确的不可读摘要。既有调用不传回调，原有返回字典路径保持不变。

新增 `--recursive-directory-tree-incremental-publish-probe`：在 8 个同级目录夹具上暂停第一次目录完成回调，确认 owner 线程可在根目录扫描结束前取到完整目录摘要，再确认全部回调与最终字典一致、根目录为最后一项。第二轮在第一个同级目录完成时取消，确认只收到该目录的结果、不回报根总量，扫描句柄释放后夹具可以删除。SettingsSmoke Release 构建 0 警告/错误；定向 probe、既有递归扫描器 smoke、Shell ListView 行模型 smoke 均通过。

边界：这是隔离扫描器 API 和队列消费模型验证；owner 线程取队列模拟 UI 投递，不是实际 Dispatcher，也未接产品扫描协调器、Shell 行刷新或可见列表，因此不能证明产品逐行绘制效果、滚动/排序稳定性、UI 响应时间或冷盘/网络取消延迟。百分比仍应等同级结果齐全后统一计算；本次只验证大小摘要可以先行发布。产品未修改。

### 2026-11-01：增量扫描中请求目录消失时回报不可读行

在同一探针加入目录竞态：扫描器已从父目录枚举出 `vanishing-folder` 后，`beforeChildOpen` 将它移出原位置，再继续扫描。红测确认此前结果字典与完成回调都缺少这条请求行；修复后扫描器以枚举时的文件 ID 回报该行 `0 B / Unreadable=1`，并将同一不可读项计入根目录摘要。身份仍由消费者的当前行身份/扫描代次校验，扫描器不会把该占位值直接写入 Shell view。真实可打开目录及根目录的逐行回报/取消合同保持通过；根目录 reparse 分支也在回报前释放根句柄。

SettingsSmoke、App 与 NativeProbe Release 构建均为 0 警告/错误；增量探针、递归扫描器 smoke、Shell ListView 行模型 smoke 通过。该测试使用本地临时目录移动竞态，不代表访问拒绝 ACL 或远程 SMB 延迟表现。仍是 NativeProbe 扫描 API 改进，产品列、Shell 行更新与 UI Dispatcher 未接入。

### 2026-11-01：行模型按单个递归结果增量更新

整批 `ApplyRecursiveScanResults` 会把结果字典缺少的文件夹行清空，因此不能安全消费一个目录一个目录到达的回调。为原型行模型新增单项应用入口：只匹配指定绝对路径，核对行快照身份、扫描句柄身份与当前路径身份后写入该行；其它行的待计算状态或已有值保持不变。同路径目录已被替换时，旧结果被拒绝且只清空目标行的旧递归值。

SettingsSmoke 的红测在实现前因增量入口缺失而编译失败；实现后 Release 构建 0 警告/错误，行模型 smoke 与递归扫描器 smoke 通过。随后将队列、真实句柄扫描器和行模型入口组合到同一无窗口 probe：worker 在首个目录回调暂停时，owner 从队列取结果并只更新目标行；扫描结束后八个文件夹行与最终结果逐一对账，根目录回调不误匹配任何子项。此项打通了 callback 结果到隔离行模型的更新语义，但尚未把它接到 NativeProbe 的 peer ListView 更新循环或产品 Dispatcher；尚无可见行渐进更新证据。

### 2026-11-01：扫描协调器逐项 owner 发布与过期队列门控

扩展隔离 `DirectoryTreeScanCoordinator` 的最新扫描合同：worker 每产出一项就投递到 owner `SynchronizationContext` 并等待该项被处理，再继续扫描，保证更新有序且最多一个待处理增量，owner 处理速度会自然限制扫描推进；最终摘要在所有增量之后发布。若队列等待期间该 ticket 被新扫描/取消/释放取代，owner 回调重新校验代次并丢弃该项，worker 随后停止，旧请求返回未发布。

新增 STA Dispatcher 检查 owner 线程及“单项在最终结果前”的顺序；queued context 检查已排队结果在代次退休后不可发布。SettingsSmoke Release build 0 警告/错误，包含 coordinator、行模型与递归扫描的 smoke 通过。该协调器并未接入 peer ListView，也没有可见增量绘制结果；这是 owner 派发/背压原型，不是产品功能。

### 2026-11-01：peer ListView 增量递归值回写接线

为句柄扫描器的逐根 `Scan` 增加完成回调；每个请求目录扫描后先关闭根句柄、写入结果表，再发布身份化摘要。无窗口 smoke 确认 8 个独立扫描回调与返回字典逐项一致。随后将 peer `SysListView32` 原型剩余目录阶段改为使用 `DirectoryTreeScanCoordinator.RunLatestIncrementalAsync`：worker 的每条结果在 owner Dispatcher 验证 Shell 行身份后，按实时 ListView 稳定 row ID 更新递归大小子项；检查行序与选中/焦点不变，全部结果到齐后再与最终字典对账并计算百分比/执行数值排序。

NativeProbe 和 SettingsSmoke Release build 均为 0 警告/错误；增量扫描探针与递归扫描器 smoke 通过。随后 Private Desktop `--host-recursive-size-projection-private-desktop` 首轮 watchdog exit 0：peer ListView 实际收到并增量写入 3 条剩余递归值，最终映射对账通过，选择/焦点不变，前台、last-input 与清理守卫通过（日志 `src/.state/native-probe-watchdog/20261101-212614-cc233652.stdout.log`）。第二轮相同功能断言也通过，但 watchdog 观察到 last-input tick 从 84680437 变为 84687812，故以 `isolation-clean=False` / exit 1 结束（日志 `src/.state/native-probe-watchdog/20261101-212630-9824c1e9.stdout.log`）；原因未确定，未继续重跑。由此确认隔离 peer ListView 的逐项回写路径可运行，但重复运行的桌面隔离守卫尚未稳定通过。实际绘制闪烁、滚动/排序期间异步时序及可见 Shell 体验仍待集中验收。产品 ExplorerBrowser 与发布版未改。

### 2026-11-01：递增 owner 发布在扫描退休时解除等待

新增队列化 owner context 复现：逐项扫描把一条结果投递给 owner 后，worker 会同步等待该回调；若 coordinator 在派发前取消，旧实现只使回调失效，却没有唤醒等待 worker。红测在不派发该回调的情况下确认增量请求两秒内仍未结束；同一等待也会令完成扫描的普通 owner 发布任务滞留。现将 owner 发布统一经过带退休取消注册的 helper，扫描 ticket 退休即完成等待并返回未发布；排队回调稍后运行仍会通过当前 ticket 检查被丢弃。`--recursive-directory-scanner-smoke` 通过，包含增量 worker 与普通发布两条退休回归、迟到回调不写入、coordinator/行模型及递归扫描检查。该修复仅覆盖隔离 NativeProbe coordinator 的显式取消/退休合同；产品窗格关闭是否总能退休对应扫描仍未接线/验证。

随后将真实 `HandleBoundRecursiveDirectoryScanner.ScanDirectoryTree` 接入同一个 SettingsSmoke 队列场景：第一条目录完成结果排入 owner context 后立即释放 coordinator。检查 worker 在两秒内退出、扫描回调只发生一次、owner 行更新和最终摘要均未发布；迟到回调被手动派发后仍不写入，且夹具目录可立即删除，证明该场景没有遗留扫描句柄。`--recursive-directory-scanner-smoke` Release 通过。仍未覆盖实际 Shell view HWND 销毁与 UI Dispatcher 关闭的联动；产品未接线。

### 2026-11-01：ExplorerBrowser 包装 namespace 的父级与 TravelLog 探针接线

扩展现有隔离 ExplorerBrowser 注册 namespace 探针：在真实临时 Shell namespace root → child → nested 导航后，调用 `SBSP_PARENT` 回到 child，再用 Shell 内建 TravelLog Back/Forward 往返 nested/child；每步按当前 `IPersistFolder2` 的 class ID 和绝对 PIDL 等待导航完成，最多 5 秒，不再依赖固定短延时。根目录/子目录已有菜单、重命名、文件操作、通知合同继续留在同一探针。

NativeProbe Release build 通过，0 警告/错误。按无窗口优先约束，本轮没有运行 Private Desktop ExplorerBrowser 宿主探针；因此父级与 Back/Forward 的 Shell 实际行为仍待隔离运行确认，更不代表 Explorer Dirs 产品窗口、标签历史或可见地址路径已验证。ExplorerBrowser 产品及 release 未改。`BrowseToIDList` 的父级/TravelLog flags 与空 PIDL 用法依据 [Microsoft API 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowser-browsetoidlist)。

后续扩展同一导航探针：Back/Forward 返回 child 后读取该文件夹的 `IColumnManager` 列集合，并从真实 child view 读取 `nested` 的递归大小，记录列是否可见且值是否仍为 `10`。首次 Private Desktop 运行在 child PIDL 已匹配后立即读取行数，未等视图枚举完成而失败；增加最多 5 秒的行数轮询后，显式导航到 nested 的 5 秒 PIDL 等待仍超时。进一步诊断确认：child view 的 `BindToObject("nested")` 成功，返回对象的 `IPersistFolder2` PIDL 与手工组合的目标完全一致；完整 Shell parsing name `:::{CLSID}\child\nested` 解析出的 PIDL 也与手工组合完全一致。`BrowseToIDList` 和 `BrowseToObject` 均返回 `S_OK`；两条路径都触发 `OnNavigationPending(nested)` 与 `OnNavigationComplete(nested)`，无 `OnNavigationFailed`，但 nested 阶段没有 `OnViewCreated`，随后 `GetCurrentView` 读到的活动 class/PIDL 仍为包装类/child。Microsoft 文档规定事件顺序为 pending → view-created → complete/failed，且 complete 在成功创建 view 后调用；本次 nested 事件顺序违反该公开合同，物理路径对照则按顺序产生新 view。参考 [IExplorerBrowserEvents](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowserevents) 与 [OnNavigationComplete](https://learn.microsoft.com/en-us/windows/desktop/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowserevents-onnavigationcomplete)。探针改为在 `Initialize` 前设置与产品相同的 `ShellBrowserSite`，结果不变。作为对照，同一 Browser 再以物理路径 `fixture\child\nested` 调用 `BrowseToIDList`，触发 pending/complete、创建一个文件系统 Shell view，并读回完整物理路径。由此排除 Browser 普通深层导航故障与缺失 ShellBrowserSite；剩余差异是注册 namespace provider 的深层目标未触发 view 创建/切换。最近两次运行证据在 `src/.state/native-probe-watchdog/20261101-215247-bdcace1f.stdout.log` 和 `src/.state/native-probe-watchdog/20261101-215459-aa5fa08e.stdout.log`；此前 PIDL/Bind 路径证据见 `20261101-214759-10cda3b4.stdout.log`。所有 watchdog 均确认前台 HWND、last-input 未变且临时注册已清理。TravelLog Back/Forward 及返回后的列/值恢复因此仍未实测；下一步应检查 provider 对 nested 的 `BindToObject`/`IPersistFolder2`/view 创建链及 Shell view site 协议，判断是当前 synthetic provider 实现缺少必要行为，还是该 namespace 包装方式与 ExplorerBrowser 不兼容。NativeProbe Release 构建 0 警告/错误；产品及 release 未改。

### 2026-11-01：wrapped namespace nested view 创建边界

在 `BindToObject("nested")` 与 `CreateViewObject(IShellView)` 增加 provider 边界日志后再次运行：nested `BindToObject` 多次被调用，均以 `S_OK` 返回目标 `fixture\child\nested`；但整个 nested 导航期间没有对应的 `CreateViewObject(IShellView)` 进入记录。Browser 仍报告 `pending → complete`，未报告失败；随后显式导航同一物理 nested 路径时，文件系统 view 创建成功并读回目标路径。由此进一步定位为 Shell/Browser 没有向 nested provider 请求 view 创建，不是 `SHCreateShellFolderView` 返回失败。

Native adapter 与 NativeProbe Release 构建通过；Private Desktop watchdog 的 nested view 功能断言按预期以 exit 1 暴露 view 未创建，隔离守卫没有检测到前台或输入污染。日志：`src/.state/native-probe-watchdog/20261101-215746-2a0b9bee.stdout.log`。下一步核对嵌套 PIDL 的子项属性、folder binding interface / bind context 与 view site 初始化时序。证明 Shell view 可稳定创建前，不把递归列原型接入产品 ExplorerBrowser。

### 2026-11-01：直接创建 nested IShellView 对照

在已由 `BindToObject("nested")` 返回的 nested `IShellFolder2` 上，直接调用 `CreateViewObject(IID_IShellView)`，结果为 `S_OK` 且返回非空 view；随后释放该接口。这个调用单独触发了 nested provider 的 `CreateViewObject` 日志，证明 provider view factory 本身可工作。接着由 ExplorerBrowser 执行 `BrowseToObject`，它仍报告 `pending/complete`，但活动位置留在 `child` 且 PIDL 不匹配；直接物理路径导航依旧创建 filesystem view 并成功。

NativeProbe Release build 0 警告/错误；Private Desktop 功能运行因既有 wrapped nested 导航断言失败而 exit 1，隔离守卫仍报告前台与 last-input 未变。日志：`src/.state/native-probe-watchdog/20261101-220048-ba141f85.stdout.log`。这排除了 nested `CreateViewObject` 不能创建 view 的假设；当前证据指向 ExplorerBrowser 对已注册包装 namespace 的深层 `BrowseToObject` 导航未切换 view。该调用只证明 view 对象能创建，未证明其 `CreateViewWindow`、菜单/命令、文件操作、通知及历史链路可用，也未接入产品。

### 2026-11-01：浏览属性与 IShellItem 导航假设对照

记录 ExplorerBrowser 对 `nested` 行请求 `SFGAO_BROWSABLE`（requested `0x7EFBBFFF`）但 backing folder 只返回 `0x7080017F`。隔离地在可浏览文件夹返回值中补 `SFGAO_BROWSABLE` 后，仍没有 nested view 创建，Browser 活动位置仍停留在 child；该行为改动已撤回。随后用 `SHCreateItemFromIDList` 从同一绝对 PIDL 构造 `IShellItem`，分别对 `IShellFolder2` 和 `IShellItem` 调用 `BrowseToObject`：两次 HRESULT 均为 `S_OK`，class ID 匹配包装 provider，但活动 PIDL 仍不匹配且位置为 child。物理 nested path 导航仍成功。

Private Desktop watchdog exit 1 是新增导航成功条件未达到；隔离守卫通过。日志：`src/.state/native-probe-watchdog/20261101-220454-536cbce2.stdout.log`。NativeProbe Release build 0 警告/错误。该结果排除了仅缺少 `SFGAO_BROWSABLE` 或 `BrowseToObject` 目标接口类型导致该现象。微软 `BrowseToObject` 文档明确说明该调用在挂起导航设置正确时可返回 `S_OK`，但这不保证导航最终成功，须结合导航成功/失败事件判断：[IExplorerBrowser::BrowseToObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowser-browsetoobject)。

重新用撤回 `SFGAO_BROWSABLE` 修改后的原生 DLL 运行相同对照：属性回到 `0x7080017F`，`BrowseToObject(IShellFolder2)` 与 `BrowseToObject(IShellItem)` 仍都返回 `S_OK`，两个路径都停留在 child；物理路径导航成功。结果与补属性版本一致，确认浏览提示不是根因。Private Desktop watchdog 日志：`src/.state/native-probe-watchdog/20261101-220619-d86d5a92.stdout.log`；前台/输入隔离守卫通过。当前 NativeProbe 源码保留属性日志与 IShellItem 对照，属性行为恢复为 backing folder 原值。

### 2026-11-01：自建 IShellView host 的 registered namespace 深层导航与递归列

将同一个临时 registered namespace root / child / nested PIDL 接入已存在的隐藏 `StreamViewHost` / `IShellBrowser` 迁移原型。该宿主通过 `SHBindToObject` 绑定每个绝对目标、创建并切换 `IShellView`，完整跑通 root → child → nested；child view 中 nested 文件夹的 Shell 大小列读回 `10`，nested view 枚举到 `deep.bin`，Shell Back/Forward 能再次恢复 child / nested 并保持历史索引 2。foreground、last-input 与隔离清理守卫通过。

实验同时发现，本 synthetic namespace 上 `ILIsEqual(childPidl, nestedPidl)` 返回 true，而两者 PIDL 长度分别为 622 与 1222 字节，后者是在前者两段 PIDL 后追加第三段 nested 项；parsing name 分别是 child / nested。该成功结果单独不足以证明导航位置精确相同。此前临时使用字节身份的结论已修正：Private Desktop host 改用“PIDL 长度相同且 `ILIsEqual`”后，history index 2、Back/Forward 都通过，无需字节级比较器。`CompareIDs` 对多段相对 PIDL 的递归修正也已加入 NativeProbe synthetic provider，但 `ILIsEqual` 对共同前缀仍返回 true，说明不能将两条比较路径混为一谈。

同一进程中的 ExplorerBrowser 对照仍对 wrapped nested `BrowseToIDList` / `BrowseToObject` 返回 `S_OK` 但保持 child；其物理路径对照正常。NativeProbe Release build 0 警告/错误；Private Desktop 总探针仍因 ExplorerBrowser 失败断言而 exit 1，自建宿主子探针通过，隔离守卫通过。日志：`src/.state/native-probe-watchdog/20261101-221422-5f60778f.stdout.log`。该结果证明自建 host 能承载此 namespace 的 nested view 与递归大小值，但仍只覆盖隔离 Shell view，尚未覆盖完整菜单/操作/拖放/通知/选择恢复或产品窗格接线。

### 2026-11-01：nested PIDL 精确历史身份复核

记录 PIDL 结构后，确认 child 为 622 字节（20 + 600 + terminator），nested 为 1222 字节（20 + 600 + 600 + terminator）。虽然 `ILIsEqual(child,nested)` 返回 true，加入长度相等条件后精确比较返回 false；同一条件用于 `ShellViewNavigationHistory` 后，无须 bytes comparer 即正确追加索引 2，Back/Forward 均恢复正确位置。递归 Size 值仍为 `10`，nested view 仍显示 `deep.bin`。NativeProbe Release build 0 警告/错误；总探针 exit 1 的唯一功能失败仍是 ExplorerBrowser registered namespace 深层导航对照，Private Desktop 前台/输入隔离守卫通过。日志：`src/.state/native-probe-watchdog/20261101-221900-a8236a7a.stdout.log`。这只修正隔离原型的精确历史身份判断，产品没有迁移或修改。

### 2026-11-01：修正多段 CompareIDs 后复测 ExplorerBrowser 深层导航

NativeProbe synthetic provider 的 `IShellFolder2::CompareIDs` 原先只比较传入相对 PIDL 的第一段。对 first item 相等的多段 PIDL，现在绑定该子文件夹并递归比较剩余路径段；若只有一侧还有尾段，则按路径前缀关系返回排序结果。单独构建 C++ adapter 后，ExplorerBrowser 原先停在 child 的问题消失：root→child→nested 均创建并激活目标 view，回到 child、Back 到 nested、Forward 到 child 均完成，row count、PIDL 与 TravelLog 核对通过。由此确认先前失败来自探针 namespace 对多段相对 PIDL 的 `CompareIDs` 实现不完整，不是 ExplorerBrowser 本身不支持嵌套导航。

Child view 初始也不含 Recursive Size 列；测试调用 `ShellColumnManager.Apply` 后，列可见且 nested 值为 `10`。TravelLog Back/Forward 返回时 Shell view 会创建默认列集；再应用 pane 的列快照后，列及值 `10` 恢复。因此原型要求宿主在每个新 Shell view 建立后恢复 pane 的列快照，不能依赖 Shell 自动跨 folder/history 携带应用自定义列。

双 provider 按需扫描超时的根因也已确认：NativeProbe 用固定 8 槽数组保留 Shell view/folder 配对，先前 root→child→nested、parent 与 TravelLog 往返累计占满槽位，创建 peer view 时记录 `view-hold-capacity-exhausted`，导致 pending 查询无法找到第二视图。扩至 32 槽后，同目录两个视图都建立 pending；销毁第一个 Browser 后其请求被取消，peer 请求正常完成并读到大小 `3`，总扫描计数只增加 1，缓存快照计数增加 2。说明同路径工作共享，但取消生命周期按视图区分。日志：`src/.state/native-probe-watchdog/20261101-222727-d71be987.stdout.log`；NativeProbe C# Release 与独立 C++ `/W4 /WX` 构建通过，Private Desktop 前台/last-input/隔离守卫通过。该槽位修正只属于隔离探针，不代表产品迁移或完成 UI/菜单/文件操作验收。

### 2026-11-01：ExplorerBrowser 使用共享视图状态协调器恢复递归大小列

把 NativeProbe 链接到产品共用的 `ShellViewStateRestore`，并用隔离 `IFolderView2` 目标实现模式、列、排序与分组写入。Child 新建 view 默认不含递归大小列；同一 `ViewState` 经共享恢复协调器应用后，列可见且 `nested` 行读回 `10`。Shell TravelLog Forward 同样新建无该列的 view，再调用相同协调器后列和值恢复。NativeProbe Release build 0 警告/错误；Private Desktop watchdog exit 0，前台/last-input/清理隔离守卫通过。日志：`src/.state/native-probe-watchdog/20261101-223215-980068ed.stdout.log`。这证明产品已有恢复协调器能够承载自定义列恢复，并验证了本机 ExplorerBrowser 下导航/TravelLog 的列快照需求；没有证明生产 `ExplorerBrowserHost` 对此注册 namespace 或物理目录的真实运行，递归扫描 provider、列标题交互、排序状态、刷新闪烁及用户可见 UI 仍不在本切片范围。产品架构与 releases 未修改。
### 2026-11-01：以生产 Loaded 优先级运行 ExplorerBrowser 列状态恢复

将上面的 child 与 TravelLog Forward 状态恢复调用排入 WPF `DispatcherPriority.Loaded`，由 DispatcherFrame 泵送执行，以对齐 `ExplorerBrowserHost.PublishNavigationLocation` 在 `OnNavigationComplete` 后调用 `ApplyViewState` 的调度优先级。两处均恢复成功：新 view 初始无递归列，恢复后列可见，nested 值为 `10`。NativeProbe Release build 0 警告/错误；Private Desktop watchdog exit 0，前台、last-input 与清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-223408-7e6b70d7.stdout.log`。本验证只复现 Loaded 调度等级，不加载 `ExplorerBrowserHost`，也没有真实标签快照捕获、导航代次退休门控或生产 UI 的重入/布局时序；仍属隔离 Shell 原型。

### 2026-11-01：递归大小排序恢复的实际行序复核

把递归大小列和升序排序键保存为 `ViewState`，分别在新建的 child `IShellView` 与 TravelLog Back 重建的 child view 上调用共享 `ShellViewStateRestore`。两处均返回成功、失败列表为空，列可见，`IFolderView2::GetSortColumns` 也读回了预期递归列键与升序方向；但实际行序仍是 `nested(10)|notification-size.bin(7)`，与升序应有的 `notification-size.bin|nested` 相反。NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台、last-input 和隔离清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-224247-6d432982.stdout.log`。

因此，状态恢复协调器成功及排序键读回，只能证明 Shell 接收/保留了排序状态，不能证明自定义递归值已触发行重排。原生菜单排序命令此前在“先由实际列标题建立排序上下文”的探针中能够排序；本次状态恢复路径没有建立该上下文。后续原型需要单独验证受支持的排序重应用方式，并以最终行序为判据。该缺口尚未进入产品，产品仍为 ExplorerBrowser。

### 2026-11-01：恢复后的 Shell 模式与实际列表样式核验

为解释标题排序为何未生效，探针同时读取 `IFolderView2::GetViewModeAndIconSize`、`SysListView32::LVM_GETVIEW` 和窗口 style。真实 ListView API 返回 Details（`LVM_GETVIEW=1`），Shell 也报告 mode 4、16 px；`GWL_STYLE & LVS_TYPEMASK` 仍编码为 `LVS_ICON`，但不能据此判定当前视图实际是图标模式。`SysHeader32` 仍报告不可见，强制显示并发送标题点击消息也没有改变目标行序。`IShellView::Refresh` 返回 `S_FALSE`。

随后把排序复核移到同级多个文件夹的 root view，避免用一个文件和一个文件夹比较而混入“文件夹优先”分组。递归值 `child=17`、`child-small=2`，恢复器后仍按 `child|child-small` 排列，排序键读回 `{7e1f84b7-c052-4e51-8a3c-869254a6b910}:1`、方向 `+1`。先临时将方向设为 `-1`，再执行原生 `sortascending`，状态读回方向 `+1`，但同一文件夹组仍是 `child(17)|child-small(2)`；文件组 `rename-complete(25)|root(4)|notification-created(3)` 也呈降序。当前证据表明排序状态与实际行序不一致，且不同项目组的排序效果也需分开核对；标题交互尚未成功建立，所以不能把原因单独归结为恢复时序或属性比较器。

NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台/last-input/隔离清理守卫通过。日志：`src/.state/native-probe-watchdog/20261101-225856-3f34a875.stdout.log`。下一步应在同一 view 对照有效原生列（如 Name/Size）的程序化排序与用户标题交互，再核对自定义列比较结果符号/方向映射；产品 ExplorerBrowser 未修改。

### 2026-11-01：原生标题槽与列快照索引逐槽对照

在相同 root view 逐一点击真实 header 槽 0–3，每次点击前重置递归 PropertyKey 并读取 `IFolderView2::GetSortColumns` 与项目行序。`IColumnManager::GetColumns` 的快照数组把递归列列在索引 3，但点击原生槽 1 后 Shell 读回排序键变成 `{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1`；点击槽 3 后才读回递归键 `{7e1f84b7-c052-4e51-8a3c-869254a6b910}:1`，方向为 `-1`。因此两个 API 的索引域不能直接互换，之前把快照索引传给 header 模拟点击并不能验证同一个列。当前 `IShellFolder2::GetDetailsOf(nullptr, ordinal)` 在该已包装的 folder 上返回 `E_INVALIDARG`，所以没有把槽 3 的显示标题猜成某个具体名称。

关键行为缺口仍存在：递归排序键读回正确时，同级 folder `child=17`、`child-small=2` 和同级 file `notification-created=3`、`root=4`、`rename-complete=25` 的行序仍未按递归值变化。普通 Shell `sortascending` 读回递归键方向 `+1`，但值顺序仍为 25、4、3；点击 header 槽 3 后方向变为 `-1`，行序仍保持原先名称顺序。槽 0/1/2 分别读回 Name、`{9e5e…}:1` 及 Type+Name 组合键。故目前已排除“误点快照索引即可解释排序失败”，但尚未证明该隔离适配器实际向 DefView 提供了用于排序的递归比较值；下一步应检查 header 的 `IShellFolder2::MapColumnToSCID`/`CompareIDs` 路径与排序键方向，并用 native Name/Size 作有效标题交互基线。Release NativeProbe build 0 警告/错误，Private Desktop watchdog exit 0，前台/输入/清理隔离守卫通过；结果日志：`src/.state/native-probe-watchdog/20261101-230454-6ad6363e.stdout.log`。运行后段仍有独立 self-hosted restore probe 的 COM 异常诊断，不影响本段 root view 的槽位记录，但该异常未调查。产品与 release 未改。

### 2026-11-01：追加递归列没有进入 Shell 比较器

NativeProbe 给 `SyntheticFolder::CompareIDs` 增加仅用于观测的逐列调用计数。在最后的原样复验中，Name 槽 0 的比较调用由 42 增至 48；Type 相关槽 2 由 0 增至 5；递归 PropertyKey 所在标题槽 3 虽让 `GetSortColumns` 读回该 key 与方向 `-1`，比较调用仍为 `0→0`，实际行序不变。一次临时原型把第 3 个 provider detail 也声明成递归大小，意外令 Shell 多出一个重复列（header 4→5、快照递归列索引 3→4），已撤回该显示变更；它说明必须区分 provider 原生 detail ordinal 与 `IColumnManager` 后加列索引，不能把重复标题当作修复。

与既有已通过的合成 `IShellView` 直接 provider 探针相比，当前失败路径使用 view-state 恢复后追加到 `IColumnManager` 的自定义列；Shell 接受排序状态，但不把该列点击路由进 provider `CompareIDs`。这把问题收敛到自定义列注册/列身份与 Shell comparator 的连接，而不是递归扫描值本身。下一步对照成功探针的原生列定义与当前追加列快照，验证能否在不重复列、不更换 ExplorerBrowser 产品架构的隔离 provider 中让递归 PropertyKey 对应到原生 detail ordinal，并要求比较器计数和实际行序同时通过。Release NativeProbe/C++ adapter build 通过，Private Desktop watchdog exit 0、输入/前台/清理守卫通过；日志：`src/.state/native-probe-watchdog/20261101-231123-8721fa6e.stdout.log`。产品与 release 未改。

### 2026-11-01：更正递归排序根因——Shell 原生排序键与数据 PropertyKey 不同

继续逐项读取真实标题文本、provider `IShellFolder2::GetDetailsOf` 与 `MapColumnToSCID` 后，确认第 1 列标题为 `Recursive Size`，provider 数据键为 `{7e1f84b7-c052-4e51-8a3c-869254a6b910}:1`；同一 view 的 `IColumnManager::GetColumns` 却将该标题关联到 Shell 原生排序键 `{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1`。先前探针强行把数据键 `7e1f…` 写入排序快照时，Shell 将其追加成无标题列，导致点击可见列不会进入对应 `CompareIDs`。因此上节“追加列没有进入比较器”的观察成立，但真正原因是快照使用了错误的 sort identity，而不是递归比较器不能工作。

隔离探针改用 live `IColumnManager` 中对应 `Recursive Size` 标题的 `{9e5e…}:1` 作为 `ViewState.SortColumns` 键，同时保留原 provider 数据键只用于 `GetDetailsEx`/值读取。共享 `ShellViewStateRestore` 随后不再添加重复列，header 标题稳定为 `Name|Recursive Size|Type`，快照列数为 3；root 行实际按递归值 `2|3|4|17|25` B 升序排列，`GetSortColumns` 读回该 Shell 键且 `order-matches-saved-sort=True`。Child view 的 `7|10` B 行序与 TravelLog Back 重建后均按快照排序；同一 NativeProbe 的原生列标题升/降序基线也分别为 `small.bin|child` 与 `child|small.bin`，比较器每方向调用一次。NativeProbe Release build 0 警告/错误，Private Desktop watchdog exit 0，前台/输入/清理守卫通过；隔离结果日志为 `src/.state/native-probe-watchdog/20261101-231548-ba6207c2.stdout.log` 和 `src/.state/native-probe-watchdog/20261101-231718-0faa1b56.stdout.log`。

这只是 `IShellView` synthetic namespace 原型结论，产品仍使用 ExplorerBrowser，未接入递归列。数据键与 Shell 排序键的稳定映射、语言/列布局变化、INI 持久化跨进程、Shell 菜单切换排序方向、刷新/通知、四窗格多视图及文件操作/拖放仍需验证；NativeProbe 当前以英文列标题识别示范列，不能复制该启发式到生产实现。更早的“按 7e 数据键排序未重排”结论由本节修正为错误排序键导致的重复列/路由失配。

### 2026-11-01：从实际 Shell 排序状态取键并复核跨进程 INI

移除 NativeProbe 根据英文标题名推断排序键的做法。探针现在对 live header 槽执行一次真实列交互，再读取 `IFolderView2::GetSortColumns`，并确认 Shell 返回的每个排序键都存在于同一 view 的 `IColumnManager` 显示列快照；数据 provider 的 `MapColumnToSCID` 仅作为独立诊断记录。root、child 与 TravelLog Back 的恢复状态仍读回 `{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1`，列快照保持三列且行序与该保存状态一致。Release NativeProbe 与 Private Desktop watchdog exit 0，前台/输入/隔离清理守卫通过；日志：`src/.state/native-probe-watchdog/20261101-231907-a27ed63c.stdout.log`。

SettingsSmoke 的跨进程 INI 用例现分别保存并核对 Shell sort/display key `{9e5e…}:1` 与 provider data/group key `{7e1f…}:1`；专用子进程往返命令 exit 0，列宽、排序方向、分组方向和路径均读回。补充比较器符号与原生菜单方向序列验证：2 B 对 17 B 的 `CompareIDs` 返回低字 `-1`，反向返回 `+1`；在已应用的升序状态下依次执行 Shell 背景菜单 `sortascending → sortdescending → sortascending`，`GetSortColumns` 方向和实际数值行序分别为 `+1/递增 → -1/递减 → +1/递增`。另观察到只调用 `SetSortColumns` 会更新方向读回但不立即重排行；先前“菜单方向不匹配”的实验先改了排序状态、后调用排序命令，因此把状态变更与实际重排混在一起，结论已撤回。Release NativeProbe build 0 警告/错误，Private Desktop watchdog exit 0，前台/输入/隔离清理守卫通过；复验日志：`src/.state/native-probe-watchdog/20261101-232658-4f6d1c6e.stdout.log`。以上只验证 synthetic `IShellView` 和通用会话 INI 往返；ExplorerBrowser 产品、用户物理目录、发布包均未改。
### 2026-11-01：递归列身份的标签快照与 INI 往返核对

生产 `OnNavigationPending` 在当前已导航标签上调用 `CaptureViewState`；捕获流程使用同一 Browser 与 view generation 读取模式、列布局、排序和分组，只有完整且仍当前的结果才替换缓存。`PaneControl`/`SessionStore` 以每标签 `ViewState` 保存这些字段。现有跨进程 `ColumnLayoutProcessSmoke` 使用与 NativeProbe provider 完全相同的递归列 PropertyKey（`7E1F84B7-C052-4E51-8A3C-869254A6B910:1`），核对列宽、排序方向、分组方向和路径从 INI 子进程读回；SettingsSmoke 全量运行 exit 0，view-state restore smoke 通过。此项是既有通用快照/INI代码对该 key 的持久化证据，不代表产品物理目录能生成此属性，或该 key 已在用户配置中提供列菜单入口。

### 2026-11-01：QDR 递归列状态恢复到隔离 IShellView

将已核实的 QDR 递归列状态形状（Shell sort/display key `{9e5e05ac-1936-4a75-94f7-4704b8b01923}:1`、provider 数据/分组 key `{7e1f84b7-c052-4e51-8a3c-869254a6b910}:1`、三列自定义顺序与宽度）构造成 `ViewState`，经生产 `ShellViewStateRestore` 恢复到 synthetic `IShellView`。`IColumnManager` 读回三列顺序/宽度，`IFolderView2::GetSortColumns` 读回自定义列及升序；原生行读取确认目录值 10、文件值 4，点击该列标题后方向在 `-1/+1` 间切换且行序反转。NativeProbe Release 构建 0 警告/错误；Private Desktop watchdog exit 0，前台窗口、最后输入和隔离清理守卫均通过；探针输出由本轮命令直接采集。

这是“QDR 字段语义/状态恢复”与“隔离 Shell 实际消费值及排序”的拼接验证：当时 NativeProbe 未直接调用 App 内部 QDR importer/exporter，也没有把 `.qdr` 文件解析后恢复到该视图。下一节已补上由生产 importer 加载文件的闭环，但输入仍是本项目 writer 生成的格式；真实 Q-Dir 文件互操作、产品 ExplorerBrowser 的列提供、跨窗格、多视图刷新和可见交互仍未验证，产品架构未改变。

### 2026-11-01：生产 QDR 文件解析到隔离视图闭环

NativeProbe 现在直接链接生产 `QdrViewWriter` 与 `QdrImporter` 源码，在临时目录夹具上写入 `.qdr` 文件，再由 `QdrImporter.Load` 解析实际文件；探针核对导入路径、三列 PROPERTYKEY/宽度、排序键和分组键后，将导入的 `ViewState` 传给生产 `ShellViewStateRestore`。Private Desktop 上 Shell 读回列与排序状态，递归值为 10/4，标题交互方向为 `-1/+1` 且行序反转；预期导入提示为“窗口位置及程序启动命令不导入”。NativeProbe Release build 0 警告/错误，探针 exit 0，前台/输入/清理守卫通过。

这证明本程序生成的合法 QDR 文件可经生产 importer 到达隔离 Shell 视图，补足上节“拼接验证”缺少真实文件解析的部分。文件来自当前 QdrViewWriter，不是从 Q-Dir 安装中取得的第三方 `.qdr`；不能据此证明 Q-Dir 导出文件兼容。产品 ExplorerBrowser 未接入递归列，用户可见交互、多窗格及生产刷新仍未验证。
### 2026-11-02：注册 namespace 递归列导航链复验

在 Private Desktop 重新运行现有 ExplorerBrowser 注册 namespace 完整探针，exit 0。根、child、nested 的实际导航及 `SBSP_PARENT`、Shell TravelLog Back/Forward 均完成；child 和 Forward 重建的新 view 初始未显示递归列时，生产 `ShellViewStateRestore` 重新应用列状态后，nested 行读回 10 B。self-hosted `IShellView` 同场探针也完成 root→child→nested，递归列可见且排序键读回；根目录和子目录 Shell 通知、原生 New Folder 行更新、IContextMenu、删除/改名、经 `IFileOperation` 的复制与移动均通过。临时 CLSID/NameSpace 注册已清理，Private Desktop 前台/输入隔离守卫通过。

同轮重跑的 `BrowseToObject` 包装物理文件夹探针再次显示：调用返回 `S_OK`，但活动 `IShellFolder` 身份不是 adapter，ExplorerBrowser 将位置绑定到普通文件系统夹。这条直达路径不能让逐行属性进入 ExplorerBrowser 的物理目录 view。注册的自定义 namespace 能提供递归列与完整层级导航，但这是另一种 host/provider 架构，不能据此认为已集成产品。

拖放诊断需谨慎解释：注册 namespace child 的直接 `IDropTarget::DragEnter/Drop` 调用接受 COPY；MOVE 在这段测试中报告 effect=0。探针源码明确说明直接调用没有执行 `DoDragDrop` 的源端完成阶段，因此此结果不能判为真实 Shell MOVE 失败。独立的 Shell `IFileOperation` IDataObject MOVE 已成功，源被移除且目标内容一致。仍需使用真实 OLE drag loop 才能判定拖放 MOVE；未验证可见 Explorer Dirs 窗格、真实物理路径、多窗格竞争和产品生命周期。

本次仅复跑隔离探针，没有修改产品或发布版。结果日志位于用户临时目录 `explorer-dirs-registered-namespace-20261102.log` 与 `explorer-dirs-recursive-probe-20261102.log`。接下来优先评估注册 namespace 方案的便携部署和物理文件夹转接成本，再决定是否推进产品级 host 原型；ExplorerBrowser `BrowseToObject` 不能作为这层转接的替代方案。

### 2026-11-02：多视图探针的物理根目录覆盖边界

为评估进入多窗格阶段的差距，检查 NativeProbe 自定义文件夹 provider 的实例化链：`SetSyntheticSortBackingPath` 写入全局 `g_syntheticSortBackingPath`；`DllGetClassObject` 仅接受全局 `g_syntheticFolderClassId`；Shell 根文件夹 `Initialize` 从该全局路径绑定文件系统对象。子文件夹实例随后继承父实例 `_backingPath`，所以已有多视图测试能验证多个 view 共用同一物理树时的缓存/列行为，但不能证明两个不同物理根目录能并行映射。

要验证真实多窗格路线，下一步原型需给 namespace 根提供稳定且独立的物理路径身份（而非临时改写全局路径），并同时打开两个不同临时根目录的 ExplorerBrowser，交叉导航后分别核对 parsing name、枚举内容、递归列值及会话恢复。当前 Shell 注册探针每次只注册一个临时 CLSID；逐窗格注册 CLSID 可以做诊断，却不是合适的产品方案。产品架构与 release 未修改。

### 2026-11-02：双根 provider 实例与递归值隔离 smoke

为验证 per-root provider 实例是否可独立承载数据，在 NativeProbe 增加按 CLSID 配置 backing path 的隔离入口。一个无窗口 smoke 同时创建并保留两个 `IShellFolder2` 根实例，再依次枚举它们；夹具在两根中都放置同名 `shared-folder`，其内容分别为 13 B 和 29 B，并各自有独有文件名。两根只枚举到自己的独有文件，递归大小列也分别读回 13 B/29 B，没有共享路径或缓存串值。NativeProbe Release 与 C++ adapter Release 构建均为 0 警告/错误；双根 smoke、原有 Shell-folder-tree smoke、按需递归大小列 smoke 均通过。

该切片证明 provider 可在同进程同时持有不同路径的根对象，但根身份目前由不同 CLSID 指定；未创建 ExplorerBrowser/IShellView 窗口，未验证 Shell 类注册、两窗格实际导航、Back/Forward、跨重建状态、文件操作或绿色部署。因此它不是可直接采用的产品身份设计。下一步应验证单一稳定 provider CLSID 下，是否能把物理根路径编码进 Shell 绝对 PIDL/解析名，并保留多标签路径身份；再决定是否进入 ExplorerBrowser 双窗格宿主验证。

### 2026-11-02：单 CLSID 的路径分段 PIDL 根身份原型

新增 NativeProbe 的 `DPRT` 路径身份 PIDL 编码：物理根路径分成每段最多 1,024 个 UTF-16 字符的多个 SHITEMID，`SyntheticSortFolder.Initialize` 优先从绝对 PIDL 解码该根路径；旧探针不带该 marker 时继续走既有 backing path。无窗口探针创建两个相同 CLSID 的 Shell 文件夹对象，各自以不同路径 PIDL 初始化，并在两个对象同时存活时核对 `GetCurFolder` 保留各自 PIDL、独有文件不串、同名目录递归大小分别为 13 B/29 B。另验证 1,100 字符路径跨多个 PIDL 段编码/解码一致。NativeProbe 与 C++ adapter Release 构建均为 0 警告/错误；单 CLSID 双根、双 CLSID 双根、Shell-folder-tree、按需递归列和 DLL unload 合同 smoke 均通过。

这只证明 provider 的 `IPersistFolder2.Initialize` 可从路径 marker PIDL 取得互相独立的物理根。测试直接构造 PIDL 并创建 COM 文件夹对象，没有把 PIDL 挂在已注册 namespace 的根之后，未实现 `ParseDisplayName`/`BindToObject` 将产品路径转换成该 marker，也未验证 Shell 解析名、ExplorerBrowser 导航/历史、跨进程恢复、可见列或系统操作菜单。下一步先在无窗口探针验证一个已注册稳定 CLSID 的 root → path-marker `ParseDisplayName`/`BindToObject`/parsing-name 往返，再考虑 ExplorerBrowser 双窗格宿主。

### 2026-11-02：路径根 PIDL 的文件夹合同往返

扩展隔离 adapter：`ParseDisplayName` 收到绝对文件系统目录路径时，将路径编码为相对 marker PIDL；`GetDisplayNameOf` 返回同一物理路径；`BindToObject` 把 marker 与当前根 PIDL 组合，并以新绝对 PIDL 初始化独立 folder 对象。NativeProbe 先初始化普通根对象指向目录 A，再通过该合同解析目录 B，核对显示名重新解析后 PIDL 字节一致，绑定后的对象只枚举 B 的独有文件，递归值为 29 B（与 A 的 13 B 条目不串）。Release build 0 警告/错误；双根 PIDL、folder-tree、按需递归列及 DLL unload 合同 smoke 通过。

此合同测试直接调用 adapter 的 `IShellFolder`，父 PIDL 是普通文件系统 PIDL，尚未通过 Windows 注册的 namespace CLSID 调用 `SHParseDisplayName` / `SHBindToObject`，也没有证明 Shell 可为组合 PIDL 生成稳定的 Desktop parsing name。因此它只验证 adapter 层的 Parse/Bind/name 逻辑，不代表注册 namespace 已可承载多路径根，更不涉及 ExplorerBrowser 或产品视图。

### 2026-11-02：注册 namespace 的根激活与 parsing-name 往返

新增 `--registered-multi-root-pidl-smoke`，使用本次生成的临时 CLSID 注册 adapter 的 `InprocServer32` 与 Desktop `NameSpace`，通过 `SHParseDisplayName("::{CLSID}")` 自然激活根，再由该根解析物理目录 B 并用 `SHBindToObject` 绑定子根。第一次尝试返回普通 `C:\...` 文件系统名，重解析得到不同大小的 PIDL；原因是 `GetDisplayNameOf(SHGDN_FORPARSING)` 直接返回物理路径，Shell 因而丢弃了 namespace 身份。

修正为 `::{CLSID}\edpath:<UTF-16 hex>` parsing name：相对显示仍用物理路径，Shell parsing name 则用稳定 CLSID 前缀与无分隔符路径 token；`ParseDisplayName` 解码 token 后校验物理目录并生成分段 PIDL。注册探针现确认绝对 parsing name 经 `SHGetNameFromIDList` 与 `SHParseDisplayName` 往返后 PIDL 字节一致，路径 B 绑定后只看到 B 的独有行及 29 B 递归值，目录 A 的根对象仍同时存活且保持 13 B；文件夹内显示名为目录叶名称，不暴露 token。此过程临时注册项在 finally 中清理，没有创建 Shell view/window。

这是自然 namespace 激活、路径 Parse/Bind 与 PIDL 名称 round-trip 的成功证据，但不是产品级完成：还需验证不同 child/path 顺序和重启后的稳定性、导航历史、真实 ExplorerBrowser 双窗格、列状态恢复、Shell 文件操作/通知及绿色部署；产品仍未采用自定义视图。

### 2026-11-02：同 CLSID 路径根的双 IShellView 操作验证

新增 Private Desktop 场景 `--synthetic-shell-path-identity-two-view-private-desktop`。它通过同一临时注册 CLSID 下两个独立 `edpath` parsing name 创建 Pane1/Pane2 的 Shell view，并逐项核实两视图只枚举自己的文件。随后从一视图向另一视图执行 Shell MOVE 与 COPY：源/目标内容在对应视图更新，来源/目标文件状态符合操作结果；private desktop 守卫确认前台窗口、最后输入时间未变化，隔离桌面及夹具清理通过。NativeProbe Release build 0 警告/错误。

这证明路径 PIDL 可以驱动两个并存的 `IShellView` 实例和 Shell 文件操作通知。没有验证每个路径根的 Back/Forward Travel Log、跨重建或进程重启后的历史恢复、Q-Dir/产品列状态保存、完整 ExplorerBrowser 控件、多标签会话或用户可见菜单/焦点；这是隔离迁移实验，不是产品接线或可发布功能。

### 2026-11-02：ExplorerBrowser 路径根导航、TravelLog 与列状态重放

扩展已注册 namespace 的真实 `IExplorerBrowser` Private Desktop 探针，新增独立 peer 目录 B（含 `peer-only.txt` 和 29 B `shared-folder`），通过稳定 CLSID 的 `edpath` parsing name 导航到 B。`NavigateAndWaitForFolder` 以 Shell 当前 view 的 `IPersistFolder2.GetCurFolder` PIDL 与目标逐字节比较；B 两行可见，递归大小读为 29。随后以 `SBSP_NAVIGATEBACK` 返回 A，再以 `SBSP_NAVIGATEFORWARD` 返回 B，两个目标 PIDL 均匹配。

Shell TravelLog 前进后没有自动恢复 B 的自定义列（`shell-restored-column=False`）。隔离宿主重放先前捕获的列集合后，B 的递归值再次读为 29；探针最后回到 A，随后原有递归扫描销毁、菜单/文件操作、通知和卸载断言均通过。Release build 0 警告/错误；Private Desktop 前台及输入守卫通过。

这验证单个 ExplorerBrowser 实例在多个路径根间导航、Shell 历史及宿主重放列状态；不证明两个 ExplorerBrowser 同时承载不同根、应用 SessionStore/TabState 的真实跨标签保存、重启后恢复、持久 ViewStateStore 接线或用户可见验收。产品仍使用当前 ExplorerBrowser 物理文件夹视图，未接入自定义递归列 provider。

### 2026-11-02：两个 ExplorerBrowser 并存承载独立路径根

在上一探针中再创建第二个真实 `IExplorerBrowser`，仍使用同一临时 CLSID，但分别让第一个浏览器停留在根 A，第二个浏览器导航到 peer 根 B。两个窗口同时存在时，A 的 `GetCurFolder` 仍与 A 根 PIDL 完全一致，A 行数及递归大小相较创建第二浏览器前保持不变（本轮为 8 行/88 B）；B 只显示自己的 2 行，`shared-folder` 为 29 B。销毁 B 后，A 的 PIDL、行数和递归值仍维持基线。

Release NativeProbe build 0 警告/错误，完整 `--explorerbrowser-registered-namespace-private-desktop` 探针退出码 0，Private Desktop 守卫通过。此前暴露的固定行数/固定大小断言已改为保存运行时基线，避免把探针前已完成的文件操作误判成串根。

这证明隔离双 ExplorerBrowser 实例可同时承载同 CLSID 下独立物理根，并且销毁一个不会改变另一个。仍未验证产品 Pane/Tab 多实例接线、每 pane 独立 session INI、跨重启列/历史恢复、ExplorerBrowser 状态流的产品持久化或可见体验；产品未改。

### 2026-11-02：递归大小列的按需同步/异步主机对照

复跑无窗口按需合同：Shell 文件夹枚举阶段不扫描；首次读取递归大小才触发扫描；重复读取命中缓存；比较器不额外扫描。异步合同另外确认首次属性读取先返回空值、后台完成后发布 10 B/2 文件/1 子目录快照；视图释放会取消尚未执行的请求。以 20 ms 人工扫描延迟复测 8 个请求的按视图批处理，读取共用同一批扫描、并发 worker 峰值为 2、重复读取仍走缓存，隔离合同通过。

再以真实 Private Desktop WPF 宿主对比 100 个文件夹：同步路径在列表完整读取前已启动 22 次扫描，扫描累计约 14.6 ms；读完列表后累计 101 次扫描、约 60.5 ms。异步宿主同样完成 101 次扫描，累计扫描约 83 ms，worker 峰值为 2，并由 Shell 通知重新读取列值；该运行中完整读取前后 Private Bytes 增加约 1.47 MB、Working Set 增加约 2.04 MB。两次是小型本机夹具的一次观测，耗时和内存差不能外推到冷盘、网络盘或大目录。

本次结果限定了“按需”的含义：未显示/未请求该列时不扫描；列加入视图后，Shell 会读取几乎所有可见文件夹的值，因此后台方式改善 UI 等待和并发上限，不会减少这一列所需的总目录遍历。异步首读暂时为空、待通知回填；在产品中仍需评估刷新/切目录/关闭标签时的取消与旧结果门控。产品物理目录尚无自定义逐行列 provider，接线仍依赖已评审的注册 Shell namespace 路线；此次没有改产品代码、会话配置或发布版。

随后把夹具扩大到 500 个同级目录重复宿主对照。同步模式完成 501 次扫描，累计扫描时间约 296 ms，列表读取前已扫描 22 项；异步模式同样完成 501 次扫描，累计 worker 时间约 508 ms、最高并发 2，首次读取返回后由一次 Shell 通知重新读取列值，view 枚举增量为 1。异步完整读取前后 Private Bytes 增加约 5.51 MB，Working Set 增加约 5.41 MB。由于异步有并发且回填会再读取属性，回调数为 1,004，而同步为 502；这些计数不代表磁盘扫描数量，实际扫描数两边均是 501。结果进一步支持只在用户启用该列后扫描，并将扫描放后台；但首次值会空白直至回填，内存/时间数据仅为一次合成夹具观测。

### 2026-11-02：四个独立根的过期扫描拒绝与 Shell 通知竞态

四个不同路径根的隔离 WPF 宿主中，直接失效注入已通过：目标 `folder-000` 在扫描中改变 38 B，旧结果未发布，重试读回递归总值 47 B（其余 9 个文件各 1 B）；4 视图选区/焦点保持，36 次基线扫描加 1 次目标重试，无队列拒绝。首次失败来自断言算术错误（期待 38 B，实际应为 47 B），修正后该直接失效场景通过。

增加 NativeProbe 专用的提交闸门后，真实 Shell 通知竞态现可确定性验证：闸门只暂停指定 Pane1 `folder-000` 在扫描和身份检查完成之后、写入快照之前的工作线程；随后修改一个文件并由同一 Private Desktop 发送 `SHCNE_UPDATEITEM`/`SHCNE_UPDATEDIR`。`SFVM_FSNOTIFY` 收到的 PIDL 精确解析为目标文件夹，两类通知命中同一个活动 reservation；释放闸门后，旧值的缓存提交被拒绝，刷新重扫读回 47 B。四个不同根的视图全部收敛，选区/焦点保持，无队列拒绝；扫描总数 37（36 个完整结果加目标旧扫描），view 枚举增量为 5。Release C++ adapter `/W4 /WX` 与 NativeProbe 构建均通过，Private Desktop 前台/输入及清理守卫通过。此闸门仅是 NativeProbe 的时序控制，不进入产品；已证明隔离 provider 的通知失效与旧结果拒绝链，不代表产品 ExplorerBrowser 已接线，也不证明任意文件系统/第三方 Shell provider 的通知覆盖面。

### 2026-11-02：注册路径 PIDL 跨进程恢复

扩展 `--registered-multi-root-pidl-smoke`，在父进程从临时注册 namespace 取得绝对 parsing name 并完成 A/B 根绑定后，另起全新 NativeProbe 进程重新加载 adapter、设置同一 CLSID，并将默认 backing fallback 指向 A；随后仅凭保存的 B parsing name 调用 `SHParseDisplayName`、绑定文件夹、读回 B 独有行及 29 B 递归值，且重新生成相同的 absolute parsing name。结果通过。首次子进程试跑因未初始化它自己的 adapter class/path 状态而在 Shell parse 阶段返回 `E_UNEXPECTED`；补足子进程启动所需类注册上下文后，通过把 fallback 设为不同根 A，排除了“默认路径碰巧就是目标 B”的假阳性。

NativeProbe Release build 0 警告/错误，探针 exit 0、`git diff --check` 通过；未创建 Shell view/window，临时类和 NameSpace 注册在父探针结束时清理。本结果证明绝对 parsing name 中的 path marker 能跨进程恢复到正确文件夹，即使新进程默认路径不同；不证明 ExplorerDirs 产品会自动初始化 Shell provider、产品 CLSID/注册生命周期、portable 无注册表部署、ExplorerBrowser 多窗格/标签 SessionStore 接线、可见菜单/操作/拖放或重启 UI 验收。产品与 release 未改。

本轮将注册路径探针再接到应用共用的 `SessionStore` 与 `ShellLocationIdentity`。首次失败不是 session 解析：`SessionStore.Render/ParseComplete` 能原样保留 parsing name，`SHParseDisplayName` 也成功，但 `ShellLocationIdentity.TryGetFolderPidl` 通过 `IShellItem.GetAttributes` 拒绝 provider 路径，返回 `E_INVALIDARG`。分项查询确认 folder/filesystem 两种属性请求都失败。根因是测试 adapter 的 `GetAttributesOf` 只识别普通合成文件行，没有识别路径身份 `DPRT` PIDL；因此 ShellItem 无法判断该项是文件系统文件夹。

修复 NativeProbe adapter：当 `GetAttributesOf` 收到单个有效路径身份 PIDL 时，从 marker 解出路径并向 Windows Shell 解析该物理目录，再按请求掩码返回文件夹/文件系统属性。之后应用的 `ShellLocationIdentity.TryGetFolderPidl` 和 `TryGetPersistedIdentity` 均通过；session INI round-trip 保持同一 parsing name，跨进程测试把 fallback 置为 A 后仍读回 B 的专属文件及 29 B。注册路径探针、双根 PIDL、folder-tree 文件操作/缓存失效合同、按需列 smoke 均通过；NativeProbe Release 0 警告/错误，C++ adapter `/W4 /WX` 构建通过。

该属性补充仅在 NativeProbe synthetic adapter 中，未更改 Explorer Dirs 产品代码。它补强了“会话路径可保存且通用身份帮助类能解析该原型 provider”的证据；仍未接入产品 CLSID/启动注册生命周期、ExplorerBrowserHost 多标签 session 恢复、可见 UI 或 portable 部署。

### 2026-11-02：产品 ExplorerBrowserHost 消费注册 Shell 路径身份

新增隐藏 HostProbe：临时创建两个不同物理根目录，注册随机 CLSID 的 synthetic Shell-folder adapter，并把它的默认 backing 路径设为 A；再通过产品实际 `ExplorerBrowserHost` 导航到包含路径 marker 的 B parsing name。生产 host 的 `CurrentPath` 保持 B 身份、`ShellLocationIdentity` 将其判为非普通文件系统路径，`ReadItemCounts` 读到 B 独有的 2 项，而不是 fallback A 的 decoy 项。WPF/HwndSource 父窗口没有 `WS_VISIBLE`，运行结果也确认不可见；测试结束清理临时注册与目录。

`ExplorerDirs.HostProbe` Release build 通过（0 警告/错误），`--shell-provider-location` exit 0。输出：`product-host-navigation=True; is-filesystem=False; rows=2; target=session-root; fallback=default-root; parent-visible=False`。因此正式 ExplorerBrowserHost 的导航/枚举接口能消费原型 parsing name；仍未验证产品 provider 注册/卸载和便携部署、应用 SessionStore 跨重启接线、可配置递归列在产品 host 中显示/排序、异步扫描回填、菜单/拖放/通知的产品生命周期或可见 UI。本次仅扩展隔离 HostProbe，没有修改产品 host 或发布版。

### 2026-11-02：产品 Host 的递归列排序应用时机

继续扩展隐藏 HostProbe，将 5 B 与 29 B 两个目录通过真实产品 `ExplorerBrowserHost` 浏览。首次枚举后，`CaptureViewState` 已读回升序 `{9e5e…}:1/+1`，但实际行序仍为 Shell 初始枚举序 `folder-large → folder-small`。provider 的 `MapColumnToSCID(1)` 返回递归数据键 `{7e1f…}:1`，直接调用 `CompareIDs(1, large, small)` 返回 `+1`；`GetDetailsEx` 能读回两值，provider 的列值与比较器合同正确。

对当前 `IShellView` 调用一次 `Refresh` 后，HRESULT 为 `S_OK`，比较器调用增量为 1，行序变成 `folder-small → folder-large`。随后对已处于升序状态的 view 执行 `sortascending`，比较器没有增加；先执行 `sortdescending` 再执行 `sortascending` 时，两个方向变化均各调用一次比较器，行序分别为大→小和小→大。真实 `SysHeader32` 标题点击也能切换排序键方向并调用比较器。

同一产品 Host 再以普通文件系统临时目录做标准 `PKEY_Size` 对照：文件名顺序 `a-large.bin → z-small.bin` 与大小升序相反；首次捕获时 Shell 已返回 `5 字节|29 字节` 和 `z-small.bin → a-large.bin`，即排序在 `IShellView.Refresh` 前已经正确应用。显式刷新后结果不变。两次重复隐藏运行一致。因此目前的初始重排差异只在注册 synthetic namespace 的产品 Host 观察到，不能推广为普通文件夹排序恢复缺陷。

递归列 provider 的 `IShellFolder2.GetDetailsOf` 直接返回标题 `Recursive Size` 和行文本 `29|5`，`GetDetailsEx` 也返回对应数值；这证明 Shell 列显示所需的 provider 文本/属性回调合同可读，但不代替用户可见像素验收。与此同时，应用 `CaptureFileViewExportSnapshot` 的 `IPropertyStore` 快速属性路径仍为空值。列回调显示、属性存储读取和应用导出是独立数据消费路径；导出空值不能用来推断屏幕列为空。

因此前一条“排序命令没有进入 provider 比较器”的归因需更正：provider 列身份映射和排序命令路由都可工作；相同方向的命令是无操作。当前原型缺口是 synthetic namespace Host 的初始排序恢复：`SetSortColumns` 状态读回成功，但它本身没有把初始枚举行序重排；该 Host 的 `IShellView.Refresh` 可以完成重排。是否在未来产品接入时为虚拟位置使用一次受限刷新，仍需评估可见闪烁和执行时机。`ExplorerDirs.HostProbe` Release 构建 0 警告/错误；包含 synthetic 与物理目录对照的 `--shell-provider-location` 连续两次 exit 0，父窗口不可见，临时 CLSID/NameSpace 与夹具由 finally 清理。产品代码及 release 未改。

### 2026-11-02：产品导出回读 Shell 自定义列文本

将前述 HostProbe 的失败断言保留为回归门槛：产品 `CaptureFileViewExportSnapshot` 原先导出递归大小列为空字符串，而当前 Shell 视图的 `IShellFolder2.GetDetailsOf` 能提供可见表头与每行文本。导出现在只对没有 `PROPERTYKEY` 显示描述的列启用受限回退：读取当前 SysListView32 表头及列视觉顺序，将可见自定义表头唯一映射到 `GetDetailsOf(NULL, ordinal)`，再依 `IFolderView2` 当前行序逐项读取对应 STRRET 文本。普通有属性描述的列仍由既有快速 `IPropertyStore` 路径读取；快速筛选打开时仍走原捕获路径，避免把视图背后的未过滤条目当成可见行。

HostProbe Release build 0 警告/错误；`--shell-provider-location` exit 0，产品导出列为 `Recursive Size|名称`、值为 `5|29`，与 Shell 行序 `folder-small|folder-large` 对应；普通文件系统 `PKEY_Size` 刷新前排序对照通过。此证据覆盖隐藏 ExplorerBrowserHost 与合成 provider，不包含可见 UI 导出验收、打印、快速筛选下的派生列值、普通第三方 Shell provider 或大目录耗时。若 Shell 视图没有对应原生详细列表表头、可见列与 Shell ordinal 不能唯一匹配，捕获退回既有属性导出路径，不猜测列位置。此改动仅补齐导出消费路径，并不代表逐行递归大小列已在产品普通物理目录中启用。

### 2026-11-02：初始递归列排序的轻量重排接口评估

在产品 HostProbe 的合成注册 namespace 上尝试用 `IShellFolderView::Rearrange` 替代完整刷新：以 provider `GetDetailsOf` ordinal 调用时接口返回 `S_OK`，但行序仍是 `folder-large → folder-small`，比较器也未被触发；把参数换成 `IColumnManager` 可见列索引后，后续 `IFolderView2.GetItem` 读行出现 `E_FAIL`，探针无法继续读取属性。撤回了产品宿主接线，避免将不稳定的旧接口用于用户视图。

恢复基线 HostProbe 后可重复确认：初始恢复键读回升序，但刷新前仍为大→小；完整 `IShellView.Refresh` 返回 `S_OK`、触发一次 provider 比较器并重排为小→大。普通文件系统 `PKEY_Size` 初次读行前已经正确升序。故当前差异仍限定在合成 provider 产品 Host。微软文档指出 `Rearrange` 的排序规则由文件夹定义，系统仅保证低 16 位传列规则，并建议调用方改用 `GetSortColumns`；该 API 不适合作为 ExplorerBrowser 产品宿主的通用修复。当前仅把它记录为原型失败结果，后续应验证 provider 自己的初始排序合同或评估受限完整刷新，不改变普通目录路径。

### 2026-11-02：注册 provider 初始递归排序不依赖完整刷新

继续验证 Shell 自己的 canonical 排序命令：在 Shell 已返回初始行后，执行一次相反方向命令，再隔一个 Dispatcher tick 执行会话保存的方向。真实产品 `ExplorerBrowserHost` 在 synthetic 注册 provider 上因此触发递归列比较器，并在完整 `IShellView.Refresh` 前收敛到目标顺序。宿主仅在非文件系统虚拟位置、无分组、恰有一个已保存排序键且该键对应可见未解析列时安排这两次命令；物理目录、标准已解析列和分组视图不进入此路径。排序命令通过现有 `IContextMenu` canonical verb 路径发送，不弹出右键菜单。

HostProbe 为保存升序及降序分别构造初始会话，均验证首次完整刷新前行序与目标方向一致；完整探针仍通过导出、Shell 列值、菜单排序、Header 交互及普通文件系统 `PKEY_Size` 控制。Release build 0 警告/错误，两种方向的隐藏 `--shell-provider-location` 运行均 exit 0，父 HWND 不可见。没有可见 UI 证据；两条 Shell 命令分两个 100 ms Dispatcher tick 执行，是否出现短暂中间行序尚待 UI 检查。此为产品 Host 的注册 synthetic provider 原型，不表示递归列已部署到普通物理目录或 release。

将第二次命令间隔试降至 16 ms 时，HostProbe 在 Shell 重新排列期间从 `IShellFolder2.GetDetailsEx` 读取项目返回 `E_FAIL`；这说明仅缩短延迟会与 Shell 行枚举竞争。恢复为已通过的 100 ms。当前没有可靠的“Shell 已完成此次重排”通知，因此不再缩短间隔；该失败发生于隔离隐藏 HostProbe，不是可见 UI 或产品文件操作的验收结果。

### 2026-11-02：产品 Host 原型混合文件与文件夹的递归大小排序

扩展注册 synthetic provider HostProbe，在同一目录加入 5 B 与 29 B 两个目录、12 B 普通文件。生产 `ExplorerBrowserHost` 的可见未解析递归列升序在完整 view 刷新前得到 `folder-small → file-middle.bin → folder-large`，降序反转；导出列值按相同 Shell 行序读回 `5 → 12 → 29`，普通文件系统 `PKEY_Size` 控制仍按 5 B 与 29 B 文件排序通过。Release HostProbe 构建 0 警告/错误，升序与降序隐藏运行均通过，父窗口不可见。

此结果仅证明 registered synthetic provider 的产品 Host 能在一列中比较目录递归值与普通文件原生字节值，不代表普通物理目录已获得派生列 provider、扫描更新或发布功能。Q-Dir 官方 FAQ/功能页说明文件夹大小默认忽略排序以避免性能问题，并提供显式选项；本次 HostProbe 使用递归大小排序，尚未在产品 Host 上验证该默认/可选策略，故不能称为 Q-Dir 排序语义已对齐。[FAQ 63](https://softwareok.com/?faq=63&seite=faq-Q-DIR) · [文件与文件夹大小列](https://www.softwareok.com/?seite=freeware%2Fq-dir%2FFile_and_folder_size_Column_with_percentage)

### 2026-11-02：Host 原型验证忽略文件夹递归大小排序

扩展隐藏 HostProbe 的测试专用开关：文件夹仍显示递归大小（5 B、29 B），但比较时将文件夹大小视为相等；普通文件继续使用原生大小值（12 B）。升序结果为 `folder-large → folder-small → file-middle.bin`，降序为 `file-middle.bin → folder-large → folder-small`；两文件夹直接比较结果相等。正常递归大小排序的升序/降序回归也通过，普通文件系统 `PKEY_Size` 排序控制通过。

HostProbe Release 构建通过，四种隐藏合成视图场景（正常/忽略文件夹大小 × 升序/降序）均退出码 0。该开关只存在于 NativeProbe 合成 provider 测试适配器，不是产品选项；验证了产品 `ExplorerBrowserHost` 消费这种排序语义的原型，但不证明普通磁盘目录自定义列可用、配置可保存或用户可见 UI 行为。没有启动产品窗口，也没有更新 release。Q-Dir 的官方说明称文件夹大小因导航性能问题默认不参与排序，并提供“忽略文件夹大小排序”选项。[Q-Dir FAQ 63](https://softwareok.com/?faq=63&seite=faq-Q-DIR) · [Q-Dir 文件夹大小列说明](https://www.softwareok.com/?seite=freeware%2Fq-dir%2FFile_and_folder_size_Column_with_percentage)

### 2026-11-02：产品 Host 的注册路径根进入子目录并回退

在递归列 HostProbe 完成根目录列/排序断言后，记录父级可见列，调用生产 `ExplorerBrowserHost.Navigate` 进入同一 provider 下的物理子目录，再调用生产 `GoBack` 返回父级。正常递归排序与“文件夹大小忽略排序”的反向场景均通过：子目录视图读到 `payload.bin` 1 行、5 B；Back 回到父目录后恢复 3 行、5/12/29 B 递归值集合及导航前的可见列集合。普通 filesystem `PKEY_Size` 排序对照也通过；两个测试父 HWND 均不可见，未启动产品窗口。

首次运行的超时是 HostProbe 的大小写敏感字符串断言：Shell 将 CLSID 规范化成大写，但 `CurrentPath`、新 view HWND 和 1 行子目录枚举均已成功。将路径身份比较改为 `OrdinalIgnoreCase` 后，两次隐藏 HostProbe 通过。结果证明产品 Host 可通过已注册路径身份进入一个物理子目录并用 TravelLog 回到父级；不代表鼠标双击输入、所有层级/特殊目录、父子目录各自独立视图设置、普通文件夹直接使用派生列、便携注册生命周期或可见 UI 已通过。产品与 release 未修改。

### 2026-11-02：递归列和注册路径的生产 INI 会话往返

扩展同一 HostProbe：以当前稳定 provider CLSID 的 `edpath` parsing name、Details 模式、递归大小排序键、递归/名称列及列宽构造生产 `SessionState`；通过生产 `SessionStore.Save` 写入临时 `ExplorerDirs.ini`，再用新的 `SessionStore.Load` 读回，并以读回的 Tab 路径与 `ViewState` 创建产品 `ExplorerBrowserHost`。路径身份保持，递归列排序键/方向、显示列键/宽度保持。正常递归排序升序与忽略文件夹大小排序降序两轮均通过；子目录导航/Back、父列恢复及普通文件系统排序控制同时通过，临时 profile/注册项随探针清理。

此为同一进程内真实 INI 文件保存、重载和产品 Host 重建，不是退出进程后重新启动或绿色无注册表部署验证；临时 provider CLSID 仍需外部注册才能解析。它证明应用 session 格式能携带原型路径与视图状态，不代表产品启动时会安装/注册 provider、每窗格多标签会话恢复、真实普通目录自定义列或可见体验已完成。

### 2026-11-02：递归列 INI 在独立进程中的 Host 恢复

将上述往返扩展为父子进程：父进程写入临时 `ExplorerDirs.ini` 并注册一次性 HKCU Shell CLSID/NameSpace；新的 HostProbe 子进程重新加载 NativeAdapter、从 INI 读取 parsing name/`ViewState`，再由 Shell 按现存注册激活 provider，创建不可见的生产 `ExplorerBrowserHost`。正常递归升序恢复为 `folder-small → file-middle.bin → folder-large`；启用忽略文件夹大小排序时，降序恢复为 `file-middle.bin → folder-large → folder-small`。两种场景均读到 3 行、递归值 5/12/29 B 和递归列，子进程前台 HWND 不变且测试窗口不可见；父进程随后完成父子目录 Back 与物理文件系统排序对照。HostProbe Release build 0 警告/错误，两种完整运行 exit 0。

首轮失败是子进程在行数刚变为 3 时立即检查顺序，Shell 尚未完成产品 Host 的延迟排序恢复；主进程既有探针已按完整目标顺序等待，本次让子进程使用同一条件等待后通过。该实验真实跨越进程边界并重新通过 Windows Shell 注册激活，但仍由父进程预先建立临时 HKCU 注册，不能证明绿色部署/无注册表激活、产品 App 重启、SessionStore 自动启动路由、所有 pane/tab 配置、普通物理目录派生列或可见交互。产品与 release 未改。

### 2026-11-02：绿色部署注册依赖门槛探针

新增一次性 HostProbe 开关，在唯一随机 CLSID 下保留临时 `HKCU\Software\Classes\CLSID` COM 类注册，但跳过 `Explorer\Desktop\NameSpace` 注册。父进程写 INI 后启动独立子进程；子进程能加载 NativeAdapter 和 SessionStore，但生产 `ExplorerBrowserHost` 在 20 秒内未能恢复 `::{CLSID}\edpath:...` 路径，探针失败并由父进程清除临时 CLSID 与夹具。相同 Release 二进制以完整临时 CLSID + Desktop NameSpace 注册运行的正向对照退出码为 0。

结论限定为：当前以 Shell parsing name 跨进程保存/恢复的原型需要 Desktop NameSpace 注册；单独的每用户 COM 类注册不足以恢复该路径。这并未证明任何无注册表方案都不可行。若继续追求绿色部署，需要另做宿主原型：应用进程直接实例化 provider 的 `IShellFolder` 并通过 ExplorerBrowser 对象导航接口挂载，同时设计应用自有的可持久化位置标识；该路径会改变 Host 的导航/恢复边界，需单独评审，当前不进入产品。逐行递归扫描仍必须按用户显式动作触发，不能在普通导航/刷新时自动扫描。

### 2026-11-02：ExplorerBrowser 对象导航无法保留文件系统包装层

运行现有 `ExplorerBrowserWrapperProbe` 的独立 Private Desktop 验证：`IExplorerBrowser::BrowseToObject` 对包装后的物理文件夹返回 `S_OK`，但 `IFolderView2.GetFolder` 返回的身份不是包装对象，当前路径已解析回原文件系统目录。Private Desktop 前台守卫、输入状态与隔离清理均通过。微软接口说明该方法会从传入对象取得 IDList 并按 PIDL 导航，因此 `BrowseToObject` 成功本身不能证明 Shell 保留了传入的 provider 对象。[IExplorerBrowser::BrowseToObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorerbrowser-browsetoobject)

该结果排除了“把普通文件系统 `IShellFolder` 包一层后直接交给现有 ExplorerBrowser，即可在无注册情况下保留逐行派生列”的简单接法；不排除可解析的虚拟 CLSID namespace 对象导航，也不等同于完整测试自定义 provider。结合上节无 Desktop NameSpace 时应用 parsing name 恢复失败，若要在绿色部署中继续保留 ExplorerBrowser，仍需有 Shell 可解析的注册 namespace；免注册方案更适合转向应用直接承载 `IShellView`、由应用恢复自有位置身份的隔离迁移原型。产品 Host 尚未改变。

### 2026-11-02：volatile CLSID 注册的直接 IShellView 对照

扩展同一 Private Desktop 原型，将随机 CLSID 及 `InprocServer32`、`ShellFolder` 子键创建为 `HKCU\Software\Classes\CLSID` 下的 volatile registry keys；Desktop `NameSpace` 只在 `SHParseDisplayName` 阶段临时存在，解析后即删除。随后由原生 `SHCreateShellFolderView` 创建真实 DefView，`IFolderView2.GetFolder` identity 与直接 provider 一致，列表正确枚举 `file-middle.bin|folder-large|folder-small` 三行。退出清理会验证临时 CLSID 树已删除。Private Desktop 前台、输入及隔离清理守卫通过。

这证明上一节“必须有持久 CLSID 注册”的结论过宽：当前用户会话内的 volatile CLSID 注册即可支持直接 IShellView 创建；COM `CoRegisterClassObject` 单独不能替代该 Shell 类键。volatile key 仍是运行时 HKCU 注册，只是不会写成跨系统重启保留的持久项；本探针没有验证崩溃残留、同会话重启恢复、ExplorerBrowser parsing-name 恢复或产品生命周期。应用若要采用它，需要明确稳定 CLSID、启动前注册与可靠退出清理，并设计异常退出后重建；本结果不等同于无注册表运行。逐行递归扫描仍只允许按用户显式动作触发，产品 ExplorerBrowser 未改。

随后保留 volatile CLSID、完全不创建 Desktop `NameSpace` 项，并单独调用 `SHParseDisplayName("::{CLSID}")`。Shell 返回 `E_INVALIDARG (0x80070057)`，说明 volatile COM 类键可供 `SHCreateShellFolderView` 的直接创建路径使用，却不足以让 Shell 的 parsing-name 路由识别该 namespace 根。此前直接把 path-marker PIDL 当绝对根交给 DefView 的试验创建了 view HWND 后卡在激活/枚举阶段并被 45 秒 Private Desktop watchdog 截止；这不是成功结果，也说明自有 PIDL 必须带正确的 Shell namespace 根身份。Private Desktop 前台和输入守卫未变化。

### 2026-11-02：volatile CLSID 与 Desktop NameSpace 的 ExplorerBrowser 对照

将既有完整 ExplorerBrowser namespace 合同探针改为同时使用 volatile `HKCU\Software\Classes\CLSID`、`InprocServer32`、`ShellFolder` 与 Desktop `NameSpace\{CLSID}` keys。临时 namespace 根成功解析，生产 `ExplorerBrowser` host 创建并保留正确根 PIDL/provider，递归大小列元数据与可见列存在，背景菜单查询成功并读到 21 项；原生删除、重命名、Shell 通知、子目录/嵌套导航、第二浏览器隔离及递归扫描 view 生命周期合同均完成，探针 exit 0。退出时 `temporary-registrations-removed=True`；Private Desktop 前台、输入及隔离清理守卫通过。

这说明对当前 ExplorerBrowser 路线来说，volatile CLSID + volatile Desktop `NameSpace` 可以在当前登录会话内提供完整的 Shell parsing-name 激活，而无需写跨系统重启保留的注册项。它仍会在运行期间写 HKCU，Desktop NameSpace 也可能被其它 Shell 进程看见；本探针没有验证注册与产品多窗口启动/退出竞态、外部 Explorer 同时导航、崩溃后重启恢复或用户可见桌面副作用。用户会话的 `.ini` parsing name 只有在产品启动、重建两类 volatile key 后才能恢复；关闭时主动移除注册则会让其它进程保存的路径在该会话内暂时不可解析。产品及 release 未变，递归扫描仍只允许显式触发。下一步先验证跨进程同会话恢复和注册 owner 生命周期，再决定是否值得进入产品实现。

### 2026-11-02：免注册直接 IShellView 原型门槛

新增隔离 NativeProbe：先在随机 HKCU CLSID/Desktop NameSpace 下解析应用路径 PIDL，随后立即删除这两项注册，再由进程内 NativeAdapter 直接构造 `SyntheticSortFolder`，并将已取得的 PIDL 传给 folder 初始化。Shell folder 对象本身创建成功，但其 `CreateViewObject(IID_IShellView)` 调用 `SHCreateShellFolderView` 返回 `REGDB_E_CLASSNOTREG (0x80040154)`；探针验证两项注册均已删除后才调用，并将此 HRESULT 作为可重复的预期门槛通过。Private Desktop 前台/输入守卫及退出隔离清理通过。

另一个使用物理文件系统 PIDL 的尝试可以创建 DefView 窗口，且 `IFolderView2.GetFolder` 仍返回直接创建的 provider 对象；但视图有 3 个空行，provider `EnumObjects` 计数为 0。该路由不能显示文件系统内容或派生列。两条结果合起来说明：现有 synthetic provider + Windows DefView 无法靠进程内直接 `IShellFolder` 创建同时避开 Shell 注册并正常呈现行。若免注册仍是硬要求，后续需评估不依赖 `SHCreateShellFolderView` 的自有 `IShellView` 实现及其排序、键盘、菜单、文件操作、拖放和通知成本；这属于宿主/视图架构迁移，尚无产品接线或用户体验验收。
