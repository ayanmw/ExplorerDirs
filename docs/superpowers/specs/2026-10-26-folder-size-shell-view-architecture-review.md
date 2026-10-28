# 逐行文件夹大小列：原生 Shell 视图架构评审

状态：架构评审完成；用户已授权隔离 `IShellView` 原型验证，不授权产品视图迁移。本文件补充[逐行文件夹大小与子项计数列调查](../../folder-size-column-investigation.md)和[IShellView 分阶段迁移门槛](2026-10-25-ishellview-host-migration-gates.md)。

## 结论

不要为了逐行大小列直接实现自绘文件列表或完整自定义 `IShellView`。最值得继续验证的方向是：**隔离宿主 + 文件系统 `IShellFolder2` 适配层 + Windows DefView（由 `SHCreateShellFolderView` 创建的系统文件视图）**。适配层委托真实文件夹的项目身份、图标、菜单、打开、文件操作和数据对象，只增加本程序的派生列，并为其定义稳定的数值排序。

这条路有机会保留大部分 Windows 文件列表行为，但仍是独立 Shell 浏览器宿主，不是可以直接塞进当前 `ExplorerBrowser` 的列插件。它必须先证明视图、folder adapter 与浏览器服务之间的契约；当前产品继续使用 ExplorerBrowser。`IColumnProvider`、`SetViewProperty` 缓存和自绘 ListView 均不满足目标。

## Q-Dir 目标行为

