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

### 2026-10-28 FAQ 82 控制入口与百分比说明

重新打开 Q-Dir 官方 [FAQ 82](https://www.softwareok.com/?faq=82&seite=faq-Q-DIR) 核对当前文字：逐行 Size 信息可以关闭/开启，入口写作 Extras → Size information 或 F9；文件及文件夹大小的下划线百分比被说明为同一目录内大小的相对百分比。该页未定义分母公式、文件/目录是否分开归一、隐藏项及扫描失败如何参与、舍入方式。故只确认“相对同目录项目”这一层语义，不据此实现百分比计算。

Explorer Dirs 的 F9 已由用户选定为显式递归汇总命令，输出整棵目录子树的状态栏统计；保留该行为，不将其静默改为 Q-Dir 的逐行信息开关。该记录确认两者当前命令语义不同；逐行功能仍须先通过新的 Shell 视图架构与性能门槛，不改变 ExplorerBrowser 产品路径。

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

2026-10-30 支持路径候选复核：将下一项隔离验证从 legacy `IShellFolderView::RefreshObject` 改为 Shell 变更通知 `SHCNE_UPDATEITEM`。Microsoft 将其定义为既有 Shell 项目的变更事件，并说明接收端通知可合并；公开合同没有说明 DefView 会因此重取扩展列、重新排序或保留选区。源码审查还发现产品已有文件系统项更新通知，并在排序/分组依赖变更元数据时补发受影响目录通知；这只是虚拟 namespace 通知实验的设计参考，不证明其结果相同。故下一次应比较单项、单项加父目录及整视图刷新，当前没有产品实现或虚拟 view 行为结论。

2026-10-30 清理链源码复核：递归大小分支在断言后直接返回，进入 `finally` 时先释放 `IFolderView2`，再调用 `IShellView::DestroyViewWindow`；这让视图子窗口销毁发生在其 view/browser-site COM 引用部分释放之后。现已把两个 `DestroyViewWindow` 调用移到 `IFolderView2`、view、文件夹和 browser-site 释放之前，并在子窗口回收后再释放合成对象引用、宿主与顶层 parent。该顺序符合“先销毁 Shell view window，再释放宿主依赖”的生命周期要求，但仍只是原型代码的修正假设，不证明此前 COM STA 等待因此消失。NativeProbe Release 编译、原生适配器 `/W4 /WX` 编译、无窗口 DLL unload contract 和 synthetic folder tree smoke 均通过；本轮没有运行 Private Desktop view，也没有卡死现场复测。下一步仍需在桌面边界稳定时运行带外部 watchdog/minidump 的递归大小对照，并检查是否越过 `cleanup-destroy-view-window-1` 阶段。产品和 release 均未改。

2026-10-30：A/B 输出增加通知到视图状态收敛的耗时，以及 `EnumObjects` 调用/行数、逐项大小列读取、自定义属性读取和大小比较次数，便于区分重枚举与局部刷新。注意这些计数覆盖探针通过 ListView 读取结果时产生的属性请求；它们用于同一探针内的相对比较，不代表纯 Shell 刷新开销。Release 构建、无窗口 folder-tree smoke、DLL unload contract 和 `git diff --check` 通过；本轮仍未启动真实 Shell view A/B。

2026-10-30：修正上条 A/B 计数的观察者干扰：探针读取 ListView 文本时，用仅作用于当前 STA 线程的标记屏蔽同步回调产生的枚举、大小属性和比较计数；消息泵期间仍正常累计 Shell 更新工作。无窗口 folder-tree smoke 直接调用 `GetDetailsOf`、`GetDetailsEx`、`CompareIDs`、`EnumObjects` 和枚举器 `Next`，确认观察标记开启时这些查询不会污染计数；输出 `metric-observer-exclusion-passed`。原生适配器 `/W4 /WX`、NativeProbe Release、folder-tree smoke、DLL unload contract 均通过。该合同证明当前线程的屏蔽逻辑，不代表异线程 Shell 调用行为；真实通知 A/B 尚未运行。

2026-10-30：首次完成真实私有桌面 `IShellView` 通知 A/B；三种策略分三个全新进程、各自从递归大小 `10`/文件大小 `4` 的干净视图开始，避免前一通知改变后一策略的状态。`SHCNE_UPDATEITEM` 与 `SHCNE_UPDATEITEM + SHCNE_UPDATEDIR` 各产生 1 次 `EnumObjects`（2 行）和 1 次大小比较；两者排序都变为 `small.bin|child`，选区/焦点保留，但大小列不可见，`GetDetailsOf`/自定义 `GetDetailsEx` 读取均为 0。`IColumnManager` 的刷新后列清单仅有 Name、类型类列和 Type，不含 `System.Size` 或递归大小属性。整视图 `Refresh` 产生 2 次枚举（4 行）、2 次比较，同样没有大小列或属性读取；排序正确，但选区/焦点在 2.5 秒等待后仍未恢复。独立日志：`src/.state/native-probe-watchdog/20261030-032905-8e6c6f4f.stdout.log`、`20261030-032911-11d351dd.stdout.log`、`20261030-032921-04ed2549.stdout.log`。三轮 Private Desktop 前台句柄和输入 tick 均未变，watchdog 均退出码 0；临时 CLSID/NameSpace 注册、夹具目录和 NativeProbe 进程均清理。该结果说明当前自有合成 `IShellView`/DefView 通知路径会重新枚举并失去大小列，不能显示为成功的逐项列值刷新；不外推为 ExplorerBrowser 产品行为。

随后在单项更新后调用 `ShellColumnManager.Apply` 重放更新前捕获的列集：调用返回 `S_OK`，但 `IColumnManager.Capture` 仍只读回 Name、类型类列和 Type，行中没有目录大小值；恢复阶段没有引发枚举、大小属性读取或比较。因此成功 HRESULT 不能当作 DefView 已恢复列配置的证据。两次隔离复跑都在 `cleanup-destroy-parent-window-1` 超过 30 秒内部期限，wait-chain 指向 Shell/COM activation 与 `ChsIME`；第二次启用探针专用的“销毁父窗前清焦点”开关后仍相同。两次用户桌面前台 HWND 与最后输入 tick 未变；外层 watchdog 退出探针并清除了临时 CLSID/NameSpace 注册及夹具。该挂起发生在列恢复读回完成之后，尚不能证明由 `Apply` 导致，也不能声称恢复路径通过完整生命周期。恢复列的临时诊断代码已撤回。下一步应将列配置丢失视为 DefView 通知刷新行为的限制，先以无视图合同/静态接口分析确认可维护自定义列状态的受支持方案；在隔离桌面父窗清理 wait-chain 有进展前，不继续扩展同一销毁路径的运行测试。产品与 release 未改。
