# Everything 文件夹大小数据源

## 接入边界

temp.86 目标为默认显示、可关闭的文件夹大小列，提供 Auto / Everything / 内置扫描三种来源，并复用到独立空间分析。当前本文件记录数据源阶段，不表示列表或设置已接入。

EverythingSizeClient 使用官方 SDK 文档公开的 IPC QUERY2W 协议，批量查询指定目录直接子项，不打开 Everything 搜索窗口、不更改外部配置。没有导入 Everything 程序，也没有新增 SDK DLL 运行依赖。

官方资料：https://www.voidtools.com/support/everything/sdk/ 及 https://www.voidtools.com/support/everything/sdk/ipc/ 。2026-11-02 下载官方 Everything-SDK.zip 用于接口核对，SHA256 F5716D9513CCE6B462B5170A0A2E7E081E191D9BCAC6774F5DECC061497DF443；SDK C 源和公开头部附 MIT 许可。下载材料仅在 src/.state/everything-sdk 调试目录，不随产品打包。IPC 字段偏移依据该包 ipc/everything_ipc.h。

## 已实现合同

- 查询前检查服务窗口、索引就绪和目录大小索引能力；无运行实例、索引不可用、超时、错误报文分别返回状态。
- 只创建 HWND_MESSAGE 接收窗口；所有工作在后台 STA，最多两条并行查询。发送使用 SendMessageTimeout，回包等待 1.5 秒，等待期间检查取消；排队取消不创建窗口。
- 每页最多 2048 项，返回总数与 offset 以支持后续分页；请求完整路径和逻辑大小，负值保留为未知，不冒充 0。
- 回包检查大小上限、计数、偏移、字段、UTF-16、终止符及路径；查询结果再校验每项确为请求目录的直接子项。

## 当前证据

- 本机 Everything 1.4.1.1032 正在运行。只读查询项目目录：Available，13 行，13 项大小可用，约 430 ms；前台不变，外部配置不变。
- --everything-size-protocol-smoke：中文路径、0 与未知值区分、报文截断/坏偏移/计数/字段、预取消通过，无窗口/IPC。
- --everything-size-live-fixture：新建两个临时目录，分别含 5 B / 37 B 文件，真实 Everything 查询读回两目录正确汇总，约 734 ms；前台不变，夹具清理成功。
- --navigation-chrome-smoke：修正旧检查对电脑菜单构造方式的假设后通过图标/可访问名称与焦点遍历合同。未进行可见 UI 验收。

## 待完成

2026-11-02 native 回调桥已接入 App 的 profile owner：后台 provider worker 先请求 Everything，Auto 缺值返回 native 扫描后备；Everything 专用缺值缓存为不可用。快照独立记录大小/计数是否可用，未知大小以空属性/破折号表示，未知计数不伪造零。来源切换取消旧回调并清空快照、失效在途 reservation；Shell 路径失效同步移除 managed 父目录批次缓存。managed 缓存最多四个第一页（8,192 行）、2 秒复用，超出第一页的目标使用精确路径查询。native 读写锁保证注销返回前已进入的回调退出，delegate 生命周期由 owner 保持。

--folder-size-native-bridge-smoke 使用真实 C++ adapter 与可控数据源，无 Shell view/IPC/注册表：父目录批次共享、子路径失效、未知与零区别、Auto native 后备、Builtin 不联系索引、native 取消、切换来源退休等待中的回调、注销后恢复原生扫描、快照可用状态和旧 reservation 拒绝发布均通过。既有 --synthetic-shell-folder-tree-smoke 通过，包括真实文件操作和缓存失效合同，明确 shell-view-created=False。C++ product/native-probe `/W4 /WX` 与 App/NativeProbe Release build 通过。真实 Everything 5 B/37 B 夹具增加精确路径查询读回验证通过，整轮约 1.8 秒，前台不变。

