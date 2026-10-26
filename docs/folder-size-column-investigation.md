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

复查 Q-Dir 官方 [FAQ 63](https://www.softwareok.com/?faq=63&seite=faq-Q-DIR) 后确认，逐行信息不止一个递归大小字段：Q-Dir 可把文件夹总大小显示在 Size 列，并把文件/目录数量（包括子目录中的项目）显示在 Type 列。官方说明通过 F9 的 Data Size Information 配置这类信息，也提供是否忽略文件夹大小参与排序的选项。后续若实现，需覆盖用户可选择的排序语义，不能把“文件夹大小永不参与排序”当作完整验收条件。

本项目目前的 F9/状态栏扫描只汇总当前目录的总字节数、文件数和子目录数，尚未提供逐行数据。此前 `SetViewProperty(System.Size)` spike 证明该视图缓存不会参与本机 Shell 大小排序且刷新会清除，不能满足可排序列。公开资料补充了 Q-Dir 的目标行为，但没有改变 ExplorerBrowser 对任意新列缺少受支持扩展接口的结论；也没有新增视图实现方案或启动 Shell 探针。

### 2026-10-26 原生 Shell 视图架构评审

用户选择先做原生 `IShellView` 架构评审。评审推荐隔离验证 `IShellFolder2` folder adapter + Windows DefView，而不是自绘列表或重写完整 `IShellView`；这仍需独立 `IShellBrowser` 宿主，不可作为 ExplorerBrowser 的列插件。评审范围、比较、门槛及递归计算策略冲突见[架构评审](superpowers/specs/2026-10-26-folder-size-shell-view-architecture-review.md)。未修改产品代码、未运行 Shell 宿主或 GUI。
