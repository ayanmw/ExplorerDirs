# 逐行文件夹大小与子项计数列调查

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

本项目现有 F9 只汇总当前目录整棵子树的总字节数、文件数和子目录数到状态栏，**不等同于 Q-Dir 的逐行列和 F9 数据大小信息**，也不提供每个可见子目录的大小/数量、百分比或排序设置。Q-Dir 官方 [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 说明文件夹大小下划线百分比相对于同一目录中的文件/文件夹，而不是磁盘容量比例；但公开文字没有定义具体分母、是否包含普通文件及未完成/失败项目。下一次可见互操作仍需确认 Size/Type 两列显示、计算未完成标记、精确百分比公式、排序切换，以及“无焦点时暂停”如何作用于四窗格；默认排序设置也仍未知。

因此，后续原型验收需要同时覆盖逐项值与交互策略：Size 列复用、Type 列复用、单位/精度、百分比、数量、未完成/失败状态、排序忽略开关、失焦暂停和多个窗格并发。目录扫描必须按需、可取消，不能在导航时对全部子目录递归扫描；任何缺值不能伪装成 0。当前隔离 adapter 尚未读取逐项属性，排序门槛未通过，不能开始产品接线。

本项目目前的 F9/状态栏扫描只汇总当前目录的总字节数、文件数和子目录数，尚未提供逐行数据。此前 `SetViewProperty(System.Size)` spike 证明该视图缓存不会参与本机 Shell 大小排序且刷新会清除，不能满足可排序列。公开资料补充了 Q-Dir 的目标行为，但没有改变 ExplorerBrowser 对任意新列缺少受支持扩展接口的结论；也没有新增视图实现方案或启动 Shell 探针。

### 2026-10-27 百分比语义补充

Q-Dir [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 将 Size 列中文件夹大小的下划线百分比说明为同一目录中项目之间的相对比例。因此它不是磁盘空间使用率，不能复用状态栏的“已用容量百分比”。FAQ 没有明确定义计算分母、文件与文件夹是否分别参与、递归未完成时如何处理；这些细节仍需对照 Q-Dir 可见行为，不能据此臆造公式。该资料只更新需求语义，不改变目前 ExplorerBrowser/DefView 的新列架构门槛。

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