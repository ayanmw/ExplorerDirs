# 逐行文件夹大小列：原生 Shell 视图架构评审

状态：架构评审完成；只授权评审，不授权产品视图迁移。本文件补充[逐行文件夹大小与子项计数列调查](../../folder-size-column-investigation.md)和[IShellView 分阶段迁移门槛](2026-10-25-ishellview-host-migration-gates.md)。

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

2026-10-26 原生 shim 进度：`StaticShellFolderAdapter.cpp` 已由本机 Visual Studio x64 编译器以 `/W4 /WX` 编译成功。隔离探针中适配器的列标题、默认状态、PROPERTYKEY 映射和 `UInt64` 取值均读回成功；DefView 的 `IColumnManager` 也读回新增列及顺序，`IFolderView2` 排序键读回测试 PROPERTYKEY，但实际文件顺序没有变化，`CompareIDs` 的自定义列分支未触发，故“可排序列”门槛仍失败。另一次 DefView 创建使前台窗口改变，探针检测后立即终止；在恢复明确桌面安全条件前，不再运行该宿主探针。CLI 已把该探针锁在显式 `--AllowInteractiveDesktopControl` 参数后，避免误运行。当前证据不足以建议产品接入，仍需解决 Shell 排序和隔离宿主边界。