Q-Dir 官方 [FAQ 63](https://www.softwareok.com/?faq=63&seite=faq-Q-DIR) 描述文件夹大小进入 Size 列、项目数量进入 Type 列，且文件夹大小是否参与排序可配置；多层目录可能需要较长时间计算。目标不只是显示一段文字，还包含递归范围、数值排序、刷新后的值一致性，以及普通文件继续使用原生大小/类型语义。当前项目的 F9 只扫描当前目录并在状态栏汇总，不能代表逐行列能力。

颜色原型和大小列是两个独立需求。逐项着色可使用 `IShellView`/DefView 绘制路径进行研究；大小列要由 Shell folder 暴露列元数据、取值和比较语义。即使它们共用默认 DefView，也不能把颜色像素通过当作新列架构通过。

## 方案比较

| 方案 | 能力与成本 | 评审 |
| --- | --- | --- |
| ExplorerBrowser + `SetViewProperty(System.Size)` | 本地隔离探针能写入视图缓存；Shell 大小排序忽略注入值，`Refresh()` 清除缓存；API 已弃用 | 淘汰。显示、排序和刷新不能保持一致 |
| ExplorerBrowser + `IColumnProvider` 或注册 Property Handler | `IColumnProvider` 是旧式方案；Property Handler 面向已关联文件类型，不是便携地给任意目录增加派生列 | 淘汰。不适用于当前绿色化、任意目录和逐行递归数据 |
| 自定义 folder adapter + Windows DefView | 用 `IShellFolder2` 的列元数据/取值接口暴露新增列；委托项目 PIDL 与 Shell 操作；可利用系统 DefView 的标准列表交互 | 推荐做隔离原型。需自行提供 adapter PIDL/导航和 `IShellBrowser` 宿主，COM 委托与异步派生值风险仍高 |
| 完全自定义 `IShellView` / WPF 列表 | 列和排序可完全控制 | 不推荐为单列投入；需自行重做 Shell 菜单、快捷键、选择、拖放、属性、实时通知和无障碍 |
| 暂缓逐行列，保留 F9 与状态栏 | 不增加列能力 | 当前产品默认。直到原型证明 Shell 行为可保留，不改变 ExplorerBrowser |

Microsoft 文档说明，`IShellFolder2` 为文件夹视图提供项目列详情与属性键映射；Shell 也提供默认 DefView，namespace extension 可用它而不自行重做整个视图。Microsoft 同时指出，自定义 `IShellView` 会要求扩展自行实现视图，且部分视图能力只由默认 Shell view 提供。[IShellFolder2 及列详情](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellfolder2)、[Shell namespace 与 DefView](https://learn.microsoft.com/en-us/windows/win32/shell/nse-works)、[Shell interfaces](https://learn.microsoft.com/en-us/windows/win32/shell/interfaces)。

## 建议原型边界

1. 单独的 NativeProbe/原型进程、单窗格、普通本地文件系统目录；不替换产品 `ExplorerBrowserHost`，不读取或写入日常 profile、release 或用户 Q-Dir 配置。
2. 先验证标准 DefView 能从 adapter 读取两列：现有原生列 + 一个简单的静态数值列；确认 `GetDetailsOf(NULL, index)` 的列标题/索引、`MapColumnToSCID` 的属性键映射、`GetDetailsEx` 返回值的明确类型、`GetDefaultColumnState` 的默认显示状态，以及 `IColumnManager` 的列可见性/顺序读回。数值比较必须在 `CompareIDs` 收到新增列索引时用数字比较；用 `2, 10, 100` 等会令字符串排序出错的静态样本分别验证升序、降序和相等值。该阶段只证明 DefView 的列合同，不做递归扫描或真实文件夹大小计算。[Microsoft `IShellFolder2::GetDetailsEx`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdetailsex)、[`GetDefaultColumn`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdefaultcolumn)、[`GetDefaultColumnState`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdefaultcolumnstate)、[`IShellFolder::CompareIDs`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids)。
3. 再验证真实文件/文件夹 PIDL 委托：图标、打开、属性、文件项和背景菜单、复制/粘贴/删除/重命名、剪贴板、拖放均仍作用于原始文件系统项目。
4. 最后才加入受控递归计算及缓存。递归不能在 Shell 列回调或 UI STA 上同步执行；需要显式启动、取消、进度、失败/权限不全表示、缓存身份、文件变化失效、刷新与排序一致性。当前 `AGENTS.md` 禁止为填补属性缺失而递归计算文件夹大小；实现递归列前必须先由用户审阅并更新该约束。
5. 再分别覆盖多层目录、重命名/移动/删除后列值失效、隐藏文件策略、重解析点循环、网络/脱机目录和高项目数。第一阶段只声称本地普通文件夹支持。

## 迁移硬门槛

隔离原型要进入产品评估，至少需要以下证据，且任何一个缺口都不以“DefView 是 Windows 原生视图”代替：

- **身份与导航：**真实绝对 PIDL/相对 PIDL 往返、深层导航、上级、Back/Forward、无效目标保留旧视图、跨标签/布局生命周期与 COM 引用释放。
- **菜单及命令：**文件、文件夹、背景与列标题菜单；canonical verb、Properties、New、排序/分组和刷新；第三方 Shell 扩展不得被 WPF 列表替代。
- **键盘与焦点：**Ctrl+A/C/X/V/Z、F2 重命名和方向键、列标题 Tab 停靠、四窗格 Tab/Shift+Tab、文件操作或确认框后的真实焦点与选区。
- **视图状态：**八种查看方式及 Details 列、排序/分组、列宽/次序、每标签状态、复制标签、刷新和跨重启恢复；新列的 `PROPERTYKEY`、排序和 `.qdr` 导入导出语义独立记录。
- **文件操作与变化：**复制、移动、删除/回收站、创建/重命名、撤销、冲突/取消；watcher、Shell 通知、刷新及选区/焦点恢复；大目录响应性和多窗格竞争。
- **Shell provider 与拖放：**虚拟位置、搜索、压缩容器、网络位置、剪贴板 Shell data object、真实 OLE source/target 效果。未支持的 provider 要可识别并保持稳定。
- **可见验收：**与当前 ExplorerBrowser 用同一用例逐项对照菜单、鼠标、真实键盘、焦点、拖放、更新、性能及高对比度；隐藏探针只作局部证据。

## 产品决策

- 评审结果是**值得做隔离 DefView + `IShellFolder2` adapter 原型**，不是批准迁移产品。
- 不采用“双宿主按位置静默切换”作为产品方案；同一个标签不能因位置或布局不同而改变菜单、历史、键盘、列或文件操作语义。
- 如果 adapter 无法同时提供可排序数值列和可委托的原生 Shell 项目操作，就保留 ExplorerBrowser + F9 汇总，不用只显示但不可排序/刷新错误的大小列冒充完成。
- 原型评估递归列前，先确认产品策略：逐行大小列是否视为用户显式请求、是否只在 Details 中按需启用，以及网络/权限不全时如何显示。现有“不要自动递归计算”的约束继续有效。

## 评审后下一步

2026-10-26 已验证静态数值列策略本身（`2, 10, 10, 100` 的数值比较、格式化和相等值次序）；这只是纯托管策略 smoke，不是 Shell 列接线证据。随后构造隔离 `IShellFolder2` 适配器：真实文件夹 PIDL 可解析，基础列枚举和静态列元数据/值调用可由适配器处理，但 `SHCreateShellFolderView` 拒绝了托管 COM 包装对象，返回 `E_NOINTERFACE`。因此 DefView 尚未创建，列显示、`IColumnManager` 读回、DefView 排序和普通 Shell 操作均未验证。

结论：当前托管 CCW 适配器不能证明此架构可行。若继续，下一步应先做一个极小的原生 COM `IShellFolder`/`IShellFolder2` shim（可由 C++ 实现），通过 `QueryInterface` 将未实现接口委托给真实文件系统文件夹，并仅在隔离夹具中重复同一静态列门槛；然后再考虑浏览器服务、PIDL 操作和文件变更通知。该原生 shim 仍只是架构实验，不授权产品迁移，也不加入递归扫描。失败的托管适配器源文件和运行入口已撤下，保留策略 smoke 与本评审结论，避免留下一个被误认为可工作的原型。

2026-10-26 原生 shim 进度：`StaticShellFolderAdapter.cpp` 已由本机 Visual Studio x64 编译器以 `/W4 /WX` 编译成功。隔离探针读回适配器的列标题、默认状态、PROPERTYKEY 映射和 `UInt64` 值；DefView 的 `IColumnManager` 读回新增列及顺序，`IFolderView2` 也读回测试 PROPERTYKEY 作为排序键。无窗口 COM 直测进一步确认 `CompareIDs` 对 `2/10/100` 及相等值返回正确的数字顺序，因此比较器实现通过。原生验证确认 DefView 的 `GetFolder` 返回同一适配器身份，但实际文件顺序没有变化，适配器的 `CompareIDs` 未被调用，Shell 的 `GetDetailsEx` 计数也只有探针预检的一次，故新增列既未参与项目排序，也未被 DefView 取值。Private Desktop 上无激活地显示父窗口仍未触发列取值或排序，且前台监测保持不变。加入 `IShellView.UIActivate(SVUIA_ACTIVATE_NOFOCUS)` 后，外层前台监测报告变化；该激活调用现已从探针移除，后续不得在用户桌面上执行。当前证据不足以建议产品接入，排序门槛失败，需查明 DefView 激活/求值前置条件及安全隔离方式。

2026-10-27 无窗口 adapter 合同补强：`--static-shell-column-folder-smoke` 现在在创建任何窗口前，直接验证 `GetDefaultColumnState`、`MapColumnToSCID`、标题、四个 `GetDetailsOf` 文本、四个明确为 `VT_UI8` 的 `GetDetailsEx` 值以及数字比较。NativeProbe Release build 0 警告、0 错误，运行输出 `state=0x12; title-and-four-values=valid; variant=UInt64; compare-2-10=-1; compare-10-100=-1; compare-equal=0; compare-100-2=1; window-created=False`。这确认 shim 的静态 folder contract，不改变 DefView 未读取该列的结论；下一步仍是安全且可重复地触发/观察 DefView 求值，不接入产品。

2026-10-27 属性 schema 调查：当前静态列使用临时自定义 PROPERTYKEY，但没有相应 `.propdesc`；微软 Explorer Data Provider 样例随自定义属性提供 property description，定义属性类型/标签。`PSRegisterPropertySchema` 官方契约称注册机器级 schema 且应在安装期调用。由此提出待验证假设：DefView 不读取该自定义键可能与其无已注册属性描述有关。为维持便携方向，不在开发机注册 schema；下一次隔离实验改用系统已有 `PKEY_Size`，使用静态值查看 DefView 是否读取并排序该标准列。此实验若成功，只证明标准 Size 路径可用，不证明任意新自定义属性可免注册扩展。

随后无窗口合同已扩展为标准 `PKEY_Size`：shim 在现有 Size 列的 `GetDetailsOf`/`GetDetailsEx` 和列索引 1 的 `CompareIDs` 返回 `2/10/10/100`；无窗口 probe 验证 `UInt64`、显示文本及数值比较均通过。不将直接接口调用结果当作 Shell 排序结果。后续 Private Desktop 探针输出区分 `GetDetailsOf`（逐项文本）、`GetDetailsEx`（属性值）及 `CompareIDs`（列比较）三条回调；旧版日志没有对标准 Size 项目的 `GetDetailsOf` 单独计数，故此前关于该调用路径的证据不完整。

## `IPropertyStore` 路径边界

2026-10-27 接口契约复核：微软将 `GetDetailsOf` 明确描述为 Explorer Details 视图的列信息接口，`GetDetailsEx` 提供同一视图信息且以 `FMTID/PID` 标识列；`GetUIObjectOf` 的职责是取得对项目执行 Shell 操作的对象，常见 IID 包括 `IContextMenu`、`IDataObject`、`IDropTarget` 和 `IExtractIcon`。`IShellItem2::GetPropertyStore` 则是 Shell item 自身取得属性存储的独立入口。[GetDetailsOf](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdetailsof)、[GetDetailsEx](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder2-getdetailsex)、[GetUIObjectOf](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getuiobjectof)、[GetPropertyStore](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem2-getpropertystore)。

这不能证明 DefView 从不通过属性系统读取 Size，但不支持把 adapter 的 `GetUIObjectOf(IPropertyStore)` 当成已知接线点。下一步先在安全隔离的视图执行时分别观察 `GetDetailsOf`、`GetDetailsEx`、`CompareIDs`；只有观察到额外的 Shell item 属性存储请求后，才评估如何拦截且保持原项目身份。`IPropertyStore` 装饰器目前不是已批准或已验证的实现路线，也不加入产品代码。

2026-10-27 Private Desktop 未激活视图复验：新的标准 Size 逐项目 `GetDetailsOf` 计数已加入原生 shim，并在隔离桌面上运行 `--static-shell-column-adapter-private-desktop`。DefView 保留 adapter（`view-uses-adapter=1`），`SetSortColumns` 成功且属性键/方向读回正确；但决定当前顺序时 `CompareIDs=0`、Size `GetDetailsOf` 项目计数为 0、Size `GetDetailsEx=0`，实际顺序仍是 `100,10,10,2`，升序门槛失败。探针未激活该 `IShellView`，这是为避免之前 `UIActivate(SVUIA_ACTIVATE_NOFOCUS)` 曾使外层前台窗口变化；本次前台保持不变。因此该结果只证明未激活视图不消费这些逐项回调，不能外推到活动 DefView。不得在用户桌面重试激活；需要先找到可验证且不触碰用户前台的激活实验，产品仍不接入。

2026-10-27 未激活 DefView 原生文本读取复验：在同一 Private Desktop 探针中，通过 `IShellView::GetWindow` 找到标准 `SysListView32`，用 `LVM_GETITEMTEXTW` 直接读取名称、Size 和新增列三个 subitem。四个名称和标准 Size 均由列表控件返回（测试夹具文件长度相同，显示均为 `1 KB`），新增 subitem 四行均为空；读取前台保持不变。读取后 shim 的比较回调仍为 0，逐项 `GetDetailsOf`、`GetDetailsEx` 也未增加（仅保留标题/合同预检计数）。随后升序请求仍读回原顺序 `100,10,10,2`，探针按未满足排序门槛退出。该证据排除了“整个未激活列表连名称文本也无法读取”，但不能区分 DefView 自身忽略新增属性、列元数据/schema 不足或需要其它 Shell 属性通路；激活与属性存储假设均未证实，不得据此改产品视图。

2026-10-27 `IPropertyStore` 请求路径计数：原生 shim 现分别记录对适配器 `QueryInterface(IPropertyStore)`、`BindToObject`、`BindToStorage`、`GetUIObjectOf` 的请求。重建 NativeProbe（C++ `/W4 /WX`、C# Release 均 0 警告/错误）后重跑 Private Desktop 探针，这四类计数全为 0；前台保护通过，未激活视图的排序门槛仍按预期失败。结论仅排除通过这四个 `IShellFolder` 适配器入口直接提供属性存储的简单接法；Shell 若从原始文件系统 `IShellItem` 取属性不会经过这些计数。暂不实现属性存储装饰器，后续应先验证列 schema / 原始 Shell item 属性路径，且不得在日常系统注册机器级 property schema。

2026-10-27 属性描述缓存直读：在无窗口合同探针中调用 Windows `PSGetPropertyDescription`，原型 `PROPERTYKEY` 返回 `TYPE_E_ELEMENTNOTFOUND`（`0x8002802B`），标准 `System.Size` 返回 `S_OK`。因此当前原型键确实没有已加载的 property description schema；这与新增列空白相符，但不证明 DefView 的 `GetDetailsOf` 路径一定需要该 schema，也不证明注册后排序能工作。Microsoft 契约说明该 HRESULT 表示属性键不存在于 schema subsystem cache；不在日常系统注册机器级 schema。[PSGetPropertyDescription](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psgetpropertydescription)。

2026-10-27 同视图 schema 控制列：适配器再暴露一个 `System.DateCreated` 测试列。无窗口检查确认其 `PSGetPropertyDescription` 为 `S_OK`，未登记的静态键仍为 `TYPE_E_ELEMENTNOTFOUND`。在同一个未激活 Private Desktop DefView 中，标准名称、Size 和额外 DateCreated 子项均有文本（DateCreated 读取到测试文件时间），而紧邻的未登记自定义列仍为空；前台保护通过。四类适配器 `IPropertyStore` 请求仍为 0，故已显示的系统列值没有通过被包装的适配器请求。两列之间最直接的可观测差别是 schema 是否存在，这强烈支持缺失描述是自定义列空白的原因，但尚未证明注册自定义 schema 后 DefView 会调用其排序/取值回调。当前环境未发现 `WindowsSandbox.exe`；DISM 检查可选功能因需提升权限未执行，因此还没有可验证的 disposable Windows 环境。不得在日常系统注册；产品迁移仍需用户评审。

2026-10-27 `IShellView::Refresh` 负向控制：在 Private Desktop 的同一静态列探针中，成功应用并读回 `IColumnManager` 列配置后立即调用 view vtable slot 8（`IShellView::Refresh`），HRESULT 为 `S_OK`，等待 Shell 消息处理后再读 `SysListView32` 子项与排序结果。自定义列四行仍为空；升序请求后的实际项次序仍为 `100,10,10,2`，适配器排序/取值回调仍未出现，前台保护通过。因此列刷新不足以触发未登记属性列的取值或排序；此结果不能区分缺失 schema、DefView 列支持限制或其它 Shell 属性路径。该 Refresh 检查保留在隔离探针中作为可重复负向控制，不改变产品视图。

Property Schema 方向已由 Microsoft API 契约排除为无机器状态的运行时试验：`PSRegisterPropertySchema` 仅应在 schema 文件首次安装时调用，schema 是机器级，且注册需要对 `HKEY_LOCAL_MACHINE` 的写权限；`PSUnregisterPropertySchema` 同样需要机器级权限并属于卸载流程。因此不在日常开发系统注册/注销临时 schema，除非后续提供可丢弃 Windows VM。参见 [PSRegisterPropertySchema](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psregisterpropertyschema) 与 [PSUnregisterPropertySchema](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psunregisterpropertyschema)。

当前工作会话复核：静态 folder contract smoke 再次通过，输出仍明确 `window-created=False`，只证明适配器合同，不证明 DefView 实际读取或排序列。当前 PowerShell 未发现 `Get-VM`、`vmconnect.exe`、`VBoxManage.exe` 或 `vmrun.exe` 命令；这只说明当前会话没有可直接使用的 VM 管理工具，不据此推断主机没有虚拟化或其他外部隔离环境。遵循共享桌面约束，本轮没有启动新的 Shell 宿主。继续原型前需提供/准备独立 Windows 会话或 VM；不得在本机日常系统注册机器级 property schema。

2026-10-27 隔离环境复核：本会话 `Win32_ComputerSystem.HypervisorPresent=True`，但 `WindowsSandbox.exe`、Hyper-V PowerShell 模块、`Get-VM`、`vmconnect.exe`、`VBoxManage.exe`、`vmrun.exe` 均不可用；WSL 仅列出 Ubuntu 24.04，不是可验证 Windows DefView/机器级 schema 的 Windows VM。此结果只说明当前会话没有发现可操作的隔离 Windows 环境，不排除主机外部或其它账号已准备的 VM；未启动任何虚拟机或 Shell 宿主。