已实现 FolderSizeDataSource 的来源选择与分页消费：Everything 专用模式缺值保留未知，Auto 只扫描索引缺值目录，BuiltIn 不联系 Everything；内置扫描全局最多两个 worker，按批次调度而非每目录创建线程。不额外复制 Everything 索引和消费者已有的缓存。FolderSizeOptionsStore 默认 Enabled=True、Source=Auto，保存于独立 INI，支持冲突、未来格式及损坏文件保护。

--folder-size-data-source-smoke 验证来源路由、真实零与缺值区分、自动模式限制扫描范围、Everything 不可用后备、取消、选项重载/冲突/未来格式保护；通过，无 IPC/UI。App Release build 0 warning/error。

来源设置菜单和空间分析已接入，见 space-analysis-temp86.md；普通列表默认大小列/视图开关、关闭后停止请求、F5 主动失效、来源切换后的既有行更新仍未完成。尚未覆盖真实 Everything 未运行/关闭目录索引、查询中服务退出、挂起服务和多页目录；当前测试不能替代这些运行期边界。

2026-11-02 后续后台接线：ConfigureFolderSizeProvider 增加 enabled 状态，关闭时 ScheduleRecursiveSizeSnapshot 不入队，scanner 在条目边界检查开关及来源 generation；快速关闭再开启也会取消旧 generation 的扫描。App 启动读取 Enabled/Source，bridge 和 owner 保持开关，注销恢复默认 native 行为。InvalidateFolderSizePath 导出连接 F5，清理当前路径的子树与祖先快照，并同步失效 managed 批次。来源切换刷新已创建的 provider 视图；不创建隐藏标签宿主。

2026-11-02 普通目录接入前的标准列补全：此前 provider 只暴露三列（扩展模式四列），会丢失现有修改时间等列，因此增加对 backing IShellFolder2 的标准列映射、列状态、显示文本、属性和排序转发。排除已提供的 Name/Size/Type 重复键，按 provider 实例缓存最多 1,024 个 backing ordinal，COM 调用不持有 map mutex；STRRET_OFFSET 在临时 PIDL 释放前转为独立字符串。普通类型文本也读取原生属性，而不统一显示 File。

VerifyBackingColumnMetadataForProbe 在真实文件夹中确认修改/创建/访问时间列存在、标题和值可读；两个已设置不同修改时间的文件通过 provider 日期 CompareIDs 得到正确次序，GetDetailsEx 可读回日期属性。该检查已纳入 native bridge smoke，无 Shell view/注册。既有 folder-tree smoke 通过。标准列排序当前需重新解析 backing 子 PIDL，尚未测量大列表性能；未运行真实 IColumnManager 列恢复或可见 UI，不能宣称透明迁移完成。

2026-11-02 身份策略准备：FolderSizeLocationPolicy 区分用户物理路径与 Shell 实际 provider identity，只对 Shell 已确认的普通目录进行 provider 路由；ZIP stream、虚拟位置及关闭时的普通目录保持原路由，已有 provider history 不因关列而换身份。选择恢复仅允许物理直接子项，并将其绑定到当前 native parent。QdrShellPathCodec 补齐 encoded-root 后的真实子项段解码；此前把整串当十六进制，遇到 nested provider identity 会失败。新合同覆盖中文/目录名 .zip、真正 ZIP stream、嵌套路径、关闭/历史、直接子项及非法相对段，无 Shell/IO。策略尚未连接 ExplorerBrowserHost 的导航与选择流程，不代表普通目录已经接入。

新版 --folder-size-native-bridge-smoke 通过关闭/重开无额外查询、显式刷新失效、在延迟扫描开始后快速关开仍返回取消等合同。App/native C++ 构建通过。视图菜单 checkbox、普通目录的透明 provider 接入仍未完成；provider 身份会影响现有选区 PIDL、历史与监视逻辑，不能只替换 AddTab 的路径就宣布接入完成。后续需统一身份展示及操作路由，并验证 ZIP/虚拟位置、选择和历史不回归。
